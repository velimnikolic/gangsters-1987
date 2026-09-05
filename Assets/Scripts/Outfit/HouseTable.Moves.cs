using System.Collections.Generic;
using LivingCity.Gangs;

namespace LivingCity.Outfit
{
    /// <summary>
    /// What the sheet knows before it asks what may be said: the books, the day, and
    /// the four things a word can point at. Gathered once a build so the ten legality
    /// questions are ten reads and not ten walks of the city.
    /// </summary>
    public sealed class TableContext
    {
        public Underworld World;
        public int Mine;
        public int Them;
        public int Day;

        /// <summary>What the safe holds, and what the outfit pays its men a day - the
        /// two figures every money row is measured against.</summary>
        public int Safe;
        public int DailyPayroll;

        /// <summary>Streets of ours a word or a line may name.</summary>
        public int Streets;

        /// <summary>Whether there is any third house at all, and any war of ours a
        /// third house could be asked into.</summary>
        public bool AnyThird;
        public bool AnyWarToJoin;

        /// <summary>Lieutenants standing and free to carry a word.</summary>
        public int Envoys;

        // ---- filled by Words(), read by the sheet that drew it ----

        public Stance Stance;
        public bool WarPending;
        public bool PactStands;
        public int BillCeiling;
        public int Levy;
        public int WeOwe;
        public int TheyOwe;

        /// <summary>The rung the street word stands on today - Warn, or Threaten once
        /// they have taken a threat's worth off us.</summary>
        public ProposalKind StreetWord = ProposalKind.Warn;

        /// <summary>Days of wages each house can still pay, by its own books. Negative
        /// when the desk cannot be read.</summary>
        public int OurDays = -1;
        public int TheirDays = -1;

        /// <summary>What they have lost to us in this war.</summary>
        public int TheirLosses;
    }

    public static partial class HouseTable
    {
        /// <summary>The eight words a key can file, in the order they stand.</summary>
        static readonly ProposalKind[] Words8 =
        {
            ProposalKind.OfferTruce, ProposalKind.OfferPeace, ProposalKind.Warn,
            ProposalKind.Bill, ProposalKind.TributeTerms,
            ProposalKind.Line, ProposalKind.Pact, ProposalKind.JoinWar,
        };

        public static string LabelOf(ProposalKind kind) => kind switch
        {
            ProposalKind.OfferTruce => "Offer truce",
            ProposalKind.OfferPeace => "Sue for peace",
            ProposalKind.Warn => "Warn them off",
            ProposalKind.Threaten => "The last warning",
            ProposalKind.Bill => "Send a bill",
            ProposalKind.TributeTerms => "Tribute terms",
            ProposalKind.Line => "Draw a line",
            ProposalKind.Pact => "Offer a pact",
            ProposalKind.JoinWar => "Call in a house",
            _ => kind.ToString(),
        };

        static string GroupOf(ProposalKind kind) => kind switch
        {
            ProposalKind.OfferTruce => "Talk",
            ProposalKind.OfferPeace => "Talk",
            ProposalKind.Warn => "Press",
            ProposalKind.Threaten => "Press",
            ProposalKind.Bill => "Money",
            ProposalKind.TributeTerms => "Money",
            _ => "The table",
        };

        static MoveFace FaceOf(ProposalKind kind, Stance stance) => kind switch
        {
            ProposalKind.OfferPeace => MoveFace.Dark,
            ProposalKind.OfferTruce => stance == Stance.War ? MoveFace.Dark : MoveFace.Outline,
            ProposalKind.Bill => MoveFace.Dark,
            ProposalKind.Pact => MoveFace.Dark,
            _ => MoveFace.Outline,
        };

        /// <summary>
        /// EVERY WORD WE MAY SAY TO THAT HOUSE TODAY, and every one we may not with the
        /// gateway's own reason under it. The order is the ladder's: what they have
        /// asked us first, then the doing verbs, then the declaration.
        /// </summary>
        public static void Words(TableContext context, List<TableMove> open,
            List<ShutMove> shut)
        {
            open?.Clear();
            shut?.Clear();
            if (context?.World == null || open == null || shut == null)
                return;

            var world = context.World;
            var mine = context.Mine;
            var them = context.Them;
            var day = context.Day;
            var book = world.Diplomacy;
            var relations = world.Relations;
            var config = book.Config;
            var rules = relations.Config;

            context.Stance = relations.StanceBetween(mine, them);
            context.WarPending = relations.TryGetPending(mine, them, out var pending) &&
                                 pending == Stance.War;
            context.PactStands = book.HasPact(mine, them, day);

            var us = world.Of(mine);
            var theirs = world.Of(them);
            context.WeOwe = us != null ? us.Runner.Tribute.OwedTo(them) : 0;
            context.TheyOwe = theirs != null ? theirs.Runner.Tribute.OwedTo(mine) : 0;
            context.Levy = context.WeOwe > 0 ? context.WeOwe : context.TheyOwe;
            context.BillCeiling = HouseDiplomacy.BillCeiling(relations, mine, them, config, day);

            var ourGrudge = relations.Grievance(mine, them);
            var grudge = ourGrudge >= rules.ThreatAt ||
                         relations.Grievance(them, mine) >= rules.ThreatAt;
            context.StreetWord = HouseDiplomacy.WordForStreet(ourGrudge, rules);
            context.OurDays = HouseRelations.Endurance(context.Safe, context.DailyPayroll);
            context.TheirLosses = theirs != null ? theirs.Runner.LossesThisWar(mine) : 0;

            // ---- what they have asked us, and the two or three answers to it ----
            AnswerKeys(context, open);

            // ---- the eight words ----
            foreach (var kind in Words8)
            {
                var said = kind == ProposalKind.Warn ? context.StreetWord : kind;
                var asked = said == ProposalKind.Warn || said == ProposalKind.Threaten
                    ? book.HasOpen(mine, them, ProposalKind.Warn, day) ||
                      book.HasOpen(mine, them, ProposalKind.Threaten, day)
                    : book.HasOpen(mine, them, said, day);
                var why = HouseDiplomacy.WhyNot(said, context.Stance, asked,
                    context.Streets > 0,
                    said == ProposalKind.JoinWar ? context.AnyWarToJoin : context.AnyThird,
                    context.PactStands, context.BillCeiling, context.WeOwe, context.TheyOwe,
                    grudge);

                if (why != null)
                {
                    shut.Add(new ShutMove(LabelOf(said), why));
                    continue;
                }

                open.Add(new TableMove
                {
                    Label = LabelOf(said),
                    Head = LabelOf(said) + " · " + NameOfHouse(them),
                    Send = said == ProposalKind.JoinWar ? "CALL IT IN" : "SEND IT",
                    Terms = TermsOf(said, context, config, rules),
                    Group = GroupOf(said),
                    Face = FaceOf(said, context.Stance),
                    SendIsRed = said == ProposalKind.Threaten,
                    Word = said,
                    NeedsMoney = NeedsMoney(said),
                    NeedsStreet = said == ProposalKind.Warn ||
                                  said == ProposalKind.Threaten || said == ProposalKind.Line,
                    NeedsThird = said == ProposalKind.Pact || said == ProposalKind.JoinWar,
                    MoneyCeiling = CeilingOf(said, context),
                });
            }

            // ---- the declaration, which is a stance and not a word ----
            var warWhy = HouseDiplomacy.WhyNotWar(context.Stance, context.WarPending);
            if (warWhy != null)
                shut.Add(new ShutMove("Declare war", warWhy));
            else
                open.Add(new TableMove
                {
                    Label = "Declare war",
                    Head = "Declare war · " + NameOfHouse(them),
                    Send = "DECLARE IT",
                    SendIsRed = true,
                    Face = MoveFace.Red,
                    Group = "Press",
                    War = true,
                    Terms =
                        "On sight, from midnight. From then their men and ours fight " +
                        "anywhere in the city, their doors are ours to take and ours " +
                        "theirs" +
                        (context.PactStands
                            ? "; the pact sworn between us breaks with it, and every " +
                              "house hears by morning"
                            : "") +
                        ". A war ends in a truce, and a beaten house cannot refuse one.",
                });
        }

        static bool NeedsMoney(ProposalKind kind) => kind switch
        {
            ProposalKind.OfferTruce => true,
            ProposalKind.OfferPeace => true,
            ProposalKind.Bill => true,
            ProposalKind.TributeTerms => true,
            ProposalKind.JoinWar => true,
            _ => false,
        };

        static int CeilingOf(ProposalKind kind, TableContext context) => kind switch
        {
            ProposalKind.Bill => context.BillCeiling,
            ProposalKind.TributeTerms => context.WeOwe > 0 ? context.WeOwe : context.Safe,
            _ => context.Safe,
        };

        /// <summary>
        /// THEIR WORD, ANSWERED. A proposal of theirs lying in our inbox is not a row
        /// with two little keys on it - it is the first thing this house can be said to
        /// today, so it stands at the head of the column as the moves it really is.
        /// </summary>
        static void AnswerKeys(TableContext context, List<TableMove> open)
        {
            var book = context.World.Diplomacy;
            var inbox = new List<Proposal>();
            book.OpenFor(context.Mine, inbox);
            for (var i = 0; i < inbox.Count; i++)
            {
                var proposal = inbox[i];
                if (proposal.From != context.Them)
                    continue;

                var what = HouseDiplomacy.Describe(proposal);
                if (proposal.InTransit)
                {
                    // Nothing to answer yet: his man is still on the road. The sheet
                    // says so rather than showing keys that would be refused.
                    continue;
                }

                open.Add(new TableMove
                {
                    Label = "Give our word",
                    Head = "Answer " + NameOfHouse(context.Them) + " · " + what,
                    Send = "GIVE IT",
                    Face = MoveFace.Dark,
                    Group = "The table",
                    AnswerTo = proposal.Id,
                    Accepts = true,
                    Terms = "We take it as they asked it: " + what + ". It lands at " +
                            "midnight and every house at the table hears what we agreed.",
                });
                open.Add(new TableMove
                {
                    Label = "Refuse them",
                    Head = "Refuse " + NameOfHouse(context.Them),
                    Send = "REFUSE",
                    Face = MoveFace.Ghost,
                    Group = "The table",
                    AnswerTo = proposal.Id,
                    Accepts = false,
                    Terms = "No, and no reason given. A house that asked and was " +
                            "refused remembers it longer than a house that was never asked.",
                });

                if (proposal.Envoy >= 0)
                    open.Add(new TableMove
                    {
                        Label = "Ambush his man",
                        Head = "Ambush the envoy · " + NameOfHouse(context.Them),
                        Send = "TAKE HIM",
                        SendIsRed = true,
                        Face = MoveFace.Red,
                        Group = "The table",
                        AnswerTo = proposal.Id,
                        Ambushes = true,
                        Terms = "Their man is standing at our door with the word in his " +
                                "hand. Taking him there answers nothing and cannot be " +
                                "taken back — every table in the city prices us by it after.",
                    });
                break;
            }
        }

        /// <summary>What a word costs and buys, in the reader's own words and with the
        /// day's real figures in it.</summary>
        static string TermsOf(ProposalKind kind, TableContext context,
            DiplomacyConfig config, HouseRelationsConfig rules)
        {
            var reads = context.TheirDays < 0 ? ""
                : context.TheirDays < rules.MinWarDays
                    ? " They cannot pay their men " + rules.MinWarDays + " days: they are beaten."
                : context.TheirDays > context.OurDays
                    ? " They read stronger than us."
                    : " They read weaker than us.";
            var cap = Money(config.CompensationCapPerDay * config.CompensationPerPoint);

            switch (kind)
            {
                case ProposalKind.OfferTruce:
                    return context.Stance == Stance.War
                        ? "Both houses stand down at midnight, and what is taken stays " +
                          "taken. Money on top clears what they hold against us, at most " +
                          cap + " of it in one day." +
                          (context.TheirDays >= 0 && context.TheirDays < rules.MinWarDays
                              ? " They are beaten: they cannot refuse."
                              : context.TheirLosses >= rules.LossesToSueForPeace
                                  ? " They have lost " + context.TheirLosses +
                                    " men to us: they cannot refuse."
                                  : reads)
                        : "Territorial only, from midnight: their men engage ours caught " +
                          "inside their blocks and ours theirs. The ladder's first word, " +
                          "before anything harder — they take it while no crew of theirs " +
                          "works our streets." + reads;
                case ProposalKind.OfferPeace:
                    return "No engagement at all, claimed ground or not, from midnight. " +
                           "Money on top clears what they hold against us — and peace is " +
                           "taken only when little is left, never for a month after a killing." +
                           reads;
                case ProposalKind.Warn:
                    return "The first word, before anything else: our men counted on that " +
                           "street for one evening, no figure and no date. Yield and they " +
                           "keep off it for " + config.ComplyDays + " days. Refused, or left " +
                           "two days, it is owed for." + reads;
                case ProposalKind.Threaten:
                    return "A promise, not a demand: off that street, or the door on it " +
                           "stops being a door. They have taken enough that this is the " +
                           "last word before a bill. Yield and they keep off for " +
                           config.ComplyDays + " days." + reads;
                case ProposalKind.Bill:
                    return "What they owe us today, named in full and due at midnight — " +
                           "at most " + Money(context.BillCeiling) + ", which is all a bill " +
                           "can still clear off this pair. Paid if they read weaker and " +
                           "their safe covers it. A bill sent twice is not a bill any more." +
                           reads;
                case ProposalKind.TributeTerms:
                    return context.WeOwe > 0
                        ? "Rewrite what WE kick up: the street prices it at " +
                          Money(context.WeOwe) + " a cycle, and half of that is the least " +
                          "they will take unless they are broke. Pinned for " +
                          config.TermsCycles + " cycles if they sign." + reads
                        : "Rewrite what they kick up to us — " + Money(context.TheyOwe) +
                          " a cycle today. A figure they can actually pay, from the " +
                          "stronger house, pinned for " + config.TermsCycles + " cycles." + reads;
                case ProposalKind.Line:
                    return "A street neither house crosses for " + config.LineDays +
                           " days. Taken only by two houses that cannot pay for a war — we " +
                           "have " + context.OurDays + " days of wages in the safe. A door " +
                           "taken or hit across it is owed for on top.";
                case ProposalKind.Pact:
                    return "Sworn for " + config.PactDays + " days: a war on either is a " +
                           "war on both, honoured by the book at midnight. They swear it " +
                           "only at peace with us, able to pay, and stronger than the " +
                           "house we name.";
                case ProposalKind.JoinWar:
                    return "They swear men to the war we are already in, for as long as it " +
                           "runs, flagged as the pact's own. Money on top clears what they " +
                           "hold against us; they come in only when little is left." + reads;
            }
            return "";
        }

        static string NameOfHouse(int gangId)
        {
            var gangs = GangRegistry.Gangs;
            for (var i = 0; i < gangs.Count; i++)
                if (gangs[i].Id == gangId)
                    return gangs[i].Name;
            return "them";
        }
    }
}
