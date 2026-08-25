#!/usr/bin/env bash
set -euo pipefail

cargo fmt --all -- --check
cargo clippy --workspace --all-targets --all-features --locked -- -D warnings
# The desktop crate carries code no host-target lint ever sees: build.rs, the
# windows subsystem attribute, and the startup log that replaces the console it
# does not have. Linting it for the target it ships to is the only way those
# are checked.
cargo clippy -p playlist-generator-gui --all-targets --locked \
    --target x86_64-pc-windows-gnu -- -D warnings
RUSTDOCFLAGS='-D warnings' cargo doc --workspace --no-deps --locked
shellcheck scripts/*.sh .githooks/pre-commit .githooks/post-commit
