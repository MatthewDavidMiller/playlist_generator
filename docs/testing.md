# Testing Guide

This is the validation source of truth. Product behavior is in
[README.md](../README.md), and architecture/releases are in
[maintainer-guide.md](maintainer-guide.md).

## Validation commands

Docker with Compose v2 is the only prerequisite. Every compiler, linter,
scanner, and test runner lives in the toolbox image, so the commands below are
the same on Windows and Linux.

Bootstrap the image and the tracked hooks once:

```bash
./scripts/setup.sh
```

On Windows, where there is no uid to map, do the same two steps directly:

```powershell
docker compose build
git config core.hooksPath .githooks
```

Run the complete pre-commit acceptance gate:

```bash
docker compose run --rm gate
```

The gate runs, in order:

1. `scripts/doctor.sh`, so a drifted image fails immediately
2. `cargo fmt --all -- --check`
3. strict workspace/all-target Clippy
4. strict Clippy for `playlist-generator-web` against `x86_64-pc-windows-gnu`
5. warning-free workspace rustdoc
6. ShellCheck over scripts and both hooks
7. all workspace tests with locked dependencies
8. cargo-deny and cargo-vet
9. Trivy vulnerability, secret, and misconfiguration scans
10. generated-notice comparison
11. verified Linux x64 and Windows x64 release packaging

Focused services are:

```bash
docker compose run --rm lint
docker compose run --rm test
docker compose run --rm coverage
docker compose run --rm security
docker compose run --rm notices
docker compose run --rm build-linux
docker compose run --rm build-windows
docker compose run --rm shell
```

Coverage HTML is produced by `cargo llvm-cov`. `docker compose run --rm
release-all` adds the Linux ARM64 package and is the full release acceptance
command; `scripts/publish-github-release.sh` runs it before publishing.

Rebuild the image after any change to `Containerfile` or
`scripts/install-trivy.sh`:

```bash
docker compose build
```

## Test scope

Core tests cover supported extensions, recursive scanning, symlink/reparse
boundaries, unreadable trees, component containment, UTF-8/CR/LF path rules,
playlist insertion and atomicity, executable lookup, loudness parsing and
filter injection, FFmpeg argument boundaries, bounded pipe draining,
process-group cancellation, normalization planning/resume/collisions,
no-clobber persistence, fixed concurrency, monotonic progress, pause/cancel,
stopped-result reporting, and deterministic per-file failures.

Web tests cover the same interaction surface without a browser. `http` tests
parse raw requests: form and query decoding, cookies, bounded heads and bodies,
and refusal of other methods, chunked bodies, bad escapes, and components that
are not UTF-8; they also cover escaping and the headers every response
carries. `server` tests call the router directly: the launch link setting the
cookie and leaving the address bar, pages refused without it, a rebound host
name refused even with it, cross-site posts refused, Browse… keeping what was
typed and returning with the choice, the folder browser listing a real folder,
starting a run moving to the page that reports it, and Quit. `pages` tests
assert on the rendered HTML: every control and its guidance, the action button
preceding the browse buttons so Enter presses it, typed paths escaped into
their boxes, the navigation marking the open page, the forms locking and
Activity refreshing only during a run, progress counts and newest-first
messages, the legal documents folded behind their headings, and the browser
offering each field only what it can use. `browse` tests list a temporary
folder for every kind of field. One test drives the real server over loopback
sockets from sign-in to Quit.

Terminal tests cover the same interaction surface without a tty. `App` tests
drive key events directly: cursor editing around multi-byte characters, focus
cycling and its wrap onto the action button, digit-only count fields, page
selection and the focus reset that follows it, run start moving to the page
that reports it, failure reaching the log and releasing the controls, and the
modified quit key. `ui` tests render into ratatui's `TestBackend` and assert on
the resulting cells: every page titles itself at 80x24, 120x40, and 200x60, no
row overflows the terminal, the form stays centered and stops widening, the
guidance text survives, the footer names the keys the current page honors,
progress reports its counts, the log keeps its newest lines, the legal
documents stay folded until asked for, and the cursor follows focus.

No display, browser, terminal, or FFmpeg is required by any test; the web
tests need only a loopback socket and the temporary directory.

CLI behavior is currently covered only through `playlist-generator-core`; the
`playlist-generator` binary itself has no tests of its command parsing or its
human and NDJSON presenters, and the shell scripts have none either. Both are
worth adding and neither exists today.

Add a regression test for every defect. Inject catalog/process/shuffle behavior
when a unit test does not need the real filesystem or process layer.

## Artifact acceptance

`scripts/verify-artifact.sh` rejects Linux packages whose ELF files are not PIE
with NX/RELRO, carry RPATH/RUNPATH, name an interpreter or a shared library, or
have the wrong architecture. The interpreter and `NEEDED` checks are what hold
the packages to being statically linked, which is what lets one Linux package
run on every distribution rather than only on musl ones.

It rejects PE files that are not console programs, without ASLR, DEP, and
high entropy VA, with unexpected non-system runtime DLL dependencies, or whose
import address table has slots belonging to no descriptor — the corruption that
killed 0.9.0 through 0.9.2.

Each platform directory must also contain the command-line and web binaries,
the terminal binary on Linux, the repository license, generated notices, and a
non-empty CycloneDX SBOM for each binary.
