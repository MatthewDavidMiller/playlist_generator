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
    # The desktop build is packaged for Windows only and the terminal build for
    # Linux only. See crates/playlist-generator-tui/src/main.rs for why.
    case ${package} in
        linux-x64)
            target=x86_64-unknown-linux-musl; suffix=
            frontend=playlist-generator-tui ;;
        linux-arm64)
            target=aarch64-unknown-linux-musl; suffix=
            frontend=playlist-generator-tui ;;
        windows-x64)
            target=x86_64-pc-windows-gnu; suffix=.exe
            frontend=playlist-generator-gui ;;
        *) echo "Unsupported release package: ${package}" >&2; exit 2 ;;
    esac

    cargo build --release --locked --target "${target}" -p playlist-generator -p "${frontend}"
    stage="artifacts/${package}"
    rm -rf "${stage}"
    mkdir -p "${stage}/bin"
    install -m 0755 "${target_dir}/${target}/release/playlist-generator${suffix}" "${stage}/bin/playlist-generator${suffix}"
    install -m 0755 "${target_dir}/${target}/release/${frontend}${suffix}" "${stage}/bin/${frontend}${suffix}"
    install -m 0644 LICENSE THIRD_PARTY_NOTICES.txt "${stage}/"
    cargo cyclonedx --format json --manifest-path "crates/${frontend}/Cargo.toml"
    sbom=$(find "crates/${frontend}" -maxdepth 1 -name '*.cdx.json' -print -quit)
    test -n "${sbom}"
    install -m 0644 "${sbom}" "${stage}/playlist-generator.cdx.json"
    ./scripts/verify-artifact.sh "${package}" "${stage}"
done
