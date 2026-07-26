#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
test_root="$(mktemp -d)"
trap 'rm -rf -- "$test_root"' EXIT

mkdir -p "$test_root/bin" "$test_root/repository/scripts"
pipeline_log="$test_root/pipeline.log"

cat >"$test_root/bin/git" <<EOF
#!/usr/bin/env bash
if [[ "\$*" == "rev-parse --show-toplevel" ]]; then
  printf '%s\n' "$test_root/repository"
  exit 0
fi

exit 2
EOF

cat >"$test_root/repository/scripts/validate.sh" <<'EOF'
#!/usr/bin/env bash
printf 'validate\n' >>"$PIPELINE_LOG"
EOF

cat >"$test_root/repository/scripts/build-release.sh" <<'EOF'
#!/usr/bin/env bash
printf 'build-release %s\n' "$*" >>"$PIPELINE_LOG"
EOF

chmod +x \
  "$test_root/bin/git" \
  "$test_root/repository/scripts/validate.sh" \
  "$test_root/repository/scripts/build-release.sh"

PATH="$test_root/bin:$PATH" \
  PIPELINE_LOG="$pipeline_log" \
  "$repository_root/.githooks/pre-commit"

expected_output=$'validate\nbuild-release --runtime win-x64'
actual_output="$(<"$pipeline_log")"

if [[ "$actual_output" != "$expected_output" ]]; then
  echo "error: pre-commit pipeline did not validate and build only win-x64." >&2
  diff -u <(printf '%s\n' "$expected_output") <(printf '%s\n' "$actual_output") >&2 || true
  exit 1
fi

cat >"$test_root/repository/scripts/validate.sh" <<'EOF'
#!/usr/bin/env bash
printf 'validate-failed\n' >>"$PIPELINE_LOG"
exit 23
EOF
chmod +x "$test_root/repository/scripts/validate.sh"
: >"$pipeline_log"

set +e
PATH="$test_root/bin:$PATH" \
  PIPELINE_LOG="$pipeline_log" \
  "$repository_root/.githooks/pre-commit"
hook_status=$?
set -e

if [[ "$hook_status" -ne 23 ]]; then
  echo "error: pre-commit hook did not preserve the validation failure status." >&2
  exit 1
fi

if [[ "$(<"$pipeline_log")" != "validate-failed" ]]; then
  echo "error: pre-commit hook published after validation failed." >&2
  exit 1
fi
