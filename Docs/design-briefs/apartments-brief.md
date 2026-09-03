# Apartments — bought, kept, and put to use

Design brief, written 2026-09-02 from the conversation that settled it. Linear: EPIC 27 (label Property, tickets `FLAT-`).

The player already buys shops. This adds the other thing a building holds: **apartments**. An apartment earns nothing on its own. It is a room the outfit owns, and what it becomes depends on the man you put in it. Every use of a flat needs a keeper; without one the flat is dark and does nothing.

## 1. What exists and is reused

| Thing | Where | Used for |
|---|---|---|
| Price | `EconomyPrices.Apartment = 55_000`, doc §3 "Apartment (stan / safehouse)", rent $400/mo | the buy and the running cost |
| Deed pattern | `Business/BusinessDeeds.cs` — simulation state keyed by canonical id, marker is a VIEW | the apartment book copies this shape (`Apartments`), keyed by building + unit |
| Building popup | `Entities/BusinessMarker.cs` (IOverlaySubject, popup rewrites itself) | the shape a per-building popup takes. NOTE: the `TurfMapPanel` "property file" this table used to name **does not exist** — that panel is the date plate, one scrolling column with a lieutenant's dossier over the roster, the key and the right-click menu (`TurfMapPanel.cs:77-99`). Putting unit rows on the map panel is new work, not a hook |
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

* A residential building deals a fixed set of units from the seed: `storeys × unitsPerFloor` (ground floor is a shop or an entrance, so flats start at floor 1). Sorted, deterministic, same on every load.
* **Neither number exists in the repo today** (measured by the contrarian pass, 2026-09-03). The harvest records `ResidentialUnit.MaxH`, a height (`ResidentialUnits.cs:62`), and dividing it by `RoadSpace.Storey = 3.2` gives 4.75–5.8 storeys while dividing by the authored module pitch of 3.0 gives 5.07–6.2 — the storey count changes with the guess, and a deed book keyed by (building, unit) cannot rest on a guess. `Storeys` therefore becomes a MEASURED field in the harvest (`ResidentialHarvest.cs:1182-1200` already writes the struct), and the deal is a table lookup. `ResidentialUnit.Doors` reads `[0,0,0,0]` on 8 of the 14 apartment units, so it cannot be the source for doors per landing either.
* **Flats per landing = the number of ground-floor SHOP BAYS** (user ruling, 2026-09-03). `ResidentialUnit.ShopBays` (`ResidentialUnits.cs:55`) is measured, so the building's own street width sets how many doors a landing has: total = `ShopBays.Length × (storeys − 1)`. Counted off the table that is 22, 20, 18, 12, 9, 9, 7, 7, 7, 6, 5, 5, 1 and 0 bays — so a big corner building deals ~88 flats and a small infill ~20, which is the point. The single unit with no bays, `residential-05`, is also the only one with real harvested residential doors (`Doors = [1, 6, 1, 6]`), so the fallback is exact and there is no hole: bays if there are any, otherwise the harvested door count.
* **There is no `BuildingId` in the codebase** (`grep BuildingId` finds nothing; `PropertyRegistry` holds only `BusinessMarker`s). The book needs one, published from the PLAN the way `ResidentialBusinessSites` publishes `spot:{index}:{unit.Name}` (`ResidentialBusinessSites.cs:365`) — `BusinessSites.cs:25-28` states the contract: the id comes from the plan, "never as the hierarchy currently shows it", so it survives a block being pooled or never composed. Reading the buildings off the composed stage would drift the deed keys under a live campaign.
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

Heat is a deposit into the block's `PoliceAttention` — but **the per-day lump in the table above cannot work as written** (contrarian pass, 2026-09-03). That pool DECAYS: cap 100, half-life 8 hours (`TerritoryRuntime.cs:54-55`), applied on read as `0.5^(elapsed/halfLife)` (`TerritoryFear.cs:825`). A day is three half-lives (×0.125), so a daily deposit of 4 has a steady-state peak of ~4.6 out of 100 and falls to ~0.57 before the next one — against a vocabulary where `ShotHeat = 4`, `GangDeathHeat = 30`, `CivilianDeathHeat = 45` (`PoliceDispatch.cs:45`). A brothel would run for a month with the block's gauge never visibly moving and no raid ever coming. FLAT-004 must pick one and say so: deposit CONTINUOUSLY (an hourly trickle, so an open card room holds a floor of attention the way a standing crew does), or scale the lump against the cap and the half-life, or drive the raid off the flat's own non-decaying accumulated heat and drop the claim that a flat pushes the block's. Card room and brothel also draw **neighbour complaints**: traffic through the stairwell pushes block fear and heat up; the complaint incident from GAN-245 rings the same bell.

Income proposals (FLAT-009, the balancing pass, decides): card room 300–600/day net, brothel 150–250/day per girl, 2–4 girls. Both land in `IllegalIncome`, so they feed RiskyMoney and need washing like everything illegal.

## 4. The keeper

* One man, one flat. He is **off the street** while keeping: not in his lieutenant's walking crew, visible in the flat in the blueprint and in the popup, paid his rank's wage.
* Set through `RosterOps.SetDuty(Keeper, unitId)`; every move that changes who he answers to clears it (existing rule for Collector). Sending him into a crew darkens the flat that moment — the player's decision, not a fault.
* If he dies or is jailed, the flat goes dark: stock stays but nothing works, and a dark cash stash is what a rival or a raid finds easiest.
* His fitness for the role prints as the same half-star scale the ledger already uses; no silent modifiers (EPIC 13 law) — every roll that changed something prints in the paper or his rap sheet.

## 5. The raid

* Chance per day = f(unit heat accumulated, block PoliceAttention, keeper Stealth). The heat the flat put into the block is what brings the raid.
* Raid: stock and cash seized (Armory guns, stash money, card-room bank), keeper collared through the GAN-216 flow with the matching deed, flat closed for N days, `Raid` fine. Nothing here needs new police AI: the officer at the door is the complaint officer.

## 6. Surfaces

1. **The way in is the building itself** (user, 2026-09-03): on the block file, the live block film already turns a click on a building into a pick (`BlockFilmView.At` → `Picked` → `PickTrade`). Clicking a RESIDENTIAL building opens the blueprint for it; shops keep going to `DoorMenu` exactly as now. Three things this costs, measured and confirmed in play on 2026-09-03 — the full working is in `apartments-blueprint-ui-brief.md` §1:
   * **Today only a shop can be clicked, never the building**, because a business's `Door.View` IS the whole building transform (`BusinessRuntime.BindBlockView`) and `At()` takes the first ancestry match. 13 of 14 apartment units carry shop bays, so appending residential doors changes nothing and reversing the order breaks the racket. **User's ruling: the building gets a MAST** — a line from its footprint centre up past its roof, ending in the same square mark the shops use, and that square opens the blueprint. Walls keep opening the shop. Mast heads are tested before the geometry raycast; the head clears the roof at every yaw off the building's measured rise, and the 22 px nearest-mark fallback is restricted to shop marks.
   * Residential buildings join **`blockCardDoors` only**, with keys in a disjoint range. `blockCardTrades` is what the PREMISES count, the racket standings and `DoorMenu` are keyed on, and must keep meaning premises.
   * **WHAT TRADES HERE is grouped by building and the header opens the blueprint** (user's ruling) — the second, filmless way in, which also makes the sheet reviewable in the city-less `Ledger.unity`. A building with flats and no shop still gets a header. This needs a building ADDRESS, which does not exist: `StreetNames` names streets, nothing carries a house number, so the address is minted once per building from its street plus a deterministic number and reused by the blueprint header and the flat form.
   * Sixteen downtown blocks have no plan-level building grouping at all, so there is nothing to click or group on them; FLAT-005 is scoped to plan-backed residential blocks.
2. **Blueprint sheet**: a 1987 blueprint in the ledger skin, opened as a popup over whichever page is showing — not a tab of its own. Floors as rows, flats as cells, the shop on the ground floor drawn as what it is but never sold from here. Click a cell: buy, name it, choose the role, choose the keeper. Type through `LedgerStyle`, widths measured. **A real 3D cutaway is explicitly deferred**; the blueprint is the plan view for now. The full screen spec — panels, copy, the three modes of the flat form, sorting, the reason lines — is `Docs/design-briefs/apartments-blueprint-ui-brief.md`, written from the `blueprint.zip` handoff; §7 there lists every place the prototype was overruled by the code.
3. **Almanac**: a POST column on the man ("Keeper · 12 Kearny St. 2B").

## 7. Out of scope now

Drug lab (waits for the drug chain), loan-sharking from card-room debts, clients as pedestrians walking in (one visitor in the stairwell at most, as a sign of life), girls as entities on the street, tenants and rent income, buying whole buildings.


## 8. What is built (2026-09-03)

The first iteration landed in one pass. It is deliberately not finished — the user's word was
"ne mora da je savršeno u prvoj iteraciji" — but everything below is in the tree and green.

| Piece | Where |
|---|---|
| Buildings off the plan, ids, address, storeys, doors per landing | `Assets/Scripts/Property/ApartmentBuildings.cs` |
| The book: deed, name, role, keeper, bank, staff, seal | `Assets/Scripts/Property/Apartments.cs` |
| The seven roles, their costs, heat, take, and who they want | `Assets/Scripts/Property/UnitRole.cs` |
| The nightly pass: rent, the night's take, the skim, the doctor, the raid | `Assets/Scripts/Property/FlatDay.cs` |
| `Duty.Keeper` and the rules about who may be spared | `Assets/Scripts/Personnel/RosterOps.cs` (`CanKeep`/`SetKeeper`/`ClearKeeper`) |
| The mast on the block film | `Assets/Scripts/UI/BlockFilmView.cs` (`Mast`), built in `PersonnelAlmanac.BlockFile.cs` |
| WHAT TRADES HERE grouped by building, header opens the blueprint | `PersonnelAlmanac.BlockFile.cs` |
| The blueprint sheet and the premises form | `PersonnelAlmanac.Blueprint.cs`, `PersonnelAlmanac.Blueprint.Form.cs` |
| The book's first text input | `LedgerKit.Field` |
| Save and load | `OutfitSnapshot` (`FlatDto`, at the underworld level) |
| The suite | `Assets/Scripts/Tests/FlatTests.cs`, `gangsters_flat_tests [--seed N]` |

**Numbers as built.** Rent 13/day a flat. Fit-out per the §3 table. Card room 450/night before its
swing, one night in four the table loses out of its bank. Brothel 210 a girl a night, wage 70,
four girls to a room. Doctor 95/day, and he takes a day off one man's bed a night. Heat is
deposited as `role heat × 12` because the block's pool decays on an eight-hour half-life (§3);
raid chance a night is `2 + heat×6 − keeper Stealth` in tenths of a percent, and a raid seals the
room for 14 days, takes the bank and puts the keeper in a cell through `RosterOps.Jail`.

**Dealt, on seed 1:** 2,688 apartment buildings, 19,494 flats, a median building of about seven
and the biggest at a hundred.

**Not built yet, and known:**

* The classified column (FLAT-007 proper). Girls and the doctor are hired ON THE FLAT'S OWN FORM
  for now, at a flat day rate — the right shape (ledger rows, nobody on the street) at the wrong
  door.
* The armory and the garage have their rooms, their cost and their heat, but no effect: a
  lieutenant still draws from the central counter and a car still cools nowhere.
* The cash stash does not yet hold `RiskyMoney` off the front — the skim is in, the shelter is
  not.
* Neighbour complaints (the card room and the brothel ringing GAN-245's bell) are not wired.
* `Storeys` is still divided out of the harvested `MaxH` in one place
  (`ApartmentBuildings.StoreysOf`) instead of being measured into the harvest table.
* FLAT-009, the balancing pass, has not been run: every number above is a first guess.
