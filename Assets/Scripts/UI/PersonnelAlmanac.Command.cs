using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using LivingCity.Personnel;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// CHAIN OF COMMAND - ORGANIZATION and PERSONNEL struck on one sheet.
    ///
    /// The two were always halves of one question. ORGANIZATION drew who answers to
    /// whom and then had to send the reader away to find out what any of those men were
    /// carrying; PERSONNEL printed every man's particulars in a roll that had lost the
    /// shape of the outfit by its third line. This sheet draws the tree and prints the
    /// particulars ON it: the Boss at the head, his branches under him, and every man a
    /// leaf on his lieutenant's rail with what he carries, how he is and what he costs
    /// beside his name. Click a name and his file opens IN PLACE, inside the card of
    /// the line it belongs to - several at once, because a boss comparing two men
    /// should not have to shut the first to read the second.
    ///
    /// EVERY MEASUREMENT HERE IS THE DESIGN'S OWN, in the design's own units. The
    /// handoff template is a stack of literal paddings, sizes and gaps and they are
    /// transcribed rather than approximated: the Boss card is fit-content and measured,
    /// not a share of the page; the connectors are two units DASHED; the filter chips
    /// stand beside their hint on the left and not out at the right margin; a branch
    /// column is 400 at most with nine units of padding either side; the man rail is
    /// 30 in with an 18-unit stub; the tail words are bare type, not boxed keys.
    ///
    /// The one thing that is the game's and not the design's is the DATA. No figure on
    /// this page is invented to fill a row the template happens to draw.
    ///
    /// Nothing here is a second authority. Every figure is the same IOrganizationQuery,
    /// Roster and Outfit reading the other two sheets take, and every verb leaves
    /// through the same filing office (FileOrder, in the ORGANIZATION partial): the
    /// page ASKS, the outfit grants or refuses, and the answer prints in ORDERS FILED
    /// at the foot. The ORGANIZATION and PERSONNEL leaves are untouched.
    ///
    /// Two things the design draws are deliberately NOT drawn, because this game holds
    /// no such fact and a sheet that invents one is a sheet that lies:
    ///  - a man cannot be "made a collector" - there is no such standing on the roster;
    ///  - a gun cannot be put straight into a hood's hand. RosterOps issues gear to the
    ///    man who RUNS a branch and his crew deals it out, so the arming block stands on
    ///    the files of the men who can take it and prints the rule on the files of the
    ///    men who cannot.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        // ----------------------------------------------------------------- the sheet
        //
        // The design's head: the title over its caption and a 3-unit rule 56 down
        // from the title, then 8 before the tree starts. Nothing else stands up here -
        // no hint, no filter chips. A sheet that has to tell the reader to click a name
        // is a sheet that does not read, and the roll's SORT and SHOW belong on the
        // PERSONNEL slip, which is the page that keeps a roll.
        const float CommandHeadH = 67f;

        static float CommandTop;
        static float CommandHeight;

        static void MeasureCommandLayout()
        {
            CommandTop = PageTop - CommandHeadH;
            CommandHeight = -(PageBottom - CommandTop);
        }

        // ---- the tree, in the design's numbers ----

        /// <summary>The Don's photograph: 76 across, floating 40 above his card, and
        /// 42 of air over the whole thing.</summary>
        const float BossPortrait = 76f;
        const float BossPortraitLift = 40f;
        const float TreeTopMargin = 42f;

        /// <summary>The Boss card's own padding - 48 over the name, 26 either side.</summary>
        const float BossPadTop = 48f;
        const float BossPadSide = 26f;
        const float BossPadFoot = 14f;

        /// <summary>A lieutenant's photograph: 52 across, floating 30 above his card,
        /// 13 in from its left edge - which is also the card's own padding.</summary>
        const float BranchPortrait = 52f;
        const float BranchPortraitLift = 30f;
        const float BranchPad = 13f;

        /// <summary>A branch column is at most 400 across and carries 9 units of
        /// padding either side, so two neighbours stand 18 apart.</summary>
        const float BranchColumn = 400f;
        const float BranchGutter = 9f;

        /// <summary>Under this a card cannot hold a leaf that reads across, so the tree
        /// stops standing its branches shoulder to shoulder and hangs them off one spine
        /// down the sheet instead. The tree NEVER scrolls sideways.</summary>
        const float BranchMin = 300f;

        /// <summary>The stub between the spine and a branch, and how far the card is
        /// pulled back up into it (the design's margin-top:-28 on a 48-unit stub).
        /// </summary>
        const float BranchStub = 48f;
        const float BranchStubBite = 28f;

        /// <summary>The dashed rail a branch's men hang off, the stub that reaches out
        /// of it to one man, and the air over and under each leaf.</summary>
        const float RailX = 30f;
        const float RailStub = 18f;
        const float LeafMargin = 3f;

        /// <summary>The leaf's own grid: the photograph, a 9-unit gutter, a middle
        /// column that never goes under 120, and the figures on the end of the line.
        ///
        /// The photograph stands the FULL height of the line, top edge to bottom edge,
        /// in the portrait proportion the studio's plates keep everywhere else - a
        /// stamp floating in the middle of a 53-unit line read as an afterthought, and
        /// a face is the first thing this roll is scanned by.
        /// </summary>
        const float LeafPad = 10f;
        const float LeafGap = 9f;
        const float LeafPortraitAspect = 20f / 26f;
        const float LeafNameMin = 120f;
        const float LeafWageW = 44f;
        const float LeafTailW = 40f;
        const float LeafCondW = 46f;

        /// <summary>Under this much room the carry column is all ellipsis, so it is not
        /// printed at all.</summary>
        const float LeafCarryMin = 44f;

        /// <summary>Every connector on this sheet: two units, dashed.</summary>
        const float DashW = 2f;

        RectTransform commandFixed;
        internal RectTransform commandViewport;
        internal RectTransform commandContent;
        internal float commandScroll;

        /// <summary>The full PERSONNEL dossier opened over this sheet. It has its own
        /// surface, but is painted by the one personal-file renderer so the popup and
        /// the PERSONNEL tab can never disagree about a man.</summary>
        int commandDossierId = -1;
        float commandDossierScroll;
        RectTransform commandDossierRoot, commandDossierPanel,
            commandDossierViewport, commandDossierContent, commandDossierFoot,
            commandDossierHoverNote;
        TMP_Text commandDossierFileNo, commandDossierHoverNoteText;

        bool CommandDossierOpen => commandDossierId >= 0 && commandDossierRoot &&
                                   commandDossierRoot.gameObject.activeSelf;

        /// <summary>Whose files are open. A SET and not a selection: opening one man's
        /// file must never shut another's.</summary>
        readonly HashSet<int> commandOpenFiles = new HashSet<int>();

        /// <summary>Whose gun drawer is open under his CARRIES line, or -1. ONE at a
        /// time: it is a drawer pulled out of a file, and two open drawers on the same
        /// sheet is a counter, not a file.</summary>
        int commandArmsOpenId = -1;

        bool commandBossOpen;

        /// <summary>FOLLOW-006's sentence for the open Boss card, measured before the
        /// card is sized and printed inside it - the card is fit-content and cannot be
        /// measured off a line it has not composed yet.</summary>
        string commandReady = "";
        bool commandReadyRoom;

        /// <summary>What the last order said when the roster answered it on the spot -
        /// the arming refusals and purchases that never reach the filing office.</summary>
        string commandNote = "";

        readonly List<OrganizationPerson> commandReserve =
            new List<OrganizationPerson>();
        readonly List<CommandBranch> commandBranches = new List<CommandBranch>();
        readonly List<(CharacterAttribute Attribute, int Steps)> commandTrades =
            new List<(CharacterAttribute, int)>();

        /// <summary>The men the book is shouting about, refilled every repaint by
        /// <see cref="Notability.Top"/>. Read-only over the score - the board is thrown
        /// away and rebuilt precisely so nothing here can keep a figure of its own.</summary>
        readonly List<Character> commandNotable = new List<Character>();

        /// <summary>The head of the reason book, in the order the section sets it -
        /// refilled every repaint by <see cref="ReasonFeed.Latest"/>.</summary>
        readonly List<ReasonLine> commandWords = new List<ReasonLine>();

        /// <summary>How many men the WHO TO LOOK AT panel names. Five or six, never
        /// sixty: the whole point is to answer "who should I be thinking about this
        /// morning" in one glance.</summary>
        const int NotableShown = 6;

        /// <summary>How far back WORD FROM THE CREWS reads. A fortnight of a busy
        /// outfit at the widths these columns run to.</summary>
        const int ReasonsShown = 14;

        /// <summary>The narrowest measure either watch section is set at. Under two of
        /// these and a gutter the pair stands one over the other instead: a reason is a
        /// sentence somebody wrote, and a sentence set across the whole sheet is not
        /// read, it is scanned.</summary>
        const float WatchColumnMin = 520f;

        /// <summary>The air between the two watch columns.</summary>
        const float WatchGutter = 26f;

        /// <summary>Under this measure a section's aside cannot stand beside its own
        /// heading. The ORGANIZATION sheet has always broken at exactly here.</summary>
        const float NarrowSection = 720f;

        /// <summary>
        /// One branch of the tree, gathered before anything is drawn - the spine cannot
        /// be struck until the sheet knows how many branches it has to cross.
        /// </summary>
        sealed class CommandBranch
        {
            public int LeaderId;
            public int CrewId = -1;
            public string Rank;
            public string Name;
            public Color Ink;
            public bool IsDetail;
            public bool IsBag;
            public int BagSlotsUsed;
            public CommandBranch Parent;
            public CommandBranch Bag;
            public bool HasMeters;
            public CapacityMeasure Men;
            public CapacityMeasure Blocks;
            public int Wage;
            public string WageLine;
            public readonly List<OrganizationPerson> Roster =
                new List<OrganizationPerson>();
        }

        // ------------------------------------------------------------------ the page

        void BuildCommandPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Command);
            commandFixed = NewRect("Command Fixed", root);
            Stretch(commandFixed);

            commandViewport = NewRect("Command Window", root);
            PlaceTopLeft(commandViewport, PageLeft, CommandTop, PageWidth, CommandHeight);
            commandViewport.gameObject.AddComponent<RectMask2D>();

            commandContent = NewRect("Command File", commandViewport);
            commandContent.anchorMin = new Vector2(0f, 1f);
            commandContent.anchorMax = new Vector2(1f, 1f);
            commandContent.pivot = new Vector2(0f, 1f);
            commandContent.anchoredPosition = Vector2.zero;
            commandContent.sizeDelta = new Vector2(0f, CommandHeight);

            BuildCommandDossierPopup(root);
        }

        void RebuildCommand()
        {
            if (!commandFixed || !commandContent)
                return;

            if (organizationNote == FiledNote && outfit && outfit.Filings.AwaitingCount == 0)
                organizationNote = "";

            foreach (Transform old in commandFixed)
                Destroy(old.gameObject);
            foreach (Transform old in commandContent)
                Destroy(old.gameObject);

            var query = director != null ? director.Organization : null;
            var roster = director != null ? director.Roster : null;
            if (query == null || roster == null || !query.TryGetBoss(out var boss))
            {
                LedgerV2.PageHead(commandFixed, PageLeft, PageTop, PageWidth,
                    "CHAIN OF COMMAND",
                    "CHAIN OF COMMAND, CAPACITY, AND EVERY MAN WHO DRAWS A WAGE");
                Line(commandContent, LedgerStyle.MonoItalic, 14f, LedgerV2.Red,
                    0f, 0f, PageWidth, 24f,
                    "The command file has no authoritative Boss Character.");
                CloseCommand(24f);
                RebuildCommandDossier();
                return;
            }

            GatherCommand(query, boss);
            BuildCommandHead(query, boss, roster);

            // What the last order came back with. It rides at the top of the sheet and
            // scrolls with it - a standing strip for a line that is usually not there
            // is furniture, and the head carries none any more.
            var cursor = 4f;
            var note = organizationNote.Length > 0 ? organizationNote : commandNote;
            if (note.Length > 0)
            {
                LedgerV2.Mono(commandContent, 0f, -cursor, PageWidth,
                        note.ToUpperInvariant(), 11f, LedgerV2.PaperBlue, 2f)
                    .overflowMode = TextOverflowModes.Ellipsis;
                cursor += LineBox(11f) + 2f;
            }

            // The design's rhythm: 8 under the tree, the reserve 22 down and the order
            // log 26 under that.
            cursor = BuildCommandTree(query, boss, cursor) + 8f;
            cursor = BuildCommandReserve(cursor + 22f);
            cursor = BuildCommandWatch(cursor + 26f);
            cursor = BuildCommandOrders(cursor + 26f);
            CloseCommand(cursor);
            RebuildCommandDossier();
        }

        /// <summary>Builds the modal furniture once. The body is populated on repaint
        /// through RebuildCommandDossier, using the PERSONNEL page's dossier painter.</summary>
        void BuildCommandDossierPopup(RectTransform root)
        {
            commandDossierRoot = NewRect("Personal file popup", root);
            Stretch(commandDossierRoot);

            var shade = Fill(commandDossierRoot, new Color(0f, 0f, 0f, 0.42f));
            shade.raycastTarget = true;
            var dismiss = commandDossierRoot.gameObject.AddComponent<Button>();
            dismiss.targetGraphic = shade;
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(CloseCommandDossier);

            var left = PageLeft + (PageWidth - FileW) * 0.5f;
            commandDossierPanel = LedgerV2.Card("Personal file", commandDossierRoot,
                left, PaneTop, FileW, PaneH);

            // A click on the paper belongs to the file, not the dismissing backdrop.
            var paperFace = PaperOf(commandDossierPanel);
            paperFace.raycastTarget = true;
            var paperButton = commandDossierPanel.gameObject.AddComponent<Button>();
            paperButton.targetGraphic = paperFace;
            paperButton.transition = Selectable.Transition.None;

            var band = NewRect("Head", commandDossierPanel);
            PlaceTopLeft(band, 0f, 0f, FileW, FileHeadH);
            Fill(band, LedgerV2.Head);
            var title = LedgerV2.Mono(band, FilePad,
                -(FileHeadH - 14f) * 0.5f, FileInner - 190f,
                "PERSONAL FILE", 10f, LedgerV2.HeadInk, 13f);
            title.font = LedgerStyle.MonoBold;
            commandDossierFileNo = LedgerV2.Mono(band,
                FilePad + FileInner - 190f, -(FileHeadH - 14f) * 0.5f,
                154f - RightInset, "", 9.5f, LedgerV2.HeadDim, 4f,
                TextAlignmentOptions.MidlineRight);
            var close = LedgerV2.Button(band, "X", FileW - 30f, -2f, 26f, 26f,
                CloseCommandDossier, LedgerV2.Key.Dark, 13f);
            close.color = LedgerV2.HeadInk;

            commandDossierViewport = NewRect("Body", commandDossierPanel);
            PlaceTopLeft(commandDossierViewport, FilePad, -FileHeadH,
                FileInner, FileBodyMax);
            commandDossierViewport.gameObject.AddComponent<RectMask2D>();

            commandDossierContent = NewRect("Content", commandDossierViewport);
            commandDossierContent.anchorMin = new Vector2(0f, 1f);
            commandDossierContent.anchorMax = new Vector2(1f, 1f);
            commandDossierContent.pivot = new Vector2(0f, 1f);
            commandDossierContent.anchoredPosition = Vector2.zero;
            commandDossierContent.sizeDelta = new Vector2(0f, FileBodyMax);

            commandDossierFoot = NewRect("Foot", commandDossierPanel);
            PlaceTopLeft(commandDossierFoot, FilePad,
                -(FileHeadH + FileBodyMax), FileInner, FileFootH);

            commandDossierHoverNote = NewRect("Note", commandDossierPanel);
            PlaceTopLeft(commandDossierHoverNote, 0f, 0f, FileInner - 60f, 60f);
            Fill(commandDossierHoverNote, LedgerV2.Head);
            commandDossierHoverNoteText = Text("Text", commandDossierHoverNote,
                LedgerStyle.Mono, 12f, LedgerV2.HeadInk,
                TextAlignmentOptions.TopLeft);
            Stretch(commandDossierHoverNoteText.rectTransform, 10f);
            commandDossierHoverNoteText.textWrappingMode = TextWrappingModes.Normal;
            commandDossierHoverNote.gameObject.SetActive(false);

            commandDossierRoot.gameObject.SetActive(false);
        }

        /// <summary>Paint the popup with the exact renderer used by PERSONNEL. The
        /// renderer predates reusable surfaces, so its targets are lent to the popup
        /// for this call and restored before returning.</summary>
        void RebuildCommandDossier()
        {
            if (!commandDossierRoot)
                return;

            var roster = director != null ? director.Roster : null;
            var member = commandDossierId >= 0 && roster != null
                ? roster.Find(commandDossierId) : null;
            if (member == null)
            {
                DismissCommandDossier();
                return;
            }

            commandDossierRoot.gameObject.SetActive(true);
            commandDossierRoot.SetAsLastSibling();

            var savedPanel = filePanel;
            var savedViewport = cardViewport;
            var savedContent = cardContent;
            var savedFoot = cardFoot;
            var savedFileNo = cardFileNo;
            var savedHover = hoverNote;
            var savedHoverText = hoverNoteText;
            var savedBodyH = fileBodyH;
            var savedScroll = cardScroll;
            var savedSelection = selectedId;

            filePanel = commandDossierPanel;
            cardViewport = commandDossierViewport;
            cardContent = commandDossierContent;
            cardFoot = commandDossierFoot;
            cardFileNo = commandDossierFileNo;
            hoverNote = commandDossierHoverNote;
            hoverNoteText = commandDossierHoverNoteText;
            cardScroll = commandDossierScroll;
            selectedId = commandDossierId;

            try
            {
                RebuildDetail();
                commandDossierScroll = cardScroll;
            }
            finally
            {
                filePanel = savedPanel;
                cardViewport = savedViewport;
                cardContent = savedContent;
                cardFoot = savedFoot;
                cardFileNo = savedFileNo;
                hoverNote = savedHover;
                hoverNoteText = savedHoverText;
                fileBodyH = savedBodyH;
                cardScroll = savedScroll;
                selectedId = savedSelection;
            }
        }

        void OpenCommandDossier(int memberId)
        {
            if (director == null || director.Roster == null ||
                director.Roster.Find(memberId) == null)
                return;

            commandDossierId = memberId;
            commandDossierScroll = 0f;
            pendingConfirm = Confirm.None;
            lastRefusal = "";
            dirty = true;
        }

        void CloseCommandDossier()
        {
            DismissCommandDossier();
            dirty = true;
        }

        /// <summary>Silent close used while changing leaves or shutting the book.</summary>
        void DismissCommandDossier()
        {
            commandDossierId = -1;
            commandDossierScroll = 0f;
            pendingConfirm = Confirm.None;
            lastRefusal = "";
            if (commandDossierRoot)
                commandDossierRoot.gameObject.SetActive(false);
            if (commandDossierHoverNote)
                commandDossierHoverNote.gameObject.SetActive(false);
        }

        void CloseCommand(float cursor)
        {
            var height = Mathf.Max(CommandHeight, cursor + 30f);
            commandContent.sizeDelta = new Vector2(0f, height);
            commandScroll = Mathf.Clamp(
                commandScroll, 0f, Mathf.Max(0f, height - CommandHeight));
            commandContent.anchoredPosition = new Vector2(0f, commandScroll);
        }

        // ------------------------------------------------------------- the gathering

        /// <summary>
        /// Everything the sheet is about to draw, read ONCE: the leaders, the men, and
        /// the branches they stand on. THE DETAIL is a branch because it IS one - a crew
        /// the Boss leads himself - so the men standing in front of him are not counted
        /// twice in the reserve.
        /// </summary>
        void GatherCommand(IOrganizationQuery query, OrganizationPerson boss)
        {
            organizationLeaders.Clear();
            organizationLeaders.Add(boss);
            query.CollectLieutenants(organizationScratch);
            organizationLeaders.AddRange(organizationScratch);
            query.CollectHoods(organizationPeople);

            if (organizationPickedHoodId >= 0 && !IsPooled(organizationPickedHoodId))
                organizationPickedHoodId = -1;

            var roster = director.Roster;
            commandBranches.Clear();

            // THE DETAIL first: the men standing in front of the Don. It carries no
            // capacity meter of its own - a guard is counted against the BOSS's
            // manpower, and his own card is where that figure is printed.
            //
            // It stands on the sheet EMPTY as well as full. The detail is the Boss's
            // only ordinary branch and the only place a bodyguard can be put under him;
            // his bag appears beside it only once a collector exists. The detail used
            // to be drawn only once somebody was already on it, which left the first
            // guard with nowhere to be posted to. A branch with nobody on it reads
            // perfectly well; a branch that is not there cannot be filed to.
            var detail = director.BodyguardDetail();
            var guards = new CommandBranch
            {
                LeaderId = boss.Id,
                Rank = "THE BOSS'S OWN",
                Name = "THE DETAIL",
                Ink = LedgerV2.Boss,
                IsDetail = true,
                WageLine = "full wages · no earnings",
            };
            if (detail != null)
            {
                for (var i = 0; i < detail.HoodIds.Count; i++)
                {
                    var guard = Person(detail.HoodIds[i]);
                    if (guard.IsValid && CommandShows(guard))
                        guards.Roster.Add(guard);
                }
                guards.CrewId = detail.Id;
                guards.Bag = GatherBagBranch(detail, guards);
            }
            SortCommandMen(guards.Roster);
            commandBranches.Add(guards);
            // The Don's collector is his own command head beside THE DETAIL. It still
            // belongs to the detail crew for posting and capacity, but it must not read
            // as one of the bodyguards hanging underneath that card. Lieutenant bags
            // keep the deeper, nested level below their own branch.
            if (guards.Bag != null)
                commandBranches.Add(guards.Bag);

            for (var i = 1; i < organizationLeaders.Count; i++)
            {
                var leader = organizationLeaders[i];
                var member = roster != null ? roster.Find(leader.Id) : null;
                var capacity = query.CapacityOf(leader.Id);
                var branch = new CommandBranch
                {
                    LeaderId = leader.Id,
                    CrewId = roster.CrewOf(leader.Id)?.Id ?? -1,
                    Rank = "LIEUTENANT",
                    Name = leader.Name,
                    Ink = capacity.IsOverCapacity ? LedgerV2.Red : LedgerV2.Lieutenant,
                    HasMeters = true,
                    Men = capacity.Manpower,
                    Blocks = capacity.Blocks,
                    Wage = member != null ? Outfit.Wages.WageFor(member, RosterDay) : 0,
                };
                branch.WageLine = member != null
                    ? CarriedGun(member) + " · " + ConditionWord(member.Status) + " · " +
                      LedgerText.Cash(branch.Wage) + " / day"
                    : "off the books";

                query.CollectDirectSubordinates(leader.Id, organizationScratch);
                for (var m = 0; m < organizationScratch.Count; m++)
                    if (organizationScratch[m].Rank == Rank.Hood &&
                        roster.Find(organizationScratch[m].Id)?.Duty == Duty.None &&
                        CommandShows(organizationScratch[m]))
                        branch.Roster.Add(organizationScratch[m]);
                var crew = roster.CrewOf(leader.Id);
                branch.Bag = GatherBagBranch(crew, branch);
                SortCommandMen(branch.Roster);
                commandBranches.Add(branch);
            }

            commandReserve.Clear();
            for (var i = 0; i < organizationPeople.Count; i++)
            {
                var person = organizationPeople[i];
                if (person.IsUnassigned && CommandShows(person))
                    commandReserve.Add(person);
            }
            SortCommandMen(commandReserve);
        }

        CommandBranch GatherBagBranch(Crew crew, CommandBranch parent)
        {
            var roster = director != null ? director.Roster : null;
            var collector = crew != null && roster != null ? roster.Find(crew.BagId) : null;
            if (collector == null || collector.Gone)
                return null;

            var bag = new CommandBranch
            {
                LeaderId = collector.Id,
                CrewId = crew.Id,
                Rank = "THE BAG",
                Name = collector.FullName,
                Ink = LedgerV2.Lieutenant,
                IsBag = true,
                BagSlotsUsed = crew.EscortIds.Count,
                Parent = parent,
                Wage = Outfit.Wages.WageFor(collector, RosterDay),
            };
            for (var i = 0; i < crew.EscortIds.Count; i++)
            {
                var escort = Person(crew.EscortIds[i]);
                if (escort.IsValid && CommandShows(escort))
                    bag.Roster.Add(escort);
            }
            SortCommandMen(bag.Roster);
            bag.WageLine = BagWageLine(crew, bag);
            return bag;
        }

        // Pure shape read for the ledger contract: the renderer below consumes these
        // same three figures, so tests can assert the branch without constructing a
        // Canvas or weakening the separation between the model and the page.
        internal static bool HasBagBranch(Crew crew) => crew != null && crew.BagId >= 0;
        internal static int BagBranchLeaves(Crew crew) =>
            HasBagBranch(crew) ? crew.EscortIds.Count : 0;
        internal static int BagBranchEmptyPlaces(Crew crew) =>
            HasBagBranch(crew)
                ? System.Math.Max(0, Crew.MaxEscorts - crew.EscortIds.Count) : 0;

        string BagWageLine(Crew crew, CommandBranch bag)
        {
            var roster = director != null ? director.Roster : null;
            var men = 0;
            var wages = 0;
            var collector = roster != null ? roster.Find(crew.BagId) : null;
            if (collector != null && !collector.Gone)
            {
                men++;
                wages += Outfit.Wages.WageFor(collector, RosterDay);
            }
            for (var i = 0; roster != null && i < crew.EscortIds.Count; i++)
            {
                var escort = roster.Find(crew.EscortIds[i]);
                if (escort == null || escort.Gone)
                    continue;
                men++;
                wages += Outfit.Wages.WageFor(escort, RosterDay);
            }
            var days = new List<int>();
            if (roster != null)
                for (var i = 0; i < roster.Organization.BlockResponsibilities.Count; i++)
                {
                    var row = roster.Organization.BlockResponsibilities[i];
                    if (row.LeaderId != crew.LieutenantId)
                        continue;
                    var day = LivingCity.Territory.TerritoryCollectionSchedule.DayOf(row.BlockId);
                    if (!days.Contains(day))
                        days.Add(day);
                }
            var rounds = "no ground";
            if (days.Count > 0)
            {
                days.Sort();
                var words = new List<string>();
                for (var i = 0; i < days.Count; i++)
                {
                    var word = LivingCity.Territory.TerritoryCollectionSchedule.WordOfDay(days[i]);
                    words.Add(word.Length > 3 ? word.Substring(0, 3) : word);
                }
                rounds = "rounds " + string.Join(" ", words);
            }
            return men + (men == 1 ? " man · " : " men · ") +
                   LedgerText.Cash(wages) + " / day · " + rounds;
        }

        /// <summary>The SHOW chip's answer, applied to one man.</summary>
        bool CommandShows(OrganizationPerson person) => options.Availability switch
        {
            AvailabilityFilter.ActiveOnly => person.IsAvailable,
            AvailabilityFilter.Unavailable => !person.IsAvailable,
            _ => true,
        };

        /// <summary>The SORT chip's answer. ROSTER ORDER leaves a branch in the order
        /// the outfit formed it, which is what the tree is about.</summary>
        void SortCommandMen(List<OrganizationPerson> men)
        {
            var roster = director != null ? director.Roster : null;
            if (roster == null || options.Sort == SortKey.Roster || men.Count < 2)
                return;

            var attribute = options.SortAttribute;
            var byLoyalty = options.Sort == SortKey.Loyalty;
            men.Sort((a, b) =>
            {
                var left = roster.Find(a.Id);
                var right = roster.Find(b.Id);
                var lv = left == null ? -1
                    : byLoyalty ? left.Loyalty : left.GetHalfSteps(attribute);
                var rv = right == null ? -1
                    : byLoyalty ? right.Loyalty : right.GetHalfSteps(attribute);
                return rv.CompareTo(lv);
            });
        }

        // ------------------------------------------------------------------ the head

        /// <summary>
        /// The design's head: the title and its caption on the left, ONE mono line held
        /// to the right margin, the 3-unit rule under both - and, 20 units under the
        /// rule, the hint with the two filter chips standing beside it. The chips sit
        /// WITH the hint on the left, which is where the design puts them; nothing on
        /// that row is held to the right margin.
        /// </summary>
        void BuildCommandHead(
            IOrganizationQuery query, OrganizationPerson boss, Roster roster)
        {
            LedgerV2.PageHead(commandFixed, PageLeft, PageTop, PageWidth,
                "CHAIN OF COMMAND",
                "CHAIN OF COMMAND, CAPACITY, AND EVERY MAN WHO DRAWS A WAGE");

            var capacity = query.CapacityOf(boss.Id);
            var tally = capacity.Manpower.Current + " / " + capacity.Manpower.Maximum +
                        " MEN · " + CountHeldBlocks() + " / " + capacity.Blocks.Maximum +
                        " BLOCKS · " +
                        LedgerText.Cash(Outfit.Wages.DailyPayroll(roster)) +
                        " / DAY · PAID AT MIDNIGHT, WORKED OR NOT";
            var tallyW = Mathf.Min(PageWidth * 0.55f, MonoWidth(tally, 11f, 2f) + 8f);
            LedgerV2.Mono(commandFixed, PageRight - tallyW, PageTop + 1f, tallyW, tally,
                    11f, LedgerV2.Muted, 2f, TextAlignmentOptions.MidlineRight)
                .overflowMode = TextOverflowModes.Ellipsis;

        }

        // ------------------------------------------------------------------ the tree

        float BuildCommandTree(
            IOrganizationQuery query, OrganizationPerson boss, float cursor)
        {
            cursor = BuildCommandBoss(query, boss, cursor);

            if (commandBranches.Count == 0)
            {
                DashDown(commandContent, PageWidth * 0.5f, cursor, 20f);
                Line(commandContent, LedgerStyle.MonoItalic, 12f, LedgerV2.Red,
                    0f, -(cursor + 26f), PageWidth, 22f,
                    "No branch hangs off him. Every man answers to the Boss himself.",
                    TextAlignmentOptions.Center);
                return cursor + 52f;
            }

            // The branch row is at most 400 to a branch and CENTRED under the Boss -
            // two branches make an 800-unit tree in the middle of the sheet, not two
            // half-page slabs.
            var count = commandBranches.Count;
            var column = Mathf.Min(BranchColumn, PageWidth / count);
            return column - BranchGutter * 2f >= BranchMin
                ? BuildCommandRow(cursor, column)
                : BuildCommandStack(cursor);
        }

        /// <summary>The design's tree: a 20-unit drop out of the Boss, the spine struck
        /// across the branch centres, and a 48-unit stub down into each branch whose
        /// last 28 the card is pulled up over. All three are two units, dashed, and all
        /// three are joined.</summary>
        float BuildCommandRow(float cursor, float column)
        {
            const float drop = 20f;

            var count = commandBranches.Count;
            var span = column * count;
            var left = (PageWidth - span) * 0.5f;

            DashDown(commandContent, PageWidth * 0.5f, cursor, drop);
            cursor += drop;

            if (count > 1)
            {
                var first = left + column * 0.5f;
                var last = left + span - column * 0.5f;
                DashAcross(commandContent, first, cursor, last - first);
            }

            var top = cursor + BranchStub - BranchStubBite;
            var tallest = 0f;
            for (var i = 0; i < count; i++)
            {
                var x = left + i * column;
                DashDown(commandContent, x + column * 0.5f, cursor, BranchStub);
                tallest = Mathf.Max(tallest, BuildCommandBranch(commandBranches[i],
                    x + BranchGutter, top, column - BranchGutter * 2f));
            }
            return top + tallest;
        }

        /// <summary>The fallback the design's fifth rule asks for: more branches than
        /// the measure will hold, so they hang off one spine down the sheet and each
        /// takes the full width. Nothing scrolls sideways, ever.</summary>
        float BuildCommandStack(float cursor)
        {
            const float gap = 36f;
            var top = cursor + 4f;
            cursor += BranchPortraitLift + 6f;
            var x = 40f;
            var width = PageWidth - x;

            for (var i = 0; i < commandBranches.Count; i++)
            {
                DashAcross(commandContent, 16f, cursor + 26f, x - 16f);
                cursor += BuildCommandBranch(commandBranches[i], x, cursor, width) + gap;
            }

            DashDown(commandContent, 16f, top, Mathf.Max(0f, cursor - top - gap));
            return cursor;
        }

        // ------------------------------------------------------------- the Boss node

        /// <summary>
        /// The head of the tree. The design gives this card WIDTH:FIT-CONTENT, so it is
        /// measured off what stands in it - his name, the 150-unit rule under it, and
        /// the file's own measure when the file is open - and never stretched to a share
        /// of the sheet. A slab across half the page is not this design.
        /// </summary>
        float BuildCommandBoss(
            IOrganizationQuery query, OrganizationPerson boss, float cursor)
        {
            // 42 of air to the top of his photograph, which floats 40 clear of the card.
            cursor += TreeTopMargin;
            var top = cursor + BossPortraitLift;

            var member = director.Roster != null ? director.Roster.Find(boss.Id) : null;

            // fit-content: the widest thing that will stand in the card, plus padding.
            // The name is MEASURED off the face that will print it, not estimated.
            var inner = Mathf.Max(CondensedWidth(boss.Name, 22f) + 4f, 150f,
                MonoWidth("HEAD OF THE FAMILY", 10f, 3f));
            if (commandBossOpen)
            {
                inner = Mathf.Max(inner, Mathf.Min(PageWidth * 0.4f, 400f));
                // FOLLOW-006's line NAMES men, so it is as long as their names are.
                // fit-content means fit THIS content: measured off the face that will
                // print it, and let the card grow to it rather than cutting the last
                // man off the end of a line whose whole job is naming men.
                commandReady = ReadyForACrewLine(boss, out commandReadyRoom);
                if (commandReady.Length > 0)
                    inner = Mathf.Max(inner, Mathf.Min(PageWidth * 0.72f,
                        MonoWidth("READY FOR A CREW · " + commandReady, 10.5f, 1f) + 6f));
            }
            else
            {
                commandReady = "";
            }
            var w = Mathf.Min(PageWidth, inner + BossPadSide * 2f);
            var x = (PageWidth - w) * 0.5f;

            var card = LedgerV2.Card("Boss", commandContent, x, -top, w, 10f,
                LedgerV2.Head);
            Block("Rank", card, 0f, 0f, w, 4f, LedgerV2.Red);

            RoundFace(card, (w - BossPortrait) * 0.5f, BossPortraitLift, BossPortrait,
                member, "BOSS", LedgerV2.DarkPlate, LedgerV2.HeadDim, LedgerV2.Red);

            var y = BossPadTop;
            var body = w - BossPadSide * 2f;

            var name = Line(card, LedgerStyle.Condensed, 22f, LedgerV2.HeadCream,
                BossPadSide, -y, body, LineBox(22f), boss.Name,
                TextAlignmentOptions.Center);
            name.characterSpacing = 1f;
            name.overflowMode = TextOverflowModes.Ellipsis;
            NameKey(card, BossPadSide, -y, body, LineBox(22f), ToggleCommandBoss);
            y += LineBox(22f) + 6f;

            Block("Name rule", card, (w - 150f) * 0.5f, -y, 150f, 1f, LedgerV2.Red);
            y += 6f;

            var under = Caps(card, BossPadSide, -y, body, "HEAD OF THE FAMILY", 10f,
                LedgerV2.HeadDim, 3f, TextAlignmentOptions.Center);
            under.font = LedgerStyle.Mono;
            y += LineBox(10f);

            if (commandBossOpen)
                y = BuildCommandBossFile(card, query, boss, member, w, y + 12f);

            y += BossPadFoot;
            card.sizeDelta = new Vector2(w, y);
            return top + y;
        }

        /// <summary>
        /// The Boss's own file: what the OUTFIT is, in six figures, then what is true of
        /// him and of no lieutenant. Every figure is somebody else's authority - the
        /// command query's ceilings, the territory reading, the wage book, the safe and
        /// the precinct's own count against him.
        /// </summary>
        float BuildCommandBossFile(Transform card, IOrganizationQuery query,
            OrganizationPerson boss, Character member, float w, float y)
        {
            var inner = w - BossPadSide * 2f;

            DottedRule(card, BossPadSide, -y, inner, LedgerV2.HeadDim);
            y += 11f;

            var capacity = query.CapacityOf(boss.Id);
            var span = member != null
                ? new CapacityMeasure(Command.LieutenantsHeld(director.Roster),
                    Command.LieutenantCap(member))
                : default;
            var held = CountHeldBlocks();
            var heat = outfit ? outfit.Heat : 0;

            var facts = new List<(string Label, string Value, Color Ink)>
            {
                ("MEN ON THE BOOKS",
                    capacity.Manpower.Current + " / " + capacity.Manpower.Maximum,
                    capacity.Manpower.IsOverCapacity
                        ? LedgerV2.Boss : LedgerV2.HeadCream),
                ("LIEUTENANTS", span.Current + " / " + span.Maximum,
                    span.IsOverCapacity ? LedgerV2.Boss : LedgerV2.HeadCream),
                ("BLOCKS HELD", held + " / " + capacity.Blocks.Maximum,
                    held > 0 ? LedgerV2.HeadStreet : LedgerV2.Boss),
                ("PAYROLL",
                    LedgerText.Cash(Outfit.Wages.DailyPayroll(director.Roster)) + " / day",
                    LedgerV2.HeadCream),
                ("IN THE SAFE",
                    outfit ? LedgerText.Cash(outfit.Accounts.Safe) : "--",
                    LedgerV2.HeadPaper),
                ("POLICE HEAT", HeatWord(heat),
                    heat < 25 ? LedgerV2.HeadStreet : LedgerV2.Boss),
            };

            // WHAT HE CARRIES is the last figure on his card and the only one that is
            // also a drawer - the Don is armed at the counter like any other man, and
            // his file was the one file on the sheet with nowhere to do it.
            var carried = member != null ? CarriedGun(member) : "--";
            var armsOpen = member != null && commandArmsOpenId == member.Id;
            facts.Add(("CARRIES",
                member != null ? carried + (armsOpen ? "  ▴" : "  ▾") : carried,
                carried == "nothing" ? LedgerV2.Boss : LedgerV2.HeadCream));
            var carryIndex = facts.Count - 1;

            // The design's auto-fit grid: columns of at least 150 with a 20-unit
            // gutter, and each cell reads label-left, figure-right.
            var columns = Mathf.Max(1, Mathf.FloorToInt((inner + 20f) / 170f));
            var cell = (inner - 20f * (columns - 1)) / columns;
            for (var i = 0; i < facts.Count; i++)
            {
                var cx = BossPadSide + i % columns * (cell + 20f);
                var cy = y + i / columns * 22f;
                var figureW = MonoWidth(facts[i].Value, 11.5f, 0f) + 6f;
                // Centred on the figure's line, not dropped at its top edge.
                var label = Caps(card, cx,
                    LedgerV2.MarkY(-cy, LineBox(11.5f), 9f + 6f),
                    Mathf.Max(20f, cell - figureW - 8f),
                    facts[i].Label, 9f, LedgerV2.HeadDim, 8f);
                label.font = LedgerStyle.Mono;
                label.overflowMode = TextOverflowModes.Ellipsis;
                LedgerV2.Figure(card, cx + cell - figureW, -cy, figureW,
                    facts[i].Value, 11.5f, facts[i].Ink);
                if (i == carryIndex && member != null)
                {
                    var bossId = member.Id;
                    NameKey(card, cx, -cy, cell, 22f, () => ToggleCommandArms(bossId));
                }
            }
            y += (facts.Count + columns - 1) / columns * 22f + 11f;

            // FOLLOW-006. The hoods the book says could run a crew, against the span
            // that is the actual constraint on making one - this is the sheet where a
            // crew is made, so this is where "who do I promote" is answered. It NAMES
            // men and does nothing; MAKE HIM LIEUTENANT is still a key on a man's own
            // file, and the outfit still rules on it.
            if (commandReady.Length > 0)
            {
                var line = LedgerV2.Mono(card, BossPadSide, -y, inner,
                    "READY FOR A CREW · " + commandReady, 10.5f,
                    commandReadyRoom ? LedgerV2.HeadPaper : LedgerV2.HeadDim, 1f);
                line.overflowMode = TextOverflowModes.Ellipsis;
                y += LineBox(10.5f) + 11f;
            }

            // The counter's own paper, pulled out of the dark folder.
            if (member != null && commandArmsOpenId == member.Id)
                y = FileArmsMenu(card, member, BossPadSide, y, inner) + 11f;

            // What he IS, in the clerk's words - Personality's own bands, plus the one
            // line no lieutenant's file can carry.
            y = TraitChips(card, member, BossPadSide, y, inner, "answers to nobody",
                LedgerV2.HeadInk, LedgerV2.Head, LedgerV2.HeadDim);
            y += 11f;

            var remark = Paragraph(card, LedgerStyle.SerifItalic, 12.5f, LedgerV2.HeadInk,
                BossPadSide, -y, Mathf.Min(inner, 52f * 6.4f), 42f,
                "Every order on this sheet is his. The lieutenants carry it out and " +
                "take the fall - that is what they are paid for.", lineSpacing: 3f);
            remark.overflowMode = TextOverflowModes.Ellipsis;
            y += Mathf.Min(42f, remark.preferredHeight) + 11f;

            var close = Caps(card, BossPadSide, -y, 140f, "CLOSE", 9.5f, LedgerV2.Boss, 8f);
            close.font = LedgerStyle.MonoBold;
            NameKey(card, BossPadSide, -y, 140f, LineBox(9.5f), ToggleCommandBoss);
            return y + LineBox(9.5f);
        }

        // ---------------------------------------------------------------- the branch

        /// <summary>One branch: the man who runs it, what he can hold, his own file if
        /// it is open, and every man on his rail. Answers the height it took, measured
        /// from <paramref name="top"/>.</summary>
        float BuildCommandBranch(CommandBranch branch, float x, float top, float w)
        {
            var roster = director.Roster;
            var card = LedgerV2.Card("Branch " + branch.Name, commandContent, x, -top, w,
                10f, LedgerV2.Panel);
            Block("Rank", card, 0f, 0f, w, 4f, branch.Ink);

            var member = !branch.IsDetail && roster != null
                ? roster.Find(branch.LeaderId)
                : null;
            if (!branch.IsDetail)
                RoundFace(card, BranchPad, BranchPortraitLift, BranchPortrait, member,
                    InitialsOf(branch.Name), LedgerV2.Portrait, LedgerV2.Muted,
                    branch.Ink);

            // The design's head padding: 28 over the rank when a photograph floats over
            // the card, 11 when nothing does.
            var y = branch.IsDetail ? 11f : 28f;
            var leaderId = branch.LeaderId;

            // The hire key keeps the right of the head; the name block takes the rest.
            var hireW = 0f;
            if (!branch.IsDetail && !branch.IsBag)
            {
                // No sum on the key. What a man costs to sign is not one flat
                // figure - the recruiting money is only the first of it, and quoting
                // it beside the word HIRE reads as the price of the man, which it is
                // not. The filed order still says exactly what was committed.
                const string hireLabel = "HIRE";
                hireW = MonoWidth(hireLabel, 9.5f, 2f) + 18f;
                var hire = LedgerV2.Button(card, hireLabel, w - BranchPad - hireW, -y,
                    hireW, 21f, () => FileRecruit(leaderId), LedgerV2.Key.Outline, 9.5f);
                SetActionEnabled(hire, director != null);
                hireW += 12f;
            }

            var textW = Mathf.Max(80f, w - BranchPad * 2f - hireW);

            var rank = Line(card, LedgerStyle.MonoBold, 10f, branch.Ink,
                BranchPad, -y, textW, LineBox(10f), branch.Rank);
            rank.characterSpacing = 10f;
            rank.overflowMode = TextOverflowModes.Ellipsis;
            y += LineBox(10f);

            var name = Line(card, LedgerStyle.Condensed, 17f, LedgerV2.Ink,
                BranchPad, -y, textW, LineBox(17f), branch.Name);
            name.overflowMode = TextOverflowModes.Ellipsis;
            if (!branch.IsDetail)
                NameKey(card, BranchPad, -y, textW, LineBox(17f),
                    () => ToggleCommandFile(leaderId));
            y += LineBox(17f) + 3f;

            if (branch.IsBag)
            {
                const float offW = 96f;
                LedgerV2.Button(card, "OFF THE BAG", w - BranchPad - offW, -y,
                    offW, 21f, () => FileBagOff(branch.LeaderId),
                    LedgerV2.Key.Red, 9f);
                y += 24f;
            }

            LedgerV2.Mono(card, BranchPad, -y, w - BranchPad * 2f,
                    branch.IsDetail
                        ? (branch.Roster.Count == 0
                              ? "nobody in front of him · "
                              : branch.Roster.Count + (branch.Roster.Count == 1
                                  ? " man in front of him · "
                                  : " men in front of him · ")) + branch.WageLine
                        : branch.WageLine,
                    10f, LedgerV2.Muted, 1f)
                .overflowMode = TextOverflowModes.Ellipsis;
            y += LineBox(10f) + 9f;

            if (branch.HasMeters)
            {
                // The design's auto-fit meter grid: columns of at least 150 at a
                // 14-unit gutter, so a 374-wide card carries both side by side.
                var inner = w - BranchPad * 2f;
                var columns = (inner + 14f) / 164f >= 2f ? 2 : 1;
                var cell = (inner - 14f * (columns - 1)) / columns;
                var a = Meter(card, BranchPad, y, cell, "MANPOWER", branch.Men,
                    "man", "men", dark: false, labelSize: 10.5f, figureSize: 13.5f);
                var b = Meter(card,
                    columns == 2 ? BranchPad + cell + 14f : BranchPad,
                    columns == 2 ? y : y + a + 8f, cell, "BLOCKS", branch.Blocks,
                    "block", "blocks", dark: false, labelSize: 10.5f, figureSize: 13.5f);
                y += columns == 2 ? Mathf.Max(a, b) : a + 8f + b;
                y += 11f;
            }

            // The footer band: what the branch IS, and what it costs a day - side by
            // side at a 9-unit gap, as the design sets them, not pushed apart.
            var band = NewRect("Branch foot", card);
            PlaceTopLeft(band, 0f, -y, w, 28f);
            Fill(band, LedgerV2.PanelBand);
            Rule(band, 0f, 0f, w, LedgerV2.Hair);
            var summary = branch.IsBag
                ? "collector detail · no player orders"
                : branch.IsDetail
                ? "protection · earns the outfit nothing"
                : BranchSummary(branch.Roster).ToLowerInvariant();
            var summaryW = Mathf.Min(MonoWidth(summary, 10.5f, 1f) + 4f,
                Mathf.Max(40f, w - BranchPad * 2f - 90f));
            LedgerV2.Mono(band, BranchPad, -7f, summaryW, summary, 10.5f,
                    LedgerV2.Muted, 1f)
                .overflowMode = TextOverflowModes.Ellipsis;
            LedgerV2.Mono(band, BranchPad + summaryW + 9f, -7f,
                    Mathf.Max(20f, w - BranchPad * 2f - summaryW - 9f),
                    LedgerText.Cash(BranchWage(branch)) + " / day", 10.5f,
                    LedgerV2.Ink, 1f)
                .font = LedgerStyle.MonoBold;
            y += 28f;

            // His own file opens INSIDE his card, under the band.
            if (!branch.IsDetail && member != null &&
                commandOpenFiles.Contains(branch.LeaderId))
                y = BuildCommandFile(card, member, Leader(branch.LeaderId), y, w);

            // The man picked out of the reserve, offered to this branch.
            if (organizationPickedHoodId >= 0 &&
                (!branch.IsBag || branch.BagSlotsUsed < Crew.MaxEscorts))
            {
                var picked = Person(organizationPickedHoodId);
                if (picked.IsValid)
                {
                    var hoodId = picked.Id;
                    var first = FirstName(picked.Name).ToUpperInvariant();
                    LedgerV2.Button(card,
                        branch.IsDetail
                            ? "FILE · PUT " + first + " ON THE DETAIL"
                            : branch.IsBag
                                ? "FILE · PUT " + first + " ON THE BAG"
                            : "FILE · PUT " + first + " UNDER HIM",
                        0f, -y, w, 33f,
                        branch.IsDetail
                            ? (UnityAction)(() => FileDetailPosting(hoodId))
                            : branch.IsBag
                                ? () => FileBagPosting(branch.CrewId, hoodId)
                            : () => FileHoodPlacement(hoodId, leaderId),
                        branch.IsDetail ? LedgerV2.Key.Red : LedgerV2.Key.Dark, 11f);
                    y += 33f;
                }
            }

            card.sizeDelta = new Vector2(w, y);

            // ---- his men, on the dashed rail under the card ----
            var railTop = top + y + 2f;
            var cursor = railTop;
            var leafX = x + RailX + RailStub;
            var leafW = w - RailX - RailStub;

            if (branch.Roster.Count == 0 && !branch.IsBag)
            {
                DashAcross(commandContent, x + RailX, cursor + 11f, RailStub);
                DashDown(commandContent, x + RailX, railTop, 11f);
                Line(commandContent, LedgerStyle.MonoItalic, 10.5f, LedgerV2.Red,
                    leafX + 6f, -cursor, Mathf.Max(40f, leafW - 6f), 22f,
                    options.Availability != AvailabilityFilter.All
                        ? "no man on this branch answers to this filter"
                        : branch.IsDetail
                            ? "nobody stands in front of the Don"
                            : "no men on this branch");
                cursor += 24f;
            }
            else
            {
                var lastStub = cursor;
                for (var i = 0; i < branch.Roster.Count; i++)
                {
                    cursor += LeafMargin;
                    lastStub = cursor + LeafRowH() * 0.5f;
                    DashAcross(commandContent, x + RailX, lastStub, RailStub);
                    cursor += BuildCommandLeaf(branch.Roster[i], leafX, cursor, leafW,
                        reserve: false, bagCrewId: branch.IsBag ? branch.CrewId : -1,
                        postBagCrewId: !branch.IsBag && branch.Bag != null &&
                                       branch.Bag.BagSlotsUsed < Crew.MaxEscorts
                            ? branch.CrewId : -1) + LeafMargin;
                }
                // The rail is trimmed at the LAST man's stub, the way the design's
                // little patch of paper trims it: a tail hanging past the last leaf
                // reads as a branch with a man missing off the end of it.
                DashDown(commandContent, x + RailX, railTop, lastStub - railTop);
            }

            if (branch.IsBag)
            {
                var empty = Mathf.Max(0, Crew.MaxEscorts - branch.BagSlotsUsed);
                for (var i = 0; i < empty; i++)
                {
                    cursor += LeafMargin;
                    var stub = cursor + LeafRowH() * 0.5f;
                    DashAcross(commandContent, x + RailX, stub, RailStub);
                    var slot = LedgerV2.Card("Escort place", commandContent,
                        leafX, -cursor, leafW, LeafRowH(), LedgerV2.Panel);
                    LedgerV2.Mono(slot, LeafPad, -(LeafRowH() - LineBox(10f)) * 0.5f,
                        leafW - LeafPad * 2f, "EMPTY ESCORT PLACE · PLACE", 10f,
                        LedgerV2.Muted, 1f, TextAlignmentOptions.MidlineRight);
                    if (organizationPickedHoodId >= 0)
                    {
                        var hoodId = organizationPickedHoodId;
                        NameKey(slot, 0f, 0f, leafW, LeafRowH(),
                            () => FileBagPosting(branch.CrewId, hoodId));
                    }
                    cursor += LeafRowH() + LeafMargin;
                }
                if (branch.Roster.Count + empty > 0)
                    DashDown(commandContent, x + RailX, railTop,
                        Mathf.Max(0f, cursor - railTop - LeafMargin - LeafRowH() * 0.5f));
            }

            // THE DETAIL's bag is already a peer in the Don's branch row. Every
            // lieutenant's bag remains the next level down on his own rail.
            if (branch.Bag != null && !branch.IsDetail)
            {
                cursor += 14f;
                var nestedX = x + RailX;
                DashAcross(commandContent, x + RailX * 0.45f, cursor + 24f,
                    RailX * 0.55f);
                cursor += BuildCommandBranch(branch.Bag, nestedX, cursor,
                    Mathf.Max(180f, w - RailX));
            }

            return cursor - top;
        }

        /// <summary>What one man's line stands: the design's 5-unit padding over a
        /// 13-unit name and the 9.5-unit line under it.</summary>
        static float LeafRowH() => 5f + LineBox(13f) + LineBox(9.5f) + 5f;

        int BranchWage(CommandBranch branch)
        {
            var roster = director != null ? director.Roster : null;
            if (roster == null)
                return 0;
            var total = branch.IsDetail ? 0 : branch.Wage;
            for (var i = 0; i < branch.Roster.Count; i++)
            {
                if (branch.Roster[i].Id == branch.LeaderId)
                    continue;
                var member = roster.Find(branch.Roster[i].Id);
                if (member != null)
                    total += Outfit.Wages.WageFor(member, RosterDay);
            }
            return total;
        }

        // ------------------------------------------------------------------ one man

        /// <summary>
        /// One man on a rail, on the design's own grid: a 20-unit portrait, a 9-unit
        /// gutter, the name over the post he stands on, and the figures on the end of
        /// the line - what he carries, how he is, what he costs, and the one word that
        /// moves him. That word is bare type with a rule under it on hover, never a
        /// boxed key: the design puts no button inside a line of a roll.
        ///
        /// His file opens INSIDE this card, under the line, so the row and the file are
        /// one piece of paper. Answers its height.
        /// </summary>
        float BuildCommandLeaf(OrganizationPerson person, float x, float top, float w,
            bool reserve, int bagCrewId = -1, int postBagCrewId = -1)
        {
            var roster = director != null ? director.Roster : null;
            var member = roster != null ? roster.Find(person.Id) : null;
            var open = commandOpenFiles.Contains(person.Id);
            var picked = organizationPickedHoodId == person.Id;
            var rowH = LeafRowH();
            var pad = reserve ? 12f : LeafPad;
            var gap = reserve ? 11f : LeafGap;

            var card = LedgerV2.Card("Man " + person.Name, commandContent, x, -top, w,
                rowH, picked ? LedgerV2.Picked : LedgerV2.Panel);

            // The LINE is the click surface, not the card: with the file open, a click
            // on the file's own paper must not shut the file under the reader's hand.
            var row = NewRect("Line", card);
            PlaceTopLeft(row, 0f, 0f, w, rowH);
            var id = person.Id;
            RowButton(row, ClickSurface(row), () => ToggleCommandFile(id));

            var dead = member == null || member.Gone;
            var posted = HasPost(person);

            // The photograph, in the corner of the line and the full depth of it -
            // no margin in front, so it reads as a photograph stuck to the card and
            // not as a stamp floating on it. It stands on the bar that says whether
            // he is earning.
            var portraitH = rowH;
            var portraitW = portraitH * LeafPortraitAspect;
            var plate = LedgerV2.PortraitPlate(row, 0f, 0f, portraitW,
                portraitH, "", LedgerV2.Thumb, LedgerV2.Muted);
            if (member != null)
                PortraitStudio.Request(MemberModel(member), PortraitStudio.Framing.Bust,
                    plate);
            Block("Duty", row, 0f, -(portraitH - 3f), portraitW, 3f,
                posted ? LedgerV2.Green : LedgerV2.Red);

            // The end of the line, laid right to left: the word that moves him, what he
            // draws, how he is, and then whatever room is left goes to the carry.
            var tailW = reserve ? 44f : LeafTailW;
            var tailX = w - pad - tailW;
            var wageX = tailX - gap - LeafWageW;
            var condX = wageX - gap - LeafCondW;

            var tailLine = LineBox(9.5f);
            var tailY = -(rowH - tailLine) * 0.5f;
            if (!reserve && bagCrewId < 0 && postBagCrewId >= 0)
            {
                // A line hood keeps his ordinary PULL and gains the brief's SECOND
                // action for the bag. Stacking the two words preserves every existing
                // column width and gives each one its own hit surface.
                var bagY = -4f;
                var pullY = -(rowH - tailLine - 4f);
                var bagTail = LedgerV2.Mono(row, tailX, bagY, tailW, "TO BAG", 9.5f,
                    dead ? LedgerV2.Faint : LedgerV2.Red, 0f,
                    TextAlignmentOptions.MidlineRight);
                bagTail.font = LedgerStyle.MonoBold;
                var pullTail = LedgerV2.Mono(row, tailX, pullY, tailW, "PULL", 9.5f,
                    dead ? LedgerV2.Faint : LedgerV2.Red, 0f,
                    TextAlignmentOptions.MidlineRight);
                pullTail.font = LedgerStyle.MonoBold;
                if (!dead)
                {
                    NameKey(row, tailX, bagY, tailW, tailLine,
                        () => FileBagPosting(postBagCrewId, id));
                    NameKey(row, tailX, pullY, tailW, tailLine,
                        () => FileHoodRecall(id));
                }
            }
            else
            {
                var tail = LedgerV2.Mono(row, tailX, tailY, tailW,
                    reserve ? picked ? "PICKED" : "PLACE"
                        : "PULL", 9.5f,
                    dead ? LedgerV2.Faint
                        : reserve ? picked ? LedgerV2.Red : LedgerV2.Muted : LedgerV2.Red,
                    0f, TextAlignmentOptions.MidlineRight);
                tail.font = LedgerStyle.MonoBold;
                if (!dead)
                    NameKey(row, tailX, tailY, tailW, tailLine,
                        reserve
                            ? (UnityAction)(() => PickHood(id))
                            : bagCrewId >= 0
                                ? () => FileBagPull(id)
                                : () => FileHoodRecall(id));
            }

            var status = member != null ? member.Status : CharacterStatus.Dead;
            var cond = LedgerV2.Mono(row, condX, -(rowH - LineBox(10.5f)) * 0.5f,
                LeafCondW, ConditionWord(status), 10.5f,
                dead ? LedgerV2.Faint : ConditionInk(status), 0f,
                TextAlignmentOptions.MidlineRight);
            cond.font = LedgerStyle.MonoBold;

            LedgerV2.Figure(row, wageX, -(rowH - LineBox(12f)) * 0.5f, LeafWageW,
                member != null ? LedgerText.Cash(Outfit.Wages.WageFor(member, RosterDay)) : "--",
                12f, dead ? LedgerV2.Faint
                    : member != null && member.WageDemand > 0 ? LedgerV2.Red
                    : LedgerV2.Ink);

            // The middle column: never under 120, and the carry takes what is left of
            // the line after it - printed only when there is room to read it.
            var nameX = portraitW + gap;
            var room = condX - gap - nameX;
            var carryW = Mathf.Max(0f, room - LeafNameMin - gap);
            if (carryW < LeafCarryMin)
                carryW = 0f;
            var nameW = Mathf.Max(LeafNameMin, room - (carryW > 0f ? carryW + gap : 0f));

            Line(row, LedgerStyle.Condensed, 13f, dead ? LedgerV2.Faint : LedgerV2.Ink,
                    nameX, -5f, nameW, LineBox(13f), person.Name)
                .overflowMode = TextOverflowModes.Ellipsis;
            LedgerV2.Mono(row, nameX, -(5f + LineBox(13f)), nameW, HoodDuty(person),
                    9.5f, posted ? LedgerV2.Muted : LedgerV2.Red, 0f)
                .overflowMode = TextOverflowModes.Ellipsis;

            if (carryW > 0f)
                LedgerV2.Mono(row, condX - gap - carryW,
                        -(rowH - LineBox(10.5f)) * 0.5f, carryW,
                        member != null ? CarryingLine(roster, member, out _) : "--",
                        10.5f, dead ? LedgerV2.Faint : LedgerV2.Body, 0f,
                        TextAlignmentOptions.MidlineRight)
                    .overflowMode = TextOverflowModes.Ellipsis;

            var height = rowH;
            if (open && member != null)
                height = BuildCommandFile(card, member, person, height, w);

            card.sizeDelta = new Vector2(w, height);
            return height;
        }

        // ------------------------------------------------------------------ the file

        /// <summary>
        /// One man's file, opened INSIDE the card of the line it belongs to and set to
        /// the design's own measure: a dotted rule over it, 12 units of padding, the
        /// post and the file's reference, then the facts FLOWING across the measure,
        /// the trades in an auto-fit grid, what he is LIKE, the plain sentence, the gun
        /// and the verbs.
        ///
        /// Every figure is the roster's own. Nothing is dealt from a hash: the trades
        /// are his practised half-steps and the character is Personality's own bands.
        /// Where the game holds no such fact the file leaves the row out rather than
        /// inventing one to fill it.
        /// </summary>
        float BuildCommandFile(Transform host, Character member,
            OrganizationPerson person, float top, float w)
        {
            var isLieutenant = member.Rank == Rank.Lieutenant;
            const float pad = 12f;
            var inner = w - pad * 2f;

            DottedRule(host, 0f, -top, w, LedgerV2.Dotted);
            var y = top + 9f;

            // ---- what he stands on, and the file's own reference ----
            var reference = "FILE P-" + (1100 + member.Id).ToString("0000");
            var referenceW = MonoWidth(reference, 9f, 8f) + 4f;
            var post = Caps(host, pad, -y, Mathf.Max(40f, inner - referenceW - 12f),
                isLieutenant
                    ? "BLOCKS ON HIS PAPER · " + BlocksOnPaper(member.Id)
                    : "POST · " + HoodDuty(person),
                10f, LedgerV2.Muted, 10f);
            post.font = LedgerStyle.Mono;
            post.overflowMode = TextOverflowModes.Ellipsis;
            Caps(host, pad + inner - referenceW, -y, referenceW, reference, 9f,
                LedgerV2.Faint, 8f, TextAlignmentOptions.MidlineRight);
            y += LineBox(10f) + 6f;

            y = FileFacts(host, member, person, isLieutenant, pad, y, inner);
            y = FileWarning(host, member, isLieutenant, pad, y, inner);

            // The gun drawer, if he has pulled it out - it belongs under CARRIES, which
            // is the line that opened it.
            if (commandArmsOpenId == member.Id)
                y = FileArmsMenu(host, member, pad, y + 4f, inner);

            y = FileTrades(host, member, pad, y + 8f, inner);
            y = TraitChips(host, member, pad, y + 8f, inner, "", LedgerV2.Body,
                LedgerV2.PanelDark, LedgerV2.Rule);
            y = FileActions(host, member, person, isLieutenant, pad, y + 9f, inner);
            return y + 10f;
        }

        /// <summary>
        /// The facts, on a GRID and not a flow: one column of labels, one column of
        /// figures, and both of them starting at the same place on every line. The
        /// label column is measured off the longest label there is, so ANSWERS TO and
        /// WAGE hand their figures over at the same margin.
        ///
        /// A card wide enough carries two pairs to a line; a fact whose figure will not
        /// stand in half the measure takes the whole line rather than being cut short -
        /// a man's own name is not a thing to print as an ellipsis. And the label and
        /// the figure are centred on ONE midline: they are set at different sizes, so
        /// laid at the same top edge they sit three units apart, which is exactly the
        /// stagger the page was showing.
        /// </summary>
        float FileFacts(Transform host, Character member, OrganizationPerson person,
            bool isLieutenant, float x, float y, float w)
        {
            var carried = CarriedGun(member);
            var idle = !isLieutenant && !HasPost(person);
            var memberId = member.Id;
            var armsOpen = commandArmsOpenId == memberId;

            // CARRIES is the one fact on the file that is also a DRAWER: what he holds
            // is the question the answer to which is bought, so the line that states it
            // is the line that opens the counter. The rest are read and not pressed.
            var facts = new List<(string Label, string Value, Color Ink, UnityAction Open)>
            {
                ("ANSWERS TO", AnswersTo(member), LedgerV2.Ink, null),
                ("STANDING",
                    member.Wanted ? "WANTED" : idle ? "IDLE · EARNING NOTHING" : "ACTIVE",
                    member.Wanted || idle ? LedgerV2.Red : LedgerV2.Green, null),
                ("CARRIES", carried + (armsOpen ? "  ▴" : "  ▾"),
                    carried == "nothing" ? LedgerV2.Red : LedgerV2.Ink,
                    (UnityAction)(() => ToggleCommandArms(memberId))),
                ("WAGE", LedgerText.Cash(Outfit.Wages.WageFor(member, RosterDay)) +
                    " / day" + (member.UnpaidSince > 0
                        ? "  ·  UNPAID SINCE DAY " + member.UnpaidSince : ""),
                    member.WageDemand > 0 || member.UnpaidSince > 0
                        ? LedgerV2.Red : LedgerV2.Ink, null),
                ("CONDITION", LedgerText.StatusLabel(member.Status),
                    member.Status == CharacterStatus.Active
                        ? LedgerV2.Ink : LedgerV2.Red, null),
                // The watch band is THE number for "we are losing him" and there is one
                // of it: a file that carried its own 35 would print a man at 34 in
                // black the day the model moved the band.
                ("LOYALTY", member.Loyalty + " of 100",
                    member.Loyalty < Loyalty.WatchBand ? LedgerV2.Red : LedgerV2.Ink,
                    null),
            };

            // FOLLOW-004. How long he has been exactly what he is - the input to the
            // one loyalty rule the player could act on, and red from the day it starts
            // costing him. The same sentence his own file on the roll prints.
            var today = OrganizationDay;
            facts.Add((TenureLabel(member), TenureFigure(member, today),
                Loyalty.IsParked(member, today) ? LedgerV2.Red : LedgerV2.Ink, null));

            // LOY-004. The book's own verdict on him, so this sheet answers "who do I
            // promote, who do I watch" without sending the reader to the roll. The red
            // flag prints in red; the two he could BE print in ink.
            var marks = ManFlags.Of(member);
            if (marks != ManFlag.None)
                facts.Add(("THE BOOK SAYS", ManFlags.Line(marks),
                    (marks & ManFlag.RedFlag) != 0 ? LedgerV2.Red : LedgerV2.Ink, null));

            // One margin for every figure on the sheet: the longest label sets it.
            var labelW = 0f;
            for (var i = 0; i < facts.Count; i++)
                labelW = Mathf.Max(labelW, MonoWidth(facts[i].Label, 9f, 10f) + 10f);

            const float gutter = 18f;
            // A cell has to carry the label and a figure worth reading; under that the
            // file runs one pair to the line.
            var columns = w >= (labelW + 96f) * 2f + gutter ? 2 : 1;
            var cell = (w - gutter * (columns - 1)) / columns;

            var line = LineBox(11.5f);
            var rowH = line + 3f;

            var column = 0;
            var cy = y;
            for (var i = 0; i < facts.Count; i++)
            {
                var valueW = MonoWidth(facts[i].Value, 11.5f, 0f) + 4f;
                var wide = valueW > cell - labelW;
                if (wide && column > 0)
                {
                    column = 0;
                    cy += rowH;
                }

                var cx = x + column * (cell + gutter);
                var room = (wide ? x + w - cx : cell) - labelW;
                // The label is set in a shorter box than the figure; centred on the
                // figure's own line, the pair reads as one line and not two.
                var label = Caps(host, cx, LedgerV2.MarkY(-cy, line, 9f + 6f),
                    labelW, facts[i].Label, 9f, LedgerV2.Label, 10f);
                label.font = LedgerStyle.Mono;
                LedgerV2.Figure(host, cx + labelW, -cy, Mathf.Max(20f, room),
                    facts[i].Value, 11.5f, facts[i].Ink,
                    TextAlignmentOptions.MidlineLeft);
                if (facts[i].Open != null)
                    NameKey(host, cx + labelW, -cy,
                        Mathf.Min(Mathf.Max(20f, room), valueW), line, facts[i].Open);

                column += wide ? columns : 1;
                if (column >= columns)
                {
                    column = 0;
                    cy += rowH;
                }
            }
            return column > 0 ? cy + rowH : cy;
        }

        /// <summary>
        /// The four trades he is best at, two to a line: the label on the left, the
        /// reading held to the right of the cell, and the stepped meter between them.
        ///
        /// The design draws five blocks and prints "n / 5". This roster counts in HALF
        /// steps, so the meter is ten blocks at half the pitch - read the same way -
        /// and the figure beside it is the exact half-star the man actually has.
        /// Rounding his practice away to fit five blocks would be the one place on this
        /// sheet where the picture lied about the book.
        ///
        /// EVERYTHING in a cell is measured off the cell. The meter used to stand at a
        /// fixed 97 units with a fixed run behind it, which is fine on a wide card and
        /// on a narrow one printed one man's reading over the next man's name and ran
        /// the last column off the edge of the paper. Now the label and the reading
        /// take what they need, the meter takes what is left, and when what is left
        /// will not carry ten legible blocks the meter is not drawn at all - a trade
        /// with no meter still reads; a trade printed over its neighbour does not.
        /// </summary>
        float FileTrades(Transform host, Character member, float x, float y, float w)
        {
            commandTrades.Clear();
            for (var a = 0; a < AttributeScale.Count; a++)
                commandTrades.Add(((CharacterAttribute)a,
                    member.GetHalfSteps((CharacterAttribute)a)));
            commandTrades.Sort((l, r) => r.Steps.CompareTo(l.Steps));

            const float gutter = 16f;
            const float minCell = 150f;
            var columns = Mathf.Max(1,
                Mathf.FloorToInt((w + gutter) / (minCell + gutter)));
            var cell = (w - gutter * (columns - 1)) / columns;

            var labelW = Mathf.Clamp(cell * 0.40f, 56f, 96f);

            // The reading is "2.5 / 5" while the cell can carry it and the meter both.
            // Where it cannot, the "/ 5" is what goes: ten blocks ARE the scale, and a
            // meter with a bare figure beside it reads better than a figure alone.
            var figureW = MonoWidth("2.5 / 5", 10f, 0f) + 4f;
            var scale = true;
            var pitch = (cell - labelW - 16f - figureW) / AttributeScale.MaxHalfSteps;
            if (pitch < 4f)
            {
                scale = false;
                figureW = MonoWidth("2.5", 10f, 0f) + 4f;
                pitch = (cell - labelW - 16f - figureW) / AttributeScale.MaxHalfSteps;
            }
            var meter = pitch >= 4f;
            if (!meter)
            {
                scale = true;
                figureW = MonoWidth("2.5 / 5", 10f, 0f) + 4f;
            }
            pitch = Mathf.Min(pitch, 7f);

            // ALL eleven, best first. The grid computed every one of them and then
            // printed the top four, which made the file quietly disagree with the man's
            // own page on the roll - and the four a man is best at are exactly the four
            // a reader can already guess. What he CANNOT do is the half of the file
            // worth opening it for.
            var shown = commandTrades.Count;
            var rowH = LineBox(9.5f) + 1f;

            for (var i = 0; i < shown; i++)
            {
                var cx = x + i % columns * (cell + gutter);
                var cy = y + i / columns * rowH;
                var steps = commandTrades[i].Steps;

                LedgerV2.Mono(host, cx, -cy, labelW,
                        LedgerText.AttributeLabel(commandTrades[i].Attribute), 9.5f,
                        LedgerV2.Label, 8f)
                    .overflowMode = TextOverflowModes.Ellipsis;
                if (meter)
                    LedgerV2.Pips(host, cx + labelW + 8f,
                        -(cy + LineBox(9.5f) * 0.5f),
                        AttributeScale.MaxHalfSteps, steps,
                        steps >= 8 ? LedgerV2.Green
                            : steps <= 3 ? LedgerV2.Red : LedgerV2.Ink,
                        Mathf.Max(2.5f, pitch - 2f), 7f, pitch, LedgerV2.Rule);
                LedgerV2.Mono(host, cx + cell - figureW, -cy, figureW,
                        AttributeScale.Stars(steps).ToString("0.#") +
                        (scale ? " / 5" : ""), 10f,
                        LedgerV2.Muted, 0f, TextAlignmentOptions.MidlineRight)
                    .font = LedgerStyle.MonoBold;
            }
            return y + (shown + columns - 1) / columns * rowH;
        }

        /// <summary>
        /// FOLLOW-002/003. A red flag on a HOOD is a man worth watching; a red flag on
        /// the man who HOLDS a branch is the branch, and this sheet is the tree - so
        /// his file says it in words and says what it would cost. The count comes off
        /// the defection arithmetic itself (<see cref="Defection.WouldFollow"/>), never
        /// off the mark: a flag informs and never acts.
        /// </summary>
        float FileWarning(Transform host, Character member, bool isLieutenant,
            float x, float y, float w)
        {
            if (member == null || !isLieutenant || director == null ||
                (ManFlags.Of(member) & ManFlag.RedFlag) == 0)
                return y;

            const float bandH = 24f;
            var would = Defection.WouldFollow(director.Roster, member);
            var band = NewRect("Warning", host);
            PlaceTopLeft(band, x, -(y + 6f), w, bandH);
            Fill(band, LedgerV2.Wrong);
            var warn = Caps(band, 10f, -(bandH - LineBox(9.5f)) * 0.5f, w - 20f,
                BearsWatchingLine(would), 9.5f, LedgerV2.Red, 2f);
            warn.font = LedgerStyle.MonoBold;
            warn.overflowMode = TextOverflowModes.Ellipsis;
            return y + 6f + bandH + 4f;
        }

        /// <summary>What a man is LIKE, in the clerk's own words - Personality's bands,
        /// one chip each at the design's 4-by-9 padding, and never the numbers behind
        /// them.</summary>
        float TraitChips(Transform host, Character member, float x, float y, float w,
            string extra, Color ink, Color ground, Color edge)
        {
            if (member == null)
                return y;

            const float size = 9f;
            var cx = x;
            var cy = y;
            var h = LineBox(size) + 2f;

            void Chip(string word)
            {
                var chipW = Mathf.Min(w, MonoWidth(word, size, 4f) + 11f);
                if (cx > x && cx + chipW > x + w)
                {
                    cx = x;
                    cy += h + 4f;
                }
                var chip = NewRect("Trait", host);
                PlaceTopLeft(chip, cx, -cy, chipW, h);
                Fill(chip, ground);
                Frame(chip, 1f, edge);
                LedgerV2.Mono(chip, 0f, -(h - LineBox(size)) * 0.5f, chipW, word, size,
                    ink, 4f, TextAlignmentOptions.Center);
                cx += chipW + 5f;
            }

            // Five of the six. Loyalty has its own FIGURE on the sheet above - the
            // watch band is a number the player acts on - and a page that prints a man
            // as "34 of 100" and then again as one word is a page saying one fact
            // twice in two voices. The man's own file on the roll settled this the same
            // way; this is that ruling applied here.
            for (var i = 0; i < Personality.All.Length; i++)
            {
                if (Personality.All[i] == PersonalityTrait.Loyalty)
                    continue;
                Chip(Personality.Band(Personality.All[i],
                    Personality.Get(member, Personality.All[i])));
            }
            if (extra.Length > 0)
                Chip(extra);

            return cy + h;
        }

        /// <summary>
        /// THE GUN DRAWER, pulled out from under his CARRIES line: what is already on
        /// the shelf and costs nothing to sign out, then what the counter sells and
        /// what it comes to. One to a line, cheapest first, and a line that cannot be
        /// afforded is greyed rather than hidden - a counter that shows only what a man
        /// can afford today tells him nothing about tomorrow.
        ///
        /// A gun bought or signed out HERE is that man's and stays his: the
        /// quartermaster's deal re-derives who carries what every time the roster
        /// moves, and it steps over a piece the boss put in a named hand
        /// (RosterEquipment.PinnedTo). Everything else in the branch's stock goes on
        /// being dealt by who can shoot - the two live side by side, and the boss's
        /// word wins where they meet. TAKE IT BACK returns his piece to the safe and
        /// puts him back in the deal.
        /// </summary>
        float FileArmsMenu(Transform host, Character member, float x, float y, float w)
        {
            var roster = director.Roster;
            var memberId = member.Id;
            var memberName = member.FullName;
            if (roster == null)
                return y;

            commandArms.Clear();

            // What the boss already put in his hand, and can take back out of it.
            roster.HeldBy(memberId, carriedScratch);
            for (var i = 0; i < carriedScratch.Count; i++)
            {
                var held = carriedScratch[i];
                if (held.PinnedTo != memberId || !RosterOps.IsWeapon(held.Kind))
                    continue;
                var heldId = held.Id;
                var heldName = held.DisplayName;
                commandArms.Add((heldName, "TAKE IT BACK", LedgerV2.Muted,
                    LedgerV2.Ink, true, (UnityAction)(() =>
                    {
                        var result = director.ReturnEquipment(heldId);
                        commandNote = result.Ok
                            ? heldName + " taken back off " + memberName
                            : result.Reason;
                        commandArmsOpenId = -1;
                        dirty = true;
                    })));
            }

            // What is already in the safe and unheld - it costs nothing to sign out.
            var shelved = 0;
            for (var i = 0; i < roster.Equipment.Count && shelved < 6; i++)
            {
                var item = roster.Equipment[i];
                if (item.OwnerId != RosterEquipment.Unheld ||
                    !RosterOps.IsWeapon(item.Kind))
                    continue;
                shelved++;
                var itemId = item.Id;
                var itemName = item.DisplayName;
                commandArms.Add((itemName, "OFF THE SHELF", LedgerV2.Green,
                    LedgerV2.Green, true, (UnityAction)(() =>
                    {
                        var result = director.GiveEquipment(itemId, memberId,
                            pin: true);
                        commandNote = result.Ok
                            ? itemName + " signed out to " + memberName
                            : result.Reason;
                        commandArmsOpenId = -1;
                        dirty = true;
                    })));
            }

            // And what the counter sells, at the armory's own prices.
            var safe = outfit ? outfit.Accounts.Safe : 0;
            for (var i = 0; i < Outfit.ArmoryCatalog.Weapons.Length; i++)
            {
                var listing = Outfit.ArmoryCatalog.Weapons[i];
                commandArms.Add((listing.DisplayName, LedgerText.Cash(listing.Price),
                    LedgerV2.Red, LedgerV2.Dotted, outfit && safe >= listing.Price,
                    (UnityAction)(() =>
                    {
                        var bought = outfit
                            ? outfit.Purchase(listing.Price, listing.DisplayName)
                            : OpResult.Fail(LedgerText.ReasonNoSuchItem);
                        if (!bought.Ok)
                        {
                            commandNote = bought.Reason;
                            dirty = true;
                            return;
                        }
                        var stock = director.AddEquipment(listing.Kind,
                            listing.DisplayName, listing.Price);
                        var given = stock != null
                            ? director.GiveEquipment(stock.Id, memberId, pin: true)
                            : OpResult.Fail(LedgerText.ReasonNoSuchItem);
                        commandNote = given.Ok
                            ? listing.DisplayName + " bought and signed out to " +
                              memberName
                            : listing.DisplayName + " bought · " + given.Reason;
                        commandArmsOpenId = -1;
                        dirty = true;
                    })));
            }

            var rowH = LineBox(12.5f) + 7f;
            var drawer = NewRect("Gun drawer", host);
            PlaceTopLeft(drawer, x, -y, w, rowH * commandArms.Count);
            Fill(drawer, LedgerV2.PanelBand);
            Frame(drawer, 1f, LedgerV2.Rule);

            for (var i = 0; i < commandArms.Count; i++)
            {
                var entry = commandArms[i];
                var row = NewRect("Gun " + entry.Title, drawer);
                PlaceTopLeft(row, 0f, -(i * rowH), w, rowH);
                var face = Fill(row, new Color(0f, 0f, 0f, 0f));
                face.raycastTarget = true;
                if (i > 0)
                    Rule(row, 0f, 0f, w, LedgerV2.Hair);

                Block("Dot", row, 11f, -(rowH - 7f) * 0.5f, 7f, 7f,
                    entry.Live ? entry.Dot : LedgerV2.Rule);
                Line(row, LedgerStyle.Condensed, 12.5f,
                        entry.Live ? LedgerV2.Ink : LedgerV2.Faint, 26f,
                        -(rowH - LineBox(12.5f)) * 0.5f,
                        Mathf.Max(40f, w - 130f), LineBox(12.5f), entry.Title)
                    .overflowMode = TextOverflowModes.Ellipsis;
                LedgerV2.Mono(row, w - 100f, -(rowH - LineBox(10f)) * 0.5f, 89f,
                        entry.Note, 10f,
                        entry.Live ? entry.NoteInk : LedgerV2.Faint, 0f,
                        TextAlignmentOptions.MidlineRight)
                    .font = LedgerStyle.MonoBold;
                if (entry.Live)
                    RowButton(row, face, entry.Do);
            }

            return y + rowH * commandArms.Count;
        }

        /// <summary>The drawer's own rows, gathered before it is drawn: the panel behind
        /// them has to be measured off how many there are.</summary>
        readonly List<(string Title, string Note, Color NoteInk, Color Dot, bool Live,
            UnityAction Do)> commandArms =
            new List<(string, string, Color, Color, bool, UnityAction)>();

        void FileBagPosting(int branchCrew, int hoodId)
        {
            var refusal = BlockRacketSeam.ActionsOrStub.PostEscort(branchCrew, hoodId);
            organizationNote = string.IsNullOrEmpty(refusal)
                ? "Filed: he is on the bag's detail."
                : refusal;
            if (string.IsNullOrEmpty(refusal))
                organizationPickedHoodId = -1;
            dirty = true;
        }

        void FileBagPull(int hoodId)
        {
            var refusal = BlockRacketSeam.ActionsOrStub.PullEscort(hoodId);
            organizationNote = string.IsNullOrEmpty(refusal)
                ? "Filed: he is back in the line."
                : refusal;
            dirty = true;
        }

        void FileBagOff(int collectorId)
        {
            var refusal = BlockRacketSeam.ActionsOrStub.TakeOffTheBag(collectorId);
            organizationNote = string.IsNullOrEmpty(refusal)
                ? "Filed: nobody carries that bag."
                : refusal;
            dirty = true;
        }

        /// <summary>
        /// The verbs at the foot of a file, as a RANK of keys and not a run of them:
        /// every key the same width, cut off the longest word in the set, and the row
        /// squared off against the measure. Laid one after another at their own widths
        /// they came out four different sizes on two ragged lines, and one of them -
        /// the recall - wore the ghost face, which is no face at all: a verb that
        /// undoes something has to look like something you can press.
        ///
        /// One key is filled and the rest are outlined. The filled one is whatever the
        /// file is FOR at that moment - the man already picked up off the reserve, or
        /// the recall that takes a posted man back - so the eye lands on the verb the
        /// page expects and not on all four at once.
        /// </summary>
        float FileActions(Transform host, Character member, OrganizationPerson person,
            bool isLieutenant, float x, float y, float w)
        {
            var id = member.Id;
            commandKeys.Clear();

            if (isLieutenant)
            {
                commandKeys.Add(("HIS FULL DOSSIER", LedgerV2.Key.Outline,
                    (UnityAction)(() => OpenCommandDossier(id)), true));
                commandKeys.Add(("CLOSE HIS FILE", LedgerV2.Key.Outline,
                    (UnityAction)(() => ToggleCommandFile(id)), true));
            }
            else
            {
                var picked = organizationPickedHoodId == id;
                commandKeys.Add((picked ? "PICKED" : "PLACE HIM",
                    picked ? LedgerV2.Key.Dark : LedgerV2.Key.Outline,
                    (UnityAction)(() => PickHood(id)),
                    person.IsUnassigned && person.IsAvailable));
                commandKeys.Add(("MAKE HIM LIEUTENANT", LedgerV2.Key.Outline,
                    (UnityAction)(() => FilePromotion(id)), !member.Gone));
                if (!person.IsUnassigned)
                    commandKeys.Add(("PULL HIM BACK", LedgerV2.Key.Red,
                        (UnityAction)(() => FileHoodRecall(id)), true));
                commandKeys.Add(("HIS FULL DOSSIER", LedgerV2.Key.Outline,
                    (UnityAction)(() => OpenCommandDossier(id)), true));
            }

            const float gap = 8f;
            const float minKey = 96f;
            var h = LineBox(10f) + 11f;

            // Two to a line at most, and never so narrow that a key is a stripe. The
            // word inside is allowed to come down a point or two to stand in its own
            // key rather than the key growing to suit the word - that is what made the
            // row ragged in the first place.
            var columns = Mathf.Clamp(Mathf.FloorToInt((w + gap) / (minKey + gap)),
                1, Mathf.Min(2, commandKeys.Count));
            var cell = (w - gap * (columns - 1)) / columns;

            for (var i = 0; i < commandKeys.Count; i++)
            {
                var key = LedgerV2.Button(host, commandKeys[i].Label,
                    x + i % columns * (cell + gap), -(y + i / columns * (h + gap)),
                    cell, h, commandKeys[i].Do, commandKeys[i].Face, 10f);
                key.enableAutoSizing = true;
                key.fontSizeMin = 7.5f;
                key.fontSizeMax = 10f;
                SetActionEnabled(key, commandKeys[i].Live);
            }

            var rows = (commandKeys.Count + columns - 1) / columns;
            return y + rows * h + (rows - 1) * gap;
        }

        readonly List<(string Label, LedgerV2.Key Face, UnityAction Do, bool Live)>
            commandKeys =
                new List<(string, LedgerV2.Key, UnityAction, bool)>();

        // --------------------------------------------------------------- the reserve

        float BuildCommandReserve(float cursor)
        {
            var roster = director != null ? director.Roster : null;
            var wage = 0;
            if (roster != null)
                for (var i = 0; i < commandReserve.Count; i++)
                {
                    var member = roster.Find(commandReserve[i].Id);
                    if (member != null)
                        wage += Outfit.Wages.WageFor(member, RosterDay);
                }

            cursor = CommandSectionHead(cursor, "RESERVE · STAYS WITH BOSS",
                commandReserve.Count == 0
                    ? "nobody is waiting on you"
                    : commandReserve.Count + (commandReserve.Count == 1
                          ? " man idle" : " men idle"),
                commandReserve.Count == 0 ? "" : LedgerText.Cash(wage) + " / day",
                commandReserve.Count == 0 ? LedgerV2.Muted : LedgerV2.Red,
                "HIRE A MAN",
                () => FileRecruit(-1));

            if (commandReserve.Count == 0)
            {
                Line(commandContent, LedgerStyle.MonoItalic, 12f, LedgerV2.Muted,
                    0f, -cursor, PageWidth, 22f,
                    "Nobody is sitting idle. Every man on the books answers to somebody.");
                return cursor + 26f;
            }

            // The design's auto-fill grid: as many 330-unit columns as the measure
            // holds, at a 10-unit gap, each card standing at the top of its own column.
            const float minCell = 330f;
            const float gap = 10f;
            var columns = Mathf.Max(1,
                Mathf.FloorToInt((PageWidth + gap) / (minCell + gap)));
            var cell = (PageWidth - gap * (columns - 1)) / columns;

            var columnY = new float[columns];
            for (var i = 0; i < columns; i++)
                columnY[i] = cursor;

            for (var i = 0; i < commandReserve.Count; i++)
            {
                // Shortest column takes the next card: a card whose file is open must
                // not leave a hole down the column beside it.
                var column = 0;
                for (var c = 1; c < columns; c++)
                    if (columnY[c] < columnY[column])
                        column = c;
                columnY[column] += gap + BuildCommandLeaf(commandReserve[i],
                    column * (cell + gap), columnY[column], cell, reserve: true);
            }

            var deepest = cursor;
            for (var i = 0; i < columns; i++)
                deepest = Mathf.Max(deepest, columnY[i]);
            return deepest;
        }

        // ------------------------------------------------------------- what the book
        //                                                             says of the men

        /// <summary>
        /// The two readings the sheet takes OF the men it has just drawn: who the book
        /// is shouting about, and what moved on anybody last night.
        ///
        /// They stand side by side because they answer the same morning question from
        /// two ends - the standing figure and the movement behind it - and a reader who
        /// has one wants the other on the same screenful. Under two full columns they
        /// stack rather than being squeezed: a reason is a sentence somebody wrote.
        /// </summary>
        float BuildCommandWatch(float cursor)
        {
            if (PageWidth >= WatchColumnMin * 2f + WatchGutter)
            {
                var column = (PageWidth - WatchGutter) * 0.5f;
                var left = BuildWhoToLookAt(0f, column, cursor);
                var right = BuildWordFromTheCrews(
                    column + WatchGutter, column, cursor);
                return Mathf.Max(left, right);
            }

            cursor = BuildWhoToLookAt(0f, PageWidth, cursor);
            return BuildWordFromTheCrews(0f, PageWidth, cursor + 26f);
        }

        /// <summary>
        /// FOLLOW-005. The notability figure itself, in the one room it belongs in.
        ///
        /// It is deliberately OFF the roll and off the personal file: attention is
        /// rationed there and a column of numbers would let the player skip learning
        /// who his men are, which is the whole design. This is the sheet where he
        /// stands back and looks at the house, and here the figure is a tool.
        ///
        /// The men and their order are a plain descending sort by
        /// <see cref="Notability.Of"/> - the board is READ and never written, and no
        /// score is cached beside it.
        /// </summary>
        float BuildWhoToLookAt(float left, float width, float cursor)
        {
            cursor = CommandSectionHead(cursor, left, width, "WHO TO LOOK AT",
                "WHAT THE BOOK IS SHOUTING ABOUT THIS MORNING", "", LedgerV2.Muted,
                null, null);

            var roster = director != null ? director.Roster : null;
            var today = OrganizationDay;
            Notability.Top(roster, today, NotableShown, commandNotable);

            const float pad = 14f;
            const float figureW = 52f;
            const float trendW = 96f;
            const float rowH = 40f;
            var inner = width - pad * 2f;

            if (commandNotable.Count == 0)
            {
                LedgerV2.Card("Notable", commandContent, left, -cursor, width, 46f,
                    LedgerV2.Panel);
                Line(commandContent, LedgerStyle.MonoItalic, 11.5f, LedgerV2.Muted,
                    left + pad, -(cursor + 14f), inner, 20f,
                    "Nobody is on the books to look at.");
                return cursor + 46f;
            }

            var height = commandNotable.Count * rowH + 12f;
            var frame = LedgerV2.Card("Notable", commandContent, left, -cursor, width,
                height, LedgerV2.Panel);

            for (var i = 0; i < commandNotable.Count; i++)
            {
                var man = commandNotable[i];
                var row = NewRect("Notable " + man.FullName, frame);
                PlaceTopLeft(row, pad, -(6f + i * rowH), inner, rowH);
                if (i < commandNotable.Count - 1)
                    Rule(row, 0f, -(rowH - 1f), inner, LedgerV2.Hair);

                // The name takes what is left after the figure and the shape of it.
                var nameW = Mathf.Max(80f, inner - figureW - trendW - 16f);
                var name = Line(row, LedgerStyle.Condensed, 15f, LedgerV2.Ink,
                    0f, -2f, nameW, LineBox(15f), man.FullName);
                name.overflowMode = TextOverflowModes.Ellipsis;

                // A man at ninety falling and a man at ninety rising are different
                // problems, and the fold answers both for nothing.
                var trend = Notability.Trend(man, today);
                var shape = trend > 0 ? "CLIMBING" : trend < 0 ? "FALLING AWAY" : "HOLDING";
                Caps(row, inner - figureW - trendW - 8f, -4f, trendW, shape, 9f,
                    trend > 0 ? LedgerV2.Green
                        : trend < 0 ? LedgerStyle.Ballpoint : LedgerV2.Label,
                    2f, TextAlignmentOptions.MidlineRight);

                var score = Notability.Of(man, today);
                var figure = Line(row, LedgerStyle.MonoBold, 15f,
                    score >= Notability.NewsBand ? LedgerV2.Ink : LedgerV2.Muted,
                    inner - figureW, -2f, figureW, LineBox(15f), score.ToString());
                figure.alignment = TextAlignmentOptions.MidlineRight;

                // WHY he is up there, in his own file's words - never re-worded here.
                var cause = Notability.Cause(man);
                var causeText = Line(row, LedgerStyle.Mono, 10f, LedgerV2.Muted,
                    0f, -21f, inner, LineBox(10f),
                    cause.Length > 0 ? cause : "Nothing on his file yet.");
                causeText.overflowMode = TextOverflowModes.Ellipsis;
            }

            return cursor + height;
        }

        /// <summary>
        /// FOLLOW-001. Every movement of a man's character, with the reason the clerk
        /// wrote for it, on the sheet where the player looks at his own house.
        ///
        /// EPIC 13's law is that there are no silent modifiers: every effect prints
        /// somewhere. The model has always obeyed it - <c>CampaignRunner.
        /// CharacterChanges</c> carries a written reason for every point that moves -
        /// and until this section nothing read the list, so a man deciding he was done
        /// with us read to the player as a number that fell for nothing.
        ///
        /// The reason is the PAYLOAD and is printed verbatim. The pen says which way it
        /// went - the ballpoint the book already uses for a man of ours who is no
        /// longer ours, the green it already uses for a promotion - and nothing here
        /// composes a sentence of its own.
        /// </summary>
        float BuildWordFromTheCrews(float left, float width, float cursor)
        {
            cursor = CommandSectionHead(cursor, left, width, "WORD FROM THE CREWS",
                "WHAT MOVED ON THE MEN, AND WHY", "", LedgerV2.Muted, null, null);

            var book = outfit ? outfit.ReasonBook : null;

            const float pad = 14f;
            const float edgeW = 3f;
            var inner = width - pad * 2f;
            var copyX = edgeW + 8f;
            var copyW = inner - copyX;

            if (book == null || book.Count == 0)
            {
                LedgerV2.Card("Word", commandContent, left, -cursor, width, 46f,
                    LedgerV2.Panel);
                Line(commandContent, LedgerStyle.MonoItalic, 11.5f, LedgerV2.Muted,
                    left + pad, -(cursor + 14f), inner, 20f,
                    book == null
                        ? "No campaign is running on this sheet."
                        : ReasonText.Quiet);
                return cursor + 46f;
            }

            // Newest DAY first, and inside it the loudest movement first. The two
            // orders pull against each other and walking the flat book backwards gets
            // only the first of them - it reads last night back to front and a limited
            // run then keeps the day's +1s and drops the swings this section exists to
            // show. ReasonFeed.Latest is where that is settled, once, and where the
            // headless suite can hold it.
            ReasonFeed.Latest(book, ReasonsShown, commandWords);

            // The panel cannot be placed until its slips have said how tall they are -
            // a reason is a sentence and the long ones run to two lines at this measure
            // - so it is built first and sized at the end.
            var frame = LedgerV2.Card("Word", commandContent, left, -cursor, width, 46f,
                LedgerV2.Panel);

            var run = commandWords.Count;
            var y = 8f;
            for (var i = 0; i < run; i++)
            {
                var word = commandWords[i];
                var ink = word.Rising ? LedgerStyle.GreenOk : LedgerStyle.Ballpoint;

                var head = word.Name + " · " +
                           ReasonText.Movement(word.Trait, word.Delta).ToUpperInvariant();
                var stamp = Caps(frame, pad + copyX, -y, copyW - 90f, head, 10f, ink, 3f);
                stamp.font = LedgerStyle.MonoBold;
                stamp.overflowMode = TextOverflowModes.Ellipsis;
                Caps(frame, pad + copyX, -y, copyW, LedgerText.DayStamp(word.Day), 9f,
                    LedgerV2.Label, 2f, TextAlignmentOptions.MidlineRight);

                // MEASURED, not assumed: TMP's ellipsis eats a whole line when the rect
                // cannot hold what it was given, so the rect is sized to what the face
                // says it needs.
                var copy = Paragraph(frame, LedgerStyle.Mono, 11f, LedgerV2.Body,
                    pad + copyX, -(y + 16f), copyW, LineBox(11f), word.Reason,
                    lineSpacing: 0f);
                var tall = Mathf.Max(LineBox(11f),
                    Mathf.Ceil(copy.GetPreferredValues(word.Reason, copyW, 0f).y));
                copy.rectTransform.sizeDelta = new Vector2(copyW, tall);

                var height = 16f + tall + 8f;
                Block("Pen", frame, pad, -y, edgeW, height - 6f, ink);
                y += height;
                if (i < run - 1)
                    Rule(frame, pad, -(y - 4f), inner, LedgerV2.Hair);
            }

            var total = y + 8f;
            frame.sizeDelta = new Vector2(width, total);
            return cursor + total;
        }

        /// <summary>
        /// The design's section head: a 19-unit heading on the left, whatever the
        /// section has to say held to the right of the same line, and a hairline under
        /// the pair at eight units. Answers the y twelve under the rule, which is where
        /// the section's own content starts.
        /// </summary>
        float CommandSectionHead(float cursor, string title, string summary,
            string figure, Color figureInk, string key, UnityAction onKey) =>
            CommandSectionHead(cursor, 0f, PageWidth, title, summary, figure, figureInk,
                key, onKey);

        /// <summary>
        /// The same head, struck inside a column of the sheet rather than across it.
        ///
        /// Under <see cref="NarrowSection"/> the aside cannot stand on the title's line
        /// without losing its own last words to an ellipsis, so it drops to a line of
        /// its own under the heading - the rule the ORGANIZATION sheet's sections have
        /// always kept, and there is one such rule in this book, not two.
        /// </summary>
        float CommandSectionHead(float cursor, float left, float width, string title,
            string summary, string figure, Color figureInk, string key, UnityAction onKey)
        {
            var narrow = width < NarrowSection;
            var line = LineBox(19f);

            // The key and the figure are measured and placed FIRST, from the right
            // margin in, because in a column they are what is left for the heading:
            // a title struck across the whole measure would run under them.
            var x = left + width;
            if (key != null)
            {
                const float keyH = 23f;
                var keyW = MonoWidth(key, 10f, 2f) + 20f;
                x -= keyW;
                var hire = LedgerV2.Button(commandContent, key, x,
                    LedgerV2.MarkY(-cursor, line, keyH), keyW, keyH, onKey,
                    LedgerV2.Key.Outline, 10f);
                SetActionEnabled(hire, director != null);
                x -= 14f;
            }
            var asideY = LedgerV2.MarkY(-cursor, line, LineBox(11f));
            if (figure.Length > 0)
            {
                var figureW = MonoWidth(figure, 11f, 1f) + 6f;
                x -= figureW;
                LedgerV2.Mono(commandContent, x, asideY, figureW, figure, 11f,
                    figureInk, 1f).font = LedgerStyle.MonoBold;
                x -= 14f;
            }

            // The heading sets the line and everything else is centred on IT - the
            // key, the figure and the aside are three different heights, and dropped
            // at their own tops they stood at three different levels.
            var headW = narrow
                ? Mathf.Max(60f, x - left)
                : width * 0.5f;
            var heading = Line(commandContent, LedgerStyle.Condensed, 19f, LedgerV2.Ink,
                left, -cursor, headW, line, title);
            heading.characterSpacing = 4f;
            heading.overflowMode = TextOverflowModes.Ellipsis;

            if (summary.Length > 0)
            {
                if (narrow)
                {
                    // A four-word aside set beside a heading in a column loses its own
                    // last words, so it drops to a line of its own - the rule the
                    // ORGANIZATION sheet's sections keep, and there is one such rule in
                    // this book rather than two.
                    Caps(commandContent, left, -(cursor + line + 1f), width,
                        summary, 9f, LedgerV2.Label, 2f);
                    cursor += LineBox(9f) + 1f;
                }
                else
                {
                    var summaryW = Mathf.Min(MonoWidth(summary, 11f, 1f) + 6f,
                        Mathf.Max(40f, x - left - width * 0.5f));
                    LedgerV2.Mono(commandContent, x - summaryW, asideY, summaryW,
                            summary, 11f, LedgerV2.Muted, 1f)
                        .overflowMode = TextOverflowModes.Ellipsis;
                }
            }

            cursor += line + 8f;
            Rule(commandContent, left, -cursor, width, LedgerV2.SheetRule);
            return cursor + 12f;
        }

        // ----------------------------------------------------------- the filed orders

        /// <summary>
        /// ORDERS FILED WITH THE OUTFIT, at the design's own measure: the stamp, what
        /// was asked, the status chip, and the outfit's ruling held to the right margin.
        /// The LOG is the filing office's - the same Outfit.Filings the ORGANIZATION
        /// sheet prints - and only the setting is this sheet's own.
        /// </summary>
        float BuildCommandOrders(float cursor)
        {
            cursor = CommandSectionHead(cursor, "ORDERS FILED WITH THE OUTFIT",
                "THIS SHEET ASKS · THE OUTFIT ANSWERS", "", LedgerV2.Muted, null, null);

            var filings = outfit ? outfit.Filings : null;
            var count = filings != null ? Mathf.Min(filings.All.Count, 6) : 0;

            const float rowH = 38f;
            const float pad = 18f;
            const float stampW = 74f;
            const float chipW = 112f;
            const float footH = 34f;
            var rulingW = Mathf.Min(240f, PageWidth * 0.22f);

            var panel = LedgerV2.Card("Filings", commandContent, 0f, -cursor, PageWidth,
                count * rowH + footH, LedgerV2.Panel);

            for (var i = 0; i < count; i++)
            {
                var filing = filings.All[i];
                var row = NewRect("Filing " + filing.Id, panel);
                PlaceTopLeft(row, 0f, -(i * rowH), PageWidth, rowH);
                Rule(row, 0f, -(rowH - 1f), PageWidth, LedgerV2.Hair);

                LedgerV2.Mono(row, pad, -(rowH - LineBox(11.5f)) * 0.5f, stampW,
                    filing.Stamp, 11.5f, LedgerV2.Muted, 0f);

                var chipX = PageWidth - pad - rulingW - 16f - chipW;
                LedgerV2.Mono(row, pad + stampW + 16f, -(rowH - LineBox(12.5f)) * 0.5f,
                        Mathf.Max(60f, chipX - pad - stampW - 32f), filing.Text, 12.5f,
                        LedgerV2.Body, 0f)
                    .overflowMode = TextOverflowModes.Ellipsis;

                var chip = NewRect("Status", row);
                PlaceTopLeft(chip, chipX, -(rowH - 23f) * 0.5f, chipW, 23f);
                Fill(chip, StatusColour(filing.Status));
                Caps(chip, 0f, -(23f - LineBox(10.5f)) * 0.5f, chipW,
                    StatusWord(filing.Status), 10.5f, LedgerV2.Panel, 5f,
                    TextAlignmentOptions.Center);

                LedgerV2.Mono(row, PageWidth - pad - rulingW,
                        -(rowH - LineBox(11f)) * 0.5f, rulingW, filing.Ruling, 11f,
                        LedgerV2.Muted, 0f, TextAlignmentOptions.MidlineRight)
                    .overflowMode = TextOverflowModes.Ellipsis;
            }

            var awaiting = filings != null ? filings.AwaitingCount : 0;
            LedgerV2.Mono(panel, pad, -(count * rowH + 10f), PageWidth - pad * 2f,
                filings == null
                    ? "The outfit's filing office is not open in this scene · orders " +
                      "take effect the moment they are given."
                    : count == 0
                        ? "Nothing has been asked of the outfit yet."
                        : awaiting > 0
                            ? "Nothing above has happened yet. The outfit is still " +
                              "ruling on " + awaiting + "."
                            : "Every order on this sheet has been ruled on.",
                11.5f, LedgerV2.Muted, 0f);

            return cursor + count * rowH + footH;
        }

        // ---------------------------------------------------------- the sheet's verbs

        void ToggleCommandBoss()
        {
            commandBossOpen = !commandBossOpen;
            commandNote = "";
            dirty = true;
        }

        void ToggleCommandFile(int id)
        {
            if (!commandOpenFiles.Remove(id))
                commandOpenFiles.Add(id);
            else if (commandArmsOpenId == id)
                commandArmsOpenId = -1;
            commandNote = "";
            dirty = true;
        }

        /// <summary>The gun drawer under one man's CARRIES line.</summary>
        void ToggleCommandArms(int id)
        {
            commandArmsOpenId = commandArmsOpenId == id ? -1 : id;
            commandNote = "";
            dirty = true;
        }

        /// <summary>Raising a man to lieutenant is filed like every other order: the
        /// outfit rules on the span of control, not this sheet.</summary>
        void FilePromotion(int hoodId)
        {
            var hood = Person(hoodId);
            if (!hood.IsValid)
                return;

            FileOrder(hood.Name + " raised to lieutenant.", () =>
            {
                var check = director.CheckPromote(hoodId);
                if (!check.CanPromote)
                    return Outfit.FilingRuling.Refuse(check.Reason);
                var result = director.Promote(hoodId, out _);
                return result.Ok
                    ? Outfit.FilingRuling.Grant("his own branch from today")
                    : Outfit.FilingRuling.Refuse(result.Reason);
            });
        }

        /// <summary>Esc peels one layer off this sheet: the full dossier popup, the
        /// pick, then the open files, then the Boss's own.</summary>
        bool CloseCommandTransient()
        {
            if (commandDossierId >= 0)
            {
                CloseCommandDossier();
                return true;
            }
            if (organizationPickedHoodId >= 0)
            {
                organizationPickedHoodId = -1;
                organizationNote = "";
                dirty = true;
                return true;
            }
            if (commandArmsOpenId >= 0)
            {
                commandArmsOpenId = -1;
                dirty = true;
                return true;
            }
            if (commandOpenFiles.Count > 0)
            {
                commandOpenFiles.Clear();
                dirty = true;
                return true;
            }
            if (commandBossOpen)
            {
                commandBossOpen = false;
                dirty = true;
                return true;
            }
            return false;
        }

        // -------------------------------------------------------------------- pieces

        /// <summary>A connector: two units, dashed. Every line in this tree is one of
        /// these - the drop, the spine, the stubs and the rails - and they all have to
        /// read as the same stroke or the tree comes apart.</summary>
        static void DashDown(Transform parent, float x, float top, float height)
        {
            if (height <= 0f)
                return;
            var rect = NewRect("Dash down", parent);
            PlaceTopLeft(rect, x - DashW * 0.5f, -top, DashW, height);
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = LedgerStyle.DotRuleDown;
            raw.color = LedgerV2.Dotted;
            raw.uvRect = new Rect(0f, 0f, 1f, height / 5f);
            raw.raycastTarget = false;
        }

        static void DashAcross(Transform parent, float x, float top, float width)
        {
            if (width <= 0f)
                return;
            var rect = NewRect("Dash across", parent);
            PlaceTopLeft(rect, x, -(top + DashW * 0.5f), width, DashW);
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = LedgerStyle.DotRule;
            raw.color = LedgerV2.Dotted;
            raw.uvRect = new Rect(0f, 0f, width / 5f, 1f);
            raw.raycastTarget = false;
        }

        /// <summary>A round photograph in a ring - the design's own portrait slot. The
        /// disc IS the mask, so the picture is cropped to the circle rather than drawn
        /// square behind it, and the ring is a second disc standing behind the first.
        /// </summary>
        static void RoundFace(Transform parent, float x, float lift, float diameter,
            Character member, string initials, Color ground, Color ink, Color rim)
        {
            const float wall = 2f;

            var ringRect = NewRect("Rim", parent);
            PlaceTopLeft(ringRect, x - wall, lift + wall, diameter + wall * 2f,
                diameter + wall * 2f);
            var ring = ringRect.gameObject.AddComponent<Image>();
            ring.sprite = LedgerStyle.FaceDisc;
            ring.color = rim;
            ring.raycastTarget = false;

            var slot = NewRect("Round face", parent);
            PlaceTopLeft(slot, x, lift, diameter, diameter);
            var disc = slot.gameObject.AddComponent<Image>();
            // The FINE disc, not the dot: this one is the stencil the photograph is
            // cut with, and a mask cuts as coarsely as the circle it was given.
            disc.sprite = LedgerStyle.FaceDisc;
            disc.color = ground;
            disc.raycastTarget = false;

            var mask = slot.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            var mark = Caps(slot, 0f, -(diameter - LineBox(11f)) * 0.5f, diameter,
                initials, 11f, ink, 10f, TextAlignmentOptions.Center);
            mark.font = LedgerStyle.Mono;

            var picture = NewRect("Picture", slot);
            Stretch(picture);
            var raw = picture.gameObject.AddComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = false;
            raw.enabled = false;
            if (member != null)
                PortraitStudio.Request(MemberModel(member), PortraitStudio.Framing.Bust,
                    raw);
        }

        /// <summary>A word that does something: an invisible key over the type, so the
        /// word itself is the thing pressed. The design's names, its CLOSE and its
        /// PULL / PLACE are all bare words, never boxed keys.</summary>
        static void NameKey(Transform parent, float x, float y, float w, float h,
            UnityAction onClick)
        {
            var zone = NewRect("Name key", parent);
            PlaceTopLeft(zone, x, y, w, h);
            RowButton(zone, ClickSurface(zone), onClick);
        }

        /// <summary>What a run of IBM Plex Mono measures. The face is monospaced at
        /// 0.6 em (LedgerStyle documents the ratio) and TMP's letter-spacing is in
        /// hundredths of an em, so a mono run's width is arithmetic - which is what lets
        /// this sheet lay a flowing row without a layout pass. The size asked for is not
        /// the size that prints - the book lifts its small print - so the arithmetic is
        /// struck off LedgerKit.BookSize, the same rule the type itself goes through.
        /// </summary>
        static float MonoWidth(string text, float size, float spacing)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;
            var printed = LedgerKit.BookSize(size);
            return text.Length * (printed * 0.6f + printed * spacing / 100f);
        }

        /// <summary>What a run of the condensed gothic measures. Oswald is proportional,
        /// so this is the real thing: TMP is asked, off a face carrying the same font
        /// and size that will print it. One hidden face answers for the whole sheet.
        /// </summary>
        static float CondensedWidth(string text, float size)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;
            if (condensedRule == null)
            {
                var host = new GameObject("Ledger measure", typeof(RectTransform));
                host.hideFlags = HideFlags.HideAndDontSave;
                condensedRule = host.AddComponent<TextMeshProUGUI>();
                condensedRule.font = LedgerStyle.Condensed;
                condensedRule.enabled = false;
            }
            // The measuring face hangs outside the book, so the lift the book gives its
            // small print never reaches it. Ask for the size that will actually print.
            condensedRule.fontSize = LedgerKit.BookSize(size);
            return condensedRule.GetPreferredValues(text, 0f, 0f).x;
        }

        static TextMeshProUGUI condensedRule;

        /// <summary>What a block of the serif copy face measures DOWN when it is poured
        /// into a column of a given width. The same question the personal file asks of
        /// its own career lines, asked off one hidden face so a page can strike a
        /// height before it commits to building the paragraph.</summary>
        static float CopyHeight(string text, float size, float width, float lineSpacing)
        {
            if (string.IsNullOrEmpty(text) || width <= 0f)
                return 0f;
            if (copyRule == null)
            {
                var host = new GameObject("Ledger copy measure", typeof(RectTransform));
                host.hideFlags = HideFlags.HideAndDontSave;
                copyRule = host.AddComponent<TextMeshProUGUI>();
                copyRule.font = LedgerStyle.Serif;
                copyRule.enabled = false;
            }
            copyRule.fontSize = LedgerKit.BookSize(size);
            copyRule.lineSpacing = lineSpacing;
            copyRule.textWrappingMode = TextWrappingModes.Normal;
            return Mathf.Ceil(copyRule.GetPreferredValues(text, width, 0f).y);
        }

        static TextMeshProUGUI copyRule;

        /// <summary>What the stock book signed out to him, in the file's own words. A
        /// man the book has issued nothing to still has the .38 in his coat, which is
        /// what the roll prints beside his name and what this has to agree with.
        /// </summary>
        string CarriedGun(Character member)
        {
            var roster = director != null ? director.Roster : null;
            if (roster == null || member == null)
                return "--";
            var line = CarryingLine(roster, member, out var issued);
            return issued || !member.Gone ? line : "nothing";
        }

        string AnswersTo(Character member)
        {
            if (member.Rank == Rank.Boss)
                return "nobody";
            return director.Organization != null &&
                   director.Organization.TryGetCommandParent(member.Id, out var parent)
                ? parent.Name
                : "no valid command parent";
        }

        string BlocksOnPaper(int leaderId)
        {
            var query = director != null ? director.Organization : null;
            if (query == null)
                return "none";
            query.CollectBlockResponsibilities(leaderId, organizationResponsibilities);
            if (organizationResponsibilities.Count == 0)
                return "none";
            var line = "";
            for (var i = 0; i < organizationResponsibilities.Count && i < 3; i++)
                line += (i > 0 ? ", " : "") +
                        BlockName(organizationResponsibilities[i].BlockId);
            if (organizationResponsibilities.Count > 3)
                line += " +" + (organizationResponsibilities.Count - 3);
            return line;
        }
    }
}
