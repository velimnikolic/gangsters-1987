using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace AirportDemo
{
    // Who and what is moving. The aeroplanes and everything that serves them are the
    // demo's own classes (FlightOps, GroundOps, Rotorcraft, AirportDriver); the
    // people are the road demo's walker with a round of points, the way the harbour's
    // dock hands are; the cars landside drive routes rather than a lane graph,
    // because a one-way kerb loop is a route and not a network.
    public partial class AirportDemoBuilder
    {
        readonly List<Aircraft> _parkedPlanes = new List<Aircraft>();
        List<GameObject> _jetPrefabs, _smallJetPrefabs, _commuterPrefabs, _lightPrefabs;
        ParticleSystem _touchdownFx, _startFx, _washFx;

        void BuildTaxiGraph()
        {
            _flights = new FlightOps(_rng, westerlyWind, RunwayHalf, commuterInterval);
        }

        // ------------------------------------------------------------ aeroplanes

        void LoadFleet()
        {
            if (_lightPrefabs != null) return;
            _jetPrefabs = AirportKit.LoadAll(AirportKit.Jets, quiet: true);
            _smallJetPrefabs = AirportKit.LoadAll(AirportKit.SmallJets, quiet: true);
            _commuterPrefabs = AirportKit.LoadAll(AirportKit.Commuters, quiet: true);
            _lightPrefabs = AirportKit.LoadAll(AirportKit.LightPlanes, quiet: true);
            if (_lightPrefabs.Count == 0 && _commuterPrefabs.Count == 0 && _jetPrefabs.Count == 0)
                Debug.LogWarning("[AirportDemo] no aircraft under Assets/SimpleAirport/Prefabs/Vehicles - the field will be empty.");
        }

        /// <summary>One aeroplane of a class, scaled to the span that class flies at
        /// and stripped of the pack's own scripts and colliders because this demo flies
        /// it itself. The aircraft - and only the aircraft - are Simple Airport's; see
        /// the note in AirportKit about why nothing else in that pack is used.</summary>
        Aircraft MakePlane(Aircraft.Kind kind, string name)
        {
            LoadFleet();
            List<GameObject> bag;
            float span;
            switch (kind)
            {
                case Aircraft.Kind.Jet:
                    bag = _jetPrefabs.Count > 0 ? _jetPrefabs : _smallJetPrefabs;
                    span = AirportSpec.JetSpan;
                    break;
                case Aircraft.Kind.Commuter:
                    bag = _commuterPrefabs;
                    span = AirportSpec.CommuterSpan;
                    break;
                default:
                    bag = _lightPrefabs;
                    span = AirportSpec.GaSpan;
                    break;
            }
            if (bag == null || bag.Count == 0) return null;
            var go = Instantiate(Pick(bag), _liveRoot);
            go.name = name;
            AirportKit.StripBehaviours(go, keepAnimator: false);
            // aircraft stay on the default layer: one on final is 1.7 km out, and the
            // mid-distance cull would have it appear out of nothing over the fence
            AirportKit.SetLayerDeep(go, 0);
            var a = new Aircraft { Callsign = name, Class = kind };
            a.Bind(go.transform, span);
            a.SetEffects(_touchdownFx, _startFx);
            WarnIfNotUrp(go);
            return a;
        }

        string Tail() => "N" + (1200 + Rnd(700)) + "K";

        bool _shaderWarned;

        /// <summary>The aircraft pack ships on the built-in pipeline's Standard shader,
        /// which URP cannot draw - it comes out magenta. The editor converts the pack
        /// once (SimpleAirportUrp); this says so out loud if that has not happened,
        /// because "why is my aeroplane purple" is otherwise a long afternoon.</summary>
        void WarnIfNotUrp(GameObject go)
        {
            if (_shaderWarned) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    var n = m.shader.name;
                    if (n.StartsWith("Universal Render Pipeline") || n.StartsWith("Shader Graphs")) continue;
                    _shaderWarned = true;
                    Debug.LogWarning($"[AirportDemo] the aircraft material \"{m.name}\" is on \"{n}\", which this URP project " +
                                     "cannot draw - that is why they are magenta. Run Tools/City/Catalog/Convert Simple Airport To URP.");
                    return;
                }
        }

        void BuildFlights()
        {
            MakeEffects();
            if (activeAircraft <= 0) return;
            int stands = _flights.StandCount;
            if (stands == 0) return;

            // the airline stands first - the trijet on one and the turboprop on the
            // other - then whatever tie-down is going for the light aeroplanes, which
            // are most of what a field like this actually sees
            var taken = new HashSet<int>();
            for (int i = 0; i < activeAircraft; i++)
            {
                bool airline = i < _flights.AirlineStands;
                int stand = airline ? i : PickFreeStand(taken, stands);
                if (stand < 0) break;
                taken.Add(stand);
                var kind = airline
                    ? (i == 0 ? Aircraft.Kind.Jet : Aircraft.Kind.Commuter)
                    : Aircraft.Kind.Light;
                string name = kind == Aircraft.Kind.Jet ? "Flight 21"
                            : kind == Aircraft.Kind.Commuter ? "Flight 108" : Tail();
                var a = MakePlane(kind, name);
                // nothing of that class in the pack: the stand takes a light one
                if (a == null && airline) a = MakePlane(Aircraft.Kind.Light, Tail());
                if (a == null) break;
                _flights.Adopt(a, stand, airline);
            }
            Debug.Log($"[AirportDemo] {_flights.Fleet.Count} aeroplanes working the field, {stands} stands on the ramp");
        }

        int PickFreeStand(HashSet<int> taken, int stands)
        {
            int first = AirportSpec.CommuterStandX.Length;
            for (int tries = 0; tries < 60 && stands > first; tries++)
            {
                int s = first + Rnd(stands - first);
                if (!taken.Contains(s)) return s;
            }
            for (int s = 0; s < stands; s++) if (!taken.Contains(s)) return s;
            return -1;
        }

        /// <summary>The aeroplanes that are going nowhere: tied down in the rows, and
        /// one inside whichever hangar stands open.</summary>
        void BuildParkedAircraft()
        {
            if (parkedAircraft <= 0) return;
            var used = new HashSet<int>();
            foreach (var a in _flights.Fleet) used.Add(a.Stand);
            var chock = AirportKit.TryLoad(AirportKit.Chock);
            int placed = 0;
            for (int s = _flights.AirlineStands; s < _flights.StandCount && placed < parkedAircraft; s++)
            {
                if (used.Contains(s)) continue;
                if (Chance(0.22f)) continue;          // a row with gaps reads as a row in use
                var (pos, yaw) = _flights.Stand(s);
                var a = MakePlane(Aircraft.Kind.Light, Tail());
                if (a == null) break;
                a.Park(pos, yaw + Rnd(-3f, 3f));
                _parkedPlanes.Add(a);
                placed++;
                if (chock != null && Chance(0.7f))
                    AirportKit.Prop(chock, pos + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, 1.6f),
                                    yaw + 90f, _detailRoot, "Chocks");
            }

            foreach (var mouth in _openHangars)
            {
                var a = MakePlane(Aircraft.Kind.Light, Tail());
                if (a == null) break;
                a.Park(mouth + new Vector3(Rnd(-1.5f, 1.5f), 0f, 7f), 180f + Rnd(-6f, 6f));
                _parkedPlanes.Add(a);
            }
            Debug.Log($"[AirportDemo] {_parkedPlanes.Count} aeroplanes tied down and hangared");
        }

        void MakeEffects()
        {
            _touchdownFx = OneEffect(AirportKit.FxTouchdown, "Touchdown dust");
            _startFx = OneEffect(AirportKit.FxPropWash, "Prop wash");
            _washFx = OneEffect(AirportKit.FxPropWash, "Rotor wash");
        }

        ParticleSystem OneEffect(string path, string name)
        {
            var prefab = AirportKit.TryLoad(path);
            if (prefab == null) return null;
            var go = Instantiate(prefab, _liveRoot);
            go.name = name;
            var ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                main.playOnAwake = false;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            return ps;
        }

        // ------------------------------------------------------------ helicopters

        void BuildRotorcraft()
        {
            if (!helicopters) return;
            var pad = new Vector3(AirportSpec.HelipadX, AirportSpec.PaveY, AirportSpec.HelipadZ);

            var helis = AirportKit.LoadAll(AirportKit.Helicopters, quiet: true);
            var police = helis.Count > 0 ? helis[0] : null;
            if (police != null)
            {
                var go = Instantiate(police, _liveRoot);
                go.name = "Sheriff's helicopter";
                AirportKit.StripBehaviours(go, keepAnimator: false);
                AirportKit.SetLayerDeep(go, 0);
                var r = new Rotorcraft { Pad = pad, PadYaw = 200f, Resident = true };
                r.Bind(go.transform, AirportSpec.HeliRotor);
                r.SetWash(_washFx);
                // out over the country, down the length of the field, back
                r.Patrol.Add(new Vector3(-500f, AirportSpec.PatternAltitude, -420f));
                r.Patrol.Add(new Vector3(500f, AirportSpec.PatternAltitude + 40f, -520f));
                r.Patrol.Add(new Vector3(700f, AirportSpec.PatternAltitude, 320f));
                r.Park();
                _rotors.Add(r);
            }

            var charter = helis.Count > 1 ? helis[1] : null;
            if (charter != null)
            {
                var go = Instantiate(charter, _liveRoot);
                go.name = "Charter helicopter";
                AirportKit.StripBehaviours(go, keepAnimator: false);
                AirportKit.SetLayerDeep(go, 0);
                var r = new Rotorcraft { Pad = pad + new Vector3(-26f, 0f, 0f), PadYaw = 150f, Resident = false };
                r.Bind(go.transform, AirportSpec.HeliRotor);
                r.Patrol.Add(new Vector3(-900f, AirportSpec.PatternAltitude + 90f, -700f));
                r.Patrol.Add(new Vector3(-200f, AirportSpec.PatternAltitude + 60f, -900f));
                r.Park();
                _rotors.Add(r);
            }
        }

        // ------------------------------------------------------------ the ramp

        void BuildGroundOps()
        {
            _ground = new GroundOps(_liveRoot, _rng, _flights);
            if (!groundEquipment) return;

            var lorryPrefabs = AirportKit.LoadAll(AirportKit.Lorries, quiet: true);
            var bowserBody = AirportKit.TryLoad(AirportKit.FuelBowser);
            if (lorryPrefabs.Count > 0)
                _ground.AddBowser(Vehicle(lorryPrefabs[0], "Fuel bowser"), bowserBody);

            var tug = AirportKit.TryLoad(AirportKit.GolfCart);
            var cart = AirportKit.TryLoad(AirportKit.BaggageCart);
            if (tug != null) _ground.AddBaggageTrain(Vehicle(tug, "Baggage tug"), cart);

            var pickup = AirportKit.TryLoad(AirportKit.PickupWorks) ?? AirportKit.TryLoad(AirportKit.Pickup);
            if (pickup != null) _ground.AddFollowMe(Vehicle(pickup, "Follow me"));

            for (int i = 0; i < lorries && lorryPrefabs.Count > 0; i++)
                _ground.AddFreightLorry(Vehicle(lorryPrefabs[(i + 1) % lorryPrefabs.Count], "Freight lorry " + (i + 1)), i);

            // the fire truck stood out on its own apron, and the forklift at the shed
            var fire = AirportKit.TryLoad(AirportKit.FireTruck);
            if (fire != null)
            {
                var go = Vehicle(fire, "Fire truck");
                go.transform.SetPositionAndRotation(
                    new Vector3(AirportSpec.ArffX + 2f, AirportSpec.PaveY, AirportSpec.BuildingFrontZ - 9f),
                    Quaternion.Euler(0f, 182f, 0f));
            }
            var forklift = AirportKit.TryLoad(AirportKit.Forklift);
            if (forklift != null)
            {
                var go = Vehicle(forklift, "Forklift");
                go.transform.SetPositionAndRotation(
                    new Vector3(AirportSpec.CargoX - 9f, AirportSpec.PaveY, AirportSpec.BuildingFrontZ - 7f),
                    Quaternion.Euler(0f, 150f, 0f));
            }
        }

        GameObject Vehicle(GameObject prefab, string name)
        {
            var go = Instantiate(prefab, _liveRoot);
            go.name = name;
            AirportKit.StripBehaviours(go, keepAnimator: false);
            AirportKit.SetLayerDeep(go, PropLayer);
            return go;
        }

        // ------------------------------------------------------------ landside

        void BuildLandsideTraffic()
        {
            _traffic = new AirportTraffic(_rng, _kerbStops);
            var carPrefabs = AirportKit.LoadAll(AirportKit.Cars, quiet: true);
            if (carPrefabs.Count == 0) return;

            var sitLoop = CrewKit.PeopleClip("Sitting_Bench_Idle");
            var drivers = AirportKit.LoadAll(AirportKit.Passengers, quiet: true);

            // round the kerb loop
            int loopCars = Mathf.Max(0, cars / 2);
            for (int i = 0; i < loopCars && _loopRoute.Count >= 3; i++)
            {
                var go = Vehicle(Pick(carPrefabs), "Car");
                _traffic.AddToLoop(go, _loopRoute, i / (float)Mathf.Max(1, loopCars));
                Seat(go, drivers, sitLoop);
            }
            // and along the approach road, both ways, wrapping off the map
            int streetCars = cars - loopCars;
            for (int i = 0; i < streetCars; i++)
            {
                bool east = (i & 1) == 0;
                var go = Vehicle(Pick(carPrefabs), "Car");
                _traffic.AddToStreet(go, east, i / (float)Mathf.Max(1, streetCars),
                                     AirportSpec.StreetZ + (east ? -2.5f : 2.5f));
                Seat(go, drivers, sitLoop);
            }

            // the cabs on the rank, and a bus at the stop
            var cab = AirportKit.TryLoad(AirportKit.Taxi);
            if (cab != null)
                for (int i = 0; i < _cabRank.Count; i++)
                {
                    var go = Vehicle(cab, "Taxi " + (i + 1));
                    _traffic.AddCab(go, _cabRank[i]);
                    if (i < 2) Seat(go, drivers, sitLoop);
                }
            var bus = AirportKit.TryLoad(AirportKit.Bus);
            if (bus != null)
            {
                var go = Vehicle(bus, "Bus");
                go.transform.SetPositionAndRotation(
                    new Vector3(48f, AirportSpec.PaveY, AirportSpec.LoopRoadZ - AirportSpec.LoopRoadHalf * 0.5f),
                    Quaternion.Euler(0f, 90f, 0f));
            }

            // the car park, filled to about the share asked for
            var parkCars = new List<GameObject>();
            int want = Mathf.Min(parkedCars, _parkBays.Count);
            var order = new List<int>();
            for (int i = 0; i < _parkBays.Count; i++) order.Add(i);
            for (int i = order.Count - 1; i > 0; i--) { int r = Rnd(i + 1); (order[i], order[r]) = (order[r], order[i]); }
            for (int i = 0; i < want; i++)
            {
                var (pos, yaw) = _parkBays[order[i]];
                var go = Vehicle(Pick(carPrefabs), "Parked car");
                go.transform.SetPositionAndRotation(pos + new Vector3(Rnd(-0.2f, 0.2f), 0f, Rnd(-0.3f, 0.3f)),
                                                    Quaternion.Euler(0f, yaw + Rnd(-2.5f, 2.5f), 0f));
                parkCars.Add(go);
                var b = AirportKit.BoundsOf(go);
                WalkObstacles.Block(b.min.x, b.max.x, b.min.z, b.max.z);
            }

            if (theLaw) BuildTheLaw(drivers, sitLoop);
            Debug.Log($"[AirportDemo] {cars} cars moving, {parkCars.Count} parked, {_cabRank.Count} cabs on the rank");
        }

        void Seat(GameObject car, List<GameObject> bodies, AnimationClip sitLoop)
        {
            if (bodies == null || bodies.Count == 0 || sitLoop == null) return;
            CarOccupant.Crew(car.transform, bodies, sitLoop, passengerChance: 0.25f, layer: CrowdLayer);
        }

        /// <summary>1987, and this is a field a light single can be flown into at night
        /// with the strip lights off: a sheriff's car on the kerb, and a plain sedan
        /// parked where it can see the general aviation gate with a man sat in it.</summary>
        void BuildTheLaw(List<GameObject> drivers, AnimationClip sitLoop)
        {
            var cruiser = AirportKit.TryLoad(AirportKit.PoliceCar);
            if (cruiser != null)
            {
                var go = Vehicle(cruiser, "Sheriff");
                go.transform.SetPositionAndRotation(
                    new Vector3(-64f, AirportSpec.PaveY, AirportSpec.LoopRoadZ - AirportSpec.LoopRoadHalf * 0.5f),
                    Quaternion.Euler(0f, 90f, 0f));
            }
            var plain = AirportKit.LoadAll(AirportKit.Cars, quiet: true);
            var agents = AirportKit.LoadAll(AirportKit.Agents, quiet: true);
            if (plain.Count > 0)
            {
                var go = Vehicle(plain[0], "Plain sedan");
                go.transform.SetPositionAndRotation(
                    new Vector3(AirportSpec.GaGateX - 26f, AirportSpec.PaveY, AirportSpec.LoopBackZ + 9f),
                    Quaternion.Euler(0f, 96f, 0f));
                if (agents.Count > 0 && sitLoop != null)
                    CarOccupant.Crew(go.transform, agents, sitLoop, passengerChance: 0.5f, layer: CrowdLayer);
            }
        }

        // ------------------------------------------------------------ people

        void BuildPeople()
        {
            _people = new AirportPeople(_liveRoot);
            if (_clips.Walk == null || _clips.Idle == null) return;

            var crewBodies = AirportKit.LoadAll(AirportKit.RampCrew, quiet: true);
            var pilotBodies = AirportKit.LoadAll(AirportKit.Pilots, quiet: true);
            var paxBodies = AirportKit.LoadAll(AirportKit.Passengers, quiet: true);
            var officerBodies = AirportKit.LoadAll(AirportKit.Officers, quiet: true);

            // the ramp crew: rounds between the hangars, the fuel island and the shed
            var rampRounds = new List<List<Vector3>>
            {
                Round(new Vector3(AirportSpec.HangarRowX0 + 20f, 0f, AirportSpec.BuildingFrontZ - 8f), 26f, 12f),
                Round(new Vector3(AirportSpec.HangarRowX0 + 110f, 0f, AirportSpec.BuildingFrontZ - 10f), 30f, 14f),
                Round(new Vector3(AirportSpec.MaintHangarX, 0f, AirportSpec.BuildingFrontZ - 11f), 22f, 10f),
                Round(new Vector3(AirportSpec.FuelIslandX, 0f, AirportSpec.FuelIslandZ - 8f), 18f, 12f),
                Round(new Vector3(AirportSpec.CargoX, 0f, AirportSpec.BuildingFrontZ - 10f), 20f, 10f),
            };
            for (int i = 0; i < rampCrew && crewBodies.Count > 0; i++)
            {
                var w = MakeWalker(Pick(crewBodies), 1.25f);
                if (w == null) break;
                w.Points = rampRounds[i % rampRounds.Count];
                w.DwellRange = new Vector2(4f, 14f);
                w.Begin();
                _people.Adopt(w);
            }

            // the marshaller stands on the commuter stand where he can be seen
            if (crewBodies.Count > 0)
            {
                var w = MakeWalker(Pick(crewBodies), 1.2f);
                if (w != null)
                {
                    w.Static = true;
                    w.Points.Add(new Vector3(AirportSpec.CommuterStandX[0] + 11f, AirportSpec.PaveY, AirportSpec.CommuterStandZ + 4f));
                    w.Begin(atFirst: true);
                    w.Tf.rotation = Quaternion.Euler(0f, 190f, 0f);
                    _people.Adopt(w);
                }
            }

            // the pilots, walking between the FBO and their aeroplanes
            for (int i = 0; i < 3 && pilotBodies.Count > 0; i++)
            {
                var w = MakeWalker(Pick(pilotBodies), 1.4f);
                if (w == null) break;
                w.Points = new List<Vector3>
                {
                    new Vector3(AirportSpec.FboX + Rnd(-7f, 7f), AirportSpec.PaveY, AirportSpec.BuildingFrontZ - 5f),
                    new Vector3(AirportSpec.FuelIslandX + Rnd(-14f, 14f), AirportSpec.PaveY, AirportSpec.FuelIslandZ - 4f),
                    new Vector3(AirportSpec.TieDownX1 - Rnd(0f, 60f), AirportSpec.PaveY, AirportSpec.TieDownRowZ0 + 4f),
                };
                w.DwellRange = new Vector2(8f, 26f);
                w.Begin();
                _people.Adopt(w);
            }

            // the passengers: the kerb, the hall, the gate, and out across the ramp to
            // the aeroplane - which in 1987 at a field like this is exactly the walk
            float kerbZ = AirportSpec.KerbZ - 8f;
            float doorZ = AirportSpec.BuildingFrontZ + AirportSpec.TerminalDepth - 1f;
            for (int i = 0; i < passengers && paxBodies.Count > 0; i++)
            {
                var w = MakeWalker(Pick(paxBodies), Rnd(1.15f, 1.6f));
                if (w == null) break;
                if (i % 3 == 0)
                {
                    // the boarding walk, out of the gate and across to the stand
                    float sx = AirportSpec.CommuterStandX[i % AirportSpec.CommuterStandX.Length];
                    w.Points = new List<Vector3>
                    {
                        new Vector3(Rnd(-24f, 24f), AirportSpec.PaveY + 0.14f, kerbZ),
                        new Vector3(Rnd(-14f, 14f), AirportSpec.PaveY + 0.14f, doorZ),
                        new Vector3(sx + Rnd(-3f, 3f), AirportSpec.PaveY, AirportSpec.BuildingFrontZ - 6f),
                        new Vector3(sx + Rnd(-2f, 2f), AirportSpec.PaveY, AirportSpec.CommuterStandZ + 6f),
                    };
                    w.DwellRange = new Vector2(6f, 20f);
                }
                else
                {
                    // and everybody else on the kerb and the forecourt
                    var centre = new Vector3(Rnd(-52f, 52f), AirportSpec.PaveY + 0.14f, Rnd(doorZ - 2f, kerbZ));
                    w.Points = Round(centre, Rnd(6f, 16f), Rnd(3f, 7f));
                    w.DwellRange = new Vector2(5f, 18f);
                }
                w.Begin();
                _people.Adopt(w);
            }

            // a man on each gate and one at the terminal door
            for (int i = 0; i < 2 && officerBodies.Count > 0; i++)
            {
                var w = MakeWalker(Pick(officerBodies), 1.2f);
                if (w == null) break;
                float x = i == 0 ? AirportSpec.GaGateX + 6f : AirportSpec.TerminalX + 12f;
                float z = i == 0 ? AirportSpec.FenceZ + 3f : doorZ + 1.5f;
                w.Static = true;
                w.Points.Add(new Vector3(x, AirportSpec.PaveY + (i == 0 ? 0f : 0.14f), z));
                w.Begin(atFirst: true);
                w.Tf.rotation = Quaternion.Euler(0f, i == 0 ? 180f : 20f, 0f);
                _people.Adopt(w);
            }

            Debug.Log($"[AirportDemo] {_people.Count} people on the field");
        }

        /// <summary>A ring of points about a centre - a round for somebody with a job
        /// in one part of the ramp and no reason to leave it.</summary>
        List<Vector3> Round(Vector3 centre, float width, float depth)
        {
            var points = new List<Vector3>();
            int n = 3 + Rnd(3);
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f + Rnd(-0.4f, 0.4f);
                points.Add(new Vector3(centre.x + Mathf.Cos(a) * width * 0.5f,
                                       centre.y,
                                       centre.z + Mathf.Sin(a) * depth * 0.5f));
            }
            return points;
        }

        AirportWalker MakeWalker(GameObject prefab, float speed)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, _liveRoot);
            go.name = prefab.name;
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            AirportKit.SetLayerDeep(go, CrowdLayer);
            var w = new AirportWalker { Speed = speed };
            w.InitAt(go.transform, CrewKit.ForCrowd(_clips, _rng), go.transform.position, Quaternion.identity);
            return w;
        }
    }
}
