using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The take made visible: from the first door that pays until the front's counter,
    /// the man walking the collection round carries the pack's duffel in his free hand.
    /// Nothing here is money - the round's arithmetic lives in TerritoryRuntime - this
    /// only dresses the man the arithmetic says is carrying it.
    ///
    /// The bag is not parented into the rig: it is held at the LEFT fist every frame
    /// and kept upright, so it hangs like a carried bag whatever the walk clip does
    /// with the arm. A round that ends banked takes the bag with it; a round that dies
    /// on the street - the crew wiped, scattered, retasked - drops the bag where its
    /// man last stood and leaves it lying there a while, which is what a lost take
    /// looks like.
    /// </summary>
    public sealed class BagCarry : MonoBehaviour
    {
        const string BagPath =
            "Assets/Synty/PolygonGangWarfare/Prefabs/Props/SM_Prop_Bag_01.prefab";

        /// <summary>Seconds a dropped bag lies on the street before it is struck.</summary>
        const float DroppedFor = 45f;

        sealed class Carry
        {
            public int CrewId;
            public CrewWalker Man;
            public Transform Hand;
            public Transform Bag;
        }

        static BagCarry instance;
        readonly List<Carry> carries = new List<Carry>();

        /// <summary>This crew's round is carrying money now, in this man's hand. Safe to
        /// call every stop - the bag simply changes hands when another man settles one.</summary>
        public static void Give(int crewId, CrewWalker man)
        {
            if (man == null || man.Dead || man.Tf == null)
                return;

            if (instance == null)
            {
                var go = new GameObject("Bag Carry") { hideFlags = HideFlags.DontSave };
                instance = go.AddComponent<BagCarry>();
            }

            var carry = instance.Of(crewId);
            if (carry != null && carry.Man == man)
                return;

            var an = man.Tf.GetComponentInChildren<Animator>();
            var hand = an != null && an.avatar != null && an.avatar.isHuman
                ? an.GetBoneTransform(HumanBodyBones.LeftHand)
                : null;
            if (hand == null)
                return;

            if (carry == null)
            {
                var prefab = DemoAssetLoad.Load<GameObject>(BagPath);
                if (prefab == null)
                    return;
                var bag = Object.Instantiate(prefab).transform;
                bag.name = "The take";
                foreach (var col in bag.GetComponentsInChildren<Collider>())
                    Destroy(col);
                foreach (var body in bag.GetComponentsInChildren<Rigidbody>())
                    Destroy(body);
                carry = new Carry { CrewId = crewId, Bag = bag };
                instance.carries.Add(carry);
            }

            carry.Man = man;
            carry.Hand = hand;
        }

        /// <summary>The round is over. Banked, the bag goes over the counter with the
        /// money; lost, it drops where the man last stood and lies there.</summary>
        public static void Drop(int crewId, bool banked)
        {
            var carry = instance != null ? instance.Of(crewId) : null;
            if (carry == null)
                return;
            instance.carries.Remove(carry);
            if (carry.Bag == null)
                return;

            if (banked)
            {
                Destroy(carry.Bag.gameObject);
                return;
            }

            var rest = carry.Bag.position;
            rest.y = 0.02f;
            carry.Bag.SetPositionAndRotation(
                rest, Quaternion.Euler(0f, carry.Bag.eulerAngles.y, 0f));
            Destroy(carry.Bag.gameObject, DroppedFor);
        }

        Carry Of(int crewId)
        {
            for (var i = 0; i < carries.Count; i++)
                if (carries[i].CrewId == crewId)
                    return carries[i];
            return null;
        }

        void LateUpdate()
        {
            for (var i = carries.Count - 1; i >= 0; i--)
            {
                var carry = carries[i];
                if (carry.Bag == null)
                {
                    carries.RemoveAt(i);
                    continue;
                }

                // The man gone mid-round - dead, or stepped inside a door - parks the
                // bag where it is; the round system will say soon enough whether it
                // banked or fell, and Drop settles the bag then.
                if (carry.Man == null || carry.Man.Dead || carry.Man.Tf == null ||
                    !carry.Man.Tf.gameObject.activeInHierarchy || carry.Hand == null)
                    continue;

                var fwd = carry.Man.Tf.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.01f)
                    fwd = Vector3.forward;
                // handle at the fist, weight under it, held upright whatever the arm does
                carry.Bag.SetPositionAndRotation(
                    carry.Hand.position - Vector3.up * 0.16f,
                    Quaternion.LookRotation(fwd.normalized, Vector3.up));
            }
        }

        void OnDestroy()
        {
            for (var i = 0; i < carries.Count; i++)
                if (carries[i].Bag != null)
                    Destroy(carries[i].Bag.gameObject);
            carries.Clear();
            if (instance == this)
                instance = null;
        }
    }
}
