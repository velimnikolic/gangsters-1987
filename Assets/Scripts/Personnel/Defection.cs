using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>Who walked, and who walked with him.</summary>
    public readonly struct DefectionReport
    {
        public readonly int LieutenantId;
        public readonly string Name;

        /// <summary>His men who went out with him, by id.</summary>
        public readonly int[] TookWithHim;

        public DefectionReport(int lieutenantId, string name, int[] tookWithHim)
        {
            LieutenantId = lieutenantId;
            Name = name;
            TookWithHim = tookWithHim ?? System.Array.Empty<int>();
        }

        public bool Happened => LieutenantId >= 0;
    }

    /// <summary>
    /// The whole betrayal, in one arithmetic: a lieutenant whose loyalty has run out
    /// leaves, and takes his most loyal men with him. How many is a function of his
    /// Leadership - the thing that made him worth promoting is the thing that makes
    /// losing him expensive.
    ///
    /// Deterministic. Given the seed and the history, the day of the break is fixed.
    /// There are NO random betrayals out of the blue: the player has been reading
    /// rumour lines about this man for weeks (PSY-004) and a red flag beside his name
    /// (LOY-004), and if he did nothing about it that is a decision he made.
    ///
    /// The exit itself is a mass, directed desertion, and goes out through the one door
    /// desertion already uses, so equipment, wages and presence all settle the way they
    /// always did.
    ///
    /// Pure and free of UnityEngine.
    /// </summary>
    public static class Defection
    {
        /// <summary>At or under this much loyalty to the Boss, a lieutenant is gone.
        /// Deliberately well under LOY-001's watch band, so a man crosses into "bears
        /// watching" long before he crosses into this.</summary>
        public const int BreakingPoint = 15;

        /// <summary>The share of his own crew a lieutenant can carry out with him, at
        /// the bottom of the Leadership scale and at the top. A man nobody would follow
        /// leaves alone; a man they would follow anywhere empties his branch.</summary>
        public const int MinTakenPercent = 0;

        public const int MaxTakenPercent = 80;

        /// <summary>A man will only follow him out if his own loyalty to him is at
        /// least this. Loyalty is to the DIRECT superior, so this is exactly the number
        /// that says whether he would.</summary>
        public const int FollowsAt = 55;

        static readonly List<Character> Followers = new List<Character>();

        /// <summary>How many of his own men he could carry out, by his Leadership.</summary>
        public static int CanTake(Character lieutenant, int crewSize)
        {
            if (lieutenant == null || crewSize <= 0)
                return 0;

            var reach = AttributeScale.ValueOf(
                lieutenant.GetHalfSteps(CharacterAttribute.Leadership));
            var floor = AttributeScale.ValueOf(AttributeScale.MinHalfSteps);
            var span = AttributeScale.ValueOf(AttributeScale.MaxHalfSteps) - floor;

            var percent = MinTakenPercent +
                          (MaxTakenPercent - MinTakenPercent) * (reach - floor) / span;
            return crewSize * percent / 100;
        }

        /// <summary>
        /// One day's check on one lieutenant. He goes when his loyalty to the Boss has
        /// run out, and the men who go with him are HIS most loyal - never another
        /// crew's, and never more than his Leadership can carry.
        /// </summary>
        public static DefectionReport Tick(Roster roster, Character lieutenant, int day,
            List<Incident> incidents)
        {
            if (roster == null || lieutenant == null || lieutenant.Gone ||
                lieutenant.Rank != Rank.Lieutenant ||
                lieutenant.Loyalty > BreakingPoint)
                return new DefectionReport(-1, "", null);

            var crew = roster.CrewOf(lieutenant.Id);
            if (crew == null || crew.LieutenantId != lieutenant.Id)
                return new DefectionReport(-1, "", null);

            Followers.Clear();
            for (var i = 0; i < crew.HoodIds.Count; i++)
            {
                var hood = roster.Find(crew.HoodIds[i]);
                if (hood != null && !hood.Gone && hood.Loyalty >= FollowsAt)
                    Followers.Add(hood);
            }

            // His most loyal first, id as the tiebreak so the same history always takes
            // the same men out.
            Followers.Sort((a, b) =>
            {
                var byLoyalty = b.Loyalty.CompareTo(a.Loyalty);
                return byLoyalty != 0 ? byLoyalty : a.Id.CompareTo(b.Id);
            });

            var room = CanTake(lieutenant, crew.HoodIds.Count);
            if (Followers.Count > room)
                Followers.RemoveRange(room, Followers.Count - room);

            var taken = new int[Followers.Count];
            for (var i = 0; i < Followers.Count; i++)
                taken[i] = Followers[i].Id;

            // Struck off after the list is built, not during: the list being walked is
            // the crew's own membership.
            for (var i = 0; i < taken.Length; i++)
                RosterOps.Desert(roster, taken[i]);
            RosterOps.Desert(roster, lieutenant.Id);

            incidents?.Add(new Incident(lieutenant.Id, lieutenant.FullName,
                IncidentKind.Defected, day, "", 0,
                IncidentText.Line(IncidentKind.Defected, lieutenant.FullName, "")));

            Followers.Clear();
            return new DefectionReport(lieutenant.Id, lieutenant.FullName, taken);
        }
    }
}
