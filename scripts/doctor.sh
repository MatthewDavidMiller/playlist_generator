#!/usr/bin/env bash
set -euo pipefail

rustc --version | grep -F '1.97.1'
cargo --version
for tool in cargo-deny cargo-vet cargo-about cargo-cyclonedx cargo-llvm-cov shellcheck trivy; do
    command -v "${tool}" >/dev/null
done
# The artifact verifier reads ELF and PE headers with these.
for tool in readelf strings file llvm-readobj; do
    command -v "${tool}" >/dev/null
done
# The Windows cross toolchain: the linker, and the resource compiler build.rs
# calls to embed the manifest and version block.
x86_64-w64-mingw32-gcc --version >/dev/null
x86_64-w64-mingw32-windres --version >/dev/null
# aarch64 musl has no cross toolchain; rustc links it itself with this.
test -n "$(find "$(rustc --print sysroot)" -name 'rust-lld' -print -quit)"
for target in x86_64-unknown-linux-musl aarch64-unknown-linux-musl x86_64-pc-windows-gnu; do
    rustup target list --installed | grep -Fqx "${target}"
done
echo "Developer toolchain is ready."
