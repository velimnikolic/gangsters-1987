using LivingCity.News;

namespace LivingCity.Save
{
    /// <summary>The explicit conversion between the engine-free public book and
    /// JsonUtility's field-only campaign DTO.</summary>
    public static class PressSnapshot
    {
        public static PressDto[] Snapshot(PressBook book)
        {
            if (book == null)
                return new PressDto[0];
            var rows = new PressDto[book.Count];
            for (var i = 0; i < book.Count; i++)
            {
                var story = book[i];
                rows[i] = new PressDto
                {
                    day = story.Day,
                    hour = story.Hour,
                    kind = (int)story.Kind,
                    where = story.Where,
                    business = story.Business,
                    factions = story.Factions,
                    namedGangId = story.NamedGangId,
                    attribution = (int)story.Attribution,
                    witnesses = story.Witnesses,
                    gangsterDeaths = story.GangsterDeaths,
                    civilianDeaths = story.CivilianDeaths,
                    officerDeaths = story.OfficerDeaths,
                    shots = story.Shots,
                    names = story.Names,
                    models = story.Models,
                    deed = (int)story.Deed,
                    sentenceDays = story.SentenceDays,
                    caseId = story.CaseId,
                    incidentNumber = story.IncidentNumber,
                    outcome = story.Outcome,
                    weight = story.Weight,
                };
            }
            return rows;
        }

        public static void Restore(PressBook book, CampaignFile file)
        {
            if (book == null || file == null)
                return;

            // Earlier files never carried a public book. Starting it empty and marking
            // that saved day delivered prevents a fabricated old edition after load.
            if (file.version <= CampaignFile.VersionBeforePress)
            {
                book.Restore(null, file.day);
                return;
            }

            var rows = new PressRecord[file.press != null ? file.press.Length : 0];
            for (var i = 0; i < rows.Length; i++)
            {
                var saved = file.press[i];
                if (saved == null) continue;
                rows[i] = new PressRecord
                {
                    Day = saved.day,
                    Hour = saved.hour,
                    Kind = (PressKind)saved.kind,
                    Where = saved.where ?? "",
                    Business = saved.business ?? "",
                    Factions = saved.factions ?? System.Array.Empty<int>(),
                    NamedGangId = saved.namedGangId,
                    Attribution = (PressAttribution)saved.attribution,
                    Witnesses = saved.witnesses,
                    GangsterDeaths = saved.gangsterDeaths,
                    CivilianDeaths = saved.civilianDeaths,
                    OfficerDeaths = saved.officerDeaths,
                    Shots = saved.shots,
                    Names = saved.names ?? System.Array.Empty<string>(),
                    Models = saved.models ?? System.Array.Empty<string>(),
                    Deed = (Personnel.Deed)saved.deed,
                    SentenceDays = saved.sentenceDays,
                    CaseId = saved.caseId,
                    IncidentNumber = saved.incidentNumber,
                    Outcome = saved.outcome,
                    Weight = saved.weight,
                };
            }
            book.Restore(rows, file.lastEditionDay);
        }
    }
}
