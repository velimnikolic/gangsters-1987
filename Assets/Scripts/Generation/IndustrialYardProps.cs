using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// Where the loose material stands inside a works yard: the slots, not the prefabs.
    ///
    /// This is the pass IndustrialLotPlanner was written for and IndustrialLotBuilder stopped
    /// short of. The planner already computes two fields nothing read - <see cref="LotZone.Facing"/>
    /// ("props align to this - a works stacks things square to something, never at 17 degrees")
    /// and <see cref="LotZone.Density"/> ("share of the lot's prop budget") - and
    /// IndustrialLotConfig already carries a maxPropsPerLot whose own tooltip says it is "read by
    /// the prop pass". All three arrive here.
    ///
    /// Pure geometry, holding no UnityEngine.Object, for the same reason IndustrialLayout and
    /// IndustrialLotPlanner hold none: IndustrialLotTests runs it in a bare .NET host with no
    /// Editor and no Play mode, which is the only way "no prop ever lands on a carriageway" gets
    /// asserted rather than eyeballed. IndustrialLotBuilder does the Instantiate half.
    ///
    /// The arrangement is deliberately NOT a scatter. IndustrialDresser's header records what
    /// happened the last time a works yard was filled by drawing a random prefab at a random yaw
    /// at uniform density - "no two adjacent items agreed about anything, which is what reads as
    /// tipped out" - and the fix there was to lay one prefab in a short row against a wall. Same
    /// rule here, one step further: every zone kind gets the arrangement its own name implies, and
    /// only the scrap corner is allowed to be untidy.
    /// </summary>
    public static class IndustrialYardProps
    {
        /// <summary>
        /// Kept clear inside the zone's own edge. Half a metre, matching the planner's Standoff:
        /// a zone may sit flush against a hall wall or a kerb, and a barrel centred on the very
        /// edge of one would then hang over it.
        /// </summary>
        const float EdgeMargin = 0.5f;

        /// <summary>Between two pieces in the same rank. Stored touching, near enough.</summary>
        const float Gap = 0.15f;

        /// <summary>
        /// Millimetre of tolerance on every fit test, so a band sized to exactly one item is not
        /// decided by the last bit of a float. See Block, where leaving it out cost two zone kinds
        /// their entire contents.
        /// </summary>
        const float Slack = 0.001f;

        /// <summary>Between two blocks of the same stockpile - room to walk down.</summary>
        const float Aisle = 1.5f;

        /// <summary>
        /// Front of the nearest rank to the dock face. The doors have to open and a hand truck
        /// has to get past; this is the same order of setback IndustrialDresser keeps between a
        /// hall front and the carriageway.
        /// </summary>
        const float DockSetback = 1.5f;

        /// <summary>
        /// Share of an apron's width, at each end, that may be stacked. The middle third is the
        /// door-to-road corridor and stays empty - which is the whole reason an apron is not just
        /// another stockpile.
        /// </summary>
        const float ApronEndShare = 1f / 3f;

        /// <summary>Ranks deep an apron may stack. Two is a staging area; three is a warehouse.</summary>
        const int ApronRanks = 2;

        /// <summary>Pieces per side of one stockpile block, before the aisle.</summary>
        const int BulkBlockRows = 3;
        const int BulkBlockCols = 4;

        /// <summary>
        /// Longest a single-rank zone's row may run, in pieces.
        ///
        /// Staging is short because the zone is standing room for a lorry that is waiting, and a
        /// row spanning its full width reads as a fence rather than as goods. The boiler house
        /// gets more because its rank IS its contents - a fuel store against a wall.
        /// </summary>
        const int StagingRank = 5;
        const int BoilerRank = 8;

        /// <summary>How much of a cinder yard's grid actually gets a piece. Ash, not stock.</summary>
        const float CinderKeep = 0.35f;

        /// <summary>How much of a scrap cluster gets dropped, so the heap has holes in it.</summary>
        const float ScrapDrop = 0.2f;

        /// <summary>Widest a scrap piece may sit off its aligned yaw. The one untidy zone.</summary>
        const float ScrapYawJitter = 15f;

        /// <summary>And how far off its slot, in metres.</summary>
        const float ScrapDrift = 0.25f;

        /// <summary>One piece of material, standing on the yard.</summary>
        public struct Slot
        {
            /// <summary>World XZ of the footprint centre.</summary>
            public Vector2 Centre;

            /// <summary>Degrees. The zone's own facing, or square to it, or - in scrap - near it.</summary>
            public float Yaw;

            /// <summary>
            /// Which arrangement this belongs to. Slots sharing a group were laid as one rank or
            /// one block and should be given the same prefab: a heap of one thing reads as stock,
            /// a heap of five reads as rubbish.
            /// </summary>
            public int Group;
        }

        /// <summary>
        /// Everything outside the zone that a slot has to keep off. Lanes and bays come straight
        /// off the WorksYard marker; obstacles are what IndustrialDresser stood inside the
        /// compound AFTER the zones were planned - the stacks beside each hall, the chimneys and
        /// the outbuildings - which the partition could not have known about.
        /// </summary>
        public struct Site
        {
            public IndustrialLayout.Rect[] Lanes;
            public IndustrialLayout.Rect[] Bays;
            public IndustrialLayout.Rect[] Obstacles;

            /// <summary>
            /// World XZ of the gate, or of the wall centre on a gateless compound. Only the scrap
            /// corner uses it, and only to pick which corner of itself to pile into: scrap goes
            /// where nobody drives, which is as far from the gate as the zone reaches.
            /// </summary>
            public Vector2 Gate;
        }

        /// <summary>
        /// The yaw every piece in a zone is laid at: the zone's facing, snapped to a right angle.
        ///
        /// Exposed because the caller has to measure its prefab at this yaw before it can ask for
        /// slots - PrefabBounds.FootprintXZ swaps x and z on a quarter turn - and deriving it
        /// twice in two places is how the two answers drift apart.
        /// </summary>
        public static float YawFor(LotZone zone)
        {
            if (zone.Facing.sqrMagnitude < 0.25f)
                return 0f;

            return Mathf.Round(
                Mathf.Atan2(zone.Facing.x, zone.Facing.z) * Mathf.Rad2Deg / 90f) * 90f;
        }

        /// <summary>
        /// Splits one lot's prop ceiling across its zones, by Density x area.
        ///
        /// Density alone is wrong and area alone is wrong. Density alone gives a 6m cinder strip
        /// the same allowance as a 20m stockpile; area alone fills the cinder yard, whose whole
        /// character is that it is nearly empty. The product is what the two fields were
        /// evidently meant to be read as together.
        ///
        /// Floors rather than rounds, so the sum can only come in UNDER the ceiling. A budget
        /// that overshoots its own cap by a couple of pieces per yard would make
        /// IndustrialLotConfig.maxPropsPerLot a suggestion, and the point of a hard ceiling is
        /// that it is one.
        /// </summary>
        public static int[] Budgets(LotZone[] zones, int total)
        {
            var count = zones?.Length ?? 0;
            var result = new int[count];

            if (count == 0 || total <= 0)
                return result;

            var weights = new float[count];
            var sum = 0f;

            for (var i = 0; i < count; i++)
            {
                var size = zones[i].Area.Size;
                weights[i] = Mathf.Max(0f, zones[i].Density)
                           * Mathf.Max(0f, size.x) * Mathf.Max(0f, size.y);
                sum += weights[i];
            }

            if (sum <= 0f)
                return result;

            var spent = 0;

            for (var i = 0; i < count; i++)
            {
                result[i] = Mathf.FloorToInt(total * weights[i] / sum);
                spent += result[i];
            }

            // Then one piece each to the zones the flooring wiped out, while the ceiling has room.
            //
            // Without this the smallest zone kind never gets anything at all. A cinder yard is
            // 3.5m deep and carries the lowest density the planner emits, 0.15, so on a compound
            // with a dozen zones its share floors to zero - and "sparsest" quietly became "empty
            // in every works in the city". Rounding is not the place to decide a zone kind does
            // not exist; that is what Density is for, and Density says 0.15, not 0.
            for (var i = 0; i < count && spent < total; i++)
            {
                if (result[i] > 0 || weights[i] <= 0f)
                    continue;

                result[i] = 1;
                spent++;
            }

            return result;
        }

        /// <summary>
        /// Lays out one zone.
        ///
        /// <paramref name="propSize"/> is the WORLD XZ footprint of the piece that will stand in
        /// every slot, measured at <see cref="YawFor"/>. One size per zone rather than one per
        /// piece, because one prefab per group is the arrangement rule - and it also means the
        /// packing is a plain grid instead of a bin-packer, which is the difference between a
        /// yard that reads as stacked and one that reads as tessellated.
        ///
        /// Returns fewer slots than <paramref name="budget"/> whenever the zone is small, the
        /// piece is large, or something already stands in the way. That is not a failure: a works
        /// needs turning room, and the planner already caps zone coverage at half the free ground
        /// for the same reason.
        /// </summary>
        public static List<Slot> Plan(
            LotZone zone, Site site, Vector2 propSize, int budget, System.Random rng)
        {
            var slots = new List<Slot>();

            if (budget <= 0 || rng == null)
                return slots;

            if (propSize.x <= 0.01f || propSize.y <= 0.01f)
                return slots;

            var yaw = YawFor(zone);

            // Local frame: d runs along the zone's facing, c across it. Both are unit world axes,
            // because every Facing the planner emits is a hall outward or a long-axis unit vector.
            var f = new Vector2(zone.Facing.x, zone.Facing.z);
            if (f.sqrMagnitude < 0.25f)
                f = new Vector2(0f, 1f);

            f = new Vector2(Mathf.Round(f.x), Mathf.Round(f.y));
            var c = new Vector2(f.y, -f.x);

            var area = zone.Area;
            var size = area.Size;

            var halfDepth = (Mathf.Abs(f.x) * size.x + Mathf.Abs(f.y) * size.y) * 0.5f - EdgeMargin;
            var halfCross = (Mathf.Abs(c.x) * size.x + Mathf.Abs(c.y) * size.y) * 0.5f - EdgeMargin;

            if (halfDepth <= 0f || halfCross <= 0f)
                return slots;

            var dSize = Mathf.Abs(f.x) * propSize.x + Mathf.Abs(f.y) * propSize.y;
            var cSize = Mathf.Abs(c.x) * propSize.x + Mathf.Abs(c.y) * propSize.y;

            if (dSize > halfDepth * 2f + Slack || cSize > halfCross * 2f + Slack)
                return slots;

            var pack = new Pack
            {
                Origin = area.Centre,
                Forward = f,
                Cross = c,
                DSize = dSize,
                CSize = cSize,
                HalfDepth = halfDepth,
                HalfCross = halfCross,
                Yaw = yaw,
                Budget = budget,
                Site = site,
                Rng = rng,
            };

            switch (zone.Kind)
            {
                case LotZoneKind.LoadingApron: Apron(ref pack, slots); break;
                case LotZoneKind.TruckStaging: EdgeRank(ref pack, slots, StagingRank); break;
                case LotZoneKind.RawMaterialYard: Stockpile(ref pack, slots); break;
                case LotZoneKind.BoilerHouse: EdgeRank(ref pack, slots, BoilerRank); break;
                case LotZoneKind.CinderYard: Sparse(ref pack, slots); break;
                case LotZoneKind.ScrapCorner: Heap(ref pack, slots); break;
            }

            return slots;
        }

        // ------------------------------------------------------------------ arrangements

        /// <summary>
        /// Ranks parallel to the dock face, at BOTH ends of the apron, with the middle third left
        /// empty.
        ///
        /// The empty middle is the point. An apron is the ground between a hall's doors and the
        /// carriageway, so anything standing across the centre of it is standing in the doorway -
        /// and the obvious layout, a tidy rank straight along the dock face, does exactly that.
        /// Goods wait beside the doors, not in front of them.
        ///
        /// Facing on an apron is -hall.Outward, so the dock face is the +d edge and the road is
        /// at -d. Ranks therefore start at the +d end and step back towards the road.
        /// </summary>
        static void Apron(ref Pack pack, List<Slot> slots)
        {
            var endSpan = pack.HalfCross * 2f * ApronEndShare;

            // A narrow apron has no outer thirds worth the name. Rather than drop the zone, give
            // the whole width to one rank: the corridor an apron protects is the ground in FRONT
            // of the doors, and on a hall this narrow the setback already provides it.
            if (endSpan < pack.CSize)
            {
                Block(ref pack, pack.HalfDepth - DockSetback - pack.DSize,
                      pack.HalfDepth - DockSetback, -pack.HalfCross, pack.HalfCross,
                      1, int.MaxValue, 0, slots);
                return;
            }

            var dStart = pack.HalfDepth - DockSetback;

            // The band is sized to the ranks rather than to the zone, because Block CENTRES what
            // it lays inside the band it is given. Handed the whole apron it would put the stock
            // in the middle of the ground the lorry uses, which is the one place it must not go.
            var dNeeded = ApronRanks * (pack.DSize + Gap) - Gap;

            for (var end = 0; end < 2; end++)
            {
                var far = end == 1;
                var cMin = far ? pack.HalfCross - endSpan : -pack.HalfCross;
                var cMax = far ? pack.HalfCross : -pack.HalfCross + endSpan;

                Block(ref pack, dStart - dNeeded, dStart, cMin, cMax,
                      ApronRanks, int.MaxValue, end, slots);
            }
        }

        /// <summary>
        /// A single rank hard against one long edge, leaving the rest of the zone open.
        ///
        /// Truck staging and a boiler house want the same shape for opposite reasons: staging is
        /// ground a lorry has to turn on, and a boiler house is a fuel store that has to be
        /// wheeled from. Filling either of them would be the mistake.
        /// </summary>
        static void EdgeRank(ref Pack pack, List<Slot> slots, int maxCols)
        {
            // The +d edge, so the rank sits behind the zone rather than across its mouth: staging
            // faces the gate and a boiler yard faces its lane, and both of those are the side
            // something arrives from.
            //
            // Capped in LENGTH, which is the whole difference between a group of goods and a
            // fence. Uncapped, a wide staging area came out as a single unbroken row of
            // twenty-five barrels down its full width - measurably "not empty", and exactly the
            // wrong picture for the one zone whose character is that a lorry can turn on it.
            Block(ref pack, pack.HalfDepth - pack.DSize, pack.HalfDepth,
                  -pack.HalfCross, pack.HalfCross, 1, maxCols, 0, slots);
        }

        /// <summary>
        /// Blocks of stock on a grid, with an aisle between them. Bulk stored in bulk.
        ///
        /// Blocked rather than laid as one solid raft because a 20m stockpile of barrels with no
        /// way into it is a texture, not a yard. The block size is in pieces rather than metres so
        /// a rank of barrels and a rank of crates both come out as something you could walk round.
        /// </summary>
        static void Stockpile(ref Pack pack, List<Slot> slots)
        {
            var dPitch = pack.DSize + Gap;
            var cPitch = pack.CSize + Gap;

            var dBlock = BulkBlockRows * dPitch - Gap;
            var cBlock = BulkBlockCols * cPitch - Gap;

            var group = 0;

            for (var d = -pack.HalfDepth; d + dBlock <= pack.HalfDepth + 0.001f; d += dBlock + Aisle)
            for (var c = -pack.HalfCross; c + cBlock <= pack.HalfCross + 0.001f; c += cBlock + Aisle)
            {
                Block(ref pack, d, d + dBlock, c, c + cBlock,
                      BulkBlockRows, BulkBlockCols, group++, slots);

                if (slots.Count >= pack.Budget)
                    return;
            }

            // A zone too narrow for even one block still gets its rank, rather than nothing. The
            // planner's minYardSize is 8m and a shelving rack is over 2m, so this fires.
            if (slots.Count == 0)
                EdgeRank(ref pack, slots, BulkBlockCols);
        }

        /// <summary>
        /// The full grid, thinned. Density 0.15 is the lowest the planner emits and this is the
        /// zone it emits it for - a cinder yard is ash with a few bins standing in it, and the
        /// ground pass has already done the work of making it read.
        /// </summary>
        static void Sparse(ref Pack pack, List<Slot> slots)
        {
            var candidates = new List<Slot>();

            Block(ref pack, -pack.HalfDepth, pack.HalfDepth, -pack.HalfCross, pack.HalfCross,
                  int.MaxValue, int.MaxValue, 0, candidates, register: false);

            foreach (var slot in candidates)
            {
                // Rolled for every candidate whether or not it is kept, so a rejection cannot
                // shift what the next one draws - the discipline IndustrialLotBuilder states.
                var keep = pack.Rng.NextDouble() < CinderKeep;

                if (slots.Count >= pack.Budget)
                    return;

                if (keep)
                    Accept(ref pack, slot, slots);
            }
        }

        /// <summary>
        /// The one untidy zone: a loose heap in the corner furthest from the gate.
        ///
        /// Everything else here is square to its facing on purpose, and scrap is what proves the
        /// rule rather than what breaks it - the drift and the yaw jitter are small enough that
        /// the pile still reads as having been put somewhere, and the dropped pieces are what
        /// stop it reading as a grid with noise added.
        /// </summary>
        static void Heap(ref Pack pack, List<Slot> slots)
        {
            // Which corner. Measured in the local frame so the answer is a sign per axis.
            var toGate = pack.Site.Gate - pack.Origin;
            var dSign = Vector2.Dot(toGate, pack.Forward) >= 0f ? -1f : 1f;
            var cSign = Vector2.Dot(toGate, pack.Cross) >= 0f ? -1f : 1f;

            // Half the zone, anchored at that corner: a heap, not a floor covering.
            var dHalf = pack.HalfDepth;
            var cHalf = pack.HalfCross;

            var dLo = dSign > 0f ? 0f : -dHalf;
            var dHi = dSign > 0f ? dHalf : 0f;
            var cLo = cSign > 0f ? 0f : -cHalf;
            var cHi = cSign > 0f ? cHalf : 0f;

            var candidates = new List<Slot>();
            Block(ref pack, dLo, dHi, cLo, cHi, int.MaxValue, int.MaxValue, 0,
                  candidates, register: false);

            foreach (var slot in candidates)
            {
                var drop = pack.Rng.NextDouble() < ScrapDrop;
                var driftD = ((float)pack.Rng.NextDouble() * 2f - 1f) * ScrapDrift;
                var driftC = ((float)pack.Rng.NextDouble() * 2f - 1f) * ScrapDrift;
                var jitter = ((float)pack.Rng.NextDouble() * 2f - 1f) * ScrapYawJitter;

                if (slots.Count >= pack.Budget)
                    return;

                if (drop)
                    continue;

                var moved = new Slot
                {
                    Centre = slot.Centre + pack.Forward * driftD + pack.Cross * driftC,
                    Yaw = slot.Yaw + jitter,
                    Group = slot.Group,
                };

                Accept(ref pack, moved, slots);
            }
        }

        // ------------------------------------------------------------------ packing

        /// <summary>Everything the arrangements share, so they read as one page of geometry.</summary>
        struct Pack
        {
            public Vector2 Origin;
            public Vector2 Forward;
            public Vector2 Cross;
            public float DSize;
            public float CSize;
            public float HalfDepth;
            public float HalfCross;
            public float Yaw;
            public int Budget;
            public Site Site;
            public System.Random Rng;
        }

        /// <summary>
        /// Fills one sub-rectangle of the zone, in local d/c coordinates, with a centred grid.
        ///
        /// Centred rather than corner-anchored so a run that does not divide evenly leaves its
        /// slack at both ends instead of all of it at one - which is the difference between a rank
        /// that looks placed and one that looks pushed.
        /// </summary>
        static void Block(
            ref Pack pack, float dLo, float dHi, float cLo, float cHi,
            int maxRows, int maxCols, int group, List<Slot> slots, bool register = true)
        {
            dLo = Mathf.Max(dLo, -pack.HalfDepth);
            dHi = Mathf.Min(dHi, pack.HalfDepth);
            cLo = Mathf.Max(cLo, -pack.HalfCross);
            cHi = Mathf.Min(cHi, pack.HalfCross);

            var dSpan = dHi - dLo;
            var cSpan = cHi - cLo;

            if (dSpan < pack.DSize - Slack || cSpan < pack.CSize - Slack)
                return;

            var dPitch = pack.DSize + Gap;
            var cPitch = pack.CSize + Gap;

            // Slack, and it is load bearing rather than defensive. A single-rank band is handed to
            // this method as EXACTLY one item deep, so the division lands on a whole number and
            // floating point decides which side of it - 0.9999 floors to zero and the rank is
            // dropped in silence. Not hypothetical: it is what gave truck staging and every boiler
            // house in the city no props at all across 220 zones, while every assertion passed,
            // because an empty list violates no ceiling.
            var rows = Mathf.Min(maxRows, Mathf.FloorToInt((dSpan + Gap) / dPitch + Slack));
            var cols = Mathf.Min(maxCols, Mathf.FloorToInt((cSpan + Gap) / cPitch + Slack));

            if (rows <= 0 || cols <= 0)
                return;

            var dRun = rows * dPitch - Gap;
            var cRun = cols * cPitch - Gap;

            var dStart = dLo + (dSpan - dRun) * 0.5f + pack.DSize * 0.5f;
            var cStart = cLo + (cSpan - cRun) * 0.5f + pack.CSize * 0.5f;

            for (var row = 0; row < rows; row++)
            for (var col = 0; col < cols; col++)
            {
                // Tested BEFORE the accept, not after. Checked afterwards, a second Block call
                // entered with the list already full takes one more piece before it notices - so
                // an apron, which calls this twice, could come in a piece over its quota and
                // maxPropsPerLot would stop being a ceiling.
                if (register && slots.Count >= pack.Budget)
                    return;

                var slot = new Slot
                {
                    Centre = pack.Origin
                           + pack.Forward * (dStart + row * dPitch)
                           + pack.Cross * (cStart + col * cPitch),
                    Yaw = pack.Yaw,
                    Group = group,
                };

                if (!register)
                {
                    slots.Add(slot);
                    continue;
                }

                if (Accept(ref pack, slot, slots) && slots.Count >= pack.Budget)
                    return;
            }
        }

        /// <summary>
        /// Takes a slot if nothing already stands there. The footprint is the piece's own, so a
        /// long crate rejects along its length rather than round a circle - which matters at the
        /// 0.15m gap the ranks are packed at.
        /// </summary>
        static bool Accept(ref Pack pack, Slot slot, List<Slot> slots)
        {
            var half = new Vector2(
                Mathf.Abs(pack.Forward.x) * pack.DSize + Mathf.Abs(pack.Cross.x) * pack.CSize,
                Mathf.Abs(pack.Forward.y) * pack.DSize + Mathf.Abs(pack.Cross.y) * pack.CSize) * 0.5f;

            var rect = new IndustrialLayout.Rect
            {
                Min = slot.Centre - half,
                Max = slot.Centre + half,
            };

            if (Blocked(rect, pack.Site.Lanes) ||
                Blocked(rect, pack.Site.Bays) ||
                Blocked(rect, pack.Site.Obstacles))
                return false;

            foreach (var taken in slots)
            {
                var other = new IndustrialLayout.Rect
                {
                    Min = taken.Centre - half,
                    Max = taken.Centre + half,
                };

                if (Overlaps(rect, other))
                    return false;
            }

            slots.Add(slot);
            return true;
        }

        static bool Blocked(IndustrialLayout.Rect rect, IndustrialLayout.Rect[] against)
        {
            if (against == null)
                return false;

            foreach (var other in against)
                if (Overlaps(rect, other))
                    return true;

            return false;
        }

        /// <summary>
        /// Tolerant by a millimetre, the same as IndustrialLotTests: two rectangles sharing an
        /// edge are abutting, and a rank stacked flush against a kerb is exactly what is wanted.
        /// </summary>
        static bool Overlaps(IndustrialLayout.Rect a, IndustrialLayout.Rect b) =>
            a.Min.x < b.Max.x - 0.001f && b.Min.x < a.Max.x - 0.001f &&
            a.Min.y < b.Max.y - 0.001f && b.Min.y < a.Max.y - 0.001f;
    }
}
