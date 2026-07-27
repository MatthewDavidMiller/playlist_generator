# Maintainer Guide

User behavior and runtime usage are documented in [README.md](../README.md).
This guide covers local maintenance of the .NET/Avalonia solution.

## Local Setup

Install the .NET 10 SDK selected by [global.json](../global.json), then restore
the locked dependency graph:

```bash
dotnet restore PlaylistGenerator.slnx --locked-mode
```

FFmpeg is only required for manual volume-normalization testing. Unit tests use
an in-process fake and do not execute FFmpeg.

## Architecture

Dependencies point inward:

```text
Avalonia desktop ──> Presentation ─┐
                                   ├──> Core models, contracts, and services
CLI host ──────────> CommandLine ──┘
```

- `PlaylistGenerator.Core` owns scanning, playlist composition, atomic writes,
  FFmpeg command construction, process cancellation, and normalization.
- `PlaylistGenerator.Presentation` owns the platform-neutral MVVM state and
  commands.
- `PlaylistGenerator.Desktop` contains AXAML views, file-picker and theme
  adapters, and the GUI entry point.
- `PlaylistGenerator.CommandLine` owns testable command parsing and
  console/JSON presentation.
- `PlaylistGenerator.Cli` is the thin console executable host.

Keep operating-system APIs and Avalonia types out of the core project. Add
domain behavior tests before adding view-specific code.

### Core Layering

Within `PlaylistGenerator.Core`, dependencies also point one way:

```text
Composition ──> Services ──> Infrastructure ──> Abstractions, Models
                                Threading ──────────┘
```

`Abstractions` must not reference `Services`. A contract needing a
collaborator, such as the pause signal an `IAudioNormalizer` observes, declares
its own interface there; the concrete `PauseController` lives in `Threading`
and implements it.

Each public type gets its own file, named after it.

### Normalization Concurrency And Failures

`AudioNormalizationService` encodes several files at once through
`Parallel.ForEachAsync`. Both FFmpeg passes are processor-bound and each works
on a single file, so a sequential run leaves most of a multi-core machine idle.
Concurrent workers are safe because `NormalizationPlanner` has already proven
every destination path distinct before any process starts.

The worker count defaults to `AudioNormalizationService.DefaultMaxDegreeOfParallelism`,
which is the processor count capped at eight; FFmpeg spawns threads of its own and
both passes also read from disk. Pass an explicit count to the four-argument
constructor, or through `CoreServices.Create`, to override it. Tests pass `1`
when they assert on ordering.

`NormalizationProgressReporter` owns every counter. It updates them and
publishes the matching report under one lock, which costs a little concurrency
but keeps reported counts monotonic; without it a progress bar runs backwards
when two workers finish together.

A file that cannot be normalized is recorded in `NormalizationResult.Failures`
and the run continues. Losing hours of completed work to one unreadable file
would make a large library impractical to process, and the run is resumable, so
a later run retries only what is missing. Cancellation still stops the whole
run.

Anything that observes work started by a worker must synchronize on an actual
signal. Bodies are dispatched to the thread pool, so a single `Task.Yield` no
longer implies that a file has begun processing.

### Composition

`Core/Composition/CoreServices` is the single composition root. Both
`App.axaml.cs` and `Program.cs` build their object graph from it, so the two
front ends cannot drift apart. Wiring is explicit and typed rather than
resolved from a container, which keeps a missing dependency a compile error
instead of a startup crash. `CoreServices.Create` accepts substituted
infrastructure for tests.

### Presentation

`MainViewModel` is a shell. It owns the shared `StatusViewModel` and
`OperationCoordinator`, and composes one view model per tab. Cross-tab
behavior, such as the normalization tab inheriting defaults from a chosen music
folder, is wired through an event in the shell rather than by one tab reaching
into another.

AXAML binds through full paths from the window's `x:DataType`, for example
`{Binding Playlist.SourceDirectory}`, so every path is verified by the compiled
bindings at build time. `tests/PlaylistGenerator.Tests/Desktop/` then loads the
window headlessly and checks that those bindings carry real values.

A transport command that can complete the run, such as `Resume` or `Stop`,
publishes its transient status message *before* releasing or cancelling the
run. The run's completion message is written from another thread, and acting
first lets the transient message overwrite the final outcome.

### Adding a CLI Command

Implement `ICliCommand` in `CommandLine/Commands/`, giving it a `Name`, its own
`Usage` text, and its own option parsing through `OptionParser`. Register it in
the `CliApplication` constructor. Exactly one command has a `null` name and
handles a command line with no command word. Exit codes come from `ExitCode`.

## Local Git Hook

Install the repository-owned pre-commit hook once per clone:

```bash
./scripts/install-hooks.sh
```

This sets the clone's `core.hooksPath` to `.githooks`. The hook runs
[`scripts/validate.sh`](../scripts/validate.sh), whose checks are documented in
[docs/testing.md](testing.md), and then publishes the `win-x64` desktop and CLI
applications. A validation or Windows publish failure blocks the commit.
Successful commits leave the binaries under `artifacts/win-x64`.

This hook is the repository's local CI/CD pipeline. There is intentionally no
GitHub Actions or remote release workflow; other runtime builds and all
distribution remain explicit local maintainer actions.

## Local Release Builds

Run:

```bash
./scripts/build-release.sh
```

The default publishes self-contained, single-file desktop and CLI applications
for `win-x64` and `linux-x64`. .NET and Avalonia do not require a Windows
workload, so Windows `.exe` files can be cross-published from Linux.

Supported runtime identifiers:

- `win-x64`
- `win-arm64`
- `linux-x64`
- `linux-arm64`

Examples:

```bash
./scripts/build-release.sh --runtime win-x64
./scripts/build-release.sh --runtime win-x64 --runtime linux-arm64
./scripts/build-release.sh --dry-run
```

Artifacts go to `artifacts/<runtime>/desktop` and
`artifacts/<runtime>/cli`. The script does not upload, tag, sign, or delete
anything. Runtime graphs for every supported RID are declared centrally and
recorded in the committed lock files; publishing uses locked restore mode.
Distribution is a separate explicit maintainer action.

The application version is set centrally through `VersionPrefix` in
[Directory.Build.props](../Directory.Build.props).

## Dependency Updates

Package versions are centralized in
[Directory.Packages.props](../Directory.Packages.props). SDK selection and the
test runner are centralized in [global.json](../global.json).

When updating a package:

1. Change the central version.
2. Run `dotnet restore PlaylistGenerator.slnx --force-evaluate`.
3. Review every changed `packages.lock.json`.
4. Run the full validation workflow.
5. Update existing documentation if runtime or build behavior changed.

Do not hand-edit lock files.

## Documentation Boundaries

- Keep user behavior and commands in `README.md`.
- Keep architecture, dependency, hook, and release workflow here.
- Keep exact validation commands and test scope in `docs/testing.md`.
- Cross-reference an existing source section instead of duplicating it.
