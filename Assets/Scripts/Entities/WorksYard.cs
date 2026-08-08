using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// One hall that actually stood, in the terms the yard dressing needs.
    ///
    /// <see cref="Outward"/> IS the dock-face normal, with no derivation: IndustrialDresser
    /// yaws the hall by Atan2(Outward.x, Outward.z) so its local +Z - its front, where the
    /// doors are - points along it. The face plane centre is Centre + Outward * HalfDepth.
    /// </summary>
    [System.Serializable]
    public struct WorksHall
    {
        /// <summary>Geometric centre of the placed footprint, at ground level.</summary>
        public Vector3 Centre;

        /// <summary>Unit, axis-aligned, pointing at the service road that serves this hall.</summary>
        public Vector3 Outward;

        /// <summary>Half extent across the front, along Cross(up, Outward).</summary>
        public float HalfWidth;

        /// <summary>Half extent front to back, along Outward.</summary>
        public float HalfDepth;

        /// <summary>The pad this hall stands on, so the apron can be clipped to it.</summary>
        public Vector2 PadMin;
        public Vector2 PadMax;
    }

    /// <summary>
    /// Data marker on a works compound: the halls that actually stood, the gate, and the zones
    /// the yard was partitioned into. Attached by IndustrialDresser at generation and read
    /// afterwards by IndustrialLotBuilder, by the scene-view gizmos, and eventually by the
    /// runtime director that staffs the yard.
    ///
    /// This exists because of one thing that cannot be re-derived. IndustrialLayout.ForBlock is
    /// deterministic in (seed, blockId) alone and freely re-callable - GroundPlacer already
    /// replays it to lay asphalt on the same carriageways - so the wall, the roads, the pads and
    /// the gate need no publishing at all. The HALLS do: BuildHalls draws its prefab through
    /// BlockBuilder's shared Buildings stream, after an unknown number of other blocks' draws,
    /// and nothing can replay that. So only the halls and what is computed from them travel here.
    ///
    /// A marker rather than a static registry, for the same reason PoliceStation is one: the city
    /// is generated in the editor and SAVED, so a static list is empty on the next domain reload
    /// and dead by the time anything plays.
    ///
    /// Stored in WORLD space, which is the one deliberate departure from PoliceStation - that
    /// marker keeps locals because it rides a building someone may nudge in the inspector. This
    /// one rides an empty the generator owns, parented at the wall-rect centre with identity
    /// rotation, and nothing ever moves it. Locals here would be ceremony that has to be undone
    /// at every read.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorksYard : MonoBehaviour
    {
        [SerializeField] int blockId;
        [SerializeField] Vector2 wallMin;
        [SerializeField] Vector2 wallMax;
        [SerializeField] bool hasGate;
        [SerializeField] Vector3 gateCentre;
        [SerializeField] Vector3 gateOutward;
        [SerializeField] WorksHall[] halls = System.Array.Empty<WorksHall>();

        [Tooltip("Filled by IndustrialLotPlanner. Rides into the saved scene so the dressing " +
                 "pass and the gizmos read the same partition.")]
        [SerializeField] LotZone[] zones = System.Array.Empty<LotZone>();

        [Tooltip("Ground that must stay clear of props and colliders: the carriageways plus " +
                 "the gate corridor. Published for a future NavMesh bake and for the tests.")]
        [SerializeField] IndustrialLayout.Rect[] lanes = System.Array.Empty<IndustrialLayout.Rect>();

        [Tooltip("Ground the vehicles own - staff bays and the lorry stand. Kept because it is " +
                 "the one part of the compound that cannot be rebuilt from the wall rect alone.")]
        [SerializeField] IndustrialLayout.Rect[] bays = System.Array.Empty<IndustrialLayout.Rect>();

        public int BlockId => blockId;
        public Vector2 WallMin => wallMin;
        public Vector2 WallMax => wallMax;
        public bool HasGate => hasGate;
        public Vector3 GateCentre => gateCentre;
        public Vector3 GateOutward => gateOutward;
        public WorksHall[] Halls => halls;
        public LotZone[] Zones => zones;
        public IndustrialLayout.Rect[] Lanes => lanes;
        public IndustrialLayout.Rect[] Bays => bays;

        /// <summary>The wall line, as the rect type the rest of the industrial code speaks.</summary>
        public IndustrialLayout.Rect Wall =>
            new IndustrialLayout.Rect { Min = wallMin, Max = wallMax };

        public void SetCompound(
            int block, IndustrialLayout.Rect wall, bool gate, Vector3 centre, Vector3 outward,
            WorksHall[] placedHalls, IndustrialLayout.Rect[] clearLanes,
            IndustrialLayout.Rect[] vehicleBays)
        {
            bays = vehicleBays ?? System.Array.Empty<IndustrialLayout.Rect>();
            blockId = block;
            wallMin = wall.Min;
            wallMax = wall.Max;
            hasGate = gate;
            gateCentre = centre;
            gateOutward = outward;
            halls = placedHalls ?? System.Array.Empty<WorksHall>();
            lanes = clearLanes ?? System.Array.Empty<IndustrialLayout.Rect>();
        }

        public void SetZones(LotZone[] planned) => zones = planned ?? System.Array.Empty<LotZone>();

        /// <summary>Where a lorry backs up to: the front face of the hall, on the ground.</summary>
        public Vector3 DockFace(int i) => halls[i].Centre + halls[i].Outward * halls[i].HalfDepth;

#if UNITY_EDITOR
        [Header("Gizmos")]
        [SerializeField] bool drawWhenNotSelected = true;

        [Tooltip("Skip drawing beyond this distance from the scene camera. A works-heavy city " +
                 "is a few hundred rects and there is no reason to draw the far ones.")]
        [SerializeField, Min(0f)] float maxDrawDistance = 220f;

        void OnDrawGizmos()
        {
            if (drawWhenNotSelected)
                Draw();
        }

        void OnDrawGizmosSelected()
        {
            if (!drawWhenNotSelected)
                Draw();
        }

        void Draw()
        {
            var cam = UnityEditor.SceneView.lastActiveSceneView
                ? UnityEditor.SceneView.lastActiveSceneView.camera
                : null;

            if (cam && maxDrawDistance > 0f &&
                Vector3.Distance(cam.transform.position, transform.position) > maxDrawDistance)
                return;

            // The wall line, so the partition can be read against the ground it had to fit in.
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            DrawRect(wallMin, wallMax, 0.2f);

            // Lanes first and underneath: everything else is judged by whether it stays off them.
            Gizmos.color = new Color(0.30f, 0.60f, 1f, 0.9f);
            if (lanes != null)
                foreach (var lane in lanes)
                    DrawRect(lane.Min, lane.Max, 0.25f);

            if (halls != null)
            {
                foreach (var hall in halls)
                {
                    var lateral = Vector3.Cross(Vector3.up, hall.Outward);
                    var front = hall.Centre + hall.Outward * hall.HalfDepth;

                    Gizmos.color = new Color(0.85f, 0.85f, 0.85f, 0.8f);
                    DrawRect(hall.PadMin, hall.PadMax, 0.15f);

                    // The dock face, and an arrow off it along the door normal. If these ever
                    // point at a wall instead of a carriageway, the apron is on the wrong side.
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawLine(front + lateral * hall.HalfWidth + Vector3.up * 0.3f,
                                    front - lateral * hall.HalfWidth + Vector3.up * 0.3f);
                    Gizmos.DrawLine(front + Vector3.up * 0.3f,
                                    front + hall.Outward * 2.5f + Vector3.up * 0.3f);
                }
            }

            if (zones != null)
            {
                foreach (var zone in zones)
                {
                    Gizmos.color = ColourFor(zone.Kind);
                    DrawRect(zone.Area.Min, zone.Area.Max, 0.35f);

                    if (zone.Facing.sqrMagnitude > 0.5f)
                    {
                        var c = new Vector3(zone.Area.Centre.x, 0.35f, zone.Area.Centre.y);
                        Gizmos.DrawLine(c, c + zone.Facing * 2f);
                    }
                }
            }

            if (hasGate)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(gateCentre + Vector3.up * 0.4f,
                                gateCentre + gateOutward * 4f + Vector3.up * 0.4f);
            }
        }

        /// <summary>
        /// Matches the surface each zone gets in the ground pass, so the gizmo view and the built
        /// scene read as the same partition rather than as two unrelated diagrams.
        /// </summary>
        static Color ColourFor(LotZoneKind kind) => kind switch
        {
            LotZoneKind.LoadingApron => new Color(0.80f, 0.80f, 0.78f),   // concrete
            LotZoneKind.TruckStaging => new Color(0.60f, 0.65f, 0.70f),   // concrete, cooler
            LotZoneKind.RawMaterialYard => new Color(0.66f, 0.48f, 0.28f),// packed dirt
            LotZoneKind.BoilerHouse => new Color(0.85f, 0.35f, 0.20f),    // brick
            LotZoneKind.CinderYard => new Color(0.30f, 0.28f, 0.26f),     // ash
            LotZoneKind.ScrapCorner => new Color(0.45f, 0.35f, 0.45f),
            _ => Color.magenta,
        };

        static void DrawRect(Vector2 min, Vector2 max, float y)
        {
            var a = new Vector3(min.x, y, min.y);
            var b = new Vector3(max.x, y, min.y);
            var c = new Vector3(max.x, y, max.y);
            var d = new Vector3(min.x, y, max.y);

            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }
#endif
    }
}
