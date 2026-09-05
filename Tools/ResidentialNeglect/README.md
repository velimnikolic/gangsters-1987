# Neglected residential set

Open `Assets/Scenes/NeglectedResidentialDemo.unity` and enter Play. The shared
`RoadDemo.DemoCamera` supplies WASD/arrows, Q/E, right drag and wheel zoom, with a
visible command hint.

Rebuild with `Tools > City > Residential > Build Neglected Set (exclude police and nightclub)`
or `unity command gangsters_residential_neglected --timeout 60 --json`.
Close the derived scene and stop Play before rebuilding. The builder copies the
saved ResidentialDemo hierarchy and dresses that copy; it never regenerates or
saves the source. Unsaved source edits are not included.

The current source has 13 blocks. Both police blocks and the nightclub are excluded,
leaving the same 10 remaining blocks at the original positions, including the gym,
skatepark, car yard, pump and fire station. Original building geometry and business
components remain in place. This adds a visual set, not district assignment or drug
trade logic, and does not change the demographic configuration of residents.

`ResidentialNeglect.Apply` owns the opt-in dressing; the editor builder supplies
persistent material variants and prefab creation. Facades, paving and furniture
receive weathering in both URP Forward and Deferred. Board placement samples authored
window triangles/atlas UVs and checks exterior clearance; no boards are placed at
shop-door height. Litter clusters accompany existing bins. New dressing has no active
colliders. The dressing pass is a bake-time operation (mesh and texture readback),
not a pass to run during streaming. Use the resulting assets or bake during content
production when integrating future district recipes.

Saved-scene integrity check:

```sh
unity command run_script --file Tools/ResidentialNeglect/Audit.cs --entry ResidentialNeglectAudit.Main --timeout 60 --json
```

Verified 2026-09-05: Unity recompile completed without errors; scene audit passed
10/10 blocks, source positions and geometry retained, no missing meshes/materials,
no new active colliders, 48 upper-window repairs, 34 wall tags and 141 litter objects.
The actual generated scene ran in Play and synthetic Input System events verified
pan, rotation and zoom. A composited Play capture showed the visible control hint.
Visual inspection covered the first residential block at overview and closer range;
this is not a historical-accuracy audit or a full gameplay acceptance of every venue.
