# Playlist Generator

Playlist Generator 0.9 is a pure-Rust desktop and command-line application for
building shuffled UTF-8 `.m3u8` playlists and creating EBU R128-normalized Opus
copies of audio libraries. Linux and Windows builds are produced locally; no
remote publishing workflow is used.

## Features

- Recursive, symlink-safe discovery of MP3, FLAC, WAV, M4A, AAC, Ogg, Opus,
  and WMA files.
- Unbiased shuffling with a special audio file inserted after every complete
  block of tracks.
- Absolute, injection-safe playlist entries and replace-safe atomic writes.
- Two-pass FFmpeg loudness normalization to Opus 160k VBR while preserving
  metadata, excluding cover art, and leaving every source untouched.
- Resumable output, collision detection, up to 32 fixed workers, per-file
  failure continuation, pause/resume, and whole-process-tree cancellation.
- An adaptive `egui` desktop interface with Create Playlist, Normalize,
  Activity, and About pages, native asynchronous pickers, and zoom controls.
- Human-readable CLI output or stable newline-delimited JSON for automation.

FFmpeg is the only runtime prerequisite for normalization. Playlist creation
does not use it. `prerequisites` reports availability and an installation
suggestion but never installs software.

## CLI

```text
playlist-generator [--json] generate \
  --source-directory PATH --special-file PATH \
  --insert-every N --output-path PATH

playlist-generator [--json] normalize \
  --source-directory PATH --output-directory PATH \
  [--ffmpeg PATH] [--jobs 1..32]

playlist-generator [--json] prerequisites [--ffmpeg PATH]
```

Normalization defaults to the available processor count capped at eight.
`--json` emits one snake-case object per line with `event`, `message`, nullable
`success`, and typed `data`. Event names are `progress`, `file_failure`,
`result`, and `error`; final results include all paths and counts.

Exit codes are `0` for complete success, `1` for a understood failure or a run
with failed files, `2` for invalid CLI usage, and `130` for interruption.

## Playlist behavior

The source is scanned recursively without following symbolic links or Windows
reparse-point trees. The special file is excluded from the shuffle pool. After
shuffling, it is inserted after every complete block; no copy is appended to an
incomplete final block. The output must end in `.m3u8` and begins with
`#EXTM3U` in UTF-8 without a BOM.

All emitted paths are absolute. Non-UTF-8 paths and paths containing CR or LF
are rejected instead of being converted lossily. Because playlists disclose
local paths, review them before sharing.

## Desktop usage

The desktop application coordinates one operation at a time. Forms remain
centered and scrollable, and reflow below the narrow breakpoint. Normalization
progress and its bounded diagnostic history are on Activity, together with
Pause, Resume, and Stop. Closing the window stops the FFmpeg process group and
removes incomplete temporary output.

The preferred first size is 960×640 points with a 420×320 minimum. The initial
size only shrinks when needed to fit the monitor work area. About contains the
build version, project link, repository license, and generated third-party
notices.

## Build from source

Development is container-first and pinned to Rust 1.97.1. On a Linux host with
rootless Podman (preferred) or Docker:

```bash
make install
make gate
```

`make install` builds the digest-pinned developer image, configures the tracked
Git hook, and checks all required tools. It installs Podman with `dnf` or `apt`
only when neither supported engine exists. Set `CONTAINER_ENGINE` to choose an
engine explicitly.

Build and release details are in
[docs/maintainer-guide.md](docs/maintainer-guide.md); validation scope is in
[docs/testing.md](docs/testing.md).

## License

Licensed under the terms in [LICENSE](LICENSE). Dependency notices are in
[THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).
