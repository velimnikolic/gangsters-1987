using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A man on a motorcycle: hips on the saddle, fists on the grips, boots on the
    /// pegs, and a foot down at the lights.
    ///
    /// There is no riding animation anywhere in the project and none is needed. The
    /// pose is DERIVED, the way CrewArms derives a gun in a fist: whatever clip the
    /// body is playing underneath - the library's Driving_Loop, a bench sit, an idle -
    /// runs first, and then this writes the four limbs and the spine over the top of
    /// it in LateUpdate, reaching for points the bike itself was measured for
    /// (BikeBody). So a rider fits the bike he is actually on, not the bike somebody
    /// had in mind when the clip was made, and the same code seats a hood on a stolen
    /// moped and a patrolman on a police tourer.
    ///
    /// What the underlying clip still gives is everything nobody is writing over: the
    /// breathing, the shift of weight, the head. That is why a seated clip is the
    /// right base and an idle stand is not - the pelvis has to be sat down.
    ///
    /// The costs are honest. This is four two-bone solves and a couple of quaternions
    /// per rider per frame - nothing beside a skinned mesh - but it is a LateUpdate on
    /// a MonoBehaviour, so a hundred bikes is a hundred callbacks; the traffic's bikes
    /// are meant to be a handful, and the crowd's culling takes the rest.
    /// </summary>
        // NOT ExecuteAlways, and that is the point. LateUpdate re-seats a man every frame -
    // right in Play, and unusable in the editor, where it puts him back before a drag of
    // the move handle has finished and the axes read as broken. Off Play nobody poses
    // anybody unless they ask: the bench calls Apply() itself when a number changes, and
    // the baked sitting scene calls it once and then takes this component off altogether.
    public sealed class BikePose : MonoBehaviour
    {
        // ------------------------------------------------------------------ the seat

        /// <summary>The bike he is on. Null and nothing happens - the clip plays as it
        /// would anyway.</summary>
        public BikeBody Bike;

        /// <summary>Riding behind, not in front: hands on the man ahead instead of on
        /// the bars, boots on the back pegs, knees folded up.</summary>
        public bool Pillion;

        /// <summary>The man in front, when this is the pillion - what his hands hold
        /// on to.</summary>
        public BikePose Rider;

        /// <summary>Metres a second, and the lean of the bike: the first bends him over
        /// the tank, the second is already in the bike's own transform and only wanted
        /// here for the pillion, who leans a little later than the man steering.</summary>
        public float Speed;

        /// <summary>Stopped: the left boot comes off the peg and goes to the road.</summary>
        public bool FootDown;

        /// <summary>A gun pointed at this place in the world (the pillion on a
        /// drive-by). Nothing: both hands where they belong.</summary>
        public Vector3? AimAt;

        /// <summary>Degrees over the tank at speed, and the speed that reaches it.</summary>
        public static float CrouchMax = 15f, CrouchAt = 16f;

        // The handful of numbers below were set by eye and are the ones a rider who
        // "sits wrong" is usually wrong by: where the elbow is thrown, which way the
        // knee bends, how the toes lie on the peg, and how wide the pillion holds on.
        // Static, like BikeBody's proportions and for the same reason - the tuning
        // bench (Assets/BikeDemo) pushes them about during Play so a man can be sat
        // properly by looking at him rather than by rebuilding.

        /// <summary>The elbow's pole: out past the flank, down, and a little back.
        /// A rider's arms are not tucked in at his sides.</summary>
        public static float ElbowOut = 0.55f, ElbowDown = 0.42f, ElbowBack = 0.18f;

        /// <summary>The knee's pole: forward and out. Never backwards - that is the one
        /// thing a two-bone solve will happily do if nobody tells it which way the joint
        /// bends. The wider figure is the boot that has gone down to the road.</summary>
        public static float KneeAhead = 0.8f, KneeOut = 0.5f, KneeOutFootDown = 0.85f, KneeDown = 0.1f;

        /// <summary>Which way the toes lie on a peg: ahead and tipped down.</summary>
        public static float ToeAhead = 0.94f, ToeDown = 0.34f;

        /// <summary>The pillion's hands on the man in front: one either side of his
        /// waist, lifted a little and forward of it.</summary>
        public static float HoldWide = 0.19f, HoldLift = 0.06f, HoldForward = 0.05f;

        /// <summary>Last word on the fists and the boots, for tuning by eye in Play:
        /// nothing is written over when they are off.</summary>
        public bool Hands = true, Feet = true, Torso = true;

        // ------------------------------------------------------------------ the rig

        Animator _an;
        Transform _hips, _spine, _chest, _head;
        Transform _armUpL, _armLowL, _handL, _armUpR, _armLowR, _handR;
        Transform _legUpL, _legLowL, _footL, _legUpR, _legLowR, _footR;

        // the T-pose frames: which way, in each part's own frame, the fingers run, the
        // back of the hand faces, the thumb points, the toes point and the sole faces.
        // Read once off the avatar (CrewArms.TPoseRotation) and true in every pose
        // after that, on every Synty rig, without a table of eyeballed angles.
        Vector3 _fingersL, _backL, _thumbL, _fingersR, _backR, _thumbR;
        Vector3 _toesL, _soleUpL, _toesR, _soleUpR;

        static readonly System.Collections.Generic.HashSet<string> Fitted =
            new System.Collections.Generic.HashSet<string>();

        float _armReach, _legReach;
        int _applied = -1;
        bool _ready;

        /// <summary>Where his hips are this frame - what a pillion holds on to.</summary>
        public Vector3 HipsPoint => _hips ? _hips.position : transform.position + Vector3.up * 0.9f;

        /// <summary>Chest height on the man, for a muzzle and for a mark.</summary>
        public Vector3 ChestPoint => _chest ? _chest.position : HipsPoint + Vector3.up * 0.35f;

        /// <summary>His gun hand, wherever the pose has put it.</summary>
        public Transform GunHand => _handR;

        // ------------------------------------------------------------------ setting up

        /// <summary>Read the rig and settle him on the bike. The animator may be on the
        /// body or under it; a rig that is not humanoid is simply left alone, which is
        /// how every optional piece in the demo fails.</summary>
        public bool Setup(BikeBody bike, bool pillion, Animator animator = null)
        {
            Bike = bike;
            Pillion = pillion;
            _an = animator ? animator : GetComponentInChildren<Animator>();
            if (_an == null || _an.avatar == null || !_an.avatar.isHuman) return false;

            // NOT GetBoneTransform(Hips) - see CrewArms.Pelvis: one pack maps the
            // foot-level Root bone as the pelvis, and a man seated by it floats
            _hips = CrewArms.Pelvis(_an);
            _spine = _an.GetBoneTransform(HumanBodyBones.Spine);
            _chest = _an.GetBoneTransform(HumanBodyBones.Chest);
            if (_chest == null) _chest = _spine;   // Unity's null is not C#'s: ?? would take a dead bone
            _head = _an.GetBoneTransform(HumanBodyBones.Head);
            _armUpL = _an.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _armLowL = _an.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _handL = _an.GetBoneTransform(HumanBodyBones.LeftHand);
            _armUpR = _an.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _armLowR = _an.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _handR = _an.GetBoneTransform(HumanBodyBones.RightHand);
            _legUpL = _an.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _legLowL = _an.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _footL = _an.GetBoneTransform(HumanBodyBones.LeftFoot);
            _legUpR = _an.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _legLowR = _an.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _footR = _an.GetBoneTransform(HumanBodyBones.RightFoot);

            if (!_hips || !_armUpR || !_armLowR || !_handR || !_legUpR || !_legLowR || !_footR) return false;

            Frame(_handL, -1f, out _fingersL, out _backL, out _thumbL);
            Frame(_handR, 1f, out _fingersR, out _backR, out _thumbR);
            Foot(_footL, out _toesL, out _soleUpL);
            Foot(_footR, out _toesR, out _soleUpR);

            _armReach = Vector3.Distance(_armUpR.position, _armLowR.position) +
                        Vector3.Distance(_armLowR.position, _handR.position);
            _legReach = Vector3.Distance(_legUpR.position, _legLowR.position) +
                        Vector3.Distance(_legLowR.position, _footR.position);

            // the pack's men are a size too big for the pack's vehicles - the reason
            // CarOccupant folds a driver's legs away under the sill. On a bike they are
            // in plain sight, so instead the man is taken down a little: enough that his
            // knee is bent like a rider's and not splayed out sideways like a frog's,
            // never so much that he reads as a child on a grown man's machine.
            if (Bike != null && _legReach > 0.01f)
            {
                float span = Vector3.Distance(Bike.Saddle(pillion), Bike.Peg(true, pillion));
                float wants = span / 0.72f;   // a rider's knee is bent: hip to boot is short of the leg
                float fit = Mathf.Clamp(wants / _legReach, 0.86f, 1f);
                if (fit < 0.995f) transform.localScale = Vector3.one * fit;
                if (fit < 0.98f && Fitted.Add(name + "/" + (Bike.Tf ? Bike.Tf.name : "?")))
                    Debug.Log($"[Bike] {name} on {(Bike.Tf ? Bike.Tf.name : "?")}: " +
                              $"leg {_legReach:F2} m for a {span:F2} m saddle-to-peg - taken to {fit:F2}");
            }

            _ready = true;
            return true;
        }

        // ------------------------------------------------------------------ the frame

        void LateUpdate() => Apply();

        /// <summary>The pose, at most once a frame. Called by LateUpdate, and by the
        /// pillion on the man in front of him - two LateUpdates run in whatever order
        /// Unity likes, and a pillion holding on to hips that have not been put on the
        /// saddle yet holds a frame of thin air.</summary>
        public void Apply()
        {
            if (!_ready || Bike == null || Bike.Tf == null) return;
            if (_applied == Time.frameCount) return;
            _applied = Time.frameCount;
            if (Pillion && Rider != null) Rider.Apply();

            var up = Bike.Tf.up;
            var fwd = Bike.Tf.forward;
            var right = Bike.Tf.right;

            Seat();
            if (Torso) Bend(right, up, fwd);
            if (Hands) Hold(up, fwd, right);
            if (Feet) Stand(up, fwd, right);
            Look(up, fwd);
        }

        // Hips onto the saddle. The clip carries the pelvis some way above the body's
        // root and every clip carries it a different way, so the offset is not guessed:
        // the body is moved by however far its hips are from where they should be, this
        // frame, after the clip has had its say. A man on a bouncing idle bounces on
        // the saddle rather than through it.
        void Seat()
        {
            transform.rotation = Bike.Tf.rotation;
            var want = Bike.Saddle(Pillion);
            transform.position += want - _hips.position;
        }

        // Over the tank with the speed, and the head kept up out of it. The pillion
        // sits up straighter - he has nothing to hold on to but the man in front.
        void Bend(Vector3 right, Vector3 up, Vector3 fwd)
        {
            float crouch = Mathf.Clamp01(Mathf.Abs(Speed) / CrouchAt) * CrouchMax * (Pillion ? 0.6f : 1f);
            if (AimAt.HasValue) crouch *= 0.4f;
            if (crouch < 0.2f && !AimAt.HasValue) return;
            Turn(_spine, Quaternion.AngleAxis(crouch * 0.45f, right));
            Turn(_chest, Quaternion.AngleAxis(crouch * 0.55f, right));

            // a man with a gun out turns his chest after it, or he is shooting through
            // his own shoulder
            if (!AimAt.HasValue || _chest == null) return;
            var to = AimAt.Value - _chest.position;
            to -= up * Vector3.Dot(to, up);
            if (to.sqrMagnitude < 0.01f) return;
            float yaw = Vector3.SignedAngle(fwd, to.normalized, up);
            Turn(_chest, Quaternion.AngleAxis(Mathf.Clamp(yaw, -60f, 60f) * 0.55f, up));
        }

        // ------------------------------------------------------------------ the fists

        void Hold(Vector3 up, Vector3 fwd, Vector3 right)
        {
            // the bars swing with the steering, and the grips with them
            var turn = Quaternion.AngleAxis(Bike.BarsTurned, up);
            var bar = turn * Bike.Way(Bike.BarAxis);

            if (Pillion)
            {
                // a hand on the man in front: at his waist, one either side. Nothing to
                // hold and the hands fall to his own knees, which is what a man does.
                var anchor = Rider != null ? Rider.HipsPoint : Bike.Point(Bike.SaddleRider);
                var lift = up * HoldLift - fwd * HoldForward;
                if (_handL) Reach(_armUpL, _armLowL, _handL, anchor - right * HoldWide + lift,
                    Elbow(_armUpL, -1f, up, fwd, right), -bar, up, _fingersL, _backL);
                if (AimAt.HasValue) Gun(up, fwd);
                else Reach(_armUpR, _armLowR, _handR, anchor + right * HoldWide + lift,
                    Elbow(_armUpR, 1f, up, fwd, right), bar, up, _fingersR, _backR);
                return;
            }

            if (_handL) Reach(_armUpL, _armLowL, _handL, Bike.GripNow(false),
                Elbow(_armUpL, -1f, up, fwd, right), -bar, up, _fingersL, _backL);
            if (AimAt.HasValue) Gun(up, fwd);   // a rider firing one-handed: rare, and it happens
            else Reach(_armUpR, _armLowR, _handR, Bike.GripNow(true),
                Elbow(_armUpR, 1f, up, fwd, right), bar, up, _fingersR, _backR);
        }

        // The gun arm: out at the mark, near enough straight, with the barrel down the
        // line. The barrel is the fingers' own direction - CrewArms hangs the gun in
        // the fist along exactly that axis, so pointing the hand points the gun.
        void Gun(Vector3 up, Vector3 fwd)
        {
            if (_armUpR == null || !AimAt.HasValue) return;
            var from = _armUpR.position;
            var dir = AimAt.Value - from;
            if (dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();
            var target = from + dir * (_armReach * 0.92f);
            var pole = from + dir * 0.4f - up * 0.55f;
            Reach(_armUpR, _armLowR, _handR, target, pole, dir, up, _fingersR, _thumbR);
        }

        // Where the elbow is thrown: out past the flank, down and a little back - a
        // rider's arms are not tucked in at his sides.
        Vector3 Elbow(Transform shoulder, float side, Vector3 up, Vector3 fwd, Vector3 right)
        {
            var at = shoulder ? shoulder.position : transform.position;
            return at + right * (side * ElbowOut) - up * ElbowDown - fwd * ElbowBack;
        }

        // ------------------------------------------------------------------ the boots

        void Stand(Vector3 up, Vector3 fwd, Vector3 right)
        {
            // at a standstill the left boot goes to the road; the right stays on the peg
            // (a rider holds the back brake with it), which is what a man does at a light
            bool down = FootDown && !Pillion;
            var toes = fwd * ToeAhead - up * ToeDown;

            if (_footL)
            {
                var target = down ? Bike.Ground(false) : Bike.Peg(false, Pillion);
                var pole = KneePole(_legUpL, -1f, up, fwd, right, down);
                Reach(_legUpL, _legLowL, _footL, target, pole,
                    down ? (Vector3)(fwd * 0.99f - up * 0.14f) : toes, up, _toesL, _soleUpL);
            }
            if (_footR)
            {
                Reach(_legUpR, _legLowR, _footR, Bike.Peg(true, Pillion),
                    KneePole(_legUpR, 1f, up, fwd, right, false), toes, up, _toesR, _soleUpR);
            }
        }

        // The knee goes forward and out - never backwards, which is the one thing a
        // two-bone solve will happily do if nobody tells it which way the joint bends.
        Vector3 KneePole(Transform hip, float side, Vector3 up, Vector3 fwd, Vector3 right, bool down)
        {
            var at = hip ? hip.position : transform.position;
            float out_ = down ? KneeOutFootDown : KneeOut;
            return at + fwd * KneeAhead + right * (side * out_) - up * KneeDown;
        }

        // ------------------------------------------------------------------ the head

        void Look(Vector3 up, Vector3 fwd)
        {
            if (_head == null) return;
            var dir = AimAt.HasValue ? (AimAt.Value - _head.position).normalized : fwd;
            // never more than a glance off the way he is going: a head screwed round
            // backwards is the classic look-at bug
            float yaw = Vector3.SignedAngle(fwd, Vector3.ProjectOnPlane(dir, up).normalized, up);
            if (Mathf.Abs(yaw) > 70f) dir = fwd;
            var want = Quaternion.LookRotation(dir, up);
            // the clip has already turned his head; blend, and only part of the way, so
            // the neck keeps whatever life the animation gave it
            var keep = Quaternion.Inverse(Quaternion.LookRotation(fwd, up)) * _head.rotation;
            _head.rotation = Quaternion.Slerp(_head.rotation, want * keep, 0.5f);
        }

        // ------------------------------------------------------------------ the solve

        // One limb: shoulder-elbow-fist, or hip-knee-boot, put on the target with the
        // joint thrown toward the pole, and the end part turned so its own axes lie
        // along the world directions given (the fingers down the bar, the toes down the
        // road). Nothing is done if the rig is short of a bone.
        void Reach(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole,
            Vector3 endAlong, Vector3 endUp, Vector3 alongLocal, Vector3 upLocal)
        {
            if (!a || !b || !c) return;
            TwoBone(a, b, c, target, pole);
            if (endAlong.sqrMagnitude < 1e-6f) return;
            var q1 = Quaternion.LookRotation(endAlong.normalized, endUp);
            var q2 = Quaternion.LookRotation(alongLocal, upLocal);
            c.rotation = q1 * Quaternion.Inverse(q2);
        }

        /// <summary>The plain analytic two-joint solve: the triangle a-b-c is re-cut so
        /// that c lands on the target, with the corner at b thrown toward the pole. No
        /// iteration, no controller, no IK pass on an Animator - which matters, because
        /// the men here are animated by bare playable graphs with no controller to hang
        /// an IK pass on (PedestrianAgent, CarOccupant).</summary>
        public static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
        {
            Vector3 pa = a.position, pb = b.position, pc = c.position;
            float lab = Vector3.Distance(pa, pb), lcb = Vector3.Distance(pc, pb);
            if (lab < 1e-4f || lcb < 1e-4f) return;
            float lat = Mathf.Clamp(Vector3.Distance(pa, target), 1e-3f, lab + lcb - 1e-3f);

            float ac_ab_0 = Between(pc - pa, pb - pa);
            float ba_bc_0 = Between(pa - pb, pc - pb);
            float ac_at_0 = Between(pc - pa, target - pa);

            float ac_ab_1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            float ba_bc_1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

            // the plane the joint bends in: the one holding the limb and the pole. A
            // limb already straight has no plane of its own, and the pole is all there
            // is to go on.
            var axis = Vector3.Cross(pc - pa, pole - pa);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(pc - pa, pb - pa);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(pc - pa, Vector3.up);
            if (axis.sqrMagnitude < 1e-8f) return;
            axis.Normalize();

            var axisAim = Vector3.Cross(pc - pa, target - pa);
            if (axisAim.sqrMagnitude < 1e-8f) axisAim = axis;
            axisAim.Normalize();

            var r0 = Quaternion.AngleAxis((ac_ab_1 - ac_ab_0) * Mathf.Rad2Deg, Quaternion.Inverse(a.rotation) * axis);
            var r1 = Quaternion.AngleAxis((ba_bc_1 - ba_bc_0) * Mathf.Rad2Deg, Quaternion.Inverse(b.rotation) * axis);
            var r2 = Quaternion.AngleAxis(ac_at_0 * Mathf.Rad2Deg, Quaternion.Inverse(a.rotation) * axisAim);

            a.localRotation = a.localRotation * r0 * r2;
            b.localRotation = b.localRotation * r1;
        }

        static float Between(Vector3 u, Vector3 v)
        {
            if (u.sqrMagnitude < 1e-10f || v.sqrMagnitude < 1e-10f) return 0f;
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(u.normalized, v.normalized), -1f, 1f));
        }

        static void Turn(Transform bone, Quaternion world)
        {
            if (bone) bone.rotation = world * bone.rotation;
        }

        // ------------------------------------------------------------------ the T-pose
        //
        // In the rig's own T-pose the arms are out and the palms are down, so for the
        // right hand the fingers run along +X, the back of the hand faces +Y and the
        // thumb points +Z (CrewArms, which hangs a pistol on exactly that); the left
        // hand mirrors it, fingers along -X, and both thumbs still point forward. The
        // feet point +Z with the sole down. Turned into each bone's own frame those
        // hold in every pose the clip puts the limb through.

        void Frame(Transform hand, float side, out Vector3 fingers, out Vector3 back, out Vector3 thumb)
        {
            fingers = Vector3.right * side;
            back = Vector3.up;
            thumb = Vector3.forward;
            if (hand == null) return;
            var inv = Quaternion.Inverse(CrewArms.TPoseRotation(_an, hand));
            fingers = inv * (Vector3.right * side);
            back = inv * Vector3.up;
            thumb = inv * Vector3.forward;
        }

        void Foot(Transform foot, out Vector3 toes, out Vector3 soleUp)
        {
            toes = Vector3.forward;
            soleUp = Vector3.up;
            if (foot == null) return;
            var inv = Quaternion.Inverse(CrewArms.TPoseRotation(_an, foot));
            toes = inv * Vector3.forward;
            soleUp = inv * Vector3.up;
        }
    }
}
