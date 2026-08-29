using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Stands a whole industrial quarter: the parcels the deal called for, each composed on
    /// the spot, and the roads the raster read between them.
    ///
    /// One class, two callers, which is this project's rule for anything that has to look
    /// the same in the editor and at Play: <c>Tools/City/Industrial/Sketch The Industrial
    /// Quarter</c> hands it <c>PrefabUtility.InstantiatePrefab</c> so the drawing is made of
    /// linked instances, and <see cref="IndustrialDistrict"/> hands it a plain
    /// <c>Instantiate</c>. Neither of them knows anything the other does not.
    /// </summary>
    public static class IndustrialQuarter
    {
        /// <summary>The name of the root the whole drawing hangs off in a scene.</summary>
        public const string SketchRoot = "INDUSTRIAL QUARTER (sketch)";

        /// <summary>One parcel, standing.</summary>
        public sealed class Stood
        {
            public IndustrialLayout.Parcel Parcel;
            public Transform Root;
            public IndustrialBlocks.Block Block;

            /// <summary>Faults the composer could see in its own work: cells with no floor,
            /// metres of fence missing, and pieces of fence standing inside a building.
            /// All three should be nought.</summary>
            public int Gaps, WallInBuilding, Straddles;
            public float WallGap;

            /// <summary>What the recipe asked for and the ground refused, worst first - not
            /// a fault, but the one thing a half-empty yard would otherwise never say.</summary>
            public string Refused;
        }

        /// <summary>
        /// Everything the quarter is made of, under one parent.
        ///
        /// The parcels go up first and the roads afterwards, and both are laid in the
        /// quarter's OWN coordinates - whoever wants it somewhere else moves the parent,
        /// which is the district contract's rule (Docs/city-districts-plan.md 1.1).
        /// </summary>
        public static List<Stood> Stand(IndustrialLayout.Plan plan, CoreRoads.Raster raster,
                                        Transform parent, Func<GameObject, Transform, GameObject> raise,
                                        Func<int, int, bool> skipRoad = null)
        {
            IndustrialBlocks.ForgetMissing();
            var stood = new List<Stood>();

            var parcels = new GameObject("Parcels").transform;
            parcels.SetParent(parent, false);

            for (int k = 0; k < plan.Parcels.Count; k++)
            {
                var parcel = plan.Parcels[k];
                var root = new GameObject(parcel.Name).transform;
                root.SetParent(parcels, false);

                // COMPOSED AT THE ORIGIN AND MOVED AFTERWARDS, and this is not a detail.
                // Every piece the composer puts down is placed by MEASURING where it lands
                // and then setting a WORLD position, because pack pieces pivot at a corner,
                // at one end or in the middle and measuring is the only answer right for all
                // of them. Which means the root's transform is not consulted: given its
                // place before composing, a parcel builds itself around the world origin and
                // the whole quarter comes out as nineteen yards stacked on one spot with the
                // roads drawn correctly around the empty ground where they should have been.
                var rng = new System.Random(unchecked(plan.Seed * 7919 + parcel.I0 * 104729 + parcel.J0 * 1299709));
                var block = IndustrialBlocks.Stand(parcel.Recipe, root,
                                                   parcel.W * (int)IndustrialLayout.Cell,
                                                   parcel.D * (int)IndustrialLayout.Cell,
                                                   parcel.Locals(), rng, raise);
                block.Streetside(rng);

                // measured BEFORE the turn, while the block's own frame and the world's are
                // still the same one - which is what makes this a local box, good wherever
                // the quarter is eventually stood
                var own = Measure(root);

                // NOW it is turned into place. A parcel is always composed facing south -
                // every recipe puts its gate on the south kerb and works north from it, and
                // a second set of those geometries for the parcels that face the other way
                // is a second set of the same bugs. Turned about, the block's own origin
                // lands on its far corner, which is where the root has to go.
                bool turned = parcel.Yaw == 180;
                root.SetPositionAndRotation(
                    new Vector3(turned ? (parcel.I0 + parcel.W) * IndustrialLayout.Cell : parcel.I0 * IndustrialLayout.Cell,
                                0f,
                                turned ? (parcel.J0 + parcel.D) * IndustrialLayout.Cell : parcel.J0 * IndustrialLayout.Cell),
                    Quaternion.Euler(0f, parcel.Yaw, 0f));

                Pickable(root, parcel, own);

                stood.Add(new Stood
                {
                    Parcel = parcel, Root = root, Block = block,
                    Gaps = block.Gaps(),
                    WallGap = block.WallGap,
                    WallInBuilding = block.WallInBuilding(),
                    Straddles = block.Straddles(),
                    Refused = block.Refused(),
                });
            }

            var roads = new GameObject("Roads").transform;
            roads.SetParent(parent, false);
            CoreRoads.Lay(raster, (prefab, under) => raise(prefab, under), roads, skipRoad);

            return stood;
        }

        /// <summary>
        /// Makes the parcel answer a click, and says what it is when it does.
        ///
        /// One box to the parcel and not one to each building, because a block is the thing
        /// being asked about: "what is this" wants "a stockyard, two sheds, three ranks of
        /// containers", not the prefab name of the shed that happened to be under the
        /// pointer. The box is the parcel's whole rectangle, so any part of it answers.
        ///
        /// The card counts what actually STOOD rather than what the recipe asked for. A
        /// recipe refuses what will not fit, so the two are not the same thing, and the one
        /// worth reading is the one you can see.
        /// </summary>
        static void Pickable(Transform root, IndustrialLayout.Parcel parcel, Bounds own)
        {
            float w = parcel.W * IndustrialLayout.Cell, d = parcel.D * IndustrialLayout.Cell;
            float tall = Mathf.Max(6f, own.size.y);

            // A TRIGGER, and that matters. This box is the whole parcel from the ground to
            // the roofline, which is the right shape for "click anywhere on this block" and
            // a disastrous one for anything else: solid, it would swallow every bullet fired
            // across the quarter and every walker crossing it. A trigger still answers
            // Physics.RaycastAll - queriesHitTriggers is on by default and the picker relies
            // on it - and stops nothing.
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(w * 0.5f, tall * 0.5f, d * 0.5f);
            box.size = new Vector3(w, tall, d);

            int buildings = 0, vehicles = 0, cans = 0, tanks = 0, stacks = 0, bays = 0;
            foreach (Transform piece in root)
            {
                var name = piece.name;
                if (name.StartsWith("building-")) buildings++;
                else if (name.StartsWith("SM_Veh_")) vehicles++;
                else if (name.StartsWith("container-20")) cans++;
                else if (name == "tank") tanks++;
                else if (name == "chimney crown") stacks++;
                else if (name.StartsWith("SM_Env_Road_ParkingLines")) bays++;
            }

            var built = new List<string>();
            if (buildings > 0) built.Add(Count(buildings, "building", "buildings"));
            if (stacks > 0) built.Add(Count(stacks, "chimney", "chimneys"));
            if (tanks > 0) built.Add(Count(tanks, "tank", "tanks"));

            var yard = new List<string>();
            if (vehicles > 0) yard.Add(Count(vehicles, "vehicle", "vehicles"));
            if (cans > 0) yard.Add(Count(cans, "container", "containers"));
            if (bays > 0) yard.Add(Count(bays, "painted bay", "painted bays"));

            // a bare newline, not Environment.NewLine: this is a string for IMGUI to draw,
            // not a line of a file, and the carriage return Windows adds comes out as a box
            // glyph at the end of every line of the card
            var body = new System.Text.StringBuilder();
            body.Append($"{w:F0} x {d:F0} m, fronts {parcel.Face.ToString().ToLowerInvariant()}");
            if (built.Count > 0) body.Append('\n').Append(string.Join(", ", built));
            if (yard.Count > 0) body.Append('\n').Append(string.Join(", ", yard));
            if (built.Count == 0 && yard.Count == 0)
                body.Append('\n').Append("nothing built on it");

            CardFacts.On(root.gameObject, IndustrialLayout.Words(parcel.Recipe), body.ToString(),
                         new Bounds(box.center, box.size));
        }

        static string Count(int many, string one, string more) =>
            many == 1 ? $"1 {one}" : $"{many} {more}";

        /// <summary>Everything the parcel stood, boxed - the smoke off a chimney excepted,
        /// because a particle renderer that has not played reports an empty box at the world
        /// origin and would drag the measurement across the map.</summary>
        static Bounds Measure(Transform root)
        {
            var box = new Bounds(root.position, Vector3.one);
            bool any = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer) continue;
                if (!any) { box = renderer.bounds; any = true; }
                else box.Encapsulate(renderer.bounds);
            }
            return box;
        }

        /// <summary>
        /// What the composer found wrong with its own work, in one line - the counterpart to
        /// the raster's report, which judges the ROADS.
        ///
        /// Both have to be nought for a quarter to be finished, and they catch different
        /// things: the raster cannot see a hole in a fence and the composer cannot see a
        /// street that goes nowhere.
        /// </summary>
        public static string Report(List<Stood> stood)
        {
            int gaps = 0, through = 0, split = 0;
            float wall = 0f;
            var bad = new List<string>();
            foreach (var one in stood)
            {
                gaps += one.Gaps;
                through += one.WallInBuilding;
                split += one.Straddles;
                wall += one.WallGap;
                if (one.Gaps == 0 && one.WallInBuilding == 0 && one.Straddles == 0 && one.WallGap < 0.5f) continue;
                bad.Add($"{one.Parcel.Name}: {one.Gaps} holes, {one.WallGap:F1} m of fence missing, " +
                        $"{one.WallInBuilding} pieces of fence inside a building, " +
                        $"{one.Straddles} buildings on two surfaces");
            }
            var sb = new System.Text.StringBuilder();
            sb.Append($"   {stood.Count} parcels: {gaps} cells with no floor, {wall:F1} m of fence missing, " +
                      $"{through} pieces of fence standing in a building, " +
                      $"{split} buildings standing on two surfaces");
            foreach (var line in bad) sb.Append(Environment.NewLine).Append("   WARNING: ").Append(line);
            if (IndustrialBlocks.Missing.Count > 0)
                sb.Append(Environment.NewLine).Append("   WARNING: missing from the project: ")
                  .Append(string.Join(", ", IndustrialBlocks.Missing));
            return sb.ToString();
        }

        /// <summary>How the quarter was cast, for the log: how many of each recipe.</summary>
        public static string Cast(IndustrialLayout.Plan plan)
        {
            var count = new Dictionary<IndustrialLayout.Recipe, int>();
            foreach (var parcel in plan.Parcels)
                count[parcel.Recipe] = count.TryGetValue(parcel.Recipe, out var c) ? c + 1 : 1;
            var order = new List<KeyValuePair<IndustrialLayout.Recipe, int>>(count);
            // by COUNT, commonest first. Sorted on the rendered string instead, "12 works"
            // came before "3 yard" and the line said nothing about which recipe the quarter
            // is mostly made of, which is the only question it is there to answer
            order.Sort((one, other) => other.Value != one.Value
                ? other.Value.CompareTo(one.Value)
                : one.Key.CompareTo(other.Key));
            var parts = new List<string>();
            foreach (var pair in order) parts.Add($"{pair.Value} {pair.Key.ToString().ToLowerInvariant()}");
            return string.Join(", ", parts);
        }
    }
}
