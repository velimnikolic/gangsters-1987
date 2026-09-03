using System;
using System.Collections.Generic;
using LivingCity.Entities;
using LivingCity.Territory;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadDemo
{
    public enum StorefrontState
    {
        Intact,
        Open,
        Smashed,
        Burning,
        Boarded,
        Shuttered,
    }

    /// <summary>
    /// The disposable view of one logical residential shop bay. The simulation owns the
    /// business and its damage; this component owns only the independently live facade:
    /// glass, Synty's cut-out leaves, boards, fire anchor and roller shutter.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class Storefront : MonoBehaviour
    {
        const string AssetRoot = "Assets/CityKit/Storefront/";
        const string GlassMaterialPath =
            "Assets/Synty/PolygonCity/Materials/Misc/Glass_01.mat";
        const string WallMaterialPath =
            "Assets/Synty/PolygonCity/Materials/Alts/PolygonCity_01_A.mat";
        const float DoorSeconds = 0.55f;
        const float DoorDegrees = 78f;

        [SerializeField] string module = string.Empty;
        [SerializeField] Vector3 doorLocal;
        [SerializeField] float doorYaw;
        [SerializeField] float frontageWidth = 4.4f;
        [SerializeField] Vector3 bindingCentreLocal;
        [SerializeField] Vector3 bindingSizeLocal = new Vector3(5f, 3f, 5f);
        [SerializeField] ResidentialStorefrontOpening[] openings =
            Array.Empty<ResidentialStorefrontOpening>();
        [SerializeField] MeshFilter[] sourceWalls = Array.Empty<MeshFilter>();
        [SerializeField] MeshRenderer[] sourceGlass = Array.Empty<MeshRenderer>();
        [SerializeField] Mesh[] doorlessWalls = Array.Empty<Mesh>();
        [SerializeField] Material paneMaterial;
        [SerializeField] Material facadeMaterial;
        [SerializeField] Material rollerMaterial;
        [SerializeField] bool authoringPreview;
        [SerializeField] StorefrontState authoringState;

        [NonSerialized] TerritoryBusinessId businessId;
        [NonSerialized] StorefrontState damageState;
        [NonSerialized] bool shuttered;
        [NonSerialized] float doorAmount;
        [NonSerialized] float doorTarget;
        [NonSerialized] float burningUntil;
        [NonSerialized] float nextStatePoll;
        [NonSerialized] Transform damageVisual;

        readonly List<Transform> leaves = new List<Transform>(2);
        readonly List<Quaternion> leafClosed = new List<Quaternion>(2);
        readonly List<Mesh> runtimeMeshes = new List<Mesh>(8);

        Transform panesRoot;
        GameObject boards;
        GameObject shutter;
        ShopEntrance entrance;
        BuildingDoor buildingDoor;

        public StorefrontState State => damageState != StorefrontState.Intact
            ? damageState
            : shuttered ? StorefrontState.Shuttered
            : doorAmount > 0.001f ? StorefrontState.Open
            : StorefrontState.Intact;
        public TerritoryBusinessId BusinessId => businessId;
        public string Module => module;
        public Vector3 DoorWorld => transform.TransformPoint(doorLocal);
        public Vector3 OutwardWorld => transform.TransformDirection(
            Quaternion.Euler(0f, doorYaw, 0f) * Vector3.forward).normalized;
        public float FrontageWidth => frontageWidth;
        public ShopEntrance Entrance => entrance != null
            ? entrance : GetComponentInChildren<ShopEntrance>(true);
        public int LeafCount => leaves.Count;
        public int PaneCount => panesRoot != null ? panesRoot.childCount : 0;

        /// <summary>The explicit five-by-five (or merged ten-by-five) binding piece.</summary>
        public Bounds BindingBounds
        {
            get
            {
                var bounds = new Bounds(
                    transform.TransformPoint(bindingCentreLocal), Vector3.zero);
                Vector3 half = bindingSizeLocal * 0.5f;
                for (int mask = 0; mask < 8; mask++)
                    bounds.Encapsulate(transform.TransformPoint(new Vector3(
                        bindingCentreLocal.x + ((mask & 1) == 0 ? -half.x : half.x),
                        bindingCentreLocal.y + ((mask & 2) == 0 ? -half.y : half.y),
                        bindingCentreLocal.z + ((mask & 4) == 0 ? -half.z : half.z))));
                return bounds;
            }
        }

        internal void Configure(
            string moduleName, Vector3 door, float yaw, Rect footprint,
            ResidentialStorefrontOpening[] measured, MeshFilter[] walls,
            MeshRenderer[] authoredGlass, Material glassMaterial,
            Material shutterMaterial, Material wallMaterial)
        {
            ClearRuntimeMeshes();
            module = moduleName ?? string.Empty;
            doorLocal = door;
            doorYaw = yaw;
            bindingCentreLocal = new Vector3(footprint.center.x, 1.5f, footprint.center.y);
            bindingSizeLocal = new Vector3(
                Mathf.Max(1f, footprint.width), 3f, Mathf.Max(1f, footprint.height));
            frontageWidth = Mathf.Max(2f,
                (Mathf.Abs(OutwardLocal().x) > Mathf.Abs(OutwardLocal().z)
                    ? footprint.height : footprint.width) - ShopDoors.FrontageMargin * 2f);
            openings = measured ?? Array.Empty<ResidentialStorefrontOpening>();
            sourceWalls = walls ?? Array.Empty<MeshFilter>();
            sourceGlass = authoredGlass ?? Array.Empty<MeshRenderer>();
            paneMaterial = glassMaterial;
            facadeMaterial = wallMaterial;
            rollerMaterial = shutterMaterial;
            doorlessWalls = new Mesh[sourceWalls.Length];
            for (int i = 0; i < sourceWalls.Length; i++)
            {
                var source = sourceWalls[i];
                string sourceName = SourceModuleName(source);
                doorlessWalls[i] = DemoAssetLoad.Load<Mesh>(
                    AssetRoot + sourceName + "_Doorless.asset");
            }

            ReapplySourceOverrides();
            RebuildPanes(glassMaterial);
            RebuildLeaves(glassMaterial, wallMaterial);
            RebuildBoards(wallMaterial);
            RebuildShutter(shutterMaterial);
            EnsureDoorMarkers();
            ApplyState();
        }

        /// <summary>Authoring-bench entry point; production instances use measured bays.</summary>
        public void ConfigurePreview(Material shutterMaterial)
        {
            string source = StorefrontDoorCatalog.Normalise(gameObject.name);
            if (!StorefrontDoorCatalog.TryGet(source, out var profile)) return;
            var filters = GetComponentsInChildren<MeshFilter>(true);
            var walls = new List<MeshFilter>(1);
            var glass = new List<MeshRenderer>(1);
            var measured = new List<ResidentialStorefrontOpening>(3);
            Material wallMaterial = null;
            Material glassMaterial = null;
            Bounds bounds = default;
            bool haveBounds = false;
            for (int i = 0; i < filters.Length; i++)
            {
                var filter = filters[i];
                if (filter == null || filter.sharedMesh == null || filter.transform == transform &&
                    filter.sharedMesh.name.IndexOf("Doorless", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (filter.sharedMesh.name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        glass.Add(renderer);
                        if (renderer.sharedMaterial != null) glassMaterial = renderer.sharedMaterial;
                        ResidentialBlocks.MeasureStorefrontOpenings(
                            transform, filter.transform, filter.sharedMesh,
                            glass.Count - 1, measured);
                    }
                    continue;
                }
                if (!string.Equals(StorefrontDoorCatalog.Normalise(filter.sharedMesh.name),
                        source, StringComparison.OrdinalIgnoreCase)) continue;
                walls.Add(filter);
                var wallRenderer = filter.GetComponent<MeshRenderer>();
                if (wallRenderer != null && wallRenderer.sharedMaterial != null)
                    wallMaterial = wallRenderer.sharedMaterial;
                var candidate = filter.sharedMesh.bounds;
                if (!haveBounds) { bounds = candidate; haveBounds = true; }
                else bounds.Encapsulate(candidate);
            }
            if (walls.Count == 0) return;
            glassMaterial ??= DemoAssetLoad.Load<Material>(GlassMaterialPath);
            wallMaterial ??= DemoAssetLoad.Load<Material>(WallMaterialPath);
            var footprint = haveBounds
                ? Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z)
                : new Rect(-2.5f, -2.5f, 5f, 5f);
            Configure(source, profile.Centre, profile.Yaw, footprint,
                measured.ToArray(), walls.ToArray(), glass.ToArray(),
                glassMaterial, shutterMaterial, wallMaterial);
        }

        public void SetPreviewState(StorefrontState state)
        {
            authoringPreview = true;
            authoringState = state;
            ApplyPreviewState(state);
        }

        void ApplyPreviewState(StorefrontState state)
        {
            damageState = StorefrontState.Intact;
            shuttered = false;
            doorAmount = doorTarget = 0f;
            if (damageVisual == null)
                for (int i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);
                    if (child.name.StartsWith("Broken glass", StringComparison.Ordinal) ||
                        child.name.StartsWith("Burning", StringComparison.Ordinal))
                    {
                        damageVisual = child;
                        break;
                    }
                }
            ClearDamageVisual();
            switch (state)
            {
                case StorefrontState.Open:
                    doorAmount = doorTarget = 1f;
                    break;
                case StorefrontState.Smashed:
                    Smash();
                    break;
                case StorefrontState.Burning:
                    Scorch();
                    break;
                case StorefrontState.Boarded:
                    BoardUp();
                    break;
                case StorefrontState.Shuttered:
                    Shutter(true);
                    break;
            }
            ApplyLeaves(doorAmount);
            ApplyState();
        }

        public void BindBusiness(TerritoryBusinessId id)
        {
            businessId = id;
            RefreshPersistentState();
            RefreshShutter();
        }

        public void Open()
        {
            if (damageState == StorefrontState.Intact)
                doorTarget = 1f;
        }

        public void Close() => doorTarget = 0f;

        public bool Smash()
        {
            if (damageState != StorefrontState.Intact) return false;
            Close();
            damageState = StorefrontState.Smashed;
            ClearDamageVisual();
            string label = businessId.IsValid ? businessId.Value : module;
            damageVisual = openings.Length > 0
                ? SmashMeasuredPanes(label)
                : ShopDamage.SmashAt(
                    DoorWorld, OutwardWorld, label, DoorWorld.y, frontageWidth);
            AdoptDamageVisual();
            ApplyState();
            return true;
        }

        Transform SmashMeasuredPanes(string label)
        {
            var root = new GameObject("Broken glass · " +
                (string.IsNullOrEmpty(label) ? "premises" : label)).transform;
            root.SetParent(transform, false);
            int paneNumber = 0;
            var rects = new List<Vector4>(2);
            for (int i = 0; i < openings.Length; i++)
            {
                DescribePaneRuns(openings[i], rects,
                    out var right, out var outward, out float front);
                float widthScale = transform.TransformVector(right).magnitude;
                float heightScale = transform.TransformVector(Vector3.up).magnitude;
                Vector3 worldOutward = transform.TransformDirection(outward);
                worldOutward.y = 0f;
                if (worldOutward.sqrMagnitude < 0.001f)
                    worldOutward = OutwardWorld;
                else
                    worldOutward.Normalize();

                for (int n = 0; n < rects.Count; n++)
                {
                    var run = rects[n];
                    float centre = (run.x + run.y) * 0.5f;
                    // PaneMesh begins 4 cm above the measured sill. Anchor the broken
                    // replacement to that same sill so both the frame fragments and the
                    // pavement shards remain on the authored facade plane.
                    float floor = run.z - 0.04f;
                    Vector3 localAt = right * centre + outward * front +
                                      Vector3.up * floor;
                    Vector3 worldAt = transform.TransformPoint(localAt);
                    var broken = ShopDamage.SmashPaneAt(
                        worldAt, worldOutward,
                        label + " pane " + (++paneNumber), worldAt.y,
                        Mathf.Max(0.45f, (run.y - run.x) * widthScale),
                        0.04f * heightScale,
                        Mathf.Max(0.39f, (run.w - floor) * heightScale),
                        paneMaterial);
                    broken.SetParent(root, true);
                }
            }
            return root;
        }

        public bool Scorch()
        {
            if (damageState == StorefrontState.Burning ||
                damageState == StorefrontState.Boarded) return false;
            Close();
            damageState = StorefrontState.Burning;
            burningUntil = Time.time + ShopDamage.BurnFor;
            ClearDamageVisual();
            damageVisual = ShopDamage.ScorchAt(
                DoorWorld, OutwardWorld, businessId.Value, DoorWorld.y,
                frontageWidth, boardWhenDone: false);
            AdoptDamageVisual();
            ApplyState();
            return true;
        }

        public void BoardUp()
        {
            damageState = StorefrontState.Boarded;
            ClearDamageVisual();
            ApplyState();
        }

        public void Repair()
        {
            damageState = StorefrontState.Intact;
            burningUntil = 0f;
            ClearDamageVisual();
            ApplyState();
            RefreshShutter();
        }

        public void Shutter(bool on)
        {
            shuttered = on;
            ApplyState();
        }

        public void SnapClosed()
        {
            doorAmount = doorTarget = 0f;
            ApplyLeaves(0f);
        }

        public bool IsOpen => leaves.Count == 0 || doorAmount >= 0.999f;
        public bool IsClosed => leaves.Count == 0 || doorAmount <= 0.001f;

        void OnEnable()
        {
            ReapplySourceOverrides();
            bool needsRebuild = panesRoot == null ||
                panesRoot.GetComponentInChildren<MeshFilter>(true)?.sharedMesh == null;
            if (needsRebuild && !string.IsNullOrEmpty(module) && sourceWalls.Length > 0)
            {
                ClearRuntimeMeshes();
                RebuildPanes(paneMaterial);
                RebuildLeaves(paneMaterial, facadeMaterial);
                RebuildBoards(facadeMaterial);
                RebuildShutter(rollerMaterial);
                EnsureDoorMarkers();
            }
            if (!Application.isPlaying && authoringPreview)
                ApplyPreviewState(authoringState);
            else
                ApplyState();
        }

        void Update()
        {
            if (!Mathf.Approximately(doorAmount, doorTarget))
            {
                doorAmount = Mathf.MoveTowards(doorAmount, doorTarget,
                    Mathf.Max(0f, Time.deltaTime) / DoorSeconds);
                ApplyLeaves(Mathf.SmoothStep(0f, 1f, doorAmount));
            }
            if (damageState == StorefrontState.Burning &&
                burningUntil > 0f && Time.time >= burningUntil)
                BoardUp();

            if (!Application.isPlaying || !businessId.IsValid || Time.time < nextStatePoll)
                return;
            nextStatePoll = Time.time + 0.5f;
            RefreshShutter();
        }

        void RefreshPersistentState()
        {
            if (!businessId.IsValid) return;
            if (ShopDamage.IsBusinessBurned(businessId)) BoardUp();
            else if (ShopDamage.IsBusinessSmashed(businessId)) Smash();
            else if (!ShopDamage.IsBusinessDamaged(businessId)) Repair();
        }

        void RefreshShutter()
        {
            var business = LivingCity.Business.BusinessRuntime.Instance;
            bool closed = business != null && business.ShouldShutter(businessId);
            Shutter(closed);
        }

        void ReapplySourceOverrides()
        {
            for (int i = 0; i < sourceGlass.Length; i++)
                if (sourceGlass[i] != null) sourceGlass[i].enabled = false;
            for (int i = 0; i < sourceWalls.Length; i++)
                if (sourceWalls[i] != null && i < doorlessWalls.Length &&
                    doorlessWalls[i] != null)
                    sourceWalls[i].sharedMesh = doorlessWalls[i];
        }

        void EnsureDoorMarkers()
        {
            var marker = transform.Find("Doorway");
            if (marker == null)
            {
                marker = new GameObject("Doorway").transform;
                marker.SetParent(transform, false);
            }
            marker.localPosition = doorLocal;
            marker.localRotation = Quaternion.Euler(0f, doorYaw, 0f);
            entrance = marker.GetComponent<ShopEntrance>();
            if (entrance == null) entrance = marker.gameObject.AddComponent<ShopEntrance>();
            entrance.SetDoor(Vector3.zero);
            entrance.SetFrontage(frontageWidth);
            buildingDoor = marker.GetComponent<BuildingDoor>();
            if (buildingDoor == null) buildingDoor = marker.gameObject.AddComponent<BuildingDoor>();
            buildingDoor.SetDoor(Vector3.zero);
        }

        void RebuildPanes(Material glassMaterial)
        {
            EnsureRoot(ref panesRoot, "Panes");
            ClearChildren(panesRoot);
            for (int i = 0; i < openings.Length; i++)
            {
                var opening = openings[i];
                var pane = new GameObject("Pane " + (i + 1));
                pane.transform.SetParent(panesRoot, false);
                pane.AddComponent<StorefrontLive>();
                var filter = pane.AddComponent<MeshFilter>();
                var renderer = pane.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = glassMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                filter.sharedMesh = PaneMesh(opening);
            }
        }

        Mesh PaneMesh(ResidentialStorefrontOpening opening)
        {
            var rects = new List<Vector4>(2);
            DescribePaneRuns(opening, rects,
                out var right, out var outward, out float front);
            var mesh = QuadRuns("Storefront pane", rects, right, outward, front);
            runtimeMeshes.Add(mesh);
            return mesh;
        }

        void DescribePaneRuns(ResidentialStorefrontOpening opening,
                              List<Vector4> rects,
                              out Vector3 right, out Vector3 outward,
                              out float front)
        {
            rects.Clear();
            outward = Flat(opening.Outward, OutwardLocal());
            right = Flat(opening.Right, Vector3.Cross(Vector3.up, outward));
            float low = opening.Front.y + 0.04f;
            float high = low + Mathf.Max(1f, opening.Height - 0.08f);
            float half = Mathf.Max(0.3f, opening.Width * 0.5f - 0.04f);
            float centre = Vector3.Dot(opening.Front, right);
            float left = centre - half;
            float rightEdge = centre + half;
            front = Vector3.Dot(opening.Front, outward) + 0.006f;

            Vector3 doorOut = OutwardLocal();
            float doorCentre = Vector3.Dot(doorLocal, right);
            float doorHalf = 0f;
            if (Vector3.Dot(outward, doorOut) > 0.94f &&
                StorefrontDoorCatalog.TryGet(module, out var profile))
                doorHalf = profile.Width * 0.5f;
            if (doorHalf <= 0f || doorCentre + doorHalf <= left ||
                doorCentre - doorHalf >= rightEdge)
                rects.Add(new Vector4(left, rightEdge, low, high));
            else
            {
                if (doorCentre - doorHalf - left > 0.12f)
                    rects.Add(new Vector4(left, doorCentre - doorHalf, low, high));
                if (rightEdge - (doorCentre + doorHalf) > 0.12f)
                    rects.Add(new Vector4(doorCentre + doorHalf, rightEdge, low, high));
            }
        }

        void RebuildLeaves(Material glassMaterial, Material wallMaterial)
        {
            leaves.Clear();
            leafClosed.Clear();
            var old = transform.Find("Leaves");
            if (old != null) ClearChildren(old);
            else
            {
                old = new GameObject("Leaves").transform;
                old.SetParent(transform, false);
            }
            if (!StorefrontDoorCatalog.TryGet(module, out var profile) ||
                profile.Leaves == 0) return;

            float moduleYaw = doorYaw - profile.Yaw;
            Vector3 doorRight = Quaternion.Euler(0f, doorYaw, 0f) * Vector3.right;
            for (int i = 0; i < profile.Leaves; i++)
            {
                bool left = i == 0;
                string suffix = left ? "_Leaf_L.asset" : "_Leaf_R.asset";
                var mesh = DemoAssetLoad.Load<Mesh>(AssetRoot + module + suffix);
                if (mesh == null) continue;
                float hinge = profile.Leaves == 1 ? -profile.Width * 0.5f
                    : (left ? profile.Width * 0.5f : -profile.Width * 0.5f);
                var go = new GameObject(module + (left ? "_Door_L" : "_Door_R"));
                go.transform.SetParent(old, false);
                go.transform.localPosition = doorLocal + doorRight * hinge;
                go.transform.localRotation = Quaternion.Euler(0f, moduleYaw, 0f);
                go.AddComponent<StorefrontLive>();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = mesh.subMeshCount > 1
                    ? new[] { wallMaterial, glassMaterial }
                    : new[] { wallMaterial };
                leaves.Add(go.transform);
                leafClosed.Add(go.transform.localRotation);
            }
            ApplyLeaves(doorAmount);
        }

        void RebuildBoards(Material material)
        {
            boards = ChildObject("Boards");
            var filter = Component<MeshFilter>(boards);
            var renderer = Component<MeshRenderer>(boards);
            if (boards.GetComponent<StorefrontLive>() == null)
                boards.AddComponent<StorefrontLive>();
            renderer.sharedMaterial = ShopDamage.StorefrontBoardMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.On;

            var rects = new List<Vector4>(6);
            float half = frontageWidth * 0.5f;
            for (int i = 0; i < 6; i++)
            {
                float y = 0.28f + i * 0.44f;
                rects.Add(new Vector4(-half, half, y, y + 0.26f));
            }
            filter.sharedMesh = LocalFacadeMesh("Storefront boards", rects, 0.10f);
        }

        void RebuildShutter(Material material)
        {
            shutter = ChildObject("Shutter");
            var filter = Component<MeshFilter>(shutter);
            var renderer = Component<MeshRenderer>(shutter);
            if (shutter.GetComponent<StorefrontLive>() == null)
                shutter.AddComponent<StorefrontLive>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var rects = new List<Vector4>(12);
            float half = frontageWidth * 0.5f;
            for (int i = 0; i < 12; i++)
            {
                float y0 = 0.08f + i * 0.225f;
                rects.Add(new Vector4(-half, half, y0, y0 + 0.205f));
            }
            filter.sharedMesh = LocalFacadeMesh("Storefront shutter", rects, 0.04f);
        }

        Mesh LocalFacadeMesh(string name, List<Vector4> rects, float outset)
        {
            Vector3 outward = OutwardLocal();
            Vector3 right = Vector3.Cross(Vector3.up, outward).normalized;
            float front = Vector3.Dot(doorLocal + outward * outset, outward);
            var mesh = QuadRuns(name, rects, right, outward, front,
                Vector3.Dot(doorLocal, right));
            runtimeMeshes.Add(mesh);
            return mesh;
        }

        static Mesh QuadRuns(string name, List<Vector4> rects, Vector3 right,
                             Vector3 outward, float front, float offset = 0f)
        {
            var vertices = new List<Vector3>(rects.Count * 4);
            var normals = new List<Vector3>(rects.Count * 4);
            var uv = new List<Vector2>(rects.Count * 4);
            var triangles = new List<int>(rects.Count * 12);
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                float x0 = r.x + offset, x1 = r.y + offset;
                int first = vertices.Count;
                vertices.Add(right * x0 + outward * front + Vector3.up * r.z);
                vertices.Add(right * x1 + outward * front + Vector3.up * r.z);
                vertices.Add(right * x1 + outward * front + Vector3.up * r.w);
                vertices.Add(right * x0 + outward * front + Vector3.up * r.w);
                for (int n = 0; n < 4; n++) normals.Add(outward);
                uv.Add(Vector2.zero); uv.Add(Vector2.right);
                uv.Add(Vector2.one); uv.Add(Vector2.up);
                triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
                triangles.Add(first + 2); triangles.Add(first + 1); triangles.Add(first);
                triangles.Add(first + 3); triangles.Add(first + 2); triangles.Add(first);
            }
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        void ApplyState()
        {
            bool glassOn = damageState == StorefrontState.Intact;
            if (panesRoot != null) panesRoot.gameObject.SetActive(glassOn);
            if (boards != null) boards.SetActive(damageState == StorefrontState.Boarded);
            if (shutter != null) shutter.SetActive(
                shuttered && damageState == StorefrontState.Intact);
        }

        void ApplyLeaves(float amount)
        {
            for (int i = 0; i < leaves.Count; i++)
            {
                if (leaves[i] == null) continue;
                float side = leaves[i].name.EndsWith("_Door_L", StringComparison.Ordinal)
                    ? 1f : -1f;
                leaves[i].localRotation = leafClosed[i] *
                    Quaternion.Euler(0f, side * DoorDegrees * amount, 0f);
            }
        }

        void AdoptDamageVisual()
        {
            if (damageVisual == null) return;
            damageVisual.SetParent(transform, true);
            foreach (var renderer in damageVisual.GetComponentsInChildren<Renderer>(true))
                if (renderer.GetComponent<StorefrontLive>() == null)
                    renderer.gameObject.AddComponent<StorefrontLive>();
        }

        void ClearDamageVisual()
        {
            if (damageVisual == null) return;
            if (Application.isPlaying) Destroy(damageVisual.gameObject);
            else DestroyImmediate(damageVisual.gameObject);
            damageVisual = null;
        }

        Vector3 OutwardLocal() =>
            Quaternion.Euler(0f, doorYaw, 0f) * Vector3.forward;

        static Vector3 Flat(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            if (value.sqrMagnitude < 0.01f) value = fallback;
            return value.normalized;
        }

        static string SourceModuleName(MeshFilter filter)
        {
            if (filter == null || filter.sharedMesh == null) return string.Empty;
            string name = filter.sharedMesh.name;
            int doorless = name.IndexOf("_Doorless", StringComparison.OrdinalIgnoreCase);
            return doorless >= 0 ? name.Substring(0, doorless) :
                StorefrontDoorCatalog.Normalise(name);
        }

        GameObject ChildObject(string childName)
        {
            var child = transform.Find(childName);
            if (child != null) return child.gameObject;
            var created = new GameObject(childName);
            created.transform.SetParent(transform, false);
            return created;
        }

        static T Component<T>(GameObject go) where T : Component
        {
            var found = go.GetComponent<T>();
            return found != null ? found : go.AddComponent<T>();
        }

        void EnsureRoot(ref Transform root, string childName)
        {
            if (root != null) return;
            root = transform.Find(childName);
            if (root != null) return;
            root = new GameObject(childName).transform;
            root.SetParent(transform, false);
        }

        static void ClearChildren(Transform root)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        void OnDestroy()
        {
            ClearDamageVisual();
            ClearRuntimeMeshes();
        }

        void ClearRuntimeMeshes()
        {
            for (int i = 0; i < runtimeMeshes.Count; i++)
            {
                if (runtimeMeshes[i] == null) continue;
                if (Application.isPlaying) Destroy(runtimeMeshes[i]);
                else DestroyImmediate(runtimeMeshes[i]);
            }
            runtimeMeshes.Clear();
        }
    }
}
