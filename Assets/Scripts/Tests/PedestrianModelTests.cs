using System.Collections.Generic;
using UnityEngine;
using LivingCity.Entities;

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
    }
}
