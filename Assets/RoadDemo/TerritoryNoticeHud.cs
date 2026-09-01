using System.Collections.Generic;
using LivingCity.Territory;
using LivingCity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// What the streets are saying, in one line at a time.
    ///
    /// The simulation announces everything it does - a street changing hands, a shop
    /// coming round or telling a house no - and until now only the debug page could hear
    /// it. This is the player's ear: the last few of those announcements, in the words the
    /// player is allowed to have, fading out on their own.
    ///
    /// It READS the event stream and nothing else. There is no button on it, it carries no
    /// raycaster, and it cannot write a thing - which is the whole rule this epic is about.
    /// </summary>
    public sealed class TerritoryNoticeHud : MonoBehaviour
    {
        /// <summary>How many lines stand at once. More than this and it stops being a
        /// margin note and starts being a feed nobody reads.</summary>
        const int Lines = 3;

        const float HoldSeconds = 7f;
        const float FadeSeconds = 1.5f;
        const float LineHeight = 22f;
        const float Width = 420f;

        readonly List<Notice> notices = new List<Notice>();

        TerritoryRuntime runtime;
        RectTransform panel;
        bool listening;

        sealed class Notice
        {
            public TMP_Text Label;
            public float Until;
        }

        public void Init(TerritoryRuntime territoryRuntime)
        {
            runtime = territoryRuntime;
            Listen();
        }

        void Start()
        {
            if (runtime == null)
                runtime = TerritoryRuntime.Instance;
            if (!TMP_Settings.instance)
            {
                enabled = false;
                return;
            }

            Build();
            Listen();
        }

        void Listen()
        {
            var events = runtime?.Events;
            if (events == null || listening)
                return;

            events.BlockControl += OnControl;
            events.BlockContested += OnContested;
            events.ControlLost += OnLost;
            events.BusinessCompliance += OnCompliance;
            events.RoundSettled += OnRound;
            events.Presence += OnPresence;
            listening = true;
        }

        void OnDestroy()
        {
            var events = runtime?.Events;
            if (events == null || !listening)
                return;
            events.BlockControl -= OnControl;
            events.BlockContested -= OnContested;
            events.ControlLost -= OnLost;
            events.BusinessCompliance -= OnCompliance;
            events.RoundSettled -= OnRound;
            events.Presence -= OnPresence;
            listening = false;
        }

        /// <summary>A round came home, or it did not (ECON-004/008). Only ours - a
        /// rival family's money is its own business.</summary>
        void OnRound(CollectionRoundSettled change)
        {
            if (change.GangId.Value != LivingCity.Gangs.GangCatalog.PlayerGangId)
                return;
            if (change.End == TerritoryRoundEnd.Banked)
                Say(change.Amount > 0
                    ? "the round banked " + LivingCity.UI.LedgerText.Cash(change.Amount) +
                      (change.Missed > 0 ? " — " + change.Missed + " door(s) did not pay" : "")
                    : "the round came home empty-handed");
            else
                Say(change.Amount > 0
                    ? "a round was lost with " + LivingCity.UI.LedgerText.Cash(change.Amount) +
                      " on it"
                    : "a round was lost");
        }

        void Build()
        {
            var go = new GameObject("Territory Notices", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvasRect = (RectTransform)go.transform;
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Under the clock bar and the context cards; a notice never covers a choice.
            canvas.sortingOrder = 85;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            var host = new GameObject("Lines", typeof(RectTransform));
            host.transform.SetParent(go.transform, false);
            panel = (RectTransform)host.transform;
            // Bottom left, above the crew bar's own corner.
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(0f, 0f);
            panel.pivot = new Vector2(0f, 0f);
            panel.anchoredPosition = new Vector2(24f, 132f);
            panel.sizeDelta = new Vector2(Width, Lines * LineHeight);

            for (var i = 0; i < Lines; i++)
            {
                var line = new GameObject("Notice", typeof(RectTransform));
                line.transform.SetParent(panel, false);
                var rect = (RectTransform)line.transform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = new Vector2(0f, i * LineHeight);
                rect.sizeDelta = new Vector2(Width, LineHeight);

                var label = line.AddComponent<TextMeshProUGUI>();
                label.font = LedgerStyle.Mono;
                label.fontSize = 13f;
                label.color = LedgerStyle.InkLabel;
                label.alignment = TextAlignmentOptions.BottomLeft;
                label.raycastTarget = false;
                label.text = "";

                notices.Add(new Notice { Label = label, Until = 0f });
            }
        }

        void Update()
        {
            Listen();
            for (var i = 0; i < notices.Count; i++)
            {
                var notice = notices[i];
                if (notice.Label == null || notice.Until <= 0f)
                    continue;

                var left = notice.Until - Time.unscaledTime;
                if (left <= 0f)
                {
                    notice.Label.text = "";
                    notice.Until = 0f;
                    continue;
                }

                var colour = notice.Label.color;
                colour.a = left >= FadeSeconds ? 1f : left / FadeSeconds;
                notice.Label.color = colour;
            }
        }

        /// <summary>
        /// A street changed hands. The player is told which street and what it now is -
        /// never a score, and never a percentage, because there is no percentage.
        /// </summary>
        void OnControl(BlockControlChanged change)
        {
            if (TooSoon(change.BlockId))
                return;
            var name = BlockName(change.BlockId);
            var word = LedgerText.ControlWord(change.Current);
            var who = change.CurrentLeader.IsValid
                ? change.CurrentLeader.Value == LivingCity.Gangs.GangCatalog.PlayerGangId
                    ? "we"
                    : LivingCity.Gangs.GangRegistry.NameOf(change.CurrentLeader.Value)
                : "";

            Say(who.Length > 0 ? name + " — " + who + " " + word : name + " — " + word);
        }

        /// <summary>A shop came round, or told somebody no. Only ours is worth a line to
        /// the player; a rival's arrangements are his business until he is seen making them.</summary>
        void OnCompliance(BusinessComplianceChanged change)
        {
            if (change.GangId.Value != LivingCity.Gangs.GangCatalog.PlayerGangId)
                return;

            var name = BusinessName(change.BusinessId);
            if (change.Current >= 1f && change.Previous < 1f)
                Say(name + " — pays us now");
            else if (change.Previous >= 1f && change.Current < 1f)
                Say(name + " — stopped paying us");
        }

        /// <summary>A street that has become a fight says so once, plainly. The same
        /// quiet spell as OnControl - the runtime publishes both events in one branch,
        /// and two near-identical lines about one street in one frame read as a stutter.</summary>
        void OnContested(BlockBecameContested change)
        {
            if (TooSoon(change.BlockId))
                return;
            Say(BlockName(change.BlockId) + " — contested ground now");
        }

        /// <summary>And a street that has slipped out of our hands says that too.</summary>
        void OnLost(BlockControlLost change)
        {
            if (change.GangId.Value != LivingCity.Gangs.GangCatalog.PlayerGangId ||
                TooSoon(change.BlockId))
                return;
            Say(BlockName(change.BlockId) + " — no longer ours");
        }

        /// <summary>
        /// The two warnings a street gives BEFORE it changes hands (UI-007): our own
        /// weight coming off it, and somebody else's going on. Both come off the one
        /// Presence channel the simulation already publishes - there is no second
        /// signal here and nothing is invented; the words are simply what a fall in our
        /// number and a rise in theirs mean.
        ///
        /// Only on ground we have some claim to. A rival gathering men on a street we
        /// have never stood on is not news, it is the city.
        /// </summary>
        void OnPresence(PresenceChanged change)
        {
            if (!change.BlockId.IsValid || runtime?.Control == null)
                return;

            // Only ground with our name on it. A rival gathering men on a street we have
            // never stood on is not news, it is the city.
            var leader = runtime.Control.LeaderOf(change.BlockId);
            if (!leader.IsValid ||
                leader.Value != LivingCity.Gangs.GangCatalog.PlayerGangId)
                return;

            // The threshold is the WORD, not a number of our own: the block panel reads
            // presence off this same scale, so a notice fires exactly when what the
            // player would read about this street has changed - and a number wobbling
            // inside one word says nothing and is not said.
            var scale = TerritoryPresentationProfile.Default.Presence;
            var was = scale.Describe(change.Previous);
            var now = scale.Describe(change.Current);
            if (string.Equals(was, now, System.StringComparison.Ordinal))
                return;

            var ours = change.GangId.Value == LivingCity.Gangs.GangCatalog.PlayerGangId;
            if (ours && change.Current >= change.Previous)
                return;
            if (!ours && change.Current <= change.Previous)
                return;
            if (TooSoonToWarn(change.BlockId))
                return;

            Say(ours
                ? BlockName(change.BlockId) + " — our hold is weakening"
                : BlockName(change.BlockId) + " — " +
                  LivingCity.Gangs.GangRegistry.NameOf(change.GangId.Value) +
                  " is working it harder");
        }

        /// <summary>A warning is a slower thing than an announcement: a street that is
        /// slipping goes on slipping for a while, and it does not need saying every few
        /// seconds while it does.</summary>
        const float WarnQuietSeconds = 45f;
        readonly Dictionary<TerritoryBlockId, float> warned =
            new Dictionary<TerritoryBlockId, float>();

        bool TooSoonToWarn(TerritoryBlockId blockId)
        {
            if (warned.TryGetValue(blockId, out var last) &&
                Time.unscaledTime - last < WarnQuietSeconds)
                return true;
            warned[blockId] = Time.unscaledTime;
            return false;
        }

        /// <summary>
        /// One line per street per spell. A block that wobbles at a threshold would
        /// otherwise fill the corner with the same sentence, and a notice nobody can
        /// finish reading is worse than no notice.
        /// </summary>
        bool TooSoon(TerritoryBlockId blockId)
        {
            if (!blockId.IsValid)
                return false;
            if (spoken.TryGetValue(blockId, out var last) &&
                Time.unscaledTime - last < QuietSeconds)
                return true;
            spoken[blockId] = Time.unscaledTime;
            return false;
        }

        const float QuietSeconds = 4f;
        readonly Dictionary<TerritoryBlockId, float> spoken =
            new Dictionary<TerritoryBlockId, float>();

        void Say(string line)
        {
            if (panel == null || string.IsNullOrEmpty(line))
                return;

            // The newest line stands at the bottom; the rest shuffle up and fade out in
            // their own time.
            for (var i = notices.Count - 1; i > 0; i--)
            {
                notices[i].Label.text = notices[i - 1].Label.text;
                notices[i].Label.color = notices[i - 1].Label.color;
                notices[i].Until = notices[i - 1].Until;
            }

            notices[0].Label.text = line;
            notices[0].Label.color = LedgerStyle.InkLabel;
            notices[0].Until = Time.unscaledTime + HoldSeconds;
        }

        string BlockName(TerritoryBlockId blockId) =>
            runtime != null && runtime.Geography != null &&
            runtime.Geography.TryGetBlock(blockId, out var definition)
                ? definition.DisplayName
                : "A street";

        string BusinessName(TerritoryBusinessId businessId)
        {
            if (runtime != null && runtime.TryGetBusinessView(businessId, out var view))
                return view.BusinessName;
            return "A shop";
        }
    }
}
