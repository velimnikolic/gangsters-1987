# The Sit-Down — every word between houses, and the table where the product changes hands (EPIC 42, draft 2)

Design brief, written 2026-09-05 and **reworked the same day** after two contrarian passes and the user's rulings. Linear: EPIC 42 = **GAN-408**, tickets `DIPL-001..010` = **GAN-409..418** (DIPL-009 = GAN-417, blocked by EPIC 41 and GAN-405's amendments). Sits beside **EPIC 25** (the houses, `Docs/rival-families.md`), touches **EPIC 40 The Connection** (GAN-395, `Docs/design-briefs/connection-brief.md`, Backlog) for the telephone card and the product, and leaves the port, retail and the player as seller to the reserved **EPIC 41 The Trade**.

Whoever implements a ticket of this epic moves that ticket, and when the last one lands the epic itself, to **Done** in Linear.

The user's words: "daj mi ideje za diplomatiju. hocu da mogu da kupujem drogu od njih po skupljoj ceni naravno no sto je nabavljaju"; on scope, "u ovom planu samo treba da znamo jel uvoznik il ne nista ne siri vise. uvoz je odgovornost drugog tiketa"; then "ovde treba pokriti svu diplomatiju ne samo za drogu". So: one mechanism for everything two houses can say to each other — a stance, a word, a bill, money, a man, a street, a pact, a kilo — and the rival minds sitting at the same table on the same terms.

## 0. Rulings (2026-09-05)

1. **Truce and peace need the other side. A beaten house cannot refuse.** [user: "da ako je porazen jbg"] War stays unilateral. A receiver that could not pay its men through a war (`Endurance < MinWarDays`) or has lost `LossesToSueForPeace` men accepts a truce automatically — player and mind alike. Nothing else can end a war today (contrarian, `HouseMind.cs:625-631`), so consent without this rule would remove the war's only exit.
2. **Tribute becomes a thing between every pair of houses.** [user: "da"] Today only the player pays (`Underworld.cs:350` `payTribute: house.IsPlayer`) and the money reaches nobody (`Tribute.cs:150-184`). Every house assesses, every house pays, the levying house is credited.
3. **GIFT is out for now.** [user] An unsolicited envelope was a universal grievance eraser on a single float.
4. **Compensation is capped and a killing has a floor.** [contrarian's recommendation, taken as the default; the user asked what it meant — see §5 — and may veto] One cap for every dollar that clears grievance per pair per day, and money never clears the last part of a killing.
5. **SELL THE STREET and HAND HIM OVER are out of the first cut.** [user: "van prvog"] The racket's switch sweep unwinds a sold street (`TerritoryRuntime.cs:1906-1951`) and no killer is ever named (`:942-948`). Named as seams in §12.
6. **The sit-down in person ships now.** [user: "odma"]
7. **The product waits for EPIC 41, and EPIC 40 gets two amendments.** [user: "oba"] DIPL-009 is blocked by EPIC 41; CONN-004 (GAN-405) gains `BuyerCapacity` and the desk rule "never quote under our own outlet" (§9, and the amendment written into GAN-405).
8. **The haggle is a roll.** [user: "može"] `Streetwise`, keyed per seed like every other job roll, never a closed-form 10 %.
9. **Ten tickets stay ten; their contents are cut.** [the user asked what the cut meant] Money folds into DIPL-001; DIPL-005 is RANSOM alone; DIPL-006 is THE LINE alone; DIPL-008 stays; DIPL-009 is blocked.

## 1. What the city can say today, and what it cannot

Verified in code 2026-09-05:

| today | where | the hole |
|---|---|---|
| A stance is set by ONE side and lands on the pair at midnight; one pending slot, last write wins | `Outfit/HouseRelations.cs:195-213` `SetPending`/`ApplyPending`; the FAMILIES buttons `UI/PersonnelAlmanac.Diplomacy.cs:375-383` → `OutfitDirector.cs:467-477`; the mind's `Stand` `Outfit/HouseMind.cs:625-643`; carried `RoadDemo/TerritoryRuntime.Minds.cs:846-851`; `Sour` "hardest wins" `CampaignRunner.cs:1206-1249` | a truce is imposed, not offered; an agreed truce can be overwritten the same evening by a defection or a re-declaration |
| A word (warning, threat, bill) is an `Incident` printed in both books | `TerritoryRuntime.Minds.cs:990-1006`, `IncidentKind.AWordBetweenHouses` | it cannot be answered: swept into `WarningIgnored` after 48 h whatever happened (`:1015-1030`); a bill carries a price nobody can pay; only a mind can send one |
| Tribute is the player's alone, paid into nothing | `Underworld.cs:327-330, 350`; `Tribute.cs:150-184` `Settle` pays, no `Receive`; `CampaignRunner.cs:881, 1277` | no house is ever credited; no AI house ever pays; the terms cannot be negotiated |
| A kidnapped man is off the books 3 days with "$5,000 to have him back" printed, at the scene edge only | `Gameplay/OutfitDirector.cs:733-766` `TakeHim`, `RosterOps.Taken`, `OrderResolution.KidnapDays` | nobody can pay; the paper city never sees a kidnap |
| `LossesThisWar` is always 0 | `TerritoryRuntime.Minds.cs:584`, `PaperCity` default; `MenLostTo` exists at `CampaignRunner.cs:1252` and is not on the view | every rule keyed to losses is dead |
| No dollar crosses between two houses | `BalanceMath.Pay/Receive` are per-`Accounts` | every transfer below is the first |

The one mechanism that fills every hole: **a proposal**, from one house to another, with terms, answered by the other side — a mind at the desk, deterministically, or the player through the ledger and (once EPIC 40's STREET-002 exists) the telephone.

## 2. What exists and is reused

| Thing | Where | Used for |
|---|---|---|
| The houses and the one book of standings | `Outfit/Underworld.cs` (`Of`, `Relations`, `DayTick` `:333-356`), `Outfit/HouseRelations.cs` (`Stance`, directed grievance `:223-232`, `LadderStep`, `HouseRelationsConfig`, `Endurance :383`, `Estimate :386-399`, `NoteBorder :242-261`) | every answer reads the stance, the ladder and the haze; new `GrievanceKind`s appended AND named in `AmountOf` (`:113-128`, whose default returns `TributeUnpaid`) |
| The mind and its wall | `Outfit/HouseMind.cs` (`Collect` before `Walk` `:185-222`, `Feud :611-692`, `Home :329-352`, `Merge :977-1010`, `Buy :890-931`, `DropTheUnbuilt :231-237`), `Outfit/HouseView.cs:225-325` (looks only), `Tests/HouseMindTests.cs:1125-1133` (the token scan) | proposals and answers as a step beside `Collect`, before `Walk`; new looks, never books |
| Intents and the two edges | `Outfit/HouseIntent.cs` (`Stand`, `Word`, `Buy`; `Key :249-252`), `RoadDemo/TerritoryRuntime.Minds.cs` `Carry` `:806-870`, `Tests/PaperCity.cs:421+` (the paper `Carry`) | two new intents, `Propose` and `Reply`, in **both** switches |
| The words already printed | `IncidentKind.AWordBetweenHouses`, `Runner.Incidents`, `UI/WireBook.cs` | every proposal and answer prints there too |
| Money | `Outfit/Accounts.cs` `BalanceMath.Pay(out dirtyPart) :161-176` (refuses below price), `Receive(kind) :150-158` (books no sheet line), `DaySheet :11-42` | `Underworld.Transfer(from, to, amount)` and two named sheet lines |
| Tribute | `Outfit/Tribute.cs` (`Levy`, `CycleDays` 5, `Assess`, `Settle :150-184`, `Overdue`) | assessed and settled for every house in one pass, credited to the payee |
| The racket's choke points | `RoadDemo/TerritoryRuntime.cs:2689-2725` `Execute(ApproachBusinessCommand)`, `TerritoryRuntime.Paper.cs:67-90` `PaperDoor` | where a KEEP-OFF (a complied warning, a line) is refused for both the street and the paper |
| Engagement | `Outfit/Engagement.cs:11-13, 36-38` `May(stance, oursIsTheGround, provoked)` | unchanged; the ambush is a provoked engagement |
| Death attribution | `TerritoryRuntime.cs:939-948` `AttributeShooting` — a gang, never a name | why HAND HIM OVER waits (§12) |
| The FAMILIES page | `UI/PersonnelAlmanac.Diplomacy.cs` (`FamilyCard` `:233-388`, `FamilyCardH` 316 `:42`, rows at 22 px, buttons at −278..−304) | the card is full: one button, THE TABLE, replaces the three stance buttons; the sheet carries everything |
| The telephone (EPIC 40) | `Outfit/StreetEvents.cs` `EventCard`/`Choice`, `UI/EventCardHud.cs`, `UI/ModalGate` (STREET-000..002) | an incoming proposal as a card with ACCEPT / REFUSE / COUNTER; **not required** — the ledger inbox is the first surface |
| The connection (EPIC 40) | `Outfit/Connection.cs` — `Stage`, `pricePerKilo`, `kilos`, `UnitRole.Stash`, `BuyerPrice`, `Deed.Trafficking`; CONN-003's test buy (GAN-404: "the crew walks, the money leaves on arrival") | the product chapter (§9) |
| The orders | `Outfit/Orders.cs` (`OrderType` appended), `Outfit/OrderEffects.cs:30-75` `Built` (an `OrderType` not listed is dropped by `DropTheUnbuilt`) | `SitDown`, `BuyProduct` — both listed in `Built` |
| Save | `Outfit/OutfitSnapshot.cs:104-111` `HouseDto` (no class-typed field today; JsonUtility writes a null `[Serializable]` object as `{}`), `Save/CampaignFile.cs`, `Tests/SaveTests.cs:787` (version pinned at 3) | DTOs carry **arrays only**, appended, no version bump |
| The suites | `Tests/RelationsTests.cs` (`:186-192` asserts the broke house's unilateral truce — rewritten), `Tests/PaperCity.cs`, `Tests/UnderworldSim.cs:320`, `gangsters_underworld_sim` | `gangsters_diplomacy_tests`, yardstick rows |

## 3. The proposal book — one mechanism (DIPL-001)

`Outfit/HouseDiplomacy.cs`, pure, one per city on `Underworld` beside `Relations`:

```
Proposal   Id, From, To, Kind, Terms { Money, Kilos, BlockIds[], CharacterId, Third, Days }, Day,
           ExpiresDay, Status (Open / Accepted / Refused / Expired), Escrow (money held, §4), Envoy (§8)
Book       Open (both directions), History (last 30 per pair), KeepOff (house, blockId, untilDay),
           Agreed (pair, stance, day) — the guarded pending write of §4, Pacts, Lines, Terms (tribute)
Answer(receiverBooks, view, relations, proposal, config) → Accept | Refuse(reason)
```

* **Two intents.** `HouseIntent.Propose(proposal)` and `HouseIntent.Reply(proposalId, accept)`. Both `Carry` switches (`Minds.cs:806-870`, `PaperCity.cs:421+`) execute them through `HouseOps.Propose` / `HouseOps.Reply`, the calls the ledger buttons make. Nothing a mind can propose is something the player cannot, and the reverse.
* **The answer is at the desk, not in a think.** When a proposal reaches a mind's house, the runtime calls `HouseDiplomacy.Answer` at once with that house's own books and its view; deterministic, from the tables in §4–§9, no roll. Precedent: tribute is settled at the tick without a think. A proposal to the player waits in his inbox until he answers or it expires.
* **The mind's step.** `HouseMind.Diplomacy(view, …)` runs beside `Collect`, **before `Walk`** (`HouseMind.cs:185`, "tier 4 never waits" — EPIC 40's card pattern): it answers nothing (the desk did), and proposes what needs no crew — a truce, a word, a line, a pact, terms, a kilo. `Walk` is untouched. `view.OpenProposalLook(other, kind)` says whether one is already open; `HouseOps.Propose` refuses a duplicate in words ("we already asked"), so a broke house does not propose a truce every think.
* **Expiry.** `ExpiresDay = Day + ProposalDays` (3), except a word's (§5: 2, and its expiry is a note). Expiry of everything else is a refusal without a note.
* **Every proposal and every answer prints** as an `AWordBetweenHouses` incident in both books, in the sentence the sender wrote ("the Morettis offer a truce - $4,000 · REFUSED").
* **Money between houses.** `Underworld.Transfer(from, to, amount)`: `BalanceMath.Pay` on the payer first (it refuses below price; nothing moves then), `Receive(Dirty)` on the payee for the whole amount — street money arriving is street money. `DaySheet` gains `ToHouses` and `FromHouses`, printed on Finances as BETWEEN THE HOUSES; `DirtyIncome` includes `FromHouses`.
* **Keep-off.** `Book.KeepOff(house, blockId, untilDay)` and `view.KeepOffLook(blockId) → untilDay`. The refusal is at the two choke points — `Execute(ApproachBusinessCommand)` and `PaperDoor` refuse every racket order on that block by the house kept off ("that street is under our word"); a crew of that house posted there is sent home through the same `Cancel` the watch uses (`Minds.cs:384`). The mind's `Expand`/`Defend` skip a block the look names. One mechanism serves a complied warning (§5) and the line (§7).
* **Save.** `DiplomacyDto` on `UnderworldDto`, `HouseDealsDto` on `HouseDto` (§9): arrays only, never a nested object, so a null reads as empty; no version bump. Quotes are derived from `(day, seller, kilos)` and not saved.
* **Numbers** in `DiplomacyConfig`, one class, never a literal in a method:

| number | value |
|---|---|
| `ProposalDays` | 3; a word 2 |
| `CompensationPerPoint` | 200 — $200 clears one point of grievance |
| `CompensationCapPerDay` | 20 points, per pair, every dollar counted (truce money, a bill paid, terms) |
| `KillingFloorDays` | 30 — after a `ManKilled`, money cannot take the pair under `ThreatAt` (20) for this long |
| `ComplyDays` | 5 |
| `LineDays` | 30 |
| `PactDays` | 30 |
| `EnvoyMarginPerHalfStep` | 2 %, cap 20 % (§8) |
| new grievance, named in `AmountOf` | `InsultingOffer` 5, `DebtUnpaid` 25, `LineCrossed` 20, `PactBroken` 30, `SitDownBetrayed` 40 |

## 4. The stance by agreement (DIPL-002)

* **War is declared, never proposed.** DECLARE WAR and the mind's `Stand(War)` stay unilateral: `SetPending(War)`, midnight, as today.
* **Truce and peace are proposals.** `OfferTruce(money)` and `OfferPeace(money)` replace the TRUCE and PEACE buttons and the mind's `Stand(Truce)` at `HouseMind.cs:629, :642`; `RelationsTests.cs:186-192` is rewritten to the new rule. The money, if any, is compensation (§5's cap and floor apply).
* **`LossesThisWar` is wired** from `CampaignRunner.MenLostTo` into the view at `Minds.cs:584` and in `PaperCity`, before any rule reads it.
* **The receiver's answer** (a mind; the player reads the same numbers on the card):

| offer | accepted when | else |
|---|---|---|
| truce, while at war | **cannot refuse** (ruling 1): `Endurance < MinWarDays` OR `LossesThisWar ≥ LossesToSueForPeace` — accepted for the player too, the card says so. Otherwise accepted when the money clears its grievance to under `RetakeBusinessAt` (40), OR it reads the sender as stronger (`Estimate`) and its own grievance is under `AttackBusinessAt` (60) | refused: "they have taken too much" |
| truce, while at peace | no crew of the receiver is posted on or routed through the sender's blocks (`CrewBlockLook`, `RoundOut`) — a truce engages trespassers on both grounds (`Engagement.cs:11-13`) | refused: "our men work those streets" (advice on the player's card; he may still accept) |
| peace, while in truce | both grievances under `PeaceGrievance` (20) after the money, OR the automatic rule would fire within 2 days | refused: "not yet" |
| peace, while at war | never — a war ends in a truce first | refused |

* **The guarded write.** Acceptance sets `Agreed(pair, stance, day)` and holds the money in `Escrow`; `ApplyPending` lands the agreed stance over any harder pending written the same day — a defection's `Sour`, a re-declaration — unless a grievance note of `AgreementBreaksAt` (35, a killing) or more arrived after acceptance, in which case the pending stands and the escrow refunds. The money crosses at midnight with the stance, never before.
* The automatic truce→peace after `PeaceAfterDays` stays; it is what happens when nobody talks.

## 5. Words with answers, and what money can clear (DIPL-003)

A warning, a threat and a bill are proposals with a demand, so they can be answered. The mind's three `Word`s in `Feud` become `Warn(block)`, `Threaten(block)` and `Bill(money)`; the player gets the same three on THE TABLE.

| word | terms | COMPLY | REFUSE / expiry (day +2) |
|---|---|---|---|
| WARNING | keep off `BlockId` | `KeepOff(receiver, block, day + ComplyDays)` — refused at the choke points, crews sent home, the mind skips it | `WarningIgnored` (10) at once; the 48 h `SweepWarnings` is deleted — expiry is the note |
| THREAT | keep off `BlockId` | as above | `WarningIgnored` again; the ladder does the rest |
| BILL | pay `Money` | `Transfer`; the sender's grievance drops by `Money / CompensationPerPoint` under the cap | `WarningIgnored`; the ladder reads DemandCompensation → RetakeBusiness on its own |

* **The bill is priced from the grievance**, not from the doors: `(grievance − ThreatAt) × CompensationPerPoint`, so paying in full lands exactly at Threat and the same bill is not sent again next think (today's `Shakedown × Theirs` at `HouseMind.cs:667-669` clears three points of thirty).
* A mind complies with a warning from a house it reads as stronger while its own grievance toward that house is under `ThreatAt`; pays a bill when the same holds and the safe covers it over the reserve; refuses otherwise. The player decides.
* **What money can clear — ruling 4, in plain words.** Grievance is one number per pair. Without a cap, every wrong has a dollar price: a killing (35) is $7,000, and a truce with $8,000 attached erases it the same afternoon, so the war the ladder exists to make visible never comes. So: every dollar that clears grievance — bill, truce money, terms — counts against ONE cap of `CompensationCapPerDay` (20 points) per pair per day; and for `KillingFloorDays` after a `ManKilled`, money cannot take the pair under `ThreatAt` — a death is not bought back below a threat. Time still clears it (`GrievanceDecayPerDay`).

## 6. Tribute for every house (DIPL-004)

* **One pass in `Underworld.DayTick`**, after every runner's own tick, iterated in sorted gang-id order: `Tribute.Assess` for every house against the holdings the runtime hands it (the same `HoldingsOf` shape the player's runner has, filled from `Look`'s blocks for a mind and from the paper city headless), then `Settle` as a `Transfer` to `levy.GangId`. `payTribute: house.IsPlayer` (`Underworld.cs:350`) goes; `CollectTribute` leaves `CampaignRunner.DayTick`. D20 in `Docs/rival-families.md` is rewritten.
* Unpaid → `TributeUnpaid` (25) as today, by whichever house went unpaid.
* **TRIBUTE TERMS** — `TributeTerms(money)`: the levied house offers a fixed envelope instead of the derived one for `3 × CycleDays`. A levying mind accepts when it is broke (`Endurance < MinWarDays`) or the offer is at least half the derived figure. A levying house may propose terms upward; a levied mind accepts only when it reads the sender as stronger and can pay. Overdue terms sour as a levy does.

## 7. The line (DIPL-006)

`Line(blockIds[], LineDays)`: a border pact. Both houses are `KeepOff` on the named blocks for `LineDays` — the choke points refuse, the crews go home, the minds skip. Crossing it — a door switched or attacked across the line — files `LineCrossed` (20) on top of the ordinary grievance. A mind proposes a line when its `BorderPressure` toward a neighbour is at the cap (`BorderPressureCap` 40) and neither house could pay for a war; it accepts one on the same test. Not a stance: `Engagement` is untouched. The player picks the blocks on the map through `MapTargeting.Surface`.

## 8. The man, the pact, and the sit-down in person (DIPL-005, 007, 008)

* **RANSOM** — the kidnap's effect moves from the scene edge (`OutfitDirector.TakeHim`) into the runner (`OrderEffects`/`CampaignRunner`), so the paper city sees a kidnap, and its printed price becomes `Ransom(characterId, KidnapCut)` from the kidnapper to the man's house, open for `KidnapDays`. PAY: `Transfer`, `BackOnDay = day + 1` (still to a bed); REFUSE or expiry: he comes back after three days as today. A mind pays for a lieutenant always if it can, for a hood when the safe covers it over the reserve.
* **PACT** — `Pact(PactDays)`: mutual defence, honoured by the book, not by a decision. A pure `HouseDiplomacy.HonourPacts(relations, book, day)` runs in `Underworld.DayTick` **after `ApplyPending`**: for every war that landed this midnight on a party to a standing pact, it writes the partner's pending stance toward the declarer at War for the **next** midnight, flagged `ByPact`; a war flagged `ByPact` is never "a declaration on a party" for any other pact, so nothing cascades. The player signs knowing it: the sheet says "a pact declares for you"; there is no card at honour time. A mind that cannot pay (`Endurance < MinWarDays`) when honour comes does not honour: `PactBroken` (30) to the abandoned party and an incident in every book ("the Morettis left the Costas to it"). A mind proposes a pact to a house at peace with it when a third house's ladder toward it has reached `RetakeBusiness`. **JOIN MY WAR** — `JoinWar(third, money)`: the pact for one war, with money; accepted on the pact's test plus the money clearing the receiver's own grievance toward the sender under the cap.
* **THE SIT-DOWN** — every proposal above goes by telephone from the desk. A house may instead send it with a man: `OrderType.SitDown` (appended, listed in `OrderEffects.Built`; Point target = the other house's front, or a Pub/Cafe door of a third house at peace with both; 3 hours; `Streetwise` floor; no cost; its own spec, not EPIC 40's `Meet`). A job is walked by the lieutenant's line, as every job is; the envoy is that lieutenant. **The margin is numeric:** every dollar test in §4–§6 (the money that clears to a threshold, the half-figure on terms) is reduced by `EnvoyMarginPerHalfStep` per half-star of the envoy's `Streetwise`, cap 20 %; the `Estimate` comparison reads the sender's side up by the same. **The host may AMBUSH**: the player from the card when hosting; a mind never in this epic (`MindAmbushes = false`). An ambush is a `SitDownAmbush` command that marks the host's unit `provoked`, so `Engagement.May` fires at Peace; the envoy dies where he stands: `ManKilled` (35) plus `SitDownBetrayed` (40) to the sender, printed in every book. The Don never goes (ruling A4).

## 9. The table — the product (DIPL-009, blocked by EPIC 41)

Ruling 7: this ticket opens when EPIC 41's retail gives houses different outlets, and CONN-004 (GAN-405) carries two amendments written into it today: **`BuyerCapacity`** (the connection's buyer takes at most N kilos a week; surplus kilos have no outlet) and **the desk rule** (a house never quotes a kilo under what its own outlet would pay for it, plus `MinMarginPerKilo`). Without both, the desk is a mug: every kilo is worth a flat 20,000 to every house, and the only trades that clear are ones where the seller loses.

This epic knows one thing about importing — whether a house imports — and nothing else: **an importer is a house at `ConnectionStage.Supplier`**; the seller's cost is its `Connection` weighted landed cost per kilo (its terms price, or what it paid for bought lots); its stock is `Connection.kilos` less `BuyerCapacity` for the week; bought kilos land in the buyer's Stash (a `Stash` room is the precondition, EPIC 40's own); a carrier collared with product carries `Deed.Trafficking`.

**The quote** — `Outfit/ProductDeal.cs`, pure, numbers in `ProductDealConfig`:

```
quote = max( cost × kilos × markup,  ownOutlet(kilos) + MinMarginPerKilo )   × haze(day, seller, buyer), rounded to $500
markup = stance (Peace 1.40 / Truce 2.00 / War: no sale)
       + the SELLER's ladder toward the buyer (Warning +0.10, Threat +0.20, DemandCompensation and above: embargo)
       − volume (0.05 per full 5 kilos, cap 0.10) − loyalty (0.05 per 4 honoured deals, cap 0.10)
       clamped to ≥ Floor 1.25          ← the user's rule: always dearer than it cost them
haze: deterministic in ±10 %, keyed (day, seller, buyer), applied so the result is still ≥ Floor × cost — two sellers with different costs can print the same quote
broke seller (Endurance < MinWarDays): the Floor.   No stock: no sale.   MaxKilosPerDeal 10.   A long partner at Peace reaches the floor after eight honoured deals — by design.
```

The cost never crosses the wall: the token scan gains `pricePerKilo` and `ProductDeal`. **ASK is an act, not a scan**: the answer comes at the next day tick and stands that day; a house not at Supplier answers "nothing for us". Nothing prints who brings it in.

**The verbs**, proposals of §3 over `ProductDeal`: **ASK** · **BUY** (the job `OrderType.BuyProduct`, in `Built`; Point = the seller's front; **paid on arrival**, as CONN-003's test buy — the kilos leave the seller's Stash and land in the buyer's on arrival; the crew walks home as every job does; no return leg, no cash bag on the street; the reservation is released on cancel or a fallen crew; a crew collared on the way carries the job's product on paper — `Trafficking`) · **OFFER** (a `Roll` on `Streetwise` keyed `(day, quote)` like every job roll; won: the offer stands if `≥ Floor × cost × kilos`; lost: refused and `InsultingOffer` 5; one per quote) · **STANDING** (N kilos every `CycleDays`, settled in **one pure pass in `Underworld.DayTick` after every house's load has landed**, iterated in sorted `(buyer, seller)` order) · **FRONT** (the kilos now, the money in 5 days at markup +0.25; **an overdue front is an embargo by rule** until paid, no second front while one is open, and the debt is a `Levy`-shaped claim the seller's `Feud` bills for; `DebtUnpaid` 25 on top; paid late clears and never counts as honoured).

**The minds** — the seller answers at the desk. The buyer proposes in the `Diplomacy` step (§3), gated by the reserve and `QuietThinks` like `Buy`: a Stash, `Safe − price ≥ reserve`, `OutletLook()` (its own outlet for the next kilo — 0 when its buyer is at capacity or it has none) above the cheapest `QuoteLook(seller, kilos)` among houses not at war with it by `MinMarginPerKilo` (1,000). The seller prints "sold N kg to X" in both books.

## 10. The ledger (DIPL-010)

The FAMILIES card is full (`FamilyCardH` 316): its three stance buttons become **one button, THE TABLE**, and the STANDING row gains a marker when they have asked us something ("TRUCE · THEY ASK"). THE TABLE is a sheet in the ledger skin for one house: **DECLARE WAR · OFFER TRUCE · OFFER PEACE** with their money; PENDING (their open proposal, ANSWER); WORD (warning / threat / bill — the block or the figure); TRIBUTE TERMS; THE LINE (blocks picked on the map); RANSOM when there is a man to pay for; PACT / JOIN MY WAR; SIT-DOWN (the envoy picked, the proposal carried, the AMBUSH choice when hosting); and, once DIPL-009 opens, the product stepper and its five verbs. Each refuses in the gateway's words. Under it, THE RECORD: the last thirty words between the two houses, what was answered and when. A standing order and terms show as Jobs rows on Finances; BETWEEN THE HOUSES is a Finances line.

An incoming proposal is a PENDING row on the sheet and on the ledger's front page first; when STREET-002's telephone exists, it also rings as a card with ACCEPT / REFUSE, spoken by the desk ("Sal says the Morettis are offering a truce, four thousand with it"), held by Esc for its days like every card. Type through `LedgerStyle`, widths measured, no `GraphicRaycaster`, no animation.

## 11. The minds' moves, in one place

| move | where | when |
|---|---|---|
| answer any proposal | at the desk | at once, by the tables above |
| offer a truce (was `Stand(Truce)`) | the `Diplomacy` step | as today's two rules, now a proposal; never twice while one is open |
| warning / threat / bill (was `Word`) | the `Diplomacy` step | as today, now answerable; the bill priced from the grievance |
| the line | the `Diplomacy` step | `BorderPressure` at cap toward a neighbour and neither could pay for a war |
| tribute terms | the `Diplomacy` step | levied and `Endurance < MinWarDays`: offers half |
| ransom | on the kidnap's effect | always, `KidnapCut` |
| pact | the `Diplomacy` step | a third house's ladder toward it at `RetakeBusiness` or above |
| join my war | the `Diplomacy` step | at war and `LossesThisWar ≥ 2`, to a house at peace with it |
| honour a pact | `HonourPacts` at midnight | by the book; breaks when it cannot pay |
| buy product | the `Diplomacy` step | §9, when DIPL-009 opens |
| sell a street, hand a man over, gift, sit down in person, ambush | — | never in this epic |

## 12. Out of scope, and the seams left

SELL THE STREET (needs the sale to move the posting and an exemption from `PressTowardSwitch`); HAND HIM OVER (needs the shooter's id recorded where `AttributeShooting` finds the gang, or "a man of the rank we lost"); GIFT; exclusivity in a district; tribute for passage; a broker between two houses at war; a hostage-guarantor; a mind that ambushes; the player as seller and retail (EPIC 41). Every one lands on `Proposal.Kind`.

## 13. Contracts — `gangsters_diplomacy_tests`

1. A proposal answered by a mind is answered the same on two runs of one seed; the player's open proposal expires on day +3 (a word on +2) as a silent refusal (a word's as `WarningIgnored`).
2. A truce offered to a house that cannot pay through the war, or has lost `LossesToSueForPeace`, is accepted whatever it is owed — mind and player; the other three conditions of §4 each accept and their absence refuses; peace at war is always refused; truce at peace is refused while a crew of the receiver works the sender's blocks.
3. **The guarded write**: accept a truce, then `Sour` the pair the same day — midnight lands the truce and the escrow crosses; accept, then a `ManKilled` — midnight lands the harder pending and the escrow refunds. `SetPending(Truce)` then `SetPending(War)` without an agreement still reads War (the slot is unchanged for the unilateral path).
4. `LossesThisWar` on the view equals `MenLostTo` for the house at war.
5. A warning complied with refuses `ApproachBusiness` on the block at both choke points for `ComplyDays`, sends the posted crew home, and files nothing; refused, `WarningIgnored` is noted once; the sweep is gone.
6. A bill priced from the grievance, paid in full, lands exactly at `ThreatAt`; every clearing dollar counts against one daily cap of 20 points; inside `KillingFloorDays` of a `ManKilled` no money takes the pair under `ThreatAt`.
7. Tribute: every house is assessed and settled in one pass in sorted order; the payee's safe rises by what the payer's fell, dirty, on the `FromHouses` line; agreed terms replace the derived figure for three cycles; overdue terms sour as a levy does; the paper city shows AI-to-AI envelopes.
8. The line refuses across it from both sides in the gateway's words; a door switched across it files `LineCrossed` on top; the mind skips the blocks while it stands and returns after `LineDays`.
9. A ransom paid returns the man on `day + 1` to a bed; refused, after `KidnapDays`; the paper city shows a kidnap and a ransom.
10. A pact honoured writes the partner's pending War for the NEXT midnight, flagged `ByPact`; a `ByPact` war triggers no other pact (three pacts in a ring, one declaration, exactly one honour); a partner that cannot pay files `PactBroken` and prints in every book.
11. A sit-down's envoy reduces the dollar tests by 2 % per half-step, cap 20 %; an ambush files `ManKilled` and `SitDownBetrayed`, fires at Peace, and never happens from a mind.
12. `OpenProposalLook` refuses a duplicate in words; a broke house proposes a truce once per expiry, not once per think.
13. Snapshot round-trip carries the book, keep-offs, agreements, escrow, lines, pacts, terms; a file with none of the blocks reads empty and `SaveTests` stays green at version 3.
14. Yardstick: `gangsters_underworld_sim --days 60 --table` prints proposals made, answered and how, money that crossed (`FromHouses`/`ToHouses`), lines and pacts standing, kidnaps and ransoms.
15. The product (when DIPL-009 opens): `quote ≥ max(1.25 × cost, ownOutlet + margin) × kilos` over the whole grid; the haze never breaks the floor and two sellers can print one quote; War and embargo refuse the mind and the ledger in the same words; an overdue front embargoes by rule and a second front is refused; BUY pays on arrival and releases the reservation on cancel; the standing pass settles after every load in sorted order; `HouseMind.cs` contains neither `pricePerKilo` nor `ProductDeal`.

## 14. Tickets, in order

| Ticket | What |
|---|---|
| DIPL-001 | The proposal book — `HouseDiplomacy`, `Proposal`, `Answer`, `Propose`/`Reply` in both `Carry` switches, the `Diplomacy` step before `Walk`, `OpenProposalLook`, `KeepOff` + `KeepOffLook` + the two choke-point refusals, `Underworld.Transfer` + the two sheet lines, `DiplomacyConfig`, the DTOs (arrays), the five grievance kinds named in `AmountOf`, the incident lines |
| DIPL-002 | The stance by agreement — truce and peace as proposals, the beaten cannot refuse, `LossesThisWar` wired, the guarded `ApplyPending` with `Agreed` and `Escrow`, the compensation cap and the killing floor, `RelationsTests` rewritten |
| DIPL-003 | Words with answers — `Warn`/`Threaten`/`Bill`, COMPLY through keep-off, expiry at +2 as the note, `SweepWarnings` deleted, the bill priced from the grievance, the player's own words |
| DIPL-004 | Tribute for every house — the one pass in `Underworld.DayTick`, `Transfer` to the payee, `payTribute` gone, TRIBUTE TERMS, D20 rewritten |
| DIPL-005 | Ransom — the kidnap's effect into the runner, `Ransom`, PAY / REFUSE |
| DIPL-006 | The line — `Line`, keep-off both sides, `LineCrossed`, the mind's proposal and answer on `BorderPressure` |
| DIPL-007 | The pact — `Pact`, `JoinWar`, `HonourPacts` after `ApplyPending` for the next midnight, `ByPact`, `PactBroken` in every book |
| DIPL-008 | The sit-down — `OrderType.SitDown` in `Built`, the numeric envoy margin, the host's ambush as a provoked engagement, `SitDownBetrayed` |
| DIPL-009 | The table — **blocked by EPIC 41** and the GAN-405 amendments; `HouseDeals`, `ProductDeal` with the desk rule and the haze, the five verbs, `BuyProduct` paid on arrival, the standing pass, the buyer in the `Diplomacy` step, the token scan |
| DIPL-010 | THE TABLE sheet and the STANDING marker, THE RECORD, BETWEEN THE HOUSES, the telephone card when STREET-002 exists; `gangsters_diplomacy_tests`, `gangsters_diplomacy_probe`; `Docs/diplomacy.md`, `Docs/rival-families.md` (D20, the words), `Docs/economy-prices.md` §6; memory; every ticket and the epic to Done |

DIPL-001..008 and DIPL-010's ledger half need nothing from EPIC 40; DIPL-010's telephone half waits for STREET-002; DIPL-009 waits for EPIC 41.

## 15. Still open for the user

1. Ruling 4's numbers (cap 20 a day, the killing floor 30 days at `ThreatAt`) — taken from the contrarian's recommendation; veto or change.
2. Every other number in §3 and §9 is a first guess; rule on them after contract 14's yardstick, the way EPIC 40 rules its weights after the probe.

## 16. How it was done (2026-09-05)

Built DIPL-001..008 and DIPL-010's ledger half in one session; DIPL-009 stays blocked. The
map is `Docs/diplomacy.md`. What the build changed against the brief:

* The table's midnight runs AFTER every runner's tick in `Underworld.DayTick`: a runner's
  tick clears its desk of the night's incidents, so a line printed before it was gone by
  morning. Escrows and pacts follow the wars that landed; tribute settles after them.
* The mind's word order is the ladder's own: at peace and owed a threat's worth the first
  word is the truce, the warning / threat / bill are said inside the truce. The line comes
  before the truce for a squeezed pair (§7).
* `HasOpen` answers true for a proposal accepted today, so an agreed truce is not asked for
  again at the next think.
* The sit-down's job is `JobResolution.Roll` for the pipeline's sake but delivers on any
  outcome; the envoy's Streetwise is read when he leaves and travels on the proposal.
* The ambush is on paper only (`HouseOps.Ambush`); the provoked engagement on the pavement
  is a seam.
* The telephone card is not wired; the FAMILIES card's THE TABLE key and its sheet are the
  inbox.
* The tribute pass hands every runner the holdings through the same `HoldingsOf` the
  player's has (the director sets it for every house; the paper city's
  `CollectHoldings` on the sim).

Verified: `gangsters_diplomacy_tests` (30 contracts), relations, house, underworld, save,
ledger, rack, learning, economy, loyalty - green in the editor and offline.

