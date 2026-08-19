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
    public partial class AirportDemoBuilder
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
            BuildKerbFurniture();
        }

        // ------------------------------------------------------------ the loop

        void BuildLoopRoad()
        {
            float half = AirportSpec.LoopHalfX;
            float near = AirportSpec.LoopRoadZ, back = AirportSpec.LoopBackZ;
            float rh = AirportSpec.LoopRoadHalf;

            FlatPlane("Loop kerb leg", -half - rh, half + rh, near - rh, near + rh, AirportSpec.PaveY, _asphaltMat, 12f, _landsideRoot);
            FlatPlane("Loop return leg", -half - rh, half + rh, back - rh, back + rh, AirportSpec.PaveY, _asphaltMat, 12f, _landsideRoot);
            FlatPlane("Loop east turn", half - rh, half + rh, near - rh, back + rh, AirportSpec.PaveY, _asphaltMat, 12f, _landsideRoot);
            FlatPlane("Loop west turn", -half - rh, -half + rh, near - rh, back + rh, AirportSpec.PaveY, _asphaltMat, 12f, _landsideRoot);
            // the island the two legs run round, in grass, because a loop with nothing
            // in the middle of it reads as a car park
            FlatPlane("Loop island", -half + rh, half - rh, near + rh, back - rh, AirportSpec.PaveY - 0.02f, _grassMat, 12f, _landsideRoot);

            // the drop-off kerb: a raised strip along the terminal side of the near leg
            FlatPlane("Drop-off kerb", -half, half, AirportSpec.KerbZ - 6f, near - rh, AirportSpec.PaveY + 0.14f, _concreteMat, 10f, _landsideRoot);

            var white = new Painter();
            float y = AirportSpec.MarkY;
            // a lane line down the middle of each leg's two lanes and the arrows that
            // say which way round it goes
            white.Dashes(new Vector3(-half, 0f, near), new Vector3(half, 0f, near), 0.14f, 3f, 4f, y);
            white.Dashes(new Vector3(half, 0f, back), new Vector3(-half, 0f, back), 0.14f, 3f, 4f, y);
            white.Emit("Loop markings", _whitePaint, _markingRoot);

            // the route the cars drive: kerb leg west to east with the terminal on
            // their right, round the east turn, back along the return leg, round again
            float laneNear = near - rh * 0.5f, laneBack = back + rh * 0.5f;
            _loopRoute.Clear();
            _loopRoute.Add(new Vector3(-half, AirportSpec.PaveY, laneNear));
            // the middle of the kerb leg, in front of the terminal doors: this is the
            // point a car stops at to let somebody out, so it is a point of the route
            _loopRoute.Add(new Vector3(0f, AirportSpec.PaveY, laneNear));
            _loopRoute.Add(new Vector3(half, AirportSpec.PaveY, laneNear));
            _loopRoute.Add(new Vector3(half + rh * 0.6f, AirportSpec.PaveY, (near + back) * 0.5f));
            _loopRoute.Add(new Vector3(half, AirportSpec.PaveY, laneBack));
            _loopRoute.Add(new Vector3(-half, AirportSpec.PaveY, laneBack));
            _loopRoute.Add(new Vector3(-half - rh * 0.6f, AirportSpec.PaveY, (near + back) * 0.5f));

            for (float x = -34f; x <= 34.1f; x += 8.5f)
                _kerbStops.Add(new Vector3(x, AirportSpec.PaveY, laneNear));
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

        void BuildCarPark()
        {
            float x0 = AirportSpec.ParkX0, x1 = AirportSpec.ParkX1;
            float z0 = AirportSpec.ParkZ0, z1 = AirportSpec.ParkZ1;
            FlatPlane("Car park", x0, x1, z0, z1, AirportSpec.PaveY, _asphaltMat, 12f, _landsideRoot);

            var white = new Painter();
            float y = AirportSpec.MarkY;
            float bay = AirportSpec.BayDepth, aisle = AirportSpec.ParkAisle, wide = AirportSpec.BayWidth;
            float z = z0 + 1f;
            while (z + bay * 2f + aisle < z1)
            {
                // a double row back to back, then the aisle that serves it
                for (int side = 0; side < 2; side++)
                {
                    float rz0 = z + side * bay, rz1 = rz0 + bay;
                    for (float x = x0 + 2f; x + wide <= x1 - 2f; x += wide)
                    {
                        white.Rect(x, x + 0.12f, rz0, rz1, y);
                        _parkBays.Add((new Vector3(x + wide * 0.5f, AirportSpec.PaveY, (rz0 + rz1) * 0.5f), side == 0 ? 180f : 0f));
                    }
                    white.Rect(x0 + 2f, x1 - 2f, side == 0 ? rz0 : rz1 - 0.12f, side == 0 ? rz0 + 0.12f : rz1, y);
                }
                z += bay * 2f + aisle;
            }
            white.Emit("Car park markings", _whitePaint, _markingRoot);

            // the lamps, the kerb round the edge and a little planting, so the lot is
            // not a black rectangle
            var lamp = AirportKit.TryLoad(AirportKit.StreetLamp);
            if (lamp != null)
                for (float x = x0 + 20f; x < x1; x += 45f)
                {
                    AirportKit.Sit(lamp, new Vector3(x, AirportSpec.PaveY, z0 - 1.5f), 0f, _landsideRoot, "Park lamp");
                    AirportKit.Sit(lamp, new Vector3(x, AirportSpec.PaveY, z1 + 1.5f), 180f, _landsideRoot, "Park lamp");
                }
            var trees = AirportKit.LoadAll(AirportKit.Trees, quiet: true);
            if (trees.Count > 0)
                for (float x = x0 + 8f; x < x1; x += 26f)
                {
                    AirportKit.Sit(Pick(trees), new Vector3(x + Rnd(-3f, 3f), AirportSpec.LandY, z1 + 6f), Rnd(0f, 360f), _floraRoot, "Tree");
                    if (Chance(0.6f))
                        AirportKit.Sit(Pick(trees), new Vector3(x + Rnd(-3f, 3f), AirportSpec.LandY, z0 - 6f), Rnd(0f, 360f), _floraRoot, "Tree");
                }
        }

        // ------------------------------------------------------------ the approach

        /// <summary>The road in: the road demo's own street, laid out of the map at
        /// both ends, with a junction where the terminal spur leaves it and one at
        /// each of the two service gates - and the three roads that run south from
        /// them, to the loop and through the wire onto the ramp.</summary>
        void BuildApproachRoad()
        {
            _street = new StreetKit(_landsideRoot, AirportSpec.PaveY - 0.1f) { Palms = false };
            if (!_street.Load()) return;
            float z = AirportSpec.StreetZ;

            // the three arms that leave the street, west to east
            var arms = new List<float> { AirportSpec.GaGateX, AirportSpec.ApproachX, AirportSpec.CargoGateX };
            arms.Sort();

            // the street in segments between them, so a junction square is never laid
            // on top of a carriageway tile
            float cursor = -420f;
            foreach (float ax in arms)
            {
                _street.LayAlongX(z, cursor, ax - 10f, southWalk: true, northWalk: true, dress: true);
                _street.LayJunction(ax, z, capNorth: true, splaySouth: 1);
                cursor = ax + 10f;
            }
            _street.LayAlongX(z, cursor, 420f, southWalk: true, northWalk: true, dress: true);

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
                for (float x = -400f; x < 400f; x += 30f)
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
