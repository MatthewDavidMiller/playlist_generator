# Testing Guide

This is the validation source of truth. Product behavior is in
[README.md](../README.md), and architecture/releases are in
[maintainer-guide.md](maintainer-guide.md).

## Validation commands

Bootstrap the pinned container and tracked hook once:

```bash
make install
```

Run the complete pre-commit acceptance gate:

```bash
make gate
```

The gate runs, in order:

1. `cargo fmt --all -- --check`
2. strict workspace/all-target Clippy
3. warning-free workspace rustdoc
4. ShellCheck over scripts and the hook
5. all workspace tests with locked dependencies
6. cargo-deny and cargo-vet
7. Trivy vulnerability, secret, and misconfiguration scans
8. generated-notice comparison
9. verified Linux x64 and Windows x64 release packaging

Every stage runs through [`scripts/container.sh`](../scripts/container.sh).
Focused targets are:

```bash
make lint
make test
make coverage
make security
make build-linux
make build-windows
```

Coverage HTML is produced by `cargo llvm-cov`. `make release-all` adds both
ARM64 packages and is the full release acceptance command.

## Test scope

Core tests cover supported extensions, recursive scanning, symlink/reparse
boundaries, unreadable trees, component containment, UTF-8/CR/LF path rules,
playlist insertion and atomicity, executable lookup, loudness parsing and
filter injection, FFmpeg argument boundaries, bounded pipe draining,
process-group cancellation, normalization planning/resume/collisions,
no-clobber persistence, fixed concurrency, monotonic progress, pause/cancel,
stopped-result reporting, and deterministic per-file failures.

CLI tests cover command/help parsing, human and NDJSON contracts, final typed
results, failure and interruption exit codes, and non-installing prerequisite
advice. GUI tests use `egui_kittest` accessibility trees for top navigation,
enabled states, Activity controls/logs, and About/legal content, and they assert
layout geometry from the same trees: no control leaves the window at the minimum
size, the default size, 150%/200% display scaling, or a full-screen width; the
column stays centered and stops widening; the browse buttons reflow only when
the column is tight. No display or FFmpeg is required.

Shell contract tests cover engine preference/override, install behavior, hook
ordering and failure propagation, release target selection, package staging,
and the absence of gate bypasses. Controlled helper executables exercise real
pipe and process-tree behavior without relying on FFmpeg.

Add a regression test for every defect. Inject catalog/process/shuffle behavior
when a unit test does not need the real filesystem or process layer.

## Artifact acceptance

`scripts/verify-artifact.sh` rejects packages whose ELF files are not PIE with
NX/RELRO, contain RPATH/RUNPATH, or have the wrong architecture. It rejects PE
files with the wrong console/GUI subsystem or without ASLR, DEP, and high
entropy VA, and rejects unexpected non-system runtime DLL dependencies.

Each platform directory must also contain both binaries, the repository
license, generated notices, and a non-empty CycloneDX SBOM.
