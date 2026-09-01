using UnityEngine;
using LivingCity.Business;
using LivingCity.Gameplay;
using LivingCity.Territory;

namespace LivingCity.Entities
{
    /// <summary>
    /// What makes a building a business: a name, a gazda, weekly takings, and a place in the
    /// overlay. Added at PLAY by PropertyDirector - never baked into the saved scene - which is
    /// why nothing here is serialized and why every popup read null-guards the owner: OnEnable
    /// fires inside AddComponent, a frame before Init has run.
    ///
    /// Clickable for free: the pack prefab's own MeshCollider is on this transform, and both
    /// pick sites resolve subjects with GetComponentInParent, so implementing IOverlaySubject
    /// IS the whole click wiring. The style opts into SelectedOnly - a hundred businesses with
    /// permanent squares over them would bury the Bank and School markers that mean something.
    ///
    /// Owner and Protected are deliberately mutable, and OverlayKey covers both: the future
    /// racket flips Protected, the future buy swaps Owner, and the popup rewrites itself with
    /// no further wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BusinessMarker : MonoBehaviour, UI.IOverlaySubject, UI.IOverlayStyledSubject
    {
        public BusinessCategory Category { get; private set; }
        public string BusinessName { get; private set; }
        public int BlockId { get; private set; }
        public int WeeklyIncome { get; private set; }

        /// <summary>Canonical domain identity, derived from generation data rather than
        /// this component or its GameObject name.</summary>
        public TerritoryBusinessId BusinessId { get; private set; }

        /// <summary>The canonical counterpart of the existing integer BlockId.</summary>
        public TerritoryBlockId CanonicalBlockId { get; private set; }

        public PropertyOwner Owner { get; set; }

        /// <summary>The gazda's name when the deed lives in the simulated business
        /// directory rather than in PropertyRegistry's legacy owner pool. The popup reads
        /// whichever of the two is present.</summary>
        public string OwnerName { get; private set; }

        /// <summary>The doorstep the simulation published for this business, world XZ. A
        /// crew ordered to approach walks here rather than to the middle of the mesh.</summary>
        public Vector3 ApproachPoint { get; private set; }

        public bool Protected { get; set; }

        /// <summary>The gang whose front this building is; -1 for the honest majority. Set
        /// by GangDirector at Play, mutable for the same reason Owner is - a future takeover
        /// re-flags the building, and OverlayKey covers it so the popup follows.</summary>
        public int GangId { get; set; } = -1;

        float markerHeight = -1f;

        public void Init(
            BusinessCategory category, string businessName, int blockId,
            PropertyOwner owner, int weeklyIncome)
        {
            Init(category, businessName, blockId, owner, weeklyIncome, default, default);
        }

        public void Init(
            BusinessCategory category, string businessName, int blockId,
            PropertyOwner owner, int weeklyIncome,
            TerritoryBusinessId businessId, TerritoryBlockId canonicalBlockId)
        {
            Category = category;
            BusinessName = businessName;
            BlockId = blockId;
            Owner = owner;
            WeeklyIncome = weeklyIncome;
            BusinessId = businessId;
            CanonicalBlockId = canonicalBlockId;
            ApproachPoint = transform.position;
        }

        /// <summary>
        /// Bind this view to a business that ALREADY EXISTS in the simulation. Nothing here
        /// creates, names or rerolls anything: the ID, the name, the trade, the deed and the
        /// doorstep are all read off the record and the site. A recycled block that comes
        /// back rebinds to the same ID, which is the whole point of the site catalogue.
        /// </summary>
        public void BindTo(BusinessRecord record, BusinessSite site, string ownerName)
        {
            if (record == null || site == null)
                return;

            var wasBound = BusinessId;
            BusinessId = record.Id;
            BusinessName = record.DisplayName;
            OwnerName = ownerName;
            Category = CategoryOf(record.Archetype);
            WeeklyIncome = record.EstimatedWeeklyTurnover;
            BlockId = site.LegacyBlockId;
            CanonicalBlockId = site.BlockHint;
            ApproachPoint = new Vector3(site.Approach.X, transform.position.y, site.Approach.Z);

            // The deed is simulation state (BusinessDeeds); this component is a view of
            // it. A street streamed out and back must come back with the same owner, so
            // the answer is read here rather than left at whatever the pool remembered.
            GangId = BusinessDeeds.GangOf(BusinessId);

            if (isActiveAndEnabled)
            {
                if (wasBound.IsValid && wasBound != BusinessId)
                    BusinessViewBindings.Unbind(wasBound, this);
                BusinessViewBindings.Bind(BusinessId, this);
            }
        }

        /// <summary>The archetype's overlay category. Three words, because the popup and the
        /// future action layer have only ever known three.</summary>
        static BusinessCategory CategoryOf(BusinessArchetypeId archetype)
        {
            if (!BusinessArchetypes.TryGet(archetype, out var entry))
                return BusinessCategory.Commercial;
            switch (entry.Category)
            {
                case "industrial": return BusinessCategory.Industrial;
                case "port": return BusinessCategory.Port;
                default: return BusinessCategory.Commercial;
            }
        }

        void OnEnable()
        {
            UI.OverlayRegistry.Register(this);
            PropertyRegistry.Register(this);
            BusinessViewBindings.Bind(BusinessId, this);
        }

        void OnDisable()
        {
            UI.OverlayRegistry.Unregister(this);
            PropertyRegistry.Unregister(this);
            // Only the BINDING goes. The business itself lives in the directory and has to
            // survive its street being streamed out.
            BusinessViewBindings.Unbind(BusinessId, this);
        }

        Transform UI.IOverlaySubject.OverlayAnchor => transform;

        float UI.IOverlaySubject.OverlayHeight
        {
            get
            {
                // Once, on first read - SchoolMarker's reason: the renderer sweep allocates,
                // and in OnEnable the world bounds are not yet meaningful.
                if (markerHeight < 0f)
                    markerHeight = UI.BuildingMarker.HeightFor(transform);

                return markerHeight;
            }
        }

        bool UI.IOverlaySubject.OverlayHidden => false;
        UI.OverlayShape UI.IOverlaySubject.MarkerShape => UI.OverlayShape.Square;
        Color UI.IOverlaySubject.OverlayColor => UI.IntentionPalette.Place;
        string UI.IOverlaySubject.OverlayTitle => BusinessName ?? name;

        /// <summary>
        /// What the card says. The racket is the authority when it is running: where this
        /// shop stands with US, or the house that holds it. The old marker fields are the
        /// fallback for the scenes that have no territory simulation in them - they are a
        /// view's own guess, and a guess loses to the ledger every time.
        /// </summary>
        string UI.IOverlaySubject.OverlayLine
        {
            get
            {
                var owner = Owner?.DisplayName ?? OwnerName;
                var runtime = RoadDemo.TerritoryRuntime.Instance;
                if (runtime != null && BusinessId.IsValid &&
                    runtime.TryGetBusinessView(BusinessId, out var view))
                {
                    var line = UI.BusinessIntention.Line(
                        owner, WeeklyIncome, view.Standing, view.Protector);
                    // The dues meter on the card itself (ECON-008): what it owes us
                    // and when it last paid, straight off the ledger.
                    if (view.PaysLine.Length > 0)
                        line += "\n" + view.PaysLine;
                    return line;
                }

                return UI.BusinessIntention.Line(
                    owner, WeeklyIncome, Protected, Gangs.GangRegistry.NameOf(GangId));
            }
        }

        // Gang bits sit at 48+, clear of the owner's at 32+ (owner counts stay far below
        // 2^16 - one per building); +2 keeps unflagged (-1) distinct from gang 0.
        long UI.IOverlaySubject.OverlayKey
        {
            get
            {
                var key = ((long)(GangId + 2) << 48)
                          | ((long)((Owner?.Index ?? -1) + 1) << 32)
                          | ((uint)WeeklyIncome << 1)
                          | (Protected ? 1u : 0u);

                // The racket can change the words without any marker field moving, so its
                // standing has to be part of what the HUD compares - and so does the
                // dues meter, which moves daily and on every collection.
                var runtime = RoadDemo.TerritoryRuntime.Instance;
                if (runtime?.Racket != null && BusinessId.IsValid)
                {
                    key ^= (long)runtime.Racket.StateOf(
                        BusinessId, new Territory.TerritoryGangId(
                            Gangs.GangCatalog.PlayerGangId)) << 24;
                    if (runtime.TryGetDues(BusinessId, out var owedNow, out var paidDay))
                        key ^= ((long)owedNow << 16) ^ ((long)(paidDay + 2) << 44);
                }
                return key;
            }
        }

        UI.MarkerStyle UI.IOverlayStyledSubject.MarkerStyle =>
            new UI.MarkerStyle { SizeScale = 1f, SelectedOnly = true };
    }
}
