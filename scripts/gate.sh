#!/usr/bin/env bash
set -euo pipefail

# First: a drifted image fails here in a second rather than somewhere further
# down in a way that looks like a code problem.
./scripts/doctor.sh
./scripts/lint.sh
cargo test --workspace --all-features --locked
./scripts/security.sh
./scripts/release.sh linux-x64 windows-x64
