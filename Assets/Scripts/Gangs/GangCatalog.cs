namespace LivingCity.Gangs
{
    /// <summary>
    /// The fixed facts of the five gangs: who exists, what they are called, and what
    /// their people wear. Engine-free tables - the headless suite asserts they stay
    /// aligned and inside the popup budgets.
    ///
    /// Model names resolve against the shipped PrefabDatabase.pedestrianGroups by NAME
    /// ONLY (GangDirector's read-only scan) - every entry below is already wired into
    /// the groups, so no AuthorPedestrians menu re-run is needed. The lieutenant always
    /// wears a different model from his soldiers, so rank reads at a glance.
    /// </summary>
    public static class GangCatalog
    {
        public const int GangCount = 5;
        public const int PlayerGangId = 0;

        /// <summary>Names are budgeted: they stand where "Protected" stands in the
        /// business popup line (see BusinessIntention.Line's gang overload).</summary>
        public static readonly string[] Names =
        {
            "The Outfit",   // 0 - the player's organisation
            "Falcone",
            "Santoro",
            "Lucchese",
            "DeMarco",
        };

        /// <summary>Indexed by gang id.</summary>
        public static readonly string[] SoldierModels =
        {
            "SM_Chr_Gang_Male_01_AI",      // The Outfit - the classic crew
            "SM_Chr_Gang_Male_02_AI",      // Falcone
            "SM_Chr_Goon_01_AI",           // Santoro - heavy muscle
            "SM_Chr_Criminal_Male_01_AI",  // Lucchese - street crew
            "SM_Gen_Chr_Street_Male_02_AI",// DeMarco - working outfit
        };

        /// <summary>Indexed by gang id.</summary>
        public static readonly string[] LieutenantModels =
        {
            "SM_Chr_Detective_Male_01_AI",
            "SM_Chr_Gang_Male_01_AI",
            "SM_Gen_Chr_Business_Male_01_AI",
            "SM_Chr_Goon_01_AI",
            "SM_Chr_City_Male_01_AI",
        };
    }
}
