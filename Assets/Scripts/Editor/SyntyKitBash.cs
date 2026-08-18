using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Assembles the buildings no Synty demo scene ships assembled - bank, hospital, fire
    /// station, detached house - from the 2.5 m wall module kits, deterministically (a
    /// recipe is a table, not a random draw), and bakes each through the same
    /// SyntyKitExtractor.BakeGroup pipeline the demo lifts use: one combined mesh per
    /// material, pivot at the footprint centre, front on +Z.
    ///
    /// Module facts measured offline off the binary FBX: every wall piece is 2.5 wide x
    /// 3.006 high with a corner pivot (x 0..2.5) and its plane at z~0; the PalmCity garage
    /// door is a 5 m piece; Roof_Flat_01 is a 2.5 slab; Villa_Roof_Straight_01 slopes 1.57
    /// over 2.5 (gable recipes therefore use even module depths only). Walls face +Z at
    /// identity - the pack-wide convention verified in the Overview scenes.
    /// </summary>
    public static class SyntyKitBash
    {
        const string PalmBld = "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/";
        const string PoliceBld = "Assets/Synty/PolygonPoliceStation/Prefabs/Buildings/";
        const string GenBase = "Assets/Synty/PolygonGeneric/Prefabs/Base/";

        public const int Version = 3;
        const string VersionPath = SyntyKitExtractor.BuildingsDir + "/KitBashVersion.txt";

        const float M = 2.5f;   // the module
        const float Course = 3.006f;

        enum Roof { Flat, Gable }

        sealed class Recipe
        {
            public string name;
            public int w, d, floors;       // modules
            public Roof roof = Roof.Flat;
            public string wall;            // window wall piece, most positions
            public string door;            // front-centre ground floor piece
            public int doorModules = 1;
            public (string path, int modules)[] frontRow; // explicit ground-floor front, in order
            public string roofSlab;
            public string roofEdge;
            public string gableSlope;
        }

        static readonly Recipe[] Recipes =
        {
            // The one building the city reaches for down two paths; art-deco marble front
            // reads as a 1980s bank without a single custom asset.
            new()
            {
                name = "building-bank", w = 8, d = 5, floors = 2,
                wall = PalmBld + "SM_Bld_Deco_Wall_Window_02.prefab",
                door = PalmBld + "SM_Bld_Deco_Wall_Door_Large_01.prefab", doorModules = 2,
                roofSlab = PalmBld + "SM_Bld_Roof_Flat_01.prefab",
                roofEdge = PalmBld + "SM_Bld_Roof_Trim_01.prefab",
            },
            new()
            {
                name = "building-hospital", w = 9, d = 6, floors = 3,
                wall = GenBase + "SM_Bld_Base_Wall_Window_01.prefab",
                door = GenBase + "SM_Bld_Base_Wall_Door_Double_01.prefab", doorModules = 1,
                roofSlab = PalmBld + "SM_Bld_Roof_Flat_01.prefab",
                roofEdge = PalmBld + "SM_Bld_Roof_Edge_Straight_01.prefab",
            },
            // Front row is wall/garage/garage/wall against a 7-module facade; the engine
            // house look does the fire-station reading on its own.
            new()
            {
                name = "building-firestation", w = 7, d = 4, floors = 1,
                wall = PoliceBld + "SM_Bld_Wall_Window_Large_01.prefab",
                door = null,
                frontRow = new[]
                {
                    (PalmBld + "SM_Bld_Wall_Trim_01.prefab", 1),
                    (PalmBld + "SM_Bld_Wall_Door_Garage_01.prefab", 2),
                    (PalmBld + "SM_Bld_Wall_Trim_01.prefab", 1),
                    (PalmBld + "SM_Bld_Wall_Door_Garage_02.prefab", 2),
                    (PalmBld + "SM_Bld_Wall_Trim_01.prefab", 1),
                },
                roofSlab = PalmBld + "SM_Bld_Roof_Flat_01.prefab",
                roofEdge = PalmBld + "SM_Bld_Roof_Edge_Straight_01.prefab",
            },
            // The residential series - the bulk of the city's street wall. Synty ships NO
            // assembled residential buildings (the three demo apartments are all there is),
            // but it ships ten distinct 2.5 x 3.0 facade panel styles, which is what a
            // rowhouse kit IS. Six silhouettes, varied by style, width and storey count, so
            // a ShuffleBag run steps the skyline the way the old 4/5-floor kit did.
            new()
            {
                name = "building-res-01", w = 8, d = 5, floors = 3,
                wall = PalmBld + "SM_Bld_Wall_Window_02.prefab",
                door = PalmBld + "SM_Bld_Deco_Wall_Door_01.prefab",
                roofSlab = PalmBld + "SM_Bld_Roof_Flat_01.prefab",
                roofEdge = PalmBld + "SM_Bld_Roof_Trim_01.prefab",
            },
            new()
            {
                name = "building-res-02", w = 6, d = 4, floors = 2,
                wall = PalmBld + "SM_Bld_Wall_Window_03.prefab",
                door = PalmBld + "SM_Bld_Deco_Wall_Door_02.prefab",
                roofSlab = PalmBld + "SM_Bld_Roof_Flat_01.prefab",
                roofEdge = PalmBld + "SM_Bld_Roof_Edge_Straight_01.prefab",
            },
            new()
            {
                name = "building-res-03", w = 9, d = 5, floors = 4,
                wall = PalmBld + "SM_Bld_Deco_Wall_Window_03.prefab",
                door = PalmBld + "SM_Bld_Deco_Wall_Door_03.prefab",
                roofSlab = PalmBld + "SM_Bld_Roof_Flat_01.prefab",
                roofEdge = PalmBld + "SM_Bld_Roof_Trim_01.prefab",
            },
            new()
            {
                name = "building-res-04", w = 7, d = 5, floors = 3,
                wall = PalmBld + "SM_Bld_Deco_Wall_Window_04.prefab",
                door = PalmBld + "SM_Bld_Deco_Wall_Door_01.prefab",
                roofSlab = PalmBld + "SM_Bld_Roof_Flat_01.prefab",
                roofEdge = PalmBld + "SM_Bld_Roof_Edge_Straight_01.prefab",
            },
            new()
            {
                name = "building-res-05", w = 6, d = 5, floors = 4,
                wall = PalmBld + "SM_Bld_Wall_Window_04.prefab",
                door = PalmBld + "SM_Bld_Deco_Wall_Door_02.prefab",
                roofSlab = PalmBld + "SM_Bld_Roof_Flat_01.prefab",
                roofEdge = PalmBld + "SM_Bld_Roof_Trim_01.prefab",
            },
            new()
            {
                name = "building-res-06", w = 8, d = 4, floors = 2,
                wall = PalmBld + "SM_Bld_Deco_Wall_Window_05.prefab",
                door = PalmBld + "SM_Bld_Deco_Wall_Door_04.prefab",
                roofSlab = PalmBld + "SM_Bld_Roof_Flat_01.prefab",
                roofEdge = PalmBld + "SM_Bld_Roof_Edge_Straight_01.prefab",
            },

            // The detached-house stock (bank neighbours, outbuildings): villa kit, gable
            // roof, even depth by the slope arithmetic above.
            new()
            {
                name = "building-house-block", w = 4, d = 4, floors = 1,
                roof = Roof.Gable,
                wall = PalmBld + "SM_Bld_Villa_Wall_Window_01.prefab",
                door = PalmBld + "SM_Bld_Villa_Wall_Door_01.prefab", doorModules = 1,
                gableSlope = PalmBld + "SM_Bld_Villa_Roof_Straight_01.prefab",
                roofSlab = GenBase + "SM_Bld_Base_Ceiling_01.prefab",
            },
            new()
            {
                name = "building-house-block-big", w = 6, d = 4, floors = 2,
                roof = Roof.Gable,
                wall = PalmBld + "SM_Bld_Villa_Wall_Window_02.prefab",
                door = PalmBld + "SM_Bld_Villa_Wall_Door_01.prefab", doorModules = 1,
                gableSlope = PalmBld + "SM_Bld_Villa_Roof_Straight_01.prefab",
                roofSlab = GenBase + "SM_Bld_Base_Ceiling_01.prefab",
            },
        };

        [MenuItem("Tools/City/Catalog/Rebuild Synty City Kit (Kit-Bash)", priority = 2)]
        public static void ForceBuild()
        {
            AssetDatabase.DeleteAsset(VersionPath);
            BuildIfStale();
        }

        public static void BuildIfStale()
        {
            var marker = AssetDatabase.LoadAssetAtPath<TextAsset>(VersionPath);
            if (marker && marker.text.Trim() == Version.ToString())
                return;

            foreach (var recipe in Recipes)
                Build(recipe);

            System.IO.File.WriteAllText(VersionPath, Version.ToString());
            AssetDatabase.ImportAsset(VersionPath);
            AssetDatabase.SaveAssets();
        }

        static void Build(Recipe r)
        {
            var root = new GameObject(r.name);
            try
            {
                float W = r.w * M, D = r.d * M;
                float halfW = W / 2f, halfD = D / 2f;

                // Corner-pivot arithmetic, derived once and reused everywhere: a piece whose
                // local x runs 0..2.5 covers, after yaw, the world interval
                //   yaw 0:   x [P.x, P.x+M]  (plane at z=P.z, facing +Z)
                //   yaw 180: x [P.x-M, P.x]  (facing -Z)   -> P.x = x0 + M covers [x0, x0+M]
                //   yaw 90:  z [P.z-M, P.z]  (facing +X)   -> P.z = z0 + M covers [z0, z0+M]
                //   yaw 270: z [P.z, P.z+M]  (facing -X)   -> P.z = z0
                for (var floor = 0; floor < r.floors; floor++)
                {
                    var y = floor * Course;

                    if (floor == 0 && r.frontRow != null)
                    {
                        var cursor = -halfW;
                        foreach (var (path, modules) in r.frontRow)
                        {
                            Place(root, path, cursor, y, halfD, 0f);
                            cursor += modules * M;
                        }
                    }
                    else
                    {
                        for (var i = 0; i < r.w; i++)
                            Place(root, FrontPiece(r, floor, i), -halfW + i * M, y, halfD, 0f);
                    }

                    for (var i = 0; i < r.w; i++)
                        Place(root, r.wall, -halfW + i * M + M, y, -halfD, 180f);

                    for (var i = 0; i < r.d; i++)
                    {
                        var z0 = -halfD + i * M;
                        Place(root, r.wall, halfW, y, z0 + M, 90f);
                        Place(root, r.wall, -halfW, y, z0, 270f);
                    }
                }

                var topY = r.floors * Course;
                if (r.roof == Roof.Flat)
                {
                    for (var i = 0; i < r.w; i++)
                        for (var j = 0; j < r.d; j++)
                            Place(root, r.roofSlab, -halfW + i * M, topY, -halfD + j * M, 0f);

                    if (!string.IsNullOrEmpty(r.roofEdge))
                    {
                        for (var i = 0; i < r.w; i++)
                        {
                            var x0 = -halfW + i * M;
                            Place(root, r.roofEdge, x0, topY, halfD, 0f);
                            Place(root, r.roofEdge, x0 + M, topY, -halfD, 180f);
                        }
                        for (var i = 0; i < r.d; i++)
                        {
                            var z0 = -halfD + i * M;
                            Place(root, r.roofEdge, halfW, topY, z0 + M, 90f);
                            Place(root, r.roofEdge, -halfW, topY, z0, 270f);
                        }
                    }
                }
                else
                {
                    // Gable: slope rows climb from both z edges to the ridge. The slope
                    // piece's pivot is its HIGH edge (y -1.57..0.1 over 2.5 depth), so the
                    // pivot of row N sits one course of rise above the eave line and one
                    // module in from the edge. Even module depths only, by construction.
                    const float Rise = 1.567f;
                    var rows = r.d / 2;
                    for (var i = 0; i < r.w; i++)
                        for (var row = 0; row < rows; row++)
                        {
                            var x0 = -halfW + i * M;
                            var y = topY + (row + 1) * Rise;
                            Place(root, r.gableSlope, x0, y, halfD - (row + 1) * M, 0f);
                            Place(root, r.gableSlope, x0 + M, y, -halfD + (row + 1) * M, 180f);
                        }
                }

                SyntyKitExtractor.BakeGroup(root, r.name, yaw: 0f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static string FrontPiece(Recipe r, int floor, int i)
        {
            if (floor == 0 && r.door != null)
            {
                var doorStart = (r.w - r.doorModules) / 2;
                if (i == doorStart) return r.door;
                if (i > doorStart && i < doorStart + r.doorModules) return null; // door piece spans it
            }
            return r.wall;
        }

        /// <summary>
        /// Corner-pivot placement in the building frame: the piece's local x runs along the
        /// wall line given by yaw; (x, z) is the piece's pivot corner AFTER yaw, so callers
        /// pass the left end of the run as seen from outside.
        /// </summary>
        static void Place(GameObject root, string prefabPath, float x, float y, float z, float yaw, bool flat = false)
        {
            if (string.IsNullOrEmpty(prefabPath))
                return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab)
            {
                Debug.LogWarning($"SyntyKitBash: missing module {prefabPath}");
                return;
            }

            var piece = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            piece.transform.SetLocalPositionAndRotation(
                new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f));
        }
    }
}
