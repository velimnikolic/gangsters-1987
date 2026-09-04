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
        public static PrisonerDto[] Prisoners(PrisonPipeline pipe)
        {
            if (pipe == null)
                return new PrisonerDto[0];

            var rows = new PrisonerDto[pipe.Inside.Count];
            for (var i = 0; i < pipe.Inside.Count; i++)
            {
                var man = pipe.Inside[i];
                rows[i] = new PrisonerDto
                {
                    characterId = man.CharacterId,
                    gangId = man.GangId,
                    deed = (int)man.Deed,
                    answer = (int)man.Answer,
                    sprung = man.Sprung,
                    takenOnDay = man.TakenOnDay,
                    courtDay = man.CourtDay,
                    sentenceDays = man.SentenceDays,
                    outOnDay = man.OutOnDay,
                    stage = (int)man.Stage,
                    caseId = man.CaseId,
                    leg = (int)man.Leg,
                    prisonDay = man.PrisonDay,
                    bailPaid = man.BailPaid,
                    skipOrdered = man.SkipOrdered,
                    transferFails = man.TransferFails,
                };
            }
            return rows;
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
                inside.Add(new Prisoner
                {
                    CharacterId = row.characterId,
                    GangId = row.gangId,
                    Deed = (Deed)row.deed,
                    Answer = (DoorAnswer)row.answer,
                    Sprung = row.sprung,
                    TakenOnDay = row.takenOnDay,
                    CourtDay = row.courtDay,
                    SentenceDays = row.sentenceDays,
                    OutOnDay = row.outOnDay,
                    Stage = (PrisonStage)row.stage,
                    CaseId = row.caseId,
                    Leg = (PrisonLeg)row.leg,
                    PrisonDay = row.prisonDay,
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
                var reopened = new CourtCase
                {
                    CaseId = row.caseId,
                    Deed = (Deed)row.deed,
                    GangId = row.gangId,
                    BusinessId = row.businessId ?? "",
                    Where = row.where ?? "",
                    OpenedDay = row.openedDay,
                    CourtDay = row.courtDay,
                    LawyerId = row.lawyerId,
                    Status = (CaseStatus)row.status,
                    AnyTried = row.anyTried,
                };
                for (var d = 0; row.defendants != null && d < row.defendants.Length; d++)
                    reopened.Defendants.Add(row.defendants[d]);
                for (var c = 0; row.counts != null && c < row.counts.Length; c++)
                    reopened.Counts.Add(row.counts[c]);
                for (var c = 0; row.extraCharges != null && c < row.extraCharges.Length; c++)
                    reopened.ExtraCharges.Add((Deed)row.extraCharges[c]);
                for (var w = 0; row.witnesses != null && w < row.witnesses.Length; w++)
                {
                    var witness = row.witnesses[w];
                    if (witness == null) continue;
                    reopened.Witnesses.Add(new Witness
                    {
                        Kind = (WitnessKind)witness.kind,
                        Name = witness.name ?? "",
                        Seed = witness.seed,
                        X = witness.x, Y = witness.y, Z = witness.z,
                        Standing = (WitnessStanding)witness.standing,
                        BusinessId = witness.businessId ?? "",
                    });
                }
                for (var v = 0; row.verdicts != null && v < row.verdicts.Length; v++)
                {
                    var verdict = row.verdicts[v];
                    if (verdict == null) continue;
                    reopened.Verdicts.Add(new CaseVerdict
                    {
                        CharacterId = verdict.characterId,
                        Outcome = (CaseOutcome)verdict.outcome,
                        Days = verdict.days,
                        OutOnDay = verdict.outOnDay,
                        Day = verdict.day,
                        Answer = (DoorAnswer)verdict.answer,
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
