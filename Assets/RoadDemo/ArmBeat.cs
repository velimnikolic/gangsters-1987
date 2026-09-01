using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The small acted gestures the street's errands were missing: a man making his
    /// point at a shopkeeper's door, and a man taking a bat to a frontage. TALK uses the
    /// city's authored Standing_Talking take: the old two-bone hand solve bent badly on
    /// some Synty rigs and read as a broken-arm attempt to open the door. SWING remains
    /// derived, because the project has no authored frontage strike and the bat must land
    /// against the actual building rather than an animation's imaginary target.
    ///
    /// TALK keeps the whole authored body and only turns its root toward the owner.
    /// SWING is the right arm and a bat from the pack in it: up over the shoulder and
    /// down at the glass, over and over, while the man's own gun waits hidden in his
    /// fist's slot. A man who comes
    /// under fire mid-gesture drops the act at once and gets his gun back - nobody
    /// finishes an argument, or a swing, in a gunfight.
    /// </summary>
    [DefaultExecutionOrder(120)] // after CrewWalker's own arm posing: last writer wins
    public sealed class ArmBeat : MonoBehaviour
    {
        public enum Kind { Talk, Swing }

        const string BatPath =
            "Assets/Synty/PolygonGangWarfare/Prefabs/Weapons/SM_Wep_Bat_01.prefab";
        const float SwingCycle = 0.85f;

        sealed class Act
        {
            public CrewWalker Man;
            public Kind Kind;
            public Vector3 At;
            public float Began, Until;
            public Transform ArmUp, ArmLow, Hand;
            public Vector3 Fingers, Thumb;   // the fist's own axes, off the T-pose
            public float Reach;
            public Transform Bat;
            public readonly List<GameObject> HiddenInFist = new List<GameObject>();
        }

        static ArmBeat instance;
        readonly List<Act> acts = new List<Act>();

        public static bool Talk(CrewWalker man, Vector3 at, float seconds) =>
            Play(man, Kind.Talk, at, seconds);

        public static bool Swing(CrewWalker man, Vector3 at, float seconds) =>
            Play(man, Kind.Swing, at, seconds);

        /// <summary>Is this man mid-gesture? DoorBeat waits for the word to finish.</summary>
        public static bool Acting(CrewWalker man)
        {
            if (instance == null || man == null)
                return false;
            for (var i = 0; i < instance.acts.Count; i++)
                if (instance.acts[i].Man == man)
                    return true;
            return false;
        }

        static bool Play(CrewWalker man, Kind kind, Vector3 at, float seconds)
        {
            if (man == null || man.Dead || man.Tf == null ||
                !man.Tf.gameObject.activeInHierarchy || seconds <= 0f)
                return false;
            // one act per man; and a man in a fight has both hands full already
            if (Acting(man) || man.WantsGunOut)
                return false;

            var an = man.Tf.GetComponentInChildren<Animator>();
            if (an == null || an.avatar == null || !an.avatar.isHuman)
                return false;

            Transform armUp = null, armLow = null, hand = null;
            if (kind == Kind.Talk)
            {
                if (!man.PlayTake(CrewKit.DoorTalk, loop: true, speed: 1f, at: 0f))
                    return false;
            }
            else
            {
                armUp = an.GetBoneTransform(HumanBodyBones.RightUpperArm);
                armLow = an.GetBoneTransform(HumanBodyBones.RightLowerArm);
                hand = an.GetBoneTransform(HumanBodyBones.RightHand);
                if (!armUp || !armLow || !hand)
                    return false;
            }

            if (instance == null)
            {
                var go = new GameObject("Arm Beat") { hideFlags = HideFlags.DontSave };
                instance = go.AddComponent<ArmBeat>();
            }

            var act = new Act
            {
                Man = man,
                Kind = kind,
                At = at,
                Began = Time.time,
                Until = Time.time + seconds,
                ArmUp = armUp,
                ArmLow = armLow,
                Hand = hand,
            };

            if (kind == Kind.Swing)
            {
                var inv = Quaternion.Inverse(CrewArms.TPoseRotation(an, hand));
                act.Fingers = inv * Vector3.right;
                act.Thumb = inv * Vector3.forward;
                act.Reach = Vector3.Distance(armUp.position, armLow.position) +
                            Vector3.Distance(armLow.position, hand.position);
                TakeUpBat(act, an);
            }

            instance.acts.Add(act);
            return true;
        }

        /// <summary>The bat into the fist, the gun out of sight. CrewArms lays the piece
        /// along the fingers exactly as it lays a pistol; only its length cap is undone -
        /// a bat cut to a pistol's cap is a blackjack, and the pack authored a bat.</summary>
        static void TakeUpBat(Act act, Animator an)
        {
            for (var i = 0; i < act.Hand.childCount; i++)
            {
                var held = act.Hand.GetChild(i).gameObject;
                if (!held.activeSelf)
                    continue;
                held.SetActive(false);
                act.HiddenInFist.Add(held);
            }

            var prefab = DemoAssetLoad.Load<GameObject>(BatPath);
            if (prefab == null)
                return;
            act.Bat = CrewArms.Attach(an, prefab);
            if (act.Bat != null)
                act.Bat.localScale = Vector3.one;
        }

        static void PutDownBat(Act act)
        {
            if (act.Bat != null)
                Destroy(act.Bat.gameObject);
            for (var i = 0; i < act.HiddenInFist.Count; i++)
                if (act.HiddenInFist[i] != null)
                    act.HiddenInFist[i].SetActive(true);
            act.HiddenInFist.Clear();
        }

        void LateUpdate()
        {
            for (var i = acts.Count - 1; i >= 0; i--)
            {
                var act = acts[i];
                var man = act.Man;
                var gone = man == null || man.Dead || man.Tf == null ||
                           !man.Tf.gameObject.activeInHierarchy;
                // the fight interrupts the gesture, never the other way round
                var interrupted = !gone && man.WantsGunOut;
                if (gone || interrupted || Time.time >= act.Until)
                {
                    Finish(act);
                    acts.RemoveAt(i);
                    continue;
                }

                Face(act);
                if (act.Kind == Kind.Swing)
                    SwingPose(act);
            }
        }

        static void Finish(Act act)
        {
            if (act.Kind == Kind.Talk)
                act.Man?.EndTake();
            else
                PutDownBat(act);
        }

        /// <summary>He addresses the thing, not the street: the root eased round to face
        /// the door or the frontage while the gesture runs.</summary>
        static void Face(Act act)
        {
            var to = act.At - act.Man.Tf.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.04f)
                return;
            act.Man.Tf.rotation = Quaternion.Slerp(
                act.Man.Tf.rotation,
                Quaternion.LookRotation(to.normalized, Vector3.up),
                8f * Time.deltaTime);
        }

        /// <summary>The swing: up over the shoulder, down at the glass, back to the
        /// middle - windup slow, strike fast, the way a swing weighs.</summary>
        void SwingPose(Act act)
        {
            var root = act.Man.Tf;
            var shoulder = act.ArmUp.position;
            var fwd = act.At - root.position;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.04f ? fwd.normalized : root.forward;

            var mid = shoulder + fwd * 0.35f + Vector3.up * 0.05f;
            var high = shoulder + Vector3.up * 0.45f - fwd * 0.10f + root.right * 0.15f;
            var low = shoulder + fwd * 0.50f - Vector3.up * 0.30f;

            var p = ((Time.time - act.Began) % SwingCycle) / SwingCycle;
            Vector3 target;
            if (p < 0.35f)
                target = Vector3.Lerp(mid, high, Smooth(p / 0.35f));
            else if (p < 0.55f)
                target = Vector3.Lerp(high, low, Smooth((p - 0.35f) / 0.2f));
            else
                target = Vector3.Lerp(low, mid, Smooth((p - 0.55f) / 0.45f));

            BikePose.TwoBone(act.ArmUp, act.ArmLow, act.Hand, target,
                shoulder + root.right * 0.5f - Vector3.up * 0.15f);

            // the bat runs on from the forearm - down the blow, not across it
            AimFist(act, act.Hand.position - act.ArmLow.position, root.right);
        }

        /// <summary>Points the fist's finger axis down a line, the SeatPose trick: the
        /// prop in the fist is laid along the fingers, so pointing the hand points it.</summary>
        static void AimFist(Act act, Vector3 along, Vector3 up)
        {
            if (along.sqrMagnitude < 0.001f)
                return;
            var want = Quaternion.LookRotation(along.normalized, up);
            var fist = Quaternion.LookRotation(act.Fingers, act.Thumb);
            act.Hand.rotation = want * Quaternion.Inverse(fist);
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        void OnDestroy()
        {
            for (var i = 0; i < acts.Count; i++)
                Finish(acts[i]);
            acts.Clear();
            if (instance == this)
                instance = null;
        }
    }
}
