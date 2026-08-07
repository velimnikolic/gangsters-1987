using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>One live pedestrian's entry in <see cref="PedestrianRegistry"/>.</summary>
    public sealed class PedestrianBody
    {
        public readonly Transform Tf;

        /// <summary>Measured once at registration from the prefab's capsule.</summary>
        public readonly float Radius;

        /// <summary>
        /// Deterministic per-body fraction in [0,1), used to break exact ties - two bodies
        /// spawned onto the same point have no direction to push each other in, and both
        /// picking the same arbitrary one would keep them coincident.
        /// </summary>
        public readonly float Jitter;

        /// <summary>Written by whoever is moving the transform this frame. m/s, never negative.</summary>
        public float SpeedMs;

        /// <summary>
        /// Standing still on purpose - talking, sitting, idling. Still very much an obstacle
        /// (others flow around a chatting pair), but exempt from the stall valve: a person
        /// mid-conversation is not jammed, however long they stand.
        /// </summary>
        public bool Stationary;

        /// <summary>Inside a shop. Invisible to every probe until they come back out.</summary>
        public bool Hidden;

        /// <summary>Seconds this walker has been held still by bodies in the way.</summary>
        public float StalledFor;

        public PedestrianBody(Transform tf, float radius, float jitter)
        {
            Tf = tf;
            Radius = radius;
            Jitter = jitter;
        }
    }

    /// <summary>What the pavement ahead looks like to one walker this frame.</summary>
    public readonly struct PedestrianObstacle
    {
        /// <summary>Un-normalised steering push - separation plus oncoming bias, pre-weight.</summary>
        public readonly Vector3 Push;

        /// <summary>
        /// How far the frame may move this frame. An allowance, not a gap - the clearance each
        /// blocker is owed has already been subtracted, and bodies already overlapped with are
        /// left out (see PedestrianSteering's class doc).
        /// </summary>
        public readonly float AllowedAdvance;

        public PedestrianObstacle(Vector3 push, float allowedAdvance)
        {
            Push = push;
            AllowedAdvance = allowedAdvance;
        }

        public static PedestrianObstacle Clear =>
            new PedestrianObstacle(Vector3.zero, float.PositiveInfinity);
    }

    /// <summary>
    /// Every pedestrian on the streets, and the one question worth asking about them: who is
    /// in the way?
    ///
    /// This exists because the pack has no answer for people, exactly as it had none for cars:
    /// HumanBehavior writes straight to transform.position with a kinematic Rigidbody, so PhysX
    /// never generates a contact between two pedestrians and they walk through each other.
    /// Mirrors <see cref="TrafficRegistry"/> - a flat list, scanned in full. pedestrianCount
    /// is 50, so a probe is 50 distance checks and a physics step is 2500, which is nothing.
    /// Bucket on CityGrid.CellSize if the population ever reaches the high hundreds.
    /// </summary>
    public static class PedestrianRegistry
    {
        /// <summary>
        /// Tuning handed down from CityConfig by the spawner before the first probe. Statics
        /// rather than per-call parameters because the probes run inside a patched pack script
        /// that has no config reference and should gain as little surface as possible.
        /// </summary>
        public static float PersonalSpace = 1.2f;
        public static float MinSeparation = 0.6f;

        /// <summary>Advance below which a walker counts as unable to move at all.</summary>
        const float BlockedAdvance = 0.02f;

        static readonly List<PedestrianBody> Bodies = new List<PedestrianBody>();

        public static int Count => Bodies.Count;

        public static PedestrianBody Register(Transform tf)
        {
            if (!tf)
                return null;

            var body = new PedestrianBody(tf, MeasureRadius(tf),
                Mathf.Abs(tf.GetHashCode() % 1000) / 1000f);
            Bodies.Add(body);
            return body;
        }

        public static void Unregister(PedestrianBody body)
        {
            if (body != null)
                Bodies.Remove(body);
        }

        /// <summary>
        /// The steering push and movement allowance for one walker heading along
        /// <paramref name="desiredDir"/>. Also runs the stall clock - this is called exactly
        /// once per body per FixedUpdate by whoever is doing the moving.
        /// </summary>
        public static PedestrianObstacle Probe(PedestrianBody self, Vector3 desiredDir)
        {
            if (self == null || !self.Tf)
                return PedestrianObstacle.Clear;

            var selfPos = self.Tf.position;
            var push = Vector3.zero;
            var allowedAdvance = float.PositiveInfinity;
            var clearance = PedestrianSteering.EffectiveClearance(MinSeparation, self.StalledFor);

            for (var i = 0; i < Bodies.Count; i++)
            {
                var other = Bodies[i];
                if (other == self || other == null || !other.Tf || other.Hidden)
                    continue;

                var otherPos = other.Tf.position;

                var separation = PedestrianSteering.SeparationPush(selfPos, otherPos, PersonalSpace);
                if (separation == Vector3.zero
                    && PedestrianSteering.Flat(otherPos - selfPos).sqrMagnitude < 1e-8f)
                {
                    // Coincident bodies have no line between them to push along. Derive one
                    // from the pair's jitters so both members pick OPPOSITE directions and the
                    // pair actually comes apart.
                    var angle = (self.Jitter - other.Jitter) * Mathf.PI * 2f;
                    separation = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                }

                push += separation;
                push += PedestrianSteering.OncomingBias(desiredDir, otherPos - selfPos,
                    other.Tf.forward, PersonalSpace * 3f);

                var cap = PedestrianSteering.AdvanceCap(selfPos, desiredDir, otherPos,
                    self.Radius + other.Radius, clearance);
                if (cap < allowedAdvance)
                    allowedAdvance = cap;
            }

            UpdateStall(self, allowedAdvance);

            return new PedestrianObstacle(push, allowedAdvance);
        }

        /// <summary>
        /// Is this spot free of other bodies? Asked before placing someone somewhere they did
        /// not walk to - reappearing at a shop door, standing up off a bench.
        /// </summary>
        public static bool IsClear(PedestrianBody self, Vector3 position, float margin = 0.1f)
        {
            for (var i = 0; i < Bodies.Count; i++)
            {
                var other = Bodies[i];
                if (other == self || other == null || !other.Tf || other.Hidden)
                    continue;

                var radiusSum = (self?.Radius ?? PedestrianSteering.DefaultRadius) + other.Radius;
                if (PedestrianSteering.Flat(other.Tf.position - position).magnitude < radiusSum + margin)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The stall clock: armed while the walker WANTS to move and the clamp forbids it,
        /// reset the moment it moves or stops wanting to. Stationary bodies never accrue -
        /// standing in a conversation is not a jam, and letting it count would have every
        /// long chat end with the talkers' clearance decayed to nothing.
        /// </summary>
        static void UpdateStall(PedestrianBody self, float allowedAdvance)
        {
            var moving = self.SpeedMs >= PedestrianSteering.StalledSpeed;

            if (self.Stationary || moving || allowedAdvance > BlockedAdvance)
                self.StalledFor = 0f;
            else
                self.StalledFor += Time.deltaTime;
        }

        /// <summary>
        /// The body radius, read off the prefab's own capsule (the pack puts it on the child
        /// rig object). Scaled by the largest horizontal axis for safety; falls back to
        /// <see cref="PedestrianSteering.DefaultRadius"/> when there is none.
        /// </summary>
        static float MeasureRadius(Transform tf)
        {
            var capsule = tf.GetComponentInChildren<CapsuleCollider>(true);
            if (!capsule)
                return PedestrianSteering.DefaultRadius;

            var scale = capsule.transform.lossyScale;
            return capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }
    }
}
