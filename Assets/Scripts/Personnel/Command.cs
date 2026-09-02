namespace LivingCity.Personnel
{
    /// <summary>
    /// How many men one man can actually hold, and how much ground. The one place a
    /// cap is worked out - the ledger, the assignment rules and the diagnostics all
    /// read it, and no call site is allowed its own arithmetic.
    ///
    /// This is the engine of the whole design. An outfit that grows past what its
    /// commanders can carry has to promote somebody, and promoting somebody means
    /// trusting a man you may not be able to trust. Nine blocks needs three
    /// lieutenants; fifty men needs a lieutenant who can hold fifty, and most men
    /// cannot hold a dozen.
    ///
    /// Pure and free of UnityEngine, like the rest of Personnel.
    /// </summary>
    public static class Command
    {
        /// <summary>Nobody is so hopeless that he cannot be given a couple of men - a
        /// cap of zero would make a promoted hood useless the moment he was promoted,
        /// which is not a decision the player should be able to make by accident.</summary>
        public const int FloorMen = 4;

        /// <summary>
        /// The men a leader can hold: the config's ceiling for his rank, approached
        /// along a square curve in his Leadership.
        ///
        /// Square rather than straight on purpose. The design asks that Leadership 25
        /// command a handful and Leadership 90 approach the ceiling, and a straight
        /// ramp gives the poor commander a third of the ceiling - which is not a
        /// handful, it is a crew. Measured against a fifty-man ceiling: Leadership 25
        /// holds 6, 50 holds 15, 75 holds 29, 90 holds 41, 100 holds 50.
        ///
        /// Integer arithmetic throughout - the sim has no floats in it and a cap that
        /// rounded differently on two machines would be a desync waiting to happen.
        /// </summary>
        public static int ManCap(Character leader, in OrganizationLimits limits)
        {
            if (leader == null)
                return 0;

            var ceiling = leader.Rank == Rank.Boss
                ? limits.BossManpower
                : limits.LieutenantManpower;
            if (ceiling <= FloorMen)
                return ceiling;

            var leadership = AttributeScale.ValueOf(
                leader.GetHalfSteps(CharacterAttribute.Leadership));

            return FloorMen + (ceiling - FloorMen) * leadership * leadership / 10_000;
        }

        /// <summary>
        /// The ground he can be held responsible for. Flat for now - the design says
        /// three blocks to a lieutenant and does not tie it to a skill, and inventing
        /// a tie would be inventing a rule. The ceiling is still a config lever.
        /// </summary>
        public static int BlockCap(Character leader, in OrganizationLimits limits)
        {
            if (leader == null)
                return 0;
            return leader.Rank == Rank.Boss ? limits.BossBlocks : limits.LieutenantBlocks;
        }

        /// <summary>The most lieutenants any Boss could ever hold, however good he
        /// gets. Eight branches is already an outfit nobody can keep an eye on.</summary>
        public const int MaxLieutenants = 8;

        /// <summary>A Boss can always have one man under him. An outfit with no
        /// lieutenant at all is a Boss doing every job himself, which is the opening
        /// position and not a permanent sentence.</summary>
        public const int FloorLieutenants = 1;

        /// <summary>
        /// How many branches the outfit can hold at all - the Boss's own span of
        /// control, on his Leadership AND what the street concedes him. Both, because
        /// a man commands lieutenants with the same two things he would need to command
        /// anybody: they will follow him, and they have heard of him.
        ///
        /// The same square curve the man-cap uses, for the same reason. Don Salvatore
        /// opens on 5; a Boss the street has never heard of holds one, and has to go
        /// out and do the work himself - which is exactly when he is likeliest to be
        /// killed, and that arc comes out of the arithmetic rather than a script.
        /// </summary>
        public static int LieutenantCap(Character boss)
        {
            if (boss == null || boss.Rank != Rank.Boss)
                return 0;

            var reach = (AttributeScale.ValueOf(
                             boss.GetHalfSteps(CharacterAttribute.Leadership)) +
                         AttributeScale.ValueOf(
                             boss.GetHalfSteps(CharacterAttribute.StreetAuthority))) / 2;

            return FloorLieutenants +
                   (MaxLieutenants - FloorLieutenants) * reach * reach / 10_000;
        }

        /// <summary>What a man on a block is worth under the worst commander there
        /// is, and under the best. A modest band on purpose: command quality has to
        /// matter without ever replacing headcount - five men well led should beat
        /// four, not eight.</summary>
        public const float WorstPresenceFactor = 0.8f;

        public const float BestPresenceFactor = 1.3f;

        /// <summary>
        /// How much of a man's physical presence his commander actually extracts.
        /// Organization and Leadership together: the rota that puts him on the right
        /// corner at the right hour, and the reason he is still standing there an hour
        /// later.
        ///
        /// This is the Roster track's hook into the territory war. Pull hoods off a
        /// block for a fight and Presence falls, businesses stop paying, Fear decays
        /// and control weakens - war is expensive even when no fight is lost - and a
        /// good commander is what makes the same men hold more of it.
        ///
        /// Out of scope, deliberately: the design also has Organization capping orders
        /// per week. That is a different lever and belongs to the orders layer when
        /// weekly budgets exist.
        /// </summary>
        public static float PresenceFactor(Character commander)
        {
            if (commander == null)
                return 1f;

            var quality = (AttributeScale.ValueOf(
                               commander.GetHalfSteps(CharacterAttribute.Organization)) +
                           AttributeScale.ValueOf(
                               commander.GetHalfSteps(CharacterAttribute.Leadership))) / 2;

            var floor = AttributeScale.ValueOf(AttributeScale.MinHalfSteps);
            var span = AttributeScale.ValueOf(AttributeScale.MaxHalfSteps) - floor;
            var t = (quality - floor) / (float)span;

            return WorstPresenceFactor + (BestPresenceFactor - WorstPresenceFactor) * t;
        }

        /// <summary>
        /// The factor for one man on the street, found through whoever commands him.
        /// A man nobody commands - pooled, or on nobody's books at all, which is every
        /// rival body the city walks past - stands at exactly what he is worth.
        /// </summary>
        public static float PresenceFactorFor(Roster roster, int characterId)
        {
            if (roster == null || characterId < 0)
                return 1f;

            var man = roster.Find(characterId);
            if (man == null)
                return 1f;

            // A commander extracts his own presence too: the Boss and his lieutenants
            // are one body each on the ground like anybody else.
            if (man.Rank == Rank.Boss || man.Rank == Rank.Lieutenant)
                return PresenceFactor(man);

            var crew = roster.CrewOf(characterId);
            // Whoever is RUNNING the crew today, not whose name is on it: a branch whose
            // lieutenant is inside is held together by his deputy, and holds ground like
            // the deputy (PIPE-004).
            var commander = EffectiveLieutenant(roster, crew);
            if (commander == null && roster.Organization.BossHoodIds.Contains(characterId))
                commander = roster.FindBoss();

            return commander != null ? PresenceFactor(commander) : 1f;
        }

        /// <summary>
        /// WHO IS ACTUALLY RUNNING THIS CREW TODAY (GAN-219, PIPE-004).
        ///
        /// A lieutenant in a cell is still the lieutenant: <see cref="Crew.LieutenantId"/>
        /// does not change, his name stays on the branch, his men stay his, and the day
        /// he is released he simply has them back. What DOES change is whose numbers the
        /// bonuses read - a crew whose commander is inside is run by his best man, and
        /// runs like his best man.
        ///
        /// Every read of a commander's ATTRIBUTES goes through here. Reads of his
        /// IDENTITY - who signs for the gear, whose name is on the record, who the
        /// branch belongs to - deliberately do not: a deputy does not inherit the branch,
        /// he holds it.
        ///
        /// The deputy is the crew's highest-Leadership active hood, and nobody at all
        /// when the crew has none - a branch of jailed men is a branch that is not
        /// working, and a null here is the honest answer rather than a stand-in.
        /// </summary>
        public static Character EffectiveLieutenant(Roster roster, Crew crew)
        {
            if (roster == null || crew == null)
                return null;

            var leader = roster.Find(crew.LieutenantId);
            if (leader != null && leader.Status == CharacterStatus.Active)
                return leader;

            Character deputy = null;
            var best = int.MinValue;
            for (var i = 0; i < crew.HoodIds.Count; i++)
            {
                var hood = roster.Find(crew.HoodIds[i]);
                if (hood == null || hood.Status != CharacterStatus.Active)
                    continue;
                var leadership = hood.GetHalfSteps(CharacterAttribute.Leadership);
                // A tie goes to the lower id: the same crew must name the same deputy on
                // two runs of the same seed, and list order is not a promise.
                if (leadership > best || (leadership == best && deputy != null && hood.Id < deputy.Id))
                {
                    best = leadership;
                    deputy = hood;
                }
            }
            return deputy;
        }

        /// <summary>Lieutenants on the books who are still on their feet.</summary>
        public static int LieutenantsHeld(Roster roster)
        {
            if (roster == null)
                return 0;
            var held = 0;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Rank == Rank.Lieutenant && !member.Gone)
                    held++;
            }
            return held;
        }
    }
}
