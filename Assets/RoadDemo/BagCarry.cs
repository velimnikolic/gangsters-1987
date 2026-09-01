using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The collection job made visible: from departure until the front's counter, the
    /// hood walking the round carries the pack's duffel in his free hand.
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
            "Assets/Synty/PolygonPoliceStation/Prefabs/Props/SM_Prop_Duffle_Bag_01.prefab";

        // The Gang Warfare SM_Prop_Bag_01 used here before is a 9 cm evidence pouch,
        // not luggage; at street-camera distance it read as part of the fist. This
        // duffel is authored at 1.7 m, so plant it at a believable carried size.
        const float DuffleScale = 0.42f;

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

        /// <summary>This hood owns the crew's collection bag for the active round. Safe
        /// to call again when a dead carrier must hand the job to a survivor.</summary>
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
                bag.localScale = Vector3.one * DuffleScale;
                foreach (var col in bag.GetComponentsInChildren<Collider>())
                    Destroy(col);
                foreach (var body in bag.GetComponentsInChildren<Rigidbody>())
                    Destroy(body);
                carry = new Carry { CrewId = crewId, Bag = bag };
                instance.carries.Add(carry);
            }

            carry.Man = man;
            carry.Hand = hand;
            SetLayer(carry.Bag, man.Tf.gameObject.layer);
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

            carry.Bag.gameObject.SetActive(true);
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

        static void SetLayer(Transform root, int layer)
        {
            if (root == null)
                return;
            root.gameObject.layer = layer;
            for (var i = 0; i < root.childCount; i++)
                SetLayer(root.GetChild(i), layer);
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

                // The bag goes through the door with its man. Leaving it parked at the
                // last fist position made a paid stop look like a floating duffel on the
                // pavement while the carrier was inside. Drop re-exposes it when a lost
                // round actually means to leave it on the street.
                if (carry.Man == null || carry.Man.Dead || carry.Man.Tf == null ||
                    !carry.Man.Tf.gameObject.activeInHierarchy || carry.Hand == null)
                {
                    carry.Bag.gameObject.SetActive(false);
                    continue;
                }

                if (!carry.Bag.gameObject.activeSelf)
                    carry.Bag.gameObject.SetActive(true);

                var fwd = carry.Man.Tf.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.01f)
                    fwd = Vector3.forward;
                // handle at the fist, weight under it, held upright whatever the arm does
                carry.Bag.SetPositionAndRotation(
                    carry.Hand.position - Vector3.up * 0.22f,
                    Quaternion.LookRotation(fwd.normalized, Vector3.up) *
                    Quaternion.Euler(0f, -90f, 0f));
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
