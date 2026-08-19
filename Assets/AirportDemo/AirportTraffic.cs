using System.Collections.Generic;
using UnityEngine;

namespace AirportDemo
{
    /// <summary>
    /// The cars on the public side of the wire: the ones going round the kerb loop,
    /// the cabs waiting on the rank, the bus, and the traffic on the approach road
    /// that has nothing to do with the airport at all and is only passing.
    ///
    /// Two kinds of road, both one-way as far as any driver here is concerned - a
    /// closed loop and a straight run that wraps at the map edge - so nobody has to
    /// give way to anybody and there is no junction to model. The loop's cars pull
    /// in at the kerb now and then, stand a moment with somebody getting out, and
    /// carry on, which is the whole life of an airport forecourt.
    /// </summary>
    public sealed class AirportTraffic
    {
        readonly List<AirportDriver> _loop = new List<AirportDriver>();
        readonly List<AirportDriver> _street = new List<AirportDriver>();
        readonly List<AirportDriver> _all = new List<AirportDriver>();
        readonly System.Random _rng;
        readonly List<Vector3> _kerbStops;

        public AirportTraffic(System.Random rng, List<Vector3> kerbStops)
        {
            _rng = rng;
            _kerbStops = kerbStops;
        }

        public IReadOnlyList<AirportDriver> All => _all;

        /// <summary>A car onto the kerb loop, dropped in at a share of the way round
        /// so they do not all start nose to tail.</summary>
        public void AddToLoop(GameObject car, List<Vector3> route, float alongFraction)
        {
            if (car == null || route.Count < 3) return;
            var d = new AirportDriver { Cruise = AirportKit.Range(_rng, 6.5f, 9f), Gap = 6.5f };
            d.Bind(car.transform);
            d.Traffic = _loop;
            int leg = Mathf.Clamp(Mathf.FloorToInt(alongFraction * route.Count), 0, route.Count - 1);
            d.Start(route, closed: true, leg: leg);
            // slide it along its own leg so two cars starting on the same one are not
            // in the same place
            var next = route[(leg + 1) % route.Count];
            d.Tf.position = Vector3.Lerp(route[leg], next, (float)_rng.NextDouble());
            // and now and then it stops at the kerb, which is what a forecourt is for
            d.OnPoint = (drv, i) =>
            {
                if (i != 1 || _kerbStops == null || _kerbStops.Count == 0) return;
                if (_rng.NextDouble() < 0.45) drv.Dwell = AirportKit.Range(_rng, 8f, 22f);
            };
            _loop.Add(d);
            _all.Add(d);
        }

        /// <summary>A car onto the approach road, wrapping round at the map edge so it
        /// is always driving somewhere and never seen to turn round.</summary>
        public void AddToStreet(GameObject car, bool eastbound, float alongFraction, float z)
        {
            if (car == null) return;
            var d = new AirportDriver { Cruise = AirportKit.Range(_rng, 11f, 15f), Gap = 9f };
            d.Bind(car.transform);
            d.Traffic = _street;
            float x0 = eastbound ? -430f : 430f, x1 = eastbound ? 430f : -430f;
            var route = new List<Vector3>
            {
                new Vector3(x0, AirportSpec.PaveY, z),
                new Vector3(x1, AirportSpec.PaveY, z),
            };
            d.Start(route, closed: false, wrap: true);
            d.Tf.position = new Vector3(Mathf.Lerp(x0, x1, alongFraction), AirportSpec.PaveY, z);
            _street.Add(d);
            _all.Add(d);
        }

        /// <summary>A cab on the rank: it does not drive, it waits, which is what a cab
        /// at a county airport did all day in 1987.</summary>
        public void AddCab(GameObject cab, Vector3 at)
        {
            if (cab == null) return;
            cab.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 90f, 0f));
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < _all.Count; i++) _all[i].Tick(dt);
        }
    }
}
