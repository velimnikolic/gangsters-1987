---
description: One sentence of work, researched, planned, built, tested, reviewed and repaired until the harness passes
argument-hint: <what should happen in the city>
---

# The whole job from one sentence

`$ARGUMENTS` is the goal. It is prose, not a specification — the first phase turns it into
something the harness can judge, and nothing is built before that has happened.

Work the six phases in order. Do not skip a phase because the change looks small; a change
that looks small is exactly the one that turns up as a `fault` one run in thirty.

## 1. Research — before a single edit

Answer these four questions, in writing, before touching code:

- **Where does this behaviour already live?** Read the CLAUDE.md table, then the plan in
  `Docs/` for the area (city districts, police behaviour, vehicle movement, bike riders,
  the play harness). Grep for the nouns in the goal. The answer is a shared class under
  `Assets/RoadDemo/` or `Assets/Scripts/`, never a demo scene — scenes are test rigs, they
  configure, they do not carry behaviour.
- **Which class is the choke point?** One place where the change makes every caller correct.
  If the answer is "three places", the research is not finished.
- **Which rig exercises it?** `BlockDemo.unity` for the crew and the mission, `RoadDemo`/
  `Game.unity` for the city, `BikeDemo`/`MotoDemo`/`CoverDemo` for their own subjects. The rig
  decides the soak mode: `--walk`, `--brawl`, `--moto`, `--roadblock`, or the default car run.
- **How will it be measured?** This is the phase's real product. Name the trace row and the
  field that will show the new behaviour happening, and the `fault` kind that will fire when
  it goes wrong. If neither exists yet, **adding them to `Assets/RoadDemo/DriveTrace.cs` and
  the reader in `Tools/play/analyze.py` is the first commit of the build phase**, not an
  afterthought — an unobservable change cannot be verified, and the loop below will spin
  forever on it.

Measure, do not guess: `unity command eval --code '...'` answers most questions about the live
editor without a file being written, and `unity command gangsters_layout --seed N` answers what
a seed gives before anything is built.

## 2. Acceptance criteria

Write `Docs/loop/GOAL.md`: the goal sentence, the choke point found in phase 1, and a short
list of conditions that a script can check — a fault count that must be zero, a trace row that
must appear, a soak tally that must be clean. Prose conditions ("looks right", "feels better")
belong in the goal line, never in the list.

## 3. Build

One change at a time, at the choke point. Match the surrounding code — its comment density, its
naming, its idiom. Never scale an asset below its authored size. Gear is issued to lieutenants
only. Behaviour goes in the shared class; the scene only sets fields.

## 4. Verify — the fast circle, editor open

    unity command recompile && unity command recompile_status --json
    unity command gangsters_play --scene <rig> --seconds 120 --out Temp/play/loop
    python Tools/play/analyze.py Temp/play/loop --verdict
    python Tools/play/analyze.py Temp/play/loop --why        # when it fails

Seconds, not minutes, and it takes no lockfile. Most iterations never leave this circle.
`unity command console --json` reads the editor's own console when the compile verdict is not
enough.

## 5. Gate — thirty in a row, editor closed

One good run proves nothing. When the fast circle is clean:

    Tools/play/soak.sh --runs 30 [--walk|--brawl|--moto|--roadblock]

**This needs the editor closed — one Unity to a project.** Ask before closing it; that is the
one step in this pipeline that is not yours to take alone. `soak.sh` exits non-zero if any run
failed and writes the tally to `soak.txt` beside the runs.

## 6. Repair

Every soak failure gets fixed. Not deferred, not explained away, not marked as a known issue.
`analyze.py <run> --why` names the frame and the reason the driving code gave for itself;
`--crew` does the same for men on foot. Fix the cause at the choke point, then go back to
phase 4. Append each attempt and its verdict to `Docs/loop/LEDGER.md` — one line, what was
tried and what the harness said — so the next iteration does not retry a dead end.

Stop when `Tools/play/gate.sh` returns 0, and report the tally.

## Standing rules

- **No git.** Edit, verify, report. The user commits.
- One change per verify circle, or the verdict cannot say which change caused it.
- If part of the goal turns out to be blocked, finish everything else and say plainly what was
  left out and why.
