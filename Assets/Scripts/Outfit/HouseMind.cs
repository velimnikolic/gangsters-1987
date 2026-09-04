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

        /// <summary>D7 / A19. A house thinks every game hour (the user's ruling of
        /// 2026-09-04; it was four), and executes at most three intents per think.
        /// </summary>
        public float ThinkEveryHours = 1f;

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

        /// <summary>
        /// A10 / L1. How many hoods a crew is filled to - "za sad do 4". BOUND to the
        /// number of bodies the street deals a crew (review finding C4): a fifth hood
        /// would be a man on the payroll with no body on the pavement, so raising one
        /// raises both or nothing.
        /// </summary>
        public int HoodsPerCrew => Crew.MaxTacticalHoods;

        /// <summary>A11 / L1. A house wants one crew per block it leads, plus one.
        /// </summary>
        public int CrewsPerBlock = 1;

        public int SpareCrews = 1;

        /// <summary>D17. The racket's own cadence.</summary>
        public float DemandPresence = 25f;

        /// <summary>A21. A block is not walked door to door twice inside a day - the
        /// cooldown lives HERE, in the mind, and not in the shared WorthAsking, so the
        /// player's own SHAKE DOWN THE BLOCK is untouched.</summary>
        public float DemandCooldownHours = 24f;

        public float StableDoorsShare = 0.5f;

        /// <summary>D10 / D22. How long an attack stays worth answering, how close to
        /// the front is close enough to alarm it, and how many quiet thinks a family
        /// wants behind it before it starts spending on things.</summary>
        public float AnswerWindowHours = 12f;

        public float ThreatMemoryHours = 24f;

        /// <summary>A22 / S1. A guard comes off twenty-four game hours after the LAST
        /// incident on the block it stands on, not the first. A block under repeated
        /// attack keeps its men; one shooting costs one crew one day.</summary>
        public float GuardStandsHours = 24f;

        public float HqAlarmMetres = 60f;

        public int QuietThinks = 3;

        /// <summary>A24 / P4. A refused intent is not proposed again for this many game
        /// hours. Hours and not thinks, so a faster mind cannot shorten it.</summary>
        public float RefusalBackoffHours = 12f;

        /// <summary>A3 / S2. A round in which nothing has moved for this long is
        /// abandoned and the block goes back on the schedule.</summary>
        public float RoundStallHours = 2f;

        /// <summary>
        /// A30. Days of payroll the doors' weekly take must cover before a house signs
        /// another man - the brake on a family recruiting itself broke. Zero lifts it
        /// and leaves only the reserve rule (D9), which looks at the safe and not at
        /// the income.
        ///
        /// It was written when the yardstick's blocks held three to six shops. A real
        /// block holds about fifteen (the user, 2026-09-04), so the gate binds far less
        /// often than the figure that justified it suggested. Measured both ways in
        /// GAN-394.
        /// </summary>
        public int GrowthIncomeDays = 7;

        /// <summary>S2. A leg whose walkers have not arrived is marched again this
        /// often, in game hours (a quarter of an hour is fifteen real seconds).</summary>
        public float RoundRemarchHours = 0.25f;

        /// <summary>Review finding C1. A whole-block walk puts every door's telephone
        /// call in one hour; the mind does not send one onto a block the law is
        /// already watching this closely (the attention scale caps at 100).</summary>
        public float WalkAttentionCap = 40f;

        /// <summary>D9's reserve, applied to a purchase: the price on top of the week.
        /// </summary>
        public int MaxWeaponPrice = 2_000;

        public int MaxVehiclePrice = 12_000;
    }

    /// <summary>
    /// WHERE A HOUSE IS IN THE USER'S ORDER OF THINGS (AI-004, C3/C5): take the free
    /// city, then turn money into men and guns, then collide. Derived from the view on
    /// every think and never stored - a stored phase would come back empty after a load
    /// and read as a bug.
    /// </summary>
    public enum HousePhase
    {
        /// <summary>There is still open ground to take.</summary>
        Land,

        /// <summary>There is not, and the house is under its target size or has men
        /// with empty hands.</summary>
        Men,

        /// <summary>It is at target and armed.</summary>
        War,
    }

    /// <summary>
    /// THE MIND EVERY FAMILY RUNS, the player's included in everything but the fact that
    /// nobody calls it for him.
    ///
    /// It reads a <see cref="HouseView"/> and emits <see cref="HouseIntent"/>s. It walks
    /// the strict priority tiers (D8) and emits for the FIRST tier with a feasible
    /// candidate, plus any due collection - tier 4 never waits behind a war - and any
    /// watch whose window has passed.
    ///
    /// PURE. No ledger, no runtime, no roll, no clock of its own. The view is the wall;
    /// a mind that reached past it would be playing a different game from the player.
    /// </summary>
    public static class HouseMind
    {
        /// <summary>The tiers, by their number in the plan.</summary>
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

            // A WATCH WHOSE WINDOW HAS PASSED COMES OFF FIRST (S1). Cancelling costs
            // nothing and frees a crew for everything below.
            Release(view, config, into);

            // TIER 4 NEVER WAITS. A round due today goes out whatever else the family is
            // doing - the money is what everything else is paid from.
            Collect(view, config, into);
            var tier = Walk(view, config, relations, into);
            DropTheUnbuilt(into);
            return tier;
        }

        /// <summary>The tiers themselves, in order. Split out so nothing may be emitted
        /// without passing the built-orders gate above.</summary>
        static int Walk(HouseView view, HouseMindConfig config,
            HouseRelationsConfig relations, List<HouseIntent> into)
        {
            // The first tier with something to do, in order.
            if (Home(view, config, into))
                return TierSurvive;
            if (Merge(view, config, into))
                return TierWages;
            if (Replace(view, config, into))
                return TierReplace;
            if (Law(view, config, into))
                return TierReplace;
            if (Answer(view, config, into))
                return TierAnswer;
            if (Defend(view, config, into))
                return TierDefend;
            if (Feud(view, config, relations, into))
                return TierDefend;
            if (Expand(view, config, into))
                return TierExpand;
            if (Grow(view, config, into))
                return TierInvest;
            if (Buy(view, config, into))
                return TierInvest;

            return into.Count > 0 ? TierCollect : 0;
        }

        /// <summary>
        /// A MIND MAY ONLY FILE AN ORDER THAT DOES SOMETHING (RIVAL-009). A person who
        /// files an order with no effect can see nothing happened and stop; twenty
        /// families would file it every week for ever and the tally would read as a
        /// working economy. <see cref="OrderEffects"/> is the one place that says which
        /// orders are built, and this is the one place a mind is held to it.
        /// </summary>
        static void DropTheUnbuilt(List<HouseIntent> into)
        {
            for (var i = into.Count - 1; i >= 0; i--)
                if (into[i].Kind == HouseIntentKind.Job &&
                    (into[i].Job == null || !OrderEffects.Built(into[i].Job.Type)))
                    into.RemoveAt(i);
        }

        /// <summary>
        /// THE ONE DOOR AN INTENT LEAVES THROUGH. A thing the gateway refused inside
        /// the back-off window is not proposed again (P4); the tier goes on looking
        /// for something else to do rather than proposing the same refusal for ever.
        /// </summary>
        static bool Propose(HouseView view, List<HouseIntent> into, HouseIntent intent)
        {
            if (view.Blocked(intent.Key))
                return false;
            into.Add(intent);
            return true;
        }

        // ------------------------------------------------------------------- the phase

        /// <summary>
        /// LAND, MEN or WAR, read off the view (AI-004). Land while there is still a
        /// door on our ground worth asking or a neighbour worth walking onto; Men while
        /// there is not and the house is under its target size or has empty hands; War
        /// once it is full and armed. The border test of AI-007 is folded in: a house
        /// with nowhere open left is one whose neighbours are somebody else's.
        /// </summary>
        public static HousePhase PhaseOf(HouseView view, HouseMindConfig config)
        {
            config = config ?? HouseMindConfig.Default;
            if (view?.Roster == null)
                return HousePhase.Land;
            if (!NothingLeftToAsk(view, config) || BestNeighbour(view, config).IsValid)
                return HousePhase.Land;
            if (UnderTarget(view, config) || UnarmedMen(view) > 0)
                return HousePhase.Men;
            return HousePhase.War;
        }

        /// <summary>
        /// WHY THE PHASE READS WHAT IT READS, in one line - the ground still worth
        /// asking, the neighbour worth walking onto, and whether there is a crew to
        /// send. A house whose phase says LAND while it files nothing is a house whose
        /// reading and whose acting disagree, and this is what names which of them is
        /// wrong (AI-000's lesson; the probe and the yardstick both print it).
        /// </summary>
        public static string PhaseNote(HouseView view, HouseMindConfig config)
        {
            config = config ?? HouseMindConfig.Default;
            if (view?.Roster == null)
                return "no books";

            var note = "";
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var blockId = view.Blocks[b];
                if (!AnyAskable(view, blockId))
                    continue;
                var walked = view.LastWalked(blockId);
                note += "askable " + blockId.Value +
                        (view.OurPresence(blockId) < config.DemandPresence
                            ? " (presence " + (int)view.OurPresence(blockId) + " short)"
                            : "") +
                        (walked >= 0.0 && view.GameHour - walked < config.DemandCooldownHours
                            ? " (walked " + (int)(view.GameHour - walked) + "h ago)"
                            : "") +
                        (view.PoliceAttention(blockId) > config.WalkAttentionCap
                            ? " (law watching)"
                            : "") +
                        (CrewOn(view, blockId) == null ? " (no crew)" : "") + "; ";
            }

            var best = BestNeighbour(view, config);
            if (best.IsValid)
                note += "neighbour " + best.Value + " worth $" +
                        Score(view, config, best, 1) + "; ";
            note += CrewOn(view, default) == null
                ? "no crew is free"
                : FreeForNewGround(view, config) != null
                    ? "a crew is free"
                    : "every free crew is still working its own street";
            return note;
        }

        // ------------------------------------------------------------------- tier 1

        /// <summary>
        /// THE FRONT. Men who shot at ours are near our own door, or there is trouble on
        /// the street the front stands on: a crew sits on it. Everything else waits.
        ///
        /// The alarm has a window now (S1): a threat within ThreatMemoryHours, or an
        /// incident whose LAST hour is within GuardStandsHours. Without one the front
        /// was alarmed for as long as the power ledger remembered, and the guard was
        /// re-filed every think.
        /// </summary>
        static bool Home(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            if (!view.Front.IsValid)
                return false;
            if (!FrontAlarmed(view, config))
                return false;

            // ONE FRONT, ONE GUARD (S1): the watch is counted by the door it stands on,
            // not by the crew that stands it. Counting by crew was where Guard x2 came
            // from - every free crew was handed the same door in turn.
            if (Guarded(view, view.Front))
                return false;

            var crew = CrewOn(view, view.FrontBlock);
            if (crew == null)
                return false;

            return Propose(view, into, HouseIntent.Work(
                Aimed(OrderType.Guard, crew.Id, view.Front, view.FrontBlock),
                TierSurvive, "they came to our own door"));
        }

        static bool FrontAlarmed(HouseView view, HouseMindConfig config)
        {
            for (var i = 0; i < view.Threats.Count; i++)
                if (view.Threats[i].AtOurFront &&
                    view.GameHour - view.Threats[i].At <= config.ThreatMemoryHours)
                    return true;
            for (var i = 0; i < view.Incidents.Count; i++)
                if (view.Incidents[i].BlockId == view.FrontBlock &&
                    view.GameHour - view.Incidents[i].LastAt <= config.GuardStandsHours)
                    return true;
            return false;
        }

        /// <summary>
        /// THE WATCH COMES OFF (S1, ruling A1/A22). A Guard is Standing - it is never
        /// finished by the clock - so the mind is what takes it off: when the block it
        /// stands on has had no incident for GuardStandsHours, or when the crew it was
        /// filed on no longer exists (review finding C6: an orphan job is never worked
        /// and reads on the book for ever).
        /// </summary>
        static void Release(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            if (view.Book == null)
                return;
            var jobs = view.Book.Jobs;
            for (var i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                if (job.Stage == JobStage.Finished)
                    continue;

                if (view.Roster.FindCrew(job.CrewId) == null)
                {
                    Propose(view, into, HouseIntent.CallOff(job.Id, job.CrewId,
                        TierSurvive, "the crew that had this order is gone"));
                    continue;
                }

                if (job.Type != OrderType.Guard)
                    continue;

                var door = new TerritoryBusinessId(job.TargetBusinessId);
                var block = new TerritoryBlockId(job.TargetLabel);
                if (door == view.Front && FrontAlarmed(view, config))
                    continue;
                if (block.IsValid && BlockTroubledWithin(view, block, config.GuardStandsHours))
                    continue;

                Propose(view, into, HouseIntent.CallOff(job.Id, job.CrewId, TierSurvive,
                    "nothing has happened on that street for a day; the watch comes off"));
            }
        }

        static bool BlockTroubledWithin(HouseView view, TerritoryBlockId blockId, float hours)
        {
            for (var i = 0; i < view.Incidents.Count; i++)
                if (view.Incidents[i].BlockId == blockId &&
                    view.GameHour - view.Incidents[i].LastAt <= hours)
                    return true;
            for (var i = 0; i < view.Threats.Count; i++)
                if (view.Threats[i].BlockId == blockId &&
                    view.GameHour - view.Threats[i].At <= hours)
                    return true;
            return false;
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
                // Men already on a door of that block have answered for it (A22b);
                // a second crew on a second door of the same street is not an answer,
                // it is the house standing still on two corners.
                if (BlockGuarded(view, incident.BlockId))
                    continue;

                var crew = CrewOn(view, incident.BlockId);
                if (crew == null)
                    continue;

                // Their men are still on the street and we have somebody near: go at
                // them. The street decides how that ends.
                if (InReach(view, config, incident.BlockId) &&
                    !Filed(view, OrderType.Assault, crew.Id) &&
                    Propose(view, into, HouseIntent.Work(
                        Aimed(OrderType.Assault, crew.Id, default, incident.BlockId),
                        TierAnswer, "somebody put hands on a door we are paid for")))
                    return true;

                // Nobody to chase: sit on the door instead. That is an answer too
                // (ruling A22b: a standing guard answers the incident on its block).
                var door = OursOn(view, incident.BlockId);
                if (door.IsValid && !Guarded(view, door) &&
                    Propose(view, into, HouseIntent.Work(
                        Aimed(OrderType.Guard, crew.Id, door, incident.BlockId),
                        TierAnswer, "the door gets men on it until this passes")))
                    return true;
            }

            return Ladder(view, config, into);
        }

        /// <summary>
        /// THE LADDER AT A DOOR THAT SAID NO, OR WOULD NOT SAY YES. A refusal gets one
        /// threat, one lean, and then the crew's own policy: a hard crew puts the
        /// shutters in, an ordinary one takes the till, a lenient one files the refusal
        /// and walks away. A hesitation gets ONE threat after a day and is then left
        /// alone until there is a war (Z4, ruling A8) - never the same demand twice.
        /// Never at our own doors and never at a door that pays us.
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

                if (door.Standing == TerritoryProtectionState.Hesitant)
                {
                    // One word, then silence (A8). The lean below is for a man who
                    // said no; a man who cannot make up his mind is not walked at.
                    if (defiance.Threats == 0 &&
                        Offers(door, TerritoryRacketIntent.Threaten) &&
                        Propose(view, into, HouseIntent.Door(
                            crew.Id, defiance.BusinessId, TerritoryRacketIntent.Threaten,
                            TierAnswer, "he was asked and would not say")))
                        return true;
                    continue;
                }

                if (defiance.Threats == 0 &&
                    Offers(door, TerritoryRacketIntent.Threaten) &&
                    Propose(view, into, HouseIntent.Door(
                        crew.Id, defiance.BusinessId, TerritoryRacketIntent.Threaten,
                        TierAnswer, "he was asked and said no")))
                    return true;

                if (defiance.Threats == 1 && !view.RoundOut(crew.Id) &&
                    Propose(view, into, HouseIntent.Block(
                        HouseOrder.LeanOnHoldouts, crew.Id, defiance.BlockId, TierAnswer,
                        "the holdouts on that street get a visit")))
                    return true;

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

                if (Propose(view, into, HouseIntent.Work(
                    Aimed(work, crew.Id, defiance.BusinessId, defiance.BlockId),
                    TierAnswer, "he was leant on twice and still says no")))
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------- tier 6

        /// <summary>
        /// CONSOLIDATE. A street we lead that is contested is stood on before anybody
        /// looks at a new one. A street whose paying doors have gone LATE gets a
        /// collection sent by the mind itself (S5, ruling A23): only a collection cures
        /// lateness, and waiting for the block's weekly day left every late door late
        /// for another week. Hesitant doors no longer count as loose - the ladder has
        /// them (Z4) - so one wavering shopkeeper cannot keep a family at home for ever.
        /// </summary>
        static bool Defend(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var blockId = view.Blocks[b];
                if (view.Leader(blockId) != view.House)
                    continue;

                var contested = view.ControlState(blockId) == TerritoryControlState.Contested;
                var owedLate = 0;
                var doors = view.Businesses(blockId);
                for (var i = 0; i < doors.Count; i++)
                    if (doors[i].Trades && !doors[i].Shut && doors[i].Late)
                        owedLate += doors[i].Owed;
                if (!contested && owedLate <= 0)
                    continue;

                if (contested)
                {
                    var crew = CrewOn(view, blockId);
                    if (crew != null && Propose(view, into, HouseIntent.Block(
                        HouseOrder.OperateInBlock, crew.Id, blockId, TierDefend,
                        "somebody else is trying that street")))
                        return true;
                    continue;
                }

                // The gateway refuses a second round for a crew that has one out, so
                // a double collection is impossible; the mind still does not ask for
                // one while the last is walking.
                var collector = CrewFor(view, blockId);
                if (collector == null || view.RoundOut(collector.Id))
                    continue;
                if (Propose(view, into, HouseIntent.Block(
                    HouseOrder.CollectDues, collector.Id, blockId, TierDefend,
                    "the doors there are late; the bag goes round")))
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
        ///
        /// NOBODY STARTS A WAR WITH MEN IN THE CELLS (AI-007 R4): a house with a capo
        /// inside or a wanted man out does not collide until the law tickets have got
        /// them back. A war already declared goes on.
        /// </summary>
        static bool Feud(HouseView view, HouseMindConfig config,
            HouseRelationsConfig relations, List<HouseIntent> into)
        {
            var ready = ReadyToCollide(view);
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
                     view.LossesThisWar >= relations.LossesToSueForPeace) &&
                    Propose(view, into, HouseIntent.Stand(them, Stance.Truce, TierDefend,
                        "we cannot pay the men through this")))
                    return true;

                if (stance != Stance.War && step >= LadderStep.AttackBusiness && ready &&
                    view.Endurance >= relations.MinWarDays &&
                    view.Endurance >= view.TheirEndurance(them) &&
                    Propose(view, into, HouseIntent.Stand(them, Stance.War, TierDefend,
                        "they have taken too much")))
                    return true;

                if (stance == Stance.Peace &&
                    step >= LadderStep.Threat && step < LadderStep.AttackBusiness &&
                    Propose(view, into, HouseIntent.Stand(them, Stance.Truce, TierDefend,
                        "they keep off our streets from now on")))
                    return true;

                // THEN THE STEP ITSELF.
                switch (step)
                {
                    case LadderStep.Ignore:
                        continue;

                    case LadderStep.DiplomaticWarning:
                        if (Propose(view, into, HouseIntent.Word(them,
                            "warns them off our streets", 0, TierDefend,
                            "a word, before anything else")))
                            return true;
                        continue;

                    case LadderStep.Threat:
                        if (Propose(view, into, HouseIntent.Word(
                            them, "will not warn them again", 0, TierDefend,
                            "the second word is the last one")))
                            return true;
                        continue;

                    case LadderStep.DemandCompensation:
                        if (Propose(view, into, HouseIntent.Word(
                            them, "sends a bill for what they took",
                            EconomyPrices.Shakedown * Theirs(view, them), TierDefend,
                            "they can pay for what they took")))
                            return true;
                        continue;

                    case LadderStep.RetakeBusiness:
                        if (ready && Retake(view, config, them, into))
                            return true;
                        continue;

                    case LadderStep.BeatCollector:
                        if (ready && Strike(view, them, step, stance, into))
                            return true;
                        continue;

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

        /// <summary>No capo in a cell and no wanted man out (R4). The books say.
        /// </summary>
        static bool ReadyToCollide(HouseView view)
        {
            var roster = view.Roster;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                if (man.Gone)
                    continue;
                if (man.Status == CharacterStatus.Jailed && man.Rank == Rank.Lieutenant)
                    return false;
                if (man.WantedLevel > 0 && man.Status == CharacterStatus.Active)
                    return false;
            }
            return true;
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

        /// <summary>
        /// A door of theirs, asked for. The one place the mind is allowed at a door
        /// another house protects, and it takes a grudge worth four steps - and, since
        /// AI-007 (R2), full strength: a house collides only once it is at target and
        /// armed, and only with a house it believes is weaker than itself.
        /// </summary>
        static bool Retake(HouseView view, HouseMindConfig config, TerritoryGangId them,
            List<HouseIntent> into)
        {
            if (PhaseOf(view, config) != HousePhase.War)
                return false;
            if (view.TheirEndurance(them) >= view.Endurance)
                return false;

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
                    if (Propose(view, into, HouseIntent.Door(
                        crew.Id, doors[i].BusinessId, TerritoryRacketIntent.Demand,
                        TierDefend, "that door was ours")))
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
                        return Propose(view, into, HouseIntent.Work(
                            Aimed(OrderType.Assault, crew.Id, doors[i].BusinessId,
                                blockId),
                            TierDefend, "their men on our streets"));
                    }

                    if (stance != Stance.War)
                        return false;

                    var work = step >= LadderStep.KidnapCrewMember
                        ? OrderType.Torch
                        : OrderType.SmashUp;
                    if (Filed(view, work, crew.Id))
                        return false;
                    return Propose(view, into, HouseIntent.Work(
                        Aimed(work, crew.Id, doors[i].BusinessId, blockId),
                        TierDefend, "what they are paid for goes in"));
                }
            }
            return false;
        }

        // ------------------------------------------------------------------- tier 8

        /// <summary>
        /// GROW TO THE GROUND HELD (AI-004 L1/L2, rulings A10/A11). A house wants one
        /// crew per block it leads plus one, each filled to HoodsPerCrew, and it signs
        /// while it is under that and the reserve rule still holds. With two blocks and
        /// men to spare it makes a new crew: the best hood by Leadership is promoted and
        /// the paper hands his crew the next block (tier 4 does that on the next think).
        /// </summary>
        static bool Grow(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            var roster = view.Roster;
            var crews = roster.Crews;

            // GROW TO THE GROUND IT HOLDS - and no further than the ground pays for
            // (A30, an assumption written in the D-table for the user to veto). The
            // reserve rule alone looks at the safe; a house whose doors bring in less
            // than its men cost signs itself into three empty envelopes and a
            // desertion, which the paper sweep showed within a month.
            var weeklyTake = WeeklyTake(view);

            // First: every working crew up to strength.
            for (var i = 0; i < crews.Count; i++)
            {
                var crew = crews[i];
                if (IsDetail(view, crew) || !Led(roster, crew))
                    continue;
                if (Hoods(roster, crew) >= config.HoodsPerCrew)
                    continue;
                if (!CanSign(view, config) ||
                    weeklyTake < config.GrowthIncomeDays *
                        (view.DailyPayroll + Outfit.Wages.HoodBase))
                    return false;
                if (Filed(view, OrderType.Recruit, crew.Id))
                    continue;
                if (Propose(view, into, HouseIntent.Work(
                    RecruitFor(crew), TierInvest,
                    "the crew is under " + config.HoodsPerCrew + " men")))
                    return true;
            }

            // Then: a new crew, when the ground calls for one and the men are there.
            if (BlocksLed(view) < 2 || !UnderTarget(view, config))
                return false;
            if (WorkingHoods(view) < config.HoodsPerCrew + 2)
                return false;
            if (weeklyTake < config.GrowthIncomeDays *
                    (view.DailyPayroll + Outfit.Wages.LieutenantBase))
                return false;

            var best = BestHood(view, includeDetail: false);
            if (best == null)
                return false;
            return Propose(view, into, HouseIntent.Raise(best.Id, TierInvest,
                "the ground calls for another crew; he gets one"));
        }

        /// <summary>What the doors that pay us are worth a week, over every block the
        /// house can see - the income the growth rule is weighed against.</summary>
        static int WeeklyTake(HouseView view)
        {
            var take = 0;
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var doors = view.Businesses(view.Blocks[b]);
                for (var i = 0; i < doors.Count; i++)
                    if (doors[i].Standing == TerritoryProtectionState.Compliant &&
                        doors[i].Trades && !doors[i].Shut)
                        take += doors[i].WeeklyRate;
            }
            return take;
        }

        /// <summary>
        /// BUY. Only with a week's wages still in the safe after the price. A gun for
        /// every empty hand FIRST, then a car for a crew on foot (L3: a man who cannot
        /// shoot is worth less than a crew that has to walk). A peaceful family still
        /// waits for a quiet spell before it spends; a family that has taken all the
        /// ground it can and is arming for what comes next does not (ruling A12).
        /// </summary>
        static bool Buy(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            if (PhaseOf(view, config) == HousePhase.Land &&
                view.QuietThinks < config.QuietThinks)
                return false;

            var roster = view.Roster;
            var reserve = config.ReserveDays * view.DailyPayroll;

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
                    if (Propose(view, into, HouseIntent.Buy(
                        gun.Kind, gun.DisplayName, gun.Price, man.Id, crew.Id,
                        TierInvest, "a man with empty hands")))
                        return true;
                }
            }

            for (var i = 0; i < roster.Crews.Count; i++)
            {
                var crew = roster.Crews[i];
                if (IsDetail(view, crew) || CrewKit.HasVehicle(roster, crew))
                    continue;
                var car = Cheapest(ArmoryCatalog.Vehicles, config.MaxVehiclePrice);
                if (car.Price <= 0 || view.Safe - car.Price < reserve)
                    return false;
                if (Propose(view, into, HouseIntent.Buy(
                    car.Kind, car.DisplayName, car.Price, crew.LieutenantId, crew.Id,
                    TierInvest, "the crew is walking to work")))
                    return true;
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

        /// <summary>Men on the books with nothing in their hands.</summary>
        static int UnarmedMen(HouseView view)
        {
            var roster = view.Roster;
            var empty = 0;
            for (var i = 0; i < roster.Crews.Count; i++)
            {
                var crew = roster.Crews[i];
                for (var h = 0; h < crew.HoodIds.Count; h++)
                {
                    var man = roster.Find(crew.HoodIds[h]);
                    if (man != null && !man.Gone && !Armed(roster, man.Id))
                        empty++;
                }
            }
            return empty;
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
            Crew smallest = null;
            Crew biggest = null;
            var working = 0;
            for (var i = 0; i < crews.Count; i++)
            {
                // The Don's detail is not a crew to fold or to fold into (S3).
                if (IsDetail(view, crews[i]))
                    continue;
                working++;
                var size = Active(view.Roster, crews[i]);
                if (smallest == null || size < Active(view.Roster, smallest))
                    smallest = crews[i];
                if (biggest == null || size > Active(view.Roster, biggest))
                    biggest = crews[i];
            }
            if (working < 2 || smallest == null || biggest == null ||
                smallest.Id == biggest.Id)
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
                if (Propose(view, into,
                    HouseIntent.MoveToCrew(man.Id, biggest.Id, TierWages, reason)))
                    return true;
            }

            return Propose(view, into,
                HouseIntent.Break(smallest.LieutenantId, TierWages, reason));
        }

        // ------------------------------------------------------------------- tier 3

        /// <summary>
        /// REPLACE THE FALLEN (D9, S4). A house with no working capo makes one: its best
        /// hood by Leadership is promoted (ruling A5, "unapredi kapa"), and only a house
        /// with no hood at all to promote signs one instead - on the Don's detail, the
        /// one paper job that crew is ever given, because signing needs no bodies on
        /// the street. A crew under strength signs a hood if the safe still holds a
        /// week's wages once the new man is on the payroll too.
        /// </summary>
        static bool Replace(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            var roster = view.Roster;
            var crews = roster.Crews;

            if (WorkingCrews(view) == 0)
            {
                var best = BestHood(view, includeDetail: true);
                if (best != null)
                    return Propose(view, into, HouseIntent.Raise(best.Id, TierReplace,
                        "the house has no capo; he gets the crew"));

                var detail = Detail(view);
                if (detail == null || !CanSign(view, config) ||
                    Filed(view, OrderType.Recruit, detail.Id))
                    return false;
                return Propose(view, into, HouseIntent.Work(RecruitFor(detail),
                    TierReplace, "there is nobody left to promote; a man is signed"));
            }

            for (var i = 0; i < crews.Count; i++)
            {
                var crew = crews[i];
                if (IsDetail(view, crew) || !Led(roster, crew))
                    continue;
                if (Hoods(roster, crew) >= config.MinHoods)
                    continue;
                if (!CanSign(view, config))
                    continue;

                // Somebody is already out signing for this crew; a second order would
                // buy the same man twice.
                if (Filed(view, OrderType.Recruit, crew.Id))
                    continue;

                if (Propose(view, into, HouseIntent.Work(RecruitFor(crew), TierReplace,
                    "the crew is under " + config.MinHoods + " men")))
                    return true;
            }
            return false;
        }

        /// <summary>The reserve rule, counting the man we are about to sign.</summary>
        static bool CanSign(HouseView view, HouseMindConfig config)
        {
            var after = view.Safe - EconomyPrices.RecruitSigning;
            var payroll = view.DailyPayroll + Outfit.Wages.HoodBase;
            return after >= config.ReserveDays * payroll;
        }

        static Job RecruitFor(Crew crew) => new Job
        {
            CrewId = crew.Id,
            Type = OrderType.Recruit,
            Men = 1,
            TargetLabel = "a man for the crew",
        };

        /// <summary>
        /// THE LAW (AI-005 P1, ruling A14). Counsel first, then bail - in that order,
        /// because bail without counsel is impossible: a house with a man inside and
        /// no lawyer retains one, and only then posts bail for a capo (with three days
        /// of wages still in the safe) or a hood (with the full reserve). A case with
        /// no bail on it is refused by the gateway once and never asked again (P4).
        /// </summary>
        static bool Law(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            if (view.Cells.Count == 0)
                return false;

            var payroll = view.DailyPayroll;
            var needsCounsel = false;
            for (var i = 0; i < view.Cells.Count && !needsCounsel; i++)
                needsCounsel = view.Cells[i].NeedsCounsel;

            if (needsCounsel && !view.HasCounsel && view.CounselPrice > 0 &&
                view.Safe - view.CounselPrice >= config.ReserveDays * payroll &&
                Propose(view, into, HouseIntent.RetainCounsel(view.CounselPrice,
                    TierReplace, "a man of ours is inside and nobody can get him a hearing")))
                return true;

            // Capos first: a crew with its lieutenant inside is a crew doing nothing.
            for (var pass = 0; pass < 2; pass++)
                for (var i = 0; i < view.Cells.Count; i++)
                {
                    var cell = view.Cells[i];
                    var capo = cell.Rank == Rank.Lieutenant;
                    if ((pass == 0) != capo)
                        continue;
                    if (!cell.Bailable)
                        continue;
                    var floor = capo ? config.MergeBelowDays : config.ReserveDays;
                    if (view.Safe - cell.BailPrice < floor * payroll)
                        continue;
                    if (Propose(view, into, HouseIntent.PostBail(
                        cell.CharacterId, cell.BailPrice, TierReplace,
                        capo ? "the capo comes out on the house's money"
                             : "a man of ours comes out on the house's money")))
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
                        if (Propose(view, into, HouseIntent.MarkDuty(
                            man.Id, Duty.Collector, TierCollect,
                            "somebody has to carry the bag")))
                            break;
                    }
                }

                if (!Answers(roster, crew.LieutenantId, blockId) &&
                    Room(roster, crew.LieutenantId))
                    Propose(view, into, HouseIntent.GiveBlock(crew.LieutenantId, blockId,
                        TierCollect, "he answers for the doors that pay us there"));
            }
        }

        // ------------------------------------------------------------------- tier 7

        /// <summary>
        /// EXPAND (Z1/Z2, rulings A6/A7). On a block the family stands on, the WHOLE
        /// block is walked in one order - every door nobody holds, the same SHAKE DOWN
        /// THE BLOCK the player has - and not one door per think. A block is not walked
        /// twice inside a day (A21) nor while the law is watching it (C1). Only when
        /// there is no door left on our ground that may still be asked does the family
        /// look at a neighbour; a contested street still keeps it at home.
        /// </summary>
        static bool Expand(HouseView view, HouseMindConfig config, List<HouseIntent> into)
        {
            // Walking onto a street and asking at a door cost nothing, so the reserve
            // rule (D9) does not gate them - it gates the SIGNING, and a family too poor
            // to expand is put back together by tier 2 first.

            // First: is there a block to walk on ground we already stand on?
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var blockId = view.Blocks[b];
                if (view.OurPresence(blockId) < config.DemandPresence)
                    continue;
                if (!AnyAskable(view, blockId))
                    continue;
                var walked = view.LastWalked(blockId);
                if (walked >= 0.0 && view.GameHour - walked < config.DemandCooldownHours)
                    continue;
                if (view.PoliceAttention(blockId) > config.WalkAttentionCap)
                    continue;
                var crew = CrewOn(view, blockId);
                if (crew == null)
                    continue;
                if (Propose(view, into, HouseIntent.Block(
                    HouseOrder.ShakeDownBlock, crew.Id, blockId, TierExpand,
                    "every door on that street that pays nobody gets asked")))
                    return true;
            }

            if (!NothingLeftToAsk(view, config))
                return false;

            // Then: which neighbour is worth walking onto.
            var best = BestNeighbour(view, config);
            if (!best.IsValid)
                return false;

            var free = FreeForNewGround(view, config);
            if (free == null)
                return false;

            return Propose(view, into, HouseIntent.Block(
                HouseOrder.OperateInBlock, free.Id, best, TierExpand,
                "there is money on that street and nobody holding it"));
        }

        /// <summary>
        /// THE BORDER (AI-007 R1, ruling A13). The houses that lead a block next to
        /// one we lead, and over how many of our blocks each of them does - read only
        /// once the house has nowhere open left to take, which is the user's order of
        /// things: take the free city first, and only then resent the neighbour. The
        /// runtime files it as grievance once a day (A18); the mind only reads.
        /// </summary>
        public static void Borders(HouseView view, HouseMindConfig config,
            List<(TerritoryGangId neighbour, int blocks)> into)
        {
            into?.Clear();
            if (into == null || view?.Roster == null)
                return;
            config = config ?? HouseMindConfig.Default;
            if (PhaseOf(view, config) == HousePhase.Land)
                return;

            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var blockId = view.Blocks[b];
                if (view.Leader(blockId) != view.House)
                    continue;
                var neighbours = view.Neighbours(blockId);
                for (var n = 0; n < neighbours.Count; n++)
                {
                    var them = view.Leader(neighbours[n]);
                    if (!them.IsValid || them == view.House)
                        continue;
                    var at = -1;
                    for (var i = 0; i < into.Count && at < 0; i++)
                        if (into[i].neighbour == them)
                            at = i;
                    if (at < 0)
                        into.Add((them, 1));
                    else
                        into[at] = (them, into[at].blocks + 1);
                }
            }
        }

        /// <summary>
        /// ONE STREET AT A TIME (the user's ruling of 2026-09-04, on the AI-008 table).
        ///
        /// A crew sent onto a street is left there until the family has actually got
        /// something out of it. The measurement is what asked for this: at a think
        /// every game hour a house held a THIRD LESS ground by day fourteen than one
        /// thinking every four, because the mind kept re-posting the same crew to
        /// whichever neighbour scored best at that moment and no street ever matured -
        /// a man changing jobs every hour and finishing none of them. The cadence is
        /// the user's one hour; this is the rule that makes an hour worth having.
        ///
        /// A street is DONE WITH when the family leads it, or when there is no door
        /// left on it worth asking. Until then the crew standing on it is not a
        /// candidate for opening new ground; another free crew still is, so a house
        /// with men to spare goes on growing.
        /// </summary>
        static Crew FreeForNewGround(HouseView view, HouseMindConfig config)
        {
            var crews = view.Roster.Crews;
            for (var i = 0; i < crews.Count; i++)
            {
                var crew = crews[i];
                if (!Candidate(view, crew))
                    continue;
                var standing = view.CrewBlock(crew.Id);
                if (standing.IsValid && !WorkedOut(view, config, standing))
                    continue;
                return crew;
            }
            return null;
        }

        /// <summary>
        /// A STREET A CREW MAY LEAVE: the family leads it, or there is nothing left on
        /// it to ask.
        ///
        /// Both halves were measured. Without the rule at all, a mind at one game hour
        /// re-posted the same crew to whichever neighbour scored best that moment and
        /// no street ever matured. Pinning the crew HARDER - until the street is
        /// actually ours, with a day's grace - is worse again: the men sit on ground
        /// they cannot take and the family's doors fall by half. Leaving when the
        /// asking is done is the line that holds.
        /// </summary>
        static bool WorkedOut(HouseView view, HouseMindConfig config,
            TerritoryBlockId blockId) =>
            view.Leader(blockId) == view.House || !AnyAskable(view, blockId);

        /// <summary>The open neighbour with the best score, or invalid when none
        /// clears zero (Z3: if none ever does, the numbers go to the user).</summary>
        static TerritoryBlockId BestNeighbour(HouseView view, HouseMindConfig config)
        {
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
            return best;
        }

        /// <summary>Every neighbour a house could walk onto and what the mind scores
        /// it at, for the probe (Z3 asks that the figures be printed, not tuned).
        /// </summary>
        public static void NeighbourScores(HouseView view, HouseMindConfig config,
            List<(TerritoryBlockId block, int score, bool open)> into)
        {
            into?.Clear();
            if (into == null || view?.Roster == null)
                return;
            config = config ?? HouseMindConfig.Default;
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var neighbours = view.Neighbours(view.Blocks[b]);
                for (var n = 0; n < neighbours.Count; n++)
                {
                    var blockId = neighbours[n];
                    var known = false;
                    for (var i = 0; i < into.Count && !known; i++)
                        known = into[i].block == blockId;
                    for (var i = 0; i < view.Blocks.Count && !known; i++)
                        known = view.Blocks[i] == blockId;
                    if (known)
                        continue;
                    into.Add((blockId, Score(view, config, blockId, 1), Open(view, blockId)));
                }
            }
        }

        /// <summary>A door the walk would put the question to: it pays nobody (or is
        /// merely on our books without paying yet), it trades, it is open, it has not
        /// said no, and it has not been left to make up its mind. What the whole-block
        /// order is sent for, and what "nothing left to ask" counts.</summary>
        static bool Askable(HouseView view, in HouseDoor door)
        {
            // NEVER A DOOR ANOTHER HOUSE PROTECTS. Taking one off a family is a
            // decision about that family, and the feud makes it.
            if (!door.Unprotected && door.Protector != view.House)
                return false;
            if (door.Shut || !door.Trades || door.Tenure == DoorTenure.Ours)
                return false;
            if (door.Standing != TerritoryProtectionState.Unaffiliated &&
                door.Standing != TerritoryProtectionState.Approached)
                return false;
            // NEVER TWO DEMANDS IN A ROW. A man who has said no is answered by the
            // ladder, not by the same question again.
            if (Refused(view, door.BusinessId))
                return false;
            return Offers(door, TerritoryRacketIntent.Demand);
        }

        static bool AnyAskable(HouseView view, TerritoryBlockId blockId)
        {
            var doors = view.Businesses(blockId);
            for (var i = 0; i < doors.Count; i++)
                if (Askable(view, doors[i]))
                    return true;
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

        /// <summary>
        /// Z2 (ruling A7). There is no door left on the ground we lead or stand on that
        /// may still be asked, and no street we lead is contested. The old D17 test -
        /// every led block Controlled with half its doors paying - kept a family at
        /// home for ever behind one wavering shopkeeper.
        /// </summary>
        static bool NothingLeftToAsk(HouseView view, HouseMindConfig config)
        {
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var blockId = view.Blocks[b];
                var ours = view.Leader(blockId) == view.House;
                if (ours && view.ControlState(blockId) == TerritoryControlState.Contested)
                    return false;
                if (!ours && view.OurPresence(blockId) < config.DemandPresence)
                    continue;
                if (AnyAskable(view, blockId))
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

        /// <summary>He has already said no to us, or would not say, and does not pay
        /// us. The ladder has him; the ask does not.</summary>
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

        // ------------------------------------------------------------------- crews

        /// <summary>THE DON'S OWN DETAIL: the crew whose lieutenant is the Boss. It is
        /// never a candidate for anything (S3/S3b, ruling A4: "Don nek sedi u kuću") -
        /// the street's own defence of the front is the whole of "mostly".</summary>
        static bool IsDetail(HouseView view, Crew crew) =>
            crew != null && crew.LieutenantId == view.Roster.BossId;

        static Crew Detail(HouseView view)
        {
            var crews = view.Roster.Crews;
            for (var i = 0; i < crews.Count; i++)
                if (IsDetail(view, crews[i]))
                    return crews[i];
            return null;
        }

        /// <summary>A crew with a lieutenant still on the books and on his feet.</summary>
        static bool Led(Roster roster, Crew crew)
        {
            var lieutenant = roster.Find(crew.LieutenantId);
            return lieutenant != null && !lieutenant.Gone &&
                   lieutenant.Status == CharacterStatus.Active;
        }

        /// <summary>A crew with a wanted man in it is not sent to a door (P3): a man
        /// the city is looking for stays inside, the way the player's hidden days work.
        /// </summary>
        static bool Wanted(Roster roster, Crew crew)
        {
            var lieutenant = roster.Find(crew.LieutenantId);
            if (lieutenant != null && !lieutenant.Gone && lieutenant.WantedLevel > 0)
                return true;
            for (var i = 0; i < crew.HoodIds.Count; i++)
            {
                var man = roster.Find(crew.HoodIds[i]);
                if (man != null && !man.Gone && man.Status == CharacterStatus.Active &&
                    man.WantedLevel > 0)
                    return true;
            }
            return false;
        }

        /// <summary>Crews that can be given work: led, not the Don's, nobody wanted.
        /// </summary>
        static int WorkingCrews(HouseView view)
        {
            var count = 0;
            var crews = view.Roster.Crews;
            for (var i = 0; i < crews.Count; i++)
                if (!IsDetail(view, crews[i]) && Led(view.Roster, crews[i]))
                    count++;
            return count;
        }

        static int WorkingHoods(HouseView view)
        {
            var count = 0;
            var crews = view.Roster.Crews;
            for (var i = 0; i < crews.Count; i++)
                if (!IsDetail(view, crews[i]) && Led(view.Roster, crews[i]))
                    count += Hoods(view.Roster, crews[i]);
            return count;
        }

        static int BlocksLed(HouseView view)
        {
            var led = 0;
            for (var b = 0; b < view.Blocks.Count; b++)
                if (view.Leader(view.Blocks[b]) == view.House)
                    led++;
            return led;
        }

        /// <summary>L1: fewer working crews than the ground calls for, or a working
        /// crew under strength.</summary>
        static bool UnderTarget(HouseView view, HouseMindConfig config)
        {
            var wanted = BlocksLed(view) * config.CrewsPerBlock + config.SpareCrews;
            if (WorkingCrews(view) < wanted)
                return true;
            var crews = view.Roster.Crews;
            for (var i = 0; i < crews.Count; i++)
                if (!IsDetail(view, crews[i]) && Led(view.Roster, crews[i]) &&
                    Hoods(view.Roster, crews[i]) < config.HoodsPerCrew)
                    return true;
            return false;
        }

        /// <summary>The best hood on his feet by Leadership - the man a house makes a
        /// capo of. The Don's own guards are only in the running when there is nobody
        /// else at all (S4).</summary>
        static Character BestHood(HouseView view, bool includeDetail)
        {
            var roster = view.Roster;
            Character best = null;
            var bestLead = -1;
            for (var c = 0; c < roster.Crews.Count; c++)
            {
                var crew = roster.Crews[c];
                if (!includeDetail && IsDetail(view, crew))
                    continue;
                for (var h = 0; h < crew.HoodIds.Count; h++)
                {
                    var man = roster.Find(crew.HoodIds[h]);
                    if (man == null || man.Gone || man.Rank != Rank.Hood ||
                        man.Status != CharacterStatus.Active ||
                        man.Specialty != Specialty.None)
                        continue;
                    var lead = man.GetHalfSteps(CharacterAttribute.Leadership);
                    if (lead <= bestLead)
                        continue;
                    bestLead = lead;
                    best = man;
                }
            }
            return best;
        }

        /// <summary>The crew that answers for a block; else a working crew with no
        /// ground of its own on the paper; else the first working crew. Never the
        /// Don's detail (S3b): it used to fall back to crews[0], which handed the Don a
        /// block on the paper and marked a bag man inside his own guard.</summary>
        static Crew CrewFor(HouseView view, TerritoryBlockId blockId)
        {
            var roster = view.Roster;
            var paper = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < paper.Count; i++)
            {
                if (paper[i].BlockId != blockId)
                    continue;
                for (var c = 0; c < roster.Crews.Count; c++)
                    if (roster.Crews[c].LieutenantId == paper[i].LeaderId &&
                        !IsDetail(view, roster.Crews[c]))
                        return roster.Crews[c];
            }

            Crew first = null;
            for (var c = 0; c < roster.Crews.Count; c++)
            {
                var crew = roster.Crews[c];
                if (IsDetail(view, crew) || !Led(roster, crew))
                    continue;
                first ??= crew;
                if (!AnswersForAnything(roster, crew.LieutenantId))
                    return crew;
            }
            return first;
        }

        /// <summary>
        /// Whose crew this street is. The one whose lieutenant answers for it if there is
        /// one, and otherwise the first crew with men on their feet and no current job -
        /// the mind has no window onto the street, so "free" is what the books can say.
        /// Never the Don's detail, never a crew with a wanted man in it.
        /// </summary>
        static Crew CrewOn(HouseView view, TerritoryBlockId blockId)
        {
            var roster = view.Roster;
            if (blockId.IsValid)
                for (var i = 0; i < roster.Crews.Count; i++)
                {
                    var crew = roster.Crews[i];
                    if (Candidate(view, crew) && Answers(roster, crew.LieutenantId, blockId))
                        return crew;
                }

            for (var i = 0; i < roster.Crews.Count; i++)
                if (Candidate(view, roster.Crews[i]))
                    return roster.Crews[i];
            return null;
        }

        static bool Candidate(HouseView view, Crew crew) =>
            !IsDetail(view, crew) && Free(view, crew) &&
            Active(view.Roster, crew) >= 2 && !Wanted(view.Roster, crew);

        /// <summary>No job on the book AND no round on the street (S7). A crew out on
        /// a walk looked idle to every tier and had its walk torn down each think.
        /// </summary>
        static bool Free(HouseView view, Crew crew) =>
            crew != null && (view.Book == null || view.Book.CurrentFor(crew.Id) == null) &&
            !view.RoundOut(crew.Id);

        static bool Answers(Roster roster, int leaderId, TerritoryBlockId blockId)
        {
            var paper = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < paper.Count; i++)
                if (paper[i].LeaderId == leaderId && paper[i].BlockId == blockId)
                    return true;
            return false;
        }

        static bool AnswersForAnything(Roster roster, int leaderId)
        {
            var paper = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < paper.Count; i++)
                if (paper[i].LeaderId == leaderId && paper[i].BlockId.IsValid)
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
            var man = roster.Find(crew.BagId);
            return man != null && !man.Gone && man.Duty == Duty.Collector;
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

        /// <summary>Whether anybody of ours is already filed to sit on this door (S1:
        /// a guard is counted by the door, not by the crew).</summary>
        static bool Guarded(HouseView view, TerritoryBusinessId door)
        {
            if (view.Book == null || !door.IsValid)
                return false;
            var jobs = view.Book.Jobs;
            for (var i = 0; i < jobs.Count; i++)
                if (jobs[i].Type == OrderType.Guard &&
                    jobs[i].Stage != JobStage.Finished &&
                    jobs[i].TargetBusinessId == door.Value)
                    return true;
            return false;
        }

        /// <summary>Whether a watch of ours is filed on any door of this block.</summary>
        static bool BlockGuarded(HouseView view, TerritoryBlockId blockId)
        {
            if (view.Book == null || !blockId.IsValid)
                return false;
            var jobs = view.Book.Jobs;
            for (var i = 0; i < jobs.Count; i++)
                if (jobs[i].Type == OrderType.Guard &&
                    jobs[i].Stage != JobStage.Finished &&
                    jobs[i].TargetLabel == blockId.Value)
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
