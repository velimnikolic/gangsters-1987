using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The tactical map's colours, and the only place they are written down.
    ///
    /// Two families live here and they do NOT mix. The TERRAIN palette is the
    /// sixteen-colour feel of a 1987 municipal survey terminal: twenty-two exact hexes
    /// that everything inside the 320x200 raster is drawn in, and nothing else. The
    /// CHROME palette is the terminal around it - panel rules, labels, the context
    /// menu - which is uGUI and never touches a pixel of the raster.
    ///
    /// Affiliation is the one thing neither table owns. A family's colour comes from
    /// <see cref="LivingCity.UI.GangPalette"/>, because an id is a family for the life
    /// of a campaign and its colour must not move under it - twenty-one of them, where
    /// the design sheet had five. What IS taken from the sheet is the treatment: a
    /// bright TAG colour for the one pixel of a man that has to read at 1:1, a DARK for
    /// the ground, and a stripe direction so two families whose hues sit near each other
    /// still tell apart in a turf wash.
    ///
    /// One deliberate crossing of the two: <see cref="PlayerAccent"/> is the sheet's
    /// teal and NOT the player's gold. It is the terminal's own accent - a selection
    /// box, a corner tick, the roster row under the pointer - and it never states an
    /// affiliation. Whose men those are is said in gold, the same gold they wear on
    /// every other surface in the game.
    /// </summary>
    public static class MapPalette
    {
        // ------------------------------------------------------------- the raster

        /// <summary>
        /// THE shadow, and the only one. One pixel, one side, never an outline and never
        /// a halo. An earlier revision ringed every sprite and every building in near
        /// black (#141614) and the map read as black mush at any distance; this is that
        /// tone lifted until it reads as shade rather than as ink.
        /// </summary>
        public static readonly Color32 Shadow = Hex(0x2b302c);

        /// <summary>Kept for the few places that want true ink and are not a sprite
        /// outline - the corner chip behind a district tag, and nothing else.</summary>
        public static readonly Color32 Ink = Hex(0x141614);
        public static readonly Color32 White = Hex(0xe8e4d4);
        public static readonly Color32 Line = Hex(0xdedacc);
        public static readonly Color32 Concrete = Hex(0xa8a49c);
        public static readonly Color32 Concrete2 = Hex(0x8f8b84);
        public static readonly Color32 Road = Hex(0x4c4c50);
        public static readonly Color32 RoadDark = Hex(0x37373b);
        public static readonly Color32 Yellow = Hex(0xe0b830);
        public static readonly Color32 Red = Hex(0xa83c30);
        public static readonly Color32 Grass = Hex(0x3c7a34);
        public static readonly Color32 Grass2 = Hex(0x2e6428);
        public static readonly Color32 Tree = Hex(0x1e4a20);
        public static readonly Color32 Dirt = Hex(0x8c7048);
        public static readonly Color32 Sand = Hex(0xc8b884);
        public static readonly Color32 Water = Hex(0x2c5c9c);
        public static readonly Color32 WaterDeep = Hex(0x1c3c70);
        public static readonly Color32 Wave = Hex(0x5c8cc4);
        public static readonly Color32 BldgA = Hex(0xc8b8a0);
        public static readonly Color32 BldgB = Hex(0xa08068);
        public static readonly Color32 BldgC = Hex(0xd8d0c0);
        public static readonly Color32 Roof = Hex(0x7c6858);
        public static readonly Color32 Steel = Hex(0x8890a0);

        /// <summary>Ground nobody has surveyed - off the sheet's edge.</summary>
        public static readonly Color32 Void = Hex(0x05070a);

        // -------------------------------------------------------------- the chrome

        public static readonly Color Page = HexF(0x0b0d0c);
        public static readonly Color Well = HexF(0x05070a);
        public static readonly Color Rule = HexF(0x2a2f2c);
        public static readonly Color Muted = HexF(0x6f7a72);
        public static readonly Color Body = HexF(0xc9c4b4);
        public static readonly Color Strong = HexF(0xe8e4d4);
        public static readonly Color Dim = HexF(0x5e665f);
        public static readonly Color Heading = HexF(0xe0b830);

        /// <summary>The terminal's own accent, not a family's colour - see the class
        /// note. Selection boxes, corner ticks, the roster's live row.</summary>
        public static readonly Color PlayerAccent = HexF(0x35d1a0);

        public static readonly Color RowBack = HexF(0x12291f);
        public static readonly Color RowText = HexF(0xe8f8f0);
        public static readonly Color RowIdle = HexF(0x1c211e);
        public static readonly Color MenuBack = HexF(0x0d1310);
        public static readonly Color MenuHover = HexF(0x1d2a24);
        public static readonly Color MenuText = HexF(0xdcd8c8);
        public static readonly Color HpGood = HexF(0x35d1a0);
        public static readonly Color HpFair = HexF(0xe0b830);
        public static readonly Color HpPoor = HexF(0xf07a5a);
        public static readonly Color Chip = new Color(6f / 255f, 10f / 255f, 8f / 255f, 0.86f);
        public static readonly Color Button = new Color(6f / 255f, 10f / 255f, 8f / 255f, 0.90f);
        public static readonly Color ToggleOff = HexF(0x4a524c);

        // ----------------------------------------------------------- the contested

        /// <summary>Ground nobody holds outright: bone white, and drawn in BOTH stripe
        /// directions so it reads as an argument rather than as a family.</summary>
        public static readonly Color32 Contested = Hex(0xf4f0e0);
        public static readonly Color32 ContestedWash = Hex(0xd8d0c0);

        /// <summary>What contested ground is tinted with: a neutral desaturating grey,
        /// multiplied over the map. Not a family's colour, because it is nobody's.</summary>
        public static readonly Color32 ContestedTint = Hex(0xc3c9c3);

        public static readonly Color32 Unclaimed = Hex(0x8b9189);
        public static readonly Color UnclaimedChrome = HexF(0x8b9189);

        // ------------------------------------------------------------- affiliation

        /// <summary>A family's own colour, as every other surface in the game says it.</summary>
        public static Color32 Gang(int gangId) =>
            gangId < 0 ? Unclaimed : (Color32)LivingCity.UI.GangPalette.Of(gangId);

        /// <summary>The bright cut of it. This is the colour a man's HEAD pixel is drawn
        /// in - one pixel out of three, and the only one the eye gets at 1:1 - so it has
        /// to be lighter than anything it could be standing on.</summary>
        public static Color32 Tag(int gangId)
        {
            if (gangId < 0)
                return Hex(0xb8bdb4);
            var c = LivingCity.UI.GangPalette.Of(gangId);
            return new Color32(
                (byte)Mathf.RoundToInt(Mathf.Lerp(c.r * 255f, 255f, 0.40f)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(c.g * 255f, 255f, 0.40f)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(c.b * 255f, 255f, 0.40f)),
                255);
        }

        /// <summary>And the deep cut - the sheet's own darks measure about a third of
        /// their primary, which is the figure used here.</summary>
        public static Color32 Dark(int gangId)
        {
            if (gangId < 0)
                return Hex(0x222622);
            var c = LivingCity.UI.GangPalette.Of(gangId);
            return new Color32(
                (byte)Mathf.RoundToInt(c.r * 255f * 0.34f),
                (byte)Mathf.RoundToInt(c.g * 255f * 0.34f),
                (byte)Mathf.RoundToInt(c.b * 255f * 0.34f),
                255);
        }

        /// <summary>Which way a family's turf stripes lean. With five families the sheet
        /// named them one by one; with twenty-one the id's parity does the same job, and
        /// it puts the player (id 0) on the lean his own colour had.</summary>
        public static int StripeLean(int gangId) => (gangId & 1) == 0 ? -1 : 1;

        // ------------------------------------------------------------------ hexes

        public static Color32 Hex(int rgb) =>
            new Color32((byte)((rgb >> 16) & 0xff), (byte)((rgb >> 8) & 0xff), (byte)(rgb & 0xff), 255);

        public static Color HexF(int rgb) => (Color)Hex(rgb);
    }
}
