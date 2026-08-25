using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace AirportDemo
{
    // Everything on the public side of the wire: the one-way kerb loop that pulls up
    // under the canopy in front of the terminal, the car park behind it, the approach
    // road out of the map, and the furniture that says 1987 - a rank of cabs, a bus
    // stop, a pair of pay phones, a news stand and a board with an aeroplane on it.
    //
    // The road is laid with the road demo's own StreetKit (5 m tiles, 10 m of
    // carriageway between kerbs) for the approach, and as plain asphalt planes with
    // painted lines for the loop, which is a kerbside road and not a street.
    public partial class AirportDistrict
    {
        /// <summary>The one-way loop, anticlockwise with the kerb on the driver's
        /// right: the leg in front of the terminal, then the return leg behind it.</summary>
        readonly List<Vector3> _loopRoute = new List<Vector3>();
        /// <summary>Where a car may pull in and let somebody out, in order along the
        /// kerb leg.</summary>
        readonly List<Vector3> _kerbStops = new List<Vector3>();
        /// <summary>Every marked bay in the car park, and which way a car in it faces.</summary>
        readonly List<(Vector3 pos, float yaw)> _parkBays = new List<(Vector3, float)>();
        /// <summary>The rank the cabs wait on, front of the rank first.</summary>
        readonly List<Vector3> _cabRank = new List<Vector3>();
        StreetKit _street;

        void BuildLandside()
        {
            BuildLoopRoad();
            BuildForecourt();
            BuildCarPark();
            BuildApproachRoad();
            BuildApproachStrip();   // and what stands on the drive in (Approach.cs)
            BuildKerbFurniture();
        }

        // ------------------------------------------------------------ the loop

        /// <summary>Where the two legs are joined: half way between them, which is the
        /// centre both turns are swept about.</summary>
        float LoopMidZ => (AirportSpec.LoopRoadZ + AirportSpec.LoopBackZ) * 0.5f;
        /// <summary>The centreline radius of a turn - half the gap between the legs, so
        /// the turn meets each of them square.</summary>
        float LoopTurnR => (AirportSpec.LoopBackZ - AirportSpec.LoopRoadZ) * 0.5f;
        /// <summary>Where the two pedestrian routes cross the loop, out of the terminal
        /// spur's way. Everything landside walks over one of these.</summary>
        static readonly float[] CrossX = { -30f, 30f };

        void BuildLoopRoad()
        {
            float half = AirportSpec.LoopHalfX;
            float near = AirportSpec.LoopRoadZ, back = AirportSpec.LoopBackZ;
            float rh = AirportSpec.LoopRoadHalf;
            float mid = LoopMidZ, turn = LoopTurnR;
            float rIn = turn - rh, rOut = turn + rh;

            // the two straights, and the two turns swept round the ends. A kerb loop is
            // a racetrack: a car goes round it without stopping, which is the whole
            // reason a terminal has one.
            FlatPlane("Loop kerb leg", -half, half, near - rh, near + rh, AirportSpec.PaveY, _asphaltMat, 12f, _landsideRoot);
            FlatPlane("Loop return leg", -half, half, back - rh, back + rh, AirportSpec.PaveY, _asphaltMat, 12f, _landsideRoot);
            RoadArc("Loop east turn", new Vector3(half, 0f, mid), rIn, rOut, -90f, 90f, AirportSpec.PaveY, _asphaltMat, _landsideRoot);
            RoadArc("Loop west turn", new Vector3(-half, 0f, mid), rIn, rOut, 90f, 270f, AirportSpec.PaveY, _asphaltMat, _landsideRoot);

            // the island the two legs run round - a stadium, not a rectangle, because
            // the turns are round now. Kerbed in concrete and planted, so it reads as
            // something the airport keeps rather than the hole left in the middle.
            FlatPlane("Loop island", -half, half, mid - rIn, mid + rIn, AirportSpec.PaveY - 0.02f, _grassMat, 12f, _landsideRoot);
            RoadArc("Loop island end", new Vector3(half, 0f, mid), 0f, rIn, -90f, 90f, AirportSpec.PaveY - 0.02f, _grassMat, _landsideRoot);
            RoadArc("Loop island end", new Vector3(-half, 0f, mid), 0f, rIn, 90f, 270f, AirportSpec.PaveY - 0.02f, _grassMat, _landsideRoot);
            // its kerb: a raised band all the way round, which is what actually stops a
            // car cutting the corner and what makes the island read as raised at all
            const float Kerb = 0.34f;
            FlatPlane("Island kerb", -half, half, mid - rIn - Kerb, mid - rIn, AirportSpec.PaveY + 0.13f, _concreteMat, 12f, _landsideRoot);
            FlatPlane("Island kerb", -half, half, mid + rIn, mid + rIn + Kerb, AirportSpec.PaveY + 0.13f, _concreteMat, 12f, _landsideRoot);
            RoadArc("Island kerb", new Vector3(half, 0f, mid), rIn, rIn + Kerb, -90f, 90f, AirportSpec.PaveY + 0.13f, _concreteMat, _landsideRoot);
            RoadArc("Island kerb", new Vector3(-half, 0f, mid), rIn, rIn + Kerb, 90f, 270f, AirportSpec.PaveY + 0.13f, _concreteMat, _landsideRoot);

            // the drop-off kerb: a raised strip along the terminal side of the near leg
            FlatPlane("Drop-off kerb", -half, half, AirportSpec.KerbZ - 6f, near - rh, AirportSpec.PaveY + 0.14f, _concreteMat, 10f, _landsideRoot);

            BuildLoopMarkings();
            BuildLoopIsland();

            // the route the cars drive: kerb leg west to east with the terminal on
            // their right, round the east turn, back along the return leg, round again.
            // The turns are laid out as points along the arc rather than one corner -
            // a driver following a square corner scythes across both lanes of it.
            float laneNear = near - rh * 0.5f, laneBack = back + rh * 0.5f;
            float laneR = turn + rh * 0.5f;
            _loopRoute.Clear();
            _loopRoute.Add(new Vector3(-half, AirportSpec.PaveY, laneNear));
            // the middle of the kerb leg, in front of the terminal doors: this is the
            // point a car stops at to let somebody out, so it is a point of the route
            _loopRoute.Add(new Vector3(0f, AirportSpec.PaveY, laneNear));
            _loopRoute.Add(new Vector3(half, AirportSpec.PaveY, laneNear));
            for (int i = 1; i < 5; i++)
            {
                float a = Mathf.Lerp(-90f, 90f, i / 5f) * Mathf.Deg2Rad;
                _loopRoute.Add(new Vector3(half + Mathf.Cos(a) * laneR, AirportSpec.PaveY, mid + Mathf.Sin(a) * laneR));
            }
            _loopRoute.Add(new Vector3(half, AirportSpec.PaveY, laneBack));
            _loopRoute.Add(new Vector3(-half, AirportSpec.PaveY, laneBack));
            for (int i = 1; i < 5; i++)
            {
                float a = Mathf.Lerp(90f, 270f, i / 5f) * Mathf.Deg2Rad;
                _loopRoute.Add(new Vector3(-half + Mathf.Cos(a) * laneR, AirportSpec.PaveY, mid + Mathf.Sin(a) * laneR));
            }

            for (float x = -34f; x <= 34.1f; x += 8.5f)
                _kerbStops.Add(new Vector3(x, AirportSpec.PaveY, laneNear));
        }

        /// <summary>What is painted on the loop: the lane line down each leg, the arrows
        /// that say which way round it goes, the zebras the passengers cross on, and the
        /// hatched nose where the terminal spur joins it. The arrows are the important
        /// half - a one-way loop with no arrow on it is just a road.</summary>
        void BuildLoopMarkings()
        {
            float half = AirportSpec.LoopHalfX;
            float near = AirportSpec.LoopRoadZ, back = AirportSpec.LoopBackZ;
            float rh = AirportSpec.LoopRoadHalf;
            float y = AirportSpec.MarkY;
            var white = new Painter();

            white.Dashes(new Vector3(-half, 0f, near), new Vector3(half, 0f, near), 0.14f, 3f, 4f, y);
            white.Dashes(new Vector3(half, 0f, back), new Vector3(-half, 0f, back), 0.14f, 3f, 4f, y);

            // arrows: east down the kerb leg, west back along the return leg
            for (float x = -half + 22f; x < half - 12f; x += 34f)
            {
                Arrow(white, new Vector3(x, 0f, near - rh * 0.5f), 90f, y);
                Arrow(white, new Vector3(-x, 0f, back + rh * 0.5f), 270f, y);
            }

            // the zebras, on both legs, at the two places the walk from the car park
            // comes down. Kept clear of the spur at x = 0, which is a road.
            foreach (float cx in CrossX)
            {
                Zebra(white, cx, near - rh, near + rh, y);
                Zebra(white, cx, back - rh, back + rh, y);
            }

            // the hatched nose where the spur meets the return leg: what a driver coming
            // off it has to give way at, and what keeps him out of the westbound lane
            for (int i = 0; i < 7; i++)
            {
                float t = i / 6f;
                float w = Mathf.Lerp(0.4f, 5.2f, t);
                white.Turned(new Vector3(0f, 0f, back + rh + 2.5f + i * 1.9f), 35f, 0.28f, w, y);
                white.Turned(new Vector3(0f, 0f, back + rh + 2.5f + i * 1.9f), -35f, 0.28f, w, y);
            }

            white.Emit("Loop markings", _whitePaint, _markingRoot);
        }

        /// <summary>One lane arrow, painted along <paramref name="yaw"/>: a shaft and a
        /// head, which is all an arrow at road scale ever is.</summary>
        static void Arrow(Painter p, Vector3 at, float yaw, float y)
        {
            p.Turned(at, yaw, 0.4f, 3.4f, y);
            var q = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < 5; i++)
            {
                float t = i / 4f;
                var local = new Vector3(0f, 0f, 1.7f + t * 1.3f);
                p.Turned(at + q * local, yaw, Mathf.Lerp(1.5f, 0.25f, t), 0.34f, y);
            }
        }

        /// <summary>A zebra across a carriageway: bars along the road, so a walker
        /// crosses over them.</summary>
        static void Zebra(Painter p, float centreX, float z0, float z1, float y)
        {
            for (float x = centreX - 2.2f; x <= centreX + 2.21f; x += 1.1f)
                p.Rect(x, x + 0.6f, z0 + 0.3f, z1 - 0.3f, y);
        }

        /// <summary>What stands on the island: the walk the passengers cross to, the
        /// trees, the lamps and the board. An island with nothing on it is a hole.</summary>
        void BuildLoopIsland()
        {
            float mid = LoopMidZ, rIn = LoopTurnR - AirportSpec.LoopRoadHalf;
            var trees = AirportKit.LoadAll(AirportKit.Trees, quiet: true);
            var bushes = AirportKit.LoadAll(AirportKit.Bushes, quiet: true);
            var lamp = AirportKit.TryLoad(AirportKit.StreetLamp);
            var bin = AirportKit.TryLoad(AirportKit.TrashBin);
            var bench = AirportKit.TryLoad(AirportKit.BenchSeat);

            // the paved walk across the island at each crossing, so the two zebras join
            // up instead of stopping at grass
            foreach (float cx in CrossX)
                FlatPlane("Island walk", cx - 2.5f, cx + 2.5f, mid - rIn, mid + rIn,
                          AirportSpec.PaveY + 0.12f, _concreteMat, 8f, _landsideRoot);

            for (float x = -AirportSpec.LoopHalfX + 14f; x < AirportSpec.LoopHalfX - 10f; x += 17f)
            {
                bool onWalk = false;
                foreach (float cx in CrossX) if (Mathf.Abs(x - cx) < 6f) onWalk = true;
                if (onWalk) continue;
                if (trees.Count > 0)
                    AirportKit.Sit(Pick(trees), new Vector3(x, AirportSpec.LandY, mid + Rnd(-4f, 4f)), Rnd(0f, 360f), _floraRoot, "Tree");
                if (bushes.Count > 0 && Chance(0.7f))
                    AirportKit.Sit(Pick(bushes), new Vector3(x + Rnd(-5f, 5f), AirportSpec.LandY, mid + Rnd(-5f, 5f)), Rnd(0f, 360f), _floraRoot, "Bush");
                if (lamp != null && Chance(0.45f))
                    AirportKit.Sit(lamp, new Vector3(x, AirportSpec.PaveY + 0.12f, mid - rIn + 1.2f), 0f, _landsideRoot, "Island lamp");
            }
            // a place to stand and wait, at each crossing's island end
            foreach (float cx in CrossX)
            {
                if (bench != null) AirportKit.Sit(bench, new Vector3(cx + 4.2f, AirportSpec.PaveY + 0.12f, mid), 90f, _landsideRoot, "Bench");
                if (bin != null) AirportKit.Sit(bin, new Vector3(cx - 3.6f, AirportSpec.PaveY + 0.12f, mid + 2f), 0f, _landsideRoot, "Bin");
            }
            // the planted stretches are walked round, but NOT the two walks - they are
            // the only way over the loop on foot and blocking them would send everybody
            // out into the carriageway
            float h = AirportSpec.LoopHalfX;
            var edges = new List<float> { -h, CrossX[0] - 3f, CrossX[0] + 3f, CrossX[1] - 3f, CrossX[1] + 3f, h };
            for (int i = 0; i + 1 < edges.Count; i += 2)
                WalkObstacles.Block(edges[i], edges[i + 1], mid - rIn, mid + rIn);
        }

        /// <summary>The forecourt between the terminal's doors and the kerb, with the
        /// canopy over it - the Plaza pack's awning, which is the one piece in any pack
        /// shaped like a kerbside canopy.</summary>
        void BuildForecourt()
        {
            float termBack = AirportSpec.BuildingFrontZ + AirportSpec.TerminalDepth;
            FlatPlane("Forecourt", -AirportSpec.TerminalWidth * 0.5f - 8f, AirportSpec.TerminalWidth * 0.5f + 8f,
                      termBack, AirportSpec.KerbZ - 6f, AirportSpec.PaveY + 0.14f, _concreteMat, 10f, _landsideRoot);

            var cover = AirportKit.TryLoad(AirportKit.AwningCover);
            var pole = AirportKit.TryLoad(AirportKit.AwningPole);
            if (cover != null)
            {
                var cb = AirportKit.PrefabBounds(cover);
                float step = Mathf.Max(4f, cb.size.x - 0.6f);
                float zc = (termBack + AirportSpec.KerbZ - 6f) * 0.5f + 1.5f;
                for (float x = -26f; x <= 26.1f; x += step)
                {
                    AirportKit.Prop(cover, new Vector3(x, AirportSpec.PaveY + 0.14f, zc), 0f, _landsideRoot, "Canopy");
                    if (pole != null)
                    {
                        AirportKit.Sit(pole, new Vector3(x - step * 0.5f, AirportSpec.PaveY + 0.14f, zc + 3.2f), 0f, _landsideRoot, "Canopy pole");
                        AirportKit.Sit(pole, new Vector3(x - step * 0.5f, AirportSpec.PaveY + 0.14f, zc - 3.2f), 0f, _landsideRoot, "Canopy pole");
                    }
                }
            }

            // the board on the approach that says what this place is
            var billboard = AirportKit.TryLoad(AirportKit.PlazaBillboard);
            if (billboard != null)
                AirportKit.Sit(billboard, new Vector3(-AirportSpec.LoopHalfX + 16f, AirportSpec.PaveY, AirportSpec.LoopBackZ + 12f), 200f, _landsideRoot, "Airport board");
            var planeSign = AirportKit.TryLoad(AirportKit.SignPlane);
            if (planeSign != null)
            {
                AirportKit.Sit(planeSign, new Vector3(-AirportSpec.LoopHalfX - 4f, AirportSpec.PaveY, AirportSpec.LoopBackZ + 6f), 180f, _landsideRoot, "Airport sign");
                AirportKit.Sit(planeSign, new Vector3(AirportSpec.ApproachX - 10f, AirportSpec.PaveY, AirportSpec.StreetZ - 14f), 180f, _landsideRoot, "Airport sign");
            }
        }

        // ------------------------------------------------------------ the car park

        /// <summary>How wide a strip either side of the terminal spur belongs to the
        /// road rather than to the lot. The spur runs up the MIDDLE of the car park to
        /// the street (BuildApproachRoad), which the old lot ignored: it laid bays and
        /// paint straight across the carriageway. Two blocks either side of it is both
        /// the fix and the right layout - that spur is how a car gets in.</summary>
        const float SpurVerge = 15f;

        void BuildCarPark()
        {
            float z0 = AirportSpec.ParkZ0, z1 = AirportSpec.ParkZ1;
            // west block and east block, with the terminal spur running up between them
            ParkBlock("West lot", AirportSpec.ParkX0, AirportSpec.ApproachX - SpurVerge, z0, z1, entryEast: true);
            ParkBlock("East lot", AirportSpec.ApproachX + SpurVerge, AirportSpec.ParkX1, z0, z1, entryEast: false);
            BuildParkEntry();
            BuildParkPlanting();
        }

        /// <summary>One block of the lot: a drive lane along the side the spur is on,
        /// then double rows of bays back to back with their aisles, and an end island
        /// so a row does not run out into the lane. The bays nearest the terminal are
        /// the short stay, which is why they are marked and the far ones are not
        /// (nothing enforces it - it is what the paint SAYS that matters).</summary>
        void ParkBlock(string name, float x0, float x1, float z0, float z1, bool entryEast)
        {
            if (x1 - x0 < 20f) return;
            FlatPlane(name, x0, x1, z0, z1, AirportSpec.PaveY, _asphaltMat, 12f, _landsideRoot);

            var white = new Painter();
            var yellow = new Painter();
            float y = AirportSpec.MarkY;
            float bay = AirportSpec.BayDepth, aisle = AirportSpec.ParkAisle, wide = AirportSpec.BayWidth;

            // the drive lane down the spur side, and the island that ends every row
            const float Lane = 7f, Island = 3.2f;
            float laneX0 = entryEast ? x1 - Lane : x0;
            float laneX1 = entryEast ? x1 : x0 + Lane;
            float bayX0 = entryEast ? x0 + Island : laneX1 + Island;
            float bayX1 = entryEast ? laneX0 - Island : x1 - Island;
            // the island is grass, not paint: a painted island is a place a car parks on
            FlatPlane("Lot island", entryEast ? bayX1 : laneX1, entryEast ? laneX0 : bayX0, z0 + 1f, z1 - 1f,
                      AirportSpec.PaveY + 0.12f, _grassMat, 8f, _landsideRoot);
            FlatPlane("Lot island", entryEast ? x0 : bayX1, entryEast ? bayX0 : x1, z0 + 1f, z1 - 1f,
                      AirportSpec.PaveY + 0.12f, _grassMat, 8f, _landsideRoot);
            // the lane's own edge line and the arrow that says which way round it goes
            white.Rect(laneX0 + 0.2f, laneX0 + 0.32f, z0 + 1f, z1 - 1f, y);
            white.Rect(laneX1 - 0.32f, laneX1 - 0.2f, z0 + 1f, z1 - 1f, y);
            Arrow(white, new Vector3((laneX0 + laneX1) * 0.5f, 0f, z0 + 9f), 0f, y);
            Arrow(white, new Vector3((laneX0 + laneX1) * 0.5f, 0f, z1 - 9f), 0f, y);

            int row = 0;
            float z = z0 + 2f;
            while (z + bay * 2f + aisle < z1)
            {
                for (int side = 0; side < 2; side++)
                {
                    float rz0 = z + side * bay, rz1 = rz0 + bay;
                    int n = 0;
                    for (float x = bayX0; x + wide <= bayX1; x += wide, n++)
                    {
                        // the two bays by the walk are the wide ones, in yellow
                        bool wideBay = row == 0 && n < 2;
                        var p = wideBay ? yellow : white;
                        p.Rect(x, x + 0.12f, rz0, rz1, y);
                        _parkBays.Add((new Vector3(x + wide * 0.5f, AirportSpec.PaveY, (rz0 + rz1) * 0.5f), side == 0 ? 180f : 0f));
                    }
                    white.Rect(bayX0, bayX1, side == 0 ? rz0 : rz1 - 0.12f, side == 0 ? rz0 + 0.12f : rz1, y);
                }
                z += bay * 2f + aisle;
                row++;
            }
            white.Emit(name + " markings", _whitePaint, _markingRoot);
            yellow.Emit(name + " markings wide", _yellowPaint, _markingRoot);
        }

        /// <summary>How a car actually gets in: the two mouths off the terminal spur,
        /// the ticket machine at each and the board over it. Without them the lot was a
        /// rectangle of paint a car had no way into and no reason to be in.</summary>
        void BuildParkEntry()
        {
            float z0 = AirportSpec.ParkZ0, z1 = AirportSpec.ParkZ1;
            float mouthZ = (z0 + z1) * 0.5f;
            var booth = AirportKit.TryLoad(AirportKit.GuardBooth);
            var boom = AirportKit.TryLoad(AirportKit.BoomGate);
            var sign = AirportKit.TryLoad(AirportKit.SignParking);
            var cone = AirportKit.TryLoad(AirportKit.Cone);

            for (int s = -1; s <= 1; s += 2)
            {
                float x = AirportSpec.ApproachX + s * SpurVerge;
                // the throat off the spur into the block
                FlatPlane("Lot mouth", Mathf.Min(x, AirportSpec.ApproachX + s * StreetKit.RoadHalf),
                          Mathf.Max(x, AirportSpec.ApproachX + s * StreetKit.RoadHalf),
                          mouthZ - 5f, mouthZ + 5f, AirportSpec.PaveY, _asphaltMat, 8f, _landsideRoot);
                if (boom != null)
                    AirportKit.Sit(boom, new Vector3(x, AirportSpec.PaveY, mouthZ + 5f), s > 0 ? 90f : 270f, _landsideRoot, "Lot boom");
                if (booth != null)
                    AirportKit.Prop(booth, new Vector3(x + s * 2.5f, AirportSpec.PaveY, mouthZ + 9f), s > 0 ? 270f : 90f, _landsideRoot, "Ticket booth");
                if (sign != null)
                    AirportKit.Sit(sign, new Vector3(x + s * 2f, AirportSpec.PaveY, mouthZ - 8f), s > 0 ? 270f : 90f, _landsideRoot, "Parking sign");
                if (cone != null)
                    for (int i = 0; i < 3; i++)
                        AirportKit.Sit(cone, new Vector3(x + s * 1.2f, AirportSpec.PaveY, mouthZ - 6f + i * 2.2f), 0f, _landsideRoot, "Cone");
            }

            // the walk from the lot down to the loop's crossings, so somebody off a bay
            // has a way to the terminal that is not the carriageway
            foreach (float cx in CrossX)
            {
                FlatPlane("Lot walk", cx - 2.5f, cx + 2.5f, AirportSpec.LoopBackZ + AirportSpec.LoopRoadHalf, z0,
                          AirportSpec.PaveY + 0.12f, _concreteMat, 8f, _landsideRoot);
                FlatPlane("Lot walk", cx - 2.5f, cx + 2.5f, z0, z1,
                          AirportSpec.PaveY + 0.12f, _concreteMat, 8f, _landsideRoot);
            }
        }

        /// <summary>The lamps and the planting. A lot the size of this one with nothing
        /// standing in it is a black rectangle whatever is painted on it.</summary>
        void BuildParkPlanting()
        {
            float x0 = AirportSpec.ParkX0, x1 = AirportSpec.ParkX1;
            float z0 = AirportSpec.ParkZ0, z1 = AirportSpec.ParkZ1;
            var lamp = AirportKit.TryLoad(AirportKit.StreetLamp);
            var trees = AirportKit.LoadAll(AirportKit.Trees, quiet: true);
            var bushes = AirportKit.LoadAll(AirportKit.Bushes, quiet: true);

            if (lamp != null)
                for (float x = x0 + 14f; x < x1; x += 28f)
                {
                    if (Mathf.Abs(x - AirportSpec.ApproachX) < SpurVerge) continue;
                    AirportKit.Sit(lamp, new Vector3(x, AirportSpec.PaveY, z0 - 1.5f), 0f, _landsideRoot, "Park lamp");
                    AirportKit.Sit(lamp, new Vector3(x, AirportSpec.PaveY, z1 + 1.5f), 180f, _landsideRoot, "Park lamp");
                }
            if (trees.Count > 0)
                for (float x = x0 + 8f; x < x1; x += 22f)
                {
                    if (Mathf.Abs(x - AirportSpec.ApproachX) < SpurVerge - 3f) continue;
                    AirportKit.Sit(Pick(trees), new Vector3(x + Rnd(-3f, 3f), AirportSpec.LandY, z1 + 6f), Rnd(0f, 360f), _floraRoot, "Tree");
                    if (Chance(0.6f))
                        AirportKit.Sit(Pick(trees), new Vector3(x + Rnd(-3f, 3f), AirportSpec.LandY, z0 - 6f), Rnd(0f, 360f), _floraRoot, "Tree");
                }
            // and the end islands, which are grass and want something growing on them
            if (bushes.Count > 0)
                for (int s = -1; s <= 1; s += 2)
                {
                    float ix = s < 0 ? x0 + 1.6f : x1 - 1.6f;
                    for (float z = z0 + 5f; z < z1 - 4f; z += 7f)
                        AirportKit.Sit(Pick(bushes), new Vector3(ix, AirportSpec.PaveY + 0.12f, z), Rnd(0f, 360f), _floraRoot, "Bush");
                }
        }

        // ------------------------------------------------------------ the approach

        /// <summary>The road in: the road demo's own street, laid out of the map at
        /// both ends, with a junction where the terminal spur leaves it and one at
        /// each of the two service gates - and the three roads that run south from
        /// them, to the loop and through the wire onto the ramp.</summary>
        void BuildApproachRoad()
        {
            // the kit's road top is at its own y (the city drives its cars at the tile
            // height), so the road is laid at the field's paving level, a step up off
            // the grass like every other surface here
            _street = new StreetKit(_landsideRoot, AirportSpec.PaveY) { Palms = false };
            if (!_street.Load()) return;
            float z = AirportSpec.StreetZ;

            // the three arms that leave the street, west to east
            var arms = new List<float> { AirportSpec.GaGateX, AirportSpec.ApproachX, AirportSpec.CargoGateX };
            arms.Sort();

            // the street in segments between them, so a junction square is never laid
            // on top of a carriageway tile. In the city the approach junction is where
            // the city's own street arrives (the portal, Portals.cs): its north side is
            // left open for it instead of capped with pavement.
            float cursor = AirportSpec.StreetX0;
            foreach (float ax in arms)
            {
                bool approach = Mathf.Approximately(ax, AirportSpec.ApproachX);
                _street.LayAlongX(z, cursor, ax - 10f, southWalk: true, northWalk: true, dress: true);
                _street.LayJunction(ax, z, capNorth: !(approach && _links != null), splaySouth: 1);
                cursor = ax + 10f;
            }
            _street.LayAlongX(z, cursor, AirportSpec.StreetX1, southWalk: true, northWalk: true, dress: true);

            // the terminal spur, and the two gate roads that run down to the ramp
            _street.LayRoadAlongZ(AirportSpec.ApproachX, AirportSpec.LoopBackZ + 4f, z - 5f);
            _street.LayRoadAlongZ(AirportSpec.GaGateX, AirportSpec.ServiceRoadZ, z - 5f);
            _street.LayRoadAlongZ(AirportSpec.CargoGateX, AirportSpec.ServiceRoadZ, z - 5f);

            // they are one lane each way, so they get a centre line rather than kerbs
            var white = new Painter();
            white.Dashes(new Vector3(AirportSpec.ApproachX, 0f, AirportSpec.LoopBackZ + 6f),
                         new Vector3(AirportSpec.ApproachX, 0f, z - 6f), 0.14f, 3f, 4f, AirportSpec.MarkY);
            foreach (float ax in new[] { AirportSpec.GaGateX, AirportSpec.CargoGateX })
                white.Dashes(new Vector3(ax, 0f, AirportSpec.ServiceRoadZ + 4f),
                             new Vector3(ax, 0f, z - 6f), 0.14f, 3f, 4f, AirportSpec.MarkY);
            white.Emit("Spur markings", _whitePaint, _markingRoot);

            var trees = AirportKit.LoadAll(AirportKit.Trees, quiet: true);
            if (trees.Count > 0)
                for (float x = AirportSpec.StreetX0 + 20f; x < AirportSpec.StreetX1 - 20f; x += 30f)
                {
                    if (Mathf.Abs(x - AirportSpec.ApproachX) < 24f) continue;
                    AirportKit.Sit(Pick(trees), new Vector3(x, AirportSpec.LandY, z + 16f), Rnd(0f, 360f), _floraRoot, "Tree");
                }
        }

        // ------------------------------------------------------------ the kerb

        void BuildKerbFurniture()
        {
            float kerbZ = AirportSpec.KerbZ - 6.6f;   // the pavement side of the kerb
            var bench = AirportKit.TryLoad(AirportKit.BenchSeat);
            var bin = AirportKit.TryLoad(AirportKit.TrashBin);
            var phone = AirportKit.TryLoad(AirportKit.PayPhone);
            var news = AirportKit.TryLoad(AirportKit.NewsStand);
            var planter = AirportKit.TryLoad(AirportKit.Planter);
            var rank = AirportKit.TryLoad(AirportKit.TaxiStand);
            var stop = AirportKit.TryLoad(AirportKit.BusStop);
            var lamp = AirportKit.TryLoad(AirportKit.PierLamp) ?? AirportKit.TryLoad(AirportKit.StreetLamp);

            for (float x = -44f; x <= 44.1f; x += 11f)
            {
                if (lamp != null) AirportKit.Sit(lamp, new Vector3(x, AirportSpec.PaveY + 0.14f, kerbZ - 1.2f), 0f, _landsideRoot, "Kerb lamp");
                if (bin != null && Chance(0.5f)) AirportKit.Sit(bin, new Vector3(x + 3f, AirportSpec.PaveY + 0.14f, kerbZ - 1.4f), Rnd(0f, 360f), _landsideRoot, "Bin");
                if (planter != null && Chance(0.45f)) AirportKit.Sit(planter, new Vector3(x - 3.5f, AirportSpec.PaveY + 0.14f, kerbZ - 2.6f), 0f, _landsideRoot, "Planter");
            }
            for (int i = -1; i <= 1; i += 2)
                if (bench != null)
                {
                    AirportKit.Sit(bench, new Vector3(i * 18f, AirportSpec.PaveY + 0.14f, kerbZ - 3.4f), 0f, _landsideRoot, "Bench");
                    AirportKit.Sit(bench, new Vector3(i * 26f, AirportSpec.PaveY + 0.14f, kerbZ - 3.4f), 0f, _landsideRoot, "Bench");
                }
            if (phone != null)
            {
                AirportKit.Sit(phone, new Vector3(-33f, AirportSpec.PaveY + 0.14f, kerbZ - 3.2f), 180f, _landsideRoot, "Pay phone");
                AirportKit.Sit(phone, new Vector3(-31.4f, AirportSpec.PaveY + 0.14f, kerbZ - 3.2f), 180f, _landsideRoot, "Pay phone");
            }
            if (news != null) AirportKit.Sit(news, new Vector3(30f, AirportSpec.PaveY + 0.14f, kerbZ - 3.2f), 180f, _landsideRoot, "News stand");

            // the cab rank at the west end of the kerb, the bus stop at the east
            if (rank != null) AirportKit.Sit(rank, new Vector3(-52f, AirportSpec.PaveY + 0.14f, kerbZ - 1.6f), 180f, _landsideRoot, "Taxi rank");
            for (int i = 0; i < 4; i++)
                _cabRank.Add(new Vector3(-50f + i * 6.5f, AirportSpec.PaveY, AirportSpec.LoopRoadZ - AirportSpec.LoopRoadHalf * 0.5f));
            if (stop != null) AirportKit.Sit(stop, new Vector3(52f, AirportSpec.PaveY + 0.14f, kerbZ - 2f), 180f, _landsideRoot, "Bus stop");

            var signTaxi = AirportKit.TryLoad(AirportKit.SignTaxi);
            if (signTaxi != null) AirportKit.Sit(signTaxi, new Vector3(-58f, AirportSpec.PaveY + 0.14f, kerbZ - 1.6f), 180f, _landsideRoot, "Taxi sign");
            var signPark = AirportKit.TryLoad(AirportKit.SignParking);
            if (signPark != null)
                AirportKit.Sit(signPark, new Vector3(AirportSpec.ParkX0 - 4f, AirportSpec.PaveY, AirportSpec.ParkZ0 - 4f), 200f, _landsideRoot, "Parking sign");
        }
    }
}
