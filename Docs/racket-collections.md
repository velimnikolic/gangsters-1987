# The racket, collected

How money gets from a shopkeeper's till into the outfit's safe, who carries it, and what
the books say about a door along the way.

Built for GAN-224 (the mechanics) against the seam GAN-225 (the block file) reads.

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

## Two clocks, one day

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
`Duty.Collector` is the only member: others (Enforcer, Driver, Guard) are in the design
and are not here because nothing would read them.

`RosterOps.SetDuty` is the only way to set one, and the rules are, in order:

1. no such man → refused
2. taking it OFF is always allowed
3. not a `Rank.Hood` → "only a hood carries the bag"
4. not `AssignmentKind.Crew` → "he has to be in a crew"

Every move that changes who a man answers to clears his duty with it — `Promote`,
`Demote`, `AssignToBoss`, `AssignToPool`, `AssignToFront`. `AssignToCrew` keeps it.

`RosterOps.CollectorsOf(roster, crewId, into)` answers the live, active, marked men of
one crew. A man in a hospital bed is on the books and not on the round.

On the street, `CollectorOf(unit)` prefers a marked man; failing that it falls back to
the old rule (the lieutenant, then the first hood on his feet). A crew with nobody marked
still collects when ordered by hand — the mark is an arrangement, not a requirement.

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

`TerritoryRuntime.TendScheduledRounds` runs on the Business tick (every four game hours),
walks the roster's `BlockResponsibilities`, and submits a `CollectDuesCommand` through the
**gateway** for each block whose day it is. A refused command (the crew is fighting, in a
car, wiped) is not recorded as sent, so the next tick asks again the same day.

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
| personnel card | MAKE HIM A COLLECTOR, and the round he actually walks under it |
| roster row | the COLLECTOR mark |
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
round had gone only when the money arrived — or never, if it did not.

## Not built

* Rival collectors on a schedule ("their man comes Thursdays" on a rival door).
* Duties other than Collector.
* **The detachment** (GAN-224 Part D): a scheduled round still marches the whole crew, so
  a Thursday round pulls every man off the block it is collecting. The design wants the
  collector plus one escort to walk while the rest hold the street. It needs
  `DemoCrews.Detach/Merge` and a `CrewOverlay` picker that skips detachments.
