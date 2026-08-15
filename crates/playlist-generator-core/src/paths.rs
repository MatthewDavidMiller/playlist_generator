use std::fs;
use std::path::{Path, PathBuf};

use walkdir::{DirEntry, WalkDir};

use crate::Result;
use crate::error::{Error, IoContext};

pub const SUPPORTED_EXTENSIONS: [&str; 8] =
    ["mp3", "flac", "wav", "m4a", "aac", "ogg", "opus", "wma"];

fn is_link_like(entry: &DirEntry) -> bool {
    entry.file_type().is_symlink() || is_windows_reparse(entry.path())
}

#[cfg(windows)]
fn is_windows_reparse(path: &Path) -> bool {
    use std::os::windows::fs::MetadataExt;
    const REPARSE_POINT: u32 = 0x400;
    fs::symlink_metadata(path).map_or(true, |metadata| {
        metadata.file_attributes() & REPARSE_POINT != 0
    })
}

#[cfg(not(windows))]
fn is_windows_reparse(_path: &Path) -> bool {
    false
}

pub fn validate_text_path(path: &Path) -> Result<()> {
    let text = path.to_str().ok_or_else(|| {
        Error::Validation(format!("path '{}' is not valid UTF-8", path.display()))
    })?;
    if text.contains(['\r', '\n']) {
        return Err(Error::Validation(format!(
            "path '{}' contains a line break",
            path.display()
        )));
    }
    Ok(())
}

pub fn absolute(path: &Path) -> Result<PathBuf> {
    validate_text_path(path)?;
    let joined = if path.is_absolute() {
        path.to_path_buf()
    } else {
        std::env::current_dir()
            .at("current directory")
            .map(|directory| directory.join(path))?
    };
    let mut normalized = PathBuf::new();
    for component in joined.components() {
        match component {
            std::path::Component::CurDir => {}
            std::path::Component::ParentDir => {
                if !normalized.pop() {
                    return Err(Error::Validation(format!(
                        "path '{}' escapes the filesystem root",
                        path.display()
                    )));
                }
            }
            std::path::Component::Prefix(prefix) => normalized.push(prefix.as_os_str()),
            std::path::Component::RootDir => normalized.push(component.as_os_str()),
            std::path::Component::Normal(segment) => normalized.push(segment),
        }
    }
    Ok(normalized)
}

pub(crate) fn resolve_output_directory(path: &Path) -> Result<PathBuf> {
    let full = absolute(path)?;
    let mut existing = full.as_path();
    while !existing.exists() {
        existing = existing.parent().ok_or_else(|| {
            Error::Validation(format!(
                "output directory '{}' has no existing ancestor",
                path.display()
            ))
        })?;
    }
    let metadata = fs::symlink_metadata(existing).at(existing)?;
    if metadata.file_type().is_symlink() || is_windows_reparse(existing) {
        return Err(Error::Validation(format!(
            "output directory '{}' crosses a symbolic link or reparse point",
            path.display()
        )));
    }
    let canonical = fs::canonicalize(existing).at(existing)?;
    let remainder = full
        .strip_prefix(existing)
        .map_err(|_| Error::Validation(format!("unable to resolve output '{}'", path.display())))?;
    Ok(canonical.join(remainder))
}

pub fn canonical_directory(path: &Path, label: &str) -> Result<PathBuf> {
    validate_text_path(path)?;
    let source_metadata = fs::symlink_metadata(path).at(path)?;
    if source_metadata.file_type().is_symlink() || is_windows_reparse(path) {
        return Err(Error::Validation(format!(
            "{label} '{}' must not be a symbolic link or reparse point",
            path.display()
        )));
    }
    let canonical = fs::canonicalize(path).at(path)?;
    if !canonical.is_dir() {
        return Err(Error::Validation(format!(
            "{label} '{}' is not a directory",
            path.display()
        )));
    }
    validate_text_path(&canonical)?;
    Ok(canonical)
}

pub fn is_supported(path: &Path) -> bool {
    path.extension()
        .and_then(|extension| extension.to_str())
        .is_some_and(|extension| {
            SUPPORTED_EXTENSIONS
                .iter()
                .any(|supported| extension.eq_ignore_ascii_case(supported))
        })
}

pub fn scan_audio_files(source: &Path) -> Result<Vec<PathBuf>> {
    let root = canonical_directory(source, "source directory")?;
    let mut files = Vec::new();
    let walker = WalkDir::new(&root)
        .follow_links(false)
        .into_iter()
        .filter_entry(|entry| !is_link_like(entry));
    for item in walker {
        let entry = item.map_err(|error| {
            Error::Validation(format!("unable to scan '{}': {error}", root.display()))
        })?;
        if entry.file_type().is_file() && is_supported(entry.path()) {
            validate_text_path(entry.path())?;
            files.push(entry.into_path());
        }
    }
    files.sort_by_key(|path| path.to_string_lossy().to_lowercase());
    Ok(files)
}

pub(crate) fn same_path(left: &Path, right: &Path) -> bool {
    #[cfg(windows)]
    {
        left.to_string_lossy()
            .eq_ignore_ascii_case(&right.to_string_lossy())
    }
    #[cfg(not(windows))]
    {
        left == right
    }
}
