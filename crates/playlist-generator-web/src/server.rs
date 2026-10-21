//! Routing, and the checks that keep the server to the browser it opened.
//!
//! The listener is bound to loopback, but loopback is shared by every user on
//! the machine and reachable from every web page the browser has open. Three
//! checks close that off:
//!
//! - `Host` must name this server, so a hostile domain re-pointed at
//!   127.0.0.1 (DNS rebinding) is refused before anything else happens.
//! - Every page needs the session cookie, which only the launch link printed
//!   in the console can set, and which changes on every launch.
//! - The cookie is `SameSite=Strict` and posts must not come from another
//!   origin, so another site cannot submit a form here on the user's behalf.

use std::path::Path;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Mutex, MutexGuard, PoisonError};

use crate::app::{App, Field, Page};
use crate::browse;
use crate::http::{Method, Request, Response};
use crate::pages;

pub const COOKIE: &str = "pg_session";

/// Compares without stopping at the first difference, so response timing
/// does not reveal how much of a guessed token was right.
fn same_secret(left: &str, right: &str) -> bool {
    left.len() == right.len()
        && left
            .bytes()
            .zip(right.bytes())
            .fold(0, |difference, (a, b)| difference | (a ^ b))
            == 0
}

pub struct Server {
    token: String,
    hosts: [String; 2],
    origins: [String; 2],
    app: Mutex<App>,
    stopping: AtomicBool,
}

impl Server {
    pub fn new(token: String, port: u16) -> Self {
        let hosts = [format!("127.0.0.1:{port}"), format!("localhost:{port}")];
        let origins = hosts.clone().map(|host| format!("http://{host}"));
        Self {
            token,
            hosts,
            origins,
            app: Mutex::new(App::default()),
            stopping: AtomicBool::new(false),
        }
    }

    /// The address that signs the browser in.
    pub fn launch_path(&self) -> String {
        format!("/?session={}", self.token)
    }

    fn app(&self) -> MutexGuard<'_, App> {
        // A panic while handling one request must not lock the user out of
        // the run that is still going.
        self.app.lock().unwrap_or_else(PoisonError::into_inner)
    }

    pub fn is_stopping(&self) -> bool {
        self.stopping.load(Ordering::SeqCst)
    }

    /// Asks the server to stop accepting work and cancels any run. The accept
    /// loop notices once it is woken; [`Server::shutdown`] then waits.
    pub fn request_stop(&self) {
        self.stopping.store(true, Ordering::SeqCst);
        self.app().stop();
    }

    /// Waits for a cancelled run to finish removing its temporary file and
    /// reaping `FFmpeg`.
    pub fn shutdown(&self) {
        self.app().shutdown();
    }

    pub fn handle(&self, request: &Request) -> Response {
        let known_host = request.header("host").is_some_and(|host| {
            self.hosts
                .iter()
                .any(|known| known.eq_ignore_ascii_case(host))
        });
        if !known_host {
            return Response::text(403, "This server only answers to 127.0.0.1.");
        }

        // Unauthenticated, because the sign-in and refusal pages use them too.
        match (request.method, request.path.as_str()) {
            (Method::Get, "/style.css") => {
                return Response::new(200, "text/css; charset=utf-8", pages::STYLESHEET);
            }
            (Method::Get, "/favicon.ico") => return Response::new(204, "image/x-icon", ""),
            _ => {}
        }

        if request.method == Method::Get
            && request.path == "/"
            && let Some(session) = request.query("session")
        {
            if !same_secret(session, &self.token) {
                return Response::html(403, pages::forbidden());
            }
            // Redirecting also takes the token out of the address bar.
            return Response::redirect(Page::Create.path()).with_header(
                "Set-Cookie",
                format!("{COOKIE}={}; HttpOnly; SameSite=Strict; Path=/", self.token),
            );
        }

        let signed_in = request
            .cookie(COOKIE)
            .is_some_and(|cookie| same_secret(cookie, &self.token));
        if !signed_in {
            return Response::html(403, pages::forbidden());
        }

        if request.method == Method::Post {
            let foreign_origin = request
                .header("origin")
                .is_some_and(|origin| !self.origins.iter().any(|known| known == origin));
            let foreign_fetch = request
                .header("sec-fetch-site")
                .is_some_and(|site| site != "same-origin" && site != "none");
            if foreign_origin || foreign_fetch {
                return Response::text(
                    403,
                    "Forms are only accepted from this server's own pages.",
                );
            }
        }

        if self.is_stopping() {
            return Response::html(200, pages::stopped());
        }
        if request.method == Method::Post && request.path == "/quit" {
            self.request_stop();
            return Response::html(200, pages::stopped());
        }

        let mut app = self.app();
        app.receive();
        match (request.method, request.path.as_str()) {
            (Method::Get, "/") => Response::redirect(Page::Create.path()),
            (Method::Get, "/create") => Response::html(200, pages::create(&app)),
            (Method::Get, "/normalize") => Response::html(200, pages::normalize(&app)),
            (Method::Get, "/activity") => Response::html(200, pages::activity(&app)),
            (Method::Get, "/about") => Response::html(200, pages::about(&app)),
            (Method::Get, "/browse") => browse_page(&app, request),
            (Method::Post, "/create") => submit(&mut app, request, Page::Create),
            (Method::Post, "/normalize") => submit(&mut app, request, Page::Normalize),
            (Method::Post, "/browse") => choose(&mut app, request),
            (Method::Post, "/pause") => {
                app.pause();
                Response::redirect(Page::Activity.path())
            }
            (Method::Post, "/resume") => {
                app.resume();
                Response::redirect(Page::Activity.path())
            }
            (Method::Post, "/stop") => {
                app.stop();
                Response::redirect(Page::Activity.path())
            }
            (_, "/" | "/activity" | "/about" | "/pause" | "/resume" | "/stop" | "/quit") => {
                Response::text(405, "That page does not accept this kind of request.")
            }
            _ => Response::text(404, "There is no such page."),
        }
    }
}

/// Saves what was typed on a form page, then follows whichever button sent it.
fn submit(app: &mut App, request: &Request, page: Page) -> Response {
    for field in Field::ALL.into_iter().filter(|field| field.page() == page) {
        if let Some(value) = request.field(field.name()) {
            app.set(field, value);
        }
    }
    match page {
        Page::Create => app.set_counts(request.field("insert_every"), None),
        _ => app.set_counts(None, request.field("jobs")),
    }

    if let Some(field) = request.field("browse").and_then(Field::from_name) {
        return Response::redirect(&format!("/browse?field={}", field.name()));
    }
    if request.field("action") == Some("start") {
        let started = match page {
            Page::Create => app.start_generate(),
            _ => app.start_normalize(),
        };
        if !started {
            app.log("A run is already in progress; wait for it to end first.");
        }
        // Progress and messages only exist on Activity, so a run started from
        // the form would otherwise look like nothing had happened.
        return Response::redirect(Page::Activity.path());
    }
    Response::redirect(page.path())
}

fn browse_page(app: &App, request: &Request) -> Response {
    let Some(field) = request.query("field").and_then(Field::from_name) else {
        return Response::text(404, "There is no such box to browse for.");
    };
    let listing = if request.query("top").is_some() {
        browse::roots()
    } else if let Some(directory) = request.query("dir") {
        browse::list(Path::new(directory), field.pick())
    } else {
        browse::start_directory(app.value(field)).map_or_else(browse::roots, |directory| {
            browse::list(&directory, field.pick())
        })
    };
    Response::html(200, pages::browse(app, field, &listing))
}

fn choose(app: &mut App, request: &Request) -> Response {
    let Some(field) = request.field("field").and_then(Field::from_name) else {
        return Response::text(400, "There is no such box to fill in.");
    };
    let chosen = match (request.field("path"), request.field("dir")) {
        (Some(path), _) => path.to_owned(),
        (None, Some(directory)) => browse::save_path(
            directory,
            request.field("name").unwrap_or_default(),
            app.value(field),
        ),
        (None, None) => return Response::text(400, "Nothing was chosen."),
    };
    app.choose(field, &chosen);
    Response::redirect(field.page().path())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::app::tests::settle;

    const TOKEN: &str = "0123456789abcdef0123456789abcdef";
    const PORT: u16 = 4321;

    fn server() -> Server {
        Server::new(TOKEN.into(), PORT)
    }

    fn signed_in(method: Method, path: &str) -> Request {
        let (path, query) = path.split_once('?').unwrap_or((path, ""));
        let mut request = Request::new(method, path);
        request.query = query
            .split('&')
            .filter_map(|pair| pair.split_once('='))
            .map(|(key, value)| (key.to_owned(), value.to_owned()))
            .collect();
        request.headers = vec![
            ("host".into(), format!("127.0.0.1:{PORT}")),
            ("cookie".into(), format!("{COOKIE}={TOKEN}")),
        ];
        request
    }

    fn post(path: &str, form: &[(&str, &str)]) -> Request {
        let mut request = signed_in(Method::Post, path);
        request
            .headers
            .push(("origin".into(), format!("http://127.0.0.1:{PORT}")));
        request.form = form
            .iter()
            .map(|(key, value)| ((*key).to_owned(), (*value).to_owned()))
            .collect();
        request
    }

    #[test]
    fn the_launch_link_signs_the_browser_in_and_leaves_the_address_bar() {
        let server = server();
        let mut request = Request::new(Method::Get, "/");
        request.headers = vec![("host".into(), format!("localhost:{PORT}"))];
        request.query = vec![("session".into(), TOKEN.into())];
        let response = server.handle(&request);
        assert_eq!(response.status, 303);
        assert_eq!(response.header("location"), Some("/create"));
        let cookie = response.header("set-cookie").unwrap_or_default();
        assert!(cookie.starts_with(&format!("{COOKIE}={TOKEN};")));
        assert!(cookie.contains("HttpOnly") && cookie.contains("SameSite=Strict"));

        request.query = vec![("session".into(), "guess".into())];
        assert_eq!(server.handle(&request).status, 403);
    }

    #[test]
    fn pages_need_the_session_cookie() {
        let server = server();
        let mut request = signed_in(Method::Get, "/create");
        assert_eq!(server.handle(&request).status, 200);
        request.headers.retain(|(name, _)| name != "cookie");
        let response = server.handle(&request);
        assert_eq!(response.status, 403);
        assert!(
            response
                .body_text()
                .contains("Open the link from the console")
        );
        request
            .headers
            .push(("cookie".into(), format!("{COOKIE}=wrong")));
        assert_eq!(server.handle(&request).status, 403);
        // The refusal page has to be able to load its own stylesheet.
        request.path = "/style.css".into();
        assert_eq!(server.handle(&request).status, 200);
    }

    #[test]
    fn a_rebound_hostname_is_refused_even_with_the_cookie() {
        let server = server();
        let mut request = signed_in(Method::Get, "/create");
        request.headers.retain(|(name, _)| name != "host");
        request
            .headers
            .push(("host".into(), format!("attacker.example:{PORT}")));
        assert_eq!(server.handle(&request).status, 403);
        request.headers.retain(|(name, _)| name != "host");
        assert_eq!(server.handle(&request).status, 403);
    }

    #[test]
    fn forms_posted_from_another_site_are_refused() {
        let server = server();
        let mut request = post("/stop", &[]);
        request.headers.retain(|(name, _)| name != "origin");
        request
            .headers
            .push(("origin".into(), "https://example.com".into()));
        assert_eq!(server.handle(&request).status, 403);
        let mut request = post("/stop", &[]);
        request
            .headers
            .push(("sec-fetch-site".into(), "cross-site".into()));
        assert_eq!(server.handle(&request).status, 403);
        assert_eq!(server.handle(&post("/stop", &[])).status, 303);
    }

    #[test]
    fn browsing_keeps_what_was_typed_and_returns_with_the_choice() {
        let server = server();
        let response = server.handle(&post(
            "/create",
            &[
                ("source", "/typed"),
                ("special", "/id.mp3"),
                ("insert_every", "7"),
                ("playlist_output", ""),
                ("browse", "playlist_output"),
            ],
        ));
        assert_eq!(response.status, 303);
        assert_eq!(
            response.header("location"),
            Some("/browse?field=playlist_output")
        );
        let response = server.handle(&post(
            "/browse",
            &[
                ("field", "playlist_output"),
                ("dir", "/music"),
                ("name", "mix.m3u8"),
            ],
        ));
        assert_eq!(response.header("location"), Some("/create"));
        let app = server.app();
        assert_eq!(app.source, "/typed");
        assert_eq!(app.special, "/id.mp3");
        assert_eq!(app.insert_every, "7");
        assert_eq!(
            app.playlist_output,
            Path::new("/music").join("mix.m3u8").display().to_string()
        );
    }

    #[test]
    fn the_folder_browser_lists_the_folder_it_is_given() {
        let root = browse::tests::temporary("server");
        assert!(std::fs::create_dir_all(root.join("Albums")).is_ok());
        let server = server();
        let mut request = signed_in(Method::Get, "/browse");
        request.query = vec![
            ("field".into(), "source".into()),
            ("dir".into(), root.display().to_string()),
        ];
        let response = server.handle(&request);
        assert_eq!(response.status, 200);
        assert!(response.body_text().contains(">Albums</a>"));
        request.query = vec![("field".into(), "nonsense".into())];
        assert_eq!(server.handle(&request).status, 404);
        drop(std::fs::remove_dir_all(root));
    }

    #[test]
    fn starting_a_run_shows_the_page_that_reports_it() {
        let server = server();
        // The paths are empty, so core refuses this before anything is written.
        let response = server.handle(&post("/create", &[("action", "start")]));
        assert_eq!(response.header("location"), Some("/activity"));
        settle(&mut server.app());
        let page = server.handle(&signed_in(Method::Get, "/activity"));
        assert!(page.body_text().contains("Creating playlist…"));
        assert!(page.body_text().contains("Playlist failed:"));
    }

    #[test]
    fn quitting_stops_the_server_and_says_so() {
        let server = server();
        let response = server.handle(&post("/quit", &[]));
        assert_eq!(response.status, 200);
        assert!(response.body_text().contains("has stopped"));
        assert!(server.is_stopping());
        let after = server.handle(&signed_in(Method::Get, "/create"));
        assert!(after.body_text().contains("has stopped"));
    }

    #[test]
    fn unknown_pages_and_wrong_methods_are_told_apart() {
        let server = server();
        assert_eq!(
            server.handle(&signed_in(Method::Get, "/nowhere")).status,
            404
        );
        assert_eq!(server.handle(&signed_in(Method::Get, "/stop")).status, 405);
        assert_eq!(server.handle(&post("/about", &[])).status, 405);
    }
}
