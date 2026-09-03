using System;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The measured door cut carried by one POLYGON City shop module. Coordinates are in
    /// the source module's frame (pivot at the north-east corner, facade on local +Z).
    /// This is shared by the residential harvest, the one-time mesh bake and the live
    /// storefront layer so those three stages cannot quietly disagree about a threshold.
    /// </summary>
    public readonly struct StorefrontDoorProfile
    {
        public StorefrontDoorProfile(string module, float x, float z, float width,
                                     float height, int leaves, float yaw)
        {
            Module = module;
            X = x;
            Z = z;
            Width = width;
            Height = height;
            Leaves = leaves;
            Yaw = yaw;
        }

        public string Module { get; }
        public float X { get; }
        public float Z { get; }
        public float Width { get; }
        public float Height { get; }
        public int Leaves { get; }
        /// <summary>Local facade yaw; 45 degrees is Corner_02's chamfer.</summary>
        public float Yaw { get; }
        public Vector3 Centre => new Vector3(X, 0f, Z);
        public Vector3 Outward => Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;
        public Vector3 Right => Vector3.Cross(Vector3.up, Outward).normalized;
    }

    public static class StorefrontDoorCatalog
    {
        // The glass extents were read from the imported FBX. Width is the surrounding
        // frame which leaves the jamb in the doorless wall, not merely the transparent
        // pane. Shop_06 is the solid panel at x -4.78..-3.89 in the wall mesh.
        static readonly StorefrontDoorProfile[] Profiles =
        {
            new StorefrontDoorProfile("SM_Bld_Shop_01", -2.50f, -0.02f, 1.70f, 2.22f, 2, 0f),
            new StorefrontDoorProfile("SM_Bld_Shop_02", -4.03f, -0.21f, 1.25f, 2.40f, 2, 0f),
            new StorefrontDoorProfile("SM_Bld_Shop_03", -5.00f, -0.05f, 1.90f, 2.40f, 2, 0f),
            new StorefrontDoorProfile("SM_Bld_Shop_04", -2.50f,  0.61f, 1.40f, 2.62f, 1, 0f),
            new StorefrontDoorProfile("SM_Bld_Shop_05", -2.50f, -0.11f, 0.00f, 0.00f, 0, 0f),
            new StorefrontDoorProfile("SM_Bld_Shop_06", -4.34f,  0.12f, 1.10f, 2.50f, 1, 0f),
            new StorefrontDoorProfile("SM_Bld_Shop_Corner_01", -0.84f, -0.04f, 1.30f, 2.05f, 2, 0f),
            new StorefrontDoorProfile("SM_Bld_Shop_Corner_02", -0.77f, -0.77f, 1.27f, 2.62f, 1, 45f),
        };

        public static int Count => Profiles.Length;
        public static StorefrontDoorProfile At(int index) => Profiles[index];

        public static bool TryGet(string source, out StorefrontDoorProfile profile)
        {
            source = Normalise(source);
            for (int i = 0; i < Profiles.Length; i++)
                if (string.Equals(Profiles[i].Module, source,
                                  StringComparison.OrdinalIgnoreCase))
                {
                    profile = Profiles[i];
                    return true;
                }
            profile = default;
            return false;
        }

        public static string Normalise(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            int slash = Mathf.Max(source.LastIndexOf('/'), source.LastIndexOf('\\'));
            if (slash >= 0) source = source.Substring(slash + 1);
            if (source.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                source = source.Substring(0, source.Length - 7);
            const string clone = "(Clone)";
            if (source.EndsWith(clone, StringComparison.OrdinalIgnoreCase))
                source = source.Substring(0, source.Length - clone.Length).TrimEnd();
            return source;
        }
    }
}
