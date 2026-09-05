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

        /// <summary>They lead the block next to ours and there is nowhere else left
        /// to grow (AI-007, ruling A13: "i sama granica"). Filed once a day per
        /// bordering block, and capped - geography alone never starts a war.</summary>
        BorderPressure,

        // ------------------------------------------------------------- EPIC 42
        // The table's own grudges. Appended, and every one named in AmountOf below:
        // its default branch answers TributeUnpaid for a kind it was not told about.

        /// <summary>They put a figure to us that was an insult (a haggle lost).</summary>
        InsultingOffer,

        /// <summary>They took product on credit and did not pay on the day.</summary>
        DebtUnpaid,

        /// <summary>They crossed a line both houses had agreed to keep.</summary>
        LineCrossed,

        /// <summary>They left us to a war they had sworn to stand in.</summary>
        PactBroken,

        /// <summary>They killed the man we sent to sit down with them.</summary>
        SitDownBetrayed,
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

        /// <summary>
        /// A18, the user's number of 2026-09-04: four a day PER BORDERING BLOCK, so a
        /// squeezed house escalates and a house with one neighbour barely does (one
        /// border nets two after decay and reaches the retake rung around day 20; two
        /// net six, day 7; three net ten, day 4). And CAPPED at the retake rung: the
        /// border alone carries a family to "take a door back off them" and stops
        /// there. Everything above has to be earned by things that happened - a door
        /// switched, a door attacked, a round lost, a man killed - so every war has a
        /// story behind it, by construction rather than by tuning.
        /// </summary>
        public int BorderPressurePerDay = 4;

        public int BorderPressureCap = 40;

        /// <summary>EPIC 42's five. An insult is a nudge; a debt is a levy unpaid; a
        /// line crossed is a threat; a pact broken is a bill; a sit-down betrayed is
        /// a killing and then some.</summary>
        public int InsultingOffer = 5;
        public int DebtUnpaid = 25;
        public int LineCrossed = 20;
        public int PactBroken = 30;
        public int SitDownBetrayed = 40;

        /// <summary>D15. A house declares war only if it can pay its men through one,
        /// and sues for peace when it cannot or when it has lost too many.</summary>
        public int MinWarDays = 14;
        public int LossesToSueForPeace = 3;

        /// <summary>EPIC 42 (DIPL-002). A grievance this heavy, noted after a truce or a
        /// peace was agreed and before it landed, breaks the agreement: a killing, or
        /// worse. Everything lighter lets the agreement stand over it.</summary>
        public int AgreementBreaksAt = 35;

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
                case GrievanceKind.BorderPressure: return BorderPressurePerDay;
                case GrievanceKind.InsultingOffer: return InsultingOffer;
                case GrievanceKind.DebtUnpaid: return DebtUnpaid;
                case GrievanceKind.LineCrossed: return LineCrossed;
                case GrievanceKind.PactBroken: return PactBroken;
                case GrievanceKind.SitDownBetrayed: return SitDownBetrayed;
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

        /// <summary>WHO WROTE THE PENDING STANCE (EPIC 42, DIPL-007): the first argument
        /// of the SetPending that last wrote the slot - the house declaring, for a war.
        /// A pact honours against the declarer, so the book has to remember one. And
        /// whether the write was a pact's own: a war a pact declared triggers no other
        /// pact.</summary>
        readonly Dictionary<long, int> pendingBy = new Dictionary<long, int>();
        readonly HashSet<long> pendingByPact = new HashSet<long>();

        /// <summary>
        /// WHAT TWO HOUSES AGREED TODAY (EPIC 42, DIPL-002). The pending slot is one
        /// slot, last write wins - a defection's Sour or a re-declaration the same
        /// evening would overwrite an accepted truce before midnight. An agreement is
        /// kept beside it and lands OVER any pending stance on the pair, unless a
        /// grievance of AgreementBreaksAt or more was noted after it: then the pending
        /// stands and the agreement is struck.
        /// </summary>
        readonly Dictionary<long, Agreement> agreed = new Dictionary<long, Agreement>();

        sealed class Agreement
        {
            public Stance Stance;
            public int Day;
            public bool Broken;
        }

        /// <summary>The day one house last had a man of its killed by another, for the
        /// killing floor (ruling 4): money cannot take the pair under ThreatAt for
        /// KillingFloorDays after it. Keyed like a grievance - the aggrieved first.
        /// </summary>
        readonly Dictionary<long, int> killings = new Dictionary<long, int>();

        /// <summary>How many points money has cleared off a pair today, against the
        /// day's cap (ruling 4). Keyed like a grievance; the day rides with it.</summary>
        readonly Dictionary<long, (int day, int points)> cleared =
            new Dictionary<long, (int, int)>();

        /// <summary>The day the last DayTick was told, for a Note that names none.
        /// </summary>
        int today;

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
        public void SetPending(int a, int b, Stance stance) =>
            SetPending(a, b, stance, false);

        /// <summary>The same write, flagged as a pact's own when a pact made it.</summary>
        public void SetPending(int a, int b, Stance stance, bool byPact)
        {
            if (a == b)
                return;
            var key = Pair(a, b);
            if (stance == StanceBetween(a, b))
            {
                pending.Remove(key);
                pendingBy.Remove(key);
                pendingByPact.Remove(key);
                return;
            }
            pending[key] = stance;
            pendingBy[key] = a;
            if (byPact)
                pendingByPact.Add(key);
            else
                pendingByPact.Remove(key);
        }

        /// <summary>Midnight. Every pending stance lands at once, for everybody.
        /// </summary>
        public void ApplyPending() => ApplyPending(null);

        /// <summary>
        /// Midnight, with the agreements honoured: an agreed stance lands over whatever
        /// was written pending on the pair since, unless the agreement was broken - and
        /// then the pending stands. <paramref name="outcomes"/> says which, for the
        /// escrow the table holds against each agreement.
        /// </summary>
        public void ApplyPending(List<AgreementOutcome> outcomes) =>
            ApplyPending(outcomes, null);

        /// <summary>
        /// Midnight, with <paramref name="landed"/> filled with every stance that landed
        /// off the pending slot - who wrote it, against whom, and whether a pact did -
        /// for the pacts that honour against a declarer (DIPL-007).
        /// </summary>
        public void ApplyPending(List<AgreementOutcome> outcomes, List<StanceLanded> landed)
        {
            outcomes?.Clear();
            landed?.Clear();
            foreach (var entry in pending)
            {
                stances[entry.Key] = entry.Value;
                if (landed == null)
                    continue;
                // WHAT ACTUALLY LANDS on the pair is the agreement, when one stands
                // unbroken over the slot (below): a war written pending after a truce
                // was agreed never lands, and must not wake a pact as if it had.
                var stance = entry.Value;
                if (agreed.TryGetValue(entry.Key, out var over) && !over.Broken)
                    stance = over.Stance;
                var a = (int)(entry.Key >> 32);
                var b = (int)(entry.Key & 0xFFFFFFFF);
                var by = pendingBy.TryGetValue(entry.Key, out var writer) ? writer : a;
                landed.Add(new StanceLanded(by, by == a ? b : a, stance,
                    pendingByPact.Contains(entry.Key)));
            }
            pending.Clear();
            pendingBy.Clear();
            pendingByPact.Clear();

            foreach (var entry in agreed)
            {
                var a = (int)(entry.Key >> 32);
                var b = (int)(entry.Key & 0xFFFFFFFF);
                if (!entry.Value.Broken)
                    stances[entry.Key] = entry.Value.Stance;
                if (entry.Value.Stance == Stance.Peace)
                    quietSince.Remove(entry.Key);
                outcomes?.Add(new AgreementOutcome(a, b, entry.Value.Stance,
                    !entry.Value.Broken));
            }
            agreed.Clear();
        }

        /// <summary>Two houses agreed on where they stand from midnight. The agreement
        /// is on the pair, like the stance it lands as.</summary>
        public void Agree(int a, int b, Stance stance, int day)
        {
            if (a == b)
                return;
            agreed[Pair(a, b)] = new Agreement { Stance = stance, Day = day };
        }

        public bool TryGetAgreed(int a, int b, out Stance stance, out bool broken)
        {
            stance = Stance.Peace;
            broken = false;
            if (a == b || !agreed.TryGetValue(Pair(a, b), out var agreement))
                return false;
            stance = agreement.Stance;
            broken = agreement.Broken;
            return true;
        }

        public float Grievance(int aggrieved, int offender) =>
            aggrieved == offender ? 0f
                : grievances.TryGetValue(Owed(aggrieved, offender), out var value)
                    ? value
                    : 0f;

        /// <summary>They did something to us. The first argument is always the house
        /// that holds the grudge. <paramref name="day"/> names the campaign day for the
        /// killing floor; left out, the last DayTick's day serves.</summary>
        public void Note(int aggrieved, int offender, GrievanceKind kind, int day = -1)
        {
            if (aggrieved == offender || aggrieved < 0 || offender < 0)
                return;
            var key = Owed(aggrieved, offender);
            grievances.TryGetValue(key, out var value);
            var amount = Config.AmountOf(kind);
            value += amount;
            grievances[key] = value > 100f ? 100f : value;
            quietSince.Remove(Pair(aggrieved, offender));

            if (kind == GrievanceKind.ManKilled)
                killings[key] = day >= 0 ? day : today;

            // A KILLING BREAKS TODAY'S AGREEMENT (DIPL-002). Anything lighter lets it
            // stand; the escrow follows the agreement either way.
            if (amount >= Config.AgreementBreaksAt &&
                agreed.TryGetValue(Pair(aggrieved, offender), out var agreement))
                agreement.Broken = true;
        }

        /// <summary>
        /// WHAT MONEY COULD STILL CLEAR TODAY off one pair, without clearing it: the
        /// day's cap less what was cleared already, and never under the floor inside
        /// the killing window. The desk decides by this and the bill is priced by it
        /// (Codex, EPIC 42), so a yes never promises more than Clear will do.
        /// </summary>
        public int Clearable(int aggrieved, int offender, int day, int capPerDay,
            int floorAt, int floorDays)
        {
            if (aggrieved == offender || aggrieved < 0 || offender < 0)
                return 0;
            var key = Owed(aggrieved, offender);
            grievances.TryGetValue(key, out var value);
            if (value <= 0f)
                return 0;
            var todayCleared = cleared.TryGetValue(key, out var row) && row.day == day
                ? row.points
                : 0;
            var room = capPerDay - todayCleared;
            if (room <= 0)
                return 0;
            var floor = 0f;
            if (killings.TryGetValue(key, out var killed) && day - killed < floorDays)
                floor = floorAt;
            var above = value - floor;
            if (above <= 0f)
                return 0;
            return room < (int)above ? room : (int)above;
        }

        /// <summary>The last day one house had a man killed by another, or -1.</summary>
        public int LastKilling(int aggrieved, int offender) =>
            killings.TryGetValue(Owed(aggrieved, offender), out var day) ? day : -1;

        /// <summary>
        /// MONEY CLEARS A GRUDGE, WITHIN LIMITS (EPIC 42, ruling 4). Every dollar that
        /// clears grievance - a truce's money, a bill paid, terms - comes through here:
        /// at most <paramref name="capPerDay"/> points off one pair in one day, and for
        /// <paramref name="floorDays"/> after a killing never under <paramref name="floorAt"/>.
        /// Time still clears the rest. Answers what was actually cleared.
        /// </summary>
        public int Clear(int aggrieved, int offender, int points, int day, int capPerDay,
            int floorAt, int floorDays)
        {
            var take = Clearable(aggrieved, offender, day, capPerDay, floorAt, floorDays);
            if (take > points)
                take = points;
            if (take <= 0)
                return 0;
            var key = Owed(aggrieved, offender);
            grievances.TryGetValue(key, out var value);
            var todayCleared = cleared.TryGetValue(key, out var row) && row.day == day
                ? row.points
                : 0;

            var left = value - take;
            if (left <= 0f)
                grievances.Remove(key);
            else
                grievances[key] = left;
            cleared[key] = (day, todayCleared + take);
            return take;
        }

        /// <summary>
        /// THE BORDER ITSELF (A13/A18). A day of standing next to a house that leads
        /// the block beside ours, with nowhere else to grow: BorderPressurePerDay per
        /// bordering block, and never past BorderPressureCap - a grudge already at or
        /// above the cap is not touched, and one below it is raised to the cap at most.
        /// Everything above the cap has to be earned by acts.
        /// </summary>
        /// <returns>What was actually added.</returns>
        public int NoteBorder(int aggrieved, int offender, int borderingBlocks)
        {
            if (aggrieved == offender || aggrieved < 0 || offender < 0 ||
                borderingBlocks <= 0)
                return 0;
            var key = Owed(aggrieved, offender);
            grievances.TryGetValue(key, out var value);
            if (value >= Config.BorderPressureCap)
                return 0;
            var added = Config.BorderPressurePerDay * borderingBlocks;
            var raised = value + added;
            if (raised > Config.BorderPressureCap)
            {
                added = (int)(Config.BorderPressureCap - value);
                raised = Config.BorderPressureCap;
            }
            grievances[key] = raised;
            quietSince.Remove(Pair(aggrieved, offender));
            return added;
        }

        public LadderStep StepOf(int aggrieved, int offender) =>
            Config.StepFor(Grievance(aggrieved, offender));

        /// <summary>
        /// A day of quiet. Every grudge fades a little, and a truce whose two sides have
        /// both stayed under PeaceGrievance long enough becomes peace again (D22).
        /// </summary>
        public void DayTick(int day)
        {
            today = day;
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

        /// <summary>
        /// THE WHOLE BOOK, FLAT (RIVAL-010). The two dictionaries are keyed by packed
        /// pairs and JsonUtility writes neither, so the save asks for them as rows and
        /// hands them back the same way. Nothing reflects over the private fields.
        /// </summary>
        public void Collect(List<StanceDto> stances, List<GrievanceDto> grievances)
        {
            if (stances != null)
            {
                stances.Clear();
                foreach (var entry in this.stances)
                    stances.Add(new StanceDto
                    {
                        a = (int)(entry.Key >> 32),
                        b = (int)(entry.Key & 0xFFFFFFFF),
                        stance = (int)entry.Value,
                        pending = false,
                    });
                foreach (var entry in pending)
                    stances.Add(new StanceDto
                    {
                        a = (int)(entry.Key >> 32),
                        b = (int)(entry.Key & 0xFFFFFFFF),
                        stance = (int)entry.Value,
                        pending = true,
                        by = pendingBy.TryGetValue(entry.Key, out var writer) ? writer : -1,
                        byPact = pendingByPact.Contains(entry.Key),
                    });
            }

            if (grievances == null)
                return;
            grievances.Clear();
            foreach (var entry in this.grievances)
                grievances.Add(new GrievanceDto
                {
                    aggrieved = (int)(entry.Key >> 32),
                    offender = (int)(entry.Key & 0xFFFFFFFF),
                    owed = entry.Value,
                });
        }

        /// <summary>The table's part of the book (EPIC 42): today's agreements, the
        /// killings the floor reads, and what money cleared today. Arrays only.</summary>
        public void CollectTable(List<AgreementDto> agreements, List<KillingDto> killed,
            List<ClearedDto> clearedToday)
        {
            if (agreements != null)
            {
                agreements.Clear();
                foreach (var entry in agreed)
                    agreements.Add(new AgreementDto
                    {
                        a = (int)(entry.Key >> 32),
                        b = (int)(entry.Key & 0xFFFFFFFF),
                        stance = (int)entry.Value.Stance,
                        day = entry.Value.Day,
                        broken = entry.Value.Broken,
                    });
            }
            if (killed != null)
            {
                killed.Clear();
                foreach (var entry in killings)
                    killed.Add(new KillingDto
                    {
                        aggrieved = (int)(entry.Key >> 32),
                        offender = (int)(entry.Key & 0xFFFFFFFF),
                        day = entry.Value,
                    });
            }
            if (clearedToday == null)
                return;
            clearedToday.Clear();
            foreach (var entry in cleared)
                clearedToday.Add(new ClearedDto
                {
                    aggrieved = (int)(entry.Key >> 32),
                    offender = (int)(entry.Key & 0xFFFFFFFF),
                    day = entry.Value.day,
                    points = entry.Value.points,
                });
        }

        public void RestoreTable(AgreementDto[] agreements, KillingDto[] killed,
            ClearedDto[] clearedToday)
        {
            agreed.Clear();
            killings.Clear();
            cleared.Clear();
            for (var i = 0; agreements != null && i < agreements.Length; i++)
            {
                var row = agreements[i];
                if (row == null || !System.Enum.IsDefined(typeof(Stance), row.stance))
                    continue;
                agreed[Pair(row.a, row.b)] = new Agreement
                {
                    Stance = (Stance)row.stance,
                    Day = row.day,
                    Broken = row.broken,
                };
            }
            for (var i = 0; killed != null && i < killed.Length; i++)
                if (killed[i] != null)
                    killings[Owed(killed[i].aggrieved, killed[i].offender)] = killed[i].day;
            for (var i = 0; clearedToday != null && i < clearedToday.Length; i++)
                if (clearedToday[i] != null)
                    cleared[Owed(clearedToday[i].aggrieved, clearedToday[i].offender)] =
                        (clearedToday[i].day, clearedToday[i].points);
        }

        /// <summary>The load boundary. Everything the book held is replaced.</summary>
        public void RestoreFrom(StanceDto[] rows, GrievanceDto[] owed)
        {
            stances.Clear();
            pending.Clear();
            grievances.Clear();
            quietSince.Clear();
            agreed.Clear();
            killings.Clear();
            cleared.Clear();

            pendingBy.Clear();
            pendingByPact.Clear();
            for (var i = 0; rows != null && i < rows.Length; i++)
            {
                var key = Pair(rows[i].a, rows[i].b);
                if (rows[i].pending)
                {
                    pending[key] = (Stance)rows[i].stance;
                    // The writer rides on the row since EPIC 42; a file from before it
                    // names none, and the lower id serves - the one loss such a save takes.
                    pendingBy[key] = rows[i].by >= 0 ? rows[i].by : rows[i].a;
                    if (rows[i].byPact)
                        pendingByPact.Add(key);
                }
                else
                    stances[key] = (Stance)rows[i].stance;
            }

            for (var i = 0; owed != null && i < owed.Length; i++)
                grievances[Owed(owed[i].aggrieved, owed[i].offender)] = owed[i].owed;
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

    /// <summary>A stance that landed off the pending slot at midnight (EPIC 42,
    /// DIPL-007): who wrote it, against whom, what it is, and whether a pact wrote it.
    /// </summary>
    public readonly struct StanceLanded
    {
        public StanceLanded(int by, int against, Stance stance, bool byPact)
        {
            By = by;
            Against = against;
            Stance = stance;
            ByPact = byPact;
        }

        public int By { get; }
        public int Against { get; }
        public Stance Stance { get; }
        public bool ByPact { get; }
    }

    /// <summary>What became of one day's agreement at midnight (EPIC 42): it landed
    /// over the pending slot, or it was broken and the pending stood.</summary>
    public readonly struct AgreementOutcome
    {
        public AgreementOutcome(int a, int b, Stance stance, bool landed)
        {
            A = a;
            B = b;
            Stance = stance;
            Landed = landed;
        }

        public int A { get; }
        public int B { get; }
        public Stance Stance { get; }
        public bool Landed { get; }

        public bool IsPair(int x, int y) => (A == x && B == y) || (A == y && B == x);
    }

    [System.Serializable]
    public sealed class AgreementDto
    {
        public int a;
        public int b;
        public int stance;
        public int day;
        public bool broken;
    }

    [System.Serializable]
    public sealed class KillingDto
    {
        public int aggrieved;
        public int offender;
        public int day;
    }

    [System.Serializable]
    public sealed class ClearedDto
    {
        public int aggrieved;
        public int offender;
        public int day;
        public int points;
    }
}
