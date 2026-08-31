using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    /// <summary>What came of one job: the answer, the money that moved, the attention
    /// it drew, and whoever it put in a hospital bed.</summary>
    public readonly struct JobOutcome
    {
        public readonly OrderOutcome Outcome;

        /// <summary>What it paid. Zero on a failure - nothing was delivered.</summary>
        public readonly int Payout;

        /// <summary>What the attempt cost, paid either way: a bribe that bought nothing
        /// is still a bribe that was paid. Kept apart from the payout rather than netted
        /// so the balance sheet can show where the money went.</summary>
        public readonly int Cost;

        public readonly int Heat;

        /// <summary>The man his own dynamite or his own fire put in a bed; -1 when
        /// everyone walked away.</summary>
        public readonly int CasualtyId;

        /// <summary>The man a Recruit order brought back; -1 when none. Reported rather
        /// than left for the caller to notice: adding a name to the books moves the
        /// roster, and a roster that moves without the personnel version moving leaves
        /// the ledger on a stale page and the street with a man it never dealt a body.</summary>
        public readonly int RecruitedId;

        public int Money => Payout - Cost;

        public JobOutcome(OrderOutcome outcome, int payout, int cost, int heat,
            int casualtyId, int recruitedId = -1)
        {
            Outcome = outcome;
            Payout = payout;
            Cost = cost;
            Heat = heat;
            CasualtyId = casualtyId;
            RecruitedId = recruitedId;
        }
    }

    /// <summary>
    /// What a job comes to when the hours are done. The whole point of the attribute
    /// sheet: until this class existed the eleven stats decided a warning line on the
    /// job card and nothing else, and a five-star arsonist burned a shop exactly as
    /// well as a man who had never held a match.
    ///
    /// Pure and free of UnityEngine so the headless suite can run a scripted campaign
    /// and assert the books to the dollar. Every roll comes off a caller-supplied
    /// System.Random, and OutfitDirector seeds one per job from (city seed, day, job
    /// id) through <see cref="Mix"/> - never one stream shared across jobs, whose draw
    /// order would then depend on how many crews happened to report first.
    /// </summary>
    public static class OrderResolution
    {
        /// <summary>A man exactly at the job's stated floor comes off with it about a
        /// third of the time. Competence is the floor, not the finish.</summary>
        public const float BaseChance = 0.35f;

        /// <summary>Each half-step over the floor. A full star over is 0.55, two over
        /// 0.75 - the band the whole star scale was drawn to be read against.</summary>
        public const float ChancePerHalfStep = 0.10f;

        public const float MinChance = 0.05f;
        public const float MaxChance = 0.95f;

        /// <summary>Orders that state no requirement still resolve against two stars,
        /// so a hopeless man is never as good as a capable one at anything.</summary>
        public const int ImplicitFloorHalfSteps = 4;

        /// <summary>Charged for every open job past what a lieutenant can hold in his
        /// head - a scattered boss botches the tail of his list.</summary>
        public const float DepthPenalty = 0.10f;

        /// <summary>Chance that a botched fire or a botched charge hurts the man who
        /// set it.</summary>
        public const float MisfireChance = 0.25f;

        /// <summary>Days in a bed. Long enough to cost the player the man for real,
        /// short enough that it is not a death sentence dressed up.</summary>
        public const int MisfireDays = 6;

        /// <summary>A quiet job draws nothing at all; a merely careful one draws half.
        /// 3.5 stars - the same bar the sheet already uses for its harder floors.</summary>
        public const int QuietHalfSteps = 7;

        public static int FloorOf(in OrderSpec spec) =>
            spec.PrimaryFloorHalfSteps > 0 ? spec.PrimaryFloorHalfSteps
                                           : ImplicitFloorHalfSteps;

        /// <summary>
        /// The odds the job card quotes and the roll uses - one function, so the number
        /// the player was shown is the number he was judged by.
        ///
        /// depth is how far down the lieutenant's book this job sat when it came up,
        /// and organizationHalfSteps how many he can carry: everything past that many
        /// open jobs is work he is running on memory, and it costs.
        /// </summary>
        public static float ChanceFor(in OrderSpec spec, int statHalfSteps, int depth,
            int organizationHalfSteps)
        {
            var over = statHalfSteps - FloorOf(spec);
            var chance = BaseChance + ChancePerHalfStep * over;

            var beyond = depth - organizationHalfSteps;
            if (beyond > 0)
                chance -= DepthPenalty * beyond;

            if (chance < MinChance)
                return MinChance;
            return chance > MaxChance ? MaxChance : chance;
        }

        /// <summary>
        /// What a stat is worth to a job's takings: 0.8 at one star, 1.3 at five. The
        /// same band for every scaled figure in the game on purpose - a player who has
        /// learned what a five-star man is worth once has learned it everywhere.
        /// </summary>
        public static float YieldScale(int halfSteps)
        {
            var t = (AttributeScale.Clamp(halfSteps) - AttributeScale.MinHalfSteps) /
                    (float)(AttributeScale.MaxHalfSteps - AttributeScale.MinHalfSteps);
            return 0.8f + 0.5f * t;
        }

        /// <summary>Money the job pays when it comes off, the crew's best man at its own
        /// trade scaling the take.</summary>
        public static int PayoutFor(
            in OrderSpec spec, int targetCount, int statHalfSteps, int unitWorth = 0)
        {
            // What the target is actually worth beats the book figure. A round of
            // collections off ten barbers and a round off a nightclub were the same
            // sixty dollars while this read a constant.
            var unit = unitWorth > 0 ? unitWorth : spec.Payout;
            if (unit <= 0)
                return 0;
            if (targetCount < 1)
                targetCount = 1;
            return (int)(unit * targetCount * YieldScale(statHalfSteps));
        }

        /// <summary>
        /// What the attempt costs, before it is known whether it worked. A clever man
        /// buys the same policeman cheaper, and a businessman fits out the same premises
        /// for less: 8 % off per star over the floor for the influence orders, 5 % for
        /// the building work. Never below a third of the book price - everybody's money
        /// is worth something.
        /// </summary>
        public static int CostFor(
            in OrderSpec spec, int targetCount, int statHalfSteps, int unitWorth = 0)
        {
            // Buying premises reads the asking price of THOSE premises when the caller
            // knows it; everything else is priced by the book.
            var book = spec.Type == OrderType.BuyPremises && unitWorth > 0
                ? unitWorth
                : spec.Cost;
            if (book <= 0)
                return 0;
            if (targetCount < 1)
                targetCount = 1;

            var discountPerHalfStep = spec.Category == OrderCategory.Influence ? 0.08f
                                    : spec.Category == OrderCategory.Business ? 0.05f
                                    : 0f;
            var over = statHalfSteps - FloorOf(spec);
            var factor = 1f - discountPerHalfStep * (over > 0 ? over : 0);
            if (factor < 0.33f)
                factor = 0.33f;
            return (int)(book * targetCount * factor);
        }

        /// <summary>
        /// Police attention the job leaves behind. A crew with a quiet man on it works
        /// at half the noise; a killing done in the dark is not heard at all, which is
        /// the one thing that makes Stealth worth a slot on a violent roster.
        ///
        /// Stealth alone now. The rule used to average Stealth with Knives, and Knives
        /// merged into Combat with every other violent trade - reading Combat here
        /// would have made the loudest man on the books the quietest killer.
        /// </summary>
        public static int HeatFor(in OrderSpec spec, int targetCount, int stealthHalfSteps)
        {
            if (spec.Heat <= 0)
                return 0;
            if (targetCount < 1)
                targetCount = 1;

            var heat = spec.Heat * targetCount;
            if (spec.Type == OrderType.Kill && stealthHalfSteps >= QuietHalfSteps)
                return 0;
            return stealthHalfSteps >= QuietHalfSteps ? heat / 2 : heat;
        }

        /// <summary>
        /// Resolves a finished job. The crew's best man at the job's own trade carries
        /// it - an outfit sends the man who can do the thing - and the money moves
        /// whichever way the roll went, because the attempt was paid for either way.
        ///
        /// streetOutcome is how the sim answered a Violence job that played out on the
        /// road; a Roll job passes null and is decided here.
        /// </summary>
        public static JobOutcome Resolve(in OrderSpec spec, Job job, Roster roster,
            Crew crew, System.Random rng, OrderOutcome? streetOutcome = null)
        {
            var targets = job?.TargetCount ?? 1;
            var stat = CrewKit.BestAt(roster, crew, spec.PrimaryAttribute);
            var cost = CostFor(spec, targets, stat, job.TargetWorth);

            OrderOutcome outcome;
            if (streetOutcome.HasValue)
            {
                outcome = streetOutcome.Value;
            }
            else
            {
                var depth = job != null ? job.BookDepth : 0;
                var organization = CrewKit.BestAt(roster, crew, CharacterAttribute.Organization);
                var chance = ChanceFor(spec, stat, depth, organization);
                outcome = rng != null && rng.NextDouble() < chance
                    ? OrderOutcome.Completed
                    : OrderOutcome.Failed;
            }

            var completed = outcome == OrderOutcome.Completed;
            var payout = completed ? PayoutFor(spec, targets, stat, job.TargetWorth) : 0;
            var heat = HeatFor(spec, targets,
                CrewKit.BestAt(roster, crew, CharacterAttribute.Stealth));

            var recruited = completed && spec.Type == OrderType.Recruit
                ? BringHimIn(roster, crew, rng, stat)
                : -1;

            var casualty = Misfire(spec, job, roster, crew, rng, completed);
            return new JobOutcome(outcome, payout, cost, heat, casualty, recruited);
        }

        /// <summary>
        /// A Recruit order that came off brings a man back with it. He joins the crew
        /// that went looking. Organization capacity is soft, so a completed recruit is
        /// never discarded merely because his lieutenant is already overloaded.
        ///
        /// The recruiter's own Awareness rides along: a sharp man knows a promising
        /// corner boy when he sees one (RosterSeeder.Recruit), which is the only thing
        /// that lifts a recruit above his three-star ceiling.
        /// </summary>
        static int BringHimIn(Roster roster, Crew crew, System.Random rng, int stat)
        {
            if (roster == null || rng == null)
                return -1;

            var member = RosterSeeder.Recruit(roster, rng, stat);
            if (crew != null)
                RosterOps.AssignToCrew(roster, member.Id, crew.Id);
            return member.Id;
        }

        /// <summary>
        /// Fire and dynamite punish amateurs. A botched Torch or Bomb run by a crew
        /// whose best man is under the stated floor may put one of them in a bed -
        /// checked only on a failure, because a charge that went off where it was meant
        /// to went off correctly by definition.
        ///
        /// The two orders are named outright rather than read off the primary skill:
        /// torch and powder work are Combat like every other violent trade now, and a
        /// skill test would put a raid and a kidnapping on the same hook.
        /// </summary>
        static int Misfire(in OrderSpec spec, Job job, Roster roster, Crew crew,
            System.Random rng, bool completed)
        {
            if (completed || rng == null || job == null)
                return -1;
            if (spec.Type != OrderType.Torch && spec.Type != OrderType.Bomb)
                return -1;
            if (CrewKit.BestAt(roster, crew, spec.PrimaryAttribute) >= FloorOf(spec))
                return -1;
            if (rng.NextDouble() >= MisfireChance)
                return -1;

            CrewKit.MenOnJob(roster, crew, job.Men, Scratch);
            return Scratch.Count == 0 ? -1 : Scratch[rng.Next(Scratch.Count)];
        }

        /// <summary>
        /// Banks the job's lesson with every man who was on it, through the one table
        /// that says what work teaches. The order's TYPE decides the lesson, not its
        /// primary skill: a month of collecting protection makes a man better at
        /// leaning on shopkeepers and at reading a street, and no better at driving.
        ///
        /// No number lives here. What a night is worth is <see cref="ActivityXp"/>'s
        /// to say and XP-004's to tune, in one place.
        /// </summary>
        public static void AwardPractice(in OrderSpec spec, Roster roster, Crew crew,
            int men, XpOutcome outcome)
        {
            if (roster == null || crew == null)
                return;

            var activity = OrderTable.ActivityOf(spec.Type);
            CrewKit.MenOnJob(roster, crew, men, Scratch);
            for (var i = 0; i < Scratch.Count; i++)
                ActivityXp.Award(roster.Find(Scratch[i]), activity, outcome);
        }

        /// <summary>One buffer for the two places that need a crew's headcount listed.
        /// Static because this class is static and single-threaded by construction -
        /// the day tick and the job finish both run on the main thread, one after the
        /// other, and neither holds the list across a call.</summary>
        static readonly List<int> Scratch = new List<int>();

        /// <summary>
        /// What the clerk writes under HURT on the roll. Dealt from the job's own rng so
        /// the same night always produces the same note - a wound that re-rolled every
        /// repaint would be a man whose ribs healed and broke as the page redrew.
        ///
        /// Free text rather than an injury enum: the column exists to say what an enum
        /// cannot, and a table of phrases is honest about being flavour laid over the
        /// one fact the sim owns, which is the day he is back.
        /// </summary>
        public static string InjuryNote(System.Random rng)
        {
            var hurt = Hurts[rng.Next(Hurts.Length)];
            var where = Wards[rng.Next(Wards.Length)];
            return hurt + " \u00B7 " + where;
        }

        static readonly string[] Hurts =
        {
            "2 ribs", "3 ribs", "cracked wrist", "shoulder", "knee",
            "collarbone", "jaw wired", "burns to the hands", "shot in the thigh",
            "shot through the arm", "concussion", "ruptured spleen",
        };

        static readonly string[] Wards =
        {
            "county ward", "St Brendan's", "the back room", "Mercy General",
            "a doctor who asks nothing",
        };

        /// <summary>
        /// Avalanches (seed, day, job) before it reaches System.Random, whose nearby
        /// seeds produce visibly correlated first draws - without it, two jobs issued
        /// the same day would tend to answer the same way. Fingerprint mix from xxHash,
        /// the same one the newspaper's editions are dealt through.
        /// </summary>
        public static int Mix(int seed, int day, int jobId)
        {
            unchecked
            {
                var h = (uint)seed * 2654435761u + (uint)day * 2246822519u +
                        (uint)jobId * 3266489917u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return (int)h;
            }
        }
    }
}
