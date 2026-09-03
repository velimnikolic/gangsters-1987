# The Night Watch — everything built on 2026-09-02 tested five times over

Design brief, written 2026-09-03 02:30, revised 03:10 after the contrarian's pass and the
user's rulings. Linear: EPIC 31 (to be created), tickets `NIGHT-000..013`. The machine runs
alone from about 03:30 until the user is back; the user merges the branch in the morning
after looking at it.

## 0. The ask, in the user's words

> testira svaku funkcionalnost 5 puta, ako u prvom prolazu nađe error staje, sredi i nastavlja
> run 5 puta dok svih 5 ne prođu. na kraju svakog uspešnog prolaza pozove skill
> codex:adversarial-review, ako taj review nađe greške run 5 puta kreće ispočetka. posle toga
> može commit. radi na posebnoj grani koju ću ja ujutru da merge kad vizuelno potvrdim.

> takođe treba da menja parametre mini core demoa da bi omogućio uspešan scenario, tipa svaki
> owner zove policiju, policije ima tonu, dal se AI širi ko mi itd.

> save nemamo UI, to preskoči. editor otvoren, nek radi u editoru. BlockDemo ne treba da se
> koristi, no MiniCoreDemo; MiniCoreDemo i umesto CoreDemo-a.

## 1. The loop, per functionality

Every ticket below is one functionality and runs the same loop:

1. **Five passes.** The ticket's check runs five times — five seeds where the check takes a
   seed, five calls into the one open editor where it does not (a pure suite that is green
   once and red on the third call is a static leaking between runs, a real class of bug
   here: `Underworld.Ensure`, `DriveTrace`, `CrewAudit` are all static).
2. **The first failure stops the pass.** Root-cause it before touching code:
   `python3 Tools/play/analyze.py <run> --why` and `--story` for a harness run, the suite's
   own failure line for a pure suite. Fix it in the shared class (`CLAUDE.md`: scenes are
   rigs; the fix goes at the choke point, never in a demo builder). Then the five start
   again from pass 1. **At most two fix attempts per ticket**; a third failure is written up
   as open with the `--why` output and the night moves on.
3. **A failure that was already there is not the night's.** Pre-flight runs every pure
   suite once at HEAD before the branch is cut; anything red then is written in the ledger
   as `PRE-EXISTING` and reported, not fixed, unless the user has ruled on it (§8.1).
4. **Five of five, then the review.** Codex's adversarial review, on the uncommitted fixes:

       node "$HOME/.claude/plugins/cache/openai-codex/codex/1.0.6/scripts/codex-companion.mjs" \
           adversarial-review "--wait --scope working-tree <the ticket's FOCUS line>"

   `/codex:adversarial-review` is `disable-model-invocation`, so the night calls the same
   script the slash command calls. `--scope working-tree` reads the fixes BEFORE they are
   committed (`--base` would read commits only — `scripts/lib/git.mjs:143-149,326-329`), so
   the user's order stands: review, then commit. The FOCUS line names the files today's
   commits touched for this functionality and tells Codex to read and challenge them even
   where the diff is empty. **A ticket with no fix has nothing in the tree**: the ledger
   says `no change, no review` and the ticket goes straight to commit-less Done.
   The call runs in the background (it takes 5–15 min; a foreground Bash call dies at
   its timeout) and the night waits on the process, never on a clock.
5. **What a finding is.** The review is built to be sceptical ("use `approve` only if you
   cannot support any substantive adversarial finding") and will always say something. The
   night acts on a finding only when its JSON confidence is **≥ 0.8** AND the night can
   first write the test or trace row that fails because of it. Design challenges,
   "consider", taste, and anything that would change a documented number (`Docs/economy-prices.md`,
   the wage table, the deal) are written into the ticket as notes and left alone. An
   acted-on finding sends the five back to pass 1 and the review runs again after them.
   **Cap: three review rounds per ticket**, and a wall-clock cap beside it: **60 min for a
   pure ticket, 120 min for a runtime ticket**, measured from the ticket's first pass.
6. **Commit.** Review clean → `code-review-unity` on the pending `.cs` (the commit-time rule
   in `CLAUDE.md`) → commit **on the night branch**, explicit paths only, one commit per
   ticket: `test(night): <functionality> — 5/5 seeds <a..b>, reviewed`, the fixes listed
   in the body. Then the ticket goes to Done in Linear with the ledger lines as a comment.
7. **Never** `pkill Unity`; never `eval` while a run is in Play (a domain reload kills the
   run — memory `editor-refresh-and-pause-traps` §4); never edit a source while a run is in
   Play (same reason); never touch `Temp/UnityLockfile`.

## 2. The machine — pre-flight (NIGHT-000, part one)

* **Editor OPEN, all night** (user's ruling). Everything goes through `unity command`
  against the editor on port 7800: the pure suites and audits as CLI commands, the runtime
  runs as `gangsters_play` (`PipelineCommands.cs:1851`: same `PlayHarness.RunWith`, same
  trace, same `analyze.py`, `quit=false`, returns at once; the run is over when
  `summary.json` appears). The batch harness (`run.sh`/`soak.sh`) is **not** used: it
  needs the editor closed.
* **Waiting.** `gangsters_play` returns immediately; the night waits on `summary.json`
  with a file poll in a background Bash (the pattern in `Tools/play/tally.sh`), never with
  `eval` (each `eval` takes the main thread and a poll loop starves the run —
  `editor-refresh-and-pause-traps` §3). If Play is still on after `wall` (the harness's
  own 900 s), `unity command editor_stop`, mark the run `NO RUN`, rerun the seed once.
* **The fix cycle in an open editor.** Auto-refresh is off, so after an edit:
  `unity command eval --code 'UnityEditor.AssetDatabase.Refresh(); return "ok";'` →
  `unity command recompile` → poll `recompile_status --json` (the result is a JSON string
  inside the envelope; parse twice) until `completed`, `failed:false`. `up_to_date` after a
  real edit means the refresh did not happen. A domain reload drops the server for a few
  seconds: poll `unity status` until `ready`. A brand-new `.cs` that the compilation never
  picks up (hit 2026-09-02 with `LawDesk.cs`) is diagnosed with the `CompilationPipeline`
  probe in that memory and cured by delete-refresh-restore.
* **Sticky Pause.** Before every run, while stopped:
  `eval 'return UnityEditor.EditorApplication.isPaused;'` — clear it if true. A paused
  editor makes `gangsters_play` write a 0-byte `play.log` and never a `summary.json`.
* **Editor health.** Every ten runs: `unity command get_performance_stats --json` and the
  editor's RSS (`ps -o rss= -p <pid>`; 4.9 GB at 10 h uptime tonight). Above 16 GB, or if
  `unity status` shows no editor for two minutes: relaunch with
  `Tools/unity/open-gangsters.command` in the background, poll `unity status` to `ready`,
  write `EDITOR RESTARTED` in the ledger. The night never exits the editor itself when a
  scene is dirty (`EditorSceneManager.GetActiveScene().isDirty`); pre-flight reports a dirty
  scene to the user and waits.
* **CLI main-thread cap.** A command over the port has 30 s: `gangsters_core --count 5`
  times out, `--count 1` five times does not. Every seeded check is one seed per call.
* **The scenes.** Every runtime run is on **`Assets/Scenes/MiniCoreDemo.unity`** (user's
  ruling: not BlockDemo, and instead of CoreDemo). It is `CoreDemoBuilder` with
  `newSeedEveryPlay: 0`, `seed 1987`, `quarterBudget: 2` ("the test rig"), 3 rival crews,
  6 cars, 20 pedestrians, 1 beat pair, clock 60 s/hour. It carries the real campaign: the
  territory plan (`CoreDistrict.cs:630` → `TerritoryRuntime`), the twenty-one houses' books,
  the minds, the courthouse (`CoreDistrict.PickCourthouse`). **Seeds:** every run sets
  `CoreDemoBuilder.seed=N` (the scene already has `newSeedEveryPlay=0`, set it anyway).
  `CoverDemo.unity` stays for cover-first and the ambush — it is EPIC 28's own bench and
  the user did not name it; **assumption, §8.7**.
* **What the core lacks that the lab had — the night builds it (user's ruling, 03:45:
  "nek menja pravila minicoredemo grada, nek doda policiju, kola i sve to kad testira").**
  `CoreDemoBuilder` has no mission or outfit knobs and hard-codes `policeCarCount = 0` /
  `policeOfficerCount = 0` ("no police forecourt in the structural core yet",
  `CoreDemoBuilder.cs:112-113`; beat pairs only). `CoreDistrict` deals a courthouse
  (`PickCourthouse`, `CoreAmenityLayout`) but no station house, and `RoadDemoBuilder`
  stands patrol cars only on a `StationHouse` forecourt (`SpawnPatrolCars`,
  `RoadDemoBuilder.cs:4349`; the stall row at `:3887`). NIGHT-000 ports the knobs **and
  gives the mini core a precinct with a forecourt** the same way the courthouse got its
  parcel, so `policeCars=N` stands N cars on it. Whatever else a scenario needs of the
  city's rules (police, cars, pedestrians, houses, doors) the night changes in the shared
  builder with a knob whose default leaves the scene as it is. `CoreDemoBuilder` is shared
  by `CoreDemo.unity` and `MiniCoreDemo.unity`; the night plays only the mini one.
* **Branch** `night/2026-09-03` from `main` HEAD, in this checkout (a worktree would
  re-import the Library). `main` == `origin/main` at 03:00. The working tree at 03:00 is
  read from `git status`, not assumed: whatever is pending belongs to another session,
  rides along, is never staged, and if it does not compile the night stops at pre-flight
  and says so.
* **One session, launched to run unattended.** Five Claude sessions have this checkout
  open right now. The night needs it alone: the user closes the others. The night session
  is started with `--dangerously-skip-permissions` (`settings.json` has `defaultMode: auto`
  and no allow entries for the scripts, `node`, `git commit` or Linear writes; a permission
  prompt at 03:04 is the night lost).
* **Baseline.** Pre-flight runs every pure suite once at HEAD (about two minutes through
  the port) and writes the red ones as `PRE-EXISTING`. Known at 03:00:
  `gangsters_core_vacancy_tests` (seed 1987: no stand-alone filling station selected;
  unprogrammed block 20×30 m at (-145,-40)), `gangsters_scenario_capture_audit` (a
  source-text grep, `PipelineCommands.cs:1023`, hits two DOC COMMENTS saying "TAKE IT":
  `House.cs:124`, `PersonnelAlmanac.Command.cs:1355` — false positives), and
  `gangsters_scenario_phase1` (aggregates that audit under `architectural_failures`,
  `PipelineCommands.cs:1169-1187`). The user rules on these (§8.1).
* **Disk.** 190 GB free; runs go to
  `~/Library/Application Support/gangsters-play/night-2026-09-03/<ticket>/<mode>/run-NN`
  (never under the project's `Temp/`). The August soaks there are 44 GB — the user's call.
* **Ledger.** `night-2026-09-03/ledger.md`. Its FIRST line is machine-readable and rewritten
  atomically: `STATE: ticket=NIGHT-00x pass=n fixes=k review=r since=HH:MM`. Below it, one
  line per pass, fix, review, commit, restart, with the time. The ledger overrides anything
  the session remembers.

## 3. What was built on 2026-09-02 — the inventory

43 commits on `main` between 09-02 02:38 and 09-03 02:20, two git identities ("Nikola
Velimirovic" 20, "velim nikolic" 23 — both the user, two machines); `main` and
`origin/main` are equal, so the remote work is in this list.

| # | functionality | commits | Linear | headless oracle today |
|---|---|---|---|---|
| A | the paper underworld: twenty-one families with a book, minds that file orders, relations and war, orders that do something, presence with nobody on the street, the month-long sim; **the chair has an heir and a family with no head stops** | `646c6965` `84a695f8` `9ac3a802` `eaf5e829` `78504a22` `01eb0e84` `f7aaf0e9` `a4d45e71` `dce798eb` `ffbe6608` | EPIC 25 GAN-244 (RIVAL-001..011 inline) | `gangsters_house/relations/underworld/command/organization_tests`, `gangsters_underworld_sim --sweep` |
| B | the campaign written down and read back | `77e28d02` `be6a4710` | RIVAL-010, GAN-271 open | `gangsters_save_tests` only — **no UI, skipped** (user) |
| C | wages: one house rate, the bargain's life, the short envelope, tenure | `1cd31ecc` | EPIC 24 GAN-238 Done | `gangsters_wage_tests`, `gangsters_economy_tests/audit` |
| D | loyalty follow-up: the reason feed, the defector's door, notability marker, time in rank; the one bag man per crew; **the book's verdict on the CHAIN OF COMMAND sheet** | `9d5dbda9` `0159de4c` `5d9c2008` | EPIC 17 GAN-227 Done, GAN-262 Done | `gangsters_loyalty/notability/organization_tests`; the sheet has no oracle |
| E | the racket on paper: rounds walk on paper, every order says whose it is, the shakedown seam, a block's doors walked unasked | `032469dc` `9ac3a802` `58d83420` `3c8ea3f9` | GAN-224/225 Done | `gangsters_round/rack_tests`, `gangsters_rack_audit`, `gangsters_business_tests/audit` |
| F | the law: the shopkeeper's telephone, a trial the state can lose, the lawyer and bail, witness pressure, longer sentences; a hideout of our own, a precinct that exists, a courthouse the transfer drives to; men answer for themselves when the law walks over; a dead policeman brings every car | `19aa7a06` `3d7e06af` `58d83420` `be6a4710` | EPIC 26 GAN-245 Done; EPIC 23 GAN-235/237 Done, GAN-236 in progress | `gangsters_police_tests` (46 contracts). **No runtime oracle**: no mission shakes a door, no `complaint`/`court` trace row |
| G | cover first and the ambush; a man takes cover on the side he came from; the player aims it | `6e061257` `574cf858` `e372fb56` | EPIC 28 GAN-255 Done | `soak.sh --cover/--ambush` SETS, `analyze.py --crew`, `--cover-tally`. **No oracle for the side rule** |
| H | the street: mixed arsenal, a braked man stops skating, the crew drives to the job and gets out, one round one flash, the routed crew crosses the furnished street, a gun handed to a named man stays his | `1a9fee35` `3c8ea3f9` `2162e65c` `12d14b22` | — | the brawl/walk/car/moto/roadblock SETS (`soak.sh`), `analyze.py --crew/--verdict/--moto`, `gangsters_crew_audit_tests` — all written for BlockDemo, ported to the core by NIGHT-000 |
| I | the city deal: a courthouse in the kit, every 5 m of glass a shop, no policeman keeps a grocery, smashed glass finds the real frontage, the almanac's block file split | `1ea0fb89` `059a4f79` `3d7e06af` | — | `gangsters_core --count`, `gangsters_storefront_audit`, `gangsters_geography_tests/audit`, `gangsters_core_vacancy_tests` |
| J | the scenario suite and control — touched by A and E through the shared gateway | — | EPICs 7/8 | the nine `gangsters_scenario_*` + `gangsters_control_tests/audit`, `gangsters_presence/fear_tests/audit` |
| K | sound, FX and the book's face: the loud gun pack, fire and smoke from the particle pack, the FX demo, the ledger layout, every sheet line on one middle, the clock keeps its speed | `6acd0134` `ce850057` `755d0da5` `adee2927` `00e37122` `059a4f79` | — | "the scene plays without an error"; pictures for the morning |

## 4. The tickets, in this order

Pure and fast first, the core's own street next, the forced scenarios after that, and the
old driving modes last — car 8/10 and moto 6/10 on 2026-08-23 were DRIVING-domain backlog
and the night must not sink into that before today's work has had its five. The order:
**000, 001, 003, 004, 005, 006, 007, 008, 010, 013, 009, 011, 012** (002 dropped).

### NIGHT-000 — The driver, the branch, the lab's player on the core
**Builds.**
1. Pre-flight (§2): dirty-scene check, `isPaused` clear, baseline run of every pure suite,
   `git status` read, the branch cut, the ledger opened.
2. **The mission lab on the core.** `BlockDemoMission` does not reference
   `BlockDemoBuilder` at all — it finds `DemoCrews` itself (`BlockDemoMission.cs:147`); the
   builder only attaches it and `BlockDemoOutfit` with the knobs
   (`BlockDemoBuilder.cs:253-284`). `CoreDemoBuilder` gains the same public knobs
   (`missionAfter`, `missionPasses`, `missionOnFoot`, `missionWalk`, `missionWalkLegs`,
   `missionLegPatience`, `panicChance`, `missionMoto`, `missionRoadblock`,
   `missionPassesRidden`, `missionMotoAct`, `missionBomb*`, `missionCarBomb*`,
   `outfitLieutenants`, `outfitHoods`, `mixedArms`, `outfitMotorcycle`) and attaches the
   same two components the same way, after `runtimeObject.SetActive(true)`. Plus the two
   police knobs the core hard-codes to zero: `policeCars`, `policeOfficers` (pass-through
   to `runtime.policeCarCount/policeOfficerCount`) **and a station house with a forecourt
   on the mini core** (`CoreAmenityLayout.PickStation` beside `PickCourthouse`; the
   `StationHouse` the police kit already understands), so the cars have stalls to stand in
   and a kerb to undock from. Default `policeCars=0` leaves the scene as it is.
3. **`Tools/play/night.sh <mode> --runs 5 --seed 101 --out DIR`** — `soak.sh`'s modes and
   SETS with `BlockDemoBuilder.` → `CoreDemoBuilder.` and the scene `MiniCoreDemo.unity`
   (`CoverDemo.unity` for `--cover`/`--ambush`, unchanged), each run through
   `unity command gangsters_play --step 0.05 --sets "<SETS>;CoreDemoBuilder.seed=N;CoreDemoBuilder.newSeedEveryPlay=0"`,
   the wait on `summary.json`, the same `analyze.py` verdict flag per mode, the same
   `soak.txt` ledger and exit codes (0 pass / 1 fail / 3 no run). `editor_stop` before each
   run if Play is somehow still on.
4. **The smoke run**, first thing after the port compiles:
   `night.sh --smoke` = `gangsters_play --scene Assets/Scenes/MiniCoreDemo.unity --seconds 120 --step 0.05 --sets "CoreDemoBuilder.seed=1;CoreDemoBuilder.newSeedEveryPlay=0;CoreDemoBuilder.realSecondsPerGameHour=5"`.
   `summary.json` `why:done`, `timesReal` written in the ledger (the core's sim speed is
   unmeasured), `house` rows > 0 (the minds run); then the same run with `policeCars=3`:
   three police `car` rows in the trace, or the forecourt is not done yet.
**Done when.** the smoke run judges; `night.sh --brawl --runs 1` writes a ledger line and a
verdict on the core. Committed on the branch.
**Time.** ~1.5 h. **Fallback.** If the mission port does not judge a brawl by 05:00, the
pure tickets (001–008) still run — they need none of it — and 010 on CoverDemo needs none
of it either; 011/013/009 are written up as blocked on the port.

### NIGHT-001 — The paper underworld (A)
**Check ×5 calls.** `gangsters_house_tests`, `gangsters_relations_tests`,
`gangsters_underworld_tests`, `gangsters_command_tests`, `gangsters_save_tests`,
`gangsters_organization_tests` (which carries Gang, Personnel, Police, Wage, Loyalty,
Notability, Command, Skill, Learning, Personality inside it). Then
`gangsters_underworld_sim --sweep 5 --days 90 --houses 21` (ran in under 30 s at 03:00)
and `python3 Tools/sim/underworld_tally.py` over it.
**Passes when.** every `passed:true`; the sweep has no error and no ownership refusal; the
tally prints. A `negatives` count above zero (a safe under water) is a **note for the
morning**, not a failure — it is balance, not a bug.
**FOCUS.** `Outfit/HouseMind.cs HouseIntent.cs HouseView.cs HouseRelations.cs Engagement.cs
OrderEffects.cs OrderResolution.cs Underworld.cs House.cs Tribute.cs Diplomacy.cs`,
`Personnel/Organization.cs RosterOps.cs` (the heir), `RoadDemo/TerritoryRuntime.Minds.cs
TerritoryRuntime.Paper.cs CrewJobs.cs`, `Tests/UnderworldSim.cs PaperCity.cs`.

### NIGHT-002 — dropped
Save has no UI yet (user: "save nemamo UI, to preskoči"). `gangsters_save_tests` stays as
one line in NIGHT-001, nothing more.

### NIGHT-003 — Wages and the envelope (C)
**Check ×5.** `gangsters_wage_tests`, `gangsters_economy_tests`;
`gangsters_economy_audit --seed N` for N in 1..5.
**FOCUS.** `Outfit/Wages.cs Accounts.cs HireMarket.cs CampaignRunner.cs`,
`Personnel/Career.cs`, `UI/PersonnelAlmanac.Finances.cs`.

### NIGHT-004 — Loyalty follow-up, the bag man, the sheet (D)
**Check ×5.** `gangsters_loyalty_tests`, `gangsters_notability_tests`,
`gangsters_personality_tests`, `gangsters_skill_tests`, `gangsters_learning_tests`;
`gangsters_organization_audit --seed N` for N in 1..5.
**FOCUS.** `Personnel/ReasonFeed.cs Defection.cs Loyalty.cs CollectorChoice.cs RosterOps.cs
Notability.cs`, `Outfit/OpenDoors.cs`, `Gameplay/PersonnelDirector.cs`,
`UI/PersonnelAlmanac.Command.cs PersonnelAlmanac.Organization.cs` (`5d9c2008`, 448 lines,
no oracle but the review).

### NIGHT-005 — The racket on paper (E)
**Check ×5.** `gangsters_round_tests`, `gangsters_rack_tests`,
`gangsters_territory_foundation_tests`, `gangsters_business_tests`;
`gangsters_rack_audit --seed N`, `gangsters_business_audit --seed N` for N in 1..5.
**FOCUS.** `Territory/TerritoryRounds.cs TerritoryCommands.cs TerritoryRacketOrders.cs`,
`RoadDemo/TerritoryRuntime.Collection.cs .Seam.cs .Shakedown.cs`,
`Gameplay/PlayerCommands.cs`, `Business/BusinessDeeds.cs`.

### NIGHT-006 — Scenarios, control, presence, fear (J)
**Check ×5.** the nine `gangsters_scenario_*`, `gangsters_scenario_phase1`,
`gangsters_control_tests`, `gangsters_presence_tests`, `gangsters_fear_tests`;
`gangsters_control_audit`, `gangsters_presence_audit`, `gangsters_fear_audit` at seeds 1..5,
one seed per call.
**Known at HEAD.** `gangsters_scenario_capture_audit` and `_phase1` are red on the two doc
comments (§2 Baseline). Per §8.1 the user either lets the night make the audit skip
comment lines (a two-minute change in `PipelineCommands.cs:1023`) or they are
`PRE-EXISTING`.
**FOCUS.** whatever A and E changed under `Territory/` and `Outfit/`; the scenarios are the
regression net, not the subject.

### NIGHT-007 — The city deal (I)
**Check ×5 seeds.** `gangsters_core --seed N --count 1 --json` for N in 1..5: every deal
clean, every deal holds **one courthouse and at least one station**;
`gangsters_geography_tests`, `gangsters_core_vacancy_tests`; `gangsters_storefront_audit`
and `gangsters_geography_audit` at seeds 1..5: no policeman on a grocery, every 5 m of
glass a door of its own.
**Known at HEAD.** `gangsters_core_vacancy_tests` is red at seed 1987 (§2 Baseline). The
generator is not edited unattended: per §8.1 the user reads the two lines now, or the
ticket is **report only** on that suite.
**FOCUS.** `RoadDemo/CoreDistrict.cs RoadDemoBuilder.cs`, `Editor/SyntyKitExtractor.cs`,
`CityKit/Buildings/building-courthouse.prefab`.

### NIGHT-008 — The law on paper (F, part one)
**Check ×5.** `gangsters_police_tests` (46 contracts: the deed decides the sentence, the
lawyer cuts the days, word against word mostly walks, two eyewitnesses convict, bail comes
back as a man, the hideout is one address, a cop killer never comes clean).
**FOCUS.** `Police/CourtCase.cs PrisonPipeline.cs`, `Personnel/Lawyer.cs Sentencing.cs
Incident.cs`, `Territory/TerritoryHideout.cs`, `RoadDemo/LawDesk.cs LawWire.cs
WitnessWatch.cs PoliceDispatch.Complaint.cs PoliceDispatch.Wanted.cs`.

### NIGHT-010 — Cover first and the ambush (G) — the side rule gets its row
On `CoverDemo.unity` (§8.7). **Builds** (~30 min): the `cover` trace row gains `side` (the
flank's side relative to the man's approach) and `CrewAudit` a `wrongside` fault (he got
behind it on the far side from where he came); `analyze.py --crew` counts it.
**Check ×5 seeds.** `night.sh --cover --runs 5`, `night.sh --ambush --runs 5`
(`CoverDemoBuilder.layoutSeed` 102..106), `gangsters_crew_audit_tests`.
**Passes when.** 5/5 each, `wrongside` 0, `openfire` 0, the ambush's five (`noambush nolurk
seenfirst openambush nospring`) 0; the cover-first share over the ten runs ≥ 80 % (85 % was
the last reading over 30).
**FOCUS.** `RoadDemo/DemoCrews.Cover.cs DemoCrews.Combat.cs CrewOverlay.CoverAim.cs
CrewAudit.cs`, `BlockDemo/BlockDemoMission.Ambush.cs`, `Editor/AmbushProbe.cs`.

### NIGHT-013 — The forced scenarios on the mini core (the levers pulled)
The user's second ask: the night does not wait for a scenario to happen by chance, it sets
`MiniCoreDemo` up so it **must** happen, and judges that it did.

**Levers that exist** (`-hSet` on `CoreDemoBuilder`, the one component in the scene):
`seed`, `newSeedEveryPlay`, `rivalCrews` 0..20, `rivalHoods`, `police`, `policeBeatPairs`,
`startHour`, `realSecondsPerGameHour`, `carCount`, `pedestrianCount`, `quarterBudget`; after
NIGHT-000 the mission/outfit knobs and `policeCars`/`policeOfficers`. **Levers the ticket
adds** (builder fields → the runtime, since `RoadDemoBuilder` and `TerritoryRuntime` are
made at runtime and cannot be `-hSet`): `ownerTraitOverride` (every owner `Connected`, or
`Frightened`; `TerritoryEconomy.cs:13,74`), `playerSafeAtStart`, `mindThinkEveryHours`
(→ `HouseMindConfig.Default.ThinkEveryHours`), `playerIdle` (house 0 files nothing).
**Row the ticket adds:** `turf` (a block's leader or control state changed: block, from,
to, hour). **The reader:** `analyze.py --core` — `house` rows by gang and tier, `turf`
changes, calls/arrests/verdicts where the rows exist, plus the ordinary fault and exception
rules.

Five scenarios, five seeds each, the clock at `realSecondsPerGameHour=5` (a game day is
120 sim-seconds; the smoke run says what that costs in real time on the core):

| # | scenario | levers | passes when (all five seeds) |
|---|---|---|---|
| S3 | **does the AI spread like we do** | `playerIdle`, `rivalCrews=20`, 7 game days | `house` rows show tier 7 (expand) fired by ≥ 1 house; ≥ 1 `turf` row moves a block's leader to a rival; every house thinks (rows for all 20 gangs); no exception. A safe under water is a note |
| S4 | **no police at all** | `police=false`, `rivalCrews=20`, 7 game days | the ladder reaches war for ≥ 1 pair (a `house` row with a war intent and both sides' `shot` rows); the crew audit is clean; no exception |
| S5 | **the broke player** | `playerSafeAtStart=0`, `outfitLieutenants=2`, `outfitHoods=3`, 3 game days | short envelopes on the day tick; ≥ 1 reason-feed line about pay; ≥ 1 man leaves through `OpenDoors`; the safe never reads below zero; no exception |
| S2 | **a ton of police** | `policeCars=12`, `policeOfficers=12`, `policeBeatPairs=12`, `rivalCrews=20`, `rivalHoods=4`, `missionOnFoot` | no car stands 90 s; no `belt`; a dead policeman's swarm arrives and dissolves (cars back on patrol within a game hour); arrests complete; no exception. The forecourt is NIGHT-000's |
| S1 | **every owner rings** | `ownerTraitOverride=Connected`, `policeCars=6`, `missionShakedown` (NIGHT-009), `rivalCrews=20` | every shakedown of a door produces a `complaint` inside the window; a car comes; the man is taken or fights; the transfer reaches the courthouse; a verdict lands; no gridlock, no exception. **Needs NIGHT-009** |

S3, S4, S5 run first (they need only the levers and the `turf` row, ~1 h); S2 next (the
forecourt from NIGHT-000); S1 after NIGHT-009. Nothing here is "blocked": what the mini
core lacks for a scenario, the night adds to the city's rules.
**FOCUS.** `RoadDemo/CoreDemoBuilder.cs TerritoryRuntime.Minds.cs`, `Territory/TerritoryEconomy.cs`,
`Outfit/HouseMind.cs`, `Tools/play/analyze.py`.

### NIGHT-009 — The law on the street (F, part two) — builds its oracle first
Nothing headless reaches the telephone today. On the core the shakedown has somewhere to
go: `TerritoryRuntime` exists there (it does not on BlockDemo) and
`Execute(ShakeDownBlockCommand)` is on it (`TerritoryRuntime.Shakedown.cs:28`).
**Builds** (~2 h): `missionShakedown` in `BlockDemoMission` — after `startAfter` the lab
files the same `ShakeDownBlockCommand` the right-click files, for the block our crew stands
on; trace rows `complaint` (who rang, from which door, how long after the lean),
`statement`, `arrest`, `court` (the transfer reached the courthouse; the verdict);
`analyze.py --law`: a connected owner rings inside the window, a car comes, the man is
taken or fights, the transfer arrives, a verdict lands; faults `noring` (connected owner,
no call), `nocar` (call, no car in 120 s), `lostman` (taken, never reached the
courthouse), `nocourt`. `night.sh --law`.
**Check ×5 seeds.** `night.sh --law --runs 5` with `ownerTraitOverride=Connected`.
**Passes when.** 5/5 with no fault and no exception.
**Fallback.** Per §8.2 this ticket runs tonight or is the morning's. If tonight and the
mode is not judging inside its 120 min, the ticket is written up as open with what was
built; 011 goes ahead.

### NIGHT-011 — The street on the core (H): brawl, walk, then the old driving modes
**Check ×5 seeds each, in this order.** `night.sh --brawl --runs 5`, `--walk`, `--car`,
`--roadblock`, `--moto` — all on `MiniCoreDemo` through the ported knobs. Brawl and walk
carry today's changes (the brake, the mixed arsenal, the boarding, the flash);
car/roadblock/moto carry the August backlog **and** a scene they have never run on, so
their first failures may be the port's, not the driving's — `--why` decides.
**Passes when.** 5/5 per mode. The rule of the soak stands — every failure it finds is
fixed, never explained away — inside §1's two attempts and 120 minutes; whatever is still
red is written into the ticket with `--why` output and left honestly open.
**FOCUS.** `RoadDemo/CrewWalker.cs CrewWalker.Steer.cs DemoCrews.Boarding.cs
DemoCrews.Combat.cs WalkRoute.cs WalkObstacles.cs`.

### NIGHT-012 — Scenes, pictures, and the morning report (K)
**Check ×5 scenes.** `gangsters_play` 90 s at step 0.05 with `--shot 40` on
`MiniCoreDemo`, `CoverDemo`, `Ledger`, `MotoDemo`, and the FX demo scene: `summary.json`
`errors:0 exceptions:0`, no `error CS`, no missing-reference line in `unity.log`; the five
pictures copied to `night-2026-09-03/pictures/` (the harness's `--shot` writes under the
run's out dir, outside `Assets`). Then the report: `night-2026-09-03/REPORT.md` — per
ticket: passes, fixes (commit hashes), review rounds and what was acted on, what is open and
why, the `PRE-EXISTING` list; the branch name and the merge command; the pictures. The epic
and every finished ticket go to Done in Linear; open ones stay open with the report pasted
in. The editor is left open on `MiniCoreDemo`, stopped, no dirty scene.

## 5. The driver

An outer shell loop, so every iteration starts with a fresh context and the ledger is the
only memory by construction (the ralph-loop Stop hook stops silently on an empty last
message, a jq hiccup or max iterations, and nothing restarts it). It is checked in as
**`Tools/play/night-loop.sh`** and the user starts it from a terminal before leaving:

    Tools/play/night-loop.sh            # runs until the ledger says NIGHT DONE, or 400 iterations

Each iteration is one `claude -p ... --dangerously-skip-permissions` call with the prompt
below, its output kept under `night-2026-09-03/loop/iter-NNN.log`; five seconds between
iterations, sixty after one that exits non-zero.

Each `claude -p` run does one ticket-step (a pass, a fix, a review, a commit) and exits;
the loop calls the next. If the night is instead run inside one interactive session, the
ralph loop is the fallback (`--max-iterations 300`, the same prompt), with the rule that
every turn ends with one line of text.

## 6. Budget

| what | one run | ×5 | note |
|---|---|---|---|
| all 34 pure suites through the port | ~2 min | ~10 min | measured 03:00 |
| a CoverDemo run (480 s sim) via `gangsters_play` | ~30 s | ~3 min | no cold start in the open editor |
| a MiniCoreDemo brawl/car/roadblock (480 s sim) | **unmeasured** | | the smoke run's `timesReal` × 480 |
| a MiniCoreDemo walk (1500 s) / moto (900 s) | unmeasured | | |
| a MiniCoreDemo scenario (7 game days at 5 s/hour = 840 s sim) | unmeasured | | |
| a Codex adversarial review | 5–15 min (guess) | ×~11 | measure the first; ~2 h over the night |
| NIGHT-000 (the port + `night.sh` + smoke) | | ~1.5 h | |
| the oracles: 010 (side row), 013 levers + `turf` + `--core` | | ~1.5 h | |
| 009 (shakedown mission, law rows, `--law`) | | ~2 h | only if §8.2 says tonight |

From 03:30 to 09:00 is five and a half hours. The pure tickets (001–008) are done inside the
first hour if nothing fails; 000 + 010 + 013 (S3–S5) are the next three; 011 and 009 are what
the night runs out on. What is open in the morning is the old backlog and the newest oracle,
never today's paper work.

## 7. What the night cannot do

* **See.** "Vizuelno potvrdim" is the morning; the night leaves pictures.
* **Click.** The player's cover aim, TAKE THE BAG, the ledger pages, the CHAIN OF COMMAND
  sheet — only `gangsters_ambush_probe --order` reaches one of them. Out; the review reads
  the sheet's code instead.
* **Answer a dialog.** A dirty-scene prompt, a licence prompt, a crash reporter, a
  permission prompt: any of these ends the night silently. Hence the dirty-scene check, the
  permission flag, and the editor never being exited by the night on a dirty scene.
* **Know another session is typing.** Hence one session.
* **Judge a design.** A Codex "consider" is a note, not a change.

## 8. The rulings (03:20)

1. **The three red suites at HEAD are the night's to fix** ("nek ih sredi taj ko bude radio
   epic"). The capture audit skips `//` and `///` lines (NIGHT-006). The vacancy failure at
   seed 1987 is settled inside NIGHT-007 under the two-attempt cap: the test if the deal is
   right by `Docs/city-districts-plan.md` and the courthouse simply took the parcel, the
   generator (`CoreAmenityLayout`) if the deal is wrong — and the ledger says which and why.
2. **NIGHT-009 and S1 run tonight, on the mini core scene.**
3. **Everything in the live editor.** No batch Unity at any point; `run.sh`/`soak.sh` are
   not called. `gangsters_play` on the open editor is the only way a scene is played.
4. **The outer `claude -p` loop** (§5) is the driver.
5. **The caps stand:** two fix attempts, 60/120 min per ticket, confidence ≥ 0.8, no
   documented number changed on a review's word.
6. Five passes; car/roadblock/moto last; the August soak directories stay; when the hours
   run out the night writes `REPORT.md` and stops.
7. `CoverDemo.unity` stays as EPIC 28's bench for NIGHT-010 (assumed; the user named only
   BlockDemo and CoreDemo). NIGHT-013's five scenarios as listed.
8. The smoke run is the night's first act, not run while the user has the editor.
9. **The night may change the mini core's city rules** — police cars, a station forecourt,
   more pedestrians, whatever a scenario needs — as knobs on the shared builder with
   defaults that leave the scene as it is. Only `MiniCoreDemo.unity` is played.
