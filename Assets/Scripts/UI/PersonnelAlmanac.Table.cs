using System.Collections.Generic;
using TMPro;
using UnityEngine;
using LivingCity.Outfit;
using LivingCity.Personnel;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// THE TABLE (EPIC 42, DIPL-010) - one house's sheet under the FAMILIES index:
    /// what they have asked us and our answer to it, and every word we can say to them
    /// - war declared, truce and peace offered, a warning, a threat, a bill, terms on
    /// the tribute, a line across the streets where we touch, a pact, a war joined -
    /// by telephone or carried in person by one of our lieutenants. Under it, THE
    /// RECORD: the last words between the two houses and what came of each.
    ///
    /// Every key calls the same HouseOps door a rival's mind does and prints the
    /// gateway's own refusal. No figure here is invented: the money is the safe's, the
    /// streets are the holdings the map already paints, the houses are the rolodex.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        /// <summary>The house whose table is open, or -1 for the card index.</summary>
        int tableFor = -1;

        /// <summary>What the next offer carries, in dollars.</summary>
        int tableMoney;

        /// <summary>Which of our streets a word or a line names.</summary>
        int tableStreet;

        /// <summary>Which house a pact or a joined war is against.</summary>
        int tableThird;

        /// <summary>Carried in person by which of our lieutenants, or by telephone.</summary>
        bool tableInPerson;
        int tableEnvoy;

        /// <summary>The word whose panel is open under the keys, or None; DECLARE WAR
        /// has its own. A key is the question, the panel under it the terms.</summary>
        ProposalKind tableWord = ProposalKind.None;
        bool tableWar;

        /// <summary>What the last build measured, for the money a word may carry:
        /// the most a bill may ask today, and the tribute figure the street prices.</summary>
        int tableCeiling;
        int tableLevy;

        /// <summary>The gateway's last word about what was asked from this sheet.</summary>
        string tableNote = "";

        const float TableRowH = 22f;
        const float TableKeyH = 26f;
        const int TableMoneyStep = 500;
        const int TableRecordShown = 12;

        readonly List<Proposal> tableInbox = new List<Proposal>();
        readonly List<Proposal> tableRecord = new List<Proposal>();
        readonly List<Territory.TerritoryBlockId> tableStreets = new List<Territory.TerritoryBlockId>();
        readonly List<Gangs.Gang> tableThirds = new List<Gangs.Gang>();
        readonly List<Character> tableEnvoys = new List<Character>();

        void OpenTable(int gangId)
        {
            tableFor = gangId;
            tableNote = "";
            tableWord = ProposalKind.None;
            tableWar = false;
            tableMoney = 0;
            familiesScroll = 0f;
            dirty = true;
        }

        void CloseTable()
        {
            tableFor = -1;
            tableNote = "";
            tableWord = ProposalKind.None;
            tableWar = false;
            familiesScroll = 0f;
            dirty = true;
        }

        /// <summary>Whether that house has a proposal of theirs waiting in our inbox.
        /// </summary>
        bool TheyAsk(int gangId)
        {
            var book = outfit ? outfit.Diplomacy : null;
            if (book == null)
                return false;
            book.OpenFor(Gangs.GangCatalog.PlayerGangId, tableInbox);
            for (var i = 0; i < tableInbox.Count; i++)
                if (tableInbox[i].From == gangId && !tableInbox[i].InTransit)
                    return true;
            return false;
        }

        void BuildTable(IReadOnlyList<Gangs.Gang> gangs)
        {
            Gangs.Gang house = null;
            foreach (var gang in gangs)
                if (gang.Id == tableFor)
                    house = gang;
            var book = outfit ? outfit.Diplomacy : null;
            if (house == null || book == null || !outfit)
            {
                CloseTable();
                return;
            }

            var mine = Gangs.GangCatalog.PlayerGangId;
            var them = new Territory.TerritoryGangId(house.Id);
            var day = outfit.Campaign.Day;
            var w = PageWidth;
            var y = 0f;
            var sheet = familiesContent;

            // ---- the head ----
            LedgerV2.Button(sheet, "< BACK TO THE INDEX", 0f, y, 200f, TableKeyH,
                CloseTable, red: false, size: 10f, outline: true);
            var title = Line(sheet, LedgerStyle.Condensed, 22f, LedgerV2.Ink, 216f, y - 2f,
                w - 216f, LineBox(22f), "THE TABLE · " + house.Name.ToUpperInvariant());
            title.characterSpacing = 2f;
            y -= TableKeyH + 10f;

            var current = outfit.StanceWith(house.Id);
            var hasPending = outfit.TryGetPendingStance(house.Id, out var pending);
            var agreed = outfit.Relations != null &&
                         outfit.Relations.TryGetAgreed(mine, house.Id, out var agreedStance, out var broken)
                ? (broken ? " · agreed, then broken" : " · " + LedgerText.StanceLabel(agreedStance) + " agreed for midnight")
                : "";
            y = TableRow(sheet, y, "STANDING",
                LedgerText.StanceLabel(current) +
                (hasPending ? " → " + LedgerText.StanceLabel(pending) : "") + agreed,
                hasPending ? LedgerV2.Red : LedgerV2.Ink);

            var weOwe = outfit.Tribute.OwedTo(house.Id);
            var theirBook = Underworld.Current?.Of(house.Id);
            var theyOwe = theirBook != null ? theirBook.Runner.Tribute.OwedTo(mine) : 0;
            y = TableRow(sheet, y, "TRIBUTE",
                weOwe > 0 ? "we kick up " + LedgerText.Cash(weOwe) + " a cycle"
                : theyOwe > 0 ? "they kick up " + LedgerText.Cash(theyOwe) + " a cycle"
                : "nobody is under anybody",
                weOwe > 0 ? LedgerV2.Red : LedgerV2.Ink);
            var taken = outfit.Runner.MenLostTo(house.Id);
            y = TableRow(sheet, y, "TAKEN",
                taken == 0 ? "nobody of ours" : taken + " of our men",
                taken > 0 ? LedgerV2.Red : LedgerV2.Muted);
            y = TableRow(sheet, y, "PACT",
                book.HasPact(mine, house.Id, day) ? "sworn - a war on either is a war on both"
                    : "none", book.HasPact(mine, house.Id, day) ? LedgerV2.Ink : LedgerV2.Muted);

            if (!string.IsNullOrEmpty(tableNote))
            {
                var note = Line(sheet, LedgerStyle.MonoItalic, 12f, LedgerV2.Red, 0f, y, w,
                    LineBox(12f), tableNote);
                note.overflowMode = TextOverflowModes.Ellipsis;
                y -= TableRowH;
            }

            // ---- what they ask ----
            y = TableBand(sheet, y, "THEY ASK");
            book.OpenFor(mine, tableInbox);
            var asked = 0;
            for (var i = 0; i < tableInbox.Count; i++)
            {
                var proposal = tableInbox[i];
                if (proposal.From != house.Id)
                    continue;
                asked++;
                var line = HouseDiplomacy.Describe(proposal) +
                           (proposal.InTransit ? " · his man is on the road"
                               : proposal.Envoy >= 0 ? " · his man is at our door" : "") +
                           " · lapses day " + proposal.ExpiresDay;
                var text = Line(sheet, LedgerStyle.MonoBold, 12f, LedgerV2.Ink, 0f, y,
                    w - 340f, LineBox(12f), line);
                text.overflowMode = TextOverflowModes.Ellipsis;
                var id = proposal.Id;
                if (!proposal.InTransit)
                {
                    LedgerV2.Button(sheet, "ACCEPT", w - 330f, y + 2f, 100f, TableKeyH,
                        () => Answer(id, true), red: false, size: 10f, outline: false);
                    LedgerV2.Button(sheet, "REFUSE", w - 222f, y + 2f, 100f, TableKeyH,
                        () => Answer(id, false), red: false, size: 10f, outline: true);
                    if (proposal.Envoy >= 0)
                        LedgerV2.Button(sheet, "AMBUSH", w - 114f, y + 2f, 110f, TableKeyH,
                            () => Ambush(id), red: true, size: 10f, outline: false);
                }
                y -= TableKeyH + 6f;
            }
            if (asked == 0)
            {
                Line(sheet, LedgerStyle.MonoItalic, 12f, LedgerV2.Muted, 0f, y, w,
                    LineBox(12f), "nothing waiting");
                y -= TableRowH;
            }

            // ---- the words: a key each; the terms open under the one pressed ----
            y = TableBand(sheet, y, "OUR WORD");
            var relations = outfit.Relations;
            var rules = relations?.Config ?? HouseRelationsConfig.Default;
            var config = book.Config;
            CollectStreets();
            var anyThird = false;
            var anyWarToJoin = false;
            foreach (var gang in gangs)
            {
                if (gang.IsPlayer || gang.Id == house.Id)
                    continue;
                anyThird = true;
                if (outfit.StanceWith(gang.Id) == Outfit.Stance.War)
                    anyWarToJoin = true;
            }
            tableCeiling = relations != null
                ? HouseDiplomacy.BillCeiling(relations, mine, house.Id, config, day)
                : 0;
            tableLevy = weOwe > 0 ? weOwe : theyOwe;
            var warPending = hasPending && pending == Outfit.Stance.War;
            var ourDays = HouseRelations.Endurance(outfit.Accounts.Safe,
                Wages.DailyPayroll(director.Roster));
            var pactStands = book.HasPact(mine, house.Id, day);
            var ours = HouseOps.Look != null && Underworld.Current?.Player != null
                ? HouseOps.Look(Underworld.Current.Player)
                : null;
            var theirDays = ours != null ? ours.TheirEndurance(them) : -1;
            var theirLosses = theirBook != null ? theirBook.Runner.LossesThisWar(mine) : 0;

            string WhyNot(ProposalKind kind) => HouseDiplomacy.WhyNot(kind, current,
                book.HasOpen(mine, house.Id, kind, day), tableStreets.Count > 0,
                kind == ProposalKind.JoinWar ? anyWarToJoin : anyThird, pactStands,
                tableCeiling, weOwe, theyOwe, ourDays, rules);

            var keyW = (w - 5f * 6f) / 6f;
            var keyPitch = TableKeyH + 18f;
            var x = 0f;
            var warWhy = HouseDiplomacy.WhyNotWar(current, warPending);
            var warKey = LedgerV2.Button(sheet, "DECLARE WAR", x, y, keyW, TableKeyH, () =>
            {
                tableWar = !tableWar;
                tableWord = ProposalKind.None;
                tableNote = "";
                dirty = true;
            }, red: true, size: 10f, outline: !tableWar);
            if (warWhy != null)
                LedgerV2.KeyEnabled(warKey, false);
            KeyWhy(sheet, x, y, keyW, warWhy);
            x += keyW + 6f;
            var column = 1;
            foreach (var kind in TableWords)
            {
                if (column == 6)
                {
                    column = 0;
                    x = 0f;
                    y -= keyPitch;
                }
                var word = kind;
                var why = WhyNot(word);
                var key = LedgerV2.Button(sheet, WordLabel(word), x, y, keyW, TableKeyH,
                    () => PickWord(word), red: false, size: 10f, outline: tableWord != word);
                if (why != null)
                    LedgerV2.KeyEnabled(key, false);
                KeyWhy(sheet, x, y, keyW, why);
                x += keyW + 6f;
                column++;
            }
            y -= keyPitch;

            // ---- the panel under the key pressed ----
            if (tableWar)
            {
                y = TableBand(sheet, y, "DECLARE WAR · " + house.Name.ToUpperInvariant());
                Paragraph(sheet, LedgerStyle.Mono, 11f, LedgerV2.Ink, 0f, y, w, 40f,
                    "War lands at midnight. From then their men and ours fight on sight, " +
                    "their doors are ours to take and ours theirs" +
                    (pactStands ? "; the pact sworn between us is broken by it, and every " +
                                  "house hears" : "") +
                    ". A war ends in a truce, and a beaten house cannot refuse one.",
                    lineSpacing: 2f);
                y -= 44f;
                LedgerV2.Button(sheet, "DECLARE", 0f, y, 140f, TableKeyH, () =>
                {
                    tableWar = false;
                    Stance(house.Id, Outfit.Stance.War);
                }, red: true, size: 10f, outline: false);
                LedgerV2.Button(sheet, "NEVER MIND", 148f, y, 140f, TableKeyH, () =>
                {
                    tableWar = false;
                    dirty = true;
                }, red: false, size: 10f, outline: true);
                y -= TableKeyH + 10f;
            }
            else if (tableWord != ProposalKind.None)
            {
                var kind = tableWord;
                y = TableBand(sheet, y, WordLabel(kind) + " · " + house.Name.ToUpperInvariant());
                var reads = theirDays < 0 ? ""
                    : theirDays < rules.MinWarDays ? " · they cannot pay their men " + rules.MinWarDays + " days: beaten"
                    : theirDays > ourDays ? " · they read stronger than us"
                    : " · they read weaker than us";
                var cap = LedgerText.Cash(config.CompensationCapPerDay * config.CompensationPerPoint);
                var safe = outfit.Accounts.Safe;
                switch (kind)
                {
                    case ProposalKind.OfferTruce:
                        y = MoneyRow(sheet, y, w, safe,
                            "clears what they hold against us · at most " + cap + " a day counts" +
                            (theirDays >= 0 && theirDays < rules.MinWarDays
                                ? " · they are beaten: they cannot refuse"
                                : theirLosses >= rules.LossesToSueForPeace
                                    ? " · they have lost " + theirLosses + " men to us: they cannot refuse"
                                    : reads));
                        break;
                    case ProposalKind.OfferPeace:
                        y = MoneyRow(sheet, y, w, safe,
                            "clears what they hold against us · peace when little is left · " +
                            "not for a month after a killing");
                        break;
                    case ProposalKind.Warn:
                    case ProposalKind.Threaten:
                        y = StreetRow(sheet, y, w,
                            "keeps them off it for " + config.ComplyDays + " days if they yield · " +
                            "they yield to a house that reads stronger and is owed nothing" + reads);
                        break;
                    case ProposalKind.Bill:
                        y = MoneyRow(sheet, y, w, tableCeiling,
                            "what they owe us today · up to " + LedgerText.Cash(tableCeiling) +
                            " · paid if they read weaker and their safe covers it" + reads);
                        break;
                    case ProposalKind.TributeTerms:
                        y = MoneyRow(sheet, y, w, weOwe > 0 ? weOwe : safe, weOwe > 0
                            ? "a cycle · the street prices it at " + LedgerText.Cash(weOwe) +
                              " · half is the least they take, unless they are broke"
                            : "a cycle · they kick up " + LedgerText.Cash(theyOwe) +
                              " · a figure they can pay, from the stronger house" + reads);
                        break;
                    case ProposalKind.Line:
                        y = StreetRow(sheet, y, w,
                            "both keep off it for " + config.LineDays + " days · taken only by two " +
                            "houses that cannot pay for a war · we have " + ourDays + " days of wages");
                        break;
                    case ProposalKind.Pact:
                        CollectThirds(gangs, house.Id, atWarOnly: false);
                        y = ThirdRow(sheet, y, w,
                            "sworn for " + config.PactDays + " days · a war on either is a war on " +
                            "both · they swear it at peace with us, able to pay, and stronger than " +
                            (tableThirds.Count > 0 ? tableThirds[tableThird].Name : "the third"));
                        break;
                    case ProposalKind.JoinWar:
                        CollectThirds(gangs, house.Id, atWarOnly: true);
                        y = ThirdRow(sheet, y, w, "the war we are in · their war on " +
                            (tableThirds.Count > 0 ? tableThirds[tableThird].Name : "the third") +
                            " from midnight, sworn like a pact");
                        y = MoneyRow(sheet, y, w, safe,
                            "clears what they hold against us · they join only when little is left" + reads);
                        break;
                }
                y = CarriedRow(sheet, y, w);
                LedgerV2.Button(sheet, "SEND IT", 0f, y, 140f, TableKeyH, () => Say(house.Id, kind),
                    red: false, size: 10f, outline: false);
                LedgerV2.Button(sheet, "NEVER MIND", 148f, y, 140f, TableKeyH, () =>
                {
                    tableWord = ProposalKind.None;
                    tableNote = "";
                    dirty = true;
                }, red: false, size: 10f, outline: true);
                y -= TableKeyH + 10f;
            }
            else
            {
                Paragraph(sheet, LedgerStyle.Mono, 11f, LedgerV2.Muted, 0f, y, w, 40f,
                    "Press a word and its terms open under it. A greyed key says why it " +
                    "cannot be said today. War is declared and lands at midnight; " +
                    "everything else is asked, and their desk answers at once - or, " +
                    "carried in person, when our man stands at their door.", lineSpacing: 2f);
                y -= 46f;
            }

            // ---- the record ----
            y = TableBand(sheet, y, "THE RECORD");
            book.Between(mine, house.Id, tableRecord);
            var shown = 0;
            for (var i = tableRecord.Count - 1; i >= 0 && shown < TableRecordShown; i--, shown++)
            {
                var entry = tableRecord[i];
                var verdict = entry.Status == ProposalStatus.Open
                    ? (entry.InTransit ? "ON THE ROAD" : "WAITING")
                    : entry.Status == ProposalStatus.Accepted
                        ? "ACCEPTED" + (string.IsNullOrEmpty(entry.Answer) ? "" : " · " + entry.Answer)
                        : entry.Status.ToString().ToUpperInvariant() +
                          (string.IsNullOrEmpty(entry.Answer) ? "" : " · " + entry.Answer);
                LedgerV2.Mono(sheet, 0f, y, 70f, "DAY " + entry.Day, 9.5f, LedgerV2.Label, 6f);
                var said = Line(sheet, LedgerStyle.Mono, 12f, LedgerV2.Ink, 76f, y, w - 76f - 260f,
                    LineBox(12f), HouseDiplomacy.Describe(entry));
                said.overflowMode = TextOverflowModes.Ellipsis;
                var came = Line(sheet, LedgerStyle.MonoBold, 12f,
                    entry.Status == ProposalStatus.Accepted ? LedgerV2.Ink
                    : entry.Status == ProposalStatus.Open ? LedgerV2.Muted : LedgerV2.Red,
                    w - 260f, y, 260f, LineBox(12f), verdict, TextAlignmentOptions.MidlineRight);
                came.overflowMode = TextOverflowModes.Ellipsis;
                LedgerV2.Leader(sheet, 0f, y - 17f, w);
                y -= TableRowH;
            }
            if (shown == 0)
            {
                Line(sheet, LedgerStyle.MonoItalic, 12f, LedgerV2.Muted, 0f, y, w, LineBox(12f),
                    "nothing has passed between the houses");
                y -= TableRowH;
            }

            SizeFamiliesContent(-y + 12f);
        }

        float TableRow(Transform sheet, float y, string label, string value, Color ink)
        {
            CardRow(sheet, 0f, y, PageWidth, label, value, ink);
            return y - TableRowH;
        }

        float TableBand(Transform sheet, float y, string label)
        {
            y -= 6f;
            var head = Line(sheet, LedgerStyle.Condensed, 13f, LedgerV2.Ink, 0f, y, PageWidth,
                LineBox(13f), label);
            head.characterSpacing = 5f;
            Block("Band rule", sheet, 0f, y - 19f, PageWidth, 1f, LedgerV2.SheetRule);
            return y - 26f;
        }

        // ---- the doing ----

        void Answer(int proposalId, bool accept)
        {
            var result = outfit ? outfit.Reply(proposalId, accept) : default;
            tableNote = result.Ok ? "" : result.Reason;
            dirty = true;
        }

        void Ambush(int proposalId)
        {
            var result = outfit ? outfit.Ambush(proposalId) : default;
            tableNote = result.Ok ? "" : result.Reason;
            dirty = true;
        }

        void Stance(int gangId, Outfit.Stance stance)
        {
            var result = outfit ? outfit.SetStance(gangId, stance) : default;
            tableNote = result.Ok ? "" : result.Reason;
            dirty = true;
        }

        /// <summary>One word to that house, by telephone or carried, with what the
        /// sheet has set beside it: the money, the street, the third house.</summary>
        void Say(int gangId, ProposalKind kind)
        {
            if (!outfit)
                return;
            var proposal = new Proposal { To = gangId, Kind = kind };
            switch (kind)
            {
                case ProposalKind.OfferTruce:
                case ProposalKind.OfferPeace:
                case ProposalKind.Bill:
                case ProposalKind.TributeTerms:
                case ProposalKind.JoinWar:
                    proposal.Terms.Money = tableMoney;
                    break;
            }
            if (kind == ProposalKind.Warn || kind == ProposalKind.Threaten || kind == ProposalKind.Line)
            {
                if (tableStreets.Count == 0)
                {
                    tableNote = "no street of ours to name";
                    dirty = true;
                    return;
                }
                proposal.Terms.Blocks.Add(tableStreets[tableStreet].Value);
            }
            if (kind == ProposalKind.Pact || kind == ProposalKind.JoinWar)
            {
                if (tableThirds.Count == 0)
                {
                    tableNote = "no third house";
                    dirty = true;
                    return;
                }
                proposal.Terms.Third = tableThirds[tableThird].Id;
            }
            if (kind == ProposalKind.TributeTerms && proposal.Terms.Money <= 0)
            {
                tableNote = "terms need a figure";
                dirty = true;
                return;
            }

            var result = tableInPerson && tableEnvoys.Count > 0
                ? outfit.SendToSitDown(proposal, tableEnvoys[tableEnvoy].Id)
                : outfit.Propose(proposal);
            if (result.Ok)
            {
                var filed = outfit.Diplomacy?.Find(proposal.Id);
                tableNote = filed == null ? ""
                    : filed.InTransit ? "on the road"
                    : filed.Status == ProposalStatus.Open ? "waiting on them"
                    : filed.Status == ProposalStatus.Accepted ? "accepted"
                    : "refused - " + filed.Answer;
                // The word reached them: the panel folds, the record shows the rest.
                tableWord = ProposalKind.None;
                tableMoney = 0;
            }
            else
                tableNote = result.Reason;
            dirty = true;
        }

        // ---- what the sheet can point at ----

        /// <summary>Our streets: every block the map paints as ours.</summary>
        void CollectStreets()
        {
            tableStreets.Clear();
            var runtime = RoadDemo.TerritoryRuntime.Instance;
            if (runtime == null)
                return;
            for (var i = 0; i < holdings.Count; i++)
            {
                if (holdings[i].GangId != Gangs.GangCatalog.PlayerGangId)
                    continue;
                if (!runtime.TryGetBlock(holdings[i].BlockId, out var blockId) || !blockId.IsValid)
                    continue;
                if (!tableStreets.Contains(blockId))
                    tableStreets.Add(blockId);
            }
        }

        string StreetName(Territory.TerritoryBlockId blockId)
        {
            var runtime = RoadDemo.TerritoryRuntime.Instance;
            if (runtime?.Geography != null &&
                runtime.Geography.TryGetBlock(blockId, out var block) &&
                !string.IsNullOrEmpty(block.DisplayName))
                return block.DisplayName;
            return blockId.Value;
        }

        /// <summary>The houses a pact can be sworn against - every other one - or,
        /// for a war joined, only the ones we are at war with.</summary>
        void CollectThirds(IReadOnlyList<Gangs.Gang> gangs, int except, bool atWarOnly)
        {
            tableThirds.Clear();
            foreach (var gang in gangs)
            {
                if (gang.IsPlayer || gang.Id == except)
                    continue;
                if (atWarOnly && outfit.StanceWith(gang.Id) != Outfit.Stance.War)
                    continue;
                tableThirds.Add(gang);
            }
            tableThird = tableThirds.Count > 0 ? Mathf.Clamp(tableThird, 0, tableThirds.Count - 1) : 0;
        }

        // ---- the keys and the rows under them ----

        /// <summary>The ten words, in the order the keys stand.</summary>
        static readonly ProposalKind[] TableWords =
        {
            ProposalKind.OfferTruce, ProposalKind.OfferPeace, ProposalKind.Warn,
            ProposalKind.Threaten, ProposalKind.Bill, ProposalKind.TributeTerms,
            ProposalKind.Line, ProposalKind.Pact, ProposalKind.JoinWar,
        };

        static string WordLabel(ProposalKind kind)
        {
            switch (kind)
            {
                case ProposalKind.OfferTruce: return "OFFER TRUCE";
                case ProposalKind.OfferPeace: return "OFFER PEACE";
                case ProposalKind.Warn: return "WARNING";
                case ProposalKind.Threaten: return "THREAT";
                case ProposalKind.Bill: return "SEND A BILL";
                case ProposalKind.TributeTerms: return "TRIBUTE TERMS";
                case ProposalKind.Line: return "DRAW A LINE";
                case ProposalKind.Pact: return "OFFER A PACT";
                case ProposalKind.JoinWar: return "JOIN MY WAR";
            }
            return kind.ToString().ToUpperInvariant();
        }

        /// <summary>A key pressed: its panel opens with the figure the word starts
        /// from - a bill at the most it may ask, terms at half the street's figure.</summary>
        void PickWord(ProposalKind kind)
        {
            tableWar = false;
            tableNote = "";
            if (tableWord == kind)
            {
                tableWord = ProposalKind.None;
                dirty = true;
                return;
            }
            tableWord = kind;
            switch (kind)
            {
                case ProposalKind.Bill:
                    tableMoney = tableCeiling;
                    break;
                case ProposalKind.TributeTerms:
                    tableMoney = tableLevy / 2;
                    break;
                default:
                    tableMoney = 0;
                    break;
            }
            dirty = true;
        }

        /// <summary>The reason a greyed key cannot be pressed, printed under it.</summary>
        void KeyWhy(Transform sheet, float x, float y, float keyW, string why)
        {
            if (string.IsNullOrEmpty(why))
                return;
            var line = Line(sheet, LedgerStyle.MonoItalic, 9f, LedgerV2.Muted, x + 4f,
                y - TableKeyH - 1f, keyW - 8f, LineBox(9f), why);
            line.overflowMode = TextOverflowModes.Ellipsis;
            line.alignment = TextAlignmentOptions.Center;
        }

        float MoneyRow(Transform sheet, float y, float w, int most, string hint)
        {
            most = Mathf.Max(0, most);
            tableMoney = Mathf.Clamp(tableMoney, 0, most);
            LedgerV2.Mono(sheet, 0f, y, 80f, "MONEY", 9.5f, LedgerV2.Label, 6f);
            var less = LedgerV2.Button(sheet, "-", 90f, y + 2f, 30f, TableKeyH, () =>
            {
                tableMoney = Mathf.Max(0, tableMoney - TableMoneyStep);
                dirty = true;
            }, red: false, size: 11f, outline: true);
            LedgerV2.Figure(sheet, 126f, y, 130f, LedgerText.Cash(tableMoney), 14f,
                tableMoney > 0 ? LedgerV2.Ink : LedgerV2.Muted);
            var more = LedgerV2.Button(sheet, "+", 262f, y + 2f, 30f, TableKeyH, () =>
            {
                tableMoney = Mathf.Min(most, tableMoney + TableMoneyStep);
                dirty = true;
            }, red: false, size: 11f, outline: true);
            LedgerV2.KeyEnabled(less, tableMoney > 0);
            LedgerV2.KeyEnabled(more, tableMoney < most);
            var line = Line(sheet, LedgerStyle.MonoItalic, 11f, LedgerV2.Muted, 300f, y, w - 300f,
                LineBox(11f), hint + " · " + LedgerText.Cash(outfit.Accounts.Safe) + " in the safe");
            line.overflowMode = TextOverflowModes.Ellipsis;
            return y - (TableKeyH + 4f);
        }

        float PickRow(Transform sheet, float y, float w, string label, string value,
            int count, System.Action<int> step, string hint)
        {
            LedgerV2.Mono(sheet, 0f, y, 80f, label, 9.5f, LedgerV2.Label, 6f);
            var back = LedgerV2.Button(sheet, "<", 90f, y + 2f, 30f, TableKeyH, () =>
            {
                step(-1);
                dirty = true;
            }, red: false, size: 11f, outline: true);
            var text = Line(sheet, LedgerStyle.MonoBold, 12f, LedgerV2.Ink, 126f, y, 300f,
                LineBox(12f), value);
            text.overflowMode = TextOverflowModes.Ellipsis;
            var forth = LedgerV2.Button(sheet, ">", 432f, y + 2f, 30f, TableKeyH, () =>
            {
                step(1);
                dirty = true;
            }, red: false, size: 11f, outline: true);
            LedgerV2.KeyEnabled(back, count > 1);
            LedgerV2.KeyEnabled(forth, count > 1);
            var line = Line(sheet, LedgerStyle.MonoItalic, 11f, LedgerV2.Muted, 470f, y, w - 470f,
                LineBox(11f), hint);
            line.overflowMode = TextOverflowModes.Ellipsis;
            return y - (TableKeyH + 4f);
        }

        float StreetRow(Transform sheet, float y, float w, string hint)
        {
            tableStreet = tableStreets.Count > 0 ? Mathf.Clamp(tableStreet, 0, tableStreets.Count - 1) : 0;
            return PickRow(sheet, y, w, "STREET",
                tableStreets.Count > 0 ? StreetName(tableStreets[tableStreet]) : "no street of ours to name",
                tableStreets.Count, by =>
                {
                    if (tableStreets.Count > 0)
                        tableStreet = (tableStreet + by + tableStreets.Count) % tableStreets.Count;
                }, hint);
        }

        float ThirdRow(Transform sheet, float y, float w, string hint) =>
            PickRow(sheet, y, w, "AGAINST",
                tableThirds.Count > 0 ? tableThirds[tableThird].Name : "no third house",
                tableThirds.Count, by =>
                {
                    if (tableThirds.Count > 0)
                        tableThird = (tableThird + by + tableThirds.Count) % tableThirds.Count;
                }, hint);

        float CarriedRow(Transform sheet, float y, float w)
        {
            CollectEnvoys();
            tableEnvoy = tableEnvoys.Count > 0 ? Mathf.Clamp(tableEnvoy, 0, tableEnvoys.Count - 1) : 0;
            if (tableEnvoys.Count == 0)
                tableInPerson = false;
            LedgerV2.Mono(sheet, 0f, y, 80f, "CARRIED", 9.5f, LedgerV2.Label, 6f);
            LedgerV2.Chip(sheet, "BY TELEPHONE", 90f, y + 2f, 130f, TableKeyH, !tableInPerson, () =>
            {
                tableInPerson = false;
                dirty = true;
            }, 10f);
            var inPerson = LedgerV2.Chip(sheet, "IN PERSON", 226f, y + 2f, 110f, TableKeyH, tableInPerson, () =>
            {
                if (tableEnvoys.Count > 0)
                    tableInPerson = true;
                dirty = true;
            }, 10f);
            LedgerV2.KeyEnabled(inPerson, tableEnvoys.Count > 0);
            if (tableInPerson && tableEnvoys.Count > 0)
            {
                var back = LedgerV2.Button(sheet, "<", 344f, y + 2f, 30f, TableKeyH, () =>
                {
                    tableEnvoy = (tableEnvoy + tableEnvoys.Count - 1) % tableEnvoys.Count;
                    dirty = true;
                }, red: false, size: 11f, outline: true);
                var envoy = tableEnvoys[tableEnvoy];
                var name = Line(sheet, LedgerStyle.MonoBold, 12f, LedgerV2.Ink, 380f, y, 260f,
                    LineBox(12f), envoy.FullName + " · streetwise " +
                                  LedgerText.Stars(envoy.GetHalfSteps(CharacterAttribute.Streetwise)));
                name.overflowMode = TextOverflowModes.Ellipsis;
                var forth = LedgerV2.Button(sheet, ">", 646f, y + 2f, 30f, TableKeyH, () =>
                {
                    tableEnvoy = (tableEnvoy + 1) % tableEnvoys.Count;
                    dirty = true;
                }, red: false, size: 11f, outline: true);
                LedgerV2.KeyEnabled(back, tableEnvoys.Count > 1);
                LedgerV2.KeyEnabled(forth, tableEnvoys.Count > 1);
                var line = Line(sheet, LedgerStyle.MonoItalic, 11f, LedgerV2.Muted, 684f, y, w - 684f,
                    LineBox(11f), "his streetwise moves their tests our way · he can be shot at their door");
                line.overflowMode = TextOverflowModes.Ellipsis;
            }
            else
                Line(sheet, LedgerStyle.MonoItalic, 11f, LedgerV2.Muted, 344f, y, w - 344f,
                    LineBox(11f), tableEnvoys.Count > 0
                        ? "in person, his streetwise moves their tests in our favour - and he can be shot at their door"
                        : "no lieutenant to send - the Don stays home");
            return y - (TableKeyH + 10f);
        }

        /// <summary>Our lieutenants, standing and free to walk. The Don never goes.</summary>
        void CollectEnvoys()
        {
            tableEnvoys.Clear();
            var roster = director.Roster;
            if (roster == null)
                return;
            foreach (var man in roster.Members)
                if (!man.Gone && man.Rank == Rank.Lieutenant &&
                    man.Status == CharacterStatus.Active)
                    tableEnvoys.Add(man);
        }
    }
}
