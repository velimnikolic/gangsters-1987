using System.Collections.Generic;
using UnityEngine;
using LivingCity.Entities;

namespace LivingCity.Generation
{
    /// <summary>What a piece of yard is for. Also the drop order when the coverage cap bites.</summary>
    public enum LotZoneKind
    {
        LoadingApron,
        TruckStaging,
        RawMaterialYard,
        BoilerHouse,
        CinderYard,
        ScrapCorner,
    }

    /// <summary>
    /// One functional area of a works yard: a rectangle, what happens on it, and which way the
    /// things standing on it should face.
    /// </summary>
    [System.Serializable]
    public struct LotZone
    {
        public LotZoneKind Kind;
        public IndustrialLayout.Rect Area;

        /// <summary>
        /// The dock normal for an apron, the gate normal for staging, otherwise the lane axis.
        /// Props align to this - a works stacks things square to something, never at 17 degrees.
        /// </summary>
        public Vector3 Facing;

        /// <summary>Index into WorksYard.Halls, or -1 when this zone serves no single hall.</summary>
        public int HallIndex;

        /// <summary>Share of the lot's prop budget. Highest at the docks, per the brief.</summary>
        public float Density;
    }

    /// <summary>
    /// Partitions the free ground inside a works compound into functional zones.
    ///
    /// Pure geometry, holding no UnityEngine.Object, for the reason IndustrialLayout gives and
    /// IndustrialLayoutTests depends on: it runs in a bare .NET host with no Editor and no Play
    /// mode, which is the only way a partitioning algorithm gets properties asserted about it.
    ///
    /// Two things about the shape of this are worth stating because the obvious design differs.
    ///
    /// First, CIRCULATION LANES ARE NOT CARVED. The brief asks for lanes at least 5 units wide
    /// kept clear of props, and the compound already has them: IndustrialLayout's carriageways
    /// are serviceRoadWidth (8m by default) and span the compound end to end, and its own comment
    /// records that the gate is aligned to one so no spur is needed. So the whole requirement
    /// collapses to "never write a zone over a road", which the occupancy grid below gives for
    /// free. Carving lanes as well would have cut a second set of corridors across the first.
    ///
    /// Second, ZONES DELIBERATELY DO NOT TILE THE LOT. Open ground is correct - a yard needs
    /// turning room - so <see cref="Tuning.maxZoneCoverage"/> is a hard cap on the fraction of
    /// free ground zones may claim, enforced by DROPPING low-priority zones rather than by
    /// hoping the seeds happen not to fill everything. On a big block they otherwise would.
    ///
    /// Deterministic in (seed, blockId) alone, the same contract IndustrialLayout and BlockLots
    /// hold, so this can be replayed by anything in any order.
    /// </summary>
    public static class IndustrialLotPlanner
    {
        /// <summary>
        /// Occupancy grid resolution. Two metres is a crate, and it makes even the largest block
        /// (106m, so a 52x52 grid) cost nothing to scan. Finer would buy precision the props
        /// cannot use, because a stack is placed against a face, not against a cell.
        /// </summary>
        public const float Cell = 2f;

        /// <summary>Spreads consecutive block ids apart in the seed space - see BlockLots.</summary>
        const int BlockStride = 397;

        /// <summary>
        /// Standoff kept around everything already standing. Half a metre, so a zone edge does
        /// not land flush against a hall wall and leave props with nowhere to sit.
        /// </summary>
        const float Standoff = 0.5f;

        /// <summary>Ground left along the inside of the wall, so the fence line stays walkable.</summary>
        const float WallSkirt = 0.6f;

        /// <summary>
        /// Depth a staff car park occupies inward from its road edge. IndustrialLayout sizes the
        /// run as "two bay rows plus their aisle is 17.2" - that figure, not the stall depth,
        /// because ParkingLayout.ForStreetBay lays both rows off this one origin.
        /// </summary>
        const float ParkingBandDepth = 17.2f;

        /// <summary>
        /// Half width of the corridor kept clear inside the gate: the gate opening plus a margin
        /// each side, matching the box PerimeterFence.Approach reserves OUTSIDE the wall. A lorry
        /// that can turn in off the street has to be able to keep turning once it is through.
        /// </summary>
        const float GateCorridorHalfWidth = IndustrialLayout.GateWidth * 0.5f + PerimeterFence.ApproachMargin;

        /// <summary>Tuning for the partition. A plain struct, so the planner stays Editor-free.</summary>
        [System.Serializable]
        public struct Tuning
        {
            [Tooltip("Deepest an apron may reach off a dock face. Caps the deepest halls, whose " +
                     "forecourt would otherwise swallow the lot.")]
            public float maxApronDepth;

            [Tooltip("Below this the forecourt is not an apron and no zone is emitted.")]
            public float minApronDepth;

            [Tooltip("How far an apron reaches past the hall's own width, each side.")]
            public float apronSideMargin;

            public float minYardSize;
            public float minScrapSize;

            [Tooltip("How many separate bulk stockpiles one yard may hold. A works has one " +
                     "boiler house and one scrap corner, but coal, timber and aggregate are " +
                     "genuinely separate heaps - so this is the zone that repeats.")]
            public int maxBulkYards;

            [Tooltip("Longest side of one stockpile. A heap of coal is not the size of a row of " +
                     "halls: unclamped, the first pile swallowed a whole empty row and the " +
                     "coverage cap then deleted the boiler house and the scrap corner to pay " +
                     "for it.")]
            public float maxBulkYardSide;
            public float boilerWidth;
            public float boilerDepth;
            public float cinderDepth;

            [Tooltip("Hard ceiling on the fraction of free ground zones may claim. Open ground " +
                     "is correct - the fix for it is surface treatment, not more objects.")]
            public float maxZoneCoverage;

            [Header("Weathering")]
            [Tooltip("Square metres of open ground per worn patch. Lower means a busier, more " +
                     "broken-up yard surface.")]
            public float metresPerPatch;

            public float minPatchSide;
            public float maxPatchSide;
            public int maxPatches;

            public static Tuning Default => new Tuning
            {
                maxApronDepth = 9f,
                minApronDepth = 3f,
                apronSideMargin = 1f,
                minYardSize = 8f,
                minScrapSize = 6f,
                maxBulkYards = 8,
                maxBulkYardSide = 20f,
                boilerWidth = 8f,
                boilerDepth = 6f,
                cinderDepth = 3.5f,
                maxZoneCoverage = 0.5f,
                metresPerPatch = 90f,
                minPatchSide = 5f,
                maxPatchSide = 16f,
                maxPatches = 26,
            };
        }

        /// <summary>
        /// Ground that must stay clear of props and colliders: every carriageway, plus the
        /// corridor inside the gate. Published on the marker so the dressing pass, the tests and
        /// a future NavMesh bake all read one answer.
        /// </summary>
        public static IndustrialLayout.Rect[] Lanes(IndustrialLayout.Layout layout)
        {
            var lanes = new List<IndustrialLayout.Rect>(layout.Roads.Count + 1);
            lanes.AddRange(layout.Roads);

            if (TryGateCorridor(layout, out var corridor))
                lanes.Add(corridor);

            return lanes.ToArray();
        }

        /// <summary>
        /// Ground the vehicles own: staff bays and the lorry stand. Published alongside the lanes
        /// because they are the one part of the compound the marker cannot re-derive later - not
        /// because they move, but because reconstructing them needs the whole Layout and the
        /// dressing pass only keeps the wall.
        /// </summary>
        public static IndustrialLayout.Rect[] Bays(IndustrialLayout.Layout layout)
        {
            var bays = new List<IndustrialLayout.Rect>(
                layout.ParkingRuns.Count + layout.LorryStands.Count);

            foreach (var run in layout.ParkingRuns)
                bays.Add(Occupancy.BandOf(run, run.Width, ParkingBandDepth));

            foreach (var stand in layout.LorryStands)
                bays.Add(Occupancy.BandOf(stand, stand.Width, IndustrialLayout.LorryStandDepth));

            return bays.ToArray();
        }

        /// <summary>
        /// Irregular patches of worn ground over whatever the zones did NOT claim.
        ///
        /// This is the half of the surface treatment that is easy to leave out and is the half
        /// that matters most. Zones cover about half the open ground by design - a yard needs
        /// turning room, and the brief is explicit that the answer to bare ground is surface
        /// treatment rather than more objects. But treating only the zones leaves the OTHER half
        /// exactly as it was: one stretched tile, one flat colour, reading as a single blob no
        /// matter how carefully the zones beside it are dressed.
        ///
        /// Small and overlapping rather than one big rectangle, because the point is a broken
        /// edge. Sizes and positions are jittered from the block's own stream.
        /// </summary>
        public static List<IndustrialLayout.Rect> Weathering(
            IndustrialLayout.Rect wall,
            IndustrialLayout.Rect[] lanes,
            IndustrialLayout.Rect[] bays,
            WorksHall[] halls,
            IReadOnlyList<LotZone> zones,
            Tuning tuning,
            int seed,
            int blockId)
        {
            var patches = new List<IndustrialLayout.Rect>();

            var grid = Occupancy.From(wall, lanes, bays, halls);
            if (grid.Width <= 0 || grid.Height <= 0)
                return patches;

            if (zones != null)
                foreach (var zone in zones)
                    grid.Reserve(zone.Area);

            // Its own sub-stream, offset off the block, so that retuning how worn a yard looks
            // cannot move a zone - the zoning above has already drawn from the block's stream and
            // the two must stay independent.
            var rng = new System.Random(seed + SeedOffsets.IndustrialLot + blockId * BlockStride + 7919);

            var wanted = Mathf.Clamp(
                Mathf.RoundToInt(grid.FreeCells * Cell * Cell / tuning.metresPerPatch),
                0, tuning.maxPatches);

            for (var i = 0; i < wanted; i++)
            {
                // Rolled before the fit is tested, never after.
                var side = Mathf.Lerp(tuning.minPatchSide, tuning.maxPatchSide,
                                      (float)rng.NextDouble());
                var aspect = Mathf.Lerp(0.55f, 1.45f, (float)rng.NextDouble());

                if (!grid.TryLargestFreeRect(wall.Centre, tuning.minPatchSide,
                                             tuning.minPatchSide, out var host))
                    break;

                var patch = Stockpile(host, side, wall.Centre);
                patch.Max = new Vector2(patch.Min.x + patch.Size.x,
                                        patch.Min.y + Mathf.Min(patch.Size.y, side * aspect));

                grid.Reserve(patch);
                patches.Add(patch);
            }

            return patches;
        }

        /// <summary>
        /// Square metres inside the wall that no carriageway, hall, bay or gate corridor already
        /// claims. This is the denominator the coverage cap is measured against, and it is
        /// exposed because measuring it any other way gets the answer wrong: against the WALL
        /// area a half cap never binds, because the compound spends most of its own ground on
        /// buildings and tarmac before the yard is even considered. Zones peak at about 18% of
        /// the wall rect on the worst block, so a cap expressed against that would pass however
        /// badly the partition behaved.
        /// </summary>
        public static float FreeGround(IndustrialLayout.Layout layout, WorksHall[] halls)
        {
            if (layout == null || !layout.Usable)
                return 0f;

            return Occupancy.Build(layout, halls).FreeCells * Cell * Cell;
        }

        /// <summary>
        /// Plans one works yard. Zones are seeded in a fixed priority order and each claims its
        /// ground before the next looks, so the order below IS the precedence: an apron always
        /// beats a bulk store for the same square metre, which is the right way round because
        /// the apron's position is derived from a door and the store's is merely convenient.
        /// </summary>
        public static List<LotZone> Plan(
            IndustrialLayout.Layout layout, WorksHall[] halls, Tuning tuning, int seed, int blockId)
        {
            var zones = new List<LotZone>();
            if (layout == null || !layout.Usable)
                return zones;

            var grid = Occupancy.Build(layout, halls);
            if (grid.Width <= 0 || grid.Height <= 0)
                return zones;

            if (grid.FreeCells == 0)
                return zones;

            var rng = new System.Random(seed + SeedOffsets.IndustrialLot + blockId * BlockStride);

            // The reference every "away from the gate" decision is measured against. With no gate
            // there is no near end, so the compound centre stands in and "furthest" still means
            // the corners - which is where a scrap heap belongs anyway.
            var anchor = layout.HasGate
                ? new Vector2(layout.GateCentre.x, layout.GateCentre.z)
                : layout.Wall.Centre;

            SeedAprons(zones, grid, halls, tuning, rng);

            // Measured AFTER the aprons, which is what makes the cap mean the right thing. The
            // aprons are not discretionary: their ground is the strip in front of a door, and
            // the yard's open turning room is the carriageways and the gate apron, neither of
            // which any zone may touch. What the cap is really guarding against is the
            // opportunistic fill below spreading across whatever is left.
            var openGround = grid.FreeCells;

            SeedStaging(zones, grid, layout, tuning);
            SeedBoilerHouse(zones, grid, anchor, tuning, rng);
            SeedScrapCorner(zones, grid, layout, anchor, tuning);

            // Last, and repeating, because bulk storage is the zone that scales with the yard.
            // The singletons above are one apiece however big the compound gets.
            SeedBulkYards(zones, grid, anchor, tuning, openGround);

            ApplyCoverageCap(zones, openGround, tuning);

            return zones;
        }

        // ---------------------------------------------------------------- zone seeds

        /// <summary>
        /// One apron per hall, on the ground between its doors and the carriageway that serves
        /// them. Derived, not chosen: the rectangle is the hall's own front face swept along its
        /// own dock normal, so an apron cannot end up behind a building or beside one.
        ///
        /// This is also the widest free band in the compound, and the reason is a defect rather
        /// than a design: IndustrialDresser pushes each hall AWAY from its road instead of
        /// towards it, so the forecourt every hall was meant to have ended up in front of it,
        /// empty, with the doors staring across it. Filling it is the single largest change to
        /// how the zone reads.
        /// </summary>
        static void SeedAprons(
            List<LotZone> zones, Occupancy grid, WorksHall[] halls, Tuning tuning, System.Random rng)
        {
            if (halls == null)
                return;

            for (var i = 0; i < halls.Length; i++)
            {
                var hall = halls[i];
                if (hall.Outward.sqrMagnitude < 0.5f)
                    continue;

                var lateral = Vector3.Cross(Vector3.up, hall.Outward);
                var padSize = hall.PadMax - hall.PadMin;
                var padCentre = (hall.PadMin + hall.PadMax) * 0.5f;

                var padDepth = Extent(padSize, hall.Outward);
                var front = Project(hall.Centre, hall.Outward) + hall.HalfDepth;
                var padEdge = Project(padCentre, hall.Outward) + padDepth * 0.5f;

                // Rolled before the fit is tested, never after - the house rule everywhere in
                // this codebase. A rejected apron must not change what the next one draws.
                var jitter = 0.85f + (float)rng.NextDouble() * 0.15f;

                var band = padEdge - front - Standoff;
                var depth = Mathf.Min(band, tuning.maxApronDepth * jitter);
                if (depth < tuning.minApronDepth)
                    continue;

                var near = front + 0.3f;
                var half = hall.HalfWidth + tuning.apronSideMargin;
                var across = Project(hall.Centre, lateral);

                var rect = FromFrame(hall.Outward, lateral, near, near + depth,
                                     across - half, across + half);

                rect = Clip(rect, hall.PadMin, hall.PadMax);

                if (!grid.TryClaim(ref rect, tuning.minApronDepth, tuning.minApronDepth))
                    continue;

                zones.Add(new LotZone
                {
                    Kind = LotZoneKind.LoadingApron,
                    Area = rect,
                    Facing = -hall.Outward,
                    HallIndex = i,
                    Density = 1f,
                });
            }
        }

        /// <summary>
        /// Truck staging: the two strips flanking the gate corridor, inside the apron the layout
        /// already gave up at the gate end. Near the gate and along the inside of the wall, which
        /// is where a yard stands vehicles that are waiting rather than working.
        /// </summary>
        static void SeedStaging(
            List<LotZone> zones, Occupancy grid, IndustrialLayout.Layout layout, Tuning tuning)
        {
            if (!TryGateCorridor(layout, out var corridor))
                return;

            var outward = layout.GateOutward;
            var lateral = Vector3.Cross(Vector3.up, outward);

            // Outward points OUT through the gate, so the yard is at lower along-coordinates.
            var lip = Project(layout.GateCentre, outward);
            var lo = lip - IndustrialLayout.GateMargin;
            var hi = lip;

            var gateAcross = Project(layout.GateCentre, lateral);
            var wallLo = Project(layout.Wall.Min, lateral);
            var wallHi = Project(layout.Wall.Max, lateral);
            if (wallLo > wallHi)
                (wallLo, wallHi) = (wallHi, wallLo);

            var sides = new[]
            {
                (from: wallLo, to: gateAcross - GateCorridorHalfWidth),
                (from: gateAcross + GateCorridorHalfWidth, to: wallHi),
            };

            foreach (var side in sides)
            {
                if (side.to - side.from < tuning.minScrapSize)
                    continue;

                var rect = FromFrame(outward, lateral, lo, hi, side.from, side.to);
                if (!grid.TryClaim(ref rect, tuning.minScrapSize, tuning.minApronDepth))
                    continue;

                zones.Add(new LotZone
                {
                    Kind = LotZoneKind.TruckStaging,
                    Area = rect,
                    Facing = -outward,
                    HallIndex = -1,
                    Density = 0.5f,
                });
            }
        }

        /// <summary>
        /// Bulk storage - coal, timber, aggregate. Each heap is the largest free rectangle that
        /// is FURTHEST from the gate, which is the derivation the brief asks for: nobody
        /// stockpiles a month of coal across the entrance.
        ///
        /// REPEATS until the coverage cap binds, and that is the whole point of it. One heap per
        /// yard was the original design and it made the cap decorative: on a 106m compound whose
        /// pads mostly came out empty there is 6700 square metres of free ground, and a single
        /// stockpile plus the two singleton zones filled 15% of it. The cap can only stop a yard
        /// being over-filled; something has to drive toward it, or a big empty works stays a big
        /// empty works with four small rectangles drawn on it.
        /// </summary>
        static void SeedBulkYards(
            List<LotZone> zones, Occupancy grid, Vector2 anchor, Tuning tuning, int openGround)
        {
            var budget = openGround * Cell * Cell * Mathf.Clamp01(tuning.maxZoneCoverage);

            var used = 0f;
            foreach (var zone in zones)
                if (zone.Kind != LotZoneKind.LoadingApron)
                    used += zone.Area.Size.x * zone.Area.Size.y;

            for (var i = 0; i < tuning.maxBulkYards && used < budget; i++)
            {
                if (!grid.TryLargestFreeRect(anchor, tuning.minYardSize, tuning.minYardSize,
                                             out var host))
                    return;

                // One heap, cut out of the end of that ground furthest from the gate. The rest
                // of the host rect goes back on the market for the next stockpile, which is what
                // turns one slab into the several distinct piles a works actually has.
                var plot = Stockpile(host, tuning.maxBulkYardSide, anchor);
                var area = plot.Size.x * plot.Size.y;

                // Stop BEFORE overshooting rather than after. Overshooting and letting the cap
                // tidy up afterwards is what deleted the boiler house and the scrap corner: the
                // cap sheds zones in priority order, so the pile that broke the budget was paid
                // for by the two zones that give the yard its character.
                if (used + area > budget)
                    return;

                grid.Claim(plot);
                used += area;

                zones.Add(new LotZone
                {
                    Kind = LotZoneKind.RawMaterialYard,
                    Area = plot,
                    Facing = LongAxis(plot),
                    HallIndex = -1,
                    Density = 0.6f,
                });
            }
        }

        /// <summary>
        /// The boiler house and the ash it throws, against a wall rather than free-standing.
        ///
        /// "Against a wall" has to mean "within the three metres of one", and that is a measured
        /// constraint rather than a stylistic choice: IndustrialLayout leaves EdgeMargin (3m) at
        /// the compound ends and hands every other metre to the rows and carriageways, so there
        /// is no five-metre wall-adjacent strip on any block size. The outbuilding therefore sits
        /// at the far end of a free rect and pushes itself to whichever edge is nearest the wall.
        /// </summary>
        static void SeedBoilerHouse(
            List<LotZone> zones, Occupancy grid, Vector2 anchor, Tuning tuning, System.Random rng)
        {
            var flip = rng.NextDouble() < 0.5;

            if (!grid.TryFurthestFreeRect(anchor, tuning.boilerWidth, tuning.boilerDepth, out var host))
                return;

            var wide = host.Size.x >= host.Size.y;
            var plot = Anchor(host, tuning.boilerWidth, tuning.boilerDepth, wide, flip);

            grid.Claim(plot);

            zones.Add(new LotZone
            {
                Kind = LotZoneKind.BoilerHouse,
                Area = plot,
                Facing = LongAxis(plot),
                HallIndex = -1,
                Density = 0.35f,
            });

            // Cinders spill off the open side, so the ash reads as coming FROM the boiler. A
            // rect beside the plot rather than a ring around it, because a ring would have to
            // overlap the plot and the no-overlap invariant is what makes the partition testable.
            var cinder = Beside(plot, tuning.cinderDepth, wide, flip);
            if (grid.TryClaim(ref cinder, 2f, 2f))
            {
                zones.Add(new LotZone
                {
                    Kind = LotZoneKind.CinderYard,
                    Area = cinder,
                    Facing = LongAxis(cinder),
                    HallIndex = -1,
                    Density = 0.15f,
                });
            }
        }

        /// <summary>
        /// The least-used ground: what is left, nearest a wall corner and furthest from the gate.
        /// Smallest zone in the lot and the first one dropped when the coverage cap bites, which
        /// is correct - a works with no scrap corner still reads as a works.
        /// </summary>
        static void SeedScrapCorner(
            List<LotZone> zones, Occupancy grid, IndustrialLayout.Layout layout,
            Vector2 anchor, Tuning tuning)
        {
            if (!grid.TryFurthestFreeRect(anchor, tuning.minScrapSize, tuning.minScrapSize, out var host))
                return;

            var corner = NearestCorner(layout.Wall, host.Centre);
            var plot = Anchor(host, tuning.minScrapSize, tuning.minScrapSize,
                              wide: host.Size.x >= host.Size.y,
                              flip: Vector2.Distance(corner, host.Min) > Vector2.Distance(corner, host.Max));

            grid.Claim(plot);

            zones.Add(new LotZone
            {
                Kind = LotZoneKind.ScrapCorner,
                Area = plot,
                Facing = LongAxis(plot),
                HallIndex = -1,
                Density = 0.5f,
            });
        }

        /// <summary>
        /// Drops discretionary zones, lowest priority first, until they fit the coverage cap.
        ///
        /// A guarantee rather than a hope: without it a 106m block seeds a bulk yard, a boiler
        /// plot, its cinders and a scrap corner into every gap and carpets its own ground, which
        /// is the failure the brief names - zones should not tile the lot, and the answer to bare
        /// ground is surface treatment rather than more objects.
        ///
        /// LOADING APRONS ARE EXEMPT, and that is the important line. An apron's position is
        /// derived from a door; every other zone here is opportunistic fill that happened to find
        /// room. When the cap could reach the aprons it did exactly what that difference predicts
        /// - on a dense 106m block it consumed all four discretionary zones and then deleted a
        /// loading apron off a hall with 9.5m of clear forecourt, leaving the one place in the
        /// yard the brief calls the highest-density area with no zone at all. A cap that can
        /// delete derived geometry is not a cap, it is a bug with a tuning knob.
        /// </summary>
        static void ApplyCoverageCap(List<LotZone> zones, int openGround, Tuning tuning)
        {
            var budget = openGround * Cell * Cell * Mathf.Clamp01(tuning.maxZoneCoverage);

            var used = 0f;
            foreach (var zone in zones)
                if (zone.Kind != LotZoneKind.LoadingApron)
                    used += zone.Area.Size.x * zone.Area.Size.y;

            // Explicit, because enum order is declaration order and shedding by that dropped the
            // singleton zones before the repeating one. Extra stockpiles are filler and go first;
            // the boiler house is the last thing a works gives up. Aprons are absent entirely.
            var shed = new[]
            {
                LotZoneKind.RawMaterialYard,
                LotZoneKind.ScrapCorner,
                LotZoneKind.CinderYard,
                LotZoneKind.TruckStaging,
                LotZoneKind.BoilerHouse,
            };

            foreach (var kind in shed)
            {
                if (used <= budget)
                    break;

                for (var i = zones.Count - 1; i >= 0 && used > budget; i--)
                {
                    if (zones[i].Kind != kind)
                        continue;

                    used -= zones[i].Area.Size.x * zones[i].Area.Size.y;
                    zones.RemoveAt(i);
                }
            }
        }

        // ---------------------------------------------------------------- geometry helpers

        /// <summary>
        /// The corridor inside the gate. The gate is aligned to a carriageway, so this is the
        /// stub between the wall and the point that carriageway takes over.
        /// </summary>
        static bool TryGateCorridor(IndustrialLayout.Layout layout, out IndustrialLayout.Rect corridor)
        {
            corridor = default;

            if (!layout.HasGate || layout.GateOutward.sqrMagnitude < 0.5f)
                return false;

            var outward = layout.GateOutward;
            var lateral = Vector3.Cross(Vector3.up, outward);

            var lip = Project(layout.GateCentre, outward);
            var across = Project(layout.GateCentre, lateral);

            corridor = FromFrame(outward, lateral,
                                 lip - IndustrialLayout.GateMargin, lip,
                                 across - GateCorridorHalfWidth, across + GateCorridorHalfWidth);
            return true;
        }

        /// <summary>
        /// Signed distance from the world origin along a cardinal unit axis.
        ///
        /// SIGNED is the whole point, and getting it wrong is silent. An earlier version read
        /// off point.x or point.z by asking only which axis Outward lay on - so for the half of
        /// all halls that face -X or -Z (IndustrialLayout.EmitRow alternates the two rows of
        /// every band) it measured the front of the building and the edge of its pad on the
        /// WRONG side of both. The arithmetic then collapses to a constant 1m forecourt on every
        /// one of them, every apron is dropped for being too shallow, and nothing anywhere says
        /// so. Project along the direction, never index by it.
        /// </summary>
        static float Project(Vector3 point, Vector3 axis) => point.x * axis.x + point.z * axis.z;

        static float Project(Vector2 point, Vector3 axis) => point.x * axis.x + point.y * axis.z;

        /// <summary>
        /// Builds a world rect from a span in the (outward, lateral) frame. Both are cardinal
        /// units and orthogonal, so two opposite corners fix the rectangle.
        /// </summary>
        static IndustrialLayout.Rect FromFrame(
            Vector3 outward, Vector3 lateral,
            float alongMin, float alongMax, float acrossMin, float acrossMax)
        {
            var a = ToWorld(outward, lateral, alongMin, acrossMin);
            var b = ToWorld(outward, lateral, alongMax, acrossMax);

            return new IndustrialLayout.Rect { Min = Vector2.Min(a, b), Max = Vector2.Max(a, b) };
        }

        static Vector2 ToWorld(Vector3 outward, Vector3 lateral, float along, float across) =>
            new Vector2(outward.x * along + lateral.x * across,
                        outward.z * along + lateral.z * across);

        static float Extent(Vector2 size, Vector3 direction) =>
            Mathf.Abs(direction.x) > 0.5f ? size.x : size.y;

        static IndustrialLayout.Rect Clip(IndustrialLayout.Rect rect, Vector2 min, Vector2 max) =>
            new IndustrialLayout.Rect
            {
                Min = new Vector2(Mathf.Max(rect.Min.x, min.x), Mathf.Max(rect.Min.y, min.y)),
                Max = new Vector2(Mathf.Min(rect.Max.x, max.x), Mathf.Min(rect.Max.y, max.y)),
            };

        /// <summary>
        /// One stockpile cut from the end of a host rect that lies further from the gate, each
        /// side clamped to a heap-sized maximum. Anchored at an end rather than centred so the
        /// remainder stays a single usable rectangle for the next pile instead of two slivers.
        /// </summary>
        static IndustrialLayout.Rect Stockpile(
            IndustrialLayout.Rect host, float maxSide, Vector2 anchor)
        {
            var size = new Vector2(Mathf.Min(host.Size.x, maxSide), Mathf.Min(host.Size.y, maxSide));

            var far = (host.Min - anchor).sqrMagnitude > (host.Max - anchor).sqrMagnitude;
            var min = far ? host.Min : host.Max - size;

            return new IndustrialLayout.Rect { Min = min, Max = min + size };
        }

        /// <summary>Cuts a plot of the given size out of one end of a host rect.</summary>
        static IndustrialLayout.Rect Anchor(
            IndustrialLayout.Rect host, float width, float depth, bool wide, bool flip)
        {
            var w = wide ? width : depth;
            var d = wide ? depth : width;

            w = Mathf.Min(w, host.Size.x);
            d = Mathf.Min(d, host.Size.y);

            var min = new Vector2(
                flip ? host.Max.x - w : host.Min.x,
                flip ? host.Max.y - d : host.Min.y);

            return new IndustrialLayout.Rect { Min = min, Max = min + new Vector2(w, d) };
        }

        /// <summary>
        /// The strip on the far side of an anchored plot - the direction the host rect's leftover
        /// ground lies in. Not clamped to anything: TryClaim shrinks it to what is actually free
        /// and drops it if that is nothing, which is the same discipline every other seed uses.
        /// </summary>
        static IndustrialLayout.Rect Beside(
            IndustrialLayout.Rect plot, float depth, bool wide, bool flip)
        {
            if (wide)
            {
                var y = flip ? plot.Min.y - depth : plot.Max.y;
                return new IndustrialLayout.Rect
                {
                    Min = new Vector2(plot.Min.x, y),
                    Max = new Vector2(plot.Max.x, y + depth),
                };
            }

            var x = flip ? plot.Min.x - depth : plot.Max.x;
            return new IndustrialLayout.Rect
            {
                Min = new Vector2(x, plot.Min.y),
                Max = new Vector2(x + depth, plot.Max.y),
            };
        }

        /// <summary>
        /// The axis a zone's props line up with when no door dictates it. Cardinal, because every
        /// rectangle in this layout is axis aligned and a stack at an odd angle reads as a bug.
        /// </summary>
        static Vector3 LongAxis(IndustrialLayout.Rect rect) =>
            rect.Size.x >= rect.Size.y ? Vector3.right : Vector3.forward;

        static Vector2 NearestCorner(IndustrialLayout.Rect wall, Vector2 point) =>
            new Vector2(
                point.x - wall.Min.x < wall.Max.x - point.x ? wall.Min.x : wall.Max.x,
                point.y - wall.Min.y < wall.Max.y - point.y ? wall.Min.y : wall.Max.y);

        // ---------------------------------------------------------------- occupancy grid

        /// <summary>
        /// Which ground inside the wall is still available. A coarse boolean grid rather than a
        /// rectangle set, because the free area is a rectangle MINUS several others and the
        /// difference of rectangles is not a rectangle - the grid is what makes "the largest
        /// thing that fits in what is left" a straightforward question instead of a clever one.
        /// </summary>
        sealed class Occupancy
        {
            public Vector2 Origin;
            public int Width;
            public int Height;

            bool[] blocked;

            public int FreeCells
            {
                get
                {
                    var free = 0;
                    for (var i = 0; i < blocked.Length; i++)
                        if (!blocked[i])
                            free++;
                    return free;
                }
            }

            public static Occupancy Build(IndustrialLayout.Layout layout, WorksHall[] halls)
            {
                var grid = Empty(layout.Wall);

                foreach (var road in layout.Roads)
                    grid.Reserve(road, Standoff);

                grid.ReserveHalls(halls);

                foreach (var run in layout.ParkingRuns)
                    grid.Reserve(BandOf(run, run.Width, ParkingBandDepth), Standoff);

                foreach (var stand in layout.LorryStands)
                    grid.Reserve(BandOf(stand, stand.Width, IndustrialLayout.LorryStandDepth), Standoff);

                if (TryGateCorridor(layout, out var corridor))
                    grid.Reserve(corridor);

                return grid;
            }

            /// <summary>
            /// The same grid rebuilt from what the WorksYard marker carries, for callers that run
            /// after generation and have the rectangles but not the Layout that produced them.
            /// </summary>
            public static Occupancy From(
                IndustrialLayout.Rect wall,
                IndustrialLayout.Rect[] lanes,
                IndustrialLayout.Rect[] bays,
                WorksHall[] halls)
            {
                var grid = Empty(wall);

                if (lanes != null)
                    foreach (var lane in lanes)
                        grid.Reserve(lane, Standoff);

                if (bays != null)
                    foreach (var bay in bays)
                        grid.Reserve(bay, Standoff);

                grid.ReserveHalls(halls);
                return grid;
            }

            static Occupancy Empty(IndustrialLayout.Rect wall)
            {
                var usable = new IndustrialLayout.Rect
                {
                    Min = wall.Min + Vector2.one * WallSkirt,
                    Max = wall.Max - Vector2.one * WallSkirt,
                };

                var size = usable.Size;
                var grid = new Occupancy
                {
                    Origin = usable.Min,
                    Width = Mathf.Max(0, Mathf.FloorToInt(size.x / Cell)),
                    Height = Mathf.Max(0, Mathf.FloorToInt(size.y / Cell)),
                };

                grid.blocked = new bool[Mathf.Max(1, grid.Width * grid.Height)];
                return grid;
            }

            void ReserveHalls(WorksHall[] halls)
            {
                if (halls == null)
                    return;

                foreach (var hall in halls)
                {
                    var lateral = Vector3.Cross(Vector3.up, hall.Outward);
                    var half = new Vector2(
                        Mathf.Abs(hall.Outward.x) * hall.HalfDepth + Mathf.Abs(lateral.x) * hall.HalfWidth,
                        Mathf.Abs(hall.Outward.z) * hall.HalfDepth + Mathf.Abs(lateral.z) * hall.HalfWidth);

                    var centre = new Vector2(hall.Centre.x, hall.Centre.z);
                    Reserve(new IndustrialLayout.Rect { Min = centre - half, Max = centre + half },
                            Standoff);
                }
            }

            /// <summary>
            /// The ground a bay run occupies: from its road edge, along the run for its width,
            /// and INWARD by the band depth - the direction ParkingLayout fills from, opposite
            /// to Outward, which points at the carriageway.
            /// </summary>
            internal static IndustrialLayout.Rect BandOf(
                IndustrialLayout.Bay bay, float width, float depth)
            {
                var a = bay.Origin + bay.Along * bay.Cursor;
                var b = a + bay.Along * width;
                var c = a - bay.Outward * depth;

                var min = new Vector2(Mathf.Min(Mathf.Min(a.x, b.x), c.x), Mathf.Min(Mathf.Min(a.z, b.z), c.z));
                var max = new Vector2(Mathf.Max(Mathf.Max(a.x, b.x), c.x), Mathf.Max(Mathf.Max(a.z, b.z), c.z));

                return new IndustrialLayout.Rect { Min = min, Max = max };
            }

            public void Reserve(IndustrialLayout.Rect rect, float margin = 0f)
            {
                var lo = ToCellFloor(new Vector2(rect.Min.x - margin, rect.Min.y - margin));
                var hi = ToCellCeil(new Vector2(rect.Max.x + margin, rect.Max.y + margin));

                for (var y = Mathf.Max(0, lo.y); y < Mathf.Min(Height, hi.y); y++)
                for (var x = Mathf.Max(0, lo.x); x < Mathf.Min(Width, hi.x); x++)
                    blocked[y * Width + x] = true;
            }

            public void Claim(IndustrialLayout.Rect rect) => Reserve(rect);

            /// <summary>
            /// Shrinks a proposed rect to the largest fully-free box INSIDE it, and claims it.
            /// False when nothing of the required size is left, in which case the zone is DROPPED
            /// rather than relocated - a zone whose position was derived from a door stops
            /// meaning anything once it is moved somewhere else.
            ///
            /// Largest-inside rather than grown-from-the-centre, and the difference is not
            /// academic. IndustrialLayout stands a lorry at the loading face of one hall per
            /// compound, dead centre of the ground its apron wants; a centre-outward search finds
            /// that first cell taken and abandons the whole apron, so the one hall in the yard
            /// that visibly IS a loading bay was the one hall with no loading apron. Taking the
            /// larger remaining side instead keeps the apron and lets the lorry stand in it,
            /// which is what a lorry is for.
            /// </summary>
            public bool TryClaim(ref IndustrialLayout.Rect rect, float minWidth, float minDepth)
            {
                var lo = ToCellCeil(rect.Min);
                var hi = ToCellFloor(rect.Max);

                lo = new Vector2Int(Mathf.Max(0, lo.x), Mathf.Max(0, lo.y));
                hi = new Vector2Int(Mathf.Min(Width, hi.x), Mathf.Min(Height, hi.y));

                var minW = Mathf.Max(1, Mathf.CeilToInt((minWidth - 0.001f) / Cell));
                var minH = Mathf.Max(1, Mathf.CeilToInt((minDepth - 0.001f) / Cell));

                if (hi.x - lo.x < minW || hi.y - lo.y < minH)
                    return false;

                if (!LargestFreeBox(lo, hi, minW, minH, out var found))
                    return false;

                rect = found;
                Claim(found);
                return true;
            }

            /// <summary>
            /// Largest all-free cell box within a window, by the standard
            /// largest-rectangle-in-histogram scan - one pass per row, with a stack of candidate
            /// left edges. Ties break on cell index so the answer is a function of the grid alone.
            /// </summary>
            bool LargestFreeBox(
                Vector2Int lo, Vector2Int hi, int minW, int minH, out IndustrialLayout.Rect best)
            {
                best = default;

                var span = hi.x - lo.x;
                var heights = new int[span];
                var stack = new int[span + 1];

                var bestArea = 0;
                var bestKey = int.MaxValue;
                var found = false;

                for (var y = lo.y; y < hi.y; y++)
                {
                    for (var i = 0; i < span; i++)
                        heights[i] = blocked[y * Width + lo.x + i] ? 0 : heights[i] + 1;

                    var top = 0;
                    for (var i = 0; i <= span; i++)
                    {
                        var h = i < span ? heights[i] : 0;

                        while (top > 0 && heights[stack[top - 1]] >= h)
                        {
                            var height = heights[stack[--top]];
                            var left = top > 0 ? stack[top - 1] + 1 : 0;
                            var width = i - left;

                            if (height < minH || width < minW)
                                continue;

                            var area = height * width;
                            var key = (y - height + 1) * Width + lo.x + left;

                            if (area > bestArea || (area == bestArea && key < bestKey))
                            {
                                bestArea = area;
                                bestKey = key;
                                best = ToRect(lo.x + left, y - height + 1, lo.x + left + width - 1, y);
                                found = true;
                            }
                        }

                        stack[top++] = i;
                    }
                }

                return found;
            }

            public bool TryFurthestFreeRect(
                Vector2 anchor, float minWidth, float minDepth, out IndustrialLayout.Rect best) =>
                TryBestFreeRect(anchor, minWidth, minDepth, byArea: false, out best);

            public bool TryLargestFreeRect(
                Vector2 anchor, float minWidth, float minDepth, out IndustrialLayout.Rect best) =>
                TryBestFreeRect(anchor, minWidth, minDepth, byArea: true, out best);

            /// <summary>
            /// The best free rectangle among all maximal ones meeting a minimum size, by one of
            /// two objectives. Maximal rectangles come from the standard
            /// largest-rectangle-in-histogram scan, one pass per row.
            ///
            /// Two objectives because the zones want opposite things and one of them nearly went
            /// unnoticed. A scrap corner wants the FURTHEST ground and does not care how big it
            /// is. Bulk storage wants the BIGGEST, and asking it for the furthest instead handed
            /// it a succession of small far corners while whole empty rows sat untouched - six
            /// stockpiles that between them covered under a third of the yard. Distance is
            /// already handled for it by ordering: staging has claimed the gate end and the scrap
            /// corner the far one before this is ever asked.
            ///
            /// Ties break on cell index, not on iteration order, so the answer is a function of
            /// the grid alone - two runs of the same seed must not disagree because a scan
            /// visited rectangles in a different order.
            /// </summary>
            bool TryBestFreeRect(
                Vector2 anchor, float minWidth, float minDepth, bool byArea,
                out IndustrialLayout.Rect best)
            {
                best = default;

                var minW = Mathf.CeilToInt(minWidth / Cell);
                var minH = Mathf.CeilToInt(minDepth / Cell);
                if (minW <= 0 || minH <= 0 || Width < minW || Height < minH)
                    return false;

                var heights = new int[Width];
                var stack = new int[Width + 1];

                var bestScore = float.NegativeInfinity;
                var bestKey = int.MaxValue;
                var found = false;

                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                        heights[x] = blocked[y * Width + x] ? 0 : heights[x] + 1;

                    var top = 0;
                    for (var x = 0; x <= Width; x++)
                    {
                        var h = x < Width ? heights[x] : 0;

                        while (top > 0 && heights[stack[top - 1]] >= h)
                        {
                            var height = heights[stack[--top]];
                            var left = top > 0 ? stack[top - 1] + 1 : 0;
                            var right = x - 1;

                            if (height < minH || right - left + 1 < minW)
                                continue;

                            var rect = ToRect(left, y - height + 1, right, y);
                            var score = byArea
                                ? rect.Size.x * rect.Size.y
                                : Vector2.SqrMagnitude(rect.Centre - anchor);
                            var key = (y - height + 1) * Width + left;

                            if (score > bestScore || (Mathf.Approximately(score, bestScore) && key < bestKey))
                            {
                                bestScore = score;
                                bestKey = key;
                                best = rect;
                                found = true;
                            }
                        }

                        stack[top++] = x;
                    }
                }

                return found;
            }

            IndustrialLayout.Rect ToRect(int x0, int y0, int x1, int y1) => new IndustrialLayout.Rect
            {
                Min = Origin + new Vector2(x0 * Cell, y0 * Cell),
                Max = Origin + new Vector2((x1 + 1) * Cell, (y1 + 1) * Cell),
            };

            Vector2Int ToCellFloor(Vector2 world) => new Vector2Int(
                Mathf.FloorToInt((world.x - Origin.x) / Cell),
                Mathf.FloorToInt((world.y - Origin.y) / Cell));

            Vector2Int ToCellCeil(Vector2 world) => new Vector2Int(
                Mathf.CeilToInt((world.x - Origin.x) / Cell),
                Mathf.CeilToInt((world.y - Origin.y) / Cell));
        }
    }
}
