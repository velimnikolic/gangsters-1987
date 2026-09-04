# The Answer at the Door — design brief (EPIC 34)

> Law & order track, part 8. Designed with the user on 2026-09-03 from a defect seen in
> MiniCoreDemo and revised the same day against the contrarian's review. Builds on EPIC 17
> (GAN-216 confrontation & surrender), EPIC 19 (GAN-220 the swarm), EPIC 20 (GAN-222 flee,
> wanted & the hideout), EPIC 21 (GAN-226 police roster), EPIC 26 (GAN-245 complaint, trial &
> lawyer) and EPIC 33 (GAN-302 the law sheet). The Linear epic mirrors this file.

## 0. The defect

Nowak's Barbershop rang the precinct. A beat officer walked to the door, drew, put the
question. The seeded roll (`SurrenderRoll.Fights`) said the crew would not go quietly, and
`PoliceDispatch.Refused()` ran. Then nothing:

* `Sic(crew, PoliceOnTheScene())` targets only a SQUAD (`PoliceDispatch.Arrest.cs` ~606). The
  beat officer is a `PoliceFootPatrol : PedestrianAgent`, not a `DemoCrews.Unit`; he has no
  `TakeHit` at all. Nobody can fire at a beat officer today, and he cannot fire back.
* `Send(first:false)` routes cars to `StreetAlarm.Incident` — the last SHOOTING, or the origin
  when there was none — not to the shop door (`PoliceDispatch.cs` ~283).
* An arriving squad never asks again: `LookForACollar` needs `QuietFor < ArrestWindow` and
  `GuiltyNear`, both tied to a shooting. It goes `Securing`, tapes off, leaves.
* Nobody is marked wanted. `WantedLevels.Mark` has one street caller (`PoliceDispatch.Swarm.cs`
  ~196, `CopKiller`). `WantedLevels.Fled` is never applied on the street.
* The complaint stage sees `MenRefused`, prints a wire line, `Close(call)` releases the officer
  back to his round (`PoliceDispatch.Complaint.cs` ~324).

Net cost of refusing the law today: 25 heat (gone in ~45 s), one open case, one paper line.

## 1. Rulings (the user's, 2026-09-03)

1. Three answers at the door, by the men's nature: **QUIET / RUN / FIGHT**. A refusing crew
   never just stands there.
2. Beat officers are `DemoCrews.Unit`s **always** — so a right-click can order them killed for
   no reason at all, and so a psychopath can fire on one.
3. Both officers of a beat pair fight. One does not stand and watch.
4. RUN never raises the swarm. Shots at an officer raise it — **the whole city**, exactly as a
   dead officer does today. A dead officer keeps raising it.
5. A unit of the law that SEES a running man goes to that location.
6. QUIET: the crew is held with hands up until a car comes; the men board; the car drives to
   the station. The officers who came on foot stay on foot and go back to the beat; the car's
   men take the prisoners over.
7. **Booking happens at the station**, when the man is walked in through the door — not on the
   pavement, not in the car. The ride and the station forecourt are a rescue window: the
   player may wait in front of the station and free him as he gets out of the car.
8. Everything lands on the case file and is weighed at the trial: the refusal, the flight, the
   shots at an officer.
9. From the moment a man is in the car the player cannot command him, and he loses every
   weapon he carried. He stays visible on the 3D map with his HUD, a right-click still focuses
   him, and he stays in the chain of command. A man sprung later comes back unarmed.

## 2. What exists and is reused

| Thing | Where | Used for |
| -- | -- | -- |
| The collar | `RoadDemo/PoliceDispatch.Arrest.cs` — `Collar {None, WalkingUp, Asking, Taking}`, `TryComplaintCollar`, `LookForACollar`, `Refused`, `Drop`, `ArrestOff`, `FightOdds`, `Banner`, `PlayerSaysFight` | rewritten around the three answers; ONE `_collar` field today |
| The roll | `Scripts/Police/SurrenderRoll.cs` — `FightChance` (0.5 courage + 0.3 temper − 0.2 loyalty, floor 0.05, ceiling 0.95), `Fights`, `StreamFor(citySeed, crewKey, incident)`, `Leaning` | gains the second roll; stays pure and seeded |
| The crew's side | `RoadDemo/DemoCrews.Arrest.cs` — `GiveUp`, `LetGo`, `TakeIn(unit, deed, pipeline, file)`, `HeldDays` | `TakeIn` moves to the station door |
| Flight | `DemoCrews.OrderFlee(unit, from)` (FleeDistance 70 m), `Unit.Fleeing/FledAt/SeenByLawAt`, `EndFlight` | RUN |
| The chase | `PoliceDispatch.Wanted.cs` — `TickWanted`, `LawWithin`, `ChaseOnSight`, `_chaseAgainAt`, `OurNearestDoor`, the hideout | RUN's pursuit; needs the `Fleeing` skip and the "nearest AVAILABLE unit" lookup |
| The swarm | `PoliceDispatch.Swarm.cs` — `RaiseSwarm(where)`, `SendSwarm` (8 cars, `anyDistance`), `_hunted`, `StandDown` (marks `CopKiller`) | FIGHT; `StandDown` must mark by grade |
| Police units | `PoliceDispatch.SpawnSquad(at, facing, count, aboardOf)` → `DemoCrews.AddRival(PoliceFaction, …)`, `Unit.IsPolice`, `OrderAttack(Unit)` stamps `PoliceFightOrderedAt` | the beat pair becomes one of these |
| Fire and death | `DemoCrews.Combat.cs` — `TakeHit` (~1399), the death channel keyed on `Faction == PoliceFaction` (~1425), `ProvokedAt`/`FightBack` (~573), and the rule "the law is not answered here" (`!unit.IsPolice`, ~571) | free for a unit; the beat brain must Sic its own unit |
| The beat | `RoadDemo/PoliceFootPatrol.cs` (1201 lines) — modes Inside/WalkOut/Patrolling/Returning/Homing/WalkIn/Responding/OnScene/Arresting/Ritual/Doorway/SceneCover; `Challenge`, `EndChallenge`, `Release`, `BeginDoorway/EndDoorway`, `SetBeat`, the wing | replaced by the beat brain; 45 references across 12 files |
| Doors | `RoadDemo/DoorBeat.cs` (`VisitBusiness(CrewWalker…)`, `Active`), `RoadDemo/CrewQuarters.cs` (`Station(unit, door, role)`, `Inside(unit)`) | the statement visit and the station walk-in on the unit model — car squads already use `VisitBusiness` |
| The force | `RoadDemo/PoliceForce.cs` — `Precinct` (`Cars: List<PolicePatrolCar>`, `Leads: List<PoliceFootPatrol>`), `MakeCar`, replacement day, `_losing`, the Convoy (`PrisonLeg`, `Load`, `TransferPatience` 300 s, wreck → `FreedFromTransfer`) | precinct loss and replacement of a dead beat; the convoy is the model for the custody drive |
| The city's car | `RoadDemo/PolicePatrolCar.cs : DemoVehicle : RoadCar` — `RouteTo(scene, standOff)`, `Release()` → `Returning`, `BeginDock` (~419), `BackOnTheRound`, `StandInTheYard`; **no seats, no `Aboard`, no `SeatOf`** | the custody drive; bodies ride through `CarOccupant.Seat` |
| Bodies in cars | `RoadDemo/CarOccupant.cs` — `Crew(car, people, sitLoop)`, `Seat(car, prefab, sitLoop, seatLocal)`, `CarBody.MeasureSeats` | the prisoners' seats in a `DemoVehicle` |
| CrewDemo's cruiser | `PoliceCruiser` (a `CrewCar`; `PoliceDispatch.cs` ~785, built in `CrewDemoBuilder`) | keeps working; it is not the city's car |
| The deed | `Scripts/Personnel/Sentencing.cs` — `Deed {Affray, Murder, CopKilling, Extortion, WitnessTampering…}` (appended values keep serialized meaning), `Days(...)`, `BandLow/BandHigh`, `ChargeFor`, `Bail`, `EscapeSurcharge` 4, `DaysToCourt` 1, `ExtraCountDays` | two new deeds |
| The case | `Scripts/Police/CourtCase.cs` — `Defendants`, `Witnesses`, `Counts`, `Verdicts`, `Status`, `Verdict.BaseFor(deed)`; `PrisonPipeline` — `Book`, `ReBook`, `AttachOpenComplaints`, `Worse`, `_everEscaped`, `Freed`, `BackToTheCells` | the answer on the file |
| Wanted | `Scripts/Police/WantedLevels.cs` — `Fled` 1 (3 days), `FreedFromTransfer` 2 (7 days), `CopKiller` 3 (never); `Mark` never downgrades and clamps at `CopKiller`; `Word` | a fourth grade |
| The sheet and the paper | `Scripts/Police/LawSheet.cs`, `RoadDemo/LawWire.cs` (`RefusedTheOfficer`), `UI/PersonnelAlmanac.Law.cs` | the answer printed |
| Save | `Save/CampaignSave.cs`, `PrisonSnapshot` (deeds as int; `CourtCaseDto` explicit fields, version `VersionBeforeDocket`) | the answer persisted on `Prisoner`, not a new list |
| The night watch | `Tools/play/night.sh` (`--core-s1..s4`), `analyze.py`; GAN-291 NIGHT-009 (Backlog) is the `arrest`/`court` trace rows | ANSW-008 needs GAN-291 first |

## 3. The model

### 3.1 The beat is a crew (ANSW-001)

A beat pair is one police `DemoCrews.Unit` of two `CrewWalker`s, spawned as `SpawnSquad` spawns
squad men (same `man_police` prefabs, `PoliceFaction`, sidearm concealed), driven by a **beat
brain** (`RoadDemo/PoliceBeat.cs`) that implements `IPoliceUnit` and `IPatrolMarker`:

* Patrol: the ring's waypoints as `OrderToPoint`/`OrderTo(PedLink, t)` legs at walking pace;
  the pair keeps the crowd's crossing discipline — police override `CrewWalker.MayEnter` back
  to the civilians' rule (the outfit jaywalks, the law does not).
* Station: `WalkOut`/`WalkIn`/`Inside` through `CrewQuarters.Station`, the same passage a crew
  uses for its own door. `Ritual` (the forecourt stand) stays as an order to a point.
* The statement visit: `DoorBeat.VisitBusiness(CrewWalker …)`, exactly as a car squad's lead
  does it today (`Complaint.cs` ~602).
* The challenge: an order to stand `CollarGap` from the man, sidearm drawn through `CrewArms`;
  `StoodOver` is the order completing.
* Return fire: a police unit does not answer fire by itself (`Combat.cs` ~571). The brain reads
  provocation — its own `ProvokedAt`, or a shot whose shooter's `TargetUnit` is the pair — and
  `Sic`s the unit on `UnitOf(shooter)`. Both men fight (ruling 3). This is also the "shots at
  an officer" signal the swarm needs.
* Death: automatic through `Combat.cs` ~1425 → `StreetAlarm.Death(Officer)` → heat 100 →
  `RaiseSwarm` → precinct loss. A dead pair is **replaced**: `PoliceForce.MakeBeat(precinct)`
  beside `MakeCar`; `Precinct.Leads` retyped to the brain.
* The overlay popup stays "Officer N"; the ground bracket (`PolicePatrolOverlay.cs` ~326),
  `PrecinctNear` (`PoliceDispatch.cs` ~214), `LawWithin`'s Inside/Doorway exclusion
  (`Wanted.cs` ~138), `TestBench.TickPavementLife`, and CrewDemo's own beat
  (`CrewDemoBuilder.cs` ~1133) all switch on the type and must join.
* `RoadDemoBuilder` (~4760 station pairs, ~4887 block pairs) builds the unit.
* Scale: the big city deals `policeBeatPairsPerBlock` (2) × blocks. `CrewWalker` has no
  `PedDetail` gate; a fight-less police walker goes under the same distance gate the crowd
  has. Measure the pair count on the big city before the epic closes.
* No fallback. Swapping a `PoliceFootPatrol` into a unit at challenge time double-owns one
  body across `DoorBeat.Active`, `CallOut.Unit`, `Precinct.Leads` and the overlay's selection,
  and a pair not under challenge would still be unshootable. Struck.

### 3.2 Three answers (ANSW-002)

`SurrenderRoll` answers `Quiet | Run | Fight` from one `System.Random(stream)` in fixed order:

* Draw 1 is today's `FightChance` (courage / temper / loyalty): goes quietly or not.
* Draw 2, among the crews that do not: `FightOdds2 = 0.5·temper + 0.3·courage − 0.2·discipline`
  of the commanding man (the lieutenant on the street, else the senior man), same floor and
  ceiling. FIGHT above the draw, RUN below. A crew with no man `Carrying` a gun can only RUN.
* Same stream, same salt (`crewKey * 31 + incident`); never `incident + 1` — that is the next
  incident's salt. Contract in `PoliceTests`: the two draws differ across incidents and repeat
  across runs.
* `Leaning` has three words: `going quietly` / `will run` / `itching to fight`.

**When it lands.** RUN lands the moment the officer is stood over the man (`StoodOver`): a man
who is going to run does not wait to be asked. QUIET and FIGHT land at
`max(stoodOverAt, collarAt + AskSeconds)` with `AskSeconds` shortened to **8 s**: that floor is
the player's window (CONF-002/003) and it keeps the paper-screen pause (`Blocked` extends to
`WalkingUp`). The player's attack order during the window is FIGHT; the player's flee or walk
order during the window is RUN (see 3.3). The banner reads the leaning from the first step of
the walk-up, as today.

### 3.3 RUN (ANSW-003)

* `DemoCrews.OrderFlee(crew, officerPos)`; every man on the books is marked `WantedLevels.Fled`
  (the first street caller of `Fled`).
* **Walking off is RUN.** During `WalkingUp`/`Asking`, a crew that leaves `WalksOff` (22 m) or is
  ordered to flee takes the RUN branch — `Drop()` (nobody refused) only when the law itself is
  gone. Today walking off is free, and the player would always walk off.
* Pursuit: `ChaseOnSight` skips a `Fleeing` unit — a running man is chased by `LastSeen`
  (EPIC 20's chase), not re-asked by a fresh collar half a second later. On a sighting the
  nearest **Available** unit (a second lookup; the seer is usually busy) is routed to the
  sighting point; the seer itself follows if it is free (ruling 5). One car is sent to the
  door.
* A rival crew's flight ends: at a `GangFront` of its own family, or at `FleeDistance` with the
  pursuit broken (`SeenByLawAt` older than `BreakSeconds`) — today the hideout and the nearest
  door are the player's only. `Fled` cools in 3 days as it does for us.
* The case: the file gets a `Resisting` count against every man who ran (3.6). No swarm.

### 3.4 FIGHT (ANSW-004)

* The crew is `Sic`ced on the beat unit at once; both officers answer (3.1).
* The first round fired at an officer raises the swarm: `RaiseSwarm(where, grade)` gets its
  second caller from the beat brain's provocation read (ruling 4, the whole city, 8 cars,
  `anyDistance`). The banner reads `SHOTS AT AN OFFICER — EVERY CAR IN THE CITY IS COMING`
  for this grade; `OFFICER DOWN — …` stays for a death.
* **The mark follows the outcome, not the alarm.** `StandDown` marks the hunted with the
  swarm's grade: `CopKiller` when an officer died during it, otherwise the new
  `WantedLevels.ShotAtOfficer` (value 4, appended; cools in 7 days like `FreedFromTransfer`).
  `Mark` orders by a `Severity(level)` function rather than by the integer, so `CopKiller`
  stays the top and saved levels keep their meaning. Without this a missed shot is a lifelong
  cop-killer mark.
* Deed: nobody down → `Deed.AssaultOnOfficer` (appended after `WitnessTampering`) becomes the
  charge on the file and the complaint's charge stays as a count. An officer dead →
  `CopKilling` as today. A hood of the crew shot by the police → still `AssaultOnOfficer`
  (`TheDeed()`'s `GangDeaths > 0 → Murder` yields to it when the shooters were the law).
* `AssaultOnOfficer` and `Resisting` are set in **every** deed switch: `BandLow`/`BandHigh`
  (`BandHigh(AssaultOnOfficer) > BandHigh(Affray)`; `Resisting` a short band), `ChargeFor`,
  `Bail`, `Verdict.BaseFor`, `PrisonPipeline.Worse`. The band test loops
  `Enum.GetValues(typeof(Deed))` so a sixth switch cannot be missed again. Save is safe: deeds
  are written as int.

### 3.5 QUIET, and the ride to the station (ANSW-005)

* **Hands up ends the collar.** `GiveUp` hands the crew to a `Custody` record (a list on the
  dispatcher, one per arrest — never the single `_collar` field, which would freeze every
  other arrest, chase and complaint in the city for the length of a car trip) and `_collar`
  returns to `None`. The officers stand over the crew; the crew stands with its guns away
  (`Surrendered`; player orders refused with `HandsUpRefusal`).
* The custody sends pickups to the **crew's** position (`RouteTo(pos, StandOff)`). A pickup has
  an eight-person limit: its two officers ride in front and as many as six prisoners are secured
  in the rear. A normal five-man street crew therefore needs one vehicle. Cars are still capped
  at what the precinct can spare (`CarsOnDuty` minus one); men beyond the available capacity
  wait with hands up for the next trip. The
  `WalksOff` test is off for a crew in custody; `CallOut.HomeBy` in `Arresting` covers the
  car's trip, not 64 s.
* The car arrives; its men get out and stand by the doors; the prisoners walk to the car in
  escorted pairs and disappear into its rear cargo load (the `CrewWalker` bodies leave the street
  at the car door, as squad men do today; the custody keeps their `CharacterId`s). The beat
  pair is released back to its round (ruling 6). The car's men get back in; the car
  `Release()`s home.
* **At the car door he is the law's, but he is still ours to see.** From the moment a man is
  seated the player cannot command him: every order to a unit in custody is refused with its
  own reason (`InCustodyRefusal`, the way `HandsUpRefusal` refuses today). He is **not**
  hidden: his overlay marker and HUD stay on the 3D map, a right-click still focuses the
  camera on him, and he stays in the chain of command on the roster (still his lieutenant's
  man; his file reads `in custody`). His **guns are gone** — every piece he carries is taken
  at the car door (`CrewArms` detached, the character's issued arms cleared on the roster;
  they do not return to the armory). A man sprung later comes back into the player's hand
  **unarmed** and is armed again only through his lieutenant, as every man is.
* **Booking at the station door** (ruling 7). At `BeginDock` the car's men and the prisoners get
  out as walkers again (the prisoners `Surrendered`, escorted) and walk the station passage
  (`CrewQuarters.Station`); `TakeIn(unit, deed, pipeline, file)` fires as they cross the
  threshold. The docket row, the court day and the LAW tab appear then and not before.
* **The rescue window.** From hands up to the threshold the men are on nobody's books. A
  custody whose escort is wiped, whose car is wrecked (`Wreck()`), or whose prisoners are
  fired on and freed at the forecourt ends with the men **sprung**: `LetGo` is not called —
  they are marked `WantedLevels.FreedFromTransfer` (the mark lives on `Character`, it needs no
  `Prisoner`), and `PrisonPipeline.Sprung(roster, id, today)` records the escape so the trial's
  surcharge (`_everEscaped`) applies when they are next taken. The case stays open with the
  `Resisting` count.
* Backstops: no car reaches the crew inside the trip's patience + `CollarPatience` → today's
  walk-off `TakeIn` on the pavement (the men leave with the beat pair); a custody drive that
  never docks inside `TransferPatience` → booked at the kerb where the car stands.
* Save mid-custody: `Custody` is not persisted; a save during the ride loads the men on the
  street, `Fled`-marked, the case open. Written down here as the accepted loss.
* Where the collar was a car squad's lead, the men board **his** car; the squad rides home
  with them.
* CrewDemo's `PoliceCruiser` (`CrewCar`) takes the same custody through `BoardCar`; the seam is
  `IPoliceUnit`, not the car type.

### 3.6 The case file carries the answer (ANSW-006)

* `Prisoner.Answer` (`DoorAnswer {Quiet, Ran, Fought}`) and `Prisoner.Sprung` are set at
  `Book`; `PrisonerDto` gains both fields (no `CourtCaseDto` change, no version bump).
* `Deed.Resisting` is a count: `AttachCount(file, Resisting)` on RUN; the file's `Counts`
  today hold case ids, so a `Deed`-typed count list (`CourtCase.ExtraCharges`) is added and
  `Sentencing.Days` receives it in `extraCounts`.
* The trial: `Ran` adds `EscapeSurcharge`'s cousin `ResistSurcharge` (2 days); a sprung man
  carries `_everEscaped` exactly like a man freed from a transfer.
* The sheet: the DOCKET card prints `ran from the officer` / `fired on the officer` on the
  defendant's line; the VERDICTS archive keeps it. `LawWire` prints the answer at the door
  (`RefusedTheOfficer` splits into `RanFromTheOfficer` / `FiredOnTheOfficer`) and the custody's
  end (`TakenIn`, `Sprung`).

### 3.7 Dispatcher plumbing (ANSW-007)

* `Send` for a complaint targets `call.Call.Pos`; `_carsSent` resets per complaint number.
* `PoliceOnTheScene` counts the beat unit.
* A squad arriving at a complaint scene with the accused still standing puts the question
  (`TryComplaintCollar` for a squad lead, not only the door's own unit).
* `Close(call)` after the custody has left with the men or the men have run; every stage keeps
  its backstop.

### 3.8 The night watch (ANSW-008)

After GAN-291 (NIGHT-009) lands the `arrest`/`court` trace rows: forced scenarios on
MiniCoreDemo, five seeds each — quiet crew, running crew, psychopath crew, right-click murder
of a beat pair, prisoner car shot up on the road, rescue at the station forecourt. Each prints
the custody's end and the file's counts; `analyze.py --law` judges them.

## 4. Rules that must hold

* One roll, pure and seeded, in `SurrenderRoll`; the street reads it, never re-rolls it.
* One police body model: `DemoCrews.Unit`. No `PedestrianAgent` learns to be shot.
* The mark follows the outcome: nobody is a cop-killer for a miss.
* Booking at the threshold and nowhere else; the docket row appears when the man is inside.
* The single `_collar` field never holds a custody.
* Police still do not shoot first (GAN-220): the beat unit answers fire, it does not open it.
* Every new `Deed` value is set in every switch, and the test loops the enum.
* A man in custody takes no order and carries no gun, but stays on the map, focusable, and
  in the chain of command; a sprung man comes back unarmed.

## 5. Tickets, in order

1. ANSW-001 — The beat is a crew (foundation; the biggest piece)
2. ANSW-002 — Three answers, and when they land
3. ANSW-007 — Dispatcher plumbing
4. ANSW-003 — RUN
5. ANSW-004 — FIGHT, the swarm by grade, the deeds
6. ANSW-005 — QUIET: custody, the ride, booking at the threshold, the rescue window
7. ANSW-006 — The case file carries the answer
8. ANSW-008 — The night watch (after GAN-291)

## 6. Acceptance

* `gangsters_police_tests` green with: three answers deterministic per (crew, incident); every
  `Deed` value in every switch; `Severity` ordering; `Sprung` surcharge; `ResistSurcharge`.
* MiniCoreDemo, live editor: a shopkeeper rings; the beat pair walks over; (a) a quiet crew
  stands, a car comes, the men ride, the docket row appears as they cross the station
  threshold; (b) a running crew is chased by the nearest free unit and is `Fled` on the LAW
  tab; (c) a psychopath crew fires, both officers fire back, the whole city comes, the
  survivors are `ShotAtOfficer` unless an officer died; (d) a right-click on a beat pair kills
  it, the pair is replaced on the next replacement day; (e) a car with prisoners shot empty on
  the road leaves the men sprung and the case open.
* `recompile_status --json` clean; `gangsters_police_tests`, `gangsters_organization_tests`,
  `gangsters_wage_tests` green; the big-city pair count measured and written here.

## 7. Ticket texts mirrored here

Linear holds the epic GAN-315 and eight tickets: GAN-316 ANSW-001, GAN-317 ANSW-002,
GAN-318 ANSW-007, GAN-319 ANSW-003, GAN-320 ANSW-004, GAN-321 ANSW-005, GAN-322 ANSW-006,
GAN-323 ANSW-008. The last three were refused at first ("You've exceeded the free issue
limit") and created after 231 Done issues were deleted from the workspace on 2026-09-03 (they
sit in Linear's "Recently deleted" for 30 days). The three texts stay mirrored here; the model
for each is in §3.5, §3.6 and §3.8 above.

### ANSW-005: QUIET — custody out of the collar, cars to the crew, the ride, disarmed and unorderable in the car, booking at the station threshold, the rescue window

**Today** `Collar.Taking` books the crew on the pavement 4 s after hands up (`TakeIn` →
`RosterOps.Jail` → `Sync` drops the bodies). The dispatcher has ONE `_collar` field;
`TryComplaintCollar`, `ChaseOnSight` and `LookForACollar` refuse while it is not `None`, and the
complaint's `Arresting` stage gives up on it after 64 s. The city's police car is
`PolicePatrolCar : DemoVehicle : RoadCar` with no seats (`grep -n "Aboard\|SeatOf\|Seats"
PolicePatrolCar.cs DemoVehicle.cs` is empty); `BoardCar` needs a `CrewCar`, which only
CrewDemo's `PoliceCruiser` is. Squads never board: they walk to the car and are `RemoveUnit`'d.
`ArrestOff` drops the collar when the crew is more than `WalksOff` (22 m) from the officer.

**Build**

* Hands up ends the collar: `GiveUp` → a `Custody` record (`PoliceDispatch.Custody.cs`, a list
  on the dispatcher: crew, deed, file, call, officers stood over it, cars sent, stage) and
  `_collar = None`. `WalksOff` is not tested for a crew in custody; `CallOut.HomeBy` covers
  the trip.
* Cars to the crew's position (`RouteTo(pos, StandOff)`), `ceil(n/2)`, capped at
  `CarsOnDuty − 1`; men who do not fit wait with hands up. Backstop: no car inside the trip's
  patience + `CollarPatience` → today's walk-off `TakeIn`.
* At the car: the car's men get out and stand by the doors; each prisoner walks to the rear
  door, his `CrewWalker` body leaves the street and a `CarOccupant.Seat` body sits in the back
  (own prefab, sit loop, `CarBody.MeasureSeats`); the custody keeps the `CharacterId`s. The
  beat pair is released to its round. The car `Release()`s home.
* Ruling 9: every order to a unit in custody is refused with `InCustodyRefusal`; marker and
  HUD stay on the 3D map (the marker follows the car); right-click still focuses him; he stays
  in the chain of command (file reads `in custody`). Guns gone at the car door: `CrewArms`
  detached, issued arms cleared on the roster, nothing back to the armory.
* Booking at the threshold: at `BeginDock` the car's men and the prisoners get out as walkers
  (prisoners `Surrendered`, escorted) and walk the station passage (`CrewQuarters.Station`);
  `TakeIn(unit, deed, pipeline, file)` fires as they cross it. Backstop: a custody that never
  docks inside `TransferPatience` → booked at the kerb.
* The rescue window: escort wiped, car `Wreck()`ed, or prisoners freed on the forecourt →
  sprung: no `LetGo`; the men return to the player unarmed, marked `FreedFromTransfer`,
  `PrisonPipeline.Sprung(roster, id, today)` records the escape for the surcharge. Case stays
  open. `LawWire.Sprung(...)`.
* Save mid-custody not persisted (accepted loss). A squad lead's collar boards his car;
  CrewDemo's `PoliceCruiser` takes the same custody through `BoardCar`.

**Contracts**: `SprungRecordsAnEscapeWithoutABooking`, `InCustodyRefusesEveryOrder`,
`CarsForPrisoners`. Street: MiniCoreDemo, a quiet crew rides visible, disarmed, unorderable,
and the docket row appears at the threshold; the same run with the car shot empty leaves the
men on the pavement, unarmed, `FreedFromTransfer`, the case open. No other arrest, chase or
complaint is blocked while a custody is on the road.

### ANSW-006: The case file carries the answer — Prisoner.Answer/Sprung, CourtCase.ExtraCharges, ResistSurcharge, the sheet and the wire

**Build**

* `Prisoner.Answer` (`DoorAnswer`) and `Prisoner.Sprung`, set at `Book`;
  `PrisonPipeline.Sprung(roster, id, today)` adds to `_everEscaped` without a booking.
* `CourtCase.ExtraCharges` (`List<Deed>`) beside `Counts`; `AttachCharge(file, deed)`;
  `Resisting` on RUN; the complaint's original charge when FIGHT makes the charge
  `AssaultOnOfficer`. `Sentencing.Days` receives `Counts.Count + ExtraCharges.Count`.
* `Sentencing.ResistSurcharge = 2`, added after the escape surcharge, before rank scaling.
* Save: `PrisonerDto` gains `answer`, `sprung`; `CourtCaseDto` gains `extraCharges[]`.
* Sheet: the DOCKET card prints `ran from the officer` / `fired on the officer` / `sprung on
  the way in`; `+N COUNTS` counts both lists; VERDICTS keeps the answer; one word table in
  `LedgerText`.
* Wire: `RefusedTheOfficer` splits into `RanFromTheOfficer` / `FiredOnTheOfficer`; the
  custody's end prints `TakenIn` / `Sprung`.

**Contracts**: `RunningCostsTwoMoreDays`, `ASprungManCarriesTheEscapeSurcharge`,
`ExtraChargesAddDays`, `TheAnswerSurvivesASave`. Ledger.unity render check of a case with a
`Resisting` charge.

### ANSW-008: The night watch — six scenarios ×5 seeds on MiniCoreDemo; everything to Done

**Prerequisite** GAN-291 (NIGHT-009): the `arrest`/`court` trace rows and `analyze.py --law`
do not exist yet (`night.sh` knows `--core-s1..s4` only).

**Build**: six forced scenarios, five seeds each, live editor: quiet crew; running crew;
psychopath crew; right-click murder of a beat pair (swarm, precinct loss, `MakeBeat`);
prisoner car shot empty on the road; rescue on the station forecourt. Trace rows `answer`,
`custody`, `sprung`, `booked`; `analyze.py --law` judges: every custody ends `booked`, `sprung`
or the walk-off backstop; no `_collar` held past `CollarPatience`; no unit stuck `OnScene`; no
dead beat pair without a replacement; no `CopKiller` in a run with no officer death. A
`gangsters_answer_probe` pipeline command forces one answer for the next collar (seeded odds
override). The brief gets the big-city pair count and the night's numbers;
`Docs/police-behaviour-plan.md` gets a §10 pointing here. Every fault the night finds is fixed,
never deferred. GAN-315 to Done with it.
