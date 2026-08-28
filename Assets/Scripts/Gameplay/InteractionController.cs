using UnityEngine;
using UnityEngine.EventSystems;
using LivingCity.UI;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The gameplay layer's ONE reader of the pointer. Click-to-move and the context menu
    /// race over the same mouse buttons, so exactly one Update owns
    /// the ordering: Esc closes, a click while the menu is open is consumed by the menu,
    /// UI clicks belong to the UI, the camera's Space chord belongs to the camera - and
    /// only what is left reaches the world.
    ///
    /// Picking mirrors CityOverlayHud.Pick, traps and all: a SphereCast because a
    /// pedestrian's capsule is 0.2m wide, triggers ignored because the AI cars tow feeler
    /// boxes, subjects resolved through GetComponentInParent because the collider is a
    /// child. Differences: NonAlloc and two answers per cast - the nearest InteractableNpc
    /// for menu work, the nearest raw hit for
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

        /// <summary>Casts everything except the UI layer; triggers are excluded per cast.</summary>
        static readonly int PickMask = ~(1 << 5);

        static readonly RaycastHit[] Hits = new RaycastHit[64];

        /// <summary>Hit indices in distance order - the cast hands them back unsorted.</summary>
        static readonly int[] Order = new int[64];

        [Tooltip("Found in the scene when empty.")]
        [SerializeField] PlayerMafioso player;

        [Tooltip("On the same object when empty.")]
        [SerializeField] ContextMenuUI menu;

        readonly IInteractionInput input = new DesktopInteractionInput();

        Camera cam;

        struct Pick
        {
            /// <summary>Nearest interactable pedestrian along the ray, dead or alive.</summary>
            public InteractableNpc Npc;

            /// <summary>Nearest menu-worthy thing along the ray - a pedestrian counts here
            /// too (InteractableNpc implements it), but so does a marked building.</summary>
            public IContextTarget Target;

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
            // The personnel ledger is modal. Its full-page raycast target already blocks
            // the pointer through the overUi path below; this line covers the KEYS - H
            // must not raise the player's hands while he is reading the books.
            if (PersonnelAlmanac.IsOpen)
                return;

            // The strategic map has no raycaster - InputBlocked IS its pointer shield,
            // and it holds through the closing frame so the click or Esc that shut the
            // map is never also a move order or a menu.
            if (StrategicMapHud.InputBlocked)
                return;

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
                return;

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
                if (pick.Target != null && pick.Target.ContextAvailable && menu)
                {
                    menu.Open(player, pick.Target, input.PointerPosition);
                }
                return;
            }
        }

        Pick PickAt(Vector2 screenPosition)
        {
            var result = new Pick();
            var ray = cam.ScreenPointToRay(screenPosition);
            var count = Physics.SphereCastNonAlloc(
                ray, PickRadius, Hits, PickDistance, PickMask, QueryTriggerInteraction.Ignore);

            // Nearest first, so each of the three answers is settled by the FIRST hit that
            // carries it and the parent walks stop there.
            SortByDistance(count);

            for (var k = 0; k < count; k++)
            {
                var hit = Hits[Order[k]];
                if (!hit.collider)
                    continue;

                // A building hidden down to its ground stub keeps its full collider,
                // but a pick on its invisible upper floors must fall through to
                // whatever is actually visible behind - person, ground or target.
                if (PlayerOcclusionHider.InvisibleAt(hit.collider, hit.point))
                    continue;

                if (!result.HasSurface)
                {
                    result.HasSurface = true;
                    result.SurfacePoint = hit.point;
                    // A hidden subject (a shopper indoors, capsule still at the door) is not
                    // pickable by the overlay either - the click must stay a move order.
                    result.OnSubject =
                        hit.collider.GetComponentInParent<PlayerMafioso>() ||
                        hit.collider.GetComponentInParent<IOverlaySubject>() is { OverlayHidden: false };
                }

                if (result.Target == null)
                    result.Target = hit.collider.GetComponentInParent<IContextTarget>();

                if (!result.Npc)
                    result.Npc = hit.collider.GetComponentInParent<InteractableNpc>();

                // An InteractableNpc is itself a context target, so once the pedestrian is
                // found every answer is - nothing further down the ray can be nearer.
                if (result.Npc)
                    break;
            }

            return result;
        }

        /// <summary>Insertion sort of the hit indices by distance - the cast returns a
        /// few dozen at most, and this allocates nothing.</summary>
        static void SortByDistance(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var index = i;
                var distance = Hits[i].distance;
                var j = i - 1;
                while (j >= 0 && Hits[Order[j]].distance > distance)
                {
                    Order[j + 1] = Order[j];
                    j--;
                }
                Order[j + 1] = index;
            }
        }
    }
}
