using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Tests
{
    /// <summary>
    /// The two gate defects that shipped together: a gate leaf narrower than the hole the wall
    /// cut for it, and a street tree standing in the throat of the entrance because the prop
    /// pass had no idea gates existed.
    ///
    /// Same discipline as <see cref="IndustrialLayoutTests"/>: a plain static class holding no
    /// UnityEngine.Object, returning failures as data rather than logging them, so it runs in a
    /// bare .NET host against the built Assembly-CSharp.dll with no Editor and no Play mode.
    /// </summary>
    public static class GateTests
    {
        /// <summary>
        /// military-gate's width along the wall, measured off the binary FBX (frame x in
        /// [-2, 2], leaf x in [1.8, 5.4]). Hard-coded for the same reason as the Works table in
        /// IndustrialLayoutTests: the number is a property of the art, the prefab cannot be
        /// loaded outside Unity, and this failing is the reminder to retune the stretch.
        /// </summary>
        const float GatePrefabWidth = 7.4f;

        /// <summary>
        /// The street cross-section the ApproachDepth constant was sized against. Wall to prop
        /// line: 15 half-tile + 7 pavement + 1 wall inset - 5.5 verge (identically 17.5 via
        /// 15 + 10 + 1 - 8.5 on the avenue); the verge on the FAR side of the street is at
        /// 15 + 7 + 1 + 5.5. If any of those change, the numbers here change with them.
        /// </summary>
        const float WallToPropLine = 17.5f;
        const float WallToFarVerge = 28.5f;

        /// <summary>Runs every check. An empty list means everything passed.</summary>
        public static List<string> Run()
        {
            var failures = new List<string>();

            TheGateSpansItsOpening(failures);
            TheStretchNeverShrinks(failures);
            TheApproachCoversThePropLine(failures);
            TheApproachSparesTheRestOfTheStreet(failures);
            SameLayoutSameApproach(failures);

            return failures;
        }

        /// <summary>
        /// The stretched gate has to meet both pier faces. The clear opening is GateWidth less
        /// half a pier each side - the piers are CENTRED on the gap edges, so their inner faces
        /// sit PierHalf inside it.
        /// </summary>
        static void TheGateSpansItsOpening(List<string> failures)
        {
            var opening = IndustrialLayout.GateWidth - 2f * PerimeterFence.PierHalf;
            var stretched = GatePrefabWidth * IndustrialDresser.GateStretch(GatePrefabWidth);

            if (Mathf.Abs(stretched - opening) > 1e-3f)
                failures.Add(
                    $"the gate stretches to {stretched:0.000}m against a {opening:0.000}m clear " +
                    "opening - daylight beside the leaf, which is the exact defect this fixes");
        }

        /// <summary>
        /// Art wider than the hole must overlap the piers, never shrink - shrinking below the
        /// authored size is the one thing this project never does to a prefab.
        /// </summary>
        static void TheStretchNeverShrinks(List<string> failures)
        {
            if (IndustrialDresser.GateStretch(IndustrialLayout.GateWidth + 5f) < 1f)
                failures.Add("GateStretch went below 1 for art wider than the opening - " +
                             "that shrinks a prefab below its authored size");
        }

        /// <summary>
        /// Every slot the prop pass could lay in front of a gate has to fall inside the
        /// approach box: the whole gate mouth, at both verge depths, for every layout the
        /// generator produces. One surviving slot is one tree in a lorry's path.
        /// </summary>
        static void TheApproachCoversThePropLine(List<string> failures)
        {
            foreach (var (approach, centre, outward, along, width) in AllApproaches())
            {
                // The prop line sits at the same 17.5m from the wall on both road classes,
                // but probe a band around it so the constant is not load-bearing to the metre.
                for (var depth = WallToPropLine - 2f; depth <= WallToPropLine + 2f; depth += 1f)
                for (var lateral = -width * 0.5f; lateral <= width * 0.5f; lateral += 1f)
                {
                    var slot = centre + outward * depth + along * lateral;

                    if (!Inside(approach, slot))
                    {
                        failures.Add(
                            $"a slot {lateral:0.0}m across the gate mouth at {depth:0.0}m out " +
                            "escapes the approach box - a tree can still stand in the entrance");
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// The box must not reach what it has no business clearing: the far side of the street,
        /// or the frontage beyond the gate's own margin. Over-clearing reads as a bald patch in
        /// an otherwise planted street.
        /// </summary>
        static void TheApproachSparesTheRestOfTheStreet(List<string> failures)
        {
            foreach (var (approach, centre, outward, along, width) in AllApproaches())
            {
                var farVerge = centre + outward * WallToFarVerge;
                if (Inside(approach, farVerge))
                {
                    failures.Add("the approach box reaches the verge across the street - " +
                                 "a tree over the road from a gate blocks nothing");
                    return;
                }

                var wide = width * 0.5f + 2f * PerimeterFence.ApproachMargin;
                var beside = centre + outward * WallToPropLine + along * wide;
                if (Inside(approach, beside))
                {
                    failures.Add($"the approach box reaches {wide:0.0}m across from the gate " +
                                 "centre - it is clearing frontage that never blocked the way in");
                    return;
                }
            }
        }

        /// <summary>
        /// The keep-out list is built in the block pass and read in the prop pass; if it is not
        /// a pure function of the layout the two passes disagree about where the gates are.
        /// </summary>
        static void SameLayoutSameApproach(List<string> failures)
        {
            foreach (var gate in AllGates())
            {
                var a = PerimeterFence.Approach(gate);
                var b = PerimeterFence.Approach(gate);

                if ((a.center - b.center).sqrMagnitude > 1e-6f ||
                    (a.size - b.size).sqrMagnitude > 1e-6f)
                {
                    failures.Add("two identical Approach calls disagree - the block pass and " +
                                 "the prop pass would see different gates");
                    return;
                }
            }
        }

        /// <summary>
        /// Bounds.Contains by hand: the instance method is a thunk over a native free function
        /// and raises SecurityException in the bare .NET host this suite runs in - same class
        /// of trap as Debug.Log, see the project's offline-verification notes.
        /// </summary>
        static bool Inside(Bounds bounds, Vector3 point)
        {
            var half = bounds.size * 0.5f;
            var d = point - bounds.center;
            return Mathf.Abs(d.x) <= half.x && Mathf.Abs(d.y) <= half.y && Mathf.Abs(d.z) <= half.z;
        }

        /// <summary>
        /// Approach boxes for every gate the generator produces, with the frame needed to walk
        /// the street in front of them: outward off the wall and along it.
        /// </summary>
        static IEnumerable<(Bounds Approach, Vector3 Centre, Vector3 Outward, Vector3 Along, float Width)>
            AllApproaches()
        {
            foreach (var gate in AllGates())
            {
                var along = new Vector3(-gate.Outward.z, 0f, gate.Outward.x);
                yield return (PerimeterFence.Approach(gate), gate.Centre, gate.Outward, along, gate.Width);
            }
        }

        /// <summary>
        /// Industrial gates from real layouts across seeds, block shapes and road sides. The
        /// churchyard used to add a second width on every cardinal; it went with the church, and
        /// the works yard is the only thing that cuts a gate now.
        /// </summary>
        static IEnumerable<PerimeterFence.Gate> AllGates()
        {
            var sizes = new[] { 46f, 76f, 106f };
            var sides = new[] { Sides.North, Sides.East, Sides.North | Sides.South, Sides.All };

            for (var seed = 1; seed <= 15; seed++)
            for (var block = 0; block < 4; block++)
            foreach (var width in sizes)
            foreach (var depth in sizes)
            foreach (var side in sides)
            {
                var layout = IndustrialLayout.ForBlock(
                    new Vector2(0f, 0f), new Vector2(width, depth), side,
                    IndustrialLayout.DefaultRoadWidth, 1f, seed, block);

                // The same guard the dresser applies: ForBlock decides HasGate before several
                // early returns that leave GateCentre/GateOutward unset.
                if (!layout.HasGate || layout.GateOutward.sqrMagnitude < 0.5f)
                    continue;

                yield return new PerimeterFence.Gate
                {
                    Has = true,
                    Centre = layout.GateCentre,
                    Outward = layout.GateOutward,
                    Width = IndustrialLayout.GateWidth,
                };
            }
        }
    }
}
