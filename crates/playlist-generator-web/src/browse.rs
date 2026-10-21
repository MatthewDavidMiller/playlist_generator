//! The folder browser that stands in for a native file dialog.
//!
//! A web page cannot learn the real path of a file the user picks: browsers
//! hand over the contents and, at most, a bare name. The server can read its
//! own filesystem, though, so it lists folders itself and the page chooses
//! from that listing. Only folders and the files a field can use are shown.

use std::cmp::Ordering;
use std::fs;
use std::path::{Path, PathBuf};

use playlist_generator_core::SUPPORTED_EXTENSIONS;

use crate::app::Pick;

/// A folder with more entries than this is shown truncated. Nobody scrolls a
/// list that long, and building one would stall the response.
pub const MAX_ENTRIES: usize = 5_000;

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct Entry {
    pub name: String,
    pub path: String,
}

#[derive(Clone, Debug, Default)]
pub struct Listing {
    /// `None` for the top level: drives on Windows, `/` elsewhere.
    pub directory: Option<String>,
    pub parent: Option<String>,
    pub folders: Vec<Entry>,
    pub files: Vec<Entry>,
    pub truncated: bool,
    pub error: Option<String>,
}

/// The home folder, which is where a box with nothing in it starts browsing.
pub fn home() -> Option<PathBuf> {
    let variable = if cfg!(windows) { "USERPROFILE" } else { "HOME" };
    std::env::var_os(variable)
        .map(PathBuf::from)
        .filter(|path| path.is_dir())
}

/// Where browsing for a box starts: the folder it names, the folder holding
/// the file it names, or the home folder when it names neither.
pub fn start_directory(current: &str) -> Option<PathBuf> {
    let current = Path::new(current.trim());
    if !current.as_os_str().is_empty() {
        if current.is_dir() {
            return Some(current.to_path_buf());
        }
        if let Some(parent) = current.parent()
            && parent.is_dir()
        {
            return Some(parent.to_path_buf());
        }
    }
    home().or_else(|| std::env::current_dir().ok())
}

/// The top level of the filesystem. Windows has no single root, so every
/// drive letter that exists is offered instead.
pub fn roots() -> Listing {
    let folders = if cfg!(windows) {
        (b'A'..=b'Z')
            .map(|letter| format!("{}:\\", char::from(letter)))
            .filter(|root| Path::new(root).is_dir())
            .map(|root| Entry {
                name: root.clone(),
                path: root,
            })
            .collect()
    } else {
        vec![Entry {
            name: "/".into(),
            path: "/".into(),
        }]
    };
    Listing {
        folders,
        ..Listing::default()
    }
}

fn wanted(pick: Pick, path: &Path) -> bool {
    let extension = path.extension().and_then(|extension| extension.to_str());
    match pick {
        Pick::Folder => false,
        Pick::Program => true,
        Pick::AudioFile => extension.is_some_and(|extension| {
            SUPPORTED_EXTENSIONS
                .iter()
                .any(|supported| extension.eq_ignore_ascii_case(supported))
        }),
        Pick::Playlist => extension.is_some_and(|extension| extension.eq_ignore_ascii_case("m3u8")),
    }
}

fn by_name(left: &Entry, right: &Entry) -> Ordering {
    left.name
        .to_lowercase()
        .cmp(&right.name.to_lowercase())
        .then_with(|| left.name.cmp(&right.name))
}

/// Lists one folder for a field.
///
/// Hidden entries are left out, and so are names that are not UTF-8: core
/// refuses those paths, so offering one would only lead to an error later.
pub fn list(directory: &Path, pick: Pick) -> Listing {
    let Some(text) = directory.to_str() else {
        return Listing {
            error: Some("This folder's name is not valid Unicode.".into()),
            ..roots()
        };
    };
    let entries = match fs::read_dir(directory) {
        Ok(entries) => entries,
        Err(error) => {
            return Listing {
                error: Some(format!("'{text}' could not be opened: {error}")),
                ..roots()
            };
        }
    };
    let mut listing = Listing {
        directory: Some(text.to_owned()),
        parent: directory.parent().and_then(Path::to_str).map(str::to_owned),
        ..Listing::default()
    };
    for entry in entries.flatten() {
        let path = entry.path();
        let (Some(name), Some(text)) =
            (entry.file_name().to_str().map(str::to_owned), path.to_str())
        else {
            continue;
        };
        if name.starts_with('.') {
            continue;
        }
        if listing.folders.len() + listing.files.len() == MAX_ENTRIES {
            listing.truncated = true;
            break;
        }
        let item = Entry {
            name,
            path: text.to_owned(),
        };
        // Follows links, so a linked music folder can be opened like any other.
        if path.is_dir() {
            listing.folders.push(item);
        } else if wanted(pick, &path) {
            listing.files.push(item);
        }
    }
    listing.folders.sort_by(by_name);
    listing.files.sort_by(by_name);
    listing
}

/// Where a playlist chosen as a folder and a name is written. An empty name
/// falls back to the one the box already held, then to `playlist.m3u8`.
pub fn save_path(directory: &str, name: &str, current: &str) -> String {
    let name = name.trim();
    let name = if name.is_empty() {
        Path::new(current.trim())
            .file_name()
            .and_then(|name| name.to_str())
            .unwrap_or("playlist.m3u8")
    } else {
        name
    };
    Path::new(directory).join(name).display().to_string()
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;

    pub(crate) fn temporary(name: &str) -> PathBuf {
        let sequence = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map_or(0, |duration| duration.as_nanos());
        let path = std::env::temp_dir().join(format!(
            "playlist-generator-web-{name}-{}-{sequence}",
            std::process::id()
        ));
        assert!(fs::create_dir_all(&path).is_ok());
        path
    }

    fn library() -> PathBuf {
        let root = temporary("browse");
        for folder in ["Zebra", "alpha", ".hidden"] {
            assert!(fs::create_dir_all(root.join(folder)).is_ok());
        }
        for file in ["b.FLAC", "a.mp3", "notes.txt", "mix.m3u8", ".secret.mp3"] {
            assert!(fs::write(root.join(file), []).is_ok());
        }
        root
    }

    fn names(entries: &[Entry]) -> Vec<&str> {
        entries.iter().map(|entry| entry.name.as_str()).collect()
    }

    #[test]
    fn each_field_is_offered_only_what_it_can_use() {
        let root = library();
        let folders = list(&root, Pick::Folder);
        assert_eq!(names(&folders.folders), ["alpha", "Zebra"]);
        assert!(folders.files.is_empty());
        assert_eq!(
            names(&list(&root, Pick::AudioFile).files),
            ["a.mp3", "b.FLAC"]
        );
        assert_eq!(names(&list(&root, Pick::Playlist).files), ["mix.m3u8"]);
        assert_eq!(
            names(&list(&root, Pick::Program).files),
            ["a.mp3", "b.FLAC", "mix.m3u8", "notes.txt"]
        );
        let listing = list(&root, Pick::Folder);
        assert_eq!(listing.directory.as_deref(), root.to_str());
        assert_eq!(
            listing.parent.as_deref(),
            root.parent().and_then(Path::to_str)
        );
        drop(fs::remove_dir_all(root));
    }

    #[test]
    fn a_folder_that_cannot_be_read_falls_back_to_the_top_level() {
        let root = temporary("missing");
        let listing = list(&root.join("absent"), Pick::Folder);
        assert!(listing.error.is_some());
        assert!(listing.directory.is_none());
        assert!(!listing.folders.is_empty());
        drop(fs::remove_dir_all(root));
    }

    #[test]
    fn browsing_starts_beside_the_file_a_box_already_names() {
        let root = library();
        let file = root.join("a.mp3");
        assert_eq!(
            start_directory(&file.display().to_string()),
            Some(root.clone())
        );
        assert_eq!(
            start_directory(&root.display().to_string()),
            Some(root.clone())
        );
        drop(fs::remove_dir_all(root));
    }

    #[test]
    fn a_saved_playlist_keeps_the_name_it_already_had() {
        let directory = Path::new("music");
        assert_eq!(
            save_path("music", "", "old/party.m3u8"),
            directory.join("party.m3u8").display().to_string()
        );
        assert_eq!(
            save_path("music", "", ""),
            directory.join("playlist.m3u8").display().to_string()
        );
        assert_eq!(
            save_path("music", " new.m3u8 ", "old/party.m3u8"),
            directory.join("new.m3u8").display().to_string()
        );
    }
}
