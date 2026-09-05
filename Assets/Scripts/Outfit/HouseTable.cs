using System.Collections.Generic;
using LivingCity.Gangs;
using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    /// <summary>
    /// What one line between two houses IS. Five of them and no more: the map, the rail
    /// and the legend all draw off this one list, so a standing can never be painted in
    /// a colour the legend does not name.
    /// </summary>
    public enum TieKind
    {
        Peace,
        Truce,
        War,
        Pact,
        Tribute,
    }

    /// <summary>One standing between two houses: what it is, and the words the line
    /// carries in the middle of it.</summary>
    public readonly struct HouseTie
    {
        public HouseTie(TieKind kind, string what)
        {
            Kind = kind;
            What = what;
        }

        public TieKind Kind { get; }

        /// <summary>The label the line wears - "war · they hold two of ours",
        /// "pact · 22 days left". Never a figure the player could not have earned.
        /// </summary>
        public string What { get; }
    }

    /// <summary>How a key is dressed: the doing verb, the one that cannot be taken
    /// back, the one that does not commit, and the one that undoes something.</summary>
    public enum MoveFace
    {
        Dark,
        Red,
        Outline,
        Ghost,
    }

    /// <summary>
    /// One word we may say to a house today. A move is never invented by the sheet: it
    /// is a <see cref="ProposalKind"/> the gateway would take, a war declaration, or an
    /// answer to something of theirs already lying in our inbox.
    /// </summary>
    public sealed class TableMove
    {
        /// <summary>The key's word - SUE FOR PEACE, SEND A BILL.</summary>
        public string Label = "";

        /// <summary>The wording panel's head, and the line the record is written in.
        /// </summary>
        public string Head = "";

        /// <summary>The verb on the send key - "Send it", "Declare it", "Give it".
        /// </summary>
        public string Send = "SEND IT";

        /// <summary>What the move actually costs and buys, in the reader's own words.
        /// </summary>
        public string Terms = "";

        /// <summary>The War Room groups its keys; the Table does not.</summary>
        public string Group = "Talk";

        public MoveFace Face = MoveFace.Outline;
        public bool SendIsRed;

        /// <summary>The word this key files, or None when the key is one of the three
        /// that are not proposals.</summary>
        public ProposalKind Word = ProposalKind.None;

        /// <summary>True for DECLARE WAR - a stance, not a proposal.</summary>
        public bool War;

        /// <summary>An answer to a proposal of theirs: the id to accept, refuse or
        /// ambush. -1 when the key is not an answer.</summary>
        public int AnswerTo = -1;
        public bool Accepts;
        public bool Ambushes;

        /// <summary>Which rows the wording panel has to carry for this word.</summary>
        public bool NeedsMoney;
        public bool NeedsStreet;
        public bool NeedsThird;

        /// <summary>The most the money row may be wound to - a bill's ceiling, the
        /// street's tribute figure, or what the safe holds.</summary>
        public int MoneyCeiling;
    }

    /// <summary>A word the sheet is showing but will not take a press on, and the
    /// gateway's own reason. Never hidden: a row that has vanished tells the reader
    /// nothing about why.</summary>
    public readonly struct ShutMove
    {
        public ShutMove(string label, string why)
        {
            Label = label;
            Why = why;
        }

        public string Label { get; }
        public string Why { get; }
    }

    /// <summary>One line of the record between two houses.</summary>
    public readonly struct TableRecordLine
    {
        public TableRecordLine(string when, string what, bool fresh)
        {
            When = when;
            What = what;
            Fresh = fresh;
        }

        public string When { get; }
        public string What { get; }

        /// <summary>Written today - the stamp goes red.</summary>
        public bool Fresh { get; }
    }

    /// <summary>
    /// Everything one house's card, dossier and rail row print. Filled from the books
    /// and nothing else: what is not known reads Unknown rather than a figure the
    /// player could not have earned.
    /// </summary>
    public sealed class HouseReading
    {
        public int GangId;
        public string Name = "";

        /// <summary>The drawer's own file number - R-101 and up.</summary>
        public string Code = "";
        public string HouseNumber = "";
        public string Boss = "";

        /// <summary>The word over the standing: WAR, TRUCE, PEACE, or SWORN when a
        /// pact stands over the peace.</summary>
        public string Stance = "";
        public TieKind Tie = TieKind.Peace;

        /// <summary>"declared day 38 · three days in", "pact sworn · 22 days left".
        /// </summary>
        public string StanceSince = "";

        /// <summary>What a stance DOES, in the rules' own words - the hover note.
        /// </summary>
        public string StanceRule = "";

        /// <summary>The one line the card carries under the name: the thing about this
        /// house that matters today.</summary>
        public string Flag = "";

        /// <summary>Power 0-100, or negative when we have no eyes inside.</summary>
        public int Power = -1;
        public string PowerText = "";
        public string PowerNote = "";

        public int Blocks;
        public int BlocksTotal;
        public int Capos;
        public bool CaposKnown;

        public int Taken;
        public string TakenText = "";

        /// <summary>What they owe us a cycle, and what we owe them. Only one of the
        /// two is ever a figure.</summary>
        public int TheyOwe;
        public int WeOwe;
        public bool Overdue;
        public string OwedText = "";

        public string Front = "";

        public string Personality = "";
        public string Temper = "";
        public string KeepsHisWord = "";
        public string FoundAtNight = "";

        /// <summary>Their open word in our inbox, if any.</summary>
        public bool TheyAsk;
        public string AskChip = "";
        public string AskWhen = "";
        public string AskBody = "";
        public string Note = "";

        public readonly List<TableRecordLine> Record = new List<TableRecordLine>();
    }

    /// <summary>
    /// THE TABLE'S OWN BOOK: what the FAMILIES sheet reads, derived from the city's
    /// books and from nothing else. Both directions of the screen - the relationship
    /// map and the war room - draw off this one class, so the two can never disagree
    /// about a standing, a figure or a reason.
    ///
    /// Nothing here decides anything. Legality is asked of <see cref="HouseDiplomacy"/>
    /// exactly as the gateway asks it, so a key the sheet greys is a key the gateway
    /// would have refused, in the gateway's own words.
    /// </summary>
    public static partial class HouseTable
    {
        /// <summary>The drawer's file number for a house.</summary>
        public static string CodeOf(int gangId) => "R-" + (100 + gangId).ToString("000");

        // ------------------------------------------------------------------- the ties

        /// <summary>
        /// The standing between two houses, whoever they are. The order is fixed: a war
        /// is louder than a pact, a pact is louder than money, money is louder than a
        /// truce. Two houses can be at truce AND owe each other; the line says the
        /// money, because the money is the thing about to change.
        /// </summary>
        public static HouseTie Between(Underworld world, int a, int b, int day)
        {
            if (world == null || a == b)
                return new HouseTie(TieKind.Peace, "nothing between them");

            var stance = world.Relations.StanceBetween(a, b);
            if (stance == Stance.War)
                return new HouseTie(TieKind.War, "war" + WarNote(world, a, b));

            var pacts = world.Diplomacy.Pacts;
            for (var i = 0; i < pacts.Count; i++)
            {
                var pact = pacts[i];
                if (!pact.Names(a) || pact.PartnerOf(a) != b || pact.UntilDay <= day)
                    continue;
                var left = pact.UntilDay - day;
                return new HouseTie(TieKind.Pact,
                    "pact · " + left + (left == 1 ? " day left" : " days left"));
            }

            var owed = LevyBetween(world, a, b, out var payer, out var payee, out var late);
            if (owed > 0)
                return new HouseTie(TieKind.Tribute,
                    NameOf(payer) + " pays " + NameOf(payee) + " " + Money(owed) +
                    " a cycle" + (late ? " · overdue" : ""));

            if (stance == Stance.Truce)
                return new HouseTie(TieKind.Truce, "truce");

            return new HouseTie(TieKind.Peace, "peace · nothing between them");
        }

        /// <summary>What one house holds against the other in a war, when the tally is
        /// worth printing on the line.</summary>
        static string WarNote(Underworld world, int a, int b)
        {
            var aHouse = world.Of(a);
            var bHouse = world.Of(b);
            var aLost = aHouse != null ? aHouse.Runner.MenLostTo(b) : 0;
            var bLost = bHouse != null ? bHouse.Runner.MenLostTo(a) : 0;
            var most = aLost >= bLost ? aLost : bLost;
            if (most <= 0)
                return "";
            return " · " + most + (most == 1 ? " man down" : " men down");
        }

        /// <summary>The levy running between the pair, whichever way it runs.</summary>
        static int LevyBetween(Underworld world, int a, int b, out int payer, out int payee,
            out bool overdue)
        {
            payer = a;
            payee = b;
            overdue = false;

            var aHouse = world.Of(a);
            var levy = aHouse?.Runner.Tribute.For(b);
            if (levy != null && levy.Amount > 0)
            {
                overdue = levy.Overdue;
                return levy.Amount;
            }

            var bHouse = world.Of(b);
            levy = bHouse?.Runner.Tribute.For(a);
            if (levy != null && levy.Amount > 0)
            {
                payer = b;
                payee = a;
                overdue = levy.Overdue;
                return levy.Amount;
            }
            return 0;
        }

        static string NameOf(int gangId)
        {
            var gangs = GangRegistry.Gangs;
            for (var i = 0; i < gangs.Count; i++)
                if (gangs[i].Id == gangId)
                    return gangs[i].IsPlayer ? "we" : gangs[i].Name.ToLowerInvariant();
            return "they";
        }

        static string Money(int amount) => "$" + amount.ToString("N0");

        // ---------------------------------------------------------------- the reading

        /// <summary>
        /// One house's sheet, filled. <paramref name="power"/> answers what the street
        /// ledger knows about a house's strength and is allowed to answer that it does
        /// not know - the sheet then prints Unknown rather than inventing a figure.
        /// </summary>
        public static void Read(Underworld world, Gang gang, int mine, int day,
            IReadOnlyList<Turf.Holding> holdings, System.Func<int, int> power,
            HouseReading into)
        {
            if (into == null || gang == null)
                return;

            into.GangId = gang.Id;
            into.Name = gang.Name;
            into.Code = CodeOf(gang.Id);
            into.Record.Clear();

            var house = world?.Of(gang.Id);
            var boss = house?.Roster?.FindBoss();
            into.Boss = boss != null ? boss.FullName
                : gang.Members.Count > 0 ? gang.Members[0].FullName
                : "persons unknown";

            // ---- the standing ----
            var stance = world != null ? world.Relations.StanceBetween(mine, gang.Id) : Stance.Peace;
            var tie = Between(world, mine, gang.Id, day);
            into.Tie = tie.Kind;
            into.Stance = tie.Kind == TieKind.Pact ? "SWORN"
                : stance == Stance.War ? "WAR"
                : stance == Stance.Truce ? "TRUCE" : "PEACE";
            into.StanceSince = tie.What;
            into.StanceRule = StanceRule(stance, tie.Kind == TieKind.Pact);

            // ---- the reading ----
            into.Power = power != null ? power(gang.Id) : -1;
            var ourPower = power != null ? power(mine) : -1;
            into.PowerText = into.Power < 0 ? "Unknown" : into.Power.ToString();
            into.PowerNote = into.Power < 0
                ? "No eyes inside. Reconnaissance is work, not a birthright."
                : ourPower < 0
                    ? "Counted on the streets they are paid for."
                    : into.Power > ourPower
                        ? "Stronger than us by " + (into.Power - ourPower) + "."
                        : into.Power < ourPower
                            ? "Weaker than us by " + (ourPower - into.Power) + "."
                            : "As strong as we are, to the point.";

            into.Blocks = Turf.CountOf(holdings, gang.Id);
            into.BlocksTotal = holdings != null ? holdings.Count : 0;

            var capos = 0;
            if (house?.Roster != null)
            {
                foreach (var man in house.Roster.Members)
                    if (!man.Gone && man.Rank == Rank.Lieutenant)
                        capos++;
                into.CaposKnown = true;
            }
            else
            {
                foreach (var man in gang.Members)
                    if (man.Lieutenant)
                        capos++;
                into.CaposKnown = capos > 0;
            }
            into.Capos = capos;

            var us = world?.Of(mine);
            into.Taken = us != null ? us.Runner.MenLostTo(gang.Id) : 0;
            into.TakenText = into.Taken == 0 ? "nobody of ours"
                : into.Taken == 1 ? "one of ours"
                : into.Taken + " of ours";

            into.WeOwe = us != null ? us.Runner.Tribute.OwedTo(gang.Id) : 0;
            var theirLevy = house?.Runner.Tribute.For(mine);
            into.TheyOwe = theirLevy?.Amount ?? 0;
            into.Overdue = theirLevy != null && theirLevy.Overdue;
            into.OwedText = into.WeOwe > 0
                ? "we kick up " + Money(into.WeOwe) + " a cycle"
                : into.TheyOwe > 0
                    ? Money(into.TheyOwe) + (into.Overdue ? " · overdue" : " a cycle")
                    : "nothing";

            var front = GangRegistry.FrontBusinessOf(gang.Id);
            var books = GangRegistry.FrontBooksOf(gang.Id);
            into.Front = front ? front.BusinessName
                : books != null
                    ? books.Sign + (string.IsNullOrEmpty(books.Address) ? "" : ", " + books.Address)
                    : "nobody has found their door";

            // ---- the man ----
            FillTheMan(boss, into, front || books != null);

            // ---- what they ask ----
            FillTheAsk(world, gang.Id, mine, day, into);

            // ---- the flag the card wears ----
            into.Flag = into.Taken > 0
                ? (into.Taken == 1 ? "took one of ours" : "took " + into.Taken + " of ours")
                : into.TheyOwe > 0 && into.Overdue
                    ? Money(into.TheyOwe) + " · overdue"
                    : into.TheyAsk ? "asks for an answer"
                    : tie.Kind == TieKind.Pact ? tie.What
                    : into.Power < 0 ? "no eyes inside"
                    : tie.What;

            // ---- the record ----
            FillRecord(world, gang.Id, mine, day, into);
        }

        /// <summary>
        /// What the boss IS, in the clerk's words. Personality's own bands do the work -
        /// the sheet never prints the number, and it never invents a sentence the books
        /// cannot stand behind.
        /// </summary>
        static void FillTheMan(Character boss, HouseReading into, bool hasDoor)
        {
            if (boss == null)
            {
                into.Personality =
                    "Nobody of ours has sat across from him. What we hold is a name and " +
                    "a door.";
                into.Temper = "unknown";
                into.KeepsHisWord = "unknown";
                into.FoundAtNight = "unknown";
                return;
            }

            var temper = Personality.Get(boss, PersonalityTrait.Temper);
            var discipline = Personality.Get(boss, PersonalityTrait.Discipline);
            var greed = Personality.Get(boss, PersonalityTrait.Greed);
            var ambition = Personality.Get(boss, PersonalityTrait.Ambition);
            var courage = Personality.Get(boss, PersonalityTrait.Courage);

            into.Temper = Personality.Band(PersonalityTrait.Temper, temper) +
                          (temper > 80 ? " — he goes first and thinks after"
                              : temper <= 20 ? " — nothing moves him" : "");

            into.KeepsHisWord = discipline <= 20 ? "not once you are out of the room"
                : discipline <= 40 ? "rarely"
                : discipline <= 60 ? "when it costs him nothing"
                : discipline <= 80 ? "he keeps it"
                : "to the letter";

            into.FoundAtNight = !hasDoor ? "unknown"
                : into.Front + (discipline > 60 ? ", never late" : ", when he is not owed money");

            // Two sentences: what he is, and what that does to us.
            var what = courage > 60
                ? (temper > 60 ? "Does not bluff and does not answer telephones."
                    : "Steady, and he has never blinked at us.")
                : (temper > 60 ? "Loud, and frightened underneath it."
                    : "Careful, and slow to put his own men in front.");
            var wants = ambition > 60
                ? " He wants the whole of it and says so."
                : greed > 60
                    ? " Charming on the doorstep and empty in the books."
                    : " He wants what he has, kept.";
            var word = discipline > 60
                ? " Reads terms twice and keeps them to the letter."
                : discipline <= 40
                    ? " Promises Friday to everybody and means it while he is saying it."
                    : "";
            into.Personality = what + wants + word;
        }

        /// <summary>Their open word in our inbox: the wire that came in, and what it
        /// asks for.</summary>
        static void FillTheAsk(Underworld world, int gangId, int mine, int day,
            HouseReading into)
        {
            into.TheyAsk = false;
            into.AskChip = "";
            into.AskWhen = "";
            into.AskBody = "";
            into.Note = "";

            var book = world?.Diplomacy;
            if (book == null)
            {
                into.Note = "Nothing has passed between the houses.";
                return;
            }

            var inbox = new List<Proposal>();
            book.OpenFor(mine, inbox);
            for (var i = 0; i < inbox.Count; i++)
            {
                var proposal = inbox[i];
                if (proposal.From != gangId)
                    continue;
                into.TheyAsk = true;
                into.AskChip = AskChip(proposal.Kind);
                into.AskWhen = "day " + proposal.Day +
                               (proposal.InTransit ? " · his man is on the road"
                                   : proposal.Envoy >= 0 ? " · his man is at our door"
                                   : "") +
                               " · lapses day " + proposal.ExpiresDay;
                into.AskBody = HouseDiplomacy.Describe(proposal).ToUpperInvariant() + ".";
                break;
            }

            into.Note = into.TheyAsk
                ? "They asked first, which means they are losing something we cannot see yet."
                : into.Taken > 0
                    ? "Whatever we say to this house goes in a man's hand."
                    : into.Power < 0
                        ? "Put one man inside and the rest of this panel fills itself."
                        : "Nothing waiting. The last word out of this house was ours.";
        }

        static string AskChip(ProposalKind kind) => kind switch
        {
            ProposalKind.OfferTruce => "Offers a truce",
            ProposalKind.OfferPeace => "Offers peace",
            ProposalKind.Warn => "Warns us off",
            ProposalKind.Threaten => "Threatens us",
            ProposalKind.Bill => "Bills us",
            ProposalKind.TributeTerms => "Asks for terms",
            ProposalKind.Ransom => "Asks a ransom",
            ProposalKind.Line => "Draws a line",
            ProposalKind.Pact => "Offers a pact",
            ProposalKind.JoinWar => "Asks for our men",
            _ => "Asks something",
        };

        static void FillRecord(Underworld world, int gangId, int mine, int day,
            HouseReading into)
        {
            var book = world?.Diplomacy;
            if (book == null)
                return;
            var between = new List<Proposal>();
            book.Between(mine, gangId, between);
            for (var i = between.Count - 1; i >= 0 && into.Record.Count < 12; i--)
            {
                var entry = between[i];
                var verdict = entry.Status == ProposalStatus.Open
                    ? (entry.InTransit ? "on the road" : "waiting")
                    : entry.Status == ProposalStatus.Accepted ? "accepted"
                    : entry.Status.ToString().ToLowerInvariant();
                var answer = string.IsNullOrEmpty(entry.Answer) ? "" : " — " + entry.Answer;
                into.Record.Add(new TableRecordLine(
                    "Day " + entry.Day,
                    (entry.From == mine ? "We said: " : "They said: ") +
                    HouseDiplomacy.Describe(entry) + ". " +
                    char.ToUpperInvariant(verdict[0]) + verdict.Substring(1) + answer + ".",
                    entry.Day == day));
            }
        }

        public static string StanceRule(Stance stance, bool sworn) => stance switch
        {
            Stance.War =>
                "WAR — on sight. Their men engage ours anywhere in the city, and ours " +
                "theirs. A change of stance takes effect at midnight, never mid-day.",
            Stance.Truce =>
                "TRUCE — territorial. Their men engage ours caught inside their " +
                "territory, and ours engage theirs on ours. Neutral ground stays quiet. " +
                "Changes take effect at midnight.",
            _ => sworn
                ? "PEACE, SWORN — no engagement, and a war on either house is a war on " +
                  "both while the pact runs. Breaking it is heard at every table by morning."
                : "PEACE — no engagement. Their men and ours pass in the street, claimed " +
                  "ground or not. Strength reads Unknown until we have eyes inside.",
        };
    }
}
