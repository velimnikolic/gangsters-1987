# The Empty Counter — design brief (EPIC 38, implemented)

> The second half of the user's ask of 2026-09-04. EPIC 37 (`beat-the-owner-brief.md`) puts a
> beating and a witness killing on the street; this one kills the man behind the counter and
> puts a new one there. It DEPENDS on EPIC 37's CNTR-004 (a body opens a file without a collar),
> CNTR-002 (the person/premises violence split and the `Refusal` signature) and CNTR-005 (the
> shot resolved inside `DemoCrews.Combat`). Carries the save-file version bump, so it is
> deliberately not mixed into the first epic. Revised against two contrarian passes.

## 0. What this is for

A beaten owner who is `Connected` testifies anyway: `PoliceForce.StillTalks:863` returns true
for that trait before fear is even read, and the trait is dealt off the city seed and the
business id (`TerritoryEconomy.Deal:61`). Fear cannot reach him. There is exactly one answer to
a man who cannot be frightened, and the player does not have it.

Killing him has to cost something the player feels, or it is simply the better move. It costs a
murder file (EPIC 37 §3.4), a three-day closure, and a stranger behind the counter who owes the
family nothing personally.

## 1. The user's rulings (2026-09-04)

1. **Killing the owner is a door order**, fired inside, heard on the street; the police answer it
   as a shooting and a file opens whether or not anybody is collared (EPIC 37 §3.4).
2. **The dead owner is replaced.** The shop shuts, then a new man stands behind the counter with
   his own name, trait, nerve, greed and connections.
3. **The door's fear outlives its owner, and the UI says so in as many words.** The block and the
   premises keep what they learned; the racket standing at that door keeps standing. The
   territory unit is the building, and this is that ruling applied to a death.
4. **A paying door is never killed at** — whatever its standing. Short is beaten (EPIC 37), not
   buried.
5. **A shut premises refuses it** ("nobody behind the counter"), through the gateway
   `DoorJobs.TryBuild` as EPIC 37's CNTR-002 establishes.

## 2. What exists and is reused

| Thing | Where | Used for |
| -- | -- | -- |
| The one door list and the split | `Scripts/Territory/TerritoryRacketOrders.cs:376`, `DamageRefusal:212` in `BusinessShutdowns`; EPIC 37's `IsPersonViolence` and `Refusal(type, tenure, inGoodStanding)` | the row and its refusals |
| The gateway | `Scripts/Gameplay/DoorJobs.cs:49` `TryBuild` | the shut-premises refusal, one choke point |
| The visit and the inside callback | `RoadDemo/DoorBeat.cs:648`, the split callback `TryVisitBusiness:694`; the body is `SetActive(false)` inside `:1107` | the shot happens inside or not at all; no flash on a hidden man |
| The street job | `RoadDemo/CrewJobs.cs:248` `Work`, the `Sicced` fall-through `:281`, the `Guard` early return `:257` | the kill beat needs the same early return the beating does, or the crew is Sicced on a rival while the lead is inside |
| The firing pipeline | `RoadDemo/DemoCrews.Combat.cs:1411-1430` — flash, scatter, loudness, `StreetAlarm.Report`, the `HearShot` propagation that makes rivals draw `:1424`, `CrewGore` | the shot inside goes through EPIC 37 CNTR-005's shared resolution, not a bare `StreetAlarm.Report` from `CrewJobs` |
| The alarm | `RoadDemo/StreetAlarm.cs:173` — loudness is **metres**, floored at 5 (`:192`); a pistol is 45 m (`CrewArms.cs:109`); the police floor their own hearing at `Earshot` (`PoliceDispatch.cs:65`) | the bang; "muffled" has no mechanical meaning and is not claimed |
| The death's fear | `RoadDemo/TerritoryRuntime.OnStreetDeath:913` files `Killing` (impact 40, `TerritoryFear.cs:154`) at severity 1.3, attributed by `AttributeRecentViolence:966`; the event is built with **no `BusinessId`** (`:936`) so the premises gains nothing | §3.2: one event, with the door named |
| The docket | `Scripts/Police/CourtCase.cs` — `Witness.BusinessId`, living testimony and distinct `BodyEvidence`; `PrisonPipeline.AttachOpenComplaints` requires one or the other | what killing the complainant actually removes without erasing the new body |
| The witness sweep | `RoadDemo/WitnessWatch.cs` — `Withdraw`, `LawWire.WitnessKilled`, the "WILL NOT BE GIVING EVIDENCE" banner | the complainant's death, printed as a witness's death already is |
| The law sheet and map | `Scripts/Police/LawSheet.cs:323`, `TurfMapHud.cs:3127`/`:3168` | a dead complainant already reads "dead" and drops off the map |
| The owner deal | `Scripts/Territory/TerritoryEconomy.cs:61` `Deal(citySeed, businessId, forced)` — pure hash; `TerritoryRuntime.Collection.cs:392` **caches it per business**, and `StillTalks:863` reads that cache | the successor, and the cache that must be invalidated |
| The name draw | `Scripts/Business/BusinessOwners.cs:95` `UniqueName(takenNames, …)` — a city-wide uniqueness set, "draw order per owner is FROZEN" | the trap: a successor's name cannot come through this at runtime |
| The directory | `Scripts/Business/BusinessRegistry.cs:410` `RegisterOwner` (returns the EXISTING record for a reused id AND logs a `problems` row, `:414`), `SetOwner:436` → `OwnerChanged`; `BusinessIdentity:113` is documented as the ONLY minter of business identities | the successor needs a properly minted new id |
| The file | `Scripts/Save/CampaignFile.cs:150` — `Version` is already **2**; owners are not saved at all, the directory is re-dealt on load | the generation is what the file carries |
| The shutdown | `Scripts/Business/BusinessShutdowns.cs` — `DurationOf:54`, `RepairPriceOf:60`, `Shut:169` (an unrefused cause OVERWRITES the active entry, `:190`), `DamageRefusal:212`, `BusinessRepair.Try:350`, `Line:386` | a three-day closure with no bill, under EPIC 37's two guards |
| The card | `RoadDemo/BusinessMarker.OverlayLine`, the block file's door row, `LawWire` | where ruling 3's sentence is printed |

## 3. The model

### 3.1 KILL THE OWNER on the book (EMPT-001)

`OrderType.KillOwner`, appended to the enum, its `OrderSpec` row placed in the Violence block of
`OrderTable.Specs` (the ledger's draft reads `Specs` order). `Kill` cannot be reused: it names a
man by `TargetCharacterId` and an owner has none. Spec: Violence, Point, 8 h,
`JobResolution.Street`, Combat 6, heat 12 (as `Kill`). `IsPersonViolence`, `NeedsDoor`,
`IsHostile`, `OrderEffects.Built`.

Refusals: our own door; **any** paying door, whatever its standing (so the `inGoodStanding`
argument is ignored for this one type); a shut premises, at the gateway. Row at the foot of the
violence, above SIT ON IT:

    KILL THE OWNER — "he rang · nobody rings twice"

Both `OrderType` sweeps from EPIC 37 CNTR-001 cover it. `RackTests` gets the label and both
refusals.

### 3.2 The shot inside (EMPT-002)

`CrewJobs`: the lead goes in exactly as the beating does, **with the same early return** so the
crew outside is not Sicced on a rival for the eight seconds he is inside, and the act fires from
`DoorBeat`'s inside callback, never a positional phase test.

Inside, one round is resolved through EPIC 37 CNTR-005's shared path in `DemoCrews.Combat`: the
report at a pistol's own 45 m, the `HearShot` propagation that makes rivals draw, the gore, and
the flash suppressed because the shooter's body is inactive. A bare `StreetAlarm.Report` from
`CrewJobs` would give a killing with no flash, no blood and no reaction anywhere — three
decisions where there should be one. Then `StreetAlarm.Death(door, DeathOf.Civilian)`.

The police answer as they answer any shooting: the incident opens at the door, `GuiltyNear` has
the crew for 150 s inside 45 m if they stand there, and EPIC 37 §3.4 opens the murder file even
when they are gone — directly, with no `CallOut` and no collar, so no other crew of ours is
booked for it. `TheDeed()` already returns `Murder` on a civilian death (`Arrest.cs:648`), so a
collar at the door is a murder charge with no new code.

**The dead complainant.** `WitnessWatch.OwnerKilled(businessId)` walks `PrisonPipeline.Cases` for
every OPEN case naming that shop's owner as `Complainant` (match `Witness.BusinessId`) — **every
case, including a rival house's**: the man is dead, he cannot testify for anybody, and a docket
that kept him alive on someone else's case would be a lie about the same fact. Standing goes to
`Dead`, `LawWire.WitnessKilled` files, the existing banner prints.

**What that actually removes, stated plainly.** A held prisoner's case with no evidence is
dismissed on court day. The commoner shape — an open complaint with no defendants — may be
folded as a COUNT on the next man of that house taken within 14 days. EMPT-002 makes that require
actual evidence: a living witness or a body's explicit evidence flag. Killing the complainant
therefore clears the old extortion complaint without also deleting the murder file created by
the body. Past 14 days a no-defendant file is removed from the live docket and save entirely.

**The fear.** One event, not two. `OnStreetDeath:913` already files the killing at severity 1.3;
a second `RecordFear(Killing, 4)` would be 160 on top of 52 through a private method, over the
cap of 100. But that event is built with **no `BusinessId`** (`:936`), so a murder at a shop's own
counter frightens the block and leaves the premises untouched — which would make EPIC 37's
punch (60.75 at the door) more frightening than this killing (0 at the door). The one-argument
fix: when the death happens at a business's door, `OnStreetDeath` names the business on its
event. 40 × 1.3 = 52 at the premises plus the block share, comfortably over the beating and over
`TestifyFearCap`. Scope it to a death AT a door, not to every street death.

`ShutBusiness(businessId, BusinessShutdownCause.Death)` — 72 h, repair price 0, under EPIC 37's
two guards: a person closure does not displace an active premises closure, and a zero-price
closure cannot be repaired. `DurationOf`, `RepairPriceOf`, `BusinessShutdownText.Line`,
`DamageRefusal` and the repair row all gain the case; the copy is fitted to `Line`'s own format
("closed - X - reopens in N days"), which is a two-way ternary that prints "smashed up" for a
Bomb today and is corrected for all four causes in the same edit.

### 3.3 The successor (EMPT-003)

Four seams, and two invariants without which this ticket silently rewrites every existing seed.

* **`Deal` takes a generation.**
  `TerritoryOwnerProfile.Deal(citySeed, businessId, generation, forced)`, the generation folded
  into the hash. **Invariant 1, with a test: `Deal(seed, id, 0, forced)` must return
  bit-identical values to today's `Deal(seed, id, forced)`.** A generation folded in
  unconditionally changes trait, nerve, greed and connections for every owner in every existing
  seed, rewriting the racket layer and breaking `EconomyTests.cs:269`, `RoundTests.cs:91`,
  `PaperCity.cs:453`, `HouseMindTests.cs:548` and `WageTests.cs:720`.
* **The cache is invalidated.** `TerritoryRuntime.Collection.cs:392` memoises the profile per
  business and `StillTalks` reads it. A generation change drops that entry, or the dead man's
  nerve answers for the living one.
* **The name is a pure hash, not a draw.** `BusinessOwners.ForSite:95` draws under a city-wide
  `takenNames` uniqueness set whose draw order is documented as frozen — state that does not
  exist at runtime, and that at load is rebuilt from generation-0 names before generations are
  replayed. Two owners killed in one campaign would swap names across a save. **Invariant 2,
  with a test: the successor's name is a pure function of (citySeed, siteId, generation) with no
  `takenNames`**, collisions accepted or resolved by the hash itself. `SaveTests` replays two
  generations in both orders and asserts the same two names.
* **The id is minted, not concatenated.** `BusinessIdentity` (`BusinessRegistry.cs:113`) is
  documented as the only place business identities are made, and `RegisterOwner` returns the
  existing record and logs a `problems` row when an id is reused. It gains
  `BusinessIdentity.Owner(siteId, generation)`; `SetOwner` then fires `OwnerChanged` for the card
  and the block file.
* **It survives a file.** `CampaignFile.Version` is already 2, so this bumps it to 3 and adds a
  per-business generation list; a version-2 file loads with every generation zero. The loader
  replays each generation through the same pure deal, so the file stores one small integer per
  killed owner, not a man.

The generation is bumped **at the kill**, not when the shutdown expires: a Repaired-before-Expired
race would otherwise decide who is behind the counter. The shop stands shut for three days with
the new man's name already on it, which is what a death in the family looks like.

Nothing else about the business changes (ruling 3): the racket relationship at that door and the
door's fear both stand, so there is no "forget" API to write and nothing to migrate.

### 3.4 The door remembers (EMPT-004)

Ruling 3 needs words on the screen, or the player learns the rule by being wrong for a week.

* The business card (`BusinessMarker.OverlayLine`) and the block file's door row print, for any
  business whose generation is above zero: **"NEW MAN AT THE COUNTER · the street's memory of us
  here is his to inherit"**.
* The wire, on the day the shop reopens: "X reopens under Y — the door pays as it paid".
* `Docs/racket-collections.md` states the rule in the same words: fear and standing belong to the
  door, not to the man behind it.

### 3.5 Tests, docs, close (EMPT-005)

`PoliceTests`: a dead complainant leaves every case that named him, ours and a rival's; a plain
case with no willing witness is not folded, while an evidenced body remains one murder count; a
held prisoner's empty case is dismissed; unanswered files expire and police crossfire is not
misattributed.
`BusinessTests`: invariant 1 (generation 0 is bit-identical to today); the generation deal gives a
different trait and nerve; the cache is invalidated; the three-day closure carries no bill and
does not displace an active premises closure. `SaveTests`: a killed owner's generation survives a
file; a version-2 file loads with every generation zero; two generations replayed in either order
give the same two names. `RackTests`: the row and its two refusals.
`Docs/racket-collections.md` and `Docs/ledger-law-sheet.md` updated; memory updated.

**Whoever implements these tickets moves them, and this epic, to Done in Linear when the work is
finished.**

### 3.6 Not in v1

* An owner body: he is killed behind a counter nobody sees, like the beating (EPIC 37 ruling 1).
* The shop closing for good, or changing trade. It reopens as itself.
* Rival houses killing owners — parity is EPIC 25.
* A successor who remembers who killed his predecessor as a personal matter. The door remembers;
  he does not.

## 4. Rules that must hold

* One fear event per act, and it names the door it happened at.
* The successor is a pure function of (city seed, site, generation), computed identically at the
  kill and at load, and generation 0 reproduces today's numbers exactly.
* Business identities are minted by `BusinessIdentity`, never concatenated at a call site.
* `OrderType` and `BusinessShutdownCause` are appended, never reordered; every switch over the
  cause gains the new value, including the refusal and the repair row.
* The act fires only from the visit's inside callback, and the crew outside is not Sicced while
  the lead is in there.
* Every shot in the game is resolved in one place.
* A killing at a paying door is refused, at any standing.

## 5. Tickets, in order

| # | Ticket | Depends on |
| -- | -- | -- |
| EMPT-001 | `OrderType.KillOwner`, the row, the two refusals | EPIC 37 CNTR-002 |
| EMPT-002 | The shot inside, the dead complainant on every case, the un-folded count, the death's fear at the door, the three-day closure | EMPT-001, EPIC 37 CNTR-004 and CNTR-005 |
| EMPT-003 | The successor: the generation in the deal (invariant 1), the pure name (invariant 2), the cache, the minted id, the file at version 3 | EMPT-002 |
| EMPT-004 | The door remembers: the card line, the wire, the doc | EMPT-003 |
| EMPT-005 | Contracts, docs, memory, everything to Done | all |

## 6. Acceptance

    unity command gangsters_police_tests --json     # the dead complainant, the count, the dismissal
    unity command gangsters_business_tests --json   # invariant 1, the successor, the cache, the closure
    unity command gangsters_save_tests --json       # the generation survives, both replay orders agree
    unity command gangsters_rack_tests --json       # the row and its refusals

On MiniCoreDemo, one seed, watched: a `Connected` owner rings the precinct, the crew is told KILL
THE OWNER, the man goes in, one shot is heard with the men outside standing still, "X WILL NOT BE
GIVING EVIDENCE" comes up, the shop reads CLOSED for three days, a murder file is on the docket by
morning with the pavement on it, the premises is more frightened of us than a beating would have
left it, and when the shop reopens a different name is on the card with the line that says the
street's memory came with the building.
