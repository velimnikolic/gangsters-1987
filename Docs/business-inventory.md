# The city's business inventory

*BIZ-002 (GAN-156), for EPIC 2.5 — Living City Businesses & Owners. Written 2026-08-31.*

Every place in this project that could describe a business, what it is, how it groups, and
which provider ticket owns it. A source with no owner here would be a shop nobody deals; a
source with two owners would be a shop dealt twice. The site catalogue refuses a duplicate
site ID out loud (`BusinessSiteCatalog.Build`), so a mistake in this table becomes a failed
`gangsters_business_audit` rather than a silently doubled city.

**Classifications** — `business` (a place with a gazda), `civic` (the city's own, never
racketable), `amenity` (furniture for the neighbourhood, no owner), `unresolved` (a real
business-capable source whose data cannot yet say where one business ends and the next
begins — published as an *ineligible* site with its reason so the audit counts it).

**Grouping rules** — `1→1` one structure, one business; `1→N` one building, several
businesses; `N→1` several structures, one firm.

---

## 1. Residential plan data — `ResidentialBlockRecipe.Plan`

The recipes live in `CoreDistrict.ResidentialBlocks` and outlive every recycled view, which
is why they, and never a composed hierarchy, are the authority.

| Source (data path) | Class | Grouping | Owner |
|---|---|---|---|
| `Plan.Spots[i].Shop` + `ResidentialUnit.Shops[side]` — ground-floor shopfronts | business | 1→N: one site per street facade the turned unit really opens onto; a `ResidentialKind.Corner` unit's glass wraps ONE corner and stays one site | **BIZ-004** |
| `Plan.Cafes` — the kit storefront gaps the lot deals (`Use.Cafe`) | business | 1→1 per gap, sign `cafe` | **BIZ-004** |
| `Plan.Spots[i].Unit.Kind == Storefront` (`pizzapub`, `pizzapub2`, `radnja1..3`) | business | 1→1, sign `pizza` for the pizzapubs | **BIZ-004** |
| `Plan.Spots[i].Unit.Kind == Amenity`, unit `gym` | business | 1→1, sign `gym` | **BIZ-005** |
| … unit `dinner`, `dinner2` (and `Plan.FeaturedDiner`, which arrives as one of these spots) | business | 1→1, sign `diner` | **BIZ-005** |
| … unit `caryard` | business | N→1 compound (office, stock rows, gate), sign `caryard` | **BIZ-006** |
| … unit `skatepark`, `kosarkaskiteren` | amenity | — | none |
| `ResidentialKind.Park` units, `Use.Park` ground | amenity | — | none |
| `Plan.Subway`, `Use.Subway` | civic | — | none |
| `Plan.Accesses`, `Use.Parking` bays, yards, alleys | amenity | — | none |

**Known limitation (unresolved detail, not an unowned source).** Which *kit* stands in a
`Plan.Cafes` gap — a coffee shop, a diner, a burger joint, a pizzapub or a radnja — is chosen
by `ResidentialBlocks` at COMPOSE time off the gap's length and its own rng, not by the plan.
The plan can only say "a storefront fronts the street here", so the site carries the `cafe`
sign. Moving that choice into `ResidentialLot` would let the sign be exact; it is a
generator change, not a business change, and is deliberately out of EPIC 2.5.

**Known limitation.** `ResidentialUnit.Shops[side]` counts shopfront PANES, not authored
shop groups: the harvest measures each storefront piece separately and no grouping data
survives the bake. A facade with four panes is therefore one business, not four. A future
harvest that emits runs of contiguous panes would let BIZ-004 split them without changing
any other layer.

---

## 2. Core layout — `CoreTerritoryPlan.Blocks` (`CoreBlockDefinition.SourceName`)

| Source | Class | Grouping | Owner |
|---|---|---|---|
| `nightclub-block` | business | 1→1, sign `nightclub` | **BIZ-005** |
| `warehouse-block` | business | N→1 compound, sign `warehouse` | **BIZ-006** |
| `police-station-block` | civic | published ineligible, reason recorded | BIZ-005 (reports it) |
| `block-01` … `block-16` (harvested POLYGON City downtown blocks) | **unresolved** | published ineligible: the harvest baked each block as one prefab and no plan-level data says where one shop ends and the next begins | BIZ-005 (reports it) |
| `res-*` | — | the block's recipe is the source; see §1 | BIZ-004/005/006 |
| `yard-<unit>` | — | one whole-lot amenity; the recipe carries the single unit spot; see §1 | BIZ-005/006 |
| `park-*` | amenity | — | none |
| `quay-*` | — | the promenade's ROOMS are the businesses; see §4 | BIZ-005 |
| `apron-*`, `bank` | amenity | river apron and river bank — geography, not premises | none |

Closing `block-01…16` is the largest single gap in the city's business population: those are
the dense commercial blocks downtown. It needs a block-interior harvest that emits storefront
groups the way `ResidentialHarvest` emits unit shopfronts. Until then the audit counts them.

---

## 3. Core amenities — `CoreDistrict`

| Source | Class | Grouping | Owner |
|---|---|---|---|
| `CoreDistrict.FuelSites` (`CoreAmenityLayout.Site`, `FuelStationBlock`) | business | N→1 compound; the parcel IS the compound (a station never shares a block); approach from `Site.Entry` | **BIZ-006** |
| `CoreDistrict.ParkingSites` | amenity | public car park, no gazda in Phase 1 | none |
| `CoreDistrict.DevelopmentSites` | — | become residential blocks; covered by §1 | BIZ-004 |

---

## 4. The promenade — `QuayWalk.ForQuay` rooms

Re-dealt from the plan with `CoreDistrict.StandQuays`' own dice (seed, then the stretch's
corner), the way `TurfMapSurvey` re-deals them. No composed object is read.

| Programme | Class | Grouping | Owner |
|---|---|---|---|
| `Terrace` | business | 1→1, sign `cafe` | **BIZ-005** |
| `Diner` | business | 1→1, sign `diner` | **BIZ-005** |
| `Fair` | business | N→1 (the whole fairground, wheel included), sign `fairground` | **BIZ-005** |
| `Landing`, `Fountain`, `Lawn`, `Plaza`, `Grove` | amenity | — | none |

---

## 5. Districts — `RoadDemoBuilder.BuiltDistricts`

| Source | Class | Grouping | Owner |
|---|---|---|---|
| `HarborDemo.HarborDistrict` | business | N→1: sheds, gantries, tank farm and gate are ONE firm, entered at the first portal. PropertyDirector's judgement kept — clicking any shed means "the port" | **BIZ-006** |
| `SuburbDemo.SuburbDistrict` | **unresolved** | its plan publishes houses, not premises; no business-capable data audited | not published |
| `AirportDemo.AirportDistrict` | **unresolved** | the terminal's concessions are geometry, not plan data | not published |
| `PadDistrict` | amenity | made ground | none |

---

## 6. Sources that exist but are not in the Core city plan

These are real business sources in this project. They are recorded so that nobody looks for
them in the catalogue and concludes the sweep missed something.

| Source | Where it is used | Why it is unsupported in Phase 1 |
|---|---|---|
| `IndustrialLayout` / `IndustrialQuarter` parcels (factory, works, refinery, warehouse casts) | `IndustrialDemo`, `HarborDemo` only — `RoadDemoBuilder.MakeDistrict` never rolls one | no industrial quarter stands in the Core city; the archetypes (`Factory`, `Works`, `Refinery`) exist and a provider row is a small change once one does |
| `LivingCity.Generation` city — `building-cafe`, `building-restaurant`, `building-post`, `building-burger-joint`, `building-casino`, `industry-*`, `PortMarker` sheds | the older generated-city scenes (`CityBuilder`) | that generator stamps businesses at PLAY over a SAVED hierarchy, so it has no plan-level site data at all. `PropertyDirector` stays its authority and `CityBusinesses` falls back to it there |
| `UniqueBuildings` — the one casino, the gun shop, the bank | `LivingCity.Generation` city | same reason; the `Casino` and `Hotel` archetypes are authored and waiting for a Core plan source |
| Civic buildings — city hall, school, post office, police, fire station | both cities | civic, never businesses |

---

## How to check it

```
unity command gangsters_business_tests --json                 # the contracts
unity command gangsters_business_audit --seed 1987 --json     # a quarter dealt and judged
unity command gangsters_business_audit --json                 # the live city, in Play
unity command gangsters_business_audit --seed 2 --rows --json  # every site, one row each
```

Seed 1987 deals 631 sites, 614 of them eligible and populated, 17 reported unsupported (the
sixteen harvested downtown blocks and the police station). Seed 2 additionally exercises the
car yard, two filling stations and the fairground.
