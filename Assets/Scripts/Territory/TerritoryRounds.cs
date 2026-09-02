using System.Collections.Generic;

namespace LivingCity.Territory
{
    /// <summary>What a walk down a block's doors is FOR. The route, the arrival, the
    /// settle and the abandon are one machine; only what happens at the counter
    /// differs.</summary>
    public enum TerritoryRoundKind
    {
        /// <summary>Money: the paying doors, the bag, the front.</summary>
        Collect,

        /// <summary>The ask: every door that does not pay this house yet.</summary>
        ShakeDown,

        /// <summary>The threat: every door holding out.</summary>
        Lean,

        /// <summary>A single door, walked up to and spoken at - the errand behind
        /// APPROACH. It carries nothing and banks nothing; the walk IS the act.</summary>
        Approach,

        /// <summary>A single door, asked to start paying.</summary>
        Demand,

        /// <summary>A single door, leaned on once.</summary>
        Threaten,
    }

    /// <summary>The racket's cadence numbers, in one place (D17). A number lives in the
    /// epic's table and in exactly one class here; never as a literal in a method.
    /// </summary>
    public static class TerritoryRoundConfig
    {
        /// <summary>How long a door takes on the paper clock. A body on the street takes
        /// as long as DoorBeat says; a house nobody has stood up takes this.</summary>
        public const float PaperStopMinutes = 2f;

        public const float PaperStopHours = PaperStopMinutes / 60f;
    }

    /// <summary>Where the men are in the walk.</summary>
    public enum TerritoryRoundStage
    {
        Walking,
        HeadingHome,
        Banked,
        Lost,
    }

    /// <summary>One door on a round, and the pavement spot outside it.</summary>
    public readonly struct TerritoryRoundStop
    {
        public TerritoryRoundStop(TerritoryBusinessId businessId, TerritoryPoint doorstep)
        {
            BusinessId = businessId;
            Doorstep = doorstep;
        }

        public TerritoryBusinessId BusinessId { get; }
        public TerritoryPoint Doorstep { get; }
    }

    /// <summary>
    /// ONE ERRAND, WALKED. Whose it is, who carries the bag, which doors in which
    /// order, how far down the list the men have got and what is in the bag.
    ///
    /// Pure data. Nothing here knows whether the walk is being made by bodies on a
    /// street or by the paper clock - that is the whole point of the split, and it is
    /// what lets a house nobody has stood up still collect its money.
    /// </summary>
    public sealed class TerritoryRound
    {
        public TerritoryGangId House;
        public int CrewId;

        /// <summary>The man with the bag, or -1 before one is named.</summary>
        public int CollectorId = -1;

        public TerritoryBlockId BlockId;
        public TerritoryRoundKind Kind = TerritoryRoundKind.Collect;
        public readonly List<TerritoryRoundStop> Stops = new List<TerritoryRoundStop>();
        public int StopIndex;
        public int Carried;
        public int Missed;
        public TerritoryRoundStage Stage = TerritoryRoundStage.Walking;
        public double OpenedAt;
        public double LastMoveAt;

        /// <summary>He is inside this stop's shop. The arrival sampling runs several
        /// times a second and the conversation takes seconds, so without this the same
        /// door would be entered again and again while he stood at its counter.</summary>
        public bool InTheDoor;

        public bool Finished =>
            Stage == TerritoryRoundStage.Banked || Stage == TerritoryRoundStage.Lost;

        public bool HasStop => StopIndex >= 0 && StopIndex < Stops.Count;

        public TerritoryRoundStop Stop => Stops[StopIndex];

        /// <summary>Doors left to knock on.</summary>
        public int StopsLeft =>
            Stage == TerritoryRoundStage.Walking ? Stops.Count - StopIndex : 0;
    }

    /// <summary>
    /// Everything the pure rule needs to know about ONE door at the moment the men are
    /// at its counter. The caller reads the world and hands it over; the ledger decides
    /// what changes hands.
    /// </summary>
    public readonly struct TerritoryStopInputs
    {
        public TerritoryStopInputs(bool open, int owed, TerritoryOwnerProfile owner,
            float protectorFear, float blockFear, int policyLevel, int archetype,
            int citySeed, int day, bool policeWereRound = false)
        {
            Open = open;
            Owed = owed;
            Owner = owner;
            ProtectorFear = protectorFear;
            BlockFear = blockFear;
            PolicyLevel = policyLevel;
            Archetype = archetype;
            CitySeed = citySeed;
            Day = day;
            PoliceWereRound = policeWereRound;
        }

        /// <summary>The shop is trading. A place with its shutters down cannot be
        /// asked for anything.</summary>
        public bool Open { get; }

        public int Owed { get; }
        public TerritoryOwnerProfile Owner { get; }
        public float ProtectorFear { get; }
        public float BlockFear { get; }
        public int PolicyLevel { get; }
        public int Archetype { get; }
        public int CitySeed { get; }
        public int Day { get; }

        /// <summary>An officer stood at this counter a few hours ago and the owner can
        /// say so (GAN-245). A door under the law's eye pays nobody today, whatever the
        /// roll would have said.</summary>
        public bool PoliceWereRound { get; }
    }

    /// <summary>What happened at one door - the money, and the figures the world owes
    /// the block afterwards. The caller prints it and files the fear and the heat; the
    /// numbers are the ledger's.</summary>
    public readonly struct TerritoryStopSettlement
    {
        public TerritoryStopSettlement(bool settled, int paid, int owed, bool missed,
            TerritoryPaymentOutcome outcome, TerritoryPaymentExcuse excuse,
            float fearLeft, float heat, bool lapsed)
        {
            Settled = settled;
            Paid = paid;
            Owed = owed;
            Missed = missed;
            Outcome = outcome;
            Excuse = excuse;
            FearLeft = fearLeft;
            Heat = heat;
            Lapsed = lapsed;
        }

        /// <summary>False when the door was shut and nothing was asked of it.</summary>
        public bool Settled { get; }

        public int Paid { get; }
        public int Owed { get; }
        public bool Missed { get; }
        public TerritoryPaymentOutcome Outcome { get; }
        public TerritoryPaymentExcuse Excuse { get; }

        /// <summary>Fear the visit left on the block, before the caller decides who
        /// saw it.</summary>
        public float FearLeft { get; }

        /// <summary>Police attention the visit drew.</summary>
        public float Heat { get; }

        /// <summary>Two misses running: the arrangement lapsed and the meter
        /// stopped.</summary>
        public bool Lapsed { get; }
    }

    /// <summary>
    /// THE ROUND'S OWN RULES, and the one place a round's money is computed.
    ///
    /// It opens rounds, settles doors, walks the cursor, banks and abandons - for any
    /// house, with no scene behind it. The physical walk (bodies on pavements) and the
    /// paper clock (<see cref="TerritoryPaperClock"/>) only tell it WHEN things happen;
    /// neither of them may touch <see cref="TerritoryRound.Carried"/>.
    ///
    /// Pure and free of UnityEngine, so a round can be walked end to end in a headless
    /// test - from "the shop owes" to "the safe grew" - with no city at all.
    /// </summary>
    public sealed class TerritoryRoundLedger
    {
        readonly TerritoryRacketLedger racket;
        readonly TerritoryDuesLedger dues;
        readonly List<TerritoryRound> rounds = new List<TerritoryRound>();
        readonly List<TerritoryProtectionChange> changes =
            new List<TerritoryProtectionChange>();

        public TerritoryRoundLedger(TerritoryRacketLedger racket, TerritoryDuesLedger dues)
        {
            this.racket = racket;
            this.dues = dues;
        }

        public IReadOnlyList<TerritoryRound> Rounds => rounds;

        /// <summary>
        /// The load boundary (RIVAL-010). A round that was out when the game stopped is
        /// out again when it starts: the same stops, the same bag, the same cursor. What
        /// clock walks it from here is the caller's business - the street re-marches its
        /// men, the paper clock is re-sent.
        /// </summary>
        public void RestoreFrom(List<TerritoryRound> live)
        {
            rounds.Clear();
            for (var i = 0; live != null && i < live.Count; i++)
                if (live[i] != null && !live[i].Finished)
                    rounds.Add(live[i]);
        }

        /// <summary>Raised when a door has been settled, so the world can print it and
        /// file what it left behind. The ledger itself owes the street nothing.</summary>
        public System.Action<TerritoryRound, TerritoryRoundStop, TerritoryStopSettlement>
            Settled;

        /// <summary>Raised when a round ends, banked or lost.</summary>
        public System.Action<TerritoryRound> Ended;

        /// <summary>A crew already has a walk out - manual or standing. One errand at a
        /// time is the rule the whole machine keeps.</summary>
        public bool RoundRunning(int crewId)
        {
            for (var i = 0; i < rounds.Count; i++)
                if (rounds[i].CrewId == crewId && !rounds[i].Finished)
                    return true;
            return false;
        }

        public TerritoryRound Of(int crewId)
        {
            for (var i = 0; i < rounds.Count; i++)
                if (rounds[i].CrewId == crewId && !rounds[i].Finished)
                    return rounds[i];
            return null;
        }

        /// <summary>Opens a walk. The stops are the caller's - it knows the street's
        /// shape and the ledger does not - and the order they arrive in is the order
        /// they are knocked on.</summary>
        public TerritoryRound Open(TerritoryGangId house, int crewId, int collectorId,
            TerritoryBlockId blockId, TerritoryRoundKind kind,
            IReadOnlyList<TerritoryRoundStop> stops, double at)
        {
            if (stops == null || stops.Count == 0)
                return null;

            var round = new TerritoryRound
            {
                House = house,
                CrewId = crewId,
                CollectorId = collectorId,
                BlockId = blockId,
                Kind = kind,
                OpenedAt = at,
                LastMoveAt = at,
            };
            for (var i = 0; i < stops.Count; i++)
                round.Stops.Add(stops[i]);
            rounds.Add(round);
            return round;
        }

        /// <summary>The men are at this stop's door. Answers false when they are
        /// already through it - the arrival is asked many times a second.</summary>
        public bool Arrive(TerritoryRound round, double at)
        {
            if (round == null || round.Finished || round.InTheDoor || !round.HasStop)
                return false;
            round.InTheDoor = true;
            round.LastMoveAt = at;
            return true;
        }

        /// <summary>
        /// THE HAND GOES OUT. The owner pays, pays part with a story, or does not pay;
        /// the crew's policy and the lieutenant's own trade say what actually changes
        /// pockets and what the stop leaves behind; and two misses running let the
        /// arrangement lapse.
        ///
        /// This is the ONLY place a round's money is computed.
        /// </summary>
        public TerritoryStopSettlement Settle(
            TerritoryRound round, in TerritoryStopInputs inputs, double gameHour)
        {
            if (round == null || round.Finished || !round.HasStop)
                return default;

            // The hour the door was settled at, on the round itself: the callback has to
            // file the fear and the heat at the moment the hand went out, and the paper
            // clock settles doors hours behind the tick that drove it there.
            round.LastMoveAt = gameHour;
            var stop = round.Stop;
            if (!inputs.Open)
            {
                var shut = new TerritoryStopSettlement(
                    false, 0, inputs.Owed, false, TerritoryPaymentOutcome.Missed,
                    TerritoryPaymentExcuse.None, 0f, 0f, false);
                Settled?.Invoke(round, stop, shut);
                return shut;
            }

            var style = TerritoryCollectionStyle.OfPolicy(inputs.PolicyLevel);
            TerritoryCollectionStyle.ArchetypeScales(
                inputs.Archetype, out var takeScale, out var fearScale, out var heatScale);

            var roll = TerritoryPaymentRoll.Roll(
                inputs.Owed, inputs.Owner, inputs.ProtectorFear, inputs.BlockFear,
                style.ShortAcceptedShare, inputs.CitySeed, inputs.Day, stop.BusinessId);

            // THE POLICE WERE ROUND (GAN-245). A door an officer stood at this morning
            // has one answer and it is the true one - and the excuse is not rolled for,
            // it is handed over, because the crew can see the squad car from the corner.
            // Whatever the payment roll said, nothing changes hands here today.
            if (inputs.PoliceWereRound && inputs.Owed > 0)
                roll = new TerritoryPaymentResult(
                    TerritoryPaymentOutcome.Missed, 0, inputs.Owed,
                    TerritoryPaymentExcuse.PoliceWereRound, true);

            var paid = (int)System.Math.Round(roll.Paid * takeScale);
            if (paid > inputs.Owed)
                paid = inputs.Owed;
            if (paid < 0)
                paid = 0;
            round.Carried += paid;

            var missed = roll.Outcome == TerritoryPaymentOutcome.Missed;
            if (missed)
                round.Missed++;

            // MONEY ON THE WIRE. A door that pays in full says nothing - the round's own
            // slip covers it, and one line per paying door per week would bury the book
            // in good news. A short and a miss are the two a house has to react to, so
            // each files with the sum and the owner's story.
            if (missed)
                racket?.FileMoney(stop.BusinessId, round.House, TerritoryDoorNews.Missed,
                    gameHour, inputs.Owed, inputs.Owed, roll.Excuse);
            else if (paid < inputs.Owed)
                racket?.FileMoney(stop.BusinessId, round.House,
                    TerritoryDoorNews.PaidShort, gameHour, paid, inputs.Owed, roll.Excuse);

            var lapsed = false;
            var runs = dues != null
                ? dues.Settle(stop.BusinessId, round.House, inputs.Day, paid, missed)
                : 0;
            if (runs >= 2 && racket != null)
            {
                // Twice running and nobody answered it: the arrangement lapses back
                // toward Hesitant (ECON-003), and the meter stops with it.
                changes.Clear();
                lapsed = racket.Lapse(stop.BusinessId, round.House, gameHour, changes);
                if (lapsed)
                    dues?.Drop(stop.BusinessId);
            }

            var settlement = new TerritoryStopSettlement(
                true, paid, inputs.Owed, missed, roll.Outcome, roll.Excuse,
                style.FearLeft * fearScale,
                style.HeatLeft * heatScale +
                    paid / 100f * TerritoryTierGuard.HeatPerHundredWeekly,
                lapsed);
            Settled?.Invoke(round, stop, settlement);
            return settlement;
        }

        /// <summary>On to the next door, or turn for home. Answers true while there is
        /// another door to walk to.</summary>
        public bool Advance(TerritoryRound round, double at)
        {
            if (round == null || round.Finished)
                return false;
            round.InTheDoor = false;
            round.StopIndex++;
            round.LastMoveAt = at;
            if (round.HasStop)
                return true;

            // A shakedown and a lean carry nothing home: the walk IS the errand.
            if (round.Kind != TerritoryRoundKind.Collect)
            {
                round.Stage = TerritoryRoundStage.Banked;
                rounds.Remove(round);
                Ended?.Invoke(round);
                return false;
            }

            round.Stage = TerritoryRoundStage.HeadingHome;
            return false;
        }

        /// <summary>
        /// The bag reaches the front. The dues are already settled door by door; what
        /// happens here is the slip and the sum - and the CALLER puts the money in the
        /// house's safe, because a ledger does not know where a safe is.
        /// </summary>
        public int Bank(TerritoryRound round, double gameHour)
        {
            if (round == null || round.Finished)
                return 0;
            round.Stage = TerritoryRoundStage.Banked;
            rounds.Remove(round);
            racket?.FileRound(round.BlockId, round.House, TerritoryDoorNews.RoundBanked,
                gameHour, round.Carried, round.Stops.Count, round.Missed);
            Ended?.Invoke(round);
            return round.Carried;
        }

        /// <summary>A crew retasked mid-round walked away from its own route; whatever
        /// it was carrying never reaches the books. An order countermanded is an order
        /// countermanded. Only a round that was CARRYING something is worth a line.
        /// </summary>
        public void Abandon(TerritoryRound round, double gameHour)
        {
            if (round == null || round.Finished)
                return;
            round.Stage = TerritoryRoundStage.Lost;
            rounds.Remove(round);
            if (round.Carried > 0)
                racket?.FileRound(round.BlockId, round.House, TerritoryDoorNews.RoundLost,
                    gameHour, round.Carried, round.StopIndex, round.Missed);
            Ended?.Invoke(round);
        }

        /// <summary>Every round this crew has out, called off.</summary>
        public void AbandonCrew(int crewId, double gameHour)
        {
            for (var i = rounds.Count - 1; i >= 0; i--)
                if (rounds[i].CrewId == crewId)
                    Abandon(rounds[i], gameHour);
        }
    }

    /// <summary>
    /// THE OTHER CLOCK. The same round, walked with no bodies at all.
    ///
    /// A house that stands physically has its rounds moved by the street - men march,
    /// DoorBeat counts the seconds at the counter, the bag comes home. A house that
    /// does not stand has this: travel priced by <see cref="Outfit.OrderMath.TravelHours"/>
    /// over the metres between doorsteps, a fixed two minutes at each door
    /// (<see cref="TerritoryRoundConfig.PaperStopMinutes"/>), and the same
    /// <see cref="TerritoryRoundLedger"/> settling, advancing and banking.
    ///
    /// Both clocks call the same three methods in the same order, so a round is worth
    /// the same money whichever one drives it.
    /// </summary>
    public sealed class TerritoryPaperClock
    {
        sealed class Walk
        {
            public TerritoryRound Round;
            public TerritoryPoint At;
            public TerritoryPoint Home;
            public bool HasHome;
            public bool HasVehicle;
            public int DrivingHalfSteps;
            public float MachineTop = 1f;

            /// <summary>The game hour the current leg ends - a walk arriving, or a
            /// conversation finishing.</summary>
            public double DueAt;

            /// <summary>True while the men are through the door rather than on it.
            /// </summary>
            public bool Inside;
        }

        readonly TerritoryRoundLedger ledger;
        readonly List<Walk> walks = new List<Walk>();

        public TerritoryPaperClock(TerritoryRoundLedger ledger)
        {
            this.ledger = ledger;
        }

        public int Walking => walks.Count;

        public bool Carries(TerritoryRound round)
        {
            for (var i = 0; i < walks.Count; i++)
                if (walks[i].Round == round)
                    return true;
            return false;
        }

        /// <summary>Puts an opened round on the road. <paramref name="from"/> is where
        /// the crew stands now; <paramref name="home"/> is the front the bag walks to.
        /// Pass <paramref name="hasHome"/> false for a bench with no city, which banks
        /// on the spot the way the street does.</summary>
        public void Send(TerritoryRound round, TerritoryPoint from, TerritoryPoint home,
            bool hasHome, bool hasVehicle, int drivingHalfSteps, float machineTop,
            double gameHour)
        {
            if (round == null || !round.HasStop || Carries(round))
                return;

            var walk = new Walk
            {
                Round = round,
                At = from,
                Home = home,
                HasHome = hasHome,
                HasVehicle = hasVehicle,
                DrivingHalfSteps = drivingHalfSteps,
                MachineTop = machineTop <= 0f ? 1f : machineTop,
            };
            walk.DueAt = gameHour + Leg(walk, from, round.Stop.Doorstep);
            walks.Add(walk);
        }

        /// <summary>Forget a round the world has taken off this clock - a crew wiped, a
        /// round abandoned. The ledger has already closed it.</summary>
        public void Forget(TerritoryRound round)
        {
            for (var i = walks.Count - 1; i >= 0; i--)
                if (walks[i].Round == round)
                    walks.RemoveAt(i);
        }

        public void ForgetCrew(int crewId)
        {
            for (var i = walks.Count - 1; i >= 0; i--)
                if (walks[i].Round != null && walks[i].Round.CrewId == crewId)
                    walks.RemoveAt(i);
        }

        /// <summary>
        /// Move every paper round up to <paramref name="gameHour"/>.
        ///
        /// <paramref name="ask"/> answers what the door owes and who owns it - the same
        /// question the street answers when a body reaches a counter.
        /// <paramref name="banked"/> is handed the sum the round brought home, because
        /// the ledger does not know where a safe is.
        /// </summary>
        public void Tick(double gameHour,
            System.Func<TerritoryRound, TerritoryRoundStop, TerritoryStopInputs> ask,
            System.Action<TerritoryRound, int> banked)
        {
            for (var i = walks.Count - 1; i >= 0; i--)
            {
                var walk = walks[i];
                if (walk.Round == null || walk.Round.Finished)
                {
                    walks.RemoveAt(i);
                    continue;
                }

                var done = false;
                while (!done && gameHour >= walk.DueAt)
                    done = Step(walk, ask, banked);

                if (done || walk.Round.Finished)
                    walks.Remove(walk);
            }
        }

        /// <summary>One leg of the walk. Answers true when the round is over and the
        /// clock should stop asking.</summary>
        bool Step(Walk walk,
            System.Func<TerritoryRound, TerritoryRoundStop, TerritoryStopInputs> ask,
            System.Action<TerritoryRound, int> banked)
        {
            var round = walk.Round;

            if (!walk.Inside)
            {
                // The leg that just ended was a walk. Either it ended at a door, or it
                // ended at the front with the bag.
                if (round.Stage == TerritoryRoundStage.HeadingHome)
                {
                    var carried = ledger.Bank(round, walk.DueAt);
                    banked?.Invoke(round, carried);
                    return true;
                }

                if (!round.HasStop)
                    return true;

                ledger.Arrive(round, walk.DueAt);
                walk.At = round.Stop.Doorstep;
                walk.Inside = true;
                walk.DueAt += TerritoryRoundConfig.PaperStopHours;
                return false;
            }

            // The conversation is over. Settle where they stand, then turn for the next
            // door or for home.
            var stop = round.Stop;
            if (ask != null)
                ledger.Settle(round, ask(round, stop), walk.DueAt);
            walk.Inside = false;

            if (ledger.Advance(round, walk.DueAt))
            {
                walk.DueAt += Leg(walk, walk.At, round.Stop.Doorstep);
                return false;
            }

            if (round.Stage != TerritoryRoundStage.HeadingHome)
                return true;

            if (!walk.HasHome)
            {
                // No front to walk to - a bench rig, or a house whose headquarters is
                // gone. The street banks on the spot in that case, and so does paper.
                var carried = ledger.Bank(round, walk.DueAt);
                banked?.Invoke(round, carried);
                return true;
            }

            walk.DueAt += Leg(walk, walk.At, walk.Home);
            return false;
        }

        float Leg(Walk walk, TerritoryPoint from, TerritoryPoint to) =>
            Outfit.OrderMath.TravelHours(
                Metres(from, to), walk.HasVehicle, walk.DrivingHalfSteps,
                walk.MachineTop);

        static float Metres(TerritoryPoint from, TerritoryPoint to)
        {
            if (!from.IsFinite || !to.IsFinite)
                return 0f;
            var dx = to.X - from.X;
            var dz = to.Z - from.Z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// THE ROUNDS THAT GO OUT BY THEMSELVES, for any house.
    ///
    /// Every block on a lieutenant's paper has a collection weekday of its own
    /// (<see cref="TerritoryCollectionSchedule"/>). On that day, once the shops are
    /// open, a crew of his that has a man on the bag walks the block's paying doors
    /// without being told. Nothing else is automatic: a DEMAND is still an order
    /// somebody gives.
    ///
    /// Pure: it decides WHO is due and hands each one to the caller's
    /// <c>submit</c>, which is the command gateway. It never opens a round itself.
    /// </summary>
    public sealed class TerritoryRoundScheduler
    {
        readonly HashSet<(int crewId, string blockId, int day)> sent =
            new HashSet<(int, string, int)>();
        readonly List<Personnel.Character> collectors = new List<Personnel.Character>();

        /// <summary>What the block's paying doors owe this house right now.</summary>
        public System.Func<TerritoryGangId, TerritoryBlockId, int> Owed;

        /// <summary>How many of its doors owe anything - the round slip's stop count.
        /// </summary>
        public System.Func<TerritoryGangId, TerritoryBlockId, int> StopsOwing;

        /// <summary>A round was taken: the house, the lieutenant who answers for the
        /// block, the block, what it owes and over how many doors. The caller files it
        /// on the wire; the scheduler owes the street nothing.</summary>
        public System.Action<Outfit.House, Personnel.Character, TerritoryBlockId,
            int, int> Filed;

        public void Forget() => sent.Clear();

        /// <summary>
        /// Send what this house's paper says is due. <paramref name="submit"/> puts the
        /// order through the gateway and answers whether it was taken - a crew in a
        /// fight or in a car is refused, and the next tick asks again the same day.
        /// </summary>
        public void Tend(Outfit.House house, int day, int dayOfWeek, int hourOfDay,
            TerritoryRoundLedger ledger,
            System.Func<Outfit.House, Personnel.Crew, TerritoryBlockId, bool> submit)
        {
            if (house == null || house.Finished || house.Roster == null ||
                ledger == null || submit == null)
                return;

            var roster = house.Roster;
            var mine = new TerritoryGangId(house.GangId);
            var paper = roster.Organization.BlockResponsibilities;

            for (var i = 0; i < paper.Count; i++)
            {
                var blockId = paper[i].BlockId;
                if (!blockId.IsValid)
                    continue;

                // The crew whose lieutenant answers for this block. No crew, no round -
                // paper alone does not collect anything.
                Personnel.Crew crew = null;
                for (var c = 0; c < roster.Crews.Count && crew == null; c++)
                    if (roster.Crews[c].LieutenantId == paper[i].LeaderId)
                        crew = roster.Crews[c];
                if (crew == null)
                    continue;

                var key = (crew.Id, blockId.Value, day);
                if (sent.Contains(key))
                    continue;

                Personnel.RosterOps.CollectorsOf(roster, crew.Id, collectors);
                var owed = Owed != null ? Owed(mine, blockId) : 0;

                if (!TerritoryCollectionSchedule.ShouldSend(
                        dayOfWeek, hourOfDay, blockId, owed,
                        collectors.Count > 0, ledger.RoundRunning(crew.Id), false))
                    continue;

                if (!submit(house, crew, blockId))
                    continue;

                sent.Add(key);
                Filed?.Invoke(house, roster.Find(paper[i].LeaderId), blockId, owed,
                    StopsOwing != null ? StopsOwing(mine, blockId) : 0);
            }

            // The book only has to remember today; anything older can never match again.
            if (sent.Count > 64)
                sent.RemoveWhere(entry => entry.day != day);
        }
    }
}
