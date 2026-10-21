#!/usr/bin/env bash
set -euo pipefail

cargo fmt --all -- --check
cargo clippy --workspace --all-targets --all-features --locked -- -D warnings
# The web crate's build.rs only embeds its manifest and version resource when
# the target is Windows, and its browser launch differs there. Linting it for
# that target is the only way those paths are checked.
cargo clippy -p playlist-generator-web --all-targets --locked \
    --target x86_64-pc-windows-gnu -- -D warnings
RUSTDOCFLAGS='-D warnings' cargo doc --workspace --no-deps --locked
shellcheck scripts/*.sh .githooks/pre-commit .githooks/post-commit
