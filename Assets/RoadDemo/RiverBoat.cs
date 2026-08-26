using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The one boat on the river: a sailboat whose mast (13.7 m) no shut bridge can pass,
    /// sailing the line from one reach of the water to the other and back, resting a
    /// while at each end. Ahead of every bridge it calls the <see cref="Bascule"/> - the
    /// gates go down, the leaves rise - holds short of the channel until the leaves are
    /// up, comes through, and lets the bridge go once it is clear of it. Only the nearest
    /// bridge ahead is called, so a second bridge close behind the first does not stand
    /// gated while the boat waits at the first.
    ///
    /// Installed by the district at Build with the bridges and where each lies along the
    /// line; nothing to set up.
    /// </summary>
    public sealed class RiverBoat : MonoBehaviour
    {
        /// <summary>The line it sails, on the water, and the bridges across it with the
        /// place of each channel's centre along the line from <see cref="From"/>.</summary>
        public Vector3 From, To;
        public List<Bascule> Bridges = new List<Bascule>();
        public List<float> Along = new List<float>();

        /// <summary>Game metres a second - a sailboat under its motor, at the harness's
        /// pace; and the rest at each end before it turns back.</summary>
        public float Speed = 6f;
        public float Rest = 45f;
        /// <summary>How far ahead of a channel's centre the bridge is called, where the
        /// boat holds short of the channel's edge until the leaves are up, and how far
        /// past the centre it lets the bridge go.</summary>
        public float Warn = 90f, Stop = 15f, Clear = 25f;

        float _s;
        int _dir = 1;
        float _resting;

        void Start()
        {
            _s = 0f;
            Face();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float length = Vector3.Distance(From, To);
            if (length < 1f) return;

            if (_resting > 0f)
            {
                _resting -= dt;
                if (_resting > 0f) return;
                _dir = -_dir;
                Face();
            }

            // the nearest bridge ahead that is not yet cleared; every bridge cleared is let go
            int next = -1;
            float nearest = float.MaxValue;
            for (int i = 0; i < Bridges.Count && i < Along.Count; i++)
            {
                if (Bridges[i] == null) continue;
                float ahead = (Along[i] - _s) * _dir;
                if (ahead <= -Clear) { Bridges[i].Called = false; continue; }
                if (ahead < nearest) { nearest = ahead; next = i; }
            }

            bool held = false;
            if (next >= 0)
            {
                var bridge = Bridges[next];
                if (nearest <= Warn) bridge.Called = true;
                if (nearest <= RiverBridge.Channel * 0.5f + Stop && !bridge.IsOpen) held = true;
            }

            if (!held) _s += _dir * Speed * dt;
            if (_dir > 0 && _s >= length) { _s = length; _resting = Rest; }
            if (_dir < 0 && _s <= 0f) { _s = 0f; _resting = Rest; }
            transform.position = Vector3.Lerp(From, To, _s / length);
        }

        void Face()
        {
            var axis = (To - From).normalized * _dir;
            if (axis.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(axis, Vector3.up);
        }
    }
}
