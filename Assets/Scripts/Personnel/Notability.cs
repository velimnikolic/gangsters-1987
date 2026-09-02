using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>
    /// How much of the player's attention a man has earned.
    ///
    /// Every man has a name; nobody is Hood #184. But attention is finite, and a
    /// sixty-man roll read top to bottom every morning is a roll nobody reads. So the
    /// ledger sorts itself: a man rises when something happens to him and sinks again
    /// while nothing does, and the corner boy keeps his name and his line and simply
    /// waits at the bottom until he earns a look.
    ///
    /// The score is DERIVED, every time, by folding the man's own career log
    /// (<see cref="Career"/>) with decay and adding a standing floor for whatever marks
    /// the book has against him today (<see cref="ManFlags"/>). There is no counter
    /// anywhere that ticks: the same campaign history produces the same score, on any
    /// machine, after any save, in any order.
    ///
    /// It is read-only over the sim. Nothing here writes a field on anybody.
    ///
    /// Pure, integer, and free of UnityEngine.
    /// </summary>
    public static class Notability
    {
        /// <summary>
        /// How long a thing that happened goes on counting for. Eight weeks: long
        /// enough that a shootout in the spring still marks a man in the summer, short
        /// enough that a quiet month genuinely sinks him.
        ///
        /// The fade is straight rather than a curve. The sim carries no floats, a
        /// straight line is exactly reproducible in integers, and the difference
        /// between the two shapes is invisible against weights this coarse.
        /// </summary>
        public const int FadesInDays = 56;

        /// <summary>Inside this many days it is still THIS WEEK's news, and the roll
        /// says so with a tick beside his name.</summary>
        public const int FreshDays = 7;

        /// <summary>At or over this, the roll marks him. Set so that one real event -
        /// a promotion, a defection, a man who took a bullet for the Don - marks him
        /// on its own, and a standing flag holds him marked without one.</summary>
        public const int NewsBand = 30;

        // ---- what a standing mark is worth while it stands. A man who is ready for a
        // crew never sinks out of sight entirely: that is the whole point of the flag.
        public const int LieutenantMaterialFloor = 30;
        public const int HitmanMaterialFloor = 25;
        public const int RedFlagFloor = 40;

        /// <summary>
        /// What one thing that happened is worth, before it starts to fade. The scale
        /// is the design's own ordering of what matters: a man going over to another
        /// family is the loudest thing that can happen on this payroll, and a birthday
        /// that took half a star off him is the quietest.
        ///
        /// One table, and it is a config lever - re-tuning it changes what the ledger
        /// puts in front of the player and nothing else. Entries already written keep
        /// the weight they were stamped with (see <see cref="CareerEntry.Weight"/>), so
        /// a re-tune never rewrites a campaign's past.
        /// </summary>
        public static int WeightOf(IncidentKind kind)
        {
            switch (kind)
            {
                case IncidentKind.Defected: return 100;
                case IncidentKind.DiedOnTheDetail: return 90;
                case IncidentKind.StoppedIt: return 80;
                case IncidentKind.Promoted: return 70;
                case IncidentKind.Demoted: return 60;
                case IncidentKind.ReadyForACrew: return 50;
                case IncidentKind.NotToBeTrusted: return 50;
                case IncidentKind.CaughtSkimming: return 45;
                case IncidentKind.TookRivalMoney: return 45;
                case IncidentKind.AGunForHire: return 40;
                case IncidentKind.Fled: return 40;
                case IncidentKind.BearsWatching: return 35;
                case IncidentKind.Escalated: return 35;
                // GAN-245: a court is how a man becomes a name the judge has read -
                // which is exactly what lengthens a marked lieutenant's next sentence.
                case IncidentKind.CutLoose: return 85;
                case IncidentKind.Convicted: return 75;
                case IncidentKind.BailForfeit: return 55;
                case IncidentKind.Acquitted: return 55;
                case IncidentKind.CaseDismissed: return 45;
                case IncidentKind.WitnessKilled: return 45;
                case IncidentKind.CaseOpened: return 35;
                case IncidentKind.BailPosted: return 30;
                case IncidentKind.Froze: return 30;
                case IncidentKind.DemandedARaise: return 25;
                // A night with nothing in the envelope is the outfit's failure, not
                // his: it belongs on his file and it must not make a name of him.
                case IncidentKind.PayrollShort: return 20;
                case IncidentKind.Deviated: return 20;
                case IncidentKind.WitnessWithdrawn: return 20;
                // The shopkeeper's telephone call is news about the SHOP, not about a
                // man - nobody is made a name by being complained about.
                case IncidentKind.ComplaintRung: return 15;
                case IncidentKind.StatementTaken: return 15;
                default: return 15; // SlowingDown, and anything added later.
            }
        }

        /// <summary>What one entry still counts for, this many days on. Nothing before
        /// the day it happened - a fold run mid-campaign must not read the future - and
        /// nothing after it has faded out.</summary>
        public static int Remaining(int weight, int entryDay, int today)
        {
            var age = today - entryDay;
            if (age < 0 || age >= FadesInDays)
                return 0;
            return weight * (FadesInDays - age) / FadesInDays;
        }

        /// <summary>The standing floor his marks hold him at, whatever the fold
        /// says.</summary>
        public static int FloorFor(ManFlag flags)
        {
            var floor = 0;
            if ((flags & ManFlag.LieutenantMaterial) != 0 && LieutenantMaterialFloor > floor)
                floor = LieutenantMaterialFloor;
            if ((flags & ManFlag.HitmanMaterial) != 0 && HitmanMaterialFloor > floor)
                floor = HitmanMaterialFloor;
            if ((flags & ManFlag.RedFlag) != 0 && RedFlagFloor > floor)
                floor = RedFlagFloor;
            return floor;
        }

        /// <summary>
        /// One man's score today: everything on his file that has not yet faded, and
        /// then the floor his standing marks hold him at.
        ///
        /// The floor is a FLOOR and not a bonus. A lieutenant-material hood who also
        /// shot his way out of a warehouse last week reads at what the warehouse was
        /// worth, not at the two added together - the flag exists to stop him sinking,
        /// not to double-count what he did.
        /// </summary>
        public static int Of(Character man, int today)
        {
            if (man == null)
                return 0;

            var total = 0;
            for (var i = 0; i < man.Career.Count; i++)
            {
                var entry = man.Career[i];
                total += Remaining(entry.Weight, entry.Day, today);
            }

            var floor = FloorFor(ManFlags.Of(man));
            return total > floor ? total : floor;
        }

        /// <summary>The day the last thing happened to him; -1 for a man nothing ever
        /// has.</summary>
        public static int LastDay(Character man)
        {
            if (man == null)
                return -1;
            var last = -1;
            for (var i = 0; i < man.Career.Count; i++)
                if (man.Career[i].Day > last)
                    last = man.Career[i].Day;
            return last;
        }

        /// <summary>Something happened to him inside the week.</summary>
        public static bool Fresh(Character man, int today)
        {
            var last = LastDay(man);
            return last >= 0 && today - last < FreshDays && today >= last;
        }

        /// <summary>The roll marks him.</summary>
        public static bool Marked(Character man, int today) => Of(man, today) >= NewsBand;

        /// <summary>
        /// WHY he is marked, in the words his own file already used: the last thing
        /// that happened to him. Empty for a man nothing ever has - a mark with no
        /// cause behind it is a mark the player learns to ignore, so the page that
        /// draws one asks for this beside it.
        /// </summary>
        public static string Cause(Character man)
        {
            if (man == null || man.Career.Count == 0)
                return "";
            return man.Career[man.Career.Count - 1].Line;
        }

        /// <summary>
        /// How his figure is moving: what a week has done to it. A man at ninety
        /// falling and a man at ninety rising are different problems, and both facts
        /// are already in the fold - a second fold a week back costs nothing and
        /// invents nothing.
        ///
        /// Positive is rising. Nothing is stored anywhere: run it twice on the same
        /// history and it answers the same.
        /// </summary>
        public static int Trend(Character man, int today) =>
            Of(man, today) - Of(man, today - FreshDays);

        /// <summary>
        /// The men worth looking at this morning, most notable first - the plain
        /// descending sort by <see cref="Of"/>, with the id breaking a tie so the same
        /// roster always answers in the same order.
        ///
        /// Men off the books are left out: the record keeps their line, but nobody is
        /// going to do anything about them today.
        /// </summary>
        public static void Top(Roster roster, int today, int count, List<Character> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (roster == null || count <= 0)
                return;

            // Scored ONCE per man and then sorted on the figures. The fold walks a
            // whole file every time it is asked, and a comparison that called it would
            // walk sixty files a hundred times to order sixty men.
            Ranking.Clear();
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                if (man != null && !man.Gone)
                    Ranking.Add(new Standing(man, Of(man, today)));
            }

            Ranking.Sort((a, b) =>
            {
                var byScore = b.Score.CompareTo(a.Score);
                return byScore != 0 ? byScore : a.Man.Id.CompareTo(b.Man.Id);
            });

            var take = Ranking.Count < count ? Ranking.Count : count;
            for (var i = 0; i < take; i++)
                into.Add(Ranking[i].Man);
            Ranking.Clear();
        }

        readonly struct Standing
        {
            public readonly Character Man;
            public readonly int Score;

            public Standing(Character man, int score)
            {
                Man = man;
                Score = score;
            }
        }

        static readonly List<Standing> Ranking = new List<Standing>();
    }

    /// <summary>
    /// Today's scores for a whole roster, worked out once and read many times.
    ///
    /// The ledger asks for a man's notability once per row per repaint, and the fold
    /// walks his whole file each time; a board keeps that off the paint path without
    /// putting a counter anywhere that could drift, because the board is thrown away
    /// and rebuilt rather than maintained. Rebuild it and it agrees with
    /// <see cref="Notability.Of"/> exactly, always.
    /// </summary>
    public sealed class NotabilityBoard
    {
        readonly Dictionary<int, int> scores = new Dictionary<int, int>();
        readonly Dictionary<int, bool> fresh = new Dictionary<int, bool>();

        /// <summary>The day these figures were worked out for. -1 before the first
        /// rebuild, which reads as a board that scores everyone at nothing - a page
        /// painted before the first day tick sorts by the roster's own order, which is
        /// what it did before this layer existed.</summary>
        public int Day { get; private set; } = -1;

        public void Rebuild(Roster roster, int day)
        {
            scores.Clear();
            fresh.Clear();
            Day = day;
            if (roster == null)
                return;

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                scores[man.Id] = Notability.Of(man, day);
                fresh[man.Id] = Notability.Fresh(man, day);
            }
        }

        public int ScoreOf(int characterId) =>
            scores.TryGetValue(characterId, out var score) ? score : 0;

        public bool Marked(int characterId) => ScoreOf(characterId) >= Notability.NewsBand;

        public bool Fresh(int characterId) =>
            fresh.TryGetValue(characterId, out var recent) && recent;
    }
}
