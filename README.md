# VLC Playlist Generator

VLC Playlist Generator is a local Avalonia desktop application for creating
shuffled UTF-8 `.m3u8` playlists and normalizing audio volume. It also includes
a command-line executable for scripts and automation.

## Features

- Cross-platform Avalonia 12 desktop interface with light and dark themes.
- Recursive, symlink-safe scanning for supported audio formats.
- Shuffled playlists with a selected audio file inserted after every complete
  block of tracks.
- Non-destructive, resumable normalization to Opus 160k copies through FFmpeg.
- Pause, resume, immediate stop, progress, and diagnostic details in the GUI.
- Atomic playlist and normalized-file writes that do not replace good output
  with a failed partial operation.
- Self-contained Windows and Linux desktop and CLI builds produced locally on
  Linux.
- Platform-neutral core services shared by the GUI and CLI.

## Requirements

To build from source:

- .NET SDK 10.0.302 or a newer 10.0 patch.
- Linux, Windows, or macOS for development.
- FFmpeg on `PATH` for volume normalization.

Playlist generation does not require FFmpeg. The published builds are
self-contained and do not require users to install .NET.

On Debian or Ubuntu, Avalonia desktop applications require:

```bash
sudo apt install libx11-6 libice6 libsm6 libfontconfig1
```

## Run From Source

Start the desktop application:

```bash
dotnet run --project src/PlaylistGenerator.Desktop
```

Show CLI help:

```bash
dotnet run --project src/PlaylistGenerator.Cli -- --help
```

## Desktop Usage

The **Create playlist** tab has separate inputs for the music library, special
audio file, insertion interval, and playlist destination.

The **Normalize volume** tab creates Opus 160k copies in a separate output
folder. It preserves relative subfolders and metadata, skips completed outputs,
and never changes source files. The output folder must differ from the source
folder. Pause takes effect before the next FFmpeg step; Stop cancels an active
FFmpeg process and safely removes partial output.

Only one operation runs at a time, so both tabs stay disabled while either is
working. Status and error detail are shared by both tabs.

## CLI Usage

Create a playlist:

```bash
dotnet run --project src/PlaylistGenerator.Cli -- \
  --source-directory "/path/to/Music" \
  --special-file "/path/to/station-id.mp3" \
  --insert-every 4 \
  --output-path "/path/to/Playlists/mix.m3u8"
```

Normalize a directory:

```bash
dotnet run --project src/PlaylistGenerator.Cli -- \
  normalize-volume \
  --source-directory "/path/to/Music" \
  --output-directory "/path/to/Normalized"
```

Show a platform-appropriate FFmpeg installation command:

```bash
dotnet run --project src/PlaylistGenerator.Cli -- install-ffmpeg
```

The command is shown for review and is never run automatically.

Show usage for all commands, or for one command:

```bash
dotnet run --project src/PlaylistGenerator.Cli -- --help
dotnet run --project src/PlaylistGenerator.Cli -- normalize-volume --help
```

Exit codes: `0` success, `1` an operation failed, `2` the command line could not
be interpreted, `70` an unexpected internal error, `130` interrupted. Successful
playlist and normalization runs also print a snake-case JSON summary for
scripting.

## Local Executable Builds

From a Linux machine, publish self-contained Windows `.exe` files and Linux
executables:

```bash
./scripts/build-release.sh
```

After installing the repository hook with `./scripts/install-hooks.sh`, every
successful commit also refreshes the `win-x64` desktop and CLI binaries
automatically.

Output is organized under:

- `artifacts/win-x64/desktop/PlaylistGenerator.exe`
- `artifacts/win-x64/cli/playlist-generator.exe`
- `artifacts/linux-x64/desktop/PlaylistGenerator`
- `artifacts/linux-x64/cli/playlist-generator`

Select one or more target architectures when needed:

```bash
./scripts/build-release.sh --runtime win-arm64
./scripts/build-release.sh --runtime win-x64 --runtime linux-arm64
```

Detailed release and validation workflow lives in
[docs/maintainer-guide.md](docs/maintainer-guide.md).

## Supported Input Audio Formats

- `.mp3`
- `.flac`
- `.wav`
- `.m4a`
- `.aac`
- `.ogg`
- `.opus`
- `.wma`

Matching is case-insensitive. Normalized outputs always use `.opus`.

## Playlist Behavior

Generation:

1. Scans the music directory recursively.
2. Excludes unsupported files and symbolic-link/reparse-point trees.
3. Removes the special file from the shuffle pool if it is inside the library.
4. Shuffles the remaining tracks.
5. Inserts the special file after every complete group of the configured size.
6. Atomically writes a UTF-8 `.m3u8` file with a `#EXTM3U` header.

With an interval of three, `A, B, C, D` becomes `A, B, C, ID, D`. No special
file is appended after an incomplete trailing group.

Generated playlists contain absolute local paths for VLC compatibility. Sharing
one can expose local directory names and will not generally work on another
computer.

## Project Layout

- `src/PlaylistGenerator.Core/`: domain models, contracts, and application
  services, with `Composition/` holding the shared object graph both hosts use.
- `src/PlaylistGenerator.CommandLine/`: testable CLI parsing and presentation,
  with one type per command under `Commands/`.
- `src/PlaylistGenerator.Presentation/`: platform-neutral MVVM state and
  commands, one view model per tab.
- `src/PlaylistGenerator.Desktop/`: Avalonia views, desktop adapters, and GUI
  host.
- `src/PlaylistGenerator.Cli/`: thin console executable host.
- `tests/PlaylistGenerator.Tests/`: xUnit v3 tests, mirroring the source layout.
- `scripts/`: local validation, hook installation, and release publishing.
- `.githooks/`: repository-owned local Git hooks.
- `docs/`: maintainer and testing documentation.

## License

This repository is licensed under the terms in [LICENSE](LICENSE).
