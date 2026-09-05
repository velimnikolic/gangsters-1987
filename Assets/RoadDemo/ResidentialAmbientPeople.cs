using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static RoadDemo.Composer;

namespace RoadDemo
{
    public static partial class ResidentialBlocks
    {
        const string PeopleAnimations = "Assets/Animations/People/";
        const string AmbientIdle = PeopleAnimations + "Breathing Idle.anim";
        const string AmbientTalk = PeopleAnimations + "Standing_Talking.anim";
        const string AmbientSit = PeopleAnimations + "Sitting_Bench_Idle.anim";
        const string AmbientStandUp = PeopleAnimations + "Sitting-Idle.anim";
        const string AmbientWalk = PeopleAnimations + "Standard Walk.anim";
        const string IdlePack = "Assets/Synty/AnimationIdles/Animations/Polygon/";

        // A deliberately civilian-only wardrobe. The residential pool uses these only as
        // ambient block dressing, so disabling their authored components cannot affect the
        // real pavement crowd or the crews.
        static readonly string[] AmbientBodies =
        {
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Male_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Male_02.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Female_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Female_02.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Rich_Male_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Rich_Female_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Characters/Character_Male_Jacket.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Characters/Character_Female_Jacket.prefab",
        };

        readonly struct ClipPair
        {
            public readonly AnimationClip Male;
            public readonly AnimationClip Female;

            public ClipPair(AnimationClip both)
            {
                Male = Female = both;
            }

            public ClipPair(AnimationClip male, AnimationClip female)
            {
                Male = male;
                Female = female;
            }

            public AnimationClip Pick(bool female, AnimationClip fallback) =>
                (female ? Female : Male) ?? Male ?? Female ?? fallback;
        }

        sealed class AmbientClipSet
        {
            public AnimationClip Idle, Talk, Sit, StandUp, Walk, Run;
            public ClipPair Gym, Inspect;
        }

        readonly struct AmbientSite
        {
            public readonly int I, J;
            public readonly ResidentialLot.Use Use;

            public AmbientSite(int i, int j, ResidentialLot.Use use)
            {
                I = i;
                J = j;
                Use = use;
            }
        }

        internal enum SeatKind { Chair, Bench }

        internal readonly struct SeatAnchor
        {
            public readonly Vector3 At;
            public readonly Vector3 Departure;
            public readonly float Yaw;
            public readonly SeatKind Kind;
            public readonly bool Restaurant;

            public SeatAnchor(Vector3 at, Vector3 departure, float yaw,
                              SeatKind kind, bool restaurant)
            {
                At = at;
                Departure = departure;
                Yaw = yaw;
                Kind = kind;
                Restaurant = restaurant;
            }
        }

        readonly struct PropAnchor
        {
            public readonly Vector3 At;
            public readonly Vector3 Forward;

            public PropAnchor(Vector3 at, Vector3 forward)
            {
                At = at;
                Forward = forward;
            }
        }

        static AmbientClipSet _ambientClips;

        /// <summary>
        /// Populates the finished block with a small, deterministic slice of city life.
        /// Venue actors are tied to authored equipment; diners use real chairs, sitters
        /// use real benches, and the bin/door loops use props and entrances that actually
        /// exist in this composition. One block scheduler owns their continuous animation
        /// graphs and the only reaction: nearby gunfire sends visible figures through the
        /// nearest reachable door.
        /// </summary>
        static void AmbientPeople(ResidentialLot.Plan plan, Transform root, Stood stood)
        {
            if (plan == null || root == null || stood == null) return;
            var clips = GetAmbientClips();
            if (clips.Idle == null && clips.Talk == null && clips.Walk == null) return;

            var rng = new System.Random(unchecked(plan.Seed * 486187739 + 1987));
            var shelters = AmbientShelters(plan);
            if (shelters.Count == 0) return;

            var seats = SeatAnchors(root, plan);
            var bins = BinAnchors(root);
            Shuffle(seats, rng);
            Shuffle(bins, rng);

            int first = stood.People;
            int target = first + TargetPeople(plan);
            // Keep room for the routines that visibly move even on a small corner block:
            // one bin trip when possible, one doorway loop and one short clear-ground walk.
            int reserve = (bins.Count > 0 ? 1 : 0) + 2;

            var pen = new GameObject("Ambient block life (decorative)").transform;
            pen.SetParent(root, false);
            var life = pen.gameObject.AddComponent<ResidentialBlockLife>();
            var clearance = new AmbientStandingClearance(root, pen);
            life.UseRuntimeClearance(clearance);
            float width = plan.W * ResidentialLot.Cell;
            float depth = plan.D * ResidentialLot.Cell;
            life.Configure(shelters, clips.Idle, clips.Walk, clips.Run, clips.StandUp,
                           new Vector3(width * 0.5f, Deck, depth * 0.5f),
                           0.5f * Mathf.Sqrt(width * width + depth * depth),
                           Between(rng, 0f, 1f / 12f));

            var occupied = new List<Vector3>(target - first);

            // The facility-specific tableaus go first. Their coordinates are measured in
            // the harvested amenity prefabs, then turned with the same arithmetic as Stand.
            for (int i = 0; i < plan.Spots.Count && stood.People < target - reserve; i++)
                VenuePeople(plan.Spots[i], life, pen, clearance, clips, rng, occupied, stood,
                            target - reserve);

            // Restaurants have dozens of authored chairs. Six occupied seats is enough to
            // make them read as open without turning every diner into a crowd simulation.
            int restaurantSeats = 0;
            for (int i = 0; i < seats.Count && stood.People < target - reserve; i++)
            {
                var seat = seats[i];
                if (!seat.Restaurant || restaurantSeats >= 6 || !Apart(occupied, seat.At, 0.9f)) continue;
                bool hasDeparture = TrySeatDeparture(clearance, seat, out Vector3 departure);
                if (!AddActor(life, pen, seat.At, seat.At, seat.Yaw, seat.Yaw,
                              new ClipPair(clips.Sit), ResidentialBlockLife.Routine.SeatedConversation,
                              rng, "Restaurant guest talking", occupied, stood, 22f, 0.94f,
                              secondary: new ClipPair(clips.Talk),
                              hasDeparture: hasDeparture, departure: departure))
                    continue;
                restaurantSeats++;
            }

            // Real benches are found in the composed hierarchy, including courtyard and
            // plaza benches placed procedurally after the building units.
            int benchSitters = 0;
            for (int i = 0; i < seats.Count && stood.People < target - reserve; i++)
            {
                var seat = seats[i];
                if (seat.Kind != SeatKind.Bench || seat.Restaurant || benchSitters >= 2 ||
                    !Apart(occupied, seat.At, 0.9f)) continue;
                bool hasDeparture = TrySeatDeparture(clearance, seat, out Vector3 departure);
                if (!AddActor(life, pen, seat.At, seat.At, seat.Yaw, seat.Yaw,
                              new ClipPair(clips.Sit), ResidentialBlockLife.Routine.Seated,
                              rng, "Neighbour sitting on bench", occupied, stood, 26f, 0.92f,
                              hasDeparture: hasDeparture, departure: departure))
                    continue;
                benchSitters++;
            }

            // Outdoor cafe tables and shop chairs get a restrained one or two customers.
            int cafeSeats = 0;
            for (int i = 0; i < seats.Count && stood.People < target - reserve; i++)
            {
                var seat = seats[i];
                if (seat.Kind != SeatKind.Chair || seat.Restaurant || cafeSeats >= 2 ||
                    !Apart(occupied, seat.At, 0.9f)) continue;
                bool hasDeparture = TrySeatDeparture(clearance, seat, out Vector3 departure);
                if (!AddActor(life, pen, seat.At, seat.At, seat.Yaw, seat.Yaw,
                              new ClipPair(clips.Sit), ResidentialBlockLife.Routine.SeatedConversation,
                              rng, "Cafe customer talking", occupied, stood, 24f, 0.94f,
                              secondary: new ClipPair(clips.Talk),
                              hasDeparture: hasDeparture, departure: departure))
                    continue;
                cafeSeats++;
            }

            // A carried litter bag is a child prop. It vanishes only at an actual bin and
            // returns on the next long decorative loop; no inventory or gameplay item exists.
            if (stood.People < target && bins.Count > 0)
            {
                var bin = bins[rng.Next(bins.Count)];
                TrashLeg(plan, bin, out Vector3 from, out Vector3 to, out float yaw);
                var blockRoom = new Rect(0.45f, 0.45f,
                                         Mathf.Max(0.5f, width - 0.9f),
                                         Mathf.Max(0.5f, depth - 0.9f));
                if (clearance.TryNearest(from, blockRoom, out from, 2.2f) &&
                    clearance.TryNearest(to, blockRoom, out to, 1.2f) &&
                    clearance.IsPathClear(from, to))
                    AddActor(life, pen, from, to, yaw, yaw,
                             clips.Inspect, ResidentialBlockLife.Routine.Trash,
                             rng, "Taking rubbish to bin", occupied, stood, 34f, 0.90f,
                             carryTrash: true);
            }

            // One resident repeatedly comes out, pauses on the frontage and goes back in.
            if (stood.People < target)
            {
                var shelter = shelters[rng.Next(shelters.Count)];
                Vector3 outward = shelter.Outside - shelter.Inside;
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.01f) outward = Vector3.forward;
                outward.Normalize();
                Vector3 tangent = Vector3.Cross(Vector3.up, outward);
                if (rng.Next(2) == 0) tangent = -tangent;
                Vector3 pause = shelter.Outside + tangent * Between(rng, 2.0f, 3.2f);
                pause.x = Mathf.Clamp(pause.x, 0.7f, width - 0.7f);
                pause.z = Mathf.Clamp(pause.z, 0.7f, depth - 0.7f);
                var blockRoom = new Rect(0.45f, 0.45f,
                                         Mathf.Max(0.5f, width - 0.9f),
                                         Mathf.Max(0.5f, depth - 0.9f));
                if (clearance.TryNearest(pause, blockRoom, out pause, 1.8f) &&
                    clearance.IsPathClear(shelter.Outside, pause))
                {
                    float yaw = YawTowards(outward);
                    AddActor(life, pen, shelter.Inside, pause, yaw, yaw,
                             new ClipPair(clips.Idle), ResidentialBlockLife.Routine.Door,
                             rng, "Resident entering and leaving", occupied, stood, 25f, 0.86f,
                             hasVia: true, via: shelter.Outside);
                }
            }

            AddWalkingResident(plan, life, pen, clearance, clips, rng,
                               occupied, stood, target);
            FillStreetTableaux(plan, life, pen, clearance, clips, rng, occupied, stood, target);

            if (stood.People == first)
                UnityEngine.Object.DestroyImmediate(pen.gameObject);
        }

        static int TargetPeople(ResidentialLot.Plan plan)
        {
            int target = plan.Klass switch
            {
                ResidentialLot.Klass.Corner => 5,
                ResidentialLot.Klass.Row => 6,
                ResidentialLot.Klass.Block => 8,
                _ => 10,
            };
            if (plan.YardBlock) target = Mathf.Max(target, 9);
            for (int i = 0; i < plan.Spots.Count; i++)
            {
                string name = plan.Spots[i].Unit?.Name;
                if (name == "dinner" || name == "dinner2") target = Mathf.Max(target, 10);
                else if (name == "gym" || name == "caryard" || name == "kosarkaskiteren" ||
                         name == "skatepark") target = Mathf.Max(target, 9);
            }
            return target;
        }

        static void VenuePeople(ResidentialLot.Spot spot, ResidentialBlockLife life,
                                Transform pen, AmbientStandingClearance clearance,
                                AmbientClipSet clips, System.Random rng,
                                List<Vector3> occupied, Stood stood, int limit)
        {
            string name = spot?.Unit?.Name;
            if (string.IsNullOrEmpty(name)) return;

            bool RoomForOne() => stood.People < limit;
            void Pose(Vector3 local, float yaw, ClipPair clip, string label)
            {
                if (!RoomForOne()) return;
                Vector3 preferred = UnitPoint(spot, local);
                if (!clearance.TryNearest(preferred, SpotRoom(spot), out Vector3 at, 3.2f) ||
                    !Apart(occupied, at, 0.85f)) return;
                float facing = (at - preferred).sqrMagnitude > 0.09f
                    ? YawTowards(preferred - at)
                    : UnitYaw(spot, yaw);
                var routine = label.IndexOf("Talking", StringComparison.OrdinalIgnoreCase) >= 0
                    ? ResidentialBlockLife.Routine.Conversation
                    : ResidentialBlockLife.Routine.Activity;
                AddActor(life, pen, at, at, facing, facing,
                         clip, routine,
                         rng, label, occupied, stood, 18f, 0.92f);
            }
            void Shuttle(Vector3 a, Vector3 b, float yawA, float yawB, ClipPair clip, string label)
            {
                if (!RoomForOne()) return;
                if (!clearance.TryNearest(UnitPoint(spot, a), SpotRoom(spot),
                                          out Vector3 from, 2.2f) ||
                    !clearance.TryNearest(UnitPoint(spot, b), SpotRoom(spot),
                                          out Vector3 to, 2.2f) ||
                    !Apart(occupied, from, 0.85f)) return;
                var routine = clearance.IsPathClear(from, to)
                    ? ResidentialBlockLife.Routine.Shuttle
                    : ResidentialBlockLife.Routine.Activity;
                if (routine == ResidentialBlockLife.Routine.Activity) to = from;
                AddActor(life, pen, from, to,
                         UnitYaw(spot, yawA), UnitYaw(spot, yawB), clip, routine,
                         rng, label, occupied, stood, 20f, 0.96f);
            }

            switch (name)
            {
                case "gym":
                    Pose(new Vector3(4.82f, 1.26f, 2.88f), 0f, clips.Gym, "Training at squat rack");
                    Pose(new Vector3(9.44f, 1.26f, 6.88f), 90f, clips.Gym, "Training at gym frame");
                    Pose(new Vector3(14.07f, 1.26f, 8.88f), 270f, clips.Gym, "Stretching beside bench press");
                    Pose(new Vector3(9.07f, 1.26f, 2.00f), 180f, clips.Inspect, "Choosing dumbbells");
                    break;

                case "caryard":
                    Pose(new Vector3(13.7f, ResidentialCaryard.Deck, 5f), 270f, clips.Inspect, "Inspecting car bodywork");
                    Pose(new Vector3(6.1f, ResidentialCaryard.Deck, 5.2f), 90f, clips.Inspect, "Looking at car interior");
                    Pose(new Vector3(12f, ResidentialCaryard.Deck, 16.2f), 0f, clips.Inspect, "Comparing cars");
                    Pose(new Vector3(32f, ResidentialCaryard.Deck + .02f, 15.8f), 65f, new ClipPair(clips.Talk), "Talking to car salesman");
                    break;

                case "kosarkaskiteren":
                    Shuttle(new Vector3(18.80f, 0.64f, 9.50f), new Vector3(29.20f, 0.64f, 9.50f),
                            90f, 270f, new ClipPair(clips.Idle), "Crossing basketball court");
                    Pose(new Vector3(21.20f, 0.64f, 12.20f), 90f, clips.Gym, "Warming up on court");
                    Pose(new Vector3(28.20f, 0.64f, 12.20f), 270f, clips.Gym, "Stretching by basketball hoop");
                    break;

                case "skatepark":
                    Shuttle(new Vector3(8.00f, 0.00f, 17.00f), new Vector3(16.00f, 0.00f, 18.00f),
                            82f, 262f, new ClipPair(clips.Idle), "Moving between skate ramps");
                    Shuttle(new Vector3(25.00f, 0.00f, 10.00f), new Vector3(31.00f, 0.00f, 14.00f),
                            56f, 236f, new ClipPair(clips.Idle), "Walking through skatepark");
                    Pose(new Vector3(17.00f, 0.00f, 6.00f), 180f, clips.Inspect, "Watching a skate line");
                    break;
            }
        }

        static bool AddActor(ResidentialBlockLife life, Transform parent,
                             Vector3 a, Vector3 b, float yawA, float yawB,
                             ClipPair action, ResidentialBlockLife.Routine routine,
                             System.Random rng, string label, List<Vector3> occupied,
                             Stood stood, float period, float cadence, bool carryTrash = false,
                             ClipPair? secondary = null, bool hasDeparture = false,
                             Vector3 departure = default, bool hasVia = false,
                             Vector3 via = default, bool legacyRuntime = false)
        {
            string path = AmbientBodies[rng.Next(AmbientBodies.Length)];
            var go = legacyRuntime ? RaiseLegacyRuntime(path, parent) : Raise(path, parent);
            if (go == null) return false;

            bool female = path.IndexOf("Female", StringComparison.OrdinalIgnoreCase) >= 0;
            AnimationClip fallback = GetAmbientClips().Idle ?? GetAmbientClips().Talk;
            AnimationClip pose = action.Pick(female, fallback);
            AnimationClip second = secondary.HasValue
                ? secondary.Value.Pick(female, null)
                : null;
            go.name = label + " (ambient prop)";
            go.transform.localPosition = a;
            go.transform.localRotation = Quaternion.Euler(0f, yawA, 0f);
            var animator = PrepareFigure(go);

            GameObject carry = null;
            if (carryTrash)
            {
                carry = Raise(Litter[0], go.transform);
                if (carry != null)
                {
                    carry.name = "Carried rubbish (decorative prop)";
                    carry.transform.localPosition = new Vector3(0.28f, 0.78f, 0.20f);
                    carry.transform.localRotation = Quaternion.Euler(8f, 18f, -12f);
                    PrepareCarry(carry);
                    stood.Props++;
                }
            }

            life.Add(go, animator, carry, pose, second, routine, a, b, yawA, yawB,
                     Between(rng, 0f, period), period + Between(rng, -2f, 3f),
                     cadence + Between(rng, -0.08f, 0.08f),
                     hasDeparture, departure, hasVia, via);
            occupied.Add(a);
            if ((b - a).sqrMagnitude > 0.5f) occupied.Add(b);
            stood.People++;
            return true;
        }

        static GameObject RaiseLegacyRuntime(string path, Transform parent)
        {
            var prefab = DemoAssetLoad.Load<GameObject>(path);
            return prefab != null ? UnityEngine.Object.Instantiate(prefab, parent) : null;
        }

        static Animator PrepareFigure(GameObject go)
        {
            var animator = go.GetComponentInChildren<Animator>(true);
            foreach (var behaviour in go.GetComponentsInChildren<Behaviour>(true))
                if (behaviour != animator) behaviour.enabled = false;
            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (var body in go.GetComponentsInChildren<Rigidbody>(true))
            {
                body.detectCollisions = false;
                body.isKinematic = true;
            }

            if (animator != null)
            {
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                // ResidentialBlockLife gives the rig a continuous PlayableGraph in Play.
                // Off camera, Unity may skip bone evaluation while the graph keeps time;
                // returning figures resume mid-action rather than replaying frame one.
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
                animator.enabled = false;
            }

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    skin.updateWhenOffscreen = false;
                    skin.quality = SkinQuality.Bone2;
                }
            }
            return animator;
        }

        static void PrepareCarry(GameObject carry)
        {
            foreach (var behaviour in carry.GetComponentsInChildren<Behaviour>(true))
                behaviour.enabled = false;
            foreach (var collider in carry.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (var body in carry.GetComponentsInChildren<Rigidbody>(true))
            {
                body.detectCollisions = false;
                body.isKinematic = true;
            }
        }

        static void FillStreetTableaux(ResidentialLot.Plan plan, ResidentialBlockLife life,
                                       Transform pen, AmbientStandingClearance clearance,
                                       AmbientClipSet clips, System.Random rng,
                                       List<Vector3> occupied, Stood stood, int target)
        {
            var sites = AmbientSites(plan, rng);
            for (int n = 0; n < sites.Count && stood.People < target; n++)
            {
                var site = sites[n];
                float x = (site.I + 0.5f) * ResidentialLot.Cell + Between(rng, -0.65f, 0.65f);
                float z = (site.J + 0.5f) * ResidentialLot.Cell + Between(rng, -0.65f, 0.65f);
                var centre = new Vector3(x, Deck, z);
                if (!Apart(occupied, centre, 4.2f) || !clearance.IsClear(centre)) continue;

                bool pair = target - stood.People >= 2 && clips.Talk != null &&
                            (site.Use == ResidentialLot.Use.Court ||
                             site.Use == ResidentialLot.Use.Yard || rng.NextDouble() < 0.5);
                if (pair)
                {
                    float yaw = 90f * rng.Next(4) + Between(rng, -12f, 12f);
                    Vector3 across = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                    var claim = new Rect(x - 1.45f, z - 1.15f, 2.9f, 2.3f);
                    if (!Room(claim)) continue;

                    Vector3 a = centre - across * 0.72f;
                    Vector3 b = centre + across * 0.72f;
                    if (!clearance.IsClear(a) || !clearance.IsClear(b)) continue;
                    bool first = AddActor(life, pen, a, a, YawTowards(b - a), YawTowards(b - a),
                                          new ClipPair(clips.Talk), ResidentialBlockLife.Routine.Conversation,
                                          rng, "Neighbours talking", occupied, stood, 19f, 0.88f);
                    bool second = stood.People < target &&
                                  AddActor(life, pen, b, b, YawTowards(a - b), YawTowards(a - b),
                                           new ClipPair(clips.Talk), ResidentialBlockLife.Routine.Conversation,
                                           rng, "Neighbours talking", occupied, stood, 19f, 0.91f);
                    if (first || second) Claim(claim);
                    continue;
                }

                var solo = new Rect(x - 0.75f, z - 0.75f, 1.5f, 1.5f);
                if (!Room(solo)) continue;
                float face = StreetFacing(plan, site, rng);
                if (!AddActor(life, pen, centre, centre, face, face,
                              new ClipPair(clips.Idle), ResidentialBlockLife.Routine.Pose,
                              rng, site.Use == ResidentialLot.Use.Walkway
                                  ? "Waiting outside" : "Neighbour taking a break",
                              occupied, stood, 23f, 0.82f))
                    continue;
                Claim(solo);
            }
        }

        static void AddWalkingResident(ResidentialLot.Plan plan, ResidentialBlockLife life,
                                       Transform pen, AmbientStandingClearance clearance,
                                       AmbientClipSet clips, System.Random rng,
                                       List<Vector3> occupied, Stood stood, int target)
        {
            if (stood.People >= target) return;
            var sites = AmbientSites(plan, rng);
            for (int i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                Vector3 centre = new Vector3((site.I + 0.5f) * ResidentialLot.Cell,
                                             Deck,
                                             (site.J + 0.5f) * ResidentialLot.Cell);
                if (!Apart(occupied, centre, 2.8f)) continue;

                float startYaw = 45f * rng.Next(8);
                for (int direction = 0; direction < 8; direction++)
                {
                    Vector3 along = Quaternion.Euler(0f, startYaw + direction * 45f, 0f) *
                                    Vector3.forward;
                    Vector3 from = centre - along * 1.15f;
                    Vector3 to = centre + along * 1.15f;
                    if (!clearance.IsClear(from) || !clearance.IsClear(to) ||
                        !clearance.IsPathClear(from, to)) continue;

                    if (AddActor(life, pen, from, to, YawTowards(to - from),
                                 YawTowards(from - to), new ClipPair(clips.Idle),
                                 ResidentialBlockLife.Routine.Shuttle, rng,
                                 "Walking through the block", occupied, stood, 17f, 0.96f))
                        return;
                }
            }
        }

        static List<ResidentialBlockLife.Shelter> AmbientShelters(ResidentialLot.Plan plan)
        {
            var shelters = new List<ResidentialBlockLife.Shelter>();
            float cell = ResidentialLot.Cell;
            for (int i = 0; i < plan.Spots.Count; i++)
            {
                var spot = plan.Spots[i];
                if (spot?.Unit == null) continue;
                if (spot.AccessSide >= 0 && spot.EntranceAt >= 0)
                {
                    int side = spot.AccessSide;
                    float along = (spot.EntranceAt + 0.5f) * cell;
                    Vector3 edge = side switch
                    {
                        0 => new Vector3(along, VenueFloor(spot.Unit.Name), spot.J * cell),
                        1 => new Vector3((spot.I + spot.CW) * cell, VenueFloor(spot.Unit.Name), along),
                        2 => new Vector3(along, VenueFloor(spot.Unit.Name), (spot.J + spot.CD) * cell),
                        _ => new Vector3(spot.I * cell, VenueFloor(spot.Unit.Name), along),
                    };
                    Vector3 outward = SideVector(side);
                    AddShelter(shelters, edge + outward * 1.0f, edge - outward * 0.9f);
                }
                else if (spot.Unit.Kind == ResidentialKind.Amenity)
                {
                    int localSide = VenueDoorSide(spot.Unit);
                    float w = spot.Unit.CW * cell;
                    float d = spot.Unit.CD * cell;
                    float y = VenueFloor(spot.Unit.Name);
                    Vector3 edge = localSide switch
                    {
                        0 => new Vector3(w * 0.5f, y, 0f),
                        1 => new Vector3(w, y, d * 0.5f),
                        2 => new Vector3(w * 0.5f, y, d),
                        _ => new Vector3(0f, y, d * 0.5f),
                    };
                    Vector3 outward = Quaternion.Euler(0f, spot.Yaw, 0f) * SideVector(localSide);
                    Vector3 at = UnitPoint(spot, edge);
                    AddShelter(shelters, at + outward * 1.0f, at - outward * 1.0f);
                }
            }

            if (shelters.Count == 0)
            {
                int side = plan.Artery >= 0 && plan.Artery < 4 ? plan.Artery : 0;
                float width = plan.W * cell, depth = plan.D * cell;
                Vector3 edge = side switch
                {
                    0 => new Vector3(width * 0.5f, Deck, cell),
                    1 => new Vector3(width - cell, Deck, depth * 0.5f),
                    2 => new Vector3(width * 0.5f, Deck, depth - cell),
                    _ => new Vector3(cell, Deck, depth * 0.5f),
                };
                Vector3 outward = SideVector(side);
                AddShelter(shelters, edge + outward * 0.8f, edge - outward * 1.2f);
            }
            return shelters;
        }

        internal static bool TryRecoverAmbientPlan(string blockName,
                                                   out ResidentialLot.Plan plan)
        {
            plan = null;
            if (string.IsNullOrEmpty(blockName)) return false;
            string[] words = blockName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int width = 0, depth = 0, seed = 0;
            for (int i = 0; i < words.Length; i++)
            {
                int x = words[i].IndexOf('x');
                if (x > 0 && int.TryParse(words[i].Substring(0, x), out int w) &&
                    int.TryParse(words[i].Substring(x + 1), out int d))
                {
                    width = w;
                    depth = d;
                }
                if (words[i].Equals("seed", StringComparison.OrdinalIgnoreCase) &&
                    i + 1 < words.Length)
                    int.TryParse(words[i + 1], out seed);
            }
            if (width <= 0 || depth <= 0 || seed == 0) return false;

            string lower = blockName.ToLowerInvariant();
            string yard = lower.Contains("caryard") ? "caryard"
                : lower.Contains("skatepark") ? "skatepark"
                : lower.Contains("kosarkaskiteren") ? "kosarkaskiteren"
                : lower.Contains("gym") ? "gym"
                : null;
            if (yard != null)
            {
                plan = ResidentialLot.Yard(width, depth, seed, yard);
                return plan != null;
            }

            ResidentialLot.Klass? forced = lower.Contains("corner") ? ResidentialLot.Klass.Corner
                : lower.Contains("row") ? ResidentialLot.Klass.Row
                : lower.Contains("court") ? ResidentialLot.Klass.Court
                : lower.Contains("block") ? ResidentialLot.Klass.Block
                : null;
            string diner = lower.Contains("dinner2") ? "dinner2"
                : lower.Contains("dinner") ? "dinner"
                : null;
            plan = ResidentialLot.Roll(width, depth, seed, forced: forced,
                                       featuredDiner: diner);
            return plan != null;
        }

        internal static List<ResidentialBlockLife.Shelter> RecoverAmbientShelters(
            string blockName, out ResidentialLot.Plan plan)
        {
            return TryRecoverAmbientPlan(blockName, out plan)
                ? AmbientShelters(plan)
                : new List<ResidentialBlockLife.Shelter>();
        }

        static void AddShelter(List<ResidentialBlockLife.Shelter> shelters,
                               Vector3 outside, Vector3 inside)
        {
            for (int i = 0; i < shelters.Count; i++)
                if ((shelters[i].Outside - outside).sqrMagnitude < 1f) return;
            shelters.Add(new ResidentialBlockLife.Shelter(outside, inside));
        }

        static int VenueDoorSide(ResidentialUnit unit)
        {
            int best = -1, doors = 0;
            for (int side = 0; side < 4; side++)
                if (unit.Doors[side] > doors)
                {
                    best = side;
                    doors = unit.Doors[side];
                }
            return best >= 0 ? best : 0;
        }

        static Vector3 SideVector(int side) => side switch
        {
            0 => Vector3.back,
            1 => Vector3.right,
            2 => Vector3.forward,
            _ => Vector3.left,
        };

        static float VenueFloor(string name) => name switch
        {
            "gym" => 1.26f,
            "caryard" => ResidentialCaryard.Deck,
            "dinner" => 1.53f,
            "dinner2" => 0.07f,
            "kosarkaskiteren" => 0.64f,
            "skatepark" => 0.00f,
            _ => Deck,
        };

        static Rect SpotRoom(ResidentialLot.Spot spot)
        {
            const float margin = 0.42f;
            float cell = ResidentialLot.Cell;
            int cw = spot.CW > 0 ? spot.CW : ResidentialLot.Turn.Of(spot.Unit, spot.Yaw).CW;
            int cd = spot.CD > 0 ? spot.CD : ResidentialLot.Turn.Of(spot.Unit, spot.Yaw).CD;
            return new Rect(spot.I * cell + margin, spot.J * cell + margin,
                            Mathf.Max(0.5f, cw * cell - 2f * margin),
                            Mathf.Max(0.5f, cd * cell - 2f * margin));
        }

        static Vector3 UnitPoint(ResidentialLot.Spot spot, Vector3 local)
        {
            float cell = ResidentialLot.Cell;
            float w = spot.Unit.CW * cell;
            float d = spot.Unit.CD * cell;
            Vector3 offset = spot.Yaw switch
            {
                90 => new Vector3(0f, 0f, w),
                180 => new Vector3(w, 0f, d),
                270 => new Vector3(d, 0f, 0f),
                _ => Vector3.zero,
            };
            return new Vector3(spot.I * cell, 0f, spot.J * cell) + offset +
                   Quaternion.Euler(0f, spot.Yaw, 0f) * local;
        }

        static float UnitYaw(ResidentialLot.Spot spot, float localYaw) =>
            Mathf.Repeat(localYaw + spot.Yaw, 360f);

        static List<SeatAnchor> SeatAnchors(Transform root, ResidentialLot.Plan plan)
        {
            var seats = new List<SeatAnchor>();
            var positions = new List<Vector3>();
            var transforms = root.GetComponentsInChildren<Transform>(true);
            var benchOffsets = new[]
            {
                new Vector3(-0.55f, 0.50f - 0.428f, 0.258f),
                new Vector3(0.55f, 0.50f - 0.428f, 0.258f),
            };

            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                string name = t.name;
                Vector3 forward = root.InverseTransformDirection(t.forward);
                forward.y = 0f;
                float yaw = forward.sqrMagnitude > 0.01f ? YawTowards(forward) : 0f;

                if (name.IndexOf("SM_Prop_Chair_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Vector3 at = root.InverseTransformPoint(t.position);
                    // Palm/coffee chairs pivot on the floor. The sit clip's contact patch
                    // is 0.428 m above its root; putting the root a palm above the pivot and
                    // a little in front of the backrest lands the pelvis on the cushion.
                    at += forward.normalized * 0.14f;
                    at.y += 0.072f;
                    if (!Apart(positions, at, 0.42f)) continue;
                    positions.Add(at);
                    Vector3 departure = at + forward.normalized * 0.62f;
                    departure.y = at.y - 0.072f;
                    bool restaurant = RestaurantAncestor(t, root) || RestaurantAt(plan, at);
                    seats.Add(new SeatAnchor(at, departure, yaw, SeatKind.Chair, restaurant));
                    continue;
                }

                bool bench = name.IndexOf("SM_Prop_ParkBench_01", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("SM_Prop_Bench_Seat_01", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("SM_Prop_Bench_Seat_02", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("SM_Prop_Planter_Bench_01", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!bench) continue;
                for (int seat = 0; seat < benchOffsets.Length; seat++)
                {
                    Vector3 at = root.InverseTransformPoint(t.TransformPoint(benchOffsets[seat]));
                    if (!Apart(positions, at, 0.42f)) continue;
                    positions.Add(at);
                    Vector3 departure = at + forward.normalized * 0.62f;
                    departure.y = root.InverseTransformPoint(t.position).y;
                    bool restaurant = RestaurantAncestor(t, root) || RestaurantAt(plan, at);
                    seats.Add(new SeatAnchor(at, departure, yaw, SeatKind.Bench, restaurant));
                }
            }
            return seats;
        }

        internal static List<SeatAnchor> RecoverAmbientSeats(Transform root,
                                                             ResidentialLot.Plan plan = null) =>
            SeatAnchors(root, plan);

        static bool TrySeatDeparture(AmbientStandingClearance clearance, SeatAnchor seat,
                                     out Vector3 departure)
        {
            departure = seat.Departure;
            if (clearance == null) return false;
            if (clearance.IsClear(departure)) return true;
            return clearance.TryNearest(departure, clearance.ContentRect,
                                        out departure, 2.4f);
        }

        static bool RestaurantAt(ResidentialLot.Plan plan, Vector3 at)
        {
            if (plan == null) return false;
            float cell = ResidentialLot.Cell;
            for (int i = 0; i < plan.Spots.Count; i++)
            {
                var spot = plan.Spots[i];
                string name = spot?.Unit?.Name;
                if (name != "dinner" && name != "dinner2") continue;
                if (at.x >= spot.I * cell - 0.15f && at.x <= (spot.I + spot.CW) * cell + 0.15f &&
                    at.z >= spot.J * cell - 0.15f && at.z <= (spot.J + spot.CD) * cell + 0.15f)
                    return true;
            }
            return false;
        }

        static bool RestaurantAncestor(Transform item, Transform stop)
        {
            for (var at = item; at != null && at != stop; at = at.parent)
            {
                string name = at.name;
                if (name.IndexOf("dinner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("diner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("restaurant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("burger", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("coffee", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        static List<PropAnchor> BinAnchors(Transform root)
        {
            var bins = new List<PropAnchor>();
            var positions = new List<Vector3>();
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                string name = t.name;
                bool bin = name.IndexOf("Trashbin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           name.IndexOf("Trash_Bin", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!bin) continue;
                Vector3 at = root.InverseTransformPoint(t.position);
                if (!Apart(positions, at, 0.7f)) continue;
                Vector3 forward = root.InverseTransformDirection(t.forward);
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                positions.Add(at);
                bins.Add(new PropAnchor(at, forward.normalized));
            }
            return bins;
        }

        static void TrashLeg(ResidentialLot.Plan plan, PropAnchor bin,
                             out Vector3 from, out Vector3 to, out float yaw)
        {
            Vector3 direction = bin.Forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
            direction.Normalize();
            float y = GroundHeight(plan, bin.At);

            Vector3 candidate = new Vector3(bin.At.x, y, bin.At.z) + direction * 3.6f;
            if (!InsideWalkable(plan, candidate))
            {
                direction = -direction;
                candidate = new Vector3(bin.At.x, y, bin.At.z) + direction * 3.6f;
            }
            if (!InsideWalkable(plan, candidate))
            {
                direction = Vector3.Cross(Vector3.up, direction).normalized;
                candidate = new Vector3(bin.At.x, y, bin.At.z) + direction * 3.2f;
            }

            from = candidate;
            to = new Vector3(bin.At.x, y, bin.At.z) + direction * 0.95f;
            yaw = YawTowards(-direction);
        }

        static float GroundHeight(ResidentialLot.Plan plan, Vector3 at)
        {
            float cell = ResidentialLot.Cell;
            for (int i = 0; i < plan.Spots.Count; i++)
            {
                var spot = plan.Spots[i];
                if (spot?.Unit == null || spot.Unit.Kind != ResidentialKind.Amenity) continue;
                if (at.x >= spot.I * cell && at.x <= (spot.I + spot.CW) * cell &&
                    at.z >= spot.J * cell && at.z <= (spot.J + spot.CD) * cell)
                    return VenueFloor(spot.Unit.Name);
            }
            return Deck;
        }

        static bool InsideWalkable(ResidentialLot.Plan plan, Vector3 at)
        {
            int i = Mathf.FloorToInt(at.x / ResidentialLot.Cell);
            int j = Mathf.FloorToInt(at.z / ResidentialLot.Cell);
            if (i < 0 || j < 0 || i >= plan.W || j >= plan.D) return false;
            var use = plan.Ground[i, j];
            return use == ResidentialLot.Use.Walkway || use == ResidentialLot.Use.Paved ||
                   use == ResidentialLot.Use.Court || use == ResidentialLot.Use.Yard ||
                   use == ResidentialLot.Use.Cafe || use == ResidentialLot.Use.Park ||
                   use == ResidentialLot.Use.Forecourt;
        }

        static List<AmbientSite> AmbientSites(ResidentialLot.Plan plan, System.Random rng)
        {
            var preferred = new List<AmbientSite>();
            var pavement = new List<AmbientSite>();
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    var use = plan.Ground[i, j];
                    if (use == ResidentialLot.Use.Court || use == ResidentialLot.Use.Yard ||
                        use == ResidentialLot.Use.Paved)
                        preferred.Add(new AmbientSite(i, j, use));
                    else if (use == ResidentialLot.Use.Walkway && FrontageNeighbour(plan, i, j))
                        pavement.Add(new AmbientSite(i, j, use));
                }

            Shuffle(preferred, rng);
            Shuffle(pavement, rng);
            preferred.AddRange(pavement);
            return preferred;
        }

        static bool FrontageNeighbour(ResidentialLot.Plan plan, int i, int j)
        {
            for (int side = 0; side < 4; side++)
            {
                int x = i + ResidentialLot.Step[side, 0];
                int y = j + ResidentialLot.Step[side, 1];
                if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                var use = plan.Ground[x, y];
                if (use == ResidentialLot.Use.Building || use == ResidentialLot.Use.Forecourt ||
                    use == ResidentialLot.Use.Cafe || use == ResidentialLot.Use.Paved)
                    return true;
            }
            return false;
        }

        static bool Apart(List<Vector3> occupied, Vector3 at, float distance)
        {
            float square = distance * distance;
            for (int i = 0; i < occupied.Count; i++)
            {
                Vector3 delta = occupied[i] - at;
                delta.y = 0f;
                if (delta.sqrMagnitude < square) return false;
            }
            return true;
        }

        static float StreetFacing(ResidentialLot.Plan plan, AmbientSite site, System.Random rng)
        {
            float cx = plan.W * ResidentialLot.Cell * 0.5f;
            float cz = plan.D * ResidentialLot.Cell * 0.5f;
            var fromCentre = new Vector3((site.I + 0.5f) * ResidentialLot.Cell - cx, 0f,
                                         (site.J + 0.5f) * ResidentialLot.Cell - cz);
            if (fromCentre.sqrMagnitude < 0.01f) return 90f * rng.Next(4);
            return YawTowards(fromCentre) + Between(rng, -20f, 20f);
        }

        static float YawTowards(Vector3 direction) =>
            Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int swap = rng.Next(i + 1);
                (list[i], list[swap]) = (list[swap], list[i]);
            }
        }

        static AmbientClipSet GetAmbientClips()
        {
            if (_ambientClips != null) return _ambientClips;
            var idle = DemoAssetLoad.Load<AnimationClip>(AmbientIdle) ?? CrewKit.StockIdle;
            var talk = DemoAssetLoad.Load<AnimationClip>(AmbientTalk) ?? idle;
            var walk = CrewKit.StockWalk ?? DemoAssetLoad.Load<AnimationClip>(AmbientWalk);
            _ambientClips = new AmbientClipSet
            {
                Idle = idle,
                Talk = talk,
                Sit = DemoAssetLoad.Load<AnimationClip>(AmbientSit) ?? idle,
                StandUp = DemoAssetLoad.Load<AnimationClip>(AmbientStandUp) ?? idle,
                Walk = walk,
                Run = CrewKit.StockRun ?? walk,
                Gym = new ClipPair(
                    IdleFbx("Masculine/Stretch/Actions/A_POLY_IDL_Stretch_Squat_Masc.fbx"),
                    IdleFbx("Feminine/Stretch/Actions/A_POLY_IDL_Stretch_Squat_Femn.fbx")),
                Inspect = new ClipPair(
                    IdleFbx("Masculine/Inspect/Actions/A_POLY_IDL_Inspect_Hands_Masc.fbx"),
                    IdleFbx("Feminine/Inspect/Actions/A_POLY_IDL_Inspect_Hands_Femn.fbx")),
            };
            return _ambientClips;
        }

        /// <summary>Restores pose intent for ResidentialDemo scenes baked before the
        /// block controller's route data became serialized.</summary>
        internal static AnimationClip RecoverAmbientPose(string label, bool female)
        {
            var clips = GetAmbientClips();
            if (label.IndexOf("Training", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("Warming", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("Stretching", StringComparison.OrdinalIgnoreCase) >= 0)
                return clips.Gym.Pick(female, clips.Idle);
            if (label.IndexOf("sitting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("guest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("customer", StringComparison.OrdinalIgnoreCase) >= 0)
                return clips.Sit ?? clips.Idle;
            if (label.IndexOf("talk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("salesman", StringComparison.OrdinalIgnoreCase) >= 0)
                return clips.Talk ?? clips.Idle;
            if (label.IndexOf("Inspect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("Looking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("Comparing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("Watching", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("dumbbells", StringComparison.OrdinalIgnoreCase) >= 0)
                return clips.Inspect.Pick(female, clips.Idle);
            return clips.Idle ?? clips.Talk;
        }

        internal static AnimationClip RecoverAmbientSecondary(string label)
        {
            if (label.IndexOf("guest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("customer", StringComparison.OrdinalIgnoreCase) >= 0)
                return GetAmbientClips().Talk;
            return null;
        }

        internal static AnimationClip RecoverAmbientStandUp() => GetAmbientClips().StandUp;

        /// <summary>
        /// The first saved ResidentialDemo bake predates restaurant occupants. Its people
        /// can be rebound from the scene, but figures that were never baked cannot be
        /// recovered. In editor Play only, add a small seated group to real diner/cafe
        /// chairs so that old review scene gains the same tableau without resaving it.
        /// Fresh and streamed compositions already create these figures normally.
        /// </summary>
        internal static int SupplementLegacyCafePeople(ResidentialBlockLife life,
                                                        Transform block,
                                                        AmbientStandingClearance clearance)
        {
            if (!Application.isPlaying || life == null || block == null) return 0;
            TryRecoverAmbientPlan(block.name, out ResidentialLot.Plan recoveredPlan);
            var seats = RecoverAmbientSeats(block, recoveredPlan);
            if (seats.Count == 0) return 0;

            bool hasRestaurantGuest = false, hasCafeGuest = false;
            var occupied = new List<Vector3>();
            for (int i = 0; i < life.transform.childCount; i++)
            {
                var child = life.transform.GetChild(i);
                occupied.Add(child.localPosition);
                string label = child.name;
                if (label.IndexOf("Restaurant guest", StringComparison.OrdinalIgnoreCase) >= 0)
                    hasRestaurantGuest = true;
                if (label.IndexOf("Cafe customer", StringComparison.OrdinalIgnoreCase) >= 0)
                    hasCafeGuest = true;
            }

            int seed = 1987;
            string key = block.name;
            for (int i = 0; i < key.Length; i++) seed = unchecked(seed * 31 + key[i]);
            var rng = new System.Random(seed);
            Shuffle(seats, rng);
            var clips = GetAmbientClips();
            var stood = new Stood();
            int added = 0;

            int AddGroup(bool restaurant, int wanted, string label)
            {
                int group = 0;
                for (int i = 0; i < seats.Count && group < wanted; i++)
                {
                    var seat = seats[i];
                    if (seat.Restaurant != restaurant || !Apart(occupied, seat.At, 0.72f)) continue;
                    bool hasDeparture = TrySeatDeparture(clearance, seat, out Vector3 departure);
                    if (!AddActor(life, life.transform, seat.At, seat.At, seat.Yaw, seat.Yaw,
                                  new ClipPair(clips.Sit),
                                  ResidentialBlockLife.Routine.SeatedConversation,
                                  rng, label, occupied, stood, 24f, 0.94f,
                                  secondary: new ClipPair(clips.Talk),
                                  hasDeparture: hasDeparture, departure: departure,
                                  legacyRuntime: true))
                        continue;
                    group++;
                }
                return group;
            }

            if (!hasRestaurantGuest) added += AddGroup(true, 4, "Restaurant guest talking");
            if (!hasCafeGuest) added += AddGroup(false, 2, "Cafe customer talking");
            return added;
        }

        static AnimationClip IdleFbx(string relative)
        {
#if UNITY_EDITOR
            string path = IdlePack + relative;
            foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
#endif
            return null;
        }
    }
}
