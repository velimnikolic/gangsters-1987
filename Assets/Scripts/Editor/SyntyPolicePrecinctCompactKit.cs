using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoadDemo;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Builds the small precinct as a real modular building. It is intentionally not a
    /// crop of Synty's open-sided showcase scene: every side is assembled here, on the
    /// 2.5 m Police Station grid, and the two furnished floors fit inside that shell.
    /// Parking belongs to PolicePrecinctBlock and is surface-only.
    /// </summary>
    public static class SyntyPolicePrecinctCompactKit
    {
        public const string CompactPath =
            "Assets/CityKit/PolicePrecinct/building-policestation-compact.prefab";

        const string OutputDir = "Assets/CityKit/PolicePrecinct";
        const string Buildings = "Assets/Synty/PolygonPoliceStation/Prefabs/Buildings/";
        const string Base = Buildings + "Base/";
        const string Props = "Assets/Synty/PolygonPoliceStation/Prefabs/Props/";
        const string Signs = "Assets/Synty/PolygonPoliceStation/Prefabs/Signs/";

        const string Floor = Base + "SM_Bld_Base_Floor_01.prefab";
        const string Wall = Base + "SM_Bld_Base_Wall_01.prefab";
        const string Window = Buildings + "SM_Bld_Wall_Window_Large_01.prefab";
        const string WindowAlt = Buildings + "SM_Bld_Wall_Window_Double_01.prefab";
        const string CellWindow = Buildings + "SM_Bld_Wall_Window_Cell_01.prefab";
        const string DoorWall = Base + "SM_Bld_Base_Wall_Door_01.prefab";
        const string Door = Buildings + "SM_Bld_Door_01.prefab";
        const string CellWall = Buildings + "SM_Bld_Wall_Cell_01.prefab";
        const string CellDoorWall = Buildings + "SM_Bld_Wall_Door_Cell_01.prefab";
        const string CellDoor = Buildings + "SM_Bld_Door_05.prefab";
        const string Pillar = Base + "SM_Bld_Base_Pillar_01.prefab";
        const string PillarAlt = Base + "SM_Bld_Base_Pillar_02.prefab";
        const string Stairs = Base + "SM_Bld_Base_Stairs_02.prefab";
        const string Plinth = Buildings + "SM_Bld_Wall_Block_01.prefab";
        const string Trim = Buildings + "SM_Bld_Wall_Trim_01.prefab";
        const string RoofEdge = Buildings + "SM_Bld_Roof_Edge_01.prefab";
        const string RoofCorner = Buildings + "SM_Bld_Roof_Edge_Corner_01.prefab";

        const string Concrete =
            "Assets/Synty/PolygonPoliceStation/Materials/Walls_Floors/Concrete_01.mat";
        const string RoofMaterial =
            "Assets/Synty/PolygonPoliceStation/Materials/Walls_Floors/Roof_01.mat";

        const float Cell = 2.5f;
        const float Storey = 3f;
        const float MinX = -7.5f;
        const float MaxX = 7.5f;
        const float MinZ = -10f;
        const float MaxZ = 10f;
        const int CellsX = 6;
        const int CellsZ = 8;

        public sealed class AuditResult
        {
            public bool passed;
            public string prefab;
            public int renderers;
            public int props;
            public int cellWalls;
            public int cellDoors;
            public int frontWallModules;
            public int rearWallModules;
            public int leftWallModules;
            public int rightWallModules;
            public int undergroundObjects;
            public int lights;
            public float width;
            public float height;
            public float depth;
            public string[] failures = Array.Empty<string>();
        }

        [MenuItem("Tools/City/Police Precinct/Rebuild Compact City-Block Prefab", priority = 3)]
        public static void BuildMenu()
        {
            var result = Build();
            Debug.Log("[CompactPrecinct] " + Describe(result));
        }

        public static AuditResult Build()
        {
            EnsureFolders();
            var previous = SceneManager.GetActiveScene();
            Scene work = default;
            GameObject root = null;
            try
            {
                work = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                                                   NewSceneMode.Additive);
                SceneManager.SetActiveScene(work);
                root = new GameObject("building-policestation-compact");
                SceneManager.MoveGameObjectToScene(root, work);

                var exterior = Group(root.transform, "10_FULLY ENCLOSED EXTERIOR SHELL");
                var ground = Group(root.transform, "20_GROUND FLOOR - RECEPTION BOOKING CELLS");
                var upper = Group(root.transform, "30_UPPER FLOOR - OFFICES OPERATIONS");
                var roof = Group(root.transform, "40_FLAT ROOF AND SERVICES");
                var lighting = Group(root.transform, "50_INTERIOR PRACTICAL LIGHTING");

                BuildFoundation(root.transform);
                BuildFacades(exterior);
                BuildFloor(ground, 0f, false);
                BuildGroundInterior(ground);
                BuildFloor(upper, Storey, true);
                BuildUpperInterior(upper);
                BuildRoof(roof);
                BuildLighting(lighting);
                BuildExteriorDetail(exterior, roof);

                var markers = Group(root.transform, "90_FUNCTIONAL PLACES");
                var entrance = Marker(markers, "PUBLIC ENTRANCE",
                    new Vector3(-1.25f, 0.02f, MaxZ + 0.9f), Vector3.forward);
                var booking = Marker(markers, "BOOKING DESK",
                    new Vector3(3.9f, 0.02f, -1.5f), Vector3.back);
                var cells = Marker(markers, "MINI HOLDING CELLS",
                    new Vector3(-2.5f, 0.02f, -7.5f), Vector3.back);

                var visual = root.AddComponent<PolicePrecinctVisual>();
                visual.Configure(
                    null, null, ground.gameObject, upper.gameObject,
                    null, lighting.gameObject,
                    entrance, booking, cells, null, null,
                    Array.Empty<Transform>(), Vector3.forward, 0f,
                    CountProps(root.transform),
                    root.GetComponentsInChildren<Renderer>(true).Length,
                    root.GetComponentsInChildren<Light>(true).Length);

                SetStatic(root);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, CompactPath);
                if (saved == null)
                    throw new IOException("Unity did not save " + CompactPath);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (work.IsValid() && work.isLoaded)
                    EditorSceneManager.CloseScene(work, true);
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
            }

            AssetDatabase.SaveAssets();
            var audit = Audit();
            if (!audit.passed)
                throw new InvalidOperationException(
                    "Compact precinct audit failed: " + string.Join("; ", audit.failures));
            return audit;
        }

        static void BuildFoundation(Transform root)
        {
            Primitive(root, "SEALED CONCRETE FOUNDATION",
                new Vector3(0f, -0.13f, 0f), new Vector3(15.35f, 0.26f, 20.35f),
                Concrete, collider: true);
        }

        static void BuildFacades(Transform exterior)
        {
            var front = Group(exterior, "FRONT FACADE - TWO STOREYS CLOSED");
            var rear = Group(exterior, "REAR FACADE - TWO STOREYS CLOSED");
            var left = Group(exterior, "LEFT FACADE - TWO STOREYS CLOSED");
            var right = Group(exterior, "RIGHT FACADE - TWO STOREYS CLOSED");

            for (int level = 0; level < 2; level++)
            {
                float y = level * Storey;
                for (int i = 0; i < CellsX; i++)
                {
                    Vector3 a = new Vector3(MinX + i * Cell, y, MaxZ);
                    Vector3 b = a + Vector3.right * Cell;
                    string path = level == 0 && i == 2
                        ? DoorWall
                        : ((i + level) % 3 == 0 ? WindowAlt : Window);
                    Segment(path, front, a, b,
                        $"EXTERIOR FRONT L{level + 1} {i + 1:00}");

                    a = new Vector3(MaxX - i * Cell, y, MinZ);
                    b = a + Vector3.left * Cell;
                    path = level == 0 && i < 4
                        ? CellWindow
                        : ((i + level) % 3 == 0 ? Wall : WindowAlt);
                    Segment(path, rear, a, b,
                        $"EXTERIOR REAR L{level + 1} {i + 1:00}");
                }

                for (int i = 0; i < CellsZ; i++)
                {
                    Vector3 a = new Vector3(MinX, y, MinZ + i * Cell);
                    Vector3 b = a + Vector3.forward * Cell;
                    string path = level == 0 && i < 3
                        ? CellWindow
                        : ((i + level) % 3 == 0 ? Wall : Window);
                    Segment(path, left, a, b,
                        $"EXTERIOR LEFT L{level + 1} {i + 1:00}");

                    a = new Vector3(MaxX, y, MaxZ - i * Cell);
                    b = a + Vector3.back * Cell;
                    path = (i + level) % 3 == 0 ? Wall : WindowAlt;
                    Segment(path, right, a, b,
                        $"EXTERIOR RIGHT L{level + 1} {i + 1:00}");
                }
            }

            var details = Group(exterior, "FACADE JOINTS PLINTHS AND ENTRANCE");
            for (int i = 0; i < CellsX; i++)
            {
                Vector3 frontA = new Vector3(MinX + i * Cell, 0f, MaxZ + 0.02f);
                Segment(i == 2 ? Trim : Plinth, details,
                    frontA, frontA + Vector3.right * Cell, $"front plinth {i + 1:00}");
                Vector3 rearA = new Vector3(MaxX - i * Cell, 0f, MinZ - 0.02f);
                Segment(Plinth, details,
                    rearA, rearA + Vector3.left * Cell, $"rear plinth {i + 1:00}");
            }
            for (int i = 0; i < CellsZ; i++)
            {
                Vector3 leftA = new Vector3(MinX - 0.02f, 0f, MinZ + i * Cell);
                Segment(Plinth, details,
                    leftA, leftA + Vector3.forward * Cell, $"left plinth {i + 1:00}");
                Vector3 rightA = new Vector3(MaxX + 0.02f, 0f, MaxZ - i * Cell);
                Segment(Plinth, details,
                    rightA, rightA + Vector3.back * Cell, $"right plinth {i + 1:00}");
            }

            var doorRotation = WallRotation(Vector3.right);
            Place(Door, details, new Vector3(-2.5f, 0f, MaxZ), doorRotation,
                  "SM_Bld_Door_01 - PUBLIC ENTRANCE LEAF");
            Primitive(details, "PUBLIC ENTRANCE CANOPY",
                new Vector3(-1.25f, 2.72f, MaxZ + 0.82f),
                new Vector3(4.3f, 0.18f, 1.7f), RoofMaterial, collider: false);
            Sit(Pillar, details, new Vector3(-3.15f, 0f, MaxZ + 1.35f), 0f,
                "SM_Bld_Base_Pillar_01 - ENTRANCE CANOPY LEFT");
            Sit(PillarAlt, details, new Vector3(0.65f, 0f, MaxZ + 1.35f), 0f,
                "SM_Bld_Base_Pillar_02 - ENTRANCE CANOPY RIGHT");
        }

        static void BuildFloor(Transform parent, float y, bool stairOpening)
        {
            var deck = Group(parent, y < 0.1f ? "GROUND FLOOR SLAB" : "UPPER FLOOR SLAB");
            for (int ix = 0; ix < CellsX; ix++)
                for (int iz = 0; iz < CellsZ; iz++)
                {
                    if (stairOpening && ix == CellsX - 1 && iz == CellsZ - 2) continue;
                    float x = MinX + ix * Cell;
                    float z = MinZ + iz * Cell;
                    Place(Floor, deck, new Vector3(x + Cell, y, z), Quaternion.identity,
                          $"floor {ix + 1:00}-{iz + 1:00}");
                }
        }

        static void BuildGroundInterior(Transform ground)
        {
            var cells = Group(ground, "MINI HOLDING - TWO COMPLETE CELLS");
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = new Vector3(MinX + i * Cell, 0f, -5f);
                Vector3 b = a + Vector3.right * Cell;
                bool doorway = i == 0 || i == 2;
                Segment(doorway ? CellDoorWall : CellWall, cells, a, b,
                        doorway ? $"cell door wall {i / 2 + 1}" : $"cell front wall {i + 1}");
                if (doorway)
                    Place(CellDoor, cells, a,
                          WallRotation(Vector3.right),
                          $"SM_Bld_Door_05 - CELL DOOR {i / 2 + 1}");
            }
            for (int i = 0; i < 2; i++)
            {
                Vector3 a = new Vector3(-2.5f, 0f, -5f - i * Cell);
                Segment(CellWall, cells, a, a + Vector3.back * Cell,
                        $"cell divider {i + 1}");
                a = new Vector3(2.5f, 0f, -5f - i * Cell);
                Segment(CellWall, cells, a, a + Vector3.back * Cell,
                        $"cell right return {i + 1}");
            }

            for (int cell = 0; cell < 2; cell++)
            {
                float x = cell == 0 ? -5.4f : -0.4f;
                SitProp(cells, "SM_Prop_Bed_01", new Vector3(x, 0f, -8.25f), 90f,
                        $"Cell {cell + 1} bunk");
                SitProp(cells, "SM_Prop_Sink_Toilet_01", new Vector3(x - 0.9f, 0f, -6.15f),
                        180f, $"Cell {cell + 1} stainless sanitary unit");
                SitProp(cells, "SM_Prop_Bench_03", new Vector3(x + 1.1f, 0f, -6.9f), 90f,
                        $"Cell {cell + 1} bench");
            }
            WallSign(cells, "SM_Sign_Cells_01", new Vector3(-4.8f, 2.05f, -4.88f), 180f,
                     "CELLS sign");

            var publicRoom = Group(ground, "PUBLIC LOBBY AND FRONT DESK");
            DeskStation(publicRoom, new Vector3(-3.8f, 0f, 3.2f), 180f, "Reception A", 0f);
            DeskStation(publicRoom, new Vector3(0.2f, 0f, 3.2f), 180f, "Reception B", 0.17f);
            SitProp(publicRoom, "SM_Prop_Bench_01", new Vector3(-4.4f, 0f, 7.5f), 180f,
                    "Public waiting bench 01");
            SitProp(publicRoom, "SM_Prop_Bench_02", new Vector3(1.1f, 0f, 7.5f), 180f,
                    "Public waiting bench 02");
            SitProp(publicRoom, "SM_Prop_Coffee_Table_01", new Vector3(-1.6f, 0f, 6.0f), 0f,
                    "Public waiting table");
            SitProp(publicRoom, "SM_Prop_WaterCooler_01", new Vector3(5.9f, 0f, 7.8f), 180f,
                    "Lobby water cooler");
            SitProp(publicRoom, "SM_Prop_Pot_Plant_03", new Vector3(-6.3f, 0f, 8.3f), 0f,
                    "Lobby plant");
            SitProp(publicRoom, "SM_Prop_Bin_01", new Vector3(5.7f, 0f, 6.6f), 0f,
                    "Lobby bin");
            WallSign(publicRoom, "SM_Sign_Police_01", new Vector3(3.6f, 2.05f, 9.45f), 0f,
                     "Lobby police crest");
            WallSign(publicRoom, "SM_Sign_Info_01", new Vector3(-5.6f, 1.7f, 9.86f), 0f,
                     "Public information");

            var booking = Group(ground, "BOOKING AND EVIDENCE INTAKE");
            DeskStation(booking, new Vector3(4.8f, 0f, -1.2f), 90f, "Booking", 0.31f);
            SitProp(booking, "SM_Prop_Filing_Cabinet_02", new Vector3(5.85f, 0f, -3.7f), 90f,
                    "Booking files");
            SitProp(booking, "SM_Prop_Camera_01", new Vector3(2.0f, 0f, -2.2f), 0f,
                    "Mugshot camera");
            SitProp(booking, "SM_Prop_Chair_04", new Vector3(2.0f, 0f, -0.8f), 180f,
                    "Booking subject chair");
            SitProp(booking, "SM_Prop_Handcuffs_01", new Vector3(5.05f, 0.83f, -1.05f), 0f,
                    "Booking cuffs");
            SitProp(booking, "SM_Prop_Evidence_Bag_01", new Vector3(4.55f, 0.83f, -1.2f), 0f,
                    "Booking evidence bag");
            WallSign(booking, "SM_Sign_Booking_01", new Vector3(3.7f, 2.05f, -4.86f), 180f,
                     "BOOKING sign");
            WallSign(booking, "SM_Sign_Mugshot_01", new Vector3(1.9f, 1.65f, -4.86f), 180f,
                     "Mugshot height chart");

            var circulation = Group(ground, "STAIRS SAFETY AND CORRIDOR DETAIL");
            Sit(Stairs, circulation, new Vector3(6.2f, 0f, 6.25f), 180f,
                "SM_Bld_Base_Stairs_02 - INTERNAL STAIR");
            SitProp(circulation, "SM_Prop_Fire_Extinguisher_01",
                    new Vector3(6.65f, 0f, 3.8f), 90f, "Ground floor extinguisher");
            SitProp(circulation, "SM_Prop_Clock_01", new Vector3(6.85f, 1.85f, 1.2f), 90f,
                    "Ground floor clock");
        }

        static void BuildUpperInterior(Transform upper)
        {
            var operations = Group(upper, "DETECTIVE BULLPEN AND RADIO OPERATIONS");
            var desks = new[]
            {
                new Vector3(-4.8f, Storey, 5.4f), new Vector3(-0.8f, Storey, 5.4f),
                new Vector3(3.2f, Storey, 5.4f), new Vector3(-4.8f, Storey, 0.9f),
                new Vector3(-0.8f, Storey, 0.9f), new Vector3(3.2f, Storey, 0.9f),
            };
            for (int i = 0; i < desks.Length; i++)
                DeskStation(operations, desks[i], i < 3 ? 180f : 0f,
                            $"Detective {i + 1:00}", i * 0.13f);

            SitProp(operations, "SM_Prop_Filing_Cabinet_02",
                    new Vector3(-5.8f, Storey, 8.75f), 180f, "Case files north 01");
            SitProp(operations, "SM_Prop_Filing_Cabinet_02",
                    new Vector3(-2.8f, Storey, 8.75f), 180f, "Case files north 02");
            SitProp(operations, "SM_Prop_Filing_Cabinet_01",
                    new Vector3(0.25f, Storey, 8.75f), 180f, "Case files north 03");
            WallSign(operations, "SM_Sign_Map_01", new Vector3(4.6f, Storey + 1.65f, 9.86f), 0f,
                     "Operations city map");
            PlaceProp(operations, "SM_Prop_Whiteboard_01",
                      new Vector3(-1.4f, Storey + 1.65f, 9.86f), Quaternion.identity,
                      "Operations whiteboard");
            WallSign(operations, "SM_Sign_Police_Chief_01",
                     new Vector3(-6.7f, Storey + 1.8f, 3.2f), 90f, "Chief office plaque");

            var armoury = Group(upper, "EVIDENCE STORE AND SMALL ARMOURY");
            for (int i = 0; i < 3; i++)
            {
                SitProp(armoury, i == 0 ? "SM_Prop_Weapon_Locker_01" : "SM_Prop_Weapon_Locker_02",
                        new Vector3(-5.8f + i * 1.65f, Storey, -8.75f), 0f,
                        $"Weapon locker {i + 1:00}");
                SitProp(armoury, "SM_Prop_Shelf_0" + (i + 1),
                        new Vector3(0.0f + i * 1.7f, Storey, -8.55f), 0f,
                        $"Evidence shelf {i + 1:00}");
            }
            for (int i = 0; i < 8; i++)
            {
                string piece = "SM_Prop_Box_0" + (i % 4 + 1);
                SitProp(armoury, piece,
                        new Vector3(-0.1f + (i % 4) * 1.35f, Storey,
                                    -6.95f + (i / 4) * 1.15f), i * 19f,
                        $"Evidence archive box {i + 1:00}");
            }
            for (int i = 0; i < 6; i++)
                SitProp(armoury, "SM_Prop_Evidence_Bag_0" + (i % 3 + 1),
                        new Vector3(-0.25f + (i % 3) * 1.45f, Storey + 0.92f,
                                    -8.45f + (i / 3) * 0.6f), i * 31f,
                        $"Logged evidence bag {i + 1:00}");
            WallSign(armoury, "SM_Sign_Evidence_01",
                     new Vector3(5.9f, Storey + 1.75f, -9.86f), 180f, "EVIDENCE sign");

            var breakRoom = Group(upper, "WATCH BREAK ROOM");
            SitProp(breakRoom, "SM_Prop_Table_01", new Vector3(4.7f, Storey, -2.8f), 0f,
                    "Break table");
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f;
                var offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 1.3f;
                SitProp(breakRoom, "SM_Prop_Chair_03",
                        new Vector3(4.7f, Storey, -2.8f) + offset, angle + 180f,
                        $"Break chair {i + 1:00}");
            }
            SitProp(breakRoom, "SM_Prop_Fridge_01", new Vector3(6.2f, Storey, -5.8f), 90f,
                    "Break room fridge");
            SitProp(breakRoom, "SM_Prop_Microwave_01",
                    new Vector3(4.95f, Storey + 0.92f, -5.7f), 180f, "Break room microwave");
            SitProp(breakRoom, "SM_Prop_Coffee_Machine_Dripper_01",
                    new Vector3(4.0f, Storey + 0.92f, -5.7f), 180f, "Coffee dripper");
            SitProp(breakRoom, "SM_Prop_Coffee_Pot_01",
                    new Vector3(3.55f, Storey + 0.92f, -5.7f), 180f, "Coffee pot");
            SitProp(breakRoom, "SM_Prop_Donut_Box_01",
                    new Vector3(4.7f, Storey + 0.8f, -2.8f), 17f, "Night-watch donuts");
            SitProp(breakRoom, "SM_Prop_Kettle_01",
                    new Vector3(5.7f, Storey + 0.92f, -5.7f), 180f, "Break room kettle");
            SitProp(breakRoom, "SM_Prop_Bin_02", new Vector3(6.25f, Storey, -4.4f), 0f,
                    "Break room bin");
            WallSign(breakRoom, "SM_Sign_Break_Room_01",
                     new Vector3(6.86f, Storey + 1.8f, -1.5f), 270f, "BREAK ROOM sign");
        }

        static void BuildRoof(Transform roof)
        {
            Primitive(roof, "SEALED FLAT ROOF",
                new Vector3(0f, Storey * 2f + 0.08f, 0f),
                new Vector3(15.3f, 0.16f, 20.3f), RoofMaterial, collider: true);

            var edges = Group(roof, "COMPLETE ROOF PARAPET");
            for (int i = 0; i < CellsX; i++)
            {
                Vector3 a = new Vector3(MinX + i * Cell, Storey * 2f, MaxZ);
                Segment(RoofEdge, edges, a, a + Vector3.right * Cell, $"front roof edge {i + 1:00}");
                a = new Vector3(MaxX - i * Cell, Storey * 2f, MinZ);
                Segment(RoofEdge, edges, a, a + Vector3.left * Cell, $"rear roof edge {i + 1:00}");
            }
            for (int i = 0; i < CellsZ; i++)
            {
                Vector3 a = new Vector3(MinX, Storey * 2f, MinZ + i * Cell);
                Segment(RoofEdge, edges, a, a + Vector3.forward * Cell, $"left roof edge {i + 1:00}");
                a = new Vector3(MaxX, Storey * 2f, MaxZ - i * Cell);
                Segment(RoofEdge, edges, a, a + Vector3.back * Cell, $"right roof edge {i + 1:00}");
            }
            foreach (var at in new[]
                     {
                         new Vector3(MinX, 6f, MinZ), new Vector3(MinX, 6f, MaxZ),
                         new Vector3(MaxX, 6f, MinZ), new Vector3(MaxX, 6f, MaxZ),
                     })
                Sit(RoofCorner, edges, at, 0f, "SM_Bld_Roof_Edge_Corner_01");

            SitProp(roof, "SM_Prop_Aircon_Roof_01", new Vector3(-4.4f, 6.18f, -2.6f), 0f,
                    "Roof HVAC 01");
            SitProp(roof, "SM_Prop_Aircon_Roof_02", new Vector3(0.0f, 6.18f, -2.4f), 90f,
                    "Roof HVAC 02");
            SitProp(roof, "SM_Prop_Aircon_Roof_03", new Vector3(4.25f, 6.18f, -2.8f), 180f,
                    "Roof HVAC 03");
        }

        static void BuildExteriorDetail(Transform exterior, Transform roof)
        {
            var signs = Group(exterior, "POLICE IDENTITY AND EXTERIOR HARDWARE");
            BuildPoliceIdentity(signs);
            for (int i = 0; i < 2; i++)
            {
                SitProp(signs, "SM_Prop_DownPipe_0" + (i + 1),
                        new Vector3(i == 0 ? MinX - 0.12f : MaxX + 0.12f, 0f, MinZ + 1.2f),
                        i == 0 ? 270f : 90f, $"Rear downpipe {i + 1}");
                SitProp(signs, "SM_Prop_Aircon_0" + (i + 1),
                        new Vector3(i == 0 ? -4.2f : 3.7f, 3.5f, MinZ - 0.16f), 180f,
                        $"Rear wall aircon {i + 1}");
            }
            foreach (var item in new[]
                     {
                         new { p = new Vector3(MinX + 0.2f, 5.45f, MaxZ + 0.15f), y = 25f },
                         new { p = new Vector3(MaxX - 0.2f, 5.45f, MaxZ + 0.15f), y = -25f },
                         new { p = new Vector3(MinX - 0.15f, 2.65f, MinZ + 0.3f), y = 225f },
                         new { p = new Vector3(MaxX + 0.15f, 2.65f, MinZ + 0.3f), y = 135f },
                     })
                SitProp(signs, "SM_Prop_Security_Camera_01", item.p, item.y,
                        "Exterior security camera");

            SitProp(roof, "SM_Prop_Toolbox_01", new Vector3(1.3f, 6.18f, 3.0f), 17f,
                    "Roof maintenance toolbox", optional: true);
        }

        static void BuildPoliceIdentity(Transform parent)
        {
            var identity = Group(parent, "POLICE ENTRANCE SIGN");
            // The vendor's station-name asset is a small horizontal plaque. Use a
            // readable fascia above the canopy, facing the same street as the door.
            Primitive(identity, "POLICE SIGN BACKBOARD",
                new Vector3(-1.25f, 3.1f, MaxZ + 1.72f),
                new Vector3(4.3f, 0.72f, 0.12f), RoofMaterial, collider: false);
            var crest = WallSign(identity, "SM_Sign_Police_01",
                new Vector3(-2.92f, 3.1f, MaxZ + 1.93f), 180f,
                "PRECINCT BADGE");
            crest.transform.localScale *= 0.34f;

            var label = new GameObject("POLICE LETTERING").AddComponent<TMPro.TextMeshPro>();
            label.transform.SetParent(identity, false);
            label.transform.localPosition = new Vector3(-0.8f, 3.1f, MaxZ + 1.79f);
            label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            label.font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            label.text = "POLICE";
            label.fontStyle = TMPro.FontStyles.Bold;
            label.color = new Color(0.96f, 0.96f, 0.9f);
            label.alignment = TMPro.TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.rectTransform.sizeDelta = new Vector2(3.1f, 0.56f);
            label.enableAutoSizing = true;
            label.fontSizeMin = 1f;
            label.fontSizeMax = 10f;
            label.ForceMeshUpdate();
        }

        [CliCommand("gangsters_police_precinct_signage",
            "Refresh only the compact precinct signage, preserving the building and scene layout.",
            MainThreadRequired = true)]
        public static object RefreshSignage()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Leave Play Mode before editing precinct signage.");
            var root = PrefabUtility.LoadPrefabContents(CompactPath);
            try
            {
                var signs = Find(root.transform, "POLICE IDENTITY AND EXTERIOR HARDWARE");
                foreach (Transform child in signs.Cast<Transform>().ToArray())
                    if (child.name == "POLICE ENTRANCE SIGN" ||
                        child.name.Contains("MAIN STREET SIGN") || child.name.Contains("PRECINCT BADGE"))
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                BuildPoliceIdentity(signs);
                var lobby = Find(root.transform, "SM_Sign_Police_01 - Lobby police crest");
                // Its relief extends +Z from the pivot. Keep that entire thickness
                // inside the facade's inner face instead of through the front window.
                lobby.localPosition = new Vector3(3.6f, 2.05f, 9.45f);
                SetStatic(signs.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, CompactPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            return Audit();
        }

        static void BuildLighting(Transform parent)
        {
            var warm = new Color(1f, 0.87f, 0.69f);
            int index = 0;
            foreach (float y in new[] { 2.72f, 5.72f })
                foreach (float x in new[] { -4.8f, 0f, 4.8f })
                    foreach (float z in new[] { -6.7f, 0f, 6.7f })
                    {
                        var at = new Vector3(x, y, z);
                        PlaceProp(parent, "SM_Prop_Light_01", at, Quaternion.identity,
                                  $"Ceiling practical {++index:00}");
                        Point(parent, "Warm practical light", at - Vector3.up * 0.12f,
                              warm, 5.2f, 2.1f);
                    }
        }

        static void DeskStation(
            Transform parent, Vector3 at, float yaw, string label, float variation)
        {
            var group = Group(parent, label.ToUpperInvariant() + " WORKSTATION");
            var facing = Quaternion.Euler(0f, yaw, 0f);
            SitProp(group, variation > 0.25f ? "SM_Prop_Desk_03" :
                    variation > 0.1f ? "SM_Prop_Desk_02" : "SM_Prop_Desk_01",
                    at, yaw, label + " desk");
            SitProp(group, variation > 0.2f ? "SM_Prop_Chair_02" : "SM_Prop_Chair_01",
                    at + facing * new Vector3(0f, 0f, -1.0f), yaw + 180f,
                    label + " chair");

            float top = at.y + 0.83f;
            PlaceProp(group, "SM_Prop_Monitor_01",
                at + facing * new Vector3(0.05f, top - at.y, 0.14f), facing,
                label + " monitor");
            PlaceProp(group, "SM_Prop_Computer_01",
                at + facing * new Vector3(-0.72f, top - at.y, 0.08f), facing,
                label + " computer");
            PlaceProp(group, "SM_Prop_Keyboard_01",
                at + facing * new Vector3(0.05f, top - at.y, -0.28f), facing,
                label + " keyboard");
            PlaceProp(group, "SM_Prop_Mousepad_0" + (variation > 0.2f ? "2" : "1"),
                at + facing * new Vector3(0.55f, top - at.y + 0.01f, -0.27f), facing,
                label + " mousepad");
            PlaceProp(group, "SM_Prop_Mouse_01",
                at + facing * new Vector3(0.55f, top - at.y + 0.025f, -0.27f), facing,
                label + " mouse");
            PlaceProp(group, "SM_Prop_Phone_01",
                at + facing * new Vector3(0.75f, top - at.y, 0.18f), facing,
                label + " phone");
            PlaceProp(group, variation > 0.2f ? "SM_Prop_Lamp_02" : "SM_Prop_Lamp_01",
                at + facing * new Vector3(-0.75f, top - at.y, 0.2f), facing,
                label + " desk lamp");
            PlaceProp(group, variation > 0.2f ? "SM_Prop_Mug_02" : "SM_Prop_Mug_01",
                at + facing * new Vector3(0.72f, top - at.y, -0.18f), facing,
                label + " mug");
            PlaceProp(group, "SM_Prop_Case_File_01",
                at + facing * new Vector3(-0.45f, top - at.y + 0.015f, -0.26f), facing,
                label + " case file");
            PlaceProp(group, "SM_Prop_Clipboard_01",
                at + facing * new Vector3(-0.2f, top - at.y + 0.02f, -0.32f), facing,
                label + " clipboard");
        }

        public static AuditResult Audit()
        {
            var failures = new List<string>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompactPath);
            if (prefab == null)
                return new AuditResult
                {
                    passed = false,
                    prefab = CompactPath,
                    failures = new[] { "compact prefab is missing" },
                };

            int renderers = prefab.GetComponentsInChildren<Renderer>(true).Length;
            int props = CountProps(prefab.transform);
            int cells = Count(prefab.transform, "SM_Bld_Wall_Cell_");
            int doors = Count(prefab.transform, "SM_Bld_Wall_Door_Cell_");
            int underground = prefab.GetComponentsInChildren<Transform>(true).Count(t =>
                t.name.IndexOf("UNDERGROUND", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.name.IndexOf("GARAGE RAMP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.name.StartsWith("SM_Env_Road_Ramp_", StringComparison.Ordinal));
            int lights = prefab.GetComponentsInChildren<Light>(true).Length;

            var front = Find(prefab.transform, "FRONT FACADE - TWO STOREYS CLOSED");
            var rear = Find(prefab.transform, "REAR FACADE - TWO STOREYS CLOSED");
            var left = Find(prefab.transform, "LEFT FACADE - TWO STOREYS CLOSED");
            var right = Find(prefab.transform, "RIGHT FACADE - TWO STOREYS CLOSED");
            int frontModules = DirectChildren(front);
            int rearModules = DirectChildren(rear);
            int leftModules = DirectChildren(left);
            int rightModules = DirectChildren(right);

            RequireFacade(failures, "front", front, frontModules, CellsX * 2, 14.7f, true);
            RequireFacade(failures, "rear", rear, rearModules, CellsX * 2, 14.7f, true);
            RequireFacade(failures, "left", left, leftModules, CellsZ * 2, 19.7f, false);
            RequireFacade(failures, "right", right, rightModules, CellsZ * 2, 19.7f, false);

            if (renderers < 320) failures.Add($"only {renderers} renderers are present");
            if (props < 145) failures.Add($"only {props} props/signs are present");
            if (cells < 6 || doors < 2)
                failures.Add("the two-cell mini holding suite is incomplete");
            if (underground != 0)
                failures.Add($"{underground} underground/ramp object(s) survived");
            if (lights < 14) failures.Add("interior practical lighting is incomplete");
            if (Find(prefab.transform, "POLICE LETTERING")?.GetComponent<TMPro.TextMeshPro>()?.text != "POLICE")
                failures.Add("the street-facing POLICE STATION sign is missing");
            if (Count(prefab.transform, "SM_Prop_Flag_") != 0)
                failures.Add("a vendor national flag remains in the compact precinct");

            var visual = prefab.GetComponent<PolicePrecinctVisual>();
            if (visual == null)
                failures.Add("PolicePrecinctVisual topology is missing");
            else
            {
                if (visual.PublicEntrance == null || visual.HoldingCells == null)
                    failures.Add("entrance or holding-cell marker is missing");
                if (visual.UndergroundGarage != null || visual.GarageRampTop != null ||
                    visual.GarageRampBottom != null)
                    failures.Add("compact topology still advertises an underground garage");
            }

            var bounds = BoundsOf(prefab);
            if (bounds.size.x > 19f || bounds.size.z > 24f)
                failures.Add($"building is still too large ({bounds.size.x:F1} x {bounds.size.z:F1} m)");

            return new AuditResult
            {
                passed = failures.Count == 0,
                prefab = CompactPath,
                renderers = renderers,
                props = props,
                cellWalls = cells,
                cellDoors = doors,
                frontWallModules = frontModules,
                rearWallModules = rearModules,
                leftWallModules = leftModules,
                rightWallModules = rightModules,
                undergroundObjects = underground,
                lights = lights,
                width = Round(bounds.size.x),
                height = Round(bounds.size.y),
                depth = Round(bounds.size.z),
                failures = failures.ToArray(),
            };
        }

        static void RequireFacade(
            List<string> failures, string side, Transform group, int modules,
            int expectedModules, float requiredSpan, bool alongX)
        {
            if (group == null)
            {
                failures.Add(side + " facade group is missing");
                return;
            }
            if (modules != expectedModules)
                failures.Add($"{side} facade has {modules}/{expectedModules} wall modules");
            foreach (Transform module in group)
            {
                var moduleBounds = BoundsOf(module.gameObject);
                float floorY = module.position.y;
                if (Vector3.Dot(module.up, Vector3.up) < 0.999f ||
                    Mathf.Abs(moduleBounds.min.y - floorY) > 0.15f ||
                    moduleBounds.max.y < floorY + Storey - 0.1f)
                    failures.Add($"{module.name} does not enclose its own storey");
            }
            var bounds = BoundsOf(group.gameObject);
            float span = alongX ? bounds.size.x : bounds.size.z;
            if (span < requiredSpan || bounds.min.y < -0.15f || bounds.min.y > 0.15f ||
                bounds.max.y < 5.9f)
                failures.Add($"{side} facade does not close both storeys " +
                             $"({span:F1} m span, y {bounds.min.y:F1}..{bounds.max.y:F1})");
        }

        static string Describe(AuditResult a) =>
            $"passed={a.passed}, {a.width:F1} x {a.height:F1} x {a.depth:F1} m, " +
            $"walls F/R/L/R {a.frontWallModules}/{a.rearWallModules}/" +
            $"{a.leftWallModules}/{a.rightWallModules}, {a.renderers} renderers, " +
            $"{a.props} props/signs, {a.cellDoors} cell doors, " +
            $"{a.undergroundObjects} underground pieces, {a.lights} lights";

        static Transform Group(Transform parent, string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        static GameObject Segment(
            string path, Transform parent, Vector3 from, Vector3 to, string label)
        {
            Vector3 direction = to - from;
            if (Mathf.Abs(direction.magnitude - Cell) > 0.02f)
                throw new InvalidOperationException(label + " is not one 2.5 m wall module.");
            var rotation = WallRotation(direction.normalized);
            return Place(path, parent, from, rotation,
                         Path.GetFileNameWithoutExtension(path) + " - " + label);
        }

        // FromToRotation(-X, +X) is ambiguous and Unity may choose a 180-degree roll,
        // which turns an otherwise present wall upside-down below the ground. These
        // modules are architecture: they may yaw, but they must never pitch or roll.
        static Quaternion WallRotation(Vector3 direction)
        {
            direction.y = 0f;
            direction.Normalize();
            float yaw = Mathf.Atan2(direction.z, -direction.x) * Mathf.Rad2Deg;
            return Quaternion.Euler(0f, yaw, 0f);
        }

        static GameObject SitProp(
            Transform parent, string piece, Vector3 at, float yaw, string name,
            bool optional = false) =>
            Sit(Props + piece + ".prefab", parent, at, yaw,
                piece + " - " + name, optional);

        static GameObject WallSign(
            Transform parent, string piece, Vector3 at, float yaw, string name) =>
            PlaceProp(parent, piece, at, Quaternion.Euler(0f, yaw, 0f), name, signs: true);

        static GameObject PlaceProp(
            Transform parent, string piece, Vector3 at, Quaternion rotation, string name,
            bool signs = false)
        {
            string path = (signs ? Signs : Props) + piece + ".prefab";
            return Place(path, parent, at, rotation, piece + " - " + name);
        }

        static GameObject Sit(
            string path, Transform parent, Vector3 at, float yaw, string name,
            bool optional = false)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                if (optional) return null;
                throw new FileNotFoundException("Missing compact precinct asset", path);
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            var bounds = BoundsOf(go);
            go.transform.position += new Vector3(
                at.x - bounds.center.x, at.y - bounds.min.y, at.z - bounds.center.z);
            return go;
        }

        static GameObject Place(
            string path, Transform parent, Vector3 at, Quaternion rotation, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new FileNotFoundException("Missing compact precinct asset", path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(at, rotation);
            return go;
        }

        static GameObject Primitive(
            Transform parent, string name, Vector3 at, Vector3 size,
            string materialPath, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider)
            {
                var box = go.GetComponent<BoxCollider>();
                if (box != null) UnityEngine.Object.DestroyImmediate(box);
            }
            return go;
        }

        static Transform Marker(Transform parent, string name, Vector3 at, Vector3 forward)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
            marker.localPosition = at;
            if (forward.sqrMagnitude > 0.001f)
                marker.localRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            return marker;
        }

        static void Point(
            Transform parent, string name, Vector3 at, Color colour, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = colour;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Mixed;
            light.renderMode = LightRenderMode.Auto;
        }

        static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        static int DirectChildren(Transform root) => root == null ? 0 : root.childCount;

        static int CountProps(Transform root) =>
            Count(root, "SM_Prop_") + Count(root, "SM_Sign_");

        static int Count(Transform root, string prefix) =>
            root.GetComponentsInChildren<Transform>(true)
                .Count(t => t.name.StartsWith(prefix, StringComparison.Ordinal));

        static Bounds BoundsOf(GameObject go) =>
            BoundsOf(go.GetComponentsInChildren<Renderer>(true));

        static Bounds BoundsOf(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.zero);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static float Round(float value) => Mathf.Round(value * 10f) / 10f;

        static void SetStatic(GameObject root)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.isStatic = true;
                foreach (var body in child.GetComponents<Rigidbody>())
                    UnityEngine.Object.DestroyImmediate(body);
                foreach (var renderer in child.GetComponents<Renderer>())
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/CityKit"))
                AssetDatabase.CreateFolder("Assets", "CityKit");
            if (!AssetDatabase.IsValidFolder(OutputDir))
                AssetDatabase.CreateFolder("Assets/CityKit", "PolicePrecinct");
        }

        [CliCommand("gangsters_police_precinct_compact",
                    "Build and audit the fully enclosed, surface-parking compact precinct.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "police", "precinct", "residential", "prefab" })]
        public static object BuildFromCli()
        {
            if (EditorApplication.isPlaying)
                return new { passed = false, reason = "Leave Play Mode before building." };
            return Build();
        }

        [CliCommand("gangsters_police_precinct_compact_audit",
                    "Audit all four compact precinct facades and prohibit underground geometry.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "police", "precinct", "audit" })]
        public static object AuditFromCli() => Audit();
    }
}
