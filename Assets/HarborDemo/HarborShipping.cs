using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    // The port's shipping: at every berth a freighter comes up the coast on that
    // berth's own lane, curves in to a stand-off abreast of her berth, crabs the last
    // dozen metres alongside, lies there being worked, crabs out, curves back to her
    // lane and runs off the other way; and far out, laden freighters that cross the
    // whole coast and never dock. Every ship a HarborShip driven along legs, every
    // berth a small state machine, and no two ever meeting because:
    //   - each berth owns a lane, inner berths inner lanes, and all berth traffic
    //     runs the coast west to east;
    //   - a ship leaves her lane only in her berth's WINDOW of the coast, and only
    //     while no other window overlapping it is in use and no ship under way is
    //     inside it; a ship running the coast holds short of a window in use.
    // Regional berths open occupied at different points of their stay; the long
    // sea approaches must not leave the opening port empty while its fleet arrives.
    public sealed class HarborShipping
    {
        public enum Phase { Gap, Inbound, Approach, Alongside, Depart, Outbound }

        public sealed class Berth
        {
            public int Index;
            public float X, LaneZ, ShipZ, StandoffZ, RunIn, RunOut;
            public Phase Phase = Phase.Gap;
            public float Timer;
            public HarborShip Ship;
            public HarborCargo Cargo;
            public HarborShipSpec Spec;
            public GameObject Gangway;
            public GameObject Mooring;
            /// <summary>A berth that works no boxes takes the coaster: a bulk quay, a
            /// car park or a fishing wall is not where a 72-metre freighter ties up.</summary>
            public bool Small;
            public bool Holding;      // an inbound ship stopped short of somebody's window
            public bool Working => Phase == Phase.Alongside && Ship != null;
            public bool Manoeuvring => Phase == Phase.Approach || Phase == Phase.Depart;
            /// <summary>The coast the current manoeuvre needs to itself.</summary>
            public Vector2 Window => Phase == Phase.Depart
                ? new Vector2(X - WindowMargin, X + RunOut + WindowMargin)
                : new Vector2(X - RunIn - WindowMargin, X + WindowMargin);
            /// <summary>Where the gangway lands on the quay - the men's way aboard.</summary>
            public Vector3 GangwayFoot;
        }

        public const float WindowMargin = 15f;
        // The bulk pier projects 18 m beyond the main quay. Keep the widest hull
        // clear of its outer wall even on the innermost outbound lane.
        public const float LaneOffset = 32f;
        public const float LaneStep = 18f;
        public const float Standoff = 12f;
        public const float ApproachCap = 1.8f;
        public const float CrabCap = 0.5f;
        public const float PasserGapMin = 25f, PasserGapMax = 70f;

        readonly HarborDistrict _b;
        readonly Transform _live;
        readonly System.Random _rng;
        readonly List<GameObject> _shipPrefabs;
        readonly GameObject _coaster, _ripple, _smoke, _plank;
        readonly List<GameObject> _boats;
        readonly float _spawnX;
        readonly HarborSeaRoute _seaRoute;
        readonly List<Berth> _berths = new List<Berth>();
        readonly List<HarborShip> _passers = new List<HarborShip>();
        float _passerTimer;

        public IReadOnlyList<Berth> Berths => _berths;

        public static float PassingLaneZ(int berthCount, bool eastbound) =>
            -(LaneOffset + berthCount * LaneStep + 12f) - (eastbound ? 0f : LaneStep);

        public HarborShipping(HarborDistrict builder, Transform live, System.Random rng,
                              List<GameObject> shipPrefabs, GameObject coaster, List<GameObject> boats,
                              GameObject ripple, GameObject smoke, GameObject plank)
        {
            _b = builder;
            _live = live;
            _rng = rng;
            _shipPrefabs = shipPrefabs;
            _coaster = coaster;
            _boats = boats;
            _ripple = ripple;
            _smoke = smoke;
            _plank = plank;
            _seaRoute = builder.SeaRoute;
            // Coastal extent. A regional SeaRoute adds the entrances through the bay;
            // legacy hosts may still configure a long straight coast with seaRun.
            _spawnX = Mathf.Max(builder.QuayHalf + 240f, builder.seaRun);
            _passerTimer = 1f + (float)rng.NextDouble() * 3f;
        }

        /// <summary>A berth at this x on the quay with this cargo handler. Regional
        /// berths open occupied; the short standalone run keeps its first berth empty.</summary>
        public void AddBerth(float x, HarborCargo cargo, bool small = false)
        {
            var berth = new Berth { Index = _berths.Count, X = x, Cargo = cargo, Small = small };
            berth.LaneZ = -(LaneOffset + berth.Index * LaneStep);
            berth.RunIn = 60f + 1.6f * (Mathf.Abs(berth.LaneZ) - 12f);
            berth.RunOut = berth.RunIn;
            _berths.Add(berth);

            if ((berth.Index == 0 && _seaRoute == null) || _shipPrefabs.Count == 0)
            {
                berth.Phase = Phase.Gap;
                berth.Timer = 3f + (float)_rng.NextDouble() * 4f;
                return;
            }
            // already alongside, part way through her stay
            var spec = ChooseSpec(berth);
            Geometry(berth, spec);
            berth.Ship = Launch(spec, new Vector3(x, HarborDistrict.WaterY, berth.ShipZ), Vector3.right, 0.6f, "Ship " + berth.Index);
            berth.Phase = Phase.Alongside;
            berth.Timer = Stay(berth) * HarborKit.Range(_rng, 0.25f, 0.6f);
            MakeFast(berth, midStay: true);
        }

        /// <summary>Preserve the configured busy/transit ratio over the actual voyage,
        /// including the bends and sea approaches. A fixed scale cap makes large islands'
        /// berths progressively emptier. Each berth still owns one ship and its cargo cycle.</summary>
        float Stay(Berth berth)
        {
            float travel = _seaRoute != null ? _seaRoute.CrossingLength(berth.LaneZ) : _spawnX * 2f;
            float scale = Mathf.Max(1f, travel / Mathf.Max(1f, 2f * (_b.QuayHalf + 240f)));
            return HarborKit.Range(_rng, _b.stayRange.x, _b.stayRange.y) * scale;
        }

        HarborShipSpec ChooseSpec(Berth berth)
        {
            // a berth that works no boxes gets the coaster, and only her: the gantries,
            // the live rows and the stacks are what a freighter is here for
            if (berth.Small && _coaster != null)
            {
                berth.Spec = HarborShipSpec.For(_coaster.name);
                return berth.Spec;
            }
            var prefab = HarborKit.Pick(_rng, _shipPrefabs);
            var spec = HarborShipSpec.For(prefab.name);
            berth.Spec = spec;
            return spec;
        }

        void Geometry(Berth berth, HarborShipSpec spec)
        {
            float halfBeam = spec != null ? spec.Beam * 0.5f : 7.5f;
            berth.ShipZ = -(HarborDistrict.QuayFace + 1.5f + halfBeam);
            berth.StandoffZ = berth.ShipZ - Standoff;
            float gz = spec != null ? spec.GangwayZ : 0f;
            berth.GangwayFoot = new Vector3(berth.X + gz, HarborDistrict.TileTop, 1.2f);
        }

        HarborShip Launch(HarborShipSpec spec, Vector3 at, Vector3 heading, float bob, string name)
        {
            GameObject prefab = null;
            foreach (var p in _shipPrefabs) if (spec != null && p.name == spec.Name) prefab = p;
            if (prefab == null && spec == HarborShipSpec.Coaster) prefab = _coaster;
            if (prefab == null) prefab = HarborKit.Pick(_rng, _shipPrefabs);
            if (prefab == null) return null;
            var ship = HarborShip.Launch(prefab, spec, at, heading, _live, _rng, bob, _ripple, _smoke, name);
            Launched?.Invoke(ship);
            return ship;
        }

        /// <summary>A berth's ship has been made: whoever crews her hooks on here.</summary>
        public System.Action<HarborShip> Launched;

        // ------------------------------------------------------------ the tick

        public void Tick(float dt)
        {
            for (int i = 0; i < _berths.Count; i++) TickBerth(_berths[i], dt);
            for (int i = 0; i < _berths.Count; i++)
            {
                var b = _berths[i];
                b.Ship?.Tick(dt);
                b.Cargo?.Tick(dt);
            }
            TickPassers(dt);
        }

        void TickBerth(Berth b, float dt)
        {
            switch (b.Phase)
            {
                case Phase.Gap:
                    b.Timer -= dt;
                    if (b.Timer > 0f || _shipPrefabs.Count == 0) return;
                    {
                        var spec = ChooseSpec(b);
                        Geometry(b, spec);
                        var coast = new Vector3(-_spawnX, HarborDistrict.WaterY, b.LaneZ);
                        var start = _seaRoute != null ? _seaRoute.Entry(b.LaneZ) : coast;
                        b.Ship = Launch(spec, start, _seaRoute != null ? Vector3.forward : Vector3.right, 1f, "Ship " + b.Index);
                        var path = _seaRoute != null ? new List<HarborShip.Leg>(_seaRoute.Inlet(b.LaneZ, _b.sailSpeed)) : new List<HarborShip.Leg>();
                        path.Add(HarborShip.Leg.Straight(coast, new Vector3(b.X - b.RunIn, HarborDistrict.WaterY, b.LaneZ), _b.sailSpeed));
                        b.Ship.SetPath(path);
                        b.Phase = Phase.Inbound;
                        b.Holding = false;
                        var berth = b;
                        b.Ship.Arrived = () => OnArrived(berth);
                    }
                    return;

                case Phase.Inbound:
                    // Offshore traffic has not entered the coastal manoeuvre windows.
                    // A hold here would otherwise replace the inlet with a diagonal shortcut.
                    if (_seaRoute != null && b.Ship != null && b.Ship.X < -_spawnX - 0.01f) return;
                    // hold short of any window in use ahead; go on when it frees
                    RunTheCoast(b, b.X - b.RunIn, () => WantApproach(b));
                    return;

                case Phase.Approach:
                    return;    // the path does the work; OnArrived moves us on

                case Phase.Alongside:
                    b.Timer -= dt;
                    if (b.Timer > 0f) return;
                    if (b.Cargo != null && b.Cargo.Busy) return;     // the box in the air lands first
                    if (!CanManoeuvre(b, new Vector2(b.X - WindowMargin, b.X + b.RunOut + WindowMargin))) return;
                    CastOff(b);
                    return;

                case Phase.Depart:
                    return;

                case Phase.Outbound:
                    RunTheCoast(b, _spawnX, null);
                    return;
            }
        }

        /// <summary>The straight runs: on the coast every ship watches for a window
        /// in use ahead of her and stops short of it; when it frees she goes on to
        /// where she was going. <paramref name="goal"/> is that x; <paramref name="then"/>
        /// what to try on reaching it (null: the run out, she is retired at its end).</summary>
        void RunTheCoast(Berth b, float goal, System.Func<bool> then)
        {
            var ship = b.Ship;
            if (ship == null) return;
            float x = ship.X;
            float blockAt = float.MaxValue;
            foreach (var o in _berths)
            {
                if (o == b || !o.Manoeuvring || o.Index < b.Index) continue;   // an inner berth's turn never crosses my lane
                var w = o.Window;
                // a window accepted from two metres astern has its hold point six
                // metres astern: she holds where she is rather than being sent backwards
                if (w.x > x - 2f && w.x < goal) blockAt = Mathf.Min(blockAt, Mathf.Max(x, w.x - 6f));
            }
            if (blockAt < float.MaxValue)
            {
                if (!b.Holding)
                {
                    b.Holding = true;
                    ship.SetPath(new[] { HarborShip.Leg.Straight(ship.Position, new Vector3(blockAt, HarborDistrict.WaterY, b.LaneZ), _b.sailSpeed) });
                }
                return;
            }
            if (b.Holding)
            {
                b.Holding = false;
                if (then == null) ship.SetPath(Outbound(ship.Position, b.LaneZ));
                else ship.SetPath(new[] { HarborShip.Leg.Straight(ship.Position, new Vector3(goal, HarborDistrict.WaterY, b.LaneZ), _b.sailSpeed) });
            }
            // near her goal an inbound ship asks early, so a clear window is entered
            // without a stop; stopped at the goal she keeps asking
            if (then != null && !b.Manoeuvring && (ship.Idle || goal - x < 45f))
                then();
        }

        void OnArrived(Berth b)
        {
            switch (b.Phase)
            {
                case Phase.Inbound:
                    if (b.Holding) return;      // stopped short of a window; RunTheCoast sends her on
                    WantApproach(b);           // or she waits at the window's edge, asking each tick
                    break;
                case Phase.Approach:
                    b.Phase = Phase.Alongside;
                    b.Timer = Stay(b);
                    MakeFast(b, midStay: false);
                    break;
                case Phase.Depart:
                    b.Phase = Phase.Outbound;
                    b.Holding = false;
                    b.Ship.SetPath(Outbound(b.Ship.Position, b.LaneZ));
                    b.Ship.Arrived = () => OnArrived(b);
                    break;
                case Phase.Outbound:
                    if (b.Holding) return;
                    b.Ship.Destroy();
                    b.Ship = null;
                    b.Phase = Phase.Gap;
                    b.Timer = HarborKit.Range(_rng, _b.gapRange.x, _b.gapRange.y);
                    break;
            }
        }

        IEnumerable<HarborShip.Leg> Outbound(Vector3 from, float laneZ)
        {
            yield return HarborShip.Leg.Straight(from, new Vector3(_spawnX, HarborDistrict.WaterY, laneZ),
                _b.sailSpeed, _seaRoute != null ? _b.sailSpeed : 0f);
            if (_seaRoute != null)
                foreach (var leg in _seaRoute.Outlet(laneZ, _b.sailSpeed)) yield return leg;
        }

        /// <summary>The turn in: if her window is free the approach and the crab are
        /// laid on ahead of her (extending the run so she never checks her way).</summary>
        bool WantApproach(Berth b)
        {
            if (b.Phase != Phase.Inbound || b.Ship == null) return false;
            var window = new Vector2(b.X - b.RunIn - WindowMargin, b.X + WindowMargin);
            if (!CanManoeuvre(b, window)) return false;
            b.Phase = Phase.Approach;
            float y = HarborDistrict.WaterY;
            var a = new Vector3(b.X - b.RunIn, y, b.LaneZ);
            var s = new Vector3(b.X, y, b.StandoffZ);
            var berthAt = new Vector3(b.X, y, b.ShipZ);
            var curve = HarborShip.Leg.Curve(a, a + Vector3.right * (b.RunIn * 0.45f), s - Vector3.right * (b.RunIn * 0.45f), s, ApproachCap);
            var crab = HarborShip.Leg.Crab(s, berthAt, CrabCap);
            if (b.Ship.Idle)
            {
                // stopped at the window's edge: a short straight to line up, then in
                b.Ship.SetPath(new[] { HarborShip.Leg.Straight(b.Ship.Position, a, ApproachCap, ApproachCap), curve, crab });
            }
            else
            {
                // still running in: the turn is laid on the end of her run, and the
                // speed profile brings her down to the curve's cap on the way
                b.Ship.Extend(curve);
                b.Ship.Extend(crab);
            }
            b.Ship.Arrived = () => OnArrived(b);
            return true;
        }

        void CastOff(Berth b)
        {
            b.Phase = Phase.Depart;
            b.Cargo?.End();
            if (b.Gangway != null) { Object.Destroy(b.Gangway); b.Gangway = null; }
            // her lines go with the gangway: a ship that sails still made fast drags
            // four ropes over the horizon
            if (b.Mooring != null) { Object.Destroy(b.Mooring); b.Mooring = null; }
            if (b.Ship.Bob != null) b.Ship.Bob.TargetScale = 1f;
            float y = HarborDistrict.WaterY;
            var berthAt = new Vector3(b.X, y, b.ShipZ);
            var s = new Vector3(b.X, y, b.StandoffZ);
            var lane = new Vector3(b.X + b.RunOut, y, b.LaneZ);
            b.Ship.SetPath(new[]
            {
                HarborShip.Leg.Crab(berthAt, s, CrabCap),
                HarborShip.Leg.Curve(s, s + Vector3.right * (b.RunOut * 0.45f), lane - Vector3.right * (b.RunOut * 0.45f), lane, ApproachCap, ApproachCap),
            });
            b.Ship.Arrived = () => OnArrived(b);
        }

        void MakeFast(Berth b, bool midStay)
        {
            if (b.Ship.Bob != null) b.Ship.Bob.TargetScale = 0.45f;
            b.Cargo?.Begin(b.Ship, midStay);
            // her lines: head and stern out of her ends, a spring back along her at each,
            // onto whichever bollards the coping carries there (HarborMooring)
            b.Mooring = HarborMooring.Make(b.Ship, b.Spec, _live, b.X, _b.QuayHalf,
                                           HarborDistrict.BollardY, HarborDistrict.BollardZ);
            // the gangway: a plank from the coping up to the deck edge by the house
            if (_plank != null && b.Spec != null)
            {
                float deckY = HarborDistrict.WaterY + b.Spec.DeckY;
                var foot = new Vector3(b.X + b.Spec.GangwayZ, 0.43f, -0.9f);
                var head = new Vector3(b.X + b.Spec.GangwayZ, deckY + 0.05f, b.ShipZ + b.Spec.Beam * 0.5f - 0.3f);
                var pb = HarborKit.PrefabBounds(_plank);
                var go = Object.Instantiate(_plank, _live);
                go.name = "Gangway";
                var d = head - foot;
                go.transform.localRotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                float len = Mathf.Max(0.5f, pb.size.z), wid = Mathf.Max(0.5f, pb.size.x);
                go.transform.localScale = new Vector3(1.2f / wid, 1f, (d.magnitude + 0.6f) / len);
                go.transform.localPosition = (foot + head) * 0.5f - go.transform.localRotation * new Vector3(pb.center.x / wid * 1.2f, pb.max.y, pb.center.z / len * (d.magnitude + 0.6f));
                b.Gangway = go;
            }
        }

        /// <summary>May this berth take the coast in this window: no other berth's
        /// manoeuvre overlaps it, and no ship under way is inside it.</summary>
        bool CanManoeuvre(Berth b, Vector2 window)
        {
            foreach (var o in _berths)
            {
                if (o == b) continue;
                if (o.Manoeuvring)
                {
                    var w = o.Window;
                    if (w.x < window.y && window.x < w.y) return false;
                }
                if (o.Index < b.Index && o.Ship != null && (o.Phase == Phase.Inbound || o.Phase == Phase.Outbound))
                {
                    float x = o.Ship.X;
                    if (x > window.x - o.Ship.Length * 0.5f && x < window.y + o.Ship.Length * 0.5f) return false;
                }
            }
            return true;
        }

        // ------------------------------------------------------------ passers

        void TickPassers(float dt)
        {
            for (int i = _passers.Count - 1; i >= 0; i--)
            {
                var p = _passers[i];
                p.Tick(dt);
                if (p.Idle) { p.Destroy(); _passers.RemoveAt(i); }
            }
            if (!_b.passingTraffic) return;
            _passerTimer -= dt;
            if (_passerTimer > 0f) return;
            // the gap goes with the length of the run: a ship that crosses the whole map
            // is on the water eight minutes, and at the old gap of half a minute there
            // were a dozen freighters abreast of the town at once. Spaced so about three
            // are in sight, which is a working coast and not a convoy.
            const float Abreast = 3f;
            float distance = _seaRoute != null ? _seaRoute.CrossingLength(PassingLaneZ(_berths.Count, true)) : _spawnX * 2f;
            float crossing = distance / Mathf.Max(1f, _b.sailSpeed);
            float gap = Mathf.Clamp(crossing / Abreast, PasserGapMin, 600f);
            _passerTimer = HarborKit.Range(_rng, gap * 0.7f, gap * 1.3f);

            bool eastbound = _rng.Next(2) == 0;
            // outside the channel buoys (z = -66): a freighter is wider than the boats that used to run here
            float laneZ = PassingLaneZ(_berths.Count, eastbound);
            float y = HarborDistrict.WaterY;
            var from = _seaRoute != null ? _seaRoute.Entry(laneZ, eastbound) : new Vector3(eastbound ? -_spawnX : _spawnX, y, laneZ);
            var to = new Vector3(eastbound ? _spawnX : -_spawnX, y, laneZ);
            // freighters only on the coast run - the coaster or one of the big two, laden
            // like everything else that passes a box port (Launched stows and crews her)
            HarborShipSpec spec = _coaster != null && (_shipPrefabs.Count == 0 || _rng.Next(2) == 0)
                ? HarborShipSpec.Coaster
                : HarborShipSpec.For(HarborKit.Pick(_rng, _shipPrefabs)?.name);
            if (spec == null && _coaster == null) return;
            var vessel = Launch(spec, from, _seaRoute != null ? Vector3.forward : (to - from).normalized, 1f, "Passer");
            if (vessel == null) return;
            float knots = spec == HarborShipSpec.Coaster ? 7f : _b.sailSpeed;
            vessel.SetPath(_seaRoute != null ? _seaRoute.Crossing(laneZ, knots, eastbound)
                : new[] { HarborShip.Leg.Straight(from, to, knots, knots) });
            vessel.Passer = true;
            _passers.Add(vessel);
        }

        public void Dispose() { }
    }
}
