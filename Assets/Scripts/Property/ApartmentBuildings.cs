using System;
using System.Collections.Generic;
using LivingCity.Territory;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Property
{
    /// <summary>
    /// One residential building as the city PLAN describes it, so that a flat bought in it
    /// keeps its address whether its block is on camera, pooled, or has never been composed
    /// at all. This is the same contract <see cref="LivingCity.Business.BusinessSite"/>
    /// states for a shop, and for the same reason: a deed keyed on the hierarchy drifts the
    /// moment the recycler pools the street under it.
    /// </summary>
    public readonly struct ApartmentBuildingId : IEquatable<ApartmentBuildingId>
    {
        public ApartmentBuildingId(string value) => Value = value ?? "";

        public string Value { get; }

        public bool IsValid => !string.IsNullOrEmpty(Value);

        public bool Equals(ApartmentBuildingId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is ApartmentBuildingId other && Equals(other);

        public override int GetHashCode() => Value == null ? 0 : Value.GetHashCode();

        public override string ToString() => Value;

        public static bool operator ==(ApartmentBuildingId a, ApartmentBuildingId b) =>
            a.Equals(b);

        public static bool operator !=(ApartmentBuildingId a, ApartmentBuildingId b) =>
            !a.Equals(b);
    }

    /// <summary>What the book knows about one building before anybody buys anything in it.</summary>
    public sealed class ApartmentBuilding
    {
        public ApartmentBuildingId Id { get; internal set; }

        /// <summary>The recipe the building was read out of - the same string a business
        /// site calls its SourcePlanId, so a shop and a flat in one building agree.</summary>
        public string PlanId { get; internal set; }

        /// <summary>Which spot of that plan it is. Plan order, never hierarchy order.</summary>
        public int SpotIndex { get; internal set; }

        public string UnitName { get; internal set; }

        public TerritoryBlockId CanonicalBlockId { get; internal set; }

        /// <summary>The footprint in world XZ. The MAST stands on its centre.</summary>
        public Rect WorldRect { get; internal set; }

        /// <summary>How tall it stands, metres, measured by the harvest (MaxH).</summary>
        public float Rise { get; internal set; }

        public int Storeys { get; internal set; }

        /// <summary>Doors on one landing - the ground floor's shop bays, or the harvested
        /// residential doors when the building has no shops at all.</summary>
        public int DoorsPerLanding { get; internal set; }

        /// <summary>Floors of flats: every storey above the ground, which is shops and the
        /// entrance.</summary>
        public int Floors => Mathf.Max(0, Storeys - 1);

        public int Flats => Floors * DoorsPerLanding;

        /// <summary>"318 CANAL HEIGHTS" - minted once here and printed everywhere, so the
        /// group header, the blueprint's title and the premises form cannot disagree.</summary>
        public string Address { get; internal set; }

        public Vector3 Centre => new Vector3(WorldRect.center.x, 0f, WorldRect.center.y);
    }

    /// <summary>
    /// The city's residential buildings, dealt from the same plan the shops are dealt from
    /// and keyed the same way. Static because the ledger, the film and the day tick all ask
    /// the same question and must get the same answer; built once, from
    /// <see cref="LivingCity.Business.BusinessRuntime.Init"/>, and never from a composed
    /// scene.
    /// </summary>
    public static class ApartmentBuildings
    {
        /// <summary>The pitch the authored POLYGON modules stack at: base at y=0, the upper
        /// stack at y=3. Used only until the harvest measures a storey count of its own -
        /// <see cref="ResidentialUnit.MaxH"/> is a HEIGHT, and a storey count divided out of
        /// it changes with the divisor, so this constant is named here once rather than
        /// guessed at three call sites.</summary>
        public const float AuthoredStorey = 3.0f;

        static readonly List<ApartmentBuilding> all = new List<ApartmentBuilding>();

        static readonly Dictionary<ApartmentBuildingId, ApartmentBuilding> byId =
            new Dictionary<ApartmentBuildingId, ApartmentBuilding>();

        static readonly Dictionary<TerritoryBlockId, List<ApartmentBuilding>> byBlock =
            new Dictionary<TerritoryBlockId, List<ApartmentBuilding>>();

        /// <summary>Moves when the city is dealt again, so a sheet can tell a rebuilt city
        /// from a repaint.</summary>
        public static int Version { get; private set; }

        public static IReadOnlyList<ApartmentBuilding> All => all;

        /// <summary>Deals the buildings off the residential plan. Idempotent by seed the way
        /// the business runtime is: a second Init with the same model is ignored.</summary>
        public static void Init(ResidentialBlockModel model, DistrictFrame frame)
        {
            Clear();

            // A NEW CITY IS A NEW BOOK. The deed book is keyed on these building ids, so
            // flats bought against the buildings of a city that no longer exists would
            // otherwise sit in the book unreachable and uncounted. A campaign LOAD deals
            // its city first and restores its flats after, which is the same order.
            Apartments.Clear();
            if (model?.Blocks == null)
                return;

            foreach (var recipe in model.Blocks)
            {
                var plan = recipe?.Plan;
                if (plan?.Spots == null)
                    continue;

                var block = TerritoryIdentity.ExistingBlock(recipe.Id);

                for (var index = 0; index < plan.Spots.Count; index++)
                {
                    var spot = plan.Spots[index];
                    var unit = spot?.Unit;
                    if (!IsApartmentBuilding(unit))
                        continue;

                    var local = SpotRect(recipe, spot);
                    var building = new ApartmentBuilding
                    {
                        Id = BuildingId(recipe.Id, index, unit.Name),
                        PlanId = recipe.Id ?? "",
                        SpotIndex = index,
                        UnitName = unit.Name ?? "",
                        CanonicalBlockId = block,
                        WorldRect = frame.ToWorldRect(local),
                        Rise = unit.MaxH,
                        Storeys = StoreysOf(unit),
                        DoorsPerLanding = DoorsPerLanding(unit),
                    };
                    building.Address = MintAddress(building);
                    Add(building);
                }
            }

            all.Sort((a, b) => string.CompareOrdinal(a.Id.Value, b.Id.Value));
            Version++;
        }

        public static void Clear()
        {
            all.Clear();
            byId.Clear();
            byBlock.Clear();
            Version++;
        }

        public static bool TryGet(ApartmentBuildingId id, out ApartmentBuilding building) =>
            byId.TryGetValue(id, out building);

        /// <summary>Every apartment building standing on a canonical block, in plan order.
        /// Empty for the downtown prefabs, which carry no plan-level building grouping at
        /// all - see Docs/business-inventory.md.</summary>
        public static IReadOnlyList<ApartmentBuilding> OnBlock(TerritoryBlockId block) =>
            byBlock.TryGetValue(block, out var list)
                ? (IReadOnlyList<ApartmentBuilding>)list
                : Array.Empty<ApartmentBuilding>();

        /// <summary>
        /// Which building a SHOP stands in. Read straight off the business id, which carries
        /// the site id it was minted from ("biz|residential|&lt;plan&gt;|spot:3:residential-06:
        /// shop:1:250:0"), so grouping the block file's trade column by building needs no
        /// geometry and no second index.
        /// </summary>
        public static bool TryBuildingOf(TerritoryBusinessId business, out ApartmentBuildingId id)
        {
            id = default;
            var value = business.Value;
            if (string.IsNullOrEmpty(value))
                return false;

            // biz | provider | plan | group
            var parts = value.Split('|');
            if (parts.Length < 4)
                return false;

            var plan = parts[2];
            var group = parts[3];
            if (!group.StartsWith("spot:", StringComparison.Ordinal))
                return false;

            var rest = group.Substring(5);
            var colon = rest.IndexOf(':');
            if (colon <= 0)
                return false;
            if (!int.TryParse(rest.Substring(0, colon), out var spotIndex))
                return false;

            var tail = rest.Substring(colon + 1);
            var nameEnd = tail.IndexOf(':');
            var unitName = nameEnd < 0 ? tail : tail.Substring(0, nameEnd);

            id = BuildingId(plan, spotIndex, unitName);
            return byId.ContainsKey(id);
        }

        // ------------------------------------------------------------------ the measures

        /// <summary>A building the outfit can hold flats in: a harvested house, not a park,
        /// not an amenity lot, not a one-unit kit storefront.</summary>
        public static bool IsApartmentBuilding(ResidentialUnit unit) =>
            unit != null &&
            !ResidentialUnits.IsLot(unit) &&
            unit.Kind != ResidentialKind.Storefront;

        /// <summary>
        /// How many storeys it stands. The harvest records a HEIGHT and not a count, so this
        /// is the one place the division happens; when `ResidentialUnit` grows a measured
        /// `Storeys` this reads it instead and every caller is already through here.
        /// </summary>
        public static int StoreysOf(ResidentialUnit unit)
        {
            if (unit == null)
                return 0;
            return Mathf.Max(1, Mathf.RoundToInt(unit.MaxH / AuthoredStorey));
        }

        /// <summary>
        /// Doors on a landing = the GROUND-FLOOR SHOP BAYS (the user, 2026-09-03): the
        /// building already says how wide it is at the street, and that count is measured.
        /// `residential-05` is the only unit with no bays at all, and it is also the only one
        /// with real harvested residential doors, so the fallback covers the whole table and
        /// no building is left with nothing.
        /// </summary>
        public static int DoorsPerLanding(ResidentialUnit unit)
        {
            if (unit == null)
                return 0;

            var bays = unit.ShopBays != null ? unit.ShopBays.Length : 0;
            if (bays > 0)
                return bays;

            var doors = 0;
            if (unit.Doors != null)
                for (var i = 0; i < unit.Doors.Length; i++)
                    doors += Mathf.Max(0, unit.Doors[i]);
            return Mathf.Max(1, doors);
        }

        /// <summary>The letter over a door on a landing: A, B, … Z, then AA. A landing runs
        /// from one door to twenty-two on the measured fabric, so this cannot stop at J the
        /// way the design prototype's fixed grid did.</summary>
        public static string DoorLetter(int slot)
        {
            if (slot < 0)
                return "?";
            var letters = "";
            var n = slot;
            do
            {
                letters = (char)('A' + n % 26) + letters;
                n = n / 26 - 1;
            } while (n >= 0);
            return letters;
        }

        // ------------------------------------------------------------------ the plumbing

        static ApartmentBuildingId BuildingId(string planId, int spotIndex, string unitName) =>
            new ApartmentBuildingId($"flat|{planId}|spot:{spotIndex}:{unitName}");

        static void Add(ApartmentBuilding building)
        {
            if (byId.ContainsKey(building.Id))
                return;
            all.Add(building);
            byId.Add(building.Id, building);
            if (!byBlock.TryGetValue(building.CanonicalBlockId, out var list))
            {
                list = new List<ApartmentBuilding>();
                byBlock.Add(building.CanonicalBlockId, list);
            }
            list.Add(building);
        }

        static Rect SpotRect(ResidentialBlockRecipe recipe, ResidentialLot.Spot spot)
        {
            float cell = ResidentialLot.Cell;
            return new Rect(
                recipe.LocalBounds.xMin + spot.I * cell,
                recipe.LocalBounds.yMin + spot.J * cell,
                spot.CW * cell,
                spot.CD * cell);
        }

        /// <summary>
        /// The house number and the street it stands in. The plan has no street names at
        /// building level - `StreetNames` names the grid's lines, and no record anywhere
        /// carries a house number - so the number is minted off the building's own id and
        /// the street reads as its ground. Deterministic: the same city always prints the
        /// same address.
        /// </summary>
        static string MintAddress(ApartmentBuilding building)
        {
            var hash = Hash(building.Id.Value);
            var number = 100 + (hash & 0x1FF) * 2 + (building.SpotIndex & 1);
            var street = StreetOf(building);
            return number.ToString() + " " + street;
        }

        static string StreetOf(ApartmentBuilding building)
        {
            var geography = TerritoryRuntime.Instance != null
                ? TerritoryRuntime.Instance.Geography
                : null;
            if (geography != null &&
                geography.TryGetBlock(building.CanonicalBlockId, out var block) &&
                !string.IsNullOrEmpty(block.DisplayName))
                return block.DisplayName.ToUpperInvariant();
            return "THE ROW";
        }

        static int Hash(string value)
        {
            unchecked
            {
                var hash = (int)2166136261;
                for (var i = 0; i < value.Length; i++)
                    hash = (hash ^ value[i]) * 16777619;
                return hash & int.MaxValue;
            }
        }
    }
}
