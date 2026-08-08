using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// Where the wall, the service road, the halls and the yard go on a works block. Pure
    /// geometry - this class instantiates nothing, for the same reason ParkingLayout does not:
    /// IndustrialDresser places the buildings and GroundPlacer lays asphalt under the road, and
    /// both have to read the SAME answer. A works whose tarmac is half a metre off its own
    /// carriageway is visible from the default camera and miserable to chase down.
    ///
    /// The shape being built is a compound, not a terrace. A works is a wall with one gate, a
    /// road in from that gate, and halls standing along the road with their doors on it. The old
    /// industrial block had none of that: it went through BlockLots and WalkSide like a
    /// residential street, so factories lined the block edges like houses and BuildScatter
    /// sprinkled bricks and a chimney across the middle at random yaw.
    ///
    /// Deterministic in (seed, blockId) ALONE, which is the same contract BlockLots.Plan holds
    /// and for the same reason - GroundPlacer calls this from its own stream, after an unknown
    /// number of other draws, and must get back the identical rectangles.
    /// </summary>
    public static class IndustrialLayout
    {
        /// <summary>
        /// Wide enough for two lorries to pass. The pack's longest is car-truck-tanker; the
        /// carriageway of an ordinary road tile is 8m between its own kerbs, and a works road
        /// that reads narrower than the street outside it reads as an alley instead. Overridden
        /// per city by CityConfig.serviceRoadWidth.
        /// </summary>
        public const float DefaultRoadWidth = 8f;

        /// <summary>
        /// Depth of one row of halls, measured against the catalogue rather than chosen. The
        /// deepest works building is industry-factory-old at 22.75m, and the dresser then pushes
        /// every hall back off the carriageway by its road setback - so a row has to hold the
        /// building PLUS that setback or the deepest piece in the kit can never be drawn. 23 was
        /// the first value tried and it silently excluded exactly one prefab, which is the sort
        /// of thing that shows up as "the old factory never appears" months later.
        ///
        /// The cost is checked against the block sizes BlockRect produces: two rows and two
        /// carriageways is 68m and a two-cell block is 76m, so nothing is priced out.
        /// </summary>
        public const float RowDepth = 25f;

        /// <summary>
        /// Target width of one hall's ground, chosen by measurement rather than by taste.
        ///
        /// Sized for the WIDEST piece in the kit, not the average, and that is the opposite of
        /// the obvious answer. The works buildings run 12.35m to 24.71m and average about 17, so
        /// a 20m target looks right - but it makes the two biggest factories unplaceable, and a
        /// sweep says how badly: industry-factory fits 0.1% of pads at target 20, 2.8% at 22,
        /// 13% at 24 and 33% at 26. The zone came out as nothing but sheds, which is what "the
        /// industrial zone looks empty" was really describing.
        ///
        /// Widening pads costs no density, because the dresser fills the leftover width with a
        /// second, narrower building - the same sweep puts buildings per 76x76 block at 5.0 for
        /// every target from 18 to 28. So the width is free and the reachability is not.
        /// </summary>
        public const float TargetPadWidth = 26f;

        /// <summary>Wall to the first row. Enough to walk a fence line and stack a pallet.</summary>
        public const float EdgeMargin = 3f;

        /// <summary>
        /// Apron inside the gate, before the first hall. Wider than EdgeMargin because it is the
        /// one strip every road meets - a lorry turning in off the street needs somewhere to
        /// swing, and without it the gate opens straight onto the end wall of a hall.
        /// </summary>
        public const float GateMargin = 8f;

        /// <summary>Gap left in the wall. Two lanes, in and out, plus a lorry's overhang.</summary>
        public const float GateWidth = 9f;

        /// <summary>
        /// How much of the row nearest the gate is given to staff parking instead of a hall.
        /// Two bay rows plus their aisle is 17.2, which is what a 23m row holds; the run length
        /// is what varies.
        /// </summary>
        public const float ParkingRunLength = 22f;

        /// <summary>
        /// A lorry stand is deeper than a marked car bay on purpose. ParkingLayout.StallDepth is
        /// 5.6 and rejects `truck` at 6.25 - correctly, a lorry does not fit a car bay either -
        /// so the works keeps its own, and IndustrialDresser fills these directly rather than
        /// through FillStalls.
        /// </summary>
        public const float LorryStandDepth = 7.5f;
        public const float LorryStandWidth = 3.4f;

        /// <summary>Spreads consecutive block ids apart in the seed space - see BlockLots.</summary>
        const int BlockStride = 397;

        /// <summary>
        /// A rectangle of the compound, in world XZ.
        ///
        /// Serializable because LotZone carries one and rides into the saved scene on the
        /// WorksYard marker - the city is generated in the editor and saved, so anything the
        /// runtime or the gizmos need again has to survive a domain reload. Unity will not
        /// serialize a struct field whose TYPE lacks the attribute, however serializable its
        /// own members are.
        /// </summary>
        [System.Serializable]
        public struct Rect
        {
            public Vector2 Min;
            public Vector2 Max;

            public Vector2 Centre => (Min + Max) * 0.5f;
            public Vector2 Size => Max - Min;

            public bool Overlaps(Rect other) =>
                Min.x < other.Max.x && other.Min.x < Max.x &&
                Min.y < other.Max.y && other.Min.y < Max.y;
        }

        /// <summary>
        /// One hall's ground. <see cref="Outward"/> points at the service road that serves it,
        /// which is what turns the building's front onto the road instead of at the wall - the
        /// single thing that makes a compound read as laid out rather than filled in.
        /// </summary>
        public struct Pad
        {
            public Rect Area;
            public Vector3 Outward;
        }

        /// <summary>
        /// A run of bays along the edge of a row, in the (origin, along, outward, cursor, width)
        /// frame BuildLot walks its sides in - so ParkingLayout.ForStreetBay takes it unchanged.
        /// </summary>
        public struct Bay
        {
            public Vector3 Origin;
            public Vector3 Along;
            public Vector3 Outward;
            public float Cursor;
            public float Width;
        }

        public sealed class Layout
        {
            /// <summary>The wall line. Inset from the block rect so the piers do not overhang.</summary>
            public Rect Wall;

            /// <summary>False when the block has no road on any side - nothing to open onto.</summary>
            public bool HasGate;

            /// <summary>Middle of the gap in the wall, on the wall line itself.</summary>
            public Vector3 GateCentre;

            /// <summary>Unit vector pointing out of the compound through the gate.</summary>
            public Vector3 GateOutward;

            public readonly List<Rect> Roads = new();
            public readonly List<Pad> Pads = new();
            public readonly List<Bay> ParkingRuns = new();
            public readonly List<Bay> LorryStands = new();

            /// <summary>A compound too small to hold a road and a row is not a compound.</summary>
            public bool Usable => Pads.Count > 0;
        }

        /// <summary>
        /// Plans one works block.
        ///
        /// The road axis comes from <paramref name="roadSides"/>, not from which side of the
        /// block is longer, for the reason ParkingLayout.ForBlock spells out: a block against the
        /// map boundary can have no street on its long axis at all, and a gate in a wall facing
        /// nothing is worse than a smaller compound.
        ///
        /// Rows and roads are packed by the same loop ParkingLayout uses for its bays and aisles,
        /// with RowDepth for StallDepth and the road width for the aisle. The leftover depth is
        /// handed back to the roads rather than to the margins, so the compound always reaches
        /// its own wall instead of stopping eight metres short of it.
        /// </summary>
        public static Layout ForBlock(
            Vector2 min, Vector2 max, Sides roadSides, float roadWidth, float wallInset,
            int seed, int blockId)
        {
            var layout = new Layout();

            var wallMin = new Vector2(min.x + wallInset, min.y + wallInset);
            var wallMax = new Vector2(max.x - wallInset, max.y - wallInset);
            layout.Wall = new Rect { Min = wallMin, Max = wallMax };

            if (wallMax.x - wallMin.x < 4f || wallMax.y - wallMin.y < 4f)
                return layout;

            var rng = new System.Random(seed + SeedOffsets.Industrial + blockId * BlockStride);

            // East/West street -> the road runs along X and the gate is on an X end. A block with
            // no street at all still gets a yard, just no way in.
            var alongX = (roadSides & (Sides.East | Sides.West)) != 0
                      || (roadSides & (Sides.North | Sides.South)) == 0;

            // With streets on both ends, the gate goes on whichever the block id picks, so a run
            // of works blocks down one avenue does not present an identical elevation each time.
            var gateAtMax = alongX
                ? Faces(roadSides, Sides.East, Sides.West, rng)
                : Faces(roadSides, Sides.North, Sides.South, rng);

            layout.HasGate = roadSides != Sides.None;

            // (a, c) is the compound's own frame: a runs along the roads, c stacks the rows.
            var aMin = alongX ? wallMin.x : wallMin.y;
            var aMax = alongX ? wallMax.x : wallMax.y;
            var cMin = alongX ? wallMin.y : wallMin.x;
            var cMax = alongX ? wallMax.y : wallMax.x;

            // The rows give up the apron at the gate end and a margin at the other.
            var runMin = aMin + (gateAtMax ? EdgeMargin : GateMargin);
            var runMax = aMax - (gateAtMax ? GateMargin : EdgeMargin);
            if (runMax - runMin < 12f)
                return layout;

            // Counted before anything is placed, exactly as ParkingLayout counts its bands.
            var budget = (cMax - EdgeMargin) - (cMin + EdgeMargin);
            var used = roadWidth;
            var roads = 1;
            var bands = 0;

            while (used + 2f * RowDepth + roadWidth <= budget)
            {
                used += 2f * RowDepth + roadWidth;
                bands++;
                roads++;
            }

            // A single row is still served by the road in front of it.
            var tail = budget - used >= RowDepth;
            if (tail)
                used += RowDepth;

            if (bands == 0 && !tail)
                return layout;

            // The leftover depth goes to the ROWS, not to the carriageways. ParkingLayout hands
            // its slack to the aisles because an aisle is what a car park is mostly made of; a
            // works is mostly buildings, and widening the road instead put 13m of tarmac down a
            // one-cell block and left the single row too shallow to stand anything behind its
            // hall. Depth given to a row becomes the back strip the outbuildings live in.
            var rowCount = bands * 2 + (tail ? 1 : 0);
            var rowDepth = RowDepth + (budget - used) / rowCount;

            var c = cMin + EdgeMargin;

            for (var band = 0; band < bands; band++)
            {
                EmitRoad(layout, alongX, aMin, aMax, c, c + roadWidth);
                c += roadWidth;

                // Two rows back to back, each facing the road that serves it, so their blank
                // rears meet in the middle of the band - the same trick ParkingLayout plays with
                // two rows of bays sharing a head line.
                EmitRow(layout, alongX, runMin, runMax, c, rowDepth, facesMax: false, rng);
                EmitRow(layout, alongX, runMin, runMax, c + rowDepth, rowDepth, facesMax: true, rng);

                c += 2f * rowDepth;
            }

            EmitRoad(layout, alongX, aMin, aMax, c, c + roadWidth);
            c += roadWidth;

            if (tail)
                EmitRow(layout, alongX, runMin, runMax, c, rowDepth, facesMax: false, rng);

            if (layout.HasGate)
            {
                // On the road nearest the middle of the compound, so the gate lines up with a
                // carriageway rather than with the end wall of a hall.
                var target = (cMin + cMax) * 0.5f;
                var gateC = layout.Roads[0].Centre;
                var gateCoord = alongX ? gateC.y : gateC.x;

                foreach (var r in layout.Roads)
                {
                    var candidate = alongX ? r.Centre.y : r.Centre.x;
                    if (Mathf.Abs(candidate - target) < Mathf.Abs(gateCoord - target))
                        gateCoord = candidate;
                }

                var gateA = gateAtMax ? aMax : aMin;
                layout.GateCentre = World(alongX, gateA, gateCoord);
                layout.GateOutward = alongX
                    ? new Vector3(gateAtMax ? 1f : -1f, 0f, 0f)
                    : new Vector3(0f, 0f, gateAtMax ? 1f : -1f);

                // No spur is needed to join the gate to the road grid, which is worth stating
                // because the obvious reading of this layout says otherwise. Every carriageway
                // spans the compound end to end, so the one the gate was aligned to already
                // arrives at the wall exactly where the gap is. A spur here was dead geometry,
                // and its Pads.RemoveAll could only ever have deleted a hall for nothing.
            }

            ReserveParking(layout, alongX, gateAtMax, rng);

            return layout;
        }

        /// <summary>
        /// Which end the gate goes on. With a street at one end only there is no choice; with one
        /// at both, the block's own rng picks, and with neither the answer does not matter
        /// because HasGate is false.
        /// </summary>
        static bool Faces(Sides roadSides, Sides maxSide, Sides minSide, System.Random rng)
        {
            var hasMax = (roadSides & maxSide) != 0;
            var hasMin = (roadSides & minSide) != 0;

            if (hasMax && hasMin)
                return rng.Next(2) == 0;

            return hasMax;
        }

        static void EmitRoad(Layout layout, bool alongX, float aMin, float aMax, float from, float to) =>
            layout.Roads.Add(new Rect
            {
                Min = Flat(World(alongX, aMin, from)),
                Max = Flat(World(alongX, aMax, to)),
            });

        /// <summary>
        /// One row of pads. facesMax means the road that serves this row is on its high-c side.
        ///
        /// The pad target is 20m, not the row length: a works building measures 12.35m to 24.71m
        /// wide, so a row cut into 30m pads stands one 17m warehouse in each and leaves a third
        /// of every row bare. That is what "the industrial zone looks empty" actually was - a
        /// 46m block came out with a single hall on it.
        ///
        /// Split UNEQUALLY, through the same BlockLots.SplitRun the block plan uses one scale up,
        /// and for the same reason: equal parts give a row of identical boxes. Here it earns its
        /// keep twice over, because the pads then span roughly 15m to 25m and that range is what
        /// lets the widest piece in the kit (industry-factory at 24.71) appear at all while the
        /// narrow pads take sheds.
        /// </summary>
        static void EmitRow(
            Layout layout, bool alongX, float runMin, float runMax, float c, float depth,
            bool facesMax, System.Random rng)
        {
            var length = runMax - runMin;
            var count = Mathf.Max(1, Mathf.RoundToInt(length / TargetPadWidth));

            var outward = alongX
                ? new Vector3(0f, 0f, facesMax ? 1f : -1f)
                : new Vector3(facesMax ? 1f : -1f, 0f, 0f);

            var widths = BlockLots.SplitRun(length, count, rng);
            var a = runMin;

            for (var i = 0; i < count; i++)
            {
                layout.Pads.Add(new Pad
                {
                    Area = new Rect
                    {
                        Min = Flat(World(alongX, a, c)),
                        Max = Flat(World(alongX, a + widths[i], c + depth)),
                    },
                    Outward = outward,
                });

                a += widths[i];
            }
        }

        /// <summary>
        /// Turns the pad nearest the gate into staff parking. One pad, because a works is halls
        /// with a car park in the corner and not the other way round - and the pad nearest the
        /// gate for the reason every real site puts it there: nobody drives a car through the
        /// yard to park behind a hall.
        /// </summary>
        static void ReserveParking(Layout layout, bool alongX, bool gateAtMax, System.Random rng)
        {
            // Three, not two. A pad is a whole building's worth of ground, and on a one-cell
            // block there are only two of them - spending one on a staff car park leaves a
            // "works" that is one shed and some tarmac. A small works has no car park, which is
            // both truer and what stops the smallest blocks reading as empty.
            if (!layout.HasGate || layout.Pads.Count < 3)
                return;

            var gate = Flat(layout.GateCentre);

            var best = 0;
            var bestDistance = float.MaxValue;

            for (var i = 0; i < layout.Pads.Count; i++)
            {
                var distance = (layout.Pads[i].Area.Centre - gate).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            var pad = layout.Pads[best];
            layout.Pads.RemoveAt(best);

            var area = pad.Area;
            var outward = pad.Outward;

            // Bays run from the pad's road edge inward, nose to the kerb - the frame
            // ParkingLayout.ForStreetBay already expects.
            var along = alongX ? Vector3.right : Vector3.forward;
            var edge = RoadEdge(area, outward);
            var width = alongX ? area.Size.x : area.Size.y;

            layout.ParkingRuns.Add(new Bay
            {
                Origin = edge,
                Along = along,
                Outward = outward,
                Cursor = 0f,
                Width = width,
            });

            // A lorry stands at the loading face of the hall opposite, on the far side of the
            // same road. Drawn here rather than in the dresser so the geometry stays in one file.
            if (layout.Pads.Count == 0)
                return;

            var stand = layout.Pads[rng.Next(layout.Pads.Count)];
            var standEdge = RoadEdge(stand.Area, stand.Outward);
            var standWidth = alongX ? stand.Area.Size.x : stand.Area.Size.y;

            if (standWidth < LorryStandWidth * 2f)
                return;

            layout.LorryStands.Add(new Bay
            {
                Origin = standEdge,
                Along = along,
                Outward = stand.Outward,
                Cursor = standWidth * 0.5f - LorryStandWidth,
                Width = LorryStandWidth * 2f,
            });
        }

        /// <summary>
        /// The corner of a pad's road-facing edge, at the low end along the run. Bays and stands
        /// are both parameterised from here.
        /// </summary>
        static Vector3 RoadEdge(Rect area, Vector3 outward)
        {
            if (outward.z > 0.5f) return new Vector3(area.Min.x, 0f, area.Max.y);
            if (outward.z < -0.5f) return new Vector3(area.Min.x, 0f, area.Min.y);
            if (outward.x > 0.5f) return new Vector3(area.Max.x, 0f, area.Min.y);
            return new Vector3(area.Min.x, 0f, area.Min.y);
        }

        static Vector3 World(bool alongX, float a, float c) =>
            alongX ? new Vector3(a, 0f, c) : new Vector3(c, 0f, a);

        static Vector2 Flat(Vector3 point) => new(point.x, point.z);
    }
}
