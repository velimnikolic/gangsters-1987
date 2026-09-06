using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Personnel;
using LivingCity.Territory;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// THE DRAWER'S PICKERS - the sheets that open OVER the block: who answers for it,
    /// who carries its bag, who walks its doors, and the menu beside one door.
    ///
    /// They are laid absolutely over the drawer's whole overlay host - the filmed plate
    /// included - and not under the roles strip that opens them. Anchored under that
    /// strip a candidate list gets under two rows of room, and the strip's own height
    /// drifts with the drawer's width, so there is no fixed top to anchor to either.
    ///
    /// Every list obeys one rule the rest of the book obeys: a man who cannot be chosen
    /// is DIMMED AND CARRIES A STATED REASON, and has no click handler at all. A row
    /// that is merely grey teaches nobody why, and a grey row that still answers a click
    /// teaches them the wrong thing.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        /// <summary>The dark band every picker opens with.</summary>
        const float BlockSheetHeadH = 50f;

        /// <summary>The picker footer, where a picker has one.</summary>
        const float BlockSheetFootH = 47f;

        readonly List<Crew> blockSquadCrews = new List<Crew>();
        readonly List<Character> blockSquadMen = new List<Character>();

        /// <summary>
        /// The chrome shared by all four: the ground, the head band with its title, its
        /// dynamic subline and the way out, and the one scrolling list under it.
        /// Answers the list's own content rect, already sized to the room left over.
        /// </summary>
        RectTransform BlockSheetShell(RectTransform host, float inner, string title,
            string subline, float footerH, out float listH)
        {
            var sheet = NewRect("Block sheet", host);
            PlaceTopLeft(sheet, 0f, 0f, blockCardW, inner);
            Fill(sheet, LedgerV2.Panel);
            // It is laid OVER the drawer, so it must also stop the clicks the plate and
            // the rows beneath it would otherwise answer.
            ClickSurface(sheet);

            var band = NewRect("Sheet head", sheet);
            PlaceTopLeft(band, 0f, 0f, blockCardW, BlockSheetHeadH);
            Fill(band, LedgerV2.Head);

            const float closeW = 62f;
            var close = NewRect("Close", band);
            PlaceTopLeft(close, blockCardW - BlockPad - closeW,
                -(BlockSheetHeadH - 22f) * 0.5f, closeW, 22f);
            Fill(close, new Color(0f, 0f, 0f, 0f));
            Frame(close, 1f, LedgerV2.HeadDim);
            RowButton(close, ClickSurface(close), CloseBlockSheet);
            Caps(close, 0f, -3f, closeW, "CLOSE", 10.8f, LedgerV2.HeadCream, 14f,
                TextAlignmentOptions.Center).font = LedgerStyle.MonoBold;

            var titleW = blockCardW - BlockPad * 2f - closeW - 10f;
            Caps(band, BlockPad, -6f, titleW, title, 12.0f, LedgerV2.HeadCream, 18f)
                .font = LedgerStyle.MonoBold;
            LedgerV2.Mono(band, BlockPad, -27f, titleW, subline, 10.8f,
                    LedgerV2.HeadDim, 12f)
                .overflowMode = TextOverflowModes.Ellipsis;

            listH = Mathf.Max(60f, inner - BlockSheetHeadH - footerH);
            blockSheetViewport = NewRect("Sheet window", sheet);
            PlaceTopLeft(blockSheetViewport, 0f, -BlockSheetHeadH, blockCardW, listH);
            blockSheetViewport.gameObject.AddComponent<RectMask2D>();

            blockSheetContent = NewRect("Sheet list", blockSheetViewport);
            PlaceTopLeft(blockSheetContent, 0f, 0f, blockCardW, listH);
            return blockSheetContent;
        }

        /// <summary>Sizes the list to what was printed into it and holds the scroll
        /// inside its own run.</summary>
        void SizeBlockSheet(float cursor, float listH)
        {
            var content = Mathf.Max(listH, cursor);
            blockSheetContent.sizeDelta = new Vector2(blockCardW, content);
            blockSheetScroll = Mathf.Clamp(blockSheetScroll, 0f,
                Mathf.Max(0f, content - listH));
            blockSheetContent.anchoredPosition = new Vector2(0f, blockSheetScroll);
        }

        void CloseBlockSheet()
        {
            blockCardSheet = BlockSheet.None;
            blockCardPick = default;
            DoorMenu.Say("");
            dirty = true;
        }

        /// <summary>The selection marker: the design's filled street square for the man
        /// who has it, an empty box for one who could, and an inert box for one who
        /// cannot be chosen at all.</summary>
        void SheetMark(RectTransform row, float x, float y, bool picked, bool live)
        {
            if (picked)
            {
                LedgerV2.StreetMark(row, x, y, LedgerV2.Red, 12f);
                return;
            }
            var box = NewRect("Mark", row);
            PlaceTopLeft(box, x, y, 12f, 12f);
            Fill(box, live ? LedgerV2.Panel : LedgerV2.PanelBand);
            Frame(box, 1f, live ? LedgerV2.SheetRule : LedgerV2.Rule);
        }

        /// <summary>A reading out of five marks, from a 0-100 personal figure.</summary>
        static int MarksOf(int hundred) =>
            hundred <= 0 ? 0 : Mathf.Clamp(Mathf.CeilToInt(hundred / 20f), 1, 5);

        // ------------------------------------------------------- who carries the bag

        /// <summary>
        /// THE BAG (GAN-262). One row per man of the responsible crew: what kind of bag
        /// man he would make, what he costs, his nerve and his loyalty, and whether
        /// anything is holding him. Naming a man takes him OUT of the crew's street line
        /// and stands him at the front, and the note on his row says so rather than
        /// letting the boss find out by watching four men become three.
        /// </summary>
        void BuildCollectorSheet(RectTransform host, float inner)
        {
            var crewId = ResponsibleCrewId();
            var leader = ResponsibleLeader();
            var source = BlockRacketSeam.SourceOrStub;
            source.CollectCrewHoods(crewId, blockCardCrewHands);
            var carried = blockRacketOk && blockRacket.CollectorId >= 0;

            var subline = crewId < 0
                ? "NOBODY ANSWERS FOR THIS BLOCK"
                : ShortLeaderWord(leader) + "'S CREW · " + blockCardCrewHands.Count +
                  (blockCardCrewHands.Count == 1 ? " MAN · " : " MEN · ") +
                  (!carried ? "NOBODY PICKED"
                      : blockRacket.CollectorNamedByBoss ? "YOUR PICK"
                      : ShortLeaderWord(leader) + "'S PICK");

            var footerH = crewId < 0 ? 0f : BlockSheetFootH;
            var list = BlockSheetShell(host, inner, "WHO CARRIES THE BAG", subline,
                footerH, out var listH);

            var y = 0f;
            if (crewId < 0 || blockCardCrewHands.Count == 0)
            {
                LedgerV2.Copytext(list, BlockPad, -14f, blockCardW - BlockPad * 2f, 60f,
                    crewId < 0
                        ? "No crew answers for this block. Name a lieutenant first - the " +
                          "bag is carried by one of his own men."
                        : "He has no men to give it to.", 12.8f, LedgerV2.Red);
                SizeBlockSheet(90f, listH);
                return;
            }

            for (var i = 0; i < blockCardCrewHands.Count; i++)
                y += CandidateRow(list, y, blockCardCrewHands[i], crewId);
            SizeBlockSheet(y, listH);

            if (footerH <= 0f)
                return;

            var foot = NewRect("Sheet foot", host);
            PlaceTopLeft(foot, 0f, -(inner - footerH), blockCardW, footerH);
            Fill(foot, LedgerV2.PanelBand);
            Rule(foot, 0f, 0f, blockCardW, LedgerV2.Rule);

            var pickWord = "LET " + ShortLeaderWord(leader) + " PICK";
            var pickW = Mathf.Min(blockCardW * 0.5f,
                LedgerV2.ButtonWidth(pickWord, 10.5f));
            LedgerV2.Button(foot, pickWord, BlockPad, -9f, pickW, 28f,
                () =>
                {
                    CloseBlockSheet();
                    var responsible = BlockMissionChoice.ResponsibleCrew(
                        director?.Roster, blockCardId);
                    var refusal = responsible?.Id != crewId
                        ? "this block now answers to another leader"
                        : BlockRacketSeam.ActionsOrStub.LetLieutenantPick(crewId);
                    if (!string.IsNullOrEmpty(refusal))
                        SayOnTheBlockCard(refusal);
                }, LedgerV2.Key.Outline, 10.5f);

            var offW = LedgerV2.ButtonWidth("OFF THE BAG", 10.5f);
            var off = LedgerV2.Button(foot, "OFF THE BAG",
                BlockPad + pickW + 8f, -9f, offW, 28f,
                () =>
                {
                    CloseBlockSheet();
                    var holder = blockRacket.CollectorId;
                    var refusal = BlockMissionChoice.BagRefusal(
                                      director?.Roster, blockCardId, holder) ??
                                  BlockRacketSeam.ActionsOrStub.TakeOffTheBag(holder);
                    if (!string.IsNullOrEmpty(refusal))
                        SayOnTheBlockCard(refusal);
                }, LedgerV2.Key.Ghost, 10.5f);
            SetActionEnabled(off, carried);
        }

        float CandidateRow(RectTransform list, float top, CrewHandView hand, int crewId)
        {
            var live = hand.Selectable && !hand.Carries;
            var row = NewRect("Man " + hand.Name, list);
            PlaceTopLeft(row, 0f, -top, blockCardW, 1f);
            Fill(row, hand.Selectable ? LedgerV2.Panel : LedgerV2.PanelDark);
            if (live)
            {
                var manId = hand.Id;
                RowButton(row, ClickSurface(row), () =>
                {
                    CloseBlockSheet();
                    var refusal = BlockMissionChoice.BagRefusal(
                                      director?.Roster, blockCardId, manId) ??
                                  BlockRacketSeam.ActionsOrStub.NameCollector(
                                      crewId, manId);
                    if (!string.IsNullOrEmpty(refusal))
                        SayOnTheBlockCard(refusal);
                });
            }

            SheetMark(row, 11f, -11f, hand.Carries, hand.Selectable);

            var chipW = hand.Carries
                ? Mathf.Ceil(LedgerV2.MonoWidth("Carries it", 10.5f, 6f)) + 20f
                : 0f;
            if (hand.Carries)
                LedgerV2.Status(row, blockCardW - BlockPad - chipW, -8f, chipW, 20f,
                    "Carries it", LedgerV2.Red, 10.5f);

            var textX = 32f;
            var textW = blockCardW - textX - BlockPad - (chipW > 0f ? chipW + 8f : 0f);
            var ink = hand.Selectable ? LedgerV2.Ink : LedgerV2.Label;
            LedgerV2.Name(row, textX, -8f, textW, hand.Name, 17.4f, ink);

            var meta = hand.Rank.Length > 0 ? hand.Rank.ToUpperInvariant() : "OF THE CREW";
            if (hand.WageADay > 0)
                meta += " · " + LedgerText.Cash(hand.WageADay) + " / DAY";
            LedgerV2.Mono(row, textX, -29f, textW, meta, 10.8f, LedgerV2.Faint, 10f)
                .overflowMode = TextOverflowModes.Ellipsis;

            // WHAT MAKES A BAG MAN, in words: the three trades the simulation actually
            // sums (streetwise, persuasion, awareness), and the warning that naming him
            // takes him out of the street line.
            var stars = (hand.FitnessHalfSteps / 6f).ToString("0.0");
            var note = "Streetwise, persuasion and awareness together read " + stars +
                       " of five for the bag.";
            if (hand.WalksTheStreet)
                note += " He walks the crew's street line today and would leave it.";
            var copy = LedgerV2.Copytext(row, textX, -46f, textW, 60f, note, 11.8f,
                LedgerV2.Muted);
            var height = 46f + Mathf.Max(16f, copy.preferredHeight) + 4f;

            if (!hand.Selectable)
            {
                var busyW = Mathf.Ceil(LedgerV2.MonoWidth("On a job", 10.5f, 6f)) + 20f;
                LedgerV2.Status(row, textX, -height, busyW, 20f, "On a job",
                    LedgerV2.Amber, 10.5f);
                LedgerV2.Mono(row, textX + busyW + 9f, -(height + 2f),
                        textW - busyW - 9f, hand.BusyReason, 10.8f, LedgerV2.Red, 10f)
                    .overflowMode = TextOverflowModes.Ellipsis;
                height += 24f;
            }
            else
            {
                Reading(textX, height, "NERVE", MarksOf(hand.Nerve), LedgerV2.Red);
                Reading(textX + 118f, height, "LOYAL", MarksOf(hand.Loyal),
                    LedgerV2.Green);
                height += 18f;
            }

            height += 9f;
            row.sizeDelta = new Vector2(blockCardW, height);
            Rule(row, 0f, -(height - 1f), blockCardW, LedgerV2.Hair);
            return height;

            void Reading(float x, float rowY, string label, int filled, Color pip)
            {
                LedgerV2.Mono(row, x, -rowY, 46f, label, 10.8f, LedgerV2.Label, 10f);
                LedgerV2.Pips(row, x + 50f, -(rowY + 8f), 5, filled, pip);
            }
        }

        // ------------------------------------------------------- who walks the doors

        /// <summary>
        /// WHO GOES on a field order - the same crews and the same lone men the door
        /// menu offers, off <see cref="BlockMissionChoice"/>, so the two surfaces can
        /// never send different men. Picking one closes the sheet.
        /// </summary>
        void BuildSquadSheet(RectTransform host, float inner)
        {
            var roster = director != null ? director.Roster : null;
            BlockMissionChoice.Collect(roster, blockCardId, true, blockSquadCrews,
                blockSquadMen, CrewMissionPicker.Physical());

            var total = blockSquadCrews.Count + blockSquadMen.Count;
            var list = BlockSheetShell(host, inner, "WHO WALKS THE DOORS",
                total == 0 ? "NOBODY IS FREE TO SEND"
                    : total + (total == 1 ? " CHOICE" : " CHOICES") +
                      " · THE BLOCK'S OWN PAPER FIRST",
                0f, out var listH);

            var y = 0f;
            if (total == 0)
            {
                LedgerV2.Copytext(list, BlockPad, -14f, blockCardW - BlockPad * 2f, 60f,
                    "No crew of ours is on the street to send here, and no man is free " +
                    "to go alone.", 12.8f, LedgerV2.Red);
                SizeBlockSheet(90f, listH);
                return;
            }

            var fallback = WalkingCrewId();
            for (var i = 0; i < blockSquadCrews.Count; i++)
            {
                var crew = blockSquadCrews[i];
                var lieutenant = roster?.Find(crew.LieutenantId);
                var men = Outfit.CrewKit.MenOf(roster, crew);
                var paper = BlockMissionChoice.ResponsibleLeader(roster, blockCardId) ==
                            crew.LieutenantId;
                var id = crew.Id;
                y += SquadRow(list, y,
                    (lieutenant != null ? lieutenant.FullName : "A crew") + " + crew",
                    men + (men == 1 ? " man · " : " men · ") +
                    (paper ? "the block is his paper" : "borrowed from another ward"),
                    CrewNerve(roster, crew),
                    DoorMenu.SelectedCrewId >= 0
                        ? DoorMenu.SelectedCrewId == id
                        : DoorMenu.SelectedPersonId < 0 && fallback == id,
                    BlockMissionChoice.Refusal(roster, blockCardId, id, true),
                    () =>
                    {
                        if (DoorMenu.SelectedCrewId != id)
                            DoorMenu.ToggleCrew(id);
                        CloseBlockSheet();
                    });
            }

            for (var i = 0; i < blockSquadMen.Count; i++)
            {
                var man = blockSquadMen[i];
                var id = man.Id;
                var busy = roster?.DoorOrders.Find(id) != null
                    ? "already on a doorstep errand"
                    : null;
                y += SquadRow(list, y, man.FullName + ", alone",
                    "one man · " + man.Rank.ToString().ToLowerInvariant() +
                    " · quiet, slower", MarksOf(man.Courage),
                    DoorMenu.SelectedPersonId == id, busy,
                    () =>
                    {
                        if (DoorMenu.SelectedPersonId != id)
                            DoorMenu.TogglePerson(id);
                        CloseBlockSheet();
                    });
            }
            SizeBlockSheet(y, listH);
        }

        /// <summary>What a crew's nerve reads, out of five: the men who would actually
        /// walk in, averaged - not the lieutenant's own, because it is not the
        /// lieutenant who stands at the door.</summary>
        static int CrewNerve(Roster roster, Crew crew)
        {
            if (roster == null || crew == null)
                return 0;
            var total = 0;
            var men = 0;
            for (var i = 0; i < crew.HoodIds.Count; i++)
            {
                var man = roster.Find(crew.HoodIds[i]);
                if (man == null || man.Gone)
                    continue;
                total += man.Courage;
                men++;
            }
            var lieutenant = roster.Find(crew.LieutenantId);
            if (lieutenant != null && !lieutenant.Gone)
            {
                total += lieutenant.Courage;
                men++;
            }
            return men == 0 ? 0 : MarksOf(total / men);
        }

        float SquadRow(RectTransform list, float top, string name, string meta,
            int nerve, bool picked, string refusal, System.Action pick)
        {
            const float rowH = 50f;
            var live = string.IsNullOrEmpty(refusal);
            var row = NewRect("Squad " + name, list);
            PlaceTopLeft(row, 0f, -top, blockCardW, rowH);
            Fill(row, live ? LedgerV2.Panel : LedgerV2.PanelDark);
            Rule(row, 0f, -(rowH - 1f), blockCardW, LedgerV2.Hair);
            if (live)
                RowButton(row, ClickSurface(row), () => pick());

            SheetMark(row, 11f, -(rowH - 12f) * 0.5f, picked && live, live);

            var pipsW = LedgerV2.PipsWidth(5);
            var textW = blockCardW - 32f - BlockPad - pipsW - 12f;
            LedgerV2.Name(row, 32f, -6f, textW, name, 17.4f,
                live ? LedgerV2.Ink : LedgerV2.Label);
            LedgerV2.Mono(row, 32f, -27f, textW, live ? meta : refusal, 10.8f,
                    live ? LedgerV2.Muted : LedgerV2.Red, 10f)
                .overflowMode = TextOverflowModes.Ellipsis;
            if (live)
                LedgerV2.Pips(row, blockCardW - BlockPad - pipsW, -rowH * 0.5f, 5, nerve,
                    LedgerV2.Red);
            return rowH;
        }

        // ------------------------------------------------------- who answers for it

        /// <summary>The lieutenant picker: the same roll ORGANIZATION files against,
        /// with each man's block capacity beside him, and the way to strike a name off.
        /// </summary>
        void BuildLieutenantSheet(RectTransform host, float inner)
        {
            var leaderId = organizationPaper.TryGetValue(blockCardId, out var id)
                ? id
                : -1;
            var query = director != null ? director.Organization : null;
            var list = BlockSheetShell(host, inner, "WHO ANSWERS FOR IT",
                leaderId >= 0
                    ? BlockName(blockCardId).ToUpperInvariant() + " IS ON HIS PAPER"
                    : "NOBODY'S PAPER · THE BLOCK EARNS NOTHING",
                0f, out var listH);

            const float rowH = 40f;
            var y = 0f;
            for (var i = 0; i < organizationLeaders.Count; i++)
            {
                var leader = organizationLeaders[i];
                var target = leader.Id;
                var full = false;
                var capacity = "";
                if (query != null)
                {
                    var blocks = query.CapacityOf(leader.Id).Blocks;
                    full = blocks.Current >= blocks.Maximum;
                    capacity = blocks.Current + " / " + blocks.Maximum +
                               (full ? " · FULL" : " BLOCKS");
                }

                var row = NewRect("Leader " + leader.Name, list);
                PlaceTopLeft(row, 0f, -y, blockCardW, rowH);
                Fill(row, LedgerV2.Panel);
                Rule(row, 0f, -(rowH - 1f), blockCardW, LedgerV2.Hair);
                RowButton(row, ClickSurface(row), () =>
                {
                    CloseBlockSheet();
                    FileBlockResponsibility(blockCardId, target);
                });

                SheetMark(row, 11f, -14f, target == leaderId, true);
                var capW = 116f;
                LedgerV2.Name(row, 32f, -5f, blockCardW - 32f - capW - BlockPad - 8f,
                    leader.Rank == Rank.Boss ? leader.Name + " · YOU" : leader.Name,
                    17.4f, leader.Rank == Rank.Boss ? LedgerV2.Amber : LedgerV2.Ink);
                if (capacity.Length > 0)
                    LedgerV2.Mono(row, blockCardW - BlockPad - capW, -10f, capW,
                        capacity, 10.8f, full ? LedgerV2.Red : LedgerV2.Label, 10f,
                        TextAlignmentOptions.MidlineRight);
                y += rowH;
            }

            if (leaderId >= 0)
            {
                var strike = NewRect("Strike", list);
                PlaceTopLeft(strike, 0f, -y, blockCardW, rowH);
                Fill(strike, LedgerV2.PanelBand);
                Rule(strike, 0f, -(rowH - 1f), blockCardW, LedgerV2.Hair);
                RowButton(strike, ClickSurface(strike), () =>
                {
                    CloseBlockSheet();
                    FileBlockRemoval(blockCardId, leaderId);
                });
                SheetMark(strike, 11f, -14f, false, true);
                LedgerV2.Name(strike, 32f, -5f, blockCardW - 44f,
                    "Nobody · strike the name off", 17.4f, LedgerV2.Red);
                y += rowH;
            }
            SizeBlockSheet(y, listH);
        }

        // -------------------------------------------------------------- one door

        /// <summary>
        /// The picked door's own menu, over the drawer. The panel itself is not this
        /// sheet's - it is <see cref="DoorMenu"/>, the same menu the turf map opens over
        /// a shop, so the two can never offer different rows or send different men. The
        /// drawer only says where it stands, and carries the name and the way out in its
        /// own head band so the menu does not print a second one.
        /// </summary>
        void BuildDoorSheet(RectTransform host, float inner)
        {
            var index = PickedTrade();
            if (index < 0)
            {
                blockCardPick = default;
                return;
            }

            var trade = blockCardTrades[index];
            // The drawer's own band no longer repeats the premises: the menu opens with
            // a file band of its own that names the shop, the file and the day, so the
            // shell keeps only the way out and says which drawer this is.
            var list = BlockSheetShell(host, inner, "THE PREMISES",
                "ONE DOOR ON THIS BLOCK", 0f, out var listH);

            var width = Mathf.Min(blockCardW - BlockPad * 2f, DoorMenu.MaxWidth);
            var panel = DoorMenu.Open(list, trade.Menu, width,
                () => dirty = true, null, DoorDispatch.BlockResponsibility);
            var height = panel.sizeDelta.y;
            PlaceTopLeft(panel, (blockCardW - width) * 0.5f, -BlockPad, width, height);
            SizeBlockSheet(height + BlockPad * 2f, listH);
        }
    }
}
