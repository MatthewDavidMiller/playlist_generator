#!/usr/bin/env bash
set -euo pipefail

if [[ -n ${CONTAINER_ENGINE:-} ]]; then
    engine=${CONTAINER_ENGINE}
elif command -v podman >/dev/null 2>&1; then
    engine=podman
elif command -v docker >/dev/null 2>&1; then
    engine=docker
else
    if command -v dnf >/dev/null 2>&1; then
        sudo dnf install -y podman
    elif command -v apt-get >/dev/null 2>&1; then
        sudo apt-get update
        sudo apt-get install -y podman
    else
        echo "Install rootless Podman or Docker, or set CONTAINER_ENGINE." >&2
        exit 1
    fi
    engine=podman
fi

"${engine}" build --pull=always --tag playlist-generator-dev:0.9.4 --file Containerfile .
git config core.hooksPath .githooks
./scripts/container.sh bash scripts/doctor.sh
