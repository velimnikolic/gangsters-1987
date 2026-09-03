# The Night Watch — everything built on 2026-09-02 tested five times over

Design brief, written 2026-09-03 02:30 from the conversation that settled it. Linear: EPIC 31
(to be created), tickets `NIGHT-000..012`. The machine runs alone from about 03:00 until the
user is back; the user merges the branch in the morning after looking at it.

## 0. The ask, in the user's words

> testira svaku funkcionalnost 5 puta, ako u prvom prolazu nađe error staje, sredi i nastavlja
> run 5 puta dok svih 5 ne prođu. na kraju svakog uspešnog prolaza pozove skill
> codex:adversarial-review, ako taj review nađe greške run 5 puta kreće ispočetka. posle toga
> može commit. radi na posebnoj grani koju ću ja ujutru da merge kad vizuelno potvrdim.

## 1. The loop, per functionality

Every ticket below is one functionality and runs the same loop:

1. **Five passes.** The ticket's check runs five times — five seeds where the check takes a
   seed, five passes in one process where it does not (a pure suite that is green once and
   red on the third pass is a static leaking between runs, which is a real bug here).
2. **The first failure stops the pass.** Root-cause it before touching code:
   `python3 Tools/play/analyze.py <run> --why` and `--story` for a harness run, the suite's
   own failure line for a pure suite. Fix it in the shared class (`Docs`: scenes are rigs;
   the fix goes at the choke point, never in the demo builder). Then the five start again
   from pass 1. A `NO RUN` (analyze exit 3: lockfile race, Unity refused to play) is not a
   pass and not a failure — wait for `pgrep -f 'Unity.app/Contents/MacOS/Unity'` to come
   back empty and run that seed again.
3. **Five of five, then the review.** Run Codex's adversarial review on the functionality:

       node "$HOME/.claude/plugins/cache/openai-codex/codex/1.0.6/scripts/codex-companion.mjs" \
           adversarial-review "--wait --base main <the ticket's FOCUS line>"

   `/codex:adversarial-review` is `disable-model-invocation`, so the night calls the same
   script the slash command calls. `--base main` scopes the diff to the night's own fixes;
   the FOCUS line names the files today's commits touched for this functionality and tells
   Codex to read and challenge them even where the diff is empty.
4. **What a finding is.** The review is built to be sceptical and will always say
   something. The night acts on a finding when it is (a) a defect that can be shown — a
   test or a trace row that fails before the fix and passes after — or (b) a plain logic
   error in the diff. Design challenges, "consider", and matters of taste are written into
   the ticket as notes and left alone. An acted-on finding sends the five back to pass 1
   and the review runs again after them. **Cap: three review rounds per ticket**; a
   ticket still red after three is written up as open and the night moves on.
5. **Commit.** Review clean → `code-review-unity` on the pending `.cs` (the commit-time rule
   in `CLAUDE.md`) → commit **on the night branch**, explicit paths only. Message:
   `test(night): <functionality> — 5/5 seeds <a..b>, reviewed` with the fixes listed in the
   body. Then the ticket goes to Done in Linear with the ledger lines as a comment.
6. **Never** edit a source while a run is in flight (the driver is synchronous, so this
   only means: no second session); never `pkill Unity`; never remove `Temp/UnityLockfile`
   while a Unity process is alive.

## 2. The branch and the machine — pre-flight (NIGHT-000, part one)

* **Branch** `night/2026-09-03` from `main` HEAD, in this checkout (a worktree would
  re-import the whole Library). The user merges it. The pending working-tree edits
  (`PersonnelAlmanac.Command/Organization/Personnel.cs`, `Docs/design-briefs/collector-brief.md`,
  `ProjectSettings/Packages/`) belong to another session's in-flight work: they ride along in
  the tree, are **never staged**, and if they do not compile the night stops at pre-flight and
  says so rather than touching them.
* **One session.** Five Claude sessions have this checkout open right now, one of them editing
  the almanac at 01:56. The night needs the tree to itself: the user closes every other
  session before leaving. The night cannot verify this; it is a condition, not a check.
* **Editor closed.** The whole night runs the batch harness (`Tools/play/run.sh` → one fresh
  Unity per run, no memory drift over a hundred runs, the fix-then-run loop serialised by
  construction). The user closes the editor before leaving. If it is still open, the night
  asks it to leave through `unity command eval 'UnityEditor.EditorApplication.Exit(0)'` only
  when no scene is dirty; a dirty scene is reported and the night waits.
* **Compile clean** on the branch before the first run: the offline Roslyn build
  (`~40 s`, memory `offline-unity-verification`) or the first batch run's `unity.log`
  (`grep 'error CS'`). A stale, red tree at 03:00 is the whole night lost.
* **Disk.** 190 GB free; the night writes under
  `~/Library/Application Support/gangsters-play/night-2026-09-03/<ticket>/<mode>/run-NN`.
  The August soaks there are 44 GB and can go, but that is the user's call, not the night's.
* **Ledger.** `night-2026-09-03/ledger.md`: one line per pass, per review, per commit, with
  the time. This is what the next loop iteration reads to know where it is, and what the user
  reads in the morning.

## 3. What was built today — the inventory

| # | functionality | commits (09-02 → 09-03) | Linear | headless oracle today |
|---|---|---|---|---|
| A | the paper underworld: twenty families with a book, minds that file orders, relations and war, orders that do something, presence with nobody on the street, the month-long sim | `646c6965` `84a695f8` `9ac3a802` `eaf5e829` `78504a22` `01eb0e84` `f7aaf0e9` `a4d45e71` `dce798eb` | EPIC 25 GAN-244 (RIVAL-001..011 inline) | `gangsters_house/relations/underworld/command/organization_tests`, `gangsters_underworld_sim --sweep` |
| B | the campaign written down and read back; a man in the cells survives a save | `77e28d02` `be6a4710` | RIVAL-010, GAN-271 open | `gangsters_save_tests`; in Play only by hand (`gangsters_save`/`gangsters_load`) |
| C | wages: one house rate, the bargain's life, the short envelope, tenure | `1cd31ecc` | EPIC 24 GAN-238 Done | `gangsters_wage_tests`, `gangsters_economy_tests/audit` |
| D | loyalty follow-up: the reason feed, the defector's door, notability marker, time in rank; the one bag man per crew | `9d5dbda9` `0159de4c` | EPIC 17 GAN-227 Done, GAN-262 Done | `gangsters_loyalty/notability/organization_tests` |
| E | the racket on paper: rounds walk on paper, every order says whose it is, the shakedown seam, a block's doors walked unasked | `032469dc` `9ac3a802` `58d83420` `3c8ea3f9` | GAN-224/225 Done | `gangsters_round/rack_tests`, `gangsters_rack_audit`, `gangsters_business_tests/audit` |
| F | the law: the shopkeeper's telephone, a trial the state can lose, the lawyer and bail, witness pressure, longer sentences; a hideout of our own, a precinct that exists, a courthouse the transfer drives to; men answer for themselves when the law walks over; a dead policeman brings every car | `19aa7a06` `3d7e06af` `58d83420` `be6a4710` | EPIC 26 GAN-245 Done; EPIC 23 GAN-235/237 Done, GAN-236 in progress | `gangsters_police_tests` (46 contracts). **No runtime oracle**: no mission shakes a door, no `complaint`/`court` trace row |
| G | cover first and the ambush; a man takes cover on the side he came from; the player aims it | `6e061257` `574cf858` `e372fb56` | EPIC 28 GAN-255 Done | `soak.sh --cover`, `--ambush`, `analyze.py --crew`, `--cover-tally`, `gangsters_ambush_probe` (by hand). **No oracle for the side rule** |
| H | the street: mixed arsenal, a braked man stops skating, the crew drives to the job and gets out, one round one flash, the routed crew crosses the furnished street, a gun handed to a named man stays his | `1a9fee35` `3c8ea3f9` `2162e65c` `12d14b22` | — | `soak.sh --brawl/--walk/--car/--moto/--roadblock`, `gangsters_crew_audit_tests` |
| I | the city deal: a courthouse in the kit, every 5 m of glass a shop, no policeman keeps a grocery, smashed glass finds the real frontage, the almanac's block file split | `1ea0fb89` `059a4f79` `3d7e06af` | — | `gangsters_core --count`, `gangsters_storefront_audit`, `gangsters_geography_tests/audit`, `gangsters_core_vacancy_tests` |
| J | the scenario suite and control (takeover, withdrawal, contest, loss, responsibility, capacities, phase 1) — touched by A and E through the shared gateway | — | EPICs 7/8 | the nine `gangsters_scenario_*` + `gangsters_control_tests/audit`, `gangsters_presence/fear_tests/audit` |
| K | sound, FX and the book's face: the loud gun pack, fire and smoke from the particle pack, the FX demo, the ledger layout, every sheet line on one middle, the clock keeps its speed | `6acd0134` `ce850057` `755d0da5` `adee2927` `00e37122` `059a4f79` | — | none but "the scene plays without an error"; pictures for the morning |

## 4. The tickets, in this order

Pure and fast first (a failure there is cheap and tells the most), the furnished-street
combat next, the forced scenarios on the mini core after that, and the old driving soaks last
— car 8/10 and moto 6/10 on 2026-08-23 were DRIVING-domain backlog (a crew that bails from a
stuck car, tow queues, moto passes without a round, belt clinches), and the night must not
sink into that before today's work has had its five. The running order is therefore
000, 001, 002, 003, 004, 005, 006, 007, 008, 009, 010, **013**, 011, 012.

### NIGHT-000 — The driver, the branch, the headless suites
**Builds.** (1) pre-flight (§2) and the branch; (2) `GangstersTools.SuiteRunner` — a batch
`-executeMethod` that runs a named list of this project's `[CliCommand]`s in one Unity with the
editor closed and writes each result as JSON (Newtonsoft is in the project; the commands are
public statics on `PipelineCommands`, reachable by attribute name), N passes in one process;
`Tools/play/suites.sh --passes 5 --commands a,b,c --out DIR`; (3) `Tools/play/night.sh
<ticket>` — runs that ticket's five, appends to the ledger, exits 0/1/3 like `analyze.py`.
**Done when.** `suites.sh --passes 1 --commands gangsters_wage_tests` answers from a closed
editor; `night.sh NIGHT-003` writes five ledger lines. Committed on the branch.
**Time.** ~1 h. If the SuiteRunner is not answering after 90 min, the fallback is the open
editor for the pure tickets (NIGHT-001..004, 007, 008) and the batch soaks for the rest; the
ledger says which path was taken.

### NIGHT-001 — The paper underworld (A)
**Check ×5 passes.** `gangsters_house_tests`, `gangsters_relations_tests`,
`gangsters_underworld_tests`, `gangsters_command_tests`, `gangsters_organization_tests`
(which carries Gang, Personnel, Police, Wage, Loyalty, Notability, Command, Skill, Learning,
Personality inside it). Then `gangsters_underworld_sim --sweep 5 --days 90 --houses 21`
and `python3 Tools/sim/underworld_tally.py` over it.
**Passes when.** every `passed:true`; the sweep has no error and no ownership refusal; the
tally prints and no house's safe is under water at day 90 (a `negatives` count above zero is
a failure, not a note).
**FOCUS.** `Outfit/HouseMind.cs HouseIntent.cs HouseView.cs HouseRelations.cs Engagement.cs
OrderEffects.cs OrderResolution.cs Underworld.cs Tribute.cs Diplomacy.cs`,
`RoadDemo/TerritoryRuntime.Minds.cs TerritoryRuntime.Paper.cs`, `Tests/UnderworldSim.cs
PaperCity.cs`.

### NIGHT-002 — Save and load (B)
**Check ×5 passes.** `gangsters_save_tests`. Then the round trip in a running city: the
harness gains `-hSave <t>` (NIGHT-000 or here, ~45 min): at `t` `CampaignSave.Write(a)`,
`Read`+`Apply`, `Write(b)` one frame later; `a` and `b` must be equal byte for byte save the
timestamp, and the run goes on to its end with no exception. Five seeds on `BlockDemo`
(`spacingSeed` 102..106, 240 s, save at 120).
**Passes when.** suites green; `a == b` on all five; `summary.json` `exceptions:0`.
**FOCUS.** `Save/CampaignFile.cs CampaignSave.cs`, `Outfit/OutfitSnapshot.cs`,
`Personnel/RosterSnapshot.cs`, `Territory/TerritorySnapshot.cs`, `Police/PrisonPipeline.cs`.

### NIGHT-003 — Wages and the envelope (C)
**Check ×5.** `gangsters_wage_tests`, `gangsters_economy_tests`;
`gangsters_economy_audit --seed 1..5`.
**FOCUS.** `Outfit/Wages.cs Accounts.cs HireMarket.cs CampaignRunner.cs`,
`Personnel/Career.cs`, `UI/PersonnelAlmanac.Finances.cs`.

### NIGHT-004 — Loyalty follow-up and the bag man (D)
**Check ×5.** `gangsters_loyalty_tests`, `gangsters_notability_tests`,
`gangsters_personality_tests`, `gangsters_skill_tests`, `gangsters_learning_tests`;
`gangsters_organization_audit --seed 1..5`.
**FOCUS.** `Personnel/ReasonFeed.cs Defection.cs Loyalty.cs CollectorChoice.cs RosterOps.cs
Notability.cs`, `Outfit/OpenDoors.cs`, `Gameplay/PersonnelDirector.cs`.

### NIGHT-005 — The racket on paper (E)
**Check ×5.** `gangsters_round_tests`, `gangsters_rack_tests`,
`gangsters_territory_foundation_tests`, `gangsters_business_tests`;
`gangsters_rack_audit --seed 1..5`, `gangsters_business_audit --seed 1..5`.
**FOCUS.** `Territory/TerritoryRounds.cs TerritoryCommands.cs TerritoryRacketOrders.cs`,
`RoadDemo/TerritoryRuntime.Collection.cs .Seam.cs .Shakedown.cs`,
`Gameplay/PlayerCommands.cs`, `Business/BusinessDeeds.cs`.

### NIGHT-006 — Scenarios, control, presence, fear (J)
**Check ×5.** the nine `gangsters_scenario_*`, `gangsters_scenario_phase1`,
`gangsters_control_tests`, `gangsters_presence_tests`, `gangsters_fear_tests`;
`gangsters_control_audit`, `gangsters_presence_audit`, `gangsters_fear_audit` at seeds 1..5.
**FOCUS.** whatever A and E changed under `Territory/` and `Outfit/`; the scenarios are the
regression net, not the subject.

### NIGHT-007 — The city deal (I)
**Check ×5 seeds.** `gangsters_core --seed 1 --count 5 --json`: every deal clean, and every
deal holds **one courthouse and at least one station**; `gangsters_geography_tests`,
`gangsters_core_vacancy_tests`; `gangsters_storefront_audit` and `gangsters_geography_audit`
at seeds 1..5: no policeman on a grocery, every 5 m of glass a door of its own.
**FOCUS.** `RoadDemo/CoreDistrict.cs RoadDemoBuilder.cs`, `Editor/SyntyKitExtractor.cs`,
`CityKit/Buildings/building-courthouse.prefab`.

### NIGHT-008 — The law on paper (F, part one)
**Check ×5.** `gangsters_police_tests` (46 contracts: the deed decides the sentence, the
lawyer cuts the days, word against word mostly walks, two eyewitnesses convict, bail comes
back as a man, the hideout is one address, a cop killer never comes clean).
**FOCUS.** `Police/CourtCase.cs PrisonPipeline.cs`, `Personnel/Lawyer.cs Sentencing.cs
Incident.cs`, `Territory/TerritoryHideout.cs`, `RoadDemo/LawDesk.cs LawWire.cs
WitnessWatch.cs PoliceDispatch.Complaint.cs PoliceDispatch.Wanted.cs`.

### NIGHT-009 — The law on the street (F, part two) — builds its oracle first
Nothing headless reaches the telephone today. **Builds** (~2 h): `BlockDemoBuilder.missionShakedown`
— after `missionAfter` the lab orders the crew to walk a block's doors (the same order the
right-click files); trace rows `complaint` (who rang, from which door, how long after the
lean), `statement`, `arrest` (already there?), `court` (the transfer reached the courthouse,
the verdict); `analyze.py --law`: a connected owner rings inside the window, a car comes,
the man is taken or fights, the transfer arrives, a verdict lands; faults: `noring`
(connected owner, no call), `nocar` (call, no car in 120 s), `lostman` (taken, never
reached the courthouse), `nocourt`. `soak.sh --law`.
**Check ×5 seeds.** `soak.sh --law --runs 5`.
**Passes when.** 5/5 with no fault and no exception.
**Fallback.** If the mode is not judging by 06:00, the ticket is written up as open with what
was built; NIGHT-010+ go ahead.

### NIGHT-010 — Cover first and the ambush (G) — the side rule gets its row
**Builds** (~30 min): the `cover` trace row gains `side` (the flank's side relative to the
man's approach) and `CrewAudit` a `wrongside` fault (he got behind it on the far side from
where he came). `analyze.py --crew` counts it.
**Check ×5 seeds.** `soak.sh --cover --runs 5`, `soak.sh --ambush --runs 5`,
`gangsters_crew_audit_tests`.
**Passes when.** 5/5 each, `wrongside` 0, `openfire` 0, the ambush's five (`noambush nolurk
seenfirst openambush nospring`) 0; the cover-first share over the ten runs ≥ 80 % (the brief
tunes this number; 85 % was the last reading over 30).
**FOCUS.** `RoadDemo/DemoCrews.Cover.cs DemoCrews.Combat.cs CrewOverlay.CoverAim.cs
CrewAudit.cs`, `BlockDemo/BlockDemoMission.Ambush.cs`, `Editor/AmbushProbe.cs`.

### NIGHT-013 — The forced scenarios on the mini core (CoreDemo with the levers pulled)
The user's second ask: the night does not wait for a scenario to happen by chance, it sets
the mini core demo up so it **must** happen, and judges that it did. `CoreDemo.unity`
(`Assets/RoadDemo/CoreDemoBuilder.cs`: `seed`, `rivalCrews` 0..20, `rivalHoods`, `police`,
`policeBeatPairs`, `startHour`, `realSecondsPerGameHour`, `carCount`, `pedestrianCount`) has
never run under the harness; the ticket opens with a smoke run (120 s, default knobs) before
any scenario, and a smoke run that will not play ends the ticket with a write-up.

**Levers that exist** (`-hSet`): `CoreDemoBuilder.*` above; `RoadDemoBuilder.policeCarCount /
policeOfficerCount / policeBeatPairs`; `BlockDemoBuilder`-style mission flags land on
`CoreDemoBuilder` for this scene. **Levers the ticket adds** (builder fields, since the
runtime components are made by the builder and cannot be `-hSet`): `ownerTraitOverride`
(every owner `Connected` / every owner `Frightened`, `TerritoryEconomy.TerritoryOwnerTrait`),
`playerSafeAtStart`, `mindThinkEveryHours` (→ `HouseMindConfig.Default`), `playerIdle` (no
orders at all from house 0). **Rows the ticket adds** where missing: `turf` (a block's leader
or control state changed: block, from, to, hour); the `complaint`/`court` rows come from
NIGHT-009. **The reader**: `analyze.py --core` — counts `house` rows by gang and tier,
`turf` changes, calls/arrests/verdicts, plus the ordinary fault and exception rules.

Five scenarios, each five seeds, each with its own verdict; the clock at
`realSecondsPerGameHour=5` so a game day is 120 sim-seconds and a week is ~15 minutes real
per run:

| # | scenario | levers | passes when (all five seeds) |
|---|---|---|---|
| S1 | **every owner rings** | `ownerTraitOverride=Connected`, police ×3, `missionShakedown` on our crew, 20 houses | every shakedown of a door produces a `complaint` inside the window; a car comes; the man is taken or fights; the transfer reaches the courthouse; a verdict lands; no gridlock, no exception |
| S2 | **a ton of police** | `policeCarCount=12`, `policeOfficerCount=12`, `policeBeatPairs=12`, 20 houses at war (`rivalHoods=4`) | no car stands 90 s; no `belt`; a dead policeman's swarm arrives and dissolves (cars back on patrol within a game hour); arrests complete; no exception |
| S3 | **does the AI spread like we do** | `playerIdle`, 20 houses, 7 game days | `house` rows show tier 7 (expand) fired by ≥ 1 house; ≥ 1 `turf` row moves a block's leader to a rival; no house safe under water at day 7; every house thinks (rows for all 20 gangs) |
| S4 | **no police at all** | `police=false`, 20 houses, 7 game days | the ladder reaches war for ≥ 1 pair (a `house` row with a war intent and both sides' `shot` rows); the crew audit is clean (no `strayman`/`zebrastuck` epidemics); no exception |
| S5 | **the broke player** | `playerSafeAtStart=0`, 3 game days | short envelopes on the day tick; ≥ 1 reason-feed line about pay; ≥ 1 man leaves through `OpenDoors`; the safe never reads below zero; no exception |

**Builds** (~2–3 h): the levers, the `turf` row, `analyze.py --core`, `soak.sh --core-s1..s5`.
**FOCUS.** `RoadDemo/CoreDemoBuilder.cs TerritoryRuntime.Minds.cs`, `Territory/TerritoryEconomy.cs`,
`Outfit/HouseMind.cs`, `Tools/play/analyze.py`.
**Fallback.** S3 and S4 need only the `turf` row; S1 needs NIGHT-009. If 009 is open, S1 is
written up as blocked and S2–S5 run.

### NIGHT-011 — The street (H): brawl, walk, then the old driving soaks
**Check ×5 seeds each, in this order.** `soak.sh --brawl --runs 5`, `--walk`, `--car`,
`--roadblock`, `--moto`. Brawl and walk carry today's changes (the brake, the mixed
arsenal, the boarding, the flash); car/roadblock/moto carry the August backlog.
**Passes when.** 5/5 per mode. The rule of the soak stands — every failure it finds is fixed,
never explained away — but whatever is still red at 08:00 is written into the ticket with
`--why` output and left honestly open.
**FOCUS.** `RoadDemo/CrewWalker.cs CrewWalker.Steer.cs DemoCrews.Boarding.cs
DemoCrews.Combat.cs WalkRoute.cs WalkObstacles.cs`.

### NIGHT-012 — Scenes, pictures, and the morning report (K)
**Check ×5 scenes.** `run.sh` 90 s at step 0.05 with `--shot 40` on `BlockDemo`,
`CoverDemo`, `Ledger`, `MotoDemo`, and the FX demo scene: `summary.json` `errors:0
exceptions:0`, no `error CS`, no missing-reference line in `unity.log`; the five pictures
copied to `night-2026-09-03/pictures/`. Then the report: `night-2026-09-03/REPORT.md` — per
ticket: passes, fixes (commit hashes), review rounds and what was acted on, what is open and
why; the branch name and the merge command; the pictures. The epic and every finished ticket
go to Done in Linear; open ones stay open with the report pasted in.

## 5. The driver

One Claude session, a ralph loop so the turn cannot end the night:

    /ralph-wiggum:ralph-loop "Work Gangsters 1987 EPIC 31 (the Night Watch, Linear GAN-xxx) \
      top to bottom by the rules in Docs/design-briefs/night-watch-brief.md. Read \
      ~/Library/Application\ Support/gangsters-play/night-2026-09-03/ledger.md first to see \
      where you are. Output <promise>NIGHT DONE</promise> only when NIGHT-012's REPORT.md is \
      written and every ticket is Done or written up as open." \
      --completion-promise "NIGHT DONE" --max-iterations 300

The loop's state is files, git and Linear: the ledger, the branch, the ticket statuses.
Nothing lives only in the conversation.

## 6. Budget

| what | one run | ×5 | note |
|---|---|---|---|
| pure suites, 5 passes in one batch process | — | ~2 min per ticket | cold start dominates |
| car / roadblock / cover / ambush / brawl (480 s sim) | ~1.2 min | ~6 min | 21 s of sim, ~45 s of Unity start |
| walk (1500 s) | ~2 min | ~10 min | |
| moto (900 s) | ~1.5 min | ~8 min | |
| the whole runtime matrix, one clean pass | | ~50 min | |
| a Codex adversarial review | 5–15 min | ×13 tickets | ~2 h across the night |
| a CoreDemo scenario run (7 game days at 5 s/hour ≈ 840 s sim, a bigger city) | ~3 min | ~15 min per scenario, ~75 min for S1–S5 | never measured; the smoke run says |
| NIGHT-000 + the oracles (009, 010, 002) + the levers (013) | | ~6–7 h | the largest single cost, and more than the night has if everything else also needs fixing |

From 03:00 to 09:00 is six hours. The pure tickets and the existing soaks fit in two of them
if nothing fails; the new harness code (000, 009, 013) is the other four and then some, so the
night is expected to end with 011 and parts of 013 open, not with everything Done. The order
in §4 makes sure what is open is the old backlog and the newest oracle, never today's work.

## 7. What the night cannot do

* **See.** The user's "vizuelno potvrdim" is the morning; the night leaves pictures.
* **Click.** The player's cover aim (`CrewOverlay.CoverAim`), TAKE THE BAG, the ledger pages —
  only `gangsters_ambush_probe --order` reaches one of them and only in an open editor. Out.
* **Answer a dialog.** A dirty-scene prompt, a licence prompt, a crash reporter: any of these
  ends the night silently. Hence the editor closed before the user leaves.
* **Know another session is typing.** Hence one session.

## 8. Decisions the user rules on before this goes to Linear

1. Five passes, not the ten of the 2026-08-23 rule.
2. A cap of three review rounds per ticket (the ask has no cap).
3. The editor closed all night, batch harness throughout; the user closes it and every other
   session before leaving.
4. The old driving soaks (car/roadblock/moto) run last and may end the night open.
5. NIGHT-009 (the law's runtime oracle) is the one ticket that is mostly new code; it can be
   dropped to "pure suite only" if the user would rather the night spent that time on fixes.
6. The August soak directories (44 GB) — delete or keep.
7. NIGHT-013's five scenarios and the levers they add (`ownerTraitOverride`,
   `playerSafeAtStart`, `mindThinkEveryHours`, `playerIdle`, the `turf` row) — the set, or a
   different five.
8. What the night does when the six hours run out: the brief says "write it up and stop at
   NIGHT-012"; the alternative is "keep looping until the user cancels the ralph loop".
