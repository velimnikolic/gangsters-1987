using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Tests
{
    /// <summary>
    /// Properties of a port block that a landlocked, overlapping or unreachable layout would
    /// violate. Same discipline as <see cref="IndustrialLayoutTests"/>: no UnityEngine.Object,
    /// failures returned as data, runnable in a bare .NET host.
    ///
    /// The water assertions are the ones that earn their keep: the sea is the first geometry
    /// this project lays OUTSIDE the map outline, below y=0, and clamped by hand at the
    /// corners - three novelties stacked on one list of rectangles, none of which any other
    /// pass will ever double-check.
    /// </summary>
    public static class PortLayoutTests
    {
        /// <summary>
        /// Block rects the generator actually produces for edge blocks: BlockRect leaves the
        /// edge side at the cell boundary and extends road sides toward the kerb, so an
        /// edge-block rect is 46-ish by 51.5-ish. Sizes here bracket the real range.
        /// </summary>
        static readonly (Vector2 min, Vector2 max, Sides roads, Sides edges, string name)[] Cases =
        {
            // One cell, quay South, streets on the other three sides.
            (new Vector2(0f, 0f), new Vector2(64f, 51.5f),
             Sides.North | Sides.East | Sides.West, Sides.South, "one-cell south"),

            // Two cells along the quay - the pier-bearing shape.
            (new Vector2(0f, 0f), new Vector2(103f, 51.5f),
             Sides.North | Sides.East | Sides.West, Sides.South, "two-cell south"),

            // Corner block: two seas, one quay, a sea wall on the second edge.
            (new Vector2(0f, 0f), new Vector2(64f, 58f),
             Sides.North | Sides.East, Sides.South | Sides.West, "corner south-west"),

            // Quay on a vertical side, to catch axis-swap slips.
            (new Vector2(0f, 0f), new Vector2(51.5f, 103f),
             Sides.North | Sides.South | Sides.West, Sides.East, "two-cell east"),

            // No street at all on the back side - the gate must fall to a lateral street.
            (new Vector2(0f, 0f), new Vector2(64f, 51.5f),
             Sides.East, Sides.South, "one-cell lateral gate"),
        };

        const float WallInset = 1f;

        public static List<string> Run()
        {
            var failures = new List<string>();

            SameSeedSamePort(failures);
            QuayTakesAnEdgeSide(failures);
            WaterStaysOutsideTheOutline(failures);
            WaterNeverOverlapsItself(failures);
            TheBerthFloatsOnLaidWater(failures);
            TheAisleStaysClearOfStacks(failures);
            WorkPointsSitOnTheirLanes(failures);
            NeighbourSeamsMeetExactly(failures);
            AForcedQuayWinsOverTheLongEdge(failures);

            return failures;
        }

        /// <summary>
        /// Two port blocks either side of a street: each extends its water to the street's
        /// centreline, and the two rects must meet edge to edge - a gap shows void between
        /// two seas, an overlap z-fights. This is the whole-side waterfront's load-bearing
        /// seam, and both halves are computed independently on purpose, exactly as
        /// GroundPlacer and BlockBuilder compute them in production.
        /// </summary>
        static void NeighbourSeamsMeetExactly(List<string> failures)
        {
            // Block A [0..64], a 14m street corridor, block B [78..142]; centreline at 71.
            var a = PortLayout.ForBlock(
                new Vector2(0f, 0f), new Vector2(64f, 51.5f),
                Sides.North | Sides.West, Sides.South, WallInset, 11, 3,
                Sides.South, new PortLayout.Continuation { Low = 0f, High = 7f });

            var b = PortLayout.ForBlock(
                new Vector2(78f, 0f), new Vector2(142f, 51.5f),
                Sides.North | Sides.East, Sides.South, WallInset, 11, 4,
                Sides.South, new PortLayout.Continuation { Low = 7f, High = 0f });

            float aHi = float.MinValue, bLo = float.MaxValue;
            foreach (var rect in a.Water) aHi = Mathf.Max(aHi, rect.Max.x);
            foreach (var rect in b.Water) bLo = Mathf.Min(bLo, rect.Min.x);

            if (Mathf.Abs(aHi - 71f) > 0.01f || Mathf.Abs(bLo - 71f) > 0.01f)
                failures.Add($"neighbour seam: A's water ends at {aHi:0.00} and B's starts at " +
                             $"{bLo:0.00}, both should be the street centreline at 71.00 - " +
                             "the waterfront has a gap or a z-fight at every street mouth.");
        }

        /// <summary>
        /// A corner block whose deep edge is NOT the city's waterfront must still put its quay
        /// on the forced side - the whole side shares one sea, and a corner compound facing
        /// its own private ocean was exactly the bug the parameter exists to prevent.
        /// </summary>
        static void AForcedQuayWinsOverTheLongEdge(List<string> failures)
        {
            // Deeper than wide, so the longest-edge rule would pick West; the city says South.
            var layout = PortLayout.ForBlock(
                new Vector2(0f, 0f), new Vector2(51.5f, 90f),
                Sides.North | Sides.East, Sides.South | Sides.West, WallInset, 13, 5,
                Sides.South, default);

            if (layout.QuaySide != Sides.South)
                failures.Add($"forced quay: corner block put its quay on {layout.QuaySide}, " +
                             "not on the city's South waterfront.");
        }

        static IEnumerable<(PortLayout.Layout layout, string name, Vector2 min, Vector2 max)> AllLayouts(int seed)
        {
            for (var i = 0; i < Cases.Length; i++)
            {
                var c = Cases[i];
                yield return (PortLayout.ForBlock(c.min, c.max, c.roads, c.edges,
                                                  WallInset, seed, blockId: i + 3),
                              c.name, c.min, c.max);
            }
        }

        /// <summary>
        /// The replay contract. GroundPlacer sinks water and BlockBuilder moors the ship off
        /// two independent calls; any drift between them is a ship on dry land.
        /// </summary>
        static void SameSeedSamePort(List<string> failures)
        {
            foreach (var (a, name, min, max) in AllLayouts(seed: 41))
            {
                var b = PortLayout.ForBlock(min, max,
                                            Cases[IndexOf(name)].roads, Cases[IndexOf(name)].edges,
                                            WallInset, 41, blockId: IndexOf(name) + 3);

                if (a.Water.Count != b.Water.Count || a.Stacks.Count != b.Stacks.Count
                    || a.Pads.Count != b.Pads.Count
                    || (a.HasBerth && (a.BerthCentre - b.BerthCentre).sqrMagnitude > 1e-6f))
                    failures.Add($"{name}: two calls with the same (seed, blockId) disagree - " +
                                 "the GroundPlacer/BlockBuilder replay contract is broken.");
            }
        }

        static int IndexOf(string name)
        {
            for (var i = 0; i < Cases.Length; i++)
                if (Cases[i].name == name)
                    return i;
            return 0;
        }

        static void QuayTakesAnEdgeSide(List<string> failures)
        {
            for (var i = 0; i < Cases.Length; i++)
            {
                var c = Cases[i];
                var layout = PortLayout.ForBlock(c.min, c.max, c.roads, c.edges,
                                                 WallInset, 7, i + 3);

                if ((layout.QuaySide & c.edges) == 0)
                    failures.Add($"{c.name}: quay went to {layout.QuaySide}, which is not a " +
                                 "map-edge side - the water would be under a street.");
            }
        }

        /// <summary>Every drop of sea lies outside the block rect - the map outline's side.</summary>
        static void WaterStaysOutsideTheOutline(List<string> failures)
        {
            foreach (var (layout, name, min, max) in AllLayouts(seed: 7))
            {
                foreach (var rect in layout.Water)
                {
                    var inside = rect.Min.x < max.x - 0.01f && rect.Max.x > min.x + 0.01f
                              && rect.Min.y < max.y - 0.01f && rect.Max.y > min.y + 0.01f;
                    if (inside)
                        failures.Add($"{name}: water rect ({rect.Min}-{rect.Max}) reaches " +
                                     "inside the block rect - the sea is flooding the city.");
                }
            }
        }

        /// <summary>
        /// No two water rects overlap. They are opaque surfaces at the same height, so any
        /// overlap is a z-fight - and the corner clamping exists precisely to prevent this,
        /// so this is the test of that clamping.
        /// </summary>
        static void WaterNeverOverlapsItself(List<string> failures)
        {
            foreach (var (layout, name, _, _) in AllLayouts(seed: 19))
                for (var i = 0; i < layout.Water.Count; i++)
                    for (var j = i + 1; j < layout.Water.Count; j++)
                        if (layout.Water[i].Overlaps(layout.Water[j]))
                            failures.Add($"{name}: water rects {i} and {j} overlap - " +
                                         "two opaque seas z-fighting at the same height.");
        }

        /// <summary>
        /// The ship's hull rectangle - 82 x 16.64, the measured ship-cargo footprint - must be
        /// covered by the union of water rects. The overhang past the block ends is the case
        /// that bites: the ship is longer than a one-cell berth by design.
        /// </summary>
        static void TheBerthFloatsOnLaidWater(List<string> failures)
        {
            const float HalfLength = 41f;
            const float HalfBeam = 8.32f;

            foreach (var (layout, name, _, _) in AllLayouts(seed: 23))
            {
                if (!layout.HasBerth)
                    continue;

                var along = Vector3.Cross(Vector3.up, layout.Seaward);

                // Sample the hull's corners and midpoints; each must be wet.
                for (var t = -1; t <= 1; t++)
                for (var s = -1; s <= 1; s += 2)
                {
                    var point = layout.BerthCentre
                              + along * (t * HalfLength)
                              + layout.Seaward * (s * HalfBeam) * 0.5f;

                    var wet = false;
                    foreach (var rect in layout.Water)
                        if (point.x >= rect.Min.x - 0.01f && point.x <= rect.Max.x + 0.01f
                         && point.z >= rect.Min.y - 0.01f && point.z <= rect.Max.y + 0.01f)
                        {
                            wet = true;
                            break;
                        }

                    if (!wet)
                        failures.Add($"{name}: ship hull point ({point.x:0.0}, {point.z:0.0}) " +
                                     "is not over any water rect - the berth hangs over void.");
                }
            }
        }

        /// <summary>
        /// The aisle corridor - the walk graph's one lane-crossing - must not intersect any
        /// container stack, or the shift walks through a box. This is a property PortLayout
        /// promises by construction; the test is what notices if a later edit breaks it.
        /// </summary>
        static void TheAisleStaysClearOfStacks(List<string> failures)
        {
            foreach (var (layout, name, _, _) in AllLayouts(seed: 31))
            {
                if (layout.Stacks.Count == 0)
                    continue;

                var alongX = Mathf.Abs(layout.Seaward.z) > 0.5f;
                var half = PortLayout.AisleWidth * 0.5f - 0.01f;

                foreach (var stack in layout.Stacks)
                {
                    var lo = alongX ? stack.Area.Min.x : stack.Area.Min.y;
                    var hi = alongX ? stack.Area.Max.x : stack.Area.Max.y;

                    if (lo < layout.AisleA + half && hi > layout.AisleA - half)
                        failures.Add($"{name}: a container stack ({lo:0.0}-{hi:0.0}) crosses " +
                                     $"the aisle at {layout.AisleA:0.0} - the walk graph's " +
                                     "only lane crossing is blocked.");
                }
            }
        }

        /// <summary>
        /// Every published work point lies on one of the two lane lines, because the routing
        /// rule ("same lane means walk straight") is only sound if that is true.
        /// </summary>
        static void WorkPointsSitOnTheirLanes(List<string> failures)
        {
            foreach (var (layout, name, _, _) in AllLayouts(seed: 47))
            {
                var alongX = layout.AlongX;

                foreach (var p in layout.QuayLanePoints)
                {
                    var c = alongX ? p.z : p.x;
                    if (Mathf.Abs(c - layout.QuayLaneC) > 0.01f)
                        failures.Add($"{name}: quay-lane point at c={c:0.00} is off its lane " +
                                     $"({layout.QuayLaneC:0.00}) - straight-line walks on the " +
                                     "lane are no longer guaranteed clear.");
                }

                foreach (var p in layout.ApronLanePoints)
                {
                    var c = alongX ? p.z : p.x;
                    if (Mathf.Abs(c - layout.ApronLaneC) > 0.01f)
                        failures.Add($"{name}: apron-lane point at c={c:0.00} is off its lane " +
                                     $"({layout.ApronLaneC:0.00}).");
                }
            }
        }
    }
}
