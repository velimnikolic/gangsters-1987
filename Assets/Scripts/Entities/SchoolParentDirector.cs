using System.Collections;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Parents dropping off and picking up at the school, using the bays the school bus does not.
    /// The whole trip is ForecourtVisitorDirector's, shared with the bank's customers; this is
    /// the four answers that make it the school's.
    ///
    /// It exists because a painted forecourt with nobody in it reads as a mistake. The school's
    /// yard is four bays and a bus berth, one bay carries a static bake, and without this the
    /// other three would stand empty from dawn to dusk while the bank next door had a car
    /// arriving every minute.
    ///
    /// Standing down is ordinary here, more so than for the bank: a seed whose school could not
    /// be recessed has no bays at all (see BlockBuilder's flush fallback), and a seed with no
    /// school has no marker.
    /// </summary>
    public sealed class SchoolParentDirector : ForecourtVisitorDirector
    {
        protected override string LogTag => "[SchoolParents]";
        protected override int SeedOffset => SeedOffsets.SchoolParents;
        protected override int Population => config.schoolParentCount;
        protected override Vector2 GapRange => config.schoolParentGapRange;
        protected override Vector2 StayRange => config.schoolParentStayRange;
        protected override UI.ForecourtErrand Errand => UI.ForecourtErrand.School;

        protected override StallHost FindForecourt() => FindFirstObjectByType<SchoolMarker>();

        /// <summary>The DOOR, not the building's position: the school is 24.9m of frontage and
        /// its origin is nowhere near the pavement its bays open onto.</summary>
        protected override Vector3 KerbFocusFrom(StallHost host) =>
            host is SchoolMarker school ? school.DoorWorld : host.transform.position;

        protected override string MissingForecourtMessage =>
            "No SchoolMarker in the scene - this seed drew no school, or the city predates the " +
            "marker. Parents stand down; regenerate the city (Tools/City/Set Up Scene) to fix.";

        IEnumerator Start() => Run();
    }
}
