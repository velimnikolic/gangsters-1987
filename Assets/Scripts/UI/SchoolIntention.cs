using UnityEngine;
using LivingCity.Entities;

namespace LivingCity.UI
{
    /// <summary>
    /// The words and colours of the school run, as a pure map from agent state - no
    /// UnityEngine.Object, so the headless suite can prove the one property that matters: every
    /// state the bus, a child or the school itself can be in HAS a colour and a sentence. A
    /// missing entry here would not throw anywhere; it would ship as a white marker with an
    /// empty popup, which is exactly the kind of defect only an exhaustiveness test catches.
    /// PoliceIntention's rule, and its test.
    ///
    /// English, as the police layer is (the user's choice there), and phrased as INTENTION
    /// rather than mechanism - "Driving to stop 2 of 3 to collect", not "Driving(1)". The
    /// school's own line is the exception and is deliberately a REPORT rather than an
    /// intention: a building does not want anything, it just knows how many of its pupils are
    /// still standing at a kerb.
    /// </summary>
    public static class SchoolIntention
    {
        public static Color BusColor(SchoolBusAgent.State state) => state switch
        {
            SchoolBusAgent.State.Resting => IntentionPalette.Idle,
            SchoolBusAgent.State.Loading => IntentionPalette.Busy,
            SchoolBusAgent.State.Undocking => IntentionPalette.Manoeuvre,
            SchoolBusAgent.State.Driving => IntentionPalette.Working,
            SchoolBusAgent.State.Halted => IntentionPalette.Busy,
            SchoolBusAgent.State.Returning => IntentionPalette.Homeward,
            SchoolBusAgent.State.Docking => IntentionPalette.Manoeuvre,
            SchoolBusAgent.State.Unloading => IntentionPalette.Busy,
            _ => Color.white,
        };

        public static Color ChildColor(SchoolChildAgent.State state) => state switch
        {
            SchoolChildAgent.State.Waiting => IntentionPalette.Waiting,
            SchoolChildAgent.State.Boarding => IntentionPalette.Busy,
            SchoolChildAgent.State.Riding => IntentionPalette.Working,
            SchoolChildAgent.State.Alighting => IntentionPalette.Busy,
            SchoolChildAgent.State.InSchool => IntentionPalette.Idle,
            SchoolChildAgent.State.WalkingToSchool => IntentionPalette.Homeward,
            _ => Color.white,
        };

        public static string BusTitle() => "School Bus";

        public static string ChildTitle(int number) => $"Schoolchild {number}";

        public static string SchoolTitle() => "School";

        /// <summary>
        /// <paramref name="stop"/> is 1-based and <paramref name="stopCount"/> is the whole
        /// round; both are zero while the bus is off the road, which the Driving branch has to
        /// survive because a run whose stops were all unreachable still passes through it.
        /// </summary>
        public static string BusIntention(
            SchoolBusAgent.State state, int stop, int stopCount, bool toSchool) => state switch
        {
            SchoolBusAgent.State.Resting => "Parked in the school yard",
            SchoolBusAgent.State.Loading => "Loading the children in the school yard",
            SchoolBusAgent.State.Undocking => "Pulling out of the school yard",
            SchoolBusAgent.State.Driving =>
                stop > 0 && stopCount > 0
                    ? (toSchool
                        ? $"Driving to stop {stop} of {stopCount} to collect"
                        : $"Driving to stop {stop} of {stopCount} to drop off")
                    : "Driving the school round",
            SchoolBusAgent.State.Halted =>
                toSchool ? "Stopped - children getting on" : "Stopped - children getting off",
            SchoolBusAgent.State.Returning => "Driving back to the school",
            SchoolBusAgent.State.Docking => "Parking in the school yard",
            SchoolBusAgent.State.Unloading => "Letting the children off at the school",
            _ => string.Empty,
        };

        public static string ChildIntention(SchoolChildAgent.State state) => state switch
        {
            SchoolChildAgent.State.Waiting => "Waiting for the school bus",
            SchoolChildAgent.State.Boarding => "Getting on the bus",
            SchoolChildAgent.State.Riding => "Riding the bus",
            SchoolChildAgent.State.Alighting => "Getting off the bus",
            SchoolChildAgent.State.InSchool => "In class",
            SchoolChildAgent.State.WalkingToSchool => "Walking to school - the bus never came",
            _ => string.Empty,
        };

        /// <summary>
        /// What the school reports about itself. Two clauses at most: where its pupils are, and
        /// where its bus is. Anything longer stops being readable at a glance, which is the only
        /// thing a building marker is for.
        ///
        /// <paramref name="hasBus"/> false is a real case and not a defect - a seed can produce
        /// a school whose bus never spawned, and saying so is more use than saying nothing.
        /// </summary>
        public static string SchoolLine(
            int waiting, int inSchool, int travelling,
            bool hasBus, SchoolBusAgent.State busState)
        {
            var pupils =
                waiting > 0 ? Count(waiting, "child", "children") + " waiting for the bus"
              : travelling > 0 ? Count(travelling, "child", "children") + " on the way"
              : inSchool > 0 ? Count(inSchool, "child", "children") + " in class"
              : "No pupils about";

            if (!hasBus)
                return pupils + " - no bus";

            var bus = busState switch
            {
                SchoolBusAgent.State.Resting => "bus in the yard",
                SchoolBusAgent.State.Loading => "bus loading",
                SchoolBusAgent.State.Unloading => "bus unloading",
                SchoolBusAgent.State.Docking => "bus pulling in",
                SchoolBusAgent.State.Undocking => "bus pulling out",
                SchoolBusAgent.State.Returning => "bus on its way back",
                _ => "bus on its round",
            };

            return pupils + " - " + bus;
        }

        static string Count(int n, string one, string many) =>
            n == 1 ? "1 " + one : n + " " + many;
    }
}
