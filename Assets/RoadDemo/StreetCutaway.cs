using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    /// <summary>
    /// The Gangsters trick: bring the boom down into the street and whatever stands
    /// between the camera and the pavement gets out of the way.
    ///
    /// From up at the map the city is roofs and that is the picture. Down at a man's
    /// height it is not: an isometric camera at fifty degrees puts a whole row of
    /// facades across the near half of the screen, and the street the player came down
    /// to watch - his crew on the corner, the pavement, the car pulling up - is behind
    /// them. Gangsters answered it by taking the near buildings out, and this is that
    /// answer: below <see cref="cutIn"/> metres of boom, every building standing between
    /// visible ground and the lens becomes a low, closed cutaway footprint.
    ///
    /// WHAT IS ASKED FOR, not what is guessed at. The sweep samples the ground the
    /// camera can actually see - a grid across the lower screen, each point dropped onto
    /// the street plane - and casts from each sample back at the lens. Everything tall
    /// it meets on the way is in the road, and hides. A sample that lands INSIDE a
    /// footprint is dropped: there is no pavement under a building, and casting from one
    /// would hide the building in front of it, then the one in front of that, until half
    /// the quarter had gone.
    ///
    /// <see cref="BuildingCutaway"/> groups every piece of one logical building and applies
    /// the shared camera-facing opacity gradient. The shadow stays, the collider never
    /// moves, and bullets, sightlines and men on foot go on treating the faded wall as the
    /// wall it still is. If the gradient cannot be prepared, the established shadows-only
    /// cut remains the safe fallback.
    ///
    /// The merge is the complication. By the time the player is down in the street the
    /// block is one combined mesh and its buildings have no renderers of their own left
    /// drawing anything, so the chunk is asked to stand in pieces for as long as
    /// anything in it is hidden - see <see cref="MergedChunk"/>. Only the blocks under
    /// the camera are ever held, and only while the boom is down.
    /// </summary>
    public sealed class StreetCutaway : MonoBehaviour
    {
        /// <summary>The boom this reads. Left unset it is looked for on the same object.</summary>
        public DemoCamera rig;

        /// <summary>Where the buildings that may be hidden live - the block bakes and the
        /// quarters. Empty means anywhere, which in this scene would also offer the
        /// camera a ship and a hangar.</summary>
        public Transform[] roots;

        /// <summary>The boom at which the near buildings start getting out of the way,
        /// and the boom at which they come back. The gap is hysteresis: a wheel resting
        /// exactly on the line must not flick a row of facades on and off.</summary>
        public float cutIn = 55f, cutOut = 68f;

        /// <summary>How tall a thing has to be before it counts as being in the way. A
        /// kiosk, a parked car and a skip are all in the same flat category as far as the
        /// street behind them is concerned.</summary>
        public float minHeight = ScenePerf.CutawayHeight;

        /// <summary>Absolute height of the closed footprint left behind. A percentage of
        /// building height made towers leave a two-storey wall; street readability needs
        /// the same low rim for a shop and a tower. Retained for the fallback cut path.</summary>
        public float proxyHeight = 0.95f;

        /// <summary>Shared gradient strength for an occluding building. The approved city
        /// value is 1.42 (142%): the rear is clear and the low front remains.</summary>
        [Range(0f, 2f)] public float gradientAmount = 1.42f;

        /// <summary>How long an occluder remains cut after the last sample met it. Longer
        /// than the grid refresh so rotating or resting on a facade edge cannot blink it.</summary>
        public float keepHiddenSeconds = 0.35f;

        /// <summary>Direct crew samples per frame. They guarantee that the selected crew
        /// is revealed immediately; the slower screen grid supplies the surrounding street.</summary>
        [Range(0, 12)] public int crewSamplesPerFrame = 6;

        /// <summary>Whether a hidden building goes on laying its shadow. It does, by
        /// default: a facade that takes its shadow with it lights the whole street up the
        /// moment it goes, which reads far worse than the missing wall.</summary>
        public bool keepShadows = true;

        /// <summary>H toggles the whole thing while a scene is being looked at. The shared
        /// city-view config owns the production default.</summary>
        public bool on = true;

        const float Chest = 1.2f;              // sample height: what a man on the pavement is
        const float Radius = 0.9f;             // fat enough that the cast is a person, not a pin
        const float BackOffset = 2f;           // a cast started inside a wall skips that wall; start behind the sample
        const int GridSamplesPerFrame = 6;      // bounded physics work, independent of frame duration
        const float PointerInterval = 0.08f;
        const int Columns = 16, Rows = 10;
        const float TopFraction = 0.8f;        // above this the screen is distance and sky
        const float ReachBooms = 2.5f;         // ground further out than this many booms is not the street in front
        const float CastRange = 160f;
        const float IndoorProbe = 0.4f;

        // Everything except the crowd, props, trees/lamps/poles, park-nav proxies and the
        // engine's own ignore layer: none of them is ever a building, and the crowd in
        // particular is a thousand capsules standing exactly where the samples land.
        static readonly int Mask = ~((1 << 2) | (1 << 8) | (1 << 10)
                                     | (1 << ScenePerf.PropLayer) | (1 << ScenePerf.CrowdLayer)
                                     | (1 << ScenePerf.MidLayer));

        static StreetCutaway _instance;

        Camera _cam;
        DemoCrews _crews;
        readonly Dictionary<BuildingCutaway, float> _gone =
            new Dictionary<BuildingCutaway, float>();
        readonly List<BuildingCutaway> _lapsed = new List<BuildingCutaway>();
        readonly Dictionary<Collider, BuildingCutaway> _known =
            new Dictionary<Collider, BuildingCutaway>();
        readonly RaycastHit[] _hits = new RaycastHit[128];
        readonly Collider[] _overlaps = new Collider[16];
        int _gridSample;
        bool _cutting;
        float _crewLookupAt;
        float _nextPointerAt;

        public int HiddenBuildings => _gone.Count;
        public int CachedColliderAnswers => _known.Count;

        /// <summary>Whether this collider belongs to a building the camera is currently
        /// seeing through. The collider stays solid on purpose - the wall is still a wall
        /// - but a CLICK must not be swallowed by something nobody can see: the card
        /// picker skips such hits, so the man standing behind the missing facade is the
        /// one who answers.</summary>
        public static bool Invisible(Collider collider)
        {
            var self = _instance;
            if (self == null || self._gone.Count == 0 || collider == null) return false;
            return BuildingCutaway.Invisible(collider);
        }

        void Awake()
        {
            _instance = this;
            _cam = GetComponent<Camera>();
            if (rig == null) rig = GetComponent<DemoCamera>();
            _crews = FindAnyObjectByType<DemoCrews>();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void OnDisable() => ShowEverything();

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.hKey.wasPressedThisFrame)
            {
                on = !on;
                Debug.Log($"[RoadDemo] the cutaway is {(on ? "on" : "off")}");
            }

            if (_cam == null || rig == null) return;

            // Off the moment the picture stops being a street: the map is a plan drawn
            // from a boom of hundreds of metres, and nothing is in anybody's way up there.
            // and off while the city is folding itself in: the merge reads a renderer's
            // shadow mode into the key it groups by, so a facade hidden mid-fold would be
            // merged as a shadows-only group and never seen again (ScenePerf.Merging).
            bool want = on && !rig.MapOut && !ScenePerf.Merging
                        && rig.distance <= (_cutting ? cutOut : cutIn);
            if (!want)
            {
                bool wasCutting = _cutting;
                _cutting = false;
                ShowEverything();
                if (wasCutting) _known.Clear();
                return;
            }
            _cutting = true;

            // Recycled colliders unregister themselves, but a negative lookup has no
            // owner to do that for it. Keep this convenience cache bounded while a long
            // pan visits the whole city.
            if (_known.Count > 2048) _known.Clear();

            SweepPointer();
            SweepCrews();
            Sweep();
            Restore();
        }

        /// <summary>The order cursor gets one exact sample every frame. The background
        /// grid makes the whole near street readable; this one makes the specific patch
        /// the player is about to click readable without waiting for its row to come round.</summary>
        void SweepPointer()
        {
            if (Time.unscaledTime < _nextPointerAt) return;
            _nextPointerAt = Time.unscaledTime + PointerInterval;
            var mouse = Mouse.current;
            if (mouse == null) return;
            var screen = mouse.position.ReadValue();
            if (!_cam.pixelRect.Contains(screen)) return;

            var ray = _cam.ScreenPointToRay(screen);
            if (ray.direction.y >= -0.001f) return;
            float t = -ray.origin.y / ray.direction.y;
            float reach = Mathf.Max(40f, rig.distance * ReachBooms);
            if (t <= 0.5f || t > reach) return;

            var sample = ray.origin + ray.direction * t + Vector3.up * Chest;
            if (!Indoors(sample)) SweepSubject(sample);
        }

        /// <summary>The screen grid turns the street into readable context over a fraction
        /// of a second. A crew cannot wait for that pass: cast straight from every visible
        /// selected member first, then spend the remaining bounded samples on other action
        /// already visible in the camera.</summary>
        void SweepCrews()
        {
            if (_crews == null && Time.unscaledTime >= _crewLookupAt)
            {
                _crewLookupAt = Time.unscaledTime + 2f;
                _crews = FindAnyObjectByType<DemoCrews>();
            }
            if (_crews == null) return;

            int left = Mathf.Clamp(crewSamplesPerFrame, 0, 12);
            var selected = _crews.Selected;
            SweepUnit(selected, ref left);
        }

        void SweepUnit(DemoCrews.Unit unit, ref int left)
        {
            if (unit == null || left <= 0) return;
            if (unit.Car != null && left > 0)
            {
                if (SweepSubject(unit.Car.Position + Vector3.up * Chest)) left--;
            }
            foreach (var man in unit.All())
            {
                if (left <= 0) break;
                if (man == null || man.Dead || man.Tf == null || !man.Tf.gameObject.activeInHierarchy)
                    continue;
                if (SweepSubject(man.ChestPosition)) left--;
            }
        }

        bool SweepSubject(Vector3 sample)
        {
            var viewport = _cam.WorldToViewportPoint(sample);
            if (viewport.z <= 0f || viewport.x < -0.08f || viewport.x > 1.08f ||
                viewport.y < -0.08f || viewport.y > 1.08f)
                return false;

            var toLens = _cam.transform.position - sample;
            float span = toLens.magnitude;
            if (span < 1f) return false;
            var dir = toLens / span;
            int count = Physics.SphereCastNonAlloc(
                sample - dir * BackOffset, Radius, dir, _hits,
                Mathf.Min(span + BackOffset - 1f, CastRange), Mask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) Hide(_hits[i].collider);
            return true;
        }

        void Sweep()
        {
            if (_cam.transform.forward.y >= -0.05f) return;   // looking along the ground: no near band to clear
            var lens = _cam.transform.position;

            float reach = Mathf.Max(40f, rig.distance * ReachBooms);
            int total = Columns * Rows;
            for (int sampleIndex = 0; sampleIndex < GridSamplesPerFrame; sampleIndex++)
            {
                int sample = _gridSample++ % total;
                int row = sample / Columns;
                int column = sample - row * Columns;
                float v = (row + 0.5f) / Rows * TopFraction;
                float u = (column + 0.5f) / Columns;
                var ray = _cam.ViewportPointToRay(new Vector3(u, v, 0f));
                if (ray.direction.y >= -0.001f) continue;

                // The street plane, not a raycast down the same line: that would land
                // on the roof of the very building being asked about. The grid is
                // flat at zero by construction - the roads sit a few centimetres
                // under it, the buildings stand on it.
                float t = -ray.origin.y / ray.direction.y;
                if (t <= 0.5f || t > reach) continue;

                var point = ray.origin + ray.direction * t + Vector3.up * Chest;
                if (Indoors(point)) continue;

                var toLens = lens - point;
                float span = toLens.magnitude;
                if (span < 1f) continue;
                var dir = toLens / span;
                int n = Physics.SphereCastNonAlloc(point - dir * BackOffset, Radius, dir, _hits,
                                                   Mathf.Min(span + BackOffset - 1f, CastRange),
                                                   Mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++) Hide(_hits[i].collider);
            }
        }

        /// <summary>Is the sample standing inside a building? Then it is not pavement and
        /// nobody is owed a view of it.</summary>
        bool Indoors(Vector3 point)
        {
            int n = Physics.OverlapSphereNonAlloc(point, IndoorProbe, _overlaps, Mask,
                                                  QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (Occluder(_overlaps[i]) != null) return true;
            return false;
        }

        void Hide(Collider collider)
        {
            var building = Occluder(collider);
            if (building == null) return;

            if (_gone.ContainsKey(building))
            {
                _gone[building] = Time.unscaledTime;
                return;
            }

            // The group owns merged-chunk holds and all renderers belonging to this one
            // logical building. A merge still folding returns false and is asked again.
            if (!building.Cut(keepShadows, proxyHeight, gradientAmount)) return;
            _gone[building] = Time.unscaledTime;
        }

        void Restore()
        {
            if (_gone.Count == 0) return;

            _lapsed.Clear();
            foreach (var pair in _gone)
                if (pair.Key == null ||
                    Time.unscaledTime - pair.Value > keepHiddenSeconds)
                    _lapsed.Add(pair.Key);

            for (int i = 0; i < _lapsed.Count; i++) Show(_lapsed[i]);
            _lapsed.Clear();
        }

        void ShowEverything()
        {
            if (_gone.Count == 0) return;
            _lapsed.Clear();
            foreach (var pair in _gone) _lapsed.Add(pair.Key);
            for (int i = 0; i < _lapsed.Count; i++) Show(_lapsed[i]);
            _lapsed.Clear();
        }

        void Show(BuildingCutaway building)
        {
            if (!_gone.ContainsKey(building)) return;
            if (building != null) building.Restore();
            _gone.Remove(building);
        }

        /// <summary>The logical building this collider belongs to, or null. Explicitly
        /// composed buildings register all their pieces; old catalogue bakes fall back to
        /// the established collider-and-renderer-on-one-object contract.</summary>
        BuildingCutaway Occluder(Collider collider)
        {
            if (collider == null) return null;
            if (_known.TryGetValue(collider, out var known))
            {
                if (ReferenceEquals(known, null)) return null;
                if (known != null && BuildingCutaway.RegisteredTo(collider, known)) return known;
                _known.Remove(collider);
            }

            BuildingCutaway found = null;
            if (Ours(collider.transform))
                found = BuildingCutaway.Resolve(collider, minHeight, proxyHeight);
            _known[collider] = found;
            return found;
        }

        bool Ours(Transform t)
        {
            if (roots == null || roots.Length == 0) return true;
            for (int i = 0; i < roots.Length; i++)
                if (roots[i] != null && t.IsChildOf(roots[i])) return true;
            return false;
        }
    }
}
