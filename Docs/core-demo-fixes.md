# CoreDemo corrections, 2026-09-06

This task continues the existing regional implementation without replacing its
uncommitted work. No Unity Editor access, asset regeneration or Git writes occurred.

Implemented:

- River quays continue on both banks from the existing urban promenades to the
  coastal mouths. IslandWaters owns the bank line; IslandLandform protects the
  ground under the extensions. The channel remains straight through developed
  areas and the motorway bridges; meanders begin outside them.
- Ocean/river colours use a muted blue-grey/green palette and gentler shoreline
  foam. Regional terrain, suburban lawn tiles and the island ground beneath gaps
  share the same world-space meadow shader; hosted suburbs add no second ground mesh.
- Two-arm collector bends become continuous curves with paired lanes and adjoining
  pavement ribbons. Their endpoints are lane seams. Junction corner paving fills
  the missing corners. Roads, terrain reservations and turf rendering use the curves.
- Motorway river spans are straight and level, with explicit concrete bank supports
  and the existing steel arches. Channel clearance and approach grades remain checked.
- Industrial estates use one tier of smaller, separated blocks. Their combined
  footprint is at least twice the former compact estate for the corresponding seed;
  each estate is smaller. The checked seeds produce 4-7 estates along the port-side
  collector. Two lorries per estate are assigned distinct port/estate loading calls.
  A bad estate layout is retried, bounded at 24 attempts; failure preserves the port,
  airport and suburbs and reports any industrial area shortfall.
- Freight completion requires a stopped vehicle at the destination road's kerb,
  within its parking search range. Interrupted/stalled trips retry; wrecks do not
  complete calls. HarborTruck publishes its moving body into the shared traffic
  registries, checks spawn admission and sweeps motion against RoadSpace. Trip end
  and district disposal release both body and lane occupancy. A harbour lorry that
  makes no physical progress for 90 s releases its blocked round and retries once;
  a second consecutive blockage suspends the route and logs an error. A successful
  round clears that failure count; Begin can rearm a cleared route. Planned
  loading/door waits are exempt. Proximity checks share RoadSpace's bin index.
  Three failed freight attempts select a reachable alternate stop and log a warning.
  A single forward traversal and one return-route search serve all candidates;
  successful arrival rearms reporting for a later failure episode.
  Port requests have unique reservations across estates, released on cancellation;
  RoadCar remains the owner of the actual occupied parking metre.
- Regional suburban area is exactly doubled through the shared layout's areaScale;
  buildings, lots and road widths retain their sizes. Population scales with area.
- The turf map can zoom out to the entire island, including after window resizing.
  It shows height tint, hillshade, 25 m contours and sampled motorway/ramp curves.
  Thin curves keep a minimum visible width in the island overview. Street
  labels recede at city zoom, independently of the island ceiling. Tactical
  crew glyphs retain their authored size and pick tolerance at every zoom.

Offline verification:

- Runtime/editor compile passed: 876/136 sources, 99/32 existing warnings. Source
  snapshot `516d0d62bba657cb4f0c9bfaf6f99d3e11bb457d8c251c44c79d8bcf6add36d5`.
- RegionSim passed ten seeds (1, 2, 7, 31, 1987, 2026, 91237, -42,
  int.MinValue, int.MaxValue), with 0/2/4-suburb fixtures. Model snapshot
  `e000a3907737436f3f27730c8e40c70a312cb54d192c2d586a95c2e1e4a90d31`.
  Checks include separate smaller estates, doubled total industry, spacing, gateways
  and seed replay. Existing CoreLayout raster faults (0-3) are reported separately;
  deliberately missing-portal/gateway cases log expected errors. Injecting 24 bad
  estate rolls terminates and preserves the harbour, airport and both suburbs.
- IslandSim passed all 30 fixtures on runtime assembly SHA256
  `0228405e8f4a79321ca8603fa3c6dbaa5a68a5f37a2184c544d01445577c861f`.
  All 10-17 accesses route both ways, 16 ramps per fixture, dry roads, no remaining
  two-arm L bends, bridge straightness/support clearance, grades, airport approaches
  and circuits, shipping depth, quay ground, and bounded mountains.
- Additional managed checks exercise the actual suburban lattice at 1x/2x area,
  turf curve coverage at island scale, height ink, five window aspect ratios, and
  return routes/loading kerbs on the actual shared HarborStreet model in four rotations.
  The map check uses production framing (70 m margin, FitToPlate, CityFrame 1.25).
- All 30 fixtures also join actual industrial RasterGraph lanes, HarborStreet and
  the expressway; all estate spawn lanes have a port route and all 8-14 distinct
  loading calls have a return route. The port uses its planned portal level, not
  a prefab-derived warehouse measurement.
- FreightSim passed shared external-body registration, admission, swept clearance,
  road changes and cleanup; distributed stop admission; interrupted/stalled/derelict
  completion rejection, alternate destination claims and watchdog loading exemption;
  four physical port-and-return jobs (8 calls), zero vehicle
  overlaps. Model snapshot `a20506324c5a79c8e935e37a60716bee25644a4eecd907aed0a108fbd8979453`.
  This uses actual scheduler/RoadCar/RoadSpace code and RoadSim's Unity stand-ins;
  the regional Connect entry point and HarborTruck prefab/transform loop are not run.
- Existing kerb-approach (44/44) and kerb-departure (27 cases) checks passed after the
  shared road-body/index changes, with no reported overlaps or position jumps.
  Rotating-clearance and contact-separation checks also passed after that change.
- Runtime graph integration passed: original carriageways retained, duplicate
  registration harmless, routes in both directions, terrain sampling below 400,000.
  Every pair in the integrated graph agrees between forward admission and the
  existing weighted route search; island checks also cover motorway lane changes.
- Deleted-asset audit and whitespace check passed. No tracked asset was deleted;
  existing GUIDs were preserved. New sources have new GUIDs. No size baseline raised;
  existing unrelated size overruns remain.

Unverified: Unity asset import and shader compilation, final rendered appearance,
frame time, manual map/input acceptance, actual populated traffic, truck parking,
loading waits and departures in Play. The offline checks do not stand in for these.
No generated catalog or saved scene was rebuilt. Entering Play must construct the
updated region before a visual judgement; an already-running scene retains its old
composition. Manual visual/Play acceptance remains with the user.

Adversarial review:

Claude reviewed a frozen task delta, SHA256
`38c6cee6e18dab8456a3977161e48304bcf71178f50af61c86292f1bc2174b26`.
Its findings about the second suburban mesh, unbounded estate retries, shared port
slots and false freight arrivals were repaired and the affected checks repeated.
The proposed far-clip failure was rejected after tracing TurfMapHud.Update ->
Show -> Blank: crossing mapAt immediately enables the paper canvas and sets the
world camera culling mask to zero. FitSheet's mapCeiling interpolation scales text
and indicators; it does not blend the 3D world. The framing regression and stale
comments were corrected. A second frozen review judges these repairs and the
additional shared HarborTruck traffic integration.

The second review covered frozen task delta SHA256
`f9f4dc12a7d7d0f698050d4768e25d773b984c7e454525d917a72bc10b8d2eef`.
Confirmed findings (blocked harbour rounds, map label scaling, per-lorry global
traffic scans and silent repeat delivery failures) were repaired as described above.
Two subclaims were rejected: RoadBody.Dispose already guarded List.Remove with
_registered; overlapping arrival tolerance windows do not admit overlapping cars,
because RoadCar chooses and reserves the physical kerb and RoadSpace guards motion.
The tolerance retains RoadCar's legitimate nearby parking search; request locations
are now separately reserved, including reassignment and cleanup. The final bounded
repair delta receives its own frozen follow-up review.

The third review covered repair delta SHA256
`c0f586b5bb89e219c692f227c33ae98294419fd3bf2c1bb86235102970f315ab`.
Its confirmed findings were repaired: crew indicators no longer follow the steep
lettering scale; candidate admission uses one forward traversal and one return
search; reporting resets after successful deliveries; the proximity test uses a
cap above its expected bound and checks both blocked and clear traffic; harbour
rounds retry only once before suspension. The affected compilation, freight,
island/map and graph checks passed on the final snapshot recorded above. These
last repairs were checked locally after Claude's third report; no fourth Claude
approval is claimed. The HarborTruck transform/prefab loop and route suspension
remain unexecuted outside Unity; the shared progress watchdog was exercised.
