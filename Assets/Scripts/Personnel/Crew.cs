using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>
    /// One lieutenant and the hoods under him. The crew OWNS its membership - a character
    /// carries no crew field - so moving a man is one list edit, never a dual write that
    /// can drift.
    ///
    /// The Id exists for the future: the weekly order system hangs a lieutenant's time
    /// budget and his crew's orders off a stable crew identity, so promoting today creates
    /// the peg those systems will hang from without restructuring anything here.
    /// </summary>
    public sealed class Crew
    {
        /// <summary>A lieutenant runs four men at most - the fifth is refused. The
        /// street bar has five slots for exactly this reason.</summary>
        public const int MaxHoods = 4;

        public int Id;
        public int LieutenantId;
        public readonly List<int> HoodIds = new List<int>();
    }
}
