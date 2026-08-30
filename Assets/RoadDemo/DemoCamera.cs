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
            distance = Mathf.Clamp(want, Mathf.Max(0.5f, minDistance), mapAt - 15f);
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
        public bool MapOut => distance > mapAt;

        /// <summary>The shared wheel rule, including the street floor and map ceiling.
        /// TurfMapHud asks before moving its cursor anchor; the street applies it directly.</summary>
        internal float DistanceAfterWheel(float scroll) => Mathf.Clamp(
            distance * (1f - Mathf.Sign(scroll) * WheelZoomStep),
            Mathf.Max(0.5f, minDistance), Mathf.Max(mapAt + 40f, mapCeiling));

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
            var kb = BookOpen ? null : Keyboard.current;
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

            var mouse = BookOpen ? null : Mouse.current;
            if (mouse != null)
            {
                // While the turf map is visible it owns the wheel: it has to pin the
                // ground below the cursor and hand that exact point back to this camera.
                // In the street the ordinary centre-based boom remains ours.
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f && !TurfMapHud.IsOpen)
                    distance = DistanceAfterWheel(scroll);
                if (mouse.rightButton.isPressed && !MapOut)
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
                Mathf.Max(mapAt + 40f, mapCeiling));
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
    }

    /// <summary>Transient play-mode harness for measuring streaming under the same
    /// continuous pivot speed as one held WASD key. It is created only by the editor
    /// performance command and destroys itself after one route.</summary>
    public sealed class DemoCameraStreamingStress : MonoBehaviour
    {
        DemoCamera _rig;
        Vector3[] _route;
        int _next;

        public void Begin(DemoCamera rig)
        {
            _rig = rig;
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
        }

        void Update()
        {
            if (_rig == null || _route == null || _next >= _route.Length)
            {
                Destroy(gameObject);
                return;
            }
            float metresPerSecond = Mathf.Max(10f, _rig.distance * 0.55f);
            _rig.pivot = Vector3.MoveTowards(
                _rig.pivot, _route[_next], metresPerSecond * Time.unscaledDeltaTime);
            if ((_rig.pivot - _route[_next]).sqrMagnitude < 0.01f) _next++;
        }
    }
}
