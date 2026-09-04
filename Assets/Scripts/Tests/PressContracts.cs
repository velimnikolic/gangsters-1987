using System;
using System.Collections.Generic;
using LivingCity.Gangs;
using LivingCity.News;
using LivingCity.Personnel;
using LivingCity.Police;
using LivingCity.Save;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>EPIC 35's pure contracts: the public-record gate, the 06-to-06
    /// edition, factual copy and the retained save book. Scene witness counts and the
    /// popup itself remain Play evidence; everything else is judged here.</summary>
    public static class NewsTests
    {
        static readonly (string Name, Action<List<string>> Check)[] Contracts =
        {
            ("APrivateIncidentNeverReachesThePress", APrivateIncidentNeverReachesThePress),
            ("AComplaintIsNotNews", AComplaintIsNotNews),
            ("ABodyIsAlwaysNews", ABodyIsAlwaysNews),
            ("AFamilyIsNamedOnlyByThePoliceTheCourtOrEnoughEyes", AFamilyIsNamedOnlyByThePoliceTheCourtOrEnoughEyes),
            ("TwoHousesShootingIsNobody", TwoHousesShootingIsNobody),
            ("SeenNeedsWitnessesOverTheThreshold", SeenNeedsWitnessesOverTheThreshold),
            ("OneShootoutIsOneStory", OneShootoutIsOneStory),
            ("OneCollarIsOneStory", OneCollarIsOneStory),
            ("AStoryBelongsToTheNightItOpened", AStoryBelongsToTheNightItOpened),
            ("AShopRestoredFromAFileIsNotNews", AShopRestoredFromAFileIsNotNews),
            ("TheLeadIsTheHeaviestStory", TheLeadIsTheHeaviestStory),
            ("NoRecordNoStory", NoRecordNoStory),
            ("EveryTemplateFitsTheRealNames", EveryTemplateFitsTheRealNames),
            ("ThePressSpeaksInTheThirdPerson", ThePressSpeaksInTheThirdPerson),
            ("TheSameSeedPrintsTheSamePaper", TheSameSeedPrintsTheSamePaper),
            ("AHistoricalDateStillPrintsOnARealLeadMorning", AHistoricalDateStillPrintsOnARealLeadMorning),
            ("PublicEdgeCasesUseTheirDesks", PublicEdgeCasesUseTheirDesks),
            ("ThePaperSurvivesASave", ThePaperSurvivesASave),
            ("AVersionTwoFileMigrates", AVersionTwoFileMigrates),
            ("TheEditionWindowIsSixToSix", TheEditionWindowIsSixToSix),
        };

        public static List<string> Run()
        {
            var failures = new List<string>();
            for (var i = 0; i < Contracts.Length; i++)
                Contracts[i].Check(failures);
            return failures;
        }

        public static string[] ContractNames()
        {
            var names = new string[Contracts.Length];
            for (var i = 0; i < Contracts.Length; i++)
                names[i] = Contracts[i].Name;
            return names;
        }

        /// <summary>The four terminal fixtures named by the brief. Every record opens
        /// during the night before edition two's cut.</summary>
        public static PressRecord[] Stage(string stage)
        {
            switch ((stage ?? "quiet").Trim().ToLowerInvariant())
            {
                case "quiet":
                    return Array.Empty<PressRecord>();
                case "shootout":
                    return new[]
                    {
                        Record(PressKind.Shootout, weight: 52, shots: 11,
                            attribution: PressAttribution.Seen),
                    };
                case "arrest":
                    return new[]
                    {
                        Record(PressKind.Arrest, weight: 68,
                            attribution: PressAttribution.Named),
                    };
                case "arson":
                    return new[]
                    {
                        Record(PressKind.Arson, weight: 76,
                            attribution: PressAttribution.Unknown),
                    };
                default:
                    return null;
            }
        }

        /// <summary>Fast proof for a staged terminal edition.</summary>
        public static List<string> Proof(Headline[] page)
        {
            var failures = new List<string>();
            if (page == null || page.Length != HeadlineGenerator.FrontPageSize)
            {
                failures.Add("PRESS BENCH: the edition did not print six stories.");
                return failures;
            }
            for (var i = 0; i < page.Length; i++)
            {
                var story = page[i];
                if (story == null || string.IsNullOrWhiteSpace(story.Text))
                    failures.Add("PRESS BENCH: story " + i + " has no headline.");
                else if (story.Text.Length > HeadlineGenerator.TextBudget)
                    failures.Add("PRESS BENCH: story " + i + " is " +
                                 story.Text.Length + " characters wide.");
                if (story?.Story != null && HasFirstPerson(
                        (story.Text ?? "") + " " + (story.Blurb ?? "")))
                    failures.Add("PRESS BENCH: a city story speaks in the first person: " +
                                 story.Text);
            }
            return failures;
        }

        static void APrivateIncidentNeverReachesThePress(List<string> failures)
        {
            Want(failures, !PressPolicy.StreetIncidentIsPublic(0, 2, 3),
                "PRESS-001: gunfire with too few witnesses and no body became public.");
            Want(failures, PressPolicy.StreetIncidentIsPublic(0, 3, 3),
                "PRESS-001: the eyewitness gate did not open at its threshold.");
        }

        static void AComplaintIsNotNews(List<string> failures)
        {
            Want(failures, !PressPolicy.ComplaintIsPublic(statementTaken: false),
                "PRESS-002: a private telephone complaint reached the paper.");
            Want(failures, PressPolicy.ComplaintIsPublic(statementTaken: true),
                "PRESS-002: the police blotter omitted a statement actually taken.");
        }

        static void ABodyIsAlwaysNews(List<string> failures)
        {
            Want(failures, PressPolicy.StreetIncidentIsPublic(1, 0, 3),
                "PRESS-003: a body needed eyewitnesses before it became public.");
            var body = Record(PressKind.Killing, deaths: 1,
                attribution: PressAttribution.Unknown);
            body.Names = new[] { "Vito Mancini" };
            body.NamedGangId = 4;
            var page = Edition.Compose(7, NewsDate.FromClockDay(1), 2,
                new[] { body });
            Want(failures, CountLocal(page) == 1 &&
                           Contains(page, "VITO MANCINI"),
                "PRESS-003: the public body did not print with the dead gangster's name.");
        }

        static void AFamilyIsNamedOnlyByThePoliceTheCourtOrEnoughEyes(
            List<string> failures)
        {
            Want(failures, PressPolicy.Attribution(1, 2, 3) == PressAttribution.Unknown,
                "PRESS-004: one unseen faction was named.");
            Want(failures, PressPolicy.Attribution(1, 3, 3) == PressAttribution.Seen,
                "PRESS-004: enough independent eyes did not identify one faction.");
            Want(failures, PressPolicy.Attribution(0, 0, 3, officiallyNamed: true) ==
                           PressAttribution.Named,
                "PRESS-004: a police or court identification was not Named.");

            var hidden = Record(PressKind.Shootout,
                attribution: PressAttribution.Unknown);
            var seen = Record(PressKind.Shootout,
                attribution: PressAttribution.Seen);
            Want(failures, PressText.Story(hidden).GangId < 0 &&
                           PressText.Story(seen).GangId == hidden.Family,
                "PRESS-004: the printed family does not follow attribution.");
            Want(failures,
                PressText.Copy(hidden).IndexOf("DEMARCO", StringComparison.OrdinalIgnoreCase) < 0 &&
                PressText.Copy(seen).IndexOf("DEMARCO", StringComparison.OrdinalIgnoreCase) >= 0,
                "PRESS-004: Seen/Unknown attribution did not change the words on the sheet.");
        }

        static void TwoHousesShootingIsNobody(List<string> failures)
        {
            Want(failures, PressPolicy.Attribution(2, 12, 3) == PressAttribution.Unknown,
                "PRESS-005: witnesses to two firing factions identified one as the shooter.");
        }

        static void SeenNeedsWitnessesOverTheThreshold(List<string> failures)
        {
            Want(failures, PressPolicy.Attribution(1, 2, 3) != PressAttribution.Seen &&
                           PressPolicy.Attribution(1, 3, 3) == PressAttribution.Seen,
                "PRESS-006: Seen does not change exactly at the configured threshold.");
        }

        static void OneShootoutIsOneStory(List<string> failures)
        {
            var report = Record(PressKind.Shootout, shots: 9,
                attribution: PressAttribution.Seen);
            report.IncidentNumber = 44;
            var page = Edition.Compose(11, NewsDate.FromClockDay(1), 2,
                new[] { report });
            Want(failures, CountLocal(page) == 1 && Contains(page, "9 SHOTS"),
                "PRESS-007: one aggregated StreetAlarm incident did not print once with its full shot count.");
        }

        static void OneCollarIsOneStory(List<string> failures)
        {
            var book = new PressBook();
            var arrest = Record(PressKind.Arrest,
                attribution: PressAttribution.Named);
            arrest.CaseId = 81;
            var charge = Record(PressKind.ChargesFiled,
                attribution: PressAttribution.Named);
            charge.CaseId = 81;
            charge.Deed = Deed.Murder;
            book.Add(arrest);
            book.Add(charge);
            Want(failures, book.Count == 1 && book[0].Kind == PressKind.ChargesFiled,
                "PRESS-008: station booking and charges printed twice for docket 81.");
        }

        static void AStoryBelongsToTheNightItOpened(List<string> failures)
        {
            var story = Record(PressKind.Shootout);
            story.Day = 1;
            story.Hour = 23.75f;
            Want(failures, Edition.InWindow(story, 2) && !Edition.InWindow(story, 1),
                "PRESS-009: an incident was filed by its later close instead of its opening night.");
        }

        static void AShopRestoredFromAFileIsNotNews(List<string> failures)
        {
            Want(failures,
                !PressPolicy.ShutdownChangeIsPublic(started: false, extended: false) &&
                 PressPolicy.ShutdownChangeIsPublic(started: true, extended: false) &&
                 PressPolicy.ShutdownChangeIsPublic(started: false, extended: true),
                "PRESS-010: restore/repair printed, or a real shutdown/extension did not.");
        }

        static void TheLeadIsTheHeaviestStory(List<string> failures)
        {
            var light = Record(PressKind.Arrest, weight: 68,
                attribution: PressAttribution.Named);
            light.CaseId = 1;
            var heavy = Record(PressKind.OfficerKilled, weight: 100, deaths: 1);
            var page = Edition.Compose(5, NewsDate.FromClockDay(1), 2,
                new[] { light, heavy });
            Want(failures, page.Length > 0 && ReferenceEquals(page[0].Story, heavy),
                "PRESS-011: the heaviest qualifying city story did not lead.");
        }

        static void NoRecordNoStory(List<string> failures)
        {
            var date = new NewsDate(2, 3);
            var page = Edition.Compose(5, date, 30,
                Array.Empty<PressRecord>());
            var original = HeadlineGenerator.FrontPage(5, date,
                (IReadOnlyList<string>)null, HeadlineGenerator.FrontPageSize);
            Want(failures, CountLocal(page) == 0 && Signature(page) == Signature(original),
                "PRESS-012: an empty public book fabricated a story or changed the wire page.");
        }

        static void EveryTemplateFitsTheRealNames(List<string> failures)
        {
            var districts = new[] { "THE INDUSTRIAL WATERFRONT", "THE FLATS" };
            for (var seed = 1; seed <= 80; seed++)
            {
                var page = HeadlineGenerator.FrontPage(seed,
                    NewsDate.FromClockDay(seed % 360), districts, 12);
                for (var i = 0; i < page.Length; i++)
                {
                    Want(failures, page[i].Text.Length <= HeadlineGenerator.TextBudget,
                        "PRESS-013: generated headline overran 56 characters: " + page[i].Text);
                    Want(failures, page[i].Text.IndexOf('{') < 0,
                        "PRESS-013: an unfilled template reached print: " + page[i].Text);
                    if (page[i].Photo.HasPicture)
                        Want(failures, (page[i].Photo.Caption ?? "").Length <=
                                       PictureDesk.CaptionBudget,
                            "PRESS-013: a picture caption overran its cut.");
                }
            }

            var records = EveryKind();
            for (var i = 0; i < records.Count; i++)
            {
                var head = PressText.Head(records[i]);
                Want(failures, head.Length > 0 && head.Length <= PressText.HeadlineBudget,
                    "PRESS-013: " + records[i].Kind + " overran or printed no headline: " + head);
            }
        }

        static void ThePressSpeaksInTheThirdPerson(List<string> failures)
        {
            var records = EveryKind();
            for (var i = 0; i < records.Count; i++)
            {
                var copy = PressText.Head(records[i]) + " " + PressText.Copy(records[i]);
                Want(failures, !HasFirstPerson(copy),
                    "PRESS-014: " + records[i].Kind + " speaks for the player: " + copy);
            }
        }

        static void TheSameSeedPrintsTheSamePaper(List<string> failures)
        {
            var records = Stage("arson");
            var date = NewsDate.FromClockDay(1);
            var a = Edition.Compose(1987, date, 2, records);
            var b = Edition.Compose(1987, date, 2, records);
            Want(failures, Signature(a) == Signature(b),
                "PRESS-015: the same city, date and public book printed two papers.");
        }

        static void AHistoricalDateStillPrintsOnARealLeadMorning(List<string> failures)
        {
            var lead = Record(PressKind.OfficerKilled, weight: 100, deaths: 1);
            var page = Edition.Compose(1987, new NewsDate(1, 13), 2,
                new[] { lead });
            Want(failures, page.Length > 1 && ReferenceEquals(page[0].Story, lead) &&
                           page[1].Historical,
                "PRESS-016: the city lead displaced the pinned 1987 date instead of moving it to the first brief.");
        }

        static void PublicEdgeCasesUseTheirDesks(List<string> failures)
        {
            var sale = PressText.Story(Record(PressKind.PremisesSold,
                attribution: PressAttribution.Named));
            var raid = Record(PressKind.FlatRaid,
                attribution: PressAttribution.Named);
            Want(failures, sale != null && sale.Desk == HeadlineDesk.Business,
                "PRESS-017: a public premises sale did not reach the business desk.");
            Want(failures, PressText.IsBlotter(raid),
                "PRESS-017: a flat raid did not stay in the police blotter.");
        }

        static void ThePaperSurvivesASave(List<string> failures)
        {
            var before = new PressBook();
            var record = Record(PressKind.Verdict, weight: 73,
                attribution: PressAttribution.Named);
            record.Day = 11;
            record.Hour = 4.5f;
            record.CaseId = 93;
            record.SentenceDays = 730;
            record.Outcome = (int)CaseOutcome.Convicted;
            before.Add(record);
            before.LastEditionDay = 10;

            var json = JsonUtility.ToJson(new CampaignFile
            {
                version = CampaignFile.Version,
                day = 11,
                press = PressSnapshot.Snapshot(before),
                lastEditionDay = before.LastEditionDay,
            });
            var file = JsonUtility.FromJson<CampaignFile>(json);
            var after = new PressBook();
            PressSnapshot.Restore(after, file);

            Want(failures, after.Count == 1 && after.LastEditionDay == 10 &&
                           after[0].CaseId == 93 && after[0].SentenceDays == 730 &&
                           after[0].Names.Length == record.Names.Length &&
                           after[0].Names[0] == record.Names[0],
                "PRESS-018: the public book did not survive its explicit v3 DTO.");
        }

        static void AVersionTwoFileMigrates(List<string> failures)
        {
            const string legacy =
                "{\"version\":2,\"citySeed\":1987,\"day\":30,\"hourOfDay\":9.0}";
            var file = JsonUtility.FromJson<CampaignFile>(legacy);
            var book = new PressBook();
            book.Add(Record(PressKind.Arson));
            PressSnapshot.Restore(book, file);
            Want(failures, book.Count == 0 && book.LastEditionDay == 30,
                "PRESS-019: v2 did not migrate to an empty, already-delivered public book.");
        }

        static void TheEditionWindowIsSixToSix(List<string> failures)
        {
            var atStart = Record(PressKind.Arson); atStart.Day = 2; atStart.Hour = 6f;
            var beforeEnd = Record(PressKind.Arrest); beforeEnd.Day = 3; beforeEnd.Hour = 5.99f;
            var beforeStart = Record(PressKind.Bombing); beforeStart.Day = 2; beforeStart.Hour = 5.99f;
            var atEnd = Record(PressKind.Killing); atEnd.Day = 3; atEnd.Hour = 6f;
            Want(failures,
                Edition.InWindow(atStart, 3) && Edition.InWindow(beforeEnd, 3) &&
                !Edition.InWindow(beforeStart, 3) && !Edition.InWindow(atEnd, 3),
                "PRESS-020: the edition window is not [previous 06:00, current 06:00).");
        }

        static PressRecord Record(PressKind kind, int weight = 0, int shots = 4,
            int deaths = 0, PressAttribution attribution = PressAttribution.Unknown)
        {
            return new PressRecord
            {
                Day = 1,
                Hour = 22.25f,
                Kind = kind,
                Where = "The Flats",
                Business = "International Delicatessen and Grocery",
                Factions = new[] { 4 },
                NamedGangId = 4,
                Attribution = attribution,
                Witnesses = 5,
                GangsterDeaths = deaths,
                Shots = shots,
                Names = new[] { "Vito Mancini" },
                Models = new[] { GangCatalog.SoldierModels[4] },
                Deed = Deed.Extortion,
                SentenceDays = 365,
                Outcome = (int)CaseOutcome.Convicted,
                Weight = weight > 0 ? weight : PressRecord.DefaultWeight(kind),
            };
        }

        static List<PressRecord> EveryKind()
        {
            var result = new List<PressRecord>();
            foreach (PressKind kind in Enum.GetValues(typeof(PressKind)))
            {
                var row = Record(kind, attribution: PressAttribution.Named);
                if (kind == PressKind.Killing || kind == PressKind.BossKilled ||
                    kind == PressKind.OfficerKilled)
                    row.GangsterDeaths = 1;
                result.Add(row);
            }
            return result;
        }

        static int CountLocal(Headline[] page)
        {
            var count = 0;
            for (var i = 0; page != null && i < page.Length; i++)
                if (page[i]?.Story != null) count++;
            return count;
        }

        static bool Contains(Headline[] page, string value)
        {
            for (var i = 0; page != null && i < page.Length; i++)
                if ((page[i]?.Text ?? "").IndexOf(value,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static bool HasFirstPerson(string text)
        {
            var padded = " " + (text ?? "").ToLowerInvariant()
                .Replace('\n', ' ').Replace('\r', ' ') + " ";
            return padded.Contains(" our ") || padded.Contains(" ours ") ||
                   padded.Contains(" we ") || padded.Contains(" us ") ||
                   padded.Contains(" my ");
        }

        static string Signature(Headline[] page)
        {
            var text = "";
            for (var i = 0; page != null && i < page.Length; i++)
            {
                var row = page[i];
                text += (int)row.Desk + "|" + row.Text + "|" + row.Blurb + "|" +
                        row.Historical + "|" + row.GangId + "|" +
                        (int)row.Photo.Subject + "|" + row.Photo.ModelName + "|" +
                        row.Photo.Caption + "\n";
            }
            return text;
        }

        static void Want(List<string> failures, bool condition, string message)
        {
            if (!condition) failures.Add(message);
        }
    }
}
