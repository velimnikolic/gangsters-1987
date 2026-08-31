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
    /// And he only ever gets as good as he was born to be. The design writes growth as
    /// a shrinking GAIN - gain = base x (1 - current/potential)^1.5 - but this codebase
    /// banks integer practice and buys half-steps at the day tick, so the same curve is
    /// inverted into a rising PRICE: the closer a man is to his hidden ceiling, the more
    /// the next half-step costs him, and at the ceiling it costs nothing because there
    /// is nothing left to buy.
    ///
    /// Deliberately free of randomness AND of floats. The rolls belong to the jobs; the
    /// growth is an integer counter crossing an integer threshold, so a scripted run of
    /// days lands on an exactly assertable roster and the headless suite can say so.
    ///
    /// Conversion runs ONCE a day (OutfitDirector's day tick) rather than at the moment
    /// the points are earned: a man does not get better in the middle of the job he is
    /// doing, and a single daily pass is also the only place the wage bill can jump, so
    /// the payroll never changes under a page that is being read.
    /// </summary>
    public static class Practice
    {
        /// <summary>What the headroom multipliers below are scaled by, so the table can
        /// be integers.</summary>
        public const int HeadroomScale = 1_000;

        /// <summary>
        /// The price of the next half-step, as a multiple of the base cost, by the
        /// man's ceiling (row) and where he stands now (column). Each entry is
        /// <c>(potential / (potential - current))^1.5 x 1000</c>, which is the design's
        /// <c>(1 - current/potential)^-1.5</c> written in half-steps - rounded once,
        /// here, and stored, so the sim path never sees a float and two machines can
        /// never disagree about a rounding.
        ///
        /// Rows are potential 2..10, columns current 2..10. A zero means he is at or
        /// past that ceiling and the step is not for sale.
        /// </summary>
        static readonly int[] Headroom =
        {
            //  c=2     3       4       5       6       7       8       9      10
            0,      0,      0,      0,      0,      0,      0,      0,      0,  // p=2
            5196,   0,      0,      0,      0,      0,      0,      0,      0,  // p=3
            2828,   8000,   0,      0,      0,      0,      0,      0,      0,  // p=4
            2152,   3953,   11180,  0,      0,      0,      0,      0,      0,  // p=5
            1837,   2828,   5196,   14697,  0,      0,      0,      0,      0,  // p=6
            1657,   2315,   3564,   6548,   18520,  0,      0,      0,      0,  // p=7
            1540,   2024,   2828,   4355,   8000,   22627,  0,      0,      0,  // p=8
            1458,   1837,   2415,   3375,   5196,   9546,   27000,  0,      0,  // p=9
            1398,   1707,   2152,   2828,   3953,   6086,   11180,  31623,  0,  // p=10
        };

        const int HeadroomStride = AttributeScale.MaxHalfSteps - AttributeScale.MinHalfSteps + 1;

        /// <summary>The base price of half-step n, before the ceiling is taken into
        /// account: 1 star to 1.5 costs 6, 4.5 to 5 costs 20.</summary>
        public static int CostOf(int halfSteps) => 2 * halfSteps;

        /// <summary>
        /// What half-step <paramref name="halfSteps"/> really costs a man whose ceiling
        /// is <paramref name="potentialHalfSteps"/>, rounded up - nobody buys a star
        /// with a fraction of a point. Zero when the step is at or beyond his ceiling.
        ///
        /// The shape, measured: a five-star-capable man climbs 1 to 2.5 stars for 45
        /// points and pays 633 for his last half-step alone - more than the whole climb
        /// that got him there. A man capped at two and a half stars pays 112 for his
        /// last one and stops there for good.
        /// </summary>
        public static int CostOf(int halfSteps, int potentialHalfSteps)
        {
            var current = halfSteps - 1;
            var ceiling = AttributeScale.Clamp(potentialHalfSteps);
            if (current < AttributeScale.MinHalfSteps ||
                halfSteps > AttributeScale.MaxHalfSteps || current >= ceiling)
                return 0;

            var headroom = Headroom[
                (ceiling - AttributeScale.MinHalfSteps) * HeadroomStride +
                (current - AttributeScale.MinHalfSteps)];
            if (headroom <= 0)
                return 0;

            var scaled = (long)CostOf(halfSteps) * headroom;
            return (int)((scaled + HeadroomScale - 1) / HeadroomScale);
        }

        /// <summary>What the next half-step of this attribute costs him, or 0 when he
        /// has reached his ceiling and has nothing left to buy - which is the only
        /// thing the player ever sees of the ceiling: a man who stops getting better.</summary>
        public static int NextCost(Character member, CharacterAttribute attribute)
        {
            if (member == null)
                return 0;
            var at = member.GetHalfSteps(attribute);
            return at >= AttributeScale.MaxHalfSteps
                ? 0
                : CostOf(at + 1, member.PotentialHalfSteps(attribute));
        }

        /// <summary>
        /// Spends every man's banked practice as far as it goes, appending one
        /// <see cref="Improvement"/> per half-step gained. The dead and the deserted
        /// are skipped - they are off the books for everything - but the jailed and the
        /// hospitalized are not: a man learns things in there.
        ///
        /// Practice banked against a trade he has already finished is thrown away here
        /// rather than kept: a man cannot pre-pay past his own ceiling, and a bank that
        /// sat there growing would buy a free half-step the day some future event lifted
        /// the cap.
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
                        if (cost <= 0)
                        {
                            member.SpendPractice(attribute, member.GetPractice(attribute));
                            break;
                        }
                        if (member.GetPractice(attribute) < cost)
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
