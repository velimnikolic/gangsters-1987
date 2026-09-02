using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Outfit
{
    /// <summary>
    /// EVERY NUMBER A MIND USES, in one place (the epic's D-table). Never a literal in a
    /// method: the user vetoes a house's behaviour by editing this class and the table,
    /// not by reading code.
    /// </summary>
    public sealed class HouseMindConfig
    {
        public static readonly HouseMindConfig Default = new HouseMindConfig();

        /// <summary>D7. A house thinks every four game hours, and executes at most three
        /// intents per think.</summary>
        public float ThinkEveryHours = 4f;

        public int MaxIntentsPerThink = 3;

        /// <summary>D8. What a hop of travel and a point of police attention are worth
        /// against a block's expected take.</summary>
        public int HopCostDollars = 100;

        public int HeatCostPerPoint = 20;

        /// <summary>D9. A house spends on men or things only if the safe still holds a
        /// week's wages afterwards; below three days with two crews it merges them; a
        /// crew below MinHoods active hoods signs somebody.</summary>
        public int ReserveDays = 7;

        public int MergeBelowDays = 3;

        public int MinHoods = 2;

        /// <summary>D17. The racket's own cadence.</summary>
        public float DemandPresence = 25f;

        public float DemandCooldownHours = 24f;

        public float StableDoorsShare = 0.5f;
    }

    /// <summary>
    /// THE MIND EVERY FAMILY RUNS, the player's included in everything but the fact that
    /// nobody calls it for him.
    ///
    /// It reads a <see cref="HouseView"/> and emits <see cref="HouseIntent"/>s. It walks
    /// the strict priority tiers (D8) and emits for the FIRST tier with a feasible
    /// candidate, plus any due collection - tier 4 never waits behind a war.
    ///
    /// PURE. No ledger, no runtime, no roll, no clock of its own. The view is the wall;
    /// a mind that reached past it would be playing a different game from the player.
    /// </summary>
    public static class HouseMind
    {
        /// <summary>The tiers, by their number in the plan. v1 fills 2, 3, 4 and 7.
        /// </summary>
        public const int TierSurvive = 1;
        public const int TierWages = 2;
        public const int TierReplace = 3;
        public const int TierCollect = 4;
        public const int TierAnswer = 5;
        public const int TierDefend = 6;
        public const int TierExpand = 7;
        public const int TierInvest = 8;
        public const int TierIdle = 9;

        static readonly List<TerritoryRacketOrder> Rows = new List<TerritoryRacketOrder>();

        /// <summary>
        /// What this house does next. Answers the tier it acted on, so the trace can
        /// print "nothing to do" rather than an empty line.
        /// </summary>
        public static int Think(
            HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            if (into == null)
                return 0;
            into.Clear();
            if (view?.Roster == null || view.Accounts == null)
                return 0;
            config = config ?? HouseMindConfig.Default;

            // TIER 4 NEVER WAITS. A round due today goes out whatever else the family is
            // doing - the money is what everything else is paid from.
            Collect(view, config, into);

            // Then the first tier with something to do, in order.
            if (Merge(view, config, into))
                return TierWages;
            if (Replace(view, config, into))
                return TierReplace;
            if (Expand(view, config, into))
                return TierExpand;

            return into.Count > 0 ? TierCollect : 0;
        }

        // ------------------------------------------------------------------- tier 2

        /// <summary>
        /// MONEY FOR WAGES (D9). Below three days of payroll a family with two crews puts
        /// them together: one lieutenant broken back to hood, his men moved across. It is
        /// the same pair of buttons the player has, one per think.
        /// </summary>
        static bool Merge(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            var payroll = view.DailyPayroll;
            if (payroll <= 0 || view.Safe >= config.MergeBelowDays * payroll)
                return false;

            var crews = view.Roster.Crews;
            if (crews.Count < 2)
                return false;

            Crew smallest = null;
            Crew biggest = null;
            for (var i = 0; i < crews.Count; i++)
            {
                var size = Active(view.Roster, crews[i]);
                if (smallest == null || size < Active(view.Roster, smallest))
                    smallest = crews[i];
                if (biggest == null || size > Active(view.Roster, biggest))
                    biggest = crews[i];
            }
            if (smallest == null || biggest == null || smallest.Id == biggest.Id)
                return false;

            // One intent per think: the hoods follow on the next passes, because the
            // lieutenant has to be a hood before he can be put in anybody's crew.
            var reason = "the safe is under " + config.MergeBelowDays +
                         " days of wages; the crews go together";
            for (var i = 0; i < smallest.HoodIds.Count; i++)
            {
                var man = view.Roster.Find(smallest.HoodIds[i]);
                if (man == null || man.Gone)
                    continue;
                into.Add(HouseIntent.MoveToCrew(man.Id, biggest.Id, TierWages, reason));
                return true;
            }

            into.Add(HouseIntent.Break(smallest.LieutenantId, TierWages, reason));
            return true;
        }

        // ------------------------------------------------------------------- tier 3

        /// <summary>
        /// REPLACE THE FALLEN (D9). A crew under strength signs a hood - if the safe
        /// still holds a week's wages once the new man is on the payroll too.
        /// </summary>
        static bool Replace(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            var roster = view.Roster;
            var crews = roster.Crews;
            for (var i = 0; i < crews.Count; i++)
            {
                var crew = crews[i];
                if (Hoods(roster, crew) >= config.MinHoods)
                    continue;

                // The reserve rule, counting the man we are about to sign.
                var after = view.Safe - EconomyPrices.RecruitSigning;
                var payroll = view.DailyPayroll + Outfit.Wages.HoodBase;
                if (after < config.ReserveDays * payroll)
                    continue;

                // Somebody is already out signing for this crew; a second order would
                // buy the same man twice.
                if (Filed(view, OrderType.Recruit, crew.Id))
                    continue;

                var recruiter = roster.Find(crew.LieutenantId);
                if (recruiter == null || recruiter.Gone)
                    continue;

                var job = new Job
                {
                    CrewId = crew.Id,
                    Type = OrderType.Recruit,
                    Men = 1,
                    TargetLabel = "a man for the crew",
                };
                into.Add(HouseIntent.Work(job, TierReplace,
                    "the crew is under " + config.MinHoods + " men"));
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------- tier 4

        /// <summary>
        /// COLLECT. The scheduler sends the rounds; the mind only makes sure the paper
        /// is there for it to read - a man on the bag in every crew that protects doors,
        /// and its lieutenant answering for the blocks those doors are on.
        /// </summary>
        static void Collect(
            HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            var roster = view.Roster;
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var blockId = view.Blocks[b];
                if (!Protects(view, blockId))
                    continue;

                var crew = CrewFor(view, blockId);
                if (crew == null)
                    continue;

                if (!HasCollector(roster, crew))
                {
                    for (var i = 0; i < crew.HoodIds.Count; i++)
                    {
                        var man = roster.Find(crew.HoodIds[i]);
                        if (man == null || man.Gone || man.Rank != Rank.Hood)
                            continue;
                        into.Add(HouseIntent.MarkDuty(man.Id, Duty.Collector, TierCollect,
                            "somebody has to carry the bag"));
                        break;
                    }
                }

                if (!Answers(roster, crew.LieutenantId, blockId) &&
                    Room(roster, crew.LieutenantId))
                    into.Add(HouseIntent.GiveBlock(crew.LieutenantId, blockId,
                        TierCollect, "he answers for the doors that pay us there"));
            }
        }

        // ------------------------------------------------------------------- tier 7

        /// <summary>
        /// EXPAND. The family walks onto the best neighbouring block it could hold, then
        /// - once it is actually standing there - asks the best unprotected door on it.
        /// A no is followed once by a threat and once by a lean, and then left alone;
        /// RIVAL-006 escalates further.
        ///
        /// A house never opens new ground while the ground it already leads is loose
        /// (D17): every block it leads must be Controlled or better with at least half
        /// its doors paying.
        /// </summary>
        static bool Expand(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            // Walking onto a street and asking at a door cost nothing, so the reserve
            // rule (D9) does not gate them - it gates the SIGNING, which is tier 3, and
            // a family too poor to expand is put back together by tier 2 first.

            // First: is there a door to ask on ground we already stand on?
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var blockId = view.Blocks[b];
                if (view.OurPresence(blockId) < config.DemandPresence)
                    continue;
                var crew = CrewOn(view, blockId);
                if (crew == null)
                    continue;
                if (Ask(view, config, blockId, crew, into))
                    return true;
            }

            if (!Settled(view, config))
                return false;

            // Then: which neighbour is worth walking onto.
            var best = default(TerritoryBlockId);
            var bestScore = 0;
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var neighbours = view.Neighbours(view.Blocks[b]);
                for (var n = 0; n < neighbours.Count; n++)
                {
                    var blockId = neighbours[n];
                    if (!Open(view, blockId))
                        continue;
                    if (view.OurPresence(blockId) >= config.DemandPresence)
                        continue;

                    var score = Score(view, config, blockId, 1);
                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    best = blockId;
                }
            }

            if (!best.IsValid)
                return false;

            var free = CrewOn(view, default);
            if (free == null)
                return false;

            into.Add(HouseIntent.Block(
                HouseOrder.OperateInBlock, free.Id, best, TierExpand,
                "there is money on that street and nobody holding it"));
            return true;
        }

        /// <summary>The ask, and the two steps after a no.</summary>
        static bool Ask(HouseView view, HouseMindConfig config, TerritoryBlockId blockId,
            Crew crew, List<HouseIntent> into)
        {
            var doors = view.Businesses(blockId);
            var bestRate = 0;
            var best = default(HouseDoor);
            var found = false;
            for (var i = 0; i < doors.Count; i++)
            {
                var door = doors[i];
                // NEVER A DOOR ANOTHER HOUSE PROTECTS. Taking one off a family is a
                // decision about that family, and RIVAL-007 makes it.
                if (!door.Unprotected && door.Protector != view.House)
                    continue;
                if (door.Shut || !door.Trades)
                    continue;
                // NEVER TWO DEMANDS IN A ROW. A man who has said no is answered by the
                // threat and the lean below, not by the same question again - and he
                // stays answered after the threat moves him off Defiant.
                if (Refused(view, door.BusinessId))
                    continue;
                if (!Offers(door, TerritoryRacketIntent.Demand))
                    continue;
                if (door.WeeklyRate <= bestRate)
                    continue;
                bestRate = door.WeeklyRate;
                best = door;
                found = true;
            }

            if (found)
            {
                into.Add(HouseIntent.Door(
                    crew.Id, best.BusinessId, TerritoryRacketIntent.Demand, TierExpand,
                    "a door on our street that pays nobody"));
                return true;
            }

            // Nothing left to ask. What refused us gets one threat and then one lean.
            for (var i = 0; i < view.Defiances.Count; i++)
            {
                var defiance = view.Defiances[i];
                if (defiance.BlockId != blockId)
                    continue;
                if (view.GameHour - defiance.OpenedAt < config.DemandCooldownHours)
                    continue;

                // ONE THREAT, THEN ONE LEAN, THEN LET HIM BE. Anything past that is a
                // war with a shopkeeper, and RIVAL-006 decides whether to have one.
                if (defiance.Threats >= 2)
                    continue;

                var door = DoorOf(view, blockId, defiance.BusinessId);
                if (defiance.Threats == 0 &&
                    Offers(door, TerritoryRacketIntent.Threaten))
                {
                    into.Add(HouseIntent.Door(
                        crew.Id, defiance.BusinessId, TerritoryRacketIntent.Threaten,
                        TierExpand, "he was asked and said no"));
                    return true;
                }

                into.Add(HouseIntent.Block(
                    HouseOrder.LeanOnHoldouts, crew.Id, blockId, TierExpand,
                    "the holdouts on that street get a visit"));
                return true;
            }

            return false;
        }

        /// <summary>D8's score: what a week there is worth, less the walk, the law and
        /// what the order costs.</summary>
        static int Score(HouseView view, HouseMindConfig config, TerritoryBlockId blockId,
            int hops)
        {
            var take = 0;
            var doors = view.Businesses(blockId);
            for (var i = 0; i < doors.Count; i++)
                if (doors[i].Unprotected && doors[i].Trades && !doors[i].Shut)
                    take += doors[i].WeeklyRate;

            var heat = (int)(view.PoliceAttention(blockId) * config.HeatCostPerPoint);
            return take - hops * config.HopCostDollars - heat;
        }

        // ------------------------------------------------------------------ readings

        /// <summary>D17. Every block this family leads is held properly - Controlled or
        /// better, with at least half its doors paying us.</summary>
        static bool Settled(HouseView view, HouseMindConfig config)
        {
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var blockId = view.Blocks[b];
                if (view.Leader(blockId) != view.House)
                    continue;

                var state = view.ControlState(blockId);
                if (state != TerritoryControlState.Controlled &&
                    state != TerritoryControlState.Dominated)
                    return false;

                var doors = view.Businesses(blockId);
                var trading = 0;
                var paying = 0;
                for (var i = 0; i < doors.Count; i++)
                {
                    if (!doors[i].Trades)
                        continue;
                    trading++;
                    if (doors[i].Standing == TerritoryProtectionState.Compliant)
                        paying++;
                }
                if (trading > 0 && paying < trading * config.StableDoorsShare)
                    return false;
            }
            return true;
        }

        /// <summary>Ground worth walking onto: nobody has it, or somebody has it loosely.
        /// </summary>
        static bool Open(HouseView view, TerritoryBlockId blockId)
        {
            var state = view.ControlState(blockId);
            if (state != TerritoryControlState.Uncontrolled &&
                state != TerritoryControlState.Influenced &&
                state != TerritoryControlState.Unknown)
                return false;

            var leader = view.Leader(blockId);
            return !leader.IsValid || leader == view.House;
        }

        /// <summary>Whether the door menu would offer this row at all - the same rule
        /// the player's own key is lit by, so a mind can never ask for something a
        /// surface would not offer.</summary>
        static bool Offers(HouseDoor door, TerritoryRacketIntent intent)
        {
            if (!door.BusinessId.IsValid)
                return false;
            TerritoryRacketOrders.For(
                door.Standing, door.Tenure, racketable: door.Trades, hasCrew: true,
                atDoor: true, askingPrice: 0, into: Rows);
            for (var i = 0; i < Rows.Count; i++)
                if (Rows[i].Kind == TerritoryDoorRowKind.Racket &&
                    Rows[i].Intent == intent && Rows[i].Available)
                    return true;
            return false;
        }

        /// <summary>He has already said no to us and does not pay us. The ladder has
        /// him; the ask does not.</summary>
        static bool Refused(HouseView view, TerritoryBusinessId businessId)
        {
            for (var i = 0; i < view.Defiances.Count; i++)
                if (view.Defiances[i].BusinessId == businessId)
                    return true;
            return false;
        }

        static HouseDoor DoorOf(
            HouseView view, TerritoryBlockId blockId, TerritoryBusinessId businessId)
        {
            var doors = view.Businesses(blockId);
            for (var i = 0; i < doors.Count; i++)
                if (doors[i].BusinessId == businessId)
                    return doors[i];
            return default;
        }

        static bool Protects(HouseView view, TerritoryBlockId blockId)
        {
            var doors = view.Businesses(blockId);
            for (var i = 0; i < doors.Count; i++)
                if (doors[i].Standing == TerritoryProtectionState.Compliant)
                    return true;
            return false;
        }

        /// <summary>The crew that answers for a block, or the first that could.</summary>
        static Crew CrewFor(HouseView view, TerritoryBlockId blockId)
        {
            var roster = view.Roster;
            var paper = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < paper.Count; i++)
            {
                if (paper[i].BlockId != blockId)
                    continue;
                for (var c = 0; c < roster.Crews.Count; c++)
                    if (roster.Crews[c].LieutenantId == paper[i].LeaderId)
                        return roster.Crews[c];
            }
            return roster.Crews.Count > 0 ? roster.Crews[0] : null;
        }

        /// <summary>
        /// Whose crew this street is. The one whose lieutenant answers for it if there is
        /// one, and otherwise the first crew with men on their feet - the mind has no
        /// window onto the street, so "free" is what the books can say.
        /// </summary>
        static Crew CrewOn(HouseView view, TerritoryBlockId blockId)
        {
            var roster = view.Roster;
            if (blockId.IsValid)
                for (var i = 0; i < roster.Crews.Count; i++)
                {
                    var crew = roster.Crews[i];
                    if (Active(roster, crew) >= 2 &&
                        Answers(roster, crew.LieutenantId, blockId))
                        return crew;
                }

            for (var i = 0; i < roster.Crews.Count; i++)
                if (Active(roster, roster.Crews[i]) >= 2)
                    return roster.Crews[i];
            return null;
        }

        static bool Answers(Roster roster, int leaderId, TerritoryBlockId blockId)
        {
            var paper = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < paper.Count; i++)
                if (paper[i].LeaderId == leaderId && paper[i].BlockId == blockId)
                    return true;
            return false;
        }

        static bool Room(Roster roster, int leaderId)
        {
            var leader = roster.Find(leaderId);
            if (leader == null)
                return false;
            var held = 0;
            var paper = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < paper.Count; i++)
                if (paper[i].LeaderId == leaderId)
                    held++;
            return held < Command.BlockCap(leader, roster.Organization.Limits);
        }

        static bool HasCollector(Roster roster, Crew crew)
        {
            for (var i = 0; i < crew.HoodIds.Count; i++)
            {
                var man = roster.Find(crew.HoodIds[i]);
                if (man != null && !man.Gone && man.Duty == Duty.Collector)
                    return true;
            }
            return false;
        }

        static bool Filed(HouseView view, OrderType type, int crewId)
        {
            if (view.Book == null)
                return false;
            var jobs = view.Book.Jobs;
            for (var i = 0; i < jobs.Count; i++)
                if (jobs[i].Type == type && jobs[i].CrewId == crewId &&
                    jobs[i].Stage != JobStage.Finished)
                    return true;
            return false;
        }

        static int Hoods(Roster roster, Crew crew)
        {
            var count = 0;
            for (var i = 0; i < crew.HoodIds.Count; i++)
            {
                var man = roster.Find(crew.HoodIds[i]);
                if (man != null && !man.Gone && man.Status == CharacterStatus.Active)
                    count++;
            }
            return count;
        }

        static int Active(Roster roster, Crew crew)
        {
            var lieutenant = roster.Find(crew.LieutenantId);
            var count = lieutenant != null && !lieutenant.Gone ? 1 : 0;
            return count + Hoods(roster, crew);
        }
    }
}
