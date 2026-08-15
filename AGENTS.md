# AGENTS

Use repository documentation as the primary reference before changing code.

## Read order

1. [README.md](README.md) for product behavior and supported usage.
2. [docs/maintainer-guide.md](docs/maintainer-guide.md) for architecture,
   maintenance, hooks, and local release builds.
3. [docs/testing.md](docs/testing.md) for validation commands and test scope.
4. [Cargo.toml](Cargo.toml), [Cargo.lock](Cargo.lock),
   [rust-toolchain.toml](rust-toolchain.toml), [deny.toml](deny.toml), and
   [supply-chain/config.toml](supply-chain/config.toml) as tooling sources of
   truth.

## Working rules

- Update existing documentation sections rather than duplicating them.
- Keep README user-facing and maintainer workflows in `docs/`.
- Keep domain and OS-independent behavior in `playlist-generator-core`.
- Keep GUI interaction testable and views composed from `egui` state.
- Keep all first-party application code safe Rust; do not weaken workspace
  warning, Clippy, dependency, license, or source policies.
- Update tests, notices, and documentation with behavior, dependency, command,
  or tooling changes.
- Validate with [docs/testing.md](docs/testing.md#validation-commands).
- Keep publishing local. Do not add remote CI, upload, signing, tagging, or
  distribution automation.
- Do not add a `Co-Authored-By` trailer or other AI/tool attribution to commit
  messages. The repository owner is the only recorded author.
