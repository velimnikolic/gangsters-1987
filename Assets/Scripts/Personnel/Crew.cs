using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>
    /// One lieutenant and every hood under his command. The branch OWNS its organization
    /// membership - a character carries no parent field - so moving a man is one list
    /// edit, never a dual write that can drift. DemoCrews projects only a small tactical
    /// detachment from this list; its physical formation is not this hierarchy.
    ///
    /// The Id exists for the future: the weekly order system hangs a lieutenant's time
    /// budget and his crew's orders off a stable crew identity, so promoting today creates
    /// the peg those systems will hang from without restructuring anything here.
    /// </summary>
    public sealed class Crew
    {
        /// <summary>Physical figures shown in one RTS detachment. This is deliberately
        /// not an organization limit: a lieutenant may command many more hoods.</summary>
        public const int MaxTacticalHoods = 4;

        public int Id;
        public int LieutenantId;
        public readonly List<int> HoodIds = new List<int>();

        /// <summary>How this crew runs its rounds (ECON-005). The player's one lever
        /// over collection: what share of a short payment is taken without a word, the
        /// fear a round leaves, the heat it draws.</summary>
        public CrewPolicy Policy = CrewPolicy.Normal;

        /// <summary>
        /// Whether the BOSS has ruled on this crew's bag - named a man for it, or taken
        /// it off the last man and left it with nobody. The lieutenant hands the bag
        /// to one of his own only where the boss has not spoken (RosterOps.TendCrewBag);
        /// LET HIM PICK clears the ruling and gives the job back to him.
        /// </summary>
        public bool BagNamedByBoss;

        /// <summary>WHO the boss named, when he named anybody: -1 means he ruled that
        /// NOBODY carries it. A ruling only outlives the man it names while that man is
        /// still one of this crew's own and still on the books - otherwise it is spent,
        /// and the lieutenant hands the bag out again (RosterOps.TendCrewBag). Without
        /// this, a named man who was killed or moved away left the crew with a standing
        /// order naming nobody, and it never collected again.</summary>
        public int BagNamedId = -1;
    }
}
