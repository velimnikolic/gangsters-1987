# Working on this project

## The editor answers the terminal

This project has `com.unity.pipeline` installed, so a **running** Unity editor holds a local
server that Unity's CLI talks to. Prefer it over anything that needs the editor closed:

    unity status                                          # port, state, PID of every open editor
    unity command                                         # every command it answers
    unity command recompile && unity command recompile_status --json   # a real compile verdict
    unity command console --json                          # the editor console
    unity command menu --path "Tools/City/Dump City Layout"            # any of the ~54 Tools menus
    unity command eval --code '...'                       # arbitrary C# in the live editor, play mode included

This project's own commands: `gangsters_layout` (district roll for a seed), `gangsters_measure`
(what a prefab really measures), `gangsters_play` (a harness run inside the open editor, no
lockfile), `gangsters_core` and `gangsters_industry` (a quarter dealt from a seed and judged;
`--draw` stands it up). They live in `Assets/Scripts/Editor/PipelineCommands.cs`.

**Read `Docs/unity-cli.md` before reaching for a batch run or a hand-built offline compiler.**

The old ways still have their place — `Tools/play/run.sh` and `soak.sh` for unattended soaks
(they need the editor **closed**), and the offline Roslyn build for a fast syntax check with no
editor at all — but neither is the first thing to try any more.

## Review the C# when the user says commit, and not before

The `code-review-unity` skill runs **once, at commit time**: when the user says "commit", the
pending `.cs` is reviewed first, the real findings are fixed or reported tersely, and then the
commit is made. Nothing gates a harness run, a soak or an editor verb during development, and
there is no hook - the old review gate (`.claude/hooks/unity-review-gate.sh`) was removed on
2026-08-26 at the user's word. Keep replies short: what was done, what is left.

## The rest

| what | where |
|---|---|
| the play harness, the trace, the reader | `Docs/play-harness.md` |
| the tactical map (320x200 raster + turf HUD) | `Docs/tactical-map.md` |
| the city's districts | `Docs/city-districts-plan.md` |
| what the port is made of | `Docs/harbor-detail.md` |
| the period | `Docs/1987-period-reference.md` |
| what the game owes a credit for | `Docs/credits.md` |
