using System.Collections.Generic;
using LivingCity.Territory;
using UnityEngine;

namespace LivingCity.Business
{
    /// <summary>
    /// The deed book: which GANG owns a premises outright. Simulation state keyed by the
    /// canonical business id, so a deed survives its street being streamed out and back -
    /// the BusinessMarker on the building is a VIEW of this and never the record. A buy
    /// that only flipped a marker was undone by the recycler the moment the camera left.
    ///
    /// -1 is the honest majority: no gang holds the paper. Nothing here creates or names
    /// a business - a deed can only be written against an id the directory already deals.
    /// </summary>
    public static class BusinessDeeds
    {
        public readonly struct Deed
        {
            public Deed(int gangId, int legacyBlockId)
            {
                GangId = gangId;
                LegacyBlockId = legacyBlockId;
            }

            public int GangId { get; }

            /// <summary>The integer block the premises stands on, captured when the deed
            /// is written - the holdings sweep wants (gang, block) pairs without a
            /// geography lookup per frame.</summary>
            public int LegacyBlockId { get; }
        }

        static readonly Dictionary<TerritoryBusinessId, Deed> deeds =
            new Dictionary<TerritoryBusinessId, Deed>();

        public static int Version { get; private set; }

        /// <summary>Raised after a deed changes hands. Carries the id and the new gang.</summary>
        public static event System.Action<TerritoryBusinessId, int> Changed;

        /// <summary>Writes the deed and restamps the live view if one is bound, so the
        /// street and the book change in the same frame.</summary>
        public static void SetGang(
            TerritoryBusinessId businessId, int gangId, int legacyBlockId)
        {
            if (!businessId.IsValid)
                return;
            deeds[businessId] = new Deed(gangId, legacyBlockId);
            Version++;
            if (BusinessViewBindings.TryGet(businessId, out var marker))
                marker.GangId = gangId;
            Changed?.Invoke(businessId, gangId);
        }

        /// <summary>The gang on the deed, or -1 - the marker default, so a rebind can
        /// take this answer without asking whether an entry exists.</summary>
        public static int GangOf(TerritoryBusinessId businessId) =>
            businessId.IsValid && deeds.TryGetValue(businessId, out var deed)
                ? deed.GangId
                : -1;

        public static bool TryGet(TerritoryBusinessId businessId, out Deed deed) =>
            deeds.TryGetValue(businessId, out deed);

        /// <summary>Every written deed, for the holdings sweep. Order is unspecified.</summary>
        public static void Collect(
            List<KeyValuePair<TerritoryBusinessId, Deed>> into)
        {
            if (into == null)
                return;
            into.Clear();
            foreach (var pair in deeds)
                into.Add(pair);
        }

        /// <summary>Statics outlive Play with domain reload off - the
        /// BusinessViewBindings discipline, closed the same way.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            deeds.Clear();
            Changed = null;
            Version++;
        }
    }
}
