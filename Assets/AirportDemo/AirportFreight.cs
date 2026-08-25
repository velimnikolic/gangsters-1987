using System.Collections.Generic;
using UnityEngine;

namespace AirportDemo
{
    /// <summary>
    /// The other freight. A county field with a lit runway, an unmanned tower after
    /// nine and a wire nobody walks is the reason the northbound trade went by air at
    /// all in 1987, and this is that run: a van waits on the verge of the general
    /// aviation gate road with its lights off, comes in through the wire when the field
    /// has gone quiet, stands beside an aeroplane on the tie-down row for as long as it
    /// takes to move what is in it, and leaves east down the approach road. A minute
    /// later the plain sedan that has been sitting where it can see the gate pulls out
    /// after it.
    ///
    /// It is told entirely in MOVEMENT - two vehicles, a route each and a stack of bags
    /// that is on the concrete for ninety seconds. No new subsystem, no dialogue, no
    /// marker: the van going somewhere a van has no business going, and a car following
    /// it, is the whole scene. That is deliberate. The field's job in the game is to be
    /// a PLACE the outfit's business can happen at, and a place earns that by having
    /// something visibly happening at it, not by carrying a card that says it does.
    ///
    /// Everything works in the field's own coordinates, under the Live root, like the
    /// aeroplanes and the ground equipment - the root is carried onto the shore with
    /// the rest, so the run never hears of the frame.
    /// </summary>
    public sealed class AirportFreight
    {
        enum Phase { Waiting, Inbound, Transfer, Outbound }

        /// <summary>Seconds between one run leaving and the next being due. Long on
        /// purpose: a van through the wire every half minute is a delivery service.</summary>
        public float Interval = 300f;
        /// <summary>How long the van stands beside the aeroplane.</summary>
        public float TransferSeconds = 78f;
        /// <summary>How long the law leaves it before pulling out after the van.</summary>
        public float TailDelay = 26f;
        /// <summary>The hours the run will go at all, when there is a clock to ask. With
        /// no clock - the field's own scene has none unless one is added - the interval
        /// is the whole of it.</summary>
        public float NightFrom = 21.5f, NightTo = 4.5f;

        readonly AirportDriver _van = new AirportDriver { Cruise = 11f };
        readonly AirportDriver _tail = new AirportDriver { Cruise = 12f };
        readonly List<GameObject> _bags = new List<GameObject>();
        readonly List<Vector3> _in = new List<Vector3>();
        readonly List<Vector3> _out = new List<Vector3>();
        readonly List<Vector3> _tailOut = new List<Vector3>();
        Vector3 _vanHome, _tailHome;

        RoadDemo.DemoClock _clock;
        Phase _phase = Phase.Waiting;
        float _timer;
        float _tailTimer = -1f;

        /// <summary>Where the transfer happens, in the field's own frame - the head of
        /// the front tie-down row, which is the furthest corner of the ramp from the
        /// terminal, the tower and every light on the field.</summary>
        public static Vector3 TransferPoint =>
            new Vector3(AirportSpec.TieDownX0 + 8f, AirportSpec.PaveY, AirportSpec.TieDownRowZ0 + 3f);

        public bool Running => _phase != Phase.Waiting;

        /// <summary>Stands the run up. The van and the sedan are already-made bodies -
        /// the district builds them the way it builds every other vehicle - and the bags
        /// are switched off until there is a reason for them to be on the concrete.</summary>
        public void Build(GameObject van, GameObject tailCar, IList<GameObject> bags, RoadDemo.DemoClock clock)
        {
            _clock = clock;
            BuildRoutes();

            if (van != null)
            {
                _van.Bind(van.transform);
                _vanHome = _in[0];
                van.transform.SetLocalPositionAndRotation(_vanHome, Quaternion.Euler(0f, 180f, 0f));
                _van.Done = true;
            }
            if (tailCar != null)
            {
                _tail.Bind(tailCar.transform);
                _tailHome = tailCar.transform.localPosition;
                _tailOut.Insert(0, _tailHome);
                _tail.Done = true;
            }
            if (bags != null)
                foreach (var b in bags)
                {
                    if (b == null) continue;
                    b.SetActive(false);
                    _bags.Add(b);
                }

            // the first run is not due the moment the field opens: something has to
            // happen on the ramp in daylight before anything happens on it in the dark
            _timer = Interval * 0.55f;
        }

        void BuildRoutes()
        {
            float y = AirportSpec.PaveY;
            float gate = AirportSpec.GaGateX;
            float ramp = AirportSpec.TieDownX0 - 14f;

            // in: off the verge, down the gate road, through the wire, along the
            // airside service road and down onto the general aviation ramp
            _in.Add(new Vector3(gate - 9f, y, AirportSpec.StreetZ - 22f));
            _in.Add(new Vector3(gate, y, AirportSpec.FenceZ + 9f));
            _in.Add(new Vector3(gate, y, AirportSpec.FenceZ - 7f));
            _in.Add(new Vector3(gate, y, AirportSpec.ServiceRoadZ));
            _in.Add(new Vector3(ramp, y, AirportSpec.ServiceRoadZ));
            _in.Add(new Vector3(ramp, y, AirportSpec.TieDownRowZ0 + 7f));
            _in.Add(TransferPoint);

            // out: back the same way, then east down the approach road and off the map
            for (int i = _in.Count - 1; i >= 1; i--) _out.Add(_in[i]);
            _out.Add(new Vector3(gate, y, AirportSpec.StreetZ - 12f));
            _out.Add(new Vector3(gate + 8f, y, AirportSpec.StreetZ - 2.5f));
            _out.Add(new Vector3(AirportSpec.StreetX1 - 20f, y, AirportSpec.StreetZ - 2.5f));

            // and the sedan, which only ever drives the road - it never goes airside,
            // because a plain car through a wire gate is a thing everybody notices
            _tailOut.Add(new Vector3(gate + 9f, y, AirportSpec.StreetZ - 14f));
            _tailOut.Add(new Vector3(gate + 14f, y, AirportSpec.StreetZ - 2.5f));
            _tailOut.Add(new Vector3(AirportSpec.StreetX1 - 20f, y, AirportSpec.StreetZ - 2.5f));
        }

        /// <summary>Whether the field is dark enough. With no clock in the scene the run
        /// keeps its own hours off the interval alone.</summary>
        bool NightNow()
        {
            if (_clock == null) return true;
            float h = _clock.Hour;
            return NightFrom > NightTo ? (h >= NightFrom || h < NightTo) : (h >= NightFrom && h < NightTo);
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            _van.Tick(dt);
            _tail.Tick(dt);

            if (_tailTimer >= 0f)
            {
                _tailTimer -= dt;
                if (_tailTimer < 0f && _tail.Tf != null) _tail.Start(_tailOut);
            }
            if (_tail.Done && _tail.Tf != null && _tail.Tf.localPosition != _tailHome && _phase == Phase.Waiting)
                Send(_tail, _tailHome, 180f + 6f);

            switch (_phase)
            {
                case Phase.Waiting:
                    _timer -= dt;
                    if (_timer > 0f || !NightNow() || _van.Tf == null) return;
                    Bags(false);
                    _van.Start(_in);
                    _phase = Phase.Inbound;
                    return;

                case Phase.Inbound:
                    if (!_van.Done) return;
                    Bags(true);
                    _timer = TransferSeconds;
                    _phase = Phase.Transfer;
                    return;

                case Phase.Transfer:
                    _timer -= dt;
                    if (_timer > 0f) return;
                    Bags(false);
                    _van.Start(_out);
                    _tailTimer = TailDelay;
                    _phase = Phase.Outbound;
                    return;

                default:
                    if (!_van.Done) return;
                    // off the map: the van goes back to the verge to wait for the next
                    // one, which is cheaper than making and destroying one a run
                    Send(_van, _vanHome, 180f);
                    _timer = Interval;
                    _phase = Phase.Waiting;
                    return;
            }
        }

        /// <summary>Puts a body back where it waits, without driving it there - it went
        /// off the map, and what a vehicle does off the map is nobody's business.</summary>
        static void Send(AirportDriver driver, Vector3 home, float yaw)
        {
            if (driver.Tf == null) return;
            driver.Route.Clear();
            driver.Done = true;
            driver.Tf.SetLocalPositionAndRotation(home, Quaternion.Euler(0f, yaw, 0f));
        }

        void Bags(bool on)
        {
            for (int i = 0; i < _bags.Count; i++)
                if (_bags[i] != null) _bags[i].SetActive(on);
        }
    }
}
