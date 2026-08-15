#![forbid(unsafe_code)]

use std::io::{self, Write};
use std::path::PathBuf;
use std::process::ExitCode;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::Duration;

use clap::{Parser, Subcommand};
use playlist_generator_core::{
    GenerateRequest, NormalizationAction, NormalizeRequest, OperationEvent, PrerequisiteStatus,
    RunControl, SystemProcessRunner, default_jobs, find_executable, generate, normalize,
};
use serde::Serialize;
use serde_json::{Value, json};

#[derive(Debug, Parser)]
#[command(
    version,
    about = "Create shuffled M3U8 playlists and normalize audio libraries"
)]
struct Cli {
    #[arg(long, global = true, help = "Emit one snake-case JSON object per line")]
    json: bool,
    #[command(subcommand)]
    command: Command,
}

#[derive(Debug, Subcommand)]
enum Command {
    /// Create a shuffled extended-M3U playlist.
    Generate {
        #[arg(long)]
        source_directory: PathBuf,
        #[arg(long)]
        special_file: PathBuf,
        #[arg(long, value_parser = parse_positive)]
        insert_every: usize,
        #[arg(long)]
        output_path: PathBuf,
    },
    /// Create two-pass EBU R128 normalized Opus copies.
    Normalize {
        #[arg(long)]
        source_directory: PathBuf,
        #[arg(long)]
        output_directory: PathBuf,
        #[arg(long)]
        ffmpeg: Option<PathBuf>,
        #[arg(long, default_value_t = default_jobs(), value_parser = parse_jobs)]
        jobs: usize,
    },
    /// Report whether `FFmpeg` is available; never installs software.
    Prerequisites {
        #[arg(long)]
        ffmpeg: Option<PathBuf>,
    },
}

#[derive(Debug)]
struct Presenter {
    json: bool,
    output: Mutex<io::Stdout>,
}

impl Presenter {
    fn line(&self, event: &str, message: &str, success: Option<bool>, data: impl Serialize) {
        if let Ok(mut output) = self.output.lock() {
            if self.json {
                let record =
                    json!({"event": event, "message": message, "success": success, "data": data});
                let _ = writeln!(output, "{record}");
            } else {
                let _ = writeln!(output, "{message}");
            }
        }
    }

    fn error(&self, message: &str) {
        if self.json {
            self.line("error", message, Some(false), Value::Null);
        } else {
            let _ = writeln!(io::stderr(), "error: {message}");
        }
    }
}

fn main() -> ExitCode {
    let cli = Cli::parse();
    let presenter = Arc::new(Presenter {
        json: cli.json,
        output: Mutex::new(io::stdout()),
    });
    match execute(cli.command, &presenter) {
        Ok(code) => ExitCode::from(code),
        Err(playlist_generator_core::Error::Interrupted) => {
            presenter.error("operation was interrupted");
            ExitCode::from(130)
        }
        Err(error) => {
            presenter.error(&error.to_string());
            ExitCode::from(1)
        }
    }
}

fn execute(command: Command, presenter: &Arc<Presenter>) -> playlist_generator_core::Result<u8> {
    match command {
        Command::Generate {
            source_directory,
            special_file,
            insert_every,
            output_path,
        } => {
            let result = generate(&GenerateRequest {
                source_directory,
                special_file,
                insert_every,
                output_path,
            })?;
            presenter.line(
                "result",
                &format!(
                    "wrote {} entries to '{}'",
                    result.playlist_entry_count,
                    result.output_path.display()
                ),
                Some(true),
                &result,
            );
            Ok(0)
        }
        Command::Normalize {
            source_directory,
            output_directory,
            ffmpeg,
            jobs,
        } => {
            let control = RunControl::default();
            install_interrupt(&control)?;
            let progress_presenter = Arc::clone(presenter);
            let result = normalize(
                &NormalizeRequest {
                    source_directory,
                    output_directory,
                    ffmpeg,
                    jobs,
                },
                &SystemProcessRunner,
                &control,
                &move |progress: OperationEvent| {
                    let kind = if progress.action == NormalizationAction::Failed {
                        "file_failure"
                    } else {
                        "progress"
                    };
                    let success = (kind == "file_failure").then_some(false);
                    progress_presenter.line(kind, &progress.message, success, &progress);
                },
            )?;
            let success = !result.stopped && result.failed_file_count == 0;
            presenter.line(
                "result",
                &format!(
                    "normalized {}, skipped {}, failed {}",
                    result.normalized_file_count,
                    result.skipped_file_count,
                    result.failed_file_count
                ),
                Some(success),
                &result,
            );
            if result.stopped {
                Ok(130)
            } else if success {
                Ok(0)
            } else {
                Ok(1)
            }
        }
        Command::Prerequisites { ffmpeg } => {
            let resolved = find_executable(ffmpeg.as_deref()).ok();
            let status = PrerequisiteStatus {
                available: resolved.is_some(),
                ffmpeg_path: resolved,
                suggestion: install_suggestion().into(),
            };
            let message = status.ffmpeg_path.as_ref().map_or_else(
                || {
                    format!(
                        "FFmpeg was not found. Suggested command: {}",
                        status.suggestion
                    )
                },
                |path| format!("FFmpeg is available at '{}'", path.display()),
            );
            presenter.line("result", &message, Some(status.available), &status);
            Ok(u8::from(!status.available))
        }
    }
}

fn install_interrupt(control: &RunControl) -> playlist_generator_core::Result<()> {
    let interrupted = Arc::new(AtomicBool::new(false));
    signal_hook::flag::register(signal_hook::consts::SIGINT, Arc::clone(&interrupted)).map_err(
        |error| {
            playlist_generator_core::Error::Process(format!(
                "unable to install interrupt handler: {error}"
            ))
        },
    )?;
    let control = control.clone();
    thread::spawn(move || {
        while !interrupted.load(Ordering::Relaxed) {
            thread::sleep(Duration::from_millis(20));
        }
        control.cancel();
    });
    Ok(())
}

fn install_suggestion() -> &'static str {
    if cfg!(target_os = "windows") {
        "winget install Gyan.FFmpeg"
    } else if cfg!(target_os = "macos") {
        "brew install ffmpeg"
    } else {
        "sudo apt install ffmpeg  # or: sudo dnf install ffmpeg"
    }
}

fn parse_positive(value: &str) -> Result<usize, String> {
    value
        .parse::<usize>()
        .ok()
        .filter(|parsed| *parsed > 0)
        .ok_or_else(|| "value must be at least 1".into())
}

fn parse_jobs(value: &str) -> Result<usize, String> {
    value
        .parse::<usize>()
        .ok()
        .filter(|parsed| (1..=32).contains(parsed))
        .ok_or_else(|| "jobs must be between 1 and 32".into())
}
