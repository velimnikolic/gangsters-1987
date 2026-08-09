using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Data marker on the port block: the compound, the gate, and the walk graph the shift
    /// moves on. Attached by PortDresser at generation, found by PortDirector at play - the
    /// same find-or-stand-down contract SchoolMarker holds with its bus director, because a
    /// chance-based zone means some seeds simply have no port.
    ///
    /// A marker rather than a registry, and world-space rather than local, for exactly the
    /// reasons WorksYard documents - it rides a generator-owned empty that nothing ever moves.
    ///
    /// What travels here and what does not follows the WorksYard rule: PortLayout.ForBlock is
    /// deterministic in (seed, blockId) and freely re-callable, so the quay, the water and the
    /// stacks need no publishing. The WORK POINTS travel anyway, against that rule's letter but
    /// with its grain: they are cheap, the director is the only consumer, and shipping them
    /// spares the runtime a Generation dependency it otherwise never needs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortMarker : MonoBehaviour
    {
        /// <summary>The two lanes of the walk graph. Values are serialized - append only.</summary>
        public enum WorkLane
        {
            Quay,
            Apron,
        }

        /// <summary>One place a docker stands and works, on one of the two lanes.</summary>
        [System.Serializable]
        public struct WorkPoint
        {
            public Vector3 Position;
            public WorkLane Lane;
        }

        [SerializeField] int blockId;
        [SerializeField] Vector2 wallMin;
        [SerializeField] Vector2 wallMax;
        [SerializeField] bool hasGate;
        [SerializeField] Vector3 gateCentre;
        [SerializeField] Vector3 gateOutward;

        [Tooltip("The water's edge, on the map outline: the coping line the quay works along.")]
        [SerializeField] Vector3 quayFrom;
        [SerializeField] Vector3 quayTo;

        [Tooltip("The berth: where a cargo ship lies alongside, in the water. PortShipDirector " +
                 "sails its live ships to exactly here; the baked preview ship (named " +
                 "port_ship_preview_*) stands here in the editor and is replaced at Play.")]
        [SerializeField] bool hasBerth;
        [SerializeField] Vector3 berthCentre;
        [SerializeField] float berthYaw;

        [Tooltip("The walk graph: points on two lanes parallel to the quay. Straight lines " +
                 "WITHIN a lane are clear by construction - PortLayout put the stacks between " +
                 "the lanes - and crossing between lanes is legal only at the aisle, which is " +
                 "what Route() encodes.")]
        [SerializeField] WorkPoint[] workPoints = System.Array.Empty<WorkPoint>();

        [Tooltip("True when the lanes run along world X. With the aisle coordinate and the two " +
                 "lane lines, this is the whole routing table.")]
        [SerializeField] bool lanesAlongX;
        [SerializeField] float quayLaneC;
        [SerializeField] float apronLaneC;
        [SerializeField] float aisleA;

        public int BlockId => blockId;
        public Vector2 WallMin => wallMin;
        public Vector2 WallMax => wallMax;
        public bool HasGate => hasGate;
        public Vector3 GateCentre => gateCentre;
        public Vector3 GateOutward => gateOutward;
        public Vector3 QuayFrom => quayFrom;
        public Vector3 QuayTo => quayTo;
        public bool HasBerth => hasBerth;
        public Vector3 BerthCentre => berthCentre;
        public float BerthYaw => berthYaw;
        public WorkPoint[] WorkPoints => workPoints;

        /// <summary>Unit vector along the coping, low end to high.</summary>
        public Vector3 AlongQuay => (quayTo - quayFrom).normalized;

        /// <summary>Unit vector from the coping out over the water, via the berth.</summary>
        public Vector3 Seaward
        {
            get
            {
                var toBerth = berthCentre - quayFrom;
                var along = AlongQuay;
                var lateral = toBerth - along * Vector3.Dot(toBerth, along);
                lateral.y = 0f;
                return lateral.sqrMagnitude > 1e-4f ? lateral.normalized : Vector3.forward;
            }
        }

        public void SetBerth(bool has, Vector3 centre, float yaw)
        {
            hasBerth = has;
            berthCentre = centre;
            berthYaw = yaw;
        }

        public void SetCompound(
            int block, IndustrialLayout.Rect wall, bool gate, Vector3 centre, Vector3 outward,
            Vector3 quayA, Vector3 quayB,
            WorkPoint[] points, bool alongX, float quayLane, float apronLane, float aisle)
        {
            blockId = block;
            wallMin = wall.Min;
            wallMax = wall.Max;
            hasGate = gate;
            gateCentre = centre;
            gateOutward = outward;
            quayFrom = quayA;
            quayTo = quayB;
            workPoints = points ?? System.Array.Empty<WorkPoint>();
            lanesAlongX = alongX;
            quayLaneC = quayLane;
            apronLaneC = apronLane;
            aisleA = aisle;
        }

        /// <summary>
        /// The waypoints from one work point to another, appended to <paramref name="path"/>
        /// (the destination included, the start not). Same lane: one straight leg. Different
        /// lanes: through the aisle, two corners - the only crossing PortLayout kept clear of
        /// containers. No search, because the graph was built so none is needed.
        /// </summary>
        public void Route(WorkPoint from, WorkPoint to, List<Vector3> path)
        {
            if (from.Lane != to.Lane)
            {
                path.Add(OnLane(aisleA, from.Lane));
                path.Add(OnLane(aisleA, to.Lane));
            }

            path.Add(to.Position);
        }

        Vector3 OnLane(float a, WorkLane lane)
        {
            var c = lane == WorkLane.Quay ? quayLaneC : apronLaneC;
            return lanesAlongX ? new Vector3(a, 0f, c) : new Vector3(c, 0f, a);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.35f, 0.65f, 1f, 0.9f);
            Gizmos.DrawLine(quayFrom, quayTo);

            foreach (var point in workPoints)
            {
                Gizmos.color = point.Lane == WorkLane.Quay
                    ? new Color(0.35f, 0.65f, 1f, 0.9f)
                    : new Color(1f, 0.8f, 0.3f, 0.9f);
                Gizmos.DrawWireSphere(point.Position + Vector3.up * 0.2f, 0.4f);
            }

            if (hasGate)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawLine(gateCentre, gateCentre + gateOutward * 4f);
            }
        }
#endif
    }
}
