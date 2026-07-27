# Testing Guide

This is the validation reference for the repository. For user behavior, see
[README.md](../README.md). For architecture and release workflow, see
[docs/maintainer-guide.md](maintainer-guide.md).

## Sources of Truth

- [Directory.Build.props](../Directory.Build.props) defines compiler,
  analyzer, deterministic-build, and lock-file rules.
- [Directory.Packages.props](../Directory.Packages.props) pins package versions.
- [global.json](../global.json) selects the .NET 10 SDK.
- [`.githooks/pre-commit`](../.githooks/pre-commit) defines the local CI/CD
  pipeline.
- [`scripts/validate.sh`](../scripts/validate.sh) defines its validation stage.

## Validation Commands

Run the validation stage used by the Git hook:

```bash
./scripts/validate.sh
```

For isolated diagnostics, run its commands in order:

```bash
./scripts/test-pre-commit-hook.sh
dotnet restore PlaylistGenerator.slnx --locked-mode
dotnet format PlaylistGenerator.slnx --no-restore --verify-no-changes
dotnet build PlaylistGenerator.slnx \
  --configuration Release \
  --no-restore \
  --disable-build-servers \
  --maxcpucount:1
dotnet test PlaylistGenerator.slnx \
  --configuration Release \
  --no-build \
  --no-restore \
  --disable-build-servers \
  --maxcpucount:1
```

Warnings are errors. Package restore must match committed lock files.

## Code Coverage

The xUnit v3 project uses the .NET test SDK and Coverlet collector. Generate
Cobertura coverage with:

```bash
dotnet test PlaylistGenerator.slnx \
  --collect:"XPlat Code Coverage" \
  --results-directory TestResults
```

The report is written below the test project's `TestResults` output.

## Test Layout

`tests/PlaylistGenerator.Tests/` mirrors the source layout:

- `Core/` covers `PlaylistGenerator.Core`, one test class per type.
- `CommandLine/` covers argument parsing and command dispatch.
- `Presentation/` covers the view models and path suggestions.
- `Desktop/` covers the Avalonia window on the headless platform.
- `TestSupport/` holds fakes and fixtures, one type per file.

No test requires FFmpeg or a display server. `FakeProcessRunner` stands in for
FFmpeg, `FakeExecutableLocator` keeps resolution off the machine's `PATH`, and
`Avalonia.Headless.XUnit` runs the real window without one.

Tests that observe normalization must synchronize on a reported signal rather
than a bare `Task.Yield`, because files are processed on thread-pool workers.
`AudioNormalizationServiceTests.CreateService` runs one file at a time so that
ordering assertions stay meaningful; the concurrency tests opt in explicitly.

## Test Scope

The suite covers:

- Recursive audio discovery, extension handling, ordering, validation,
  symbolic-link boundaries, and link cycles.
- Path normalization: home expansion, relative segments, platform case rules,
  and containment that does not treat `/musicbox` as a child of `/music`.
- Pure interval-playlist composition, special-file exclusion, UTF-8 and
  non-ASCII output, absence of leftover temporary files, and preservation of an
  existing playlist when input disappears.
- FFmpeg argument construction without shell parsing.
- Real process argument boundaries, shell-metacharacter inertness, output
  larger than a pipe buffer, diagnostics, start failures, process-tree
  cancellation, and absence of unobserved task exceptions after cancellation.
- Executable resolution by explicit path and by search path, including
  precedence, blank entries, and permission checks.
- Loudness JSON extraction from mixed log output, quoted and bare numeric
  values, and malformed, missing, and empty-field diagnostics.
- Normalization planning: relative paths, output-tree skips, resumable existing
  outputs, parent/child output layouts, and destination collisions.
- Normalization execution: Opus settings, untouched sources, progress
  invariants, pause between passes, cancellation while paused, retention of
  files that already finished, FFmpeg failures, diagnostic truncation, and
  missing output after a false-success process result.
- Normalization concurrency: every file converted when several run at once,
  observed overlap bounded by the configured worker count, counts that stay
  self-consistent and never run backwards, rejection of a worker count below
  one, and an unexpected fault reaching the caller unwrapped rather than buried
  in an `AggregateException`.
- Per-file failure handling: a broken file is recorded rather than thrown, the
  files around it still produce output, a failed file counts as completed for
  progress, and a source that disappears fails only itself.
- Rejection of an output folder equal to the source folder, which would
  otherwise silently skip every file.
- Pause signalling: non-blocking waits, idempotent pause and resume, release of
  every waiter, and cancellation while paused.
- CLI parsing, JSON contracts, usage errors, per-command help, exit codes
  including the internal-error path, and non-executing FFmpeg installation
  advice.
- Composition root wiring for both the default and substituted graphs, and the
  CLI application the root builds.
- Atomic writes: a created destination directory, a write that cannot succeed,
  and the absence of a stranded temporary file afterwards.
- Unreadable source trees, executable lookups that match nothing, and a
  directory that shares an executable's name.
- Loudness JSON taken from either FFmpeg output stream.
- View-model path suggestions, request mapping, shared status and busy state,
  progress reset between runs, theme delegation, disposal while a run is in
  flight, failure counts and detail, and pause/resume/stop coordination.
- AXAML compiled bindings and platform adapter compatibility through the
  Release solution build, and window construction, live binding values, tab
  content, and close-cancels-the-run through the headless tests.

Add regression tests for every defect fixed. Prefer core and view-model tests
over tests that require a display server.

## Release Build Validation

Inspect publish commands without restoring runtime packs:

```bash
./scripts/build-release.sh --dry-run
```

Produce and inspect a real Windows cross-build on Linux:

```bash
./scripts/build-release.sh --runtime win-x64
test -x artifacts/win-x64/desktop/PlaylistGenerator.exe
test -x artifacts/win-x64/cli/playlist-generator.exe
```

The Windows executable bit is a Unix filesystem attribute only; functional GUI
smoke testing still needs a supported Windows system.

## Hook Behavior

After `./scripts/install-hooks.sh`, every `git commit` runs the full validation
script and then `./scripts/build-release.sh --runtime win-x64`. A hook
regression, format, build, analyzer, lock-file, test, or Windows publish failure
blocks the commit. A successful commit refreshes:

- `artifacts/win-x64/desktop/PlaylistGenerator.exe`
- `artifacts/win-x64/cli/playlist-generator.exe`

The repository has no remote CI substitute, so do not bypass the hook without
running both stages manually.
