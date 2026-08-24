using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

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
    /// visible ground and the lens stops drawing.
    ///
    /// WHAT IS ASKED FOR, not what is guessed at. The sweep samples the ground the
    /// camera can actually see - a grid across the lower screen, each point dropped onto
    /// the street plane - and casts from each sample back at the lens. Everything tall
    /// it meets on the way is in the road, and hides. A sample that lands INSIDE a
    /// footprint is dropped: there is no pavement under a building, and casting from one
    /// would hide the building in front of it, then the one in front of that, until half
    /// the quarter had gone.
    ///
    /// HIDING, not fading. The city is one opaque atlas material and a few dozen tint
    /// variants of it, so per-building transparency does not exist without shader
    /// surgery. <see cref="ShadowCastingMode.ShadowsOnly"/> is the one per-renderer
    /// channel that does: the walls go, the shadow they lay across the street stays - so
    /// the light on the pavement does not jump every time a facade comes and goes - and
    /// the collider never moves, so bullets, sightlines and men on foot go on treating
    /// the invisible wall as the wall it still is.
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

        /// <summary>Whether a hidden building goes on laying its shadow. It does, by
        /// default: a facade that takes its shadow with it lights the whole street up the
        /// moment it goes, which reads far worse than the missing wall.</summary>
        public bool keepShadows = true;

        /// <summary>H toggles the whole thing while a scene is being looked at.</summary>
        public bool on = true;

        const float Chest = 1.2f;              // sample height: what a man on the pavement is
        const float Radius = 0.9f;             // fat enough that the cast is a person, not a pin
        const float BackOffset = 2f;           // a cast started inside a wall skips that wall; start behind the sample
        const float KeepHiddenSeconds = 0.35f; // longer than a full sweep, so a facade the last pass held does not blink
        const float RefreshSeconds = 0.2f;     // how long the whole grid takes to come round again
        const float StepCeiling = 1f / 30f;    // a slow frame gets a nominal frame's rows, never the whole grid
        const int Columns = 16, Rows = 10;
        const float TopFraction = 0.8f;        // above this the screen is distance and sky
        const float ReachBooms = 2.5f;         // ground further out than this many booms is not the street in front
        const float CastRange = 160f;
        const float IndoorProbe = 0.4f;

        // Everything except the crowd, the small props, the park-nav proxies and the
        // engine's own ignore layer: none of them is ever a building, and the crowd in
        // particular is a thousand capsules standing exactly where the samples land.
        static readonly int Mask = ~((1 << 2) | (1 << 8) | (1 << 10)
                                     | (1 << ScenePerf.PropLayer) | (1 << ScenePerf.CrowdLayer));

        struct Gone
        {
            public ShadowCastingMode Shadows;   // what the renderer cast before it went
            public bool ByShadow;               // how it was taken out, in case the flag is flipped meanwhile
            public float Seen;                  // when a sample last found it in the way
            public MergedChunk Chunk;           // the hold keeping its block in pieces, or none
        }

        static StreetCutaway _instance;

        Camera _cam;
        readonly Dictionary<MeshRenderer, Gone> _gone = new Dictionary<MeshRenderer, Gone>();
        readonly List<MeshRenderer> _lapsed = new List<MeshRenderer>();
        readonly Dictionary<Collider, MeshRenderer> _known = new Dictionary<Collider, MeshRenderer>();
        readonly RaycastHit[] _hits = new RaycastHit[128];
        readonly Collider[] _overlaps = new Collider[16];
        int _row;
        bool _cutting;

        /// <summary>Whether this collider belongs to a building the camera is currently
        /// seeing through. The collider stays solid on purpose - the wall is still a wall
        /// - but a CLICK must not be swallowed by something nobody can see: the card
        /// picker skips such hits, so the man standing behind the missing facade is the
        /// one who answers.</summary>
        public static bool Invisible(Collider collider)
        {
            var self = _instance;
            if (self == null || self._gone.Count == 0 || collider == null) return false;
            var mr = self.Building(collider);
            return mr != null && self._gone.ContainsKey(mr);
        }

        void Awake()
        {
            _instance = this;
            _cam = GetComponent<Camera>();
            if (rig == null) rig = GetComponent<DemoCamera>();
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
                _cutting = false;
                ShowEverything();
                return;
            }
            _cutting = true;

            Sweep();
            Restore();
        }

        void Sweep()
        {
            if (_cam.transform.forward.y >= -0.05f) return;   // looking along the ground: no near band to clear
            var lens = _cam.transform.position;

            float reach = Mathf.Max(40f, rig.distance * ReachBooms);
            float step = Mathf.Min(Time.deltaTime, StepCeiling);
            int rowsThisFrame = Mathf.Clamp(Mathf.CeilToInt(Rows * step / RefreshSeconds), 1, Rows);

            for (int r = 0; r < rowsThisFrame; r++)
            {
                float v = (_row + 0.5f) / Rows * TopFraction;
                for (int c = 0; c < Columns; c++)
                {
                    float u = (c + 0.5f) / Columns;
                    var ray = _cam.ViewportPointToRay(new Vector3(u, v, 0f));
                    if (ray.direction.y >= -0.001f) continue;

                    // The street plane, not a raycast down the same line: that would land
                    // on the roof of the very building being asked about. The grid is
                    // flat at zero by construction - the roads sit a few centimetres
                    // under it, the buildings stand on it.
                    float t = -ray.origin.y / ray.direction.y;
                    if (t <= 0.5f || t > reach) continue;

                    var sample = ray.origin + ray.direction * t + Vector3.up * Chest;
                    if (Indoors(sample)) continue;

                    var toLens = lens - sample;
                    float span = toLens.magnitude;
                    if (span < 1f) continue;
                    var dir = toLens / span;
                    int n = Physics.SphereCastNonAlloc(sample - dir * BackOffset, Radius, dir, _hits,
                                                       Mathf.Min(span + BackOffset - 1f, CastRange),
                                                       Mask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++) Hide(_hits[i].collider);
                }
                _row = (_row + 1) % Rows;
            }
        }

        /// <summary>Is the sample standing inside a building? Then it is not pavement and
        /// nobody is owed a view of it.</summary>
        bool Indoors(Vector3 point)
        {
            int n = Physics.OverlapSphereNonAlloc(point, IndoorProbe, _overlaps, Mask,
                                                  QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (Building(_overlaps[i]) != null) return true;
            return false;
        }

        void Hide(Collider collider)
        {
            var mr = Building(collider);
            if (mr == null) return;

            if (_gone.TryGetValue(mr, out var gone))
            {
                gone.Seen = Time.time;
                _gone[mr] = gone;
                return;
            }

            // Merged away: the renderer is off and its triangles are in a combined mesh,
            // so the whole block has to stand in pieces before this one building can go.
            // A chunk the merge has not finished with says no and is asked again on the
            // next sweep - a facade a fraction of a second late is nothing; a hole in the
            // city while the merge is mid-fold is not.
            MergedChunk chunk = null;
            if (!mr.enabled)
            {
                chunk = MergedChunk.Of(mr);
                if (chunk == null || !chunk.Hold()) return;
            }

            _gone[mr] = new Gone
            {
                Shadows = mr.shadowCastingMode, ByShadow = keepShadows, Seen = Time.time, Chunk = chunk,
            };
            if (keepShadows) mr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            else mr.enabled = false;
        }

        void Restore()
        {
            if (_gone.Count == 0) return;

            _lapsed.Clear();
            foreach (var pair in _gone)
                if (pair.Key == null || Time.time - pair.Value.Seen > KeepHiddenSeconds)
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

        void Show(MeshRenderer mr)
        {
            if (!_gone.TryGetValue(mr, out var gone)) return;
            if (mr != null)
            {
                if (gone.ByShadow) mr.shadowCastingMode = gone.Shadows;
                else mr.enabled = true;
            }
            // the hold goes last: releasing it may switch the piece off again, which is
            // exactly right once it is the merged mesh drawing the building instead
            if (gone.Chunk != null) gone.Chunk.Release();
            _gone.Remove(mr);
        }

        /// <summary>The building this collider is, or null for everything else. Every
        /// catalog bake is one GameObject carrying its own footprint box beside its own
        /// renderer, so there is no climbing to do - and the answer never changes, so it
        /// is settled once per collider and remembered.</summary>
        MeshRenderer Building(Collider collider)
        {
            if (collider == null) return null;
            if (_known.TryGetValue(collider, out var known)) return known;

            MeshRenderer found = null;
            if (collider.TryGetComponent<MeshRenderer>(out var mr)
                && mr.bounds.size.y >= minHeight
                && Ours(collider.transform))
                found = mr;
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
