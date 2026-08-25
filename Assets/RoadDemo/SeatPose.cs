using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A man in a car seat with his gun out of the window.
    ///
    /// The seat is the whole problem. A man sat in a pack car is a body whose ROOT is
    /// on the cushion and whose pelvis is carried up off it by the sit clip
    /// (CarBody's seats are measured for exactly that), so the moment anything plays
    /// him a STANDING clip he stands up where he sits: the pelvis goes half a metre
    /// up, and the head goes through the roof. That is what the aim clip did on every
    /// drive-by the town ever drove - a car going past with two heads out of the top
    /// of it - and no amount of tuning the clip fixes it, because the clip is right
    /// and the seat is right and they are simply not the same pose.
    ///
    /// So the aim is DERIVED instead, the way a rider's is (BikePose, and CrewArms
    /// before it): the sit loop keeps playing - the pelvis stays on the cushion, the
    /// breathing and the shift of weight stay his - and this writes the two things a
    /// man shooting out of a window does that a man sitting does not. He leans out
    /// after the gun, and the gun arm goes out at the mark with the barrel down the
    /// line. The arena has already turned him in his seat to face what he is shooting
    /// at (DemoCrews turns the root), so "out of the window" is forward from where he
    /// is now, and the lean is a bend at the spine.
    ///
    /// Two bone writes and a bend, in LateUpdate, on the men actually firing out of a
    /// car - which is at most a carful.
    /// </summary>
    public sealed class SeatPose : MonoBehaviour
    {
        /// <summary>Where the gun points, in the world - the mark's chest. Nothing and
        /// he is just sitting: the clip has him and nothing here writes a bone.</summary>
        public Vector3? AimAt;

        /// <summary>Degrees out of the window, split between the spine and the chest.
        /// Enough that the shoulder clears the sill and the muzzle is outside the door
        /// skin; never so much that he is hanging out of the car by his belt.</summary>
        public static float LeanOut = 20f;

        /// <summary>How much of his own arm he puts out - short of straight, because a
        /// straight arm reads as a man pointing rather than a man shooting.</summary>
        public static float ArmOut = 0.92f;

        /// <summary>The elbow's pole: ahead down the line of the gun and dropped, so
        /// the joint bends under the arm and not up over the shoulder.</summary>
        public static float ElbowAhead = 0.4f, ElbowDown = 0.55f;

        /// <summary>Metres the fist is carried above the line from the shoulder to the
        /// mark. A seated man's shoulder is a good deal LOWER than the window he is
        /// shooting out of - the sill is about at his collarbone - so an arm run flat
        /// along the line of the shot puts the gun through the door skin. He lifts it
        /// out through the opening instead, which is what a man does, and the barrel is
        /// then laid on the mark from wherever the fist ended up rather than from the
        /// shoulder.</summary>
        public static float HandLift = 0.25f;

        Animator _an;
        Transform _spine, _chest, _armUp, _armLow, _hand;
        Vector3 _fingers, _thumb;
        float _armReach;
        bool _ready;

        /// <summary>Read the rig. A body that is not humanoid, or is short of an arm,
        /// is left alone - the way every optional piece in the demo fails.</summary>
        public bool Setup(Animator animator)
        {
            _an = animator != null ? animator : GetComponentInChildren<Animator>();
            if (_an == null || _an.avatar == null || !_an.avatar.isHuman) return false;
            _spine = _an.GetBoneTransform(HumanBodyBones.Spine);
            _chest = _an.GetBoneTransform(HumanBodyBones.Chest);
            if (_chest == null) _chest = _spine;   // Unity's null is not C#'s: ?? would take a dead bone
            _armUp = _an.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _armLow = _an.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _hand = _an.GetBoneTransform(HumanBodyBones.RightHand);
            if (!_armUp || !_armLow || !_hand) return false;

            // the fist's own axes, off the rig's T-pose: for the right hand the fingers
            // run along +X and the thumb points +Z, and CrewArms hangs the gun down
            // exactly that finger axis - so pointing the hand points the gun (BikePose
            // reads the same frame for the same reason)
            var inv = Quaternion.Inverse(CrewArms.TPoseRotation(_an, _hand));
            _fingers = inv * Vector3.right;
            _thumb = inv * Vector3.forward;

            _armReach = Vector3.Distance(_armUp.position, _armLow.position) +
                        Vector3.Distance(_armLow.position, _hand.position);
            _ready = _armReach > 0.01f;
            return _ready;
        }

        // After the clip has had its say, and only while there is a mark: the man not
        // firing is switched off by CrewWalker rather than posed with nothing to do.
        void LateUpdate()
        {
            if (!_ready || !AimAt.HasValue) return;
            Lean(transform.right);
            Gun(transform.up);
        }

        // Out after the gun. He faces the mark already, so the window is ahead of him
        // and leaning out of it is a bend forward - low in the spine, the rest at the
        // chest, which is a man reaching out of a car and not a man bowing.
        void Lean(Vector3 right)
        {
            if (LeanOut < 0.2f) return;
            Turn(_spine, Quaternion.AngleAxis(LeanOut * 0.4f, right));
            Turn(_chest, Quaternion.AngleAxis(LeanOut * 0.6f, right));
        }

        // The gun arm: out through the window, near enough straight, the barrel down
        // the line. Solved AFTER the lean - the bend has moved the shoulder, and an arm
        // put on a shoulder that is about to move is an arm in the door skin.
        void Gun(Vector3 up)
        {
            var from = _armUp.position;
            var dir = AimAt.Value - from;
            if (dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();
            BikePose.TwoBone(_armUp, _armLow, _hand,
                from + dir * (_armReach * ArmOut) + up * HandLift,
                from + dir * ElbowAhead - up * ElbowDown);

            // the barrel is laid from the FIST, not from the shoulder - the lift put
            // them a hand's breadth apart, and it is the fist the round leaves
            var line = AimAt.Value - _hand.position;
            if (line.sqrMagnitude > 0.01f) dir = line.normalized;
            var q1 = Quaternion.LookRotation(dir, up);
            var q2 = Quaternion.LookRotation(_fingers, _thumb);
            _hand.rotation = q1 * Quaternion.Inverse(q2);
        }

        static void Turn(Transform bone, Quaternion world)
        {
            if (bone) bone.rotation = world * bone.rotation;
        }
    }
}
