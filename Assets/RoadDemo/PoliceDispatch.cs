using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Police;
using LivingCity.Territory;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A police unit the dispatcher can send: a patrol car (which brings officers)
    /// or a beat officer (who is one). The city's patrols and the crew demo's cruiser
    /// both answer it; the dispatcher does not care which road they drive.
    /// </summary>
    public interface IPoliceUnit
    {
        Transform Tf { get; }
        Vector3 Position { get; }
        /// <summary>Idle - on patrol or resting - and free to be sent.</summary>
        bool Available { get; }
        /// <summary>Sent, and now stopped at the scene.</summary>
        bool OnScene { get; }
        /// <summary>A car with men in it (true) or a man on foot (false).</summary>
        bool Carries { get; }
        /// <summary>Which precinct this unit belongs to - whose roster it comes off and
        /// whose roster its loss lands on (GAN-226). Nought while the city has one
        /// station, which is every scene today.</summary>
        int Precinct { get; }
        /// <summary>Go to the scene: stop about <paramref name="standOff"/> metres short of it.</summary>
        void RouteTo(Vector3 scene, float standOff);
        /// <summary>Done here: back to whatever it was doing.</summary>
        void Release();
    }

    /// <summary>
    /// The law's answer to a shooting, 1987: nobody rings until somebody gets to a
    /// telephone (a frightened civilian indoors) - or a patrol hears it itself - and
    /// then, by how much heat the incident has made (rounds, bodies, a dead officer),
    /// the nearest cars come with the siren going and pull up short of it. Their men
    /// get out, walk up, and if the shooting is still on shout the warning; whoever
    /// keeps shooting - anybody - is shot at; the rest is a scene of crime: tape, a
    /// stare at the chalk, the crowd moved back, and after a while they drive off.
    /// One per scene; the builders register the units and the crews.
    /// </summary>
    public sealed partial class PoliceDispatch : MonoBehaviour
    {
        // heat: what the parked wanted system measured (WantedConfig), read as figures
        const float ShotHeat = 4f, ShotHeatCap = 12f, GangDeathHeat = 30f, CivilianDeathHeat = 45f, OfficerDeathHeat = 100f;
        static readonly float[] Levels = { 10f, 25f, 45f, 70f, 100f };
        const float HeatDecay = 1f;          // a second, once quiet
        const float DecayAfter = 20f;
        const float NobodyRang = 45f;        // seconds until somebody has rung regardless
        const float StandOff = 20f;          // metres short of the scene a car pulls up
        const float WarnRange = 26f;         // metres from the scene an officer shouts from
        // The heat's cars are the law that HAPPENS to be near a fight - a patrol a
        // couple of blocks off; there is no city-wide dispatch pulling a third car across
        // the map to a scene it could never see. The PAIR is the exception (the user's
        // rule, 2026-09-04): the nearest pair comes wherever it is, and past
        // PoliceProcedure.FootResponseCarRange a car comes with it, over the whole city.
        const float ResponseRange = 150f;    // metres a heat car must be within to answer a scene
        // WHAT A MAN OF THE LAW HEARS. A block and the street round it - further than
        // the report itself carries to the crowd (CrewArms Loudness: 45 m for a .38,
        // 80 for a rifle), and deliberately so. Loudness is what a passer-by READS: how
        // near he has to be for a bang to frighten him. An officer a block off does not
        // have to be frightened by it, only to know what it was and which way it came
        // from, and that is the whole of a beat: he turns and he goes. Anybody inside
        // this rings it in himself, at once, and does not wait on a telephone.
        const float Earshot = LivingCity.Police.PoliceProcedure.NearbyPoliceGunfightRange;
        const float SceneSeconds = 90f;      // how long the law stays once it is quiet

        public float Heat { get; private set; }
        public int Level
        {
            get { int l = 0; while (l < Levels.Length && Heat >= Levels[l]) l++; return l; }
        }

        DemoCrews _crews;
        GameObject _sidearm;
        List<GameObject> _officerPrefabs = new List<GameObject>();
        PedClips _clips;
        AnimationClip _sitLoop;

        internal AnimationClip CarriageSitLoop => _sitLoop;

        // A car raises the police response once per shooting incident. Keeping the
        // incident number on every car retained every RoadCar ever hit for the whole
        // session; the authoritative StreetAlarm boundary lets this be one short-lived
        // set instead.
        // IDs preserve per-car deduplication even after a halted carrier leaves the
        // dispatch list, without keeping dead RoadCar/Transform objects alive.
        readonly HashSet<int> _tinRaised = new HashSet<int>();
        int _tinRaisedIncident = -1;
        CourtCase _civilianDeathCase;
        int _civilianDeathIncident = -1;
        readonly List<IPoliceUnit> _units = new List<IPoliceUnit>();
        readonly Dictionary<IPoliceUnit, PoliceLights> _lights = new Dictionary<IPoliceUnit, PoliceLights>();
        readonly List<Squad> _squads = new List<Squad>();
        readonly Dictionary<IPoliceUnit, float> _footOnSceneAt = new Dictionary<IPoliceUnit, float>();

        int _incident = -1;          // the incident number the bookkeeping below is for
        float _shotHeat;             // heat this incident has made from rounds alone
        float _callAt = float.MaxValue;
        // the witness at the telephone: which hiding this delay was rolled for, and
        // the delay - rolled once per witness, not once per frame (re-rolled every
        // Update under a Min it collapsed to the bottom of its range in a second)
        float _witnessAt = float.MinValue;
        float _witnessDelay;
        bool _called;
        bool _playerIncident;
        int _carsSent;
        float _lastSentAt = -1000f;
        bool _officerDied;
        bool _escortWanted;          // the pair was far, or there was none: a car must go too
        bool _escortSent;            // and it has
        // The pairs sent to this incident. Whether it is still owed one is read off
        // them (FootOwed), not off a flag set on the order: that outlived a pair that
        // was wiped, and was cleared by a stall on somebody else's scene.
        readonly List<IPoliceUnit> _footTried = new List<IPoliceUnit>();
        bool _footAnswered;          // one of them was seen stood at this scene
        int _rank;
        readonly List<CrewWalker> _shooters = new List<CrewWalker>();

        /// <summary>The scene builder wires the arena and the bodies of the law: any
        /// officer prefabs (the police station pack's) and their sidearm.</summary>
        public void Init(DemoCrews crews, PedClips clips, IList<GameObject> officerPrefabs,
            GameObject sidearm, AnimationClip sitLoop = null)
        {
            _crews = crews;
            _clips = clips;
            // Enforce the uniform at the authority that actually deals every patrol,
            // response squad and car occupant. Scene builders may hand us a broader
            // pack list, but novelty/variant bodies never enter the force from here.
            _officerPrefabs = new List<GameObject>();
            if (officerPrefabs != null)
                for (var i = 0; i < officerPrefabs.Count; i++)
                {
                    var body = officerPrefabs[i];
                    if (body != null && string.Equals(body.name,
                            LivingCity.Police.PoliceProcedure.UniformOfficerPrefabName,
                            System.StringComparison.Ordinal))
                        _officerPrefabs.Add(body);
                }
            _sidearm = sidearm;
            _sitLoop = sitLoop != null ? sitLoop : clips.SitLoop;
            StreetAlarm.OnShot += OnShot;
            StreetAlarm.OnDeath += OnDeath;
            StreetAlarm.OnComplaint += OnComplaint;
        }

        void OnDestroy()
        {
            if (_arrestCrew != null)
                _arrestCrew.ArrestChallenged = false;
            StreetAlarm.OnShot -= OnShot;
            StreetAlarm.OnDeath -= OnDeath;
            StreetAlarm.OnComplaint -= OnComplaint;
        }

        /// <summary>A unit the dispatcher may send. Cars get lights and a siren.</summary>
        public void Register(IPoliceUnit unit)
        {
            if (unit == null || _units.Contains(unit)) return;
            _units.Add(unit);
            if (unit is PolicePatrolCar patrol) patrol.BeforeRemoval = PrepareCarRemoval;
            if (unit.Carries && unit.Tf != null) _lights[unit] = new PoliceLights(unit.Tf);
        }

        /// <summary>Remove a lost permanent unit before its replacement is dealt.</summary>
        public void Unregister(IPoliceUnit unit)
        {
            if (unit == null) return;
            if (_lights.TryGetValue(unit, out var lights))
                lights.Set(lights: false, siren: false);
            _units.Remove(unit);
            _lights.Remove(unit);
            _footOnSceneAt.Remove(unit);
        }

        /// <summary>The beat pair at an officer-death position, if that death belonged
        /// to a permanent foot unit. The death channel intentionally carries only kind
        /// and place, so the force resolves ownership against the same registered-unit
        /// book it already uses for precinct attribution.</summary>
        public PoliceBeat BeatNear(Vector3 where, float metres)
        {
            PoliceBeat best = null;
            var bestD = metres * metres;
            for (var i = 0; i < _units.Count; i++)
            {
                if (!(_units[i] is PoliceBeat beat) || beat.Unit == null || beat.Tf == null)
                    continue;
                var d = (beat.Position - where).sqrMagnitude;
                if (d > bestD) continue;
                bestD = d;
                best = beat;
            }
            return best;
        }

        /// <summary>Deals the shared two-man body for a beat and puts its brain on the
        /// same dispatcher list as every car and response squad.</summary>
        public PoliceBeat MakeBeat(PedLink start, float startT, List<PedNode> nodes,
            List<PedNode> ring, int unitNumber, Vector3? stationDoor,
            Vector2 restRange, float firstRest)
        {
            if (start == null || _crews == null) return null;
            var at = Vector3.Lerp(start.From.Pos, start.To.Pos,
                Mathf.Clamp01(startT / Mathf.Max(0.01f, start.Length)));
            var facing = start.To.Pos - start.From.Pos;
            var unit = SpawnSquad(at, facing, 2, aboardOf: null, start, startT);
            if (unit == null) return null;
            var beat = new PoliceBeat(_crews, unit, unitNumber, nodes, ring,
                stationDoor, restRange, firstRest);
            beat.Provoked = BeatProvoked;
            Register(beat);
            return beat;
        }

        void BeatProvoked(PoliceBeat beat, DemoCrews.Unit attacker)
        {
            if (beat == null || attacker == null) return;
            var defensive = LivingCity.Police.PoliceProcedure.IsDefensivePoliceReturn(
                attacker.PoliceAttackedIncident, StreetAlarm.IncidentNumber);
            if (LivingCity.Police.PoliceProcedure.ShotAtPoliceStartsSwarm(
                    targetIsPolice: true, defensiveReturn: defensive))
                RaiseSwarm(beat.Position, SwarmGrade.ShotsFired, attacker);
        }

        /// <summary>A transfer and its arrest-side predecessor both need real officers,
        /// dealt by the dispatcher's one police-body factory.</summary>
        internal DemoCrews.Unit SpawnCarriageEscort(Vector3 at, Vector3 facing) =>
            SpawnSquad(at, facing, 2, aboardOf: null);

        internal void RetireCarriageEscort(DemoCrews.Unit escort)
        {
            if (escort != null && !escort.Wiped)
                _crews?.RemoveUnit(escort);
        }

        /// <summary>The ordinary OnShot gate cannot see a car mark. This closes that
        /// hole and de-duplicates every round after the first in one street incident.</summary>
        internal bool ShotAtPoliceCar(RoadCar car, CrewWalker shooter)
        {
            if (car == null || shooter == null || _crews == null) return false;
            var incident = StreetAlarm.IncidentNumber;
            BeginTinIncident(incident);
            if (!_tinRaised.Add(car.Id))
                return false;
            var culprit = _crews.UnitOf(shooter);
            RaiseSwarm(car.Position, SwarmGrade.ShotsFired, culprit);
            return true;
        }

        void BeginTinIncident(int incident)
        {
            if (_tinRaisedIncident == incident) return;
            _tinRaisedIncident = incident;
            _tinRaised.Clear();
        }

        /// <summary>The crew demo's cruiser: a CrewCar with its two officers already in
        /// it, dealt into the arena as a unit of the law. Registered here.</summary>
        public PoliceCruiser AddCruiser(CrewCar car, Vector3 home)
        {
            if (car == null || _crews == null) return null;
            car.Civic = true;
            car.DisplayName = "Police";
            var unit = SpawnSquad(car.Position, car.Tf.forward, 2, aboardOf: car);
            var cruiser = new PoliceCruiser(car, unit, home, _crews);
            Register(cruiser);
            return cruiser;
        }

        // ------------------------------------------------------------ the call

        void OnShot(StreetAlarm.Shot shot)
        {
            var shooterUnit = shot.Shooter != null && _crews != null
                ? _crews.UnitOf(shot.Shooter) : null;
            var involvesPlayer = shot.Faction == LivingCity.Gangs.GangCatalog.PlayerGangId ||
                (shooterUnit != null && shooterUnit.TargetUnit != null &&
                 shooterUnit.TargetUnit.Faction == LivingCity.Gangs.GangCatalog.PlayerGangId);

            BeginTinIncident(StreetAlarm.IncidentNumber);
            if (StreetAlarm.IncidentNumber != _incident)
            {
                // a new incident: the clock to the call starts - somebody will have rung
                // within the minute regardless, sooner if a witness gets to a phone
                _incident = StreetAlarm.IncidentNumber;
                _shotHeat = 0f;
                _called = false;
                _playerIncident = involvesPlayer;
                _carsSent = 0;
                _footTried.Clear();
                _footAnswered = false;
                _escortWanted = false;
                _escortSent = false;
                _officerDied = false;
                _callAt = Time.time + NobodyRang;
                // WHO SAW IT IS DECIDED NOW (GAN-245), not when an officer eventually
                // gets round to an arrest a hundred seconds later. Evidence is about the
                // moment of the act: the people who were on this pavement when the first
                // round went off are the witnesses, and the crowd that gathers
                // afterwards - or has gone home by then - is not.
                SnapshotTheScene(shot.Pos);
                if (_playerIncident)
                    CrewOverlay.Announce("SHOTS FIRED", 4f,
                        new Color(1f, 0.55f, 0.45f));
            }
            else if (involvesPlayer && !_playerIncident)
            {
                // A player can enter a fight that began between rivals. It becomes his
                // news at that moment, without replaying any earlier AI-only traffic.
                _playerIncident = true;
                CrewOverlay.Announce("SHOTS FIRED", 4f,
                    new Color(1f, 0.55f, 0.45f));
            }
            float add = Mathf.Min(ShotHeat, ShotHeatCap - _shotHeat);
            if (add > 0f) { _shotHeat += add; Heat = Mathf.Min(120f, Heat + add); }

            // The first round AT any officer is the escalation, not only a hit or a
            // death. The shooter's crew already carries its ordered target.
            if (shooterUnit != null && shooterUnit.TargetUnit != null &&
                shooterUnit.TargetUnit.IsPolice)
            {
                // Police do not open a fight, but a shot directed at their unit is the
                // line: the whole pair/squad answers through the shared combat model.
                if (!shooterUnit.TargetUnit.Wiped)
                    _crews.Sic(shooterUnit.TargetUnit, shooterUnit);
                var defensive = LivingCity.Police.PoliceProcedure.IsDefensivePoliceReturn(
                    shooterUnit.PoliceAttackedIncident, StreetAlarm.IncidentNumber);
                if (LivingCity.Police.PoliceProcedure.ShotAtPoliceStartsSwarm(
                        targetIsPolice: true, defensiveReturn: defensive))
                    RaiseSwarm(shot.Pos, SwarmGrade.ShotsFired, shooterUnit);
            }

            // a patrol in earshot rings it in itself, at once - no telephone, no wait
            float heard = Mathf.Max(shot.Loudness, Earshot);
            foreach (var u in _units)
                if (u.Tf != null && (u.Position - shot.Pos).sqrMagnitude < heard * heard)
                { _callAt = Mathf.Min(_callAt, Time.time); break; }

            // Once the call is live, every FREE patrol that comes within earshot of any
            // later round joins it too, on foot or in a car. The first dispatch used to
            // be the only scan, so an officer could pass a fight already involving law.
            if (_called && SendNearbyPolice(shot.Pos) && _playerIncident)
                CrewOverlay.Announce("POLICE BACKUP RESPONDING", 4f,
                    new Color(0.55f, 0.78f, 1f));
        }

        void OnDeath(Vector3 where, StreetAlarm.DeathOf who, int victimFaction)
        {
            if (victimFaction == LivingCity.Gangs.GangCatalog.PlayerGangId)
                _playerIncident = true;
            float add = who switch
            {
                StreetAlarm.DeathOf.Civilian => CivilianDeathHeat,
                StreetAlarm.DeathOf.Officer => OfficerDeathHeat,
                _ => GangDeathHeat,
            };
            Heat = Mathf.Min(120f, Heat + add);
            if (who == StreetAlarm.DeathOf.Civilian)
                OpenCivilianDeathCase(where);
            if (who != StreetAlarm.DeathOf.Officer) return;
            _officerDied = true;
            // and the radio call that is not an escalation but a different kind of day
            // (GAN-220): every car in the city, and a hunt that outlives the shooting
            if (!StreetAlarm.LastOfficerDeathWasDefensiveReturn)
                RaiseSwarm(where, SwarmGrade.OfficerDown,
                    StreetAlarm.LastDeathAttacker != null && _crews != null
                        ? _crews.UnitOf(StreetAlarm.LastDeathAttacker) : null);
            // and the precinct is a man short until the department fills the hole
            // (GAN-226). Through here rather than through a second listener: StreetAlarm
            // is the one channel for a death, and this is already listening to it.
            if (Force != null) Force.OfficerDown(where);
        }

        /// <summary>A body is a docket even when nobody is collared. It never becomes
        /// a CallOut: the file names only the house the recent gunfire can attribute.</summary>
        void OpenCivilianDeathCase(Vector3 where)
        {
            var pipeline = Force != null ? Force.Pipeline : null;
            var runtime = TerritoryRuntime.Instance;
            if (pipeline == null || runtime == null)
                return;
            var faction = runtime.RecentViolenceAt(where);
            if (!faction.IsValid)
                return;

            var businessId = "";
            var name = "";
            if (runtime.TryGetBusinessNear(where, 4f, out var atDoor))
            {
                businessId = atDoor.Value;
                if (runtime.TryGetBusinessView(atDoor, out var view))
                    name = view.BusinessName;
            }
            var today = Today();
            var file = OpenCivilianDeathCase(
                pipeline, faction, today, businessId, name);
            if (file == null)
                return;
            CopySceneWitnesses(file, StreetAlarm.IncidentNumber);
            _civilianDeathCase = file;
            _civilianDeathIncident = StreetAlarm.IncidentNumber;
            LawWire.CaseOpened(file);
        }

        /// <summary>The engine-free decision under the death listener: only a uniquely
        /// attributed house earns a murder file, and the file itself names no innocent
        /// defendant merely because another crew is later found nearby.</summary>
        public static CourtCase OpenCivilianDeathCase(
            PrisonPipeline pipeline, TerritoryGangId faction, int today,
            string businessId = "", string where = "")
        {
            if (pipeline == null || !faction.IsValid)
                return null;
            var file = pipeline.OpenCase(
                Deed.Murder, faction.Value, today,
                today > 0 ? today + Sentencing.DaysToCourt : 0,
                businessId, where);
            file.BodyEvidence = true;
            return file;
        }

        /// <summary>The institution behind the units - who is on the roster, who is on
        /// the watch, and when a hole is filled. Null in a scene that has no station.</summary>
        public PoliceForce Force;

        /// <summary>
        /// WHOSE MAN THAT WAS. A death reaches the force as a place and a kind - StreetAlarm
        /// is the one channel and stays one - but the man who went down was standing with
        /// his own unit, and every unit knows its precinct. So the unit nearest the body
        /// says whose books he was on, which is the answer a house-nearest guess gets wrong
        /// the moment a car ranges across town into another precinct's ground (GAN-236).
        ///
        /// -1 when nothing of the law is near enough to have been him, and the force falls
        /// back on the nearest station house.
        /// </summary>
        public int PrecinctNear(Vector3 where, float metres)
        {
            var best = -1;
            var bestD = metres * metres;
            foreach (var u in _units)
            {
                if (u == null || u.Tf == null) continue;
                var d = (u.Position - where).sqrMagnitude;
                if (d > bestD) continue;
                var precinct = u.Precinct;
                if (precinct < 0) continue;
                bestD = d;
                best = precinct;
            }
            return best;
        }

        static readonly Unity.Profiling.ProfilerMarker updateMarker = new Unity.Profiling.ProfilerMarker("PoliceDispatch.Update");

        void Update()
        {
            using var profile = updateMarker.Auto();
            float dt = Time.deltaTime;
            if (StreetAlarm.QuietFor > DecayAfter && Heat > 0f)
                Heat = Mathf.Max(0f, Heat - HeatDecay * dt);

            // a witness at a telephone brings the call forward
            if (StreetAlarm.IncidentOpen && CivilianAgent.LastHidAt > StreetAlarm.IncidentStart)
            {
                if (CivilianAgent.LastHidAt != _witnessAt)
                {
                    _witnessAt = CivilianAgent.LastHidAt;
                    _witnessDelay = Random.Range(2f, 5f);
                }
                _callAt = Mathf.Min(_callAt, _witnessAt + _witnessDelay);
            }

            if (StreetAlarm.IncidentOpen && Time.time >= _callAt)
            {
                if (!_called)
                {
                    _called = true;
                    Send(first: true);
                }
                else if (StreetAlarm.QuietFor < 12f && Time.time - _lastSentAt > (_officerDied ? 25f : 40f))
                    Send(first: false); // still going: more
            }

            // A patrol car can cross the 110 m line BETWEEN two rounds. Keep watching
            // the current shooting ground while the gunfight is live; proximity itself,
            // not only the first dispatch or a shot callback, makes it turn in.
            if (_called && StreetAlarm.IncidentOpen && StreetAlarm.QuietFor < 12f &&
                SendNearbyPolice(StreetAlarm.LastShotPos) && _playerIncident)
                CrewOverlay.Announce("POLICE BACKUP RESPONDING", 4f,
                    new Color(0.55f, 0.78f, 1f));

            foreach (var unit in _units)
                if (unit is PoliceCruiser cruiser) cruiser.TickParkingRetry();
            for (int i = _squads.Count - 1; i >= 0; i--) TickSquad(_squads[i], dt); // Done() removes
            TickFoot();
            TickPending();
            TickSwarm(dt);
            TickWanted(dt);
            TickCustody(dt);
            TickCalls(dt);
            WitnessWatch.Tick();
            TickArrest(dt);
            foreach (var kv in _lights) kv.Value.Tick(dt);
        }

        // Dispatch calls one marked car at most. Cars that physically enter the 110 m
        // fight radius volunteer separately; the officer-down swarm also has its own cap.
        int CarsWanted()
        {
            return LivingCity.Police.PoliceProcedure.OrdinaryDispatchedCars(
                gunfightActive: StreetAlarm.QuietFor < 12f,
                heatLevel: Level,
                anyFootFree: AnyFootAvailable());
        }

        bool AnyFootAvailable()
        {
            foreach (var u in _units) if (!u.Carries && u.Available) return true;
            return false;
        }

        void Send(bool first)
        {
            var scene = StreetAlarm.Incident;
            int wanted = CarsWanted();
            // Units already within earshot volunteer first. A nearby car satisfies the
            // ordinary one-car call; cars that wander into earshot later still volunteer
            // because that is presence at the fight, not another dispatch escalation.
            bool any = SendNearbyPolice(scene);
            any |= SendFoot(scene) | SendEscort(scene);
            // and the heat's cars, which stay station-local (GAN-220)
            while (_carsSent < wanted)
            {
                var car = Nearest(scene, carries: true);
                if (car == null) break;
                SendCar(car, scene);
                any = true;
            }
            _lastSentAt = Time.time;
            if (any && _playerIncident)
                CrewOverlay.Announce("POLICE RESPONDING", 5f,
                    new Color(0.55f, 0.78f, 1f));
        }

        /// <summary>THE NEAREST PAIR COMES, WHEREVER IT IS (the user's rule, 2026-09-04):
        /// on the call, or the moment a pair is free if none was, or again if the pair
        /// sent could not get there (TickPending). Measured before anybody is sent: a
        /// pair that has just been routed is no longer free and would not be found
        /// afterwards. A pair already tried for this incident is not tried twice.</summary>
        bool SendFoot(Vector3 scene)
        {
            var owed = FootOwed();
            IPoliceUnit foot = null;
            var footD = float.MaxValue;
            if (owed)
                foot = Nearest(scene, carries: false, anyDistance: true,
                    out footD, _footTried);

            if (!owed) return false;

            var any = false;
            if (foot != null && foot.Available)
            {
                // The nearest pair comes even when it began outside earshot.
                RouteNearbyIntoResponse(foot, scene);
                any = true;
            }
            // Past 150 m a car goes out beside him, and a city with nobody free on
            // foot sends the car alone - the nearest car there is, not the nearest
            // inside the heat rule's reach. Whoever arrives first makes the arrest.
            if (LivingCity.Police.PoliceProcedure.CarJoinsFootResponse(foot != null, footD))
                _escortWanted = true;
            return any;
        }

        bool SendNearbyPolice(Vector3 scene)
        {
            var any = false;
            foreach (var unit in _units)
            {
                if (unit == null || unit.Tf == null || ResponseOwns(unit)) continue;

                // An idle pair already holding this shooting scene may re-enter when
                // another round goes off. A collar, statement or custody keeps priority;
                // everybody merely walking/resting is free for the emergency.
                var free = unit.Available;
                if (!free && unit is PoliceBeat beat && unit.OnScene &&
                    beat.Unit != null && !beat.Unit.Wiped &&
                    beat.Unit.TargetUnit == null && !beat.Unit.Surrendered &&
                    !FootHeldByLawWork(unit))
                    free = true;

                var distance = LivingCity.Police.PoliceProcedure.AirDistanceSquared(
                    unit.Position.x, unit.Position.z, scene.x, scene.z);
                if (!LivingCity.Police.PoliceProcedure.NearbyPoliceJoinsGunfight(
                        free, distance))
                    continue;

                RouteNearbyIntoResponse(unit, scene);
                any = true;
            }
            return any;
        }

        void RouteNearbyIntoResponse(IPoliceUnit unit, Vector3 scene)
        {
            if (unit == null || ResponseOwns(unit)) return;
            if (unit.Carries)
            {
                // This is a car already driving/resting inside the fight's own audible
                // radius. It deploys through the normal car squad path, regardless of
                // whether dispatch has already sent its one outside response car.
                SendCar(unit, scene);
                return;
            }

            unit.RouteTo(scene, 6f);
            if (!_footTried.Contains(unit)) _footTried.Add(unit);

            // A permanent beat already HAS its two officers. It must enter the same
            // Warning -> Engaging state machine as a car squad, but must neither spawn
            // duplicate bodies on arrival nor be removed from DemoCrews afterwards.
            if (unit is PoliceBeat beat && beat.Unit != null && !beat.Unit.Wiped)
                _squads.Add(new Squad
                {
                    Ride = unit,
                    Men = beat.Unit,
                    Scene = scene,
                    State = SquadState.Sent,
                    Incident = _incident,
                    PlayerNews = _playerIncident,
                });
        }

        bool ResponseOwns(IPoliceUnit unit)
        {
            for (var i = 0; i < _squads.Count; i++)
                if (_squads[i].Ride == unit && _squads[i].State != SquadState.Done)
                    return true;
            return false;
        }

        /// <summary>The car a far pair needs, looked for until one is free: decided once,
        /// with the pair, but not given up on because every car in the city happened to
        /// be out at that moment.</summary>
        bool SendEscort(Vector3 scene)
        {
            if (!_escortWanted || _escortSent) return false;
            // A replacement pair chosen after the first one stalled may itself be far
            // away. That must not turn the escort path into a second ordinary response
            // car after this incident has already had its one.
            if (!LivingCity.Police.PoliceProcedure.OrdinaryDispatchCarStillAllowed(
                    _carsSent))
            {
                _escortSent = true;
                return false;
            }
            var escort = Nearest(scene, carries: true, anyDistance: true, out _);
            if (escort == null) return false;
            SendCar(escort, scene);
            _escortSent = true;
            return true;
        }

        /// <summary>Whether this incident is still owed a pair: none of those sent has
        /// been seen stood at THIS scene (answered - once, for the incident, so a pair
        /// released after the scene hold or sent on elsewhere has still answered), and
        /// none is on its way to it. Read off where the pair is and what it is doing,
        /// not off its arrival time, which belongs to the pair and not to this incident:
        /// a pair that reached a shop door after the shooting began has not answered
        /// the shooting. A wiped pair is neither; a stalled one is neither; a pair sent
        /// on somewhere else by a telephone call is on its way to a different scene.</summary>
        bool FootOwed()
        {
            if (_footAnswered) return false;
            var here = StreetAlarm.Incident;
            for (var i = 0; i < _footTried.Count; i++)
            {
                if (!(_footTried[i] is PoliceBeat beat) || beat.Unit == null || beat.Unit.Wiped ||
                    !AtThisScene(beat.Scene, here))
                    continue;
                switch (beat.State)
                {
                    case PoliceBeat.Mode.OnScene:
                    case PoliceBeat.Mode.Arresting:
                    case PoliceBeat.Mode.Doorway:
                        _footAnswered = true;
                        return false;
                    case PoliceBeat.Mode.Responding:
                        if (!beat.StalledOnTheWay) return false;
                        break;
                }
            }
            return true;
        }

        /// <summary>What the call still owes the scene once the shooting has stopped and
        /// Send no longer runs: the pair, if none was free or the one sent could not get
        /// there, and the far pair's car - for as long as an arrest could still be made
        /// over it (ArrestWindow). Not before the call itself has been made.</summary>
        void TickPending()
        {
            if (!_called || StreetAlarm.QuietFor > ArrestWindow) return;
            if (!FootOwed() && (!_escortWanted || _escortSent)) return;
            var scene = StreetAlarm.Incident;
            if (SendFoot(scene) | SendEscort(scene))
                if (_playerIncident)
                    CrewOverlay.Announce("POLICE RESPONDING", 5f,
                        new Color(0.55f, 0.78f, 1f));
        }

        void SendCar(IPoliceUnit car, Vector3 scene)
        {
            car.RouteTo(scene, StandOff);
            _squads.Add(new Squad
            {
                Ride = car,
                Men = MenOf(car),
                Scene = scene,
                State = SquadState.Sent,
                Incident = _incident,
                PlayerNews = _playerIncident,
            });
            if (_lights.TryGetValue(car, out var lights)) lights.Set(true, siren: true);
            _carsSent++;
        }

        /// <summary>Within <see cref="ResponseRange"/> unless <paramref name="anyDistance"/>:
        /// the heat's cars are station-LOCAL by a deliberate rule (GAN-220), while the
        /// pair, the car that goes out beside a far pair, and the swarm after a dead
        /// officer are found over the whole city.</summary>
        IPoliceUnit Nearest(Vector3 to, bool carries, bool anyDistance = false) =>
            Nearest(to, carries, anyDistance, out _);

        IPoliceUnit Nearest(Vector3 to, bool carries, bool anyDistance, out float distanceSquared,
            List<IPoliceUnit> except = null)
        {
            IPoliceUnit best = null;
            float bestD = anyDistance
                ? float.MaxValue
                : ResponseRange * ResponseRange;   // out of this reach, nobody answers
            foreach (var u in _units)
            {
                if (u.Carries != carries || !u.Available || u.Tf == null) continue;
                if (except != null && except.Contains(u)) continue;
                var at = u.Position;
                float d = LivingCity.Police.PoliceProcedure.AirDistanceSquared(
                    at.x, at.z, to.x, to.z);
                if (d < bestD) { bestD = d; best = u; }
            }
            distanceSquared = best != null ? bestD : float.MaxValue;
            return best;
        }

        static DemoCrews.Unit MenOf(IPoliceUnit unit) => unit switch
        {
            PoliceCruiser cruiser => cruiser.Men,
            PoliceBeat beat => beat.Unit,
            _ => null,
        };

        // ------------------------------------------------------------ the squads

        enum SquadState { Sent, Deploying, Responding, Warning, Engaging, Securing, Leaving, Done }

        sealed class Squad
        {
            public IPoliceUnit Ride;
            public DemoCrews.Unit Men;
            public Vector3 Scene;
            public SquadState State;
            public float Timer;
            public bool Ordered;
            public bool Taped;
            public float MoveAlongIn;
            public float Reassess;
            public float ArrivedAt;
            public float SecuringAt;     // when its men were stood at the scene, for who was first
            public float RouteRetryAt;
            public int Incident;
            public bool PlayerNews;
            public bool SwarmResponse;
        }

        bool IsPlayerNews(Squad squad) => squad != null &&
            (squad.PlayerNews || squad.Incident == _incident && _playerIncident);

        void TickSquad(Squad squad, float dt)
        {
            var ride = squad.Ride;
            switch (squad.State)
            {
                case SquadState.Sent:
                    if (ride.Tf == null) { Done(squad); return; }
                    // TickFoot leaves owned responses to this state machine. A pair
                    // that cannot reach the scene must therefore be released here;
                    // waiting only on OnScene otherwise owns it forever.
                    if (ride is PoliceBeat stalled && stalled.StalledOnTheWay &&
                        !FootHeldByLawWork(ride))
                    {
                        Done(squad);
                        return;
                    }
                    if (!ride.OnScene) return;
                    squad.ArrivedAt = Time.time;
                    if (_lights.TryGetValue(ride, out var lights)) lights.Set(true, siren: false);
                    if (IsPlayerNews(squad))
                        CrewOverlay.Announce("POLICE ON THE SCENE", 4f,
                            new Color(0.55f, 0.78f, 1f));
                    if (ride is PoliceBeat beat)
                    {
                        // The pair walked here in its existing DemoCrews.Unit. Do not
                        // deal a second pair beside it as the patrol-car branch does.
                        squad.Men = beat.Unit;
                        squad.State = SquadState.Responding;
                    }
                    else if (ride is PoliceCruiser cruiser)
                    {
                        // the men climb out through their doors
                        if (squad.Men != null && !squad.Men.Wiped) _crews.LeaveCar(squad.Men);
                        squad.State = SquadState.Deploying;
                    }
                    else
                    {
                        // the city's patrol car: two officers get out beside it
                        var t = ride.Tf;
                        var toScene = squad.Scene - ride.Position;
                        toScene.y = 0f;
                        float side = Vector3.Dot(toScene, t.right) >= 0f ? 1f : -1f;
                        squad.Men = SpawnSquad(ride.Position + t.right * side * 2.4f, toScene.normalized, 2, aboardOf: null);
                        squad.State = SquadState.Responding;
                    }
                    return;

                case SquadState.Deploying:
                    if (squad.Men == null || squad.Men.Wiped) { squad.State = SquadState.Leaving; return; }
                    if (squad.Men.Car == null && !squad.Men.Leaving) squad.State = SquadState.Responding;
                    return;

                case SquadState.Responding:
                {
                    if (Wiped(squad)) return;
                    var boss = Lead(squad);
                    if (boss == null || boss.Tf == null) return;

                    var from = ride.Position;
                    var to = squad.Scene;
                    var dir = to - from;
                    dir.y = 0f;
                    float d = dir.magnitude;
                    dir = d > 0.1f ? dir / d : Vector3.forward;
                    // The car stops short so it does not drive through the incident.
                    // Its officers close the remaining ground on foot, to a useful
                    // pistol distance, over the same shared WalkRoute as a player crew.
                    var stand = to - dir * Mathf.Min(8f, d * 0.5f);
                    if (!WalkObstacles.TryClearStandingSpot(
                            stand, WalkObstacles.Radius, boss.Tf.position,
                            out stand, 12f))
                        return;

                    var toPost = boss.Tf.position - stand;
                    toPost.y = 0f;
                    if (toPost.sqrMagnitude > 3.5f * 3.5f &&
                        (!squad.Ordered || !boss.HasOrder) &&
                        Time.time >= squad.RouteRetryAt)
                    {
                        squad.RouteRetryAt = Time.time + 1.25f;
                        squad.Ordered = _crews.MarchTo(squad.Men, stand,
                            run: LivingCity.Police.PoliceProcedure.RunToScene,
                            keepOffRoad: false, allowCustody: true);
                    }
                    // A stopped route is not an arrival. It is retried above until the
                    // body itself reaches the post; only then may the warning/scene beat
                    // begin.
                    toPost = boss.Tf.position - stand;
                    toPost.y = 0f;
                    if (toPost.sqrMagnitude > 3.5f * 3.5f) return;
                    // THE HUNTED ARE NOT WARNED AGAIN (GAN-220). They were warned when
                    // the first squad arrived, and a squad shouting DROP THE GUNS at the
                    // man who has just shot a policeman is the city being polite about
                    // the one thing it is not polite about. Everybody ELSE on the street
                    // still gets the warning first, which is the rule this does not touch.
                    if (_swarm && PickFight(squad)) return;
                    if (StreetAlarm.QuietFor < 4f) BeginWarning(squad);
                    else BeginSecuring(squad);
                    return;
                }

                case SquadState.Warning:
                    if (Wiped(squad)) return;
                    squad.Timer -= dt;
                    if (squad.Timer > 0f) return;
                    if (!PickFight(squad)) BeginSecuring(squad);
                    return;

                case SquadState.Engaging:
                {
                    if (Wiped(squad)) return;
                    squad.Reassess -= dt;
                    if (squad.Reassess > 0f) return;
                    squad.Reassess = 0.5f;
                    var men = squad.Men;
                    bool fightOver = men.TargetUnit == null || men.TargetUnit.Wiped || men.TargetUnit.Retreated;
                    if (fightOver && !PickFight(squad) && StreetAlarm.QuietFor > 6f)
                    {
                        men.TargetUnit = null;
                        foreach (var man in men.All()) man.Disengage();
                        BeginSecuring(squad);
                    }
                    else if (fightOver) PickFight(squad);
                    return;
                }

                case SquadState.Securing:
                {
                    if (Wiped(squad)) return;
                    // shooting again within earshot of the scene: back to the warning
                    if (StreetAlarm.QuietFor < 1f && (StreetAlarm.LastShotPos - squad.Scene).sqrMagnitude < 60f * 60f &&
                        !StreetAlarm.FactionFiredSince(StreetAlarm.PoliceFaction, 1f))
                    {
                        BeginWarning(squad);
                        return;
                    }
                    if (!squad.Taped) { squad.Taped = true; TapeOff(squad); }
                    squad.MoveAlongIn -= dt;
                    if (squad.MoveAlongIn <= 0f)
                    {
                        squad.MoveAlongIn = 5f;
                        CivilianAgent.MoveAlong(squad.Scene, 14f);
                    }
                    squad.Timer -= dt;
                    if (squad.Timer <= 0f && StreetAlarm.QuietFor > 20f) BeginLeaving(squad);
                    return;
                }

                case SquadState.Leaving:
                {
                    if (ride is PoliceBeat)
                    {
                        Done(squad);
                        return;
                    }
                    if (ride is PoliceCruiser home)
                    {
                        if (squad.Men != null && !squad.Men.Wiped)
                        {
                            if (!squad.Ordered) { squad.Ordered = true; _crews.BoardCar(squad.Men, home.Car); }
                            if (squad.Men.Car != home.Car) return; // still climbing in
                        }
                        Done(squad);
                        return;
                    }
                    if (squad.Men != null && !squad.Men.Wiped)
                    {
                        if (!squad.Ordered)
                        {
                            squad.Ordered = true;
                            int k = 0;
                            foreach (var man in squad.Men.All())
                                if (!man.Dead) man.OrderToPoint(ride.Position + (ride.Tf != null ? ride.Tf.right : Vector3.right) * (k++ % 2 == 0 ? -2.2f : 2.2f), k * 0.3f);
                        }
                        foreach (var man in squad.Men.All())
                            if (!man.Dead && (man.HasOrder || (man.Tf.position - ride.Position).sqrMagnitude > 4f * 4f)) return;
                        _crews.RemoveUnit(squad.Men);
                        squad.Men = null;
                    }
                    Done(squad);
                    return;
                }
            }
        }

        // The squad has no one left standing: after a while the car goes back empty.
        bool Wiped(Squad squad)
        {
            if (squad.Men != null && !squad.Men.Wiped) return false;
            if (Time.time - squad.ArrivedAt < 20f) return true;
            squad.State = SquadState.Leaving;
            squad.Ordered = true;
            return true;
        }

        void Done(Squad squad)
        {
            squad.State = SquadState.Done;
            if (_lights.TryGetValue(squad.Ride, out var lights)) lights.Set(false, siren: false);
            squad.Ride.Release();
            _squads.Remove(squad);
            if (IsPlayerNews(squad))
                CrewOverlay.Announce("POLICE LEAVING THE SCENE", 4f,
                    new Color(0.55f, 0.78f, 1f));
        }

        static CrewWalker Lead(Squad squad) => squad == null ? null : Lead(squad.Men);

        /// <summary>Whoever is speaking for a body of the law: its sergeant while he is
        /// standing, and the first man still on his feet after that.</summary>
        static CrewWalker Lead(DemoCrews.Unit men)
        {
            if (men == null) return null;
            if (men.Boss != null && !men.Boss.Dead) return men.Boss;
            foreach (var m in men.All()) if (!m.Dead) return m;
            return null;
        }

        void BeginWarning(Squad squad)
        {
            // A squad shouting DROP THE GUNS is a squad LOOKING at it. That is what
            // makes it a police eyewitness to the act (GAN-245), and it has to be
            // recorded while it is true - by the time an arrest is made the same squad
            // is Securing a quiet street and nothing about its state says it saw
            // anything.
            NoteLawWatchedIt(squad);
            squad.State = SquadState.Warning;
            squad.Timer = 3f;
            var lead = Lead(squad);
            if (lead != null)
            {
                lead.OrderToPoint(lead.Tf.position); // stand
                lead.HearShot(squad.Scene);
                lead.Shout(3f);
            }
            foreach (var man in squad.Men.All()) if (!man.Dead && man != lead) man.HearShot(squad.Scene);
            if (IsPlayerNews(squad))
                CrewOverlay.Announce("\"POLICE! DROP THE GUNS!\"", 3.5f,
                    new Color(0.55f, 0.78f, 1f));
            _crews.PoliceWarning(lead != null ? lead.Tf.position : squad.Scene, squad.Men);
        }

        // Whoever fired in the last few seconds and is not the law: of their crews
        // the squad goes for the nearest one no other squad has taken on - two
        // squads at one scene split the war between them instead of piling onto
        // one side of it. Only when every shooting crew is spoken for does a
        // second squad double up, nearest first.
        bool PickFight(Squad squad)
        {
            _shooters.Clear();
            StreetAlarm.ShootersSince(4f, _shooters);
            var lead = Lead(squad);
            var from = lead != null ? lead.Tf.position : squad.Scene;
            DemoCrews.Unit target = null;
            float bestD = float.MaxValue;
            bool bestTaken = true;
            bool bestHunted = false;
            foreach (var s in _shooters)
            {
                if (s.Faction == StreetAlarm.PoliceFaction || s.Dead) continue;
                var unit = _crews.UnitOf(s);
                if (unit == null || unit.Wiped || unit.Retreated || unit.IsPolice) continue;
                bool taken = TakenByAnother(squad, unit);
                float d = (unit.Position - from).sqrMagnitude;
                bool better = target == null
                    || (bestTaken && !taken)
                    || (taken == bestTaken && d < bestD);
                if (better) { bestD = d; target = unit; bestTaken = taken; bestHunted = Hunted(unit); }
            }

            // AND THE MEN THE CITY IS LOOKING FOR, whether or not they have fired in the
            // last four seconds (GAN-220). A crew that shot a policeman and then stopped
            // shooting and ran used to fall off this list the moment it stopped - which
            // is the moment the hunt is supposed to begin.
            if (_swarm)
            {
                foreach (var unit in _hunted)
                {
                    if (unit == null || unit.Wiped || unit.Retreated || unit.Surrendered) continue;
                    bool taken = TakenByAnother(squad, unit);
                    float d = (unit.Position - from).sqrMagnitude;
                    bool better = target == null
                        || (!bestHunted)
                        || (bestTaken && !taken)
                        || (taken == bestTaken && d < bestD);
                    if (better) { bestD = d; target = unit; bestTaken = taken; bestHunted = true; }
                }
            }
            if (target == null) return false;
            if (bestHunted) squad.SwarmResponse = true;
            NoteLawWatchedIt(squad);
            // A squad shot at before it was sent already holds the shooter as a fight
            // that CAME to it, and a man in one of those waits behind cover for the
            // range. Dispatch is the order: the same suspect is sicced again so the
            // fight is an ordered one and the officers close in.
            if (squad.Men.TargetUnit != target || !squad.Men.OrderedFight)
                _crews.Sic(squad.Men, target);
            squad.State = SquadState.Engaging;
            squad.Reassess = 0.5f;
            return true;
        }

        // True when another squad's men are already on this crew.
        bool TakenByAnother(Squad squad, DemoCrews.Unit unit)
        {
            foreach (var other in _squads)
                if (other != squad && other.Men != null && !other.Men.Wiped &&
                    other.Men.TargetUnit == unit)
                    return true;
            return false;
        }

        void BeginSecuring(Squad squad)
        {
            if (squad.Ride is PoliceBeat beat)
            {
                // Combat ownership ends here. Hand the permanent pair back to the
                // existing foot-scene/arrest lifecycle instead of treating it as the
                // temporary two-man detail spawned from a patrol car.
                beat.SecureScene();
                _squads.Remove(squad);
                _footOnSceneAt[beat] = Time.time;
                return;
            }
            if (squad.State != SquadState.Securing) squad.SecuringAt = Time.time;
            squad.State = SquadState.Securing;
            squad.Timer = Random.Range(SceneSeconds * 0.7f, SceneSeconds * 1.3f);
            squad.MoveAlongIn = 2f;
            var men = squad.Men;
            men.TargetUnit = null;
            var from = squad.Ride.Position;
            var dir = squad.Scene - from;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.1f ? dir.normalized : Vector3.forward;
            // Out of the car and onto the crime scene by the crew's shared WalkRoute,
            // at the run. The response must not become a pair of leisurely straight
            // lines through whatever stands between the cruiser and the body.
            var scenePost = squad.Scene - dir * 3.5f;
            _crews.MarchTo(men, scenePost,
                run: LivingCity.Police.PoliceProcedure.RunToScene,
                keepOffRoad: false, allowCustody: true);
        }

        void BeginLeaving(Squad squad)
        {
            squad.State = SquadState.Leaving;
            squad.Ordered = false;
        }

        // ------------------------------------------------------------ the men

        // Two officers of the law as a unit of the arena: a sergeant and a constable,
        // .38s in hand, in the police pack's bodies - dealt at a spot (beside the car),
        // or straight into a car's seats.
        DemoCrews.Unit SpawnSquad(Vector3 at, Vector3 facing, int count, CrewCar aboardOf,
            PedLink spawnLink = null, float spawnT = 0f)
        {
            if (_crews == null) return null;
            var names = new List<string>();
            var hoods = new List<GameObject>();
            for (int i = 1; i < count; i++)
            {
                names.Add(OfficerName());
                if (_officerPrefabs.Count > 0) hoods.Add(_officerPrefabs[(_rank + i) % _officerPrefabs.Count]);
            }
            var bossPrefab = _officerPrefabs.Count > 0 ? _officerPrefabs[_rank % _officerPrefabs.Count] : null;
            _rank++;
            if (bossPrefab == null)
            {
                Debug.LogWarning("[Police] No officer body - the law sits this one out.");
                return null;
            }
            var unit = _crews.AddRival(StreetAlarm.PoliceFaction, "Police", "Sgt. " + OfficerName(), bossPrefab,
                names, hoods, at, facing, _sidearm, EquipmentKind.Pistol, lineUp: true,
                spawnLink: spawnLink, spawnT: spawnT);
            if (unit == null) return null;
            unit.Root.name = "Police · " + unit.Name;
            foreach (var man in unit.All()) man.RangeFactor = 0.9f;
            if (aboardOf != null)
            {
                // straight into the seats: the car carries them from here
                int seat = 0;
                foreach (var man in unit.All())
                {
                    aboardOf.SeatOf[man] = seat;
                    aboardOf.Aboard.Add(man);
                    man.SetRiding(true);
                    man.Tf.SetPositionAndRotation(aboardOf.Seat(seat), aboardOf.Tf.rotation);
                    seat++;
                }
                aboardOf.Occupant = unit;
                unit.Car = aboardOf;
            }
            return unit;
        }

        static readonly string[] Surnames =
            { "Kowalski", "Brennan", "Delgado", "Murphy", "Washington", "Russo", "O'Neill", "Jackson", "Ferraro", "Nowak" };

        string OfficerName() => Surnames[(_rank * 7 + Random.Range(0, 3)) % Surnames.Length];

        // ------------------------------------------------------------ the scene

        // Tape and cones across the pavement between the chalk and where the crowd
        // stands: two cones six metres apart, the tape between them.
        void TapeOff(Squad squad)
        {
#if UNITY_EDITOR
            var cone = RoadDemo.DemoAssetLoad.Load<GameObject>(
                "Assets/Synty/PolygonPoliceStation/Prefabs/Props/SM_Prop_Cone_01.prefab");
            var tape = RoadDemo.DemoAssetLoad.Load<GameObject>(
                "Assets/Synty/PolygonPoliceStation/Prefabs/Props/SM_Prop_Scene_Tape_01.prefab");
            var from = squad.Ride.Position;
            var dir = squad.Scene - from;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.1f ? dir.normalized : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, dir);
            float y = _crews.GroundY;
            var mid = squad.Scene - dir * 5.5f;
            mid.y = y;
            var root = new GameObject("Scene of Crime").transform;
            var a = mid + side * 3f;
            var b = mid - side * 3f;
            if (cone)
            {
                Instantiate(cone, a, Quaternion.identity, root);
                Instantiate(cone, b, Quaternion.identity, root);
            }
            if (tape)
            {
                var strip = Instantiate(tape, mid, Quaternion.LookRotation(side, Vector3.up), root);
                strip.transform.position = mid + Vector3.up * 0.02f;
                // stretch it to span the cones, whatever length the pack made it
                var rs = strip.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var bnd = rs[0].bounds;
                    foreach (var r in rs) bnd.Encapsulate(r.bounds);
                    float len = Mathf.Max(bnd.size.x, bnd.size.z);
                    if (len > 0.05f)
                    {
                        var s = strip.transform.localScale;
                        s.z *= 6f / len;
                        strip.transform.localScale = s;
                    }
                }
            }
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
            foreach (var col in root.GetComponentsInChildren<Collider>()) Destroy(col);
#endif
        }

        // ------------------------------------------------------------ on foot

        // The beat officer sent to the scene stands there a while, then goes back to
        // his beat; the dispatcher only has to release him.
        void TickFoot()
        {
            foreach (var u in _units)
            {
                if (u.Carries) continue;
                // Warning/engagement is currently owned by TickSquad. Its ordinary
                // ninety-second scene timer must not release it in the middle of a long
                // fight.
                if (ResponseOwns(u))
                {
                    _footOnSceneAt.Remove(u);
                    continue;
                }
                // A PAIR THAT CANNOT GET THERE IS SENT BACK, and the incident is free to
                // send the next nearest. Not a pair a telephone call, a collar or a
                // custody still owns - those have their own patience and hand him back
                // themselves.
                if (u is PoliceBeat stuck && stuck.StalledOnTheWay && !FootHeldByLawWork(u))
                {
                    u.Release();
                    // not the nearest again; TickPending sends the next if the scene is
                    // still owed one
                    if (!_footTried.Contains(u)) _footTried.Add(u);
                    continue;
                }
                if (u.OnScene)
                {
                    // A scene timer may send an idle beat home; it may not take the
                    // officers away from an open collar, complaint, or prisoners who
                    // are still waiting for their carrier.
                    if (FootHeldByLawWork(u))
                    {
                        _footOnSceneAt.Remove(u);
                        continue;
                    }
                    if (!_footOnSceneAt.ContainsKey(u)) _footOnSceneAt[u] = Time.time;
                    else if (Time.time - _footOnSceneAt[u] > SceneSeconds && StreetAlarm.QuietFor > 20f)
                    {
                        _footOnSceneAt.Remove(u);
                        u.Release();
                    }
                }
                else if (u.Available) _footOnSceneAt.Remove(u);
            }
        }

        bool FootHeldByLawWork(IPoliceUnit unit)
        {
            if (unit == null) return false;
            if (_collar != Collar.None && _arrestOfficer == unit) return true;
            for (var i = 0; i < _calls.Count; i++)
                if (_calls[i] != null && _calls[i].Stage != CallStage.Done &&
                    _calls[i].Unit == unit)
                    return true;
            for (var i = 0; i < _custodies.Count; i++)
                if (_custodies[i] != null && !_custodies[i].Finished &&
                    _custodies[i].Beat == unit)
                    return true;
            return false;
        }
    }

    /// <summary>
    /// The crew demo's cruiser: a police-pack four-door driven by the same path
    /// controller as the outfit's car (CrewCar), with its two officers aboard. Sent,
    /// it pulls in at the kerb short of the scene; released, it drives home and
    /// parks, men inside, ready for the next call.
    /// </summary>
    public sealed class PoliceCruiser : IPoliceUnit
    {
        public readonly CrewCar Car;
        public readonly DemoCrews.Unit Men;
        readonly Vector3 _home;
        readonly DemoCrews _crews;
        Vector3 _target;
        bool _sent;
        float _parkingRetryAt;

        public PoliceCruiser(CrewCar car, DemoCrews.Unit men, Vector3 home, DemoCrews crews)
        {
            Car = car;
            Men = men;
            _home = home;
            _crews = crews;
        }

        public Transform Tf => Car.Tf;
        public Vector3 Position => Car.Position;
        internal bool CustodyReserved { get; set; }
        public bool Available => !CustodyReserved && !_sent && Men != null && !Men.Wiped;
        public bool Carries => true;
        public int Precinct { get; set; }
        public bool OnScene => _sent && !Car.ParkingFailed && !Car.Moving && Flat(Car.Position - _target).sqrMagnitude < 8f * 8f;
        public Vector3 Home => _home;
        public bool AtHome => !_sent && !Car.ParkingFailed && !Car.Moving && Flat(Car.Position - _home).sqrMagnitude < 8f * 8f;

        internal void TickParkingRetry()
        {
            if (!Car.ParkingFailed || Car.Wrecked || Car.EngineDead || Car.Tf == null ||
                Men == null || Men.Wiped || Time.time < _parkingRetryAt) return;
            _parkingRetryAt = Time.time + 3f;
            Car.ParkNear(_sent ? _target : _home);
        }

        /// <summary>Short of the scene ALONG THE STREET it is on, on the car's side of
        /// it. Measured along x it stood off into the yards on every north-south street.</summary>
        public void RouteTo(Vector3 scene, float standOff)
        {
            _sent = true;
            Car.CivicResponse = true;
            var toScene = Flat(scene - Car.Position);
            var lane = Car.Net?.NearestLane(scene, out _, 12f);
            var along = lane != null ? Flat(lane.Dir) : toScene;
            if (along.sqrMagnitude < 1e-6f) along = Vector3.forward;
            along.Normalize();
            float dir = Vector3.Dot(toScene, along) >= 0f ? 1f : -1f;
            _target = scene - along * (dir * standOff);
            _target.y = Car.RoadY;
            _parkingRetryAt = Time.time + 3f;
            Car.ParkNear(_target);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        public void Release()
        {
            _sent = false;
            Car.CivicResponse = false;
            _parkingRetryAt = Time.time + 3f;
            Car.ParkNear(_home);
        }
    }

    /// <summary>The roof bar and the siren on a police car: the pack's light bar
    /// (a renderer named ..._Lights_..) flashed red and blue by property block, or -
    /// on a body without one - two small lamps stood on the roof; and a looping
    /// wail the car carries while it drives to a call.</summary>
    public sealed class PoliceLights
    {
        readonly List<Renderer> _bar = new List<Renderer>();
        readonly Renderer[] _lamps = new Renderer[2];
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        readonly AudioSource _siren;
        bool _on;
        float _phase;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Color Red = new Color(1f, 0.08f, 0.05f), Blue = new Color(0.1f, 0.35f, 1f);

        public PoliceLights(Transform car)
        {
            foreach (var r in car.GetComponentsInChildren<Renderer>(true))
                if (r.name.IndexOf("Lights", System.StringComparison.OrdinalIgnoreCase) >= 0) _bar.Add(r);
            if (_bar.Count == 0)
            {
                // no bar part: two lamps on the roof
                var rs = car.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                    for (int i = 0; i < 2; i++)
                    {
                        var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        lamp.name = i == 0 ? "Lamp Red" : "Lamp Blue";
                        Object.Destroy(lamp.GetComponent<Collider>());
                        lamp.transform.SetParent(car, true);
                        lamp.transform.position = new Vector3(b.center.x, b.max.y + 0.08f, b.center.z) + car.right * (i == 0 ? -0.32f : 0.32f);
                        lamp.transform.rotation = car.rotation;
                        lamp.transform.localScale = new Vector3(0.28f, 0.14f, 0.34f);
                        var mr = lamp.GetComponent<MeshRenderer>();
                        if (shader) mr.sharedMaterial = new Material(shader) { name = lamp.name };
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        _lamps[i] = mr;
                        lamp.SetActive(false);
                    }
                }
            }
            var clip = DemoSounds.Siren;
            if (clip != null)
            {
                _siren = car.gameObject.AddComponent<AudioSource>();
                _siren.clip = clip;
                _siren.loop = true;
                _siren.playOnAwake = false;
                _siren.spatialBlend = 1f;
                _siren.rolloffMode = AudioRolloffMode.Linear;
                _siren.minDistance = 30f;
                _siren.maxDistance = 320f;
                _siren.dopplerLevel = 0.3f;
                _siren.volume = DemoSounds.SirenVolume * DemoSounds.Master;
            }
        }

        public void Set(bool lights, bool siren)
        {
            _on = lights;
            foreach (var l in _lamps) if (l) l.gameObject.SetActive(lights);
            if (!lights) Paint(Color.white, 0f);
            if (_siren != null)
            {
                if (siren && !_siren.isPlaying) _siren.Play();
                else if (!siren && _siren.isPlaying) _siren.Stop();
            }
        }

        public void Tick(float dt)
        {
            if (!_on) return;
            _phase += dt * 3f;
            bool red = Mathf.FloorToInt(_phase) % 2 == 0;
            var c = red ? Red : Blue;
            Paint(c, 2.5f);
            if (_lamps[0]) Tint(_lamps[0], red ? Red : Red * 0.15f);
            if (_lamps[1]) Tint(_lamps[1], red ? Blue * 0.15f : Blue);
        }

        void Paint(Color c, float glow)
        {
            foreach (var r in _bar)
            {
                if (!r) continue;
                r.GetPropertyBlock(_block);
                if (glow <= 0f) { _block.Clear(); r.SetPropertyBlock(_block); continue; }
                _block.SetColor(BaseColorId, c);
                _block.SetColor(EmissionId, c * glow);
                r.SetPropertyBlock(_block);
            }
        }

        void Tint(Renderer r, Color c)
        {
            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, c);
            _block.SetColor(ColorId, c);
            r.SetPropertyBlock(_block);
        }
    }
}
