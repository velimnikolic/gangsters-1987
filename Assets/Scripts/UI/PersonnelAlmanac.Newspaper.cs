using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.News;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// THE PAPER: the morning tabloid, folded into the front of the ledger - the whole
    /// sheet, header and all, is newsprint. Masthead, dateline, the lead in condensed
    /// caps with a halftone cut beside it, then the rest of the front page in three
    /// columns. Every headline comes off HeadlineGenerator for this campaign week -
    /// the real 1987 pinned to its dates, the city's own families in the blotter -
    /// and every picture is PortraitStudio's newsprint print of a model the player
    /// can meet in the street.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        const float NewsLeft = 28f;
        const float NewsRight = PaperW - 28f;
        const float NewsWidth = NewsRight - NewsLeft;

        RectTransform newsContent;

        /// <summary>The week the sheet was set for - it is only re-set when the week
        /// turns, because staging two studio photographs per repaint would be waste.</summary>
        int newsPaintedWeek = -1;

        void BuildNewspaperPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Newspaper);

            // The paper lies over the ledger page whole - its own stock, its own grain.
            var stock = NewRect("Newsprint", root);
            Stretch(stock);
            Fill(stock, LedgerStyle.Newsprint);
            Grain(stock, PaperW, PaperH, 1.3f);

            newsContent = NewRect("Edition", root);
            Stretch(newsContent);

            // The other half of the paper - the same sheet of newsprint, turned over.
            BuildClassifiedPage(root);
        }

        void RebuildNewspaper()
        {
            // Which side of the paper is up. Both roots live on the same page, so
            // turning it over costs a SetActive and not a rebuild.
            newsContent.gameObject.SetActive(!classifiedOpen);
            classifiedContent.gameObject.SetActive(classifiedOpen);
            if (classifiedOpen)
            {
                RebuildClassified();
                return;
            }

            var week = outfit ? outfit.Campaign.Week : 1;
            if (week == newsPaintedWeek && newsContent.childCount > 0)
                return;
            newsPaintedWeek = week;

            foreach (Transform old in newsContent)
                Destroy(old.gameObject);

            var date = NewsDate.FromClockDay((week - 1) * 7);
            var seed = director ? director.Seed : 42;
            var stories = HeadlineGenerator.FrontPage(seed, date);

            // ---- masthead ----
            var ear = NewRect("EarLeft", newsContent);
            PlaceTopLeft(ear, NewsLeft, -14f, 118f, 46f);
            Frame(ear, 1f, LedgerStyle.Ink);
            var earText = Text("Text", ear, LedgerStyle.Serif, 10.5f, LedgerStyle.Ink,
                TextAlignmentOptions.Center);
            Stretch(earText.rectTransform, 4f);
            earText.textWrappingMode = TextWrappingModes.Normal;
            earText.text = "FINAL\nCITY EDITION";

            // The right ear is where a paper prints its pointer to the inside pages,
            // and this one points at the men advertising for work. A label-maker tape
            // because it is a VERB: every other one in the book is one too. The cover
            // price it replaces moved into the dateline under the masthead.
            var adsTape = Tape(newsContent, "ADS", NewsRight - 118f, -14f, 118f, 46f,
                () => SetClassified(true), size: 20f);
            adsTape.rectTransform.offsetMin = new Vector2(0f, 14f);
            var earNote = Text("Note", adsTape.transform.parent, LedgerStyle.Condensed,
                10f, LedgerStyle.TapeText, TextAlignmentOptions.Center);
            PlaceTopLeft(earNote.rectTransform, 0f, -30f, 118f, 14f);
            earNote.characterSpacing = 2f;
            earNote.text = "SITUATIONS WANTED";

            var masthead = Line(newsContent, LedgerStyle.SerifBold, 54f, LedgerStyle.Ink,
                NewsLeft, -6f, NewsWidth, 70f, "THE CITY WIRE", TextAlignmentOptions.Center);
            masthead.characterSpacing = 6f;

            Rule(newsContent, NewsLeft, -82f, NewsWidth, LedgerStyle.Ink, 2f);
            var dateline = Line(newsContent, LedgerStyle.SerifItalic, 13f, LedgerStyle.Ink,
                NewsLeft, -86f, NewsWidth, 22f,
                date.Masthead() + "   ·   VOL. LXI, No. " + (week + 3) + "   ·   " +
                "MORNING EDITION   ·   25 CENTS", TextAlignmentOptions.Center);
            dateline.characterSpacing = 1f;
            Rule(newsContent, NewsLeft, -110f, NewsWidth, LedgerStyle.Ink, 3f);

            if (stories.Length == 0)
                return;

            // ---- the lead, with the cut beside it if the day has one ----
            var lead = stories[0];
            var leadPhoto = lead.Photo.HasPicture;
            var leadWidth = leadPhoto ? 572f : NewsWidth;

            var headline = Paragraph(newsContent, LedgerStyle.Condensed, 44f, LedgerStyle.Ink,
                NewsLeft, -122f, leadWidth, 104f, lead.Text, lineSpacing: -8f);
            headline.overflowMode = TextOverflowModes.Overflow;

            var kicker = lead.Historical
                ? DeskName(lead.Desk) + "  ·  FROM THE WIRE"
                : DeskName(lead.Desk) + "  ·  BY A STAFF CORRESPONDENT";
            var kickerText = Line(newsContent, LedgerStyle.Condensed, 12f, LedgerStyle.InkDim,
                NewsLeft, -230f, leadWidth, 18f, kicker);
            kickerText.characterSpacing = 3f;

            Paragraph(newsContent, LedgerStyle.Serif, 15f, LedgerStyle.Ink,
                NewsLeft, -252f, leadWidth, 176f,
                Blurb(lead.Desk, seed + date.DayOfYear) + "\n\n" +
                Blurb(lead.Desk, seed + date.DayOfYear + 1) +
                "  Continued on page 3.", lineSpacing: 6f);

            if (leadPhoto)
                NewsCut(lead, NewsRight - 268f, -122f, 268f, 250f);

            Rule(newsContent, NewsLeft, -436f, NewsWidth, LedgerStyle.Ink);

            // ---- the rest of the front page, three columns ----
            const float gap = 24f;
            var column = (NewsWidth - gap * 2f) / 3f;
            for (var i = 1; i < stories.Length && i < 6; i++)
            {
                var slot = i - 1;
                var col = slot % 3;
                var row = slot / 3;
                var x = NewsLeft + col * (column + gap);
                var y = -448f - row * 262f;
                NewsColumn(stories[i], x, y, column, 250f, seed + date.DayOfYear + i);
                if (col > 0)
                    VRule(newsContent, x - gap * 0.5f, y, 250f, LedgerStyle.InkFaint);
            }

            // The sixth cell: the paper's own furniture - an advertisement and the weather.
            {
                var x = NewsLeft + 2f * (column + gap);
                var y = -448f - 262f;
                VRule(newsContent, x - gap * 0.5f, y, 250f, LedgerStyle.InkFaint);
                var ad = NewRect("Advert", newsContent);
                PlaceTopLeft(ad, x, y, column, 150f);
                Frame(ad, 2f, LedgerStyle.Ink);
                var inner = NewRect("Inner", ad);
                Stretch(inner, 4f);
                Frame(inner, 1f, LedgerStyle.Ink);
                var adHead = Line(ad, LedgerStyle.SerifBold, 17f, LedgerStyle.Ink, 10f, -14f,
                    column - 20f, 26f, "MARLOWE'S", TextAlignmentOptions.Center);
                adHead.characterSpacing = 4f;
                Line(ad, LedgerStyle.Serif, 13f, LedgerStyle.Ink, 10f, -40f, column - 20f, 20f,
                    "FINE TAILORING", TextAlignmentOptions.Center).characterSpacing = 3f;
                Rule(ad, 30f, -64f, column - 60f, LedgerStyle.Ink);
                var adBody = Paragraph(ad, LedgerStyle.SerifItalic, 12.5f, LedgerStyle.Ink,
                    12f, -70f, column - 24f, 70f,
                    "Suits cut for the discreet professional. Wide in the shoulder, " +
                    "quiet in the cloth. Fittings by appointment only.", lineSpacing: 2f);
                adBody.alignment = TextAlignmentOptions.Top;

                var weatherHead = Line(newsContent, LedgerStyle.Condensed, 12f, LedgerStyle.InkDim,
                    x, y - 164f, column, 18f, "WEATHER");
                weatherHead.characterSpacing = 3f;
                Rule(newsContent, x, y - 182f, column, LedgerStyle.InkFaint);
                Paragraph(newsContent, LedgerStyle.Serif, 13f, LedgerStyle.Ink, x, y - 188f,
                    column, 62f, WeatherLine(date), lineSpacing: 2f);
            }
        }

        /// <summary>One story in the grid: kicker, condensed headline, a rule, and
        /// either its halftone cut or a few lines of copy under it.</summary>
        void NewsColumn(Headline story, float x, float y, float w, float h, int salt)
        {
            var kicker = Line(newsContent, LedgerStyle.Condensed, 11f, LedgerStyle.InkDim,
                x, y, w, 16f, story.Historical
                    ? DeskName(story.Desk) + "  ·  WIRE REPORT"
                    : DeskName(story.Desk));
            kicker.characterSpacing = 3f;

            var head = Paragraph(newsContent, LedgerStyle.Condensed, 21f, LedgerStyle.Ink,
                x, y - 18f, w, 66f, story.Text, lineSpacing: -4f);
            head.overflowMode = TextOverflowModes.Overflow;

            Rule(newsContent, x, y - 88f, w, LedgerStyle.InkFaint);

            var bodyTop = y - 94f;
            var bodyHeight = h - 94f;
            if (story.Photo.HasPicture)
            {
                NewsCut(story, x, bodyTop, w, 116f);
                bodyTop -= 150f;
                bodyHeight -= 150f;
            }

            Paragraph(newsContent, LedgerStyle.Serif, 13.5f, LedgerStyle.Ink, x, bodyTop, w,
                bodyHeight, Blurb(story.Desk, salt), lineSpacing: 3f);
        }

        /// <summary>A halftone cut in a hairline frame with its italic caption under.
        /// The print is the studio's newsprint treatment of the story's own model; until
        /// it lands (or when no model resolves) the frame reads WIREPHOTO.</summary>
        void NewsCut(Headline story, float x, float y, float w, float h)
        {
            var frame = NewRect("Cut", newsContent);
            PlaceTopLeft(frame, x, y, w, h);
            Fill(frame, new Color(LedgerStyle.Newsprint.r * 0.92f,
                LedgerStyle.Newsprint.g * 0.92f, LedgerStyle.Newsprint.b * 0.92f));
            Frame(frame, 1f, LedgerStyle.Ink);

            var mark = Text("Mark", frame, LedgerStyle.Condensed, 14f, LedgerStyle.InkDim,
                TextAlignmentOptions.Center);
            Stretch(mark.rectTransform);
            mark.characterSpacing = 8f;
            mark.text = "WIREPHOTO";

            var print = NewRect("Print", frame);
            Stretch(print, 1f);
            var raw = print.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false;
            // A wide slot shows the middle band of the square print - the subject is
            // centred, so a landscape window keeps it whole.
            if (w > h * 1.3f)
            {
                var band = h / w;
                raw.uvRect = new Rect(0f, (1f - band) * 0.5f, 1f, band);
            }

            var photo = story.Photo;
            var model = photo.Subject == PhotoSubject.Vehicle
                ? PortraitStudio.FindVehiclePrefab(photo.ModelName)
                : PortraitStudio.FindPeoplePrefab(photo.ModelName);
            PortraitStudio.Request(model,
                photo.Subject == PhotoSubject.Vehicle
                    ? PortraitStudio.Framing.Vehicle
                    : PortraitStudio.Framing.Bust,
                raw, PortraitStudio.Treatment.Newsprint);

            var caption = Paragraph(newsContent, LedgerStyle.SerifItalic, 12.5f, LedgerStyle.Ink,
                x, y - h - 4f, w, 30f, photo.Caption, lineSpacing: 0f);
            caption.overflowMode = TextOverflowModes.Ellipsis;
        }

        static string DeskName(HeadlineDesk desk) => desk switch
        {
            HeadlineDesk.Crime => "CRIME",
            HeadlineDesk.DrugWar => "THE DRUG WAR",
            HeadlineDesk.Nation => "THE NATION",
            HeadlineDesk.World => "THE WORLD",
            HeadlineDesk.Business => "BUSINESS",
            HeadlineDesk.Culture => "ARTS & LEISURE",
            _ => "CITY DESK",
        };

        /// <summary>The copy under a headline. The generator writes headlines, not
        /// stories, so each desk keeps a few paragraphs of the kind of prose that runs
        /// under any of its heads - chosen by a stable salt so a reloaded save reads
        /// the same paper.</summary>
        static string Blurb(HeadlineDesk desk, int salt)
        {
            var pool = desk switch
            {
                HeadlineDesk.Crime => CrimeCopy,
                HeadlineDesk.DrugWar => DrugWarCopy,
                HeadlineDesk.Nation => NationCopy,
                HeadlineDesk.World => WorldCopy,
                HeadlineDesk.Business => BusinessCopy,
                _ => CultureCopy,
            };
            var index = salt % pool.Length;
            if (index < 0)
                index += pool.Length;
            return pool[index];
        }

        static readonly string[] CrimeCopy =
        {
            "Detectives worked the block past midnight while a crowd stood behind the " +
            "tape. Nobody saw anything, everybody has a theory, and the precinct captain " +
            "has promised arrests before the weekend.",
            "The department is treating the matter as organized, a spokesman said, and " +
            "declined to say more. Sources close to the investigation describe a pattern " +
            "going back months.",
            "Neighbors say the address has kept odd hours since spring. A councilman " +
            "called for more patrols; the precinct says it is stretched thin already.",
        };

        static readonly string[] DrugWarCopy =
        {
            "Federal agents describe the trade as bigger than anything the city has " +
            "seen. Vials sell for five dollars on corners the police no longer walk " +
            "after dark.",
            "The DEA's field office would neither confirm nor deny an operation. " +
            "Community leaders asked for treatment beds; City Hall answered with a task " +
            "force.",
            "Rock cocaine, the officers say, has changed the arithmetic of the street: " +
            "more money, younger hands, and automatic weapons where there used to be " +
            "knives.",
        };

        static readonly string[] NationCopy =
        {
            "In Washington the hearings continue, and the networks carry them live. What " +
            "the country learns each afternoon, the capital pretends to have known " +
            "already.",
            "Prosecutors are calling it the most important case of the decade. Defense " +
            "counsel calls it a show. Both agree it will be a long trial.",
            "The White House had no comment beyond a written statement. Congressional " +
            "aides said the mood on the Hill was, in one word, weary.",
        };

        static readonly string[] WorldCopy =
        {
            "Diplomats in three capitals called the development significant and would " +
            "not say why. Observers expect the story to move again before the month is " +
            "out.",
            "In Moscow the word is reform; in the West the word is caution. Between " +
            "them, the wire carries pictures of men shaking hands.",
            "The foreign desk notes that the region has been quiet before, and that " +
            "quiet there has never lasted. Markets shrugged.",
        };

        static readonly string[] BusinessCopy =
        {
            "Traders described the session as nervous. Volume was heavy, the tape ran " +
            "late, and by the close nobody on the floor would guess at tomorrow.",
            "Analysts say the deal is another sign that debt is cheap and patience is " +
            "not. The firm's chairman was photographed leaving in a limousine and said " +
            "nothing.",
            "Downtown, the money keeps arriving in cash. Bankers call it a boom; the " +
            "tax men call it interesting.",
        };

        static readonly string[] CultureCopy =
        {
            "The record is everywhere this month - car radios, corner boxes, the " +
            "aerobics class at the Y. Critics are divided; the charts are not.",
            "The film opened to lines around the block. Audiences cheered the villain, " +
            "which the studio insists was the idea all along.",
            "Fashion editors report wide shoulders, gold chains, and colors that would " +
            "embarrass a lifeguard. The city has taken it all up without complaint.",
        };

        /// <summary>A season's worth of weather in one line, off the calendar month.</summary>
        static string WeatherLine(NewsDate date) => date.Month switch
        {
            12 or 1 or 2 => "Cold, wind off the river. Snow flurries after dark; " +
                            "the harbor road ices by morning. High 31.",
            3 or 4 => "Rain, clearing late. Fog on the water tonight. High 52.",
            5 or 6 => "Warm and hazy. A chance of thunder toward evening. High 78.",
            7 or 8 => "Hot. Smog over the harbor through the weekend; visibility poor " +
                      "on the water after dark. High 91.",
            _ => "Cool and clear. First frost expected inland. High 58.",
        };
    }
}
