use std::path::PathBuf;

use serde::Serialize;

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct GenerateRequest {
    pub source_directory: PathBuf,
    pub special_file: PathBuf,
    pub insert_every: usize,
    pub output_path: PathBuf,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
pub struct GenerateResult {
    pub source_directory: PathBuf,
    pub special_file: PathBuf,
    pub output_path: PathBuf,
    pub source_track_count: usize,
    pub playlist_entry_count: usize,
    pub insert_every: usize,
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct NormalizeRequest {
    pub source_directory: PathBuf,
    pub output_directory: PathBuf,
    pub ffmpeg: Option<PathBuf>,
    pub jobs: usize,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
pub struct NormalizeResult {
    pub source_directory: PathBuf,
    pub output_directory: PathBuf,
    pub normalized_file_count: usize,
    pub skipped_file_count: usize,
    pub failed_file_count: usize,
    pub failures: Vec<NormalizationFailure>,
    pub stopped: bool,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
pub struct NormalizationFailure {
    pub source_path: PathBuf,
    pub reason: String,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum NormalizationAction {
    Planning,
    Skipped,
    Paused,
    Analyzing,
    Encoding,
    Completed,
    Failed,
    Stopped,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
pub struct OperationEvent {
    pub action: NormalizationAction,
    pub path: Option<PathBuf>,
    pub completed: usize,
    pub total: usize,
    pub normalized: usize,
    pub skipped: usize,
    pub failed: usize,
    pub message: String,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
pub struct PrerequisiteStatus {
    pub ffmpeg_path: Option<PathBuf>,
    pub available: bool,
    pub suggestion: String,
}
