# Territory geography (EPIC 3, GAN-68)

One canonical answer to *what is a block and what is a neighborhood in this city*, shared by
simulation, maps and UI, so every per-block value the later epics add — Presence, Fear,
compliance, control — has a stable home.

## The facade

`LivingCity.Territory.TerritoryGeography` (`Assets/Scripts/Territory/TerritoryGeography.cs`)
is pure data: no GameObject, no engine call, so a whole city can be dealt and judged from the
terminal with the editor idle. It is built once, by `TerritoryRuntime.Init`, out of the
`TerritoryBlockDefinition`s read from `CoreTerritoryPlan`.

What it answers (`ITerritoryGeography`):

| question | member |
|---|---|
| every block, in one stable order | `BlockIds` (ascending canonical id, ordinal) |
| the block record | `TryGetBlock` → id, legacy id, neighborhood, bounds, **centre**, kind |
| the neighborhoods | `NeighborhoodIds`, `TryGetNeighborhood` → name, bounds, member blocks, neighbour hoods |
| what a point is on | `TryGetBlockAt` (smallest containing rect, so nested downtown blocks win) |
| where a body standing here belongs | `TryResolveStanding(point, previous)` |
| block adjacency | `Neighbours`, `AreNeighbours` |
| which businesses stand on a block | `BusinessesOf`, `TryGetBusinessBlock`, `UnplacedBusinesses` |
| ground that is not territory | `OffGridAreas` |
| what could not be accounted for | `Report` (faults + notes) |

`TerritoryBlockDefinition` is THE block model. It carries geometry and identity only — no
`OwnerGangId`, no capture progress. Who holds a block is derived from signals in
`TerritorySimulationState`; it is never stamped on the geography.

## The measures come from the plan, never from a constant

`TerritoryGeographySettings` is handed the city's own street widths — `CoreLayout.AlleyWidth`
(5 m), `StreetWidth` (15 m), `BoulevardWidth` (the main road kerb to kerb, 35 m). Two rules
are derived from them:

* **NeighbourGap** = boulevard + alley. Two blocks are neighbours when they face each other
  along at least an alley's width of shared frontage across no more than that. Corner meeting
  corner across a junction is *not* adjacency; a block nested in another *is*. Symmetric, no
  self-edges, stable across runs of a seed.
* **RoadHysteresis** = half the widest street. A man on a block is on that block; off it he
  keeps the block he last stood on while he is within the hysteresis; beyond that he is
  **blockless**, and is never handed to the nearest block. Crossing an ordinary street
  therefore produces exactly one leave/enter pair; crossing the boulevard produces one leave,
  a stretch of nobody's road, and one enter.

## Business membership

Resolved once, at build, from plan data (`BusinessGeographySites` hands the site catalogue
over; geography holds no reference to the business layer). The order is: the provider's own
block hint, then the footprint's largest overlap, then the doorstep pulled inward by the road
hysteresis — a doorstep lies on the pavement, which belongs to no block, so it is the last
word rather than the first. A site that resolves to nothing is **reported and left unplaced**;
a business hung on the wrong block would tell the player a pavement pays rent.

## Off-grid ground

Districts that are not the primary structure — the port, the airfield, the suburbs — carry no
canonical block in Phase 1. They are published as `TerritoryOffGridArea` with the reason, so
"this place belongs to nobody" is a stated classification rather than a failed lookup. Core's
own quay and apron blocks *are* territory: they are blocks of the plan like any other, and
their kind is on the record.

## Who reads it

* `Gameplay.CityBlocks` is a **shim** over the geography wherever a plan exists (the ledger's
  orders page and the strategic map speak the legacy integer block id). Only the older
  CityBuilder city still falls back to the ground-slab name parse.
* `StrategicMapHud` draws canonical blocks when there is a plan; that is what re-enabled
  ASSIGN BLOCK in CoreDemo, together with `GameplayBootstrap` installing the map for a
  planned city and not only for a `CityBuilder` one (it absorbed the old `RoadDemoLedger`).
* `TerritoryRuntime` resolves actors through it every PhysicalPresence tick.
* `PropertyDirector` and `BlockOverlayHud` keep their own parses: they only ever run in the
  generated CityBuilder city, which has no canonical plan to diverge from.
* `TurfMapSurvey` keeps its visual survey and already takes identity from plan data
  (`CoreQuarterId`, `recipe.BlockId`) rather than from renderer names.

## Verify

    unity command gangsters_geography_tests --json
    unity command gangsters_geography_audit --seed 3 --json
    unity command --timeout 120 gangsters_geography_audit --seed 1987 --twice --json

The audit deals its own quarter (`CoreDistrict.Plan` is pure data), so it needs no scene;
`--twice` re-deals the same seed and proves identity and the graph did not move, which costs a
second plan roll and wants the longer CLI timeout. With a city standing it audits that instead.

**F9** draws the whole thing on the ground it describes: block outlines (magenta where men
stand, red where a block has no neighbour), green neighbour links, yellow spurs to each
business doorstep, red crosses on businesses that resolved to no block, and a legend with the
blockless-men count. Off, it costs one key read a frame.
