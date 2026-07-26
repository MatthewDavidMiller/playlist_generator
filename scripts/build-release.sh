#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${PLAYLIST_GENERATOR_DOTNET:-dotnet}"
output_root="$repository_root/artifacts"
dry_run=false
runtimes=()

usage() {
  cat <<'EOF'
Usage: ./scripts/build-release.sh [options]

Options:
  --runtime <rid>  Publish one supported runtime. Repeat to publish several.
  --output <path>  Output root (default: ./artifacts).
  --dry-run        Print publish commands without executing them.
  --help           Show this help.

Supported runtime identifiers:
  win-x64, win-arm64, linux-x64, linux-arm64

Without --runtime, win-x64 and linux-x64 are published.
EOF
}

is_supported_runtime() {
  case "$1" in
    win-x64|win-arm64|linux-x64|linux-arm64) return 0 ;;
    *) return 1 ;;
  esac
}

while (($# > 0)); do
  case "$1" in
    --runtime)
      if (($# < 2)); then
        echo "error: --runtime requires a value." >&2
        exit 2
      fi
      if ! is_supported_runtime "$2"; then
        echo "error: unsupported runtime '$2'." >&2
        exit 2
      fi
      runtimes+=("$2")
      shift 2
      ;;
    --output)
      if (($# < 2)); then
        echo "error: --output requires a value." >&2
        exit 2
      fi
      output_root="$2"
      shift 2
      ;;
    --dry-run)
      dry_run=true
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "error: unknown option '$1'." >&2
      usage >&2
      exit 2
      ;;
  esac
done

if ((${#runtimes[@]} == 0)); then
  runtimes=(win-x64 linux-x64)
fi

if [[ "$output_root" != /* ]]; then
  output_root="$repository_root/$output_root"
fi

if [[ "$dry_run" == false ]] && ! command -v "$dotnet_command" >/dev/null 2>&1; then
  echo "error: .NET SDK 10 is required; '$dotnet_command' was not found." >&2
  exit 1
fi

run_publish() {
  local project="$1"
  local runtime="$2"
  local destination="$3"
  local command=(
    "$dotnet_command" publish "$project"
    --configuration Release
    --runtime "$runtime"
    --self-contained true
    --output "$destination"
    --disable-build-servers
    --maxcpucount:1
    -p:DebugType=None
    -p:DebugSymbols=false
    -p:RestoreLockedMode=true
  )

  if [[ "$dry_run" == true ]]; then
    printf '%q ' "${command[@]}"
    printf '\n'
  else
    "${command[@]}"
  fi
}

cd "$repository_root"
for runtime in "${runtimes[@]}"; do
  run_publish \
    "src/PlaylistGenerator.Desktop/PlaylistGenerator.Desktop.csproj" \
    "$runtime" \
    "$output_root/$runtime/desktop"
  run_publish \
    "src/PlaylistGenerator.Cli/PlaylistGenerator.Cli.csproj" \
    "$runtime" \
    "$output_root/$runtime/cli"
done
