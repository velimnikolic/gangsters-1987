using System;
using System.Globalization;

namespace LivingCity.Territory
{
    /// <summary>
    /// Canonical gang identity for territory data. It adapts the existing integer
    /// Gang.Id; it does not allocate a second gang number.
    /// </summary>
    public readonly struct TerritoryGangId : IEquatable<TerritoryGangId>
    {
        readonly bool hasValue;

        public TerritoryGangId(int value)
        {
            Value = value;
            hasValue = value >= 0;
        }

        public int Value { get; }
        public bool IsValid => hasValue;

        public bool Equals(TerritoryGangId other) =>
            hasValue == other.hasValue && (!hasValue || Value == other.Value);

        public override bool Equals(object obj) => obj is TerritoryGangId other && Equals(other);
        public override int GetHashCode() => hasValue ? Value : int.MinValue;
        public override string ToString() => hasValue ? Value.ToString() : "unknown";

        public static bool operator ==(TerritoryGangId left, TerritoryGangId right) =>
            left.Equals(right);

        public static bool operator !=(TerritoryGangId left, TerritoryGangId right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Canonical character identity. Player characters reuse Personnel.Character.Id;
    /// physical rival actors may reuse their deterministic negative street IDs.
    /// </summary>
    public readonly struct TerritoryCharacterId : IEquatable<TerritoryCharacterId>
    {
        readonly bool hasValue;

        public TerritoryCharacterId(int value)
        {
            Value = value;
            hasValue = true;
        }

        public int Value { get; }
        public bool IsValid => hasValue;

        public bool Equals(TerritoryCharacterId other) =>
            hasValue == other.hasValue && (!hasValue || Value == other.Value);

        public override bool Equals(object obj) =>
            obj is TerritoryCharacterId other && Equals(other);

        public override int GetHashCode() => hasValue ? Value : int.MinValue;
        public override string ToString() => hasValue ? Value.ToString() : "unknown";

        public static bool operator ==(TerritoryCharacterId left, TerritoryCharacterId right) =>
            left.Equals(right);

        public static bool operator !=(TerritoryCharacterId left, TerritoryCharacterId right) =>
            !left.Equals(right);
    }

    public enum TerritoryCommandNodeKind
    {
        None,
        Outfit,
        Boss,
        Lieutenant,
        Crew,
    }

    /// <summary>
    /// Stable command-chain node. Phase 1 maps physical groups to the existing Crew.Id;
    /// later organization tickets can use boss/lieutenant character IDs without changing
    /// territory command payloads.
    /// </summary>
    public readonly struct TerritoryCommandNodeId : IEquatable<TerritoryCommandNodeId>
    {
        public TerritoryCommandNodeId(TerritoryCommandNodeKind kind, int value)
        {
            Kind = kind;
            Value = value;
        }

        public TerritoryCommandNodeKind Kind { get; }
        public int Value { get; }
        public bool IsValid => Kind != TerritoryCommandNodeKind.None;

        public static TerritoryCommandNodeId Crew(int crewId) =>
            new TerritoryCommandNodeId(TerritoryCommandNodeKind.Crew, crewId);

        public static TerritoryCommandNodeId Boss(int characterId) =>
            new TerritoryCommandNodeId(TerritoryCommandNodeKind.Boss, characterId);

        public static TerritoryCommandNodeId Lieutenant(int characterId) =>
            new TerritoryCommandNodeId(TerritoryCommandNodeKind.Lieutenant, characterId);

        public bool Equals(TerritoryCommandNodeId other) =>
            Kind == other.Kind && Value == other.Value;

        public override bool Equals(object obj) =>
            obj is TerritoryCommandNodeId other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ Value;
        public override string ToString() => IsValid ? $"{Kind}:{Value}" : "none";

        public static bool operator ==(TerritoryCommandNodeId left, TerritoryCommandNodeId right) =>
            left.Equals(right);

        public static bool operator !=(TerritoryCommandNodeId left, TerritoryCommandNodeId right) =>
            !left.Equals(right);
    }

    public readonly struct TerritoryNeighborhoodId : IEquatable<TerritoryNeighborhoodId>
    {
        public TerritoryNeighborhoodId(string value) => Value = value ?? "";

        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);

        public bool Equals(TerritoryNeighborhoodId other) =>
            string.Equals(Value ?? "", other.Value ?? "", StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is TerritoryNeighborhoodId other && Equals(other);

        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? "";

        public static bool operator ==(TerritoryNeighborhoodId left, TerritoryNeighborhoodId right) =>
            left.Equals(right);

        public static bool operator !=(TerritoryNeighborhoodId left, TerritoryNeighborhoodId right) =>
            !left.Equals(right);
    }

    public readonly struct TerritoryBlockId : IEquatable<TerritoryBlockId>
    {
        public TerritoryBlockId(string value) => Value = value ?? "";

        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);

        public bool Equals(TerritoryBlockId other) =>
            string.Equals(Value ?? "", other.Value ?? "", StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is TerritoryBlockId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? "";

        public static bool operator ==(TerritoryBlockId left, TerritoryBlockId right) =>
            left.Equals(right);

        public static bool operator !=(TerritoryBlockId left, TerritoryBlockId right) =>
            !left.Equals(right);
    }

    public readonly struct TerritoryBusinessId : IEquatable<TerritoryBusinessId>
    {
        public TerritoryBusinessId(string value) => Value = value ?? "";

        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);

        public bool Equals(TerritoryBusinessId other) =>
            string.Equals(Value ?? "", other.Value ?? "", StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is TerritoryBusinessId other && Equals(other);

        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? "";

        public static bool operator ==(TerritoryBusinessId left, TerritoryBusinessId right) =>
            left.Equals(right);

        public static bool operator !=(TerritoryBusinessId left, TerritoryBusinessId right) =>
            !left.Equals(right);
    }

    /// <summary>Engine-free world XZ point carried by commands.</summary>
    public readonly struct TerritoryPoint
    {
        public TerritoryPoint(float x, float z)
        {
            X = x;
            Z = z;
        }

        public float X { get; }
        public float Z { get; }
        public bool IsFinite => !float.IsNaN(X) && !float.IsInfinity(X) &&
                                !float.IsNaN(Z) && !float.IsInfinity(Z);
    }

    /// <summary>Engine-free world XZ bounds published by the immutable city plan.</summary>
    public readonly struct TerritoryBounds
    {
        public TerritoryBounds(float xMin, float zMin, float width, float depth)
        {
            XMin = xMin;
            ZMin = zMin;
            Width = width;
            Depth = depth;
        }

        public float XMin { get; }
        public float ZMin { get; }
        public float Width { get; }
        public float Depth { get; }
        public TerritoryPoint Center => new TerritoryPoint(XMin + Width * 0.5f, ZMin + Depth * 0.5f);
    }

    /// <summary>
    /// The only adapters that mint territory IDs. Existing Core stable IDs pass through
    /// unchanged; generated-city identities are derived from plan data, never GameObject names.
    /// </summary>
    public static class TerritoryIdentity
    {
        public static TerritoryBlockId ExistingBlock(string stableId) =>
            new TerritoryBlockId(stableId);

        public static TerritoryNeighborhoodId CoreNeighborhood(int seed, int quarterId) =>
            new TerritoryNeighborhoodId(string.Format(
                CultureInfo.InvariantCulture, "core:{0}:quarter:{1}", seed, quarterId));

        public static TerritoryBlockId GeneratedBlock(int seed, int blockId) =>
            new TerritoryBlockId(string.Format(
                CultureInfo.InvariantCulture, "city:{0}:block:{1}", seed, blockId));

        public static TerritoryBusinessId GeneratedBusiness(
            int seed, int blockId, int quantizedX, int quantizedZ, int category,
            int collisionOrdinal = 0)
        {
            var suffix = collisionOrdinal > 0
                ? ":" + collisionOrdinal.ToString(CultureInfo.InvariantCulture)
                : "";
            return new TerritoryBusinessId(string.Format(
                CultureInfo.InvariantCulture,
                "city:{0}:business:{1}:{2}:{3}:{4}{5}",
                seed, blockId, quantizedX, quantizedZ, category, suffix));
        }
    }
}
