using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Entities;

namespace LivingCity.Generation
{
    /// <summary>
    /// Stands the loose material on a works yard: the Instantiate half of the pass
    /// <see cref="IndustrialYardProps"/> plans.
    ///
    /// Its own file rather than more of IndustrialLotBuilder, whose header makes a promise this
    /// would break - "This pass is the ground only" - and whose closing argument, that a dressed
    /// yard costs a few dozen renderers because every stroke sharing a material batches into one
    /// mesh, is true of painted ground and false of props. So the industrial code keeps the split
    /// it already has twice over: IndustrialLayout plans and IndustrialDresser builds,
    /// IndustrialLotPlanner plans and IndustrialLotBuilder builds the ground, IndustrialYardProps
    /// plans and this builds the stock.
    ///
    /// ONE PREFAB PER ZONE, which is the whole arrangement rule and is worth stating plainly
    /// because the obvious alternative looks better on paper. IndustrialDresser's own header
    /// records what drawing a fresh prefab per attempt produced - "no two adjacent items agreed
    /// about anything, which is what reads as tipped out" - and the variety a yard actually wants
    /// comes from somewhere else entirely: IndustrialLotPlanner emits up to maxBulkYards SEPARATE
    /// raw-material zones precisely so that "coal, timber and aggregate are genuinely separate
    /// heaps". Three heaps of three things, not one heap of everything.
    ///
    /// Nothing here is ever scaled. There is no scale parameter to pass, which is the cheapest
    /// way to hold a rule that has no exceptions.
    /// </summary>
    public static class IndustrialYardDresser
    {
        /// <summary>Spreads consecutive block ids apart in the seed space - see BlockLots.</summary>
        const int BlockStride = 397;

        /// <summary>
        /// Salt, keeping this stream clear of the three already drawn off SeedOffsets.IndustrialLot
        /// (the planner, the weathering, and IndustrialLotBuilder's ground). Retuning props must
        /// not move a zone any more than it may move a hall.
        /// </summary>
        const int Salt = 15485863;

        /// <summary>
        /// Height the stock stands at. Must clear IndustrialLotBuilder's own layers - its zone
        /// cores sit at 0.07 and its fringe and weathering at 0.085 - or a barrel's base is buried
        /// under the concrete that was laid to put it on.
        /// </summary>
        const float StandOn = 0.09f;

        /// <summary>The ceiling used when no IndustrialLotConfig asset exists. Matches the class.</summary>
        const int DefaultBudget = 120;

        public static List<GameObject> Build(
            Transform cityRoot, PrefabDatabase prefabs, CityConfig config,
            IndustrialLotConfig lot, Transform parent, SpawnPrefab spawn)
        {
            var placed = new List<GameObject>();
            if (!cityRoot || !prefabs || !config)
                return placed;

            spawn ??= RoadNetworkBuilder.RuntimeSpawn;

            // Scoped to this city root rather than FindObjectsByType, for the reason
            // IndustrialLotBuilder gives: a stale second city left in the scene must not get
            // dressed. See CityBuilder's own note on duplicate roots.
            var yards = cityRoot.GetComponentsInChildren<WorksYard>(true);
            if (yards.Length == 0)
                return placed;

            var palette = prefabs.PaletteFor(BlockZone.Industrial);
            if (palette == null)
                return placed;

            var budget = lot ? lot.maxPropsPerLot : DefaultBudget;

            foreach (var yard in yards)
                Dress(yard, palette, config, budget, parent, spawn, placed);

            return placed;
        }

        /// <summary>
        /// One compound. Seeded per YARD rather than per city, so a block whose zoning changed
        /// cannot shift the stock of every yard generated after it - the hierarchy order
        /// GetComponentsInChildren returns is stable in practice and is still not something worth
        /// depending on.
        /// </summary>
        static void Dress(
            WorksYard yard, PrefabDatabase.ZonePalette palette, CityConfig config,
            int budget, Transform parent, SpawnPrefab spawn, List<GameObject> placed)
        {
            var zones = yard.Zones;
            if (zones == null || zones.Length == 0 || budget <= 0)
                return;

            var rng = new System.Random(
                config.seed + SeedOffsets.IndustrialLot + yard.BlockId * BlockStride + Salt);

            var quotas = IndustrialYardProps.Budgets(zones, budget);

            var site = new IndustrialYardProps.Site
            {
                Lanes = yard.Lanes,
                Bays = yard.Bays,
                Obstacles = yard.Obstacles,
                Gate = yard.HasGate
                    ? new Vector2(yard.GateCentre.x, yard.GateCentre.z)
                    : yard.Wall.Centre,
            };

            var spent = 0;
            var wanted = 0;

            for (var i = 0; i < zones.Length; i++)
            {
                var zone = zones[i];
                var quota = quotas[i];

                wanted += quota;

                if (quota <= 0)
                    continue;

                // Rolled for every zone whether or not it ends up placing anything, so a zone
                // with no usable bag cannot shift what the next one draws. The discipline
                // IndustrialLotBuilder and StreetPropPlacer both state.
                var prefab = Pick(zone.Kind, palette, rng);
                if (!prefab)
                    continue;

                var yaw = IndustrialYardProps.YawFor(zone);
                var footprint = PrefabBounds.FootprintXZ(prefab, yaw);

                var slots = IndustrialYardProps.Plan(zone, site, footprint, quota, rng);

                foreach (var slot in slots)
                {
                    var instance = Stand(prefab, slot, parent, spawn);
                    if (!instance)
                        continue;

                    placed.Add(instance);
                    spent++;
                }
            }

            // Only when the ceiling actually bit. A well-fed yard stays silent; a starved one
            // says so, which is the project's standing rule against caps that truncate quietly.
            if (spent >= budget)
                Debug.Log($"[IndustrialYardDresser] Yard {yard.BlockId} hit its prop ceiling at " +
                          $"{budget} - {wanted} were planned across {zones.Length} zones. Raise " +
                          $"IndustrialLotConfig.maxPropsPerLot if the yards read as under-stocked.");
        }

        /// <summary>
        /// Which bag a zone draws from. A switch rather than six palette arrays, for the reason
        /// IndustrialLotBuilder.SurfaceFor is a switch over two ground fields: this is a design
        /// decision about which art belongs where, and a decision belongs in code where it can
        /// carry its argument, not in a .asset where it is invisible.
        ///
        /// The three-way draws are what turn a compound's repeated zones into different heaps.
        /// </summary>
        static GameObject Pick(
            LotZoneKind kind, PrefabDatabase.ZonePalette palette, System.Random rng)
        {
            switch (kind)
            {
                case LotZoneKind.LoadingApron:
                    return Draw(rng, palette.yardCrates, palette.stackProps);

                case LotZoneKind.TruckStaging:
                    return Draw(rng, palette.yardBarrels, palette.stackProps);

                // The zone that repeats, so the bag is rolled per zone: a compound with three
                // stockpiles comes out as barrels, sacks and timber rather than as three
                // identical grids. This is what maxBulkYards was written for.
                case LotZoneKind.RawMaterialYard:
                    return Draw(rng, Roll(rng, palette.yardBarrels, palette.yardSacks,
                                          palette.stackProps));

                // Fixtures, which is why it is a boiler house and not another stockpile: the
                // things here have their backs to a wall and do not stack.
                case LotZoneKind.BoilerHouse:
                    return Draw(rng, palette.yardFixtures, palette.yardBarrels);

                case LotZoneKind.CinderYard:
                    return Draw(rng, palette.yardFixtures, palette.yardCrates);

                case LotZoneKind.ScrapCorner:
                    return Draw(rng, Roll(rng, palette.yardCrates, palette.yardBarrels,
                                          palette.stackProps));

                default:
                    return Draw(rng, palette.stackProps);
            }
        }

        /// <summary>
        /// Picks one of three bags, skipping the empty ones. Always draws, so the choice costs
        /// the same number of rolls whether or not the storage pack is present - which is what
        /// keeps a city generated without it laid out identically to one generated with it.
        /// </summary>
        static GameObject[] Roll(
            System.Random rng, GameObject[] a, GameObject[] b, GameObject[] c)
        {
            var choice = rng.Next(3);

            var bags = new[] { a, b, c };

            for (var i = 0; i < 3; i++)
            {
                var bag = bags[(choice + i) % 3];
                if (bag != null && bag.Length > 0)
                    return bag;
            }

            return null;
        }

        /// <summary>
        /// One prefab from the first bag that has anything in it.
        ///
        /// The fallback chain is what makes the storage pack additive rather than load-bearing:
        /// every yard kind ends at a bag the project already ships, so a licence question or a
        /// failed import costs variety and not the feature.
        /// </summary>
        static GameObject Draw(System.Random rng, params GameObject[][] bags)
        {
            // Drawn before the bag is chosen, so the roll count does not depend on which bags
            // happen to be filled.
            var roll = rng.Next(1 << 20);

            foreach (var bag in bags)
            {
                if (bag == null || bag.Length == 0)
                    continue;

                var prefab = bag[roll % bag.Length];
                if (prefab)
                    return prefab;
            }

            return null;
        }

        /// <summary>
        /// Stands one piece on its slot.
        ///
        /// Two things it does that OverlapSpawn.Place does not, and one it deliberately
        /// omits.
        ///
        /// It corrects for the PIVOT in y as well as in xz. Every Epic City prefab is authored
        /// with its base on the ground, so nothing in the project has ever needed this and
        /// IndustrialDresser passes position.y straight through - but a foreign pack is free to
        /// centre its pivots, and a barrel buried to its waist is the failure that reads as the
        /// generator being broken. Costs nothing where min.y is already zero.
        ///
        /// It takes no overlap test, because IndustrialYardProps.Plan already rejected every slot
        /// that touched a lane, a bay, an obstacle or another slot - and doing it twice, against
        /// a list this pass would have to keep itself, is how the two answers drift apart.
        ///
        /// And it has NO SCALE PARAMETER. Nothing in a yard is ever scaled below its authored
        /// size; leaving no argument to pass is stronger than remembering not to pass one.
        /// </summary>
        static GameObject Stand(
            GameObject prefab, IndustrialYardProps.Slot slot, Transform parent, SpawnPrefab spawn)
        {
            if (!prefab)
                return null;

            var rotation = Quaternion.Euler(0f, slot.Yaw, 0f);
            var local = PrefabBounds.Get(prefab);

            // The mesh is not necessarily centred on its pivot, so offset by the rotated local
            // bounds centre to land the geometry where the slot asked for it.
            var offset = rotation * new Vector3(local.center.x, 0f, local.center.z);

            var position = new Vector3(
                slot.Centre.x - offset.x,
                StandOn - local.min.y,
                slot.Centre.y - offset.z);

            return spawn(prefab, position, rotation, parent);
        }
    }
}
