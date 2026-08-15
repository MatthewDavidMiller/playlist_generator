# Maintainer Guide

User-facing behavior is documented in [README.md](../README.md). This guide
covers architecture, supply-chain policy, hooks, and local releases.

## Architecture

The Rust 2024 workspace is versioned at 0.9.4 and pinned to Rust 1.97.1:

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
  in core. The native renderer is Glow on every target.

Lay pages out inside `form`, which centres one column, caps it at `FORM_WIDTH`,
and reports whether it fell under `NARROW_BREAKPOINT`. Take every width from
that column and never from the window, and give a row that fills its width a
height of its own: a text box told to fill a row pushes the button beside it out
of the window, and a `right_to_left` row with no allocated height centres itself
down the whole scroll area. Both were visible at every window size before 0.9.4,
and worst under Windows display scaling, which leaves the window fewer points to
work with.

### Windows desktop binary

The binary is linked for the Windows GUI subsystem, so it has no console and
nothing it writes to standard error is ever seen. Three rules follow.

Both Windows packages link with llvm-mingw, through the
`x86_64-pc-windows-gnullvm` and `aarch64-pc-windows-gnullvm` targets. The GNU
binutils Debian ships for `x86_64-pc-windows-gnu` are 2.40, which drops import
descriptors when one DLL is imported through several of them. The desktop
binary imported kernel32 from four descriptors; three were discarded and the
eighteen thunks they owned kept the file-relative address of their own import
name, which the loader never overwrites. The first call through one,
`GetModuleHandleA`, jumped to 0x7265c0, so 0.9.0 through 0.9.2 died with
0xC0000005 before reaching `main` — no window, no message box, and a process
too short-lived to see. The CLI, which imports far less, was unaffected.
Binutils 2.44 and LLD both emit the table correctly. `verify-artifact.sh` now
fails the release unless every import address table slot belongs to a
descriptor.

Every fatal path must reach a message box, and every launch must leave
evidence. `run` reports a failed `eframe::run_native`, an installed panic hook
reports the first panic from any thread, and both are also written to
`%LOCALAPPDATA%\PlaylistGenerator\startup.log`, which each launch rewrites with
the stage it reached. A crash below our own code — the failure above — reaches
none of the in-process reporting, and the absent log is what says so.

`build.rs` compiles `windows/playlist-generator-gui.manifest` and a
`VERSIONINFO` block with `windres` and links the result into the executable
only. Without a resource section Windows treats the binary as a pre-Vista
application: compatibility and UAC virtualisation shims apply, DPI awareness is
resolved late, and Explorer, SmartScreen, and endpoint protection see an
unnamed, unversioned executable. `windres` comes from the MinGW toolchain that
already provides the cross linkers, so this adds no build dependency; set
`WINDRES` to override the detected one. `scripts/verify-artifact.sh` fails the
release if `.rsrc`, the manifest, or the version resource is missing. LLD sets
the subsystem and OS versions to 6.00 on its own, so no linker argument does.

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
incomplete temporary file. The GUI retains and joins its operation thread when
the window closes so that this cleanup finishes before process exit.

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
