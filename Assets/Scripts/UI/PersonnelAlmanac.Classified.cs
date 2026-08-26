using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Outfit;
using LivingCity.Personnel;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// THE CLASSIFIED: the back of the morning paper, reached by the ADS tape on the
    /// front page. Four men advertise themselves every day - a trade in the head, a
    /// halftone of the face the player will meet in the street if he signs him, his
    /// three best stats in stars, his own copy, and the price.
    ///
    /// The price is quoted BY THE DAY, which is how this city advertises a man, while
    /// the books still pay a week at a time on payday - both figures come off the one
    /// wage table (Wages.PerDay), so the column and the balance sheet can never
    /// disagree. Under it, the signing money: four weeks in his hand before he works a
    /// single one.
    ///
    /// The column itself is HireMarket's - dealt off (city seed, campaign day), so the
    /// same morning always prints the same four men, and turning to the page twice
    /// never re-rolls them.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        /// <summary>True while the paper is open at the classified column rather than
        /// the front page. The ADS tape sets it; FRONT PAGE clears it.</summary>
        bool classifiedOpen;

        RectTransform classifiedContent;

        /// <summary>What the last HIRE click came to - a red line under the rules,
        /// same voice as the armory's note.</summary>
        string classifiedNote = "";

        /// <summary>Market revision and safe, folded into one number: a column that has
        /// not changed and money that has not moved is a page already on the sheet.</summary>
        int classifiedPaintedKey = int.MinValue;

        /// <summary>The note as printed. Compared as a STRING, not as a length - two
        /// refusals can be the same length and different sentences.</summary>
        string classifiedPaintedNote = "\u0000";

        // The column's furniture, in the newsprint sheet's own coordinates. Four
        // boxes across one row: the sheet is wide and short now, so a column of ads
        // two deep would run off the foot of the paper.
        const int AdColumns = 4;
        const float AdGap = 20f;
        const float AdWidth = (NewsWidth - AdGap * (AdColumns - 1)) / AdColumns;
        const float AdHeight = 430f;
        const float AdsTop = -172f;

        void BuildClassifiedPage(RectTransform root)
        {
            classifiedContent = NewRect("Classified", root);
            Stretch(classifiedContent);
            classifiedContent.gameObject.SetActive(false);
        }

        /// <summary>Turns the paper over, both ways. The front page keeps its own paint
        /// key, so coming back to it costs nothing.</summary>
        void SetClassified(bool open)
        {
            classifiedOpen = open;
            classifiedNote = "";
            dirty = true;
        }

        void RebuildClassified()
        {
            var market = director.ColumnToday();
            var safe = outfit ? outfit.Accounts.Safe : 0;

            // The column and the safe are the whole page: nothing else on it can move.
            var key = unchecked(market.Revision * 1000003 + safe);
            if (key == classifiedPaintedKey && classifiedNote == classifiedPaintedNote &&
                classifiedContent.childCount > 0)
                return;
            classifiedPaintedKey = key;
            classifiedPaintedNote = classifiedNote;

            foreach (Transform old in classifiedContent)
                Destroy(old.gameObject);

            var day = outfit ? outfit.Campaign.Day : 1;
            var date = News.NewsDate.FromClockDay(day - 1);

            // ---- the page's own head ----
            var flag = Line(classifiedContent, LedgerStyle.SerifBold, 26f, LedgerStyle.Ink,
                NewsLeft, -12f, 420f, 38f, "THE CITY WIRE");
            flag.characterSpacing = 2f;

            Tape(classifiedContent, "FRONT PAGE", NewsRight - 160f, -14f, 160f, 32f,
                () => SetClassified(false));

            Rule(classifiedContent, NewsLeft, -60f, NewsWidth, LedgerStyle.Ink, 2f);

            var head = Line(classifiedContent, LedgerStyle.Condensed, 34f, LedgerStyle.Ink,
                NewsLeft, -66f, NewsWidth, 44f, "CLASSIFIED  ·  SITUATIONS WANTED",
                TextAlignmentOptions.Center);
            head.characterSpacing = 5f;

            var sub = Line(classifiedContent, LedgerStyle.SerifItalic, 13f, LedgerStyle.Ink,
                NewsLeft, -112f, NewsWidth, 20f,
                date.Masthead() + "   ·   MEN OF ABILITY SEEKING PLACES   ·   " +
                "TERMS BY THE DAY, PAID BY THE WEEK", TextAlignmentOptions.Center);
            sub.characterSpacing = 1f;

            Rule(classifiedContent, NewsLeft, -134f, NewsWidth, LedgerStyle.Ink, 3f);

            Line(classifiedContent, LedgerStyle.MonoBold, 14f, LedgerStyle.Ink,
                NewsRight - 300f, -140f, 300f, 22f, "IN THE SAFE:  " + LedgerText.Cash(safe),
                TextAlignmentOptions.MidlineRight);

            if (classifiedNote.Length > 0)
                Line(classifiedContent, LedgerStyle.MonoItalic, 13.5f, LedgerStyle.RedPen,
                    NewsLeft, -140f, 540f, 22f, classifiedNote);

            // ---- the column ----
            var ads = market.Ads;
            if (ads.Count == 0)
            {
                Line(classifiedContent, LedgerStyle.SerifItalic, 16f, LedgerStyle.InkDim,
                    NewsLeft, AdsTop, NewsWidth, 30f,
                    "Every man in this morning's column has found a place. " +
                    "Try tomorrow's paper.", TextAlignmentOptions.Center);
                return;
            }

            for (var i = 0; i < ads.Count && i < AdColumns; i++)
                BuildAd(ads[i], NewsLeft + i * (AdWidth + AdGap), AdsTop, safe);
        }

        /// <summary>One boxed advertisement. Everything on it is read off the ad at
        /// paint time - HireAd derives its own money from the wage table, so the tape
        /// can never charge a price the box did not print.</summary>
        void BuildAd(HireAd ad, float x, float y, int safe)
        {
            var man = ad.Man;

            var box = NewRect("Ad " + man.Surname, classifiedContent);
            PlaceTopLeft(box, x, y, AdWidth, AdHeight);
            Frame(box, 2f, LedgerStyle.Ink);
            var inner = NewRect("Inner", box);
            Stretch(inner, 4f);
            Frame(inner, 1f, LedgerStyle.Ink);

            var trade = Line(box, LedgerStyle.Condensed, 20f, LedgerStyle.Ink, 12f, -12f,
                AdWidth - 24f, 26f, HireMarket.TradeName(ad.Trade),
                TextAlignmentOptions.Center);
            trade.characterSpacing = 5f;
            Rule(box, 34f, -40f, AdWidth - 68f, LedgerStyle.Ink);

            const float cutW = 200f;
            AdCut(box, man, (AdWidth - cutW) * 0.5f, -50f, cutW, 120f);

            var name = Line(box, LedgerStyle.SerifBold, 17f, LedgerStyle.Ink, 12f, -176f,
                AdWidth - 24f, 24f, man.FullName.ToUpperInvariant(),
                TextAlignmentOptions.Center);
            name.characterSpacing = 1f;
            name.overflowMode = TextOverflowModes.Ellipsis;

            var late = Line(box, LedgerStyle.SerifItalic, 11.5f, LedgerStyle.InkDim, 12f,
                -198f, AdWidth - 24f, 18f,
                "Late of " + Titled(ad.From) + "  ·  " + ad.Box,
                TextAlignmentOptions.Center);
            late.overflowMode = TextOverflowModes.Ellipsis;

            // His three best trades, in stars - the same currency the personal file
            // uses, so a man in the paper reads against a man on the books at a glance.
            var y0 = -222f;
            for (var slot = 0; slot < 3; slot++)
            {
                var attribute = NthBest(man, slot);
                Line(box, LedgerStyle.Serif, 12.5f, LedgerStyle.Ink, 14f, y0 - slot * 22f,
                    150f, 20f, LedgerText.AttributeLabel(attribute));
                Stars(box, 172f, y0 - slot * 22f - 10f, man.GetHalfSteps(attribute),
                    15f, 16f);
            }

            var copy = Paragraph(box, LedgerStyle.Serif, 12.5f, LedgerStyle.Ink, 14f, -292f,
                AdWidth - 28f, 52f, "\u201C" + HireMarket.Pitch(ad.Trade) + "\u201D",
                lineSpacing: 3f);
            copy.alignment = TextAlignmentOptions.Top;
            copy.overflowMode = TextOverflowModes.Ellipsis;

            // The terms sit ABOVE the price rule, not under the price: the box's own
            // frame is drawn over its bottom edge, and a line laid against it is a line
            // cut in half.
            Line(box, LedgerStyle.Mono, 11f, LedgerStyle.InkDim, 14f, -350f,
                AdWidth - 28f, 16f,
                LedgerText.Cash(ad.Weekly) + " the week  \u00B7  " + LedgerText.Cash(ad.Down) +
                " down");

            Rule(box, 14f, -370f, AdWidth - 28f, LedgerStyle.Ink);

            // The price the column is read for: what he costs A DAY.
            var price = Line(box, LedgerStyle.Condensed, 24f, LedgerStyle.Ink, 14f, -374f,
                AdWidth - 130f, LineBox(24f), LedgerText.Cash(ad.Daily) + " A DAY");
            price.characterSpacing = 2f;

            var captured = ad;
            var hire = Tape(box, "HIRE", AdWidth - 112f, -378f, 98f, 32f, () =>
            {
                var result = director.HireFromAd(captured, out var newId);
                if (result.Ok)
                {
                    var hired = director.Roster != null ? director.Roster.Find(newId) : null;
                    classifiedNote = (hired != null ? hired.FullName : "The man") +
                                     " signed - he runs his own crew from today.";
                }
                else
                    classifiedNote = result.Reason;
                dirty = true;
            }, size: 13f);

            // Short money reads at a glance; the click still spells out how short.
            if (safe < ad.Down)
                ButtonOf(hire).targetGraphic.color = new Color(0.45f, 0.42f, 0.38f);
        }

        /// <summary>The ad's halftone: the studio's newsprint print of the very body
        /// this man will wear in the street, on the same hatched plate every other
        /// picture in the book stands on until its print lands.</summary>
        void AdCut(RectTransform parent, Character man, float x, float y, float w, float h)
        {
            var raw = Plate(parent, x, y, w, h, "PRESS PHOTO",
                new Color(LedgerStyle.Newsprint.r * 0.94f, LedgerStyle.Newsprint.g * 0.94f,
                    LedgerStyle.Newsprint.b * 0.94f));

            // The studio's prints are square and this slot is wider than it is tall:
            // show the middle band rather than stretching the man in it.
            if (w > h)
            {
                var band = h / w;
                raw.uvRect = new Rect(0f, (1f - band) * 0.5f, 1f, band);
            }

            PortraitStudio.Request(MemberModel(man), PortraitStudio.Framing.Bust, raw,
                PortraitStudio.Treatment.Newsprint);
        }

        /// <summary>His nth-best attribute, ties broken by attribute order - the same
        /// order the personnel card lists them in, so the two pages agree.</summary>
        static CharacterAttribute NthBest(Character man, int rank)
        {
            var best = CharacterAttribute.Intelligence;
            var taken = 0;

            for (var pick = 0; pick <= rank; pick++)
            {
                var bestValue = int.MinValue;
                best = CharacterAttribute.Intelligence;
                for (var a = 0; a < AttributeScale.Count; a++)
                {
                    if ((taken & (1 << a)) != 0)
                        continue;
                    var value = man.GetHalfSteps((CharacterAttribute)a);
                    if (value > bestValue)
                    {
                        bestValue = value;
                        best = (CharacterAttribute)a;
                    }
                }
                taken |= 1 << (int)best;
            }

            return best;
        }

        /// <summary>"HARBOR ROW" as the paper's body copy would set it - the column's
        /// headings shout, its sentences do not.</summary>
        static string Titled(string caps)
        {
            if (string.IsNullOrEmpty(caps))
                return "";

            var chars = caps.ToCharArray();
            var startOfWord = true;
            for (var i = 0; i < chars.Length; i++)
            {
                if (chars[i] == ' ')
                {
                    startOfWord = true;
                    continue;
                }
                if (!startOfWord)
                    chars[i] = char.ToLowerInvariant(chars[i]);
                startOfWord = false;
            }
            return new string(chars);
        }
    }
}
