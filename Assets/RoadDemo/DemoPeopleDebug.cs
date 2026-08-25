using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RoadDemo
{
    /// <summary>
    /// Press F3 and a tag stands over every person near the camera saying who he is,
    /// what state his own machine is in and what he means to do about it. Press F3
    /// again and they go.
    ///
    /// D was asked for first and D is the camera's own pan (DemoCamera reads WASD), so
    /// the overlay took the debugger's usual key instead - nothing else in the demo
    /// binds it (M the map, O the lots, P the ledger, H the cutaway).
    ///
    /// EVERY PERSON, NOT EVERY TAG. The town runs thousands of walkers and a canvas
    /// with a thousand cards on it is a slideshow, so the cards are a POOL lent to the
    /// nearest people the way the headlights and the exhaust lend theirs
    /// (DemoExhaust): a partial sort around where the camera is LOOKING, and the pool
    /// handed to the front of it. Nothing far enough off to lose a tag is near enough
    /// to be read.
    ///
    /// The three lines come off the walkers themselves (PedestrianAgent.DebugName,
    /// DebugState, DebugIntent) so a crew man, a bystander and an officer each answer
    /// in their own words, and no overlay has to know how any of them work.
    ///
    /// Screen-space cards over world points, the same trick the lot overlay uses: on a
    /// ScreenSpaceOverlay canvas a UI transform's position IS screen pixels, so
    /// WorldToScreenPoint feeds it straight in and nothing has to billboard.
    /// </summary>
    public sealed class DemoPeopleDebug : MonoBehaviour
    {
        /// <summary>How many people carry a tag at once.</summary>
        public static int Budget = 40;

        /// <summary>Over the world's own annotations (the lot cards sit at 10) and
        /// under the top bar's 20 - a debug tag covers the street, never the chrome.</summary>
        const int SortingOrder = 14;

        const float CardWidth = 268f;
        const float CardHeight = 74f;

        /// <summary>Metres above his root the tag hangs - clear of the head of a man
        /// stood up, and of the roof of the car a man is sitting in.</summary>
        const float Overhead = 2.15f;

        /// <summary>Seconds between one ranking of the crowd and the next, and it is
        /// also when the words are re-read. A tag is read, not watched: four times a
        /// second is faster than anybody can take one in, and it keeps a string built
        /// per person off every frame of the budget.</summary>
        const float ResortInterval = 0.25f;

        /// <summary>The book owns the screen while it stands open, so the tags drop
        /// while it is up and come back with it - the toggle's own state untouched.</summary>
        static bool BookOpen => LivingCity.UI.PersonnelAlmanac.IsOpen;

        sealed class Tag
        {
            public RectTransform Rect;
            public TMP_Text Who;
            public TMP_Text State;
            public TMP_Text Intent;
            public PedestrianAgent Person;
        }

        readonly List<Tag> _tags = new List<Tag>();
        readonly List<PedestrianAgent> _people = new List<PedestrianAgent>();
        float[] _key = new float[0];
        int[] _order = new int[0];
        float _nextResort;

        GameObject _root;
        Camera _cam;
        bool _shown;

        /// <summary>The layer puts itself up the moment the game runs: no menu, no
        /// builder line, nothing for a scene to remember to do. A scene with nobody on
        /// foot in it simply never lends a card out.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindFirstObjectByType<DemoPeopleDebug>() != null) return;
            var go = new GameObject("People Debug");
            go.AddComponent<DemoPeopleDebug>();
        }

        void Start()
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogWarning("[RoadDemo] No TMP default font - the F3 people overlay is off.");
                Destroy(this);
                return;
            }

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            // Built ACTIVE and hidden at the end: a TMP text only loads its font in
            // OnEnable, which never runs under an inactive parent.
            _root = new GameObject("People Tags", typeof(RectTransform));
            _root.transform.SetParent(transform, false);
            for (int i = 0; i < Budget; i++) _tags.Add(BuildTag(i));
            _root.SetActive(false);
        }

        Tag BuildTag(int index)
        {
            var rect = DemoUi.NewRect("Tag " + index, _root.transform);
            rect.sizeDelta = new Vector2(CardWidth, CardHeight);
            // pivot at the bottom middle: the card stands ON the point over his head
            // rather than straddling it, so it never covers the man it describes
            rect.pivot = new Vector2(0.5f, 0f);

            var face = rect.gameObject.AddComponent<Image>();
            face.raycastTarget = false;
            DemoUi.Dress(face, DemoUi.Box, 13f, DemoUi.Panel);

            var tag = new Tag { Rect = rect };
            tag.Who = Row(rect, "Who", 15f, DemoUi.Gold, top: -6f, height: 20f, display: true);
            tag.State = Row(rect, "State", 13f, DemoUi.Accent, top: -25f, height: 18f);
            tag.Intent = Row(rect, "Intent", 12f, DemoUi.InkDim, top: -43f, height: 26f);
            tag.Intent.textWrappingMode = TextWrappingModes.Normal;
            tag.Intent.overflowMode = TextOverflowModes.Ellipsis;
            rect.gameObject.SetActive(false);
            return tag;
        }

        /// <summary>A line of the tag, hung from its top edge.</summary>
        TMP_Text Row(RectTransform card, string name, float size, Color colour,
            float top, float height, bool display = false)
        {
            var text = DemoUi.Text(card, name, size, colour,
                TextAlignmentOptions.TopLeft, display);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-24f, height);
            rect.anchoredPosition = new Vector2(0f, top);
            return text;
        }

        void Update()
        {
            if (_root == null || BookOpen) return;
            var kb = Keyboard.current;
            if (kb == null || !kb.f3Key.wasPressedThisFrame) return;
            _shown = !_shown;
            _root.SetActive(_shown);
            if (!_shown) Park(0);
            else _nextResort = 0f;   // the first frame after the key deals the pool
        }

        void LateUpdate()
        {
            if (!_shown || _root == null) return;

            bool showRoot = !BookOpen;
            if (_root.activeSelf != showRoot) _root.SetActive(showRoot);
            if (!showRoot) return;

            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) return;
            }

            if (Time.unscaledTime >= _nextResort)
            {
                _nextResort = Time.unscaledTime + ResortInterval;
                Deal();
            }
            Place();
        }

        /// <summary>Who carries a tag this time round, and what it says.</summary>
        void Deal()
        {
            _people.Clear();
            var everyone = PedestrianAgent.Everyone;
            for (int i = 0; i < everyone.Count; i++)
            {
                var person = everyone[i];
                if (person == null || person.Tf == null) continue;
                if (!person.Tf.gameObject.activeInHierarchy) continue;
                _people.Add(person);
            }
            if (_people.Count == 0) { Park(0); return; }

            // rank around where the camera LOOKS, not where it stands: the rig parks it
            // a couple of hundred metres back along its boom (DemoHeadlights)
            var eye = _cam.transform.position;
            var forward = _cam.transform.forward;
            if (forward.y < -0.05f && eye.y > 0f) eye += forward * (eye.y / -forward.y);

            DemoStreetLamps.Prepare(ref _key, ref _order, _people.Count);
            for (int i = 0; i < _people.Count; i++)
                _key[i] = (_people[i].Tf.position - eye).sqrMagnitude;
            DemoStreetLamps.Nearest(_key, _order, Budget);

            int want = Mathf.Min(Budget, Mathf.Min(_people.Count, _tags.Count));
            for (int rank = 0; rank < want; rank++)
            {
                var tag = _tags[rank];
                var person = _people[_order[rank]];
                tag.Person = person;
                tag.Who.SetText(person.DebugName);
                tag.State.SetText(person.DebugState + "  ·  " + person.DebugGait);
                tag.Intent.SetText(person.DebugIntent);
            }
            Park(want);
        }

        /// <summary>Every tag from here down has nobody.</summary>
        void Park(int from)
        {
            for (int i = from; i < _tags.Count; i++)
            {
                var tag = _tags[i];
                if (tag.Person == null && !tag.Rect.gameObject.activeSelf) continue;
                tag.Person = null;
                tag.Rect.gameObject.SetActive(false);
            }
        }

        /// <summary>Put each dealt tag over its man, this frame. Position only - the
        /// words were read when the pool was dealt, and a man crossing the ground
        /// between two deals is still doing what his tag says he is.</summary>
        void Place()
        {
            float w = Screen.width, h = Screen.height;
            for (int i = 0; i < _tags.Count; i++)
            {
                var tag = _tags[i];
                var person = tag.Person;
                if (person == null) continue;
                if (person.Tf == null)
                {
                    // his body has gone since the deal (a corpse struck off, a man
                    // taken off the street): the tag drops rather than hanging over
                    // the spot he used to stand on
                    tag.Person = null;
                    tag.Rect.gameObject.SetActive(false);
                    continue;
                }

                var screen = _cam.WorldToScreenPoint(person.Tf.position + Vector3.up * Overhead);
                bool on = screen.z > 0f &&
                          screen.x >= 0f && screen.x <= w &&
                          screen.y >= 0f && screen.y <= h;
                var go = tag.Rect.gameObject;
                if (go.activeSelf != on) go.SetActive(on);
                if (!on) continue;

                tag.Rect.position = new Vector3(screen.x, screen.y, 0f);
            }
        }
    }
}
