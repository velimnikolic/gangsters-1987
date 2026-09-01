using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    /// <summary>
    /// The campaign, running. Everything that happens to the outfit as time passes -
    /// crews travelling, jobs finishing, money moving, men improving, wages falling
    /// due - lives here, and it lives here rather than in OutfitDirector for the reason
    /// every other class in this namespace is pure: a game whose rules are locked inside
    /// a MonoBehaviour can only be checked by watching it, and watching a realtime game
    /// is the slowest and least certain way to find out whether its books add up.
    ///
    /// OutfitDirector keeps only the edges - reading the clock, knowing where the
    /// headquarters stands, logging, and bumping the version the ledger repaints on.
    /// The headless suite drives this class directly: a scripted month of days, and the
    /// safe, the practice and the record asserted to the dollar and the half-step.
    ///
    /// Time enters through exactly two doors, and their order matters: hours are worked
    /// first (<see cref="AdvanceHours"/>), then the day turns (<see cref="DayTick"/>).
    /// A job that finished at ten to midnight is in the record before the midnight pass
    /// counts its practice.
    /// </summary>
    public sealed class CampaignRunner
    {
        /// <summary>Records kept. A rolling window rather than the whole campaign: the
        /// record is what the player just did, and a book that grows without limit is a
        /// leak with a nice name.</summary>
        public const int RecordsKept = 40;

        public readonly Campaign Campaign = new Campaign();
        public readonly Accounts Accounts = new Accounts();
        public readonly GangRelations Relations = new GangRelations();
        public readonly OrderBook Book = new OrderBook();

        /// <summary>What the outfit kicks up to the houses above it - re-priced off the
        /// live city every midnight and collected when it falls due.</summary>
        public readonly Tribute Tribute = new Tribute();

        /// <summary>What came of the last few days' work, most recent first.</summary>
        public readonly List<OrderRecord> Records = new List<OrderRecord>();

        /// <summary>Men who got better overnight - cleared and refilled at each day
        /// tick, so a page shows today's rises and not the campaign's.</summary>
        public readonly List<Improvement> Rises = new List<Improvement>();

        /// <summary>Men the year took something off overnight - the mirror of
        /// <see cref="Rises"/>, on the same clear-and-refill cycle, so a page shows
        /// today's losses and not the campaign's.</summary>
        public readonly List<Decline> Declines = new List<Decline>();

        /// <summary>What the men's own characters did that nobody ordered - who froze,
        /// who ran, whose temper turned a collection into a shooting. Written as jobs
        /// finish and cleared at the day tick, so the page carries today's.</summary>
        public readonly List<Incident> Incidents = new List<Incident>();

        /// <summary>Every trait that moved today and the reason it moved - the same
        /// clear-and-refill cycle, and the record the paper's rumour lines read.</summary>
        public readonly List<PersonalityChange> CharacterChanges =
            new List<PersonalityChange>();

        /// <summary>
        /// What the morning paper prints: everything the outfit's own men did between
        /// one midnight and the next. A paper reports YESTERDAY - which is also what
        /// keeps the page still, since a sheet that repainted every time a crew came
        /// home would be a sheet nobody could read.
        /// </summary>
        public readonly List<Incident> LastNight = new List<Incident>();

        /// <summary>The last few weeks of incidents, newest last - the stream the
        /// notability score reads. Scored from the FIELDS; nothing downstream ever
        /// parses the sentence back into facts.</summary>
        public readonly List<Incident> IncidentBook = new List<Incident>();

        /// <summary>How far back the incident book reaches. A rolling window rather
        /// than the whole campaign, for the reason <see cref="RecordsKept"/> is one.</summary>
        public const int IncidentsKept = 120;

        /// <summary>Scratch for the day's defections - the roster is walked to find
        /// them and then walked again to strike them off, because a lieutenant going
        /// over takes his crew with him as he goes.</summary>
        static readonly List<Character> DefectedToday = new List<Character>();

        /// <summary>Police attention the outfit has drawn. Nothing spends it yet; the
        /// jobs pay into it so the police layer inherits a history when it lands.</summary>
        public int Heat;

        /// <summary>The city seed the rolls are dealt from.</summary>
        public int Seed;

        /// <summary>Metres from headquarters to a job's door. A function rather than a
        /// stored number because only the scene knows where anything is - and because
        /// it is the ONLY thing this class cannot work out for itself, which is what
        /// keeps it free of UnityEngine. A test supplies its own.</summary>
        public System.Func<Job, float> DistanceOf;

        /// <summary>Fills the list with who holds what across the city. Same bargain as
        /// <see cref="DistanceOf"/>: only the scene can walk the markers, so the scene
        /// hands the reading in and this class stays pure. Null means an empty city,
        /// which prices every tribute claim out of existence rather than guessing one.</summary>
        public System.Action<List<Turf.Holding>> HoldingsOf;

        /// <summary>Raised when the roster itself moved - a man into a hospital bed, a
        /// new name onto the books - so the caller can bump the personnel version the
        /// ledger and the street both re-deal on.</summary>
        public System.Action RosterMoved;

        /// <summary>Raised as a job finishes, after its money and record are booked.
        /// The scene applies what the job did to the CITY - a bought deed, a shop put
        /// in fear - which this class has no hands to touch.</summary>
        public System.Action<Job, OrderOutcome> JobResolved;

        readonly List<int> scratchMen = new List<int>();
        readonly List<Turf.Holding> scratchHoldings = new List<Turf.Holding>();
        readonly List<int> scratchSoured = new List<int>();

        // ---------------------------------------------------------------- the hours

        /// <summary>
        /// Works every crew for this many game-hours. Returns true when something moved
        /// that a page would want to show - a crew set off, arrived, or came back with
        /// an answer.
        /// </summary>
        /// <summary>
        /// The Don is dead and the campaign is over. Set once, at the first tick that
        /// observes it, and never cleared - there is nothing after this.
        /// </summary>
        public bool Fallen { get; private set; }

        /// <summary>The day the outfit ended; 0 while it has not.</summary>
        public int FallenOnDay { get; private set; }

        /// <summary>Raised once, when the campaign ends. The scene edge presents it;
        /// the sim never touches a screen.</summary>
        public event System.Action BossFell;

        /// <summary>
        /// Has the outfit lost its head? Death reaches the roster through exactly one
        /// path (RosterOps.Kill), so this is the only place that needs to notice, and
        /// it notices before anything else in the tick moves.
        ///
        /// It is checked at both doors time comes through, so nothing resolves after
        /// the Don goes down - not a job finishing at ten to midnight, not the
        /// midnight pass itself.
        /// </summary>
        bool CampaignOver(Roster roster)
        {
            if (Fallen)
                return true;
            if (roster == null)
                return false;

            var boss = roster.FindBoss();
            if (boss == null || boss.Status != CharacterStatus.Dead)
                return false;

            Fallen = true;
            FallenOnDay = Campaign.Day;
            BossFell?.Invoke();
            return true;
        }

        public bool AdvanceHours(Roster roster, float hours)
        {
            if (roster == null || hours <= 0f || CampaignOver(roster))
                return false;

            var moved = false;
            for (var i = 0; i < roster.Crews.Count; i++)
                moved |= WorkCrew(roster, roster.Crews[i], hours);

            if (moved)
                Book.DropFinished();
            return moved;
        }

        /// <summary>One crew's hour. A crew works the first live job in its book and
        /// nothing else - the rest of the queue is work it has not got to.</summary>
        bool WorkCrew(Roster roster, Crew crew, float hours)
        {
            var job = Book.CurrentFor(crew.Id);
            if (job == null)
                return false;

            var spec = OrderTable.SpecOf(job.Type);
            var moved = false;

            if (job.Stage == JobStage.Queued)
            {
                if (!StartJob(roster, crew, job, spec))
                    return true;
                moved = true;
            }

            if (job.Stage == JobStage.Travelling)
            {
                job.TravelHoursLeft -= hours;
                if (job.TravelHoursLeft > 0f)
                    return moved;

                // The hours left over from the journey are hours at the door: a step
                // that swallowed the last of the travel must not throw its remainder
                // away, or a fast clock would lose work on every arrival.
                hours = -job.TravelHoursLeft;
                job.TravelHoursLeft = 0f;
                job.Stage = JobStage.Working;
                moved = true;
            }

            if (job.Stage != JobStage.Working ||
                spec.Resolution == JobResolution.Standing)
                return moved;

            job.WorkHoursLeft -= hours;
            if (job.WorkHoursLeft > 0f)
                return moved;

            Finish(roster, crew, job, spec);
            return true;
        }

        /// <summary>Puts a crew on the road. False when there is nobody left to send -
        /// the job is called off rather than left blocking the book forever.</summary>
        bool StartJob(Roster roster, Crew crew, Job job, in OrderSpec spec)
        {
            CrewKit.MenOnJob(roster, crew, job.Men, scratchMen);
            if (scratchMen.Count == 0)
            {
                Record(roster, job, OrderOutcome.CalledOff, 0, 0);
                job.Stage = JobStage.Finished;
                return false;
            }

            // A job books the men who can actually go: the dead and the laid-up are not
            // on it, and the record must not claim they were.
            job.Men = scratchMen.Count;
            job.BookDepth = Book.LiveCount(crew.Id) - 1;
            job.TravelHoursLeft = OrderMath.TravelHours(
                DistanceOf != null ? DistanceOf(job) : 0f,
                CrewKit.HasVehicle(roster, crew),
                CrewKit.BestAt(roster, crew, CharacterAttribute.Driving),
                CrewKit.MachineTopOf(roster, crew));
            job.WorkHoursLeft = OrderMath.WorkHours(spec, job.TargetCount, job.Men);
            job.Stage = JobStage.Travelling;
            return true;
        }

        void Finish(Roster roster, Crew crew, Job job, in OrderSpec spec)
        {
            var rng = new System.Random(OrderResolution.Mix(
                Seed + Generation.SeedOffsets.Orders, job.IssuedDay, job.Id));

            var incidentsBefore = Incidents.Count;
            var result = OrderResolution.Resolve(spec, job, roster, crew, rng,
                job.StreetOutcome, Incidents);

            BookMoney(spec, result.Payout, result.Cost);
            Heat += result.Heat;

            if (result.CasualtyId >= 0)
                RosterOps.Hospitalize(roster, result.CasualtyId,
                    Campaign.Day + OrderResolution.MisfireDays,
                    OrderResolution.InjuryNote(rng));

            // The law's own record of what the men did. Only the escalations go on it:
            // a rap sheet is his file WITH THE CITY, and a man who went to pieces on a
            // corner has not been charged with anything.
            for (var i = incidentsBefore; i < Incidents.Count; i++)
            {
                var incident = Incidents[i];
                if (incident.Kind != IncidentKind.Escalated)
                    continue;
                var man = roster.Find(incident.CharacterId);
                if (man == null)
                    continue;
                RapSheet.Add(man,
                    News.NewsDate.FromClockDay(incident.Day - 1).Short(),
                    "Discharging a firearm",
                    "Under investigation");
            }

            // Either of these MOVED THE ROSTER - a man into a bed, a new name onto the
            // books, or a man who ran off them - and a roster that moves without the
            // personnel version moving leaves the ledger on a stale page and the street
            // short a body it never dealt.
            var ranOff = false;
            for (var i = incidentsBefore; i < Incidents.Count && !ranOff; i++)
                ranOff = Incidents[i].Kind == IncidentKind.Fled;
            if (result.CasualtyId >= 0 || result.RecruitedId >= 0 || ranOff)
                RosterMoved?.Invoke();

            // What the job teaches. A deviation (a freeze, a runner, work done louder
            // than ordered) turns a completed job's lesson into the half-pay Partial -
            // the outcome the XP table defined for exactly this and nothing ever fed.
            var lesson = result.Outcome == OrderOutcome.Completed
                ? XpOutcome.Completed
                : XpOutcome.Failed;
            if (lesson == XpOutcome.Completed)
                for (var i = incidentsBefore; i < Incidents.Count; i++)
                    if (Incidents[i].Kind == IncidentKind.Froze ||
                        Incidents[i].Kind == IncidentKind.Fled ||
                        Incidents[i].Kind == IncidentKind.Deviated)
                    {
                        lesson = XpOutcome.Partial;
                        break;
                    }

            // A violence job the STREET answered already taught its men out there -
            // CrewSkill pays the shots that landed, capped per day. Paying the book's
            // practice on top of that was the double award XP-003 forbids.
            if (job.StreetOutcome == null)
                OrderResolution.AwardPractice(spec, roster, crew, job.Men, lesson);

            Record(roster, job, result.Outcome, result.Payout - result.Cost, result.Heat);
            job.Stage = JobStage.Finished;

            // The world's turn. The campaign owns money, men and the record; what a
            // finished job DID to the city - a deed changing hands, a shop frightened -
            // is the scene's business, and this is the seam it hears it through.
            JobResolved?.Invoke(job, result.Outcome);
        }

        // ----------------------------------------------------------------- the money

        /// <summary>
        /// Books what the job did to the money. Payout and cost are kept apart on the
        /// sheet even when they cancel out: premises that cost 2,500 and returned
        /// nothing are not the same line as a quiet week, and the Finances page is
        /// specified to show where the money went, not merely how much is left.
        /// </summary>
        void BookMoney(in OrderSpec spec, int payout, int cost)
        {
            Accounts.Safe += payout - cost;

            var sheet = Accounts.Current;
            if (sheet == null)
                return;

            if (payout > 0)
            {
                if (spec.Category == OrderCategory.Business)
                    sheet.LegalIncome += payout;
                else
                    sheet.IllegalIncome += payout;
            }

            if (cost <= 0)
                return;
            if (spec.Category == OrderCategory.Influence)
                sheet.Bribes += cost;
            else if (spec.Category == OrderCategory.Business)
                sheet.Purchases += cost;
            else
                sheet.OtherCosts += cost;
        }

        void Record(Roster roster, Job job, OrderOutcome outcome, int money, int heat)
        {
            var crew = roster?.FindCrew(job.CrewId);
            var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;

            Records.Insert(0, new OrderRecord
            {
                Day = Campaign.Day,
                Lieutenant = lieutenant != null ? lieutenant.FullName : "?",
                Type = job.Type,
                TargetSummary = job.BlockTargets.Count > 0
                    ? job.BlockTargets.Count + " blocks"
                    : job.TargetLabel,
                Men = job.Men,
                Outcome = outcome,
                Money = money,
                Heat = heat,
            });

            while (Records.Count > RecordsKept)
                Records.RemoveAt(Records.Count - 1);
        }

        // ------------------------------------------------------------------- the day

        /// <summary>
        /// Midnight, and the only place the books turn: a standing watch is paid its
        /// practice and its takings, practice becomes stars, the laid-up who are due
        /// stand up, stances take effect, and the men are paid - EVERY day, because a
        /// day is the only period the outfit keeps.
        ///
        /// Everything that could surprise a player by happening mid-read happens HERE,
        /// once, rather than scattered through the frame where a wage bill could jump
        /// while the Finances page was open.
        /// </summary>
        /// <returns>Wages paid; 0 on the campaign's first day, which settles nothing.</returns>
        public int DayTick(Roster roster)
        {
            // Before the day even turns: a campaign with no Boss does not have another
            // morning in it.
            if (CampaignOver(roster))
                return 0;

            Campaign.Day++;

            // Last night's page is set before the new day clears the desk, and the
            // book keeps what the score will want to read next week.
            LastNight.Clear();
            LastNight.AddRange(Incidents);
            IncidentBook.AddRange(Incidents);
            if (IncidentBook.Count > IncidentsKept)
                IncidentBook.RemoveRange(0, IncidentBook.Count - IncidentsKept);

            Rises.Clear();
            Declines.Clear();
            Incidents.Clear();
            CharacterChanges.Clear();
            if (roster != null)
            {
                // The calendar, written through before anything reads it: a man taken
                // on today is dealt a date of birth in THIS year.
                roster.Year = Campaign.Year;
                roster.Day = Campaign.Day;

                StandingDay(roster);
                // The command drip BEFORE the conversion, so a day spent holding a
                // crew can be the day that buys the half-step. Banked after it, every
                // command day would be worth one day less than it was.
                CommandDrip.Tick(roster);
                Bodyguards.DayOnDuty(roster);
                Practice.Convert(roster, Rises);
                // What the day gave and what it took, in one pass and in that order,
                // so a man who earned a half-step this morning and lost one to his
                // birthday tonight reads as both and not as neither.
                Aging.Tick(roster, Campaign.Year,
                    (Campaign.Day - 1) % Campaign.DaysPerYear, Declines);

                // A birthday that took something off a man is news too. ONE line per
                // man, not one per trade: "losing his hands, his eye and his nerve" is
                // three ways of saying he turned forty-seven.
                var last = -1;
                for (var i = 0; i < Declines.Count; i++)
                {
                    var loss = Declines[i];
                    if (loss.CharacterId == last)
                        continue;
                    last = loss.CharacterId;
                    Incidents.Add(new Incident(loss.CharacterId, loss.Name,
                        IncidentKind.SlowingDown, Campaign.Day, "", 0,
                        IncidentText.SlowingLine(loss.Name, loss.Age,
                            UI.LedgerText.AttributeLabel(loss.Attribute).ToLowerInvariant())));
                }

                // What the underpaid are doing about it, and what the men think of the
                // one man each of them actually answers to. Both read against the live
                // roster rather than anything stored, so a raise, a promotion or a
                // transfer lands the same midnight it happens.
                var branch = new OrganizationQuery(roster);
                for (var i = 0; i < roster.Members.Count; i++)
                {
                    var member = roster.Members[i];
                    GreedLadder.Tick(member, Wages.WageFor(member),
                        Wages.WorthOf(member), Campaign.Day, Incidents, CharacterChanges);

                    var hasSuperior = branch.TryGetCommandParent(member.Id, out var above);
                    var crowded = hasSuperior &&
                                  branch.CapacityOf(above.Id).IsOverCapacity;
                    Loyalty.Drift(member, hasSuperior, crowded, Wages.PayGap(member),
                        Campaign.Day, Campaign.Day - member.RankSince,
                        CharacterChanges, Incidents);
                }

                // And then the men whose loyalty has run out entirely. Walked over a
                // copy, because a lieutenant going over takes his crew off the books
                // as he goes.
                DefectedToday.Clear();
                for (var i = 0; i < roster.Members.Count; i++)
                    if (roster.Members[i].Rank == Rank.Lieutenant &&
                        !roster.Members[i].Gone &&
                        roster.Members[i].Loyalty <= Defection.BreakingPoint)
                        DefectedToday.Add(roster.Members[i]);
                for (var i = 0; i < DefectedToday.Count; i++)
                {
                    var report = Defection.Tick(roster, DefectedToday[i], Campaign.Day,
                        Incidents);
                    if (report.Happened)
                        RosterMoved?.Invoke();
                }

                var back = RosterOps.Discharge(roster, Campaign.Day);
                if (back > 0 || Rises.Count > 0 || Declines.Count > 0)
                    RosterMoved?.Invoke();
            }

            var paid = Campaign.Settles(Campaign.Day) ? TurnTheBooks(roster) : 0;
            CollectTribute();
            Relations.ApplyPending();
            return paid;
        }

        /// <summary>
        /// A day of a standing job: its practice, and its takings. A watch is never
        /// FINISHED, so the day tick is the only place either can be paid - a standing
        /// job waiting for a resolution would stand forever and earn nothing, which is
        /// exactly what a business the outfit is running would not do.
        ///
        /// Paid per crew, not per queued job: only the watch a crew is actually
        /// standing counts, and a crew stands one at a time.
        /// </summary>
        void StandingDay(Roster roster)
        {
            for (var i = 0; i < roster.Crews.Count; i++)
            {
                var crew = roster.Crews[i];
                var job = Book.CurrentFor(crew.Id);
                if (job == null || job.Stage != JobStage.Working)
                    continue;

                var spec = OrderTable.SpecOf(job.Type);
                if (spec.Resolution != JobResolution.Standing)
                    continue;

                job.DaysStood++;
                // A day of a standing watch is a day's work done, not a fraction of a
                // job: the watch is never FINISHED, so the day is the only piece of it
                // there is.
                OrderResolution.AwardPractice(spec, roster, crew, job.Men,
                    XpOutcome.Completed);

                if (spec.Payout <= 0)
                    continue;
                // The per-type worth rides the job (a casino is not a barber): dropping
                // it here was what made every RunBusiness pay the flat book figure.
                BookMoney(spec, OrderResolution.PayoutFor(spec, job.TargetCount,
                    CrewKit.BestAt(roster, crew, spec.PrimaryAttribute),
                    job.TargetWorth), 0);
                Heat += OrderResolution.HeatFor(spec, job.TargetCount,
                    CrewKit.BestAt(roster, crew, CharacterAttribute.Stealth));
            }
        }

        /// <summary>Payday and a fresh sheet, every midnight. Pay falls due as it falls
        /// due: there is no envelope and no boundary to wait for, and the jailed and the
        /// hurt draw their day the same as the men who worked it. The day closed because
        /// the clock passed midnight, not because anybody pressed anything.</summary>
        public int TurnTheBooks(Roster roster)
        {
            var paid = 0;
            var sheet = Accounts.Current;
            if (sheet != null && !sheet.Closed)
            {
                paid = Wages.DailyPayroll(roster);
                sheet.WagesPaid = paid;
                Accounts.Safe -= paid;
                Accounts.RiskyMoney += sheet.IllegalIncome;
                sheet.Closed = true;
            }

            Accounts.Open(Campaign.Day);
            return paid;
        }

        /// <summary>
        /// Re-prices what the houses above the outfit are owed against the city as it
        /// stands this morning, then hands over whatever has fallen due. A house that
        /// went unpaid hardens one step - it is the only stance change in the game the
        /// player does not choose, and it is the point of the mechanic: falling behind
        /// on tribute is how a quiet city turns on you.
        /// </summary>
        void CollectTribute()
        {
            scratchHoldings.Clear();
            HoldingsOf?.Invoke(scratchHoldings);

            Tribute.Assess(Relations, scratchHoldings, Gangs.GangCatalog.PlayerGangId,
                Campaign.Day);
            Tribute.Settle(Accounts, Campaign.Day, scratchSoured);

            for (var i = 0; i < scratchSoured.Count; i++)
            {
                var gangId = scratchSoured[i];
                // Pending, like every other stance change: it lands with the next
                // midnight, so a page open on the families is never rewritten under
                // the reader's eyes.
                var harder = Relations.StanceWith(gangId) == Stance.Peace
                    ? Stance.Truce
                    : Stance.War;
                Relations.SetPending(gangId, harder);
            }
            scratchHoldings.Clear();
        }

        // ----------------------------------------------------------------- the book

        /// <summary>
        /// The end of the job card's flow - only here does the job exist. It joins the
        /// back of the lieutenant's book and his crew starts it as soon as they are
        /// free; there is no turn to wait for. Headcount is clamped to the men he
        /// actually has, because a job cannot book five men out of a crew of three and
        /// pretending otherwise only produces a job that never starts.
        /// </summary>
        public OpResult Issue(Roster roster, Job job)
        {
            if (job == null || job.TargetCount == 0)
                return OpResult.Fail(UI.LedgerText.ReasonNoTargets);

            var crew = roster?.FindCrew(job.CrewId);
            if (crew == null)
                return OpResult.Fail(UI.LedgerText.ReasonNoSuchCrew);

            var available = CrewKit.MenOf(crew);
            if (job.Men < 1)
                job.Men = 1;
            if (job.Men > available)
                job.Men = available;

            // NOBODY AGREES TO BUY WHAT HE CANNOT PAY FOR. Only a purchase is gated
            // here: every other order is an attempt, and an outfit may certainly attempt
            // something it will struggle to pay for. Premises are a sale - the asking
            // price is due on the day - so the order book refuses it at the counter,
            // with the shortfall spelled out, exactly as the Armory does.
            if (job.Type == OrderType.BuyPremises)
            {
                var spec = OrderTable.SpecOf(job.Type);
                var price = OrderResolution.CostFor(
                    spec, job.TargetCount,
                    CrewKit.BestAt(roster, crew, spec.PrimaryAttribute),
                    job.TargetWorth);
                if (price > Accounts.Safe)
                    return OpResult.Fail(
                        UI.LedgerText.InsufficientFunds(price, Accounts.Safe));
            }

            job.Id = Book.NextJobId();
            job.IssuedDay = Campaign.Day;
            job.Stage = JobStage.Queued;
            Book.Jobs.Add(job);
            return OpResult.Success;
        }

        /// <summary>Calls a job off. One that had already started leaves a record - the
        /// men went, and the book says so - while one still queued simply never happened
        /// and is struck without a line.</summary>
        public OpResult Cancel(Roster roster, int jobId)
        {
            for (var i = 0; i < Book.Jobs.Count; i++)
            {
                var job = Book.Jobs[i];
                if (job.Id != jobId)
                    continue;

                if (job.Stage != JobStage.Queued)
                    Record(roster, job, OrderOutcome.CalledOff, 0, 0);
                Book.Jobs.RemoveAt(i);
                return OpResult.Success;
            }
            return OpResult.Fail(UI.LedgerText.ReasonNoSuchOrder);
        }

        /// <summary>
        /// List order IS queue order, so moving a row moves the work. A job the crew is
        /// already out on does not move: they are there.
        ///
        /// The swap is with the neighbour IN THE SAME LIEUTENANT'S BOOK, not the
        /// neighbouring row on the page. The page interleaves every crew's work, so a
        /// bare index swap would reorder somebody else's queue to move this one - which
        /// only became a bug when the queues stopped being one shared week's plan.
        /// </summary>
        public OpResult Move(int jobId, int direction)
        {
            var at = -1;
            for (var i = 0; i < Book.Jobs.Count && at < 0; i++)
                if (Book.Jobs[i].Id == jobId)
                    at = i;
            if (at < 0)
                return OpResult.Fail(UI.LedgerText.ReasonNoSuchOrder);

            var job = Book.Jobs[at];
            if (job.Stage != JobStage.Queued)
                return OpResult.Fail(UI.LedgerText.ReasonJobUnderway);

            var step = direction < 0 ? -1 : 1;
            for (var to = at + step; to >= 0 && to < Book.Jobs.Count; to += step)
            {
                var other = Book.Jobs[to];
                if (other.CrewId != job.CrewId || !other.Live)
                    continue;
                if (other.Stage != JobStage.Queued)
                    break;

                (Book.Jobs[at], Book.Jobs[to]) = (Book.Jobs[to], Book.Jobs[at]);
                return OpResult.Success;
            }
            return OpResult.Fail(UI.LedgerText.ReasonNoSuchOrder);
        }

        /// <summary>
        /// The men are AT the address. The book's travel hours are an estimate of how
        /// long getting there takes; when the street has actually put them on the
        /// doorstep the estimate is spent, whatever it said.
        ///
        /// Without this they stood outside the shop they had walked to, doing nothing,
        /// until an arithmetic somewhere agreed they had arrived - which is the delay a
        /// player reads as the game being broken, because the men are visibly there.
        /// </summary>
        public void ReportArrived(int jobId)
        {
            for (var i = 0; i < Book.Jobs.Count; i++)
            {
                var job = Book.Jobs[i];
                if (job.Id != jobId || !job.Live || job.Stage != JobStage.Travelling)
                    continue;
                job.TravelHoursLeft = 0f;
                return;
            }
        }

        /// <summary>
        /// The street's answer to a Violence job: the crew went, the sim played it out,
        /// and this is what happened. Still RESOLVED through Finish on the next tick, so
        /// one code path writes every record - but the work hours end here, because the
        /// street has already spent them.
        ///
        /// They used to run on: a front went in, the men then stood by the boarded window
        /// for the rest of the order's hours with the next order queued behind it, and
        /// the shop was not told it had been wrecked until the clock caught up. The hours
        /// are what the work COSTS, and the work is over the moment the street says so.
        /// </summary>
        public void ReportStreetOutcome(int jobId, OrderOutcome outcome)
        {
            for (var i = 0; i < Book.Jobs.Count; i++)
                if (Book.Jobs[i].Id == jobId && Book.Jobs[i].Live)
                {
                    Book.Jobs[i].StreetOutcome = outcome;
                    Book.Jobs[i].WorkHoursLeft = 0f;
                    return;
                }
        }

        public void OpenFirstSheet()
        {
            if (Accounts.Sheets.Count == 0)
                Accounts.Open(Campaign.Day);
        }
    }
}
