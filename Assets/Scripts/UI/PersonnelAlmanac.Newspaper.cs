using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.News;
using LedgerOutfit = LivingCity.Outfit;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// THE PAPER: the morning tabloid, folded into the front of the ledger and laid on
    /// the sheet under the file's own masthead. Its own flag, its own dateline band,
    /// the lead in condensed caps with a press cut beside it and its copy set in two
    /// columns, then a row of briefs across the foot. Every headline comes off
    /// HeadlineGenerator for this campaign day - the real 1987 pinned to its dates, the
    /// city's own families in the blotter - and every picture is PortraitStudio's
    /// newsprint print of a model the player can meet in the street.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        /// <summary>The newsprint is a sheet laid ON the ledger page, not the page
        /// itself: the rail's readouts and the telex strip stay printed round it, the
        /// way the design has the boss reading the paper on top of his own book.
        ///
        /// Measured, not const: the sheet is full bleed and takes whatever the window
        /// leaves, so every column below is struck in MeasureNewspaperLayout.</summary>
        static float NewsH = -(PageBottom - PageTop);
        const float NewsPad = 24f;
        const float NewsLeft = NewsPad;
        static float NewsRight = PageWidth - NewsPad;
        static float NewsWidth = NewsRight - NewsLeft;

        /// <summary>The briefs across the foot - the design's auto-fit at a 230-unit
        /// minimum, which comes out at five columns on a 16:9 sheet and more on a
        /// wider one.</summary>
        const float BriefGap = 22f;
        const float BriefMin = 230f;
        static int BriefColumns = 5;
        static float BriefW = (NewsWidth - BriefGap * (BriefColumns - 1)) / BriefColumns;
        static float BriefTop = -420f;
        static float BriefH = 194f;

        /// <summary>The flag: the ears, the masthead and the dateline over their rule.
        /// Fixed - a masthead is the same size on a broadsheet and a tabloid.</summary>
        const float NewsFlagH = 102f;

        /// <summary>What the briefs take of the sheet under the flag. A front page gives
        /// its foot about a third, and never less than the row was drawn for.</summary>
        const float BriefShare = 0.34f;

        /// <summary>Where the lead's deck and its press cut start, under the flag.</summary>
        const float CutTop = -112f;

        static void MeasureNewspaperLayout()
        {
            NewsH = -(PageBottom - PageTop);
            NewsRight = PageWidth - NewsPad;
            NewsWidth = NewsRight - NewsLeft;
            // The design's grid is repeat(auto-fit, minmax(230px, 1fr)): as many columns
            // as fit at the minimum, and never fewer than the five the copy is written
            // for. A wider window earns another brief rather than five wider ones.
            BriefColumns = Mathf.Max(5,
                Mathf.FloorToInt((NewsWidth + BriefGap) / (BriefMin + BriefGap)));
            BriefW = (NewsWidth - BriefGap * (BriefColumns - 1)) / BriefColumns;
            // The foot takes its share of whatever height the window gave the sheet, and
            // the lead takes the rest. A full-bleed page must FILL: a front page that
            // stops two thirds down reads as one that failed to print.
            BriefH = Mathf.Max(194f, (NewsH - NewsFlagH) * BriefShare);
            BriefTop = -(NewsH - 24f - BriefH);
            LeadRuleY = BriefTop + 14f;
            MeasureClassifiedLayout();
        }

        /// <summary>The rule that closes the lead and opens the briefs.</summary>
        static float LeadRuleY = -406f;

        RectTransform newsContent;

        /// <summary>The last word from the file desk. It runs on the ledger's telex
        /// rather than being painted into the edition, so saving does not reset the
        /// morning paper just to acknowledge the click.</summary>
        string newspaperNote = "";

        /// <summary>The day the sheet was set for - it is only re-set when the day
        /// turns, because staging two studio photographs per repaint would be waste.</summary>
        int newsPaintedDay = -1;
        int newsEditionDay = -1;
        int newsPaintedEdition = -1;
        int newsPaintedLastEdition = -1;
        int newsPaintedPressVersion = -1;

        /// <summary>STREET TALK's own repaint key (EPIC 40): the player's event book
        /// and connection paper move on their own versions, the press book's aside.</summary>
        int newsPaintedStreet = -1;

        static int StreetVersion()
        {
            var runner = LivingCity.Outfit.Underworld.Current?.Player?.Runner;
            if (runner == null)
                return 0;
            return runner.Events.Version * 7919 + runner.Connection.Version;
        }

        void BuildNewspaperPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Newspaper);

            // The paper as an object on the desk: greyer, colder stock than the ledger's,
            // with its own shadow where it overhangs the file.
            var stock = LedgerV2.Card("Newsprint", root, PageLeft, PageTop, PageWidth, NewsH);

            newsContent = NewRect("Edition", stock);
            Stretch(newsContent);

            // The other half of the paper - the same sheet, turned over.
            BuildClassifiedPage(stock);
        }

        void RebuildNewspaper()
        {
            // Which side of the paper is up. Both roots live on the same sheet, so
            // turning it over costs a SetActive and not a rebuild.
            newsContent.gameObject.SetActive(!classifiedOpen);
            classifiedContent.gameObject.SetActive(classifiedOpen);
            if (classifiedOpen)
            {
                RebuildClassified();
                return;
            }

            // Both the archive and the 06:00 popup go through the one compositor. The
            // older private helpers below remain as a safe fallback while this partial
            // class is loaded by old scene data, but production always takes this door.
            var useSharedPainter = true;
            if (useSharedPainter)
            {
                var currentDay = outfit ? outfit.Campaign.Day : 1;
                var press = LivingCity.Outfit.Underworld.Current?.Press;
                var latest = LatestPressEdition(press, currentDay);
                if (newsEditionDay < 1 || currentDay != newsPaintedDay)
                    newsEditionDay = latest;
                else if (latest > newsPaintedLastEdition &&
                         newsEditionDay == newsPaintedLastEdition)
                    newsEditionDay = latest;
                if (newsEditionDay > latest)
                    newsEditionDay = latest;
                var version = press != null ? press.Version : 0;
                var street = StreetVersion();
                if (currentDay == newsPaintedDay &&
                    newsEditionDay == newsPaintedEdition &&
                    latest == newsPaintedLastEdition &&
                    version == newsPaintedPressVersion &&
                    street == newsPaintedStreet && newsContent.childCount > 0)
                    return;
                newsPaintedStreet = street;

                newsPaintedDay = currentDay;
                newsPaintedEdition = newsEditionDay;
                newsPaintedLastEdition = latest;
                newsPaintedPressVersion = version;
                var earliest = EarliestPressEdition(press, latest);
                var citySeed = director ? director.Seed : 42;
                NewspaperSheet.Paint(newsContent, PageWidth, NewsH, citySeed,
                    newsEditionDay, press?.Records, NewspaperSheet.CityQuarters(),
                    new NewspaperSheet.Controls
                    {
                        Save = SaveCampaignFromLedger,
                        Load = LoadCampaignFromLedger,
                        Ads = () => SetClassified(true),
                        Archive = true,
                        HasPrevious = newsEditionDay > earliest,
                        HasNext = newsEditionDay < latest,
                        Previous = PreviousPressEdition,
                        Next = NextPressEdition,
                    });
                if (newsEditionDay == latest)
                    BuildStreetTalk(newsContent);
                return;
            }

            var day = outfit ? outfit.Campaign.Day : 1;
            if (day == newsPaintedDay && newsContent.childCount > 0)
                return;
            newsPaintedDay = day;

            foreach (Transform old in newsContent)
                Destroy(old.gameObject);

            var date = NewsDate.FromClockDay(day - 1);
            var seed = director ? director.Seed : 42;
            var stories = HeadlineGenerator.FrontPage(seed, date);

            // ---- the flag ----
            // The file desk lives in the left ear: two rows in the newspaper's own
            // outlined-key hand, always on the front page. Both use the same autosave
            // file as midnight and the terminal verbs, so the player never has a
            // second, subtly different save path.
            LedgerV2.Button(newsContent, "SAVE", NewsLeft, -14f, 118f, 21f,
                SaveCampaignFromLedger, LedgerV2.Key.Outline, 9.5f);
            LedgerV2.Button(newsContent, "LOAD", NewsLeft, -39f, 118f, 21f,
                LoadCampaignFromLedger, LedgerV2.Key.Red, 9.5f);

            // The right ear is where a paper prints its pointer to the inside pages,
            // and this one points at the men advertising for work.
            var adsTape = LedgerV2.Button(newsContent, "ADS", NewsRight - 118f, -14f, 118f, 46f,
                () => SetClassified(true), red: false, size: 19f);
            adsTape.rectTransform.offsetMin = new Vector2(0f, 14f);
            var earNote = Text("Note", adsTape.transform.parent, LedgerStyle.Condensed,
                10f, LedgerV2.HeadCream, TextAlignmentOptions.Center);
            PlaceTopLeft(earNote.rectTransform, 0f, -30f, 118f, 14f);
            earNote.characterSpacing = 3f;
            earNote.text = "SITUATIONS WANTED";

            var masthead = Line(newsContent, LedgerStyle.SerifBold, 48f, LedgerV2.Ink,
                NewsLeft, -8f, NewsWidth, 62f, "THE CITY WIRE", TextAlignmentOptions.Center);
            masthead.characterSpacing = 6f;

            Rule(newsContent, NewsLeft, -74f, NewsWidth, LedgerV2.Ink, 2f);
            var dateline = Line(newsContent, LedgerStyle.SerifItalic, 13f, LedgerV2.Ink,
                NewsLeft, -80f, NewsWidth, 20f,
                date.Masthead() + "   ·   VOL. LXI, No. " +
                ((day - 1) % Outfit.Campaign.DaysPerYear + 1) +
                "   ·   MORNING EDITION   ·   25 CENTS", TextAlignmentOptions.Center);
            dateline.characterSpacing = 1f;
            Rule(newsContent, NewsLeft, -102f, NewsWidth, LedgerV2.Ink, 3f);

            if (stories.Length == 0)
                return;

            BuildLead(stories[0], seed + date.DayOfYear);

            Rule(newsContent, NewsLeft, LeadRuleY, NewsWidth, LedgerV2.Ink, 2f);

            // ---- the briefs, one row across the foot ----
            //
            // The outfit's own night is RESERVED a column before the wire is let at the
            // row. It is not the paper's furniture - it is the one column on this page
            // the player came for - and letting the wire fill first made it the first
            // thing a narrower window dropped, silently and without a test to catch it.
            var ourOwn = outfit && outfit.LastNight.Count > 0;
            var wireColumns = ourOwn ? Mathf.Max(1, BriefColumns - 1) : BriefColumns;

            var slot = 0;
            for (var i = 1; i < stories.Length && slot < wireColumns; i++, slot++)
            {
                var x = NewsLeft + slot * (BriefW + BriefGap);
                NewsColumn(stories[i], x, BriefTop, BriefW, BriefH,
                    seed + date.DayOfYear + i);
                if (slot > 0)
                    VRule(newsContent, x - BriefGap * 0.5f, BriefTop, BriefH,
                        LedgerV2.Rule);
            }

            // What the outfit's own men did last night, in the column held for it.
            if (ourOwn && slot < BriefColumns)
            {
                var x = NewsLeft + slot * (BriefW + BriefGap);
                if (slot > 0)
                    VRule(newsContent, x - BriefGap * 0.5f, BriefTop, BriefH,
                        LedgerV2.Rule);
                BuildOurOwn(x, BriefTop, BriefW);
                slot++;
            }

            // The paper's own furniture fills whatever the wire left empty - an
            // advertisement, then the weather.
            if (slot < BriefColumns)
            {
                var x = NewsLeft + slot * (BriefW + BriefGap);
                if (slot > 0)
                    VRule(newsContent, x - BriefGap * 0.5f, BriefTop, BriefH,
                        LedgerV2.Rule);
                BuildAdvert(x, BriefTop, BriefW);
                slot++;
            }
            if (slot < BriefColumns)
            {
                var x = NewsLeft + slot * (BriefW + BriefGap);
                if (slot > 0)
                    VRule(newsContent, x - BriefGap * 0.5f, BriefTop, BriefH,
                        LedgerV2.Rule);
                Caps(newsContent, x, BriefTop, BriefW, "WEATHER", 11f,
                    LedgerV2.Muted, 4f);
                Rule(newsContent, x, BriefTop - 18f, BriefW, LedgerV2.Rule);
                Paragraph(newsContent, LedgerStyle.Serif, 13f, LedgerV2.Ink, x,
                    BriefTop - 26f, BriefW, BriefH - 30f, WeatherLine(date), lineSpacing: 3f);
            }
        }

        /// <summary>
        /// STREET TALK (EPIC 40, STREET-002): the last column of the paper's foot, on
        /// today's edition only. Every number the street computes for the house, in
        /// words: the card on the table and what holds it, each signal and its state,
        /// the gate when shut, how full the pot is, the broker's door and its watch,
        /// whose the line is, and the last lines the wire brought. The same words the
        /// probe prints.
        /// </summary>
        void BuildStreetTalk(RectTransform root)
        {
            var x = NewsLeft + (BriefColumns - 1) * (BriefW + BriefGap);
            var y = BriefTop;
            var w = BriefW;
            var h = BriefH;

            var box = NewRect("Street talk", root);
            PlaceTopLeft(box, x, y, w, h);
            Fill(box, LedgerV2.PanelDark);
            Caps(box, 8f, 0f, w - 16f, "STREET TALK", 11f, LedgerV2.Ink, 4f);
            Rule(box, 8f, -18f, w - 16f, LedgerV2.Ink);

            var line = -24f;
            void Say(string text, Color ink, float size = 9.5f, int lines = 1)
            {
                if (string.IsNullOrEmpty(text) || line < -h + 12f)
                    return;
                var box_ = LineBox(size, lines);
                if (lines > 1)
                    Paragraph(box, LedgerStyle.Mono, size, ink, 8f, line, w - 16f, box_, text, 1f);
                else
                    LedgerV2.Mono(box, 8f, line, w - 16f, text, size, ink, 0f);
                line -= box_ - 2f;
            }

            var underworld = LivingCity.Outfit.Underworld.Current;
            var runtime = RoadDemo.TerritoryRuntime.Instance;
            var house = underworld?.Player;
            if (house == null || runtime == null)
            {
                Say("No street in this scene.", LedgerV2.Muted);
                return;
            }
            var card = runtime.PlayerCard(out var hold);
            var ctx = runtime.PlayerContext();
            var view = runtime.Peek(house);
            var book = house.Runner.Events;
            var paper = house.Runner.Connection;
            var day = outfit ? outfit.Campaign.Day : 1;
            if (view == null || ctx == null)
            {
                Say("The street has not been read yet.", LedgerV2.Muted);
                return;
            }

            // The card on the table, and what holds it.
            if (card != null)
            {
                Say("PENDING · " + card.Title + " · " + card.SpeakerName.ToUpperInvariant(),
                    LedgerV2.Red, 9.5f);
                Say(hold != LedgerOutfit.HoldReason.None
                        ? LedgerOutfit.HoldReasons.Line(hold) + " - " +
                          LedgerOutfit.HoldReasons.Clears(hold)
                        : "He waits until day " + card.ExpiresDay + ".",
                    LedgerV2.Body, 9f, 2);
                LedgerV2.Button(box, "THE PHONE", 8f, line, 96f, 20f, () =>
                {
                    Close();
                    EventCardHud.Instance?.Reopen();
                }, LedgerV2.Key.Dark, 9f);
                line -= 26f;
            }

            // The def nearest its threshold: every signal, the gate, the pot.
            LedgerOutfit.EventDef nearest = null;
            var defs = LedgerOutfit.ConnectionEvents.Defs;
            for (var i = 0; i < defs.Count; i++)
                if (defs[i].Applies(view, ctx) && (defs[i].Id == LedgerOutfit.EventId.TheMan ||
                                                   defs[i].Id == LedgerOutfit.EventId.BrokerRumour))
                    nearest = defs[i];
            if (nearest != null && card == null)
            {
                var signals = nearest.Signals != null ? nearest.Signals(view, ctx) : null;
                for (var i = 0; signals != null && i < signals.Count; i++)
                    Say(signals[i].Name + " - " + signals[i].Line, LedgerV2.Body, 9f);
                var gate = nearest.Gate(view, ctx);
                if (gate != LedgerOutfit.HoldReason.None)
                    Say("SHUT - " + LedgerOutfit.HoldReasons.Line(gate) + " - " +
                        LedgerOutfit.HoldReasons.Clears(gate), LedgerV2.Amber, 9f, 2);
                var pot = book.PotOf(nearest.Id);
                if (pot >= LedgerOutfit.StreetEvents.Full)
                    Say("The word is in. " + nearest.PotLine(ctx) + ".", LedgerV2.Green, 9f);
                else if (pot >= LedgerOutfit.StreetEvents.ShowFrom)
                    Say(char.ToUpperInvariant(nearest.PotLine(ctx)[0]) +
                        nearest.PotLine(ctx).Substring(1) + " (" + (int)(pot * 100) + "%).",
                        LedgerV2.Muted, 9f);
            }

            // The connection's own row.
            if (paper.Stage != LedgerOutfit.ConnectionStage.None)
            {
                var stage = LedgerOutfit.Connection.StageWord(paper.Stage);
                if (paper.Stage == LedgerOutfit.ConnectionStage.Burned)
                    stage += " - " + System.Math.Max(0, paper.BurnedUntilDay - day) + " DAYS LEFT";
                else if (paper.Grade != LedgerOutfit.SupplierGrade.None)
                    stage += " - " + LedgerOutfit.Connection.GradeWord(paper.Grade);
                Say(stage, LedgerV2.Ink, 9.5f);
                Say(paper.WhoseLine(house.Roster) + " · trust " + paper.Trust +
                    (paper.Kilos > 0 ? " · " + paper.Kilos + " kilos in the room" : "") +
                    (paper.Stage == LedgerOutfit.ConnectionStage.Supplier
                        ? " · next load day " + paper.NextLoadDay : ""),
                    LedgerV2.Body, 9f, 2);
                if (paper.Stage == LedgerOutfit.ConnectionStage.Rumour ||
                    paper.Stage == LedgerOutfit.ConnectionStage.Contact)
                {
                    var door = LedgerOutfit.ConnectionEvents.BrokerDoor(view, ctx);
                    var block = LedgerOutfit.ConnectionEvents.BlockOfDoor(view, door);
                    var name = LedgerOutfit.ConnectionEvents.DoorWord(door);
                    Say(char.ToUpperInvariant(LedgerOutfit.ConnectionText.Watch(
                            view.PoliceAttention(block), ctx.RaidThreshold, name)[0]) +
                        LedgerOutfit.ConnectionText.Watch(
                            view.PoliceAttention(block), ctx.RaidThreshold, name).Substring(1) + ".",
                        LedgerV2.Muted, 9f, 2);
                }
            }

            // The last lines the wire brought.
            var wire = book.Wire;
            for (var i = System.Math.Max(0, wire.Count - 3); i < wire.Count; i++)
                Say("Day " + wire[i].Day + " · " + wire[i].Text, LedgerV2.Muted, 8.5f, 2);
        }

        static int EarliestPressEdition(PressBook press, int currentDay)
        {
            // The day-one filler was delivered even when the public book has no local
            // fact in it. Wire/filler is deterministic, so it remains a real back issue
            // rather than making the archive arrows disappear on a quiet campaign.
            if (press != null && press.LastEditionDay > 0)
                return 1;
            var earliest = currentDay;
            for (var i = 0; press != null && i < press.Count; i++)
            {
                var row = press[i];
                var edition = row.Day + (row.Hour >= Edition.PressHour ? 1 : 0);
                if (edition < earliest) earliest = edition;
            }
            return Mathf.Max(1, earliest);
        }

        static int LatestPressEdition(PressBook press, int currentDay)
        {
            var latest = press != null ? press.LastEditionDay : 0;
            if (latest < 1)
                latest = currentDay;
            return Mathf.Clamp(latest, 1, Mathf.Max(1, currentDay));
        }

        void PreviousPressEdition()
        {
            var currentDay = outfit ? outfit.Campaign.Day : 1;
            var press = LivingCity.Outfit.Underworld.Current?.Press;
            var latest = LatestPressEdition(press, currentDay);
            var earliest = EarliestPressEdition(
                press, latest);
            if (newsEditionDay <= earliest) return;
            newsEditionDay--;
            newsPaintedEdition = -1;
            dirty = true;
        }

        void NextPressEdition()
        {
            var currentDay = outfit ? outfit.Campaign.Day : 1;
            var latest = LatestPressEdition(
                LivingCity.Outfit.Underworld.Current?.Press, currentDay);
            if (newsEditionDay >= latest) return;
            newsEditionDay++;
            newsPaintedEdition = -1;
            dirty = true;
        }

        void SaveCampaignFromLedger()
        {
            var refusal = LivingCity.Save.CampaignSave.Write(
                LivingCity.Save.CampaignSave.AutosavePath);
            if (string.IsNullOrEmpty(refusal))
            {
                lastRefusal = "";
                newspaperNote = "Campaign saved to " +
                                LivingCity.Save.CampaignSave.AutosaveName + ".";
            }
            else
            {
                newspaperNote = "";
                lastRefusal = "Save refused: " + refusal;
            }
            dirty = true;
        }

        void LoadCampaignFromLedger()
        {
            var refusal = LivingCity.Save.CampaignSave.LoadFromFile(
                LivingCity.Save.CampaignSave.AutosavePath);
            if (string.IsNullOrEmpty(refusal))
                return; // The scene reload has begun; this page is going away.

            newspaperNote = "";
            lastRefusal = "Load refused: " + refusal;
            dirty = true;
        }

        /// <summary>The lead: the deck across five sevenths of the sheet with its copy
        /// set in two columns under it, the press cut and its caption holding the rest.
        /// The design's 1.55-to-1 split, which is what a front page does.</summary>
        void BuildLead(Headline lead, int salt)
        {
            const float gap = 28f;
            var leadW = Mathf.Round((NewsWidth - gap) * (1.55f / 2.55f));
            var cutX = NewsLeft + leadW + gap;
            var cutW = NewsRight - cutX;

            const float headlineTop = CutTop;
            var headline = Paragraph(newsContent, LedgerStyle.Condensed, 44f, LedgerV2.Ink,
                NewsLeft, headlineTop, leadW, 100f, lead.Text, lineSpacing: -8f);
            headline.overflowMode = TextOverflowModes.Overflow;

            // A one-line deck and a three-line deck are both ordinary; the byline follows
            // whichever it turns out to be, or a short head leaves a hole under it.
            var deck = Mathf.Max(46f, headline.preferredHeight);
            var kickerY = headlineTop - deck - 10f;

            var kicker = lead.Historical
                ? DeskName(lead.Desk) + "  ·  FROM THE WIRE"
                : DeskName(lead.Desk) + "  ·  BY A STAFF CORRESPONDENT";
            Caps(newsContent, NewsLeft, kickerY, leadW, kicker, 11f, LedgerV2.Muted, 4f);

            // Two columns of copy, the way a lead is set. TMP wraps within one rect, so
            // the columns are two rects with the same run split between them - which is
            // also what a compositor does.
            // y runs DOWN as a negative here, so the run's height is how far the body's
            // top sits ABOVE the closing rule: bodyY - LeadRuleY, never the other way
            // round - reversed it comes out negative and TMP prints nothing at all.
            var bodyY = kickerY - 24f;
            var bodyH = Mathf.Max(60f, bodyY - LeadRuleY - 10f);
            var columnW = (leadW - 22f) * 0.5f;
            Paragraph(newsContent, LedgerStyle.Serif, 15f, LedgerV2.Ink, NewsLeft, bodyY,
                columnW, bodyH, Blurb(lead.Desk, salt), lineSpacing: 5f);
            Paragraph(newsContent, LedgerStyle.Serif, 15f, LedgerV2.Ink,
                NewsLeft + columnW + 22f, bodyY, columnW, bodyH,
                Blurb(lead.Desk, salt + 1) + "  Continued on page 3.", lineSpacing: 5f);

            // The cut takes the lead's height, capped a little wider than square: a
            // press photograph on a tall sheet is a big photograph, but a tower is a
            // poster and this is a newspaper. What the cap leaves goes to the copy.
            if (lead.Photo.HasPicture)
                NewsCut(lead, cutX, CutTop, cutW,
                    Mathf.Clamp(-LeadRuleY + CutTop - 26f, 250f, cutW * 1.15f));
        }

        /// <summary>The paper's own advertisement - furniture, and the one thing on the
        /// front page that is not news.</summary>
        void BuildAdvert(float x, float y, float w)
        {
            var ad = NewRect("Advert", newsContent);
            PlaceTopLeft(ad, x, y, w, 150f);
            Frame(ad, 2f, LedgerV2.Ink);
            var inner = NewRect("Inner", ad);
            Stretch(inner, 4f);
            Frame(inner, 1f, LedgerV2.Ink);

            var head = Line(ad, LedgerStyle.SerifBold, 17f, LedgerV2.Ink, 10f, -14f,
                w - 20f, 26f, "MARLOWE'S", TextAlignmentOptions.Center);
            head.characterSpacing = 4f;
            Caps(ad, 10f, -40f, w - 20f, "FINE TAILORING", 12f, LedgerV2.Ink, 4f,
                TextAlignmentOptions.Center);
            Rule(ad, 30f, -64f, w - 60f, LedgerV2.Ink);
            var body = Paragraph(ad, LedgerStyle.SerifItalic, 12.5f, LedgerV2.Ink, 12f,
                -70f, w - 24f, 70f,
                "Suits cut for the discreet professional. Wide in the shoulder, quiet " +
                "in the cloth. Fittings by appointment only.", lineSpacing: 2f);
            body.alignment = TextAlignmentOptions.Top;
        }

        /// <summary>
        /// The column the outfit reads first: what its own men did between one midnight
        /// and the next, in the paper's own voice. The sentences are the incident
        /// records' own - the sim wrote them when the thing happened, and the page does
        /// not get to reword them.
        /// </summary>
        void BuildOurOwn(float x, float y, float w)
        {
            Caps(newsContent, x, y, w, "CITY DESK  ·  LATE", 10.5f, LedgerV2.Muted, 4f);
            var head = Line(newsContent, LedgerStyle.Condensed, 22f, LedgerV2.Ink,
                x, y - 18f, w, 26f, "ON OUR STREETS");
            head.characterSpacing = 1f;
            Rule(newsContent, x, y - 48f, w, LedgerV2.Ink, 2f);

            var line = y - 54f;
            var room = BriefH - 60f;
            for (var i = 0; i < outfit.LastNight.Count && room > 30f; i++)
            {
                var text = outfit.LastNight[i].Line;
                if (text.Length == 0)
                    continue;

                // MEASURED, exactly as the same sentence is measured on the man's own
                // file. The column used to size a line by whether it ran past seventy
                // characters - a guess about a proportional face poured into a column
                // whose width the window decides - and the sentences it cut short were
                // the long ones, which are the ones worth reading. A line that will not
                // fit what is left of the column is not started at all.
                const float lead = 6f;
                var height = CopyHeight(text, 12.5f, w, 2f);
                if (height + lead > room)
                    break;
                Paragraph(newsContent, LedgerStyle.Serif, 12.5f, LedgerV2.Ink, x, line,
                    w, height, text, lineSpacing: 2f);
                line -= height + lead;
                room -= height + lead;
            }
        }

        /// <summary>One brief in the row: desk label, condensed head, a hairline, copy.</summary>
        void NewsColumn(Headline story, float x, float y, float w, float h, int salt)
        {
            Caps(newsContent, x, y, w, story.Historical
                ? DeskName(story.Desk) + "  ·  WIRE REPORT"
                : DeskName(story.Desk), 10.5f, LedgerV2.Muted, 4f);

            // Two lines of the condensed gothic, measured - at 52 units a two-line head
            // could not fit its second line and TMP dropped BOTH, ellipsing mid-word.
            var head = Paragraph(newsContent, LedgerStyle.Condensed, 21f, LedgerV2.Ink,
                x, y - 18f, w, LineBox(21f, 2), story.Text, lineSpacing: -4f);
            head.overflowMode = TextOverflowModes.Ellipsis;

            Rule(newsContent, x, y - 90f, w, LedgerV2.Rule);

            var bodyTop = y - 98f;
            var bodyHeight = h - 98f;
            if (story.Photo.HasPicture && bodyHeight > 120f)
            {
                NewsCut(story, x, bodyTop, w, 74f);
                bodyTop -= 100f;
                bodyHeight -= 100f;
            }

            Paragraph(newsContent, LedgerStyle.Serif, 13f, LedgerV2.Ink, x, bodyTop, w,
                bodyHeight, Blurb(story.Desk, salt), lineSpacing: 3f);
        }

        /// <summary>A halftone cut in a hairline frame with its italic caption under.
        /// The print is the studio's newsprint treatment of the story's own model; until
        /// it lands (or when no model resolves) the hatched plate simply stays.</summary>
        void NewsCut(Headline story, float x, float y, float w, float h)
        {
            var raw = LedgerV2.PortraitPlate(newsContent, x, y, w, h, "PRESS PHOTO",
                LedgerV2.PanelInset);

            // The crop that keeps the subject in his own proportions is PortraitFit's,
            // put on with the print itself - see PortraitStudio.Show.

            var photo = story.Photo;
            var model = photo.Subject == PhotoSubject.Vehicle
                ? PortraitStudio.FindVehiclePrefab(photo.ModelName)
                : PortraitStudio.FindPeoplePrefab(photo.ModelName);
            PortraitStudio.Request(model,
                photo.Subject == PhotoSubject.Vehicle
                    ? PortraitStudio.Framing.Vehicle
                    : PortraitStudio.Framing.Bust,
                raw, PortraitStudio.Treatment.Newsprint);

            var caption = Paragraph(newsContent, LedgerStyle.SerifItalic, 12.5f,
                LedgerV2.Ink, x, y - h - 4f, w, 30f, photo.Caption, lineSpacing: 0f);
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
