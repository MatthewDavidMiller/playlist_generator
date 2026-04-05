# VLC Playlist Generator

VLC Playlist Generator is a Python application for building shuffled `.m3u8`
playlists and inserting a specific audio file after every configured block of
tracks. The repository is now Python-only and is intended to run the same way on
Windows, macOS, and Linux.

## Features

- Recursive music library scanning for supported audio formats.
- Deterministic, testable playlist generation logic in a shared core module.
- Command-line interface for scripting and automation.
- Tkinter desktop GUI for local interactive use.
- Windows executable build support through PyInstaller.
- UTF-8 `.m3u8` output with absolute paths for VLC compatibility.
- Linting, formatting, tests, and git commit hooks for the Python codebase.

## Requirements

- Python 3.9 or newer.
- Tkinter for the GUI.

Tkinter ships with the standard Python installers on Windows and macOS. On some
Linux distributions you may need to install it separately, for example
`python3-tk`.

## Installation

Run directly from the repository:

```bash
python3 -m playlist_generator --help
```

Or install the package locally:

```bash
python3 -m pip install .
```

That installation exposes:

- `vlc-playlist-generator`
- `vlc-playlist-generator-gui`

To install the development tooling as well:

```bash
python3 -m pip install -e ".[dev]"
```

To install the Windows executable build tooling in the active environment:

```bash
python3 -m pip install -e ".[windows-build]"
```

## CLI Usage

```bash
python3 -m playlist_generator \
  --source-directory "/path/to/Music" \
  --special-file "/path/to/station-id.mp3" \
  --insert-every 4 \
  --output-path "/path/to/Playlists/mix.m3u8"
```

The CLI prints the output path followed by a JSON summary with:

- `source_directory`
- `special_file`
- `output_path`
- `source_track_count`
- `playlist_entry_count`
- `insert_every`

Generated playlists store absolute local filesystem paths for VLC
compatibility. Sharing a playlist can expose your directory structure and may
not work on another machine.

## GUI Usage

Run the Tkinter application:

```bash
python3 -m playlist_generator.gui
```

Or, after installation:

```bash
vlc-playlist-generator-gui
```

## Windows EXE

The repository includes a supported PyInstaller build path for producing a
standalone Windows GUI executable. Maintainer-facing build steps live in
[docs/maintainer-guide.md](docs/maintainer-guide.md). Validation details for
the build helper live in [docs/testing.md](docs/testing.md). Tagged builds also
publish the generated `.exe` files to GitHub Releases.

## Supported Audio Formats

- `.mp3`
- `.flac`
- `.wav`
- `.m4a`
- `.aac`
- `.ogg`
- `.wma`

## Playlist Behavior

Generation follows this sequence:

1. Scan the selected source directory recursively.
2. Keep only supported audio files.
3. Remove the special file from the source pool if it lives inside that
   directory.
4. Shuffle the remaining tracks.
5. Insert the special file after every full block of `x` songs.
6. Write the playlist as UTF-8 `.m3u8` with a `#EXTM3U` header.

Example with `--insert-every 3`:

- Shuffled tracks: `A, B, C, D, E, F, G`
- Special file: `ID`
- Result: `A, B, C, ID, D, E, F, ID, G`

No extra special file is added after a trailing partial block.

## Project Layout

- `playlist_generator/core.py`: shared playlist generation logic.
- `playlist_generator/cli.py`: command-line entrypoint.
- `playlist_generator/gui.py`: Tkinter GUI entrypoint.
- `playlist_generator/windows_build.py`: Windows executable build helper.
- `scripts/`: PyInstaller launcher entrypoints for Windows packaging.
- `tests/`: pytest-discovered automated tests.
- `docs/`: maintainer-oriented project documentation.

## Documentation

- Maintainer workflow lives in [docs/maintainer-guide.md](docs/maintainer-guide.md).
- Testing and validation workflow lives in [docs/testing.md](docs/testing.md).
- Repository instructions for coding agents live in [AGENTS.md](AGENTS.md).
- Tool configuration remains centralized in [pyproject.toml](pyproject.toml) and
  [.pre-commit-config.yaml](.pre-commit-config.yaml).

## License

This repository is licensed under the terms in [LICENSE](LICENSE).
