namespace LivingCity.Gangs
{
    /// <summary>
    /// The fixed facts of the five gangs: who exists, what they are called, and what
    /// their people wear. Engine-free tables - the headless suite asserts they stay
    /// aligned and inside the popup budgets.
    ///
    /// Model names resolve by NAME against the cast baked into LedgerModelSet, which
    /// accepts the "_AI" suffix the retired crowd prefabs carried - so an entry here
    /// only has to be on LedgerArtBootstrap's people list to reach the street. WHICH
    /// bodies may be named at all is <see cref="GangLooks"/>' decision, not this
    /// table's; this one only says which of them each mob leads with. The lieutenant
    /// always wears a different model from his soldiers, so rank reads at a glance.
    /// </summary>
    public static class GangCatalog
    {
        public const int GangCount = 5;
        public const int PlayerGangId = 0;

        /// <summary>The player's boss - the main character. The ledger's front card
        /// wears his face and name; nothing else names him yet, so this is the one
        /// source when the story layer arrives.</summary>
        public const string BossName = "Don Salvatore Ricci";

        /// <summary>The boss's street model - the rich suit, reserved for him (the
        /// mugshot tables deliberately leave it out of the lieutenant looks).</summary>
        public const string BossModel = "SM_Chr_Rich_Male_01_AI";

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

        /// <summary>Each gang's staple soldier - the body its crew leads with. Indexed by
        /// gang id, and every entry drawn from the approved hood stock
        /// (<see cref="GangLooks.Hoods"/>): a mob's muscle may only wear what a gangster
        /// is allowed to wear. The REST of a rival crew is dealt out of that same stock
        /// by GangLooks.HoodsFor, so no two men on one pavement are the same man.</summary>
        public static readonly string[] SoldierModels =
        {
            "SM_Chr_Gang_Male_01_AI",      // The Outfit - the classic crew
            "SM_Chr_Gang_Male_02_AI",      // Falcone
            "SM_Chr_Goon_01_AI",           // Santoro - heavy muscle
            "SM_Chr_Criminal_Male_01_AI",  // Lucchese - street crew
            "SM_Chr_GangMember_Male_01_AI",// DeMarco - working outfit
        };

        /// <summary>Indexed by gang id, out of the approved capo stock
        /// (<see cref="GangLooks.Lieutenants"/>) and never the model that gang's soldiers
        /// wear - rank has to read across a street.</summary>
        public static readonly string[] LieutenantModels =
        {
            "SM_Chr_Italian_Gangster_01_AI",
            "SM_Chr_Kingpin_01_AI",
            "SM_Gen_Chr_Business_Male_01_AI",
            "SM_Chr_Goon_01_AI",
            "SM_Chr_Criminal_Male_01_AI",
        };
    }
}
