using UnityEngine;
using LivingCity.Entities;

namespace LivingCity.UI
{
    /// <summary>
    /// The words and colours of the police overlay, as a pure map from agent state - no
    /// UnityEngine.Object, so the headless suite can prove the one property that matters:
    /// every state a patrol car or an officer can be in HAS a colour and a sentence. A
    /// missing entry here would not throw anywhere; it would ship as a white diamond with an
    /// empty popup, which is exactly the kind of defect only an exhaustiveness test catches.
    ///
    /// English on purpose (user's choice), and phrased as INTENTION rather than mechanism -
    /// "Returning to the station", not "Returning(2)". The unit's number comes from the
    /// director at spawn, so the popup can say which car this is.
    /// </summary>
    public static class PoliceIntention
    {
        // The four colours this map used to own now live in IntentionPalette, so that the
        // school bus resting in its yard reads the same grey as a patrol car resting in
        // its own. Aliased rather than inlined below to keep the switches readable.
        static readonly Color Patrol = IntentionPalette.Working;
        static readonly Color Homeward = IntentionPalette.Homeward;
        static readonly Color Idle = IntentionPalette.Idle;
        static readonly Color Manoeuvre = IntentionPalette.Manoeuvre;

        public static Color CarColor(PolicePatrolAgent.State state) => state switch
        {
            PolicePatrolAgent.State.Patrolling => Patrol,
            PolicePatrolAgent.State.Returning => Homeward,
            PolicePatrolAgent.State.Resting => Idle,
            PolicePatrolAgent.State.Docking => Manoeuvre,
            PolicePatrolAgent.State.Undocking => Manoeuvre,
            _ => Color.white,
        };

        public static Color OfficerColor(PoliceOfficerAgent.State state) => state switch
        {
            PoliceOfficerAgent.State.Patrolling => Patrol,
            PoliceOfficerAgent.State.Returning => Homeward,
            PoliceOfficerAgent.State.AtStation => Idle,
            _ => Color.white,
        };

        public static string CarTitle(int unitNumber) => $"Patrol Car {unitNumber}";

        public static string OfficerTitle(int unitNumber) => $"Officer {unitNumber}";

        public static string CarIntention(PolicePatrolAgent.State state, int routesRemaining) =>
            state switch
            {
                PolicePatrolAgent.State.Patrolling =>
                    routesRemaining == 1
                        ? "On patrol - last route before heading back"
                        : $"On patrol - {routesRemaining} routes until return",
                PolicePatrolAgent.State.Returning => "Returning to the station",
                PolicePatrolAgent.State.Docking => "Parking at the station",
                PolicePatrolAgent.State.Resting => "Resting at the station",
                PolicePatrolAgent.State.Undocking => "Pulling out on patrol",
                _ => string.Empty,
            };

        public static string OfficerIntention(PoliceOfficerAgent.State state, int routesRemaining) =>
            state switch
            {
                PoliceOfficerAgent.State.Patrolling =>
                    routesRemaining == 1
                        ? "Walking the beat - last route before heading back"
                        : $"Walking the beat - {routesRemaining} routes until return",
                PoliceOfficerAgent.State.Returning => "Returning to the station",
                PoliceOfficerAgent.State.AtStation => "Inside the station",
                _ => string.Empty,
            };
    }
}
