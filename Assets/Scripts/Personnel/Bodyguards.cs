using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>What came of an attempt on the Boss's life.</summary>
    public readonly struct AssassinationOutcome
    {
        /// <summary>True when the shot got through to him. The campaign layer decides
        /// what that means; this only says whether anybody stopped it.</summary>
        public readonly bool ReachedTheBoss;

        /// <summary>How many of the detail were spent stopping it - killed, put in a
        /// bed, or off the books because they ran.</summary>
        public readonly int GuardsSpent;

        public AssassinationOutcome(bool reachedTheBoss, int guardsSpent)
        {
            ReachedTheBoss = reachedTheBoss;
            GuardsSpent = guardsSpent;
        }
    }

    /// <summary>
    /// The men who stand between the Boss and the street.
    ///
    /// Permadeath has to be the consequence of a DECISION - a thin detail, a famous
    /// name, a war on - and never a coin that came up wrong. So an attempt on the Boss
    /// is spent on his detail first, man by man, and only reaches him when there is
    /// nobody left in front of him.
    ///
    /// The detail is not a new structure: it is a Crew whose lieutenant IS the Boss.
    /// That way the assignment rules, the capacity, the ledger and the street's
    /// crew-follow machinery all work on it unchanged, and a man is either on the
    /// detail or on other work because a man is only ever in one crew.
    ///
    /// Pure and free of UnityEngine.
    /// </summary>
    public static class Bodyguards
    {
        /// <summary>Of the guards who stand their ground and take it, how many die
        /// rather than end up in a bed. A detail is not a wall of meat - putting men
        /// in front of a gun costs the outfit real men.</summary>
        public const int GuardDiesPercent = 40;

        /// <summary>Days a guard who lived spends in a bed.</summary>
        public const int GuardBedDays = 9;

        /// <summary>
        /// The Boss's own detail, or null when he has none. One crew, his - the roster
        /// holds at most one because a man leads at most one.
        /// </summary>
        public static Crew DetailOf(Roster roster)
        {
            if (roster == null || roster.BossId < 0)
                return null;
            for (var i = 0; i < roster.Crews.Count; i++)
                if (roster.Crews[i].LieutenantId == roster.BossId)
                    return roster.Crews[i];
            return null;
        }

        /// <summary>
        /// Stands a detail up, or returns the one already standing. The Boss leads it
        /// himself; he is mechanically a lieutenant and this is his crew.
        /// </summary>
        public static Crew FormDetail(Roster roster)
        {
            if (roster == null)
                return null;

            var existing = DetailOf(roster);
            if (existing != null)
                return existing;

            var boss = roster.FindBoss();
            if (boss == null)
                return null;

            var detail = new Crew { Id = roster.NextCrewId(), LieutenantId = boss.Id };
            roster.Crews.Add(detail);
            return detail;
        }

        /// <summary>
        /// THE DON DOES NOT WALK OUT ALONE. The men who already answer directly to him -
        /// the ordinary hoods on no lieutenant's branch - fall in behind him, up to the
        /// number that can physically stand with a crew.
        ///
        /// It is a MOVE, not a new posting: the man he answers to is the Boss before and
        /// after, so his loyalty is not re-aimed the way it is when a hood changes
        /// lieutenants. Nothing else about him moves either - his wage, his gun and his
        /// place in the books are what they were.
        /// </summary>
        /// <returns>How many fell in.</returns>
        public static int FallIn(Roster roster)
        {
            var detail = FormDetail(roster);
            if (detail == null)
                return 0;

            var room = Crew.MaxTacticalHoods - detail.HoodIds.Count;
            var fell = 0;
            var direct = roster.Organization.BossHoodIds;
            for (var i = 0; i < direct.Count && fell < room; )
            {
                var man = roster.Find(direct[i]);
                // The man minding the FRONT is not a free man - he is on the desk, and
                // the books list him under the Boss only because he is on nobody's
                // branch. Taking him would leave the front unmanned and the roster
                // naming him in two places at once.
                if (man == null || man.Gone || man.Rank != Rank.Hood ||
                    man.Specialty != Specialty.None || roster.FrontId == man.Id ||
                    roster.CrewOf(man.Id) != null)
                {
                    i++;   // not a man who can stand with him; leave him where he is
                    continue;
                }
                direct.RemoveAt(i);
                detail.HoodIds.Add(man.Id);
                fell++;
            }

            return fell;
        }

        /// <summary>The guards on their feet, best first: the man most likely to stop
        /// something goes in front of the man least likely to.</summary>
        public static void Standing(Roster roster, List<Character> into)
        {
            if (into == null)
                return;
            into.Clear();

            var detail = DetailOf(roster);
            if (detail == null)
                return;

            for (var i = 0; i < detail.HoodIds.Count; i++)
            {
                var guard = roster.Find(detail.HoodIds[i]);
                if (guard != null && guard.Status == CharacterStatus.Active)
                    into.Add(guard);
            }

            // Combat first, nerve as the tiebreak, id last so the order never depends
            // on how the list happened to be built.
            into.Sort((a, b) =>
            {
                var byCombat = b.GetHalfSteps(CharacterAttribute.Combat)
                    .CompareTo(a.GetHalfSteps(CharacterAttribute.Combat));
                if (byCombat != 0)
                    return byCombat;
                var byNerve = Personality.Get(b, PersonalityTrait.Courage)
                    .CompareTo(Personality.Get(a, PersonalityTrait.Courage));
                return byNerve != 0 ? byNerve : a.Id.CompareTo(b.Id);
            });
        }

        /// <summary>
        /// A day of standing about behind the Don. Men on the detail are men not
        /// earning - they draw full wages and learn almost nothing, which is the price
        /// of the Boss being hard to kill, and the reason a fat detail is a real
        /// decision rather than a free one.
        /// </summary>
        /// <returns>How many stood the day.</returns>
        public static int DayOnDuty(Roster roster)
        {
            var detail = DetailOf(roster);
            if (detail == null)
                return 0;

            var stood = 0;
            for (var i = 0; i < detail.HoodIds.Count; i++)
            {
                var guard = roster.Find(detail.HoodIds[i]);
                if (guard == null || guard.Status != CharacterStatus.Active)
                    continue;
                if (ActivityXp.Award(guard, Activity.BodyguardDuty,
                        XpOutcome.Completed) > 0)
                    stood++;
            }
            return stood;
        }

        static readonly List<Character> Detail = new List<Character>();

        /// <summary>
        /// Somebody has come for the Boss. His detail is spent on it first, best man
        /// first: each in turn is asked for his nerve, and a man who fails it runs
        /// instead of standing - so a detail of cowards is a detail that is not there.
        /// A man who stands takes it, and either dies or spends a while in a bed.
        ///
        /// The attempt reaches the Boss only when there is nobody left in front of him.
        /// Whether that kills him is the caller's to resolve - this says who was in the
        /// way and what it cost.
        /// </summary>
        public static AssassinationOutcome Attempt(Roster roster, System.Random rng,
            int day, string where, List<Incident> incidents)
        {
            var boss = roster?.FindBoss();
            if (boss == null || boss.Gone || rng == null)
                return new AssassinationOutcome(false, 0);

            Standing(roster, Detail);
            var spent = 0;

            for (var i = 0; i < Detail.Count; i++)
            {
                var guard = Detail[i];

                // He is committed to violence like anybody else, and his nerve decides
                // whether he is actually in the way.
                if (PersonalityChecks.TryCourage(guard, rng, day, where, out var nerve))
                {
                    incidents?.Add(nerve);
                    if (nerve.Kind == IncidentKind.Fled)
                    {
                        RosterOps.Desert(roster, guard.Id);
                        spent++;
                    }
                    // Frozen or fled, he is not stopping anything: the next man is
                    // asked instead.
                    continue;
                }

                if (rng.Next(100) < GuardDiesPercent)
                {
                    RosterOps.Kill(roster, guard.Id);
                    incidents?.Add(new Incident(guard.Id, guard.FullName,
                        IncidentKind.DiedOnTheDetail, day, where, 0,
                        IncidentText.Line(IncidentKind.DiedOnTheDetail,
                            guard.FullName, where)));
                }
                else
                {
                    RosterOps.Hospitalize(roster, guard.Id, day + GuardBedDays,
                        "shot standing in front of the Don");
                    incidents?.Add(new Incident(guard.Id, guard.FullName,
                        IncidentKind.StoppedIt, day, where, 0,
                        IncidentText.Line(IncidentKind.StoppedIt, guard.FullName, where)));
                }

                Detail.Clear();
                return new AssassinationOutcome(false, spent + 1);
            }

            Detail.Clear();
            return new AssassinationOutcome(true, spent);
        }
    }
}
