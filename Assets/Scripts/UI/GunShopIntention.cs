namespace LivingCity.UI
{
    /// <summary>
    /// The gun shop's words. Pure, no UnityEngine.Object, per the intention-helper rule.
    /// One line, middots, no labels - the 280px NoWrap budget BusinessIntention measured.
    /// </summary>
    public static class GunShopIntention
    {
        public static string Title() => "Gun Shop";

        /// <summary>
        /// What the counter reports: the player's standing against the catalog. "2 of 2"
        /// reads as sold out, which for a shop with no restock is the truth.
        /// </summary>
        public static string Line(int owned, int total) =>
            total <= 0 ? "Iron under the counter"
                       : $"Iron under the counter · {owned} of {total} owned";
    }
}
