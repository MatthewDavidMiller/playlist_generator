#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

git config core.hooksPath .githooks
chmod +x \
  .githooks/pre-commit \
  scripts/build-release.sh \
  scripts/test-pre-commit-hook.sh \
  scripts/validate.sh

echo "Installed repository hooks from .githooks."
