# The Storefront — a live layer over every shop bay (EPIC 32)

> Design brief, 2026-09-03. Standalone: an agent on another machine reads this and the Linear
> epic and touches code; nothing here needs the conversation that produced it. The plan went
> through the contrarian pass on the same day; its findings are folded in and the rulings below
> are the user's.

In one sentence: **every ground-floor shop bay of a residential building gets its own live parts
— glass per pane, a door leaf that swings, boards, fire, a shutter — laid over the Synty wall
that stands today, bound to its own business, so a smashed window is one pane, an opened door is
one door, and a boarded shop is that shop's width.**

## 0. What exists — build on it, do not duplicate

| what | where |
|---|---|
| the residential units, harvested Synty modules kept as nested prefab instances (residential-01: 159 `m_PrefabInstance`) | `Assets/Prefabs/Residential/residential-*.prefab`; the harvest `Assets/Scripts/Editor/ResidentialHarvest.cs` |
| the measured table: footprint, faces, `ShopBays(side, x, z)` per unit — one physical 5 m bay per `SM_Bld_Shop_*` module | `Assets/RoadDemo/ResidentialUnits.cs` (generated — rerun the harvest, never edit) |
| one business site per physical bay (`AddPhysicalBays`, `PhysicalSiteId`) | `Assets/Scripts/Business/ResidentialBusinessSites.cs:104-108, 202-254` |
| the measured opening of every pane, read off the `_Glass` mesh normals: `ResidentialStorefrontOpening { Front, Outward, Right, Width, Height, Group, Entrance, Corner }` | `Assets/RoadDemo/ResidentialStorefrontShell.cs:10-37`; `DiscoverStorefronts` in `Assets/RoadDemo/ResidentialStorefrontDressing.cs:319`, cache per unit TYPE at `:83` |
| the shallow room behind every pane (0.8–1.25 m, opaque back wall) and the 12-slat roller shutter for closed fronts | `ResidentialStorefrontShell.cs` `AddRoom :148`, `AddShutter :168` |
| the per-unit deterministic dice for the dressing (`StorefrontSeed(plan.Seed, unit.Name, I, J, Yaw)`), `PlanStorefronts` (pure, tested for repeat-equality), the 23 % closed mask at `:214` | `ResidentialStorefrontDressing.cs:186, 281` |
| the dressing runs from `ResidentialBlocks.Stand`; `BuildingCutaway.Prepare` is deferred until it has finished | `Assets/RoadDemo/ResidentialBlocks.cs:940-952, 983` |
| door leaves already swing: `DoorSwing` finds children named `*_Door_L` / `*_Door_R` under the doorway transform, hinges them about the measured outer edge, 78°, 0.55 s; `VisitThrough` plays OpeningEntry → Entering → Inside → OpeningExit → Exiting → Closing | `Assets/RoadDemo/DoorBeat.cs:178-275, 446`; precedent `FuelStation.cs:866-883`; `RoomDepth` clamps 1.6–4.5 m at `:710` |
| the doorway a crew visit swings is the **BusinessMarker's transform = the whole building**; a site binds to the tightest direct child ≥ 2.5 m tall (`PieceAt`), a second site on the same piece is refused | `DoorBeat.cs:558`; `Assets/Scripts/Business/BusinessRuntime.cs:229-231, 252, 323-353` |
| the damage layer today: `SmashBusiness / ScorchBusiness / BoardUp / RepairBusiness`; `TryReplaceOriginalGlass` scans every MeshRenderer, finds glass by material name, cuts the triangles, builds shards; a smashed pane holds the whole 120 m chunk open with `heldChunk.Hold()` | `Assets/RoadDemo/ShopDamage.cs:530-627, 740, 1256-1263`; `MergedChunk.cs:84-87` |
| the merge: still geometry folded into 120 m chunks per material; skips only `!enabled`, `isStatic`, `Animated` (ferris wheel, bascule), `SwaysOrFlows`, unreadable; every residential block view is merged | `Assets/RoadDemo/ScenePerf.cs:276-312`; `CityBlockRecycler.cs:710` |
| the pool: renderer state snapshotted at `Create`, `Restore` re-enables everything on every `Acquire` — a disabled module comes back next lease, a destroyed one leaves a dead reference | `Assets/RoadDemo/ResidentialPrefabPool.cs:427-470` |
| the colourway swaps atlas materials per building | `ResidentialBlocks.cs:931-943, 1045` |
| commands: `gangsters_storefront_audit`, `gangsters_storefront_refresh`, `gangsters_door_audit` | `Assets/Scripts/Editor/PipelineCommands.cs` |
| the showroom: every shop module in a row, name above, red tile on the measured door; `Tools/City/Residential/Build Shop Showroom Scene` | `Assets/Scenes/ShopDemo.unity`, `Assets/Scripts/Editor/ShopShowroom.cs`; camera rig self-installs at Play via `Assets/RoadDemo/ReviewSceneCamera.cs` |

Who sees a door today: street pedestrians do not enter residential shops (`ShopEntrance` is stamped only by the old `CityBuilder`, `Assets/RoadDemo/ShopDoors.cs:10-16`; `ResidentialAmbientPeople` uses unit and venue doors). The only consumer of a shop door is the crew visit (`DoorBeat`). The user's ruling widens that (§2.1).

## 1. What was measured (2026-09-03, offline off the binary FBX; checked in ShopDemo)

The eight POLYGON City modules the residential units are built from. Frame: the module's own
(pivot at the NE corner of its cell, the module fills x −5..0, face on +Z). "Left/right" is as
seen **from the street**; the x figures are in the module frame, so the two read opposite ways.

| module | door, from the street | door centre x | glass width | leaves | kind |
|---|---|---|---|---|---|
| SM_Bld_Shop_01 | centre | −2.50 | 1.3 m (frame 1.7) | 2 | glass, set 0.3 m back from the windows |
| SM_Bld_Shop_02 | right | −4.03 | 1.05 m (frame 1.25) | 2 | glass, transom window above |
| SM_Bld_Shop_03 (10 m) | centre | −5.00 | 1.7 m (frame 1.9) | 2 | glass |
| SM_Bld_Shop_04 | centre | −2.50 | 1.2 m | 1 | glass, recessed 0.9 m |
| SM_Bld_Shop_05 | **no door** | — | — | 0 | display window only |
| SM_Bld_Shop_06 | right, ≈ −4.35 | ≈ −4.35 | ≈ 1.1 m | 1 | **solid** panel in the wall mesh, shutter painted on the rest; measure exactly in the editor |
| SM_Bld_Shop_Corner_01 | left end of the +Z face | −0.84 | 1.3 m | 2 | glass |
| SM_Bld_Shop_Corner_02 | on the 45° chamfer at the corner | (−0.77, −0.77) | 0.9 m (face 1.27) | 1 | glass |

What that means:

* **Seven of eight doors are glass.** The frame is in the wall mesh; the door's glass is in the
  separate `_Glass` child, with the shop windows. Hiding the module's `_Glass` renderer hides the
  door glass with it; what is left in the wall at the door is the door's stiles and rails —
  the same plane as the wall, with holes.
* **A doorless module (Shop_05) has no door to bind.** Today every site gets a doorstep at the
  facade centre, which is why a man walks into the glass. Ruling: a doorless bay joins its
  doored neighbour into one 10 m business.
* **Shop_06 is a closed shop as authored** — the one solid door, the shutter painted on.
* `_Glass` is transparent (`Glass_01.mat`, alpha 0.435), so an open doorway would show the void;
  the existing shell (§0) already closes it.

Physical bays per unit, from `ResidentialUnits.cs`: 12 / 18 / 20 / 22 / 9 / 7 / 7 / 5 (residential-06
has **22**; the `Shops` column counts street-visible faces only). Draw-call budget per frame:
2 496 (`Docs/city-performance-plan.md:40`).

## 2. Rulings (the user's, 2026-09-03 — do not relitigate)

1. **Doors open for everyone**: the crew visit (`DoorBeat`) and the people who go in and out
   (`ResidentialAmbientPeople`, the pedestrian shop visit). A door somebody passes through swings.
2. **Corner shops are in round one**, both kinds: the face door of Corner_01 and the chamfer door
   of Corner_02.
3. **The shutter follows the real state** of the business (closed by `BusinessShutdowns`, or the
   clock at night). The 23 % dice is retired; `BusinessPopulation` promises no invented vacancies.
4. Every scene built for this work carries camera controls at Play (`ReviewSceneCamera`).
5. **The shallow rooms go on storefronts only** (the user, 2026-09-06: "fake enterijeri idu samo
   na storefronts"). A building standing on its own ground takes no fake room, in two places:
   * the kit venue in a cafe gap - the coffee shop, the diner, the burger joint - is a whole
     authored building with its own front, so the gap dressing is gated on
     `NeedsStorefrontDressing` (`ResidentialBlocks.cs`, `ResidentialBlocks.Incremental.cs`);
   * the whole-facade routes in `DiscoverStorefronts` (`MeasureFallback`, `AddMissingUnitFaces`)
     only run for a unit that carries a real `SM_Bld_Shop_*` module (`HasAuthoredShopModules`).
     The harvested standalone shops - `radnja1/2/3`, Palm City groups with their own windows and
     stands and no shop module at all - were being given one 12 m room across the front.
   A measured pane, or an authored shop bay, is what makes a shallow room honest.

## 3. Design decisions (from the contrarian pass — the shape of the work)

* **Keep the Synty wall.** Never destroy or disable a module: the pool's `Restore` re-enables a
  disabled one on the next lease and a destroyed one leaves a hole. The live layer hides only the
  `_Glass` renderer, and only AFTER `DiscoverStorefronts` has measured it (the layout cache is per
  unit type; the first instance measured decides for the city). Re-apply on every bind.
* **The leaf is Synty's own door**, cut out of the wall mesh once in the editor (a bake, not a
  runtime cut): per module a doorless wall mesh and one or two leaf meshes with the pivot on the
  hinge edge, the same `PolygonCity_01_A` atlas material, saved under `Assets/CityKit/Storefront/`.
  The door's glass from `_Glass` rides on the leaf. The look does not change; nothing is scaled.
  At Play the module's wall renderer gets the doorless `sharedMesh` (a swap on the renderer, never
  a new object; re-applied on every bind).
* **A `Storefront` per bay** owns the live parts: panes generated from the measured opening
  with the same `Glass_01.mat` (so `IsStoreGlass`, `HasStoreGlass`, `DemoNightWindows.IsPane` keep
  working), the leaf(s) named `*_Door_L` / `*_Door_R` (so `DoorSwing` finds them unchanged),
  the boards (pre-built, hidden), a fire anchor, the shutter (the existing one, driven by state).
* **The merge skips live parts** by a `StorefrontLive` marker component (one line at
  `ScenePerf.cs:281`); the frame stays in the chunk. The verdict asserts `MergedChunk.Of(r) == null`
  for every live renderer. This also removes today's `heldChunk.Hold()` cost: one smashed pane
  no longer re-enables a 120 m chunk. Measure both before and after.
* **The Storefront is the bindable piece**: a 5 × 5 m footprint that `PieceAt` scores above the
  unit root, so every `AddPhysicalBays` site binds its own bay and `DoorSwing` sees only its own
  leaves. The bay must exist before `BuildingCutaway.Prepare` runs (it collects the unit's
  children once) or it floats when the building fades.
* **Determinism**: every new dice (door style, awning, sign) goes into `PlanStorefronts`, which is
  pure and tested for repeat-equality. Nothing reads the block's `System.Random rng` after `Stand`
  — the ShuffleBag relayout trap. `BusinessRegistry.MixSeed` is the business sim's stream, not the
  compose stream; do not cross them.
* **The door in the table**: the harvest measures the door of every bay (the tall pane(s) that
  reach the floor; the solid panel of Shop_06; the chamfer pane of a corner) into
  `ResidentialShopBay.Door`. Every doorstep — `ResidentialBusinessSites`, the marker, `DoorBeat`,
  `BuildingDoor` — reads it. "Facade centre" goes away.
* **The interior is not new work.** The shell's 0.8–1.25 m room stays; `DoorBeat.RoomDepth`
  already clamps 1.6–4.5 m. No interior kit in round one.
* **The colourway** applies to the leaf and the doorless wall the way it applies to the module.

## 4. Tickets

* FRONT-001 — The door in the table: harvest measures it, `ResidentialShopBay.Door`, the doorless
  bay joins its neighbour, every doorstep moves to the door
* FRONT-002 — The cut: the bake that turns each module's door into a doorless wall and a leaf
* FRONT-003 — The live layer: `Storefront` per bay — panes, leaves, boards, marker, merge skip,
  cutaway, pool-safe re-bind, colourway
* FRONT-004 — Binding and the swing: the bay is the piece; only its door opens; crews and people
  go through it
* FRONT-005 — States: Intact / Open / Smashed / Burning / Boarded / Shuttered; `ShopDamage`
  routes to the bay; the shutter follows the business
* FRONT-006 — The bench and the verdict: the showroom's state row, `gangsters_storefront`, the
  MiniCoreDemo run, five seeds
* FRONT-007 — Docs, the decisions recorded, everything to Done

Order is the order above; FRONT-001 and FRONT-002 are independent of each other.

## 5. Acceptance

* `gangsters_door_audit`: zero sites whose doorstep lies on glass or on a doorless bay.
* Showroom (`ShopDemo.unity`): every module stands with its leaf open at 78° and no floating stile;
  the state row shows every state on one module.
* `gangsters_storefront --seed N`: panes == `ShopBays.Length` per unit; every bay's door tile on a
  leaf; the same seed twice gives the same `PlanStorefronts` mask; zero Storefront renderers with
  `MergedChunk.Of(r) != null`.
* MiniCoreDemo, `gangsters_play`: a SmashUp order breaks one bay's glass and leaves the neighbours
  whole; a Torch order burns that bay and boards its width; a collector's visit swings one door;
  a pedestrian's visit swings one door; a shut business shows the shutter, an open one at night
  shows it too, an open one by day does not.
* Draw calls and frame time on MiniCoreDemo before and after, in the ticket's comment.
* `recompile_status --json` clean; `code-review-unity` before every commit.

## 6. Out of scope

An interior kit; the core district's catalogue buildings and the kit storefronts (radnja, pizzapub,
the diners) — the old `ShopDamage` path stays for them; awnings, signs, the concrete slab and the
beach kiosks (dressing, not fronts); a new camera; save data.
