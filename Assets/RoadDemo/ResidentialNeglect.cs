using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Opt-in dressing of the existing residential composition. Does not alter
    /// business state, occupants, entrances, floor plans or the source prefabs.</summary>
    public static class ResidentialNeglect
    {
        public const string DressingRoot = "Neglected district dressing";
        public sealed class Report
        {
            public int surfaces, boardedWindows, tags, litter;
        }

        static readonly string[] Rubbish = {
            "Assets/Synty/PolygonCity/Prefabs/Props/SM_Prop_TrashBag_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Props/SM_Prop_TrashBag_03.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Props/SM_Prop_Junk_Cardboard_01.prefab"
        };
        static readonly string[] Tags = { "STAY UP", "87", "EAST SIDE", "NO FUTURE", "FRESH", "ONE LOVE" };

        public static Report Apply(GameObject block, int seed, Func<Material, Material> weather,
                                   Material timber, Func<GameObject, Transform, GameObject> raise)
        {
            if (block.transform.Find(DressingRoot))
                throw new InvalidOperationException("Already dressed: rebuild from the source block.");
            var report = new Report();
            var rng = new System.Random(seed);
            var filters = block.GetComponentsInChildren<MeshFilter>(true);
            var holder = new GameObject(DressingRoot);
            holder.transform.SetParent(block.transform, false);
            var occupied = new List<Vector3>();
            foreach (var filter in filters)
            {
                if (!filter.sharedMesh) continue;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                string mesh = filter.sharedMesh.name;
                bool building = mesh.StartsWith("SM_Bld_", StringComparison.Ordinal);
                bool paving = mesh.Contains("Sidewalk") || mesh.Contains("Road_Bare") || mesh.Contains("Road_Parking");
                bool metal = mesh.Contains("Fence") || mesh.Contains("Trash") || mesh.Contains("PowerBox");
                bool furniture = mesh.Contains("Umbrella") || mesh.Contains("Bench") || mesh.Contains("Table") || mesh.Contains("Awning");
                if (building || paving || metal || furniture)
                {
                    var mats = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (!mats[i] || mats[i].renderQueue >= 2500 || (!mats[i].HasProperty("_BaseMap") && !mats[i].HasProperty("_Albedo_Map"))) continue;
                        mats[i] = weather(mats[i]); changed = true;
                    }
                    if (changed) { renderer.sharedMaterials = mats; report.surfaces++; }
                }
                // A sparse set of upper-floor openings; shops and their doors stay usable.
                if (mesh.Contains("Apartment") && rng.NextDouble() < 0.24)
                    DressFacade(filter, renderer, holder.transform, timber, rng, report, true);
                if (mesh.StartsWith("SM_Bld_Shop", StringComparison.Ordinal) &&
                    !mesh.Contains("Glass") && !mesh.Contains("Leaf") && rng.NextDouble() < 0.32)
                    DressFacade(filter, renderer, holder.transform, timber, rng, report, false);

                // Clusters belong beside existing bins, never randomly across a pavement.
                if ((mesh.Contains("Trashbin") || mesh.Contains("Trash_Bin") || mesh.Contains("Skip_")) &&
                    renderer.bounds.min.y < block.transform.position.y + 0.5f)
                {
                    var b = renderer.bounds;
                    if (occupied.Exists(p => Vector3.Distance(p, b.center) < 3f)) continue;
                    occupied.Add(b.center);
                    for (int i = 0; i < 3; i++)
                    {
                        var prefab = DemoAssetLoad.Load<GameObject>(Rubbish[(i + rng.Next(3)) % 3]);
                        if (!prefab) throw new InvalidOperationException("Missing district litter prefab.");
                        var go = raise(prefab, holder.transform);
                        go.name = "Overflow litter " + report.litter;
                        go.transform.rotation = Quaternion.Euler(0, rng.Next(360), 0);
                        go.transform.localScale *= 0.65f;
                        var p = b.center + new Vector3((i - 1) * 0.48f, 0, b.extents.z + 0.30f);
                        p.y = b.min.y;
                        Ground(go, p);
                        // Cosmetic only: do not add navigation obstacles or physical blockers.
                        foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
                        report.litter++;
                    }
                }
            }
            return report;
        }

        static void DressFacade(MeshFilter filter, Renderer renderer, Transform holder,
                                Material timber, System.Random rng, Report report, bool board)
        {
            // Raycast only against this measured module; no global physics queries or
            // permanent colliders are introduced into the generated block.
            var probe = new GameObject("temporary facade probe");
            probe.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
            probe.transform.localScale = filter.transform.lossyScale;
            var collider = probe.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            try
            {
                var bounds = renderer.bounds;
                float openingWidth = 1.4f, openingHeight = 1.28f;
                Vector3 openingNormal = Vector3.zero, openingCentre = Vector3.zero;
                if (board && !Window(filter, renderer, out openingCentre, out openingNormal,
                                     out openingWidth, out openingHeight)) return;
                if (board && openingCentre.y - openingHeight * .5f < holder.parent.position.y + 3f) return;
                var directions = new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
                int start = rng.Next(4);
                for (int i = 0; i < 4; i++)
                {
                    var outward = board ? openingNormal : directions[(start + i) % 4];
                    var tangent = Vector3.Cross(Vector3.up, outward);
                    var centre = bounds.center;
                    centre.y = board ? bounds.center.y : bounds.min.y + 0.9f;
                    centre += tangent * 1.65f;
                    if (board) centre = openingCentre;
                    var origin = centre + outward * (bounds.extents.magnitude + 1f);
                    if (!collider.Raycast(new Ray(origin, -outward), out var hit, 30f) ||
                        Vector3.Dot(hit.normal, outward) < 0.9f) continue;
                    // Only dress the exterior: reject a face buried inside a neighbouring
                    // apartment module or furniture (especially fire escapes).
                    var clearance = new Bounds(hit.point + outward * 0.3f, new Vector3(0.5f, 1f, 0.5f));
                    bool blocked = false;
                    foreach (var other in holder.parent.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (other == renderer || other.transform.IsChildOf(holder) || !other.enabled) continue;
                        if (other.bounds.Intersects(clearance)) { blocked = true; break; }
                    }
                    if (blocked) continue;
                    var rotation = Quaternion.LookRotation(outward);
                    if (board)
                    {
                        var group = new GameObject("Boarded upper window");
                        group.transform.SetParent(holder, false);
                        group.transform.SetPositionAndRotation(hit.point + outward * 0.055f, rotation);
                        for (int n = 0; n < 4; n++)
                        {
                            var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            plank.name = "Weathered timber plank";
                            plank.transform.SetParent(group.transform, false);
                            plank.transform.localPosition = new Vector3(0, (n - 1.5f) * openingHeight / 4f, 0);
                            plank.transform.localRotation = Quaternion.Euler(0, 0, rng.Next(-3, 4));
                            plank.transform.localScale = new Vector3(openingWidth, openingHeight / 4f - .025f, 0.045f);
                            plank.GetComponent<MeshRenderer>().sharedMaterial = timber;
                            UnityEngine.Object.DestroyImmediate(plank.GetComponent<Collider>());
                        }
                        report.boardedWindows++;
                    }
                    else
                    {
                        var go = new GameObject("Faded wall tag");
                        go.transform.SetParent(holder, false);
                        // TMP fronts face local -Z.
                        go.transform.SetPositionAndRotation(hit.point + outward * 0.04f,
                            Quaternion.LookRotation(-outward));
                        var text = go.AddComponent<TextMeshPro>();
                        text.text = Tags[rng.Next(Tags.Length)];
                        text.fontSize = 5.8f;
                        text.fontStyle = FontStyles.Bold | FontStyles.Italic;
                        text.alignment = TextAlignmentOptions.Center;
                        text.color = rng.Next(2) == 0 ? new Color(.48f,.24f,.16f) : new Color(.19f,.23f,.20f);
                        text.rectTransform.sizeDelta = new Vector2(1.7f, 0.8f);
                        text.textWrappingMode = TextWrappingModes.NoWrap;
                        report.tags++;
                    }
                    break;
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
        }

        // Read the authored pane triangles, rather than assuming a window grid.
        // The City atlas uses blue-grey glazing and green plaster; sample the original
        // UVs and reject broad walls, tiny trim, roofs and door-height geometry.
        static bool Window(MeshFilter filter, Renderer renderer, out Vector3 centre,
                           out Vector3 normal, out float width, out float height)
        {
            centre = normal = Vector3.zero; width = height = 0;
            var mesh = filter.sharedMesh;
            var materials = renderer.sharedMaterials;
            if (materials.Length == 0 || !materials[0]) return false;
            var mat = materials[0];
            var texture = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") :
                mat.HasProperty("_Albedo_Map") ? mat.GetTexture("_Albedo_Map") : null;
            if (!texture) return false;
            var rt = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32,
                                                RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(texture, rt); RenderTexture.active = rt;
                pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); pixels.Apply();
                var vertices = mesh.vertices; var uv = mesh.uv; var tris = mesh.triangles;
                if (uv.Length != vertices.Length) return false;
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    var texel = (uv[a] + uv[b] + uv[c]) / 3f;
                    var colour = pixels.GetPixelBilinear(texel.x, texel.y);
                    if (colour.b < colour.r * 1.12f || colour.b < colour.g * .96f || colour.b > .65f) continue;
                    var p = filter.transform.TransformPoint(vertices[a]);
                    var q = filter.transform.TransformPoint(vertices[b]);
                    var r = filter.transform.TransformPoint(vertices[c]);
                    var n = Vector3.Cross(q - p, r - p).normalized;
                    if (Mathf.Abs(n.y) > .05f) continue;
                    var box = new Bounds(p, Vector3.zero); box.Encapsulate(q); box.Encapsulate(r);
                    float w = Mathf.Max(box.size.x, box.size.z), h = box.size.y;
                    if (w < .45f || w > 1.65f || h < .85f || h > 2.3f) continue;
                    centre = box.center; normal = n; width = w + .08f; height = h + .08f;
                    return true;
                }
                return false;
            }
            finally
            {
                RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        static void Ground(GameObject go, Vector3 at)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            go.transform.position += at - new Vector3(b.center.x, b.min.y, b.center.z);
        }
    }
}
