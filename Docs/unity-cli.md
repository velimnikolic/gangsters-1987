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

## Opening Gangsters without the CoreDemo graphics stall

Open this project through `Tools/unity/open-gangsters.command` (it is also safe to double-click
from Finder). The launcher reads the checked-in Unity version and starts the project with a
64 MiB graphics command ring:

    Tools/unity/open-gangsters.command

CoreDemo incrementally composes generated blocks and registers at most 64 renderers per frame,
but the Editor can still submit a large first-traverse command burst while its Game view and URP
are active. Unity's default ring for this editor version is 16 MiB; exhausting it can freeze the
scene, not merely print a harmless warning. The launcher passes
`-gfx-ring-buffer-size 67108864` as an Editor safety margin. It does not increase the number of
resident city blocks or replace the recycler's per-frame limits.

Launching this project directly from Unity Hub bypasses that argument. Close an already-open
Gangsters Editor before using the launcher; it refuses to start a second instance over the same
project lock.

## What it is good for here

**A compile verdict in seconds, with no harness of our own.**

    Tools/play/recompile.sh        # prints COMPILED or FAILED with the error lines; exits 0 or 1

**Use the script, not the two commands under it.** The raw pair

    unity command recompile --json
    unity command recompile_status --json     # {"status":"completed","failed":false,"errors":[]}

is a trap taken three times already, because Unity **does not compile while it is
playing**: the request is accepted and deferred, and the status then answers about the
PREVIOUS build. A killed soak leaves the editor in Play, and three recompiles in a row
say `completed` while the fix under test is not in the assemblies at all — recognisable
only by the same error coming back with its OLD line numbers. The script stops Play
first and fails if it will not stop, reads the answer to ITS OWN trigger rather than
whatever status was left lying about, waits out the domain reload that drops the port,
and refuses to take `up_to_date` on trust until the console has been read clean of
`error CS` since the moment of the trigger. Each of those three was got wrong first.

This is the editor's own compile — asmdef boundaries, the Editor assembly, Unity 6.5's
obsolete-as-error rules, all of it. The offline Roslyn build in
[the memory notes] is still faster for a quick syntax check, but it is a hand-built
reference set that has been silently wrong before; this cannot be.

**When the editor cannot answer**, there is a middle road, and it sits much closer to the editor
than the hand-built set does: Unity's OWN generated project, built by dotnet.

    dotnet build Assembly-CSharp.csproj -t:Rebuild          # runtime
    dotnet build Assembly-CSharp-Editor.csproj -t:Rebuild   # editor

Unity's reference assemblies, Unity's analyzers, and `-t:Rebuild` so every file is really
compiled instead of skipped as up to date. **But check the project file first, or the verdict is
worthless:**

    ls -la Assembly-CSharp.csproj                        # newer than your edits?
    grep -c 'YourFile.cs"' Assembly-CSharp.csproj        # is your file even in it?

Grep the BARE FILENAME, never the path. The project lists files Windows-style
(`Assets\RoadDemo\TurfMapSurvey.cs`), and a grep carrying those backslashes comes back 0
through this shell whether the file is listed or not - which reads exactly like "my file is not
in the build" and is the same trap one paragraph down, sprung on the check meant to catch it.
If you want the path as well, do it from python, where the backslashes survive.

And the rule that would have caught it without luck, because neither of the two people who hit
it that day caught it by being careful: **when a search is your evidence of ABSENCE, put
something you know is PRESENT in the same search.** A count of zero cannot tell you whether the
thing is missing or the search is broken; a zero beside a one can. One session was saved only by
having a file in the list it happened to know had been there for months - checking just the
eleven new files would have given eleven honest zeros and nothing to weigh them against.

Unity regenerates these on import, so an editor that is wedged - or has simply not refreshed -
leaves a project file that predates the work. A green build then means "the old shape of the code
compiles", which is a true answer to a question nobody asked.

On 2026-08-26 the Editor project was seven hours stale and reported two CS0234s for a type that
existed perfectly well: it had compiled the caller without the callee, because the callee was
written after the project file. Nothing was broken and nothing needed fixing. The same day, the
runtime project happened to be current and did contain every changed file, so its clean build was
worth something - and only the timestamp and the grep told the two cases apart.

Neither build reproduces a domain reload, and the Editor project goes stale first because Editor
files are added less often. For runtime code with the inclusion check done, this settles the
question; for anything else, wait for the editor.

**The console, since the last time anyone looked.**

    python Tools/play/console.py            # errors and exceptions since the mark; 0 clean, 1 not, 2 unread
    python Tools/play/console.py --all      # every level since the mark
    python Tools/play/console.py --mark     # move the mark to now, print nothing
    python Tools/play/console.py --tail 50  # the last 50, mark ignored

`unity command console --json` reads the console directly, so a run's errors come back without
grepping `Logs/Editor.log` — which, with two editors on one project, is not ground truth anyway.
But asked plainly it answers with HISTORY: `clear_console` empties Unity's window and **not** the
buffer this reads, so `--tail` keeps handing back thousands of old entries (once: 22 identical
leak warnings counted across four fix cycles, all of them the same stale ones — the giveaway was
a line number that had since moved). The only honest question is "what is new since this number",
and the script is the number kept on disk. It also folds each stack trace to the one frame that
names a file in this project and collapses a repeated message to `x22`, which is the difference
between two kilobytes and thirty.

Exit 2 is the one to read carefully: it means the read was refused, unparseable **or
incomplete** — a marked window that came back full has older entries outside it, and those are
the ones nearest the mark, which is where the cause of a burst lives. A reader that moves the
mark gets one chance at every line, so an incomplete read says so and never passes for a clean
one. `python Tools/play/console.py --selftest` holds both of those down without an editor.

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

There are **82** of them, and `unity command` prints all 232 the editor answers with every
description in full: 38 KB to find out whether the fear audit is `gangsters_fear_audit` or
`gangsters_audit_fear`. Ask the index instead.

    python Tools/unity/cmds.py              # the 82, names only, four to a line (2.7 KB)
    python Tools/unity/cmds.py --gist       # ...and a line each on what they answer
    python Tools/unity/cmds.py fear         # every match in full, with its parameters
    python Tools/unity/cmds.py --all        # the editor's own commands as well

The names are the index: `gangsters_<subject>_tests` runs an epic's contracts, `_audit` reports
on the live city or a dealt seed, `_probe` orders the Outfit to do one thing so it can be
watched, `_scenario_*` are the named TEST-nnn cases. The table below is the handful that build
or draw something; everything else is a keyword away.

| command | what it answers |
|---|---|
| `gangsters_layout` | the district roll for a seed — the paper plan, no build, no Play |
| `gangsters_measure` | what a prefab actually measures, from the imported asset |
| `gangsters_play` | a harness run **inside the open editor** |
| `gangsters_core` | the city core dealt from a seed (or a run of seeds), judged: deals needed, faults, areas, roads; `--draw` draws the first in the open scene |
| `gangsters_coreblocks` | the catalog's buildings brought into the core: copied into the kit, baked into blocks, stood in the stock row |
| `gangsters_storefront` | GAN-294 storefront bake, visual bench and audit; `--what all|bake|bench|audit` |
| `gangsters_industrial` | four industrial block candidates on the lab bench, and the bake of the ones worth keeping |
| `gangsters_industry` | a whole industrial QUARTER dealt from a seed and judged; `--draw` draws the first in the open scene |

    unity command gangsters_layout --scene Assets/Scenes/CoreDemo.unity --seed 7 --json
    unity command gangsters_layout --seed 1 --count 20 --json     # sweep seeds
    unity command gangsters_measure --name building-bank --json
    unity command gangsters_core --seed 1 --count 30 --json      # thirty seeds: is every one clean?
    unity command gangsters_industry --seed 1987 --count 30 --json   # the same question of the industrial quarter
    unity command gangsters_industry --seed 1987 --draw              # and stand that one up to look at
    unity command gangsters_core --seed 4 --draw                 # draw seed 4 where the Sketch menu would
    unity command gangsters_coreblocks --what all --json         # copy, bake and stand the core's buildings
    unity command gangsters_storefront --what audit --json       # 8 modules, 19 meshes, prefabs and five seeds
    unity command gangsters_play --scene Assets/Scenes/BlockDemo.unity --seconds 90 \
        --step 0.05 --out Temp/play/cli --sets "BlockDemoBuilder.rivalCrews=2"
    python Tools/play/analyze.py Temp/play/cli --verdict

`gangsters_layout` reads road axes from a `RoadDemoBuilder` when the open scene has one; otherwise
it uses a hidden component carrying the canonical field defaults. Passing `--scene` **opens that
scene in the editor** — the person at the keyboard will see their scene change.

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
- **`eval` runs on the main thread, so a busy editor refuses it.** `Main thread operation timed
  out after 5000ms` means the editor had no focus or the scene was hitching, not that the command
  is broken — on an idle editor `return 1+1;` answers in 1.5 s. Raise `--timeout` or ask again;
  do not write a whole Editor script to route around one that failed once.
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
