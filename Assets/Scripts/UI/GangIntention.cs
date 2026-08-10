namespace LivingCity.UI
{
    /// <summary>
    /// The words of a gang member's popup - PoliceIntention's rule: pure strings, no
    /// UnityEngine.Object, budgets proven headless (title 35 chars, line 44). "Soldier"
    /// is deliberate popup wording; the ledger's own word is "Hood" and LedgerText owns
    /// that - different domains already speak differently about the same man.
    /// </summary>
    public static class GangIntention
    {
        public static string Title(string firstName, string surname, bool lieutenant) =>
            $"{firstName} {surname} — {(lieutenant ? "Lieutenant" : "Soldier")}";

        /// <summary>Phase 0 has no missions, so everyone is standing by.</summary>
        public static string Line(string gangName, bool isPlayer) =>
            isPlayer
                ? "Your outfit · Standing by"
                : $"{gangName} family · Standing by";
    }
}
