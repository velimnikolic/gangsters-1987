using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Gangs
{
    /// <summary>
    /// Every gang in the city and which door each one operates behind - the
    /// PropertyRegistry pattern exactly: static, Version bumped on every change, reset
    /// for domain-reload-off Play. Consumers that only want words (BusinessMarker's
    /// popup line, GangIntention) read from here without a scene find.
    /// </summary>
    public static class GangRegistry
    {
        static readonly List<Gang> gangs = new List<Gang>();
        static readonly Dictionary<int, Entities.BusinessMarker> frontBusinesses =
            new Dictionary<int, Entities.BusinessMarker>();

        public static IReadOnlyList<Gang> Gangs => gangs;

        public static int Version { get; private set; }

        public static void Install(IReadOnlyList<Gang> installed)
        {
            gangs.Clear();
            if (installed != null)
                gangs.AddRange(installed);
            Version++;
        }

        public static void SetFrontBusiness(int gangId, Entities.BusinessMarker marker)
        {
            frontBusinesses[gangId] = marker;
            Version++;
        }

        public static Entities.BusinessMarker FrontBusinessOf(int gangId) =>
            frontBusinesses.TryGetValue(gangId, out var marker) ? marker : null;

        /// <summary>Empty for -1/unknown - callers feed it straight into the popup
        /// line, where empty means "no gang clause".</summary>
        public static string NameOf(int gangId)
        {
            foreach (var gang in gangs)
                if (gang.Id == gangId)
                    return gang.Name;

            return "";
        }

        /// <summary>Static state outlives Play when domain reload is off -
        /// OverlayRegistry's reason, closed the same way.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            gangs.Clear();
            frontBusinesses.Clear();
            Version = 0;
        }
    }
}
