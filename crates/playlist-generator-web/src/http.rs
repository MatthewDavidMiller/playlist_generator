//! Just enough HTTP/1.1 for one browser talking to one local server.
//!
//! Every connection carries exactly one request and is closed after the
//! response, so there is no keep-alive, pipelining, or chunked encoding to get
//! wrong. Heads and bodies are bounded before they are buffered, and anything
//! outside that subset is refused rather than guessed at.

use std::fmt::Write as _;
use std::io::{self, BufRead, BufReader, Read, Write};

/// Largest request line plus headers that is read.
pub const MAX_HEAD: usize = 16 * 1024;
/// Largest form body that is read. The forms hold a handful of paths.
pub const MAX_BODY: usize = 64 * 1024;

/// Nothing is loaded from anywhere but this server, nothing runs as script,
/// and forms only post back to it.
const CONTENT_SECURITY_POLICY: &str = "default-src 'none'; style-src 'self'; \
     form-action 'self'; frame-ancestors 'none'; base-uri 'none'";

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Method {
    Get,
    Post,
}

#[derive(Debug)]
pub enum ParseError {
    /// The connection closed or timed out before a whole request arrived.
    Io,
    Malformed,
    TooLarge,
    /// A method this server never serves.
    Method,
}

impl From<io::Error> for ParseError {
    fn from(_: io::Error) -> Self {
        Self::Io
    }
}

#[derive(Clone, Debug)]
pub struct Request {
    pub method: Method,
    pub path: String,
    pub query: Vec<(String, String)>,
    /// Header names are lowercased.
    pub headers: Vec<(String, String)>,
    pub form: Vec<(String, String)>,
}

fn lookup<'a>(pairs: &'a [(String, String)], name: &str) -> Option<&'a str> {
    pairs
        .iter()
        .find(|(key, _)| key == name)
        .map(|(_, value)| value.as_str())
}

impl Request {
    pub fn new(method: Method, path: &str) -> Self {
        Self {
            method,
            path: path.to_owned(),
            query: Vec::new(),
            headers: Vec::new(),
            form: Vec::new(),
        }
    }

    pub fn header(&self, name: &str) -> Option<&str> {
        lookup(&self.headers, name)
    }

    pub fn query(&self, name: &str) -> Option<&str> {
        lookup(&self.query, name)
    }

    pub fn field(&self, name: &str) -> Option<&str> {
        lookup(&self.form, name)
    }

    pub fn cookie(&self, name: &str) -> Option<&str> {
        self.headers
            .iter()
            .filter(|(key, _)| key == "cookie")
            .flat_map(|(_, value)| value.split(';'))
            .filter_map(|pair| pair.trim().split_once('='))
            .find(|(key, _)| *key == name)
            .map(|(_, value)| value)
    }
}

/// Reads one request.
pub fn read_request(stream: impl Read) -> Result<Request, ParseError> {
    let mut reader = BufReader::new(stream);
    let mut lines = Vec::new();
    let mut used = 0;
    loop {
        let remaining = u64::try_from(MAX_HEAD - used).unwrap_or_default();
        let mut line = Vec::new();
        let read = (&mut reader).take(remaining).read_until(b'\n', &mut line)?;
        used += read;
        if !line.ends_with(b"\n") {
            return Err(if used >= MAX_HEAD {
                ParseError::TooLarge
            } else {
                ParseError::Malformed
            });
        }
        line.pop();
        if line.ends_with(b"\r") {
            line.pop();
        }
        if line.is_empty() {
            break;
        }
        lines.push(String::from_utf8(line).map_err(|_| ParseError::Malformed)?);
    }

    let mut lines = lines.into_iter();
    let start = lines.next().ok_or(ParseError::Malformed)?;
    let mut parts = start.split(' ');
    let (Some(method), Some(target), Some(version), None) =
        (parts.next(), parts.next(), parts.next(), parts.next())
    else {
        return Err(ParseError::Malformed);
    };
    if !version.starts_with("HTTP/1.") {
        return Err(ParseError::Malformed);
    }
    let method = match method {
        "GET" => Method::Get,
        "POST" => Method::Post,
        _ => return Err(ParseError::Method),
    };
    let (path, query) = target.split_once('?').unwrap_or((target, ""));
    if !path.starts_with('/') {
        return Err(ParseError::Malformed);
    }

    let mut request = Request::new(method, path);
    request.query = parse_pairs(query).ok_or(ParseError::Malformed)?;
    for line in lines {
        let (name, value) = line.split_once(':').ok_or(ParseError::Malformed)?;
        request
            .headers
            .push((name.trim().to_ascii_lowercase(), value.trim().to_owned()));
    }

    // Browsers send forms with a length. Chunked bodies are never needed, and
    // accepting a framing this does not implement is how requests get smuggled.
    if request.header("transfer-encoding").is_some() {
        return Err(ParseError::Malformed);
    }
    let length = match request.header("content-length") {
        Some(length) => length.parse::<usize>().map_err(|_| ParseError::Malformed)?,
        None => 0,
    };
    if length > MAX_BODY {
        return Err(ParseError::TooLarge);
    }
    let mut body = vec![0; length];
    reader.read_exact(&mut body)?;
    let is_form = request
        .header("content-type")
        .is_some_and(|kind| kind.starts_with("application/x-www-form-urlencoded"));
    if is_form {
        let body = std::str::from_utf8(&body).map_err(|_| ParseError::Malformed)?;
        request.form = parse_pairs(body).ok_or(ParseError::Malformed)?;
    }
    Ok(request)
}

fn hex_value(byte: u8) -> Option<u8> {
    char::from(byte)
        .to_digit(16)
        .and_then(|digit| u8::try_from(digit).ok())
}

/// Decodes one `application/x-www-form-urlencoded` component. Paths are
/// rejected by core when they are not UTF-8, so a component that is not
/// UTF-8 is refused here rather than converted lossily.
pub fn decode_component(input: &str) -> Option<String> {
    let mut bytes = input.bytes();
    let mut decoded = Vec::with_capacity(input.len());
    while let Some(byte) = bytes.next() {
        match byte {
            b'+' => decoded.push(b' '),
            b'%' => {
                let high = bytes.next().and_then(hex_value)?;
                let low = bytes.next().and_then(hex_value)?;
                decoded.push((high << 4) | low);
            }
            other => decoded.push(other),
        }
    }
    String::from_utf8(decoded).ok()
}

/// Encodes a value for a query string. Everything outside the unreserved set
/// is escaped, so a path never changes the meaning of the URL it sits in.
pub fn encode_component(input: &str) -> String {
    let mut encoded = String::with_capacity(input.len());
    for byte in input.bytes() {
        if byte.is_ascii_alphanumeric() || matches!(byte, b'-' | b'.' | b'_' | b'~') {
            encoded.push(char::from(byte));
        } else {
            let _ = write!(encoded, "%{byte:02X}");
        }
    }
    encoded
}

fn parse_pairs(input: &str) -> Option<Vec<(String, String)>> {
    input
        .split('&')
        .filter(|pair| !pair.is_empty())
        .map(|pair| {
            let (name, value) = pair.split_once('=').unwrap_or((pair, ""));
            Some((decode_component(name)?, decode_component(value)?))
        })
        .collect()
}

#[derive(Clone, Debug)]
pub struct Response {
    pub status: u16,
    pub headers: Vec<(&'static str, String)>,
    pub body: Vec<u8>,
}

impl Response {
    pub fn new(status: u16, content_type: &str, body: impl Into<Vec<u8>>) -> Self {
        Self {
            status,
            headers: vec![("Content-Type", content_type.to_owned())],
            body: body.into(),
        }
    }

    pub fn html(status: u16, body: String) -> Self {
        Self::new(status, "text/html; charset=utf-8", body)
    }

    pub fn text(status: u16, body: &str) -> Self {
        Self::new(status, "text/plain; charset=utf-8", body)
    }

    /// A 303, which turns every form post into a fresh GET. Reloading the page
    /// afterwards therefore never starts the same run a second time.
    pub fn redirect(location: &str) -> Self {
        Self::new(303, "text/plain; charset=utf-8", "").with_header("Location", location)
    }

    #[must_use]
    pub fn with_header(mut self, name: &'static str, value: impl Into<String>) -> Self {
        self.headers.push((name, value.into()));
        self
    }

    #[cfg(test)]
    pub fn header(&self, name: &str) -> Option<&str> {
        self.headers
            .iter()
            .find(|(key, _)| key.eq_ignore_ascii_case(name))
            .map(|(_, value)| value.as_str())
    }

    #[cfg(test)]
    pub fn body_text(&self) -> &str {
        std::str::from_utf8(&self.body).unwrap_or_default()
    }

    pub fn write_to(&self, mut stream: impl Write) -> io::Result<()> {
        let mut head = format!("HTTP/1.1 {} {}\r\n", self.status, reason(self.status));
        for (name, value) in &self.headers {
            let _ = write!(head, "{name}: {value}\r\n");
        }
        let _ = write!(
            head,
            "Content-Length: {}\r\n\
             Connection: close\r\n\
             Cache-Control: no-store\r\n\
             X-Content-Type-Options: nosniff\r\n\
             X-Frame-Options: DENY\r\n\
             Referrer-Policy: no-referrer\r\n\
             Content-Security-Policy: {CONTENT_SECURITY_POLICY}\r\n\r\n",
            self.body.len()
        );
        stream.write_all(head.as_bytes())?;
        stream.write_all(&self.body)?;
        stream.flush()
    }
}

const fn reason(status: u16) -> &'static str {
    match status {
        200 => "OK",
        204 => "No Content",
        303 => "See Other",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        413 => "Content Too Large",
        _ => "Internal Server Error",
    }
}

/// Escapes text for an HTML text node or a quoted attribute value.
pub fn escape(text: &str) -> String {
    let mut escaped = String::with_capacity(text.len());
    for character in text.chars() {
        match character {
            '&' => escaped.push_str("&amp;"),
            '<' => escaped.push_str("&lt;"),
            '>' => escaped.push_str("&gt;"),
            '"' => escaped.push_str("&quot;"),
            '\'' => escaped.push_str("&#39;"),
            other => escaped.push(other),
        }
    }
    escaped
}

#[cfg(test)]
mod tests {
    use super::*;

    fn parse(raw: &str) -> Result<Request, ParseError> {
        read_request(raw.as_bytes())
    }

    #[test]
    fn a_form_post_is_decoded_into_fields() {
        let body = "source=%2Fm%C3%BAsica+nueva&insert_every=4&browse=";
        let raw = format!(
            "POST /create?x=1 HTTP/1.1\r\nHost: 127.0.0.1:9\r\n\
             Content-Type: application/x-www-form-urlencoded\r\n\
             Content-Length: {}\r\nCookie: a=b; pg_session=t0k\r\n\r\n{body}",
            body.len()
        );
        let Ok(request) = parse(&raw) else {
            panic!("a well-formed post was refused");
        };
        assert_eq!(request.method, Method::Post);
        assert_eq!(request.path, "/create");
        assert_eq!(request.query("x"), Some("1"));
        assert_eq!(request.header("host"), Some("127.0.0.1:9"));
        assert_eq!(request.field("source"), Some("/música nueva"));
        assert_eq!(request.field("insert_every"), Some("4"));
        assert_eq!(request.field("browse"), Some(""));
        assert_eq!(request.cookie("pg_session"), Some("t0k"));
    }

    #[test]
    fn requests_outside_the_supported_subset_are_refused() {
        assert!(matches!(
            parse("PUT / HTTP/1.1\r\n\r\n"),
            Err(ParseError::Method)
        ));
        assert!(matches!(
            parse("GET relative HTTP/1.1\r\n\r\n"),
            Err(ParseError::Malformed)
        ));
        assert!(matches!(
            parse("POST / HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n"),
            Err(ParseError::Malformed)
        ));
        assert!(matches!(
            parse("GET /?bad=%ZZ HTTP/1.1\r\n\r\n"),
            Err(ParseError::Malformed)
        ));
        // Not UTF-8 once decoded, which no path core accepts can be.
        assert!(matches!(
            parse("GET /?bad=%FF HTTP/1.1\r\n\r\n"),
            Err(ParseError::Malformed)
        ));
        assert!(matches!(
            parse("GET / HTTP/1.1\r\nHost: x"),
            Err(ParseError::Malformed)
        ));
    }

    #[test]
    fn oversized_requests_are_refused_before_they_are_buffered() {
        let head = format!("GET / HTTP/1.1\r\nX-Long: {}\r\n\r\n", "a".repeat(MAX_HEAD));
        assert!(matches!(parse(&head), Err(ParseError::TooLarge)));
        let body = format!(
            "POST / HTTP/1.1\r\nContent-Length: {}\r\n\r\n",
            MAX_BODY + 1
        );
        assert!(matches!(parse(&body), Err(ParseError::TooLarge)));
    }

    #[test]
    fn components_round_trip_through_encoding() {
        for value in ["C:\\Music\\Ünïcode & more", "/a b/+%/?#=", ""] {
            assert_eq!(
                decode_component(&encode_component(value)).as_deref(),
                Some(value)
            );
        }
    }

    #[test]
    fn markup_in_text_cannot_escape_its_element() {
        assert_eq!(
            escape(r#"<a href="x">'&'</a>"#),
            "&lt;a href=&quot;x&quot;&gt;&#39;&amp;&#39;&lt;/a&gt;"
        );
    }

    #[test]
    fn every_response_carries_its_length_and_the_security_headers() {
        let mut written = Vec::new();
        assert!(
            Response::html(200, "<p>hi</p>".into())
                .write_to(&mut written)
                .is_ok()
        );
        let written = String::from_utf8_lossy(&written);
        assert!(written.starts_with("HTTP/1.1 200 OK\r\n"));
        assert!(written.contains("Content-Length: 9\r\n"));
        assert!(written.contains("Connection: close\r\n"));
        assert!(written.contains("Content-Security-Policy: default-src 'none'"));
        assert!(written.ends_with("\r\n\r\n<p>hi</p>"));
    }
}
