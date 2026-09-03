using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What a bomb does to a shopfront. A grenade on the doorstep sets the ground floor
    /// alight - flames licking up the storefront and a glow on the street for the best
    /// part of half a minute - and when the fire has burnt itself out the premises are
    /// boarded up: planks nailed across the ground-floor windows, the way a gutted shop
    /// stands while somebody decides whether to reopen it.
    ///
    /// Generated residential bays route first to their bound Storefront, which owns only
    /// that bay's panes, fire and measured-width boards and never holds a merged chunk open.
    /// Catalogue buildings and named kit venues have no Storefront component and deliberately
    /// remain on the legacy mesh-derived path below. A shop is only ever done once: a second
    /// charge on an already boarded front does nothing new.
    /// </summary>
    public static class ShopDamage
    {
        /// <summary>How near a blast must fall to a shop's door to set it alight.</summary>
        public const float ScorchRange = 8f;

        /// <summary>Seconds the front burns before it is boarded up.</summary>
        public const float BurnFor = 22f;

        const float StoreWidth = 7f;    // metres of frontage the boards cover
        const float StoreHeight = 2.9f; // the ground floor
        /// <summary>How far off the door the boards and the fire stand: TEN CENTIMETRES
        /// toward the street. The old resolution put them at the job's approach point,
        /// which is a spot on the PAVEMENT and often out in the road.</summary>
        const float BoardOutset = 0.1f;

        static Transform _root;
        static Material _fire, _board, _smoke, _brokenGlass, _brokenEdge;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            _root = null;
            _fire = _board = _smoke = _brokenGlass = _brokenEdge = null;
        }

        static Transform Root()
        {
            if (_root == null)
            {
                var root = new GameObject("Shop Damage");
                root.AddComponent<ShopDamageLifecycle>();
                _root = root.transform;
            }
            return _root;
        }

        /// <summary>Any shopfront caught in a blast at <paramref name="at"/> is set alight.
        /// Called from Explosion.Blow, so a grenade thrown at a door, or a car blown up
        /// beside one, both scorch the shop behind it.</summary>
        public static void ScorchNear(Vector3 at, float groundY)
        {
            var all = GangFront.All;
            float r2 = ScorchRange * ScorchRange;
            for (int i = 0; i < all.Count; i++)
            {
                var f = all[i];
                if (f == null || f.Damaged) continue;
                if ((f.Door - at).sqrMagnitude <= r2) Scorch(f, groundY);
            }
        }

        /// <summary>Set this shop alight and, once it has burnt, board it up. Does nothing
        /// to a shop already done.</summary>
        public static void Scorch(GangFront front, float groundY)
        {
            if (front == null || front.Damaged) return;
            front.Damaged = true;
            front.Boarded = false;

            var go = new GameObject("Burning · " + front.GangName);
            go.transform.SetParent(Root(), false);
            var fire = go.AddComponent<ShopFire>();
            fire.Begin(front, groundY, FireMaterial(), SmokeMaterial(), BoardMaterial());

            if (DriveTrace.On)
                DriveTrace.Event("bomb", "shop", front.GangName + "'s front set alight");
        }

        // -------------------------------------------------- ordinary premises (EPIC 9)

        /// <summary>Businesses already wrecked, by canonical id - the once-only rule the
        /// GangFront flags carry, for premises that have no GangFront. Simulation-keyed,
        /// so a street streamed out and back is still a wreck.</summary>
        static readonly HashSet<string> DamagedBusinesses = new HashSet<string>();
        static readonly HashSet<string> SmashedBusinesses = new HashSet<string>();
        static readonly HashSet<string> BurnedBusinesses = new HashSet<string>();
        static readonly HashSet<string> DeferredSmashedViews = new HashSet<string>();
        static readonly Dictionary<string, Transform> SmashedVisuals =
            new Dictionary<string, Transform>();
        static readonly Dictionary<string, List<ShopGlassSurface>> SmashedSurfaces =
            new Dictionary<string, List<ShopGlassSurface>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDamagedForPlay()
        {
            DamagedBusinesses.Clear();
            SmashedBusinesses.Clear();
            BurnedBusinesses.Clear();
            DeferredSmashedViews.Clear();
            SmashedVisuals.Clear();
            SmashedSurfaces.Clear();
        }

        public static bool IsBusinessDamaged(LivingCity.Territory.TerritoryBusinessId id) =>
            id.IsValid && DamagedBusinesses.Contains(id.Value);

        public static bool IsBusinessSmashed(LivingCity.Territory.TerritoryBusinessId id) =>
            id.IsValid && SmashedBusinesses.Contains(id.Value);

        public static bool IsBusinessBurned(LivingCity.Territory.TerritoryBusinessId id) =>
            id.IsValid && BurnedBusinesses.Contains(id.Value);

        /// <summary>Remove the finite damage presentation when the authoritative business
        /// reopens, either because its timer expired or its owner paid for repairs.</summary>
        public static bool RepairBusiness(LivingCity.Territory.TerritoryBusinessId id)
        {
            if (!id.IsValid)
                return false;

            var known = DamagedBusinesses.Remove(id.Value);
            DeferredSmashedViews.Remove(id.Value);
            known |= RestoreOriginalGlass(id.Value);
            known |= SmashedBusinesses.Remove(id.Value);
            known |= BurnedBusinesses.Remove(id.Value);
            SmashedVisuals.Remove(id.Value);
            if (TryStorefront(id, out var repairedStorefront))
            {
                repairedStorefront.Repair();
                known = true;
            }
            if (_root == null)
                return known;

            for (var i = _root.childCount - 1; i >= 0; i--)
            {
                var child = _root.GetChild(i);
                if (child == null ||
                    !child.name.EndsWith(id.Value, System.StringComparison.Ordinal))
                    continue;
                if (Application.isPlaying)
                    Object.Destroy(child.gameObject);
                else
                    Object.DestroyImmediate(child.gameObject);
                known = true;
            }
            return known;
        }

        /// <summary>A Torch that came off at an ORDINARY shop: the same fire and the
        /// same boards the rival fronts get, at the business's own door. False when the
        /// door cannot be resolved or the place is already a wreck.</summary>
        public static bool ScorchBusiness(LivingCity.Territory.TerritoryBusinessId id)
        {
            if (BurnedBusinesses.Contains(id.Value))
                return false;


            if (TryStorefront(id, out var liveStorefront))
            {
                SmashedBusinesses.Remove(id.Value);
                DeferredSmashedViews.Remove(id.Value);
                DamagedBusinesses.Add(id.Value);
                BurnedBusinesses.Add(id.Value);
                return liveStorefront.Scorch();
            }

            if (!TryFrontage(id, out var door, out var outward, out var width))
                return false;

            // Arson is the stronger presentation. If this shop was smashed first, replace
            // the broken-glass frontage with fire (and eventually boards) instead of
            // letting the generic once-damaged guard swallow the later attack.
            if (SmashedBusinesses.Remove(id.Value))
            {
                DeferredSmashedViews.Remove(id.Value);
                if (SmashedVisuals.TryGetValue(id.Value, out var smashed) && smashed != null)
                {
                    if (Application.isPlaying)
                        Object.Destroy(smashed.gameObject);
                    else
                        Object.DestroyImmediate(smashed.gameObject);
                }
                SmashedVisuals.Remove(id.Value);
            }

            DamagedBusinesses.Add(id.Value);
            BurnedBusinesses.Add(id.Value);
            ScorchAt(door, outward, id.Value, door.y, width);
            return true;
        }

        /// <summary>A SmashUp that came off: no fire and no tidy repair - the panes are
        /// punched out and their glass is left across the pavement. Once per premises.
        /// Torched shops still burn and board themselves up through <see cref="ScorchBusiness"/>.</summary>
        public static bool SmashBusiness(LivingCity.Territory.TerritoryBusinessId id)
        {
            if (!id.IsValid || !DamagedBusinesses.Add(id.Value))
                return false;
            SmashedBusinesses.Add(id.Value);
            if (TryStorefront(id, out var liveStorefront))
                return liveStorefront.Smash();
            if (!TryFrontage(id, out _, out _, out _))
            {
                DamagedBusinesses.Remove(id.Value);
                SmashedBusinesses.Remove(id.Value);
                return false;
            }
            Root();
            RefreshBusinessView(id);
            return true;
        }

        /// <summary>
        /// A smashed business may have received its state while its streamed view did not
        /// exist. Once the real building binds, remove its authored ground-floor glass and
        /// build the fragments directly from that glass's plane, bounds and material.
        /// Torch damage is not tracked here: its fire owns its transition to boards.
        /// </summary>
        internal static void RefreshBusinessView(
            LivingCity.Territory.TerritoryBusinessId id)
        {
            if (TryStorefront(id, out var liveStorefront))
            {
                liveStorefront.BindBusiness(id);
                DeferredSmashedViews.Remove(id.Value);
                return;
            }
            if (!id.IsValid || !SmashedBusinesses.Contains(id.Value))
            {
                if (id.IsValid) DeferredSmashedViews.Remove(id.Value);
                return;
            }
            if (ScenePerf.Merging)
            {
                // Never mutate a mesh while ScenePerf may be reading its submeshes. The
                // root's lifecycle retries after the final concurrent merge has ended.
                DeferredSmashedViews.Add(id.Value);
                Root();
                return;
            }

            DeferredSmashedViews.Remove(id.Value);
            if (!TryFrontage(id, out var door, out var outward, out var width) ||
                !TryReplaceOriginalGlass(
                    id.Value, door, outward, width, out var captured))
                return;

            if (SmashedVisuals.TryGetValue(id.Value, out var old) && old != null)
                Object.Destroy(old.gameObject);

            SmashedVisuals[id.Value] = ShatterAt(
                captured.Origin, captured.Outward, id.Value, captured.GroundY,
                captured.Width, captured.Bottom, captured.Top, captured.Material,
                exactGlassPlane: true);
        }

        /// <summary>Retry smash views that arrived while ScenePerf was reading source
        /// meshes. The set is normally empty, so the damage root's per-frame cost is one
        /// count and one merge flag.</summary>
        internal static void PumpDeferredViews()
        {
            if (ScenePerf.Merging || DeferredSmashedViews.Count == 0)
                return;

            var retry = new string[DeferredSmashedViews.Count];
            DeferredSmashedViews.CopyTo(retry);
            DeferredSmashedViews.Clear();
            for (var i = 0; i < retry.Length; i++)
                RefreshBusinessView(
                    new LivingCity.Territory.TerritoryBusinessId(retry[i]));
        }

        /// <summary>A streamed block finished attaching/merging. Reapply every smashed
        /// site belonging to that plan, including extra residential shop runs that share
        /// a building and therefore cannot each own a BusinessMarker.</summary>
        internal static void RefreshPlanView(string planId)
        {
            if (string.IsNullOrEmpty(planId) || SmashedBusinesses.Count == 0)
                return;

            var business = LivingCity.Business.BusinessRuntime.Instance;
            if (business == null)
                return;

            foreach (var value in SmashedBusinesses)
            {
                var id = new LivingCity.Territory.TerritoryBusinessId(value);
                if (business.TryGetSite(id, out var site) && site != null &&
                    string.Equals(site.SourcePlanId, planId, System.StringComparison.Ordinal))
                    RefreshBusinessView(id);
            }
        }

        /// <summary>The ordinary-premises torch visual at already resolved frontage
        /// geometry. The business overload owns persistence; this geometry overload owns
        /// only the same shared fire presentation and returns it for finite-lived callers.</summary>
        public static Transform ScorchAt(
            Vector3 door, Vector3 outward, string label, float groundY,
            float width = StoreWidth, bool boardWhenDone = true)
        {
            var go = new GameObject("Burning · " + (label ?? "premises"));
            go.transform.SetParent(Root(), false);
            var fire = go.AddComponent<ShopFire>();
            fire.BeginAt(door, outward, label, groundY,
                FireMaterial(), SmokeMaterial(), BoardMaterial(), width, boardWhenDone);
            return go.transform;
        }

        static bool TryStorefront(
            LivingCity.Territory.TerritoryBusinessId id, out Storefront storefront)
        {
            storefront = null;
            if (!id.IsValid ||
                !LivingCity.Business.BusinessViewBindings.TryGet(id, out var marker) ||
                marker == null)
                return false;
            storefront = marker.GetComponent<Storefront>() ??
                         marker.GetComponentInParent<Storefront>();
            return storefront != null;
        }

        /// <summary>The ordinary-premises smash visual at already resolved frontage
        /// geometry: dark open panes, jagged glass left in the frames and loose shards
        /// scattered across the pavement.</summary>
        public static Transform SmashAt(
            Vector3 door, Vector3 outward, string label, float groundY,
            float width = StoreWidth) =>
            ShatterAt(door, outward, label, groundY, width);

        /// <summary>The doorstep and which way the front faces. A live facade is exact;
        /// the simulation site remains the stable fallback while its view is streamed out.</summary>
        static bool TryFrontage(
            LivingCity.Territory.TerritoryBusinessId id, out Vector3 door, out Vector3 outward) =>
            TryFrontage(id, out door, out outward, out _);

        /// <summary>No shopfront is narrower than this, and none is boarded wider.</summary>
        const float NarrowestFront = 3f;
        const float WidestFront = 14f;

        static bool TryFrontage(
            LivingCity.Territory.TerritoryBusinessId id, out Vector3 door, out Vector3 outward,
            out float width)
        {
            door = default;
            outward = Vector3.forward;
            width = StoreWidth;
            var runtime = TerritoryRuntime.Instance;
            if (runtime == null || !id.IsValid ||
                !runtime.TryGetBusinessApproach(id, out door))
                return false;

            // A gang HQ already carries the exact generated door and facade normal.
            // Prefer that authored street link over measuring the whole building again:
            // interiors and terraces can make a mesh heuristic choose the rear wall.
            if (TryGangFrontage(id, out door, out outward, out width))
                return true;

            if (TryLiveFrontage(id, out door, out outward, out width))
                return true;

            // HOW WIDE THIS SHOP IS, out of the simulation's own site rather than the
            // meshes standing there: one view can carry a whole terrace, and measuring it
            // gave an eighty-metre shopfront to a laundry.
            var business = LivingCity.Business.BusinessRuntime.Instance;
            LivingCity.Business.BusinessSite site = null;
            LivingCity.Territory.TerritoryBounds footprint = default;
            var hasSite = false;
            if (business != null && business.TryGetSite(id, out site) && site != null)
            {
                footprint = site.Footprint;
                hasSite = true;
            }

            // No streamed view: the simulation's own ground is still a stable fallback.
            // The doorstep lies off one of its edges; ShopDoors projects it back onto that
            // edge instead of leaving damage at the walkable point in the pavement/road.
            if (ShopDoors.TryStreetFront(id, out var front, out var facing, out var wide))
            {
                door = front;
                outward = facing;
                width = Mathf.Clamp(wide, NarrowestFront, WidestFront);
                return true;
            }

            if (hasSite)
            {
                var centre = new Vector3(
                    footprint.XMin + footprint.Width * 0.5f, door.y,
                    footprint.ZMin + footprint.Depth * 0.5f);
                var toDoor = door - centre;
                toDoor.y = 0f;
                if (toDoor.sqrMagnitude > 1e-4f)
                    outward = toDoor.normalized;
            }

            width = FrontageOf(hasSite, footprint, outward, 0f);
            return true;
        }

        static bool TryGangFrontage(
            LivingCity.Territory.TerritoryBusinessId id, out Vector3 door,
            out Vector3 outward, out float width)
        {
            door = default;
            outward = Vector3.forward;
            width = StoreWidth;

            var business = LivingCity.Business.BusinessRuntime.Instance;
            LivingCity.Business.BusinessSite site = null;
            var hasSite = business != null && business.TryGetSite(id, out site) && site != null;
            var fronts = GangFront.All;
            for (var i = 0; i < fronts.Count; i++)
            {
                var front = fronts[i];
                if (front == null ||
                    (front.BusinessId != id && (!hasSite || front.SiteId != site.SiteId)))
                    continue;

                door = front.Door;
                outward = front.Outward;
                outward.y = 0f;
                if (outward.sqrMagnitude < 1e-4f)
                    outward = Vector3.forward;
                else
                    outward.Normalize();

                if (hasSite)
                    width = FrontageOf(true, site.Footprint, outward, 0f);
                return true;
            }

            return false;
        }

        /// <summary>Resolve only a currently bound renderer, with no plan fallback. This
        /// distinction matters when a stale fallback visual is being corrected on stream-in.</summary>
        static bool TryLiveFrontage(
            LivingCity.Territory.TerritoryBusinessId id, out Vector3 door, out Vector3 outward,
            out float width)
        {
            door = default;
            outward = Vector3.forward;
            width = StoreWidth;

            var runtime = TerritoryRuntime.Instance;
            if (runtime == null || !id.IsValid ||
                !runtime.TryGetBusinessApproach(id, out var approach))
                return false;

            var entrance = ShopDoors.Of(id, out var measured);
            if (entrance == null)
                return false;

            outward = entrance.Facing;

            // The facade plane, AT THIS SHOP'S OWN PLACE ALONG IT: the view's door can be
            // the terrace's centre, while the approach point is this business's own spot
            // on the pavement. Slide along the wall to that spot.
            var plane = entrance.DoorWorld;
            var lateral = Vector3.Cross(Vector3.up, outward);
            door = plane + lateral * Vector3.Dot(approach - plane, lateral);
            door.y = plane.y;

            var business = LivingCity.Business.BusinessRuntime.Instance;
            LivingCity.Business.BusinessSite site = null;
            LivingCity.Territory.TerritoryBounds footprint = default;
            var hasSite = business != null && business.TryGetSite(id, out site) && site != null;
            if (hasSite)
                footprint = site.Footprint;

            // A residential apartment can publish several independent shop runs out of
            // one building. There the site's slice is narrower and authoritative; a
            // standalone cafe/store owns its renderer, so the measured facade wins.
            bool slicedResidential = site != null &&
                site.ProviderId == LivingCity.Business.BusinessProviders.Residential &&
                (site.Role == LivingCity.Business.ResidentialBusinessSites.FrontageRole ||
                 site.Role == LivingCity.Business.ResidentialBusinessSites.ExtraFrontageRole);
            width = FrontageOf(
                hasSite, footprint, outward, measured, preferMeasured: !slicedResidential);
            return true;
        }

        /// <summary>How wide to cut the boards: the shop's own ground first, whatever the
        /// meshes measured second, and never wider than a shopfront gets.</summary>
        static float FrontageOf(
            bool hasSite, LivingCity.Territory.TerritoryBounds footprint, Vector3 outward,
            float measured, bool preferMeasured = false)
        {
            var width = StoreWidth;
            if (preferMeasured && measured > 0.5f)
            {
                width = measured;
            }
            else if (hasSite)
            {
                var alongZ = Mathf.Abs(outward.z) > Mathf.Abs(outward.x);
                var own = alongZ ? footprint.Width : footprint.Depth;
                if (own > 0.5f)
                    width = own - ShopDoors.FrontageMargin * 2f;
            }
            else if (measured > 0.5f)
            {
                width = measured;
            }

            return Mathf.Clamp(width, NarrowestFront, WidestFront);
        }

        /// <summary>Resolve the same authoritative frontage used by business damage so a
        /// visible projectile can hit the actual facade rather than the job's approach
        /// point. The simulation ID remains the authority; this only exposes geometry.</summary>
        internal static bool TryBusinessFrontage(
            LivingCity.Territory.TerritoryBusinessId id,
            out Vector3 door,
            out Vector3 outward) => TryFrontage(id, out door, out outward);

        // --------------------------------------------------- authored glass replacement

        const float GlassStoreyTop = 3.45f;
        const float GlassPlaneSearch = 5.5f;
        const float GlassPlaneBand = 0.42f;

        readonly struct CapturedGlass
        {
            public readonly Vector3 Origin;
            public readonly Vector3 Outward;
            public readonly float GroundY;
            public readonly float Width;
            public readonly float Bottom;
            public readonly float Top;
            public readonly Material Material;

            public CapturedGlass(
                Vector3 origin, Vector3 outward, float groundY, float width,
                float bottom, float top, Material material)
            {
                Origin = origin;
                Outward = outward;
                GroundY = groundY;
                Width = width;
                Bottom = bottom;
                Top = top;
                Material = material;
            }
        }

        sealed class GlassTriangle
        {
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Material Material;
            public int Submesh;
            public int TriangleOffset;
            public float Depth;
            public Vector3 A, B, C;
        }

        sealed class GlassCutPlan
        {
            public ShopGlassSurface Surface;
            public readonly Dictionary<int, HashSet<int>> Removed =
                new Dictionary<int, HashSet<int>>();
        }

        /// <summary>
        /// Find the real ground-floor glass triangles at this frontage, remove those
        /// triangles from runtime copies of their source meshes, and describe their exact
        /// plane/size/material for the broken replacement. Upper-storey and neighbouring
        /// panes remain in their original submeshes.
        /// </summary>
        static bool TryReplaceOriginalGlass(
            string businessId, Vector3 door, Vector3 facingOut, float frontage,
            out CapturedGlass captured)
        {
            captured = default;
            if (string.IsNullOrEmpty(businessId))
                return false;

            var outward = facingOut;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f) outward = Vector3.forward;
            else outward.Normalize();
            var lateral = Vector3.Cross(Vector3.up, outward).normalized;
            var half = Mathf.Max(0.75f, frontage * 0.5f + 0.35f);
            var triangles = new List<GlassTriangle>(96);

            // Only a live hierarchy is eligible. A cached residential holder may still
            // sit at its old coordinates while inactive; selecting it would cut an
            // invisible pooled instance rather than the storefront standing in the city.
            var renderers = Object.FindObjectsByType<MeshRenderer>();
            for (var r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null ||
                    renderer.GetComponentInParent<ShopDamageMesh>() != null)
                    continue;

                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null)
                    continue;
                var surface = filter.GetComponent<ShopGlassSurface>();
                var mesh = surface != null ? surface.SourceMesh : filter.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                    continue;

                // A disabled source only matters when a ready merged chunk can stand it
                // back up. Other disabled renderers are LOD/authoring variants, not the
                // glass currently visible on this street.
                var chunk = MergedChunk.Of(renderer);
                if (!renderer.enabled && chunk == null)
                    continue;

                var bounds = renderer.bounds;
                if (bounds.max.y < door.y - 0.1f ||
                    bounds.min.y > door.y + GlassStoreyTop)
                    continue;
                var delta = bounds.center - door;
                var lateralRadius = ProjectedRadius(bounds.extents, lateral);
                if (Mathf.Abs(Vector3.Dot(delta, lateral)) - lateralRadius > half)
                    continue;
                var depthRadius = ProjectedRadius(bounds.extents, outward);
                if (Mathf.Abs(Vector3.Dot(delta, outward)) - depthRadius > GlassPlaneSearch)
                    continue;

                var materials = renderer.sharedMaterials;
                var vertices = mesh.vertices;
                var subs = Mathf.Min(mesh.subMeshCount, materials.Length);
                for (var sub = 0; sub < subs; sub++)
                {
                    var material = materials[sub];
                    if (!IsStoreGlass(material))
                        continue;

                    var indices = mesh.GetTriangles(sub);
                    for (var t = 0; t + 2 < indices.Length; t += 3)
                    {
                        var a = filter.transform.TransformPoint(vertices[indices[t]]);
                        var b = filter.transform.TransformPoint(vertices[indices[t + 1]]);
                        var c = filter.transform.TransformPoint(vertices[indices[t + 2]]);
                        var centre = (a + b + c) / 3f;
                        if (centre.y < door.y - 0.08f ||
                            centre.y > door.y + GlassStoreyTop ||
                            Mathf.Abs(Vector3.Dot(centre - door, lateral)) > half)
                            continue;

                        var normal = Vector3.Cross(b - a, c - a);
                        if (normal.sqrMagnitude < 1e-8f ||
                            Mathf.Abs(Vector3.Dot(normal.normalized, outward)) < 0.55f)
                            continue;

                        var depth = Vector3.Dot(centre - door, outward);
                        if (Mathf.Abs(depth) > GlassPlaneSearch)
                            continue;
                        triangles.Add(new GlassTriangle
                        {
                            Filter = filter,
                            Renderer = renderer,
                            Material = material,
                            Submesh = sub,
                            TriangleOffset = t,
                            Depth = depth,
                            A = a,
                            B = b,
                            C = c,
                        });
                    }
                }
            }

            if (triangles.Count == 0)
                return false;

            var nearest = triangles[0].Depth;
            for (var i = 1; i < triangles.Count; i++)
                if (Mathf.Abs(triangles[i].Depth) < Mathf.Abs(nearest))
                    nearest = triangles[i].Depth;

            var plans = new Dictionary<MeshFilter, GlassCutPlan>();
            var materialUse = new Dictionary<Material, int>();
            var minL = float.PositiveInfinity;
            var maxL = float.NegativeInfinity;
            var minY = float.PositiveInfinity;
            var maxY = float.NegativeInfinity;
            var planeTotal = 0f;
            var planePoints = 0;

            for (var i = 0; i < triangles.Count; i++)
            {
                var triangle = triangles[i];
                if (Mathf.Abs(triangle.Depth - nearest) > GlassPlaneBand)
                    continue;

                if (!plans.TryGetValue(triangle.Filter, out var plan))
                {
                    var surface = triangle.Filter.GetComponent<ShopGlassSurface>();
                    if (surface == null)
                        surface = triangle.Filter.gameObject.AddComponent<ShopGlassSurface>();
                    plan = new GlassCutPlan { Surface = surface };
                    plans.Add(triangle.Filter, plan);
                }
                if (!plan.Removed.TryGetValue(triangle.Submesh, out var removed))
                {
                    removed = new HashSet<int>();
                    plan.Removed.Add(triangle.Submesh, removed);
                }
                removed.Add(triangle.TriangleOffset);

                materialUse.TryGetValue(triangle.Material, out var uses);
                materialUse[triangle.Material] = uses + 1;
                Include(triangle.A);
                Include(triangle.B);
                Include(triangle.C);
            }

            if (plans.Count == 0 || !float.IsFinite(minL) || !float.IsFinite(maxL))
                return false;

            // Capture succeeded before mutating anything. Now retire the previous streamed
            // instance (if any) and apply the same business cut to this live one.
            RestoreOriginalGlass(businessId);
            var applied = new List<ShopGlassSurface>(plans.Count);
            foreach (var pair in plans)
            {
                if (!pair.Value.Surface.Apply(businessId, pair.Value.Removed))
                {
                    for (var i = 0; i < applied.Count; i++)
                        applied[i].Remove(businessId);
                    return false;
                }
                applied.Add(pair.Value.Surface);
            }
            SmashedSurfaces[businessId] = applied;

            Material glass = null;
            var most = -1;
            foreach (var pair in materialUse)
                if (pair.Key != null && pair.Value > most)
                {
                    glass = pair.Key;
                    most = pair.Value;
                }
            if (glass == null)
            {
                RestoreOriginalGlass(businessId);
                return false;
            }

            var centreL = (minL + maxL) * 0.5f;
            var plane = planePoints > 0 ? planeTotal / planePoints : nearest;
            var origin = new Vector3(door.x, door.y, door.z) +
                         lateral * centreL + outward * plane;
            var bottom = Mathf.Max(0.02f, minY - door.y);
            var top = Mathf.Max(bottom + 0.35f, maxY - door.y);
            captured = new CapturedGlass(
                origin, outward, door.y, Mathf.Max(0.45f, maxL - minL),
                bottom, top, glass);
            return true;

            void Include(Vector3 point)
            {
                var relative = point - door;
                var along = Vector3.Dot(relative, lateral);
                minL = Mathf.Min(minL, along);
                maxL = Mathf.Max(maxL, along);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
                planeTotal += Vector3.Dot(relative, outward);
                planePoints++;
            }
        }

        static float ProjectedRadius(Vector3 extents, Vector3 axis) =>
            Mathf.Abs(axis.x) * extents.x + Mathf.Abs(axis.y) * extents.y +
            Mathf.Abs(axis.z) * extents.z;

        static bool IsStoreGlass(Material material)
        {
            if (material == null)
                return false;
            var name = material.name;
            return name.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                   name.IndexOf("Vehicle", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                   name.IndexOf("Glasses", System.StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>ScenePerf keeps a reverse receipt for these source renderers so an
        /// individual smashed storefront can stand back out of a merged block.</summary>
        internal static bool HasStoreGlass(Renderer renderer)
        {
            if (renderer == null)
                return false;
            var materials = renderer.sharedMaterials;
            for (var i = 0; i < materials.Length; i++)
                if (IsStoreGlass(materials[i]))
                    return true;
            return false;
        }

        static bool RestoreOriginalGlass(string businessId)
        {
            if (string.IsNullOrEmpty(businessId) ||
                !SmashedSurfaces.TryGetValue(businessId, out var surfaces))
                return false;

            for (var i = 0; i < surfaces.Count; i++)
                if (surfaces[i] != null)
                    surfaces[i].Remove(businessId);
            SmashedSurfaces.Remove(businessId);
            return true;
        }

        // ------------------------------------------------------------------ materials

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        static Material FireMaterial()
        {
            if (_fire != null) return _fire;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _fire = new Material(shader);
            SetColor(_fire, new Color(1f, 0.55f, 0.12f, 1f));
            return _fire;
        }

        static Material SmokeMaterial()
        {
            if (_smoke != null) return _smoke;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _smoke = new Material(shader);
            SetColor(_smoke, new Color(0.12f, 0.12f, 0.12f, 0.7f));
            return _smoke;
        }

        static Material BoardMaterial()
        {
            if (_board != null) return _board;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _board = new Material(shader);
            SetColor(_board, new Color(0.36f, 0.24f, 0.13f));   // bare timber
            return _board;
        }

        internal static Material StorefrontBoardMaterial => BoardMaterial();

        static Material BrokenGlassMaterial()
        {
            if (_brokenGlass != null) return _brokenGlass;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _brokenGlass = new Material(shader)
            {
                name = "Broken storefront glass",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };
            SetColor(_brokenGlass, new Color(0.28f, 0.52f, 0.62f, 0.68f));
            SetFloat(_brokenGlass, "_Metallic", 0.08f);
            SetFloat(_brokenGlass, "_Smoothness", 0.82f);
            SetFloat(_brokenGlass, "_Glossiness", 0.82f);
            // Geometry supplies the missing chunks; transparency is only for the shards
            // that remain. Without configuring the surface this colour's alpha is ignored
            // by URP and the result reads as blue sheet metal rather than broken glass.
            SetFloat(_brokenGlass, "_Surface", 1f);
            SetFloat(_brokenGlass, "_Mode", 3f);
            SetFloat(_brokenGlass, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SetFloat(_brokenGlass, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetFloat(_brokenGlass, "_ZWrite", 0f);
            _brokenGlass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _brokenGlass.DisableKeyword("_ALPHATEST_ON");
            _brokenGlass.SetOverrideTag("RenderType", "Transparent");
            _brokenGlass.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return _brokenGlass;
        }

        static Material BrokenEdgeMaterial()
        {
            if (_brokenEdge != null) return _brokenEdge;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _brokenEdge = new Material(shader)
            {
                name = "Broken glass fracture edges",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };
            // A fracture catches a pale highlight even when the shop's own glass material
            // is nearly black at night. This draws only thin broken edges: the fragments
            // themselves still use the exact original material and colour.
            SetColor(_brokenEdge, new Color(0.62f, 0.82f, 0.88f, 1f));
            return _brokenEdge;
        }

        static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            else m.color = c;
        }

        // ----------------------------------------------------------- smashed storefront

        const float BrokenBottom = 0.14f;
        const float BrokenTop = 2.78f;
        const float BrokenOutset = BoardOutset + 0.012f;
        const float PavementGlassY = 0.075f;

        /// <summary>
        /// Stand a smashed shopfront on the captured plane of its original glass. The live
        /// business path first removes only this shop's ground-floor glass triangles from a
        /// runtime mesh clone; neighbouring shops and upper floors stay untouched. This
        /// replacement reuses that original material for the jagged frame and pavement
        /// shards. One combined mesh keeps the result to one renderer and no colliders.
        /// </summary>
        static Transform ShatterAt(
            Vector3 doorAt, Vector3 facingOut, string label, float groundY,
            float width = StoreWidth, float bottom = BrokenBottom, float top = BrokenTop,
            Material glassMaterial = null, bool exactGlassPlane = false)
        {
            float frontage = exactGlassPlane
                ? Mathf.Clamp(width, 0.45f, WidestFront)
                : Mathf.Clamp(
                    width > 0.5f ? width : StoreWidth, NarrowestFront, WidestFront);
            bottom = Mathf.Max(0.02f, bottom);
            top = Mathf.Max(bottom + 0.35f, top);
            Vector3 outward = facingOut.sqrMagnitude > 1e-4f
                ? facingOut.normalized
                : Vector3.forward;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f) outward = Vector3.forward;
            else outward.Normalize();

            var broken = new GameObject("Broken glass · " + (label ?? "premises"));
            broken.transform.SetParent(Root(), false);
            broken.transform.SetPositionAndRotation(
                new Vector3(doorAt.x, groundY, doorAt.z) +
                    outward * (exactGlassPlane ? 0f : BrokenOutset),
                Quaternion.LookRotation(outward, Vector3.up));

            var mesh = BuildBrokenGlassMesh(
                frontage, bottom, top, DamageSeed(label, doorAt, frontage));
            var filter = broken.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var drawn = broken.AddComponent<MeshRenderer>();
            drawn.sharedMaterials = new[]
            {
                glassMaterial != null ? glassMaterial : BrokenGlassMaterial(),
                BrokenEdgeMaterial(),
            };
            drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            drawn.receiveShadows = false;
            drawn.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            drawn.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            drawn.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            broken.AddComponent<ShopDamageMesh>().Own(mesh);
            return broken.transform;
        }

        static Mesh BuildBrokenGlassMesh(float frontage, float bottom, float top, uint seed)
        {
            var vertices = new List<Vector3>(420);
            var normals = new List<Vector3>(420);
            var uvs = new List<Vector2>(420);
            var edges = new List<int>(720);
            var glass = new List<int>(720);
            var dice = new DamageDice(seed);

            int panes = Mathf.Clamp(Mathf.CeilToInt(frontage / 2.35f), 1, 8);
            float pitch = frontage / panes;
            float inset = Mathf.Min(0.075f, pitch * 0.045f);
            float paneHeight = top - bottom;

            for (int pane = 0; pane < panes; pane++)
            {
                float x0 = -frontage * 0.5f + pane * pitch + inset;
                float x1 = -frontage * 0.5f + (pane + 1) * pitch - inset;
                float width = x1 - x0;

                float hx = (x0 + x1) * 0.5f + dice.Range(-width * 0.11f, width * 0.11f);
                float hy = bottom + paneHeight * dice.Range(0.43f, 0.60f);
                float rx = width * dice.Range(0.25f, 0.37f);
                float ry = paneHeight * dice.Range(0.23f, 0.35f);

                var outer = new[]
                {
                    new Vector2(x0, bottom),
                    new Vector2((x0 + x1) * 0.5f, bottom),
                    new Vector2(x1, bottom),
                    new Vector2(x1, (bottom + top) * 0.5f),
                    new Vector2(x1, top),
                    new Vector2((x0 + x1) * 0.5f, top),
                    new Vector2(x0, top),
                    new Vector2(x0, (bottom + top) * 0.5f),
                };
                var hole = new[]
                {
                    new Vector2(hx - rx * dice.Range(0.58f, 0.82f), hy - ry * dice.Range(0.58f, 0.82f)),
                    new Vector2(hx + dice.Range(-rx * 0.16f, rx * 0.16f), hy - ry),
                    new Vector2(hx + rx * dice.Range(0.58f, 0.82f), hy - ry * dice.Range(0.55f, 0.80f)),
                    new Vector2(hx + rx, hy + dice.Range(-ry * 0.16f, ry * 0.16f)),
                    new Vector2(hx + rx * dice.Range(0.58f, 0.82f), hy + ry * dice.Range(0.56f, 0.82f)),
                    new Vector2(hx + dice.Range(-rx * 0.16f, rx * 0.16f), hy + ry),
                    new Vector2(hx - rx * dice.Range(0.58f, 0.82f), hy + ry * dice.Range(0.56f, 0.82f)),
                    new Vector2(hx - rx, hy + dice.Range(-ry * 0.16f, ry * 0.16f)),
                };

                // Eight irregular sectors are the original-colour glass still clinging to
                // the frame. Skip another wedge so the silhouette is visibly broken too.
                int missing = dice.Int(8);
                for (int i = 0; i < 8; i++)
                {
                    if (i == missing) continue;
                    int next = (i + 1) & 7;
                    AddDamageQuad(
                        Face(outer[i], 0.014f), Face(outer[next], 0.014f),
                        Face(hole[next], 0.014f), Face(hole[i], 0.014f),
                        Vector3.forward, vertices, normals, uvs, glass);
                }

                // Pale fracture highlights outline the open hole and branch into the
                // surviving glass. They remain readable when night windows turn off.
                for (int i = 0; i < 8; i++)
                {
                    int next = (i + 1) & 7;
                    AddDamageCrack(
                        hole[i], hole[next], dice.Range(0.030f, 0.052f), 0.026f,
                        vertices, normals, uvs, edges);
                }
                for (int i = dice.Int(2); i < 8; i += 2)
                    AddDamageCrack(
                        hole[i], Vector2.Lerp(hole[i], outer[i], dice.Range(0.72f, 0.98f)),
                        dice.Range(0.024f, 0.044f), 0.028f,
                        vertices, normals, uvs, edges);
            }

            // More frontage means more glass, but cap it: these are readable triangles,
            // not physics debris, and every wreck remains a single cheap renderer.
            int shardCount = Mathf.Clamp(Mathf.CeilToInt(frontage * 2.7f), 12, 42);
            for (int i = 0; i < shardCount; i++)
            {
                float x = dice.Range(-frontage * 0.52f, frontage * 0.52f);
                float z = 0.18f + Mathf.Pow(dice.Unit(), 1.65f) * 2.25f;
                float size = dice.Range(0.055f, 0.19f);
                float angle = dice.Range(0f, Mathf.PI * 2f);
                Vector2 along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 across = new Vector2(-along.y, along.x);
                Vector3 a = new Vector3(
                    x + along.x * size, PavementGlassY, z + along.y * size);
                Vector3 b = new Vector3(
                    x - along.x * size * 0.72f + across.x * size * 0.48f,
                    PavementGlassY + dice.Range(0f, 0.018f),
                    z - along.y * size * 0.72f + across.y * size * 0.48f);
                Vector3 c = new Vector3(
                    x - along.x * size * 0.48f - across.x * size * 0.62f,
                    PavementGlassY,
                    z - along.y * size * 0.48f - across.y * size * 0.62f);
                AddDamageTriangle(a, b, c, Vector3.up,
                    vertices, normals, uvs, glass);
                AddPavementEdge(a, b, 0.014f, vertices, normals, uvs, edges);
                AddPavementEdge(b, c, 0.014f, vertices, normals, uvs, edges);
                AddPavementEdge(c, a, 0.014f, vertices, normals, uvs, edges);
            }

            var mesh = new Mesh
            {
                name = "Smashed storefront and pavement glass",
                hideFlags = HideFlags.HideAndDontSave,
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(glass, 0, true);
            mesh.SetTriangles(edges, 1, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Vector3 Face(Vector2 point, float z) => new Vector3(point.x, point.y, z);

        static void AddPavementEdge(
            Vector3 from, Vector3 to, float width,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
            List<int> triangles)
        {
            var along = to - from;
            along.y = 0f;
            if (along.sqrMagnitude < 0.0001f) return;
            var side = Vector3.Cross(Vector3.up, along).normalized * (width * 0.5f);
            from.y += 0.007f;
            to.y += 0.007f;
            AddDamageQuad(
                from - side, to - side, to + side, from + side, Vector3.up,
                vertices, normals, uvs, triangles);
        }

        static void AddDamageCrack(
            Vector2 from, Vector2 to, float width, float z,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
            List<int> triangles)
        {
            Vector2 along = to - from;
            if (along.sqrMagnitude < 0.001f) return;
            Vector2 side = new Vector2(-along.y, along.x).normalized * (width * 0.5f);
            AddDamageQuad(
                Face(from - side, z), Face(from + side, z),
                Face(to + side, z), Face(to - side, z), Vector3.forward,
                vertices, normals, uvs, triangles);
        }

        static void AddDamageQuad(
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
            List<int> triangles)
        {
            int first = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int i = 0; i < 4; i++) normals.Add(normal);
            uvs.Add(Vector2.zero); uvs.Add(Vector2.right);
            uvs.Add(Vector2.one); uvs.Add(Vector2.up);
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
            // Paper-thin shards have to read from the street and through a cutaway.
            triangles.Add(first + 2); triangles.Add(first + 1); triangles.Add(first);
            triangles.Add(first + 3); triangles.Add(first + 2); triangles.Add(first);
        }

        static void AddDamageTriangle(
            Vector3 a, Vector3 b, Vector3 c, Vector3 normal,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
            List<int> triangles)
        {
            int first = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            uvs.Add(Vector2.zero); uvs.Add(Vector2.right); uvs.Add(Vector2.up);
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first + 2); triangles.Add(first + 1); triangles.Add(first);
        }

        static uint DamageSeed(string label, Vector3 at, float width)
        {
            uint hash = 2166136261u;
            label ??= string.Empty;
            for (int i = 0; i < label.Length; i++) hash = (hash ^ label[i]) * 16777619u;
            Mix(ref hash, Mathf.RoundToInt(at.x * 10f));
            Mix(ref hash, Mathf.RoundToInt(at.z * 10f));
            Mix(ref hash, Mathf.RoundToInt(width * 10f));
            return hash != 0 ? hash : 0x9E3779B9u;
        }

        static void Mix(ref uint hash, int value)
        {
            hash = (hash ^ unchecked((uint)value)) * 16777619u;
        }

        struct DamageDice
        {
            uint state;

            public DamageDice(uint seed) => state = seed != 0 ? seed : 0x9E3779B9u;

            uint Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return state;
            }

            public float Unit() => (Next() & 0x00FFFFFFu) / 16777215f;
            public float Range(float min, float max) => Mathf.Lerp(min, max, Unit());
            public int Int(int max) => max <= 1 ? 0 : (int)(Next() % (uint)max);
        }

        /// <summary>Nail a run of planks across the storefront, at the door's line, from
        /// the ground up - the boarded-up ground floor. Left standing (parented to the
        /// damage root, not to the burn object that made it).</summary>
        internal static void BoardUp(GangFront front, float groundY, Material board)
        {
            front.Boarded = true;
            BoardUpAt(front.Door, front.Outward, front.GangName, groundY, board);
        }

        internal static Transform BoardUpAt(
            Vector3 doorAt, Vector3 facingOut, string label, float groundY, Material board,
            float width = StoreWidth)
        {
            // THE SHOP'S width, not a constant: planks cut to seven metres ran across the
            // neighbours' fronts, which on an ordinary street is most of them.
            var frontage = width > 0.5f ? width : StoreWidth;
            var outward = facingOut.sqrMagnitude > 1e-4f ? facingOut.normalized : Vector3.forward;
            // LookRotation(outward) puts the plank's local +X along the frontage, so the
            // boards run across the storefront with no separate lateral axis to carry.
            // Boarding belongs on the exterior face. A small outward offset clears the
            // glass/facade plane without pushing the planks out onto the pavement.
            var baseAt = new Vector3(doorAt.x, groundY, doorAt.z) + outward * BoardOutset;
            var facing = Quaternion.LookRotation(outward, Vector3.up);

            var boards = new GameObject("Boards · " + label).transform;
            boards.SetParent(Root(), false);

            const int planks = 5;
            float gap = StoreHeight / planks;
            for (int i = 0; i < planks; i++)
            {
                float h = gap * (i + 0.5f);
                var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plank.name = "Plank";
                var col = plank.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                plank.transform.SetParent(boards, false);
                plank.transform.rotation = facing * Quaternion.Euler(0f, 0f, Random.Range(-2.5f, 2.5f));
                plank.transform.position = baseAt + Vector3.up * h;
                plank.transform.localScale = new Vector3(frontage, gap * 0.82f, 0.09f);
                var mr = plank.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.sharedMaterial = board;
            }

            // two cross-braces, corner to corner, the way a shopfront gets nailed shut
            for (int s = -1; s <= 1; s += 2)
            {
                var brace = GameObject.CreatePrimitive(PrimitiveType.Cube);
                brace.name = "Brace";
                var col = brace.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                brace.transform.SetParent(boards, false);
                float diag = Mathf.Atan2(StoreHeight, frontage) * Mathf.Rad2Deg;
                brace.transform.rotation = facing * Quaternion.Euler(0f, 0f, s * diag);
                brace.transform.position = baseAt + Vector3.up * (StoreHeight * 0.5f);
                float len = Mathf.Sqrt(frontage * frontage + StoreHeight * StoreHeight);
                brace.transform.localScale = new Vector3(len, 0.16f, 0.07f);
                var mr = brace.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.sharedMaterial = board;
            }

            return boards;
        }
    }

    /// <summary>Small retry pump owned by the persistent damage root. It exists because
    /// source storefront meshes must remain untouched during ScenePerf's incremental
    /// gather, which can overlap the frame in which a crew finishes smashing a shop.</summary>
    [DisallowMultipleComponent]
    sealed class ShopDamageLifecycle : MonoBehaviour
    {
        void Update() => ShopDamage.PumpDeferredViews();
    }

    /// <summary>
    /// Runtime-only cut list for one authored building mesh. The source asset is never
    /// edited: every smashed business contributes exact glass-triangle ordinals to one
    /// instance clone, and removing the final contribution puts the original mesh back.
    /// A merged city chunk is held open while that clone has to be visible.
    /// </summary>
    [DisallowMultipleComponent]
    sealed class ShopGlassSurface : MonoBehaviour
    {
        readonly Dictionary<string, Dictionary<int, HashSet<int>>> cuts =
            new Dictionary<string, Dictionary<int, HashSet<int>>>();

        MeshFilter filter;
        MeshRenderer renderer;
        Mesh source;
        Mesh damaged;
        MergedChunk heldChunk;

        public Mesh SourceMesh
        {
            get
            {
                Ensure();
                return source;
            }
        }

        void Awake() => Ensure();

        void Ensure()
        {
            if (filter == null) filter = GetComponent<MeshFilter>();
            if (renderer == null) renderer = GetComponent<MeshRenderer>();
            if (source == null && filter != null) source = filter.sharedMesh;
        }

        public bool Apply(string businessId, Dictionary<int, HashSet<int>> removed)
        {
            Ensure();
            if (filter == null || renderer == null || source == null || !source.isReadable ||
                string.IsNullOrEmpty(businessId) || removed == null || removed.Count == 0)
                return false;

            if (cuts.Count == 0)
            {
                heldChunk = MergedChunk.Of(renderer);
                if (heldChunk != null)
                {
                    if (!heldChunk.Hold())
                    {
                        heldChunk = null;
                        return false;
                    }
                }
                else if (!renderer.enabled)
                {
                    return false;
                }
            }

            var copy = new Dictionary<int, HashSet<int>>();
            foreach (var pair in removed)
                copy[pair.Key] = new HashSet<int>(pair.Value);
            cuts[businessId] = copy;
            Rebuild();
            return true;
        }

        public bool Remove(string businessId)
        {
            if (string.IsNullOrEmpty(businessId) || !cuts.Remove(businessId))
                return false;
            if (cuts.Count == 0) Restore();
            else Rebuild();
            return true;
        }

        void Rebuild()
        {
            if (filter == null || source == null)
                return;

            var next = Object.Instantiate(source);
            next.name = source.name + " (shop glass cut)";
            next.hideFlags = HideFlags.HideAndDontSave;
            for (var sub = 0; sub < source.subMeshCount; sub++)
            {
                var original = source.GetTriangles(sub);
                var kept = new List<int>(original.Length);
                for (var t = 0; t + 2 < original.Length; t += 3)
                {
                    var remove = false;
                    foreach (var cut in cuts.Values)
                        if (cut.TryGetValue(sub, out var offsets) && offsets.Contains(t))
                        {
                            remove = true;
                            break;
                        }
                    if (remove)
                        continue;
                    kept.Add(original[t]);
                    kept.Add(original[t + 1]);
                    kept.Add(original[t + 2]);
                }
                next.SetTriangles(kept, sub, false);
            }

            filter.sharedMesh = next;
            DestroyOwned(damaged);
            damaged = next;
        }

        void Restore()
        {
            if (filter != null && source != null)
                filter.sharedMesh = source;
            DestroyOwned(damaged);
            damaged = null;
            if (heldChunk != null)
            {
                heldChunk.Release();
                heldChunk = null;
            }
        }

        void Clear()
        {
            cuts.Clear();
            Restore();
        }

        static void DestroyOwned(Object owned)
        {
            if (owned == null) return;
            if (Application.isPlaying) Object.Destroy(owned);
            else Object.DestroyImmediate(owned);
        }

        void OnDisable() => Clear();
        void OnDestroy() => Clear();
    }

    /// <summary>Owns the native mesh made for one smashed frontage. The damage object
    /// normally lives for the scene, but editor play can tear it down without a domain
    /// reload; releasing explicitly keeps repeated smash demos from leaking meshes.</summary>
    [DisallowMultipleComponent]
    sealed class ShopDamageMesh : MonoBehaviour
    {
        Mesh owned;

        public void Own(Mesh mesh) => owned = mesh;

        void OnDestroy()
        {
            if (owned == null) return;
            if (Application.isPlaying) Destroy(owned);
            else DestroyImmediate(owned);
            owned = null;
        }
    }

    /// <summary>The fire on a bombed shopfront: authored flames and embers, a warm street
    /// glow, and textured smoke drifting up. When it has burnt BurnFor seconds it boards
    /// the front up and is gone.</summary>
    public sealed class ShopFire : MonoBehaviour
    {
        GangFront _front;
        Vector3 _doorAt;
        Vector3 _facingOut;
        string _label = "";
        float _groundY;
        float _frontage = 7f;
        bool _boardWhenDone = true;
        Material _board;
        float _age;
        Light _glow;
        readonly List<Transform> _flames = new List<Transform>();          // procedural fallback
        readonly List<Transform> _fireFx = new List<Transform>();          // authored fire instances
        readonly List<Vector3> _fireBase = new List<Vector3>();            // their planted scale
        readonly List<(Transform tf, float born)> _smokes = new List<(Transform, float)>();   // procedural fallback
        readonly List<ParticleSystem> _smokeFx = new List<ParticleSystem>(); // authored smoke columns
        Material _smokeMat;
        float _nextSmoke;

        public void Begin(GangFront front, float groundY, Material fire, Material smoke, Material board)
        {
            _front = front;
            BeginAt(front.Door, front.Outward, front.GangName, groundY, fire, smoke, board);
        }

        /// <summary>The same fire on a front that has no GangFront - an ordinary shop
        /// torched over its dues (EPIC 9). Boards itself up by position and label.</summary>
        public void BeginAt(Vector3 doorAt, Vector3 facingOut, string label,
            float groundY, Material fire, Material smoke, Material board,
            float width = 7f, bool boardWhenDone = true)
        {
            // The fire is strung across THIS front, and the boards it leaves behind are
            // cut to the same width.
            _frontage = width > 0.5f ? width : 7f;
            _doorAt = doorAt;
            _facingOut = facingOut;
            _label = label ?? "";
            _groundY = groundY;
            _board = board;
            _boardWhenDone = boardWhenDone;
            _smokeMat = smoke;

            var outward = facingOut.sqrMagnitude > 1e-4f ? facingOut.normalized : Vector3.forward;
            var lateral = Vector3.Cross(Vector3.up, outward).normalized;
            var baseAt = new Vector3(doorAt.x, groundY, doorAt.z) + outward * 0.3f;
            transform.position = baseAt;
            var facing = Quaternion.LookRotation(outward, Vector3.up);

            // the fire itself: the project's shared realistic fire, a run of it strung
            // across the ground-floor frontage
            var step = Mathf.Max(0.6f, _frontage / 3f);
            for (int i = -1; i <= 1; i++)
            {
                var pos = baseAt + lateral * (i * step) + Vector3.up * 0.2f;
                var fx = BombFx.Spawn(BombFx.Fire, pos, facing, 1.15f, 0f, transform);
                if (fx == null) break;   // pack absent - drop to the procedural flames below
                _fireFx.Add(fx.transform);
                _fireBase.Add(fx.transform.localScale);
            }

            // no pack: the old primitive flames, so a stripped project still shows fire
            if (_fireFx.Count == 0)
            {
                for (int i = 0; i < 6; i++)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    q.name = "Flame";
                    var col = q.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                    q.transform.SetParent(transform, false);
                    q.transform.localPosition =
                        lateral * Random.Range(-_frontage * 0.5f, _frontage * 0.5f) +
                        Vector3.up * 0.9f;
                    var mr = q.GetComponent<MeshRenderer>();
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.sharedMaterial = fire;
                    _flames.Add(q.transform);
                }
            }

            // Smoke comes off the whole burning frontage, not one faint point above its
            // centre. Three overlapping but laterally separated columns make a dense plume
            // without turning the transparent flipbook into one flat black wall.
            for (int i = -1; i <= 1; i++)
            {
                var pos = baseAt + lateral * (i * step) + Vector3.up * 0.9f;
                var smk = BombFx.Spawn(
                    BombFx.Smoke, pos, Quaternion.identity, 1f, 0f, transform);
                if (smk == null) break;
                var system = LivingCity.Ambient.FireSmokeFx.TuneFireSmoke(
                    smk, i == 0 ? 1.15f : 0.9f);
                if (system != null) _smokeFx.Add(system);
            }

            _glow = gameObject.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.color = new Color(1f, 0.55f, 0.2f);
            _glow.range = 16f;
            _glow.intensity = 6f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _age += dt;

            // burnt out: board it up and go
            if (_age >= ShopDamage.BurnFor)
            {
                if (_boardWhenDone && _front != null)
                    ShopDamage.BoardUp(_front, _groundY, _board);
                else if (_boardWhenDone)
                    ShopDamage.BoardUpAt(
                        _doorAt, _facingOut, _label, _groundY, _board, _frontage);
                Destroy(gameObject);
                return;
            }

            // the flames flicker and always face the camera enough (billboard to +Y up),
            // fading down over the last few seconds as the fire dies
            float fade = Mathf.Clamp01((ShopDamage.BurnFor - _age) / 5f);
            var cam = Camera.main;

            // Authored fire burns at full, then is shrunk away over the last few seconds as
            // it dies down to the boarding-up
            for (int i = 0; i < _fireFx.Count; i++)
            {
                var f = _fireFx[i];
                if (f != null) f.localScale = _fireBase[i] * Mathf.Lerp(0.35f, 1f, fade);
            }
            for (int i = 0; i < _smokeFx.Count; i++)
            {
                var smoke = _smokeFx[i];
                if (smoke == null) continue;
                var emission = smoke.emission;
                emission.rateOverTime = (i == 1 ? 15f : 12f) * fade;
            }

            for (int i = 0; i < _flames.Count; i++)
            {
                var f = _flames[i];
                float flick = 0.7f + 0.5f * Mathf.Abs(Mathf.Sin((_age + i) * (5f + i)));
                f.localScale = new Vector3(1.4f, (2.2f + flick) * fade, 1f);
                if (cam != null)
                {
                    var to = f.position - cam.transform.position; to.y = 0f;
                    if (to.sqrMagnitude > 1e-3f) f.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
                }
            }
            if (_glow != null) _glow.intensity = (5f + 3f * Mathf.Abs(Mathf.Sin(_age * 11f))) * fade;

            // procedural smoke puffs - only when the Synty smoke column is not present
            if (_smokeFx.Count == 0)
            {
            _nextSmoke -= dt;
            if (_nextSmoke <= 0f && _age < ShopDamage.BurnFor - 4f)
            {
                _nextSmoke = 0.6f;
                var s = GameObject.CreatePrimitive(PrimitiveType.Quad);
                s.name = "Smoke";
                var col = s.GetComponent<Collider>();
                if (col != null) Destroy(col);
                s.transform.SetParent(transform, false);
                s.transform.localPosition = Vector3.up * 2.6f;
                var mr = s.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.sharedMaterial = _smokeMat;
                _smokes.Add((s.transform, _age));
            }
            for (int i = _smokes.Count - 1; i >= 0; i--)
            {
                var (tf, born) = _smokes[i];
                float sa = _age - born;
                if (tf == null || sa > 4f) { if (tf != null) Destroy(tf.gameObject); _smokes.RemoveAt(i); continue; }
                tf.localPosition = Vector3.up * (2.6f + sa * 1.6f);
                tf.localScale = Vector3.one * (1.2f + sa * 0.9f);
                if (Camera.main != null)
                {
                    var to = tf.position - Camera.main.transform.position; to.y = 0f;
                    if (to.sqrMagnitude > 1e-3f) tf.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
                }
            }
            }   // end procedural-smoke fallback
        }
    }
}
