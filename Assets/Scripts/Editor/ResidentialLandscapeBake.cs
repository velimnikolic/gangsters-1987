using System;
using System.IO;
using System.Linq;
using RoadDemo;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LivingCity.EditorTools
{
    /// <summary>Reusable planted beds; merged meshes keep small foliage visible at city zoom.</summary>
    public static class ResidentialLandscapeBake
    {
        static Material rim, brick, soil, gravel;
        static Transform structure;

        [MenuItem("Tools/City/Residential/Bake Street Planting")]
        public static void Bake()
        {
            Directory.CreateDirectory(ResidentialLandscaping.Folder);
            AssetDatabase.Refresh();
            rim = Finish("Edging", new Color(.47f, .45f, .40f));
            brick = Finish("Brick", new Color(.37f, .225f, .16f));
            soil = Finish("Soil", new Color(.16f, .135f, .09f));
            gravel = Finish("Gravel", new Color(.38f, .365f, .30f));
            Build("StreetBed", 0, .28f, false, 717);
            Build("BrickBed", 1, .20f, true, 918);
            Build("GardenIsland", 2, .16f, false, 231);
            Build("StreetBorder", 3, .22f, false, 717);
            Build("CourtBorder", 4, .16f, false, 231);
            AssetDatabase.SaveAssets();
        }

        static void Build(string name, int variant, float height, bool masonry, int seed)
        {
            var size = ResidentialLandscaping.Footprint(variant);
            float width = size.x, depth = size.y;
            var root = new GameObject(name);
            try
            {
                structure = root.transform;
                // A shallow gravel apron meets the existing pavement; no floor tile is removed.
                Box("Gravel apron", new Vector3(0f, .012f, 0f), new Vector3(width, .024f, depth), gravel);
                float w = width - .22f, d = depth - .22f;
                Box("Planting soil", new Vector3(0f, height - .065f, 0f), new Vector3(w - .15f, .1f, d - .15f), soil);
                if (masonry)
                {
                    for (int row = 0; row < 2; row++)
                    {
                        for (float x = -w * .5f + .125f; x < w * .5f; x += .25f)
                            foreach (float sign in new[] { -1f, 1f })
                                Box("Brick edging", new Vector3(x, .026f + row * .087f + .04f, sign * (d - .12f) * .5f),
                                    new Vector3(.242f, .082f, .12f), brick);
                        foreach (float sign in new[] { -1f, 1f })
                            Box("Brick end", new Vector3(sign * (w - .12f) * .5f, .026f + row * .087f + .04f, 0f),
                                new Vector3(.12f, .082f, d - .24f), brick);
                    }
                }
                else foreach (float sign in new[] { -1f, 1f })
                {
                    Box("Concrete edging", new Vector3(0f, height * .5f, sign * (d - .09f) * .5f), new Vector3(w, height, .09f), rim);
                    Box("Concrete end", new Vector3(sign * (w - .09f) * .5f, height * .5f, 0f), new Vector3(.09f, height, d - .18f), rim);
                }
                var rng = new System.Random(seed);
                // A low leafy understorey makes a planted mass, with taller shrubs above it.
                int coverX = Mathf.CeilToInt((w - .18f) / 1.05f);
                int coverZ = Mathf.CeilToInt((d - .18f) / 1.05f);
                float coverSpan = Mathf.Min((w - .18f) / coverX, (d - .18f) / coverZ) * 1.04f;
                for (int x = 0; x < coverX; x++) for (int z = 0; z < coverZ; z++)
                    Plant(root.transform, 0, new Vector3(((x + .5f) / coverX - .5f) * (w - .18f),
                        height - .025f, ((z + .5f) / coverZ - .5f) * (d - .18f)),
                        coverSpan, .16f + (float)rng.NextDouble() * .07f, rng.Next(4) * 90f);
                int columns = Mathf.Max(2, Mathf.CeilToInt((w - .2f) / .85f));
                int rows = depth > 2f ? 2 : 1;
                for (int x = 0; x < columns; x++) for (int z = 0; z < rows; z++)
                {
                    float px = ((x + .5f) / columns - .5f) * (w - .20f);
                    float pz = ((z + .5f) / rows - .5f) * (d - .22f);
                    Plant(root.transform, 1 + (x + z + seed) % 3, new Vector3(px, height - .02f, pz),
                        Mathf.Min((w - .16f) / columns * 1.05f, (d - .16f) / rows),
                        .44f + (float)rng.NextDouble() * .32f, (float)rng.NextDouble() * 360f);
                }
                Merge(name);
                var collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, height * .5f, 0f);
                collider.size = new Vector3(w, height, d);
                PrefabUtility.SaveAsPrefabAsset(root, ResidentialLandscaping.Folder + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); structure = null; }
        }

        static void Plant(Transform parent, int variant, Vector3 at, float span, float height, float yaw)
        {
            string path = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/" +
                (variant == 0 ? "SM_Env_Hedge_03" : "SM_Env_Bush_0" + variant) + ".prefab";
            var plant = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), parent);
            try
            {
                plant.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                var group = plant.GetComponentInChildren<LODGroup>();
                var renderers = group.GetLODs()[0].renderers;
                var box = renderers[0].bounds; foreach (var r in renderers) box.Encapsulate(r.bounds);
                float fit = span / Mathf.Max(box.size.x, box.size.z);
                plant.transform.localScale = new Vector3(fit, height / box.size.y, fit);
                box = renderers[0].bounds; foreach (var r in renderers) box.Encapsulate(r.bounds);
                plant.transform.position += at - new Vector3(box.center.x, box.min.y, box.center.z);
                foreach (var renderer in renderers)
                {
                    var source = renderer.GetComponent<MeshFilter>();
                    var leaf = new GameObject("Low planting"); leaf.transform.SetParent(structure, false);
                    leaf.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                    leaf.transform.localScale = source.transform.lossyScale;
                    leaf.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                    leaf.AddComponent<MeshRenderer>().sharedMaterial = renderer.sharedMaterial;
                }
            }
            finally { Object.DestroyImmediate(plant); }
        }

        static Material Finish(string name, Color tint)
        {
            string path = "Assets/Materials/ResidentialGarden_" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("LivingCity/Street Prop Surface")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", tint); material.SetTexture("_BaseMap", null);
            material.SetFloat("_Prop", 2f); material.SetFloat("_Smoothness", .1f);
            material.shaderKeywords = new[] { "_PROP_STONE" }; material.enableInstancing = true;
            EditorUtility.SetDirty(material); return material;
        }

        static void Box(string name, Vector3 at, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(structure, false); go.transform.localPosition = at; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        static void Merge(string name)
        {
            var filters = structure.GetComponentsInChildren<MeshFilter>();
            foreach (var batch in filters.GroupBy(f => f.GetComponent<Renderer>().sharedMaterial))
            {
                string path = ResidentialLandscaping.Folder + name + "_" + batch.Key.name + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
                mesh.Clear(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(batch.Select(f => new CombineInstance { mesh = f.sharedMesh,
                    transform = structure.worldToLocalMatrix * f.transform.localToWorldMatrix }).ToArray(), true, true);
                mesh.name = name + " " + batch.Key.name; EditorUtility.SetDirty(mesh);
                var go = new GameObject(batch.Key.name); go.transform.SetParent(structure, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = batch.Key;
            }
            foreach (var filter in filters) Object.DestroyImmediate(filter.gameObject);
        }
    }
}
