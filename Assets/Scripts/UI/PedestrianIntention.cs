using System.Collections.Generic;
using UnityEngine;
using LivingCity.Entities;

namespace LivingCity.UI
{
    /// <summary>
    /// The words and colours of a civilian's overlay popup - the PoliceIntention shape: a
    /// pure map from PedestrianAgent.Activity, no UnityEngine.Object, so the headless suite
    /// can prove every activity has a sentence and a colour. A gap here ships as a white
    /// diamond over an empty popup, which nothing at runtime would ever complain about.
    ///
    /// Shopping is the one activity with variety: an errand table indexed by the agent's
    /// own hash, because "Out buying bread" clicked twice in a row on two different people
    /// reading identically would give the trick away. Every line is budgeted against the
    /// popup's 280px NoWrap width (the business overlay's 44-char rule, asserted in tests).
    /// </summary>
    public static class PedestrianIntention
    {
        // The palette's meanings hold: blue is out and about, green is mid-errand, grey is
        // at rest. Dead is this map's own - the palette has no colour for it, and white is
        // the "unmapped" sentinel the tests assert against.
        static readonly Color About = IntentionPalette.Working;
        static readonly Color Errand = IntentionPalette.Busy;
        static readonly Color Rest = IntentionPalette.Idle;

        /// <summary>Dark red, unmistakably not a state of life. Public for the tests.</summary>
        public static readonly Color Dead = new Color(0.55f, 0.15f, 0.15f);

        static readonly string[] ErrandTable =
        {
            "Out buying bread",
            "Picking up cigarettes",
            "Buying the morning paper",
            "Fetching groceries",
            "Picking up a prescription",
            "Buying stamps at the counter",
            "After a new pair of shoes",
            "Just window shopping",
        };

        /// <summary>Read-only view for the tests, which budget every entry.</summary>
        public static IReadOnlyList<string> Errands => ErrandTable;

        public static Color ActivityColor(PedestrianAgent.Activity activity) => activity switch
        {
            PedestrianAgent.Activity.Strolling => About,
            PedestrianAgent.Activity.Chatting => Errand,
            PedestrianAgent.Activity.Arguing => Errand,
            PedestrianAgent.Activity.Sitting => Rest,
            PedestrianAgent.Activity.Shopping => Errand,
            PedestrianAgent.Activity.Visiting => Errand,
            PedestrianAgent.Activity.Idling => Rest,
            PedestrianAgent.Activity.CommutingToWork => About,
            PedestrianAgent.Activity.AtWork => About,
            PedestrianAgent.Activity.HeadingHome => IntentionPalette.Homeward,
            PedestrianAgent.Activity.AtHome => Rest,
            PedestrianAgent.Activity.EveningStroll => About,
            PedestrianAgent.Activity.Dead => Dead,
            _ => Color.white,
        };

        public static string Line(PedestrianAgent.Activity activity, int errand) => activity switch
        {
            PedestrianAgent.Activity.Strolling => "Out for a walk",
            PedestrianAgent.Activity.Chatting => "Stopped for a chat",
            PedestrianAgent.Activity.Arguing => "Having words with somebody",
            PedestrianAgent.Activity.Sitting => "Resting on a bench",
            PedestrianAgent.Activity.Shopping =>
                ErrandTable[((errand % ErrandTable.Length) + ErrandTable.Length) % ErrandTable.Length],
            PedestrianAgent.Activity.Visiting => "Calling on somebody indoors",
            PedestrianAgent.Activity.Idling => "Taking a moment",
            PedestrianAgent.Activity.CommutingToWork => "Heading in to work",
            PedestrianAgent.Activity.AtWork => "On the clock - out on an errand",
            PedestrianAgent.Activity.HeadingHome => "Heading home",
            PedestrianAgent.Activity.AtHome => "Home for the night",
            PedestrianAgent.Activity.EveningStroll => "Out for the evening",
            PedestrianAgent.Activity.Dead => "Dead",
            _ => string.Empty,
        };
    }
}
