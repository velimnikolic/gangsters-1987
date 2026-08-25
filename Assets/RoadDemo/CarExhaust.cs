using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Smoke out of the tailpipes. A running car breathes, and in 1987 it breathes
    /// visibly: a wisp standing at the lights, a grey cough when the driver puts his
    /// foot down, a thin stream at a steady cruise.
    ///
    /// The art is the pack's own (Synty PolygonParticleFX, FX_Smoke_White_01) - the
    /// alpha-blended smoke the chimneys and the burning shopfronts are made of, which
    /// is the right stuff for a pipe and the wrong SIZE for one: the pack blows it at
    /// three to six metres a puff, for a factory stack seen across a city. Cut to a
    /// quarter of a metre, given a cone to leave by and told to simulate in WORLD
    /// space, it is a tailpipe - the puffs stay standing in the street where they were
    /// blown instead of being dragged along under the boot. (The pack's own
    /// FX_Trail_Exhaust is the wrong stuff by its name: it is ADDITIVE, a rocket's
    /// glow, and smoke that adds light gets brighter the sootier you paint it.)
    ///
    /// Nobody registers with it. Every driving thing in the demo - the traffic, the
    /// law's cars, the outfit's, the bikes - is a RoadCar in StreetTraffic.Users, and
    /// that list is what this reads, so a car spawned by any scene's builder smokes
    /// without a line of wiring anywhere. What it does NOT read is the shells stood at
    /// the kerb (StoodCar is not a RoadCar): nobody is in them and their engines are
    /// cold.
    ///
    /// Like the headlights (DemoHeadlights), only the few nearest the camera smoke at
    /// all: a hundred looping particle systems is a hundred draw calls of overdraw for
    /// smoke nobody can see at two hundred metres. A fixed pool of plumes is handed to
    /// the nearest cars a few times a second and follows them from there; a plume that
    /// changes hands leaves its old puffs behind to die where they were emitted, which
    /// is what they would have done anyway.
    /// </summary>
    public sealed class CarExhaust : MonoBehaviour
    {
        /// <summary>The pack prefab a plume is made of.</summary>
        public const string Plume = "Assets/Synty/PolygonParticleFX/Prefabs/FX_Smoke_White_Small_01.prefab";

        /// <summary>How many pipes smoke at once.</summary>
        const int PlumeBudget = 10;

        /// <summary>Metres from the camera's focus past which nothing smokes.</summary>
        const float Reach = 70f;
        const float ResortInterval = 0.35f;

        // What the exhaust reads as. Idle is a thin pale wisp barely leaving the pipe;
        // the cough is what a foot on the accelerator does - fatter, darker, thrown
        // further back - and it dies away again in half a second.
        const float IdleRate = 5f, CruiseRate = 4f, ThrottleRate = 26f;
        const float ThrottleFor = 4f;      // m/s^2 that counts as a foot to the floor
        const float ThrottleFade = 2.2f;   // how fast the cough dies back to nothing
        const float PuffLifeLo = 0.7f, PuffLifeHi = 1.4f;
        static readonly Vector2 IdleSize = new Vector2(0.16f, 0.36f);
        static readonly Vector2 SootSize = new Vector2(0.30f, 0.70f);
        static readonly Vector2 IdleBlow = new Vector2(0.3f, 0.9f);
        static readonly Vector2 SootBlow = new Vector2(1.0f, 2.4f);
        static readonly Color Cold = new Color(0.80f, 0.80f, 0.78f, 0.30f);
        static readonly Color Soot = new Color(0.33f, 0.32f, 0.30f, 0.60f);

        sealed class Puff
        {
            public Transform Tf;
            public ParticleSystem.EmissionModule Emission;
            public ParticleSystem.MainModule Main;
            public RoadCar Car;
            public Vector3 Pipe;      // where the pipe is, in that car's own frame
            public float LastSpeed;
            public float Throttle;    // 0 off the gas .. 1 foot down
            public float Shown = -1f; // the throttle the plume is drawn at
            public float Rate = -1f;  // the emission it is set to
        }

        readonly List<Puff> _puffs = new List<Puff>();
        readonly List<RoadCar> _running = new List<RoadCar>();
        readonly List<RoadCar> _chosen = new List<RoadCar>();
        float[] _key = new float[0];
        int[] _order = new int[0];
        float _nextResort;
        bool _dead;   // the pack is not here: nothing to do, ever

        /// <summary>Put the exhaust over whatever scene is being built, once. Idempotent
        /// - a second call from another builder finds the first and does nothing.</summary>
        public static CarExhaust Install()
        {
            var found = FindAnyObjectByType<CarExhaust>();
            if (found != null) return found;
            return new GameObject("Car Exhaust").AddComponent<CarExhaust>();
        }

        void LateUpdate()
        {
            if (_dead) return;

            if (Time.unscaledTime >= _nextResort)
            {
                _nextResort = Time.unscaledTime + ResortInterval;
                Rescan();
            }

            float dt = Time.deltaTime;
            for (int i = 0; i < _puffs.Count; i++) Follow(_puffs[i], dt);
        }

        // ------------------------------------------------------------- who smokes

        // The cars nearest where the camera is LOOKING get the plumes - the ground the
        // boom is pointed at, not where the camera stands, the same way the lamps and
        // the headlights are ranked: the rig parks the eye a couple of hundred metres
        // back along its own arm.
        void Rescan()
        {
            var camera = Camera.main;
            _running.Clear();
            if (camera != null)
            {
                var eye = camera.transform.position;
                var forward = camera.transform.forward;
                if (forward.y < -0.05f && eye.y > 0f)
                    eye += forward * (eye.y / -forward.y);

                float reachSqr = Reach * Reach;
                var users = StreetTraffic.Users;
                for (int i = 0; i < users.Count; i++)
                {
                    // something with a driver in it only: the shells at the kerb are not
                    // RoadCars, and a car whose driver has left it - parked, derelict,
                    // blown apart - has its engine off
                    if (!(users[i] is RoadCar car) || car.Tf == null) continue;
                    if (car.Parked || car.Derelict || car.Wrecked) continue;
                    var delta = car.Tf.position - eye;
                    delta.y = 0f;
                    if (delta.sqrMagnitude > reachSqr) continue;
                    _running.Add(car);
                }

                if (_running.Count > PlumeBudget) KeepNearest(eye);
            }

            // a plume holding a car that is still on the list keeps it - a pipe that
            // hops from car to car every third of a second reads as a flicker, not as
            // smoke
            for (int i = 0; i < _puffs.Count; i++)
            {
                var puff = _puffs[i];
                int held = puff.Car == null ? -1 : _running.IndexOf(puff.Car);
                if (held >= 0) _running.RemoveAt(held);
                else Hand(puff, null);
            }

            for (int i = 0; i < _running.Count; i++)
            {
                var free = Free();
                if (free == null) break;
                Hand(free, _running[i]);
            }
            _running.Clear();
        }

        // Cuts the candidates down to the nearest PlumeBudget of them: a partial
        // selection over plain floats (DemoStreetLamps.Nearest), and the chosen ranks
        // read off into the list the plumes are handed out of.
        void KeepNearest(Vector3 eye)
        {
            if (_key.Length != _running.Count)
            {
                _key = new float[_running.Count];
                _order = new int[_running.Count];
            }
            for (int i = 0; i < _running.Count; i++)
            {
                _order[i] = i;
                var delta = _running[i].Tf.position - eye;
                delta.y = 0f;
                _key[i] = delta.sqrMagnitude;
            }
            DemoStreetLamps.Nearest(_key, _order, PlumeBudget);

            _chosen.Clear();
            for (int rank = 0; rank < PlumeBudget; rank++) _chosen.Add(_running[_order[rank]]);
            _running.Clear();
            _running.AddRange(_chosen);
        }

        Puff Free()
        {
            for (int i = 0; i < _puffs.Count; i++)
                if (_puffs[i].Car == null) return _puffs[i];
            return _puffs.Count < PlumeBudget ? Make() : null;
        }

        void Hand(Puff puff, RoadCar car)
        {
            puff.Car = car;
            puff.LastSpeed = car != null ? car.RoadSpeed : 0f;
            puff.Throttle = 0f;
            puff.Pipe = car == null ? Vector3.zero : Tailpipe(car);
            if (car == null) Emit(puff, 0f);
        }

        // -------------------------------------------------------------- the plume

        Puff Make()
        {
            // the demo's own particle-prefab cache - its names are the bomb's, its job
            // is any of the pack's FX: loaded once, spawned as often as asked
            var go = BombFx.Spawn(Plume, Vector3.zero, Quaternion.identity, 1f, 0f, transform);
            if (go == null)
            {
                _dead = true;   // no pack, no smoke, and no point looking again
                return null;
            }
            go.name = "exhaust";
            var ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps == null) { _dead = true; Destroy(go); return null; }

            // The prefab is a chimney's: a slow ball of smoke welling out of a point in
            // its own frame, and a local frame drags every puff along with the car that
            // made it. Stopped first - the frame and the prewarm are settable only on a
            // system that is not running.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.prewarm = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(PuffLifeLo, PuffLifeHi);
            // and it leaves by a pipe rather than welling out of a point: a narrow cone,
            // which the plume aims down the exhaust for it
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 11f;
            shape.radius = 0.03f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            ps.Play();

            var puff = new Puff { Tf = go.transform, Emission = emission, Main = main };
            _puffs.Add(puff);
            return puff;
        }

        // The plume rides the pipe, pointing back and a little up, and how hard it
        // smokes is read off what the car is doing.
        void Follow(Puff puff, float dt)
        {
            var car = puff.Car;
            if (car == null) return;
            if (car.Tf == null || car.Parked || car.Derelict || car.Wrecked)
            {
                Hand(puff, null);   // its engine is off; the plume is free for another
                return;
            }

            var tf = car.Tf;
            puff.Tf.SetPositionAndRotation(
                tf.TransformPoint(puff.Pipe),
                Quaternion.LookRotation(Vector3.Slerp(-tf.forward, Vector3.up, 0.18f), Vector3.up));

            float speed = car.RoadSpeed;
            float accel = dt > 1e-4f ? (speed - puff.LastSpeed) / dt : 0f;
            puff.LastSpeed = speed;
            // the cough climbs with the foot and falls away on its own, so a car that
            // has reached its cruise stops smoking without a step in it
            puff.Throttle = Mathf.Max(
                Mathf.Clamp01(accel / ThrottleFor),
                puff.Throttle - ThrottleFade * dt);

            Emit(puff, IdleRate + CruiseRate * Mathf.Clamp01(speed / 12f) + ThrottleRate * puff.Throttle);

            // fatter, darker and blown harder out of the pipe the harder he drives it -
            // four native writes, so they wait for the foot to actually move. The size
            // and the blow are written whole rather than through the modules' own
            // multipliers, which mean one thing over a curve and another over a pair of
            // constants; the pack's numbers are a chimney's and none of them survive.
            if (Mathf.Abs(puff.Throttle - puff.Shown) < 0.02f) return;
            float foot = puff.Shown = puff.Throttle;
            var size = Vector2.Lerp(IdleSize, SootSize, foot);
            var blow = Vector2.Lerp(IdleBlow, SootBlow, foot);
            puff.Main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            puff.Main.startSpeed = new ParticleSystem.MinMaxCurve(blow.x, blow.y);
            puff.Main.startColor = Color.Lerp(Cold, Soot, foot);
        }

        // Writing a curve rebuilds the module's state, so the rate is touched only when
        // the number actually moves - except at nought, which is a pipe going out and
        // has to land exactly (a plume left emitting a wisp is a plume nobody owns
        // smoking in an empty street).
        static void Emit(Puff puff, float rate)
        {
            if (rate <= 0f ? puff.Rate == 0f : Mathf.Abs(rate - puff.Rate) < 0.25f) return;
            puff.Rate = rate;
            puff.Emission.rateOverTime = rate;
        }

        // ----------------------------------------------------------- the tailpipe

        static readonly Dictionary<Transform, Vector3> Pipes = new Dictionary<Transform, Vector3>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Pipes.Clear();

        /// <summary>Where a body's pipe is, in its own frame. The packs model one on the
        /// bikes and name it (Exhaust, Muffler), so that is asked for first and the
        /// smoke leaves exactly the chrome it should; a pack car has no such part and
        /// gets the ordinary place for one - under the back bumper, off to one flank,
        /// which flank alternating body by body so that a street of them is not one
        /// mirrored car ten times over. Measured once per body.</summary>
        public static Vector3 Tailpipe(RoadCar car)
        {
            var tf = car.Tf;
            if (Pipes.TryGetValue(tf, out var pipe)) return pipe;
            if (Pipes.Count > 256) Sweep();

            pipe = Vector3.zero;
            foreach (var t in tf.GetComponentsInChildren<Transform>(true))
            {
                if (t == tf) continue;
                if (t.name.IndexOf("Exhaust", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    t.name.IndexOf("Muffler", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var r = t.GetComponentInChildren<Renderer>();
                if (r == null) continue;
                // the packs pivot every part at the vehicle's own origin (see WheelSpin),
                // so a part's PLACE is read off what it draws, never off its transform
                pipe = tf.InverseTransformPoint(r.bounds.center);
                break;
            }

            if (pipe == Vector3.zero)
            {
                float side = (Pipes.Count & 1) == 0 ? 1f : -1f;
                pipe = new Vector3(side * car.HalfWide * 0.5f, 0.3f, -car.HalfLen + 0.1f);
            }
            // never under the road, never up in the boot, never ahead of the back of it
            pipe.y = Mathf.Clamp(pipe.y, 0.15f, 1.2f);
            pipe.z = Mathf.Min(pipe.z, -car.HalfLen + 0.25f);

            Pipes[tf] = pipe;
            return pipe;
        }

        // A car blown apart or driven off the edge of the demo takes its body with it,
        // and the measurement kept against it would sit in the table for the rest of
        // the run. Cleared out whenever the table has grown past a city's worth of
        // bodies - which, in a demo that spawns a getaway car per job, it does.
        static void Sweep()
        {
            var gone = new List<Transform>();
            foreach (var pair in Pipes)
                if (pair.Key == null) gone.Add(pair.Key);
            for (int i = 0; i < gone.Count; i++) Pipes.Remove(gone[i]);
        }
    }
}
