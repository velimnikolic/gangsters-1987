using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// The foot of THE WIRE: the telex slip a drawn line prints on, and what stands
    /// there when nothing has been drawn.
    ///
    /// This is the ONLY place a whole dispatch appears. The register clips a long line
    /// with an ellipsis because a register line is one line; the slip prints the body
    /// word for word, as it was filed, and nothing here re-words it.
    ///
    /// Its own object rather than another face of the sheet: the strip is rebuilt when
    /// the DRAWN SLIP changes - a selection, not a wheel notch - so it is laid out
    /// plainly while the register beside it is pooled.
    /// </summary>
    public sealed class WireSlip
    {
        const float SlipW = 900f, LeaderX = 924f, LeaderW = 300f, KeyW = 200f;

        readonly List<string> labels = new List<string>();
        readonly List<string> figures = new List<string>();
        readonly List<int> pens = new List<int>();

        RectTransform run;
        float width, height;

        static float Pt(float px) => LedgerStyle.FromPx(px, LedgerStyle.MonoOptical);

        /// <summary>The strip's own ground: the heavy rule that closes the register, and
        /// the run everything below it is laid into.</summary>
        public void Build(RectTransform parent, float x, float top, float w, float h)
        {
            Block("Slip rule", parent, x, top, w, 3f, LedgerV2.Head);
            width = w;
            height = h - 15f;
            run = NewRect("Slip strip", parent);
            PlaceTopLeft(run, x, top - 15f, width, height);
        }

        public void Paint(WireRegister register, WireLine? drawn, string trouble,
            Action<WireLine> open, Action close)
        {
            if (!run)
                return;
            for (var i = run.childCount - 1; i >= 0; i--)
            {
                var child = run.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
            if (drawn.HasValue)
                Drawn(drawn.Value, trouble, open, close);
            else
                Nothing(register);
        }

        void Drawn(WireLine line, string trouble, Action<WireLine> open, Action close)
        {
            var ink = line.Ink;
            var slip = Slip(run, 0f, 0f, SlipW, height - 4f,
                "Wire · " + (line.Origin.Length > 0 ? line.Origin : "THE OUTFIT"),
                line.ClockFace.Length > 0 ? line.ClockFace : "day only", line.Body, ink,
                true, Pt(14f));
            LedgerV2.Cell(slip, 12f, -20f, SlipW - 24f, 20f,
                (line.Stamp + " · " + line.Tag).ToUpperInvariant(), Pt(12f), ink, 7f,
                TextAlignmentOptions.MidlineLeft, LedgerStyle.MonoBold);

            var y = Leader(LeaderX, 0f, "FILED", line.Stamp, LedgerV2.Ink);
            y = Leader(LeaderX, y, "BOOK", line.FromDoor ? "OUR DOORS" : "OUR MEN",
                LedgerV2.Ink);
            y = Leader(LeaderX, y, "TAG", line.Tag.ToUpperInvariant(), ink);
            if (line.Heat > 0)
                y = Leader(LeaderX, y, "POLICE ATTENTION", "+" + line.Heat + " HEAT", ink);
            if (line.Money > 0)
                Leader(LeaderX, y, line.FromDoor ? "AT THE DOOR" : "ON THE BOOKS",
                    LedgerText.Cash(line.Money), LedgerV2.Ink);

            // A file that is no longer there is said BEFORE the key is pressed: the key
            // greys and the reason stands beside it under the stamp.
            var dead = !string.IsNullOrEmpty(trouble);
            if (dead)
            {
                var block = NewRect("No file", run);
                PlaceTopLeft(block, width - 318f, 0f, 318f, 84f);
                Fill(block, LedgerV2.PanelBand);
                Block("Dead edge", block, 0f, 0f, 3f, 84f, LedgerStyle.RedPen);
                Stamp(block, "NO FILE", 12f, -10f, 130f, 40f);
                Paragraph(block, LedgerStyle.Mono, Pt(12f), LedgerV2.Muted, 152f, -10f,
                    154f, 68f, trouble, lineSpacing: 4f);
            }

            LedgerV2.Button(run, "CLOSE ITEM", width - KeyW, -(height - 74f), KeyW, 30f,
                () => close(), LedgerV2.Key.Outline, Pt(10f));
            var go = LedgerV2.Button(run, line.ActionLabel, width - KeyW,
                -(height - 34f), KeyW, 30f, () => open(line), LedgerV2.Key.Dark, Pt(10f));
            LedgerV2.KeyEnabled(go, !dead, LedgerV2.Muted);
        }

        float Leader(float x, float y, string label, string figure, Color ink)
        {
            const float rowH = 20f;
            LedgerV2.Cell(run, x, -y, 170f, rowH, label.ToUpperInvariant(), Pt(11f),
                LedgerV2.Muted, 10f);
            var wide = LedgerV2.MonoWidth(label, Pt(11f), 10f) + 6f;
            LedgerV2.Leader(run, x + wide, -y - rowH * 0.5f,
                Mathf.Max(10f, LeaderW - wide - 90f));
            LedgerV2.Cell(run, x + LeaderW - 120f, -y, 120f, rowH, figure, Pt(13f), ink,
                0f, TextAlignmentOptions.MidlineRight, LedgerStyle.MonoBold);
            return y + rowH + 7f;
        }

        /// <summary>
        /// Nothing drawn - a state of its own, not a leftover. What the page says when
        /// the reader has not asked for a slip yet, and the count of everything that is
        /// in front of him while he decides.
        /// </summary>
        void Nothing(WireRegister register)
        {
            LedgerV2.Cell(run, 0f, 0f, 520f, 20f, "NO SLIP DRAWN", Pt(11f),
                LedgerV2.Muted, 16f);
            var copy = Paragraph(run, LedgerStyle.Mono, Pt(13.5f), LedgerV2.Copy, 0f,
                -26f, 520f, 110f,
                "Click any line of the register to draw it out of the archive — the " +
                "dispatch prints here word for word, as it was filed. ↑ and ↓ walk the " +
                "register line by line.", lineSpacing: 6f);
            copy.overflowMode = TextOverflowModes.Ellipsis;

            register.Tally(labels, figures, pens);
            for (var i = 0; i < labels.Count; i++)
            {
                var x = 600f + i / 4 * 468f;
                var y = i % 4 * 28f;
                if (pens[i] >= 0)
                    Block("Pen", run, x, -y - 5f, 10f, 10f,
                        WireRegister.InkOf((WirePen)pens[i]));
                LedgerV2.Cell(run, x + 18f, -y, 200f, 20f, labels[i], Pt(11f),
                    LedgerV2.Muted, 10f);
                var wide = LedgerV2.MonoWidth(labels[i], Pt(11f), 10f) + 24f;
                LedgerV2.Leader(run, x + wide, -y - 10f, Mathf.Max(10f, 420f - wide - 80f));
                LedgerV2.Cell(run, x + 300f, -y, 120f, 20f, figures[i], Pt(13f),
                    LedgerV2.Ink, 0f, TextAlignmentOptions.MidlineRight,
                    LedgerStyle.MonoBold);
            }
        }
    }
}
