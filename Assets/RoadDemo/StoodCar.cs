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
        readonly Transform _tf;
        readonly float _halfLength, _halfWidth;

        StoodCar(Transform tf, float halfLength, float halfWidth)
        {
            _tf = tf;
            _halfLength = halfLength;
            _halfWidth = halfWidth;
        }

        /// <summary>Stand this body on the road, measured off its own renderers (it is
        /// laid down nose along the street, so the box its meshes fill is its size), and
        /// put it among the road's users.</summary>
        public static StoodCar Park(GameObject car)
        {
            if (car == null) return null;
            var renderers = car.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return null;
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            // the body as it stands: along its nose, and across it
            var f = car.transform.forward;
            float halfLength = Mathf.Abs(f.z) * b.extents.z + Mathf.Abs(f.x) * b.extents.x;
            float halfWidth = Mathf.Abs(f.z) * b.extents.x + Mathf.Abs(f.x) * b.extents.z;
            var stood = new StoodCar(car.transform, Mathf.Max(1f, halfLength), Mathf.Max(0.7f, halfWidth));
            StreetTraffic.Users.Add(stood);
            // and on the road's own books (LaneNet): the lane's traffic plans round it
            stood._occupant = LaneNet.Active?.AddStatic(stood);
            return stood;
        }

        RoadOccupant _occupant;

        public void Forget()
        {
            StreetTraffic.Users.Remove(this);
            if (_occupant != null) { LaneNet.Active?.Remove(_occupant); _occupant = null; }
        }

        public Vector3 RoadPosition => _tf ? _tf.position : Vector3.zero;
        public Vector3 RoadForward => _tf ? _tf.forward : Vector3.forward;
        public float RoadSpeed => 0f;
        public float HalfLength => _halfLength;
        public float HalfWidth => _halfWidth;
    }
}
