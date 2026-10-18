#!/usr/bin/env sh
# Full benchmark run on Linux/macOS: a thin wrapper over run-all.ps1 (PowerShell 7 required).
#   ./scripts/run-all.sh            full run
#   ./scripts/run-all.sh -Quick     smoke test
set -e
exec pwsh -NoProfile -File "$(dirname "$0")/run-all.ps1" "$@"
