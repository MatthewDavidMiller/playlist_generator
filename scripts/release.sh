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
    # The web frontend is packaged everywhere: it is a standard-library HTTP
    # server, so it links statically on Linux and needs nothing but a browser.
    # The terminal frontend is packaged for Linux only.
    case ${package} in
        linux-x64)
            target=x86_64-unknown-linux-musl; suffix=
            crates=(playlist-generator playlist-generator-tui playlist-generator-web) ;;
        linux-arm64)
            target=aarch64-unknown-linux-musl; suffix=
            crates=(playlist-generator playlist-generator-tui playlist-generator-web) ;;
        windows-x64)
            target=x86_64-pc-windows-gnu; suffix=.exe
            crates=(playlist-generator playlist-generator-web) ;;
        *) echo "Unsupported release package: ${package}" >&2; exit 2 ;;
    esac

    packages=()
    for crate in "${crates[@]}"; do packages+=(-p "${crate}"); done
    cargo build --release --locked --target "${target}" "${packages[@]}"
    stage="artifacts/${package}"
    rm -rf "${stage}"
    mkdir -p "${stage}/bin"
    install -m 0644 LICENSE THIRD_PARTY_NOTICES.txt "${stage}/"
    # One SBOM per shipped binary, named after it.
    for crate in "${crates[@]}"; do
        install -m 0755 "${target_dir}/${target}/release/${crate}${suffix}" "${stage}/bin/${crate}${suffix}"
        cargo cyclonedx --format json --manifest-path "crates/${crate}/Cargo.toml"
        sbom=$(find "crates/${crate}" -maxdepth 1 -name '*.cdx.json' -print -quit)
        test -n "${sbom}"
        install -m 0644 "${sbom}" "${stage}/${crate}.cdx.json"
        cleanup_sboms
    done
    ./scripts/verify-artifact.sh "${package}" "${stage}"
done
