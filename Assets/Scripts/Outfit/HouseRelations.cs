using System.Collections.Generic;

namespace LivingCity.Outfit
{
    /// <summary>What one house did to another that it is owed for (D14).</summary>
    public enum GrievanceKind
    {
        /// <summary>They put hands on a door we are paid to keep the peace at.</summary>
        DoorAttacked,

        /// <summary>A door that paid us now pays them.</summary>
        DoorSwitched,

        /// <summary>Our round was taken off our men by theirs.</summary>
        RoundLost,

        /// <summary>They killed one of ours.</summary>
        ManKilled,

        /// <summary>We warned them off and they carried on.</summary>
        WarningIgnored,

        /// <summary>They stopped paying what they owe us.</summary>
        TributeUnpaid,

        /// <summary>They have one of ours in a cellar somewhere (RIVAL-009 step 6).
        /// </summary>
        ManTaken,
    }

    /// <summary>
    /// The design's nine steps (§26), by how much one house is owed by another. A house
    /// never skips a step; the ladder is what makes a war something the player watches
    /// coming rather than something that happens to him.
    /// </summary>
    public enum LadderStep
    {
        Ignore,
        DiplomaticWarning,
        Threat,
        DemandCompensation,
        RetakeBusiness,
        BeatCollector,
        AttackBusiness,
        KidnapCrewMember,
        KillCrewMember,
    }

    /// <summary>
    /// EVERY NUMBER BETWEEN TWO HOUSES, in one place (D13, D14, D15, D22). Never a
    /// literal in a method.
    /// </summary>
    public sealed class HouseRelationsConfig
    {
        public static readonly HouseRelationsConfig Default = new HouseRelationsConfig();

        /// <summary>D13. A grudge fades on its own; it takes fifty days of quiet to
        /// forget a killing.</summary>
        public float GrievanceDecayPerDay = 2f;

        /// <summary>D13. What each step of the ladder costs in grievance.</summary>
        public int DiplomaticWarningAt = 10;
        public int ThreatAt = 20;
        public int DemandCompensationAt = 30;
        public int RetakeBusinessAt = 40;
        public int BeatCollectorAt = 50;
        public int AttackBusinessAt = 60;
        public int KidnapCrewMemberAt = 70;
        public int KillCrewMemberAt = 80;

        /// <summary>D14. What each thing one house does to another is worth.</summary>
        public int DoorAttacked = 15;
        public int DoorSwitched = 10;
        public int RoundLost = 20;
        public int ManKilled = 35;
        public int WarningIgnored = 10;
        public int TributeUnpaid = 25;

        /// <summary>RIVAL-009 step 6's own figure. A man in their hands is worth what an
        /// unpaid levy is worth: an insult with a price on it.</summary>
        public int ManTaken = 25;

        /// <summary>D15. A house declares war only if it can pay its men through one,
        /// and sues for peace when it cannot or when it has lost too many.</summary>
        public int MinWarDays = 14;
        public int LossesToSueForPeace = 3;

        /// <summary>D22. A truce becomes peace again when both sides have stayed under
        /// this for this long, and a warning ignored this long is a grievance.</summary>
        public int PeaceGrievance = 20;
        public int PeaceAfterDays = 7;
        public float WarningHours = 48f;

        public int AmountOf(GrievanceKind kind)
        {
            switch (kind)
            {
                case GrievanceKind.DoorAttacked: return DoorAttacked;
                case GrievanceKind.DoorSwitched: return DoorSwitched;
                case GrievanceKind.RoundLost: return RoundLost;
                case GrievanceKind.ManKilled: return ManKilled;
                case GrievanceKind.WarningIgnored: return WarningIgnored;
                case GrievanceKind.ManTaken: return ManTaken;
                default: return TributeUnpaid;
            }
        }

        /// <summary>The step a house is at, by what it is owed. Monotone by
        /// construction: a bigger grudge is never a smaller step.</summary>
        public LadderStep StepFor(float grievance)
        {
            if (grievance >= KillCrewMemberAt) return LadderStep.KillCrewMember;
            if (grievance >= KidnapCrewMemberAt) return LadderStep.KidnapCrewMember;
            if (grievance >= AttackBusinessAt) return LadderStep.AttackBusiness;
            if (grievance >= BeatCollectorAt) return LadderStep.BeatCollector;
            if (grievance >= RetakeBusinessAt) return LadderStep.RetakeBusiness;
            if (grievance >= DemandCompensationAt) return LadderStep.DemandCompensation;
            if (grievance >= ThreatAt) return LadderStep.Threat;
            if (grievance >= DiplomaticWarningAt) return LadderStep.DiplomaticWarning;
            return LadderStep.Ignore;
        }
    }

    /// <summary>
    /// WHERE EVERY HOUSE STANDS WITH EVERY OTHER, and what each is owed by each.
    ///
    /// A STANCE is symmetric and belongs to the PAIR: two families are at war with each
    /// other or they are not, and the street reads one answer for both of them. A
    /// GRIEVANCE is directed: the first argument is always the house holding the grudge.
    /// One house may be owed a great deal by another that is owed nothing back.
    ///
    /// A stance change never lands mid-day. It is stored as PENDING and applied at
    /// midnight - orders in flight were priced under the old rules, and a war declared
    /// on Tuesday afternoon would reprice a Tuesday morning plan behind the player's
    /// back.
    ///
    /// Pure. One per city, on <see cref="Underworld"/>.
    /// </summary>
    public sealed class HouseRelations
    {
        readonly Dictionary<long, Stance> stances = new Dictionary<long, Stance>();
        readonly Dictionary<long, Stance> pending = new Dictionary<long, Stance>();
        readonly Dictionary<long, float> grievances = new Dictionary<long, float>();

        /// <summary>Since when both sides of a truce have been under PeaceGrievance.
        /// Negative while one of them is not.</summary>
        readonly Dictionary<long, int> quietSince = new Dictionary<long, int>();

        public HouseRelations(HouseRelationsConfig config = null) =>
            Config = config ?? HouseRelationsConfig.Default;

        public HouseRelationsConfig Config { get; }

        /// <summary>The stance's key: the unordered pair, so the two houses can never
        /// disagree about whether they are at war.</summary>
        static long Pair(int a, int b) =>
            a <= b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        /// <summary>The grievance's key: ordered, because being owed is not mutual.
        /// </summary>
        static long Owed(int aggrieved, int offender) =>
            ((long)aggrieved << 32) | (uint)offender;

        /// <summary>Everyone starts at Peace - a city of families who have not fallen
        /// out yet.</summary>
        public Stance StanceBetween(int a, int b) =>
            a == b ? Stance.Peace
                   : stances.TryGetValue(Pair(a, b), out var stance) ? stance : Stance.Peace;

        public bool TryGetPending(int a, int b, out Stance stance) =>
            pending.TryGetValue(Pair(a, b), out stance) & (a != b);

        /// <summary>Setting the pending stance back to the current one withdraws the
        /// change - "never mind" must be expressible before midnight seals it.</summary>
        public void SetPending(int a, int b, Stance stance)
        {
            if (a == b)
                return;
            var key = Pair(a, b);
            if (stance == StanceBetween(a, b))
                pending.Remove(key);
            else
                pending[key] = stance;
        }

        /// <summary>Midnight. Every pending stance lands at once, for everybody.
        /// </summary>
        public void ApplyPending()
        {
            foreach (var entry in pending)
                stances[entry.Key] = entry.Value;
            pending.Clear();
        }

        public float Grievance(int aggrieved, int offender) =>
            aggrieved == offender ? 0f
                : grievances.TryGetValue(Owed(aggrieved, offender), out var value)
                    ? value
                    : 0f;

        /// <summary>They did something to us. The first argument is always the house
        /// that holds the grudge.</summary>
        public void Note(int aggrieved, int offender, GrievanceKind kind)
        {
            if (aggrieved == offender || aggrieved < 0 || offender < 0)
                return;
            var key = Owed(aggrieved, offender);
            grievances.TryGetValue(key, out var value);
            value += Config.AmountOf(kind);
            grievances[key] = value > 100f ? 100f : value;
            quietSince.Remove(Pair(aggrieved, offender));
        }

        public LadderStep StepOf(int aggrieved, int offender) =>
            Config.StepFor(Grievance(aggrieved, offender));

        /// <summary>
        /// A day of quiet. Every grudge fades a little, and a truce whose two sides have
        /// both stayed under PeaceGrievance long enough becomes peace again (D22).
        /// </summary>
        public void DayTick(int day)
        {
            if (grievances.Count > 0)
            {
                var keys = new List<long>(grievances.Keys);
                for (var i = 0; i < keys.Count; i++)
                {
                    var value = grievances[keys[i]] - Config.GrievanceDecayPerDay;
                    if (value <= 0f)
                        grievances.Remove(keys[i]);
                    else
                        grievances[keys[i]] = value;
                }
            }

            if (stances.Count == 0)
                return;

            var pairs = new List<long>(stances.Keys);
            for (var i = 0; i < pairs.Count; i++)
            {
                if (stances[pairs[i]] != Stance.Truce)
                {
                    quietSince.Remove(pairs[i]);
                    continue;
                }

                var a = (int)(pairs[i] >> 32);
                var b = (int)(pairs[i] & 0xFFFFFFFF);
                if (Grievance(a, b) >= Config.PeaceGrievance ||
                    Grievance(b, a) >= Config.PeaceGrievance)
                {
                    quietSince.Remove(pairs[i]);
                    continue;
                }

                if (!quietSince.TryGetValue(pairs[i], out var since))
                {
                    quietSince[pairs[i]] = day;
                    continue;
                }

                if (day - since >= Config.PeaceAfterDays)
                {
                    stances[pairs[i]] = Stance.Peace;
                    quietSince.Remove(pairs[i]);
                }
            }
        }

        /// <summary>How many days a house could pay its men through a war (D15).
        /// </summary>
        public static int Endurance(int safe, int dailyPayroll) =>
            safe / (dailyPayroll > 0 ? dailyPayroll : 1);

        /// <summary>
        /// What house A believes house B could last. Nobody reads another family's
        /// books: the true figure comes back through a deterministic haze between 0.7
        /// and 1.3 (D15), the same haze every time for the same day and pair, so a mind
        /// cannot shake a better answer out of it by asking twice.
        /// </summary>
        public static int Estimate(int trueDays, int citySeed, int day, int a, int b)
        {
            var mix = Personnel.Potential.Mix(
                Personnel.Potential.Mix(citySeed, day), a * 31 + b);
            var factor = 0.7f + (mix & 0xFFFF) / 65535f * 0.6f;
            var seen = (int)(trueDays * factor);
            return seen < 0 ? 0 : seen;
        }
    }
}
