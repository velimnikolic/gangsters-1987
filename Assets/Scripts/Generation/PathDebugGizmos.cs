using UnityEngine;
using PolyPerfect.City;

namespace LivingCity.Generation
{
    /// <summary>
    /// Draws the Path graph so a broken tile connection is visible instead of being a
    /// silent no-op. Without this, a wrong tile rotation looks identical to a pathfinding
    /// bug: cars simply never move and the only clue is a "Path not found" warning.
    ///
    /// Green line  = a nextPaths link that successfully crosses a tile boundary.
    /// Red sphere  = a path END point with no outgoing link. On an interior tile this is
    ///               the signature of a misrotated neighbour.
    ///
    /// Path.nextPaths is populated in Tile.Start(), so links only appear in play mode.
    /// In edit mode this still draws the endpoints, which is enough to eyeball alignment.
    ///
    /// Lives in the runtime assembly rather than Scripts/Editor because a MonoBehaviour
    /// attached to a scene object cannot be compiled into an editor-only assembly. All of
    /// the gizmo work is stripped from builds by the UNITY_EDITOR guards below.
    /// </summary>
    public sealed class PathDebugGizmos : MonoBehaviour
    {
#if UNITY_EDITOR
        public enum Filter { RoadsAndSidewalks, RoadsOnly, SidewalksOnly }

        [SerializeField] bool drawWhenNotSelected = true;
        [SerializeField] Filter show = Filter.RoadsAndSidewalks;
        [SerializeField, Min(0.05f)] float endpointRadius = 0.35f;

        [Tooltip("Skip drawing beyond this distance from the scene camera. The full graph on " +
                 "an 11x11 city is thousands of gizmos and will crawl.")]
        [SerializeField, Min(0f)] float maxDrawDistance = 120f;

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
            var origin = cam ? cam.transform.position : Vector3.zero;
            var cull = cam && maxDrawDistance > 0f;

            foreach (var path in FindObjectsByType<Path>(FindObjectsSortMode.None))
            {
                if (!Wanted(path.pathType)) continue;
                if (path.pathPositions == null || path.pathPositions.Count == 0) continue;

                var first = path.pathPositions[0];
                var last = path.pathPositions[path.pathPositions.Count - 1];
                if (!first || !last) continue;

                if (cull && Vector3.Distance(origin, last.position) > maxDrawDistance)
                    continue;

                Gizmos.color = path.pathType == PathType.Sidewalk
                    ? new Color(0.4f, 0.8f, 1f)
                    : new Color(1f, 0.9f, 0.3f);
                Gizmos.DrawWireSphere(first.position, endpointRadius * 0.6f);

                var links = 0;
                if (path.nextPaths != null)
                {
                    foreach (var next in path.nextPaths)
                    {
                        if (!next || next.pathPositions == null || next.pathPositions.Count == 0) continue;
                        var target = next.pathPositions[0];
                        if (!target) continue;

                        links++;
                        Gizmos.color = Color.green;
                        Gizmos.DrawLine(last.position, target.position);
                    }
                }

                // An endpoint with no outgoing link is where traffic dead-ends.
                Gizmos.color = links > 0 ? Color.green : Color.red;
                Gizmos.DrawWireSphere(last.position, endpointRadius);
            }
        }

        bool Wanted(PathType type) => show switch
        {
            Filter.RoadsOnly => type != PathType.Sidewalk,
            Filter.SidewalksOnly => type == PathType.Sidewalk,
            _ => true,
        };
#endif
    }
}
