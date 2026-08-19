using System.Collections.Generic;
using UnityEngine;

namespace AirportDemo
{
    /// <summary>
    /// The turnaround: what happens between an aeroplane shutting down on a stand and
    /// starting up again. In 1987 at a county field there is no airbridge, no bus and
    /// no carousel - the aeroplane stops, the steps are run up, and the people walk.
    /// So that is what this does, and it is the whole of it:
    ///
    ///   the propeller stops              -> the steps go to the door
    ///   a quarter of a minute            -> the first passenger comes down them
    ///   one every second and a half      -> a file across the ramp to the gate door
    ///   the aeroplane is cleaned         -> the bowser comes, the bags change over
    ///   one every couple of seconds      -> the next lot walk out and up the steps
    ///   the last one aboard              -> the steps come away, and it goes
    ///
    /// FlightOps holds the aeroplane on its stand the whole time (Aircraft.GroundHold)
    /// and this is what clears it, so the departure is genuinely waiting on the
    /// boarding rather than on a timer that happens to be about as long.
    ///
    /// The people are a pool. A passenger who reaches the end of his walk is switched
    /// off and goes back into it, so four stands turning round at once cost a dozen
    /// bodies rather than a hundred - and every one of them is a Synty character, as
    /// everything but the aircraft on this field is.
    /// </summary>
    public sealed class AirportBoarding
    {
        enum Step { Waiting, Disembarking, Break, Boarding, Ready }

        sealed class Turn
        {
            public Aircraft Plane;
            public Step Step;
            public float Timer;        // until the next thing happens
            public float Release;      // until the next passenger steps out
            public int Left;           // still to come off, or still to go on
            public int Walking;        // out on the concrete this moment
        }

        /// <summary>A hook on the route's points, identity by default. Everything that
        /// moves on the field - the aeroplanes AND the walkers (AirportWalker works in
        /// localPosition) - hangs under the Live root and works in its space, which is
        /// the field's own, so a route worked out from an aeroplane's position is
        /// already in the walkers' coordinates and nothing needs turning.</summary>
        public System.Func<Vector3, Vector3> ToWorld = own => own;

        readonly List<AirportWalker> _pool = new List<AirportWalker>();
        readonly List<AirportWalker> _live = new List<AirportWalker>();
        readonly List<Turn> _turns = new List<Turn>();
        readonly System.Random _rng;

        public AirportBoarding(System.Random rng) => _rng = rng;

        /// <summary>How many bodies are waiting to be used, for the log line.</summary>
        public int PoolSize => _pool.Count;

        /// <summary>A body the turnarounds may use. It is switched off until somebody
        /// needs it - a passenger only exists while he is walking.</summary>
        public void AddBody(AirportWalker w)
        {
            if (w == null || w.Tf == null) return;
            w.Tf.gameObject.SetActive(false);
            _pool.Add(w);
        }

        /// <summary>Hooked to FlightOps.OnShutdown: an aeroplane is on its stand.</summary>
        public void Meet(Aircraft a)
        {
            if (a == null || !a.Commuter || a.Seats <= 0) return;
            // nobody to walk: the aeroplane is better left to FlightOps' own timer than
            // held on a stand waiting for a file of passengers that will never form
            if (_pool.Count == 0 && _live.Count == 0) return;
            foreach (var t in _turns) if (t.Plane == a) return;
            _turns.Add(new Turn
            {
                Plane = a,
                Step = Step.Waiting,
                Timer = AirportSpec.DoorsToFirstOff,
                Left = Load(a),
            });
        }

        /// <summary>How full it was. Never the whole cabin - a scheduled run out of a
        /// field this size is half empty, and a file of twenty-four people takes the
        /// best part of a minute to walk across the ramp.</summary>
        int Load(Aircraft a) => Mathf.Max(3, Mathf.RoundToInt(a.Seats * AirportKit.Range(_rng, 0.35f, 0.7f)));

        // ------------------------------------------------------------ the tick

        public void Tick(float dt)
        {
            for (int i = 0; i < _live.Count; i++) _live[i].Tick(dt);

            for (int i = _turns.Count - 1; i >= 0; i--)
            {
                var t = _turns[i];
                if (t.Plane == null || t.Plane.Tf == null) { _turns.RemoveAt(i); continue; }
                t.Timer -= dt;
                t.Release -= dt;

                switch (t.Step)
                {
                    case Step.Waiting:
                        if (t.Timer > 0f) break;
                        t.Step = Step.Disembarking;
                        t.Release = 0f;
                        break;

                    case Step.Disembarking:
                        if (t.Left > 0)
                        {
                            if (t.Release > 0f) break;
                            Send(t, off: true);
                            t.Release = AirportSpec.DisembarkGap;
                            break;
                        }
                        // everybody off; the aeroplane is turned round
                        t.Step = Step.Break;
                        t.Timer = AirportSpec.TurnaroundGap;
                        break;

                    case Step.Break:
                        if (t.Timer > 0f) break;
                        t.Step = Step.Boarding;
                        t.Left = Load(t.Plane);
                        t.Release = 0f;
                        break;

                    case Step.Boarding:
                        if (t.Left > 0)
                        {
                            if (t.Release > 0f) break;
                            Send(t, off: false);
                            t.Release = AirportSpec.BoardingGap;
                            break;
                        }
                        // the last of them is still crossing the ramp; the aeroplane
                        // does not shut its door until he is up the steps
                        if (t.Walking > 0) break;
                        t.Step = Step.Ready;
                        t.Timer = AirportSpec.DoorsToStartUp;
                        break;

                    case Step.Ready:
                        if (t.Timer > 0f) break;
                        t.Plane.Doors(false);      // the steps come away
                        t.Plane.GroundHold = false;
                        _turns.RemoveAt(i);
                        break;
                }
            }
        }

        // ------------------------------------------------------------ the walk

        /// <summary>Puts one passenger on the concrete: off the steps and away to the
        /// gate, or out of the gate and up the steps. He walks it once and then he is
        /// done with - switched off, and back in the pool for the next stand.</summary>
        void Send(Turn t, bool off)
        {
            t.Left--;
            var w = Take();
            if (w == null) return;          // nobody spare: the file is thinner, not stuck

            var route = Route(t.Plane, off);
            if (route.Count < 2) { Give(w); return; }

            t.Walking++;
            w.Points = route;
            w.Static = false;
            w.OneWay = true;
            w.DwellRange = new Vector2(0.2f, 0.8f);
            w.OnFinished = done =>
            {
                t.Walking--;
                Give(done);
            };
            w.Tf.gameObject.SetActive(true);
            w.Begin(atFirst: true);
            _live.Add(w);
        }

        /// <summary>The walk itself. Off the steps, out from under the wing, forward
        /// past the nose, over the service road and in at the gate door - or the same
        /// thing backwards. The dog-leg round the wingtip is not decoration: a file of
        /// people walking straight from the door to the terminal would go through the
        /// port wing of a thirty-three metre aeroplane.</summary>
        List<Vector3> Route(Aircraft a, bool off)
        {
            var points = new List<Vector3>();
            if (a.Tf == null) return points;

            float y = AirportSpec.PaveY;
            var right = a.Right;
            var fore = a.Forward;

            // at the foot of the steps, and out clear of the wingtip
            var foot = a.DoorFoot;
            var clear = a.Position - right * (a.HalfSpan + 3f) + fore * (a.Nose * a.DoorFore);
            clear.y = y;
            // then forward past the nose, still outside the wing
            var ahead = a.Position - right * (a.HalfSpan + 3f) + fore * (a.Nose + 6f);
            ahead.y = y;

            int stand = Mathf.Clamp(a.Stand, 0, AirportSpec.CommuterStandX.Length - 1);
            float gx = AirportSpec.GateDoorX(stand);
            var cross = new Vector3(gx, y, AirportSpec.ServiceRoadZ);
            var gate = new Vector3(gx + AirportKit.Range(_rng, -2.5f, 2.5f), y, AirportSpec.GateDoorZ);

            points.Add(ToWorld(foot));
            points.Add(ToWorld(clear));
            points.Add(ToWorld(ahead));
            points.Add(ToWorld(cross));
            points.Add(ToWorld(gate));
            if (!off) points.Reverse();
            return points;
        }

        AirportWalker Take()
        {
            if (_pool.Count == 0) return null;
            int i = _pool.Count - 1;
            var w = _pool[i];
            _pool.RemoveAt(i);
            return w;
        }

        void Give(AirportWalker w)
        {
            if (w == null) return;
            _live.Remove(w);
            w.OnFinished = null;
            w.Static = false;
            w.OneWay = false;
            if (w.Tf != null) w.Tf.gameObject.SetActive(false);
            _pool.Add(w);
        }

        public void Dispose()
        {
            for (int i = 0; i < _live.Count; i++) _live[i].Dispose();
            for (int i = 0; i < _pool.Count; i++) _pool[i].Dispose();
            _live.Clear();
            _pool.Clear();
            _turns.Clear();
        }
    }
}
