# Residential comparison set

Open `Assets/Scenes/ResidentialDemo.unity`. Its 13 normal blocks remain in their
original positions. Ten matching neglected blocks stand together to the right,
separated from the normal set by at least 45 metres. Both police blocks and the
nightclub are omitted only from the neglected set.

Large world-space labels identify **NORMALNI BLOKOVI** and **ZAPUSTENI BLOKOVI** above the two sets.

In Play, **Tab** jumps to the corresponding place in the other set while preserving
camera angle and zoom. **Home** frames both sets. Movement uses the shared
`RoadDemo.DemoCamera`: WASD/arrows, Q/E, right drag and wheel zoom. The overlay labels
the current set and shows the controls. `NeglectedResidentialDemo.unity` is retired.

Refresh the comparison with `Tools > City > Residential > Add or Refresh Neglected Comparison Set`
or `unity command gangsters_residential_neglected --timeout 60 --json`.
Stop Play first. This copies the current normal block hierarchy, replaces only the
`RESIDENTIAL NEGLECTED COMPARISON` group, and saves ResidentialDemo. It never rerolls
or repositions the normal blocks. If the normal bench was regenerated, refresh the
comparison to obtain matching copies of those new seeds.

`ResidentialNeglect.Apply` owns the opt-in dressing; the editor builder supplies
persistent material variants and prefab creation. Facades, paving and furniture
receive subtle weathering in URP Forward and Deferred. Upper-window repairs sample
authored pane triangles and atlas UVs; litter clusters accompany existing bins.
The dressing has no active colliders and does not change businesses or demographics.
The dressing pass uses mesh/texture readback and is intended for content baking,
not streaming-time composition. District assignment and trade logic are separate.

Saved-scene integrity check:

```sh
unity command run_script --file Tools/ResidentialNeglect/Audit.cs --entry ResidentialNeglectAudit.Main --timeout 60 --json
```

The original standalone set was checked in Play, including camera pan, rotation and
zoom. After user review, weathering contrast was reduced substantially: no broad
cloudy grime, rare faint concrete marks, subtle damp and narrow runoff on walls.
The merged scene is prepared for the user's own Play comparison; its new Tab/Home
shortcuts have not been manually accepted.

Per user instruction, no further Play tests were run after adding the set labels.
