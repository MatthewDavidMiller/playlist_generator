# Maintainer Guide

User-facing behavior is documented in [README.md](../README.md). This guide
covers architecture, supply-chain policy, hooks, and local releases.

## Architecture

The Rust 2024 workspace is versioned at 0.9.0 and pinned to Rust 1.97.1:

```text
playlist-generator-gui ─┐
                        ├──> playlist-generator-core
playlist-generator ─────┘
```

- `playlist-generator-core` owns typed requests/results, validation, scanning,
  playlist composition, atomic writes, FFmpeg execution, normalization,
  fixed-worker concurrency, progress, and `RunControl`.
- `playlist-generator` is the `clap` CLI and its human/NDJSON presenters.
- `playlist-generator-gui` owns the `eframe` shell and asynchronous `rfd`
  pickers. Keep interaction state here and operating-system-independent rules
  in core.

All first-party crates inherit `unsafe_code = "forbid"`, warning denial, and
strict Clippy policy. Release panic unwinding remains enabled so temporary-file
and process cleanup runs.

### Normalization invariants

Planning completes before FFmpeg starts. It skips the output subtree and
existing results, maps relative source paths to `.opus`, and rejects collisions.
A scoped fixed worker pool shares one synchronized progress state, so counters
are monotonic and planner skips are emitted once.

Each worker performs FFmpeg `loudnorm` analysis, validates all five measurement
fields as finite numbers, then encodes Opus 160k VBR with metadata and without
video/cover art. FFmpeg is resolved to an absolute executable and receives
discrete arguments. `command-group` contains descendants in a Unix process
group or Windows Job Object. Both pipes drain concurrently with a 1 MiB tail
bound; surfaced diagnostics retain at most 4 KiB.

Encoding uses a same-directory temporary file. A no-clobber hard-link persist
step prevents races from replacing completed output, after which the temporary
name is removed. Cancellation kills and reaps the group and cleanup removes an
incomplete temporary file.

## Container workflow

[`Containerfile`](../Containerfile) pins its base image by digest and installs
Rust, GUI headers, Linux/Windows cross linkers, ShellCheck, Trivy, cargo-deny,
cargo-vet, cargo-about, cargo-cyclonedx, and cargo-llvm-cov at the versions
declared there.

[`scripts/container.sh`](../scripts/container.sh) is the single wrapper used by
Make. It runs as the invoking UID/GID, mounts no engine socket, drops all
capabilities, enables `no-new-privileges`, applies process/memory bounds, and
uses named Cargo/target caches. Podman workspace mounts use SELinux relabeling.

Authoritative targets are `install`, `lint`, `test`, `coverage`, `security`,
`build-linux`, `build-windows`, `build-release`, `release-all`, `gate`, and
`clean`. Exact commands and order are documented in
[testing.md](testing.md#validation-commands).

## Local hook

`make install` sets `core.hooksPath=.githooks`. The tracked pre-commit hook
unsets Git repository variables inherited by nested test repositories and runs
`make gate`. Formatting, Clippy, rustdoc, ShellCheck, tests, supply-chain
checks, Trivy, notice verification, or either x64 release failure blocks the
commit. There is no skip path and no remote CI replacement.

## Dependencies and notices

Use exact, registry-hosted workspace dependency declarations. Git, wildcard,
unknown-registry, unapproved-license, warning, `unwrap`, and `expect` usage are
rejected by policy. After changing dependencies:

1. Update `Cargo.toml` and regenerate `Cargo.lock` normally.
2. Review the lockfile and `cargo deny check` results.
3. Update cargo-vet exemptions/audits deliberately.
4. Run `make notices` and review the generated legal text.
5. Run `make gate`.

Do not hand-edit generated notices.

## Local releases

`make build-release` builds and verifies Linux x64 and Windows x64. ARM64 is
explicitly outside the commit hook; `make release-all` also builds
`aarch64-unknown-linux-gnu` and `aarch64-pc-windows-gnullvm`.

Each `artifacts/<platform>/` package contains both binaries under `bin/`,
`LICENSE`, `THIRD_PARTY_NOTICES.txt`, and a CycloneDX JSON SBOM. Verification
checks ELF/PE architecture and hardening, CLI versus GUI PE subsystems,
forbidden ELF runtime paths, and unexpected Windows runtime DLLs.

Publishing is deliberately local. These scripts do not upload, tag, sign, or
otherwise distribute artifacts.
