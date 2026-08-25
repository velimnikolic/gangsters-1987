using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LivingCity.Personnel;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// The PERSONNEL page: the roll on ruled ledger paper down the left, one man's
    /// index card on the right. Crews are typed groups with a blank rule between them,
    /// the lieutenant in bold heading his men; the selected man is a highlighter
    /// stroke; in assign mode every place he could go is a green stroke. The card is
    /// a stapled Polaroid, the typed particulars, a loyalty bar, eleven rows of stars,
    /// what he carries, and the tape verbs - PROMOTE, REASSIGN, DEMOTE - with any
    /// refusal written under them in red pen.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        enum Confirm
        {
            None,
            Promote,
            Demote,
        }

        const float FilterY = PageTop;
        const float FilterH = 24f;

        const float ListLeft = PageLeft;
        const float ListTop = -114f;
        const float ListWidth = 446f;

        /// <summary>One ledger rule per row; section heads and the blank line between
        /// crews are rows too, so every line of type sits on a rule.</summary>
        const float RowHeight = 34f;
        const int ListRows = 25;
        const float ListHeight = RowHeight * ListRows;
        const float HoodIndent = 24f;

        const float CardLeft = 486f;
        const float CardWidth = PageRight - CardLeft;
        const float CardPad = 16f;
        const float CardInner = CardWidth - CardPad * 2f;

        /// <summary>selectedId's sentinel for "the front is selected" - the boss's
        /// card rather than a member's. Never a real Character id (those are >= 0).</summary>
        const int FrontSelection = -2;

        RectTransform listViewport;
        RectTransform listContent;
        RectTransform cardContent;
        RectTransform hoverNote;
        TMP_Text hoverNoteText;
        GameObject sortMenu;
        TMP_Text sortTape, rankTape, postTape, showTape;

        ViewOptions options;
        int selectedId = -1;
        bool assignMode;
        Confirm pendingConfirm;
        string lastRefusal = "";
        float listScroll;

        readonly List<LedgerRow> rows = new List<LedgerRow>();

        void BuildPersonnelPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Personnel);

            // The filter tapes across the top: SORT opens the little menu, the other
            // three cycle their setting on click.
            sortTape = Tape(root, "", ListLeft, FilterY, 232f, FilterH, ToggleSortMenu);
            rankTape = Tape(root, "", ListLeft + 240f, FilterY, 176f, FilterH, CycleRank);
            postTape = Tape(root, "", ListLeft + 424f, FilterY, 176f, FilterH, CyclePost);
            showTape = Tape(root, "", ListLeft + 608f, FilterY, PageRight - ListLeft - 608f,
                FilterH, CycleShow);

            // The ruled ledger under the roll - fixed, the rows scroll over it a whole
            // rule at a time so type never straddles a line.
            RuledPaper(root, ListLeft, ListTop, ListWidth, ListHeight, RowHeight, 6f);

            listViewport = NewRect("Roll", root);
            PlaceTopLeft(listViewport, ListLeft, ListTop, ListWidth, ListHeight);
            listViewport.gameObject.AddComponent<RectMask2D>();

            listContent = NewRect("Rows", listViewport);
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0f, 1f);
            listContent.anchoredPosition = Vector2.zero;
            listContent.sizeDelta = new Vector2(0f, ListHeight);

            // The index card, laid a touch askew the way a card in a folder lies.
            var card = Card("Card", root, CardLeft, ListTop, CardWidth, ListHeight,
                LedgerStyle.Card);
            cardContent = NewRect("CardContent", card);
            Stretch(cardContent, CardPad);
            // The content clips to the card; the sticky note beside it does not.
            cardContent.gameObject.AddComponent<RectMask2D>();

            // The one shared hover note - a sticky note, child of the CARD, not the
            // content (which rebuilds under the pointer), raised to last sibling on
            // every show so it prints over whatever it covers.
            hoverNote = StickyNote(card, CardInner - 40f, 60f);
            hoverNoteText = Text("Text", hoverNote, LedgerStyle.Type, 14f, LedgerStyle.Ink,
                TextAlignmentOptions.TopLeft);
            Stretch(hoverNoteText.rectTransform, 10f);
            hoverNoteText.textWrappingMode = TextWrappingModes.Normal;
            hoverNote.gameObject.SetActive(false);

            BuildSortMenu(root);
        }

        // ---------------------------------------------------------- filter tapes

        void ToggleSortMenu()
        {
            if (sortMenu)
                sortMenu.SetActive(!sortMenu.activeSelf);
        }

        void CycleRank()
        {
            options.Rank = (RankFilter)(((int)options.Rank + 1) % 4);
            dirty = true;
        }

        void CyclePost()
        {
            options.Assignment = (AssignmentFilter)(((int)options.Assignment + 1) % 4);
            dirty = true;
        }

        void CycleShow()
        {
            options.Availability = (AvailabilityFilter)(((int)options.Availability + 1) % 3);
            dirty = true;
        }

        void RefreshFilterTapes()
        {
            if (!sortTape)
                return;

            sortTape.text = options.Sort switch
            {
                SortKey.Attribute => "SORT: " +
                    LedgerText.AttributeLabel(options.SortAttribute).ToUpperInvariant(),
                SortKey.Loyalty => "SORT: LOYALTY",
                _ => "SORT: ROSTER ORDER",
            };
            rankTape.text = options.Rank switch
            {
                RankFilter.Hoods => "RANK: HOODS",
                RankFilter.Lieutenants => "RANK: LIEUTENANTS",
                RankFilter.Specialists => "RANK: SPECIALISTS",
                _ => "RANK: ALL",
            };
            postTape.text = options.Assignment switch
            {
                AssignmentFilter.Crews => "POST: CREWS",
                AssignmentFilter.Pool => "POST: POOL",
                AssignmentFilter.Front => "POST: FRONT",
                _ => "POST: ALL",
            };
            showTape.text = options.Availability switch
            {
                AvailabilityFilter.ActiveOnly => "SHOW: ACTIVE",
                AvailabilityFilter.Unavailable => "SHOW: OUT OF ACTION",
                _ => "SHOW: ALL",
            };
        }

        /// <summary>A slip of card that drops from the SORT tape: thirteen typed
        /// entries, the current one highlighted. Built once; it toggles rather than
        /// rebuilds because its contents never change. Built LAST under the page so
        /// hierarchy order draws it over the roll.</summary>
        void BuildSortMenu(RectTransform root)
        {
            const float rowH = 24f;
            var entries = 2 + AttributeScale.Count;

            var slip = Card("SortMenu", root, ListLeft, FilterY - FilterH - 4f, 260f,
                entries * rowH + 12f, LedgerStyle.Card);
            sortMenu = slip.gameObject;
            // The slip's own body must swallow stray clicks.
            PaperOf(slip).raycastTarget = true;

            for (var i = 0; i < entries; i++)
            {
                var index = i;
                string label;
                if (i == 0)
                    label = "ROSTER ORDER";
                else if (i <= AttributeScale.Count)
                    label = LedgerText.AttributeLabel((CharacterAttribute)(i - 1))
                        .ToUpperInvariant();
                else
                    label = "LOYALTY";

                var row = NewRect("Entry", slip);
                PlaceTopLeft(row, 6f, -6f - i * rowH, 248f, rowH);
                var surface = ClickSurface(row);
                RowButton(row, surface, () =>
                {
                    if (index == 0)
                        options.Sort = SortKey.Roster;
                    else if (index <= AttributeScale.Count)
                    {
                        options.Sort = SortKey.Attribute;
                        options.SortAttribute = (CharacterAttribute)(index - 1);
                    }
                    else
                        options.Sort = SortKey.Loyalty;

                    sortMenu.SetActive(false);
                    dirty = true;
                });

                var text = Text("Label", row, LedgerStyle.Type, 14.5f, LedgerStyle.Ink,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(text.rectTransform, 12f, 230f);
                text.text = label;
            }

            sortMenu.SetActive(false);
        }

        // ------------------------------------------------------------------ the roll

        void RebuildList()
        {
            foreach (Transform old in listContent)
                Destroy(old.gameObject);

            var roster = director.Roster;
            if (roster == null)
                return;

            // Assign mode reads the whole book: filters fall away (a valid target must
            // never be hidden by one) and the empty sections show as targets.
            var effective = options;
            if (assignMode)
            {
                effective.Rank = RankFilter.All;
                effective.Assignment = AssignmentFilter.All;
                effective.Availability = AvailabilityFilter.All;
                effective.IncludeEmptySections = true;
            }

            RosterView.Build(roster, effective, rows);

            var y = 0f;
            var indented = false;
            var first = true;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                switch (row.Kind)
                {
                    case RowKind.CrewHeader:
                        // The crew band is gone at the user's word - the lieutenant's
                        // row is the crew's handle. A blank rule separates crews.
                        indented = true;
                        if (!first)
                            y -= RowHeight;
                        break;

                    case RowKind.FrontHeader:
                    case RowKind.PoolHeader:
                    case RowKind.SpecialistHeader:
                        indented = true;
                        if (!first)
                            y -= RowHeight;
                        BuildSectionHeader(row.Kind, y);
                        y -= RowHeight;
                        break;

                    case RowKind.Lieutenant:
                        BuildCharacterRow(roster, row.CharacterId, y, indent: false,
                            lieutenantRow: true);
                        y -= RowHeight;
                        break;

                    default:
                        BuildCharacterRow(roster, row.CharacterId, y, indented);
                        y -= RowHeight;
                        break;
                }
                first = false;
            }

            listContent.sizeDelta = new Vector2(0f, Mathf.Max(ListHeight, -y));
            var maxScroll = Mathf.Max(0f, listContent.sizeDelta.y - ListHeight);
            listScroll = Mathf.Clamp(listScroll, 0f, maxScroll);
            listScroll = Mathf.Floor(listScroll / RowHeight) * RowHeight;
            listContent.anchoredPosition = new Vector2(0f, listScroll);
        }

        void BuildSectionHeader(RowKind kind, float y)
        {
            var rect = NewRect("Section", listContent);
            PlaceTopLeft(rect, 0f, y, ListWidth, RowHeight);

            var isTarget = assignMode && selectedId >= 0 && kind != RowKind.SpecialistHeader;
            // The front's header is also the BOSS's row: clicking it opens the front
            // card - his face, the desk, the locker - the way a member row opens his.
            var frontSelectable = kind == RowKind.FrontHeader && !assignMode;
            var chosen = frontSelectable && selectedId == FrontSelection;

            if (isTarget || frontSelectable)
            {
                var surface = ClickSurface(rect);
                if (isTarget)
                {
                    var toPool = kind == RowKind.PoolHeader;
                    RowButton(rect, surface, () => FinishAssign(toPool
                        ? director.AssignToPool(selectedId)
                        : director.AssignToFront(selectedId)));
                }
                else
                    RowButton(rect, surface, () => SelectMember(FrontSelection));
            }

            if (isTarget)
                Highlight(rect, LedgerStyle.HighlighterGreen);
            else if (chosen)
                Highlight(rect, LedgerStyle.Highlighter);

            var label = Text("Label", rect, LedgerStyle.Type, 14f, LedgerStyle.Ink,
                TextAlignmentOptions.MidlineLeft);
            FillRow(label.rectTransform, 12f, 400f);
            label.characterSpacing = 2f;
            label.text = kind switch
            {
                RowKind.FrontHeader => "THE FRONT",
                RowKind.PoolHeader => "THE POOL",
                _ => "SPECIALISTS",
            };
        }

        void BuildCharacterRow(Roster roster, int id, float y, bool indent,
            bool lieutenantRow = false)
        {
            var member = roster.Find(id);
            if (member == null)
                return;

            var rect = NewRect("Row", listContent);
            PlaceTopLeft(rect, 0f, y, ListWidth, RowHeight);

            var chosen = id == selectedId && !assignMode;
            // A lieutenant's row is his crew's handle: in assign mode it lights as
            // the drop target the crew band used to be.
            var isCrewTarget = assignMode && lieutenantRow && selectedId >= 0;
            var surface = ClickSurface(rect);

            // In assign mode the LIEUTENANT'S row takes the man into his crew - the
            // lieutenant IS his group's handle. An ordinary man is no target, so
            // clicking one cancels the mode - the "never mind" that costs nothing.
            var crew = lieutenantRow ? roster.CrewOf(id) : null;
            var crewId = crew != null ? crew.Id : -1;
            RowButton(rect, surface, () =>
            {
                if (assignMode)
                {
                    if (crewId >= 0)
                        FinishAssign(director.AssignToCrew(selectedId, crewId));
                    else
                    {
                        assignMode = false;
                        dirty = true;
                    }
                }
                else
                    SelectMember(id);
            });

            if (isCrewTarget)
                Highlight(rect, LedgerStyle.HighlighterGreen);
            else if (chosen)
                Highlight(rect, LedgerStyle.Highlighter);

            var x = 12f + (indent ? HoodIndent : 0f);
            var dead = member.Gone; // struck through: dead or deserted
            var ink = dead ? LedgerStyle.InkDim : LedgerStyle.Ink;

            var name = Text("Name", rect, lieutenantRow ? LedgerStyle.MonoBold : LedgerStyle.Mono,
                14.5f, ink, TextAlignmentOptions.MidlineLeft);
            FillRow(name.rectTransform, x, GunColumn - 4f - x);
            name.text = member.FullName;

            BuildGunCell(roster, rect, member, dead);
            BuildRowCells(rect, member, 236f, dead);

            // The dead are struck through in pen - the record keeps their line.
            if (dead)
                Rule(rect, x - 2f, -RowHeight * 0.5f + 1f, 220f - x, LedgerStyle.RedPen, 1.5f);
        }

        /// <summary>Where the gun sits on a row - between the name and the rank tag.</summary>
        const float GunColumn = 198f;
        const float GunWidth = 34f, GunHeight = 16f;

        /// <summary>The body of the gun a man carries: the first firearm on his line in
        /// the armory, else the .38 every man of the outfit carries in his coat - the
        /// default that is nobody's stock and never on the counter.</summary>
        static GameObject SidearmModel(Roster roster, Character member)
        {
            var carried = new List<RosterEquipment>();
            roster.HeldBy(member.Id, carried);
            foreach (var item in carried)
            {
                // Wheels of any sort are not the gun in his coat - the motorcycle
                // joined the stock book after this line was written, and a man was
                // very nearly drawn holding one (RosterOps.IsWeapon is the one test).
                if (!RosterOps.IsWeapon(item.Kind))
                    continue;
                string modelName = null;
                foreach (var listing in Outfit.ArmoryCatalog.Weapons)
                    if (listing.DisplayName == item.DisplayName) { modelName = listing.ModelName; break; }
                var model = LedgerModelSet.WeaponModelFor(item.Kind, modelName);
                if (model)
                    return model;
            }
            return LedgerModelSet.WeaponModelFor(EquipmentKind.Pistol, DefaultSidearmModel);
        }

        /// <summary>The revolver every man carries by default - Gang Warfare's .38.</summary>
        const string DefaultSidearmModel = "SM_Wep_Pistol_Revolver_01";

        /// <summary>A small newsprint of the man's gun beside his name - the armory
        /// page's photograph, cut to the barrel band, so the roll reads who carries
        /// what at a glance. Specialists carry nothing and show nothing.</summary>
        void BuildGunCell(Roster roster, RectTransform rect, Character member, bool dead)
        {
            if (member.Specialty != Specialty.None)
                return;
            var model = SidearmModel(roster, member);
            if (!model)
                return;

            var cut = NewRect("Gun", rect);
            cut.sizeDelta = new Vector2(GunWidth, GunHeight);
            cut.anchorMin = new Vector2(0f, 0.5f);
            cut.anchorMax = new Vector2(0f, 0.5f);
            cut.pivot = new Vector2(0f, 0.5f);
            cut.anchoredPosition = new Vector2(GunColumn, 0f);
            var raw = cut.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false;
            raw.uvRect = new Rect(0f, 0.25f, 1f, 0.5f);
            raw.color = dead ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            PortraitStudio.Request(model, PortraitStudio.Framing.Item, raw,
                PortraitStudio.Treatment.Newsprint);
        }

        /// <summary>The scan columns every man's row shares: rank tag, status stamp,
        /// WANTED, and - under an attribute or loyalty sort - the sorted value itself,
        /// right-aligned where a column of numbers can be read straight down. That
        /// column is what makes sixty men scannable.</summary>
        void BuildRowCells(RectTransform rect, Character member, float x, bool dead)
        {
            var rank = Text("Rank", rect, LedgerStyle.Type, 11.5f,
                dead ? LedgerStyle.InkFaint : LedgerStyle.InkDim,
                TextAlignmentOptions.MidlineLeft);
            FillRow(rank.rectTransform, x, 84f);
            rank.characterSpacing = 1f;
            rank.text = (member.Specialty != Specialty.None
                ? LedgerText.SpecialtyLabel(member.Specialty)
                : LedgerText.RankLabel(member.Rank)).ToUpperInvariant();

            if (member.Status != CharacterStatus.Active)
            {
                var status = Text("Status", rect, LedgerStyle.Condensed, 11f,
                    LedgerStyle.RedPen, TextAlignmentOptions.MidlineLeft);
                FillRow(status.rectTransform, x + 88f, 74f);
                status.characterSpacing = 2f;
                status.text = LedgerText.StatusLabel(member.Status).ToUpperInvariant();
            }

            if (member.Wanted)
            {
                var wanted = Text("Wanted", rect, LedgerStyle.Condensed, 11f,
                    LedgerStyle.RedPen, TextAlignmentOptions.MidlineLeft);
                FillRow(wanted.rectTransform, x + 150f, 60f);
                wanted.characterSpacing = 2f;
                wanted.text = "WANTED";
            }

            if (options.Sort != SortKey.Roster && !assignMode)
            {
                var value = Text("Value", rect, LedgerStyle.MonoBold, 14f, LedgerStyle.Ink,
                    TextAlignmentOptions.MidlineRight);
                FillRow(value.rectTransform, ListWidth - 62f, 52f);
                value.text = options.Sort == SortKey.Loyalty
                    ? member.Loyalty.ToString()
                    : LedgerText.Stars(member.GetHalfSteps(options.SortAttribute));
            }
        }

        void SelectMember(int id)
        {
            selectedId = id;
            pendingConfirm = Confirm.None;
            lastRefusal = "";
            dirty = true;
        }

        void FinishAssign(OpResult result)
        {
            assignMode = false;
            lastRefusal = result.Ok ? "" : result.Reason;
            dirty = true;
        }

        // ------------------------------------------------------------------ the card

        /// <summary>The face this member wears in his photograph and on the street - the
        /// same man in both, and never a body one of his own crewmates is wearing. The
        /// rule and the approved stock live together in Gangs.GangLooks; the book only
        /// asks it who this man is, and the roster it asks against is the director's.
        /// </summary>
        public static GameObject MemberModel(Character member) =>
            PortraitStudio.FindPeoplePrefab(Gangs.GangLooks.LookFor(member,
                Gameplay.PersonnelDirector.Instance != null
                    ? Gameplay.PersonnelDirector.Instance.Roster : null));

        /// <summary>First letters of the first and last word of a name - "Don Salvatore
        /// Ricci" prints DR in the slot until his photograph arrives.</summary>
        static string InitialsOf(string fullName)
        {
            var parts = fullName.Split(' ');
            var head = parts.Length > 0 && parts[0].Length > 0
                ? parts[0][0].ToString() : "";
            var tail = parts.Length > 1 && parts[parts.Length - 1].Length > 0
                ? parts[parts.Length - 1][0].ToString() : "";
            return head + tail;
        }

        /// <summary>Prints the sticky note just under the hovered row, sized to its
        /// copy. Row coordinates are the card content's, inset by CardPad from the
        /// card the note itself hangs on.</summary>
        void ShowHoverNote(string note, RectTransform row)
        {
            if (note.Length == 0 || hoverNote == null)
                return;

            hoverNoteText.text = note;
            hoverNote.gameObject.SetActive(true);
            hoverNote.SetAsLastSibling();

            var width = CardInner - 40f;
            var height = hoverNoteText.GetPreferredValues(note, width - 20f, 0f).y + 20f;
            hoverNote.sizeDelta = new Vector2(width, height);
            hoverNote.anchoredPosition = new Vector2(CardPad + 20f,
                row.anchoredPosition.y - row.sizeDelta.y - CardPad - 2f);
        }

        void HideHoverNote()
        {
            if (hoverNote != null)
                hoverNote.gameObject.SetActive(false);
        }

        /// <summary>The pointer half of the card's hover notes: an invisible zone laid
        /// over one stat row. AddComponent-only, never serialized.</summary>
        sealed class StatHoverZone : MonoBehaviour, IPointerEnterHandler,
            IPointerExitHandler
        {
            public PersonnelAlmanac almanac;
            public string note;

            public void OnPointerEnter(PointerEventData eventData) =>
                almanac.ShowHoverNote(note, (RectTransform)transform);

            public void OnPointerExit(PointerEventData eventData) =>
                almanac.HideHoverNote();
        }

        void RebuildDetail()
        {
            // The rows under the pointer are about to be destroyed, and destroyed
            // rows send no PointerExit - drop the note with them.
            HideHoverNote();

            foreach (Transform old in cardContent)
                Destroy(old.gameObject);

            var roster = director.Roster;
            if (roster != null && selectedId == FrontSelection)
            {
                BuildFrontDetail(roster);
                return;
            }

            var member = roster != null && selectedId >= 0 ? roster.Find(selectedId) : null;
            if (member == null)
            {
                var hint = Text("Hint", cardContent, LedgerStyle.Type, 14f, LedgerStyle.InkDim,
                    TextAlignmentOptions.Center);
                Stretch(hint.rectTransform);
                hint.text = "- pick a man off the roll -";
                return;
            }

            // The Polaroid, stapled top-left and a little crooked.
            var raw = Polaroid(cardContent, 4f, -6f, 84f,
                InitialsOf(member.FullName), -3.5f, out _);
            PortraitStudio.Request(MemberModel(member), PortraitStudio.Framing.Bust, raw);

            var name = Line(cardContent, LedgerStyle.Type, 18f, LedgerStyle.Ink, 118f, -10f,
                CardInner - 118f, 30f, member.FullName);

            var rankLine = member.Specialty != Specialty.None
                ? LedgerText.SpecialtyLabel(member.Specialty)
                : LedgerText.RankLabel(member.Rank);
            var assignment = roster.AssignmentOf(member.Id);
            var crewName = "";
            if (assignment.Kind == AssignmentKind.Crew)
            {
                var crew = roster.FindCrew(assignment.CrewId);
                var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
                crewName = lieutenant != null
                    ? LedgerText.CrewName(lieutenant.Surname)
                    : "A crew";
            }

            Line(cardContent, LedgerStyle.Mono, 14.5f, LedgerStyle.InkDim, 118f, -42f,
                CardInner - 118f, 20f,
                rankLine + "  ·  " + LedgerText.AssignmentLine(assignment, crewName));

            var active = member.Status == CharacterStatus.Active;
            Line(cardContent, LedgerStyle.Mono, 14.5f,
                active ? LedgerStyle.InkDim : LedgerStyle.RedPen, 118f, -64f,
                CardInner - 118f, 20f,
                "Status: " + LedgerText.StatusLabel(member.Status));
            Rule(cardContent, 118f, -90f, CardInner - 118f, LedgerStyle.InkFaint);

            // The stamps: the law's word over the photograph, WANTED beside the name.
            if (!active)
                Stamp(cardContent, member.Status switch
                {
                    CharacterStatus.Dead => "DECEASED",
                    CharacterStatus.Deserted => "DESERTED",
                    CharacterStatus.Jailed => "IN CUSTODY",
                    _ => "IN HOSPITAL",
                }, 0f, -56f, 124f, 30f, tilt: -14f, size: 15f);
            if (member.Wanted)
                Stamp(cardContent, "WANTED", CardInner - 92f, -4f, 88f, 26f, tilt: 8f,
                    size: 14f);

            var y = -128f;

            y = BuildLoyaltyBar(member, y);
            y -= 8f;

            for (var a = 0; a < AttributeScale.Count; a++)
                y = BuildAttributeRow(member, (CharacterAttribute)a, y);
            y -= 8f;

            y = BuildEquipmentSection(roster, member, y);
            y -= 12f;

            BuildActionStrip(roster, member, y);
        }

        /// <summary>The front's card - the BOSS's card: his face and name up top, then
        /// the desk, the guards, what sits at the front, and the stock with GIVE
        /// straight into the headquarters locker.</summary>
        void BuildFrontDetail(Roster roster)
        {
            var bossName = Gangs.GangCatalog.BossName;
            var raw = Polaroid(cardContent, 4f, -6f, 84f, InitialsOf(bossName), -3.5f, out _);
            PortraitStudio.Request(
                PortraitStudio.FindPeoplePrefab(Gangs.GangCatalog.BossModel),
                PortraitStudio.Framing.Bust, raw);

            Line(cardContent, LedgerStyle.Type, 18f, LedgerStyle.Ink, 118f, -10f,
                CardInner - 118f, 30f, bossName);
            Line(cardContent, LedgerStyle.Mono, 14.5f, LedgerStyle.InkDim, 118f, -42f,
                CardInner - 118f, 20f,
                "Boss  ·  " + Gangs.GangCatalog.Names[Gangs.GangCatalog.PlayerGangId]);
            Rule(cardContent, 118f, -90f, CardInner - 118f, LedgerStyle.InkFaint);

            var y = -128f;

            // The desk and its guard.
            var manager = roster.Find(roster.FrontId);
            y = CardHeading("The desk", y);
            y = DetailLine(manager != null
                    ? manager.FullName + " runs the desk."
                    : "Nobody runs the desk.",
                manager != null ? LedgerStyle.Ink : LedgerStyle.RedPen, y);

            var guards = 0;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Status == CharacterStatus.Active &&
                    member.Id != roster.FrontId &&
                    roster.AssignmentOf(member.Id).Kind == AssignmentKind.Pool)
                    guards++;
            }
            y = DetailLine(guards == 1
                    ? "1 hood on guard at the front."
                    : guards + " hoods on guard at the front.",
                guards > 0 ? LedgerStyle.Ink : LedgerStyle.InkDim, y);

            // What the front holds - the locker and the guards' hands.
            y = CardHeading("At the front", y - 6f);
            var anyHeld = false;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.OwnerId != RosterEquipment.FrontArmory)
                    continue;
                var holder = roster.Find(item.HolderId);
                anyHeld = true;

                y = DetailLine(LedgerText.EquipmentLine(item.Kind, item.DisplayName) +
                    "  —  " +
                    (holder != null ? LedgerText.HeldByLine(holder.FullName)
                        : "in the locker"),
                    LedgerStyle.Ink, y + 2f);
                var itemId = item.Id;
                Tape(cardContent, "RETURN", CardInner - 96f, y + 23f, 88f, 20f, () =>
                {
                    lastRefusal = "";
                    var result = director.ReturnEquipment(itemId);
                    if (!result.Ok)
                        lastRefusal = result.Reason;
                    dirty = true;
                }, size: 11f);
            }
            if (!anyHeld)
                y = DetailLine("The locker is empty.", LedgerStyle.InkDim, y);

            // The stock: GIVE dumps gear at the front, the guards draw it at once.
            y = CardHeading("Armory", y - 6f);
            var anyStock = false;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.OwnerId != RosterEquipment.Unheld)
                    continue;
                anyStock = true;

                y = DetailLine(LedgerText.EquipmentLine(item.Kind, item.DisplayName),
                    LedgerStyle.Ink, y + 2f);
                var itemId = item.Id;
                Tape(cardContent, "GIVE", CardInner - 96f, y + 23f, 88f, 20f, () =>
                {
                    lastRefusal = "";
                    var result = director.GiveEquipmentToFront(itemId);
                    if (!result.Ok)
                        lastRefusal = result.Reason;
                    dirty = true;
                }, size: 11f);
            }
            if (!anyStock)
                y = DetailLine("The stock is empty.", LedgerStyle.InkDim, y);

            if (lastRefusal.Length > 0)
                DetailLine(lastRefusal, LedgerStyle.RedPen, y - 4f, LedgerStyle.MonoItalic);
        }

        /// <summary>A typed sub-heading on the card, underlined.</summary>
        float CardHeading(string label, float y)
        {
            var text = Line(cardContent, LedgerStyle.Type, 14f, LedgerStyle.Ink, 4f, y,
                CardInner - 8f, 20f, label.ToUpperInvariant());
            text.characterSpacing = 3f;
            Rule(cardContent, 4f, y - 20f, CardInner - 8f, LedgerStyle.InkFaint);
            return y - 26f;
        }

        float DetailLine(string text, Color color, float y, TMP_FontAsset font = null)
        {
            Line(cardContent, font ? font : LedgerStyle.Mono, 14.5f, color, 4f, y,
                CardInner - 8f, 22f, text);
            return y - 23f;
        }

        float BuildLoyaltyBar(Character member, float y)
        {
            Line(cardContent, LedgerStyle.Type, 14.5f, LedgerStyle.Ink, 4f, y, 110f, 20f,
                "Loyalty");
            Bar(cardContent, 130f, y - 4f, 150f, 12f, member.Loyalty / 100f,
                member.Loyalty < 35 ? LedgerStyle.RedPen : LedgerStyle.Ink);
            Line(cardContent, LedgerStyle.MonoBold, 14f, LedgerStyle.Ink, CardInner - 64f, y,
                60f, 20f, member.Loyalty.ToString(), TextAlignmentOptions.MidlineRight);
            return y - 26f;
        }

        float BuildAttributeRow(Character member, CharacterAttribute attribute, float y)
        {
            Line(cardContent, LedgerStyle.Mono, 14.5f, LedgerStyle.Ink, 4f, y, 150f, 22f,
                LedgerText.AttributeLabel(attribute));

            var halfSteps = member.GetHalfSteps(attribute);
            Stars(cardContent, 158f, y - 11f, halfSteps);

            Line(cardContent, LedgerStyle.Mono, 14f, LedgerStyle.InkDim, CardInner - 64f, y,
                60f, 22f, LedgerText.Stars(halfSteps), TextAlignmentOptions.MidlineRight);

            // The whole line is a hover zone: rest the pointer on a stat and the
            // sticky note under it says what the number is FOR.
            var zone = NewRect("Hover", cardContent);
            PlaceTopLeft(zone, 0f, y, CardInner, 24f);
            ClickSurface(zone);
            var hover = zone.gameObject.AddComponent<StatHoverZone>();
            hover.almanac = this;
            hover.note = LedgerText.AttributeNote(attribute);

            return y - 24f;
        }

        /// <summary>The gear half of a card - and a LIEUTENANT's card only. Gear issues
        /// to the head of a crew and is read back off the same card, so on a hood's or a
        /// specialist's card both listings are somebody else's book: what he carries is
        /// already on his roster row, in the gun drawn beside his name.</summary>
        float BuildEquipmentSection(Roster roster, Character member, float y)
        {
            if (member.Rank != Rank.Lieutenant)
                return y;

            y = BuildCrewDeck(roster, member, y);
            return BuildArmoryStock(roster, member, y - 6f);
        }

        /// <summary>What his crew owns. Read-only by design: gear dealt to a crew stays
        /// with it, so each line carries the word ASSIGNED where a verb would be.</summary>
        float BuildCrewDeck(Roster roster, Character member, float y)
        {
            y = CardHeading("In hand", y);

            // His crew's DECK, not his own two hands: every item the crew owns, wherever
            // the deal has put it. NormalizeArms moves holders and never owners, so a
            // piece riding on one of his hoods still reads on his card - and this is now
            // the only card it appears on.
            var anyHeld = false;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.OwnerId != member.Id)
                    continue;
                anyHeld = true;

                var holder = roster.Find(item.HolderId);
                y = DetailLine(LedgerText.EquipmentLine(item.Kind, item.DisplayName) +
                    (holder != null && holder.Id != member.Id
                        ? "  —  " + LedgerText.HeldByLine(holder.FullName)
                        : ""),
                    LedgerStyle.Ink, y + 2f);

                // Dealt gear is dealt: no tape here, because there is no taking it back
                // off a crew. The word is a state the boss reads, not a verb he presses.
                var mark = Line(cardContent, LedgerStyle.Type, 11.5f, LedgerStyle.InkDim,
                    CardInner - 100f, y + 23f, 92f, 20f, "ASSIGNED",
                    TextAlignmentOptions.MidlineRight);
                mark.characterSpacing = 2f;
            }
            if (!anyHeld)
                y = DetailLine("Nothing signed out.", LedgerStyle.InkDim, y);

            return y;
        }

        /// <summary>The rest of the outfit's gear as he sees it: what is in the safe,
        /// with GIVE, and what another group already holds, muted and untakeable.</summary>
        float BuildArmoryStock(Roster roster, Character member, float y)
        {
            y = CardHeading("Armory", y);

            var anyStock = false;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                // What his crew already owns is the IN HAND listing above, not stock.
                if (item.OwnerId == member.Id)
                    continue;
                anyStock = true;

                if (item.OwnerId == RosterEquipment.Unheld)
                {
                    y = DetailLine(LedgerText.EquipmentLine(item.Kind, item.DisplayName),
                        LedgerStyle.Ink, y + 2f);
                    if (!member.Gone)
                        Tape(cardContent, "GIVE", CardInner - 96f, y + 23f, 88f, 20f, () =>
                        {
                            lastRefusal = "";
                            var result = director.GiveEquipment(item.Id, member.Id);
                            if (!result.Ok)
                                lastRefusal = result.Reason;
                            dirty = true;
                        }, size: 11f);
                }
                else
                {
                    // The finite pool made visible: an item another group owns shows
                    // here, muted and with no tape. Nothing on this card can take it -
                    // a crew's gear stays with the crew, and only the front's locker
                    // gives anything back to the safe.
                    var holder = roster.Find(item.HolderId);
                    y = DetailLine(LedgerText.EquipmentLine(item.Kind, item.DisplayName) +
                        "  —  " +
                        LedgerText.HeldByLine(holder != null ? holder.FullName
                            : item.OwnerId == RosterEquipment.FrontArmory
                                ? "the front" : "?"),
                        LedgerStyle.InkDim, y + 2f);
                }
            }

            if (!anyStock)
                y = DetailLine("The stock is empty.", LedgerStyle.InkDim, y);

            return y;
        }

        /// <summary>
        /// The card's verbs, or - because there is no dialog system and never has been -
        /// the inline confirm that replaces them: the warning in red pen plus PROMOTE
        /// ANYWAY / CANCEL tapes in the same space the PROMOTE tape occupied.
        /// </summary>
        void BuildActionStrip(Roster roster, Character member, float y)
        {
            if (lastRefusal.Length > 0)
                y = DetailLine(lastRefusal, LedgerStyle.RedPen, y, LedgerStyle.MonoItalic);

            if (member.Gone || member.Specialty != Specialty.None)
                return;

            if (pendingConfirm == Confirm.Promote)
            {
                Paragraph(cardContent, LedgerStyle.MonoItalic, 14f, LedgerStyle.RedPen, 4f, y,
                    CardInner - 8f, 44f, LedgerText.PromoteWarning(member.FullName));
                y -= 48f;
                Tape(cardContent, "PROMOTE ANYWAY", 4f, y, 160f, 26f,
                    () => DoPromote(member.Id), red: true);
                Tape(cardContent, "CANCEL", 172f, y, 96f, 26f, () =>
                {
                    pendingConfirm = Confirm.None;
                    dirty = true;
                });
                return;
            }

            if (pendingConfirm == Confirm.Demote)
            {
                var crew = roster.CrewOf(member.Id);
                Paragraph(cardContent, LedgerStyle.MonoItalic, 14f, LedgerStyle.RedPen, 4f, y,
                    CardInner - 8f, 44f, LedgerText.DemoteConfirm(member.FirstName,
                        crew != null ? crew.HoodIds.Count : 0));
                y -= 48f;
                Tape(cardContent, "DISBAND", 4f, y, 120f, 26f, () =>
                {
                    pendingConfirm = Confirm.None;
                    var result = director.Demote(member.Id);
                    lastRefusal = result.Ok ? "" : result.Reason;
                    dirty = true;
                }, red: true);
                Tape(cardContent, "CANCEL", 132f, y, 96f, 26f, () =>
                {
                    pendingConfirm = Confirm.None;
                    dirty = true;
                });
                return;
            }

            if (assignMode)
            {
                y = DetailLine("Pick a crew, the pool, or the front.", LedgerStyle.Ink, y);
                Tape(cardContent, "CANCEL", 4f, y, 96f, 26f, () =>
                {
                    assignMode = false;
                    dirty = true;
                });
                return;
            }

            if (member.Rank == Rank.Lieutenant)
            {
                Tape(cardContent, "DEMOTE", 4f, y, 110f, 26f, () =>
                {
                    pendingConfirm = Confirm.Demote;
                    dirty = true;
                }, red: true);
                return;
            }

            Tape(cardContent, "PROMOTE", 4f, y, 120f, 26f, () =>
            {
                var check = director.CheckPromote(member.Id);
                if (!check.CanPromote)
                    lastRefusal = check.Reason;
                else if (check.LowStatWarning)
                    pendingConfirm = Confirm.Promote;
                else
                    DoPromote(member.Id);
                dirty = true;
            });
            Tape(cardContent, "REASSIGN", 132f, y, 120f, 26f, () =>
            {
                assignMode = true;
                lastRefusal = "";
                dirty = true;
            });
        }

        void DoPromote(int id)
        {
            pendingConfirm = Confirm.None;
            var result = director.Promote(id, out _);
            lastRefusal = result.Ok ? "" : result.Reason;
            dirty = true;
        }
    }
}
