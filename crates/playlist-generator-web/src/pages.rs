//! Rendering. Every function here reads [`App`] and returns a complete HTML
//! document, so the tests assert on the markup without a browser.
//!
//! The pages are plain forms and links with no script. Each action is a post
//! answered by a redirect, and Activity reloads itself once a second while a
//! run is in progress, which is all the liveness the interface needs.

use std::fmt::Write as _;

use playlist_generator_core::OperationEvent;

use crate::app::{App, Field, Page, Pick, REPOSITORY};
use crate::browse::{Listing, MAX_ENTRIES};
use crate::http::{encode_component, escape};

pub const STYLESHEET: &str = include_str!("style.css");

/// What each path box explains underneath itself.
const fn help(field: Field) -> &'static str {
    match field {
        Field::Source => {
            "This folder and every folder inside it is searched for MP3, FLAC, WAV, \
             M4A, AAC, Ogg, Opus, and WMA files."
        }
        Field::Special => {
            "One audio file that is added again and again through the playlist, such \
             as a jingle or a station ident. It is left out of the shuffle."
        }
        Field::PlaylistOutput => {
            "The playlist file to write. The name has to end in .m3u8, and an existing \
             file at this location is replaced."
        }
        Field::NormalizeSource => {
            "The folder of audio files to copy, including everything inside it."
        }
        Field::NormalizeOutput => {
            "A separate folder for the results, which keeps the same folder layout. \
             Files that are already there are left alone, so a run that was stopped \
             can simply be started again."
        }
        Field::Ffmpeg => {
            "Leave this empty to use the FFmpeg already installed on this computer. \
             Fill it in only to choose a particular copy of FFmpeg."
        }
    }
}

fn plural(count: &str, one: &'static str, many: &'static str) -> &'static str {
    if count.trim() == "1" { one } else { many }
}

/// The frame every page shares: navigation, the page itself, and the status
/// bar with Quit.
fn layout(app: &App, current: Option<Page>, title: &str, body: &str) -> String {
    let mut html = String::with_capacity(body.len() + 2048);
    html.push_str(
        "<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">\
         <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\
         <meta name=\"color-scheme\" content=\"light dark\">",
    );
    // Only Activity changes on its own, and only while something is running.
    if app.busy && current == Some(Page::Activity) {
        html.push_str("<meta http-equiv=\"refresh\" content=\"1\">");
    }
    let _ = write!(
        html,
        "<title>{} · Playlist Generator</title>\
         <link rel=\"stylesheet\" href=\"/style.css\"></head><body>\
         <nav class=\"tabs\" aria-label=\"Pages\">",
        escape(title)
    );
    for page in Page::ORDER {
        let selected = if current == Some(page) {
            " aria-current=\"page\""
        } else {
            ""
        };
        let _ = write!(
            html,
            "<a href=\"{}\"{selected}>{}</a>",
            page.path(),
            page.title()
        );
    }
    let _ = write!(
        html,
        "</nav><main class=\"form\">{body}</main>\
         <footer class=\"status\"><span>{}</span>\
         <form method=\"post\" action=\"/quit\"><button type=\"submit\" \
         title=\"Stop Playlist Generator. A run in progress is stopped first and \
         finished files are kept.\">Quit</button></form></footer></body></html>",
        if app.busy { "Working…" } else { "Ready" }
    );
    html
}

fn intro(html: &mut String, title: &str, description: &str) {
    let _ = write!(html, "<h1>{title}</h1><p class=\"intro\">{description}</p>");
}

fn busy_note(html: &mut String, app: &App) {
    if app.busy {
        html.push_str(
            "<p class=\"notice\">A run is in progress, so these settings are locked \
             until it ends. <a href=\"/activity\">Follow it on Activity.</a></p>",
        );
    }
}

/// A labelled path box with its browse button and an explanation.
fn path_field(html: &mut String, app: &App, field: Field) {
    let name = field.name();
    let _ = write!(
        html,
        "<div class=\"control\"><label for=\"{name}\">{label}</label>\
         <div class=\"row\"><input type=\"text\" id=\"{name}\" name=\"{name}\" \
         value=\"{value}\" spellcheck=\"false\" autocomplete=\"off\" \
         aria-describedby=\"{name}-help\">\
         <button type=\"submit\" name=\"browse\" value=\"{name}\" formnovalidate>\
         Browse…</button></div>\
         <p class=\"help\" id=\"{name}-help\">{help}</p></div>",
        label = field.label(),
        value = escape(app.value(field)),
        help = help(field),
    );
}

/// A count box, written as the sentence it completes.
struct Count<'a> {
    name: &'a str,
    label: &'a str,
    before: &'a str,
    value: &'a str,
    after: &'a str,
    range: &'a str,
    help: &'a str,
}

fn count_field(html: &mut String, count: &Count<'_>) {
    let _ = write!(
        html,
        "<div class=\"control\"><label for=\"{name}\">{label}</label>\
         <div class=\"sentence\">{before} <input type=\"number\" id=\"{name}\" \
         name=\"{name}\" value=\"{value}\" {range} step=\"1\" inputmode=\"numeric\" \
         required aria-describedby=\"{name}-help\"> {after}</div>\
         <p class=\"help\" id=\"{name}-help\">{help}</p></div>",
        name = count.name,
        label = count.label,
        before = count.before,
        value = escape(count.value),
        after = count.after,
        range = count.range,
        help = count.help,
    );
}

/// The action button is written first so that Enter in any box presses it
/// rather than the first Browse button; the stylesheet moves it to the end.
fn form_start(html: &mut String, app: &App, action: &str, label: &str) {
    let _ = write!(
        html,
        "<form method=\"post\" action=\"{action}\"><fieldset{}>\
         <button type=\"submit\" name=\"action\" value=\"start\" class=\"primary\">\
         {label}</button>",
        if app.busy { " disabled" } else { "" }
    );
}

pub fn create(app: &App) -> String {
    let mut body = String::new();
    intro(
        &mut body,
        "Create Playlist",
        "Builds a shuffled playlist file from a folder of music, dropping the same \
         chosen track back in at a regular interval. Your audio files are only read, \
         never changed.",
    );
    busy_note(&mut body, app);
    form_start(&mut body, app, "/create", "Create playlist");
    path_field(&mut body, app, Field::Source);
    path_field(&mut body, app, Field::Special);
    count_field(
        &mut body,
        &Count {
            name: "insert_every",
            label: "How often to repeat it",
            before: "Add it after every",
            value: &app.insert_every,
            after: plural(&app.insert_every, "shuffled track.", "shuffled tracks."),
            range: "min=\"1\"",
            help: "If fewer tracks than that are left at the end, no copy is added there.",
        },
    );
    path_field(&mut body, app, Field::PlaylistOutput);
    body.push_str("</fieldset></form>");
    layout(app, Some(Page::Create), "Create Playlist", &body)
}

pub fn normalize(app: &App) -> String {
    let mut body = String::new();
    intro(
        &mut body,
        "Normalize Volume",
        "Copies a music folder at an even listening volume, so tracks no longer jump \
         between loud and quiet. The copies are new Opus files with the original tags \
         kept; nothing in the source folder is modified. This needs FFmpeg installed.",
    );
    busy_note(&mut body, app);
    form_start(&mut body, app, "/normalize", "Start normalizing");
    path_field(&mut body, app, Field::NormalizeSource);
    path_field(&mut body, app, Field::NormalizeOutput);
    path_field(&mut body, app, Field::Ffmpeg);
    count_field(
        &mut body,
        &Count {
            name: "jobs",
            label: "Files at a time",
            before: "Convert",
            value: &app.jobs,
            after: plural(&app.jobs, "file at a time.", "files at a time."),
            range: "min=\"1\" max=\"32\"",
            help: "Higher finishes sooner but uses more of the processor. Lower leaves \
                   the computer freer for other work.",
        },
    );
    body.push_str("</fieldset></form>");
    layout(app, Some(Page::Normalize), "Normalize Volume", &body)
}

fn progress(html: &mut String, progress: &OperationEvent) {
    let _ = write!(
        html,
        "<div class=\"progress\"><progress max=\"{total}\" value=\"{completed}\" \
         aria-label=\"Files done\">{completed} of {total}</progress>\
         <p>{completed} of {total} files</p>\
         <p>{} copied  •  {} skipped, already in the output folder  •  {} failed</p></div>",
        progress.normalized,
        progress.skipped,
        progress.failed,
        total = progress.total.max(1),
        completed = progress.completed.min(progress.total.max(1)),
    );
}

fn control_button(html: &mut String, action: &str, label: &str, hint: &str) {
    let _ = write!(
        html,
        "<form method=\"post\" action=\"{action}\"><button type=\"submit\" \
         title=\"{hint}\">{label}</button></form>"
    );
}

pub fn activity(app: &App) -> String {
    let mut body = String::new();
    intro(
        &mut body,
        "Activity",
        "Progress and messages from the current run, and from the last one that \
         finished.",
    );
    if let Some(event) = &app.progress {
        progress(&mut body, event);
    }
    if app.is_controllable() {
        body.push_str("<div class=\"controls\">");
        if app.is_paused() {
            control_button(
                &mut body,
                "/resume",
                "Resume",
                "Carry on with the remaining files.",
            );
        } else {
            control_button(
                &mut body,
                "/pause",
                "Pause",
                "Wait once the files in progress are finished.",
            );
        }
        control_button(
            &mut body,
            "/stop",
            "Stop",
            "End the run. Finished files are kept.",
        );
        body.push_str("</div>");
    }
    body.push_str("<h2>Messages, newest first</h2>");
    if app.activity.is_empty() {
        body.push_str("<p>Nothing has run yet.</p>");
    } else {
        body.push_str("<ul class=\"log\">");
        for line in app.activity.iter().rev() {
            let _ = write!(body, "<li>{}</li>", escape(line));
        }
        body.push_str("</ul>");
    }
    layout(app, Some(Page::Activity), "Activity", &body)
}

pub fn about(app: &App) -> String {
    let mut body = String::new();
    let _ = write!(
        body,
        "<h1>Playlist Generator</h1><p>Version {}</p>\
         <p>Builds shuffled .m3u8 playlists and volume-matched Opus copies of a music \
         library. A command-line version of the same program is next to this one in \
         the installation folder.</p>\
         <h2>Project repository</h2>\
         <p><a href=\"{REPOSITORY}\" rel=\"noopener noreferrer\" target=\"_blank\">\
         {REPOSITORY}</a></p>",
        env!("CARGO_PKG_VERSION")
    );
    // Both documents run to thousands of lines, so they stay folded away until
    // somebody asks to read them.
    let _ = write!(
        body,
        "<details><summary>License</summary><pre>{}</pre></details>\
         <details><summary>Third-party notices</summary><pre>{}</pre></details>",
        escape(include_str!("../../../LICENSE")),
        escape(include_str!("../../../THIRD_PARTY_NOTICES.txt")),
    );
    layout(app, Some(Page::About), "About", &body)
}

fn browse_link(field: Field, directory: Option<&str>) -> String {
    directory.map_or_else(
        || format!("/browse?field={}&top=1", field.name()),
        |directory| {
            format!(
                "/browse?field={}&dir={}",
                field.name(),
                encode_component(directory)
            )
        },
    )
}

fn choose_form(html: &mut String, field: Field, path: &str, label: &str, class: &str) {
    let _ = write!(
        html,
        "<form method=\"post\" action=\"/browse\">\
         <input type=\"hidden\" name=\"field\" value=\"{}\">\
         <input type=\"hidden\" name=\"path\" value=\"{}\">\
         <button type=\"submit\" class=\"{class}\">{}</button></form>",
        field.name(),
        escape(path),
        escape(label),
    );
}

const fn browse_guidance(pick: Pick) -> &'static str {
    match pick {
        Pick::Folder => "Open folders until the one you want is shown, then use it.",
        Pick::AudioFile => "Open folders until the track is listed, then choose it.",
        Pick::Playlist => {
            "Open the folder to save in and give the file a name ending in .m3u8, or \
             choose an existing playlist to replace it."
        }
        Pick::Program => "Open the folder that holds the FFmpeg program, then choose it.",
    }
}

pub fn browse(app: &App, field: Field, listing: &Listing) -> String {
    let pick = field.pick();
    let mut body = String::new();
    let _ = write!(
        body,
        "<h1>Choose: {}</h1><p class=\"intro\">{}</p>",
        field.label(),
        browse_guidance(pick)
    );
    if let Some(error) = &listing.error {
        let _ = write!(body, "<p class=\"notice\">{}</p>", escape(error));
    }
    body.push_str("<p class=\"where\">Looking in <code>");
    body.push_str(&listing.directory.as_deref().map_or_else(
        || {
            if cfg!(windows) {
                "every drive".to_owned()
            } else {
                "the top level".to_owned()
            }
        },
        escape,
    ));
    body.push_str("</code></p><nav class=\"crumbs\">");
    if let Some(parent) = &listing.parent {
        let _ = write!(
            body,
            "<a href=\"{}\">Up one folder</a>",
            escape(&browse_link(field, Some(parent)))
        );
    }
    if listing.directory.is_some() {
        let _ = write!(
            body,
            "<a href=\"{}\">{}</a>",
            escape(&browse_link(field, None)),
            if cfg!(windows) {
                "All drives"
            } else {
                "Top level"
            }
        );
    }
    let _ = write!(body, "<a href=\"{}\">Cancel</a></nav>", field.page().path());

    if let Some(directory) = &listing.directory {
        match pick {
            Pick::Folder => {
                choose_form(&mut body, field, directory, "Use this folder", "primary");
            }
            Pick::Playlist => {
                let current = std::path::Path::new(app.value(field))
                    .file_name()
                    .and_then(|name| name.to_str())
                    .unwrap_or("playlist.m3u8");
                let _ = write!(
                    body,
                    "<form method=\"post\" action=\"/browse\" class=\"save\">\
                     <input type=\"hidden\" name=\"field\" value=\"{}\">\
                     <input type=\"hidden\" name=\"dir\" value=\"{}\">\
                     <label for=\"name\">File name</label>\
                     <div class=\"row\"><input type=\"text\" id=\"name\" name=\"name\" \
                     value=\"{}\" spellcheck=\"false\" autocomplete=\"off\">\
                     <button type=\"submit\" class=\"primary\">Save here</button></div></form>",
                    field.name(),
                    escape(directory),
                    escape(current),
                );
            }
            Pick::AudioFile | Pick::Program => {}
        }
    }

    body.push_str("<h2>Folders</h2>");
    if listing.folders.is_empty() {
        body.push_str("<p class=\"help\">No folders here.</p>");
    } else {
        body.push_str("<ul class=\"entries\">");
        for folder in &listing.folders {
            let _ = write!(
                body,
                "<li><a class=\"folder\" href=\"{}\">{}</a></li>",
                escape(&browse_link(field, Some(&folder.path))),
                escape(&folder.name)
            );
        }
        body.push_str("</ul>");
    }
    if pick != Pick::Folder && listing.directory.is_some() {
        body.push_str("<h2>Files</h2>");
        if listing.files.is_empty() {
            body.push_str("<p class=\"help\">No files here that this box can use.</p>");
        } else {
            body.push_str("<ul class=\"entries\">");
            for file in &listing.files {
                body.push_str("<li>");
                choose_form(&mut body, field, &file.path, &file.name, "entry");
                body.push_str("</li>");
            }
            body.push_str("</ul>");
        }
    }
    if listing.truncated {
        let _ = write!(
            body,
            "<p class=\"notice\">Only the first {MAX_ENTRIES} entries are shown.</p>"
        );
    }
    layout(app, None, &format!("Choose {}", field.label()), &body)
}

/// Shown to a browser that arrives without the session cookie: another local
/// user, another site, or an old bookmark from a previous launch.
pub fn forbidden() -> String {
    "<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">\
     <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\
     <title>Playlist Generator</title><link rel=\"stylesheet\" href=\"/style.css\">\
     </head><body><main class=\"form\"><h1>Open the link from the console</h1>\
     <p class=\"intro\">Playlist Generator only answers the browser it opened. Use \
     the address printed in the window where it is running; every launch prints a \
     new one.</p></main></body></html>"
        .to_owned()
}

pub fn stopped() -> String {
    "<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">\
     <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\
     <title>Playlist Generator</title><link rel=\"stylesheet\" href=\"/style.css\">\
     </head><body><main class=\"form\"><h1>Playlist Generator has stopped</h1>\
     <p class=\"intro\">Any run in progress was stopped and its finished files were \
     kept. You can close this tab.</p></main></body></html>"
        .to_owned()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::browse::Entry;

    #[test]
    fn every_control_explains_itself() {
        let app = App::default();
        let page = create(&app);
        for field in [Field::Source, Field::Special, Field::PlaylistOutput] {
            assert!(page.contains(field.label()), "create lacks {field:?}");
            assert!(
                page.contains(help(field)),
                "create lacks help for {field:?}"
            );
        }
        assert!(page.contains("How often to repeat it"));
        assert!(page.contains("only read, never changed"));
        let page = normalize(&app);
        for field in [
            Field::NormalizeSource,
            Field::NormalizeOutput,
            Field::Ffmpeg,
        ] {
            assert!(page.contains(field.label()), "normalize lacks {field:?}");
            assert!(
                page.contains(help(field)),
                "normalize lacks help for {field:?}"
            );
        }
        assert!(page.contains("Files at a time"));
        assert!(page.contains("needs FFmpeg installed"));
    }

    #[test]
    fn the_action_button_comes_before_every_browse_button() {
        // Enter in a text box presses the form's first submit button.
        for page in [create(&App::default()), normalize(&App::default())] {
            let action = page.find("value=\"start\"");
            let browse = page.find("name=\"browse\"");
            assert!(action.is_some() && action < browse);
        }
    }

    #[test]
    fn typed_paths_are_escaped_into_their_boxes() {
        let mut app = App::default();
        app.source = "\"><script>alert(1)</script>".into();
        let page = create(&app);
        assert!(!page.contains("<script>"));
        assert!(page.contains("value=\"&quot;&gt;&lt;script&gt;"));
    }

    #[test]
    fn the_navigation_marks_the_page_that_is_open() {
        let app = App::default();
        for (html, page) in [
            (create(&app), Page::Create),
            (normalize(&app), Page::Normalize),
            (activity(&app), Page::Activity),
            (about(&app), Page::About),
        ] {
            for other in Page::ORDER {
                assert!(html.contains(&format!("href=\"{}\"", other.path())));
            }
            assert!(html.contains(&format!("href=\"{}\" aria-current=\"page\"", page.path())));
            assert_eq!(html.matches("aria-current").count(), 1);
            assert!(html.contains("action=\"/quit\""));
        }
    }

    #[test]
    fn forms_lock_and_activity_refreshes_only_while_a_run_is_in_progress() {
        let mut app = App::default();
        assert!(!create(&app).contains("<fieldset disabled>"));
        assert!(!activity(&app).contains("http-equiv=\"refresh\""));
        assert!(activity(&app).contains("Nothing has run yet."));
        app.busy = true;
        assert!(create(&app).contains("<fieldset disabled>"));
        assert!(normalize(&app).contains("<fieldset disabled>"));
        assert!(activity(&app).contains("http-equiv=\"refresh\""));
        assert!(!create(&app).contains("http-equiv=\"refresh\""));
        app.busy = false;
    }

    #[test]
    fn progress_reports_its_counts_and_the_log_reads_newest_first() {
        let mut app = App::default();
        app.progress = Some(OperationEvent {
            action: playlist_generator_core::NormalizationAction::Encoding,
            path: None,
            completed: 3,
            total: 10,
            normalized: 2,
            skipped: 1,
            failed: 0,
            message: String::new(),
        });
        app.log("older");
        app.log("newer <b>");
        let page = activity(&app);
        assert!(page.contains("<progress max=\"10\" value=\"3\""));
        assert!(page.contains("3 of 10 files"));
        assert!(page.contains("2 copied"));
        assert!(page.contains("1 skipped"));
        let newer = page.find("newer &lt;b&gt;");
        assert!(newer.is_some() && newer < page.find("<li>older</li>"));
    }

    #[test]
    fn legal_documents_are_folded_behind_their_headings() {
        let page = about(&App::default());
        assert!(page.contains(&format!("Version {}", env!("CARGO_PKG_VERSION"))));
        assert!(page.contains(REPOSITORY));
        assert!(page.contains("<details><summary>License</summary>"));
        assert!(page.contains("<details><summary>Third-party notices</summary>"));
        assert!(!page.contains("<details open"));
    }

    #[test]
    fn the_browser_offers_what_the_field_needs() {
        let app = App::default();
        let listing = Listing {
            directory: Some("/music".into()),
            parent: Some("/".into()),
            folders: vec![Entry {
                name: "Rock & Roll".into(),
                path: "/music/Rock & Roll".into(),
            }],
            files: vec![Entry {
                name: "id.mp3".into(),
                path: "/music/id.mp3".into(),
            }],
            truncated: false,
            error: None,
        };
        let folder = browse(&app, Field::Source, &listing);
        assert!(folder.contains("Use this folder"));
        assert!(folder.contains("Up one folder"));
        assert!(folder.contains("dir=%2Fmusic%2FRock%20%26%20Roll"));
        assert!(!folder.contains("id.mp3"));
        let file = browse(&app, Field::Special, &listing);
        assert!(!file.contains("Use this folder"));
        assert!(file.contains("value=\"/music/id.mp3\""));
        let save = browse(&app, Field::PlaylistOutput, &listing);
        assert!(save.contains("Save here"));
        assert!(save.contains("value=\"playlist.m3u8\""));
    }
}
