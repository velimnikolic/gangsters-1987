using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The wayside: what stands on the road OUT of town. Between the city's last
    // junction and each district's gate the connecting street crosses a long reach of
    // wild ground, and halfway along it - where such a road always has one - stands a
    // filling station.
    //
    // It used to be a pile of props: the Town cluster stood on a rectangle of asphalt,
    // with the pack's own pickup parked at a pump and the shop door shut. Nothing on it
    // could be used and nothing on it moved. It is now the SAME forecourt the pump bench
    // runs (FuelStation for the plan, ForecourtSet for the ground), which means it comes
    // with bays, two crossovers through the pavement, painted lines and arrows, a
    // parking row, a gas cage, a tanker at the back - and cars that pull in off the
    // road, fill up, and drive on (FuelCustomer).
    //
    // The order matters and is the reason this file is in two halves. WHERE the station
    // goes has to be settled BEFORE the connecting street is laid, because the street
    // has to leave two gaps in its pavement for the forecourt's mouths; what stands
    // there can only be built AFTER the lane graph is welded, because a customer needs a
    // lane to arrive on.
    public partial class RoadDemoBuilder
    {
        Transform _waysideRoot;
        readonly List<FuelStation> _waysideStations = new List<FuelStation>();
        readonly List<FuelCustomer> _fuelCustomers = new List<FuelCustomer>();

        /// <summary>How many cars want petrol at each wayside station. A wayside station
        /// is not a bench: two or three is a forecourt in use, and the road out to a
        /// quarter carries nothing like the traffic that would fill more.
        ///
        /// It is a field and not a constant so a run can be told to build the forecourts
        /// and put nobody on them, which is the only way to read what the customers
        /// themselves cost the traffic (--sets RoadDemoBuilder.waysideCustomers=0).</summary>
        [Header("The wayside")]
        [Tooltip("Cars per filling station that want petrol. Each one cycles: drives, " +
                 "books a bay, turns in, is filled, pays, and drives on. Zero builds the " +
                 "forecourts and leaves them empty.")]
        [Range(0, 6)] public int waysideCustomers = 3;

        /// <summary>How far up and down the lane the customer's kerb slots sit - the
        /// spot he pulls up at coming in, and the one he rejoins the traffic at.</summary>
        const float KerbRun = 24f;

        /// <summary>The clear frontage a station needs on the connector: the apron's own
        /// width and a margin, either side of the anchor.</summary>
        const float Frontage = FuelStation.ApronHalfX;
        const float FrontageRoom = Frontage + 6f;

        /// <summary>The two edges the forecourt is cut to, in the STATION'S own frame:
        /// the pavement's outer edge, where the apron stops, and the carriageway's,
        /// where the crossovers stop. The anchor stands SetBack behind the first.</summary>
        const float FrontZ = FuelStation.SetBack;
        const float KerbZ = FuelStation.SetBack + StreetKit.OuterHalf - StreetKit.StreetHalf;

        /// <summary>Where a filling station is going to stand on a connecting road, and
        /// which way round. Settled before the street is laid; built after it is welded.</summary>
        sealed class WaysidePlan
        {
            public Vector3 Anchor;      // the canopy's centre
            public Vector3 Mid;         // the point on the road's centre line it fronts
            public Quaternion Rot;      // its front (+Z) turned to the road
            public float U;             // where along the connector's own axis it stands
            public bool AlongZ;         // whether that axis is Z
            public bool SidePositive;   // whether the forecourt is on the +X / +Z side
            public Rect Ground;
            public int Seed;
        }

        // ---------------------------------------------------------------- the placing

        /// <summary>Settle where a filling station goes on the road between the city's
        /// edge junction (<paramref name="face"/>) and the district's gate
        /// (<paramref name="portal"/>), or answer null when the road cannot hold one.
        ///
        /// A forecourt needs thirty metres of straight frontage with a pavement it may
        /// break, so it wants clear road either side of it: not just clear of a river or
        /// the freeway's run (RunsIntoSeam), which is what the first version checked,
        /// but clear of the belt's own pad and of the crossroads where the freeway's
        /// link road crosses this street. Both of those are laid by other passes, and a
        /// station that landed on one would have its two crossovers cut out of a
        /// junction.</summary>
        WaysidePlan PlanWayside(Vector3 face, Vector3 portal, DistrictSlot slot)
        {
            var along = portal - face;
            along.y = 0f;
            float len = along.magnitude;
            if (len < 90f) return null;
            var dir = along / len;
            bool alongZ = Mathf.Abs(dir.z) > 0.5f;

            // which shoulder, out of the district's own seed - the same stream the first
            // version rolled, so a seed that put the station on the left still does
            var rng = new System.Random(slot.seed * 613 + 29);
            float first = rng.Next(2) == 0 ? 1f : -1f;

            // a fixed way out of town rather than halfway: clear of the city's last
            // corner AND of the freeway's link road, which crosses the strip further
            // out. When the first stretch is spoken for the station walks on down the
            // road rather than giving up on it.
            foreach (float run in new[] { 62f, 88f, 114f, 40f })
            {
                if (run < FrontageRoom + 10f || run > len - FrontageRoom - 10f) continue;
                var mid = face + dir * run;
                float u = alongZ ? mid.z : mid.x;
                if (!ClearOfJunctions(slot.edge, alongZ, u)) continue;

                for (int turn = 0; turn < 2; turn++)
                {
                    var side = new Vector3(-dir.z, 0f, dir.x) * (turn == 0 ? first : -first);
                    var ground = ForecourtGround(mid, dir, side);
                    if (RunsIntoSeam(ground)) continue;

                    var anchor = mid + side * (StreetKit.OuterHalf + FuelStation.SetBack);
                    // AT ROAD LEVEL, not at the pavement's. A filling station's frontage
                    // is a dropped kerb the whole way across - that is what lets a car
                    // turn in at all - so the forecourt, the canopy and the man who got
                    // out of the car all stand on the carriageway's own plane, and the
                    // step up to the footway is the pavement's own 7 cm.
                    anchor.y = _connectorKit != null ? _connectorKit.Surface : 0f;
                    return new WaysidePlan
                    {
                        Anchor = anchor,
                        Mid = mid,
                        Rot = Quaternion.Euler(0f, SuburbDemo.TownKit.YawToFace(
                                  SuburbDemo.TownKit.Side.PlusZ, -side), 0f),
                        U = u,
                        AlongZ = alongZ,
                        SidePositive = (alongZ ? side.x : side.z) > 0f,
                        Ground = ground,
                        Seed = slot.seed,
                    };
                }
            }
            return null;
        }

        /// <summary>Is this stretch of the connector clear of the two junctions other
        /// passes lay on it - the belt freeway's pad and the freeway link's crossroads?</summary>
        bool ClearOfJunctions(CityEdge edge, bool alongZ, float u)
        {
            if (BeltOn && _beltU.TryGetValue(edge, out float beltU)
                && Mathf.Abs(u - beltU) < BeltPadHalf + FrontageRoom) return false;
            if (_highwayEnds.TryGetValue(edge, out var end) && end.Vertical == alongZ
                && Mathf.Abs(u - end.LinkU) < StreetKit.StreetHalf + StreetKit.Cell + FrontageRoom)
                return false;
            return true;
        }

        /// <summary>The ground the forecourt covers, world-axis-aligned: the apron's own
        /// width along the road and its depth back off it, with a skirt for the tree
        /// line. It is measured off the SAME figures the apron is cut to, so the levelled
        /// ground and the asphalt on it cannot disagree.</summary>
        static Rect ForecourtGround(Vector3 mid, Vector3 dir, Vector3 side)
        {
            // measured OUT FROM THE ROAD'S CENTRE, which is what mid stands on: the
            // pavement, then the apron back to the deepest the cluster reaches (about
            // twenty metres behind the canopy), then the tree line five and a half
            // metres behind that again. Cut to the apron alone it left the back row of
            // trees standing on unlevelled ground with the island's own flora in them.
            const float Deep = StreetKit.OuterHalf + FuelStation.SetBack + 28f;
            const float Wide = Frontage + 5f;    // the apron, and the trees down its sides
            var a = mid + side * StreetKit.StreetHalf - dir * Wide;
            var b = mid + side * Deep + dir * Wide;
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z),
                                   Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
        }

        // ---------------------------------------------------------------- the frontage

        /// <summary>Lay a stretch of a connecting street, leaving the forecourt's two
        /// crossovers out of the pavement where one stands on it.
        ///
        /// The footway carries on ACROSS the frontage and is broken only at the two
        /// mouths, which is what a filling station's frontage looks like and what stops a
        /// car turning in over twenty metres of pavement. Nothing is dressed along there
        /// either: a lamp or a bin in the middle of a mouth is a lamp a car drives
        /// through.</summary>
        void LayStreet(bool vertical, float centre, float from, float to, WaysidePlan pump)
        {
            if (to - from < 1f) return;
            if (pump == null || pump.AlongZ != vertical
                || pump.U - Frontage > to || pump.U + Frontage < from)
            {
                if (vertical) _connectorKit.LayAlongZ(centre, from, to);
                else _connectorKit.LayAlongX(centre, from, to);
                return;
            }

            float mIn = FuelStation.MouthX - FuelStation.MouthHalf;    // the near edge of a mouth
            float mOut = FuelStation.MouthX + FuelStation.MouthHalf;   // the far one
            float f0 = Mathf.Max(from, pump.U - Frontage), f1 = Mathf.Min(to, pump.U + Frontage);

            Piece(from, f0, true, true);
            Piece(f0, pump.U - mOut, true, false);
            Piece(pump.U - mOut, pump.U - mIn, false, false);   // the crossover in
            Piece(pump.U - mIn, pump.U + mIn, true, false);     // the island between them
            Piece(pump.U + mIn, pump.U + mOut, false, false);   // the crossover out
            Piece(pump.U + mOut, f1, true, false);
            Piece(f1, to, true, true);

            void Piece(float a, float b, bool walk, bool dress)
            {
                if (b - a < 0.5f) return;
                // the far side of the street always keeps its pavement; only the side
                // the forecourt stands on is broken
                bool near = pump.SidePositive;
                if (vertical)
                    _connectorKit.LayAlongZ(centre, a, b, a, b, near || walk, !near || walk, dress);
                else
                    _connectorKit.LayAlongX(centre, a, b, a, b, near || walk, !near || walk, dress);
            }
        }

        // ---------------------------------------------------------------- the building

        /// <summary>Stand the whole forecourt on the ground the plan picked, wire it to
        /// the lane outside, and put cars on that lane that want petrol.</summary>
        void StandWayside(WaysidePlan plan)
        {
            if (plan == null) return;

            // the ground: flat at the city's own level and growing nothing through the
            // asphalt. The SURFACE is the forecourt's own (ForecourtSet.LayApron), cut
            // to the station's frame rather than to this world-aligned rectangle.
            var skirt = Rect.MinMaxRect(plan.Ground.xMin - 4f, plan.Ground.yMin - 4f,
                                        plan.Ground.xMax + 4f, plan.Ground.yMax + 4f);
            _reservations.Level(skirt, RoadBed);
            _reservations.NoFlora(plan.Ground);

            if (_waysideRoot == null) _waysideRoot = ((IDistrictHost)this).StaticRoot("Wayside");
            var root = new GameObject("Filling Station").transform;
            root.SetParent(_waysideRoot, false);

            var station = FuelStation.Stand(root, plan.Anchor, plan.Rot, plan.Anchor.y,
                                            ForecourtSet.CrossZ(FrontZ, KerbZ));
            ForecourtSet.LayApron(station, root, WaysideAsphalt, FrontZ, KerbZ);
            ForecourtSet.Paint(station, root, WaysideWhite, WaysideBlue);
            station.Dress(root, new System.Random(plan.Seed * 613 + 71));
            ForecourtSet.StandTheStill(station, root, Tanker(), _carPrefabs,
                                       new System.Random(plan.Seed * 613 + 97));

            _waysideStations.Add(station);
            if (!WireWayside(station, plan)) return;
            SendCustomers(station, plan);
        }

        /// <summary>Which lane the station stands on, and the two kerb spots either side
        /// of the forecourt where a customer leaves it and comes back to it.
        ///
        /// The lane is the one pointing the way the forecourt is DRIVEN - along the
        /// station's own local +X, which every bay points down. That is not a choice: a
        /// yaw turning the station's +Z to face the road puts its +X along the side of
        /// the road the forecourt is on, so the lane matching it is by construction the
        /// near one, and a customer on it never crosses the oncoming traffic to turn
        /// in.</summary>
        bool WireWayside(FuelStation station, WaysidePlan plan)
        {
            if (Net == null) return false;
            var want = station.Way(1f, 0f);
            RoadEdge best = null;
            float bestOff = 9f;
            foreach (var road in Net.Roads)
                foreach (var lane in road.Lanes)
                {
                    if (Vector3.Dot(lane.Dir, want) < 0.9f) continue;
                    float at = Vector3.Dot(plan.Mid - lane.Start, lane.Dir);
                    if (at < KerbRun + 6f || at > lane.Length - KerbRun - 6f) continue;
                    var off = plan.Mid - (lane.Start + lane.Dir * at);
                    off.y = 0f;
                    if (off.magnitude >= bestOff) continue;
                    bestOff = off.magnitude;
                    best = lane;
                }
            if (best == null)
            {
                // the forecourt still stands and still reads; what it has not got is
                // anybody pulling into it
                Debug.LogWarning("[RoadDemo] the wayside station's frontage is on no lane " +
                                 "long enough to pull off - it stands, but nobody stops.");
                return false;
            }

            station.Lane = best;
            float mid = Vector3.Dot(plan.Mid - best.Start, best.Dir);
            station.KerbInS = Mathf.Clamp(mid - KerbRun, 6f, best.Length - 6f);
            station.KerbOutS = Mathf.Clamp(mid + KerbRun, 6f, best.Length - 6f);
            station.KerbIn = best.Start + best.Dir * station.KerbInS;
            station.KerbOut = best.Start + best.Dir * station.KerbOutS;
            station.MapRoads(Net);
            return true;
        }

        /// <summary>Cars on the road that want petrol. They are ordinary traffic with an
        /// errand (FuelCustomer) and they live on the whole lane graph, not on the
        /// connector: between one tankful and the next they drive the city like anything
        /// else, and the route map the station worked out is what steers them back.</summary>
        void SendCustomers(FuelStation station, WaysidePlan plan)
        {
            if (_carPrefabs.Count == 0 || station.Lane == null) return;
            var clips = ((IDistrictHost)this).Clips;
            var crowd = new System.Random(plan.Seed * 613 + 131);
            var root = new GameObject("Customers").transform;
            root.SetParent(_waysideRoot, false);

            for (int i = 0; i < waysideCustomers; i++)
            {
                var prefab = _carPrefabs[crowd.Next(_carPrefabs.Count)];
                var go = Instantiate(prefab, root);
                go.name = prefab.name;
                LivingCity.Gameplay.VehiclePaint.Apply(go, prefab);
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>())
                    if (!CarBody.IsVisualRig(mb)) Destroy(mb);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);

                var body = new CarBody(go.transform);
                var car = new FuelCustomer
                {
                    Tf = go.transform, Body = body, HalfLen = body.TrafficHalfLength,
                    HalfWide = body.TrafficHalfWidth, AxleBack = body.AxleBack,
                    RoadY = station.GroundY, Net = Net, Tag = "traffic",
                    Plate = $"pump {plan.Seed % 1000}-{i + 1}",
                };

                // the same man twice: sat at the wheel while the car drives, on his feet
                // the moment it stops at the pump. A driver who got out looking like
                // somebody else would be the one thing on a forecourt nobody believes.
                if (_pedPrefabs.Count > 0)
                {
                    var face = _pedPrefabs[crowd.Next(_pedPrefabs.Count)];
                    car.Seated = CarOccupant.Seat(go.transform, face, _sitLoopClip,
                                                  body.SeatLocalPoint(0), layer: CrowdLayer);
                    car.Driver = MakeFuelDriver(face, clips, crowd, root, station.ShopStep);
                }

                // spread down the lane and never on top of anybody: the connector's
                // traffic was laid first on its own beat, and a customer dropped at a
                // figure of its own lands on one about one time in three
                float want = 14f + i * (station.Lane.Length - 30f) / waysideCustomers;
                car.Spawn(station.Lane, FreeStretch(station.Lane, want));
                car.SetStation(station, 6f + (float)crowd.NextDouble() * 90f);
                // among the road's users, so every other driver and everybody on foot
                // plans round it - but NOT in _vehicles. That list is driven a frame at
                // a time in Update, and TickErrand drives the car itself on the legs
                // where it is driving at all: in both lists it would be stepped twice a
                // frame, at double speed, and thinned off the map mid-errand with a bay
                // still booked in its name (StreetTraffic.Thin).
                StreetTraffic.Users.Add(car);
                _fuelCustomers.Add(car);
            }
        }

        /// <summary>The man who will get out of the car, made and put away. He is stood
        /// AT THE STATION rather than at the world's origin: he is hidden the moment he
        /// is made and StepOut puts him at his own car door when the time comes, but a
        /// walker parked at nought is a walker registered in the middle of the city
        /// (PedestrianAgent.Everyone), where every crossing car and every crowd query
        /// has to step over him.</summary>
        FuelDriver MakeFuelDriver(GameObject prefab, PedClips clips, System.Random rng,
                                  Transform root, Vector3 waitAt)
        {
            var go = Instantiate(prefab, root);
            go.name = prefab.name + " (driver)";
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>())
                    if (!CarBody.IsVisualRig(mb)) Destroy(mb);
            foreach (var animator in go.GetComponentsInChildren<Animator>())
                animator.runtimeAnimatorController = null;
            SetLayerDeep(go, CrowdLayer);
            var driver = new FuelDriver { Speed = 1.25f + (float)rng.NextDouble() * 0.3f, Tag = "driver" };
            driver.InitAt(go.transform, CrewKit.ForCrowd(clips, rng), waitAt, Quaternion.identity);
            driver.Show(false);
            return driver;
        }

        /// <summary>The nearest progress along this lane, at or after the one wanted,
        /// with nothing standing within a body and a half of it.</summary>
        static float FreeStretch(RoadEdge lane, float want)
        {
            float lo = 8f, hi = Mathf.Max(lo, lane.Length - 12f);
            for (int step = 0; step < 24; step++)
            {
                float at = Mathf.Clamp(want + step * 13f, lo, hi);
                var point = lane.Start + lane.Dir * at;
                bool clear = true;
                var users = StreetTraffic.Users;
                for (int i = 0; i < users.Count && clear; i++)
                {
                    var d = users[i].RoadPosition - point;
                    d.y = 0f;
                    if (d.sqrMagnitude < 121f) clear = false;
                }
                if (clear) return at;
                if (at >= hi) break;
            }
            return Mathf.Clamp(want, lo, hi);
        }

        // ------------------------------------------------------------------- the watch

        /// <summary>Every forecourt's customers, one line each, on the same slow clock
        /// the rest of the watch runs on. It is the shape the bench prints
        /// (PumpDemo.TickAudit) because it is read by the same eyes looking for the same
        /// thing: a bay booked by a car that never arrives.
        ///
        /// It is off unless the profile is on. A city with six quarters has six
        /// forecourts, and six lines every fifteen seconds in a run nobody is auditing
        /// is six lines of noise over whatever the run was actually printed for.</summary>
        void TickWaysideWatch(float dt)
        {
            if (!updateProfile || _waysideStations.Count == 0) return;
            _waysideWatch -= dt;
            if (_waysideWatch > 0f) return;
            _waysideWatch = WaysideWatchEvery;

            for (int s = 0; s < _waysideStations.Count; s++)
            {
                var station = _waysideStations[s];
                int busy = 0;
                for (int i = 0; i < station.Bays.Length; i++) if (station.Taken(i)) busy++;
                var line = new System.Text.StringBuilder();
                line.Append($"[Wayside] station {s + 1}: {busy}/{station.Bays.Length} bays");
                for (int i = 0; i < _fuelCustomers.Count; i++)
                    if (_fuelCustomers[i].Station == station)
                        line.Append($" | {_fuelCustomers[i].Plate}: {_fuelCustomers[i].Doing}");
                Debug.Log(line.ToString());
            }
        }

        const float WaysideWatchEvery = 15f;
        float _waysideWatch = WaysideWatchEvery;

        /// <summary>The men who got out of the customers' cars are walkers of their own -
        /// they are not in the city's crowd, so nothing else lets go of them.</summary>
        void DisposeWayside()
        {
            for (int i = 0; i < _fuelCustomers.Count; i++)
            {
                StreetTraffic.Users.Remove(_fuelCustomers[i]);
                _fuelCustomers[i].Driver?.Dispose();
            }
        }

        // ------------------------------------------------------------------ the pieces

        Material _waysideAsphalt, _waysideWhite, _waysideBlue;
        GameObject _tanker;
        bool _tankerLooked;

        // one set of materials for every station on the map: four per forecourt was four
        // more draw setups a quarter for surfaces that are the same surface
        Material WaysideAsphalt => _waysideAsphalt != null
            ? _waysideAsphalt : _waysideAsphalt = ForecourtSet.Asphalt();
        Material WaysideWhite => _waysideWhite != null
            ? _waysideWhite : _waysideWhite = ForecourtSet.WhitePaint();
        Material WaysideBlue => _waysideBlue != null
            ? _waysideBlue : _waysideBlue = ForecourtSet.BluePaint();

        GameObject Tanker()
        {
            if (_tankerLooked) return _tanker;
            _tankerLooked = true;
            foreach (var path in new[]
            {
                "Assets/Synty/PolygonTown/Prefabs/Vehicles/SM_Veh_Truck_Delivery_01.prefab",
                "Assets/Synty/PolygonTown/Prefabs/Vehicles/SM_Veh_Truck_01.prefab",
            })
            {
                _tanker = RoadDemo.DemoAssetLoad.Load<GameObject>(path);
                if (_tanker != null) break;
            }
            return _tanker;
        }

        /// <summary>Whether the rectangle strays into the run of a seam that goes on
        /// past the grid - the freeway's corridor, a river's channel out to the sea.</summary>
        bool RunsIntoSeam(Rect r)
        {
            if (seams == null) return false;
            foreach (var s in seams)
            {
                if (s == null || (s.kind != SeamKind.Highway && s.kind != SeamKind.River)) continue;
                var span = SeamSpan(s);
                float lo = span.lo - 16f, hi = span.hi + 16f;
                if (s.vertical ? (r.xMax > lo && r.xMin < hi) : (r.yMax > lo && r.yMin < hi)) return true;
            }
            return false;
        }
    }
}
