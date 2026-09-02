using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    // Demo camera: WASD/arrows pan, Q/E rotate, mouse wheel zoom and right-drag
    // yaw. The shared CityViewConfig decides whether right-drag may also change pitch.
    // Uses the new Input System (the project runs InputSystem-only).
    public class DemoCamera : MonoBehaviour
    {
        public Vector3 pivot;
        public float distance = 170f;
        public float yaw = 35f;
        public float pitch = 52f;

        [SerializeField, HideInInspector] float _minimumPitch = CityViewConfig.MinimumStreetPitch;
        [SerializeField, HideInInspector] float _maximumPitch = CityViewConfig.MaximumStreetPitch;

        public float MinimumPitch => _minimumPitch;
        public float MaximumPitch => _maximumPitch;
        public bool PitchLocked => Mathf.Abs(_maximumPitch - _minimumPitch) < 0.01f;

        /// <summary>Apply the shared street-camera policy. Zero freedom fixes pitch at
        /// the configured angle while right-drag and Q/E continue to rotate yaw.</summary>
        public void ConfigurePitch(float centre, float freedom)
        {
            Vector2 range = CityViewConfig.ResolvePitchRange(centre, freedom);
            _minimumPitch = range.x;
            _maximumPitch = range.y;
            pitch = Mathf.Clamp(centre, _minimumPitch, _maximumPitch);
        }

        /// <summary>The boom at which the city stops being a place and becomes a
        /// PLAN: pull back past this and the printed map takes the screen, push in
        /// past it and the streets come back exactly where they were. The map is the
        /// same camera - same pivot, same wheel - drawn another way, so the two never
        /// disagree about where the player is looking.</summary>
        public float mapAt = CityViewConfig.DefaultMax3DDistance;

        /// <summary>Whether crossing <see cref="mapAt"/> hands the view to the turf
        /// map. Small review scenes reuse this camera without a map; for them the same
        /// WASD/orbit/wheel controls stay in 3D all the way to <see cref="mapCeiling"/>.</summary>
        public bool mapTransition = true;

        /// <summary>Metres of ground down the view per metre of boom once the map is
        /// up. The one number the plate's scale, the minimap's frame and the ceiling
        /// below are all read off, so the map opens showing exactly what a given click
        /// of the wheel always showed.</summary>
        public const float BoomToMetres = 1.15f;

        /// <summary>Proportional boom change per wheel event. Sign-only keeps a mouse
        /// wheel and a trackpad on the same zoom cadence.</summary>
        internal const float WheelZoomStep = 0.09f;

        /// <summary>Open the boom on something of a given SPAN - the widest side of
        /// what has to be in frame - and stay on the street side of the map line.
        ///
        /// A scene that lays out its own grid knows its span and nothing else, and the
        /// obvious "hold all of it" arithmetic quietly opens the map instead: past
        /// mapAt this camera is a PLAN, drawn with the culling mask at zero, so a
        /// large quarter came up as a flat blue rectangle with the whole city built and
        /// standing behind it. Every scene that frames itself goes through here.</summary>
        public void FrameSpan(float span, float fill = 0.8f, float floor = 110f)
        {
            float want = Mathf.Max(floor, fill * Mathf.Max(1f, span));
            float minimum = Mathf.Max(0.5f, minDistance);
            float maximum = mapTransition
                ? Mathf.Max(minimum, mapAt - 15f)
                : MaximumDistance;
            distance = Mathf.Clamp(want, minimum, maximum);
        }

        /// <summary>How close the wheel may bring it. Eighteen metres is a man's
        /// shoulder in the street and the right floor for the city, and it is far too
        /// far away for a bench that exists to have an animation looked at - so the
        /// benches lower it. It was a hard 18 in the clamp below, which is why the bike
        /// bench asking for 4.2 m quietly sat at 18.</summary>
        public float minDistance = 18f;

        /// <summary>How far back the wheel may go once the map is up. Set by
        /// <see cref="TurfMapHud"/> from the CITY's own size and a margin of country,
        /// so the last click of the wheel is the town filling the frame - the way the
        /// original's plan opens - and not the whole island with the streets a smudge
        /// in the middle of it.</summary>
        public float mapCeiling = 900f;

        /// <summary>Whether the map should be up: the boom is past the threshold.</summary>
        public bool MapOut => mapTransition && distance > mapAt;
        /// <summary>Transient deterministic-harness gate. Gameplay never sets this;
        /// it prevents stale editor input from contaminating a measured camera route.</summary>
        public bool SuppressInput { get; set; }

        /// <summary>THE RIGHT BUTTON IS SOMEBODY ELSE'S THIS DRAG. The overlay takes it
        /// while the player holds it on something to get behind and swings the pointer
        /// to turn his crew's cover (CrewOverlay's aim): the camera must not orbit under
        /// a preview he is aiming. Set on the press that starts the aim and cleared on
        /// the release that ends it - a static because there is one pointer, whatever
        /// scene's camera is reading it.</summary>
        public static bool RightDragTaken { get; set; }

        float MaximumDistance => mapTransition
            ? Mathf.Max(mapAt + 40f, mapCeiling)
            : Mathf.Max(Mathf.Max(0.5f, minDistance), mapCeiling);

        /// <summary>The shared wheel rule, including the street floor and map ceiling.
        /// TurfMapHud asks before moving its cursor anchor; the street applies it directly.</summary>
        internal float DistanceAfterWheel(float scroll) => Mathf.Clamp(
            distance * (1f - Mathf.Sign(scroll) * WheelZoomStep),
            Mathf.Max(0.5f, minDistance), MaximumDistance);

        /// <summary>Where the thing the camera is riding is now, or null when there is
        /// nothing left of it to watch. Null itself while the camera is the player's
        /// own - which is all the time until someone calls <see cref="Ride"/>.</summary>
        System.Func<Vector3?> _ride;

        /// <summary>Made on first OnGUI: GUI.skin exists only there.</summary>
        GUIStyle _zoomStyle;

        /// <summary>How quickly the pivot closes on what it rides. A man on foot is
        /// held dead centre; a car at speed runs a metre or two ahead of the picture -
        /// the camera eases after it instead of snapping about behind every turn.</summary>
        const float RideEase = 6f;

        /// <summary>The key line under the top bar. The road demo's by default; a
        /// scene with other clicks to explain writes its own.</summary>
        public string hint =
            "WASD/arrows: move   Q/E or right-click: rotate   wheel: zoom   " +
            "click a building: card   O: lot info   click a lieutenant: select   " +
            "right-click: send his crew   H: see through the near buildings   M: mute";

        /// <summary>Pixels the hint sits below the top of the screen (the road demo's
        /// top bar is 42 canvas-px on the 1080 reference height).</summary>
        public float hintTopPx = 42f;

        /// <summary>Whether the key line is drawn at all. Off: the road demo wants a
        /// clean picture, and IMGUI over the scene costs a little every frame besides.
        /// A scene that needs its controls explained turns it on.</summary>
        public bool showHint = false;

        /// <summary>Debug readout of the boom in the bottom-left corner: how far back
        /// the camera sits, and at what angles. On while the demos are being tuned -
        /// the numbers are the ones to quote when a zoom level looks wrong.</summary>
        public bool showZoom = false;

        /// <summary>The readout as last printed, and the rounded figures it was
        /// printed from: the line is only formatted again when one of them moves,
        /// not on every frame the camera stands still.</summary>
        string _zoomLine = "";
        int _zoomDistance = -1, _zoomPitch, _zoomYaw, _zoomX, _zoomZ;
        bool _zoomRiding;

        /// <summary>The book owns the screen while it is open - its own keys, its own
        /// wheel (the roster scrolls on it) - and the map takes the half of the world
        /// that is still visible. Nothing the player does over either may also steer
        /// the camera underneath.</summary>
        static bool BookOpen => LivingCity.UI.PersonnelAlmanac.IsOpen;

        /// <summary>Something already owns the whole screen: the book, or the plan
        /// the wheel brings up past mapAt. Neither wants the street's furniture
        /// printed across it - and IMGUI in particular prints over every canvas in
        /// the scene, so a hint meant for the street lands on the paper itself.
        /// </summary>
        static bool ScreenTaken => BookOpen || TurfMapHud.IsOpen;

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            var kb = BookOpen || SuppressInput ? null : Keyboard.current;
            if (kb != null)
            {
                // T owns one shared information panel at every zoom level. Keeping the
                // same switch in the street and on the turf map means its time scrubber
                // never disappears merely because the camera crossed the map line.
                if (kb.tKey.wasPressedThisFrame)
                    showZoom = !showZoom;

                Vector2 pan = Vector2.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) pan.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) pan.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed)
                    pan.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) pan.x -= 1f;
                if (pan != Vector2.zero)
                {
                    // Both the street and the turf plan turn with this heading, so the
                    // keys always move by the picture the player is looking at.
                    var flat = Quaternion.Euler(0f, yaw, 0f);
                    pivot += (flat * Vector3.forward * pan.y + flat * Vector3.right * pan.x)
                             * (distance * 0.55f * dt);
                    _ride = null; // the player moved the camera himself: the ride is over
                }
                // Q/E turn the one shared camera at every zoom level. On the turf map
                // the paper follows this yaw; the right button stays reserved for orders.
                if (kb.qKey.isPressed) yaw -= 70f * dt;
                if (kb.eKey.isPressed) yaw += 70f * dt;
            }

            var mouse = BookOpen || SuppressInput ? null : Mouse.current;
            if (mouse != null)
            {
                // While the turf map is visible it owns the wheel: it has to pin the
                // ground below the cursor and hand that exact point back to this camera.
                // In the street the ordinary centre-based boom remains ours.
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f && !TurfMapHud.IsOpen)
                    distance = DistanceAfterWheel(scroll);
                if (mouse.rightButton.isPressed && !MapOut && !RightDragTaken)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    yaw += d.x * 0.25f;
                    if (!PitchLocked)
                        pitch -= d.y * 0.2f;
                }
            }

            // Riding a crew: the pivot goes where he goes, and keeps going, until the
            // player pans off it. Turning and zooming leave the ride alone - they only
            // look at the same man from somewhere else.
            if (_ride != null)
            {
                var at = _ride();
                if (at.HasValue) pivot = Vector3.Lerp(pivot, at.Value, 1f - Mathf.Exp(-RideEase * dt));
                else _ride = null; // nothing left of him to watch; the camera stays where it stopped
            }

            // 18 m is a man's shoulder; the ceiling is the map's, which is the TOWN
            // and a margin of country round it once the turf map has measured it
            // (900 m until it has). The floor under that ceiling is the line the map comes up
            // at and not a fixed 900 m: a small city's last click of the wheel must be
            // allowed to stop at the city, or the plan opens on a stamp of streets in a
            // screenful of sea.
            distance = Mathf.Clamp(distance, Mathf.Max(0.5f, minDistance),
                MaximumDistance);
            pitch = Mathf.Clamp(pitch, _minimumPitch, _maximumPitch);

            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(pivot + rot * new Vector3(0f, 0f, -distance), rot);
        }

        /// <summary>Is the camera riding someone?</summary>
        public bool Riding => _ride != null;

        /// <summary>Ride a man - or the car he is in: the camera goes to him now and
        /// stays with him wherever he walks or drives, until the player pans away. The
        /// call gives his place each frame, and null once there is nothing left of him
        /// to follow.</summary>
        public void Ride(System.Func<Vector3?> where)
        {
            _ride = where;
            if (where == null) return;
            var at = where();
            if (at.HasValue) pivot = at.Value;
        }

        /// <summary>Focus one street crew using the same rule everywhere: its car while
        /// the lieutenant is riding, otherwise the lieutenant, otherwise the first man
        /// still represented on the street. Distance is deliberately preserved, so the
        /// focus works at both the 3D and turf-map zoom levels.</summary>
        public void Ride(DemoCrews.Unit unit)
        {
            if (unit == null)
            {
                Drop();
                return;
            }
            Ride(() => FocusOf(unit));
        }

        static Vector3? FocusOf(DemoCrews.Unit unit)
        {
            if (unit == null) return null;
            if (unit.Car != null && unit.Car.Tf != null) return unit.Car.Position;
            if (unit.Boss != null && unit.Boss.Tf != null) return unit.Boss.Tf.position;
            foreach (var man in unit.All())
                if (man != null && man.Tf != null) return man.Tf.position;
            return null;
        }

        /// <summary>Let go - the camera is the player's again.</summary>
        public void Drop() => _ride = null;

        /// <summary>Shove the pivot by so many metres of ground, east and north: the
        /// map's drag, which is the player moving the camera by hand and so ends any
        /// ride the same way the keys do.</summary>
        public void PanBy(Vector2 metres)
        {
            if (metres == Vector2.zero) return;
            pivot += new Vector3(metres.x, 0f, metres.y);
            _ride = null;
        }

        void OnGUI()
        {
            // IMGUI prints over every canvas in the scene, so this would land on the
            // open book, or on the plan - and none of what the hint names works on
            // either of them anyway.
            // The debug readout is useful on the map itself; only the book owns the
            // screen completely and should suppress it.
            if (BookOpen)
                return;

            if (showHint && !TurfMapHud.IsOpen)
            {
                // below the top bar, which spans the full width at 42 canvas-px
                // (reference height 1080) - convert to real screen pixels
                float barPx = UnityEngine.Screen.height / 1080f * hintTopPx;
                GUI.Label(new Rect(12f, barPx + 6f, 1400f, 24f), hint);
            }

            if (showZoom)
            {
                // top-right, over the map: the boom in metres
                // is what a "too close / too far" report needs to be reproducible, and
                // the angles go with it because the same distance reads differently
                // from 22 degrees than from 82. Two and a half times the IMGUI default,
                // because at the default nobody could read what it said.
                if (_zoomStyle == null)
                    _zoomStyle = new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = FontStyle.Bold };

                int d = Mathf.RoundToInt(distance * 10f);
                int p = Mathf.RoundToInt(pitch);
                int y = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f));
                int px = Mathf.RoundToInt(pivot.x);
                int pz = Mathf.RoundToInt(pivot.z);
                bool riding = _ride != null;
                if (d != _zoomDistance || p != _zoomPitch || y != _zoomYaw ||
                    px != _zoomX || pz != _zoomZ || riding != _zoomRiding)
                {
                    _zoomDistance = d;
                    _zoomPitch = p;
                    _zoomYaw = y;
                    _zoomX = px;
                    _zoomZ = pz;
                    _zoomRiding = riding;
                    _zoomLine = string.Format(
                        "zoom {0:0.0} m   pitch {1}   yaw {2}   pivot {3}, {4}{5}",
                        d / 10f, p, y, px, pz, riding ? "   [riding]" : string.Empty);
                }

                string map = TurfMapHud.IsOpen ? "   MAP" : string.Empty;
                string displayLine = _zoomLine + map;
                var at = new Rect(UnityEngine.Screen.width - 760f, 14f, 746f, 44f);
                var was = GUI.color;
                GUI.color = new Color(0.15f, 1f, 0.05f, 1f);
                GUI.Label(at, displayLine, _zoomStyle);
                GUI.color = was;
            }
        }

        /// <summary>
        /// THE RIG IS THE MAIN CAMERA, and nothing else in the scene may be.
        ///
        /// Camera.main is "the first ENABLED camera tagged MainCamera", and a scene made
        /// from Unity's own template ships with one - "Main Camera", parked at (0, 1, -10)
        /// looking down +Z. The builders stand their own rig up beside it and tag that
        /// MainCamera too; the rig has the higher depth so it is what the player SEES,
        /// while Camera.main quietly keeps answering with the forgotten one at the origin.
        ///
        /// Everything that turns the world into pixels then lies. Every overlay marker
        /// (CrewOverlay, CityOverlayHud, the police overlay, the cards) projects through
        /// the wrong lens, so the dots stand over nobody and a card wanders the screen;
        /// and every pick - the right-click that sends a crew somewhere - casts its ray
        /// from the origin, so the men walk off to a point nobody clicked. MiniCoreDemo
        /// had exactly that camera in it; CoreDemo, built as a bare scene, did not, which
        /// is why the same city behaved in one scene and not in the other.
        ///
        /// So the rig retires them: untagged and switched off, named in the log so the
        /// scene can be tidied. Cameras that never claimed the tag - a portrait studio,
        /// a crew feed - are nobody's business here and are left alone.
        /// </summary>
        public static void ClaimMainCamera(Camera rig)
        {
            if (rig == null) return;
            var all = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var other in all)
            {
                if (other == null || other == rig) continue;
                if (!other.CompareTag("MainCamera")) continue;
                other.tag = "Untagged";
                other.enabled = false;
                Debug.LogWarning($"[RoadDemo] '{other.name}' was also tagged MainCamera and " +
                                 "would have answered Camera.main instead of the rig - it is " +
                                 "untagged and switched off. Take it out of the scene.", other);
            }
        }
    }

    /// <summary>Transient play-mode harness for measuring streaming under the same
    /// continuous pivot speed as one held WASD key. It is created only by the editor
    /// performance command and destroys itself after one route.</summary>
    public sealed class DemoCameraStreamingStress : MonoBehaviour
    {
        public sealed class StressReport
        {
            public int Frames;
            public float DurationSeconds;
            public float AverageFrameMs;
            public float P95FrameMs;
            public float P99FrameMs;
            public float WorstFrameMs;
            public int Over16Ms;
            public int Over33Ms;
            public int Over50Ms;
            public int Gen0Collections;
            public int MinimapUploads;
            public float WorstMinimapUploadFrameMs;
            public int PeakActiveViews;
            public int PeakPendingViews;
        }

        static StressReport _last;
        public static StressReport Last => _last;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => _last = null;

        DemoCamera _rig;
        Vector3[] _route;
        int _next;
        float _beganAt, _measureAt, _worst, _worstUpload;
        int _over16, _over33, _over50, _gen0At, _uploadsAt, _lastUploads;
        int _peakActive, _peakPending;
        TurfMinimap _minimap;
        CityBlockRecycler[] _recyclers;
        readonly System.Collections.Generic.List<float> _frames =
            new System.Collections.Generic.List<float>(2048);

        public int Frames => _frames.Count;
        public float ElapsedSeconds => Mathf.Max(0f, Time.unscaledTime - _beganAt);
        public float WorstFrameMs => _worst;

        public void Begin(DemoCamera rig)
        {
            _rig = rig;
            _rig.SuppressInput = true;
            _rig.Drop();
            var start = rig.pivot;
            _route = new[]
            {
                new Vector3(300f, start.y, 700f),
                new Vector3(700f, start.y, 700f),
                new Vector3(700f, start.y, 1100f),
                new Vector3(300f, start.y, 1100f),
                start,
            };
            _next = 0;
            _last = null;
            _frames.Clear();
            _beganAt = Time.unscaledTime;
            _measureAt = _beganAt + 0.75f;
            _gen0At = System.GC.CollectionCount(0);
            _minimap = FindAnyObjectByType<TurfMinimap>();
            _recyclers = FindObjectsByType<CityBlockRecycler>();
            _uploadsAt = _lastUploads = _minimap != null ? _minimap.Uploads : 0;
        }

        void Update()
        {
            if (_rig == null || _route == null || _next >= _route.Length)
            {
                Finish();
                return;
            }

            Measure();
            float metresPerSecond = Mathf.Max(10f, _rig.distance * 0.55f);
            _rig.pivot = Vector3.MoveTowards(
                _rig.pivot, _route[_next], metresPerSecond * Time.unscaledDeltaTime);
            if ((_rig.pivot - _route[_next]).sqrMagnitude < 0.01f) _next++;
        }

        void Measure()
        {
            if (Time.unscaledTime < _measureAt) return;
            float ms = Time.unscaledDeltaTime * 1000f;
            _frames.Add(ms);
            _worst = Mathf.Max(_worst, ms);
            if (ms > 16.667f) _over16++;
            if (ms > 33.333f) _over33++;
            if (ms > 50f) _over50++;

            int uploads = _minimap != null ? _minimap.Uploads : _lastUploads;
            if (uploads != _lastUploads)
            {
                _worstUpload = Mathf.Max(_worstUpload, ms);
                _lastUploads = uploads;
            }

            if (_recyclers == null) return;
            int active = 0, pending = 0;
            for (int i = 0; i < _recyclers.Length; i++)
            {
                var recycler = _recyclers[i];
                if (recycler == null) continue;
                active += recycler.ActiveViews;
                pending += recycler.PendingViews + recycler.ComposingViews + recycler.AttachingViews;
            }
            _peakActive = Mathf.Max(_peakActive, active);
            _peakPending = Mathf.Max(_peakPending, pending);
        }

        void Finish()
        {
            if (_frames.Count > 0)
            {
                _frames.Sort();
                float total = 0f;
                for (int i = 0; i < _frames.Count; i++) total += _frames[i];
                _last = new StressReport
                {
                    Frames = _frames.Count,
                    DurationSeconds = Mathf.Max(0f, Time.unscaledTime - _measureAt),
                    AverageFrameMs = total / _frames.Count,
                    P95FrameMs = Percentile(0.95f),
                    P99FrameMs = Percentile(0.99f),
                    WorstFrameMs = _worst,
                    Over16Ms = _over16,
                    Over33Ms = _over33,
                    Over50Ms = _over50,
                    Gen0Collections = System.GC.CollectionCount(0) - _gen0At,
                    MinimapUploads = Mathf.Max(0, _lastUploads - _uploadsAt),
                    WorstMinimapUploadFrameMs = _worstUpload,
                    PeakActiveViews = _peakActive,
                    PeakPendingViews = _peakPending,
                };
                Debug.Log($"[StreamingStress] {_last.Frames} frames, avg {_last.AverageFrameMs:0.00} ms, " +
                          $"p95 {_last.P95FrameMs:0.00}, p99 {_last.P99FrameMs:0.00}, " +
                          $"worst {_last.WorstFrameMs:0.00}; >33 ms {_last.Over33Ms}, " +
                          $"minimap uploads {_last.MinimapUploads}.");
            }
            Destroy(gameObject);

            float Percentile(float share)
            {
                int at = Mathf.Clamp(Mathf.CeilToInt(_frames.Count * share) - 1,
                    0, _frames.Count - 1);
                return _frames[at];
            }
        }

        void OnDestroy()
        {
            if (_rig != null) _rig.SuppressInput = false;
        }
    }
}
