# The Road to the Courthouse — design brief (EPIC 35)

Written 2026-09-04 from the user's rulings of that day, and **reworked the same day** after a
contrarian pass found four load-bearing mechanisms the first draft assumed and the code does not
have. Companion to `Docs/design-briefs/answer-at-the-door-brief.md` (EPIC 34, the arrest-side
custody drive) and `Docs/police-behaviour-plan.md` §8.3 (the two-leg convoy, GAN-237).
Label **Police**.

Whoever implements a ticket of this epic moves that ticket, and when the last one lands the
epic itself, to **Done** in Linear.

## 0. The defect

A held man survives a bomb under his transfer, and there is no other way to take the transfer.

* The prison convoy (`PoliceForce.Convoy`) carries **no bodies**. The prisoner's `CrewWalker` is
  switched off inside the station (`DoorBeat.cs:1107`) and only a position pin
  (`PoliceForce._custodyPins`, written at `PoliceForce.cs:530, 603`) rides in the car.
  `PolicePatrolCar` carries one decorative `CarOccupant` and no escort. A bomb under the car
  kills two officers **by decree** (`PoliceForce.cs:619-622`), cannot reach the prisoner
  (`Explosion.KillCrews` measures distance to a body that is at the station,
  `Explosion.cs:73-86`), and `Freed` then stands him up beside the wreck, alive
  (`PoliceForce.cs:634-643`).
* Only an explosion wrecks a car. `RoadCar.Wreck()` (`RoadCar.cs:152-158`) is called only by
  `CarShatter.Shatter`, called only by `Explosion.Blow` with a car — and only the **planted**
  charge passes one (`CrewBomb.cs:351`); a thrown grenade passes `null` (`CrewBomb.cs:74`).
* Gunfire reaches a `CrewCar` but never a `PolicePatrolCar`: `CarMark` is typed `CrewCar`
  (`CrewWalker.cs:114-125`), `OrderShootCar` refuses anything else (`DemoCrews.cs:2074-2076`),
  and `CrewOverlay.PickCarAt` walks only `_crews.Cars` (`CrewOverlay.cs:684-712`).
* Men riding in a car are never targets (`DemoCrews.Combat.cs:170-179`), so a moving transfer's
  escort cannot be shot at, and it never gets out.
* The court leg ends at a drive target six metres in front of the courthouse face
  (`RoadDemoBuilder.CourthouseSetback = 6f`, `:4207`), not at a threshold anybody walks. The man
  is tried on arrival of the car, without leaving it.

EPIC 34's arrest drive already has the physical model the convoy lacks: seated bodies parented to
the car (`PoliceDispatch.Custody.cs:1003-1033`), two-man escort squads that are real
`DemoCrews.Unit`s, the walk from the car to the station threshold, booking at the threshold, and
the spring rules. This epic lifts that model out and gives it to both legs of the convoy — **but
only after the four gaps of §1a are closed**, because three of them make the lift a silent no-op.

## 1. Rulings (the user's, 2026-09-04)

1. **A man is held one day and tried the next.** `Sentencing.DaysToCourt = 1` (landed 2026-09-04,
   contract `DaysToCourt == 1`). `DaysToPrison` stays 1.
2. **Realism over mercy.** A bomb under a car kills everyone in it, the prisoner included. The
   bomb is for killing the man in the transfer, not for freeing him.
3. **Freeing him means stopping the car, not blowing it up.** Gunfire on the transfer stops it and
   brings the escort out on foot to fight. Escort dead → the man is out of the car.
4. **A roadblock is a real tool.** An outfit car stood across the road stops the transfer where the
   player chose.
5. **Our own man can be killed by our own fire, on a small chance.** Fire into a car is not
   surgical and not a coin toss.
6. **The walks are seen.** Cells to the car, car to the courthouse door, out again to the van —
   every leg on foot is visible on the 3D map and can be intercepted there.
7. All of 2–6 are built. One epic.

## 1a. What the contrarian pass found (verified in code, and what changes because of it)

These are not caveats. Each one made a ticket move or a mechanism change.

| # | The gap | Verified at | What changed |
|---|---|---|---|
| C1 | **A blast death never reaches the books.** `_deaths.Add` has exactly three call sites, all gunshot/run-down/bodyguard (`DemoCrews.Combat.cs:1588, 1919`, `DemoCrews.RunDown.cs:162`). `Explosion.KillCrews` calls `man.Kill()` direct and `CrewWalker.Kill()` (`CrewWalker.cs:2233-2263`) touches neither `_deaths` nor `StreetAlarm`. The decree deaths were the **only** thing charging a precinct for a bombed transfer. | verified 2026-09-04 | **New first ticket ROAD-000.** The decree deaths are not deleted until the body channel is proven, and then only as a fallback (§3.0). |
| C2 | **The pin is the body's liveness latch, not a stand-in for it.** `Sync` builds `trackedCustodyIds` purely from `TryCustodyPosition` (`DemoCrews.cs:2887-2891`) and only those ids escape the active-roster re-deal (`:2955-2958`). Remove pins and the next Sync destroys the body the carriage is about to walk out of the cells. | verified 2026-09-04 | §4's rule re-scoped; the latch is **split from the position** (§3.1). |
| C3 | **The escort has no brain.** `SpawnSquad` returns a bare `Unit` (`PoliceDispatch.cs:756-796`); `PickFight` belongs to `Squad` (`PoliceDispatch.cs:666`); the only self-Sic is `PoliceBeat.ReadProvocation`. Police never target on their own (`DemoCrews.Combat.cs:603`), and the swarm needs `shooterUnit.TargetUnit.IsPolice` (`PoliceDispatch.cs:236-246`) — a crew on `CarMark` has `TargetUnit == null`. | verified 2026-09-04 | `CarriageEscort` with its own `ReadProvocation`; rounds into police tin raise the swarm (§3.3). |
| C4 | **`OccupantHitChance` per round is unbounded.** `SHOOT IT UP` fires until the car is a wreck or the order is lifted (`CrewWalker.cs:3529-3595`), and a `RoadCar` only wrecks from a blast. At 0.06 a round, sixty rounds kill him ~97% of the time. | verified 2026-09-04 | The roll moves off the round onto a **capped jeopardy budget**, and `Halted` clears every `CarMark` (§3.3). |
| M1 | **The roadblock can be a silent lock.** The jam escape needs `!InQueue && toEnd > 30f` (`RoadCar.cs:2112-2113`); a block near a junction never accumulates `_jammed`, and `PolicePatrolCar.VanishesWhenStuck` is false (`:19`), so the transfer stands until `TransferPatience` sends him back to the cells. Pedestrians walk through `StoodCar` (`WalkObstacles.cs:621`). | verified 2026-09-04 | §3.4 states the barricade reverse and registers the block in the walking ledger. |
| M2 | **The courthouse has no threshold.** `FindCourthouse` derives one point 6 m off the face (`RoadDemoBuilder.cs:4192-4197`) and it is both the convoy's `To` (`PoliceForce.cs:490-491`) and the plan's door. `DoorBeat.MoveIn(man, point)` is a doorstep hide, by its own doc (`DoorBeat.cs:770-773`). | verified 2026-09-04 | Two measured points, kerb and door (§3.1). |
| M3 | **ROAD-006 had no driver.** `MonkeyRunner` issues only attack, car and drive-by orders (`:195, 218, 260`); the only scripted bomb plant is BlockDemo's (`BlockDemoMission.CarBomb.cs:290`), and the night-watch rule replaced BlockDemo with MiniCoreDemo. A planted charge also springs only above `RoadSpeed >= 1.2` (`CrewBomb.cs:321, 349`), so it can only be laid on a **stopped** car. | verified 2026-09-04 | ROAD-006 gets its own mission driver and the scenarios are re-ordered (§3.6). |

## 2. What exists and is reused

| Piece | Where | Reused for |
|---|---|---|
| Seated bodies (`Seat`, `SeatedBody`, `RestoreBodies`) | `PoliceDispatch.Custody.cs:1003-1033, 1180-1202` | every ride of a prisoner or an escort |
| Boarding geometry (`BoardCustody`, `TickPrisonerBoarding`, `OrderPairToRearDoor`, `PrepareBoardingGeometry`, `VehicleDoor`) | `PoliceDispatch.Custody.cs:515-935` | both convoy legs |
| The walk to a threshold (`WalkIntoStation`, `TickStationThresholds`, `DoorBeat.MoveIn`, `DoorBeat.Held`) | `PoliceDispatch.Custody.cs:1057-1128`, `DoorBeat.cs:772-789` | station door out, courthouse door in and out |
| Pure custody rules (`CustodyPlan`) | `Assets/Scripts/Police/PoliceShifts.cs:104-170` | extended, never duplicated |
| Convoy scheduling (`RunTransfers`, `Riding`, `FreeCar`, `TransferPatience`) | `PoliceForce.cs:448-551` | kept as the scheduler; the carriage replaces the pin |
| Gunfire into tin (`CrewCar.TakeRound`, `EngineChance`, `CarSmoke.Bonnet`, `CrewGore.Hole`, `PutRoundIntoTin`) | `CrewCar.cs:164-187`, `DemoCrews.Combat.cs:1638-1740` | lifted to `RoadCar` |
| `SHOOT IT UP` / `PLANT A BOMB` rows | `CrewOverlay.OpenCarOrders:1980-2046` | shown on a police car too |
| `StandDown` → `Derelict`, driven round | `RoadCar.cs:679-686, 3145` | the shot-up transfer |
| Stood car as a static road occupant | `StoodCar.cs:16-53` | the roadblock |
| `PoliceBeat.ReadProvocation` (the one self-Sic that exists) | `PoliceBeat.cs:323-340` | the model for `CarriageEscort` |
| `Explosion.KillCrews` by distance; `CarShatter` spares rigged bodies | `Explosion.cs:73-86`, `CarShatter.cs:65-76` | the bomb reaching seated men |
| Pipeline closes (`Note`, `ResolveDefendant`, `Freed`, `Sprung`, `BackToTheCells`) | `PrisonPipeline.cs` | a `Killed` close beside them |
| Wire and banners | `LawWire.cs`, `CrewOverlay.Announce` | new lines in the same voice |

## 3. The model

### 3.0 The blast must be able to kill anybody (ROAD-000) — FIRST

Nothing else in this epic is true until a man killed by a blast reaches his house's books.

* `Explosion.KillCrews` stops calling `man.Kill()` raw. It calls a new
  `DemoCrews.KilledByBlast(CrewWalker)` which does exactly what the gunshot path does at
  `DemoCrews.Combat.cs:1586-1591`: `Kill()`, queue `_deaths` with `DeathReportDelay`, and report
  the death down `StreetAlarm.Death` with the right `DeathOf` (officer or gangster). The
  per-man reporting rule of `Explosion.cs:48-51` is honoured — the blast still reports no blanket
  death of its own.
* **The decree stays as a fallback.** `PoliceForce.Ended(wrecked)` keeps charging its precinct two
  officers **only when the carriage stood no escort bodies** (`SpawnSquad` returns null with no
  officer prefab, `PoliceDispatch.cs:767-770`). `_losing` is set around the carriage's own death
  reporting so precinct attribution stays exact instead of falling to the `PrecinctNear` guess.
* Contract before anything else ships: **a bomb on a crewed car strikes every occupant off his
  house's roster, and a bombed police car costs its precinct exactly two officers, once.**

### 3.1 One carriage for every ride (ROAD-001)

**The cut, measured.** `PoliceDispatch.Custody.cs` is 1247 lines. What is genuinely
body-and-walk, and moves into `PrisonerCarriage`, is about **508 lines**: `Seat`/`SeatedBody`,
`RestoreBodies`, `BoardCustody`, `EscortAt`, `TickPrisonerBoarding`, `AllBoarded`,
`EscortJoinSpot`, `EscortDoorSpot`, `AtBoardingDoor`, `OrderEscortToPrisoner`,
`OrderPairToRearDoor`, `PrepareBoardingGeometry`, `VehicleDoor`, `OrderBoarderToDoor`,
`OrderCustodyLeg`, `MeasureCarHalfWidth`, `BeginOfficerBoarding`, `OrderOfficerToSeat`,
`TickOfficerBoarding`, `CarDoor`, `DisarmPrisoner`, `WalkIntoStation`, `CustodyFlat`.

What **stays** with the collar, because it is welded to a `DemoCrews.Unit` the convoy has no
equivalent of (`TickCustody` aborts on `Crew == null`, `:263-267`): `BeginCustody`,
`ClaimArrestingCar`, `ReassertCustody`, `KeepCustodyCovered`, `HoldSquadAtGunpoint`,
`NearestPrisonerToCover`, the wave machinery (`BeginReturnForNextWave`, `DepartForNextWave`,
`ArriveForNextWave`), `Spring`, `FinishBookedCustody`, `ReleaseHoldingSquad`,
`WriteCustodySaveFallback`, `Call.MenRefused`.

So the carriage is keyed on **`CharacterId` + `CrewWalker` + an escort `Unit`**, never on a crew.
`Custody` becomes its *caller*, not its subclass — the arrest drive keeps its own stages and asks
the carriage to board, ride, walk and restore. This is the boundary; a ticket that finds itself
dragging `Crew`, `Beat` or `Call` into the carriage has cut in the wrong place.

**The liveness latch survives (C2).** `PoliceForce` keeps a custody registry, but it splits in
two: `KeepAlive(characterId)` — the set `Sync` reads so a booked or riding man's body is never
re-dealt away — and `Position(characterId)` — which the carriage now answers from the real body
instead of a stored point. §4's rule is therefore "no pin stands in for a man's **position**",
not "no pin". Contract: **a man in the carriage survives a `Sync`.**

**The court leg, physically.** Day tick → `RunTransfers` picks the car as today. The car drives
to the station kerb. The prisoner is let out of the station (`DoorBeat.SendOut`, on his feet,
`Surrendered`, unarmed, the existing surrender pose); a two-man escort walks him to the rear door
and seats him; the escort takes the front seats; `Pipeline.Away` fires **when he is seated**, not
when the car reaches the kerb. The car drives to the courthouse **kerb**. The escort walks him
from the kerb to the **door**, and the trial is the threshold: `Pipeline.Tried` runs when he is
`DoorBeat.Held` inside, not when the car parks. Acquitted or dismissed → he walks back out the
same door, released there. Sentenced → he stays inside overnight.

**Two courthouse points, both measured (M2).** `FindCourthouse` today throws away the road-edge
point it computes (`nearest`, `RoadDemoBuilder.cs:4176-4182`) and returns one door. It now
returns both: the **kerb** (that road-edge point, where the car stops) and the **door**
(the face plus a doorstep of about 1.5 m). `PoliceForce.StandCourthouse` takes both. The walk is
the gap between them, which is the building's real setback from the road; the builder **logs the
measured metres** so a scene where it is under about 4 m is a known-bad court parcel rather than
a silent zero-metre walk. `WalkObstacles.ClearSpot` is asked for both ends before the walk is
ordered. With no courthouse in the city the leg drives to the county line as today, and the walk
out of the station is the only visible foot leg — stated, not implied.

**The prison leg.** Next day tick: a car to the courthouse kerb, he walks out with the escort,
boards, rides to the county line, and the carriage delivers him — `Pipeline.Delivered`, the body
retired for good, the escort re-boards, the car goes home.

**Pickup rule kept.** A carriage broken before he is seated frees nobody (`Freed` refuses
`ForTransfer`, `PrisonPipeline.cs:857-858`); broken with him seated, it is an escape.

**Stages** (one enum, both users): `Calling`, `WalkingOut`, `Boarding`, `Riding`, `Halted`,
`WalkingIn`, `Delivered`. `CustodyPlan` gains the pure questions they ask (`ShouldHalt`,
`ShouldDismount`, `WalkTheRest`, `CanDeliver`, `InJeopardy`).

**Which `Riding` passes must change.** One table, one contract each. Keep skipping him:
`Targetable` (`DemoCrews.Combat.cs:179` — nobody targets a prisoner deliberately), formation and
tether (`DemoCrews.cs:2388, 2433, 2447`), gait and shout passes (`:1264, 1792, 2102, 2143, 2767`).
Must now see him: the map and HUD paths, so §3.5's glow is true, and `Explosion.KillCrews`, which
already does because it reads position only.

### 3.2 A bomb kills the man in the car (ROAD-002)

With ROAD-000 landed and bodies really parented to the car, the blast reaches them and the books
hear about it. What is left is the case:

* `PrisonPipeline.Killed(roster, characterId, today)`: removes him from `_inside`, writes
  `CaseOutcome.Killed` (**appended** — the enum ends at `CutLoose`, `CourtCase.cs:51-66`) through
  `Note`, closes his part with `ResolveDefendant`, rap-sheet line "Killed in the transfer". The
  roster death itself arrives through `ReportDeaths`, which now hears the blast. The pipeline
  never calls `Kill` itself: two doors, one death.
* `LedgerText.CaseOutcomeLine` → "killed in the transfer". `LawWire.Killed(character)`. Banner
  "THE PRISONER IS DEAD IN THE CAR".
* `Ended(wrecked)` asks the carriage who is alive: a living seated man is `Freed` as today, a dead
  one is `Killed`.
* **The dead man leaves the wreck.** A body killed in a seat keeps disabled renderers and stays
  parented to the husk (`PoliceDispatch.Custody.cs:1030-1032`), and gets no floor or chalk because
  both are gated on `!Riding` (`DemoCrews.Combat.cs:1587`, `DemoCrews.cs:686`). The carriage's
  restore therefore runs **for the dead too**: un-parented, renderers back on, laid on the road
  beside the wreck, chalked like any other body.
* Arrest-side custody bombed before booking: dead men are reported by `ReportDeaths`, `Spring`
  marks only the living, and the case file gets no `Sprung` for a dead man.

### 3.3 Shooting the transfer (ROAD-003)

* **A police car can be clicked.** `PickCarAt` also walks the force's cars. `OpenCarOrders` shows
  `PLANT A BOMB` and `SHOOT IT UP` on one; `HIDE BEHIND IT` as for any car. A charge under a
  resting patrol car at the station is a legal, ugly thing to do. **Fog:** a transfer is revealed
  once the wire announces it, so the pointer and the plate can both find it
  (`MapVisionRegistry.IsRevealed` at `CrewOverlay.cs:696`, `MapVehicleVisible` at
  `TurfMapHud.cs:2781`). Written down as a rule because the plate already tells the player it is
  on the road.
* **Tin is tin.** The damage model moves from `CrewCar` (which is `sealed`, `:19`) to `RoadCar` or
  a shared `Tin`. The full surface, named so nobody discovers it late: `CarMark`
  (`CrewWalker.cs:114-125`), `OrderShootCar`'s `is not CrewCar` refusal (`DemoCrews.cs:2074-2076`),
  `PutRoundIntoTin` and `TinHole` and `CarWith` (`DemoCrews.Combat.cs:1725, 1740`),
  `CrewOverlay._cardPlantCar` (`:930`, cleared at eight sites), the `AnchorOf` row (`:2043`),
  `GivesWayTo` (`CrewCar.cs:206-213`), and the `CrewBike` path, which is untouched. The bomb path
  is **already** `RoadCar`-typed (`DemoCrews.Bomb.cs:95, 161`, `Explosion.cs:32`,
  `CrewBomb.cs:315`) and needs nothing.
* **Under fire the transfer halts and the escort fights (C3).** The first round into the tin flips
  the carriage to `Halted`: the car `StandDown`s (Derelict, driven round) and the escort is
  unseated beside it as a `CarriageEscort` — a pair with its own brain, modelled on
  `PoliceBeat.ReadProvocation` (`PoliceBeat.cs:323-340`), which the carriage Sics on `Halted`.
  Without this the officers stand and are shot at while doing nothing, because police never
  target on their own (`DemoCrews.Combat.cs:603`).
* **A round into police tin is a shot at officers.** `PutRoundIntoTin` on a car belonging to the
  force raises `SwarmGrade.ShotsFired` and marks `ShotAtOfficer` the way a shot at an officer's
  unit does today — the existing gate misses it because a crew on `CarMark` carries no
  `TargetUnit` (`PoliceDispatch.cs:236-246`). Contract: **a round into a police car raises the
  swarm exactly once.**
* **The prisoner's jeopardy is capped, not per round (C4).** `SHOOT IT UP` is not a burst; it
  fires until the car wrecks or the order lifts, and a `RoadCar` never wrecks from bullets. So:
  (a) on `Halted` every shooter's `CarMark` is **cleared** — the fight becomes men against men,
  which is what ruling 3 describes; (b) the occupant roll is taken at most **once per second of
  fire on that car**, and at most `MaxOccupantRolls` times in one engagement; (c) the chance is
  derived from a measured number, not guessed: ROAD-003 runs the halted engagement in the harness,
  counts rounds into tin before the escort is wiped, and sets the constant so that the prisoner
  dies in roughly one attempt in six. Ruling 5 is a risk, not a tax.
* **Escort wiped** → `Freed`: unseated beside the car, unarmed, `FreedFromTransfer`, the case open
  with `Resisting`. New is only that the car need not be wrecked.
* **Escort wins** (`StreetAlarm.QuietFor` 20 s) → they radio for a fresh car
  (`CollectCustodyCars`); none in the city → the escort walks him the rest of the way. The walk is
  bounded: over about 250 m the carriage waits for a car instead, because a cross-city foot march
  in the 10x city outlives `TransferPatience` (300 s) and would time out to the cells anyway. The
  backstop reads "walking", not "teleport back to the cells".

### 3.4 The roadblock (ROAD-004)

* Order `BLOCK THE ROAD HERE` on an outfit car with a road point under the pointer: the car drives
  there and stands **across** the lane, yawed 90°, spanning both lanes of a two-way road. It is
  registered as a static occupant the way `StoodCar` is, **and** its measured body is entered in
  the fixed walking ledger — the `is StoodCar` skip at `WalkObstacles.cs:621` assumes both current
  producers do that, and a player-placed block would be a third that does not (M1). Its driver
  stays at the wheel.
* **The transfer reverses out of a barricade.** The existing jam ladder is gated on
  `!InQueue && toEnd > 30f` (`RoadCar.cs:2112-2113`), so a block near a junction never unlocks it
  and the transfer would stand silently until `TransferPatience` sent him back to the cells — a
  no-fight "cancel the trial" button. A carriage car facing a full-width static occupant therefore
  reverses regardless of `toEnd` and re-routes, because that is what a police driver at a
  barricade does. Under fire, §3.3 applies and it halts for good. **The block buys seconds; the
  guns stop the car.** Contract: **a blocked transfer either re-routes or halts, and never times
  out to the cells.**
* `MOVE ON` clears it; the static occupant and the walking-ledger entry are dropped with the pose.
* **The route ahead is on the map.** `RoadCar.Route` is a real next-hop table from
  `LaneNet.RouteToward` (`RoadCar.cs:278, 810`), so the turf map draws the transfer's remaining
  route as a dashed line. It is cached on the `RouteTo` edge and invalidated on re-route, never
  walked per frame — `_next`/`_via` are cleared constantly (`:424, 869, 1045, 2552`) and the city
  is 10x.

### 3.5 Reading it (ROAD-005)

* Banners in the custody voice: "THE PRISONER IS WALKING TO THE CAR", "THE TRANSFER IS UNDER
  FIRE", "THE ESCORT IS OUT OF THE CAR", "THE PRISONER IS DEAD IN THE CAR", "THE ESCORT IS WALKING
  HIM THE REST OF THE WAY", "HE IS AT THE COURTHOUSE DOOR".
* Wire: `LawWire.Killed`, `LawWire.TransferHalted`, `LawWire.WalkedIn`.
* THE LAW tab INSIDE row reads the carriage stage in words: "walking to the car", "in the car to
  the court", "at the courthouse door", "in the van out of town".
* Turf map: men on foot are ordinary crew glows; the car keeps the big mark; the route ahead is
  the dashed line.
* A prisoner in the carriage keeps his marker and focus and refuses orders with
  `InCustodyRefusal` (EPIC 34's rule, unchanged).

### 3.6 The night watch (ROAD-006)

**It needs a driver first (M3).** `MonkeyRunner` can order an attack, a car and a drive-by
(`:195, 218, 260`) and nothing else; the only scripted bomb plant in the repo is BlockDemo's
(`BlockDemoMission.CarBomb.cs:290`), and the night-watch rule replaced BlockDemo with
MiniCoreDemo. ROAD-006 therefore includes a MiniCoreDemo mission driver that can plant a charge,
order `SHOOT IT UP` and lay a block.

The scenarios are re-ordered, because a planted charge springs only above `RoadSpeed >= 1.2`
(`CrewBomb.cs:321, 349`) and so can only be laid on a **stopped** car. The bomb reaches a loaded
transfer at the loading kerb or after the gun has halted it — an honest consequence, written down
rather than assumed away.

1. Escort dismount: `SHOOT IT UP` on a moving transfer → halts, escort out and **fighting**, swarm
   raised once.
2. Escort wiped → man freed, alive, unarmed, W2, case open with `Resisting`.
3. Bomb at the loading kerb → prisoner dead, case closed `Killed`, roster struck, precinct charged
   two officers, nobody freed.
4. Bomb on the halted transfer (depends on 1) → same.
5. Bomb on the empty transfer before pickup → two officers dead, prisoner still held, rides
   tomorrow.
6. Roadblock alone → the transfer reverses and re-routes; it never times out to the cells.
7. Roadblock plus fire → halts at the block, then as 1–2.
8. Foot ambush at the station door before seating → sprung; `Freed` refused (never in the car).
9. Foot ambush at the courthouse door on the walk in → freed, trial never held.
10. Escort wins → fresh car, or the walk within the bound; man tried at the threshold.
11. No courthouse in the city → county line, and the walk out of the station is still seen.
12. Save mid-transfer → loads back in the cells, rides again next day (accepted loss, as EPIC 34).

Five seeds each. Everything to Done in Linear.

## 4. Rules that must hold

* **A body rides where the car is.** No stored point stands in for a man's **position** — but the
  custody registry keeps its liveness latch, because that is what stops `Sync` destroying him.
* One death, one door. A man killed anywhere reaches his house's books exactly once, through
  `ReportDeaths`; the pipeline closes the case and never kills.
* Only a wrecked or halted car with the man **seated** frees him. Before seating, an attack springs
  him (arrest side) or frees nobody (transfer side).
* The bomb kills; the gun stops; the block delays. None does another's job, and the constants are
  set so that stays true in play, not only on paper.
* Police fight only when their own brain Sics them; the carriage's escort has one.
* A running man never fires; a prisoner never fires; a seated man never fires.
* Enum values are appended (`CaseOutcome.Killed`; `PrisonStage` untouched; the carriage stage is
  new and unserialized).
* The carriage is not saved; a save during any ride loads the man back in the cells.
* Gear reaches a man only through his lieutenant; a freed man is unarmed.

## 5. Tickets, in order

| # | Title | Depends on |
|---|---|---|
| ROAD-000 | The blast reports its dead: `DemoCrews.KilledByBlast`, `StreetAlarm.Death` per body, decree kept as a fallback with exact `_losing` attribution | — |
| ROAD-001 | One carriage: `PrisonerCarriage` (the 508 measured lines), the liveness latch split from the position, the walks, two measured courthouse points, trial at the threshold, the `Riding` table | 000 |
| ROAD-002 | The bomb kills the man in the car: `PrisonPipeline.Killed`, `CaseOutcome.Killed`, the dead man out of the wreck, wire and archive lines | 000, 001 |
| ROAD-003 | Shooting the transfer: police cars clickable, tin lifted to `RoadCar`, `CarriageEscort` with a brain, tin rounds raise the swarm, `CarMark` cleared on halt, capped occupant jeopardy measured in the harness, freed / radio / bounded walk | 001 |
| ROAD-004 | The roadblock: `BLOCK THE ROAD HERE`, static occupant in both ledgers, barricade reverse, `MOVE ON`, cached route on the map | 001 |
| ROAD-005 | Reading it: banners, wire, THE LAW INSIDE words, map glows | 001–004 |
| ROAD-006 | The night watch: a MiniCoreDemo mission driver, twelve scenarios ×5 seeds, everything to Done | all |

## 6. Acceptance

* **ROAD-000 first, and contracted before anything else ships:** a bomb on a crewed car strikes
  every occupant off his house's roster; a bombed police car costs its sending precinct exactly two
  officers, exactly once, with or without officer prefabs.
* A man in the carriage survives a `Sync`.
* A round into a police car raises the swarm exactly once and marks `ShotAtOfficer`.
* A blocked transfer either re-routes or halts; it never times out to the cells.
* The courthouse kerb-to-door walk is a **logged measured number**, and both ends pass
  `WalkObstacles.ClearSpot`.
* `gangsters_police_tests` gains contracts for every pure question (`ShouldHalt`, `ShouldDismount`,
  `WalkTheRest`, `CanDeliver`, `InJeopardy`), for `PrisonPipeline.Killed` (out of the pipe,
  `CaseOutcome.Killed` noted once, case closed for the last man, `Freed` refused after), and for
  the serialized enum order.
* `gangsters_law_sheet` prints the INSIDE words per carriage stage.
* The night watch passes on five seeds, and the trace shows the walks as walker rows.

## 7. Open constants

| Constant | Value | Why |
|---|---|---|
| `OccupantHitChance` | **measured in ROAD-003**, tuned so the prisoner dies in roughly one attempt in six | the user's ruling is a small chance; a per-round number against an unbounded order was near-certain death. Derived from the counted rounds of a real halted engagement, not proposed |
| `OccupantRollInterval` | 1 s of fire on that car | the roll is on the engagement, not the bullet |
| `MaxOccupantRolls` | 6 per engagement | a hard ceiling, so a long fight cannot grind him down |
| `HaltOnFirstRound` | true, and the halt clears every `CarMark` | a driver who is shot at stops; the fight then belongs to the men |
| `EscortQuietBeforeRadio` | 20 s | the escort re-boards or radios only after the street is quiet |
| `WalkTheRestLimit` | about 250 m | beyond it the carriage waits for a car; a cross-city march outlives `TransferPatience` |
| `CourthouseDoorstep` | about 1.5 m off the face, kerb from the road edge | two points, so the walk exists and is measured |
