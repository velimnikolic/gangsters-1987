using UnityEngine;

namespace AirportDemo
{
    // What the field has done to itself. Everything here is flat - quads a finger over
    // the pavement they lie on - and none of it is a new prefab or a new renderer per
    // piece: one mesh a material, laid after the markings so the oil covers the paint
    // rather than the other way round.
    //
    // The reason this file exists at all: the airport read as sterile long before it
    // read as too big. Uniform concrete, paint the same white from end to end, grass
    // that stops dead at the tarmac and starts again on the other side. A working
    // airfield is none of those things. It is scrubbed where the wheels land, black
    // where the aeroplanes stand, patched where the frost got in, and green in every
    // seam that has not been sprayed this year.
    //
    // The wear is laid off the field's own geometry rather than sprinkled at random:
    // the touchdown zones are worn because that is where the mains touch, the stands
    // are stained because that is where the engines drip, the perimeter track is bare
    // because a truck drives it twice a day. Random noise reads as noise; wear that
    // agrees with what the place is for reads as history.
    public partial class AirportDistrict
    {
        /// <summary>Fresh, faded, scrubbed - the three states airfield paint is in.</summary>
        public const int WearTiers = 3;
        /// <summary>How long a length of edge stripe or taxiway centreline is painted
        /// before the next one may be a different shade.</summary>
        const float EdgeStripeRun = 60f;

        // ------------------------------------------------------------ how worn

        /// <summary>How worn a runway marking is where it lies. Distance in from the
        /// nearer threshold decides it: the last few metres of the paved end are only
        /// weathered, the touchdown zone is scrubbed, and the long middle - which a
        /// landing is rolling over at taxi speed and a departure has already left - is
        /// the freshest paint on the field.</summary>
        int RunwayWear(float x)
        {
            float into = RunwayHalf - Mathf.Abs(x);
            if (into < 45f) return 1;
            if (into < AirportSpec.AimingPointFrom + 60f) return 2;
            return Chance(0.3f) ? 1 : 0;
        }

        /// <summary>How worn a taxiway marking is: the ends, where everything turns
        /// round, and the stretch abeam the apron entries, where everything crosses.</summary>
        int TaxiwayWear(float x)
        {
            if (Mathf.Abs(x) > RunwayHalf - 60f) return 2;
            foreach (float ex in AirportSpec.ApronEntryX)
                if (Mathf.Abs(x - ex) < 40f) return 2;
            foreach (float cx in AirportSpec.ConnectorX)
                if (Mathf.Abs(x - cx) < 30f) return 2;
            return Chance(0.35f) ? 1 : 0;
        }

        // ------------------------------------------------------------ the pass

        /// <summary>Everything the field has spilt, cracked, patched and grown, laid
        /// after the markings so it lies over them.</summary>
        void BuildWear()
        {
            BuildPours();
            BuildApronJoints();
            BuildRubberTracks();
            BuildStands();
            BuildPatches();
            BuildWeeds();
            BuildPerimeterTrack();
        }

        // ------------------------------------------------------------ the pours

        /// <summary>The bays the ramp was poured in, a few of them in a slightly
        /// different batch of concrete. This is what stops a big paved area reading as
        /// one sheet, and it is the RIGHT way to do it: a repeating texture over eighty
        /// thousand square metres gives a chessboard whose squares are all the same
        /// square, which is exactly what the field looked like and exactly what nobody
        /// believes. Real concrete varies pour to pour and not metre to metre.
        ///
        /// The bays line up with the crack seal (BuildPatches) because they are the same
        /// joints - a pour edge IS where a slab cracks.</summary>
        void BuildPours()
        {
            var pale = new Painter();
            var dark = new Painter();
            float y = AirportSpec.PaveY + 0.004f;
            const float Bay = 30f;

            for (float x = AirportSpec.ApronX0; x < AirportSpec.ApronX1; x += Bay)
            {
                float x1 = Mathf.Min(AirportSpec.ApronX1, x + Bay);
                for (float z = AirportSpec.ApronZ0; z < AirportSpec.ApronZ1; z += Bay)
                {
                    float z1 = Mathf.Min(AirportSpec.ApronZ1, z + Bay);
                    // most bays are the ramp's own colour and get nothing at all
                    float roll = Rnd();
                    if (roll < 0.62f) continue;
                    var p = roll < 0.82f ? pale : dark;
                    p.Rect(x + 0.1f, x1 - 0.1f, z + 0.1f, z1 - 0.1f, y);
                }
            }

            // and the building apron behind it, which was poured in a different decade
            // from the ramp and shows it
            for (float x = AirportSpec.ApronX0 - 30f; x < AirportSpec.ApronX1 + 30f; x += Bay)
            {
                if (!Chance(0.45f)) continue;
                float x1 = Mathf.Min(AirportSpec.ApronX1 + 30f, x + Bay);
                var roads = new System.Collections.Generic.List<Rect>(AirportLandsidePlan.GateRoads())
                {
                    Rect.MinMaxRect(AirportSpec.ApronX0 - 30f, AirportSpec.ServiceRoadZ - AirportSpec.ServiceRoadWidth * 0.5f,
                        AirportSpec.ApronX1 + 30f, AirportSpec.ServiceRoadZ + AirportSpec.ServiceRoadWidth * 0.5f),
                };
                foreach (var r in AirportLandsidePlan.Subtract(Rect.MinMaxRect(x + 0.1f,
                    AirportSpec.ApronZ1 + 0.1f, x1 - 0.1f, AirportSpec.BuildingFrontZ), roads))
                    pale.Rect(r.xMin, r.xMax, r.yMin, r.yMax, y);
            }

            pale.Emit("Ramp pours pale", _pourPale, _apronRoot);
            dark.Emit("Ramp pours dark", _pourDark, _apronRoot);
        }

        // ------------------------------------------------------------ tyre marks

        // Fine expansion joints establish scale without the high-contrast 30 m
        // checkerboard of the old pour patches. One mesh for the entire apron.
        void BuildApronJoints()
        {
            var joints = new Painter();
            float y = AirportSpec.PaveY + 0.006f;
            const float spacing = 7.5f, halfWidth = 0.022f;
            for (float x = AirportSpec.ApronX0 + spacing; x < AirportSpec.ApronX1; x += spacing)
                joints.Rect(x - halfWidth, x + halfWidth, AirportSpec.ApronZ0, AirportSpec.ApronZ1, y);
            for (float z = AirportSpec.ApronZ0 + spacing; z < AirportSpec.ApronZ1; z += spacing)
                joints.Rect(AirportSpec.ApronX0, AirportSpec.ApronX1, z - halfWidth, z + halfWidth, y);
            joints.Emit("Apron expansion joints", _pourDark, _apronRoot);
        }

        /// <summary>The black off the tyres where an aeroplane turns: the runway ends,
        /// where a departure lines up and a landing turns off, and the connectors. Not
        /// on the straights - a rolling tyre leaves nothing, it is the scrub of a turn
        /// that marks pavement.</summary>
        void BuildRubberTracks()
        {
            var rubber = new Painter();
            float y = AirportSpec.MarkY - 0.003f;
            float half = RunwayHalf, tz = AirportSpec.TaxiwayZ;

            // the turn-round at each threshold: an aeroplane back-tracks to the end,
            // swings through a half circle and lines up, and forty years of that leaves
            // a black arc on the pavement
            for (int end = 0; end < 2; end++)
            {
                float sign = end == 0 ? -1f : 1f;
                var centre = new Vector3(sign * (half - 14f), 0f, 0f);
                for (int i = 0; i < 22; i++)
                {
                    float a = Mathf.PI * (0.15f + 0.7f * i / 21f) * (end == 0 ? 1f : -1f);
                    float r = 9f + Rnd(-1.4f, 1.4f);
                    var p = centre + new Vector3(Mathf.Cos(a) * r * sign, 0f, Mathf.Sin(a) * r);
                    rubber.Turned(p, a * Mathf.Rad2Deg, Rnd(0.5f, 0.9f), Rnd(2.4f, 4.2f), y);
                }
            }

            // and the connectors, where the exit turn is made at speed
            foreach (float cx in AirportSpec.ConnectorX)
            {
                float x = Mathf.Clamp(cx, -half + 30f, half - 30f);
                for (float z = AirportSpec.RunwayHalfWidth; z < tz; z += 5.5f)
                {
                    float t = (z - AirportSpec.RunwayHalfWidth) / Mathf.Max(1f, tz - AirportSpec.RunwayHalfWidth);
                    float fade = 1f - t * 0.7f;
                    for (int s = -1; s <= 1; s += 2)
                        rubber.Turned(new Vector3(x + s * (1.4f + t * 1.2f), 0f, z), 0f,
                                      0.7f * fade, 4.5f, y);
                }
            }

            rubber.Emit("Tyre marks", _rubberMat, _markingRoot);
        }

        // ------------------------------------------------------------ the stands

        /// <summary>Oil and jet fuel: under every stand, every tie-down and the shop
        /// door. Concrete an aeroplane has been parked on is black in the shape of the
        /// aeroplane, and this is most of what makes a ramp look used.</summary>
        void BuildStands()
        {
            var stain = new Painter();
            float y = AirportSpec.MarkY + 0.004f;

            // the airline stands: a wide smear under each engine and a run of drips
            // back along the fuselage line
            foreach (float sx in AirportSpec.CommuterStandX)
            {
                float stop = AirportSpec.CommuterStandZ;
                Splash(stain, new Vector3(sx, 0f, stop - 9f), 5.5f, 7f, 9, y);
                for (int s = -1; s <= 1; s += 2)
                    Splash(stain, new Vector3(sx + s * 7.5f, 0f, stop - 12f), 4f, 4f, 6, y);
            }

            // the tie-downs: a small patch under each nose, only where an aeroplane
            // has actually been standing - the front row, mostly
            for (int row = 0; row < AirportSpec.TieDownRows; row++)
            {
                float z = AirportSpec.TieDownRowZ0 + row * AirportSpec.TieDownRowPitch;
                for (float x = AirportSpec.TieDownX0; x <= AirportSpec.TieDownX1 + 0.1f; x += AirportSpec.TieDownPitch)
                {
                    if (!Chance(row == 0 ? 0.8f : row == 1 ? 0.55f : 0.3f)) continue;
                    Splash(stain, new Vector3(x + Rnd(-1f, 1f), 0f, z - 1.5f), 2.2f, 3f, 4, y);
                }
            }

            // the fuel island, the shop door and the freight dock - where the ground
            // equipment stands with its engine running
            Splash(stain, new Vector3(AirportSpec.FuelIslandX, 0f, AirportSpec.FuelIslandZ - 5f), 9f, 5f, 12, y);
            Splash(stain, new Vector3(AirportSpec.MaintHangarX, 0f, AirportSpec.BuildingFrontZ - 8f), 13f, 6f, 14, y);
            Splash(stain, new Vector3(AirportSpec.CargoX, 0f, AirportSpec.BuildingFrontZ - 7f), 11f, 5f, 11, y);
            Splash(stain, new Vector3(AirportSpec.ArffX, 0f, AirportSpec.BuildingFrontZ - 8f), 7f, 5f, 7, y);
            for (int i = 0; i < AirportSpec.Hangars; i++)
                Splash(stain, new Vector3(AirportSpec.HangarRowX0 + i * AirportSpec.HangarPitch, 0f,
                                          AirportSpec.BuildingFrontZ - 6f), 6f, 4f, Chance(0.6f) ? 6 : 3, y);

            stain.Emit("Ramp stains", _stainMat, _markingRoot);
        }

        /// <summary>A cluster of overlapping blots about a centre - one stain is a
        /// rectangle and reads as one, half a dozen of them at odd angles read as a
        /// spill.</summary>
        void Splash(Painter p, Vector3 centre, float width, float depth, int blots, float y)
        {
            for (int i = 0; i < blots; i++)
                p.Turned(centre + new Vector3(Rnd(-width, width) * 0.5f, 0f, Rnd(-depth, depth) * 0.5f),
                         Rnd(0f, 180f), Rnd(0.8f, width * 0.55f), Rnd(0.8f, depth * 0.5f), y);
        }

        // ------------------------------------------------------------ the patches

        /// <summary>Where the frost got in and somebody came out with a lorry of
        /// bitumen: overlay patches on the runway and the taxiway, and the seal down the
        /// long cracks. A patch is a different tarmac from the one round it and always
        /// will be, which is why it is the cheapest thing on an airfield to draw and the
        /// loudest thing on it to see.</summary>
        void BuildPatches()
        {
            var patch = new Painter();
            var seal = new Painter();
            float y = AirportSpec.MarkY - 0.002f;
            float half = RunwayHalf, w = AirportSpec.RunwayHalfWidth;

            // runway: patches out at the edges, where the pavement is thinnest and
            // nothing lands, and never over the touchdown zones or the centreline
            for (int i = 0; i < 14; i++)
            {
                float x = Rnd(-half + 30f, half - 30f);
                float side = Chance(0.5f) ? -1f : 1f;
                float z = side * Rnd(w * 0.45f, w - 1.5f);
                patch.Turned(new Vector3(x, 0f, z), Rnd(-6f, 6f), Rnd(6f, 22f), Rnd(2.5f, 6f), y);
            }
            // the long joint down each side of the runway, sealed in lengths
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * (w - 0.35f);
                for (float x = -half; x < half; x += Rnd(40f, 110f))
                {
                    float len = Rnd(25f, 70f);
                    seal.Rect(x, Mathf.Min(half, x + len), z - 0.12f, z + 0.12f, y);
                }
            }
            // and the cross joints, every slab-and-a-bit, only some of them opened
            for (float x = -half + 40f; x < half - 40f; x += 38f)
            {
                if (!Chance(0.42f)) continue;
                float from = Chance(0.5f) ? -w : -w * Rnd(0.1f, 0.6f);
                float to = from + Rnd(w * 0.5f, w * 1.7f);
                seal.Rect(x - 0.12f, x + 0.12f, from, Mathf.Min(w, to), y);
            }

            // the taxiway: it carries the same weight at walking pace and breaks up
            // faster than the runway does
            float tz = AirportSpec.TaxiwayZ, th = AirportSpec.TaxiwayHalf;
            for (int i = 0; i < 12; i++)
            {
                float x = Rnd(-half, half);
                patch.Turned(new Vector3(x, 0f, tz + Rnd(-th + 1f, th - 1f)), Rnd(-8f, 8f), Rnd(5f, 16f), Rnd(2f, 5f), y);
            }
            for (float x = -half; x < half; x += Rnd(30f, 80f))
                seal.Rect(x, Mathf.Min(half, x + Rnd(20f, 60f)), tz + th - 0.5f, tz + th - 0.26f, y);

            // the ramp: concrete, so it cracks in slabs rather than patching in blots
            for (float x = AirportSpec.ApronX0 + 20f; x < AirportSpec.ApronX1; x += 30f)
            {
                if (!Chance(0.5f)) continue;
                float z0 = Rnd(AirportSpec.ApronZ0, AirportSpec.ApronZ1 - 30f);
                seal.Rect(x - 0.11f, x + 0.11f, z0, z0 + Rnd(12f, 34f), y);
            }

            patch.Emit("Pavement patches", _patchMat, _markingRoot);
            seal.Emit("Crack seal", _sealMat, _markingRoot);
        }

        // ------------------------------------------------------------ the weeds

        /// <summary>Grass through the seams. It grows where nothing runs over it: the
        /// outer edge of the runway shoulder, the back of the ramp, the corners of the
        /// tie-down rows, the joint between two surfaces laid twenty years apart. Kept
        /// OFF the runway, the taxiway centre and the stands, because a field that lets
        /// grass grow where an aeroplane rolls is a field that has closed.</summary>
        void BuildWeeds()
        {
            var weed = new Painter();
            float y = AirportSpec.MarkY + 0.006f;
            float half = RunwayHalf;
            float shoulder = AirportSpec.RunwayHalfWidth + AirportSpec.RunwayShoulder;

            // the runway shoulder's outer edge, in broken lengths
            for (float x = -half - 10f; x < half + 10f; x += Rnd(4f, 14f))
            {
                if (!Chance(0.55f)) continue;
                float len = Rnd(1.5f, 7f);
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = s * (shoulder - Rnd(0f, 0.9f));
                    weed.Rect(x, x + len, Mathf.Min(z, z + s * 0.7f), Mathf.Max(z, z + s * 0.7f), y);
                }
            }

            // the taxiway shoulder, the same
            float te = AirportSpec.TaxiwayZ + AirportSpec.TaxiwayHalf + AirportSpec.TaxiwayShoulder;
            float tw = AirportSpec.TaxiwayZ - AirportSpec.TaxiwayHalf - AirportSpec.TaxiwayShoulder;
            for (float x = -half; x < half; x += Rnd(5f, 16f))
            {
                if (!Chance(0.5f)) continue;
                float len = Rnd(1.5f, 6f);
                weed.Rect(x, x + len, te - 0.8f, te, y);
                if (Chance(0.6f)) weed.Rect(x, x + len, tw, tw + 0.8f, y);
            }

            // the back of the ramp, against the service road and the building line,
            // where a mower cannot reach and a broom never has
            for (float x = AirportSpec.ApronX0; x < AirportSpec.ApronX1; x += Rnd(3f, 11f))
            {
                if (Chance(0.45f)) weed.Rect(x, x + Rnd(1f, 4f), AirportSpec.ApronZ0 - 0.1f, AirportSpec.ApronZ0 + Rnd(0.4f, 1.1f), y);
                if (Chance(0.3f)) weed.Rect(x, x + Rnd(1f, 3f), AirportSpec.ApronZ1 - Rnd(0.4f, 1f), AirportSpec.ApronZ1 + 0.1f, y);
            }

            // and the ends of the tie-down rows, which is the least-swept concrete on
            // any airfield in the country
            for (int row = 0; row < AirportSpec.TieDownRows; row++)
            {
                float z = AirportSpec.TieDownRowZ0 + row * AirportSpec.TieDownRowPitch;
                for (int s = -1; s <= 1; s += 2)
                {
                    float x = s < 0 ? AirportSpec.TieDownX0 - 8f : AirportSpec.TieDownX1 + 4f;
                    for (int i = 0; i < 5; i++)
                        weed.Turned(new Vector3(x + Rnd(-3f, 3f), 0f, z + Rnd(-6f, 6f)), Rnd(0f, 90f),
                                    Rnd(0.5f, 1.8f), Rnd(0.5f, 1.6f), y);
                }
            }

            weed.Emit("Weeds", _grassMat, _markingRoot);
        }

        // ------------------------------------------------------------ the track

        /// <summary>The perimeter track: bare earth inside the wire, all the way round,
        /// worn by the truck that drives the fence twice a day and by the mower. Every
        /// real airfield has one and it is the single line that most says a place is
        /// enclosed and patrolled rather than merely mown.</summary>
        void BuildPerimeterTrack()
        {
            var dirt = new Painter();
            float y = AirportSpec.LandY + 0.02f;
            const float wide = 3.2f;

            // down each flank. NOT along the back wire: the rear yard road is already
            // laid there (Ground.BuildApron) and dirt under asphalt is dirt nobody sees
            for (int s = 0; s < 2; s++)
            {
                float x = (s == 0 ? AirportSpec.FenceX0 : AirportSpec.FenceX1) + (s == 0 ? 4f : -4f);
                Track(dirt, new Vector3(x, 0f, AirportSpec.FenceZ - 5f),
                            new Vector3(x, 0f, AirportSpec.FenceSouthZ + 4f), wide, y);
            }
            // and the run along the south side of the runway, which is how the fence
            // truck gets from one flank to the other without crossing the strip
            Track(dirt, new Vector3(AirportSpec.FenceX0 + 4f, 0f, AirportSpec.FenceSouthZ + 4f),
                        new Vector3(AirportSpec.FenceX1 - 4f, 0f, AirportSpec.FenceSouthZ + 4f), wide, y);

            // the spur out to the windsock, which somebody has to walk to
            Track(dirt, new Vector3(AirportSpec.WindsockX, 0f, AirportSpec.TaxiwayZ - AirportSpec.TaxiwayHalf - 2f),
                        new Vector3(AirportSpec.WindsockX, 0f, AirportSpec.WindsockZ), 1.6f, y);

            dirt.Emit("Perimeter track", _dirtMat, _markingRoot);
        }

        /// <summary>A worn track from A to B: a run of overlapping lengths that wander
        /// off the line, because a track a truck has driven is not a painted stripe.</summary>
        void Track(Painter p, Vector3 a, Vector3 b, float width, float y)
        {
            var d = b - a; d.y = 0f;
            float len = d.magnitude;
            if (len < 1f) return;
            var dir = d / len;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            var side = new Vector3(dir.z, 0f, -dir.x);
            for (float s = 0f; s < len; s += 9f)
            {
                float run = Mathf.Min(11f, len - s);
                if (run < 1f) break;
                var at = a + dir * (s + run * 0.5f) + side * Rnd(-0.7f, 0.7f);
                p.Turned(at, yaw + Rnd(-2.5f, 2.5f), width * Rnd(0.75f, 1.15f), run, y);
            }
        }
    }
}
