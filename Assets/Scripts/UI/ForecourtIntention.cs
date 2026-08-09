using UnityEngine;
using LivingCity.Entities;

namespace LivingCity.UI
{
    /// <summary>Which building a forecourt driver came for. The one thing that separates a bank
    /// customer from a parent on the school run - the trip is identical.</summary>
    public enum ForecourtErrand
    {
        Bank,
        School,
    }

    /// <summary>
    /// The words and colours of a landmark forecourt: the drivers using it, and the bank's own
    /// report on itself. Pure, with no UnityEngine.Object, so the headless suite can prove that
    /// every state has a colour and a sentence - PoliceIntention's rule and its test.
    ///
    /// English, phrased as intention, as the rest of the overlay is.
    /// </summary>
    public static class ForecourtIntention
    {
        public static Color VisitorColor(ForecourtVisitorAgent.State state) => state switch
        {
            ForecourtVisitorAgent.State.Arriving => IntentionPalette.Working,
            ForecourtVisitorAgent.State.Docking => IntentionPalette.Manoeuvre,
            ForecourtVisitorAgent.State.Visiting => IntentionPalette.Idle,
            ForecourtVisitorAgent.State.Leaving => IntentionPalette.Homeward,
            _ => Color.white,
        };

        public static string VisitorTitle(ForecourtErrand errand) => errand switch
        {
            ForecourtErrand.Bank => "Bank Customer",
            ForecourtErrand.School => "Parent",
            _ => string.Empty,
        };

        public static string VisitorIntention(
            ForecourtVisitorAgent.State state, ForecourtErrand errand) => state switch
        {
            ForecourtVisitorAgent.State.Arriving => errand switch
            {
                ForecourtErrand.Bank => "Driving to the bank",
                ForecourtErrand.School => "Driving to the school",
                _ => string.Empty,
            },
            ForecourtVisitorAgent.State.Docking => "Parking in the forecourt",
            ForecourtVisitorAgent.State.Visiting => errand switch
            {
                ForecourtErrand.Bank => "Inside the bank",
                ForecourtErrand.School => "At the school gate",
                _ => string.Empty,
            },
            ForecourtVisitorAgent.State.Leaving => "Driving out of the city",
            _ => string.Empty,
        };

        public static string BankTitle() => "Bank";

        /// <summary>
        /// What the bank reports about itself: how busy it is, and whether a driver arriving now
        /// would find room. Both numbers come from the forecourt rather than from a count of
        /// cars, because a bay claimed by a car still driving in is not a free bay.
        /// </summary>
        public static string BankLine(int visitors, int freeBays, int totalBays)
        {
            var people = visitors == 0 ? "No customers"
                       : visitors == 1 ? "1 customer here"
                       : $"{visitors} customers here";

            if (totalBays <= 0)
                return people;

            var bays = freeBays == 0 ? "forecourt full"
                     : freeBays == 1 ? $"1 of {totalBays} bays free"
                     : $"{freeBays} of {totalBays} bays free";

            return people + " - " + bays;
        }
    }
}
