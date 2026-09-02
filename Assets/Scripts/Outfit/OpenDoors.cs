using System.Collections.Generic;
using LivingCity.Gangs;

namespace LivingCity.Outfit
{
    /// <summary>
    /// One lieutenant who went over: the night, the man, how many hands went with him
    /// and whose door they all walked through.
    ///
    /// The campaign keeps a book of these because a defection is the loudest thing that
    /// happens on this payroll and the paper's line about it scrolls away in a week.
    /// The FAMILIES sheet reads it to say what each house has taken off us.
    /// </summary>
    public readonly struct DefectionRecord
    {
        public readonly int Day;
        public readonly int LieutenantId;
        public readonly string Name;

        /// <summary>How many of his own men followed him out.</summary>
        public readonly int TookWithHim;

        /// <summary>The house that took them; -1 when nobody named one.</summary>
        public readonly int GangId;

        public readonly string Family;

        public DefectionRecord(int day, int lieutenantId, string name, int tookWithHim,
            int gangId, string family)
        {
            Day = day;
            LieutenantId = lieutenantId;
            Name = name ?? "";
            TookWithHim = tookWithHim;
            GangId = gangId;
            Family = family ?? "";
        }

        /// <summary>The man plus everyone behind him - what the house actually
        /// gained.</summary>
        public int Men => 1 + TookWithHim;
    }

    /// <summary>One house, and what claim it has on a man of ours who is leaving.</summary>
    public readonly struct OpenDoor
    {
        public readonly int GangId;

        /// <summary>Blocks where this house and the outfit BOTH hold ground - the
        /// street he has been standing on the other side of.</summary>
        public readonly int Shoulders;

        /// <summary>Where the outfit stands with them today.</summary>
        public readonly Stance Stance;

        /// <summary>Buildings the house holds city-wide - the size of the thing he
        /// would be joining.</summary>
        public readonly int Held;

        public OpenDoor(int gangId, int shoulders, Stance stance, int held)
        {
            GangId = gangId;
            Shoulders = shoulders;
            Stance = stance;
            Held = held;
        }
    }

    /// <summary>
    /// Where a man who has had enough of us actually goes.
    ///
    /// THE RULE, stated once, because LOY-002 is a formula epic and a destination
    /// picked by a die roll would be the one scripted event in it:
    ///
    ///     claim(house) = <see cref="PerShoulder"/> x blocks where that house and the
    ///                    outfit both hold ground
    ///                  + what the standing with them is worth
    ///                      (war <see cref="AtWar"/>, truce <see cref="AtTruce"/>,
    ///                       peace <see cref="AtPeace"/>)
    ///
    /// Highest claim takes him. Ties go to the house holding the most ground, and then
    /// to the lowest gang id - which is fixed for the life of the catalog, so the same
    /// seed and the same history send him through the same door forever.
    ///
    /// The two terms are the two things a man in his position would actually weigh:
    /// the house he has been standing across the street from knows him and can use him,
    /// and a house at war with us is recruiting against us this week. A house with
    /// neither still scores - nobody is ever homeless - and then it is the biggest one
    /// that takes him, because a man walking out with four hands behind him goes where
    /// the money is.
    ///
    /// INDEPENDENCE IS NOT AN ANSWER, and that is a decision rather than an omission.
    /// A family is a fixed row of the catalog: an id that never moves, a colour on the
    /// map, a front, a crew on the pavement and a saved campaign's stance hanging off
    /// it (<see cref="GangCatalog"/>). "He started his own thing" would mean a
    /// twenty-second house the city has none of those for, and the honest version of
    /// that is a whole epic rather than a branch in this method.
    ///
    /// Pure and free of UnityEngine: the city is handed in as holdings and stances.
    /// </summary>
    public static class OpenDoors
    {
        /// <summary>What a shared block is worth. Deliberately larger than the whole
        /// stance scale: who he has been standing next to beats who we are shouting
        /// at.</summary>
        public const int PerShoulder = 4;

        public const int AtWar = 3;
        public const int AtTruce = 1;
        public const int AtPeace = 0;

        public static int WorthOf(Stance stance) => stance switch
        {
            Stance.War => AtWar,
            Stance.Truce => AtTruce,
            _ => AtPeace,
        };

        /// <summary>What one house's claim comes to.</summary>
        public static int ClaimOf(in OpenDoor door) =>
            door.Shoulders * PerShoulder + WorthOf(door.Stance);

        /// <summary>
        /// Reads the city into one candidate per rival house. Every figure comes off
        /// the holdings sweep the tribute pass already takes, so nothing new is
        /// measured and nothing is stored.
        /// </summary>
        public static void Read(IReadOnlyList<Turf.Holding> holdings,
            GangRelations relations, int playerGangId, List<OpenDoor> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (holdings == null)
                return;

            var count = GangCatalog.GangCount;
            var held = new int[count];
            var shoulders = new int[count];

            var ours = new HashSet<int>();
            for (var i = 0; i < holdings.Count; i++)
            {
                var gangId = holdings[i].GangId;
                if (gangId >= 0 && gangId < count)
                    held[gangId]++;
                if (gangId == playerGangId)
                    ours.Add(holdings[i].BlockId);
            }

            // A shoulder is a BLOCK, not a building: a house with six shops on one
            // street we also stand in is one street's worth of acquaintance, not six.
            var counted = new HashSet<long>();
            for (var i = 0; i < holdings.Count; i++)
            {
                var theirs = holdings[i];
                if (theirs.GangId == playerGangId || theirs.GangId < 0 ||
                    theirs.GangId >= count || !ours.Contains(theirs.BlockId))
                    continue;

                if (counted.Add((long)theirs.GangId * 1_000_003L + theirs.BlockId))
                    shoulders[theirs.GangId]++;
            }

            for (var gangId = 0; gangId < count; gangId++)
            {
                if (gangId == playerGangId)
                    continue;
                into.Add(new OpenDoor(gangId, shoulders[gangId],
                    relations != null ? relations.StanceWith(gangId) : Stance.Peace,
                    held[gangId]));
            }
        }

        /// <summary>
        /// Whose door opens. Answers an invalid door only when there is nothing to
        /// choose from at all - the caller then prints what the book always printed.
        /// </summary>
        public static LivingCity.Personnel.DefectionDoor Pick(IReadOnlyList<OpenDoor> doors)
        {
            var best = -1;
            var bestClaim = 0;
            var bestHeld = 0;

            for (var i = 0; i < doors.Count; i++)
            {
                var door = doors[i];
                if (door.GangId <= 0 || door.GangId >= GangCatalog.GangCount)
                    continue;

                var claim = ClaimOf(door);
                var takes = best < 0 ||
                            claim > bestClaim ||
                            (claim == bestClaim && door.Held > bestHeld) ||
                            (claim == bestClaim && door.Held == bestHeld &&
                             door.GangId < best);
                if (!takes)
                    continue;

                best = door.GangId;
                bestClaim = claim;
                bestHeld = door.Held;
            }

            return best < 0
                ? default
                : new LivingCity.Personnel.DefectionDoor(best, GangCatalog.Names[best]);
        }
    }
}
