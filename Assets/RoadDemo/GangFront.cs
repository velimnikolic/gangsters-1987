using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The premises a family operates behind - stuck on the BUILDING itself, so a click
    /// on the door is a click on the family. One per family, chosen when the mobs are
    /// dealt onto the street (RoadDemoBuilder.SpawnRivals): the capo's crew stands
    /// outside this door, and this is the building the front card opens over.
    ///
    /// The books it carries (<see cref="LivingCity.Gangs.FrontDossier"/>) are dealt off
    /// the family's own child seed and never change afterwards - the shop's licence
    /// number must not be a different number the second time the player looks at it.
    ///
    /// The registry is static and reset at SubsystemRegistration, the OverlayRegistry
    /// discipline: with domain reload off, statics outlive Play and the second run would
    /// open cards over buildings that were destroyed with the first city.
    /// </summary>
    public sealed class GangFront : MonoBehaviour
    {
        static readonly List<GangFront> all = new List<GangFront>();

        /// <summary>Every front standing in the city, in the order the families were
        /// seated. Read by the map and the front card; nobody writes it but Awake.</summary>
        public static IReadOnlyList<GangFront> All => all;

        public int GangId;
        public string GangName = "";
        public LivingCity.Gangs.FrontDossier Books;

        /// <summary>The doorstep - where the card's tail points and the crew stands.</summary>
        public Vector3 Door;

        /// <summary>The exact pavement point paired with the generated shop door. Core's
        /// residential view may be recycled, so this model-side link is the stable home
        /// anchor for the outfit's people and machines.</summary>
        public Vector3 Entry;
        public PedLink EntryLink;
        public float EntryT;
        public Vector3 Outside => EntryLink != null ? Entry : Door;

        /// <summary>The facade normal, toward the street - which way the storefront
        /// faces. Boards go up ACROSS this, fire licks up in front of it (ShopDamage).</summary>
        public Vector3 Outward = Vector3.forward;

        /// <summary>Bombed and now a burnt-out shell being boarded up (ShopDamage) - so
        /// a shop is not set alight twice, and the map or a card could say so.</summary>
        public bool Damaged;

        /// <summary>The fire has burnt out and the storefront is boarded over (ShopDamage).</summary>
        public bool Boarded;

        public void Bind(int gangId, string gangName,
                         LivingCity.Gangs.FrontDossier books, Vector3 door)
        {
            GangId = gangId;
            GangName = gangName;
            Books = books;
            Door = door;
            Entry = door;
            EntryLink = null;
            EntryT = 0f;
        }

        public void Bind(int gangId, string gangName,
                         LivingCity.Gangs.FrontDossier books, Vector3 door, Vector3 outward)
        {
            Bind(gangId, gangName, books, door);
            if (outward.sqrMagnitude > 1e-4f) Outward = outward.normalized;
        }

        public void Bind(int gangId, string gangName,
                         LivingCity.Gangs.FrontDossier books, Vector3 door,
                         Vector3 entry, PedLink entryLink, float entryT, Vector3 outward)
        {
            Bind(gangId, gangName, books, door, outward);
            if (entryLink == null) return;
            Entry = entry;
            EntryLink = entryLink;
            EntryT = Mathf.Clamp(entryT, 0f, entryLink.Length);
        }

        /// <summary>The front this transform belongs to, if any - a click lands on a
        /// sub-building or a collider child, and the marker sits on the bake root.</summary>
        public static GangFront Of(Transform t) =>
            t == null ? null : t.GetComponentInParent<GangFront>();

        void Awake() => all.Add(this);

        void OnDestroy() => all.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => all.Clear();
    }
}
