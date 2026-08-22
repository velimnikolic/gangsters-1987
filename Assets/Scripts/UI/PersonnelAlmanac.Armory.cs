using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Personnel;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// ARMORY: the supplier's catalogue page - each piece with a halftone cut, its
    /// price typed in the margin and a BUY tape - and under it the stock book: what
    /// the outfit owns, who holds it, GIVE and RETURN. GIVE's second step turns the
    /// stock book into the list of lieutenants (and the front), because all gear
    /// issues through a crew's head.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        /// <summary>Below the heading AND below the shelf tapes.</summary>
        const float CatalogueTop = PageTop - 40f - ShelfTabH - 8f;
        const float CatalogueRowH = 54f;

        /// <summary>The board is a window now, not a fixed list: the counter outgrew the
        /// half sheet the moment it stocked the street tier under the guns. Stops short
        /// of StockHeadY so the two regions never touch - and takes every pixel there is
        /// between the two, because the counter has outgrown it AGAIN.
        ///
        /// Eleven listings at fifty-four pixels apiece with three headings over them is
        /// seven hundred and eleven pixels of catalogue. At five hundred and twelve the
        /// motorcycle shelf began at five hundred and eight - four pixels of its heading
        /// showing and all three machines below the fold, on a board with nothing to say
        /// it scrolled. The player's report was that the ledger would not sell him a
        /// motorcycle, and he was right: it would not show him one. One shelf at a time
        /// (see <see cref="shelf"/>), and the deepest of the three is three hundred and
        /// five pixels, so nothing on the counter is below the fold any more - the
        /// scroll under it stays for the day a shelf outgrows even this.</summary>
        const float CatalogueHeight = 496f;

        const float StockHeadY = -660f;
        const float StockTop = -690f;
        const float StockPitch = 26f;
        const float StockHeight = StockPitch * 11f;
        const float StockWidth = PageWidth;

        RectTransform armoryContent;
        RectTransform catalogueViewport;
        RectTransform catalogueContent;
        float catalogueScroll;
        RectTransform stockViewport;
        RectTransform stockContent;
        float stockScroll;

        /// <summary>The item a GIVE click is finding a holder for; -1 = browsing.</summary>
        int givePickerItemId = -1;

        /// <summary>Which shelf of the counter is on the board: 0 guns, 1 cars,
        /// 2 machines. The counter is eleven listings deep now and a listing is a
        /// photograph, a name, a line of copy, a price and a tape - five hundred and
        /// ninety-four pixels of merchandise on a page with five hundred and thirty-four
        /// to give it. Printed as one list it ran past the foot of the board with the
        /// motorcycles below the fold and nothing to say so, and the player's report was
        /// that the ledger would not sell him one. A dealer's catalogue has a page per
        /// shelf; so does this.</summary>
        int shelf;

        static readonly string[] ShelfNames = { "WEAPONS", "VEHICLES", "MOTORCYCLES", "EXPLOSIVES" };

        const float ShelfTabH = 24f;

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

            catalogueContent = NewRect("Rows", catalogueViewport);
            catalogueContent.anchorMin = new Vector2(0f, 1f);
            catalogueContent.anchorMax = new Vector2(1f, 1f);
            catalogueContent.pivot = new Vector2(0f, 1f);
            catalogueContent.anchoredPosition = Vector2.zero;
            catalogueContent.sizeDelta = new Vector2(0f, CatalogueHeight);

            // The stock book scrolls on its own - a sixty-man outfit's stock outgrows
            // the page - and its ruled lines are fixed under the rows.
            RuledPaper(root, PageLeft, StockTop, StockWidth, StockHeight, StockPitch, -1f);

            stockViewport = NewRect("Stock", root);
            PlaceTopLeft(stockViewport, PageLeft, StockTop, StockWidth, StockHeight);
            stockViewport.gameObject.AddComponent<RectMask2D>();

            stockContent = NewRect("Rows", stockViewport);
            stockContent.anchorMin = new Vector2(0f, 1f);
            stockContent.anchorMax = new Vector2(1f, 1f);
            stockContent.pivot = new Vector2(0f, 1f);
            stockContent.anchoredPosition = Vector2.zero;
            stockContent.sizeDelta = new Vector2(0f, StockHeight);
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

            var heading = Line(armoryContent, LedgerStyle.Type, 18f, LedgerStyle.Ink, PageLeft,
                PageTop, 400f, 30f, "THE ARMORY");
            heading.characterSpacing = 4f;

            Line(armoryContent, LedgerStyle.MonoBold, 14f, LedgerStyle.Ink, PageRight - 300f,
                PageTop + 2f, 300f, 26f, "IN THE SAFE:  " + LedgerText.Cash(safe),
                TextAlignmentOptions.MidlineRight);

            if (armoryNote.Length > 0)
                Line(armoryContent, LedgerStyle.MonoItalic, 14f, LedgerStyle.RedPen, PageLeft,
                    PageTop - 26f, PageWidth, 18f, armoryNote);

            BuildShelfTabs();
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

            if (catalogueScroll > 1f)
                Line(armoryContent, LedgerStyle.MonoItalic, 11.5f, LedgerStyle.InkDim,
                    PageLeft, CatalogueTop + 15f, PageWidth, 14f,
                    "\u25B4  more of the counter above",
                    TextAlignmentOptions.MidlineRight);

            if (catalogueScroll < hidden - 1f)
                Line(armoryContent, LedgerStyle.MonoItalic, 11.5f, LedgerStyle.InkDim,
                    PageLeft, CatalogueTop - CatalogueHeight - 1f, PageWidth, 14f,
                    "\u25BE  more of the counter below - roll the wheel over it",
                    TextAlignmentOptions.MidlineRight);
        }

        /// <summary>The board's rows live in the scrolling window, so every x here is
        /// measured from the window's own left edge, not the page's.</summary>
        void BuildCatalogue(int safe)
        {
            var stock = shelf == 1 ? Outfit.ArmoryCatalog.Vehicles
                      : shelf == 2 ? Outfit.ArmoryCatalog.Motorcycles
                      : shelf == 3 ? Outfit.ArmoryCatalog.Explosives
                      : Outfit.ArmoryCatalog.Weapons;

            var y = 0f;
            y = Heading(catalogueContent, 0f, y, PageWidth,
                "Catalogue  ·  " + ShelfNames[shelf].ToLowerInvariant(), 13f);
            foreach (var item in stock)
                y = CatalogueRow(item, safe, y);

            SizeCatalogueContent(-y);
        }

        /// <summary>The three tapes over the board that turn to a shelf. On the FIXED
        /// layer, above the window, so they never scroll away from the thing they
        /// select.</summary>
        void BuildShelfTabs()
        {
            var width = 148f;
            for (var i = 0; i < ShelfNames.Length; i++)
            {
                var pick = i;
                var tape = Tape(armoryContent, ShelfNames[i],
                    PageLeft + i * (width + 8f), CatalogueTop + ShelfTabH + 4f,
                    width, ShelfTabH, () =>
                    {
                        if (shelf == pick)
                            return;
                        shelf = pick;
                        catalogueScroll = 0f;   // a new shelf opens at its own top
                        dirty = true;
                    }, size: 12f);

                // the shelf you are looking at is the one that is not faded
                if (i != shelf)
                    ButtonOf(tape).targetGraphic.color = new Color(1f, 1f, 1f, 0.45f);
            }
        }

        float CatalogueRow(Outfit.ArmoryItem item, int safe, float y)
        {
            // The merchandise itself, photographed by PortraitStudio and screened to
            // newsprint - a catalogue cut, not a colour photo. The square print is
            // cropped to its middle band by uvRect; no model, no cut, the frame stays.
            var cut = NewRect("Cut", catalogueContent);
            PlaceTopLeft(cut, 0f, y - 2f, 92f, 46f);
            Fill(cut, new Color(LedgerStyle.Paper.r * 0.9f, LedgerStyle.Paper.g * 0.9f,
                LedgerStyle.Paper.b * 0.9f));
            Frame(cut, 1f, LedgerStyle.InkFaint);
            var print = NewRect("Print", cut);
            Stretch(print, 1f);
            var raw = print.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false;
            raw.uvRect = new Rect(0f, 0.25f, 1f, 0.5f);
            var vehicle = item.Kind == EquipmentKind.Vehicle ||
                          item.Kind == EquipmentKind.Motorcycle;
            var model = vehicle
                ? PortraitStudio.FindVehiclePrefab(
                    PortraitStudio.VehicleModelFor(item.DisplayName))
                : LedgerModelSet.WeaponModelFor(item.Kind, item.ModelName);
            PortraitStudio.Request(model,
                vehicle ? PortraitStudio.Framing.Vehicle : PortraitStudio.Framing.Item,
                raw, PortraitStudio.Treatment.Newsprint);

            var name = Line(catalogueContent, LedgerStyle.Type, 14.5f, LedgerStyle.Ink,
                108f, y, 300f, 24f, item.DisplayName);
            name.characterSpacing = 1f;

            Line(catalogueContent, LedgerStyle.Mono, 12.5f, LedgerStyle.InkDim, 108f,
                y - 24f, 520f, 18f, item.Note);

            // Dotted leader to the price, the way a price list runs.
            Rule(catalogueContent, 108f, y - 44f, PageWidth - 108f, LedgerStyle.InkFaint);

            Line(catalogueContent, LedgerStyle.MonoBold, 14f, LedgerStyle.Ink,
                PageWidth - 236f, y, 120f, 24f, LedgerText.Cash(item.Price),
                TextAlignmentOptions.MidlineRight);

            var captured = item;
            var buy = Tape(catalogueContent, "BUY", PageWidth - 96f, y + 1f, 96f, 22f, () =>
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
            });
            // Short money reads at a glance - the tape fades; the click still explains
            // exactly how short.
            if (safe < item.Price)
                ButtonOf(buy).targetGraphic.color = new Color(1f, 1f, 1f, 0.45f);

            return y - CatalogueRowH;
        }

        void BuildStock(Roster roster)
        {
            var head = Line(armoryContent, LedgerStyle.Type, 14f, LedgerStyle.Ink, PageLeft,
                StockHeadY, 500f, 24f, "STOCK BOOK  ·  " + roster.Equipment.Count + " ITEMS");
            head.characterSpacing = 3f;
            Rule(armoryContent, PageLeft, StockHeadY - 24f, PageWidth, LedgerStyle.Ink);

            var y = 0f;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                var row = NewRect("Item", stockContent);
                PlaceTopLeft(row, 0f, y, StockWidth, StockPitch);

                var kind = Text("Kind", row, LedgerStyle.Type, 11.5f, LedgerStyle.InkDim,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(kind.rectTransform, 8f, 110f);
                kind.characterSpacing = 1f;
                kind.text = LedgerText.EquipmentLabel(item.Kind).ToUpperInvariant();

                var name = Text("Name", row, LedgerStyle.Mono, 14f, LedgerStyle.Ink,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(name.rectTransform, 124f, 240f);
                name.text = item.DisplayName;

                var holder = roster.Find(item.HolderId);
                var atFront = item.OwnerId == RosterEquipment.FrontArmory;
                var holderText = Text("Holder", row, LedgerStyle.Mono, 14.5f,
                    holder != null || atFront ? LedgerStyle.Ink : LedgerStyle.InkDim,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(holderText.rectTransform, 380f, 340f);
                holderText.text = holder != null ? "held by " + holder.FullName
                    : atFront ? "at the front" : "in the armory";

                var itemId = item.Id;
                if (item.OwnerId != RosterEquipment.Unheld)
                    Tape(row, "RETURN", StockWidth - 96f, -3f, 92f, 20f, () =>
                    {
                        var result = director.ReturnEquipment(itemId);
                        armoryNote = result.Ok ? "" : result.Reason;
                        dirty = true;
                    }, size: 11f);
                else
                    Tape(row, "GIVE", StockWidth - 96f, -3f, 92f, 20f, () =>
                    {
                        givePickerItemId = itemId;
                        armoryNote = "";
                        dirty = true;
                    }, size: 11f);

                y -= StockPitch;
            }

            SizeStockContent(-y);
        }

        /// <summary>GIVE's second step: the stock book becomes the lieutenants, pick
        /// the crew. ALL gear issues through a crew's head - he deals his men in
        /// himself - so the row shows the one stat every deal runs on: his Organization.</summary>
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

            var head = Line(armoryContent, LedgerStyle.Type, 14f, LedgerStyle.Ink, PageLeft,
                StockHeadY, 600f, 24f, "GIVE " + item.DisplayName.ToUpperInvariant() + " TO:");
            head.characterSpacing = 3f;
            Rule(armoryContent, PageLeft, StockHeadY - 24f, PageWidth, LedgerStyle.Ink);

            Tape(armoryContent, "CANCEL", PageRight - 96f, StockHeadY + 1f, 96f, 22f, () =>
            {
                givePickerItemId = -1;
                dirty = true;
            });

            var y = 0f;

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
                if (member.Gone ||
                    member.Rank != Rank.Lieutenant)
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

        void PickerRow(float y, string name, string stat, string men, UnityEngine.Events.UnityAction pick)
        {
            var row = NewRect("Pick", stockContent);
            PlaceTopLeft(row, 0f, y, StockWidth, StockPitch);
            var surface = ClickSurface(row);
            RowButton(row, surface, pick);
            Highlight(row, LedgerStyle.HighlighterGreen, inset: 2f);

            var nameText = Text("Name", row, LedgerStyle.MonoBold, 14f, LedgerStyle.Ink,
                TextAlignmentOptions.MidlineLeft);
            FillRow(nameText.rectTransform, 8f, 300f);
            nameText.text = name;

            var statText = Text("Stat", row, LedgerStyle.Mono, 14f, LedgerStyle.InkDim,
                TextAlignmentOptions.MidlineLeft);
            FillRow(statText.rectTransform, 320f, 220f);
            statText.text = stat;

            var menText = Text("Crew", row, LedgerStyle.Mono, 14f, LedgerStyle.InkDim,
                TextAlignmentOptions.MidlineLeft);
            FillRow(menText.rectTransform, 560f, 240f);
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
            stockContent.sizeDelta = new Vector2(0f, Mathf.Max(StockHeight, height));
            var maxScroll = Mathf.Max(0f, stockContent.sizeDelta.y - StockHeight);
            stockScroll = Mathf.Clamp(stockScroll, 0f, maxScroll);
            stockContent.anchoredPosition = new Vector2(0f, stockScroll);
        }
    }
}
