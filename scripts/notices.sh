#!/usr/bin/env bash
set -euo pipefail

output=THIRD_PARTY_NOTICES.txt
if [[ ${1:-} == --check ]]; then
    generated=$(mktemp)
    trap 'rm -f "${generated}"' EXIT
    cargo about generate about.hbs --config about.toml --output-file "${generated}" --workspace --locked --offline --fail
    cmp "${generated}" "${output}"
else
    cargo about generate about.hbs --config about.toml --output-file "${output}" --workspace --locked --offline --fail
fi
