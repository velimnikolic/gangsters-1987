using UnityEngine;
using UnityEngine.UI;
using LivingCity.Gameplay;

namespace LivingCity.UI
{
    /// <summary>
    /// The whole booter for the standalone Ledger scene: a city-less room where the
    /// book IS the game. GameplayBootstrap declines scenes without a CityBuilder on
    /// purpose, so this component brings the two directors and the almanac itself -
    /// the runtime-self-install contract, just scoped to one scene - hangs the Modern
    /// Menus pack's heist art behind the book, and opens the book without waiting for
    /// P. After that first opening the book is the almanac's as ever: P toggles, Esc
    /// peels, and closing it leaves the player looking at the key art.
    ///
    /// The backdrop canvas carries NO GraphicRaycaster - the project rule: a canvas
    /// earns a raycaster only by owning clicks, and art owns none.
    /// </summary>
    public sealed class LedgerMenuScene : MonoBehaviour
    {
        /// <summary>Far under the book's 110 - and under every HUD, were one to exist
        /// here. The art is a floor, not a layer.</summary>
        const int BackdropOrder = 10;

        PersonnelAlmanac almanac;
        bool opened;

        void Awake()
        {
            // Scene-wide checks, not host-local - the GameplayBootstrap discipline: a
            // scene someone already wired must not get a second director.
            if (!FindAnyObjectByType<PersonnelDirector>())
                gameObject.AddComponent<PersonnelDirector>();
            if (!FindAnyObjectByType<OutfitDirector>())
                gameObject.AddComponent<OutfitDirector>();

            almanac = FindAnyObjectByType<PersonnelAlmanac>();
            if (!almanac)
                almanac = gameObject.AddComponent<PersonnelAlmanac>();
        }

        void Start() => BuildBackdrop();

        void Update()
        {
            // The almanac builds its page in Start and this component must not assume
            // Start order - poll until the book is ready, open it ONCE, and never
            // fight the player over it afterwards.
            if (!opened && almanac && almanac.TryOpenBook())
                opened = true;
        }

        void BuildBackdrop()
        {
            var go = new GameObject("Ledger Backdrop", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BackdropOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var art = new GameObject("Art", typeof(RectTransform));
            art.transform.SetParent(go.transform, false);
            var artRect = (RectTransform)art.transform;
            artRect.anchorMin = artRect.anchorMax = new Vector2(0.5f, 0.5f);
            artRect.anchoredPosition = Vector2.zero;

            var image = art.AddComponent<Image>();
            image.raycastTarget = false;

            var sprite = LedgerSkinSet.Backdrop;
            if (sprite)
            {
                image.sprite = sprite;
                image.color = Color.white;
                // Envelope, not fit: key art may crop at odd aspects but must never
                // letterbox - the fitter keeps the sprite's own proportions while
                // covering the screen whole.
                var fitter = art.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            }
            else
            {
                // Skin not baked yet - the dark room the book already assumes.
                artRect.anchorMin = Vector2.zero;
                artRect.anchorMax = Vector2.one;
                artRect.offsetMin = artRect.offsetMax = Vector2.zero;
                image.color = LedgerPalette.Room;
            }

            // A quiet scrim so the art reads as a room behind the book, not a rival
            // for it - and white ledger text never lands on a neon sign.
            var scrim = new GameObject("Scrim", typeof(RectTransform));
            scrim.transform.SetParent(go.transform, false);
            var scrimRect = (RectTransform)scrim.transform;
            scrimRect.anchorMin = Vector2.zero;
            scrimRect.anchorMax = Vector2.one;
            scrimRect.offsetMin = scrimRect.offsetMax = Vector2.zero;
            var scrimImage = scrim.AddComponent<Image>();
            scrimImage.color = new Color(0f, 0f, 0f, 0.35f);
            scrimImage.raycastTarget = false;
        }
    }
}
