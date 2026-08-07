using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Tests
{
    /// <summary>
    /// Properties of a works compound that a bare, empty or impossible industrial block would
    /// violate.
    ///
    /// Same discipline as <see cref="TrafficModelTests"/>: a plain static class holding no
    /// UnityEngine.Object, returning failures as data rather than logging them, so it runs in a
    /// bare .NET host against the built Assembly-CSharp.dll with no Editor and no Play mode.
    ///
    /// Every assertion here corresponds to a defect that actually shipped in this zone. The last
    /// one is the important one and the least obvious: a building the pads can never admit is
    /// invisible in every screenshot and in every log, and it is what turned the works into a
    /// yard full of sheds.
    /// </summary>
    public static class IndustrialLayoutTests
    {
        /// <summary>The sizes BlockRect actually produces: one cell 46m, two 76m, three 106m.</summary>
        static readonly float[] BlockSizes = { 46f, 76f, 106f };

        /// <summary>
        /// The works catalogue, width x depth in metres, measured off the binary FBX and
        /// calibrated against tile-road-straight coming out at exactly CityGrid.CellSize.
        ///
        /// Hard-coded rather than read from the PrefabDatabase on purpose - the database is a
        /// ScriptableObject and cannot be loaded outside Unity, and the numbers are properties of
        /// the art, not of the config. If the palette changes, this list is what has to be
        /// updated alongside it, and the test failing is the reminder.
        /// </summary>
        static readonly (string Name, float Width, float Depth)[] Works =
        {
            ("industry-factory",      24.71f, 16.60f),
            ("industry-factory-old",  21.55f, 22.75f),
            ("industry-factory-hall", 12.78f, 20.08f),
            ("industry-warehouse",    17.55f, 17.48f),
            ("industry-storage",       6.00f, 16.35f),
            ("industry-refinery",     22.00f, 15.00f),
            ("industry-building",     16.41f, 12.35f),
        };

        /// <summary>Must match IndustrialDresser's own constants - see FitsAPad.</summary>
        const float HallClearance = 2f;
        const float RoadSetback = 1.5f;

        /// <summary>Runs every check. An empty list means everything passed.</summary>
        public static List<string> Run()
        {
            var failures = new List<string>();

            EveryWorksPrefabCanBeDrawn(failures);
            EveryBlockGetsMoreThanOneBuilding(failures);
            HallsNeverStandInTheCarriageway(failures);
            EveryPadIsServedByARoad(failures);
            TheGateOpensOntoACarriageway(failures);
            SameSeedSameCompound(failures);

            return failures;
        }

        /// <summary>
        /// Every building in the works kit must fit some pad, on some block, some of the time.
        ///
        /// This is the assertion that would have caught the emptiness immediately. With the pad
        /// target set to the AVERAGE building width - which is the intuitive choice - the two
        /// widest factories fit 0.2% and 3.7% of pads, and industry-factory, the twin-stacked one
        /// the smoke hangs off, was effectively never built. Nothing logged, nothing warned; the
        /// zone just came out as sheds. The floor is deliberately generous: this is a test for
        /// "can never appear", not a tuning knob for how often each piece shows up.
        /// </summary>
        static void EveryWorksPrefabCanBeDrawn(List<string> failures)
        {
            var admits = new Dictionary<string, int>();
            var pads = 0;

            foreach (var pad in AllPads())
            {
                pads++;
                foreach (var piece in Works)
                    if (FitsAPad(piece.Width, piece.Depth, pad))
                    {
                        admits.TryGetValue(piece.Name, out var count);
                        admits[piece.Name] = count + 1;
                    }
            }

            if (pads == 0)
            {
                failures.Add("no pads produced at all - the layout builds nothing");
                return;
            }

            foreach (var piece in Works)
            {
                admits.TryGetValue(piece.Name, out var count);
                var share = 100f * count / pads;

                if (share < 5f)
                    failures.Add(
                        $"{piece.Name} ({piece.Width:0.0} x {piece.Depth:0.0}) fits only " +
                        $"{share:0.0}% of pads - it will effectively never be built. " +
                        "Raise IndustrialLayout.TargetPadWidth or lower IndustrialDresser's clearance.");
            }
        }

        /// <summary>
        /// Even the smallest block the map produces has to hold more than one building. A single
        /// shed behind a wall is what "the industrial zone looks empty" was.
        /// </summary>
        static void EveryBlockGetsMoreThanOneBuilding(List<string> failures)
        {
            foreach (var width in BlockSizes)
            foreach (var depth in BlockSizes)
            {
                var worst = int.MaxValue;
                var worstSeed = 0;

                for (var seed = 1; seed <= 30; seed++)
                for (var block = 0; block < 6; block++)
                {
                    var layout = Plan(width, depth, Sides.All, seed, block);
                    var built = CountBuildable(layout);

                    if (built < worst)
                    {
                        worst = built;
                        worstSeed = seed;
                    }
                }

                if (worst < 2)
                    failures.Add($"a {width:0} x {depth:0} block builds only {worst} building(s) " +
                                 $"(seed {worstSeed}) - that reads as an empty lot with a wall round it");
            }
        }

        /// <summary>A hall standing on its own service road is the loudest possible layout bug.</summary>
        static void HallsNeverStandInTheCarriageway(List<string> failures)
        {
            foreach (var layout in AllLayouts())
            foreach (var pad in layout.Pads)
            foreach (var road in layout.Roads)
                if (Overlaps(pad.Area, road))
                {
                    failures.Add($"a pad overlaps a carriageway - halls would stand in the road");
                    return;
                }
        }

        /// <summary>
        /// A pad with no road on its outward face is a building nobody can reach, and its doors
        /// would face a wall.
        /// </summary>
        static void EveryPadIsServedByARoad(List<string> failures)
        {
            foreach (var layout in AllLayouts())
            foreach (var pad in layout.Pads)
            {
                var centre = new Vector3(pad.Area.Centre.x, 0f, pad.Area.Centre.y);
                var half = Mathf.Max(pad.Area.Size.x, pad.Area.Size.y) * 0.5f;

                var served = false;
                for (var step = 0.5f; step <= 3f && !served; step += 0.5f)
                {
                    var probe = centre + pad.Outward * (half + step);
                    foreach (var road in layout.Roads)
                        if (Contains(road, probe))
                        {
                            served = true;
                            break;
                        }
                }

                if (!served)
                {
                    failures.Add("a pad faces no carriageway - its doors would open onto a wall");
                    return;
                }
            }
        }

        /// <summary>
        /// The hole in the wall has to have road behind it. No spur is built to guarantee this -
        /// every carriageway already spans the compound end to end and so reaches the wall where
        /// the gate is - so this is the check that keeps that reasoning true.
        /// </summary>
        static void TheGateOpensOntoACarriageway(List<string> failures)
        {
            foreach (var layout in AllLayouts())
            {
                if (!layout.HasGate || !layout.Usable)
                    continue;

                var inward = layout.GateCentre - layout.GateOutward * 2f;

                var reached = false;
                foreach (var road in layout.Roads)
                    if (Contains(road, inward))
                    {
                        reached = true;
                        break;
                    }

                if (!reached)
                {
                    failures.Add("the gate opens onto no carriageway - lorries drive into a hall");
                    return;
                }
            }
        }

        /// <summary>
        /// GroundPlacer plans the compound independently, from its own stream, to lay tarmac under
        /// the carriageways. If the plan is not a pure function of (seed, blockId) the asphalt
        /// lands somewhere other than the road the halls were arranged around.
        /// </summary>
        static void SameSeedSameCompound(List<string> failures)
        {
            for (var seed = 1; seed <= 20; seed++)
            for (var block = 0; block < 6; block++)
            {
                var a = Plan(76f, 76f, Sides.All, seed, block);
                var b = Plan(76f, 76f, Sides.All, seed, block);

                if (a.Pads.Count != b.Pads.Count || a.Roads.Count != b.Roads.Count)
                {
                    failures.Add($"seed {seed} block {block}: two identical calls disagree on the plan");
                    return;
                }

                for (var i = 0; i < a.Pads.Count; i++)
                    if ((a.Pads[i].Area.Min - b.Pads[i].Area.Min).sqrMagnitude > 1e-6f)
                    {
                        failures.Add($"seed {seed} block {block}: pad {i} moved between two identical calls");
                        return;
                    }
            }
        }

        /// <summary>
        /// Mirrors IndustrialDresser.BuildHalls: a piece fits if its width clears the pad less the
        /// clearance and its depth clears the pad less the road setback.
        /// </summary>
        static bool FitsAPad(float width, float depth, IndustrialLayout.Pad pad)
        {
            var alongX = Mathf.Abs(pad.Outward.z) > 0.5f;
            var padWidth = alongX ? pad.Area.Size.x : pad.Area.Size.y;
            var padDepth = alongX ? pad.Area.Size.y : pad.Area.Size.x;

            return width <= padWidth - HallClearance && depth <= padDepth - RoadSetback;
        }

        /// <summary>
        /// How many buildings a block's pads can actually take, walking each pad's width the way
        /// the dresser does - widest piece that still fits, then whatever the leftover admits.
        /// </summary>
        static int CountBuildable(IndustrialLayout.Layout layout)
        {
            var built = 0;

            foreach (var pad in layout.Pads)
            {
                var alongX = Mathf.Abs(pad.Outward.z) > 0.5f;
                var padWidth = alongX ? pad.Area.Size.x : pad.Area.Size.y;
                var padDepth = alongX ? pad.Area.Size.y : pad.Area.Size.x;

                var runDepth = padDepth - RoadSetback;
                var remaining = padWidth - HallClearance;

                for (var slot = 0; slot < 2 && remaining >= 6f; slot++)
                {
                    // Mirrors IndustrialDresser.Choose: score by the width the PAD ends up
                    // using, not by the candidate's own width. Greedy-widest strands the
                    // leftover, which is the defect this whole test exists for.
                    var chosen = -1f;
                    var bestScore = 0f;

                    foreach (var piece in Works)
                    {
                        if (piece.Width > remaining || piece.Depth > runDepth)
                            continue;

                        var score = piece.Width;

                        if (slot == 0)
                        {
                            var left = remaining - piece.Width - HallClearance;
                            foreach (var other in Works)
                                if (other.Width <= left && other.Depth <= runDepth)
                                    score = Mathf.Max(score, piece.Width + other.Width);
                        }

                        if (score > bestScore)
                        {
                            bestScore = score;
                            chosen = piece.Width;
                        }
                    }

                    if (chosen < 0f)
                        break;

                    built++;
                    remaining -= chosen + HallClearance;
                }
            }

            return built;
        }

        static IEnumerable<IndustrialLayout.Pad> AllPads()
        {
            foreach (var layout in AllLayouts())
            foreach (var pad in layout.Pads)
                yield return pad;
        }

        static IEnumerable<IndustrialLayout.Layout> AllLayouts()
        {
            var sides = new[]
            {
                Sides.North, Sides.East, Sides.North | Sides.South, Sides.All,
            };

            for (var seed = 1; seed <= 25; seed++)
            for (var block = 0; block < 6; block++)
            foreach (var width in BlockSizes)
            foreach (var depth in BlockSizes)
            foreach (var side in sides)
                yield return Plan(width, depth, side, seed, block);
        }

        static IndustrialLayout.Layout Plan(float width, float depth, Sides sides, int seed, int block) =>
            IndustrialLayout.ForBlock(new Vector2(0f, 0f), new Vector2(width, depth), sides,
                                      IndustrialLayout.DefaultRoadWidth, 1f, seed, block);

        static bool Overlaps(IndustrialLayout.Rect a, IndustrialLayout.Rect b) =>
            a.Min.x < b.Max.x - 0.001f && b.Min.x < a.Max.x - 0.001f &&
            a.Min.y < b.Max.y - 0.001f && b.Min.y < a.Max.y - 0.001f;

        static bool Contains(IndustrialLayout.Rect rect, Vector3 point) =>
            point.x >= rect.Min.x - 0.5f && point.x <= rect.Max.x + 0.5f &&
            point.z >= rect.Min.y - 0.5f && point.z <= rect.Max.y + 0.5f;
    }
}
