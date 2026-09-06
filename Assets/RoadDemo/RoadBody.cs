using UnityEngine;

namespace RoadDemo
{
    /// <summary>Publishes a body driven outside RoadCar to the shared road and collision registries.</summary>
    public sealed class RoadBody : System.IDisposable
    {
        readonly IRoadUser _who;
        readonly RoadOccupant _occupant;
        bool _registered;
        public RoadBody(IRoadUser who) { _who = who; _occupant = new RoadOccupant { Who = who }; }

        public void Sync(LaneNet net, float dt)
        {
            if (!_registered) { StreetTraffic.Users.Add(_who); _registered = true; RoadSpace.Invalidate(); }
            var road = net?.Locate(_who.RoadPosition, out _, out _, within: 2f);
            if (road != _occupant.Road)
            {
                _occupant.Road?.Occupants.Remove(_occupant);
                _occupant.Road = road;
                road?.Occupants.Add(_occupant);
            }
            if (road == null) return;
            float oldSpeed = Mathf.Abs(_occupant.Vel);
            Project(_occupant);
            _occupant.Slowing = dt > 0f ? Mathf.Max(0f, (oldSpeed - Mathf.Abs(_occupant.Vel)) / dt) : 0f;
        }

        public void Dispose()
        {
            if (_registered) { StreetTraffic.Users.Remove(_who); RoadSpace.Invalidate(); }
            _registered = false;
            _occupant.Road?.Occupants.Remove(_occupant);
            _occupant.Road = null;
            _occupant.Vel = 0f;
        }

        /// <summary>The same road-frame projection for a parked prop and a moving external body.</summary>
        public static void Project(RoadOccupant o)
        {
            var road = o.Road; var who = o.Who;
            road.Project(who.RoadPosition, out float s, out float d);
            float alongFacing = Vector3.Dot(who.RoadForward, road.DirAt(s));
            float acrossFacing = Vector3.Dot(who.RoadForward, road.RightAt(s));
            float along = Mathf.Abs(alongFacing) * who.HalfLength + Mathf.Abs(acrossFacing) * who.HalfWidth;
            float across = Mathf.Abs(acrossFacing) * who.HalfLength + Mathf.Abs(alongFacing) * who.HalfWidth;
            o.BodyS0 = o.S0 = s - along; o.BodyS1 = o.S1 = s + along;
            o.BodyD0 = o.D0 = d - across; o.BodyD1 = o.D1 = d + across;
            o.Heading = Mathf.Abs(alongFacing) < .1f ? 0 : alongFacing > 0f ? 1 : -1;
            o.Vel = who.RoadSpeed * alongFacing;
            o.Parked = Mathf.Abs(who.RoadSpeed) < .05f;
            foreach (var lane in road.Lanes)
                if (o.BodyD0 < lane.Offset + 1.25f && o.BodyD1 > lane.Offset - 1.25f) { o.Parked = false; break; }
        }

        public float SpeedLimit(float brake, float gap, float cap = 7f)
        {
            float clear = float.PositiveInfinity;
            var forward = _who.RoadForward; var right = new Vector3(forward.z, 0f, -forward.x);
            float reach = _who.HalfLength + gap + cap * cap / (2f * brake);
            foreach (var other in RoadSpace.Nearby(_who.RoadPosition, reach))
            {
                if (ReferenceEquals(other, _who)) continue;
                var delta = other.RoadPosition - _who.RoadPosition;
                if (Mathf.Abs(delta.y) > RoadSpace.Storey) continue;
                float ahead = Vector3.Dot(delta, forward);
                float cosine = Mathf.Abs(Vector3.Dot(other.RoadForward, forward));
                float sine = Mathf.Abs(Vector3.Dot(other.RoadForward, right));
                float width = sine * other.HalfLength + cosine * other.HalfWidth;
                if (ahead <= 0f || Mathf.Abs(Vector3.Dot(delta, right)) >= _who.HalfWidth + width + .3f) continue;
                float length = cosine * other.HalfLength + sine * other.HalfWidth;
                clear = Mathf.Min(clear, ahead - _who.HalfLength - length - gap);
            }
            return Mathf.Min(cap, Mathf.Sqrt(2f * brake * Mathf.Max(0f, clear)));
        }
    }
}
