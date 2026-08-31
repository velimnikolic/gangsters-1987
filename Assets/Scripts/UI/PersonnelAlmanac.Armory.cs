using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Personnel;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// ARMORY: the supplier's mail-order catalogue - a grid of plates, each with its
    /// catalogue code, a line of the copywriter's prose, what it does in stepped marks
    /// and an ORDER button - and under it the stock book on its pink carbon copy: what
    /// the outfit owns and who signed it out. RECALL takes a piece back off the front;
    /// GIVE's second step turns the carbon into the list of lieutenants, because all
    /// gear issues through a crew's head.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        // ---- the counter ----

        const float ShelfHeadH = 100f;
        static float CatalogueTop = PageTop - ShelfHeadH;

        /// <summary>The board is one row of plates deep, because every shelf the
        /// catalogue stocks is five listings or fewer and five fit across the sheet. It
        /// is still a WINDOW rather than a fixed list - a sixth listing on any shelf
        /// scrolls, and CatalogueEdges says so out loud - but a second row's worth of
        /// blank board under a full shelf is a hole in the page, so the stock book gets
        /// that space instead.</summary>
        const float CatalogueHeight = CardH + 8f;

        const float CatalogueGap = 16f;
        const float CardMin = 250f;
        static int CatalogueColumns = 5;
        static float CardW = (PageWidth - CatalogueGap * (CatalogueColumns - 1))
                             / CatalogueColumns;
        const float CardH = 250f;

        // ---- the stock book ----

        const float StockPitch = 26f;
        static float StockWidth = PageWidth;
        static float StockTop = CatalogueTop - CatalogueHeight - 16f;
        static float StockHeight = -(PageBottom - StockTop);
        const float StockPad = 18f;
        static float StockInner = StockWidth - StockPad * 2f;

        // The carbon's column grid, in stock-inner coordinates.
        const float StockKindX = 0f;
        const float StockItemX = 110f;
        const float StockHolderX = 500f;
        static float StockActionX = StockInner - 110f;

        /// <summary>The counter and the stock book both take the width the window
        /// gives them. The board follows the design's auto-fill at a 250-unit minimum,
        /// so a wider window earns a sixth plate on the shelf rather than five wide
        /// ones - and the stock book takes whatever is left down to the sheet's foot.
        /// </summary>
        static void MeasureArmoryLayout()
        {
            CatalogueTop = PageTop - ShelfHeadH;
            CatalogueColumns = Mathf.Max(5,
                Mathf.FloorToInt((PageWidth + CatalogueGap) / (CardMin + CatalogueGap)));
            CardW = (PageWidth - CatalogueGap * (CatalogueColumns - 1)) / CatalogueColumns;
            StockWidth = PageWidth;
            StockTop = CatalogueTop - CatalogueHeight - 16f;
            StockHeight = -(PageBottom - StockTop);
            StockInner = StockWidth - StockPad * 2f;
            StockActionX = StockInner - 110f;
        }

        RectTransform armoryContent;
        RectTransform catalogueViewport;
        RectTransform catalogueContent;
        float catalogueScroll;
        RectTransform stockViewport;
        RectTransform stockContent;
        float stockScroll;
        TMP_Text stockHeading;
        TMP_Text stockCount;

        /// <summary>The item a GIVE click is finding a holder for; -1 = browsing.</summary>
        int givePickerItemId = -1;

        /// <summary>Which shelf of the counter is on the board: 0 guns, 1 cars,
        /// 2 machines, 3 explosives. A dealer's catalogue has a page per shelf; so does
        /// this, and for the same reason - printed as one list the last shelf ends up
        /// below the fold with nothing to say so.</summary>
        int shelf;

        static readonly string[] ShelfNames =
            { "WEAPONS", "VEHICLES", "MOTORCYCLES", "EXPLOSIVES" };

        /// <summary>The catalogue codes a mail-order house prints beside a line. Fixed
        /// per shelf and per position, so the same piece keeps the same code.</summary>
        static readonly string[] ShelfCodes = { "21", "22", "23", "24" };

        string armoryNote = "";

        void BuildArmoryPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Armory);

            armoryContent = NewRect("Counter", root);
            Stretch(armoryContent);

            // The merchandise board: its own window, its own scroll, so a catalogue that
            // grows a listing never pushes the stock book off the page.
            catalogueViewport = NewRect("Catalogue", root);
            PlaceTopLeft(catalogueViewport, PageLeft, CatalogueTop, PageWidth,
                CatalogueHeight);
            catalogueViewport.gameObject.AddComponent<RectMask2D>();

            catalogueContent = NewRect("Plates", catalogueViewport);
            catalogueContent.anchorMin = new Vector2(0f, 1f);
            catalogueContent.anchorMax = new Vector2(1f, 1f);
            catalogueContent.pivot = new Vector2(0f, 1f);
            catalogueContent.anchoredPosition = Vector2.zero;
            catalogueContent.sizeDelta = new Vector2(0f, CatalogueHeight);

            // The stock book: the supplier's pink carbon, kept in the folder. Its own
            // scroll - a sixty-man outfit's stock outgrows the page.
            var carbon = LedgerV2.Card("Stock Book", root, PageLeft, StockTop, StockWidth, StockHeight, LedgerV2.Carbon);

            stockHeading = Caps(carbon, StockPad, -10f, 600f, "STOCK BOOK · CARBON COPY",
                13f, LedgerV2.CarbonInk, 5f);
            stockCount = Caps(carbon, StockWidth - StockPad - 400f, -11f, 400f, "", 10f,
                new Color(107f / 255f, 43f / 255f, 35f / 255f, 0.7f), 3f,
                TextAlignmentOptions.MidlineRight);
            Rule(carbon, StockPad, -36f, StockInner,
                new Color(107f / 255f, 43f / 255f, 35f / 255f, 0.35f));

            stockViewport = NewRect("Rows", carbon);
            PlaceTopLeft(stockViewport, StockPad, -44f, StockInner, StockHeight - 52f);
            stockViewport.gameObject.AddComponent<RectMask2D>();

            stockContent = NewRect("Lines", stockViewport);
            stockContent.anchorMin = new Vector2(0f, 1f);
            stockContent.anchorMax = new Vector2(1f, 1f);
            stockContent.pivot = new Vector2(0f, 1f);
            stockContent.anchoredPosition = Vector2.zero;
            stockContent.sizeDelta = new Vector2(0f, StockHeight - 52f);
        }

        void RebuildArmory()
        {
            foreach (Transform old in armoryContent)
                Destroy(old.gameObject);
            foreach (Transform old in catalogueContent)
                Destroy(old.gameObject);
            foreach (Transform old in stockContent)
                Destroy(old.gameObject);

            var roster = director.Roster;
            if (roster == null)
                return;

            var safe = outfit ? outfit.Accounts.Safe : 0;

            LedgerV2.PageHead(armoryContent, PageLeft, PageTop, PageWidth, "ARMORY",
                "MAIL-ORDER CATALOGUE · NO NAMES · NO PAPERWORK · KERBSIDE DELIVERY");

            if (armoryNote.Length > 0)
                LedgerV2.Mono(armoryContent, PageRight - 620f, PageTop - 34f, 620f,
                    armoryNote, 10.5f, LedgerV2.Red, 2f,
                    TextAlignmentOptions.MidlineRight);

            BuildShelfPills();
            BuildCatalogue(safe);
            CatalogueEdges();

            if (givePickerItemId >= 0)
                BuildGivePicker(roster);
            else
                BuildStock(roster);
        }

        /// <summary>What a printed price list does when it runs past the foot of the
        /// page: it says so. Drawn on the FIXED layer over the window's edges, never in
        /// the scrolling content, so the words stay put while the board moves under
        /// them - and only when there is something past that edge to reach.</summary>
        void CatalogueEdges()
        {
            var height = catalogueContent.sizeDelta.y;
            var hidden = height - CatalogueHeight;
            if (hidden <= 1f)
                return;

            // Arrows, not the small triangles this once carried: U+25B4/U+25BE are cut
            // by no face in Assets/Fonts/Ledger1987, so both marks were printing tofu.
            // IBM Plex Mono does cut U+2191 and U+2193.
            if (catalogueScroll > 1f)
                Line(armoryContent, LedgerStyle.MonoItalic, 11.5f, LedgerV2.Muted,
                    PageLeft, CatalogueTop + 15f, PageWidth, 14f,
                    "\u2191  more of the counter above",
                    TextAlignmentOptions.MidlineRight);

            if (catalogueScroll < hidden - 1f)
                Line(armoryContent, LedgerStyle.MonoItalic, 11.5f, LedgerV2.Muted,
                    PageLeft, CatalogueTop - CatalogueHeight - 1f, PageWidth, 14f,
                    "\u2193  more of the counter below - roll the wheel over it",
                    TextAlignmentOptions.MidlineRight);
        }

        /// <summary>The board's plates live in the scrolling window, so every x here is
        /// measured from the window's own left edge, not the page's.</summary>
        void BuildCatalogue(int safe)
        {
            var stock = shelf == 1 ? Outfit.ArmoryCatalog.Vehicles
                      : shelf == 2 ? Outfit.ArmoryCatalog.Motorcycles
                      : shelf == 3 ? Outfit.ArmoryCatalog.Explosives
                      : Outfit.ArmoryCatalog.Weapons;

            for (var i = 0; i < stock.Length; i++)
            {
                var column = i % CatalogueColumns;
                var row = i / CatalogueColumns;
                CataloguePlate(stock[i], safe, i,
                    column * (CardW + CatalogueGap),
                    -row * (CardH + CatalogueGap));
            }

            var rows = (stock.Length + CatalogueColumns - 1) / CatalogueColumns;
            SizeCatalogueContent(rows * CardH + Mathf.Max(0, rows - 1) * CatalogueGap);
        }

        /// <summary>The four pills over the board that turn to a shelf. On the FIXED
        /// layer, above the window, so they never scroll away from the thing they
        /// select - and single-select, so the dark one is always the shelf you can see.
        /// </summary>
        void BuildShelfPills()
        {
            const float width = 128f;
            const float height = 24f;
            for (var i = 0; i < ShelfNames.Length; i++)
            {
                var pick = i;
                LedgerV2.Chip(armoryContent, ShelfNames[i],
                    PageRight - (ShelfNames.Length - i) * (width + 6f) + 6f,
                    PageTop - 2f, width, height, i == shelf, () =>
                    {
                        if (shelf == pick)
                            return;
                        shelf = pick;
                        catalogueScroll = 0f;   // a new shelf opens at its own top
                        dirty = true;
                    });
            }
        }

        /// <summary>
        /// One plate on the board: the cut, the copy, what it does, and the price with
        /// the button that spends it. The two stepped rows under the blurb are the only
        /// numbers a catalogue ever prints about a gun, and they are read off the same
        /// listing the game buys from - never a second table.
        /// </summary>
        void CataloguePlate(Outfit.ArmoryItem item, int safe, int index, float x, float y)
        {
            var card = LedgerV2.Card("Plate", catalogueContent, x, y, CardW, CardH);
            const float pad = 14f;
            var inner = CardW - pad * 2f;

            var name = Line(card, LedgerStyle.Condensed, 15f, LedgerV2.Ink, pad, -8f,
                inner - 76f, LineBox(15f), item.DisplayName.ToUpperInvariant());
            name.characterSpacing = 3f;
            name.overflowMode = TextOverflowModes.Ellipsis;
            Caps(card, pad + inner - 76f, -11f, 76f,
                "CAT. " + ShelfCodes[Mathf.Clamp(shelf, 0, ShelfCodes.Length - 1)] + "-" +
                (char)('A' + Mathf.Clamp(index, 0, 25)), 9f, LedgerV2.Label, 2f,
                TextAlignmentOptions.MidlineRight);

            // The merchandise itself: the real street prefab in a live, colour display
            // case. The turntable studio owns the render rig and stops it whenever this
            // card or page is inactive. The hatched plate is only the honest fallback
            // while a model cannot be resolved.
            var raw = LedgerV2.PortraitPlate(card, pad, -36f, inner, 86f,
                item.DisplayName.ToUpperInvariant());
            var vehicle = item.Kind == EquipmentKind.Vehicle ||
                          item.Kind == EquipmentKind.Motorcycle;
            var model = vehicle
                ? PortraitStudio.FindVehiclePrefab(
                    PortraitStudio.VehicleModelFor(item.DisplayName))
                : LedgerModelSet.WeaponModelFor(item.Kind, item.ModelName);
            CatalogueTurntableStudio.Show(model, vehicle, raw,
                item.Kind == EquipmentKind.TwinPistols ? 2 : 1);

            var blurb = Paragraph(card, LedgerStyle.Serif, 13f, LedgerV2.Body, pad,
                -130f, inner, 40f, item.Note, lineSpacing: 2f);
            blurb.overflowMode = TextOverflowModes.Ellipsis;

            // What it does, in the two words a catalogue is allowed. Both are derived
            // from the listing's own price band - the game has no ballistics table, and
            // inventing one on a shop page would be a number nothing else agrees with.
            var band = Mathf.Clamp(Mathf.RoundToInt(item.Price / 400f), 1, 6);
            SpecRow(card, pad, -176f, inner, vehicle ? "SPEED" : "RANGE", band);
            SpecRow(card, pad, -194f, inner, vehicle ? "ROOM" : "STOPPING",
                Mathf.Clamp(7 - band, 1, 6));

            var price = Line(card, LedgerStyle.Condensed, 20f, LedgerV2.Ink, pad, -212f,
                inner - 110f, 26f, LedgerText.Cash(item.Price));
            price.characterSpacing = 1f;

            var captured = item;
            var order = LedgerV2.Button(card, "ORDER", pad + inner - 104f, -210f, 104f, 28f, () =>
            {
                var result = outfit
                    ? outfit.Purchase(captured.Price, captured.DisplayName)
                    : OpResult.Fail(LedgerText.ReasonNoSuchItem);
                if (result.Ok)
                {
                    director.AddEquipment(captured.Kind, captured.DisplayName,
                        captured.Price);
                    armoryNote = captured.DisplayName + " added to the stock.";
                }
                else
                    armoryNote = result.Reason;
                dirty = true;
            }, red: false, size: 11f);
            // Short money reads at a glance - the button fades; the click still
            // explains exactly how short.
            if (safe < item.Price)
                ButtonOf(order).targetGraphic.color = new Color(0.45f, 0.42f, 0.38f);
        }

        void SpecRow(Transform card, float x, float y, float w, string label, int marks)
        {
            Caps(card, x, y, 120f, label, 9f, LedgerV2.Label, 3f);
            var bar = LedgerV2.PipsWidth(6);
            LedgerV2.Pips(card, x + w - bar, y - 8f, 6, marks, LedgerV2.Red);
            LedgerV2.Leader(card, x + 76f, y - 9f, w - bar - 86f);
        }

        void BuildStock(Roster roster)
        {
            if (stockHeading)
                stockHeading.text = "STOCK BOOK · CARBON COPY";

            var out_ = 0;
            for (var i = 0; i < roster.Equipment.Count; i++)
                if (roster.Equipment[i].OwnerId != RosterEquipment.Unheld)
                    out_++;
            if (stockCount)
                stockCount.text = out_ + (out_ == 1 ? " ITEM SIGNED OUT" : " ITEMS SIGNED OUT");

            var carbonInk = LedgerV2.CarbonInk;
            var carbonFaint = new Color(107f / 255f, 43f / 255f, 35f / 255f, 0.55f);

            var y = 0f;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                var row = NewRect("Item", stockContent);
                PlaceTopLeft(row, 0f, y, StockInner, StockPitch);
                LedgerV2.Leader(row, 0f, -StockPitch + 2f, StockInner);

                var kind = Caps(row, 0f, 0f, 100f,
                    LedgerText.EquipmentLabel(item.Kind), 9.5f, carbonFaint, 3f);
                FillRow(kind.rectTransform, StockKindX, 100f);

                var name = Text("Name", row, LedgerStyle.Mono, 13.5f, carbonInk,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(name.rectTransform, StockItemX, StockHolderX - StockItemX - 20f);
                name.overflowMode = TextOverflowModes.Ellipsis;
                name.text = item.DisplayName;

                var holder = roster.Find(item.HolderId);
                var atFront = item.OwnerId == RosterEquipment.FrontArmory;
                var holderText = Text("Holder", row, LedgerStyle.Mono, 13.5f,
                    holder != null || atFront ? carbonInk : carbonFaint,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(holderText.rectTransform, StockHolderX,
                    StockActionX - StockHolderX - 20f);
                holderText.overflowMode = TextOverflowModes.Ellipsis;
                holderText.text = holder != null ? "signed out to " + holder.FullName
                    : atFront ? "signed out to the front" : "on the shelf, unissued";

                var itemId = item.Id;
                if (item.OwnerId != RosterEquipment.Unheld)
                    LedgerV2.Button(row, "RECALL", StockActionX, -2f, 100f, 22f, () =>
                    {
                        var result = director.ReturnEquipment(itemId);
                        armoryNote = result.Ok ? "" : result.Reason;
                        dirty = true;
                    }, red: true, size: 10f, outline: true);
                else
                    LedgerV2.Button(row, "ISSUE", StockActionX, -2f, 100f, 22f, () =>
                    {
                        givePickerItemId = itemId;
                        armoryNote = "";
                        dirty = true;
                    }, red: false, size: 10f);

                y -= StockPitch;
            }

            if (roster.Equipment.Count == 0)
                Line(stockContent, LedgerStyle.MonoItalic, 13f, carbonFaint, 0f, 0f,
                    StockInner, StockPitch, "Nothing on the books. The counter is above.");

            SizeStockContent(-y);
        }

        /// <summary>ISSUE's second step: the carbon becomes the lieutenants, pick the
        /// crew. ALL gear issues through a crew's head - he deals his men in himself -
        /// so the row shows the one stat every deal runs on: his Organization.</summary>
        void BuildGivePicker(Roster roster)
        {
            RosterEquipment item = null;
            for (var i = 0; i < roster.Equipment.Count; i++)
                if (roster.Equipment[i].Id == givePickerItemId)
                    item = roster.Equipment[i];
            if (item == null)
            {
                givePickerItemId = -1;
                BuildStock(roster);
                return;
            }

            if (stockHeading)
                stockHeading.text = "SIGN OUT " + item.DisplayName.ToUpperInvariant() + " TO";
            if (stockCount)
                stockCount.text = "GEAR GOES TO A CREW'S HEAD, NEVER TO A HOOD";

            LedgerV2.Button(stockContent, "CANCEL", StockActionX, 0f, 100f, 22f, () =>
            {
                givePickerItemId = -1;
                dirty = true;
            }, red: false, size: 10f, outline: true);

            // CANCEL owns the first line of the carbon on its own - the picks start
            // under it, or the FRONT row's click surface would sit beneath the button.
            var y = -StockPitch;

            // The front first: the desk is a destination like any lieutenant - gear
            // dumped there arms the men guarding it.
            {
                var frontGuards = 0;
                for (var i = 0; i < roster.Members.Count; i++)
                    if (roster.Members[i].Status == CharacterStatus.Active &&
                        RosterOps.InFrontGuard(roster, roster.Members[i].Id))
                        frontGuards++;

                PickerRow(y, "THE FRONT", "",
                    frontGuards > 0 ? "deals to " + frontGuards + " men" : "nobody on guard",
                    () =>
                    {
                        var result = director.GiveEquipmentToFront(givePickerItemId);
                        armoryNote = result.Ok ? "" : result.Reason;
                        givePickerItemId = -1;
                        dirty = true;
                    });
                y -= StockPitch;
            }

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Gone || member.Rank != Rank.Lieutenant)
                    continue;

                var memberId = member.Id;
                var crew = roster.CrewOf(memberId);
                PickerRow(y, member.FullName,
                    LedgerText.AttributeLabel(CharacterAttribute.Organization) + " " +
                    LedgerText.Stars(member.GetHalfSteps(CharacterAttribute.Organization)),
                    crew != null ? "deals to " + crew.HoodIds.Count + " men" : "no crew yet",
                    () =>
                    {
                        var result = director.GiveEquipment(givePickerItemId, memberId);
                        armoryNote = result.Ok ? "" : result.Reason;
                        givePickerItemId = -1;
                        dirty = true;
                    });
                y -= StockPitch;
            }

            SizeStockContent(-y);
        }

        void PickerRow(float y, string name, string stat, string men,
            UnityEngine.Events.UnityAction pick)
        {
            var row = NewRect("Pick", stockContent);
            PlaceTopLeft(row, 0f, y, StockInner, StockPitch);
            var surface = ClickSurface(row);
            RowButton(row, surface, pick);
            Highlight(row, LedgerV2.Picked);

            var carbonInk = LedgerV2.CarbonInk;
            var nameText = Text("Name", row, LedgerStyle.MonoBold, 13.5f, carbonInk,
                TextAlignmentOptions.MidlineLeft);
            FillRow(nameText.rectTransform, 12f, 320f);
            nameText.text = name;

            var statText = Text("Stat", row, LedgerStyle.Mono, 13.5f,
                new Color(107f / 255f, 43f / 255f, 35f / 255f, 0.7f),
                TextAlignmentOptions.MidlineLeft);
            FillRow(statText.rectTransform, 340f, 260f);
            statText.text = stat;

            var menText = Text("Crew", row, LedgerStyle.Mono, 13.5f,
                new Color(107f / 255f, 43f / 255f, 35f / 255f, 0.7f),
                TextAlignmentOptions.MidlineLeft);
            FillRow(menText.rectTransform, 620f, 300f);
            menText.text = men;
        }

        void SizeCatalogueContent(float height)
        {
            catalogueContent.sizeDelta = new Vector2(0f, Mathf.Max(CatalogueHeight, height));
            var maxScroll = Mathf.Max(0f, catalogueContent.sizeDelta.y - CatalogueHeight);
            catalogueScroll = Mathf.Clamp(catalogueScroll, 0f, maxScroll);
            catalogueContent.anchoredPosition = new Vector2(0f, catalogueScroll);
        }

        void SizeStockContent(float height)
        {
            var window = StockHeight - 52f;
            stockContent.sizeDelta = new Vector2(0f, Mathf.Max(window, height));
            var maxScroll = Mathf.Max(0f, stockContent.sizeDelta.y - window);
            stockScroll = Mathf.Clamp(stockScroll, 0f, maxScroll);
            stockContent.anchoredPosition = new Vector2(0f, stockScroll);
        }
    }
}
