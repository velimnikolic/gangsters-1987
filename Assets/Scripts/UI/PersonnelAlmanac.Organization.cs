using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Gangs;
using LivingCity.Personnel;
using LivingCity.Territory;
using RoadDemo;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// ORGANIZATION is a paper command file: compact Boss/Lieutenant summaries open one
    /// dossier at a time. Every figure is an IOrganizationQuery snapshot and every verb
    /// leaves through PersonnelDirector or the territory command gateway. No roster,
    /// account, Character assignment, or territory signal is written by this page.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        const float OrganizationTop = PageTop - 42f;
        const float OrganizationHeight = 618f;
        const float OrganizationGap = 14f;
        const float OrganizationRowH = 38f;

        enum OrganizationPersonnelMode
        {
            None,
            AssignUnassigned,
            Transfer,
        }

        RectTransform organizationFixed;
        internal RectTransform organizationViewport;
        internal RectTransform organizationContent;
        internal float organizationScroll;

        int selectedOrganizationLeaderId = -1;
        bool organizationDetailOpen;
        OrganizationPersonnelMode organizationPersonnelMode;
        string organizationNote = "";

        // Map targeting is intentionally transient. The canonical ID is not committed
        // until the returned Ledger confirmation is pressed.
        int organizationTargetLeaderId = -1;
        TerritoryBlockId organizationPendingBlock;
        int organizationPendingLeaderId = -1;
        string organizationPendingBlockName = "";

        readonly List<OrganizationPerson> organizationLeaders =
            new List<OrganizationPerson>();
        readonly List<OrganizationPerson> organizationPeople =
            new List<OrganizationPerson>();
        readonly List<OrganizationPerson> organizationScratch =
            new List<OrganizationPerson>();
        readonly List<OrganizationBlockResponsibility> organizationResponsibilities =
            new List<OrganizationBlockResponsibility>();
        readonly List<TacticalPersonnelMapping> organizationPhysical =
            new List<TacticalPersonnelMapping>();
        readonly HashSet<int> organizationPhysicalIds = new HashSet<int>();

        bool OrganizationTargetingActive => organizationTargetLeaderId >= 0;

        void BuildOrganizationPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Organization);
            organizationFixed = NewRect("Organization Fixed", root);
            Stretch(organizationFixed);

            organizationViewport = NewRect("Organization Window", root);
            PlaceTopLeft(organizationViewport, PageLeft, OrganizationTop,
                PageWidth, OrganizationHeight);
            organizationViewport.gameObject.AddComponent<RectMask2D>();

            organizationContent = NewRect("Organization File", organizationViewport);
            organizationContent.anchorMin = new Vector2(0f, 1f);
            organizationContent.anchorMax = new Vector2(1f, 1f);
            organizationContent.pivot = new Vector2(0f, 1f);
            organizationContent.anchoredPosition = Vector2.zero;
            organizationContent.sizeDelta = new Vector2(0f, OrganizationHeight);
        }

        void RebuildOrganization()
        {
            if (!organizationFixed || !organizationContent)
                return;

            foreach (Transform old in organizationFixed)
                Destroy(old.gameObject);
            foreach (Transform old in organizationContent)
                Destroy(old.gameObject);

            var heading = Line(organizationFixed, LedgerStyle.Condensed, 19f,
                LedgerStyle.Ink, PageLeft, PageTop, 760f, 26f,
                organizationDetailOpen ? "ORGANIZATION · COMMAND DOSSIER" : "ORGANIZATION");
            heading.characterSpacing = 5f;
            Caps(organizationFixed, PageRight - 730f, PageTop - 1f, 730f,
                "RESPONSIBILITY IS ADMINISTRATION · CONTROL IS STREET TRUTH",
                9.5f, LedgerStyle.RedPen, 3f, TextAlignmentOptions.MidlineRight);
            if (!string.IsNullOrEmpty(organizationNote))
                Caps(organizationFixed, PageRight - 900f, PageTop - 24f, 900f,
                    organizationNote, 9f, LedgerStyle.Ballpoint, 2f,
                    TextAlignmentOptions.MidlineRight);

            var query = director != null ? director.Organization : null;
            if (query == null || !query.TryGetBoss(out var boss))
            {
                Line(organizationContent, LedgerStyle.MonoItalic, 14f,
                    LedgerStyle.RedPen, 0f, 0f, PageWidth, 24f,
                    "The command file has no authoritative Boss Character.");
                return;
            }

            organizationLeaders.Clear();
            organizationLeaders.Add(boss);
            query.CollectLieutenants(organizationScratch);
            organizationLeaders.AddRange(organizationScratch);
            query.CollectHoods(organizationPeople);

            if (!ContainsLeader(selectedOrganizationLeaderId))
            {
                selectedOrganizationLeaderId = boss.Id;
                organizationDetailOpen = false;
                ClearOrganizationPendingBlock();
            }

            var cursor = organizationDetailOpen
                ? BuildOrganizationDetail(query, Leader(selectedOrganizationLeaderId))
                : BuildOrganizationOverview(query, boss);
            var contentHeight = Mathf.Max(OrganizationHeight, cursor + 24f);
            organizationContent.sizeDelta = new Vector2(0f, contentHeight);
            organizationScroll = Mathf.Clamp(
                organizationScroll, 0f, Mathf.Max(0f, contentHeight - OrganizationHeight));
            organizationContent.anchoredPosition = new Vector2(0f, organizationScroll);
        }

        float BuildOrganizationOverview(IOrganizationQuery query, OrganizationPerson boss)
        {
            var title = Line(organizationContent, LedgerStyle.Condensed, 28f,
                LedgerStyle.Ink, 0f, 0f, PageWidth, 36f, OutfitTitle(boss.Name));
            title.characterSpacing = 2f;
            Caps(organizationContent, 0f, -35f, PageWidth,
                "CHAIN OF COMMAND · CURRENT AUTHORITATIVE BOOK",
                10f, LedgerStyle.InkLabel, 4f);

            var cursor = 66f;
            cursor = BuildLeaderSummary(query, boss, cursor, bossCard: true);
            cursor += 18f;

            Line(organizationContent, LedgerStyle.Condensed, 18f, LedgerStyle.Ink,
                0f, -cursor, PageWidth, 24f, "LIEUTENANTS");
            cursor += 28f;
            Rule(organizationContent, 0f, -cursor, PageWidth, LedgerStyle.Ink);
            cursor += 8f;

            if (organizationLeaders.Count == 1)
            {
                Line(organizationContent, LedgerStyle.MonoItalic, 12f,
                    LedgerStyle.InkDim, 8f, -cursor, PageWidth - 16f, 20f,
                    "No lieutenant branches are on the books.");
                cursor += 36f;
            }
            else
            {
                for (var i = 1; i < organizationLeaders.Count; i++)
                    cursor = BuildLeaderSummary(query, organizationLeaders[i], cursor,
                        bossCard: false);
            }

            cursor += 12f;
            return BuildRecruitmentCard(cursor);
        }

        float BuildLeaderSummary(
            IOrganizationQuery query, OrganizationPerson leader, float cursor, bool bossCard)
        {
            var height = bossCard ? 126f : 82f;
            var card = Card("Command · " + leader.Name, organizationContent,
                0f, -cursor, PageWidth, height,
                bossCard ? LedgerStyle.IndexCard : LedgerStyle.Printout,
                shadowSpread: bossCard ? 8f : 3f,
                low: bossCard ? LedgerStyle.IndexCardLow : LedgerStyle.PrintoutLow);

            var name = Line(card, LedgerStyle.Condensed, bossCard ? 23f : 19f,
                LedgerStyle.Ink, 16f, -10f, 570f, 28f, leader.Name);
            name.characterSpacing = 1.5f;
            Caps(card, 16f, bossCard ? -43f : -38f, 500f,
                leader.Rank == Rank.Boss ? "BOSS" : "LIEUTENANT",
                10f, LedgerStyle.RedPen, 4f);

            var capacity = query.CapacityOf(leader.Id);
            CapacityFigure(card, bossCard ? 610f : 540f, -12f, 250f,
                "MANPOWER", capacity.Manpower);
            CapacityFigure(card, bossCard ? 880f : 810f, -12f, 290f,
                "BLOCK RESPONSIBILITY", capacity.Blocks);

            if (capacity.IsOverCapacity)
                Caps(card, 16f, bossCard ? -69f : -58f, 640f,
                    "OVER CAPACITY" + OverageSuffix(capacity),
                    10f, LedgerStyle.RedPen, 4f);
            else if (bossCard)
            {
                var available = CountUnassigned();
                Caps(card, 16f, -69f, 640f,
                    available + " HOOD" + (available == 1 ? "" : "S") +
                    " AVAILABLE FOR ASSIGNMENT",
                    9.5f, LedgerStyle.InkLabel, 3f);
            }

            var id = leader.Id;
            Tape(card, "OPEN DOSSIER", PageWidth - 190f, -height + 40f,
                174f, 28f, () => OpenOrganizationLeader(id),
                outline: true, size: 9.5f);
            return cursor + height + (bossCard ? 0f : 8f);
        }

        static void CapacityFigure(
            Transform parent, float x, float y, float width,
            string label, CapacityMeasure measure)
        {
            Caps(parent, x, y, width, label, 9f,
                measure.IsOverCapacity ? LedgerStyle.RedPen : LedgerStyle.InkLabel,
                3f);
            Line(parent, LedgerStyle.MonoBold, 18f,
                measure.IsOverCapacity ? LedgerStyle.RedPen : LedgerStyle.Ink,
                x, y - 24f, width, 25f,
                measure.Current + " / " + measure.Maximum);
        }

        float BuildRecruitmentCard(float cursor)
        {
            var card = Card("Recruit Hood", organizationContent,
                0f, -cursor, PageWidth, 104f, LedgerStyle.IndexCard,
                shadowSpread: 7f, low: LedgerStyle.IndexCardLow);
            Line(card, LedgerStyle.Condensed, 20f, LedgerStyle.Ink,
                16f, -11f, 380f, 27f, "RECRUIT HOOD");
            Caps(card, 16f, -44f, 300f,
                "COST · " + LedgerText.Cash(director.HoodRecruitmentCost),
                10f, LedgerStyle.RedPen, 4f);
            Line(card, LedgerStyle.MonoItalic, 11f, LedgerStyle.InkDim,
                330f, -14f, 720f, 48f,
                "One randomized Hood signs immediately, enters the unassigned pool, " +
                "and reports directly to the Boss. No crew is created.");

            var recruit = Tape(card, "RECRUIT", PageWidth - 184f, -34f,
                168f, 30f, RecruitOrganizationHood, red: true, size: 10f);
            SetActionEnabled(recruit, outfit != null);
            if (!outfit)
                Caps(card, PageWidth - 410f, -73f, 394f,
                    "ACCOUNT BOOK UNAVAILABLE", 9f, LedgerStyle.RedPen, 2f,
                    TextAlignmentOptions.MidlineRight);
            return cursor + 104f;
        }

        float BuildOrganizationDetail(IOrganizationQuery query, OrganizationPerson leader)
        {
            var cursor = 0f;
            Tape(organizationContent, "< ORGANIZATION", 0f, 0f, 190f, 28f,
                BackToOrganizationOverview, outline: true, size: 9.5f);

            var name = Line(organizationContent, LedgerStyle.Condensed, 28f,
                LedgerStyle.Ink, 0f, -43f, 790f, 36f, leader.Name);
            name.characterSpacing = 2f;
            Caps(organizationContent, 0f, -78f, 500f,
                leader.Rank == Rank.Boss ? "BOSS · ROOT COMMAND" : "LIEUTENANT · BRANCH COMMAND",
                10f, LedgerStyle.RedPen, 4f);
            OrganizationPerson commandParent = default;
            var hasCommandParent = leader.Rank != Rank.Boss &&
                                   query.TryGetCommandParent(leader.Id, out commandParent);
            Caps(organizationContent, 520f, -78f, PageWidth - 520f,
                leader.Rank == Rank.Boss
                    ? "REPORTS TO · NOBODY · ROOT COMMAND"
                    : "REPORTS TO · " +
                      (hasCommandParent ? commandParent.Name : "NO VALID COMMAND PARENT"),
                9f, hasCommandParent || leader.Rank == Rank.Boss
                    ? LedgerStyle.InkLabel
                    : LedgerStyle.RedPen,
                3f, TextAlignmentOptions.MidlineRight);
            cursor = 112f;

            var capacity = query.CapacityOf(leader.Id);
            var half = (PageWidth - OrganizationGap) * 0.5f;
            BuildCapacityCard("MANPOWER", capacity.Manpower, 0f, cursor, half);
            BuildCapacityCard("BLOCK RESPONSIBILITY", capacity.Blocks,
                half + OrganizationGap, cursor, half);
            cursor += 102f;

            if (capacity.IsOverCapacity)
            {
                Stamp(organizationContent, "OVER CAPACITY", PageWidth - 250f,
                    -cursor + 6f, 240f, 34f, tilt: -2f, size: 15f);
                cursor += 42f;
            }

            if (organizationPendingBlock.IsValid &&
                organizationPendingLeaderId == leader.Id)
                cursor = BuildBlockConfirmation(query, leader, cursor);

            cursor = BuildAssignedResponsibilities(query, leader, cursor);
            cursor = BuildPersonnelSection(query, leader, cursor);

            return cursor;
        }

        void BuildCapacityCard(
            string label, CapacityMeasure measure, float x, float cursor, float width)
        {
            var card = Card(label, organizationContent, x, -cursor, width, 88f,
                LedgerStyle.Printout, shadowSpread: 3f, low: LedgerStyle.PrintoutLow);
            Caps(card, 14f, -10f, width - 28f, label, 10f,
                measure.IsOverCapacity ? LedgerStyle.RedPen : LedgerStyle.InkLabel, 4f);
            Line(card, LedgerStyle.MonoBold, 24f,
                measure.IsOverCapacity ? LedgerStyle.RedPen : LedgerStyle.Ink,
                14f, -37f, width - 28f, 32f,
                measure.Current + " / " + measure.Maximum);
            if (measure.IsOverCapacity)
                Caps(card, width - 220f, -53f, 206f,
                    "+" + measure.Overage + " OVER NORMAL CAPACITY",
                    8.5f, LedgerStyle.RedPen, 2f,
                    TextAlignmentOptions.MidlineRight);
        }

        float BuildBlockConfirmation(
            IOrganizationQuery query, OrganizationPerson leader, float cursor)
        {
            var capacity = query.CapacityOf(leader.Id).Blocks;
            var existingLeader = ResponsibilityLeader(query, organizationPendingBlock);
            var extra = existingLeader == leader.Id ? 0 : 1;
            var willOver = capacity.Current + extra > capacity.Maximum;
            var alreadyHere = existingLeader == leader.Id;

            var card = Card("Confirm Block Responsibility", organizationContent,
                0f, -cursor, PageWidth, willOver ? 174f : 148f,
                LedgerStyle.IndexCard, shadowSpread: 9f, low: LedgerStyle.IndexCardLow);
            Line(card, LedgerStyle.Condensed, 19f, LedgerStyle.Ink,
                16f, -10f, 720f, 26f, "ASSIGN TERRITORY RESPONSIBILITY");
            Caps(card, 16f, -43f, 700f,
                leader.Name + " · " + organizationPendingBlockName,
                10f, LedgerStyle.Ballpoint, 3f);
            Line(card, LedgerStyle.Mono, 12f, LedgerStyle.Ink,
                16f, -69f, 470f, 20f,
                "CURRENT  " + capacity.Current + " / " + capacity.Maximum + " BLOCKS");

            if (willOver)
                Paragraph(card, LedgerStyle.MonoItalic, 11.5f, LedgerStyle.RedPen,
                    16f, -94f, 740f, 42f,
                    "This assignment will put " + leader.Name +
                    " over normal command capacity. No efficiency penalty is applied here.",
                    lineSpacing: 1f);
            else if (alreadyHere)
                Line(card, LedgerStyle.MonoItalic, 11.5f, LedgerStyle.RedPen,
                    16f, -96f, 720f, 20f,
                    "This command node is already responsible for that block.");

            var y = willOver ? -128f : -104f;
            var confirm = Tape(card, willOver ? "ASSIGN ANYWAY" : "ASSIGN",
                PageWidth - 356f, y, 166f, 30f,
                alreadyHere ? null : ConfirmBlockResponsibility,
                red: willOver, size: 9.5f);
            SetActionEnabled(confirm, !alreadyHere);
            Tape(card, "CANCEL", PageWidth - 178f, y, 162f, 30f,
                CancelPendingBlock, outline: true, size: 9.5f);
            return cursor + (willOver ? 174f : 148f) + 16f;
        }

        float BuildAssignedResponsibilities(
            IOrganizationQuery query, OrganizationPerson leader, float cursor)
        {
            Line(organizationContent, LedgerStyle.Condensed, 18f, LedgerStyle.Ink,
                0f, -cursor, 520f, 24f, "RESPONSIBLE FOR");
            cursor += 29f;
            Rule(organizationContent, 0f, -cursor, PageWidth, LedgerStyle.Ink);
            cursor += 8f;

            query.CollectBlockResponsibilities(leader.Id, organizationResponsibilities);
            if (organizationResponsibilities.Count == 0)
            {
                Line(organizationContent, LedgerStyle.MonoItalic, 12f,
                    LedgerStyle.InkDim, 8f, -cursor, 760f, 20f,
                    "No organizational block responsibility is filed here.");
                cursor += 34f;
            }
            else
            {
                for (var i = 0; i < organizationResponsibilities.Count; i++)
                {
                    var responsibility = organizationResponsibilities[i];
                    var row = NewRect("Responsibility · " + responsibility.BlockId,
                        organizationContent);
                    PlaceTopLeft(row, 0f, -cursor, PageWidth, 50f);
                    if ((i & 1) == 1)
                        Fill(row, LedgerStyle.GreenbarBand);

                    Line(row, LedgerStyle.MonoBold, 12f, LedgerStyle.Ink,
                        10f, -3f, 480f, 20f, BlockName(responsibility.BlockId));
                    Caps(row, 510f, -3f, 570f,
                        "RESPONSIBLE FOR · " + leader.Name,
                        9f, LedgerStyle.Ballpoint, 2f);
                    Caps(row, 510f, -25f, 570f,
                        CurrentControlLine(responsibility.BlockId),
                        9f, LedgerStyle.InkLabel, 2f);

                    var blockId = responsibility.BlockId;
                    Tape(row, "REMOVE", PageWidth - 148f, -10f, 138f, 28f,
                        () => RemoveBlock(blockId), red: true,
                        outline: true, size: 9f);
                    cursor += 50f;
                }
            }

            return cursor + 12f;
        }

        float BuildPersonnelSection(
            IOrganizationQuery query, OrganizationPerson leader, float cursor)
        {
            Line(organizationContent, LedgerStyle.Condensed, 18f, LedgerStyle.Ink,
                0f, -cursor, 520f, 24f, "PERSONNEL");
            cursor += 29f;
            Rule(organizationContent, 0f, -cursor, PageWidth, LedgerStyle.Ink);
            cursor += 10f;

            query.CollectDirectSubordinates(leader.Id, organizationScratch);
            query.CollectPhysicalMappings(organizationPhysical);
            organizationPhysicalIds.Clear();
            for (var i = 0; i < organizationPhysical.Count; i++)
                for (var p = 0; p < organizationPhysical[i].PersonnelIds.Count; p++)
                    organizationPhysicalIds.Add(organizationPhysical[i].PersonnelIds[p]);

            var available = 0;
            var assigned = 0;
            var unavailable = 0;
            var onStreet = 0;
            for (var i = 0; i < organizationScratch.Count; i++)
            {
                var person = organizationScratch[i];
                if (person.Rank != Rank.Hood)
                    continue;
                if (!person.IsAvailable)
                    unavailable++;
                else
                    available++;
                if (!person.IsUnassigned)
                    assigned++;
                if (person.IsAvailable && organizationPhysicalIds.Contains(person.Id))
                    onStreet++;
            }

            var cell = PageWidth / 4f;
            PersonnelFigure(0f, cursor, cell, "AVAILABLE", available);
            PersonnelFigure(cell, cursor, cell, "ON STREET", onStreet);
            PersonnelFigure(cell * 2f, cursor, cell, "ASSIGNED", assigned);
            PersonnelFigure(cell * 3f, cursor, cell, "UNAVAILABLE", unavailable);
            cursor += 58f;

            var gap = 10f;
            var buttonW = (PageWidth - gap * 3f) / 4f;
            Tape(organizationContent, "VIEW PERSONNEL", 0f, -cursor,
                buttonW, 30f, ViewSelectedLeaderPersonnel, outline: true, size: 9f);
            Tape(organizationContent, "ASSIGN MEN", buttonW + gap, -cursor,
                buttonW, 30f, () => SetOrganizationPersonnelMode(
                    OrganizationPersonnelMode.AssignUnassigned), size: 9f);
            Tape(organizationContent, "TRANSFER MEN", (buttonW + gap) * 2f, -cursor,
                buttonW, 30f, () => SetOrganizationPersonnelMode(
                    OrganizationPersonnelMode.Transfer), size: 9f);
            var target = Tape(organizationContent, "ASSIGN BLOCK",
                (buttonW + gap) * 3f, -cursor, buttonW, 30f,
                BeginBlockTargeting, red: true, size: 9f);
            SetActionEnabled(target,
                StrategicMapHud.Instance != null && TerritoryRuntime.Instance?.Commands != null);
            cursor += 48f;

            if (organizationPersonnelMode != OrganizationPersonnelMode.None)
                cursor = BuildPersonnelPicker(query, leader, cursor);
            return cursor;
        }

        void PersonnelFigure(
            float x, float cursor, float width, string label, int value,
            bool numeric = true, string text = "")
        {
            Caps(organizationContent, x, -cursor, width - 10f,
                label, 9f, LedgerStyle.InkLabel, 3f);
            Line(organizationContent, LedgerStyle.MonoBold, numeric ? 18f : 12f,
                LedgerStyle.Ink, x, -cursor - 22f, width - 10f, 24f,
                numeric ? value.ToString() : text);
        }

        float BuildPersonnelPicker(
            IOrganizationQuery query, OrganizationPerson leader, float cursor)
        {
            var assigning = organizationPersonnelMode ==
                            OrganizationPersonnelMode.AssignUnassigned;
            Line(organizationContent, LedgerStyle.Condensed, 16f, LedgerStyle.Ink,
                0f, -cursor, 760f, 23f,
                assigning ? "ASSIGN UNASSIGNED HOODS" : "TRANSFER / REMOVE HOODS");
            Tape(organizationContent, "CLOSE", PageWidth - 120f, -cursor,
                120f, 24f, () => SetOrganizationPersonnelMode(
                    OrganizationPersonnelMode.None), outline: true, size: 8.5f);
            cursor += 30f;

            var shown = 0;
            for (var i = 0; i < organizationPeople.Count; i++)
            {
                var hood = organizationPeople[i];
                if (assigning && (!hood.IsUnassigned || !hood.IsAvailable))
                    continue;

                query.TryGetCommandParent(hood.Id, out var parent);
                var sameParent = parent.IsValid && parent.Id == leader.Id;
                var row = NewRect("Hood · " + hood.Name, organizationContent);
                PlaceTopLeft(row, 0f, -cursor, PageWidth, OrganizationRowH);
                if ((shown & 1) == 1)
                    Fill(row, LedgerStyle.GreenbarBand);

                Line(row, LedgerStyle.Mono, 12f, LedgerStyle.Ink,
                    10f, -6f, 390f, 20f, hood.Name);
                var status = !hood.IsAvailable
                    ? hood.Status.ToString().ToUpperInvariant()
                    : hood.IsUnassigned ? "UNASSIGNED"
                    : hood.Assignment == AssignmentKind.Front ? "FRONT POST"
                    : hood.Assignment == AssignmentKind.Crew ? "BRANCH ASSIGNED"
                    : hood.Assignment.ToString().ToUpperInvariant();
                Caps(row, 410f, -7f, 650f,
                    "HOOD · " + status + " · REPORTS TO " +
                    (parent.IsValid ? parent.Name : "NO VALID PARENT"),
                    9f, parent.IsValid ? LedgerStyle.InkLabel : LedgerStyle.RedPen, 2f);

                var hoodId = hood.Id;
                Tape(row, "FILE", PageWidth - 250f, -5f, 90f, 28f,
                    () => ViewPersonnelMember(hoodId), outline: true, size: 8.5f);
                if (sameParent)
                {
                    if (!assigning && leader.Rank == Rank.Lieutenant)
                        Tape(row, "REMOVE", PageWidth - 150f, -5f, 140f, 28f,
                            () => RemoveHoodToBoss(hoodId), red: true,
                            outline: true, size: 8.5f);
                    else
                        Caps(row, PageWidth - 150f, -8f, 140f,
                            hood.IsUnassigned ? "AVAILABLE" : "REPORTS HERE",
                            8.5f, LedgerStyle.InkLabel, 2f,
                            TextAlignmentOptions.MidlineRight);
                }
                else
                {
                    Tape(row, assigning ? "ASSIGN HERE" : "MOVE HERE",
                        PageWidth - 150f, -5f, 140f, 28f,
                        () => MoveHood(hoodId), size: 8.5f);
                }

                shown++;
                cursor += OrganizationRowH;
            }

            if (shown == 0)
            {
                Line(organizationContent, LedgerStyle.MonoItalic, 12f,
                    LedgerStyle.InkDim, 8f, -cursor, PageWidth - 16f, 20f,
                    assigning
                        ? "No unassigned Hoods are available. Recruit one or remove one from a branch."
                        : "No Hoods are available in the command graph.");
                cursor += 32f;
            }
            return cursor + 12f;
        }

        void OpenOrganizationLeader(int leaderId)
        {
            selectedOrganizationLeaderId = leaderId;
            organizationDetailOpen = true;
            organizationPersonnelMode = OrganizationPersonnelMode.None;
            ClearOrganizationPendingBlock();
            organizationScroll = 0f;
            organizationNote = "command dossier opened";
            dirty = true;
        }

        void BackToOrganizationOverview()
        {
            organizationDetailOpen = false;
            organizationPersonnelMode = OrganizationPersonnelMode.None;
            ClearOrganizationPendingBlock();
            organizationScroll = 0f;
            organizationNote = "";
            dirty = true;
        }

        void SetOrganizationPersonnelMode(OrganizationPersonnelMode mode)
        {
            organizationPersonnelMode = mode;
            organizationNote = mode == OrganizationPersonnelMode.None
                ? ""
                : mode == OrganizationPersonnelMode.AssignUnassigned
                    ? "showing Hoods available for assignment"
                    : "showing authoritative command parents";
            dirty = true;
        }

        void ViewSelectedLeaderPersonnel()
        {
            ViewPersonnelMember(selectedOrganizationLeaderId);
        }

        void ViewPersonnelMember(int memberId)
        {
            SelectMember(memberId);
            SetPage(LedgerPage.Personnel);
        }

        void RecruitOrganizationHood()
        {
            var result = director.RecruitHood(out _);
            organizationNote = result.Ok
                ? "one Hood recruited · available for assignment"
                : result.Reason;
            if (result.Ok)
                organizationPersonnelMode = OrganizationPersonnelMode.AssignUnassigned;
            dirty = true;
        }

        void MoveHood(int hoodId)
        {
            var leader = Leader(selectedOrganizationLeaderId);
            if (!leader.IsValid)
                return;
            var result = SubmitHoodAssignment(hoodId, leader);
            organizationNote = result.Ok ? "command transfer entered" : result.Reason;
            dirty = true;
        }

        void RemoveHoodToBoss(int hoodId)
        {
            var boss = organizationLeaders.Count > 0 ? organizationLeaders[0] : default;
            var result = boss.IsValid
                ? SubmitHoodAssignment(hoodId, boss)
                : OpResult.Fail(LedgerText.ReasonNoBoss);
            organizationNote = result.Ok
                ? "Hood returned to the unassigned Boss branch"
                : result.Reason;
            dirty = true;
        }

        OpResult SubmitHoodAssignment(int hoodId, OrganizationPerson leader)
        {
            var commands = TerritoryRuntime.Instance?.Commands;
            if (commands == null)
                return leader.Rank == Rank.Boss
                    ? director.AssignToBoss(hoodId, leader.Id)
                    : director.AssignToLieutenant(hoodId, leader.Id);

            var result = leader.Rank == Rank.Boss
                ? commands.Submit(new AssignHoodToBossCommand(
                    new TerritoryCharacterId(hoodId),
                    new TerritoryCharacterId(leader.Id)))
                : commands.Submit(new AssignHoodToLieutenantCommand(
                    new TerritoryCharacterId(hoodId),
                    new TerritoryCharacterId(leader.Id)));
            return result.Status == TerritoryCommandStatus.Succeeded
                ? OpResult.Success
                : OpResult.Fail(string.IsNullOrEmpty(result.Reason)
                    ? "the command was not completed"
                    : result.Reason);
        }

        void BeginBlockTargeting()
        {
            var leader = Leader(selectedOrganizationLeaderId);
            var map = StrategicMapHud.Instance;
            if (!leader.IsValid || map == null || TerritoryRuntime.Instance?.Commands == null)
            {
                organizationNote = "canonical map targeting is unavailable";
                dirty = true;
                return;
            }

            organizationTargetLeaderId = leader.Id;
            ClearOrganizationPendingBlock();
            organizationNote = "select one canonical block on the map";
            Close();
            map.Open();
            if (!StrategicMapHud.IsOpen)
            {
                organizationTargetLeaderId = -1;
                organizationNote = "the strategic map could not open";
                OpenAtPage(LedgerPage.Organization);
                return;
            }
            RefreshTargeting();
        }

        /// <summary>Called by the shared IMapTargetingConsumer dispatch.</summary>
        void CaptureOrganizationBlock(int legacyBlockId)
        {
            var runtime = TerritoryRuntime.Instance;
            if (!OrganizationTargetingActive || runtime == null ||
                !runtime.TryGetBlock(legacyBlockId, out var blockId))
                return;

            organizationPendingBlock = blockId;
            organizationPendingLeaderId = organizationTargetLeaderId;
            organizationPendingBlockName = BlockName(blockId);
            selectedOrganizationLeaderId = organizationTargetLeaderId;
            organizationDetailOpen = true;
            organizationTargetLeaderId = -1;
            StrategicMapHud.ClearTargeting(this);
            if (StrategicMapHud.Instance)
                StrategicMapHud.Instance.Close();
            organizationNote = "block selected · confirm responsibility below";
            organizationScroll = 0f;
            OpenAtPage(LedgerPage.Organization);
        }

        void ConfirmBlockResponsibility()
        {
            var leader = Leader(organizationPendingLeaderId);
            var runtime = TerritoryRuntime.Instance;
            if (!leader.IsValid || !organizationPendingBlock.IsValid || runtime?.Commands == null)
            {
                organizationNote = "territory command gateway unavailable";
                dirty = true;
                return;
            }

            var node = leader.Rank == Rank.Boss
                ? TerritoryCommandNodeId.Boss(leader.Id)
                : TerritoryCommandNodeId.Lieutenant(leader.Id);
            var result = runtime.Commands.Submit(new AssignBlockResponsibilityCommand(
                organizationPendingBlock,
                new TerritoryGangId(GangCatalog.PlayerGangId),
                node,
                leader.Rank == Rank.Boss ? new TerritoryCharacterId(leader.Id) : default,
                leader.Rank == Rank.Lieutenant
                    ? new TerritoryCharacterId(leader.Id)
                    : default));
            organizationNote = result.WasAccepted
                ? "organizational responsibility entered · control unchanged"
                : result.Reason;
            if (result.WasAccepted)
                ClearOrganizationPendingBlock();
            dirty = true;
        }

        void RemoveBlock(TerritoryBlockId blockId)
        {
            var runtime = TerritoryRuntime.Instance;
            var result = runtime != null
                ? runtime.RemoveBlockResponsibility(blockId, selectedOrganizationLeaderId)
                : OpResult.Fail("territory command gateway unavailable");
            organizationNote = result.Ok
                ? "responsibility removed · control unchanged"
                : result.Reason;
            dirty = true;
        }

        void CancelPendingBlock()
        {
            ClearOrganizationPendingBlock();
            organizationNote = "block assignment cancelled";
            dirty = true;
        }

        void ClearOrganizationPendingBlock()
        {
            organizationPendingBlock = default;
            organizationPendingLeaderId = -1;
            organizationPendingBlockName = "";
        }

        /// <summary>Map Esc has no callback, so the closed Ledger notices the map is gone.</summary>
        void CancelOrganizationTargetingAndReturn()
        {
            organizationTargetLeaderId = -1;
            StrategicMapHud.ClearTargeting(this);
            organizationNote = "block assignment cancelled";
            OpenAtPage(LedgerPage.Organization);
        }

        bool CloseOrganizationTransient()
        {
            if (organizationPendingBlock.IsValid)
            {
                CancelPendingBlock();
                return true;
            }
            if (organizationPersonnelMode != OrganizationPersonnelMode.None)
            {
                SetOrganizationPersonnelMode(OrganizationPersonnelMode.None);
                return true;
            }
            if (organizationDetailOpen)
            {
                BackToOrganizationOverview();
                return true;
            }
            return false;
        }

        void DismissOrganizationTransient()
        {
            organizationPersonnelMode = OrganizationPersonnelMode.None;
            ClearOrganizationPendingBlock();
        }

        int ResponsibilityLeader(IOrganizationQuery query, TerritoryBlockId blockId)
        {
            for (var i = 0; i < organizationLeaders.Count; i++)
            {
                query.CollectBlockResponsibilities(
                    organizationLeaders[i].Id, organizationResponsibilities);
                for (var b = 0; b < organizationResponsibilities.Count; b++)
                    if (organizationResponsibilities[b].BlockId == blockId)
                        return organizationResponsibilities[b].LeaderId;
            }
            return -1;
        }

        string BlockName(TerritoryBlockId blockId)
        {
            var query = TerritoryRuntime.Instance?.PlayerQuery;
            return query != null && query.TryGetBlock(blockId, out var block) && block != null
                ? block.BlockName
                : blockId.Value;
        }

        string CurrentControlLine(TerritoryBlockId blockId)
        {
            var query = TerritoryRuntime.Instance?.PlayerQuery;
            if (query == null || !query.TryGetBlock(blockId, out var block) || block == null ||
                string.IsNullOrEmpty(block.Control) || block.Control == "Unknown")
                return "CURRENT CONTROL · UNAVAILABLE";
            return "CURRENT CONTROL · " + block.Control.ToUpperInvariant();
        }

        bool TryObservedBlock(int characterId, out string blockName)
        {
            var truth = TerritoryRuntime.Instance?.DebugTruth;
            if (truth != null)
                for (var i = 0; i < truth.BlockIds.Count; i++)
                    if (truth.TryGetBlock(truth.BlockIds[i], out var block) && block != null)
                        for (var a = 0; a < block.Actors.Count; a++)
                            if (block.Actors[a].GangId.IsValid &&
                                block.Actors[a].GangId.Value == GangCatalog.PlayerGangId &&
                                block.Actors[a].CharacterId.IsValid &&
                                block.Actors[a].CharacterId.Value == characterId)
                            {
                                blockName = block.Definition.DisplayName;
                                return true;
                            }
            blockName = "";
            return false;
        }

        OrganizationPerson Leader(int id)
        {
            for (var i = 0; i < organizationLeaders.Count; i++)
                if (organizationLeaders[i].Id == id)
                    return organizationLeaders[i];
            return default;
        }

        bool ContainsLeader(int id) => Leader(id).IsValid;

        int CountUnassigned()
        {
            var count = 0;
            for (var i = 0; i < organizationPeople.Count; i++)
                if (organizationPeople[i].IsUnassigned &&
                    organizationPeople[i].IsAvailable)
                    count++;
            return count;
        }

        static string OutfitTitle(string bossName)
        {
            if (string.IsNullOrWhiteSpace(bossName))
                return "THE OUTFIT";
            var words = bossName.Split(' ');
            var surname = words.Length > 0 ? words[words.Length - 1] : "";
            return "THE " + surname.ToUpperInvariant() + " OUTFIT";
        }

        static string OverageSuffix(OrganizationCapacityView capacity)
        {
            var suffix = "";
            if (capacity.Manpower.Overage > 0)
                suffix += " · +" + capacity.Manpower.Overage + " MANPOWER";
            if (capacity.Blocks.Overage > 0)
                suffix += " · +" + capacity.Blocks.Overage + " BLOCKS";
            return suffix;
        }

        static void SetActionEnabled(TMP_Text label, bool enabled)
        {
            if (!label)
                return;
            var button = label.GetComponentInParent<Button>();
            if (button)
                button.interactable = enabled;
            if (!enabled)
                label.color = LedgerStyle.InkFaint;
        }
    }
}
