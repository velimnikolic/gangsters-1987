# CoreDemo region

CoreDemoBuilder enables `regionalDistricts` for the full city (`quarterBudget == 0`).
The default region contains one cargo harbour, several small industrial estates near
its approaches, one airport, and two suburban districts. `suburbanDistricts` accepts 0–4.
MiniCore keeps its compact extent. All placement and district sub-seeds derive from
the displayed Core city seed; turning off `newSeedEveryPlay` replays the region too.

The port chooses the east or west shore, away from Core's north/south river mouths.
The airport takes the opposite shore. Suburbs take the other edges and avoid the
river. Industrial roads and buildings use IndustrialDistrict's compact/pocket options;
the standalone estate keeps its existing size. Districts reserve their own ground
and water before the shared island is built.

CoreRegion connects actual exposed raster junctions and district portals through
RegionalExpresswayPlan. It uses ExpresswayDemo's shared RoadLine curves, DeckMesh
sweeps, ExpresswayLayout dimensions/grades and LaneNet seam/ramp rules. The full
Core adapter disables the old belt; the focused belt and expressway scenes remain.
There are two curved carriageways and four diamond interchanges (16 ramps).
Collectors connect the actual gates, and floor roads pass beneath the mainline.
Auxiliary exit lanes end at off-ramps so the existing seam matcher admits exits.
River crossings climb to 23 m, clearing the existing sailing boat, with paired
steel arch trusses and piers kept outside the navigable water. Mainline and ramp
heights are shared by the meshes and the lane graph.

All district carriageways join Core's LaneNet, preserving district vehicles,
occupants and driver bindings. Districts tick and dispose through RoadDemoBuilder.
Missing district portals are reconciled before road composition; a failed regional
placement leaves the Core build running. District pavements remain local: regional
roads do not add pedestrian spawn links or walking routes across the motorway.
Suburban names exclude Core's existing quarter names. The map includes the region;
its terrain sample cache is capped at 400,000 samples and detail still follows zoom.

IslandLandform owns one seeded, irregular island envelope, a mountain spine,
rolling foothills, shore slopes and road earthworks. IslandWaters preserves every
shipping reservation, opens the harbour seaward into a bay and widens/meanders the
river beyond its existing sailing reach. Ground roads and district pads stay dry;
only bridges cross the channels. Mountains reach approximately 325-390 m across
the checked configurations. Relief fades before urban pads and roads.

RegionalIslandView draws matching terrain tiles with meadow, woodland, sand and
rock colours. Its depth-writing ocean covers the whole island and shipping lanes,
including below hulls; it owns the regional river surface too, avoiding coplanar
water planes. Water animation changes shading only, preserving boat waterlines.
IslandForest draws seeded stands of broadleaf trees, pines and rocks as culled GPU
instances. It excludes developed pads, road shoulders and water, caps placement
at 22,000 instances, fills near-capacity GPU batches and limits distant shadows.
The 20 m terrain mesh shares its height samples with its normals and logs build time.
Its URP material follows the project's Lit pass layout. Runtime landscape
meshes/materials are released with their views. Regional haze makes the distant
ridge visible. A broad scenic approach valley holds terrain and canopy below the
airport's existing final approach without changing flight behavior.
Outside built-up districts, access-road StreetKits use LampsOnly: no bins, benches,
parking meters or other urban furniture. Existing wayside fuel blocks remain.

IndustrialFreight assigns two existing estate lorries to recurring loading calls on
the port's public back street and at estate kerbs. RoadCar retains route admission,
traffic, parking and completion; the district ticks the call schedule and clears it
on disposal. These ambient trips carry no business inventory or campaign truth and
are recreated with the district, like its other civilian traffic. HarborStreet owns
the public street's paired carriageways and parking strips. The harbour's internal
cargo handling and yard trucks retain their existing owners.

CoreServicePlan assigns full ResidentialDemo fire-station and compact precinct
blocks before housing recipes are created. Fire stations favour uncovered residential
quarters, street access and a 300 m minimum separation; precincts use a wider 550 m
spacing and require both the east parking access and north public frontage to reach
streets after rotation. Counts scale to retained residential quarters, capped at five
fire stations and three additional precincts. The original central station remains.
PoliceForce discovers the actual precinct buildings and their authored surface fleet.
Fire engines bind to the city LaneNet and keep their working doors outside static merging.

Fuel placement prefers wider roads and at least 400 m between stations. It may replace
a generated housing block when a leftover parcel cannot hold the full PumpDemo block.
Regional approach stations use the shared wayside logic and reject district footprints
and nearby existing stations. These are game placement heuristics, not a fire-response
or municipal service-time simulation.

Offline validation: `python3 Tools/RegionSim/run.py` executes the runtime core,
amenity, service, gateway, industrial and region planners. Set
`GANGSTERS_REGION_FIXTURES` to an output JSON path to export region fixtures.
Satellite view classes are dimension-contract doubles; this does not verify their
actual meshes. Injected missing-portal and missing-gateway errors are expected;
existing CoreLayout raster faults for some seeds are recorded separately.

`python3 Tools/IslandSim/run.py <compile-output> <UnityEngine.CoreModule.dll> <fixtures.json>`
loads the freshly compiled runtime assembly into a separate managed harness. It
uses actual HarborDistrict/AirportDistrict Plan/Reserve, RegionalExpresswayPlan,
LaneNet and IslandLandform without native Unity or Editor calls. Suburb/industrial
footprints remain model contracts. Checks cover all-pairs routes between accesses,
dry road centres/edges, loop/ramp continuity, pier clearance, bridge/mast clearance,
grades, both airport approaches, shipping depth and terrain bounds.
`Tools/IslandSim/plot.py <island.csv>` optionally plots exported heights/roads using
Pillow; this is a model plot, not a Unity render.

## Validation record - 2026-09-06, curved expressway and island revision

- Runtime/editor offline compile: PASS (865/136 sources, 99/32 warnings), source
  snapshot `b37c43e2319c801bba9d318714a7c34cd3be9434045f70f4fa9111b75b9a8641`.
- Region model: PASS on seeds 1, 2, 7, 31, 1987, 2026, 91237, -42,
  int.MinValue and int.MaxValue; snapshot
  `05c2b8b4695f3537acf1279e0658f6a350c844cfc1f1c66d62b1e5aeb7592ecf`.
  Five fire stations, three added precincts and five Core fuel stations per seed.
- Actual managed island/expressway harness: PASS on 30 fixtures (the ten seeds,
  each with 0, 2 and 4 suburbs); assembly SHA256
  `e783684f1b17f47b8807615da28a346df8f1de367e43b3f5d73fb5f8862c0fe2`.
  All 7/9/11 accesses route both ways; 16 curved ramps each; dry floor roads,
  continuous loops/gore joins, clear pier sites, unobstructed airport approaches,
  mainline/ramp grades below 7.5%, river clearance above 18 m and deep shipping water.
- Asset audit: PASS, zero deleted tracked GUIDs. Existing GUIDs are unchanged.
  Diff whitespace check: PASS. No generated catalog or scene asset was rebuilt.
- Size check: existing unrelated overruns remain. RoadDemoBuilder is 15,267 lines
  against its existing 15,278 budget; no size baseline was raised.

Unity import, shader compilation, actual district meshes, driving through every
interchange, fire-engine departures/returns, precinct arrivals, frame time and
visual acceptance remain unverified. No Editor/Play run was authorized or performed
for this revision. Offline C# compilation and model plots are not that verdict.
