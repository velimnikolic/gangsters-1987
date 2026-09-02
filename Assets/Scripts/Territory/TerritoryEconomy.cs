using System;
using System.Collections.Generic;

namespace LivingCity.Territory
{
    // ------------------------------------------------------------------ ECON-002
    /// <summary>Who the man behind the counter is. One word the crew can learn.</summary>
    public enum TerritoryOwnerTrait
    {
        Cowardly,
        Proud,
        Greedy,
        Connected,
        Stubborn,
        Careful,
    }

    /// <summary>
    /// The shopkeeper himself (ECON-002): a trait, and the three numbers the economy
    /// reads. Dealt deterministically from the city seed and the business id - its own
    /// stream, never a shared RNG - so the same city deals the same man twice.
    ///
    /// nerve: how much fear it takes to move him (0 folds at a look, 1 takes a war).
    /// greed: how much parting with the cut hurts (0 pays and shrugs, 1 palms coins).
    /// connections: how much police attention leaning on him draws (0 nobody, 1 a
    /// cousin at the precinct).
    /// </summary>
    public readonly struct TerritoryOwnerProfile
    {
        public TerritoryOwnerProfile(
            TerritoryOwnerTrait trait, float nerve, float greed, float connections)
        {
            Trait = trait;
            Nerve = Clamp01(nerve);
            Greed = Clamp01(greed);
            Connections = Clamp01(connections);
        }

        public TerritoryOwnerTrait Trait { get; }
        public float Nerve { get; }
        public float Greed { get; }
        public float Connections { get; }

        /// <summary>How far the compliance thresholds move for THIS man, in score
        /// points. Positive is harder: a Proud or Stubborn owner costs an extra lean, a
        /// Cowardly one folds a step earlier. Neutral nerve (0.5) shifts nothing, so
        /// every pre-ECON evaluation reads exactly as it did.</summary>
        public float NerveShift => (Nerve - 0.5f) * 20f;

        public static TerritoryOwnerProfile Neutral { get; } =
            new TerritoryOwnerProfile(TerritoryOwnerTrait.Careful, 0.5f, 0.5f, 0.5f);

        /// <summary>Deal the owner. Pure hashing - no RNG object, no draw order to
        /// disturb - so a profile can be asked for lazily, in any order, forever.</summary>
        public static TerritoryOwnerProfile Deal(int citySeed, TerritoryBusinessId businessId)
        {
            if (!businessId.IsValid)
                return Neutral;

            var h = Hash(citySeed, businessId.Value);
            var trait = (TerritoryOwnerTrait)(int)(h % 6UL);
            var nerve = Unit(h >> 8);
            var greed = Unit(h >> 24);
            var connections = Unit(h >> 40);

            // The trait is not just a word: it pulls its own number to its own end,
            // so the word the crew learns predicts the man it describes.
            switch (trait)
            {
                case TerritoryOwnerTrait.Cowardly: nerve *= 0.45f; break;
                case TerritoryOwnerTrait.Proud: nerve = 0.55f + nerve * 0.45f; break;
                case TerritoryOwnerTrait.Stubborn: nerve = 0.5f + nerve * 0.5f; break;
                case TerritoryOwnerTrait.Greedy: greed = 0.55f + greed * 0.45f; break;
                case TerritoryOwnerTrait.Connected:
                    connections = 0.6f + connections * 0.4f; break;
                case TerritoryOwnerTrait.Careful: greed *= 0.6f; break;
            }

            return new TerritoryOwnerProfile(trait, nerve, greed, connections);
        }

        static float Unit(ulong h) => (h & 0xFFFF) / 65535f;

        static ulong Hash(int seed, string id)
        {
            // FNV-1a over the seed then the id - stable across runs and platforms.
            var h = 14695981039346656037UL;
            unchecked
            {
                h = (h ^ (ulong)(uint)seed) * 1099511628211UL;
                for (var i = 0; i < id.Length; i++)
                    h = (h ^ id[i]) * 1099511628211UL;
            }
            return h;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    // ------------------------------------------------------------------ ECON-001
    /// <summary>One shop's account with the family it pays.</summary>
    public readonly struct TerritoryDuesAccount
    {
        public TerritoryDuesAccount(
            TerritoryGangId gangId, int weeklyRate, int owedSevenths,
            int lastCollectedDay, int missedInARow)
        {
            GangId = gangId;
            WeeklyRate = weeklyRate;
            OwedSevenths = owedSevenths;
            LastCollectedDay = lastCollectedDay;
            MissedInARow = missedInARow;
        }

        public TerritoryGangId GangId { get; }
        public int WeeklyRate { get; }

        /// <summary>Owed money times seven - the integer trick that makes a week of
        /// daily accruals sum to EXACTLY the weekly rate with no rounding drift.</summary>
        public int OwedSevenths { get; }

        public int Owed => OwedSevenths / 7;
        public int LastCollectedDay { get; }
        public int MissedInARow { get; }
    }

    /// <summary>
    /// The money side of protection (ECON-001), held apart from the standing that
    /// produced it. One account per business toward the family it currently pays; the
    /// meter runs only while the racket says Compliant, and a lapsed arrangement stops
    /// it rather than building a debt nobody can collect. No Unity type, no clock of
    /// its own: the campaign day is handed in.
    /// </summary>
    public sealed class TerritoryDuesLedger
    {
        sealed class Account
        {
            public TerritoryGangId GangId;
            public int WeeklyRate;
            public int OwedSevenths;
            public int LastCollectedDay;
            public int MissedInARow;
        }

        readonly Dictionary<TerritoryBusinessId, Account> accounts =
            new Dictionary<TerritoryBusinessId, Account>();
        readonly List<TerritoryBusinessId> ids = new List<TerritoryBusinessId>();

        public IReadOnlyList<TerritoryBusinessId> Businesses => ids;

        /// <summary>One day of the meter, for a shop the racket holds Compliant toward
        /// this family. A different family taking the shop restarts the account - the
        /// old debt died with the old arrangement.</summary>
        public void AccrueDay(
            TerritoryBusinessId businessId, TerritoryGangId gangId, int weeklyRate)
        {
            if (!businessId.IsValid || !gangId.IsValid || weeklyRate <= 0)
                return;

            if (!accounts.TryGetValue(businessId, out var account))
            {
                account = new Account { GangId = gangId, LastCollectedDay = -1 };
                accounts.Add(businessId, account);
                ids.Add(businessId);
            }

            if (account.GangId != gangId)
            {
                account.GangId = gangId;
                account.OwedSevenths = 0;
                account.MissedInARow = 0;
                account.LastCollectedDay = -1;
            }

            account.WeeklyRate = weeklyRate;
            account.OwedSevenths += weeklyRate;
        }

        public bool TryGet(TerritoryBusinessId businessId, out TerritoryDuesAccount account)
        {
            account = default;
            if (!accounts.TryGetValue(businessId, out var row))
                return false;
            account = new TerritoryDuesAccount(
                row.GangId, row.WeeklyRate, row.OwedSevenths,
                row.LastCollectedDay, row.MissedInARow);
            return true;
        }

        public int OwedOf(TerritoryBusinessId businessId, TerritoryGangId gangId) =>
            accounts.TryGetValue(businessId, out var row) && row.GangId == gangId
                ? row.OwedSevenths / 7
                : 0;

        /// <summary>
        /// The collector was at the door and the owner answered. What he paid comes off
        /// the meter; a miss counts against him, anything else clears the run. Returns
        /// how many collections in a row he has now missed.
        /// </summary>
        public int Settle(
            TerritoryBusinessId businessId, TerritoryGangId gangId, int day,
            int paid, bool missed)
        {
            if (!accounts.TryGetValue(businessId, out var row) || row.GangId != gangId)
                return 0;

            row.OwedSevenths = Math.Max(0, row.OwedSevenths - Math.Max(0, paid) * 7);
            row.LastCollectedDay = day;
            row.MissedInARow = missed ? row.MissedInARow + 1 : 0;
            return row.MissedInARow;
        }

        /// <summary>The arrangement ended - a lapse, a switch, a shop lost. The meter
        /// and the debt go with it.</summary>
        public void Drop(TerritoryBusinessId businessId)
        {
            if (!accounts.Remove(businessId))
                return;
            ids.Remove(businessId);
        }
    }

    // ------------------------------------------------------------------ ECON-003
    public enum TerritoryPaymentOutcome
    {
        Paid,
        Short,
        Missed,
    }

    /// <summary>What the owner says when he does not pay in full. The words are the
    /// crew's to weigh - whether they are TRUE is in the roll, never on a card.</summary>
    public enum TerritoryPaymentExcuse
    {
        None,
        BadWeek,
        WasRobbed,
        PoliceWereRound,
    }

    public readonly struct TerritoryPaymentResult
    {
        public TerritoryPaymentResult(
            TerritoryPaymentOutcome outcome, int paid, int owed,
            TerritoryPaymentExcuse excuse, bool excuseTruthful)
        {
            Outcome = outcome;
            Paid = paid;
            Owed = owed;
            Excuse = excuse;
            ExcuseTruthful = excuseTruthful;
        }

        public TerritoryPaymentOutcome Outcome { get; }
        public int Paid { get; }
        public int Owed { get; }
        public TerritoryPaymentExcuse Excuse { get; }

        /// <summary>Whether the excuse is honest. The SIM knows; the player is never
        /// handed this flag - a crew learns it by knowing the street.</summary>
        public bool ExcuseTruthful { get; }
    }

    /// <summary>
    /// What happens at the door when the collector puts his hand out (ECON-003).
    /// Deterministic from (business, day, seed): the same door on the same day answers
    /// the same way, so a round is a fact the player can plan against.
    /// </summary>
    public static class TerritoryPaymentRoll
    {
        /// <summary>Fear at or above this, on a man of at most average nerve, and he
        /// NEVER misses - the terrified owner with the fat till pays every time.</summary>
        public const float SureFear = 70f;

        public static TerritoryPaymentResult Roll(
            int owed,
            TerritoryOwnerProfile owner,
            float protectorFear,
            float blockTrouble,
            float shortAcceptedShare,
            int citySeed,
            int day,
            TerritoryBusinessId businessId)
        {
            if (owed <= 0)
                return new TerritoryPaymentResult(
                    TerritoryPaymentOutcome.Paid, 0, 0, TerritoryPaymentExcuse.None, true);

            var h = Mix(citySeed, day, businessId);
            var r = (h & 0xFFFF) / 65536f;

            // How likely he is to try it on. Fear of his protector presses down on it,
            // his greed and a troubled street push it up - a street full of trouble is
            // a street where "I was robbed" might even be true.
            var courage = owner.Nerve * 0.5f;
            var payPressure = protectorFear * (1.1f - courage) / 100f;
            var missChance = Clamp01(0.45f - payPressure + owner.Greed * 0.35f +
                                     blockTrouble / 400f);
            var shortChance = Clamp01(0.30f - payPressure * 0.5f + owner.Greed * 0.25f);

            if (protectorFear >= SureFear && owner.Nerve <= 0.7f)
            {
                missChance = 0f;
                shortChance = 0f;
            }

            if (r < missChance)
            {
                var excuse = ExcuseOf(h >> 16);
                return new TerritoryPaymentResult(
                    TerritoryPaymentOutcome.Missed, 0, owed, excuse,
                    Truthful(h >> 20, blockTrouble));
            }

            if (r < missChance + shortChance)
            {
                // He offers a share; the crew's policy says what share they accept
                // without pressing, and that is what actually changes hands.
                var offered = (int)(owed * (0.35f + ((h >> 28) & 0xFF) / 255f * 0.3f));
                var accepted = Math.Max(offered, (int)(owed * Clamp01(shortAcceptedShare)));
                accepted = Math.Min(accepted, owed);
                var excuse = ExcuseOf(h >> 16);
                return new TerritoryPaymentResult(
                    TerritoryPaymentOutcome.Short, accepted, owed, excuse,
                    Truthful(h >> 20, blockTrouble));
            }

            return new TerritoryPaymentResult(
                TerritoryPaymentOutcome.Paid, owed, owed, TerritoryPaymentExcuse.None, true);
        }

        static TerritoryPaymentExcuse ExcuseOf(ulong h) =>
            (TerritoryPaymentExcuse)(1 + (int)(h % 3UL));

        /// <summary>A quiet street makes a liar of most excuses; a troubled one makes
        /// honest men of them. Deterministic like everything else here.</summary>
        static bool Truthful(ulong h, float blockTrouble) =>
            (h & 0xFF) / 255f < Clamp01(0.2f + blockTrouble / 120f);

        public static ulong Mix(int citySeed, int day, TerritoryBusinessId businessId)
        {
            var h = 14695981039346656037UL;
            unchecked
            {
                h = (h ^ (ulong)(uint)citySeed) * 1099511628211UL;
                h = (h ^ (ulong)(uint)day) * 1099511628211UL;
                var id = businessId.Value ?? "";
                for (var i = 0; i < id.Length; i++)
                    h = (h ^ id[i]) * 1099511628211UL;
            }
            return h;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    // ------------------------------------------------------------------ ECON-005
    /// <summary>
    /// What a policy does to a round (ECON-005): three numbers and nothing else. The
    /// enum itself lives with the roster (Personnel.CrewPolicy); this table is keyed by
    /// its integer value so the territory side owes the roster nothing.
    /// Lenient=0, Normal=1, Strict=2, Brutal=3.
    /// </summary>
    public readonly struct TerritoryCollectionStyle
    {
        public TerritoryCollectionStyle(float shortAcceptedShare, float fearLeft, float heatLeft)
        {
            ShortAcceptedShare = shortAcceptedShare;
            FearLeft = fearLeft;
            HeatLeft = heatLeft;
        }

        /// <summary>The floor share of a short payment the crew takes without a word.
        /// Brutal presses for the whole figure - it collects more today.</summary>
        public float ShortAcceptedShare { get; }

        /// <summary>Fear act severity a settled stop leaves on the street. Brutal
        /// burns the street it collects from.</summary>
        public float FearLeft { get; }

        /// <summary>Police attention a settled stop draws, before the tier's own.</summary>
        public float HeatLeft { get; }

        public static TerritoryCollectionStyle OfPolicy(int policyLevel)
        {
            switch (policyLevel)
            {
                case 0: return new TerritoryCollectionStyle(0.35f, 0.00f, 0.00f); // Lenient
                case 2: return new TerritoryCollectionStyle(0.75f, 0.45f, 0.30f); // Strict
                case 3: return new TerritoryCollectionStyle(1.00f, 1.20f, 0.90f); // Brutal
                default: return new TerritoryCollectionStyle(0.50f, 0.15f, 0.10f); // Normal
            }
        }

        /// <summary>The lieutenant's own hand on top of the policy (ECON-005): an
        /// Earner squeezes a better figure quietly, a Psychopath frightens the street
        /// and the precinct in the same afternoon. Keyed by the roster's
        /// LieutenantArchetype integer value, same bargain as the policy key.
        /// Earner=0, Negotiator=1, Enforcer=2, Psychopath=3, Administrator=4, Soldier=5.</summary>
        public static void ArchetypeScales(
            int archetype, out float takeScale, out float fearScale, out float heatScale)
        {
            switch (archetype)
            {
                case 0: takeScale = 1.15f; fearScale = 0.90f; heatScale = 0.90f; break;
                case 1: takeScale = 1.05f; fearScale = 0.75f; heatScale = 0.80f; break;
                case 2: takeScale = 1.00f; fearScale = 1.25f; heatScale = 1.10f; break;
                case 3: takeScale = 0.95f; fearScale = 1.60f; heatScale = 1.60f; break;
                case 4: takeScale = 1.00f; fearScale = 0.85f; heatScale = 0.85f; break;
                default: takeScale = 1.00f; fearScale = 1.00f; heatScale = 1.00f; break;
            }
        }
    }

    // ------------------------------------------------------------------ ECON-007
    /// <summary>
    /// The tier guard (ECON-007): the fat targets are not free money. Tuned in numbers,
    /// never with an eligibility flag - every business stays racketable, a tier-4 place
    /// just needs a family the street already fears and obeys before it pays.
    /// </summary>
    public static class TerritoryTierGuard
    {
        /// <summary>Added to the compliance ACCEPT threshold, by tier (1-4). A tier-1
        /// shopfront moves nothing; a casino wants near everything a family can be.</summary>
        public static float AcceptBar(int tier)
        {
            switch (tier)
            {
                case 2: return 8f;
                case 3: return 26f;
                case 4: return 48f;
                default: return 0f;
            }
        }

        /// <summary>Police attention a collected stop draws per hundred dollars of its
        /// weekly rate - the take of a fat place carries heat in proportion.</summary>
        public const float HeatPerHundredWeekly = 0.06f;
    }

    // ------------------------------------------------------------- ECON-004 order
    /// <summary>One door a round could stop at, flattened for the planner.</summary>
    public readonly struct TerritoryRoundStopSeed
    {
        public TerritoryRoundStopSeed(string id, float x, float z)
        {
            Id = id ?? "";
            X = x;
            Z = z;
        }

        public string Id { get; }
        public float X { get; }
        public float Z { get; }
    }

    /// <summary>
    /// WHEN A BLOCK IS COLLECTED, and whether a round is owed today.
    ///
    /// The player will not send a man to every door by hand. A lieutenant's blocks each
    /// get a weekday of their own, and on that day a man of his who is marked for the
    /// bag walks the paying doors and banks what they owe. The manual COLLECT THE TAKE
    /// order stays as the override; this is only the standing arrangement.
    ///
    /// Pure and total: every input is a plain value, so the headless suite drives the
    /// whole rule without a city standing.
    /// </summary>
    public static class TerritoryCollectionSchedule
    {
        /// <summary>Rounds go out from nine in the morning. A collector knocking at four
        /// is a man waking a shopkeeper, which is a different act.</summary>
        public const int OpeningHour = 9;

        public const int DaysInWeek = 7;

        /// <summary>0 is Monday, the way <c>Campaign.DayOfWeek</c> counts.</summary>
        static readonly string[] DayWords =
        {
            "Mondays", "Tuesdays", "Wednesdays", "Thursdays", "Fridays", "Saturdays",
            "Sundays",
        };

        /// <summary>
        /// The weekday this block is walked on. Derived from the block's own id and
        /// nothing else, so the arrangement is the same in every session of one city and
        /// the player can learn it.
        ///
        /// FNV-1a over the id's characters, never <c>string.GetHashCode()</c>: that is
        /// not stable across runs, and a collection day that moved between sessions
        /// would be an arrangement nobody could plan around.
        /// </summary>
        public static int DayOf(TerritoryBlockId blockId)
        {
            var text = blockId.Value;
            unchecked
            {
                var hash = 2166136261u;
                for (var i = 0; i < (text?.Length ?? 0); i++)
                {
                    hash ^= text[i];
                    hash *= 16777619u;
                }
                return (int)(hash % DaysInWeek);
            }
        }

        /// <summary>The day in the word the ledger prints - "Thursdays".</summary>
        public static string WordOf(TerritoryBlockId blockId) => WordOfDay(DayOf(blockId));

        public static string WordOfDay(int dayOfWeek) =>
            dayOfWeek >= 0 && dayOfWeek < DayWords.Length ? DayWords[dayOfWeek] : "";

        /// <summary>
        /// Whether a round is owed on this block right now. Every condition has to hold:
        /// it is the block's day, the shops are open, something is actually owed, a man
        /// of the crew carries the bag, no round is already out, and one has not gone
        /// today already.
        /// </summary>
        public static bool ShouldSend(
            int dayOfWeek, int hourOfDay, TerritoryBlockId blockId,
            int owed, bool hasCollector, bool roundRunning, bool sentToday) =>
            blockId.IsValid &&
            DayOf(blockId) == dayOfWeek &&
            hourOfDay >= OpeningHour &&
            owed > 0 &&
            hasCollector &&
            !roundRunning &&
            !sentToday;

        /// <summary>
        /// Whether a paying door is LATE with us. Either it has run up a full week's
        /// money without anybody carrying it home, or nobody has been to see it in over
        /// a week - both are a door the collection has stopped reaching.
        /// </summary>
        public static bool IsLate(int owed, int weeklyRate, int day, int lastCollectedDay)
        {
            if (weeklyRate > 0 && owed >= weeklyRate)
                return true;
            // Never collected is not late until a week has passed since day one.
            return lastCollectedDay >= 0 && day - lastCollectedDay > 7;
        }

        /// <summary>How many days a door has been late, or 0. Counted off the last
        /// collection, because that is the day the clock started.</summary>
        public static int DaysLate(int day, int lastCollectedDay) =>
            lastCollectedDay < 0 || day - lastCollectedDay <= 7
                ? 0
                : day - lastCollectedDay - 7;
    }

    /// <summary>
    /// The walk order of a round (ECON-004): nearest door first from where the men
    /// stand, then nearest from each door - the street's own shape, never the id
    /// list's. Deterministic: a distance tie falls to the lower id.
    /// </summary>
    public static class TerritoryRoundPlanner
    {
        public static void Order(
            IReadOnlyList<TerritoryRoundStopSeed> stops, float fromX, float fromZ,
            List<int> orderInto)
        {
            if (orderInto == null)
                return;
            orderInto.Clear();
            if (stops == null || stops.Count == 0)
                return;

            var taken = new bool[stops.Count];
            var atX = fromX;
            var atZ = fromZ;
            for (var step = 0; step < stops.Count; step++)
            {
                var best = -1;
                var bestDistance = float.MaxValue;
                for (var i = 0; i < stops.Count; i++)
                {
                    if (taken[i])
                        continue;
                    var dx = stops[i].X - atX;
                    var dz = stops[i].Z - atZ;
                    var distance = dx * dx + dz * dz;
                    if (distance < bestDistance - 0.01f ||
                        (Math.Abs(distance - bestDistance) <= 0.01f && best >= 0 &&
                         string.CompareOrdinal(stops[i].Id, stops[best].Id) < 0))
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }

                taken[best] = true;
                orderInto.Add(best);
                atX = stops[best].X;
                atZ = stops[best].Z;
            }
        }
    }

    // ------------------------------------------------------------------ ECON-006
    /// <summary>
    /// A man's name, street by street (ECON-006). Earned only by acts - a collection
    /// leaned on, a threat made good, violence seen - and it decays the way fear does.
    /// It is HIS, not the family's, and it counts only where he is standing.
    /// </summary>
    public sealed class TerritoryReputationLedger
    {
        /// <summary>Half-life of a name nobody is refreshing, in game hours (a week).</summary>
        public const float HalfLifeHours = 168f;

        /// <summary>The cap one man's name can reach in one neighbourhood.</summary>
        public const float Cap = 100f;

        readonly struct Key : IEquatable<Key>
        {
            public Key(int characterId, string neighborhood)
            {
                CharacterId = characterId;
                Neighborhood = neighborhood ?? "";
            }

            public int CharacterId { get; }
            public string Neighborhood { get; }

            public bool Equals(Key other) =>
                CharacterId == other.CharacterId &&
                string.Equals(Neighborhood, other.Neighborhood, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is Key other && Equals(other);

            public override int GetHashCode() =>
                unchecked(CharacterId * 397 ^ Neighborhood.GetHashCode());
        }

        sealed class Row
        {
            public float Value;
            public double Stamp;
        }

        readonly Dictionary<Key, Row> rows = new Dictionary<Key, Row>();

        public void Note(int characterId, string neighborhood, float amount, double gameHour)
        {
            if (characterId < 0 || string.IsNullOrEmpty(neighborhood) || amount <= 0f)
                return;

            var key = new Key(characterId, neighborhood);
            if (!rows.TryGetValue(key, out var row))
            {
                row = new Row();
                rows.Add(key, row);
            }

            row.Value = Math.Min(Cap, Decayed(row, gameHour) + amount);
            row.Stamp = gameHour;
        }

        public float Of(int characterId, string neighborhood, double gameHour) =>
            rows.TryGetValue(new Key(characterId, neighborhood), out var row)
                ? Decayed(row, gameHour)
                : 0f;

        /// <summary>
        /// The streets one man is known on, best first. A name is a fact about him that
        /// the player has to be able to READ before he can plan around it - which
        /// quarter to send him back to, and which one has forgotten him.
        /// </summary>
        public void CollectFor(int characterId, double gameHour,
            List<(string Neighborhood, float Name)> into)
        {
            if (into == null)
                return;
            into.Clear();
            foreach (var pair in rows)
            {
                if (pair.Key.CharacterId != characterId)
                    continue;
                var value = Decayed(pair.Value, gameHour);
                if (value >= Faint)
                    into.Add((pair.Key.Neighborhood, value));
            }
            into.Sort((a, b) =>
            {
                var byName = b.Name.CompareTo(a.Name);
                return byName != 0
                    ? byName
                    : string.CompareOrdinal(a.Neighborhood, b.Neighborhood);
            });
        }

        /// <summary>The men with a name on one street, best first - who the quarter
        /// would recognise coming round the corner.</summary>
        public void CollectOn(string neighborhood, double gameHour,
            List<(int CharacterId, float Name)> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (string.IsNullOrEmpty(neighborhood))
                return;
            foreach (var pair in rows)
            {
                if (!string.Equals(pair.Key.Neighborhood, neighborhood,
                        StringComparison.Ordinal))
                    continue;
                var value = Decayed(pair.Value, gameHour);
                if (value >= Faint)
                    into.Add((pair.Key.CharacterId, value));
            }
            into.Sort((a, b) =>
            {
                var byName = b.Name.CompareTo(a.Name);
                return byName != 0 ? byName : a.CharacterId.CompareTo(b.CharacterId);
            });
        }

        /// <summary>Under this a name has faded to nothing worth printing.</summary>
        public const float Faint = 1f;

        /// <summary>
        /// What a name amounts to, in words. The player never sees the figure - the
        /// same rule every other territory reading follows - and the quarters are the
        /// ledger's own: a quarter of the cap each.
        /// </summary>
        public static string Word(float name)
        {
            if (name < Faint) return "unknown";
            if (name < Cap * 0.25f) return "seen about";
            if (name < Cap * 0.5f) return "known";
            if (name < Cap * 0.75f) return "respected";
            return "feared";
        }

        /// <summary>The multiplier a man's presence carries on his own streets - the
        /// PRES-003 rank weight, extended (never a second presence channel). One at no
        /// name, up to 1.5 at a full one.</summary>
        public float PresenceScale(int characterId, string neighborhood, double gameHour) =>
            1f + Of(characterId, neighborhood, gameHour) / (Cap * 2f);

        static float Decayed(Row row, double gameHour)
        {
            var elapsed = gameHour - row.Stamp;
            if (elapsed <= 0)
                return row.Value;
            return row.Value * (float)Math.Pow(0.5, elapsed / HalfLifeHours);
        }
    }
}
