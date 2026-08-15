#!/usr/bin/env bash
set -euo pipefail

cargo deny check
cargo vet --locked
trivy fs --scanners vuln,secret,misconfig --exit-code 1 --severity HIGH,CRITICAL \
  --skip-dirs .cache --skip-dirs artifacts --skip-dirs target .
./scripts/notices.sh --check
