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
    /// rides the camera, so a pan or a turn of the wheel is what puts it out of date.
    ///
    /// THE AUTHORED / REAL SPLIT. Structure is laid out in authored units (320x200) and
    /// multiplied up; dither, kerbs, lane markings and the shoreline are drawn at the
    /// full 960x600. That is what keeps the city's blocking chunky while its detail
    /// stays fine, and it is why the terrain is CLASSIFIED coarse and PAINTED fine: the
    /// heightfield is sampled once per two authored units - sixteen thousand samples, as
    /// before - and the dither that textures it is laid a real pixel at a time over the
    /// top. The expensive half did not get nine times more expensive; only the cheap
    /// half did.
    ///
    /// The classification survives the bake on purpose. <see cref="IsWaterAt"/> is how
    /// the turf overlay knows to leave the harbour and the river alone, and it has to be
    /// the same answer the map was drawn from, not a second guess at it.
    /// </summary>
    public static class MapBase
    {
        /// <summary>How wide a road has to come out, in real pixels, before it is worth
        /// painting a line down the middle of it.</summary>
        const float PaintablePx = 7f;

        /// <summary>Trees and waves are scattered on a fixed grid IN THE WORLD and not
        /// on the sheet, so a wood stays where it is while the map is panned over it.
        /// Finer than the old sheet's: the detail passes carry about two and a half
        /// times the density now that there are real pixels to put it in.</summary>
        const float TreePitch = 16f;
        const float WavePitch = 60f;

        /// <summary>The terrain classification, one byte per AUTHORED unit.</summary>
        static readonly byte[] Kind = new byte[MapRaster.AW * MapRaster.AH];

        const byte Sea = 0, Beach = 1, Land = 2, Town = 3;

        public static void Bake(MapRaster into, MapSheet sheet, RoadDemoBuilder builder,
            Rect grid)
        {
            into.Clear(MapPalette.Void);
            if (builder == null)
                return;

            Classify(sheet, builder, grid);
            Paint(into);
            Ground(into, sheet, builder, grid);
            Scatter(into, sheet);
            Roads(into, sheet, builder);
            Airfield(into, sheet);
        }

        /// <summary>
        /// Whether an authored unit of the sheet is water, as the map itself decided.
        /// The turf overlay asks this per pixel so a district's tint stops at the water
        /// line - without it a port district turns the harbour into a coloured puddle
        /// and a riverside one paints straight over the river.
        /// </summary>
        public static bool IsWaterAt(int authoredX, int authoredY) =>
            (uint)authoredX < MapRaster.AW && (uint)authoredY < MapRaster.AH &&
            Kind[authoredY * MapRaster.AW + authoredX] == Sea;

        // ----------------------------------------------------------------- terrain

        static void Classify(MapSheet sheet, RoadDemoBuilder builder, Rect grid)
        {
            var island = builder.IslandArea;
            var hasIsland = island.width > 1f && island.height > 1f;
            var sea = RoadDemoBuilder.WaterY;
            var beach = RoadDemoBuilder.BeachLine;

            for (var ay = 0; ay < MapRaster.AH; ay += 2)
            {
                for (var ax = 0; ax < MapRaster.AW; ax += 2)
                {
                    var world = sheet.ToWorld(new Vector2(ax + 1f, ay + 1f));

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

                    for (var dy = 0; dy < 2 && ay + dy < MapRaster.AH; dy++)
                        for (var dx = 0; dx < 2 && ax + dx < MapRaster.AW; dx++)
                            Kind[(ay + dy) * MapRaster.AW + ax + dx] = kind;
                }
            }
        }

        /// <summary>
        /// The classification blown up into real pixels, dithered a real pixel at a
        /// time, with the shoreline traced in the same sweep - one pass over the buffer
        /// rather than three.
        /// </summary>
        static void Paint(MapRaster into)
        {
            var pixels = into.Pixels;
            for (var y = 0; y < MapRaster.H; y++)
            {
                var row = y * MapRaster.W;
                var ay = y / MapRaster.S;
                var authored = ay * MapRaster.AW;
                for (var x = 0; x < MapRaster.W; x++)
                {
                    var kind = Kind[authored + x / MapRaster.S];

                    // Land against water is a single real pixel of sand: the coast is
                    // the one edge on this map worth drawing at full resolution.
                    if (kind != Sea && (Wet(x - 1, y) || Wet(x + 1, y) ||
                                        Wet(x, y - 1) || Wet(x, y + 1)))
                    {
                        pixels[row + x] = MapPalette.Sand;
                        continue;
                    }

                    var checkers = (x >> 1) + (y >> 1);
                    switch (kind)
                    {
                        case Sea:
                            pixels[row + x] = checkers % 5 == 0
                                ? MapPalette.WaterDeep : MapPalette.Water;
                            break;
                        case Beach:
                            pixels[row + x] = MapPalette.Sand;
                            break;
                        default:
                            pixels[row + x] = checkers % 4 == 0
                                ? MapPalette.Grass2 : MapPalette.Grass;
                            break;
                    }
                }
            }
        }

        /// <summary>Whether the authored cell under a REAL pixel is water.</summary>
        static bool Wet(int x, int y) =>
            (uint)x < MapRaster.W && (uint)y < MapRaster.H &&
            Kind[y / MapRaster.S * MapRaster.AW + x / MapRaster.S] == Sea;

        static void Wettest(RectInt real)
        {
            // Mark real-pixel water back into the authored classification, so the turf
            // overlay leaves it alone. A river inside the town and a harbour basin are
            // both drawn AFTER the coast is classified and neither is on the coast.
            var x0 = Mathf.Max(0, real.xMin / MapRaster.S);
            var y0 = Mathf.Max(0, real.yMin / MapRaster.S);
            var x1 = Mathf.Min(MapRaster.AW, (real.xMax + MapRaster.S - 1) / MapRaster.S);
            var y1 = Mathf.Min(MapRaster.AH, (real.yMax + MapRaster.S - 1) / MapRaster.S);
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                    Kind[y * MapRaster.AW + x] = Sea;
        }

        // ------------------------------------------------------------------ ground

        static void Ground(MapRaster into, MapSheet sheet, RoadDemoBuilder builder,
            Rect grid)
        {
            // The town's own ground is street: the blocks are laid over it in a moment
            // and what is left between them IS the road network, which is how the plan
            // gets every alley without having to enumerate one.
            if (sheet.Sees(grid))
                into.Fill(sheet.RealBox(grid), MapPalette.Road);

            foreach (var seam in builder.SeamPlans)
            {
                if (!sheet.Sees(seam.Area))
                    continue;
                var box = sheet.RealBox(seam.Area);
                switch (seam.Kind)
                {
                    case SeamKind.River:
                        Dither(into, box, MapPalette.Water, MapPalette.WaterDeep, 5);
                        Wettest(box);
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
                var driftX = Mathf.RoundToInt(sheet.Centre.x * sheet.RealPerMetre);
                var driftY = Mathf.RoundToInt(sheet.Centre.y * sheet.RealPerMetre);
                foreach (var paved in reserved.Paved)
                {
                    if (!sheet.Sees(paved))
                        continue;
                    var box = sheet.RealBox(paved);
                    into.Fill(box, MapPalette.Concrete);
                    Speckle(into, box, MapPalette.Concrete2, driftX, driftY);
                }
                foreach (var water in reserved.Water)
                {
                    if (!sheet.Sees(water))
                        continue;
                    var box = sheet.RealBox(water);
                    Dither(into, box, MapPalette.Water, MapPalette.WaterDeep, 5);
                    Wettest(box);
                }
            }

            foreach (var yard in builder.MergedYards)
                if (sheet.Sees(yard))
                    into.Fill(sheet.RealBox(yard), MapPalette.Concrete);

            foreach (var lot in builder.LotPlans)
            {
                if (!sheet.Sees(lot.Slab))
                    continue;
                if (lot.Green)
                {
                    Dither(into, sheet.RealBox(lot.Slab), MapPalette.Grass, MapPalette.Grass2, 4);
                    continue;
                }
                // The kerb ring the eye reads as the edge of a block, and the yard
                // inside it a shade down - the two together are what makes a city of
                // blocks read as blocks and not as one grey field.
                into.Fill(sheet.RealBox(lot.Slab), MapPalette.Concrete);
                into.Fill(sheet.RealBox(lot.Interior), MapPalette.Concrete2);
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

        /// <summary>Concrete's own texture: a sparse speckle hashed off the pixel's
        /// place IN THE WORLD, so a pad does not crawl when the map is panned.</summary>
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
                    if (Hash(x + driftX, y - driftY, 20261) % 11 == 0)
                        pixels[row + x] = colour;
            }
        }

        // ---------------------------------------------------------------- scatter

        static void Scatter(MapRaster into, MapSheet sheet)
        {
            var window = sheet.Margin(4f);

            var x0 = Mathf.FloorToInt(window.xMin / TreePitch);
            var x1 = Mathf.CeilToInt(window.xMax / TreePitch);
            var z0 = Mathf.FloorToInt(window.yMin / TreePitch);
            var z1 = Mathf.CeilToInt(window.yMax / TreePitch);

            // The wood thins out as the map is walked into: what is drawn is a stand of
            // trees, not every trunk.
            var step = Mathf.Max(1, Mathf.RoundToInt(2f / Mathf.Max(0.3f, sheet.Metres)));

            if ((long)(x1 - x0) * (z1 - z0) < 900000L)
            {
                for (var iz = z0; iz <= z1; iz += step)
                {
                    for (var ix = x0; ix <= x1; ix += step)
                    {
                        if (Hash(ix, iz, 7919) % 5 != 0)
                            continue;
                        var world = new Vector2(
                            ix * TreePitch + Hash(ix, iz, 104729) % 11,
                            iz * TreePitch + Hash(ix, iz, 15485863) % 11);
                        var at = sheet.ToReal(world);
                        var px = Mathf.FloorToInt(at.x);
                        var py = Mathf.FloorToInt(at.y);
                        if ((uint)px >= MapRaster.W || (uint)py >= MapRaster.H)
                            continue;
                        if (Kind[py / MapRaster.S * MapRaster.AW + px / MapRaster.S] != Land)
                            continue;
                        into.Fill(px, py, 2, 2, MapPalette.Tree);
                    }
                }
            }

            var wx0 = Mathf.FloorToInt(window.xMin / WavePitch);
            var wx1 = Mathf.CeilToInt(window.xMax / WavePitch);
            var wz0 = Mathf.FloorToInt(window.yMin / WavePitch);
            var wz1 = Mathf.CeilToInt(window.yMax / WavePitch);
            if ((long)(wx1 - wx0) * (wz1 - wz0) >= 900000L)
                return;

            for (var iz = wz0; iz <= wz1; iz++)
            {
                for (var ix = wx0; ix <= wx1; ix++)
                {
                    if (Hash(ix, iz, 6151) % 3 != 0)
                        continue;
                    var at = sheet.ToReal(new Vector2(ix * WavePitch, iz * WavePitch));
                    var px = Mathf.FloorToInt(at.x);
                    var py = Mathf.FloorToInt(at.y);
                    if ((uint)px >= MapRaster.W || (uint)py >= MapRaster.H)
                        continue;
                    if (!Wet(px, py) || !Wet(px + 2, py))
                        continue;
                    into.Fill(px, py, 3, 1, MapPalette.Wave);
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

        // ------------------------------------------------------------------- roads

        static readonly List<(Vector2 a, Vector2 b)> Chords = new List<(Vector2, Vector2)>();

        static void Roads(MapRaster into, MapSheet sheet, RoadDemoBuilder builder)
        {
            var perMetre = sheet.RealPerMetre;
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
                        var pa = sheet.ToReal(a);
                        var pb = sheet.ToReal(b);
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
                var pa = sheet.ToReal(a);
                var pb = sheet.ToReal(b);
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

        /// <summary>A carriageway as straight chords. The motorway is cut into stretches
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

        static readonly List<(Rect world, Color32 fill, Color32 line)> Aprons =
            new List<(Rect, Color32, Color32)>();

        static bool _looked;

        /// <summary>
        /// A runway is the most legible thing in a city seen from the air and the only
        /// piece of a quarter's own geometry this map draws by hand. Nothing reports it:
        /// the airfield paves nothing - it only asks the island to hold its ground level
        /// - so it is absent from the reservations, and it carries no collider so it is
        /// absent from the buildings. So it is found the way runtime identity has always
        /// been found in this project, by the name the thing was built under.
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
                var box = sheet.RealBox(world);
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
                if (box.height < 5)
                    return;
                into.HDash(box.xMin, box.yMin + box.height / 2, box.width, 2, 5, colour);
            }
            else
            {
                if (box.width < 5)
                    return;
                into.VDash(box.xMin + box.width / 2, box.yMin, box.height, 2, 5, colour);
            }
        }
    }
}
