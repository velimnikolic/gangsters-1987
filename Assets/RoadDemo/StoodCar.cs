using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A car left standing on the road that nobody will ever drive - one at a kerb for
    /// the look of the street. Nothing ticks it; it is here so that everybody else has
    /// to go ROUND it: it stands in the same list the moving cars are read from
    /// (StreetTraffic.Users), which is what a driver plans against (CrewCar.ClearAhead,
    /// GatherObstacles) and what the belt keeps bodies out of (RoadSpace).
    ///
    /// Whoever puts one down keeps hold of nothing: drop the transform and it goes with
    /// the scene. <see cref="Forget"/> takes it out of the list again if a scene ever
    /// wants its kerb back.
    /// </summary>
    public sealed class StoodCar : IRoadUser
    {
        static readonly System.Collections.Generic.List<StoodCar> Registered =
            new System.Collections.Generic.List<StoodCar>();

        public static System.Collections.Generic.IReadOnlyList<StoodCar> All => Registered;

        readonly Transform _tf;
        readonly float _halfLength, _halfWidth;

        StoodCar(Transform tf, float halfLength, float halfWidth)
        {
            _tf = tf;
            _halfLength = halfLength;
            _halfWidth = halfWidth;
            Registered.Add(this);
        }

        public Transform Tf => _tf;

        /// <summary>Stand this body on the road with the same traffic footprint as a
        /// moving car, and put it among the road's users.</summary>
        public static StoodCar Park(GameObject car, LaneNet net = null)
        {
            if (car == null) return null;
            // An owned car becoming a roadblock already has a measured body. Its
            // passengers, open doors and attached effects must not permanently resize
            // it just because it switched from driving to standing. These dimensions
            // already include TrafficFootprintScale; do not scale them a second time.
            float halfLength = 0f, halfWidth = 0f;
            foreach (var moving in RoadCar.All)
                if (moving.Tf == car.transform)
                {
                    halfLength = moving.HalfLength;
                    halfWidth = moving.HalfWidth;
                    break;
                }
            if (halfLength <= 0f && !CarBody.MeasureTrafficFootprint(car.transform, out halfLength, out halfWidth))
                return null;
            var stood = new StoodCar(car.transform, halfLength, halfWidth);
            StreetTraffic.Users.Add(stood);
            // and on the road's own books (LaneNet): the lane's traffic plans round it
            stood._occupant = (net ?? LaneNet.Active)?.AddStatic(stood);
            return stood;
        }

        RoadOccupant _occupant;

        public void Forget()
        {
            StreetTraffic.Users.Remove(this);
            Registered.Remove(this);
            if (_occupant != null) { _occupant.Road?.Net?.Remove(_occupant); _occupant = null; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistered() => Registered.Clear();

        public Vector3 RoadPosition => _tf ? _tf.position : Vector3.zero;
        public Vector3 RoadForward => _tf ? _tf.forward : Vector3.forward;
        public float RoadSpeed => 0f;
        public float HalfLength => _halfLength;
        public float HalfWidth => _halfWidth;
    }
}
