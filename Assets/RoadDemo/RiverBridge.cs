using System;
using System.Collections.Generic;
using UnityEngine;
using static RoadDemo.Composer;

namespace RoadDemo
{
    /// <summary>
    /// The river itself and what crosses it: the water, one plane; the far bank's wall and
    /// kerb; and every bridge's dressing over the water - the walkways with their parapets,
    /// the soffit under the deck, the girders every fifteen metres, the posts where the
    /// parapets meet the banks and the lamps along them. The deck is the road's own tiles,
    /// laid by <see cref="CoreRoads.Lay"/> like any street; the bridge is what is hung on
    /// them, which is exactly how the grid city bridges its river
    /// (<c>RoadDemoBuilder.DressBridge</c>, whose placing this is).
    ///
    /// Every bridge is a BASCULE: the palm city's two leaves and their towers over a forty
    /// metre channel in the middle of the water, with fixed approaches from each bank. A
    /// street's carriageway is one leaf wide; the boulevard's two carriageways get a leaf
    /// each, side by side with the median between the towers. The road's tiles are left
    /// off the channel (the leaves carry their own deck) and the leaves are stood shut;
    /// raising them is <c>Bascule</c>'s business, and the boat that asks is <c>RiverBoat</c>.
    ///
    /// Everything is placed in the core's own coordinates under the root it is given; the
    /// host moves the quarter afterwards.
    /// </summary>
    public static class RiverBridge
    {
        public const float Cell = CoreLayout.Cell;

        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string PalmEnv = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";

        const string Water = "Assets/Synty/PNB_Core/Prefabs/SM_Env_Water_Plane_01.prefab";
        const string Wall = CityEnv + "SM_Env_WaterEdge_Straight_03.prefab";
        const string Abutment = CityEnv + "SM_Env_Bridge_Wall_01.prefab";
        const string KerbTile = CityEnv + "SM_Env_Sidewalk_Straight_01.prefab";
        const string Paving = CityEnv + "SM_Env_Sidewalk_01.prefab";
        const string Walkway = CityEnv + "SM_Env_Bridge_Edge_01.prefab";
        const string Soffit = CityEnv + "SM_Env_Bridge_Underside_01.prefab";
        const string Girder = CityEnv + "SM_Env_Bridge_Support_01.prefab";
        const string Post = CityEnv + "SM_Env_Bridge_Pillar_01.prefab";
        const string PierLamp = PalmProps + "SM_Prop_Pier_Lamp_01.prefab";
        const string Leaf = PalmEnv + "SM_Env_Drawbridge_01.prefab";
        const string Tower = PalmEnv + "SM_Env_Drawbridge_Base_01.prefab";

        public const float WaterY = QuayBlocks.WaterY;
        /// <summary>The channel a bascule opens, in metres: two leaves of twenty.</summary>
        public const float Channel = 40f;
        /// <summary>How far the water runs past the line's two ends, so the river does not
        /// stop where the core does; the boat sails from one reach to the other.</summary>
        public const float Reach = 1000f;
        /// <summary>The boulevard's two carriageways, off its crown: the lanes lie at 7.5
        /// and 12.5 m (<c>RasterGraph.Boulevard</c>), so each carriageway is centred at 10.</summary>
        static readonly float[] BoulevardSeats = { -10f, 10f };
        static readonly float[] StreetSeats = { 0f };

        /// <summary>The bridge's channel, where no road tile goes down: forty metres in the
        /// middle of the water, the band's width.</summary>
        public static Rect ChannelOf(CoreLayout.Plan plan, CoreLayout.Bridge bridge)
        {
            float mid = (plan.River.Wall + plan.River.FarWater) * 0.5f;
            return Rect.MinMaxRect(mid - Channel * 0.5f, bridge.Band.yMin, mid + Channel * 0.5f, bridge.Band.yMax);
        }

        /// <summary>Which of the raster's cells <see cref="CoreRoads.Lay"/> leaves bare:
        /// the ones in a channel. Null when the plan has no river.</summary>
        public static Func<int, int, bool> Skip(CoreLayout.Plan plan, CoreRoads.Raster raster)
        {
            if (plan.Quays.Count == 0 || plan.Bridges.Count == 0) return null;
            var channels = new List<Rect>();
            foreach (var bridge in plan.Bridges) channels.Add(ChannelOf(plan, bridge));
            return (i, j) =>
            {
                var centre = new Vector2(raster.X(i) + Cell * 0.5f, raster.Z(j) + Cell * 0.5f);
                foreach (var channel in channels) if (channel.Contains(centre)) return true;
                return false;
            };
        }

        /// <summary>The name of the bridge's deck under the river root: what
        /// <c>CoreDistrict</c> finds to hang the <see cref="Bascule"/> on.</summary>
        public static string DeckName(CoreLayout.Bridge bridge) =>
            bridge.Boulevard ? "Boulevard bridge" : $"Bridge z {bridge.Band.yMin:F0}";

        /// <summary>Stands the river and dresses the bridges, under the root, in the core's
        /// coordinates.</summary>
        public static void Dress(CoreLayout.Plan plan, Transform root, Func<GameObject, Transform, GameObject> raise, bool layWater = true)
        {
            if (plan.Quays.Count == 0) return;
            Begin(raise);
            var line = plan.River;

            // the water: one plane, measured and scaled to the rectangle, placed by its centre
            var plane = layWater ? Raise(Water, root) : null;
            if (plane != null)
            {
                plane.name = "Water";
                var b = Box(Water);
                float sx = Mathf.Max(0.01f, b.size.x), sz = Mathf.Max(0.01f, b.size.z);
                float x0 = Mathf.Min(line.Wall, line.FarWater), x1 = Mathf.Max(line.Wall, line.FarWater);
                float z0 = line.Z0 - Reach, z1 = line.Z1 + Reach;
                plane.transform.localScale = new Vector3((x1 - x0) / sx, 1f, (z1 - z0) / sz);
                plane.transform.position = new Vector3((x0 + x1) * 0.5f - b.center.x * (x1 - x0) / sx, WaterY - b.center.y,
                                                       (z0 + z1) * 0.5f - b.center.z * (z1 - z0) / sz);
            }

            // the wall pieces face the water with their local -Z: on the bank with the water
            // to its east a piece stands turned -90 with its pivot at the cell's south end,
            // on the bank with the water to its west turned 90 with its pivot at the north
            // end (RoadDemoBuilder.BuildRiver's two banks)
            void WallPiece(string path, Transform under, float x, float z, bool waterEast, string name)
            {
                var piece = Raise(path, under);
                if (piece == null) return;
                piece.name = name;
                piece.transform.SetPositionAndRotation(new Vector3(x, 0f, waterEast ? z : z + Cell),
                                                       Quaternion.Euler(0f, waterEast ? -90f : 90f, 0f));
            }

            // the far bank: its wall, with the water on the core's side; the apron between
            // the wall and its road, paved, with a kerb along the road; and the kerb beyond
            // the road, its kerb toward the road. Under a bridge the apron is the road's
            float apronLo = Mathf.Min(line.FarWater, line.FarRoad);
            var far = new GameObject("Far bank").transform;
            far.SetParent(root, false);
            for (float z = line.Z0; z < line.Z1 - 0.1f; z += Cell)
            {
                bool under = false;
                foreach (var bridge in plan.Bridges) if (z + Cell * 0.5f > bridge.Band.yMin && z + Cell * 0.5f < bridge.Band.yMax) under = true;
                WallPiece(under ? Abutment : Wall, far, line.FarWater, z, !line.East, under ? "Abutment" : "Quay");
                if (!under)
                    for (int c = 0; c < CoreLayout.FarApron; c++)
                    {
                        float x = apronLo + c * Cell;
                        bool atRoad = line.East ? c == CoreLayout.FarApron - 1 : c == 0;
                        Lay(atRoad ? KerbTile : Paving, far, x, z, Cell, Cell, atRoad ? (line.East ? 90f : 270f) : 0f);
                    }
                Lay(KerbTile, far, Mathf.Min(line.FarLand, line.BankEnd), z, Cell, Cell, line.East ? 270f : 90f);
            }
            // and the near bank's abutments under the bridges, where the promenade's wall stops
            var near = new GameObject("Abutments").transform;
            near.SetParent(root, false);
            foreach (var bridge in plan.Bridges)
                for (float z = bridge.Band.yMin; z < bridge.Band.yMax - 0.1f; z += Cell)
                    WallPiece(Abutment, near, line.Wall, z, line.East, "Abutment");

            foreach (var bridge in plan.Bridges) One(plan, bridge, root);
        }

        /// <summary>One bridge over the water, bank to bank, along x: fixed approaches from
        /// each bank and the leaves over the channel in the middle.</summary>
        static void One(CoreLayout.Plan plan, CoreLayout.Bridge bridge, Transform root)
        {
            var line = plan.River;
            var deck = new GameObject(DeckName(bridge)).transform;
            deck.SetParent(root, false);
            float crown = (bridge.Band.yMin + bridge.Band.yMax) * 0.5f;
            float half = bridge.Band.height * 0.5f;
            float bankLo = Mathf.Min(line.Wall, line.FarWater), bankHi = Mathf.Max(line.Wall, line.FarWater);
            var channel = ChannelOf(plan, bridge);
            float chLo = channel.xMin, chHi = channel.xMax;
            bool InChannel(float m) => m + Cell * 0.5f > chLo && m + Cell * 0.5f < chHi;

            // walkways with their parapets outside the carriageway, and the soffit under
            // every cell of it, over the water only - and never over the channel
            for (float m = bankLo; m < bankHi - 0.1f; m += Cell)
            {
                if (InChannel(m)) continue;
                Piece(Walkway, deck, new Vector3(m, 0f, crown + half), -90f, "Walkway");
                Piece(Walkway, deck, new Vector3(m + Cell, 0f, crown - half), 90f, "Walkway");
                for (float x = -half; x < half - 0.1f; x += Cell)
                    Piece(Soffit, deck, new Vector3(m + Cell, 0f, crown + x + Cell), 90f, "Soffit");
            }
            // girders every fifteen metres over the water, two side by side under a boulevard
            float[] seats = half > 12f ? new[] { -8f, 8f } : new[] { 0f };
            for (float m = bankLo + 7.5f; m < bankHi - 3f; m += 15f)
            {
                if (m > chLo - 3f && m < chHi + 3f) continue;
                foreach (float seat in seats) Piece(Girder, deck, new Vector3(m, -1.8f, crown + seat), 90f, "Girder");
            }
            // the parapets' end posts at the banks, and lamps down the parapets turned in
            float outer = half + 4.6f;
            foreach (float bank in new[] { bankLo, bankHi })
                foreach (float side in new[] { -outer, outer })
                    Piece(Post, deck, new Vector3(bank, 0f, crown + side), 0f, "Post");
            for (float m = bankLo + 10f; m < bankHi - 6f; m += 16f)
            {
                if (m > chLo - 1f && m < chHi + 1f) continue;
                foreach (float side in new[] { -outer + 0.4f, outer - 0.4f })
                    Sit(PierLamp, deck, m, crown + side, side < 0f ? 0f : 180f, 0.15f);
            }

            // the towers on the channel's edges and the leaves between them, shut: one
            // pair over a street's carriageway, a pair over each of the boulevard's two.
            // The leaf runs twenty metres along its own +Z from a pivot at its shore end and
            // both leaf and tower are centred five metres off that pivot along their -X, so
            // the pivot stands five metres off the carriageway's centre: on the south side
            // for the west leaf (turned +Z east) and the north side for the east leaf
            // (turned +Z west)
            bool missing = false;
            foreach (float seat in bridge.Boulevard ? BoulevardSeats : StreetSeats)
                foreach (var (x, yaw, dz, name) in new[] { (chLo, 90f, -5f, "West"), (chHi, 270f, 5f, "East") })
                {
                    string which = bridge.Boulevard ? (seat < 0f ? " south" : " north") : "";
                    var tower = Piece(Tower, deck, new Vector3(x, 0f, crown + seat + dz), yaw, name + " tower" + which);
                    var leaf = Piece(Leaf, deck, new Vector3(x, 0f, crown + seat + dz), yaw, name + " leaf" + which);
                    if (tower == null || leaf == null) missing = true;
                }
            if (missing)
                Debug.LogWarning("[RiverBridge] the palm city's drawbridge is missing; the channel stands open.");
        }

        /// <summary>A piece stood by its pivot, exactly where the grid's bridge stands it.</summary>
        static GameObject Piece(string path, Transform parent, Vector3 at, float yaw, string name)
        {
            var go = Raise(path, parent);
            if (go == null) return null;
            go.name = name;
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            return go;
        }
    }
}
