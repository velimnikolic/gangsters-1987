using System.Collections.Generic;
using UnityEngine;

namespace AirportDemo
{
    /// <summary>
    /// The ground handling: what meets an aeroplane once the propeller stops. A fuel
    /// bowser is despatched to whoever has just shut down and stands at his wing for
    /// three minutes; the baggage train runs out to the commuter stand when one is on
    /// it and back to the terminal; a follow-me pickup patrols the service road; the
    /// freight lorries come in through the gate, back onto the shed's dock, load and
    /// leave; and the fire truck stands out on its apron whenever anybody is landing.
    ///
    /// None of these is a Synty asset either - no pack has a bowser, a tug or a dolly
    /// - so each is a pack lorry or cart carrying a body baked by AirportKitBash, and
    /// each drives on an AirportDriver over the ramp's service road.
    /// </summary>
    public sealed class GroundOps
    {
        readonly List<AirportDriver> _drivers = new List<AirportDriver>();
        readonly List<AirportDriver> _rampTraffic = new List<AirportDriver>();
        readonly Transform _root;
        readonly System.Random _rng;
        readonly FlightOps _flights;

        AirportDriver _bowser, _baggage, _followMe;
        Aircraft _bowserTarget;
        float _bowserIdle;
        readonly Queue<Aircraft> _fuelQueue = new Queue<Aircraft>();

        /// <summary>The lane the ground vehicles use, along the back of the ramp.</summary>
        readonly float _roadZ;

        public GroundOps(Transform root, System.Random rng, FlightOps flights)
        {
            _root = root;
            _rng = rng;
            _flights = flights;
            _roadZ = AirportSpec.ServiceRoadZ;
            if (_flights != null)
            {
                _flights.OnShutdown += a => { if (!_fuelQueue.Contains(a)) _fuelQueue.Enqueue(a); };
            }
        }

        public IReadOnlyList<AirportDriver> Drivers => _drivers;

        AirportDriver Adopt(AirportDriver d)
        {
            _drivers.Add(d);
            _rampTraffic.Add(d);
            d.Traffic = _rampTraffic;
            return d;
        }

        // ------------------------------------------------------------ building

        /// <summary>The bowser: a flatbed with the baked tank on its deck.</summary>
        public void AddBowser(GameObject lorry, GameObject tank)
        {
            if (lorry == null) return;
            var d = new AirportDriver { Cruise = 9f, Gap = 8f };
            d.Bind(lorry.transform);
            if (tank != null)
            {
                var body = Object.Instantiate(tank, lorry.transform);
                body.name = "Bowser body";
                var lb = AirportKit.BoundsOf(lorry);
                body.transform.localPosition = new Vector3(0f, Mathf.Max(1.1f, lb.size.y * 0.35f), -0.4f);
                body.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            d.Start(ParkRun(AirportSpec.FboX + 30f), wrap: false);
            d.Done = true;
            _bowser = Adopt(d);
        }

        /// <summary>The baggage train: a cart and two dollies, terminal to stand.</summary>
        public void AddBaggageTrain(GameObject tug, GameObject cartPrefab)
        {
            if (tug == null) return;
            var d = new AirportDriver { Cruise = 6f, Gap = 5f, HalfLength = 1.6f };
            d.Bind(tug.transform, 1.6f);
            if (cartPrefab != null)
                for (int i = 0; i < 2; i++)
                {
                    var cart = Object.Instantiate(cartPrefab, _root);
                    cart.name = "Baggage dolly " + (i + 1);
                    d.Tow(cart, 4.2f + i * 3.4f);
                }
            d.Start(ParkRun(AirportSpec.TerminalX - 18f), wrap: false);
            d.Done = true;
            _baggage = Adopt(d);
        }

        /// <summary>The follow-me: a works pickup that runs the service road all day,
        /// which at a field this size is exactly what it does.</summary>
        public void AddFollowMe(GameObject pickup)
        {
            if (pickup == null) return;
            var d = new AirportDriver { Cruise = 11f, Gap = 9f };
            d.Bind(pickup.transform);
            var route = new List<Vector3>
            {
                new Vector3(AirportSpec.ApronX0 + 20f, AirportSpec.PaveY, _roadZ),
                new Vector3(AirportSpec.ApronX1 - 20f, AirportSpec.PaveY, _roadZ),
                new Vector3(AirportSpec.ApronX1 - 10f, AirportSpec.PaveY, _roadZ + 8f),
                new Vector3(AirportSpec.ApronX0 + 10f, AirportSpec.PaveY, _roadZ + 8f),
            };
            d.Start(route, closed: true);
            _followMe = Adopt(d);
        }

        /// <summary>A freight lorry: in at the gate, round to the shed's dock, a while
        /// being worked, out at the gate again - the harbour's lorry round, on tarmac.</summary>
        public void AddFreightLorry(GameObject lorry, int index)
        {
            if (lorry == null) return;
            var d = new AirportDriver { Cruise = 9f, Gap = 10f };
            d.Bind(lorry.transform);
            float gate = AirportSpec.CargoGateX;
            float dockX = AirportSpec.CargoX + (index % 2 == 0 ? -3f : 9f);
            // in off the approach road, down the gate road, through the wire, along
            // the back of the sheds to the dock, and out the way it came
            float back = AirportSpec.BuildingFrontZ + AirportSpec.CargoDepth + 6f;
            var route = new List<Vector3>
            {
                new Vector3(gate - 2.5f, AirportSpec.PaveY, AirportSpec.StreetZ - 12f),
                new Vector3(gate - 2.5f, AirportSpec.PaveY, back),
                new Vector3(dockX + 12f, AirportSpec.PaveY, back),
                new Vector3(dockX, AirportSpec.PaveY, AirportSpec.BuildingFrontZ + AirportSpec.CargoDepth + 3.5f),
                new Vector3(dockX + 12f, AirportSpec.PaveY, back),
                new Vector3(gate + 2.5f, AirportSpec.PaveY, back),
                new Vector3(gate + 2.5f, AirportSpec.PaveY, AirportSpec.StreetZ - 12f),
            };
            d.OnPoint = (drv, leg) => { if (leg == 3) drv.Dwell = (float)(_rng.NextDouble() * 30.0 + 25.0); };
            d.Start(route, closed: false, wrap: true);
            Adopt(d);
        }

        /// <summary>A vehicle stood where it lives when it has nothing to do.</summary>
        static List<Vector3> ParkRun(float x)
        {
            float z = AirportSpec.ServiceRoadZ;
            return new List<Vector3> { new Vector3(x, AirportSpec.PaveY, z), new Vector3(x + 12f, AirportSpec.PaveY, z) };
        }

        // ------------------------------------------------------------ the tick

        public void Tick(float dt)
        {
            for (int i = 0; i < _drivers.Count; i++) _drivers[i].Tick(dt);
            TickBowser(dt);
            TickBaggage(dt);
        }

        /// <summary>Whoever shut down last gets the bowser: out along the service road,
        /// in beside his left wing, three minutes there, back to the FBO.</summary>
        void TickBowser(float dt)
        {
            if (_bowser == null) return;
            if (_bowserTarget != null)
            {
                if (_bowser.Done && _bowser.Dwell <= 0f)
                {
                    // done with him: home
                    _bowser.Divert(HomeRun(_bowser.Position, AirportSpec.FboX + 30f));
                    _bowserTarget = null;
                    _bowserIdle = 12f;
                }
                return;
            }
            _bowserIdle -= dt;
            if (_bowserIdle > 0f || _fuelQueue.Count == 0) return;
            var plane = _fuelQueue.Dequeue();
            if (plane == null || plane.Tf == null) return;
            // beside the port wing, clear of the propeller arc
            var at = plane.Position + plane.Right * -(plane.HalfSpan + 4f) - plane.Forward * 1.5f;
            var route = new List<Vector3>
            {
                new Vector3(_bowser.Position.x, AirportSpec.PaveY, _roadZ),
                new Vector3(at.x, AirportSpec.PaveY, _roadZ),
                new Vector3(at.x, AirportSpec.PaveY, at.z),
            };
            _bowser.Divert(route);
            _bowser.OnArrive = d => d.Dwell = 150f;
            _bowserTarget = plane;
        }

        static List<Vector3> HomeRun(Vector3 from, float homeX)
        {
            float z = AirportSpec.ServiceRoadZ;
            return new List<Vector3>
            {
                new Vector3(from.x, AirportSpec.PaveY, z),
                new Vector3(homeX, AirportSpec.PaveY, z),
            };
        }

        /// <summary>The baggage train runs when a commuter is on a stand with its doors
        /// open, and goes home when it is not.</summary>
        void TickBaggage(float dt)
        {
            if (_baggage == null || _flights == null) return;
            if (!_baggage.Done || _baggage.Dwell > 0f) return;
            Aircraft onStand = null;
            foreach (var a in _flights.Fleet)
                if (a.Commuter && a.State == Aircraft.Phase.Shutdown && a.Timer < 900f) { onStand = a; break; }

            float homeX = AirportSpec.TerminalX - 18f;
            if (onStand != null && Mathf.Abs(_baggage.Position.z - AirportSpec.ServiceRoadZ) < 6f)
            {
                var at = onStand.Position + onStand.Right * (onStand.HalfSpan * 0.8f) + onStand.Forward * (onStand.Tail + 4f);
                _baggage.Divert(new List<Vector3>
                {
                    new Vector3(_baggage.Position.x, AirportSpec.PaveY, _roadZ),
                    new Vector3(at.x + 6f, AirportSpec.PaveY, _roadZ),
                    new Vector3(at.x, AirportSpec.PaveY, at.z),
                });
                _baggage.OnArrive = d => d.Dwell = 45f;
            }
            else if (Mathf.Abs(_baggage.Position.z - AirportSpec.ServiceRoadZ) > 6f)
            {
                _baggage.Divert(HomeRun(_baggage.Position, homeX));
                _baggage.OnArrive = d => d.Dwell = 20f;
            }
        }

        public void Dispose() { }
    }
}
