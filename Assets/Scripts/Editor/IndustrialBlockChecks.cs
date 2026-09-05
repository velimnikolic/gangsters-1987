using System;
using System.Collections.Generic;
using System.Linq;
using RoadDemo;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    public static class IndustrialBlockChecks
    {
        // Checks actual instantiated obstacles against the promised traffic courts,
        // including both minimum parcels and parcels with shared district boundaries.
        public static object Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Industrial checks require Edit mode.");
            SyntyIndustrialKitBash.BuildIfStale();
            IndustrialBlocks.ForgetMeasurements();
            IndustrialBlocks.ForgetMissing();
            var failures = new List<string>();
            int cases = 0, routes = 0;
            foreach (IndustrialLayout.Recipe recipe in Enum.GetValues(typeof(IndustrialLayout.Recipe)))
                for (int variant = 0; variant < 3; variant++)
                {
                    IndustrialLayout.Smallest(recipe, out int w, out int d);
                    w = w * 5 + variant * 5;
                    d = d * 5 + variant * 5;
                    var root = new GameObject("Industrial validation");
                    try
                    {
                        var edges = IndustrialBlocks.Alone();
                        if (variant == 2)
                        {
                            edges[(int)IndustrialLayout.Side.West] = new IndustrialLayout.Edge(IndustrialLayout.Rim.Party, false);
                            edges[(int)IndustrialLayout.Side.North] = new IndustrialLayout.Edge(IndustrialLayout.Rim.Party, true);
                        }
                        var b = IndustrialBlocks.Stand(recipe, root.transform, w, d, edges,
                            new System.Random(1987 + variant * 97 + (int)recipe * 31),
                            (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent));
                        string id = recipe + " " + w + "x" + d;
                        cases++;
                        if (b.Gaps() != 0) failures.Add(id + ": unlaid floor cells " + b.Gaps());
                        if (b.WallGap > 0.1f) failures.Add(id + ": missing fence " + b.WallGap);
                        if (b.WallInBuilding() != 0) failures.Add(id + ": fence intersects building");
                        int required = recipe == IndustrialLayout.Recipe.Works || recipe == IndustrialLayout.Recipe.Plant ? 3 :
                            recipe == IndustrialLayout.Recipe.Depot || recipe == IndustrialLayout.Recipe.Strip || recipe == IndustrialLayout.Recipe.Yard ? 2 : 0;
                        if (b.Built.Count < required) failures.Add(id + ": only " + b.Built.Count + " buildings; expected " + required);
                        if (recipe == IndustrialLayout.Recipe.Strip)
                        {
                            var buildingSources = new HashSet<string>();
                            foreach (Transform child in root.transform)
                            {
                                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
                                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/CityKit/Buildings/", StringComparison.Ordinal)) continue;
                                if (!buildingSources.Add(path)) failures.Add(id + ": repeated building " + path);
                            }
                        }
                        if (required > 0 && b.Routes.Count == 0) failures.Add(id + ": no cross-court");
                        foreach (var route in b.Routes)
                        {
                            routes++;
                            if (route.height < 7.5f) failures.Add(id + ": narrow manoeuvring court");
                            foreach (Transform child in root.transform)
                            {
                                if (!IndustrialBlocks.WorldBox(child.gameObject, out var box)) continue;
                                // Overhead lights, ground paint and flat weathering are not obstacles.
                                if (box.size.y < 0.45f || box.min.y > 2.5f || child.GetComponent<TextMesh>()) continue;
                                var footprint = new Rect(box.min.x + 0.08f, box.min.z + 0.08f,
                                    Mathf.Max(0f, box.size.x - 0.16f), Mathf.Max(0f, box.size.z - 0.16f));
                                if (footprint.Overlaps(route)) failures.Add(id + ": court blocked by " + child.name);
                            }
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                }
            foreach (string missing in IndustrialBlocks.Missing) failures.Add("Missing asset: " + missing);
            return new { passed = failures.Count == 0, cases, routes, failures = failures.Distinct().ToArray() };
        }
    }
}
