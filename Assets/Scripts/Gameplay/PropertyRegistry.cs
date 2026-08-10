using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>What kind of establishment a business is. Read by the popup words now; the
    /// future action layer gates on it (a bomb suits a warehouse, a racket suits a cafe).</summary>
    public enum BusinessCategory
    {
        Industrial,
        Commercial,
        Port,
    }

    /// <summary>
    /// A gazda. Plain data, not a component: owners are not places or agents, they are names
    /// that businesses point at, and several dozen businesses point at the same five of them -
    /// a district boss owning whole blocks reads more gangster than a unique stranger per door,
    /// and it is the seed of faction play later.
    ///
    /// Index is the owner's stable position in the registry pool - cheap enough for a popup
    /// change key, which is exactly what BusinessMarker packs it into.
    /// </summary>
    public sealed class PropertyOwner
    {
        public PropertyOwner(int index, string displayName, bool civic)
        {
            Index = index;
            DisplayName = displayName;
            Civic = civic;
        }

        public int Index { get; }
        public string DisplayName { get; }

        /// <summary>State property - the post office. Future actions can refuse to racket
        /// what the city itself runs (or charge extra for the nerve).</summary>
        public bool Civic { get; }
    }

    /// <summary>
    /// Every owner and every business in the city, the OverlayRegistry pattern exactly: static,
    /// self-service, Version bumped on every change (not a count - a swap must be visible), and
    /// reset for domain-reload-off Play. The action layer that racket/buy will need enumerates
    /// businesses from here rather than walking the hierarchy.
    /// </summary>
    public static class PropertyRegistry
    {
        static readonly List<PropertyOwner> owners = new List<PropertyOwner>();
        static readonly List<Entities.BusinessMarker> businesses =
            new List<Entities.BusinessMarker>();

        public static IReadOnlyList<PropertyOwner> Owners => owners;
        public static IReadOnlyList<Entities.BusinessMarker> Businesses => businesses;

        /// <summary>Bumped on every add and every remove, owners and businesses alike.</summary>
        public static int Version { get; private set; }

        public static PropertyOwner AddOwner(string displayName, bool civic = false)
        {
            var owner = new PropertyOwner(owners.Count, displayName, civic);
            owners.Add(owner);
            Version++;
            return owner;
        }

        public static void Register(Entities.BusinessMarker business)
        {
            if (business == null)
                return;

            businesses.Add(business);
            Version++;
        }

        public static void Unregister(Entities.BusinessMarker business)
        {
            if (business != null && businesses.Remove(business))
                Version++;
        }

        /// <summary>Static state outlives Play when domain reload is off - OverlayRegistry's
        /// reason, closed the same way.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            owners.Clear();
            businesses.Clear();
            Version = 0;
        }
    }
}
