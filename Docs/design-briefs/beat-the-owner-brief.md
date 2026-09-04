# The Man Behind the Counter — design brief (EPIC 37, implemented)

> Racket and law tracks, joined. Drafted 2026-09-04 from the user's ask: "an option to beat
> only the shop owner, and to be able to kill the witness." Revised twice the same day against
> two contrarian passes and the user's rulings. The owner's DEATH is the second half and lives
> in `Docs/design-briefs/kill-the-owner-brief.md` (EPIC 38), which depends on this one.
> Builds on EPIC 6 (GAN-203 the racket), EPIC 26 (GAN-245 complaint, trial and lawyer),
> EPIC 32 (GAN-294 the storefront), EPIC 33 (GAN-302 the law sheet), EPIC 34 (GAN-315 the
> answer at the door).

## 0. The gap

The ladder at a shop door today is words, then glass:

    GO TO THE DOOR → DEMAND PROTECTION → THREATEN THE OWNER → SMASH IT UP / TORCH IT / ROB IT

Nothing on it hurts the MAN. `ThreatenBusinessOwnerCommand` is a conversation at the counter
(`Assets/RoadDemo/TerritoryRuntime.cs:2569`, through `DoorBeat.VisitBusiness`); the three
wreckings shut the shop and bill it (`Assets/Scripts/Gameplay/OutfitDirector.cs:596`).
`OrderType.Assault` exists in the book but resolves as `Activity.AttackOnARival` — a crew set
on the nearest rival UNIT (`Assets/Scripts/Outfit/Orders.cs:242`). It is not a beating of
anybody.

On the docket a case has two witness kinds a crew may reach
(`Assets/Scripts/Police/CourtCase.cs:103`):

| Kind | On the street | What can be done today |
| -- | -- | -- |
| `Complainant` | the shopkeeper who rang; **no body** (`WitnessWatch.BodyOf`) | THREATEN raises his shop's fear; `PoliceForce.StillTalks:854` compares it to `Verdict.TestifyFearCap` 55 on court morning; a `Connected` owner testifies regardless |
| `Eyewitness` | a `CivilianAgent`, registered in `WitnessWatch` | LEAN ON THE WITNESS (`CrewOverlay.cs:1375`). The card's comment claims killing him is "the ordinary attack order". **It is not.** `Engage`, `Target`, `TakeHit`, `BarrelOn` are typed on `CrewWalker` (`CrewWalker.cs:114`, `DemoCrews.Combat.cs:1564`); the only civilian hit is a round that MISSED a `CrewWalker`, 12 % of the time, damage 1 (`DemoCrews.Combat.cs:1628`) |

So the owner cannot be beaten and the eyewitness cannot be ordered killed. Only death takes a
name off a case (`WitnessWatch.Tick`), and neither reachable kind has a door to it.

## 1. The user's rulings (2026-09-04, do not relitigate)

1. **The beating happens inside, at the counter.** No owner body in v1; the shopkeeper stays a
   name and a door. ("za sad" — a body on the pavement is a later thing.)
2. **The shop shuts one day.** The owner is in hospital: no repair bill, but that day's dues do
   not accrue, which is what the beating costs the family.
3. **Battery is its own deed**, band **10–16**, bail **4,000**. The user's first answer bundled
   the band with the bail; asked again which the band should be, the user left it to the model,
   and 10–16 is the answer that matches the sentence the ruling was written to produce: a
   beating is the graver thing that happened at that door, strictly above an extortion (8–14),
   below a murder (15–25). At 8–14 it merely ties extortion and leads the file only because it
   happened later.
4. **A body opens a murder file without a collar.** Today only the telephone
   (`PoliceDispatch.Complaint.cs:773`) and the collar (`Arrest.cs:421`) open a case, so a crew
   that walks away leaves no docket at all. Fixed here, because SHOOT HIM needs it as much as
   the owner's death does.
5. **A door's fear outlives its owner** (EPIC 38's ruling, recorded because it settles what this
   epic must NOT do): nothing forgets a business's fear or its racket standing.
6. **BEAT is allowed at a door that pays us but came up short or late.** Refused at a door in
   good standing, refused at our own door, refused at a shut premises.
7. **Two epics.** This one is the beating and the witness.

## 2. What exists and is reused

| Thing | Where | Used for |
| -- | -- | -- |
| The one door list | `Scripts/Territory/TerritoryRacketOrders.cs:376` (`Door(...)`), labels `:198`, `DamageRefusal:405`; `Tests/RackTests.cs:697` asserts every label; the `violence[]` loop `:510` asserts EVERY violence type is refused at a Paying door | one new row, and that loop must learn the split |
| The three surfaces | street card `RoadDemo/CrewOverlay.cs:1679` → `FileDoorJob:1900`; map planner `Scripts/UI/DoorMenu.cs:1099` → `FileStreetJob:1181`; the ledger's draft enumerates `OrderTable.Specs` (`PersonnelAlmanac.Orders.cs:129`) | all generic; **but the draft reads `Specs`, not the enum**, so the spec row sits in the Violence block while the enum value is appended last |
| The gateway | `Scripts/Gameplay/DoorJobs.cs:49` `TryBuild` — the choke point every surface files through; maps only SmashUp/Torch to a `damageCause` (`:52`) | where the shut-premises refusal belongs |
| The order table | `Scripts/Outfit/Orders.cs` — `OrderType` (append only), specs `:150`, `ActivityOf:236` with `default: BlockPatrol` at `:270`; `Scripts/Outfit/DoorOrders.cs` — `Refusal(OrderType, DoorTenure):102`, `ViolenceSeverity:78`; `OrderEffects.Built` | the new order type, the person/premises split, and five `OrderType` defaults to sweep |
| The street job | `RoadDemo/CrewJobs.cs` — `Work:248`, `SwingBeat:395` (its lesson: `Done(house, job)` on the ACT, `:431`), the `Sicced` fall-through `:281`, the `Guard` early return `:257` | the beating beat, and the fall-through it must escape |
| The visit | `RoadDemo/DoorBeat.cs:648` `VisitBusiness(...)`; `PhaseOf:902` (**exists**, `VisitPhase.Inside` at `:104`); `Tell` also fires on man-lost `:978`, give-up `:1004`, under fire `:1041`, stay cut short `:1054`; the split callback `TryVisitBusiness:694` already carries a failure path | the act must fire from INSIDE, through that split callback |
| The completion seam | `Scripts/Gameplay/OutfitDirector.cs:596` — job type → `ResolveEscalation` / `ShutBusiness` / `ShopDamage`, business id in hand; today it rings the precinct for NOTHING | the beating's telephone, fear and closure |
| The escalation | `RoadDemo/TerritoryRuntime.cs:1511` `ResolveEscalation(...)`; Assault impact 18 (`TerritoryFear.cs:151`), `MaxSeverity` 4 (`:145`), Public 1.0 / Seen 0.7 (`:119`), block share 0.35 (`:381`), `BusinessFear = own + block` (`:672`) | the arithmetic in §3.3, written down because it is load-bearing |
| The telephone | `RoadDemo/TerritoryRuntime.Collection.cs:471` `MaybeRingThePrecinct` — private, two-arg, always `Deed.Extortion` (`:529`), and it is a **roll**: `ComplaintRoll.Chance` with `(1 − standing)²` (`CourtCase.cs:271`) | a public deed-carrying entry that rings BEFORE the fear is filed |
| The complaint machinery | `RoadDemo/PoliceDispatch.Complaint.cs` — `OpenComplaintCase:773`, the pavement snapshot `SnapshotTheScene:149` / `CopySceneWitnesses:790` (taken when the receiver comes off the hook, `:326`), `TryComplaintCollar:557`, `ComplaintReach` 30 m `:41` | the case a body opens — **without** the collar |
| The collar's suspect | `RoadDemo/PoliceDispatch.Arrest.cs` — `AccusedNear(door, faction):384` takes the nearest crew OF THAT FACTION, not the crew that fired; `_arrestDeed = call.Call.Charge:361`; `GuiltyNear:619` by contrast needs `ShootersSince` | the reason a body must NOT be routed through a CallOut |
| The docket | `Scripts/Police/CourtCase.cs` — living witnesses and the distinct `BodyEvidence`; `PrisonPipeline.AttachOpenComplaints` folds an evidenced defendant-less file inside the 14-day memory window, while `Tried` needs a prisoner | what a murder file is actually worth |
| The witness's body | `RoadDemo/WitnessWatch.cs` — `Register`, `NameOf`, `OrderLean:154`, `TickLeans:179` (reach 4.5 m, patience 120 s, **never repaths**), the death sweep `Tick` | the walk-up SHOOT HIM reuses, and the sweep that takes him off the case |
| The firing pipeline | `RoadDemo/DemoCrews.Combat.cs:1411-1430` — flash, scatter, loudness, `StreetAlarm.Report`, `HearShot` propagation (`:1424`, which is what makes rivals draw), `CrewGore`; `StrayRound:1628`; `CrewWalker.BarrelOn` private `:4046`, the running-man gate `:4038`, Carrying/Drawn `:68`/`:76` | one civilian execution, built INSIDE that pipeline |
| The shutdown | `Scripts/Business/BusinessShutdowns.cs` — `DurationOf:54`, `RepairPriceOf:60`, `Shut:169` (an unrefused cause **overwrites** the active entry, `:190`), `DamageRefusal:212` (refuses only Arson/Bomb-active and SmashUp-on-SmashUp), `BusinessRepair.Try:350` (checks only the payer's gang), `Line:386` (a two-way ternary; Bomb already prints "smashed up"), `ShouldAccrueRacketAt:242`; the repair row `DoorMenu.ClosureOf:236` | a one-day closure that cannot be used to erase a real one |
| Sound / voice | `Assets/Audio/Weapons/punch_2/3/4.wav` (unused); `VoiceLines.OrdWitness`, `OrdKill`, `ForOrder:112` (a default) | the beating, and the two orders' lines |

## 3. The model

### 3.1 The deed and the sweeps (CNTR-001)

`Deed.Battery`, appended after `Resisting`. Band **10–16**, bail **4,000**
(`Docs/economy-prices.md` gains the row — it is the price authority), charge "Assault and
battery", wanted grade as Affray.

**Why 10–16 and not 8–14.** 8–14 is exactly `Extortion`'s and `WitnessTampering`'s band
(`Sentencing.cs:113`), so `PrimaryCharge:136` would put the battery on the front of the file only
by the tie-break "the fresh act leads", and a fresh extortion count tomorrow would demote it
again. A beating is meant to be the graver thing that happened at that door, so the band sits
strictly above extortion and below murder (15–25). Nothing else in the table is disturbed, and
`PrimaryCharge`'s one caller (`Arrest.cs:695`) is a path this feature never traverses anyway —
the band matters for the sentence, which is the point.

`Verdict.BatteryBase = 0.30`, as extortion: a beating inside is one man's word against ours.
§3.4 keeps that true by not handing the court a pavement full of eyewitnesses to an indoor act.

**Two sweeps, not one.** Five `Deed` defaults: `BandLow:108`, `BandHigh:121`, `ChargeFor:209`,
`Bail:228`, `Verdict.BaseFor` (`CourtCase.cs:403`). And five `OrderType` defaults the first
draft never named: `ActivityOf:270` (a missed case banks **patrol** practice, silently),
`LedgerText.OrderLabel:362`, `VoiceLines.ForOrder:112`, `CrewOverlay.DoorJobVoice:1956`,
`PersonnelAlmanac.Orders.DraftedWorth:204`. One contract per enum, failing on a value that fell
through.

### 3.2 BEAT THE OWNER on the book (CNTR-002)

`OrderType.Beating`, appended to the enum; its `OrderSpec` row placed in the **Violence block of
`OrderTable.Specs`**, because the ledger's draft reads `Specs` order and the order would
otherwise appear at the bottom under the influence orders. Spec: Violence, Point, 6 h,
`JobResolution.Street`, Combat 6, heat 4. `Activity.Leaning`. `OrderEffects.Built` true — minds
name their order types explicitly (`HouseMind.cs:184, 220, 288, 506, 516, 678`), so this leaks
nothing to rivals.

**The violence question splits in two, and the signature changes with it.**

* `IsPremisesViolence` — Raid, SmashUp, Torch, Bomb. Today's `IsViolence` renamed at every call
  site; the paying-door refusal ("we do not rob the takings we collect", `DoorOrders.cs:102`)
  keeps guarding exactly these.
* `IsPersonViolence` — Beating (and KillOwner, EPIC 38).
* `IsHostile` and `IsViolence` become the union, so no existing reader loses a case.

`DoorOrders.Refusal(OrderType, DoorTenure)` cannot answer ruling 6: `DoorTenure` has one
`Paying` value (`:12`). The signature becomes
`Refusal(OrderType type, DoorTenure tenure, bool inGoodStanding = true)` — the smallest form; a
new `DoorTenure` value would break the four-way switches. Five non-test call sites thread it:
`PersonnelAlmanac.Orders.cs:460` and `:1001`, `TerritoryRacketOrders.cs:381` and `:397`,
`DoorJobs.cs:49`. Standing comes from `TerritoryDoorStandings.cs:118`. `RackTests`' `violence[]`
loop (`:510`) splits: premises orders stay refused at a Paying door, a Beating is refused only
in good standing, with the note "he came up short" when it is lit.

Beating a Compliant door does not move its standing (`TerritoryRacket.Escalate:740` leaves a
compliant entry where it is) — it files fear and nothing else. Correct: the round is the
collector's business; this is a message, not a renegotiation.

**The shut-premises refusal goes in the gateway, not only the menu.** `DoorJobs.TryBuild:49` is
the choke point every surface files through and it maps only SmashUp/Torch to a damage cause
(`:52`); a Beating filed from the map planner against a shut shop passes straight through today.
The refusal lands there ("nobody behind the counter") and `TerritoryRacketOrders.DamageRefusal`
carries the same words for the menu.

Row on the one list, between THREATEN THE OWNER and SMASH IT UP:

    BEAT THE OWNER — "the man, not the glass · his windows keep, his shop shuts a day"

### 3.3 The beating on the street (CNTR-003)

`CrewJobs.Work`: `OrderType.Beating` → `BeatingBeat`, on `SwingBeat`'s shape, **with an early
return** in the shape `Guard` already has (`:257`). Without it the job falls through to the
`Sicced` block (`:281`) for the whole seven seconds the lead is inside, the four men on the
pavement are set on the nearest rival, `DoorBeat`'s under-fire give-up fires, the inside gate
correctly rejects the act and the order self-destructs on a busy street.

The lead at the door goes in through `DoorBeat`'s **split callback** (`TryVisitBusiness:694`,
the seam `PoliceDispatch.Complaint.cs:641` already uses), not a positional `PhaseOf` test: `Tell`
fires on four failure paths (`:978`, `:1004`, `:1041`, `:1054`) and today returns `None` from
`PhaseOf` only because each removes the call first — an incidental ordering that one refactor
would silently undo. On the failure path the job reports `Failed` and nothing is filed. On the
inside path: three punches off the three wavs, a short cry, then `Done(house, job)` — the deed
is the answer, `SwingBeat`'s lesson.

**Completion order matters and is the fix the second contrarian pass forced.** In
`OutfitDirector:596`, in this order:

1. **The telephone first.** A public `RingAbout(gang, business, Deed)` beside the private
   `MaybeRingThePrecinct`, keeping the roll. It must run BEFORE the fear is filed: after the
   beating the standing is 0.73 and `(1 − standing)²` puts the chance at ≈ 0.06, so a beating
   would deterministically silence its own telephone and the acceptance line "the call comes in
   as WitnessTampering" would fire once in eighteen. Rung first, the chance is the ≈ 0.78 a
   fresh street already has: the man reaches for the receiver with the fear he had a second
   before the punch. The deed is `WitnessTampering` when the shop's owner is a willing
   `Complainant` on an open case against this house (walk `PrisonPipeline.Cases`, match
   `BusinessId`), else `Battery`.
2. **The fear.** `ResolveEscalation(whose, businessId, Assault, severity 2.5, Public)` =
   18 × 2.5 × 1.0 = 45 at the premises plus 0.35 × 45 = 15.75 from the block = **60.75**, over
   `TestifyFearCap` 55. Severity 3 was the first draft's figure and gives 72.9 — which makes a
   punch (one day, no bill) more frightening than a firebombing (67.5, seven days, $5,000) and
   flattens the ladder `ViolenceSeverity` exists to keep. 2.5 puts the beating between Torch and
   Bomb, which is where a beating belongs. `Public`, not `Seen` (51.03, under the cap): the
   pavement did not watch it, but the block knows what happened at that door by morning.
3. **The closure.** `ShutBusiness(businessId, BusinessShutdownCause.Beating)` — 24 h, repair
   price 0, and two guards without which the closure is free:
   * `BusinessShutdowns.DamageRefusal:212` gains an explicit rule: a **person** closure must not
     displace an active **premises** closure. Today an unrefused cause overwrites `Cause`,
     `StartedAt` and `RecoveryAt` outright (`:190`), so a beating two hours after a SMASH IT UP
     would replace a three-day, $1,000 closure with a one-day, free one.
   * `BusinessRepair.Try:350` refuses when `RepairPriceOf(status.Cause) <= 0` ("there is nothing
     to repair"). It checks only the payer's gang today, so buying the premises after a beating
     would let the player reopen it instantly for $0 through PAY FOR REPAIRS
     (`DoorMenu.ClosureOf:236` sets `affordable = Safe >= 0`).
   * `DurationOf`, `RepairPriceOf` and `BusinessShutdownText.Line` gain the case. `Line` is a
     two-way ternary that already prints "smashed up" for a Bomb — the copy is fixed for all
     four causes in the same edit, and the brief's words ("the owner is in hospital") are fitted
     to that method's format, not the other way round.
   * A shut day is a day of no dues (`ShouldAccrueRacketAt:242`). That is the order's price.
4. The wire: "the owner of X was beaten behind his own counter".

Nothing new decides whether he then talks: the fear jump goes through
`PoliceForce.StillTalks:854` against `TestifyFearCap`, and the sheet already prints "may not
testify — frightened". A `Connected` man turns up anyway. That asymmetry is why EPIC 38 exists.

**Out of scope, named:** the wreckings do not ring the precinct today either
(`OutfitDirector:596` rings for nothing; only `ResolveThreat` does, `TerritoryRuntime.cs:739`),
so a firebombing brings no police and a punch brings a case. That is backwards and it is a
follow-up ticket on the racket track, not a change smuggled into this one.

### 3.4 A body opens a file, with or without a collar (CNTR-004)

Today a killing with nobody collared leaves no docket: the two case openers are the telephone
and the collar, and `GuiltyNear` needs a crew that FIRED, within 45 m, inside 150 s. So the
cheapest way to silence a witness would be to shoot him and walk.

**The neighbours open the file directly. No unit is dispatched and no collar is opened.** On
`StreetAlarm.Death(DeathOf.Civilian)`, attribute shots within 6 s / 40 m with the stricter murder
rule: any nearby police round is a competing shooter rather than something discarded. Then call
`pipeline.OpenCase(Deed.Murder, faction, today, today + DaysToCourt)`, mark the body's own
evidence, and copy the same scene snapshot `OpenComplaintCase` copies.

**It must not go through a `CallOut`.** A complaint's collar takes its suspect from
`AccusedNear(door, faction):384` — *the nearest crew of that faction*, not the crew that fired —
and books it on `call.Call.Charge` (`:361`), which would be `Deed.Murder`. A hit crew that
shoots a bystander and drives off would have its own collector crew (GAN-273, its own unit, one
bag man) walk the same block ninety seconds later and be booked for murder: band 15–25, bail
25,000, court tomorrow. Deterministically, every time. The `CallOut` route also imposes three
gates a body should not care about: a free unit or the call dies "NOBODY CAME" (`:266`),
`StillAtTheDoor`, and 12 s of quiet before a statement (`:343`).

**What this does to every other civilian death, written down so the night-watch scenarios are
read correctly:**

* An unattributable death rings nobody: no name, no file, no invented defendant.
  `AttributeRecentViolence` returns `default` when two houses are shooting (`:987`), so a
  bystander caught in a two-sided firefight opens no file while one caught in a one-sided
  drive-by does. Police and gang fire together is ambiguous for the same reason.
* `Explosion.cs:68` kills outright with no shot, so a car bomb or a grenade that kills
  bystanders is unattributable and rings nobody. Deliberate for now, named here so nobody reads
  it as a defect.
* The police shooting a fleeing man is a police faction death, not ours.

**What the file is worth, honestly.** A case with no defendants is never tried (`Tried:700`
needs a prisoner). It survives as a folded COUNT on the next man of that house taken within
`ComplaintMemoryDays` 14 (`AttachOpenComplaints:229`), worth `ExtraCountDays` = 3 days
(`Sentencing.cs:83`). So SHOOT HIM removes an eyewitness worth 0.20 of a conviction chance and
costs three days on some later arrest. Three flat days for a murder count is not "sentences are
longer for every deed", so `ExtraCountDays` stops being a constant: a folded count is worth
`BandLow(that case's deed) / 3`, rounded down, floor 1 — a murder count 5 days, a battery 3, an
extortion 2, a resisting 1. The flat 3 stays as the fallback for a count whose deed is unknown.
This is one number in `Sentencing` and it changes every folded count in the game, so it carries
its own contract and is named here rather than smuggled in.

The body is evidence in its own right; it does not masquerade as a willing witness, so killing
the last eyewitness does not erase the murder count while killing the complainant on an ordinary
extortion file still does. A no-defendant file is physically removed after the same 14-day
window, and the map reads a separate open-case index, so bodies cannot grow the save or the
per-frame witness scan for the lifetime of the campaign.

**The indoor act.** `OpenComplaintCase` copies the pavement snapshot unconditionally
(`:790`) with `SightRadius` 25 m and up to 3 eyewitnesses (`CivilianAgent.cs:1222`). A beating
inside would therefore open at 0.30 + 0.15 + 0.10 + 0.40 = 0.95, "IT LOOKS BAD FOR HIM" — the
opposite of §3.1's design, and nobody on that pavement saw anything through a wall. `RingAbout`
carries an `indoors` flag that suppresses the eyewitness snapshot: the complainant and the
officer who found them, and nothing else. The killing in EPIC 38 does NOT set it — a shot is
heard and the people outside saw the crew leave.

### 3.5 SHOOT HIM (CNTR-005)

The witness card (`CrewOverlay.OpenWitnessOrders:1355`) gains a second row, red:

    SHOOT HIM — "a murder in the open · whoever is on the pavement sees it"

**The shot is built where every other shot is built.** Not on `CrewWalker`: flash, scatter,
loudness, `StreetAlarm.Report`, the `HearShot` propagation that makes rival crews draw
(`DemoCrews.Combat.cs:1424`) and `CrewGore` all live in one block at `Combat.cs:1411`. A
`CrewWalker.Execute` written beside them would kill a man with no flash, no blood and no rival
reaction, and would break CLAUDE.md's rule that behaviour lives in the shared class. The
execution is a target variant of the existing resolution in `DemoCrews.Combat`, called from the
card's order; EPIC 38's shot inside goes through the same door, so the flash suppression (the
body is `SetActive(false)`, `DoorBeat.cs:1107`), the earshot draw and the gore are one decision
rather than three.

The walk-up reuses the lean's plumbing (`WitnessWatch.OrderLean:154`, reach 4.5 m, patience
120 s) **with one fix it needs**: `TickLeans:179` never repaths, so a witness who has walked on
is never reached and the order lapses in silence. The move is re-issued as the body moves. At
reach the man stops (a new behaviour, not an existing seam — the USER RULE is that a running man
never fires), the piece comes out (Carrying → Drawn), and one round is resolved.

His name leaves the case through the existing `WitnessWatch.Tick` sweep, which prints "X WILL
NOT BE GIVING EVIDENCE". §3.4 means the murder he was killed to bury has a file of its own by
morning. The stale comment on the card is deleted.

### 3.6 Tests, probe, docs, close (CNTR-006)

`PoliceTests`: both enum sweeps (§3.1); the beating's fear arithmetic — 60.75 Public clears
`TestifyFearCap`, the same act filed `Seen` does not; the telephone rings on the PRE-beating
standing, not the post; the tampering deed when the beaten man is on our docket; an attributable
civilian death opens a case and an unattributable one opens none; an indoor act's case carries
no eyewitness; a dead eyewitness leaves the willing count one lower.
`RackTests`: the new row, the `violence[]` loop split, the shut-premises refusal at
`DoorJobs.TryBuild`. `BusinessTests`: a Beating closure does not displace an active SmashUp, and
a zero-price closure cannot be repaired. `RacketProbe` gains a beating scenario beside its smash
and torch. `Docs/racket-collections.md` gets "What may be done to the man";
`Docs/economy-prices.md` the battery bail row; memory updated.

**Whoever implements these tickets moves them, and this epic, to Done in Linear when the work is
finished.**

### 3.7 Not in v1

* An owner body at the counter or on the pavement (ruling 1: "za sad").
* The owner's death, the successor, the save bump — EPIC 38.
* The wreckings ringing the precinct (§3.3, follow-up on the racket track).
* The rival minds beating anybody — parity is EPIC 25.
* A knife or a silenced gun.

## 4. Rules that must hold

* Witness lists only ever shrink.
* Every order runs through the one door list and `DoorJobs.TryBuild`; a rival house filing the
  same order tomorrow is refused by the same rules.
* `OrderType` and `Deed` are appended, never reordered; the SPEC row is placed by category, not
  by enum order.
* Both `_ =>` sweeps are run, not trusted.
* A job completes on the ACT, never on its hours, and the act fires only from the visit's
  inside callback — never from a positional phase test.
* A person order never displaces a premises closure, and a free closure is never repairable.
* Every shot in the game is resolved in one place.

## 5. Tickets, in order

| # | Ticket | Depends on |
| -- | -- | -- |
| CNTR-001 | `Deed.Battery`, the price row, both enum sweeps | — |
| CNTR-002 | `OrderType.Beating`, the row, the person/premises split, the `Refusal` signature, the gateway's shut gate | — |
| CNTR-003 | The beating on the street: the early return, the inside callback, ring-then-fear, severity 2.5, the one-day closure and its two guards | 001, 002 |
| CNTR-004 | A body opens a file: the direct case, no collar, the indoors flag, what the count is worth | — |
| CNTR-005 | SHOOT HIM: the card row, the repathing walk-up, the execution inside `DemoCrews.Combat` | 004 |
| CNTR-006 | Contracts, the probe, docs, memory, everything to Done | all |

## 6. Acceptance

    unity command gangsters_police_tests --json     # the deed, the fear gate, the telephone order, the body's file
    unity command gangsters_rack_tests --json       # the row, the paying-door split, the shut gate
    unity command gangsters_business_tests --json   # the one-day closure, no displacement, no free repair

On MiniCoreDemo, one seed, watched: BEAT THE OWNER at a refusing door — the man goes IN, punches
are heard, he walks out, the owner reaches for the telephone at the odds he had BEFORE the punch,
the shop reads CLOSED for a day with no repair row, the block's fear clears the cap, and the
sheet turns to "may not testify — frightened". SHOOT HIM on a man on the pavement — the crew
repaths to him as he walks, he is shot with a flash and blood like any other man in this game,
his name leaves the case, and a fresh murder file is on the docket by morning with the pavement
on it and no innocent crew of ours booked for it.
