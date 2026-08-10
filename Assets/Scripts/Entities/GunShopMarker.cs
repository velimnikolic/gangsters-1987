using UnityEngine;
using LivingCity.Gameplay;

namespace LivingCity.Entities
{
    /// <summary>
    /// Data marker on the placed gun shop: the door, the overlay face, and - the part no
    /// other building has - the right-click target the buy actions serve. The shop is a
    /// Shops-group storefront in the street wall (the post office's placement, with the
    /// same uniqueBuildings cap: at most one per city, and a seed may have none); the
    /// marker is stamped by InteractionMarkers' name sweep, not by BlockBuilder.
    ///
    /// Not a StallHost: the shop has no forecourt and no visiting cars - iron changes
    /// hands on foot. Not a ShopEntrance either, for SchoolMarker's reason: ShopEntrance
    /// serves the opportunistic pedestrian traffic and is granted by a rule that can
    /// decline; this door serves the PLAYER, so it carries its own coordinates. Door
    /// stored in local space, per StallHost's persistence rule.
    ///
    /// The overlay marker is always-on like the bank's and the school's, NOT
    /// BusinessMarker's SelectedOnly - a one-per-city service the player is meant to go
    /// looking for should be findable from the boom.
    /// </summary>
    public sealed class GunShopMarker : MonoBehaviour, UI.IOverlaySubject, IContextTarget
    {
        public const string PrefabName = "building-shop-china";

        [SerializeField] Vector3 doorLocal;

        public void SetDoor(Vector3 localDoor) => doorLocal = localDoor;

        /// <summary>On the facade plane at ground level - the counter is through here.</summary>
        public Vector3 DoorWorld => transform.TransformPoint(doorLocal);

        /// <summary>A step out from the door - where a buyer stands. ShopEntrance's 1.1:
        /// the shop stands flush on the street wall, no forecourt walkway to cross.</summary>
        public Vector3 StandWorld => DoorWorld + Facing * 1.1f;

        /// <summary>Out of the shop toward the street - the facade normal.</summary>
        public Vector3 Facing
        {
            get
            {
                var forward = transform.forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            }
        }

        // ------------------------------------------------------------ the menu target

        public Transform TargetTransform => transform;

        /// <summary>Always open. Whether anything is left to BUY is each row's own
        /// IsAvailable - a sold-out shop still opens to a Cancel, which is honest.</summary>
        public bool ContextAvailable => true;

        // --------------------------------------------------------------- the overlay

        /// <summary>Found once, lazily - the popup wants the owned count, and the player
        /// is spawned by GameplayBootstrap after the city (and this marker) is built.</summary>
        PlayerArsenal arsenal;

        float markerHeight = -1f;

        PlayerArsenal Arsenal
        {
            get
            {
                if (!arsenal)
                    arsenal = FindAnyObjectByType<PlayerArsenal>();
                return arsenal;
            }
        }

        void OnEnable() => UI.OverlayRegistry.Register(this);

        void OnDisable() => UI.OverlayRegistry.Unregister(this);

        Transform UI.IOverlaySubject.OverlayAnchor => transform;

        float UI.IOverlaySubject.OverlayHeight
        {
            get
            {
                // Once, on first read - see SchoolMarker for why not in OnEnable.
                if (markerHeight < 0f)
                    markerHeight = UI.BuildingMarker.HeightFor(transform);

                return markerHeight;
            }
        }

        bool UI.IOverlaySubject.OverlayHidden => false;
        UI.OverlayShape UI.IOverlaySubject.MarkerShape => UI.OverlayShape.Square;
        Color UI.IOverlaySubject.OverlayColor => UI.IntentionPalette.Place;
        string UI.IOverlaySubject.OverlayTitle => UI.GunShopIntention.Title();

        string UI.IOverlaySubject.OverlayLine =>
            UI.GunShopIntention.Line(
                Arsenal ? Arsenal.OwnedCount : 0,
                WeaponCatalog.Instance.weapons.Length);

        long UI.IOverlaySubject.OverlayKey => Arsenal ? Arsenal.OwnedCount : 0;
    }
}
