namespace LivingCity.Entities
{
    /// <summary>
    /// Who has no business on a pavement. The crowd pools scan whole Synty character
    /// folders (RoadDemoBuilder, CrewDemoBuilder) because a hand-list of forty bodies
    /// would rot the first time a pack was imported - but a folder scan takes the
    /// costumes with the people, and a costume in the crowd is read instantly.
    ///
    /// Three separate questions decide who walks past the player, and they live apart
    /// on purpose:
    ///   - may this body be a gangster?  <see cref="Gangs.GangLooks.IsGangBody"/>
    ///   - is this body on the force?    the officer prefix, at the scan
    ///   - has this body any business on a 1987 city pavement at all?  HERE.
    ///
    /// Engine-free like the rest of the Entities core, so the headless suite holds the
    /// table rather than the player meeting a man in a prison jumpsuit downtown.
    /// </summary>
    public static class CrowdLooks
    {
        /// <summary>Bodies the crowd never wears, and why. Every one of these is a
        /// person somewhere - just not a passer-by on a city street in 1987 - so this is
        /// a filter on the CROWD and not a bar on the prefab: a scene that wants one by
        /// name (the harbor's sea captain, a crime scene's technician) still gets it.
        ///
        ///   Prisoner_*   a man in a prison jumpsuit, walking free downtown
        ///   Forensic_01  a scene-suit technician; he belongs inside the tape, with the
        ///                law, not strolling a boulevard four blocks away
        ///   Peasent_*    the generic pack's rural/period dress - it is shared stock for
        ///                Synty's other settings and reads as a costume in a 1987 city
        ///   SeaCaptain_* a peaked cap and full merchant uniform, thirty streets inland.
        ///                The harbor names this body itself (HarborKit.SeaCaptain), which
        ///                is where a captain in uniform makes sense
        ///   *_Police     the city pack's own two coppers. THE FORCE IS ONE UNIFORM -
        ///                the police station pack, the same way the patrols are one fleet
        ///                (VehicleCatalog.PoliceCars) - so these are neither crowd nor
        ///                beat; they are a second uniform and belong to neither
        /// </summary>
        public static readonly string[] Barred =
        {
            "SM_Gen_Chr_Prisoner_Male_01",
            "SM_Gen_Chr_Prisoner_Female_01",
            "SM_Chr_Forensic_01",
            "SM_Gen_Chr_Peasent_Male_01",
            "SM_Gen_Chr_Peasent_Female_01",
            "SM_Chr_SeaCaptain_Male_01",
            "SM_Chr_SeaCaptain_Female_01",
            "Character_Male_Police",
            "Character_Female_Police",
        };

        /// <summary>Whether the crowd may wear this body - by prefab name or by asset
        /// path. Tolerates the retired "_AI" suffix, the way the cast tables do, so one
        /// body cannot slip through looking like two.</summary>
        public static bool IsBarred(string nameOrPath)
        {
            var name = Bare(FileName(nameOrPath));
            if (string.IsNullOrEmpty(name))
                return false;

            foreach (var barred in Barred)
                if (name == barred)
                    return true;
            return false;
        }

        static string Bare(string name) =>
            !string.IsNullOrEmpty(name) && name.EndsWith("_AI")
                ? name.Substring(0, name.Length - 3)
                : name;

        /// <summary>The prefab name out of an asset path, with the extension off; a bare
        /// name comes back as it went in.</summary>
        static string FileName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            var cut = path.LastIndexOfAny(new[] { '/', '\\' });
            var name = cut >= 0 ? path.Substring(cut + 1) : path;
            return name.EndsWith(".prefab") ? name.Substring(0, name.Length - 7) : name;
        }
    }
}
