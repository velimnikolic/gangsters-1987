using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;
using LivingCity.City;

namespace LivingCity.Entities
{
    /// <summary>One live car's entry in <see cref="TrafficRegistry"/>.</summary>
    public sealed class TrafficBody
    {
        public readonly CarBehavior Car;
        public readonly Transform Tf;
        /// <summary>
        /// EntityId rather than int: Unity 6.5 raises BOTH GetInstanceID() and the implicit
        /// EntityId-to-int conversion as CS0619 errors, so the id has to stay in its own type
        /// the whole way. EntityId carries its own comparison operators, which is all
        /// ShouldYieldTo's tie-break needs.
        /// </summary>
        public readonly EntityId Id;

        /// <summary>Measured once at registration - see <see cref="TrafficRegistry.Measure"/>.</summary>
        public readonly float HalfLength;
        public readonly float HalfWidth;

        /// <summary>Written by the car each frame. Metres per second, never negative.</summary>
        public float SpeedMs;

        /// <summary>How long this car has been held still by crossing traffic. See the stall breaker.</summary>
        public float StalledFor;

        /// <summary>
        /// Seconds this car has been motionless with the movement clamp itself as the binding
        /// constraint - wedged, not merely waiting. Distinct from <see cref="StalledFor"/> on
        /// purpose: StalledFor arms the periodic release windows and resets every time one fires,
        /// which is correct for a valve but useless as a measure of how long the jam has lasted.
        /// This one only resets on actual escape - see <see cref="EscapeProgress"/> - so it can
        /// escalate: it drives the clearance decay in <see cref="CarFollowing.ClearanceFor"/>.
        /// </summary>
        public float StuckFor;

        /// <summary>
        /// Metres travelled since the car was last wedged. Reaching
        /// <see cref="CarFollowing.EscapeResetDistance"/> clears <see cref="StuckFor"/>; being
        /// wedged again first zeroes the odometer, so partial escapes keep their escalation.
        /// </summary>
        public float EscapeProgress;

        /// <summary>Time.time until which this car refuses to yield to crossing traffic.</summary>
        public float IgnoreCrossingUntil;

        /// <summary>
        /// Extra seconds this particular car waits before giving up on yielding, so a symmetric
        /// pair does not give up on the same frame and recreate the standoff. Derived from the id
        /// rather than drawn randomly, so a car behaves the same way every run.
        /// </summary>
        public readonly float StallJitter;

        public TrafficBody(CarBehavior car, float halfLength, float halfWidth, float stallJitterRange)
        {
            Car = car;
            Tf = car.transform;
            Id = car.GetEntityId();
            HalfLength = halfLength;
            HalfWidth = halfWidth;
            StallJitter = Mathf.Abs(Id.GetHashCode() % 1000) / 1000f * stallJitterRange;
        }

        /// <summary>
        /// Built fresh on every read rather than cached per frame. Cars move during Update in
        /// whatever order Unity feels like, so a cached pose would be stale for roughly half of
        /// them - and a stale pose makes the clamp optimistic in exactly the situation it exists
        /// to catch. Thirty cars is nine hundred of these a frame, which is nothing.
        /// </summary>
        public TrafficBox Box => new TrafficBox(Tf.position, Tf.forward, HalfLength, HalfWidth);
    }

    /// <summary>What the road ahead looks like to one car this frame.</summary>
    public readonly struct Obstacle
    {
        /// <summary>Gap to the nearest blocker the car must SLOW for. Infinite when clear.</summary>
        public readonly float Gap;

        /// <summary>That blocker's speed resolved along our heading, m/s. Never negative.</summary>
        public readonly float LeadSpeedMs;

        /// <summary>
        /// How far the car may move this frame, over every blocker of ANY kind including ones we
        /// have priority over: right of way decides who slows down gracefully, it does not decide
        /// who is allowed to drive through whom.
        ///
        /// This is an ALLOWANCE, not a gap - the clearance each blocker is owed has already been
        /// subtracted, per blocker, because the clearance is no longer one number: a
        /// same-direction leader is always owed the full <see cref="CarFollowing.MinClearance"/>,
        /// while a crossing blocker's entitlement decays with <see cref="TrafficBody.StuckFor"/>
        /// so a wedged ring can eventually rotate itself loose. See
        /// <see cref="CarFollowing.ClearanceFor"/> for why the two classes differ.
        ///
        /// Bodies we ALREADY overlap are left out of it, and only those. The clamp's job is to
        /// stop a gap closing; against something that has already closed it there is nothing left
        /// to protect, and including it would freeze the pair at the overlap forever. Every other
        /// blocker stays in, which is the part that matters - see <see cref="Overlapping"/>.
        /// </summary>
        public readonly float AllowedAdvance;

        /// <summary>
        /// Our body genuinely intersects another car's, by the exact separating-axis test.
        ///
        /// It used to be inferred from a negative <see cref="Gap"/>, which is a projection onto
        /// ONE axis and says nothing about the other. A car crossing the junction ahead of us -
        /// centre 2.5m forward, 3.5m to the side, bodies comfortably apart - measures gap -0.8
        /// and set this true. Worse, it was one flag for the whole probe and the caller read it as
        /// "the clamp does not apply this frame", so a car passing in front switched off the
        /// anti-overlap guarantee against the car we were following. Junctions were where cars
        /// crashed because junctions were where this went wrong.
        ///
        /// Now it means what it says, and it no longer disables anything: overlapping bodies are
        /// simply absent from <see cref="AllowedAdvance"/>. The flag survives only to tell the car it
        /// may creep - being inside somebody is the one state a car has to be allowed to leave.
        /// </summary>
        public readonly bool Overlapping;

        /// <summary>The binding constraint on <see cref="Gap"/> was crossing traffic, not a leader.</summary>
        public readonly bool BlockedByCrossing;

        /// <summary>
        /// The stall breaker has given up on yielding for this car. Also the car's licence to
        /// creep - see <see cref="CarFollowing.CreepSpeed"/>. Both halves are needed: dropping the
        /// yield alone only decides who SHOULD go, and a car whose speed model is pinned at zero
        /// cannot act on winning that argument.
        /// </summary>
        public readonly bool ReleaseActive;

        public Obstacle(float gap, float leadSpeedMs, float allowedAdvance, bool overlapping,
                        bool blockedByCrossing, bool releaseActive)
        {
            Gap = gap;
            LeadSpeedMs = leadSpeedMs;
            AllowedAdvance = allowedAdvance;
            Overlapping = overlapping;
            BlockedByCrossing = blockedByCrossing;
            ReleaseActive = releaseActive;
        }

        public static Obstacle Clear =>
            new Obstacle(float.PositiveInfinity, 0f, float.PositiveInfinity, false, false, false);
    }

    /// <summary>
    /// Every AI car in the scene, and the one question worth asking about them: what is in the way?
    ///
    /// This exists because the pack has no answer. Its cars are moved by writing straight to
    /// transform.position with a KINEMATIC Rigidbody, so PhysX never generates a contact between
    /// two of them - there is no collision to suppress and none to rely on. Its avoidance is a
    /// trigger box reaching about two metres past the bumper of a car doing 100km/h, read once on
    /// OnTriggerEnter, and disabled outright on the first lane of every route. Cars therefore
    /// drove through each other, which is what this replaces.
    ///
    /// Bodies live in a uniform grid of CityGrid.CellSize buckets, rebuilt at most once per
    /// frame. The flat list was right at carCount 30 (a probe was 30 comparisons); at 300 it is
    /// 90k SAT tests a frame, which is exactly the "high hundreds" case the old comment promised
    /// to bucket for. The buckets only choose CANDIDATES - every geometric decision still reads
    /// the live transform - so the ring has to cover the worst question ever asked of it:
    /// MaxLookahead plus both bodies' half-lengths. See CollectCandidates.
    /// </summary>
    public static class TrafficRegistry
    {
        /// <summary>
        /// Seconds of being held still by crossing traffic before a car stops yielding. Insurance,
        /// not a mechanism: the right-of-way rule below is symmetric and should never stall a pair
        /// in the first place. It fires when three or more cars meet in a way the pairwise rule
        /// cannot resolve, and when a car yields to one that has itself been stopped by a red light.
        /// </summary>
        const float StallTimeout = 4f;

        /// <summary>How long a stalled car ignores crossing traffic once the timeout fires.</summary>
        const float StallRelease = 1.5f;

        /// <summary>Spread of the per-car stagger added to StallTimeout. See TrafficBody.StallJitter.</summary>
        const float StallJitterRange = 1.5f;

        /// <summary>Below this the car counts as stopped for the stall breaker, m/s.</summary>
        const float StalledSpeed = 0.2f;

        /// <summary>
        /// How much closer to the conflict one car has to be before the other yields to it. Without
        /// the margin, two cars arriving symmetrically would flip the decision every frame as their
        /// gaps crossed; with it, ties fall through to the instance-id rule and stay decided.
        /// </summary>
        const float PriorityMargin = 0.25f;

        /// <summary>Used when a prefab has no solid box to measure. No pack car hits this.</summary>
        const float FallbackHalfLength = 2.25f;
        const float FallbackHalfWidth = 1.05f;

        static readonly List<TrafficBody> Bodies = new List<TrafficBody>();

        /// <summary>
        /// The spatial buckets: XZ grid of <see cref="CityGrid.CellSize"/>-metre cells, same
        /// long-key idiom as <see cref="PedestrianRegistry"/>. A body sits in exactly one bucket
        /// (keyed on its centre), so a cell sweep never visits anybody twice.
        /// </summary>
        static readonly Dictionary<long, List<TrafficBody>> Buckets =
            new Dictionary<long, List<TrafficBody>>();

        /// <summary>Recycled cell lists, so a moving fleet does not churn the heap every rebuild.</summary>
        static readonly Stack<List<TrafficBody>> BucketPool = new Stack<List<TrafficBody>>();

        /// <summary>
        /// Frame the buckets were last built for. Rebuilt lazily on the first query of a frame:
        /// cars move inside a frame, but at 45km/h that is 0.2m per frame against the whole
        /// spare cell of ring slack, so membership staleness within a frame cannot push a
        /// relevant body out of the swept ring. Register/Unregister invalidate outright - a
        /// just-removed body must stop existing NOW, not at the next frame boundary.
        /// </summary>
        static int bucketsBuiltFrame = -1;

        /// <summary>
        /// Longest half-length ever registered. Grows monotonically (the bus pins it at 4.43m)
        /// and feeds the candidate ring: a blocker's RELEVANCE is judged from our centre, but
        /// its bucket is keyed on ITS centre, up to its own half-length further away.
        /// </summary>
        static float maxHalfLength = FallbackHalfLength;

        /// <summary>
        /// Lateral allowance added to every candidate ring. The corridor tests reach sideways as
        /// well as forward - two half-widths plus the probe's own slop - and 4m comfortably
        /// exceeds the widest pairing in the fleet (a bus is 1.43m to the midline).
        /// </summary>
        const float WidthSlack = 4f;

        public static int Count => Bodies.Count;

        /// <summary>
        /// Every live car, read-only. Exists for the audio layer's nearest-to-camera scan;
        /// thirty bodies makes a flat pass the right tool, same as Probe's own loop.
        /// </summary>
        public static IReadOnlyList<TrafficBody> All => Bodies;

        public static TrafficBody Register(CarBehavior car)
        {
            if (!car)
                return null;

            Measure(car.gameObject, out var halfLength, out var halfWidth);

            var body = new TrafficBody(car, halfLength, halfWidth, StallJitterRange);
            Bodies.Add(body);
            if (halfLength > maxHalfLength)
                maxHalfLength = halfLength;
            bucketsBuiltFrame = -1;
            return body;
        }

        public static void Unregister(TrafficBody body)
        {
            if (body != null && Bodies.Remove(body))
                bucketsBuiltFrame = -1;
        }

        /// <summary>
        /// Puts every live body in its bucket, once per frame at most. Clearing and refilling
        /// 300 entries is cheaper than maintaining incremental membership from code this file
        /// does not own - cars are moved by CarBehavior writing transform.position directly, so
        /// there is no move event to hook.
        /// </summary>
        static void SyncBuckets()
        {
            if (Time.frameCount == bucketsBuiltFrame)
                return;
            bucketsBuiltFrame = Time.frameCount;

            foreach (var pair in Buckets)
            {
                pair.Value.Clear();
                BucketPool.Push(pair.Value);
            }
            Buckets.Clear();

            for (var i = 0; i < Bodies.Count; i++)
            {
                var body = Bodies[i];
                if (body == null || !body.Tf)
                    continue;

                var key = KeyFor(body.Tf.position);
                if (!Buckets.TryGetValue(key, out var cell))
                {
                    cell = BucketPool.Count > 0 ? BucketPool.Pop() : new List<TrafficBody>();
                    Buckets.Add(key, cell);
                }
                cell.Add(body);
            }
        }

        static long KeyFor(Vector3 position) =>
            Key(Mathf.FloorToInt(position.x / CityGrid.CellSize),
                Mathf.FloorToInt(position.z / CityGrid.CellSize));

        static long Key(int cx, int cz) => ((long)cx << 32) | (uint)cz;

        /// <summary>
        /// The cell rectangle that provably contains every body whose CENTRE could matter to a
        /// query of this range about this point. Pure maths, exercised headlessly - the covering
        /// guarantee is the whole correctness argument for the buckets, so it gets its own test
        /// (TrafficModelTests.BucketRingCoversItsRange).
        /// </summary>
        internal static void CellRange(Vector3 centre, float range,
                                       out int minX, out int maxX, out int minZ, out int maxZ)
        {
            // Ceil, not round: a centre flush against its cell wall still has to reach `range`
            // past it, and ring*CellSize is exactly the guaranteed reach beyond any wall.
            var ring = Mathf.Max(1, Mathf.CeilToInt(range / CityGrid.CellSize));
            var cx = Mathf.FloorToInt(centre.x / CityGrid.CellSize);
            var cz = Mathf.FloorToInt(centre.z / CityGrid.CellSize);
            minX = cx - ring;
            maxX = cx + ring;
            minZ = cz - ring;
            maxZ = cz + ring;
        }

        /// <summary>
        /// Every body other than <paramref name="self"/> whose centre lies in a bucket the query
        /// ring touches, into <paramref name="into"/>. The caller states its range honestly -
        /// lookahead or required room, PLUS its own half-length (the corridor starts at its
        /// bumper, buckets are keyed on centres) PLUS <see cref="maxHalfLength"/> (the blocker's
        /// centre trails its bumper by the same argument) - and the width slack covers the rest.
        /// </summary>
        static void CollectCandidates(TrafficBody self, Vector3 centre, float range,
                                      List<TrafficBody> into)
        {
            SyncBuckets();
            into.Clear();

            CellRange(centre, range + WidthSlack, out var minX, out var maxX, out var minZ, out var maxZ);
            for (var cz = minZ; cz <= maxZ; cz++)
            {
                for (var cx = minX; cx <= maxX; cx++)
                {
                    if (!Buckets.TryGetValue(Key(cx, cz), out var cell))
                        continue;

                    for (var i = 0; i < cell.Count; i++)
                    {
                        var other = cell[i];
                        if (other != self)
                            into.Add(other);
                    }
                }
            }
        }

        /// <summary>Scratch for the query methods. Single-threaded, never re-entered.</summary>
        static readonly List<TrafficBody> Candidates = new List<TrafficBody>();

        /// <summary>
        /// The nearest thing in the way of <paramref name="self"/>, and whether it is something to
        /// slow for or merely something not to drive into.
        ///
        /// Two separate answers come back on purpose. <see cref="Obstacle.Gap"/> drives the speed
        /// model and honours right of way, so the car with priority keeps its speed through a
        /// junction instead of both cars braking at each other. <see cref="Obstacle.AllowedAdvance"/>
        /// ignores priority entirely and is what physically stops the car - being in the right is
        /// not a licence to occupy someone else's bodywork.
        /// </summary>
        public static Obstacle Probe(TrafficBody self, float lookahead)
        {
            if (self == null || !self.Tf)
                return Obstacle.Clear;

            var box = self.Box;
            var yieldingSuspended = Time.time < self.IgnoreCrossingUntil;

            var gap = float.PositiveInfinity;
            var leadSpeed = 0f;
            var allowedAdvance = float.PositiveInfinity;
            var overlapping = false;
            var blocked = false;
            var blockedByCrossing = false;

            CollectCandidates(self, box.Position, lookahead + self.HalfLength + maxHalfLength,
                              Candidates);
            for (var i = 0; i < Candidates.Count; i++)
            {
                var other = Candidates[i];
                if (other == null || !other.Tf)
                    continue;

                var otherBox = other.Box;
                var intersecting = TrafficGeometry.Overlaps(box, otherBox);

                if (!TrafficGeometry.TryMeasure(box, otherBox, lookahead, out var thisGap, out _, out var facing))
                {
                    // TryMeasure answers "is it in the corridor AHEAD", which a body we are
                    // already inside can fail - it can be beside us, or mostly behind us. Being
                    // inside somebody still has to be reported, or the car has no licence to creep
                    // back out and stays there.
                    if (intersecting)
                        overlapping = true;
                    continue;
                }

                // Traffic coming the other way is in the other carriageway, and no width test can
                // establish that: the lanes are 3m apart and a bus is 2.85m wide, so two buses
                // pass with 15cm of daylight between them. Heading settles it exactly, so an
                // oncoming car counts only when the bodies genuinely intersect - which means
                // somebody is on the wrong side of the road, not that somebody is approaching.
                //
                // Getting this wrong is what jammed the city: every car was braking for oncoming
                // traffic as though it were a stationary obstacle in its own lane, and because the
                // approach speeds add up, IDM read it as an emergency. The bus tripped it worst -
                // being the widest thing on the road, it started reacting at 2.8m of clearance
                // where a car only reacted at 5.3m - and everything behind the bus queued up.
                if (facing < TrafficGeometry.OncomingDot && !intersecting)
                    continue;

                blocked = true;

                // Same-direction traffic is facing >= CrossingDot; everything below that -
                // crossing, or the oncoming survivors of the test above, which are the
                // intersecting ones - is "crossing-like" for the purposes of clearance. The
                // distinction feeds ClearanceFor: rings that need dissolving are made of
                // crossing geometry, queues that must never compact are made of following
                // geometry.
                var crossingLike = facing < TrafficGeometry.CrossingDot;

                if (intersecting)
                {
                    // Already inside this one. The clamp cannot un-close a gap that is shut, and
                    // feeding it a negative number would hold the pair together permanently, so
                    // this body is left out of the allowance entirely - and ONLY this body.
                    // Everything else on the road still constrains us, which is the difference
                    // between "let these two untangle" and the old "this car may now drive
                    // through anything".
                    overlapping = true;
                }
                else if (crossingLike && self.StuckFor > CarFollowing.HardEscapeAfter
                         && other.SpeedMs < StalledSpeed)
                {
                    // The last rung of the escape ladder. This car has been wedged for
                    // HardEscapeAfter seconds - the clearance decay alone did not free it, so the
                    // ring it is in is geometrically locked and the only way out is through. A
                    // stalled crossing blocker stops constraining the clamp, exactly as if the
                    // pair already overlapped, and the creep the release windows keep granting
                    // finally moves the car. Three conditions bound it: the car must have been
                    // stuck for a very long time, the blocker must be crossing-like - a
                    // same-direction leader is NEVER passed through, so a queue cannot ghost -
                    // and the blocker must itself be stationary, so nothing is driven through
                    // moving traffic.
                }
                else
                {
                    var allowance = CarFollowing.AllowedAdvance(thisGap,
                        CarFollowing.ClearanceFor(crossingLike, self.StuckFor));
                    if (allowance < allowedAdvance)
                        allowedAdvance = allowance;
                }

                var crossing = Mathf.Abs(facing) < TrafficGeometry.CrossingDot;
                if (crossing && !ShouldYieldTo(self, box, other, otherBox, yieldingSuspended))
                {
                    // We have right of way, so we do not slow down early for them. We do still
                    // have to react once they are inside the distance we could stop in, or the
                    // clamp below ends up doing the braking - which is not braking, it is the car
                    // hitting a wall at the last frame and wedging there.
                    if (thisGap > CarFollowing.StoppingDistance(self.SpeedMs))
                        continue;
                }

                if (thisGap >= gap)
                    continue;

                gap = thisGap;
                blockedByCrossing = crossing;

                // Only the component of the blocker's motion that carries it away along OUR heading
                // relieves the gap. A car crossing at right angles contributes nothing, and one
                // coming the other way contributes less than nothing - clamped to zero, since IDM
                // treats the lead speed as the speed we could safely settle at.
                leadSpeed = Mathf.Max(0f, other.SpeedMs * Vector3.Dot(otherBox.Forward, box.Forward));
            }

            // Overlapping counts as blocked in its own right. It no longer shows up in the
            // allowance, and a car wedged inside another with clear road otherwise would look
            // unobstructed - which is the one arrangement that most needs the timer running.
            // "blocked" is tracked as its own flag rather than inferred from a finite allowance,
            // because the hard-escape rung above can empty the allowance while the car is still
            // very much in a jam - and concluding "not blocked" there would stop the release
            // windows that the escape depends on.
            UpdateStall(self, overlapping || blocked, allowedAdvance);

            // Re-read rather than reusing yieldingSuspended from the top: UpdateStall may have
            // fired the release on this very frame, and making the car wait until the next one to
            // act on it is a frame of standing still for no reason.
            return new Obstacle(gap, leadSpeed, allowedAdvance, overlapping, blockedByCrossing,
                                Time.time < self.IgnoreCrossingUntil);
        }

        /// <summary>
        /// Right of way between two cars whose paths cross: the one further from the conflict
        /// gives way. That is both what a driver does and, more importantly, ANTISYMMETRIC - run
        /// it from either car and exactly one of them yields, which is what stops a junction
        /// deadlocking with both cars politely stopped.
        ///
        /// Distance is measured as each car's own gap to the other, so a long vehicle that is
        /// already halfway across counts as having arrived.
        /// </summary>
        static bool ShouldYieldTo(TrafficBody self, in TrafficBox selfBox, TrafficBody other, in TrafficBox otherBox,
                                  bool yieldingSuspended)
        {
            if (yieldingSuspended)
                return false;

            // A car that is already stopped has nothing to lose by giving way, and a car that is
            // moving has a junction to clear. Deciding it this way round drains a knot instead of
            // tightening it: whoever is stuck stays stuck a moment longer, whoever is rolling gets
            // out of everyone's way. It also breaks the case the gap comparison below cannot -
            // two cars whose gaps are equal but only one of which can actually act on winning.
            var selfStopped = self.SpeedMs < StalledSpeed;
            var otherStopped = other.SpeedMs < StalledSpeed;
            if (selfStopped != otherStopped)
                return selfStopped;

            var mine = TrafficGeometry.GapTo(selfBox, otherBox);
            var theirs = TrafficGeometry.GapTo(otherBox, selfBox);

            if (mine > theirs + PriorityMargin)
                return true;
            if (theirs > mine + PriorityMargin)
                return false;

            // Dead heat. Any total order will do as long as both cars compute the same one.
            return self.Id > other.Id;
        }

        /// <summary>
        /// Arms the release valve when a car is standing still with something in front of it.
        ///
        /// Keyed on being motionless rather than on what kind of blocker it is. The earlier version
        /// only counted crossing traffic, which meant the one arrangement that most needs a way out
        /// - a car wedged against something it had right of way over, so its speed model never even
        /// saw the obstacle - was the one arrangement the valve could not detect.
        ///
        /// Firing it while merely queued at a red light is harmless. It does two things - the car
        /// stops deferring to crossing traffic, and it is allowed to creep - and the clamp bounds
        /// both, so the worst case is a car in a queue closing up to MinClearance.
        ///
        /// The creep half is not optional. Dropping the yield alone was the original design and it
        /// released nothing: yielding is decided by ShouldYieldTo, whose result is then gated on
        /// the blocker being outside StoppingDistance, and the car this fires for is by definition
        /// stationary and wedged well inside it. The valve opened onto a wall.
        /// </summary>
        static void UpdateStall(TrafficBody self, bool blocked, float allowedAdvance)
        {
            var moving = self.SpeedMs >= StalledSpeed;

            // The stuck clock resets on TRAVEL, not on time and not on the release firing.
            // Resetting it when the release fired is precisely the old defect in miniature: the
            // valve opened, achieved nothing against the clamp, and its own bookkeeping declared
            // the problem solved. Distance cannot be argued with - a car that has moved a
            // car-length of road is out of whatever it was wedged in, and a car that has not is
            // still in it, however many times the valve cycled.
            if (moving)
            {
                self.EscapeProgress += self.SpeedMs * Time.deltaTime;
                if (self.EscapeProgress >= CarFollowing.EscapeResetDistance)
                {
                    self.StuckFor = 0f;
                    self.EscapeProgress = 0f;
                }
            }

            if (!blocked || moving)
            {
                self.StalledFor = 0f;
                return;
            }

            self.EscapeProgress = 0f;
            self.StalledFor += Time.deltaTime;

            // Wedged, as opposed to merely stopped: the clamp itself is what forbids movement.
            // MinClearance is the right threshold because it is the largest allowance the
            // clearance decay can ever hand back (MinClearance down to EscapeFloor), so a car
            // mid-escape that has not actually moved yet keeps escalating instead of oscillating
            // at the boundary. A car stopped with real room in front - yielding at a junction
            // mouth, queued behind a light - never trips this, and so never escalates.
            if (allowedAdvance <= CarFollowing.MinClearance)
                self.StuckFor += Time.deltaTime;

            // The jitter matters. A symmetric pair reaches the timeout on the same frame, and two
            // cars that stop deferring to each other simultaneously are in exactly the standoff
            // they started in. Staggering by a per-car fraction of a second lets one go first.
            if (self.StalledFor >= StallTimeout + self.StallJitter)
            {
                self.IgnoreCrossingUntil = Time.time + StallRelease;
                self.StalledFor = 0f;
            }
        }

        /// <summary>
        /// Below this a car near the junction exit counts as parked there rather than passing
        /// through, m/s. The filter is what keeps "don't block the box" from strangling
        /// throughput: traffic FLOWING across the far side of a junction is not a reason to hold
        /// back, only traffic standing on it is.
        /// </summary>
        const float ExitBlockerSpeed = 1f;

        /// <summary>
        /// Would a car of <paramref name="self"/>'s footprint, placed just past the junction at
        /// <paramref name="exitPos"/>, have <paramref name="requiredRoom"/> metres of road to
        /// itself? Asked BEFORE committing to a junction: a car that cannot clear the far side
        /// must wait at the entry line, because a car standing inside the box is one arc of a
        /// mutual-block ring waiting for its other members to arrive.
        ///
        /// Oncoming bodies are still ignored - the opposite carriageway beyond the exit is no
        /// more our problem there than anywhere else - and so are moving ones, per
        /// <see cref="ExitBlockerSpeed"/>.
        /// </summary>
        public static bool IsExitBlocked(TrafficBody self, Vector3 exitPos, Vector3 exitDir, float requiredRoom)
        {
            if (self == null)
                return false;

            var exitBox = new TrafficBox(exitPos, exitDir, self.HalfLength, self.HalfWidth);

            CollectCandidates(self, exitPos, requiredRoom + self.HalfLength + maxHalfLength,
                              Candidates);
            for (var i = 0; i < Candidates.Count; i++)
            {
                var other = Candidates[i];
                if (other == null || !other.Tf)
                    continue;

                if (other.SpeedMs >= ExitBlockerSpeed)
                    continue;

                var otherBox = other.Box;

                // A body standing ON the exit itself fails the corridor measure - it can be
                // beside or behind the virtual car's centre - but it is the most literal way of
                // being in the way, so it is tested for directly.
                if (TrafficGeometry.Overlaps(exitBox, otherBox))
                    return true;

                if (!TrafficGeometry.TryMeasure(exitBox, otherBox, requiredRoom, out var gap, out _, out var facing))
                    continue;

                if (facing < TrafficGeometry.OncomingDot)
                    continue;

                if (gap < requiredRoom)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Would a car of this footprint, placed here, be inside another one? Used before the two
        /// moments that can put a car somewhere without driving it there: the re-path teleport in
        /// CarBehavior.SetNewPath, and a spawn at a map-edge gate.
        /// </summary>
        public static bool IsClear(TrafficBody self, Vector3 position, Vector3 forward, float margin = 0.4f)
        {
            if (self == null)
                return true;

            var box = new TrafficBox(position, forward, self.HalfLength, self.HalfWidth);

            CollectCandidates(self, position, self.HalfLength + maxHalfLength + margin,
                              Candidates);
            for (var i = 0; i < Candidates.Count; i++)
            {
                var other = Candidates[i];
                if (other == null || !other.Tf)
                    continue;

                if (TrafficGeometry.Overlaps(box, other.Box, margin))
                    return false;
            }

            return true;
        }

        /// <summary>Same question for a car that has not been registered yet, i.e. before Instantiate.</summary>
        public static bool IsClear(Vector3 position, Vector3 forward, float halfLength, float halfWidth, float margin = 0.4f)
        {
            var box = new TrafficBox(position, forward, halfLength, halfWidth);

            CollectCandidates(null, position, halfLength + maxHalfLength + margin, Candidates);
            for (var i = 0; i < Candidates.Count; i++)
            {
                var other = Candidates[i];
                if (other == null || !other.Tf)
                    continue;

                if (TrafficGeometry.Overlaps(box, other.Box, margin))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The car's footprint, taken from its largest solid BoxCollider.
        ///
        /// It has to be measured rather than assumed: the fleet runs from a 4.72m car-passenger_AI
        /// to the 8.85m bus-school_AI, and a fixed lookahead would either strand the car a
        /// bus-length behind everything or let the bus swallow whatever it is following. The
        /// trigger box is skipped - that is the pack's forward feeler, not the body - and the
        /// largest solid one is taken.
        ///
        /// That last rule has no live example left. bus-passenger_AI was the only model in the
        /// pack carrying five solid boxes, and it left with the Transit bucket; every vehicle on
        /// the road today has exactly one. The loop stays because the rule is about the pack, not
        /// about the current fleet - re-add a multi-box model and this keeps working.
        ///
        /// Length is local z and width local x, which is how every AI prefab in the pack is built.
        /// </summary>
        static void Measure(GameObject car, out float halfLength, out float halfWidth)
        {
            BoxCollider best = null;
            var bestVolume = -1f;

            var boxes = car.GetComponentsInChildren<BoxCollider>(true);
            for (var i = 0; i < boxes.Length; i++)
            {
                var candidate = boxes[i];
                if (candidate.isTrigger)
                    continue;

                var size = Vector3.Scale(candidate.size, candidate.transform.lossyScale);
                var volume = Mathf.Abs(size.x * size.y * size.z);
                if (volume <= bestVolume)
                    continue;

                bestVolume = volume;
                best = candidate;
            }

            if (best)
            {
                var size = Vector3.Scale(best.size, best.transform.lossyScale);
                halfLength = Mathf.Abs(size.z) * 0.5f;
                halfWidth = Mathf.Abs(size.x) * 0.5f;
                return;
            }

            halfLength = FallbackHalfLength;
            halfWidth = FallbackHalfWidth;
        }
    }
}
