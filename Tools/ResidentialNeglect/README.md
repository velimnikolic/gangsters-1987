# Dynamic residential condition

`Assets/Scenes/ResidentialDemo.unity` has one original set. Both police blocks and
the nightclub are excluded from the condition preview. In Play, use **ZAPUSTENOST
BLOKOVA** to move between maintained and neglected appearances. **HOME** frames the
set. The camera retains WASD, Q/E, right drag and wheel controls.

**Settings > Kolicina propsa** controls small decorative props independently of
neglect, from 0% to 100%. The setting is shared with Core and saved in PlayerPrefs.
It changes litter/flowers and added cosmetic props, not buildings, bins, functional
furniture, entrances, navigation or business operations. A stable seeded subset
means that dragging back restores the same arrangement.

Authoring: **Tools > City > Residential > Prepare Dynamic Condition** (outside
Play), or `gangsters_residential_condition`. This removes the former comparison
root and labels, retains original block geometry and prepares a collider-free
Resources catalog. The normal Residential generator also installs these controls.
The old `gangsters_residential_neglected` command redirects to this preparation.

## Core integration

The same `ResidentialConditionView` is used by the streamed Core recycler.
Simulation can set `recipe.SetNeglect(value)` (0..1), or call
`RoadDemoBuilder.SetResidentialNeglect(recipeId, value)`. Model state survives
view eviction/map handoff. This value is not part of ContentKey/Revision and does
not fire geometry invalidation, regenerate the block or rebuild navigation.
New city generation starts at zero; save-game storage of district progression is
outside this visual adapter. An external simulation/save owner should restore
its value through the same setter.

The recycler steps appearance work only for attached visible views, under its
existing work budget and a 96-step cap per frame. Each view reserves at most 64
cosmetic slots, instantiated lazily through the existing prefab pool. Hidden slots
remain leased until view disposal, avoiding repeated allocation while dragging.
Before returning source parts to the pool, the adapter restores original material
references and density overrides, returns cosmetic leases, and destroys its own
material instances. There is no per-slider material construction or texture/mesh
readback. At zero neglect the exact original material references are restored.

Weathering keeps the previously reduced subtle damp/runoff appearance. At 5% neglect, cardboard starts collecting beside bins; glass and bottles follow,
then groups of bags from 22%, and overflow from 66%. Each cluster includes bags
instead of relying on a random choice of tiny props. Flat debris is placed above
the measured pavement surface. Flower colour fades; standalone flower meshes droop without deforming their pots. Measured shop panes can receive corrugated shutters;
entrance panes remain clear. Residential previews closure with neglect. In Core,
shutters follow `BusinessOperationalState.Shut`, independently of density; the
slider never closes a trading business or overrides damage presentation.

Optional mesh merging preserves condition-driven source renderers, as it already
preserves moving geometry. Decorative props remain outside merging. The current
Core config disables merging; enabling it may merge fewer surfaces than before.

No automated tests or Play sessions were run for this change, per user request.
Compilation and saved-scene preparation are authoring checks, not visual or
performance acceptance. The user will inspect the appearance and recycler cost.
