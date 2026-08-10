using System.Collections.Generic;

namespace LivingCity.Gangs
{
    /// <summary>
    /// One member of a gang - a name and a role, nothing more yet. Pure data, free of
    /// UnityEngine (the Personnel discipline), so the headless suite can build gangs
    /// without an Editor. For the player's gang PersonnelId ties the man to his ledger
    /// Character; AI members carry -1 and exist only as the crowd sees them.
    /// </summary>
    public sealed class GangMemberIdentity
    {
        public string FirstName = "";
        public string Surname = "";
        public bool Lieutenant;

        /// <summary>Personnel.Character.Id for the player's materialized roster; -1 for
        /// AI gangs. The binding is by id on purpose - a future ledger watcher can
        /// re-resolve the man without respawning him.</summary>
        public int PersonnelId = -1;

        public string FullName => FirstName + " " + Surname;
    }

    /// <summary>
    /// One gang: the player's outfit or an AI family. Holds no scene references and no
    /// colour - the front BusinessMarker binding lives in GangRegistry and the colour in
    /// UI.GangPalette - so this class compiles and tests in the bare .NET host.
    /// </summary>
    public sealed class Gang
    {
        public int Id;
        public string Name = "";
        public bool IsPlayer;

        /// <summary>The rng draw the player's front pick consumes (AI fronts are derived
        /// spatially, no draws - see GangFronts).</summary>
        public int FrontRoll;

        /// <summary>Per-gang child seed, drawn up front from the master stream - the
        /// PortDirector idiom. Everything this gang's agents ever roll comes from here,
        /// so deepening one gang can never reshuffle another.</summary>
        public int MemberSeed;

        /// <summary>Lieutenant first - slot 0 outside the front is his.</summary>
        public readonly List<GangMemberIdentity> Members = new List<GangMemberIdentity>();
    }
}
