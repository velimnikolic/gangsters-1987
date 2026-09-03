---
name: adversarial-claude
description: Run an adversarial code review of the current change with Claude instead of Codex, using the same adversarial prompt the Codex plugin uses. Use when a second opinion from a different model is wanted, when a Codex review came back clean and that feels too easy, or when the user asks for a Claude review, a cross-model review, or a challenge pass on the working tree or branch.
---

# Adversarial review, judged by Claude

`review.sh` beside this file runs the Codex plugin's own adversarial-review prompt
through the Claude CLI in headless mode. The questions are identical to
`/codex:adversarial-review`; only the model answering them changes. That is the whole
point — a second model reading the same diff catches what the first one talked itself
out of.

The skill lives in the repository at `Tools/review/adversarial-claude/` and is linked
into `~/.codex/skills/` by `Tools/review/install-skill.sh`, so every machine that has
the checkout gets the same reviewer.

## Running it

From anywhere inside the repository being reviewed:

```bash
~/.codex/skills/adversarial-claude/review.sh
```

Working tree is the default scope: staged, unstaged, and untracked files together.
For a branch instead:

```bash
~/.codex/skills/adversarial-claude/review.sh --base main
```

Any remaining words are passed as the review's focus area, weighted heavily but not
exclusively:

```bash
~/.codex/skills/adversarial-claude/review.sh --base main the collector's bag handoff
```

Other flags: `--model <name>` (default `opus`; `sonnet` is faster and cheaper) and
`--max-bytes N` (default 400000, where the diff is truncated).

## Rules

- This is review-only. Do not fix anything the review names, do not stage, do not
  commit. Report the findings and stop.
- Return the script's output as it came back. Do not summarise it away or soften a
  finding you disagree with; say you disagree and leave the finding intact.
- The script exits 0 with `nothing to review` when the scope is empty. That is not a
  failure — say the scope was empty and stop, rather than widening it on your own.
- The reviewer may read files beyond the diff and may run read-only git commands. It
  cannot write.

## Requirements

- The `claude` CLI on PATH, logged in.
- The Codex plugin for Claude Code installed, since the prompt template is read from
  its cache. The script prints the install command if the template is missing.
