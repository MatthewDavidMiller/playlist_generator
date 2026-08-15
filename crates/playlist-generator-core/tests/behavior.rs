#![forbid(unsafe_code)]

use std::ffi::OsString;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};

use playlist_generator_core::{
    GenerateRequest, NormalizeRequest, ProcessOutput, ProcessRunner, RunControl, compose_playlist,
    generate, normalize, parse_loudness,
};

fn temporary(name: &str) -> PathBuf {
    let sequence = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map_or(0, |duration| duration.as_nanos());
    std::env::temp_dir().join(format!(
        "playlist-generator-{name}-{}-{sequence}",
        std::process::id()
    ))
}

#[test]
fn composition_inserts_only_after_complete_blocks() {
    let tracks = [PathBuf::from("A"), PathBuf::from("B"), PathBuf::from("C")];
    let result = compose_playlist(&tracks, Path::new("ID"), 2);
    assert_eq!(
        result.ok(),
        Some(vec!["A".into(), "B".into(), "ID".into(), "C".into()])
    );
}

#[test]
fn generated_playlist_is_extended_utf8_and_absolute() {
    let root = temporary("generate");
    let music = root.join("music");
    let output = root.join("mix.m3u8");
    assert!(fs::create_dir_all(&music).is_ok());
    assert!(fs::write(music.join("café.mp3"), []).is_ok());
    assert!(fs::write(root.join("id.wav"), []).is_ok());
    let result = generate(&GenerateRequest {
        source_directory: music,
        special_file: root.join("id.wav"),
        insert_every: 1,
        output_path: output.clone(),
    });
    assert!(result.is_ok());
    let content = fs::read_to_string(output);
    assert!(
        content
            .as_ref()
            .is_ok_and(|text| text.starts_with("#EXTM3U\n/") && text.contains("café.mp3"))
    );
    let _ = fs::remove_dir_all(root);
}

#[test]
fn output_extension_and_line_break_paths_are_rejected() {
    let root = temporary("validation");
    let music = root.join("music\nattack");
    assert!(fs::create_dir_all(&music).is_ok());
    assert!(fs::write(music.join("song.mp3"), []).is_ok());
    assert!(fs::write(root.join("id.mp3"), []).is_ok());
    let request = GenerateRequest {
        source_directory: music,
        special_file: root.join("id.mp3"),
        insert_every: 1,
        output_path: root.join("mix.txt"),
    };
    assert!(generate(&request).is_err());
    let _ = fs::remove_dir_all(root);
}

#[test]
fn loudness_parser_accepts_numbers_and_rejects_injection() {
    let valid = r#"noise {"input_i":"-20.1","input_tp":-2.0,"input_lra":"4","input_thresh":"-30","target_offset":"0.5"}"#;
    assert!(parse_loudness(valid, "", Path::new("song.mp3")).is_ok());
    let malicious = r#"{"input_i":"-20:linear=false","input_tp":-2,"input_lra":4,"input_thresh":-30,"target_offset":0}"#;
    assert!(parse_loudness(malicious, "", Path::new("song.mp3")).is_err());
}

#[test]
fn run_control_is_idempotent_and_cancel_releases_pause() {
    let control = RunControl::default();
    control.pause();
    control.pause();
    assert!(control.is_paused());
    control.cancel();
    assert!(control.checkpoint().is_err());
}

#[derive(Debug, Default)]
struct FakeFfmpeg(AtomicUsize);

impl ProcessRunner for FakeFfmpeg {
    fn run(
        &self,
        _executable: &Path,
        arguments: &[OsString],
        _control: &RunControl,
    ) -> playlist_generator_core::Result<ProcessOutput> {
        self.0.fetch_add(1, Ordering::Relaxed);
        let encoding = arguments.iter().any(|argument| argument == "libopus");
        if encoding {
            if let Some(output) = arguments.last() {
                let path = PathBuf::from(output);
                fs::write(&path, b"normalized")
                    .map_err(|source| playlist_generator_core::Error::Io { path, source })?;
            }
            Ok(ProcessOutput {
                exit_code: 0,
                stdout: String::new(),
                stderr: String::new(),
            })
        } else {
            Ok(ProcessOutput {
                exit_code: 0,
                stdout: String::new(),
                stderr: r#"{"input_i":"-20","input_tp":"-2","input_lra":"4","input_thresh":"-30","target_offset":"0.5"}"#.into(),
            })
        }
    }
}

#[test]
fn normalization_is_resumable_and_never_changes_the_source() {
    let root = temporary("normalize");
    let source = root.join("source");
    let output = root.join("output");
    let ffmpeg = root.join("ffmpeg");
    assert!(fs::create_dir_all(&source).is_ok());
    assert!(fs::write(source.join("track.mp3"), b"source").is_ok());
    assert!(fs::write(&ffmpeg, []).is_ok());
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt as _;
        assert!(fs::set_permissions(&ffmpeg, fs::Permissions::from_mode(0o755)).is_ok());
    }
    let request = NormalizeRequest {
        source_directory: source.clone(),
        output_directory: output.clone(),
        ffmpeg: Some(ffmpeg),
        jobs: 2,
    };
    let runner = FakeFfmpeg::default();
    let first = normalize(&request, &runner, &RunControl::default(), &|_| {});
    assert!(
        first
            .as_ref()
            .is_ok_and(|result| result.normalized_file_count == 1)
    );
    assert_eq!(
        fs::read(source.join("track.mp3")).ok(),
        Some(b"source".to_vec())
    );
    assert_eq!(
        fs::read(output.join("track.opus")).ok(),
        Some(b"normalized".to_vec())
    );
    let second = normalize(&request, &runner, &RunControl::default(), &|_| {});
    assert!(
        second
            .as_ref()
            .is_ok_and(|result| result.skipped_file_count == 1)
    );
    assert_eq!(runner.0.load(Ordering::Relaxed), 2);
    let _ = fs::remove_dir_all(root);
}

#[derive(Debug)]
struct CancellingFfmpeg(RunControl);

impl ProcessRunner for CancellingFfmpeg {
    fn run(
        &self,
        _executable: &Path,
        _arguments: &[OsString],
        _control: &RunControl,
    ) -> playlist_generator_core::Result<ProcessOutput> {
        self.0.cancel();
        Err(playlist_generator_core::Error::Interrupted)
    }
}

#[test]
fn cancellation_during_a_worker_is_reported_as_stopped() {
    let root = temporary("cancelled-normalize");
    let source = root.join("source");
    let output = root.join("output");
    let ffmpeg = root.join("ffmpeg");
    assert!(fs::create_dir_all(&source).is_ok());
    assert!(fs::write(source.join("track.mp3"), []).is_ok());
    assert!(fs::write(&ffmpeg, []).is_ok());
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt as _;
        assert!(fs::set_permissions(&ffmpeg, fs::Permissions::from_mode(0o755)).is_ok());
    }
    let control = RunControl::default();
    let runner = CancellingFfmpeg(control.clone());
    let result = normalize(
        &NormalizeRequest {
            source_directory: source,
            output_directory: output,
            ffmpeg: Some(ffmpeg),
            jobs: 1,
        },
        &runner,
        &control,
        &|_| {},
    );
    assert!(result.is_ok_and(|summary| summary.stopped));
    let _ = fs::remove_dir_all(root);
}

#[cfg(unix)]
#[test]
fn non_utf8_playlist_paths_are_rejected() {
    use std::os::unix::ffi::OsStringExt as _;
    let root = temporary("non-utf8");
    assert!(fs::create_dir_all(&root).is_ok());
    let invalid = root.join(OsString::from_vec(vec![b'x', 0xff, b'.', b'm', b'p', b'3']));
    assert!(fs::write(&invalid, []).is_ok());
    assert!(playlist_generator_core::scan_audio_files(&root).is_err());
    let _ = fs::remove_dir_all(root);
}
