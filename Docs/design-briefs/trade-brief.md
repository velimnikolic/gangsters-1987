# EPIC 41 — The Trade: the product table, dealers on the block, and what a kilo is worth to THIS house

Design brief, **draft 2**, 2026-09-05 — draft 1 reworked the same evening after the contrarian pass (§0b) and the user's rulings (§0a). Sits after **EPIC 40 The Connection** (GAN-395, `Docs/connection.md`) and unblocks **DIPL-009** (GAN-417, the product between houses) and **REC-008** (GAN-439, the chemist), both of which move under this epic.

Read first: `Docs/connection.md`, `Docs/economy-prices.md` §6–§7, `Docs/design-briefs/diplomacy-brief.md` §9, `Outfit/Connection.cs` (`Load`, `Sell`, `OutletForNextKilo`, `BuyerCapacity`), `Outfit/HouseView.cs`, `Outfit/Underworld.cs` (`DayTick`, the tribute pass), `Personnel/RosterOps.cs` (`SetKeeper` / `CanKeep`), `Property/FlatDay.cs`, `Tests/PaperCity.cs`, `RoadDemo/BagCarry.cs` (the one-man unit off the line), `Docs/racket-collections.md`.

## 0a. The user's rulings (2026-09-05)

1. **Retail now; the port later.** This epic is the trade — product, dealers, the outlet per house. The port as geography, the watched box, trucks, customs, THE RIVAL path, the conspiracy case: seams, a later epic ("The Waterfront").
2. **A dealer is a man.** Product is sold only by a hood posted as `Duty.Dealer`, off the street line, like the bag man. Pull him and the corner goes dark that moment.
3. **Dealers work a BLOCK, not a door.** In the block file the player picks dealers off the roster of the lieutenant who holds the block; they walk the street physically and deal. **They may be sent to any block** — a block where another house already deals is a contested corner (§3). The shop owners have nothing to do with it; they pay their racket and that is all.
4. **One product** — cocaine; crack, heroin, marijuana are rows in §6, not built.
5. **A street deed under the statute** — `Deed.Dealing` for a dealer collared under 400 g.
6. **The minds trade** — the same door for the player and the twenty houses; **but the minds' ticket waits for the FBI raid on the safe (WASH-003, GAN-466)**, so twenty-one houses do not print money before dirty money has a sink.
7. **The trade dwarfs the racket, on purpose.** The balance is heat, the collar, the raid and EPIC 46's laundering ceiling; the draws are anchored in 1987 sources and TUNED on the yardstick, never guessed.
8. **The vice squad watches the HOUSE, not the block** — one trade-heat figure per house, falling on a half-life in game days, so twenty cafés are as hot as one club.
9. **The warning is a slip, not a card** — THE CORNER IS HOT on the block file and THE WIRE, answered by midnight; unanswered means CEASE.
10. **The wholesaler refuses stepped product** — cut product sells on the street only.
11. **The paper city gets real doors and real heat** so the yardstick measures the balanced half of the mechanic, not the free half.
12. **The customers are people.** In the live city pedestrians walk up to a dealer and buy; the beat can collar him at the hand-off; a rival can shoot him.

## 0b. What the contrarian pass changed (draft 1 → draft 2)

* The paper city could not deal one door from the outlet table except a betting shop, had no attention and a sting fixed at zero — every "measured" draw would have been measured on a city with no club and no police. → TRADE-002 gives `PaperCity` the archetype spread of the measured core (one Nightclub, one Fairground, six Diners, 187 Cafés, seed 1987) and the per-HOUSE trade heat is pure, so every balancing contract runs headless.
* Per-block attention decays on an 8 h half-life against a 40 gate: a café at 15 g/day could never be hot at any per-gram figure that left the nightclub sellable; a mind that ceases over the gate is never stung; laundering does not exist; trade cash re-weights `Endurance` and every EPIC 42 table. → ruling 8 (the vice squad watches the house), ruling 6 (the minds' ticket waits for WASH-003), the contrarian's "Endurance reads clean money only" **rejected** — racket money is dirty too and it would have re-balanced EPIC 42 overnight.
* THE PHONE holds one card three days and the midnight order sells before the phone rings — the player would be collared before being asked. → ruling 9, the slip, with CEASE as the unanswered default (the opposite of the captain's LET THEM COME, on purpose).
* The day-tick sale in `CampaignRunner` could not see doors, presence or the gate. → one pure pass in `Underworld.DayTick` after every house's load, with each house's `Look`, sorted gang order — DIPL-004's tribute precedent.
* `Duty.Dealer` had no op and `AssignToCrew` clears a duty without clearing the flat's `KeeperId` — a dealer dragged into a crew would keep selling. → `RosterOps.SetDealer / ClearDealer` mirroring the keeper pair, the row read off the MARK, and the same hole closed for keepers in the same commit.
* Three readers of `Kilos` would lie on a sub-kilo remainder and a `grams` DTO field would reset a saved room to zero. → `Kilos` becomes a getter, heat reads `(Grams + 999) / 1000`, restore reads `grams > 0 ? grams : kilos × 1000`, contract `AnOldFileKeepsItsKilos`.
* The buyer's flat $20,000 made a step free money and "one purity per room" left DIPL-009's BUY undefined. → ruling 10 and the room holds `PureGrams` + `Grams`, the band derived at display.
* `OutletForNextKilo` cannot live on `Connection` and two definitions of `OutletLook` existed (GAN-417's and draft 1's). → `HouseView.OutletLook` is the one number; TRADE-004 rewrites GAN-417's sentence and moves "sell to the buyer only what the dealers cannot take" into the same commit.
* The sting seam (`WatchOnTheDoor`, `Sting(gangId, job)`) takes a job and a crew and subtracts the Colombian's trust; a dealer is neither. → its own pair, `DealerCollared`, odds off the house's trade heat and the dealer's `Streetwise`, no trust term; the dealer is a one-man unit on the street, so EPIC 34's walk-up has a body.
* Cease/resume would flap at the think cadence across 21 houses, one incident each. → hysteresis (cease at the gate, resume at half), one incident per state change per day.
* D9 on posting gated nothing (the man is already on the payroll); the crew he leaves cannot refill because `WeeklyTake` counts dues only. → D9 dropped from posting; last week's trade income enters `WeeklyTake`.
* Minor, all adopted: `TradeIncome` is "of which" under `IllegalIncome`; a `Ceased` corner resumes on load only by the player's RESUME key (the mind re-checks the heat, which IS saved); draws anchored in §6 with a source; the three places that define EPIC 41 as the port rewritten in TRADE-000; a dark Stash reads `Dark(NoStock)` "the room is dark"; `Dealing` carries no mandatory minimum and says so.

## 1. What exists, verified 2026-09-05

A house at `Supplier` gets `MinLoad` kilos a week into a `UnitRole.Stash` at the terms price (`Connection.Load`); every kilo is worth one flat number to every house (`BuyerPrice` $20,000, `BuyerCapacity` a week, `Connection.Sell`); `OutletForNextKilo` answers that or 0 and is the only outlet DIPL-009 can read. §6 knows the street ($100 a gram) and says "not modelled". The Stash raid (`FlatDay`) seizes and seals, no case; only a sting on a buy opens `Deed.Trafficking`. The minds sell the think after a load lands (`HouseMind.cs:1933`). Nothing in the code is a port. `Duty` holds `None, Collector, Escort, Keeper`; the bag man is a one-man unit off the street line (`BagCarry`, EPIC 30). The paper city deals 18 generic archetypes and `AttentionLook = 0`.

## 2. The product — `Outfit/Product.cs`, pure

```
Room          PureGrams (as landed) and Grams (what is in the room now); Kilos = Grams / 1000, a getter
Band          derived at display from Grams / PureGrams:  Pure (1×) | Street (≤ 2×) | Cut (> 2×)
PricePerGram  Pure $100 · Street $70 · Cut $40    (1987 Miami; §6 anchors the $100 — DEA STRIDE 1987)
Step(skill)   the chemist (REC-008): one kilo a day → (1 + skill/10) kilos; PureGrams unchanged, Grams up — the band follows
The buyer     SELL TO HIS BUYER takes whole kilos of PURE only (Grams == PureGrams); stepped product is refused in words
```

Every reader of `Kilos` (`CampaignRunner.cs:883` heat, `HouseMind.cs:1934`, `HouseOps.cs:635`, the Stash card, `UnderworldSim`, `PipelineCommands`) compiles against the getter; heat reads `(Grams + 999) / 1000`. `ConnectionDto` gains `grams` and `pureGrams`; restore reads `grams > 0 ? grams : kilos × 1000`; the writer keeps writing `kilos`. Prices in `EconomyPrices` and §6 first.

## 3. The dealers — `Outfit/Dealers.cs`, pure

**The book.** Per house, `Dealers`: rows `(CharacterId, BlockId, SinceDay, Dark: None | NoStock | NoDealer | Ceased, SoldToday)`; saved as a nullable `dealers` array on `HouseDto`, no version bump. A man is a dealer by the MARK: `Duty.Dealer` (appended). `RosterOps.SetDealer(roster, characterId)` / `ClearDealer` mirror `SetKeeper` / the keeper clear; `CanDeal(man)`: a hood, `Active`, `Duty.None`, not the bag man, not a keeper, in the crew of a lieutenant (the Boss's reserve may deal too). `AssignToCrew` / `AssignToPool` clear the mark AND the row — one choke point; `Apartments.KeeperId` gets the same fix (`FlatDay` reads `Duty == Keeper`).

**Posting.** `HouseOps.PostDealer(house, characterId, blockId, day)` / `PullDealer` — the one door for the player and the minds. Refuses in words: not a hood / already marked; no Stash with grams ("nothing to sell — the room is empty"); `DealersPerBlock` (3) already there for this house. The block file is where the player picks: the roster of the lieutenant responsible for the block first, every other crew after (ruling 3).

**The block's draw** — `BlockDraw(block)` in grams a day, the block's foot traffic, summed over the businesses standing on it by archetype (the outlet table, anchored in §6 and tuned in TRADE-008; dormant rows kept and marked):

| archetype | g/day | in the core city (seed 1987) |
|---|---|---|
| Nightclub | 120 | one — the prize |
| Fairground | 80 | one |
| Diner | 40 | six |
| Pizzeria, BettingShop | 30 | |
| Gym | 20 | |
| Cafe | 15 | 187 — the long tail |
| every other archetype | 0 | |
| Pub, Casino, Hotel, Restaurant | dormant | not in the core city today |

A block's draw is SHARED by every dealer on it, ours and theirs: `Share = BlockDraw / dealersOnBlock`. A dealer sells `min(Share, DealerCap × (0.6 + 0.1 × Streetwise stars))`, `DealerCap` 60 g. A block the house does not control sells at `OffTurfFactor` (0.7) — the street knows whose ground it is. Every factor prints in words on the block file.

**A contested corner.** Two houses' dealers on one block: the draw splits (above), and the house that was there first files `GrievanceKind.CornerContested` (appended, named in `HouseRelationsConfig.AmountOf`, 5 a day) against the newcomer — the ladder does the rest (a Warning through THE TABLE, or worse); at war the two units engage on sight in the live city (the existing engagement seam, no new behaviour). The block file says "Falcone's men are on this corner too — half the trade".

**The sale — `Dealers.DayTick`**, one pure pass in `Underworld.DayTick` after every house's load has landed, iterated in sorted gang order with each house's `Look` (the tribute precedent, `Underworld.cs:517-534`): per dealer in sorted `(BlockId, CharacterId)` order, `grams = min(draw, roomGrams)`; money `grams × PricePerGram(band)` lands dirty-first in the safe as `IllegalIncome`, with `DaySheet.TradeIncome` as its "of which" (THE TRADE on Finances); the room's `Grams` fall; the house's trade heat rises (§4). One wire line a day per house: "The corners moved 340 grams for $27,000". A dealer with nothing to sell is `Dark(NoStock)` and the line says so; a dark Stash (keeper in a cell) is the same.

`HouseMind.WeeklyTake` gains last week's `TradeIncome` so the growth gate sees the money; posting a dealer has no D9 test (he is already paid).

## 4. Heat and the law

* **Trade heat, per house** — `House.TradeHeat`, 0–100, `+HeatPerGram × grams` each day, half-life `TradeHeatHalfLifeDays` (3) in game days; saved (a memory, like WASH-002's suspicion — a load must not amnesty it). The vice squad watches the house, so twenty cafés are as hot as one club. `HouseView.TradeHeatLook` reads it.
* **The corner deposit** — each dealer also deposits `CornerAttentionPerDay` on his block through the one multiplier (`AttentionFactorLook`, FIX-003; `NotePoliceAttention` at the caller until it exists — the report-row shape of `FlatHeatDeposit`, never `TerritoryFear` from the pure class), so the beat and the racket see the corner. The vice gate never reads this pool.
* **THE CORNER IS HOT** — when `TradeHeat` crosses `ViceGate` (40, §7): a tonight slip on the block file of every block with a dealer and on THE WIRE — CEASE (every dealer `Dark(Ceased)`, no sale, no deposit) / KEEP SELLING (the collar odds in words) — answered by midnight; **unanswered is CEASE**. RESUME on the block file lights them again. Hysteresis: hot at the gate, cold under half of it; one incident per state change per day (`IncidentKind.CornersCeased` / `CornersResumed`, appended, `Notability.WeightOf` named). The minds cease at the gate and resume under half, never KEEP SELLING.
* **The collar** — while hot and selling, each dealer rolls `DealerCollared` per day on his own stream `(citySeed, gangId, characterId, day)`: odds `(TradeHeat − gate) / (100 − gate) × (1 − 0.1 × Streetwise stars)`, no trust term. Collared: he carries the day's grams on paper — `Deed.Dealing` under 400 g, `Deed.Trafficking` at or over; the row goes `Dark(NoDealer)`; the room is untouched (the Stash raid is `FlatDay`'s). Live: the beat's walk-up at his position (EPIC 34); paper: the book jails him. The seam `CampaignRunner.DealerCollared: Func<int gang, int characterId, int grams, bool>` answers whether the street took him.
* **`Deed.Dealing`** — appended, in the five exhaustive switches (`Verdict.BaseFor`, `BandLow`, `BandHigh`, `ChargeFor`, `Bail`; the enumerating test extends): Fla. Stat. 893.13 sale of cocaine — band 4–10 days, bail $10,000, "Sale of a controlled substance", no mandatory minimum (unlike Trafficking, and it says so). In §7 first.

## 5. The outlet — what DIPL-009 waits for

`HouseView.OutletLook()` is THE number: what the house's own dealers would fetch for the next kilo this week — their remaining draw for the week × `PricePerGram(band)` — and when they cannot take it, the buyer's floor (`BuyerPrice` while `BuyerCapacity` has room and the product is pure, else 0). `Connection.OutletForNextKilo` goes; GAN-417's sentence ("0 when the buyer is at capacity or there is none") is rewritten to this. The mind sells to the buyer only what the dealers will not take this week (`SellKilos` gated on `OutletLook`), in the same commit. The token scan of `HouseMindTests` stays green. Two houses print two numbers for one kilo — the house with the club sees $100,000, the house with two cafés sees $3,000 a day of it and the floor for the rest. DIPL-009 opens the day this lands.

## 6. The minds — TRADE-005, blocked by WASH-003

A mind at `Supplier` with grams in the room posts a dealer to the highest-draw block it CONTROLS (the one predicate), then its neighbours, off the crew responsible for it; never onto a block where another house already deals unless at war with it; pulls a `Dark(NoStock)` dealer after `DealerIdleDays`; ceases at the gate and resumes under half. `HouseIntentKind.PostDealer` / `PullDealer` appended, carried by both `Carry` switches. The paper city (TRADE-002) deals the measured archetype spread and carries the house's trade heat, so the yardstick plays the same game headless.

## 7. The street — TRADE-006

A dealer is a one-man unit off the street line (the bag man's shape, `BagCarry` / EPIC 30), wandering his block's pavements on the walk graph; unarmed; unpickable for orders except PULL. Customers: CityLife pedestrians (EPIC 39) get a `Buy` errand toward a dealer at a rate derived from the day's grams (one customer per `GramsPerCustomer`, 2), a short stop, and walk on — the rendering of the paper's number, never a second count (scenes are rigs; the model decides). The beat (EPIC 34's police units) collars him at the hand-off when `DealerCollared` says so; a rival at war engages on sight. His marker on the turf map and minimap; the block file says where he stands.

## 8. The surfaces — TRADE-007

The block file: DEALERS — the roster picker (the responsible lieutenant's crew first), each dealer's row (draw and its factors in words, today's grams, `Dark` reason), PULL, RESUME, the contested line, THE CORNER IS HOT slip with its two keys. The Stash card: grams, the band, the dealers fed from it, the buyer's floor and his refusal. Finances: THE TRADE row. THE WIRE: the daily line, the slip, the collar. The man's file: "dealing on Riverside since day 12". The chemist's card (REC-008) reads `Product.Step`'s sentence. All type through `LedgerStyle`; widths measured.

## 9. Out of scope, and the seams

The port as geography, the watched box, trucks, customs (`PressKind.Seizure`), THE RIVAL path, the conspiracy case — "The Waterfront", a later epic; the room-to-corner move is a paper move its trucks will want, named here. Crack, heroin, marijuana — §6 rows. The owner of a shop on the block — nothing, by ruling 3. Street price moving with supply — not modelled. Several bands in one room — derived, not modelled.

## 10. Tickets

* **TRADE-000** — The ruling in words: this brief's rulings into `Docs/connection.md` (§"What EPIC 41 picks up" rewritten as "The Waterfront"), §6 (the product table, the draws with their source, the vice gate) and §7 (`Dealing`), GAN-417's `OutletLook` sentence, GAN-439's blocked-by text. Before any code.
* **TRADE-001** — `Product` (pure): `PureGrams` / `Grams`, the band, `PricePerGram`, `Step`, the buyer's refusal, `Kilos` a getter, the DTO and the old-file restore; `gangsters_trade_tests`.
* **TRADE-002** — `Dealers` (pure): the book, `Duty.Dealer` and the roster pair (the keeper hole closed with it), `PostDealer` / `PullDealer`, the block draw table, the contested split and grievance, the day pass in `Underworld.DayTick`, `TradeIncome`, `WeeklyTake`, the save rows; the paper city's archetype spread.
* **TRADE-003** — Heat and the law: `TradeHeat` saved on a half-life, the corner deposit through the one multiplier, THE CORNER IS HOT slip with CEASE as the default, hysteresis, `DealerCollared` and its odds, `Deed.Dealing` in every switch.
* **TRADE-004** — The outlet: `HouseView.OutletLook`, `OutletForNextKilo` removed, the sell-only-surplus rule, the token scan; DIPL-009 and REC-008 unblocked.
* **TRADE-005** — The minds: `PostDealer` / `PullDealer` intents, the block choice, the contested rule, cease/resume, the yardstick row (grams sold, money and heat per house by day 30). **Blocked by WASH-003 (GAN-466).**
* **TRADE-006** — The street: the dealer unit, the wander, the customers from CityLife, the beat's collar at the hand-off, the rival's engagement, the markers.
* **TRADE-007** — The surfaces: the block file, the Stash card, THE TRADE row, THE WIRE lines, the man's file.
* **TRADE-008** — Measure, contracts, probe, docs: the draws, `HeatPerGram`, the gate and `DealerCap` tuned on the paper city (thirty seeds, fourteen days, three and six houses) and checked on `DebugSeedLarge`; `gangsters_trade_probe`; `Docs/trade.md`; §6/§7 from the tuned numbers; WASH-008's yardstick re-run with a trading house; the CLAUDE.md row; the memory note; the Waterfront's seams listed.

Order: TRADE-000 → 001 → 002 → 003 / 004 (either order) → 006 / 007 (either order) → 005 (when WASH-003 has landed) → 008. DIPL-009 after TRADE-004; REC-008 after TRADE-001.

## 11. Contracts — `gangsters_trade_tests`

`ALoadLandsPure`, `AStepRaisesGramsNotPureGrams`, `TheBandFollowsTheRatio`, `TheBuyerRefusesSteppedProduct`, `AnOldFileKeepsItsKilos`, `ADealerIsAMarkNotAnId`, `PullingIntoACrewClearsTheRow`, `AKeeperPulledIntoACrewDarkensTheFlat`, `TheBlockDrawIsSharedByEveryDealerOnIt`, `AContestedCornerFilesTheGrievance`, `TheDayPassRunsAfterEveryLoadInSortedOrder`, `TradeMoneyIsDirtyAndOfWhich`, `TradeHeatIsAHalfLifeInGameDays`, `TradeHeatSurvivesAFile`, `TwentyCafesAreAsHotAsOneClub`, `AnUnansweredSlipIsCease`, `ACeasedCornerDepositsNothing`, `TheCornerDepositsThroughTheOneFactor`, `HysteresisResumesUnderHalf`, `UnderFourHundredGramsIsDealing`, `EveryDeedSwitchNamesDealing`, `TheCollarLeavesTheRoomAlone`, `TwoHousesPrintTwoOutlets`, `TheBuyerIsTheFloorForPureOnly`, `AMindNeverPostsOntoARivalsCornerAtPeace`, `AMindCeasesAtTheGateAndResumesUnderHalf`, `ThePaperCityDealsTheMeasuredSpread`, `TheTokenScanStaysGreen`.
