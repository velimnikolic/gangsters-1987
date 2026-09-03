namespace LivingCity.Gangs
{
    /// <summary>
    /// The fixed facts of the city's gangs: who exists, what they are called, and what
    /// their people wear. Engine-free tables - the headless suite asserts they stay
    /// aligned and inside the popup budgets.
    ///
    /// TWENTY families and the player's outfit: the stable catalogue the largest city can
    /// draw from. A particular city deals only the leading slice it can stand
    /// (<see cref="LivingCity.Outfit.Underworld.Dealt"/>), and GangSeeder mirrors exactly
    /// that slice. The table stays full
    /// sized so saved ids, stances and colours never move; its length is a capacity, not a
    /// promise that every name exists in every city.
    ///
    /// Model names resolve by NAME against the cast baked into LedgerModelSet, which
    /// accepts the "_AI" suffix the retired crowd prefabs carried - so an entry here
    /// only has to be on LedgerArtBootstrap's people list to reach the street. WHICH
    /// bodies may be named at all is <see cref="GangLooks"/>' decision, not this
    /// table's; this one only says which of them each mob leads with. The lieutenant
    /// always wears a different model from his soldiers, so rank reads at a glance.
    ///
    /// With twenty-one mobs and ten male hood bodies the STAPLES repeat - two families
    /// in ten lead with the same coat. What keeps two crews apart on one street is the
    /// pairing (no two ids share both bodies), the family colour
    /// (<see cref="UI.GangPalette"/>) and the name over the man's head; the rest of a
    /// crew is dealt around its staple by GangLooks.HoodsFor, so no crew is one man
    /// standing four times.
    /// </summary>
    public static class GangCatalog
    {
        /// <summary>The player plus twenty rivals. Every table below is this long, and
        /// the suite fails the moment one is not.</summary>
        public const int GangCount = 21;

        public const int PlayerGangId = 0;

        /// <summary>The player's boss - the main character. The ledger's front card
        /// wears his face and name; nothing else names him yet, so this is the one
        /// source when the story layer arrives.</summary>
        public const string BossName = "Don Salvatore Ricci";

        /// <summary>The boss's street model - the rich suit, reserved for him (the
        /// mugshot tables deliberately leave it out of the lieutenant looks).</summary>
        public const string BossModel = "SM_Chr_Rich_Male_01_AI";

        /// <summary>Names are budgeted: they stand where "Protected" stands in the
        /// business popup line (see BusinessIntention.Line's gang overload), and over a
        /// rival's head in the street overlay. Surnames only - the wording around them
        /// ("the Falcone family") is the caller's, and a crew name would read wrong in
        /// it. The first four are the original families; ids never move, because a
        /// saved campaign, a stance and a map colour all hang off the id.</summary>
        public static readonly string[] Names =
        {
            "The Outfit",   // 0 - the player's organisation
            "Falcone",      // 1
            "Santoro",      // 2
            "Lucchese",     // 3
            "DeMarco",      // 4
            "Corvetti",     // 5
            "Barzini",      // 6
            "Moretti",      // 7
            "Rinaldi",      // 8
            "Castellano",   // 9
            "Vitelli",      // 10
            "Marchetti",    // 11
            "Greco",        // 12
            "Ferraro",      // 13
            "Serrano",      // 14
            "Delgado",      // 15
            "O'Shea",       // 16 - the Irish end of the docks
            "Doyle",        // 17
            "Kowalski",     // 18 - the packing houses
            "Volkov",       // 19
            "Petrov",       // 20
        };

        /// <summary>Each gang's staple soldier - the body its crew leads with. Indexed by
        /// gang id, and every entry drawn from the approved hood stock
        /// (<see cref="GangLooks.Hoods"/>): a mob's muscle may only wear what a gangster
        /// is allowed to wear. The REST of a rival crew is dealt out of that same stock
        /// by GangLooks.HoodsFor, so no two men on one pavement are the same man.
        ///
        /// Ids 5 and up walk the male half of the stock in order, so neighbouring
        /// families never lead with the same coat.</summary>
        public static readonly string[] SoldierModels =
        {
            "SM_Chr_Gang_Male_01_AI",      // 0  The Outfit - the classic crew
            "SM_Chr_Gang_Male_02_AI",      // 1  Falcone
            "SM_Chr_Goon_01_AI",           // 2  Santoro - heavy muscle
            "SM_Chr_Criminal_Male_01_AI",  // 3  Lucchese - street crew
            "SM_Chr_GangMember_Male_01_AI",// 4  DeMarco - working outfit
            "SM_Gen_Chr_Street_Male_01_AI",// 5  Corvetti
            "SM_Chr_GangMember_Male_03_AI",// 6  Barzini
            "SM_Chr_Goon_01_AI",           // 7  Moretti
            "SM_Chr_Bouncer_Male_01_AI",   // 8  Rinaldi - doormen
            "SM_Chr_Salesman_01_AI",       // 9  Castellano - the respectable front
            "SM_Chr_GangMember_Male_01_AI",// 10 Vitelli
            "SM_Chr_Gang_Male_01_AI",      // 11 Marchetti
            "SM_Chr_Criminal_Male_01_AI",  // 12 Greco
            "SM_Chr_GangMember_Male_02_AI",// 13 Ferraro
            "SM_Chr_Gang_Male_02_AI",      // 14 Serrano
            "SM_Gen_Chr_Street_Male_01_AI",// 15 Delgado
            "SM_Chr_GangMember_Male_03_AI",// 16 O'Shea
            "SM_Chr_Goon_01_AI",           // 17 Doyle
            "SM_Chr_Bouncer_Male_01_AI",   // 18 Kowalski
            "SM_Chr_Salesman_01_AI",       // 19 Volkov
            "SM_Chr_GangMember_Male_01_AI",// 20 Petrov
        };

        /// <summary>Indexed by gang id, out of the approved capo stock
        /// (<see cref="GangLooks.Lieutenants"/>) and never the model that gang's soldiers
        /// wear - rank has to read across a street. Six looks over twenty-one mobs, so
        /// the table cycles on a different period from the staples above: no two
        /// families are the same PAIR of bodies.</summary>
        public static readonly string[] LieutenantModels =
        {
            "SM_Chr_Italian_Gangster_01_AI",  // 0  The Outfit
            "SM_Chr_Goon_01_AI",              // 1  Falcone
            "SM_Gen_Chr_Business_Male_01_AI", // 2  Santoro
            "SM_Chr_Goon_01_AI",              // 3  Lucchese
            "SM_Chr_Criminal_Male_01_AI",     // 4  DeMarco
            "SM_Chr_Goon_01_AI",              // 5  Corvetti
            "SM_Chr_Criminal_Male_01_AI",     // 6  Barzini
            "SM_Chr_Italian_Gangster_01_AI",  // 7  Moretti
            "SM_Chr_Italian_Gangster_01_AI",  // 8  Rinaldi
            "SM_Gen_Chr_Business_Male_01_AI", // 9  Castellano
            "Character_BusinessMan_Suit",     // 10 Vitelli
            "SM_Chr_GangBoss_01_AI",          // 11 Marchetti
            "SM_Chr_GangBoss_01_AI",          // 12 Greco
            "SM_Chr_Criminal_Male_01_AI",     // 13 Ferraro
            "SM_Chr_Italian_Gangster_01_AI",  // 14 Serrano
            "Character_BusinessMan_Suit",     // 15 Delgado
            "SM_Gen_Chr_Business_Male_01_AI", // 16 O'Shea
            "Character_BusinessMan_Suit",     // 17 Doyle
            "SM_Chr_GangBoss_01_AI",          // 18 Kowalski
            "SM_Chr_Goon_01_AI",              // 19 Volkov
            "SM_Gen_Chr_Business_Male_01_AI", // 20 Petrov
        };
    }
}
