using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LivingCity.EditorTools
{
    public static partial class SyntyIndustrialKitBash
    {
        // Full production floors, assembled on the authored 3 m module. Each 9 m roof
        // bay has its own trusses, supporting columns, valley gutter and downpipes.
        static void BuildProductionHall(string name, int w, int d, int floors)
        {
            var root = new GameObject(name);
            try
            {
                float hw = w * M / 2f, hd = d * M / 2f, top = floors * Course;
                for (int floor = 0; floor < floors; floor++)
                {
                    var front = new List<(string path, int modules)>();
                    for (int i = 0; i < w; i++)
                    {
                        if (i == 2 || i == w - 4)
                        {
                            front.Add((floor == 0 ? MetalSlideBig : floor == 1 ? null : MetalWindow, floor < 2 ? 2 : 1));
                            if (floor < 2) i++;
                        }
                        else front.Add((floor == 0 && i == w / 2 ? MetalDoor :
                            floor == floors - 1 && i % 3 != 0 ? MetalWindow : MetalWall, 1));
                    }
                    WallRing(root, w, d, floor, front.ToArray(),
                        Enumerable.Range(0, w).Select(i => floor == floors - 1 && i % 3 != 0 ? MetalWindow : MetalWall).ToArray(),
                        Enumerable.Range(0, d).Select(i => floor == floors - 1 && i % 3 != 0 ? MetalWindow : MetalWall).ToArray());
                }
                CornerPosts(root, w, d, floors, MetalCorner);
                for (int bay = 0; bay < d / 3; bay++)
                {
                    float z = -hd + (bay + 1) * 9f;
                    for (int i = 0; i < w; i++)
                    {
                        if (i == 0) Place(root, Bld + "SM_Bld_Roof_End_01.prefab", -hw + M, top, z - 9f, 180f);
                        else if (i == w - 1) Place(root, Bld + "SM_Bld_Roof_End_02.prefab", hw - M, top, z, 0f);
                        else Place(root, HallRoof, -hw + i * M, top, z, 0f);
                        if (bay > 0) Place(root, Bld + "SM_Bld_Roof_Connector_01.prefab", -hw + i * M, top, z - 9f, 0f);
                    }
                    for (int i = 1; i < w; i += 2) Place(root, HallTruss, -hw + i * M, top, z, 0f);
                    if (bay > 0)
                        for (int i = 0; i <= w; i += 2)
                            for (int floor = 0; floor < floors; floor++)
                                Place(root, MetalCorner, -hw + i * M, floor * Course, z - 9f, 0f);
                    // External risers sit against the wall, below the roof valleys.
                    for (int floor = 0; floor < floors; floor++)
                    {
                        WallProp(root, Riser, w, d, Side.Left, z - 9f, floor * Course, 0.13f);
                        WallProp(root, Riser, w, d, Side.Right, z - 9f, floor * Course, 0.13f);
                    }
                }
                foreach (float x in new[] { -hw + 3f, 0f, hw - 3f })
                    WallProp(root, BracketLight, w, d, Side.Front, x, 5.5f);
                WallProp(root, CompanySign, w, d, Side.Front, 0f, 4.3f, 0.06f);
                WallProp(root, WallCamera, w, d, Side.Front, hw - 1f, top - 0.4f);
                for (int i = 1; i < d; i += 3)
                {
                    WallProp(root, WallFan, w, d, Side.Left, -hd + i * M, 4.5f);
                    WallProp(root, WallFan, w, d, Side.Right, -hd + i * M, 4.5f);
                }
                // The outer solid bays flank the full-height roller doors. Their signs
                // stay below the lamps and clear of both doors and upper glazing.
                string label = name == "building-distribution-hall" ? "DESPATCH" : "GOODS IN";
                BakeWithSigns(root,
                    (label + "  01", new Vector3(hw - 3f, 1.6f, hd + Face + 0.03f), 4.8f),
                    (label + "  02", new Vector3(-hw + 3f, 1.6f, hd + Face + 0.03f), 4.8f));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
