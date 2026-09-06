using UnityEngine;

namespace RoadDemo
{
    /// <summary>Visual brake-pedal indication from the shared simulation's speed
    /// and halt state. Holds the lamps through a stop; parking/engine-off resets it.</summary>
    public struct VehicleBrakeState
    {
        float _previous, _hold;
        bool _sampled, _braking;

        public bool Step(float speed, float dt, bool enabled, bool halted)
        {
            if (!enabled) { this = default; return false; }
            speed = Mathf.Abs(speed);
            if (!_sampled) { _sampled = true; _previous = speed; }
            float change = speed - _previous;
            bool slowing = dt > 0f && change < -Mathf.Max(.025f, dt * .35f);
            _hold = slowing ? .18f : Mathf.Max(0f, _hold - dt);
            if (change > .04f) _hold = 0f;
            _braking = slowing || _hold > 0f || (speed < .12f && (_braking || halted));
            _previous = speed;
            return _braking;
        }
    }
}
