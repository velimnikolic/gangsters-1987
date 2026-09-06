using System;
using System.Collections.Generic;
using System.Globalization;
using LivingCity.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// THE WIRE - the outfit's archive, kept as a ruled register.
    ///
    /// One line per slip, newest day at the top, under a day band that adds the day up;
    /// the whole dispatch prints on a telex slip at the foot of the page when a line is
    /// drawn. There is ONE navigation model - a single continuous scroll - and the day
    /// rail, the arrow keys and the two day keys are all jumps inside it, never a second
    /// way of walking the same list.
    ///
    /// This surface never composes a sentence. Every body, tag and stamp on it was
    /// written the day the thing happened (<see cref="WireBook"/>'s one rule); the
    /// register may clip a line with an ellipsis, which is typesetting, and the slip at
    /// the foot then prints it whole. The only words this page writes are counts of what
    /// is in front of the reader, and it writes them once, in <see cref="WireRegister"/>.
    ///
    /// The page is BUILT once and then only bound: two thousand slips are laid out as a
    /// list of y offsets and only the window around the scroll is given views out of a
    /// pool. Nothing on this sheet is destroyed and rebuilt on a wheel notch.
    /// </summary>
    public sealed partial class WireSheet
    {
        // ------------------------------------------------------------------ geometry
        // The design's own reference units. The ledger canvas is 1920x1080 and this
        // sheet is 1590x980 of it, so these are literal, not scaled.

        const float Pad = 24f, TopPad = 22f, FootPad = 18f;
        const float RegisterW = 1494f, RailW = 34f, RailGap = 14f;
        const float HeadBandH = 28f, StripH = 32f, HeldH = 24f;
        const float SlipStripH = 178f, FooterH = 38f;
        const float LineH = WireRegister.LineH - 1f;
        const float Window = 120f;

        // The columns, and the rules that stand between them. One table: the head band,
        // every line and the pre-ruled page all read their x out of these.
        const float DayX = 0f, DayW = 100f;
        const float PenX = 100f, PenW = 26f;
        const float TagX = 126f, TagW = 146f;
        const float BodyX = 272f, BodyW = 826f;
        const float HeatX = 1098f, HeatW = 76f;
        const float MoneyX = 1174f, MoneyW = 92f;
        const float SourceX = 1266f, SourceW = 118f;
        const float FileX = 1384f, FileW = 110f;
        static readonly float[] ColumnRules =
            { PenX, TagX, BodyX, HeatX, MoneyX, SourceX, FileX };

        // Two greys the book had no token for: the register's banded line and the line
        // under the pointer. Both are the design's own values, converted once.
        static readonly Color Banded = LedgerV2.Rgb2(0xefe9e2);
        static readonly Color Hovered = LedgerV2.Rgb2(0xe9e1d7);

        static float MonoPt(float px) => LedgerStyle.FromPx(px, LedgerStyle.MonoOptical);
        static float GothicPt(float px) =>
            LedgerStyle.FromPx(px, LedgerStyle.CondensedOptical);

        // --------------------------------------------------------------------- state

        readonly WireRegister register = new WireRegister();
        readonly WireSlip slip = new WireSlip();
        readonly List<WireLine> lines = new List<WireLine>();

        RectTransform root;
        Action<WireLine> open;
        Func<WireLine, string> trouble;
        OutfitDirector outfit;

        float width, height, scroll;
        int paintedVersion;
        bool built, booksDirty = true, narrowDirty = true;

        WireNarrow narrow = WireNarrow.Open;

        /// <summary>The slip drawn at the foot, and where its line stands in the run -
        /// -1 when the reader has narrowed it out of sight but is still reading it.</summary>
        WireLine? drawn;
        int drawnIndex = -1;
        string drawnTrouble = "";

        int hovered = -1, hoveredDay = -1;
        int heldNew;

        /// <summary>Where the reader was standing when the books changed under him, so
        /// entries landing above it can be held without moving the page - and where that
        /// line stood in the WHOLE archive, which is how many arrived above it.</summary>
        WireLine? anchor;
        float anchorOffset;
        int anchorFiled;

        public void Build(RectTransform parent, float pageWidth, float pageHeight,
            Action<WireLine> onOpen, Func<WireLine, string> targetTrouble = null)
        {
            root = parent;
            width = pageWidth - Pad * 2f;
            height = pageHeight;
            open = onOpen;
            trouble = targetTrouble;
            built = false;
            booksDirty = true;
            narrowDirty = true;
        }

        /// <summary>Draw one slip out of the archive from somewhere else in the book -
        /// a click on the rail's wire, or a jump whose file has since been closed.</summary>
        public void ShowRecord(WireLine line, string reason = "")
        {
            drawn = line;
            drawnTrouble = reason ?? "";
            drawnIndex = register.IndexOf(line);
            // Drawn onto a page that has not been built yet - by a jump from another
            // tab, which is where most of these come from. The slip is painted with the
            // rest of the sheet the first time it is refreshed.
            narrowDirty |= !built;
            if (built)
            {
                ShowLine();
                PaintSlip();
                Lay();
            }
        }

        /// <summary>The caret is in the FIND field, so the keys belong to it.</summary>
        public bool Typing => find != null && find.isFocused;

        /// <summary>Give the keys back to the book - P closes it, and a reader typing a
        /// man's name into FIND must not lose the book at the first letter.</summary>
        public void StopTyping()
        {
            if (find != null)
                find.DeactivateInputField();
        }

        // -------------------------------------------------------------------- paint

        public void Refresh(OutfitDirector source)
        {
            outfit = source;
            if (!root)
                return;
            if (!built)
                BuildSheet();

            var version = WireBook.Version(source);
            if (booksDirty || paintedVersion != version)
            {
                paintedVersion = version;
                booksDirty = false;
                Collect();
                narrowDirty = true;
            }
            if (narrowDirty)
            {
                narrowDirty = false;
                register.Build(narrow);
                drawnIndex = drawn.HasValue ? register.IndexOf(drawn.Value) : -1;
                RestoreAnchor();
                PaintStrip();
                PaintRail();
                PaintSlip();
            }
            Lay();
            PaintFooter();
        }

        /// <summary>
        /// Take both books again and count what landed above the reader.
        ///
        /// A slip arriving while day one is under the pointer must NOT throw the page
        /// back to the top. So the run is re-read, the entries that came in over the top
        /// of the old head are counted, and the notice offers them; the reader's own
        /// line is put back under his eye afterwards.
        /// </summary>
        void Collect()
        {
            anchor = null;
            anchorFiled = -1;
            if (scroll > 0f)
            {
                var items = register.Items;
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item.Kind != WireItemKind.Line || item.Y + item.H < scroll)
                        continue;
                    anchor = register.Kept[item.Index];
                    anchorOffset = item.Y - scroll;
                    anchorFiled = WireRegister.FiledAt(lines, anchor.Value);
                    break;
                }
            }

            WireBook.Collect(outfit, lines);

            // How many arrived ABOVE the line the reader is on - which is what its place
            // in the whole archive moved by. Counting the run in front of the old head
            // instead would miss a door slip filed under a day an incident already
            // leads: the books tie on the day, and the incident keeps the tie.
            if (anchor.HasValue && anchorFiled >= 0)
            {
                var moved = WireRegister.FiledAt(lines, anchor.Value);
                if (moved > anchorFiled)
                    heldNew += moved - anchorFiled;
            }
            register.Take(lines);
        }

        /// <summary>Put the reader back on the line he was reading after a rebuild, and
        /// clamp the scroll to whatever the register now comes to.</summary>
        void RestoreAnchor()
        {
            if (anchor.HasValue)
            {
                var index = register.IndexOf(anchor.Value);
                var item = index >= 0 ? register.ItemOfLine(index) : -1;
                if (item >= 0)
                    scroll = register.Items[item].Y - anchorOffset;
                anchor = null;
            }
            ClampScroll();
        }

        void ClampScroll() =>
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, register.Height - ViewportH + 6f));

        float ViewportH => RegisterH - HeadBandH - (heldNew > 0 ? HeldH : 0f);

        /// <summary>The register block's own height: what is left of the sheet once the
        /// head, the filter strip, the slip at the foot and the footer have taken
        /// theirs.</summary>
        float RegisterH => RegisterTop - RegisterBottom;

        float RegisterTop => -(TopPad + 72f + StripH + 10f);
        float RegisterBottom => SlipTop + 14f;
        float SlipTop => FooterTop + 1f + 10f + SlipStripH;
        float FooterTop => -(height - FootPad - FooterH);

        // -------------------------------------------------------------------- input

        /// <summary>The wheel, from the book's one scroll reader. 34 units a notch, the
        /// same as every other region in the ledger.</summary>
        public void Scroll(float wheel, Vector2 point)
        {
            if (!viewport ||
                !RectTransformUtility.RectangleContainsScreenPoint(viewport, point))
                return;
            ScrollTo(scroll - wheel * 34f);
        }

        void ScrollTo(float y)
        {
            var was = scroll;
            scroll = y;
            ClampScroll();
            if (Mathf.Approximately(was, scroll))
                return;
            if (scroll <= 0f)
                TakeHeld(false);
            Lay();
            PaintRail();
            PaintFooter();
        }

        /// <summary>
        /// The keys the register answers to. Up and down WALK it - selection moves one
        /// line and its slip prints at the foot - so the whole archive can be read
        /// without a single click; the rest scroll.
        /// </summary>
        public void Keys(Keyboard keyboard)
        {
            if (keyboard == null || !built || Typing)
                return;
            if (keyboard.upArrowKey.wasPressedThisFrame)
                Walk(-1);
            if (keyboard.downArrowKey.wasPressedThisFrame)
                Walk(1);
            if (keyboard.pageUpKey.wasPressedThisFrame)
                ScrollTo(scroll - (ViewportH - 60f));
            if (keyboard.pageDownKey.wasPressedThisFrame ||
                keyboard.spaceKey.wasPressedThisFrame)
                ScrollTo(scroll + (ViewportH - 60f));
            if (keyboard.homeKey.wasPressedThisFrame)
                ScrollTo(0f);
            if (keyboard.endKey.wasPressedThisFrame)
                ScrollTo(register.Height);
        }

        /// <summary>One line up or down the register, drawing its slip and scrolling
        /// only as far as it takes to keep the line under the reader's eye.</summary>
        void Walk(int step)
        {
            if (register.Count == 0)
                return;
            var index = drawnIndex;
            if (index < 0)
            {
                var items = register.Items;
                index = 0;
                for (var i = 0; i < items.Count; i++)
                    if (items[i].Kind == WireItemKind.Line && items[i].Y >= scroll)
                    {
                        index = items[i].Index;
                        break;
                    }
            }
            else
                index = Mathf.Clamp(index + step, 0, register.Count - 1);
            Draw(index);
            ShowLine();
        }

        /// <summary>Scroll only as far as it takes to bring the drawn line inside the
        /// viewport - 60 units of lead at the head, 30 at the foot.</summary>
        void ShowLine()
        {
            if (drawnIndex < 0)
                return;
            var item = register.ItemOfLine(drawnIndex);
            if (item < 0)
                return;
            var row = register.Items[item];
            if (row.Y < scroll + 60f)
                ScrollTo(row.Y - 60f);
            else if (row.Y + row.H > scroll + ViewportH - 30f)
                ScrollTo(row.Y + row.H - ViewportH + 30f);
        }

        /// <summary>Draw one line of the register out as a slip. A click does NOT
        /// navigate: the destination key on the slip does, and a dead file says so
        /// before the reader presses it.</summary>
        void Draw(int index)
        {
            if (index < 0 || index >= register.Count)
                return;
            drawnIndex = index;
            drawn = register.Kept[index];
            drawnTrouble = trouble != null ? trouble(drawn.Value) : "";
            PaintSlip();
            Lay();
        }

        void BuildSlipStrip() =>
            slip.Build(root, Pad, SlipTop, width, SlipStripH);

        /// <summary>The foot of the page prints whatever line is drawn, and says so when
        /// the file that line points at is no longer there.</summary>
        void PaintSlip() => slip.Paint(register, drawn, drawnTrouble, Open, CloseSlip);

        /// <summary>The destination key: the one thing on this page that leaves it.</summary>
        void Open(WireLine line)
        {
            HideTip();
            open?.Invoke(line);
        }

        void CloseSlip()
        {
            drawn = null;
            drawnIndex = -1;
            drawnTrouble = "";
            PaintSlip();
            Lay();
        }

        void Hover(int index)
        {
            if (hovered == index)
                return;
            hovered = index;
            Lay();
        }

        // ------------------------------------------------------------------ narrowing

        void Narrow(WireNarrow next, bool toTop = true)
        {
            narrow = next;
            narrowDirty = true;
            // The pointer's line is an index into the RUN, and the run is about to be a
            // different one. A hover left standing would tint whichever slip happens to
            // land in that place.
            hovered = -1;
            if (toTop)
                scroll = 0f;
            Refresh(outfit);
        }

        void PickBook(int book) => Narrow(narrow.WithBook((WireScope)book));

        void TogglePen(WirePen pen) => Narrow(narrow.WithPen(pen));

        void AllPens() => Narrow(narrow.WithEveryPen());

        void PickSource(string source) => Narrow(narrow.WithSource(source));

        void Find(string query) => Narrow(narrow.WithQuery(query));

        void IsolateDay(int day) => Narrow(narrow.WithDay(day));

        void ClearScope() => Narrow(WireNarrow.Open);

        /// <summary>Take the entries that were held above the reader's place.</summary>
        void TakeHeld(bool jump)
        {
            if (heldNew == 0)
                return;
            heldNew = 0;
            if (jump)
                scroll = 0f;
            ClampScroll();
            PaintHeld();
            Lay();
            PaintFooter();
        }

        /// <summary>Jump the scroll to a day's band. The rail's single click, and both
        /// day keys in the footer.</summary>
        void JumpToDay(int day)
        {
            var top = register.TopOf(day);
            if (top >= 0f)
                ScrollTo(top);
        }

        void StepDay(int direction)
        {
            var current = register.DayAt(scroll);
            if (current < 0)
                return;
            JumpToDay(register.Step(current, direction));
        }

        // ------------------------------------------------------------------- helpers

        static string Figure(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>A mono cell on this sheet: the book's own measured line, centred on
        /// the row it stands on.</summary>
        static TextMeshProUGUI Cell(Transform parent, float x, float y, float w,
            float rowH, string text, float size, Color ink, float spacing = 0f,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft,
            TMP_FontAsset font = null) =>
            LedgerV2.Cell(parent, x, y, w, rowH, text, size, ink, spacing, align, font);

        static WireHit Hit(RectTransform rect)
        {
            ClickSurface(rect);
            return rect.gameObject.AddComponent<WireHit>();
        }
    }
}
