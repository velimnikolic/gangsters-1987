using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>One true-scale piece of a prepared building proxy, in world metres.</summary>
    public readonly struct TurfMapBuildingMass
    {
        public readonly Rect World;
        public readonly float Bottom;
        public readonly float Top;
        public readonly TurfType Type;
        public readonly int BlockId;

        public TurfMapBuildingMass(Rect world, float bottom, float top,
                                   TurfType type, int blockId)
        {
            World = world;
            Bottom = bottom;
            Top = Mathf.Max(bottom + 0.05f, top);
            Type = type;
            BlockId = blockId;
        }
    }

    public readonly struct TurfMapBuildingProxyReport
    {
        public readonly int Buildings;
        public readonly int Masses;
        public readonly int PrefabDerived;
        public readonly int SceneDerived;
        public readonly int Fallback;
        public readonly float Tallest;

        public TurfMapBuildingProxyReport(int buildings, int masses,
            int prefabDerived, int sceneDerived, int fallback, float tallest)
        {
            Buildings = buildings;
            Masses = masses;
            PrefabDerived = prefabDerived;
            SceneDerived = sceneDerived;
            Fallback = fallback;
            Tallest = tallest;
        }
    }

    /// <summary>
    /// RecyclerView for the TurfMap's prepared building tiles.
    ///
    /// Residential prefabs publish their map massing during the editor bake, and each
    /// ResidentialBlockRecipe places that data while the city model is generated. This
    /// layer groups the resulting immutable answer into world tiles once. Each tile owns
    /// a ready Mesh payload; a bounded pool of CanvasRenderer holders merely swaps those
    /// meshes as the survey window crosses tile boundaries. Panning only changes one root
    /// transform and tile membership. It never scans prefabs, sorts city-wide geometry or
    /// repopulates a giant uGUI mesh.
    /// </summary>
    public sealed class TurfMapBuildingLayer : MonoBehaviour
    {
        const float TileSize = 128f;
        const float HeadingStep = 0.5f;
        const float PitchStep = 0.35f;

        sealed class Tile
        {
            public Vector2Int Key;
            public Vector2 Origin;
            public Rect World;
            public readonly List<TurfMapBuildingMass> Masses =
                new List<TurfMapBuildingMass>();
            public Mesh Mesh;
            public Vector3[] Vertices;
            public Color32[] Colours;
            public Vector2[] Uvs;
            public int[] Triangles;
            public int VisibleStamp;
            public float MeshHeading = float.NaN;
            public float MeshPitch = float.NaN;
        }

        sealed class View
        {
            public RectTransform Rect;
            public CanvasRenderer Renderer;
            public Tile Tile;
        }

        sealed class DepthComparer : IComparer<TurfMapBuildingMass>
        {
            public float Heading;

            public int Compare(TurfMapBuildingMass a, TurfMapBuildingMass b)
            {
                float da = TurfMapHud.RotateForHeading(a.World.center, Heading).y;
                float db = TurfMapHud.RotateForHeading(b.World.center, Heading).y;
                int depth = db.CompareTo(da);
                if (depth != 0) return depth;
                int height = a.Top.CompareTo(b.Top);
                if (height != 0) return height;
                int x = a.World.xMin.CompareTo(b.World.xMin);
                return x != 0 ? x : a.World.yMin.CompareTo(b.World.yMin);
            }
        }

        readonly List<TurfMapBuildingMass> _masses =
            new List<TurfMapBuildingMass>();
        readonly List<Tile> _tiles = new List<Tile>();
        readonly Dictionary<Vector2Int, Tile> _tileByKey =
            new Dictionary<Vector2Int, Tile>();
        readonly List<View> _allViews = new List<View>();
        readonly List<View> _activeViews = new List<View>();
        readonly Stack<View> _pool = new Stack<View>();
        readonly DepthComparer _depth = new DepthComparer();

        RectTransform _worldRoot;
        TurfProjection _projection;
        float _heading = float.NaN;
        float _pitch = float.NaN;
        bool _hasProjection;
        bool _hasClip;
        Rect _clipRect;
        int _visibleStamp;

        public TurfMapBuildingProxyReport Report { get; private set; }
        public int GeometryVersion { get; private set; } = -1;
        public int VisibleChunks { get; private set; }
        public int TotalTiles => _tiles.Count;
        public int PooledTiles => _pool.Count;
        public int TotalMasses => _masses.Count;
        public int VisibleMasses { get; private set; }
        /// <summary>Number of actual tile-set changes, not survey/pan updates.</summary>
        public int ViewRebuilds { get; private set; }
        public int TileRebinds { get; private set; }
        public int MeshBuilds { get; private set; }

        /// <summary>
        /// Mount the one prepared building layer on whichever survey sheet is visible.
        /// TurfMap and its corner minimap are mutually exclusive, so sharing this layer
        /// keeps their building geometry identical without retaining a second city-wide
        /// mesh catalogue for the postcard.
        /// </summary>
        public void Attach(RectTransform sheet, int siblingIndex)
        {
            if (sheet == null) return;

            var rect = (RectTransform)transform;
            if (rect.parent != sheet)
                rect.SetParent(sheet, false);
            if (rect.localScale != Vector3.one)
                rect.localScale = Vector3.one;
            if (rect.localRotation != Quaternion.identity)
                rect.localRotation = Quaternion.identity;
            if (rect.anchorMin != Vector2.zero || rect.anchorMax != Vector2.one ||
                rect.offsetMin != Vector2.zero || rect.offsetMax != Vector2.zero)
                DemoUi.Fill(rect);
            if (!Mathf.Approximately(rect.localPosition.z, 0f))
            {
                var local = rect.localPosition;
                rect.localPosition = new Vector3(local.x, local.y, 0f);
            }
            int wanted = Mathf.Clamp(siblingIndex, 0,
                Mathf.Max(0, sheet.childCount - 1));
            if (rect.GetSiblingIndex() != wanted)
                rect.SetSiblingIndex(wanted);
        }

        /// <summary>Clip direct CanvasRenderer tile meshes to a compact map card.
        /// The full-screen sheet passes <c>false</c>; the minimap supplies its card in
        /// root-canvas coordinates because bare CanvasRenderers are not MaskableGraphic
        /// instances and therefore do not register themselves with RectMask2D.</summary>
        public void SetClipRect(Rect rect, bool enabled)
        {
            if (_hasClip == enabled && (!enabled || _clipRect == rect)) return;
            _hasClip = enabled;
            _clipRect = rect;
            for (int i = 0; i < _allViews.Count; i++)
                ApplyClip(_allViews[i].Renderer);
        }

        void ApplyClip(CanvasRenderer renderer)
        {
            if (renderer == null) return;
            if (_hasClip) renderer.EnableRectClipping(_clipRect);
            else renderer.DisableRectClipping();
        }

        /// <summary>Bank the shared camera pose before a geometry catalogue rebuild,
        /// so its prepared meshes are born in the pose the map will actually open at.</summary>
        public void PreparePose(float heading, float pitch)
        {
            _heading = heading;
            _pitch = pitch;
        }

        public void Rebuild(RoadDemoBuilder builder, TurfMapSurvey survey)
        {
            EnsureRoot();
            ReleaseAll();
            DisposeTiles();
            _masses.Clear();

            TurfMapBuildingProxyGeometry.Build(builder, survey, _masses, out var report);
            Report = report;
            GeometryVersion = builder != null ? builder.ResidentialGeometryVersion : -1;
            BuildTileCatalogue();

            float heading = float.IsNaN(_heading) ? 0f : _heading;
            float pitch = float.IsNaN(_pitch) ? 45f : _pitch;
            RebuildTileMeshes(heading, pitch);
            PrewarmViews(_tiles.Count);

            if (_hasProjection)
            {
                PoseRoot();
                RefreshVisible(force: true);
            }
        }

        /// <summary>Adopt the survey printed on the sheet. A pan changes only the
        /// world-root transform and which already-built tiles occupy pooled holders.</summary>
        public void SetView(TurfProjection projection, float heading, float pitch)
        {
            bool projectionChanged = !_hasProjection ||
                projection.OriginPx != _projection.OriginPx ||
                !Mathf.Approximately(projection.MetresPerUnit, _projection.MetresPerUnit);
            bool headingChanged = float.IsNaN(_heading) ||
                Mathf.Abs(Mathf.DeltaAngle(_heading, heading)) >= HeadingStep;
            bool pitchChanged = float.IsNaN(_pitch) ||
                Mathf.Abs(_pitch - pitch) >= PitchStep;
            if (!projectionChanged && !headingChanged && !pitchChanged) return;

            if (projectionChanged)
            {
                _projection = projection;
                _hasProjection = projection.MetresPerUnit > 0.0001f;
                PoseRoot();
            }

            if (headingChanged) _heading = heading;
            if (pitchChanged) _pitch = pitch;
            if ((headingChanged || pitchChanged) && _tiles.Count > 0)
            {
                for (int i = 0; i < _activeViews.Count; i++)
                {
                    EnsureTileMesh(_activeViews[i].Tile);
                    _activeViews[i].Renderer.SetMesh(_activeViews[i].Tile.Mesh);
                }
            }

            bool membershipChanged = projectionChanged && RefreshVisible(force: false);
            if (headingChanged || membershipChanged) SortActiveViews();
        }

        void EnsureRoot()
        {
            if (_worldRoot != null) return;
            _worldRoot = transform.Find("Turf Tile World") as RectTransform;
            if (_worldRoot != null)
            {
                // Domain reload keeps runtime UI objects but resets this component's
                // non-serialised holder catalogue. Retire those stale renderers before
                // rebuilding the pool, or the old tile meshes would remain underneath.
                for (int i = _worldRoot.childCount - 1; i >= 0; i--)
                {
                    var child = _worldRoot.GetChild(i).gameObject;
                    child.SetActive(false);
                    Destroy(child);
                }
            }
            else
            {
                _worldRoot = DemoUi.NewRect("Turf Tile World", transform);
                _worldRoot.anchorMin = _worldRoot.anchorMax = new Vector2(0.5f, 0.5f);
                _worldRoot.pivot = new Vector2(0.5f, 0.5f);
                _worldRoot.sizeDelta = Vector2.zero;
            }

            // Adopt and silence the pre-recycler chunk objects after a hot script reload.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == _worldRoot ||
                    !child.name.StartsWith("Building Volume ", StringComparison.Ordinal))
                    continue;
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        void PoseRoot()
        {
            if (!_hasProjection || _worldRoot == null) return;
            float pixelsPerMetre = TurfPlate.S /
                Mathf.Max(0.0001f, _projection.MetresPerUnit);
            _worldRoot.localScale = Vector3.one * pixelsPerMetre;
            _worldRoot.anchoredPosition = -_projection.World.center * pixelsPerMetre;
        }

        void BuildTileCatalogue()
        {
            for (int i = 0; i < _masses.Count; i++)
            {
                var mass = _masses[i];
                var key = new Vector2Int(
                    Mathf.FloorToInt(mass.World.center.x / TileSize),
                    Mathf.FloorToInt(mass.World.center.y / TileSize));
                if (!_tileByKey.TryGetValue(key, out var tile))
                {
                    tile = new Tile
                    {
                        Key = key,
                        Origin = new Vector2(
                            (key.x + 0.5f) * TileSize,
                            (key.y + 0.5f) * TileSize),
                        World = mass.World,
                    };
                    _tileByKey.Add(key, tile);
                    _tiles.Add(tile);
                }
                else
                    tile.World = Encapsulate(tile.World, mass.World);
                tile.Masses.Add(mass);
            }
        }

        bool RefreshVisible(bool force)
        {
            if (!_hasProjection)
            {
                bool had = _activeViews.Count > 0;
                ReleaseAll();
                return had;
            }

            unchecked { _visibleStamp++; }
            if (_visibleStamp == 0)
            {
                _visibleStamp = 1;
                for (int i = 0; i < _tiles.Count; i++) _tiles[i].VisibleStamp = 0;
            }

            float pitch = float.IsNaN(_pitch) ? 45f : Mathf.Clamp(_pitch, 5f, 89f);
            float lean = Report.Tallest /
                Mathf.Max(0.1f, Mathf.Tan(pitch * Mathf.Deg2Rad));
            float margin = Mathf.Max(10f, lean + 5f);
            var window = Expand(_projection.World, margin);
            bool changed = false;

            for (int i = 0; i < _tiles.Count; i++)
            {
                var tile = _tiles[i];
                if (!window.Overlaps(tile.World)) continue;
                tile.VisibleStamp = _visibleStamp;
                bool resident = false;
                for (int v = 0; v < _activeViews.Count; v++)
                    if (ReferenceEquals(_activeViews[v].Tile, tile))
                    { resident = true; break; }
                if (resident) continue;
                Bind(AcquireView(), tile);
                changed = true;
            }

            for (int i = _activeViews.Count - 1; i >= 0; i--)
            {
                var view = _activeViews[i];
                if (view.Tile != null && view.Tile.VisibleStamp == _visibleStamp) continue;
                Release(view);
                changed = true;
            }

            VisibleChunks = _activeViews.Count;
            VisibleMasses = 0;
            for (int i = 0; i < _activeViews.Count; i++)
                VisibleMasses += _activeViews[i].Tile.Masses.Count;
            if (changed || force) ViewRebuilds++;
            return changed;
        }

        void PrewarmViews(int wanted)
        {
            EnsureRoot();
            while (_allViews.Count < wanted)
            {
                var rect = DemoUi.NewRect(
                    "Turf Tile View " + _allViews.Count, _worldRoot);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = Vector2.zero;
                var renderer = rect.gameObject.AddComponent<CanvasRenderer>();
                renderer.materialCount = 1;
                renderer.SetMaterial(Graphic.defaultGraphicMaterial, 0);
                renderer.SetTexture(Texture2D.whiteTexture);
                renderer.cullTransparentMesh = true;
                renderer.cull = true;
                ApplyClip(renderer);
                var view = new View { Rect = rect, Renderer = renderer };
                _allViews.Add(view);
                _pool.Push(view);
            }
        }

        View AcquireView()
        {
            if (_pool.Count == 0) PrewarmViews(_allViews.Count + 1);
            return _pool.Pop();
        }

        void Bind(View view, Tile tile)
        {
            view.Tile = tile;
            view.Rect.localPosition = new Vector3(tile.Origin.x, tile.Origin.y, 0f);
            EnsureTileMesh(tile);
            view.Renderer.SetMesh(tile.Mesh);
            view.Renderer.cull = false;
            _activeViews.Add(view);
            TileRebinds++;
        }

        void Release(View view)
        {
            if (view == null) return;
            view.Renderer.cull = true;
            view.Tile = null;
            _activeViews.Remove(view);
            _pool.Push(view);
        }

        void ReleaseAll()
        {
            for (int i = _activeViews.Count - 1; i >= 0; i--)
                Release(_activeViews[i]);
            VisibleChunks = 0;
            VisibleMasses = 0;
        }

        void SortActiveViews()
        {
            float heading = float.IsNaN(_heading) ? 0f : _heading;
            _activeViews.Sort((a, b) =>
            {
                float da = TurfMapHud.RotateForHeading(a.Tile.World.center, heading).y;
                float db = TurfMapHud.RotateForHeading(b.Tile.World.center, heading).y;
                int depth = db.CompareTo(da);
                if (depth != 0) return depth;
                int x = a.Tile.Key.x.CompareTo(b.Tile.Key.x);
                return x != 0 ? x : a.Tile.Key.y.CompareTo(b.Tile.Key.y);
            });
            for (int i = 0; i < _activeViews.Count; i++)
                _activeViews[i].Rect.SetSiblingIndex(i);
        }

        void RebuildTileMeshes(float heading, float pitch)
        {
            _depth.Heading = heading;
            for (int i = 0; i < _tiles.Count; i++) BuildMesh(_tiles[i], heading, pitch);
        }

        void EnsureTileMesh(Tile tile)
        {
            if (tile == null) return;
            float heading = float.IsNaN(_heading) ? 0f : _heading;
            float pitch = float.IsNaN(_pitch) ? 45f : _pitch;
            if (tile.Mesh != null &&
                Mathf.Abs(Mathf.DeltaAngle(tile.MeshHeading, heading)) < HeadingStep &&
                Mathf.Abs(tile.MeshPitch - pitch) < PitchStep)
                return;
            _depth.Heading = heading;
            BuildMesh(tile, heading, pitch);
        }

        void BuildMesh(Tile tile, float heading, float pitch)
        {
            tile.Masses.Sort(_depth);
            int massCount = tile.Masses.Count;
            int vertices = massCount * 12;
            int indices = massCount * 18;
            if (tile.Vertices == null || tile.Vertices.Length != vertices)
            {
                tile.Vertices = new Vector3[vertices];
                tile.Colours = new Color32[vertices];
                tile.Uvs = new Vector2[vertices];
                tile.Triangles = new int[indices];
            }

            float radians = heading * Mathf.Deg2Rad;
            var viewer = new Vector2(-Mathf.Sin(radians), -Mathf.Cos(radians));
            int v = 0, t = 0;
            for (int i = 0; i < massCount; i++)
            {
                var mass = tile.Masses[i];
                var style = TurfTypeStyle.Of(mass.Type);
                Color32 roof = WithAlpha(style.Fill, 244);
                Color32 sideA = Shade(style.Fill, 0.73f, 252);
                Color32 sideB = Shade(style.Fill, 0.58f, 252);

                float x0 = mass.World.xMin, x1 = mass.World.xMax;
                float z0 = mass.World.yMin, z1 = mass.World.yMax;
                Vector2 b00 = P(x0, mass.Bottom, z0);
                Vector2 b10 = P(x1, mass.Bottom, z0);
                Vector2 b11 = P(x1, mass.Bottom, z1);
                Vector2 b01 = P(x0, mass.Bottom, z1);
                Vector2 t00 = P(x0, mass.Top, z0);
                Vector2 t10 = P(x1, mass.Top, z0);
                Vector2 t11 = P(x1, mass.Top, z1);
                Vector2 t01 = P(x0, mass.Top, z1);

                if (viewer.y <= 0f) AddQuad(b00, t00, t10, b10, sideA);
                else AddQuad(b11, t11, t01, b01, sideA);
                if (viewer.x <= 0f) AddQuad(b01, t01, t00, b00, sideB);
                else AddQuad(b10, t10, t11, b11, sideB);
                AddQuad(t00, t01, t11, t10, roof);

                Vector2 P(float x, float y, float z) =>
                    new Vector2(x - tile.Origin.x, z - tile.Origin.y) +
                    HeightOffsetWorld(y, pitch, heading);

                void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 colour)
                {
                    int first = v;
                    Put(a, colour); Put(b, colour); Put(c, colour); Put(d, colour);
                    tile.Triangles[t++] = first;
                    tile.Triangles[t++] = first + 1;
                    tile.Triangles[t++] = first + 2;
                    tile.Triangles[t++] = first;
                    tile.Triangles[t++] = first + 2;
                    tile.Triangles[t++] = first + 3;
                }

                void Put(Vector2 at, Color32 colour)
                {
                    tile.Vertices[v] = new Vector3(at.x, at.y, 0f);
                    tile.Colours[v] = colour;
                    tile.Uvs[v] = Vector2.zero;
                    v++;
                }
            }

            if (tile.Mesh == null)
            {
                tile.Mesh = new Mesh
                {
                    name = $"Turf buildings {tile.Key.x},{tile.Key.y}",
                    hideFlags = HideFlags.DontSave,
                };
            }
            tile.Mesh.Clear(false);
            tile.Mesh.indexFormat = vertices > ushort.MaxValue
                ? IndexFormat.UInt32 : IndexFormat.UInt16;
            tile.Mesh.vertices = tile.Vertices;
            tile.Mesh.colors32 = tile.Colours;
            tile.Mesh.uv = tile.Uvs;
            tile.Mesh.triangles = tile.Triangles;
            tile.Mesh.RecalculateBounds();
            tile.MeshHeading = heading;
            tile.MeshPitch = pitch;
            MeshBuilds++;
        }

        void DisposeTiles()
        {
            for (int i = 0; i < _tiles.Count; i++)
                if (_tiles[i].Mesh != null) Destroy(_tiles[i].Mesh);
            _tiles.Clear();
            _tileByKey.Clear();
        }

        void OnDestroy() => DisposeTiles();

        internal static Vector2 HeightOffsetWorld(float height, float pitch, float heading)
        {
            float radians = Mathf.Clamp(pitch, 0.01f, 89.99f) * Mathf.Deg2Rad;
            float tilt = Mathf.Max(0.0001f, Mathf.Sin(radians));
            float rise = Mathf.Cos(radians);
            return TurfMapHud.RotateForHeading(
                new Vector2(0f, height * rise / tilt), -heading);
        }

        internal static Vector2 Project(TurfProjection projection, Vector3 world,
                                        float heading, float pitch)
        {
            float pixelsPerMetre = TurfPlate.S /
                Mathf.Max(0.0001f, projection.MetresPerUnit);
            return (new Vector2(world.x, world.z) - projection.World.center +
                    HeightOffsetWorld(world.y, pitch, heading)) * pixelsPerMetre;
        }

        static Rect Expand(Rect rect, float by) => Rect.MinMaxRect(
            rect.xMin - by, rect.yMin - by, rect.xMax + by, rect.yMax + by);

        static Rect Encapsulate(Rect one, Rect other) => Rect.MinMaxRect(
            Mathf.Min(one.xMin, other.xMin), Mathf.Min(one.yMin, other.yMin),
            Mathf.Max(one.xMax, other.xMax), Mathf.Max(one.yMax, other.yMax));

        static Color32 WithAlpha(Color32 colour, byte alpha)
            => new Color32(colour.r, colour.g, colour.b, alpha);

        static Color32 Shade(Color32 colour, float amount, byte alpha) => new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.r * amount), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.g * amount), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.b * amount), 0, 255), alpha);
    }

    /// <summary>Collects immutable recipe proxies plus fixed scene structures. The
    /// residential path is data-only; only genuinely fixed scene buildings inspect live
    /// renderers once when the city catalogue changes.</summary>
    static class TurfMapBuildingProxyGeometry
    {
        const float Sample = 1f;
        const float MinimumBuildingHeight = 1.5f;
        const int MaximumGridSide = 160;

        readonly struct SceneMass
        {
            public readonly Rect Footprint;
            public readonly float Bottom;
            public readonly float Top;

            public SceneMass(Rect footprint, float bottom, float top)
            {
                Footprint = footprint;
                Bottom = bottom;
                Top = top;
            }
        }

        public static void Build(RoadDemoBuilder builder, TurfMapSurvey survey,
            List<TurfMapBuildingMass> into, out TurfMapBuildingProxyReport report)
        {
            int buildings = 0, prefabDerived = 0, sceneDerived = 0, fallback = 0;
            float tallest = 0f;
            var covered = new List<Rect>();

            if (builder != null)
                foreach (var source in builder.ResidentialMapSources)
                {
                    if (source.Model == null) continue;
                    for (int r = 0; r < source.Model.Blocks.Count; r++)
                    {
                        var recipe = source.Model.Blocks[r];
                        if (recipe == null) continue;
                        Rect whole = default;
                        bool any = false;
                        var ready = recipe.TurfMasses;
                        for (int m = 0; m < ready.Count; m++)
                        {
                            var local = ready[m];
                            if (local.StartsBuilding)
                            {
                                if (any) covered.Add(whole);
                                whole = default;
                                any = false;
                                buildings++;
                                if (local.PrefabDerived) prefabDerived++;
                                else fallback++;
                            }

                            Rect world = source.Frame.ToWorldRect(local.Local);
                            float bottom = source.Frame.origin.y + local.Bottom;
                            float top = source.Frame.origin.y + local.Top;
                            into.Add(new TurfMapBuildingMass(
                                world, bottom, top, local.Type, recipe.BlockId));
                            whole = any ? Encapsulate(whole, world) : world;
                            any = true;
                            tallest = Mathf.Max(tallest, top - bottom);
                        }
                        if (any) covered.Add(whole);
                    }
                }

            if (survey != null)
                for (int i = 0; i < survey.Buildings.Count; i++)
                {
                    var building = survey.Buildings[i];
                    // A live streamed residential holder is another view of the model
                    // data above, not a second fixed building. Duplicating it caused the
                    // detached dark slabs that appeared and disappeared while panning.
                    if (building?.Tf == null || Covered(covered, building.World)) continue;
                    var worldMasses = SceneMasses(building);
                    if (worldMasses.Count == 0) continue;
                    Rect whole = default;
                    bool any = false;
                    for (int m = 0; m < worldMasses.Count; m++)
                    {
                        var source = worldMasses[m];
                        into.Add(new TurfMapBuildingMass(
                            source.Footprint, source.Bottom, source.Top,
                            building.Type, building.BlockId));
                        whole = any ? Encapsulate(whole, source.Footprint) : source.Footprint;
                        any = true;
                        tallest = Mathf.Max(tallest, source.Top - source.Bottom);
                    }
                    if (!any) continue;
                    covered.Add(whole);
                    buildings++;
                    sceneDerived++;
                }

            if (survey != null)
                for (int i = 0; i < survey.Buildings.Count; i++)
                {
                    var building = survey.Buildings[i];
                    if (building == null || Covered(covered, building.World)) continue;
                    float top = Mathf.Max(2f, building.Floors * 3.2f);
                    into.Add(new TurfMapBuildingMass(
                        building.World, 0f, top, building.Type, building.BlockId));
                    covered.Add(building.World);
                    tallest = Mathf.Max(tallest, top);
                    buildings++;
                    fallback++;
                }

            report = new TurfMapBuildingProxyReport(
                buildings, into.Count, prefabDerived, sceneDerived, fallback, tallest);
        }

        static List<SceneMass> SceneMasses(TurfBuilding building)
        {
            var result = new List<SceneMass>();
            if (building?.Tf == null) return result;

            var structural = new List<Bounds>();
            var reserve = new List<Bounds>();
            var renderers = building.Tf.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.bounds.max.y <= MinimumBuildingHeight)
                    continue;
                reserve.Add(renderer.bounds);
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && Structural(filter, filter.sharedMesh))
                    structural.Add(renderer.bounds);
            }
            Rasterise(structural.Count > 0 ? structural : reserve, result);
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

        static void Rasterise(List<Bounds> boxes, List<SceneMass> result)
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
                    float high = top[i, j], low = bottom[i, j];
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
                    result.Add(new SceneMass(
                        new Rect(x0 + i * step, z0 + j * step,
                                 wide * step, deep * step),
                        low == float.MaxValue ? 0f : low, high));
                }
        }

        static bool Covered(List<Rect> coverage, Rect world)
        {
            for (int i = 0; i < coverage.Count; i++)
                if (coverage[i].Contains(world.center) || world.Contains(coverage[i].center))
                    return true;
            return false;
        }

        static bool Same(float a, float b) => Mathf.Abs(a - b) <= 0.02f;

        static Rect Encapsulate(Rect one, Rect other) => Rect.MinMaxRect(
            Mathf.Min(one.xMin, other.xMin), Mathf.Min(one.yMin, other.yMin),
            Mathf.Max(one.xMax, other.xMax), Mathf.Max(one.yMax, other.yMax));
    }
}
