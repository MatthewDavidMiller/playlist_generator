# AGENTS

Use repository documentation as the primary reference before changing code.

## Read order

1. [README.md](README.md) for product behavior and supported usage.
2. [docs/maintainer-guide.md](docs/maintainer-guide.md) for architecture,
   maintenance, hooks, and local release builds.
3. [docs/testing.md](docs/testing.md) for validation commands and test scope.
4. [Cargo.toml](Cargo.toml), [Cargo.lock](Cargo.lock),
   [rust-toolchain.toml](rust-toolchain.toml), [Containerfile](Containerfile),
   [compose.yaml](compose.yaml), [deny.toml](deny.toml), and
   [supply-chain/config.toml](supply-chain/config.toml) as tooling sources of
   truth.

## Working rules

- Update existing documentation sections rather than duplicating them.
- Keep README user-facing and maintainer workflows in `docs/`.
- Keep domain and OS-independent behavior in `playlist-generator-core`.
- Keep frontend interaction testable: the web pages rendered from `App` state
  and asserted as HTML, the terminal views drawn from `App` state and asserted
  through ratatui's test backend.
- Package the web frontend for Windows and Linux and the terminal frontend for
  Linux. Keep the web frontend on the standard library and
  `playlist-generator-core` alone: no third-party crates and no JavaScript.
- Keep all first-party application code safe Rust; do not weaken workspace
  warning, Clippy, dependency, license, or source policies.
- Update tests, notices, and documentation with behavior, dependency, command,
  or tooling changes.
- Validate with [docs/testing.md](docs/testing.md#validation-commands).
- Keep publishing local. Do not add a hosted CI runner or any workflow that
  builds, tests, or releases off this machine. Releases are cut by
  `scripts/publish-github-release.sh`, which runs the containerized builds
  locally and then uses the developer's own authenticated `gh`.
- Do not add a `Co-Authored-By` trailer or other AI/tool attribution to commit
  messages. The repository owner is the only recorded author.
