# Playing a scene with nobody watching

A run of the city without a person at the keyboard: Unity is started headless, a scene
is played for so many sim seconds, and what comes out is a directory of numbers — every
driver, every man on foot, every shot, one line each — plus the editor log and a few
pictures. The point is a **loop**: run, read, change the driving code, run again.

    # the editor must be CLOSED: one Unity to a project
    pwsh Tools/play/run.ps1 -Scene Assets/Scenes/BlockDemo.unity -Seconds 300 `
        -Out $env:LOCALAPPDATA\gangsters-play\r01 `
        -Set "BlockDemoBuilder.rivalCrews=2;BlockDemoBuilder.missionAfter=15"

    python Tools/play/analyze.py $env:LOCALAPPDATA\gangsters-play\r01 --verdict
    python Tools/play/analyze.py $env:LOCALAPPDATA\gangsters-play\r01 --story
    python Tools/play/analyze.py $env:LOCALAPPDATA\gangsters-play\r01 --car 21

Three hundred seconds of city take about twelve of wall clock — the run is stepped in
fixed slices (`Time.captureDeltaTime`), the cameras are switched off, the shadows and
the sound with them, so it is neither waiting on the frame rate nor varying with it.
Two runs of one seed line up row for row.

## The parts

| what | where |
|---|---|
| the runner | `Tools/play/run.ps1` — finds the editor, refuses if the project is open, kills a hung run |
| the harness | `Assets/Scripts/Editor/PlayHarness.cs` — opens the scene, sets fields, plays it, writes the run, shuts Unity down |
| the black box | `Assets/RoadDemo/DriveTrace.cs` — one JSON object a line; off in a normal Play session |
| the reader | `Tools/play/analyze.py` — counts, ranks and tells the story |
| the lab's player | `Assets/BlockDemo/BlockDemoMission.cs` — clicks what a person would click |

`-Set Type.field=value` writes any public field of any component in the scene before it
wakes up; several are joined with `;`. Note that only components ALREADY in the scene
can be set — knobs on things the builder makes at runtime belong on the builder.

## What the trace holds

`car` a driver's frame (speed, the speed asked for, lane, s and d, manoeuvre, and the
reason the code gave for holding back), `ped` a man on foot, `rider` a gun out of a car
window and whether it bore, `board` a man walking to his door, `shot`/`hit` the fight,
`man` a manoeuvre begun or given up, `belt` a step the collision belt had to refuse
(should be none), `mission` what the lab ordered, and `fault` — what the driving code
flagged against ITSELF:

- `stall` — standing still with the throttle asking for speed
- `overbrake` / `brake` — braking harder than the profile allows, or with no reason given
- `jump` — a step longer than the speed accounts for
- `steer` — full lock at speed: the line being followed has a kink in it
- `speeding`, `walkstall`, `carstuck`, `nokill`, `nopark`
- `openfire` — a round from the open with a free flank standing on the man's own fire
  line (EPIC 28). The audit asks the same oracle the man asks, at the moment he pulls
  the trigger, and only after four seconds of it: an empty street is not a fault, and a
  beat between the street changing and the man noticing is not one either.
- the ambush's own five, from the CoverDemo run (`--ambush`): `noambush` (nothing to get
  behind), `nolurk` (dealt flanks, never got down behind them), `seenfirst` (the mob had
  its guns on us first — the surprise range is not holding), `openambush` (it sprang with
  the men standing in the street), `nospring` (the mob walked up and nothing happened).

`cover` is the row a fighting man writes every time he asks the street for a flank:
`found` whether it had one, `first` whether he has yet fired a round in this fight,
`walk` how far off it is. The `shot` row carries `fromcover` beside it - the SHOOTER's
own cover state, as against the older `cover`/`ducked` pair, which have always been
about the man being shot AT. "He got behind something BEFORE he opened up" is the
first `shot` row of each man with `fromcover` true, and `analyze.py --crew` prints
the share.

## Thirty in a row

One good run proves nothing - the faults that matter here turn up one time in three.

    pwsh Tools/play/soak.ps1 -Runs 30

On a Mac editor, `bash Tools/play/soak.sh --runs 30` does the same, and takes the mode
it should run in: `--moto`, `--roadblock`, `--walk`, `--brawl`, `--freeway`, and the two
combat ones — `--cover` (a KILL down the furnished CoverDemo street: the men have forty
metres to cross, which is the exact case that used to fire on the move and look for a bin
afterwards) and `--ambush` (the crew put behind a bin between itself and a mob, left to
lie in wait, and the mob then walked into it). Both end with the epic's own number:

    == cover first: 41/48 men over 30 runs (85%)

which is the share of men whose FIRST round of a fight left from behind something. It is
a RATE over runs and never a seed against a seed — the cover code draws from the shared
`Random`, so one seed says nothing.

Each run is a different quarter (the seed steps), each is judged the same way, and the
tally is written to `soak.txt` beside the runs. A run FAILS on a **defect** only:

- the crew car stood still with somewhere to be (`carstuck`), or would not park
- the traffic gridlocked (any car stood 90 s or more)
- the belt refused a step (two bodies in the same place) - it should refuse none
- anything threw
- the mission's machinery broke: nobody could get into the car, nobody could be set down

Losing the gunfight is NOT a defect. Three men against two is a coin toss (the trace has
both sides at about the same chance a shot), and a run where the outfit is wiped is the
game being a game, not the code being wrong.

## What it has caught so far

- **A turn in the road taken at cruising speed** — the arc is a couple of metres, so half
  a turn took half a second and the body slewed round on the spot. A turn is now refused
  above the speed its arc can carry, and the driver slows for it first.
- **A junction held for ever by a car nobody was coming back for** — the driver shot, the
  car stopped dead across the box, six cars nose to tail behind it for four minutes. A
  stop ordered mid-junction is now served on the FAR side, and a wreck that ends up in one
  gives the box back.
- **Drive-bys that went by without a round fired** — the guns bore for about a second of
  each pass at the hot pace. The car now comes off the throttle alongside the mark, and a
  man leaning out of a window reaches further and further round than one on the pavement.
- **Two cars in one junction** - a car still crossing junction A would claim junction B,
  and (after the first fix) drop its claim on A: somebody then drove in on top of it.
  The rule now is the road's own - you do not enter a junction you cannot leave.
- **A quarter queued behind a parked car** - a car in the box whose exit lane held a
  parked car waited in the junction for ever, and everything crossing waited on it. It
  now comes out and stops behind it on the ROAD, where it can be driven round.
- **A crew that left a man on the kerb** — the lab drove off with two of three aboard, both
  on the same side of the car, so every pass ran with the guns on the wrong side.

## Rules of the loop

- Do not edit the project while a run is in flight: Unity recompiles mid-play and the run
  is spoiled (a reload wipes the statics under it).
- Runs are written OUTSIDE the project. `Temp/` is Unity's own scratch and is emptied when
  the editor shuts down — which is exactly when a run has finished writing its trace.

## The ambush, by hand

`gangsters_ambush_probe` gives the player's right click from the terminal, on a scene
already in Play. It is what proves the two halves of EPIC 28 no unattended run reaches
by luck:

    unity command gangsters_ambush_probe                    # who is lying in wait behind what
    unity command gangsters_ambush_probe --order            # click the nearest thing to get behind
    unity command gangsters_ambush_probe --order --fight    # the same click with the fight already on
    unity command gangsters_ambush_probe --drive            # the car they are behind pulls away

The rows say, per man: `held` (he was dealt a flank), `lurking` (he is down behind it),
`armed`, `inCover`, `fighting` (his mark), `fromHeld` (he is fighting FROM the flank he
was put on) and `onRoad`. After `--drive`, no man should read `onRoad` - nobody guards
the space where a car was.
