using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// Where the water, the quay, the warehouses and the container stacks go on a port block.
    /// Pure geometry, like IndustrialLayout and for the same reason: PortDresser stands the
    /// cranes and moors the ship, GroundPlacer sinks the water and lays the quay, and both have
    /// to read the SAME answer - a ship moored half a metre up its own quay is visible from the
    /// default camera.
    ///
    /// The shape being built is a working waterfront, and it is asymmetric in a way a works
    /// compound is not. A works is rows around its roads; a port is a gradient, back to front:
    /// street, wall, warehouses, open apron, container stacks, quay, water. Everything faces the
    /// water, and the water is not on the block at all - it is laid OUTSIDE the map outline,
    /// which is why ZonePlanner only ever gives this zone a block touching the boundary.
    ///
    /// Deterministic in (seed, blockId) ALONE - the same contract IndustrialLayout holds, held
    /// the same way: an own stream from SeedOffsets.Port, drawn in a fixed order.
    /// </summary>
    public static class PortLayout
    {
        /// <summary>
        /// Depth of the concrete strip along the water, outline inward. Sized for the crane:
        /// crane-port is 4.39m deep, stood with 2m of coping outside it, and inland of it the
        /// strip still holds the lane the shift walks - see QuayLaneInset.
        /// </summary>
        public const float QuayDepth = 14f;

        /// <summary>
        /// How far the water runs out from the map outline, in cell rows. Two rows is 78m -
        /// far enough that the 45 degree camera never sees the far edge of the sea from any
        /// angle that also shows the quay. One number on purpose: if a camera sweep ever
        /// catches the horizon, this is the only thing to raise.
        /// </summary>
        public const int WaterRows = 2;

        /// <summary>
        /// How far past each end of the block the water runs, so the sea reads as continuing
        /// rather than as a pool cut to the block's width - and so the ship, which is longer
        /// than its berth, has water under both ends.
        /// </summary>
        public const float WaterOverhang = CityGrid.CellSize;

        /// <summary>
        /// How far the water surface sits below the city's y=0. The quay coping standing above
        /// the water is what makes it a quay; flush water read as flooding. 2.5 rather than
        /// the first pass's 1.2 by request - the drop should read from the default camera,
        /// not only up close - and it is still well inside the 6m the covering skirt hangs:
        /// the block slab is the BORDERED concrete tile, whose skirt drops down the outline
        /// exactly where the face is, and the road tiles ending at the water carry the same
        /// 6m skirt of their own.
        /// </summary>
        public const float WaterDrop = 2.5f;

        /// <summary>
        /// Centre of the moored ship, outline outward. ship-cargo is 16.64 in the beam, so 10
        /// stands its near hull face about 1.7m off the coping - touching distance, fenders
        /// implied, without the hull clipping the quay.
        /// </summary>
        public const float ShipStandoff = 10f;

        /// <summary>Depth of one container band: a box is 6m on its long side, plus lashing room.</summary>
        public const float StackBandDepth = 8f;

        /// <summary>Open water-side lane between the stacks and the quay strip.</summary>
        public const float StackGap = 1f;

        /// <summary>Working lane between two stack bands - a forklift's width and swing.</summary>
        public const float BandLane = 7f;

        /// <summary>Bands a deep block may fill before the rest stays open apron.</summary>
        public const int MaxStackBands = 3;

        /// <summary>One crane per this much berth.</summary>
        public const float CraneSpacing = 35f;

        /// <summary>
        /// Depth of one warehouse row, measured against the catalogue the way RowDepth is:
        /// industry-warehouse is 17.55m deep and building-port-sea 20m, and the dresser pushes
        /// both back off the apron by a setback - so anything under 22 silently excludes the
        /// port building from its own port.
        /// </summary>
        public const float RowDepth = 22f;

        /// <summary>Target width of one warehouse pad - same figure the works uses, same sweep.</summary>
        public const float TargetPadWidth = 26f;

        /// <summary>Wall to the first pad, and the back margin behind the row.</summary>
        public const float EdgeMargin = 3f;

        /// <summary>Gap left in the wall. Same two-lanes-and-overhang figure as the works gate.</summary>
        public const float GateWidth = IndustrialLayout.GateWidth;

        /// <summary>
        /// The central aisle the stacks part around, lined up with the gate: it is the one
        /// route from the gate to the quay, and the route the worker walk graph uses to cross
        /// the stack band without clipping a container corner.
        /// </summary>
        public const float AisleWidth = 8f;

        /// <summary>The two lanes the shift walks, as insets. See WorkLane in the marker.</summary>
        public const float QuayLaneInset = 11.5f;   // outline inward - behind the cranes.
        public const float ApronLaneOffset = 2.5f;  // in front of the warehouse doors.

        /// <summary>A pier finger only fits beside the ship on a two-cell quay.</summary>
        public const float MinSpanForPier = 90f;

        /// <summary>pier-tile-straight is a 4m module; six of them outreach boat-fishing's 21m.</summary>
        public const int PierSegments = 6;
        public const float PierModule = 4f;

        /// <summary>Spreads consecutive block ids apart in the seed space - see BlockLots.</summary>
        const int BlockStride = 397;

        /// <summary>One container stack: a run of one colour, side by side along the quay.</summary>
        public struct Stack
        {
            public IndustrialLayout.Rect Area;

            /// <summary>Unit vector along the run - the direction boxes are ranked in.</summary>
            public Vector3 Along;
        }

        /// <summary>A crane station on the quay strip, long axis along the water.</summary>
        public struct CraneSpot
        {
            public Vector3 Centre;

            /// <summary>Toward the water - the face the hook works over.</summary>
            public Vector3 Outward;
        }

        public sealed class Layout
        {
            /// <summary>The wall line, inset on the street sides. The quay side stays open.</summary>
            public IndustrialLayout.Rect Wall;

            /// <summary>The side the water is on - always one of the block's map-edge sides.</summary>
            public Sides QuaySide;

            /// <summary>Unit vector out of the block through the quay, over the water.</summary>
            public Vector3 Seaward;

            public bool HasGate;
            public Vector3 GateCentre;
            public Vector3 GateOutward;

            /// <summary>Sea rectangles, all outside the map outline, laid at -WaterDrop.</summary>
            public readonly List<IndustrialLayout.Rect> Water = new();

            /// <summary>The concrete strip along the water, on the block.</summary>
            public IndustrialLayout.Rect Quay;

            /// <summary>Warehouse ground, fronts toward the water.</summary>
            public readonly List<IndustrialLayout.Pad> Pads = new();

            public readonly List<Stack> Stacks = new();
            public readonly List<CraneSpot> Cranes = new();

            /// <summary>Centre of the moored ship, in the water. Length runs along the quay.</summary>
            public bool HasBerth;
            public Vector3 BerthCentre;
            public float BerthYaw;

            /// <summary>Root of the pier finger, on the outline; it runs Seaward from here.</summary>
            public bool HasPier;
            public Vector3 PierRoot;

            /// <summary>Where the lorry noses up to the stacks, same frame as the works stands.</summary>
            public readonly List<IndustrialLayout.Bay> LorryStands = new();

            /// <summary>
            /// The walk graph the shift moves on: points on two lanes parallel to the quay,
            /// crossable only at the aisle. See PortMarker.WorkLane for the routing rule.
            /// </summary>
            public readonly List<Vector3> QuayLanePoints = new();
            public readonly List<Vector3> ApronLanePoints = new();

            /// <summary>World coordinate of each lane along the cross-quay axis, and of the aisle.</summary>
            public float QuayLaneC;
            public float ApronLaneC;
            public float AisleA;

            /// <summary>True when the lanes run along world X (quay on a North/South side).</summary>
            public bool AlongX;

            /// <summary>A port with no water or no quay is not a port.</summary>
            public bool Usable => Water.Count > 0 && Quay.Max.x > Quay.Min.x;
        }

        /// <summary>
        /// Which side the quay takes: the map-edge side with the longest run of block along it,
        /// so the berth gets the most frontage. Ties - a square corner block - fall South, East,
        /// North, West, fixed, so the answer is a pure function of the rectangle.
        /// </summary>
        public static Sides QuaySideFor(Vector2 min, Vector2 max, Sides edgeSides)
        {
            var spanX = max.x - min.x;
            var spanZ = max.y - min.y;

            var best = Sides.None;
            var bestSpan = -1f;

            foreach (var side in new[] { Sides.South, Sides.East, Sides.North, Sides.West })
            {
                if ((edgeSides & side) == 0)
                    continue;

                var span = side == Sides.South || side == Sides.North ? spanX : spanZ;
                if (span > bestSpan + 0.01f)
                {
                    bestSpan = span;
                    best = side;
                }
            }

            return best;
        }

        /// <summary>
        /// How far the water reaches past this block's rect along the quay, per end.
        /// Zero means this end is a true end of the waterfront - a map corner - and gets the
        /// corner treatment; positive means the neighbouring block along the side is also
        /// Port, and the water runs exactly to the centreline of the street between them,
        /// where the neighbour's water takes over. Both blocks compute the same centreline
        /// from the same grid, which is what keeps the seam invisible and unoverlapped.
        /// </summary>
        public struct Continuation
        {
            public float Low;
            public float High;
        }

        /// <summary>
        /// The water-seam contract, computed from the grid so GroundPlacer and BlockBuilder
        /// cannot disagree: an end continues when the block does not reach the map corner,
        /// and the extension is the sidewalk clearance of the street being crossed - which
        /// is exactly the distance from BlockRect's edge to that street's centreline.
        /// </summary>
        public static Continuation ContinuationFor(
            CityGrid grid, List<Vector2Int> cells, Sides quaySide,
            float sidewalkWidth, float mainSidewalkWidth)
        {
            var alongX = quaySide == Sides.North || quaySide == Sides.South;

            var minCell = new Vector2Int(int.MaxValue, int.MaxValue);
            var maxCell = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var cell in cells)
            {
                minCell = Vector2Int.Min(minCell, cell);
                maxCell = Vector2Int.Max(maxCell, cell);
            }

            // The row of cells the quay runs along - the streets between port blocks are
            // probed there, where they meet the water.
            var edgeRow = quaySide switch
            {
                Sides.South => minCell.y,
                Sides.North => maxCell.y,
                _ => 0,
            };
            var edgeCol = quaySide switch
            {
                Sides.West => minCell.x,
                Sides.East => maxCell.x,
                _ => 0,
            };

            float Extend(int x, int z) =>
                grid.IsMainRoad(x, z) ? mainSidewalkWidth : sidewalkWidth;

            var result = new Continuation();

            if (alongX)
            {
                if (minCell.x > 0)
                    result.Low = Extend(minCell.x - 1, edgeRow);
                if (maxCell.x < grid.Width - 1)
                    result.High = Extend(maxCell.x + 1, edgeRow);
            }
            else
            {
                if (minCell.y > 0)
                    result.Low = Extend(edgeCol, minCell.y - 1);
                if (maxCell.y < grid.Height - 1)
                    result.High = Extend(edgeCol, maxCell.y + 1);
            }

            return result;
        }

        /// <summary>
        /// Plans one port block. <paramref name="edgeSides"/> are the sides with the map
        /// boundary across them - at least one, or ZonePlanner broke its own gate; this
        /// returns an unusable layout rather than inventing water.
        ///
        /// <paramref name="quaySide"/> is the CITY's waterfront side - ZonePlanner.PortSideOf.
        /// It is forced rather than derived because a corner block has two map-edge sides and
        /// must not pick its own: the whole side shares one sea. Sides.None falls back to the
        /// longest-edge rule for a caller without a grid (the tests).
        /// </summary>
        public static Layout ForBlock(
            Vector2 min, Vector2 max, Sides roadSides, Sides edgeSides, float wallInset,
            int seed, int blockId,
            Sides quaySide = Sides.None, Continuation continuation = default)
        {
            var layout = new Layout();

            layout.QuaySide = quaySide != Sides.None && (edgeSides & quaySide) != 0
                ? quaySide
                : QuaySideFor(min, max, edgeSides);
            if (layout.QuaySide == Sides.None)
                return layout;

            var rng = new System.Random(seed + SeedOffsets.Port + blockId * BlockStride);

            // The block's own frame: a runs along the quay, c across it, and every c in this
            // method is measured from the OUTLINE INWARD - "c = 14" is 14m inland of the water
            // whichever compass side the water is on. World() folds the sign back in.
            var alongX = layout.QuaySide == Sides.North || layout.QuaySide == Sides.South;
            var quayAtMax = layout.QuaySide == Sides.North || layout.QuaySide == Sides.East;

            var aMin = alongX ? min.x : min.y;
            var aMax = alongX ? max.x : max.y;
            var outline = alongX ? (quayAtMax ? max.y : min.y) : (quayAtMax ? max.x : min.x);
            var depth = alongX ? max.y - min.y : max.x - min.x;
            var inward = quayAtMax ? -1f : 1f;

            layout.AlongX = alongX;
            layout.Seaward = alongX
                ? new Vector3(0f, 0f, quayAtMax ? 1f : -1f)
                : new Vector3(quayAtMax ? 1f : -1f, 0f, 0f);

            Vector3 World(float a, float c)
            {
                var w = outline + inward * c;
                return alongX ? new Vector3(a, 0f, w) : new Vector3(w, 0f, a);
            }

            IndustrialLayout.Rect RectAC(float a0, float a1, float c0, float c1)
            {
                var p = World(a0, c0);
                var q = World(a1, c1);
                return new IndustrialLayout.Rect
                {
                    Min = new Vector2(Mathf.Min(p.x, q.x), Mathf.Min(p.z, q.z)),
                    Max = new Vector2(Mathf.Max(p.x, q.x), Mathf.Max(p.z, q.z)),
                };
            }

            // --- The wall. Inset on every side but the quay's, where the strip runs open to
            // the water. A second map-edge side (a corner block) keeps its wall - it stands
            // over the water there and reads as the sea wall.
            var wallMin = new Vector2(min.x + wallInset, min.y + wallInset);
            var wallMax = new Vector2(max.x - wallInset, max.y - wallInset);
            switch (layout.QuaySide)
            {
                case Sides.North: wallMax.y = max.y; break;
                case Sides.South: wallMin.y = min.y; break;
                case Sides.East: wallMax.x = max.x; break;
                case Sides.West: wallMin.x = min.x; break;
            }
            layout.Wall = new IndustrialLayout.Rect { Min = wallMin, Max = wallMax };

            // --- The water. One rectangle per map-edge side, clamped at the map corner so a
            // corner block's two seas tile instead of z-fighting where they meet, plus the
            // corner square that fills the diagonal between them. Along the quay the rect
            // runs to the street centrelines where the waterfront continues - see Continuation.
            var waterDepth = WaterRows * CityGrid.CellSize;
            EmitWater(layout, min, max, edgeSides, layout.QuaySide, waterDepth, continuation);

            // --- The quay strip, and everything stacked inland of it.
            layout.Quay = RectAC(aMin, aMax, 0f, QuayDepth);

            var runMin = aMin + EdgeMargin;
            var runMax = aMax - EdgeMargin;
            if (runMax - runMin < 12f || depth < QuayDepth + StackGap + StackBandDepth + 8f)
                return layout;

            // The gate: on the side opposite the water when a street runs there - a lorry
            // drives straight through the yard to the quay - else on a lateral street. The
            // aisle lines up with it, so the gate is chosen before the stacks are cut.
            var backSide = Opposite(layout.QuaySide);
            var lateralSides = alongX ? Sides.East | Sides.West : Sides.North | Sides.South;

            float aisleA;
            if ((roadSides & backSide) != 0)
            {
                layout.HasGate = true;
                aisleA = (aMin + aMax) * 0.5f;
                layout.GateCentre = World(aisleA, depth - wallInset);
                layout.GateOutward = -layout.Seaward;
            }
            else if ((roadSides & lateralSides) != 0)
            {
                // Which lateral end has the street; with both, the block id picks, the same
                // spread rule the works gate uses down an avenue.
                var maxSide = alongX ? Sides.East : Sides.North;
                var atMax = (roadSides & maxSide) != 0
                         && ((roadSides & lateralSides) == (roadSides & maxSide) || rng.Next(2) == 0);

                layout.HasGate = true;
                var gateA = atMax ? aMax - wallInset : aMin + wallInset;
                var gateC = QuayDepth + StackGap + StackBandDepth + 4f;
                layout.GateCentre = World(gateA, gateC);
                layout.GateOutward = alongX
                    ? new Vector3(atMax ? 1f : -1f, 0f, 0f)
                    : new Vector3(0f, 0f, atMax ? 1f : -1f);
                aisleA = gateA + (atMax ? -1f : 1f) * (AisleWidth * 0.5f + 2f);
            }
            else
            {
                // No street at all - still a yard, no way in. Same posture as the works.
                aisleA = (aMin + aMax) * 0.5f;
            }
            layout.AisleA = aisleA;

            // --- The warehouse row, against the back wall, fronts to the water. Skipped when
            // the block is too shallow to hold it AND the working band - the quay wins.
            var bandFrom = QuayDepth + StackGap;
            var bandTo = depth - wallInset - EdgeMargin;

            if (bandTo - bandFrom >= StackBandDepth + 4f + RowDepth)
            {
                var rowFrom = bandTo - RowDepth;
                EmitPads(layout, runMin, runMax, World, rowFrom, RowDepth,
                         layout.Seaward, rng);
                bandTo = rowFrom - 2f;
            }

            // --- The container stacks: bands between quay and apron, every one parted at the
            // aisle. A deep block - the whole-side route hands over 3x3 giants now - fills its
            // depth with further bands instead of leaving a 60m bare apron, with a working
            // lane between each pair for the forklift.
            var alongVec = alongX ? Vector3.right : Vector3.forward;
            var stackC = bandFrom;
            var bandsEmitted = 0;
            while (stackC + StackBandDepth <= bandTo && bandsEmitted < MaxStackBands)
            {
                EmitStacks(layout, runMin, runMax, aisleA, stackC, World, alongVec, rng);
                stackC += StackBandDepth + BandLane;
                bandsEmitted++;
            }
            var lastBandEnd = stackC - BandLane;

            // --- Cranes on the quay, one per ~35m of berth - a working terminal, not a
            // monument. The gantries stand well seaward of the quay walk lane.
            var span = runMax - runMin;
            var craneC = 4f;
            var craneCount = Mathf.Clamp(Mathf.RoundToInt(span / CraneSpacing), 1, 3);
            for (var i = 0; i < craneCount; i++)
                EmitCrane(layout, World(runMin + span * ((i + 0.5f) / craneCount), craneC),
                          layout.Seaward);

            // --- The berth. The ship is 82m against a 64m one-cell quay; the overhang is
            // real - ships outsize their berths - and the water overhang holds it. Nudged
            // along the quay by the block's own rng so a two-port... there is only ever one
            // port, but the same seed discipline costs nothing.
            layout.HasBerth = true;
            var berthA = (aMin + aMax) * 0.5f + (float)(rng.NextDouble() - 0.5) * span * 0.15f;
            layout.BerthCentre = World(berthA, -ShipStandoff);
            layout.BerthCentre.y = -WaterDrop;
            layout.BerthYaw = alongX ? 90f : 0f;

            // --- The pier finger, only where the quay is long enough that it clears the ship:
            // on the end the berth's nudge moved away from.
            if (span >= MinSpanForPier)
            {
                layout.HasPier = true;
                var pierA = berthA < (aMin + aMax) * 0.5f
                    ? runMax - PierModule * 2f
                    : runMin + PierModule * 2f;
                layout.PierRoot = World(pierA, 0f);
            }

            // --- One lorry stand, nosed up to the stack band from the apron side, beside the
            // aisle so the lorry reads as loading rather than parked.
            if (bandTo - lastBandEnd >= IndustrialLayout.LorryStandDepth)
            {
                // Outward toward the stacks: BuildLorries noses the cab at Outward and lays
                // the body back along -Outward, into the open apron.
                layout.LorryStands.Add(new IndustrialLayout.Bay
                {
                    Origin = World(aisleA + AisleWidth * 0.5f + 1f, lastBandEnd),
                    Along = alongVec,
                    Outward = layout.Seaward,
                    Cursor = 0f,
                    Width = IndustrialLayout.LorryStandWidth * 2f,
                });
            }

            // --- The walk graph. Two lanes parallel to the quay - one on the quay strip
            // behind the cranes, one on the apron in front of the warehouse doors - joined
            // only at the aisle. Points are emitted ON the lanes, so any two points on one
            // lane see each other, and crossing lanes is two waypoints through the aisle.
            layout.QuayLaneC = LaneWorld(outline, inward, QuayLaneInset);
            layout.ApronLaneC = LaneWorld(outline, inward, Mathf.Min(bandTo + ApronLaneOffset,
                                                                     depth - wallInset - 1f));

            foreach (var crane in layout.Cranes)
                layout.QuayLanePoints.Add(OnLane(crane.Centre, layout.QuayLaneC, alongX));

            foreach (var stack in layout.Stacks)
            {
                var centre = stack.Area.Centre;
                layout.QuayLanePoints.Add(OnLane(new Vector3(centre.x, 0f, centre.y),
                                                 layout.QuayLaneC, alongX));
                layout.ApronLanePoints.Add(OnLane(new Vector3(centre.x, 0f, centre.y),
                                                  layout.ApronLaneC, alongX));
            }

            if (layout.HasPier)
                layout.QuayLanePoints.Add(OnLane(layout.PierRoot, layout.QuayLaneC, alongX));

            foreach (var pad in layout.Pads)
            {
                var centre = pad.Area.Centre;
                layout.ApronLanePoints.Add(OnLane(new Vector3(centre.x, 0f, centre.y),
                                                  layout.ApronLaneC, alongX));
            }

            if (layout.HasGate)
                layout.ApronLanePoints.Add(OnLane(layout.GateCentre, layout.ApronLaneC, alongX));

            // The aisle's own crossing points, so the graph is connected even on a layout
            // whose stacks all fell on one side.
            layout.QuayLanePoints.Add(OnLane(World(aisleA, 0f), layout.QuayLaneC, alongX));
            layout.ApronLanePoints.Add(OnLane(World(aisleA, 0f), layout.ApronLaneC, alongX));

            return layout;
        }

        /// <summary>
        /// The sea. One rectangle per map-edge side plus the corner squares. Emitted for EVERY
        /// edge side, not only the quay's: a corner block's second boundary reads as coastline
        /// too, and dry void past a sea wall is the louder bug.
        ///
        /// Along the QUAY side the ends follow the continuation contract: to the street
        /// centreline where the waterfront continues (the neighbour block's water meets it
        /// there, edge to edge), clamped at a shared map corner (the corner square takes the
        /// diagonal), and the plain overhang only in the test-harness case of a lone block.
        /// The perpendicular side of a corner block is clamped to the block span at its
        /// city-interior end - the sea wall stops where the port does.
        /// </summary>
        static void EmitWater(
            Layout layout, Vector2 min, Vector2 max, Sides edgeSides, Sides quaySide,
            float waterDepth, Continuation continuation)
        {
            bool Edge(Sides side) => (edgeSides & side) != 0;

            foreach (var side in new[] { Sides.South, Sides.North, Sides.West, Sides.East })
            {
                if (!Edge(side))
                    continue;

                var alongX = side == Sides.South || side == Sides.North;
                var isQuay = side == quaySide;

                var lo = alongX ? min.x : min.y;
                var hi = alongX ? max.x : max.y;

                var cornerLo = alongX ? Edge(Sides.West) : Edge(Sides.South);
                var cornerHi = alongX ? Edge(Sides.East) : Edge(Sides.North);

                // A sea wall's water keeps the block span as it stands: clamped at the shared
                // corner like any other, and clamped at the city end - the coastline stops
                // where the port does instead of jutting 39m along a dry neighbour. Only the
                // quay's water reaches further.
                if (isQuay)
                {
                    // Continue to the centreline, stop at a shared corner (the square takes
                    // the diagonal - overlap here is two opaque seas z-fighting), and only
                    // overhang where there is neither: a block tested on its own.
                    lo += continuation.Low > 0f ? -continuation.Low : (cornerLo ? 0f : -WaterOverhang);
                    hi += continuation.High > 0f ? continuation.High : (cornerHi ? 0f : WaterOverhang);
                }

                IndustrialLayout.Rect rect;
                switch (side)
                {
                    case Sides.South:
                        rect = Rect(lo, min.y - waterDepth, hi, min.y); break;
                    case Sides.North:
                        rect = Rect(lo, max.y, hi, max.y + waterDepth); break;
                    case Sides.West:
                        rect = Rect(min.x - waterDepth, lo, min.x, hi); break;
                    default:
                        rect = Rect(max.x, lo, max.x + waterDepth, hi); break;
                }

                layout.Water.Add(rect);
            }

            // The corner squares, one per pair of adjacent edge sides.
            if (Edge(Sides.South) && Edge(Sides.West))
                layout.Water.Add(Rect(min.x - waterDepth, min.y - waterDepth, min.x, min.y));
            if (Edge(Sides.South) && Edge(Sides.East))
                layout.Water.Add(Rect(max.x, min.y - waterDepth, max.x + waterDepth, min.y));
            if (Edge(Sides.North) && Edge(Sides.West))
                layout.Water.Add(Rect(min.x - waterDepth, max.y, min.x, max.y + waterDepth));
            if (Edge(Sides.North) && Edge(Sides.East))
                layout.Water.Add(Rect(max.x, max.y, max.x + waterDepth, max.y + waterDepth));
        }

        /// <summary>
        /// The warehouse pads, split unequally through BlockLots.SplitRun the way every row in
        /// the city is, and for the same reason: equal pads give a row of identical sheds, and
        /// the unequal spread is what lets the 27m port building appear at all.
        /// </summary>
        static void EmitPads(
            Layout layout, float runMin, float runMax,
            System.Func<float, float, Vector3> world, float rowFrom, float rowDepth,
            Vector3 seaward, System.Random rng)
        {
            var length = runMax - runMin;
            var count = Mathf.Max(1, Mathf.RoundToInt(length / TargetPadWidth));
            var widths = BlockLots.SplitRun(length, count, rng);

            var a = runMin;
            for (var i = 0; i < count; i++)
            {
                var p = world(a, rowFrom);
                var q = world(a + widths[i], rowFrom + rowDepth);

                layout.Pads.Add(new IndustrialLayout.Pad
                {
                    Area = new IndustrialLayout.Rect
                    {
                        Min = new Vector2(Mathf.Min(p.x, q.x), Mathf.Min(p.z, q.z)),
                        Max = new Vector2(Mathf.Max(p.x, q.x), Mathf.Max(p.z, q.z)),
                    },
                    // A pad's outward points at what serves it. For a works that is a road;
                    // here it is the water - the doors open onto the apron the cargo crosses.
                    Outward = seaward,
                });

                a += widths[i];
            }
        }

        /// <summary>
        /// The stack band, parted at the aisle: each half is cut into runs of 8 to 16m with
        /// walking gaps between them, and every run becomes one stack - one colour, one rank
        /// of boxes, which is what a container yard looks like and uniform scatter does not.
        /// </summary>
        static void EmitStacks(
            Layout layout, float runMin, float runMax, float aisleA, float stackC,
            System.Func<float, float, Vector3> world, Vector3 along, System.Random rng)
        {
            void FillHalf(float lo, float hi)
            {
                var a = lo;
                while (hi - a >= 8f)
                {
                    var length = Mathf.Min(hi - a, 8f + (float)rng.NextDouble() * 8f);

                    var p = world(a, stackC);
                    var q = world(a + length, stackC + StackBandDepth);

                    layout.Stacks.Add(new Stack
                    {
                        Area = new IndustrialLayout.Rect
                        {
                            Min = new Vector2(Mathf.Min(p.x, q.x), Mathf.Min(p.z, q.z)),
                            Max = new Vector2(Mathf.Max(p.x, q.x), Mathf.Max(p.z, q.z)),
                        },
                        Along = along,
                    });

                    a += length + 2.5f;
                }
            }

            FillHalf(runMin, aisleA - AisleWidth * 0.5f);
            FillHalf(aisleA + AisleWidth * 0.5f, runMax);
        }

        static void EmitCrane(Layout layout, Vector3 centre, Vector3 seaward) =>
            layout.Cranes.Add(new CraneSpot { Centre = centre, Outward = seaward });

        static IndustrialLayout.Rect Rect(float x0, float z0, float x1, float z1) =>
            new() { Min = new Vector2(x0, z0), Max = new Vector2(x1, z1) };

        static float LaneWorld(float outline, float inward, float inset) =>
            outline + inward * inset;

        /// <summary>Projects a point onto a lane: keeps its along-quay coordinate.</summary>
        static Vector3 OnLane(Vector3 point, float laneC, bool alongX) =>
            alongX ? new Vector3(point.x, 0f, laneC) : new Vector3(laneC, 0f, point.z);

        public static Sides Opposite(Sides side) => side switch
        {
            Sides.North => Sides.South,
            Sides.South => Sides.North,
            Sides.East => Sides.West,
            Sides.West => Sides.East,
            _ => Sides.None,
        };
    }
}
