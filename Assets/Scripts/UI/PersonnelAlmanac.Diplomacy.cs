using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Outfit;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// FAMILIES - THE TABLE. One screen that answers, in this order: who is against me,
    /// what do they ask, what can I say back.
    ///
    /// A relationship map on a table tilted away from the reader. Our house sits in the
    /// middle, the rivals stand round it, and every line IS a standing. Touch a card and
    /// it stands up off the table into a dossier with its words beside it. FOCUS puts
    /// any house in the middle to read ITS table with everyone else - the information
    /// the old card index never showed.
    ///
    /// The handoff carried a second direction, THE WAR ROOM: a dark rail of houses and a
    /// paper dossier. It was built and then retired on the user's ruling - everything it
    /// said is on the table's own dossier, which now carries the reading in words, the
    /// three things about the man, the door he keeps, what they have asked us and the
    /// record between the two houses.
    ///
    /// This file owns the room and the chrome - the desk, the head, the line legend and
    /// the footer's last word. The map is in PersonnelAlmanac.TheTable.cs, every figure
    /// and every reason in <see cref="HouseTable"/>, and every key still goes through
    /// the same HouseOps door a rival's mind does.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        // ------------------------------------------------------------ the page's frame

        /// <summary>The head band: the title's line and the sub-line under it, over the
        /// design's 2-unit rule.</summary>
        const float FamiliesHeadH = 66f;

        /// <summary>The foot: the hint and the last word out of this room.</summary>
        const float FamiliesFootH = 40f;

        /// <summary>The line the sub-head and the legend share.</summary>
        static float PageTopLegend => PageTop - 33f;

        static float StageTop = -80f;
        static float StageH = 700f;

        /// <summary>The stage takes the sheet between the head and the foot. Full bleed:
        /// a taller window is a bigger table, not a bigger card.</summary>
        static void MeasureDiplomacyLayout()
        {
            StageTop = PageTop - FamiliesHeadH;
            StageH = Mathf.Max(240f, StageTop - (PageBottom + FamiliesFootH - 8f));
        }

        RectTransform diplomacyContent;

        /// <summary>The gang whose card is standing up, or -1. Keyed off the house's id
        /// and never off a slot: the ring is re-dealt whenever a house is focused.
        /// </summary>
        int tableFor = -1;

        /// <summary>The house sitting in the middle of the table, or -1 for us.</summary>
        int tableFocus = -1;

        /// <summary>The last word that left this room today, printed in the foot.
        /// </summary>
        string tableLastWord;

        void BuildDiplomacyPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Diplomacy);
            diplomacyContent = NewRect("Families", root);
            Stretch(diplomacyContent);
        }

        void RebuildDiplomacy()
        {
            foreach (Transform old in diplomacyContent)
                Destroy(old.gameObject);
            ForgetTablePieces();

            var gangs = Gangs.GangRegistry.Gangs;
            var rivals = 0;
            foreach (var gang in gangs)
                if (!gang.IsPlayer)
                    rivals++;

            if (rivals == 0)
            {
                BuildFamiliesRoom();
                BuildFamiliesHead(0);
                Line(diplomacyContent, LedgerStyle.SerifItalic, 13.3f, LedgerV2.SheetRule,
                    PageLeft, StageTop - 40f, 800f, 26f,
                    "The families have not shown themselves yet.");

                // DEV, editor only: deal a dummy hand of families so the sheet can be
                // seen dressed before the street layer seeds the real ones.
                if (Application.isEditor)
                    LedgerV2.Button(diplomacyContent, "DEAL DUMMY FAMILIES", PageLeft,
                        StageTop - 76f, 220f, 28f, () =>
                        {
                            var underworld = Underworld.Ensure(1987);
                            Gangs.GangRegistry.Install(Gangs.GangSeeder.Generate(
                                1987, underworld.Dealt,
                                gang => underworld.Of(gang)?.Roster));
                        });
                return;
            }

            if (outfit)
                outfit.CollectKnownHoldings(holdings);
            else
                holdings.Clear();

            // A house that has left the ring cannot stay standing on the table.
            if (tableFor >= 0 && !IsRival(gangs, tableFor))
                tableFor = -1;
            if (tableFocus >= 0 && !IsRival(gangs, tableFocus))
                tableFocus = -1;

            BuildFamiliesRoom();
            BuildFamiliesHead(rivals);
            BuildTheTable(gangs);
            BuildFamiliesFoot();
        }

        static bool IsRival(IReadOnlyList<Gangs.Gang> gangs, int gangId)
        {
            for (var i = 0; i < gangs.Count; i++)
                if (gangs[i].Id == gangId && !gangs[i].IsPlayer)
                    return true;
            return false;
        }

        /// <summary>The head and the foot are printed on the desk, not on paper.
        /// </summary>
        static Color HeadInk => LedgerV2.HeadCream;
        static Color HeadFaint => LedgerV2.SheetRule;
        static Color HeadRule => LedgerStyle.InkMid;

        /// <summary>
        /// THE ROOM. This sheet is not paper: it is the desk the table stands on, and
        /// the head and the foot are printed on the desk with it. The design's own two
        /// grounds - a vertical fall from walnut to near-black, and the lamp's pool
        /// hung off the top edge - laid under everything else on the page.
        /// </summary>
        void BuildFamiliesRoom()
        {
            var room = NewRect("Room", diplomacyContent);
            PlaceTopLeft(room, 0f, 0f, SheetW, SheetH);
            Gradient(room, LedgerStyle.DeskFall);
            room.gameObject.AddComponent<RectMask2D>();
            var height = SheetH;

            // radial-gradient(ellipse 90% 60% at 50% 4%, lamp, transparent 72%): the
            // pool is wider than the page and hangs off the top, so what the reader
            // sees is its lower half falling away down the desk.
            var lamp = NewRect("Lamp", room);
            lamp.anchorMin = lamp.anchorMax = new Vector2(0.5f, 1f);
            lamp.pivot = new Vector2(0.5f, 0.5f);
            lamp.sizeDelta = new Vector2(SheetW * 1.30f, height * 0.86f);
            lamp.anchoredPosition = new Vector2(0f, -height * 0.04f);
            var pool = lamp.gameObject.AddComponent<RawImage>();
            pool.texture = LedgerStyle.RadialLight;
            pool.color = LedgerStyle.Lamp;
            pool.raycastTarget = false;
        }

        // ------------------------------------------------------------------- the head

        /// <summary>
        /// The head: what this sheet is, what day it is read on, and the five lines a
        /// standing can be drawn in.
        /// </summary>
        void BuildFamiliesHead(int rivals)
        {
            var day = outfit ? outfit.Campaign.Day : 1;
            var middle = FocusedName();

            var title = Line(diplomacyContent, LedgerStyle.Condensed, 30.1f,
                HeadInk, PageLeft, PageTop, PageWidth - 120f, LineBox(30.1f),
                middle == null ? "THE TABLE" : "THE TABLE · " + middle.ToUpperInvariant());
            title.characterSpacing = 4f;
            title.overflowMode = TextOverflowModes.Ellipsis;

            var sub = Caps(diplomacyContent, PageLeft, PageTopLegend, PageWidth - 460f,
                middle == null
                    ? "DAY " + day + " · OUR STANDING WITH " + Spelled(rivals) +
                      " · FOCUS A CARD TO SEE ITS OWN TABLE"
                    : middle + " IN THE MIDDLE · EVERY LINE IS THEIR STANDING WITH THE " +
                      "OTHER HOUSES · DAY " + day,
                12f, HeadFaint, 8f);
            sub.font = LedgerStyle.Mono;
            sub.overflowMode = TextOverflowModes.Ellipsis;

            // The legend, laid right to left off the margin so the longest word never
            // pushes the first entry off the sheet.
            var legendY = PageTopLegend;
            var boxH = LineBox(BookSize(10.8f));
            var x = PageRight;
            for (var i = FamiliesLegend.Length - 1; i >= 0; i--)
            {
                var entry = FamiliesLegend[i];
                var labelW = MonoWidth(entry.Word, 10.8f, 10f) + 4f;
                x -= labelW;
                Line(diplomacyContent, LedgerStyle.Mono, 10.8f, HeadFaint, x,
                    legendY, labelW, boxH, entry.Word).characterSpacing = 10f;
                x -= 7f + 24f;
                TieRule(diplomacyContent, x,
                    LedgerV2.MarkY(legendY, boxH, TieWeight(entry.Kind)), 24f, entry.Kind);
                x -= 18f;
            }

            Block("Head rule", diplomacyContent, PageLeft, PageTop - FamiliesHeadH + 9f,
                PageWidth, 2f, HeadRule);
        }

        static string Spelled(int houses) => houses switch
        {
            1 => "ONE HOUSE",
            2 => "TWO HOUSES",
            3 => "THREE HOUSES",
            4 => "FOUR HOUSES",
            5 => "FIVE HOUSES",
            6 => "SIX HOUSES",
            _ => houses + " HOUSES",
        };

        /// <summary>The name in the middle of the table, or null when it is ours.
        /// </summary>
        string FocusedName()
        {
            if (tableFocus < 0)
                return null;
            var gangs = Gangs.GangRegistry.Gangs;
            for (var i = 0; i < gangs.Count; i++)
                if (gangs[i].Id == tableFocus)
                    return gangs[i].Name;
            return null;
        }

        readonly struct LegendEntry
        {
            public LegendEntry(TieKind kind, string word)
            {
                Kind = kind;
                Word = word;
            }

            public TieKind Kind { get; }
            public string Word { get; }
        }

        static readonly LegendEntry[] FamiliesLegend =
        {
            new LegendEntry(TieKind.War, "WAR"),
            new LegendEntry(TieKind.Truce, "TRUCE"),
            new LegendEntry(TieKind.Pact, "PACT"),
            new LegendEntry(TieKind.Tribute, "TRIBUTE"),
            new LegendEntry(TieKind.Peace, "PEACE"),
        };

        // ------------------------------------------------------- the five kinds of line

        /// <summary>The one place a standing is turned into ink. Five kinds and no more.
        /// </summary>
        public static Color TieTone(TieKind kind) => kind switch
        {
            TieKind.War => LedgerStyle.RailRed,
            TieKind.Truce => LedgerStyle.RailAmber,
            TieKind.Pact => LedgerStyle.RailGreen,
            TieKind.Tribute => LedgerStyle.RailGold,
            _ => LedgerStyle.RailNote,
        };

        /// <summary>How thick that line is drawn: the design's 3 / 2 / 3 / 2 / 1.
        /// </summary>
        public static float TieWeight(TieKind kind) => kind switch
        {
            TieKind.War => 3f,
            TieKind.Truce => 2f,
            TieKind.Pact => 3f,
            TieKind.Tribute => 2f,
            _ => 1f,
        };

        /// <summary>How that line is broken: solid, dashed, or dotted.</summary>
        public static float TieDash(TieKind kind) => kind switch
        {
            TieKind.Truce => 6f,
            TieKind.Peace => 2f,
            _ => 0f,
        };

        /// <summary>
        /// A specimen of one kind of line, w units long: what the legend prints, and
        /// what a rail row's connector is drawn with. Solid kinds are one block; a
        /// dashed or dotted kind is laid as a run of blocks at its own pitch, because
        /// nothing in this book is allowed to invent a second dash.
        /// </summary>
        public static void TieRule(Transform parent, float x, float y, float w, TieKind kind)
        {
            var tone = TieTone(kind);
            var thickness = TieWeight(kind);
            var dash = TieDash(kind);
            if (dash <= 0f)
            {
                Block("Tie", parent, x, y, w, thickness, tone);
                return;
            }
            var pitch = dash * 2f;
            for (var run = 0f; run < w; run += pitch)
                Block("Dash", parent, x + run, y, Mathf.Min(dash, w - run), thickness, tone);
        }

        // ------------------------------------------------------------------- the foot

        /// <summary>The foot: how to work the sheet, and the last word that left this
        /// room today. The word is the record's own newest line between us and the open
        /// house - never a note the screen invented for itself.</summary>
        void BuildFamiliesFoot()
        {
            var y = PageBottom + FamiliesFootH - 8f;
            Block("Foot rule", diplomacyContent, PageLeft, y, PageWidth, 1f, HeadRule);

            var hint = Caps(diplomacyContent, PageLeft, y - 10f, PageWidth * 0.55f,
                tableFor >= 0
                    ? "TOUCH THE TABLE TO LAY THE CARD BACK DOWN"
                    : "TOUCH A CARD TO STAND IT UP · FOCUS TO PUT A HOUSE IN THE MIDDLE",
                11.4f, HeadFaint, 8f);
            hint.font = LedgerStyle.Mono;
            hint.overflowMode = TextOverflowModes.Ellipsis;

            var last = Line(diplomacyContent, LedgerStyle.SerifItalic, 12.8f,
                HeadFaint, PageLeft + PageWidth * 0.45f, y - 10f,
                PageWidth * 0.55f, LineBox(12.8f),
                string.IsNullOrEmpty(tableLastWord)
                    ? "Nothing has left this room today."
                    : tableLastWord,
                TextAlignmentOptions.MidlineRight);
            last.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>A house's power, 0-100, off the territory runtime's own ledger
        /// (AI-009); negative in a scene with no territory, and the sheet then reads
        /// Unknown rather than inventing a figure the player could not have earned.
        /// </summary>
        static int PowerFigure(int gangId)
        {
            var runtime = RoadDemo.TerritoryRuntime.Instance;
            return runtime != null
                ? runtime.PowerOf(new Territory.TerritoryGangId(gangId))
                : -1;
        }

        /// <summary>The family's map colour, as the coloured dot sticker an office puts
        /// on a file.</summary>
        void Swatch(int gangId, float x, float y, Transform parent = null)
        {
            var rect = NewRect("Swatch", parent ? parent : diplomacyContent);
            PlaceTopLeft(rect, x, y, 16f, 16f);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = LedgerStyle.Disc;
            image.color = GangPalette.Of(gangId);
            image.raycastTarget = false;
        }

        /// <summary>One line of a card's particulars: the label on the left, the answer
        /// held to the right margin over a dotted rule, and a hairline under the pair.
        /// </summary>
        static void CardRow(Transform card, float x, float y, float w, string label,
            string value, Color ink)
        {
            const float labelW = 56f;
            LedgerV2.Mono(card, x, y, labelW, label, 9.5f, LedgerV2.Label, 6f);
            var text = Line(card, LedgerStyle.MonoBold, 12f, ink, x + labelW + 6f, y,
                w - labelW - 6f, LineBox(12f), value,
                TextAlignmentOptions.MidlineRight);
            text.overflowMode = TextOverflowModes.Ellipsis;
            LedgerV2.Leader(card, x, y - 17f, w);
        }
    }
}
