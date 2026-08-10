using UnityEngine;
using LivingCity.Gameplay;

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

        public PropertyOwner Owner { get; set; }
        public bool Protected { get; set; }

        float markerHeight = -1f;

        public void Init(
            BusinessCategory category, string businessName, int blockId,
            PropertyOwner owner, int weeklyIncome)
        {
            Category = category;
            BusinessName = businessName;
            BlockId = blockId;
            Owner = owner;
            WeeklyIncome = weeklyIncome;
        }

        void OnEnable()
        {
            UI.OverlayRegistry.Register(this);
            PropertyRegistry.Register(this);
        }

        void OnDisable()
        {
            UI.OverlayRegistry.Unregister(this);
            PropertyRegistry.Unregister(this);
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

        string UI.IOverlaySubject.OverlayLine =>
            UI.BusinessIntention.Line(Owner?.DisplayName, WeeklyIncome, Protected);

        long UI.IOverlaySubject.OverlayKey =>
            ((long)((Owner?.Index ?? -1) + 1) << 32)
            | ((uint)WeeklyIncome << 1)
            | (Protected ? 1u : 0u);

        UI.MarkerStyle UI.IOverlayStyledSubject.MarkerStyle =>
            new UI.MarkerStyle { SizeScale = 1f, SelectedOnly = true };
    }
}
