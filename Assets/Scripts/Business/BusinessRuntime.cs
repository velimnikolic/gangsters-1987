using System.Collections.Generic;
using LivingCity.Entities;
using LivingCity.Territory;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Business
{
    /// <summary>
    /// The scene owner of the city's simulated businesses: it deals them ONCE from the plan
    /// while the city is being built, holds the directory and the site catalogue for
    /// everybody else, and binds streamed views to IDs that already exist.
    ///
    /// TerritoryRuntime's shape exactly, and for its reason: the plan is the authority, this
    /// component is the seam, and no view may establish anything through it. A block coming
    /// on camera binds a marker; a block leaving takes its marker with it and the directory
    /// does not notice - <see cref="BusinessDirectory.Version"/> is untouched by binding.
    /// </summary>
    public sealed class BusinessRuntime : MonoBehaviour
    {
        public static BusinessRuntime Instance { get; private set; }

        BusinessDirectory directory;
        BusinessShutdownLedger shutdowns;
        BusinessSiteCatalog catalog;
        BusinessPopulationReport report;
        int citySeed;

        readonly Dictionary<string, List<BusinessSite>> byPlan =
            new Dictionary<string, List<BusinessSite>>(System.StringComparer.Ordinal);

        public IBusinessQuery Query => directory;
        public BusinessDirectory Directory => directory;
        public BusinessShutdownLedger Shutdowns => shutdowns;
        public BusinessSiteCatalog Catalog => catalog;
        public BusinessPopulationReport Report => report;
        public int CitySeed => citySeed;
        public bool Populated => directory != null;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Business] A second runtime was ignored.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (shutdowns != null)
                shutdowns.Changed -= OnShutdownChanged;
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            if (shutdowns == null)
                return;
            var clock = LivingCity.Ambient.DayClock.Current;
            if (clock != null)
                shutdowns.AdvanceTo(clock.Day * 24d + clock.Hour);
        }

        /// <summary>
        /// Deal the city's businesses. Idempotent by design: the second call is ignored, so
        /// a rebuild pass, a second district or a re-entered Play cannot double the city or
        /// reroll a single deed.
        /// </summary>
        public void Init(RoadDemoBuilder builder, int seed)
        {
            if (directory != null)
                return;

            citySeed = seed;
            directory = new BusinessDirectory();
            shutdowns = new BusinessShutdownLedger(directory);
            shutdowns.Changed += OnShutdownChanged;
            catalog = new BusinessSiteCatalog();

            var core = builder != null ? builder.PrimaryCore : null;
            if (core != null)
            {
                catalog.Add(new ResidentialBusinessSites(core.ResidentialBlocks, core.Frame));
                catalog.Add(new StandaloneBusinessSites(core));
            }

            catalog.Add(new CompoundBusinessSites(core, builder?.BuiltDistricts));
            catalog.Build();
            IndexByPlan();

            report = BusinessPopulation.Populate(catalog, citySeed, directory);
            Debug.Log("[Business] " + report.Summary() + $" (seed {citySeed}).");
            foreach (var problem in report.Problems)
                Debug.LogWarning("[Business] " + problem, this);
        }

        /// <summary>The active closure at the live campaign hour. Presentation callers
        /// use this rather than maintaining their own countdown.</summary>
        public bool TryGetShutdown(
            TerritoryBusinessId businessId, out BusinessShutdownStatus status)
        {
            status = default;
            var clock = LivingCity.Ambient.DayClock.Current;
            var gameHour = clock != null ? clock.Day * 24d + clock.Hour : 0d;
            return shutdowns != null && shutdowns.TryGet(businessId, gameHour, out status);
        }

        public double CurrentGameHour
        {
            get
            {
                var clock = LivingCity.Ambient.DayClock.Current;
                return clock != null ? clock.Day * 24d + clock.Hour : 0d;
            }
        }

        void OnShutdownChanged(BusinessShutdownChange change)
        {
            if (change.Kind == BusinessShutdownChangeKind.Repaired ||
                change.Kind == BusinessShutdownChangeKind.Expired)
                ShopDamage.RepairBusiness(change.BusinessId);
        }

        void IndexByPlan()
        {
            byPlan.Clear();
            var sites = catalog.Sites;
            for (var i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                if (!byPlan.TryGetValue(site.SourcePlanId, out var list))
                {
                    list = new List<BusinessSite>();
                    byPlan.Add(site.SourcePlanId, list);
                }
                list.Add(site);
            }
        }

        /// <summary>Every site published out of one plan or recipe, in the catalogue's
        /// order. The view binder asks for a block's sites by the recipe ID it is standing,
        /// so it never has to search the whole city.</summary>
        public IReadOnlyList<BusinessSite> SitesOfPlan(string planId)
        {
            if (planId != null && byPlan.TryGetValue(planId, out var list))
                return list;
            return System.Array.Empty<BusinessSite>();
        }

        /// <summary>The site a business stands on, for a consumer that has an ID and needs a
        /// doorstep - the approach command's path when no view is in the scene.</summary>
        public bool TryGetSite(TerritoryBusinessId businessId, out BusinessSite site)
        {
            site = null;
            return directory != null && catalog != null &&
                   directory.TryGet(businessId, out var record) &&
                   catalog.TryGet(record.SiteId, out site);
        }

        public bool TryGetBusiness(TerritoryBusinessId businessId, out BusinessRecord record)
        {
            record = null;
            return directory != null && directory.TryGet(businessId, out record);
        }

        public string OwnerNameOf(BusinessRecord record) =>
            record != null && directory != null &&
            directory.TryGetOwner(record.OwnerId, out var owner)
                ? owner.DisplayName
                : "Unknown owner";

        // ------------------------------------------------------------------ view binding

        /// <summary>
        /// A block view has just been composed: stamp a marker on whatever piece of it
        /// stands on each of the plan's sites. The marker is a BINDING - it carries an ID
        /// that already exists and creates none - so a view that is pooled, disabled or
        /// destroyed removes only itself.
        ///
        /// A site with no piece under it (a merged block, a storefront the composer refused)
        /// is left unbound on purpose and counted by the audit. Simulation truth is not
        /// allowed to depend on whether a mesh happened to stand there.
        /// </summary>
        public int BindBlockView(string planId, Transform content)
        {
            if (directory == null || content == null)
                return 0;

            var sites = SitesOfPlan(planId);
            if (sites.Count == 0)
                return 0;

            // The pieces are measured ONCE per block, not once per shop. This runs on the
            // streaming path while the camera moves, and a renderer sweep per (child, site)
            // pair allocated an array per pair for the same answer every time.
            MeasurePieces(content);

            // The composer acquires prefab instances from a pool. A BusinessMarker was
            // added to one of those instances the first time it represented a shop, so
            // the component comes back with the prefab even when the instance now stands
            // in another block. Treat every marker under this freshly composed payload
            // as unassigned before matching the current plan. Otherwise the different-ID
            // guard below mistakes yesterday's pooled binding for a second shop on this
            // pass, skips the real binding, and OnEnable publishes the old business at
            // the new building. Clicking that building then sends a crew to the old
            // business's doorstep, potentially at the other end of the city.
            ViewMarkers.Clear();
            content.GetComponentsInChildren(true, ViewMarkers);
            for (var i = 0; i < ViewMarkers.Count; i++)
                if (ViewMarkers[i] != null)
                    ViewMarkers[i].ClearViewBinding();
            ViewMarkers.Clear();

            var bound = 0;
            for (var i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                if (!site.Eligible || !directory.TryGetBySite(site.SiteId, out var record))
                    continue;

                var target = PieceAt(site);
                if (target == null)
                    continue;

                var marker = target.GetComponent<BusinessMarker>();
                if (marker != null && marker.BusinessId.IsValid && marker.BusinessId != record.Id)
                    continue; // that piece is already somebody else's shop

                if (marker == null)
                    marker = target.gameObject.AddComponent<BusinessMarker>();

                marker.BindTo(record, site, OwnerNameOf(record));
                bound++;
            }

            return bound;
        }

        /// <summary>How many of the city's businesses currently have a live view bound to
        /// them. Read by the audit; nothing acts on it.</summary>
        public int BoundViews => BusinessViewBindings.Count;

        /// <summary>
        /// Anything shorter than this is not a shop: paving, kerbs and painted bays are
        /// flat, and a bin, a bench or an ambient figure stands under two and a half metres.
        /// The lowest thing this must still admit is the coffee-shop kit.
        /// </summary>
        const float BuildingHeight = 2.5f;

        /// <summary>How much of a site's own ground a piece must stand on to BE that
        /// business. A shop covers nearly all of its site; a prop container that happens to
        /// straddle the corner of one covers almost none.</summary>
        const float MinOverlapShare = 0.25f;

        /// <summary>Every candidate piece of the block being bound, and its world box.
        /// Rebuilt once per block bind - see BindBlockView.</summary>
        static readonly List<Transform> Pieces = new List<Transform>();
        static readonly List<Bounds> PieceBounds = new List<Bounds>();
        static readonly List<Renderer> PieceRenderers = new List<Renderer>();
        static readonly List<BusinessMarker> ViewMarkers = new List<BusinessMarker>();

        /// <summary>
        /// The block's buildings, measured. Paving, kerbs, road markings and painted bays
        /// are all flat and are dropped here rather than at every site: a business is a
        /// building, and the height test is what keeps a shop off a pavement slab.
        /// </summary>
        static void MeasurePieces(Transform content)
        {
            Pieces.Clear();
            PieceBounds.Clear();

            for (var i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);
                if (!child.gameObject.activeSelf)
                    continue;

                PieceRenderers.Clear();
                // includeInactive, and it is load-bearing: the recycler binds while the
                // pooled holder is still INACTIVE (the holder is switched on after the
                // build), so an active-only sweep finds no renderer and no shop ever
                // binds in the streamed city. Bounds on an inactive renderer are valid -
                // the recycler's own flat-caster pass reads them the same way.
                child.GetComponentsInChildren(true, PieceRenderers);
                if (PieceRenderers.Count == 0)
                    continue;

                var bounds = PieceRenderers[0].bounds;
                for (var r = 1; r < PieceRenderers.Count; r++)
                    bounds.Encapsulate(PieceRenderers[r].bounds);
                if (bounds.size.y < BuildingHeight)
                    continue;

                Pieces.Add(child);
                PieceBounds.Add(bounds);
            }

            PieceRenderers.Clear();
        }

        /// <summary>
        /// The measured piece that IS this business: the one standing on the site's own
        /// ground and fitting it most tightly.
        ///
        /// The score is overlap squared over the piece's own area, and both halves of it
        /// are load-bearing. Three earlier cuts each got this wrong: searching from the
        /// DOORSTEP bound every shop to a paving slab (a doorstep lies on the pavement);
        /// taking the smallest box containing the site's centre bound a whole block to its
        /// "Ambient block life" container, because two figures make a small box that can
        /// straddle a centre; and plain maximum overlap bound a cafe to the block-wide
        /// "Palm City pavement essentials", which covers the cafe's ground as completely as
        /// the cafe does. Dividing by the piece's own area is what tells a shop from a
        /// container that happens to lie over it.
        ///
        /// There is deliberately no nearest-piece fallback. A site whose building did not
        /// compose stays unbound and is counted by the audit: a marker on the wrong mesh
        /// would tell the player a pavement is a coffee house.
        /// </summary>
        static Transform PieceAt(BusinessSite site)
        {
            var xMin = site.Footprint.XMin;
            var zMin = site.Footprint.ZMin;
            var xMax = xMin + site.Footprint.Width;
            var zMax = zMin + site.Footprint.Depth;
            var siteArea = Mathf.Max(1f, site.Footprint.Width * site.Footprint.Depth);

            Transform best = null;
            var bestScore = 0f;

            for (var i = 0; i < Pieces.Count; i++)
            {
                var bounds = PieceBounds[i];
                var overlap =
                    Mathf.Max(0f, Mathf.Min(xMax, bounds.max.x) - Mathf.Max(xMin, bounds.min.x)) *
                    Mathf.Max(0f, Mathf.Min(zMax, bounds.max.z) - Mathf.Max(zMin, bounds.min.z));
                if (overlap < siteArea * MinOverlapShare)
                    continue;

                var pieceArea = Mathf.Max(1f, bounds.size.x * bounds.size.z);
                var score = overlap * overlap / pieceArea;
                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = Pieces[i];
            }

            return best;
        }
    }

    /// <summary>
    /// Which businesses currently have a view in the world. A pure projection: entries
    /// arrive when a marker is enabled and leave when it is disabled, and nothing here can
    /// add, remove or change a business.
    /// </summary>
    public static class BusinessViewBindings
    {
        static readonly Dictionary<TerritoryBusinessId, BusinessMarker> Bound =
            new Dictionary<TerritoryBusinessId, BusinessMarker>();

        public static int Count => Bound.Count;

        public static void Bind(TerritoryBusinessId id, BusinessMarker marker)
        {
            if (!id.IsValid || marker == null)
                return;
            Bound[id] = marker;
        }

        public static void Unbind(TerritoryBusinessId id, BusinessMarker marker)
        {
            if (!id.IsValid)
                return;
            if (Bound.TryGetValue(id, out var current) && current == marker)
                Bound.Remove(id);
        }

        public static bool TryGet(TerritoryBusinessId id, out BusinessMarker marker) =>
            Bound.TryGetValue(id, out marker) && marker != null;

        /// <summary>Static state outlives Play when domain reload is off - PropertyRegistry's
        /// reason, closed the same way.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Bound.Clear();
    }
}
