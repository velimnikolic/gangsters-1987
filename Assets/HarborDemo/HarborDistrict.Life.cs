using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace HarborDemo
{
    // What moves: the shipping and its cargo handlers, a forklift a berth, the dock
    // hands on their rounds, the crew a ship comes with, the delivery truck.
    public partial class HarborDistrict
    {
        readonly List<GameObject> _workerBodies = new List<GameObject>();
        GameObject _captainBody, _palletPrefab, _cratePrefab;

        void BuildShipping()
        {
            var ships = HarborKit.LoadAll(HarborKit.CargoShips, quiet: true);
            var coaster = HarborKit.TryLoad(HarborKit.Coaster);
            if (ships.Count == 0)
                Debug.LogWarning("[HarborDemo] no ship prefabs under Assets/CityKit/Ships - run Tools/City/Catalog/Rebuild Harbor Ships (Kit-Bash).");
            var boats = HarborKit.LoadAll(HarborKit.SmallBoats, quiet: true);
            var ripple = HarborKit.TryLoad(HarborKit.FxRipple);
            var smoke = HarborKit.TryLoad(HarborKit.FxSmoke);
            var plank = HarborKit.TryLoad(HarborKit.WalkwaySingle);

            _shipping = new HarborShipping(this, _liveRoot, new System.Random(seed * 7 + 1), ships, coaster, boats, ripple, smoke, plank);
            _shipping.Launched = ship => { StowTheShip(ship); CrewTheShip(ship); };
            for (int i = 0; i < berths; i++)
            {
                HarborCargo cargo = null;
                if (_boxPrefabs.Count > 0 && _liveSlots.TryGetValue(i, out var slots))
                {
                    cargo = new HarborCargo(_liveRoot, slots, _boxPrefabs, new System.Random(seed * 13 + i));
                    cargo.Frame = Placed;
                    cargo.SeedQuay(_rng.Next(2, 6));
                    if (quayCranes)
                    {
                        var crane = HarborCrane.Build(_liveRoot, _quayRoot, BerthX(i), TileTop, i + 1);
                        if (crane != null) { crane.Frame = Placed; cargo.Hook = crane; _cranes.Add(crane); }
                    }
                }
                _shipping.AddBerth(BerthX(i), cargo);
            }
        }

        // ------------------------------------------------------------ forklifts

        void BuildForklifts()
        {
            if (!forklifts) return;
            var prefab = HarborKit.TryLoad(HarborKit.Forklift);
            _palletPrefab = HarborKit.TryLoad(HarborKit.Pallet);
            _cratePrefab = HarborKit.TryLoad(HarborKit.Crate2);
            if (prefab == null) return;
            for (int i = 0; i < berths; i++)
            {
                float xb = BerthX(i), ax = AisleX(i);
                var route = new List<Vector3>
                {
                    new Vector3(xb + 8f, TileTop, QuayLaneZ),           // the pile
                    new Vector3(ax, TileTop, QuayLaneZ),
                    new Vector3(ax, TileTop, YardRoadZ0 + 2.5f),
                    new Vector3(NearestDoor(ax).x - 6.5f, TileTop, YardRoadZ0 + 2.5f),
                    new Vector3(NearestDoor(ax).x - 6.5f, TileTop, NearestDoor(ax).z - 1f),   // the door, west of the shed's own lift and the lorry
                };
                var go = Instantiate(prefab, route[0], Quaternion.Euler(0f, 90f, 0f), _liveRoot);
                go.name = "Forklift";
                HarborKit.StripBehaviours(go, keepAnimator: false);
                var lift = new HarborForklift { Route = WorldPoints(route) };
                var berth = _shipping != null && i < _shipping.Berths.Count ? _shipping.Berths[i] : null;
                lift.Working = () => berth != null && berth.Working;
                if (_palletPrefab != null)
                {
                    // the load: a pallet with a crate on it, riding the forks
                    var load = new GameObject("Load");
                    var pallet = Instantiate(_palletPrefab, load.transform);
                    pallet.transform.localPosition = new Vector3(0f, 0.05f, 0.9f);
                    if (_cratePrefab != null)
                    {
                        var crate = Instantiate(_cratePrefab, load.transform);
                        crate.transform.localPosition = new Vector3(0f, 0.2f, 0.9f);
                    }
                    lift.Payload = load;
                    // the pile at the door it builds up, four pallets then cleared
                    var pile = new GameObject("DoorPile").transform;
                    pile.SetParent(_yardRoot, false);
                    var pb = HarborKit.PrefabBounds(_palletPrefab);
                    for (int k = 0; k < 4; k++)
                    {
                        var p = Instantiate(_palletPrefab, new Vector3(route[4].x - 2.4f, TileTop + k * pb.size.y, route[4].z + 1.3f), Quaternion.Euler(0f, k * 5f, 0f), pile);
                        p.SetActive(false);
                    }
                    lift.PileGrows = pile;
                }
                lift.Bind(go.transform);
                _forklifts.Add(lift);
            }
        }

        /// <summary>The shed door nearest a place on the yard road - and where it is,
        /// not where the shed line runs: a shed set back off the line has its door back
        /// off it too, and the forklifts and the men are sent to the door.</summary>
        Vector3 NearestDoor(float x)
        {
            var best = new Vector3(x, TileTop, ShedFrontZ - 1.5f);
            float bestD = float.MaxValue;
            foreach (var d in _shedDoors)
            {
                float dd = Mathf.Abs(d.x - x);
                if (dd < bestD) { bestD = dd; best = d; }
            }
            return best;
        }

        // ------------------------------------------------------------ the men

        void LoadBodies()
        {
            if (_workerBodies.Count > 0) return;
            _workerBodies.AddRange(HarborKit.LoadAll(HarborKit.Workers, quiet: true));
            _captainBody = HarborKit.TryLoad(HarborKit.SeaCaptain);
            if (_workerBodies.Count == 0)
                Debug.LogWarning("[HarborDemo] no worker bodies found in the Synty character folders.");
        }

        HarborWorker Man(GameObject prefab, Transform parent, Vector3 at, float pace, List<Vector3> points, Transform frame, bool isStatic = false)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, at, Quaternion.identity, parent);
            go.name = "Man";
            HarborKit.StripBehaviours(go);
            var man = new HarborWorker { Speed = pace, Points = points, Frame = frame, Static = isStatic };
            man.InitAt(go.transform, _clips, at, Quaternion.Euler(0f, HarborKit.Range(_rng, 0f, 360f), 0f));
            man.Begin();
            _workers.Add(man);
            return man;
        }

        /// <summary>The dock hands: a round per berth between the gangway's foot, the
        /// pallets, the aisle and the shed door, with the smoke by the drum, and a
        /// couple who wander the whole yard.</summary>
        void BuildWorkers()
        {
            LoadBodies();
            if (dockWorkers <= 0 || _workerBodies.Count == 0) return;
            float y = TileTop;
            int perBerth = Mathf.Max(1, dockWorkers / Mathf.Max(1, berths));
            for (int i = 0; i < berths; i++)
            {
                float xb = BerthX(i), ax = AisleX(i);
                var round = new List<Vector3>
                {
                    new Vector3(xb - 20f, y, 3.2f),                     // by the gangway
                    new Vector3(xb + 6f, y, QuayLaneZ + 5.5f),          // the pallets
                    new Vector3(ax - 3f, y, BlockZ0 + 8f),              // up the aisle through the blocks
                    NearestDoor(ax) + new Vector3(-11f, 0f, 0.3f),      // the door, clear of the pile and the lorry
                    new Vector3(xb - 29f, y, QuayLaneZ + 3.2f),         // the drum
                    new Vector3(xb - 8f, y, 3f),                        // the coping
                };
                for (int k = 0; k < perBerth; k++)
                {
                    var pts = new List<Vector3>(round);
                    // each man his own order and his own detour, so they do not walk in file
                    for (int s = 0; s < pts.Count; s++)
                    {
                        int r = _rng.Next(pts.Count);
                        (pts[s], pts[r]) = (pts[r], pts[s]);
                        pts[s] += new Vector3(HarborKit.Range(_rng, -1.6f, 1.6f), 0f, HarborKit.Range(_rng, -1.2f, 1.2f));
                    }
                    Man(HarborKit.Pick(_rng, _workerBodies), _liveRoot, pts[0], HarborKit.Range(_rng, 1.15f, 1.6f), WorldPoints(pts), null);
                }
            }
            // the wanderers: the length of the yard road and the quay
            for (int k = 0; k < 2; k++)
            {
                var pts = new List<Vector3>();
                for (int i = 0; i < berths; i++)
                {
                    pts.Add(new Vector3(BerthX(i) + HarborKit.Range(_rng, -20f, 20f), y, YardRoadZ0 - 0.8f));
                    pts.Add(new Vector3(BerthX(i) + HarborKit.Range(_rng, -30f, 30f), y, 4f + k * 2f));
                }
                Man(HarborKit.Pick(_rng, _workerBodies), _liveRoot, pts[0], 1.3f, WorldPoints(pts), null);
            }
        }

        /// <summary>Every freighter comes laden: her deck stacked with through cargo -
        /// boxes bound for somewhere else that ride in and out with her - from the
        /// first slot up, leaving the top of the stack for what this berth works
        /// (HarborCargo.Begin lays that above <see cref="HarborShip.DeckBase"/>). Half
        /// to three quarters of the deck, never so much that the handler has nothing
        /// to put on top. Children of the model, so they ride her wherever she goes.</summary>
        void StowTheShip(HarborShip ship)
        {
            if (ship == null || ship.Spec == null || ship.Model == null || _boxPrefabs.Count == 0) return;
            var slots = ship.Spec.DeckSlots();
            if (slots.Count == 0) return;
            int keepFree = Mathf.Min(9, Mathf.Max(2, slots.Count / 3));
            int most = slots.Count - keepFree;
            int through = Mathf.Clamp(_rng.Next(Mathf.RoundToInt(slots.Count * 0.5f), Mathf.RoundToInt(slots.Count * 0.8f) + 1), 0, most);
            // one shipper's boxes now and then, otherwise the usual mix
            var shipper = _rng.NextDouble() < 0.3 ? HarborKit.Pick(_rng, _boxPrefabs) : null;
            for (int i = 0; i < through; i++)
            {
                var prefab = shipper != null && _rng.NextDouble() < 0.8 ? shipper : HarborKit.Pick(_rng, _boxPrefabs);
                var box = Instantiate(prefab, ship.Model);
                box.name = "Container";
                box.transform.localPosition = slots[i];
                box.transform.localRotation = Quaternion.identity;
            }
            ship.DeckBase = through;
        }

        /// <summary>Every freighter comes with her people: the master out on his wing,
        /// the mate on the other, two hands walking their lanes, more standing at the
        /// rail, one on the stairs off the poop and one aft by the winch. They are put
        /// where they can be seen - inside the stretch of deck edge that carries an
        /// open railing rather than a bulwark (HarborShipSpec.RailZ0/RailZ1), or up on
        /// the wings and the landings - since a man behind a bulwark is a cap and
        /// nothing more. All of them children of the model, so they ride the ship
        /// wherever she goes.</summary>
        void CrewTheShip(HarborShip ship)
        {
            LoadBodies();
            if (ship == null || ship.Spec == null || _workerBodies.Count == 0) return;
            var s = ship.Spec;
            var frame = ship.Model;

            Stand(frame, _captainBody ?? HarborKit.Pick(_rng, _workerBodies), s.BridgeWing(1), 30f);

            float dx = s.DeckWalkX;                    // the lane the two walkers keep to
            float rail = s.Beam * 0.5f - 1.25f;        // at the rail, clear of the pipe run
            float hw = s.HouseWidth * 0.5f;
            int hands = Mathf.Clamp(shipCrew, 0, 8);
            for (int k = 0; k < hands; k++)
            {
                var body = HarborKit.Pick(_rng, _workerBodies);
                switch (k)
                {
                    case 0:
                    case 1:
                    {
                        float x = k == 0 ? -dx : dx;
                        var lane = new List<Vector3>
                        {
                            new Vector3(x, s.DeckY, s.HouseZ1 + 1.6f),
                            new Vector3(x, s.DeckY, s.DeckWalkZFore),
                        };
                        Man(body, frame, frame.TransformPoint(lane[0]), HarborKit.Range(_rng, 1.0f, 1.4f), lane, frame);
                        break;
                    }
                    // the mate on the other wing, and hands at the rail either side
                    case 2: Stand(frame, body, s.BridgeWing(-1), -25f); break;
                    case 3: Stand(frame, body, new Vector3(-rail, s.DeckY, s.GangwayZ), -90f); break;
                    case 4: Stand(frame, body, new Vector3(rail, s.DeckY, s.ForecastleZ - 3.5f), 90f); break;
                    // one on the first landing of the outside stairs, one more at the
                    // rail by the house, one aft on the poop by the mooring winch
                    case 5: Stand(frame, body, new Vector3(-(hw - 1.6f), s.DeckY + HarborShipSpec.Course, s.HouseZ0 - 1.35f), 165f); break;
                    case 6: Stand(frame, body, new Vector3(rail, s.DeckY, s.HouseZ1 + 1.4f), 105f); break;
                    case 7: Stand(frame, body, new Vector3(-2.5f, s.DeckY, s.SternZ + 2.7f), 200f); break;
                }
            }
        }

        /// <summary>A man put at one spot in a ship's own frame, facing where he is told.</summary>
        HarborWorker Stand(Transform frame, GameObject body, Vector3 local, float yaw)
        {
            var man = Man(body, frame, frame.TransformPoint(local), 1f, new List<Vector3> { local }, frame, isStatic: true);
            if (man != null) man.Tf.rotation = frame.rotation * Quaternion.Euler(0f, yaw, 0f);
            return man;
        }

        /// <summary>Men whose bodies went with their ship are let go of.</summary>
        void PruneWorkers()
        {
            for (int i = _workers.Count - 1; i >= 0; i--)
                if (_workers[i].Tf == null) { _workers[i].Dispose(); _workers.RemoveAt(i); }
        }

        // ------------------------------------------------------------ the lorries
        /// <summary>The lorries, and the whole point of the road outside the wire. The
        /// port's roads are a one-way loop - in at the west gate, the yard road
        /// eastbound past the shed doors, out at the east gate; in at the east gate, the
        /// service road westbound past the loading docks, out at the west - so nothing
        /// ever meets anything head on, and a lorry standing at a door stands on the
        /// shoulder while the traffic goes by on the other lane. One comes down the
        /// approach road past the length of the port, turns in through the junction
        /// square (never across a kerb), pulls in at her door or dock, stands there
        /// being worked with her doors open, and goes out through the other gate and
        /// away. Every turn is taken through the bare square at the junction and the
        /// splayed kerb beside it: no lorry on the pavement.
        ///
        /// At each working door the shed's own gang: a forklift that parks inside the
        /// doorway and shuttles pallets between the lorry's tail and the shed while she
        /// stands, and a hand about the forecourt. What each lorry is there for is told
        /// by her goods: half deliver - the pallets inside her go, the stack on the
        /// forecourt grows - and half collect. Which doors are theirs and which belong
        /// to the lorries that never move is FreeBays' rule.</summary>
        void BuildTraffic()
        {
            if (!deliveryTruck || lorries <= 0) return;
            var bodies = HarborKit.LoadAll(HarborKit.Lorries, quiet: true);
            if (bodies.Count == 0) return;
            var pallet = HarborKit.TryLoad(HarborKit.Pallet);
            var freight = HarborKit.LoadAll(HarborKit.Freight, quiet: true);
            var forklift = HarborKit.TryLoad(HarborKit.Forklift);
            LoadBodies();
            var bays = new List<Vector3>(FreeBays());
            float y = TileTop;
            float laneS = _streetZ - 2.5f, laneN = _streetZ + 2.5f;       // eastbound, westbound
            float inZ = _streetZ - 8f;                                    // on the gate road, just off the square - one point per turn, spaced for the lorry's swing
            float half = QuayHalf;
            float gW = _gateWestX, gE = _gateEastX;
            float laneE = YardRoadZ0 + 2.5f;                // the yard road's south lane, eastbound
            float laneW = _serviceRoadZ1 - 2.5f;            // the service road's north lane, westbound
            float dockZ = _serviceRoadZ0 + 1.8f;            // the south shoulder, at the docks

            HarborTruck Lorry(List<Vector3> route, int dockNode, int yardTo, Vector2 stay, Vector2 gap) => new HarborTruck
            {
                Prefabs = bodies, Parent = _liveRoot, Route = WorldPoints(route),
                DockNode = dockNode, YardFrom = 2, YardTo = yardTo,
                DockStay = HarborKit.Range(_rng, stay.x, stay.y), GapRange = gap,
                Pallet = pallet, Freight = freight, People = _workerBodies, SitLoop = _clips.SitLoop,
            };

            int backRuns = _loadingDocks.Count > 0 ? lorries / 3 : 0;
            int n = Mathf.Min(lorries - backRuns, bays.Count);
            for (int k = 0; k < n; k++)
            {
                var bay = bays[k];
                float bx = Mathf.Clamp(bay.x, gW + 24f, gE - 30f);
                var route = new List<Vector3>
                {
                    new Vector3(-half - 130f, y, laneS),
                    new Vector3(gW - 14f, y, laneS),
                    new Vector3(gW - 2.5f, y, inZ),                   // down the gate road's west lane
                    new Vector3(gW - 2.5f, y, _fenceZ - 3f),
                    new Vector3(gW - 2.5f, y, _serviceRoadZ1 + 6f),
                    new Vector3(gW - 2.5f, y, laneE + 6f),            // the corner
                    new Vector3(gW + 6f, y, laneE),                   // left onto the spine
                    new Vector3(bx - 16f, y, laneE),
                    new Vector3(bx - 6f, y, bay.z),                   // in to the shoulder
                    new Vector3(bx, y, bay.z),                        // the door: doors open
                    new Vector3(bx + 10f, y, bay.z),
                    new Vector3(bx + 20f, y, laneE),                  // back out into the lane
                    new Vector3(gE - 6f, y, laneE),
                    new Vector3(gE + 2.5f, y, laneE + 7f),            // left, up the gate road's east lane
                    new Vector3(gE + 2.5f, y, _fenceZ - 3f),
                    new Vector3(gE + 2.5f, y, inZ),
                    new Vector3(gE + 14f, y, laneS),
                    new Vector3(half + 140f, y, laneS),
                };
                var lorry = Lorry(route, 9, 15, new Vector2(24f, 38f), new Vector2(35f, 80f));
                lorry.Delivering = _rng.NextDouble() < 0.5;
                var stand = new Vector3(bx, y, bay.z);
                lorry.Load = BayGoods(stand);
                lorry.Begin(k * 14f + HarborKit.Range(_rng, 0f, 8f));
                _trucks.Add(lorry);
                ShedGang(lorry, NearestDoor(bx - 3f), stand, forklift);
            }

            // and the rounds at the sheds' backs, where the docks are: in at the east
            // gate, west along the service road, out at the west gate
            for (int k = 0; k < backRuns; k++)
            {
                var dock = _loadingDocks[(k * 7 + _rng.Next(_loadingDocks.Count)) % _loadingDocks.Count];
                float dx = Mathf.Clamp(dock.x, gW + 26f, gE - 24f);
                var back = new List<Vector3>
                {
                    new Vector3(half + 130f, y, laneN),
                    new Vector3(gE + 14f, y, laneN),
                    new Vector3(gE - 2.5f, y, inZ),                   // down the gate road's west lane
                    new Vector3(gE - 2.5f, y, _serviceRoadZ1 + 6f),
                    new Vector3(gE - 9f, y, laneW),                   // right onto the service road
                    new Vector3(dx + 14f, y, laneW),
                    new Vector3(dx + 6f, y, dockZ),                   // in to the shoulder
                    new Vector3(dx, y, dockZ),                        // the dock: doors open
                    new Vector3(dx - 10f, y, dockZ),
                    new Vector3(dx - 18f, y, laneW),
                    new Vector3(gW + 8f, y, laneW),
                    new Vector3(gW + 2.5f, y, _fenceZ + 4f),          // right, up the gate road's east lane
                    new Vector3(gW + 2.5f, y, inZ),
                    new Vector3(gW - 14f, y, laneN),
                    new Vector3(-half - 140f, y, laneN),
                };
                var backRun = Lorry(back, 7, 12, new Vector2(22f, 30f), new Vector2(50f, 110f));
                backRun.Delivering = _rng.NextDouble() < 0.5;
                backRun.Begin(12f + k * 30f + HarborKit.Range(_rng, 0f, 10f));
                _trucks.Add(backRun);
            }
        }

        /// <summary>The shed's own gang at a working door: a forklift that lives inside
        /// the doorway, comes out to the lorry's tail while she stands and shuttles a
        /// pallet a trip - laden into the shed if she delivers, out to her if she
        /// collects - and a hand who keeps about the forecourt, the doorway and the
        /// lorry's flank.</summary>
        void ShedGang(HarborTruck lorry, Vector3 door, Vector3 stand, GameObject forklift)
        {
            float y = TileTop;
            if (forklift != null && _palletPrefab != null)
            {
                var route = new List<Vector3>
                {
                    new Vector3(door.x, y, door.z + 4f),                // home: inside the doorway (the door is 1.5 m short of the front)
                    new Vector3(door.x - 1f, y, door.z - 0.3f),         // the forecourt
                    new Vector3(stand.x - 6.5f, y, stand.z),            // the lorry's tail, on the shoulder
                };
                var go = Instantiate(forklift, route[0], Quaternion.Euler(0f, 180f, 0f), _liveRoot);
                go.name = "Forklift";
                HarborKit.StripBehaviours(go, keepAnimator: false);
                var lift = new HarborForklift { Route = WorldPoints(route), LadenOutward = !lorry.Delivering };
                lift.Working = () => lorry.Docked;
                var load = new GameObject("Load");
                Instantiate(_palletPrefab, load.transform).transform.localPosition = new Vector3(0f, 0.05f, 0.9f);
                if (_cratePrefab != null) Instantiate(_cratePrefab, load.transform).transform.localPosition = new Vector3(0f, 0.2f, 0.9f);
                lift.Payload = load;
                lift.Bind(go.transform);
                _forklifts.Add(lift);
            }
            if (_workerBodies.Count > 0)
            {
                var pts = new List<Vector3>
                {
                    new Vector3(door.x + 1.5f, y, door.z + 3f),         // in the doorway
                    new Vector3(stand.x + 2f, y, door.z + 0.2f),        // along the lorry's flank, on the forecourt
                    new Vector3(door.x + 6f, y, door.z + 0.6f),         // the far end of the forecourt
                };
                Man(HarborKit.Pick(_rng, _workerBodies), _liveRoot, pts[0], HarborKit.Range(_rng, 1.1f, 1.4f), WorldPoints(pts), null);
            }
        }

        /// <summary>The goods that stand at a bay while a lorry is worked: four pallets
        /// with a stack apiece, laid along the shed side of the bay and switched on or
        /// off one at a time as the load goes on or comes off. Live things, so they are
        /// kept out of the static batch.</summary>
        List<GameObject> BayGoods(Vector3 bay)
        {
            var load = new List<GameObject>();
            var pallet = HarborKit.TryLoad(HarborKit.Pallet);
            if (pallet == null) return load;
            var freight = HarborKit.LoadAll(HarborKit.Freight, quiet: true);
            var pb = HarborKit.PrefabBounds(pallet);
            for (int j = 0; j < 4; j++)
            {
                var at = new Vector3(bay.x - 2.4f + j * 1.9f, TileTop, bay.z + 3.4f);
                var group = new GameObject("Goods");
                group.transform.SetParent(_liveRoot, false);
                HarborKit.Sit(pallet, at, HarborKit.Range(_rng, -8f, 8f), group.transform, "Pallet");
                if (freight.Count > 0)
                    HarborKit.Sit(HarborKit.Pick(_rng, freight), at + new Vector3(0f, pb.size.y, 0f),
                                  HarborKit.Range(_rng, 0f, 360f), group.transform, "Freight");
                group.SetActive(false);
                load.Add(group);
            }
            return load;
        }
    }
}
