using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Entities;

namespace LivingCity.Generation
{
    /// <summary>
    /// Dresses the inside of a works compound, on the zones IndustrialLotPlanner cut out of it.
    ///
    /// Runs after BlockBuilder rather than inside it, off the WorksYard markers IndustrialDresser
    /// left behind - the same generate-in-editor-and-save handoff PoliceStation uses. So it
    /// cannot see, and cannot disturb, BlockBuilder's shared Buildings stream: every draw here
    /// comes from SeedOffsets.IndustrialLot.
    ///
    /// This pass is the ground only. What it is fixing is a works yard that reads as one flat
    /// untextured plane, and the fix is three things rather than one:
    ///
    ///   1. SURFACE per zone - concrete under the loading aprons and the truck staging, packed
    ///      dirt in the bulk yards and the scrap corner, cinders round the boiler house. Each is
    ///      a different tile-plain_*-nb prefab, which on this pack means a different cell of the
    ///      shared atlas rather than a different material, so they all still batch.
    ///
    ///   2. FRINGE - jittered patches straddling every zone edge, at a slightly higher lift, so
    ///      concrete meets dirt on a worn irregular line instead of a drawn rectangle. This is
    ///      the cheapest thing here and it does more than the zones themselves.
    ///
    ///   3. WEATHERING - patches over the ground no zone claimed. Easy to leave out and the part
    ///      that matters most: zones cover about half the open ground on purpose, so treating
    ///      only them leaves the other half exactly as bare as it started.
    ///
    /// Then grime on top of all of it: tyre ruts running in from the gate, oil under the lorry
    /// stands, and on a big enough compound a painted stub-end rail spur.
    ///
    /// Every stroke and blob that shares a material is batched into ONE mesh by GroundPaint, and
    /// the surfaces are stretched instances of the pack's own tiles, so a fully dressed yard adds
    /// a few dozen renderers that all fall into existing batches.
    /// </summary>
    public static class IndustrialLotBuilder
    {
        /// <summary>Zone surfaces. Above GroundPlacer's patch layer (0.05), below the paint.</summary>
        const float ZoneLift = 0.07f;

        /// <summary>Fringe and weathering, above the zone cores so their ragged edges win.</summary>
        const float FringeLift = 0.085f;

        /// <summary>Spreads consecutive block ids apart in the seed space - see BlockLots.</summary>
        const int BlockStride = 397;

        public static List<GameObject> Build(
            Transform cityRoot, PrefabDatabase prefabs, CityConfig config,
            IndustrialLotConfig lot, Transform parent, SpawnPrefab spawn)
        {
            var placed = new List<GameObject>();
            if (!cityRoot || !prefabs || !config)
                return placed;

            spawn ??= RoadNetworkBuilder.RuntimeSpawn;

            // Scoped to this city root, not FindObjectsByType: a stale second city left in the
            // scene must not get dressed - see CityBuilder's own note on duplicate roots.
            var yards = cityRoot.GetComponentsInChildren<WorksYard>(true);
            if (yards.Length == 0)
                return placed;

            var tuning = lot ? lot.Zoning : IndustrialLotPlanner.Tuning.Default;

            foreach (var yard in yards)
                Dress(yard, prefabs, config, lot, tuning, parent, spawn, placed);

            return placed;
        }

        static void Dress(
            WorksYard yard, PrefabDatabase prefabs, CityConfig config, IndustrialLotConfig lot,
            IndustrialLotPlanner.Tuning tuning, Transform parent, SpawnPrefab spawn,
            List<GameObject> placed)
        {
            var palette = prefabs.PaletteFor(BlockZone.Industrial);
            if (palette == null)
                return;

            var rng = new System.Random(
                config.seed + SeedOffsets.IndustrialLot + yard.BlockId * BlockStride + 104729);

            var concrete = palette.yardConcrete ? palette.yardConcrete : prefabs.groundTile;
            var dirt = palette.yardDirt ? palette.yardDirt : prefabs.groundTile;

            var id = yard.BlockId;

            // --- 1 and 2: a surface per zone, with a broken edge -------------------------------
            var zones = yard.Zones;
            for (var i = 0; i < zones.Length; i++)
            {
                var zone = zones[i];
                var tile = SurfaceFor(zone.Kind, concrete, dirt);
                if (!tile)
                    continue;

                var core = GroundPlacer.LaySurface(tile, zone.Area.Min, zone.Area.Max, ZoneLift,
                                                  $"yard_{id}_{zone.Kind}_{i}", parent, spawn);
                if (core)
                {
                    TintFor(core, zone.Kind, prefabs);
                    placed.Add(core);
                }

                Fringe(zone.Area, tile, zone.Kind, prefabs, lot, rng, parent, spawn, placed,
                       $"yard_{id}_{zone.Kind}_{i}_fringe");
            }

            // --- 3: the ground no zone claimed -------------------------------------------------
            var patches = IndustrialLotPlanner.Weathering(
                yard.Wall, yard.Lanes, yard.Bays, yard.Halls, zones, tuning, config.seed, id);

            for (var i = 0; i < patches.Count; i++)
            {
                // Rolled for every patch whether or not it lands, so a rejected one cannot shift
                // what the next draws - the same discipline StreetPropPlacer states.
                var wornDirt = rng.NextDouble() < 0.62;

                var patch = GroundPlacer.LaySurface(
                    wornDirt ? dirt : concrete, patches[i].Min, patches[i].Max, FringeLift,
                    $"yard_{id}_worn_{i}", parent, spawn);

                if (!patch)
                    continue;

                if (!wornDirt)
                    MaterialTint.Repaint(patch, prefabs.buildingBaseMaterial, prefabs.cinderTint);

                placed.Add(patch);
            }

            // --- grime -------------------------------------------------------------------------
            Grime(yard, prefabs, lot, rng, parent, placed);
        }

        /// <summary>
        /// Patches straddling a zone's edges, half in and half out, so the seam between two
        /// surfaces is a worn line rather than a drawn one.
        ///
        /// Laid at a higher lift than either surface so they read as the newer of the two, and
        /// emitted in the ZONE's own material - a tongue of concrete reaching into the dirt is
        /// what a yard actually wears, not a neat border.
        /// </summary>
        static void Fringe(
            IndustrialLayout.Rect area, GameObject tile, LotZoneKind kind, PrefabDatabase prefabs,
            IndustrialLotConfig lot, System.Random rng, Transform parent, SpawnPrefab spawn,
            List<GameObject> placed, string name)
        {
            var perEdge = lot ? lot.fringePatchesPerEdge : 4;
            if (perEdge <= 0 || !tile)
                return;

            var minReach = lot ? lot.minFringeReach : 1.5f;
            var maxReach = lot ? lot.maxFringeReach : 4f;
            var minRun = lot ? lot.minFringeRun : 2f;
            var maxRun = lot ? lot.maxFringeRun : 6f;

            var size = area.Size;
            var n = 0;

            // The two LONG edges only, which halves the object count and is also the better
            // picture. A zone's short edges are the ones that abut a hall wall or a carriageway
            // kerb, where a ragged line reads as a mistake; the long ones face the open yard,
            // which is where concrete is actually supposed to peter out into dirt.
            var longEdgesRunAlongX = size.x >= size.y;
            var first = longEdgesRunAlongX ? 0 : 2;

            for (var edge = first; edge < first + 2; edge++)
            {
                var horizontal = edge < 2;
                var span = horizontal ? size.x : size.y;

                for (var i = 0; i < perEdge; i++)
                {
                    var run = Mathf.Lerp(minRun, maxRun, (float)rng.NextDouble());
                    var reach = Mathf.Lerp(minReach, maxReach, (float)rng.NextDouble());
                    var at = (float)rng.NextDouble() * Mathf.Max(0.01f, span - run);

                    if (run >= span)
                        continue;

                    Vector2 min, max;

                    if (horizontal)
                    {
                        var y = edge == 0 ? area.Min.y : area.Max.y;
                        min = new Vector2(area.Min.x + at, y - reach * 0.5f);
                        max = new Vector2(min.x + run, y + reach * 0.5f);
                    }
                    else
                    {
                        var x = edge == 2 ? area.Min.x : area.Max.x;
                        min = new Vector2(x - reach * 0.5f, area.Min.y + at);
                        max = new Vector2(x + reach * 0.5f, min.y + run);
                    }

                    var patch = GroundPlacer.LaySurface(tile, min, max, FringeLift,
                                                        $"{name}_{n++}", parent, spawn);
                    if (!patch)
                        continue;

                    TintFor(patch, kind, prefabs);
                    placed.Add(patch);
                }
            }
        }

        /// <summary>
        /// Everything drawn flat on top: tyre ruts in from the gate, oil under the vehicle
        /// ground, and a painted rail spur where a long enough run exists.
        ///
        /// All of it goes through GroundPaint, which batches every stroke sharing a material into
        /// a single mesh - a yard's worth of ruts and sleepers is three renderers, not two
        /// hundred. The pack's flat untextured Colors materials need no UV authoring, which is
        /// the whole reason this project paints rather than projects decals.
        /// </summary>
        static void Grime(
            WorksYard yard, PrefabDatabase prefabs, IndustrialLotConfig lot, System.Random rng,
            Transform parent, List<GameObject> placed)
        {
            var ruts = new List<GroundPaint.Stroke>();
            var stains = new List<Vector2[]>();

            var gauge = lot ? lot.rutGauge : 1.8f;
            var width = lot ? lot.rutWidth : 0.55f;

            // Ruts run the length of every carriageway, which is where the lorries actually go.
            foreach (var lane in yard.Lanes)
            {
                var size = lane.Size;
                if (size.x < 1f || size.y < 1f)
                    continue;

                var alongX = size.x >= size.y;
                var centre = lane.Centre;

                for (var side = -1; side <= 1; side += 2)
                {
                    var offset = side * gauge * 0.5f;

                    ruts.Add(alongX
                        ? new GroundPaint.Stroke(new Vector2(lane.Min.x, centre.y + offset),
                                                 new Vector2(lane.Max.x, centre.y + offset), width)
                        : new GroundPaint.Stroke(new Vector2(centre.x + offset, lane.Min.y),
                                                 new Vector2(centre.x + offset, lane.Max.y), width));
                }
            }

            // And a worn track off each carriageway into each dock, which is the line a lorry
            // takes when it backs up to a door.
            foreach (var zone in yard.Zones)
            {
                if (zone.Kind != LotZoneKind.LoadingApron || zone.Facing.sqrMagnitude < 0.5f)
                    continue;

                var c = zone.Area.Centre;
                var away = new Vector2(-zone.Facing.x, -zone.Facing.z);
                var half = Mathf.Max(zone.Area.Size.x, zone.Area.Size.y) * 0.5f;

                for (var side = -1; side <= 1; side += 2)
                {
                    var lateral = new Vector2(-away.y, away.x) * (side * gauge * 0.5f);
                    ruts.Add(new GroundPaint.Stroke(c + lateral - away * half,
                                                    c + lateral + away * half, width));
                }
            }

            // Appends to the same stroke list, so the spur costs no extra renderer.
            Spur(yard, lot, ruts);

            // Oil where the vehicles stand: the aprons and the staging, not the open yard.
            var maxStains = lot ? lot.maxStains : 14;
            var minR = lot ? lot.minStainRadius : 0.7f;
            var maxR = lot ? lot.maxStainRadius : 2.2f;

            foreach (var zone in yard.Zones)
            {
                if (stains.Count >= maxStains)
                    break;

                if (zone.Kind != LotZoneKind.LoadingApron && zone.Kind != LotZoneKind.TruckStaging)
                    continue;

                var wanted = 1 + rng.Next(3);

                for (var i = 0; i < wanted && stains.Count < maxStains; i++)
                {
                    var r = Mathf.Lerp(minR, maxR, (float)rng.NextDouble());
                    var t = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
                    var centre = zone.Area.Min + Vector2.Scale(zone.Area.Size, t);

                    stains.Add(Blob(centre, r, rng));
                }
            }

            Add(placed, GroundPaint.Emit(ruts, prefabs.grimeMaterial ? prefabs.grimeMaterial
                                                                    : prefabs.paintDarkMaterial,
                                         GroundPaint.PaintLift, $"yard_{yard.BlockId}_ruts", parent));

            Add(placed, GroundPaint.EmitPolys(stains, prefabs.oilMaterial ? prefabs.oilMaterial
                                                                         : prefabs.paintDarkMaterial,
                                              GroundPaint.PaintLift + 0.005f,
                                              $"yard_{yard.BlockId}_oil", parent));
        }

        /// <summary>
        /// A stub-end works spur, drawn rather than tiled. Two rails and a run of sleepers down
        /// the longest carriageway, stopping short of the wall.
        ///
        /// Painted because the tiled version does not fit and cannot be made to. The pack's
        /// tile-rail-straight-nb is 30m square and mostly ballast, while the deepest free band in
        /// a compound is about 16m; squashing it across the track collapses the sleepers into the
        /// rails. Reaching a lot edge would also need a second opening in PerimeterFence, which
        /// tests exactly one side. A stub-end spur inside the wall is the more correct shape
        /// anyway - most works sidings ended at a buffer, not at the street.
        ///
        /// Appended to the same stroke list as the ruts, so it costs no extra renderer.
        /// </summary>
        static bool Spur(WorksYard yard, IndustrialLotConfig lot, List<GroundPaint.Stroke> into)
        {
            var minRun = lot ? lot.minSpurRun : 20f;
            var gauge = lot ? lot.spurGauge : 1.5f;
            var pitch = lot ? lot.sleeperPitch : 0.9f;

            var best = default(IndustrialLayout.Rect);
            var bestRun = 0f;

            foreach (var lane in yard.Lanes)
            {
                var run = Mathf.Max(lane.Size.x, lane.Size.y);
                if (run <= bestRun)
                    continue;

                bestRun = run;
                best = lane;
            }

            if (bestRun < minRun)
                return false;

            var alongX = best.Size.x >= best.Size.y;
            var centre = best.Centre;

            // Off to one side of the carriageway rather than down the middle: a siding runs
            // beside the road it shares a yard with, and on the crown it would read as a tramway.
            var offset = (alongX ? best.Size.y : best.Size.x) * 0.5f - gauge;

            var from = alongX ? best.Min.x + 3f : best.Min.y + 3f;
            var to = alongX ? best.Max.x - 3f : best.Max.y - 3f;

            for (var side = -1; side <= 1; side += 2)
            {
                var rail = offset + side * gauge * 0.5f;

                into.Add(alongX
                    ? new GroundPaint.Stroke(new Vector2(from, centre.y + rail),
                                             new Vector2(to, centre.y + rail), 0.15f)
                    : new GroundPaint.Stroke(new Vector2(centre.x + rail, from),
                                             new Vector2(centre.x + rail, to), 0.15f));
            }

            for (var at = from; at <= to; at += pitch)
            {
                into.Add(alongX
                    ? new GroundPaint.Stroke(new Vector2(at, centre.y + offset - gauge * 0.75f),
                                             new Vector2(at, centre.y + offset + gauge * 0.75f), 0.22f)
                    : new GroundPaint.Stroke(new Vector2(centre.x + offset - gauge * 0.75f, at),
                                             new Vector2(centre.x + offset + gauge * 0.75f, at), 0.22f));
            }

            return true;
        }

        /// <summary>An irregular convex blob - a stain, not a circle.</summary>
        static Vector2[] Blob(Vector2 centre, float radius, System.Random rng)
        {
            const int Points = 8;
            var poly = new Vector2[Points];

            for (var i = 0; i < Points; i++)
            {
                var angle = i * Mathf.PI * 2f / Points;
                var r = radius * Mathf.Lerp(0.55f, 1f, (float)rng.NextDouble());
                poly[i] = centre + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
            }

            return poly;
        }

        static GameObject SurfaceFor(LotZoneKind kind, GameObject concrete, GameObject dirt) =>
            kind switch
            {
                LotZoneKind.LoadingApron => concrete,
                LotZoneKind.TruckStaging => concrete,
                LotZoneKind.BoilerHouse => concrete,
                _ => dirt,
            };

        /// <summary>
        /// Cinders are dirt repainted, not a new tile. The whole pack shares one opaque material
        /// and its colours are UV cells, so the only way to get ash-dark ground is to multiply
        /// the base colour - which is exactly what GroundPlacer's own shade pass does.
        /// </summary>
        static void TintFor(GameObject surface, LotZoneKind kind, PrefabDatabase prefabs)
        {
            if (kind != LotZoneKind.CinderYard || !prefabs.cinderTint)
                return;

            MaterialTint.Repaint(surface, prefabs.buildingBaseMaterial, prefabs.cinderTint);
        }

        static void Add(List<GameObject> placed, GameObject instance)
        {
            if (instance)
                placed.Add(instance);
        }
    }
}
