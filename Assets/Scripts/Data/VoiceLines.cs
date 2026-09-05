namespace LivingCity.Data
{
    /// <summary>
    /// Every key the game can ask for by name. A key is what is SAID, never who says it -
    /// the actor comes off the man (see VoiceCasting), and the take is picked inside
    /// CrewVoice. Nothing outside this file spells a key as a literal, so a line that is
    /// re-cut or re-named breaks the compile rather than going quietly silent.
    ///
    /// The sheet itself - the English of every take, who speaks it and what it costs to
    /// record - is Docs/voice-lines.md.
    /// </summary>
    public static class VoiceLines
    {
        // ------------------------------------------------------------------ selection
        public const string SelReady = "VOX_SEL_READY";
        public const string SelCar = "VOX_SEL_CAR";
        public const string SelInside = "VOX_SEL_INSIDE";
        public const string SelHurt = "VOX_SEL_HURT";
        public const string SelRound = "VOX_SEL_ROUND";
        public const string SelFew = "VOX_SEL_FEW";

        // -------------------------------------------------------------- street orders
        public const string OrdMove = "VOX_ORD_MOVE";
        public const string OrdRun = "VOX_ORD_RUN";
        public const string OrdCover = "VOX_ORD_COVER";
        public const string OrdKill = "VOX_ORD_KILL";
        public const string OrdDriveBy = "VOX_ORD_DRIVEBY";
        public const string OrdGrenade = "VOX_ORD_GRENADE";
        public const string OrdDoorBomb = "VOX_ORD_DOORBOMB";
        public const string OrdCarBomb = "VOX_ORD_CARBOMB";
        public const string OrdShootCar = "VOX_ORD_SHOOTCAR";
        public const string OrdBoard = "VOX_ORD_BOARD";
        public const string OrdOut = "VOX_ORD_OUT";
        public const string OrdFlee = "VOX_ORD_FLEE";
        public const string OrdAway = "VOX_ORD_AWAY";
        public const string OrdBag = "VOX_ORD_BAG";
        public const string OrdWitness = "VOX_ORD_WITNESS";
        public const string OrdInside = "VOX_ORD_INSIDE";
        public const string OrdOutside = "VOX_ORD_OUTSIDE";

        // ------------------------------------------------------------- the racket door
        public const string RktApproach = "VOX_RKT_APPROACH";
        public const string RktDemand = "VOX_RKT_DEMAND";
        public const string RktThreat = "VOX_RKT_THREAT";
        public const string RktCollect = "VOX_RKT_COLLECT";
        public const string RktShake = "VOX_RKT_SHAKE";
        public const string RktHoldout = "VOX_RKT_HOLDOUT";
        public const string RktSmash = "VOX_RKT_SMASH";
        public const string RktTorch = "VOX_RKT_TORCH";
        public const string RktRob = "VOX_RKT_ROB";
        public const string RktGuard = "VOX_RKT_GUARD";
        public const string RktBuy = "VOX_RKT_BUY";
        public const string RktRepair = "VOX_RKT_REPAIR";

        /// <summary>The two hideout lines mean opposite things, so they are two keys and
        /// not two takes of one - the baker knows it as well (VoiceAssetBootstrap.Split).
        /// </summary>
        public const string RktHideoutOn = "VOX_RKT_HIDEOUT_01";
        public const string RktHideoutOff = "VOX_RKT_HIDEOUT_02";

        // --------------------------------------------------------------- the refusals
        public const string NoGeneric = "VOX_NO_GENERIC";
        public const string NoArms = "VOX_NO_ARMS";
        public const string NoReach = "VOX_NO_REACH";
        public const string NoMen = "VOX_NO_MEN";

        // -------------------------------------------------------------------- the fight
        //
        // Everything here fires off something the street did - a bullet lands, a man goes
        // down, the law turns the corner - so no line of it is atmosphere on a timer.

        /// <summary>Shot and still standing.</summary>
        public const string HitTake = "VOX_HIT_TAKE";

        /// <summary>Shot, and one more would put him down.</summary>
        public const string HitBad = "VOX_HIT_BAD";

        /// <summary>The hit that kills him. The one line nothing may talk over.</summary>
        public const string DownCry = "VOX_DOWN_CRY";

        /// <summary>Firing - the curses and the taunts, thinned hard.</summary>
        public const string FightCurse = "VOX_FIGHT_CURSE";

        /// <summary>The first sight of a man to shoot at.</summary>
        public const string SpotContact = "VOX_SPOT_CONTACT";

        /// <summary>He put a rival down.</summary>
        public const string DropGot = "VOX_DROP_GOT";

        /// <summary>One of his own has fallen.</summary>
        public const string LossMan = "VOX_LOSS_MAN";

        /// <summary>Hit while holding cover - the fire is on top of them.</summary>
        public const string PinHeld = "VOX_PIN_HELD";

        /// <summary>Something is coming: a grenade on the pavement, a car at the kerb.</summary>
        public const string WarnCall = "VOX_WARN_CALL";

        /// <summary>Hands up to the law.</summary>
        public const string SurrHands = "VOX_SURR_HANDS";

        /// <summary>The law has arrived on the fight.</summary>
        public const string LawHeat = "VOX_LAW_HEAT";

        /// <summary>The last rival on the street is down.</summary>
        public const string WinOver = "VOX_WIN_OVER";

        // ---------------------------------------------------------- filed at the desk
        //
        // One key per OrderType, spoken by the office bank: the order is filed at a desk
        // and nobody is on the street yet, so it is the consigliere who answers.
        public static string ForOrder(Outfit.OrderType type) => type switch
        {
            Outfit.OrderType.Extort => "VOX_JOB_EXTORT",
            Outfit.OrderType.Intimidate => "VOX_JOB_INTIMIDATE",
            Outfit.OrderType.CollectProtection => "VOX_JOB_COLLECTPROTECTION",
            Outfit.OrderType.AdjustProtection => "VOX_JOB_ADJUSTPROTECTION",
            Outfit.OrderType.Assault => "VOX_JOB_ASSAULT",
            Outfit.OrderType.SmashUp => "VOX_JOB_SMASHUP",
            Outfit.OrderType.Raid => "VOX_JOB_RAID",
            Outfit.OrderType.Torch => "VOX_JOB_TORCH",
            Outfit.OrderType.Bomb => "VOX_JOB_BOMB",
            Outfit.OrderType.Kill => "VOX_JOB_KILL",
            Outfit.OrderType.Kidnap => "VOX_JOB_KIDNAP",
            Outfit.OrderType.Patrol => "VOX_JOB_PATROL",
            Outfit.OrderType.Guard => "VOX_JOB_GUARD",
            Outfit.OrderType.Ambush => "VOX_JOB_AMBUSH",
            Outfit.OrderType.Explore => "VOX_JOB_EXPLORE",
            Outfit.OrderType.BuyPremises => "VOX_JOB_BUYPREMISES",
            Outfit.OrderType.SetUpBusiness => "VOX_JOB_SETUPBUSINESS",
            Outfit.OrderType.RunBusiness => "VOX_JOB_RUNBUSINESS",
            Outfit.OrderType.Audit => "VOX_JOB_AUDIT",
            Outfit.OrderType.Recruit => "VOX_JOB_RECRUIT",
            Outfit.OrderType.Bribe => "VOX_JOB_BRIBE",
            Outfit.OrderType.EmployPolice => "VOX_JOB_EMPLOYPOLICE",
            Outfit.OrderType.Donate => "VOX_JOB_DONATE",
            Outfit.OrderType.Beating => OrdWitness,
            Outfit.OrderType.KillOwner => OrdKill,
            // The connection's two jobs are given off a card, never from the office.
            Outfit.OrderType.Meet => "VOX_JOB_BRIBE",
            Outfit.OrderType.TestBuy => "VOX_JOB_BRIBE",
            // The sit-down is given from THE TABLE, never from the office (EPIC 42).
            Outfit.OrderType.SitDown => "VOX_JOB_BRIBE",
            _ => throw new System.ArgumentOutOfRangeException(nameof(type), type,
                "Every order needs an explicit office voice decision."),
        };
    }
}
