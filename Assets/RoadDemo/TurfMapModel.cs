using System.Collections.Generic;
using UnityEngine;
using LivingCity.Personnel;
using LivingCity.Entities;

namespace RoadDemo
{
    /// <summary>
    /// The 1987 survey plate's palette, named the way a printer's spec sheet names
    /// them rather than the way a renderer does. Every colour on the map comes from
    /// here; nothing anywhere else declares a Color32 for the plate.
    /// </summary>
    public static class TurfInk
    {
        public static Color32 Hex(string rgb)
        {
            int v = System.Convert.ToInt32(rgb, 16);
            return new Color32((byte)(v >> 16), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF), 255);
        }

        public static readonly Color32 Paper = Hex("efe5c8");
        public static readonly Color32 Stipple = Hex("cdbe95");

        public static readonly Color32 Land = Hex("ddd0a6");
        public static readonly Color32 Land2 = Hex("d2c398");
        public static readonly Color32 Hill = Hex("bcc396");
        public static readonly Color32 Highland = Hex("ac977b");
        public static readonly Color32 Contour = Hex("786d52");
        public static readonly Color32 Grass = Hex("c3cb9a");
        public static readonly Color32 Grass2 = Hex("b2bb87");
        public static readonly Color32 Tree = Hex("87954f");

        public static readonly Color32 Water = Hex("aebfc4");
        public static readonly Color32 Water2 = Hex("9cb0b8");
        public static readonly Color32 Wave = Hex("8ba3ad");

        /// <summary>Dark asphalt. The carriageway on this plate is the DARK shape and
        /// the ground around it is the pale one - the earlier revision had it the
        /// other way round and the city read as a negative of itself.</summary>
        public static readonly Color32 Road = Hex("6f6857");
        public static readonly Color32 RoadInk = Hex("3b352a");
        public static readonly Color32 RoadDark = Hex("7d7663");
        public static readonly Color32 Kerb = Hex("ded7bd");
        public static readonly Color32 Dash = Hex("a1926e");

        /// <summary>The centre divider, and the only pure paint on the road.</summary>
        public static readonly Color32 Lane = Hex("f2ecd6");

        /// <summary>Zebra bars: the same paint at 80%, laid with TurfPlate.Wash.</summary>
        public static readonly Color32 Zebra = Hex("f6f0dc");
        public const float ZebraStrength = 0.8f;

        /// <summary>Street lettering. Full white on purpose - the names are the loudest
        /// cartographic mark on the survey plate. Live tactical indicators are the one
        /// exception: they sit above the names because an order must never disappear
        /// under a word.</summary>
        public static readonly Color32 Street = Hex("fffdf2");

        /// <summary>The I-key movement inks, shared semantically with the world overlay:
        /// turquoise for an ordinary walk, yellow for a run and violet for a car.</summary>
        public static readonly Color32 MovementWalk = Hex("40f2d1");
        public static readonly Color32 MovementRun = Hex("ffd133");
        public static readonly Color32 MovementDrive = Hex("eb7aff");

        public static readonly Color32 Ink = Hex("1e1a12");
        public static readonly Color32 Ink2 = Hex("4a3f2c");

        public static readonly Color32 BlockA = Hex("8d7c5c");
        public static readonly Color32 BlockB = Hex("75674b");
        public static readonly Color32 BlockC = Hex("a0906c");
        public static readonly Color32 BlockD = Hex("5f5540");

        public static readonly Color32 Concrete = Hex("e3dcc4");
        public static readonly Color32 Concrete2 = Hex("d6cdb2");
        public static readonly Color32 Steel = Hex("6f6d5a");

        public static readonly Color32 Red = Hex("8f2119");
        public static readonly Color32 Rail = Hex("6d5c40");

        /// <summary>The lighter core inside a footprint's ink outline, and the hatch
        /// the industrial types get.</summary>
        public static readonly Color32 Core = Hex("9a8a68");
        public static readonly Color32 Hatch = Hex("3f3729");

        /// <summary>The pencil survey grid, ruled every 40 authored units.</summary>
        public static readonly Color32 Pencil = Hex("c2b18a");

        /// <summary>Vehicles: every cabin is this, whoever is driving.</summary>
        public static readonly Color32 Cabin = Hex("2a2620");
        public static readonly Color32 Lamp = Hex("f6efd8");
        public static readonly Color32 Civilian = Hex("4a3f2c");
    }

    /// <summary>
    /// One family's three tones. A house is never ONE colour on this map: the ink is
    /// what a hairline and a dot are drawn in, the pencil is the light core inside
    /// them, and the wash is the pale film the turf overlay multiplies the ground by.
    /// Mixing them up is what turns a survey plate into a heat map.
    /// </summary>
    public readonly struct TurfHouse
    {
        public readonly int GangId;
        public readonly string Name;
        public readonly string Short;
        public readonly Color32 Ink;
        public readonly Color32 Pencil;
        public readonly Color32 Wash;

        public TurfHouse(int gangId, string name, string shortName,
            Color32 ink, Color32 pencil, Color32 wash)
        {
            GangId = gangId;
            Name = name;
            Short = shortName;
            Ink = ink;
            Pencil = pencil;
            Wash = wash;
        }
    }

    /// <summary>
    /// Who is drawn in what. The design's table fixes three roles exactly - OUR
    /// OUTFIT, UNCLAIMED and CONTESTED - and those three never move: they are read
    /// on every screen of the game and a player learns them in the first minute.
    ///
    /// The rivals are the honest complication. The design sheet names three families
    /// (Marchetti, Salvatore, O'Rourke) because its own city had three; this city has
    /// twenty, each already carrying a campaign colour in
    /// <see cref="LivingCity.UI.GangPalette"/> that must not move under a running
    /// campaign. So a rival's three tones are DERIVED from that campaign colour by
    /// the same relationship the design's own table uses - ink is the colour pulled
    /// down to paper-ink strength, pencil is that ink a third of the way to the
    /// paper, wash is nearly three quarters of the way - which keeps a family the
    /// colour the player already knows it by while it prints like ink on a plate.
    /// </summary>
    public static class TurfHouses
    {
        /// <summary>The paper the tones are mixed toward. Not pure white: nothing on
        /// a 1987 plate is pure white except the street lettering.</summary>
        static readonly Color PaperWhite = new Color(1f, 0.992f, 0.949f);

        /// <summary>Ink lives in a narrow band of the colour solid, and that band is
        /// what makes twenty families read as one document. Outside it a bright
        /// family glares off the sheet and a dark one disappears into the shoreline.
        /// </summary>
        const float InkMinSat = 0.50f, InkMaxSat = 0.86f;
        const float InkMinVal = 0.36f, InkMaxVal = 0.60f;

        const float PencilMix = 0.38f, WashMix = 0.72f;

        /// <summary>Ground nobody holds.</summary>
        public static readonly TurfHouse Unclaimed = new TurfHouse(
            -1, "UNCLAIMED", "UNCLAIMED",
            TurfInk.Hex("7a684a"), TurfInk.Hex("a3906c"), TurfInk.Hex("d8cba8"));

        /// <summary>Ground two families hold equally. Neutral by design - a contested
        /// block that leaned toward either colour would be read as taken.</summary>
        public static readonly TurfHouse Contested = new TurfHouse(
            -2, "CONTESTED", "CONTESTED",
            TurfInk.Hex("4a3f2c"), TurfInk.Hex("8a7756"), TurfInk.Hex("cfc7ae"));

        /// <summary>The player. Fixed green, whatever gang 0's campaign colour is on
        /// other screens: on a paper plate the gold the outfit wears elsewhere is a
        /// pencil tone, not an ink one, and it cannot carry a hairline.</summary>
        public static readonly TurfHouse Ours = new TurfHouse(
            0, "OUR OUTFIT", "OURS",
            TurfInk.Hex("3f6b3a"), TurfInk.Hex("7fae74"), TurfInk.Hex("b9d3ac"));

        /// <summary>
        /// The rivals' tones, mixed once and kept. CONCURRENT because the map's drawing
        /// passes run on worker threads and there are two of them - the full plate and
        /// the corner card - either of which can ask for a family's ink at the same
        /// moment the panel on the main thread is asking for the same one. A plain
        /// Dictionary written from three threads corrupts, and the failure is a hang or
        /// a wrong answer rather than an exception, which is the worst kind.
        ///
        /// <see cref="Warm"/> fills it before any of that starts, so in practice every
        /// lookup is a read; the concurrent type is what makes a family outside the
        /// catalog's own range safe rather than lucky.
        /// </summary>
        static readonly System.Collections.Concurrent.ConcurrentDictionary<int, TurfHouse>
            _derived = new System.Collections.Concurrent.ConcurrentDictionary<int, TurfHouse>();

        /// <summary>Static state outlives Play with domain reload off - the
        /// OverlayRegistry rule.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => _derived.Clear();

        /// <summary>Mixes every family in the catalog, on the main thread, before the
        /// first plate is drawn. Called from the map's own prepare.</summary>
        public static void Warm()
        {
            var names = LivingCity.Gangs.GangCatalog.Names;
            for (int gangId = 1; gangId < names.Length; gangId++)
                For(gangId);
        }

        public static TurfHouse For(int gangId)
        {
            if (gangId == 0)
                return Ours;
            if (gangId < 0)
                return Unclaimed;

            if (_derived.TryGetValue(gangId, out var had))
                return had;

            var names = LivingCity.Gangs.GangCatalog.Names;
            var house = Mix(gangId, gangId < names.Length ? names[gangId] : null,
                LivingCity.UI.GangPalette.Of(gangId));
            return _derived.GetOrAdd(gangId, house);
        }

        /// <summary>One family's campaign colour, pressed into ink, pencil and wash.
        /// </summary>
        public static TurfHouse Mix(int gangId, string name, Color screen)
        {
            Color.RGBToHSV(screen, out float h, out float s, out float v);
            var ink = Color.HSVToRGB(h,
                Mathf.Clamp(s, InkMinSat, InkMaxSat),
                Mathf.Clamp(v, InkMinVal, InkMaxVal));

            var display = string.IsNullOrEmpty(name) ? "FAMILY " + gangId : name.ToUpperInvariant();
            return new TurfHouse(gangId, display, display,
                ink, Color.Lerp(ink, PaperWhite, PencilMix), Color.Lerp(ink, PaperWhite, WashMix));
        }
    }

    /// <summary>What a footprint is for. The design's nine types; the classifier that
    /// fills them in from the live city is TurfMapSurvey.TypeOf.</summary>
    public enum TurfType
    {
        House, Apartment, Shop, Tower, Warehouse, Factory, Civic, Hangar, Terminal
    }

    public readonly struct TurfTypeStyle
    {
        public readonly string Label;
        public readonly Color32 Fill;
        public readonly bool Hatch;

        public TurfTypeStyle(string label, Color32 fill, bool hatch)
        {
            Label = label;
            Fill = fill;
            Hatch = hatch;
        }

        /// <summary>The hatched types are the industrial ones - a works, a shed, a
        /// hangar. The diagonal is how a survey plate says "not somewhere anybody
        /// lives" without a legend.</summary>
        public static TurfTypeStyle Of(TurfType type)
        {
            switch (type)
            {
                case TurfType.House: return new TurfTypeStyle("ROW HOUSE", TurfInk.BlockB, false);
                case TurfType.Apartment: return new TurfTypeStyle("TENEMENT", TurfInk.BlockA, false);
                case TurfType.Shop: return new TurfTypeStyle("STOREFRONT", TurfInk.BlockC, false);
                case TurfType.Tower: return new TurfTypeStyle("OFFICE BLOCK", TurfInk.BlockC, true);
                case TurfType.Warehouse: return new TurfTypeStyle("WAREHOUSE", TurfInk.Steel, true);
                case TurfType.Factory: return new TurfTypeStyle("WORKS", TurfInk.BlockD, true);
                case TurfType.Civic: return new TurfTypeStyle("CIVIC HALL", TurfInk.Concrete, false);
                case TurfType.Hangar: return new TurfTypeStyle("HANGAR", TurfInk.Steel, true);
                default: return new TurfTypeStyle("AIR TERMINAL", TurfInk.Steel, true);
            }
        }
    }

    /// <summary>
    /// A place whose purpose matters more on the tactical map than the outline of its
    /// individual props. Houses and parks deliberately do not appear here: houses keep
    /// their surveyed footprints and parks keep their real lawn geometry.
    /// </summary>
    public enum TurfLandmarkKind
    {
        Generic,
        Gym,
        FuelStation,
        CarYard,
        Skatepark,
        Diner,
        SportsCourt,
        Parking,
        Police,
        FireStation,
        Nightclub,
        Warehouse,
        Fairground,
        Cafe,
        Transit,
        Landing,
    }

    /// <summary>One vocabulary for generated units, fixed Core blocks and future
    /// amenities. The source plans keep their own names; this adapter only decides which
    /// stable map glyph represents them.</summary>
    public static class TurfLandmarkKinds
    {
        public static string Label(TurfLandmarkKind kind)
        {
            switch (kind)
            {
                case TurfLandmarkKind.Gym: return "GYM";
                case TurfLandmarkKind.FuelStation: return "GAS";
                case TurfLandmarkKind.CarYard: return "CAR YARD";
                case TurfLandmarkKind.Skatepark: return "SKATE";
                case TurfLandmarkKind.Diner: return "DINER";
                case TurfLandmarkKind.SportsCourt: return "COURT";
                case TurfLandmarkKind.Parking: return "PARKING";
                case TurfLandmarkKind.Police: return "POLICE";
                case TurfLandmarkKind.FireStation: return "FIRE STATION";
                case TurfLandmarkKind.Nightclub: return "CLUB";
                case TurfLandmarkKind.Warehouse: return "WAREHOUSE";
                case TurfLandmarkKind.Fairground: return "FAIR";
                case TurfLandmarkKind.Cafe: return "CAFE";
                case TurfLandmarkKind.Transit: return "METRO";
                case TurfLandmarkKind.Landing: return "DOCK";
                default: return "SITE";
            }
        }

        public static TurfLandmarkKind From(string source)
        {
            string name = (source ?? "").Trim().ToLowerInvariant();
            if (name.Contains("caryard") || name.Contains("car-yard") ||
                name.Contains("car yard")) return TurfLandmarkKind.CarYard;
            if (name.Contains("skate")) return TurfLandmarkKind.Skatepark;
            if (name.Contains("gym")) return TurfLandmarkKind.Gym;
            if (name.Contains("fuel") || name.Contains("filling") ||
                name.Contains("gas-pump") || name.Contains("gas pump") ||
                name.Contains("gas-station") || name.Contains("gas station"))
                return TurfLandmarkKind.FuelStation;
            if (name.Contains("diner") || name.Contains("dinner"))
                return TurfLandmarkKind.Diner;
            if (name.Contains("kosarka") || name.Contains("basket") ||
                name.Contains("court")) return TurfLandmarkKind.SportsCourt;
            if (name.Contains("parking") || name.Contains("car-park") ||
                name.Contains("car park")) return TurfLandmarkKind.Parking;
            if (name.Contains("police")) return TurfLandmarkKind.Police;
            // The fire house is matched on the whole word: "fire" alone is a hydrant,
            // an escape and half the dressing in the city.
            if (name.Contains("firestation") || name.Contains("fire-station") ||
                name.Contains("fire station") || name.Contains("firehouse") ||
                name.Contains("fire-house") || name.Contains("fire house"))
                return TurfLandmarkKind.FireStation;
            if (name.Contains("nightclub") || name.Contains("night-club") ||
                name.Contains("night club")) return TurfLandmarkKind.Nightclub;
            if (name.Contains("warehouse")) return TurfLandmarkKind.Warehouse;
            if (name.Contains("fair")) return TurfLandmarkKind.Fairground;
            if (name.Contains("cafe") || name.Contains("coffee") ||
                name.Contains("terrace")) return TurfLandmarkKind.Cafe;
            if (name.Contains("subway") || name.Contains("metro"))
                return TurfLandmarkKind.Transit;
            if (name.Contains("landing") || name.Contains("marina") ||
                name.Contains("dock")) return TurfLandmarkKind.Landing;
            return TurfLandmarkKind.Generic;
        }
    }

    /// <summary>A plan-owned icon on the survey. It contains no Unity object so the
    /// worker-thread draw remains independent of streamed/recycled scene views.</summary>
    public sealed class TurfLandmark
    {
        public TurfLandmarkKind Kind;
        public string Name = "";
        public string Label = "";
        public Rect World;
        public Rect Plan;
        public int BlockId = -1;
        public bool ReplacesFootprints;
    }

    /// <summary>
    /// One building on the plate, and the SAME building in the world. Tf is the very
    /// transform the street's own picker raycasts and Business is the very marker the
    /// ownership layer writes - the map holds references, never copies, so a
    /// footprint that changes hands in the city has changed hands on the map before
    /// anything asks. Plan is the only derived thing here: the world rect projected
    /// into authored units, cached because it never moves.
    /// </summary>
    public sealed class TurfBuilding
    {
        public int Id;
        public Transform Tf;
        public BusinessMarker Business;
        /// <summary>A district explicitly identified this structure and its pick bounds.
        /// Use its complete renderer mass, clipped to World, without mesh-name guesses.</summary>
        public bool AuthoredFootprint;

        /// <summary>World XZ footprint, metres - renderer bounds, the same union the
        /// street card measures.</summary>
        public Rect World;

        /// <summary>The same rect in authored units.</summary>
        public Rect Plan;

        public TurfType Type;
        public int Floors;
        public string Name;
        public string District;
        public int BlockId = -1;

        /// <summary>The business's own weekly take, dollars; nought when the city
        /// has not priced it, and then the file prints no row rather than a guess.
        /// </summary>
        public int Rent;

        /// <summary>Whose front this is; -1 for the honest majority. Read live off
        /// the marker so a takeover shows without a rebuild. MAIN THREAD ONLY: the
        /// null test on a Unity object is a call into the engine.</summary>
        public int GangId => Business != null ? Business.GangId : -1;

        /// <summary>The same answer, sampled on the main thread before a survey is
        /// handed to a worker. The drawing passes read THIS - a plate is painted off
        /// the thread pool and must not ask a MonoBehaviour anything.</summary>
        public int Owner = -1;
    }

    /// <summary>What a crew has been told to do. The order is what the map prints
    /// under a lieutenant's name and what the marker at the target means.</summary>
    public enum TurfOrder
    {
        Holding, Moving, WalkingIn, Walking, PullingBack, Taking, ToTheOutfit, InTheCar,

        /// <summary>Off the street altogether: the crew was taken into one of our own
        /// buildings and is standing in it (CrewQuarters).</summary>
        Inside,
    }

    public static class TurfOrders
    {
        public static string Label(TurfOrder order)
        {
            switch (order)
            {
                case TurfOrder.Moving: return "MOVING";
                case TurfOrder.WalkingIn: return "WALKING IN";
                case TurfOrder.Walking: return "WALKING";
                case TurfOrder.PullingBack: return "PULLING BACK";
                case TurfOrder.Taking: return "TAKING";
                case TurfOrder.ToTheOutfit: return "TO THE OUTFIT";
                case TurfOrder.InTheCar: return "IN THE CAR";
                case TurfOrder.Inside: return "INSIDE";
                default: return "HOLDING";
            }
        }
    }

    /// <summary>One man on the book. Condition is the three-way the dossier prints in
    /// three colours; the note is what the ledger's own personnel page says.</summary>
    public sealed class TurfMan
    {
        public string Name = "";
        public string Role = "MUSCLE";
        public string Gun = "Bare hands";
        public string Condition = "FIT";
        public string ConditionNote = "";
    }

    /// <summary>
    /// A crew as the map knows it: a dot, a lieutenant's face and a standing order.
    /// The dot is a CREW, never a man - the men are a list inside it, and the panel
    /// is where their number is read.
    ///
    /// Unit is the live simulation object. Everything the map does to a crew it does
    /// through DemoCrews (MarchTo, Sic, BoardCar); nothing here moves anybody, so a
    /// crew the street is already walking somewhere cannot be teleported by a map
    /// that thinks it knows better.
    /// </summary>
    public sealed class TurfCrew
    {
        public DemoCrews.Unit Unit;

        /// <summary>The actual personnel records behind our street unit. Rival crews
        /// have neither: they are observed on the pavement, not carried on our books.</summary>
        public Crew Book;
        public Character Lieutenant;

        public int Id;
        public int GangId;

        /// <summary>Authored-space position, refreshed each frame from the world.
        /// </summary>
        public Vector2 Plan;

        /// <summary>Where the crew posts up - what PULLING BACK returns to, in
        /// world metres.</summary>
        public Vector3 Post;

        public TurfOrder Order = TurfOrder.Holding;

        /// <summary>The patrol box WALKING re-homes to, in WORLD METRES about the point
        /// the order was given. Ground and not paper: a box measured in the plate's
        /// authored units is a different piece of city at every zoom, so the same order
        /// would mean a different block depending on where the wheel happened to be
        /// when it was given.</summary>
        public Rect Zone;

        /// <summary>The corner of the patrol box the crew is walking to now, world
        /// metres. A patrol is a run of ordinary marches inside a box, not a separate
        /// movement mode - DemoCrews already knows how to walk a crew down a pavement
        /// and this map must not grow a second way of doing it.</summary>
        public Vector2 Wander;

        /// <summary>The footprint a TAKING order is walking at, held until they get
        /// there. Null on every other order.</summary>
        public TurfBuilding Taking;

        public readonly List<TurfMan> Men = new List<TurfMan>();

        public string Name = "";

        /// <summary>The lieutenant's rank off the personnel ledger, in caps. Empty
        /// for a crew on nobody's books - a rival's - and then the dossier leaves the
        /// word out rather than making one up from the crew's size.</summary>
        public string Rank = "";
        public string Ride = "On foot";
        public string Gun = "Bare hands";

        /// <summary>The lieutenant's actual ledger ratings, in half steps. The map
        /// only carries the three that matter while directing a crew here.</summary>
        public int Awareness = 3, Organization = 3, Combat = 3;
        public int Heat, Loyal, Take;

        /// <summary>Ours to command. Only ours can be selected, marqueed or ordered.
        /// </summary>
        public bool Mine;

        public bool Alive => Unit != null && !Unit.Wiped;
        public int MenStanding => Unit != null ? Unit.Standing() : Men.Count;
        public int HoodsOnBooks => Book != null ? Book.HoodIds.Count : Mathf.Max(0, Men.Count - 1);
    }

    /// <summary>A quarter of the city, as a rectangle of authored ground with a name
    /// and whoever holds most of it.</summary>
    /// <summary>
    /// One canonical block as the plate draws it: where it is, what it currently reads as,
    /// and who leads it. A SNAPSHOT - taken on the main thread with the rest of the
    /// ownership read, so the worker draw never touches the simulation.
    /// </summary>
    public struct TurfBlockReading
    {
        public Rect World;
        public Rect Plan;
        public LivingCity.Territory.TerritoryControlState State;

        /// <summary>The family the street answers to, or -1 while nobody leads it.</summary>
        public int GangId;

        /// <summary>The other house in a fight, for the two-tone hatch. -1 when there is
        /// only one name on the street.</summary>
        public int RivalGangId;
    }

    public sealed class TurfDistrict
    {
        public string Name = "";
        public Rect Plan;
        public Rect World;

        /// <summary>Set only for Core's logical territory. None keeps the ordinary
        /// RoadDemo rule where ownership is scored from premises inside the district.</summary>
        public CoreQuarterId TerritoryId;

        /// <summary>Main-thread snapshot of Core campaign state, read by the worker draw.</summary>
        public int TerritoryGangId = -1;

        /// <summary>Recomputed from the holdings whenever ownership changes: the
        /// family holding most footprints here, -1 for nobody, -2 for a tie.</summary>
        public int GangId = -1;

        public bool Contested => GangId == -2;

        public TurfHouse House =>
            GangId == -2 ? TurfHouses.Contested : TurfHouses.For(GangId);
    }

    /// <summary>
    /// The one conversion between the city and the plate: world metres (X east,
    /// Z north) to authored units (X east, Y north). Y is NOT flipped - north is up
    /// the sheet and up the texture both, which is what a survey plan means by north
    /// and what saves every draw call on this map from carrying a flip.
    ///
    /// The fit is to the ROAD GRID and a margin, never to the whole island. An island
    /// is three kilometres of coast around a city a mile across; fitted to the island
    /// the streets come out four pixels wide, no name is readable and no boulevard is
    /// wide enough to carry a centre line. Fitted to the grid a 20 m boulevard lands
    /// at four authored units - exactly the width the design's own lane-line rule
    /// asks for - and the coast still has room at the edges of the sheet.
    /// </summary>
    public readonly struct TurfProjection
    {
        /// <summary>Metres of city in one authored unit. Derived, not declared: the
        /// design sheet's "1 unit = 8 m" was its own city's number, and this one is
        /// whatever makes this city's grid fill the plate.</summary>
        public readonly float MetresPerUnit;

        /// <summary>The world point that lands at authored (0,0). Derived from
        /// <see cref="OriginPx"/>, so it is always a whole real pixel of the world.</summary>
        public readonly Vector2 Origin;

        /// <summary>
        /// THE framing: the origin as whole REAL pixels of the world. The projection
        /// works in world pixels - a point is scaled first, by a factor that knows
        /// nothing about where the sheet is, and only then offset by this integer -
        /// so two draws at one scale differ by a whole number of pixels and by
        /// nothing else. Subtract the origin first and scale after, the obvious way
        /// and the way this used to go, and every edge carries a float error that
        /// depends on the framing; a city's coordinates are whole metres that land
        /// exactly on pixel boundaries, and that error decided which side of the
        /// boundary a kerb, a zebra or a fleck fell on, differently every pan.
        /// </summary>
        public readonly Vector2Int OriginPx;

        public readonly Rect World;

        public TurfProjection(Rect fit)
        {
            MetresPerUnit = Mathf.Max(fit.width / TurfPlate.AW, fit.height / TurfPlate.AH);
            if (MetresPerUnit <= 0f)
                MetresPerUnit = 1f;

            // Centre what is fitted on the sheet: whatever the aspect mismatch leaves
            // over becomes coast and country at the margins, evenly on both sides -
            // and then snapped to the pixel grid, half a pixel at most.
            var span = new Vector2(TurfPlate.AW * MetresPerUnit, TurfPlate.AH * MetresPerUnit);
            var pixel = MetresPerUnit / TurfPlate.S;
            var wanted = fit.center - span * 0.5f;
            OriginPx = new Vector2Int(
                Mathf.RoundToInt(wanted.x / pixel), Mathf.RoundToInt(wanted.y / pixel));
            Origin = new Vector2(OriginPx.x * pixel, OriginPx.y * pixel);
            World = new Rect(Origin, span);
        }

        /// <summary>Real pixels to the metre.</summary>
        float PixelsPerMetre => TurfPlate.S / MetresPerUnit;

        float PlanX(float worldX) => (worldX * PixelsPerMetre - OriginPx.x) / TurfPlate.S;
        float PlanY(float worldZ) => (worldZ * PixelsPerMetre - OriginPx.y) / TurfPlate.S;

        public Vector2 ToPlan(Vector2 worldXZ) => new Vector2(PlanX(worldXZ.x), PlanY(worldXZ.y));

        public Vector2 ToPlan(Vector3 world) => new Vector2(PlanX(world.x), PlanY(world.z));

        /// <summary>Both edges projected on their own and the size taken between
        /// them, so a far edge lands where the near edge of its neighbour does.</summary>
        public Rect ToPlan(Rect worldRect) => Rect.MinMaxRect(
            PlanX(worldRect.xMin), PlanY(worldRect.yMin),
            PlanX(worldRect.xMax), PlanY(worldRect.yMax));

        public Vector2 ToWorld(Vector2 plan) => Origin + plan * MetresPerUnit;

        public float Metres(float units) => units * MetresPerUnit;
        public float Units(float metres) => metres / MetresPerUnit;
    }
}
