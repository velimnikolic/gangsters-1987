namespace LivingCity.Outfit
{
    /// <summary>
    /// WHETHER THESE TWO FIGHT, for every pair of houses in the city including the
    /// player's.
    ///
    /// It is exactly the three sentences the ledger prints on the FAMILIES card
    /// (<c>LedgerText.StanceEffect</c>), and nothing else:
    ///
    ///  Peace - no engagement. Their men and ours pass in the street, claimed ground or
    ///          not. The one exception is a man already being shot at, who fights back.
    ///  Truce - territorial. Their men engage ours caught inside THEIR ground, and ours
    ///          engage theirs on OURS. Neutral ground stays quiet.
    ///  War   - on sight, anywhere in the city.
    ///
    /// Pure, and deliberately tiny: a rule this consequential must be readable in one
    /// screen and testable against the sentences it claims to implement.
    /// </summary>
    public static class Engagement
    {
        /// <summary>
        /// May the FIRST house's men engage the second's, here?
        /// </summary>
        /// <param name="stance">Where the pair stands. Symmetric - both sides read the
        /// same answer.</param>
        /// <param name="oursIsTheGround">The block they are standing on is led by the
        /// house that would be doing the engaging.</param>
        /// <param name="provoked">This man is inside the window after being shot at.
        /// Fighting back is not a stance question.</param>
        public static bool May(Stance stance, bool oursIsTheGround, bool provoked)
        {
            if (provoked)
                return true;
            switch (stance)
            {
                case Stance.War:
                    return true;
                case Stance.Truce:
                    return oursIsTheGround;
                default:
                    return false;
            }
        }
    }
}
