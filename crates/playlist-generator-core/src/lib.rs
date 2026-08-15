#![forbid(unsafe_code)]

mod control;
mod error;
mod ffmpeg;
mod models;
mod normalize;
mod paths;
mod playlist;

pub use control::RunControl;
pub use error::{Error, Result};
pub use ffmpeg::{
    FfmpegRunner, ProcessOutput, ProcessRunner, SystemProcessRunner, find_executable,
    parse_loudness,
};
pub use models::{
    GenerateRequest, GenerateResult, NormalizationAction, NormalizationFailure, NormalizeRequest,
    NormalizeResult, OperationEvent, PrerequisiteStatus,
};
pub use normalize::{default_jobs, normalize};
pub use paths::{SUPPORTED_EXTENSIONS, scan_audio_files};
pub use playlist::{compose_playlist, generate};
