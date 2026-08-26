using System.Collections.Generic;
using HarborDemo;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Everything on the map that moves, drawn straight into the screen buffer every
    /// frame. This is the ONLY per-frame rasterising the map does - the ground, the
    /// buildings and the turf wash are all cached buffers blitted underneath - and it is
    /// cheap because a man is three pixels and a car is six.
    ///
    /// The sprites are the design sheet's, to the pixel. A person is 1x3 with the head
    /// pixel in the family's BRIGHT cut, because at 1:1 that one pixel is the whole of
    /// what the player reads: who that is. Everything else about a figure - selected,
    /// inspected, on foot or in a car - is said in the pixels AROUND it, so the
    /// affiliation pixel is never spent on anything else.
    ///
    /// The crowd is budgeted. A city of ten thousand civilians would cost more to plot
    /// than the map is worth, and a map that says where every pedestrian is says nothing
    /// - so the crews and the law are drawn first and always, and the crowd fills
    /// whatever is left of <see cref="CrowdBudget"/>.
    /// </summary>
    public sealed class MapAgents
    {
        /// <summary>How many of the crowd are worth plotting. Above this the map is a
        /// grey wash of civilians with the news buried in it.</summary>
        public const int CrowdBudget = 900;

        const int ManHeight = 3;
        const int CarLength = 3;
        const int TruckLength = 5;

        readonly List<HarborBob> _vessels = new List<HarborBob>();
        float _vesselsDue;

        // ------------------------------------------------------------------ people

        /// <summary>
        /// The crews first - the player's, the rivals', the law's - and then as much of
        /// the crowd as the budget allows.
        /// </summary>
        public void People(MapRaster into, MapSheet sheet, DemoCrews crews,
            List<CivilianAgent> crowd, List<PoliceFootPatrol> officers,
            HashSet<int> selected, int inspectedCrew, bool plotCrowd)
        {
            if (crews != null)
            {
                foreach (var unit in crews.Units)
                {
                    if (unit == null)
                        continue;
                    var lit = selected != null && selected.Contains(unit.CrewId);
                    var read = unit.CrewId == inspectedCrew;
                    var gang = unit.IsPolice ? -2 : unit.Faction;
                    foreach (var man in unit.All())
                    {
                        if (man == null || man.Dead || man.Tf == null ||
                            !man.Tf.gameObject.activeInHierarchy)
                            continue;
                        Man(into, sheet, man.Tf.position, gang,
                            lit, read && man.IsLieutenant, man.IsLieutenant);
                    }
                }
            }

            if (officers != null)
                foreach (var officer in officers)
                {
                    if (officer == null || officer.Tf == null ||
                        !officer.Tf.gameObject.activeInHierarchy)
                        continue;
                    Man(into, sheet, officer.Tf.position, -2, false, false, false);
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
                Man(into, sheet, civilian.Tf.position, -1, false, false, false);
            }
        }

        /// <summary>
        /// One figure. Gang -1 is a civilian, -2 the law, anything else a family.
        /// </summary>
        void Man(MapRaster into, MapSheet sheet, Vector3 world, int gang,
            bool selected, bool inspected, bool lieutenant)
        {
            var at = sheet.ToPx(world);
            var x = Mathf.FloorToInt(at.x);
            var y = Mathf.FloorToInt(at.y);
            if (x < -3 || y < -6 || x > MapRaster.W + 3 || y > MapRaster.H + 6)
                return;

            Color32 body, head;
            if (gang == -2)
            {
                // The law is not a family and must never wear one's colour: white body,
                // and the head pixel the blue of a light bar.
                body = MapPalette.White;
                head = MapPalette.Water;
            }
            else if (gang < 0)
            {
                body = MapPalette.Unclaimed;
                head = MapPalette.Hex(0xb8bdb4);
            }
            else
            {
                body = MapPalette.Gang(gang);
                head = MapPalette.Tag(gang);
            }

            into.Fill(x + 1, y + 1, 1, ManHeight, MapPalette.Ink);
            into.Fill(x, y + 1, 1, ManHeight - 1, body);
            into.Fill(x, y, 1, 1, head);

            // A lieutenant is the man the player is looking for on a street of his own
            // colour: one pixel wider at the shoulders, and nothing else.
            if (lieutenant)
                into.Fill(x - 1, y + 1, 1, 1, head);

            if (selected)
            {
                into.Fill(x - 2, y - 2, 1, 1, MapPalette.White);
                into.Fill(x + 2, y - 2, 1, 1, MapPalette.White);
                into.Fill(x - 2, y + 4, 1, 1, MapPalette.White);
                into.Fill(x + 2, y + 4, 1, 1, MapPalette.White);
                into.Fill(x - 1, y + 5, 3, 1, (Color32)MapPalette.PlayerAccent);
            }

            if (inspected)
                into.Fill(x - 1, y - 4, 3, 1, MapPalette.Yellow);
        }

        // ---------------------------------------------------------------- vehicles

        public void Vehicles(MapRaster into, MapSheet sheet, List<DemoVehicle> traffic,
            List<PolicePatrolCar> patrols, DemoCrews crews)
        {
            if (traffic != null)
                foreach (var car in traffic)
                {
                    if (car == null || car.Tf == null || !car.Tf.gameObject.activeInHierarchy)
                        continue;
                    var tf = car.Tf;
                    Car(into, sheet, tf, Paint(tf.GetHashCode()), CarLength);
                }

            if (patrols != null)
                foreach (var car in patrols)
                {
                    if (car == null || car.Tf == null || !car.Tf.gameObject.activeInHierarchy)
                        continue;
                    var tf = car.Tf;
                    Car(into, sheet, tf, MapPalette.White, CarLength);
                    // The light bar: one red pixel, which is all a squad car needs to
                    // read as one at this size.
                    var light = sheet.ToPx(tf.position);
                    into.Fill(Mathf.FloorToInt(light.x), Mathf.FloorToInt(light.y), 1, 1, MapPalette.Red);
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
                Car(into, sheet, car.Tf,
                    unit.IsPolice ? MapPalette.White : MapPalette.Gang(unit.Faction),
                    TruckLength);
            }
        }

        /// <summary>A car: three pixels by two, laid along whichever axis it is
        /// pointing down, with its shadow under it.</summary>
        static void Car(MapRaster into, MapSheet sheet, Transform tf, Color32 colour, int length)
        {
            var at = sheet.ToPx(tf.position);
            var x = Mathf.FloorToInt(at.x);
            var y = Mathf.FloorToInt(at.y);
            if (x < -8 || y < -8 || x > MapRaster.W + 8 || y > MapRaster.H + 8)
                return;

            var forward = tf.forward;
            var vertical = Mathf.Abs(forward.z) > Mathf.Abs(forward.x);
            if (vertical)
            {
                into.Fill(x, y, 2, length, MapPalette.Ink);
                into.Fill(x, y, 2, length - 1, colour);
            }
            else
            {
                into.Fill(x, y, length, 2, MapPalette.Ink);
                into.Fill(x, y, length - 1, 2, colour);
            }
        }

        static readonly Color32[] CarPaints =
        {
            MapPalette.Red, MapPalette.White, MapPalette.Steel,
            MapPalette.Yellow, MapPalette.BldgB, MapPalette.BldgC,
        };

        /// <summary>A car's colour, fixed to the car and not to the frame - hashed off
        /// its instance so it does not change colour as it drives.</summary>
        static Color32 Paint(int id) =>
            CarPaints[(id & 0x7fffffff) % CarPaints.Length];

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

                var tf = vessel.transform;
                var renderer = tf.GetComponentInChildren<Renderer>();
                if (renderer == null)
                    continue;

                var bounds = renderer.bounds;
                var at = sheet.ToPx(bounds.center);
                var x = Mathf.FloorToInt(at.x);
                var y = Mathf.FloorToInt(at.y);
                if (x < -40 || y < -20 || x > MapRaster.W + 40 || y > MapRaster.H + 20)
                    continue;

                var alongX = bounds.size.x >= bounds.size.z;
                var length = Mathf.Max(4, Mathf.RoundToInt(
                    (alongX ? bounds.size.x : bounds.size.z) * sheet.PixelsPerMetre));
                var beam = Mathf.Max(2, Mathf.RoundToInt(
                    (alongX ? bounds.size.z : bounds.size.x) * sheet.PixelsPerMetre));

                if (alongX)
                {
                    into.Fill(x - length / 2, y - beam / 2, length, beam + 1, MapPalette.Ink);
                    into.Fill(x - length / 2, y - beam / 2, length - 1, beam, MapPalette.Steel);
                    into.Fill(x - length / 2 + 2, y - beam / 2 - 1, 3, 2, MapPalette.Red);
                }
                else
                {
                    into.Fill(x - beam / 2, y - length / 2, beam + 1, length, MapPalette.Ink);
                    into.Fill(x - beam / 2, y - length / 2, beam, length - 1, MapPalette.Steel);
                    into.Fill(x - beam / 2 - 1, y - length / 2 + 2, 2, 3, MapPalette.Red);
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
                var at = sheet.ToPx(marker.World);
                var x = Mathf.FloorToInt(at.x);
                var y = Mathf.FloorToInt(at.y);
                var reach = MapOrders.MarkerRadius(marker);
                var colour = MapOrders.MarkerColour(marker.Kind);
                into.Fill(x - reach, y, reach * 2, 1, colour);
                into.Fill(x, y - reach, 1, reach * 2, colour);
            }
        }

        /// <summary>The drag box, marching a pixel on and a pixel off.</summary>
        public static void SelectionBox(MapRaster into, Vector2 from, Vector2 to)
        {
            var x0 = Mathf.FloorToInt(Mathf.Min(from.x, to.x));
            var y0 = Mathf.FloorToInt(Mathf.Min(from.y, to.y));
            var w = Mathf.FloorToInt(Mathf.Abs(to.x - from.x));
            var h = Mathf.FloorToInt(Mathf.Abs(to.y - from.y));
            var colour = (Color32)MapPalette.PlayerAccent;
            for (var x = 0; x <= w; x += 2)
            {
                into.Fill(x0 + x, y0, 1, 1, colour);
                into.Fill(x0 + x, y0 + h, 1, 1, colour);
            }
            for (var y = 0; y <= h; y += 2)
            {
                into.Fill(x0, y0 + y, 1, 1, colour);
                into.Fill(x0 + w, y0 + y, 1, 1, colour);
            }
        }

        /// <summary>The frame round the building the card is open on, blinking between
        /// bone and gold on the sheet's own 600 ms cycle.</summary>
        public static void Blink(MapRaster into, RectInt box, float time)
        {
            var colour = time * 1000f % 600f < 300f ? MapPalette.White : MapPalette.Yellow;
            into.Frame(box.xMin - 2, box.yMin - 2, box.width + 4, box.height + 4, 1, colour);
        }
    }
}
