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
    /// man last stood. An ordinary abandoned prop fades; a dead carrier's take is
    /// registered below and stays until somebody physically claims it.
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

        /// <summary>
        /// The round is over. Banked, the bag goes over the counter with the money;
        /// lost, it drops where the man last stood and lies there.
        ///
        /// THE MONEY IS NOT THE MODEL. A carry can legitimately fail to exist - the
        /// carrier's rig has no humanoid left hand, or the duffel prefab did not load -
        /// and the round is settled as lost either way. Returning early on a missing
        /// visual used to destroy a dead collector's take outright: the wire said it
        /// was lying in the street to be claimed and there was nothing there. So the
        /// GROUND RECORD is made whenever the round says one is owed, and the model is
        /// hung on it afterwards if there is one to hang.
        /// </summary>
        public static void Drop(int crewId, bool banked, int take = 0,
            int ownerFaction = -1, string fallenName = "", bool persistent = false,
            DemoCrews crews = null, Vector3? at = null)
        {
            var carry = instance != null ? instance.Of(crewId) : null;
            if (carry != null)
                instance.carries.Remove(carry);

            var bag = carry != null ? carry.Bag : null;
            var owed = !banked && persistent && take > 0;

            if (banked || !owed)
            {
                if (bag != null)
                {
                    if (banked) Destroy(bag.gameObject);
                    else { Rest(bag, at); Destroy(bag.gameObject, DroppedFor); }
                }
                return;
            }

            // A place to lie: where the bag was, else where the caller says the man
            // fell. Without either there is nowhere to put it and nothing to claim.
            var where = bag != null ? bag.position : at ?? Vector3.zero;
            if (bag == null && !at.HasValue)
                return;

            if (bag == null)
            {
                // No model - the hand or the prefab was missing. The take is still the
                // take: it lies here as a claimable thing with whatever body can be
                // made for it, and the street's own marker draws off BagOnGround.All.
                var prefab = DemoAssetLoad.Load<GameObject>(BagPath);
                bag = prefab != null
                    ? Object.Instantiate(prefab).transform
                    : new GameObject("The take").transform;
                bag.localScale = Vector3.one * DuffleScale;
                foreach (var col in bag.GetComponentsInChildren<Collider>())
                    Destroy(col);
                foreach (var body in bag.GetComponentsInChildren<Rigidbody>())
                    Destroy(body);
            }

            bag.gameObject.SetActive(true);
            Rest(bag, where);
            bag.gameObject.AddComponent<BagOnGround>().Initialize(
                take, crewId, ownerFaction, fallenName, crews);
        }

        /// <summary>Lay it flat on the pavement where it fell.</summary>
        static void Rest(Transform bag, Vector3? at)
        {
            var rest = at ?? bag.position;
            rest.y = 0.02f;
            bag.SetPositionAndRotation(
                rest, Quaternion.Euler(0f, bag.eulerAngles.y, 0f));
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

    /// <summary>A dead collector's take. It has no expiry: somebody reaches it and
    /// banks it, a rival takes it, or it remains part of the street.</summary>
    public sealed class BagOnGround : MonoBehaviour
    {
        const float TakeReach = 3.5f;
        static readonly List<BagOnGround> Ground = new List<BagOnGround>();

        public static IReadOnlyList<BagOnGround> All => Ground;
        public int Take { get; private set; }
        public int CrewId { get; private set; }
        public int OwnerFaction { get; private set; }
        public string FallenName { get; private set; } = "";

        DemoCrews crews;
        DemoCrews.Unit claimant;

        public void Initialize(int take, int crewId, int ownerFaction,
            string fallenName, DemoCrews source)
        {
            Take = Mathf.Max(0, take);
            CrewId = crewId;
            OwnerFaction = ownerFaction;
            FallenName = fallenName ?? "";
            crews = source;
            if (!Ground.Contains(this))
                Ground.Add(this);
            name = "The take · $" + Take;
        }

        public bool Claim(DemoCrews source, DemoCrews.Unit unit)
        {
            if (source == null || unit == null || unit.Faction != 0 ||
                unit.IsDetachment || unit.Wiped)
                return false;
            crews = source;
            claimant = unit;
            if (!crews.OrderUnit(unit, transform.position, out _, run: false, speak: false))
            {
                claimant = null;
                return false;
            }
            CrewOverlay.Announce("TAKE THE BAG · $" + Take, 3f,
                new Color(0.9f, 0.85f, 0.65f));
            CrewSpeech.Say(unit, LivingCity.Data.VoiceLines.OrdBag);
            return true;
        }

        void Update()
        {
            if (crews == null || Take <= 0)
                return;

            if (claimant != null)
            {
                if (claimant.Wiped || !crews.Units.Contains(claimant))
                    claimant = null;
                else if (FirstWithinReach(claimant) is { } taker)
                {
                    var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
                    if (outfit == null)
                        return;
                    BagCarry.Give(CrewId, taker);
                    outfit.BankTake(Take);
                    BagCarry.Drop(CrewId, banked: true);
                    CrewOverlay.Announce(
                        taker.DisplayName.ToUpperInvariant() + " TOOK THE BAG · $" + Take, 4f,
                        new Color(0.65f, 0.9f, 0.65f));
                    Destroy(gameObject);
                    return;
                }
            }

            for (var i = 0; i < crews.Units.Count; i++)
            {
                var rival = crews.Units[i];
                if (rival == null || rival.Faction <= 0 || rival.Wiped ||
                    FirstWithinReach(rival) == null)
                    continue;
                var house = LivingCity.Outfit.Underworld.Current?.Of(rival.Faction);
                if (house != null)
                {
                    house.Runner.BankTake(Take);
                    house.Touch();
                }
                CrewOverlay.Announce(
                    (rival.GangName ?? "A RIVAL").ToUpperInvariant() + " TOOK THE BAG OFF " +
                    (string.IsNullOrEmpty(FallenName)
                        ? "OUR MEN" : FallenName.ToUpperInvariant()) + " · $" + Take, 4f,
                    new Color(1f, 0.55f, 0.45f));
                Destroy(gameObject);
                return;
            }
        }

        CrewWalker FirstWithinReach(DemoCrews.Unit unit)
        {
            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.Tf == null ||
                    !man.Tf.gameObject.activeInHierarchy)
                    continue;
                var gap = man.Tf.position - transform.position;
                gap.y = 0f;
                if (gap.sqrMagnitude <= TakeReach * TakeReach)
                    return man;
            }
            return null;
        }

        void OnEnable()
        {
            if (!Ground.Contains(this))
                Ground.Add(this);
        }

        void OnDisable() => Ground.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Ground.Clear();
    }
}
