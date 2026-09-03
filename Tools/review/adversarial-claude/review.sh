#!/usr/bin/env bash
# Adversarial review of the local change, judged by Claude instead of Codex.
# Reuses the Codex plugin's own adversarial-review prompt so both reviews ask the
# same questions and only the model answering them differs.
set -euo pipefail

MODEL="opus"
BASE=""
FOCUS=""
MAX_BYTES=400000

while [ $# -gt 0 ]; do
  case "$1" in
    --base) BASE="${2:?--base needs a ref}"; shift 2 ;;
    --model) MODEL="${2:?--model needs a name}"; shift 2 ;;
    --max-bytes) MAX_BYTES="${2:?--max-bytes needs a number}"; shift 2 ;;
    -h|--help)
      echo "usage: review.sh [--base <ref>] [--model <name>] [--max-bytes N] [focus text ...]"
      exit 0 ;;
    *) FOCUS="${FOCUS:+$FOCUS }$1"; shift ;;
  esac
done

command -v claude >/dev/null || { echo "claude CLI not on PATH" >&2; exit 1; }
command -v python3 >/dev/null || { echo "python3 not on PATH" >&2; exit 1; }
git rev-parse --show-toplevel >/dev/null 2>&1 || { echo "not a git repository" >&2; exit 1; }
cd "$(git rev-parse --show-toplevel)"

# The prompt lives in the Codex Claude Code plugin, whose cache path carries a
# version. Take the newest one rather than pinning a version that will age out.
find_template() {
  local p
  if [ -n "${CLAUDE_PLUGIN_ROOT:-}" ] && [ -f "$CLAUDE_PLUGIN_ROOT/prompts/adversarial-review.md" ]; then
    echo "$CLAUDE_PLUGIN_ROOT/prompts/adversarial-review.md"; return 0
  fi
  p="$(ls -1d "$HOME"/.claude/plugins/cache/openai-codex/codex/*/prompts/adversarial-review.md 2>/dev/null | sort -V | tail -1)"
  if [ -n "$p" ]; then echo "$p"; return 0; fi
  p="$HOME/.claude/plugins/marketplaces/openai-codex/plugins/codex/prompts/adversarial-review.md"
  if [ -f "$p" ]; then echo "$p"; return 0; fi
  return 1
}

TEMPLATE="$(find_template)" || {
  echo "codex adversarial prompt not found. Install the Codex plugin for Claude Code:" >&2
  echo "  /plugin marketplace add openai/codex   then   /plugin install codex" >&2
  exit 1
}

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
DIFF="$WORK/diff.txt"

if [ -n "$BASE" ]; then
  TARGET="branch review against $BASE"
  git diff "$BASE...HEAD" > "$DIFF"
else
  TARGET="working tree (staged + unstaged + untracked)"
  { git diff --cached; git diff; } > "$DIFF"
  # Untracked files are part of the change even though no diff carries them.
  while IFS= read -r f; do
    [ -f "$f" ] || continue
    printf '\n--- new file: %s ---\n' "$f" >> "$DIFF"
    cat "$f" >> "$DIFF"
  done < <(git ls-files --others --exclude-standard)
fi

if [ ! -s "$DIFF" ]; then
  echo "nothing to review in $TARGET"
  exit 0
fi

SIZE=$(wc -c < "$DIFF" | tr -d ' ')
if [ "$SIZE" -gt "$MAX_BYTES" ]; then
  head -c "$MAX_BYTES" "$DIFF" > "$DIFF.cut"
  printf '\n\n[TRUNCATED at %s of %s bytes. Read the files directly for the rest.]\n' "$MAX_BYTES" "$SIZE" >> "$DIFF.cut"
  mv "$DIFF.cut" "$DIFF"
fi

PROMPT="$WORK/prompt.md"
python3 - "$TEMPLATE" "$DIFF" "$TARGET" "${FOCUS:-none given}" > "$PROMPT" <<'PY'
import sys, pathlib
template, diff, target, focus = sys.argv[1:5]
body = pathlib.Path(template).read_text()
body = body.replace("You are Codex performing an adversarial software review.",
                    "You are performing an adversarial software review.")
body = body.replace("{{TARGET_LABEL}}", target)
body = body.replace("{{USER_FOCUS}}", focus)
body = body.replace("{{REVIEW_COLLECTION_GUIDANCE}}",
    "You may read any file in the repository to confirm or kill a suspicion. "
    "The diff below is the change; the surrounding code is fair game and often decides the verdict.")
body = body.replace("""<structured_output_contract>
Return only valid JSON matching the provided schema.
Keep the output compact and specific.""", """<structured_output_contract>
Return markdown, not JSON.
Open with one line: NEEDS ATTENTION or APPROVE.
Then one section per finding.
Keep the output compact and specific.""")
body = body.replace("Use `needs-attention` if", "Use NEEDS ATTENTION if")
body = body.replace("Use `approve` only if", "Use APPROVE only if")
body = body.replace("- `line_start` and `line_end`", "- the line range")
body = body.replace("{{REVIEW_INPUT}}", pathlib.Path(diff).read_text())
print(body)
PY

exec claude -p \
  --model "$MODEL" \
  --allowedTools "Read,Grep,Glob,Bash(git diff:*),Bash(git log:*),Bash(git show:*)" \
  --append-system-prompt "Review only. Never edit a file, never stage or commit, never propose that you are about to change anything." \
  "$(cat "$PROMPT")"
