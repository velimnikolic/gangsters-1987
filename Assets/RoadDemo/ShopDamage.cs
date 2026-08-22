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
        const float BoardInset = 1f;    // metres the boards sit back from the door, into the building

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
            var outward = front.Outward.sqrMagnitude > 1e-4f ? front.Outward.normalized : Vector3.forward;
            // LookRotation(outward) puts the plank's local +X along the frontage, so the
            // boards run across the storefront with no separate lateral axis to carry.
            // Set back a metre from the door line, into the building, so they sit inside
            // the window reveal the way a real boarding-up does rather than out on the kerb.
            var baseAt = new Vector3(front.Door.x, groundY, front.Door.z) - outward * BoardInset;
            var facing = Quaternion.LookRotation(outward, Vector3.up);

            var boards = new GameObject("Boards · " + front.GangName).transform;
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
        }
    }

    /// <summary>The fire on a bombed shopfront: a few flames that flicker and lean, a
    /// glow on the street, and smoke drifting up - all on its own clock, no particle
    /// system. When it has burnt BurnFor seconds it boards the front up and is gone.</summary>
    public sealed class ShopFire : MonoBehaviour
    {
        GangFront _front;
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
            _groundY = groundY;
            _board = board;
            _smokeMat = smoke;

            var outward = front.Outward.sqrMagnitude > 1e-4f ? front.Outward.normalized : Vector3.forward;
            var lateral = Vector3.Cross(Vector3.up, outward).normalized;
            var baseAt = new Vector3(front.Door.x, groundY, front.Door.z) + outward * 0.3f;
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
                if (_front != null) ShopDamage.BoardUp(_front, _groundY, _board);
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
