using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The static half of the map: everything that is ground rather than news. Terrain,
    /// water, the shoreline, trees, the town's streets and their markings, the block
    /// slabs, the quarters' paved yards and basins, the expressway deck, the airfield.
    ///
    /// It is rasterised into a buffer of its own and then blitted, which is the design
    /// sheet's first performance rule and the reason a frame of this map costs almost
    /// nothing. What makes the bake happen at all is the framing changing - the map
    /// rides the camera, so a pan or a turn of the wheel is what puts it out of date,
    /// and <see cref="MapSheet.Matches"/> is what decides.
    ///
    /// Two things are worth knowing about how it draws.
    ///
    /// The terrain is classified at HALF resolution and painted in 2x2 blocks. The
    /// island is a heightfield and sampling it is the one expensive thing on this map -
    /// sixty-four thousand samples a bake, on every frame of a zoom, is a stutter you
    /// can feel. At half resolution it is sixteen thousand, and nothing is lost:
    /// the land is a one-pixel dither over the top of it, so the block is invisible,
    /// and the shoreline is traced afterwards on the FULL resolution classification, so
    /// the coast still comes out a single pixel wide.
    ///
    /// The markings are gated on how wide their road actually comes out. A yellow line
    /// down a street that is one pixel across is not a marking, it is the street being
    /// painted yellow - so the paint only goes on once there is a road under it wide
    /// enough to carry it, which happens naturally as the wheel comes in.
    /// </summary>
    public static class MapBase
    {
        /// <summary>How wide a road has to come out before it is worth painting a line
        /// down the middle of it.</summary>
        const float PaintablePx = 5f;

        /// <summary>Trees are scattered on a fixed grid IN THE WORLD and not on the
        /// sheet, so a wood stays where it is while the map is panned over it.</summary>
        const float TreePitch = 26f;
        const float WavePitch = 90f;

        static readonly byte[] Kind = new byte[MapRaster.W * MapRaster.H];

        const byte Sea = 0, Beach = 1, Land = 2, Town = 3;

        public static void Bake(MapRaster into, MapSheet sheet, RoadDemoBuilder builder,
            Rect grid)
        {
            into.Clear(MapPalette.Void);
            if (builder == null)
                return;

            Terrain(into, sheet, builder, grid);
            Shore(into, sheet);
            Scatter(into, sheet);
            Ground(into, sheet, builder, grid);
            Roads(into, sheet, builder);
            Airfield(into, sheet);
        }

        // ----------------------------------------------------------------- terrain

        static void Terrain(MapRaster into, MapSheet sheet, RoadDemoBuilder builder,
            Rect grid)
        {
            var island = builder.IslandArea;
            var hasIsland = island.width > 1f && island.height > 1f;
            var sea = RoadDemoBuilder.WaterY;
            var beach = RoadDemoBuilder.BeachLine;
            var pixels = into.Pixels;

            for (var hy = 0; hy < MapRaster.H; hy += 2)
            {
                for (var hx = 0; hx < MapRaster.W; hx += 2)
                {
                    var world = sheet.ToWorld(new Vector2(hx + 1f, hy + 1f));

                    byte kind;
                    if (grid.Contains(world))
                    {
                        // The town lays its own ground over this in a moment; classifying
                        // it costs a heightfield sample for a pixel nobody will see.
                        kind = Town;
                    }
                    else if (!hasIsland)
                    {
                        kind = Land;
                    }
                    else
                    {
                        var height = builder.LandHeight(world.x, world.y);
                        kind = height < sea ? Sea : height < beach ? Beach : Land;
                    }

                    for (var dy = 0; dy < 2; dy++)
                    {
                        var py = hy + dy;
                        if (py >= MapRaster.H)
                            break;
                        var row = py * MapRaster.W;
                        for (var dx = 0; dx < 2; dx++)
                        {
                            var px = hx + dx;
                            if (px >= MapRaster.W)
                                break;
                            Kind[row + px] = kind;
                            pixels[row + px] = Paint(kind, px, py);
                        }
                    }
                }
            }
        }

        /// <summary>The dither the whole map is textured with - never a gradient, and
        /// the same test the design sheet names.</summary>
        static Color32 Paint(byte kind, int x, int y)
        {
            var checkers = (x >> 1) + (y >> 1);
            switch (kind)
            {
                case Sea: return checkers % 5 == 0 ? MapPalette.WaterDeep : MapPalette.Water;
                case Beach: return MapPalette.Sand;
                default: return checkers % 4 == 0 ? MapPalette.Grass2 : MapPalette.Grass;
            }
        }

        /// <summary>One pixel of sand wherever the land meets the water. Traced on the
        /// full-resolution classification, so the coast is a line and not a stair.</summary>
        static void Shore(MapRaster into, MapSheet sheet)
        {
            var pixels = into.Pixels;
            for (var y = 0; y < MapRaster.H; y++)
            {
                var row = y * MapRaster.W;
                for (var x = 0; x < MapRaster.W; x++)
                {
                    if (Kind[row + x] == Sea)
                        continue;
                    if (!Wet(x - 1, y) && !Wet(x + 1, y) && !Wet(x, y - 1) && !Wet(x, y + 1))
                        continue;
                    pixels[row + x] = MapPalette.Sand;
                }
            }
        }

        static bool Wet(int x, int y) =>
            (uint)x < MapRaster.W && (uint)y < MapRaster.H &&
            Kind[y * MapRaster.W + x] == Sea;

        /// <summary>Trees on the land, wave ticks on the water. Both are hashed off a
        /// fixed world grid, never off the pixel, so they do not crawl under a pan.</summary>
        static void Scatter(MapRaster into, MapSheet sheet)
        {
            var window = sheet.Margin(4f);

            var x0 = Mathf.FloorToInt(window.xMin / TreePitch);
            var x1 = Mathf.CeilToInt(window.xMax / TreePitch);
            var z0 = Mathf.FloorToInt(window.yMin / TreePitch);
            var z1 = Mathf.CeilToInt(window.yMax / TreePitch);

            // A pixel of tree per twenty-six metres of country would be a forest at one
            // metre to the pixel and a smear at eleven. So the wood thins out as the map
            // is walked into: what is drawn is a stand of trees, not every trunk.
            var step = Mathf.Max(1, Mathf.RoundToInt(3f / Mathf.Max(0.3f, sheet.Metres)));

            if ((long)(x1 - x0) * (z1 - z0) < 400000L)
            {
                for (var iz = z0; iz <= z1; iz += step)
                {
                    for (var ix = x0; ix <= x1; ix += step)
                    {
                        if (Hash(ix, iz, 7919) % 5 != 0)
                            continue;
                        var world = new Vector2(
                            ix * TreePitch + Hash(ix, iz, 104729) % 17,
                            iz * TreePitch + Hash(ix, iz, 15485863) % 17);
                        var at = sheet.ToPx(world);
                        var px = Mathf.FloorToInt(at.x);
                        var py = Mathf.FloorToInt(at.y);
                        if ((uint)px >= MapRaster.W || (uint)py >= MapRaster.H)
                            continue;
                        if (Kind[py * MapRaster.W + px] != Land)
                            continue;
                        into.Fill(px, py, 2, 2, MapPalette.Tree);
                    }
                }
            }

            var wx0 = Mathf.FloorToInt(window.xMin / WavePitch);
            var wx1 = Mathf.CeilToInt(window.xMax / WavePitch);
            var wz0 = Mathf.FloorToInt(window.yMin / WavePitch);
            var wz1 = Mathf.CeilToInt(window.yMax / WavePitch);
            if ((long)(wx1 - wx0) * (wz1 - wz0) >= 400000L)
                return;

            for (var iz = wz0; iz <= wz1; iz++)
            {
                for (var ix = wx0; ix <= wx1; ix++)
                {
                    if (Hash(ix, iz, 6151) % 3 != 0)
                        continue;
                    var at = sheet.ToPx(new Vector2(ix * WavePitch, iz * WavePitch));
                    var px = Mathf.FloorToInt(at.x);
                    var py = Mathf.FloorToInt(at.y);
                    if ((uint)px >= MapRaster.W || (uint)py >= MapRaster.H)
                        continue;
                    if (Kind[py * MapRaster.W + px] != Sea || !Wet(px + 1, py))
                        continue;
                    into.Fill(px, py, 2, 1, MapPalette.Wave);
                }
            }
        }

        static int Hash(int x, int y, int salt)
        {
            unchecked
            {
                var h = x * 374761393 + y * 668265263 + salt;
                h = (h ^ (h >> 13)) * 1274126177;
                return (h ^ (h >> 16)) & 0x7fffffff;
            }
        }

        // ------------------------------------------------------------------ ground

        static void Ground(MapRaster into, MapSheet sheet, RoadDemoBuilder builder,
            Rect grid)
        {
            // The town's own ground is street: the blocks are laid over it in a moment
            // and what is left between them IS the road network, which is how the plan
            // gets every alley without having to enumerate one.
            if (sheet.Sees(grid))
                into.Fill(sheet.Box(grid), MapPalette.Road);

            foreach (var seam in builder.SeamPlans)
            {
                if (!sheet.Sees(seam.Area))
                    continue;
                var box = sheet.Box(seam.Area);
                switch (seam.Kind)
                {
                    case SeamKind.River:
                        Dither(into, box, MapPalette.Water, MapPalette.WaterDeep, 5);
                        break;
                    case SeamKind.Highway:
                        into.Fill(box, MapPalette.RoadDark);
                        break;
                    default:
                        Dither(into, box, MapPalette.Grass, MapPalette.Grass2, 4);
                        break;
                }
            }

            // What the quarters had the island keep for them: a port's paved yard and
            // the basin it has to be sailed into. Nobody else can report either - a
            // basin is not a seam, not a block and not a footprint.
            var reserved = builder.Reservations;
            if (reserved != null)
            {
                // The speckle is hashed off the pixel's place IN THE WORLD, like the
                // trees and the wave ticks: hashed off the sheet it would crawl over the
                // ground every time the map was panned.
                var driftX = Mathf.RoundToInt(sheet.Centre.x / sheet.Metres);
                var driftY = Mathf.RoundToInt(sheet.Centre.y / sheet.Metres);
                foreach (var paved in reserved.Paved)
                {
                    if (!sheet.Sees(paved))
                        continue;
                    var box = sheet.Box(paved);
                    into.Fill(box, MapPalette.Concrete);
                    Speckle(into, box, MapPalette.Concrete2, driftX, driftY);
                }
                foreach (var water in reserved.Water)
                {
                    if (!sheet.Sees(water))
                        continue;
                    Dither(into, sheet.Box(water), MapPalette.Water, MapPalette.WaterDeep, 5);
                }
            }

            foreach (var yard in builder.MergedYards)
                if (sheet.Sees(yard))
                    into.Fill(sheet.Box(yard), MapPalette.Concrete);

            foreach (var lot in builder.LotPlans)
            {
                if (!sheet.Sees(lot.Slab))
                    continue;
                if (lot.Green)
                {
                    Dither(into, sheet.Box(lot.Slab), MapPalette.Grass, MapPalette.Grass2, 4);
                    continue;
                }
                // The kerb ring the eye reads as the edge of a block, and the yard
                // inside it a shade down - the two together are what makes a city of
                // blocks read as blocks and not as one grey field.
                into.Fill(sheet.Box(lot.Slab), MapPalette.Concrete);
                into.Fill(sheet.Box(lot.Interior), MapPalette.Concrete2);
            }
        }

        static void Dither(MapRaster into, RectInt box, Color32 a, Color32 b, int every)
        {
            var x0 = Mathf.Max(0, box.xMin);
            var y0 = Mathf.Max(0, box.yMin);
            var x1 = Mathf.Min(MapRaster.W, box.xMax);
            var y1 = Mathf.Min(MapRaster.H, box.yMax);
            var pixels = into.Pixels;
            for (var y = y0; y < y1; y++)
            {
                var row = y * MapRaster.W;
                for (var x = x0; x < x1; x++)
                    pixels[row + x] = ((x >> 1) + (y >> 1)) % every == 0 ? b : a;
            }
        }

        /// <summary>Concrete's own texture: a sparse speckle, on the same fixed pattern
        /// so a pad does not shimmer when the map moves.</summary>
        static void Speckle(MapRaster into, RectInt box, Color32 colour, int driftX, int driftY)
        {
            var x0 = Mathf.Max(0, box.xMin);
            var y0 = Mathf.Max(0, box.yMin);
            var x1 = Mathf.Min(MapRaster.W, box.xMax);
            var y1 = Mathf.Min(MapRaster.H, box.yMax);
            var pixels = into.Pixels;
            for (var y = y0; y < y1; y++)
            {
                var row = y * MapRaster.W;
                for (var x = x0; x < x1; x++)
                    if (Hash(x + driftX, y - driftY, 20261) % 9 == 0)
                        pixels[row + x] = colour;
            }
        }

        // ------------------------------------------------------------------- roads

        static readonly List<(Vector2 a, Vector2 b)> Chords =
            new List<(Vector2, Vector2)>();

        static void Roads(MapRaster into, MapSheet sheet, RoadDemoBuilder builder)
        {
            var perMetre = sheet.PixelsPerMetre;
            var net = builder.Net;
            if (net != null)
            {
                foreach (var road in net.Roads)
                {
                    if (road == null)
                        continue;
                    var deck = road.Class == RoadClass.Freeway || road.Class == RoadClass.Ramp;
                    var half = Mathf.Max(3f, road.HalfRoad) * perMetre;
                    Split(road, deck ? 3f : 0f);
                    foreach (var (a, b) in Chords)
                    {
                        var pa = sheet.ToPx(a);
                        var pb = sheet.ToPx(b);
                        if (Offsheet(pa, pb, half))
                            continue;
                        into.Strip(pa, pb, half, deck ? MapPalette.RoadDark : MapPalette.Road);
                        if (half * 2f >= PaintablePx)
                            into.StripDash(pa, pb, 2, 5,
                                deck ? MapPalette.Line : MapPalette.Yellow);
                    }
                }
            }

            foreach (var (a, b, half) in builder.QuarterRoads)
            {
                var pa = sheet.ToPx(a);
                var pb = sheet.ToPx(b);
                var wide = Mathf.Max(3f, half) * perMetre;
                if (Offsheet(pa, pb, wide))
                    continue;
                into.Strip(pa, pb, wide, MapPalette.Road);
                if (wide * 2f >= PaintablePx)
                    into.StripDash(pa, pb, 2, 5, MapPalette.Yellow);
            }
        }

        static bool Offsheet(Vector2 a, Vector2 b, float half)
        {
            var pad = half + 2f;
            return (a.x < -pad && b.x < -pad) || (a.x > MapRaster.W + pad && b.x > MapRaster.W + pad) ||
                   (a.y < -pad && b.y < -pad) || (a.y > MapRaster.H + pad && b.y > MapRaster.H + pad);
        }

        /// <summary>A carriageway as straight chords. The motorway is laid in stretches
        /// with a few metres of junction between each pair that no carriageway covers,
        /// so its chords are run on at both ends and the seam closes.</summary>
        static void Split(Carriageway road, float pad)
        {
            Chords.Clear();
            if (road.Path != null && road.Path.Pts.Length > 2)
            {
                var pts = road.Path.Pts;
                var from = pts[0];
                for (var i = 1; i < pts.Length; i++)
                {
                    if (i < pts.Length - 1 && (pts[i] - from).sqrMagnitude < 400f)
                        continue;
                    Chords.Add((new Vector2(from.x, from.z), new Vector2(pts[i].x, pts[i].z)));
                    from = pts[i];
                }
            }
            if (Chords.Count == 0)
                Chords.Add((new Vector2(road.A.x, road.A.z), new Vector2(road.B.x, road.B.z)));
            if (pad <= 0f)
                return;

            var head = Chords[0];
            var d0 = (head.b - head.a).normalized;
            Chords[0] = (head.a - d0 * pad, head.b);
            var tail = Chords[Chords.Count - 1];
            var d1 = (tail.b - tail.a).normalized;
            Chords[Chords.Count - 1] = (tail.a, tail.b + d1 * pad);
        }

        // ---------------------------------------------------------------- the field

        /// <summary>The airfield's own pavement, once it has been found.</summary>
        static readonly List<(Rect world, Color32 fill, Color32 line)> Aprons =
            new List<(Rect, Color32, Color32)>();

        static bool _looked;

        /// <summary>
        /// A runway is the most legible thing in a city seen from the air and the only
        /// piece of a quarter's own geometry this map draws by hand. Nothing reports it:
        /// the airfield paves nothing (it only asks the island to hold its ground level)
        /// so it is absent from the reservations, and it carries no collider so it is
        /// absent from the buildings.
        ///
        /// So it is found the way runtime block identity has always been found in this
        /// project - by the name the thing was built under - and measured off its own
        /// renderer. Found once; if the field is not in this city, or its pieces were
        /// merged away under other names, nothing is drawn and nothing is guessed.
        /// </summary>
        public static void Look()
        {
            _looked = true;
            Aprons.Clear();
            Apron("Runway", MapPalette.RoadDark, MapPalette.Line);
            Apron("Taxiway", MapPalette.Road, MapPalette.Yellow);
            Apron("Ramp", MapPalette.Concrete, MapPalette.Concrete);
        }

        static void Apron(string name, Color32 fill, Color32 line)
        {
            var found = GameObject.Find(name);
            if (found == null)
                return;
            var renderers = found.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            Aprons.Add((Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z),
                fill, line));
        }

        static void Airfield(MapRaster into, MapSheet sheet)
        {
            if (!_looked)
                Look();

            foreach (var (world, fill, line) in Aprons)
            {
                if (!sheet.Sees(world))
                    continue;
                var box = sheet.Box(world);
                into.Fill(box, fill);
                Centreline(into, box, line);
            }
        }

        /// <summary>A dashed line down the long axis of a box, 2 on and 3 off - only
        /// once the box is wide enough across to carry one.</summary>
        static void Centreline(MapRaster into, RectInt box, Color32 colour)
        {
            if (box.width >= box.height)
            {
                if (box.height < 3)
                    return;
                into.HDash(box.xMin, box.yMin + box.height / 2, box.width, 2, 5, colour);
            }
            else
            {
                if (box.width < 3)
                    return;
                into.VDash(box.xMin + box.width / 2, box.yMin, box.height, 2, 5, colour);
            }
        }
    }
}
