using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The two marks the file's last row carries - a house to send a crew back to the
    /// outfit, a car to put it back in one. There is no icon font in this project and
    /// no sprite for either.
    ///
    /// They are BAKED, as pixels, rather than assembled out of rotated rectangles:
    /// rotated rects were what was here, and a roof made of two thin bars pivoted about
    /// their own left ends comes out as a scribble at fourteen units tall. A tiny
    /// point-filtered raster is also the right register for this screen - the map under
    /// it is a raster plate.
    ///
    /// White pixels, tinted at the Image. That is what lets a button's mark go pale
    /// with its word when the pointer arrives, off one sprite rather than two.
    /// </summary>
    static class TurfGlyphs
    {
        const int Wide = 20, Tall = 14;

        /// <summary>
        /// Baked once and kept. There is deliberately no reset at play: these two are
        /// constant pixels and nothing about a new session can make them wrong, so a
        /// pair that survived a domain reload is a pair worth keeping. What the reset
        /// WOULD do is drop the reference and leave the native texture behind.
        ///
        /// The tests below are Unity's null, not C#'s, so a sprite the editor did unload
        /// between sessions is baked again rather than handed on as a dead reference.
        /// </summary>
        static Sprite _house, _car, _arrow, _back, _combat, _chat, _death, _plus;

        public static Sprite Arrow => _arrow != null ? _arrow :
            (_arrow = Pattern("Move", "...#...", "...##..", "#######", "...##..", "...#..."));
        public static Sprite Back => _back != null ? _back :
            (_back = Pattern("Return", "...#...", "..##...", "#######", "..##...", "...#..."));
        public static Sprite Combat => _combat != null ? _combat :
            (_combat = Pattern("Fight", "#.....#", ".#...#.", "..#.#..", "...#...", "..#.#..", ".#...#.", "#.....#"));
        public static Sprite Chat => _chat != null ? _chat :
            (_chat = Pattern("Talk", "#######", "#.....#", "#.#.#.#", "#.....#", "#######", ".#.....", "#......"));
        public static Sprite Death => _death != null ? _death :
            (_death = Pattern("Dead", ".#####.", "#######", "#..#..#", "#######", ".#.#.#.", ".#####.", "..#.#.."));
        public static Sprite Plus => _plus != null ? _plus :
            (_plus = Pattern("Add", "...#...", "...#...", "...#...", "#######", "...#...", "...#...", "...#..."));

        static Sprite Pattern(string name, params string[] rows) => Bake(name, pixels =>
        {
            int top = (Tall - rows.Length * 2) / 2;
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    if (rows[y][x] == '#')
                    {
                        Row(pixels, top + y * 2, 3 + x * 2, 4 + x * 2);
                        Row(pixels, top + y * 2 + 1, 3 + x * 2, 4 + x * 2);
                    }
        });

        public static Sprite House
        {
            get
            {
                if (_house == null)
                    _house = Bake("Turf Glyph House", PaintHouse);
                return _house;
            }
        }

        public static Sprite Car
        {
            get
            {
                if (_car == null)
                    _car = Bake("Turf Glyph Car", PaintCar);
                return _car;
            }
        }

        static Sprite Bake(string name, System.Action<Color32[]> paint)
        {
            var pixels = new Color32[Wide * Tall];
            paint(pixels);

            var texture = new Texture2D(Wide, Tall, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            return Sprite.Create(texture, new Rect(0f, 0f, Wide, Tall),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
        }

        /// <summary>Rows are given TOP DOWN, the way the shape reads on paper; the
        /// texture is bottom-up, so the row index is turned over here and nowhere
        /// else.</summary>
        static void Row(Color32[] pixels, int fromTop, int x0, int x1)
        {
            int y = Tall - 1 - fromTop;
            if (y < 0 || y >= Tall)
                return;
            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Wide - 1, x1); x++)
                pixels[y * Wide + x] = new Color32(255, 255, 255, 255);
        }

        static void Clear(Color32[] pixels, int fromTop, int x0, int x1)
        {
            int y = Tall - 1 - fromTop;
            if (y < 0 || y >= Tall)
                return;
            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Wide - 1, x1); x++)
                pixels[y * Wide + x] = new Color32(0, 0, 0, 0);
        }

        /// <summary>A gable roof over a body, with a door knocked out of it.</summary>
        static void PaintHouse(Color32[] pixels)
        {
            for (int r = 0; r < 6; r++)
                Row(pixels, r, 9 - r * 2, 10 + r * 2);   // the roof, widening by two a row

            for (int r = 6; r < Tall; r++)
                Row(pixels, r, 3, 16);                   // the body, under the eaves

            for (int r = 9; r < Tall; r++)
                Clear(pixels, r, 8, 11);                 // the doorway
            for (int r = 7; r <= 8; r++)
            {
                Clear(pixels, r, 5, 6);                  // two windows
                Clear(pixels, r, 13, 14);
            }
        }

        /// <summary>A saloon in profile: cabin, body, two wheels under it. Rows 2 to 11
        /// of fourteen, so the shape sits on the same middle line the house does.
        /// </summary>
        static void PaintCar(Color32[] pixels)
        {
            Row(pixels, 2, 6, 13);                       // the cabin
            Row(pixels, 3, 5, 14);
            Row(pixels, 4, 4, 15);
            for (int r = 5; r <= 8; r++)
                Row(pixels, r, 1, 18);                   // the body

            Clear(pixels, 3, 6, 8);                      // the glass
            Clear(pixels, 3, 11, 13);
            Clear(pixels, 4, 5, 8);
            Clear(pixels, 4, 11, 14);

            for (int r = 9; r <= 11; r++)
            {
                Row(pixels, r, 3, 6);                    // the wheels
                Row(pixels, r, 13, 16);
            }
            Clear(pixels, 10, 4, 5);
            Clear(pixels, 10, 14, 15);
        }
    }
}
