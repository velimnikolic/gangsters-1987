using UnityEngine;
using UnityEngine.UI;
using LivingCity.Gameplay;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// The whole booter for the standalone Ledger scene: a city-less room where the
    /// book IS the game. GameplayBootstrap declines scenes without a CityBuilder on
    /// purpose, so this component brings the two directors and the almanac itself -
    /// the runtime-self-install contract, just scoped to one scene - lays the rest of
    /// the desk behind the folder, and opens the book without waiting for P. After
    /// that first opening the book is the almanac's as ever: P toggles, Esc peels,
    /// and closing it leaves the player looking at the empty desk.
    ///
    /// The backdrop canvas carries NO GraphicRaycaster - the project rule: a canvas
    /// earns a raycaster only by owning clicks, and a desk owns none.
    /// </summary>
    public sealed class LedgerMenuScene : MonoBehaviour
    {
        /// <summary>Far under the book's 110 - and under every HUD, were one to exist
        /// here. The desk is a floor, not a layer.</summary>
        const int BackdropOrder = 10;

        PersonnelAlmanac almanac;
        bool opened;

        void Awake()
        {
            // Scene-wide checks, not host-local - the GameplayBootstrap discipline: a
            // scene someone already wired must not get a second director.
            if (!FindAnyObjectByType<Ambient.CityClock>())
                gameObject.AddComponent<Ambient.CityClock>();
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

        /// <summary>The rest of the desk: the same walnut and lamp the folder sits on,
        /// across the whole screen, so the right half (where the city's map would
        /// dock) reads as more desk rather than a void.</summary>
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

            var desk = NewRect("Desk", go.transform);
            Stretch(desk);
            Fill(desk, LedgerStyle.Desk);
            Grain(desk, 1920f, 1200f, 1.6f);

            var lamp = NewRect("Lamp", desk);
            lamp.anchorMin = lamp.anchorMax = new Vector2(0f, 1f);
            lamp.pivot = new Vector2(0.5f, 0.5f);
            lamp.anchoredPosition = new Vector2(180f, -40f);
            lamp.sizeDelta = new Vector2(1500f, 1500f);
            var lampImage = lamp.gameObject.AddComponent<RawImage>();
            lampImage.texture = LedgerStyle.RadialLight;
            lampImage.color = LedgerStyle.Lamp;
            lampImage.raycastTarget = false;
        }
    }
}
