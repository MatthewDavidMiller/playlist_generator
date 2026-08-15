#!/usr/bin/env bash
set -euo pipefail

cargo fmt --all -- --check
cargo clippy --workspace --all-targets --all-features --locked -- -D warnings
RUSTDOCFLAGS='-D warnings' cargo doc --workspace --no-deps --locked
shellcheck scripts/*.sh .githooks/pre-commit
