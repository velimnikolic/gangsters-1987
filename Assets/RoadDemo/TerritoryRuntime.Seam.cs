using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Territory;
using LivingCity.UI;

namespace RoadDemo
{
    /// <summary>
    /// THE LINE BETWEEN THE RACKET AND THE PAGE.
    ///
    /// The block file reads every racket figure through <see cref="BlockRacketSeam"/> and
    /// acts through it too; this is the real thing behind that seam, installed when the
    /// territory layer stands up and taken away when it goes. A bench scene with no city
    /// falls back to the stub, and the page says so on its own face.
    ///
    /// Nothing here decides anything. Every rule it answers with is the pure layer's -
    /// <see cref="TerritoryDoorStandings"/> for where a door stands,
    /// <see cref="TerritoryCollectionSchedule"/> for the day it is walked,
    /// <see cref="TerritoryShakedown"/> for which doors an order reaches - and every act
    /// goes through the command gateway, which is the one mutation boundary (SIM-003).
    /// </summary>
    public sealed partial class TerritoryRuntime
    {
        BlockRacketBinding racketSeam;

        /// <summary>What the last round on each block came to - the block file prints it
        /// as LAST ROUND, and nothing else keeps it per block.</summary>
        readonly Dictionary<TerritoryBlockId, (int Day, int Banked, int Short)> lastRounds =
            new Dictionary<TerritoryBlockId, (int, int, int)>();

        /// <summary>What each block has banked THIS WEEK. Reset when the campaign's
        /// weekday wraps back to Monday - a week's figure that never resets is a total.
        /// </summary>
        readonly Dictionary<TerritoryBlockId, int> bankedThisWeek =
            new Dictionary<TerritoryBlockId, int>();

        /// <summary>Which campaign week the tally above belongs to. The WEEK, not the
        /// weekday: two rounds banking on consecutive Wednesdays are seven days apart
        /// and a weekday comparison sees no wrap between them, so the second week's
        /// money used to be added to the first's.</summary>
        int bankedWeek = -1;

        /// <summary>Moves whenever anything the block file reads would read differently:
        /// a round starting, a stop settling, a duty changing, a policy changing. The
        /// racket's own version is mixed in, so a door answering counts too.</summary>
        int racketSeamVersion;

        void BumpRacketSeam() => racketSeamVersion++;

        void InstallRacketSeam()
        {
            racketSeam ??= new BlockRacketBinding(this);
            BlockRacketSeam.Source = racketSeam;
            BlockRacketSeam.Actions = racketSeam;
        }

        void RemoveRacketSeam()
        {
            if (ReferenceEquals(BlockRacketSeam.Source, racketSeam))
                BlockRacketSeam.Source = null;
            if (ReferenceEquals(BlockRacketSeam.Actions, racketSeam))
                BlockRacketSeam.Actions = null;
        }

        /// <summary>Books this round against its block, for the two figures the file
        /// prints about money that has actually arrived.</summary>
        void NoteRoundBanked(TerritoryBlockId blockId, int banked, int shortCount, int day)
        {
            if (!blockId.IsValid)
                return;
            lastRounds[blockId] = (day, banked, shortCount);

            // A new week wipes the slate. Checked on the WRITE rather than on a tick,
            // because the figure is only ever read beside a fresh one.
            var week = WeekOf(day);
            if (week != bankedWeek)
            {
                bankedThisWeek.Clear();
                bankedWeek = week;
            }

            bankedThisWeek.TryGetValue(blockId, out var already);
            bankedThisWeek[blockId] = already + banked;
            BumpRacketSeam();
        }

        /// <summary>Which week of the campaign a day falls in. Day 1 is week 0, the
        /// same reckoning Campaign.DayOfWeek uses.</summary>
        static int WeekOf(int day) => (day > 1 ? day - 1 : 0) / 7;

        /// <summary>
        /// The one implementation of both halves of the seam. A class rather than the
        /// runtime itself so the statics can be cleared by identity - a second runtime in
        /// a torn-down scene must not unhook the live one's.
        /// </summary>
        sealed class BlockRacketBinding : IBlockRacketSource, IBlockRacketActions
        {
            readonly TerritoryRuntime runtime;
            readonly List<TerritoryProtectionRelationship> relationships =
                new List<TerritoryProtectionRelationship>();
            readonly List<Character> collectors = new List<Character>();

            /// <summary>
            /// One block's standings, read once and held until something moves.
            ///
            /// Reading them is not cheap: every door walks the racket's dispatch book
            /// (a thousand slips) looking for its own last one. The block file asks for
            /// the whole column AND for the two counts in its head, on every repaint,
            /// and a repaint happens on every observation tick - so without this a
            /// thirty-door block scanned sixty thousand slips several times a second.
            /// </summary>
            readonly List<DoorStanding> standings = new List<DoorStanding>();
            TerritoryBlockId standingsBlock;
            int standingsVersion = -1;

            /// <summary>The newest slip about each door, gathered in ONE pass over the
            /// book instead of one pass per door.</summary>
            readonly Dictionary<TerritoryBusinessId, TerritoryDoorDispatch> lastSlip =
                new Dictionary<TerritoryBusinessId, TerritoryDoorDispatch>();

            public BlockRacketBinding(TerritoryRuntime runtime) => this.runtime = runtime;

            /// <summary>Reads the block's doors if anything has moved since the last
            /// time, and answers the held list either way.</summary>
            List<DoorStanding> Standings(TerritoryBlockId blockId)
            {
                if (standingsBlock == blockId && standingsVersion == Version)
                    return standings;

                standings.Clear();
                standingsBlock = blockId;
                standingsVersion = Version;
                if (!blockId.IsValid || runtime.geography == null ||
                    runtime.racket == null)
                    return standings;

                var gang = new TerritoryGangId(LivingCity.Gangs.GangCatalog.PlayerGangId);

                // One pass over the book, newest first: the FIRST slip seen for a door
                // is that door's newest, so a door already in the map is left alone.
                lastSlip.Clear();
                var slips = runtime.racket.Dispatches;
                for (var i = slips.Count - 1; i >= 0; i--)
                {
                    var slip = slips[i];
                    if (slip.GangId != gang || !slip.BusinessId.IsValid ||
                        lastSlip.ContainsKey(slip.BusinessId))
                        continue;
                    lastSlip[slip.BusinessId] = slip;
                }

                var day = Today();
                var word = TerritoryCollectionSchedule.WordOf(blockId);
                var here = runtime.geography.BusinessesOf(blockId);
                for (var i = 0; i < here.Count; i++)
                    standings.Add(StandingOf(here[i].BusinessId, gang, day, word));
                return standings;
            }

            public int Version =>
                (runtime.racket != null ? runtime.racket.Version : 0) * 397 +
                runtime.racketSeamVersion;

            // ------------------------------------------------------------- the figures

            public bool TryGetBlock(TerritoryBlockId blockId, out BlockRacketView view)
            {
                view = default;
                if (!blockId.IsValid || runtime.geography == null ||
                    runtime.racket == null)
                    return false;

                var roster = Roster();
                var leaderId = -1;
                var crewId = -1;
                var name = "";
                var policy = CrewPolicy.Normal;
                var bagManId = -1;
                var bagManName = "";
                var bagNamedByBoss = false;

                if (roster != null)
                {
                    var paper = roster.Organization.BlockResponsibilities;
                    for (var i = 0; i < paper.Count; i++)
                        if (paper[i].BlockId == blockId)
                        {
                            leaderId = paper[i].LeaderId;
                            break;
                        }
                    if (leaderId >= 0)
                    {
                        var leader = roster.Find(leaderId);
                        name = leader != null ? leader.FullName : "";
                        for (var c = 0; c < roster.Crews.Count; c++)
                            if (roster.Crews[c].LieutenantId == leaderId)
                            {
                                var crew = roster.Crews[c];
                                crewId = crew.Id;
                                policy = crew.Policy;
                                // THE BAG MAN (GAN-262): the one man of the crew who
                                // carries it, and whose word put it in his hand.
                                bagManId = RosterOps.CollectorOf(roster, crew.Id);
                                var bagMan = bagManId >= 0 ? roster.Find(bagManId) : null;
                                bagManName = bagMan != null ? bagMan.FullName : "";
                                bagNamedByBoss = crew.BagNamedByBoss;
                                break;
                            }
                    }
                }

                // A block is only WALKED when somebody on that crew carries the bag.
                // Paper alone collects nothing, and the file says so in red.
                collectors.Clear();
                if (roster != null && crewId >= 0)
                    RosterOps.CollectorsOf(roster, crewId, collectors);
                var walked = collectors.Count > 0;
                var weekday = walked ? TerritoryCollectionSchedule.DayOf(blockId) : -1;
                var word = walked ? TerritoryCollectionSchedule.WordOf(blockId) : "";

                // The round, if one of this crew's is out on THIS block.
                var roundOut = false;
                var cursor = 0;
                var stops = 0;
                var carried = 0;
                var collectorName = "";
                for (var i = 0; i < runtime.rounds.Count; i++)
                {
                    var round = runtime.rounds[i];
                    if (round.Kind != RoundKind.Collect || round.BlockId != blockId)
                        continue;
                    roundOut = true;
                    cursor = round.Cursor;
                    stops = round.Stops.Count;
                    carried = round.Carried;
                    collectorName = NameOfWalker(roster, round.Collector);
                    break;
                }

                runtime.TryGetCollectibleDues(blockId, out var owed);

                var needing = 0;
                var holdouts = 0;
                CountDoors(blockId, out needing, out holdouts);

                runtime.lastRounds.TryGetValue(blockId, out var last);
                runtime.bankedThisWeek.TryGetValue(blockId, out var banked);

                view = new BlockRacketView(
                    leaderId >= 0, name, crewId, policy, weekday, word, collectors.Count,
                    roundOut, cursor, stops, carried, collectorName,
                    owed, roundOut ? carried : 0, banked,
                    last.Day, last.Banked, last.Short, needing, holdouts,
                    bagManId, bagManName, bagNamedByBoss);
                return true;
            }

            /// <summary>Every hood of the crew for the bag menu (GAN-262): his name,
            /// what kind of bag man he would make, and whether he is one of the men in
            /// the street line today - the line as it actually stands, where the
            /// scene has one, else the books' own four.</summary>
            public void CollectCrewHoods(int crewId, List<CrewHandView> into)
            {
                into?.Clear();
                var roster = Roster();
                var crew = roster?.FindCrew(crewId);
                if (into == null || crew == null)
                    return;

                var line = runtime.crews != null ? runtime.crews.UnitOfCrew(crewId) : null;
                // With no street under the page (a bench scene), the line is read off
                // the books the way DemoCrews.Sync deals it: the bag man spends one of
                // the crew's four places even though he does not stand in the line.
                var bagId = RosterOps.CollectorOf(roster, crewId);
                var bagMan = bagId >= 0 ? roster.Find(bagId) : null;
                var dealt = bagMan != null && bagMan.Status == CharacterStatus.Active ? 1 : 0;
                for (var i = 0; i < crew.HoodIds.Count; i++)
                {
                    var man = roster.Find(crew.HoodIds[i]);
                    if (man == null || man.Gone)
                        continue;
                    var carries = man.Duty == Duty.Collector;
                    bool walks;
                    if (line != null)
                    {
                        walks = false;
                        for (var h = 0; h < line.Hoods.Count && !walks; h++)
                            walks = line.Hoods[h] != null && !line.Hoods[h].Dead &&
                                    line.Hoods[h].CharacterId == man.Id;
                    }
                    else
                    {
                        walks = !carries && man.Status == CharacterStatus.Active &&
                                dealt < Crew.MaxTacticalHoods;
                        if (walks)
                            dealt++;
                    }
                    into.Add(new CrewHandView(man.Id, man.FullName,
                        CollectorChoice.Fitness(man), walks, carries));
                }
            }

            public void CollectDoorStandings(
                TerritoryBlockId blockId, List<DoorStanding> into)
            {
                into?.Clear();
                if (into == null)
                    return;
                into.AddRange(Standings(blockId));
            }

            DoorStanding StandingOf(
                TerritoryBusinessId businessId, TerritoryGangId gang, int day, string word)
            {
                var state = runtime.racket.StateOf(businessId, gang);
                var shut = !runtime.RacketCanAccrueAt(businessId, runtime.lastGameHour);

                // Who else is being paid here. A door another house holds is not a door
                // that owes US anything, whatever our own ledger remembers.
                var rival = "";
                runtime.racket.CollectRelationships(businessId, relationships);
                for (var i = 0; i < relationships.Count; i++)
                    if (relationships[i].GangId != gang &&
                        relationships[i].State == TerritoryProtectionState.Compliant)
                    {
                        rival = LivingCity.Gangs.GangRegistry.NameOf(
                            relationships[i].GangId.Value);
                        break;
                    }

                TerritoryDoorDispatch? news = lastSlip.TryGetValue(businessId, out var slip)
                    ? slip
                    : (TerritoryDoorDispatch?)null;

                var hasDues = runtime.dues != null &&
                              runtime.dues.TryGet(businessId, out var account) &&
                              account.GangId == gang;
                var owed = hasDues ? runtime.dues.OwedOf(businessId, gang) : 0;
                var rate = 0;
                var lastPaid = -1;
                var missed = 0;
                if (hasDues && runtime.dues.TryGet(businessId, out var dues))
                {
                    rate = dues.WeeklyRate;
                    lastPaid = dues.LastCollectedDay;
                    missed = dues.MissedInARow;
                }

                TerritoryDoorStandings.Of(
                    state, OursByDeed(businessId), shut, "shut", rival, news, hasDues,
                    owed, rate, lastPaid, missed, day, word,
                    out var kind, out var line, out var outOwed, out var daysLate,
                    out var newsDay);

                return new DoorStanding(
                    businessId, (DoorStandingKind)kind, line, outOwed, daysLate, newsDay,
                    rival);
            }

            /// <summary>The two figures the block card's head prints, counted off the
            /// SAME list the column under it renders - they cannot disagree.</summary>
            void CountDoors(TerritoryBlockId blockId, out int needing, out int holdouts)
            {
                needing = 0;
                holdouts = 0;
                var rows = Standings(blockId);
                for (var i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Severity > 0)
                        needing++;
                    // A holdout is a door that refused us or will not say yes. It is the
                    // standing's own kind, not a second reading of the ledger.
                    if (rows[i].Kind == DoorStandingKind.Refused ||
                        rows[i].Kind == DoorStandingKind.Wavering)
                        holdouts++;
                }
            }

            public bool IsCollector(int characterId)
            {
                var man = Roster()?.Find(characterId);
                return man != null && man.Duty == Duty.Collector;
            }

            /// <summary>
            /// The block a man is walking a round on. EVERY man of the crew, not only
            /// the one with the bag: until the collector is detached from his crew the
            /// whole unit walks the route, so saying only the carrier is on the round
            /// would leave the rest reading as standing on a street they left.
            /// </summary>
            public bool TryGetRoundOf(int characterId, out TerritoryBlockId blockId)
            {
                blockId = default;
                for (var i = 0; i < runtime.rounds.Count; i++)
                {
                    var round = runtime.rounds[i];
                    if (round.Kind != RoundKind.Collect)
                        continue;
                    var unit = round.Walkers ?? (runtime.crews != null
                        ? runtime.crews.UnitOfCrew(round.CrewId)
                        : null);
                    if (unit == null)
                    {
                        if (round.Collector != null &&
                            round.Collector.CharacterId == characterId)
                        {
                            blockId = round.BlockId;
                            return blockId.IsValid;
                        }
                        continue;
                    }
                    foreach (var man in unit.All())
                        if (man != null && !man.Dead && man.CharacterId == characterId)
                        {
                            blockId = round.BlockId;
                            return blockId.IsValid;
                        }
                }
                return false;
            }

            /// <summary>
            /// Why a key cannot fire, in the SAME words the command would refuse with.
            /// The key and the order are asked the same question, so a lit key that then
            /// refuses is impossible.
            /// </summary>
            public string Refusal(string key, int crewId, TerritoryBlockId blockId)
            {
                if (crewId < 0)
                    return "nobody is picked to send";
                if (!blockId.IsValid || runtime.geography == null || runtime.racket == null)
                    return "this block is not on the geography";

                var gang = new TerritoryGangId(LivingCity.Gangs.GangCatalog.PlayerGangId);
                var here = runtime.geography.BusinessesOf(blockId);

                switch (key)
                {
                    case "shakedown":
                        for (var i = 0; i < here.Count; i++)
                            if (TerritoryShakedown.WorthAsking(
                                    runtime.racket.StateOf(here[i].BusinessId, gang),
                                    OursByDeed(here[i].BusinessId)))
                                return "";
                        return ShakedownRefusal;

                    case "lean":
                        for (var i = 0; i < here.Count; i++)
                            if (TerritoryShakedown.IsHoldout(
                                    runtime.racket.StateOf(here[i].BusinessId, gang),
                                    OursByDeed(here[i].BusinessId)))
                                return "";
                        return LeanRefusal;

                    case "round":
                        if (runtime.RoundRunning(crewId))
                            return "a round is already out";
                        return runtime.TryGetCollectibleDues(blockId, out var owed) &&
                               owed > 0
                            ? ""
                            : "nothing owed yet";
                }
                return "";
            }

            // --------------------------------------------------------------- the acts

            public TerritoryCommandResult ShakeDown(int crewId, TerritoryBlockId blockId) =>
                runtime.Commands.Submit(new ShakeDownBlockCommand(
                    TerritoryCommandNodeId.Crew(crewId), blockId));

            public TerritoryCommandResult SendRound(int crewId, TerritoryBlockId blockId) =>
                runtime.Commands.Submit(new CollectDuesCommand(
                    TerritoryCommandNodeId.Crew(crewId), blockId));

            public TerritoryCommandResult LeanOnHoldouts(
                int crewId, TerritoryBlockId blockId) =>
                runtime.Commands.Submit(new LeanOnHoldoutsCommand(
                    TerritoryCommandNodeId.Crew(crewId), blockId));

            public string SetPolicy(int crewId, CrewPolicy policy)
            {
                var roster = Roster();
                if (roster == null)
                    return "the books are not open in this scene";
                for (var i = 0; i < roster.Crews.Count; i++)
                    if (roster.Crews[i].Id == crewId)
                    {
                        roster.Crews[i].Policy = policy;
                        runtime.BumpRacketSeam();
                        return "";
                    }
                return "no crew of ours carries that number";
            }

            public string SetCollector(int characterId, bool on)
            {
                // Through the DIRECTOR, never RosterOps: the bag takes a man off the
                // street line (GAN-262), and DemoCrews.Sync re-deals only on the
                // director's Version.
                var director = LivingCity.Gameplay.PersonnelDirector.Instance;
                var roster = Roster();
                if (director == null || roster == null)
                    return "the books are not open in this scene";

                OpResult result;
                if (!on)
                    result = director.TakeOffTheBag(characterId);
                else
                {
                    var crew = roster.CrewOf(characterId);
                    result = crew == null
                        ? OpResult.Fail("he has to be in a crew")
                        : crew.LieutenantId == characterId
                            ? OpResult.Fail("only a hood carries the bag")
                            : director.NameCollector(crew.Id, characterId);
                }
                if (result.Ok)
                    runtime.BumpRacketSeam();
                return result.Ok ? "" : result.Reason;
            }

            public string NameCollector(int crewId, int hoodId)
            {
                var director = LivingCity.Gameplay.PersonnelDirector.Instance;
                if (director == null || Roster() == null)
                    return "the books are not open in this scene";
                var result = director.NameCollector(crewId, hoodId);
                if (result.Ok)
                    runtime.BumpRacketSeam();
                return result.Ok ? "" : result.Reason;
            }

            public string LetLieutenantPick(int crewId)
            {
                var director = LivingCity.Gameplay.PersonnelDirector.Instance;
                if (director == null || Roster() == null)
                    return "the books are not open in this scene";
                var result = director.LetLieutenantPick(crewId, out _);
                if (result.Ok)
                    runtime.BumpRacketSeam();
                return result.Ok ? "" : result.Reason;
            }

            public string TakeOffTheBag(int hoodId)
            {
                var director = LivingCity.Gameplay.PersonnelDirector.Instance;
                if (director == null || Roster() == null)
                    return "the books are not open in this scene";
                var result = director.TakeOffTheBag(hoodId);
                if (result.Ok)
                    runtime.BumpRacketSeam();
                return result.Ok ? "" : result.Reason;
            }

            // -------------------------------------------------------------- fixtures

            static Roster Roster() =>
                LivingCity.Gameplay.PersonnelDirector.Instance != null
                    ? LivingCity.Gameplay.PersonnelDirector.Instance.Roster
                    : null;

            static int Today()
            {
                var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
                return outfit != null ? outfit.Campaign.Day : 1;
            }

            static string NameOfWalker(Roster roster, CrewWalker walker)
            {
                if (walker == null)
                    return "";
                var man = roster?.Find(walker.CharacterId);
                return man != null ? man.FullName : walker.DisplayName;
            }
        }
    }
}
