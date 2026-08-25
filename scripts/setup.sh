#!/usr/bin/env bash
# Prepare a workstation: build the toolbox image, map the local user into it,
# and install the tracked hooks. Docker with Compose v2 is the only
# prerequisite; everything the gates call lives inside the image.
#
# Windows hosts run `docker compose build` and `git config core.hooksPath
# .githooks` directly -- see docs/testing.md. This script is the Unix path,
# and the only thing it adds is the uid mapping, which Windows does not need.

set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
cd "${repo_root}"

if ! docker compose version >/dev/null 2>&1; then
    echo "Docker with Compose v2 is required. Install it, then re-run." >&2
    exit 1
fi

# compose.yaml reads these. Without them it falls back to root, which is right
# on Windows and wrong here: a gate would leave root-owned build output in the
# working tree.
umask 022
cat > .env <<ENV
# Written by scripts/setup.sh so gates run as you and leave no root-owned
# files in the tree. Not tracked: the values are specific to this machine.
PG_UID=$(id -u)
PG_GID=$(id -g)
ENV

docker compose build
git config core.hooksPath .githooks
docker compose run --rm doctor
