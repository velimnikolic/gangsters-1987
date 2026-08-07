using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Entities;
using LivingCity.Generation;

namespace LivingCity.Tests
{
    /// <summary>
    /// Properties of the pedestrian model that a person walking through another person, or a
    /// pavement freezing solid, would violate. Same discipline as TrafficModelTests: no
    /// UnityEngine.Object, nothing logs, failures come back as data, and the co-simulations
    /// below run the SAME Probe-Blend-clamp loop the patched HumanBehavior and the agent's
    /// off-graph walk run, rebuilt from the pure PedestrianSteering pieces.
    /// </summary>
    public static class PedestrianModelTests
    {
        const float Dt = 1f / 60f;
        const float PersonalSpace = 1.2f;
        const float MinSeparation = 0.6f;
        const float Radius = 0.3f;
        const float WalkSpeed = 2.5f;
        const float OncomingRange = PersonalSpace * 3f;

        /// <summary>Runs every check. An empty list means everything passed.</summary>
        public static List<string> Run()
        {
            var failures = new List<string>();

            SeparationIsLocalAndBounded(failures);
            AdvanceCapGeometry(failures);
            ClearanceDecayIsBoundedAndMonotone(failures);
            BlendNeverFlipsToARandomHeading(failures);
            OncomingBiasSendsBothToTheirOwnRight(failures);
            HeadOnPairPassesWithoutTouching(failures);
            WalkerStopsShortOfAStandingCrowd(failures);
            CrossingCrowdNeitherOverlapsNorDeadlocks(failures);
            PairingBooksNobodyTwice(failures);
            PairingIsDeterministic(failures);
            PairingPrefersTheNearest(failures);
            PairingMatchesTheFullScan(failures);
            RegistryCellsCoverTheProbeReach(failures);
            RegistryCellKeysDoNotCollide(failures);
            DoorNameFilterRejectsWhatItMust(failures);
            SeatsPutTheSittingPelvisOnTheSlats(failures);
            CrowdWeightsAreTheSharesTheyClaim(failures);
            CrowdSkipsGroupsThatCannotDeal(failures);
            CrowdIsDeterministicForASeed(failures);
            RoadCrossSectionsClassifyAsMeasured(failures);
            RoadTileNamesRoundTrip(failures);
            CurveBandCoversAsphaltAndSparesPavement(failures);
            CrossingPairMidpointIsRefused(failures);

            return failures;
        }

        /// <summary>
        /// The crowd mix, restated independently of the bootstrap that builds it - same reason
        /// the bench table above is restated rather than imported.
        /// </summary>
        static readonly (string Label, float Weight, int Models)[] Crowd =
        {
            ("Suits", 3f, 5), ("Civilians", 4f, 8), ("Workers", 2f, 5),
            ("Fringe", 1f, 5), ("Children", 0.5f, 1),
        };

        static PrefabDatabase.WeightedPrefabs[] CrowdGroups()
        {
            var groups = new PrefabDatabase.WeightedPrefabs[Crowd.Length];
            for (var i = 0; i < Crowd.Length; i++)
                groups[i] = new PrefabDatabase.WeightedPrefabs
                {
                    label = Crowd[i].Label,
                    weight = Crowd[i].Weight,
                    // Null entries: the picker's group roll never dereferences a prefab, and a
                    // real GameObject cannot be constructed outside the Editor anyway.
                    prefabs = new GameObject[Crowd[i].Models],
                };
            return groups;
        }

        /// <summary>
        /// Reaches the private group roll directly. The public Next() cannot answer this: with
        /// no real prefabs to hand back it reports nothing about WHICH group it drew from, and
        /// that split is the entire point of the change - a flat list made the pack's one
        /// gangster exactly as common as its lifeguard.
        /// </summary>
        static int RollGroup(PedestrianPicker picker) =>
            (int)typeof(PedestrianPicker)
                .GetMethod("PickGroupIndex", System.Reflection.BindingFlags.NonPublic |
                                             System.Reflection.BindingFlags.Instance)
                .Invoke(picker, null);

        /// <summary>
        /// Each group has to take its weight's share of the street. A picker that always
        /// returned 0, or that fell through to the fallback on every roll, still compiles and
        /// still spawns people - it just quietly spawns one group of them.
        /// </summary>
        static void CrowdWeightsAreTheSharesTheyClaim(List<string> failures)
        {
            const int Rolls = 200000;
            const float Tolerance = 0.01f;

            var picker = new PedestrianPicker(CrowdGroups(), new System.Random(12345));
            var hits = new int[Crowd.Length];

            for (var i = 0; i < Rolls; i++)
                hits[RollGroup(picker)]++;

            var total = 0f;
            foreach (var group in Crowd)
                total += group.Weight;

            for (var i = 0; i < Crowd.Length; i++)
            {
                var expected = Crowd[i].Weight / total;
                var actual = hits[i] / (float)Rolls;

                if (Mathf.Abs(actual - expected) > Tolerance)
                    failures.Add($"Crowd share for {Crowd[i].Label}: expected " +
                                 $"{expected:P1}, got {actual:P1} over {Rolls} rolls.");
            }
        }

        /// <summary>
        /// A group with no weight or no prefabs must be dropped at construction, not rolled and
        /// then found empty: a zero-weight group left in the list still occupies an index, and
        /// PickGroupIndex's fallback would hand it out on every roll that overshoots.
        /// </summary>
        static void CrowdSkipsGroupsThatCannotDeal(List<string> failures)
        {
            var groups = new[]
            {
                new PrefabDatabase.WeightedPrefabs
                    { label = "Real", weight = 1f, prefabs = new GameObject[2] },
                new PrefabDatabase.WeightedPrefabs
                    { label = "NoWeight", weight = 0f, prefabs = new GameObject[2] },
                new PrefabDatabase.WeightedPrefabs
                    { label = "NoPrefabs", weight = 5f, prefabs = new GameObject[0] },
            };

            var picker = new PedestrianPicker(groups, new System.Random(7));

            for (var i = 0; i < 200; i++)
                if (RollGroup(picker) != 0)
                {
                    failures.Add("Crowd rolled a group that has no weight or no prefabs.");
                    break;
                }

            if (new PedestrianPicker(null, new System.Random(7)).IsEmpty == false)
                failures.Add("Crowd built from a null catalogue does not report itself empty.");

            if (!new PedestrianPicker(new[] { groups[1], groups[2] }, new System.Random(7)).IsEmpty)
                failures.Add("Crowd of only unusable groups does not report itself empty.");
        }

        /// <summary>
        /// One seed, one city. The picker consumes the pedestrian RNG stream, so a roll that
        /// varied run to run would desync every draw made after it.
        /// </summary>
        static void CrowdIsDeterministicForASeed(List<string> failures)
        {
            var first = new PedestrianPicker(CrowdGroups(), new System.Random(99));
            var second = new PedestrianPicker(CrowdGroups(), new System.Random(99));

            for (var i = 0; i < 500; i++)
                if (RollGroup(first) != RollGroup(second))
                {
                    failures.Add("Crowd rolls diverge between two pickers built on the same seed.");
                    break;
                }
        }

        /// <summary>
        /// Bench geometry, measured off the FBXs: seat top, the Z span of the seat slab, and
        /// the half-width the slats run to. Restated here rather than imported so the test is
        /// an independent statement of the measurement - a table checking itself proves
        /// nothing.
        /// </summary>
        struct BenchShape
        {
            public string Prefab;
            public float SeatTop;
            public float SlabBack;
            public float SlabFront;
            public float HalfWidth;
            public bool Backrest;
        }

        static readonly BenchShape[] Benches =
        {
            new BenchShape { Prefab = "bench-old", SeatTop = 0.428f,
                             SlabBack = -0.245f, SlabFront = 0.242f, HalfWidth = 0.834f,
                             Backrest = true },
            new BenchShape { Prefab = "bench-forest", SeatTop = 0.401f,
                             SlabBack = -0.234f, SlabFront = 0.234f, HalfWidth = 1.464f,
                             Backrest = false },
        };

        /// <summary>
        /// The seat offsets are derived from the authored sit pose, and nothing at runtime
        /// would complain if one drifted - a sitter half a metre out still sits, just in mid
        /// air. So state the derivation as assertions.
        ///
        /// A seat is a place for the CONTACT PATCH, not for the root: the root ends up
        /// SitContactHeight below it and the pelvis SitPelvisBack behind it. Both have to
        /// land on the bench.
        /// </summary>
        static void SeatsPutTheSittingPelvisOnTheSlats(List<string> failures)
        {
            foreach (var bench in Benches)
            {
                var seats = InteractionMarkers.SeatsFor(bench.Prefab);
                if (seats == null || seats.Length == 0)
                {
                    failures.Add($"seats: {bench.Prefab} has no seat table");
                    continue;
                }

                for (var i = 0; i < seats.Length; i++)
                {
                    var seat = seats[i];
                    var where = $"seats: {bench.Prefab}[{i}]";

                    // The seat names the surface, so its Y is the surface.
                    if (Mathf.Abs(seat.y - bench.SeatTop) > 0.005f)
                        failures.Add($"{where} Y {seat.y:F3} is not the seat top {bench.SeatTop:F3}");

                    // Where the weight actually goes. The pose reclines, so on a bench that HAS
                    // a backrest the pelvis has to be back against it - a hand's width of the
                    // slab's back edge, clear of it but not out on the open slats, which is
                    // where a plain "is it on the bench" range check would happily leave it.
                    // A backless bench has nothing to sit back against, so it wants the middle.
                    var pelvisZ = seat.z - PedestrianAnimation.SitPelvisBack;
                    var nearest = bench.Backrest ? bench.SlabBack + 0.06f
                                                 : (bench.SlabBack + bench.SlabFront) * 0.5f;
                    if (Mathf.Abs(pelvisZ - nearest) > 0.06f)
                        failures.Add($"{where} seats the pelvis at z {pelvisZ:F3}, not within " +
                                     $"0.06 of {nearest:F3} on a bench " +
                                     (bench.Backrest ? "with a backrest" : "without one"));

                    if (pelvisZ < bench.SlabBack + 0.02f || pelvisZ > bench.SlabFront - 0.02f)
                        failures.Add($"{where} seats the pelvis at z {pelvisZ:F3}, off the " +
                                     $"slats {bench.SlabBack:F3}..{bench.SlabFront:F3}");

                    // An adult (humanScale 1) has to end up standing on the pavement, not
                    // hovering over it or buried in it.
                    var adultRootY = seat.y - PedestrianAnimation.SitContactHeight;
                    if (adultRootY > 0.005f || adultRootY < -0.05f)
                        failures.Add($"{where} puts an adult root at y {adultRootY:F3}");

                    if (Mathf.Abs(seat.x) + 0.25f > bench.HalfWidth)
                        failures.Add($"{where} X {seat.x:F3} hangs a sitter off a bench of " +
                                     $"half-width {bench.HalfWidth:F3}");
                }

                for (var i = 1; i < seats.Length; i++)
                    if (Mathf.Abs(seats[i].x - seats[i - 1].x) < 0.6f)
                        failures.Add($"seats: {bench.Prefab} seats {i - 1} and {i} overlap " +
                                     $"({Mathf.Abs(seats[i].x - seats[i - 1].x):F3} apart)");
            }
        }

        /// <summary>
        /// The checks that need a real generated city rather than a synthetic crowd. Separate
        /// entry point because it mutates the config's seed - pass a clone, never the
        /// project's own asset, the ZoneFrequencySweep rule.
        /// </summary>
        public static List<string> RunCityRules(CityConfig config, int seeds = 40)
        {
            var failures = new List<string>();

            DoorsFrontTheStreetAndOnlyTheStreet(failures, config, seeds);
            IndustrialAndParkingAdmitNobody(failures, config);

            return failures;
        }

        // ------------------------------------------------------------------ the loop

        struct Walker
        {
            public Vector3 Pos;
            public Vector3 Forward;
            public Vector3 Target;
            public float StalledFor;
            public bool Arrived;
        }

        /// <summary>
        /// One frame of the real movement loop for one walker: probe every other body, blend
        /// the push into the heading, clamp the step, run the stall clock - the combination
        /// HumanBehavior's patch and PedestrianRegistry.Probe run between them.
        /// </summary>
        static void Step(Walker[] walkers, int self)
        {
            ref var w = ref walkers[self];
            if (w.Arrived)
                return;

            var desired = w.Target - w.Pos;
            if (PedestrianSteering.Flat(desired).magnitude < 0.3f)
            {
                w.Arrived = true;
                return;
            }

            var push = Vector3.zero;
            var allowed = float.PositiveInfinity;
            var clearance = PedestrianSteering.EffectiveClearance(MinSeparation, w.StalledFor);

            for (var i = 0; i < walkers.Length; i++)
            {
                if (i == self)
                    continue;

                var other = walkers[i];
                push += PedestrianSteering.SeparationPush(w.Pos, other.Pos, PersonalSpace);
                push += PedestrianSteering.OncomingBias(desired, other.Pos - w.Pos,
                    other.Forward, OncomingRange);

                var cap = PedestrianSteering.AdvanceCap(w.Pos, desired, other.Pos,
                    Radius * 2f, clearance);
                if (cap < allowed)
                    allowed = cap;
            }

            var heading = PedestrianSteering.Blend(desired, push);
            var advance = Mathf.Min(WalkSpeed * Dt, allowed);

            w.Pos += heading * advance;
            if (heading != Vector3.zero)
                w.Forward = heading;

            // PedestrianRegistry.UpdateStall, faithfully.
            var moving = advance / Dt >= PedestrianSteering.StalledSpeed;
            if (moving || allowed > 0.02f)
                w.StalledFor = 0f;
            else
                w.StalledFor += Dt;
        }

        static float MinPairDistance(Walker[] walkers)
        {
            var min = float.PositiveInfinity;
            for (var i = 0; i < walkers.Length; i++)
                for (var j = i + 1; j < walkers.Length; j++)
                {
                    var d = PedestrianSteering.Flat(walkers[j].Pos - walkers[i].Pos).magnitude;
                    if (d < min)
                        min = d;
                }
            return min;
        }

        // ------------------------------------------------------------------ primitives

        static void SeparationIsLocalAndBounded(List<string> failures)
        {
            var origin = Vector3.zero;

            if (PedestrianSteering.SeparationPush(origin, new Vector3(PersonalSpace, 0f, 0f), PersonalSpace)
                != Vector3.zero)
                failures.Add("SeparationPush: non-zero at exactly personal space.");

            if (PedestrianSteering.SeparationPush(origin, new Vector3(5f, 0f, 0f), PersonalSpace)
                != Vector3.zero)
                failures.Add("SeparationPush: non-zero far outside personal space.");

            var near = PedestrianSteering.SeparationPush(origin, new Vector3(0.2f, 0f, 0f), PersonalSpace);
            var far = PedestrianSteering.SeparationPush(origin, new Vector3(0.9f, 0f, 0f), PersonalSpace);

            if (near.x >= 0f)
                failures.Add("SeparationPush: does not push AWAY from the neighbour.");
            if (near.magnitude > 1f + 1e-4f)
                failures.Add("SeparationPush: exceeds a unit vector at close range.");
            if (near.magnitude <= far.magnitude)
                failures.Add("SeparationPush: does not grow as the bodies close.");
        }

        static void AdvanceCapGeometry(List<string> failures)
        {
            var origin = Vector3.zero;
            var ahead = new Vector3(1f, 0f, 0f);
            var radiusSum = Radius * 2f;

            if (!float.IsPositiveInfinity(
                PedestrianSteering.AdvanceCap(origin, ahead, new Vector3(-2f, 0f, 0f), radiusSum, MinSeparation)))
                failures.Add("AdvanceCap: a body BEHIND constrains the step.");

            if (!float.IsPositiveInfinity(
                PedestrianSteering.AdvanceCap(origin, ahead, new Vector3(2f, 0f, 2f), radiusSum, MinSeparation)))
                failures.Add("AdvanceCap: a body clear to the side constrains the step.");

            if (!float.IsPositiveInfinity(
                PedestrianSteering.AdvanceCap(origin, ahead, new Vector3(0.3f, 0f, 0f), radiusSum, MinSeparation)))
                failures.Add("AdvanceCap: an already-overlapped body constrains the step - " +
                             "the clamp cannot un-close a closed gap and must leave untangling " +
                             "to the separation push.");

            var cap = PedestrianSteering.AdvanceCap(origin, ahead, new Vector3(2f, 0f, 0f), radiusSum, MinSeparation);
            var expected = 2f - radiusSum - MinSeparation;
            if (Mathf.Abs(cap - expected) > 1e-4f)
                failures.Add($"AdvanceCap: dead-ahead blocker gives {cap:F3}, expected {expected:F3}.");

            if (PedestrianSteering.AdvanceCap(origin, ahead, new Vector3(0.7f, 0f, 0f), radiusSum, MinSeparation)
                > 0f)
                failures.Add("AdvanceCap: a gap inside the clearance still grants advance.");
        }

        static void ClearanceDecayIsBoundedAndMonotone(List<string> failures)
        {
            if (PedestrianSteering.EffectiveClearance(MinSeparation, 0f) != MinSeparation)
                failures.Add("EffectiveClearance: fresh walker does not get the full clearance.");

            if (Mathf.Abs(PedestrianSteering.EffectiveClearance(MinSeparation, 999f)
                          - PedestrianSteering.ClearanceFloor) > 1e-5f)
                failures.Add("EffectiveClearance: long-stalled walker does not reach the floor.");

            var previous = float.PositiveInfinity;
            for (var t = 0f; t < 12f; t += 0.25f)
            {
                var clearance = PedestrianSteering.EffectiveClearance(MinSeparation, t);
                if (clearance > previous + 1e-5f)
                    failures.Add($"EffectiveClearance: not monotone at stalled={t:F2}.");
                if (clearance < PedestrianSteering.ClearanceFloor - 1e-5f)
                    failures.Add($"EffectiveClearance: below the floor at stalled={t:F2}.");
                previous = clearance;
            }
        }

        static void BlendNeverFlipsToARandomHeading(List<string> failures)
        {
            var desired = new Vector3(1f, 0f, 0f);
            var cancelling = new Vector3(-1f / PedestrianSteering.SeparationWeight, 0f, 0f);

            if (PedestrianSteering.Blend(desired, cancelling) != Vector3.zero)
                failures.Add("Blend: a push that exactly cancels the goal should mean " +
                             "'stand still', not an arbitrary heading.");

            var bent = PedestrianSteering.Blend(desired, new Vector3(0f, 0f, 0.5f));
            if (bent.x <= 0f)
                failures.Add("Blend: a sideways push reversed forward progress.");
            if (Mathf.Abs(bent.magnitude - 1f) > 1e-4f)
                failures.Add("Blend: result is not unit length.");
        }

        static void OncomingBiasSendsBothToTheirOwnRight(List<string> failures)
        {
            // A walks +X, B walks -X, each sees the other 2m ahead.
            var biasA = PedestrianSteering.OncomingBias(new Vector3(1f, 0f, 0f),
                new Vector3(2f, 0f, 0f), new Vector3(-1f, 0f, 0f), OncomingRange);
            var biasB = PedestrianSteering.OncomingBias(new Vector3(-1f, 0f, 0f),
                new Vector3(-2f, 0f, 0f), new Vector3(1f, 0f, 0f), OncomingRange);

            if (biasA == Vector3.zero || biasB == Vector3.zero)
                failures.Add("OncomingBias: a genuine head-on meeting produced no bias.");
            if (Vector3.Dot(biasA, biasB) >= 0f)
                failures.Add("OncomingBias: the pair is not biased to OPPOSITE world sides - " +
                             "they would dodge into each other.");

            if (PedestrianSteering.OncomingBias(new Vector3(1f, 0f, 0f),
                    new Vector3(-2f, 0f, 0f), new Vector3(-1f, 0f, 0f), OncomingRange) != Vector3.zero)
                failures.Add("OncomingBias: someone BEHIND still produces a bias.");

            if (PedestrianSteering.OncomingBias(new Vector3(1f, 0f, 0f),
                    new Vector3(20f, 0f, 0f), new Vector3(-1f, 0f, 0f), OncomingRange) != Vector3.zero)
                failures.Add("OncomingBias: someone across the square already produces a bias - " +
                             "every walker on a long pavement would drift kerbward.");

            if (PedestrianSteering.OncomingBias(new Vector3(1f, 0f, 0f),
                    new Vector3(2f, 0f, 0f), new Vector3(1f, 0f, 0f), OncomingRange) != Vector3.zero)
                failures.Add("OncomingBias: someone walking AWAY ahead still produces a bias.");
        }

        // ------------------------------------------------------------------ co-simulations

        /// <summary>
        /// The defect this whole layer exists for: two people walking straight at each other
        /// must pass - not stop dead, and never occupy the same ground.
        /// </summary>
        static void HeadOnPairPassesWithoutTouching(List<string> failures)
        {
            var walkers = new[]
            {
                new Walker { Pos = new Vector3(0f, 0f, 0f), Forward = new Vector3(1f, 0f, 0f),
                             Target = new Vector3(20f, 0f, 0f) },
                new Walker { Pos = new Vector3(8f, 0f, 0f), Forward = new Vector3(-1f, 0f, 0f),
                             Target = new Vector3(-12f, 0f, 0f) },
            };

            var minDistance = float.PositiveInfinity;
            for (var frame = 0; frame < 60 * 15; frame++)
            {
                for (var i = 0; i < walkers.Length; i++)
                    Step(walkers, i);
                minDistance = Mathf.Min(minDistance, MinPairDistance(walkers));
            }

            if (minDistance < Radius * 2f - 1e-3f)
                failures.Add($"Head-on pair: bodies overlapped (closest {minDistance:F3}m, " +
                             $"bodies are {Radius * 2f:F2}m).");

            if (!walkers[0].Arrived || !walkers[1].Arrived)
                failures.Add("Head-on pair: did not pass each other in 15 seconds - " +
                             $"A at {walkers[0].Pos.x:F1}, B at {walkers[1].Pos.x:F1}.");
        }

        /// <summary>
        /// A standing conversation is an obstacle, not a wall: a walker aimed straight at a
        /// stationary pair must stop short of them - full clearance while fresh - and never
        /// touch them, however long it waits.
        /// </summary>
        static void WalkerStopsShortOfAStandingCrowd(List<string> failures)
        {
            var walkers = new[]
            {
                new Walker { Pos = new Vector3(0f, 0f, 0f), Forward = new Vector3(1f, 0f, 0f),
                             Target = new Vector3(4f, 0f, 0f) },
                // The talker: never stepped, target = own position.
                new Walker { Pos = new Vector3(4f, 0f, 0f), Forward = new Vector3(0f, 0f, 1f),
                             Target = new Vector3(4f, 0f, 0f), Arrived = true },
            };

            var minDistance = float.PositiveInfinity;
            for (var frame = 0; frame < 60 * 20; frame++)
            {
                Step(walkers, 0);
                minDistance = Mathf.Min(minDistance, MinPairDistance(walkers));
            }

            if (minDistance < Radius * 2f + PedestrianSteering.ClearanceFloor - 1e-3f)
                failures.Add($"Standing-crowd: walker closed inside the clearance floor " +
                             $"({minDistance:F3}m).");
        }

        /// <summary>
        /// Four walkers crossing one point from four directions: nobody overlaps, and - the
        /// deadlock half - everybody gets where they were going. This is the pavement
        /// equivalent of the cars' mutual-block ring.
        /// </summary>
        static void CrossingCrowdNeitherOverlapsNorDeadlocks(List<string> failures)
        {
            var walkers = new[]
            {
                new Walker { Pos = new Vector3(-6f, 0f, 0f), Forward = new Vector3(1f, 0f, 0f),
                             Target = new Vector3(6f, 0f, 0f) },
                new Walker { Pos = new Vector3(6f, 0f, 0f), Forward = new Vector3(-1f, 0f, 0f),
                             Target = new Vector3(-6f, 0f, 0f) },
                new Walker { Pos = new Vector3(0f, 0f, -6f), Forward = new Vector3(0f, 0f, 1f),
                             Target = new Vector3(0f, 0f, 6f) },
                new Walker { Pos = new Vector3(0f, 0f, 6f), Forward = new Vector3(0f, 0f, -1f),
                             Target = new Vector3(0f, 0f, -6f) },
            };

            var minDistance = float.PositiveInfinity;
            for (var frame = 0; frame < 60 * 30; frame++)
            {
                for (var i = 0; i < walkers.Length; i++)
                    Step(walkers, i);
                minDistance = Mathf.Min(minDistance, MinPairDistance(walkers));
            }

            if (minDistance < Radius * 2f - 1e-3f)
                failures.Add($"Crossing crowd: bodies overlapped (closest {minDistance:F3}m).");

            for (var i = 0; i < walkers.Length; i++)
                if (!walkers[i].Arrived)
                    failures.Add($"Crossing crowd: walker {i} never arrived - the crossing " +
                                 $"deadlocked (stalled {walkers[i].StalledFor:F1}s at " +
                                 $"{walkers[i].Pos}).");
        }

        // ------------------------------------------------------------------ pairing

        static void PairingBooksNobodyTwice(List<string> failures)
        {
            var positions = new List<Vector3>();
            var rng = new System.Random(7);
            for (var i = 0; i < 40; i++)
                positions.Add(new Vector3((float)rng.NextDouble() * 30f, 0f,
                                          (float)rng.NextDouble() * 30f));

            var pairs = new List<(int a, int b)>();
            InteractionPairing.Pairs(positions, 3f, pairs);

            var seen = new HashSet<int>();
            foreach (var (a, b) in pairs)
            {
                if (a == b)
                    failures.Add("Pairing: paired somebody with themselves.");
                if (!seen.Add(a) || !seen.Add(b))
                    failures.Add("Pairing: booked one walker into two conversations.");

                var distance = PedestrianSteering.Flat(positions[b] - positions[a]).magnitude;
                if (distance > 3f + 1e-4f)
                    failures.Add($"Pairing: paired across {distance:F2}m with range 3.");
            }
        }

        static void PairingIsDeterministic(List<string> failures)
        {
            var positions = new List<Vector3>();
            var rng = new System.Random(11);
            for (var i = 0; i < 25; i++)
                positions.Add(new Vector3((float)rng.NextDouble() * 20f, 0f,
                                          (float)rng.NextDouble() * 20f));

            var first = new List<(int a, int b)>();
            var second = new List<(int a, int b)>();
            InteractionPairing.Pairs(positions, 3f, first);
            InteractionPairing.Pairs(positions, 3f, second);

            if (first.Count != second.Count)
            {
                failures.Add("Pairing: two runs over the same input disagree on pair count.");
                return;
            }

            for (var i = 0; i < first.Count; i++)
                if (first[i] != second[i])
                    failures.Add("Pairing: two runs over the same input disagree.");
        }

        static void PairingPrefersTheNearest(List<string> failures)
        {
            var positions = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(2.5f, 0f, 0f),
            };

            var pairs = new List<(int a, int b)>();
            InteractionPairing.Pairs(positions, 3f, pairs);

            if (pairs.Count != 1 || pairs[0] != (0, 1))
                failures.Add("Pairing: did not pair the nearest available neighbour.");

            // Two floors of the same street corner must not chat through the bridge deck.
            positions[1] = new Vector3(1f, 12f, 0f);
            InteractionPairing.Pairs(positions, 3f, pairs);
            foreach (var (a, b) in pairs)
                if (Mathf.Abs(positions[a].y - positions[b].y) > 2f)
                    failures.Add("Pairing: paired across a large height difference.");
        }

        /// <summary>
        /// The exact algorithm InteractionPairing used before it was bucketed: a triangular
        /// scan, nearest forward neighbour, replacement on less-OR-EQUAL so the highest index
        /// wins ties. The bucketed version exists purely for cost; its OUTPUT must be this,
        /// pair for pair, in order.
        /// </summary>
        static void ReferencePairs(IReadOnlyList<Vector3> candidates, float maxDistance,
                                   List<(int a, int b)> result)
        {
            result.Clear();
            var used = new bool[candidates.Count];
            var maxSq = maxDistance * maxDistance;

            for (var i = 0; i < candidates.Count; i++)
            {
                if (used[i])
                    continue;

                var best = -1;
                var bestSq = maxSq;

                for (var j = i + 1; j < candidates.Count; j++)
                {
                    if (used[j])
                        continue;

                    var delta = candidates[j] - candidates[i];
                    if (Mathf.Abs(delta.y) > 2f)
                        continue;

                    delta.y = 0f;
                    var distSq = delta.sqrMagnitude;
                    if (distSq > bestSq)
                        continue;

                    best = j;
                    bestSq = distSq;
                }

                if (best < 0)
                    continue;

                used[i] = true;
                used[best] = true;
                result.Add((i, best));
            }
        }

        static void PairingMatchesTheFullScan(List<string> failures)
        {
            var rng = new System.Random(23);
            var fast = new List<(int a, int b)>();
            var reference = new List<(int a, int b)>();

            for (var trial = 0; trial < 25; trial++)
            {
                var count = 2 + rng.Next(150);
                var positions = new List<Vector3>();

                // Negative coordinates on purpose - cell flooring is where a hash slips -
                // and every fifth walker up on the bridge deck to exercise the height gate.
                for (var i = 0; i < count; i++)
                    positions.Add(new Vector3(
                        ((float)rng.NextDouble() - 0.5f) * 60f,
                        rng.Next(5) == 0 ? 12f : 0f,
                        ((float)rng.NextDouble() - 0.5f) * 60f));

                // A dense cluster straddling a cell corner, where neighbours span four cells.
                for (var i = 0; i < 12; i++)
                    positions.Add(new Vector3(
                        (float)rng.NextDouble() * 4f - 2f, 0f,
                        (float)rng.NextDouble() * 4f - 2f));

                InteractionPairing.Pairs(positions, 3f, fast);
                ReferencePairs(positions, 3f, reference);

                if (fast.Count != reference.Count)
                {
                    failures.Add($"Pairing/full-scan: trial {trial} pair counts differ " +
                                 $"({fast.Count} vs {reference.Count}).");
                    continue;
                }

                for (var i = 0; i < fast.Count; i++)
                    if (fast[i] != reference[i])
                    {
                        failures.Add($"Pairing/full-scan: trial {trial} pair {i} differs " +
                                     $"({fast[i]} vs {reference[i]}).");
                        break;
                    }
            }
        }

        // ------------------------------------------------------------------ registry hash

        /// <summary>
        /// The probe visits one ring of cells around its own, so any body inside the longest
        /// query reach (OncomingBias at PersonalSpace * 3) must never be more than one cell
        /// index away on either axis. This is the invariant the whole bucketing rests on.
        /// </summary>
        static void RegistryCellsCoverTheProbeReach(List<string> failures)
        {
            var reach = PersonalSpace * 3f;
            if (reach > PedestrianRegistry.HashCellSize + 1e-3f)
            {
                failures.Add($"Registry hash: cell size {PedestrianRegistry.HashCellSize} is " +
                             $"smaller than the probe reach {reach} - a 3x3 scan misses bodies.");
                return;
            }

            var rng = new System.Random(31);
            for (var trial = 0; trial < 500; trial++)
            {
                var a = new Vector3(((float)rng.NextDouble() - 0.5f) * 400f, 0f,
                                    ((float)rng.NextDouble() - 0.5f) * 400f);
                var angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                var dist = (float)rng.NextDouble() * reach;
                var b = a + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;

                var dax = Mathf.Abs(Mathf.FloorToInt(a.x / PedestrianRegistry.HashCellSize)
                                  - Mathf.FloorToInt(b.x / PedestrianRegistry.HashCellSize));
                var daz = Mathf.Abs(Mathf.FloorToInt(a.z / PedestrianRegistry.HashCellSize)
                                  - Mathf.FloorToInt(b.z / PedestrianRegistry.HashCellSize));

                if (dax > 1 || daz > 1)
                    failures.Add($"Registry hash: bodies {dist:F2}m apart landed {dax}x{daz} " +
                                 $"cells apart at {a} - the 3x3 scan would miss one.");
            }
        }

        // ------------------------------------------------------------------ street doors

        /// <summary>
        /// The whole geometric claim BuildingDoorRule rests on, checked against real generated
        /// cities: a facade that fronts a street stands INSIDE the road cell (facades are 7m
        /// from the centreline, 10m on the avenue, and the cell boundary is at 15), while a
        /// facade turned toward the interior of its block stands in a block cell. If that ever
        /// stops holding, doors appear on alley walls and pedestrians walk into brickwork.
        ///
        /// The facades here are synthesised rather than placed, because placing them means
        /// BlockBuilder and prefab instantiation, which no headless host can do. What is being
        /// tested is the rule, and the rule only ever sees a point and a normal.
        /// </summary>
        static void DoorsFrontTheStreetAndOnlyTheStreet(List<string> failures, CityConfig config, int seeds)
        {
            // `is null`, not `== null`: CityConfig is a UnityEngine.Object, and the headless
            // host constructs one through GetUninitializedObject - which has no native peer,
            // so Unity's overloaded == reports the live object we were just handed as null.
            if (config is null)
            {
                failures.Add("Door rule: no CityConfig supplied.");
                return;
            }

            var streetFacades = 0;
            var interiorFacades = 0;
            var backlessFacades = 0;
            var maps = 0;

            for (var seed = 0; seed < seeds; seed++)
            {
                config.seed = seed;

                var grid = CityGenerator.Generate(config);
                if (grid == null || grid.BlockCount <= 1)
                    continue;

                maps++;

                for (var x = 0; x < grid.Width; x++)
                for (var z = 0; z < grid.Height; z++)
                {
                    if (!grid.IsRoad(x, z))
                        continue;

                    foreach (var step in Steps)
                    {
                        var bx = x + step.x;
                        var bz = z + step.y;

                        if (!grid.InBounds(bx, bz) || grid.IsRoad(bx, bz))
                            continue;
                        if (grid.BlockIdAt(bx, bz) < 0)
                            continue;

                        // Toward the block from the road centreline; the facade looks back.
                        var toBlock = new Vector3(step.x, 0f, step.y);
                        var centre = grid.CellToWorld(x, z);
                        var facing = -toBlock;

                        // Both street cross-sections: ordinary street and the avenue.
                        foreach (var setback in FacadeSetbacks)
                        {
                            streetFacades++;
                            if (!BuildingDoorRule.Eligible(grid, "building-block-4floor",
                                                           centre + toBlock * setback, facing))
                                failures.Add($"Door rule: seed {seed}, a facade {setback}m off the " +
                                             $"centreline of road cell ({x},{z}) facing block " +
                                             $"({bx},{bz}) was rejected - street facades must qualify.");
                        }
                    }
                }

                // A facade standing in a road cell is necessary but not sufficient: it also
                // needs a block BEHIND it. Two road cells side by side give a wall facing
                // across the carriageway at nothing, and a road cell on the map edge gives one
                // backing onto the void. Both stand exactly where a real facade stands, so
                // only the behind-test can tell them apart.
                for (var x = 0; x < grid.Width; x++)
                for (var z = 0; z < grid.Height; z++)
                {
                    if (!grid.IsRoad(x, z))
                        continue;

                    foreach (var step in Steps)
                    {
                        var nx = x + step.x;
                        var nz = z + step.y;

                        var offMap = !grid.InBounds(nx, nz);
                        if (!offMap && !grid.IsRoad(nx, nz))
                            continue;

                        var toward = new Vector3(step.x, 0f, step.y);
                        var door = grid.CellToWorld(x, z) + toward * 7f;

                        backlessFacades++;
                        if (BuildingDoorRule.Eligible(grid, "building-block-4floor", door, -toward))
                            failures.Add($"Door rule: seed {seed}, a facade in road cell ({x},{z}) " +
                                         $"backing onto {(offMap ? "the map edge" : $"road cell ({nx},{nz})")} " +
                                         "qualified - there is no building behind that door.");
                    }
                }

                // The other side of the claim: a cell buried inside a block. Its own centre is
                // 15m from every boundary, so nothing there can be mistaken for a street.
                for (var x = 1; x < grid.Width - 1; x++)
                for (var z = 1; z < grid.Height - 1; z++)
                {
                    if (grid.IsRoad(x, z) || grid.BlockIdAt(x, z) < 0)
                        continue;

                    var buried = true;
                    foreach (var step in Steps)
                        if (grid.IsRoad(x + step.x, z + step.y))
                            buried = false;
                    if (!buried)
                        continue;

                    interiorFacades++;
                    if (BuildingDoorRule.Eligible(grid, "building-block-4floor",
                                                  grid.CellToWorld(x, z), Vector3.forward))
                        failures.Add($"Door rule: seed {seed}, a facade in the interior of block " +
                                     $"cell ({x},{z}) qualified - that door opens onto nothing.");
                }
            }

            // A rule that rejects everything passes every assertion above. Say so out loud.
            if (maps == 0)
                failures.Add("Door rule: no city generated across the whole sweep.");
            else if (streetFacades == 0)
                failures.Add("Door rule: the sweep found no street facades at all to test.");
            else if (interiorFacades == 0)
                failures.Add("Door rule: the sweep found no block interiors at all to test.");
            else if (backlessFacades == 0)
                failures.Add("Door rule: the sweep found no backless facades at all to test.");
        }

        /// <summary>
        /// Nobody strolls home into a factory yard or a car park. Same facade, same geometry -
        /// only the zone changes - so this isolates the zone gate from everything else.
        /// </summary>
        static void IndustrialAndParkingAdmitNobody(List<string> failures, CityConfig config)
        {
            config.seed = 7;

            var grid = CityGenerator.Generate(config);
            if (grid == null || grid.BlockCount <= 1)
            {
                failures.Add("Door rule: seed 7 produced no city to zone-test.");
                return;
            }

            for (var x = 0; x < grid.Width; x++)
            for (var z = 0; z < grid.Height; z++)
            {
                if (!grid.IsRoad(x, z))
                    continue;

                foreach (var step in Steps)
                {
                    var bx = x + step.x;
                    var bz = z + step.y;
                    var blockId = grid.BlockIdAt(bx, bz);

                    if (!grid.InBounds(bx, bz) || grid.IsRoad(bx, bz) || blockId < 0)
                        continue;

                    var toBlock = new Vector3(step.x, 0f, step.y);
                    var door = grid.CellToWorld(x, z) + toBlock * 7f;
                    var facing = -toBlock;

                    grid.SetZone(blockId, BlockZone.ResidentialHigh);
                    if (!BuildingDoorRule.Eligible(grid, "building-block-4floor", door, facing))
                    {
                        failures.Add($"Door rule: a residential facade at road cell ({x},{z}) was rejected.");
                        return;
                    }

                    grid.SetZone(blockId, BlockZone.Industrial);
                    if (BuildingDoorRule.Eligible(grid, "building-block-4floor", door, facing))
                    {
                        failures.Add($"Door rule: an INDUSTRIAL facade at road cell ({x},{z}) qualified.");
                        return;
                    }

                    grid.SetZone(blockId, BlockZone.Parking);
                    if (BuildingDoorRule.Eligible(grid, "building-block-4floor", door, facing))
                    {
                        failures.Add($"Door rule: a car-park facade at road cell ({x},{z}) qualified.");
                        return;
                    }

                    return;
                }
            }

            failures.Add("Door rule: seed 7 offered no road-and-block pair to zone-test.");
        }

        static void DoorNameFilterRejectsWhatItMust(List<string> failures)
        {
            foreach (var name in new[]
                     {
                         "building-block-4floor", "building-block-5floor-front(Clone)",
                         "building-house-block-big", "building-cafe", "building-school",
                     })
                if (!BuildingDoorRule.NameQualifies(name))
                    failures.Add($"Door rule: '{name}' should carry a street door.");

            foreach (var name in new[]
                     {
                         // A corner's forward aligns the quadrant bisector, so its "facade
                         // centre" is inside a wall.
                         "building-block-4floor-corner", "building-block-5floor-corner(Clone)",
                         // The whole industrial catalogue, and the one vehicle door named
                         // building-*.
                         "industry-factory", "industry-warehouse", "building-policestation-garage",
                         // Not buildings at all.
                         "bench-old", "tree-conifer", "tile-park", "", null,
                     })
                if (BuildingDoorRule.NameQualifies(name))
                    failures.Add($"Door rule: '{name}' must not carry a street door.");
        }

        static readonly Vector2Int[] Steps =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        /// <summary>Facade distance from the road centreline: ordinary street, then avenue.</summary>
        static readonly float[] FacadeSetbacks = { 7f, 10f };

        static void RegistryCellKeysDoNotCollide(List<string> failures)
        {
            // Distinct cells must give distinct keys across the whole plausible city extent
            // (plus far outside it - walkers can be flung anywhere by a bad path).
            var seen = new HashSet<long>();
            for (var cx = -100; cx <= 100; cx++)
                for (var cz = -100; cz <= 100; cz++)
                    if (!seen.Add(PedestrianRegistry.KeyFor(
                            new Vector3(cx * PedestrianRegistry.HashCellSize + 0.5f, 0f,
                                        cz * PedestrianRegistry.HashCellSize + 0.5f))))
                    {
                        failures.Add($"Registry hash: key collision at cell ({cx}, {cz}).");
                        return;
                    }
        }

        static void RoadCheck(List<string> failures, string what, RoadTileKind kind,
                              float x, float z, bool expectRoad)
        {
            var shape = RoadSurface.Classify(kind);
            if (RoadSurface.OnAsphalt(shape, x, z) != expectRoad)
                failures.Add($"Road surface: {what} at ({x}, {z}) on {kind} should be " +
                             $"{(expectRoad ? "road" : "pavement")} and is not.");
        }

        /// <summary>
        /// The tile cross-sections, restated independently of RoadSurface's constants (the
        /// bench-table rule: a table checking itself proves nothing). Street: lanes at 1.5,
        /// asphalt to the kerb at 3, pavement 3..5, walking line at 4. Avenue: lanes at 1.75
        /// and 4.75, kerb at 6.25, pavement to 8.5, walking line at 7.25. Crosswalk straights
        /// keep the Straight kind, so their stripe centres at 1.55 ride the same test.
        /// </summary>
        static void RoadCrossSectionsClassifyAsMeasured(List<string> failures)
        {
            foreach (var lane in new[] { -1.5f, 1.5f, -1.55f, 1.55f, 0f })
                RoadCheck(failures, "lane", RoadTileKind.Straight, lane, 9f, true);

            foreach (var pavement in new[] { -4.75f, -4f, -3.5f, 3.5f, 4f, 4.75f })
                RoadCheck(failures, "pavement", RoadTileKind.Straight, pavement, 9f, false);

            foreach (var lane in new[] { -4.75f, -1.75f, 1.75f, 4.75f })
                RoadCheck(failures, "avenue lane", RoadTileKind.MainStraight, lane, 9f, true);

            foreach (var pavement in new[] { -8.4f, -7.25f, -6.6f, 6.6f, 7.25f, 8.4f })
                RoadCheck(failures, "avenue pavement", RoadTileKind.MainStraight, pavement, 9f, false);

            // Junction arms are road along both axes; the corner quadrants stay sociable -
            // that is where pavement pairs meet, and over-blocking them would thin out
            // exactly the street-corner conversations that look most natural.
            RoadCheck(failures, "junction centre", RoadTileKind.Cross, 0f, 0f, true);
            RoadCheck(failures, "junction arm", RoadTileKind.Cross, 12f, 1.5f, true);
            RoadCheck(failures, "junction arm", RoadTileKind.Cross, 1.5f, 12f, true);
            RoadCheck(failures, "junction corner", RoadTileKind.Cross, 4.5f, 4.5f, false);
            RoadCheck(failures, "junction corner", RoadTileKind.Cross, 12f, 4.5f, false);

            // T at 0 degrees: through road left-right (the |z| band), branch north.
            RoadCheck(failures, "T through", RoadTileKind.TJunction, 12f, -1.5f, true);
            RoadCheck(failures, "T branch", RoadTileKind.TJunction, 1.5f, 12f, true);
            RoadCheck(failures, "T corner", RoadTileKind.TJunction, 8f, 8f, false);

            // MainTJunction: boulevard east-west, minor street teeing north.
            RoadCheck(failures, "boulevard", RoadTileKind.MainTJunction, 12f, 5.5f, true);
            RoadCheck(failures, "minor branch", RoadTileKind.MainTJunction, 1.5f, 12f, true);
            RoadCheck(failures, "corner", RoadTileKind.MainTJunction, 5f, 8f, false);

            // MainCross: boulevard north-south crossing a minor street.
            RoadCheck(failures, "boulevard", RoadTileKind.MainCross, 5.5f, 12f, true);
            RoadCheck(failures, "minor street", RoadTileKind.MainCross, 12f, 1.5f, true);
            RoadCheck(failures, "corner", RoadTileKind.MainCross, 8f, 5f, false);

            // Dead end: the turning circle is road, the sidewalk ring behind it is not.
            RoadCheck(failures, "turning circle", RoadTileKind.End, 0f, 0f, true);
            RoadCheck(failures, "arm", RoadTileKind.End, 1.5f, 9f, true);
            RoadCheck(failures, "ring behind circle", RoadTileKind.End, 0f, -4.2f, false);
        }

        /// <summary>
        /// Every generated road tile is named tile_{x}_{y}_{Kind}; the parse must recover
        /// each kind, refuse pack demo names, and refuse a bare coordinate suffix (which
        /// Enum.TryParse would otherwise happily read as a numeric enum value).
        /// </summary>
        static void RoadTileNamesRoundTrip(List<string> failures)
        {
            foreach (RoadTileKind kind in System.Enum.GetValues(typeof(RoadTileKind)))
            {
                var parsed = RoadSurface.TryParseKind($"tile_4_-7_{kind}", out var got);

                if (kind == RoadTileKind.None)
                {
                    if (parsed)
                        failures.Add("Road names: tile_4_-7_None parsed as a road.");
                    continue;
                }

                if (!parsed || got != kind)
                    failures.Add($"Road names: tile_4_-7_{kind} did not round-trip (got {got}).");

                // Every Main* kind must carry the avenue half-width somewhere; the rest only
                // street widths - a swapped table would put the no-stopping zone 3m wide on
                // the boulevard and 6m wide on a side street.
                var shape = RoadSurface.Classify(kind);
                var widest = Mathf.Max(shape.BandX, shape.BandZ);
                var isMain = kind.ToString().StartsWith("Main");
                var mainEdge = 6.25f + RoadSurface.KerbMargin;
                if (isMain != Mathf.Approximately(widest, mainEdge))
                    failures.Add($"Road names: {kind} widest band {widest} does not match " +
                                 $"its {(isMain ? "avenue" : "street")} family.");
            }

            foreach (var name in new[] { "tile_3_4", "tile-road-straight", "lights_2_3", "tile_", "" })
                if (RoadSurface.TryParseKind(name, out _))
                    failures.Add($"Road names: '{name}' parsed as a road tile.");
        }

        /// <summary>
        /// The curve's asphalt, measured off the mesh: an annulus of radius 15 +/- 3 around
        /// the corner shared by its two connected edges (local (-15, +15) unrotated), with
        /// sidewalk arcs at radii 11 and 19. The car nav polylines additionally take the
        /// corner square - through (-1.5, 1.5) - so those bands must classify as road too:
        /// pedestrians must not stand where the wheels actually go, whatever the paint says.
        /// </summary>
        static void CurveBandCoversAsphaltAndSparesPavement(List<string> failures)
        {
            // Points on the drawn asphalt: entry lanes at the north edge, the annulus
            // centreline at the 45-degree diagonal, mesh face centroids.
            RoadCheck(failures, "curve entry", RoadTileKind.Curve, -1.5f, 15f, true);
            RoadCheck(failures, "curve exit", RoadTileKind.Curve, -15f, 1.5f, true);
            RoadCheck(failures, "curve diagonal", RoadTileKind.Curve, -4.39f, 4.39f, true);
            RoadCheck(failures, "curve mesh face", RoadTileKind.Curve, -5.8f, 7.3f, true);

            // The square nav corner the cars actually drive.
            RoadCheck(failures, "nav apex inner", RoadTileKind.Curve, -1.5f, 1.5f, true);
            RoadCheck(failures, "nav apex outer", RoadTileKind.Curve, 1.5f, -1.5f, true);

            // Sidewalk arc points read off the prefab's walking lines (radii 11 and 19).
            RoadCheck(failures, "inner walk line", RoadTileKind.Curve, -4f, 15f, false);
            RoadCheck(failures, "inner walk line", RoadTileKind.Curve, -6.36f, 8.06f, false);
            RoadCheck(failures, "inner walk line", RoadTileKind.Curve, -15f, 4f, false);
            RoadCheck(failures, "outer walk line", RoadTileKind.Curve, 4f, 15f, false);
            RoadCheck(failures, "outer walk line", RoadTileKind.Curve, -15f, -4f, false);
        }

        /// <summary>
        /// The exact regression that put chat pairs on the asphalt: two walkers on opposite
        /// pavements pair up across the street, and the midpoint they both walk to is the
        /// middle of the carriageway. The predicate has to refuse it - and has to allow the
        /// midpoint of a same-side pair, or corner conversations die out entirely.
        /// </summary>
        static void CrossingPairMidpointIsRefused(List<string> failures)
        {
            var shape = RoadSurface.Classify(RoadTileKind.Straight);

            var oppositeMidX = (-4f + 4f) * 0.5f;
            if (!RoadSurface.OnAsphalt(shape, oppositeMidX, 6f))
                failures.Add("Midpoint: opposite-pavement pair's midpoint passed as pavement.");

            var sameSideMidX = (-4f + -4.5f) * 0.5f;
            if (RoadSurface.OnAsphalt(shape, sameSideMidX, 6f))
                failures.Add("Midpoint: same-pavement pair's midpoint classified as road.");
        }
    }
}
