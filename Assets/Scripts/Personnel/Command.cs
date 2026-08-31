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
    }
}
