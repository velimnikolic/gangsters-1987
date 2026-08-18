using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    public enum EdgeKind { Sand, Harbour }

    // What the city stands against past its last road: the plain sand fringe it
    // always had, or the harbour - the bay the city sits on, water off the last
    // road's kerb behind a quay, piers out into it, boats alongside. Nothing else:
    // no backdrop, no silhouettes - the city is the city, and what it needs is more
    // of itself, not scenery round it.
    public partial class RoadDemoBuilder
    {
        [Header("Beyond the grid")]
        public EdgeKind southEdge = EdgeKind.Harbour;
        public EdgeKind northEdge = EdgeKind.Sand;
        public EdgeKind eastEdge = EdgeKind.Sand;
        public EdgeKind westEdge = EdgeKind.Sand;

        /// <summary>How far the harbour's water reaches out.</summary>
        const float HarbourDepth = 380f;

        // ---------------------------------------------------------------- the kit

        GameObject _dockPlatform, _dockPillar, _dockRailing;
        bool _edgeKitLoaded;

        void LoadEdgeKit()
        {
            if (_edgeKitLoaded) return;
            _edgeKitLoaded = true;
            const string PalmBld = "Assets/Synty/PolygonPalmCity/Prefabs/Buildings/";
            _dockPlatform = Load(PalmBld + "SM_Bld_Dock_Platform_01.prefab");
            _dockPillar = Load(PalmBld + "SM_Bld_Dock_Pillar_01.prefab");
            _dockRailing = Load(PalmBld + "SM_Bld_Dock_Railing_01.prefab");
        }

        // ------------------------------------------------------------- the fringe

        /// <summary>Lays what each side gets past the grid. <paramref name="sand"/> lays
        /// one sand strip (x0, x1, z0, z1, alongX) - the environment's own, which knows
        /// to leave the river's channel open.</summary>
        void LayEdges(float gx0, float gx1, float gz0, float gz1, float fringe,
            System.Action<float, float, float, float, bool> sand)
        {
            LoadSeamKit();
            LoadEdgeKit();
            float Depth(EdgeKind k) => k == EdgeKind.Harbour ? 0f : fringe;
            float dS = Depth(southEdge), dN = Depth(northEdge), dE = Depth(eastEdge), dW = Depth(westEdge);

            // the ground: a strip per side, the corners going to the east/west strips'
            // neighbours; a harbour side has no ground at all, its water takes the
            // corners too
            if (southEdge != EdgeKind.Harbour) sand(gx0 - dW, gx1 + dE, gz0 - dS, gz0, true);
            if (northEdge != EdgeKind.Harbour) sand(gx0 - dW, gx1 + dE, gz1, gz1 + dN, true);
            if (westEdge != EdgeKind.Harbour) sand(gx0 - dW, gx0, gz0, gz1, false);
            if (eastEdge != EdgeKind.Harbour) sand(gx1, gx1 + dE, gz0, gz1, false);

            // the harbour's water
            if (southEdge == EdgeKind.Harbour) BuildHarbour(0, gx0, gx1, gz0, gz1, gx0 - dW, gx1 + dE);
            if (northEdge == EdgeKind.Harbour) BuildHarbour(1, gx0, gx1, gz0, gz1, gx0 - dW, gx1 + dE);
            if (westEdge == EdgeKind.Harbour) BuildHarbour(2, gx0, gx1, gz0, gz1, gz0 - dS, gz1 + dN);
            if (eastEdge == EdgeKind.Harbour) BuildHarbour(3, gx0, gx1, gz0, gz1, gz0 - dS, gz1 + dN);
        }

        Transform _edgesRoot;
        Transform EdgesRoot => _edgesRoot != null ? _edgesRoot : (_edgesRoot = new GameObject("Edges").transform);

        // A side as a frame: u runs along the grid's edge, v out from it (positive =
        // away from the city). Side 0 south, 1 north, 2 west, 3 east.
        Vector3 EdgeW(int side, float u, float v, float y, float gx0, float gx1, float gz0, float gz1) => side switch
        {
            0 => new Vector3(u, y, gz0 - v),
            1 => new Vector3(u, y, gz1 + v),
            2 => new Vector3(gx0 - v, y, u),
            _ => new Vector3(gx1 + v, y, u),
        };

        /// <summary>Yaw that turns local +Z to face away from the city on this side.</summary>
        static float EdgeOutYaw(int side) => side switch { 0 => 180f, 1 => 0f, 2 => 270f, _ => 90f };

        // -------------------------------------------------------------- harbour

        // The bay off one side: open water from the last road's kerb, the quay wall
        // along the kerb, piers out into it with boats along them.
        void BuildHarbour(int side, float gx0, float gx1, float gz0, float gz1, float spanLo, float spanHi)
        {
            bool alongX = side < 2;
            float uLo = spanLo - 40f, uHi = spanHi + 40f;

            // the water
            switch (side)
            {
                case 0: WaterTiles(uLo, uHi, gz0 - HarbourDepth, gz0); break;
                case 1: WaterTiles(uLo, uHi, gz1, gz1 + HarbourDepth); break;
                case 2: WaterTiles(gx0 - HarbourDepth, gx0, uLo, uHi); break;
                default: WaterTiles(gx1, gx1 + HarbourDepth, uLo, uHi); break;
            }
            // the quay wall along the kerb line: pivot on the land edge, local -Z to
            // the water (the same rule as the river's high bank, turned to the side)
            if (_quayStraight != null)
            {
                float yaw = side switch { 0 => 0f, 1 => 180f, 2 => 90f, _ => 270f };
                for (float u = uLo; u < uHi - 0.1f; u += Cell)
                {
                    var piece = Random.value < 0.18f && _quayStraightWorn != null ? _quayStraightWorn : _quayStraight;
                    // which end of the 5 m run the pivot rides depends on where +X lands
                    float pu = side == 0 || side == 3 ? u : u + Cell;
                    Instantiate(piece, EdgeW(side, pu, 0f, 0f, gx0, gx1, gz0, gz1), Quaternion.Euler(0f, yaw, 0f), EdgesRoot).name = "Quay";
                }
            }

            // piers: three, out from the kerb, 5 m wide on 2.5 m dock plates over
            // pillar frames, railed both sides and across the end
            float gridLo = alongX ? gx0 : gz0, gridHi = alongX ? gx1 : gz1;
            if (_dockPlatform != null)
                foreach (float f in new[] { 0.2f, 0.5f, 0.8f })
                {
                    float u = Mathf.Round(Mathf.Lerp(gridLo, gridHi, f) / Cell) * Cell;
                    BuildPier(side, u, Random.Range(28f, 42f), gx0, gx1, gz0, gz1);
                }

            // boats: along the piers and out on the water
            if (_boats.Count > 0)
            {
                for (int k = 0; k < 14; k++)
                {
                    float u = Random.Range(gridLo - 20f, gridHi + 20f);
                    float v = Random.Range(12f, HarbourDepth - 60f);
                    var boat = Pick(_boats);
                    Instantiate(boat, EdgeW(side, u, v, WaterY, gx0, gx1, gz0, gz1),
                        Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), EdgesRoot).name = "Boat";
                }
            }

        }

        // A pier: two rows of 2.5 m plates out over the water, a pillar frame under
        // every 5 m, railings down both sides and across the end. Plates cover local
        // -X / +Z from the pivot, the pillar frame is 5 m wide along its X, the
        // railing 2.5 m along its -X.
        void BuildPier(int side, float u, float length, float gx0, float gx1, float gz0, float gz1)
        {
            float outYaw = EdgeOutYaw(side); // local +Z away from the city
            var outRot = Quaternion.Euler(0f, outYaw, 0f);
            // in the pier's own frame: x across (-2.5..2.5), z out (0..length); a point
            // (px, pz) in that frame is EdgeW(side, u + across, v = pz)
            Vector3 P(float across, float outv, float y)
            {
                // "across" runs +X in the pier frame; on the south side (+Z away means
                // yaw 180) +X in the pier frame is world -X, so u decreases
                float su = side switch { 0 => -across, 1 => across, 2 => across, _ => -across };
                return EdgeW(side, u + su, outv, y, gx0, gx1, gz0, gz1);
            }
            for (float z = 0f; z < length - 0.1f; z += 2.5f)
                foreach (float x in new[] { -2.5f, 0f })
                {
                    // plate covers local x in [-2.5, 0], z in [0, 2.5]: pivot at (x + 2.5, z)
                    Instantiate(_dockPlatform, P(x + 2.5f, z, 0f), outRot, EdgesRoot).name = "Pier";
                }
            if (_dockPillar != null)
                for (float z = 2.5f; z < length; z += 5f)
                    Instantiate(_dockPillar, P(0f, z, -4.5f), outRot, EdgesRoot).name = "Pier Frame";
            if (_dockRailing != null)
            {
                // sides: the railing runs 2.5 m along its local -X; turned a quarter it
                // runs along the pier
                for (float z = 0f; z < length - 0.1f; z += 2.5f)
                {
                    Instantiate(_dockRailing, P(-2.45f, z, 0.19f), outRot * Quaternion.Euler(0f, 90f, 0f), EdgesRoot).name = "Rail";
                    Instantiate(_dockRailing, P(2.45f, z + 2.5f, 0.19f), outRot * Quaternion.Euler(0f, -90f, 0f), EdgesRoot).name = "Rail";
                }
                // the end
                Instantiate(_dockRailing, P(2.5f, length - 0.05f, 0.19f), outRot, EdgesRoot).name = "Rail";
                Instantiate(_dockRailing, P(0f, length - 0.05f, 0.19f), outRot, EdgesRoot).name = "Rail";
            }
            // a boat or two alongside
            if (_boats.Count > 0)
                foreach (float across in new[] { -5.5f, 5.5f })
                    if (Random.value < 0.7f)
                        Instantiate(Pick(_boats), P(across, Random.Range(6f, length - 6f), WaterY),
                            outRot * Quaternion.Euler(0f, Random.value < 0.5f ? 0f : 180f, 0f), EdgesRoot).name = "Boat";
        }
    }
}
