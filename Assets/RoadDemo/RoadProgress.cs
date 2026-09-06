using UnityEngine;

namespace RoadDemo
{
    /// <summary>Tracks physical trip progress while allowing planned loading waits.</summary>
    public sealed class RoadProgress
    {
        readonly float _limit, _distance;
        Vector3 _anchor;
        float _elapsed;
        public RoadProgress(float limit, float distance) { _limit = limit; _distance = distance; }
        public void Reset(Vector3 position) { _anchor = position; _elapsed = 0f; }
        public bool Stalled(float dt, Vector3 position, bool holding = false)
        {
            if (holding || (position - _anchor).sqrMagnitude >= _distance * _distance) Reset(position);
            else _elapsed += Mathf.Max(0f, dt);
            return _elapsed > _limit;
        }
    }
}
