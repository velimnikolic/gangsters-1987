# The Fixer — the pad, the clerk and the bench (EPIC 45, draft 2)

Design brief, written 2026-09-05 and revised the same day against the contrarian pass (§0b) and
the user's rulings (§0a). Linear: EPIC 45 = GAN-441 (tickets `FIX-000..008` = GAN-442..GAN-450). EPIC 43 is the
intelligence brief (GAN-419); EPIC 44 is The Corner (GAN-430).

The user's words: "uvodimo specijalistu Fixer koji je za bribes and shit — plan za bribes u igri.
Mislim se da možeš da podmitiš policajca, šefa policije, sudiju, ne znam šta dalje."

So: a third specialist beside the accountant and the lawyer, hired out of the classified column,
who is the ONLY man in the outfit who carries an envelope — and who **carries it himself, on
foot**, to the precinct door or the courthouse steps. Through him the outfit can buy a patrolman,
a precinct captain, the court clerk and a judge; every one of those can take the money, hand it
back, or hand the Fixer to the desk sergeant; and a rival can shoot him on the way with the
envelope in his coat.

Sits beside **EPIC 26** the complaint, the trial and the lawyer, **EPIC 33** THE LAW tab
(`Docs/ledger-law-sheet.md`), **EPIC 27** the flats and their raids, **EPIC 30** the collector's
detail (a man of his own on the street, the bag where he fell), **EPIC 40** the street event book
(`Docs/street-events.md`) and **EPIC 29** the safe that spends dirty-first (`Docs/headquarters.md`).
It gives an effect to two orders that have been on the books since the first week with none —
`OrderType.Bribe` and `OrderType.EmployPolice`, which today move money and buy nothing
(`Assets/Scripts/Outfit/OrderEffects.cs:83-88`, `Docs/rival-families.md:436`).

## 0a. Rulings (the user's, 2026-09-05)

The user took every recommendation of draft 1's sixteen questions except two:

* **13. The Fixer walks.** He carries the envelope to the precinct door or the courthouse steps in
  the city, and can be stopped on the way. Not paper.
* **16. Higher prices, and no "half the house first" for the captain** — the captain is simply
  expensive.

Everything else stands as recommended: only the Fixer bribes; a good Fixer reads a man but never
removes the risk; a Straight man can report even a single envelope; the pad is per house; the pad
never buys shots and bodies, a witness the court already has, or a cop-killer; the pad does not
reach the vice squad's sting; a wanted mark is not for sale (EPIC 20's ruling stands); a judge on a
murder is for sale, never on a cop-killing, never under the trafficking floor; the judge is dealt
per case from the seed; the clerk's continuance is three days and may be bought for a man in a
cell (the sheet warns first); the captain's tip is a "tonight" slip, not a pot card; CLEAR THE
ROOM darkens the room for the night; rivals pay the pad, as a separate ticket that may slip;
Streetwise + Persuasion, $350 a day.

## 0b. What the contrarian pass changed (adopted in full)

1. **The risk was decorative.** Draft 1's Straight man reported only on a double envelope and the
   seeded stream was never drawn, so nobody ever reached `Deed.Bribery`. Now a Straight man
   reports on a seeded roll for ANY envelope (one in three on the ask, certain on double); a
   Bendable man's "double" is a seeded floor that the Fixer's stars narrow but never remove; and
   the three-star read says `HE WILL WANT MORE`, never a guarantee.
2. **The shut door was on the unsaved object.** `Officials` deals name and disposition only; the
   per-official rows (`ShutUntilDay`, `Known`, `Blown`) live on the saved `CorruptionBook`.
   Contract `AShutDoorSurvivesAFile`.
3. **The tick-order contract was in the wrong class.** The courts sit in `PoliceForce.TickDay`
   (`Assets/RoadDemo/PoliceForce.cs:1861,1931`), not in `CampaignRunner`; the convoy's trial lands
   mid-day at arrival. The contract is STATE, not order: the fix is written on the `CourtCase` at
   the door and `PrisonPipeline.Tried` reads the case. The gate is "the judge has not seen him
   yet" (`!AnyTried` for that defendant), not "court is today".
4. **Three judges in rotation + a three-day continuance = the same judge, always.** The judge is
   dealt per case from `(citySeed, caseId)`; the continuance and a failed convoy's +1 change
   nothing.
5. **`NobodyRang` is the shooting clock** (`Assets/RoadDemo/PoliceDispatch.cs:51,296`, beside
   `_shotHeat`). Dropped. The pad has two seams, not three.
6. **The rival wiring rested on a false premise.** No mind files `EmployPolice`; there is no pure
   "which precinct polices this block"; the paper city has no precinct. Its own ticket (FIX-007)
   with `HouseIntentKind.Pad`, `TerritoryGeography.PrecinctOf(block)`, one paper precinct, and
   the pad's nightly cost in `HouseView.DailyPayroll` so D9 counts it.
7. **The attention pool has no house, and the sting reads it.** Blinding is applied at the
   DEPOSIT, only to deposits the house itself makes; the sting reads an unblinded number, and the
   pad row prints `THE PAD DOES NOT REACH THE VICE SQUAD`.
8. **Two readers of the odds.** `LawSheet.cs:370` calls the 7-argument `ConvictionChance`; the
   court calls the file overload (`PrisonPipeline.cs:858`). The sheet moves to the file overload
   and the fix is not an optional parameter, so the sheet's word and the court's roll cannot part.
9. **THE PHONE holds one card three days; a tonight-only tip cannot live there.** The tip is a
   "tonight" slip with two keys on THE LAW tab and THE WIRE, outside the one-card rule, and it
   says `THIS ONE DOES NOT WAIT`.
10. **"The same roll a night early" was not the same roll** (kilos land after `FlatDay.Tick`,
    keepers change). The tip is a stored commitment on the book — `RaidDue { unit, day }` — and
    `FlatDay` honours it the next night instead of re-rolling.
11. **CLEAR THE ROOM had nowhere to put the kilos.** The room is marked cleared for that night:
    the raid seizes nothing and jails nobody, the room is still sealed a fortnight, the kilos stay
    on the house's count, and the next load holds until there is a room (existing `LoadHeld`).
12. **Under the captain, leaning on a witness became risk-free.** The pad drops the SHOPKEEPER's
    call (`StreetAlarm.Complain` with a business id); a tampering call about a witness the court
    already lists (`WitnessWatch.cs:360`, empty business id) goes past the desk. Printed on the
    pad row: `NOT — a witness the court already has`.
13. **Bribery's band contradicted itself.** 8–14 days (Extortion's row), bail $3,000; the five
    exhaustive deed switches (`Verdict.BaseFor`, `Sentencing.BandLow/BandHigh/ChargeFor/Bail`) are
    listed in FIX-002 so a miss is not a runtime throw the first time a Fixer is booked.
14. **A BLOWN case needs evidence and the Fixer needs his own docket.** The reporting official is
    a `PoliceSawIt` witness (unpressurable); the Fixer is booked on his OWN `Bribery` case, never
    as a defendant on the murder file he tried to fix.
15. **The continuance has three copies of the court day** (`CourtCase.CourtDay`,
    `Prisoner.CourtDay`, `Character.BailedUntil`) — all three move, one desk verb. The verdict
    stream is keyed by the day, so a continuance is also a fresh draw; the sheet says so.
16. **`OrderType.Bribe` had two meanings.** It IS the walked envelope now (ruling 13), so it has
    one. `EmployPolice` is the pad's first approach through the same job.
17. **The precinct's size is seed-dependent** (`men + 2 × owned`, `RoadDemoBuilder.cs:4490`).
    The probe prints it per seed; "half the house" is gone anyway.

## 1. What exists and is reused

| Thing | Where | Used for |
|---|---|---|
| The specialist | `Personnel/Character.cs:14-24` `Specialty { None, Accountant, Lawyer }`; `Wages.cs:165` reads a specialty before a rank | `Fixer` appended; his flat wage |
| The skill as one function | `Personnel/Lawyer.cs:23-40` `Skill(man)` = Awareness + Organization in five bands | `Fixer.Skill(man)` = Streetwise + Persuasion, same bands, same readers (ad, approach, file) |
| The column | `Outfit/HireMarket.cs` `DealLawyer`, `LawyerMorning` (`:78,130-134,170-171`), `CounselFor`, `HeadingFor`, `PitchFor` | `DealFixer`, `FixerMorning` (every 7 days while none on the books), heading THE FIXER, from CITY HALL |
| The orders | `Outfit/Orders.cs:221-226` `Bribe`, `EmployPolice`, `Activity.Negotiation`, `TargetMode.Point`; `OrderEffects.cs:83-88` | `Bribe` = the walked envelope to the courthouse (clerk, judge); `EmployPolice` = the walked envelope to the precinct (a man, the captain) |
| A man of his own on the street | EPIC 30: the collector out of the line as his own unit, `Duty.Escort`, `RoadDemo/BagCarry.cs` (the bag lies where he fell, TAKE THE BAG) | the Fixer walks the same way, with the envelope where the bag is |
| The precinct | `Police/PoliceRoster.cs` (`StationId`, `Officers`, `Cars`); `RoadDemo/PoliceForce.cs:427` `Add(...)`, `Precinct.Door` | the pad is per precinct; the door is where the envelope goes |
| The courthouse | `RoadDemo/PoliceForce.cs:135-154` `CourthouseDoor`, `HasCourthouse`, `StandCourthouse` | the clerk's and the judge's door; no courthouse → `NO COURTHOUSE IN THIS CITY` |
| Attention | `Territory/TerritoryFear.cs:439-444` `NotePoliceAttention(block, amount, hour)` (no house on it), `PoliceAttentionValue.Add` (cap 100, half-life 8 h) | blinding at the house's own deposits: the collection walk (`TerritoryRuntime.Collection.cs:1507`), the flats (`OutfitDirector.cs:449`), the connected owner (`:695`) |
| The complaint | `RoadDemo/PoliceDispatch.Complaint.cs:221` `OnComplaint`; `StreetAlarm.Complain(pos, faction, businessId, ...)` (`StreetAlarm.cs:161`); `WitnessWatch.cs:360` (tampering, empty business id) | the ONE choke point where a bought desk drops the shopkeeper's call |
| The sting | `Outfit/CampaignRunner.cs:1025-1033` `WatchOnTheDoor` | reads the UNBLINDED number |
| The raid | `Property/FlatDay.cs:282-347` `Raid` (`RaidBaseChance` 2 + heat × 6 − care, per mille, stream `(unit, citySeed, day, roll)`), `SealedDays` 14, `FlatRaid` | the captain's tip: a stored decision honoured the next night |
| The docket | `Police/PrisonPipeline.cs` `Book` (:400), `Tried` (:814, `AnyEvidence` :838), `TryOnPaper` (:759), `BackToTheCells` (:646); `Sentencing.DaysToCourt` 1 | the continuance and the fix live on the `CourtCase`; the Fixer's own case |
| The trial | `Police/CourtCase.cs:438-464` two `ConvictionChance` overloads, `Leaning` (:486, four words), `WitnessKind.PoliceSawIt` (unpressurable, :133); `Sentencing.Days` (:178) | the fix is one term in the file overload; both readers use it |
| The deeds | `Personnel/Sentencing.cs:7-42` `Deed`; the five switches `BaseFor` (`CourtCase.cs:422`), `BandLow` (:116), `BandHigh` (:132), `ChargeFor` (:231), `Bail` (:253); `RapSheet.cs:50` "Bribery of a public servant" | `Deed.Bribery` appended and named in all five |
| The sheet | `UI/PersonnelAlmanac.Law.cs`, `Police/LawSheet.cs` `Collect` (pure, contracted, `gangsters_law_sheet`), `RoadDemo/LawDesk.cs` | THE PAD region; the docket keys; the tonight slip; one desk |
| The event book | `Outfit/StreetEvents.cs` `EventDef`, `Roll` (:522 one card a day, never over a pending one), `HoldDays` 3 (:432), `HoldReason` `Line` + `Clears` (:35-93) | `CopWantsAWord` only; the tip is NOT a pot card |
| The money | `Outfit/Accounts.cs` `Bribes`, written by `CampaignRunner.cs:688-689`; `Finances.cs:298` "Bribes" row; `Accounts.cs:169-183` dirty-first | every envelope and every night of the pad on that one row |
| The mind | `Outfit/HouseMind.cs` D9 (`row.Upkeep * ReserveDays`, :1976), `WalkAttentionCap` 40; `HouseIntent.cs:8-53`; `HouseView.PoliceAttention` delegate (:394) | FIX-007 only |
| Save | `Outfit/OutfitSnapshot.cs:141-153` nullable books on `HouseDto`; `Save/PrisonSnapshot.cs` version 3 | `CorruptionDto` nullable on the house; case rows `judgeFixed`, `continued` |
| Prices | `Outfit/EconomyPrices.cs` (`Bribe` 500, `PoliceOnThePad` 800); `Docs/economy-prices.md:196-207` | the §8 table replaces both constants and the two §7 rows |

## 2. The model — `Assets/Scripts/Police/Corruption.cs` (pure, UnityEngine-free)

**The officials — `Officials`.** Dealt from `(citySeed, kind, index)` and never saved: `Captain`
per precinct (`StationId`), `Judge` × 3 (the judge on a case is `JudgeFor(citySeed, caseId)`),
`Clerk` × 1. Each has a name and a `Disposition` — `Crooked` / `Bendable` / `Straight`, weights
30 / 45 / 25. Nothing else lives here.

**The house's book — `CorruptionBook` (one per house, saved as `CorruptionDto`).**
* `Doors`: one row per official — `Known` (the Fixer has read him), `ShutUntilDay`, `Blown`.
* `Pads`: one row per precinct — `OfficersBought`, `CaptainBought`, `SinceDay`, `PaidThroughDay`.
  `Share = OfficersBought / Officers`; the captain reads as share 1.
* `Fixes`: per case — `JudgeFixed` (day, outcome), `Continued`.
* `RaidsDue`: `{ unit, day, cleared }` — the captain's tip, frozen (finding 10).
* `Record`: envelopes taken / refused / blown, printed on the Fixer's file.

**The approach — ONE function, drawn from the stream.** `Corruption.Approach(disposition,
envelope, ask, fixerSkill, stream) → Taken | Refused | Blown`, stream
`MixSeed(citySeed, houseId, officialId, day)`:

| Disposition | the ask | double the ask |
|---|---|---|
| Crooked | TAKEN | TAKEN |
| Bendable | TAKEN if the roll is under `0.25 + 0.15 × (stars − 1)` (a one-star Fixer one in four, five stars 0.85); else REFUSED | TAKEN |
| Straight | REFUSED, or BLOWN one in three | BLOWN |

REFUSED shuts the door thirty days; BLOWN shuts it for the campaign. The Fixer's read at three
stars and up: `HE CAN BE REACHED` / `HE WILL WANT MORE` / `NOT THIS ONE`; under three,
`UNKNOWN — he has not asked around` until the first approach marks the row `Known`. Reasons are
values with `Line` + `Clears` (`CorruptionReason`): NO FIXER, NO MONEY, HE IS INSIDE, HE IS OUT
(already walking), DOOR SHUT UNTIL DAY d, THE JUDGE HAS SEEN HIM, NOT FOR SALE (cop-killing),
ALREADY SEEN, NO COURTHOUSE, NO PRECINCT.

**The night — `Corruption.DayTick`** in `CampaignRunner.DayTick`: pay every pad row from the safe
(dirty-first, `Bribes`); a night the safe cannot cover breaks that row, prints `THE PAD LAPSED —
<precinct>` and shuts the captain's door thirty days; under a bought captain, decide tomorrow's
raids (§3) and write `RaidsDue`. The courts are not here (finding 3): the fix is on the file the
moment the envelope is taken, and `Tried` reads the file.

## 3. What each bought man buys

| Man | Shape | Price | What he buys | What he does not |
|---|---|---|---|---|
| **A patrolman** | pad, per officer | **$1,000/mo = $33/day** each | a share `s` of the precinct: the house's own attention deposits on its blocks × (1 − 0.5 s); the shopkeeper's call against us dropped at the desk with odds `0.8 s` | shots and bodies; a witness the court already has; the vice squad's sting |
| **The captain** (one station house today, so he is the chief) | pad, one man | **$6,000/mo = $200/day** — a lieutenant's wage, no other condition | share 1: every shopkeeper's call against us dropped, our deposits × 0.5; **the tip** | the same three, and a cop-killer's paper, the swarm, a transfer on the road |
| **The clerk** | fix, once per case | **$1,000** | a continuance: all three court days += 3. On bail: three more days to lean on witnesses. In a cell: three more nights inside — the key says so before it is pressed. A fresh draw on the new day | the verdict |
| **The judge** (dealt per case) | fix, per case | Extortion / Witness tampering / Bribery **$3,000**; Battery **$5,000**; Affray **$6,000**; Resisting / Assault on an officer **$10,000**; Murder / Trafficking **$15,000**; Cop-killing **not for sale** | `ConvictionChance − 0.35` on every defendant of ours on the case; a conviction sentenced at the band's floor. COUNSEL SAYS reads the fixed odds, in words; the card prints `THE JUDGE HAS BEEN SEEN` | the trafficking floor (fifteen days bind); life; a BLOWN judge sits the case at `+ 0.10` |

The judge's 0.35 is the size of four stars of counsel and stacks with them (a murder, two
eyewitnesses, police saw it, five-star lawyer: 0.55 + 0.40 + 0.30 − 0.40 − 0.35 = 0.50, IT COULD GO
EITHER WAY; without the police, 0.20, THEY HAVE ALMOST NOTHING). Every constant here is a
parameter measured in FIX-008 before it is fixed.

**The walk (ruling 13).** An approach is a job: `EmployPolice` to the precinct door (a man, the
captain), `Bribe` to the courthouse door (the clerk, the judge). The Fixer leaves headquarters as
a unit of his own the way the collector does (EPIC 30), the envelope on him like the bag, an
escort if the player details one (`Duty.Escort`). At the door he goes in; the answer comes on the
spot and is written on the book and the file there and then. BLOWN: he does not come out — booked
inside through `PrisonPipeline.Book` on his own `Bribery` case, the official as `PoliceSawIt`,
no street arrest scene; the envelope is gone. Shot on the way: the envelope lies where he fell
(`BagCarry`, TAKE THE BAG) and the approach is void. One approach at a time (`HE IS OUT`). The
pad's nightly money after the first envelope is paper — nobody walks $33 to the desk every night.

**The captain's tip.** At the tick under a bought captain, `FlatDay` rolls each of our rooms for
tomorrow with tonight's inputs and writes `RaidsDue` for the ones that land; tomorrow's tick
honours the row and rolls nothing. The slip — `THE CAPTAIN RANG — THEY ARE COMING TO <door>
TOMORROW NIGHT · THIS ONE DOES NOT WAIT` — sits on THE LAW tab and THE WIRE with two keys:
**CLEAR THE ROOM** (the room is dark that night: nothing seized, nobody jailed, the room still
sealed a fortnight, the kilos stay on the count and the next load holds until there is a room)
and **LET THEM COME**. Unanswered by midnight is LET THEM COME. Not a pot card; THE PHONE is
untouched.

**The crooked cop's own approach — `CopWantsAWord`.** A pot def, like the rest: a `Crooked`
captain whose blocks we stand on rings first, fed by our attention on his precinct, gated on
`NoSpeaker` / `NoMoney`. Row 1 puts him on the pad at the ask with no walk (he came to us); row 2
is WALK AWAY. A house without a Fixer sees the card and cannot take row 1 (`Clears`: hire a
Fixer).

## 4. The Fixer himself

* `Specialty.Fixer` (appended). `Fixer.Skill(man)` = Streetwise + Persuasion in the lawyer's five
  bands. `Wages.FixerWage` = **$350** flat. Ad every 7 days while the outfit has none: THE FIXER,
  from CITY HALL, "knows everybody worth knowing"; the ask `FixerWage × 125 %`, fourteen days down.
* On the street he is a man like the collector: unarmed (the weapons rule stands), walks, can be
  shot, can run for it like anybody. Off the street he is at headquarters.
* Booked on `Bribery`: held, tried, bailed, cut loose through the ordinary pipeline, the lawyer on
  his case. Inside, the pad still runs and he is still paid; no new approach (`HE IS INSIDE`).
* His file: HIS RECORD — taken / refused / blown; the pad he keeps; the doors shut on him.

## 5. The sheet — THE LAW tab

* **THE PAD** beside COUNSEL (a fourth box on the right; its own scroll field in `LawSettle`):
  the Fixer's line (stars, wage, record, or the want of one and a key to the column); a row per
  precinct — the captain's name and word, `n OF m OFFICERS · $x A NIGHT · SINCE DAY d`, what it
  buys and what it does not in one line each (`NOT — shots and bodies · a witness the court
  already has · the vice squad`), keys SEND HIM TO THE PRECINCT (a man / the captain) and STOP
  PAYING; the courthouse — the clerk's word and the judges' words.
* **On every DOCKET card**: SEE THE CLERK ($1,000 · three more days · a fresh draw) and SEE THE
  JUDGE ($n · his name and word). Greyed with the reason's `Line`.
* **The tonight slip** with its two keys, when there is one.
* The precinct tile on the turf map: `ON OUR PAD` / `THE CAPTAIN IS OURS`; the Fixer on the map
  while he walks, like the bag man.
* Every row from `LawSheet.Collect`; every write through `LawDesk` (`SendToThePrecinct`,
  `StopPaying`, `SeeTheClerk`, `SeeTheJudge`, `ClearTheRoom`, `LetThemCome`), each calling
  `director.Touch()`.

## 6. The AI (FIX-007, may slip)

`HouseIntentKind.Pad` (appended); `TerritoryGeography.PrecinctOf(block)` (the station block and
its road neighbours, seeded, pure); one paper precinct (`StationId 0`) in the paper city so the
yardstick sees a pad; the pad's nightly cost in `HouseView.DailyPayroll` so D9 counts it every
night. A mind buys officers when attention on its home blocks is over `WalkAttentionCap` and the
safe covers a week of pad on top of wages; it never walks a Fixer (its approach is paper, answered
by the same `Approach` function). Rivals' men are tried in the scene (`PoliceForce.cs:1854`) but
not headless, and no mind sees a judge.

## 7. Contracts (headless, `gangsters_corruption_tests`, `Tests/PoliceTests.cs`'s pattern)

`NoFixerNoEnvelope` · `TheDispositionIsDealtFromTheSeed` · `AStraightManCanReportASingleEnvelope`
(the stream is drawn) · `AShutDoorSurvivesAFile` · `ThePadSurvivesAFile` ·
`ThePadIsPaidEveryNightAndLapses` · `ADroppedCallPutsNothingOnTheDocket` ·
`ATamperingCallGoesPastTheDesk` · `TheStingReadsTheUnblindedNumber` ·
`TheJudgeFixReadsOnTheCounselsWord` (the sheet's word, not the float) ·
`TheJudgeCannotBeBoughtForACopKilling` · `TheStatuteStillBinds` · `TheJudgeIsTheSameAfterTheClerk`
· `TheClerkMovesAllThreeDays` · `TheFixIsOnTheFileBeforeTried` · `TheBlownFixerHasHisOwnCase` ·
`TheTipIsAStoredDecision` · `AClearedRoomLosesNothingButTheRoom` · `ARivalsPadIsItsOwn` (FIX-007).
Probe `gangsters_fixer_probe`: deals a seed, prints every official's name, word and price, the
precinct's size, and files each verb, printing the sheet's own words.

## 8. Money (1987 $; `Docs/economy-prices.md` §1 and §7)

| Row | $ | Note |
|---|---|---|
| Fixer, wage | 350/day | between the bookkeeper (250) and counsel (400) |
| A patrolman on the pad | 1,000/mo (33/day) per officer | above Knapp's $400–1,500 band's middle: the safe is bigger than a 1971 precinct's |
| The captain | 6,000/mo (200/day) | a lieutenant's wage; no precondition |
| The clerk | 1,000 once per case | |
| The judge | 3,000 / 5,000 / 6,000 / 10,000 / 15,000 by deed | Greylord's thousands, scaled to the game's bail rows |
| Bribery | bail 3,000; 8–14 days | Extortion's band |

`EconomyPrices.Bribe` and `PoliceOnThePad` are replaced by these rows; the walked jobs read them
through `CostFor`.

## 9. Tickets

| # | Title | Owns |
|---|---|---|
| FIX-000 | The Fixer: a third specialty, his skill, his ad, his wage, his file | `Specialty.Fixer`, `Personnel/Fixer.cs`, `HireMarket.DealFixer`/`FixerMorning`, `Wages.FixerWage`, the column heading and pitch, HIS RECORD on the file, price doc §1 |
| FIX-001 | The officials and the book, saved, and on the sheet from day one | `Officials`, `CorruptionBook` (`Doors`, `Pads`, `Fixes`, `RaidsDue`, `Record`), `CorruptionDto` on `HouseDto`, `judgeFixed`/`continued` on the case DTO, `LawSheet.Collect` rows + `gangsters_law_sheet` bench, `AShutDoorSurvivesAFile`, `ThePadSurvivesAFile` |
| FIX-002 | The approach: one drawn function, three answers, `Deed.Bribery`, the walk, the door | `Corruption.Approach`, `CorruptionReason`, `Deed.Bribery` in all five switches, `Bribe`/`EmployPolice` as the walked jobs, the Fixer's unit (EPIC 30's shape), the door resolution, booked inside on his own case, the envelope where he fell, the wire slips |
| FIX-003 | The pad: paid nightly, lapses, two seams | `Corruption.DayTick` (pay, lapse), the drop at `OnComplaint` (shopkeeper's call only), blinding at the house's own deposits, the sting unblinded, `EmployPolice`'s price |
| FIX-004 | The captain's tip and the crooked cop's card | `RaidsDue` written at the tick, `FlatDay` honours it, the tonight slip and its two keys, CLEAR THE ROOM, `CopWantsAWord` def (appended `EventId`/`CardId`) |
| FIX-005 | The bench: the clerk's continuance and the judge's fix, on the file before the trial | `JudgeFor(citySeed, caseId)`, `Continued` (three court days), `JudgeFixed`, `Verdict.ConvictionChance` file overload reads it, `LawSheet` moved to that overload, `Sentencing.Days` at the floor, BLOWN +0.10, `Bribe`'s price |
| FIX-006 | THE LAW tab: THE PAD, the docket keys, the slip, the precinct tile, the desk | `PersonnelAlmanac.Law.cs`, `LawDesk`, `TurfMap` precinct word, the Fixer on the map while he walks |
| FIX-007 | The rival's pad (may slip) | `HouseIntentKind.Pad`, `TerritoryGeography.PrecinctOf`, the paper precinct, `DailyPayroll`, `ARivalsPadIsItsOwn` |
| FIX-008 | Measure, contracts, probe, docs | every constant in §2/§3/§8 measured on `DebugSeedLarge`, `gangsters_corruption_tests`, `gangsters_fixer_probe`, `Docs/corruption.md`, CLAUDE.md row, price doc §7 |

Order: FIX-000 → FIX-001 → FIX-002 → FIX-003 / FIX-005 (either order) → FIX-004 → FIX-006 →
FIX-007 → FIX-008. Whoever builds a ticket moves it to Done in Linear when it is finished, and
the last one moves the epic.

## 10. Out of scope, and the seams left

* **The editor.** Killing a story would cut notability (the marked lieutenant's 150 % sentence)
  while THE PHONE wants our name in the paper (EPIC 40's F3 lever): its own epic.
* **A wanted man's paper lost at the desk.** Ruled out (the user, ruling 7 of draft 1).
* **Federal heat.** RICO and the DEA were cut with EPIC 22 (GAN-221); the sting stays outside the
  pad by ruling, which is the seam for them.
* **A prosecutor.** Folded into the judge: one bench, one price.
* **The pad rising.** Knapp's pad went up every year; a `+10 %` a month is one line in
  `DayTick` when the economy wants it.
