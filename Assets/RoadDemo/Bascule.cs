using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A bridge that opens. When the river's boat calls it (<see cref="RiverBoat"/>) the
    /// gates come down on both approaches - a standing claim on the carriageway at each
    /// edge of the channel, with no car behind it, which the drivers treat as they treat
    /// anything dead in the lane: they stop short of it, and after a while their own
    /// rules let them try to pass or turn back (<c>RoadCar</c>'s jam logic; the harness
    /// sees them stand 20-60 s). The channel is let clear, the leaves rise about their
    /// trunnions and stay up while the call stands; when the boat is through the leaves
    /// come down and the gates lift.
    ///
    /// This is a game mechanic before it is scenery (Docs/river-plan.md 6): a crew car
    /// that meets the gates cannot cross. Nothing here touches how a car drives; the gate
    /// is an occupant on the road like a parked truck, and the drivers' own rules do the
    /// rest - a true queue that waits for the gate, rather than a jam behind a dead
    /// thing, is the driver layer's to add (Docs/river-plan.md 5.7).
    ///
    /// The leaves are the palm city's, stood shut by <see cref="RiverBridge"/>: a leaf runs
    /// twenty metres along its own +Z from a pivot at its shore end, so raising it is a
    /// turn about its own X; the two of a pair face each other, so the same turn lifts
    /// both, and the boulevard's four are two such pairs. Installed by the district at
    /// Build; nothing to set up.
    /// </summary>
    public sealed class Bascule : MonoBehaviour
    {
        /// <summary>The road over the bridge, and where along it the channel lies.</summary>
        public Carriageway Road;
        public float S0, S1;
        public List<Transform> Leaves = new List<Transform>();

        /// <summary>How far the leaves rise, and how long - game seconds, shortened from
        /// the minutes a real bascule takes so a run of the harness sees a whole cycle and
        /// a queue does not stand long enough to be called a fault.</summary>
        public float Angle = 72f;
        public float Rise = 15f, Lower = 15f;
        /// <summary>How long the gates wait for the channel to clear before the leaves rise
        /// regardless.</summary>
        public float Patience = 20f;
        /// <summary>The gates stand this far outside the channel: a car length and the gap
        /// a driver keeps, so a queue at a gate never reaches the leaves.</summary>
        public const float GateBack = 9f;

        /// <summary>The boat's call: while it stands the bridge goes up and stays up.</summary>
        public bool Called;
        /// <summary>Are the leaves all the way up?</summary>
        public bool IsOpen => _state == State.Open;
        /// <summary>Is the bridge shut to traffic right now - gates down, or leaves up?</summary>
        public bool Closed => _state != State.Shut;

        enum State { Shut, Gated, Rising, Open, Lowering }
        State _state = State.Shut;
        float _clock;
        float _lift;                        // 0 shut .. 1 up
        RoadOccupant _westGate, _eastGate;
        readonly List<Quaternion> _rest = new List<Quaternion>();

        void Start()
        {
            _rest.Clear();
            foreach (var leaf in Leaves) _rest.Add(leaf != null ? leaf.localRotation : Quaternion.identity);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            switch (_state)
            {
                case State.Shut:
                    if (!Called) break;
                    Gate();
                    _state = State.Gated;
                    _clock = 0f;
                    break;

                case State.Gated:
                    // the gates are down; the leaves wait for the channel to be clear of
                    // anything already on them - the channel INSIDE the gates, because the
                    // cars queued at a gate stand a length short of it and their claims
                    // reach past it, and a bridge that waits for those never rises (the
                    // harness: four runs in five with the gates down for the rest of the
                    // run). A car that stands on the leaves for a whole cycle is stuck
                    // for its own reasons, and the bridge stops waiting for it
                    _clock += dt;
                    if (!Called) { Ungate(); _state = State.Shut; break; }
                    if (Road != null && _clock < Patience && Road.Busy(null, S0 + 1f, S1 - 1f, Road.EdgeLo, Road.EdgeHi)) break;
                    _state = State.Rising;
                    _clock = 0f;
                    break;

                case State.Rising:
                    _clock += dt;
                    _lift = Mathf.Clamp01(_clock / Rise);
                    Pose();
                    if (_clock < Rise) break;
                    _state = State.Open;
                    break;

                case State.Open:
                    if (Called) break;
                    _state = State.Lowering;
                    _clock = 0f;
                    break;

                case State.Lowering:
                    _clock += dt;
                    _lift = 1f - Mathf.Clamp01(_clock / Lower);
                    Pose();
                    if (Called)
                    {
                        // called back on the way down: up again from where the leaves are
                        // (the lift is linear in the clock, so this is exact)
                        _state = State.Rising;
                        _clock = _lift * Rise;
                        break;
                    }
                    if (_clock < Lower) break;
                    Ungate();
                    _state = State.Shut;
                    break;
            }
        }

        void Pose()
        {
            // a leaf rises about its own X, its +Z end swinging up; the ease is put on
            // here, so the lift itself stays linear and a turn-around mid-way is exact
            var turn = Quaternion.Euler(-Angle * Mathf.SmoothStep(0f, 1f, _lift), 0f, 0f);
            for (int i = 0; i < Leaves.Count && i < _rest.Count; i++)
                if (Leaves[i] != null) Leaves[i].localRotation = _rest[i] * turn;
        }

        /// <summary>The gates: a standing claim across the whole road at each edge of the
        /// channel, a car length back from it, that never moves.</summary>
        void Gate()
        {
            if (Road == null) return;
            _westGate = Slab(S0 - GateBack);
            _eastGate = Slab(S1 + GateBack);
        }

        RoadOccupant Slab(float s)
        {
            var o = new RoadOccupant
            {
                Road = Road,
                S0 = s - 0.4f, S1 = s + 0.4f, BodyS0 = s - 0.4f, BodyS1 = s + 0.4f,
                D0 = Road.EdgeLo, D1 = Road.EdgeHi, BodyD0 = Road.EdgeLo, BodyD1 = Road.EdgeHi,
                Vel = 0f, Heading = 0, Parked = false, Priority = 100,
            };
            Road.Occupants.Add(o);
            return o;
        }

        void Ungate()
        {
            if (Road == null) return;
            if (_westGate != null) Road.Occupants.Remove(_westGate);
            if (_eastGate != null) Road.Occupants.Remove(_eastGate);
            _westGate = _eastGate = null;
        }

        void OnDestroy() => Ungate();
    }
}
