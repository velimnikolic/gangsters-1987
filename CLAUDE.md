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
lockfile). They live in `Assets/Scripts/Editor/PipelineCommands.cs`.

**Read `Docs/unity-cli.md` before reaching for a batch run or a hand-built offline compiler.**
The old ways still have their place — `Tools/play/run.sh` and `soak.sh` for unattended soaks
(they need the editor **closed**), and the offline Roslyn build for a fast syntax check with no
editor at all — but neither is the first thing to try any more.

## The rest

| what | where |
|---|---|
| the play harness, the trace, the reader | `Docs/play-harness.md` |
| the city's districts | `Docs/city-districts-plan.md` |
| the period | `Docs/1987-period-reference.md` |
