using System;
using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Police;

namespace LivingCity.Save
{
    /// <summary>
    /// THE PIPE AND THE DOCKET, TO DISK AND BACK (GAN-302).
    ///
    /// One place, engine-free, so the campaign file and the headless contract that
    /// guards it run the SAME conversion. They used not to: the save wrote its own
    /// fields and the test hand-rolled a copy of them, which is exactly how the docket
    /// came to be missing from the file for a fortnight without a test noticing - a man
    /// loaded out of a save was tried with no case behind him, which the trial reads as
    /// "no docket, no defence" and convicts without a roll, whatever the player had done
    /// to the witnesses.
    ///
    /// The witness BODY is deliberately not part of this. The pedestrian who saw it is
    /// gone when a scene is torn down; the name and the place he was standing are what
    /// the case needs, and the map draws its marker from the position exactly as it does
    /// for a witness who walked indoors (WitnessWatch).
    /// </summary>
    public static class PrisonSnapshot
    {
        public static PrisonerDto[] Prisoners(PrisonPipeline pipe, int today = 0)
        {
            if (pipe == null)
                return new PrisonerDto[0];

            var rows = new PrisonerDto[pipe.Inside.Count];
            for (var i = 0; i < pipe.Inside.Count; i++)
            {
                var man = pipe.Inside[i];
                // The carriage is deliberately runtime-only. Saving its paper as
                // InTransit would restore a man into a car which no longer exists and
                // leave him there forever. A live leg is filed back at its physical
                // source and put on tomorrow's call sheet instead: cells for court,
                // courthouse custody for prison.
                var liveCarriage = man.Carriage.HasValue &&
                                   man.Carriage.Value != CarriageStage.Delivered;
                var courtLeg = man.Leg != PrisonLeg.Prison;
                var stage = liveCarriage
                    ? courtLeg ? PrisonStage.Held : PrisonStage.Sentenced
                    : man.Stage;
                var courtDay = liveCarriage && courtLeg
                    ? RetryDay(man.CourtDay, today) : man.CourtDay;
                var prisonDay = liveCarriage && !courtLeg
                    ? RetryDay(man.PrisonDay, today) : man.PrisonDay;
                rows[i] = new PrisonerDto
                {
                    characterId = man.CharacterId,
                    gangId = man.GangId,
                    deed = (int)man.Deed,
                    answer = (int)man.Answer,
                    sprung = man.Sprung,
                    takenOnDay = man.TakenOnDay,
                    courtDay = courtDay,
                    sentenceDays = man.SentenceDays,
                    outOnDay = man.OutOnDay,
                    stage = (int)stage,
                    caseId = man.CaseId,
                    leg = (int)(liveCarriage ? PrisonLeg.None : man.Leg),
                    prisonDay = prisonDay,
                    bailPaid = man.BailPaid,
                    skipOrdered = man.SkipOrdered,
                    transferFails = man.TransferFails,
                };
            }
            return rows;
        }

        static int RetryDay(int scheduled, int today)
        {
            var afterSchedule = scheduled > 0 ? scheduled + 1 : 0;
            var tomorrow = today > 0 ? today + 1 : 0;
            return System.Math.Max(afterSchedule, tomorrow);
        }

        /// <summary>Every case the city has opened, open or closed - a closed one is
        /// what the ledger's archive prints.</summary>
        public static CourtCaseDto[] Cases(PrisonPipeline pipe)
        {
            if (pipe == null)
                return new CourtCaseDto[0];

            var rows = new CourtCaseDto[pipe.Cases.Count];
            for (var i = 0; i < pipe.Cases.Count; i++)
            {
                var file = pipe.Cases[i];
                var witnesses = new WitnessDto[file.Witnesses.Count];
                for (var w = 0; w < file.Witnesses.Count; w++)
                {
                    var witness = file.Witnesses[w];
                    witnesses[w] = new WitnessDto
                    {
                        kind = (int)witness.Kind,
                        name = witness.Name,
                        seed = witness.Seed,
                        x = witness.X, y = witness.Y, z = witness.Z,
                        standing = (int)witness.Standing,
                        businessId = witness.BusinessId,
                    };
                }

                var verdicts = new CaseVerdictDto[file.Verdicts.Count];
                for (var v = 0; v < file.Verdicts.Count; v++)
                {
                    var verdict = file.Verdicts[v];
                    verdicts[v] = new CaseVerdictDto
                    {
                        characterId = verdict.CharacterId,
                        outcome = (int)verdict.Outcome,
                        days = verdict.Days,
                        outOnDay = verdict.OutOnDay,
                        day = verdict.Day,
                        answer = (int)verdict.Answer,
                        sprung = verdict.Sprung,
                    };
                }

                rows[i] = new CourtCaseDto
                {
                    caseId = file.CaseId,
                    deed = (int)file.Deed,
                    gangId = file.GangId,
                    businessId = file.BusinessId,
                    where = file.Where,
                    defendants = file.Defendants.ToArray(),
                    witnesses = witnesses,
                    bodyEvidence = file.BodyEvidence,
                    counts = file.Counts.ToArray(),
                    extraCharges = ToInts(file.ExtraCharges),
                    openedDay = file.OpenedDay,
                    courtDay = file.CourtDay,
                    lawyerId = file.LawyerId,
                    status = (int)file.Status,
                    anyTried = file.AnyTried,
                    verdicts = verdicts,
                };
            }
            return rows;
        }

        /// <summary>
        /// THE LOAD BOUNDARY. Everybody the city was holding and every docket number it
        /// had opened, put back as they were.
        /// </summary>
        public static void Restore(PrisonPipeline pipe, CampaignFile file)
        {
            if (pipe == null || file == null)
                return;

            var inside = new List<Prisoner>();
            for (var i = 0; file.prisoners != null && i < file.prisoners.Length; i++)
            {
                var row = file.prisoners[i];
                if (row == null) continue;
                var restoredStage = EnumOr(row.stage, PrisonStage.Held);
                var restoredLeg = EnumOr(row.leg, PrisonLeg.None);
                var orphanedJourney = restoredStage == PrisonStage.ForTransfer ||
                                      restoredStage == PrisonStage.InTransit;
                var prisonLeg = restoredLeg == PrisonLeg.Prison;
                // Compatibility for a file written before ROAD-006: a serialized
                // ForTransfer/InTransit row cannot have a serialized carriage beside
                // it. Put that orphan back at its source exactly as new writes do.
                if (orphanedJourney)
                {
                    restoredStage = prisonLeg
                        ? PrisonStage.Sentenced : PrisonStage.Held;
                    restoredLeg = PrisonLeg.None;
                }
                inside.Add(new Prisoner
                {
                    CharacterId = row.characterId,
                    GangId = row.gangId,
                    // Keep a recoverable custody row even if one enum integer is
                    // corrupt. Affray/Quiet are the least inventive legacy meanings;
                    // unlike the raw cast, neither can explode in an exhaustive table.
                    Deed = EnumOr(row.deed, Deed.Affray),
                    Answer = EnumOr(row.answer, DoorAnswer.Quiet),
                    Sprung = row.sprung,
                    TakenOnDay = row.takenOnDay,
                    CourtDay = orphanedJourney && !prisonLeg
                        ? RetryDay(row.courtDay, file.day) : row.courtDay,
                    SentenceDays = row.sentenceDays,
                    OutOnDay = row.outOnDay,
                    Stage = restoredStage,
                    CaseId = row.caseId,
                    Leg = restoredLeg,
                    PrisonDay = orphanedJourney && prisonLeg
                        ? RetryDay(row.prisonDay, file.day) : row.prisonDay,
                    BailPaid = row.bailPaid,
                    SkipOrdered = row.skipOrdered,
                    TransferFails = row.transferFails,
                });
            }

            var docket = new List<CourtCase>();
            for (var i = 0; file.cases != null && i < file.cases.Length; i++)
            {
                var row = file.cases[i];
                if (row == null) continue;
                var deed = EnumOr(row.deed, Deed.Affray);
                var reopened = new CourtCase
                {
                    CaseId = row.caseId,
                    Deed = deed,
                    GangId = row.gangId,
                    BusinessId = row.businessId ?? "",
                    Where = row.where ?? "",
                    // A charge is not evidence. Preserve an explicit false (and the
                    // documented false default of older files) instead of creating
                    // prosecution evidence merely because the charge names a death.
                    BodyEvidence = row.bodyEvidence,
                    OpenedDay = row.openedDay,
                    CourtDay = row.courtDay,
                    LawyerId = row.lawyerId,
                    Status = EnumOr(row.status, CaseStatus.Open),
                    AnyTried = row.anyTried,
                };
                for (var d = 0; row.defendants != null && d < row.defendants.Length; d++)
                    reopened.Defendants.Add(row.defendants[d]);
                for (var c = 0; row.counts != null && c < row.counts.Length; c++)
                    reopened.Counts.Add(row.counts[c]);
                for (var c = 0; row.extraCharges != null && c < row.extraCharges.Length; c++)
                    if (TryEnum(row.extraCharges[c], out Deed extraCharge))
                        reopened.ExtraCharges.Add(extraCharge);
                for (var w = 0; row.witnesses != null && w < row.witnesses.Length; w++)
                {
                    var witness = row.witnesses[w];
                    if (witness == null ||
                        !TryEnum(witness.kind, out WitnessKind witnessKind))
                        continue;
                    reopened.Witnesses.Add(new Witness
                    {
                        Kind = witnessKind,
                        Name = witness.name ?? "",
                        Seed = witness.seed,
                        X = witness.x, Y = witness.y, Z = witness.z,
                        // An unknown standing must never be treated as testimony.
                        Standing = EnumOr(
                            witness.standing, WitnessStanding.Withdrawn),
                        BusinessId = witness.businessId ?? "",
                    });
                }
                for (var v = 0; row.verdicts != null && v < row.verdicts.Length; v++)
                {
                    var verdict = row.verdicts[v];
                    if (verdict == null ||
                        !TryEnum(verdict.outcome, out CaseOutcome caseOutcome))
                        continue;
                    reopened.Verdicts.Add(new CaseVerdict
                    {
                        CharacterId = verdict.characterId,
                        Outcome = caseOutcome,
                        Days = verdict.days,
                        OutOnDay = verdict.outOnDay,
                        Day = verdict.day,
                        Answer = EnumOr(verdict.answer, DoorAnswer.Quiet),
                        Sprung = verdict.sprung,
                    });
                }
                docket.Add(reopened);
            }

            // Version 2 knew the gang on the docket but not on the prisoner row. Carry
            // that ownership across before any multi-house day pass can touch him.
            for (var i = 0; i < inside.Count; i++)
                if ((file.version <= CampaignFile.VersionBeforePress ||
                     inside[i].GangId < 0) && inside[i].CaseId >= 0)
                    for (var c = 0; c < docket.Count; c++)
                        if (docket[c].CaseId == inside[i].CaseId)
                        {
                            inside[i].GangId = docket[c].GangId;
                            break;
                        }

            if (file.version <= CampaignFile.VersionBeforeDocket)
                MigrateFromBeforeTheDocket(inside, docket);

            pipe.RestoreFrom(inside, docket, file.escaped, file.nextCaseId,
                file.prisonRosterSeed);
        }

        static int[] ToInts(System.Collections.Generic.List<Deed> values)
        {
            var result = new int[values != null ? values.Count : 0];
            for (var i = 0; i < result.Length; i++) result[i] = (int)values[i];
            return result;
        }

        /// <summary>Every enum in a campaign file began as an arbitrary JSON integer.
        /// Validate it before it can reach an exhaustive sentence, verdict or ledger
        /// switch. Callers either choose a conservative fallback or drop the nested
        /// row when inventing one would manufacture evidence.</summary>
        static T EnumOr<T>(int raw, T fallback) where T : struct =>
            TryEnum(raw, out T value) ? value : fallback;

        static bool TryEnum<T>(int raw, out T value) where T : struct
        {
            if (Enum.IsDefined(typeof(T), raw))
            {
                value = (T)Enum.ToObject(typeof(T), raw);
                return true;
            }
            value = default(T);
            return false;
        }

        /// <summary>
        /// A FILE WRITTEN BEFORE CASES WERE SAVED (GAN-302).
        ///
        /// Version 1 kept the men and nothing of what they were answering for. Reading
        /// such a file straight through leaves every one of them on CaseId -1, which the
        /// trial reads as "no docket, no defence" and converts to a conviction with NO
        /// ROLL AT ALL: the lawyer does not count, the sentence bands do not care, and
        /// the player cannot lose a case he was never going to be allowed to fight.
        ///
        /// So each man awaiting a court day is put back on a docket number of his own,
        /// with the one witness such an arrest actually amounts to: the officer who
        /// FOUND them. It is the weakest thing on the docket by design - the arresting
        /// officer only found the crew at the scene - and it is the honest reading of a
        /// record that kept no scene. Nothing is invented in the prosecution's favour:
        /// a legacy case is the easiest kind there is to beat.
        ///
        /// A man already sentenced or in transit is left alone: his verdict has been
        /// passed and there is nothing left to try.
        /// </summary>
        static void MigrateFromBeforeTheDocket(
            List<Prisoner> inside, List<CourtCase> docket)
        {
            var nextId = 1;
            for (var i = 0; i < docket.Count; i++)
                if (docket[i].CaseId >= nextId)
                    nextId = docket[i].CaseId + 1;

            for (var i = 0; i < inside.Count; i++)
            {
                var man = inside[i];
                if (man.CaseId >= 0) continue;
                if (man.Stage != PrisonStage.Held && man.Stage != PrisonStage.Bailed &&
                    man.Stage != PrisonStage.ForTransfer &&
                    man.Stage != PrisonStage.InTransit)
                    continue;
                // A man in transit on the PRISON leg has already been sentenced; only
                // the drive to court is still a trial waiting to happen.
                if (man.Leg == PrisonLeg.Prison) continue;

                var file = new CourtCase
                {
                    CaseId = nextId++,
                    Deed = man.Deed,
                    GangId = LivingCity.Gangs.GangCatalog.PlayerGangId,
                    Where = "",
                    OpenedDay = man.TakenOnDay,
                    CourtDay = man.CourtDay,
                };
                file.Defendants.Add(man.CharacterId);
                file.Witnesses.Add(new Witness
                {
                    Kind = WitnessKind.PoliceFoundThem,
                    Name = "The arresting officer",
                });
                docket.Add(file);
                man.CaseId = file.CaseId;
                man.GangId = file.GangId;
            }
        }
    }
}
