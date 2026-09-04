using LivingCity.Gameplay;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using static LivingCity.UI.LedgerKit;

namespace RoadDemo
{
    /// <summary>
    /// The end of the outfit, said out loud.
    ///
    /// RANK-002 puts the rule in the simulation - the Don goes down, CampaignRunner
    /// latches <see cref="LivingCity.Outfit.CampaignRunner.Fallen"/> at both doors time
    /// comes through, and nothing resolves afterwards - and leaves the telling to the
    /// scene, because the sim never touches a screen. Until this class the telling was
    /// a Debug.LogWarning, so a campaign could end without the player being told
    /// anything at all.
    ///
    /// One black leaf with the day's front page struck on it. It is deliberately not
    /// dismissable into the running game: there IS no running game after this, and a
    /// card the player can wave away would say otherwise. The only key it takes is the
    /// one that closes the session's window on it.
    ///
    /// Sorting order 400 - over the street, over the plate, over the book (110) and
    /// over the strategic map. Nothing outranks the end.
    /// </summary>
    public sealed class OutfitEnd : MonoBehaviour
    {
        const int SortingOrder = 400;
        const float LeafW = 560f;
        /// <summary>The headline's point size, and the longest headline that prints at
        /// it across a leaf this wide.</summary>
        const float HeadPoints = 46f;
        const int HeadFitsAt = 16;
        const float LeafH = 300f;

        public static OutfitEnd Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Instance = null;

        /// <summary>Is the end on the screen? The HUDs ask, the same way they ask the
        /// almanac, so nothing paints over it and no click reaches the street.</summary>
        public static bool IsUp => Instance != null && Instance.painted;

        bool painted;
        bool listening;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            var outfit = OutfitDirector.Instance;
            if (outfit == null)
                return;

            // The event is the announcement; the latch is the truth. Subscribing when
            // the director turns up covers a scene where this class was standing first,
            // and reading Fallen covers the case where it went up after the fact -
            // a reload, or a HUD added to a campaign already over.
            if (!listening)
            {
                outfit.Runner.BossFell += Show;
                listening = true;
            }

            if (!painted && outfit.Runner.Fallen)
                Show();
        }

        void Show()
        {
            if (painted || !TMP_Settings.instance)
                return;
            painted = true;
            // Nothing after this frame can change: the campaign is latched over and the
            // leaf never comes down. Stop asking.
            enabled = false;

            EnsureEventSystem();
            Build();
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current)
                return;
            var host = new GameObject("EventSystem");
            host.AddComponent<EventSystem>();
            host.AddComponent<InputSystemUIInputModule>();
        }

        void Build()
        {
            var root = new GameObject("The End", typeof(RectTransform));
            root.transform.SetParent(transform, false);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();

            // The city goes out. Not a wash over it - the street is gone, and a card
            // floating on a lit city reads as one more panel.
            var black = NewRect("Blackout", root.transform);
            Stretch(black);
            var ground = black.gameObject.AddComponent<Image>();
            ground.color = new Color(0.02f, 0.017f, 0.015f, 0.92f);
            ground.raycastTarget = true;

            var leaf = NewRect("Leaf", root.transform);
            leaf.anchorMin = leaf.anchorMax = new Vector2(0.5f, 0.5f);
            leaf.pivot = new Vector2(0.5f, 0.5f);
            leaf.anchoredPosition = Vector2.zero;
            leaf.sizeDelta = new Vector2(LeafW, LeafH);
            Fill(leaf, LedgerV2.Panel);

            Block("Mourning band", leaf, 0f, 0f, LeafW, 8f, LedgerV2.Ink);

            var outfit = OutfitDirector.Instance;
            var roster = PersonnelDirector.Instance != null
                ? PersonnelDirector.Instance.Roster
                : null;
            var boss = roster != null ? roster.FindBoss() : null;
            var day = outfit != null ? outfit.Runner.FallenOnDay : 0;

            var kicker = Line(leaf, LedgerStyle.MonoBold, 10f, LedgerV2.Red,
                34f, -26f, LeafW - 68f, LineBox(10f),
                "THE OUTFIT · DAY " + day + " · 1987");
            kicker.characterSpacing = 14f;
            kicker.alignment = TextAlignmentOptions.Center;

            var ending = outfit != null ? outfit.Runner.Ending : OutfitEnding.TheDonIsDead;

            // The three headlines are not the same length, and a condensed 46 pt line
            // that does not fit the leaf does not shrink - TMP wraps it and then drops
            // the whole line, so the end would print with no headline at all. Cut the
            // point size to the longest word count the leaf can actually hold.
            var headline = EndingText.Headline(ending);
            var headSize = headline.Length <= HeadFitsAt
                ? HeadPoints
                : HeadPoints * HeadFitsAt / headline.Length;

            var head = Line(leaf, LedgerStyle.Condensed, headSize, LedgerV2.Ink,
                34f, -48f, LeafW - 68f, LineBox(HeadPoints), headline);
            head.characterSpacing = 2f;
            head.alignment = TextAlignmentOptions.Center;

            Block("Rule", leaf, 34f, -114f, LeafW - 68f, 2f, LedgerV2.Ink);

            var named = Line(leaf, LedgerStyle.Serif, 17f, LedgerV2.Body,
                34f, -128f, LeafW - 68f, LineBox(17f, 2),
                EndingText.Standfirst(ending, boss != null ? boss.FullName : "", day));
            named.alignment = TextAlignmentOptions.Center;

            var closing = Paragraph(leaf, LedgerStyle.Serif, 14f, LedgerV2.Muted,
                48f, -172f, LeafW - 96f, LineBox(14f, 3),
                EndingText.Closing(ending));
            closing.alignment = TextAlignmentOptions.Center;

            // What the outfit was worth when it ended - the only figures still true.
            var men = 0;
            if (roster != null)
                for (var i = 0; i < roster.Members.Count; i++)
                    if (roster.Members[i] != null && !roster.Members[i].Gone)
                        men++;
            var safe = outfit != null ? outfit.Accounts.Safe : 0;

            var tally = Line(leaf, LedgerStyle.MonoBold, 11f, LedgerV2.Label,
                34f, -(LeafH - 46f), LeafW - 68f, LineBox(11f),
                men + (men == 1 ? " MAN" : " MEN") + " LEFT ON THE BOOKS · " +
                LedgerText.Cash(safe) + " IN THE SAFE");
            tally.characterSpacing = 4f;
            tally.alignment = TextAlignmentOptions.Center;

            Block("Foot band", leaf, 0f, -(LeafH - 8f), LeafW, 8f, LedgerV2.Ink);
        }
    }
}
