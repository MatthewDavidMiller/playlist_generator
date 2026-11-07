#!/usr/bin/env bash
# Prepare a workstation: build the toolbox image, install the tracked hooks,
# and check every required tool. Rootless Podman is the only prerequisite;
# everything the gates call lives inside the image. Windows hosts run this from
# WSL.

set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
cd "${repo_root}"

if ! command -v podman >/dev/null 2>&1; then
    echo "Rootless Podman is required. Install it, then re-run." >&2
    exit 1
fi

./scripts/toolbox.sh --build
git config core.hooksPath .githooks
./scripts/toolbox.sh doctor
