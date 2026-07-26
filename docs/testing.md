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

## Test Scope

The suite covers:

- Recursive audio discovery, extension handling, ordering, validation, and
  symbolic-link boundaries.
- Pure interval-playlist composition, special-file exclusion, UTF-8 output,
  and preservation of an existing playlist when input disappears.
- FFmpeg argument construction without shell parsing.
- Real process argument boundaries, diagnostics, start failures, process-tree
  cancellation, executable permissions, and FFmpeg installation advice.
- Loudness JSON extraction and malformed/missing-field diagnostics.
- Recursive normalization, relative paths, Opus settings, output-tree skips,
  resumable existing outputs, parent/child output layouts, destination
  collisions, progress, pause, cancellation, FFmpeg failures, and missing
  output after a false-success process result.
- CLI parsing, JSON contracts, usage errors, expected failures, normalization,
  and non-executing FFmpeg installation advice.
- View-model path suggestions, request mapping, status and diagnostic state,
  theme delegation, and pause/resume/stop coordination.
- AXAML compiled bindings and platform adapter compatibility through the
  Release solution build.

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
