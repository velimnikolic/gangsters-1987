using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The pieces an elevated road is made of, and the one thing worth knowing about
    // each of them: where its pivot sits. Nothing here is a number somebody typed in.
    // Every placement below is worked out of the prefab's OWN bounds when the road is
    // built, and the bounds are logged the first time a piece is used - so a pack that
    // moves a pivot shows up as a line in the console rather than as a road with a
    // step in it.
    //
    // The deck piece (PalmCity's SM_Env_Road_Highway_01) runs along its local +Z and
    // lies to one side of its pivot across local X, which is why laying it needs the
    // bounds and not just a position: a run of them is centred on the line it is meant
    // to carry, not hung off its edge.
    public static class FreewayKit
    {
        const string PalmEnv = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";

        /// <summary>One carriageway of elevated deck, barrier down its outer edge.</summary>
        public const string DeckPath = PalmEnv + "SM_Env_Road_Highway_01.prefab";
        /// <summary>What holds it up.</summary>
        public const string PillarPath = PalmEnv + "SM_Env_Road_Highway_Pillar_01.prefab";
        /// <summary>The boom of a toll gate: a post and an arm across the lane.</summary>
        public const string BoomPath = PalmProps + "SM_Prop_Barrier_Gate_01.prefab";
        /// <summary>The booth the man in it takes the money through - the airport's own
        /// gatehouse, three metres square and glazed on all four sides, which is what a
        /// toll booth is (Editor/AirportKitBash.Buildings.cs bakes it).</summary>
        public const string BoothPath = "Assets/CityKit/Airport/airport-guard-booth.prefab";

        // ------------------------------------------------------------------ loading

        public static GameObject TryLoad(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            return null;
#endif
        }

        static readonly Dictionary<GameObject, Bounds> Measured = new Dictionary<GameObject, Bounds>();

        /// <summary>A prefab's local-space bounds, measured once off its renderers and
        /// kept. Local, not world: the caller turns it itself.</summary>
        public static Bounds Measure(GameObject prefab)
        {
            if (prefab == null) return new Bounds(Vector3.zero, Vector3.zero);
            if (Measured.TryGetValue(prefab, out var had)) return had;

            var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            var box = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (var r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var local = filter.sharedMesh.bounds;
                // the piece's own frame: the renderer's transform relative to the root
                var t = r.transform;
                var centre = prefab.transform.InverseTransformPoint(t.TransformPoint(local.center));
                var size = Vector3.Scale(local.size, t.lossyScale);
                var one = new Bounds(centre, size);
                if (!any) { box = one; any = true; } else box.Encapsulate(one);
            }
            Measured[prefab] = box;
            Debug.Log($"[freeway] {prefab.name}: {box.size.x:F2} x {box.size.y:F2} x {box.size.z:F2} m, " +
                      $"pivot at ({-box.center.x:F2}, {-box.center.y:F2}, {-box.center.z:F2}) of its centre");
            return box;
        }

        public static GameObject Prop(GameObject prefab, Vector3 pos, float yaw, Transform parent, string name = null)
        {
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent);
            go.name = name ?? prefab.name;
            if (!go.activeSelf) go.SetActive(true);
            return go;
        }

        /// <summary>A piece stood ON a surface: its lowest point at that height.</summary>
        public static GameObject Sit(GameObject prefab, Vector3 at, float yaw, Transform parent, string name = null)
        {
            if (prefab == null) return null;
            var b = Measure(prefab);
            return Prop(prefab, new Vector3(at.x, at.y - b.min.y, at.z), yaw, parent, name);
        }

        // ------------------------------------------------------------------- laying

        /// <summary>A run of deck from a to b, climbing from ya to yb, CENTRED on that
        /// line: each piece a straight chord, its ends on the profile, so neighbours
        /// always meet. The run's length is absorbed by stretching every piece a hair -
        /// never by squeezing one below the size it was authored at, and never by
        /// leaving a gap.
        ///
        /// <paramref name="pillarFree"/> is asked before a pier goes in: a pier standing
        /// in a carriageway is a wreck waiting.</summary>
        public static int LayDeck(GameObject deck, GameObject pillar, Vector3 a, float ya, Vector3 b, float yb,
                                  Transform parent, System.Func<Vector3, bool> pillarFree, string name)
        {
            if (deck == null) return 0;
            var d = b - a; d.y = 0f;
            float run = d.magnitude;
            if (run < 1f) return 0;
            var dir = d / run;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            var right = new Vector3(dir.z, 0f, -dir.x);      // the piece's own +X, level

            var box = Measure(deck);
            float piece = Mathf.Max(1f, box.size.z);
            // FLOOR, not round: every piece is then stretched a little rather than
            // squeezed, and nothing is ever laid smaller than it was drawn
            int count = Mathf.Max(1, Mathf.FloorToInt(run / piece));
            float len = run / count;
            float pitch = Mathf.Atan2(ya - yb, run) * Mathf.Rad2Deg;

            for (int k = 0; k < count; k++)
            {
                // the pivot: back off the piece's own start along the run, and off its
                // own centre across it, so the 11 m of deck lands on the line
                var at = a + dir * (k * len - box.min.z * (len / piece)) - right * box.center.x;
                at.y = Mathf.Lerp(ya, yb, k / (float)count);
                var go = Prop(deck, at, yaw, parent, name);
                go.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
                if (len > piece + 0.01f)
                    go.transform.localScale = new Vector3(1f, 1f, len / piece);

                if (pillar == null) continue;
                float y = Mathf.Lerp(ya, yb, (k + 0.5f) / count);
                if (y < 3.5f) continue;                      // a road on the ground needs no piers
                var pier = a + dir * ((k + 0.5f) * len);
                pier.y = y;
                if (pillarFree != null && !pillarFree(pier)) continue;
                StandPillar(pillar, pier, yaw, parent);
            }
            return count;
        }

        /// <summary>A pier under a deck whose surface is at <paramref name="at"/>.y: its
        /// own top against the road, its foot on the ground. Where the pack drew it
        /// shorter than the road stands it is stretched down to reach - never squeezed
        /// to fit, which would put it below the size it was authored at.</summary>
        public static void StandPillar(GameObject pillar, Vector3 at, float yaw, Transform parent, float ground = 0f)
        {
            var b = Measure(pillar);
            float authored = Mathf.Max(0.01f, b.size.y);
            float wanted = at.y - ground;                    // road surface down to the ground
            float scale = wanted > authored ? wanted / authored : 1f;
            var go = Prop(pillar, new Vector3(at.x, at.y - b.max.y * scale, at.z), yaw, parent, "Pier");
            if (scale > 1.001f) go.transform.localScale = new Vector3(1f, scale, 1f);
        }
    }
}
