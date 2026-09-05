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
    // The approach uses CoreRoads' two-way profile and CorePavement at its edges.
    // The terminal's one-way loop retains its own swept asphalt turns.
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
        static readonly float[] CrossX = AirportLandsidePlan.Crossings;

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

            // The spur ends at the loop edge. Southbound arrivals yield and turn
            // right onto its westbound return lane; the island stays unbroken.
            white.Rect(AirportSpec.ApproachX - 4.6f, AirportSpec.ApproachX - 0.3f,
                AirportLandsidePlan.LoopEdge + 1.5f, AirportLandsidePlan.LoopEdge + 1.8f, y);
            Arrow(white, new Vector3(AirportSpec.ApproachX - 2.5f, 0, back + rh + 8f), 180f, y);
            Arrow(white, new Vector3(AirportSpec.ApproachX + 2.5f, 0, back + rh + 8f), 0f, y);

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
                BlockLocal(edges[i], edges[i + 1], mid - rIn, mid + rIn);
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

        const float SpurVerge = AirportLandsidePlan.SpurVerge;
        readonly AirportLandsidePlan.Lot[] _parkingLots = AirportLandsidePlan.Lots();

        void BuildCarPark()
        {
            foreach (var lot in _parkingLots) ParkBlock(lot);
            BuildParkEntry();
            BuildParkPlanting();
        }

        // The shared parking plan owns stalls, open mouths and aisles. The airport
        // supplies materials and maps the same stall positions to its parked cars.
        void ParkBlock(AirportLandsidePlan.Lot lot)
        {
            var box = lot.Bounds;
            FlatPlane(lot.Name, box.xMin, box.xMax, box.yMin, box.yMax,
                AirportSpec.PaveY, _asphaltMat, 12f, _landsideRoot);
            var paint = new Painter();
            foreach (var stripe in lot.Parking.Markings)
                paint.Dashes(new Vector3(box.xMin + stripe.A.x, 0, box.yMin + stripe.A.y),
                    new Vector3(box.xMin + stripe.B.x, 0, box.yMin + stripe.B.y), 0.1f, 1000f, 0f, AirportSpec.MarkY);
            foreach (var stall in lot.Parking.Stalls)
                _parkBays.Add((new Vector3(box.xMin + stall.Stand.x, AirportSpec.PaveY,
                    box.yMin + stall.Stand.z), Mathf.Atan2(stall.Forward.x, stall.Forward.z) * Mathf.Rad2Deg));
            Arrow(paint, new Vector3(lot.GateX + 1.7f, 0, box.yMin + 4f), 0, AirportSpec.MarkY);
            Arrow(paint, new Vector3(lot.GateX - 1.7f, 0, box.yMin + 4f), 180, AirportSpec.MarkY);
            paint.Emit(lot.Name + " markings", _whitePaint, _markingRoot);
        }

        void BuildParkEntry()
        {
            var booth = AirportKit.TryLoad(AirportKit.GuardBooth);
            var sign = AirportKit.TryLoad(AirportKit.SignParking);
            var paint = new Painter();
            foreach (var lot in _parkingLots)
            {
                var drive = lot.Driveway;
                FlatPlane(lot.Name + " access", drive.xMin, drive.xMax, drive.yMin, drive.yMax,
                    AirportSpec.PaveY, _asphaltMat, 8f, _landsideRoot);
                float z = drive.yMin + 6f;
                // The attendant stands beside the throat, outside both lanes and
                // the footpath. No closed prop barrier across an unmodelled gate.
                if (booth != null)
                {
                    var at = new Vector3(drive.xMax + 2.5f, AirportSpec.PaveY, z);
                    AirportKit.Prop(booth, at, 270, _landsideRoot, "Parking attendant");
                    BlockLocal(at.x - 2f, at.x + 2f, at.z - 2f, at.z + 2f);
                }
                if (sign != null)
                    AirportKit.Sit(sign, new Vector3(drive.xMax + 1.5f, AirportSpec.PaveY, drive.yMin + 1.5f),
                        180, _landsideRoot, "Parking entrance sign");
                Arrow(paint, new Vector3(lot.GateX + 1.7f, 0, z), 0, AirportSpec.MarkY);
                Arrow(paint, new Vector3(lot.GateX - 1.7f, 0, z), 180, AirportSpec.MarkY);
                paint.Rect(drive.xMin + 0.3f, lot.GateX - 0.3f, drive.yMin + 1.2f,
                    drive.yMin + 1.5f, AirportSpec.MarkY);

                // The front walk is cut at the driveway; a painted crossing spans
                // its asphalt. There is no raised slab across the vehicle entrance.
                float walkZ = lot.Bounds.yMin - AirportLandsidePlan.WalkWidth;
                foreach (var r in AirportLandsidePlan.Subtract(
                    Rect.MinMaxRect(lot.Bounds.xMin, walkZ, lot.Bounds.xMax, lot.Bounds.yMin), new[] { drive }))
                    FlatPlane("Parking front walk", r.xMin, r.xMax, r.yMin, r.yMax,
                        AirportSpec.PaveY + 0.12f, _concreteMat, 8f, _landsideRoot);
                for (float x = drive.xMin + 0.4f; x < drive.xMax - 0.3f; x += 1.1f)
                    paint.Rect(x, Mathf.Min(x + 0.6f, drive.xMax), walkZ + 0.3f,
                        lot.Bounds.yMin - 0.3f, AirportSpec.MarkY);
            }
            foreach (float cx in CrossX)
                FlatPlane("Lot walk", cx - 2.5f, cx + 2.5f, AirportLandsidePlan.LoopEdge,
                    AirportSpec.ParkZ0 - AirportLandsidePlan.WalkWidth,
                    AirportSpec.PaveY + 0.12f, _concreteMat, 8f, _landsideRoot);
            paint.Emit("Parking access markings", _whitePaint, _markingRoot);
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
                    if (!ParkVergeClear(x)) continue;
                    AirportKit.Sit(lamp, new Vector3(x, AirportSpec.PaveY, z0 - 4.5f), 0f, _landsideRoot, "Park lamp");
                    AirportKit.Sit(lamp, new Vector3(x, AirportSpec.PaveY, z1 + 1.5f), 180f, _landsideRoot, "Park lamp");
                }
            if (trees.Count > 0)
                for (float x = x0 + 8f; x < x1; x += 22f)
                {
                    if (!ParkVergeClear(x)) continue;
                    AirportKit.Sit(Pick(trees), new Vector3(x, AirportSpec.LandY, z1 + 2f), Rnd(0f, 360f), _floraRoot, "Tree");
                    if (Chance(0.6f))
                        AirportKit.Sit(Pick(trees), new Vector3(x, AirportSpec.LandY, z0 - 8f), Rnd(0f, 360f), _floraRoot, "Tree");
                }
        }

        bool ParkVergeClear(float x)
        {
            if (Mathf.Abs(x - AirportSpec.ApproachX) < SpurVerge + 3f) return false;
            foreach (var lot in _parkingLots) if (Mathf.Abs(x - lot.GateX) < 11f) return false;
            foreach (var crossing in CrossX) if (Mathf.Abs(x - crossing) < 6f) return false;
            return true;
        }

        // ------------------------------------------------------------ the approach

        // CoreDemo's ten-metre two-way road, with its pavement directly at
        // the asphalt edge. Each junction and each fuel parcel owns its surface.
        void BuildApproachRoad()
        {
            float half = AirportLandsidePlan.RoadHalf;
            var arms = new List<float> { AirportSpec.GaGateX, AirportSpec.ApproachX, AirportSpec.CargoGateX };
            arms.Sort();
            float cursor = AirportSpec.StreetX0;
            foreach (float x in arms)
            {
                LayFrontageSpan(cursor, x - half);
                CoreRoads.LayAsphalt(Rect.MinMaxRect(x - half, AirportSpec.StreetZ - half,
                    x + half, AirportSpec.StreetZ + half), RaiseRoad, _landsideRoot, AirportSpec.PaveY);
                if (_links == null || !Mathf.Approximately(x, AirportSpec.ApproachX))
                    CorePavement.LayFootway(AirportSpec.StreetZ + half, x - half, x + half,
                        180, RaiseRoad, _landsideRoot, AirportSpec.PaveY);
                cursor = x + half;
            }
            LayFrontageSpan(cursor, AirportSpec.StreetX1);
            BuildAccessRoad(AirportSpec.ApproachX, AirportLandsidePlan.LoopEdge,
                AirportLandsidePlan.LoopEdge);
            foreach (float x in new[] { AirportSpec.GaGateX, AirportSpec.CargoGateX })
                BuildAccessRoad(x, AirportSpec.ServiceRoadZ - AirportSpec.ServiceRoadWidth * 0.5f,
                    AirportSpec.FenceZ + 5f);
        }

        static GameObject RaiseRoad(GameObject prefab, Transform parent)
            => Object.Instantiate(prefab, parent);

        void BuildAccessRoad(float x, float fromZ, float walkFrom)
        {
            float edge = AirportLandsidePlan.StreetEdge;
            float half = AirportLandsidePlan.RoadHalf;
            CoreRoads.LayTwoWay(x, fromZ, edge, true, RaiseRoad, _landsideRoot, AirportSpec.PaveY);
            // Stop at the frontage footway, so its corner square is laid once.
            float walkTo = edge - CorePavement.Cell;
            CorePavement.LayFootway(x - half, walkFrom, walkTo, 90,
                RaiseRoad, _landsideRoot, AirportSpec.PaveY);
            CorePavement.LayFootway(x + half, walkFrom, walkTo, 270,
                RaiseRoad, _landsideRoot, AirportSpec.PaveY);
        }

        void LayFrontageSpan(float from, float to)
        {
            if (to - from < 0.01f) return;
            float half = AirportLandsidePlan.RoadHalf;
            CoreRoads.LayTwoWay(AirportSpec.StreetZ, from, to, false,
                RaiseRoad, _landsideRoot, AirportSpec.PaveY);
            CorePavement.LayFootway(AirportSpec.StreetZ + half, from, to, 180,
                RaiseRoad, _landsideRoot, AirportSpec.PaveY);
            // The complete PumpDemo parcel already contains its front pavement
            // and both vehicle crossovers. Do not lay another footway over them.
            float fuelLeft = GasX - FuelStationBlock.BlockFrontage * 0.5f;
            float fuelRight = GasX + FuelStationBlock.BlockFrontage * 0.5f;
            CorePavement.LayFootway(AirportSpec.StreetZ - half, from, Mathf.Min(to, fuelLeft), 0,
                RaiseRoad, _landsideRoot, AirportSpec.PaveY);
            CorePavement.LayFootway(AirportSpec.StreetZ - half, Mathf.Max(from, fuelRight), to, 0,
                RaiseRoad, _landsideRoot, AirportSpec.PaveY);
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
