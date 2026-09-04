using System.Collections.Generic;
using LivingCity.News;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>The single, runtime compositor used by the ledger archive and the 06:00 popup.
    /// It owns the printed sheet; its callers supply only controls around the edition.</summary>
    public static class NewspaperSheet
    {
        public sealed class Controls
        {
            public UnityAction Save;
            public UnityAction Load;
            public UnityAction Ads;
            public UnityAction Close;
            public UnityAction Previous;
            public UnityAction Next;
            public bool HasPrevious;
            public bool HasNext;
            public bool Archive;
        }

        public static Headline[] Paint(RectTransform root, float width, float height,
            int seed, int editionDay, IReadOnlyList<PressRecord> book,
            IReadOnlyList<string> districts, Controls controls = null)
        {
            foreach (Transform old in root)
                Object.Destroy(old.gameObject);

            var paper = NewRect("Newsprint", root);
            Stretch(paper);
            Fill(paper, LedgerV2.Panel);
            Grain(paper, width, height, 0.18f);

            var date = NewsDate.FromClockDay(Mathf.Max(0, editionDay - 1));
            var stories = Edition.Compose(seed, date, editionDay, book, districts);
            const float pad = 24f;
            var left = pad;
            var right = width - pad;
            var contentW = right - left;

            PaintControls(paper, width, editionDay, controls);

            var masthead = Line(paper, LedgerStyle.SerifBold, 48f, LedgerV2.Ink,
                left, -8f, contentW, 62f, "THE CITY WIRE", TextAlignmentOptions.Center);
            masthead.characterSpacing = 6f;
            Rule(paper, left, -74f, contentW, LedgerV2.Ink, 2f);
            var dateline = Line(paper, LedgerStyle.SerifItalic, 13f, LedgerV2.Ink,
                left, -80f, contentW, 20f,
                date.Masthead() + "   ·   VOL. LXI, No. " +
                ((editionDay - 1) % LivingCity.Outfit.Campaign.DaysPerYear + 1) +
                "   ·   MORNING EDITION   ·   25 CENTS", TextAlignmentOptions.Center);
            dateline.characterSpacing = 1f;
            Rule(paper, left, -102f, contentW, LedgerV2.Ink, 3f);

            if (stories.Length == 0)
                return stories;

            var briefH = Mathf.Max(180f, (height - 110f) * 0.35f);
            var briefTop = -(height - 24f - briefH);
            var leadBottom = briefTop + 15f;
            PaintLead(paper, stories[0], left, right, leadBottom,
                seed + date.DayOfYear);
            Rule(paper, left, leadBottom, contentW, LedgerV2.Ink, 2f);

            var columns = Mathf.Max(4, Mathf.FloorToInt((contentW + 18f) / 225f));
            var shown = Mathf.Min(stories.Length - 1, columns);
            var gap = 20f;
            var columnW = (contentW - gap * (columns - 1)) / columns;
            for (var i = 0; i < shown; i++)
            {
                var x = left + i * (columnW + gap);
                if (i > 0)
                    VRule(paper, x - gap * 0.5f, briefTop, briefH, LedgerV2.Rule);
                PaintBrief(paper, stories[i + 1], x, briefTop, columnW, briefH,
                    seed + date.DayOfYear + i + 1);
            }

            // Wide sheets may have a spare column after all six stories. It remains
            // newspaper furniture, not a fabricated city report.
            if (shown < columns)
            {
                var x = left + shown * (columnW + gap);
                if (shown > 0)
                    VRule(paper, x - gap * 0.5f, briefTop, briefH, LedgerV2.Rule);
                Caps(paper, x, briefTop, columnW, "WEATHER", 10.5f,
                    LedgerV2.Muted, 4f);
                Rule(paper, x, briefTop - 20f, columnW, LedgerV2.Rule);
                Paragraph(paper, LedgerStyle.Serif, 13f, LedgerV2.Ink, x,
                    briefTop - 30f, columnW, briefH - 36f, Weather(date), 3f);
            }

            return stories;
        }

        static void PaintControls(RectTransform paper, float width, int day,
            Controls controls)
        {
            if (controls == null)
                return;
            if (controls.Save != null)
                LedgerV2.Button(paper, "SAVE", 24f, -14f, 92f, 21f,
                    controls.Save, LedgerV2.Key.Outline, 9f);
            if (controls.Load != null)
                LedgerV2.Button(paper, "LOAD", 24f, -39f, 92f, 21f,
                    controls.Load, LedgerV2.Key.Red, 9f);
            if (controls.Ads != null)
                LedgerV2.Button(paper, "ADS", width - 116f, -14f, 92f, 46f,
                    controls.Ads, LedgerV2.Key.Dark, 16f);
            if (controls.Close != null)
                LedgerV2.Button(paper, "X", width - 76f, -14f, 52f, 46f,
                    controls.Close, LedgerV2.Key.Outline, 18f);

            if (!controls.Archive)
                return;
            var centre = width * 0.5f;
            var previous = LedgerV2.Button(paper, "<", centre - 156f, -12f,
                38f, 24f, controls.Previous, LedgerV2.Key.Outline, 12f);
            var next = LedgerV2.Button(paper, ">", centre + 118f, -12f,
                38f, 24f, controls.Next, LedgerV2.Key.Outline, 12f);
            LedgerV2.KeyEnabled(previous, controls.HasPrevious);
            LedgerV2.KeyEnabled(next, controls.HasNext);
            var stamp = Caps(paper, centre - 112f, -11f, 224f,
                "BACK ISSUES · DAY " + day, 9f, LedgerV2.Muted, 3f,
                TextAlignmentOptions.Center);
            stamp.font = LedgerStyle.MonoBold;
        }

        static void PaintLead(RectTransform paper, Headline story, float left,
            float right, float bottom, int salt)
        {
            const float top = -114f;
            const float gap = 28f;
            var total = right - left;
            var hasPhoto = story.Photo.HasPicture;
            var cutW = hasPhoto ? Mathf.Clamp(total * 0.31f, 260f, 440f) : 0f;
            var copyW = total - (hasPhoto ? cutW + gap : 0f);

            var head = Paragraph(paper, LedgerStyle.Condensed, 43f, LedgerV2.Ink,
                left, top, copyW, 108f, story.Text, -8f);
            head.overflowMode = TextOverflowModes.Overflow;
            var headH = Mathf.Max(48f, head.preferredHeight);
            var kickerY = top - headH - 8f;
            Caps(paper, left, kickerY, copyW,
                Desk(story.Desk) + (story.Historical
                    ? " · FROM THE WIRE" : " · STAFF REPORT"),
                10.5f, LedgerV2.Muted, 4f);
            var bodyY = kickerY - 24f;
            var bodyH = Mathf.Max(54f, bodyY - bottom - 12f);
            var copy = Body(story, salt);
            if (copyW > 620f)
            {
                var column = (copyW - 22f) * 0.5f;
                SplitColumns(copy, out var first, out var second);
                Paragraph(paper, LedgerStyle.Serif, 14.5f, LedgerV2.Ink,
                    left, bodyY, column, bodyH, first, 5f);
                Paragraph(paper, LedgerStyle.Serif, 14.5f, LedgerV2.Ink,
                    left + column + 22f, bodyY, column, bodyH,
                    second, 5f);
            }
            else
            {
                Paragraph(paper, LedgerStyle.Serif, 14.5f, LedgerV2.Ink,
                    left, bodyY, copyW, bodyH, copy, 5f);
            }

            if (hasPhoto)
                PaintCut(paper, story, right - cutW, top, cutW,
                    Mathf.Clamp(-bottom + top - 30f, 180f, cutW * 1.1f));
        }

        static void PaintBrief(RectTransform paper, Headline story, float x,
            float y, float width, float height, int salt)
        {
            Caps(paper, x, y, width, Desk(story.Desk) +
                (story.Historical ? " · WIRE REPORT" : ""),
                10f, LedgerV2.Muted, 3f);
            var head = Paragraph(paper, LedgerStyle.Condensed, 20f, LedgerV2.Ink,
                x, y - 18f, width, LineBox(20f, 2), story.Text, -4f);
            head.overflowMode = TextOverflowModes.Ellipsis;
            Rule(paper, x, y - 86f, width, LedgerV2.Rule);
            var bodyTop = y - 94f;
            var bodyH = height - 98f;
            if (story.Photo.HasPicture && bodyH > 150f)
            {
                PaintCut(paper, story, x, bodyTop, width, 70f);
                bodyTop -= 96f;
                bodyH -= 96f;
            }
            Paragraph(paper, LedgerStyle.Serif, 12.5f, LedgerV2.Ink,
                x, bodyTop, width, bodyH, Body(story, salt), 3f);
        }

        static void PaintCut(RectTransform paper, Headline story, float x,
            float y, float width, float height)
        {
            var raw = LedgerV2.PortraitPlate(paper, x, y, width, height,
                "PRESS PHOTO", LedgerV2.PanelInset);
            var photo = story.Photo;
            var model = photo.Subject == PhotoSubject.Vehicle
                ? PortraitStudio.FindVehiclePrefab(photo.ModelName)
                : PortraitStudio.FindPeoplePrefab(photo.ModelName);
            PortraitStudio.Request(model,
                photo.Subject == PhotoSubject.Vehicle
                    ? PortraitStudio.Framing.Vehicle : PortraitStudio.Framing.Bust,
                raw, PortraitStudio.Treatment.Newsprint);
            var caption = Paragraph(paper, LedgerStyle.SerifItalic, 11.5f,
                LedgerV2.Ink, x, y - height - 4f, width, 28f,
                photo.Caption ?? "", 0f);
            caption.overflowMode = TextOverflowModes.Ellipsis;
        }

        public static List<string> CityQuarters()
        {
            var names = new List<string>();
            var geography = RoadDemo.TerritoryRuntime.Instance?.Geography;
            var ids = geography?.NeighborhoodIds;
            for (var i = 0; ids != null && i < ids.Count; i++)
                if (geography.TryGetNeighborhood(ids[i], out var hood) &&
                    hood != null && !string.IsNullOrWhiteSpace(hood.Name) &&
                    !names.Contains(hood.Name))
                    names.Add(hood.Name);
            return names;
        }

        static string Body(Headline story, int salt) =>
            !string.IsNullOrWhiteSpace(story.Blurb)
                ? story.Blurb : Filler(story.Desk, salt);

        static void SplitColumns(string copy, out string first, out string second)
        {
            copy ??= "";
            var split = copy.IndexOf(". ", System.StringComparison.Ordinal);
            if (split >= 0 && split + 2 < copy.Length)
            {
                first = copy.Substring(0, split + 1).Trim();
                second = copy.Substring(split + 2).Trim();
                return;
            }

            split = copy.LastIndexOf(' ', copy.Length / 2);
            if (split <= 0)
            {
                first = copy;
                second = "";
                return;
            }
            first = copy.Substring(0, split).Trim();
            second = copy.Substring(split + 1).Trim();
        }

        static string Desk(HeadlineDesk desk) => desk switch
        {
            HeadlineDesk.City => "CITY DESK",
            HeadlineDesk.Courts => "THE COURTS",
            HeadlineDesk.Crime => "CRIME",
            HeadlineDesk.DrugWar => "THE DRUG WAR",
            HeadlineDesk.Nation => "THE NATION",
            HeadlineDesk.World => "THE WORLD",
            HeadlineDesk.Business => "BUSINESS",
            _ => "ARTS & LEISURE",
        };

        static string Filler(HeadlineDesk desk, int salt)
        {
            var copy = desk switch
            {
                HeadlineDesk.Crime => Crime,
                HeadlineDesk.DrugWar => Drugs,
                HeadlineDesk.Nation => Nation,
                HeadlineDesk.World => World,
                HeadlineDesk.Business => Business,
                _ => Culture,
            };
            return copy[Mathf.Abs(salt) % copy.Length];
        }

        static readonly string[] Crime =
        {
            "Detectives worked the block past midnight while a crowd stood behind the tape. The precinct captain promised arrests before the weekend.",
            "The department is treating the matter as organized, a spokesman said. Sources describe a pattern going back months.",
        };
        static readonly string[] Drugs =
        {
            "Federal agents describe the trade as larger than anything the city has seen. Vials sell for five dollars on corners after dark.",
            "The field office declined to discuss an operation. Community leaders asked City Hall for treatment beds.",
        };
        static readonly string[] Nation =
        {
            "In Washington the hearings continue, and the networks carry them live. The capital offered no further comment.",
            "Prosecutors call it the most important case of the decade. Defense counsel calls it a show.",
        };
        static readonly string[] World =
        {
            "Diplomats in three capitals called the development significant. Observers expect the story to move again before month's end.",
            "In Moscow the word is reform; in the West the word is caution. The wire carries pictures of men shaking hands.",
        };
        static readonly string[] Business =
        {
            "Traders described the session as nervous. Volume was heavy and the tape ran late.",
            "Analysts say the deal is another sign that debt is cheap and patience is not.",
        };
        static readonly string[] Culture =
        {
            "The record is everywhere this month: car radios, corner boxes and the aerobics class at the Y.",
            "The film opened to lines around the block. Audiences cheered the villain.",
        };

        static string Weather(NewsDate date) => date.Month switch
        {
            12 or 1 or 2 => "Cold, wind off the river. Snow flurries after dark; the harbor road ices by morning. High 31.",
            3 or 4 => "Rain, clearing late. Fog on the water tonight. High 52.",
            5 or 6 => "Warm and hazy. A chance of thunder toward evening. High 78.",
            7 or 8 => "Hot. Smog over the harbor through the weekend. High 91.",
            _ => "Cool and clear. First frost expected inland. High 58.",
        };
    }
}
