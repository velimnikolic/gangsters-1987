using System;
using System.Collections.Generic;
using LivingCity.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>The Wire's archive view. Owns only paging, scroll and the selected
    /// record; the campaign and racket books remain the owners of every event.</summary>
    public sealed class WireSheet
    {
        const int PageSize = 24;
        readonly List<WireLine> lines = new List<WireLine>();
        RectTransform root, viewport, content;
        Action<WireLine> open;
        OutfitDirector outfit;
        float width, height, scroll;
        int first, paintedVersion;
        bool dirty = true;
        WireLine? selected;
        string notice;

        public void Build(RectTransform parent, float pageWidth, float pageHeight,
            Action<WireLine> onOpen)
        {
            root = parent;
            width = pageWidth - 48f;
            height = pageHeight;
            open = onOpen;
            dirty = true;
        }

        public void ShowRecord(WireLine line, string reason = "")
        {
            selected = line;
            notice = reason;
            dirty = true;
        }

        public void Refresh(OutfitDirector source)
        {
            outfit = source;
            var version = WireBook.Version(source);
            if (!root || (!dirty && paintedVersion == version))
                return;
            paintedVersion = version;
            dirty = false;
            // Retire hit targets immediately; Unity destroys their objects at frame end.
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }

            WireBook.Collect(source, lines);
            first = Mathf.Clamp(first, 0, Math.Max(0, (lines.Count - 1) / PageSize) * PageSize);
            var y = LedgerV2.PageHead(root, 24f, -16f, width, "THE WIRE",
                "THE OUTFIT'S DISPATCHES · NEWEST FIRST · CLICK AN ITEM TO OPEN ITS FILE");

            if (selected.HasValue)
            {
                var record = selected.Value;
                var panel = NewRect("Selected wire item", root);
                PlaceTopLeft(panel, 24f, y, width, 0f);
                Fill(panel, LedgerV2.PanelBand);
                LedgerV2.Mono(panel, 16f, -10f, width - 150f,
                    record.Stamp + " · " + record.Tag, 12f, record.Ink);
                LedgerV2.Button(panel, "CLOSE ITEM", width - 128f, -8f, 112f, 24f,
                    () => { selected = null; dirty = true; Refresh(outfit); });
                var copyH = Copy(panel, record.Body, 16f, -38f, width - 32f);
                var panelH = 50f + copyH;
                if (!string.IsNullOrEmpty(notice))
                {
                    var note = Paragraph(panel, LedgerStyle.Mono, 12f, LedgerV2.Muted,
                        16f, -panelH, width - 32f, 0f, notice);
                    var noteH = Mathf.Ceil(note.GetPreferredValues(notice, width - 32f, 0f).y);
                    note.rectTransform.sizeDelta = new Vector2(width - 32f, noteH);
                    panelH += noteH + 12f;
                }
                panel.sizeDelta = new Vector2(width, panelH);
                y -= panelH + 16f;
            }

            viewport = NewRect("Wire archive viewport", root);
            PlaceTopLeft(viewport, 24f, y, width, Mathf.Max(80f, height + y - 66f));
            viewport.gameObject.AddComponent<RectMask2D>();
            content = NewRect("Wire archive", viewport);
            PlaceTopLeft(content, 0f, 0f, width, 0f);

            var cursor = 0f;
            if (lines.Count == 0)
            {
                cursor -= Copy(content, "Nothing on the wire yet. New dispatches will appear here.",
                    16f, -16f, width - 32f) + 32f;
            }
            var end = Math.Min(lines.Count, first + PageSize);
            for (var i = first; i < end; i++)
                cursor = DrawRow(cursor, lines[i]);
            content.sizeDelta = new Vector2(width, -cursor);
            ApplyScroll();

            var foot = -height + 48f;
            LedgerV2.Mono(root, 24f, foot, width - 340f,
                lines.Count == 0 ? "0 FILED" :
                    (first + 1) + "–" + end + " OF " + lines.Count + " FILED · SCROLL TO READ",
                12f);
            var newer = LedgerV2.Button(root, "NEWER", 24f + width - 256f, foot, 120f, 28f,
                () => Turn(-PageSize));
            ButtonOf(newer).interactable = first > 0;
            var older = LedgerV2.Button(root, "OLDER", 24f + width - 120f, foot, 120f, 28f,
                () => Turn(PageSize));
            ButtonOf(older).interactable = end < lines.Count;
        }

        float DrawRow(float y, WireLine line)
        {
            var row = NewRect("Wire item " + line.Tag, content);
            PlaceTopLeft(row, 0f, y, width, 0f);
            Fill(row, LedgerV2.Panel);
            LedgerV2.Mono(row, 18f, -10f, width - 220f,
                line.Stamp + " · " + line.Tag, 12f, line.Ink);
            if (!string.IsNullOrEmpty(line.Figure))
                LedgerV2.Figure(row, width - 202f, -10f, 184f, line.Figure,
                    12f, line.Ink);
            var bodyH = Copy(row, line.Body, 18f, -38f, width - 238f);
            var rowH = Mathf.Max(80f, bodyH + 54f);
            row.sizeDelta = new Vector2(width, rowH);
            Block("Event ink", row, 0f, -1f, 3f, rowH - 2f, line.Ink);
            LedgerV2.Mono(row, width - 202f, -45f, 184f,
                line.ActionLabel + " >", 12f, LedgerV2.Ink,
                align: TextAlignmentOptions.MidlineRight);
            Rule(row, 0f, -rowH, width, LedgerV2.Rule);
            RowButton(row, ClickSurface(row), () => open(line));
            var button = row.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f);
            colors.selectedColor = Color.white;
            button.colors = colors;
            return y - rowH - 8f;
        }

        static float Copy(Transform parent, string text, float x, float y, float w)
        {
            var copy = Paragraph(parent, LedgerStyle.Mono, 14f, LedgerV2.Copy,
                x, y, w, 0f, text, lineSpacing: 0f);
            var h = Mathf.Ceil(copy.GetPreferredValues(text, w, 0f).y);
            copy.rectTransform.sizeDelta = new Vector2(w, h);
            return h;
        }

        void Turn(int step)
        {
            first += step;
            scroll = 0f;
            dirty = true;
            Refresh(outfit);
        }

        public void Scroll(float wheel, Vector2 point)
        {
            if (!viewport || !content ||
                !RectTransformUtility.RectangleContainsScreenPoint(viewport, point))
                return;
            scroll -= wheel * 34f;
            ApplyScroll();
        }

        void ApplyScroll()
        {
            scroll = Mathf.Clamp(scroll, 0f,
                Mathf.Max(0f, content.sizeDelta.y - viewport.rect.height));
            content.anchoredPosition = new Vector2(0f, scroll);
        }
    }
}
