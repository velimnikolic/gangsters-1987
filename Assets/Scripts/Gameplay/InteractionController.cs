using UnityEngine;
using UnityEngine.EventSystems;
using LivingCity.UI;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The gameplay layer's ONE reader of the pointer. Click-to-move, hover highlight and
    /// the context menu all race over the same mouse buttons, so exactly one Update owns
    /// the ordering: Esc closes, a click while the menu is open is consumed by the menu,
    /// UI clicks belong to the UI, the camera's Space chord belongs to the camera - and
    /// only what is left reaches the world.
    ///
    /// Picking mirrors CityOverlayHud.Pick, traps and all: a SphereCast because a
    /// pedestrian's capsule is 0.2m wide, triggers ignored because the AI cars tow feeler
    /// boxes, subjects resolved through GetComponentInParent because the collider is a
    /// child. Differences: NonAlloc (hover runs at 10Hz, not per click) and two answers
    /// per cast - the nearest InteractableNpc for hover/menu, the nearest raw hit for
    /// "where did the click land".
    ///
    /// A left click on anything that owns an overlay popup (the player, an officer, the
    /// bank) is a SELECTION - CityOverlayHud handles it - and must not double as a move
    /// order; only a click on plain world becomes one.
    /// </summary>
    public sealed class InteractionController : MonoBehaviour
    {
        /// <summary>CityOverlayHud's pick constants, kept in lockstep.</summary>
        const float PickRadius = 0.35f;
        const float PickDistance = 600f;

        const float HoverInterval = 0.1f;

        /// <summary>Casts everything except the UI layer; triggers are excluded per cast.</summary>
        static readonly int PickMask = ~(1 << 5);

        static readonly RaycastHit[] Hits = new RaycastHit[64];

        [Tooltip("Found in the scene when empty.")]
        [SerializeField] PlayerMafioso player;

        [Tooltip("On the same object when empty.")]
        [SerializeField] ContextMenuUI menu;

        readonly IInteractionInput input = new DesktopInteractionInput();

        Camera cam;
        InteractableNpc hovered;
        float nextHoverAt;

        struct Pick
        {
            /// <summary>Nearest interactable pedestrian along the ray, dead or alive.</summary>
            public InteractableNpc Npc;

            /// <summary>The nearest raw hit resolves to something selectable - the player,
            /// an officer, a marked building. Selection's business, never movement's.</summary>
            public bool OnSubject;

            public bool HasSurface;
            public Vector3 SurfacePoint;
        }

        void Awake()
        {
            if (!menu)
                menu = GetComponent<ContextMenuUI>();
        }

        void Start()
        {
            cam = Camera.main ? Camera.main : FindAnyObjectByType<Camera>();
            if (!player)
                player = FindAnyObjectByType<PlayerMafioso>();

            if (!cam || !player)
            {
                Debug.LogWarning("[Interaction] Needs a camera and a PlayerMafioso in the " +
                                 "scene - interaction off.", this);
                enabled = false;
            }
        }

        void Update()
        {
            // H = hands up. Harmless when nobody is pointing a gun at him; decisive when
            // somebody is - the engage window reads IsSurrendering.
            if (input.SurrenderPressed)
                player.Surrender();

            if (input.CancelPressed && menu && menu.IsOpen)
            {
                menu.Close();
                return;
            }

            var overUi = EventSystem.current && EventSystem.current.IsPointerOverGameObject();

            if (menu && menu.IsOpen)
            {
                SetHovered(null);

                var clicked = input.PrimaryPressed || input.SecondaryPressed;
                if (!clicked || overUi)
                    return; // Button clicks are the Button's; everything else waits.

                // A click outside closes and is CONSUMED - it must not also move the player.
                menu.Close();
                if (!input.SecondaryPressed)
                    return;
                // A right-click outside falls through: closing one menu and opening
                // another over a different pedestrian is one gesture, not two.
            }

            if (overUi || input.PanModifierHeld)
            {
                SetHovered(null);
                return;
            }

            if (input.PrimaryPressed)
            {
                var pick = PickAt(input.PointerPosition);
                if (!pick.OnSubject && pick.HasSurface)
                    player.OrderMove(pick.SurfacePoint);
                return;
            }

            if (input.SecondaryPressed)
            {
                var pick = PickAt(input.PointerPosition);
                if (pick.Npc && !pick.Npc.IsDead && menu)
                {
                    SetHovered(null);
                    menu.Open(player, pick.Npc, input.PointerPosition);
                }
                return;
            }

            // Hover, throttled - a cast at 10Hz reads instant and costs a tenth of one
            // per frame.
            if (Time.unscaledTime >= nextHoverAt)
            {
                nextHoverAt = Time.unscaledTime + HoverInterval;
                var pick = PickAt(input.PointerPosition);
                SetHovered(pick.Npc && !pick.Npc.IsDead ? pick.Npc : null);
            }
        }

        void OnDisable() => SetHovered(null);

        void SetHovered(InteractableNpc npc)
        {
            if (hovered == npc)
                return;

            if (hovered)
                HoverHighlight.Clear(hovered);
            hovered = npc;
            if (hovered)
                HoverHighlight.Apply(hovered);
        }

        Pick PickAt(Vector2 screenPosition)
        {
            var result = new Pick();
            var ray = cam.ScreenPointToRay(screenPosition);
            var count = Physics.SphereCastNonAlloc(
                ray, PickRadius, Hits, PickDistance, PickMask, QueryTriggerInteraction.Ignore);

            var bestNpcDistance = float.MaxValue;
            var bestHitDistance = float.MaxValue;

            for (var i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (!hit.collider)
                    continue;

                if (hit.distance < bestHitDistance)
                {
                    bestHitDistance = hit.distance;
                    result.HasSurface = true;
                    result.SurfacePoint = hit.point;
                    result.OnSubject =
                        hit.collider.GetComponentInParent<PlayerMafioso>() ||
                        hit.collider.GetComponentInParent<IOverlaySubject>() != null;
                }

                if (hit.distance < bestNpcDistance)
                {
                    var npc = hit.collider.GetComponentInParent<InteractableNpc>();
                    if (npc)
                    {
                        bestNpcDistance = hit.distance;
                        result.Npc = npc;
                    }
                }
            }

            return result;
        }
    }
}
