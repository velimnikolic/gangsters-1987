using TMPro;
using UnityEngine;
using LivingCity.Personnel;
using LivingCity.Territory;
using static LivingCity.UI.LedgerKit;
using BlockTenure = LivingCity.Outfit.DoorTenure;

namespace LivingCity.UI
{
    /// <summary>
    /// THE THREE TAB BODIES of the block drawer: what can be done to this ground, what
    /// it is worth, and what stands on it. Splitting them is what makes the block fit -
    /// stacked in one column they ran to twice the drawer's height and pushed the ledger
    /// beside them off the page.
    ///
    /// ORDERS is verbs and the policy his crew runs them by. THE BOOK is the arithmetic
    /// and the one sentence that says what it comes to. THE DOORS is the premises and
    /// the men standing among them. Every figure and every refusal is read, never
    /// composed: the orders' own words come off
    /// <see cref="TerritoryRacketOrders"/>, their refusals off
    /// <see cref="BlockRacketSeam"/>, and the policy's readings off the territory's own
    /// collection-style table.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        // ---------------------------------------------------------------- the orders

        /// <summary>One order as the drawer offers it: the words, what it costs, and
        /// either the verb that fires it or the chip and the reason it cannot.</summary>
        readonly struct BlockOrder
        {
            public BlockOrder(string key, string title, string note, string cost,
                string chip, string reason, System.Action run, string outLine = "")
            {
                Key = key;
                Title = title;
                Note = note;
                Cost = cost;
                Chip = chip;
                Reason = reason;
                Run = run;
                OutLine = outLine ?? "";
            }

            /// <summary>The seam's own key for this order - "shakedown", "round",
            /// "lean" - and what <see cref="blockRacketOutKey"/> is matched against.
            /// Never the label: the label is words for a reader and moves with the
            /// copy, and matching on it meant the OUT line never printed at all.
            /// </summary>
            public string Key { get; }

            public string Title { get; }
            public string Note { get; }
            public string Cost { get; }

            /// <summary>The one word the blocked row's chip carries.</summary>
            public string Chip { get; }

            /// <summary>Why it cannot fire, in the words of whatever refused it. Empty
            /// when it can.</summary>
            public string Reason { get; }

            /// <summary>What the office said when this order went out, and the whole
            /// of what says it is still out. It lives ON the order and not in a test the
            /// row does for itself, because the footer's one key chooses from this same
            /// list: a row that had gone inert while the key beside it still offered the
            /// same order was the drawer contradicting itself.</summary>
            public string OutLine { get; }

            public bool Out => OutLine.Length > 0;

            /// <summary>Null when the order cannot go - either refused, or already out.
            /// </summary>
            public System.Action Run { get; }
        }

        readonly System.Collections.Generic.List<BlockOrder> blockOrders =
            new System.Collections.Generic.List<BlockOrder>();

        /// <summary>
        /// THE FOUR ORDERS. Only four, because the two that used to sit among them -
        /// who carries the bag, who answers for the block - are not orders at all and
        /// now stand in the roles strip, and MARK IT ON THE MAP is the footer's own key.
        ///
        /// The label and the note come off the shared order table and never a literal
        /// here: the door menu prints the same rows, and two surfaces that word one
        /// order differently are two surfaces describing two different orders.
        /// </summary>
        void ReadBlockOrders()
        {
            blockOrders.Clear();
            var crewId = WalkingCrewId();
            var bagCrewId = ResponsibleCrewId();
            var block = blockCardId;
            var source = BlockRacketSeam.SourceOrStub;
            var actions = BlockRacketSeam.ActionsOrStub;
            var leader = ResponsibleLeader();

            var unpaid = DoorsNotPaying();
            Add("shakedown", TerritoryRacketOrders.ShakeDownLabel,
                TerritoryRacketOrders.ShakeDownNote,
                unpaid + (unpaid == 1 ? " door" : " doors"),
                source.Refusal("shakedown", crewId, block),
                () => FireRacketOrder("shakedown", crewId,
                    () => actions.ShakeDown(crewId, block)));

            Add("round", "SEND THE ROUND NOW", TerritoryRacketOrders.RoundNote,
                "off schedule",
                source.Refusal("round", bagCrewId, block),
                () => FireRacketOrder("round", bagCrewId,
                    () => actions.SendRound(bagCrewId, block)));

            var holdouts = blockRacketOk ? blockRacket.Holdouts : 0;
            Add("lean", TerritoryRacketOrders.LeanLabel, TerritoryRacketOrders.LeanNote,
                holdouts + (holdouts == 1 ? " door" : " doors"),
                source.Refusal("lean", crewId, block),
                () => FireRacketOrder("lean", crewId,
                    () => actions.LeanOnHoldouts(crewId, block)));

            // PUT A MAN ON IT stays available with nobody named. It is how men get onto
            // a block in the first place, and a boss who cannot do it until he has named
            // a lieutenant cannot take ground he has nobody spare for.
            Add("men", "PUT A MAN ON IT",
                "one more of ours stands on this block · presence, not paper",
                blockCardShort > 0
                    ? blockCardShort + (blockCardShort == 1 ? " man short" : " men short")
                    : "presence",
                "", FileMenOntoBlock);

            void Add(string orderKey, string title, string note, string cost,
                string refusal, System.Action run)
            {
                // An order the office already has is neither available nor refused: it
                // is OUT, on THIS block, until the seam's version moves.
                if (blockRacketOutKey == orderKey && blockRacketOutBlock == block)
                {
                    blockOrders.Add(new BlockOrder(orderKey, title, note, cost, "", "",
                        null, blockRacketOutLine));
                    return;
                }
                blockOrders.Add(string.IsNullOrEmpty(refusal)
                    ? new BlockOrder(orderKey, title, note, cost, "", "", run)
                    : new BlockOrder(orderKey, title, note, cost,
                        ChipFor(orderKey, refusal), refusal, null));
            }

            // The chip is the state in ONE word; the reason under it is the sentence the
            // system that refused it used. The order the tests run in is the design's:
            // a later condition wins the chip, because it is the nearer obstacle.
            string ChipFor(string orderKey, string refusal)
            {
                var word = "BLOCKED";
                if (!leader.IsValid)
                    word = "NO PAPER";
                if (orderKey == "round")
                {
                    if (bagCrewId >= 0 && (!blockRacketOk || blockRacket.CollectorId < 0))
                        word = "NO BAG MAN";
                    if (blockRacketOk && blockRacket.RoundOut)
                        word = "OUT NOW";
                }
                return refusal.Length > 0 ? word : "";
            }
        }

        /// <summary>How many doors on this block do not pay us yet - what a shakedown
        /// would be asking at. Counted off the standings the racket published, so the
        /// figure on the key is the one the round would actually walk.</summary>
        int DoorsNotPaying()
        {
            var count = 0;
            for (var i = 0; i < blockCardTrades.Count; i++)
            {
                var id = blockCardTrades[i].Id;
                if (!blockStandings.TryGetValue(id, out var standing))
                {
                    if (blockCardTrades[i].Tenure != BlockTenure.Paying &&
                        blockCardTrades[i].Tenure != BlockTenure.Ours)
                        count++;
                    continue;
                }
                if (standing.Kind != DoorStandingKind.Paying &&
                    standing.Kind != DoorStandingKind.Rival &&
                    standing.Kind != DoorStandingKind.Shut)
                    count++;
            }
            return count;
        }

        /// <summary>The order the footer's one red key gives: the first of the four that
        /// can actually go out today. A key whose verb is not named is a key nobody
        /// presses, so the footer prints its title beside it.</summary>
        BlockOrder PrimaryOrder()
        {
            ReadBlockOrders();
            for (var i = 0; i < blockOrders.Count; i++)
                if (blockOrders[i].Run != null)
                    return blockOrders[i];
            return default;
        }

        float BuildOrdersTab(RectTransform body)
        {
            ReadBlockOrders();
            var y = 0f;
            for (var i = 0; i < blockOrders.Count; i++)
                y += BlockOrderRow(body, y, blockOrders[i]);

            y += BuildPolicyPanel(body, y);
            return y + 12f;
        }

        /// <summary>
        /// One order row. Two mutually exclusive renderings, and a third for the key that
        /// is already OUT: an order that has fired stands as a status line until the
        /// seam's version moves and it can be given again.
        ///
        /// A blocked row is dimmed AND carries a stated reason - never dimmed alone. It
        /// has no click handler at all, so there is nothing for a reader to press and be
        /// silently ignored by.
        /// </summary>
        float BlockOrderRow(RectTransform body, float top, BlockOrder order)
        {
            if (order.Out)
            {
                var strip = NewRect("Order out", body);
                PlaceTopLeft(strip, 0f, -top, blockCardW, 36f);
                Fill(strip, LedgerV2.PanelDark);
                Rule(strip, 0f, -35f, blockCardW, LedgerV2.Hair);
                Caps(strip, BlockPad, -12f, blockCardW - BlockPad * 2f,
                        order.Title + " · OUT · " + order.OutLine, 10.8f, LedgerV2.Amber,
                        10f)
                    .font = LedgerStyle.Mono;
                return 36f;
            }

            var can = order.Run != null;
            var row = NewRect("Order " + order.Title, body);
            PlaceTopLeft(row, 0f, -top, blockCardW, 1f);
            Fill(row, can ? LedgerV2.Panel : LedgerV2.PanelDark);
            if (can)
                RowButton(row, ClickSurface(row), () => order.Run());

            // The right rail: what the order costs over the chevron that says it is a
            // control. A blocked row prints a dash instead - there is nothing to follow.
            const float railW = 96f;
            LedgerV2.Mono(row, blockCardW - BlockPad - railW, -10f, railW, order.Cost,
                10.8f, LedgerV2.Label, 10f, TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(row, blockCardW - BlockPad - railW, -26f, railW,
                can ? "›" : "—", 13f, can ? LedgerV2.Faint : LedgerV2.SheetRule, 0f,
                TextAlignmentOptions.MidlineRight);

            var textW = blockCardW - BlockPad * 2f - railW - 10f;
            Caps(row, BlockPad, -10f, textW, order.Title, 13.2f,
                can ? LedgerV2.Ink : LedgerV2.Label, 13f).font = LedgerStyle.MonoBold;
            var copy = LedgerV2.Copytext(row, BlockPad, -30f, textW, 60f, order.Note,
                12.8f, can ? LedgerV2.Body : LedgerV2.Muted);
            var height = 30f + Mathf.Max(16f, copy.preferredHeight);

            if (!can)
            {
                var chipW = Mathf.Ceil(LedgerV2.MonoWidth(order.Chip, 10.5f, 6f)) + 20f;
                LedgerV2.Status(row, BlockPad, -(height + 2f), chipW, 20f, order.Chip,
                    LedgerV2.Head, 10.5f);
                LedgerV2.Mono(row, BlockPad + chipW + 8f, -(height + 4f),
                        textW - chipW - 8f, order.Reason, 10.8f, LedgerV2.Red, 10f)
                    .overflowMode = TextOverflowModes.Ellipsis;
                height += 26f;
            }

            height += 11f;
            row.sizeDelta = new Vector2(blockCardW, height);
            Rule(row, 0f, -(height - 1f), blockCardW, LedgerV2.Hair);
            return height;
        }

        // ---------------------------------------------------------------- the policy

        static readonly string[] BlockPolicies =
            { "Lenient", "Normal", "Strict", "Brutal" };

        /// <summary>What each policy does, in the words a boss gives the order in. The
        /// pips beside them are NOT written here - they are struck off the territory's
        /// own collection-style table, so the sentence and the reading cannot drift
        /// apart.</summary>
        static string PolicyLine(CrewPolicy policy) => policy switch
        {
            CrewPolicy.Lenient =>
                "A no is taken for now. The door is asked again next round. Nothing " +
                "breaks, nothing draws police.",
            CrewPolicy.Strict =>
                "A no gets a window put through the same night. Fear climbs, and so " +
                "does police attention.",
            CrewPolicy.Brutal =>
                "A no gets the man himself, not his window. Heavy fear, heavy heat, " +
                "and the whole ward talks.",
            _ =>
                "A no gets one warning and a second knock. A short payment is taken " +
                "and noted against the door.",
        };

        /// <summary>A policy's fear and heat as a count of marks out of four, measured
        /// against the heaviest policy there is. Never a figure chosen for the drawing:
        /// Brutal is four by definition and the rest read where they actually fall.
        /// </summary>
        static void PolicyMarks(CrewPolicy policy, out int fear, out int heat)
        {
            var style = TerritoryCollectionStyle.OfPolicy((int)policy);
            var worst = TerritoryCollectionStyle.OfPolicy((int)CrewPolicy.Brutal);
            fear = Marks(style.FearLeft, worst.FearLeft);
            heat = Marks(style.HeatLeft, worst.HeatLeft);

            static int Marks(float value, float cap) =>
                cap <= 0f || value <= 0f
                    ? 0
                    : Mathf.Clamp(Mathf.CeilToInt(value / cap * 4f), 1, 4);
        }

        float BuildPolicyPanel(RectTransform body, float top)
        {
            var crewId = ResponsibleCrewId();
            var panel = NewRect("Policy", body);
            PlaceTopLeft(panel, 0f, -top, blockCardW, 1f);
            Fill(panel, LedgerV2.PanelBand);

            var width = blockCardW - BlockPad * 2f;
            var y = 11f;
            y += SectionBar(panel, BlockPad, y, width, "POLICY", "ON A SHORT OR A NO");

            var policy = blockRacketOk ? blockRacket.Policy : CrewPolicy.Normal;
            LedgerV2.Segmented(panel, BlockPad, -y, 26f, BlockPolicies, (int)policy,
                index =>
                {
                    var refusal = BlockRacketSeam.ActionsOrStub.SetPolicy(
                        crewId, (CrewPolicy)index);
                    if (!string.IsNullOrEmpty(refusal))
                        SayOnTheBlockCard(refusal);
                    dirty = true;
                }, width / BlockPolicies.Length, 9.5f);
            y += 26f + 9f;

            // The sentence and the two readings stand side by side, the design's 200 of
            // copy against a 130 column of marks. Under that the column would be two
            // words wide, so the readings drop under the sentence instead.
            const float readingW = 130f;
            var copyW = width - readingW - 14f;
            var readings = y;
            if (copyW < 180f)
            {
                copyW = width;
                readings = 0f;
            }

            var copy = LedgerV2.Copytext(panel, BlockPad, -y, copyW, 70f,
                PolicyLine(policy), 12.8f, LedgerV2.Body);
            var copyH = Mathf.Max(20f, copy.preferredHeight);

            PolicyMarks(policy, out var fear, out var heat);
            var readingX = readings > 0f ? BlockPad + copyW + 14f : BlockPad;
            if (readings <= 0f)
                readings = y + copyH + 8f;
            Reading(readingX, readings, "FEAR", fear, LedgerV2.Red);
            Reading(readingX, readings + 16f, "HEAT", heat, LedgerV2.Amber);

            var height = Mathf.Max(y + copyH, readings + 32f) + 14f;
            panel.sizeDelta = new Vector2(blockCardW, height);
            return height;

            void Reading(float x, float rowY, string label, int filled, Color ink)
            {
                LedgerV2.Mono(panel, x, -rowY, readingW - 50f, label, 10.8f,
                    LedgerV2.Label, 10f);
                LedgerV2.Pips(panel, x + readingW - LedgerV2.PipsWidth(4),
                    -(rowY + 8f), 4, filled, ink);
            }
        }

        /// <summary>The design's own section head: a mono label, a hairline that runs to
        /// the aside, and the aside held to the right margin.</summary>
        float SectionBar(RectTransform panel, float x, float top, float width,
            string label, string aside, Color? asideInk = null)
        {
            var labelW = Mathf.Ceil(LedgerV2.MonoWidth(label, 10.8f, 18f)) + 6f;
            var asideW = aside.Length > 0
                ? Mathf.Ceil(LedgerV2.MonoWidth(aside, 10.8f, 10f)) + 6f
                : 0f;
            Caps(panel, x, -top, labelW, label, 10.8f, LedgerV2.Ink, 18f)
                .font = LedgerStyle.MonoBold;
            if (asideW > 0f)
                Caps(panel, x + width - asideW, -top, asideW, aside, 10.8f,
                        asideInk ?? LedgerV2.Faint, 10f,
                        TextAlignmentOptions.MidlineRight)
                    .font = LedgerStyle.Mono;
            var rule = width - labelW - asideW - 20f;
            if (rule > 8f)
                Rule(panel, x + labelW + 10f, -(top + 8f), rule, LedgerV2.Rule);
            return 20f;
        }

        // ------------------------------------------------------------------ the book

        /// <summary>
        /// THE BOOK: what the block is worth and what it costs, and then the one
        /// sentence that says what that adds up to. Two bounded readings as a count of
        /// marks, everything else as a figure over a dotted leader - the design's rule
        /// that a bounded reading is never a percentage bar.
        /// </summary>
        float BuildBookTab(RectTransform body)
        {
            var width = blockCardW - BlockPad * 2f;
            var y = BlockPad;

            var standing = blockCardHands.Count;
            var wanted = standing + blockCardShort;
            y += LedgerV2.Meter(body, BlockPad, -y, width, "Men standing on it",
                standing, Mathf.Max(1, wanted), "man", "men") + 10f;

            if (blockCardHeatCap > 0f)
            {
                var marks = Mathf.RoundToInt(
                    Mathf.Clamp01(blockCardHeat / blockCardHeatCap) * 10f);
                y += LedgerV2.Meter(body, BlockPad, -y, width, "Heat on this ground",
                    marks, 10, "mark", "marks") + 10f;
            }

            if (blockRacketOk && blockRacket.RoundOut && blockRacket.RoundStops > 0)
            {
                y += LedgerV2.Meter(body, BlockPad, -y, width, "Doors done on the round",
                    blockRacket.RoundCursor, blockRacket.RoundStops, "door", "doors");
                LedgerV2.Mono(body, BlockPad, -y, width,
                    LedgerText.Cash(blockRacket.RoundCarried) + " in the bag · " +
                    blockRacket.RoundCollectorName, 10.8f, LedgerV2.Amber, 1f);
                y += 18f;
            }

            y += 2f;
            Rule(body, BlockPad, -y, width, LedgerV2.Rule);
            y += 11f;

            // MONEY WALKS, and the figures are set in the order it travels, so nobody
            // can read a door's take as money in the safe: owed at the doors, carried in
            // the bag, banked this week, banked ever.
            var leader = ResponsibleLeader();
            if (blockRacketOk)
            {
                y += Fact(body, BlockPad, y, width, "OWED AT THE DOORS",
                    LedgerText.Cash(blockRacket.Owed),
                    blockRacket.Owed > 0 ? LedgerV2.Red : LedgerV2.Muted);
                y += Fact(body, BlockPad, y, width, "IN THE BAG",
                    blockRacket.RoundOut ? LedgerText.Cash(blockRacket.InTheBag) : "—",
                    blockRacket.RoundOut ? LedgerV2.Amber : LedgerV2.Muted);
                y += Fact(body, BlockPad, y, width, "BANKED THIS WEEK",
                    LedgerText.Cash(blockRacket.BankedThisWeek),
                    blockRacket.BankedThisWeek > 0 ? LedgerV2.Green : LedgerV2.Muted);
                y += Fact(body, BlockPad, y, width, "BANKED ALL GAME",
                    LedgerText.Cash(blockRacket.BankedAllGame),
                    blockRacket.BankedAllGame > 0 ? LedgerV2.Green : LedgerV2.Muted);

                // A block with a lieutenant on it and nobody carrying the bag earns
                // nothing at all, and the book says so in red rather than printing a
                // blank where a weekday should be.
                var noCollector = leader.IsValid && blockRacket.CollectsWeekday < 0;
                y += Fact(body, BlockPad, y, width, "COLLECTS",
                    noCollector ? "NOBODY ON THE BAG"
                        : blockRacket.CollectsWord.Length > 0
                            ? blockRacket.CollectsWord
                            : "nothing to collect yet",
                    noCollector ? LedgerV2.Red : LedgerV2.Ink);

                if (blockRacket.LastRoundDay > 0)
                    y += Fact(body, BlockPad, y, width, "LAST ROUND",
                        "day " + blockRacket.LastRoundDay + " · " +
                        LedgerText.Cash(blockRacket.LastRoundBanked) + " · " +
                        blockRacket.LastRoundShort + " short", LedgerV2.Muted);
            }

            y += Fact(body, BlockPad, y, width, "TAKE A DAY, STANDING",
                LedgerText.Cash(blockCardTake),
                blockCardTake > 0 ? LedgerV2.Green : LedgerV2.Red);
            y += Fact(body, BlockPad, y, width, "WAGES STANDING HERE",
                LedgerText.Cash(blockCardWages) + " / day",
                blockCardWages > 0 ? LedgerV2.Red : LedgerV2.Muted);
            y += Fact(body, BlockPad, y, width, "PREMISES ON IT",
                blockCardTrades.Count.ToString(), LedgerV2.Ink);

            // NET is banked against wages over the SAME week - not a day's take against
            // a day's wages, because a take is not money until it is banked.
            var net = (blockRacketOk ? blockRacket.BankedThisWeek : 0) -
                      blockCardWages * 7;
            y += Fact(body, BlockPad, y, width, "NET OFF THIS BLOCK",
                LedgerText.Cash(net) + " / week",
                net < 0 ? LedgerV2.Red : LedgerV2.Green);

            // THE VERDICT. The amber lives in the rule beside it and never in the words:
            // amber type on this ground reads at about 3.5:1 and body copy owes 4.5:1.
            y += 13f;
            var verdict = BlockVerdict(leader, out var ink);
            var needing = DoorsNeedingAnswer();
            if (needing == 1)
                verdict += " One door needs an answer.";
            else if (needing > 1)
                verdict += " " + needing + " doors need an answer.";
            var copy = LedgerV2.Copytext(body, BlockPad + 12f, -y, width - 12f, 90f,
                verdict, 13.8f, LedgerV2.Body, italic: true);
            var copyH = Mathf.Max(24f, copy.preferredHeight);
            Block("Verdict rule", body, BlockPad, -y, 3f, copyH, ink);
            y += copyH + 6f;

            // Invented money must never be read as the city's. The stub says so on the
            // page itself, in the same breath as the sentence above it.
            if (BlockRacketSeam.IsStub)
            {
                LedgerV2.Mono(body, BlockPad, -y, width,
                    "(stub figures · no racket is running in this scene)", 10.8f,
                    LedgerV2.Muted, 0.5f);
                y += 18f;
            }
            return y + 18f;
        }

        // ----------------------------------------------------------------- the doors

        float BuildDoorsTab(RectTransform body)
        {
            var width = blockCardW - BlockPad * 2f;
            var y = BlockPad;
            y += BuildBlockTrades(body, BlockPad, y, width);
            y += 13f;
            Rule(body, BlockPad, -y, width, LedgerV2.Rule);
            y += 14f;
            y += BuildBlockHands(body, BlockPad, y, width);
            return y + 18f;
        }
    }
}
