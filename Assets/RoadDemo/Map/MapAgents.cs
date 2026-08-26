using System.Collections.Generic;
using HarborDemo;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Everything on the map that moves, drawn straight into the screen buffer every
    /// frame at full raster resolution. This is the ONLY per-frame rasterising the map
    /// does - the ground, the buildings and the turf tint are cached buffers blitted
    /// underneath.
    ///
    /// TWO SHAPES AND NO OUTLINES. A crew is a GLOWING DOT: a stack of faint additive
    /// squares in the family's colour with a hard 3x3 core in its bright cut, breathing
    /// on its own slow cycle, and a bloom that grows with the number of men in it - so
    /// how big a crew is can be read from across the map without a number. A vehicle is
    /// the opposite: a hard block, nine real pixels by four (thirteen for a lorry), with
    /// a bright trim at the leading end and one pixel of shade down one side.
    ///
    /// Both wear their family's colour, and that is deliberate. Person against vehicle
    /// is told apart by SHAPE - soft round glow against hard long block - and never by
    /// colour, which is reserved for saying whose they are. An earlier revision made
    /// vehicles grey to tell them apart and it cost the map the one thing it most needed
    /// to show: that the car pulling up outside your front belongs to somebody.
    ///
    /// NOTHING here is outlined or haloed. The only shadow tone is
    /// <see cref="MapPalette.Shadow"/>, one pixel, one side. An earlier revision ringed
    /// every sprite in near-black and the map read as black mush at any distance.
    /// </summary>
    public sealed class MapAgents
    {
        /// <summary>How many of the crowd are worth plotting. Above this the map is a
        /// grey wash of civilians with the news buried in it.</summary>
        public const int CrowdBudget = 900;

        /// <summary>A vehicle's body, in real pixels: long enough to read as a vehicle
        /// at a glance, and clearly longer than it is thick.</summary>
        const int CarLength = 9;
        const int TruckLength = 13;
        const int CarThick = 4;

        /// <summary>The bloom's floor, before the crew's own size adds to it.</summary>
        const int BloomBase = 2;
        const int BloomPerMan = 1;
        const int BloomMost = 5;

        readonly List<HarborBob> _vessels = new List<HarborBob>();
        float _vesselsDue;

        // ------------------------------------------------------------------ crews

        /// <summary>
        /// The crews - the player's, the rivals', the law's - each one dot, and then as
        /// much of the crowd as the budget allows.
        /// </summary>
        public void Crews(MapRaster into, MapSheet sheet, DemoCrews crews,
            List<CivilianAgent> crowd, List<PoliceFootPatrol> officers,
            HashSet<int> selected, int inspectedCrew, bool plotCrowd, float time)
        {
            if (crews != null)
            {
                foreach (var unit in crews.Units)
                {
                    if (unit == null || unit.Wiped)
                        continue;
                    var boss = unit.Boss;
                    if (boss == null || boss.Dead || boss.Tf == null)
                        continue;

                    Glow(into, sheet, boss.Tf.position,
                        unit.IsPolice ? -2 : unit.Faction,
                        unit.Standing(), unit.CrewId, time,
                        selected != null && selected.Contains(unit.CrewId),
                        unit.CrewId == inspectedCrew);
                }
            }

            if (officers != null)
                foreach (var officer in officers)
                {
                    if (officer == null || officer.Tf == null ||
                        !officer.Tf.gameObject.activeInHierarchy)
                        continue;
                    Pip(into, sheet, officer.Tf.position, MapPalette.White, MapPalette.Water);
                }

            if (!plotCrowd || crowd == null)
                return;

            // Thinned rather than truncated: taking the first nine hundred would plot one
            // corner of the town and leave the rest empty, which reads as a curfew.
            var stride = Mathf.Max(1, crowd.Count / CrowdBudget);
            for (var i = 0; i < crowd.Count; i += stride)
            {
                var civilian = crowd[i];
                if (civilian == null || civilian.Tf == null ||
                    !civilian.Tf.gameObject.activeInHierarchy)
                    continue;
                Pip(into, sheet, civilian.Tf.position, MapPalette.Unclaimed, default);
            }
        }

        /// <summary>
        /// One crew: a bloom whose radius grows with the men standing in it, a hard core
        /// in the family's bright cut, and a slow breath keyed off the crew's own id so
        /// no two pulse together.
        /// </summary>
        void Glow(MapRaster into, MapSheet sheet, Vector3 world, int gang, int men,
            int crewId, float time, bool selected, bool inspected)
        {
            var at = sheet.ToReal(world);
            var x = Mathf.RoundToInt(at.x);
            var y = Mathf.RoundToInt(at.y);

            var reach = BloomBase + Mathf.Min(BloomMost, men * BloomPerMan);
            if (x < -reach - 6 || y < -reach - 6 ||
                x > MapRaster.W + reach + 6 || y > MapRaster.H + reach + 6)
                return;

            Color32 body, core;
            if (gang == -2)
            {
                // The law is not a family and must never wear one's colour.
                body = MapPalette.Water;
                core = MapPalette.White;
            }
            else if (gang < 0)
            {
                body = MapPalette.Unclaimed;
                core = MapPalette.Hex(0xb8bdb4);
            }
            else
            {
                body = MapPalette.Gang(gang);
                core = MapPalette.Tag(gang);
            }

            var pulse = 0.7f + 0.3f * Mathf.Sin(time * 2.63f + crewId);
            var span = reach * 2 + 1;

            into.AddRect(x - reach, y - reach, span, span, body, 0.13f * pulse);
            into.AddRect(x - 1, y - reach, 3, span, body, 0.30f * pulse);
            into.AddRect(x - reach, y - 1, span, 3, body, 0.30f * pulse);
            into.AddRect(x - 2, y - 2, 5, 5, body, 0.75f);
            into.Fill(x - 1, y - 1, 3, 3, core);

            if (selected)
            {
                var tick = reach + 3;
                into.Fill(x, y - tick, 1, 2, MapPalette.White);
                into.Fill(x, y + tick, 1, 2, MapPalette.White);
                into.Fill(x - tick, y, 2, 1, MapPalette.White);
                into.Fill(x + tick, y, 2, 1, MapPalette.White);
            }

            if (inspected)
                into.Fill(x - 1, y - reach - 4, 3, 1, MapPalette.Yellow);
        }

        /// <summary>One of the crowd, or one officer on foot: small, flat, and NOT a
        /// glow - the glow means a crew, and a street of pedestrians lighting up like
        /// crews would drown the only thing on this map worth looking for.</summary>
        static void Pip(MapRaster into, MapSheet sheet, Vector3 world, Color32 colour,
            Color32 mark)
        {
            var at = sheet.ToReal(world);
            var x = Mathf.RoundToInt(at.x);
            var y = Mathf.RoundToInt(at.y);
            if (x < -2 || y < -3 || x > MapRaster.W + 2 || y > MapRaster.H + 3)
                return;

            into.Fill(x + 1, y + 1, 2, 2, MapPalette.Shadow);
            into.Fill(x, y, 2, 2, colour);
            if (mark.a != 0)
                into.Fill(x, y, 1, 1, mark);
        }

        // --------------------------------------------------------------- vehicles

        public void Vehicles(MapRaster into, MapSheet sheet, List<DemoVehicle> traffic,
            List<PolicePatrolCar> patrols, DemoCrews crews)
        {
            if (traffic != null)
                foreach (var car in traffic)
                {
                    if (car == null || car.Tf == null || !car.Tf.gameObject.activeInHierarchy)
                        continue;
                    var paint = Civilian(car.Tf.GetHashCode());
                    Block(into, sheet, car.Tf, paint, Lighter(paint), CarLength);
                }

            if (patrols != null)
                foreach (var car in patrols)
                {
                    if (car == null || car.Tf == null || !car.Tf.gameObject.activeInHierarchy)
                        continue;
                    Block(into, sheet, car.Tf, MapPalette.White, MapPalette.Water, CarLength);
                }

            if (crews == null)
                return;

            foreach (var unit in crews.Units)
            {
                if (unit == null || unit.Wiped)
                    continue;
                var car = crews.CarOf(unit);
                if (car == null || car.Tf == null || !car.Tf.gameObject.activeInHierarchy)
                    continue;
                // A family's car wears the family's colour. Shape says it is a car;
                // colour says whose.
                var paint = unit.IsPolice ? MapPalette.White : MapPalette.Gang(unit.Faction);
                var trim = unit.IsPolice ? MapPalette.Water : MapPalette.Tag(unit.Faction);
                Block(into, sheet, car.Tf, paint, trim, TruckLength);
            }
        }

        /// <summary>
        /// A vehicle: a hard block laid along whichever axis it is pointing down, one
        /// pixel of shade down a single side, and a two-pixel bright trim at the leading
        /// end so which way it is going can be read without an arrow.
        /// </summary>
        static void Block(MapRaster into, MapSheet sheet, Transform tf, Color32 colour,
            Color32 trim, int length)
        {
            var at = sheet.ToReal(tf.position);
            var x = Mathf.RoundToInt(at.x);
            var y = Mathf.RoundToInt(at.y);
            if (x < -20 || y < -20 || x > MapRaster.W + 20 || y > MapRaster.H + 20)
                return;

            var forward = tf.forward;
            if (Mathf.Abs(forward.z) > Mathf.Abs(forward.x))
            {
                into.Fill(x + CarThick, y + 1, 1, length, MapPalette.Shadow);
                into.Fill(x, y, CarThick, length, colour);
                into.Fill(x, forward.z > 0f ? y : y + length - 2, CarThick, 2, trim);
            }
            else
            {
                into.Fill(x + 1, y + CarThick, length, 1, MapPalette.Shadow);
                into.Fill(x, y, length, CarThick, colour);
                into.Fill(forward.x > 0f ? x + length - 2 : x, y, 2, CarThick, trim);
            }
        }

        static readonly Color32[] Metal =
        {
            MapPalette.Line, MapPalette.Concrete, MapPalette.Steel, MapPalette.BldgA,
        };

        /// <summary>Ordinary traffic is not anybody's, so it is painted in metal rather
        /// than in a colour that would claim it for a family. Fixed to the car and not
        /// to the frame, so it does not change colour as it drives.</summary>
        static Color32 Civilian(int id) => Metal[(id & 0x7fffffff) % Metal.Length];

        static Color32 Lighter(Color32 c) => new Color32(
            (byte)Mathf.Min(255, c.r + 40), (byte)Mathf.Min(255, c.g + 40),
            (byte)Mathf.Min(255, c.b + 40), 255);

        // ------------------------------------------------------------------- water

        /// <summary>
        /// The shipping. Found by the bob component every vessel's model carries - the
        /// harbour keeps its ships in a private list of its own and the map is not worth
        /// opening the district up for. Re-found on a slow timer, because a freighter is
        /// made and sunk over minutes, not frames.
        /// </summary>
        public void Ships(MapRaster into, MapSheet sheet)
        {
            if (Time.unscaledTime >= _vesselsDue)
            {
                _vesselsDue = Time.unscaledTime + 4f;
                _vessels.Clear();
                _vessels.AddRange(Object.FindObjectsByType<HarborBob>(
                    FindObjectsInactive.Exclude));
            }

            foreach (var vessel in _vessels)
            {
                if (vessel == null)
                    continue;

                var renderer = vessel.GetComponentInChildren<Renderer>();
                if (renderer == null)
                    continue;

                var bounds = renderer.bounds;
                var at = sheet.ToReal(bounds.center);
                var x = Mathf.RoundToInt(at.x);
                var y = Mathf.RoundToInt(at.y);
                if (x < -60 || y < -40 || x > MapRaster.W + 60 || y > MapRaster.H + 40)
                    continue;

                var perMetre = sheet.RealPerMetre;
                var alongX = bounds.size.x >= bounds.size.z;
                var length = Mathf.Max(6, Mathf.RoundToInt(
                    (alongX ? bounds.size.x : bounds.size.z) * perMetre));
                var beam = Mathf.Max(3, Mathf.RoundToInt(
                    (alongX ? bounds.size.z : bounds.size.x) * perMetre));

                if (alongX)
                {
                    into.Fill(x - length / 2 + 1, y + beam / 2, length, 1, MapPalette.Shadow);
                    into.Fill(x - length / 2, y - beam / 2, length, beam, MapPalette.Steel);
                    into.Fill(x - length / 2 + 2, y - beam / 2 - 2, 3, 2, MapPalette.Red);
                }
                else
                {
                    into.Fill(x + beam / 2, y - length / 2 + 1, 1, length, MapPalette.Shadow);
                    into.Fill(x - beam / 2, y - length / 2, beam, length, MapPalette.Steel);
                    into.Fill(x - beam / 2 - 2, y - length / 2 + 2, 2, 3, MapPalette.Red);
                }
            }
        }

        // ----------------------------------------------------------------- markers

        /// <summary>The expanding cross an order leaves at the place it was given.</summary>
        public static void Markers(MapRaster into, MapSheet sheet,
            List<MapOrders.Marker> markers)
        {
            if (markers == null)
                return;
            for (var i = markers.Count - 1; i >= 0; i--)
            {
                var marker = markers[i];
                if (--marker.Life <= 0)
                {
                    markers.RemoveAt(i);
                    continue;
                }
                var at = sheet.ToReal(marker.World);
                var x = Mathf.RoundToInt(at.x);
                var y = Mathf.RoundToInt(at.y);
                var reach = MapOrders.MarkerRadius(marker) * MapRaster.S;
                var colour = MapOrders.MarkerColour(marker.Kind);
                into.Fill(x - reach, y, reach * 2, 1, colour);
                into.Fill(x, y - reach, 1, reach * 2, colour);
            }
        }

        /// <summary>The drag box, marching two real pixels on and one off. Given in
        /// AUTHORED coordinates, because that is the space the drag itself lives in.</summary>
        public static void SelectionBox(MapRaster into, Vector2 from, Vector2 to)
        {
            var x0 = Mathf.RoundToInt(Mathf.Min(from.x, to.x) * MapRaster.S);
            var y0 = Mathf.RoundToInt(Mathf.Min(from.y, to.y) * MapRaster.S);
            var w = Mathf.RoundToInt(Mathf.Abs(to.x - from.x) * MapRaster.S);
            var h = Mathf.RoundToInt(Mathf.Abs(to.y - from.y) * MapRaster.S);
            var colour = (Color32)MapPalette.PlayerAccent;
            for (var x = 0; x <= w; x += 3)
            {
                into.Fill(x0 + x, y0, 2, 1, colour);
                into.Fill(x0 + x, y0 + h, 2, 1, colour);
            }
            for (var y = 0; y <= h; y += 3)
            {
                into.Fill(x0, y0 + y, 1, 2, colour);
                into.Fill(x0 + w, y0 + y, 1, 2, colour);
            }
        }

        /// <summary>The frame round the building the card is open on, blinking between
        /// bone and gold on the sheet's own 600 ms cycle. Real pixels.</summary>
        public static void Blink(MapRaster into, RectInt real, float time)
        {
            var colour = time * 1000f % 600f < 300f ? MapPalette.White : MapPalette.Yellow;
            into.Frame(real.xMin - 2, real.yMin - 2, real.width + 4, real.height + 4, 1, colour);
        }
    }
}
