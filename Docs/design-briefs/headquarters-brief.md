# Headquarters — the safe, the stock, and the armory gate

Design brief, written 2026-09-02 from the conversation that settled it. Linear: EPIC 29, GAN-263 (tickets `HQ-001..005` = GAN-264..268).

Today the headquarters is a building with "HQ" painted outside it and nothing behind the door. The user's words: "hq je trenutno samo sranje koje postoji na mapi. treba da bude mesto gde se drzi novac i oruzje kupljeno i da se to nekako prikazuje na popup. crni novac je tu a opran je u banci al jos nismo stigli do banke." So: the HQ becomes the place where the outfit's cash and its bought weapons physically ARE, the street popup over its door says so, and gear can only change hands where an armory stands. The bank comes later and is out of scope here.

## 1. What exists and is reused

| Thing | Where | Used for |
|---|---|---|
| The front on the map | `RoadDemo/GangFront.cs` — `Role = "HQ"`, `BlockId`, `Outside` doorstep, `Books` (`FrontDossier`) | the building the popup opens over; its block is the first armory site |
| The street popup | `RoadDemo/FrontOverlay.cs` — LEGIT / THE BUSINESS tabs from the fake books; "YOUR OWN HOUSE" for ours | gets a THE HOUSE face for the player's own front |
| Money | `Outfit/Accounts.cs` — `Safe` (all cash), `RiskyMoney` (the dirty share of that same cash) | `Safe` is the total in the HQ; dirty is marked at receipt and spent first |
| Money writes | `BalanceMath.TryPurchase`, `HouseOps.Refund`, `CampaignRunner.BookMoney`, `OutfitDirector.SettleBusinessDay` / `BankCollection`, `BusinessShutdowns` repair refund | all route through one Receive / Pay seam |
| Gear | `Personnel/RosterEquipment.cs` — `OwnerId` = `Unheld` (−1, the stock), `FrontArmory` (−2, the HQ locker), or a lieutenant's id (his crew's deck) | the stock and the locker are both "at the HQ" |
| Gear ops | `RosterOps.GiveEquipment / MoveEquipment / GiveEquipmentToFront / ReturnEquipment`; `PersonnelDirector` wraps them | the armory gate sits in the director, before the roster op |
| Quartermaster deal | `RosterOps.NormalizeArms` — re-deals a group's gear over its own men | never gated: men in one crew are together |
| Who runs the desk | `Roster.FrontId`, `RosterOps.InFrontGuard` (manager + pooled hoods) | THE DESK and ON GUARD lines |
| Men inside | `RoadDemo/CrewQuarters.cs` — `Inside(unit)`, billet keyed by crew id | the INSIDE line |
| Physical seam | `IOrganizationPhysicalSource` (`Personnel/Organization.cs`), set by `DemoCrews` on `PersonnelDirector` | extended with "which block is this crew on" |
| Block of a point | `TerritoryGeography.TryGetBlockAt` | resolves the crew's block |
| Ledger front card | `PersonnelAlmanac.Personnel.cs` `BuildFrontDetail` — IN THE SAFE, AT THE FRONT, ARMORY, GIVE / RETURN | reads the same report as the popup |
| Ledger Armory page | `PersonnelAlmanac.Armory.cs` — catalogue + stock book, GIVE picks THE FRONT or a lieutenant | picker rows show where each crew stands |
| HQ button | `CityClockHud.FocusHeadquarters` via `OutfitDirector.TryGetHeadquarters` | unchanged |
| Apartments plan | `Docs/design-briefs/apartments-brief.md` — Armory and Cash stash roles | an Armory flat is a second armory site; a Cash stash later takes dirty money off the HQ |

## 2. The money

* `Accounts.Safe` remains the whole cash pile, and the pile IS the HQ safe. Every existing seam that reads `Safe` keeps reading it.
* `Accounts.RiskyMoney` becomes the **dirty share** of that pile. Invariant: `0 ≤ RiskyMoney ≤ Safe`. Clean = `Safe − RiskyMoney`. The Finances page keeps its risk rating on the dirty figure.
* Money is dirty the moment it enters, not at midnight: a banked collection round is dirty when it lands; a shop's declared net is clean. The midnight line `RiskyMoney += IllegalIncome` goes away, because the entry already counted it.
* One seam: `BalanceMath.Receive(accounts, amount, MoneyKind)` and `BalanceMath.Pay(accounts, price)`. Every `Safe +=` / `Safe -=` in the codebase goes through these. `TryPurchase` becomes a caller of `Pay`.
* **Spending is dirty-first.** Street cash gets spent on the street: wages, guns, bribes, cars, signings all come out of the dirty pile before they touch the clean one. This is the only spending rule in this epic. The bank epic adds the other half (paper purchases only from clean money, laundering at a cost, tax actually paid); adding that gate now with no laundering would deadlock the campaign the day the starting stake ran out.
* Refunds put money back the way it left (a refund of a dirty-paid purchase is dirty again). Keep this simple: a refund is dirty up to what the purchase drew from dirty; `Pay` returns how much was dirty so `Refund` can mirror it.
* A future raid on the HQ seizes the dirty pile. Not built here; the seam is `BalanceMath.Seize(accounts)` returning what was taken, named so GAN-245 lands on it.

## 3. The readout

One pure struct, `Outfit/Headquarters.cs`, `HeadquartersReport.For(accounts, roster, inside)`:

| Field | Source |
|---|---|
| `Safe`, `Dirty`, `Clean`, `Risk` | `Accounts` + `BalanceMath.RiskFor` |
| `DeskManager` (name or empty), `Guards` (pooled hoods) | `Roster.FrontId`, `InFrontGuard` |
| `Stock` — per `EquipmentKind`: in the stock (`Unheld`), in the locker (`FrontArmory` owner, holder = front), in guards' hands (`FrontArmory` owner, holder = a man) | `Roster.Equipment` |
| `Grenades` | `RosterOps.GrenadesOf(FrontArmory)` + unheld |
| `Vehicles` — cars and motorcycles unheld or on the front | `Roster.Equipment` |
| `Inside` — crews billeted in the HQ (lieutenant name, men) | passed in by the scene through the physical seam; empty headless |

Both the street popup and the ledger front card read this struct and nothing else, so the two can never disagree.

## 4. The popup

`FrontOverlay` over the player's own front shows LEGIT | **THE HOUSE** (rival fronts keep LEGIT | THE BUSINESS). THE HOUSE rows, top to bottom, in the same row layout as the other faces:

```
IN THE SAFE        $23,410
DIRTY              $8,200        (red from Risk ≥ Moderate)
CLEAN              $15,210
THE DESK           Sal Provenzano runs it   /  nobody runs the desk (red)
ON GUARD           3 hoods
INSIDE             Byrne's crew, 5 men      /  nobody
ARMORY             3 pistols · 1 shotgun · 2 grenades
IN HANDS           2 pistols
OUT BACK           2 cars · 1 motorcycle
```

Note under the rows: "cash on the premises is what a raid takes · the bank is not built yet". Type through `LedgerStyle` (`DemoUi` defers to it), widths measured, no `GraphicRaycaster` (the overlay's own hit-test rule). No new card class; the existing card gets a third face and the tab labels change per owner.

The ledger's front card (THE BOSS) adds a DIRTY line under IN THE SAFE and reads the report for everything it already prints. The Finances page line "Risky money (unlaundered)" is renamed "Dirty cash in the safe".

## 5. The armory gate

The user's rule: "mozemo da dodelimo oruzje ljudima samo kad su u hq ili u zgradi koja ima armory", measured by the BLOCK ("ako ima armory u bloku moze da se naoruzaju, ne moramo toliki realizam").

* **Armory sites** — `Outfit/ArmorySites.cs`, a list of `TerritoryBlockId`. V1 holds one entry: the HQ's block. EPIC 27's Armory flat adds its building's block while it has a keeper. Pure, testable.
* **The gate** — gear changes GROUP only when the lieutenant's crew stands on a block with one of our armories:
  * stock → crew (`GiveEquipment`), crew → crew (`MoveEquipment`, including a car's keys), crew → stock (`ReturnEquipment`), crew → front locker (`GiveEquipmentToFront` from a crew).
  * a crew billeted inside a door (`CrewQuarters.Inside`) counts as standing on that door's block.
* **Never gated**: the deal inside one crew (`NormalizeArms`), the desk and the pool (`FrontArmory` is at the HQ by definition), the day-one seeding, and any op with no physical source bound (the headless suite binds a fake source when it wants the gate).
* **Seam** — `IOrganizationPhysicalSource.TryLocateGroup(int leaderId, out TerritoryBlockId block)`. `DemoCrews` answers from the unit's position through `TerritoryGeography.TryGetBlockAt`; a crew inside a door answers with the door's block. The gate itself lives in `PersonnelDirector` before the roster op, so every surface that issues gear (Armory page, personnel card, street right-click on a car) is gated by the same line.
* **Refusal** in `LedgerText`: "his crew is on Kearny St · no armory there", the block named. A crew whose block cannot be resolved (off the map, no geography) is refused with "his crew is nowhere the book can find".
* **UI** — the Armory page's GIVE picker and the personnel card's GIVE list print under each lieutenant "AT THE FRONT" or "on Kearny St · 4 blocks out" and grey the row when he cannot draw. One key beside a greyed row, SEND FOR THEM, issues the existing walk order to the HQ's block; the player presses GIVE when they are there. No pending-issue chit in this epic.

## 6. Out of scope

The bank (deposits, laundering, clean-only paper purchases, tax paid), the raid on the HQ (GAN-245 seam only), the Armory and Cash stash flats (EPIC 27 plugs into `ArmorySites` and the dirty pile), buying a second premises as a new HQ, moving the HQ.

## 7. Acceptance

* Headless: `gangsters_hq_tests` green — dirty-first spend, the invariant under every Receive / Pay / Refund path, the report's counts against a dealt roster, the gate's four ops with a fake source on and off the armory block. `WageTests` and the rest of the suite stay green.
* Play: click the HQ door → THE HOUSE tab reads the same figures as the ledger's THE BOSS card and Finances page. Bank a round → DIRTY rises the same frame. Pay wages at midnight → DIRTY falls first.
* Play: GIVE a pistol to a lieutenant whose crew stands three blocks away → refused, block named, SEND FOR THEM walks them home; GIVE again on the HQ block → dealt.
* `recompile_status --json` clean; `code-review-unity` before commit.

## Proposed tickets

* HQ-001 — The money seam: Receive / Pay / Refund, dirty share of the safe, dirty-first spending, midnight line removed
* HQ-002 — `HeadquartersReport` (pure) and the headless suite, `gangsters_hq_tests`
* HQ-003 — THE HOUSE face on the street card; the ledger front card and Finances page read the report
* HQ-004 — The armory gate: `ArmorySites`, `TryLocateGroup`, the gate in the director, refusal text, picker rows, SEND FOR THEM
* HQ-005 — Docs: `economy-prices.md` §9 treasury rules, `Docs/headquarters.md`, the raid seam named
