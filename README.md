# Playlist Generator

Playlist Generator 1.0 is a pure-Rust interactive and command-line application
for building shuffled UTF-8 `.m3u8` playlists and creating EBU R128-normalized
Opus copies of audio libraries. Windows and Linux both get a graphical
interface that runs in the local web browser, served by a small program on the
computer itself; Linux also gets a terminal application. Every program is
statically linked, so one Linux build runs on every distribution, and each sits
next to the same command-line program. Builds are produced locally; no hosted
CI runner is used.

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
- A browser interface on Windows and Linux with Create Playlist, Normalize
  Volume, Activity, and About pages, a built-in folder browser, and an
  explanation under every control. It is served from 127.0.0.1 by a server
  written in Rust on the standard library alone, uses no JavaScript, and
  answers only the browser it opened.
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

## Web interface (Windows and Linux)

Start `playlist-generator-web`; on Windows, double-click it. It opens a console
window, listens on a free port on 127.0.0.1, prints the address, and opens that
address in the default browser. `--port PORT` uses a fixed port instead, and
`--no-browser` only prints the address. Nothing listens on any other network
interface.

The printed address carries a secret that changes on every launch. Opening it
signs that browser in, and the server refuses every other browser, other host
names pointed at 127.0.0.1, and forms posted from other sites. An address from
an earlier launch is refused, so open the one the console shows.

The application coordinates one operation at a time. Each control is labelled
in plain language and explained underneath, and the two starting pages name
what they read, what they write, and what they leave alone. Every page is one
centered column that stops widening at 620 pixels, so a maximized window and a
half-screen window read the same; the browse buttons move under their text
boxes only when that column is narrower than 430 pixels. The browser's own zoom
scales everything, and the colors follow its light or dark setting.

Paths can be typed, or chosen with Browse…, which lists the computer's folders
and only the files the box can use: audio files for the repeated track,
playlists for the output (or a folder and a file name to save as), and any file
for FFmpeg. Windows lists its drives at the top level. Hidden entries and names
that are not valid Unicode are not listed, because playlist paths cannot use
the latter.

Starting a run opens Activity, which holds progress, the counts, the bounded
message history with the newest first, and Pause, Resume, and Stop. It reloads
itself every second while a run is in progress, and the forms are locked until
it ends.

Quit, at the bottom of every page, or Enter in the console window stops the
program: a run in progress is cancelled first, FFmpeg is reaped, and any
incomplete temporary output is removed. Closing the browser tab leaves it
running. Ctrl+C or closing the console window ends it immediately without that
cleanup, so prefer Quit while a run is in progress. About contains the build
version, project link, and, folded away because they are thousands of lines,
the repository license and the generated third-party notices.

## Terminal usage (Linux)

The terminal application has the same four pages and the same guidance under
every control. `Tab` and `Shift+Tab` move between the boxes and the action
button, `Enter` starts the run, and `Alt+1` through `Alt+4` — or `Alt+←` and
`Alt+→` — change page. Quit is `Ctrl+Q`, deliberately modified so that it
cannot be typed into a path.

Paths are typed rather than picked: a terminal has no file dialog. The web
interface has a folder browser when one is wanted. Starting a run opens Activity, which holds the progress bar, the counts,
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
