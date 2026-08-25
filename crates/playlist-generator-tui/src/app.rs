//! Interaction state for the terminal frontend.
//!
//! Nothing here touches a terminal. The event loop in `main` feeds key events
//! in and reads state out, and [`crate::ui`] draws from the same state, so the
//! whole frontend is exercised by the tests below and by
//! [`crate::ui`]'s without a tty ever being opened.

use std::collections::VecDeque;
use std::path::PathBuf;
use std::sync::mpsc::{self, Receiver, Sender};
use std::thread::{self, JoinHandle};

use crossterm::event::{KeyCode, KeyEvent, KeyModifiers};
use playlist_generator_core::{
    GenerateRequest, NormalizeRequest, NormalizeResult, OperationEvent, RunControl,
    SystemProcessRunner, default_jobs, generate, normalize,
};

pub(crate) const REPOSITORY: &str = "https://github.com/MatthewDavidMiller/playlist_generator";
const MAX_LOG_ENTRIES: usize = 500;

/// Which of the two folded legal documents are open. Both run to thousands of
/// lines, so neither is laid out until somebody asks for it.
#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub(crate) struct Documents {
    pub(crate) license: bool,
    pub(crate) notices: bool,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub(crate) enum Page {
    Create,
    Normalize,
    Activity,
    About,
}

impl Page {
    pub(crate) const ORDER: [Self; 4] =
        [Self::Create, Self::Normalize, Self::Activity, Self::About];

    pub(crate) const fn title(self) -> &'static str {
        match self {
            Self::Create => "Create Playlist",
            Self::Normalize => "Normalize Volume",
            Self::Activity => "Activity",
            Self::About => "About",
        }
    }

    /// How many things on the page can hold focus. The last one is the action
    /// button; the pages without one report zero and are driven by their own
    /// keys instead.
    pub(crate) const fn focus_count(self) -> usize {
        match self {
            Self::Create | Self::Normalize => 5,
            Self::Activity | Self::About => 0,
        }
    }
}

/// A single line of editable text with a cursor.
///
/// The cursor is a character index rather than a byte offset: paths are the
/// only thing typed here and they are routinely not ASCII, so counting bytes
/// would put the cursor inside a character.
#[derive(Clone, Debug, Default)]
pub(crate) struct TextField {
    pub(crate) value: String,
    pub(crate) cursor: usize,
}

impl TextField {
    fn byte_offset(&self) -> usize {
        self.value
            .char_indices()
            .nth(self.cursor)
            .map_or(self.value.len(), |(offset, _)| offset)
    }

    pub(crate) fn char_count(&self) -> usize {
        self.value.chars().count()
    }

    fn insert(&mut self, character: char) {
        let offset = self.byte_offset();
        self.value.insert(offset, character);
        self.cursor += 1;
    }

    fn backspace(&mut self) {
        if self.cursor == 0 {
            return;
        }
        self.cursor -= 1;
        let offset = self.byte_offset();
        self.value.remove(offset);
    }

    fn delete(&mut self) {
        if self.cursor < self.char_count() {
            let offset = self.byte_offset();
            self.value.remove(offset);
        }
    }

    fn left(&mut self) {
        self.cursor = self.cursor.saturating_sub(1);
    }

    fn right(&mut self) {
        self.cursor = (self.cursor + 1).min(self.char_count());
    }

    fn home(&mut self) {
        self.cursor = 0;
    }

    fn end(&mut self) {
        self.cursor = self.char_count();
    }

    fn set(&mut self, value: impl Into<String>) {
        self.value = value.into();
        self.cursor = self.char_count();
    }

    /// The number this field holds, or `0` when it is empty or does not fit a
    /// `usize`. Core validates the range and reports the failure in its own
    /// words, so an out-of-range value is deliberately passed through rather
    /// than silently clamped here.
    pub(crate) fn number(&self) -> usize {
        self.value.trim().parse().unwrap_or_default()
    }
}

enum Message {
    Progress(Box<OperationEvent>),
    Finished(String),
}

/// How a completed normalization is announced in the log.
///
/// A stopped run is not a finished one. Core reports the difference and the
/// command-line build exits 130 for it, so calling it "finished" here would be
/// the one place that told the user their cancellation had not happened.
fn normalize_summary(summary: &NormalizeResult) -> String {
    let outcome = if summary.stopped {
        "Normalization stopped"
    } else {
        "Normalization finished"
    };
    format!(
        "{outcome}: {} normalized, {} skipped, {} failed",
        summary.normalized_file_count, summary.skipped_file_count, summary.failed_file_count
    )
}

pub(crate) struct App {
    pub(crate) page: Page,
    pub(crate) focus: usize,
    pub(crate) source: TextField,
    pub(crate) special: TextField,
    pub(crate) insert_every: TextField,
    pub(crate) playlist_output: TextField,
    pub(crate) normalize_source: TextField,
    pub(crate) normalize_output: TextField,
    pub(crate) ffmpeg: TextField,
    pub(crate) jobs: TextField,
    pub(crate) busy: bool,
    pub(crate) progress: Option<OperationEvent>,
    pub(crate) activity: VecDeque<String>,
    pub(crate) documents: Documents,
    pub(crate) about_scroll: u16,
    pub(crate) should_quit: bool,
    control: Option<RunControl>,
    operation_thread: Option<JoinHandle<()>>,
    tx: Sender<Message>,
    rx: Receiver<Message>,
}

impl Default for App {
    fn default() -> Self {
        let (tx, rx) = mpsc::channel();
        let mut insert_every = TextField::default();
        insert_every.set("4");
        let mut jobs = TextField::default();
        jobs.set(default_jobs().to_string());
        Self {
            page: Page::Create,
            focus: 0,
            source: TextField::default(),
            special: TextField::default(),
            insert_every,
            playlist_output: TextField::default(),
            normalize_source: TextField::default(),
            normalize_output: TextField::default(),
            ffmpeg: TextField::default(),
            jobs,
            busy: false,
            progress: None,
            activity: VecDeque::new(),
            documents: Documents::default(),
            about_scroll: 0,
            should_quit: false,
            control: None,
            operation_thread: None,
            tx,
            rx,
        }
    }
}

impl App {
    pub(crate) fn log(&mut self, message: impl Into<String>) {
        if self.activity.len() == MAX_LOG_ENTRIES {
            self.activity.pop_front();
        }
        self.activity.push_back(message.into());
    }

    pub(crate) fn is_running(&self) -> bool {
        self.busy
    }

    pub(crate) fn is_paused(&self) -> bool {
        self.control.as_ref().is_some_and(RunControl::is_paused)
    }

    /// Drain everything the worker thread has sent. Called once per event-loop
    /// pass, so progress keeps arriving while the user is typing.
    pub(crate) fn receive(&mut self) {
        while let Ok(message) = self.rx.try_recv() {
            match message {
                Message::Progress(progress) => {
                    self.log(progress.message.clone());
                    self.progress = Some(*progress);
                }
                Message::Finished(message) => {
                    self.log(message);
                    self.busy = false;
                    self.control = None;
                    self.join_worker();
                }
            }
        }
    }

    fn join_worker(&mut self) {
        if let Some(worker) = self.operation_thread.take() {
            drop(worker.join());
        }
    }

    /// The field the focus index currently names, or `None` when it names the
    /// action button.
    fn focused_field(&mut self) -> Option<&mut TextField> {
        match (self.page, self.focus) {
            (Page::Create, 0) => Some(&mut self.source),
            (Page::Create, 1) => Some(&mut self.special),
            (Page::Create, 2) => Some(&mut self.insert_every),
            (Page::Create, 3) => Some(&mut self.playlist_output),
            (Page::Normalize, 0) => Some(&mut self.normalize_source),
            (Page::Normalize, 1) => Some(&mut self.normalize_output),
            (Page::Normalize, 2) => Some(&mut self.ffmpeg),
            (Page::Normalize, 3) => Some(&mut self.jobs),
            _ => None,
        }
    }

    /// Whether the focused field only accepts digits. Both numeric fields are
    /// counts, and a typed letter in one would only surface much later as a
    /// validation failure from core.
    fn focus_is_numeric(&self) -> bool {
        matches!(
            (self.page, self.focus),
            (Page::Create, 2) | (Page::Normalize, 3)
        )
    }

    fn on_action_button(&self) -> bool {
        self.focus + 1 == self.page.focus_count() && self.page.focus_count() > 0
    }

    fn select_page(&mut self, page: Page) {
        self.page = page;
        self.focus = 0;
    }

    fn cycle_focus(&mut self, forward: bool) {
        let count = self.page.focus_count();
        if count == 0 {
            return;
        }
        self.focus = if forward {
            (self.focus + 1) % count
        } else {
            (self.focus + count - 1) % count
        };
    }

    pub(crate) fn on_key(&mut self, key: KeyEvent) {
        let control = key.modifiers.contains(KeyModifiers::CONTROL);
        let alt = key.modifiers.contains(KeyModifiers::ALT);

        // Quit and page switching are modified so that they cannot collide
        // with text entry, which is always live on a focused field.
        if control && matches!(key.code, KeyCode::Char('q' | 'c')) {
            self.request_quit();
            return;
        }
        if alt {
            match key.code {
                KeyCode::Char('1') => self.select_page(Page::Create),
                KeyCode::Char('2') => self.select_page(Page::Normalize),
                KeyCode::Char('3') => self.select_page(Page::Activity),
                KeyCode::Char('4') => self.select_page(Page::About),
                KeyCode::Left | KeyCode::Right => self.step_page(key.code == KeyCode::Right),
                _ => {}
            }
            return;
        }

        match self.page {
            Page::Create | Page::Normalize => self.on_form_key(key),
            Page::Activity => self.on_activity_key(key),
            Page::About => self.on_about_key(key),
        }
    }

    fn step_page(&mut self, forward: bool) {
        let current = Page::ORDER.iter().position(|page| *page == self.page);
        let index = current.unwrap_or_default();
        let count = Page::ORDER.len();
        let next = if forward {
            (index + 1) % count
        } else {
            (index + count - 1) % count
        };
        if let Some(page) = Page::ORDER.get(next) {
            self.select_page(*page);
        }
    }

    fn request_quit(&mut self) {
        // A run in flight owns temporary files and an FFmpeg process group.
        // Cancelling lets its cleanup finish; `shutdown` then waits for it.
        if let Some(control) = &self.control {
            control.cancel();
        }
        self.should_quit = true;
    }

    fn on_form_key(&mut self, key: KeyEvent) {
        match key.code {
            KeyCode::Tab | KeyCode::Down => self.cycle_focus(true),
            KeyCode::BackTab | KeyCode::Up => self.cycle_focus(false),
            KeyCode::Enter => {
                if self.on_action_button() && !self.busy {
                    match self.page {
                        Page::Create => self.start_generate(),
                        Page::Normalize => self.start_normalize(),
                        Page::Activity | Page::About => {}
                    }
                }
            }
            KeyCode::Char(character) => {
                if self.busy {
                    return;
                }
                let numeric = self.focus_is_numeric();
                if let Some(field) = self.focused_field()
                    && (!numeric || character.is_ascii_digit())
                {
                    field.insert(character);
                }
            }
            KeyCode::Backspace => {
                if !self.busy
                    && let Some(field) = self.focused_field()
                {
                    field.backspace();
                }
            }
            KeyCode::Delete => {
                if !self.busy
                    && let Some(field) = self.focused_field()
                {
                    field.delete();
                }
            }
            KeyCode::Left => {
                if let Some(field) = self.focused_field() {
                    field.left();
                }
            }
            KeyCode::Right => {
                if let Some(field) = self.focused_field() {
                    field.right();
                }
            }
            KeyCode::Home => {
                if let Some(field) = self.focused_field() {
                    field.home();
                }
            }
            KeyCode::End => {
                if let Some(field) = self.focused_field() {
                    field.end();
                }
            }
            _ => {}
        }
    }

    fn on_activity_key(&mut self, key: KeyEvent) {
        // No field holds focus on this page, so unmodified letters are free to
        // drive the run itself.
        let Some(control) = self.control.clone() else {
            return;
        };
        match key.code {
            KeyCode::Char('p') => {
                if control.is_paused() {
                    control.resume();
                    self.log("Resumed");
                } else {
                    control.pause();
                    self.log("Pause requested");
                }
            }
            KeyCode::Char('s') => {
                control.cancel();
                self.log("Stop requested");
            }
            _ => {}
        }
    }

    fn on_about_key(&mut self, key: KeyEvent) {
        match key.code {
            KeyCode::Char('l') => {
                self.documents.license = !self.documents.license;
                self.about_scroll = 0;
            }
            KeyCode::Char('n') => {
                self.documents.notices = !self.documents.notices;
                self.about_scroll = 0;
            }
            KeyCode::Down => self.about_scroll = self.about_scroll.saturating_add(1),
            KeyCode::Up => self.about_scroll = self.about_scroll.saturating_sub(1),
            KeyCode::PageDown => self.about_scroll = self.about_scroll.saturating_add(20),
            KeyCode::PageUp => self.about_scroll = self.about_scroll.saturating_sub(20),
            KeyCode::Home => self.about_scroll = 0,
            _ => {}
        }
    }

    fn start_generate(&mut self) {
        let request = GenerateRequest {
            source_directory: PathBuf::from(self.source.value.trim()),
            special_file: PathBuf::from(self.special.value.trim()),
            insert_every: self.insert_every.number(),
            output_path: PathBuf::from(self.playlist_output.value.trim()),
        };
        self.busy = true;
        // Progress and messages only exist on Activity, so a run started from
        // the form would otherwise look like nothing had happened.
        self.select_page(Page::Activity);
        self.progress = None;
        self.log("Creating playlist…");
        let tx = self.tx.clone();
        self.operation_thread = Some(thread::spawn(move || {
            let message = generate(&request).map_or_else(
                |error| format!("Playlist failed: {error}"),
                |result| {
                    format!(
                        "Created '{}' with {} entries",
                        result.output_path.display(),
                        result.playlist_entry_count
                    )
                },
            );
            drop(tx.send(Message::Finished(message)));
        }));
    }

    fn start_normalize(&mut self) {
        let ffmpeg = self.ffmpeg.value.trim();
        let request = NormalizeRequest {
            source_directory: PathBuf::from(self.normalize_source.value.trim()),
            output_directory: PathBuf::from(self.normalize_output.value.trim()),
            ffmpeg: (!ffmpeg.is_empty()).then(|| PathBuf::from(ffmpeg)),
            jobs: self.jobs.number(),
        };
        let control = RunControl::default();
        self.control = Some(control.clone());
        self.busy = true;
        self.select_page(Page::Activity);
        self.progress = None;
        self.log("Starting normalization…");
        let tx = self.tx.clone();
        self.operation_thread = Some(thread::spawn(move || {
            let progress_tx = tx.clone();
            let result = normalize(&request, &SystemProcessRunner, &control, &move |progress| {
                drop(progress_tx.send(Message::Progress(Box::new(progress))));
            });
            let message = result.map_or_else(
                |error| format!("Normalization failed: {error}"),
                |summary| normalize_summary(&summary),
            );
            drop(tx.send(Message::Finished(message)));
        }));
    }

    /// Wait for a cancelled run to finish unwinding.
    ///
    /// Normalization writes to a same-directory temporary file and contains
    /// `FFmpeg` in a process group; both are cleaned up as the worker unwinds.
    /// Exiting without waiting would leave a partial `.opus` file behind and
    /// orphan the process group.
    pub(crate) fn shutdown(&mut self) {
        if let Some(control) = &self.control {
            control.cancel();
        }
        self.join_worker();
    }
}

impl Drop for App {
    fn drop(&mut self) {
        self.shutdown();
    }
}

#[cfg(test)]
mod tests {
    use std::path::PathBuf;

    use playlist_generator_core::NormalizeResult;

    use super::{App, KeyCode, KeyEvent, KeyModifiers, Page, TextField, normalize_summary};

    fn summary(stopped: bool) -> NormalizeResult {
        NormalizeResult {
            source_directory: PathBuf::from("/src"),
            output_directory: PathBuf::from("/out"),
            normalized_file_count: 3,
            skipped_file_count: 1,
            failed_file_count: 0,
            failures: Vec::new(),
            stopped,
        }
    }

    #[test]
    fn a_stopped_run_is_not_reported_as_a_finished_one() {
        let finished = normalize_summary(&summary(false));
        let stopped = normalize_summary(&summary(true));
        assert!(finished.starts_with("Normalization finished"), "{finished}");
        assert!(stopped.starts_with("Normalization stopped"), "{stopped}");
        // Both still carry the counts; only the verdict differs.
        for message in [&finished, &stopped] {
            assert!(
                message.ends_with("3 normalized, 1 skipped, 0 failed"),
                "{message}"
            );
        }
    }

    fn press(app: &mut App, code: KeyCode) {
        app.on_key(KeyEvent::new(code, KeyModifiers::NONE));
    }

    fn type_text(app: &mut App, text: &str) {
        for character in text.chars() {
            press(app, KeyCode::Char(character));
        }
    }

    #[test]
    fn a_text_field_edits_around_multi_byte_characters() {
        let mut field = TextField::default();
        for character in "Música".chars() {
            field.insert(character);
        }
        assert_eq!(field.value, "Música");
        assert_eq!(field.cursor, 6);

        // Character 1 is 'ú', which occupies bytes 1 and 2. A cursor counted in
        // bytes would put this backspace inside the character: the whole two
        // bytes have to go, and nothing after them may shift.
        field.home();
        field.right();
        field.right();
        field.backspace();
        assert_eq!(field.value, "Msica");
        assert_eq!(field.cursor, 1);

        // The same boundary from the other side.
        field.set("Música");
        field.home();
        field.right();
        field.delete();
        assert_eq!(field.value, "Msica");

        // And inserting straight after one.
        field.set("Música");
        field.home();
        field.right();
        field.right();
        field.insert('!');
        assert_eq!(field.value, "Mú!sica");

        field.end();
        field.backspace();
        assert_eq!(field.value, "Mú!sic");
    }

    #[test]
    fn typing_reaches_the_focused_field_and_only_that_one() {
        let mut app = App::default();
        type_text(&mut app, "/music");
        assert_eq!(app.source.value, "/music");
        assert_eq!(app.special.value, "");

        press(&mut app, KeyCode::Tab);
        type_text(&mut app, "/jingle.mp3");
        assert_eq!(app.source.value, "/music");
        assert_eq!(app.special.value, "/jingle.mp3");
    }

    #[test]
    fn the_count_fields_refuse_anything_that_is_not_a_digit() {
        let mut app = App::default();
        app.focus = 2;
        press(&mut app, KeyCode::End);
        type_text(&mut app, "2x");
        assert_eq!(app.insert_every.value, "42");
        assert_eq!(app.insert_every.number(), 42);

        app.page = Page::Normalize;
        app.focus = 3;
        app.jobs.set("");
        type_text(&mut app, "1a6");
        assert_eq!(app.jobs.value, "16");
    }

    #[test]
    fn focus_wraps_through_every_control_and_ends_on_the_button() {
        let mut app = App::default();
        assert_eq!(Page::Create.focus_count(), 5);
        for expected in [1, 2, 3, 4, 0] {
            press(&mut app, KeyCode::Tab);
            assert_eq!(app.focus, expected);
        }
        press(&mut app, KeyCode::BackTab);
        assert_eq!(app.focus, 4);
        // The last stop is the action button, so no field takes the keystroke.
        type_text(&mut app, "z");
        assert_eq!(app.playlist_output.value, "");
    }

    #[test]
    fn every_page_is_reachable_by_its_own_shortcut_and_by_stepping() {
        let mut app = App::default();
        for (digit, expected) in [
            ('2', Page::Normalize),
            ('3', Page::Activity),
            ('4', Page::About),
            ('1', Page::Create),
        ] {
            app.on_key(KeyEvent::new(KeyCode::Char(digit), KeyModifiers::ALT));
            assert_eq!(app.page, expected);
        }
        app.on_key(KeyEvent::new(KeyCode::Left, KeyModifiers::ALT));
        assert_eq!(app.page, Page::About);
        app.on_key(KeyEvent::new(KeyCode::Right, KeyModifiers::ALT));
        assert_eq!(app.page, Page::Create);
    }

    #[test]
    fn switching_pages_never_leaves_focus_pointing_past_the_new_page() {
        let mut app = App::default();
        app.focus = 4;
        app.on_key(KeyEvent::new(KeyCode::Char('3'), KeyModifiers::ALT));
        assert_eq!(app.focus, 0);
        // Activity has no focusable control, so cycling must not divide by its
        // count or move focus somewhere that cannot be drawn.
        press(&mut app, KeyCode::Tab);
        assert_eq!(app.focus, 0);
    }

    #[test]
    fn starting_a_run_shows_the_page_that_reports_it() {
        let mut app = App::default();
        app.focus = 4;
        press(&mut app, KeyCode::Enter);
        assert_eq!(app.page, Page::Activity);
        assert!(app.is_running());
        assert!(
            app.activity.iter().any(|line| line.contains("Creating")),
            "the run should announce itself in the log: {:?}",
            app.activity
        );
    }

    #[test]
    fn a_failed_run_reports_itself_and_releases_the_controls() {
        let mut app = App::default();
        // No paths are filled in, so core rejects the request promptly.
        app.focus = 4;
        press(&mut app, KeyCode::Enter);
        app.shutdown();
        app.receive();
        assert!(!app.is_running());
        assert!(
            app.activity.iter().any(|line| line.contains("failed")),
            "the failure should reach the log: {:?}",
            app.activity
        );
    }

    #[test]
    fn the_activity_keys_do_nothing_until_there_is_a_run_to_control() {
        let mut app = App::default();
        app.page = Page::Activity;
        press(&mut app, KeyCode::Char('p'));
        press(&mut app, KeyCode::Char('s'));
        assert!(app.activity.is_empty());
        assert!(!app.is_paused());
    }

    #[test]
    fn the_about_page_folds_its_documents_away_and_scrolls_them() {
        let mut app = App::default();
        app.page = Page::About;
        assert!(!app.documents.license);
        press(&mut app, KeyCode::Char('l'));
        assert!(app.documents.license);
        press(&mut app, KeyCode::PageDown);
        assert_eq!(app.about_scroll, 20);
        press(&mut app, KeyCode::Up);
        assert_eq!(app.about_scroll, 19);
        // Opening the other document starts it from the top rather than at the
        // offset the previous one was left at.
        press(&mut app, KeyCode::Char('n'));
        assert_eq!(app.about_scroll, 0);
        press(&mut app, KeyCode::Up);
        assert_eq!(app.about_scroll, 0);
    }

    #[test]
    fn quitting_is_modified_so_that_it_cannot_be_typed_into_a_path() {
        let mut app = App::default();
        type_text(&mut app, "q");
        assert!(!app.should_quit);
        assert_eq!(app.source.value, "q");
        app.on_key(KeyEvent::new(KeyCode::Char('q'), KeyModifiers::CONTROL));
        assert!(app.should_quit);
    }
}
