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
    }
}
