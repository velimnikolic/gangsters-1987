using System.Collections.Generic;
using UnityEngine;
using PolyPerfect.City;

namespace LivingCity.Generation
{
    /// <summary>
    /// Finds the places where a road crosses the boundary of the map, so traffic can enter and
    /// leave there instead of materialising in the middle of a street.
    ///
    /// Nothing here is authored. CityGenerator.Subdivide never cuts at index 0 or size-1, so a
    /// street that reaches the boundary runs THROUGH the boundary row or column and is sliced by
    /// it - that is exactly the one-connection case in RoadTileTable, which drops a plain
    /// straight whose lanes run the full z = -15..+15 of the tile. The far half of that tile is
    /// the map's outline, so the lane's first or last checkpoint sits on it. Those checkpoints
    /// are the gates.
    ///
    /// All four sides are reached. The map starts as one rectangle larger than the maximum block
    /// on both axes, so the chain of rectangles containing any given edge keeps being cut until
    /// it is within that maximum - and a cut spans its whole rectangle, so the first one made
    /// across that chain lands a road cell on the edge itself.
    ///
    /// Detection is geometric rather than link-based, and that is deliberate. "A lane whose
    /// nextPaths is empty" identifies the same set and is cheaper to test, but DeadEndStitcher
    /// fills those lists with U-turns during its own Start(), so the signal is gone by the time
    /// VehicleSpawner - which waits a frame for Tile.Start() - gets a chance to look. Measuring
    /// against the outline instead makes this independent of execution order entirely.
    ///
    /// The bounds come from Tile.Tiles rather than from CityBuilder.Grid for the reason already
    /// documented in VehicleSpawner: a city generated in the editor and saved into the scene has
    /// no grid object at runtime, but every Tile has registered itself in OnEnable.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public sealed class MapEdgeGates : MonoBehaviour
    {
        /// <summary>A point on the outline of the map where one traffic lane crosses it.</summary>
        public readonly struct Gate
        {
            public readonly Path Lane;

            /// <summary>Where the lane meets the outline - a lane start for an entry, a lane end for an exit.</summary>
            public readonly Vector3 Point;

            /// <summary>Which way traffic is travelling through the gate. Inward for an entry, outward for an exit.</summary>
            public readonly Vector3 Direction;

            public Gate(Path lane, Vector3 point, Vector3 direction)
            {
                Lane = lane;
                Point = point;
                Direction = direction;
            }

            public bool IsValid => Lane;
        }

        [Tooltip("How close a lane endpoint has to be to the outline of the map to count as a " +
                 "gate. A boundary tile's lane lands on it exactly; the slack is for the " +
                 "crosswalk straight, whose lanes are split into segments. Interior lane " +
                 "endpoints are at least half a cell (15m) inside, so this cannot over-match.")]
        [SerializeField, Min(0f)] float edgeTolerance = 2f;

        [Tooltip("Logs how many gates were found. Expect roughly two per street that reaches " +
                 "the edge - one lane in, one lane out.")]
        [SerializeField] bool logSummary = true;

        readonly List<Gate> entries = new();
        readonly List<Gate> exits = new();

        bool built;
        Vector2 boundsMin;
        Vector2 boundsMax;

        /// <summary>Lanes that start on the outline and head into the city.</summary>
        public IReadOnlyList<Gate> Entries { get { EnsureBuilt(); return entries; } }

        /// <summary>Lanes that end on the outline, heading out of the city.</summary>
        public IReadOnlyList<Gate> Exits { get { EnsureBuilt(); return exits; } }

        public bool HasGates => Entries.Count > 0 && Exits.Count > 0;

        void Start()
        {
            EnsureBuilt();

            if (logSummary)
                Debug.Log($"[MapEdgeGates] {entries.Count} entry and {exits.Count} exit gate(s) " +
                          $"on a map spanning {boundsMin} to {boundsMax}.", this);
        }

        /// <summary>
        /// Built on demand as well as in Start(), so a spawner that resolves this component
        /// early still gets a populated list rather than an empty one.
        /// </summary>
        public void EnsureBuilt()
        {
            if (built)
                return;

            built = true;

            if (!TryMeasureBounds())
                return;

            MeasureSeaSides();

            foreach (var tile in Tile.Tiles)
            {
                if (!tile || tile.paths == null)
                    continue;
                if (tile.tileType != Tile.TileType.Road && tile.tileType != Tile.TileType.RoadAndRail)
                    continue;

                foreach (var lane in tile.paths)
                {
                    if (!lane)
                        continue;

                    if (TryGetLaneEntry(lane, out var start, out var heading)
                        && OnOutline(start) && !OnSeaSide(start) && Crosses(start, heading, inward: true))
                        entries.Add(new Gate(lane, start, heading));

                    if (TryGetLaneExit(lane, out var end, out var leaving)
                        && OnOutline(end) && !OnSeaSide(end) && Crosses(end, leaving, inward: false))
                        exits.Add(new Gate(lane, end, leaving));
                }
            }
        }

        // The map sides that are open water rather than open road - a port city's quay side.
        // A gate there would spawn cars out of the sea and despawn them into it. The streets
        // that reach that outline still exist and still dead-end; DeadEndStitcher gives them
        // the same U-turn every boundary stub gets, so a car that drives down to the water
        // turns around at the quay - which is what a car on a dead-end quay street does.
        bool seaSouth, seaNorth, seaWest, seaEast;

        /// <summary>
        /// Reads the port's quay line off the PortMarker(s) the generator baked. The line lies
        /// exactly on the outline, so which side it is on is a coordinate comparison. Marker
        /// rather than grid for VehicleSpawner's reason: a saved city has no grid at runtime.
        /// </summary>
        void MeasureSeaSides()
        {
            foreach (var port in FindObjectsByType<Entities.PortMarker>())
            {
                var from = port.QuayFrom;
                var to = port.QuayTo;

                if (Mathf.Abs(from.z - to.z) < 0.5f)
                {
                    // Horizontal quay line - the sea is North or South of it.
                    if (Mathf.Abs(from.z - boundsMin.y) <= edgeTolerance) seaSouth = true;
                    if (Mathf.Abs(from.z - boundsMax.y) <= edgeTolerance) seaNorth = true;
                }
                else
                {
                    if (Mathf.Abs(from.x - boundsMin.x) <= edgeTolerance) seaWest = true;
                    if (Mathf.Abs(from.x - boundsMax.x) <= edgeTolerance) seaEast = true;
                }
            }
        }

        bool OnSeaSide(Vector3 point) =>
            (seaSouth && point.z - boundsMin.y <= edgeTolerance)
            || (seaNorth && boundsMax.y - point.z <= edgeTolerance)
            || (seaWest && point.x - boundsMin.x <= edgeTolerance)
            || (seaEast && boundsMax.x - point.x <= edgeTolerance);

        /// <summary>
        /// Picks a gate to bring a car in through, preferring one the player cannot see.
        ///
        /// A car appearing at the boundary is only convincing while the boundary is off screen.
        /// With the 34x33 map and maxOrthoSize 70 the edges are usually outside the frame, so
        /// the hidden-gate branch is the common case; the fallback - the gate furthest from the
        /// centre of the view - only fires when the camera is parked against a map edge.
        /// </summary>
        public bool TryPickEntry(Camera view, System.Random rng, out Gate gate)
        {
            gate = default;
            EnsureBuilt();

            if (entries.Count == 0)
                return false;

            if (!view)
            {
                gate = entries[rng.Next(entries.Count)];
                return true;
            }

            var planes = GeometryUtility.CalculateFrustumPlanes(view);
            var hidden = 0;

            // Reservoir-sample the off-screen gates so no second list has to be allocated
            // every time a car is replaced.
            foreach (var candidate in entries)
            {
                if (GeometryUtility.TestPlanesAABB(planes, new Bounds(candidate.Point, Vector3.one * 4f)))
                    continue;

                hidden++;
                if (rng.Next(hidden) == 0)
                    gate = candidate;
            }

            if (hidden > 0)
                return true;

            var furthest = 0f;
            foreach (var candidate in entries)
            {
                var viewport = view.WorldToViewportPoint(candidate.Point);
                var offset = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f).sqrMagnitude;
                if (offset < furthest && gate.IsValid)
                    continue;

                furthest = offset;
                gate = candidate;
            }

            return gate.IsValid;
        }

        /// <summary>
        /// Picks a gate to send a car out through, biased away from where it is now so the trip
        /// across the city is worth watching. Best-of-four rather than strictly the furthest,
        /// which would send every car in a given corner to the same gate.
        /// </summary>
        public bool TryPickExit(Vector3 from, System.Random rng, out Gate gate)
        {
            gate = default;
            EnsureBuilt();

            if (exits.Count == 0)
                return false;

            var best = -1f;
            for (var draw = 0; draw < 4; draw++)
            {
                var candidate = exits[rng.Next(exits.Count)];
                var distance = (candidate.Point - from).sqrMagnitude;
                if (distance < best)
                    continue;

                best = distance;
                gate = candidate;
            }

            return gate.IsValid;
        }

        /// <summary>Where a lane begins, and the direction it sets off in.</summary>
        public static bool TryGetLaneEntry(Path lane, out Vector3 start, out Vector3 entry)
        {
            start = Vector3.zero;
            entry = Vector3.zero;

            var points = lane.pathPositions;
            if (points == null || points.Count < 2)
                return false;

            var first = points[0];
            var second = points[1];
            if (!first || !second)
                return false;

            start = first.position;
            entry = (second.position - start).normalized;
            return entry.sqrMagnitude > 0.01f;
        }

        /// <summary>Where a lane finishes, and the direction it is travelling as it does.</summary>
        public static bool TryGetLaneExit(Path lane, out Vector3 end, out Vector3 heading)
        {
            end = Vector3.zero;
            heading = Vector3.zero;

            var points = lane.pathPositions;
            if (points == null || points.Count < 2)
                return false;

            var last = points[points.Count - 1];
            var previous = points[points.Count - 2];
            if (!last || !previous)
                return false;

            end = last.position;
            heading = (end - previous.position).normalized;
            return heading.sqrMagnitude > 0.01f;
        }

        /// <summary>
        /// The outline of the map: the extent of the tile centres, pushed out by half a cell to
        /// reach the outer face of the outermost tiles.
        /// </summary>
        bool TryMeasureBounds()
        {
            var found = false;

            foreach (var tile in Tile.Tiles)
            {
                if (!tile)
                    continue;

                var position = tile.transform.position;
                if (!found)
                {
                    boundsMin = boundsMax = new Vector2(position.x, position.z);
                    found = true;
                    continue;
                }

                boundsMin = Vector2.Min(boundsMin, new Vector2(position.x, position.z));
                boundsMax = Vector2.Max(boundsMax, new Vector2(position.x, position.z));
            }

            if (!found)
            {
                Debug.LogWarning("[MapEdgeGates] No tiles in the scene - traffic will fall back " +
                                 "to spawning anywhere on the road network.", this);
                return false;
            }

            var half = Vector2.one * (CityGrid.CellSize * 0.5f);
            boundsMin -= half;
            boundsMax += half;
            return true;
        }

        bool OnOutline(Vector3 point) => DistanceInside(point) <= edgeTolerance;

        /// <summary>
        /// Whether stepping a metre along <paramref name="direction"/> from a point on the
        /// outline takes it off one of the edges it is standing on (inward) or carries it past
        /// one (outward). Probing rather than working out a per-side normal keeps this the same
        /// three lines for all four edges.
        ///
        /// Each edge is tested separately, and NOT via DistanceInside's minimum of the four.
        /// At a corner two edges are both zero away, so the minimum stays zero for a direction
        /// that leaves one of them while running parallel to the other, and a lane entering
        /// there would be read as going nowhere. The road generator never puts a lane on a
        /// corner - the outer ring is deliberately unpaved - but a rule that only holds because
        /// its input never reaches the awkward case is a trap for whoever changes the layout.
        ///
        /// Inward and outward are asked for explicitly rather than one being the negation of
        /// the other, so a lane running ALONG an edge is neither, instead of defaulting to an
        /// exit and quietly draining traffic into it.
        /// </summary>
        bool Crosses(Vector3 point, Vector3 direction, bool inward)
        {
            var probe = point + direction;

            return Moved(point.x - boundsMin.x, probe.x - boundsMin.x, inward)
                || Moved(boundsMax.x - point.x, boundsMax.x - probe.x, inward)
                || Moved(point.z - boundsMin.y, probe.z - boundsMin.y, inward)
                || Moved(boundsMax.y - point.z, boundsMax.y - probe.z, inward);
        }

        /// <summary>Was this one edge both near enough to matter, and left in the right direction?</summary>
        bool Moved(float before, float after, bool inward) =>
            before <= edgeTolerance && (inward ? after > before : after < before);

        /// <summary>How far a point is from the nearest edge of the outline. Negative outside it.</summary>
        float DistanceInside(Vector3 point) =>
            Mathf.Min(point.x - boundsMin.x, boundsMax.x - point.x,
                      point.z - boundsMin.y, boundsMax.y - point.z);

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
                return;

            foreach (var entry in entries)
                DrawGate(entry, Color.green);

            foreach (var exit in exits)
                DrawGate(exit, Color.red);
        }

        static void DrawGate(Gate gate, Color color)
        {
            Gizmos.color = color;
            var tip = gate.Point + gate.Direction * 4f;
            Gizmos.DrawLine(gate.Point, tip);
            Gizmos.DrawWireSphere(gate.Point, 0.6f);
            Gizmos.DrawLine(tip, tip - Quaternion.Euler(0f, 25f, 0f) * gate.Direction * 1.5f);
            Gizmos.DrawLine(tip, tip - Quaternion.Euler(0f, -25f, 0f) * gate.Direction * 1.5f);
        }
#endif
    }
}
