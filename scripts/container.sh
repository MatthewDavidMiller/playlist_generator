#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
image=${PLAYLIST_GENERATOR_DEV_IMAGE:-playlist-generator-dev:0.9.4}
mkdir -p "${repo_root}/.cache/cargo" "${repo_root}/.cache/target"

if [[ -n ${CONTAINER_ENGINE:-} ]]; then
    engine=${CONTAINER_ENGINE}
elif command -v podman >/dev/null 2>&1; then
    engine=podman
elif command -v docker >/dev/null 2>&1; then
    engine=docker
else
    echo "No container engine found. Run 'make install'." >&2
    exit 1
fi

tty=()
if [[ -t 0 && -t 1 ]]; then tty=(-it); fi
mount_suffix=
engine_options=()
if [[ ${engine} == podman ]]; then
    mount_suffix=:Z
    engine_options=(--userns=keep-id)
fi

exec "${engine}" run --rm "${tty[@]}" "${engine_options[@]}" \
    --user "$(id -u):$(id -g)" \
    --cap-drop=ALL \
    --security-opt=no-new-privileges \
    --pids-limit=1024 \
    --memory=8g \
    --volume "${repo_root}:/workspace${mount_suffix}" \
    --workdir /workspace \
    --env HOME=/tmp/playlist-generator-home \
    --env CARGO_HOME=/workspace/.cache/cargo \
    --env CARGO_TARGET_DIR=/workspace/.cache/target \
    --env RUSTUP_HOME=/usr/local/rustup \
    "${image}" "$@"
