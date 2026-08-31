using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>
    /// The one thing a man gets better at without being exposed to anything: command.
    /// A shooter improves by standing where he can be shot; a lieutenant improves by
    /// holding the chair, every quiet day, whether anything happened or not.
    ///
    /// It is deliberately the ONLY passive source in the game. Everything else has to
    /// be gone out and done, which is what keeps the danger ordering honest - and why
    /// this class is small, named, and easy to find when somebody wonders where a
    /// lieutenant's Leadership came from.
    ///
    /// Pure and free of UnityEngine; the runner calls it from the day tick.
    /// </summary>
    public static class CommandDrip
    {
        static readonly List<OrganizationPerson> Under = new List<OrganizationPerson>();

        /// <summary>
        /// One day of holding a command, for everybody actually holding one. A
        /// lieutenant with an empty crew commands nothing and draws nothing; a man in a
        /// cell or a bed is not holding his chair and draws nothing either, and starts
        /// again the day he stands up.
        /// </summary>
        /// <returns>How many men drew a drip today.</returns>
        public static int Tick(Roster roster)
        {
            if (roster == null)
                return 0;

            var query = new OrganizationQuery(roster);
            var drew = 0;

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Rank != Rank.Lieutenant && member.Rank != Rank.Boss)
                    continue;
                // Not Gone, but Active: the outfit pays a man in a cell and does not
                // pretend he ran it from there.
                if (member.Status != CharacterStatus.Active)
                    continue;

                query.CollectDirectSubordinates(member.Id, Under);
                if (Under.Count == 0)
                    continue;

                if (ActivityXp.AwardCommand(member, Under.Count) > 0)
                    drew++;
            }

            Under.Clear();
            return drew;
        }
    }
}
