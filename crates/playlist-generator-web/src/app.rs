//! Interaction state for the web frontend.
//!
//! Nothing here touches a socket. [`crate::server`] feeds form posts in and
//! [`crate::pages`] renders from the same state, so the whole frontend is
//! exercised by the tests without a browser.

use std::collections::VecDeque;
use std::path::PathBuf;
use std::sync::mpsc::{self, Receiver, Sender};
use std::thread::{self, JoinHandle};

use playlist_generator_core::{
    GenerateRequest, NormalizeRequest, NormalizeResult, OperationEvent, RunControl,
    SystemProcessRunner, default_jobs, generate, normalize,
};

pub const REPOSITORY: &str = "https://github.com/MatthewDavidMiller/playlist_generator";
const MAX_LOG_ENTRIES: usize = 500;

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Page {
    Create,
    Normalize,
    Activity,
    About,
}

impl Page {
    pub const ORDER: [Self; 4] = [Self::Create, Self::Normalize, Self::Activity, Self::About];

    pub const fn title(self) -> &'static str {
        match self {
            Self::Create => "Create Playlist",
            Self::Normalize => "Normalize Volume",
            Self::Activity => "Activity",
            Self::About => "About",
        }
    }

    pub const fn path(self) -> &'static str {
        match self {
            Self::Create => "/create",
            Self::Normalize => "/normalize",
            Self::Activity => "/activity",
            Self::About => "/about",
        }
    }
}

/// What the folder browser offers for a field.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Pick {
    Folder,
    AudioFile,
    /// A playlist to write: a folder and a file name, or an existing playlist
    /// to replace.
    Playlist,
    /// Any file, because an executable has no extension to recognise on Linux.
    Program,
}

/// Every path box on the two form pages.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Field {
    Source,
    Special,
    PlaylistOutput,
    NormalizeSource,
    NormalizeOutput,
    Ffmpeg,
}

impl Field {
    pub const ALL: [Self; 6] = [
        Self::Source,
        Self::Special,
        Self::PlaylistOutput,
        Self::NormalizeSource,
        Self::NormalizeOutput,
        Self::Ffmpeg,
    ];

    /// The form and query-string key.
    pub const fn name(self) -> &'static str {
        match self {
            Self::Source => "source",
            Self::Special => "special",
            Self::PlaylistOutput => "playlist_output",
            Self::NormalizeSource => "normalize_source",
            Self::NormalizeOutput => "normalize_output",
            Self::Ffmpeg => "ffmpeg",
        }
    }

    pub fn from_name(name: &str) -> Option<Self> {
        Self::ALL.into_iter().find(|field| field.name() == name)
    }

    pub const fn label(self) -> &'static str {
        match self {
            Self::Source | Self::NormalizeSource => "Music folder",
            Self::Special => "Repeated track",
            Self::PlaylistOutput => "Save the playlist as",
            Self::NormalizeOutput => "Save the copies to",
            Self::Ffmpeg => "FFmpeg program (optional)",
        }
    }

    pub const fn pick(self) -> Pick {
        match self {
            Self::Source | Self::NormalizeSource | Self::NormalizeOutput => Pick::Folder,
            Self::Special => Pick::AudioFile,
            Self::PlaylistOutput => Pick::Playlist,
            Self::Ffmpeg => Pick::Program,
        }
    }

    pub const fn page(self) -> Page {
        match self {
            Self::Source | Self::Special | Self::PlaylistOutput => Page::Create,
            Self::NormalizeSource | Self::NormalizeOutput | Self::Ffmpeg => Page::Normalize,
        }
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

/// The number a count box holds, or `0` when it is empty or does not fit a
/// `usize`. Core validates the range and reports the failure in its own words,
/// so an out-of-range value is deliberately passed through rather than
/// silently clamped here.
fn number(text: &str) -> usize {
    text.trim().parse().unwrap_or_default()
}

pub struct App {
    pub source: String,
    pub special: String,
    pub insert_every: String,
    pub playlist_output: String,
    pub normalize_source: String,
    pub normalize_output: String,
    pub ffmpeg: String,
    pub jobs: String,
    pub busy: bool,
    pub progress: Option<OperationEvent>,
    pub activity: VecDeque<String>,
    control: Option<RunControl>,
    operation_thread: Option<JoinHandle<()>>,
    tx: Sender<Message>,
    rx: Receiver<Message>,
}

impl Default for App {
    fn default() -> Self {
        let (tx, rx) = mpsc::channel();
        Self {
            source: String::new(),
            special: String::new(),
            insert_every: "4".into(),
            playlist_output: String::new(),
            normalize_source: String::new(),
            normalize_output: String::new(),
            ffmpeg: String::new(),
            jobs: default_jobs().to_string(),
            busy: false,
            progress: None,
            activity: VecDeque::new(),
            control: None,
            operation_thread: None,
            tx,
            rx,
        }
    }
}

impl App {
    pub fn log(&mut self, message: impl Into<String>) {
        if self.activity.len() == MAX_LOG_ENTRIES {
            self.activity.pop_front();
        }
        self.activity.push_back(message.into());
    }

    pub fn value(&self, field: Field) -> &str {
        match field {
            Field::Source => &self.source,
            Field::Special => &self.special,
            Field::PlaylistOutput => &self.playlist_output,
            Field::NormalizeSource => &self.normalize_source,
            Field::NormalizeOutput => &self.normalize_output,
            Field::Ffmpeg => &self.ffmpeg,
        }
    }

    fn slot(&mut self, field: Field) -> &mut String {
        match field {
            Field::Source => &mut self.source,
            Field::Special => &mut self.special,
            Field::PlaylistOutput => &mut self.playlist_output,
            Field::NormalizeSource => &mut self.normalize_source,
            Field::NormalizeOutput => &mut self.normalize_output,
            Field::Ffmpeg => &mut self.ffmpeg,
        }
    }

    /// Stores what was typed into a box. A run in flight was started from the
    /// values on screen, so the boxes stay as they were until it finishes.
    pub fn set(&mut self, field: Field, value: &str) {
        if !self.busy {
            value.clone_into(self.slot(field));
        }
    }

    pub fn set_counts(&mut self, insert_every: Option<&str>, jobs: Option<&str>) {
        if self.busy {
            return;
        }
        if let Some(value) = insert_every {
            value.clone_into(&mut self.insert_every);
        }
        if let Some(value) = jobs {
            value.clone_into(&mut self.jobs);
        }
    }

    /// Stores a path chosen in the folder browser. The music folder is usually
    /// the same on both pages, so choosing it on one fills in the other when
    /// that one is still empty.
    pub fn choose(&mut self, field: Field, value: &str) {
        self.set(field, value);
        if field == Field::Source && self.normalize_source.is_empty() {
            self.set(Field::NormalizeSource, value);
        }
    }

    pub fn is_paused(&self) -> bool {
        self.control.as_ref().is_some_and(RunControl::is_paused)
    }

    /// Whether the current run can be paused and stopped. Playlist creation
    /// is one quick pass and offers neither.
    pub fn is_controllable(&self) -> bool {
        self.control.is_some()
    }

    /// Drains everything the worker thread has sent. Called before every
    /// response, so each page shows the latest progress.
    pub fn receive(&mut self) {
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

    pub fn pause(&mut self) {
        if let Some(control) = self.control.clone()
            && !control.is_paused()
        {
            control.pause();
            self.log("Pause requested");
        }
    }

    pub fn resume(&mut self) {
        if let Some(control) = self.control.clone()
            && control.is_paused()
        {
            control.resume();
            self.log("Resumed");
        }
    }

    pub fn stop(&mut self) {
        if let Some(control) = self.control.clone() {
            control.cancel();
            self.log("Stop requested");
        }
    }

    /// Starts playlist creation. Returns `false` when a run already holds the
    /// worker, which the forms prevent but a second tab could still ask for.
    pub fn start_generate(&mut self) -> bool {
        if self.busy {
            return false;
        }
        let request = GenerateRequest {
            source_directory: PathBuf::from(self.source.trim()),
            special_file: PathBuf::from(self.special.trim()),
            insert_every: number(&self.insert_every),
            output_path: PathBuf::from(self.playlist_output.trim()),
        };
        self.busy = true;
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
        true
    }

    pub fn start_normalize(&mut self) -> bool {
        if self.busy {
            return false;
        }
        let ffmpeg = self.ffmpeg.trim();
        let request = NormalizeRequest {
            source_directory: PathBuf::from(self.normalize_source.trim()),
            output_directory: PathBuf::from(self.normalize_output.trim()),
            ffmpeg: (!ffmpeg.is_empty()).then(|| PathBuf::from(ffmpeg)),
            jobs: number(&self.jobs),
        };
        let control = RunControl::default();
        self.control = Some(control.clone());
        self.busy = true;
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
        true
    }

    /// Cancels any run and waits for it to finish unwinding.
    ///
    /// Normalization writes to a same-directory temporary file and contains
    /// `FFmpeg` in a process group; both are cleaned up as the worker unwinds.
    /// Exiting without waiting would leave a partial `.opus` file behind and
    /// an `FFmpeg` that outlives the server.
    pub fn shutdown(&mut self) {
        if let Some(control) = &self.control {
            control.cancel();
        }
        self.join_worker();
        self.receive();
    }
}

impl Drop for App {
    fn drop(&mut self) {
        self.shutdown();
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;
    use std::time::{Duration, Instant};

    /// Waits for the worker to report that it finished.
    pub(crate) fn settle(app: &mut App) {
        let deadline = Instant::now() + Duration::from_secs(10);
        while app.busy && Instant::now() < deadline {
            app.receive();
            thread::sleep(Duration::from_millis(5));
        }
        assert!(!app.busy, "the run never finished");
    }

    #[test]
    fn every_field_round_trips_through_its_form_name() {
        for field in Field::ALL {
            assert_eq!(Field::from_name(field.name()), Some(field));
        }
        assert_eq!(Field::from_name("jobs"), None);
    }

    #[test]
    fn choosing_the_music_folder_fills_the_empty_one_on_the_other_page() {
        let mut app = App::default();
        app.choose(Field::Source, "/music");
        assert_eq!(app.normalize_source, "/music");
        app.choose(Field::Source, "/elsewhere");
        assert_eq!(app.source, "/elsewhere");
        assert_eq!(app.normalize_source, "/music");
    }

    #[test]
    fn a_failed_run_reaches_the_log_and_releases_the_controls() {
        let mut app = App::default();
        // Every path is empty, so core refuses this before anything is read.
        assert!(app.start_generate());
        assert!(app.busy);
        assert!(!app.start_generate(), "a second run started over the first");
        settle(&mut app);
        assert_eq!(
            app.activity.front().map(String::as_str),
            Some("Creating playlist…")
        );
        assert!(
            app.activity
                .back()
                .is_some_and(|line| line.starts_with("Playlist failed:"))
        );
    }

    #[test]
    fn boxes_keep_the_values_a_running_job_started_from() {
        let mut app = App::default();
        app.busy = true;
        app.set(Field::Source, "/changed");
        app.set_counts(Some("9"), Some("9"));
        assert_eq!(app.source, "");
        assert_eq!(app.insert_every, "4");
    }

    #[test]
    fn a_stopped_normalization_is_not_called_finished() {
        let summary = NormalizeResult {
            source_directory: PathBuf::new(),
            output_directory: PathBuf::new(),
            normalized_file_count: 1,
            skipped_file_count: 2,
            failed_file_count: 0,
            failures: Vec::new(),
            stopped: true,
        };
        assert_eq!(
            normalize_summary(&summary),
            "Normalization stopped: 1 normalized, 2 skipped, 0 failed"
        );
    }

    #[test]
    fn the_log_keeps_only_its_newest_lines() {
        let mut app = App::default();
        for line in 0..=MAX_LOG_ENTRIES {
            app.log(line.to_string());
        }
        assert_eq!(app.activity.len(), MAX_LOG_ENTRIES);
        assert_eq!(app.activity.front().map(String::as_str), Some("1"));
    }
}
