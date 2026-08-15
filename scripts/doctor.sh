#!/usr/bin/env bash
set -euo pipefail

rustc --version | grep -F '1.97.1'
cargo --version
for tool in cargo-deny cargo-vet cargo-about cargo-cyclonedx cargo-llvm-cov shellcheck trivy; do
    command -v "${tool}" >/dev/null
done
x86_64-w64-mingw32-gcc --version >/dev/null
aarch64-linux-gnu-gcc --version >/dev/null
aarch64-w64-mingw32-clang --version >/dev/null
echo "Developer toolchain is ready."
