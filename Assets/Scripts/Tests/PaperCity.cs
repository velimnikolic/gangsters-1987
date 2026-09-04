using System.Collections.Generic;
using LivingCity.Business;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// A CITY WITH NOTHING IN IT BUT DOORS. Four blocks a family (AI-008): the street it
    /// lives on and three beside it, laid in one row so every block has neighbours and
    /// the last block of one family's strip touches the first of the next's - which is
    /// where a border is. Doors per block vary with the seed, and each door pays what
    /// the real price table charges its trade, so a block is worth what a block is
    /// worth and not a flat figure.
    ///
    /// Nothing here decides anything a mind or a ledger decides. It answers where things
    /// are and how far apart, and it carries an intent to the same pure calls the runtime
    /// carries it to - the racket's own Demand, the round ledger's own Open. The city
    /// itself is the only thing that is fictional.
    /// </summary>
    public sealed class PaperCity
    {
        public const int BlocksPerHouse = 4;
        public const int MinDoors = 3;
        public const int MaxDoors = 6;

        /// <summary>The generic ground-floor trades - the pool an unlabelled shopfront
        /// is dealt from in the real city, and the rates the yardstick prices doors at.
        /// </summary>
        static readonly BusinessArchetypeId[] Trades =
        {
            BusinessArchetypeId.Grocer, BusinessArchetypeId.Butcher,
            BusinessArchetypeId.Baker, BusinessArchetypeId.Barber,
            BusinessArchetypeId.Tailor, BusinessArchetypeId.Laundry,
            BusinessArchetypeId.Pharmacy, BusinessArchetypeId.Hardware,
            BusinessArchetypeId.Bookshop, BusinessArchetypeId.RecordShop,
            BusinessArchetypeId.Florist, BusinessArchetypeId.Newsstand,
            BusinessArchetypeId.Cobbler, BusinessArchetypeId.Locksmith,
            BusinessArchetypeId.PawnShop, BusinessArchetypeId.ElectricalShop,
            BusinessArchetypeId.TravelAgent, BusinessArchetypeId.BettingShop,
        };

        readonly TerritoryBlockId[] blocks;
        readonly int[] doorsPerBlock;
        readonly Dictionary<string, float> presence = new Dictionary<string, float>();
        readonly Dictionary<int, (TerritoryGangId House, TerritoryBlockId Block)> posted =
            new Dictionary<int, (TerritoryGangId, TerritoryBlockId)>();
        readonly List<HouseDoor> doorScratch = new List<HouseDoor>();
        readonly List<TerritoryBlockId> viewBlocks = new List<TerritoryBlockId>();
        readonly List<HouseDefiance> defiances = new List<HouseDefiance>();
        readonly List<TerritoryGangId> rivals = new List<TerritoryGangId>();
        readonly List<TerritoryRoundStop> stops = new List<TerritoryRoundStop>();
        readonly List<Character> bag = new List<Character>();

        /// <summary>The two memories the runtime keeps per house: what it was refused
        /// (P4) and when it last walked a block (A21).</summary>
        readonly Dictionary<int, HouseBackoffs> backoffs = new Dictionary<int, HouseBackoffs>();
        readonly Dictionary<(int gang, TerritoryBlockId block), double> walked =
            new Dictionary<(int, TerritoryBlockId), double>();

        public readonly int Seed;
        public double Hour;
        public int LastDay = -1;

        /// <summary>
        /// THE RACKET THIS CITY'S DOORS ARE ON. Handed over once, because a block is
        /// not held by men alone: control is presence, fear and COMPLIANCE together
        /// (TerritoryControlConfig), and a paper city that read leadership off standing
        /// men only rewarded a crew for loitering. That bias is what made a slower mind
        /// look better than a faster one on the cadence table, so the yardstick reads
        /// the real formula now (AI-008).
        /// </summary>
        public TerritoryRacketLedger Racket;

        static readonly TerritoryControlConfig Control = TerritoryControlConfig.Default;
        readonly List<TerritoryControlScore> scoreScratch = new List<TerritoryControlScore>();

        public PaperCity(int houses, int seed = 1987)
        {
            Seed = seed;
            blocks = new TerritoryBlockId[houses * BlocksPerHouse];
            doorsPerBlock = new int[blocks.Length];
            for (var b = 0; b < blocks.Length; b++)
            {
                blocks[b] = new TerritoryBlockId("block:" + b);
                // Varied, deterministic, and never a flat four: the same seed deals
                // the same street twice, and thirty seeds deal thirty streets.
                doorsPerBlock[b] = MinDoors +
                    (int)((uint)Potential.Mix(seed, b * 7 + 3) % (MaxDoors - MinDoors + 1));
            }
        }

        public int Blocks => blocks.Length;
        public int Day => (int)(Hour / 24.0);

        public TerritoryBlockId BlockAt(int index) =>
            index >= 0 && index < blocks.Length ? blocks[index] : default;

        public TerritoryBlockId HomeBlockOf(int gangId) => BlockAt(gangId * BlocksPerHouse);

        /// <summary>How many doors this block has.</summary>
        public int DoorsOn(TerritoryBlockId blockId)
        {
            var index = IndexOf(blockId);
            return index < 0 ? 0 : doorsPerBlock[index];
        }

        public TerritoryBusinessId Door(TerritoryBlockId blockId, int index) =>
            new TerritoryBusinessId("biz:" + blockId.Value + ":" + index);

        /// <summary>What a week of protection is worth at this door - the price table's
        /// own figure for the trade the seed dealt there.</summary>
        public int RateOf(TerritoryBusinessId businessId) =>
            EconomyPrices.ProtectionPerWeek(TradeOf(businessId));

        BusinessArchetypeId TradeOf(TerritoryBusinessId businessId)
        {
            var mix = Potential.Mix(Seed, businessId.Value.GetHashCode());
            return Trades[(int)((uint)mix % (uint)Trades.Length)];
        }

        public TerritoryPoint Doorstep(TerritoryBlockId blockId, int index)
        {
            var block = IndexOf(blockId);
            return new TerritoryPoint(
                block * UnderworldSim.BlockMetres + index * UnderworldSim.DoorstepMetres,
                0f);
        }

        int IndexOf(TerritoryBlockId blockId)
        {
            for (var i = 0; i < blocks.Length; i++)
                if (blocks[i] == blockId)
                    return i;
            return -1;
        }

        public HouseBackoffs BackoffsOf(int gangId)
        {
            if (!backoffs.TryGetValue(gangId, out var book))
            {
                book = new HouseBackoffs();
                backoffs.Add(gangId, book);
            }
            return book;
        }

        // ------------------------------------------------------------------ standing

        static string Key(TerritoryBlockId blockId, TerritoryGangId gang) =>
            blockId.Value + "/" + gang.Value;

        public float Presence(TerritoryBlockId blockId, TerritoryGangId gang) =>
            presence.TryGetValue(Key(blockId, gang), out var value) ? value : 0f;

        public void Stand(TerritoryBlockId blockId, TerritoryGangId gang, float amount)
        {
            if (!blockId.IsValid)
                return;
            var key = Key(blockId, gang);
            presence.TryGetValue(key, out var value);
            value += amount;
            presence[key] = value > UnderworldSim.PresenceCap
                ? UnderworldSim.PresenceCap
                : value;
        }

        /// <summary>The crews this family has walked onto other streets.</summary>
        public void StandPosted(TerritoryGangId gang, float amount)
        {
            foreach (var posting in posted)
                if (posting.Value.House == gang)
                    Stand(posting.Value.Block, gang, amount);
        }

        public IReadOnlyList<TerritoryBlockId> Neighbours(TerritoryBlockId blockId)
        {
            var list = new List<TerritoryBlockId>();
            var index = IndexOf(blockId);
            if (index < 0)
                return list;
            if (index > 0)
                list.Add(blocks[index - 1]);
            if (index < blocks.Length - 1)
                list.Add(blocks[index + 1]);
            return list;
        }

        public int BlocksLedBy(int gangId)
        {
            var led = 0;
            for (var b = 0; b < blocks.Length; b++)
            {
                var leader = Leader(blocks[b]);
                if (leader.IsValid && leader.Value == gangId)
                    led++;
            }
            return led;
        }

        /// <summary>
        /// WHO HOLDS THIS STREET, by the city's own reading: presence, fear and the
        /// share of its doors that pay, weighed and classified by
        /// <see cref="TerritoryControlReading"/> - the same arithmetic the real control
        /// ledger runs. A bench needs one answer and this is it.
        ///
        /// NOBODY IS NOT HOUSE ZERO: a default TerritoryGangId reads as gang 0 unless
        /// it is built invalid, and a street nobody stands on read as the player's for
        /// a whole sweep once.
        /// </summary>
        public TerritoryGangId Leader(TerritoryBlockId blockId)
        {
            Read(blockId, out var leader, out var state);
            return state == TerritoryControlState.Controlled ||
                   state == TerritoryControlState.Dominated ||
                   state == TerritoryControlState.Contested
                ? leader
                : new TerritoryGangId(-1);
        }

        public TerritoryControlState StateOf(TerritoryBlockId blockId)
        {
            Read(blockId, out _, out var state);
            return state;
        }

        void Read(TerritoryBlockId blockId, out TerritoryGangId leader,
            out TerritoryControlState state)
        {
            scoreScratch.Clear();
            var doors = DoorsOn(blockId);
            foreach (var pair in presence)
            {
                var slash = pair.Key.LastIndexOf('/');
                if (slash < 0 || pair.Key.Substring(0, slash) != blockId.Value)
                    continue;
                var gang = new TerritoryGangId(int.Parse(pair.Key.Substring(slash + 1)));

                // The share of the street's doors that pay this family. Fear and
                // standing are one thing on paper (men on a corner for a week are
                // known on it); compliance is the ledger's own.
                var paying = 0;
                for (var d = 0; Racket != null && d < doors; d++)
                    if (Racket.StateOf(Door(blockId, d), gang) ==
                        TerritoryProtectionState.Compliant)
                        paying++;
                var compliance = doors > 0 ? 100f * paying / doors : 0f;

                scoreScratch.Add(Control.Score(new TerritoryControlInputs(
                    gang, pair.Value, pair.Value, compliance, 1f)));
            }

            state = TerritoryControlReading.Read(
                scoreScratch, Control, alreadyContested: false, out leader, out _, out _);
            if (!leader.IsValid || state == TerritoryControlState.Uncontrolled)
                leader = new TerritoryGangId(-1);
        }

        // ---------------------------------------------------------------- the money

        public int Owed(TerritoryRacketLedger racket, TerritoryDuesLedger dues,
            TerritoryBlockId blockId, TerritoryGangId gang)
        {
            var owed = 0;
            for (var d = 0; d < DoorsOn(blockId); d++)
            {
                var businessId = Door(blockId, d);
                if (racket.StateOf(businessId, gang) ==
                    TerritoryProtectionState.Compliant)
                    owed += dues.OwedOf(businessId, gang);
            }
            return owed;
        }

        public int Stops(TerritoryDuesLedger dues, TerritoryBlockId blockId,
            TerritoryGangId gang)
        {
            var count = 0;
            for (var d = 0; d < DoorsOn(blockId); d++)
                if (dues.OwedOf(Door(blockId, d), gang) > 0)
                    count++;
            return count;
        }

        // ----------------------------------------------------------------- the view

        public HouseView Look(Underworld world, TerritoryRacketLedger racket,
            TerritoryDuesLedger dues, House house, HouseMindConfig config,
            TerritoryRoundLedger rounds = null)
        {
            var mine = new TerritoryGangId(house.GangId);
            var backoff = BackoffsOf(house.GangId);
            backoff.Sweep(Hour);

            viewBlocks.Clear();
            viewBlocks.Add(HomeBlockOf(house.GangId));
            var paper = house.Roster.Organization.BlockResponsibilities;
            for (var i = 0; i < paper.Count; i++)
                if (paper[i].BlockId.IsValid && !viewBlocks.Contains(paper[i].BlockId))
                    viewBlocks.Add(paper[i].BlockId);
            for (var b = 0; b < blocks.Length; b++)
                if (Presence(blocks[b], mine) > 0f && !viewBlocks.Contains(blocks[b]))
                    viewBlocks.Add(blocks[b]);

            defiances.Clear();
            for (var i = 0; i < viewBlocks.Count; i++)
                for (var d = 0; d < DoorsOn(viewBlocks[i]); d++)
                {
                    var businessId = Door(viewBlocks[i], d);
                    if (!racket.TryGetRelationship(businessId, mine, out var row) ||
                        row.State == TerritoryProtectionState.Compliant)
                        continue;
                    // The runtime's own two rules (CollectDefiances): a door that
                    // ever refused us, and a hesitant door opened at its last visit.
                    if (row.RefusedAt >= 0.0)
                        defiances.Add(new HouseDefiance(
                            businessId, viewBlocks[i], row.RefusedAt, row.Threats));
                    else if (row.State == TerritoryProtectionState.Hesitant)
                        defiances.Add(new HouseDefiance(
                            businessId, viewBlocks[i], row.LastInteraction, row.Threats));
                }

            rivals.Clear();
            for (var g = 0; g < world.Count; g++)
                if (g != house.GangId && world.Of(g) != null && !world.Of(g).Extinct)
                    rivals.Add(new TerritoryGangId(g));

            return new HouseView
            {
                House = mine,
                Roster = house.Roster,
                Accounts = house.Runner.Accounts,
                Book = house.Runner.Book,
                Front = house.Front,
                FrontBlock = HomeBlockOf(house.GangId),
                Blocks = viewBlocks,
                NeighbourLook = Neighbours,
                DoorLook = blockId => Doors(racket, dues, blockId, mine, house),
                PresenceLook = blockId => Presence(blockId, mine),
                FearLook = blockId => Presence(blockId, mine),
                AttentionLook = blockId => 0f,
                ControlLook = StateOf,
                LeaderLook = Leader,
                StanceLook = other =>
                    world.Relations.StanceBetween(house.GangId, other.Value),
                LadderLook = other =>
                    world.Relations.StepOf(house.GangId, other.Value),
                EnduranceLook = other => HouseRelations.Estimate(
                    HouseRelations.Endurance(
                        world.Of(other.Value).Runner.Accounts.Safe,
                        Wages.DailyPayroll(world.Of(other.Value).Roster)),
                    house.Runner.Seed, Day, house.GangId, other.Value),
                Rivals = rivals,
                Defiances = defiances,
                QuietThinks = house.QuietThinks,
                BackoffLook = key => backoff.Blocked(key, Hour),
                RoundLook = crewId => rounds != null && rounds.RoundRunning(crewId),
                WalkedLook = blockId =>
                    walked.TryGetValue((house.GangId, blockId), out var at) ? at : -1.0,
                CrewBlockLook = crewId =>
                    posted.TryGetValue(crewId, out var post) && post.House == mine
                        ? post.Block
                        : HomeBlockOf(house.GangId),
                GameHour = Hour,
                Day = Day + 1,
            };
        }

        IReadOnlyList<HouseDoor> Doors(TerritoryRacketLedger racket,
            TerritoryDuesLedger dues, TerritoryBlockId blockId, TerritoryGangId mine,
            House house)
        {
            doorScratch.Clear();
            for (var d = 0; d < DoorsOn(blockId); d++)
            {
                var businessId = Door(blockId, d);
                racket.TryGetProtector(businessId, out var protector);
                var owed = dues.OwedOf(businessId, mine);
                var rate = RateOf(businessId);
                var late = protector == mine && dues.TryGet(businessId, out var account) &&
                           TerritoryCollectionSchedule.IsLate(
                               owed, rate, Day, account.LastCollectedDay);
                var lastInteraction = -1.0;
                var demands = 0;
                if (racket.TryGetRelationship(businessId, mine, out var row))
                {
                    lastInteraction = row.LastInteraction;
                    demands = row.Demands;
                }
                doorScratch.Add(new HouseDoor(
                    businessId, 1, rate, protector, racket.StateOf(businessId, mine),
                    owed, false, true,
                    businessId == house.Front
                        ? DoorTenure.Ours
                        : protector == mine
                            ? DoorTenure.Paying
                            : protector.IsValid
                                ? DoorTenure.Rival
                                : DoorTenure.Open,
                    late, lastInteraction, demands));
            }
            return doorScratch;
        }

        // ---------------------------------------------------------------- the doing

        public string Carry(Underworld world, TerritoryRacketLedger racket,
            TerritoryDuesLedger dues, TerritoryRoundLedger rounds,
            TerritoryPaperClock clock, House house, HouseIntent intent,
            UnderworldSim.Ledger book)
        {
            var mine = new TerritoryGangId(house.GangId);
            switch (intent.Kind)
            {
                case HouseIntentKind.Command:
                    return Order(
                        world, racket, dues, rounds, clock, house, mine, intent, book);

                case HouseIntentKind.Job:
                    if (intent.Job == null)
                        return "no order";
                    intent.Job.GangId = house.GangId;
                    var issued = world.Issue(intent.Job);
                    if (issued.Ok && book != null)
                    {
                        if (intent.Job.Type == OrderType.Recruit && book.Signed < 0)
                            book.Signed = Day;
                        if ((intent.Job.Type == OrderType.Guard ||
                             intent.Job.Type == OrderType.Assault) &&
                            book.Answered < 0 &&
                            (intent.Tier == HouseMind.TierAnswer ||
                             intent.Tier == HouseMind.TierSurvive))
                            book.Answered = Day;
                    }
                    return issued.Reason;

                case HouseIntentKind.SetDuty:
                    return HouseOps.SetDuty(house, intent.CharacterId, intent.Duty).Reason;

                case HouseIntentKind.AssignToCrew:
                    return HouseOps.AssignToCrew(
                        house, intent.CharacterId, intent.CrewId).Reason;

                case HouseIntentKind.Promote:
                    return HouseOps.Promote(house, intent.CharacterId, out _).Reason;

                case HouseIntentKind.Demote:
                    return HouseOps.Demote(house, intent.CharacterId).Reason;

                case HouseIntentKind.SetPolicy:
                    return HouseOps.SetPolicy(house, intent.CrewId, intent.Policy).Reason;

                case HouseIntentKind.AssignBlock:
                    return HouseOps.AssignBlock(
                        house, intent.BlockId, intent.CharacterId, true).Reason;

                case HouseIntentKind.Buy:
                    var paid = HouseOps.Purchase(house, intent.Price);
                    if (!paid.Ok)
                        return paid.Reason;
                    var item = RosterOps.AddEquipment(
                        house.Roster, intent.Kit, intent.Listing, intent.Price);
                    return RosterOps.GiveEquipment(
                        house.Roster, item.Id, intent.CharacterId).Reason;

                case HouseIntentKind.SetStance:
                    world.Relations.SetPending(
                        house.GangId, intent.Other.Value, intent.Stance);
                    return "";

                case HouseIntentKind.Warn:
                    return "";

                case HouseIntentKind.Cancel:
                    return house.Runner.Cancel(house.Roster, intent.CharacterId).Reason;

                case HouseIntentKind.Bail:
                    return "there is no station in a paper city";

                case HouseIntentKind.Retain:
                    return "there is no courthouse in a paper city";
            }
            return "nothing to do";
        }

        string Order(Underworld world, TerritoryRacketLedger racket,
            TerritoryDuesLedger dues, TerritoryRoundLedger rounds,
            TerritoryPaperClock clock, House house, TerritoryGangId mine,
            HouseIntent intent, UnderworldSim.Ledger book)
        {
            switch (intent.Order)
            {
                case HouseOrder.OperateInBlock:
                    posted[intent.CrewId] = (mine, intent.BlockId);
                    if (book != null && book.TookAStreet < 0 &&
                        intent.BlockId != HomeBlockOf(house.GangId))
                        book.TookAStreet = Day;
                    return "";

                case HouseOrder.ApproachBusiness:
                    racket.Approach(intent.BusinessId, mine, Hour, null, false);
                    if (intent.FollowUp == TerritoryRacketIntent.Demand)
                    {
                        if (book != null && book.AskedADoor < 0)
                            book.AskedADoor = Day;
                        racket.Demand(
                            intent.BusinessId, mine, Asking(intent.BusinessId, mine), Hour,
                            out _);
                    }
                    else if (intent.FollowUp == TerritoryRacketIntent.Threaten)
                        racket.Threaten(intent.BusinessId, mine, Hour);
                    return "";

                case HouseOrder.LeanOnHoldouts:
                case HouseOrder.ShakeDownBlock:
                    var asked = 0;
                    for (var d = 0; d < DoorsOn(intent.BlockId); d++)
                    {
                        var businessId = Door(intent.BlockId, d);
                        var state = racket.StateOf(businessId, mine);
                        var ours = businessId == house.Front;
                        if (intent.Order == HouseOrder.LeanOnHoldouts)
                        {
                            if (!TerritoryShakedown.IsHoldout(state, ours))
                                continue;
                            racket.Threaten(businessId, mine, Hour);
                        }
                        else
                        {
                            if (!TerritoryShakedown.WorthAsking(state, ours))
                                continue;
                            racket.Approach(businessId, mine, Hour, null, false);
                            racket.Demand(
                                businessId, mine, Asking(businessId, mine), Hour, out _);
                            if (book != null && book.AskedADoor < 0)
                                book.AskedADoor = Day;
                        }
                        asked++;
                    }
                    if (asked == 0)
                        return intent.Order == HouseOrder.LeanOnHoldouts
                            ? "nobody is holding out"
                            : "every door here has answered us";
                    if (intent.Order == HouseOrder.ShakeDownBlock)
                        walked[(house.GangId, intent.BlockId)] = Hour;
                    return "";

                case HouseOrder.CollectDues:
                    return Send(house, house.Roster.FindCrew(intent.CrewId),
                        intent.BlockId, racket, dues, rounds, clock)
                        ? ""
                        : "nothing owed there";
            }
            return "no such order";
        }

        TerritoryComplianceInputs Asking(TerritoryBusinessId businessId,
            TerritoryGangId mine)
        {
            var blockId = BlockOf(businessId);
            var standing = Presence(blockId, mine);
            return new TerritoryComplianceInputs(standing, standing, 0f, 0f, 0f, false);
        }

        TerritoryBlockId BlockOf(TerritoryBusinessId businessId)
        {
            for (var b = 0; b < blocks.Length; b++)
                for (var d = 0; d < doorsPerBlock[b]; d++)
                    if (Door(blocks[b], d) == businessId)
                        return blocks[b];
            return default;
        }

        // --------------------------------------------------------------- the rounds

        public bool Send(House house, Crew crew, TerritoryBlockId blockId,
            TerritoryRacketLedger racket, TerritoryDuesLedger dues,
            TerritoryRoundLedger rounds, TerritoryPaperClock clock)
        {
            if (house == null || crew == null || rounds.RoundRunning(crew.Id))
                return false;

            var mine = new TerritoryGangId(house.GangId);
            stops.Clear();
            for (var d = 0; d < DoorsOn(blockId); d++)
            {
                var businessId = Door(blockId, d);
                if (racket.StateOf(businessId, mine) !=
                    TerritoryProtectionState.Compliant ||
                    dues.OwedOf(businessId, mine) <= 0)
                    continue;
                stops.Add(new TerritoryRoundStop(businessId, Doorstep(blockId, d)));
            }
            if (stops.Count == 0)
                return false;

            RosterOps.CollectorsOf(house.Roster, crew.Id, bag);
            var round = rounds.Open(
                mine, crew.Id, bag.Count > 0 ? bag[0].Id : -1, blockId,
                TerritoryRoundKind.Collect, stops, Hour);
            if (round == null)
                return false;
            round.Origin = TerritoryRoundOrigin.Schedule;

            var home = Doorstep(HomeBlockOf(house.GangId), 0);
            clock.Send(round, stops[0].Doorstep, home, true,
                CrewKit.HasVehicle(house.Roster, crew),
                CrewKit.BestAt(house.Roster, crew, CharacterAttribute.Driving),
                CrewKit.MachineTopOf(house.Roster, crew), Hour);
            return true;
        }

        public TerritoryStopInputs Stop(TerritoryRacketLedger racket,
            TerritoryDuesLedger dues, TerritoryRound round, TerritoryRoundStop stop,
            int seed) =>
            new TerritoryStopInputs(
                true, dues.OwedOf(stop.BusinessId, round.House),
                TerritoryOwnerProfile.Deal(seed, stop.BusinessId),
                Presence(round.BlockId, round.House), 0f,
                (int)CrewPolicy.Normal, (int)LieutenantArchetype.Soldier, seed, Day);

        // ---------------------------------------------------------------- readings

        /// <summary>The doors of the whole city as this house stands with them, for
        /// the table: paying, hesitant, refused, and the most any one door has been
        /// asked.</summary>
        public void CountDoors(TerritoryRacketLedger racket, int gangId,
            out int paying, out int hesitant, out int refused, out int worstDemands)
        {
            paying = 0;
            hesitant = 0;
            refused = 0;
            worstDemands = 0;
            var mine = new TerritoryGangId(gangId);
            for (var b = 0; b < blocks.Length; b++)
                for (var d = 0; d < doorsPerBlock[b]; d++)
                {
                    var businessId = Door(blocks[b], d);
                    var state = racket.StateOf(businessId, mine);
                    if (state == TerritoryProtectionState.Compliant) paying++;
                    else if (state == TerritoryProtectionState.Hesitant) hesitant++;
                    else if (state == TerritoryProtectionState.Defiant) refused++;
                    if (racket.TryGetRelationship(businessId, mine, out var row) &&
                        row.Demands > worstDemands)
                        worstDemands = row.Demands;
                }
        }

        /// <summary>Every block this house leads, in the order of the row.</summary>
        public void BlocksLed(int gangId, List<TerritoryBlockId> into)
        {
            into?.Clear();
            for (var b = 0; into != null && b < blocks.Length; b++)
            {
                var leader = Leader(blocks[b]);
                if (leader.IsValid && leader.Value == gangId)
                    into.Add(blocks[b]);
            }
        }
    }
}
