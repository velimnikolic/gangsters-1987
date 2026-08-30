# Gangsters 1987 — Project Architecture Snapshot

Audit date: 2026-08-30

Unity version: 6000.5.6f1

Git base: `main` at `59d00ffd4838`, plus the current uncommitted/untracked working-tree state

Audit type: static source and serialized-project inspection of the current working tree

This document records the implementation that exists now. It does not define a future territory design. No Unity scene, Play Mode session, build, test, or simulation was launched for this audit, so runtime behavior described below is source-derived rather than Play-verified.

## Status legend

| Label | Meaning in this document |
|---|---|
| **IMPLEMENTED** | Concrete production code and an identifiable current runtime path exist. |
| **PARTIAL** | Useful pieces exist, but the end-to-end behavior is incomplete, split between runtimes, inactive in a current bootstrap, or lacks one required state transition. |
| **STUB** | The name, API, field, UI, or placeholder behavior exists, but it does not perform the implied domain behavior. |
| **NOT FOUND** | No corresponding implementation was found in the inspected project source. |

“Extension point” below means an already-existing callable seam, registry, callback, interface, or model boundary. It does not mean that the seam is necessarily sufficient for a territory feature.

## Executive architecture summary

The project currently contains two substantially different city/runtime stacks:

1. **LivingCity / generated-and-saved city stack** under `Assets/Scripts`. It is composed by `GameplayBootstrap` only when a `CityBuilder` is present. It contains generated blocks, runtime `BusinessMarker` components, the shared outfit/personnel simulation, gang-front businesses, the strategic map, civilian traffic, and the older context-action path.
2. **RoadDemo / CoreDemo stack** under `Assets/RoadDemo`. `RoadDemoBuilder` constructs its own road, pedestrian, vehicle, crew, combat, district, streaming, and TurfMap systems. `RoadDemoLedger` installs only the shared personnel/outfit/ledger layer into these demo scenes.

These stacks share some pure data and singleton directors, but they do **not** share one city model, one block model, one navigation model, one business model, or one territory authority.

There are currently three different territory-adjacent representations:

- **IMPLEMENTED:** LivingCity premise holdings, where `BusinessMarker.GangId` is counted by `Outfit.Turf` and displayed by `StrategicMapHud`.
- **PARTIAL:** Core quarter state, where `CityTerritoryRegistry` stores mutable owner/conflict fields, but production gameplay does not currently drive those mutations.
- **IMPLEMENTED as a map derivation:** non-Core TurfMap districts derive an owner from the majority of held `BusinessMarker` footprints. This result belongs to the survey/model view and is not a persisted territory authority.

There is no production save/load system, no persistent fear model, no recurring business-protection payment model, and no strategic territory AI.

## Runtime composition and scene entry points

**Overall status: PARTIAL** — both runtime stacks are substantial, but their domain models are parallel rather than unified.

1. **What currently exists**
   - `GameplayBootstrap` conditionally composes the LivingCity gameplay runtime when a `CityBuilder` exists.
   - It installs `GameplayRuntime`, `PropertyDirector`, `PersonnelDirector`, `OutfitDirector`, `GangDirector`, `PlayerOcclusionHider`, one `PedestrianInteractionDirector`, `CityOverlayHud`, `PersonnelAlmanac`, and `StrategicMapHud`.
   - The playable-mafioso interaction/crime route is explicitly parked in `GameplayBootstrap`: `PlayerMafioso`, `InteractionController`, `WantedSystem`, `WitnessSystem`, `PoliceResponseDirector`, and `WantedHud` are not installed by that bootstrap.
   - `RoadDemoBuilder` composes the RoadDemo/CoreDemo city and simulation. `RoadDemoLedger` adds the shared personnel/outfit/almanac directors because `GameplayBootstrap` exits in scenes without `CityBuilder`.
   - Current `ProjectSettings/EditorBuildSettings.asset` contains no configured scenes.
2. **Important classes/components**
   - `GameplayBootstrap`, `GameplayRuntime`, `RoadDemoBuilder`, `RoadDemoLedger`, `DemoCrews`, `PoliceDispatch`, `TurfMapHud`.
3. **Important interfaces**
   - There is no common top-level runtime or city interface spanning both stacks.
   - Lower-level shared interfaces are documented in their owning sections.
4. **Important data models**
   - LivingCity is based on `CityBuilder`/generated scene objects and runtime registries.
   - RoadDemo/CoreDemo uses `CityLayout`, district objects, lane/pedestrian graphs, `CoreTerritoryPlan`, and `ResidentialBlockModel` recipes.
5. **How objects reference each other**
   - Both stacks rely heavily on scene components, `FindAnyObjectByType`, static registries, and singleton-style `Instance` properties.
   - `RoadDemoLedger` is the explicit bridge from the RoadDemo stack to shared `PersonnelDirector` and `OutfitDirector` state.
6. **Authoritative state**
   - No top-level object owns all project state. Authority is divided among directors, registries, RoadDemo managers, and pure data models.
7. **Important events/callbacks**
   - Runtime composition is mostly lifecycle-driven (`Awake`, `Start`, `Update`) rather than event-driven.
   - Static registry versions and direct C# callbacks are used after composition.
8. **Existing territory-relevant extension points**
   - `OutfitDirector`, `PersonnelDirector`, `CityBlocks`, `CityTerritoryRegistry`, `PropertyRegistry`, `GangRegistry`, `DemoCrews`, and the two map systems are already accessible runtime boundaries.
9. **Current limitations/placeholders**
   - `RoadDemoBuilder.Awake` performs construction only inside `#if UNITY_EDITOR`; the non-Editor path logs that the demo must be built in the editor.
   - A behavior present in one city stack must not be assumed to exist in the other.
   - Empty build settings do not identify a canonical player entry scene.
10. **Relevant file paths**
    - `Assets/Scripts/Gameplay/GameplayBootstrap.cs`
    - `Assets/Scripts/Gameplay/GameplayRuntime.cs`
    - `Assets/RoadDemo/RoadDemoBuilder.cs`
    - `Assets/RoadDemo/RoadDemoLedger.cs`
    - `ProjectSettings/EditorBuildSettings.asset`

## Outfit, campaign, finances, orders, and existing turf arithmetic

**Overall status: IMPLEMENTED for campaign/order bookkeeping; PARTIAL for physical order execution; STUB for protection and intimidation as territory mechanics.**

1. **What currently exists**
   - `OutfitDirector` is a scene singleton wrapper around the pure `CampaignRunner` simulation.
   - The campaign tracks calendar time, cash/accounts, the roster, equipment, relations, jobs/orders, records, tribute, and accumulated order heat.
   - Jobs progress through queued, travelling, working, and finished stages.
   - Daily processing includes standing jobs, practice, discharge/availability, wages/books, tribute, and pending relation changes.
   - The order catalogue already names extortion, intimidation, collection and adjustment of protection, assault, smash-up, raid, arson, bombing, killing, kidnapping, patrol, guard, ambush, exploration, business acquisition/operation, recruitment, bribery, police employment, and donations.
   - `Outfit.Turf` can count held premises by block and return a dominant gang, with no owner on a tie.
2. **Important classes/components**
   - `OutfitDirector`, `CampaignRunner`, `Campaign`, `Accounts`, `OrderTable`, `OrderBook`, `OrderResolution`, `OrderMath`, `Turf`, `Tribute`, `GangRelations`, `CrewJobs`.
3. **Important interfaces**
   - There is no general outfit-service interface.
   - `OutfitDirector` supplies `CampaignRunner` with delegates for distance, holdings collection, and roster movement rather than implementing a named interface.
4. **Important data models**
   - `OrderCategory`, `TargetMode`, `OrderType`, `OrderSpec`, `JobStage`, `Job`, `JobResolution`, `OrderOutcome`, `OrderRecord`, `CrewKit`, `JobOutcome`, `Holding`.
   - A `Job` stores crew id, target blocks or target block id, world X/Z, label, headcount, stage/timers, book depth, and an optional street outcome.
   - `Campaign` starts in 1987 and uses a 364-day year.
5. **How objects reference each other**
   - `OutfitDirector` owns one `CampaignRunner` and exposes its campaign subsystems.
   - It collects holdings by scanning `PropertyRegistry.Businesses` and converting each claimed `BusinessMarker` to a `Holding` using `GangId` and `BlockId`.
   - It resolves the outfit headquarters through `GangRegistry.FrontBusinessOf(0)`.
   - `CrewJobs` maps a ledger `Job.CrewId` to a physical `DemoCrews.Unit`, orders street movement, and reports a street outcome back to the campaign when one exists.
6. **Authoritative state**
   - `CampaignRunner` is authoritative for campaign time, accounts, jobs, order history, tribute, relations, and campaign heat.
   - `OutfitDirector` owns the runtime instance and exposes a `Version` for polling UI/consumers.
   - `Outfit.Turf` is a stateless calculation; it does not own territory.
7. **Important events/callbacks**
   - `OutfitDirector.Update` polls `Ambient.DayClock.Current`, advances campaign hours, and invokes day processing across day boundaries.
   - `CampaignRunner` uses injected callbacks such as roster movement.
   - Consumers primarily poll `OutfitDirector.Version`; there is no domain event stream for individual job or account changes.
8. **Existing territory-relevant extension points**
   - `OrderTable` and `Job` already carry territory-adjacent order vocabulary, block/world targets, crew assignment, timings, outcome records, cash effects, and heat values.
   - `CampaignRunner` already has a holdings-provider seam and daily tick.
   - `CrewJobs` is an existing abstract-job-to-physical-crew bridge.
   - `Turf.DominantIn` is an existing block-control derivation from premise holdings.
9. **Current limitations/placeholders**
   - Extort, Intimidate, and Collect Protection currently resolve as seeded arithmetic, records, money, relations, and heat. They do not mutate a specific `BusinessMarker.Protected`, install a payer, or create recurring protection state.
   - Most order resolutions are abstract rolls. Only `JobResolution.Street` attempts to use a real RoadDemo fight result; absent a physical answer, campaign arithmetic is used.
   - Campaign heat is separate from RoadDemo police heat and is not consumed by a response system.
   - `Tribute` is upstream family-house accounting derived from holdings; it is not a shop paying protection.
10. **Relevant file paths**
    - `Assets/Scripts/Gameplay/OutfitDirector.cs`
    - `Assets/Scripts/Outfit/CampaignRunner.cs`
    - `Assets/Scripts/Outfit/Campaign.cs`
    - `Assets/Scripts/Outfit/Accounts.cs`
    - `Assets/Scripts/Outfit/Orders.cs`
    - `Assets/Scripts/Outfit/OrderResolution.cs`
    - `Assets/Scripts/Outfit/Territory.cs`
    - `Assets/Scripts/Outfit/Tribute.cs`
    - `Assets/Scripts/Outfit/Diplomacy.cs`
    - `Assets/RoadDemo/CrewJobs.cs`

## Boss

**Overall status: IMPLEMENTED as catalogue/story identity; STUB as a personnel or simulated domain entity.**

1. **What currently exists**
   - `GangCatalog` exposes the outfit boss name, “Don Salvatore Ricci,” and a boss model reference used by presentation/catalogue code.
   - The outfit exists as gang id `0` in the gang catalogue/registry.
2. **Important classes/components**
   - `GangCatalog`, `Gang`, `GangRegistry`, `OutfitDirector`.
3. **Important interfaces**
   - No `IBoss`, command-authority interface, or boss lifecycle interface was found.
4. **Important data models**
   - There is no Boss rank in `Personnel.Rank`; only `Hood` and `Lieutenant` exist.
   - No boss record is present in `Roster` or `Character` by default.
5. **How objects reference each other**
   - UI/catalogue paths read the static `GangCatalog` identity/model.
   - `GangRegistry` represents the player outfit as a gang, but does not point to a boss `Character`.
6. **Authoritative state**
   - The boss identity is a static catalogue value. There is no authoritative boss simulation state.
7. **Important events/callbacks**
   - No boss-specific event, callback, order issuer, death, succession, or availability event was found.
8. **Existing territory-relevant extension points**
   - The existing outfit gang id and catalogue identity can identify the player organization in current registries and maps.
9. **Current limitations/placeholders**
   - Boss is not a world actor, roster member, rank, selectable unit, campaign resource, or AI decision-maker in the current implementation.
   - `DemoCrews.Unit.Boss` means the physical crew leader/lieutenant; it is not the organization boss.
10. **Relevant file paths**
    - `Assets/Scripts/Gangs/GangCatalog.cs`
    - `Assets/Scripts/Gangs/Gang.cs`
    - `Assets/Scripts/Gangs/GangRegistry.cs`
    - `Assets/Scripts/Personnel/Character.cs`
    - `Assets/Scripts/Personnel/Roster.cs`
    - `Assets/RoadDemo/DemoCrews.cs`

## Lieutenants, crews, hoods, and physical crew actors

**Overall status: IMPLEMENTED, with a deliberate split between persistent personnel data and RoadDemo physical squad state.**

1. **What currently exists**
   - Pure personnel models represent hoods, lieutenants, crews, equipment, status, stats, wages, loyalty, wanted level, condition, and rap sheets.
   - `PersonnelDirector` seeds and mutates the runtime roster.
   - A crew has one lieutenant id and up to four hood ids.
   - `DemoCrews` materializes personnel crews as RoadDemo street units and synchronizes them when `PersonnelDirector.Version` changes.
2. **Important classes/components**
   - `Character`, `Crew`, `Roster`, `RosterOps`, `RosterSeeder`, `RosterEquipment`, `PersonnelDirector`, `DemoCrews`, `DemoCrews.Unit`, `CrewWalker`, `CrewCar`, `CrewOverlay`, `CrewBar`.
3. **Important interfaces**
   - No shared `ICrew`, `ICharacter`, or actor-location interface exists.
   - `CrewWalker` and crew vehicles interact with RoadDemo road/walk systems through their concrete APIs and `IRoadUser` where applicable.
4. **Important data models**
   - `Rank` contains `Hood` and `Lieutenant`.
   - `Character` stores identity, name, rank, specialty, status, appearance, loyalty, wanted value, wage, return day, condition, rap sheet, stats, and practice state.
   - `Crew` stores id, `LieutenantId`, and `HoodIds`; `Roster.Assignment` is derived from crew membership.
5. **How objects reference each other**
   - Crews reference characters by string id; characters do not store a crew id.
   - `Roster` owns the character and crew collections.
   - `DemoCrews.Unit` keeps the physical walkers, leader, target unit, vehicle, post/order state, and gang id; it maps player units back to personnel crew ids.
   - `GangSeeder` mirrors player personnel ids into `GangMemberIdentity.PersonnelId`; AI gang members generally have gang identities without complete `Character` records.
6. **Authoritative state**
   - `Roster` under `PersonnelDirector` is authoritative for personnel identity, rank, status, crew membership, and equipment.
   - `RosterOps` is the pure mutation layer used by the director.
   - `DemoCrews.Unit` is authoritative for current RoadDemo physical squad position, target, car, post, combat, arrest, and retreat state.
7. **Important events/callbacks**
   - Personnel consumers poll `PersonnelDirector.Version`.
   - Crew deaths and desertions are written back through `PersonnelDirector.Kill` and `PersonnelDirector.Desert`.
   - `CrewWalker.Fired` reports weapon discharge to crew/combat systems.
8. **Existing territory-relevant extension points**
   - Stable crew and personnel ids already connect campaign jobs, UI dossiers, and physical RoadDemo units.
   - `DemoCrews` exposes the selected unit, all units, gang affiliation, positions, movement orders, attack orders, and member alive/fleeing state.
   - `PersonnelDirector` already provides recruit, promote, demote, kill, desert, assign, and equipment operations.
9. **Current limitations/placeholders**
   - There is no persistent character home block, current block, assigned territory, presence contribution, fear effect, or protection-collection assignment.
   - AI families in RoadDemo are not backed by full `PersonnelDirector` rosters.
   - Personnel and physical-state synchronization is version/poll based, not a general actor event model.
10. **Relevant file paths**
    - `Assets/Scripts/Personnel/Character.cs`
    - `Assets/Scripts/Personnel/Crew.cs`
    - `Assets/Scripts/Personnel/Roster.cs`
    - `Assets/Scripts/Personnel/RosterOps.cs`
    - `Assets/Scripts/Personnel/RosterSeeder.cs`
    - `Assets/Scripts/Personnel/RosterEquipment.cs`
    - `Assets/Scripts/Gameplay/PersonnelDirector.cs`
    - `Assets/RoadDemo/DemoCrews.cs`
    - `Assets/RoadDemo/CrewWalker.cs`
    - `Assets/RoadDemo/CrewCar.cs`

## Gangs, rival families, fronts, and gang AI identity

**Overall status: IMPLEMENTED for identities, registries, spawned rival crews, and fronts; PARTIAL for shared economic/front semantics; NOT FOUND for strategic territory AI.**

1. **What currently exists**
   - `GangCatalog` defines 21 gangs: the player outfit plus 20 rivals.
   - `GangRegistry` holds the current gang list and two separate front mappings.
   - LivingCity `GangDirector` selects commercial `BusinessMarker` fronts, assigns their gang ids, and spawns stationary `GangMemberAgent` guards/loiterers.
   - RoadDemo uses `GangSeeder`, `GangFront`, and `FrontDossier`/front books, and spawns physical rival `DemoCrews` units.
2. **Important classes/components**
   - `Gang`, `GangMemberIdentity`, `GangCatalog`, `GangSeeder`, `GangRegistry`, `GangDirector`, `GangMemberAgent`, `GangFront`, `FrontDossier`, `FrontOverlay`, `CoreResidentialFronts`.
3. **Important interfaces**
   - `GangMemberAgent` participates in overlay interfaces through its component setup.
   - No `IGangAI`, `ITerritoryOwner`, or common front/business interface exists.
4. **Important data models**
   - `Gang` stores id, name, `IsPlayer`, front roll, member seed, and member identities.
   - `GangMemberIdentity` can carry a player personnel id.
   - `GangFront` stores gang id/name, books, door/entry/facing/pedestrian link, plus `Damaged` and `Boarded` flags.
   - `CoreResidentialFronts.Site` stores recipe id, address, block id, door, and outward direction independently of recycled views.
5. **How objects reference each other**
   - `GangRegistry.FrontBusinessOf` maps a gang to a LivingCity `BusinessMarker`.
   - `GangRegistry.FrontBooksOf` maps a gang to a separate `FrontDossier`.
   - `GangFront.All` is a static RoadDemo collection.
   - Rival physical units carry `GangId`; player crew identities additionally map to personnel records.
6. **Authoritative state**
   - `GangRegistry` is authoritative for the installed gang list and current front mappings and exposes a registry `Version`.
   - LivingCity premise affiliation remains authoritative on `BusinessMarker.GangId`.
   - RoadDemo front damage/books are authoritative on `GangFront` and its dossier, not on a `BusinessMarker`.
7. **Important events/callbacks**
   - Consumers poll `GangRegistry.Version`.
   - There is no gang-created, front-changed, gang-defeated, or strategic-decision event.
8. **Existing territory-relevant extension points**
   - Stable integer gang ids are already used consistently by personnel mirrors, crews, premise holdings, Core quarter ownership, and both maps.
   - `GangRegistry`, `GangFront.All`, and `CoreResidentialFronts` expose the current family/front locations.
9. **Current limitations/placeholders**
   - LivingCity fronts (`BusinessMarker`) and RoadDemo fronts (`GangFront`) are different models with different state.
   - `GangMemberAgent` only loiters near a front; it does not run missions or make territory decisions.
   - No current system assigns goals, evaluates territory, spends resources, or changes Core ownership for rival families.
10. **Relevant file paths**
    - `Assets/Scripts/Gangs/Gang.cs`
    - `Assets/Scripts/Gangs/GangCatalog.cs`
    - `Assets/Scripts/Gangs/GangSeeder.cs`
    - `Assets/Scripts/Gangs/GangRegistry.cs`
    - `Assets/Scripts/Gangs/GangFronts.cs`
    - `Assets/Scripts/Gangs/FrontBooks.cs`
    - `Assets/Scripts/Gameplay/GangDirector.cs`
    - `Assets/Scripts/Entities/GangMemberAgent.cs`
    - `Assets/RoadDemo/GangFront.cs`
    - `Assets/RoadDemo/RoadDemoBuilder.CoreFronts.cs`

## City structure, districts, quarters, blocks, and streaming

**Overall status: IMPLEMENTED as multiple stack-specific structures; PARTIAL as a project-wide territory geography.**

1. **What currently exists**
   - LivingCity generation uses `CityGrid` to flood-fill cells into numbered blocks and assign `BlockZone` zoning.
   - At runtime, `CityBlocks` parses generated ground slab renderers into a shared static block registry with id, zone, union bounds, slabs, and center.
   - RoadDemo has construction districts (`Pad`, `Suburb`, `Harbor`, `Airport`) with plan/reserve/build/portal/tick lifecycle.
   - CoreDemo has an immutable `CoreTerritoryPlan` containing six named quarters and stable logical blocks, plus mutable `CityTerritoryRegistry` quarter state.
   - Streamed residential content uses durable `ResidentialBlockRecipe`/`ResidentialBlockModel` data while `CityBlockRecycler` reuses views.
2. **Important classes/components**
   - `CityBuilder`, `CityGrid`, `CityBlocks`, `CityBlocks.BlockInfo`, `BlockZone`, `CityLayout`, `DistrictSlot`, `DistrictFrame`, `DistrictReservations`, `DistrictPortal`, `CoreDistrict`, `CoreTerritoryPlan`, `CoreQuarterDefinition`, `CoreBlockDefinition`, `CityTerritoryRegistry`, `CityQuarterState`, `ResidentialBlockRecipe`, `ResidentialBlockModel`, `CityBlockRecycler`.
3. **Important interfaces**
   - `IDistrict`, `IDistrictHost`, `ICityTerritoryHost`, `IStreamedDistrictHost`.
4. **Important data models**
   - LivingCity `BlockZone`: `ResidentialHigh`, `Industrial`, retired `Police`, `Hospital`, `School`, `Park`, `Parking`, `Bank`, `Port`, and `CarSalon`.
   - Core blocks contain numeric id, stable id, name, source name, quarter id, and local bounds.
   - Core quarters contain id, name, bounds, block ids, and neighboring quarter ids.
   - `CityQuarterState` contains owner gang id, conflict flag, attacker gang id, and capture progress.
   - A residential recipe contains id/name, block id, quarter id, local bounds, seed, authored plan, revision/hash, visual height, and turf masses.
5. **How objects reference each other**
   - LivingCity runtime block identity is reconstructed from names such as `ground_{zone}_{blockId}` and renderer bounds.
   - `RoadDemoBuilder` implements `ICityTerritoryHost` and owns its `CityTerritoryRegistry` through `Territories`.
   - `CoreDistrict` loads the Core plan into the registry.
   - Streamed renderers reference recipes; map source collection deliberately reads recipes/model data rather than requiring active recycled views.
6. **Authoritative state**
   - `CityGrid` is generation-time authority but is not retained as the runtime block service for saved cities.
   - `CityBlocks` is the shared LivingCity runtime lookup authority.
   - `CoreTerritoryPlan` is authoritative for Core logical geography; `CityTerritoryRegistry` is authoritative for mutable Core quarter owner/conflict state.
   - `ResidentialBlockModel`/recipes are authoritative for persistent streamed-block plans; recycled GameObjects are views.
7. **Important events/callbacks**
   - `CityTerritoryRegistry.QuarterOwnerChanged` fires on owner changes; `StateStamp` allows polling.
   - `ResidentialBlockRecipe.Changed` and `ResidentialBlockModel.Changed` notify plan/view consumers.
   - `IDistrict.Tick` is called from the RoadDemo builder loop.
8. **Existing territory-relevant extension points**
   - LivingCity: `CityBlocks.Get`, `Nearest`, and `At` expose runtime block lookup.
   - Core: `QuarterAt`, `BlockAt`, `State`, `SetOwner`, `Contest`, `ClearContest`, `AreNeighbours`, `WorldBounds`, and `BattleAnchor` already exist.
   - Recipes expose stable block and quarter ids even when their views are not active.
9. **Current limitations/placeholders**
   - LivingCity has no implemented neighborhood/district aggregation. `BlockZone` is land use, not a neighborhood or political district.
   - `CityBlocks.At` returns no block on road space because blocks are derived from ground slab bounds.
   - `PropertyDirector`, `StrategicMapHud`, and `BlockOverlayHud` still contain older private ground-parsing logic in addition to `CityBlocks`.
   - RoadDemo construction districts are not gang territory districts.
   - The Core registry mutation API is implemented, but no non-test production caller was found for `SetOwner`, `Contest`, or `ClearContest`; states therefore begin unclaimed and remain undriven by current gameplay.
   - Core plan comments describe save/load-friendly data, but no persistence layer exists.
10. **Relevant file paths**
    - `Assets/Scripts/Generation/CityBuilder.cs`
    - `Assets/Scripts/Generation/CityGrid.cs`
    - `Assets/Scripts/Generation/BlockZone.cs`
    - `Assets/Scripts/Gameplay/CityBlocks.cs`
    - `Assets/RoadDemo/District.cs`
    - `Assets/RoadDemo/CityLayout.cs`
    - `Assets/RoadDemo/CoreTerritory.cs`
    - `Assets/RoadDemo/CoreDistrict.cs`
    - `Assets/RoadDemo/ResidentialBlockModel.cs`
    - `Assets/RoadDemo/CityBlockRecycler.cs`
    - `Assets/RoadDemo/RoadDemoBuilder.Streaming.cs`

## Buildings, businesses, shops, premises, and fronts

**Overall status: IMPLEMENTED in LivingCity; PARTIAL and structurally separate in RoadDemo/CoreDemo.**

1. **What currently exists**
   - `PropertyDirector` classifies generated LivingCity buildings and adds runtime `BusinessMarker` components to commercial, industrial, and port premises.
   - A business has category, display name, block id, weekly income, owner, protected flag, and gang id.
   - `PropertyRegistry` maintains static runtime lists of owners and businesses.
   - `ShopEntrance` records door/facing information.
   - RoadDemo fronts use `GangFront`; Core/residential buildings use logical plans, recipes, bounds, and recycled views rather than generalized business components.
2. **Important classes/components**
   - `BusinessMarker`, `PropertyOwner`, `PropertyRegistry`, `PropertyDirector`, `ShopEntrance`, `GangFront`, `CoreResidentialFronts`, `ResidentialBlockRecipe`, `ResidentialTurfCatalog`, `TurfBuilding`.
3. **Important interfaces**
   - `BusinessMarker` implements `IOverlaySubject` and `IOverlayStyledSubject`.
   - There is no common `IBusiness`, `IPremise`, `IProtectionPayer`, or building-domain interface across the two stacks.
4. **Important data models**
   - `BusinessMarker` data is held directly on a runtime MonoBehaviour.
   - `PropertyOwner` is a plain owner record.
   - `GangFront` carries books, entry geometry, and damaged/boarded flags.
   - `TurfBuilding` is a map projection record containing a world transform/footprint and an optional `BusinessMarker`; its gang id is `-1` when no business is attached.
5. **How objects reference each other**
   - `PropertyDirector` derives a block id from generated building/ground placement and registers each marker with `PropertyRegistry` and `OverlayRegistry`.
   - `GangDirector` assigns selected LivingCity fronts by writing `BusinessMarker.GangId` and recording them in `GangRegistry`.
   - RoadDemo `GangFront` is not a `BusinessMarker` and does not register in `PropertyRegistry`.
6. **Authoritative state**
   - `BusinessMarker.Owner`, `Protected`, and `GangId` are the LivingCity premise-level state; `GangId` is the source used for holdings/control calculations.
   - `PropertyRegistry` is an index, not the owner of each marker’s mutable fields.
   - `GangFront` is authoritative for RoadDemo front damage/boarded/books state.
   - Residential/Core recipes are authoritative for streamed building plans and map masses, not business economics.
7. **Important events/callbacks**
   - `PropertyRegistry.Version` changes on register/unregister.
   - Direct writes to an existing marker’s `Owner`, `Protected`, or `GangId` do not themselves bump `PropertyRegistry.Version` and emit no dedicated event.
   - `BusinessMarker` participates in overlay registry/version updates through registration lifecycle.
8. **Existing territory-relevant extension points**
   - LivingCity markers already expose block id, gang id, owner, protected flag, weekly income, world transform, map style, and overlay selection.
   - `PropertyRegistry.Businesses` provides an enumerable premise set.
   - `ShopEntrance`, `GangFront.Entry`, and Core front sites provide existing physical door/approach positions.
9. **Current limitations/placeholders**
   - `BusinessMarker.Protected` is displayed/read by map UI, but no production writer or protection lifecycle was found.
   - No business-specific fear, payer, collector, debt, payment schedule, refusal, intimidation state, or protection event exists.
   - RoadDemo/Core building footprints usually have no `BusinessMarker`; the TurfMap default TAKE IT action cannot claim them.
   - `ShopDamage` changes `GangFront.Damaged`/`Boarded`; it does not change business income, protected state, ownership, or territory control.
10. **Relevant file paths**
    - `Assets/Scripts/Entities/BusinessMarker.cs`
    - `Assets/Scripts/Entities/ShopEntrance.cs`
    - `Assets/Scripts/Gameplay/PropertyRegistry.cs`
    - `Assets/Scripts/Gameplay/PropertyDirector.cs`
    - `Assets/RoadDemo/GangFront.cs`
    - `Assets/RoadDemo/ShopDamage.cs`
    - `Assets/RoadDemo/ResidentialBlockModel.cs`
    - `Assets/RoadDemo/ResidentialTurfCatalog.cs`
    - `Assets/RoadDemo/TurfMapModel.cs`

## Existing territory-control state and derivations

**Overall status: PARTIAL — multiple usable implementations exist, but they are separate authorities/derivations.**

1. **What currently exists**
   - LivingCity premise control: each `BusinessMarker.GangId` denotes the gang holding that premise.
   - LivingCity block control: `Turf.DominantIn` counts held premises per gang in one block and reports no owner for no holdings or a tie.
   - Core quarter control: `CityQuarterState` stores direct owner and conflict/capture fields.
   - Non-Core TurfMap district control: `TurfMapSurvey` derives a district owner from the family holding the largest number of mapped business footprints and returns a contested sentinel on ties.
2. **Important classes/components**
   - `BusinessMarker`, `Holding`, `Turf`, `CityTerritoryRegistry`, `CityQuarterState`, `TurfMapSurvey`, `TurfMapModel`, `TurfDistrict`, `StrategicMapHud`.
3. **Important interfaces**
   - `ICityTerritoryHost` exposes the Core registry to hosted systems.
   - There is no shared territory-control interface across premise, block, district, and quarter concepts.
4. **Important data models**
   - `Holding { GangId, BlockId }`.
   - `CityQuarterState { OwnerGangId, Conflict, AttackerGangId, CaptureProgress }`.
   - Map-side `TurfDistrict` owner/conflict projection.
5. **How objects reference each other**
   - `OutfitDirector` produces holdings from `PropertyRegistry`.
   - `StrategicMapHud` uses those holdings and business footprints.
   - `TurfMapSurvey` reads both business gang ids and Core quarter states into its map model.
6. **Authoritative state**
   - Premise authority: `BusinessMarker.GangId`.
   - Core quarter authority: `CityTerritoryRegistry`.
   - `Turf.DominantIn` and the non-Core TurfMap owner are derived values and own no durable state.
7. **Important events/callbacks**
   - Core owner changes can raise `QuarterOwnerChanged`; conflict/capture changes are observed through `StateStamp` rather than a complete event set.
   - Premise gang-id changes have no dedicated event.
8. **Existing territory-relevant extension points**
   - `Turf.DominantIn`, registry state accessors/mutators, holdings enumeration, and TurfMap owner snapshots are current calculation/access seams.
   - `TurfMapHud.ClaimRule` is a replaceable `Func` used by the TAKE IT action.
9. **Current limitations/placeholders**
   - The default TAKE IT implementation is explicitly a stub: after a selected crew arrives and no nearby living enemy remains, it writes `building.Business.GangId = crew.GangId` directly.
   - It does not perform intimidation, fear, economic, relation, heat, notification, or persistence behavior.
   - TAKE IT does not alter `CityTerritoryRegistry` quarter state and does nothing when the selected building has no `BusinessMarker`.
   - No system reconciles direct quarter ownership with derived premise-majority control.
10. **Relevant file paths**
    - `Assets/Scripts/Outfit/Territory.cs`
    - `Assets/Scripts/Gameplay/OutfitDirector.cs`
    - `Assets/Scripts/Entities/BusinessMarker.cs`
    - `Assets/RoadDemo/CoreTerritory.cs`
    - `Assets/RoadDemo/TurfMapSurvey.cs`
    - `Assets/RoadDemo/TurfMapHud.cs`

## NPC movement and pedestrian simulation

**Overall status: IMPLEMENTED as two independent movement stacks.**

1. **What currently exists**
   - LivingCity uses the Synty `PathFinding` component over `Tile`/`Path` graphs for sidewalk, road, and rail route types.
   - `HumanBehavior` owns pedestrian path traversal and reports route completion.
   - `PedestrianSpawner`, `PedestrianAgent`, `PedestrianInteractionDirector`, `AgentLocomotion`, and `PedestrianRegistry` add spawning, schedules/activities, local scripted actions, chat/routine coordination, and spatial avoidance.
   - RoadDemo pedestrians use `PedNode`/`PedLink` graphs.
   - RoadDemo crew walkers use `WalkRoute.Plan`, a separate 2.5-meter lattice A* pathfinder over `WalkObstacles`, followed by string-pulling and local steering.
2. **Important classes/components**
   - LivingCity: `PathFinding`, `Tile`, `Path`, `HumanBehavior`, `PedestrianSpawner`, `PedestrianAgent`, `PedestrianInteractionDirector`, `AgentLocomotion`, `PedestrianRegistry`, `PedestrianSteering`.
   - RoadDemo: `PedestrianAgent`, `CivilianAgent`, `PedNode`, `PedLink`, `CrewWalker`, `WalkRoute`, `WalkObstacles`.
3. **Important interfaces**
   - `IPathFinder` exists but is not implemented or used by the current pathfinding components.
   - RoadDemo has no common pedestrian/crew navigation interface.
4. **Important data models**
   - LivingCity paths are scene graph objects categorized by `PathType`.
   - RoadDemo pedestrian graphs use node/link records.
   - `WalkRoute` uses cached lattice cells, obstacle-version invalidation, and road-avoidance options.
5. **How objects reference each other**
   - LivingCity agents hold concrete `HumanBehavior`/`PathFinding` components and interact through `PedestrianRegistry` and the central interaction director.
   - RoadDemo crew walkers call the static `WalkRoute`/`WalkObstacles` services; the builder registers fixed and live obstacles.
   - `WalkObstacles` includes props/solids, city bounds/fences, standing actors, and live street traffic queries.
6. **Authoritative state**
   - Individual behavior components own each LivingCity route/activity state.
   - `PedestrianInteractionDirector` owns time-sliced social/activity coordination, not world navigation.
   - `CrewWalker` owns each RoadDemo hood’s locomotion/combat state; `WalkObstacles` is the current shared free-ground query and `Version` authority.
7. **Important events/callbacks**
   - `HumanBehavior.routeCompleted` is used by higher-level pedestrian behaviors.
   - RoadDemo movement is primarily polled/ticked; crew commands directly replace walker goals/routes.
   - `WalkObstacles.Version` invalidates route caches when fixed geometry changes.
8. **Existing territory-relevant extension points**
   - Both actor stacks expose world positions that can be passed to their stack’s block lookup.
   - RoadDemo commands already accept destination positions and can route a crew to a block/business approach.
   - `PedestrianRegistry` and `DemoCrews.Units` provide current actor collections in their respective stacks.
9. **Current limitations/placeholders**
   - No NavMesh is the active RoadDemo crew solution; `DemoCrews` removes prefab `NavMeshAgent` components.
   - `IPathFinder.GetPath(Vector3)` is an orphan/stub contract relative to the concrete implementations.
   - There is no unified actor, location, route, or occupancy service across stacks.
   - LivingCity daily pedestrian routines search for concrete `CityClock`; they do not use `IDayClock`, so `DemoClock` is not a drop-in driver for those routines.
10. **Relevant file paths**
    - `Assets/Scripts/City/PathFinding.cs`
    - `Assets/Scripts/City/Path.cs`
    - `Assets/Scripts/City/Tile.cs`
    - `Assets/Scripts/City/HumanBehavior.cs`
    - `Assets/Scripts/City/Interfaces/IPathFinder.cs`
    - `Assets/Scripts/Entities/PedestrianSpawner.cs`
    - `Assets/Scripts/Entities/PedestrianAgent.cs`
    - `Assets/Scripts/Entities/PedestrianInteractionDirector.cs`
    - `Assets/Scripts/Entities/AgentLocomotion.cs`
    - `Assets/Scripts/Entities/PedestrianRegistry.cs`
    - `Assets/RoadDemo/PedestrianAgent.cs`
    - `Assets/RoadDemo/CivilianAgent.cs`
    - `Assets/RoadDemo/CrewWalker.cs`
    - `Assets/RoadDemo/WalkRoute.cs`
    - `Assets/RoadDemo/WalkObstacles.cs`

## Vehicle movement and road navigation

**Overall status: IMPLEMENTED as two independent traffic/vehicle stacks.**

1. **What currently exists**
   - LivingCity `VehicleSpawner` maintains civilian vehicle population. Synty `CarBehavior` follows `PathFinding` road routes, while `TrafficAgent` chooses wandering/exit behavior and replaces completed traffic.
   - `TrafficRegistry` tracks nearby dynamic traffic bodies for following/collision decisions.
   - RoadDemo uses `LaneNet` carriageways, nodes, edges, connectors, and lane routing. `RoadCar` is a kinematic vehicle model with `GoTo`, parking, route, speed, heading, and goal state.
   - Crew, police, civilian, bike, and special vehicles build on or use the RoadDemo vehicle model.
2. **Important classes/components**
   - LivingCity: `VehicleSpawner`, `CarBehavior`, `TrafficAgent`, `TrafficRegistry`, `TrafficBody`, `MapEdgeGates`.
   - RoadDemo: `LaneNet`, `Carriageway`, `RoadNode`, `RoadEdge`, `Connector`, `RoadCar`, `DemoVehicle`, `CrewCar`, `StreetTraffic`, `RoadNet`, `RoadPath`.
3. **Important interfaces**
   - `IRoadUser` is used by RoadDemo road users.
   - `IRoadModel` abstracts part of the RoadDemo road model.
   - No common vehicle/navigation interface spans LivingCity and RoadDemo.
4. **Important data models**
   - `RoadCar` stores road/lane coordinates, longitudinal/lateral position, heading, route/via state, speed, parked state, and goal state.
   - LivingCity traffic state is component-based around route objects and registered traffic bodies.
5. **How objects reference each other**
   - LivingCity agents hold `CarBehavior`/`PathFinding`; spawners use edge gates and registry state.
   - RoadDemo cars reference `LaneNet`; `CrewCar` wraps drive-to, park-near, and drive-by behavior for a crew unit.
   - `RoadDemoBuilder` registers and ticks vehicles.
6. **Authoritative state**
   - LivingCity individual vehicle route/locomotion state is owned by `CarBehavior`/`TrafficAgent`; `TrafficRegistry` is the spatial index.
   - RoadDemo individual vehicle authority is `RoadCar`; `LaneNet` is the road-graph authority.
7. **Important events/callbacks**
   - `CarBehavior.routeCompleted` drives LivingCity traffic decisions.
   - RoadDemo vehicle movement is tick-based and command-driven rather than event-based.
8. **Existing territory-relevant extension points**
   - `CrewCar.DriveTo`/`ParkNear` and `RoadCar.GoTo` already carry crews to world destinations.
   - Vehicle world positions can be queried against Core/LivingCity block lookups.
   - Street traffic is already included in RoadDemo local obstacle/steering queries.
9. **Current limitations/placeholders**
   - `RoadNet`/`RoadPath` coexist with the current `LaneNet`/`RoadCar` path; code must identify which model a consumer actually uses.
   - No vehicle carries a persistent home block, territory route, supply route, collection route, or presence contribution.
   - There is no cross-stack route request or vehicle registry.
10. **Relevant file paths**
    - `Assets/Scripts/Entities/VehicleSpawner.cs`
    - `Assets/Scripts/Entities/TrafficAgent.cs`
    - `Assets/Scripts/Entities/TrafficRegistry.cs`
    - `Assets/Scripts/City/CarBehavior.cs`
    - `Assets/Scripts/Generation/MapEdgeGates.cs`
    - `Assets/RoadDemo/LaneNet.cs`
    - `Assets/RoadDemo/RoadCar.cs`
    - `Assets/RoadDemo/DemoVehicle.cs`
    - `Assets/RoadDemo/CrewCar.cs`
    - `Assets/RoadDemo/StreetTraffic.cs`
    - `Assets/RoadDemo/RoadNet.cs`
    - `Assets/RoadDemo/RoadPath.cs`

## Player commands, selection, and interactions

**Overall status: IMPLEMENTED for RoadDemo crew commands; PARTIAL/parked for LivingCity context actions; STUB for territory/business intimidation interactions.**

1. **What currently exists**
   - RoadDemo supports selecting player crews, ordering movement, attacking another unit, fighting, boarding/using cars, parking, and map-issued movement/combat.
   - `CrewJobs` can turn campaign jobs into street destinations and, for street-resolution jobs, physical rival fights.
   - `BuildingCardPicker` handles world click selection through a raycast and a chained static click-veto mechanism used by overlays.
   - LivingCity has a context-target/action architecture and context menu, with kill/cancel actions in the active registry definition.
2. **Important classes/components**
   - `DemoCrews`, `CrewOverlay`, `CrewBar`, `CrewJobs`, `TurfMapHud`, `BuildingCardPicker`, `FrontOverlay`, `InteractionController`, `DesktopInteractionInput`, `ContextActionRegistry`, `ContextMenuUI`, `KillAction`, `CancelAction`, `BuyWeaponAction`.
3. **Important interfaces**
   - `IContextTarget`, `IContextAction`, `IInteractionInput`, `IMapTargetingConsumer`.
4. **Important data models**
   - RoadDemo commands operate directly on `DemoCrews.Unit`, target units, destination vectors, and vehicles.
   - LivingCity context actions carry availability/label/execute behavior against an `IContextTarget`.
   - Map-order targeting creates campaign `Job` records with block and world targets.
5. **How objects reference each other**
   - `DemoCrews.Selected` identifies the current street unit; crew UI and TurfMap delegate orders back to `DemoCrews`.
   - `PersonnelAlmanac.Orders` implements `IMapTargetingConsumer`; `StrategicMapHud` returns the selected map target to it.
   - `InteractionController` expects a `PlayerMafioso` and context targets, but the current `GameplayBootstrap` does not install that playable path.
6. **Authoritative state**
   - `DemoCrews`/`DemoCrews.Unit` own current RoadDemo selection and issued physical orders.
   - `OrderBook` owns accepted campaign jobs.
   - The context action registry owns action availability for the parked LivingCity path.
7. **Important events/callbacks**
   - Commands are primarily direct method calls (`Select`, `OrderSelected`, `OrderUnit`, `MarchTo`, attack/board/drive methods).
   - Map targeting uses the `IMapTargetingConsumer` callback.
   - `BuildingCardPicker.ClickVeto` is a static delegate chain used to intercept clicks.
8. **Existing territory-relevant extension points**
   - Existing selection, destination orders, campaign job targets, map targeting, business overlays, and front door positions already connect player intent to physical locations.
   - `TurfMapHud.ClaimRule` is the current callback seam behind TAKE IT.
9. **Current limitations/placeholders**
   - `BusinessMarker` does not implement `IContextTarget`; no racket, threaten, collect, or protection action is registered for it.
   - `BuyWeaponAction` exists, but no current registration call was found; a source comment claiming bootstrap registration is stale.
   - The LivingCity context path is not currently installed by `GameplayBootstrap`.
   - TurfMap TAKE IT is a direct claim stub rather than a physical intimidation interaction.
10. **Relevant file paths**
    - `Assets/RoadDemo/DemoCrews.cs`
    - `Assets/RoadDemo/CrewOverlay.cs`
    - `Assets/RoadDemo/CrewBar.cs`
    - `Assets/RoadDemo/CrewJobs.cs`
    - `Assets/RoadDemo/TurfMapHud.cs`
    - `Assets/Scripts/Camera/BuildingCardPicker.cs`
    - `Assets/Scripts/Gameplay/IContextTarget.cs`
    - `Assets/Scripts/Gameplay/IContextAction.cs`
    - `Assets/Scripts/Gameplay/InteractionInput.cs`
    - `Assets/Scripts/Gameplay/InteractionController.cs`
    - `Assets/Scripts/Gameplay/ContextActionRegistry.cs`
    - `Assets/Scripts/Gameplay/ContextMenuUI.cs`

## Maps, overlays, and world-space feedback

**Overall status: IMPLEMENTED for both stacks; PARTIAL as one territory visualization system.**

1. **What currently exists**
   - LivingCity `StrategicMapHud` uses a second orthographic camera to show the actual city top-down, select individual buildings, return map targets, draw business-held territory washes, and show block cards.
   - `BlockOverlayHud` is a debug/read-only overlay for generated block id/zone labels.
   - `CityOverlayHud` displays registered world subjects, selection, hover, cards, and markers.
   - RoadDemo TurfMap uses a surveyed/rasterized map, a dedicated building layer, live crew/traffic projections, district fills, labels, a full panel, and minimap.
   - RoadDemo crew/front/combat UI supplies world-space crew markers, order markers, intent lines, front cards, and announcements.
2. **Important classes/components**
   - `StrategicMapHud`, `BlockOverlayHud`, `CityOverlayHud`, `OverlayRegistry`, `TurfMapHud`, `TurfMapSurvey`, `TurfMapModel`, `TurfMapPanel`, `TurfMapBuildingLayer`, `TurfMapLabels`, `TurfMinimap`, `CrewOverlay`, `CombatIntentOverlay`, `FrontOverlay`, `DemoLotOverlay`.
3. **Important interfaces**
   - `IOverlaySubject`, `IOverlayStyledSubject`, `IMapTargetingConsumer`.
4. **Important data models**
   - LivingCity overlays expose label, world anchor, focus/selection behavior, color/style, and subject identity.
   - RoadDemo `TurfBuilding`, `TurfCrew`, and `TurfDistrict` project buildings, physical units/personnel data, and territory/district state into the map.
5. **How objects reference each other**
   - `BusinessMarker` and `GangMemberAgent` register with `OverlayRegistry`.
   - `StrategicMapHud` scans premises/ground, map targets, and overlay subjects.
   - `TurfMapSurvey` reads RoadDemo/Core geometry, `DemoCrews`, optional `BusinessMarker`s, and `CityTerritoryRegistry` state into a `TurfMapModel`.
6. **Authoritative state**
   - Neither map is authoritative for campaign or territory state except for the TurfMap default claim callback’s direct write to `BusinessMarker.GangId`.
   - `OverlayRegistry` is authoritative only for its current subject list and `Version`.
   - Map geometry and owner colors are projections of city/business/Core/crew state.
7. **Important events/callbacks**
   - Overlay consumers poll `OverlayRegistry.Version` and other director/registry versions.
   - `IMapTargetingConsumer` returns chosen world/block targets to an initiating UI.
   - `TurfMapHud.ClaimRule` controls TAKE IT completion behavior.
   - `CrewOverlay.Announce` provides a timed RoadDemo banner.
8. **Existing territory-relevant extension points**
   - StrategicMap already renders premise ownership and computes block cards from holdings.
   - TurfMap already renders district/Core owner/conflict state, building footprints, crews, traffic, and selected targets.
   - World-space subject/crew/order/combat marker systems already exist for local feedback.
9. **Current limitations/placeholders**
   - StrategicMap territory wash covers held business footprints, not complete controlled block polygons.
   - Core quarter ownership and non-Core business-majority ownership have different sources.
   - Map updates depend on polling/versions; direct business gang-id writes do not produce a property-change event/version increment.
   - `BlockOverlayHud`, `StrategicMapHud`, and `PropertyDirector` retain duplicate ground parsing instead of uniformly consuming `CityBlocks`.
   - No territory-specific world marker, boundary, control-change effect, or block-state notification exists.
10. **Relevant file paths**
    - `Assets/Scripts/UI/StrategicMapHud.cs`
    - `Assets/Scripts/UI/BlockOverlayHud.cs`
    - `Assets/Scripts/UI/OverlaySubject.cs`
    - `Assets/Scripts/UI/CityOverlayHud.cs`
    - `Assets/Scripts/UI/PersonnelAlmanac.Orders.cs`
    - `Assets/RoadDemo/TurfMapHud.cs`
    - `Assets/RoadDemo/TurfMapSurvey.cs`
    - `Assets/RoadDemo/TurfMapModel.cs`
    - `Assets/RoadDemo/TurfMapPanel.cs`
    - `Assets/RoadDemo/TurfMapBuildingLayer.cs`
    - `Assets/RoadDemo/TurfMinimap.cs`
    - `Assets/RoadDemo/CrewOverlay.cs`
    - `Assets/RoadDemo/CombatIntentOverlay.cs`

## UI architecture and notifications

**Overall status: IMPLEMENTED for current demo/runtime surfaces; PARTIAL for shared notification and modal coordination.**

1. **What currently exists**
   - Most runtime UI is built in code with uGUI and TextMeshPro; `BuildingCardPicker` also uses an IMGUI card.
   - LivingCity/shared UI includes `PersonnelAlmanac`, finances/personnel/armory/diplomacy/newspaper pages, a hidden Orders page, the Strategic Map, city overlays, block debug overlay, clock HUD, and ledger visual primitives.
   - RoadDemo UI includes the clock/top bar, crew bar/markers, TurfMap/panel/minimap, front dossier, combat intent overlay, and lot overlay.
2. **Important classes/components**
   - `PersonnelAlmanac` and its partial page classes, `StrategicMapHud`, `CityOverlayHud`, `BlockOverlayHud`, `CityClockHud`, `LedgerKit`, `LedgerStyle`, `LedgerText`, `UiSkin`, `DemoUi`, `DemoClockHud`, `CrewBar`, `CrewOverlay`, `TurfMapHud`, `TurfMapPanel`, `FrontOverlay`, `CombatIntentOverlay`.
3. **Important interfaces**
   - `IMapTargetingConsumer`, `IOverlaySubject`, `IOverlayStyledSubject`.
   - No shared notification-service interface was found.
4. **Important data models**
   - UI reads `Roster`, `Campaign`, `Accounts`, `OrderBook`, gang/front dossiers, map projection models, and registry/version state directly.
   - `LedgerModelSet` is a ScriptableObject presentation catalogue.
5. **How objects reference each other**
   - UI generally locates singleton directors/scene components and rebuilds when a `Version`/stamp changes.
   - Several UIs use static modal flags such as open/input-blocked state.
   - Input is read directly from Input System devices; no project `.inputactions` asset is used by these paths.
6. **Authoritative state**
   - UI is a view/controller layer. Directors, registries, and RoadDemo managers remain authoritative.
   - `PersonnelAlmanac` can submit orders, and TurfMap can submit street commands/its default direct claim, but their displayed copies are not state authorities.
7. **Important events/callbacks**
   - Director/registry version polling drives most refreshes.
   - `CrewOverlay.Announce(text, seconds, tint)` is a static RoadDemo notification entry point.
   - Orders UI uses a local note/status field rather than a shared notification channel.
8. **Existing territory-relevant extension points**
   - Ledger pages, map panels, overlay subjects, selection cards, crew markers, and the RoadDemo announcement banner are existing presentation surfaces.
9. **Current limitations/placeholders**
   - There is no cross-stack notification queue/service or persisted news/event log for territory changes.
   - `CrewOverlay.Announce` is tied to the RoadDemo overlay.
   - The Classified almanac page is incomplete/partial, and the Orders page is not a normal visible tab.
   - Modal/input ownership is coordinated by static flags rather than one UI navigation service.
10. **Relevant file paths**
    - `Assets/Scripts/UI/PersonnelAlmanac.cs`
    - `Assets/Scripts/UI/PersonnelAlmanac.Orders.cs`
    - `Assets/Scripts/UI/PersonnelAlmanac.Finances.cs`
    - `Assets/Scripts/UI/PersonnelAlmanac.Personnel.cs`
    - `Assets/Scripts/UI/PersonnelAlmanac.Classified.cs`
    - `Assets/Scripts/UI/StrategicMapHud.cs`
    - `Assets/Scripts/UI/CityOverlayHud.cs`
    - `Assets/Scripts/UI/LedgerKit.cs`
    - `Assets/Scripts/UI/LedgerStyle.cs`
    - `Assets/RoadDemo/DemoUi.cs`
    - `Assets/RoadDemo/CrewBar.cs`
    - `Assets/RoadDemo/CrewOverlay.cs`
    - `Assets/RoadDemo/TurfMapPanel.cs`

## AI architecture

**Overall status: IMPLEMENTED for local tactical/civilian behavior; NOT FOUND for strategic territory control.**

1. **What currently exists**
   - LivingCity civilian behavior is component/coroutine/state driven. `PedestrianInteractionDirector` centrally time-slices ambient activities and interactions.
   - LivingCity gang members loiter near their assigned fronts.
   - RoadDemo uses hand-written state machines and manager loops for rival crews, individual crew walkers, civilians, drivers, and police dispatch.
   - Rival crews can react to nearby player crews, weapon fire, damage, searches, chases, retreat, panic, arrest, and combat outcomes.
2. **Important classes/components**
   - `PedestrianAgent`, `PedestrianInteractionDirector`, `GangMemberAgent`, `DemoCrews`, `CrewWalker`, `CivilianAgent`, `DriverNerve`, `PoliceDispatch`, `RoadCar`, `ResidentialBlockLife`.
3. **Important interfaces**
   - No behavior-tree, GOAP, utility-AI, strategic-gang-AI, or territory-planner interface was found.
   - `IPoliceUnit` provides a narrower police-unit contract.
4. **Important data models**
   - AI state is mostly stored on runtime components/units as enums, timers, targets, health, fear/panic, search positions, posts, and movement goals.
   - There is no strategic gang objective, territory desire, presence budget, or neighborhood knowledge model.
5. **How objects reference each other**
   - RoadDemo managers scan their registered units and `StreetAlarm`; walkers reference current targets and route services.
   - LivingCity pedestrians use registries/directors and concrete clock/path components.
6. **Authoritative state**
   - Each local AI component/manager owns its tactical state.
   - No component owns strategic family decisions or territory goals.
7. **Important events/callbacks**
   - `StreetAlarm.OnShot`/`OnDeath`, fire callbacks, route completion, and manager polling trigger tactical reactions.
   - No territory-owner-change listener driving gang behavior was found.
8. **Existing territory-relevant extension points**
   - Tactical managers already expose living units, faction ids, positions, targets, alarm incidents, and destination orders.
   - Core ownership changes provide `QuarterOwnerChanged`, although current AI does not subscribe to it.
9. **Current limitations/placeholders**
   - No production code was found calling Core `SetOwner`, `Contest`, or `ClearContest` as an AI decision.
   - No AI assesses holdings, block control, business protection, fear, collection schedules, or retaliation at a strategic level.
   - `MonkeyRunner`/`MonkeyOutfit` are audit/test automation, not production strategic AI.
10. **Relevant file paths**
    - `Assets/Scripts/Entities/PedestrianAgent.cs`
    - `Assets/Scripts/Entities/PedestrianInteractionDirector.cs`
    - `Assets/Scripts/Entities/GangMemberAgent.cs`
    - `Assets/RoadDemo/DemoCrews.cs`
    - `Assets/RoadDemo/DemoCrews.Combat.cs`
    - `Assets/RoadDemo/CrewWalker.cs`
    - `Assets/RoadDemo/CivilianAgent.cs`
    - `Assets/RoadDemo/DriverNerve.cs`
    - `Assets/RoadDemo/PoliceDispatch.cs`
    - `Assets/RoadDemo/ResidentialBlockLife.cs`

## Event and message architecture

**Overall status: PARTIAL — concrete local events and version stamps exist; a general domain event/message architecture was NOT FOUND.**

1. **What currently exists**
   - The project uses direct C# events/delegates, direct method calls, static callbacks, Unity lifecycle methods, and version polling.
   - Static registries are cleared through `RuntimeInitializeOnLoadMethod` subsystem-registration hooks.
2. **Important classes/components**
   - `StreetAlarm`, `CrimeFeed`, `WantedSystem`, `CityTerritoryRegistry`, `ResidentialBlockRecipe`, `ResidentialBlockModel`, `OverlayRegistry`, `PropertyRegistry`, `GangRegistry`, `PersonnelDirector`, `OutfitDirector`, `CrewWalker`, `HumanBehavior`, `CarBehavior`.
3. **Important interfaces**
   - There is no general `IEventBus`, message broker, command bus, or domain-event interface.
   - Narrow callback interfaces include `IMapTargetingConsumer`; other notifications are C# delegates/events.
4. **Important data models**
   - Street incidents are represented by `StreetAlarm` shot/death records.
   - Crime/wanted events use their own report/value types.
   - Registry changes are represented mainly by integer versions/stamps rather than typed change records.
5. **How objects reference each other**
   - Publishers expose static or instance events; subscribers bind directly.
   - UI and synchronization systems retain last-seen version values and rescan state when versions change.
   - Many runtime systems use concrete scene references and singleton lookups instead of messages.
6. **Authoritative state**
   - Events do not own state; their source director/registry/component remains authoritative.
   - Version numbers indicate that something changed but usually do not identify what changed.
7. **Important events/callbacks**
   - `StreetAlarm.OnShot`, `StreetAlarm.OnDeath`.
   - `CrimeFeed.Reported`.
   - Wanted-level/crime-reported callbacks in `WantedSystem`.
   - `HumanBehavior.routeCompleted`, `CarBehavior.routeCompleted`.
   - `PedestrianDeath.died`, `PlayerMafioso.Died`.
   - `ResidentialBlockRecipe.Changed`, `ResidentialBlockModel.Changed`.
   - `CityTerritoryRegistry.QuarterOwnerChanged`.
   - `CrewWalker.Fired`.
   - `TurfMapHud.ClaimRule` and `BuildingCardPicker.ClickVeto` delegates.
8. **Existing territory-relevant extension points**
   - Core owner changes, block-model changes, tactical incident events, campaign/day polling, and existing version stamps are concrete current observation points.
9. **Current limitations/placeholders**
   - No typed business-control, protection-paid, intimidation, fear, block-control, territory-conflict, or presence-change event exists.
   - Core conflict/capture mutations do not have a complete event family comparable to owner changes.
   - Direct `BusinessMarker.GangId` changes do not notify `PropertyRegistry`.
   - `CrimeFeed` belongs to the parked LivingCity crime path; current comments note no installed subscribers in that bootstrap.
10. **Relevant file paths**
    - `Assets/RoadDemo/StreetAlarm.cs`
    - `Assets/RoadDemo/CoreTerritory.cs`
    - `Assets/RoadDemo/ResidentialBlockModel.cs`
    - `Assets/Scripts/Gameplay/CrimeFeed.cs`
    - `Assets/Scripts/Gameplay/WantedSystem.cs`
    - `Assets/Scripts/Gameplay/PropertyRegistry.cs`
    - `Assets/Scripts/Gangs/GangRegistry.cs`
    - `Assets/Scripts/UI/OverlaySubject.cs`
    - `Assets/Scripts/Gameplay/PersonnelDirector.cs`
    - `Assets/Scripts/Gameplay/OutfitDirector.cs`

## Save and load

**Overall status: NOT FOUND for production game-state persistence.**

1. **What currently exists**
   - Multiple pure data objects use ids, seeds, day numbers, revisions, and simple fields that are compatible with future serialization.
   - Core territory plan comments describe save/load-safe logical data.
   - Some performance/editor tooling writes files, but it is not game save/load.
2. **Important classes/components**
   - No save repository, serializer, save-game controller, load bootstrap, slot manager, migration system, or autosave component was found.
3. **Important interfaces**
   - No `ISaveable`, save-store, serializer, or persistence interface was found.
4. **Important data models**
   - Potentially durable pure models include `Campaign`, `Roster`, `Job`, `Character`, `Crew`, `CoreTerritoryPlan`, `CityQuarterState`, and `ResidentialBlockRecipe`.
   - They are not assembled into a save schema.
5. **How objects reference each other**
   - Current runtime state is reconstructed from scene objects, ScriptableObjects, Resources, static catalogues, procedural seeds, and bootstrap seeding.
6. **Authoritative state**
   - Authority exists only in memory for the active session.
   - Static registries reset at subsystem registration; directors seed/recreate state when entering a runtime.
7. **Important events/callbacks**
   - No save-requested, before-save, after-load, migration, checkpoint, or dirty-state event was found.
8. **Existing territory-relevant extension points**
   - Existing stable ids and pure data boundaries are present, but there is no current persistence execution path to reuse.
9. **Current limitations/placeholders**
   - Accounts, roster changes, jobs, business affiliation/protection, Core owner/conflict state, tactical outcomes, and time do not persist through a new session via production code.
   - “Save/load-safe” in comments is a data-shape claim, not an implemented save/load feature.
10. **Relevant file paths**
    - `Assets/Scripts/Outfit/Campaign.cs`
    - `Assets/Scripts/Personnel/Roster.cs`
    - `Assets/RoadDemo/CoreTerritory.cs`
    - `Assets/RoadDemo/ResidentialBlockModel.cs`
    - `Assets/Scripts/Gameplay/GameplayBootstrap.cs`
    - `Assets/RoadDemo/RoadDemoLedger.cs`

## Game time, day/night, and simulation ticks

**Overall status: IMPLEMENTED for clocks and visual cycles; PARTIAL as one simulation scheduler.**

1. **What currently exists**
   - `IDayClock` exposes current day and hour through static `DayClock.Current` registration.
   - LivingCity `CityClock` and RoadDemo `DemoClock` implement the interface and own their day/hour, speed, pause, and `Time.timeScale` behavior.
   - `OutfitDirector` polls the current day clock and advances campaign hours/days.
   - Both stacks have their own sky/lighting/window/lamp/headlight/HUD consumers.
   - RoadDemo managers run their own `Update`/tick loops; construction districts receive `IDistrict.Tick`.
2. **Important classes/components**
   - `IDayClock`, `DayClock`, `CityClock`, `DemoClock`, `OutfitDirector`, `CityWeather`, `NightWindows`, `StreetLampLights`, `DemoSky`, `DemoGrade`, `DemoStreetLamps`, `DemoNightWindows`, `DemoHeadlights`, `DemoClockHud`, `RoadDemoBuilder`, `DemoCrews`, `PoliceDispatch`, `TickTimer`.
3. **Important interfaces**
   - `IDayClock`, `IDistrict.Tick` through `IDistrict`.
   - No general `ISimulationTick`, scheduler, or calendar-event interface exists.
4. **Important data models**
   - Day/hour are scalar clock state.
   - Campaign time tracks 1987-based day/hour progression separately but is advanced from the active day clock.
5. **How objects reference each other**
   - `DayClock.Current` supplies the active clock to shared consumers.
   - Several LivingCity visual/routine systems still reference concrete `CityClock`, while RoadDemo visuals reference `DemoClock`.
   - `RoadDemoBuilder.Update` ticks traffic, police cars, civilians, crowds, foot police, and districts; other managers also have their own updates.
6. **Authoritative state**
   - The active `CityClock` or `DemoClock` is authoritative for runtime day/hour and time scale.
   - `CampaignRunner` is authoritative for campaign calendar/accounting state after `OutfitDirector` advances it.
7. **Important events/callbacks**
   - Clocks are polled; no hour-changed/day-changed C# event is exposed by `IDayClock`.
   - `OutfitDirector` detects transitions and calls campaign hourly/daily processing.
8. **Existing territory-relevant extension points**
   - The existing clock interface, Outfit hourly/day processing, RoadDemo manager updates, and district tick callback are available current timing seams.
9. **Current limitations/placeholders**
   - There is no single simulation scheduler or deterministic ordered tick shared by campaign, AI, traffic, districts, and UI.
   - `TickTimer` records/profiles tick cost; it is not a scheduling service.
   - Concrete `CityClock` dependencies prevent all LivingCity time consumers from automatically working with `DemoClock`.
10. **Relevant file paths**
    - `Assets/Scripts/Ambient/IDayClock.cs`
    - `Assets/Scripts/Ambient/CityClock.cs`
    - `Assets/Scripts/Ambient/CityWeather.cs`
    - `Assets/Scripts/Ambient/NightWindows.cs`
    - `Assets/Scripts/Ambient/StreetLampLights.cs`
    - `Assets/RoadDemo/DemoClock.cs`
    - `Assets/RoadDemo/DemoClockHud.cs`
    - `Assets/RoadDemo/DemoSky.cs`
    - `Assets/RoadDemo/DemoStreetLamps.cs`
    - `Assets/RoadDemo/DemoNightWindows.cs`
    - `Assets/RoadDemo/DemoHeadlights.cs`
    - `Assets/RoadDemo/RoadDemoBuilder.cs`
    - `Assets/RoadDemo/TickTimer.cs`

## Combat, violence, police response, and fear-adjacent state

**Overall status: IMPLEMENTED for RoadDemo tactical violence; PARTIAL/parked for LivingCity crime response; NOT FOUND for persistent territory fear.**

1. **What currently exists**
   - RoadDemo supports armed crew combat, targeting, suppression/panic/retreat, deaths, surrender/arrest, bombing/explosions, drive-bys, vehicle run-downs, front damage, and police response.
   - `StreetAlarm` records/reports shots and deaths and provides recent-danger/hearing/shooter queries.
   - Civilians, residential life, drivers, and police respond to street incidents.
   - LivingCity has weapon, NPC health/death, crime feed, witnesses, wanted levels, police response, and player-death code, but `GameplayBootstrap` parks the playable path that would install most of it.
2. **Important classes/components**
   - RoadDemo: `DemoCrews`, `CrewWalker`, `StreetAlarm`, `PoliceDispatch`, `CivilianAgent`, `DriverNerve`, `ResidentialBlockLife`, `Explosion`, `ShopDamage`.
   - LivingCity: `WeaponController`, `NpcHealth`, `PedestrianDeath`, `CrimeFeed`, `NpcWitness`, `WitnessSystem`, `WantedSystem`, `PoliceResponseDirector`, `PlayerMafioso`.
3. **Important interfaces**
   - `IPoliceUnit` is the current police-unit contract.
   - There is no generic violence-event or fear-event interface.
4. **Important data models**
   - RoadDemo incidents contain shot/death position, time, shooter/victim and gang context needed by subscribers.
   - `CivilianAgent` has transient per-agent `Fear`; walkers/drivers/residential life have panic, shaken, or nerve state.
   - Campaign order outcomes separately store abstract violence results and heat.
5. **How objects reference each other**
   - Weapon fire/deaths report to `StreetAlarm`; police dispatch and civilians subscribe/query it.
   - `DemoCrews` writes player crew deaths/desertions back into `PersonnelDirector` after the physical outcome.
   - `ShopDamage` locates and mutates a `GangFront` rather than a LivingCity business.
6. **Authoritative state**
   - `DemoCrews.Unit`/`CrewWalker` own current tactical crew health/combat state.
   - `StreetAlarm` is the transient street-incident authority.
   - `PoliceDispatch` owns RoadDemo response/heat state; this is separate from `CampaignRunner.Heat`.
   - Per-agent components own their current fear/panic/nerve values.
7. **Important events/callbacks**
   - `StreetAlarm.OnShot`, `StreetAlarm.OnDeath`, `CrewWalker.Fired`, `PedestrianDeath.died`, `CrimeFeed.Reported`, wanted-level/crime-reported callbacks, and `PlayerMafioso.Died`.
8. **Existing territory-relevant extension points**
   - `StreetAlarm` already supplies localized violent incidents with gang/actor context.
   - Current civilian fear/panic and front damage are observable physical consequences.
   - Campaign orders already record violence outcomes and abstract heat.
9. **Current limitations/placeholders**
   - No durable block fear, business fear, outfit reputation/fear, witness-memory, intimidation-result, or fear-decay model exists.
   - `CivilianAgent.Fear` and related panic values are local transient tactical behavior, not territory state.
   - RoadDemo police heat and campaign order heat are separate and unsynchronized.
   - `ShopDamage` does not affect ownership, control, protection, income, or a persistent fear score.
   - LivingCity crime/wanted code must not be treated as active in the current `GameplayBootstrap` path.
10. **Relevant file paths**
    - `Assets/RoadDemo/DemoCrews.Combat.cs`
    - `Assets/RoadDemo/DemoCrews.Bomb.cs`
    - `Assets/RoadDemo/DemoCrews.DriveBy.cs`
    - `Assets/RoadDemo/DemoCrews.RunDown.cs`
    - `Assets/RoadDemo/CrewWalker.cs`
    - `Assets/RoadDemo/StreetAlarm.cs`
    - `Assets/RoadDemo/PoliceDispatch.cs`
    - `Assets/RoadDemo/CivilianAgent.cs`
    - `Assets/RoadDemo/DriverNerve.cs`
    - `Assets/RoadDemo/ResidentialBlockLife.cs`
    - `Assets/RoadDemo/Explosion.cs`
    - `Assets/RoadDemo/ShopDamage.cs`
    - `Assets/Scripts/Gameplay/WeaponController.cs`
    - `Assets/Scripts/Gameplay/CrimeFeed.cs`
    - `Assets/Scripts/Gameplay/WitnessSystem.cs`
    - `Assets/Scripts/Gameplay/WantedSystem.cs`
    - `Assets/Scripts/Gameplay/PoliceResponseDirector.cs`

## ScriptableObjects and configuration data

**Overall status: IMPLEMENTED for city, presentation, audio, and parked combat/player configuration; territory configuration is code/model based rather than a dedicated asset.**

1. **What currently exists**
   - Project-owned ScriptableObjects configure the generated city, prefabs, industrial lots, parks, performance, sound, combat/player/wanted/police values, weapons, RoadDemo city view, ledger models, and residential turf catalogue.
   - Corresponding assets exist under `Assets/Configs`, including Resources-based gameplay assets.
2. **Important classes/components**
   - `CityConfig`, `PrefabDatabase`, `IndustrialLotConfig`, `ParkConfig`, `PerformanceConfig`, `SoundDatabase`, `CombatConfig`, `PlayerConfig`, `WantedConfig`, `PoliceConfig`, `WeaponCatalog`, `CityViewConfig`, `LedgerModelSet`, `ResidentialTurfCatalog`.
3. **Important interfaces**
   - No configuration-provider interface or territory-configuration interface was found.
4. **Important data models**
   - City/content tuning resides in ScriptableObject fields and referenced prefab/audio/model assets.
   - `OrderTable`, `GangCatalog`, and Core territory definitions are code tables/pure runtime models, not ScriptableObjects.
5. **How objects reference each other**
   - `GameplayRuntime` exposes Resources-loaded combat/player/wanted/police configuration.
   - `WeaponCatalog` is loaded separately.
   - RoadDemo resolves `CityViewConfig` and `ResidentialTurfCatalog` through its asset-loading helpers/current builder path.
6. **Authoritative state**
   - ScriptableObjects are authoritative for their static configuration values, not mutable session state.
   - Code tables are authoritative for order specs, gang catalogue entries, and Core plan definitions.
7. **Important events/callbacks**
   - No general live configuration-changed event was found.
   - Residential recipe/model changes are runtime data events, separate from the catalogue asset.
8. **Existing territory-relevant extension points**
   - Existing city/prefab/view catalogues expose the content and geometry inputs used by current city and map construction.
   - Existing order specs contain territory-adjacent tuning such as duration, risk, heat, costs, and target modes.
9. **Current limitations/placeholders**
   - No `TerritoryConfig`, fear tuning asset, protection tuning asset, control-rule asset, or territory presentation asset was found.
   - `ResidentialTurfCatalog` is a lightweight visual/content index, not ownership/control state.
   - Presence of Resources combat/player assets does not make the parked LivingCity playable/crime bootstrap active.
10. **Relevant file paths**
    - `Assets/Scripts/Data/CityConfig.cs`
    - `Assets/Scripts/Data/PrefabDatabase.cs`
    - `Assets/Scripts/Data/IndustrialLotConfig.cs`
    - `Assets/Scripts/Data/ParkConfig.cs`
    - `Assets/Scripts/Data/PerformanceConfig.cs`
    - `Assets/Scripts/Data/SoundDatabase.cs`
    - `Assets/Scripts/Gameplay/CombatConfig.cs`
    - `Assets/Scripts/Gameplay/PlayerConfig.cs`
    - `Assets/Scripts/Gameplay/WantedConfig.cs`
    - `Assets/Scripts/Gameplay/PoliceConfig.cs`
    - `Assets/Scripts/Gameplay/WeaponCatalog.cs`
    - `Assets/RoadDemo/CityViewConfig.cs`
    - `Assets/RoadDemo/ResidentialTurfCatalog.cs`
    - `Assets/Scripts/UI/LedgerModelSet.cs`
    - `Assets/Configs/CityConfig.asset`
    - `Assets/Configs/CityViewConfig.asset`
    - `Assets/Configs/Gameplay/Resources/CombatConfig.asset`
    - `Assets/Configs/Gameplay/Resources/PlayerConfig.asset`
    - `Assets/Configs/Gameplay/Resources/PoliceConfig.asset`
    - `Assets/Configs/Gameplay/Resources/WantedConfig.asset`
    - `Assets/Configs/Gameplay/Resources/WeaponCatalog.asset`
    - `Assets/Configs/ResidentialTurfCatalog.asset`
    - `Assets/Configs/UI/Resources/LedgerModelSet.asset`

## Authoritative-state index

This table identifies the current source of truth; map/UI projections are intentionally not promoted to authority.

| Domain | Current authority | Scope and caveat |
|---|---|---|
| Outfit campaign, cash, jobs, records, relations, tribute, campaign heat | `CampaignRunner` owned by `OutfitDirector` | Shared into both stacks when the director is installed. |
| Personnel identity/rank/status/equipment/crew membership | `Roster` mutated through `RosterOps`/`PersonnelDirector` | Player outfit personnel; rival RoadDemo gangs are not full rosters. |
| Physical RoadDemo crew state | `DemoCrews.Unit` and its `CrewWalker`s/vehicle | Position, selection, goal, combat, car, arrest, retreat. |
| Installed gangs and front lookup | `GangRegistry` | Front business and front dossier are separate mappings. |
| LivingCity premise affiliation | `BusinessMarker.GangId` | Direct writes have no dedicated change event/version bump. |
| LivingCity property owner/protected flag/income | Each `BusinessMarker` | Registry indexes markers but does not own mutable field state. |
| LivingCity runtime blocks | `CityBlocks` | Reconstructed from generated ground slab names/bounds. |
| Core logical geography | `CoreTerritoryPlan` | Immutable quarter/block definitions. |
| Core mutable quarter control/conflict | `CityTerritoryRegistry` | API exists; production gameplay currently does not drive it. |
| Streamed residential block plans | `ResidentialBlockModel` and `ResidentialBlockRecipe` | Recycled GameObjects are views, not durable truth. |
| LivingCity block-control result | `Turf.DominantIn` calculation over current holdings | Derived on demand; no stored block owner. |
| Non-Core TurfMap district-control result | `TurfMapSurvey`/`TurfMapModel` projection | Derived majority/tie result; not durable state. |
| LivingCity civilian navigation | Per-agent `PathFinding`/`HumanBehavior` | Scene graph based. |
| RoadDemo crew navigation | `CrewWalker` plus `WalkRoute`/`WalkObstacles` | Lattice A* plus steering. |
| RoadDemo vehicle navigation | `RoadCar` plus `LaneNet` | Separate from LivingCity traffic. |
| Active day/hour | `CityClock` or `DemoClock` through `DayClock.Current` | Several consumers still use concrete clock types. |
| RoadDemo tactical incidents | `StreetAlarm` | Transient shots/deaths; not a campaign or territory history. |
| Persistent game state | **NOT FOUND** | No production save/load authority exists. |

## TERRITORY SYSTEM INTEGRATION MAP

This section maps the requested territory concerns to current implementation only. “Reusable current pieces” lists existing seams/data; it does not prescribe a redesign.

### Districts — PARTIAL

- **Current pieces:** Core’s six `CoreQuarterDefinition`s, neighbor links, bounds, and `CityTerritoryRegistry`; RoadDemo construction `DistrictSlot`/`IDistrict` objects.
- **Current authority:** `CoreTerritoryPlan` for Core geography and `CityTerritoryRegistry` for Core mutable owner/conflict state.
- **Boundary:** RoadDemo construction districts (`Pad`, `Suburb`, `Harbor`, `Airport`) describe build/tick regions, not gang territory. LivingCity has no neighborhood/district aggregation.
- **Paths:** `Assets/RoadDemo/CoreTerritory.cs`, `Assets/RoadDemo/CoreDistrict.cs`, `Assets/RoadDemo/District.cs`, `Assets/RoadDemo/CityLayout.cs`.

### Blocks — IMPLEMENTED, stack-specific

- **Current pieces:** LivingCity `CityBlocks.BlockInfo`; Core `CoreBlockDefinition`; streamed `ResidentialBlockRecipe.BlockId`/`QuarterId`.
- **Current authority:** `CityBlocks` for LivingCity runtime lookup; `CoreTerritoryPlan` and `ResidentialBlockModel` for Core/streamed logical blocks.
- **Boundary:** IDs and geometry are not represented by one common type or registry across stacks.
- **Paths:** `Assets/Scripts/Gameplay/CityBlocks.cs`, `Assets/RoadDemo/CoreTerritory.cs`, `Assets/RoadDemo/ResidentialBlockModel.cs`.

### Detecting which block an actor is in — IMPLEMENTED with limits

- **Current pieces:** `CityBlocks.At(Vector2)`/`Nearest` for LivingCity; `CityTerritoryRegistry.BlockAt(Vector3)` and `QuarterAt(Vector3)` for Core.
- **Current inputs:** All current actors and vehicles expose world positions through their transforms/models.
- **Boundary:** LivingCity `At` can return no block on roads; Core and LivingCity lookup APIs are unrelated; no actor caches or publishes a current block id.
- **Paths:** `Assets/Scripts/Gameplay/CityBlocks.cs`, `Assets/RoadDemo/CoreTerritory.cs`, `Assets/RoadDemo/DemoCrews.cs`, `Assets/RoadDemo/CrewWalker.cs`.

### Outfit Presence — PARTIAL

- **Current pieces:** `DemoCrews.Units` exposes gang id, living members, position, target, car, and post; TurfMap already projects those units. LivingCity has gang-front `GangMemberAgent`s and player/rival identities. `PropertyRegistry` exposes held premises.
- **Current authority:** Physical presence is spread across `DemoCrews.Unit`/walkers and LivingCity scene agents; premise presence is `BusinessMarker.GangId`.
- **Missing:** No persistent block-presence record, aggregation rule, contributor interface, decay, occupancy event, or presence history.
- **Paths:** `Assets/RoadDemo/DemoCrews.cs`, `Assets/RoadDemo/TurfMapModel.cs`, `Assets/Scripts/Entities/GangMemberAgent.cs`, `Assets/Scripts/Gameplay/PropertyRegistry.cs`.

### Fear events — PARTIAL for tactical inputs; persistent fear NOT FOUND

- **Current pieces:** `StreetAlarm.OnShot`/`OnDeath`, localized incident queries, `CivilianAgent.Fear`, crew panic/shaken state, `DriverNerve`, and `ResidentialBlockLife` panic responses.
- **Current authority:** `StreetAlarm` for transient incidents; each agent/component for its transient reaction.
- **Missing:** No block/business/outfit fear value, fear event abstraction, durable accumulation/decay, or fear-to-control/payment link.
- **Paths:** `Assets/RoadDemo/StreetAlarm.cs`, `Assets/RoadDemo/CivilianAgent.cs`, `Assets/RoadDemo/DriverNerve.cs`, `Assets/RoadDemo/ResidentialBlockLife.cs`.

### Businesses paying protection — STUB

- **Current pieces:** `BusinessMarker.WeeklyIncome`, `Owner`, `Protected`, `GangId`; `OrderType.Extort`, `Intimidate`, `CollectProtection`, and `AdjustProtection`; campaign accounts, records, daily ticks, and abstract payouts.
- **Current authority:** Business fields reside on `BusinessMarker`; money/order bookkeeping resides in `CampaignRunner`.
- **Missing:** No protected-by relationship, rate, debt, due date, payer/collector, refusal, recurring transfer, business-specific job link, or production writer for `Protected`.
- **Important distinction:** `Tribute` is family-house accounting and is not shop protection.
- **Paths:** `Assets/Scripts/Entities/BusinessMarker.cs`, `Assets/Scripts/Outfit/Orders.cs`, `Assets/Scripts/Outfit/CampaignRunner.cs`, `Assets/Scripts/Outfit/Tribute.cs`.

### Physical business intimidation — PARTIAL infrastructure; action STUB

- **Current pieces:** campaign jobs carry block/world targets; `CrewJobs` moves a crew to target coordinates; `DemoCrews` supports local combat; `ShopEntrance`, `GangFront`, and Core front sites expose doors/approach locations; maps select buildings.
- **Current authority:** `OrderBook` for the abstract job and `DemoCrews.Unit` for physical execution state.
- **Missing:** No intimidate interaction/state machine, business target contract, proprietor actor, success callback tied to a business, fear mutation, or protection-state mutation.
- **Current placeholder:** TurfMap TAKE IT waits for arrival/no living enemy and directly assigns `BusinessMarker.GangId`.
- **Paths:** `Assets/RoadDemo/CrewJobs.cs`, `Assets/RoadDemo/DemoCrews.cs`, `Assets/RoadDemo/TurfMapHud.cs`, `Assets/Scripts/Entities/BusinessMarker.cs`, `Assets/Scripts/Entities/ShopEntrance.cs`.

### Derived block control — IMPLEMENTED in LivingCity; PARTIAL across the project

- **Current pieces:** `Turf.DominantIn` derives LivingCity block control from held premises; non-Core TurfMap derives district owner from held building counts; Core stores direct quarter owner/conflict.
- **Current authority:** The derivations own no state. Inputs are `BusinessMarker.GangId`; direct Core control is `CityTerritoryRegistry`.
- **Boundary:** Tie semantics exist, but block versus district versus quarter results are not unified or reconciled.
- **Paths:** `Assets/Scripts/Outfit/Territory.cs`, `Assets/Scripts/Gameplay/OutfitDirector.cs`, `Assets/RoadDemo/TurfMapSurvey.cs`, `Assets/RoadDemo/CoreTerritory.cs`.

### Territory map visualization — IMPLEMENTED with separate inputs

- **Current pieces:** `StrategicMapHud` premise washes and block cards; `TurfMapHud`/`TurfMapBuildingLayer`/`TurfMapPanel`/`TurfMinimap` district, Core owner/conflict, building, crew, and traffic rendering.
- **Current authority:** Maps are projections. StrategicMap reads holdings; TurfMap reads Core states and optional business affiliations.
- **Limit:** StrategicMap does not fill whole controlled block polygons. TurfMap claim behavior only affects businesses and does not update Core quarter state.
- **Paths:** `Assets/Scripts/UI/StrategicMapHud.cs`, `Assets/RoadDemo/TurfMapHud.cs`, `Assets/RoadDemo/TurfMapSurvey.cs`, `Assets/RoadDemo/TurfMapBuildingLayer.cs`, `Assets/RoadDemo/TurfMinimap.cs`.

### World-space feedback — IMPLEMENTED generic/tactical surfaces; territory-specific feedback NOT FOUND

- **Current pieces:** `IOverlaySubject`/`OverlayRegistry`/`CityOverlayHud`; `CrewOverlay`; movement/order markers; `CombatIntentOverlay`; `FrontOverlay`; `DemoLotOverlay`; map-selected highlights.
- **Current authority:** These are views over business, actor, combat, and selection state.
- **Missing:** No block boundary, block owner, contest progress, presence, fear, protection, or control-change world feedback component.
- **Paths:** `Assets/Scripts/UI/OverlaySubject.cs`, `Assets/Scripts/UI/CityOverlayHud.cs`, `Assets/RoadDemo/CrewOverlay.cs`, `Assets/RoadDemo/CombatIntentOverlay.cs`, `Assets/RoadDemo/FrontOverlay.cs`.

### Notifications — PARTIAL

- **Current pieces:** `CrewOverlay.Announce` supplies a timed RoadDemo banner; Orders UI has local status notes; crime/incident systems have events but no shared player notification route.
- **Current authority:** Notification presentation is local to each UI.
- **Missing:** No shared notification service, queue, severity/category model, history, or territory event-to-notification binding.
- **Paths:** `Assets/RoadDemo/CrewOverlay.cs`, `Assets/Scripts/UI/PersonnelAlmanac.Orders.cs`, `Assets/Scripts/Gameplay/CrimeFeed.cs`.

### Simulation ticks — IMPLEMENTED hooks; unified scheduler NOT FOUND

- **Current pieces:** `IDayClock`; `OutfitDirector` hourly/day transition handling; `CampaignRunner.DayTick`; `RoadDemoBuilder.Update`; `DemoCrews.Update`; `PoliceDispatch.Update`; `IDistrict.Tick`.
- **Current authority:** Each owning manager controls its own cadence; the active clock controls day/hour.
- **Boundary:** There is no common deterministic simulation-tick interface or ordered scheduler. `TickTimer` is profiling only.
- **Paths:** `Assets/Scripts/Ambient/IDayClock.cs`, `Assets/Scripts/Gameplay/OutfitDirector.cs`, `Assets/Scripts/Outfit/CampaignRunner.cs`, `Assets/RoadDemo/RoadDemoBuilder.cs`, `Assets/RoadDemo/District.cs`, `Assets/RoadDemo/TickTimer.cs`.

### Save/load — NOT FOUND

- **Current pieces:** Stable ids, pure campaign/personnel/order/Core models, seeds, block recipe revisions, and subsystem reset hooks.
- **Current authority:** All mutable game state is session-memory state.
- **Missing:** Save schema, serializer, storage, slots, load/bootstrap restoration, versioning/migration, dirty tracking, and persistence callbacks.
- **Consequence for current territory-adjacent state:** `BusinessMarker.GangId`/`Protected`, Core owner/conflict, campaign changes, and derived control are not restored by a production save system.
- **Paths:** `Assets/Scripts/Outfit/Campaign.cs`, `Assets/Scripts/Personnel/Roster.cs`, `Assets/RoadDemo/CoreTerritory.cs`, `Assets/RoadDemo/ResidentialBlockModel.cs`.

## Current gaps that another architect must treat as facts

- **PARTIAL:** Shared outfit/personnel systems exist in both runtime families, but the city, business, actor, navigation, map, and territory representations remain stack-specific.
- **PARTIAL:** Premise-level affiliation and derived block/district control exist, while Core has a separate direct quarter-control state.
- **STUB:** Protection and intimidation names, fields, jobs, UI action, and abstract money outcomes exist without a business-specific protection lifecycle.
- **STUB:** TurfMap TAKE IT directly changes a business gang id after a simplified physical condition; it is not a territory simulation.
- **PARTIAL:** Tactical violence and transient fear/panic provide localized inputs, but no durable fear domain exists.
- **NOT FOUND:** Strategic territory AI.
- **NOT FOUND:** Unified territory/presence/control event model.
- **NOT FOUND:** Production save/load.
- **NOT FOUND:** A project-wide district/block/actor-location abstraction.

These are implementation boundaries observed in the current source, not recommendations for how the upcoming territory system should be designed.
