#!/usr/bin/env bash
# Unity C# review gate.
#
# check  (default) : reads a Stop/SubagentStop hook payload on stdin and blocks
#                    completion while modified .cs files have not been reviewed.
# gate             : reads a PreToolUse hook payload on stdin and denies the call
#                    when it would spend a test run - the play harness, a soak, or
#                    a unity command that drives the editor (eval, menu) - while
#                    .cs changes are still unreviewed. A compile verdict and the
#                    console are NOT gated: knowing whether the code builds, and
#                    what the editor said, is what a review is read against.
# touch            : reads a PostToolUse Edit/Write payload and notes the .cs file
#                    as one THIS session wrote.
# record [id]      : marks this session's pending .cs changes as reviewed.
#
# YOUR OWN CHANGES ONLY. Two sessions share this repo and both write C# (the
# project's own note about it is in Docs/city-districts-plan.md). A gate keyed to
# every dirty .cs in the tree meant the other session's save re-closed yours
# between the record and the very next call, which is a gate nobody can ever get
# through. The fingerprint therefore covers only the files this session has
# touched, tracked by the `touch` hook; a session that has written no C# is not
# gated at all.
#
# The marker is still keyed by a fingerprint of the CONTENT, so your own next
# edit invalidates it and the gate closes again - which is the point of it.

set -uo pipefail

repo_root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
cd "$repo_root" || exit 0

state_dir=".claude/.unity-review"
mkdir -p "$state_dir" 2>/dev/null || exit 0
find "$state_dir" -type f -mtime +7 -delete 2>/dev/null

mode=${1:-check}
max_blocks=${2:-3}

# The payload is read once: check/gate/touch all need the session id out of it.
payload=""
case "$mode" in
  check|gate|touch) payload=$(cat 2>/dev/null) ;;
esac

session=$(printf '%s' "$payload" | jq -r '.session_id // ""' 2>/dev/null)
# `record` is typed by hand and carries no payload, so it takes the id as an
# argument; failing that it falls back to the session list written most recently,
# which is this one in every case but a dead heat.
[ -z "$session" ] && [ "$mode" = "record" ] && session=${2:-}
if [ -z "$session" ]; then
  session=$(ls -t "$state_dir"/*.mine 2>/dev/null | head -1)
  session=${session##*/}
  session=${session%.mine}
fi
mine_file="$state_dir/${session:-unknown}.mine"

if [ "$mode" = "touch" ]; then
  f=$(printf '%s' "$payload" | jq -r '.tool_input.file_path // ""' 2>/dev/null)
  case "$f" in
    *.cs) ;;
    *) exit 0 ;;
  esac
  # store repo-relative, so it matches what git reports
  f=${f#"$repo_root"/}
  [ -n "$session" ] && printf '%s\n' "$f" >> "$mine_file"
  exit 0
fi

# Every path this session wrote, whether or not git still calls it dirty.
mine() { sort -u "$mine_file" 2>/dev/null; }

changed_files() {
  {
    git diff --name-only -- '*.cs'
    git diff --cached --name-only -- '*.cs'
    git ls-files --others --exclude-standard -- '*.cs'
  } 2>/dev/null | sort -u | { [ -s "$mine_file" ] && comm -12 - <(mine) || cat; }
}

# Content of every pending .cs change of OURS: patch text for tracked files, whole
# file for untracked ones. Hashing the content (not just names) means a re-edit of
# the same file produces a new fingerprint and re-closes the gate.
changed_content() {
  local list
  list=$(changed_files)
  [ -z "$list" ] && return 0
  printf '%s\n' "$list" | while IFS= read -r f; do
    [ -n "$f" ] || continue
    printf '=== %s\n' "$f"
    if git ls-files --error-unmatch -- "$f" >/dev/null 2>&1; then
      git diff -- "$f" 2>/dev/null
      git diff --cached -- "$f" 2>/dev/null
    else
      cat -- "$f" 2>/dev/null
    fi
  done
}

files=$(changed_files)
content=$(changed_content)

if [ "$mode" = "record" ]; then
  if [ -z "$content" ]; then
    echo "no pending .cs changes of this session's; nothing to record"
    exit 0
  fi
  fp=$(printf '%s' "$content" | shasum | awk '{print $1}')
  : > "$state_dir/$fp.done"
  echo "recorded review for $fp ($(printf '%s\n' "$files" | grep -c . ) file(s) this session wrote)"
  exit 0
fi

if [ "$mode" = "gate" ]; then
  cmd=$(printf '%s' "$payload" | jq -r '.tool_input.command // ""' 2>/dev/null)

  # Only the calls that SPEND something: a harness run costs sim time and leaves a
  # trace to read, and eval/menu change the editor's state under you. Everything
  # that only asks a question is left alone.
  printf '%s' "$cmd" | grep -Eq \
    'gangsters_play|play/run\.sh|soak\.sh|unity +(command|cmd) +eval|unity +(command|cmd) +menu' \
    || exit 0

  [ -z "$content" ] && exit 0

  fp=$(printf '%s' "$content" | shasum | awk '{print $1}')
  [ -f "$state_dir/$fp.done" ] && exit 0

  # NO RELEASE VALVE HERE, unlike check mode. A denied tool call is not a deadlock -
  # the agent can review, or do something else - whereas a blocked Stop leaves a turn
  # with no way to end, which is why that one counts attempts and gives up. Counting
  # here would be worse than useless: this matcher fires on the command TEXT, so a
  # grep or a cat that merely mentions the harness would spend the budget and then
  # wave the real run through unreviewed.

  jq -n --arg files "$files" --arg session "$session" '
  {
    hookSpecificOutput: {
      hookEventName: "PreToolUse",
      permissionDecision: "deny",
      permissionDecisionReason: (
        "Unity review gate: review the C# before you spend a test run. These files have uncommitted changes that have not been reviewed yet:\n"
        + $files
        + "\n\nInvoke the code-review-unity skill on them (pass the paths above, since untracked files do not appear in a plain git diff), act on or report its findings, then record the review by running:\n"
        + "  .claude/hooks/unity-review-gate.sh record " + $session + "\n"
        + "After that the harness run is allowed. A compile check (unity command recompile) and the console are not gated - run those first if you need them."
      )
    }
  }'
  exit 0
fi

# check mode -----------------------------------------------------------------
# (the payload was drained at the top - check needs the session id out of it)

[ -z "$content" ] && exit 0

fp=$(printf '%s' "$content" | shasum | awk '{print $1}')
[ -f "$state_dir/$fp.done" ] && exit 0

tries_file="$state_dir/$fp.tries"
tries=$(cat "$tries_file" 2>/dev/null || echo 0)
case "$tries" in ''|*[!0-9]*) tries=0 ;; esac

if [ "$tries" -ge "$max_blocks" ]; then
  jq -n --arg n "$tries" '{systemMessage:("Unity review gate: released after " + $n + " blocked attempts; the .cs changes were never reviewed.")}'
  exit 0
fi

echo $((tries + 1)) > "$tries_file"

jq -n --arg files "$files" --arg session "$session" '
{
  decision: "block",
  reason: (
    "Unity review gate: these C# files have uncommitted changes that have not been reviewed yet:\n"
    + $files
    + "\n\nInvoke the code-review-unity skill on them (pass the paths above, since untracked files do not appear in a plain git diff), act on or report its findings, then record the review by running:\n"
    + "  .claude/hooks/unity-review-gate.sh record " + $session + "\n"
    + "Only after that command succeeds may you finish this turn."
  )
}'
