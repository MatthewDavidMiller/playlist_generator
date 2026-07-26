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
Avalonia desktop ─┐
                  ├──> Core models, contracts, and services
CLI ──────────────┘
```

- `PlaylistGenerator.Core` owns scanning, playlist composition, atomic writes,
  FFmpeg command construction, process cancellation, and normalization.
- `PlaylistGenerator.Presentation` owns the platform-neutral MVVM state and
  commands.
- `PlaylistGenerator.Desktop` contains AXAML views, file-picker and theme
  adapters, composition, and the GUI entry point.
- `PlaylistGenerator.CommandLine` owns testable command parsing and
  console/JSON presentation.
- `PlaylistGenerator.Cli` is the thin console executable host.

Keep operating-system APIs and Avalonia types out of the core project. Add
domain behavior tests before adding view-specific code.

## Local Git Hook

Install the repository-owned pre-commit hook once per clone:

```bash
./scripts/install-hooks.sh
```

This sets the clone's `core.hooksPath` to `.githooks`. The hook runs
[`scripts/validate.sh`](../scripts/validate.sh), whose checks are documented in
[docs/testing.md](testing.md).

There is intentionally no GitHub Actions or remote release workflow. Validation
and release publishing are local and explicit.

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
