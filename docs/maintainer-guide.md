# Maintainer Guide

User-facing behavior is documented in [README.md](../README.md). This guide
covers architecture, supply-chain policy, hooks, and local releases.

## Architecture

The Rust 2024 workspace is versioned at 1.0.0-rc.1 and pinned to Rust 1.97.1:

```text
playlist-generator-web ─┐   (packaged for Windows and Linux)
playlist-generator-tui ─┼──> playlist-generator-core
playlist-generator ─────┘
```

- `playlist-generator-core` owns typed requests/results, validation, scanning,
  playlist composition, atomic writes, FFmpeg execution, normalization,
  fixed-worker concurrency, progress, and `RunControl`.
- `playlist-generator` is the `clap` CLI and its human/NDJSON presenters.
- `playlist-generator-web` is the graphical frontend: a local HTTP server and
  the HTML pages it renders, built on the standard library and core alone.
  Keep interaction state here and operating-system-independent rules in core.
- `playlist-generator-tui` owns the `ratatui` terminal frontend over the same
  core API. `app.rs` holds interaction state and takes key events; `ui.rs`
  draws from that state and nothing else, so both are tested without a tty.

### Which frontend ships where

The web crate is packaged for Windows and Linux, and the terminal crate for
Linux only.

The toolbox is Alpine, so the Linux packages are musl, and they must be static:
a dynamically linked musl binary resolves musl's loader and runs on Alpine and
nowhere else. That rules out any native windowing stack, which dlopens libX11,
libwayland, and libGL. A browser interface needs none of it — the server is
sockets, files, and processes, and the browser that draws the pages is already
on the machine — so the same crate links fully static on Linux and as an
ordinary console program on Windows. A terminal resolves nothing at run time
either, so the terminal build is static too.

### Web frontend

`playlist-generator-web` depends on `playlist-generator-core` and nothing else.
Keep it that way: the HTTP subset, the HTML, the folder browser, and the browser
launch are all first-party, and the pages carry no JavaScript.

- `http.rs` reads one request per connection, with the head bounded at 16 KiB
  and form bodies at 64 KiB, refuses chunked bodies rather than half-supporting
  them, and writes every response with `Connection: close`, `no-store`, and a
  Content-Security-Policy that allows only the server's own stylesheet and form
  targets. `escape` is the only way text reaches markup and `encode_component`
  the only way it reaches a URL.
- `server.rs` routes and enforces the boundary. Loopback is reachable by every
  local user and every page the browser has open, so `Host` must be
  `127.0.0.1:PORT` or `localhost:PORT` (DNS rebinding), every page but the
  stylesheet needs the session cookie that only the per-launch link can set,
  and posts whose `Origin` or `Sec-Fetch-Site` name another site are refused on
  top of the cookie being `SameSite=Strict`. The token is 128 bits from
  `RandomState`, which std seeds from the operating system's random source; it
  is the only such source std exposes, which is why there is no `rand` here.
- `app.rs` holds interaction state and the worker thread; `pages.rs` renders a
  complete document from that state and nothing else; `browse.rs` lists
  folders for Browse…, because a page cannot learn the real path of a file a
  browser picks.
- Every action is a form post answered with a 303, so reloading never repeats
  it. The action button is written first in each form, because Enter presses
  the first submit button, and the stylesheet moves it to the end. Activity
  reloads itself with a meta refresh only while a run is in progress.
- The page is one centered column capped at 620 pixels. The browse buttons
  stack under their boxes through a container query on that column at 430
  pixels, so the reflow follows the column rather than the window.
- Connections are served on their own threads, at most 64 at once, each with a
  15-second timeout, because browsers open spare connections they may never
  use. Quit, or a line typed into the console, sets the stop flag and wakes the
  accept loop with a connection of its own; the run is then cancelled and its
  worker joined before the process exits. There is no signal handler, which
  would need a third-party crate, so Ctrl+C skips that cleanup and the README
  says so.

### Windows packages

The Windows package links with the MinGW GNU toolchain through
`x86_64-pc-windows-gnu`. The GNU binutils Debian shipped for that target were
2.40, which drops import descriptors when one DLL is imported through several
of them. The former egui desktop binary imported kernel32 from four
descriptors; three were discarded and the eighteen thunks they owned kept the
file-relative address of their own import name, which the loader never
overwrites. The first call through one, `GetModuleHandleA`, jumped to 0x7265c0,
so 0.9.0 through 0.9.2 died with 0xC0000005 before reaching `main` — no window,
no message box, and a process too short-lived to see. The CLI, which imports
far less, was unaffected.

0.9.3 worked around it by linking with LLD through the `*-pc-windows-gnullvm`
targets. That is no longer possible: llvm-mingw publishes no musl build, so its
prebuilt toolchain cannot run on the Alpine toolbox at all. Alpine's binutils
are 2.45, past the 2.44 release that emits the table correctly, so the GNU
linker is used again — and not on trust. `verify-artifact.sh` fails the release
unless every import address table slot belongs to a descriptor, which is
exactly this corruption, and it runs on every packaged build. LLD also set the
subsystem and OS versions to 6.00 unprompted; GNU ld does not, so
`.cargo/config.toml` asks for them explicitly.

Windows ARM64 was dropped in the same change. Alpine packages no aarch64 MinGW
and llvm-mingw has no musl build, so `aarch64-pc-windows-gnullvm` cannot be
produced from the toolbox.

Both Windows binaries are console programs. The web frontend's console is where
it prints its address, where a fatal error such as a port already in use stays
readable until Enter is pressed, and where Enter stops it; closing the console
ends it. No message box or startup log is needed, because nothing it reports
is ever thrown away.

`build.rs` compiles `windows/playlist-generator-web.manifest` and a
`VERSIONINFO` block with `windres` and links the result into the web executable
only. Without a resource section Windows treats the binary as a pre-Vista
application: compatibility shims apply, the console keeps the legacy code page
rather than UTF-8, and Explorer, SmartScreen, and endpoint protection see an
unnamed, unversioned executable. `windres` comes from the MinGW toolchain that
already provides the cross linkers, so this adds no build dependency; set
`WINDRES` to override the detected one. `scripts/verify-artifact.sh` fails the
release if `.rsrc`, the manifest, or the version resource is missing.

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
incomplete temporary file. Both interactive frontends retain and join their
operation thread when they quit so that this cleanup finishes before process
exit.

## Container workflow

[`Containerfile`](../Containerfile) builds the toolbox on the official Alpine
image, pinned by digest. Alpine is what keeps the supply chain short: the GNU
userland, the MinGW cross toolchain and its binutils, LLVM's object readers,
and ShellCheck all arrive as apk-signed packages.
Three things cannot:

- Rust, because Alpine's newest is behind the pin in `rust-toolchain.toml`. It
  comes from the Rust project's own channel at the pinned version.
- The five cargo subcommands, at the versions the policy files were written
  against, from the registry.
- Trivy, which Alpine does not package, through
  [`scripts/install-trivy.sh`](../scripts/install-trivy.sh) — pinned to a
  version and a SHA-256 together, so a replaced asset fails the build.

[`compose.yaml`](../compose.yaml) is the only entry point, and it is the same
one on Windows and Linux. Every gate is a service. Services run as the invoking
uid, mount no engine socket, drop all capabilities, enable
`no-new-privileges`, and apply process and memory bounds. The Cargo registry
cache is a named volume; the build tree is left at its default `target/` so
that a gate's output is where a developer expects to find it.

`scripts/setup.sh` writes the git-ignored `.env` holding that uid, builds the
image, and installs the hooks. Windows needs no uid mapping and runs
`docker compose build` and `git config core.hooksPath .githooks` directly.

Authoritative services are `gate`, `lint`, `test`, `coverage`, `security`,
`notices`, `doctor`, `build-linux`, `build-windows`, `build-release`,
`release-all`, `clean`, and `shell`. Exact commands and order are documented in
[testing.md](testing.md#validation-commands).

## Local hook

`scripts/setup.sh` sets `core.hooksPath=.githooks`.

The tracked pre-commit hook unsets Git repository variables inherited by nested
test repositories and runs the whole gate in one container. Formatting, either
Clippy pass, rustdoc, ShellCheck, tests, supply-chain checks, Trivy, notice
verification, or either x64 release failure blocks the commit. There is no skip
path and no hosted CI replacement.

The post-commit hook packages the commit that was just made, so the version the
packages record is the committed one. It skips while a rebase, cherry-pick,
revert, or merge is replaying commits: packaging each intermediate tree would
turn a ten-commit rebase into ten cross-builds.

## Dependencies and notices

Use exact, registry-hosted workspace dependency declarations. Git, wildcard,
unknown-registry, unapproved-license, warning, `unwrap`, and `expect` usage are
rejected by policy. After changing dependencies:

1. Update `Cargo.toml` and regenerate `Cargo.lock` normally.
2. Review the lockfile and `cargo deny check` results.
3. Update cargo-vet exemptions/audits deliberately, with
   `cargo vet add-exemption <crate> <version> --criteria safe-to-deploy` for
   each one rather than regenerating the file wholesale.
4. Run `docker compose run --rm notices` and review the generated legal text.
   `about.toml` lists the release triples, so a new target needs that list
   updated first or the notices will not describe what is shipped.
5. Run `docker compose run --rm gate`.

Do not hand-edit generated notices.

## Local releases

`docker compose run --rm build-release` builds and verifies Linux x64 and
Windows x64. Linux ARM64 is outside the commit hook; `release-all` adds it.

Each `artifacts/<platform>/` package contains the command-line and web binaries
under `bin/` (Linux packages add the terminal binary), `LICENSE`,
`THIRD_PARTY_NOTICES.txt`, and one CycloneDX JSON SBOM per binary named after
it. Verification checks ELF architecture, static linkage, and hardening; PE
architecture, subsystem, resources, import-table integrity, and runtime DLL
imports.

Targets and linking live in [`.cargo/config.toml`](../.cargo/config.toml).
Both Linux packages are static-PIE musl, which Rust's musl targets already
produce by default; the config deliberately does not restate that, because a
rustflag saying so would also apply to host builds including the test
harnesses. `verify-artifact.sh` asserts both properties on every
packaged build, so a changed default fails the release rather than shipping
quietly. The ARM64 package has no cross toolchain and needs none — nothing in
the dependency graph compiles C, so rustc links it with `rust-lld` against the
musl objects `rust-std` already ships.

## Publishing

[`scripts/publish-github-release.sh`](../scripts/publish-github-release.sh) is
the only script that runs on the host rather than in the toolbox: it needs the
developer's authenticated `gh` and their push credentials, and the toolbox has
neither. The builds it depends on still run in the container.

It refuses a dirty worktree, a version other than `vMAJOR.MINOR.PATCH` or a
release candidate `vMAJOR.MINOR.PATCH-rc.N`, a tag that
already points somewhere other than HEAD, and a detached HEAD or a branch with
no upstream. It exits without rebuilding if the release already exists. Archives
are built with fixed ownership, ordering, and timestamps so a retry is
byte-reproducible, accompanied by `SHA256SUMS`. The branch and tag are pushed
atomically, because the API cannot attach a release to a commit GitHub has not
received. 0.x versions and release candidates are published as GitHub
pre-releases.

There is no hosted CI runner and nothing builds, tests, or releases off this
machine.
