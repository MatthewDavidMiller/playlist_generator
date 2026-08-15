use std::ffi::OsString;
use std::fs;
use std::io::{Read, Result as IoResult};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::thread;
use std::time::Duration;

use command_group::CommandGroup;
use serde_json::Value;

use crate::paths::validate_text_path;
use crate::{Error, Result, RunControl};

const STREAM_LIMIT: usize = 1024 * 1024;

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct ProcessOutput {
    pub exit_code: i32,
    pub stdout: String,
    pub stderr: String,
}

pub trait ProcessRunner: Send + Sync {
    fn run(
        &self,
        executable: &Path,
        arguments: &[OsString],
        control: &RunControl,
    ) -> Result<ProcessOutput>;
}

pub trait FfmpegRunner: ProcessRunner {}
impl<T: ProcessRunner + ?Sized> FfmpegRunner for T {}

#[derive(Clone, Copy, Debug, Default)]
pub struct SystemProcessRunner;

fn drain_bounded(mut reader: impl Read) -> IoResult<Vec<u8>> {
    let mut retained = Vec::new();
    let mut buffer = [0_u8; 16 * 1024];
    loop {
        let count = reader.read(&mut buffer)?;
        if count == 0 {
            break;
        }
        if count >= STREAM_LIMIT {
            retained.clear();
            retained.extend_from_slice(&buffer[count - STREAM_LIMIT..count]);
        } else {
            let excess = retained
                .len()
                .saturating_add(count)
                .saturating_sub(STREAM_LIMIT);
            if excess > 0 {
                retained.drain(..excess);
            }
            retained.extend_from_slice(&buffer[..count]);
        }
    }
    Ok(retained)
}

impl ProcessRunner for SystemProcessRunner {
    fn run(
        &self,
        executable: &Path,
        arguments: &[OsString],
        control: &RunControl,
    ) -> Result<ProcessOutput> {
        if !executable.is_absolute() {
            return Err(Error::Process(
                "FFmpeg executable was not resolved to an absolute path".into(),
            ));
        }
        control.checkpoint()?;
        let mut command = Command::new(executable);
        command
            .args(arguments)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());
        let mut child = command.group_spawn().map_err(|error| {
            Error::Process(format!(
                "unable to start '{}': {error}",
                executable.display()
            ))
        })?;
        let stdout = child
            .inner()
            .stdout
            .take()
            .ok_or_else(|| Error::Process("unable to capture FFmpeg output".into()))?;
        let stderr = child
            .inner()
            .stderr
            .take()
            .ok_or_else(|| Error::Process("unable to capture FFmpeg diagnostics".into()))?;
        let stdout_reader = thread::spawn(move || drain_bounded(stdout));
        let stderr_reader = thread::spawn(move || drain_bounded(stderr));
        let status = loop {
            if control.is_cancelled() {
                let _ = child.kill();
                let _ = child.wait();
                let _ = stdout_reader.join();
                let _ = stderr_reader.join();
                return Err(Error::Interrupted);
            }
            match child
                .try_wait()
                .map_err(|error| Error::Process(format!("unable to wait for FFmpeg: {error}")))?
            {
                Some(status) => break status,
                None => thread::sleep(Duration::from_millis(20)),
            }
        };
        let stdout = stdout_reader
            .join()
            .map_err(|_| Error::Process("FFmpeg output reader failed".into()))?
            .map_err(|error| Error::Process(format!("unable to read FFmpeg output: {error}")))?;
        let stderr = stderr_reader
            .join()
            .map_err(|_| Error::Process("FFmpeg diagnostics reader failed".into()))?
            .map_err(|error| {
                Error::Process(format!("unable to read FFmpeg diagnostics: {error}"))
            })?;
        Ok(ProcessOutput {
            exit_code: status.code().unwrap_or(-1),
            stdout: String::from_utf8_lossy(&stdout).into_owned(),
            stderr: String::from_utf8_lossy(&stderr).into_owned(),
        })
    }
}

pub fn find_executable(requested: Option<&Path>) -> Result<PathBuf> {
    if let Some(path) = requested {
        validate_text_path(path)?;
        let resolved = fs::canonicalize(path).map_err(|error| {
            Error::Process(format!(
                "FFmpeg '{}' is unavailable: {error}",
                path.display()
            ))
        })?;
        if executable_file(&resolved) {
            return Ok(resolved);
        }
        return Err(Error::Process(format!(
            "FFmpeg '{}' is not executable",
            path.display()
        )));
    }
    let path = std::env::var_os("PATH")
        .ok_or_else(|| Error::Process("FFmpeg was not found on PATH".into()))?;
    for directory in std::env::split_paths(&path).filter(|entry| !entry.as_os_str().is_empty()) {
        for name in executable_names() {
            let candidate = directory.join(name);
            if executable_file(&candidate) {
                return fs::canonicalize(&candidate).map_err(|error| {
                    Error::Process(format!(
                        "unable to resolve '{}': {error}",
                        candidate.display()
                    ))
                });
            }
        }
    }
    Err(Error::Process("FFmpeg was not found on PATH".into()))
}

#[cfg(windows)]
fn executable_names() -> [&'static str; 2] {
    ["ffmpeg", "ffmpeg.exe"]
}
#[cfg(not(windows))]
fn executable_names() -> [&'static str; 1] {
    ["ffmpeg"]
}

fn executable_file(path: &Path) -> bool {
    if !path.is_file() {
        return false;
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        fs::metadata(path).is_ok_and(|metadata| metadata.permissions().mode() & 0o111 != 0)
    }
    #[cfg(windows)]
    {
        true
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct LoudnessStats {
    fields: [String; 5],
}

pub fn parse_loudness(primary: &str, fallback: &str, source: &Path) -> Result<LoudnessStats> {
    parse_stream(primary, source)?
        .or(parse_stream(fallback, source)?)
        .ok_or_else(|| {
            Error::Process(format!(
                "FFmpeg returned missing or malformed loudness analysis JSON for '{}'",
                source.display()
            ))
        })
}

fn parse_stream(stream: &str, source: &Path) -> Result<Option<LoudnessStats>> {
    let mut ends = stream
        .match_indices('}')
        .map(|(index, _)| index)
        .collect::<Vec<_>>();
    while let Some(end) = ends.pop() {
        let Some(start) = stream[..=end].rfind('{') else {
            continue;
        };
        let Ok(value) = serde_json::from_str::<Value>(&stream[start..=end]) else {
            continue;
        };
        let names = [
            "input_i",
            "input_tp",
            "input_lra",
            "input_thresh",
            "target_offset",
        ];
        let mut fields = Vec::with_capacity(names.len());
        for name in names {
            let raw = value.get(name).ok_or_else(|| {
                Error::Process(format!(
                    "FFmpeg loudness analysis for '{}' is missing '{name}'",
                    source.display()
                ))
            })?;
            let number = match raw {
                Value::Number(value) => value.as_f64(),
                Value::String(value) => value.parse::<f64>().ok(),
                _ => None,
            }
            .filter(|number| number.is_finite())
            .ok_or_else(|| {
                Error::Process(format!(
                    "FFmpeg loudness analysis for '{}' has an invalid '{name}'",
                    source.display()
                ))
            })?;
            fields.push(number.to_string());
        }
        let fields: [String; 5] = fields
            .try_into()
            .map_err(|_| Error::Process("invalid loudness field count".into()))?;
        return Ok(Some(LoudnessStats { fields }));
    }
    Ok(None)
}

impl LoudnessStats {
    pub(crate) fn filter(&self) -> String {
        format!(
            "loudnorm=I=-16:TP=-1.5:LRA=11:measured_I={}:measured_TP={}:measured_LRA={}:measured_thresh={}:offset={}:linear=true",
            self.fields[0], self.fields[1], self.fields[2], self.fields[3], self.fields[4]
        )
    }
}
