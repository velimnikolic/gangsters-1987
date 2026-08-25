using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    // A forklift on its shuttle: from the pallet pile by the gangway along the quay
    // lane and up the aisle to the shed door with a pallet on its forks, back empty,
    // pivoting on the spot at each turn as forklifts do, forks up when laden and
    // down when not, a pause at each end. Runs while its berth is being worked and
    // parks at the pile otherwise. Plain class ticked by the builder.
    public sealed class HarborForklift
    {
        const float DriveSpeed = 4f;
        const float Accel = 3f, Brake = 4f;
        const float PivotRate = 200f;
        const float ForksUp = 0.45f, ForksDown = 0.06f;

        public Transform Tf;
        public List<Vector3> Route = new List<Vector3>();     // pile first, door last
        public System.Func<bool> Working = () => true;
        public GameObject Payload;                             // shown on the way out
        public Transform PileGrows;                            // the door-side pile that grows and recycles
        /// <summary>Which leg carries the load: out from the first point (the quay
        /// shuttle, pile to door) or back to it (a shed lift emptying a lorry at the
        /// kerb into the shed it parks in).</summary>
        public bool LadenOutward = true;
        /// <summary>Called the moment the machine picks its load up. What the quay
        /// shuttle's devanning gang counts its pallets off by (HarborDevanning).</summary>
        public System.Action Took;

        /// <summary>Move one node of the route while the machine is running - the pick-up
        /// end follows the box the gantry last landed, so the shuttle is seen to go to
        /// the box that has just come off the ship rather than to a fixed spot.</summary>
        public void SetNode(int i, Vector3 at)
        {
            if (i >= 0 && i < Route.Count) Route[i] = at;
        }

        Transform _carriage;
        readonly RoadDemo.WheelSpin _wheels = new RoadDemo.WheelSpin();
        float _carriageRest;
        int _node;            // the node we are at or have left
        bool _outward = true; // pile -> door
        float _pause = 1f, _speed;
        bool _laden;

        public void Bind(Transform tf)
        {
            Tf = tf;
            _carriage = HarborKit.FindDeep(tf, "Platform");
            if (_carriage != null) _carriageRest = _carriage.localPosition.y;
            _wheels.Read(tf);
            if (Payload != null && _carriage != null)
            {
                Payload.transform.SetParent(_carriage, false);
                Payload.transform.localPosition = Vector3.zero;
                Payload.transform.localRotation = Quaternion.identity;
                Payload.SetActive(false);
            }
        }

        public void Tick(float dt)
        {
            if (Tf == null || Route.Count < 2) return;
            // the forks follow the load
            if (_carriage != null)
            {
                float want = _carriageRest + (_laden ? ForksUp : ForksDown);
                var lp = _carriage.localPosition;
                lp.y = Mathf.MoveTowards(lp.y, want, dt * 0.35f);
                _carriage.localPosition = lp;
            }

            bool working = Working == null || Working();
            int goal = _outward ? _node + 1 : _node - 1;
            if (_pause > 0f)
            {
                _pause -= dt;
                Spin(dt);
                return;
            }
            // parked at the pile while the berth is quiet. Not asked which way she is
            // pointing: coming home she reaches node 0 with _outward already false, and
            // the old `&& _outward` meant this guard fired only on the very first cycle
            if (!working && _node == 0 && !_laden) { _speed = 0f; return; }
            // and standing at the pile with work waiting: the load goes on the forks
            // BEFORE she sets off, not after a wasted run out and back. Only the machine
            // that carries its load outward - a shed lift is meant to go out empty.
            if (working && LadenOutward && _node == 0 && _outward && !_laden)
            {
                _laden = true;
                if (Payload != null) Payload.SetActive(true);
                Took?.Invoke();
                _pause = HarborKit.Range(_rng, 1.4f, 3f);
                return;
            }
            if (goal < 0 || goal >= Route.Count)
            {
                // an end: turn round after a pause. On the laden leg's far end the load
                // is dropped (and the pile there grows); at the other end it is taken
                // up - if there is work; if not, the machine stands where it is
                bool dropping = _outward == LadenOutward;
                if (dropping)
                {
                    _laden = false;
                    if (Payload != null) Payload.SetActive(false);
                    Grow();
                }
                else
                {
                    if (!working)
                    {
                        // nothing to take: at the far end turn back empty, at home stand
                        if (_outward) { _outward = false; _pause = 1f; return; }
                        _speed = 0f;
                        return;
                    }
                    _laden = true;
                    if (Payload != null) Payload.SetActive(true);
                    Took?.Invoke();
                }
                _outward = !_outward;
                _pause = HarborKit.Range(_rng, 1.4f, 3f);
                return;
            }

            var target = Route[goal];
            var pos = Tf.position;
            var to = target - pos; to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.15f)
            {
                _node = goal;
                _speed = 0f;
                _pause = 0.2f;
                return;
            }
            // pivot first, then drive
            var face = Quaternion.LookRotation(to / dist, Vector3.up);
            float angle = Quaternion.Angle(Tf.rotation, face);
            Tf.rotation = Quaternion.RotateTowards(Tf.rotation, face, PivotRate * dt);
            if (angle > 10f) { _speed = Mathf.MoveTowards(_speed, 0f, Brake * dt); return; }
            float wantSpeed = Mathf.Min(DriveSpeed, Mathf.Sqrt(2f * Brake * Mathf.Max(0f, dist - 0.3f)));
            _speed = Mathf.MoveTowards(_speed, wantSpeed, (wantSpeed > _speed ? Accel : Brake) * dt);
            var step = Mathf.Min(dist, _speed * dt);
            Tf.position = pos + to / dist * step;
            Spin(dt);
        }

        // Each wheel on its own hub - the pack pivots them all at the machine's
        // origin, which would swing them round its middle instead (WheelSpin).
        void Spin(float dt) => _wheels.Tick(dt, _speed, 0f);

        static readonly System.Random _rng = new System.Random(11);
        int _piled;

        /// <summary>The pile at the door: a pallet a trip, four high, then it is
        /// taken in (cleared) and starts again.</summary>
        void Grow()
        {
            if (PileGrows == null) return;
            _piled++;
            int shown = _piled % 5;
            for (int i = 0; i < PileGrows.childCount; i++)
                PileGrows.GetChild(i).gameObject.SetActive(i < shown);
        }
    }
}
