#!/usr/bin/env bash
# Offline evidence gate. Does not query an editor or run a scenario.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "$HERE/gate.py" "$@"
