using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>One named piece of the city, and who is running it.</summary>
    public sealed class MapDistrict
    {
        public int Id;
        public string Name;

        /// <summary>Ground it covers, world XZ.</summary>
        public Rect World;

        /// <summary>Null for a quarter of the grid; set for the outlying districts,
        /// which are a different kind of place and say so on the card.</summary>
        public DistrictKind? Kind;

        /// <summary>The family running it, or -1 for ground nobody holds.</summary>
        public int Gang = -1;

        /// <summary>Two families with an equal claim - the ground is being argued over,
        /// and the map says so by crawling its border.</summary>
        public bool Contested;

        /// <summary>How many family doors stand in it, whoever's they are.</summary>
        public int Fronts;
    }

    /// <summary>
    /// The turf layer: who holds what, and the wash that says so.
    ///
    /// The design sheet's districts were nine hand-placed rectangles. Ours are the
    /// city's own - the named quarters of the grid (RoadDemoBuilder.CityQuarters, three
    /// blocks by two, the middle one Downtown) and the outlying districts hanging off
    /// its edges (the port, the field, the villages). Nothing is invented and nothing is
    /// placed: if the roll did not make a quarter, there is no quarter.
    ///
    /// Who holds one is not stored anywhere either, and deliberately: ownership lives on
    /// the fronts (<see cref="GangFront"/>), one door per family, and a district is held
    /// by whichever family has the most doors in it. An equal count is CONTESTED - the
    /// same rule the outfit's own turf arithmetic uses (LivingCity.Outfit.Turf), applied
    /// to a bigger rectangle. A quarter with no door in it at all is unclaimed, unless a
    /// family's door stands within <see cref="Reach"/> of it, which is how a mob's
    /// influence spills a street or two past its own premises.
    ///
    /// The wash itself is baked into a cached buffer with alpha and laid over the base
    /// map, because re-striping a dozen districts every frame is precisely the work the
    /// sheet's performance note says not to do. Only the marching border is drawn live,
    /// and only because a contested one has to crawl.
    /// </summary>
    public sealed class MapTurf
    {
        /// <summary>How far a family's ground reaches past its own door when nobody
        /// keeps a door in the quarter at all. The figure the map has always used.</summary>
        public const float Reach = 240f;

        readonly List<MapDistrict> _all = new List<MapDistrict>();
        readonly Dictionary<int, int> _tally = new Dictionary<int, int>();

        int _stamp = int.MinValue;

        public IReadOnlyList<MapDistrict> All => _all;

        // ------------------------------------------------------------------ collect

        public void Collect(RoadDemoBuilder builder)
        {
            _all.Clear();
            if (builder == null)
                return;

            // A name is never null past this point. The city names its own quarters and
            // its own districts, but the map prints these and a place with no name at
            // all still has to be printable.
            var id = 0;
            foreach (var quarter in builder.CityQuarters)
                _all.Add(new MapDistrict
                {
                    Id = id++,
                    Name = Named(quarter.Name),
                    World = quarter.World,
                });

            foreach (var district in builder.DistrictPlans)
                _all.Add(new MapDistrict
                {
                    Id = id++,
                    Name = Named(district.Name),
                    World = district.World,
                    Kind = district.Kind,
                });

            Version++;
        }

        static string Named(string name) =>
            string.IsNullOrEmpty(name) ? "UNNAMED QUARTER" : name;

        /// <summary>The district a point stands in - the grid's quarters first, so a
        /// village that overlaps the town's edge does not swallow a city block.</summary>
        public MapDistrict At(Vector2 world)
        {
            for (var i = 0; i < _all.Count; i++)
                if (_all[i].World.Contains(world))
                    return _all[i];
            return null;
        }

        public MapDistrict Get(int id) => id >= 0 && id < _all.Count ? _all[id] : null;

        // ------------------------------------------------------------------ resolve

        /// <summary>
        /// Re-reads who holds what. Cheap enough to call on a timer and does nothing at
        /// all unless the roll of fronts has actually changed - a family can be seated
        /// after the map is built, and a front can burn down later.
        /// </summary>
        public bool Resolve(MapOwnership owned)
        {
            var fronts = GangFront.All;
            var stamp = fronts.Count * 31 + (owned?.Version ?? 0);
            for (var i = 0; i < fronts.Count; i++)
                if (fronts[i] != null)
                    stamp = stamp * 31 + fronts[i].GangId;
            if (stamp == _stamp)
                return false;
            _stamp = stamp;

            foreach (var district in _all)
            {
                _tally.Clear();
                district.Fronts = 0;

                for (var i = 0; i < fronts.Count; i++)
                {
                    var front = fronts[i];
                    if (front == null || !district.World.Contains(
                            new Vector2(front.Door.x, front.Door.z)))
                        continue;
                    district.Fronts++;
                    _tally.TryGetValue(front.GangId, out var count);
                    _tally[front.GangId] = count + 1;
                }

                if (_tally.Count == 0)
                {
                    // Nobody keeps a door here. The nearest family's does the talking,
                    // but only from close by - a mob two miles off holds nothing.
                    var centre = district.World.center;
                    var best = -1;
                    var nearest = Reach * Reach;
                    for (var i = 0; i < fronts.Count; i++)
                    {
                        var front = fronts[i];
                        if (front == null)
                            continue;
                        var dx = front.Door.x - centre.x;
                        var dz = front.Door.z - centre.y;
                        var away = dx * dx + dz * dz;
                        if (away >= nearest)
                            continue;
                        nearest = away;
                        best = front.GangId;
                    }
                    district.Gang = best;
                    district.Contested = false;
                    continue;
                }

                var leader = -1;
                var lead = 0;
                var tied = false;
                foreach (var pair in _tally)
                {
                    if (pair.Value > lead)
                    {
                        lead = pair.Value;
                        leader = pair.Key;
                        tied = false;
                    }
                    else if (pair.Value == lead)
                    {
                        tied = true;
                    }
                }

                district.Gang = tied ? -1 : leader;
                district.Contested = tied;
            }

            Version++;
            return true;
        }

        public void Invalidate() => Version++;

        // -------------------------------------------------------------------- tint

        /// <summary>
        /// The whole overlay: one flat rectangle of the family's colour per district,
        /// MULTIPLIED into the map. Nothing else.
        ///
        /// An earlier revision striped it, cross-hatched it, ran marching ants round it
        /// and put a corner tag in it. Over a raster this dense that read as noise laid
        /// on noise, and none of it is coming back. Multiply is what makes the flat
        /// version work: streets, buildings and terrain stay completely legible
        /// underneath, merely tinted - an alpha wash would grey the map out toward the
        /// colour instead of tinting what is there.
        ///
        /// And it stops at the water. The tint is laid scanline by scanline, skipping
        /// every pixel the map classified as water (<see cref="MapBase.IsWaterAt"/>),
        /// because a port district tinting its own harbour turns the one part of the map
        /// with a distinct colour into a coloured puddle - and a riverside district
        /// paints straight over the river.
        ///
        /// Unclaimed ground is not tinted at all. Nobody holds it, so nothing is said.
        /// </summary>
        public void Tint(MapRaster into, MapSheet sheet)
        {
            foreach (var district in _all)
            {
                if (!district.Contested && district.Gang < 0)
                    continue;   // unclaimed: draw nothing
                if (!sheet.Sees(district.World))
                    continue;

                var colour = district.Contested
                    ? MapPalette.ContestedTint
                    : MapPalette.Gang(district.Gang);

                var box = sheet.RealBox(district.World);
                var x0 = Mathf.Max(0, box.xMin);
                var x1 = Mathf.Min(MapRaster.W, box.xMax);
                var y0 = Mathf.Max(0, box.yMin);
                var y1 = Mathf.Min(MapRaster.H, box.yMax);

                for (var y = y0; y < y1; y++)
                {
                    var ay = y / MapRaster.S;
                    var run = -1;
                    for (var x = x0; x <= x1; x++)
                    {
                        var wet = x == x1 || MapBase.IsWaterAt(x / MapRaster.S, ay);
                        if (!wet)
                        {
                            if (run < 0)
                                run = x;
                            continue;
                        }
                        if (run < 0)
                            continue;
                        into.MultiplyRun(run, y, x - run, colour);
                        run = -1;
                    }
                }
            }
        }

        /// <summary>Bumped whenever the tint would come out different, so the map knows
        /// when to re-lay the ground it is multiplied into.</summary>
        public int Version { get; private set; }
    }
}
