using System;

namespace RoadDemo
{
    public enum ResidentialDecorAnchor { Unbound, Shop, WindowFloor, RoofCell, FireEscapeColumn, WashingLine, VentChain, Billboard, Terrace }
    public enum ResidentialDecorRelation { Single, FireEscapeChain, WashingLinePair, VentChain, BillboardPair, TerraceGroup }

    [Serializable]
    public sealed class ResidentialDecorRule
    {
        public string Family, Prefab, Variant;
        public ResidentialDecorAnchor Anchor;
        public ResidentialDecorRelation Relation;
        public ResidentialModuleKind HostKind;
        public float X, Y, Z, Yaw, Min, Mean, Max, FamilyMin, FamilyMean, FamilyMax;
        public int HostStyle, Count, Buildings, Part, Parts, Span, ColumnOffset, RowOffset, FloorOffset, Role, VariantWeight;
        public float UnboundShare;
        public bool Repeat, Ready;
    }

    [Serializable]
    public sealed class ResidentialDecorCatalog
    {
        public int Version;
        public int[] StyleWeights;
        public float FireEscapeShareMin, FireEscapeShareMax;
        public ResidentialDecorRule[] All;
        public string[] NotReady, Unresolved;

        public void Validate()
        {
            if (Version != 1 || StyleWeights == null || StyleWeights.Length != 3 ||
                All == null || All.Length == 0 || NotReady == null || Unresolved == null ||
                !(FireEscapeShareMin >= 0 && FireEscapeShareMax <= 1 && FireEscapeShareMin <= FireEscapeShareMax))
                throw new InvalidOperationException("Invalid residential decor catalog; regenerate gangsters_decor_table.");
            foreach (var rule in All)
                if (rule == null || string.IsNullOrEmpty(rule.Prefab) || string.IsNullOrEmpty(rule.Variant) ||
                    string.IsNullOrEmpty(rule.Family) || rule.Parts < 1 || rule.Part < 0 || rule.Part >= rule.Parts ||
                    !Enum.IsDefined(typeof(ResidentialDecorAnchor), rule.Anchor) ||
                    !Enum.IsDefined(typeof(ResidentialDecorRelation), rule.Relation) ||
                    !Enum.IsDefined(typeof(ResidentialModuleKind), rule.HostKind))
                    throw new InvalidOperationException("Invalid residential decor rule.");
        }
    }

    /// <summary>Shared measured data; generated JSON is not executable source.</summary>
    public static class ResidentialDecor
    {
        static ResidentialDecorCatalog cached;
        public static int Revision { get; private set; }
        static ResidentialDecorCatalog Data => cached ?? (cached = Load());
        public static int[] StyleWeights => Data.StyleWeights;
        public static float FireEscapeShareMin => Data.FireEscapeShareMin;
        public static float FireEscapeShareMax => Data.FireEscapeShareMax;
        public static ResidentialDecorRule[] All => Data.All;
        public static string[] NotReady => Data.NotReady;
        public static string[] Unresolved => Data.Unresolved;

        // The editor generator invalidates after importing its new JSON, which no
        // longer causes a C# domain reload. Also reset when domain reload is disabled.
#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        public static void Invalidate()
        {
            cached = null;
            unchecked { Revision++; }
        }

        static ResidentialDecorCatalog Load()
        {
#if UNITY_5_3_OR_NEWER
            var asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("ResidentialDecor");
            if (asset == null) throw new InvalidOperationException("Missing Resources/ResidentialDecor.json.");
            var result = UnityEngine.JsonUtility.FromJson<ResidentialDecorCatalog>(asset.text);
#else
            using var stream = typeof(ResidentialDecor).Assembly.GetManifestResourceStream("ResidentialDecor.json");
            if (stream == null) throw new InvalidOperationException("Missing embedded ResidentialDecor.json.");
            var result = System.Text.Json.JsonSerializer.Deserialize<ResidentialDecorCatalog>(stream,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
#endif
            if (result == null) throw new InvalidOperationException("Unreadable residential decor catalog.");
            result.Validate();
            return result;
        }
    }
}
