using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The industrial quarter, dealt from a seed: where the parcels are, how big, which
    /// recipe stands on each, and which of their sides are kerb and which are a fence
    /// shared with the neighbour.
    ///
    /// This is the THIRD of the layout generators and it is worth saying how it differs
    /// from the other two, because all three end up drawing roads with the same code:
    ///
    ///   - <see cref="CoreLayout"/> arranges blocks that already EXIST. Sixteen prefabs
    ///     were harvested out of the Synty demo, so the deal is a packing problem: turn
    ///     them, row them up, and put streets in whatever gaps come out. A block shallower
    ///     than its row leaves ground behind it, and that ground becomes its car park.
    ///   - <c>IndustrialBlockForge</c> composes ONE block to a size it picks itself.
    ///   - here the PARCEL comes first and the recipe fills it. The quarter says "an
    ///     eighty by sixty works stands here, sharing its east fence with a stockyard", and
    ///     the composer has to make that fit. That is the only way a district comes out
    ///     with no left-over ground and no two blocks alike.
    ///
    /// What makes it read as industry rather than as a city with big blocks, and every one
    /// of these is a rule below rather than a decoration:
    ///
    ///   - ONE artery through the middle, and everything fronts it or backs onto it. A
    ///     works estate is not a grid; it is a road with works down both sides of it.
    ///   - NEIGHBOURS SHARE FENCES. A street does not run between every two parcels, so
    ///     the cross streets fall 150-250 m apart instead of every 90 m. This is the single
    ///     biggest difference from the core, where every block is an island.
    ///   - BACK TO BACK. Two rows of parcels meet along their back fences with no street
    ///     between them, and the street runs behind the pair. Yards touch yards; only the
    ///     fronts see a road.
    ///   - the frontage on the artery is BUILT and brick; the ground behind is WIRE. From
    ///     above the quarter reads solid along the middle and transparent at the edges.
    ///
    /// The roads themselves are not this class's business. It hands
    /// <see cref="CoreRoads.Build"/> a block per island and a band per declared street,
    /// and the corridor reader finds the cross streets, judges the drawing and reports its
    /// faults exactly as it does for the core - which is the point of doing it this way:
    /// a quarter that passes is a quarter drawn by code five other things already rely on.
    /// </summary>
    public static class IndustrialLayout
    {
        public const float Cell = 5f;

        /// <summary>A street between two parcels: three cells, the city's own 15 m.</summary>
        public const int StreetGap = 3;

        /// <summary>
        /// The artery: seven cells, the city's 35 m divided road.
        ///
        /// Every other road in the quarter is a street, and if the spine were a street too
        /// the drawing would be a grid of equal roads with works on it - which is what an
        /// industrial estate is NOT. It is one road that carries the lorries, with the works
        /// either side fronting on to it, and everything else a service street behind.
        /// Thirty-five metres is also the only other width the raster reads (5, 10, 15, 35),
        /// and the one that gives a lorry two lanes each way and somewhere to wait to turn.
        /// </summary>
        public const int ArteryGap = 7;

        /// <summary>Wider than any drawing, for a band that runs the whole way across.</summary>
        const float Any = 100000f;

        // ------------------------------------------------------------------ the recipes

        /// <summary>
        /// What stands on a parcel. The first five are the forge's own; the last three are
        /// this quarter's, and are the pieces a district needs that a single block never
        /// did - somewhere to buy fuel, somewhere to keep it, and a plot nobody has built
        /// on yet.
        /// </summary>
        public enum Recipe { Works, Plant, Strip, Yard, Depot, Haulage, Fuel, Waste }

        /// <summary>
        /// The smallest parcel a recipe can fill, in cells.
        ///
        /// Every one of these is the recipe's own frontage plus its setbacks, measured off
        /// the kit rather than picked: a works is FactoryOld (22.5 m) + the gate + Factory
        /// (24.4 m) = 55 m of street, and 13.2 m of setback either side, so 70 m across. The
        /// first table rounded all of them UP for comfort and the quarter came out of
        /// compounds a hundred metres across with the yard showing through the middle - the
        /// thing this floor exists to prevent, arrived at from the other direction.
        /// </summary>
        public static void Smallest(Recipe recipe, out int w, out int d)
        {
            switch (recipe)
            {
                case Recipe.Works: w = 14; d = 11; break;
                case Recipe.Plant: w = 14; d = 12; break;
                case Recipe.Yard: w = 14; d = 11; break;
                case Recipe.Depot: w = 12; d = 14; break;
                // the haulage yard runs a garage on the frontage, a fuel island and rows
                // of lorry bays one behind the other, and wants the depth for all three:
                // asked for less, the yard behind the bays comes out with a negative height
                // and everything in it quietly does nothing
                case Recipe.Haulage: w = 12; d = 11; break;
                case Recipe.Strip: w = 12; d = 10; break;
                case Recipe.Fuel: w = 10; d = 9; break;
                default: w = 9; d = 8; break;       // the empty plot, which wants nothing
            }
        }

        static bool Fits(Recipe recipe, int w, int d)
        {
            Smallest(recipe, out int mw, out int md);
            return w >= mw && d >= md;
        }

        /// <summary>What a recipe is called on a card, in words rather than in the enum's
        /// shorthand: a click is meant to answer "what is this", and "Yard" is not an
        /// answer if the thing is a container stockyard.</summary>
        public static string Words(Recipe recipe)
        {
            switch (recipe)
            {
                case Recipe.Works: return "Works";
                case Recipe.Plant: return "Processing plant";
                case Recipe.Strip: return "Service strip";
                case Recipe.Yard: return "Stockyard";
                case Recipe.Depot: return "Warehouse depot";
                case Recipe.Haulage: return "Haulage yard";
                case Recipe.Fuel: return "Tank farm";
                default: return "Empty plot";
            }
        }

        /// <summary>Does this recipe wall itself in brick and build its street frontage?
        /// Those go on the artery; the wire ones go behind.</summary>
        public static bool Brick(Recipe recipe) =>
            recipe == Recipe.Works || recipe == Recipe.Plant || recipe == Recipe.Strip;

        // ------------------------------------------------------------------- the pieces

        /// <summary>Which of a parcel's four sides this is, in the QUARTER's compass rather
        /// than the parcel's own - the parcel is composed facing south and turned into
        /// place afterwards.</summary>
        public enum Side { South, North, West, East }

        /// <summary>What runs along a side: the block's own kerb, with a street beyond it,
        /// or a fence shared with the parcel next door.</summary>
        public enum Rim { Kerb, Party }

        /// <summary>
        /// One side of a parcel: what runs along it, and whether THIS parcel is the one that
        /// lays it.
        ///
        /// The second half is the whole reason this is a pair and not just a
        /// <see cref="Rim"/>. A shared fence has two owners and must have one builder: told
        /// only that the side is shared, both neighbours lay a fence on the same line and
        /// the island comes out wearing two fences in every internal gap, posts and all.
        /// The rule is fixed rather than negotiated - the parcel to the WEST lays the fence
        /// between two in a row, the parcel to the SOUTH the fence between two rows - and it
        /// is settled here, in the quarter's own compass, because in the parcel's frame the
        /// two of them are the same side and could not tell each other apart.
        /// </summary>
        public struct Edge
        {
            public Rim Rim;
            public bool Lays;
            public Edge(Rim rim, bool lays) { Rim = rim; Lays = lays; }
        }

        public sealed class Parcel
        {
            public int I0, J0;             // south-west corner, cells from the quarter's origin
            public int W, D;               // cells
            public Recipe Recipe;
            /// <summary>0 fronts south, 180 fronts north. The composer only ever builds a
            /// parcel fronting south; this is the quarter turn that puts its face on the
            /// street it belongs to.</summary>
            public int Yaw;
            public int Island;
            /// <summary>Its place in the deal, so a parcel has a name a person can say. Read
            /// off the position instead, a parcel west or south of the artery is called
            /// "stop--14--15", which nobody can read at a glance or type into a search.</summary>
            public int Index;
            public int Tier;
            public bool Back;              // the far row of a back-to-back pair

            readonly Edge[] _edges = new Edge[4];
            public Edge this[Side side] { get => _edges[(int)side]; set => _edges[(int)side] = value; }

            /// <summary>The parcel on the ground, in the quarter's own metres.</summary>
            public Rect Box => new Rect(I0 * Cell, J0 * Cell, W * Cell, D * Cell);

            /// <summary>The side of the parcel its gate opens onto.</summary>
            public Side Face => Yaw == 180 ? Side.North : Side.South;

            /// <summary>A side read in the parcel's OWN frame, which is the frame the
            /// composer works in: at 180 the compass turns with it.</summary>
            public Edge Local(Side local)
            {
                if (Yaw != 180) return this[local];
                switch (local)
                {
                    case Side.South: return this[Side.North];
                    case Side.North: return this[Side.South];
                    case Side.West: return this[Side.East];
                    default: return this[Side.West];
                }
            }

            /// <summary>All four sides in the parcel's own frame, in the order the composer
            /// indexes them (south, north, west, east).</summary>
            public Edge[] Locals() => new[]
            {
                Local(Side.South), Local(Side.North), Local(Side.West), Local(Side.East),
            };

            public string Name => $"{Recipe.ToString().ToLowerInvariant()}-{Index:00}";
        }

        /// <summary>
        /// A run of parcels with no street between them: what the roads see as one block.
        ///
        /// This is where the quarter's texture comes from. The core's blocks are islands of
        /// one; here an island is two or three works side by side, and where the tier is
        /// doubled it is two rows of them back to back - so the drawing gets a block of
        /// 150 x 130 m with six businesses inside it and a street only where the island
        /// ends.
        /// </summary>
        public sealed class Island
        {
            public int I0, J0, W, D;
            public readonly List<Parcel> Parcels = new List<Parcel>();
            public string Name;
            public Rect Box => new Rect(I0 * Cell, J0 * Cell, W * Cell, D * Cell);
        }

        public sealed class Plan
        {
            public string Name;
            public int Seed;
            public int Attempt;
            /// <summary>
            /// This is one roadside half of an estate. Its first road band belongs to the
            /// district hosting it, so the industrial quarter must join that road instead
            /// of laying its usual divided artery over the top.
            /// </summary>
            public bool ExternalArtery;
            /// <summary>The shared road's local Z band, after the raster has clipped it
            /// to the blocks which remain.</summary>
            public Vector2 ExternalRoad;
            public readonly List<Island> Islands = new List<Island>();
            public readonly List<Parcel> Parcels = new List<Parcel>();
            /// <summary>What <see cref="CoreRoads.Build"/> is handed: the artery and the
            /// street behind every tier, declared the whole way across.</summary>
            public readonly CoreLayout.Plan Roads = new CoreLayout.Plan();
            public readonly List<string> Rows = new List<string>();
        }

        // ---------------------------------------------------------------------- dealing

        /// <summary>Tiers either side of the artery. Two or three a side is a quarter of
        /// ten to sixteen parcels, which is what the city has room for beside everything
        /// else in it.</summary>
        const int TiersMin = 2, TiersMax = 3;

        /// <summary>
        /// A parcel, in cells: 75-105 m across and 55-75 m deep.
        ///
        /// The floor is read off <see cref="Smallest"/> and the ceiling is a judgement:
        /// 60-85 m across, 55-70 m deep, which is the size the bench composes a candidate at
        /// (Tools/City/Core/Industrial) and the size a works actually is.
        ///
        /// It was 75-105 x 60-75 and that was too big - the first drawing came out of
        /// compounds you could not see the far fence of, with the yard spread so thin that
        /// no amount of clutter filled it. A recipe furnishes what it is given, so the way to
        /// a dense yard is a smaller yard, not more barrels.
        ///
        /// The floor still has to clear <see cref="Smallest"/> or the deal reverts to a
        /// monoculture: with the depth floor under 11 cells nothing but the service strip
        /// fits, and a tier that rolled it came out as a row of strips and nothing else.
        /// </summary>
        const int ParcelWMin = 12, ParcelWMax = 17;
        const int ParcelDMin = 11, ParcelDMax = 14;

        /// <summary>How long a tier runs before it stops, in cells: 150-230 m.</summary>
        const int TierMin = 30, TierMax = 46;

        /// <summary>
        /// How often a tier is two rows deep, back fence to back fence.
        ///
        /// It was every other tier, and doubled onto parcels of 75 m that made a single
        /// walled block 150 m deep - which with two or three of them side by side is a
        /// quarter of four enormous compounds and no texture at all. A quarter of a hundred
        /// small works with streets between them is the thing being drawn, so back to back
        /// is now the exception it is in life: the odd pair whose yards happen to meet.
        /// </summary>
        const double DoubleOdds = 0.25;

        /// <summary>
        /// How often two parcels share a fence instead of taking a street between them.
        ///
        /// The point of a shared fence is that an industrial street falls every 150-250 m
        /// rather than every 90 - but three to an island made compounds 300 m across. Two is
        /// the most, and most islands are one block on its own, which is what the bench
        /// composes and what the quarter is meant to read as.
        /// </summary>
        const double ShareOdds = 0.35;

        /// <summary>The seed deals a quarter; the drawing judges it. Same seed, same
        /// quarter, every time.</summary>
        public static Plan Roll(int seed)
        {
            var dice = new System.Random(seed);
            var plan = new Plan { Seed = seed, Name = $"seed {seed}" };

            float arteryTo = ArteryGap * Cell;
            plan.Roads.MainRoad = new Vector2(0f, arteryTo);
            plan.Roads.Bands.Add(Rect.MinMaxRect(-Any, 0f, Any, arteryTo));

            int tiersNorth = dice.Next(TiersMin, TiersMax + 1);
            int tiersSouth = dice.Next(TiersMin, TiersMax + 1);

            int northNext = ArteryGap;          // the first tier north starts past the artery
            int southNext = 0;                  // and the first south ends at its south kerb
            var northStreets = new List<int>();
            var southStreets = new List<int>();

            int tiers = tiersNorth + tiersSouth;
            int northTier = 0, southTier = 0;
            bool north = dice.Next(2) == 0;
            for (int t = 0, laid = 0; laid < tiers && t < tiers * 2; t++)
            {
                if (north && tiersNorth <= 0) north = false;
                if (!north && tiersSouth <= 0) north = true;
                if (north) tiersNorth--; else tiersSouth--;
                laid++;
                // the tier's place in its OWN half of the quarter: the first one either
                // side of the artery is the one that fronts it, and that is what decides
                // whether the parcels on it are built in brick or fenced in wire
                int rank = north ? ++northTier : ++southTier;

                int front = dice.Next(ParcelDMin, ParcelDMax + 1);
                bool doubled = dice.NextDouble() < DoubleOdds;
                int back = doubled ? dice.Next(ParcelDMin, ParcelDMax + 1) : 0;
                int depth = front + back;

                // where the tier's islands fall along x, and how wide each one is
                var widths = new List<int>();
                int length = 0, want = dice.Next(TierMin, TierMax + 1);
                while (length < want)
                {
                    int parcels = dice.NextDouble() < ShareOdds ? 2 : 1;
                    int wide = 0;
                    for (int p = 0; p < parcels; p++) wide += dice.Next(ParcelWMin, ParcelWMax + 1);
                    widths.Add(wide);
                    length += wide;
                    if (length < want) length += StreetGap;
                }

                // the tier stands roughly centred, at whichever offset puts its cross
                // streets either exactly in line with the neighbouring tier's or well
                // clear of them: two streets meeting the same road a few metres apart
                // merge into one wide junction box, and no lane graph drives that
                var facing = north ? (northStreets.Count > 0 ? northStreets : southStreets)
                                   : (southStreets.Count > 0 ? southStreets : northStreets);
                int centre = -length / 2;
                int at = centre, worst = int.MaxValue;
                foreach (int jitter in Jitters(dice))
                {
                    int clash = Clashes(Streets(widths, centre + jitter), facing);
                    if (clash >= worst) continue;
                    worst = clash;
                    at = centre + jitter;
                    if (clash == 0) break;
                }
                var streets = Streets(widths, at);
                if (north) northStreets = streets; else southStreets = streets;

                int j0 = north ? northNext : southNext - depth;
                if (north) northNext = j0 + depth + StreetGap;
                else southNext = j0 - StreetGap;

                var line = new System.Text.StringBuilder();
                line.Append(north ? "north" : "south").Append(" tier ")
                    .Append(doubled ? $"{front}+{back}" : front.ToString()).Append(" deep, z ")
                    .Append((j0 * Cell).ToString("F0")).Append("..").Append(((j0 + depth) * Cell).ToString("F0"))
                    .Append(':');

                int i = at;
                foreach (int wide in widths)
                {
                    var island = new Island
                    {
                        I0 = i, J0 = j0, W = wide, D = depth,
                        Name = $"island-{plan.Islands.Count + 1:00}",
                    };
                    // the near row fronts the artery; the far row of a doubled tier turns
                    // its back on it and fronts the street behind. Which of the two is
                    // nearer depends on the side: north of the artery the near row is the
                    // island's southern half, south of it the northern
                    if (north)
                    {
                        Row(plan, island, dice, i, j0, wide, front, 0, false, rank);
                        if (doubled) Row(plan, island, dice, i, j0 + front, wide, back, 180, true, rank);
                    }
                    else
                    {
                        if (doubled) Row(plan, island, dice, i, j0, wide, back, 0, true, rank);
                        Row(plan, island, dice, i, j0 + back, wide, front, 180, false, rank);
                    }
                    plan.Islands.Add(island);
                    line.Append(' ').Append(island.W * 5).Append('x').Append(island.D * 5)
                        .Append('[').Append(island.Parcels.Count).Append(']');
                    i += wide + StreetGap;
                }
                // the street behind the tier, declared the whole way across the drawing.
                // Without it the cross street at the end of a short tier runs into the
                // edge street of the next and the two make an L with two arms, where the
                // traffic locks (the core learned this the hard way, seed 1987)
                float bandMin = north ? (j0 + depth) * Cell : (j0 - StreetGap) * Cell;
                float reach = 2f * StreetGap * Cell;    // past the edge street at either end
                plan.Roads.Bands.Add(Rect.MinMaxRect(at * Cell - reach, bandMin,
                                                     (at + length) * Cell + reach, bandMin + StreetGap * Cell));
                plan.Rows.Add(line.ToString());
                north = !north;
            }

            Cast(plan, dice);
            return plan;
        }

        /// <summary>One row of an island cut into parcels, each at least a recipe wide,
        /// with a shared fence between every two of them.</summary>
        static void Row(Plan plan, Island island, System.Random dice, int i0, int j0, int wide,
                        int deep, int yaw, bool back, int tier)
        {
            int most = Mathf.Max(1, wide / ParcelWMin);
            int parts = Mathf.Clamp(dice.NextDouble() < ShareOdds ? 2 : 1, 1, most);
            var cuts = Split(wide, parts, ParcelWMin, dice);

            int i = i0;
            for (int p = 0; p < cuts.Count; p++)
            {
                var parcel = new Parcel
                {
                    I0 = i, J0 = j0, W = cuts[p], D = deep, Yaw = yaw,
                    Island = plan.Islands.Count, Index = plan.Parcels.Count + 1,
                    Tier = tier, Back = back,
                };
                // the island's outside is kerb; everything inside it is a shared fence, and
                // of each shared pair the western and the southern parcel is the one that
                // lays it - so every gap inside an island gets exactly one fence
                bool westEnd = p == 0, eastEnd = p == cuts.Count - 1;
                bool doubled = island.D > deep;
                bool southIsIsland = j0 == island.J0;
                bool southEnd = !doubled || southIsIsland;
                parcel[Side.West] = new Edge(westEnd ? Rim.Kerb : Rim.Party, westEnd);
                parcel[Side.East] = new Edge(eastEnd ? Rim.Kerb : Rim.Party, true);
                parcel[Side.South] = new Edge(southEnd ? Rim.Kerb : Rim.Party, southEnd);
                parcel[Side.North] = new Edge(!doubled || !southIsIsland ? Rim.Kerb : Rim.Party, true);
                island.Parcels.Add(parcel);
                plan.Parcels.Add(parcel);
                i += cuts[p];
            }
        }

        /// <summary>Cuts a run of cells into parts, none shorter than <paramref name="min"/>.
        /// The remainder is dealt a cell at a time so no part is left a sliver.</summary>
        static List<int> Split(int total, int parts, int min, System.Random dice)
        {
            // no part wider than a parcel may be, however the dice fell. The island's width
            // and the row's division are rolled SEPARATELY - the width is a sum of parcel
            // draws, the division a fresh coin - so a run of two draws could be handed back
            // as one parcel of a hundred and forty metres, which is twice what any recipe
            // was measured for. The floor is the roll's; the ceiling is not negotiable.
            int least = Mathf.CeilToInt(total / (float)ParcelWMax);
            parts = Mathf.Max(least, parts);
            parts = Mathf.Max(1, Mathf.Min(parts, total / Mathf.Max(1, min)));
            var cuts = new List<int>();
            // a run shorter than one part is ONE part of the whole run, not one part of the
            // minimum: taking the minimum there would hand back a parcel wider than the
            // island it was cut from
            if (total < min) return new List<int> { Mathf.Max(1, total) };
            for (int p = 0; p < parts; p++) cuts.Add(min);
            int spare = total - parts * min;
            for (int k = 0; k < spare; k++) cuts[dice.Next(parts)]++;
            return cuts;
        }

        /// <summary>Where a tier's cross streets fall, in cells: off either end, and
        /// between every two islands.</summary>
        static List<int> Streets(List<int> widths, int at)
        {
            var streets = new List<int> { at - StreetGap };
            int i = at;
            for (int k = 0; k < widths.Count; k++)
            {
                i += widths[k];
                streets.Add(i);
                i += StreetGap;
            }
            return streets;
        }

        /// <summary>Cross streets of two tiers that neither line up nor stand clear.</summary>
        static int Clashes(List<int> mine, List<int> theirs)
        {
            int clash = 0;
            foreach (int street in mine)
                foreach (int other in theirs)
                {
                    int apart = street > other ? street - other : other - street;
                    if (apart > 0 && apart < StreetGap * 2) clash++;
                }
            return clash;
        }

        static int[] Jitters(System.Random dice)
        {
            var jitters = new List<int>();
            for (int j = -6; j <= 6; j++) jitters.Add(j);
            Dice.Shuffle(jitters, dice);
            return jitters.ToArray();
        }

        // ------------------------------------------------------------------ the casting

        /// <summary>
        /// Which recipe stands on which parcel.
        ///
        /// Not a roll per parcel: a quarter dealt that way comes out as eight stockyards
        /// and no chimney one time and five plants the next. The rules are the ones a
        /// works estate is actually laid out by:
        ///
        ///   - the artery frontage is BUILT and brick. Anything fronting the main road is
        ///     a works, a plant or a service strip;
        ///   - the ground behind is wire: stockyards and depots;
        ///   - one HAULAGE yard, on a corner of the artery, because the lorries that serve
        ///     an estate are kept at one end of it and go in and out by the main road. It is
        ///     not a filling station: a retail forecourt with a shop behind it is a roadside
        ///     thing, and the city already has one where it belongs, out on the road between
        ///     the districts (RoadDemoBuilder.Wayside);
        ///   - one tank farm and one empty plot, back among the wire;
        ///   - a chimney SOMEWHERE. A works quarter with nothing smoking over it reads as
        ///     a retail park;
        ///   - and never the same recipe twice running in a row.
        /// </summary>
        static void Cast(Plan plan, System.Random dice)
        {
            foreach (var parcel in plan.Parcels)
            {
                bool artery = Artery(parcel);
                var wants = artery
                    ? new[] { Recipe.Works, Recipe.Plant, Recipe.Works, Recipe.Strip }
                    : new[] { Recipe.Yard, Recipe.Depot, Recipe.Yard, Recipe.Works };
                parcel.Recipe = Choose(wants, parcel, plan, dice);
            }

            // the one-offs, each on the best parcel for it rather than wherever it lands
            var corner = Best(plan, p => Artery(p) && Corner(p) && Fits(Recipe.Haulage, p.W, p.D));
            if (corner != null) corner.Recipe = Recipe.Haulage;

            var tank = Best(plan, p => !Artery(p) && p.Recipe != Recipe.Haulage && Fits(Recipe.Fuel, p.W, p.D));
            if (tank != null) tank.Recipe = Recipe.Fuel;

            var spare = Best(plan, p => !Artery(p) && p.Recipe != Recipe.Haulage && p.Recipe != Recipe.Fuel &&
                                        Fits(Recipe.Waste, p.W, p.D));
            if (spare != null) spare.Recipe = Recipe.Waste;

            // and a chimney, if the deal has left the quarter without one
            bool smoke = false;
            foreach (var parcel in plan.Parcels)
                if (parcel.Recipe == Recipe.Works || parcel.Recipe == Recipe.Plant) { smoke = true; break; }
            if (smoke) return;
            var stack = Best(plan, p => p.Recipe != Recipe.Haulage && p.Recipe != Recipe.Fuel &&
                                        Fits(Recipe.Works, p.W, p.D));
            if (stack != null) stack.Recipe = Recipe.Works;
        }

        /// <summary>Does this parcel front the artery? The first tier either side of it,
        /// near row only - the far row of a doubled tier has its back to the main road.</summary>
        public static bool Artery(Parcel parcel) => parcel.Tier == 1 && !parcel.Back;

        /// <summary>The first recipe on the list that fits the parcel and is not what the
        /// parcel to the west is, falling back to the smallest thing that fits.</summary>
        static Recipe Choose(Recipe[] wants, Parcel parcel, Plan plan, System.Random dice)
        {
            var west = West(plan, parcel);
            int from = dice.Next(wants.Length);
            for (int k = 0; k < wants.Length; k++)
            {
                var want = wants[(from + k) % wants.Length];
                if (!Fits(want, parcel.W, parcel.D)) continue;
                if (west != null && west.Recipe == want) continue;
                return want;
            }
            for (int k = 0; k < wants.Length; k++)
            {
                var want = wants[(from + k) % wants.Length];
                if (Fits(want, parcel.W, parcel.D)) return want;
            }
            // nothing the tier wanted fits: the biggest recipe that does, rather than the
            // smallest. A parcel too shallow for a works is a service strip, but a WIDE one
            // is a strip with sixty metres of nothing beside it
            var most = Recipe.Strip;
            int room = 0;
            foreach (Recipe recipe in System.Enum.GetValues(typeof(Recipe)))
            {
                if (recipe == Recipe.Haulage || recipe == Recipe.Fuel || recipe == Recipe.Waste) continue;
                if (!Fits(recipe, parcel.W, parcel.D)) continue;
                Smallest(recipe, out int mw, out int md);
                if (mw * md <= room) continue;
                room = mw * md;
                most = recipe;
            }
            return most;
        }

        /// <summary>The parcel sharing this one's west fence, if any.</summary>
        static Parcel West(Plan plan, Parcel parcel)
        {
            foreach (var other in plan.Parcels)
                if (other.J0 == parcel.J0 && other.I0 + other.W == parcel.I0) return other;
            return null;
        }

        /// <summary>Is the parcel at the end of its island - the corner where the cross
        /// street meets the artery?</summary>
        static bool Corner(Parcel parcel) =>
            parcel[Side.West].Rim == Rim.Kerb || parcel[Side.East].Rim == Rim.Kerb;

        /// <summary>
        /// A MIDDLING parcel of the ones the test allows - not the roomiest.
        ///
        /// The roomiest was the first rule, and it put the tank farm and the empty plot on
        /// the two biggest plots in the quarter: the two least built recipes standing on the
        /// most ground, so the estate's widest views were its emptiest. Neither wants room -
        /// a tank farm is a tight block of tanks whatever the plot, and an empty plot is
        /// empty by definition. The middle of the field suits both, and leaves the big plots
        /// to the works that fill them.
        /// </summary>
        static Parcel Best(Plan plan, System.Func<Parcel, bool> allowed)
        {
            var fits = new List<Parcel>();
            foreach (var parcel in plan.Parcels)
                if (allowed(parcel)) fits.Add(parcel);
            if (fits.Count == 0) return null;
            fits.Sort((one, other) => (one.W * one.D).CompareTo(other.W * other.D));
            return fits[fits.Count / 2];
        }

        // ----------------------------------------------------------------- the verdict

        /// <summary>How many deals of one seed are tried before the best is taken with its
        /// faults on record. The core's number, for the core's reason.</summary>
        public const int Deals = 40;

        /// <summary>
        /// The quarter a seed gives, with the roads drawn off it and the drawing judged.
        ///
        /// A deal whose drawing has a fault - ground left bare, an island with no road down
        /// a side, a stub of road between two junctions - is thrown away and the seed's next
        /// deal tried. The same seed always runs the same deals, so the same seed is always
        /// the same quarter; if none of them is clean the cleanest is kept and its report
        /// says what is wrong with it.
        /// </summary>
        public static Plan Arrange(int seed, out CoreRoads.Raster raster)
        {
            Plan best = null;
            CoreRoads.Raster bestRaster = null;
            for (int attempt = 0; attempt < Deals; attempt++)
            {
                var plan = Roll(unchecked(seed * 1000003 + attempt * 7919));
                plan.Seed = seed;
                plan.Attempt = attempt;
                plan.Name = $"seed {seed}" + (attempt > 0 ? $" (deal {attempt + 1})" : "");
                var drawn = CoreRoads.Build(Blocks(plan), plan.Roads);
                if (drawn.Faults == 0)
                {
                    raster = drawn;
                    return plan;
                }
                if (bestRaster != null && drawn.Faults >= bestRaster.Faults) continue;
                best = plan;
                bestRaster = drawn;
            }
            raster = bestRaster;
            return best;
        }

        /// <summary>
        /// Deals the landward half of an industrial estate for a host which already owns
        /// the road along its front. The ordinary estate's south half and its 35 m divided
        /// artery are discarded; the surviving service streets still terminate in the
        /// shared road band, but <see cref="IndustrialDistrict"/> does not tile that band.
        /// </summary>
        public static Plan ArrangeRoadside(int seed, out CoreRoads.Raster raster)
        {
            Plan best = null;
            CoreRoads.Raster bestRaster = null;
            for (int attempt = 0; attempt < Deals; attempt++)
            {
                int rolled = unchecked(seed * 1000003 + attempt * 7919);
                var plan = Roll(rolled);
                KeepRoadsideHalf(plan);
                Cast(plan, new System.Random(unchecked(rolled ^ 0x51f15e)));
                plan.Seed = seed;
                plan.Attempt = attempt;
                plan.Name = $"roadside seed {seed}" + (attempt > 0 ? $" (deal {attempt + 1})" : "");

                var drawn = CoreRoads.Build(Blocks(plan), plan.Roads);
                MarkExternalRoad(plan, drawn);
                if (drawn.Faults == 0)
                {
                    raster = drawn;
                    return plan;
                }
                if (bestRaster != null && drawn.Faults >= bestRaster.Faults) continue;
                best = plan;
                bestRaster = drawn;
            }
            MarkExternalRoad(best, bestRaster);
            raster = bestRaster;
            return best;
        }

        static void KeepRoadsideHalf(Plan plan)
        {
            int arteryTo = ArteryGap;
            plan.Islands.RemoveAll(island => island.J0 < arteryTo);
            plan.Parcels.RemoveAll(parcel => parcel.J0 < arteryTo);
            plan.Rows.RemoveAll(row => row.StartsWith("south", System.StringComparison.Ordinal));

            // Bands[0] is the artery. Of the rest keep only the streets behind the north
            // tiers; CoreRoads clips the first band to the 15 m edge road the blocks need.
            for (int k = plan.Roads.Bands.Count - 1; k >= 1; k--)
            {
                if (plan.Roads.Bands[k].yMin < arteryTo * Cell)
                {
                    plan.Roads.Bands.RemoveAt(k);
                    continue;
                }
                // Roll gives every tier road one extra street-width beyond its two edge
                // junctions so neighbouring full-estate tiers can meet. In the roadside
                // zone those lengths are visible as purposeless arms. End the road at the
                // outside edge of each edge junction instead.
                var band = plan.Roads.Bands[k];
                float trim = StreetGap * Cell;
                plan.Roads.Bands[k] = Rect.MinMaxRect(
                    band.xMin + trim, band.yMin,
                    band.xMax - trim, band.yMax);
            }

            for (int k = 0; k < plan.Islands.Count; k++)
            {
                var island = plan.Islands[k];
                island.Name = $"island-{k + 1:00}";
                foreach (var parcel in island.Parcels) parcel.Island = k;
            }
            for (int k = 0; k < plan.Parcels.Count; k++) plan.Parcels[k].Index = k + 1;
        }

        static void MarkExternalRoad(Plan plan, CoreRoads.Raster raster)
        {
            if (plan == null || raster == null) return;
            plan.ExternalArtery = true;
            plan.ExternalRoad = new Vector2(raster.Z0, ArteryGap * Cell);
        }

        /// <summary>The islands as blocks the road reader understands: a solid rectangle
        /// of ground apiece, standing where the deal put it.</summary>
        public static List<CoreLayout.Block> Blocks(Plan plan)
        {
            var blocks = new List<CoreLayout.Block>();
            foreach (var island in plan.Islands)
            {
                var mask = new bool[island.W, island.D];
                for (int i = 0; i < island.W; i++)
                    for (int j = 0; j < island.D; j++) mask[i, j] = true;
                var block = CoreLayout.Describe(island.Name, Vector2.zero, island.W, island.D, mask, 14f);
                block.Pivot = new Vector2(island.I0 * Cell, island.J0 * Cell);
                blocks.Add(block);
            }
            return blocks;
        }

        /// <summary>The quarter's ground, kerb to kerb.</summary>
        public static Rect Bounds(CoreRoads.Raster raster) =>
            Rect.MinMaxRect(raster.X0, raster.Z0, raster.X(raster.NX), raster.Z(raster.NZ));
    }
}
