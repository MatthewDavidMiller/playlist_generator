use std::ffi::OsString;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::thread;

use crate::error::{Error, IoContext};
use crate::ffmpeg::{find_executable, parse_loudness};
use crate::paths::{canonical_directory, same_path, scan_audio_files, validate_text_path};
use crate::{
    FfmpegRunner, NormalizationAction, NormalizationFailure, NormalizeRequest, NormalizeResult,
    OperationEvent, Result, RunControl,
};

const DIAGNOSTIC_LIMIT: usize = 4096;

#[derive(Clone, Debug)]
struct Job {
    source: PathBuf,
    destination: PathBuf,
}

#[derive(Debug)]
struct ProgressState {
    total: usize,
    normalized: usize,
    skipped: usize,
    failures: Vec<NormalizationFailure>,
}

pub fn default_jobs() -> usize {
    num_cpus::get().clamp(1, 8)
}

pub fn normalize(
    request: &NormalizeRequest,
    runner: &dyn FfmpegRunner,
    control: &RunControl,
    event: &(dyn Fn(OperationEvent) + Sync),
) -> Result<NormalizeResult> {
    if !(1..=32).contains(&request.jobs) {
        return Err(Error::Validation("jobs must be between 1 and 32".into()));
    }
    let source = canonical_directory(&request.source_directory, "source directory")?;
    let output = crate::paths::resolve_output_directory(&request.output_directory)?;
    validate_text_path(&output)?;
    if same_path(&source, &output) {
        return Err(Error::Validation(
            "output directory must differ from the source directory".into(),
        ));
    }
    let ffmpeg = find_executable(request.ffmpeg.as_deref())?;
    let files = scan_audio_files(&source)?;
    if files.is_empty() {
        return Err(Error::Validation(format!(
            "no supported audio files were found in '{}'",
            source.display()
        )));
    }
    let (jobs, skipped) = plan(&files, &source, &output)?;
    let state = Arc::new(Mutex::new(ProgressState {
        total: files.len(),
        normalized: 0,
        skipped: skipped.len(),
        failures: Vec::new(),
    }));
    if !skipped.is_empty() {
        publish(
            &state,
            event,
            NormalizationAction::Skipped,
            None,
            format!("skipped {} existing or output-tree files", skipped.len()),
        );
    }
    let next = AtomicUsize::new(0);
    let stopped = thread::scope(|scope| {
        for _ in 0..request.jobs.min(jobs.len()) {
            let state = Arc::clone(&state);
            let next = &next;
            let jobs = &jobs;
            let ffmpeg = &ffmpeg;
            scope.spawn(move || {
                loop {
                    if control.is_cancelled() {
                        break;
                    }
                    let index = next.fetch_add(1, Ordering::Relaxed);
                    let Some(job) = jobs.get(index) else { break };
                    match normalize_one(job, ffmpeg, runner, control, &state, event) {
                        Ok(()) => {}
                        Err(Error::Interrupted) => break,
                        Err(error) => record_failure(&state, event, &job.source, error.to_string()),
                    }
                }
            });
        }
        control.is_cancelled()
    });
    let mut progress = state
        .lock()
        .map_err(|_| Error::Process("progress lock failed".into()))?;
    progress
        .failures
        .sort_by(|left, right| left.source_path.cmp(&right.source_path));
    Ok(NormalizeResult {
        source_directory: source,
        output_directory: output,
        normalized_file_count: progress.normalized,
        skipped_file_count: progress.skipped,
        failed_file_count: progress.failures.len(),
        failures: progress.failures.clone(),
        stopped,
    })
}

fn plan(files: &[PathBuf], source: &Path, output: &Path) -> Result<(Vec<Job>, Vec<PathBuf>)> {
    let mut jobs = Vec::new();
    let mut skipped = Vec::new();
    let mut destinations: Vec<(PathBuf, PathBuf)> = Vec::new();
    for file in files {
        if file.starts_with(output) {
            skipped.push(file.clone());
            continue;
        }
        let relative = file.strip_prefix(source).map_err(|_| {
            Error::Validation(format!(
                "'{}' is outside the source directory",
                file.display()
            ))
        })?;
        let destination = output.join(relative).with_extension("opus");
        if same_path(file, &destination) || destination.exists() {
            skipped.push(file.clone());
            continue;
        }
        if let Some((_, prior)) = destinations
            .iter()
            .find(|(candidate, _)| same_path(candidate, &destination))
        {
            return Err(Error::Validation(format!(
                "multiple sources map to '{}': '{}' and '{}'",
                destination.display(),
                prior.display(),
                file.display()
            )));
        }
        destinations.push((destination.clone(), file.clone()));
        jobs.push(Job {
            source: file.clone(),
            destination,
        });
    }
    Ok((jobs, skipped))
}

fn normalize_one(
    job: &Job,
    ffmpeg: &Path,
    runner: &dyn FfmpegRunner,
    control: &RunControl,
    state: &Mutex<ProgressState>,
    event: &(dyn Fn(OperationEvent) + Sync),
) -> Result<()> {
    if control.is_paused() {
        publish(
            state,
            event,
            NormalizationAction::Paused,
            Some(job.source.clone()),
            "paused".into(),
        );
    }
    control.checkpoint()?;
    publish(
        state,
        event,
        NormalizationAction::Analyzing,
        Some(job.source.clone()),
        "analyzing loudness".into(),
    );
    let analysis = runner.run(ffmpeg, &analysis_arguments(&job.source), control)?;
    ensure_success(&analysis, "FFmpeg analysis failed")?;
    let loudness = parse_loudness(&analysis.stderr, &analysis.stdout, &job.source)?;
    control.checkpoint()?;
    if let Some(parent) = job.destination.parent() {
        fs::create_dir_all(parent).at(parent)?;
    }
    let temporary = temporary_path(&job.destination);
    let result = (|| {
        publish(
            state,
            event,
            NormalizationAction::Encoding,
            Some(job.source.clone()),
            "encoding Opus output".into(),
        );
        let encoded = runner.run(
            ffmpeg,
            &encode_arguments(&job.source, &temporary, &loudness.filter()),
            control,
        )?;
        ensure_success(&encoded, "FFmpeg encoding failed")?;
        if !temporary.is_file() {
            return Err(Error::Process(format!(
                "FFmpeg reported success but did not create '{}'",
                temporary.display()
            )));
        }
        fs::hard_link(&temporary, &job.destination).at(&job.destination)?;
        fs::remove_file(&temporary).at(&temporary)?;
        Ok(())
    })();
    if result.is_err() {
        let _ = fs::remove_file(&temporary);
    }
    result?;
    let mut progress = state
        .lock()
        .map_err(|_| Error::Process("progress lock failed".into()))?;
    progress.normalized += 1;
    emit_locked(
        &progress,
        event,
        NormalizationAction::Completed,
        Some(job.source.clone()),
        "completed".into(),
    );
    Ok(())
}

fn analysis_arguments(source: &Path) -> Vec<OsString> {
    [
        OsString::from("-hide_banner"),
        OsString::from("-nostdin"),
        OsString::from("-i"),
        source.as_os_str().to_owned(),
        OsString::from("-af"),
        OsString::from("loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json"),
        OsString::from("-vn"),
        OsString::from("-f"),
        OsString::from("null"),
        OsString::from("-"),
    ]
    .into()
}

fn encode_arguments(source: &Path, output: &Path, filter: &str) -> Vec<OsString> {
    [
        OsString::from("-hide_banner"),
        OsString::from("-nostdin"),
        OsString::from("-n"),
        OsString::from("-i"),
        source.as_os_str().to_owned(),
        OsString::from("-af"),
        OsString::from(filter),
        OsString::from("-c:a"),
        OsString::from("libopus"),
        OsString::from("-b:a"),
        OsString::from("160k"),
        OsString::from("-vbr"),
        OsString::from("on"),
        OsString::from("-map_metadata"),
        OsString::from("0"),
        OsString::from("-vn"),
        OsString::from("-f"),
        OsString::from("opus"),
        output.as_os_str().to_owned(),
    ]
    .into()
}

fn temporary_path(destination: &Path) -> PathBuf {
    let sequence = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map_or(0, |duration| duration.as_nanos());
    let file = destination
        .file_name()
        .and_then(|name| name.to_str())
        .unwrap_or("output.opus");
    destination.with_file_name(format!(".{file}.{}.{}.tmp", std::process::id(), sequence))
}

fn ensure_success(output: &crate::ProcessOutput, message: &str) -> Result<()> {
    if output.exit_code == 0 {
        return Ok(());
    }
    let diagnostics = output.stderr.trim();
    let tail = if diagnostics.len() > DIAGNOSTIC_LIMIT {
        let target = diagnostics.len() - DIAGNOSTIC_LIMIT;
        let start = diagnostics
            .char_indices()
            .find_map(|(index, _)| (index >= target).then_some(index))
            .unwrap_or(diagnostics.len());
        &diagnostics[start..]
    } else {
        diagnostics
    };
    Err(Error::Process(if tail.is_empty() {
        message.into()
    } else {
        format!("{message}: {tail}")
    }))
}

fn record_failure(
    state: &Mutex<ProgressState>,
    event: &(dyn Fn(OperationEvent) + Sync),
    path: &Path,
    reason: String,
) {
    if let Ok(mut progress) = state.lock() {
        progress.failures.push(NormalizationFailure {
            source_path: path.to_path_buf(),
            reason: reason.clone(),
        });
        emit_locked(
            &progress,
            event,
            NormalizationAction::Failed,
            Some(path.to_path_buf()),
            reason,
        );
    }
}

fn publish(
    state: &Mutex<ProgressState>,
    event: &(dyn Fn(OperationEvent) + Sync),
    action: NormalizationAction,
    path: Option<PathBuf>,
    message: String,
) {
    if let Ok(progress) = state.lock() {
        emit_locked(&progress, event, action, path, message);
    }
}

fn emit_locked(
    progress: &ProgressState,
    event: &(dyn Fn(OperationEvent) + Sync),
    action: NormalizationAction,
    path: Option<PathBuf>,
    message: String,
) {
    let failed = progress.failures.len();
    event(OperationEvent {
        action,
        path,
        completed: progress.normalized + progress.skipped + failed,
        total: progress.total,
        normalized: progress.normalized,
        skipped: progress.skipped,
        failed,
        message,
    });
}
