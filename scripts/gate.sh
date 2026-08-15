#!/usr/bin/env bash
set -euo pipefail

./scripts/lint.sh
cargo test --workspace --all-features --locked
./scripts/security.sh
./scripts/release.sh linux-x64 windows-x64
