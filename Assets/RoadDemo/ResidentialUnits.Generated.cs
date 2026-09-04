using System;
using System.Collections.Generic;

namespace RoadDemo
{
    /// <summary>
    /// Runtime-only units made by the residential forge. The harvested catalogue remains
    /// <see cref="All"/>; generated units are visible only to readers which need to recognise
    /// a shell that is already standing. In particular, this does not offer forged buildings
    /// to the residential lot dealer.
    /// </summary>
    public static partial class ResidentialUnits
    {
        static readonly List<ResidentialUnit> GeneratedUnits = new List<ResidentialUnit>();

        public static IReadOnlyList<ResidentialUnit> Generated => GeneratedUnits;

        public static IEnumerable<ResidentialUnit> Known
        {
            get
            {
                for (int i = 0; i < All.Length; i++) yield return All[i];
                for (int i = 0; i < GeneratedUnits.Count; i++) yield return GeneratedUnits[i];
            }
        }

        /// <summary>Remember one deterministic forge signature without growing duplicates.</summary>
        internal static ResidentialUnit RememberGenerated(ResidentialUnit unit)
        {
            if (unit == null || string.IsNullOrEmpty(unit.Name))
                throw new ArgumentException("A generated residential unit needs a name.", nameof(unit));

            for (int i = 0; i < GeneratedUnits.Count; i++)
            {
                if (!string.Equals(GeneratedUnits[i].Name, unit.Name, StringComparison.Ordinal))
                    continue;
                GeneratedUnits[i] = unit;
                return unit;
            }

            GeneratedUnits.Add(unit);
            return unit;
        }

        /// <summary>
        /// Forget a forged unit whose standing shell has been removed. This is deliberately
        /// by deterministic signature rather than by object reference: a refreshed sheet
        /// replaces its unit instance under the same name.
        /// </summary>
        public static bool ForgetGenerated(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = GeneratedUnits.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(GeneratedUnits[i].Name, name, StringComparison.Ordinal))
                    continue;
                GeneratedUnits.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>Forget only the named forged units, leaving other live sheets known.</summary>
        public static int ForgetGenerated(IEnumerable<string> names)
        {
            if (names == null) return 0;
            int removed = 0;
            foreach (var name in names)
                if (ForgetGenerated(name)) removed++;
            return removed;
        }
    }
}
