# The collector and his detail — a node of his own in the chain of command

Design brief, written 2026-09-03 from the conversation that settled it. Linear: EPIC 30, GAN-273 (tickets `BAG-001..006` = GAN-274..279).

GAN-262 gave every crew one bag man, dealt into a street unit of his own but still one of the crew's four. This epic finishes the thought the way the user put it: **the collector leaves the crew** — "kolektor treba da izađe iz crew, da bude poseban kao mini lieutenant unutar lieutenanta/bossa za čiji blok radi, a pošto je mini lieutenant dodaš mu obezbeđenje i to je to." He becomes his own node under the lieutenant (or the Don) whose ground he collects, he gets an escort posted to him the way men are posted to the Don's detail, he cannot be ordered about while he carries the bag, he sits inside the HQ between rounds and comes out only when the clock says, and if he and his escort die on the round the bag lies where he fell and whoever killed him may take it.

Two smaller defects ride along because the same conversation found them: a robbery prints as "Protection" on the Finances page, and the ledger says a collector "stands with the Don".

## 1. Decisions

| # | Question | Ruling |
|---|---|---|
| 1 | Does the collector spend one of the crew's four street places? | **No.** He and his escort are their own body on the street; the line gets its fourth man back. More men on the street per lieutenant is intended, not an accident. |
| 2 | Where does an escort come from? | The reserve **or** the crew's own men (bench or line). Posting a man from the reserve re-aims his loyalty like any other posting; posting one of the crew's own does not. |
| 3 | Can the player order the bag unit? | **No.** Not while the man carries the bag. It has its own AI: inside the HQ, out on the round when the schedule says, back inside after banking, out again to defend the HQ block. Take the bag off him and he is an ordinary hood again. |
| 4 | Finances | A new row **Jobs** (robbery, ransom cut) beside Protection; `IllegalIncome` stays as the sum so laundering and the save file keep their meaning. |
| 5 | The Don's own block | His collector's node hangs under THE DETAIL. The collector is **not** a bodyguard: no attempt on the Don ever spends him. |
| 6 | The collector dies on the round | A living escort picks the bag up and finishes the round. If nobody of ours is left standing, the bag lies on the ground with the day's take in it; **the man who killed him may take it** — a right-click on the bag, TAKE THE BAG, is a new order. |
| 7 | Men inside the HQ between rounds | They count as presence on the HQ block, **and they come out to defend it** when a fight reaches the block. Taking that block gets harder; that is the point. |

## 2. What exists and is reused

| Thing | Where | What changes |
|---|---|---|
| The bag man | `Personnel/Character.cs` `Duty.Collector`; `RosterOps.NameCollector / TakeOffTheBag / LetLieutenantPick / TendCrewBag / TendCollectors`; `CollectorChoice` | the same ops, but they move the man out of `HoodIds` into the crew's bag node and back |
| The Don's detail | `Personnel/Bodyguards.cs` — `FormDetail` makes a `Crew` led by the Boss; `FallIn` pulls the reserve into it; `Standing` / `Attempt` / `DayOnDuty` spend it | the pattern the bag node copies; `Standing` and `DayOnDuty` must never see a collector or an escort |
| The street unit | `RoadDemo/DemoCrews.cs` `Unit.IsDetachment / Parent`, `Sync` (the deal), `BagUnitOf` | the deal reads the node instead of scanning the line; the unit holds collector + escort |
| The round | `RoadDemo/TerritoryRuntime.Collection.cs` — `CollectionRound.Walkers`, `SendRound`, `EnsureCollector`, `WatchRounds`, `Bank`, `HomeDoor` | walks the same unit; carrier fallback = collector, then an escort, never the line |
| Men inside a door | `RoadDemo/CrewQuarters.cs` — `Station / BringOut / Inside / Retasked`, billets keyed by **crew id** | re-keyed by unit identity so a bag unit and its line can be inside or out independently |
| The bag prop | `RoadDemo/BagCarry.cs` — `Give / Drop`, a dropped bag is destroyed after `DroppedFor` | a dropped bag becomes a pickup that remembers the take |
| The right-click card | `RoadDemo/CrewOverlay.cs` — `CrewEnemyAction` rows (KILL, MOTO DRIVE-BY, BOMBA) | gains TAKE THE BAG when the pointer is on a bag on the ground |
| Chain of command | `UI/PersonnelAlmanac.Command.cs` — `CommandBranch`, THE DETAIL first, one branch per lieutenant, RESERVE with PLACE / PICKED tails; `FileDetailPosting` in the ORGANIZATION partial | a sub-branch per collector, hung under its leader's card; PLACE targets it |
| What a man is doing | `UI/PersonnelAlmanac.Organization.cs` `HoodDuty` | a collector / escort case before the detail case |
| Personal file | `UI/PersonnelAlmanac.Personnel.cs` — MAKE HIM A COLLECTOR / TAKE HIM OFF THE BAG | a second key for the escort |
| Money | `Outfit/Accounts.cs` `DaySheet.IllegalIncome`; `CampaignRunner.BookMoney` (jobs), `OutfitDirector.BankCollection` (rounds), the midnight `RiskyMoney += IllegalIncome`; `OutfitSnapshot` | one new field, `JobIncome`; the sum keeps its name |
| Capacity | `Personnel/Organization.cs` `CapacityOf(leader).Manpower` | counts the collector and his escort under their leader, as it already counts every man on a crew |
| Saves | `Personnel/RosterSnapshot.cs` (`duty`, `crews`), `Outfit/OutfitSnapshot.cs` (`illegalIncome`), `gangsters_save_tests` | every new field round-trips |

## 3. The node

The collector is **a member of the crew who is not in its line**. `Crew` gains:

```
public int BagId = -1;                  // the collector, or -1
public readonly List<int> EscortIds;    // his escort, at most MaxEscorts
public const int MaxEscorts = 2;
```

* `HoodIds` stays the line and the bench: the men the lieutenant walks and the men he keeps on the books. The collector and his escort are **not in it**. `Roster.CrewOf(id)` answers the crew for them too, so wages, gear, loyalty and "who does he answer to" keep working unchanged.
* `Character.Duty` (`Collector`, and a new `Escort`) is kept in lockstep with the node by the same `RosterOps` op — never written on its own. `OrganizationValidator` asserts the two agree: a `Duty.Collector` is exactly the `BagId` of exactly one crew, a `Duty.Escort` is in exactly one `EscortIds`.
* The Don's detail is a `Crew` already; its `BagId` is the Don's own collector when he answers for ground himself.
* Naming a collector who is in the line takes him **out of the line** — the next man on the bench walks in his place, and the line is a man short until somebody is recruited. Taking the bag off him puts him back on the bench (`HoodIds`), not the reserve.
* Posting an escort: from the reserve (`AssignToCrew` first, then into `EscortIds`), or from the crew's own `HoodIds`. Pulling him off the escort puts him on the bench. Cap `MaxEscorts`; refuse with the number.
* A collector who dies, deserts, is cut loose or moves crews leaves an empty `BagId`; his escort go back to the bench (they escort a bag, not a man). `TendCrewBag` fills the bag again the way it does today, under the same rulings (`BagNamedByBoss`, `BagNamedId`).
* Capacity: the lieutenant's `Manpower` counts line + bench + collector + escort. The line's four places are the line's alone.
* Rank does not change. The collector stays a hood on hood's wages; "mini lieutenant" is his place in the tree, not a promotion.

## 4. The tree

The CHAIN OF COMMAND page draws one more level:

```
THE DON
 ├─ THE DETAIL ........................ his own men
 │    └─ THE BAG · Artie Levine ....... his collector, when he answers for ground
 │         ├─ Artie Levine            carries the bag
 │         └─ Lou Kaminski           guards the bag
 ├─ LIEUTENANT Byrne
 │    ├─ (his men) ..................... the line and the bench
 │    └─ THE BAG · Sal Provenzano
 │         ├─ Sal Provenzano          carries the bag
 │         ├─ Frank Stein             guards the bag
 │         └─ (empty)                 PLACE
 └─ RESERVE · STAYS WITH BOSS
```

* The sub-branch is a `CommandBranch` with the collector as its head, hung off the parent card's rail on a second stub, in the same measurements as a branch (portrait, name, the men on a dashed rail). It is drawn whenever the crew has a collector, empty escort slots showing as PLACE rows.
* The RESERVE's PLACE / PICKED flow targets it the way it targets THE DETAIL: pick a man in the reserve, PLACE on the bag branch, filed through `FileOrder`, refused when the escort is full or the leader's manpower is. A line leaf gets a second tail word, TO THE BAG, that moves one of the crew's own men across. PULL on an escort leaf puts him on the bench.
* Wage line under the bag card: "<n> men · $x / day · on the round Tue Thu" from the schedule the seam already knows.
* `HoodDuty`: "carries the bag for Byrne's ground"; "on the round · Kearny St" while `TryGetRoundOf` answers; "guards the bag". The detail case comes after these, so a collector under THE DETAIL never reads "stands with the Don".
* Personal file: MAKE HIM A COLLECTOR stays; a hood in a crew that has a collector gets PUT HIM ON THE BAG'S DETAIL / TAKE HIM OFF THE BAG'S DETAIL.

## 5. The street

* `DemoCrews.Sync` deals the line from `HoodIds` (the first four active) and the bag unit from `BagId + EscortIds`: one `Unit`, `IsDetachment`, `Parent` = the line, `Boss` = null, `Hoods` = collector first then the escort. `BagUnitOf(crewId)` unchanged.
* **Not orderable, not pickable.** Every list / pick surface already skips detachments; `CrewOverlay.PickAt` joins them. Hovering the unit on the street or the turf map shows "THE BAG · Byrne · on the round / inside" so the player can see him; no card opens.
* **Inside between rounds.** `CrewQuarters` is re-keyed by unit identity (crew id + detachment flag, or the `Unit` itself) so the bag unit's billet and the line's billet are separate rows; `Retasked` on one never removes the other's. The latent GAN-262 bug — a crew TAKE THEM INSIDE at the HQ loses its billet the moment its bag man marches — is fixed by the same change. After the deal, and after `Bank`, the bag unit is `Station`ed inside the HQ. `TendScheduledRounds` calls `BringOut` and sends the round when the clock says; the unit walks its doors, marches to `HomeDoor`, banks, and goes back in.
* **Defending the block.** While inside, the bag unit counts as presence on the HQ block (the `DoorBeat.Active` exception stays as it is). When our men are engaged on the HQ block, or a rival crew stands on it, the bag unit comes out and fights with the rest; when the block is clear it goes back in. This is its own AI, in `TerritoryRuntime` or a small `BagQuarters` beside `CrewQuarters`, never a player order.
* Carrier on the round: `EnsureCollector` prefers the collector, then an escort in the walking unit, never the line. `BagCarry.Give` already lets a survivor take the bag.
* No safe house exists in this game; the HQ is the only house. A second house is its own epic and this brief does not assume it.
* Rival families keep marking a collector on the line (HouseMind); rival bag units on the street are EPIC 25's (GAN-244). Everything on the ground below works for any bag, whoever dropped it.

## 6. The bag on the ground

* When the carrier dies, `BagCarry` drops the bag where he fell and the drop **remembers the round's take**. It stays on the ground; it is not destroyed after a timer.
* A living escort in the same unit picks it up and the round goes on (`WatchRounds` hands the round to him; today it hands it on only under a living carrier — a dead carrier with a living escort now hands it on too).
* If nobody of ours is standing, the round is filed `RoundLost` as today and the bag lies there.
* **TAKE THE BAG.** Right-click on a bag on the ground with a crew selected opens the card with one row, "TAKE THE BAG · $x · the take of the day". The crew walks to it, one man picks it up (`BagCarry.Give`), and the money enters the safe as **`JobIncome`** — it is not our protection, it is what we took off somebody. The pickup is destroyed. Any bag, ours or a rival's, once rival bags exist.
* A rival crew that wiped our bag unit takes the bag on the spot: the pickup is destroyed and the wire prints "<family> took the bag off <name> · $x". If the rival House keeps books, the money is theirs; if it does not yet, it is simply gone.
* A bag our own line walks up to (the escort dead, the line sent there by hand) is taken with the same row.

## 7. The money

* `DaySheet` gains `JobIncome`. `BookMoney` writes a non-Business payout into it; `BankCollection` keeps writing `IllegalIncome`, which from now on means the racket rounds only. A derived `DirtyIncome => IllegalIncome + JobIncome` is what the midnight line, and anything else that meant "the dirty money of the day", reads. The Finances page prints Protection (`IllegalIncome`), Jobs (`JobIncome`), Sales, Legitimate; `TotalIncome` includes all four; the card is one row taller.
* EPIC 29 (HQ-001) replaces the midnight line with dirty-on-entry; when it lands, both a banked round and a job payout enter dirty. Nothing here fights that.
* `OutfitSnapshot` carries `jobIncome`; an old save with no field loads as 0 and the sheet's sum is what it was.

## 8. Rules that must hold

* A `Duty.Collector` is one crew's `BagId` and in nobody's `HoodIds`; a `Duty.Escort` is in one crew's `EscortIds` and in nobody's `HoodIds`. The validator says so.
* The line never has more than four bodies on the street; the bag unit never more than `1 + MaxEscorts`.
* No attempt on the Don, and no bodyguard XP, ever touches a collector or an escort.
* The bag unit takes no player order while the crew has a collector. Its billet and the line's are independent.
* The bag falls with the man; it is picked up only by a man standing over it.
* `IllegalIncome + JobIncome` is exactly what the day's dirty money was before this epic.

## 9. Out of scope

A safe house or second premises; rival bag units on the street (EPIC 25); the bank and laundering (EPIC 29 and after); a collector per block rather than per crew; escort wages other than the house rate; police interest in a bag on the ground.

## 10. Acceptance

* Headless green: `gangsters_organization_tests`, `gangsters_command_tests`, `gangsters_rack_tests`, `gangsters_economy_tests`, `gangsters_wage_tests`, `gangsters_save_tests`, `gangsters_ledger_tests` — with the new contracts named in the tickets.
* Ledger: a crew with a collector shows THE BAG under its card; PLACE from the reserve fills the escort; the Don's collector sits under THE DETAIL and reads "carries the bag", never "stands with the Don"; Finances shows a robbery under Jobs and a round under Protection.
* Play: the bag unit is inside the HQ at 08:00, out at the scheduled hour with the escort behind the collector, back inside after banking; a click on it opens nothing; TAKE THEM INSIDE on the line and a round leaving the same door leave both billets correct.
* Play: kill the collector on the round with the escort alive — the escort finishes it; kill both — the bag lies there, a right-click on it offers TAKE THE BAG, and the take lands under Jobs.
