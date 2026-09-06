using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// The page's furniture: its head, the strip that narrows the archive, and the
    /// footer that says where in it the reader is standing.
    ///
    /// Every control here is BUILT once. A repaint binds words and colours onto the
    /// same objects, because one of them holds a caret: a FIND field destroyed and
    /// rebuilt on the reader's own keystroke loses half the word he is typing.
    /// </summary>
    public sealed partial class WireSheet
    {
        static readonly string[] BookNames = { "BOTH BOOKS", "OUR MEN", "OUR DOORS" };
        const string AllSources = "ALL SOURCES";

        RectTransform bookHost, penHost, sourceKey, sourceMenu, sourceShade, tip;
        TMP_InputField find;
        TextMeshProUGUI sourceWord, placeholder, scopeWord, clearKey;
        TextMeshProUGUI footWord, footHint, newerKey, olderKey, tipWord;
        readonly List<Image> penFace = new List<Image>();
        readonly List<RectTransform> penBox = new List<RectTransform>();

        float StripTop => -(TopPad + 72f);

        void BuildSheet()
        {
            built = true;
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
            penFace.Clear();
            penBox.Clear();

            LedgerV2.PageHead(root, Pad, -TopPad, width, "THE WIRE",
                "THE OUTFIT'S DISPATCHES · NEWEST FIRST · CLICK A LINE TO DRAW ITS " +
                "SLIP · ↑ ↓ WALKS THE REGISTER");

            BuildStrip();
            BuildRegister();
            BuildRail();
            BuildSlipStrip();
            BuildFooter();
            BuildTip();
        }

        // ------------------------------------------------------------- filter strip

        void BuildStrip()
        {
            var y = StripTop;
            var x = Pad;

            x += Label(x, y, "BOOK") + 10f;
            bookHost = NewRect("Book", root);
            PlaceTopLeft(bookHost, x, y - 3f, 300f, 26f);
            x += LayBook() + 16f;

            x += Divider(x, y) + 16f;
            x += Label(x, y, "PEN") + 10f;
            penHost = NewRect("Pens", root);
            PlaceTopLeft(penHost, x, y - 5f, 5f * 28f + 60f, 22f);
            for (var i = 0; i < 5; i++)
            {
                var pen = (WirePen)i;
                var box = NewRect("Pen " + pen, penHost);
                PlaceTopLeft(box, i * 28f, 0f, 22f, 22f);
                Fill(box, LedgerV2.Ink);
                var face = NewRect("Face", box);
                Stretch(face, 1f);
                penFace.Add(Fill(face, WireRegister.InkOf(pen)));
                penBox.Add(box);
                var hit = Hit(box);
                hit.index = i;
                hit.click = index => TogglePen((WirePen)index);
                hit.enter = index => ShowTip(
                    WireRegister.PenMeaning((WirePen)index).ToUpperInvariant(),
                    penHost.anchoredPosition.x + index * 28f, StripTop - 30f);
                hit.exit = _ => HideTip();
            }
            LedgerV2.Button(penHost, "ALL", 5f * 28f + 6f, 0f, 44f, 22f, AllPens,
                LedgerV2.Key.Outline, MonoPt(10f));
            x += 5f * 28f + 56f + 16f;

            x += Divider(x, y) + 16f;
            x += Label(x, y, "SOURCE") + 10f;
            sourceKey = NewRect("Source", root);
            PlaceTopLeft(sourceKey, x, y - 4f, 190f, 24f);
            Rule(sourceKey, 0f, -22f, 190f, LedgerV2.SheetRule);
            sourceWord = Cell(sourceKey, 0f, 0f, 190f, 22f, AllSources, MonoPt(11f),
                LedgerV2.Ink, 5f);
            var sourceHit = Hit(sourceKey);
            sourceHit.click = _ => ToggleSourceMenu();
            x += 190f + 16f;

            x += Divider(x, y) + 16f;
            x += Label(x, y, "FIND") + 10f;
            find = Field(root, x, y - 4f, 220f, 24f, "", Find, null, MonoPt(11.5f), 24);
            var rule = find.transform.Find("Rule");
            if (rule)
                rule.GetComponent<Image>().color = LedgerV2.SheetRule;
            placeholder = Cell(find.transform, 6f, 0f, 210f, 24f,
                "A MAN, A DOOR, A WORD", MonoPt(11.5f), LedgerV2.Muted, 3f);

            // The scope readout and the key that clears it stand in the head's right
            // margin rather than at the end of this strip. The strip's controls are set
            // in the BOOK's type, which prints larger than the design's screen faces,
            // and a readout squeezed in beside them would be an ellipsis by the second
            // facet. The head's right half is empty, and a document says what it has
            // been narrowed to at its head.
            scopeWord = Cell(root, Pad + width - 620f, -TopPad - 4f, 620f, 26f, "",
                MonoPt(11f), LedgerV2.Muted, 7f, TextAlignmentOptions.MidlineRight);
            clearKey = LedgerV2.Button(root, "CLEAR SCOPE", Pad + width - 130f,
                -TopPad - 30f, 130f, 24f, ClearScope, LedgerV2.Key.Ghost, MonoPt(10f));
        }

        float LayBook()
        {
            for (var i = bookHost.childCount - 1; i >= 0; i--)
            {
                var child = bookHost.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
            return LedgerV2.Segmented(bookHost, 0f, 0f, 26f, BookNames, (int)narrow.Book,
                PickBook, 0f, MonoPt(10.5f));
        }

        float Label(float x, float y, string word)
        {
            var wide = LedgerV2.MonoWidth(word, MonoPt(10.5f), 14f);
            Cell(root, x, y, wide + 8f, StripH, word, MonoPt(10.5f), LedgerV2.Muted, 14f);
            return wide;
        }

        float Divider(float x, float y)
        {
            Block("Divider", root, x, y - 6f, 1f, 20f, LedgerV2.Rule);
            return 1f;
        }

        // ------------------------------------------------------------- source picker

        void ToggleSourceMenu()
        {
            if (sourceMenu)
            {
                CloseSourceMenu();
                return;
            }
            sourceShade = NewRect("Source shade", root);
            Stretch(sourceShade);
            var shade = sourceShade.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0f);
            shade.raycastTarget = true;
            var shadeHit = sourceShade.gameObject.AddComponent<WireHit>();
            shadeHit.click = _ => CloseSourceMenu();

            var names = register.Sources;
            var rows = names.Count + 1;
            var menuH = rows * 24f + 8f;
            sourceMenu = NewRect("Source list", root);
            PlaceTopLeft(sourceMenu, sourceKey.anchoredPosition.x,
                sourceKey.anchoredPosition.y - 24f, 190f, menuH);
            Fill(sourceMenu, LedgerV2.Panel);
            Frame(sourceMenu, 1f, LedgerV2.SheetRule);
            for (var i = 0; i < rows; i++)
            {
                var name = i == 0 ? "" : names[i - 1];
                var row = NewRect("Source row", sourceMenu);
                PlaceTopLeft(row, 4f, -4f - i * 24f, 182f, 24f);
                Fill(row, name == narrow.Source ? LedgerV2.Picked : LedgerV2.Panel);
                Cell(row, 8f, 0f, 166f, 24f, i == 0 ? AllSources : name, MonoPt(11f),
                    LedgerV2.Ink, 5f);
                var hit = Hit(row);
                hit.click = _ =>
                {
                    CloseSourceMenu();
                    PickSource(name);
                };
            }
        }

        void CloseSourceMenu()
        {
            if (sourceMenu)
                Object.Destroy(sourceMenu.gameObject);
            if (sourceShade)
                Object.Destroy(sourceShade.gameObject);
            sourceMenu = null;
            sourceShade = null;
        }

        // ------------------------------------------------------------------- footer

        void BuildFooter()
        {
            var y = FooterTop;
            Rule(root, Pad, y + 1f, width, LedgerV2.SheetRule);
            footWord = Cell(root, Pad, y, 700f, FooterH, "", MonoPt(12f), LedgerV2.Muted,
                8f);

            olderKey = LedgerV2.Button(root, "OLDER DAY", Pad + width - 130f, y - 4f,
                130f, 30f, () => StepDay(1), LedgerV2.Key.Outline, MonoPt(10f));
            newerKey = LedgerV2.Button(root, "NEWER DAY", Pad + width - 270f, y - 4f,
                130f, 30f, () => StepDay(-1), LedgerV2.Key.Outline, MonoPt(10f));

            footHint = Cell(root, Pad + width - 700f, y, 420f, FooterH, "", MonoPt(11f),
                LedgerV2.Muted, 8f, TextAlignmentOptions.MidlineRight);
        }

        void PaintFooter()
        {
            if (!footWord)
                return;
            var reading = register.DayAt(scroll);
            var oldest = register.Days.Count > 0
                ? register.Days[register.Days.Count - 1].Day : 0;
            var newestDay = register.Days.Count > 0 ? register.Days[0].Day : 0;
            var filed = narrow.Narrowed
                ? Figure(register.Count) + " OF " + Figure(register.Total) +
                  " FILED IN SCOPE"
                : Figure(register.Total) + " FILED";
            var span = newestDay > 0
                ? " · DAY " + newestDay + " – DAY " + oldest : "";
            var at = reading > 0 ? " · READING DAY " + reading : "";
            footWord.text = filed + span + at;

            footHint.text = narrow.DayOnly >= 0
                ? "DOUBLE-CLICK THE DAY AGAIN TO RELEASE IT"
                : "DAY RAIL · CLICK TO JUMP · DOUBLE-CLICK TO ISOLATE";

            var newer = reading >= 0 && register.Step(reading, -1) != reading;
            var older = reading >= 0 && register.Step(reading, 1) != reading;
            LedgerV2.KeyEnabled(newerKey, newer);
            LedgerV2.KeyEnabled(olderKey, older);
            if (newer)
                newerKey.color = LedgerV2.Ink;
            if (older)
                olderKey.color = LedgerV2.Ink;
        }

        // --------------------------------------------------------------- strip state

        void PaintStrip()
        {
            if (!bookHost)
                return;
            LayBook();
            var chosen = narrow.Pens != 0;
            for (var i = 0; i < penFace.Count; i++)
            {
                var on = !chosen || (narrow.Pens & (1 << i)) != 0;
                var ink = WireRegister.InkOf((WirePen)i);
                penFace[i].color = LedgerV2.At(ink, on ? 1f : 0.28f);
                var picked = (narrow.Pens & (1 << i)) != 0;
                penBox[i].GetComponent<Image>().color = picked
                    ? LedgerV2.Ink
                    : LedgerV2.At(LedgerV2.Ink, on ? 0.25f : 0.12f);
            }

            sourceWord.text = string.IsNullOrEmpty(narrow.Source)
                ? AllSources : narrow.Source;
            // CLEAR SCOPE clears the scope, and the field is part of it: a word left
            // standing in FIND is re-applied whole by the reader's next keystroke.
            if (find && find.text != narrow.Query)
                find.SetTextWithoutNotify(narrow.Query);
            if (placeholder)
                placeholder.gameObject.SetActive(string.IsNullOrEmpty(narrow.Query));

            var scope = "WHOLE ARCHIVE · " + Figure(register.Total) + " FILED";
            if (narrow.Narrowed)
            {
                var bits = "";
                if (narrow.Book != WireScope.Both)
                    bits += BookNames[(int)narrow.Book];
                var pens = 0;
                for (var i = 0; i < 5; i++)
                    if ((narrow.Pens & (1 << i)) != 0)
                        pens++;
                if (pens > 0)
                    bits += (bits.Length > 0 ? " · " : "") + pens +
                        (pens == 1 ? " PEN" : " PENS");
                if (!string.IsNullOrEmpty(narrow.Source))
                    bits += (bits.Length > 0 ? " · " : "") + narrow.Source;
                if (narrow.DayOnly >= 0)
                    bits += (bits.Length > 0 ? " · " : "") + "DAY " + narrow.DayOnly +
                        " ONLY";
                if (!string.IsNullOrEmpty(narrow.Query))
                    bits += (bits.Length > 0 ? " · " : "") + "\"" +
                        narrow.Query.Trim().ToUpperInvariant() + "\"";
                scope = "SCOPE · " + bits + " · " + Figure(register.Count) + " OF " +
                    Figure(register.Total);
            }
            scopeWord.text = scope;
            scopeWord.color = narrow.Narrowed ? LedgerV2.Ink : LedgerV2.Muted;
            var clear = LedgerV2.KeyOf(clearKey);
            if (clear)
                clear.gameObject.SetActive(narrow.Narrowed);
        }

        // ------------------------------------------------------------------ tooltip

        void BuildTip()
        {
            tip = NewRect("Wire tip", root);
            PlaceTopLeft(tip, 0f, 0f, 260f, 22f);
            Fill(tip, LedgerV2.Head);
            tipWord = Cell(tip, 8f, 0f, 244f, 22f, "", MonoPt(11f), LedgerV2.HeadCream,
                8f);
            tip.gameObject.SetActive(false);
        }

        void ShowTip(string word, float x, float y)
        {
            if (!tip)
                return;
            tipWord.text = word;
            var w = Mathf.Max(80f, LedgerV2.MonoWidth(word, MonoPt(11f), 8f) + 24f);
            tip.sizeDelta = new Vector2(w, 22f);
            tipWord.rectTransform.sizeDelta = new Vector2(w - 16f,
                tipWord.rectTransform.sizeDelta.y);
            tip.anchoredPosition = new Vector2(Mathf.Min(x, Pad + width - w), y);
            tip.SetAsLastSibling();
            tip.gameObject.SetActive(true);
        }

        void HideTip()
        {
            if (tip)
                tip.gameObject.SetActive(false);
        }
    }
}
