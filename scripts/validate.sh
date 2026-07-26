#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${PLAYLIST_GENERATOR_DOTNET:-dotnet}"

if ! command -v "$dotnet_command" >/dev/null 2>&1; then
  echo "error: .NET SDK 10 is required; '$dotnet_command' was not found." >&2
  exit 1
fi

cd "$repository_root"

"$dotnet_command" restore PlaylistGenerator.slnx --locked-mode
"$dotnet_command" format PlaylistGenerator.slnx --no-restore --verify-no-changes
"$dotnet_command" build PlaylistGenerator.slnx \
  --configuration Release \
  --no-restore \
  --disable-build-servers \
  --maxcpucount:1
"$dotnet_command" test PlaylistGenerator.slnx \
  --configuration Release \
  --no-build \
  --no-restore \
  --disable-build-servers \
  --maxcpucount:1
