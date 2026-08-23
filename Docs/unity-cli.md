# Driving the open editor from the terminal

There are two ways to make Unity do something without a person clicking. The old one starts a
second Unity in `-batchmode`: it needs `Temp/UnityLockfile`, so the editor has to be **closed**,
and every run pays a cold start. The new one talks to the editor that is **already open** over a
local port — no lock, no start-up, no scene stolen from another session.

    unity status                     # which editors are up, on which port, in which state
    unity command                    # every command the open editor answers
    unity command <name> --json      # ask it one

`unity` is Unity's own CLI (`~/.unity/bin/unity`). The port exists because
`com.unity.pipeline` is in `Packages/manifest.json`; the package carries the server and a large
set of built-in commands, and `Assets/Scripts/Editor/PipelineCommands.cs` adds this project's own.

The whole thing needs the editor **open**. With no editor up, `unity command` fails with
"No Unity Editor instances found" — that is when the batch harness is still the right tool.

## What it is good for here

**A compile verdict in seconds, with no harness of our own.**

    unity command recompile --json
    unity command recompile_status --json     # {"status":"completed","failed":false,"errors":[]}

This is the editor's own compile — asmdef boundaries, the Editor assembly, Unity 6.5's
obsolete-as-error rules, all of it. The offline Roslyn build in
[the memory notes] is still faster for a quick syntax check, but it is a hand-built
reference set that has been silently wrong before; this cannot be.

`unity command console --json` reads the console the same way, so a run's errors come back
without grepping `Logs/Editor.log` — which, with two editors on one project, is not ground truth
anyway.

**Every `Tools/…` menu, from the terminal.** The project has ~54 `[MenuItem]`s that until now
only a mouse could reach:

    unity command menu --path "Tools/City/Dump City Layout"
    unity command menu                       # lists every menu item

Some are heavy, some open a modal, some trigger a domain reload that tears down the server
before it can answer. That is the menu item's nature, not a bug in the call.

**Reading the project.** `find_assets`, `find_gameobjects`, `get_scene_hierarchy`,
`get_component_properties`, `get_serialized_fields`, `search`, `screenshot`,
`capture_game_view`, `get_performance_stats`, `run_tests`. And `eval` runs arbitrary C# in the
editor through Roslyn, which answers most one-off questions without a file being written at all.

## This project's commands

| command | what it answers |
|---|---|
| `gangsters_layout` | the district roll for a seed — the paper plan, no build, no Play |
| `gangsters_measure` | what a prefab actually measures, from the imported asset |
| `gangsters_play` | a harness run **inside the open editor** |

    unity command gangsters_layout --scene Assets/Scenes/Game.unity --seed 7 --json
    unity command gangsters_layout --seed 1 --count 20 --json     # sweep seeds
    unity command gangsters_measure --name building-bank --json
    unity command gangsters_play --scene Assets/Scenes/BlockDemo.unity --seconds 90 \
        --step 0.05 --out Temp/play/cli --sets "BlockDemoBuilder.rivalCrews=2"
    python Tools/play/analyze.py Temp/play/cli --verdict

`gangsters_layout` reads the road axes off the `RoadDemoBuilder` in the open scene, so it needs
a scene that has one (every demo scene does). Passing `--scene` **opens that scene in the
editor** — the person at the keyboard will see their scene change.

`gangsters_measure` measures an instance, not the asset: a prefab asset's renderers report
bounds in their own local space, and the scaling the Synty packs rely on only applies once the
thing stands in a scene. The instance is created and destroyed inside the call.

`gangsters_play` is the batch harness with `Cfg.quit` cleared. Everything else is the same code
(`PlayHarness.RunWith`), so the trace, the summary and the pictures are identical and
`analyze.py` reads them the same way. It returns as soon as the run has started; the run is over
when `summary.json` appears. A verdict is only comparable to a soak's at the same `--step`
(**0.05**).

## When to use which

| | `Tools/play/run.sh` | `unity command gangsters_play` |
|---|---|---|
| editor | must be **closed** | must be **open** |
| lockfile | takes it — races another run | never touches it |
| cost | a cold Unity per run | none, the editor is already up |
| good for | soaks, many seeds, CI, a machine to itself | one run while you are working, a quick check |

A soak still belongs to `Tools/play/soak.sh`. Anything you would otherwise have closed the
editor for belongs here.

## Traps

- **A domain reload kills the connection.** Anything that recompiles (`package_add`, editing a
  script, some menu items) drops the server for a few seconds. Poll `unity status` until the
  state is `ready` again.
- **The port belongs to one editor.** With two editors open on this project, `unity status`
  lists both — check the PID before sending anything that changes state.
- **`result` is sometimes a JSON string, not an object.** `recompile_status` returns
  `"result": "{\"status\":\"completed\",…}"`. Parse twice, or you will read a status of `None`
  and conclude the command is broken.
- **The editor must have imported the package.** Unity only notices a manifest change when it
  gets focus (or on `package_resolve`), so right after `unity pipeline install` the server is not
  up yet and `unity status` prints an empty table.
- **A picture taken too early is a flat blue rectangle.** Both `capture_game_view` and the
  harness's own `--shot` render whatever the demo camera sees at that instant, and for the first
  few seconds of a run that is nothing: the city is still being built and the camera has not been
  put anywhere. Ask for a picture after the warm-up, not at `t=0`.
- **`capture_game_view --save_path` is confined to the authoring root**, so a bare `Temp/...`
  lands in `Assets/Temp/...` and gets imported into the project. Use the `screenshot` command
  instead — it writes to `Temp/pipeline-screenshots/` outside `Assets`.
