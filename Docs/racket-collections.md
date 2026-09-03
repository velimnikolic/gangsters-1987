# The racket, collected

How money gets from a shopkeeper's till into the outfit's safe, who carries it, and what
the books say about a door along the way.

Built for GAN-224 (the mechanics) against the seam GAN-225 (the block file) reads, and
rewritten by GAN-262 and GAN-273 (the collector and up to two escorts form a separate
command node and autonomous street detail).

## The one rule under all of it

**Money walks.** A door's take is not money. It becomes money in three steps, and every
surface prints them in that order and never sums them:

| figure | what it is |
|---|---|
| `OWED AT THE DOORS` | dues that have accrued and that nobody has been to fetch |
| `IN THE BAG` | a collector is carrying it, on the street, right now |
| `BANKED THIS WEEK` | it reached the front and went into the safe. The only real one. |

`NET OFF THIS BLOCK` is banked minus wages, both over the same week.

The second rule: **the sim never opens a demand in the player's name.** Automatic
behaviour here is COLLECTION only. Asking a man for money is an order the player gives.
The comment on `TerritoryRuntime.DriveRivalDemands` is where that rule is written down.

## The round ledger

`TerritoryRoundLedger` (pure, `Assets/Scripts/Territory/TerritoryRounds.cs`) owns what a
round IS, for every house. It has exactly six verbs:

| verb | what it does |
|---|---|
| `Open(house, crewId, collectorId, blockId, kind, stops, at)` | starts a walk over stops the caller chose |
| `Arrive(round, at)` | the men are at this stop's door (false when they are already through it) |
| `Settle(round, inputs, hour)` | the hand goes out: the roll, the policy and archetype scales, the wire slip, the dues, the two-misses lapse |
| `Advance(round, at)` | on to the next door, or turn for home |
| `Bank(round, hour)` | files `RoundBanked` and answers the sum — **the caller** puts it in the safe |
| `Abandon(round, hour)` | files `RoundLost` if the bag had anything in it |

`TerritoryStopInputs` is everything the ledger is allowed to know about a door: open,
owed, the owner, the protector's fear, the block's fear, the crew's policy, the
lieutenant's archetype, the city seed and the day. `TerritoryStopSettlement` is what
comes back: paid, owed, missed, outcome, excuse, the fear left, the heat, and whether the
arrangement lapsed.

**Money is computed in the ledger only.** Nothing outside `TerritoryRounds.cs` may write
a round's `Carried`; `gangsters_scenario_capture_audit` scans the whole tree for it.

Two callbacks carry the world back out: `Settled` (the runtime files the fear, the heat,
the reputation, the practice and the word over the street) and `Ended` (the bag is
dropped, the money moves to that house's safe, the event is published).

## The two clocks

The same round moves under one of two clocks, and both call `Arrive` → `Settle` →
`Advance` → `Bank` in that order, so a round is worth the same money either way
(`gangsters_round_tests` asserts it to the penny).

* **The street.** `TerritoryRuntime.Collection.cs`: `crews.MarchTo` the stop's doorstep,
  `NoteRoundArrival` → `Arrive`, `DoorBeat.VisitBusiness(whenInside: Settle,
  whenOut: Advance)`, `BagCarry.Give/Drop`, the front reached → `Bank`. The runtime keeps
  a `RoundBody` beside each round: who carries the bag, and where each stop is on the
  ground.
* **Paper.** `TerritoryPaperClock`: travel between doorsteps priced by
  `OrderMath.TravelHours` (on foot or by car per `CrewKit.HasVehicle`), a stop worth
  `TerritoryRoundConfig.PaperStopMinutes` (2), then the same three calls and `Bank` at the
  front. No bodies at all — this is how a house the city has not stood up still collects.

The line and its bag detail have independent `CrewQuarters` keys. Between rounds the
bag detail is inside the headquarters; the scheduled round first calls `BringOut`, waits
until its living men are through the door, then starts the route. The exit is reasserted
if roster sync stations the detail again and fails after 20 real seconds instead of
wedging the crew. A standing-round slip and `RoundOut` news are filed only after the
physical round opens, never merely because that exit was requested. Banking stations it
inside again. An idle detail whose billet is missing is sent straight home; the 20-second
street grace applies only after a real headquarters threat. Its only unscheduled outing
is autonomous headquarters defence, checked four times a second: it comes out when a
rival who is actually outside is on the home block and returns after the block has stayed
clear for 20 seconds. Men crossing the door still count as presence through
`DoorBeat.Active`; men already held inside another premises are not street threats.

Doorsteps come from `ITerritoryGeography.TryGetDoorstep(businessId, out TerritoryPoint)`,
which the geography answers from the approach point each business site published.

## Two clocks in a day, one day apart

There are two day counters and they are one apart.

* The **city clock** counts from **0**. `TerritoryRuntime.lastGameHour` is
  `clock.Day * 24 + clock.Hour`.
* The **campaign** counts from **1**. `OutfitDirector` does `today = clock.Day + 1`.

`TerritoryDoorDispatch.Day` adds the +1 so a door slip and an incident filed on the same
afternoon carry the same number. Until 2026-09-02 it did not, and every door slip sorted
under yesterday's incidents on the ledger's wire. `HourOfDay` beside it is what puts
`DAY 3 · 09:34` on the stamp.

## The duty

`Character.Duty` is standing work a man is marked for, over and above the crew he is in.
`Duty.Collector` heads the bag node and `Duty.Escort` marks its guards.

**One bag to a crew** (GAN-262). `RosterOps.SetDuty` is the only way to set one, and the
rules are, in order:

1. no such man → refused
2. taking it OFF is always allowed
3. not a `Rank.Hood` → "only a hood carries the bag"
4. not `AssignmentKind.Crew` → "he has to be in a crew"
5. otherwise he leaves `HoodIds` and becomes `Crew.BagId`; the old collector returns
   to the line

The collector and escorts remain members of the crew through `Roster.CrewOf`, but are not
line members. `RosterOps.PostEscort` moves a line or reserve hood into `EscortIds` (maximum
two); `PullEscort` returns him to `HoodIds`. Every move to another assignment clears the
node duty, including `AssignToCrew`.

`RosterOps.CollectorOf(roster, crewId)` answers the one man marked, `CollectorsOf` the
same man only while he can actually walk (a man in a hospital bed is on the books and not
on the round).

### Who the lieutenant picks

`CollectorChoice` (pure, no UnityEngine) is how a lieutenant hands the bag out himself:

* `Fitness(man)` = half-steps of Streetwise + Persuasion + Awareness. Combat is not on
  the list: the bag is carried, not fought over.
* `PickRank(organizationHalfSteps, candidates)` — the index into the fitness-sorted list
  the lieutenant reaches for. 8+ → the best man, 6–7 → the second, 4–5 → the middle,
  2–3 → the worst. A good organizer picks well; a poor one does not.
* Ties break on lower Greed, then lower id. Deterministic, no draw anywhere.

`RosterOps.NameCollector(roster, crewId, hoodId)` is the boss's word and sets
`Crew.BagNamedByBoss` with `Crew.BagNamedId`; `TakeOffTheBag` is the boss's "nobody"
(`BagNamedId = -1`); `LetLieutenantPick` clears both and gives the job back to the
lieutenant. `TendCrewBag` / `TendCollectors` fill a gap: a crew with ground on the paper
and nobody on his feet carrying its bag gets one handed out — **unless the boss has ruled
on that bag**.

**A ruling outlives a sentence, not a man.** The named man keeps the bag through a cell or
a hospital bed — that is the point of naming him. A named man who is DEAD, or who now
answers to another lieutenant, is not a ruling any more: it is spent, the flag clears and
the lieutenant hands the bag out again. Without that check one death left a crew with a
standing order naming a corpse and its ground was never collected on again.
A moved collector leaves the old node empty and arrives as an ordinary hood; the old
escorts return to that crew's line.

It runs at three moments: `PersonnelDirector.AssignBlockResponsibility` (ground without a
collector is paper nobody collects on), the day tick (`CampaignRunner.DayTick`, after the
books and before the closing passes, filing an `IncidentKind.BagHanded` line), and
LET HIM PICK on the block file.

### The bag man is not in the line

`DemoCrews.Sync` deals the first four ACTIVE `HoodIds` onto the street. The collector and
escorts spend none of those places: they are a **unit of their own**
(`Unit.IsDetachment`, `Unit.Parent`, the same `CrewId`) and billet inside headquarters
between rounds. `DemoCrews.BagUnitOf(crewId)` finds them;
`UnitOfCrew`, `TerritoryRuntime.FindUnit`/`FindPlayerUnit`/`TryGetCrewNode` and every
surface that LISTS or PICKS a crew (`CrewOverlay`, `CrewBar`, `RacketProbe`,
`DemoCrews.Boarding`, the tactical projection) return the parent and skip him. Everything
that samples BODIES — presence, arrivals, combat, avoidance — sees him like any man,
because he is in `Units`.

So a Thursday round brings the bag detail out and leaves the line standing on its block.
`CollectionRound.Walkers` is the unit that walks: the bag unit where the crew has one,
else a rival crew's own line where no rival detachment is projected. Our line never
accepts a collection round. An order to the CREW (`DropPendingApproaches`) no longer
kills the bag detail's round.

Two rules hold the handover together. `EnsureCollector` requires the carrier to be one of
the men **of the unit walking the round**, not merely alive — the unit object is reused
between deals, so a carrier dealt back into the line would otherwise have gone on settling
doors from wherever the crew was standing. And **the bag falls with the man**: a living
escort in the same detail takes over. With nobody standing, `BagOnGround` retains the
take without a timer; right-clicking it with a line selected offers TAKE THE BAG, and a
rival standing over it can take it first.

## The schedule

`TerritoryCollectionSchedule` (pure, `Assets/Scripts/Territory/TerritoryEconomy.cs`):

* `DayOf(blockId)` — 0..6, FNV-1a over the block's own id. Never `string.GetHashCode()`:
  that is not stable across runs, and a collection day that moved between sessions is an
  arrangement nobody can plan around.
* `OpeningHour = 9` — rounds go out from nine.
* `ShouldSend(dayOfWeek, hourOfDay, blockId, owed, hasCollector, roundRunning, sentToday)`
  — every one of the six has to hold.
* `IsLate(owed, weeklyRate, day, lastCollectedDay)` — a week's money owed, or over a week
  since anybody collected. `DaysLate` counts from the seventh day.

`TerritoryRoundScheduler.Tend(house, day, dayOfWeek, hourOfDay, ledger, submit)` (pure)
walks one house's `BlockResponsibilities` and hands each block whose day it is to the
caller's `submit`. `TerritoryRuntime.TendScheduledRounds` runs it on the Business tick
(every four game hours) for **every** house and submits a `CollectDuesCommand` through the
**gateway**. A refused command (the crew is fighting, in a car, wiped) is not recorded as
sent, so the next tick asks again the same day. A round that IS taken files `RoundOut`;
only the player's own is called over his wire.

## What the wire carries

`TerritoryDoorNews` gained four money kinds: `PaidShort`, `Missed`, `RoundBanked`,
`RoundLost`. `TerritoryDoorDispatch` carries `Amount`, `Excuse`, `BlockId`, `Stops` and
`Short` beside them, and `TerritoryRacketLedger.FileMoney` / `FileRound` file them.

* A door that pays **in full** files nothing. The round's own slip covers it, and one line
  per paying door per week would bury the book in good news.
* `SettleStop` files the short and the miss, with the sum and the owner's story.
* `Bank` files `RoundBanked`; `AbandonRound` files `RoundLost`, and only when the bag had
  something in it.

The words are `TerritoryStandingVocabulary.Describe(dispatch, shop, block)`. The excuse
words (`"A BAD WEEK"`) live there too, so the toast over the street and the slip in the
book cannot word the same excuse differently.

Two fixes went in beside this:

* **One visit, one slip.** `TerritoryRacketLedger.Approach` takes `announce`. A walk that
  carries a demand or a threat moves the state silently — the answer, seconds later, is
  the news. A bare GO TO THE DOOR still announces itself.
* **The strip stopped starving the incidents.** `StreetHud.DoorLinesKept` is half the
  strip, not all of it.

## Where a door stands

`TerritoryDoorStandings.Of(...)` is the pure read model behind the block file's WHAT
TRADES HERE column. First match wins, and the order is the priority a boss reads by:

| | |
|---|---|
| `Shut` | the closure note |
| `Rival` | `"<house> holds it"` |
| `Refused` | `"refused us · day N"` |
| `Wavering` | `"wavering · not visited since day N"` |
| `Late` | `"owes $N · N days late"` |
| `Short` | `"short last round · <excuse>"` |
| `Paying` | `"pays us · $N owed · collects thursdays"` |
| `Unvisited` | `"nobody has been to see him"` |
| `Other` | empty — the page falls back to its tenure phrase |

`SeverityOf` is 2 for red (Refused, Late), 1 for amber (Wavering, Short), 0 for the rest.
The **ink is not decided here**: the page colours by kind.

## The two block orders

`TerritoryShakedown` (pure) says who a block order reaches:

* `WorthAsking(state, ours)` — Unaffiliated, Approached, Hesitant, Intimidated. What
  SHAKE DOWN THE BLOCK walks.
* `IsHoldout(state, ours)` — Defiant or Hesitant. What LEAN ON THE HOLDOUTS walks.
* `ThreatenAfter(verdict, policyLevel)` — Strict and Brutal crews put hands on a door
  that just said no or would not say yes, while they are still standing in it. Lenient
  and Normal walk on and file the refusal.

A waverer is on both lists. That is deliberate: the player chooses which of the two he is
doing.

**Our own premises is on neither.** A shop the family holds the deed to has no protection
to sell, so its standing sits at Unaffiliated for ever and a block sweep would otherwise
walk the crew up to its own headquarters and ask for money. `ours` is
`BusinessDeeds.GangOf(id) == PlayerGangId`, supplied by the scene edge because a deed is
the business layer's fact and not the territory layer's, and read in exactly one place
(`TerritoryRuntime.OursByDeed`) so the key that offers the order and the order itself
count the same doors. It drops out of the block file's standings column too — our own door
gets the tenure phrase that says whose it is, not "nobody has been to see him".

`ShakeDownBlockCommand` and `LeanOnHoldoutsCommand` go through
`TerritoryRuntime.WalkTheDoors` (`TerritoryRuntime.Shakedown.cs`), which reuses the
collection round's machine — the route, the arrival, the abandon — because a walk down a
block's doors is a walk down a block's doors. `CollectionRound.Kind` says what happens at
the counter, and `SettleDoor` branches on it.

The **whole crew** walks a shakedown: the men in the doorway are the argument.

## The seam

`Assets/Scripts/UI/BlockRacketSeam.cs` (GAN-225 owns it) is the line between the racket
and the block file. `TerritoryRuntime.Seam.cs` implements both halves and installs them at
territory init; a bench scene with no city keeps the stub, and the page says
"(stub figures)" on its own face.

`Refusal(key, crewId, blockId)` answers the executor's precondition **without executing**,
in the same words the executor would refuse with — so a lit key that then refuses is
impossible. The refusal strings are named once, in `TerritoryRuntime.Shakedown.cs`.

`Version` mixes the racket's own version with a counter bumped on every round start, stop,
settle, duty change and policy change.

## Where the player touches it

| surface | what it offers |
|---|---|
| block file (BLOCKS sheet) | the six orders, POLICY, WHO WALKS THE DOORS, the standings column, the money figures |
| **turf map, right-click a block** | SHAKE DOWN / COLLECT THE TAKE / LEAN, over the block whose label is up |
| block file | WHO CARRIES THE BAG — the responsible crew's own roll, each man with his bag-fitness in stars and whether he walks the street, plus LET HIM PICK and NOBODY |
| chain of command | THE BAG beside the Don's DETAIL or below a lieutenant; escort-only PLACE / TO BAG / PULL leaves, and OFF THE BAG on its head |
| personnel card | collector and escort posting keys, and the round under them |
| roster row | the COLLECTOR / escort duty mark |
| the wire (rail + street strip) | short, missed, round out, banked, lost |
| the street | a toast when a standing round goes out, and when a bag is lost |

The map's block menu opens on the block `HoverBlock` has a **label** up for, so the reader
is always ordering the block he can read the name of. It comes after the door menu: a
right click on a shopfront is about that shop, and only ground with no door under it is
the block itself. Its rows are the shared order table's words and the seam's own
refusals — a refused row keeps its place with the reason in place of its note, never
hidden. With no seam installed (a bench scene) the menu does not open at all, rather than
offering an order the stub would only pretend to carry out.

`RoundOut` is the ninth door-news kind and the only one nothing filed before: a standing
round is the one thing in the racket the player did not order, and without it he learned a
round had gone only when the money arrived — or never, if it did not. A detail still
crossing the headquarters door is not `RoundOut`; the scheduler confirms that daily slip
only after the round ledger contains the opened round.

## Money split

Protection rounds book `DaySheet.IllegalIncome`; robberies, ransoms and recovered ground
bags book `JobIncome`. `DirtyIncome` is their derived sum. Both enter the dirty share of
the safe on receipt; the Finances sheet prints separate Protection and Jobs rows.
