using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Builds the park a ParkLayout.Plan describes: paints the walks, opens the hedge at the
    /// planned entrances, and spawns every station. The thinking all lives in ParkLayout - this
    /// class is hands, not head, the same split IndustrialLayout/IndustrialDresser keep and for
    /// the same reason: the painter, the dresser and (in ParkNavBuilder) the pedestrian nav all
    /// read the SAME plan, so the paint, the lamps and the path nodes cannot drift apart.
    ///
    /// Takes NOTHING from BlockBuilder's shared Buildings rng. The old dresser drained it, which
    /// meant retuning a shrub count re-laid every block built after the park; the plan draws from
    /// its own SeedOffsets.Park stream, deterministic in (seed, blockId) alone. Landing that
    /// change reshuffled the post-park blocks once, on purpose.
    ///
    /// The ground under all of this is bare grass laid by GroundPlacer - tile-park is retired.
    /// Its baked walk cross was 4-fold symmetric by construction, so every park was the same
    /// cross however the dice fell; worse, its walk endpoints sat at cell-edge midpoints where
    /// an ordinary road tile has no path node at all, so the park only ever joined the pavement
    /// network where a crosswalk happened to be rolled. The painted walks follow the plan's
    /// spines instead, and the spines end on the road tiles' MEASURED sidewalk endpoints.
    /// </summary>
    public static class ParkDresser
    {
        /// <summary>Half a cell, world - the frame every constant in this file lives in.</summary>
        const float CellHalf = CityGrid.CellSize * 0.5f;

        /// <summary>
        /// How far inside the cell boundary the hedge stands on a map-edge side, where there is
        /// no road to take a block line from. World, matching HedgeLayoutTests' MapEdge.
        /// </summary>
        const float HedgeInset = 0.8f;

        /// <summary>
        /// How far past the hedge line a painted walk may run - to the kerb, so an entrance
        /// reads as a walk meeting the street rather than stopping dead at the hedge. The verge
        /// past the kerb belongs to the road tile's own raised pavement, which would hide paint
        /// anyway. clearance - PavementEdge on an ordinary street (11.96 - 7.8, rounded out -
        /// over-length hides under the raised pavement), near enough on the avenue.
        /// </summary>
        const float PaintStub = 4.2f;

        /// <summary>Water sits above the block slab and below the patch layer - see GroundPlacer's ladder.</summary>
        const float PondLift = 0.04f;

        /// <summary>
        /// Tree species wider than this, crown measured, stay out of the main planting pool:
        /// tree-lime at 7.07m fills a grove on its own. They come back as the accent specimen.
        /// </summary>
        const float SpecimenCrown = 6f;

        public static void Build(
            CityGrid grid,
            List<Vector2Int> cells,
            int blockId,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            CityConfig config,
            ParkConfig parkConfig,
            IReadOnlyDictionary<Vector2Int, GameObject> roadTilesByCell,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed,
            List<Bounds> gateKeepOuts)
        {
            if (cells == null || cells.Count == 0)
                return;

            var tuning = parkConfig ? parkConfig.Tuning : ParkLayout.Tuning.Default;
            var clearance = BlockBuilder.ClearanceFor(palette, config);
            var mainClearance = BlockBuilder.MainClearanceFor(palette, config);

            var kit = MeasureKit(palette, prefabs);

            // Anchors read from the real road tile instances - the only reader that knows which
            // straight was rolled as a crosswalk. The measured fallback covers tests and a
            // roads-off build; ForBlock falls back internally when the list comes back empty.
            var anchors = roadTilesByCell != null
                ? ParkNavBuilder.Anchors(grid, cells, roadTilesByCell, clearance, mainClearance)
                : null;

            var plan = ParkLayout.ForBlock(
                grid, cells, clearance, mainClearance, CellHalf - HedgeInset,
                anchors, kit.Pool.Count, config.seed, blockId, tuning, kit.Footprints);

            foreach (var warning in plan.Warnings)
                Debug.LogWarning($"[ParkDresser] Block {blockId}: {warning}");

            PaintWalks(plan, prefabs, palette, parent, spawn, placed, blockId);
            BuildHedge(grid, cells, plan, palette, clearance, mainClearance, tuning,
                parent, spawn, placed, gateKeepOuts);
            SpawnStations(plan, palette, prefabs, kit, parent, spawn, occupied, placed);

            // The pedestrian graph, after everything physical: it reads the plan, not the
            // scene, but its Relink probes physics and wants the colliders settled.
            var navWarnings = new List<string>();
            ParkNavBuilder.Build(plan, grid, cells, roadTilesByCell, parent, placed, navWarnings);
            foreach (var warning in navWarnings)
                Debug.LogWarning($"[ParkDresser] Block {blockId} nav: {warning}");

            Publish(plan, blockId, parent);
        }

        // ------------------------------------------------------------------ kit

        /// <summary>
        /// The measured vocabulary the plan spaces things by: the flattened species pool, the
        /// accent specimen, and a footprint table overwritten from PrefabBounds - so a prefab
        /// swap in the palette re-spaces the park instead of silently overlapping it.
        /// </summary>
        struct Kit
        {
            public List<GameObject> Pool;
            public GameObject Accent;
            public ParkLayout.Footprint[] Footprints;
        }

        static Kit MeasureKit(PrefabDatabase.ZonePalette palette, PrefabDatabase prefabs)
        {
            var kit = new Kit
            {
                Pool = new List<GameObject>(),
                Footprints = ParkLayout.DefaultFootprints(),
            };

            // Flatten the weighted buckets into distinct species; each pool entry IS a species,
            // which is what lets the plan's two-species cap mean what the spec says.
            var seen = new HashSet<GameObject>();
            var specimens = new List<GameObject>();
            if (palette.parkTrees != null)
                foreach (var bucket in palette.parkTrees)
                {
                    if (bucket?.prefabs == null)
                        continue;
                    foreach (var prefab in bucket.prefabs)
                    {
                        if (!prefab || !seen.Add(prefab))
                            continue;
                        var crown = PrefabBounds.FootprintXZ(prefab, 0f);
                        if (Mathf.Max(crown.x, crown.y) > SpecimenCrown)
                            specimens.Add(prefab);
                        else
                            kit.Pool.Add(prefab);
                    }
                }
            if (kit.Pool.Count == 0 && prefabs.trees is { Length: > 0 })
                foreach (var prefab in prefabs.trees)
                    if (prefab && seen.Add(prefab))
                        kit.Pool.Add(prefab);

            kit.Accent = specimens.Count > 0 ? specimens[0] : null;

            Measure(kit.Footprints, ParkLayout.StationKind.Tree, kit.Pool);
            if (kit.Accent)
                MeasureOne(kit.Footprints, ParkLayout.StationKind.AccentTree, kit.Accent);
            Measure(kit.Footprints, ParkLayout.StationKind.DeadTree, palette.parkDeadTrees);
            Measure(kit.Footprints, ParkLayout.StationKind.Shrub, palette.parkUndergrowth);
            Measure(kit.Footprints, ParkLayout.StationKind.Bench, palette.parkBenches);
            Measure(kit.Footprints, ParkLayout.StationKind.Bin, palette.parkBins);
            Measure(kit.Footprints, ParkLayout.StationKind.Boulder, palette.parkBoulders);
            Measure(kit.Footprints, ParkLayout.StationKind.PondRim, palette.parkBoulders);
            Measure(kit.Footprints, ParkLayout.StationKind.Centrepiece, palette.landmarks);
            Measure(kit.Footprints, ParkLayout.StationKind.Monument, palette.parkMonuments);
            Measure(kit.Footprints, ParkLayout.StationKind.Carousel, palette.parkAmusement);
            if (palette.parkGatePiers)
                MeasureOne(kit.Footprints, ParkLayout.StationKind.GatePost, palette.parkGatePiers);

            // The lamp's XZ bounds are its ARM SPAN, not its ground footprint - a 0.3m post
            // measures 7.2m wide on lamp-road-double. Keep the default ground radius and take
            // only the height, which is what the category cap reads.
            var lamps = Lamps(palette, prefabs);
            if (lamps is { Length: > 0 } && lamps[0])
            {
                var footprint = kit.Footprints[(int)ParkLayout.StationKind.Lamp];
                footprint.Height = PrefabBounds.Get(lamps[0]).size.y;
                kit.Footprints[(int)ParkLayout.StationKind.Lamp] = footprint;
            }

            return kit;
        }

        static void Measure(
            ParkLayout.Footprint[] footprints, ParkLayout.StationKind kind,
            IReadOnlyList<GameObject> pool)
        {
            if (pool == null || pool.Count == 0)
                return;
            var radius = 0f;
            var height = 0f;
            foreach (var prefab in pool)
            {
                if (!prefab)
                    continue;
                var xz = PrefabBounds.FootprintXZ(prefab, 0f);
                radius = Mathf.Max(radius, Mathf.Max(xz.x, xz.y) * 0.5f);
                height = Mathf.Max(height, PrefabBounds.Get(prefab).size.y);
            }
            if (radius <= 0f)
                return;
            var footprint = footprints[(int)kind];
            footprint.Radius = radius;
            footprint.Height = height;
            footprints[(int)kind] = footprint;
        }

        static void MeasureOne(
            ParkLayout.Footprint[] footprints, ParkLayout.StationKind kind, GameObject prefab) =>
            Measure(footprints, kind, new[] { prefab });

        static GameObject[] Lamps(PrefabDatabase.ZonePalette palette, PrefabDatabase prefabs) =>
            palette.parkLamps is { Length: > 0 } ? palette.parkLamps : prefabs.streetLamps;

        // ------------------------------------------------------------------ paint

        /// <summary>
        /// The walks, as one light-material mesh: every spine's sampled segments plus the plaza
        /// roundel. Segments are dropped once their midpoint runs PaintStub past the hedge -
        /// the anchor tails carry the NAV out to the road corners, but paint on the road tile's
        /// own pavement band would be paint on paving.
        /// </summary>
        static void PaintWalks(
            ParkLayout.Plan plan, PrefabDatabase prefabs, PrefabDatabase.ZonePalette palette,
            Transform parent, SpawnPrefab spawn, List<GameObject> placed, int blockId)
        {
            var keep = new ParkLayout.Rect(
                plan.Interior.Min - new Vector2(PaintStub, PaintStub),
                plan.Interior.Max + new Vector2(PaintStub, PaintStub));

            var strokes = new List<GroundPaint.Stroke>();
            foreach (var spine in plan.Spines)
            {
                if (spine.Points == null)
                    continue;
                for (var i = 1; i < spine.Points.Length; i++)
                {
                    var a = spine.Points[i - 1];
                    var b = spine.Points[i];
                    if (!keep.Contains((a + b) * 0.5f))
                        continue;
                    strokes.Add(new GroundPaint.Stroke(a, b, spine.Width));
                }
            }

            var paint = GroundPaint.Emit(strokes, prefabs.paintLightMaterial,
                GroundPaint.PaintLift, $"park_walks_{blockId}", parent);
            if (paint)
                placed.Add(paint);

            if (plan.PlazaRadius > 0f)
            {
                const int Segments = 28;
                var circle = new Vector2[Segments];
                for (var i = 0; i < Segments; i++)
                {
                    var angle = i * (Mathf.PI * 2f / Segments);
                    circle[i] = plan.PlazaCentre
                                + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle))
                                * plan.PlazaRadius;
                }
                var plaza = GroundPaint.EmitPolys(new List<Vector2[]> { circle },
                    prefabs.paintLightMaterial, GroundPaint.PaintLift,
                    $"park_plaza_{blockId}", parent);
                if (plaza)
                    placed.Add(plaza);
            }

            foreach (var zone in plan.Zones)
            {
                if (zone.Kind != ParkLayout.ZoneKind.Pond || !palette.parkWaterTile)
                    continue;
                var pond = GroundPlacer.LaySurface(palette.parkWaterTile,
                    zone.Area.Min, zone.Area.Max, GroundPlacer.BlockLift + PondLift,
                    $"park_pond_{blockId}", parent, spawn);
                if (pond)
                    placed.Add(pond);
            }
        }

        // ------------------------------------------------------------------ hedge

        static void BuildHedge(
            CityGrid grid, List<Vector2Int> cells, ParkLayout.Plan plan,
            PrefabDatabase.ZonePalette palette, float clearance, float mainClearance,
            ParkLayout.Tuning tuning, Transform parent, SpawnPrefab spawn,
            List<GameObject> placed, List<Bounds> gateKeepOuts)
        {
            var gates = new List<Vector2>(plan.Entrances.Count);
            foreach (var entrance in plan.Entrances)
                gates.Add(entrance.Gate);

            var runs = HedgeLayout.Plan(grid, cells, clearance, mainClearance,
                CellHalf - HedgeInset, gates, tuning.gateHalfWidth, GroundPlacer.BlockLift);
            foreach (var run in runs)
                FenceRun.Lay(palette.fenceSegment, run.Origin, run.Along, run.From, run.To,
                    parent, spawn, placed);

            // Publish each entrance's approach so StreetPropPlacer keeps its verge trees and
            // lamps out of the one hole the hedge has - the works gates' own arrangement.
            if (gateKeepOuts == null)
                return;
            foreach (var entrance in plan.Entrances)
            {
                var leg = SideLeg(entrance.Side);
                gateKeepOuts.Add(PerimeterFence.Approach(new PerimeterFence.Gate
                {
                    Has = true,
                    Centre = new Vector3(entrance.Gate.x, 0f, entrance.Gate.y),
                    Outward = new Vector3(leg.x, 0f, leg.y),
                    Width = tuning.gateHalfWidth * 2f,
                }));
            }
        }

        static Vector2Int SideLeg(int side) => side switch
        {
            0 => new Vector2Int(1, 0),
            1 => new Vector2Int(0, 1),
            2 => new Vector2Int(-1, 0),
            _ => new Vector2Int(0, -1),
        };

        // ------------------------------------------------------------------ stations

        static void SpawnStations(
            ParkLayout.Plan plan, PrefabDatabase.ZonePalette palette, PrefabDatabase prefabs,
            Kit kit, Transform parent, SpawnPrefab spawn,
            List<Bounds> occupied, List<GameObject> placed)
        {
            // Per-kind counters, so a kind with several prefabs rotates through them
            // deterministically without spending a draw anywhere.
            var counters = new int[16];

            foreach (var station in plan.Stations)
            {
                var prefab = PrefabFor(plan, station, palette, prefabs, kit, counters);
                if (!prefab)
                    continue;

                var position = new Vector3(station.Pos.x, GroundPlacer.BlockLift, station.Pos.y);
                var instance = OverlapSpawn.Place(prefab, position, station.Yaw, station.Scale,
                    parent, spawn, occupied, placed);
                if (!instance)
                    continue;

                // The ride should run. The rotator finds the prop's own rotate pivot and
                // no-ops on props without one, so it is safe across the whole pool.
                if (station.Kind == ParkLayout.StationKind.Carousel)
                    instance.AddComponent<City.FerrisWheelRotator>();

                // The knoll is a terrain dome that renders in the atlas's earth swatch - the
                // old park's "giant red boulder". Dressed in the lawn's own material it reads
                // as the grassy rise it is meant to be.
                if (station.Kind == ParkLayout.StationKind.Knoll && palette.ground)
                {
                    var lawn = palette.ground.GetComponentInChildren<MeshRenderer>();
                    if (lawn)
                        foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>())
                            renderer.sharedMaterial = lawn.sharedMaterial;
                }
            }
        }

        static GameObject PrefabFor(
            ParkLayout.Plan plan, ParkLayout.Station station,
            PrefabDatabase.ZonePalette palette, PrefabDatabase prefabs,
            Kit kit, int[] counters)
        {
            var index = counters[(int)station.Kind]++;
            switch (station.Kind)
            {
                case ParkLayout.StationKind.Tree:
                {
                    if (kit.Pool.Count == 0)
                        return null;
                    var species = station.SpeciesSlot == 0
                        ? plan.PrimarySpecies
                        : plan.SecondarySpecies;
                    return kit.Pool[Mathf.Clamp(species, 0, kit.Pool.Count - 1)];
                }
                case ParkLayout.StationKind.AccentTree:
                    return kit.Accent ? kit.Accent : Rotate(kit.Pool, index);
                case ParkLayout.StationKind.DeadTree:
                    return Rotate(palette.parkDeadTrees, index);
                case ParkLayout.StationKind.Shrub:
                    return Rotate(palette.parkUndergrowth, index);
                case ParkLayout.StationKind.Flower:
                    return Flower(palette, index);
                case ParkLayout.StationKind.Lamp:
                    return Rotate(Lamps(palette, prefabs), index);
                case ParkLayout.StationKind.Bench:
                    return Rotate(palette.parkBenches, index);
                case ParkLayout.StationKind.Bin:
                    return Rotate(palette.parkBins, index);
                case ParkLayout.StationKind.Boulder:
                case ParkLayout.StationKind.PondRim:
                    return Rotate(palette.parkBoulders, index);
                case ParkLayout.StationKind.GatePost:
                    return palette.parkGatePiers;
                case ParkLayout.StationKind.Centrepiece:
                    return Rotate(palette.landmarks, index);
                case ParkLayout.StationKind.Monument:
                    return Rotate(palette.parkMonuments, index);
                case ParkLayout.StationKind.Knoll:
                    return Rotate(palette.parkMounds, index);
                case ParkLayout.StationKind.Carousel:
                    return Rotate(palette.parkAmusement, index);
                default:
                    return null;
            }
        }

        static GameObject Rotate(IReadOnlyList<GameObject> pool, int index)
        {
            if (pool == null || pool.Count == 0)
                return null;
            return pool[index % pool.Count];
        }

        /// <summary>
        /// The parterre wants flowers, and the palette keeps them mixed into parkUndergrowth
        /// with the shrubs and the grass tufts. Name-prefix selection is the house pattern
        /// (InteractionMarkers finds its benches the same way); the fallback is the whole list,
        /// which reads as a mixed bed rather than as nothing.
        /// </summary>
        static readonly string[] FlowerNames = { "SM_Env_Flowers" };

        static GameObject Flower(PrefabDatabase.ZonePalette palette, int index)
        {
            var flowers = new List<GameObject>();
            if (palette.parkUndergrowth != null)
                foreach (var prefab in palette.parkUndergrowth)
                {
                    if (!prefab)
                        continue;
                    foreach (var name in FlowerNames)
                        if (prefab.name.StartsWith(name, System.StringComparison.Ordinal))
                        {
                            flowers.Add(prefab);
                            break;
                        }
                }
            return flowers.Count > 0
                ? flowers[index % flowers.Count]
                : Rotate(palette.parkUndergrowth, index);
        }

        // ------------------------------------------------------------------ publish

        static void Publish(ParkLayout.Plan plan, int blockId, Transform parent)
        {
            var marker = new GameObject($"park_grounds_{blockId}");
            marker.transform.SetParent(parent, false);
            marker.transform.SetPositionAndRotation(
                new Vector3(plan.Interior.Centre.x, 0f, plan.Interior.Centre.y),
                Quaternion.identity);
            marker.AddComponent<Entities.ParkGrounds>().SetPlan(blockId, plan);
        }
    }
}
