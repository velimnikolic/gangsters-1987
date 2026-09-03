#!/usr/bin/env bash
# Link this repository's agent skills into the places Codex and Claude Code scan.
# Both scan a fixed home directory, so a checked-in skill reaches a new machine by
# symlink rather than by copy — edits in the repo take effect with no re-install.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SRC="$REPO/Tools/review/adversarial-claude"
[ -d "$SRC" ] || { echo "missing $SRC" >&2; exit 1; }

link_into() {
  local root="$1" dest="$1/adversarial-claude"
  mkdir -p "$root"
  if [ -L "$dest" ]; then
    local current
    current="$(readlink "$dest")"
    if [ "$current" = "$SRC" ]; then echo "already linked: $dest"; return 0; fi
    rm "$dest"
  elif [ -e "$dest" ]; then
    echo "refusing to replace real directory $dest — move it aside first" >&2
    return 1
  fi
  ln -s "$SRC" "$dest"
  echo "linked: $dest -> $SRC"
}

link_into "$HOME/.codex/skills"
link_into "$HOME/.claude/skills"

command -v claude >/dev/null || echo "warning: claude CLI not on PATH; the review will not run" >&2
