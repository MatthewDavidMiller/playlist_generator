#![forbid(unsafe_code)]

//! The graphical frontend, served to the user's own web browser.
//!
//! It is a small HTTP server on 127.0.0.1 written against the standard
//! library alone, with pages that are plain HTML forms rendered here in Rust.
//! Nothing is drawn natively and nothing is loaded at run time, so the same
//! code links statically on Linux and Windows and runs wherever a browser does.

mod app;
mod browse;
mod http;
mod pages;
mod server;

use std::collections::hash_map::RandomState;
use std::fmt::Write as _;
use std::hash::{BuildHasher, Hasher};
use std::io::{self, BufRead, IsTerminal};
use std::net::{Ipv4Addr, SocketAddr, TcpListener, TcpStream};
use std::process::{Command, ExitCode, Stdio};
use std::sync::Arc;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::thread;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use crate::http::{ParseError, Response};
use crate::server::Server;

/// How long a connection may take to send its request. Browsers open spare
/// connections they may never use, and each one holds a thread until then.
const TIMEOUT: Duration = Duration::from_secs(15);
/// Connections served at once. Anything beyond this is closed unanswered.
const MAX_CONNECTIONS: usize = 64;

const USAGE: &str = "\
Usage: playlist-generator-web [--port PORT] [--no-browser]

Serves Playlist Generator to your web browser at 127.0.0.1 and opens it.

  --port PORT    Listen on this port instead of a free one the system picks
  --no-browser   Print the address without opening a browser
  -h, --help     Show this help
  -V, --version  Show the version";

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
struct Options {
    port: u16,
    open_browser: bool,
}

#[derive(Debug, Eq, PartialEq)]
enum Parsed {
    Run(Options),
    Help,
    Version,
}

fn parse_options(arguments: impl IntoIterator<Item = String>) -> Result<Parsed, String> {
    let mut options = Options {
        port: 0,
        open_browser: true,
    };
    let mut arguments = arguments.into_iter();
    while let Some(argument) = arguments.next() {
        match argument.as_str() {
            "-h" | "--help" => return Ok(Parsed::Help),
            "-V" | "--version" => return Ok(Parsed::Version),
            "--no-browser" => options.open_browser = false,
            "--port" => {
                let value = arguments.next().ok_or("--port needs a number")?;
                options.port = value
                    .parse()
                    .map_err(|_| format!("'{value}' is not a port number"))?;
            }
            other => return Err(format!("unrecognised argument '{other}'")),
        }
    }
    Ok(Parsed::Run(options))
}

/// A fresh secret for each launch.
///
/// `RandomState` keys its hasher from the operating system's random source
/// the first time a thread uses it, which is the one cryptographic-quality
/// randomness the standard library exposes. Two keyed hashes give 128 bits.
fn session_token() -> String {
    let now = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_or(0, |duration| duration.as_nanos());
    let mut token = String::with_capacity(32);
    for round in 0..2_u8 {
        let mut hasher = RandomState::new().build_hasher();
        hasher.write_u8(round);
        hasher.write_u128(now);
        hasher.write_u32(std::process::id());
        let _ = write!(token, "{:016x}", hasher.finish());
    }
    token
}

/// Hands the address to the desktop's default browser. Nothing here waits for
/// it: the launcher is reaped on its own thread, and a failure only means the
/// user opens the printed address themselves.
fn open_browser(url: &str) -> io::Result<()> {
    let mut command = if cfg!(windows) {
        // No shell is involved, so nothing in the address is reinterpreted.
        let mut command = Command::new("rundll32.exe");
        command.arg("url.dll,FileProtocolHandler");
        command
    } else if cfg!(target_os = "macos") {
        Command::new("open")
    } else {
        Command::new("xdg-open")
    };
    let mut child = command
        .arg(url)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()?;
    thread::spawn(move || {
        if !child.wait().is_ok_and(|status| status.success()) {
            println!("No browser could be opened. Open the address above yourself.");
        }
    });
    Ok(())
}

fn wake(address: SocketAddr) {
    drop(TcpStream::connect(address));
}

fn connection(stream: &TcpStream, server: &Server) {
    drop(stream.set_read_timeout(Some(TIMEOUT)));
    drop(stream.set_write_timeout(Some(TIMEOUT)));
    let response = match http::read_request(stream) {
        Ok(request) => server.handle(&request),
        // Closed, timed out, or a spare connection the browser never used.
        Err(ParseError::Io) => return,
        Err(ParseError::TooLarge) => Response::text(413, "That request is too large."),
        Err(ParseError::Method) => Response::text(405, "That kind of request is not served."),
        Err(ParseError::Malformed) => Response::text(400, "That request could not be read."),
    };
    drop(response.write_to(stream));
}

/// Serves until [`Server::request_stop`] is called and the loop is woken.
fn serve(listener: &TcpListener, server: &Arc<Server>) {
    let address = listener.local_addr();
    let active = Arc::new(AtomicUsize::new(0));
    for stream in listener.incoming() {
        if server.is_stopping() {
            break;
        }
        let Ok(stream) = stream else { continue };
        if active.load(Ordering::SeqCst) >= MAX_CONNECTIONS {
            continue;
        }
        active.fetch_add(1, Ordering::SeqCst);
        let server = Arc::clone(server);
        let active = Arc::clone(&active);
        let address = address.as_ref().ok().copied();
        thread::spawn(move || {
            connection(&stream, &server);
            drop(stream);
            active.fetch_sub(1, Ordering::SeqCst);
            // Quit was pressed: the accept loop is still blocked waiting for
            // the next connection, so give it one.
            if server.is_stopping()
                && let Some(address) = address
            {
                wake(address);
            }
        });
    }
}

/// Keeps a fatal message on screen. Started by double-clicking, the console
/// window would otherwise close before the message could be read.
fn hold_console() {
    if io::stdin().is_terminal() {
        eprintln!("Press Enter to close.");
        drop(io::stdin().lock().read_line(&mut String::new()));
    }
}

pub fn run() -> ExitCode {
    let options = match parse_options(std::env::args().skip(1)) {
        Ok(Parsed::Run(options)) => options,
        Ok(Parsed::Help) => {
            println!("{USAGE}");
            return ExitCode::SUCCESS;
        }
        Ok(Parsed::Version) => {
            println!("playlist-generator-web {}", env!("CARGO_PKG_VERSION"));
            return ExitCode::SUCCESS;
        }
        Err(message) => {
            eprintln!("{message}\n\n{USAGE}");
            return ExitCode::from(2);
        }
    };

    let listener = match TcpListener::bind((Ipv4Addr::LOCALHOST, options.port)) {
        Ok(listener) => listener,
        Err(error) => {
            eprintln!(
                "Playlist Generator could not listen on 127.0.0.1:{}: {error}",
                options.port
            );
            hold_console();
            return ExitCode::FAILURE;
        }
    };
    let address = match listener.local_addr() {
        Ok(address) => address,
        Err(error) => {
            eprintln!("Playlist Generator could not read its own address: {error}");
            hold_console();
            return ExitCode::FAILURE;
        }
    };
    let server = Arc::new(Server::new(session_token(), address.port()));
    let url = format!("http://{address}{}", server.launch_path());

    println!(
        "Playlist Generator {} is running at\n\n    {url}\n",
        env!("CARGO_PKG_VERSION")
    );
    if options.open_browser {
        match open_browser(&url) {
            Ok(()) => println!("Your browser should open it now."),
            Err(_) => println!("No browser could be opened. Open the address above yourself."),
        }
    }
    println!(
        "Leave this window open while you work. Press Enter here, or choose Quit in \
         the page, to stop. Quitting either way finishes cleaning up a run first."
    );

    // An empty or closed standard input, as under a desktop launcher, is not a
    // request to stop; only a line typed into the console is.
    let watcher = Arc::clone(&server);
    thread::spawn(move || {
        if io::stdin()
            .lock()
            .read_line(&mut String::new())
            .unwrap_or(0)
            > 0
        {
            watcher.request_stop();
            wake(address);
        }
    });

    serve(&listener, &server);
    println!("Stopping…");
    server.shutdown();
    println!("Playlist Generator has stopped.");
    ExitCode::SUCCESS
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::{Read, Write};

    fn exchange(address: SocketAddr, raw: &str) -> String {
        let Ok(mut stream) = TcpStream::connect(address) else {
            panic!("could not reach the test server");
        };
        assert!(stream.write_all(raw.as_bytes()).is_ok());
        let mut response = String::new();
        assert!(stream.read_to_string(&mut response).is_ok());
        response
    }

    #[test]
    fn a_browser_signs_in_uses_the_pages_and_quits_over_real_sockets() {
        let Ok(listener) = TcpListener::bind((Ipv4Addr::LOCALHOST, 0)) else {
            panic!("loopback is unavailable");
        };
        let Ok(address) = listener.local_addr() else {
            panic!("the listener has no address");
        };
        let server = Arc::new(Server::new(session_token(), address.port()));
        let serving = {
            let server = Arc::clone(&server);
            thread::spawn(move || serve(&listener, &server))
        };
        let host = format!("Host: {address}\r\n");

        let signin = exchange(
            address,
            &format!("GET {} HTTP/1.1\r\n{host}\r\n", server.launch_path()),
        );
        assert!(signin.starts_with("HTTP/1.1 303 See Other\r\n"));
        let Some(cookie) = signin
            .lines()
            .find_map(|line| line.strip_prefix("Set-Cookie: "))
            .and_then(|cookie| cookie.split(';').next())
        else {
            panic!("signing in set no cookie: {signin}");
        };

        let page = exchange(
            address,
            &format!("GET /create HTTP/1.1\r\n{host}Cookie: {cookie}\r\n\r\n"),
        );
        assert!(page.starts_with("HTTP/1.1 200 OK\r\n"));
        assert!(page.contains("<h1>Create Playlist</h1>"));

        let garbage = exchange(address, "NOT HTTP\r\n\r\n");
        assert!(garbage.starts_with("HTTP/1.1 400"));

        let quit = exchange(
            address,
            &format!(
                "POST /quit HTTP/1.1\r\n{host}Cookie: {cookie}\r\n\
                 Content-Type: application/x-www-form-urlencoded\r\nContent-Length: 0\r\n\r\n"
            ),
        );
        assert!(quit.contains("has stopped"));
        assert!(serving.join().is_ok());
        server.shutdown();
    }

    #[test]
    fn options_cover_the_port_and_the_browser() {
        let parse = |arguments: &[&str]| {
            parse_options(arguments.iter().map(|argument| (*argument).to_owned()))
        };
        assert_eq!(
            parse(&[]),
            Ok(Parsed::Run(Options {
                port: 0,
                open_browser: true
            }))
        );
        assert_eq!(
            parse(&["--port", "8080", "--no-browser"]),
            Ok(Parsed::Run(Options {
                port: 8080,
                open_browser: false
            }))
        );
        assert_eq!(parse(&["--help"]), Ok(Parsed::Help));
        assert_eq!(parse(&["-V"]), Ok(Parsed::Version));
        assert!(parse(&["--port"]).is_err());
        assert!(parse(&["--port", "70000"]).is_err());
        assert!(parse(&["--gui"]).is_err());
    }

    #[test]
    fn every_launch_gets_its_own_unguessable_token() {
        let first = session_token();
        assert_eq!(first.len(), 32);
        assert!(first.bytes().all(|byte| byte.is_ascii_hexdigit()));
        assert_ne!(first, session_token());
    }
}
