# CoreDemo: camera-window block streaming

Implemented 2026-08-28. This is the runtime counterpart of `city-performance-plan.md` L6.
It applies to generated residential blocks; the existing baked core, roads, parks and quays
remain ordinary district geometry.

## The RecyclerView mapping

| Android idea | CoreDemo implementation | Responsibility |
|---|---|---|
| adapter data | `ResidentialBlockModel` | The whole generated residential catalogue, without scene objects. |
| item id/version | `ResidentialBlockRecipe.Id` + `ContentKey` | Stable identity plus deterministic plan hash, generator version and revision. |
| ViewHolder | `CityBlockRecycler.View` | A reusable holder transform with a bound generated payload. |
| layout manager | `CityBlockVisibility` | Intersects ground rectangles and conservative block volumes with the camera footprint/frustum. |
| recycled-view pool | inactive LRU cache + empty-holder pool | Fast return while panning; bounded memory when moving on. |
| notify item changed | `Replace` / `Invalidate` | Evict and rebind only the changed recipe. |

`CoreDistrict.Plan` now deals scene-free block descriptions and residential recipes. Gameplay,
walk obstacles and the 2D map receive footprints for the entire model. `CoreDistrict.Build` only
creates residential GameObjects through `CityBlockRecycler` when a recipe enters the camera
window. CoreDemo remains a thin host over `RoadDemoBuilder`; it does not fork map, traffic,
people, crews, combat or day/night rules.

## View lifecycle

1. The camera ground quadrilateral is expanded by 25 m and intersected with all recipe bounds.
   A conservative 3D recipe volume is also tested against the frustum, so a shallow camera tilt
   admits distant buildings before their ground footprint enters the screen.
2. Visible recipes are sorted nearest to the camera itself.
3. At most one new payload starts per frame. Startup uses the same incremental path as movement;
   it never synchronously clones a hidden inventory of blocks.
4. The origin-space residential composer stands a payload at world identity; only then is its
   ViewHolder moved into the district slot. The generator advances at most 12 yield steps and its
   renderers attach in slices of 64 per frame from
   a 15 m screen-edge lead; flat pieces do not enter the directional shadow pass. Runtime mesh
   merging remains off because its allocation spike costs more than it saves here. Night-window
   materials and street lamps are registered dynamically.
5. A block leaving the wider release area is deactivated, not immediately destroyed. Its bulbs
   leave the global light budget while cached and reuse the same light objects on activation.
6. At most four deactivated payloads remain in the LRU cache. Older payloads are destroyed and
   their empty holders enter the pool.
7. Once the boom is greater than the shared map threshold, every active and cached 3D payload is
   evicted. The existing `TurfMapHud` is the only far view.
8. Descending from the map binds holders around the current map/pivot position, rather than
   rebuilding the whole city.

The camera footprint plus 3D frustum, not a radius alone, is authoritative. This keeps off-screen
blocks out in the usual elevated view and admits blocks visible near a screen corner. Prefetch and
release hysteresis stop edge thrashing. The shared street camera is currently fixed at 55 degrees,
so a vertical right-drag cannot expose an arbitrarily long corridor of detailed blocks; horizontal
right-drag still rotates yaw normally. The frustum path remains the safety net for scripted camera
motion and future device profiles.

`180 m` is a boom threshold, not a guarantee about view depth. If pitch freedom is enabled later,
a shallow view can genuinely see a long corridor through the city while its boom is still 165 m.
Those blocks are legitimate visible ViewHolders. We are deliberately leaving a distant HLOD/proxy
view type for that future requirement instead of paying its complexity in the current locked view.

## Settings and device profiles

The project setting is `Assets/Configs/CityViewConfig.asset`:

| setting | current value | meaning |
|---|---:|---|
| `max3DDistance` | 180 m | Boom greater than this opens the existing 2D map and clears 3D blocks. |
| `streetPitch` | 55 degrees | Fixed normal angle for the shared street camera. |
| `streetPitchFreedom` | 0 degrees | Vertical orbit range on either side; zero disables tilt but preserves yaw. |
| `streetCutaway` | off | Keep exterior shells intact; current residential prefabs have no interiors to reveal. `H` remains a diagnostic toggle. |
| `prefetchMetres` | 25 m | Prepare just outside the camera footprint. |
| `recycleHysteresisMetres` | 45 m | Extra release margin beyond prefetch. |
| `renderHysteresisMetres` | 15 m | Begin/retain renderer attachment before a block reaches the screen. |
| `maxBuildsPerFrame` | 1 | Bound main-thread composition count. |
| `compositionStepsPerFrame` | 12 | Hard cap on generator yield steps advanced in one frame. |
| `rendererAttachBudget` | 64 | Maximum renderer components registered in one frame. |
| `workBudgetMs` | 6 ms | Soft budget for incremental compose/optional merge work; renderer attachment has its own hard per-frame cap. |
| `cachedViews` | 4 | Full block payloads retained for a quick pan back. |
| `prewarmPartLimit` | 5,600 | Retained prefab-root high-water cap. Nothing is cloned speculatively at startup; surplus roots retire incrementally. |
| `minimapViewHeight` | 360 m | Local 2D minimap height; independent of the 180 m map handoff. |

`RoadDemoBuilder.SetMax3DDistance(float)` is the future settings/UI hook. The same
`DemoCamera.mapAt` drives both the map and every recycler, so a 300 m device preset cannot leave
one subsystem at 180 m. No Core host carries a second hard-coded streaming threshold.

## Generator changes

Changing ResidentialDemo's generator does not require camera or pooling changes:

- call `ResidentialBlockRecipe.Replace(...)` when a block receives a new plan or bounds;
- call `Invalidate()` when a material, prefab bake or optimiser dependency changes without a new
  plan;
- bump `ResidentialBlockRecipe.GeneratorVersion` when the composer interprets the same plan
  differently.

The deterministic plan hash, version and revision form `ContentKey`. A resident view with an old
key is evicted and rebound; unrelated blocks remain alive. The full TurfMap and local minimap read
the same registered recipes, so recycling a holder cannot erase a street, building, cafe, subway
or generated green from either map.

## Acceptance checks

`unity command gangsters_streaming_audit --json` runs the pure
catalogue/hash/invalidation/viewport contracts and reports live counts. Run it before or after a
profile window, not inside one: the contract pass owns Unity's main thread while it executes.
Large core seed sweeps remain the separate `gangsters_core` command and must not be mixed into a
live probe.

Measured in a cold CoreDemo, seed 1987, on 2026-08-29:

- model: 97 generated recipes;
- startup performs no speculative block/root prewarm; the first visible window finished at five
  active and two cached views, with every compose, texture-warm and renderer-attachment queue empty;
- the initial pool high-water was 2,193 prefab roots, created by actual visible recipes rather
  than a synchronous 4,291-root startup inventory;
- the worst incremental block build/step during that cold start was 65/37 ms;
- deterministic first-traverse pan at 90.75 m/s ended at five active and four cached views,
  38 built / 29 evicted views, 4,157 retained prefab roots and 8,040 pool reuses;
- compose, attachment, texture-warm and gradual-retirement queues were all zero at route end;
- the configured four-view inactive LRU cap was reached but not exceeded;
- with the project launcher and its 64 MiB graphics ring, startup plus the full route emitted zero
  Graphics Ring Buffer warnings, zero negative-BoxCollider warnings and zero Unity errors;
- the settled post-route sample was 8.20 ms CPU, 5.16 ms main thread and 5.50 ms GPU. This is a
  point sample, not a percentile claim.

The graphics warning was a real Editor stall, but it was not evidence that the machine had run out
of general RAM or VRAM. Unity 6000.5.6f1 defaults this separate graphics-command ring to 16 MiB.
Removing synchronous prewarm made normal startup responsive at that default, but an artificial
90.75 m/s first traverse could still fill it once while many never-seen renderer hierarchies were
registered. `Tools/unity/open-gangsters.command` supplies a bounded 64 MiB ring; the cold rerun above
did not reproduce the warning. Opening the project directly from Hub bypasses that argument.

The pool intentionally grows toward the real first-route high-water rather than cloning every
possible variant up front. A later pass reuses those roots. Surplus roots above 5,600 are disabled
and retired a few at a time, so neither eviction nor `Destroy` becomes a new one-frame burst.

HLOD is intentionally deferred; the runtime recycler does not hide graphics pressure by doing
allocation-heavy mesh merges.

The shallow-pitch measurement is the honest worst case of visibility-only streaming: more than
half of the 97 recipes really intersected the frustum. A distant block HLOD/proxy remains the next
optimisation layer only if free horizon-looking is intentionally restored later.

The core layout deal is also independent of view streaming. In this run it still took about 19.9 s
of the 27.8 s bootstrap. That cost existed before any ViewHolder can be selected and should be
profiled as a separate generator/layout optimisation, not hidden inside the recycler.
