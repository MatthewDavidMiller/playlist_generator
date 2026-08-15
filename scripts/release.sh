#!/usr/bin/env bash
set -euo pipefail

cleanup_sboms() {
    find crates -mindepth 2 -maxdepth 2 -type f -name '*.cdx.json' -delete
}
cleanup_sboms
trap cleanup_sboms EXIT

if [[ $# -eq 0 ]]; then set -- linux-x64 windows-x64; fi
target_dir=${CARGO_TARGET_DIR:-target}

for package in "$@"; do
    case ${package} in
        linux-x64) target=x86_64-unknown-linux-gnu; suffix= ;;
        linux-arm64) target=aarch64-unknown-linux-gnu; suffix= ;;
        windows-x64) target=x86_64-pc-windows-gnullvm; suffix=.exe ;;
        windows-arm64) target=aarch64-pc-windows-gnullvm; suffix=.exe ;;
        *) echo "Unsupported release package: ${package}" >&2; exit 2 ;;
    esac

    cargo build --release --locked --target "${target}" -p playlist-generator -p playlist-generator-gui
    stage="artifacts/${package}"
    rm -rf "${stage}"
    mkdir -p "${stage}/bin"
    install -m 0755 "${target_dir}/${target}/release/playlist-generator${suffix}" "${stage}/bin/playlist-generator${suffix}"
    install -m 0755 "${target_dir}/${target}/release/playlist-generator-gui${suffix}" "${stage}/bin/playlist-generator-gui${suffix}"
    install -m 0644 LICENSE THIRD_PARTY_NOTICES.txt "${stage}/"
    cargo cyclonedx --format json --manifest-path crates/playlist-generator-gui/Cargo.toml
    sbom=$(find crates/playlist-generator-gui -maxdepth 1 -name '*.cdx.json' -print -quit)
    test -n "${sbom}"
    install -m 0644 "${sbom}" "${stage}/playlist-generator.cdx.json"
    ./scripts/verify-artifact.sh "${package}" "${stage}"
done
