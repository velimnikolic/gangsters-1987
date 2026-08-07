using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// One car reduced to the only thing the spacing maths cares about: a flat oriented box.
    ///
    /// Length runs along <see cref="Forward"/> and width along <see cref="Right"/>, which is how
    /// every AI car prefab in the pack is authored - the solid body BoxCollider has its long side
    /// on local z. Height is thrown away: the road network is flat apart from bridges, and two
    /// cars on a bridge and the road beneath it never share a lane anyway.
    /// </summary>
    public readonly struct TrafficBox
    {
        public readonly Vector3 Position;
        public readonly Vector3 Forward;
        public readonly Vector3 Right;
        public readonly float HalfLength;
        public readonly float HalfWidth;

        public TrafficBox(Vector3 position, Vector3 forward, float halfLength, float halfWidth)
        {
            Position = position;

            // Flatten before normalising. UpdateCarIncline() pitches the car to keep its wheels on
            // the path, so transform.forward has a vertical component on any sloped tile, and a
            // pitched axis would shorten every projection below by cos(pitch).
            var flat = new Vector3(forward.x, 0f, forward.z);
            Forward = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;

            // The horizontal equivalent of transform.right: cross(up, forward).
            Right = new Vector3(Forward.z, 0f, -Forward.x);

            HalfLength = halfLength;
            HalfWidth = halfWidth;
        }
    }

    /// <summary>
    /// The separation maths, kept free of every UnityEngine.Object so it can be compiled and
    /// exercised headlessly - see the offline Roslyn harness. Nothing here logs, allocates or
    /// touches the scene; give it two boxes and it answers in scalars.
    ///
    /// Everything is measured in ONE car's frame, which is what makes a single formula cover both
    /// cases that matter. The extent of the other car projected onto this car's axes is the
    /// standard support function of an oriented box, so a car crossing at right angles correctly
    /// presents its WIDTH as the obstacle while a car being followed presents its LENGTH. Without
    /// that, a bus turning across a junction reads as 11m of obstacle when it is 2.9m of one.
    /// </summary>
    public static class TrafficGeometry
    {
        /// <summary>
        /// How much the corridor ahead widens per metre of distance, as a half-slope.
        ///
        /// A straight corridor loses the car ahead the moment the lane bends, and this road
        /// network is built from 30m tiles with a curve at every turn - so a purely straight test
        /// goes blind exactly where cars are slowest and closest together. 0.15 is about 8.5
        /// degrees, wide enough to hold a car through a tile-radius curve and narrow enough that
        /// it never reaches the opposite carriageway 3m away until 19m out, by which point the
        /// facing test has already rejected it.
        /// </summary>
        public const float CorridorSlope = 0.15f;

        /// <summary>Constant slack on the corridor, for lane-centring wobble at zero distance.</summary>
        public const float CorridorPad = 0.1f;

        /// <summary>
        /// Hard cap on how wide the corridor may open, whatever the distance.
        ///
        /// Without it the cone eventually reaches the other carriageway, and there is far less
        /// room there than the 3m between lane centres suggests: two cars leave 0.90m of clear air
        /// between their flanks, a bus and a car 0.52m, and two buses 0.15m. An uncapped 0.15
        /// slope crosses 0.90m at 5.3m of gap, so every car on a two-way street was flagging the
        /// traffic coming the other way as an obstacle in its own lane.
        ///
        /// The cap alone cannot save the bus cases - no positive slack fits inside 0.15m - which
        /// is why heading, not geometry, is what separates the carriageways. See the oncoming test
        /// in TrafficRegistry.Probe.
        /// </summary>
        public const float MaxCorridorSlack = 0.6f;

        /// <summary>
        /// Above this closing angle the other car is coming the other way rather than being
        /// followed. -0.5 is 120 degrees, which no same-direction pair reaches even mid-curve.
        /// </summary>
        public const float OncomingDot = -0.5f;

        /// <summary>
        /// Below this |dot| the two cars count as crossing rather than following, and the
        /// right-of-way rule in <see cref="TrafficRegistry"/> applies instead of car-following.
        /// 0.6 is 53 degrees, which puts a car turning out of a junction on the crossing side.
        /// </summary>
        public const float CrossingDot = 0.6f;

        /// <summary>
        /// Extent of <paramref name="box"/> projected onto an arbitrary horizontal axis - the
        /// support function of an oriented box, i.e. how far the box reaches from its own centre
        /// when measured along that axis.
        /// </summary>
        public static float ExtentAlong(in TrafficBox box, Vector3 axis)
        {
            return Mathf.Abs(Vector3.Dot(box.Forward, axis)) * box.HalfLength
                 + Mathf.Abs(Vector3.Dot(box.Right, axis)) * box.HalfWidth;
        }

        /// <summary>
        /// Is <paramref name="other"/> in the corridor ahead of <paramref name="self"/>, and if so
        /// how far?
        ///
        /// <paramref name="gap"/> is bumper to bumper along self's heading - NEGATIVE means the two
        /// bodies already overlap along that axis, which callers must treat as "get out of this"
        /// rather than "stop", or a pair that was teleported into each other freezes there forever.
        ///
        /// <paramref name="lateral"/> is the sideways clearance between the two bodies; negative
        /// means their corridors overlap. <paramref name="facing"/> is the heading agreement, +1
        /// nose to tail, -1 head on, ~0 crossing.
        /// </summary>
        public static bool TryMeasure(in TrafficBox self, in TrafficBox other, float lookahead,
                                      out float gap, out float lateral, out float facing)
        {
            var delta = other.Position - self.Position;

            var along = Vector3.Dot(delta, self.Forward);
            var side = Vector3.Dot(delta, self.Right);

            gap = along - self.HalfLength - ExtentAlong(other, self.Forward);
            lateral = Mathf.Abs(side) - self.HalfWidth - ExtentAlong(other, self.Right);
            facing = Vector3.Dot(self.Forward, other.Forward);

            // Centre behind our centre: we are past it, or it is following us. Either way it is
            // not something we can drive into by going forwards.
            //
            // Crossing traffic is the exception, and it has to be, because a crossing body
            // presents its LENGTH to us: a bus is 11.3m long, so halfway through a junction its
            // centre is already behind ours while its back half is still lying across our nose.
            // The strict test made it vanish at exactly that moment and we drove into its flank.
            // For a crossing body, reject only once the whole thing is behind us.
            //
            // The relaxation is deliberately NOT extended to same-direction traffic. Two cars in
            // a queue can never trip it - they are 4.5m of half-lengths apart before their bodies
            // even touch, so the follower's rear edge is still well behind us - but a car that has
            // been TELEPORTED into us from behind can, and it would come back with a gap around
            // -6.5m: a number that is not a distance to anything, describing a car we cannot
            // drive into, fed to a model that reads negative gaps as an emergency. A body behind
            // us is somebody else's problem in every case, and that is easier to guarantee here
            // than to keep re-establishing downstream.
            var crossing = Mathf.Abs(facing) < CrossingDot;
            var rearmost = crossing ? along + ExtentAlong(other, self.Forward) : along;
            if (rearmost <= 0f)
                return false;

            if (gap > lookahead)
                return false;

            var slack = Mathf.Min(CorridorSlope * Mathf.Max(0f, gap) + CorridorPad, MaxCorridorSlack);
            return lateral <= slack;
        }

        /// <summary>
        /// Bumper-to-bumper distance along self's heading with no corridor or range test - the raw
        /// scalar, used by the right-of-way rule which needs both cars' distance to the same
        /// conflict measured on comparable terms.
        /// </summary>
        public static float GapTo(in TrafficBox self, in TrafficBox other)
        {
            return Vector3.Dot(other.Position - self.Position, self.Forward)
                 - self.HalfLength
                 - ExtentAlong(other, self.Forward);
        }

        /// <summary>
        /// Do the two boxes intersect? A separating-axis test over all four axes of the pair,
        /// which for flat boxes is exact. Used to keep spawns and re-path teleports from putting a
        /// car inside one that is already there.
        /// </summary>
        public static bool Overlaps(in TrafficBox a, in TrafficBox b, float margin = 0f)
        {
            return !SeparatedOnAxesOf(a, b, margin) && !SeparatedOnAxesOf(b, a, margin);
        }

        static bool SeparatedOnAxesOf(in TrafficBox frame, in TrafficBox other, float margin)
        {
            var delta = other.Position - frame.Position;

            if (Mathf.Abs(Vector3.Dot(delta, frame.Forward)) > frame.HalfLength + ExtentAlong(other, frame.Forward) + margin)
                return true;

            return Mathf.Abs(Vector3.Dot(delta, frame.Right)) > frame.HalfWidth + ExtentAlong(other, frame.Right) + margin;
        }
    }

    /// <summary>
    /// The Intelligent Driver Model: free-flow acceleration and car following in one expression,
    /// with no modes to switch between.
    ///
    /// The pack's own attempt was two lines - match the car in front if it is slower - which has
    /// no notion of a gap at all. Two cars at the same speed fail its comparison outright, so the
    /// follower closes at full relative speed and drives through. IDM instead computes a DESIRED
    /// gap that grows with speed and with closing rate, and brakes on the ratio between that and
    /// the real gap, so it converges on a following distance rather than on a speed.
    ///
    /// Pure maths, no Unity objects, no logging - exercised headlessly alongside
    /// <see cref="TrafficGeometry"/>.
    /// </summary>
    public static class CarFollowing
    {
        /// <summary>Bumper-to-bumper gap held at a standstill, in metres.</summary>
        public const float StandstillGap = 1.5f;

        /// <summary>Seconds of following distance at speed. Overridable per car from CityConfig.</summary>
        public const float DefaultHeadway = 0.9f;

        /// <summary>Comfortable acceleration, m/s^2.</summary>
        public const float Accel = 3f;

        /// <summary>Comfortable deceleration, m/s^2. IDM will exceed this when it has to.</summary>
        public const float ComfortBrake = 4f;

        /// <summary>
        /// Hard floor on the bumper-to-bumper gap, enforced by clamping the frame's movement
        /// rather than by the speed model. Deliberately below <see cref="StandstillGap"/> so IDM
        /// does the driving and this only ever fires as a backstop - if it is fired often the
        /// model is mistuned, but cars still never overlap.
        /// </summary>
        public const float MinClearance = 0.6f;

        /// <summary>Nothing further away than this is considered, whatever the speed.</summary>
        public const float MaxLookahead = 40f;

        /// <summary>
        /// Floor on the lookahead, whatever the speed - including zero.
        ///
        /// Without it a stationary car saw exactly <see cref="StandstillGap"/> = 1.5m, and 1.5m
        /// of sight is how junctions gridlocked: a car that stopped to yield went blind to the
        /// conflict it was yielding to, IDM read the empty 1.5m as a clear road, and the car
        /// nosed forward until the crosser was back inside 1.5m - by which point the car was
        /// standing INSIDE the junction. Repeat from three directions and the box is full of
        /// cars that each arrived politely.
        ///
        /// 8m is a crossing bus's half-length (5.65m) plus the standstill gap, with margin: a
        /// stopped car keeps sight of the largest vehicle that can be lying across its line.
        /// Queues do not feel it - at an 8m gap IDM's desired gap at rest is 1.5m, so the
        /// interaction term is (1.5/8)^2 and the brake is negligible.
        /// </summary>
        public const float MinLookahead = 8f;

        /// <summary>Emergency deceleration, m/s^2 - what the car can do rather than what it likes.</summary>
        public const float HardBrake = 8f;

        /// <summary>
        /// Speed a car is allowed to hold when it would otherwise be stuck, m/s. Walking pace.
        ///
        /// The speed model alone cannot get a car out of a jam, because it is a model of comfort
        /// and being wedged 0.8m from somebody's bumper is never comfortable: at rest it wants
        /// <see cref="StandstillGap"/> = 1.5m, so for any real gap below that it returns a
        /// NEGATIVE acceleration and the car it has already stopped can never restart. That is
        /// not a tuning accident, it is what IDM is - it converges on a gap from outside, and
        /// says nothing about how to leave one from inside.
        ///
        /// So the way out is not part of the model. A car that has been declared stalled, or that
        /// is genuinely inside another body, is granted this floor regardless of what the model
        /// wants. Safety does not depend on the model agreeing: the movement clamp in
        /// CarBehavior.Update still bounds the frame, so a creeping car closes to
        /// <see cref="MinClearance"/> and no further.
        /// </summary>
        public const float CreepSpeed = 0.8f;

        /// <summary>
        /// The clearance the clamp will never surrender, however stuck the car is. Not zero on
        /// purpose: the escape below works by letting wedged cars shave the clearance down, and
        /// this is the line where shaving stops - bodies stay visually apart even mid-escape.
        /// </summary>
        public const float EscapeFloor = 0.1f;

        /// <summary>Seconds a car must be genuinely stuck before its clearance starts to decay.</summary>
        public const float EscapeStartsAfter = 6f;

        /// <summary>Stuck seconds at which the decay reaches <see cref="EscapeFloor"/>.</summary>
        public const float EscapeFullAfter = 18f;

        /// <summary>
        /// Stuck seconds after which a car may disregard a stalled crossing blocker entirely -
        /// the same licence a pair that already overlaps gets, and for the same reason: at this
        /// point staying apart has failed and untangling is the only property left worth having.
        /// The registry additionally requires the blocker to be crossing-like AND stationary, so
        /// this can never fire a car through the queue leader in front of it.
        /// </summary>
        public const float HardEscapeAfter = 25f;

        /// <summary>
        /// Metres of actual travel that count as having escaped, resetting the stuck clock. It is
        /// deliberately travel and not time: a car that inches 0.3m and wedges again is still in
        /// the same jam and keeps its escalation level, rather than starting the whole ladder
        /// over after every twitch.
        /// </summary>
        public const float EscapeResetDistance = 1.5f;

        /// <summary>
        /// How close the clamp lets this car get to a particular blocker, given how long the car
        /// has been stuck.
        ///
        /// The escape ladder exists because of the mutual-block ring: A blocks B blocks C blocks
        /// A, every member parked at exactly <see cref="MinClearance"/> - which is where the
        /// clamp puts a car it stops. Each member's allowed advance is then zero, the creep the
        /// stall breaker grants moves nothing, and the arrangement is permanent BY CONSTRUCTION:
        /// no pairwise rule can drain a cycle. The way out is collective - every member gains a
        /// little room, the ring rotates at creep speed, and rotation preserves the pairwise gaps
        /// so the motion sustains itself.
        ///
        /// Only crossing-like blockers decay. A same-direction leader keeps the full clearance
        /// whatever the stuck time, because the car behind a red-light queue IS stuck by this
        /// measure and must never conclude it may close on the leader's bumper: rings are made of
        /// crossing geometry, queues are made of following geometry, and the distinction is
        /// exactly the facing test the registry already runs.
        /// </summary>
        public static float ClearanceFor(bool crossingLike, float stuckSeconds)
        {
            if (!crossingLike)
                return MinClearance;

            var t = Mathf.InverseLerp(EscapeStartsAfter, EscapeFullAfter, stuckSeconds);
            return Mathf.Lerp(MinClearance, EscapeFloor, t);
        }

        /// <summary>
        /// How far the frame may move given a gap and the clearance to preserve. Zero-floored:
        /// a gap already inside the clearance forbids advance, it does not command reverse.
        /// </summary>
        public static float AllowedAdvance(float gap, float clearance)
        {
            return Mathf.Max(0f, gap - clearance);
        }

        /// <summary>
        /// The shortest distance in which this car could still stop. Inside it, a car has to react
        /// to what is in the way whether or not it has right of way - priority means not slowing
        /// down EARLY for someone, not pretending they are absent once they are committed.
        ///
        /// The floor is <see cref="MinClearance"/> and not <see cref="StandstillGap"/>, because
        /// this is a question about STOPPING, not about comfort. Flooring it at the standstill gap
        /// made the answer 1.5m for a car that was already stationary, so a stopped car with right
        /// of way still counted its blocker as something to react to - which meant right of way
        /// became unobtainable in the last 1.5m, and the stall breaker, whose entire mechanism is
        /// to hand out right of way, could not release anything. MinClearance is the honest floor:
        /// inside it the clamp forbids movement anyway, so there is nothing left to decide.
        /// </summary>
        public static float StoppingDistance(float speedMs)
        {
            return MinClearance + speedMs * speedMs / (2f * HardBrake);
        }

        /// <summary>
        /// How far ahead to look: the standstill gap, plus the distance covered during the headway,
        /// plus the distance needed to brake to a stop - floored at <see cref="MinLookahead"/> so
        /// a stationary car does not go blind. Anything nearer than this can matter; anything
        /// beyond it cannot be reached before the next frame's probe.
        /// </summary>
        public static float Lookahead(float speedMs, float headway)
        {
            var distance = StandstillGap
                         + speedMs * headway
                         + speedMs * speedMs / (2f * ComfortBrake);

            return Mathf.Clamp(distance, MinLookahead, MaxLookahead);
        }

        /// <summary>
        /// Acceleration in m/s^2 for a car at <paramref name="speedMs"/> wanting
        /// <paramref name="desiredSpeedMs"/>, with a blocker <paramref name="gap"/> metres ahead
        /// travelling at <paramref name="leadSpeedMs"/>. Pass <see cref="float.PositiveInfinity"/>
        /// for a clear road.
        /// </summary>
        public static float Acceleration(float speedMs, float desiredSpeedMs, float gap, float leadSpeedMs, float headway)
        {
            var free = 1f - Pow4(speedMs / Mathf.Max(desiredSpeedMs, 0.01f));

            if (float.IsPositiveInfinity(gap))
                return Accel * free;

            // The gap the driver wants right now: the standing gap, the headway, and a term that
            // grows sharply with closing rate so approaching a stopped car brakes early.
            var desiredGap = StandstillGap + Mathf.Max(0f,
                speedMs * headway
                + speedMs * (speedMs - leadSpeedMs) / (2f * Mathf.Sqrt(Accel * ComfortBrake)));

            // Squared ratio, so braking rises steeply as the real gap closes on the desired one.
            // Below a hair's breadth the ratio would blow up to infinity, so it is capped at a
            // hard but finite brake instead of producing NaN.
            float interaction;
            if (gap > 0.01f)
            {
                var ratio = desiredGap / gap;
                interaction = Mathf.Min(ratio * ratio, 16f);
            }
            else
            {
                interaction = 16f;
            }

            return Accel * (free - interaction);
        }

        /// <summary>
        /// One frame of longitudinal control: the model, then the floor that keeps a stuck car
        /// from staying stuck.
        ///
        /// The two are combined here rather than in the caller so that the combination is the
        /// thing under test. The defect this replaces was not in either half - IDM is correct
        /// about comfort and the creep is correct about escape - it was that nothing put them
        /// together, so a car the model had stopped had no route back to a positive speed.
        ///
        /// <paramref name="mayCreep"/> comes from Obstacle: the stall breaker has fired, or the
        /// car is inside another body. It does NOT mean "go" - the movement clamp in
        /// CarBehavior.Update still decides how far the car actually gets.
        /// </summary>
        public static float NextSpeed(float speedMs, float desiredSpeedMs, float gap, float leadSpeedMs,
                                      float headway, bool mayCreep, float deltaTime)
        {
            var next = Mathf.Max(0f,
                speedMs + deltaTime * Acceleration(speedMs, desiredSpeedMs, gap, leadSpeedMs, headway));

            if (mayCreep && next < CreepSpeed)
                next = CreepSpeed;

            return next;
        }

        static float Pow4(float x)
        {
            var squared = x * x;
            return squared * squared;
        }
    }
}
