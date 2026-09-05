using System.Collections.Generic;
using LivingCity.Business;
using LivingCity.Gangs;
using LivingCity.News;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Police;
using LivingCity.Territory;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The one scene-side writer for the city paper. It listens to public street and
    /// law facts, resolves transient scene identities while they still exist, and files
    /// fact records into Underworld.Press. It never reads orders or account books.
    /// </summary>
    public sealed class PressDesk : MonoBehaviour
    {
        sealed class Shooting
        {
            public int Number;
            public Vector3 At;
            public PressRecord Record;
            public readonly HashSet<int> Factions = new HashSet<int>();
        }

        public static PressDesk Instance { get; private set; }

        [Min(1)] public int WitnessThreshold = 3;
        [Min(1)] public int WitnessCap = 12;
        [Min(1f)] public float SightRadius = CivilianAgent.SightRadius;

        readonly List<CivilianAgent> witnesses = new List<CivilianAgent>();
        Shooting shooting;
        BusinessShutdownLedger shutdowns;
        double lastClock = double.NaN;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Instance = null;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                return;
            }
            Instance = this;
        }

        void OnEnable()
        {
            StreetAlarm.OnShot += OnShot;
            StreetAlarm.OnDeath += OnDeath;
            BindBusiness();
        }

        void OnDestroy()
        {
            StreetAlarm.OnShot -= OnShot;
            StreetAlarm.OnDeath -= OnDeath;
            if (shutdowns != null)
                shutdowns.Changed -= OnShutdown;
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            BindBusiness();
            if (shooting != null && StreetAlarm.QuietFor > StreetAlarm.IncidentGap)
                FlushOpenIncident();

            var serial = SerialNow();
            if (double.IsNaN(serial))
                return;
            if (!double.IsNaN(lastClock) &&
                EditionNumber(serial) != EditionNumber(lastClock))
                FlushOpenIncident();
            lastClock = serial;
        }

        void BindBusiness()
        {
            var next = BusinessRuntime.Instance != null
                ? BusinessRuntime.Instance.Shutdowns : null;
            if (ReferenceEquals(next, shutdowns))
                return;
            if (shutdowns != null)
                shutdowns.Changed -= OnShutdown;
            shutdowns = next;
            if (shutdowns != null)
                shutdowns.Changed += OnShutdown;
        }

        void OnShot(StreetAlarm.Shot shot)
        {
            var number = StreetAlarm.IncidentNumber;
            if (shooting == null || shooting.Number != number)
            {
                FlushOpenIncident();
                var now = Now();
                if (now.day <= 0)
                    return;
                shooting = new Shooting
                {
                    Number = number,
                    At = shot.Pos,
                    Record = new PressRecord
                    {
                        Day = now.day,
                        Hour = now.hour,
                        Kind = PressKind.Shootout,
                        Where = Quarter(shot.Pos),
                        IncidentNumber = number,
                    },
                };
            }

            shooting.At = StreetAlarm.Incident;
            shooting.Record.Shots++;
            if (shot.Faction != StreetAlarm.PoliceFaction)
                shooting.Factions.Add(shot.Faction);
            MeasureWitnesses(shot.Pos, shooting.Record);
        }

        void OnDeath(Vector3 at, StreetAlarm.DeathOf who, int victimFaction)
        {
            if (shooting == null || shooting.Number != StreetAlarm.IncidentNumber)
            {
                var now = Now();
                if (now.day <= 0)
                    return;
                shooting = new Shooting
                {
                    Number = StreetAlarm.IncidentNumber,
                    At = at,
                    Record = new PressRecord
                    {
                        Day = now.day,
                        Hour = now.hour,
                        Where = Quarter(at),
                        IncidentNumber = StreetAlarm.IncidentNumber,
                    },
                };
            }

            switch (who)
            {
                case StreetAlarm.DeathOf.Civilian:
                    shooting.Record.CivilianDeaths++;
                    break;
                case StreetAlarm.DeathOf.Officer:
                    shooting.Record.OfficerDeaths++;
                    break;
                default:
                    shooting.Record.GangsterDeaths++;
                    NameVictim(at, victimFaction, shooting.Record);
                    break;
            }
            MeasureWitnesses(at, shooting.Record);
        }

        void MeasureWitnesses(Vector3 at, PressRecord record)
        {
            var found = CivilianAgent.SnapshotWitnesses(
                at, SightRadius, Mathf.Max(WitnessCap, WitnessThreshold), witnesses);
            if (found > record.Witnesses)
                record.Witnesses = found;
        }

        /// <summary>Files the current shooting as it stands. A later round under the
        /// same StreetAlarm number opens a continuation with a new opening time.</summary>
        public void FlushOpenIncident()
        {
            var open = shooting;
            shooting = null;
            if (open == null || open.Record == null)
                return;

            var record = open.Record;
            var factions = new List<int>(open.Factions);
            factions.Sort();
            record.Factions = factions.ToArray();
            if (record.OfficerDeaths > 0)
                record.Kind = PressKind.OfficerKilled;
            else if (record.Toll > 0)
                record.Kind = record.Kind == PressKind.BossKilled
                    ? PressKind.BossKilled : PressKind.Killing;
            else
                record.Kind = PressKind.Shootout;

            record.Attribution = PressPolicy.Attribution(record.Factions.Length,
                record.Witnesses, WitnessThreshold);

            // A body on the pavement is public regardless of how many people saw the
            // shot. Gunfire without a casualty needs the eyewitness gate.
            if (!PressPolicy.StreetIncidentIsPublic(
                    record.Toll, record.Witnesses, WitnessThreshold))
                return;
            File(record);
        }

        void NameVictim(Vector3 at, int victimFaction, PressRecord record)
        {
            if (victimFaction < 0 || record == null)
                return;
            var crews = DemoCrews.Active;
            CrewWalker closest = null;
            var best = 3f * 3f;
            if (crews != null)
                for (var i = 0; i < crews.Units.Count; i++)
                {
                    var unit = crews.Units[i];
                    if (unit == null || unit.Faction != victimFaction) continue;
                    foreach (var man in unit.All())
                    {
                        if (man == null || man.Tf == null || !man.Dead) continue;
                        var d = (man.Tf.position - at).sqrMagnitude;
                        if (d > best) continue;
                        best = d;
                        closest = man;
                    }
                }
            if (closest == null)
                return;

            record.Names = new[] { closest.DisplayName ?? "" };
            record.Models = closest.SourcePrefab != null
                ? new[] { closest.SourcePrefab.name } : System.Array.Empty<string>();
            record.NamedGangId = victimFaction;
            var house = Underworld.Current?.Of(victimFaction);
            var member = house?.Roster?.Find(closest.CharacterId);
            if (member != null && member.Rank == Rank.Boss)
                record.Kind = PressKind.BossKilled;
        }

        void OnShutdown(BusinessShutdownChange change)
        {
            if (!PressPolicy.ShutdownChangeIsPublic(
                    change.Kind == BusinessShutdownChangeKind.Started,
                    change.Kind == BusinessShutdownChangeKind.Extended))
                return;

            var kind = change.Cause == BusinessShutdownCause.Arson
                ? PressKind.Arson
                : change.Cause == BusinessShutdownCause.Bomb
                    ? PressKind.Bombing : PressKind.SmashUp;
            Business(change.BusinessId, out var name, out var where, out _);
            var moment = Moment(change.GameHour);
            File(new PressRecord
            {
                Day = moment.day,
                Hour = moment.hour,
                Kind = kind,
                Where = where,
                Business = name,
                Attribution = PressAttribution.Unknown,
            });
        }

        /// <summary>A violent door job is printable only when the live crowd supplies
        /// the eyewitness threshold.</summary>
        public void BusinessAssault(TerritoryBusinessId id, int faction)
        {
            Business(id, out var name, out var where, out var at);
            var record = New(PressKind.Assault, where);
            if (record == null) return;
            record.Business = name;
            record.Factions = faction >= 0 ? new[] { faction } : System.Array.Empty<int>();
            MeasureWitnesses(at, record);
            if (!PressPolicy.StreetIncidentIsPublic(
                    0, record.Witnesses, WitnessThreshold))
                return;
            record.Attribution = PressPolicy.Attribution(record.Factions.Length,
                record.Witnesses, WitnessThreshold);
            File(record);
        }

        public void PremisesBought(TerritoryBusinessId id, int gangId)
        {
            Business(id, out var name, out var where, out _);
            var record = New(PressKind.PremisesSold, where);
            if (record == null) return;
            record.Business = name;
            record.Factions = gangId >= 0 ? new[] { gangId } : System.Array.Empty<int>();
            record.Attribution = PressAttribution.Named;
            File(record);
        }

        public void PaperKilling(Character victim, int gangId, Vector3 at)
        {
            if (victim == null) return;
            var record = New(victim.Rank == Rank.Boss
                ? PressKind.BossKilled : PressKind.Killing, Quarter(at));
            if (record == null) return;
            record.Names = new[] { victim.FullName ?? "" };
            record.GangsterDeaths = 1;
            record.NamedGangId = gangId;
            var house = Underworld.Current?.Of(gangId);
            record.Models = house?.Roster != null
                ? new[] { GangLooks.LookFor(victim, house.Roster) }
                : System.Array.Empty<string>();
            record.Attribution = PressAttribution.Named;
            File(record);
        }

        public void Arrest(StreetAlarm.Complaint? call, DemoCrews.Unit crew)
        {
            if (crew == null) return;
            var file = crew.ArrestCase;
            var where = call.HasValue ? Quarter(call.Value.Pos) : Quarter(crew.Position);
            var record = New(PressKind.Arrest, where);
            if (record == null) return;
            record.CaseId = file != null ? file.CaseId : -1;
            record.Deed = file != null ? file.Deed : crew.ArrestDeed;
            record.Factions = new[] { crew.Faction };
            record.NamedGangId = crew.Faction;
            record.Attribution = PressAttribution.Named;
            if (call.HasValue)
                record.Business = call.Value.Where ?? "";
            Names(file, crew.Faction, out record.Names, out record.Models);
            if (record.Names.Length == 0)
                Names(crew, out record.Names, out record.Models);
            File(record);
        }

        public void CustodyBroken(StreetAlarm.Complaint? call, DemoCrews.Unit crew)
        {
            if (crew == null) return;
            var record = New(PressKind.CustodyBroken,
                call.HasValue ? Quarter(call.Value.Pos) : Quarter(crew.Position));
            if (record == null) return;
            record.CaseId = crew.ArrestCase != null ? crew.ArrestCase.CaseId : -1;
            record.Deed = crew.ArrestDeed;
            record.Factions = new[] { crew.Faction };
            record.NamedGangId = crew.Faction;
            record.Attribution = PressAttribution.Named;
            Names(crew.ArrestCase, crew.Faction, out record.Names, out record.Models);
            File(record);
        }

        public void FiredOnPolice(StreetAlarm.Complaint? call, DemoCrews.Unit crew)
        {
            if (crew == null) return;
            var record = New(PressKind.FiredOnPolice,
                call.HasValue ? Quarter(call.Value.Pos) : Quarter(crew.Position));
            if (record == null) return;
            record.Factions = new[] { crew.Faction };
            record.NamedGangId = crew.Faction;
            record.Attribution = PressAttribution.Named;
            Names(crew.ArrestCase, crew.Faction, out record.Names, out record.Models);
            if (record.Names.Length == 0)
                Names(crew, out record.Names, out record.Models);
            File(record);
        }

        public void RanFromPolice(StreetAlarm.Complaint? call, DemoCrews.Unit crew)
        {
            if (crew == null) return;
            var record = New(PressKind.FledPolice,
                call.HasValue ? Quarter(call.Value.Pos) : Quarter(crew.Position));
            if (record == null) return;
            record.Factions = new[] { crew.Faction };
            record.NamedGangId = crew.Faction;
            record.Attribution = PressAttribution.Named;
            if (call.HasValue)
                record.Business = call.Value.Where ?? "";
            File(record);
        }

        public void CaseOpened(CourtCase file)
        {
            if (file == null || file.Defendants.Count == 0)
                return;
            var record = New(PressKind.ChargesFiled, file.Where);
            if (record == null) return;
            record.CaseId = file.CaseId;
            record.Deed = file.Deed;
            record.Business = BusinessName(file.BusinessId);
            record.Factions = new[] { file.GangId };
            record.NamedGangId = file.GangId;
            record.Attribution = PressAttribution.Named;
            Names(file, file.GangId, out record.Names, out record.Models);
            File(record);
        }

        public void Statement(StreetAlarm.Complaint call)
        {
            var record = New(PressKind.PoliceBlotter, Quarter(call.Pos));
            if (record == null) return;
            record.Business = call.Where ?? "";
            record.Factions = call.Faction >= 0
                ? new[] { call.Faction } : System.Array.Empty<int>();
            File(record);
        }

        public void BailForfeit(Character man, Prisoner prisoner, CourtCase file)
        {
            if (man == null) return;
            var gang = prisoner != null ? prisoner.GangId : file != null ? file.GangId : -1;
            var record = New(PressKind.BailJumped, file != null ? file.Where : "");
            if (record == null) return;
            record.CaseId = file != null ? file.CaseId : prisoner != null ? prisoner.CaseId : -1;
            record.Names = new[] { man.FullName ?? "" };
            record.NamedGangId = gang;
            record.Factions = gang >= 0 ? new[] { gang } : System.Array.Empty<int>();
            record.Models = Model(man, gang);
            record.Attribution = PressAttribution.Named;
            File(record);
        }

        public void Verdict(Character man, Prisoner prisoner, CourtCase file,
            CaseStatus status)
        {
            if (man == null) return;
            var gang = prisoner != null ? prisoner.GangId : file != null ? file.GangId : -1;
            var record = New(PressKind.Verdict, file != null ? file.Where : "");
            if (record == null) return;
            record.CaseId = file != null ? file.CaseId : prisoner != null ? prisoner.CaseId : -1;
            record.Deed = prisoner != null ? prisoner.Deed : file != null ? file.Deed : Deed.Affray;
            record.Names = new[] { man.FullName ?? "" };
            record.Models = Model(man, gang);
            record.NamedGangId = gang;
            record.Factions = gang >= 0 ? new[] { gang } : System.Array.Empty<int>();
            record.Attribution = PressAttribution.Named;
            var verdict = file?.VerdictFor(man.Id);
            record.Outcome = verdict != null
                ? (int)verdict.Outcome
                : status == CaseStatus.Dismissed
                    ? (int)CaseOutcome.Dismissed
                    : prisoner != null && prisoner.Stage == PrisonStage.Sentenced
                        ? (int)CaseOutcome.Convicted : (int)CaseOutcome.Acquitted;
            record.SentenceDays = verdict != null
                ? verdict.Days : prisoner != null ? prisoner.SentenceDays : 0;
            File(record);
        }

        public void WitnessKilled(Witness witness)
        {
            if (witness == null) return;
            CourtCase found = null;
            var police = FindFirstObjectByType<PoliceForce>();
            var cases = police?.Pipeline?.Cases;
            for (var i = 0; cases != null && i < cases.Count && found == null; i++)
                for (var w = 0; w < cases[i].Witnesses.Count; w++)
                    if (ReferenceEquals(cases[i].Witnesses[w], witness) ||
                        cases[i].Witnesses[w].Name == witness.Name)
                    {
                        found = cases[i];
                        break;
                    }
            var record = New(PressKind.WitnessDead, found != null ? found.Where : "");
            if (record == null) return;
            record.CaseId = found != null ? found.CaseId : -1;
            record.CivilianDeaths = 1;
            record.Names = string.IsNullOrWhiteSpace(witness.Name)
                ? System.Array.Empty<string>() : new[] { witness.Name };
            File(record);
        }

        /// <summary>The police seized something of a family's and said so (EPIC 40).
        /// The connection's own line names it; the paper prints the fact.</summary>
        public void Seizure(int gangId, TerritoryBusinessId front, string line)
        {
            var where = TerritoryRuntime.Instance != null &&
                        TerritoryRuntime.Instance.TryGetBusinessView(front, out var view)
                ? view.BusinessName
                : "";
            var record = New(PressKind.Seizure, where);
            if (record == null) return;
            record.NamedGangId = gangId;
            record.Factions = gangId >= 0 ? new[] { gangId } : System.Array.Empty<int>();
            record.Attribution = PressAttribution.Named;
            record.Business = line ?? "";
            File(record);
        }

        public void FlatRaid(LivingCity.Property.FlatRaid raid, Character keeper, int gangId)
        {
            var record = New(PressKind.FlatRaid, raid.Unit.Door);
            if (record == null) return;
            if (keeper != null)
            {
                record.Names = new[] { keeper.FullName ?? "" };
                record.Models = Model(keeper, gangId);
            }
            record.NamedGangId = gangId;
            record.Factions = gangId >= 0 ? new[] { gangId } : System.Array.Empty<int>();
            record.Attribution = PressAttribution.Named;
            File(record);
        }

        PressRecord New(PressKind kind, string where)
        {
            var now = Now();
            return now.day > 0 ? new PressRecord
            {
                Day = now.day,
                Hour = now.hour,
                Kind = kind,
                Where = where ?? "",
            } : null;
        }

        void File(PressRecord record)
        {
            var book = Underworld.Current?.Press;
            if (record == null || book == null || record.Day <= 0)
                return;
            if (record.Weight <= 0)
                record.Weight = PressRecord.DefaultWeight(record.Kind);
            book.Add(record);
            Debug.Log("[Press] FILED day " + record.Day + " " + Clock(record.Hour) + " " +
                      record.Kind + " at " + (record.Where ?? "") + ", " + record.Toll +
                      " dead, seen by " + record.Witnesses + ", factions " +
                      (record.Factions?.Length ?? 0));
        }

        static void Names(CourtCase file, int gangId, out string[] names,
            out string[] models)
        {
            var named = new List<string>();
            var looks = new List<string>();
            var roster = Underworld.Current?.Of(gangId)?.Roster;
            for (var i = 0; file != null && i < file.Defendants.Count; i++)
            {
                var man = roster?.Find(file.Defendants[i]);
                if (man == null) continue;
                named.Add(man.FullName ?? "");
                looks.Add(GangLooks.LookFor(man, roster));
            }
            names = named.ToArray();
            models = looks.ToArray();
        }

        static void Names(DemoCrews.Unit crew, out string[] names, out string[] models)
        {
            var named = new List<string>();
            var looks = new List<string>();
            if (crew != null)
                foreach (var man in crew.All())
                {
                    if (man == null || string.IsNullOrWhiteSpace(man.DisplayName)) continue;
                    named.Add(man.DisplayName);
                    if (man.SourcePrefab != null) looks.Add(man.SourcePrefab.name);
                }
            names = named.ToArray();
            models = looks.ToArray();
        }

        static string[] Model(Character man, int gangId)
        {
            var roster = Underworld.Current?.Of(gangId)?.Roster;
            return man != null && roster != null
                ? new[] { GangLooks.LookFor(man, roster) }
                : System.Array.Empty<string>();
        }

        static void Business(TerritoryBusinessId id, out string name, out string where,
            out Vector3 at)
        {
            name = "";
            where = "";
            at = default;
            var business = BusinessRuntime.Instance;
            if (business != null && business.Directory.TryGet(id, out var record))
                name = record.DisplayName ?? "";
            if (CityBusinesses.TryApproachPoint(id, out var door))
                at = door;
            var geography = TerritoryRuntime.Instance?.Geography;
            if (geography != null && geography.TryGetBusinessBlock(id, out var blockId) &&
                geography.TryGetBlock(blockId, out var block))
                where = block.NeighborhoodName ?? "";
        }

        static string BusinessName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var business = BusinessRuntime.Instance;
            return business != null && business.Directory.TryGet(
                    new TerritoryBusinessId(id), out var record)
                ? record.DisplayName ?? "" : "";
        }

        static string Quarter(Vector3 at)
        {
            var geography = TerritoryRuntime.Instance?.Geography;
            if (geography == null)
                return "";
            var reach = Mathf.Max(geography.Settings.RoadHysteresis,
                                  geography.Settings.StreetWidth);
            return geography.TryGetBlockNear(new TerritoryPoint(at.x, at.z), reach,
                       out var id) && geography.TryGetBlock(id, out var block)
                ? block.NeighborhoodName ?? "" : "";
        }

        static (int day, float hour) Now()
        {
            var underworld = Underworld.Current;
            var day = underworld?.Player?.Runner?.Campaign?.Day ?? 0;
            var clock = LivingCity.Ambient.DayClock.Current;
            return (day, clock != null ? clock.Hour : 0f);
        }

        static (int day, float hour) Moment(double gameHour)
        {
            if (gameHour < 0d) return Now();
            return ((int)(gameHour / 24d) + 1, (float)(gameHour % 24d));
        }

        static double SerialNow()
        {
            var now = Now();
            return now.day > 0 ? (now.day - 1) * 24d + now.hour : double.NaN;
        }

        static int EditionNumber(double serial) => Mathf.FloorToInt((float)((serial - 6d) / 24d));

        static string Clock(float hour)
        {
            var h = Mathf.FloorToInt(Mathf.Repeat(hour, 24f));
            var m = Mathf.FloorToInt(Mathf.Repeat(hour, 1f) * 60f);
            return h.ToString("00") + ":" + m.ToString("00");
        }
    }
}
