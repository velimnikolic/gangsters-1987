using System.Collections.Generic;
using System.Text;
using LivingCity.Business;
using LivingCity.News;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Save;
using LivingCity.Territory;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>
    /// RIVAL-010. A campaign written down and read back.
    ///
    /// The contract is not "the file parses" - it is that a city put back from a file is
    /// the SAME CITY, and that it goes on being the same city as it is played. So the
    /// suite mutates everything it can reach, round trips it through the JSON the game
    /// actually writes, and compares a full dump of both; then it plays both forward a
    /// day and compares again.
    /// </summary>
    public static class SaveTests
    {
        const int Seed = 1987;

        public static List<string> Run()
        {
            var failures = new List<string>();

            ACampaignComesBackTheSameCampaign(failures);
            TheCityComesBackTheSameCity(failures);
            ALoadedCampaignPlaysOnTheSameWay(failures);
            AnActiveShutdownComesBackThroughCampaignApply(failures);
            NobodyIsLeftInTheCells(failures);
            TheDocketComesBackWithIts(failures);
            ThePublicBookComesBackWithTheCampaign(failures);
            AVersionTwoPrisonerFindsHisHouseFromTheDocket(failures);
            BodyEvidenceSurvivesTheFile(failures);
            BadEnumIntegersStopAtTheLoadBoundary(failures);
            ASaveFromBeforeTheDocketStillGetsATrial(failures);
            OwnerGenerationsSurviveVersionThree(failures);
            VersionTwoMeansOriginalOwners(failures);
            SuccessorReplayOrderDoesNotMatter(failures);
            ANewerSaveIsRefused(failures);

            return failures;
        }

        // ------------------------------------------------------------------ RIVAL-010

        /// <summary>
        /// (a) The books: money, days, orders in flight, a man in a bed, a man struck
        /// off, a pending stance, a grudge. Everything mutated, written, read, restored -
        /// and every figure the same on both sides.
        /// </summary>
        static void ACampaignComesBackTheSameCampaign(List<string> failures)
        {
            var world = Mutated();
            var before = Dump(world);

            // THE DUMP HAS TO BE WORTH COMPARING. Two empty strings match perfectly, and
            // a contract that would pass over an empty campaign proves nothing about a
            // full one.
            if (before.Length < 5000)
                failures.Add("SAVE-001: the dump is " + before.Length + " characters - " +
                             "too little of a campaign to be worth comparing.");
            var wanted = new[]
            {
                "safe=41250", "Hospitalized", "pending 0-9 War", "owed 5->0",
                "skim=True", "job ", "jobs=733",
            };
            for (var i = 0; i < wanted.Length; i++)
                if (!before.Contains(wanted[i]))
                    failures.Add("SAVE-001: the fixture never produced [" + wanted[i] +
                                 "], so the round trip is not being asked about it.");

            var hasBagDetail = false;
            for (var g = 0; g < world.Count && !hasBagDetail; g++)
            {
                var house = world.Of(g);
                for (var c = 0; house != null && c < house.Roster.Crews.Count; c++)
                {
                    var crew = house.Roster.Crews[c];
                    if (crew.BagId >= 0 && crew.EscortIds.Count == Crew.MaxEscorts)
                    {
                        hasBagDetail = true;
                        break;
                    }
                }
            }
            if (!hasBagDetail)
                failures.Add("SAVE-001: the fixture never produced a collector with two escorts.");

            var json = JsonUtility.ToJson(OutfitSnapshot.Snapshot(world));
            var read = JsonUtility.FromJson<UnderworldDto>(json);

            var after = Underworld.Deal(Seed);
            OutfitSnapshot.Restore(after, read);

            var back = Dump(after);
            if (before != back)
                failures.Add("SAVE-001: the campaign did not come back the same. " +
                             FirstDifference(before, back));
        }

        /// <summary>
        /// (b) The city: who pays whom, what they owe, and a round that was out when the
        /// game stopped.
        /// </summary>
        static void TheCityComesBackTheSameCity(List<string> failures)
        {
            var racket = new TerritoryRacketLedger();
            var dues = new TerritoryDuesLedger();
            var rounds = new TerritoryRoundLedger(racket, dues);
            var mine = new TerritoryGangId(3);

            var shop = new TerritoryBusinessId("biz:corner");
            var bar = new TerritoryBusinessId("biz:bar");
            racket.Demand(shop, mine, Strong(), 9.0, out _);
            racket.Demand(bar, new TerritoryGangId(7), Hopeless(), 9.0, out _);
            for (var day = 0; day < 7; day++)
                dues.AccrueDay(shop, mine, 700);

            var stops = new List<TerritoryRoundStop>
            {
                new TerritoryRoundStop(shop, new TerritoryPoint(10f, 20f)),
                new TerritoryRoundStop(bar, new TerritoryPoint(30f, 20f)),
            };
            var round = rounds.Open(
                mine, 3001, 11, new TerritoryBlockId("block:a"),
                TerritoryRoundKind.Collect, stops, 9.0);
            rounds.Arrive(round, 9.5);
            round.Carried = 240;

            var before = Dump(racket, dues, rounds);
            var json = JsonUtility.ToJson(
                TerritorySnapshot.Snapshot(racket, dues, rounds));
            var read = JsonUtility.FromJson<TerritoryDto>(json);

            var racketBack = new TerritoryRacketLedger();
            var duesBack = new TerritoryDuesLedger();
            var roundsBack = new TerritoryRoundLedger(racketBack, duesBack);
            TerritorySnapshot.Restore(racketBack, duesBack, roundsBack, read);

            var back = Dump(racketBack, duesBack, roundsBack);
            if (before != back)
                failures.Add("SAVE-002: the city did not come back the same. " +
                             FirstDifference(before, back));
        }

        /// <summary>
        /// (c) Determinism. A campaign saved, restored and then played a day forward is
        /// the same campaign as one that was never saved and played the same day.
        /// </summary>
        static void ALoadedCampaignPlaysOnTheSameWay(List<string> failures)
        {
            var kept = Mutated();
            var loaded = Underworld.Deal(Seed);
            OutfitSnapshot.Restore(
                loaded,
                JsonUtility.FromJson<UnderworldDto>(
                    JsonUtility.ToJson(OutfitSnapshot.Snapshot(kept))));

            kept.AdvanceHours(24f);
            kept.DayTick();
            loaded.AdvanceHours(24f);
            loaded.DayTick();

            var a = Dump(kept);
            var b = Dump(loaded);
            if (a != b)
                failures.Add("SAVE-003: a loaded campaign played on differently. " +
                             FirstDifference(a, b));
        }

        /// <summary>
        /// WAR-001 THROUGH THE DOOR THE GAME SHIPS. The file is written and read by
        /// CampaignSave, then the same Apply wiring production uses restores an actual
        /// CityClock and BusinessRuntime fixture. This catches the one-based campaign /
        /// zero-based clock mismatch where a load silently ate one shutdown day.
        /// </summary>
        static void AnActiveShutdownComesBackThroughCampaignApply(List<string> failures)
        {
            const int campaignDay = 30;
            const float hour = 9.5f;
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "gangsters-shutdown-apply.json");
            var wasCurrent = Underworld.Current;
            var wasClock = LivingCity.Ambient.DayClock.Current;
            GameObject fixture = null;

            try
            {
                fixture = new GameObject("SAVE WAR-001 fixture")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                fixture.SetActive(false);
                var clock = fixture.AddComponent<LivingCity.Ambient.CityClock>();
                var business = fixture.AddComponent<BusinessRuntime>();
                business.Init(null, Seed);

                var site = BusinessIdentity.Site("save", "shutdown", "apply");
                var owner = BusinessIdentity.Owner(site);
                business.Directory.RegisterOwner(
                    owner, BusinessOwnerKind.Individual, "Saved Owner",
                    BusinessOwnerAge.Middle, 17);
                var record = business.Directory.Register(
                    site, BusinessArchetypeId.Grocer, "Saved Shop", owner,
                    BusinessSiteSize.Small, 1_200, "save-test");
                if (record == null)
                {
                    failures.Add("SAVE-008: the shutdown Apply fixture could not register its business.");
                    return;
                }

                var savedAt = LivingCity.Ambient.CityClock.GameHourOfCampaignTime(
                    campaignDay, hour);
                var file = new CampaignFile
                {
                    citySeed = Seed,
                    day = campaignDay,
                    hourOfDay = hour,
                    shutdowns = new[]
                    {
                        new ShutdownDto
                        {
                            businessId = record.Id.Value,
                            cause = (int)BusinessShutdownCause.SmashUp,
                            startedAt = savedAt,
                            recoveryAt = savedAt + 72d,
                        },
                    },
                };

                var refusal = CampaignSave.Write(file, path);
                var read = CampaignSave.Read(path, out var readRefusal);
                if (!string.IsNullOrEmpty(refusal) || read == null ||
                    !string.IsNullOrEmpty(readRefusal))
                {
                    failures.Add("SAVE-008: the shutdown file did not cross Write/Read: " +
                                 (refusal + " " + readRefusal).Trim());
                    return;
                }

                Underworld.ResetForPlay();
                CampaignSave.Apply(read, clock, null, business, null);

                if (clock.Day != campaignDay - 1 ||
                    Mathf.Abs(clock.Hour - hour) > 0.001f)
                    failures.Add("SAVE-008: CampaignSave.Apply restored the central clock to the wrong date.");
                if (!business.Shutdowns.TryGet(record.Id, savedAt, out var shutdown) ||
                    shutdown.Cause != BusinessShutdownCause.SmashUp ||
                    shutdown.RecoveryAt != savedAt + 72d ||
                    shutdown.RemainingHours != 72d ||
                    record.State != BusinessOperationalState.Shut)
                    failures.Add("SAVE-008: CampaignSave.Apply did not preserve the active shutdown deadline.");
            }
            finally
            {
                Underworld.Restore(wasCurrent);
                if (fixture != null)
                    Object.DestroyImmediate(fixture);
                if (wasClock != null)
                    LivingCity.Ambient.DayClock.Register(wasClock);
                try
                {
                    System.IO.File.Delete(path);
                }
                catch (System.Exception)
                {
                    // A temp file that will not delete is not this contract's business.
                }
            }
        }

        /// <summary>
        /// A MAN IN CUSTODY IS NOT LOST BY SAVING. He is booked with NO release date -
        /// the day tick refuses to discharge a man without one - so the pipe is the only
        /// thing that will ever let him out. Until this contract, the campaign file left
        /// the pipe out entirely: a save taken while anybody was in the cells jailed him
        /// for the rest of the campaign, drawing his envelope, against his lieutenant's
        /// headcount, and never coming back.
        /// </summary>
        static void NobodyIsLeftInTheCells(List<string> failures)
        {
            var world = Underworld.Deal(Seed);
            var theirs = world.Of(7);

            var man = -1;
            for (var i = 0; i < theirs.Roster.Members.Count && man < 0; i++)
                if (!theirs.Roster.Members[i].Gone &&
                    theirs.Roster.Members[i].Rank == Rank.Hood)
                    man = theirs.Roster.Members[i].Id;
            if (man < 0)
            {
                failures.Add("SAVE-005: the fixture dealt nobody to arrest.");
                return;
            }

            var pipe = new Police.PrisonPipeline { RosterSeed = Seed };
            var taken = pipe.Book(theirs.Roster, man, Deed.Affray, 12);
            if (taken == null)
            {
                failures.Add("SAVE-005: the fixture could not book anybody.");
                return;
            }

            // The state the defect turned on: held, and no way out but the pipe.
            if (theirs.Roster.Find(man).BackOnDay != 0)
                failures.Add("SAVE-005: a held man has a release date after all - the " +
                             "contract is asking about a case that no longer exists.");

            // ONLY A MAN WHO WAS IN THE CAR gets out of one. He is put in the back
            // first, which is the only way the pipe lets anybody be freed.
            var escapee = taken.CharacterId;
            taken.Stage = Police.PrisonStage.InTransit;
            if (pipe.Freed(theirs.Roster, taken, 13) == null)
            {
                failures.Add("SAVE-005: the fixture could not get anybody out of a car.");
                return;
            }
            var second = pipe.Book(theirs.Roster, man, Deed.Affray, 20);
            var secondCourtDay = second != null ? second.CourtDay : 0;
            if (second != null)
            {
                var due = new List<Police.Prisoner>();
                pipe.DayTick(secondCourtDay, due);
                pipe.Away(second);
                second.Carriage = Police.CarriageStage.Riding;
            }

            // THROUGH THE CONVERSION THE GAME ACTUALLY SHIPS. This fixture used to
            // hand-roll its own copy of the DTO fields, which is precisely why nobody
            // noticed the docket was never written at all (GAN-302): a test that
            // rewrites the code it is guarding guards nothing.
            var escaped = new List<int>();
            pipe.CollectEscapes(escaped);
            var file = new CampaignFile
            {
                day = secondCourtDay,
                prisoners = Save.PrisonSnapshot.Prisoners(pipe, secondCourtDay),
                cases = Save.PrisonSnapshot.Cases(pipe),
                nextCaseId = pipe.NextCaseId,
                escaped = escaped.ToArray(),
                prisonRosterSeed = pipe.RosterSeed,
            };
            var read = JsonUtility.FromJson<CampaignFile>(JsonUtility.ToJson(file));

            var back = new Police.PrisonPipeline();
            Save.PrisonSnapshot.Restore(back, read);

            if (back.RosterSeed != pipe.RosterSeed)
                failures.Add("SAVE-005: the pipe came back on a different stream, so " +
                             "his sentence would be rolled twice differently.");
            if (back.Inside.Count != pipe.Inside.Count)
                failures.Add("SAVE-005: " + pipe.Inside.Count + " men were in the cells " +
                             "and " + back.Inside.Count + " came back.");

            var restored = back.Find(man);
            if (restored == null)
                failures.Add("SAVE-005: a man in the cells was lost by saving - nothing " +
                             "will ever let him out again.");
            else if (restored.Stage != Police.PrisonStage.Held ||
                     restored.Leg != Police.PrisonLeg.None ||
                     restored.CourtDay != secondCourtDay + 1)
                failures.Add("SAVE-005: he came back at the wrong point in the pipe (" +
                             restored.Stage + ", court day " + restored.CourtDay + ").");

            if (!back.EverEscaped(escapee))
                failures.Add("SAVE-005: the city forgot he had been out of a car once, " +
                             "so the next judge goes easy on him.");
        }

        /// <summary>
        /// (d) THE DOCKET SURVIVES (GAN-302). The case is what a trial is decided on:
        /// its witnesses are what the player leaned on and its counts are what the men
        /// answer for. It was not written to the file at all, so a man loaded out of a
        /// save was tried with no case behind him - which the trial reads as "no docket,
        /// no defence" and converts to a conviction with no roll at all.
        /// </summary>
        static void TheDocketComesBackWithIts(List<string> failures)
        {
            var roster = new Roster { Seed = Seed };
            var held = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Sal", Surname = "Rizzo",
            };
            var bailed = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Nunzio", Surname = "Alto",
            };
            roster.Members.Add(held);
            roster.Members.Add(bailed);

            var pipe = new Police.PrisonPipeline { RosterSeed = roster.Seed };
            var file = pipe.OpenCase(Deed.Extortion, 0, 10, 15, "shop-4", "THE DELICATESSEN");
            file.Witnesses.Add(new Police.Witness
            {
                Kind = Police.WitnessKind.Complainant, Name = "Aldo Bruni", Seed = 7,
                X = 12.5f, Y = 0f, Z = -40.25f, BusinessId = "shop-4",
            });
            file.Witnesses.Add(new Police.Witness
            {
                Kind = Police.WitnessKind.Eyewitness, Name = "Rosa Conti", Seed = 9,
                X = 3f, Y = 0f, Z = 7f,
                Standing = Police.WitnessStanding.Withdrawn,
            });
            var complaint = pipe.OpenCase(Deed.Extortion, 0, 12, 0, "shop-9", "THE BARBER");

            var inside = pipe.Book(roster, held.Id, Deed.Extortion, 10, file);
            var out_ = pipe.Book(roster, bailed.Id, Deed.Extortion, 10, file);
            pipe.PostBail(roster, out_, Police.PrisonPipeline.BailPrice(out_), 10);
            pipe.SkipBail(out_);

            var escaped = new List<int>();
            pipe.CollectEscapes(escaped);
            var written = JsonUtility.FromJson<CampaignFile>(JsonUtility.ToJson(
                new CampaignFile
                {
                    prisoners = Save.PrisonSnapshot.Prisoners(pipe),
                    cases = Save.PrisonSnapshot.Cases(pipe),
                    nextCaseId = pipe.NextCaseId,
                    escaped = escaped.ToArray(),
                    prisonRosterSeed = pipe.RosterSeed,
                }));

            var back = new Police.PrisonPipeline();
            Save.PrisonSnapshot.Restore(back, written);

            if (back.Cases.Count != pipe.Cases.Count)
            {
                failures.Add("SAVE-006: " + pipe.Cases.Count + " cases were on the " +
                             "docket and " + back.Cases.Count + " came back - a held " +
                             "man with no case is convicted without a roll.");
                return;
            }

            var restored = back.CaseOf(held.Id);
            if (restored == null || restored.CaseId != file.CaseId)
            {
                failures.Add("SAVE-006: the man in the cells came back off his docket " +
                             "number.");
                return;
            }
            if (back.Find(held.Id) == null || back.Find(held.Id).CaseId != file.CaseId)
                failures.Add("SAVE-006: his prisoner row lost the case it points at.");

            if (restored.Witnesses.Count != 2)
                failures.Add("SAVE-006: the witness list came back " +
                             restored.Witnesses.Count + " long instead of 2.");
            else
            {
                var complainant = restored.Witnesses[0];
                if (complainant.Name != "Aldo Bruni" || complainant.Seed != 7 ||
                    complainant.BusinessId != "shop-4" ||
                    Mathf.Abs(complainant.X - 12.5f) > 0.001f ||
                    Mathf.Abs(complainant.Z + 40.25f) > 0.001f)
                    failures.Add("SAVE-006: the complainant came back a different man " +
                                 "or standing somewhere else - the map draws his " +
                                 "marker off that position.");
                if (restored.Witnesses[1].Standing != Police.WitnessStanding.Withdrawn)
                    failures.Add("SAVE-006: a witness the player had already silenced " +
                                 "came back willing to testify.");
            }

            if (restored.Where != "THE DELICATESSEN" || restored.BusinessId != "shop-4" ||
                restored.CourtDay != 15 || restored.OpenedDay != 10)
                failures.Add("SAVE-006: the case came back with a different heading.");

            var skipped = back.Find(bailed.Id);
            if (skipped == null || skipped.Stage != Police.PrisonStage.Bailed)
                failures.Add("SAVE-006: the bailed man did not come back on bail.");
            else if (skipped.BailPaid <= 0 || !skipped.SkipOrdered)
                failures.Add("SAVE-006: his bail money and the boss's order to skip " +
                             "were lost - the forfeit would refund itself.");

            // The verdicts a closed case collected are the archive.
            back.CutLoose(held.Id, 16);
            var line = back.CaseOf(held.Id) ?? restored;
            if (line.VerdictFor(held.Id) == null)
                failures.Add("SAVE-006: a restored case takes no verdict.");

            var backAgain = new Police.PrisonPipeline();
            Save.PrisonSnapshot.Restore(backAgain, JsonUtility.FromJson<CampaignFile>(
                JsonUtility.ToJson(new CampaignFile
                {
                    prisoners = Save.PrisonSnapshot.Prisoners(back),
                    cases = Save.PrisonSnapshot.Cases(back),
                    nextCaseId = back.NextCaseId,
                    escaped = new int[0],
                    prisonRosterSeed = back.RosterSeed,
                })));
            var archived = backAgain.FindCase(file.CaseId);
            if (archived == null || archived.VerdictFor(held.Id) == null ||
                archived.VerdictFor(held.Id).Outcome != Police.CaseOutcome.CutLoose ||
                archived.VerdictFor(held.Id).Day != 16)
                failures.Add("SAVE-006: a verdict was lost by saving, so the ledger's " +
                             "archive would forget what the court did.");

            if (backAgain.NextCaseId <= complaint.CaseId)
                failures.Add("SAVE-006: the next docket number would collide with a " +
                             "case already on the books.");
        }

        /// <summary>EPIC 35. The newspaper is a city book, not one family's runner,
        /// and its explicit top-level DTO must retain both the facts and the delivery
        /// latch that prevents a loaded morning from printing twice.</summary>
        static void ThePublicBookComesBackWithTheCampaign(List<string> failures)
        {
            var before = new PressBook();
            before.Add(new PressRecord
            {
                Day = 30,
                Hour = 2.75f,
                Kind = PressKind.Verdict,
                Where = "The Flats",
                Factions = new[] { 7 },
                NamedGangId = 7,
                Attribution = PressAttribution.Named,
                Names = new[] { "Sal Rizzo" },
                Models = new[] { "SM_Chr_Goon_01_AI" },
                Deed = Deed.Extortion,
                CaseId = 41,
                Outcome = (int)Police.CaseOutcome.Convicted,
                SentenceDays = 365,
                Weight = 73,
            });
            before.LastEditionDay = 29;

            var file = JsonUtility.FromJson<CampaignFile>(JsonUtility.ToJson(
                new CampaignFile
                {
                    day = 30,
                    press = PressSnapshot.Snapshot(before),
                    lastEditionDay = before.LastEditionDay,
                }));
            var after = new PressBook();
            PressSnapshot.Restore(after, file);

            if (after.Count != 1 || after.LastEditionDay != 29 ||
                after[0].Day != 30 || Mathf.Abs(after[0].Hour - 2.75f) > 0.001f ||
                after[0].Kind != PressKind.Verdict || after[0].CaseId != 41 ||
                after[0].Factions.Length != 1 || after[0].Factions[0] != 7 ||
                after[0].Names.Length != 1 || after[0].Names[0] != "Sal Rizzo")
                failures.Add("SAVE-009: the public press book or its last-edition " +
                             "latch changed in the campaign file.");
        }

        /// <summary>Version 2 had a gang on each docket but no gang on a prisoner row.
        /// Missing JSON fields may deserialize as zero, so migration deliberately takes
        /// the docket's owner instead of trusting the new field's apparent default.</summary>
        static void AVersionTwoPrisonerFindsHisHouseFromTheDocket(List<string> failures)
        {
            const string legacy =
                "{\"version\":2,\"prisoners\":[{" +
                "\"characterId\":700000,\"deed\":0,\"takenOnDay\":10," +
                "\"courtDay\":15,\"stage\":0,\"caseId\":41}]," +
                "\"cases\":[{\"caseId\":41,\"deed\":0,\"gangId\":7," +
                "\"defendants\":[700000],\"openedDay\":10,\"courtDay\":15," +
                "\"status\":0}],\"nextCaseId\":42}";
            var file = JsonUtility.FromJson<CampaignFile>(legacy);
            var pipe = new Police.PrisonPipeline();
            Save.PrisonSnapshot.Restore(pipe, file);
            var prisoner = pipe.Find(700000);
            if (prisoner == null || prisoner.GangId != 7)
                failures.Add("SAVE-010: a version-2 rival prisoner was assigned to " +
                             "the player's house instead of his docket's house.");
        }

        static void BodyEvidenceSurvivesTheFile(List<string> failures)
        {
            var pipe = new Police.PrisonPipeline();
            var body = RoadDemo.PoliceDispatch.OpenCivilianDeathCase(
                pipe, new TerritoryGangId(7), 12, "biz:counter", "THE COUNTER");
            var written = new CampaignFile
            {
                cases = Save.PrisonSnapshot.Cases(pipe),
                nextCaseId = pipe.NextCaseId,
            };
            var read = JsonUtility.FromJson<CampaignFile>(JsonUtility.ToJson(written));
            var back = new Police.PrisonPipeline();
            Save.PrisonSnapshot.Restore(back, read);
            var restored = body != null ? back.FindCase(body.CaseId) : null;

            if (restored == null || !restored.BodyEvidence || !restored.AnyEvidence())
                failures.Add("CNTR-AUDIT: a murder body disappeared across a version-3 save.");
        }

        static void BadEnumIntegersStopAtTheLoadBoundary(List<string> failures)
        {
            // A malformed order is discarded at restore, before an unknown type can
            // reach OrderTable's exhaustive switches. A sound row beside it survives.
            var source = Underworld.Deal(Seed);
            var dto = OutfitSnapshot.Snapshot(source);
            var runner = dto.houses[0].runner;
            runner.jobs = new[]
            {
                SavedJob(70, int.MaxValue, (int)JobStage.Queued, -1),
                SavedJob(71, (int)OrderType.Donate, int.MaxValue, -1),
                SavedJob(72, (int)OrderType.Donate, (int)JobStage.Queued, int.MaxValue),
                SavedJob(73, (int)OrderType.Donate, (int)JobStage.Queued, -1),
            };
            var restoredWorld = Underworld.Deal(Seed);
            OutfitSnapshot.Restore(restoredWorld, dto);
            var jobs = restoredWorld.Of(0).Runner.Book.Jobs;
            if (jobs.Count != 1 || jobs[0].Id != 73 ||
                jobs[0].Type != OrderType.Donate)
                failures.Add("CNTR-AUDIT: malformed order enums entered the running book.");

            // Custody is recoverable, so unsafe top-level values take conservative
            // defaults. Nested evidence/verdict rows are dropped when defaulting them
            // would manufacture testimony or a court result.
            var malformed = new CampaignFile
            {
                version = CampaignFile.Version,
                prisoners = new[]
                {
                    new PrisonerDto
                    {
                        characterId = 4,
                        deed = int.MaxValue,
                        answer = int.MaxValue,
                        stage = int.MaxValue,
                        leg = int.MaxValue,
                        caseId = 1,
                    },
                },
                cases = new[]
                {
                    new CourtCaseDto
                    {
                        caseId = 1,
                        deed = int.MaxValue,
                        gangId = 0,
                        status = int.MaxValue,
                        bodyEvidence = true,
                        extraCharges = new[] { (int)Deed.Murder, int.MaxValue },
                        witnesses = new[]
                        {
                            new WitnessDto { kind = int.MaxValue },
                            new WitnessDto
                            {
                                kind = (int)Police.WitnessKind.Eyewitness,
                                standing = int.MaxValue,
                            },
                        },
                        verdicts = new[]
                        {
                            new CaseVerdictDto { outcome = int.MaxValue },
                            new CaseVerdictDto
                            {
                                outcome = (int)Police.CaseOutcome.Dismissed,
                                answer = int.MaxValue,
                            },
                        },
                    },
                },
                nextCaseId = 2,
            };
            var pipe = new Police.PrisonPipeline();
            Save.PrisonSnapshot.Restore(pipe, malformed);
            var prisoner = pipe.Find(4);
            var file = pipe.FindCase(1);
            if (prisoner == null || prisoner.Deed != Deed.Affray ||
                prisoner.Answer != Police.DoorAnswer.Quiet ||
                prisoner.Stage != Police.PrisonStage.Held ||
                prisoner.Leg != Police.PrisonLeg.None)
                failures.Add("CNTR-AUDIT: malformed custody enums survived restoration.");
            if (file == null || file.Deed != Deed.Affray ||
                file.Status != Police.CaseStatus.Open || !file.BodyEvidence ||
                file.ExtraCharges.Count != 1 || file.ExtraCharges[0] != Deed.Murder ||
                file.Witnesses.Count != 1 ||
                file.Witnesses[0].Standing != Police.WitnessStanding.Withdrawn ||
                file.Verdicts.Count != 1 ||
                file.Verdicts[0].Outcome != Police.CaseOutcome.Dismissed ||
                file.Verdicts[0].Answer != Police.DoorAnswer.Quiet)
                failures.Add("CNTR-AUDIT: malformed docket enums survived restoration.");
        }

        static JobDto SavedJob(int id, int type, int stage, int streetOutcome) =>
            new JobDto
            {
                id = id,
                crewId = 0,
                type = type,
                stage = stage,
                streetOutcome = streetOutcome,
                targetLabel = "save boundary",
                targetBusinessId = "biz:counter",
            };

        /// <summary>
        /// (e) A FILE WRITTEN BEFORE THE DOCKET (GAN-302). Version 1 kept the men and
        /// nothing of what they were answering for. Read straight through, every one of
        /// them lands on CaseId -1, which the trial reads as "no docket, no defence" and
        /// converts to a conviction with no roll at all - the lawyer counts for nothing
        /// and the case cannot be fought. The load migrates them onto a docket instead.
        ///
        /// The fixture is LITERAL version-1 JSON, with none of today's fields in it:
        /// a round trip of freshly written DTOs cannot catch a compatibility failure,
        /// because it never produces the shape the old game wrote.
        /// </summary>
        static void ASaveFromBeforeTheDocketStillGetsATrial(List<string> failures)
        {
            const string legacy =
                "{\"version\":1,\"prisoners\":[" +
                "{\"characterId\":4,\"deed\":0,\"takenOnDay\":8,\"courtDay\":13," +
                "\"sentenceDays\":0,\"outOnDay\":0,\"stage\":0}," +
                "{\"characterId\":5,\"deed\":2,\"takenOnDay\":2,\"courtDay\":7," +
                "\"sentenceDays\":9,\"outOnDay\":16,\"stage\":4}]," +
                "\"escaped\":[9],\"prisonRosterSeed\":1987}";

            var read = JsonUtility.FromJson<CampaignFile>(legacy);
            if (read == null || read.prisoners == null || read.prisoners.Length != 2)
            {
                failures.Add("SAVE-007: the legacy fixture did not parse.");
                return;
            }
            if (read.version > CampaignFile.Version)
            {
                failures.Add("SAVE-007: a file written before the docket is refused as " +
                             "if it came from a newer game.");
                return;
            }
            if (read.cases != null && read.cases.Length > 0)
                failures.Add("SAVE-007: the legacy fixture is not legacy - it has cases.");

            var pipe = new Police.PrisonPipeline();
            Save.PrisonSnapshot.Restore(pipe, read);

            var held = pipe.Find(4);
            if (held == null)
            {
                failures.Add("SAVE-007: the man in the cells was lost by the migration.");
                return;
            }
            if (held.CaseId < 0 || pipe.CaseOf(4) == null)
                failures.Add("SAVE-007: he came back with no docket behind him, so his " +
                             "court day convicts him without a roll.");
            var file = pipe.CaseOf(4);
            if (file != null)
            {
                if (!file.AnyWilling())
                    failures.Add("SAVE-007: a migrated case with nobody to give evidence " +
                                 "would be dismissed outright - a gift, not a migration.");
                if (!file.Has(Police.WitnessKind.PoliceFoundThem) ||
                    file.Has(Police.WitnessKind.PoliceSawIt) ||
                    file.WillingEyewitnesses() > 0)
                    failures.Add("SAVE-007: a record that kept no scene is worth the " +
                                 "ARRESTING officer and nothing stronger.");
                if (file.CourtDay != held.CourtDay || file.GangId != 0)
                    failures.Add("SAVE-007: the migrated case is heard on a different " +
                                 "day, or against a different house, from the man on it.");
                // And it must be beatable: that is the whole point of putting him on a
                // docket rather than leaving him on the no-defence path.
                var alone = Police.Verdict.ConvictionChance(
                    file.Deed, 0, false, true, false, 0, 5);
                if (alone >= 0.5f)
                    failures.Add("SAVE-007: a legacy case a good lawyer cannot beat is " +
                                 "the old conviction with extra steps (" + alone + ").");
            }

            // A man already serving is NOT put up for a trial he has had.
            var serving = pipe.Find(5);
            if (serving != null && serving.CaseId >= 0)
                failures.Add("SAVE-007: a man already sentenced was given a fresh case.");

            if (!pipe.EverEscaped(9))
                failures.Add("SAVE-007: the legacy escape record was dropped.");
        }

        static void OwnerGenerationsSurviveVersionThree(List<string> failures)
        {
            var file = new CampaignFile
            {
                version = CampaignFile.Version,
                citySeed = Seed,
                ownerGenerations = new[]
                {
                    new OwnerGenerationDto { businessId = "biz:one", generation = 1 },
                    new OwnerGenerationDto { businessId = "biz:two", generation = 3 },
                },
            };
            var read = JsonUtility.FromJson<CampaignFile>(JsonUtility.ToJson(file));
            var generations = CampaignSave.OwnerGenerationMap(read);
            if (read == null || read.version != 3 || generations.Count != 2 ||
                !generations.TryGetValue(new TerritoryBusinessId("biz:one"), out var one) ||
                one != 1 ||
                !generations.TryGetValue(new TerritoryBusinessId("biz:two"), out var two) ||
                two != 3)
                failures.Add("EMPT-003: owner generations did not survive a version-3 campaign file.");
        }

        static void VersionTwoMeansOriginalOwners(List<string> failures)
        {
            const string old = "{\"version\":2,\"citySeed\":1987," +
                               "\"ownerGenerations\":[{\"businessId\":\"biz:one\"," +
                               "\"generation\":9}]}";
            var read = JsonUtility.FromJson<CampaignFile>(old);
            if (read == null || read.version != CampaignFile.VersionBeforeOwnerGenerations ||
                CampaignSave.OwnerGenerationMap(read).Count != 0)
                failures.Add("EMPT-003: a version-2 file did not migrate to generation zero.");
        }

        static void SuccessorReplayOrderDoesNotMatter(List<string> failures)
        {
            var forward = ReplaySuccessors(reverse: false);
            var reverse = ReplaySuccessors(reverse: true);
            if (forward[0] != reverse[0] || forward[1] != reverse[1] ||
                string.IsNullOrEmpty(forward[0]) || string.IsNullOrEmpty(forward[1]))
                failures.Add("EMPT-003: replay order swapped deterministic successor names.");
        }

        static string[] ReplaySuccessors(bool reverse)
        {
            var sites = new[]
            {
                SuccessorSite("one"),
                SuccessorSite("two"),
            };
            var directory = new BusinessDirectory();
            var ids = new TerritoryBusinessId[sites.Length];
            for (var i = 0; i < sites.Length; i++)
            {
                var ownerId = BusinessIdentity.Owner(sites[i].SiteId);
                directory.RegisterOwner(ownerId, BusinessOwnerKind.Individual,
                    "Original " + i, BusinessOwnerAge.Middle, i);
                ids[i] = directory.Register(
                    sites[i].SiteId, BusinessArchetypeId.Grocer, "Shop " + i,
                    ownerId, BusinessSiteSize.Small, 1_200, "save").Id;
            }

            for (var n = 0; n < sites.Length; n++)
            {
                var i = reverse ? sites.Length - 1 - n : n;
                BusinessSuccession.Replace(directory, sites[i], ids[i], Seed, i + 1);
            }

            var names = new string[sites.Length];
            for (var i = 0; i < sites.Length; i++)
            {
                directory.TryGet(ids[i], out var business);
                if (business != null && directory.TryGetOwner(business.OwnerId, out var owner))
                    names[i] = owner.DisplayName;
            }
            return names;
        }

        static BusinessSite SuccessorSite(string key) => new BusinessSite(
            "save", "successors", key,
            new TerritoryBounds(0f, 0f, 10f, 10f),
            new TerritoryPoint(5f, 0f), new TerritoryPoint(0f, -1f),
            BusinessSignage.None, BusinessSiteSize.Small, default, 0,
            "Shop " + key, "frontage", 0);

        /// <summary>(f) A file from a later game is refused, not half-read.</summary>
        static void ANewerSaveIsRefused(List<string> failures)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "gangsters-newer-save.json");
            var file = new CampaignFile { version = CampaignFile.Version + 1 };
            System.IO.File.WriteAllText(path, JsonUtility.ToJson(file));

            var read = CampaignSave.Read(path, out var refusal);
            if (read != null)
                failures.Add("SAVE-004: a save from a newer game was read anyway.");
            if (string.IsNullOrEmpty(refusal))
                failures.Add("SAVE-004: it was refused without saying why.");

            try
            {
                System.IO.File.Delete(path);
            }
            catch (System.Exception)
            {
                // A temp file that will not delete is not this contract's business.
            }
        }

        // ------------------------------------------------------------------ the bench

        /// <summary>An underworld with something wrong with every part of it - the only
        /// kind worth round tripping.</summary>
        static Underworld Mutated()
        {
            var world = Underworld.Deal(Seed);

            var ours = world.Of(0);
            ours.Runner.Accounts.Safe = 41_250;
            ours.Runner.Heat = 17;
            ours.Runner.Campaign.Day = 30;
            ours.Front = new TerritoryBusinessId("biz:our-front");

            // THE PLAYER OPENS ON THE DON ALONE. The men, the crews and the orders to
            // round trip are a FAMILY's - house 7 is dealt a full book on day one, which
            // is what makes it worth writing down.
            var them = world.Of(7);

            // A man in a bed, a man struck off, a man short in the count.
            var men = them.Roster.Members;
            for (var i = 0; i < men.Count; i++)
            {
                if (men[i].Rank != Rank.Hood || men[i].Gone)
                    continue;
                RosterOps.Hospitalize(them.Roster, men[i].Id, 36, "two ribs");
                break;
            }
            for (var i = men.Count - 1; i >= 0; i--)
            {
                if (men[i].Rank != Rank.Hood || men[i].Gone)
                    continue;
                RosterOps.Kill(them.Roster, men[i].Id);
                break;
            }
            for (var i = 0; i < men.Count; i++)
                if (!men[i].Gone && men[i].Rank == Rank.Hood)
                {
                    men[i].Skimming = true;
                    break;
                }

            // An order in flight.
            if (them.Roster.Crews.Count > 0)
                them.Runner.Issue(them.Roster, new Job
                {
                    CrewId = them.Roster.Crews[0].Id,
                    Type = OrderType.Guard,
                    Men = 2,
                    TargetBusinessId = "biz:our-front",
                    TargetBlockId = 4,
                });

            // A war coming, and a grudge behind it.
            world.Relations.SetPending(0, 5, Stance.Truce);
            world.Relations.Note(0, 5, GrievanceKind.DoorAttacked);
            world.Relations.Note(5, 0, GrievanceKind.ManKilled);
            world.Relations.ApplyPending();
            world.Relations.SetPending(0, 9, Stance.War);

            them.Runner.Accounts.Safe = 9_000;
            them.Runner.Accounts.Current.JobIncome = 733;

            // A canonical bag branch. Its node and escorts are deliberately removed
            // from HoodIds by the real roster operations before the snapshot is taken.
            for (var i = 0; i < them.Roster.Crews.Count; i++)
            {
                var crew = them.Roster.Crews[i];
                if (crew.HoodIds.Count < 3)
                    continue;
                var collector = crew.HoodIds[0];
                var firstEscort = crew.HoodIds[1];
                var secondEscort = crew.HoodIds[2];
                if (RosterOps.NameCollector(them.Roster, crew.Id, collector).Ok &&
                    RosterOps.PostEscort(them.Roster, crew.Id, firstEscort).Ok &&
                    RosterOps.PostEscort(them.Roster, crew.Id, secondEscort).Ok)
                    break;
            }
            them.NextThinkHour = 34.5;
            return world;
        }

        static TerritoryComplianceInputs Strong() =>
            new TerritoryComplianceInputs(70f, 60f, 10f, 0f, 0f, false);

        static TerritoryComplianceInputs Hopeless() =>
            new TerritoryComplianceInputs(0f, 0f, 0f, 40f, 0f, false);

        // -------------------------------------------------------------------- dumps

        /// <summary>
        /// EVERY FIGURE THE CAMPAIGN HAS, as one string. A dump is the only honest way
        /// to assert a save: a handful of spot checks passes while the thing it did not
        /// check is quietly lost.
        /// </summary>
        static string Dump(Underworld world)
        {
            var sb = new StringBuilder();
            sb.Append("seed ").Append(world.CitySeed).Append('\n');

            for (var g = 0; g < world.Count; g++)
            {
                var house = world.Of(g);
                if (house == null)
                    continue;
                sb.Append("house ").Append(house.GangId)
                  .Append(" front=").Append(house.Front.Value)
                  .Append(" think=").Append(house.NextThinkHour.ToString("F2"))
                  .Append(" safe=").Append(house.Runner.Accounts.Safe)
                  .Append(" risky=").Append(house.Runner.Accounts.RiskyMoney)
                  .Append(" heat=").Append(house.Runner.Heat)
                  .Append(" day=").Append(house.Runner.Campaign.Day)
                  .Append('\n');

                var roster = house.Roster;
                sb.Append("  roster gang=").Append(roster.GangId)
                  .Append(" seed=").Append(roster.Seed)
                  .Append(" day=").Append(roster.Day)
                  .Append(" boss=").Append(roster.Organization.BossId)
                  .Append(" front=").Append(roster.FrontId)
                  .Append('\n');

                for (var i = 0; i < roster.Members.Count; i++)
                {
                    var man = roster.Members[i];
                    sb.Append("  man ").Append(man.Id).Append(' ').Append(man.FullName)
                      .Append(' ').Append(man.Rank).Append(' ').Append(man.Status)
                      .Append(" duty=").Append(man.Duty)
                      .Append(" back=").Append(man.BackOnDay)
                      .Append(" loyal=").Append(man.Loyalty)
                      .Append(" skim=").Append(man.Skimming)
                      .Append(" note=").Append(man.ConditionNote);
                    for (var a = 0; a < AttributeScale.Count; a++)
                        sb.Append(' ')
                          .Append(man.GetHalfSteps((CharacterAttribute)a)).Append('/')
                          .Append(man.GetPractice((CharacterAttribute)a)).Append('/')
                          .Append(man.PotentialValue((CharacterAttribute)a));
                    sb.Append('\n');
                }

                for (var i = 0; i < roster.Crews.Count; i++)
                {
                    var crew = roster.Crews[i];
                    sb.Append("  crew ").Append(crew.Id).Append(" lt=")
                      .Append(crew.LieutenantId).Append(" policy=").Append(crew.Policy)
                      .Append(" bag=").Append(crew.BagId)
                      .Append(" named=").Append(crew.BagNamedByBoss)
                      .Append('/').Append(crew.BagNamedId)
                      .Append(" hoods=");
                    for (var h = 0; h < crew.HoodIds.Count; h++)
                        sb.Append(crew.HoodIds[h]).Append(',');
                    sb.Append(" escorts=");
                    for (var e = 0; e < crew.EscortIds.Count; e++)
                        sb.Append(crew.EscortIds[e]).Append(',');
                    sb.Append('\n');
                }

                for (var i = 0; i < roster.Equipment.Count; i++)
                {
                    var item = roster.Equipment[i];
                    sb.Append("  kit ").Append(item.Id).Append(' ').Append(item.Kind)
                      .Append(' ').Append(item.DisplayName)
                      .Append(" owner=").Append(item.OwnerId)
                      .Append(" holder=").Append(item.HolderId)
                      .Append(" pinned=").Append(item.PinnedTo)
                      .Append('\n');
                }

                var paper = roster.Organization.BlockResponsibilities;
                for (var i = 0; i < paper.Count; i++)
                    sb.Append("  paper ").Append(paper[i].BlockId.Value).Append(' ')
                      .Append(paper[i].LeaderId).Append('\n');

                for (var i = 0; i < house.Runner.Book.Jobs.Count; i++)
                {
                    var job = house.Runner.Book.Jobs[i];
                    sb.Append("  job ").Append(job.Id).Append(' ').Append(job.Type)
                      .Append(" crew=").Append(job.CrewId)
                      .Append(" stage=").Append(job.Stage)
                      .Append(" travel=").Append(job.TravelHoursLeft.ToString("F2"))
                      .Append(" work=").Append(job.WorkHoursLeft.ToString("F2"))
                      .Append(" target=").Append(job.TargetBusinessId)
                      .Append(" man=").Append(job.TargetCharacterId)
                      .Append('\n');
                }

                for (var i = 0; i < house.Runner.Accounts.Sheets.Count; i++)
                {
                    var sheet = house.Runner.Accounts.Sheets[i];
                    sb.Append("  sheet ").Append(sheet.Day)
                      .Append(" legal=").Append(sheet.LegalIncome)
                      .Append(" illegal=").Append(sheet.IllegalIncome)
                      .Append(" jobs=").Append(sheet.JobIncome)
                      .Append(" wages=").Append(sheet.WagesPaid)
                      .Append(" closed=").Append(sheet.Closed)
                      .Append('\n');
                }
            }

            for (var a = 0; a < world.Count; a++)
                for (var b = 0; b < world.Count; b++)
                {
                    if (a == b)
                        continue;
                    var owed = world.Relations.Grievance(a, b);
                    if (owed > 0f)
                        sb.Append("owed ").Append(a).Append("->").Append(b).Append(' ')
                          .Append(owed.ToString("F2")).Append('\n');
                    if (a >= b)
                        continue;
                    var stance = world.Relations.StanceBetween(a, b);
                    if (stance != Stance.Peace)
                        sb.Append("stance ").Append(a).Append('-').Append(b).Append(' ')
                          .Append(stance).Append('\n');
                    if (world.Relations.TryGetPending(a, b, out var pending))
                        sb.Append("pending ").Append(a).Append('-').Append(b).Append(' ')
                          .Append(pending).Append('\n');
                }

            return sb.ToString();
        }

        static string Dump(
            TerritoryRacketLedger racket, TerritoryDuesLedger dues,
            TerritoryRoundLedger rounds)
        {
            var sb = new StringBuilder();
            var protection = new List<ProtectionRowDto>();
            racket.Collect(protection);
            protection.Sort((x, y) =>
            {
                var by = string.CompareOrdinal(x.businessId, y.businessId);
                return by != 0 ? by : x.gangId.CompareTo(y.gangId);
            });
            for (var i = 0; i < protection.Count; i++)
                sb.Append("door ").Append(protection[i].businessId).Append(' ')
                  .Append(protection[i].gangId).Append(' ').Append(protection[i].state)
                  .Append(" since=").Append(protection[i].stateSince.ToString("F2"))
                  .Append(" refused=").Append(protection[i].refusedAt.ToString("F2"))
                  .Append(" d/t/e=").Append(protection[i].demands).Append('/')
                  .Append(protection[i].threats).Append('/')
                  .Append(protection[i].escalations).Append('\n');

            var owed = new List<DuesRowDto>();
            dues.Collect(owed);
            owed.Sort((x, y) => string.CompareOrdinal(x.businessId, y.businessId));
            for (var i = 0; i < owed.Count; i++)
                sb.Append("dues ").Append(owed[i].businessId).Append(' ')
                  .Append(owed[i].gangId).Append(" rate=").Append(owed[i].weeklyRate)
                  .Append(" sevenths=").Append(owed[i].owedSevenths)
                  .Append(" last=").Append(owed[i].lastCollectedDay)
                  .Append(" missed=").Append(owed[i].missedInARow).Append('\n');

            for (var i = 0; i < rounds.Rounds.Count; i++)
            {
                var round = rounds.Rounds[i];
                sb.Append("round ").Append(round.House.Value).Append(' ')
                  .Append(round.CrewId).Append(' ').Append(round.BlockId.Value)
                  .Append(' ').Append(round.Kind).Append(' ').Append(round.Stage)
                  .Append(" cursor=").Append(round.StopIndex)
                  .Append(" carried=").Append(round.Carried)
                  .Append(" missed=").Append(round.Missed)
                  .Append(" inside=").Append(round.InTheDoor)
                  .Append(" stops=");
                for (var s = 0; s < round.Stops.Count; s++)
                    sb.Append(round.Stops[s].BusinessId.Value).Append('@')
                      .Append(round.Stops[s].Doorstep.X.ToString("F1")).Append(',')
                      .Append(round.Stops[s].Doorstep.Z.ToString("F1")).Append(';');
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>The first line the two dumps disagree on - a diff nobody has to
        /// squint at.</summary>
        static string FirstDifference(string a, string b)
        {
            var left = a.Split('\n');
            var right = b.Split('\n');
            for (var i = 0; i < left.Length || i < right.Length; i++)
            {
                var x = i < left.Length ? left[i] : "<end>";
                var y = i < right.Length ? right[i] : "<end>";
                if (x != y)
                    return "line " + (i + 1) + ": saved [" + x + "] loaded [" + y + "]";
            }
            return "no line differs, but the dumps are not equal.";
        }
    }
}
