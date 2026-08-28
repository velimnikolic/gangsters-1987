using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static RoadDemo.Composer;

namespace RoadDemo
{
    /// <summary>
    /// Stands a residential block up from the division <see cref="ResidentialLot"/> made:
    /// the units on their cells, the ground under everything that is not a building, and
    /// what belongs on that ground.
    ///
    /// The same bargain as the park and the industrial parcel: one delegate says how a
    /// prefab is raised, so the editor gets linked instances and the game plain ones, and
    /// nothing here knows which called it.
    ///
    /// The ground is concrete and the trees are palms on the pavement - the user's word on
    /// the first drawing (2026-08-27: "zajebi travu izmedju, ocu samo beton i palme po
    /// trotoaru"). ONE TILE A CELL, always: two coplanar tiles flicker through each other.
    ///
    /// The second drawing was judged the same day: too empty, one kind of cafe, too many
    /// skips, a car in a stall it could never have reached, and the cafe in its neighbour's
    /// fire escape. So the pavement got its street furniture (parking meters, bins, a bus
    /// shelter, bollards, a mailbox, a hot dog cart), the yards their power boxes and
    /// picnic tables, the paved gaps a bench or a billboard or a subway stair, the ring a
    /// storm drain every dozen tiles and now and then a dug-up stretch, the parks he laid
    /// in the harvest scene stand where the houses left room, and the storefront is drawn
    /// from a pool and set a fire escape's reach off its neighbour.
    /// </summary>
    public static partial class ResidentialBlocks
    {
        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string CityProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string Units = "Assets/Prefabs/Residential/";
        const string KitBld = "Assets/CityKit/Buildings/";

        const string Kerb = CityEnv + "SM_Env_Sidewalk_Straight_01.prefab";
        const string KerbCorner = CityEnv + "SM_Env_Sidewalk_Corner_01.prefab";
        const string Paving = CityEnv + "SM_Env_Sidewalk_01.prefab";
        const string Bare = CityEnv + "SM_Env_Road_Bare_01.prefab";
        const string Bays = CityEnv + "SM_Env_Road_ParkingLines_01.prefab";
        const string Arrow = CityEnv + "SM_Env_Road_Arrow_01.prefab";
        const string Lamp = CityProps + "SM_Prop_LightPole_Base_01.prefab";

        /// <summary>The kerb tile with a storm drain in it, and the kerb tile that is two
        /// cells long and dug up behind its barriers. Both carry the kerb on the same edge
        /// as the plain one (+z at yaw 0, measured 2026-08-27), so they turn the same way.
        /// The user asked why the pavement never had either: "zacinio bi grad".</summary>
        const string Drain = CityEnv + "SM_Env_Sidewalk_Gutter_01.prefab";
        const string Dug = CityEnv + "SM_Env_Sidewalk_Construction_01.prefab";
        const int DrainEvery = 12;              // the core's own rhythm (CorePavement)
        const double DugOdds = 0.3;             // blocks with a dug-up stretch

        /// <summary>The subway entrance with the glass canopy. Its mouth is its +z end and
        /// its stair runs down towards -z, 15 m in all (measured 2026-08-27).</summary>
        const string SubwayPath = CityEnv + "Custom/SM_Env_SubwayEntrance_01.prefab";

        /// <summary>The pavement's furniture, all POLYGON City, all by the names the user
        /// read off the demo scene (2026-08-27) - the demo's instance names, mapped back
        /// to the prefabs they came from.</summary>
        const string Meter = CityProps + "SM_Prop_ParkingMeter_02.prefab";
        const string PowerBox = CityProps + "SM_Prop_PowerBox_01.prefab";
        const string Bollards = CityProps + "SM_Prop_SidewalkPoles_01.prefab";
        const string Hotdog = CityProps + "SM_Prop_HotdogStand_01.prefab";
        const string Picnic = CityProps + "SM_Prop_PicnicTable_01.prefab";
        const string Mailbox = CityProps + "SM_Prop_Mailbox_01.prefab";
        const string Carton = CityProps + "SM_Prop_CardboardBox_04.prefab";
        const string BusStop = CityProps + "SM_Prop_BusStop_01.prefab";
        const string BillboardPole = CityProps + "SM_Prop_Billboard_Pole_01.prefab";
        const string BillboardPanel = CityProps + "SM_Prop_Billboard_01.prefab";
        static readonly string[] Bins =
        {
            CityProps + "SM_Prop_Trashbin_01.prefab",
            CityProps + "SM_Prop_Trashbin_02.prefab",
        };

        /// <summary>The patio beside the storefront: the pier's own tables and chairs
        /// (QuayBlocks), a bench against the back line.</summary>
        const string CoffeeProps = "Assets/Synty/PolygonCoffeeShop/Prefabs/";
        const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        const string CafeTable = CityProps + "SM_Prop_Table_02.prefab";
        const string CafeChair = CoffeeProps + "SM_Prop_Chair_01.prefab";
        static readonly string[] Umbrellas =
        {
            PalmProps + "SM_Prop_Umbrella_01.prefab",
            PalmProps + "SM_Prop_Umbrella_02.prefab",
            PalmProps + "SM_Prop_Umbrella_03.prefab",
        };
        const string ParkBench = CityProps + "SM_Prop_ParkBench_01.prefab";
        const string CourtFountain = PalmProps + "SM_Prop_Fountain_01.prefab";
        const string CourtPlanterBench = PalmProps + "SM_Prop_Planter_Bench_01.prefab";
        const string CourtPlanter = PalmProps + "SM_Prop_Planter_04.prefab";
        const string CourtTable = PalmProps + "SM_Prop_Table_Outdoor_01.prefab";
        const float TableAlongMin = 3.6f, TableAlongMax = 4.6f;
        const float TableRowMin = 3.6f, TableRowMax = 4.5f;
        const float TableJitter = 0.45f;
        const double TableKeep = 0.84, ShadeOdds = 0.55;

        /// <summary>How many seats of its own a storefront has to bring before the block
        /// stops laying a patio in front of it: a bench and a table are dressing, a dozen
        /// chairs under umbrellas are a terrace already.</summary>
        const int OwnSeats = 6;

        /// <summary>How far a storefront stands off the wall of the house beside it, before
        /// whatever that house hangs over the line is added.</summary>
        const float Clear = 1.2f;

        /// <summary>
        /// Whether the block is DRESSED: the street furniture, the props in its yards and the
        /// bench-and-billboard in its paved gaps.
        ///
        /// Off, on the user's word (2026-08-28: "nemoj da dodajes rucno props na ove blokove
        /// za sad jer mi se cini da props zauzmu mesto gde bi mogao biti kafic recimo i onda
        /// imam jedan kontenjer i prazan prostor"). Every prop books the ground it stands on,
        /// so a bin dropped in a gap is a gap a shop can no longer have. The pavement's own
        /// rhythm - the kerb, its lamps and its palms - is not props and stays.
        /// </summary>
        public static bool Dressed;

        /// <summary>
        /// The kit storefronts a gap can take, and every one of them is MEASURED against
        /// the gap at compose time rather than filed by size: the coffee shop (5.8 x 7.2 m)
        /// in two cells, the corner store (12.5 x 6.9) in three, the diners (16.3 x 8.8) in
        /// four. The restaurants and the cafe are 21 m deep and never fit a two-cell gap,
        /// so they are not offered; the harvested storefronts (the user's pizzapub) come
        /// out of the units table and stand by arithmetic like the houses.
        /// </summary>
        static readonly string[] Kit =
        {
            KitBld + "building-coffeeshop.prefab",
            KitBld + "building-diner.prefab",
            KitBld + "building-burger-joint.prefab",
        };

        static readonly string[] Skips =
        {
            CityProps + "SM_Prop_Skip_01.prefab",
            CityProps + "SM_Prop_Skip_02.prefab",
        };
        static readonly string[] Litter =
        {
            CityProps + "SM_Prop_TrashBag_01.prefab",
            CityProps + "SM_Prop_TrashBag_03.prefab",
            CityProps + "SM_Prop_CardboardBox_01.prefab",
            CityProps + "SM_Prop_CardboardBox_04.prefab",
        };
        /// <summary>The height the pack's own paving stands at, so a prop set on a tile
        /// stands on the tile rather than in it.</summary>
        const float Deck = 0.054f;

        /// <summary>Lamps go a cell in from the corner and then every four cells - 20 m -
        /// which is the rhythm measured off the demo (Docs/synty-demo-anatomy.md).</summary>
        const int LampEvery = 4;

        /// <summary>A storefront whose floor goes deeper than this below zero has a sunken
        /// floor of its own (the diner: -1.56 m), and no tile is laid where it stands.</summary>
        const float SunkFloor = -0.5f;

        /// <summary>One painted stall: where its middle is and which way a car noses into
        /// it - away from the aisle.</summary>
        readonly struct Stall
        {
            public readonly Vector3 At;
            public readonly int Into;
            public Stall(Vector3 at, int into) { At = at; Into = into; }
        }

        /// <summary>How many of the stalls have a car in them. One in two is what the pack's
        /// own demo does and what the industrial yards do; a lot of freshly painted empty
        /// bays is a car park nobody uses.</summary>
        const double Parked = 0.5;

        /// <summary>How often a verge cell beside the alley gets a skip. The first cut put
        /// one on two cells in five, both verges, and the alley was a skip yard (the user,
        /// 2026-08-27: "stavljas previse kontenjera"). Now one cell in seven, and never
        /// two within ten metres.</summary>
        const double SkipOdds = 0.15;
        const float SkipApart = 10f;

        public sealed class Stood
        {
            public int Units, Tiles, Props, Lamps, Palms, Stalls, Cars, Tables, Benches, Parks;
            public int Drains, Dug, Meters, Bins, Boxes, Picnics;
            public int SurfaceFlush, SurfaceClusters, SurfaceMissing;
            public string SurfaceProfile = "";
            public bool Subway, BusStop, Hotdog, Billboard, Mailbox;
            public int Missing;
            public string Cafe = "";
            public string Refused = "";
            public List<string> Absent = new List<string>();

            public override string ToString()
            {
                var extra = new List<string>();
                if (Parks > 0) extra.Add($"{Parks} park(s)");
                if (Subway) extra.Add("subway");
                if (BusStop) extra.Add("bus stop");
                if (Hotdog) extra.Add("hot dog cart");
                if (Billboard) extra.Add("billboard");
                if (Mailbox) extra.Add("mailbox");
                if (Meters > 0) extra.Add($"{Meters} meter(s)");
                if (Bins > 0) extra.Add($"{Bins} bin(s)");
                if (Boxes > 0) extra.Add($"{Boxes} power box(es)");
                if (Picnics > 0) extra.Add($"{Picnics} picnic table(s)");
                if (Drains > 0) extra.Add($"{Drains} drain(s)");
                if (Dug > 0) extra.Add("dug-up pavement");
                if (SurfaceFlush + SurfaceClusters > 0)
                    extra.Add($"surface {SurfaceProfile}: {SurfaceFlush} flush/{SurfaceClusters} cluster(s)");
                return
                    $"{Units} unit(s), {Tiles} tile(s), {Props} prop(s) ({Palms} palm(s), {Lamps} lamp(s), " +
                    $"{Tables} table(s), {Benches} bench(es)), {Cars} car(s) in {Stalls} stall(s)" +
                    (Cafe.Length > 0 ? $", {Cafe}" : "") +
                    (extra.Count > 0 ? ", " + string.Join(", ", extra) : "") +
                    (Absent.Count > 0 ? $", MISSING {string.Join(", ", Absent)}" : "") +
                    (Refused.Length > 0 ? $", refused {Refused}" : "");
            }
        }

        /// <summary>
        /// Stands the plan up under <paramref name="root"/>, with the block's south-west
        /// corner at the root's own origin. Composed at the origin and moved afterwards is
        /// the caller's business, as it is for the industrial quarter.
        /// </summary>
        public static Stood Compose(ResidentialLot.Plan plan, Transform root, System.Random rng,
                                    System.Func<GameObject, Transform, GameObject> raise)
        {
            Begin(raise);
            ForgetMissing();
            var stood = new Stood();

            // Storefronts are chosen before the ground, which is laid round their feet.
            // Each reserved gap is composed independently: folding two gaps into one bound
            // made the first shop claim a huge, mostly empty slab.
            var cafes = new List<(ResidentialLot.Gap Gap, CafeSpot Spot)>();
            var cafeGaps = plan.Cafes.Count > 0
                ? plan.Cafes
                : (plan.Cafe != null ? new List<ResidentialLot.Gap> { plan.Cafe }
                                     : new List<ResidentialLot.Gap>());
            foreach (var gap in cafeGaps)
            {
                var spot = CafeOf(plan, gap, rng, stood);
                if (spot != null) cafes.Add((gap, spot));
            }
            var ring = Ring(plan, rng);
            var kerbs = new List<CorePavement.Kerbstone>();
            var stalls = new List<Stall>();
            var standing = new List<Vector3>();

            AmenityFloors(plan, root, stood);
            Ground(plan, root, cafes.Select(c => c.Spot).ToList(), ring, kerbs, stalls, stood);
            Stand(plan, root, stood);
            Subway(plan, root, stood);
            foreach (var placed in cafes)
            {
                if (!CafeStand(placed.Spot, root, stood)) continue;
                // A storefront that arrived with its own terrace is not given a second one.
                if ((placed.Spot.Unit?.Seats ?? 0) < OwnSeats)
                {
                    Patio(plan, placed.Gap, placed.Spot, root, rng, standing, stood);
                    Terraces(plan, placed.Gap, placed.Spot, root, rng, standing, stood);
                }
            }
            Courtyard(plan, root, rng, standing, stood);
            SharedYards(plan, root, rng, standing, stood);
            Cars(stalls, root, rng, raise, stood);
            if (Dressed)
            {
                Dress(plan, root, rng, stood);
                Yards(plan, root, rng, stood);
                Plazas(plan, root, rng, stood);
            }

            Lamps(plan, root, standing, stood);
            if (Dressed) Street(plan, root, rng, standing, stood);
            Palms(kerbs, standing, root, raise, rng.Next(), stood);
            SurfaceDetails(plan, root, stood);

            stood.Absent.AddRange(Missing);
            stood.Missing = Missing.Count;
            stood.Refused = Worst();
            return stood;
        }

        /// <summary>
        /// Outdoor amenities were harvested from Palm City's terrain. Their ramps, fences,
        /// machines and tables came across, but the terrain did not, so holes showed through
        /// below the gym, car yard, basketball court and both diners. A quiet concrete backing
        /// is laid one tile per cell under the complete amenity rectangle, just below zero:
        /// its own court/tarmac stays visible and every gap still has a floor.
        /// </summary>
        static void AmenityFloors(ResidentialLot.Plan plan, Transform root, Stood stood)
        {
            const float below = -0.06f;
            float cell = ResidentialLot.Cell;
            foreach (var spot in plan.Spots)
            {
                if (spot.Unit.Kind != ResidentialKind.Amenity) continue;
                var turn = ResidentialLot.Turn.Of(spot.Unit, spot.Yaw);
                for (int u = 0; u < turn.CW; u++)
                    for (int v = 0; v < turn.CD; v++)
                        if (Lay(Paving, root, (spot.I + u) * cell, (spot.J + v) * cell,
                                cell, cell, 0f, below) != null) stood.Tiles++;
            }
        }

        // ------------------------------------------------------------------ the ring

        /// <summary>One tile of the pavement ring: which piece, turned how, and how many
        /// cells along the side it covers. A cell covered by a longer tile has no entry.</summary>
        sealed class RingTile
        {
            public string Tile;
            public float Yaw;
            public int Cells = 1;
            public bool Kerbed = true;       // a kerb tile: palms may be planted on it
        }

        /// <summary>Is this ring cell a mouth - cut for a way in?</summary>
        static bool Mouth(ResidentialLot.Plan plan, int i, int j) =>
            i >= 0 && j >= 0 && i < plan.W && j < plan.D &&
            (i == 0 || j == 0 || i == plan.W - 1 || j == plan.D - 1) &&
            ResidentialLot.Drives(plan.Ground[i, j]);

        /// <summary>The ring cell <paramref name="at"/> along this side, corners included.</summary>
        static (int, int) RingCell(ResidentialLot.Plan plan, int side, int at) => side switch
        {
            0 => (at, 0),
            2 => (at, plan.D - 1),
            1 => (plan.W - 1, at),
            _ => (0, at),
        };

        /// <summary>
        /// The kerb ring decided before it is laid: one tile a cell, turned to face OUT,
        /// with corner tiles on the corners - the rule measured off all sixteen harvested
        /// blocks - a storm drain in the run every <see cref="DrainEvery"/> kerb tiles, and
        /// on some blocks one stretch of two cells dug up behind its barriers. The dug-up
        /// stretch never takes a corner, a mouth or the cell beside either.
        /// </summary>
        static Dictionary<(int, int), RingTile> Ring(ResidentialLot.Plan plan, System.Random rng)
        {
            var ring = new Dictionary<(int, int), RingTile>();
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    if (plan.Ground[i, j] != ResidentialLot.Use.Walkway) continue;
                    bool west = i == 0, east = i == plan.W - 1, south = j == 0, north = j == plan.D - 1;
                    if ((west || east) && (south || north))
                    {
                        float turn = KerbYaw.Corner(north, east);
                        ring[(i, j)] = new RingTile { Tile = KerbCorner, Yaw = turn, Kerbed = false };
                        continue;
                    }
                    // beside a mouth the pavement turns its corner into it, so the kerb
                    // wraps round the way in instead of stopping flat (the user,
                    // 2026-08-27: "samo stavi pavement za coskove")
                    bool alongX = south || north;
                    int ahead = alongX ? i + 1 : j + 1, behind = alongX ? i - 1 : j - 1;
                    bool mouthAhead = Mouth(plan, alongX ? ahead : i, alongX ? j : ahead);
                    bool mouthBehind = Mouth(plan, alongX ? behind : i, alongX ? j : behind);
                    if (mouthAhead || mouthBehind)
                    {
                        // the corner that faces the mouth: along a south or north side the
                        // mouth ahead lies east of the cell, along an east or west side north of it
                        float turn = KerbYaw.Corner(!south && (north || mouthAhead), east || (!west && mouthAhead));
                        ring[(i, j)] = new RingTile { Tile = KerbCorner, Yaw = turn, Kerbed = false };
                        continue;
                    }
                    float yaw = north ? 0f : east ? 90f : south ? 180f : 270f;
                    ring[(i, j)] = new RingTile { Tile = Kerb, Yaw = yaw };
                }

            // the drains, counted round the ring side by side
            int n = 0, drainAt = rng.Next(DrainEvery);
            for (int side = 0; side < 4; side++)
            {
                int length = side == 0 || side == 2 ? plan.W : plan.D;
                for (int at = 1; at < length - 1; at++)
                {
                    if (!ring.TryGetValue(RingCell(plan, side, at), out var tile) || tile.Tile != Kerb) continue;
                    if (n++ % DrainEvery == drainAt) tile.Tile = Drain;
                }
            }

            if (!Chance(rng, DugOdds)) return ring;
            foreach (int side in Enumerable.Range(0, 4).Where(s => plan.Street[s]).OrderBy(_ => rng.Next()))
            {
                int length = side == 0 || side == 2 ? plan.W : plan.D;
                var spots = new List<int>();
                for (int at = 2; at + 1 <= length - 3; at++)
                {
                    bool clear = true;
                    // the two cells it covers and the one either side are plain kerb
                    for (int k = -1; k <= 2 && clear; k++)
                        if (!ring.TryGetValue(RingCell(plan, side, at + k), out var t) || t.Tile == KerbCorner)
                            clear = false;
                    if (clear) spots.Add(at);
                }
                if (spots.Count == 0) continue;
                int start = spots[rng.Next(spots.Count)];
                var first = ring[RingCell(plan, side, start)];
                first.Tile = Dug;
                first.Cells = 2;
                first.Kerbed = false;
                ring.Remove(RingCell(plan, side, start + 1));
                break;
            }
            return ring;
        }

        // ------------------------------------------------------------------ the ground

        static void Ground(ResidentialLot.Plan plan, Transform root, List<CafeSpot> cafes,
                           Dictionary<(int, int), RingTile> ring,
                           List<CorePavement.Kerbstone> kerbs, List<Stall> stalls, Stood stood)
        {
            var laid = new bool[plan.W, plan.D];   // the cells a bay pair has already covered
            float cell = ResidentialLot.Cell;
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    if (laid[i, j]) continue;
                    string tile;
                    float yaw = 0f;
                    switch (plan.Ground[i, j])
                    {
                        case ResidentialLot.Use.Walkway:
                            Pavement(plan, root, i, j, ring, kerbs, stood);
                            continue;
                        case ResidentialLot.Use.Building:
                            // nothing under a unit: it stands at the level the pack gave it
                            continue;
                        case ResidentialLot.Use.Forecourt:
                            // the unit's own ground is paved at the level IT keeps: a pit
                            // gets its slab at the bottom, where the stoops come down to,
                            // and the garden inside the brownstone's L is paved at zero.
                            // Nothing at zero over a pit (the user, 2026-08-27: "ispod
                            // residential4 ne smes da crtas pod"), and no hole showing the
                            // sky either ("i dalje vidim cudan pod")
                            if (Forecourt(plan, i, j, out float floor) &&
                                Lay(Paving, root, i * cell, j * cell, cell, cell, 0f, floor) != null) stood.Tiles++;
                            continue;
                        case ResidentialLot.Use.Park:
                            // the park brings its own grass
                            continue;
                        case ResidentialLot.Use.Subway:
                            // the stair goes down through here: nothing over it
                            continue;
                        case ResidentialLot.Use.Yard:
                        case ResidentialLot.Use.Court:
                        case ResidentialLot.Use.Paved:
                            // concrete, never grass (the user's call, 2026-08-27)
                            tile = Paving;
                            break;
                        case ResidentialLot.Use.Cafe:
                            // the storefront with a sunken floor of its own gets no tile
                            // under its foot; the paved ground round it is paved
                            // any part of the foot in the cell, not just its middle: a 16 m
                            // diner in a 20 m gap runs a metre of sunken wall into the
                            // fourth cell, and a slab laid over that roofs it
                            if (cafes.Any(cafe => cafe.Sunk &&
                                cafe.Foot.Overlaps(new Rect(i * cell, j * cell, cell, cell)))) continue;
                            tile = Paving;
                            break;
                        case ResidentialLot.Use.Verge:
                            Verge(plan, root, i, j, stood);
                            continue;
                        case ResidentialLot.Use.Drive:
                            // the mouth is plain tarmac: the arrow tile in the kerb line
                            // read as a road sign on a driveway (the user, 2026-08-27:
                            // "nemoj da koristis strelicu")
                            tile = Bare;
                            break;
                        case ResidentialLot.Use.Alley:
                            tile = Bare;
                            break;
                        case ResidentialLot.Use.Parking:
                            if (Bay(plan, root, laid, i, j, stalls, stood)) continue;
                            tile = Bare;
                            break;
                        default:
                            continue;                       // Empty: nothing invented here
                    }
                    if (Tile(tile, root, i, j, yaw) != null) stood.Tiles++;
                }
        }

        /// <summary>The level a forecourt cell's floor is laid at: the unit's own floor
        /// where the cell is a pit, the ground where it is not. False if no unit owns it.</summary>
        static bool Forecourt(ResidentialLot.Plan plan, int i, int j, out float floor)
        {
            floor = 0f;
            foreach (var spot in plan.Spots)
            {
                var turn = ResidentialLot.Turn.Of(spot.Unit, spot.Yaw);
                int u = i - spot.I, v = j - spot.J;
                if (u < 0 || v < 0 || u >= turn.CW || v >= turn.CD) continue;
                if (turn.Pit(u, v)) floor = Mathf.Min(0f, spot.Unit.Floor);
                return true;
            }
            return false;
        }

        /// <summary>
        /// A pair of painted bays, ten metres along the row and five deep, nosed to the
        /// aisle.
        ///
        /// The tile's four lines run across its five-metre depth, so tiles laid ALONG the
        /// aisle make a row of bays, and tiles stacked away from it make stripes the length
        /// of the lot - which is what the first car park was (the user, 2026-08-27: "zna se
        /// kako parking izgleda"). The row runs square to the aisle this cell touches; an odd
        /// cell at the end of a row is bare asphalt, not half a bay.
        ///
        /// The aisle is INSIDE the block. The mouth - the ring cell cut for the way in - is
        /// tarmac too, and read as an aisle it had a row of bays nosed to it, so a car sat
        /// in a stall whose only way in was across the pavement and out of the mouth's
        /// sightline (the user, 2026-08-27: "kako je ovaj auto usao ovde").
        /// </summary>
        static bool Bay(ResidentialLot.Plan plan, Transform root, bool[,] laid, int i, int j,
                        List<Stall> stalls, Stood stood)
        {
            bool Is(int x, int y, ResidentialLot.Use use) =>
                x >= 0 && y >= 0 && x < plan.W && y < plan.D && plan.Ground[x, y] == use;
            // no bay in a cell that touches a house: a car is longer than its stall and a
            // house hangs its fire escapes two metres past its wall (the user, 2026-08-27:
            // "zgrada i parking ne smeju da se preplicu"). That cell is bare tarmac
            bool ByWall(int x, int y)
            {
                for (int s = 0; s < 4; s++)
                    if (Is(x + ResidentialLot.Step[s, 0], y + ResidentialLot.Step[s, 1], ResidentialLot.Use.Building) ||
                        Is(x + ResidentialLot.Step[s, 0], y + ResidentialLot.Step[s, 1], ResidentialLot.Use.Forecourt))
                        return true;
                return false;
            }
            bool Free(int x, int y) => Is(x, y, ResidentialLot.Use.Parking) && !laid[x, y] && !ByWall(x, y);
            bool Inside(int x, int y) => x > 0 && y > 0 && x < plan.W - 1 && y < plan.D - 1;
            bool Aisle(int x, int y) => Inside(x, y) &&
                (Is(x, y, ResidentialLot.Use.Drive) || Is(x, y, ResidentialLot.Use.Alley));
            if (ByWall(i, j)) return false;

            float cell = ResidentialLot.Cell;
            float x0 = i * cell, z0 = j * cell;
            const int perTile = 3;                      // the tile's three stalls, 3.33 m each
            float pitch = cell * 2f / perTile;
            // both cells of the pair have the aisle on the SAME side: a pair laid off one
            // cell's aisle put the other cell's stall with a house at its tail
            bool south = Aisle(i, j - 1) && Aisle(i + 1, j - 1), north = Aisle(i, j + 1) && Aisle(i + 1, j + 1);
            if ((south || north) && Free(i + 1, j))
            {
                // the aisle north or south: the row runs east-west, the lines north-south,
                // and a car noses away from the aisle
                if (Lay(Bays, root, x0, z0, cell * 2f, cell, 0f) != null) stood.Tiles++;
                laid[i, j] = laid[i + 1, j] = true;
                int into = north ? 180 : 0;
                for (int n = 0; n < perTile; n++)
                    stalls.Add(new Stall(new Vector3(x0 + (n + 0.5f) * pitch, 0f, z0 + cell * 0.5f), into));
                return true;
            }
            bool west = Aisle(i - 1, j) && Aisle(i - 1, j + 1), east = Aisle(i + 1, j) && Aisle(i + 1, j + 1);
            if ((west || east) && Free(i, j + 1))
            {
                if (Lay(Bays, root, x0, z0, cell, cell * 2f, 90f) != null) stood.Tiles++;
                laid[i, j] = laid[i, j + 1] = true;
                int into = east ? 270 : 90;
                for (int n = 0; n < perTile; n++)
                    stalls.Add(new Stall(new Vector3(x0 + cell * 0.5f, 0f, z0 + (n + 0.5f) * pitch), into));
                return true;
            }
            return false;
        }

        /// <summary>
        /// The cars in the stalls: one stall in two (<see cref="Parked"/>), nosed in, out of
        /// the pool the core's own car parks draw on (<see cref="CoreRoads.PickCar"/> - the
        /// catalogue's road cars, the wrong decade and the marked liveries left out), stood
        /// the way the core stands them (<see cref="CoreRoads.InBay"/>).
        /// </summary>
        static void Cars(List<Stall> stalls, Transform root, System.Random rng,
                         System.Func<GameObject, Transform, GameObject> raise, Stood stood)
        {
            stood.Stalls = stalls.Count;
            if (stalls.Count == 0) return;
            var under = new GameObject("Parked").transform;
            under.SetParent(root, false);
            foreach (var stall in stalls)
            {
                if (!Chance(rng, Parked)) continue;
                var prefab = CoreRoads.PickCar(rng);
                if (prefab == null) return;
                var car = raise(prefab, under);
                if (car == null) continue;
                car.transform.SetPositionAndRotation(stall.At, Quaternion.Euler(0f, stall.Into, 0f));
                CoreRoads.InBay(car, stall.At, stall.Into, ResidentialLot.Cell);
                stood.Cars++;
            }
        }

        /// <summary>
        /// The pavement inside the block, where it edges a way cars use: paving, and a kerb
        /// along every side that faces the tarmac.
        ///
        /// This is the user's rule of 2026-08-26 made of tiles - a way put in between the
        /// houses is kerbed on every side, and a car never crosses a pavement to reach it.
        /// </summary>
        static void Verge(ResidentialLot.Plan plan, Transform root, int i, int j, Stood stood)
        {
            // ONE TILE A CELL. The kerb tile is the whole pavement tile with a kerb along one
            // edge (which is why CorePavement lays nothing under it), so a cell that edges
            // tarmac gets the kerb tile and NOT the plain paving as well - the two are
            // coplanar and flicker. Two adjacent sides on tarmac take the corner tile, which
            // wraps one corner of its cell the way the block's own ring turns it; a cell with
            // tarmac on opposite sides has no tile in the kit and keeps the kerb on the first.
            bool[] drives = new bool[4];
            int count = 0;
            for (int side = 0; side < 4; side++)
            {
                int x = i + ResidentialLot.Step[side, 0], y = j + ResidentialLot.Step[side, 1];
                if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                if (!ResidentialLot.Drives(plan.Ground[x, y])) continue;
                drives[side] = true;
                count++;
            }

            string tile = Paving;
            float yaw = 0f;
            if (count == 2 && (drives[0] != drives[2]))
            {
                // sides: 0 south, 1 east, 2 north, 3 west
                tile = KerbCorner;
                yaw = KerbYaw.Corner(drives[2], drives[1]);
            }
            else if (count > 0)
            {
                tile = Kerb;
                // the kerb faces the road, the same way the block's own ring faces the street
                int first = drives[0] ? 0 : drives[1] ? 1 : drives[2] ? 2 : 3;
                yaw = first switch { 0 => 180f, 1 => 90f, 2 => 0f, _ => 270f };
            }
            if (Tile(tile, root, i, j, yaw) != null) stood.Tiles++;
        }

        /// <summary>One cell of the ring, as <see cref="Ring"/> decided it. Every kerb laid
        /// is remembered, because the palms are planted on them afterwards - and not on a
        /// drain, a corner or a hole.</summary>
        static void Pavement(ResidentialLot.Plan plan, Transform root, int i, int j,
                             Dictionary<(int, int), RingTile> ring,
                             List<CorePavement.Kerbstone> kerbs, Stood stood)
        {
            if (!ring.TryGetValue((i, j), out var tile)) return;    // covered by a longer tile
            float cell = ResidentialLot.Cell;
            GameObject go;
            if (tile.Cells == 1) go = Tile(tile.Tile, root, i, j, tile.Yaw);
            else
            {
                // the long tile at its own width across the pavement (the dug-up one is
                // 5.06 m, its barriers lean over the kerb) and whole cells along it
                bool alongX = j == 0 || j == plan.D - 1;
                float across = Box(tile.Tile).size.z, along = cell * tile.Cells;
                go = Lay(tile.Tile, root, i * cell, j * cell,
                         alongX ? along : across, alongX ? across : along, tile.Yaw);
            }
            if (go == null) return;
            stood.Tiles++;
            if (tile.Tile == Drain) stood.Drains++;
            if (tile.Tile == Dug)
            {
                stood.Dug++;
                // This prefab is an OPEN HOLE with barriers, not decorative paving. Reserve
                // both cells of it before tables, lamps and other props are offered so none
                // can stand over the excavation.
                bool alongX = j == 0 || j == plan.D - 1;
                float across = Box(tile.Tile).size.z;
                Claim(new Rect(i * cell, j * cell,
                               alongX ? cell * tile.Cells : across,
                               alongX ? across : cell * tile.Cells));
            }
            if (!tile.Kerbed) return;
            kerbs.Add(new CorePavement.Kerbstone(new Vector3((i + 0.5f) * cell, 0f, (j + 0.5f) * cell), tile.Yaw));
        }

        // ------------------------------------------------------------------ the units

        /// <summary>
        /// A unit on its cells.
        ///
        /// It is placed by ARITHMETIC, not by measuring the instance: a unit's prefab was
        /// baked with its footprint running from its own origin, so where the turned
        /// footprint starts is known exactly - and measuring the instance instead would take
        /// the fire escapes and the eaves into the reckoning and set the building a metre
        /// up the street. The parks stand the same way: they are units too.
        /// </summary>
        static void Stand(ResidentialLot.Plan plan, Transform root, Stood stood)
        {
            int nth = 0;
            foreach (var spot in plan.Spots)
            {
                // A COLOURWAY PER BUILDING. Synty ships the city atlas three times over -
                // PolygonCity_0N_A, _B and _C, the same UVs over three different albedo maps
                // - and every unit was harvested in _A, so a quarter of six houses was also a
                // quarter of one brick. Dealt a colourway each, the same six read as
                // eighteen. Off the block's own shape and the unit's place in it, so it is
                // settled by the deal and not by the order things happen to be built in.
                int mix = unchecked((plan.W * 73856093) ^ (plan.D * 19349663) ^
                                    (spot.I * 83492791) ^ (spot.J * 486187739) ^
                                    (nth++ * 2038074743));
                var go = StandUnit(spot.Unit, spot.Yaw, spot.I, spot.J, root,
                                   ResidentialUnits.IsLot(spot.Unit) ? 0 : ((mix % 3) + 3) % 3);
                if (go == null) continue;
                if (ResidentialUnits.IsLot(spot.Unit)) stood.Parks++;
                else stood.Units++;
            }
        }

        static GameObject StandUnit(ResidentialUnit unit, int yaw, int i, int j, Transform root,
                                   int way = 0)
        {
            var go = Raise($"{Units}{unit.Name}.prefab", root);
            if (go == null) return null;
            Colourway(go, way);

            float cell = ResidentialLot.Cell;
            float w = unit.CW * cell, d = unit.CD * cell;
            float x = i * cell, z = j * cell;
            var offset = yaw switch
            {
                90 => new Vector3(0f, 0f, w),
                180 => new Vector3(w, 0f, d),
                270 => new Vector3(d, 0f, 0f),
                _ => Vector3.zero,
            };
            go.transform.SetPositionAndRotation(new Vector3(x, 0f, z) + offset, Quaternion.Euler(0f, yaw, 0f));
            go.name = $"{unit.Name} ({i},{j}) {yaw}";
            return go;
        }

        /// <summary>Where Synty keeps the other two albedo maps of the city atlas.</summary>
        const string CityAlts = "Assets/Synty/PolygonCity/Materials/Alts/";

        /// <summary>
        /// Put this building in one of the pack's three colourways.
        ///
        /// The _A maps carry an emission map - the lit windows - and _B and _C do not, so a
        /// tinted building's windows will not glow when the city has a night. Worth knowing
        /// before it does.
        /// </summary>
        static void Colourway(GameObject go, int way)
        {
            if (way <= 0) return;                       // _A: what the prefab was baked in
            string letter = way == 1 ? "B" : "C";
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = mr.sharedMaterials;
                bool swapped = false;
                for (int k = 0; k < mats.Length; k++)
                {
                    var mat = mats[k];
                    if (mat == null) continue;
                    if (!mat.name.StartsWith("PolygonCity_") || !mat.name.EndsWith("_A")) continue;
                    var alt = DemoAssetLoad.Load<Material>(
                        CityAlts + mat.name.Substring(0, mat.name.Length - 1) + letter + ".mat");
                    if (alt == null) continue;
                    mats[k] = alt;
                    swapped = true;
                }
                if (swapped) mr.sharedMaterials = mats;
            }
        }

        // ------------------------------------------------------------------ the subway

        /// <summary>The cell <paramref name="k"/> in from this street side, at
        /// <paramref name="at"/> along it - the recipe's own reckoning.</summary>
        static (int, int) Into(ResidentialLot.Plan plan, int side, int at, int k) => side switch
        {
            0 => (at, ResidentialLot.Walk + k),
            2 => (at, plan.D - ResidentialLot.Walk - 1 - k),
            1 => (plan.W - ResidentialLot.Walk - 1 - k, at),
            _ => (ResidentialLot.Walk + k, at),
        };

        /// <summary>
        /// The subway entrance in the column the recipe kept for it: the mouth (the prefab's
        /// +z end, under the glass canopy) a step in from the pavement line, the stair
        /// running down into the block. Placed by MEASURING the turned instance, because
        /// the piece is pivoted on a corner and 15 m long.
        /// </summary>
        static void Subway(ResidentialLot.Plan plan, Transform root, Stood stood)
        {
            if (plan.Subway == null || plan.SubwayAt < 0) return;
            int side = plan.Subway.Side;
            var go = Raise(SubwayPath, root);
            if (go == null) return;

            // the mouth to the street: +z goes to -z at 180, +x at 90, -x at 270
            float yaw = side switch { 0 => 180f, 1 => 90f, 2 => 0f, _ => 270f };
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            if (!WorldBox(go, out var box)) return;

            float cell = ResidentialLot.Cell;
            const float step = 0.1f;
            var (i, j) = Into(plan, side, plan.SubwayAt, 0);
            float cx = (i + 0.5f) * cell, cz = (j + 0.5f) * cell;
            var pos = go.transform.position;
            switch (side)
            {
                case 0: pos.z += j * cell + step - box.min.z; pos.x += cx - box.center.x; break;
                case 2: pos.z += (j + 1) * cell - step - box.max.z; pos.x += cx - box.center.x; break;
                case 1: pos.x += (i + 1) * cell - step - box.max.x; pos.z += cz - box.center.z; break;
                default: pos.x += i * cell + step - box.min.x; pos.z += cz - box.center.z; break;
            }
            go.transform.position = pos;
            go.name = "subway entrance";
            if (WorldBox(go, out var stood_box))
                Claim(new Rect(stood_box.min.x, stood_box.min.z, stood_box.size.x, stood_box.size.z));
            stood.Subway = true;
        }

        // ------------------------------------------------------------------ the cafe

        sealed class CafeSpot
        {
            public string Name;
            public string Path;              // a kit storefront, or null
            public ResidentialUnit Unit;     // a harvested storefront, or null
            public int Yaw, I, J;            // the harvested one's cells
            public int Perp = -1;            // the second street at the block corner it stands on, if any
            public bool CornerLow;           // that corner is at the low end of the gap
            public float X, Z, YawF;         // the kit one's middle and turn
            public Rect Foot;
            public bool Sunk;
        }

        /// <summary>
        /// The storefront in the gap the plan kept for it: drawn from the pool of every one
        /// that FITS the gap - the kit ones measured (<see cref="Composer.Foot"/>, front off
        /// the mesh through <see cref="Composer.FrontYaw"/>), the harvested ones read off the
        /// units table - turned to face its street with its front a step in from the
        /// pavement line, and stood at one END of the gap, off the neighbour by what the
        /// neighbour reaches out: a brownstone's fire escape hangs 1.8 m past its wall, and
        /// the first cut stood the coffee shop's terrace in it (the user, 2026-08-27:
        /// "kafic i zgrada pored se preklapaju").
        /// </summary>
        static CafeSpot CafeOf(ResidentialLot.Plan plan, ResidentialLot.Gap gap,
                               System.Random rng, Stood stood)
        {
            if (gap == null) return null;

            float cell = ResidentialLot.Cell;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int n = 0; n < gap.Run; n++)
                for (int k = 0; k < ResidentialLot.CafeDeep; k++)
                {
                    var (i, j) = Into(plan, gap.Side, gap.At + n, k);
                    if (i < 0 || j < 0 || i >= plan.W || j >= plan.D) continue;
                    if (plan.Ground[i, j] != ResidentialLot.Use.Cafe) continue;
                    minX = Mathf.Min(minX, i * cell); maxX = Mathf.Max(maxX, (i + 1) * cell);
                    minZ = Mathf.Min(minZ, j * cell); maxZ = Mathf.Max(maxZ, (j + 1) * cell);
                }
            if (minX > maxX) return null;

            bool alongX = gap.Side == 0 || gap.Side == 2;
            float along = alongX ? maxX - minX : maxZ - minZ;
            float deep = alongX ? maxZ - minZ : maxX - minX;
            // at one END of the gap, not its middle: centred, a 5.8 m shop in a 10 m gap
            // leaves 2 m either side and no room for a table on either; at the end it leaves
            // one patio of 4 m, which seats a row. The end at the block's corner if the gap
            // reaches one - a cafe on the corner gets its terraces round it - else the seed's
            int length = alongX ? plan.W : plan.D;
            bool lowCorner = gap.At == ResidentialLot.Walk && plan.Street[alongX ? 3 : 0];
            bool highCorner = gap.At + gap.Run == length - ResidentialLot.Walk && plan.Street[alongX ? 1 : 2];
            bool low = lowCorner && highCorner ? rng.Next(2) == 0 : lowCorner || (!highCorner && rng.Next(2) == 0);
            // clear of the neighbour's wall, plus whatever the neighbour hangs out. It was a
            // hand's breadth (0.1 m) and that read as a shop glued to the house beside it
            // (the user, 2026-08-28); it is a stride now
            float flankLow = Clear + Reach(plan, gap, true), flankHigh = Clear + Reach(plan, gap, false);
            const float step = 0.3f;    // the front a step in from the pavement line

            var picks = new List<CafeSpot>();
            foreach (var path in Kit)
            {
                // FrontYaw turns the front to +x; the street lies -z, +x, +z, -x of the gap,
                // and a yaw is a CLOCKWISE turn from above (+x goes to -z at 90, -x at 180,
                // +z at 270)
                float yaw = FrontYaw(path) + gap.Side switch { 0 => 90f, 1 => 0f, 2 => 270f, _ => 180f };
                var foot = Foot(path, yaw);
                float footAlong = alongX ? foot.x : foot.y, footDeep = alongX ? foot.y : foot.x;
                if (footDeep > deep + 0.01f) continue;
                float flank = low ? flankLow : flankHigh;
                if (footAlong + flank > along + 0.01f)
                {
                    // try the other end before giving up on it - and STAY there if it fits:
                    // the flip is not undone for the next candidate, so once one shop has
                    // found the seed's end too tight the rest of the kit, and the harvested
                    // storefronts after it, start from the other end. That is the order the
                    // block was judged in on 2026-08-27, and it is kept on purpose
                    low = !low;
                    flank = low ? flankLow : flankHigh;
                    if (footAlong + flank > along + 0.01f) { low = !low; continue; }
                }
                float x = alongX ? (low ? minX + flank + foot.x * 0.5f : maxX - flank - foot.x * 0.5f)
                                 : (minX + maxX) * 0.5f;
                float z = alongX ? (minZ + maxZ) * 0.5f
                                 : (low ? minZ + flank + foot.y * 0.5f : maxZ - flank - foot.y * 0.5f);
                switch (gap.Side)
                {
                    case 0: z = minZ + step + foot.y * 0.5f; break;
                    case 2: z = maxZ - step - foot.y * 0.5f; break;
                    case 1: x = maxX - step - foot.x * 0.5f; break;
                    default: x = minX + step + foot.x * 0.5f; break;
                }
                bool kitCorner = low ? gap.At == ResidentialLot.Walk
                                     : gap.At + gap.Run == (alongX ? plan.W : plan.D) - ResidentialLot.Walk;
                int kitPerp = low ? (alongX ? 3 : 0) : (alongX ? 1 : 2);
                picks.Add(new CafeSpot
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(path),
                    Path = path, X = x, Z = z, YawF = yaw,
                    Foot = new Rect(x - foot.x * 0.5f, z - foot.y * 0.5f, foot.x, foot.y),
                    Sunk = Box(path).min.y < SunkFloor,
                    Perp = kitCorner && plan.Street[kitPerp] ? kitPerp : -1,
                    CornerLow = low,
                });
            }

            foreach (var unit in ResidentialUnits.Storefronts)
            {
                var options = new List<CafeSpot>();

                // A corner shop at the block's corner: both its faces on the two streets,
                // and the rest of the gap its patio (the user, 2026-08-27: "nek bude
                // okrenut ka cosku kad je u ovom uskom i napuni ceo plato tim stolovima").
                foreach (bool end in new[] { true, false })
                {
                    bool atCorner = end ? gap.At == ResidentialLot.Walk
                                        : gap.At + gap.Run == length - ResidentialLot.Walk;
                    int perp = end ? (alongX ? 3 : 0) : (alongX ? 1 : 2);
                    if (!atCorner || !plan.Street[perp]) continue;
                    for (int yaw = 0; yaw < 360; yaw += 90)
                    {
                        var turn = ResidentialLot.Turn.Of(unit, yaw);
                        if (!turn.Face(gap.Side) || !turn.Face(perp)) continue;
                        var spot = Standing(plan, gap, unit, turn, yaw, end, 0f);
                        if (spot == null) continue;
                        spot.Perp = perp;
                        spot.CornerLow = end;
                        options.Add(spot);
                    }
                }

                // Otherwise its front to the street and every other face it has onto the
                // patio or the pavement - never at a neighbour's wall. The first cut only
                // asked that of the flank at the end it stood at, and a pub that filled its
                // run showed its second shopfront to the brownstone's stoops behind (the
                // user, 2026-08-27: "pizza pub je i dalje okrenut cudno").
                if (options.Count == 0)
                {
                    int front = FrontSide(unit);
                    if (front < 0) continue;
                    // the turned unit's side gap.Side shows the unturned side (gap.Side + yaw/90)
                    int yaw = 90 * ((front - gap.Side + 4) % 4);
                    var turn = ResidentialLot.Turn.Of(unit, yaw);
                    foreach (bool end in new[] { low, !low })
                    {
                        var spot = Standing(plan, gap, unit, turn, yaw, end, end ? flankLow : flankHigh);
                        if (spot == null || !Flanks(plan, turn, spot.I, spot.J, gap.Side)) continue;
                        options.Add(spot);
                        break;
                    }
                }
                if (options.Count > 0) picks.Add(options[rng.Next(options.Count)]);
            }

            if (picks.Count == 0)
            {
                string failed = $"no restaurant fits the {along:0} x {deep:0} m gap";
                stood.Cafe = stood.Cafe.Length == 0 ? failed : stood.Cafe + "; " + failed;
                return null;
            }
            var restaurants = picks.Where(p =>
                p.Name.IndexOf("coffee", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.Name.IndexOf("diner", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.Name.IndexOf("burger", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.Name.IndexOf("pizza", System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (restaurants.Count > 0) picks = restaurants;
            return picks[rng.Next(picks.Count)];
        }

        /// <summary>
        /// A harvested storefront stood at one end of the gap, front row, whole cells - or
        /// null if it does not fit there. <paramref name="reach"/> is what the neighbour at
        /// that end hangs into the gap: a storefront whose flank is against a wall wants
        /// nothing hanging over it.
        /// </summary>
        static CafeSpot Standing(ResidentialLot.Plan plan, ResidentialLot.Gap gap, ResidentialUnit unit,
                                 ResidentialLot.Turn turn, int yaw, bool low, float reach)
        {
            bool alongX = gap.Side == 0 || gap.Side == 2;
            int cellsAlong = alongX ? turn.CW : turn.CD, cellsDeep = alongX ? turn.CD : turn.CW;
            // A WHOLE CELL OF AIR TO THE NEIGHBOUR. A storefront that filled its gap wall to
            // wall stood in the houses either side of it - their eaves, their fire escapes
            // and their stoops all reach past their own wall (the user, 2026-08-28: "kafici
            // se dodaju tik uz zgrade... se preplicu uz zgrade").
            if (cellsAlong > gap.Run - 1 || cellsDeep > ResidentialLot.CafeDeep) return null;
            if (reach > 0.7f) return null;
            // the spare cell goes against the neighbour that hangs out furthest
            int spare = gap.Run - cellsAlong;
            int start = low ? gap.At + Mathf.Max(1, spare / 2) : gap.At + spare - Mathf.Max(1, spare / 2);
            int i, j;
            switch (gap.Side)
            {
                case 0: i = start; j = ResidentialLot.Walk; break;
                case 2: i = start; j = plan.D - ResidentialLot.Walk - cellsDeep; break;
                case 1: i = plan.W - ResidentialLot.Walk - cellsDeep; j = start; break;
                default: i = ResidentialLot.Walk; j = start; break;
            }
            float cell = ResidentialLot.Cell;
            return new CafeSpot
            {
                Name = unit.Name, Unit = unit, Yaw = yaw, I = i, J = j,
                Foot = new Rect(i * cell, j * cell, turn.CW * cell, turn.CD * cell),
            };
        }

        /// <summary>Does every face of the turned unit other than its front look at open
        /// ground - the patio, the pavement, a yard - and never at another building?</summary>
        static bool Flanks(ResidentialLot.Plan plan, ResidentialLot.Turn turn, int i, int j, int front)
        {
            for (int s = 0; s < 4; s++)
            {
                if (s == front || !turn.Face(s)) continue;
                for (int u = 0; u < turn.CW; u++)
                    for (int v = 0; v < turn.CD; v++)
                    {
                        if (!turn.Filled(u, v)) continue;
                        int x = i + u + ResidentialLot.Step[s, 0], y = j + v + ResidentialLot.Step[s, 1];
                        if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                        var use = plan.Ground[x, y];
                        if (use != ResidentialLot.Use.Walkway && use != ResidentialLot.Use.Cafe &&
                            use != ResidentialLot.Use.Paved && use != ResidentialLot.Use.Yard &&
                            use != ResidentialLot.Use.Court) return false;
                    }
            }
            return true;
        }

        /// <summary>The side a harvested storefront shows the street: of its faces, the one
        /// with the longest frontage, then the most shopfronts.</summary>
        static int FrontSide(ResidentialUnit unit)
        {
            int best = -1, bestLong = -1, bestShops = -1;
            for (int s = 0; s < 4; s++)
            {
                if (!unit.Face[s]) continue;
                int along = s == 0 || s == 2 ? unit.CW : unit.CD;
                if (along < bestLong || (along == bestLong && unit.Shops[s] <= bestShops)) continue;
                best = s; bestLong = along; bestShops = unit.Shops[s];
            }
            return best;
        }

        /// <summary>How far the neighbour at this end of the gap reaches into it, past its
        /// own cells: its fire escapes and awnings, off the units table.</summary>
        static float Reach(ResidentialLot.Plan plan, ResidentialLot.Gap gap, bool low)
        {
            int at = low ? gap.At - 1 : gap.At + gap.Run;
            var (i, j) = Into(plan, gap.Side, at, 0);
            if (i < 0 || j < 0 || i >= plan.W || j >= plan.D) return 0f;
            bool alongX = gap.Side == 0 || gap.Side == 2;
            int facing = low ? (alongX ? 1 : 2) : (alongX ? 3 : 0);
            foreach (var spot in plan.Spots)
            {
                var turn = ResidentialLot.Turn.Of(spot.Unit, spot.Yaw);
                int u = i - spot.I, v = j - spot.J;
                if (u < 0 || v < 0 || u >= turn.CW || v >= turn.CD || !turn.Filled(u, v)) continue;
                return Mathf.Max(0f, turn.Over(facing));
            }
            return 0f;
        }

        static bool CafeStand(CafeSpot cafe, Transform root, Stood stood)
        {
            GameObject go;
            if (cafe.Unit != null)
            {
                go = StandUnit(cafe.Unit, cafe.Yaw, cafe.I, cafe.J, root);
                if (go != null) Claim(cafe.Foot);
            }
            else go = Building(cafe.Path, root, cafe.X, cafe.Z, cafe.YawF, 1f);
            if (go == null)
            {
                string failed = $"no {cafe.Name}: no room";
                stood.Cafe = stood.Cafe.Length == 0 ? failed : stood.Cafe + "; " + failed;
                return false;
            }
            go.name = $"{cafe.Name} (cafe)";
            stood.Cafe = stood.Cafe.Length == 0 ? cafe.Name : stood.Cafe + " + " + cafe.Name;
            return true;
        }

        /// <summary>
        /// A court is not leftover paving. Its parks and passages are placed by the plan;
        /// this gives the remaining court a Palm City civic centre and several small
        /// seating/green anchors. It runs after buildings and restaurants, so Composer's
        /// booked footprints reject anything that would crowd them.
        /// </summary>
        static void Courtyard(ResidentialLot.Plan plan, Transform root, System.Random rng,
                              List<Vector3> standing, Stood stood)
        {
            if (plan.Klass != ResidentialLot.Klass.Court) return;

            float cell = ResidentialLot.Cell;
            float cx = plan.W * cell * 0.5f, cz = plan.D * cell * 0.5f;
            var cells = new List<(int I, int J, float D2)>();
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    if (plan.Ground[i, j] != ResidentialLot.Use.Court) continue;
                    float x = (i + 0.5f) * cell, z = (j + 0.5f) * cell;
                    cells.Add((i, j, (x - cx) * (x - cx) + (z - cz) * (z - cz)));
                }
            if (cells.Count == 0) return;

            var pen = new GameObject("courtyard programme").transform;
            pen.SetParent(root, false);
            bool Put(string path, int i, int j, float yaw, float room, bool seat = false)
            {
                float x = (i + 0.5f) * cell, z = (j + 0.5f) * cell;
                if (Prop(path, pen, x, z, yaw, room, Deck) == null) return false;
                stood.Props++;
                if (seat) stood.Benches++;
                standing.Add(new Vector3(x, 0f, z));
                return true;
            }

            // One legible centre, then a loose ring. The modulo spreads candidates without
            // turning the whole court into furniture storage.
            foreach (var c in cells.OrderBy(c => c.D2))
                if (Put(CourtFountain, c.I, c.J, 90f * rng.Next(4), 1.25f)) break;

            int benches = 0;
            foreach (var c in cells.OrderBy(c => c.D2).ThenBy(_ => rng.Next()))
            {
                if (benches >= 8 || (c.I * 3 + c.J * 5) % 4 != 0) continue;
                float yaw = Mathf.Atan2(cx - (c.I + 0.5f) * cell,
                                       cz - (c.J + 0.5f) * cell) * Mathf.Rad2Deg;
                if (Put(CourtPlanterBench, c.I, c.J, yaw, 1.15f, seat: true)) benches++;
            }

            int planters = 0;
            foreach (var c in cells.OrderByDescending(c => c.D2).ThenBy(_ => rng.Next()))
            {
                if (planters >= 6 || (c.I + c.J * 2) % 3 != 0) continue;
                if (Put(CourtPlanter, c.I, c.J, 90f * rng.Next(4), 1.1f)) planters++;
            }

            int tables = 0;
            foreach (var c in cells.OrderBy(_ => rng.Next()))
            {
                if (tables >= 3) break;
                if (Put(CourtTable, c.I, c.J, 90f * rng.Next(4), 1.2f)) tables++;
            }
        }

        /// <summary>
        /// The privacy depth between a house and its alley is useful shared space, not a
        /// second building row. Each connected yard gets one restrained Palm City seating
        /// cluster, placed only in cells with a full-cell buffer from residential walls.
        /// Cafe and parking ground are different uses and can never be selected here.
        /// </summary>
        static void SharedYards(ResidentialLot.Plan plan, Transform root, System.Random rng,
                                List<Vector3> standing, Stood stood)
        {
            if (plan.Klass != ResidentialLot.Klass.Block) return;

            bool[,] seen = new bool[plan.W, plan.D];
            var components = new List<List<(int I, int J)>>();
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    if (seen[i, j] || plan.Ground[i, j] != ResidentialLot.Use.Yard) continue;
                    var part = new List<(int, int)>();
                    var todo = new Queue<(int, int)>();
                    todo.Enqueue((i, j));
                    seen[i, j] = true;
                    while (todo.Count > 0)
                    {
                        var cell = todo.Dequeue();
                        part.Add(cell);
                        for (int side = 0; side < 4; side++)
                        {
                            int x = cell.Item1 + ResidentialLot.Step[side, 0];
                            int y = cell.Item2 + ResidentialLot.Step[side, 1];
                            if (x < 0 || y < 0 || x >= plan.W || y >= plan.D || seen[x, y]) continue;
                            if (plan.Ground[x, y] != ResidentialLot.Use.Yard) continue;
                            seen[x, y] = true;
                            todo.Enqueue((x, y));
                        }
                    }
                    if (part.Count >= 3) components.Add(part);
                }
            if (components.Count == 0) return;

            var pen = new GameObject("shared yards").transform;
            pen.SetParent(root, false);
            float unit = ResidentialLot.Cell;
            foreach (var part in components.OrderByDescending(p => p.Count).Take(4))
            {
                bool Clear(int i, int j)
                {
                    for (int side = 0; side < 4; side++)
                    {
                        int x = i + ResidentialLot.Step[side, 0], y = j + ResidentialLot.Step[side, 1];
                        if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) return false;
                        var use = plan.Ground[x, y];
                        if (use == ResidentialLot.Use.Building || use == ResidentialLot.Use.Forecourt ||
                            use == ResidentialLot.Use.Alley || use == ResidentialLot.Use.Drive) return false;
                    }
                    return true;
                }

                float ci = (float)part.Average(c => c.I), cj = (float)part.Average(c => c.J);
                var candidates = part.Where(c => Clear(c.I, c.J))
                    .OrderBy(c => (c.I - ci) * (c.I - ci) + (c.J - cj) * (c.J - cj))
                    .ThenBy(_ => rng.Next()).ToList();
                if (candidates.Count == 0) continue;

                bool Put(string path, (int I, int J) cell, float yaw, float room, bool bench)
                {
                    float x = (cell.I + 0.5f) * unit, z = (cell.J + 0.5f) * unit;
                    if (Prop(path, pen, x, z, yaw, room, Deck) == null) return false;
                    stood.Props++;
                    if (bench) stood.Benches++;
                    standing.Add(new Vector3(x, 0f, z));
                    return true;
                }

                var anchor = candidates[0];
                Put(CourtPlanterBench, anchor, 90f * rng.Next(4), 1.1f, bench: true);
                if (part.Count >= 6 && candidates.Count > 1)
                {
                    var table = candidates.OrderByDescending(c =>
                        (c.I - anchor.I) * (c.I - anchor.I) + (c.J - anchor.J) * (c.J - anchor.J)).First();
                    Put(Picnic, table, 90f * rng.Next(2), 1.1f, bench: false);
                }

                int greens = 0;
                foreach (var green in candidates.OrderByDescending(c =>
                    (c.I - anchor.I) * (c.I - anchor.I) + (c.J - anchor.J) * (c.J - anchor.J)))
                {
                    int di = green.I - anchor.I, dj = green.J - anchor.J;
                    if (greens >= 2 || di * di + dj * dj < 2) continue;
                    if (Put(CourtPlanter, green, 90f * rng.Next(4), 1.05f, bench: false)) greens++;
                }
            }
        }

        /// <summary>
        /// The cafe's plateau: its own gap's cells and every paved cell joined to them -
        /// the whole end of a row block, both gaps together. The patio fills all of it
        /// (the user, 2026-08-27: "ispuni citav patio na toj strani bloka").
        /// </summary>
        static bool[,] Plateau(ResidentialLot.Plan plan, ResidentialLot.Gap gap)
        {
            var on = new bool[plan.W, plan.D];
            var todo = new Queue<(int, int)>();
            for (int n = 0; n < gap.Run; n++)
                for (int k = 0; k < ResidentialLot.CafeDeep; k++)
                {
                    var (i, j) = Into(plan, gap.Side, gap.At + n, k);
                    if (i < 0 || j < 0 || i >= plan.W || j >= plan.D) continue;
                    if (plan.Ground[i, j] != ResidentialLot.Use.Cafe) continue;
                    on[i, j] = true;
                    todo.Enqueue((i, j));
                }
            while (todo.Count > 0)
            {
                var (i, j) = todo.Dequeue();
                for (int s = 0; s < 4; s++)
                {
                    int x = i + ResidentialLot.Step[s, 0], y = j + ResidentialLot.Step[s, 1];
                    if (x < 0 || y < 0 || x >= plan.W || y >= plan.D || on[x, y]) continue;
                    if (plan.Ground[x, y] != ResidentialLot.Use.Paved) continue;
                    on[x, y] = true;
                    todo.Enqueue((x, y));
                }
            }
            return on;
        }

        static bool Bounds(bool[,] on, out float minX, out float minZ, out float maxX, out float maxZ)
        {
            float cell = ResidentialLot.Cell;
            minX = float.MaxValue; minZ = float.MaxValue; maxX = float.MinValue; maxZ = float.MinValue;
            for (int i = 0; i < on.GetLength(0); i++)
                for (int j = 0; j < on.GetLength(1); j++)
                {
                    if (!on[i, j]) continue;
                    minX = Mathf.Min(minX, i * cell); maxX = Mathf.Max(maxX, (i + 1) * cell);
                    minZ = Mathf.Min(minZ, j * cell); maxZ = Mathf.Max(maxZ, (j + 1) * cell);
                }
            return minX <= maxX;
        }

        /// <summary>The gap's cells as a rectangle, in metres.</summary>
        static bool GapRect(ResidentialLot.Plan plan, ResidentialLot.Use use,
                            out float minX, out float minZ, out float maxX, out float maxZ)
        {
            float cell = ResidentialLot.Cell;
            minX = float.MaxValue; minZ = float.MaxValue; maxX = float.MinValue; maxZ = float.MinValue;
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    if (plan.Ground[i, j] != use) continue;
                    minX = Mathf.Min(minX, i * cell); maxX = Mathf.Max(maxX, (i + 1) * cell);
                    minZ = Mathf.Min(minZ, j * cell); maxZ = Mathf.Max(maxZ, (j + 1) * cell);
                }
            return minX <= maxX;
        }

        /// <summary>The yaw that points a prop's +z front at this street.</summary>
        static float ToStreet(int side) => side switch { 0 => 180f, 1 => 90f, 2 => 0f, _ => 270f };

        /// <summary>
        /// The patio: what the storefront's gap has left beside it, set with the pier's own
        /// tables and chairs (the user, 2026-08-27: "patio pored kafica bi mogao da ima
        /// stolove i klupe"), a bench along its back line facing the street. Everything is
        /// booked, so nothing stands in the storefront's foot or in another table's room;
        /// what does not fit is refused and counted, never crammed.
        /// </summary>
        static void Patio(ResidentialLot.Plan plan, ResidentialLot.Gap gap, CafeSpot cafe,
                          Transform root, System.Random rng,
                          List<Vector3> standing, Stood stood)
        {
            if (gap == null) return;
            var plateau = Plateau(plan, gap);
            if (!Bounds(plateau, out float minX, out float minZ, out float maxX, out float maxZ)) return;
            var pen = new GameObject("patio").transform;
            pen.SetParent(root, false);
            float cell = ResidentialLot.Cell;
            bool On(float x, float z)
            {
                int i = Mathf.FloorToInt(x / cell), j = Mathf.FloorToInt(z / cell);
                return i >= 0 && j >= 0 && i < plan.W && j < plan.D && plateau[i, j];
            }

            // the patio in its own frame: "along" runs with the street, "in" runs away from it
            bool alongX = gap.Side == 0 || gap.Side == 2;
            float along0 = alongX ? minX : minZ, along1 = alongX ? maxX : maxZ;
            float in0, in1, inward;      // in0 at the pavement line, in1 at the back
            switch (gap.Side)
            {
                case 0: in0 = minZ; in1 = maxZ; inward = 1f; break;
                case 2: in0 = maxZ; in1 = minZ; inward = -1f; break;
                case 1: in0 = maxX; in1 = minX; inward = -1f; break;
                default: in0 = minX; in1 = maxX; inward = 1f; break;
            }
            Vector2 At(float along, float @in) => alongX ? new Vector2(along, @in) : new Vector2(@in, along);
            float toStreet = ToStreet(gap.Side);

            // The patio is the whole of the gap the storefront does not stand on - beside
            // it and behind it - set out in loose rows, every one a step off the
            // storefront's walls (the user, 2026-08-27: "napuni ceo plato tim stolovima",
            // "odmakni stolove od kafica malo"). The room a table books is the table AND
            // its chairs, so none lands with a chair in the wall. Row phase, spacing and
            // angle all vary; a cafe patio is not a parade ground.
            const float off = 0.8f;     // off the shop's walls
            var keepOut = new Rect(cafe.Foot.xMin - off, cafe.Foot.yMin - off,
                                   cafe.Foot.width + 2f * off, cafe.Foot.height + 2f * off);
            // the table's whole width, reached either side of its middle: the room a table
            // books is the table AND the chairs round it, which is about a width again
            // from the middle to a chair's far edge - the spacing the patio was judged at
            float width = Box(CafeTable).size.x;
            float depth = Mathf.Abs(in1 - in0);
            float rowStep = Between(rng, TableRowMin, TableRowMax);
            int rows = Mathf.Max(1, Mathf.FloorToInt((depth - 1.0f) / rowStep));
            float firstIn = 1.8f + Between(rng, -0.15f, 0.45f);
            int offered = 0;
            for (int r = 0; r < rows; r++)
            {
                float @in = in0 + inward * (firstIn + r * rowStep + Between(rng, -TableJitter, TableJitter));
                float a = along0 + width + Between(rng, 0f, 1.1f);
                while (a + width <= along1 + 0.01f)
                {
                    float scattered = a + Between(rng, -TableJitter, TableJitter);
                    a += Between(rng, TableAlongMin, TableAlongMax);
                    var at = At(scattered, @in);
                    // on the plateau, table and chairs alike - and off the cafe's walls
                    if (!On(at.x - width, at.y - width) || !On(at.x + width, at.y - width) ||
                        !On(at.x - width, at.y + width) || !On(at.x + width, at.y + width)) continue;
                    if (keepOut.Overlaps(new Rect(at.x - width, at.y - width, 2f * width, 2f * width))) continue;
                    if (offered++ > 0 && !Chance(rng, TableKeep)) continue;
                    if (!Table(pen, at.x, at.y, rng, Chance(rng, ShadeOdds), stood)) continue;
                    standing.Add(new Vector3(at.x, 0f, at.y));
                }
            }
            // no bench among the tables: a bench beside a cafe's own chairs is furniture
            // for its own sake (the user, 2026-08-27: "sta ce ti klupe tik uz kafic")
        }

        /// <summary>
        /// The terraces on the pavement: along the storefront's own front, and - when it
        /// stands at the block's corner - round that corner on both streets, from the
        /// corner to the storefront's far end on one and to the back of its gap on the
        /// other (the user, 2026-08-27, of the row block: "narokaj stolove svuda oko kafica
        /// na tom cosku").
        /// </summary>
        static void Terraces(ResidentialLot.Plan plan, ResidentialLot.Gap gap, CafeSpot cafe,
                             Transform root, System.Random rng,
                             List<Vector3> standing, Stood stood)
        {
            float cell = ResidentialLot.Cell;
            bool alongX = gap.Side == 0 || gap.Side == 2;
            float foot0 = alongX ? cafe.Foot.xMin : cafe.Foot.yMin, foot1 = alongX ? cafe.Foot.xMax : cafe.Foot.yMax;
            if (cafe.Perp < 0)
            {
                Terrace(plan, gap.Side, foot0, foot1, root, rng, standing, stood);
                return;
            }
            // the corner cell's own inner corner is where the two runs meet: each run
            // starts a little short of the other street's pavement line
            float length = (alongX ? plan.W : plan.D) * cell;
            const float into = 1.6f;
            float cornerEdge = cafe.CornerLow ? ResidentialLot.Walk * cell - into : length - ResidentialLot.Walk * cell + into;
            Terrace(plan, gap.Side, Mathf.Min(cornerEdge, foot0), Mathf.Max(cornerEdge, foot1), root, rng, standing, stood);

            // and round the corner, the whole length of the plateau along the other street
            var plateau = Plateau(plan, gap);
            if (!Bounds(plateau, out float minX, out float minZ, out float maxX, out float maxZ)) return;
            float across = (alongX ? plan.D : plan.W) * cell;
            bool nearEdge = gap.Side == 0 || gap.Side == 3;     // the gap's street is at the low end of the other axis
            float lo = alongX ? minZ : minX, hi = alongX ? maxZ : maxX;
            float p0 = nearEdge ? ResidentialLot.Walk * cell - into : lo;
            float p1 = nearEdge ? hi : across - ResidentialLot.Walk * cell + into;
            Terrace(plan, cafe.Perp, p0, p1, root, rng, standing, stood);
        }

        /// <summary>A cafe table with its four chairs, and one of the three Palm City
        /// umbrella colourways if asked. The room booked is the table AND the chairs.</summary>
        static bool Table(Transform pen, float x, float z, System.Random rng, bool shade, Stood stood,
                          float y = 0f)
        {
            const float room = 2f;
            float chair = Box(CafeChair).size.x * 0.5f + Box(CafeTable).size.x * 0.5f + 0.05f;
            float groupYaw = Between(rng, 0f, 360f);
            var table = Prop(CafeTable, pen, x, z, groupYaw, room, y);
            if (table == null) return false;
            stood.Tables++;
            stood.Props++;
            for (int k = 0; k < 4; k++)
            {
                float yaw = groupYaw + k * 90f;
                var spot = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, chair);
                Sit(CafeChair, pen, x + spot.x, z + spot.z, yaw + 180f + Between(rng, -15f, 15f), y);
            }
            if (shade) Sit(Any(Umbrellas, rng), pen, x, z, Between(rng, 0f, 360f), y);
            return true;
        }

        /// <summary>
        /// The terrace: a row of tables ON THE PAVEMENT in front of the storefront, against
        /// its own front and no wider than it (the user, 2026-08-27, of the pizza pub:
        /// "ispred ovoga dodaj stolove"). The tables sit in the lane by the wall; the kerb
        /// lane stays clear for the lamps and the meters, and the palms are planted round
        /// them afterwards.
        /// </summary>
        static void Terrace(ResidentialLot.Plan plan, int side, float a0, float a1, Transform root, System.Random rng,
                            List<Vector3> standing, Stood stood)
        {
            var pen = new GameObject("terrace").transform;
            pen.SetParent(root, false);

            float cell = ResidentialLot.Cell;
            bool alongX = side == 0 || side == 2;
            float line = side switch
            {
                0 => ResidentialLot.Walk * cell,
                2 => (plan.D - ResidentialLot.Walk) * cell,
                1 => (plan.W - ResidentialLot.Walk) * cell,
                _ => ResidentialLot.Walk * cell,
            };
            float outward = side == 0 || side == 3 ? -1f : 1f;
            const float lane = 2.1f;    // the table's middle, out from the pavement line: a
                                        // step of clear pavement between the chairs and the door
            float @in = line + outward * lane;
            // the table's whole width either side of its middle, as on the patio: the
            // table with its chairs, not the table alone
            float width = Box(CafeTable).size.x;
            float a = a0 + width + 0.2f + Between(rng, 0f, 0.9f);
            int offered = 0;
            while (a + width <= a1 - 0.2f + 0.01f)
            {
                float scattered = Mathf.Clamp(a + Between(rng, -TableJitter, TableJitter),
                                              a0 + width + 0.2f, a1 - width - 0.2f);
                a += Between(rng, TableAlongMin, TableAlongMax);
                if (offered++ > 0 && !Chance(rng, TableKeep)) continue;
                float x = alongX ? scattered : @in, z = alongX ? @in : scattered;
                if (!Table(pen, x, z, rng, Chance(rng, ShadeOdds), stood, Deck)) continue;
                standing.Add(new Vector3(x, 0f, z));
            }
        }

        // ------------------------------------------------------------------ what is on it

        /// <summary>What stands on the ground: the skips on the alley's verges, and nothing
        /// in the yards. The yards had fences, washing lines, bins and boxes; the fences
        /// were a run of railings that finished nowhere and the washing hung on nothing (the
        /// user, 2026-08-27: "makni zidove skroz") - a yard the recipe has nothing for is
        /// left bare, not dressed.</summary>
        static void Dress(ResidentialLot.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            var skips = new List<Vector3>();
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                    if (plan.Ground[i, j] == ResidentialLot.Use.Verge) Skip(plan, root, rng, i, j, skips, stood);
        }

        /// <summary>
        /// The skips: on the verge beside the alley, against the backs of the houses - and
        /// never on the alley itself, which is a road (the user, 2026-08-27: "ako si stavio
        /// put ne mozes na put da stavis kontenjere"). A carton or a bag spills beside one.
        /// </summary>
        static void Skip(ResidentialLot.Plan plan, Transform root, System.Random rng, int i, int j,
                         List<Vector3> skips, Stood stood)
        {
            int alley = -1;
            for (int side = 0; side < 4; side++)
            {
                int x = i + ResidentialLot.Step[side, 0], y = j + ResidentialLot.Step[side, 1];
                if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                if (plan.Ground[x, y] == ResidentialLot.Use.Alley) alley = side;
            }
            if (alley < 0 || !Chance(rng, SkipOdds)) return;

            float cell = ResidentialLot.Cell;
            float cx = (i + 0.5f) * cell, cz = (j + 0.5f) * cell;
            // against the far edge of the verge: half the cell, less the skip's own half
            // depth and a hand's breadth off the wall
            const float back = 1.6f;
            float dx = -ResidentialLot.Step[alley, 0] * back, dz = -ResidentialLot.Step[alley, 1] * back;
            bool alongX = alley == 0 || alley == 2;     // the alley runs east-west: so does the skip
            float slide = Between(rng, -1.2f, 1.2f);
            float sx = cx + dx + (alongX ? slide : 0f), sz = cz + dz + (alongX ? 0f : slide);
            foreach (var other in skips)
                if ((other.x - sx) * (other.x - sx) + (other.z - sz) * (other.z - sz) < SkipApart * SkipApart)
                    return;
            // the skip's lid slopes down to its +z side, so that is its front: it is turned
            // to face the alley, back to the wall (the user, 2026-08-27: "kante treba da su
            // okrenute ka ulicici")
            float yaw = alley switch { 2 => 0f, 1 => 90f, 0 => 180f, _ => 270f };
            if (Prop(Any(Skips, rng), root, sx, sz, yaw, 1f, Deck) == null) return;
            stood.Props++;
            skips.Add(new Vector3(sx, 0f, sz));

            if (!Chance(rng, 0.6)) return;
            float along = Between(rng, 1.5f, 2.1f) * (Chance(rng, 0.5) ? 1f : -1f);
            float lx = sx + (alongX ? along : Between(rng, -0.4f, 0.4f));
            float lz = sz + (alongX ? Between(rng, -0.4f, 0.4f) : along);
            // a carton beside the skip more often than a bag: the user's pick (CardboardBox_09
            // in the demo, which is the pack's _04)
            string spill = Chance(rng, 0.5) ? Carton : Any(Litter, rng);
            if (Prop(spill, root, lx, lz, Between(rng, 0f, 360f), 1f, Deck) != null) stood.Props++;
        }

        /// <summary>
        /// The yards, which were bare: a power box against the back of a house (the user,
        /// 2026-08-27: "obicno u unutrasnjosti bloka uz zgradu") and a picnic table on open
        /// ground away from the walls and the ways. A few of each, never a furnished garden.
        /// </summary>
        static void Yards(ResidentialLot.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            const int mostBoxes = 3, mostTables = 3;
            float cell = ResidentialLot.Cell;
            var cells = new List<(int, int)>();
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    var use = plan.Ground[i, j];
                    if (use == ResidentialLot.Use.Yard || use == ResidentialLot.Use.Court ||
                        use == ResidentialLot.Use.Paved) cells.Add((i, j));
                }

            foreach (var (i, j) in cells.OrderBy(_ => rng.Next()))
            {
                float cx = (i + 0.5f) * cell, cz = (j + 0.5f) * cell;
                int wall = -1;
                bool open = true;
                for (int s = 0; s < 4; s++)
                {
                    int x = i + ResidentialLot.Step[s, 0], y = j + ResidentialLot.Step[s, 1];
                    if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) { open = false; continue; }
                    var near = plan.Ground[x, y];
                    if (near == ResidentialLot.Use.Building && wall < 0) wall = s;
                    if (near != ResidentialLot.Use.Yard && near != ResidentialLot.Use.Court &&
                        near != ResidentialLot.Use.Paved && near != ResidentialLot.Use.Park) open = false;
                }

                if (wall >= 0 && stood.Boxes < mostBoxes && Chance(rng, 0.25))
                {
                    // back to the wall: its +z is its front, so the front looks away from it
                    float push = cell * 0.5f - Box(PowerBox).size.z * 0.5f - 0.15f;
                    float x = cx + ResidentialLot.Step[wall, 0] * push, z = cz + ResidentialLot.Step[wall, 1] * push;
                    float yaw = wall switch { 0 => 0f, 1 => 270f, 2 => 180f, _ => 90f };
                    if (Prop(PowerBox, root, x, z, yaw, 1.1f, Deck) != null) { stood.Boxes++; stood.Props++; }
                    continue;
                }
                if (open && stood.Picnics < mostTables && Chance(rng, 0.2))
                {
                    float yaw = 90f * rng.Next(2) + Between(rng, -10f, 10f);
                    if (Prop(Picnic, root, cx + Between(rng, -1f, 1f), cz + Between(rng, -1f, 1f), yaw, 1.3f, Deck) != null)
                    { stood.Picnics++; stood.Props++; }
                }
            }
        }

        /// <summary>
        /// The paved gaps that got neither the cafe nor the subway - the empty ground on the
        /// street the user pointed at ("ovaj prazan prostor cemu"): a bench on the back line
        /// looking at the street, a bin beside it, now and then a hot dog cart by the
        /// pavement or a billboard on a pole at the back. Each is offered, none is forced.
        /// </summary>
        static void Plazas(ResidentialLot.Plan plan, Transform root, System.Random rng, Stood stood)
        {
            float cell = ResidentialLot.Cell;
            foreach (var gap in plan.Gaps)
            {
                if (plan.Cafes.Contains(gap) || gap.Use != ResidentialLot.Use.Paved || gap.Run < 2) continue;
                bool alongX = gap.Side == 0 || gap.Side == 2;
                float toStreet = ToStreet(gap.Side);

                // the gap's columns that are still paved ground, and how deep each goes
                var columns = new List<(int At, int Deep)>();
                for (int n = 0; n < gap.Run; n++)
                {
                    int deep = 0;
                    for (int k = 0; k < gap.Depth; k++)
                    {
                        var (i, j) = Into(plan, gap.Side, gap.At + n, k);
                        if (i < 0 || j < 0 || i >= plan.W || j >= plan.D) break;
                        if (plan.Ground[i, j] != ResidentialLot.Use.Paved) break;
                        deep++;
                    }
                    if (deep > 0) columns.Add((gap.At + n, deep));
                }
                if (columns.Count == 0) continue;

                Vector2 Where(int at, int k, float alongOff, float inOff)
                {
                    var (i, j) = Into(plan, gap.Side, at, k);
                    float cx = (i + 0.5f) * cell, cz = (j + 0.5f) * cell;
                    float inward = gap.Side == 0 || gap.Side == 3 ? 1f : -1f;
                    return alongX ? new Vector2(cx + alongOff, cz + inward * inOff)
                                  : new Vector2(cx + inward * inOff, cz + alongOff);
                }

                var pick = columns[rng.Next(columns.Count)];
                if (Chance(rng, 0.6))
                {
                    // the bench against the back of the column, looking at the street
                    var at = Where(pick.At, pick.Deep - 1, 0f, cell * 0.5f - 0.6f);
                    if (Prop(ParkBench, root, at.x, at.y, toStreet, 1.1f, Deck) != null)
                    {
                        stood.Benches++; stood.Props++;
                        var bin = Where(pick.At, pick.Deep - 1, 1.7f, cell * 0.5f - 0.5f);
                        if (Chance(rng, 0.5) && Prop(Any(Bins, rng), root, bin.x, bin.y, toStreet, 1.2f, Deck) != null)
                        { stood.Bins++; stood.Props++; }
                    }
                }
                if (Chance(rng, 0.25))
                {
                    var other = columns[rng.Next(columns.Count)];
                    var at = Where(other.At, 0, Between(rng, -1f, 1f), -(cell * 0.5f) + 1.6f);
                    if (Prop(Hotdog, root, at.x, at.y, alongX ? 0f : 90f, 1.2f, Deck) != null)
                    { stood.Hotdog = true; stood.Props++; }
                }
                if (pick.Deep >= 2 && Chance(rng, 0.25))
                {
                    var at = Where(pick.At, pick.Deep - 1, Between(rng, -1.5f, 1.5f), cell * 0.5f - 1.2f);
                    if (Billboard(root, at.x, at.y, toStreet)) { stood.Billboard = true; stood.Props += 2; }
                }
            }
        }

        /// <summary>A billboard: the pack's pole with its panel on top, the panel's face
        /// (its +z) turned the way asked. The panel stands on its own legs, so it is set
        /// down on the pole's top.</summary>
        static bool Billboard(Transform root, float x, float z, float yaw)
        {
            var pole = Prop(BillboardPole, root, x, z, yaw, 1.5f, Deck);
            if (pole == null) return false;
            float top = Box(BillboardPole).size.y + Deck - 0.1f;
            var panel = Sit(BillboardPanel, root, x, z, yaw, top);
            return panel != null;
        }

        /// <summary>Street lamps on the kerb, a cell in from the corner and every 20 m after
        /// - and only on sides that have a street. Where each one stands is remembered, so
        /// no palm is planted through it.</summary>
        static void Lamps(ResidentialLot.Plan plan, Transform root, List<Vector3> standing, Stood stood)
        {
            float cell = ResidentialLot.Cell, lane = 1.0f;
            for (int side = 0; side < 4; side++)
            {
                if (!plan.Street[side]) continue;
                bool alongX = side == 0 || side == 2;
                int length = alongX ? plan.W : plan.D;

                for (int at = 1; at < length - 1; at += LampEvery)
                {
                    int i = alongX ? at : side == 1 ? plan.W - 1 : 0;
                    int j = alongX ? (side == 2 ? plan.D - 1 : 0) : at;
                    // never on a mouth: a lamp in the ring cell cut for the car park's way
                    // in stood in the way of the cars (the user, 2026-08-27: "ne mozes na
                    // sred ulaznog puta da stavis lampu")
                    if (plan.Ground[i, j] != ResidentialLot.Use.Walkway) continue;
                    var at3 = KerbLane(plan, side, i, j, lane);
                    if (Prop(Lamp, root, at3.x, at3.z, ToStreet(side), 0.4f, Deck) == null) continue;
                    stood.Props++;
                    stood.Lamps++;
                    standing.Add(at3);
                }
            }
        }

        /// <summary>A point on a ring cell, <paramref name="inset"/> metres in from the
        /// kerb's outer edge on the side that faces this street.</summary>
        static Vector3 KerbLane(ResidentialLot.Plan plan, int side, int i, int j, float inset)
        {
            float cell = ResidentialLot.Cell;
            float x = (i + 0.5f) * cell, z = (j + 0.5f) * cell;
            float push = cell * 0.5f - inset;
            x += side == 1 ? push : side == 3 ? -push : 0f;
            z += side == 2 ? push : side == 0 ? -push : 0f;
            return new Vector3(x, 0f, z);
        }

        /// <summary>
        /// The pavement's furniture, in the demo's own lanes: the kerb lane for what serves
        /// the road (meters, the bus shelter, bollards, bins), the wall lane for what serves
        /// the door (a mailbox, a hot dog cart at the corner). Every one is booked and
        /// counted, and every one is remembered for the palms.
        /// </summary>
        static void Street(ResidentialLot.Plan plan, Transform root, System.Random rng,
                           List<Vector3> standing, Stood stood)
        {
            float cell = ResidentialLot.Cell;
            var streets = Enumerable.Range(0, 4).Where(s => plan.Street[s]).ToList();
            if (streets.Count == 0) return;
            int artery = plan.Street[plan.Artery] ? plan.Artery : streets[0];

            bool Put(string path, Vector3 at, float yaw, float room)
            {
                if (Prop(path, root, at.x, at.z, yaw, room, Deck) == null) return false;
                stood.Props++;
                standing.Add(at);
                return true;
            }

            // the corners: bollards along the longer street's kerb, a bin tucked in
            int lo = 0, hi = plan.W - 1, bo = 0, to = plan.D - 1;
            foreach (var (i, j, a, b) in new[] { (lo, bo, 0, 3), (hi, bo, 0, 1), (hi, to, 2, 1), (lo, to, 2, 3) })
            {
                if (!plan.Street[a] || !plan.Street[b]) continue;
                int longSide = plan.W >= plan.D ? a : b;     // a is the E-W street
                if (Chance(rng, 0.5))
                {
                    var at = KerbLane(plan, longSide, i, j, 0.6f);
                    bool alongX = longSide == 0 || longSide == 2;
                    Put(Bollards, at, alongX ? 0f : 90f, 1.05f);
                }
                if (Chance(rng, 0.6))
                {
                    // the bin a metre and a half in from both kerbs
                    var at = KerbLane(plan, a, i, j, 1.5f);
                    var other = KerbLane(plan, b, i, j, 1.5f);
                    at = new Vector3(b == 1 || b == 3 ? other.x : at.x, 0f, a == 0 || a == 2 ? at.z : other.z);
                    if (Put(Any(Bins, rng), at, ToStreet(a), 1.3f)) stood.Bins++;
                }
                if (!stood.Hotdog && (a == artery || b == artery) && Chance(rng, 0.3))
                {
                    // the cart by the wall lane at the corner, along the artery
                    var at = KerbLane(plan, a, i, j, 3.4f);
                    var other = KerbLane(plan, b, i, j, 3.4f);
                    at = new Vector3(b == 1 || b == 3 ? other.x : at.x, 0f, a == 0 || a == 2 ? at.z : other.z);
                    bool alongX = artery == 0 || artery == 2;
                    if (Put(Hotdog, at, alongX ? 0f : 90f, 1.2f)) stood.Hotdog = true;
                }
            }

            // the parking meters: on half the blocks, between the lamps, facing the road
            foreach (int side in streets)
            {
                if (!Chance(rng, 0.5)) continue;
                bool alongX = side == 0 || side == 2;
                int length = alongX ? plan.W : plan.D;
                for (int at = 3; at < length - 1; at += LampEvery)
                {
                    var (i, j) = RingCell(plan, side, at);
                    if (plan.Ground[i, j] != ResidentialLot.Use.Walkway) continue;
                    var spot = KerbLane(plan, side, i, j, 0.8f);
                    spot += alongX ? new Vector3(Between(rng, -1f, 1f), 0f, 0f) : new Vector3(0f, 0f, Between(rng, -1f, 1f));
                    if (Put(Meter, spot, ToStreet(side), 1.3f)) stood.Meters++;
                }
            }

            // one mailbox, on the artery, facing the pavement
            if (Chance(rng, 0.6))
            {
                bool alongX = artery == 0 || artery == 2;
                int length = alongX ? plan.W : plan.D;
                int at = rng.Next(2) == 0 ? 2 : length - 3;
                var (i, j) = RingCell(plan, artery, at);
                if (at > 0 && at < length - 1 && plan.Ground[i, j] == ResidentialLot.Use.Walkway)
                {
                    var spot = KerbLane(plan, artery, i, j, 1.1f);
                    if (Put(Mailbox, spot, ToStreet(artery) + 180f, 1.3f)) stood.Mailbox = true;
                }
            }

            // a bus shelter on a third of the blocks, on a long side, open to the road
            if (Chance(rng, 0.35))
            {
                foreach (int side in streets.OrderBy(_ => rng.Next()))
                {
                    bool alongX = side == 0 || side == 2;
                    int length = alongX ? plan.W : plan.D;
                    if (length < 7) continue;
                    // a cell that has no lamp (the lamps are at 1, 5, 9 ...)
                    var spots = Enumerable.Range(2, length - 4).Where(a => (a - 1) % LampEvery != 0).ToList();
                    if (spots.Count == 0) continue;
                    int at = spots[rng.Next(spots.Count)];
                    var (i, j) = RingCell(plan, side, at);
                    if (plan.Ground[i, j] != ResidentialLot.Use.Walkway) continue;
                    var spot = KerbLane(plan, side, i, j, 1.3f);
                    if (!Put(BusStop, spot, ToStreet(side), 1.05f)) continue;
                    stood.BusStop = true;
                    // and a bin at its end
                    var bin = spot + (alongX ? new Vector3(2.9f, 0f, 0f) : new Vector3(0f, 0f, 2.9f));
                    if (Put(Any(Bins, rng), bin, ToStreet(side), 1.2f)) stood.Bins++;
                    break;
                }
            }
        }

        /// <summary>The palms on the pavement ring, one to ten kerb tiles in the core's own
        /// lane and rhythm (<see cref="CorePavement.Plant"/>), so a residential block and a
        /// core block are planted alike - the only trees the block gets outside its parks.</summary>
        static void Palms(List<CorePavement.Kerbstone> kerbs, List<Vector3> standing, Transform root,
                          System.Func<GameObject, Transform, GameObject> raise, int seed, Stood stood)
        {
            var under = new GameObject("Palms").transform;
            under.SetParent(root, false);
            stood.Palms = CorePavement.Plant(kerbs, standing, raise, under, seed);
            stood.Props += stood.Palms;
        }
    }
}
