using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>One man got better at one trade. A record, written when the rise
    /// happens, never re-derived - the ledger and the newspaper both print it.</summary>
    public readonly struct Improvement
    {
        public readonly int CharacterId;
        public readonly string Name;
        public readonly CharacterAttribute Attribute;

        /// <summary>Half-steps AFTER the rise.</summary>
        public readonly int HalfSteps;

        public Improvement(int characterId, string name, CharacterAttribute attribute,
            int halfSteps)
        {
            CharacterId = characterId;
            Name = name;
            Attribute = attribute;
            HalfSteps = halfSteps;
        }
    }

    /// <summary>
    /// How a man improves: he practises the trade he was sent to do, and the practice
    /// turns into stars. There is no experience pool and no level - a hood who spent a
    /// month collecting protection gets better at leaning on shopkeepers and no better
    /// at driving, which is both the fiction and the whole design pressure on how the
    /// player uses his roster.
    ///
    /// Deliberately free of randomness. The rolls belong to the jobs; the growth is an
    /// integer counter crossing an integer threshold, so a scripted run of days lands
    /// on an exactly assertable roster and the headless suite can say so.
    ///
    /// Conversion runs ONCE a day (OutfitDirector's day tick) rather than at the moment
    /// the points are earned: a man does not get better in the middle of the job he is
    /// doing, and a single daily pass is also the only place the wage bill can jump, so
    /// the payroll never changes under a page that is being read.
    /// </summary>
    public static class Practice
    {
        /// <summary>Points to reach half-step n. 1 star to 1.5 costs 6; 4.5 to 5 costs
        /// 20 - the last half-star is a career, the first is a fortnight.</summary>
        public static int CostOf(int halfSteps) => 2 * halfSteps;

        /// <summary>What the next half-step of this attribute costs him, or 0 when he
        /// is already at five stars and has nothing left to buy.</summary>
        public static int NextCost(Character member, CharacterAttribute attribute)
        {
            if (member == null)
                return 0;
            var at = member.GetHalfSteps(attribute);
            return at >= AttributeScale.MaxHalfSteps ? 0 : CostOf(at + 1);
        }

        /// <summary>
        /// Spends every man's banked practice as far as it goes, appending one
        /// <see cref="Improvement"/> per half-step gained. The dead and the deserted
        /// are skipped - they are off the books for everything - but the jailed and the
        /// hospitalized are not: a man learns things in there.
        /// </summary>
        public static void Convert(Roster roster, List<Improvement> into)
        {
            if (roster == null)
                return;

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Gone)
                    continue;

                for (var a = 0; a < AttributeScale.Count; a++)
                {
                    var attribute = (CharacterAttribute)a;
                    // A while, not an if: a big job can carry a man across two
                    // half-steps at once, and leaving the remainder banked for
                    // tomorrow would make the same points worth less than they are.
                    while (true)
                    {
                        var cost = NextCost(member, attribute);
                        if (cost <= 0 || member.GetPractice(attribute) < cost)
                            break;

                        member.SpendPractice(attribute, cost);
                        member.SetHalfSteps(attribute, member.GetHalfSteps(attribute) + 1);
                        into?.Add(new Improvement(member.Id, member.FullName, attribute,
                            member.GetHalfSteps(attribute)));
                    }
                }
            }
        }
    }
}
