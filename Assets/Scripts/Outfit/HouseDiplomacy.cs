using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Outfit
{
    /// <summary>
    /// WHAT ONE HOUSE CAN SAY TO ANOTHER (EPIC 42). Every kind is a proposal with terms,
    /// answered by the other side; the kinds are appended as the epic's tickets land and
    /// never reordered - a saved proposal keeps its meaning.
    /// </summary>
    public enum ProposalKind
    {
        None = 0,

        /// <summary>A truce from midnight, with money on it or not (DIPL-002).</summary>
        OfferTruce,

        /// <summary>Peace from midnight, out of a truce, with money on it or not
        /// (DIPL-002). Never out of a war: a war ends in a truce first.</summary>
        OfferPeace,

        /// <summary>Keep off that street (DIPL-003). Complied with, the receiver keeps
        /// off it for ComplyDays; refused or unanswered, the sender is owed for it.
        /// </summary>
        Warn,

        /// <summary>The second word is the last one - the same demand, from the
        /// threat rung (DIPL-003).</summary>
        Threaten,

        /// <summary>A bill for what they took, priced from the grudge so that paying
        /// it in full lands exactly on the threat rung (DIPL-003).</summary>
        Bill,

        /// <summary>A fixed tribute envelope in place of the street's figure, for
        /// three cycles (DIPL-004): the levied house offering less, or the levying
        /// house asking more.</summary>
        TributeTerms,

        /// <summary>A man of theirs in our cellar, and the price to have him back
        /// (DIPL-005). Filed by the kidnap itself, open for as long as he is held.
        /// </summary>
        Ransom,

        /// <summary>A line between two houses (DIPL-006): both keep off the streets it
        /// names for LineDays, and a door taken or hit across it is owed for on top.
        /// </summary>
        Line,

        /// <summary>Mutual defence for PactDays (DIPL-007): a war declared on either
        /// puts the other at war with the declarer from the next midnight - by the
        /// book, not by a decision. Third names the house the proposer fears.</summary>
        Pact,

        /// <summary>The pact for one war, with money on it (DIPL-007): come to war with
        /// Third, and here is what it is worth to us.</summary>
        JoinWar,
    }

    public enum ProposalStatus
    {
        Open = 0,
        Accepted,
        Refused,
        Expired,

        /// <summary>Accepted, and then broken before midnight by a grievance heavy
        /// enough (a killing): the pending stance stood and the money went back.
        /// </summary>
        Broken,
    }

    /// <summary>The terms a proposal carries. Every kind reads the seats it needs and
    /// leaves the rest at their defaults.</summary>
    public sealed class ProposalTerms
    {
        public int Money;
        public int Kilos;
        public readonly List<string> Blocks = new List<string>();
        public int CharacterId = -1;
        public int Third = -1;
        public int Days;

        /// <summary>A name for the line - the man a ransom is for.</summary>
        public string Label = "";
    }

    /// <summary>
    /// ONE THING ONE HOUSE ASKED ANOTHER, and what came of it. Filed by
    /// <see cref="HouseOps.Propose"/> - the ledger's button and a mind's intent alike -
    /// answered at the desk or from the inbox, and printed in both books either way.
    /// </summary>
    public sealed class Proposal
    {
        public int Id;
        public int From;
        public int To;
        public ProposalKind Kind;
        public ProposalTerms Terms = new ProposalTerms();

        /// <summary>The campaign day it was filed, and the day it lapses unanswered.
        /// </summary>
        public int Day;
        public int ExpiresDay;

        public ProposalStatus Status = ProposalStatus.Open;

        /// <summary>The desk's own words when it said no, or empty.</summary>
        public string Answer = "";

        /// <summary>Money held between acceptance and the midnight it lands on
        /// (DIPL-002). Nothing in DIPL-001 fills it.</summary>
        public int Escrow;
        public int EscrowDirty;

        /// <summary>For a bill: the points money had cleared off the pair on the day it
        /// was filed, as they stood at filing - so the book can tell compensation that
        /// came AFTER (the bill lapses) from the grudge's own decay (it does not).</summary>
        public int ClearedAtFiling;

        /// <summary>The lieutenant who carried it in person, or -1 by telephone
        /// (DIPL-008), his Streetwise in half-steps as it was read when he left, and
        /// whether he is still on the road - a proposal in transit is not answered
        /// until he stands at their door.</summary>
        public int Envoy = -1;
        public int EnvoyHalfSteps;
        public bool InTransit;

        public bool Open => Status == ProposalStatus.Open;

        /// <summary>The street a word names, or the first of the blocks it carries.
        /// </summary>
        public string Street() => Terms.Blocks.Count > 0 ? Terms.Blocks[0] : "our streets";

        /// <summary>The sentence the sender wrote, for both books.</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case ProposalKind.OfferTruce:
                    return "offers a truce" + (Terms.Money > 0 ? " - $" + Terms.Money : "");
                case ProposalKind.OfferPeace:
                    return "offers peace" + (Terms.Money > 0 ? " - $" + Terms.Money : "");
                case ProposalKind.Warn:
                    return "warns them off " + Street();
                case ProposalKind.Threaten:
                    return "will not warn them again - off " + Street();
                case ProposalKind.Bill:
                    return "sends a bill for what they took - $" + Terms.Money;
                case ProposalKind.TributeTerms:
                    return "puts $" + Terms.Money + " a cycle on the table as tribute";
                case ProposalKind.Ransom:
                    return "has " + (string.IsNullOrEmpty(Terms.Label) ? "a man of theirs" : Terms.Label) +
                           " - $" + Terms.Money + " to have him back";
                case ProposalKind.Line:
                    return "draws a line across " + Terms.Blocks.Count +
                           (Terms.Blocks.Count == 1 ? " street" : " streets");
                case ProposalKind.Pact:
                    return "offers a pact" + (Terms.Third >= 0
                        ? " against " + Gangs.GangCatalog.Names[Terms.Third]
                        : "");
                case ProposalKind.JoinWar:
                    return "asks them into the war with " +
                           (Terms.Third >= 0 ? Gangs.GangCatalog.Names[Terms.Third] : "nobody") +
                           (Terms.Money > 0 ? " - $" + Terms.Money : "");
                default:
                    return "says nothing";
            }
        }
    }

    /// <summary>The desk's verdict on a proposal: yes, or no in its own words.</summary>
    public readonly struct DeskAnswer
    {
        public DeskAnswer(bool accepted, string reason)
        {
            Accepted = accepted;
            Reason = reason ?? "";
        }

        public bool Accepted { get; }
        public string Reason { get; }

        public static readonly DeskAnswer Yes = new DeskAnswer(true, "");
        public static DeskAnswer No(string reason) => new DeskAnswer(false, reason);
    }

    /// <summary>
    /// EVERY NUMBER BETWEEN TWO HOUSES AT THE TABLE, in one place (EPIC 42 §3). Never a
    /// literal in a method.
    /// </summary>
    public sealed class DiplomacyConfig
    {
        public static readonly DiplomacyConfig Default = new DiplomacyConfig();

        /// <summary>How long an open proposal waits for an answer; a word waits less.
        /// </summary>
        public int ProposalDays = 3;
        public int WordDays = 2;

        /// <summary>What a dollar clears of a grudge, and the most any money clears
        /// of one pair's in a day (ruling 4).</summary>
        public int CompensationPerPoint = 200;
        public int CompensationCapPerDay = 20;

        /// <summary>For this long after a killing, money cannot take the pair under
        /// ThreatAt (ruling 4).</summary>
        public int KillingFloorDays = 30;

        /// <summary>A grievance this heavy, noted after a truce was agreed, breaks the
        /// agreement before midnight (DIPL-002).</summary>
        public int AgreementBreaksAt = 35;

        /// <summary>How long a house that complied with a warning keeps off the block
        /// (DIPL-003).</summary>
        public int ComplyDays = 5;

        public int LineDays = 30;
        public int PactDays = 30;

        /// <summary>What a sit-down in person moves the receiver's dollar tests per
        /// half-star of the envoy, and the most it moves them (DIPL-008).</summary>
        public float EnvoyMarginPerHalfStep = 0.02f;
        public float EnvoyMarginCap = 0.2f;

        /// <summary>A mind never ambushes a sit-down in this epic.</summary>
        public bool MindAmbushes = false;

        /// <summary>A mind pays a bill only with this many days of wages left over
        /// (the reserve rule, D9, applied to a bill).</summary>
        public int BillReserveDays = 7;

        /// <summary>How many tribute cycles agreed terms stand for (DIPL-004).</summary>
        public int TermsCycles = 3;

        /// <summary>A mind kicks up an envelope only with this many days of wages left
        /// over afterwards (DIPL-004): twice the mind's ordinary reserve, because the
        /// wages go on falling due after the envelope and a house that paid down to
        /// one week was under it by the next morning. Below it the envelope goes
        /// overdue and the levying house is owed for it - the pressure the mechanic
        /// is for.</summary>
        public int TributeReserveDays = 14;

        /// <summary>How many closed proposals a pair's record keeps.</summary>
        public int HistoryPerPair = 30;
    }

    /// <summary>
    /// THE PROPOSAL BOOK - one per city, on <see cref="Underworld"/> beside the book of
    /// standings. What every house has asked every other, what was answered, which
    /// streets a house has given its word to keep off. Pure and free of UnityEngine;
    /// the runtime and the paper city carry intents to it through <see cref="HouseOps"/>.
    /// </summary>
    public sealed class HouseDiplomacy
    {
        public const string ReasonAlreadyAsked = "we already asked";
        public const string ReasonNobodyToAskWord = "there is nobody to say it to";
        public const string ReasonNothingToSay = "nothing to say";
        public const string ReasonUnderOurWord = "that street is under our word";
        public const string ReasonTakenTooMuch = "they have taken too much";
        public const string ReasonNoSuchProposal = "nobody asked us that";
        public const string ReasonCouldNotPutTheMoneyUp = "they could not put the money up";
        public const string ReasonOurMenWorkThoseStreets = "our men work those streets";
        public const string ReasonNotYet = "not yet";
        public const string ReasonAWarEndsInATruce = "a war ends in a truce first";
        public const string ReasonNothingToEnd = "there is nothing between us to end";
        public const string ReasonWeCouldNotRefuse = "we could not refuse";
        public const string ReasonBrokenBeforeMidnight = "the agreement was broken before midnight";
        public const string ReasonWeKeepOurStreets = "we keep our streets";
        public const string ReasonWhistleForIt = "they can whistle for it";
        public const string ReasonNoStreetNamed = "no street named";
        public const string ReasonNoSuchDebt = "they owe us no such thing";
        public const string ReasonNobodyOwesAnybody = "nobody owes anybody here";
        public const string ReasonHeCanWait = "he can wait";
        public const string ReasonWeCanAffordToArgue = "we can afford to argue";
        public const string ReasonNotAtPeaceWithThem = "we are not at peace with them";
        public const string ReasonNoWarToJoin = "there is no war to join";
        public const string ReasonTheyReadStronger = "they read stronger than us";
        public const string ReasonCannotPayForAWar = "we cannot pay for a war";
        public const string ReasonLeftThemToIt = "left them to it";
        public const string ReasonTheDonStaysHome = "the Don stays home";
        public const string ReasonNoEnvoy = "nobody to carry it";
        public const string ReasonStillOnTheRoad = "he has not arrived";
        public const string ReasonAmbushed = "ambushed at the door";
        public const string ReasonNotOurMan = "he is not ours";
        public const string ReasonTheStreetPricesIt = "the street prices it";

        readonly List<Proposal> proposals = new List<Proposal>();
        readonly Dictionary<(int house, string block), int> keepOff =
            new Dictionary<(int, string), int>();
        int nextId = 1;

        /// <summary>THE LINES (DIPL-006): which two houses agreed to keep off which
        /// street, until when. A door taken or hit across one is owed for on top.
        /// </summary>
        public sealed class Line
        {
            public int A;
            public int B;
            public string Block = "";
            public int UntilDay;

            public bool Names(int house) => A == house || B == house;
        }

        readonly List<Line> lines = new List<Line>();

        public IReadOnlyList<Line> Lines => lines;

        /// <summary>THE PACTS (DIPL-007): two houses sworn to each other's wars until
        /// a day. Honoured by the book at midnight.</summary>
        public sealed class Pact
        {
            public int A;
            public int B;
            public int UntilDay;

            public bool Names(int house) => A == house || B == house;
            public int PartnerOf(int house) => A == house ? B : A;
        }

        readonly List<Pact> pacts = new List<Pact>();

        public IReadOnlyList<Pact> Pacts => pacts;

        public bool HasPact(int a, int b, int day)
        {
            for (var i = 0; i < pacts.Count; i++)
                if (pacts[i].Names(a) && pacts[i].Names(b) && a != b && day < pacts[i].UntilDay)
                    return true;
            return false;
        }

        public HouseDiplomacy(DiplomacyConfig config = null) =>
            Config = config ?? DiplomacyConfig.Default;

        public DiplomacyConfig Config { get; }

        /// <summary>Every proposal the book still holds - open ones and the record.
        /// </summary>
        public IReadOnlyList<Proposal> All => proposals;

        // ----------------------------------------------------------------- filing

        /// <summary>A proposal on the book, open, with its expiry set. The caller has
        /// already refused what should not be filed; this only writes.</summary>
        public Proposal File(int from, int to, ProposalKind kind, ProposalTerms terms,
            int day)
        {
            var proposal = new Proposal
            {
                Id = nextId++,
                From = from,
                To = to,
                Kind = kind,
                Terms = terms ?? new ProposalTerms(),
                Day = day,
                ExpiresDay = day + DaysFor(kind),
            };
            proposals.Add(proposal);
            Prune(from, to);
            return proposal;
        }

        /// <summary>How long a proposal of this kind waits for an answer: a word two
        /// days, a ransom as long as the man is held, everything else the table's
        /// three.</summary>
        public int DaysFor(ProposalKind kind) =>
            IsWord(kind) ? Config.WordDays
            : kind == ProposalKind.Ransom ? OrderResolution.KidnapDays
            : Config.ProposalDays;

        /// <summary>A warning, a threat or a bill waits less than everything else,
        /// and its lapse is a grudge (DIPL-003).</summary>
        public static bool IsWord(ProposalKind kind) =>
            kind == ProposalKind.Warn || kind == ProposalKind.Threaten ||
            kind == ProposalKind.Bill;

        public Proposal Find(int id)
        {
            for (var i = 0; i < proposals.Count; i++)
                if (proposals[i].Id == id)
                    return proposals[i];
            return null;
        }

        /// <summary>Whether this house has an open proposal of this kind to that one -
        /// or, given the day, one accepted today and waiting for midnight to land. The
        /// view's look, and the duplicate rule's test: a truce agreed at noon is not
        /// asked for again at one.</summary>
        public bool HasOpen(int from, int to, ProposalKind kind, int day = -1)
        {
            for (var i = 0; i < proposals.Count; i++)
            {
                var p = proposals[i];
                if (p.From != from || p.To != to || p.Kind != kind)
                    continue;
                if (p.Open)
                    return true;
                if (day >= 0 && p.Status == ProposalStatus.Accepted && p.Day == day)
                    return true;
            }
            return false;
        }

        /// <summary>The day this house last filed a proposal of this kind to that one,
        /// or -1. The mind's look for "not again so soon".</summary>
        public int LastFiled(int from, int to, ProposalKind kind)
        {
            var last = -1;
            for (var i = 0; i < proposals.Count; i++)
            {
                var p = proposals[i];
                if (p.From == from && p.To == to && p.Kind == kind && p.Day > last)
                    last = p.Day;
            }
            return last;
        }

        /// <summary>Every open proposal addressed to this house - the inbox.</summary>
        public void OpenFor(int to, List<Proposal> into)
        {
            into?.Clear();
            if (into == null)
                return;
            for (var i = 0; i < proposals.Count; i++)
                if (proposals[i].Open && proposals[i].To == to)
                    into.Add(proposals[i]);
        }

        /// <summary>The record between two houses, oldest first.</summary>
        public void Between(int a, int b, List<Proposal> into)
        {
            into?.Clear();
            if (into == null)
                return;
            for (var i = 0; i < proposals.Count; i++)
            {
                var p = proposals[i];
                if ((p.From == a && p.To == b) || (p.From == b && p.To == a))
                    into.Add(p);
            }
        }

        /// <summary>The record keeps HistoryPerPair closed proposals a pair; the oldest
        /// closed one goes when a new one is filed - never one still holding money in
        /// escrow: that record is the only place the deducted cash lives until
        /// midnight (Codex).</summary>
        void Prune(int a, int b)
        {
            var closed = 0;
            for (var i = 0; i < proposals.Count; i++)
            {
                var p = proposals[i];
                if (!p.Open && p.Escrow <= 0 &&
                    ((p.From == a && p.To == b) || (p.From == b && p.To == a)))
                    closed++;
            }
            for (var i = 0; i < proposals.Count && closed > Config.HistoryPerPair; i++)
            {
                var p = proposals[i];
                if (p.Open || p.Escrow > 0 ||
                    !((p.From == a && p.To == b) || (p.From == b && p.To == a)))
                    continue;
                proposals.RemoveAt(i);
                i--;
                closed--;
            }
        }

        // --------------------------------------------------------------- the desk

        /// <summary>
        /// THE ANSWER, AT ONCE AND BY THE TABLES. The receiver's own view and nothing
        /// else: its own books, and its own side of the pair through the looks. No roll
        /// anywhere - the same proposal gets the same answer on two runs of one seed.
        /// </summary>
        public static DeskAnswer Answer(HouseView view, Proposal proposal,
            DiplomacyConfig config, HouseRelationsConfig relations = null)
        {
            config = config ?? DiplomacyConfig.Default;
            relations = relations ?? HouseRelationsConfig.Default;
            if (view == null || proposal == null)
                return DeskAnswer.No(ReasonNothingToSay);
            var from = new TerritoryGangId(proposal.From);
            var stance = view.StanceToward(from);

            // THE ENVOY'S MARGIN (DIPL-008): a good talker at the door moves every
            // dollar test in his house's favour and reads his house a little stronger.
            // Rounded in double: five half-steps at two percent is a hair under a
            // tenth in single precision, and a truncation there read $2,200 as $2,199
            // and eleven days as ten - the editor's Mono and the offline CoreCLR
            // disagreed about it, so neither is trusted with the last bit.
            var margin = MarginOf(proposal, config);
            var money = (int)System.Math.Round(
                (proposal.Terms != null ? proposal.Terms.Money : 0) * (1.0 + margin));
            var theirs = (int)System.Math.Round(view.TheirEndurance(from) * (1.0 + margin));
            switch (proposal.Kind)
            {
                case ProposalKind.OfferTruce:
                    if (stance == Stance.War)
                    {
                        // THE BEATEN CANNOT REFUSE (ruling 1).
                        if (MustAccept(view, proposal, relations))
                            return DeskAnswer.Yes;
                        // The money clears the grudge under the retake rung.
                        if (AfterMoney(view, from, money, config) < relations.RetakeBusinessAt)
                            return DeskAnswer.Yes;
                        // They read as the stronger house, and we are not owed shops.
                        if (theirs > view.Endurance &&
                            view.Grievance(from) < relations.AttackBusinessAt)
                            return DeskAnswer.Yes;
                        return DeskAnswer.No(ReasonTakenTooMuch);
                    }
                    if (stance == Stance.Truce)
                        return DeskAnswer.No(ReasonNothingToEnd);
                    // At peace a truce engages trespassers on both grounds: taken only
                    // while none of our crews works their streets.
                    return CrewOnTheirGround(view, from)
                        ? DeskAnswer.No(ReasonOurMenWorkThoseStreets)
                        : DeskAnswer.Yes;

                case ProposalKind.OfferPeace:
                    if (stance == Stance.War)
                        return DeskAnswer.No(ReasonAWarEndsInATruce);
                    if (stance == Stance.Peace)
                        return DeskAnswer.No(ReasonNothingToEnd);
                    return AfterMoney(view, from, money, config) < relations.PeaceGrievance
                        ? DeskAnswer.Yes
                        : DeskAnswer.No(ReasonNotYet);

                // A WORD IS COMPLIED WITH from a house that reads stronger, while we
                // are owed less than a threat's worth by it; a bill, on the same test,
                // when the safe covers it over the reserve. Otherwise it is refused,
                // and the ladder does the rest.
                case ProposalKind.Warn:
                case ProposalKind.Threaten:
                    return Yields(view, from, theirs, relations)
                        ? DeskAnswer.Yes
                        : DeskAnswer.No(ReasonWeKeepOurStreets);

                case ProposalKind.Bill:
                    if (!Yields(view, from, theirs, relations))
                        return DeskAnswer.No(ReasonWhistleForIt);
                    return view.Safe - money >= config.BillReserveDays * view.DailyPayroll
                        ? DeskAnswer.Yes
                        : DeskAnswer.No(ReasonWhistleForIt);

                // TRIBUTE TERMS (DIPL-004). From the house that pays us: taken when we
                // are broke, or when it is at least half of what the street prices
                // the envelope at. From the house we pay: taken only when it reads
                // stronger and the safe covers the new figure over the reserve.
                case ProposalKind.TributeTerms:
                    if (stance == Stance.War)
                        return DeskAnswer.No(ReasonNobodyOwesAnybody);
                    // Both figures are the STREET's own - what the holdings price the
                    // envelope at, never the pinned one - so a discount agreed cannot be
                    // halved again off itself (Codex).
                    var theyOwe = view.TributeOwed(from);
                    var weOwe = view.TributeOwe(from);
                    if (theyOwe > 0)
                        return view.Endurance < relations.MinWarDays || money * 2 >= theyOwe
                            ? DeskAnswer.Yes
                            : DeskAnswer.No(ReasonTheStreetPricesIt);
                    if (weOwe > 0)
                        return theirs > view.Endurance &&
                               view.Safe - money >= config.BillReserveDays * view.DailyPayroll
                            ? DeskAnswer.Yes
                            : DeskAnswer.No(ReasonTheStreetPricesIt);
                    return DeskAnswer.No(ReasonNobodyOwesAnybody);

                // A LINE (DIPL-006) is taken by a house that could not pay for a war
                // with the house that drew it, and that reads the other as unable to
                // pay for one either - the pair the border was squeezing.
                case ProposalKind.Line:
                    if (proposal.Terms.Blocks.Count == 0)
                        return DeskAnswer.No(ReasonNoStreetNamed);
                    return view.Endurance < relations.MinWarDays &&
                           theirs < relations.MinWarDays
                        ? DeskAnswer.Yes
                        : DeskAnswer.No(ReasonWeCanAffordToArgue);

                // A PACT (DIPL-007) is taken by a house at peace with the one offering
                // it, that can pay for a war, against a third house that reads weaker
                // than itself. JOIN MY WAR is the pact for one war: the same test,
                // plus the money clearing our own grudge against the asker.
                case ProposalKind.Pact:
                case ProposalKind.JoinWar:
                    if (stance != Stance.Peace)
                        return DeskAnswer.No(ReasonNotAtPeaceWithThem);
                    var third = new TerritoryGangId(proposal.Terms.Third);
                    if (!third.IsValid || third == view.House)
                        return DeskAnswer.No(ReasonNoWarToJoin);
                    if (view.Endurance < relations.MinWarDays)
                        return DeskAnswer.No(ReasonCannotPayForAWar);
                    if (view.TheirEndurance(third) >= view.Endurance)
                        return DeskAnswer.No(ReasonTheyReadStronger);
                    if (proposal.Kind == ProposalKind.JoinWar &&
                        AfterMoney(view, from, money, config) >= relations.ThreatAt)
                        return DeskAnswer.No(ReasonTakenTooMuch);
                    return DeskAnswer.Yes;

                // A RANSOM (DIPL-005): a lieutenant is bought back whenever the safe
                // covers it; a hood, when it covers him over the reserve. Otherwise he
                // sits it out and comes home in a bed.
                case ProposalKind.Ransom:
                    var held = view.Roster != null
                        ? view.Roster.Find(proposal.Terms.CharacterId)
                        : null;
                    if (held == null || held.Gone)
                        return DeskAnswer.No(ReasonNotOurMan);
                    if (held.Rank == Rank.Lieutenant || held.Rank == Rank.Boss)
                        return view.Safe >= money ? DeskAnswer.Yes : DeskAnswer.No(ReasonHeCanWait);
                    return view.Safe - money >= config.BillReserveDays * view.DailyPayroll
                        ? DeskAnswer.Yes
                        : DeskAnswer.No(ReasonHeCanWait);
            }
            return DeskAnswer.No(ReasonNothingToSay);
        }

        /// <summary>The most a bill from one house to another may ask: what the sender
        /// is owed above the threat rung, at the table's rate - the mind's own price
        /// for its bill, made the ceiling for everybody's.</summary>
        public static int BillCeiling(HouseRelations relations, int from, int to,
            DiplomacyConfig config, int day)
        {
            if (relations == null)
                return 0;
            config = config ?? DiplomacyConfig.Default;
            // What a bill can still clear TODAY - a second bill after the day's cap is
            // spent asks for nothing, so a debtor is not billed until it is dry (Codex)
            // - and never past the threat rung: a bill is the ladder's third step, and
            // paying it in full lands exactly on the second.
            var clearable = relations.Clearable(from, to, day, config.CompensationCapPerDay,
                relations.Config.ThreatAt, config.KillingFloorDays);
            var aboveThreat = (int)(relations.Grievance(from, to) - relations.Config.ThreatAt);
            if (clearable > aboveThreat)
                clearable = aboveThreat;
            return clearable > 0 ? clearable * config.CompensationPerPoint : 0;
        }

        /// <summary>The house that sent the word reads as the stronger, and we are
        /// owed less than a threat's worth by it.</summary>
        static bool Yields(HouseView view, TerritoryGangId from, int theirs,
            HouseRelationsConfig relations) =>
            theirs > view.Endurance && view.Grievance(from) < relations.ThreatAt;

        /// <summary>What a sit-down in person moves the receiver's tests by: the
        /// envoy's Streetwise, per half-star, capped (DIPL-008). Zero by telephone.
        /// </summary>
        public static float MarginOf(Proposal proposal, DiplomacyConfig config)
        {
            config = config ?? DiplomacyConfig.Default;
            if (proposal == null || proposal.Envoy < 0 || proposal.EnvoyHalfSteps <= 0)
                return 0f;
            var margin = proposal.EnvoyHalfSteps * config.EnvoyMarginPerHalfStep;
            return margin > config.EnvoyMarginCap ? config.EnvoyMarginCap : margin;
        }

        /// <summary>
        /// A HOUSE THAT CANNOT PAY ITS MEN THROUGH THE WAR, OR HAS LOST TOO MANY, TAKES
        /// THE TRUCE WHATEVER IT IS OWED (ruling 1: "da ako je porazen jbg") - the mind
        /// at the desk and the player at his inbox alike. Read off the receiver's own
        /// books and its own tally, nothing of the sender's.
        /// </summary>
        public static bool MustAccept(HouseView view, Proposal proposal,
            HouseRelationsConfig relations = null)
        {
            relations = relations ?? HouseRelationsConfig.Default;
            if (view == null || proposal == null || proposal.Kind != ProposalKind.OfferTruce)
                return false;
            var from = new TerritoryGangId(proposal.From);
            if (view.StanceToward(from) != Stance.War)
                return false;
            return view.Endurance < relations.MinWarDays ||
                   view.Losses(from) >= relations.LossesToSueForPeace;
        }

        /// <summary>What our grudge against them would read after their money, by the
        /// table's own rate and the day's cap - the desk's reckoning, which the
        /// settlement then applies for real through HouseRelations.Clear.</summary>
        static float AfterMoney(HouseView view, TerritoryGangId from, int money,
            DiplomacyConfig config)
        {
            var points = money / (config.CompensationPerPoint > 0
                ? config.CompensationPerPoint
                : 1);
            // What the money can actually clear today: the cap less what was cleared
            // already, over the killing floor - the same reckoning Clear will make,
            // so the desk never says yes to a figure that then does nothing (Codex).
            var clearable = view.Clearable(from, config.CompensationCapPerDay);
            if (points > clearable)
                points = clearable;
            var after = view.Grievance(from) - points;
            return after < 0f ? 0f : after;
        }

        /// <summary>Whether a crew of ours stands on a block they lead.</summary>
        static bool CrewOnTheirGround(HouseView view, TerritoryGangId them)
        {
            var crews = view.Roster != null ? view.Roster.Crews : null;
            for (var i = 0; crews != null && i < crews.Count; i++)
            {
                var block = view.CrewBlock(crews[i].Id);
                if (block.IsValid && view.Leader(block) == them)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// WHAT AN ANSWER DOES. The status, the line in both books, and on acceptance
        /// the effect - the stance pending, the money across. Answers false when the
        /// effect could not be carried (the money was not there), in which case the
        /// proposal reads Refused in the desk's own words.
        /// </summary>
        public bool Settle(Underworld world, Proposal proposal, bool accepted,
            string reason, int day)
        {
            if (world == null || proposal == null || !proposal.Open)
                return false;

            // A BILL THAT LAPSED is not a bill answered: the debt it named was cleared
            // by other money while it lay open, so whatever the answer, nothing moves
            // and no grudge is taken for it (Codex) - read before the answer is.
            if (BillLapsed(world.Relations, proposal, day))
            {
                Lapse(world, proposal, day);
                return false;
            }

            var note = accepted ? reason ?? "" : "";
            if (accepted)
            {
                var refusal = Apply(world, proposal, day);
                if (refusal != null)
                {
                    accepted = false;
                    reason = refusal;
                }
            }

            proposal.Status = accepted ? ProposalStatus.Accepted : ProposalStatus.Refused;
            proposal.Answer = accepted ? note : (reason ?? "");
            // A WORD REFUSED IS A GRUDGE AT ONCE (DIPL-003) - not after a sweep.
            if (!accepted && IsWord(proposal.Kind))
                world.Relations.Note(proposal.From, proposal.To, GrievanceKind.WarningIgnored,
                    day);
            Print(world, proposal, accepted
                ? Describe(proposal) + " · ACCEPTED" +
                  (string.IsNullOrEmpty(note) ? "" : " - " + note)
                : Describe(proposal) + " · REFUSED" +
                  (string.IsNullOrEmpty(reason) ? "" : " - " + reason), day);
            return accepted;
        }

        /// <summary>
        /// The effect of a yes, by kind. Null when it was carried. A stance is AGREED
        /// on the pair, to land over the pending slot at midnight (the guarded write);
        /// its money leaves the sender's safe now and waits in escrow until the same
        /// midnight; the grudge it clears, it clears now, within the day's cap.
        /// </summary>
        string Apply(Underworld world, Proposal proposal, int day)
        {
            switch (proposal.Kind)
            {
                case ProposalKind.OfferTruce:
                case ProposalKind.OfferPeace:
                    var money = proposal.Terms.Money;
                    if (money > 0)
                    {
                        var payer = world.Of(proposal.From);
                        if (payer == null)
                            return ReasonCouldNotPutTheMoneyUp;
                        var refusal = BalanceMath.Pay(payer.Runner.Accounts, money,
                            out var dirty);
                        if (refusal != null)
                            return ReasonCouldNotPutTheMoneyUp;
                        proposal.Escrow = money;
                        proposal.EscrowDirty = dirty;
                        var sheet = payer.Runner.Accounts.Current;
                        if (sheet != null)
                            sheet.ToHouses += money;
                        payer.Touch();
                        Compensate(world.Relations, proposal.To, proposal.From, money, day);
                    }
                    world.Relations.Agree(proposal.From, proposal.To,
                        proposal.Kind == ProposalKind.OfferTruce ? Stance.Truce : Stance.Peace,
                        day);
                    return null;

                // COMPLIED WITH: the receiver keeps off the street it was warned off.
                case ProposalKind.Warn:
                case ProposalKind.Threaten:
                    if (proposal.Terms.Blocks.Count == 0)
                        return ReasonNoStreetNamed;
                    KeepOff(proposal.To, new TerritoryBlockId(proposal.Terms.Blocks[0]),
                        day + Config.ComplyDays);
                    return null;

                // PAID: the receiver's money to the sender, and the sender's grudge
                // cleared by it within the day's cap (a stale bill never reaches here:
                // Settle lapses it first).
                case ProposalKind.Bill:
                    var moved = world.Transfer(proposal.To, proposal.From, proposal.Terms.Money);
                    if (moved != null)
                        return ReasonCouldNotPutTheMoneyUp;
                    Compensate(world.Relations, proposal.From, proposal.To,
                        proposal.Terms.Money, day);
                    return null;

                // TERMS: the levy between the two is pinned at the figure for the
                // cycles agreed, on whichever book carries it.
                case ProposalKind.TributeTerms:
                    var until = day + Config.TermsCycles * Tribute.CycleDays;
                    var levied = world.Of(proposal.From);
                    var levy = levied?.Runner.Tribute.For(proposal.To);
                    if (levy == null)
                    {
                        levied = world.Of(proposal.To);
                        levy = levied?.Runner.Tribute.For(proposal.From);
                    }
                    if (levy == null)
                        return ReasonNobodyOwesAnybody;
                    levy.Pin(proposal.Terms.Money, until);
                    levied.Touch();
                    return null;

                // THE LINE: both houses keep off every street it names, and the book
                // remembers whose line it is for the crossing rule.
                case ProposalKind.Line:
                    if (proposal.Terms.Blocks.Count == 0)
                        return ReasonNoStreetNamed;
                    var lineUntil = day + Config.LineDays;
                    for (var i = 0; i < proposal.Terms.Blocks.Count; i++)
                    {
                        var street = new TerritoryBlockId(proposal.Terms.Blocks[i]);
                        KeepOff(proposal.From, street, lineUntil);
                        KeepOff(proposal.To, street, lineUntil);
                        lines.Add(new Line
                        {
                            A = proposal.From,
                            B = proposal.To,
                            Block = proposal.Terms.Blocks[i],
                            UntilDay = lineUntil,
                        });
                    }
                    return null;

                // A PACT on the book; JOIN MY WAR is the money across, the grudge
                // cleared, and the receiver's war on the third house from midnight -
                // flagged the pact's own, so it wakes no other pact.
                case ProposalKind.Pact:
                    pacts.Add(new Pact
                    {
                        A = proposal.From,
                        B = proposal.To,
                        UntilDay = day + Config.PactDays,
                    });
                    return null;

                case ProposalKind.JoinWar:
                    if (proposal.Terms.Money > 0)
                    {
                        var crossed = world.Transfer(proposal.From, proposal.To,
                            proposal.Terms.Money);
                        if (crossed != null)
                            return ReasonCouldNotPutTheMoneyUp;
                        Compensate(world.Relations, proposal.To, proposal.From,
                            proposal.Terms.Money, day);
                    }
                    world.Relations.SetPending(proposal.To, proposal.Terms.Third, Stance.War,
                        byPact: true);
                    return null;

                // PAID: the money to the house that holds him, and he is let go in
                // the morning - to a bed, the way a man comes back from a cellar.
                case ProposalKind.Ransom:
                    var family = world.Of(proposal.To);
                    var man = family?.Roster.Find(proposal.Terms.CharacterId);
                    if (man == null || man.Status != CharacterStatus.Taken)
                        return ReasonNotOurMan;
                    var paid = world.Transfer(proposal.To, proposal.From, proposal.Terms.Money);
                    if (paid != null)
                        return ReasonCouldNotPutTheMoneyUp;
                    RosterOps.LetGo(family.Roster, man.Id, day + 1);
                    family.Touch();
                    return null;
            }
            return null;
        }

        /// <summary>
        /// EVERY DOLLAR THAT CLEARS A GRUDGE COMES THROUGH HERE (ruling 4): the table's
        /// rate, the day's cap on the pair, and the killing floor - all applied by
        /// HouseRelations.Clear. Answers the points actually cleared.
        /// </summary>
        public int Compensate(HouseRelations relations, int aggrieved, int offender,
            int money, int day)
        {
            if (relations == null || money <= 0)
                return 0;
            var rate = Config.CompensationPerPoint > 0 ? Config.CompensationPerPoint : 1;
            return relations.Clear(aggrieved, offender, money / rate, day,
                Config.CompensationCapPerDay, relations.Config.ThreatAt,
                Config.KillingFloorDays);
        }

        /// <summary>
        /// MIDNIGHT'S SECOND HALF. The agreements have landed or broken in
        /// HouseRelations.ApplyPending; the money each accepted proposal held in escrow
        /// follows them - to the payee when the stance landed, back to the payer when
        /// a killing broke it - and a broken one is written up as such.
        /// </summary>
        public void ReleaseEscrows(Underworld world, List<AgreementOutcome> outcomes, int day)
        {
            if (world == null || outcomes == null)
                return;
            for (var i = 0; i < proposals.Count; i++)
            {
                var p = proposals[i];
                if (p.Status != ProposalStatus.Accepted ||
                    (p.Kind != ProposalKind.OfferTruce && p.Kind != ProposalKind.OfferPeace))
                    continue;
                var found = false;
                var landed = false;
                for (var o = 0; o < outcomes.Count && !found; o++)
                    if (outcomes[o].IsPair(p.From, p.To))
                    {
                        found = true;
                        landed = outcomes[o].Landed;
                    }
                if (!found)
                    continue;

                if (landed)
                {
                    if (p.Escrow > 0)
                    {
                        var payee = world.Of(p.To);
                        if (payee != null)
                        {
                            BalanceMath.Receive(payee.Runner.Accounts, p.Escrow,
                                MoneyKind.Dirty);
                            var sheet = payee.Runner.Accounts.Current;
                            if (sheet != null)
                                sheet.FromHouses += p.Escrow;
                            payee.Touch();
                        }
                    }
                    p.Escrow = 0;
                    p.EscrowDirty = 0;
                    continue;
                }

                if (p.Escrow > 0)
                {
                    var payer = world.Of(p.From);
                    if (payer != null)
                    {
                        BalanceMath.Refund(payer.Runner.Accounts, p.Escrow, p.EscrowDirty);
                        var sheet = payer.Runner.Accounts.Current;
                        if (sheet != null)
                            sheet.ToHouses -= p.Escrow;
                        payer.Touch();
                    }
                }
                p.Escrow = 0;
                p.EscrowDirty = 0;
                p.Status = ProposalStatus.Broken;
                p.Answer = ReasonBrokenBeforeMidnight;
                Print(world, p, Describe(p) + " · BROKEN - " + ReasonBrokenBeforeMidnight, day);
            }
        }

        /// <summary>The sender's name and its sentence, as both books print it.</summary>
        public static string Describe(Proposal proposal) =>
            Gangs.GangCatalog.Names[proposal.From] + " " + proposal.Describe();

        /// <summary>One line in both books - theirs so they know, ours so the player
        /// can read what his own house said (the Word precedent, RIVAL-007).</summary>
        public static void Print(Underworld world, Proposal proposal, string line, int day)
        {
            if (world == null || proposal == null)
                return;
            var note = new Incident(-1, line, IncidentKind.AWordBetweenHouses, day, "",
                0, line);
            world.Of(proposal.From)?.Runner.Incidents.Add(note);
            world.Of(proposal.To)?.Runner.Incidents.Add(note);
        }

        // ---------------------------------------------------------------- midnight

        /// <summary>Every open proposal whose day has come lapses - a refusal without
        /// a note, except a word: a word nobody answered is owed for (DIPL-003). Filled
        /// with what lapsed, for the edge that wants to print it.</summary>
        public void Expire(int day, HouseRelations relations = null,
            List<Proposal> expired = null)
        {
            expired?.Clear();
            for (var i = 0; i < proposals.Count; i++)
            {
                var p = proposals[i];
                if (!p.Open || day < p.ExpiresDay)
                    continue;
                p.Status = ProposalStatus.Expired;
                // A bill left unanswered is a word ignored - unless the debt it named
                // was cleared meanwhile: then it merely lapses (Codex).
                if (BillLapsed(relations, p, day))
                    p.Answer = ReasonNoSuchDebt;
                else if (IsWord(p.Kind))
                    relations?.Note(p.From, p.To, GrievanceKind.WarningIgnored, day);
                expired?.Add(p);
            }
            SweepKeepOffs(day);
        }

        /// <summary>A bill lying open while other money - a truce bought, another bill
        /// paid - cleared the grudge it was priced from: the ceiling is read again on
        /// the day it is answered or left, not only at filing (Codex). Only money
        /// counts: the grudge's own daily decay lowers the ceiling too, and a bill
        /// that merely aged is still a bill ignored.</summary>
        public bool BillLapsed(HouseRelations relations, Proposal proposal, int day)
        {
            if (proposal == null || proposal.Kind != ProposalKind.Bill || relations == null)
                return false;
            if (proposal.Terms.Money <= BillCeiling(relations, proposal.From, proposal.To, Config, day))
                return false;
            var (clearedDay, points) = relations.ClearedOn(proposal.From, proposal.To);
            return clearedDay > proposal.Day ||
                   (clearedDay == proposal.Day && points > proposal.ClearedAtFiling);
        }

        void Lapse(Underworld world, Proposal proposal, int day)
        {
            proposal.Status = ProposalStatus.Expired;
            proposal.Answer = ReasonNoSuchDebt;
            Print(world, proposal, Describe(proposal) + " · LAPSED - " + ReasonNoSuchDebt, day);
        }

        // ---------------------------------------------------------------- keep-off

        /// <summary>This house keeps off this block until this day - a complied
        /// warning, a line. Read by the racket's two choke points and the mind.</summary>
        public void KeepOff(int house, TerritoryBlockId blockId, int untilDay)
        {
            if (house < 0 || !blockId.IsValid)
                return;
            var key = (house, blockId.Value);
            keepOff.TryGetValue(key, out var standing);
            keepOff[key] = untilDay > standing ? untilDay : standing;
        }

        public bool IsKeptOff(int house, TerritoryBlockId blockId, int day) =>
            blockId.IsValid && keepOff.TryGetValue((house, blockId.Value), out var until) &&
            day < until;

        /// <summary>The day the keep-off lifts, or -1 when there is none.</summary>
        public int KeptOffUntil(int house, TerritoryBlockId blockId) =>
            blockId.IsValid && keepOff.TryGetValue((house, blockId.Value), out var until)
                ? until
                : -1;

        /// <summary>Every standing keep-off, for the probe and the runtime's sweep.
        /// </summary>
        public void CollectKeepOffs(
            List<(int house, TerritoryBlockId block, int untilDay)> into)
        {
            into?.Clear();
            if (into == null)
                return;
            foreach (var pair in keepOff)
                into.Add((pair.Key.house, new TerritoryBlockId(pair.Key.block), pair.Value));
        }

        readonly List<(int, string)> staleKeepOffs = new List<(int, string)>();

        void SweepKeepOffs(int day)
        {
            staleKeepOffs.Clear();
            foreach (var pair in keepOff)
                if (day >= pair.Value)
                    staleKeepOffs.Add(pair.Key);
            for (var i = 0; i < staleKeepOffs.Count; i++)
                keepOff.Remove(staleKeepOffs[i]);
            for (var i = lines.Count - 1; i >= 0; i--)
                if (day >= lines[i].UntilDay)
                    lines.RemoveAt(i);
            for (var i = pacts.Count - 1; i >= 0; i--)
                if (day >= pacts[i].UntilDay)
                    pacts.RemoveAt(i);
        }

        // ------------------------------------------------------------------- pacts

        /// <summary>
        /// THE PACTS ARE HONOURED BY THE BOOK (DIPL-007), after the stances have
        /// landed: for every war that landed this midnight on a party to a standing
        /// pact - declared by a house, not by another pact - the partner's pending
        /// stance toward the declarer is written at War for the NEXT midnight, flagged
        /// the pact's own, so nothing cascades. A partner that cannot pay for a war
        /// does not honour: the pact is struck, the abandoned party is owed for it,
        /// and every house hears. The player always honours - he signed knowing it.
        /// </summary>
        public void HonourPacts(Underworld world, List<StanceLanded> landed, int day,
            HouseRelationsConfig relations)
        {
            if (world == null || landed == null || pacts.Count == 0)
                return;
            relations = relations ?? HouseRelationsConfig.Default;
            for (var l = 0; l < landed.Count; l++)
            {
                var war = landed[l];
                if (war.Stance != Stance.War || war.ByPact)
                    continue;
                var declarer = war.By;
                var victim = war.Against;
                for (var i = pacts.Count - 1; i >= 0; i--)
                {
                    var pact = pacts[i];
                    if (!pact.Names(victim) || day >= pact.UntilDay)
                        continue;
                    var partner = pact.PartnerOf(victim);
                    if (partner == declarer)
                        continue;
                    var house = world.Of(partner);
                    if (house == null || house.Finished)
                        continue;
                    if (world.Relations.StanceBetween(partner, declarer) == Stance.War)
                        continue;

                    var canPay = house.IsPlayer ||
                                 HouseRelations.Endurance(house.Runner.Accounts.Safe,
                                     Wages.DailyPayroll(house.Roster)) >= relations.MinWarDays;
                    if (canPay)
                    {
                        world.Relations.SetPending(partner, declarer, Stance.War, byPact: true);
                        PrintEverywhere(world,
                            Gangs.GangCatalog.Names[partner] + " stands with " +
                            Gangs.GangCatalog.Names[victim] + " against " +
                            Gangs.GangCatalog.Names[declarer], day);
                        continue;
                    }

                    pacts.RemoveAt(i);
                    world.Relations.Note(victim, partner, GrievanceKind.PactBroken, day);
                    PrintEverywhere(world,
                        Gangs.GangCatalog.Names[partner] + " " + ReasonLeftThemToIt + " - " +
                        Gangs.GangCatalog.Names[victim] + " were sworn to them", day);
                }
            }
        }

        /// <summary>A line in EVERY house's book - a pact broken is the city's news.</summary>
        public static void PrintEverywhere(Underworld world, string line, int day)
        {
            if (world == null)
                return;
            var note = new Incident(-1, line, IncidentKind.AWordBetweenHouses, day, "", 0,
                line);
            for (var g = 0; g < world.Count; g++)
                world.Of(g)?.Runner.Incidents.Add(note);
        }

        // ------------------------------------------------------------------- lines

        /// <summary>Whether a standing line names this house on this street.</summary>
        public bool Crosses(int house, TerritoryBlockId blockId, int day)
        {
            if (!blockId.IsValid)
                return false;
            for (var i = 0; i < lines.Count; i++)
                if (lines[i].Names(house) && lines[i].Block == blockId.Value &&
                    day < lines[i].UntilDay)
                    return true;
            return false;
        }

        /// <summary>
        /// A DOOR TAKEN OR HIT ACROSS A LINE IS OWED FOR ON TOP (DIPL-006). Called
        /// beside the ordinary note wherever a door switches or is attacked; answers
        /// whether a line was crossed.
        /// </summary>
        public bool NoteCrossing(HouseRelations relations, int aggrieved, int offender,
            TerritoryBlockId blockId, int day)
        {
            if (relations == null || aggrieved == offender || !Crosses(offender, blockId, day))
                return false;
            relations.Note(aggrieved, offender, GrievanceKind.LineCrossed, day);
            return true;
        }

        // -------------------------------------------------------------------- save

        /// <summary>The whole book, flat: arrays only, so a file with no block reads
        /// as an empty book and nothing nested comes back as {}.</summary>
        /// <summary>Every standing line, flat.</summary>
        public void CollectLines(List<LineDto> into)
        {
            into?.Clear();
            if (into == null)
                return;
            for (var i = 0; i < lines.Count; i++)
                into.Add(new LineDto
                {
                    a = lines[i].A,
                    b = lines[i].B,
                    block = lines[i].Block,
                    untilDay = lines[i].UntilDay,
                });
        }

        public void CollectPacts(List<PactDto> into)
        {
            into?.Clear();
            if (into == null)
                return;
            for (var i = 0; i < pacts.Count; i++)
                into.Add(new PactDto { a = pacts[i].A, b = pacts[i].B, untilDay = pacts[i].UntilDay });
        }

        public void RestorePacts(PactDto[] rows)
        {
            pacts.Clear();
            for (var i = 0; rows != null && i < rows.Length; i++)
                if (rows[i] != null)
                    pacts.Add(new Pact { A = rows[i].a, B = rows[i].b, UntilDay = rows[i].untilDay });
        }

        public void RestoreLines(LineDto[] rows)
        {
            lines.Clear();
            for (var i = 0; rows != null && i < rows.Length; i++)
                if (rows[i] != null && !string.IsNullOrEmpty(rows[i].block))
                    lines.Add(new Line
                    {
                        A = rows[i].a,
                        B = rows[i].b,
                        Block = rows[i].block,
                        UntilDay = rows[i].untilDay,
                    });
        }

        public void Collect(List<ProposalDto> rows, List<KeepOffDto> offs, out int next)
        {
            next = nextId;
            if (rows != null)
            {
                rows.Clear();
                for (var i = 0; i < proposals.Count; i++)
                {
                    var p = proposals[i];
                    rows.Add(new ProposalDto
                    {
                        id = p.Id,
                        from = p.From,
                        to = p.To,
                        kind = (int)p.Kind,
                        money = p.Terms.Money,
                        kilos = p.Terms.Kilos,
                        blocks = p.Terms.Blocks.ToArray(),
                        characterId = p.Terms.CharacterId,
                        third = p.Terms.Third,
                        days = p.Terms.Days,
                        label = p.Terms.Label,
                        day = p.Day,
                        expiresDay = p.ExpiresDay,
                        status = (int)p.Status,
                        answer = p.Answer,
                        escrow = p.Escrow,
                        escrowDirty = p.EscrowDirty,
                        clearedAtFiling = p.ClearedAtFiling,
                        envoy = p.Envoy,
                        envoyHalfSteps = p.EnvoyHalfSteps,
                        inTransit = p.InTransit,
                    });
                }
            }
            if (offs == null)
                return;
            offs.Clear();
            foreach (var pair in keepOff)
                offs.Add(new KeepOffDto
                {
                    house = pair.Key.house,
                    block = pair.Key.block,
                    untilDay = pair.Value,
                });
        }

        /// <summary>The load boundary. Everything the book held is replaced; a null
        /// row set reads as none.</summary>
        public void RestoreFrom(ProposalDto[] rows, KeepOffDto[] offs, int next)
        {
            proposals.Clear();
            keepOff.Clear();
            lines.Clear();
            pacts.Clear();
            nextId = next > 0 ? next : 1;

            for (var i = 0; rows != null && i < rows.Length; i++)
            {
                var row = rows[i];
                if (row == null || !System.Enum.IsDefined(typeof(ProposalKind), row.kind) ||
                    !System.Enum.IsDefined(typeof(ProposalStatus), row.status))
                    continue;
                var terms = new ProposalTerms
                {
                    Money = row.money,
                    Kilos = row.kilos,
                    CharacterId = row.characterId,
                    Third = row.third,
                    Days = row.days,
                    Label = row.label ?? "",
                };
                for (var b = 0; row.blocks != null && b < row.blocks.Length; b++)
                    if (!string.IsNullOrEmpty(row.blocks[b]))
                        terms.Blocks.Add(row.blocks[b]);
                proposals.Add(new Proposal
                {
                    Id = row.id,
                    From = row.from,
                    To = row.to,
                    Kind = (ProposalKind)row.kind,
                    Terms = terms,
                    Day = row.day,
                    ExpiresDay = row.expiresDay,
                    Status = (ProposalStatus)row.status,
                    Answer = row.answer ?? "",
                    Escrow = row.escrow,
                    EscrowDirty = row.escrowDirty,
                    ClearedAtFiling = row.clearedAtFiling,
                    Envoy = row.envoy,
                    EnvoyHalfSteps = row.envoyHalfSteps,
                    InTransit = row.inTransit,
                });
                if (row.id >= nextId)
                    nextId = row.id + 1;
            }

            for (var i = 0; offs != null && i < offs.Length; i++)
            {
                var off = offs[i];
                if (off == null || string.IsNullOrEmpty(off.block))
                    continue;
                keepOff[(off.house, off.block)] = off.untilDay;
            }
        }
    }

    [System.Serializable]
    public sealed class ProposalDto
    {
        public int id;
        public int from;
        public int to;
        public int kind;
        public int money;
        public int kilos;
        public string[] blocks;
        public int characterId;
        public int third;
        public int days;
        public string label;
        public int day;
        public int expiresDay;
        public int status;
        public string answer;
        public int escrow;
        public int escrowDirty;
        public int clearedAtFiling;
        public int envoy;
        public int envoyHalfSteps;
        public bool inTransit;
    }

    [System.Serializable]
    public sealed class KeepOffDto
    {
        public int house;
        public string block;
        public int untilDay;
    }

    [System.Serializable]
    public sealed class LineDto
    {
        public int a;
        public int b;
        public string block;
        public int untilDay;
    }

    [System.Serializable]
    public sealed class PactDto
    {
        public int a;
        public int b;
        public int untilDay;
    }
}
