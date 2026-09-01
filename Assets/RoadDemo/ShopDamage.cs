using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What a bomb does to a shopfront. A grenade on the doorstep sets the ground floor
    /// alight - flames licking up the storefront and a glow on the street for the best
    /// part of half a minute - and when the fire has burnt itself out the premises are
    /// boarded up: planks nailed across the ground-floor windows, the way a gutted shop
    /// stands while somebody decides whether to reopen it.
    ///
    /// A shop is only ever done once (GangFront.Damaged): a second charge on a already
    /// boarded front does nothing new. The geometry it needs is only the doorstep and
    /// which way it faces (GangFront.Door / Outward) - it lays a fixed run of boards
    /// across the front rather than measuring a merged building it may no longer find
    /// the renderers of.
    /// </summary>
    public static class ShopDamage
    {
        /// <summary>How near a blast must fall to a shop's door to set it alight.</summary>
        public const float ScorchRange = 8f;

        /// <summary>Seconds the front burns before it is boarded up.</summary>
        public const float BurnFor = 22f;

        const float StoreWidth = 7f;    // metres of frontage the boards cover
        const float StoreHeight = 2.9f; // the ground floor
        const float BoardOutset = 0.16f; // just proud of the facade, on the street-facing side

        static Transform _root;
        static Material _fire, _board, _smoke;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() { _root = null; _fire = _board = _smoke = null; }

        static Transform Root()
        {
            if (_root == null) _root = new GameObject("Shop Damage").transform;
            return _root;
        }

        /// <summary>Any shopfront caught in a blast at <paramref name="at"/> is set alight.
        /// Called from Explosion.Blow, so a grenade thrown at a door, or a car blown up
        /// beside one, both scorch the shop behind it.</summary>
        public static void ScorchNear(Vector3 at, float groundY)
        {
            var all = GangFront.All;
            float r2 = ScorchRange * ScorchRange;
            for (int i = 0; i < all.Count; i++)
            {
                var f = all[i];
                if (f == null || f.Damaged) continue;
                if ((f.Door - at).sqrMagnitude <= r2) Scorch(f, groundY);
            }
        }

        /// <summary>Set this shop alight and, once it has burnt, board it up. Does nothing
        /// to a shop already done.</summary>
        public static void Scorch(GangFront front, float groundY)
        {
            if (front == null || front.Damaged) return;
            front.Damaged = true;
            front.Boarded = false;

            var go = new GameObject("Burning · " + front.GangName);
            go.transform.SetParent(Root(), false);
            var fire = go.AddComponent<ShopFire>();
            fire.Begin(front, groundY, FireMaterial(), SmokeMaterial(), BoardMaterial());

            if (DriveTrace.On)
                DriveTrace.Event("bomb", "shop", front.GangName + "'s front set alight");
        }

        // -------------------------------------------------- ordinary premises (EPIC 9)

        /// <summary>Businesses already wrecked, by canonical id - the once-only rule the
        /// GangFront flags carry, for premises that have no GangFront. Simulation-keyed,
        /// so a street streamed out and back is still a wreck.</summary>
        static readonly HashSet<string> DamagedBusinesses = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDamagedForPlay() => DamagedBusinesses.Clear();

        public static bool IsBusinessDamaged(LivingCity.Territory.TerritoryBusinessId id) =>
            id.IsValid && DamagedBusinesses.Contains(id.Value);

        /// <summary>A Torch that came off at an ORDINARY shop: the same fire and the
        /// same boards the rival fronts get, at the business's own door. False when the
        /// door cannot be resolved or the place is already a wreck.</summary>
        public static bool ScorchBusiness(LivingCity.Territory.TerritoryBusinessId id)
        {
            if (!TryFrontage(id, out var door, out var outward) ||
                !DamagedBusinesses.Add(id.Value))
                return false;

            ScorchAt(door, outward, id.Value, door.y);
            return true;
        }

        /// <summary>A SmashUp that came off: no fire, straight to the boards - the
        /// wrecked ground floor nailed shut. Once per premises.</summary>
        public static bool SmashBusiness(LivingCity.Territory.TerritoryBusinessId id)
        {
            if (!TryFrontage(id, out var door, out var outward) ||
                !DamagedBusinesses.Add(id.Value))
                return false;
            SmashAt(door, outward, id.Value, door.y);
            return true;
        }

        /// <summary>The ordinary-premises torch visual at already resolved frontage
        /// geometry. The business overload owns persistence; this geometry overload owns
        /// only the same shared fire presentation and returns it for finite-lived callers.</summary>
        public static Transform ScorchAt(
            Vector3 door, Vector3 outward, string label, float groundY)
        {
            var go = new GameObject("Burning · " + (label ?? "premises"));
            go.transform.SetParent(Root(), false);
            var fire = go.AddComponent<ShopFire>();
            fire.BeginAt(door, outward, label, groundY,
                FireMaterial(), SmokeMaterial(), BoardMaterial());
            return go.transform;
        }

        /// <summary>The ordinary-premises smash visual at already resolved frontage
        /// geometry. Uses the exact boarding presentation applied by SmashBusiness.</summary>
        public static Transform SmashAt(
            Vector3 door, Vector3 outward, string label, float groundY) =>
            BoardUpAt(door, outward, label, groundY, BoardMaterial());

        /// <summary>The doorstep and which way the front faces, off the SIMULATION's
        /// site (never a marker that may be streamed out): outward is door minus the
        /// footprint's centre, which is what "facing the street" means for a shop.</summary>
        static bool TryFrontage(
            LivingCity.Territory.TerritoryBusinessId id, out Vector3 door, out Vector3 outward)
        {
            door = default;
            outward = Vector3.forward;
            var runtime = TerritoryRuntime.Instance;
            if (runtime == null || !id.IsValid ||
                !runtime.TryGetBusinessApproach(id, out door))
                return false;

            var business = LivingCity.Business.BusinessRuntime.Instance;
            if (business != null && business.TryGetSite(id, out var site) && site != null)
            {
                var centre = new Vector3(
                    site.Footprint.XMin + site.Footprint.Width * 0.5f, door.y,
                    site.Footprint.ZMin + site.Footprint.Depth * 0.5f);
                var toDoor = door - centre;
                toDoor.y = 0f;
                if (toDoor.sqrMagnitude > 1e-4f)
                    outward = toDoor.normalized;
            }

            return true;
        }

        /// <summary>Resolve the same authoritative frontage used by business damage so a
        /// visible projectile can hit the actual facade rather than the job's approach
        /// point. The simulation ID remains the authority; this only exposes geometry.</summary>
        internal static bool TryBusinessFrontage(
            LivingCity.Territory.TerritoryBusinessId id,
            out Vector3 door,
            out Vector3 outward) => TryFrontage(id, out door, out outward);

        // ------------------------------------------------------------------ materials

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        static Material FireMaterial()
        {
            if (_fire != null) return _fire;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _fire = new Material(shader);
            SetColor(_fire, new Color(1f, 0.55f, 0.12f, 1f));
            return _fire;
        }

        static Material SmokeMaterial()
        {
            if (_smoke != null) return _smoke;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _smoke = new Material(shader);
            SetColor(_smoke, new Color(0.12f, 0.12f, 0.12f, 0.7f));
            return _smoke;
        }

        static Material BoardMaterial()
        {
            if (_board != null) return _board;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _board = new Material(shader);
            SetColor(_board, new Color(0.36f, 0.24f, 0.13f));   // bare timber
            return _board;
        }

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            else m.color = c;
        }

        /// <summary>Nail a run of planks across the storefront, at the door's line, from
        /// the ground up - the boarded-up ground floor. Left standing (parented to the
        /// damage root, not to the burn object that made it).</summary>
        internal static void BoardUp(GangFront front, float groundY, Material board)
        {
            front.Boarded = true;
            BoardUpAt(front.Door, front.Outward, front.GangName, groundY, board);
        }

        internal static Transform BoardUpAt(
            Vector3 doorAt, Vector3 facingOut, string label, float groundY, Material board)
        {
            var outward = facingOut.sqrMagnitude > 1e-4f ? facingOut.normalized : Vector3.forward;
            // LookRotation(outward) puts the plank's local +X along the frontage, so the
            // boards run across the storefront with no separate lateral axis to carry.
            // Boarding belongs on the exterior face. A small outward offset clears the
            // glass/facade plane without pushing the planks out onto the pavement.
            var baseAt = new Vector3(doorAt.x, groundY, doorAt.z) + outward * BoardOutset;
            var facing = Quaternion.LookRotation(outward, Vector3.up);

            var boards = new GameObject("Boards · " + label).transform;
            boards.SetParent(Root(), false);

            const int planks = 5;
            float gap = StoreHeight / planks;
            for (int i = 0; i < planks; i++)
            {
                float h = gap * (i + 0.5f);
                var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plank.name = "Plank";
                var col = plank.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                plank.transform.SetParent(boards, false);
                plank.transform.rotation = facing * Quaternion.Euler(0f, 0f, Random.Range(-2.5f, 2.5f));
                plank.transform.position = baseAt + Vector3.up * h;
                plank.transform.localScale = new Vector3(StoreWidth, gap * 0.82f, 0.09f);
                var mr = plank.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.sharedMaterial = board;
            }

            // two cross-braces, corner to corner, the way a shopfront gets nailed shut
            for (int s = -1; s <= 1; s += 2)
            {
                var brace = GameObject.CreatePrimitive(PrimitiveType.Cube);
                brace.name = "Brace";
                var col = brace.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                brace.transform.SetParent(boards, false);
                float diag = Mathf.Atan2(StoreHeight, StoreWidth) * Mathf.Rad2Deg;
                brace.transform.rotation = facing * Quaternion.Euler(0f, 0f, s * diag);
                brace.transform.position = baseAt + Vector3.up * (StoreHeight * 0.5f);
                float len = Mathf.Sqrt(StoreWidth * StoreWidth + StoreHeight * StoreHeight);
                brace.transform.localScale = new Vector3(len, 0.16f, 0.07f);
                var mr = brace.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.sharedMaterial = board;
            }

            return boards;
        }
    }

    /// <summary>The fire on a bombed shopfront: a few flames that flicker and lean, a
    /// glow on the street, and smoke drifting up - all on its own clock, no particle
    /// system. When it has burnt BurnFor seconds it boards the front up and is gone.</summary>
    public sealed class ShopFire : MonoBehaviour
    {
        GangFront _front;
        Vector3 _doorAt;
        Vector3 _facingOut;
        string _label = "";
        float _groundY;
        Material _board;
        float _age;
        Light _glow;
        readonly List<Transform> _flames = new List<Transform>();          // procedural fallback
        readonly List<Transform> _fireFx = new List<Transform>();          // Synty fire instances
        readonly List<Vector3> _fireBase = new List<Vector3>();            // their planted scale
        readonly List<(Transform tf, float born)> _smokes = new List<(Transform, float)>();   // procedural fallback
        Transform _smokeFx;      // Synty smoke instance
        Material _smokeMat;
        float _nextSmoke;

        public void Begin(GangFront front, float groundY, Material fire, Material smoke, Material board)
        {
            _front = front;
            BeginAt(front.Door, front.Outward, front.GangName, groundY, fire, smoke, board);
        }

        /// <summary>The same fire on a front that has no GangFront - an ordinary shop
        /// torched over its dues (EPIC 9). Boards itself up by position and label.</summary>
        public void BeginAt(Vector3 doorAt, Vector3 facingOut, string label,
            float groundY, Material fire, Material smoke, Material board)
        {
            _doorAt = doorAt;
            _facingOut = facingOut;
            _label = label ?? "";
            _groundY = groundY;
            _board = board;
            _smokeMat = smoke;

            var outward = facingOut.sqrMagnitude > 1e-4f ? facingOut.normalized : Vector3.forward;
            var lateral = Vector3.Cross(Vector3.up, outward).normalized;
            var baseAt = new Vector3(doorAt.x, groundY, doorAt.z) + outward * 0.3f;
            transform.position = baseAt;
            var facing = Quaternion.LookRotation(outward, Vector3.up);

            // the fire itself: the project's Synty fire particle, a run of them strung
            // across the ground-floor frontage
            for (int i = -1; i <= 1; i++)
            {
                var pos = baseAt + lateral * (i * 2.4f) + Vector3.up * 0.2f;
                var fx = BombFx.Spawn(BombFx.Fire, pos, facing, 1.15f, 0f, transform);
                if (fx == null) break;   // pack absent - drop to the procedural flames below
                _fireFx.Add(fx.transform);
                _fireBase.Add(fx.transform.localScale);
            }

            // no pack: the old primitive flames, so a stripped project still shows fire
            if (_fireFx.Count == 0)
            {
                for (int i = 0; i < 6; i++)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    q.name = "Flame";
                    var col = q.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                    q.transform.SetParent(transform, false);
                    q.transform.localPosition = lateral * Random.Range(-3f, 3f) + Vector3.up * 0.9f;
                    var mr = q.GetComponent<MeshRenderer>();
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.sharedMaterial = fire;
                    _flames.Add(q.transform);
                }
            }

            // black smoke boiling up off the front - the project's Synty smoke, a column
            // rising above the fire (procedural puffs below only if the pack is stripped)
            var smk = BombFx.Spawn(BombFx.Smoke, baseAt + Vector3.up * 1.2f, Quaternion.identity, 0.5f, 0f, transform);
            _smokeFx = smk != null ? smk.transform : null;

            _glow = gameObject.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.color = new Color(1f, 0.55f, 0.2f);
            _glow.range = 16f;
            _glow.intensity = 6f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _age += dt;

            // burnt out: board it up and go
            if (_age >= ShopDamage.BurnFor)
            {
                if (_front != null)
                    ShopDamage.BoardUp(_front, _groundY, _board);
                else
                    ShopDamage.BoardUpAt(_doorAt, _facingOut, _label, _groundY, _board);
                Destroy(gameObject);
                return;
            }

            // the flames flicker and always face the camera enough (billboard to +Y up),
            // fading down over the last few seconds as the fire dies
            float fade = Mathf.Clamp01((ShopDamage.BurnFor - _age) / 5f);
            var cam = Camera.main;

            // Synty fire burns at full, then is shrunk away over the last few seconds as
            // it dies down to the boarding-up
            for (int i = 0; i < _fireFx.Count; i++)
            {
                var f = _fireFx[i];
                if (f != null) f.localScale = _fireBase[i] * Mathf.Lerp(0.35f, 1f, fade);
            }

            for (int i = 0; i < _flames.Count; i++)
            {
                var f = _flames[i];
                float flick = 0.7f + 0.5f * Mathf.Abs(Mathf.Sin((_age + i) * (5f + i)));
                f.localScale = new Vector3(1.4f, (2.2f + flick) * fade, 1f);
                if (cam != null)
                {
                    var to = f.position - cam.transform.position; to.y = 0f;
                    if (to.sqrMagnitude > 1e-3f) f.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
                }
            }
            if (_glow != null) _glow.intensity = (5f + 3f * Mathf.Abs(Mathf.Sin(_age * 11f))) * fade;

            // procedural smoke puffs - only when the Synty smoke column is not present
            if (_smokeFx == null)
            {
            _nextSmoke -= dt;
            if (_nextSmoke <= 0f && _age < ShopDamage.BurnFor - 4f)
            {
                _nextSmoke = 0.6f;
                var s = GameObject.CreatePrimitive(PrimitiveType.Quad);
                s.name = "Smoke";
                var col = s.GetComponent<Collider>();
                if (col != null) Destroy(col);
                s.transform.SetParent(transform, false);
                s.transform.localPosition = Vector3.up * 2.6f;
                var mr = s.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.sharedMaterial = _smokeMat;
                _smokes.Add((s.transform, _age));
            }
            for (int i = _smokes.Count - 1; i >= 0; i--)
            {
                var (tf, born) = _smokes[i];
                float sa = _age - born;
                if (tf == null || sa > 4f) { if (tf != null) Destroy(tf.gameObject); _smokes.RemoveAt(i); continue; }
                tf.localPosition = Vector3.up * (2.6f + sa * 1.6f);
                tf.localScale = Vector3.one * (1.2f + sa * 0.9f);
                if (Camera.main != null)
                {
                    var to = tf.position - Camera.main.transform.position; to.y = 0f;
                    if (to.sqrMagnitude > 1e-3f) tf.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
                }
            }
            }   // end procedural-smoke fallback
        }
    }
}
