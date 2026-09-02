using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>
    /// The door a defector walks through: which house took him, and what it is called.
    ///
    /// A plain id and a name, so <see cref="Defection"/> stays what it has always been -
    /// pure arithmetic over a roster with no idea a city exists. WHO the house is, and
    /// why it is that one and not another, is the caller's decision
    /// (<see cref="Outfit.OpenDoors"/>); this only carries the answer far enough to
    /// reach the sentence.
    ///
    /// The default is UNKNOWN, and a report that carries it prints exactly the words
    /// the book printed before any of this existed - "another family".
    /// </summary>
    public readonly struct DefectionDoor
    {
        public readonly int GangId;

        readonly string family;

        public DefectionDoor(int gangId, string family)
        {
            GangId = gangId;
            this.family = family ?? "";
        }

        /// <summary>The house's name. A PROPERTY rather than the field, because the
        /// zero value of a struct carries a null string and every caller here reads a
        /// length - the unhanded door has to answer an empty name, not throw.</summary>
        public string Family => family ?? "";

        /// <summary>Gang zero is the player's own outfit and can never be a
        /// destination, so the zero value of this struct reads as "nowhere named".</summary>
        public bool IsKnown => GangId > 0 && Family.Length > 0;
    }

    /// <summary>Who walked, who walked with him, and whose door he knocked on.</summary>
    public readonly struct DefectionReport
    {
        public readonly int LieutenantId;
        public readonly string Name;

        /// <summary>His men who went out with him, by id.</summary>
        public readonly int[] TookWithHim;

        /// <summary>The house that took them. Unknown when nobody handed one in - a
        /// headless fixture, or a campaign with no city under it.</summary>
        public readonly DefectionDoor Door;

        public DefectionReport(int lieutenantId, string name, int[] tookWithHim,
            DefectionDoor door = default)
        {
            LieutenantId = lieutenantId;
            Name = name;
            TookWithHim = tookWithHim ?? System.Array.Empty<int>();
            Door = door;
        }

        public bool Happened => LieutenantId >= 0;

        /// <summary>The house's name, or an empty string when nobody named one.</summary>
        public string Family => Door.Family;

        public int ToGangId => Door.IsKnown ? Door.GangId : -1;
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

        /// <summary>A second scratch, so a page asking who WOULD follow him never
        /// disturbs the list the night's own pass is walking.</summary>
        static readonly List<Character> Watchers = new List<Character>();

        /// <summary>
        /// The men who would go out behind him tonight, in the order they would go:
        /// his own crew, only the ones loyal enough to him to follow, his most loyal
        /// first, and never more than his Leadership can carry. Id breaks the tie so
        /// the same history always takes the same men out.
        ///
        /// It reads and never writes, which is what lets a page ask the question
        /// without a defection happening.
        /// </summary>
        static void Gather(Roster roster, Character lieutenant, Crew crew,
            List<Character> into)
        {
            into.Clear();
            if (roster == null || lieutenant == null || crew == null)
                return;

            for (var i = 0; i < crew.HoodIds.Count; i++)
            {
                var hood = roster.Find(crew.HoodIds[i]);
                if (hood != null && !hood.Gone && hood.Loyalty >= FollowsAt)
                    into.Add(hood);
            }

            into.Sort((a, b) =>
            {
                var byLoyalty = b.Loyalty.CompareTo(a.Loyalty);
                return byLoyalty != 0 ? byLoyalty : a.Id.CompareTo(b.Id);
            });

            var room = CanTake(lieutenant, crew.HoodIds.Count);
            if (into.Count > room)
                into.RemoveRange(room, into.Count - room);
        }

        /// <summary>
        /// How many of his men would actually walk out behind him if he broke tonight -
        /// the SAME arithmetic the night uses, asked without anything happening.
        ///
        /// The page that prints it reads this and never the mark beside his name: a
        /// flag informs and never acts, and a branch card that counted the flag rather
        /// than the numbers under it would be the mark acting.
        /// </summary>
        public static int WouldFollow(Roster roster, Character lieutenant)
        {
            if (roster == null || lieutenant == null || lieutenant.Gone ||
                lieutenant.Rank != Rank.Lieutenant)
                return 0;
            var crew = roster.CrewOf(lieutenant.Id);
            if (crew == null || crew.LieutenantId != lieutenant.Id)
                return 0;

            Gather(roster, lieutenant, crew, Watchers);
            var count = Watchers.Count;
            Watchers.Clear();
            return count;
        }

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
        /// <param name="door">The house that takes him, decided by the caller off the
        /// city it can see (<see cref="Outfit.OpenDoors"/>). Left unhanded, the paper
        /// and his file say "another family", exactly as they did before anybody
        /// worked out where he went.</param>
        public static DefectionReport Tick(Roster roster, Character lieutenant, int day,
            List<Incident> incidents, DefectionDoor door = default)
        {
            if (roster == null || lieutenant == null || lieutenant.Gone ||
                lieutenant.Rank != Rank.Lieutenant ||
                lieutenant.Loyalty > BreakingPoint)
                return new DefectionReport(-1, "", null);

            var crew = roster.CrewOf(lieutenant.Id);
            if (crew == null || crew.LieutenantId != lieutenant.Id)
                return new DefectionReport(-1, "", null);

            Gather(roster, lieutenant, crew, Followers);

            var taken = new int[Followers.Count];
            for (var i = 0; i < Followers.Count; i++)
                taken[i] = Followers[i].Id;

            // Struck off after the list is built, not during: the list being walked is
            // the crew's own membership.
            //
            // Each of them takes his OWN line out with him. The desertion door is the
            // right door - gear, wages and posts settle the way they always did - but
            // the clerk's sentence about it is not the right sentence: these men did
            // not run from a fight, they followed somebody, and a file that said
            // otherwise would be the ledger lying about the one night it most matters.
            var followed = CareerText.WalkedOutWith(lieutenant.FullName);
            var loudness = Notability.WeightOf(IncidentKind.Defected);
            for (var i = 0; i < taken.Length; i++)
                RosterOps.Desert(roster, taken[i], followed, loudness);
            RosterOps.Desert(roster, lieutenant.Id,
                CareerText.WentOver(taken.Length, door.Family), loudness);

            incidents?.Add(new Incident(lieutenant.Id, lieutenant.FullName,
                IncidentKind.Defected, day, "", 0,
                IncidentText.DefectedLine(lieutenant.FullName, taken.Length,
                    door.Family)));

            Followers.Clear();
            return new DefectionReport(lieutenant.Id, lieutenant.FullName, taken, door);
        }
    }
}
