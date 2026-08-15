#![forbid(unsafe_code)]

use std::collections::VecDeque;
use std::panic;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{self, Receiver, Sender};
use std::thread::{self, JoinHandle};

use eframe::egui;
use playlist_generator_core::{
    GenerateRequest, NormalizeRequest, OperationEvent, RunControl, SystemProcessRunner,
    default_jobs, generate, normalize,
};

const REPOSITORY: &str = "https://github.com/MatthewDavidMiller/playlist_generator";
const MAX_LOG_ENTRIES: usize = 500;
/// Widest the form is allowed to become. Everything is measured against the
/// form rather than the window, so a maximised window and a half-screen window
/// lay out identically instead of stretching every text box across a monitor.
const FORM_WIDTH: f32 = 620.0;
/// Below this form width the browse button moves underneath its text box.
const NARROW_BREAKPOINT: f32 = 430.0;

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum Page {
    Create,
    Normalize,
    Activity,
    About,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum PickTarget {
    Source,
    Special,
    PlaylistOutput,
    NormalizeSource,
    NormalizeOutput,
    Ffmpeg,
}

enum Message {
    Picked(PickTarget, Option<PathBuf>),
    Progress(OperationEvent),
    Finished(String),
}

pub struct PlaylistGeneratorApp {
    page: Page,
    source: String,
    special: String,
    insert_every: usize,
    playlist_output: String,
    normalize_source: String,
    normalize_output: String,
    ffmpeg: String,
    jobs: usize,
    busy: bool,
    progress: Option<OperationEvent>,
    activity: VecDeque<String>,
    control: Option<RunControl>,
    operation_thread: Option<JoinHandle<()>>,
    painted: bool,
    tx: Sender<Message>,
    rx: Receiver<Message>,
}

impl Default for PlaylistGeneratorApp {
    fn default() -> Self {
        let (tx, rx) = mpsc::channel();
        Self {
            page: Page::Create,
            source: String::new(),
            special: String::new(),
            insert_every: 4,
            playlist_output: String::new(),
            normalize_source: String::new(),
            normalize_output: String::new(),
            ffmpeg: String::new(),
            jobs: default_jobs(),
            busy: false,
            progress: None,
            activity: VecDeque::new(),
            control: None,
            operation_thread: None,
            painted: false,
            tx,
            rx,
        }
    }
}

impl PlaylistGeneratorApp {
    fn log(&mut self, message: impl Into<String>) {
        if self.activity.len() == MAX_LOG_ENTRIES {
            self.activity.pop_front();
        }
        self.activity.push_back(message.into());
    }

    fn receive(&mut self) {
        while let Ok(message) = self.rx.try_recv() {
            match message {
                Message::Picked(target, path) => {
                    if let Some(path) = path {
                        let text = path.display().to_string();
                        match target {
                            PickTarget::Source => {
                                self.source.clone_from(&text);
                                if self.normalize_source.is_empty() {
                                    self.normalize_source.clone_from(&text);
                                }
                            }
                            PickTarget::Special => self.special = text,
                            PickTarget::PlaylistOutput => self.playlist_output = text,
                            PickTarget::NormalizeSource => self.normalize_source = text,
                            PickTarget::NormalizeOutput => self.normalize_output = text,
                            PickTarget::Ffmpeg => self.ffmpeg = text,
                        }
                    }
                }
                Message::Progress(progress) => {
                    self.log(progress.message.clone());
                    self.progress = Some(progress);
                }
                Message::Finished(message) => {
                    self.log(message);
                    self.busy = false;
                    self.control = None;
                    if let Some(worker) = self.operation_thread.take() {
                        let _ = worker.join();
                    }
                }
            }
        }
    }

    fn pick(&self, target: PickTarget, folder: bool, save: bool) {
        let tx = self.tx.clone();
        thread::spawn(move || {
            let dialog = rfd::AsyncFileDialog::new();
            let path = if folder {
                pollster::block_on(dialog.pick_folder()).map(|handle| handle.path().to_path_buf())
            } else if save {
                pollster::block_on(dialog.add_filter("M3U8 playlist", &["m3u8"]).save_file())
                    .map(|handle| handle.path().to_path_buf())
            } else {
                pollster::block_on(dialog.pick_file()).map(|handle| handle.path().to_path_buf())
            };
            let _ = tx.send(Message::Picked(target, path));
        });
    }

    fn start_generate(&mut self) {
        let request = GenerateRequest {
            source_directory: self.source.clone().into(),
            special_file: self.special.clone().into(),
            insert_every: self.insert_every,
            output_path: self.playlist_output.clone().into(),
        };
        self.busy = true;
        // Progress and messages only exist on Activity, so a run that started
        // somewhere else would otherwise look like nothing had happened.
        self.page = Page::Activity;
        self.progress = None;
        self.log("Creating playlist…");
        let tx = self.tx.clone();
        let worker = thread::spawn(move || {
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
            let _ = tx.send(Message::Finished(message));
        });
        self.operation_thread = Some(worker);
    }

    fn start_normalize(&mut self) {
        let request = NormalizeRequest {
            source_directory: self.normalize_source.clone().into(),
            output_directory: self.normalize_output.clone().into(),
            ffmpeg: (!self.ffmpeg.trim().is_empty()).then(|| self.ffmpeg.clone().into()),
            jobs: self.jobs,
        };
        let control = RunControl::default();
        self.control = Some(control.clone());
        self.busy = true;
        self.page = Page::Activity;
        self.progress = None;
        self.log("Starting normalization…");
        let tx = self.tx.clone();
        let worker = thread::spawn(move || {
            let progress_tx = tx.clone();
            let result = normalize(&request, &SystemProcessRunner, &control, &move |progress| {
                let _ = progress_tx.send(Message::Progress(progress));
            });
            let message = result.map_or_else(
                |error| format!("Normalization failed: {error}"),
                |summary| {
                    format!(
                        "Normalization finished: {} normalized, {} skipped, {} failed",
                        summary.normalized_file_count,
                        summary.skipped_file_count,
                        summary.failed_file_count
                    )
                },
            );
            let _ = tx.send(Message::Finished(message));
        });
        self.operation_thread = Some(worker);
    }

    fn nav(&mut self, ui: &mut egui::Ui) {
        ui.horizontal_wrapped(|ui| {
            for (page, label) in [
                (Page::Create, "Create Playlist"),
                (Page::Normalize, "Normalize Volume"),
                (Page::Activity, "Activity"),
                (Page::About, "About"),
            ] {
                if ui.selectable_label(self.page == page, label).clicked() {
                    self.page = page;
                }
            }
        });
    }

    /// Centres one column of controls and reports whether it had to be narrowed.
    ///
    /// Widths are taken from this column and never from the window, so a text
    /// box is the same size in a maximised window as in a small one, and the
    /// reflow happens when the column really is too tight rather than when a
    /// display-scaling setting makes the window report fewer points.
    fn form(ui: &mut egui::Ui, content: impl FnOnce(&mut egui::Ui, bool)) {
        let margin = ui.spacing().item_spacing.x.max(8.0);
        let outer = ui.available_width();
        let width = (outer - margin * 2.0).clamp(0.0, FORM_WIDTH);
        let leading = ((outer - width) * 0.5).max(0.0);
        let narrow = width < NARROW_BREAKPOINT;
        ui.horizontal_top(|ui| {
            ui.add_space(leading);
            ui.allocate_ui_with_layout(
                egui::vec2(width, ui.available_height()),
                egui::Layout::top_down(egui::Align::Min),
                |ui| {
                    ui.set_min_width(width);
                    ui.add_space(margin);
                    content(ui, narrow);
                    ui.add_space(margin);
                },
            );
        });
    }

    fn show_page(&mut self, ui: &mut egui::Ui, narrow: bool) {
        match self.page {
            Page::Create => self.create_page(ui, narrow),
            Page::Normalize => self.normalize_page(ui, narrow),
            Page::Activity => self.activity_page(ui),
            Page::About => Self::about_page(ui),
        }
    }

    /// A labelled path box with its browse button and an explanation.
    ///
    /// The button is placed before the text box in a right-to-left row so it
    /// keeps its own width and the box takes what is left. Filling the row with
    /// the box first pushes the button past the edge of the window instead.
    fn field(ui: &mut egui::Ui, narrow: bool, label: &str, help: &str, value: &mut String) -> bool {
        ui.label(egui::RichText::new(label).strong());
        let mut clicked = false;
        if narrow {
            ui.add(egui::TextEdit::singleline(value).desired_width(f32::INFINITY));
            clicked = ui
                .add_sized(
                    [ui.available_width(), Self::button_height(ui)],
                    egui::Button::new("Browse…"),
                )
                .clicked();
        } else {
            // The row is allocated at one control's height. A right-to-left
            // layout otherwise centres itself down the whole of the space the
            // column has left, which in a scroll area is the entire page.
            ui.allocate_ui_with_layout(
                egui::vec2(ui.available_width(), ui.spacing().interact_size.y),
                egui::Layout::right_to_left(egui::Align::Center),
                |ui| {
                    clicked = ui.button("Browse…").clicked();
                    ui.add(egui::TextEdit::singleline(value).desired_width(ui.available_width()));
                },
            );
        }
        ui.label(egui::RichText::new(help).small().weak());
        ui.add_space(12.0);
        clicked
    }

    /// Sized from the current text height so the control follows both the
    /// display scaling and the zoom setting.
    fn button_height(ui: &egui::Ui) -> f32 {
        ui.spacing().interact_size.y * 1.8
    }

    fn primary_button(ui: &mut egui::Ui, label: &str) -> bool {
        ui.add_space(4.0);
        ui.add_sized(
            [ui.available_width(), Self::button_height(ui)],
            egui::Button::new(label),
        )
        .clicked()
    }

    fn intro(ui: &mut egui::Ui, title: &str, description: &str) {
        ui.heading(title);
        ui.label(description);
        ui.add_space(14.0);
    }

    fn create_page(&mut self, ui: &mut egui::Ui, narrow: bool) {
        Self::intro(
            ui,
            "Create Playlist",
            "Builds a shuffled playlist file from a folder of music, dropping the \
             same chosen track back in at a regular interval. Your audio files are \
             only read, never changed.",
        );
        ui.add_enabled_ui(!self.busy, |ui| {
            if Self::field(
                ui,
                narrow,
                "Music folder",
                "This folder and every folder inside it is searched for MP3, FLAC, \
                 WAV, M4A, AAC, Ogg, Opus, and WMA files.",
                &mut self.source,
            ) {
                self.pick(PickTarget::Source, true, false);
            }
            if Self::field(
                ui,
                narrow,
                "Repeated track",
                "One audio file that is added again and again through the playlist, \
                 such as a jingle or a station ident. It is left out of the shuffle.",
                &mut self.special,
            ) {
                self.pick(PickTarget::Special, false, false);
            }
            ui.label(egui::RichText::new("How often to repeat it").strong());
            ui.horizontal_wrapped(|ui| {
                ui.label("Add it after every");
                ui.add(egui::DragValue::new(&mut self.insert_every).range(1..=usize::MAX));
                ui.label(if self.insert_every == 1 {
                    "shuffled track."
                } else {
                    "shuffled tracks."
                });
            });
            ui.label(
                egui::RichText::new(
                    "If fewer tracks than that are left at the end, no copy is added there.",
                )
                .small()
                .weak(),
            );
            ui.add_space(12.0);
            if Self::field(
                ui,
                narrow,
                "Save the playlist as",
                "The playlist file to write. The name has to end in .m3u8, and an \
                 existing file at this location is replaced.",
                &mut self.playlist_output,
            ) {
                self.pick(PickTarget::PlaylistOutput, false, true);
            }
            if Self::primary_button(ui, "Create playlist") {
                self.start_generate();
            }
        });
    }

    fn normalize_page(&mut self, ui: &mut egui::Ui, narrow: bool) {
        Self::intro(
            ui,
            "Normalize Volume",
            "Copies a music folder at an even listening volume, so tracks no longer \
             jump between loud and quiet. The copies are new Opus files with the \
             original tags kept; nothing in the source folder is modified. This \
             needs FFmpeg installed.",
        );
        ui.add_enabled_ui(!self.busy, |ui| {
            if Self::field(
                ui,
                narrow,
                "Music folder",
                "The folder of audio files to copy, including everything inside it.",
                &mut self.normalize_source,
            ) {
                self.pick(PickTarget::NormalizeSource, true, false);
            }
            if Self::field(
                ui,
                narrow,
                "Save the copies to",
                "A separate folder for the results, which keeps the same folder \
                 layout. Files that are already there are left alone, so a run that \
                 was stopped can simply be started again.",
                &mut self.normalize_output,
            ) {
                self.pick(PickTarget::NormalizeOutput, true, false);
            }
            if Self::field(
                ui,
                narrow,
                "FFmpeg program (optional)",
                "Leave this empty to use the FFmpeg already installed on this \
                 computer. Fill it in only to choose a particular copy of FFmpeg.",
                &mut self.ffmpeg,
            ) {
                self.pick(PickTarget::Ffmpeg, false, false);
            }
            ui.label(egui::RichText::new("Files at a time").strong());
            ui.horizontal_wrapped(|ui| {
                ui.label("Convert");
                ui.add(egui::DragValue::new(&mut self.jobs).range(1..=32));
                ui.label(if self.jobs == 1 {
                    "file at a time."
                } else {
                    "files at a time."
                });
            });
            ui.label(
                egui::RichText::new(
                    "Higher finishes sooner but uses more of the processor. Lower \
                     leaves the computer freer for other work.",
                )
                .small()
                .weak(),
            );
            ui.add_space(12.0);
            if Self::primary_button(ui, "Start normalizing") {
                self.start_normalize();
            }
        });
    }

    fn activity_page(&mut self, ui: &mut egui::Ui) {
        Self::intro(
            ui,
            "Activity",
            "Progress and messages from the current run, and from the last one that \
             finished.",
        );
        if let Some(progress) = &self.progress {
            let fraction = if progress.total == 0 {
                0.0
            } else {
                let per_mille = progress
                    .completed
                    .saturating_mul(1_000)
                    .checked_div(progress.total)
                    .unwrap_or_default()
                    .min(1_000);
                let bounded = u16::try_from(per_mille).unwrap_or(1_000);
                f32::from(bounded) / 1_000.0
            };
            ui.add(egui::ProgressBar::new(fraction).text(format!(
                "{} of {} files",
                progress.completed, progress.total
            )));
            ui.add_space(4.0);
            ui.label(format!(
                "{} copied  •  {} skipped, already in the output folder  •  {} failed",
                progress.normalized, progress.skipped, progress.failed
            ));
            ui.add_space(8.0);
        }
        let control = self.control.clone();
        ui.horizontal_wrapped(|ui| {
            if let Some(control) = &control {
                if control.is_paused() {
                    if ui
                        .button("Resume")
                        .on_hover_text("Carry on with the remaining files.")
                        .clicked()
                    {
                        control.resume();
                        self.log("Resumed");
                    }
                } else if ui
                    .button("Pause")
                    .on_hover_text("Wait once the files in progress are finished.")
                    .clicked()
                {
                    control.pause();
                    self.log("Pause requested");
                }
                if ui
                    .button("Stop")
                    .on_hover_text("End the run. Finished files are kept.")
                    .clicked()
                {
                    control.cancel();
                    self.log("Stop requested");
                }
            }
        });
        ui.separator();
        if self.activity.is_empty() {
            ui.label("Nothing has run yet.");
        }
        for line in &self.activity {
            ui.label(line);
        }
    }

    fn about_page(ui: &mut egui::Ui) {
        ui.heading("Playlist Generator");
        ui.label(format!("Version {}", env!("CARGO_PKG_VERSION")));
        ui.add_space(8.0);
        ui.label(
            "Builds shuffled .m3u8 playlists and volume-matched Opus copies of a \
             music library. A command-line version of the same program is next to \
             this one in the installation folder.",
        );
        ui.add_space(12.0);
        // Opening a browser would cost `eframe/links` and the twenty-odd crates
        // behind it, so the repository is offered as copyable text instead.
        ui.label(egui::RichText::new("Project repository").strong());
        ui.horizontal_wrapped(|ui| {
            ui.label(REPOSITORY);
            if ui.button("Copy").clicked() {
                ui.ctx().copy_text(REPOSITORY.to_owned());
            }
        });
        ui.add_space(12.0);
        // Both documents run to thousands of lines. Laying them out costs more
        // than the rest of the interface put together and it is repeated on
        // every frame, every resize, and every zoom step, so they stay folded
        // away until somebody asks to read them.
        ui.collapsing("License", |ui| {
            ui.label(include_str!("../../../LICENSE"));
        });
        ui.collapsing("Third-party notices", |ui| {
            ui.label(include_str!("../../../THIRD_PARTY_NOTICES.txt"));
        });
    }
}

impl eframe::App for PlaylistGeneratorApp {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let context = ui.ctx().clone();
        if !self.painted {
            self.painted = true;
            // eframe keeps the window hidden until the first frame has been
            // painted, so this is the line that separates a window that never
            // appeared from one that was never built.
            note("first frame");
        }
        self.receive();
        if self.busy {
            context.request_repaint_after(std::time::Duration::from_millis(50));
        }
        egui::Panel::top("navigation").show(ui, |ui| self.nav(ui));
        egui::Panel::bottom("status").show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.label(if self.busy { "Working…" } else { "Ready" });
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    // `pixels_per_point` already includes the zoom factor, so the
                    // scaling Windows asked for is what is left after dividing it
                    // back out.
                    let zoom = context.zoom_factor();
                    let display = context.pixels_per_point() / zoom;
                    let explanation = format!(
                        "Size of everything in the window. This display is set to \
                         {:.0}% scaling.",
                        display * 100.0
                    );
                    if ui.button("+").on_hover_text(&explanation).clicked() {
                        context.set_zoom_factor((zoom + 0.1).min(3.0));
                    }
                    ui.label(format!("{:.0}%", zoom * 100.0))
                        .on_hover_text(&explanation);
                    if ui.button("−").on_hover_text(&explanation).clicked() {
                        context.set_zoom_factor((zoom - 0.1).max(0.5));
                    }
                    // Dropped first when the window is too narrow to hold the row.
                    ui.label("Zoom").on_hover_text(&explanation);
                });
            })
        });
        egui::CentralPanel::default().show(ui, |ui| {
            egui::ScrollArea::vertical()
                .auto_shrink([false; 2])
                .show(ui, |ui| {
                    Self::form(ui, |ui, narrow| self.show_page(ui, narrow));
                });
        });
    }
}

impl Drop for PlaylistGeneratorApp {
    fn drop(&mut self) {
        if let Some(control) = &self.control {
            control.cancel();
        }
        if let Some(worker) = self.operation_thread.take() {
            let _ = worker.join();
        }
    }
}

/// The startup log, rewritten on every launch.
///
/// A message box only reaches the user when the process is still healthy enough
/// to show one. A launch that is stopped before `main`, or that is killed inside
/// a display driver, reports nothing at all, and the GUI subsystem has already
/// thrown away standard error. The file records how far the launch got, so the
/// difference between "never started", "died opening the window", and "reported
/// an error" is visible after the fact.
#[cfg(windows)]
fn log_path() -> Option<PathBuf> {
    let base = std::env::var_os("LOCALAPPDATA").map_or_else(std::env::temp_dir, PathBuf::from);
    let directory = base.join("PlaylistGenerator");
    std::fs::create_dir_all(&directory).ok()?;
    Some(directory.join("startup.log"))
}

#[cfg(windows)]
fn note(stage: &str) {
    use std::io::Write as _;

    let Some(path) = log_path() else { return };
    let Ok(mut file) = std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(path)
    else {
        return;
    };
    let _ = writeln!(file, "{stage}");
    let _ = file.flush();
}

#[cfg(not(windows))]
fn note(stage: &str) {
    eprintln!("{stage}");
}

/// Starts a fresh log so the file always describes the launch being debugged.
#[cfg(windows)]
fn begin_log() {
    let Some(path) = log_path() else { return };
    let _ = std::fs::remove_file(path);
    note(&format!(
        "Playlist Generator {} starting",
        env!("CARGO_PKG_VERSION")
    ));
    if let Ok(executable) = std::env::current_exe() {
        note(&format!("executable {}", executable.display()));
    }
}

#[cfg(not(windows))]
fn begin_log() {}

/// Presents a fatal error where the user can actually read it.
///
/// The Windows binary is linked for the GUI subsystem and therefore has no
/// console, so a returned error or a panic leaves nothing behind: no window, no
/// message, and no exit status the user ever sees. Every other platform keeps
/// the binary attached to the terminal it was started from.
#[cfg(windows)]
fn report_fatal(summary: &str, detail: &str) {
    note(&format!("{summary} {detail}"));
    let location = log_path().map_or_else(String::new, |path| {
        format!("\n\nStartup log: {}", path.display())
    });
    let _ = rfd::MessageDialog::new()
        .set_level(rfd::MessageLevel::Error)
        .set_title("Playlist Generator")
        .set_description(format!("{summary}\n\n{detail}{location}"))
        .show();
}

#[cfg(not(windows))]
fn report_fatal(summary: &str, detail: &str) {
    eprintln!("{summary}\n\n{detail}");
}

/// Reports the first panic from any thread, then defers to the default hook.
fn install_panic_reporter() {
    static REPORTED: AtomicBool = AtomicBool::new(false);
    let default_hook = panic::take_hook();
    panic::set_hook(Box::new(move |info| {
        default_hook(info);
        if !REPORTED.swap(true, Ordering::Relaxed) {
            report_fatal(
                "Playlist Generator stopped unexpectedly.",
                &info.to_string(),
            );
        }
    }));
}

pub fn run() -> eframe::Result<()> {
    begin_log();
    install_panic_reporter();
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_title("Playlist Generator")
            .with_inner_size([960.0, 640.0])
            .with_min_inner_size([420.0, 320.0])
            .with_clamp_size_to_monitor_size(true),
        ..Default::default()
    };
    note("opening the window");
    let result = eframe::run_native(
        "Playlist Generator",
        options,
        Box::new(|_context| {
            note("window open");
            Ok(Box::<PlaylistGeneratorApp>::default())
        }),
    );
    match &result {
        Ok(()) => note("closed"),
        Err(error) => report_fatal(
            "Playlist Generator could not open its window.",
            &error.to_string(),
        ),
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    use egui_kittest::{Harness, kittest::Queryable as _};

    /// The smallest window the application allows.
    const MINIMUM: (f32, f32) = (420.0, 320.0);
    /// The size the window opens at.
    const DEFAULT: (f32, f32) = (960.0, 640.0);

    fn sized(
        (width, height): (f32, f32),
        pixels_per_point: f32,
    ) -> Harness<'static, PlaylistGeneratorApp> {
        let mut harness = Harness::builder()
            .with_size(egui::Vec2::new(width, height))
            .with_pixels_per_point(pixels_per_point)
            .build_eframe(|_| PlaylistGeneratorApp::default());
        harness.run();
        harness
    }

    /// Opens a page from the navigation bar.
    ///
    /// The tab and the heading below it share a name, so the tab is taken as the
    /// first of the two rather than by an ambiguous lookup.
    fn open(harness: &mut Harness<'_, PlaylistGeneratorApp>, page: &str) {
        let Some(tab) = harness.get_all_by_label(page).next() else {
            panic!("no navigation tab labelled '{page}'");
        };
        tab.click();
        harness.run();
    }

    /// The first browse button on the page that is open.
    fn browse(harness: &Harness<'_, PlaylistGeneratorApp>) -> egui::Rect {
        let Some(button) = harness.get_all_by_label("Browse…").next() else {
            panic!("no browse button on this page");
        };
        button.rect()
    }

    /// Asserts that no control on the page hangs off the side of the window.
    ///
    /// Every browse button once did, because the text box beside it was told to
    /// fill the row and took the button's space with it.
    fn assert_fits(harness: &Harness<'_, PlaylistGeneratorApp>, width: f32) {
        for label in ["Browse…", "Create playlist", "Start normalizing"] {
            for node in harness.query_all_by_label(label) {
                let rect = node.rect();
                assert!(
                    rect.left() >= 0.0 && rect.right() <= width,
                    "'{label}' spans {:?} in a window {width} points wide",
                    rect.x_range()
                );
            }
        }
    }

    #[test]
    fn navigation_and_about_content_are_accessible() {
        let mut harness = sized(DEFAULT, 1.0);
        harness.get_by_label("About").click();
        harness.run();
        assert!(harness.query_by_label("Version 0.9.4").is_some());
        assert!(harness.query_by_label("Project repository").is_some());
        assert!(harness.query_by_label(REPOSITORY).is_some());
    }

    #[test]
    fn legal_text_is_reachable_behind_its_headings() {
        let mut harness = sized(DEFAULT, 1.0);
        harness.get_by_label("About").click();
        harness.run();
        // Folded away so the page does not lay out several thousand lines on
        // every frame, but still one click from the reader.
        assert!(
            harness
                .query_by_label(include_str!("../../../LICENSE"))
                .is_none()
        );
        harness.get_by_label("License").click();
        harness.run();
        assert!(
            harness
                .query_by_label(include_str!("../../../LICENSE"))
                .is_some()
        );
        harness.get_by_label("Third-party notices").click();
        harness.run();
        assert!(harness.query_by_label_contains("THIRD PARTY").is_some());
    }

    #[test]
    fn controls_fit_the_window_at_every_supported_size() {
        for (size, pixels_per_point) in [
            (MINIMUM, 1.0),
            (DEFAULT, 1.0),
            // A 960x640 point window on a display Windows scales to 150% and
            // 200% reports the room below to whoever is laying it out.
            ((640.0, 427.0), 1.5),
            ((480.0, 320.0), 2.0),
            ((1920.0, 1080.0), 1.0),
        ] {
            for page in ["Create Playlist", "Normalize Volume"] {
                let mut harness = sized(size, pixels_per_point);
                open(&mut harness, page);
                assert_fits(&harness, size.0);
            }
        }
    }

    #[test]
    fn the_form_stops_widening_once_it_is_wide_enough_to_read() {
        let mut harness = sized((1920.0, 1080.0), 1.0);
        harness.run();
        let button = harness.get_by_label("Create playlist").rect();
        assert!(
            button.width() <= FORM_WIDTH,
            "the form stretched to {} points across a wide window",
            button.width()
        );
        let centre = button.center().x;
        assert!(
            (centre - 960.0).abs() < 1.0,
            "the form sat at {centre} instead of the middle of the window"
        );
    }

    #[test]
    fn the_browse_button_moves_below_its_box_only_when_the_form_is_tight() {
        let stacked = browse(&sized(MINIMUM, 1.0));
        assert!(
            stacked.width() > 200.0,
            "the browse button did not take a row of its own in a narrow window"
        );

        let beside = browse(&sized(DEFAULT, 1.0));
        assert!(
            beside.width() < 200.0,
            "the browse button took a whole row in a window with space for both"
        );
    }

    #[test]
    fn every_control_explains_itself() {
        let mut harness = sized(DEFAULT, 1.0);
        for label in [
            "Music folder",
            "Repeated track",
            "How often to repeat it",
            "Save the playlist as",
        ] {
            assert!(
                harness.query_by_label(label).is_some(),
                "the create page is missing '{label}'"
            );
        }
        assert!(
            harness
                .query_by_label_contains("only read, never changed")
                .is_some()
        );
        open(&mut harness, "Normalize Volume");
        for label in [
            "Music folder",
            "Save the copies to",
            "FFmpeg program (optional)",
            "Files at a time",
        ] {
            assert!(
                harness.query_by_label(label).is_some(),
                "the normalize page is missing '{label}'"
            );
        }
        assert!(
            harness
                .query_by_label_contains("needs FFmpeg installed")
                .is_some()
        );
    }

    #[test]
    fn starting_a_run_shows_the_page_that_reports_it() {
        let mut harness = sized(DEFAULT, 1.0);
        // The paths are empty, so this is refused before anything is written.
        harness.get_by_label("Create playlist").click();
        harness.run();
        assert!(
            harness
                .query_by_label_contains("Progress and messages")
                .is_some()
        );
        assert!(harness.query_by_label("Creating playlist…").is_some());
    }
}
