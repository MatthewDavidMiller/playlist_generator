#!/usr/bin/env bash
# Run a gate inside the toolbox image, under rootless Podman.
#
# Usage: ./scripts/toolbox.sh <gate>        gate, lint, test, coverage,
#                                           security, notices, doctor,
#                                           build-linux, build-windows,
#                                           build-release, release-all,
#                                           clean, or shell
#        ./scripts/toolbox.sh --build       build (or rebuild) the image only
#        ./scripts/toolbox.sh -- <command>  run any command in the toolbox
#
# Rootless Podman is the only thing this needs from the host; every compiler,
# linter, scanner, and test runner lives in the Containerfile's image. The
# gates keep the names they had as compose services, so the command surface
# did not change when Docker Compose stopped being a prerequisite.
#
# Windows hosts run this from WSL, as the sibling projects do.

set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd "${script_dir}/.." && pwd)
containerfile="${repo_root}/Containerfile"
toolbox_repo=playlist-generator-toolbox
cargo_volume="${PG_TOOLBOX_CARGO_VOLUME:-playlist-generator-toolbox-cargo}"

usage() {
    sed -n '2,15p' "${BASH_SOURCE[0]}"
}

# Already inside the toolbox (a gate calling another gate): run the command
# instead of nesting a container.
if [[ -n "${PG_IN_TOOLBOX:-}" && "${1:-}" != "--build" ]]; then
    [[ $# -gt 0 ]] || { usage >&2; exit 2; }
    if [[ "$1" == "--" ]]; then shift; fi
    exec "$@"
fi

command -v podman >/dev/null 2>&1 || { echo "ERROR: podman is required" >&2; exit 1; }
if [[ "$(id -u)" -eq 0 ]]; then
    echo "ERROR: run the toolbox as a regular user (rootless Podman)" >&2
    exit 1
fi

# The tag is the digest of everything that determines what lands in the image,
# so a changed pin names a different image and the rebuild happens on the next
# gate run rather than whenever somebody remembers to force one.
toolbox_tag() {
    local digest
    digest=$(cat "${containerfile}" "${repo_root}/scripts/install-trivy.sh" |
        sha256sum | cut -c1-16)
    printf '%s:%s\n' "${toolbox_repo}" "${digest}"
}

# Content-hash tags otherwise strand every superseded image. A failure means a
# gate is still using it; it is collected on the next build.
prune_superseded_toolboxes() {
    local keep="$1" image
    while read -r image; do
        [[ -n "${image}" && "${image}" != "${keep}" ]] || continue
        podman rmi "${image}" >/dev/null 2>&1 || true
    done < <(podman images --format '{{.Repository}}:{{.Tag}}' 2>/dev/null |
        sed 's|^localhost/||' | grep "^${toolbox_repo}:" || true)
}

build_toolbox() {
    local tag="$1" platform
    echo "=== Building toolbox image ${tag} ===" >&2
    # The platform is explicit because the base is pinned by manifest digest,
    # and Podman resolves a digest to whatever variant it already holds
    # locally: an arm64 copy pulled by another project would otherwise yield an
    # emulated arm64 toolbox.
    case "$(uname -m)" in
        x86_64 | amd64) platform=linux/amd64 ;;
        aarch64 | arm64) platform=linux/arm64 ;;
        *) echo "ERROR: unsupported workstation architecture $(uname -m)" >&2; return 1 ;;
    esac
    podman build --format docker --platform "${platform}" \
        -f "${containerfile}" -t "${tag}" "${repo_root}"
    prune_superseded_toolboxes "${tag}"
}

# The command each gate runs. Kept in one place so the names match the ones
# docs/testing.md documents.
gate_command() {
    case "$1" in
        gate) command=(bash scripts/gate.sh) ;;
        lint) command=(bash scripts/lint.sh) ;;
        test) command=(cargo test --workspace --all-features --locked) ;;
        coverage) command=(cargo llvm-cov --workspace --all-features --locked --html) ;;
        security) command=(bash scripts/security.sh) ;;
        notices) command=(bash scripts/notices.sh) ;;
        doctor) command=(bash scripts/doctor.sh) ;;
        build-linux) command=(bash scripts/release.sh linux-x64) ;;
        build-windows) command=(bash scripts/release.sh windows-x64) ;;
        build-release) command=(bash scripts/release.sh linux-x64 windows-x64) ;;
        release-all) command=(bash scripts/release.sh linux-x64 linux-arm64 windows-x64) ;;
        clean) command=(cargo clean) ;;
        shell) command=(bash) ;;
        *) echo "ERROR: unknown gate '$1'" >&2; usage >&2; return 2 ;;
    esac
}

main() {
    local tag
    local -a command=() tty=()
    case "${1:-}" in
        "" | -h | --help) usage; [[ -n "${1:-}" ]] && exit 0 || exit 2 ;;
        --build) build_toolbox "$(toolbox_tag)"; exit 0 ;;
        --) shift; [[ $# -gt 0 ]] || { usage >&2; exit 2; }; command=("$@") ;;
        *) gate_command "$1" || exit 2 ;;
    esac
    [[ "${1:-}" == shell ]] && tty=(-it)

    tag=$(toolbox_tag)
    podman image inspect "${tag}" >/dev/null 2>&1 || build_toolbox "${tag}"

    # --userns=keep-id runs the container as the invoking uid, so a gate leaves
    # no root-owned files in the tree and needs no uid bookkeeping of its own.
    # No engine socket is mounted: no gate here builds or scans another image.
    # The Cargo registry cache is a named volume, so downloads survive between
    # gate runs; the build tree stays at the default target/, where a developer
    # looks for it.
    exec podman run --rm --init "${tty[@]}" \
        --userns=keep-id \
        --cap-drop=ALL \
        --security-opt=no-new-privileges \
        --pids-limit=1024 \
        --memory=8g \
        -v "${repo_root}:/workspace:Z" \
        -v "${cargo_volume}:/cargo" \
        -w /workspace \
        -e HOME=/cargo \
        -e PG_IN_TOOLBOX=1 \
        "${tag}" "${command[@]}"
}

main "$@"
