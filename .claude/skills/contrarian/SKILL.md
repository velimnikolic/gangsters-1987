---
name: contrarian
description: Stress-test a plan before it is built. Spawns the contrarian agent to steel-man the plan, audit its unstated assumptions, run a pre-mortem against this repository's known failure shapes, and return findings with a verdict. Use when a plan was produced with another agent or model, when a plan feels too agreeable, before starting an epic, or when the user asks to challenge, counter, poke holes in or second-opinion a plan. Not a code reviewer.
argument-hint: [plan file | Linear ID | PR/branch | nothing = the plan in this conversation]
allowed-tools: Agent, Read, Grep, Glob, Bash(git *), mcp__linear-server__*
---

# Contrarian

Second opinion on a plan, from an agent whose assigned duty is to disagree well.

## What this does

1. Assembles the plan into one self-contained brief.
2. Spawns the `contrarian` subagent with that brief.
3. Relays its findings, verdict and cheapest test back to the user.

The subagent has no view of this conversation. Whatever the plan is, it goes into the prompt in
full. A brief that says "review the plan above" produces a worthless review.

## Step 1 - find the plan

Resolve `$ARGUMENTS`:

- **empty** - the plan is in this conversation: what was just designed, what an ExitPlanMode plan
  proposed, what another agent or model handed back. Restate it in full from the transcript.
- **a file path** - read it whole (`Docs/design-briefs/*.md`, a plan in the scratchpad, a doc).
- **a Linear identifier** (`GAN-263`, an epic) - fetch the issue and its sub-issues with the
  Linear tools, and any document it links.
- **a branch, PR or `HEAD`** - the plan is what the diff intends; get `git log` and
  `git diff --stat` plus the diff of the interesting files.
- **prose** - the user typed the plan on the command line; that is the plan.

If nothing resolves, ask which plan - one question, then stop. Do not invent a plan to review.

## Step 2 - build the brief

Write a single prompt containing:

- **The plan, verbatim and complete.** Every step, every number, every file it names. No summary,
  no "as discussed".
- **Where it came from** - the user's own plan, an agent's, another model's, a Linear epic. Say
  which; do not name the agent as a target for criticism.
- **The stakes** - reversible tweak, significant feature, or epic/architecture/determinism/save
  data. This sets the agent's calibration.
- **Anything already settled** - decisions the user has made and does not want relitigated
  (era is 1987; territory unit is the building; no polyperfect; UI dresses through UiSkin; every
  commit lands on main). Say so explicitly, so the agent spends its effort on what is still open.
- **The open questions** the user actually wants pressure on, if they named any.
- **The instruction to cite `path:line` from this repository** for every claim it can.

## Step 3 - spawn

Call the Agent tool once with `subagent_type: "contrarian"` and that brief. One agent. Do not fan
out, do not run it twice for a second opinion on the second opinion.

Do not do the analysis yourself in the main thread while waiting.

## Step 4 - relay

The subagent's report is not shown to the user, so relay it. Keep the shape: strengths, findings
ordered Critical first, verdict, cheapest test. Trim its prose, never its substance - a dropped
failure scenario is the whole value gone.

Then, in one or two lines: which findings you think are right, which you think are the agent being
contrarian for its own sake, and what you would do next. The user asked for a counterweight, not
a new authority - say where you still disagree with it.

## Rules

- **Read-only.** This skill changes nothing. No edits, no commits, no Linear moves. If a finding
  should become a ticket or an edit, ask first.
- **No harness run.** Never start `gangsters_play` from here; it takes over the editor the user
  may be working in.
- **The verdict is advice.** "Needs rework" does not cancel the user's plan. Report it and let
  them decide.
