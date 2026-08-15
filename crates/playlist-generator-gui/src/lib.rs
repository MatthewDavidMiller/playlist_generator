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
const FORM_WIDTH: f32 = 640.0;
const NARROW_BREAKPOINT: f32 = 620.0;

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
                (Page::Normalize, "Normalize"),
                (Page::Activity, "Activity"),
                (Page::About, "About"),
            ] {
                if ui.selectable_label(self.page == page, label).clicked() {
                    self.page = page;
                }
            }
        });
    }

    fn field(ui: &mut egui::Ui, narrow: bool, label: &str, value: &mut String) -> bool {
        ui.label(label);
        let clicked = if narrow {
            ui.add(egui::TextEdit::singleline(value).desired_width(f32::INFINITY));
            ui.button("Browse…").clicked()
        } else {
            let mut clicked = false;
            ui.horizontal(|ui| {
                ui.add(egui::TextEdit::singleline(value).desired_width(f32::INFINITY));
                clicked = ui.button("Browse…").clicked();
            });
            clicked
        };
        ui.add_space(8.0);
        clicked
    }

    fn create_page(&mut self, ui: &mut egui::Ui, narrow: bool) {
        ui.heading("Create Playlist");
        ui.label("Shuffle a music library and insert a special track after each complete block.");
        ui.add_enabled_ui(!self.busy, |ui| {
            if Self::field(ui, narrow, "Music library", &mut self.source) {
                self.pick(PickTarget::Source, true, false);
            }
            if Self::field(ui, narrow, "Special audio file", &mut self.special) {
                self.pick(PickTarget::Special, false, false);
            }
            ui.label("Insert after every");
            ui.add(egui::DragValue::new(&mut self.insert_every).range(1..=usize::MAX));
            ui.add_space(8.0);
            if Self::field(
                ui,
                narrow,
                "Playlist destination",
                &mut self.playlist_output,
            ) {
                self.pick(PickTarget::PlaylistOutput, false, true);
            }
            if ui
                .add_sized(
                    [ui.available_width(), 36.0],
                    egui::Button::new("Create playlist"),
                )
                .clicked()
            {
                self.start_generate();
            }
        });
    }

    fn normalize_page(&mut self, ui: &mut egui::Ui, narrow: bool) {
        ui.heading("Normalize");
        ui.label("Create non-destructive two-pass EBU R128 Opus copies.");
        ui.add_enabled_ui(!self.busy, |ui| {
            if Self::field(ui, narrow, "Source folder", &mut self.normalize_source) {
                self.pick(PickTarget::NormalizeSource, true, false);
            }
            if Self::field(ui, narrow, "Output folder", &mut self.normalize_output) {
                self.pick(PickTarget::NormalizeOutput, true, false);
            }
            if Self::field(ui, narrow, "FFmpeg executable (optional)", &mut self.ffmpeg) {
                self.pick(PickTarget::Ffmpeg, false, false);
            }
            ui.horizontal(|ui| {
                ui.label("Concurrent files");
                ui.add(egui::DragValue::new(&mut self.jobs).range(1..=32));
            });
            if ui
                .add_sized(
                    [ui.available_width(), 36.0],
                    egui::Button::new("Normalize library"),
                )
                .clicked()
            {
                self.start_normalize();
            }
        });
    }

    fn activity_page(&mut self, ui: &mut egui::Ui) {
        ui.heading("Activity");
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
            ui.add(
                egui::ProgressBar::new(fraction)
                    .text(format!("{} / {}", progress.completed, progress.total)),
            );
            ui.label(format!(
                "Normalized {}  •  Skipped {}  •  Failed {}",
                progress.normalized, progress.skipped, progress.failed
            ));
        }
        let control = self.control.clone();
        ui.horizontal(|ui| {
            if let Some(control) = &control {
                if control.is_paused() {
                    if ui.button("Resume").clicked() {
                        control.resume();
                        self.log("Resumed");
                    }
                } else if ui.button("Pause").clicked() {
                    control.pause();
                    self.log("Pause requested");
                }
                if ui.button("Stop").clicked() {
                    control.cancel();
                    self.log("Stop requested");
                }
            }
        });
        ui.separator();
        for line in &self.activity {
            ui.label(line);
        }
    }

    fn about_page(ui: &mut egui::Ui) {
        ui.heading("Playlist Generator");
        ui.label(format!("Version {}", env!("CARGO_PKG_VERSION")));
        // Opening a browser would cost `eframe/links` and the twenty-odd crates
        // behind it, so the repository is offered as copyable text instead.
        ui.horizontal(|ui| {
            ui.label("Project repository");
            if ui.button("Copy").clicked() {
                ui.ctx().copy_text(REPOSITORY.to_owned());
            }
        });
        ui.label(REPOSITORY);
        ui.separator();
        ui.heading("License");
        ui.label(include_str!("../../../LICENSE"));
        ui.separator();
        ui.heading("Third-party notices");
        ui.label(include_str!("../../../THIRD_PARTY_NOTICES.txt"));
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
                ui.label(if self.busy { "Busy" } else { "Idle" });
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    if ui.button("+").clicked() {
                        context.set_zoom_factor((context.zoom_factor() + 0.1).min(3.0));
                    }
                    ui.label(format!("{:.0}%", context.zoom_factor() * 100.0));
                    if ui.button("−").clicked() {
                        context.set_zoom_factor((context.zoom_factor() - 0.1).max(0.5));
                    }
                    ui.label(format!(
                        "Display {:.0}%",
                        context.pixels_per_point() * 100.0
                    ));
                });
            })
        });
        egui::CentralPanel::default().show(ui, |ui| {
            let narrow = ui.available_width() < NARROW_BREAKPOINT;
            egui::ScrollArea::vertical().show(ui, |ui| {
                ui.vertical_centered(|ui| {
                    ui.set_max_width(FORM_WIDTH);
                    match self.page {
                        Page::Create => self.create_page(ui, narrow),
                        Page::Normalize => self.normalize_page(ui, narrow),
                        Page::Activity => self.activity_page(ui),
                        Page::About => Self::about_page(ui),
                    }
                })
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

    #[test]
    fn navigation_and_about_content_are_accessible() {
        let mut harness = Harness::new_eframe(|_| PlaylistGeneratorApp::default());
        harness.get_by_label("About").click();
        harness.run();
        assert!(harness.query_by_label("Version 0.9.3").is_some());
        assert!(harness.query_by_label("Project repository").is_some());
        assert!(harness.query_by_label(REPOSITORY).is_some());
    }
}
