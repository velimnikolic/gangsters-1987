using System.Collections.Generic;
using LivingCity.Personnel;
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
        // Only the law that HAPPENS to be near a fight turns out for it - a patrol a
        // couple of blocks off. There is no city-wide dispatch pulling a car across the
        // whole map to a scene it could never see: if nobody is near, nobody comes until
        // a patrol's own beat carries it close. (The reach of the response, not of the
        // patrols - the cars are already spread over the city; this is who answers.)
        const float ResponseRange = 150f;    // metres a unit must be within to answer a scene
        // WHAT A MAN OF THE LAW HEARS. A block and the street round it - further than
        // the report itself carries to the crowd (CrewArms Loudness: 45 m for a .38,
        // 80 for a rifle), and deliberately so. Loudness is what a passer-by READS: how
        // near he has to be for a bang to frighten him. An officer a block off does not
        // have to be frightened by it, only to know what it was and which way it came
        // from, and that is the whole of a beat: he turns and he goes. Anybody inside
        // this rings it in himself, at once, and does not wait on a telephone.
        const float Earshot = 110f;
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
        int _carsSent;
        float _lastSentAt = -1000f;
        bool _officerDied;
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
            StreetAlarm.OnShot -= OnShot;
            StreetAlarm.OnDeath -= OnDeath;
            StreetAlarm.OnComplaint -= OnComplaint;
        }

        /// <summary>A unit the dispatcher may send. Cars get lights and a siren.</summary>
        public void Register(IPoliceUnit unit)
        {
            if (unit == null || _units.Contains(unit)) return;
            _units.Add(unit);
            if (unit.Carries && unit.Tf != null) _lights[unit] = new PoliceLights(unit.Tf);
        }

        /// <summary>A permanent street unit whose body has been lost leaves dispatch's
        /// books before its replacement is dealt. Temporary response squads use their
        /// existing lifecycle and never come through here.</summary>
        public void Unregister(IPoliceUnit unit)
        {
            if (unit == null) return;
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
            RaiseSwarm(beat.Position, SwarmGrade.ShotsFired, attacker);
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
            if (StreetAlarm.IncidentNumber != _incident)
            {
                // a new incident: the clock to the call starts - somebody will have rung
                // within the minute regardless, sooner if a witness gets to a phone
                _incident = StreetAlarm.IncidentNumber;
                _shotHeat = 0f;
                _called = false;
                _carsSent = 0;
                _officerDied = false;
                _callAt = Time.time + NobodyRang;
                // WHO SAW IT IS DECIDED NOW (GAN-245), not when an officer eventually
                // gets round to an arrest a hundred seconds later. Evidence is about the
                // moment of the act: the people who were on this pavement when the first
                // round went off are the witnesses, and the crowd that gathers
                // afterwards - or has gone home by then - is not.
                SnapshotTheScene(shot.Pos);
                CrewOverlay.Announce("SHOTS FIRED", 4f, new Color(1f, 0.55f, 0.45f));
            }
            float add = Mathf.Min(ShotHeat, ShotHeatCap - _shotHeat);
            if (add > 0f) { _shotHeat += add; Heat = Mathf.Min(120f, Heat + add); }

            // The first round AT any officer is the escalation, not only a hit or a
            // death. The shooter's crew already carries its ordered target.
            var shooterUnit = shot.Shooter != null && _crews != null
                ? _crews.UnitOf(shot.Shooter) : null;
            if (shooterUnit != null && shooterUnit.TargetUnit != null &&
                shooterUnit.TargetUnit.IsPolice)
            {
                // Police do not open a fight, but a shot directed at their unit is the
                // line: the whole pair/squad answers through the shared combat model.
                if (!shooterUnit.TargetUnit.Wiped)
                    _crews.Sic(shooterUnit.TargetUnit, shooterUnit);
                RaiseSwarm(shot.Pos, SwarmGrade.ShotsFired, shooterUnit);
            }

            // a patrol in earshot rings it in itself, at once - no telephone, no wait
            float heard = Mathf.Max(shot.Loudness, Earshot);
            foreach (var u in _units)
                if (u.Tf != null && (u.Position - shot.Pos).sqrMagnitude < heard * heard)
                { _callAt = Mathf.Min(_callAt, Time.time); break; }
        }

        void OnDeath(Vector3 where, StreetAlarm.DeathOf who, int victimFaction)
        {
            float add = who switch
            {
                StreetAlarm.DeathOf.Civilian => CivilianDeathHeat,
                StreetAlarm.DeathOf.Officer => OfficerDeathHeat,
                _ => GangDeathHeat,
            };
            Heat = Mathf.Min(120f, Heat + add);
            if (who != StreetAlarm.DeathOf.Officer) return;
            _officerDied = true;
            // and the radio call that is not an escalation but a different kind of day
            // (GAN-220): every car in the city, and a hunt that outlives the shooting
            RaiseSwarm(where, SwarmGrade.OfficerDown);
            // and the precinct is a man short until the department fills the hole
            // (GAN-226). Through here rather than through a second listener: StreetAlarm
            // is the one channel for a death, and this is already listening to it.
            if (Force != null) Force.OfficerDown(where);
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

        void Update()
        {
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

            for (int i = _squads.Count - 1; i >= 0; i--) TickSquad(_squads[i], dt); // Done() removes
            TickFoot();
            TickSwarm(dt);
            TickWanted(dt);
            TickCustody(dt);
            TickCalls(dt);
            WitnessWatch.Tick();
            TickArrest(dt);
            foreach (var kv in _lights) kv.Value.Tick(dt);
        }

        // Cars by heat: none below the second level (a beat officer walks over), one,
        // then two, three at the top; the first call always brings something.
        int CarsWanted()
        {
            int level = Level;
            int cars = level <= 1 ? 0 : level == 2 ? 1 : level <= 4 ? 2 : 3;
            if (cars == 0 && !AnyFootAvailable()) cars = 1;
            return cars;
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
            bool any = false;
            if (first)
            {
                // EVERY man on foot who could hear it turns out, not just the nearest.
                // Two officers a block apart both heard the same shots; one of them
                // walking on as though he had not is the thing that reads as nobody
                // being home. Out of earshot it is the nearest man only - he is coming
                // because he was told, and one man is what a telephone call sends.
                foreach (var u in _units)
                {
                    if (u.Carries || !u.Available || u.Tf == null) continue;
                    if ((u.Position - scene).sqrMagnitude > Earshot * Earshot) continue;
                    u.RouteTo(scene, 6f);
                    any = true;
                }
                if (!any)
                {
                    var foot = Nearest(scene, carries: false);
                    if (foot != null) { foot.RouteTo(scene, 6f); any = true; }
                }
            }
            while (_carsSent < wanted)
            {
                var car = Nearest(scene, carries: true);
                if (car == null) break;
                car.RouteTo(scene, StandOff);
                _squads.Add(new Squad { Ride = car, Men = MenOf(car), Scene = scene, State = SquadState.Sent });
                if (_lights.TryGetValue(car, out var lights)) lights.Set(true, siren: true);
                _carsSent++;
                any = true;
            }
            _lastSentAt = Time.time;
            if (any) CrewOverlay.Announce("POLICE RESPONDING", 5f, new Color(0.55f, 0.78f, 1f));
        }

        /// <summary><paramref name="anyDistance"/> is the swarm and nothing else
        /// (GAN-220): response is station-LOCAL by a deliberate rule, and a dead officer
        /// is the one sanctioned exception to it.</summary>
        IPoliceUnit Nearest(Vector3 to, bool carries, bool anyDistance = false)
        {
            IPoliceUnit best = null;
            float bestD = anyDistance
                ? float.MaxValue
                : ResponseRange * ResponseRange;   // out of this reach, nobody answers
            foreach (var u in _units)
            {
                if (u.Carries != carries || !u.Available || u.Tf == null) continue;
                float d = (u.Position - to).sqrMagnitude;
                if (d < bestD) { bestD = d; best = u; }
            }
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
            public float RouteRetryAt;
        }

        void TickSquad(Squad squad, float dt)
        {
            var ride = squad.Ride;
            switch (squad.State)
            {
                case SquadState.Sent:
                    if (!ride.OnScene) return;
                    squad.ArrivedAt = Time.time;
                    if (_lights.TryGetValue(ride, out var lights)) lights.Set(true, siren: false);
                    CrewOverlay.Announce("POLICE ON THE SCENE", 4f, new Color(0.55f, 0.78f, 1f));
                    if (ride is PoliceCruiser cruiser)
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
                                if (!man.Dead) man.OrderToPoint(ride.Position + ride.Tf.right * (k++ % 2 == 0 ? -2.2f : 2.2f), k * 0.3f);
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
            CrewOverlay.Announce("POLICE LEAVING THE SCENE", 4f, new Color(0.55f, 0.78f, 1f));
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
            CrewOverlay.Announce("\"POLICE! DROP THE GUNS!\"", 3.5f, new Color(0.55f, 0.78f, 1f));
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
            NoteLawWatchedIt(squad);
            if (squad.Men.TargetUnit != target) _crews.Sic(squad.Men, target);
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

        public PoliceCruiser(CrewCar car, DemoCrews.Unit men, Vector3 home, DemoCrews crews)
        {
            Car = car;
            Men = men;
            _home = home;
            _crews = crews;
        }

        public Transform Tf => Car.Tf;
        public Vector3 Position => Car.Position;
        public bool Available => !_sent && Men != null && !Men.Wiped;
        public bool Carries => true;
        public int Precinct { get; set; }
        public bool OnScene => _sent && !Car.Moving && Flat(Car.Position - _target).sqrMagnitude < 8f * 8f;
        public Vector3 Home => _home;
        public bool AtHome => !_sent && !Car.Moving && Flat(Car.Position - _home).sqrMagnitude < 8f * 8f;

        /// <summary>Short of the scene ALONG THE STREET it is on, on the car's side of
        /// it. Measured along x it stood off into the yards on every north-south street.</summary>
        public void RouteTo(Vector3 scene, float standOff)
        {
            _sent = true;
            var toScene = Flat(scene - Car.Position);
            var lane = Car.Net?.NearestLane(scene, out _, 12f);
            var along = lane != null ? Flat(lane.Dir) : toScene;
            if (along.sqrMagnitude < 1e-6f) along = Vector3.forward;
            along.Normalize();
            float dir = Vector3.Dot(toScene, along) >= 0f ? 1f : -1f;
            _target = scene - along * (dir * standOff);
            _target.y = Car.RoadY;
            Car.ParkNear(_target);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        public void Release()
        {
            _sent = false;
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
