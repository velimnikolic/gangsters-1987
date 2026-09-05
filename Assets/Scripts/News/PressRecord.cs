using System;
using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.News
{
    /// <summary>The public fact the city paper is allowed to print. Append only: the
    /// integer values are written into campaign files.</summary>
    public enum PressKind
    {
        Shootout,
        Killing,
        OfficerKilled,
        Arson,
        Bombing,
        SmashUp,
        Assault,
        Arrest,
        CustodyBroken,
        FiredOnPolice,
        ChargesFiled,
        Verdict,
        BailJumped,
        WitnessDead,
        FlatRaid,
        BossKilled,
        PremisesSold,
        PoliceBlotter,
        FledPolice,

        /// <summary>The police seized something and said so (EPIC 40): a sting at a
        /// bar, a room full of kilos. A rumour is never public; a seizure always is.</summary>
        Seizure,
    }

    /// <summary>How confidently a story may identify the people behind an act.</summary>
    public enum PressAttribution
    {
        Unknown,
        Seen,
        Named,
    }

    /// <summary>The publication gate in pure data. The scene writer supplies counts
    /// and official status; this class decides what is fit to print.</summary>
    public static class PressPolicy
    {
        public static bool StreetIncidentIsPublic(int deaths, int witnesses,
            int witnessThreshold) =>
            deaths > 0 || witnesses >= Math.Max(1, witnessThreshold);

        public static bool ComplaintIsPublic(bool statementTaken) => statementTaken;

        public static bool ShutdownChangeIsPublic(bool started, bool extended) =>
            started || extended;

        public static PressAttribution Attribution(int nonPoliceFactions,
            int witnesses, int witnessThreshold, bool officiallyNamed = false)
        {
            if (officiallyNamed)
                return PressAttribution.Named;
            return nonPoliceFactions == 1 && witnesses >= Math.Max(1, witnessThreshold)
                ? PressAttribution.Seen : PressAttribution.Unknown;
        }
    }

    /// <summary>
    /// One public record, kept as facts rather than prose. The writer resolves volatile
    /// scene data (names, neighbourhood and model names) before filing it, so back
    /// issues survive a reload without consulting the current street.
    /// </summary>
    [Serializable]
    public sealed class PressRecord
    {
        public int Day = 1;
        public float Hour;
        public PressKind Kind;
        public string Where = "";
        public string Business = "";
        public int[] Factions = Array.Empty<int>();
        /// <summary>The family of a named subject (a victim or defendant), which may
        /// differ from the people who fired.</summary>
        public int NamedGangId = -1;
        public PressAttribution Attribution;
        public int Witnesses;
        public int GangsterDeaths;
        public int CivilianDeaths;
        public int OfficerDeaths;
        public int Shots;
        public string[] Names = Array.Empty<string>();
        public string[] Models = Array.Empty<string>();
        public Deed Deed;
        public int SentenceDays;
        public int CaseId = -1;
        public int IncidentNumber = -1;

        /// <summary>CaseOutcome as an integer. Kept loose so the news core does not
        /// need to turn a court enum into a second, drifting vocabulary.</summary>
        public int Outcome;

        public int Weight;

        public int Family => Factions != null && Factions.Length == 1
            ? Factions[0]
            : -1;

        public int Toll => GangsterDeaths + CivilianDeaths + OfficerDeaths;

        public PressRecord Copy()
        {
            return new PressRecord
            {
                Day = Day,
                Hour = Hour,
                Kind = Kind,
                Where = Where ?? "",
                Business = Business ?? "",
                Factions = Factions != null ? (int[])Factions.Clone() : Array.Empty<int>(),
                NamedGangId = NamedGangId,
                Attribution = Attribution,
                Witnesses = Witnesses,
                GangsterDeaths = GangsterDeaths,
                CivilianDeaths = CivilianDeaths,
                OfficerDeaths = OfficerDeaths,
                Shots = Shots,
                Names = Names != null ? (string[])Names.Clone() : Array.Empty<string>(),
                Models = Models != null ? (string[])Models.Clone() : Array.Empty<string>(),
                Deed = Deed,
                SentenceDays = SentenceDays,
                CaseId = CaseId,
                IncidentNumber = IncidentNumber,
                Outcome = Outcome,
                Weight = Weight,
            };
        }

        public static int DefaultWeight(PressKind kind) => kind switch
        {
            PressKind.OfficerKilled => 100,
            PressKind.BossKilled => 96,
            PressKind.Killing => 88,
            PressKind.Arson => 76,
            PressKind.Bombing => 80,
            PressKind.CustodyBroken => 74,
            PressKind.Arrest => 68,
            PressKind.FiredOnPolice => 66,
            PressKind.ChargesFiled => 62,
            PressKind.Verdict => 58,
            PressKind.BailJumped => 54,
            PressKind.WitnessDead => 72,
            PressKind.FlatRaid => 48,
            PressKind.Shootout => 52,
            PressKind.SmashUp => 35,
            PressKind.Assault => 30,
            PressKind.PremisesSold => 24,
            PressKind.PoliceBlotter => 18,
            PressKind.FledPolice => 18,
            PressKind.Seizure => 64,
            _ => 20,
        };
    }

    /// <summary>The city's retained public record. It belongs to Underworld beside
    /// Relations, because every house appears in the same newspaper.</summary>
    public sealed class PressBook
    {
        /// <summary>A bounded archive: enough for many back issues without growing for
        /// the whole campaign. Oldest records leave first.</summary>
        public const int PressKept = 256;

        readonly List<PressRecord> records = new List<PressRecord>();

        public IReadOnlyList<PressRecord> Records => records;
        public int Count => records.Count;
        public int Version { get; private set; }
        public int LastEditionDay { get; set; }

        public PressRecord this[int index] => records[index];

        public void Add(PressRecord record)
        {
            if (record == null)
                return;
            if (record.Weight <= 0)
                record.Weight = PressRecord.DefaultWeight(record.Kind);
            // Opening a shooting case precedes station booking. Both are one collar,
            // so charges and arrest update a single public record rather than printing
            // twice under the same docket number.
            if (record.CaseId >= 0 &&
                (record.Kind == PressKind.Arrest || record.Kind == PressKind.ChargesFiled))
            {
                for (var i = records.Count - 1; i >= 0; i--)
                    if (records[i].CaseId == record.CaseId &&
                        (records[i].Kind == PressKind.Arrest ||
                         records[i].Kind == PressKind.ChargesFiled))
                    {
                        records[i] = record;
                        Version++;
                        return;
                    }
            }
            records.Add(record);
            if (records.Count > PressKept)
                records.RemoveRange(0, records.Count - PressKept);
            Version++;
        }

        public PressRecord FindCase(int caseId, PressKind kind = PressKind.Arrest)
        {
            if (caseId < 0)
                return null;
            for (var i = records.Count - 1; i >= 0; i--)
                if (records[i].CaseId == caseId && records[i].Kind == kind)
                    return records[i];
            return null;
        }

        public void Restore(IEnumerable<PressRecord> saved, int lastEditionDay)
        {
            records.Clear();
            if (saved != null)
                foreach (var row in saved)
                    Add(row?.Copy());
            LastEditionDay = lastEditionDay < 0 ? 0 : lastEditionDay;
            Version++;
        }

        public void Clear()
        {
            records.Clear();
            LastEditionDay = 0;
            Version++;
        }
    }
}
