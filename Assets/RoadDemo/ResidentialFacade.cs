using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace RoadDemo
{
    /// <summary>
    /// Pure, deterministic residential-facade forge. It knows measurements and arithmetic,
    /// but no scene, Transform, asset loader or Unity random state.
    /// </summary>
    public static class ResidentialFacade
    {
        public const int MinLength = 3, MaxLength = 13;
        public const int MinFloors = 3, MaxFloors = 5;
        public const int MinPropsPercent = 0, DefaultPropsPercent = 100,
                         MaxPropsPercent = 200;
        public const int Depth = 2;
        public const float Cell = 5f, Storey = 3f;

        public enum FaultKind
        {
            Hole, Double, CornerOff, RoofPair, ShopAcrossCorner, LoneGlass,
            NoDoor, NoEscape, EscapeOrder, LineLoose, Clash, OutOfBox, Empty,
            UnitMismatch,
        }

        public sealed class Fault
        {
            public FaultKind Kind;
            public int Column;
            public int Floor;
            public string Detail;

            public override string ToString() =>
                $"{Kind} column {Column} floor {Floor}" +
                (string.IsNullOrEmpty(Detail) ? string.Empty : ": " + Detail);
        }

        public readonly struct Piece
        {
            public Piece(string module, int i, int j, int floor, int yaw)
            {
                Module = module ?? string.Empty;
                I = i;
                J = j;
                Floor = floor;
                Yaw = QuarterYaw(yaw);
            }

            public string Module { get; }
            public int I { get; }
            public int J { get; }
            public int Floor { get; }
            public int Yaw { get; }
        }

        public readonly struct Prop
        {
            public Prop(string prefab, float x, float y, float z, float yaw, int column)
            {
                Prefab = prefab ?? string.Empty;
                X = x;
                Y = y;
                Z = z;
                Yaw = NormalYaw(yaw);
                Column = column;
            }

            public string Prefab { get; }
            public float X { get; }
            public float Y { get; }
            public float Z { get; }
            public float Yaw { get; }
            public int Column { get; }
        }

        public sealed class Sheet
        {
            public int Seed;
            public int Length;
            public int Floors;
            public int PropsPercent = DefaultPropsPercent;
            public string Signature;
            public Piece[] Pieces = Array.Empty<Piece>();
            public Prop[] Props = Array.Empty<Prop>();
            public ResidentialUnit Unit;
            public Fault[] Faults = Array.Empty<Fault>();
        }

        public sealed class FamilyRate
        {
            public string Family, Anchor, Relation;
            // Eligible and Requested* retain the measured raw-density question. Capacity
            // is narrowed only where the pre-optional resolver can state an exact limit
            // (currently WindowFloor/Single after required fire escapes); otherwise it is
            // the raw upper bound and does not claim an exact spatial packing maximum.
            public int Actual, Eligible, RequestedMinimum, RequestedMaximum,
                       Capacity, Minimum, Maximum;
            public bool Within => Actual >= Minimum && Actual <= Maximum;
            public override string ToString() =>
                $"{Family}/{Anchor}/{Relation}: {Actual} of {Eligible}, " +
                $"requested {RequestedMinimum}..{RequestedMaximum}, " +
                $"capacity {Capacity}, allowed {Minimum}..{Maximum}" +
                (Capacity < RequestedMinimum ? " (saturated)" : string.Empty);
        }

        sealed class Anchor
        {
            public int I, J, Floor, Yaw, Column;
            public int RateWeight = 1;
            public float X, Y, Z, HostY;
            public ResidentialModule Host;
        }

        // ResidentialDecor is emitted by an editor measurement command. This adapter keeps
        // the forge compileable during the bootstrap pass which creates that source file.
        sealed class DecorRow
        {
            public string Family, Prefab, Anchor, Variant, Relation, HostKind;
            public float X, Y, Z, Yaw, Min, Mean, Max,
                         FamilyMin, FamilyMean, FamilyMax, UnboundShare;
            public int Count, Buildings, Part, Parts, Span, ColumnOffset, RowOffset,
                       FloorOffset, VariantWeight, Role, HostStyle;
            public bool Ready, Repeat;
        }

        sealed class DecorData
        {
            public int[] StyleWeights = { 1, 1, 1 };
            public float FireEscapeShareMin, FireEscapeShareMax;
            public DecorRow[] Rows = Array.Empty<DecorRow>();
        }

        sealed class DecorVariant
        {
            public string Family, Anchor, Relation, Name;
            public int Span, Weight;
            public float Min, Mean, Max, FamilyMin, FamilyMean, FamilyMax;
            public DecorRow[] Rows = Array.Empty<DecorRow>();
        }

        sealed class EscapeRecipe
        {
            public int Row, Column;
            public DecorVariant Variant;
            public DecorRow[] Roles;
        }

        sealed class ExpectedPlacement
        {
            public Prop Prop;
            public DecorVariant Variant;
            public DecorRow Row;
            public int BaseColumn, BaseFloor, MemberColumn, MemberFloor, Occurrence;
        }

        sealed class SupportBox
        {
            public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        }

        sealed class ExpectedGroup
        {
            public string Key, Family, Relation;
            public DecorVariant Variant;
            public Anchor Base;
            public ExpectedPlacement[] Members = Array.Empty<ExpectedPlacement>();
            public long[] Occupancy = Array.Empty<long>();
            public int[] RequiredColumns = Array.Empty<int>();
        }

        sealed class ResolvedVariant
        {
            public DecorVariant Variant;
            public Anchor Base;
            public ExpectedPlacement[] Members = Array.Empty<ExpectedPlacement>();
            public long[] Occupancy = Array.Empty<long>();
            public int[] RequiredColumns = Array.Empty<int>();
        }

        sealed class PlacementAtlas
        {
            public readonly Dictionary<string, List<ExpectedPlacement>> ByTransform =
                new Dictionary<string, List<ExpectedPlacement>>(StringComparer.Ordinal);
            public readonly List<ExpectedGroup> Groups = new List<ExpectedGroup>();
            public bool HasBounds;
            public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        }

        sealed class AnchorIndex
        {
            public readonly Dictionary<(long X, long Z, int Floor), List<Anchor>> ByPosition =
                new Dictionary<(long, long, int), List<Anchor>>();
        }

        sealed class AtlasCacheEntry
        {
            public int Length, Floors;
            public string PiecesKey;
            public PlacementAtlas Atlas;
        }

        static readonly DecorData Decor = ReadDecor();
        static readonly object VariantLock = new object();
        static readonly Dictionary<string, DecorVariant[]> VariantCache =
            new Dictionary<string, DecorVariant[]>(StringComparer.Ordinal);
        static readonly ConditionalWeakTable<List<Anchor>, AnchorIndex> AnchorIndices =
            new ConditionalWeakTable<List<Anchor>, AnchorIndex>();
        static readonly ConditionalWeakTable<Piece[], AtlasCacheEntry> AtlasCache =
            new ConditionalWeakTable<Piece[], AtlasCacheEntry>();
        static readonly object LastAtlasLock = new object();
        static string LastAtlasKey;
        static PlacementAtlas LastAtlas;

        public static Sheet Roll(int seed, int length, int floors)
        {
            return Roll(seed, length, floors, DefaultPropsPercent);
        }

        public static Sheet Roll(int seed, int length, int floors, int propsPercent)
        {
            CheckSize(length, floors);
            CheckPropsPercent(propsPercent);
            return Roll(new Random(seed), seed, length, floors, propsPercent);
        }

        public static Sheet Roll(Random dice, int length, int floors)
        {
            return Roll(dice, length, floors, DefaultPropsPercent);
        }

        public static Sheet Roll(Random dice, int length, int floors, int propsPercent)
        {
            if (dice == null) throw new ArgumentNullException(nameof(dice));
            CheckSize(length, floors);
            CheckPropsPercent(propsPercent);
            int identity = dice.Next(int.MinValue, int.MaxValue);
            return Roll(dice, identity, length, floors, propsPercent);
        }

        static Sheet Roll(Random dice, int seed, int length, int floors, int propsPercent)
        {
            var pieces = new List<Piece>(length * (floors + 2));
            GroundRow(dice, length, 0, pieces);
            GroundRow(dice, length, 1, pieces);
            var escapeRecipes = EscapeRecipes(dice, length, floors);
            var styles = Styles(dice, length, floors, escapeRecipes);
            UpperStoreys(dice, length, floors, styles, escapeRecipes, pieces);
            Roof(dice, length, floors, styles, pieces);

            var props = Decorate(dice, length, floors, pieces, propsPercent);
            string signature = propsPercent == DefaultPropsPercent
                ? $"forge-L{length}-N{floors}-{seed}"
                : $"forge-L{length}-N{floors}-P{propsPercent}-{seed}";
            var pieceArray = pieces.ToArray();
            var propArray = props.ToArray();
            var sheet = new Sheet
            {
                Seed = seed, Length = length, Floors = floors,
                PropsPercent = propsPercent, Signature = signature,
                Pieces = pieceArray, Props = propArray,
                Unit = Describe(signature, length, floors, pieceArray, propArray),
            };
            sheet.Faults = Judge(sheet);
            return sheet;
        }

        static void CheckSize(int length, int floors)
        {
            if (length < MinLength || length > MaxLength)
                throw new ArgumentOutOfRangeException(nameof(length), length,
                    $"Facade length must be {MinLength}..{MaxLength} cells.");
            if (floors < MinFloors || floors > MaxFloors)
                throw new ArgumentOutOfRangeException(nameof(floors), floors,
                    $"Facade height must be {MinFloors}..{MaxFloors} floors.");
        }

        static void CheckPropsPercent(int propsPercent)
        {
            if (propsPercent < MinPropsPercent || propsPercent > MaxPropsPercent)
                throw new ArgumentOutOfRangeException(nameof(propsPercent), propsPercent,
                    $"Optional props must be {MinPropsPercent}..{MaxPropsPercent} percent.");
        }

        static void GroundRow(Random dice, int length, int j, List<Piece> pieces)
        {
            int door = 1 + dice.Next(length - 2);
            var cell = new ResidentialModule[length];
            var owner = new int[length];
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;

            cell[0] = Pick(dice, ResidentialModuleKind.ShopCorner, 0, 1, true);
            cell[length - 1] = Pick(dice, ResidentialModuleKind.ShopCorner, 0, 1, true);
            owner[0] = 0; owner[length - 1] = length - 1;
            cell[door] = Pick(dice, ResidentialModuleKind.ApartmentDoor);
            owner[door] = door;

            for (int i = 1; i < length - 1; i++)
            {
                if (owner[i] >= 0) continue;
                bool wide = i + 1 < length - 1 && owner[i + 1] < 0 &&
                            i + 1 != door && dice.NextDouble() < 0.24;
                var module = wide ? Pick(dice, ResidentialModuleKind.Shop, 0, 2)
                                  : PickDooredShop(dice);
                if (module == null && wide) module = PickDooredShop(dice);
                int cells = module == null ? 1 : Math.Max(1, module.Cells);
                if (i + cells > length - 1 || Contains(door, i, cells))
                {
                    module = PickDooredShop(dice);
                    cells = 1;
                }
                for (int k = 0; k < cells; k++)
                {
                    cell[i + k] = module;
                    owner[i + k] = i;
                }
            }

            // Doorless Shop_05 is only offered after its exact doored neighbour is known.
            if (dice.NextDouble() < 0.38)
            {
                var glass = FindNamed("SM_Bld_Shop_05");
                var choices = new List<int>();
                for (int i = 1; glass != null && i < length - 1; i++)
                {
                    if (i == door || owner[i] != i || cell[i] == null || cell[i].Cells != 1)
                        continue;
                    if (IsDooredShop(cell[i - 1]) || IsDooredShop(cell[i + 1])) choices.Add(i);
                }
                if (choices.Count > 0) cell[choices[dice.Next(choices.Count)]] = glass;
            }

            for (int i = 0; i < length; i++)
            {
                if (owner[i] != i) continue;
                int yaw = j == 0 ? 180 : 0;
                if (i == 0) yaw = j == 0 ? 180 : 270;
                else if (i == length - 1) yaw = j == 0 ? 90 : 0;
                pieces.Add(new Piece(cell[i]?.Name, i, j, 0, yaw));
            }
        }

        static EscapeRecipe[] EscapeRecipes(Random dice, int length, int floors)
        {
            var variants = ReadyVariants("FireEscape", "FireEscapeChain");
            variants.RemoveAll(v => !RecipeFits(v, floors));
            var answer = new List<EscapeRecipe>();
            int needed = Math.Max(1, (int)Math.Ceiling(
                Math.Max(0f, Decor.FireEscapeShareMin) * (length - 2) - 0.0001f));
            for (int j = 0; j < Depth && variants.Count > 0; j++)
            {
                var columns = new List<int>();
                for (int i = 1; i < length - 1; i++) columns.Add(i);
                Shuffle(dice, columns);
                for (int n = 0; n < needed && n < columns.Count; n++)
                {
                    var styles = new List<int>();
                    for (int v = 0; v < variants.Count; v++)
                    {
                        int style = FireRoles(variants[v])[0].HostStyle;
                        if (!styles.Contains(style)) styles.Add(style);
                    }
                    int chosenStyle = PickStyle(dice, styles);
                    var matching = variants.FindAll(v =>
                        FireRoles(v)[0].HostStyle == chosenStyle);
                    var variant = PickVariant(dice, matching);
                    answer.Add(new EscapeRecipe
                    {
                        Row = j, Column = columns[n], Variant = variant,
                        Roles = FireRoles(variant),
                    });
                }
            }
            return answer.ToArray();
        }

        static bool RecipeFits(DecorVariant variant, int floors)
        {
            var roles = FireRoles(variant);
            if (roles == null) return false;
            int style = roles[0].HostStyle;
            for (int r = 0; r < roles.Length; r++)
                if (roles[r].HostStyle != style || style < 1 || style > 3 ||
                    roles[r].HostKind != ResidentialModuleKind.Apartment.ToString() &&
                    roles[r].HostKind != ResidentialModuleKind.ApartmentStack.ToString())
                    return false;
            int floor = 1;
            while (floor <= floors)
            {
                int role = floor == 1 ? 1 : (floor == floors ? 3 : 2);
                string kind = roles[role - 1].HostKind;
                if (kind == ResidentialModuleKind.Apartment.ToString()) { floor++; continue; }
                if (floor + 2 > floors) return false;
                for (int f = floor; f < floor + 3; f++)
                {
                    int coveredRole = f == 1 ? 1 : (f == floors ? 3 : 2);
                    if (roles[coveredRole - 1].HostKind !=
                        ResidentialModuleKind.ApartmentStack.ToString()) return false;
                }
                floor += 3;
            }
            return true;
        }

        static int[,,] Styles(Random dice, int length, int floors,
                              EscapeRecipe[] escapeRecipes)
        {
            var answer = new int[length, Depth, floors + 1];
            for (int j = 0; j < Depth; j++)
            {
                for (int i = 1; i < length - 1; i++)
                {
                    int style = PickStyle(dice);
                    for (int floor = 1; floor <= floors; floor++)
                        answer[i, j, floor] = style;
                }
                for (int floor = 1; floor <= floors; floor++)
                {
                    answer[0, j, floor] = answer[1, j, floor];
                    answer[length - 1, j, floor] = answer[length - 2, j, floor];
                }
            }
            for (int e = 0; e < escapeRecipes.Length; e++)
            {
                var recipe = escapeRecipes[e];
                for (int floor = 1; floor <= floors; floor++)
                {
                    int role = floor == 1 ? 1 : (floor == floors ? 3 : 2);
                    answer[recipe.Column, recipe.Row, floor] =
                        recipe.Roles[role - 1].HostStyle;
                }
            }
            // A corner's cornice is paired with its nearest long-edge bay. Re-copy after
            // the measured escape recipe has specialized that neighbour's vertical styles.
            for (int j = 0; j < Depth; j++)
                for (int floor = 1; floor <= floors; floor++)
                {
                    answer[0, j, floor] = answer[1, j, floor];
                    answer[length - 1, j, floor] = answer[length - 2, j, floor];
                }
            return answer;
        }

        static void UpperStoreys(Random dice, int length, int floors, int[,,] styles,
                                  EscapeRecipe[] escapeRecipes, List<Piece> pieces)
        {
            for (int j = 0; j < Depth; j++)
                for (int i = 0; i < length; i++)
                {
                    bool corner = i == 0 || i == length - 1;
                    int yaw = corner
                        ? (j == 0 ? (i == 0 ? 180 : 90) : (i == 0 ? 270 : 0))
                        : (j == 0 ? 180 : 0);
                    if (corner)
                    {
                        for (int floor = 1; floor <= floors; floor++)
                            pieces.Add(new Piece(Pick(dice,
                                ResidentialModuleKind.ApartmentCorner, styles[i, j, floor], 1, true)?.Name,
                                i, j, floor, yaw));
                        continue;
                    }
                    EscapeRecipe escapeRecipe = null;
                    for (int e = 0; e < escapeRecipes.Length; e++)
                        if (escapeRecipes[e].Row == j && escapeRecipes[e].Column == i)
                            escapeRecipe = escapeRecipes[e];
                    if (escapeRecipe != null)
                    {
                        int floor = 1;
                        while (floor <= floors)
                        {
                            int role = floor == 1 ? 1 : (floor == floors ? 3 : 2);
                            bool stackHost = escapeRecipe.Roles[role - 1].HostKind ==
                                             ResidentialModuleKind.ApartmentStack.ToString();
                            var kind = stackHost ? ResidentialModuleKind.ApartmentStack :
                                                   ResidentialModuleKind.Apartment;
                            pieces.Add(new Piece(Pick(dice, kind, styles[i, j, floor])?.Name,
                                                 i, j, floor, yaw));
                            floor += stackHost ? 3 : 1;
                        }
                        continue;
                    }
                    int style = styles[i, j, 1];
                    bool stack = floors >= 3 &&
                                 styles[i, j, 2] == style && styles[i, j, 3] == style &&
                                 Has(ResidentialModuleKind.ApartmentStack, style) &&
                                 dice.NextDouble() < 0.58;
                    int firstSingle = 1;
                    if (stack)
                    {
                        pieces.Add(new Piece(Pick(dice,
                            ResidentialModuleKind.ApartmentStack, style)?.Name,
                            i, j, 1, yaw));
                        firstSingle = 4;
                    }
                    for (int floor = firstSingle; floor <= floors; floor++)
                        pieces.Add(new Piece(Pick(dice,
                            ResidentialModuleKind.Apartment, styles[i, j, floor])?.Name,
                            i, j, floor, yaw));
                }
        }

        static void Roof(Random dice, int length, int floors, int[,,] styles,
                         List<Piece> pieces)
        {
            int floor = floors + 1;
            for (int j = 0; j < Depth; j++)
                for (int i = 0; i < length; i++)
                {
                    bool corner = i == 0 || i == length - 1;
                    int yaw = corner
                        ? (j == 0 ? (i == 0 ? 180 : 90) : (i == 0 ? 270 : 0))
                        : (j == 0 ? 180 : 0);
                    pieces.Add(new Piece(PickRoof(dice, corner, styles[i, j, floors])?.Name,
                                         i, j, floor, yaw));
                }
        }

        static List<Prop> Decorate(Random dice, int length, int floors,
                                   List<Piece> pieces, int propsPercent)
        {
            var props = new List<Prop>();
            var allWindows = WindowAnchors(length, floors, pieces, null);
            var escapeColumns = new HashSet<int>();
            FireEscapes(dice, length, floors, props, escapeColumns, allWindows);

            var shops = ShopAnchors(length, pieces);
            var windows = WindowAnchors(length, floors, pieces, escapeColumns);
            var roof = RoofAnchors(length, floors, pieces);
            var reservedRoof = new HashSet<long>();
            var reservedWindow = new HashSet<long>();

            // Fire escapes and RoofAccess are grammar, not density-controlled dressing.
            // They consume all of their random choices before any optional family so the
            // same seed has the same required prop multiset at every density.
            PlaceSingleFamilies(dice, length, floors, props, shops, windows, roof,
                                reservedRoof, reservedWindow,
                                new[] { "RoofAccess" },
                                ShopRateDenominator(shops), allWindows.Count, roof.Count,
                                DefaultPropsPercent);

            if (propsPercent == MinPropsPercent) return props;

            WashingLines(dice, length, floors, props, escapeColumns, allWindows,
                         propsPercent);

            // Measured non-zero minima get first claim on their independently eligible
            // anchors. Optional relations are fitted around that guaranteed stock.
            PlaceSingleFamilies(dice, length, floors, props, shops, windows, roof,
                                reservedRoof, reservedWindow, new[] { "Aircon" },
                                ShopRateDenominator(shops), allWindows.Count, roof.Count,
                                propsPercent);

            // Relations are templates, not bags of members. A selected billboard, vent
            // chain or terrace stands every measured member of one observed Variant.
            PlaceRoofGroups(dice, length, floors, props, roof, reservedRoof, roof.Count,
                            "BillboardPair", "Billboard", propsPercent);
            PlaceRoofGroups(dice, length, floors, props, roof, reservedRoof, roof.Count,
                            "TerraceGroup", "Terrace", propsPercent);
            PlaceRoofGroups(dice, length, floors, props, roof, reservedRoof, roof.Count,
                            "VentChain", "Vent", propsPercent);

            string[] families =
            {
                "Skylight", "RoofAircon", "SatelliteDish",
                "Window", "WindowPlanter", "PowerBox",
                "ShopCover", "Sign", "LargeSign",
            };
            PlaceSingleFamilies(dice, length, floors, props, shops, windows, roof,
                                reservedRoof, reservedWindow, families,
                                ShopRateDenominator(shops), allWindows.Count, roof.Count,
                                propsPercent);
            return props;
        }

        static void PlaceSingleFamilies(Random dice, int length, int floors, List<Prop> props,
                                        List<Anchor> shops, List<Anchor> windows,
                                        List<Anchor> roof, HashSet<long> reservedRoof,
                                        HashSet<long> reservedWindow, string[] families,
                                        int shopDenominator, int windowDenominator,
                                        int roofDenominator, int propsPercent)
        {
            for (int f = 0; f < families.Length; f++)
            {
                string family = families[f];
                var variants = ReadyVariants(family, "Single");
                var anchorKinds = VariantAnchors(variants);
                for (int k = 0; k < anchorKinds.Count; k++)
                {
                    string anchorKind = anchorKinds[k];
                    var bucket = variants.FindAll(v => v.Anchor == anchorKind);
                    var anchors = Anchors(anchorKind, shops, windows, roof);
                    var eligible = EligibleAnchors(bucket, anchors, length, floors,
                                                   reservedRoof, reservedWindow);
                    bool roofExclusive = IsRoofAnchor(anchorKind);
                    bool windowExclusive = anchorKind == "WindowFloor";
                    int denominator = RateDenominator(anchorKind, shopDenominator,
                                                      windowDenominator, roofDenominator);
                    int wanted = family == "RoofAccess"
                        ? 1 + dice.Next(2)
                        : DecorTarget(dice, bucket, denominator, eligible.Count,
                                      propsPercent);
                    int target = Math.Min(eligible.Count,
                        wanted);
                    Shuffle(dice, eligible);
                    int placed = 0;
                    for (int a = 0; a < eligible.Count && placed < target; a++)
                    {
                        var fitting = ResolveVariants(bucket, eligible[a], anchors, length, floors,
                                                      reservedRoof, reservedWindow);
                        var chosen = PickResolved(dice, fitting);
                        if (chosen == null) continue;
                        AddResolved(chosen, props);
                        if (roofExclusive) AddAll(reservedRoof, chosen.Occupancy);
                        if (windowExclusive) AddAll(reservedWindow, chosen.Occupancy);
                        placed++;
                    }
                }
            }
        }

        static void PlaceRoofGroups(Random dice, int length, int floors, List<Prop> props,
                                    List<Anchor> roof, HashSet<long> reserved, int denominator,
                                    string relation, string family, int propsPercent)
        {
            var variants = ReadyVariants(family, relation);
            if (variants.Count == 0) return;
            var initial = EligibleAnchors(variants, roof, length, floors, reserved, null);
            int target = DecorTarget(dice, variants, denominator, initial.Count,
                                     propsPercent);
            int attempts = 0, placed = 0;
            while (placed < target && attempts++ < Math.Max(8, target * 8))
            {
                var eligible = EligibleAnchors(variants, roof, length, floors,
                                               reserved, null);
                if (eligible.Count == 0) continue;
                var anchor = eligible[dice.Next(eligible.Count)];
                var fitting = ResolveVariants(variants, anchor, roof, length, floors,
                                              reserved, null);
                var chosen = PickResolved(dice, fitting);
                if (chosen == null) continue;
                AddResolved(chosen, props);
                AddAll(reserved, chosen.Occupancy);
                placed++;
            }
        }

        static void FireEscapes(Random dice, int length, int floors, List<Prop> props,
                                HashSet<int> selected, List<Anchor> windows)
        {
            var variants = ReadyVariants("FireEscape", "FireEscapeChain");
            variants.RemoveAll(v => FireRoles(v) == null);
            if (variants.Count == 0) return;
            float lo = Math.Max(0f, Decor.FireEscapeShareMin);
            float hi = Math.Max(lo, Decor.FireEscapeShareMax);
            for (int j = 0; j < Depth; j++)
            {
                int possible = length - 2;
                int minCount = Math.Max(1, (int)Math.Ceiling(lo * possible - 0.0001f));
                int maxCount = Math.Max(minCount,
                    Math.Min(possible, (int)Math.Floor(hi * possible + 0.0001f)));
                int count = minCount == maxCount ? minCount : dice.Next(minCount, maxCount + 1);
                var candidates = new List<Anchor>(possible);
                for (int i = 1; i < length - 1; i++)
                {
                    var anchor = FindAnchor(windows, i, j, 1);
                    if (anchor != null && ResolveVariants(variants, anchor, windows,
                                                          length, floors, null, null).Count > 0)
                        candidates.Add(anchor);
                }
                count = Math.Min(count, candidates.Count);
                Shuffle(dice, candidates);
                for (int c = 0; c < count; c++)
                {
                    var fitting = ResolveVariants(variants, candidates[c], windows,
                                                  length, floors, null, null);
                    var chosen = PickResolved(dice, fitting);
                    if (chosen == null) continue;
                    selected.Add(candidates[c].Column);
                    AddResolved(chosen, props);
                }
            }
        }

        static void WashingLines(Random dice, int length, int floors, List<Prop> props,
                                 HashSet<int> escapes, List<Anchor> windows,
                                 int propsPercent)
        {
            var variants = ReadyVariants("WashingLine", "WashingLinePair");
            if (variants.Count == 0) return;
            var candidates = new List<ResolvedVariant>();
            for (int a = 0; a < windows.Count; a++)
            {
                var anchor = windows[a];
                if (anchor.Floor != 1 || anchor.I <= 0 || anchor.I >= length - 1) continue;
                var fitting = ResolveVariants(variants, anchor, windows, length, floors,
                                              null, null);
                for (int v = 0; v < fitting.Count; v++)
                {
                    var resolved = fitting[v];
                    bool bound = resolved.RequiredColumns.Length >= 2;
                    for (int c = 0; bound && c < resolved.RequiredColumns.Length; c++)
                        bound = escapes.Contains(resolved.RequiredColumns[c]);
                    if (bound) candidates.Add(resolved);
                }
            }
            int target = Math.Min(candidates.Count,
                                  DecorTarget(dice, variants, candidates.Count,
                                              candidates.Count, propsPercent));
            Shuffle(dice, candidates);
            var used = new HashSet<string>(StringComparer.Ordinal);
            int placed = 0;
            for (int i = 0; i < candidates.Count && placed < target; i++)
            {
                string key = string.Join(",", candidates[i].RequiredColumns);
                if (!used.Add(key)) continue;
                AddResolved(candidates[i], props);
                placed++;
            }
        }

        static List<Anchor> ShopAnchors(int length, List<Piece> pieces)
        {
            var answer = new List<Anchor>();
            for (int p = 0; p < pieces.Count; p++)
            {
                var piece = pieces[p];
                if (piece.Floor != 0) continue;
                var module = FindNamed(piece.Module);
                if (module == null || module.Kind != ResidentialModuleKind.Shop &&
                                      module.Kind != ResidentialModuleKind.ShopCorner) continue;
                Pivot(piece, module, out float x, out float z);
                answer.Add(new Anchor
                {
                    I = piece.I, J = piece.J, Floor = 0, Yaw = piece.Yaw,
                    Column = Column(length, piece.I, piece.J), X = x, Y = 0f, Z = z,
                    HostY = 0f, Host = module,
                    RateWeight = Math.Max(1, module.Cells),
                });
            }
            return answer;
        }

        static int ShopRateDenominator(List<Anchor> shops)
        {
            int answer = 0;
            for (int i = 0; i < shops.Count; i++) answer += Math.Max(1, shops[i].RateWeight);
            return answer;
        }

        static int RateDenominator(string anchor, int shops, int windows, int roof)
        {
            if (anchor == "Shop") return shops;
            if (anchor == "WindowFloor" || anchor == "FireEscapeColumn" ||
                anchor == "WashingLine") return windows;
            if (IsRoofAnchor(anchor)) return roof;
            return 0;
        }

        static List<Anchor> WindowAnchors(int length, int floors, List<Piece> pieces,
                                          HashSet<int> escapes)
        {
            var answer = new List<Anchor>(length * Depth * floors);
            for (int p = 0; p < pieces.Count; p++)
            {
                var piece = pieces[p];
                var module = FindNamed(piece.Module);
                if (piece.Floor < 1 || module == null ||
                    module.Kind != ResidentialModuleKind.Apartment &&
                    module.Kind != ResidentialModuleKind.ApartmentStack &&
                    module.Kind != ResidentialModuleKind.ApartmentCorner) continue;
                int rise = module.Kind == ResidentialModuleKind.ApartmentStack
                    ? Math.Max(1, module.Floors) : 1;
                Pivot(piece, module, out float x, out float z);
                for (int f = 0; f < rise && piece.Floor + f <= floors; f++)
                {
                    int floor = piece.Floor + f;
                    int column = Column(length, piece.I, piece.J);
                    if (escapes != null && escapes.Contains(column)) continue;
                    answer.Add(new Anchor
                    {
                        I = piece.I, J = piece.J, Floor = floor, Yaw = piece.Yaw,
                        Column = column, X = x, Y = floor * Storey, Z = z, Host = module,
                        HostY = piece.Floor * Storey,
                    });
                }
            }
            return answer;
        }

        static List<Anchor> RoofAnchors(int length, int floors, List<Piece> pieces)
        {
            var answer = new List<Anchor>(length * Depth);
            for (int p = 0; p < pieces.Count; p++)
            {
                var piece = pieces[p];
                var module = FindNamed(piece.Module);
                if (piece.Floor != floors + 1 || module == null ||
                    module.Kind != ResidentialModuleKind.Roof &&
                    module.Kind != ResidentialModuleKind.RoofCorner) continue;
                Pivot(piece, module, out float x, out float z);
                answer.Add(new Anchor
                {
                    I = piece.I, J = piece.J, Floor = piece.Floor, Yaw = piece.Yaw,
                    Column = Column(length, piece.I, piece.J), X = x,
                    Y = piece.Floor * Storey, Z = z,
                    HostY = piece.Floor * Storey, Host = module,
                });
            }
            return answer;
        }

        static Prop Place(DecorRow rule, Anchor anchor)
        {
            Turn(rule.X, rule.Z, anchor.Yaw, out float dx, out float dz);
            return new Prop(rule.Prefab, anchor.X + dx, anchor.Y + rule.Y,
                            anchor.Z + dz, anchor.Yaw + rule.Yaw, anchor.Column);
        }

        static List<Anchor> Anchors(string kind, List<Anchor> shops,
                                    List<Anchor> windows, List<Anchor> roof)
        {
            if (kind == "Shop") return shops;
            if (kind == "WindowFloor") return windows;
            if (kind == "RoofCell" || kind == "RoofAccess" || kind == "Billboard" ||
                kind == "Terrace" || kind == "VentChain") return roof;
            return new List<Anchor>();
        }

        static int DecorTarget(Random dice, List<DecorVariant> variants, int eligible,
                               int capacity, int propsPercent)
        {
            if (eligible == 0 || variants.Count == 0) return 0;
            DecorRange(variants[0].FamilyMin, variants[0].FamilyMean,
                       variants[0].FamilyMax, eligible, propsPercent,
                       out int low, out int high, out double expected);
            capacity = Math.Max(0, Math.Min(eligible, capacity));
            low = Math.Min(low, capacity);
            high = Math.Min(high, capacity);
            expected = Math.Min(capacity, Math.Max(low, Math.Min(high, expected)));
            int target = (int)Math.Floor(expected);
            if (target < high && dice.NextDouble() < expected - target) target++;
            return target;
        }

        static void DecorRange(float rawMin, float rawMean, float rawMax, int eligible,
                               int propsPercent, out int low, out int high,
                               out double expected)
        {
            float scale = propsPercent / 100f;
            float min = Math.Max(0f, rawMin * scale);
            float mean = Math.Max(0f, rawMean * scale);
            float max = Math.Max(min, rawMax * scale);
            low = Math.Min(eligible,
                Math.Max(0, (int)Math.Ceiling(min * eligible - 0.0001f)));
            high = Math.Min(eligible,
                Math.Max(low, (int)Math.Floor(max * eligible + 0.0001f)));
            expected = Math.Max(low, Math.Min(high, mean * eligible));
        }

        static bool IsRoofFamily(string family) =>
            family == "RoofAccess" || family == "Billboard" || family == "Terrace" ||
            family == "Vent" || family == "Skylight" || family == "RoofAircon" ||
            family == "SatelliteDish";

        static bool IsRoofAnchor(string anchor) =>
            anchor == "RoofCell" || anchor == "RoofAccess" || anchor == "Billboard" ||
            anchor == "Terrace" || anchor == "VentChain";

        static long WindowKey(int column, int floor) => ((long)column << 32) ^ (uint)floor;

        static List<DecorVariant> ReadyVariants(string family, string relation)
        {
            string request = (family ?? string.Empty) + "\n" + (relation ?? string.Empty);
            lock (VariantLock)
                if (VariantCache.TryGetValue(request, out var cached))
                    return new List<DecorVariant>(cached);
            var answer = new List<DecorVariant>();
            var byName = new Dictionary<string, DecorVariant>(StringComparer.Ordinal);
            for (int i = 0; i < Decor.Rows.Length; i++)
            {
                var row = Decor.Rows[i];
                if (!string.IsNullOrEmpty(family) && row.Family != family ||
                    !string.IsNullOrEmpty(relation) && row.Relation != relation) continue;
                string name = string.IsNullOrEmpty(row.Variant) ? "legacy-" + i : row.Variant;
                string key = row.Family + "\n" + row.Relation + "\n" + name;
                if (!byName.TryGetValue(key, out var variant))
                {
                    variant = new DecorVariant
                    {
                        Family = row.Family, Anchor = row.Anchor, Relation = row.Relation,
                        Name = name, Span = Math.Max(1, row.Span),
                        Weight = Math.Max(1, row.VariantWeight),
                        Min = row.Min, Mean = row.Mean, Max = row.Max,
                        FamilyMin = row.FamilyMin, FamilyMean = row.FamilyMean,
                        FamilyMax = row.FamilyMax,
                    };
                    byName[key] = variant;
                    answer.Add(variant);
                }
                var rows = new List<DecorRow>(variant.Rows) { row };
                variant.Rows = rows.ToArray();
            }
            answer.RemoveAll(variant => !ValidVariant(variant));
            lock (VariantLock) VariantCache[request] = answer.ToArray();
            return answer;
        }

        static bool ValidVariant(DecorVariant variant)
        {
            if (variant == null || variant.Rows.Length == 0) return false;
            int parts = Math.Max(1, variant.Rows[0].Parts);
            if (variant.Rows.Length != parts) return false;
            var seen = new bool[parts];
            for (int i = 0; i < variant.Rows.Length; i++)
            {
                var row = variant.Rows[i];
                if (!row.Ready || row.UnboundShare > 0.2f || row.Family != variant.Family ||
                    row.Anchor != variant.Anchor || row.Relation != variant.Relation ||
                    row.Parts != parts || row.Part < 0 || row.Part >= parts || seen[row.Part])
                    return false;
                seen[row.Part] = true;
            }
            Array.Sort(variant.Rows, (a, b) => a.Part.CompareTo(b.Part));
            return true;
        }

        static DecorVariant PickVariant(Random dice, List<DecorVariant> variants)
        {
            if (variants == null || variants.Count == 0) return null;
            int total = 0;
            for (int i = 0; i < variants.Count; i++) total += Math.Max(1, variants[i].Weight);
            int roll = dice.Next(total);
            for (int i = 0; i < variants.Count; i++)
            {
                roll -= Math.Max(1, variants[i].Weight);
                if (roll < 0) return variants[i];
            }
            return variants[variants.Count - 1];
        }

        static List<string> VariantAnchors(List<DecorVariant> variants)
        {
            var answer = new List<string>();
            for (int i = 0; i < variants.Count; i++)
                if (!answer.Contains(variants[i].Anchor)) answer.Add(variants[i].Anchor);
            return answer;
        }

        static DecorRow[] FireRoles(DecorVariant variant)
        {
            if (variant == null) return null;
            var roles = new DecorRow[3];
            for (int i = 0; i < variant.Rows.Length; i++)
            {
                var row = variant.Rows[i];
                if (row.Role < 1 || row.Role > 3 || roles[row.Role - 1] != null) return null;
                var module = FindPrefab(row.Prefab);
                if (module?.Kind != ResidentialModuleKind.FireEscape ||
                    module.EscapeOrder != row.Role) return null;
                roles[row.Role - 1] = row;
            }
            return roles[0] != null && roles[1]?.Repeat == true && roles[2] != null
                ? roles : null;
        }

        static List<Anchor> EligibleAnchors(List<DecorVariant> variants, List<Anchor> anchors,
                                            int length, int floors, HashSet<long> reservedRoof,
                                            HashSet<long> reservedWindow)
        {
            var answer = new List<Anchor>();
            for (int a = 0; a < anchors.Count; a++)
                if (ResolveVariants(variants, anchors[a], anchors, length, floors,
                                    reservedRoof, reservedWindow).Count > 0)
                    answer.Add(anchors[a]);
            return answer;
        }

        static List<ResolvedVariant> ResolveVariants(List<DecorVariant> variants, Anchor anchor,
                                                      List<Anchor> anchors, int length, int floors,
                                                      HashSet<long> reservedRoof,
                                                      HashSet<long> reservedWindow)
        {
            var answer = new List<ResolvedVariant>();
            for (int i = 0; i < variants.Count; i++)
                if (TryResolveVariant(variants[i], anchor, anchors, length, floors,
                                      out var resolved) &&
                    !Overlaps(resolved.Occupancy, reservedRoof) &&
                    !Overlaps(resolved.Occupancy, reservedWindow))
                    answer.Add(resolved);
            return answer;
        }

        static bool TryResolveVariant(DecorVariant variant, Anchor anchor, List<Anchor> anchors,
                                      int length, int floors, out ResolvedVariant resolved)
        {
            resolved = null;
            if (variant == null || anchor == null || anchors == null) return false;
            var members = new List<ExpectedPlacement>();
            if (variant.Relation == "FireEscapeChain")
            {
                var roles = FireRoles(variant);
                if (roles == null || anchor.Floor != 1) return false;
                for (int floor = 1; floor <= floors; floor++)
                {
                    int role = floor == 1 ? 1 : (floor == floors ? 3 : 2);
                    var row = roles[role - 1];
                    var member = FindAnchorAt(anchors, anchor.X, anchor.Z, floor, row);
                    if (member == null) return false;
                    members.Add(Expected(row, variant, anchor, member, floor - 1));
                }
            }
            else
            {
                for (int i = 0; i < variant.Rows.Length; i++)
                {
                    var row = variant.Rows[i];
                    var member = OffsetAnchor(anchors, anchor, row.ColumnOffset,
                                              row.RowOffset, row.FloorOffset, row);
                    if (member == null) return false;
                    members.Add(Expected(row, variant, anchor, member, 0));
                }
            }

            if (variant.Relation == "FireEscapeChain" &&
                !FireEscapeOnFloorLattice(members)) return false;
            // Some harvested roofs use a negative X/Z source scale (a 180 degree parity
            // flip) which is not represented by the table's quaternion-only host yaw.
            // Never transplant such a template beside an outer corner: all four lower
            // footprint corners of the actual base must land on generated roof renderers.
            if (!PhysicalGroupFits(variant, members, anchors, length)) return false;
            if (variant.Relation == "BillboardPair" &&
                !BillboardHasRoofSupport(members, anchors)) return false;

            var required = new List<int>();
            if (!required.Contains(anchor.Column)) required.Add(anchor.Column);
            for (int i = 0; i < members.Count; i++)
                if (!required.Contains(members[i].MemberColumn))
                    required.Add(members[i].MemberColumn);
            // Span is only a scalar frequency/fit hint for spatial groups. Their real 2D
            // footprint comes from signed member offsets. WashingLinePair is the one
            // relation whose second support is an empty endpoint rather than a prefab row.
            if (variant.Relation == "WashingLinePair" && Math.Max(1, variant.Span) >= 2)
            {
                var occupied = OffsetAnchor(anchors, anchor, 1, 0, 0, null);
                if (occupied == null) return false;
                if (!required.Contains(occupied.Column)) required.Add(occupied.Column);
            }

            var occupancy = new List<long>();
            bool cellExclusive = variant.Relation == "FireEscapeChain" ||
                                 variant.Anchor == "WindowFloor" ||
                                 IsRoofAnchor(variant.Anchor);
            if (cellExclusive)
                for (int i = 0; i < members.Count; i++)
                {
                    int occupiedFloor = variant.Anchor == "WindowFloor"
                        ? (int)Math.Floor((members[i].Prop.Y + 0.001f) / Storey)
                        : members[i].MemberFloor;
                    long key = WindowKey(members[i].MemberColumn, occupiedFloor);
                    if (!occupancy.Contains(key)) occupancy.Add(key);
                }
            resolved = new ResolvedVariant
            {
                Variant = variant, Base = anchor, Members = members.ToArray(),
                Occupancy = occupancy.ToArray(), RequiredColumns = required.ToArray(),
            };
            return true;
        }

        static bool BillboardHasRoofSupport(List<ExpectedPlacement> members,
                                            List<Anchor> roof)
        {
            ExpectedPlacement footing = null;
            ResidentialModule billboard = null;
            for (int i = 0; i < members.Count; i++)
            {
                var module = FindPrefab(members[i].Prop.Prefab);
                if (module == null || !module.Name.StartsWith(
                    "SM_Prop_Billboard_Roof_", StringComparison.Ordinal)) continue;
                footing = members[i];
                billboard = module;
                break;
            }
            if (footing == null || billboard == null) return false;

            float supportY = footing.Prop.Y + billboard.MinY;
            float[] localX = { billboard.MinX, billboard.MaxX };
            float[] localZ = { billboard.MinZ, billboard.MaxZ };
            const float tolerance = 0.02f;
            for (int x = 0; x < localX.Length; x++)
                for (int z = 0; z < localZ.Length; z++)
                {
                    TurnExact(localX[x], localZ[z], footing.Prop.Yaw,
                              out float dx, out float dz);
                    float supportX = footing.Prop.X + dx;
                    float supportZ = footing.Prop.Z + dz;
                    bool supported = false;
                    for (int i = 0; i < roof.Count && !supported; i++)
                    {
                        var host = roof[i];
                        if (host?.Host == null || host.Host.Kind != ResidentialModuleKind.Roof &&
                                                  host.Host.Kind != ResidentialModuleKind.RoofCorner)
                            continue;
                        TransformBounds(host.Host, host.X, host.Y, host.Z, host.Yaw,
                                        out float roofMinX, out float roofMinY,
                                        out float roofMinZ, out float roofMaxX,
                                        out float roofMaxY, out float roofMaxZ);
                        supported = supportX >= roofMinX - tolerance &&
                                    supportX <= roofMaxX + tolerance &&
                                    supportZ >= roofMinZ - tolerance &&
                                    supportZ <= roofMaxZ + tolerance &&
                                    supportY >= roofMinY - tolerance &&
                                    supportY <= roofMaxY + tolerance;
                    }
                    if (!supported) return false;
                }
            return true;
        }

        const float SupportTolerance = 0.02f;
        const float JoinedPartTolerance = 0.12f;

        static bool PhysicalGroupFits(DecorVariant variant,
                                      List<ExpectedPlacement> members,
                                      List<Anchor> anchors, int length)
        {
            if (variant == null || members == null || members.Count == 0) return false;
            var boxes = new SupportBox[members.Count];
            var supported = new bool[members.Count];
            bool roofGroup = IsRoofAnchor(variant.Anchor);
            float lowest = float.MaxValue;
            for (int i = 0; i < members.Count; i++)
            {
                var module = FindPrefab(members[i].Prop.Prefab);
                if (module == null) return false;
                boxes[i] = SupportBounds(module, members[i].Prop.X, members[i].Prop.Y,
                                         members[i].Prop.Z, members[i].Prop.Yaw);
                // A root below the lot is never a supported facade attachment. Renderer
                // bounds may legitimately dip below a correctly authored pivot.
                if (members[i].Prop.Y < -SupportTolerance) return false;
                lowest = Math.Min(lowest, boxes[i].MinY);
                supported[i] = roofGroup
                    ? HasRoofFooting(boxes[i], anchors)
                    : TouchesStructuralHost(boxes[i], anchors) ||
                      HasGroundFooting(members[i].Prop, boxes[i], length);
            }

            // Every lowest roof member needs its own footing. Raised atomic members such
            // as a billboard face may inherit support through a touching base, but an
            // entire vent/terrace cluster cannot be suspended from one distant member.
            if (roofGroup)
                for (int i = 0; i < boxes.Length; i++)
                    if (boxes[i].MinY <= lowest + SupportTolerance && !supported[i])
                        return false;

            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < boxes.Length; i++)
                {
                    if (supported[i]) continue;
                    for (int j = 0; j < boxes.Length; j++)
                        if (supported[j] && BoxesTouch(boxes[i], boxes[j],
                                                       JoinedPartTolerance))
                        {
                            supported[i] = true;
                            changed = true;
                            break;
                        }
                }
            } while (changed);
            for (int i = 0; i < supported.Length; i++)
                if (!supported[i]) return false;
            return true;
        }

        static bool HasRoofFooting(SupportBox prop, List<Anchor> anchors)
        {
            float x = (prop.MinX + prop.MaxX) * 0.5f;
            float z = (prop.MinZ + prop.MaxZ) * 0.5f;
            for (int i = 0; i < anchors.Count; i++)
            {
                var host = anchors[i];
                if (host?.Host == null || host.Host.Kind != ResidentialModuleKind.Roof &&
                                          host.Host.Kind != ResidentialModuleKind.RoofCorner)
                    continue;
                var roof = SupportBounds(host.Host, host.X, host.HostY, host.Z, host.Yaw);
                if (x >= roof.MinX - SupportTolerance &&
                    x <= roof.MaxX + SupportTolerance &&
                    z >= roof.MinZ - SupportTolerance &&
                    z <= roof.MaxZ + SupportTolerance &&
                    prop.MinY >= roof.MinY - SupportTolerance &&
                    prop.MinY <= roof.MaxY + SupportTolerance)
                    return true;
            }
            return false;
        }

        static bool TouchesStructuralHost(SupportBox prop, List<Anchor> anchors)
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                var anchor = anchors[i];
                if (anchor?.Host == null) continue;
                var host = SupportBounds(anchor.Host, anchor.X, anchor.HostY,
                                         anchor.Z, anchor.Yaw);
                if (BoxesTouch(prop, host, SupportTolerance)) return true;
            }
            return false;
        }

        static bool HasGroundFooting(Prop prop, SupportBox box, int length)
        {
            if (Math.Abs(prop.Y) > SupportTolerance) return false;
            float x = (box.MinX + box.MaxX) * 0.5f;
            float z = (box.MinZ + box.MaxZ) * 0.5f;
            return x >= -Cell - SupportTolerance &&
                   x <= length * Cell + Cell + SupportTolerance &&
                   z >= -Cell - SupportTolerance &&
                   z <= Depth * Cell + Cell + SupportTolerance;
        }

        static bool BoxesTouch(SupportBox a, SupportBox b, float tolerance)
        {
            float dx = Math.Max(0f, Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
            float dy = Math.Max(0f, Math.Max(a.MinY - b.MaxY, b.MinY - a.MaxY));
            float dz = Math.Max(0f, Math.Max(a.MinZ - b.MaxZ, b.MinZ - a.MaxZ));
            return dx * dx + dy * dy + dz * dz <= tolerance * tolerance;
        }

        static SupportBox SupportBounds(ResidentialModule module, float x, float y,
                                        float z, float yaw)
        {
            TransformBounds(module, x, y, z, yaw,
                            out float minX, out float minY, out float minZ,
                            out float maxX, out float maxY, out float maxZ);
            return new SupportBox
            {
                MinX = minX, MinY = minY, MinZ = minZ,
                MaxX = maxX, MaxY = maxY, MaxZ = maxZ,
            };
        }

        static bool FireEscapeOnFloorLattice(List<ExpectedPlacement> members)
        {
            for (int i = 0; i < members.Count; i++)
                if (Math.Abs(members[i].Prop.Y - members[i].MemberFloor * Storey) >
                    SupportTolerance)
                    return false;
            return true;
        }

        static ExpectedPlacement Expected(DecorRow row, DecorVariant variant, Anchor basis,
                                          Anchor member, int occurrence)
        {
            var prop = Place(row, member);
            // A fire-escape role is resolved against its destination floor above. Four
            // measured templates carry source-floor deltas (-6/-3m) because their parts
            // were all associated with a different source host. Reapplying that delta
            // counts the floor twice; the role pivot belongs on the resolved floor.
            if (variant.Relation == "FireEscapeChain")
                prop = new Prop(prop.Prefab, prop.X, member.Y, prop.Z,
                                prop.Yaw, prop.Column);
            return new ExpectedPlacement
            {
                Prop = prop, Variant = variant, Row = row,
                BaseColumn = basis.Column, BaseFloor = basis.Floor,
                MemberColumn = member.Column, MemberFloor = member.Floor,
                Occurrence = occurrence,
            };
        }

        static Anchor OffsetAnchor(List<Anchor> anchors, Anchor basis, int columnOffset,
                                   int rowOffset, int floorOffset, DecorRow row)
        {
            Turn(columnOffset * Cell, rowOffset * Cell, basis.Yaw,
                 out float dx, out float dz);
            return FindAnchorAt(anchors, basis.X + dx, basis.Z + dz,
                                basis.Floor + floorOffset, row);
        }

        static Anchor FindAnchorAt(List<Anchor> anchors, float x, float z, int floor,
                                   DecorRow row)
        {
            var index = AnchorIndices.GetValue(anchors, BuildAnchorIndex);
            if (!index.ByPosition.TryGetValue((Milli(x), Milli(z), floor), out var found))
                return null;
            for (int i = 0; i < found.Count; i++)
                if (HostFits(row, found[i])) return found[i];
            return null;
        }

        static AnchorIndex BuildAnchorIndex(List<Anchor> anchors)
        {
            var index = new AnchorIndex();
            for (int i = 0; i < anchors.Count; i++)
            {
                var anchor = anchors[i];
                var key = (Milli(anchor.X), Milli(anchor.Z), anchor.Floor);
                if (!index.ByPosition.TryGetValue(key, out var found))
                {
                    found = new List<Anchor>();
                    index.ByPosition[key] = found;
                }
                found.Add(anchor);
            }
            return index;
        }

        static Anchor FindAnchor(List<Anchor> anchors, int i, int j, int floor)
        {
            for (int a = 0; a < anchors.Count; a++)
                if (anchors[a].I == i && anchors[a].J == j && anchors[a].Floor == floor)
                    return anchors[a];
            return null;
        }

        static bool HostFits(DecorRow row, Anchor anchor)
        {
            if (anchor?.Host == null) return false;
            if (row == null) return true;
            if (!string.IsNullOrEmpty(row.HostKind) && row.HostKind != "Unknown" &&
                row.HostKind != "0" && row.HostKind != anchor.Host.Kind.ToString()) return false;
            return row.HostStyle <= 0 || row.HostStyle == anchor.Host.Style ||
                   row.HostStyle == anchor.Host.RoofPairStyle;
        }

        static ResolvedVariant PickResolved(Random dice, List<ResolvedVariant> choices)
        {
            if (choices == null || choices.Count == 0) return null;
            int total = 0;
            for (int i = 0; i < choices.Count; i++)
                total += Math.Max(1, choices[i].Variant.Weight);
            int roll = dice.Next(total);
            for (int i = 0; i < choices.Count; i++)
            {
                roll -= Math.Max(1, choices[i].Variant.Weight);
                if (roll < 0) return choices[i];
            }
            return choices[choices.Count - 1];
        }

        static void AddResolved(ResolvedVariant resolved, List<Prop> props)
        {
            for (int i = 0; i < resolved.Members.Length; i++)
                props.Add(resolved.Members[i].Prop);
        }

        static bool Overlaps(long[] values, HashSet<long> reserved)
        {
            if (reserved == null) return false;
            for (int i = 0; i < values.Length; i++)
                if (reserved.Contains(values[i])) return true;
            return false;
        }

        static void AddAll(HashSet<long> target, long[] values)
        {
            if (target == null) return;
            for (int i = 0; i < values.Length; i++) target.Add(values[i]);
        }

        public static Fault[] Judge(Sheet sheet)
        {
            var faults = new List<Fault>();
            if (sheet == null)
            {
                AddFault(faults, FaultKind.Empty, -1, -1, "sheet is null");
                return faults.ToArray();
            }
            int length = sheet.Length, floors = sheet.Floors;
            if (length < MinLength || length > MaxLength ||
                floors < MinFloors || floors > MaxFloors)
            {
                AddFault(faults, FaultKind.OutOfBox, -1, -1,
                         $"invalid L{length}/N{floors}");
                return faults.ToArray();
            }
            if (sheet.PropsPercent < MinPropsPercent ||
                sheet.PropsPercent > MaxPropsPercent)
            {
                AddFault(faults, FaultKind.UnitMismatch, -1, -1,
                         $"invalid optional props {sheet.PropsPercent} percent");
                return faults.ToArray();
            }
            var occupancy = new int[length, Depth, floors + 2];
            var moduleAt = new ResidentialModule[length, Depth, floors + 2];
            var pieces = sheet.Pieces ?? Array.Empty<Piece>();
            for (int p = 0; p < pieces.Length; p++)
            {
                var piece = pieces[p];
                var module = FindNamed(piece.Module);
                if (module == null)
                {
                    AddFault(faults, FaultKind.Empty, ColumnSafe(length, piece.I, piece.J),
                             piece.Floor, string.IsNullOrEmpty(piece.Module)
                                ? "unresolved module" : piece.Module);
                    continue;
                }
                if (!IsStructure(module.Kind)) continue;
                if (!AllowedLayer(module, piece.Floor, floors))
                {
                    AddFault(faults, FaultKind.Empty, ColumnSafe(length, piece.I, piece.J),
                             piece.Floor, $"{module.Name} is not valid on this layer");
                    continue;
                }
                int span = Math.Max(1, module.Cells);
                int rise = module.Kind == ResidentialModuleKind.ApartmentStack
                    ? Math.Max(1, module.Floors) : 1;
                bool alongJ = ((piece.Yaw / 90) & 1) != 0 && span > 1;
                for (int s = 0; s < span; s++)
                    for (int f = 0; f < rise; f++)
                    {
                        int i = piece.I + (alongJ ? 0 : s);
                        int j = piece.J + (alongJ ? s : 0);
                        int floor = piece.Floor + f;
                        if (i < 0 || i >= length || j < 0 || j >= Depth ||
                            floor < 0 || floor > floors + 1)
                        {
                            AddFault(faults, FaultKind.OutOfBox, ColumnSafe(length, i, j),
                                     floor, module.Name);
                            continue;
                        }
                        occupancy[i, j, floor]++;
                        moduleAt[i, j, floor] = module;
                    }
                bool cornerKind = IsCorner(module.Kind);
                bool cornerCell = piece.I == 0 || piece.I == length - 1;
                if (cornerKind != cornerCell || cornerKind &&
                    (!module.OuterCorner || RotatedFaces(module.Faces, piece.Yaw) !=
                     CornerFaces(length, piece.I, piece.J)))
                    AddFault(faults, FaultKind.CornerOff, ColumnSafe(length, piece.I, piece.J),
                             piece.Floor, module.Name);
                if (module.Kind == ResidentialModuleKind.Shop && module.Cells > 1 &&
                    (piece.I <= 0 || piece.I + module.Cells > length - 1))
                    AddFault(faults, FaultKind.ShopAcrossCorner,
                             ColumnSafe(length, piece.I, piece.J), 0, module.Name);
            }
            for (int floor = 0; floor <= floors + 1; floor++)
                for (int j = 0; j < Depth; j++)
                    for (int i = 0; i < length; i++)
                    {
                        int n = occupancy[i, j, floor];
                        if (n == 0) AddFault(faults, FaultKind.Hole,
                                            Column(length, i, j), floor, "uncovered cell");
                        else if (n > 1) AddFault(faults, FaultKind.Double,
                                                Column(length, i, j), floor, n + " pieces");
                    }
            DoorsAndGlass(length, moduleAt, faults);
            RoofPairs(length, floors, moduleAt, faults);
            JudgeProps(length, floors, pieces, sheet.Props ?? Array.Empty<Prop>(),
                       sheet.Unit, faults);
            var expected = Describe(sheet.Signature, length, floors, pieces,
                                    sheet.Props ?? Array.Empty<Prop>());
            if (!SameUnit(expected, sheet.Unit))
                AddFault(faults, FaultKind.UnitMismatch, -1, -1,
                         "ResidentialUnit does not describe the sheet");
            return faults.ToArray();
        }

        static void DoorsAndGlass(int length, ResidentialModule[,,] modules,
                                  List<Fault> faults)
        {
            for (int j = 0; j < Depth; j++)
            {
                int doors = 0;
                for (int i = 0; i < length; i++)
                    if (modules[i, j, 0]?.Kind == ResidentialModuleKind.ApartmentDoor) doors++;
                if (doors != 1)
                    AddFault(faults, FaultKind.NoDoor, Column(length, 0, j), 0,
                             $"row has {doors}, expected 1");
                for (int i = 0; i < length; i++)
                {
                    var module = modules[i, j, 0];
                    if (module == null || module.Name != "SM_Bld_Shop_05") continue;
                    bool beside = i > 0 && IsDooredShop(modules[i - 1, j, 0]) ||
                                  i + 1 < length && IsDooredShop(modules[i + 1, j, 0]);
                    if (!beside)
                        AddFault(faults, FaultKind.LoneGlass, Column(length, i, j), 0,
                                 module.Name);
                }
            }
        }

        static void RoofPairs(int length, int floors, ResidentialModule[,,] modules,
                              List<Fault> faults)
        {
            int roofFloor = floors + 1;
            for (int j = 0; j < Depth; j++)
                for (int i = 0; i < length; i++)
                {
                    var wall = modules[i, j, floors];
                    var roof = modules[i, j, roofFloor];
                    if (wall == null || roof == null) continue;
                    if (roof.RoofPairStyle <= 0 || roof.RoofPairStyle != wall.Style)
                        AddFault(faults, FaultKind.RoofPair, Column(length, i, j), roofFloor,
                                 $"wall {wall.Style}, roof {roof.RoofPairStyle}");
                }
            for (int j = 0; j < Depth; j++)
            {
                Pair(0, 1, j);
                Pair(length - 1, length - 2, j);
            }

            void Pair(int corner, int edge, int j)
            {
                var a = modules[corner, j, roofFloor];
                var b = modules[edge, j, roofFloor];
                if (a == null || b == null || a.RoofPairStyle == b.RoofPairStyle) return;
                AddFault(faults, FaultKind.RoofPair, Column(length, corner, j), roofFloor,
                         $"corner {a.RoofPairStyle}, edge {b.RoofPairStyle}");
            }
        }

        /// <summary>
        /// Counts complete generated templates against measured raw anchor densities.
        /// RoofAccess is the explicit GAN-332 exception: its measured roof-cell placement
        /// data is constrained by the higher-priority absolute policy of 1..2 per building.
        /// CoreSim consumes these gates so drift is an exit-code failure.
        /// </summary>
        public static FamilyRate[] AuditRates(Sheet sheet)
        {
            if (sheet == null || sheet.Length < MinLength || sheet.Length > MaxLength ||
                sheet.Floors < MinFloors || sheet.Floors > MaxFloors ||
                sheet.PropsPercent < MinPropsPercent ||
                sheet.PropsPercent > MaxPropsPercent)
                return Array.Empty<FamilyRate>();
            var atlas = BuildPlacementAtlas(sheet.Length, sheet.Floors,
                                            sheet.Pieces ?? Array.Empty<Piece>());
            var available = new Dictionary<string, int>(StringComparer.Ordinal);
            var props = sheet.Props ?? Array.Empty<Prop>();
            for (int i = 0; i < props.Length; i++)
            {
                string key = PlacementKey(props[i]);
                if (!atlas.ByTransform.ContainsKey(key)) continue;
                available[key] = available.TryGetValue(key, out int n) ? n + 1 : 1;
            }
            var selected = SelectCompleteGroups(atlas, available);
            var fireColumns = new HashSet<int>();
            for (int i = 0; i < selected.Count; i++)
                if (selected[i].Relation == "FireEscapeChain")
                    fireColumns.Add(selected[i].Base.Column);

            var answer = new List<FamilyRate>();
            for (int row = 0; row < Depth; row++)
            {
                int eligible = sheet.Length - 2, actual = 0;
                foreach (int column in fireColumns)
                    if (column / sheet.Length == row) actual++;
                int minimum = eligible == 0 ? 0 : Math.Max(1,
                    (int)Math.Ceiling(Math.Max(0f, Decor.FireEscapeShareMin) * eligible - 0.0001f));
                int maximum = eligible == 0 ? 0 : Math.Max(minimum, Math.Min(eligible,
                    (int)Math.Floor(Math.Max(Decor.FireEscapeShareMin,
                                             Decor.FireEscapeShareMax) * eligible + 0.0001f)));
                answer.Add(new FamilyRate
                {
                    Family = "FireEscape", Anchor = row == 0 ? "South" : "North",
                    Relation = "FireEscapeChain", Actual = actual, Eligible = eligible,
                    RequestedMinimum = minimum, RequestedMaximum = maximum,
                    Capacity = eligible, Minimum = minimum, Maximum = maximum,
                });
            }

            var actualByBucket = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i].Relation == "FireEscapeChain") continue;
                string key = RateKey(selected[i]);
                actualByBucket[key] = actualByBucket.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            // Aggregate table rates use the raw source census, not only anchors for which
            // this generated shell happens to have an observed compatible variant.
            var buckets = new Dictionary<string, DecorVariant>(StringComparer.Ordinal);
            var variants = ReadyVariants(string.Empty, string.Empty);
            for (int i = 0; i < variants.Count; i++)
            {
                var variant = variants[i];
                if (variant.Relation == "FireEscapeChain") continue;
                string key = RateKey(variant);
                if (!buckets.ContainsKey(key)) buckets[key] = variant;
            }
            var pieceList = new List<Piece>(sheet.Pieces ?? Array.Empty<Piece>());
            var shops = ShopAnchors(sheet.Length, pieceList);
            int shopDenominator = ShopRateDenominator(shops);
            var rawWindows = WindowAnchors(sheet.Length, sheet.Floors, pieceList, null);
            var availableWindows = WindowAnchors(sheet.Length, sheet.Floors,
                                                  pieceList, fireColumns);
            int windowDenominator = rawWindows.Count;
            int roofDenominator = RoofAnchors(sheet.Length, sheet.Floors, pieceList).Count;
            foreach (var pair in buckets)
            {
                var rate = pair.Value;
                int eligible = RateDenominator(rate.Anchor, shopDenominator,
                                               windowDenominator, roofDenominator);
                int requestedMinimum, requestedMaximum;
                if (rate.Family == "RoofAccess")
                {
                    requestedMinimum = Math.Min(1, eligible);
                    requestedMaximum = Math.Min(2, eligible);
                }
                else
                {
                    DecorRange(rate.FamilyMin, rate.FamilyMean, rate.FamilyMax,
                               eligible, sheet.PropsPercent,
                               out requestedMinimum, out requestedMaximum, out _);
                }
                int capacity = eligible;
                // The explicit fire-escape grammar blocks every logical floor in its
                // column. For WindowFloor singles the same resolver can therefore state
                // the exact post-required capacity without changing the raw denominator.
                if (rate.Anchor == "WindowFloor" && rate.Relation == "Single")
                {
                    var bucket = variants.FindAll(variant => RateKey(variant) == pair.Key);
                    capacity = EligibleAnchors(bucket, availableWindows, sheet.Length,
                                               sheet.Floors, null, null).Count;
                }
                int minimum = Math.Min(requestedMinimum, capacity);
                int maximum = Math.Min(requestedMaximum, capacity);
                answer.Add(new FamilyRate
                {
                    Family = rate.Family, Anchor = rate.Anchor, Relation = rate.Relation,
                    Actual = actualByBucket.TryGetValue(pair.Key, out int n) ? n : 0,
                    Eligible = eligible, RequestedMinimum = requestedMinimum,
                    RequestedMaximum = requestedMaximum, Capacity = capacity,
                    Minimum = minimum, Maximum = maximum,
                });
            }
            return answer.ToArray();
        }

        public static string[] RequiredDecorFamilies()
        {
            var answer = new List<string>();
            string[] supported =
            {
                "FireEscape", "WashingLine", "Billboard", "Terrace", "Vent",
                "RoofAccess", "Skylight", "RoofAircon", "SatelliteDish", "Aircon",
                "Window", "WindowPlanter", "PowerBox", "ShopCover", "Sign", "LargeSign",
            };
            for (int i = 0; i < supported.Length; i++)
            {
                var variants = ReadyVariants(supported[i], string.Empty);
                for (int v = 0; v < variants.Count; v++)
                    if (variants[v].FamilyMean > 0f)
                    {
                        answer.Add(supported[i]);
                        break;
                    }
            }
            return answer.ToArray();
        }

        static string RateKey(ExpectedGroup group) =>
            group.Family + "\n" + group.Variant.Anchor + "\n" + group.Relation;

        static string RateKey(DecorVariant variant) =>
            variant.Family + "\n" + variant.Anchor + "\n" + variant.Relation;

        static void JudgeProps(int length, int floors, Piece[] pieces, Prop[] props,
                               ResidentialUnit unit, List<Fault> faults)
        {
            var atlas = BuildPlacementAtlas(length, floors, pieces);
            var actual = new Dictionary<string, int>(StringComparer.Ordinal);
            var actualProps = new Dictionary<string, Prop>(StringComparer.Ordinal);
            var escapes = new Dictionary<int, List<(float Y, int Order)>>();
            var washing = new List<Prop>();
            for (int p = 0; p < props.Length; p++)
            {
                var prop = props[p];
                var module = FindPrefab(prop.Prefab);
                string family = FamilyOf(prop.Prefab);
                if (module == null)
                {
                    AddFault(faults, FaultKind.Empty, prop.Column, -1,
                             string.IsNullOrEmpty(prop.Prefab) ? "unresolved prop" : prop.Prefab);
                    continue;
                }
                string key = PlacementKey(prop);
                actual[key] = actual.TryGetValue(key, out int had) ? had + 1 : 1;
                actualProps[key] = prop;
                int floor = LogicalFloor(prop, family);
                if (module.Kind == ResidentialModuleKind.FireEscape)
                {
                    if (!escapes.TryGetValue(prop.Column, out var stack))
                    {
                        stack = new List<(float, int)>();
                        escapes[prop.Column] = stack;
                    }
                    stack.Add((prop.Y, module.EscapeOrder));
                }
                if (family == "WashingLine" ||
                    module.Name.StartsWith("SM_Prop_Washingline_", StringComparison.OrdinalIgnoreCase))
                    washing.Add(prop);
                if (!atlas.ByTransform.ContainsKey(key))
                    AddFault(faults, FaultKind.OutOfBox, prop.Column, floor,
                             module.Name + " is not an observed placement on this host");
                TransformBounds(module, prop.X, prop.Y, prop.Z, prop.Yaw,
                                out float x0, out float y0, out float z0,
                                out float x1, out float y1, out float z1);
                if (!atlas.HasBounds || x0 < atlas.MinX - 0.001f || y0 < atlas.MinY - 0.001f ||
                    z0 < atlas.MinZ - 0.001f || x1 > atlas.MaxX + 0.001f ||
                    y1 > atlas.MaxY + 0.001f || z1 > atlas.MaxZ + 0.001f)
                {
                    AddFault(faults, FaultKind.OutOfBox, prop.Column, floor,
                             module.Name + " renderer bounds exceed measured facade allowance");
                }
            }
            JudgePhysicalSupport(length, floors, pieces, props, faults);

            // Resolve complete templates from a transform multiset. Largest templates win,
            // so a billboard base/sign or vent chain cannot be accepted as unrelated singles.
            var selected = SelectCompleteGroups(atlas, actual);

            foreach (var pair in actual)
            {
                if (pair.Value <= 0) continue;
                var prop = actualProps[pair.Key];
                if (atlas.ByTransform.TryGetValue(pair.Key, out var expected) &&
                    expected.Count > 0)
                {
                    bool grouped = expected[0].Variant.Relation != "Single";
                    AddFault(faults, grouped ? FaultKind.Empty : FaultKind.Clash,
                             prop.Column, expected[0].MemberFloor,
                             grouped ? "incomplete measured decor group" :
                                       "duplicate single-anchor decor");
                }
            }

            var occupied = new Dictionary<long, ExpectedGroup>();
            var repeatedGroups = new HashSet<string>(StringComparer.Ordinal);
            var selectedEscapes = new HashSet<int>();
            for (int i = 0; i < selected.Count; i++)
            {
                var group = selected[i];
                if (!repeatedGroups.Add(group.Key))
                    AddFault(faults, FaultKind.Clash, group.Base.Column, group.Base.Floor,
                             "duplicate " + group.Family + " template on one anchor");
                if (group.Relation == "FireEscapeChain")
                    selectedEscapes.Add(group.Base.Column);
                for (int k = 0; k < group.Occupancy.Length; k++)
                {
                    long cell = group.Occupancy[k];
                    if (occupied.TryGetValue(cell, out var other))
                        AddFault(faults, FaultKind.Clash, (int)(cell >> 32), (int)cell,
                                 other.Family + " overlaps " + group.Family);
                    else occupied[cell] = group;
                }
            }
            foreach (var pair in escapes)
            {
                pair.Value.Sort((a, b) => a.Y.CompareTo(b.Y));
                bool right = pair.Value.Count == floors;
                for (int i = 0; right && i < pair.Value.Count; i++)
                {
                    int expected = i == 0 ? 1 : (i == pair.Value.Count - 1 ? 3 : 2);
                    if (pair.Value[i].Order != expected) right = false;
                }
                if (!right)
                    AddFault(faults, FaultKind.EscapeOrder, pair.Key, 1,
                             "expected bottom, middle(s), top");
            }
            for (int j = 0; j < Depth; j++)
            {
                bool any = false;
                foreach (var pair in escapes)
                    if (pair.Key / length == j) { any = true; break; }
                if (!any)
                    AddFault(faults, FaultKind.NoEscape, Column(length, 0, j), 1,
                             "long side has no fire escape");
            }
            for (int i = 0; i < selected.Count; i++)
            {
                var group = selected[i];
                if (group.Relation != "WashingLinePair") continue;
                bool bound = group.RequiredColumns.Length >= 2;
                for (int c = 0; bound && c < group.RequiredColumns.Length; c++)
                    bound = selectedEscapes.Contains(group.RequiredColumns[c]);
                if (!bound)
                    AddFault(faults, FaultKind.LineLoose, group.Base.Column,
                             group.Base.Floor,
                             "washing line lacks two adjacent escapes");
            }
            // An unmatched washing member still carries the explicit semantic verdict,
            // even though incomplete-group validation above also explains the structural gap.
            for (int i = 0; i < washing.Count; i++)
            {
                string key = PlacementKey(washing[i]);
                if (actual.TryGetValue(key, out int left) && left > 0)
                    AddFault(faults, FaultKind.LineLoose, washing[i].Column,
                             LogicalFloor(washing[i], "WashingLine"),
                             "washing line is not a complete bound pair");
            }
        }

        static void JudgePhysicalSupport(int length, int floors, Piece[] pieces,
                                         Prop[] props, List<Fault> faults)
        {
            var source = new List<Piece>(pieces ?? Array.Empty<Piece>());
            var shops = ShopAnchors(length, source);
            var windows = WindowAnchors(length, floors, source, null);
            var roof = RoofAnchors(length, floors, source);
            var facade = new List<Anchor>(shops.Count + windows.Count);
            facade.AddRange(shops);
            facade.AddRange(windows);

            var boxes = new SupportBox[props.Length];
            var supported = new bool[props.Length];
            var validHeight = new bool[props.Length];
            var families = new string[props.Length];
            for (int i = 0; i < props.Length; i++)
            {
                var module = FindPrefab(props[i].Prefab);
                if (module == null) continue;
                boxes[i] = SupportBounds(module, props[i].X, props[i].Y,
                                         props[i].Z, props[i].Yaw);
                families[i] = FamilyOf(props[i].Prefab);
                validHeight[i] = props[i].Y >= -SupportTolerance &&
                                 FireEscapePropOnFloorLattice(props[i], module, floors);
                if (!validHeight[i]) continue;
                supported[i] = IsRoofFamily(families[i])
                    ? HasRoofFooting(boxes[i], roof)
                    : TouchesStructuralHost(boxes[i], facade) ||
                      HasGroundFooting(props[i], boxes[i], length);
            }

            // Atomic raised parts inherit support through geometry, never merely through
            // sharing a harvested variant name. This keeps the verdict independent from
            // the placement atlas it is auditing.
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < boxes.Length; i++)
                {
                    if (!validHeight[i] || boxes[i] == null || supported[i]) continue;
                    for (int j = 0; j < boxes.Length; j++)
                        if (validHeight[j] && supported[j] && boxes[j] != null &&
                            BoxesTouch(boxes[i], boxes[j], JoinedPartTolerance))
                        {
                            supported[i] = true;
                            changed = true;
                            break;
                        }
                }
            } while (changed);

            for (int i = 0; i < props.Length; i++)
            {
                if (boxes[i] == null || validHeight[i] && supported[i]) continue;
                int floor = LogicalFloor(props[i], families[i]);
                AddFault(faults, FaultKind.OutOfBox, props[i].Column, floor,
                         (FindPrefab(props[i].Prefab)?.Name ?? props[i].Prefab) +
                         (validHeight[i] ? " has no physical support" :
                                           " is off its vertical support lattice"));
            }
        }

        static bool FireEscapePropOnFloorLattice(Prop prop, ResidentialModule module,
                                                  int floors)
        {
            if (module?.Kind != ResidentialModuleKind.FireEscape) return true;
            int floor = (int)Math.Round(prop.Y / Storey, MidpointRounding.AwayFromZero);
            if (Math.Abs(prop.Y - floor * Storey) > SupportTolerance) return false;
            if (module.EscapeOrder == 1) return floor == 1;
            if (module.EscapeOrder == 3) return floor == floors;
            return module.EscapeOrder == 2 && floor > 1 && floor < floors;
        }

        static PlacementAtlas BuildPlacementAtlas(int length, int floors, Piece[] pieces)
        {
            pieces = pieces ?? Array.Empty<Piece>();
            string piecesKey = PieceFingerprint(pieces);
            string globalKey = length + "|" + floors + "|" + piecesKey;
            var cached = AtlasCache.GetOrCreateValue(pieces);
            lock (cached)
                if (cached.Atlas != null && cached.Length == length && cached.Floors == floors &&
                    cached.PiecesKey == piecesKey) return cached.Atlas;
            lock (LastAtlasLock)
                if (LastAtlas != null && LastAtlasKey == globalKey)
                {
                    lock (cached)
                    {
                        cached.Length = length; cached.Floors = floors;
                        cached.PiecesKey = piecesKey; cached.Atlas = LastAtlas;
                    }
                    return LastAtlas;
                }
            var atlas = new PlacementAtlas
            {
                HasBounds = true, MinX = 0f, MinY = 0f, MinZ = 0f,
                MaxX = length * Cell, MaxY = (floors + 2) * Storey,
                MaxZ = Depth * Cell,
            };
            var source = new List<Piece>(pieces);
            var shops = ShopAnchors(length, source);
            var windows = WindowAnchors(length, floors, source, null);
            var roof = RoofAnchors(length, floors, source);
            var variants = ReadyVariants(string.Empty, string.Empty);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int v = 0; v < variants.Count; v++)
            {
                var variant = variants[v];
                var anchors = variant.Relation == "FireEscapeChain" ||
                              variant.Relation == "WashingLinePair"
                    ? windows : Anchors(variant.Anchor, shops, windows, roof);
                for (int a = 0; a < anchors.Count; a++)
                {
                    var anchor = anchors[a];
                    if ((variant.Relation == "FireEscapeChain" ||
                         variant.Relation == "WashingLinePair") &&
                        (anchor.Floor != 1 || anchor.I <= 0 || anchor.I >= length - 1))
                        continue;
                    if (!TryResolveVariant(variant, anchor, anchors, length, floors,
                                           out var resolved)) continue;
                    AddExpectedGroup(atlas, resolved, seen);
                }
            }
            lock (cached)
            {
                cached.Length = length; cached.Floors = floors;
                cached.PiecesKey = piecesKey; cached.Atlas = atlas;
            }
            lock (LastAtlasLock)
            {
                LastAtlasKey = globalKey;
                LastAtlas = atlas;
            }
            return atlas;
        }

        static string PieceFingerprint(Piece[] pieces)
        {
            var text = new StringBuilder(pieces.Length * 24);
            for (int i = 0; i < pieces.Length; i++)
                text.Append(pieces[i].Module).Append('|').Append(pieces[i].I).Append(',')
                    .Append(pieces[i].J).Append(',').Append(pieces[i].Floor).Append(',')
                    .Append(pieces[i].Yaw).Append(';');
            return text.ToString();
        }

        static void AddExpectedGroup(PlacementAtlas atlas, ResolvedVariant resolved,
                                     HashSet<string> seen)
        {
            var keys = new string[resolved.Members.Length];
            for (int i = 0; i < keys.Length; i++) keys[i] = PlacementKey(resolved.Members[i].Prop);
            Array.Sort(keys, StringComparer.Ordinal);
            string shape = string.Join(";", keys);
            string key = resolved.Variant.Family + "|" + resolved.Variant.Relation + "|" +
                         resolved.Base.Column + "|" + resolved.Base.Floor + "|" + shape;
            if (!seen.Add(key)) return;
            var group = new ExpectedGroup
            {
                Key = key, Family = resolved.Variant.Family,
                Relation = resolved.Variant.Relation, Variant = resolved.Variant,
                Base = resolved.Base, Members = resolved.Members,
                Occupancy = resolved.Occupancy,
                RequiredColumns = resolved.RequiredColumns,
            };
            atlas.Groups.Add(group);
            for (int i = 0; i < resolved.Members.Length; i++)
            {
                var expected = resolved.Members[i];
                string transform = PlacementKey(expected.Prop);
                if (!atlas.ByTransform.TryGetValue(transform, out var list))
                {
                    list = new List<ExpectedPlacement>();
                    atlas.ByTransform[transform] = list;
                }
                list.Add(expected);
                var module = FindPrefab(expected.Prop.Prefab);
                if (module == null) continue;
                TransformBounds(module, expected.Prop.X, expected.Prop.Y, expected.Prop.Z,
                                expected.Prop.Yaw, out float x0, out float y0, out float z0,
                                out float x1, out float y1, out float z1);
                Include(ref atlas.MinX, ref atlas.MinY, ref atlas.MinZ,
                        ref atlas.MaxX, ref atlas.MaxY, ref atlas.MaxZ,
                        x0, y0, z0, x1, y1, z1);
            }
        }

        static List<ExpectedGroup> SelectCompleteGroups(PlacementAtlas atlas,
                                                         Dictionary<string, int> available)
        {
            var groups = new List<ExpectedGroup>(atlas.Groups);
            groups.Sort((a, b) =>
            {
                int size = b.Members.Length.CompareTo(a.Members.Length);
                return size != 0 ? size : string.CompareOrdinal(a.Key, b.Key);
            });
            var selected = new List<ExpectedGroup>();
            for (int i = 0; i < groups.Count; i++)
                while (CanConsume(groups[i], available))
                {
                    Consume(groups[i], available);
                    selected.Add(groups[i]);
                }
            return selected;
        }

        static bool CanConsume(ExpectedGroup group, Dictionary<string, int> available)
        {
            var needed = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < group.Members.Length; i++)
            {
                string key = PlacementKey(group.Members[i].Prop);
                needed[key] = needed.TryGetValue(key, out int count) ? count + 1 : 1;
            }
            foreach (var pair in needed)
                if (!available.TryGetValue(pair.Key, out int count) || count < pair.Value)
                    return false;
            return true;
        }

        static void Consume(ExpectedGroup group, Dictionary<string, int> available)
        {
            for (int i = 0; i < group.Members.Length; i++)
                available[PlacementKey(group.Members[i].Prop)]--;
        }

        static string PlacementKey(Prop prop)
        {
            var module = FindPrefab(prop.Prefab);
            string name = module?.Name ?? prop.Prefab ?? string.Empty;
            return name.ToLowerInvariant() + "|" + Milli(prop.X) + "|" + Milli(prop.Y) +
                   "|" + Milli(prop.Z) + "|" + Milli(NormalYaw(prop.Yaw)) + "|" + prop.Column;
        }

        static long Milli(float value) =>
            (long)Math.Round(value * 1000f, MidpointRounding.AwayFromZero);

        static ResidentialUnit Describe(string signature, int length, int floors,
                                        Piece[] pieces, Prop[] props)
        {
            var bays = ShopBays(length, pieces);
            var shops = new int[4];
            for (int i = 0; i < bays.Length; i++) shops[bays[i].Side]++;
            string[] plan = UnitPlan(length, pieces);
            bool[] face = UnitFaces(length, pieces);
            int[] doors = UnitDoors(pieces);
            var unit = new ResidentialUnit
            {
                Name = signature ?? string.Empty,
                CW = length, CD = Depth,
                Plan = plan, Face = face, Doors = doors, Shops = shops, Stoops = new int[4],
                ShopCells = ShopCells(length, pieces), ShopBays = bays, Over = new float[4],
                Trees = 0, Pieces = pieces.Length + props.Length, Seats = 0,
                MaxH = 0f, Floor = 0f, Kind = UnitKind(face),
            };
            MeasureUnit(unit, pieces, props);
            return unit;
        }

        static string[] UnitPlan(int length, Piece[] pieces)
        {
            var filled = new bool[length, Depth];
            for (int p = 0; p < pieces.Length; p++)
            {
                var piece = pieces[p];
                if (piece.Floor != 0) continue;
                var module = FindNamed(piece.Module);
                if (module == null || module.Kind != ResidentialModuleKind.Shop &&
                                      module.Kind != ResidentialModuleKind.ShopCorner &&
                                      module.Kind != ResidentialModuleKind.ApartmentDoor) continue;
                int span = Math.Max(1, module.Cells);
                bool alongJ = ((piece.Yaw / 90) & 1) != 0 && span > 1;
                for (int s = 0; s < span; s++)
                {
                    int i = piece.I + (alongJ ? 0 : s);
                    int j = piece.J + (alongJ ? s : 0);
                    if (i >= 0 && i < length && j >= 0 && j < Depth) filled[i, j] = true;
                }
            }
            var plan = new string[Depth];
            for (int j = 0; j < Depth; j++)
            {
                var row = new char[length];
                for (int i = 0; i < length; i++) row[i] = filled[i, j] ? '#' : '.';
                plan[Depth - 1 - j] = new string(row);
            }
            return plan;
        }

        static bool[] UnitFaces(int length, Piece[] pieces)
        {
            bool south = CompletePrimaryFace(length, 0, ResidentialModules.FaceMinusZ, pieces);
            bool north = CompletePrimaryFace(length, 1, ResidentialModules.FacePlusZ, pieces);
            // Corner glass wraps onto E/W and is represented by ShopBays. Face is the lot
            // planner's primary full-row frontage, so no corner secondary is promoted here.
            return new[] { south, false, north, false };
        }

        static bool CompletePrimaryFace(int length, int row, int face, Piece[] pieces)
        {
            var seen = new bool[length];
            for (int p = 0; p < pieces.Length; p++)
            {
                var piece = pieces[p];
                if (piece.Floor != 0 || piece.J != row) continue;
                var module = FindNamed(piece.Module);
                if (module == null || (RotatedFaces(module.Faces, piece.Yaw) & face) == 0)
                    continue;
                int span = Math.Max(1, module.Cells);
                for (int s = 0; s < span && piece.I + s < length; s++) seen[piece.I + s] = true;
            }
            for (int i = 0; i < seen.Length; i++) if (!seen[i]) return false;
            return true;
        }

        static int[] UnitDoors(Piece[] pieces)
        {
            var doors = new int[4];
            for (int p = 0; p < pieces.Length; p++)
            {
                var piece = pieces[p];
                var module = FindNamed(piece.Module);
                if (piece.Floor != 0 || module?.Kind != ResidentialModuleKind.ApartmentDoor)
                    continue;
                int faces = RotatedFaces(module.Faces, piece.Yaw);
                if ((faces & ResidentialModules.FaceMinusZ) != 0) doors[0]++;
                if ((faces & ResidentialModules.FacePlusX) != 0) doors[1]++;
                if ((faces & ResidentialModules.FacePlusZ) != 0) doors[2]++;
                if ((faces & ResidentialModules.FaceMinusX) != 0) doors[3]++;
            }
            return doors;
        }

        static ResidentialKind UnitKind(bool[] face)
        {
            if (face[0] && face[2]) return ResidentialKind.Through;
            int count = 0;
            for (int i = 0; i < face.Length; i++) if (face[i]) count++;
            return count > 2 ? ResidentialKind.Island :
                   count == 2 ? ResidentialKind.Corner : ResidentialKind.Row;
        }

        static ResidentialShopBay[] ShopBays(int length, Piece[] pieces)
        {
            var bays = new List<ResidentialShopBay>();
            for (int p = 0; p < pieces.Length; p++)
            {
                var piece = pieces[p];
                if (piece.Floor != 0) continue;
                var module = FindNamed(piece.Module);
                if (module == null || module.Kind != ResidentialModuleKind.Shop &&
                                      module.Kind != ResidentialModuleKind.ShopCorner) continue;
                Pivot(piece, module, out float pivotX, out float pivotZ);
                Turn(module.DoorX, module.DoorZ, piece.Yaw, out float doorDX, out float doorDZ);
                // The harvest records a real door centre, including legitimate chamfer and
                // eave overhang. Preserve it; clamping would move the interaction marker off
                // the mesh and make the synthetic unit disagree with the stood prefab.
                float doorX = pivotX + doorDX;
                float doorZ = pivotZ + doorDZ;
                int side = module.Kind == ResidentialModuleKind.ShopCorner
                    ? (piece.I == 0 ? 3 : 1) : Side(piece.Yaw);
                int count = module.Kind == ResidentialModuleKind.ShopCorner
                    ? 1 : Math.Max(1, module.Cells);
                int doorBay = -1;
                float nearest = float.MaxValue;
                for (int b = 0; b < count && module.DoorLeaves > 0; b++)
                {
                    BayCentre(piece, b, side, out float x, out float z);
                    float d = (x - doorX) * (x - doorX) + (z - doorZ) * (z - doorZ);
                    if (d < nearest) { nearest = d; doorBay = b; }
                }
                for (int b = 0; b < count; b++)
                {
                    BayCentre(piece, b, side, out float x, out float z);
                    bool owns = b == doorBay;
                    var door = new ResidentialStorefrontDoor(
                        Round2(owns ? doorX : x), Round2(owns ? doorZ : z),
                        owns ? Round2(module.DoorWidth) : 0f, owns ? module.DoorLeaves : 0,
                        RoundYaw(piece.Yaw + module.DoorYaw));
                    bays.Add(new ResidentialShopBay(
                        side, Round2(x), Round2(z), module.Name, door));
                }
            }
            bays.Sort((a, b) =>
            {
                int side = a.Side.CompareTo(b.Side);
                if (side != 0) return side;
                float aa = a.Side == 0 || a.Side == 2 ? a.X : a.Z;
                float bb = b.Side == 0 || b.Side == 2 ? b.X : b.Z;
                return aa.CompareTo(bb);
            });
            return bays.ToArray();
        }

        static void BayCentre(Piece piece, int bay, int side, out float x, out float z)
        {
            if (side == 0 || side == 2)
            {
                x = (piece.I + bay + 0.5f) * Cell;
                z = side == 0 ? 0f : Depth * Cell;
            }
            else
            {
                x = side == 1 ? (piece.I + 1) * Cell : piece.I * Cell;
                z = (piece.J + 0.5f) * Cell;
            }
        }

        static string[] ShopCells(int length, Piece[] pieces)
        {
            var sides = new[] { Filled(length, '0'), Filled(Depth, '0'),
                                Filled(length, '0'), Filled(Depth, '0') };
            for (int p = 0; p < pieces.Length; p++)
            {
                var piece = pieces[p];
                if (piece.Floor != 0) continue;
                var module = FindNamed(piece.Module);
                if (module == null || module.Kind != ResidentialModuleKind.Shop &&
                                      module.Kind != ResidentialModuleKind.ShopCorner) continue;
                int side = module.Kind == ResidentialModuleKind.ShopCorner
                    ? (piece.I == 0 ? 3 : 1) : Side(piece.Yaw);
                char[] cells = sides[side].ToCharArray();
                int count = module.Kind == ResidentialModuleKind.ShopCorner
                    ? 1 : Math.Max(1, module.Cells);
                for (int b = 0; b < count; b++)
                {
                    int at = side == 0 || side == 2 ? piece.I + b : piece.J;
                    if (at >= 0 && at < cells.Length) cells[at] = 'a';
                }
                sides[side] = new string(cells);
            }
            return sides;
        }

        static string Filled(int count, char value) => new string(value, count);

        static void MeasureUnit(ResidentialUnit unit, Piece[] pieces, Prop[] props)
        {
            float minX = 0f, minY = 0f, minZ = 0f;
            float maxX = unit.CW * Cell, maxY = 0f, maxZ = unit.CD * Cell;
            for (int p = 0; p < pieces.Length; p++)
            {
                var module = FindNamed(pieces[p].Module);
                if (module == null) continue;
                Pivot(pieces[p], module, out float x, out float z);
                TransformBounds(module, x, pieces[p].Floor * Storey, z, pieces[p].Yaw,
                                out float x0, out float y0, out float z0,
                                out float x1, out float y1, out float z1);
                Include(ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ,
                        x0, y0, z0, x1, y1, z1);
            }
            for (int p = 0; p < props.Length; p++)
            {
                var module = FindPrefab(props[p].Prefab);
                if (module == null) continue;
                TransformBounds(module, props[p].X, props[p].Y, props[p].Z, props[p].Yaw,
                                out float x0, out float y0, out float z0,
                                out float x1, out float y1, out float z1);
                Include(ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ,
                        x0, y0, z0, x1, y1, z1);
            }
            unit.Over[0] = Math.Max(0f, -minZ);
            unit.Over[1] = Math.Max(0f, maxX - unit.CW * Cell);
            unit.Over[2] = Math.Max(0f, maxZ - unit.CD * Cell);
            unit.Over[3] = Math.Max(0f, -minX);
            unit.Floor = minY; unit.MaxH = maxY;
        }

        static void Include(ref float minX, ref float minY, ref float minZ,
                            ref float maxX, ref float maxY, ref float maxZ,
                            float x0, float y0, float z0, float x1, float y1, float z1)
        {
            minX = Math.Min(minX, x0); minY = Math.Min(minY, y0); minZ = Math.Min(minZ, z0);
            maxX = Math.Max(maxX, x1); maxY = Math.Max(maxY, y1); maxZ = Math.Max(maxZ, z1);
        }

        static bool SameUnit(ResidentialUnit a, ResidentialUnit b)
        {
            if (a == null || b == null || a.Name != b.Name || a.CW != b.CW || a.CD != b.CD ||
                a.Kind != b.Kind || a.Trees != b.Trees || a.Pieces != b.Pieces ||
                a.Seats != b.Seats || !Near(a.MaxH, b.MaxH) || !Near(a.Floor, b.Floor)) return false;
            if (!Same(a.Plan, b.Plan) || !Same(a.Face, b.Face) || !Same(a.Doors, b.Doors) ||
                !Same(a.Shops, b.Shops) || !Same(a.Stoops, b.Stoops) ||
                !Same(a.ShopCells, b.ShopCells) || !Same(a.Over, b.Over)) return false;
            if (a.ShopBays == null || b.ShopBays == null || a.ShopBays.Length != b.ShopBays.Length)
                return false;
            for (int i = 0; i < a.ShopBays.Length; i++)
            {
                var x = a.ShopBays[i]; var y = b.ShopBays[i];
                if (x.Side != y.Side || x.Module != y.Module || !Near(x.X, y.X) || !Near(x.Z, y.Z) ||
                    x.Door.Leaves != y.Door.Leaves || !Near(x.Door.X, y.Door.X) ||
                    !Near(x.Door.Z, y.Door.Z) || !Near(x.Door.Width, y.Door.Width) ||
                    !Near(x.Door.Yaw, y.Door.Yaw)) return false;
            }
            return true;
        }

        static bool Same<T>(T[] a, T[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!Equals(a[i], b[i])) return false;
            return true;
        }

        static bool Same(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!Near(a[i], b[i])) return false;
            return true;
        }

        static bool Near(float a, float b) => Math.Abs(a - b) <= 0.001f;
        static bool AngleNear(float a, float b)
        {
            float delta = Math.Abs(NormalYaw(a) - NormalYaw(b));
            return Math.Min(delta, 360f - delta) <= 0.001f;
        }

        static void TransformBounds(ResidentialModule m, float x, float y, float z, float yaw,
                                    out float minX, out float minY, out float minZ,
                                    out float maxX, out float maxY, out float maxZ)
        {
            minX = minZ = float.MaxValue; maxX = maxZ = float.MinValue;
            float[] xs = { m.MinX, m.MaxX };
            float[] zs = { m.MinZ, m.MaxZ };
            for (int ix = 0; ix < 2; ix++)
                for (int iz = 0; iz < 2; iz++)
                {
                    TurnExact(xs[ix], zs[iz], yaw, out float turnedX, out float turnedZ);
                    minX = Math.Min(minX, x + turnedX); maxX = Math.Max(maxX, x + turnedX);
                    minZ = Math.Min(minZ, z + turnedZ); maxZ = Math.Max(maxZ, z + turnedZ);
                }
            minY = y + m.MinY; maxY = y + m.MaxY;
        }

        static void TurnExact(float x, float z, float yaw,
                              out float turnedX, out float turnedZ)
        {
            double quarterTurns = Math.Round(yaw / 90.0);
            float quarterYaw = (float)(quarterTurns * 90.0);
            if (yaw == quarterYaw)
            {
                Turn(x, z, quarterYaw, out turnedX, out turnedZ);
                return;
            }
            // Unity's positive Y rotation is clockwise in the X/Z plane. Keep the
            // calculation in double precision until the final coordinate.
            double radians = yaw * (Math.PI / 180.0);
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            turnedX = (float)(x * cosine + z * sine);
            turnedZ = (float)(-x * sine + z * cosine);
        }

        /// <summary>Module pivot in the sheet's south-west local frame.</summary>
        internal static void Pivot(Piece piece, ResidentialModule module,
                                   out float x, out float z)
        {
            int cells = Math.Max(1, module?.Cells ?? 1);
            switch (QuarterYaw(piece.Yaw))
            {
                case 90: x = (piece.I + 1) * Cell; z = piece.J * Cell; break;
                case 180: x = piece.I * Cell; z = piece.J * Cell; break;
                case 270: x = piece.I * Cell; z = (piece.J + cells) * Cell; break;
                default: x = (piece.I + cells) * Cell; z = (piece.J + 1) * Cell; break;
            }
        }

        static void Turn(float x, float z, float yaw, out float turnedX, out float turnedZ)
        {
            int quarter = ((int)Math.Round(yaw / 90f) % 4 + 4) % 4;
            switch (quarter)
            {
                case 1: turnedX = z; turnedZ = -x; break;
                case 2: turnedX = -x; turnedZ = -z; break;
                case 3: turnedX = -z; turnedZ = x; break;
                default: turnedX = x; turnedZ = z; break;
            }
        }

        static int RotatedFaces(int faces, int yaw)
        {
            int turns = QuarterYaw(yaw) / 90;
            for (int n = 0; n < turns; n++)
            {
                int next = 0;
                if ((faces & ResidentialModules.FacePlusZ) != 0) next |= ResidentialModules.FacePlusX;
                if ((faces & ResidentialModules.FacePlusX) != 0) next |= ResidentialModules.FaceMinusZ;
                if ((faces & ResidentialModules.FaceMinusZ) != 0) next |= ResidentialModules.FaceMinusX;
                if ((faces & ResidentialModules.FaceMinusX) != 0) next |= ResidentialModules.FacePlusZ;
                faces = next;
            }
            return faces;
        }

        static int CornerFaces(int length, int i, int j)
        {
            int faces = j == 0 ? ResidentialModules.FaceMinusZ : ResidentialModules.FacePlusZ;
            faces |= i == 0 ? ResidentialModules.FaceMinusX : ResidentialModules.FacePlusX;
            return faces;
        }

        static int PickStyle(Random dice) => PickStyle(dice, new List<int> { 1, 2, 3 });

        static int PickStyle(Random dice, List<int> allowed)
        {
            var styles = new List<int>();
            var weights = new List<int>();
            int total = 0;
            for (int style = 1; style <= 3; style++)
            {
                if (!allowed.Contains(style) || !StyleReady(style)) continue;
                int weight = Decor.StyleWeights != null && Decor.StyleWeights.Length >= style
                    ? Math.Max(0, Decor.StyleWeights[style - 1]) : 1;
                if (weight == 0) continue;
                styles.Add(style); weights.Add(weight); total += weight;
            }
            if (styles.Count == 0)
                for (int style = 1; style <= 3; style++)
                    if (allowed.Contains(style) && StyleReady(style))
                    { styles.Add(style); weights.Add(1); total++; }
            if (styles.Count == 0) return 0;
            int roll = dice.Next(total);
            for (int i = 0; i < styles.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0) return styles[i];
            }
            return styles[styles.Count - 1];
        }

        static bool StyleReady(int style) =>
            Has(ResidentialModuleKind.Apartment, style) &&
            Has(ResidentialModuleKind.ApartmentCorner, style, true) &&
            HasRoof(ResidentialModuleKind.Roof, style) &&
            HasRoof(ResidentialModuleKind.RoofCorner, style, true);

        static ResidentialModule PickDooredShop(Random dice)
        {
            var found = new List<ResidentialModule>();
            for (int i = 0; i < ResidentialModules.All.Length; i++)
            {
                var module = ResidentialModules.All[i];
                if (module.Kind == ResidentialModuleKind.Shop && module.Cells == 1 &&
                    module.DoorLeaves > 0) found.Add(module);
            }
            return found.Count == 0 ? null : found[dice.Next(found.Count)];
        }

        static ResidentialModule Pick(Random dice, ResidentialModuleKind kind,
                                      int style = 0, int cells = 0, bool outer = false)
        {
            var found = new List<ResidentialModule>();
            for (int i = 0; i < ResidentialModules.All.Length; i++)
            {
                var module = ResidentialModules.All[i];
                if (module.Kind != kind || style > 0 && module.Style != style ||
                    cells > 0 && Math.Max(1, module.Cells) != cells ||
                    outer && !module.OuterCorner) continue;
                found.Add(module);
            }
            return found.Count == 0 ? null : found[dice.Next(found.Count)];
        }

        static ResidentialModule PickRoof(Random dice, bool corner, int pairStyle)
        {
            var kind = corner ? ResidentialModuleKind.RoofCorner : ResidentialModuleKind.Roof;
            var found = new List<ResidentialModule>();
            for (int i = 0; i < ResidentialModules.All.Length; i++)
            {
                var module = ResidentialModules.All[i];
                if (module.Kind == kind && module.RoofPairStyle == pairStyle &&
                    (!corner || module.OuterCorner)) found.Add(module);
            }
            return found.Count == 0 ? null : found[dice.Next(found.Count)];
        }

        static bool Has(ResidentialModuleKind kind, int style, bool outer = false)
        {
            for (int i = 0; i < ResidentialModules.All.Length; i++)
                if (ResidentialModules.All[i].Kind == kind &&
                    ResidentialModules.All[i].Style == style &&
                    (!outer || ResidentialModules.All[i].OuterCorner)) return true;
            return false;
        }

        static bool HasRoof(ResidentialModuleKind kind, int pairStyle, bool outer = false)
        {
            for (int i = 0; i < ResidentialModules.All.Length; i++)
                if (ResidentialModules.All[i].Kind == kind &&
                    ResidentialModules.All[i].RoofPairStyle == pairStyle &&
                    (!outer || ResidentialModules.All[i].OuterCorner)) return true;
            return false;
        }

        static bool IsDooredShop(ResidentialModule module) => module != null &&
            (module.Kind == ResidentialModuleKind.Shop ||
             module.Kind == ResidentialModuleKind.ShopCorner) && module.DoorLeaves > 0;

        static bool IsStructure(ResidentialModuleKind kind) =>
            kind == ResidentialModuleKind.Shop || kind == ResidentialModuleKind.ShopCorner ||
            kind == ResidentialModuleKind.ApartmentDoor || kind == ResidentialModuleKind.Apartment ||
            kind == ResidentialModuleKind.ApartmentStack || kind == ResidentialModuleKind.ApartmentCorner ||
            kind == ResidentialModuleKind.Roof || kind == ResidentialModuleKind.RoofCorner;

        static bool AllowedLayer(ResidentialModule module, int floor, int floors)
        {
            if (floor == 0)
                return module.Kind == ResidentialModuleKind.Shop ||
                       module.Kind == ResidentialModuleKind.ShopCorner ||
                       module.Kind == ResidentialModuleKind.ApartmentDoor;
            if (floor == floors + 1)
                return module.Kind == ResidentialModuleKind.Roof ||
                       module.Kind == ResidentialModuleKind.RoofCorner;
            if (floor < 1 || floor > floors) return false;
            if (module.Kind == ResidentialModuleKind.ApartmentStack)
                return floor == 1 && Math.Max(1, module.Floors) <= floors;
            return module.Kind == ResidentialModuleKind.Apartment ||
                   module.Kind == ResidentialModuleKind.ApartmentCorner;
        }

        static bool IsCorner(ResidentialModuleKind kind) =>
            kind == ResidentialModuleKind.ShopCorner ||
            kind == ResidentialModuleKind.ApartmentCorner ||
            kind == ResidentialModuleKind.RoofCorner;

        static ResidentialModule FindNamed(string name) => ResidentialModules.Find(name);

        static ResidentialModule FindPrefab(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            var direct = ResidentialModules.Find(prefab);
            if (direct != null) return direct;
            int slash = Math.Max(prefab.LastIndexOf('/'), prefab.LastIndexOf('\\'));
            string name = slash >= 0 ? prefab.Substring(slash + 1) : prefab;
            if (name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 7);
            return ResidentialModules.Find(name);
        }

        static int LogicalFloor(Prop prop, string family)
        {
            for (int i = 0; i < Decor.Rows.Length; i++)
            {
                var row = Decor.Rows[i];
                if (row.Family != family || !SamePrefab(row.Prefab, prop.Prefab)) continue;
                return (int)Math.Round((prop.Y - row.Y) / Storey,
                                       MidpointRounding.AwayFromZero);
            }
            return (int)Math.Round(prop.Y / Storey, MidpointRounding.AwayFromZero);
        }

        static string FamilyOf(string prefab)
        {
            for (int i = 0; i < Decor.Rows.Length; i++)
                if (SamePrefab(Decor.Rows[i].Prefab, prefab)) return Decor.Rows[i].Family;
            return string.Empty;
        }

        static bool SamePrefab(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            var aa = FindPrefab(a); var bb = FindPrefab(b);
            return aa != null && bb != null &&
                   string.Equals(aa.Name, bb.Name, StringComparison.OrdinalIgnoreCase);
        }

        static int Side(int yaw)
        {
            switch (QuarterYaw(yaw))
            {
                case 90: return 1;
                case 180: return 0;
                case 270: return 3;
                default: return 2;
            }
        }

        static int QuarterYaw(int yaw)
        {
            int answer = yaw % 360;
            if (answer < 0) answer += 360;
            return ((answer + 45) / 90 * 90) % 360;
        }

        static float NormalYaw(float yaw)
        {
            float answer = yaw % 360f;
            if (answer < 0f) answer += 360f;
            return answer >= 359.995f ? 0f : answer;
        }

        static int Column(int length, int i, int j) => j * length + i;
        static int ColumnSafe(int length, int i, int j) =>
            i < 0 || j < 0 ? -1 : Column(length, i, j);
        static bool Contains(int value, int start, int count) =>
            value >= start && value < start + count;
        static float Clamp(float value, float min, float max) =>
            value < min ? min : (value > max ? max : value);
        static float Round2(float value) =>
            (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
        static float RoundYaw(float value) => Round2(NormalYaw(value));

        static void Shuffle<T>(Random dice, List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int other = dice.Next(i + 1);
                T hold = list[i]; list[i] = list[other]; list[other] = hold;
            }
        }

        static void AddFault(List<Fault> faults, FaultKind kind, int column,
                             int floor, string detail) =>
            faults.Add(new Fault { Kind = kind, Column = column, Floor = floor, Detail = detail });

        static DecorData ReadDecor()
        {
            var answer = new DecorData();
            Type type = typeof(ResidentialFacade).Assembly.GetType("RoadDemo.ResidentialDecor");
            if (type == null) return answer;
            answer.StyleWeights = ReadStatic<int[]>(type, "StyleWeights") ?? answer.StyleWeights;
            answer.FireEscapeShareMin = ReadStatic<float>(type, "FireEscapeShareMin");
            answer.FireEscapeShareMax = ReadStatic<float>(type, "FireEscapeShareMax");
            var all = ReadStatic<Array>(type, "All");
            if (all == null) return answer;
            var rows = new List<DecorRow>(all.Length);
            foreach (object source in all)
            {
                if (source == null) continue;
                Type rowType = source.GetType();
                rows.Add(new DecorRow
                {
                    Family = Read<string>(rowType, source, "Family") ?? string.Empty,
                    Prefab = Read<string>(rowType, source, "Prefab") ?? string.Empty,
                    Anchor = Convert.ToString(Read<object>(rowType, source, "Anchor")),
                    Variant = Read<string>(rowType, source, "Variant") ?? string.Empty,
                    Relation = Convert.ToString(Read<object>(rowType, source, "Relation")),
                    HostKind = Convert.ToString(Read<object>(rowType, source, "HostKind")),
                    X = Read<float>(rowType, source, "X"),
                    Y = Read<float>(rowType, source, "Y"),
                    Z = Read<float>(rowType, source, "Z"),
                    Yaw = Read<float>(rowType, source, "Yaw"),
                    Min = Read<float>(rowType, source, "Min"),
                    Mean = Read<float>(rowType, source, "Mean"),
                    Max = Read<float>(rowType, source, "Max"),
                    FamilyMin = Read<float>(rowType, source, "FamilyMin"),
                    FamilyMean = Read<float>(rowType, source, "FamilyMean"),
                    FamilyMax = Read<float>(rowType, source, "FamilyMax"),
                    Count = Read<int>(rowType, source, "Count"),
                    Buildings = Read<int>(rowType, source, "Buildings"),
                    Part = Read<int>(rowType, source, "Part"),
                    Parts = Read<int>(rowType, source, "Parts"),
                    Span = Read<int>(rowType, source, "Span"),
                    ColumnOffset = Read<int>(rowType, source, "ColumnOffset"),
                    RowOffset = Read<int>(rowType, source, "RowOffset"),
                    FloorOffset = Read<int>(rowType, source, "FloorOffset"),
                    VariantWeight = Read<int>(rowType, source, "VariantWeight"),
                    Role = Read<int>(rowType, source, "Role"),
                    HostStyle = Read<int>(rowType, source, "HostStyle"),
                    Repeat = Read<bool>(rowType, source, "Repeat"),
                    UnboundShare = Read<float>(rowType, source, "UnboundShare"),
                    Ready = Read<bool>(rowType, source, "Ready"),
                });
            }
            answer.Rows = rows.ToArray();
            return answer;
        }

        static T ReadStatic<T>(Type type, string field)
        {
            object value = type.GetField(field, BindingFlags.Public | BindingFlags.Static)
                               ?.GetValue(null);
            return value is T typed ? typed : default;
        }

        static T Read<T>(Type type, object source, string field)
        {
            object value = type.GetField(field, BindingFlags.Public | BindingFlags.Instance)
                               ?.GetValue(source);
            return value is T typed ? typed : default;
        }
    }
}
