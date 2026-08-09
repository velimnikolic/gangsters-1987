using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// The park's plan, published onto the generated scene - what WorksYard is to a works
    /// compound. A marker rather than a static registry for the reason that class gives: the
    /// city is generated in the editor and SAVED, so anything the gizmos or a future director
    /// need again has to survive a domain reload as serialized state.
    ///
    /// Strictly speaking most of this is re-derivable - ParkLayout.ForBlock is deterministic in
    /// (seed, blockId) - but the gizmos would then need the grid, the palette and the config
    /// wired through to draw a rectangle, and the first thing anyone does with a wrong-looking
    /// park is select it and look. Stored in WORLD space on an empty parented at the interior
    /// centre with identity rotation, and nothing ever moves it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ParkGrounds : MonoBehaviour
    {
        /// <summary>One spine's polyline - a wrapper because Unity will not serialize Vector2[][].</summary>
        [System.Serializable]
        public struct SpinePath
        {
            public Vector2[] Points;
            public float Width;
            public ParkLayout.SpineKind Kind;
        }

        [SerializeField] int blockId;
        [SerializeField] ParkLayout.Archetype archetype;
        [SerializeField] Vector2 interiorMin;
        [SerializeField] Vector2 interiorMax;
        [SerializeField] Vector2 plazaCentre;
        [SerializeField] float plazaRadius;
        [SerializeField] SpinePath[] spines;
        [SerializeField] ParkLayout.Entrance[] entrances;
        [SerializeField] ParkLayout.Zone[] zones;
        [SerializeField] ParkLayout.Station[] stations;

        public int BlockId => blockId;
        public ParkLayout.Archetype Archetype => archetype;

        public void SetPlan(int block, ParkLayout.Plan plan)
        {
            blockId = block;
            archetype = plan.Archetype;
            interiorMin = plan.Interior.Min;
            interiorMax = plan.Interior.Max;
            plazaCentre = plan.PlazaCentre;
            plazaRadius = plan.PlazaRadius;

            spines = new SpinePath[plan.Spines.Count];
            for (var i = 0; i < plan.Spines.Count; i++)
                spines[i] = new SpinePath
                {
                    Points = plan.Spines[i].Points,
                    Width = plan.Spines[i].Width,
                    Kind = plan.Spines[i].Kind,
                };

            entrances = plan.Entrances.ToArray();
            zones = plan.Zones.ToArray();
            stations = plan.Stations.ToArray();
        }

#if UNITY_EDITOR
        [Header("Gizmos")]
        [SerializeField] bool drawWhenNotSelected;

        [Tooltip("Skip drawing beyond this distance from the scene camera.")]
        [SerializeField, Min(0f)] float maxDrawDistance = 250f;

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

            // The interior - the hedge line the whole plan lives inside.
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            DrawRect(interiorMin, interiorMax, 0.2f);

            if (zones != null)
                foreach (var zone in zones)
                {
                    Gizmos.color = ColourFor(zone.Kind);
                    DrawRect(zone.Area.Min, zone.Area.Max, 0.3f);
                }

            // The walks, drawn over the zones - everything else is judged against them.
            Gizmos.color = new Color(0.95f, 0.9f, 0.65f, 0.95f);
            if (spines != null)
                foreach (var spine in spines)
                {
                    if (spine.Points == null)
                        continue;
                    for (var i = 1; i < spine.Points.Length; i++)
                        Gizmos.DrawLine(At(spine.Points[i - 1], 0.4f), At(spine.Points[i], 0.4f));
                }

            if (plazaRadius > 0f)
            {
                Gizmos.color = new Color(0.95f, 0.9f, 0.65f, 0.95f);
                DrawCircle(plazaCentre, plazaRadius, 0.4f);
            }

            // Entrances: gate on the hedge line, arrow out to the road anchor it links at.
            Gizmos.color = Color.green;
            if (entrances != null)
                foreach (var entrance in entrances)
                {
                    Gizmos.DrawLine(At(entrance.Gate, 0.5f), At(entrance.Anchor, 0.5f));
                    Gizmos.DrawSphere(At(entrance.Anchor, 0.5f), 0.4f);
                }

            // Station exclusion radii - the circles the overlap sweep enforced.
            Gizmos.color = new Color(0.4f, 0.8f, 0.4f, 0.5f);
            if (stations != null)
                foreach (var station in stations)
                    DrawCircle(station.Pos, Mathf.Max(0.2f, station.Radius), 0.25f);
        }

        static Color ColourFor(ParkLayout.ZoneKind kind) => kind switch
        {
            ParkLayout.ZoneKind.Lawn => new Color(0.45f, 0.85f, 0.35f),
            ParkLayout.ZoneKind.Grove => new Color(0.15f, 0.5f, 0.2f),
            ParkLayout.ZoneKind.Feature => new Color(0.9f, 0.8f, 0.3f),
            ParkLayout.ZoneKind.ScreenBelt => new Color(0.3f, 0.4f, 0.3f, 0.6f),
            ParkLayout.ZoneKind.Parterre => new Color(0.9f, 0.4f, 0.6f),
            _ => new Color(0.3f, 0.5f, 0.9f),
        };

        static Vector3 At(Vector2 p, float y) => new(p.x, y, p.y);

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

        static void DrawCircle(Vector2 centre, float radius, float y)
        {
            const int Segments = 20;
            var previous = At(centre + new Vector2(0f, radius), y);
            for (var i = 1; i <= Segments; i++)
            {
                var angle = i * (Mathf.PI * 2f / Segments);
                var next = At(centre + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius, y);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
#endif
    }
}
