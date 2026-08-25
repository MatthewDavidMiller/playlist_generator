# Playlist Generator

Playlist Generator 0.9 is a pure-Rust interactive and command-line application
for building shuffled UTF-8 `.m3u8` playlists and creating EBU R128-normalized
Opus copies of audio libraries. Windows gets a graphical desktop application;
Linux gets a terminal application, statically linked so one build runs on every
distribution. Both sit next to the same command-line program. Builds are
produced locally; no hosted CI runner is used.

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
- An adaptive `egui` desktop interface on Windows with Create Playlist,
  Normalize Volume, Activity, and About pages, native asynchronous pickers, an
  explanation under every control, and zoom controls.
- A `ratatui` terminal interface on Linux with the same four pages, the same
  explanation under every control, and keyboard-driven pause, resume, and stop.
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

## Desktop usage (Windows)

The desktop application coordinates one operation at a time. Each control is
labelled in plain language and explained underneath, and the two starting pages
name what they read, what they write, and what they leave alone.

Every page is one centered, scrollable column that stops widening at 620 points,
so a maximized window and a half-screen window read the same. Widths are
measured from that column rather than from the window, which is what keeps the
layout correct under Windows display scaling: at 150% or 200% the same window
reports fewer points, and the browse buttons move under their text boxes only
when the column itself is genuinely too tight. Zoom in the status bar rescales
everything on top of the display setting, which its tooltip reports.

Starting a run opens Activity, which holds progress, the counts, the bounded
message history, and Pause, Resume, and Stop. Closing the window stops the
FFmpeg process group and removes incomplete temporary output.

The preferred first size is 960×640 points with a 420×320 minimum. The initial
size only shrinks when needed to fit the monitor work area. About contains the
build version, project link, and, folded away because they are thousands of
lines, the repository license and the generated third-party notices.

On Windows the desktop binary is linked for the GUI subsystem and has no
console, so every launch rewrites
`%LOCALAPPDATA%\PlaylistGenerator\startup.log` with how far it got: the version
and executable path, `opening the window`, `window open` once the window and
renderer exist, and `first frame` once something has been painted, which is
also when the window stops being hidden. A launch that fails reports the error
in a message box and writes it there too. A missing file means the process
never reached its own code.

## Terminal usage (Linux)

The terminal application has the same four pages and the same guidance under
every control. `Tab` and `Shift+Tab` move between the boxes and the action
button, `Enter` starts the run, and `Alt+1` through `Alt+4` — or `Alt+←` and
`Alt+→` — change page. Quit is `Ctrl+Q`, deliberately modified so that it
cannot be typed into a path.

Paths are typed rather than picked: a terminal has no file dialog, and pulling
one in would mean linking the desktop stack that keeps this build from being
static. Starting a run opens Activity, which holds the progress bar, the counts,
the bounded message history, and `p` to pause or resume and `s` to stop.
Quitting during a run cancels it and waits for FFmpeg to be reaped and any
incomplete temporary output to be removed. About holds the build version, the
project link, and, folded away behind `l` and `n`, the license and third-party
notices.

## Build from source

Development is container-first and pinned to Rust 1.97.1. Docker with Compose
v2 is the only prerequisite on either host — every compiler, linter, scanner,
and test runner lives in the toolbox image.

```bash
./scripts/setup.sh
docker compose run --rm gate
```

`scripts/setup.sh` builds the digest-pinned toolbox image, records the local
uid so gates leave no root-owned files behind, installs the tracked Git hooks,
and checks every required tool. On Windows there is no uid to map, so run
`docker compose build` and `git config core.hooksPath .githooks` instead; the
gate command is identical.

Build and release details are in
[docs/maintainer-guide.md](docs/maintainer-guide.md); validation scope is in
[docs/testing.md](docs/testing.md).

## License

Licensed under the terms in [LICENSE](LICENSE). Dependency notices are in
[THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).
