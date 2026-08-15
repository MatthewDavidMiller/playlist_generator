use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};

use atomic_write_file::AtomicWriteFile;
use rand::seq::SliceRandom;

use crate::error::{Error, IoContext};
use crate::paths::{absolute, is_supported, same_path, scan_audio_files, validate_text_path};
use crate::{GenerateRequest, GenerateResult, Result};

pub fn compose_playlist(
    tracks: &[PathBuf],
    special_file: &Path,
    insert_every: usize,
) -> Result<Vec<PathBuf>> {
    if insert_every == 0 {
        return Err(Error::Validation("insert every must be at least 1".into()));
    }
    let mut entries = Vec::with_capacity(tracks.len() + tracks.len() / insert_every);
    for (index, track) in tracks.iter().enumerate() {
        entries.push(track.clone());
        if (index + 1) % insert_every == 0 {
            entries.push(special_file.to_path_buf());
        }
    }
    Ok(entries)
}

pub fn generate(request: &GenerateRequest) -> Result<GenerateResult> {
    if request.insert_every == 0 {
        return Err(Error::Validation("insert every must be at least 1".into()));
    }
    if !request
        .output_path
        .extension()
        .and_then(|value| value.to_str())
        .is_some_and(|value| value.eq_ignore_ascii_case("m3u8"))
    {
        return Err(Error::Validation(
            "output path must use the .m3u8 extension".into(),
        ));
    }
    let source_directory =
        crate::paths::canonical_directory(&request.source_directory, "source directory")?;
    let special_file = fs::canonicalize(&request.special_file).at(&request.special_file)?;
    validate_text_path(&special_file)?;
    if !special_file.is_file() || !is_supported(&special_file) {
        return Err(Error::Validation(
            "special file must be an existing supported audio file".into(),
        ));
    }
    let output_path = absolute(&request.output_path)?;
    validate_text_path(&output_path)?;
    let mut tracks = scan_audio_files(&source_directory)?;
    tracks.retain(|track| !same_path(track, &special_file));
    if tracks.is_empty() {
        return Err(Error::Validation(
            "no supported audio files were found after excluding the special file".into(),
        ));
    }
    tracks.shuffle(&mut rand::rng());
    let entries = compose_playlist(&tracks, &special_file, request.insert_every)?;
    for path in tracks.iter().chain(std::iter::once(&special_file)) {
        if !path.is_file() {
            return Err(Error::Validation(format!(
                "audio file '{}' became unavailable",
                path.display()
            )));
        }
    }
    if let Some(parent) = output_path.parent() {
        fs::create_dir_all(parent).at(parent)?;
    }
    let mut file = AtomicWriteFile::options()
        .open(&output_path)
        .at(&output_path)?;
    file.write_all(b"#EXTM3U\n").at(&output_path)?;
    for entry in &entries {
        let text = entry
            .to_str()
            .ok_or_else(|| Error::Validation("playlist path is not valid UTF-8".into()))?;
        file.write_all(text.as_bytes())
            .and_then(|()| file.write_all(b"\n"))
            .at(&output_path)?;
    }
    file.commit().at(&output_path)?;
    Ok(GenerateResult {
        source_directory,
        special_file,
        output_path,
        source_track_count: tracks.len(),
        playlist_entry_count: entries.len(),
        insert_every: request.insert_every,
    })
}
