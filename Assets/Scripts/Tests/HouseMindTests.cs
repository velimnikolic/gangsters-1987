using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// RIVAL-005. One family, running itself, without cheating.
    ///
    /// The rig is a paper city: three blocks in a row, four doors each, twenty metres
    /// between doorsteps, the family's front on the first block. Nothing is rendered and
    /// nobody stands anywhere - the rounds walk on <see cref="TerritoryPaperClock"/> and
    /// the orders go through the same ledgers the street's do.
    ///
    /// The proof is the MVP: the family loses a hood, signs and deploys a replacement,
    /// walks onto the next block, asks a door and leans on it when it says no, collects
    /// what it is owed, carries the bag home, and pays its men out of that money - inside
    /// fourteen game days, for every seed from 1 to 30.
    /// </summary>
    public static class HouseMindTests
    {
        const int MvpDays = 14;
        const int Seeds = 30;

        /// <summary>Days-to-complete per seed, in the order they were run - the tally the
        /// verdict is read from rather than a pass/fail bit (BalanceNotes style).
        /// </summary>
        public static readonly List<string> Notes = new List<string>();

        public static List<string> Run()
        {
            var failures = new List<string>();
            Notes.Clear();

            AHoodLostIsAHoodReplaced(failures);
            TheFamilyWalksOntoTheNextStreet(failures);
            ARefusalGetsOneThreatAndOneLean(failures);
            TheMindNeverAsksADoorAnotherHouseHolds(failures);
            TheMindReadsNothingButItsView(failures);
            TheMvpRunsForEverySeed(failures);

            return failures;
        }

        // ------------------------------------------------------------------- the city

        /// <summary>Three blocks in a row: A - B - C. A is the family's own street.
        /// </summary>
        sealed class RigCity
        {
            public const int Doors = 4;
            public const float DoorstepMetres = 20f;

            /// <summary>What a crew standing on a block adds to its presence every game
            /// hour. The real presence ledger samples bodies; a paper city has none, so a
            /// posted crew simply builds up.</summary>
            public const float PresencePerHour = 4f;

            public const float PresenceCap = 60f;

            public readonly TerritoryBlockId[] BlockIds =
            {
                new TerritoryBlockId("block:a"),
                new TerritoryBlockId("block:b"),
                new TerritoryBlockId("block:c"),
            };

            public readonly TerritoryRacketLedger Racket = new TerritoryRacketLedger();
            public readonly TerritoryDuesLedger Dues = new TerritoryDuesLedger();
            public readonly TerritoryRoundLedger Rounds;
            public readonly TerritoryPaperClock Clock;
            public readonly TerritoryRoundScheduler Scheduler = new TerritoryRoundScheduler();

            public readonly House House;
            public readonly List<TerritoryProtectionChange> Changes =
                new List<TerritoryProtectionChange>();

            readonly Dictionary<TerritoryBlockId, List<TerritoryBusinessId>> doors =
                new Dictionary<TerritoryBlockId, List<TerritoryBusinessId>>();
            readonly Dictionary<TerritoryBusinessId, TerritoryPoint> doorsteps =
                new Dictionary<TerritoryBusinessId, TerritoryPoint>();
            readonly Dictionary<TerritoryBusinessId, TerritoryBlockId> owner =
                new Dictionary<TerritoryBusinessId, TerritoryBlockId>();
            readonly Dictionary<string, float> presence = new Dictionary<string, float>();
            readonly Dictionary<int, TerritoryBlockId> posted =
                new Dictionary<int, TerritoryBlockId>();
            readonly List<HouseIntent> intents = new List<HouseIntent>();
            readonly List<string> refused = new List<string>();
            readonly List<HouseDefiance> defiances = new List<HouseDefiance>();
            readonly List<TerritoryBlockId> seen = new List<TerritoryBlockId>();

            /// <summary>The city's own seed, so the shopkeepers and their answers are
            /// this run's and not one fixture's. Thirty seeds that all deal the same
            /// owner would prove nothing about thirty cities.</summary>
            public readonly int Seed;

            public double Hour;
            public int LastDay = -1;

            /// <summary>What the run is watching for - the MVP's own steps.</summary>
            public bool Signed;
            public bool Deployed;
            public bool Entered;
            public bool Demanded;
            public bool Leant;
            public int Banked;
            public bool Paid;

            /// <summary>What the run actually did, so a seed that fails says WHY rather
            /// than only that it did.</summary>
            public int RoundsOut;
            public int StopsWalked;
            public int Missed;

            public TerritoryGangId Mine => new TerritoryGangId(House.GangId);
            public int Day => (int)(Hour / 24.0);

            public RigCity(int seed, int gangId)
            {
                Seed = seed;
                Rounds = new TerritoryRoundLedger(Racket, Dues);
                Clock = new TerritoryPaperClock(Rounds);

                for (var b = 0; b < BlockIds.Length; b++)
                {
                    var list = new List<TerritoryBusinessId>();
                    doors.Add(BlockIds[b], list);
                    for (var d = 0; d < Doors; d++)
                    {
                        var businessId = new TerritoryBusinessId(
                            "biz:" + (char)('a' + b) + d);
                        list.Add(businessId);
                        owner.Add(businessId, BlockIds[b]);
                        doorsteps.Add(businessId, new TerritoryPoint(
                            b * 200f + d * DoorstepMetres, 0f));
                    }
                }

                var roster = RosterSeeder.Generate(seed, gangId);
                House = new House(gangId, roster, new CampaignRunner { Seed = seed });
                House.Runner.OpenFirstSheet();
                House.Front = doors[BlockIds[0]][0];

                // The family already holds two doors on its own street - the deal the
                // city gives every house on day one, and enough for a round to be a walk
                // rather than a single knock.
                Racket.Demand(doors[BlockIds[0]][1], Mine, Strong(), 1.0, out _);
                Racket.Demand(doors[BlockIds[0]][2], Mine, Strong(), 1.0, out _);

                Rounds.Settled = (round, stop, settlement) =>
                {
                    if (settlement.Missed)
                        Missed++;
                };

                // Its lieutenant answers for that street, so the schedule has paper to
                // read. Everything past this the family does for itself.
                if (roster.Crews.Count > 0)
                    RosterOps.AssignBlockResponsibility(
                        roster, BlockIds[0], roster.Crews[0].LieutenantId, true);

                Scheduler.Owed = (gang, blockId) => Owed(blockId, gang);
                Scheduler.StopsOwing = (gang, blockId) => Stops(blockId, gang);

                // THE ONLY PLACE THE TAKE BECOMES THIS FAMILY'S MONEY, exactly as the
                // street does it: the ledger says what came home, the caller banks it.
                Rounds.Ended = round =>
                {
                    if (round.Stage != TerritoryRoundStage.Banked || round.Carried <= 0)
                        return;
                    House.Runner.BankCollection(round.Carried);
                    Banked += round.Carried;
                };
            }

            public IReadOnlyList<TerritoryBusinessId> DoorsOn(TerritoryBlockId blockId) =>
                doors.TryGetValue(blockId, out var list)
                    ? (IReadOnlyList<TerritoryBusinessId>)list
                    : new List<TerritoryBusinessId>();

            public float Presence(TerritoryBlockId blockId, TerritoryGangId gang) =>
                presence.TryGetValue(Key(blockId, gang), out var value) ? value : 0f;

            static string Key(TerritoryBlockId blockId, TerritoryGangId gang) =>
                blockId.Value + "/" + gang.Value;

            public void Stand(TerritoryBlockId blockId, TerritoryGangId gang, float amount)
            {
                var key = Key(blockId, gang);
                presence.TryGetValue(key, out var value);
                value += amount;
                presence[key] = value > PresenceCap ? PresenceCap : value;
            }

            public IReadOnlyList<TerritoryBlockId> Neighbours(TerritoryBlockId blockId)
            {
                var list = new List<TerritoryBlockId>();
                for (var i = 0; i < BlockIds.Length; i++)
                {
                    if (BlockIds[i] != blockId)
                        continue;
                    if (i > 0)
                        list.Add(BlockIds[i - 1]);
                    if (i < BlockIds.Length - 1)
                        list.Add(BlockIds[i + 1]);
                }
                return list;
            }

            public int Owed(TerritoryBlockId blockId, TerritoryGangId gang)
            {
                var owed = 0;
                var here = DoorsOn(blockId);
                for (var i = 0; i < here.Count; i++)
                    if (Racket.StateOf(here[i], gang) == TerritoryProtectionState.Compliant)
                        owed += Dues.OwedOf(here[i], gang);
                return owed;
            }

            public int Stops(TerritoryBlockId blockId, TerritoryGangId gang)
            {
                var stops = 0;
                var here = DoorsOn(blockId);
                for (var i = 0; i < here.Count; i++)
                    if (Dues.OwedOf(here[i], gang) > 0)
                        stops++;
                return stops;
            }

            // --------------------------------------------------------------- the view

            public HouseView Look()
            {
                seen.Clear();
                seen.Add(BlockIds[0]);
                var paper = House.Roster.Organization.BlockResponsibilities;
                for (var i = 0; i < paper.Count; i++)
                    if (paper[i].BlockId.IsValid && !seen.Contains(paper[i].BlockId))
                        seen.Add(paper[i].BlockId);
                for (var i = 0; i < BlockIds.Length; i++)
                    if (Presence(BlockIds[i], Mine) > 0f && !seen.Contains(BlockIds[i]))
                        seen.Add(BlockIds[i]);

                defiances.Clear();
                for (var b = 0; b < seen.Count; b++)
                {
                    var here = DoorsOn(seen[b]);
                    for (var i = 0; i < here.Count; i++)
                        if (Racket.TryGetRelationship(here[i], Mine, out var row) &&
                            row.RefusedAt >= 0.0 &&
                            row.State != TerritoryProtectionState.Compliant)
                            defiances.Add(new HouseDefiance(
                                here[i], seen[b], row.RefusedAt, row.Threats));
                }

                return new HouseView
                {
                    House = Mine,
                    Roster = House.Roster,
                    Accounts = House.Runner.Accounts,
                    Book = House.Runner.Book,
                    Front = House.Front,
                    FrontBlock = BlockIds[0],
                    Blocks = seen,
                    NeighbourLook = Neighbours,
                    DoorLook = Doorsteps,
                    PresenceLook = blockId => Presence(blockId, Mine),
                    FearLook = Fear,
                    AttentionLook = blockId => 0f,
                    ControlLook = blockId =>
                        Presence(blockId, Mine) >= 40f
                            ? TerritoryControlState.Controlled
                            : TerritoryControlState.Uncontrolled,
                    LeaderLook = blockId =>
                        Presence(blockId, Mine) >= 40f ? Mine : default,
                    Defiances = defiances,
                    LastRefusals = refused,
                    GameHour = Hour,
                    Day = Day + 1,
                };
            }

            IReadOnlyList<HouseDoor> Doorsteps(TerritoryBlockId blockId)
            {
                var built = new List<HouseDoor>();
                var here = DoorsOn(blockId);
                for (var i = 0; i < here.Count; i++)
                {
                    Racket.TryGetProtector(here[i], out var protector);
                    built.Add(new HouseDoor(
                        here[i], 1, EconomyPrices.ProtectionPerWeek(
                            LivingCity.Business.BusinessArchetypeId.Grocer),
                        protector, Racket.StateOf(here[i], Mine),
                        Dues.OwedOf(here[i], Mine), false, true,
                        here[i] == House.Front
                            ? DoorTenure.Ours
                            : protector == Mine
                                ? DoorTenure.Paying
                                : protector.IsValid
                                    ? DoorTenure.Rival
                                    : DoorTenure.Open));
                }
                return built;
            }

            // --------------------------------------------------------------- the doing

            public string Carry(HouseIntent intent)
            {
                switch (intent.Kind)
                {
                    case HouseIntentKind.Command:
                        return Order(intent);

                    case HouseIntentKind.Job:
                        if (intent.Job == null)
                            return "no order";
                        intent.Job.GangId = House.GangId;
                        var issued = House.Runner.Issue(House.Roster, intent.Job);
                        if (issued.Ok && intent.Job.Type == OrderType.Recruit)
                            Signed = true;
                        return issued.Ok ? "" : issued.Reason;

                    case HouseIntentKind.SetDuty:
                        return HouseOps.SetDuty(
                            House, intent.CharacterId, intent.Duty).Reason;

                    case HouseIntentKind.AssignToCrew:
                        return HouseOps.AssignToCrew(
                            House, intent.CharacterId, intent.CrewId).Reason;

                    case HouseIntentKind.Promote:
                        return HouseOps.Promote(House, intent.CharacterId, out _).Reason;

                    case HouseIntentKind.Demote:
                        return HouseOps.Demote(House, intent.CharacterId).Reason;

                    case HouseIntentKind.SetPolicy:
                        return HouseOps.SetPolicy(
                            House, intent.CrewId, intent.Policy).Reason;

                    case HouseIntentKind.AssignBlock:
                        return HouseOps.AssignBlock(
                            House, intent.BlockId, intent.CharacterId, true).Reason;
                }
                return "nothing to do";
            }

            string Order(HouseIntent intent)
            {
                switch (intent.Order)
                {
                    case HouseOrder.OperateInBlock:
                        posted[intent.CrewId] = intent.BlockId;
                        return "";

                    case HouseOrder.ApproachBusiness:
                        Changes.Clear();
                        Racket.Approach(intent.BusinessId, Mine, Hour, Changes, false);
                        if (intent.FollowUp == TerritoryRacketIntent.Demand)
                        {
                            Demanded = true;
                            Racket.Demand(
                                intent.BusinessId, Mine, Asking(intent.BusinessId), Hour,
                                out _, Changes);
                        }
                        else if (intent.FollowUp == TerritoryRacketIntent.Threaten)
                        {
                            Racket.Threaten(intent.BusinessId, Mine, Hour, Changes);
                        }
                        return "";

                    case HouseOrder.LeanOnHoldouts:
                        Leant = true;
                        var here = DoorsOn(intent.BlockId);
                        for (var i = 0; i < here.Count; i++)
                            if (Racket.StateOf(here[i], Mine) ==
                                TerritoryProtectionState.Defiant)
                                Racket.Threaten(here[i], Mine, Hour, Changes);
                        return "";
                }
                return "no such order";
            }

            /// <summary>How frightened of this family the street is. Standing and fear
            /// are the same thing here: men who have been on a corner for a week are
            /// known on it. The real ledger measures acts; a paper city has none.
            /// </summary>
            public float Fear(TerritoryBlockId blockId) => Presence(blockId, Mine);

            TerritoryComplianceInputs Asking(TerritoryBusinessId businessId) =>
                new TerritoryComplianceInputs(
                    Fear(owner[businessId]), Presence(owner[businessId], Mine), 0f, 0f,
                    0f, false);

            // --------------------------------------------------------------- the clock

            /// <summary>One game hour: the men on the street build presence, the mind
            /// takes its turn when its four hours are up, rounds go out on their day and
            /// walk themselves home, and midnight pays the wages.</summary>
            public void Tick(HouseMindConfig config)
            {
                Hour += 1.0;

                // THE FAMILY LIVES ON ITS OWN STREET. Its front is there and its men are
                // in and out of it all day, so it has standing on that block without
                // anybody being posted to it - which is why it can ask a door there.
                Stand(BlockIds[0], Mine, PresencePerHour);

                foreach (var pair in posted)
                {
                    Stand(pair.Value, Mine, PresencePerHour);
                    if (pair.Value != BlockIds[0] &&
                        Presence(pair.Value, Mine) >= config.DemandPresence)
                        Entered = true;
                }

                House.Runner.AdvanceHours(House.Roster, 1f);
                Think(config);

                Scheduler.Tend(
                    House, Day, Day % 7, (int)(Hour - Day * 24.0), Rounds, Send);
                Clock.Tick(Hour, Ask, null);

                if (Day == LastDay)
                    return;
                LastDay = Day;
                Midnight();
            }

            void Think(HouseMindConfig config)
            {
                if (Hour < House.NextThinkHour)
                    return;
                if (House.NextThinkHour <= 0.0)
                    House.OpenTheRota(Hour, config.ThinkEveryHours, 21);
                House.NextThinkHour = Hour + config.ThinkEveryHours;

                HouseMind.Think(Look(), config, intents);
                refused.Clear();
                var done = 0;
                for (var i = 0; i < intents.Count && done < config.MaxIntentsPerThink; i++)
                {
                    var refusal = Carry(intents[i]);
                    done++;
                    if (!string.IsNullOrEmpty(refusal))
                        refused.Add(intents[i] + ": " + refusal);
                }
            }

            bool Send(House house, Crew crew, TerritoryBlockId blockId)
            {
                var stops = new List<TerritoryRoundStop>();
                var here = DoorsOn(blockId);
                for (var i = 0; i < here.Count; i++)
                {
                    if (Racket.StateOf(here[i], Mine) !=
                        TerritoryProtectionState.Compliant ||
                        Dues.OwedOf(here[i], Mine) <= 0)
                        continue;
                    stops.Add(new TerritoryRoundStop(here[i], doorsteps[here[i]]));
                }
                if (stops.Count == 0)
                    return false;

                RosterOps.CollectorsOf(house.Roster, crew.Id, Bag);
                var round = Rounds.Open(
                    Mine, crew.Id, Bag.Count > 0 ? Bag[0].Id : -1, blockId,
                    TerritoryRoundKind.Collect, stops, Hour);
                if (round == null)
                    return false;
                RoundsOut++;
                StopsWalked += stops.Count;

                Clock.Send(round, doorsteps[House.Front], doorsteps[House.Front], true,
                    false, 0, 1f, Hour);
                return true;
            }

            readonly List<Character> Bag = new List<Character>();

            TerritoryStopInputs Ask(TerritoryRound round, TerritoryRoundStop stop) =>
                new TerritoryStopInputs(
                    true, Dues.OwedOf(stop.BusinessId, round.House),
                    TerritoryOwnerProfile.Deal(Seed, stop.BusinessId),
                    Fear(round.BlockId), 0f,
                    (int)CrewPolicy.Normal, (int)LieutenantArchetype.Soldier, Seed, Day);

            void Midnight()
            {
                var paid = House.Runner.DayTick(House.Roster, payTribute: false);
                if (paid > 0 && Banked > 0)
                    Paid = true;

                // A door that pays accrues a day of the meter, for every block.
                for (var b = 0; b < BlockIds.Length; b++)
                {
                    var here = DoorsOn(BlockIds[b]);
                    for (var i = 0; i < here.Count; i++)
                        if (Racket.TryGetProtector(here[i], out var protector))
                            Dues.AccrueDay(here[i], protector,
                                EconomyPrices.ProtectionPerWeek(
                                    LivingCity.Business.BusinessArchetypeId.Grocer));
                }

                Deployed = UpToStrength();
            }

            /// <summary>Hoods on their feet in one crew.</summary>
            /// <summary>How many doors in the whole rig pay this family right now.
            /// </summary>
            public int Paying()
            {
                var paying = 0;
                for (var b = 0; b < BlockIds.Length; b++)
                {
                    var here = DoorsOn(BlockIds[b]);
                    for (var i = 0; i < here.Count; i++)
                        if (Racket.StateOf(here[i], Mine) ==
                            TerritoryProtectionState.Compliant)
                            paying++;
                }
                return paying;
            }

            public int Hoods(Crew crew)
            {
                var count = 0;
                for (var i = 0; i < crew.HoodIds.Count; i++)
                {
                    var man = House.Roster.Find(crew.HoodIds[i]);
                    if (man != null && !man.Gone && man.Status == CharacterStatus.Active)
                        count++;
                }
                return count;
            }

            /// <summary>EVERY crew back to strength - not just the luckiest one.
            /// </summary>
            public bool UpToStrength()
            {
                var crews = House.Roster.Crews;
                for (var c = 0; c < crews.Count; c++)
                    if (Hoods(crews[c]) < HouseMindConfig.Default.MinHoods)
                        return false;
                return true;
            }
        }

        static TerritoryComplianceInputs Strong() =>
            new TerritoryComplianceInputs(70f, 60f, 10f, 0f, 0f, false);

        /// <summary>
        /// The loss the MVP starts from: one crew is shot down below strength. A family
        /// that lost a man it did not need would have nothing to do about it, and the
        /// step under test is what it DOES about a hole in a crew.
        /// </summary>
        static void LoseAHood(RigCity city)
        {
            var roster = city.House.Roster;
            if (roster.Crews.Count == 0)
                return;
            var crew = roster.Crews[0];
            while (city.Hoods(crew) >= HouseMindConfig.Default.MinHoods)
            {
                var shot = false;
                for (var i = 0; i < crew.HoodIds.Count && !shot; i++)
                {
                    var man = roster.Find(crew.HoodIds[i]);
                    if (man == null || man.Gone ||
                        man.Status != CharacterStatus.Active)
                        continue;
                    HouseOps.Kill(city.House, man.Id);
                    shot = true;
                }
                if (!shot)
                    return;
            }
        }

        // ------------------------------------------------------------------ contracts

        /// <summary>(a) A crew under strength signs somebody, and he ends up in it.
        /// </summary>
        static void AHoodLostIsAHoodReplaced(List<string> failures)
        {
            var city = new RigCity(7, 3);
            var config = HouseMindConfig.Default;
            LoseAHood(city);
            if (city.UpToStrength())
            {
                failures.Add("HOUSE-001: the fixture did not leave a crew short.");
                return;
            }

            for (var hour = 0; hour < 24 * 4 && !city.Signed; hour++)
                city.Tick(config);

            if (!city.Signed)
                failures.Add("HOUSE-001: a crew was left short and nobody was signed.");

            for (var hour = 0; hour < 24 * 6 && !city.UpToStrength(); hour++)
                city.Tick(config);

            if (!city.UpToStrength())
                failures.Add("HOUSE-001: the signed man never reached the crew.");
        }

        /// <summary>(b) The family walks onto the empty neighbour, and only asks a door
        /// there once it is actually standing on the street.</summary>
        static void TheFamilyWalksOntoTheNextStreet(List<string> failures)
        {
            var city = new RigCity(11, 4);
            var config = HouseMindConfig.Default;

            var askedEarly = false;
            for (var hour = 0; hour < 24 * 10 && !city.Entered; hour++)
            {
                city.Tick(config);
                if (city.Demanded &&
                    city.Presence(city.BlockIds[1], city.Mine) < config.DemandPresence &&
                    city.Presence(city.BlockIds[0], city.Mine) < config.DemandPresence)
                    askedEarly = true;
            }

            if (!city.Entered)
                failures.Add("HOUSE-002: the family never took the next street.");
            if (askedEarly)
                failures.Add("HOUSE-002: a door was asked before anybody stood there.");
        }

        /// <summary>(c) A door that says no gets one threat and one lean, and is then let
        /// be - never a second demand in a row.</summary>
        static void ARefusalGetsOneThreatAndOneLean(List<string> failures)
        {
            var city = new RigCity(3, 5);
            var config = HouseMindConfig.Default;
            var door = city.DoorsOn(city.BlockIds[0])[2];

            // He has been asked and has refused; the clock on it is a day old.
            city.Hour = 48.0;
            city.Racket.Demand(door, city.Mine, Hopeless(), 24.0, out _, city.Changes);
            if (city.Racket.StateOf(door, city.Mine) != TerritoryProtectionState.Defiant)
            {
                failures.Add("HOUSE-003: the fixture did not leave the door defiant.");
                return;
            }

            var demands = 0;
            city.Racket.TryGetRelationship(door, city.Mine, out var start);
            demands = start.Demands;

            city.Stand(city.BlockIds[0], city.Mine, 60f);
            for (var hour = 0; hour < 24 * 6; hour++)
                city.Tick(config);

            city.Racket.TryGetRelationship(door, city.Mine, out var after);
            if (after.Demands != demands)
                failures.Add("HOUSE-003: a defiant door was asked again (" +
                             after.Demands + " demands, was " + demands + ").");
            if (after.Threats < 1)
                failures.Add("HOUSE-003: a refusal was never answered at all.");
            if (after.Threats > 3)
                failures.Add("HOUSE-003: the family kept knocking - " + after.Threats +
                             " threats on one door.");
        }

        static TerritoryComplianceInputs Hopeless() =>
            new TerritoryComplianceInputs(0f, 0f, 0f, 40f, 0f, false);

        /// <summary>(f) A door another house protects is never proposed at all. The
        /// gateway would refuse it; the mind must not even ask.</summary>
        static void TheMindNeverAsksADoorAnotherHouseHolds(List<string> failures)
        {
            var city = new RigCity(5, 6);
            var config = HouseMindConfig.Default;
            var theirs = city.DoorsOn(city.BlockIds[1])[0];
            city.Racket.Demand(theirs, new TerritoryGangId(9), Strong(), 1.0, out _);
            city.Stand(city.BlockIds[1], city.Mine, 60f);

            var intents = new List<HouseIntent>();
            for (var think = 0; think < 20; think++)
            {
                city.Hour += 4.0;
                HouseMind.Think(city.Look(), config, intents);
                for (var i = 0; i < intents.Count; i++)
                    if (intents[i].Kind == HouseIntentKind.Command &&
                        intents[i].BusinessId == theirs)
                    {
                        failures.Add("HOUSE-004: the mind asked at a door the Ninth " +
                                     "family already protects.");
                        return;
                    }
            }
        }

        /// <summary>(g) The mind reads the view and nothing else. A compile-time rule
        /// cannot be written, so the source is scanned: no ledger, no runtime, no roll.
        /// </summary>
        static void TheMindReadsNothingButItsView(List<string> failures)
        {
            var mind = System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(),
                "Assets/Scripts/Outfit/HouseMind.cs");
            if (!System.IO.File.Exists(mind))
            {
                failures.Add("HOUSE-005: HouseMind.cs is not where the rule looks.");
                return;
            }

            var lines = System.IO.File.ReadAllLines(mind);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//") || line.Contains("///"))
                    continue;
                if (line.Contains("TerritoryRuntime") || line.Contains("Ledger") ||
                    line.Contains("Roll"))
                    failures.Add("HOUSE-005: HouseMind.cs:" + (i + 1) +
                                 " reaches past the view.");
            }
        }

        /// <summary>
        /// (h) THE MVP. Every seed from 1 to 30 runs the whole loop inside fourteen game
        /// days: a hood lost, a hood signed, a hood deployed, the next street entered, a
        /// door asked, the take collected and carried home, and the men paid out of it.
        /// </summary>
        static void TheMvpRunsForEverySeed(List<string> failures)
        {
            var config = HouseMindConfig.Default;
            var slowest = 0;
            var failed = 0;
            for (var seed = 1; seed <= Seeds; seed++)
            {
                var city = new RigCity(seed, 1 + seed % 20);
                LoseAHood(city);

                var days = 0;
                for (var hour = 0; hour < 24 * MvpDays; hour++)
                {
                    city.Tick(config);
                    if (Done(city))
                    {
                        days = city.Day + 1;
                        break;
                    }
                }

                if (!Done(city))
                {
                    failed++;
                    Notes.Add("seed " + seed + ": DID NOT FINISH · " + Missing(city));
                    if (failed <= 3)
                        failures.Add("HOUSE-006: seed " + seed + " did not finish the " +
                                     "MVP in " + MvpDays + " days · " + Missing(city));
                    continue;
                }
                Notes.Add("seed " + seed + ": " + days + " days, $" + city.Banked +
                          " banked");
                if (days > slowest)
                    slowest = days;
            }

            Notes.Add("slowest: " + slowest + " of " + MvpDays + " days · " +
                      (Seeds - failed) + "/" + Seeds + " finished");
            if (failed > 3)
                failures.Add("HOUSE-006: " + failed + " of " + Seeds +
                             " seeds did not finish the MVP.");
        }

        static bool Done(RigCity city) =>
            city.Signed && city.Deployed && city.Entered && city.Demanded &&
            city.Banked > 0 && city.Paid;

        static string Missing(RigCity city)
        {
            var missing = "";
            if (!city.Signed) missing += "no signing; ";
            if (!city.Deployed) missing += "never back to strength; ";
            if (!city.Entered) missing += "never took the next street; ";
            if (!city.Demanded) missing += "never asked a door; ";
            if (city.Banked <= 0)
                missing += "no bag came home (" + city.RoundsOut + " rounds, " +
                           city.StopsWalked + " doors, " + city.Missed + " missed, " +
                           city.Paying() + " paying); ";
            if (!city.Paid) missing += "wages never left the take; ";
            return missing;
        }
    }
}
