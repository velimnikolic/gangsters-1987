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

        /// <summary>Which spatial-hash cell this body currently sits in. Registry-internal.</summary>
        internal long CellKey;

        /// <summary>Index in the registry's flat list, so removal is swap-pop. Registry-internal.</summary>
        internal int ListIndex;

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
    ///
    /// Bodies live in a spatial hash: a flat XZ grid of <see cref="HashCellSize"/>-metre cells,
    /// so a probe visits the 3x3 neighbourhood instead of the whole city. The cell size covers
    /// the longest query reach (OncomingBias at PersonalSpace * 3 = 3.6m); everything farther
    /// contributes zero push, and an AdvanceCap from a body outside the neighbourhood is at
    /// least CellSize - radii - clearance, about 2.7 metres, which can never bind a step of
    /// at most maxspeed * fixed dt = 0.075m. So the bucketed probe is behaviourally identical
    /// to the old full scan, at O(local density) per body instead of O(population).
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

        /// <summary>
        /// Fixed steps between two avoidance probes per walker, also handed down by the
        /// spawner. 1 probes every step, the original cadence; at N each walker probes every
        /// Nth step (staggered by its jitter) and spends the cached allowance in between -
        /// see HumanBehavior's patch for why that stays overlap-free.
        /// </summary>
        public static int ProbeInterval = 1;

        /// <summary>Advance below which a walker counts as unable to move at all.</summary>
        const float BlockedAdvance = 0.02f;

        /// <summary>
        /// Spatial hash cell side, metres. Must stay >= the longest probe reach for the 3x3
        /// scan to be exhaustive; when PersonalSpace is raised past CellSize / 3 the ring
        /// count adapts (see <see cref="QueryRings"/>) rather than the cells resizing.
        /// </summary>
        public const float HashCellSize = 4f;

        static readonly List<PedestrianBody> Bodies = new List<PedestrianBody>();
        static readonly Dictionary<long, List<PedestrianBody>> Cells =
            new Dictionary<long, List<PedestrianBody>>();

        public static int Count => Bodies.Count;

        /// <summary>One hash key per cell: packed (floor(x / cell), floor(z / cell)).</summary>
        public static long KeyFor(Vector3 position) =>
            Key(Mathf.FloorToInt(position.x / HashCellSize),
                Mathf.FloorToInt(position.z / HashCellSize));

        static long Key(int cx, int cz) => ((long)cx << 32) | (uint)cz;

        /// <summary>
        /// How many cell rings a probe must visit around its own: 1 (a 3x3 block) until
        /// PersonalSpace is tuned past CellSize / 3, then just enough to keep covering the
        /// oncoming-bias reach.
        /// </summary>
        static int QueryRings =>
            Mathf.Max(1, Mathf.CeilToInt(PersonalSpace * 3f / HashCellSize));

        public static PedestrianBody Register(Transform tf)
        {
            if (!tf)
                return null;

            var body = new PedestrianBody(tf, MeasureRadius(tf),
                Mathf.Abs(tf.GetHashCode() % 1000) / 1000f);

            body.ListIndex = Bodies.Count;
            Bodies.Add(body);

            body.CellKey = KeyFor(tf.position);
            CellFor(body.CellKey).Add(body);
            return body;
        }

        public static void Unregister(PedestrianBody body)
        {
            if (body == null)
                return;

            // Swap-pop from the flat list: 10k pedestrians tearing a scene down must not be
            // 10k linear removes.
            var last = Bodies[Bodies.Count - 1];
            Bodies[body.ListIndex] = last;
            last.ListIndex = body.ListIndex;
            Bodies.RemoveAt(Bodies.Count - 1);

            if (Cells.TryGetValue(body.CellKey, out var cell))
                cell.Remove(body);
        }

        /// <summary>
        /// Moves a body to the cell its position now falls in. Called by Probe for the body
        /// being probed, and by anyone who teleports a body without probing (the bench glide).
        /// </summary>
        public static void Rebucket(PedestrianBody body, Vector3 position)
        {
            if (body == null)
                return;

            var key = KeyFor(position);
            if (key == body.CellKey)
                return;

            if (Cells.TryGetValue(body.CellKey, out var oldCell))
                oldCell.Remove(body);
            body.CellKey = key;
            CellFor(key).Add(body);
        }

        static List<PedestrianBody> CellFor(long key)
        {
            if (!Cells.TryGetValue(key, out var cell))
                Cells[key] = cell = new List<PedestrianBody>();
            return cell;
        }

        /// <summary>
        /// The steering push and movement allowance for one walker heading along
        /// <paramref name="desiredDir"/>. Also runs the stall clock - this is called once per
        /// body per probe cadence by whoever is doing the moving; <paramref name="stallDt"/>
        /// is the wall time the call stands for, so a walker probing every Nth step passes
        /// N * fixed dt and the stall clock keeps real time.
        /// </summary>
        public static PedestrianObstacle Probe(PedestrianBody self, Vector3 desiredDir) =>
            Probe(self, desiredDir, Time.deltaTime);

        public static PedestrianObstacle Probe(PedestrianBody self, Vector3 desiredDir, float stallDt)
        {
            if (self == null || !self.Tf)
                return PedestrianObstacle.Clear;

            var selfPos = self.Tf.position;
            Rebucket(self, selfPos);

            var push = Vector3.zero;
            var allowedAdvance = float.PositiveInfinity;
            var clearance = PedestrianSteering.EffectiveClearance(MinSeparation, self.StalledFor);

            var rings = QueryRings;
            var cx = Mathf.FloorToInt(selfPos.x / HashCellSize);
            var cz = Mathf.FloorToInt(selfPos.z / HashCellSize);

            for (var dx = -rings; dx <= rings; dx++)
            for (var dz = -rings; dz <= rings; dz++)
            {
                if (!Cells.TryGetValue(Key(cx + dx, cz + dz), out var cell))
                    continue;

                for (var i = 0; i < cell.Count; i++)
                {
                    var other = cell[i];
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
            }

            UpdateStall(self, allowedAdvance, stallDt);

            return new PedestrianObstacle(push, allowedAdvance);
        }

        /// <summary>
        /// Is this spot free of other bodies? Asked before placing someone somewhere they did
        /// not walk to - reappearing at a shop door, standing up off a bench. The reach is
        /// radii + margin, at most about a metre, so the 3x3 neighbourhood always covers it.
        /// </summary>
        public static bool IsClear(PedestrianBody self, Vector3 position, float margin = 0.1f)
        {
            var cx = Mathf.FloorToInt(position.x / HashCellSize);
            var cz = Mathf.FloorToInt(position.z / HashCellSize);

            for (var dx = -1; dx <= 1; dx++)
            for (var dz = -1; dz <= 1; dz++)
            {
                if (!Cells.TryGetValue(Key(cx + dx, cz + dz), out var cell))
                    continue;

                for (var i = 0; i < cell.Count; i++)
                {
                    var other = cell[i];
                    if (other == self || other == null || !other.Tf || other.Hidden)
                        continue;

                    var radiusSum = (self?.Radius ?? PedestrianSteering.DefaultRadius) + other.Radius;
                    if (PedestrianSteering.Flat(other.Tf.position - position).magnitude < radiusSum + margin)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The stall clock: armed while the walker WANTS to move and the clamp forbids it,
        /// reset the moment it moves or stops wanting to. Stationary bodies never accrue -
        /// standing in a conversation is not a jam, and letting it count would have every
        /// long chat end with the talkers' clearance decayed to nothing.
        /// </summary>
        static void UpdateStall(PedestrianBody self, float allowedAdvance, float dt)
        {
            var moving = self.SpeedMs >= PedestrianSteering.StalledSpeed;

            if (self.Stationary || moving || allowedAdvance > BlockedAdvance)
                self.StalledFor = 0f;
            else
                self.StalledFor += dt;
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
