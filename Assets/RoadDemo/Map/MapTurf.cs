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

        const float WashAlpha = 0.30f;
        const float StripeAlpha = 0.55f;
        const float ContestedWashAlpha = 0.20f;
        const float ContestedStripeAlpha = 0.50f;

        /// <summary>Stripe pitch, in pixels, exactly as the sheet draws it.</summary>
        const int StripePitch = 6;

        readonly List<MapDistrict> _all = new List<MapDistrict>();
        readonly MapRaster _layer = new MapRaster();
        readonly Dictionary<int, int> _tally = new Dictionary<int, int>();

        MapSheet _baked;
        bool _dirty = true;
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

            _dirty = true;
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

            _dirty = true;
            return true;
        }

        public void Invalidate() => _dirty = true;

        // --------------------------------------------------------------------- bake

        /// <summary>The wash, striped and tagged, ready to be laid over the base.</summary>
        public MapRaster Layer(MapSheet sheet)
        {
            if (!_dirty && _baked.Matches(sheet))
                return _layer;

            _dirty = false;
            _baked = sheet;
            _layer.Clear(new Color32(0, 0, 0, 0));

            foreach (var district in _all)
            {
                if (!sheet.Sees(district.World))
                    continue;
                var box = sheet.Box(district.World);
                if (box.width < 3 || box.height < 3)
                    continue;

                var contested = district.Contested;
                var wash = contested ? MapPalette.ContestedWash : MapPalette.Gang(district.Gang);
                var stripe = contested ? MapPalette.Contested : MapPalette.Gang(district.Gang);

                _layer.LayerWash(box.xMin, box.yMin, box.width, box.height, wash,
                    contested ? ContestedWashAlpha : WashAlpha);

                var strength = contested ? ContestedStripeAlpha : StripeAlpha;
                Stripes(box, stripe, strength, contested ? 1 : MapPalette.StripeLean(district.Gang));
                if (contested)
                    Stripes(box, stripe, strength, -1);

                Tag(box, contested ? MapPalette.Contested : MapPalette.Gang(district.Gang));
            }

            return _layer;
        }

        /// <summary>Diagonals a pixel wide every six, leaning the way the family leans.
        /// Two families whose colours sit near each other on the wheel still read apart
        /// because their ground is combed in opposite directions.</summary>
        void Stripes(RectInt box, Color32 colour, float alpha, int lean)
        {
            for (var i = -box.height; i < box.width + box.height; i += StripePitch)
            {
                for (var s = 0; s < box.height; s++)
                {
                    var x = box.xMin + i + (lean > 0 ? s : box.height - s);
                    if (x < box.xMin || x >= box.xMax)
                        continue;
                    _layer.LayerPx(x, box.yMin + s, colour, alpha);
                }
            }
        }

        /// <summary>The ownership tag in the district's top-left corner: a black chip
        /// with the family's colour inside it, so a district that is mostly under
        /// buildings still says whose it is.</summary>
        void Tag(RectInt box, Color32 colour)
        {
            if (box.width < 12 || box.height < 10)
                return;
            _layer.LayerFill(box.xMin + 2, box.yMin + 2, 7, 5, MapPalette.Hex(0x0b0d0c));
            _layer.LayerFill(box.xMin + 3, box.yMin + 3, 5, 3, colour);
        }

        // ------------------------------------------------------------------ borders

        /// <summary>
        /// The hard border, drawn live over everything the turf layer put down.
        ///
        /// The design sheet crawls a contested border - marching ants, the dash phase
        /// pushed round with the clock. It was built that way and it was wrong at this
        /// size: the sheet is blown up six times over a map that is itself the screen,
        /// and four pixels of dash travelling round a district becomes a band of
        /// glitter that drags the eye off everything else on the map. Contested ground
        /// is already saying so - it is the only ground cross-hatched in both
        /// directions, in bone white, with a bone-white tag.
        ///
        /// So nothing marches. A held border is a solid rule in the family's colour; a
        /// contested one is a solid rule that BREATHES, brightening and dimming on a
        /// slow cycle. It reads as unsettled from across the room and it does not
        /// shimmer when you look at it.
        /// </summary>
        public void DrawBorders(MapRaster into, MapSheet sheet, float time)
        {
            // A slow swell, not a blink: about a second and a half, eased, so the eye
            // registers it as movement without ever catching a frame of it.
            var breath = (Mathf.Sin(time * 4.2f) + 1f) * 0.5f;
            var pulse = Color32.Lerp(MapPalette.ContestedWash, MapPalette.Contested, breath);

            foreach (var district in _all)
            {
                if (!sheet.Sees(district.World))
                    continue;
                var box = sheet.Box(district.World);
                if (box.width < 6 || box.height < 6)
                    continue;
                var colour = district.Contested ? pulse : MapPalette.Gang(district.Gang);
                into.Frame(box.xMin, box.yMin, box.width, box.height, 2, colour);
            }
        }
    }
}
