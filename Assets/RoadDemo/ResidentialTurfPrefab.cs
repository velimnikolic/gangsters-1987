using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>One structural slice baked into a residential prefab for the TurfMap.
    /// It is deliberately plain serialised data: loading a generated block recipe never
    /// has to inspect renderers, read mesh bounds or rasterise a prefab.</summary>
    [Serializable]
    public struct ResidentialTurfPrefabMass
    {
        [SerializeField] Rect _footprint;
        [SerializeField] float _bottom;
        [SerializeField] float _top;

        public Rect Footprint => _footprint;
        public float Bottom => _bottom;
        public float Top => _top;

        public ResidentialTurfPrefabMass(Rect footprint, float bottom, float top)
        {
            _footprint = footprint;
            _bottom = bottom;
            _top = Mathf.Max(bottom + 0.05f, top);
        }
    }

    /// <summary>
    /// Offline-produced TurfMap companion carried by the normal residential prefab.
    /// ResidentialHarvest refreshes it whenever it bakes the 3D prefab. Runtime code
    /// only reads the compact rectangles below; it never traverses the prefab meshes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResidentialTurfPrefab : MonoBehaviour
    {
        [SerializeField] ResidentialTurfPrefabMass[] _masses =
            Array.Empty<ResidentialTurfPrefabMass>();

        public int MassCount => _masses?.Length ?? 0;
        public ResidentialTurfPrefabMass MassAt(int index) => _masses[index];
        public ResidentialTurfPrefabMass[] CopyMasses() =>
            _masses != null ? (ResidentialTurfPrefabMass[])_masses.Clone()
                            : Array.Empty<ResidentialTurfPrefabMass>();

        /// <summary>Prepared proxy for a generated modular unit which has no single
        /// authored prefab root (the three one-cell frontage variants). The editor writes
        /// this answer into <see cref="ResidentialTurfCatalog"/> beside ordinary harvested
        /// prefab proxies. Runtime only reaches this method as compatibility for an old or
        /// missing catalog.</summary>
        public static ResidentialTurfPrefabMass[] FromMask(ResidentialUnit unit)
        {
            if (unit == null) return Array.Empty<ResidentialTurfPrefabMass>();
            var result = new List<ResidentialTurfPrefabMass>();
            var used = new bool[Mathf.Max(1, unit.CW), Mathf.Max(1, unit.CD)];
            float cell = ResidentialLot.Cell;
            float top = Mathf.Max(2f, unit.MaxH);
            for (int j = 0; j < unit.CD; j++)
                for (int i = 0; i < unit.CW; i++)
                {
                    if (used[i, j] || !unit.Wall(i, j)) continue;
                    int wide = 1;
                    while (i + wide < unit.CW && !used[i + wide, j] &&
                           unit.Wall(i + wide, j)) wide++;
                    int deep = 1;
                    bool more = true;
                    while (j + deep < unit.CD && more)
                    {
                        for (int x = 0; x < wide; x++)
                            if (used[i + x, j + deep] || !unit.Wall(i + x, j + deep))
                            { more = false; break; }
                        if (more) deep++;
                    }
                    for (int x = 0; x < wide; x++)
                        for (int z = 0; z < deep; z++) used[i + x, j + z] = true;
                    result.Add(new ResidentialTurfPrefabMass(
                        new Rect(i * cell, j * cell, wide * cell, deep * cell),
                        0f, top));
                }
            return result.ToArray();
        }

#if UNITY_EDITOR
        const float Sample = 1f;
        const float MinimumBuildingHeight = 1.5f;
        const int MaximumGridSide = 160;

        /// <summary>Editor-only prefab bake. The runtime player contains no mesh scan
        /// path, which makes a missing proxy an explicit cheap mask fallback.</summary>
        public static ResidentialTurfPrefab BakeInto(
            GameObject root, float lotWidth, float lotDepth, float measuredHeight)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var proxy = root.GetComponent<ResidentialTurfPrefab>();
            if (proxy == null)
            {
                // A proxy baked before this component had its Unity-matching filename
                // serialised as a missing root script. Retire exactly that root entry;
                // nested source-prefab components are not ours to alter.
                UnityEditor.GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
                proxy = root.AddComponent<ResidentialTurfPrefab>();
            }

            var boxes = StructuralBounds(root, lotWidth, lotDepth, measuredHeight);
            var made = new List<ResidentialTurfPrefabMass>();
            Rasterise(boxes, made);
            proxy._masses = made.ToArray();
            UnityEditor.EditorUtility.SetDirty(proxy);
            return proxy;
        }

        static List<Bounds> StructuralBounds(
            GameObject root, float lotWidth, float lotDepth, float measuredHeight)
        {
            var result = new List<Bounds>();
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            Matrix4x4 intoRoot = root.transform.worldToLocalMatrix;
            float margin = 5f;
            float cap = Mathf.Max(MinimumBuildingHeight, measuredHeight + 1f);
            var allowed = Rect.MinMaxRect(-margin, -margin,
                Mathf.Max(0.1f, lotWidth) + margin,
                Mathf.Max(0.1f, lotDepth) + margin);

            for (int i = 0; i < filters.Length; i++)
            {
                var filter = filters[i];
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || !Structural(filter, mesh)) continue;
                var box = TransformBounds(mesh.bounds,
                    intoRoot * filter.transform.localToWorldMatrix);
                if (box.max.y <= MinimumBuildingHeight ||
                    box.size.x <= 0.02f || box.size.z <= 0.02f) continue;

                float x0 = Mathf.Max(allowed.xMin, box.min.x);
                float z0 = Mathf.Max(allowed.yMin, box.min.z);
                float x1 = Mathf.Min(allowed.xMax, box.max.x);
                float z1 = Mathf.Min(allowed.yMax, box.max.z);
                if (x1 <= x0 || z1 <= z0) continue;
                float bottom = Mathf.Clamp(box.min.y, -1.5f, 0f);
                float top = Mathf.Clamp(box.max.y, MinimumBuildingHeight, cap);
                result.Add(new Bounds(
                    new Vector3((x0 + x1) * 0.5f, (bottom + top) * 0.5f,
                                (z0 + z1) * 0.5f),
                    new Vector3(x1 - x0, top - bottom, z1 - z0)));
            }
            return result;
        }

        static bool Structural(MeshFilter filter, Mesh mesh)
        {
            string name = ((mesh != null ? mesh.name : "") + " " +
                           (filter != null ? filter.gameObject.name : "")).ToLowerInvariant();
            return name.Contains("sm_bld") || name.Contains("building") ||
                   name.Contains("apartment") || name.Contains("roof") ||
                   name.Contains("facade");
        }

        static void Rasterise(List<Bounds> boxes, List<ResidentialTurfPrefabMass> result)
        {
            if (boxes == null || boxes.Count == 0) return;
            float x0 = float.MaxValue, z0 = float.MaxValue;
            float x1 = float.MinValue, z1 = float.MinValue;
            for (int i = 0; i < boxes.Count; i++)
            {
                x0 = Mathf.Min(x0, boxes[i].min.x); z0 = Mathf.Min(z0, boxes[i].min.z);
                x1 = Mathf.Max(x1, boxes[i].max.x); z1 = Mathf.Max(z1, boxes[i].max.z);
            }
            if (x0 >= x1 || z0 >= z1) return;

            float step = Mathf.Max(Sample,
                Mathf.Max((x1 - x0) / MaximumGridSide, (z1 - z0) / MaximumGridSide));
            x0 = Mathf.Floor(x0 / step) * step;
            z0 = Mathf.Floor(z0 / step) * step;
            x1 = Mathf.Ceil(x1 / step) * step;
            z1 = Mathf.Ceil(z1 / step) * step;
            int nx = Mathf.Clamp(Mathf.RoundToInt((x1 - x0) / step), 1, MaximumGridSide);
            int nz = Mathf.Clamp(Mathf.RoundToInt((z1 - z0) / step), 1, MaximumGridSide);
            var bottom = new float[nx, nz];
            var top = new float[nx, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++) bottom[i, j] = float.MaxValue;

            for (int n = 0; n < boxes.Count; n++)
            {
                var box = boxes[n];
                int i0 = Mathf.Clamp(Mathf.FloorToInt((box.min.x - x0) / step), 0, nx - 1);
                int i1 = Mathf.Clamp(Mathf.CeilToInt((box.max.x - x0) / step) - 1, 0, nx - 1);
                int j0 = Mathf.Clamp(Mathf.FloorToInt((box.min.z - z0) / step), 0, nz - 1);
                int j1 = Mathf.Clamp(Mathf.CeilToInt((box.max.z - z0) / step) - 1, 0, nz - 1);
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                    {
                        bottom[i, j] = Mathf.Min(bottom[i, j], box.min.y);
                        top[i, j] = Mathf.Max(top[i, j], box.max.y);
                    }
            }

            var used = new bool[nx, nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float high = top[i, j];
                    float low = bottom[i, j];
                    if (used[i, j] || high <= MinimumBuildingHeight) continue;

                    int wide = 1;
                    while (i + wide < nx && !used[i + wide, j] &&
                           Same(top[i + wide, j], high) && Same(bottom[i + wide, j], low))
                        wide++;
                    int deep = 1;
                    bool more = true;
                    while (j + deep < nz && more)
                    {
                        for (int x = 0; x < wide; x++)
                            if (used[i + x, j + deep] ||
                                !Same(top[i + x, j + deep], high) ||
                                !Same(bottom[i + x, j + deep], low))
                            { more = false; break; }
                        if (more) deep++;
                    }

                    for (int x = 0; x < wide; x++)
                        for (int z = 0; z < deep; z++) used[i + x, j + z] = true;
                    result.Add(new ResidentialTurfPrefabMass(
                        new Rect(x0 + i * step, z0 + j * step,
                                 wide * step, deep * step),
                        low == float.MaxValue ? 0f : low, high));
                }
        }

        static bool Same(float a, float b) => Mathf.Abs(a - b) <= 0.02f;

        static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            var min = bounds.min;
            var max = bounds.max;
            var first = matrix.MultiplyPoint3x4(new Vector3(min.x, min.y, min.z));
            var result = new Bounds(first, Vector3.zero);
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                        result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z)));
            return result;
        }
#endif
    }

    /// <summary>Already-placed, GameObject-free map payload owned by one generated
    /// residential block recipe.</summary>
    public readonly struct ResidentialTurfMass
    {
        public readonly Rect Local;
        public readonly float Bottom;
        public readonly float Top;
        public readonly TurfType Type;
        public readonly bool StartsBuilding;
        public readonly bool PrefabDerived;
        public readonly string SourceName;
        public readonly ResidentialKind SourceKind;

        public ResidentialTurfMass(Rect local, float bottom, float top, TurfType type,
                                   bool startsBuilding, bool prefabDerived,
                                   string sourceName, ResidentialKind sourceKind)
        {
            Local = local;
            Bottom = bottom;
            Top = Mathf.Max(bottom + 0.05f, top);
            Type = type;
            StartsBuilding = startsBuilding;
            PrefabDerived = prefabDerived;
            SourceName = sourceName;
            SourceKind = sourceKind;
        }
    }

    /// <summary>Places baked prefab proxy data at the same moment the procedural block
    /// recipe is generated. This path performs no renderer or mesh inspection.</summary>
    static class ResidentialTurfRecipeBaker
    {
        sealed class UnitProxy
        {
            public readonly List<ResidentialTurfPrefabMass> Masses =
                new List<ResidentialTurfPrefabMass>();
            public bool PrefabDerived;
        }

        static readonly Dictionary<string, UnitProxy> Units =
            new Dictionary<string, UnitProxy>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Units.Clear();

        public static void Bake(ResidentialLot.Plan plan, Rect block,
                                List<ResidentialTurfMass> into)
        {
            into.Clear();
            if (plan?.Spots == null) return;

            for (int s = 0; s < plan.Spots.Count; s++)
            {
                var spot = plan.Spots[s];
                var unit = spot?.Unit;
                if (unit == null || unit.Kind == ResidentialKind.Park) continue;
                var proxy = For(unit);
                if (proxy.Masses.Count == 0) continue;

                var placed = UnitMatrix(block, spot, unit);
                TurfType type = TypeOf(unit, spot.Shop);
                for (int m = 0; m < proxy.Masses.Count; m++)
                {
                    var source = proxy.Masses[m];
                    into.Add(new ResidentialTurfMass(
                        TransformRect(source.Footprint, placed),
                        source.Bottom, source.Top, type,
                        startsBuilding: m == 0,
                        prefabDerived: proxy.PrefabDerived,
                        sourceName: unit.Name, sourceKind: unit.Kind));
                }
            }
        }

        static UnitProxy For(ResidentialUnit unit)
        {
            string key = unit?.Name ?? "";
            if (Units.TryGetValue(key, out var known)) return known;

            var made = new UnitProxy();
            if (unit != null && ResidentialTurfCatalog.TryGet(key, out var baked))
            {
                made.Masses.AddRange(baked);
                made.PrefabDerived = made.Masses.Count > 0;
            }

            if (made.Masses.Count == 0 && unit != null)
                made.Masses.AddRange(ResidentialTurfPrefab.FromMask(unit));
            Units[key] = made;
            return made;
        }

        static Matrix4x4 UnitMatrix(Rect block, ResidentialLot.Spot spot,
                                    ResidentialUnit unit)
        {
            float cell = ResidentialLot.Cell;
            float w = unit.CW * cell, d = unit.CD * cell;
            Vector3 offset = spot.Yaw switch
            {
                90 => new Vector3(0f, 0f, w),
                180 => new Vector3(w, 0f, d),
                270 => new Vector3(d, 0f, 0f),
                _ => Vector3.zero,
            };
            var local = new Vector3(
                block.xMin + spot.I * cell, 0f,
                block.yMin + spot.J * cell) + offset;
            return Matrix4x4.TRS(local, Quaternion.Euler(0f, spot.Yaw, 0f), Vector3.one);
        }

        static Rect TransformRect(Rect rect, Matrix4x4 matrix)
        {
            var a = matrix.MultiplyPoint3x4(new Vector3(rect.xMin, 0f, rect.yMin));
            var b = matrix.MultiplyPoint3x4(new Vector3(rect.xMax, 0f, rect.yMin));
            var c = matrix.MultiplyPoint3x4(new Vector3(rect.xMax, 0f, rect.yMax));
            var d = matrix.MultiplyPoint3x4(new Vector3(rect.xMin, 0f, rect.yMax));
            return Rect.MinMaxRect(
                Mathf.Min(a.x, b.x, c.x, d.x), Mathf.Min(a.z, b.z, c.z, d.z),
                Mathf.Max(a.x, b.x, c.x, d.x), Mathf.Max(a.z, b.z, c.z, d.z));
        }

        static TurfType TypeOf(ResidentialUnit unit, bool shop)
        {
            if (unit != null && (unit.Kind == ResidentialKind.Storefront || shop))
                return TurfType.Shop;
            if (unit != null && unit.Kind == ResidentialKind.Amenity)
                return TurfType.Civic;
            int floors = Mathf.Max(1, Mathf.RoundToInt(
                Mathf.Max(2f, unit != null ? unit.MaxH : 2f) / 3.2f));
            if (floors >= 7) return TurfType.Tower;
            if (floors >= 3) return TurfType.Apartment;
            return TurfType.House;
        }
    }
}
