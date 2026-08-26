# Skills into the game, and how a man improves

Status: **BUILT** (2026-08-26). This document is now the design *and* the map of
where it lives. What shipped differs from the first draft in a few places; those
are marked **[as built]**.

**The one structural decision worth knowing before reading any of it:** the
campaign's rules live in `Outfit/CampaignRunner.cs`, which is pure C# with no
UnityEngine in it, and `Gameplay/OutfitDirector` is a thin scene wrapper holding
only the four things a scene can answer — what time it is, where headquarters
stands, what to log, and when the ledger repaints. That split is not tidiness:
a realtime game whose rules are locked inside a `MonoBehaviour` can only be
checked by watching it run, and watching is the slowest and least certain way to
find out whether the books add up. Because the rules are pure, the suite plays a
scripted 28-day campaign twice and asserts the safe, every man's practice and
every record line — see `AScriptedMonthIsRepeatable`.

| Stage | Where it lives |
|---|---|
| 1. Day tick, no commit | `Outfit/Campaign.cs` (Day is the counter), `Ambient/IDayClock.cs` + `DayClock` registry, `Outfit/CampaignRunner.DayTick`, driven by `Gameplay/OutfitDirector.Update` |
| 2. Job resolution | `Outfit/OrderResolution.cs`, `Outfit/Orders.cs` (`Job`, `OrderBook`, hours), driven by `CampaignRunner.AdvanceHours` |
| 3. Practice core | `Personnel/Character.cs` (practice array), `Personnel/Practice.cs` |
| 4. Order effects | `OrderResolution` (yield band, cost discounts, misfire, heat), `RosterOps.Hospitalize`/`Discharge` |
| 5. Street wiring | `RoadDemo/CrewSkill.cs`, `DemoCrews.Resolve` shot roll, `CrewWalker.FirearmsHalfSteps` |
| 6. Dispatch | `RoadDemo/CrewJobs.cs`, ticked from `DemoCrews.Update` |
| 7. Recruit re-tune | `Personnel/RosterSeeder.Recruit`, `OrderResolution.BringHimIn` |
| 8. Ledger UI | `UI/PersonnelAlmanac.Orders.cs`, `UI/PersonnelAlmanac.Personnel.cs` (practice pips), `UI/LedgerText.cs` |

Verified by `LedgerTests` and `PersonnelTests`, both green headless. Every new
assertion was mutation-checked rather than merely observed passing — a suite
that has never failed proves only that it runs. Five mutations, five caught:
bending the practice cost curve, bending the odds slope, shifting payday by a
day, silencing a standing job's takings, and swapping the seeded roll for
`new System.Random()` each fail a named assertion and nothing else.

> **DESIGN DECISION (2026-08-26): the game is REALTIME.** The weekly turn is
> abandoned — in-game days pass normally on the city clock, and nothing below
> hangs on a "commit the week" boundary. The existing `OutfitDirector.CommitWeek`,
> `WeekPlan` and the man-week arithmetic in `OrderMath` are the old shape: they get
> reshaped into realtime dispatch plus a **day tick**, not extended.

## What already exists (the seams this plan plugs into)

| Seam | File | State today |
|---|---|---|
| The 11 attributes, half-step ints 2–10 | `Personnel/CharacterAttribute.cs` | Done. Doc comment already promises an improvement system that bumps a stat by +1 half-step. |
| Order table: every order names a `PrimaryAttribute` + floor | `Outfit/Orders.cs` (`OrderTable`) | Done, but only used to WARN on the job card ("warn-but-permit"). |
| `CrewKit.BestAt(roster, crew, attr)` | `Outfit/Orders.cs` | Done, ready to be the resolution's stat read. |
| Week commit | `Gameplay/OutfitDirector.CommitWeek` | Stubs every reached order `Completed`. **To be dissolved** into realtime dispatch + a day tick. |
| The city clock | `RoadDemo/DemoClock`, pedestrian routines | Days already pass; commutes already run on it. The day tick attaches here. |
| Street shot roll | `RoadDemo/DemoCrews.Resolve` (~line 3380) | `p = weapon.Accuracy × falloff`, flat `+0.08` if lieutenant. Firearms stat NOT in the roll. |
| Street violence machinery | `DemoCrews.DriveBy/.Bomb/.RunDown`, `CrewBomb` | Drive-bys, bombs, run-downs already play out live — violence orders can RESOLVE THROUGH THE SIM instead of an abstract roll. |
| Arms dealing | `RosterOps.NormalizeArms` | Lieutenant's Organization already gates correct gun/wheel deals. |
| Wages | `Outfit/Wages.cs` | Hood wage = base + 5 per half-step above minimum, derived at read. **Level-ups auto-raise payroll — the built-in cost of training.** |
| Promotion gate | `RosterOps.CheckPromote` | Warns below 3★ Intelligence/Organization. |
| Street ↔ ledger bridge | `RoadDemo/CrewWalker.CharacterId` | Every street figure knows his roster id — practice earned on the street lands on the book. |

## The realtime frame

Three clocks, and which bookkeeping runs on each:

1. **Continuous (frames)** — the street sim: fights, driving, travel. Men
   actually go to the job; travel is real time on real streets, not a deducted
   travel fraction. `OrderMath.TravelFraction` and the man-week capacity
   arithmetic retire; what limits a crew is that its men are genuinely
   somewhere, doing something, for as long as it takes.
2. **Job completion (event)** — the moment a job finishes (arrived + worked its
   duration, or the street fight ends): outcome resolves, money moves, practice
   is awarded, the record row is written.
3. **Day tick (clock midnight)** — the bookkeeping boundary the week commit
   used to be: practice converts to half-steps, protection income lands, heat
   cools, and every 7th day is **payday** (wages still a weekly envelope —
   period flavor — but time never stops for it).

Orders are issued from the ledger at any moment and go to a lieutenant's queue;
his crew works them one at a time, in queue order, in the live city. The ledger
stays open over the running game (the Orders panel already sits over the live
map) — issuing an order is a realtime act, not a turn submission.

## Part 1 — attributes drive outcomes

### 1a. Job resolution

New pure class `Outfit/OrderResolution.cs`, called at the job-completion event
(clock #2), not at any commit.

Two resolution paths:

- **Street-resolved** (Kill, Bomb, Torch, Assault, SmashUp, Raid, Kidnap,
  Ambush): the sim IS the dice. The crew travels there and the existing
  machinery plays it out — the outcome is whatever actually happened (target
  dead, shop wrecked, crew driven off). Attributes bite through the sim
  modifiers (1c), not through an abstract roll. `OrderResolution` only
  translates the street result into a record row + consequences.
- **Roll-resolved** (everything with no street scene: Extort, CollectProtection,
  BuyPremises, SetUpBusiness, RunBusiness, Audit, Recruit, Bribe, EmployPolice,
  Donate, Explore, Intimidate): when the crew has been at the door for the
  job's duration:

      stat  = CrewKit.BestAt(roster, crew, spec.PrimaryAttribute)   // half-steps 2..10
      p     = clamp(0.35 + 0.10 × (stat − spec.PrimaryFloorHalfSteps), 0.05, 0.95)
      roll with System.Random seeded MixSeed(citySeed, SeedOffsets.Orders, day, order.Id)

  At the floor exactly: 35 %. One full star over: 55 %. Two over: 75 %. Capped
  95 %, floored 5 %. Floor-0 orders resolve against an implicit floor of 4 (2★).

Job durations replace point costs **[as built]**: one `OrderSpec.HoursPerTarget`
covers both modes (a point order has one target), and `OrderMath.WorkHours`
divides by the headcount. Fewer men = it simply takes longer, in real days — no
"undermanned" flag, the calendar is the cost. Travel is
`OrderMath.TravelHours`: 400 m/hour on foot, 2,000 m/hour by car, the car figure
scaled 0.90–1.15 by the crew's best Driving, floored at 15 minutes and capped at
72 hours.

**[as built]** A third resolution joined the two: `JobResolution.Standing` for
Patrol, Guard, Ambush and RunBusiness. A watch is never *finished* — it holds
its men and pays practice a day at a time until it is called off. This is what
made the plan's "a Patrol/Guard day served" rule fall out naturally instead of
needing a special case.

### 1b. What each attribute buys (effects table)

| Attribute | Where it bites |
|---|---|
| **Intimidation** | Extort/Intimidate success (primary); protection yield per block scales with best Intimidation on the job (×0.8 at 2★ … ×1.3 at 5★). |
| **Business** | RunBusiness income multiplier (same ×0.8–×1.3 band); SetUpBusiness build cost −5 % per star over floor. |
| **Intelligence** | Recruit: candidate's rolls improve with recruiter's Intelligence (2d); Bribe/EmployPolice price −8 % per star over floor; Audit catches skim only if Intelligence beats the skimmer's Business. |
| **Firearms** | Street shot roll (1c) — carries every street-resolved gun job. |
| **Fists** | Assault/SmashUp/Kidnap play out in the melee path; Fists shifts knock-down odds (1c). |
| **Knives** | Quiet-kill: a Kill assigned to a man with (Stealth+Knives)/2 ≥ 7 is done off-screen with **no shot heard, no heat** — the one violence order that may roll instead of playing out, and only for such a specialist. |
| **Arson / Explosives** | Torch/Bomb: fuse/spread quality in the street scene; a botcher (stat under floor) risks a misfire sub-roll — 25 % → one assigned man Hospitalized. |
| **Driving** | Travel is real now, so Driving buys real speed: driver's stat scales cruise speed and corner confidence of a crew car on a job (×0.9–×1.15), and keeps its drive-by role-ranking. |
| **Organization** | Already gates arms dealing; adds queue depth: a lieutenant works without penalty up to `Organization` half-steps of queued orders — each order past that takes −0.10 on its roll. **[as built]** The depth is frozen onto the job when it *starts* (`Job.BookDepth`), not read at resolution: the penalty is for the attention he had to spare while the work was being done, and a book that empties afterwards must not retroactively improve a job already run. |
| **Stealth** | Explore coverage ×(0.8–1.3); any violence job whose crew's best Stealth ≥ 7 halves the heat it generates. |

Heat is a future ledger; resolution just EMITS `heatGenerated` per record so the
police layer can consume it when it lands.

### 1c. Street fight modifiers (live sim)

- `DemoCrews.Resolve`: replace the flat lieutenant `+0.08` with the shooter's
  stat: `p = stats.Accuracy × falloff × (0.70 + 0.06 × firearmsHalfSteps)`.
  Range ×0.82 at 1★ … ×1.30 at 5★. Rivals (negative CharacterId) default to 6.
  Clamp stays 0.04–0.98.
- Melee path: Fists difference shifts knock-down chance the same
  multiplicative way.
- Crew car on a job: Driving scales speed as in 1b.

## Part 2 — the level-up system: practice, not XP

No generic experience pool. A man gets better at exactly the thing he was sent
to do. Matches the ledger fiction and keeps every number attribute-local.

### 2a. Data model

`Character` gains one parallel array:

    readonly int[] practice = new int[AttributeScale.Count];   // points toward the NEXT half-step
    public int GetPractice(CharacterAttribute a);
    public void AddPractice(CharacterAttribute a, int points);  // accumulation only

Serializes as a flat list, same as `halfSteps`. Pure, headless-testable.

### 2b. Earning practice (at the job-completion event, and live)

| Source | Who | Points |
|---|---|---|
| Job finished, succeeded | every man on it, in the order's PrimaryAttribute | 3 |
| Job finished, failed | same men, same attribute | 1 (you learn from a botch, less) |
| Street: a shot that HITS | the shooter, Firearms | 1 (cap 5 per day per man — a long firefight is one lesson, not ten) |
| Street: melee knock-down | the winner, Fists | 1 (same cap) |
| A Patrol/Guard day served | assigned men, Firearms | 1 |
| Idle in the pool | nobody | 0 — men rust on the bench; that is the incentive to work the roster |

Street points flow through `PersonnelDirector` via `CrewWalker.CharacterId`
(negative rival ids ignored — they are on nobody's books). **[as built]**
`RoadDemo/CrewSkill` owns both halves of that wire: `Aim(halfSteps)` is the
multiplier the shot roll applies, and `Landed(id, attribute)` banks the lesson
with the daily cap. The shot roll reads a *cached* `CrewWalker.FirearmsHalfSteps`
rather than looking the man up per round — it cannot go stale, because a man only
improves at the day tick and that bumps the personnel version `DemoCrews` re-deals
on.

### 2c. Converting practice to half-steps — at the day tick

At clock midnight:

    cost(next) = 2 × next        // to reach half-step n costs 2n points
    while practice[a] >= cost(halfSteps[a] + 1) and halfSteps[a] < Max:
        practice[a] -= cost; halfSteps[a] += 1; record the rise

- 1★→1.5★ costs 6 points (a few good jobs); 4.5★→5★ costs 20 (a career).
- Deterministic — integer counters, no roll. The headless suite asserts an
  exact roster after N scripted days.
- Every rise lands in a `List<ImprovementRecord>` (name, attribute, new stars)
  the ledger and `News/Headline` can print: "T. Marchetti has become a better
  arsonist."
- Wage rises the same frame automatically (`Wages` derives at read); the bigger
  bill lands at the next payday (every 7th day). Training men IS raising
  payroll — the tension is free.

### 2d. Recruit quality re-tune

`RosterSeeder` currently rolls recruits uniform 2..10. With growth in the game,
raw recruits start low and are BUILT:

- Street recruit (Recruit order): rolls 2..6, plus recruiter's Intelligence
  half-steps over floor added as bonus rolls on random attributes.
- The founding six keep their generous rolls — the campaign start is the
  campaign start.

### 2e. Explicitly NOT in scope

- No stat decay, no training-camp building, no respec. One system, one
  direction, half-steps only.
- No per-man XP levels/titles. Rank stays Hood/Lieutenant; stars ARE the level.
- No pause-and-plan mode. The ledger opens over the running city as it already
  does; if a pause ever comes it is a UX affordance, not a turn structure.

## Determinism rules (non-negotiable, per the sim's standing traps)

- One `System.Random` per resolution, seeded `MixSeed(citySeed,
  SeedOffsets.Orders, day, order.Id)` — never `UnityEngine.Random` in the pure
  layer, never streams created in a loop from correlated seeds.
- New `SeedOffsets.Orders = 29_000` (Personnel took 28_000).
- Practice conversion has no randomness at all.
- Day index comes from the game clock, never from wall time.

## Ledger UI touches (last, smallest)

- Character card: a thin practice pip under each star row — `practice/cost`
  fraction as a short ink tick. No layout change; `PersonnelAlmanac` repaints
  on `Version` as it already does.
- Orders page: the plan/record split stays, but the record grows in realtime as
  jobs finish — rows appear as they happen, with the improvement lines under
  the table ("Rises today: …").
- Job card: the warn-but-permit line also shows the computed chance for
  roll-resolved jobs ("about 3 in 4") and the travel estimate in hours for all.

## Build order (each stage compiles, tests, and stands alone)

1. **Day tick + job clock** — a `CampaignClock` seam on DemoClock's midnight;
   `CommitWeek` dissolves: payday every 7th day, records written at job
   completion. (This stage is the realtime conversion of the existing stubs.)
2. **`OrderResolution.cs`** — roll-resolved jobs with seeded rolls; `Failed`
   rows appear in the book. Headless tests: exact outcomes for scripted days at
   a known seed.
3. **Practice core** — `Character.practice`, `AddPractice`, conversion at the
   day tick, `ImprovementRecord`. Tests: cost curve, cap, wage follow-through.
4. **Order effects** — per-category income/cost multipliers (1b), misfire
   hospitalization, quiet-kill rule, Organization queue penalty. Tests per rule.
5. **Street wiring** — Firearms in the shot roll, Driving on the job car,
   hit-practice feed with the daily cap, rival default stat. Verified via
   `gangsters_play` + drive-by soak (`analyze --crew` stays clean).
6. **Dispatch** — crews physically travel to roll-resolved jobs too (walk/ride
   there, stand the duration, come home): the city shows the outfit working.
7. **Recruit re-tune** — low rolls + recruiter Intelligence bonus.
8. **Ledger UI** — pips, rises line, chance + travel estimate on the job card.

Stages 2–4 are pure C# under `Assets/Scripts/Outfit|Personnel` — reviewable and
testable headless before the editor is ever asked to play a frame.
