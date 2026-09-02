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

        /// <summary>D10 / D22. How long an attack stays worth answering, how close to
        /// the front is close enough to alarm it, and how many quiet thinks a family
        /// wants behind it before it starts spending on things.</summary>
        public float AnswerWindowHours = 12f;

        public float ThreatMemoryHours = 24f;

        public float HqAlarmMetres = 60f;

        public int QuietThinks = 3;

        /// <summary>D9's reserve, applied to a purchase: the price on top of the week.
        /// </summary>
        public int MaxWeaponPrice = 2_000;

        public int MaxVehiclePrice = 12_000;
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
            HouseView view, HouseMindConfig config, List<HouseIntent> into) =>
            Think(view, config, HouseRelationsConfig.Default, into);

        /// <summary>The same turn of mind, with the city's own relations numbers.
        /// </summary>
        public static int Think(HouseView view, HouseMindConfig config,
            HouseRelationsConfig relations, List<HouseIntent> into)
        {
            relations = relations ?? HouseRelationsConfig.Default;
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
            if (Home(view, config, into))
                return TierSurvive;
            if (Merge(view, config, into))
                return TierWages;
            if (Replace(view, config, into))
                return TierReplace;
            if (Answer(view, config, into))
                return TierAnswer;
            if (Defend(view, config, into))
                return TierDefend;
            if (Feud(view, config, relations, into))
                return TierDefend;
            if (Expand(view, config, into))
                return TierExpand;
            if (Buy(view, config, into))
                return TierInvest;

            return into.Count > 0 ? TierCollect : 0;
        }

        // ------------------------------------------------------------------- tier 1

        /// <summary>
        /// THE FRONT. Men who shot at ours are near our own door, or there is trouble on
        /// the street the front stands on: a crew sits on it. Everything else waits.
        /// </summary>
        static bool Home(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            if (!view.Front.IsValid)
                return false;

            var alarmed = false;
            for (var i = 0; i < view.Threats.Count && !alarmed; i++)
                alarmed = view.Threats[i].AtOurFront &&
                          view.GameHour - view.Threats[i].At <= config.ThreatMemoryHours;
            for (var i = 0; i < view.Incidents.Count && !alarmed; i++)
                alarmed = view.Incidents[i].BlockId == view.FrontBlock;
            if (!alarmed)
                return false;

            var crew = CrewOn(view, view.FrontBlock);
            if (crew == null || Filed(view, OrderType.Guard, crew.Id))
                return false;

            into.Add(HouseIntent.Work(
                Aimed(OrderType.Guard, crew.Id, view.Front, view.FrontBlock),
                TierSurvive, "they came to our own door"));
            return true;
        }

        // ------------------------------------------------------------------- tier 5

        /// <summary>
        /// ANSWER. Somebody hit a door we are paid to keep the peace at, or a shopkeeper
        /// told us no. Both have a window, and a house that lets the window close is
        /// worth less on that street for as long as the street remembers.
        ///
        /// Against an attack: sic a crew on them if we have one near enough, otherwise
        /// sit on the door. Against a refusal: one threat, then one lean, then - and
        /// only under a hard policy - the shutters.
        /// </summary>
        static bool Answer(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            for (var i = 0; i < view.Incidents.Count; i++)
            {
                var incident = view.Incidents[i];
                if (incident.Unanswered <= 0)
                    continue;
                if (view.GameHour - incident.Since > config.AnswerWindowHours)
                    continue;

                var crew = CrewOn(view, incident.BlockId);
                if (crew == null)
                    continue;

                // Their men are still on the street and we have somebody near: go at
                // them. The street decides how that ends.
                if (InReach(view, config, incident.BlockId) &&
                    !Filed(view, OrderType.Assault, crew.Id))
                {
                    into.Add(HouseIntent.Work(
                        Aimed(OrderType.Assault, crew.Id, default, incident.BlockId),
                        TierAnswer, "somebody put hands on a door we are paid for"));
                    return true;
                }

                // Nobody to chase: sit on the door instead. That is an answer too.
                var door = OursOn(view, incident.BlockId);
                if (door.IsValid && !Filed(view, OrderType.Guard, crew.Id))
                {
                    into.Add(HouseIntent.Work(
                        Aimed(OrderType.Guard, crew.Id, door, incident.BlockId),
                        TierAnswer, "the door gets men on it until this passes"));
                    return true;
                }
            }

            return Ladder(view, config, into);
        }

        /// <summary>
        /// THE LADDER AT A DOOR THAT SAID NO. One threat, one lean, and then the crew's
        /// own policy: a hard crew puts the shutters in, an ordinary one takes the till,
        /// a lenient one files the refusal and walks away. Never at our own doors and
        /// never at a door that pays us - the mind does not even propose it.
        /// </summary>
        static bool Ladder(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            for (var i = 0; i < view.Defiances.Count; i++)
            {
                var defiance = view.Defiances[i];
                if (view.GameHour - defiance.OpenedAt < config.DemandCooldownHours)
                    continue;

                var crew = CrewOn(view, defiance.BlockId);
                if (crew == null)
                    continue;

                var door = DoorOf(view, defiance.BlockId, defiance.BusinessId);
                if (door.Tenure == DoorTenure.Ours ||
                    door.Standing == TerritoryProtectionState.Compliant)
                    continue;

                if (defiance.Threats == 0 &&
                    Offers(door, TerritoryRacketIntent.Threaten))
                {
                    into.Add(HouseIntent.Door(
                        crew.Id, defiance.BusinessId, TerritoryRacketIntent.Threaten,
                        TierAnswer, "he was asked and said no"));
                    return true;
                }

                if (defiance.Threats == 1)
                {
                    into.Add(HouseIntent.Block(
                        HouseOrder.LeanOnHoldouts, crew.Id, defiance.BlockId, TierAnswer,
                        "the holdouts on that street get a visit"));
                    return true;
                }

                if (defiance.Threats < 2)
                    continue;

                // The crew's own policy decides what comes after the leaning, exactly as
                // it does when the player's men are the ones standing there.
                var policy = PolicyOf(view, crew);
                if (policy == CrewPolicy.Lenient)
                    continue;

                var work = policy == CrewPolicy.Normal ? OrderType.Raid : OrderType.SmashUp;
                if (Filed(view, work, crew.Id))
                    continue;

                into.Add(HouseIntent.Work(
                    Aimed(work, crew.Id, defiance.BusinessId, defiance.BlockId),
                    TierAnswer, "he was leant on twice and still says no"));
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------- tier 6

        /// <summary>
        /// CONSOLIDATE. A street we lead that is contested, or whose doors are wavering
        /// or late, is walked door to door before anybody looks at a new one.
        /// </summary>
        static bool Defend(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var blockId = view.Blocks[b];
                if (view.Leader(blockId) != view.House)
                    continue;

                var contested = view.ControlState(blockId) == TerritoryControlState.Contested;
                var loose = false;
                var doors = view.Businesses(blockId);
                for (var i = 0; i < doors.Count && !loose; i++)
                    loose = doors[i].Trades && !doors[i].Shut &&
                            (doors[i].Standing == TerritoryProtectionState.Hesitant ||
                             doors[i].Late);
                if (!contested && !loose)
                    continue;

                var crew = CrewOn(view, blockId);
                if (crew == null)
                    continue;

                if (contested)
                {
                    into.Add(HouseIntent.Block(
                        HouseOrder.OperateInBlock, crew.Id, blockId, TierDefend,
                        "somebody else is trying that street"));
                    return true;
                }

                into.Add(HouseIntent.Block(
                    HouseOrder.ShakeDownBlock, crew.Id, blockId, TierDefend,
                    "the doors there have gone loose"));
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------- the feud

        /// <summary>
        /// WHAT WE DO ABOUT ANOTHER FAMILY, one step at a time (design §26, D13).
        ///
        /// A house never skips a step. It warns, then it threatens, then it sends a
        /// bill, then it takes a door back, then it goes at their collector, then at
        /// their shops - and only at the top, and only at war, at a man by name. The
        /// ladder is what makes a war something the player watches coming.
        ///
        /// The stance is decided here too (D15): war is declared only by a house that
        /// can pay its men through one, and a house that cannot - or that has lost too
        /// many - offers a truce.
        /// </summary>
        static bool Feud(HouseView view, HouseMindConfig config,
            HouseRelationsConfig relations, List<HouseIntent> into)
        {
            for (var i = 0; i < view.Rivals.Count; i++)
            {
                var them = view.Rivals[i];
                if (them == view.House || !them.IsValid)
                    continue;

                var stance = view.StanceToward(them);
                var step = view.Ladder(them);

                // WAR AND PEACE FIRST. A family with a month's wages behind it and a
                // grudge worth shops declares; one that cannot pay through the month it
                // is already in offers a truce, whatever it is owed.
                if (stance == Stance.War &&
                    (view.Endurance < relations.MinWarDays ||
                     view.LossesThisWar >= relations.LossesToSueForPeace))
                {
                    into.Add(HouseIntent.Stand(them, Stance.Truce, TierDefend,
                        "we cannot pay the men through this"));
                    return true;
                }

                if (stance != Stance.War && step >= LadderStep.AttackBusiness &&
                    view.Endurance >= relations.MinWarDays &&
                    view.Endurance >= view.TheirEndurance(them))
                {
                    into.Add(HouseIntent.Stand(them, Stance.War, TierDefend,
                        "they have taken too much"));
                    return true;
                }

                if (stance == Stance.Peace &&
                    step >= LadderStep.Threat && step < LadderStep.AttackBusiness)
                {
                    into.Add(HouseIntent.Stand(them, Stance.Truce, TierDefend,
                        "they keep off our streets from now on"));
                    return true;
                }

                // THEN THE STEP ITSELF.
                switch (step)
                {
                    case LadderStep.Ignore:
                        continue;

                    case LadderStep.DiplomaticWarning:
                        into.Add(HouseIntent.Word(them, "warns them off our streets", 0,
                            TierDefend, "a word, before anything else"));
                        return true;

                    case LadderStep.Threat:
                        into.Add(HouseIntent.Word(
                            them, "will not warn them again", 0, TierDefend,
                            "the second word is the last one"));
                        return true;

                    case LadderStep.DemandCompensation:
                        into.Add(HouseIntent.Word(
                            them, "sends a bill for what they took",
                            EconomyPrices.Shakedown * Theirs(view, them), TierDefend,
                            "they can pay for what they took"));
                        return true;

                    case LadderStep.RetakeBusiness:
                        if (Retake(view, them, into))
                            return true;
                        continue;

                    case LadderStep.BeatCollector:
                    case LadderStep.AttackBusiness:
                    case LadderStep.KidnapCrewMember:
                    case LadderStep.KillCrewMember:
                        if (Strike(view, them, step, stance, into))
                            return true;
                        continue;
                }
            }
            return false;
        }

        /// <summary>How many doors on ground we can see are theirs.</summary>
        static int Theirs(HouseView view, TerritoryGangId them)
        {
            var count = 0;
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var doors = view.Businesses(view.Blocks[b]);
                for (var i = 0; i < doors.Count; i++)
                    if (doors[i].Protector == them)
                        count++;
            }
            return count > 0 ? count : 1;
        }

        /// <summary>A door of theirs, asked for. The one place the mind is allowed at a
        /// door another house protects, and it takes a grudge worth four steps.</summary>
        static bool Retake(HouseView view, TerritoryGangId them, List<HouseIntent> into)
        {
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var blockId = view.Blocks[b];
                var crew = CrewOn(view, blockId);
                if (crew == null)
                    continue;

                var doors = view.Businesses(blockId);
                for (var i = 0; i < doors.Count; i++)
                {
                    if (doors[i].Protector != them || doors[i].Shut || !doors[i].Trades)
                        continue;
                    if (!Offers(doors[i], TerritoryRacketIntent.Demand))
                        continue;
                    into.Add(HouseIntent.Door(
                        crew.Id, doors[i].BusinessId, TerritoryRacketIntent.Demand,
                        TierDefend, "that door was ours"));
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Hands on them. Which hands depends on the step, and the two hardest are only
        /// ever laid at war - a family does not burn shops it has a truce with.
        /// </summary>
        static bool Strike(HouseView view, TerritoryGangId them, LadderStep step,
            Stance stance, List<HouseIntent> into)
        {
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var blockId = view.Blocks[b];
                var crew = CrewOn(view, blockId);
                if (crew == null)
                    continue;

                var doors = view.Businesses(blockId);
                for (var i = 0; i < doors.Count; i++)
                {
                    if (doors[i].Protector != them)
                        continue;

                    if (step == LadderStep.BeatCollector)
                    {
                        if (Filed(view, OrderType.Assault, crew.Id))
                            return false;
                        into.Add(HouseIntent.Work(
                            Aimed(OrderType.Assault, crew.Id, doors[i].BusinessId,
                                blockId),
                            TierDefend, "their men on our streets"));
                        return true;
                    }

                    if (stance != Stance.War)
                        return false;

                    var work = step >= LadderStep.KidnapCrewMember
                        ? OrderType.Torch
                        : OrderType.SmashUp;
                    if (Filed(view, work, crew.Id))
                        return false;
                    into.Add(HouseIntent.Work(
                        Aimed(work, crew.Id, doors[i].BusinessId, blockId),
                        TierDefend, "what they are paid for goes in"));
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------- tier 8

        /// <summary>
        /// BUY. Only with a week's wages still in the safe after the price, and only
        /// when nothing louder has needed doing for a while: a car for a crew on foot,
        /// then a gun for a hood with empty hands.
        /// </summary>
        static bool Buy(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            if (view.QuietThinks < config.QuietThinks)
                return false;

            var roster = view.Roster;
            var reserve = config.ReserveDays * view.DailyPayroll;

            for (var i = 0; i < roster.Crews.Count; i++)
            {
                var crew = roster.Crews[i];
                if (CrewKit.HasVehicle(roster, crew))
                    continue;
                var car = Cheapest(ArmoryCatalog.Vehicles, config.MaxVehiclePrice);
                if (car.Price <= 0 || view.Safe - car.Price < reserve)
                    break;
                into.Add(HouseIntent.Buy(
                    car.Kind, car.DisplayName, car.Price, crew.LieutenantId, crew.Id,
                    TierInvest, "the crew is walking to work"));
                return true;
            }

            for (var i = 0; i < roster.Crews.Count; i++)
            {
                var crew = roster.Crews[i];
                for (var h = 0; h < crew.HoodIds.Count; h++)
                {
                    var man = roster.Find(crew.HoodIds[h]);
                    if (man == null || man.Gone || Armed(roster, man.Id))
                        continue;
                    var gun = Cheapest(ArmoryCatalog.Weapons, config.MaxWeaponPrice);
                    if (gun.Price <= 0 || view.Safe - gun.Price < reserve)
                        return false;
                    into.Add(HouseIntent.Buy(
                        gun.Kind, gun.DisplayName, gun.Price, man.Id, crew.Id,
                        TierInvest, "a man with empty hands"));
                    return true;
                }
            }
            return false;
        }

        static ArmoryItem Cheapest(ArmoryItem[] stock, int ceiling)
        {
            var best = default(ArmoryItem);
            for (var i = 0; i < stock.Length; i++)
                if (stock[i].Price > 0 && stock[i].Price <= ceiling &&
                    (best.Price <= 0 || stock[i].Price < best.Price))
                    best = stock[i];
            return best;
        }

        static bool Armed(Roster roster, int characterId)
        {
            for (var i = 0; i < roster.Equipment.Count; i++)
                if (RosterOps.IsWeapon(roster.Equipment[i].Kind) &&
                    roster.Equipment[i].HolderId == characterId)
                    return true;
            return false;
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

            // A door that said no is tier 5's business, not this one's: the ladder
            // answers a refusal, and asking again is what a ladder is FOR not having.
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

        /// <summary>A job aimed at a door and a block, in the shape the street reads:
        /// the business it is against, the block it is on, and the crew that has it.
        /// </summary>
        static Job Aimed(OrderType type, int crewId, TerritoryBusinessId businessId,
            TerritoryBlockId blockId)
        {
            var job = new Job
            {
                CrewId = crewId,
                Type = type,
                Men = 1,
                TargetBusinessId = businessId.IsValid ? businessId.Value : "",
                TargetLabel = blockId.IsValid ? blockId.Value : "",
            };
            if (blockId.IsValid)
                job.BlockTargets.Add(0);
            return job;
        }

        /// <summary>Their men are still about, and we have somebody near enough.</summary>
        static bool InReach(
            HouseView view, HouseMindConfig config, TerritoryBlockId blockId)
        {
            for (var i = 0; i < view.Threats.Count; i++)
            {
                var threat = view.Threats[i];
                if (threat.BlockId != blockId || !threat.InReach)
                    continue;
                if (view.GameHour - threat.At <= config.ThreatMemoryHours)
                    return true;
            }
            return false;
        }

        /// <summary>A door on this block that pays US - somewhere worth sitting on.
        /// </summary>
        static TerritoryBusinessId OursOn(HouseView view, TerritoryBlockId blockId)
        {
            var doors = view.Businesses(blockId);
            for (var i = 0; i < doors.Count; i++)
                if (doors[i].Standing == TerritoryProtectionState.Compliant)
                    return doors[i].BusinessId;
            return default;
        }

        static CrewPolicy PolicyOf(HouseView view, Crew crew) =>
            crew != null ? crew.Policy : CrewPolicy.Normal;

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
