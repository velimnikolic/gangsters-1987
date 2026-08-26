using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// One barrier of a toll plaza: a junction box on the freeway with an arm across
    /// it, and the rule every driver meets there - come to a stand at the line, pay,
    /// and go when the arm is up. It is asked in exactly the place a red light is
    /// asked about (RoadCar.CanEnter), which is what makes it a thing the whole city
    /// obeys rather than a prop on a deck.
    ///
    /// A gate serves ONE car at a time. Whoever asks first while the window is free
    /// has it; everybody else is held at the line and forms the queue behind him. He
    /// keeps it while he goes on asking - a driver who turned away or was wrecked
    /// stops asking, and the window is free again within a third of a second.
    /// </summary>
    public sealed class TollGate
    {
        /// <summary>What the trace calls it: "toll west", "toll east".</summary>
        public string Name = "toll";
        /// <summary>Seconds the money takes, once he has actually stopped.</summary>
        public float Dwell = 2.2f;
        /// <summary>Longer than this at the window and something is wrong with the
        /// gate rather than with the traffic: it goes down as a fault.</summary>
        public float Patience = 30f;
        /// <summary>The junction the gate stands in, for the trace and for catching a
        /// car that crossed the line with the arm down.</summary>
        public RoadNode Node;

        /// <summary>The booms this gate lifts: one over every lane of the road it
        /// stands across, all of them going up together, because the gate lets ONE car
        /// through at a time whichever lane he is in. A single arm here meant that on a
        /// plaza with more than one lane every boom but the last stayed down for good,
        /// with the traffic driving through it.</summary>
        internal readonly List<TollArm> Arms = new List<TollArm>();

        RoadCar _at;              // whoever is at the window
        float _asked = -1f;       // when he last asked
        float _stopped = -1f;     // when he came to a stand (-1: still rolling)
        float _paidAt = -1f;      // when he was let through
        bool _flagged;            // his wait has already gone down as a fault
        int _waiting;             // asked this instant and refused
        int _waitingSeen;

        /// <summary>Cars let through, and the longest anybody stood here.</summary>
        public int Paid { get; private set; }
        public float WorstWait { get; private set; }

        /// <summary>Whether the arm is up. It starts going up while the man at the
        /// window is still counting out his money - an arm that only began to lift
        /// once the car was let through would be lifting as the car drove under it -
        /// and it comes down again a few seconds after he is away.</summary>
        public bool Open
        {
            get
            {
                if (_paidAt >= 0f) return Time.time - _paidAt < 4f;
                return _stopped >= 0f && Time.time - _stopped > Dwell * 0.4f;
            }
        }

        /// <summary>May this driver cross the line? Asked every frame he is on the
        /// approach, so a refusal is a thing he can still be let out of.</summary>
        public bool MayPass(RoadCar car)
        {
            if (car == null) return true;
            float now = Time.time;

            // the window: free if nobody holds it, or the man holding it has stopped
            // asking (turned off, wrecked, driven through)
            if (_at != car && (_at == null || now - _asked > 0.35f || _at.Wrecked)) Take(car, now);
            if (_at != car)
            {
                if (_waitingSeen != Time.frameCount) { _waitingSeen = Time.frameCount; _waiting = 0; }
                _waiting++;
                return false;
            }

            _asked = now;
            if (car.Speed > 0.7f) { _stopped = -1f; return false; }   // he has not stopped yet
            if (_stopped < 0f) _stopped = now;
            float waited = now - _stopped;
            if (waited > WorstWait) WorstWait = waited;
            if (waited < Dwell)
            {
                if (!_flagged && waited > Patience)
                {
                    _flagged = true;
                    Fault("tollstuck", car, $"{waited:F0}s at {Name}");
                }
                return false;
            }

            if (_paidAt < 0f)
            {
                _paidAt = now;
                Paid++;
                _through[car.Id] = now;
                if (DriveTrace.On)
                {
                    var sb = DriveTrace.Take();
                    DriveTrace.Int(sb, "id", car.Id);
                    DriveTrace.Str(sb, "gate", Name);
                    DriveTrace.Num(sb, "wait", waited, "F1");
                    DriveTrace.Int(sb, "q", _waiting);
                    DriveTrace.Row("toll", sb.ToString());
                }
            }
            return true;
        }

        void Take(RoadCar car, float now)
        {
            _at = car;
            _asked = now;
            _stopped = -1f;
            _paidAt = -1f;
            _flagged = false;
        }

        /// <summary>Every frame, from the plaza: the arm follows the window, and a car
        /// that is in the box without having paid is reported.</summary>
        internal void Tick(float dt)
        {
            float want = Open ? 1f : 0f;
            for (int a = 0; a < Arms.Count; a++) Arms[a].Toward(want, dt);
            if (Node == null) return;
            float now = Time.time;
            for (int i = 0; i < Node.Inside.Count; i++)
            {
                var car = Node.Inside[i].Car;
                if (car == null || car.Wrecked) continue;
                // he paid, and he is still crossing: the box takes a moment to clear,
                // and by then the window has usually been taken by the man behind him
                if (_through.TryGetValue(car.Id, out float paid) && now - paid < 12f) continue;
                if (_ran.Contains(car.Id)) continue;
                _ran.Add(car.Id);
                Fault("tollrun", car, $"through {Name} with the arm down");
            }
        }

        readonly HashSet<int> _ran = new HashSet<int>();
        readonly Dictionary<int, float> _through = new Dictionary<int, float>();

        void Fault(string kind, RoadCar car, string what)
        {
            Debug.LogWarning($"[toll] {kind}: car {car.Id} - {what}");
            if (!DriveTrace.On) return;
            var sb = DriveTrace.Take();
            DriveTrace.Str(sb, "fault", kind);
            DriveTrace.Int(sb, "id", car.Id);
            DriveTrace.Str(sb, "who", "car " + car.Id);
            DriveTrace.Str(sb, "gate", Name);
            DriveTrace.Str(sb, "what", what);
            DriveTrace.Row("fault", sb.ToString());
        }
    }

    /// <summary>The boom itself: which way its arm points was measured off the prefab,
    /// so lifting it is a turn about the right axis whichever way the gate faces.</summary>
    public sealed class TollArm
    {
        readonly Transform _t;
        readonly Quaternion _down;
        readonly Vector3 _axis;
        readonly float _lift;
        float _at;

        public TollArm(Transform t, Vector3 axis, float lift)
        {
            _t = t;
            _down = t.localRotation;
            _axis = axis;
            _lift = lift;
        }

        /// <summary>An arm takes about a second and a half to go up or come down.</summary>
        public void Toward(float want, float dt)
        {
            if (_t == null) return;
            float step = dt / 1.4f;
            _at = Mathf.MoveTowards(_at, want, step);
            _t.localRotation = _down * Quaternion.AngleAxis(_lift * _at, _axis);
        }
    }

    /// <summary>
    /// The plaza: the gates on one stretch of road, ticked together. Nothing but the
    /// arms and the audit happen here - the deciding is the gate's, and the asking is
    /// the driver's.
    /// </summary>
    public sealed class TollPlaza : MonoBehaviour
    {
        public readonly List<TollGate> Gates = new List<TollGate>();

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < Gates.Count; i++) Gates[i].Tick(dt);
        }

        /// <summary>What the plaza took, for the console at the end of a run.</summary>
        public string Story()
        {
            int paid = 0;
            float worst = 0f;
            foreach (var g in Gates) { paid += g.Paid; worst = Mathf.Max(worst, g.WorstWait); }
            return $"{Gates.Count} gate(s), {paid} paid, longest wait {worst:F0}s";
        }
    }
}
