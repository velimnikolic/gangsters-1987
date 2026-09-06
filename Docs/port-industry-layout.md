# Port industrial area

CoreRegion now packs the regional industrial estates along the landward edge of
the harbor. PortIndustryLayout owns the packing: long parcels run inland, their
back edges share a line 30 metres from the port, and neighboring estates have
30-metre plot setbacks. Both port approaches remain clear. Industrial gateways use
real western raster junctions turned toward the city.

The area excludes wild flora around each plot and across the port-side gap.
Those reservations follow the individual plot outlines, including their different
depths. Terrain levels remain owned by the existing districts. Freight uses the
existing regional road network.

The existing industrial area target and individual estate simulations remain in
use. The port and estates are placed as a group, with space for the expressway's
river-mouth widening. Airport and suburban districts retain their placement and
separation behavior. The layout is generated when CoreDemo starts; no generated
catalog or serialized scene was edited for this change.

## Verification, 2026-09-06

- `Tools/project.py compile`: runtime and editor compilation passed, 99 runtime
  and 32 editor warnings, zero errors. Source snapshot:
  `cbaa0622d100631cc7d15963a5a83a92c6ba83a70bae6bc702aa44ad9eaa761d`.
  Output: `C:/Users/N/AppData/Local/Temp/gangsters-compile-58d634r7`.
- `Tools/RegionSim/run.py`: ten seeds passed replay, retained industrial area,
  nonoverlap, approach clearance, rotated gateway alignment and one connected
  port industrial area. Regional fixtures cover zero, two and four suburbs.
  Model snapshot:
  `78af116fbf3183943d8458ec6f57fc2a13e34413e1d99e917d5876c4ae540659`.
  Harbor bounds planning and its numerical inputs are extracted from runtime
  source. Existing core raster faults (0–3 by seed) also occurred before this
  change; injected missing-portal/gateway errors are expected test cases.
- `Tools/IslandSim/run.py`: all 30 fixtures passed using the compiled runtime:
  freight return routes, distinct loading stops, dry ground roads, rounded
  collectors, bridges, harbor water reservations and airport approach clearance.
  Tests call the production ground-reservation method with actual harbor/estate
  plans: independent gap samples become flora-free; samples beyond shallower
  estates retain their previous flora policy; the harbor apron keeps LandY.
  The harness includes the current harbor sea-route planning step.
  Output: `C:/Users/N/AppData/Local/Temp/gangsters-island-model-o8g1n8qs`.
- Deleted-asset audit and scoped whitespace checks passed. No assets were deleted
  or moved. Global source-size checking still reports existing over-budget files
  and partial classes outside this change; CoreRegion is 312 lines and the new
  planner is 134 lines.

These are offline source/model checks. Satellite views are test doubles, and the
graph checks do not simulate moving traffic. Unity import, physical traffic,
meshes and Play/visual acceptance have not been checked. No Unity Editor access
was used.
