using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Incremental steering search inside a parking lot, including reverse gears.</summary>
    sealed class ParkingManeuver
    {
        internal readonly struct Pose
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public Pose(Vector3 position, Quaternion rotation) { Position = position; Rotation = rotation; }
        }

        readonly struct Node
        {
            public readonly float X, Z, Yaw, Cost;
            public readonly int Parent, Direction;
            public Node(float x, float z, float yaw, float cost, int parent, int direction)
            { X = x; Z = z; Yaw = yaw; Cost = cost; Parent = parent; Direction = direction; }
        }

        readonly struct Body
        {
            public readonly Vector3 Position, Forward;
            public readonly float HalfLength, HalfWidth;
            public Body(Vector3 position, Vector3 forward, float halfLength, float halfWidth)
            { Position = position; Forward = forward; HalfLength = halfLength; HalfWidth = halfWidth; }
        }

        const float Travel = 0.5f;
        const float TurnRadius = 3f;
        const int MaxExpansions = 80000;
        readonly ParkingBlockSite _site;
        readonly float _halfLength, _halfWidth, _goalYaw;
        readonly Vector3 _goal;
        readonly List<Body> _bodies = new List<Body>();
        readonly List<Node> _nodes = new List<Node>();
        readonly Dictionary<long, float> _best = new Dictionary<long, float>();
        readonly WalkHeap _open = new WalkHeap();
        int _expanded;

        public bool Finished { get; private set; }
        public bool Found { get; private set; }
        public readonly List<Pose> Path = new List<Pose>();

        public ParkingManeuver(ParkingBlockSite site, IReadOnlyList<ParkingCar> cars, ParkingCar self,
            Vector3 from, Vector3 forward, Vector3 goal, Vector3 goalForward)
        {
            _site = site;
            _halfLength = self.HalfLen;
            _halfWidth = self.HalfWide;
            _goal = site.Root.InverseTransformPoint(goal);
            var endWay = site.Root.InverseTransformDirection(goalForward);
            _goalYaw = Mathf.Atan2(endWay.x, endWay.z);
            foreach (var car in cars)
            {
                if (car == self || car.Gone || car.Tf == null || car.State != ParkingCar.Mode.Parked) continue;
                _bodies.Add(new Body(site.Root.InverseTransformPoint(car.Position),
                    site.Root.InverseTransformDirection(car.RoadForward), car.HalfLen, car.HalfWide));
            }
            foreach (var box in site.Plan.Exclusions)
                _bodies.Add(new Body(new Vector3(box.center.x, 0f, box.center.y),
                    Vector3.forward, box.height * 0.5f, box.width * 0.5f));
            var start = site.Root.InverseTransformPoint(from);
            var way = site.Root.InverseTransformDirection(forward);
            float yaw = Mathf.Atan2(way.x, way.z);
            if (!Clear(start.x, start.z, yaw) || !Clear(_goal.x, _goal.z, _goalYaw))
            { Finished = true; return; }
            Add(start.x, start.z, yaw, 0f, -1, 1);
        }

        public void Step(int budget)
        {
            if (Finished) return;
            while (_open.Count != 0 && budget-- > 0)
            {
                int index = _open.Pop();
                var node = _nodes[index];
                if (_best[Key(node.X, node.Z, node.Yaw, node.Direction)] < node.Cost) continue;
                if (++_expanded > MaxExpansions) break;
                if (Vector2.Distance(new Vector2(node.X, node.Z), new Vector2(_goal.x, _goal.z)) < 0.35f &&
                    Mathf.Abs(Angle(node.Yaw, _goalYaw)) < 0.12f && GoalClear(node))
                { Complete(index); return; }
                for (int direction = -1; direction <= 1; direction += 2)
                    for (int steering = -1; steering <= 1; steering++)
                        Expand(node, index, direction, steering);
            }
            if (_open.Count == 0 || _expanded > MaxExpansions) Finished = true;
        }

        void Expand(Node node, int parent, int direction, int steering)
        {
            float distance = Travel * direction, curvature = steering / TurnRadius;
            float x = node.X, z = node.Z, yaw = node.Yaw;
            for (int i = 1; i <= 3; i++)
            {
                float travel = distance * i / 3f;
                yaw = node.Yaw + travel * curvature;
                x = node.X + (steering == 0 ? travel * Mathf.Sin(node.Yaw)
                    : (Mathf.Cos(node.Yaw) - Mathf.Cos(yaw)) / curvature);
                z = node.Z + (steering == 0 ? travel * Mathf.Cos(node.Yaw)
                    : (Mathf.Sin(yaw) - Mathf.Sin(node.Yaw)) / curvature);
                if (!Clear(x, z, yaw)) return;
            }
            // Playback interpolates position and rotation along each chord. Its
            // interior can be closer to a neighbour than either steering endpoint.
            for (int i = 1; i < 10; i++)
            {
                float fraction = i / 10f;
                if (!Clear(Mathf.Lerp(node.X, x, fraction), Mathf.Lerp(node.Z, z, fraction),
                    Mathf.Lerp(node.Yaw, yaw, fraction))) return;
            }
            float cost = node.Cost + Travel * (direction > 0 ? 1f : 1.15f) +
                (direction != node.Direction ? 1.5f : 0f);
            Add(x, z, yaw, cost, parent, direction);
        }

        void Add(float x, float z, float yaw, float cost, int parent, int direction)
        {
            long key = Key(x, z, yaw, direction);
            if (_best.TryGetValue(key, out float known) && known <= cost) return;
            _best[key] = cost;
            int index = _nodes.Count;
            _nodes.Add(new Node(x, z, yaw, cost, parent, direction));
            float remaining = Vector2.Distance(new Vector2(x, z), new Vector2(_goal.x, _goal.z)) +
                Mathf.Abs(Angle(yaw, _goalYaw)) * 1.5f;
            _open.Push(index, cost + remaining * 1.15f);
        }

        bool Clear(float x, float z, float yaw)
        {
            float sx = Mathf.Sin(yaw), cz = Mathf.Cos(yaw);
            float across = _halfLength * Mathf.Abs(sx) + _halfWidth * Mathf.Abs(cz);
            float along = _halfLength * Mathf.Abs(cz) + _halfWidth * Mathf.Abs(sx);
            if (x - across < 0.1f || x + across > _site.Plan.Width - 0.1f ||
                z + along > _site.Plan.Depth - 0.1f) return false;
            // Only the gate opening admits a nose beyond the lot's southern edge.
            if (z - along < 0.1f && (z - along < -5f ||
                Mathf.Abs(x - _site.Plan.Width * 0.5f) + across > ParkingBlockPlan.GateWidth * 0.5f - 0.1f)) return false;
            var position = new Vector3(x, 0f, z);
            var forward = new Vector3(sx, 0f, cz);
            foreach (var body in _bodies)
                if (RoadSpace.Overlap(position, forward, _halfLength, _halfWidth,
                    body.Position, body.Forward, body.HalfLength, body.HalfWidth, 0.16f, out _)) return false;
            return true;
        }

        bool GoalClear(Node node)
        {
            for (int i = 1; i <= 5; i++)
            {
                float t = i / 5f;
                if (!Clear(Mathf.Lerp(node.X, _goal.x, t), Mathf.Lerp(node.Z, _goal.z, t),
                    node.Yaw + Angle(node.Yaw, _goalYaw) * t)) return false;
            }
            return true;
        }

        void Complete(int index)
        {
            for (int i = index; i >= 0; i = _nodes[i].Parent)
            {
                var node = _nodes[i];
                Path.Add(WorldPose(node.X, node.Z, node.Yaw));
            }
            Path.Reverse();
            Path.Add(WorldPose(_goal.x, _goal.z, _goalYaw));
            Found = Finished = true;
        }

        Pose WorldPose(float x, float z, float yaw) => new Pose(
            _site.Root.TransformPoint(new Vector3(x, 0f, z)),
            _site.Root.rotation * Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f));

        static float Angle(float from, float to) =>
            Mathf.DeltaAngle(from * Mathf.Rad2Deg, to * Mathf.Rad2Deg) * Mathf.Deg2Rad;

        static long Key(float x, float z, float yaw, int direction)
        {
            int ix = Mathf.RoundToInt(x / 0.4f), iz = Mathf.RoundToInt(z / 0.4f);
            int angle = (Mathf.RoundToInt(yaw * Mathf.Rad2Deg / 10f) % 36 + 36) % 36;
            return ((((long)(ix + 100) * 1000 + iz + 100) * 36 + angle) * 2 + (direction > 0 ? 1 : 0));
        }
    }
}
