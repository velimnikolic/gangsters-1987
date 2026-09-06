using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.UI
{
    /// <summary>
    /// Where the door surfaces get the men from.
    ///
    /// It used to DRAW the choice as well - a run of chips under a WHO GOES label - and
    /// that drawing is gone: the premises card sets the same choice as a dropdown of its
    /// own (<see cref="DoorMenu"/>), because the redesign gives the answer a place in the
    /// section head and a wall of chips has none. The collecting rule stays here, shared,
    /// so the card and anything else that asks reads one roster.
    /// </summary>
    public static class CrewMissionPicker
    {
        internal static List<TacticalPersonnelMapping> Physical()
        {
            var rows = new List<TacticalPersonnelMapping>();
            Gameplay.PersonnelDirector.Instance?.Organization?.CollectPhysicalMappings(rows);
            return rows;
        }
    }
}
