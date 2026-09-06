using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Continuous, known kerbs in the city's model coordinates. Generated
    /// residential ground supplies driveway cuts; parks have no vehicle accesses.
    /// Authored blocks without an access plan are left clear.</summary>
    public sealed class CoreParkingFrontage
    {
        readonly CoreRoads.Raster _raster;
        readonly List<(Rect Bounds, ResidentialLot.Plan Plan)> _fronts =
            new List<(Rect, ResidentialLot.Plan)>();

        public CoreParkingFrontage(CoreRoads.Raster raster) { _raster = raster; }

        public void Add(Rect bounds, ResidentialLot.Plan plan = null) => _fronts.Add((bounds, plan));

        /// <summary>The complete frontage behind the car, with the caller's entry
        /// clearance already included. Reads no scene objects or streamed views.</summary>
        public bool Allows(Vector3 first, Vector3 last)
        {
            if (_raster == null) return false;
            var delta = last - first;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(delta.x) + Mathf.Abs(delta.z)));
            for (int n = 0; n <= steps; n++)
            {
                var local = first + delta * ((float)n / steps);
                int i = Mathf.FloorToInt((local.x - _raster.X0) / CoreRoads.Cell);
                int j = Mathf.FloorToInt((local.z - _raster.Z0) / CoreRoads.Cell);
                if (_raster.At(i, j) != CoreRoads.Kind.Block) return false;
                bool known = false;
                foreach (var front in _fronts)
                {
                    if (!front.Bounds.Contains(new Vector2(local.x, local.z))) continue;
                    known = true;
                    if (front.Plan == null) continue;
                    int x = Mathf.FloorToInt((local.x - front.Bounds.xMin) / ResidentialLot.Cell);
                    int z = Mathf.FloorToInt((local.z - front.Bounds.yMin) / ResidentialLot.Cell);
                    if (x < 0 || z < 0 || x >= front.Plan.W || z >= front.Plan.D ||
                        ResidentialLot.Drives(front.Plan.Ground[x, z])) return false;
                }
                if (!known) return false;
            }
            return true;
        }
    }
}
