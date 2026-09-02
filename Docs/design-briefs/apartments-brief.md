# Apartments — bought, kept, and put to use

Design brief, written 2026-09-02 from the conversation that settled it. Linear: EPIC 27 (label Property, tickets `FLAT-`).

The player already buys shops. This adds the other thing a building holds: **apartments**. An apartment earns nothing on its own. It is a room the outfit owns, and what it becomes depends on the man you put in it. Every use of a flat needs a keeper; without one the flat is dark and does nothing.

## 1. What exists and is reused

| Thing | Where | Used for |
|---|---|---|
| Price | `EconomyPrices.Apartment = 55_000`, doc §3 "Apartment (stan / safehouse)", rent $400/mo | the buy and the running cost |
| Deed pattern | `Business/BusinessDeeds.cs` — simulation state keyed by canonical id, marker is a VIEW | the apartment book copies this shape (`Apartments`), keyed by building + unit |
| Building popup | `Entities/BusinessMarker.cs` (IOverlaySubject, popup rewrites itself), `TurfMapPanel` property file | where the flats of a building are listed |
| Duty | `Character.Duty` (`None`, `Collector`), `RosterOps.SetDuty` owns the rules | `Duty.Keeper` + the unit he keeps |
| Personality | Loyalty / Greed / Discipline / Courage / Temper; `GreedLadder` skimming | the keeper rolls |
| Skills | `CharacterAttribute`: Combat, Awareness, Stealth, Driving, Streetwise, Leadership, Organization, StreetAuthority, Persuasion, Intimidation, Connections | who is good at keeping what |
| Hospital / jail | `RosterOps.Hospitalize / Jail` with absolute `BackOnDay` | the infirmary shortens the bed, a raid jails the keeper |
| Money | `Accounts.Safe`, `Accounts.RiskyMoney` (illegal profit not yet washed), `DaySheet.IllegalIncome` | the cash stash holds RiskyMoney off the premises; card room and brothel income is illegal income |
| Hideout | GAN-222 FLEE-003 resolved "the hideout" as every door the outfit owns (`PoliceDispatch.Wanted.cs`) | the Safehouse role becomes the PREFERRED hideout; every owned door stays the fallback |
| Raid | `EconomyPrices.Raid = 500`, the GAN-216 collar with a `Deed`, `PoliceAttention` per block | the raid on a flat |
| Classified column | ADS tape in the paper, `WageAsked` bargain, `Wages`/HouseRate | doctor and girls are hired here |
| Wages | one wage table (HouseRate), EPIC 24 | the keeper is paid like any man of his rank; staff rows get their own rate |
| Day tick | `Campaign.DayTick` in `CampaignRunner` (pure) | every flat effect lands here, never on a frame |
| Buildings | residential fabric is 4- and 5-storey terrace kit (`BlockZone`, `LotKit`) | units dealt per building from storeys |

## 2. The unit

* A residential building deals a fixed set of units from the seed: `storeys × unitsPerFloor` (ground floor is a shop or an entrance, so flats start at floor 1; 2 units per floor on the terrace kit). Sorted, deterministic, same on every load.
* `Unit` = building id + floor + door. Owned by a gang or nobody (−1, the honest majority, as in `BusinessDeeds`).
* Buy = `Apartment` price through the existing pay seam. Rent $400/mo is charged at the day tick (13/day) as `OtherCosts`.
* Territory stays **per building** (user decision). A flat in a rival-held building is allowed and is a risk, not a rule violation: rival crews on the block find it sooner.
* A unit has a **name** the player types (the blueprint asks for it), a **role**, and a **keeper**.

## 3. Roles, first iteration (7)

| Role | Setup | What it does | Keeper wants | Heat/day |
|---|---|---|---|---|
| Armory | 5,000 | holds guns; lieutenants draw from the nearest armory instead of one central counter (issue rule unchanged: only lieutenants deal) | lieutenant, or hood with Loyalty; low Loyalty = guns leak | 1 |
| Cash stash | 3,000 (a safe) | takes RiskyMoney off the front; money in a stash is not seized with the front and not in the declared sheet | Loyalty, Greed — the GreedLadder skim applies to what he sits on; a deserter takes the stash | 1 |
| Safehouse | 2,000 | men with Wanted lie low here; the preferred hideout for FLEE; hidden-day cooling per GAN-222 | Discipline — low Discipline and a guest gets seen on the street (resets his cooling) | 0 |
| Infirmary | 8,000 | wounded men heal here with no police report; bed days shortened by the doctor's skill | doctor from the classified column (staff row), or a hood with Awareness for a slow bed | 1 |
| Garage | 6,000 | cars used in a drive-by cool here (not seen by patrols), car bombs are fitted here | Driving; better keeper = shorter cool | 1 |
| Card room | 15,000 + bank | illegal income, seed-varied (a bad night the bank loses); needs the bank stocked or it is closed | Organization + Loyalty; low Loyalty = house cheated | 3 |
| Brothel | 10,000 | illegal income per girl; girls are staff rows hired from the classified column | Persuasion + StreetAuthority; personality gate: some men refuse (Loyalty −) | 4 |

Heat is a per-day deposit into the block's `PoliceAttention`. Card room and brothel also draw **neighbour complaints**: traffic through the stairwell pushes block fear and heat up; the complaint incident from GAN-245 rings the same bell.

Income proposals (balancing ticket decides): card room 300–600/day net, brothel 150–250/day per girl, 2–4 girls. Both land in `IllegalIncome`, so they feed RiskyMoney and need washing like everything illegal.

## 4. The keeper

* One man, one flat. He is **off the street** while keeping: not in his lieutenant's walking crew, visible in the flat in the blueprint and in the popup, paid his rank's wage.
* Set through `RosterOps.SetDuty(Keeper, unitId)`; every move that changes who he answers to clears it (existing rule for Collector). Sending him into a crew darkens the flat that moment — the player's decision, not a fault.
* If he dies or is jailed, the flat goes dark: stock stays but nothing works, and a dark cash stash is what a rival or a raid finds easiest.
* His fitness for the role prints as the same half-star scale the ledger already uses; no silent modifiers (EPIC 13 law) — every roll that changed something prints in the paper or his rap sheet.

## 5. The raid

* Chance per day = f(unit heat accumulated, block PoliceAttention, keeper Stealth). The heat the flat put into the block is what brings the raid.
* Raid: stock and cash seized (Armory guns, stash money, card-room bank), keeper collared through the GAN-216 flow with the matching deed, flat closed for N days, `Raid` fine. Nothing here needs new police AI: the officer at the door is the complaint officer.

## 6. Surfaces

1. **Building popup** (block detail): the building's units as rows — door, name, role, keeper, state (OPEN / DARK / RAIDED, closed until day N). Buy and assign from here.
2. **Blueprint sheet**: a 1987 blueprint in the ledger skin. Floors as rows, flats as cells, the shop on the ground floor drawn as what it is. Click a cell: buy, name it, choose the role, choose the keeper. Type through `LedgerStyle`, widths measured. **A real 3D cutaway is explicitly deferred**; the blueprint is the plan view for now.
3. **Almanac**: a POST column on the man ("Keeper · 12 Kearny St. 2B").

## 7. Out of scope now

Drug lab (waits for the drug chain), loan-sharking from card-room debts, clients as pedestrians walking in (one visitor in the stairwell at most, as a sign of life), girls as entities on the street, tenants and rent income, buying whole buildings.
