using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A bomb in the air: lobbed from a man's hand onto a mark - a rival, his doorstep,
    /// the crowd - it arcs over under its own weight and goes off where it lands
    /// (Explosion). It carries who threw it (for the alarm) and nothing else; the blast
    /// is the blast's, the same one a planted charge makes.
    ///
    /// Its own ballistic arithmetic, no Rigidbody: a headless run and a played one throw
    /// the same parabola, and it never snags on a collider halfway.
    /// </summary>
    public sealed class BombProjectile : MonoBehaviour
    {
        DemoCrews _crews;
        int _faction;
        float _groundY;
        Vector3 _vel;
        Vector3 _target;
        float _age, _flight;

        const float Gravity = 16f;

        /// <summary>Throw it from <paramref name="from"/> to <paramref name="to"/>, over a
        /// flight time set by the distance. <paramref name="crews"/> is handed on to the
        /// blast for the gangsters it catches.</summary>
        public static void Throw(Vector3 from, Vector3 to, DemoCrews crews, int faction, float groundY)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Bomb";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.3f;
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.sharedMaterial = BombKit.CasingMaterial();

            var b = go.AddComponent<BombProjectile>();
            b.Begin(from, to, crews, faction, groundY);
        }

        void Begin(Vector3 from, Vector3 to, DemoCrews crews, int faction, float groundY)
        {
            _crews = crews;
            _faction = faction;
            _groundY = groundY;
            _target = to;

            float dist = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(to.x, 0f, to.z));
            _flight = Mathf.Clamp(dist / 12f, 0.5f, 1.6f);
            var flat = (to - from); flat.y = 0f;
            _vel = flat / _flight;
            _vel.y = (to.y - from.y) / _flight + 0.5f * Gravity * _flight;
            transform.position = from;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _age += dt;
            _vel.y -= Gravity * dt;
            transform.position += _vel * dt;
            transform.Rotate(37f, 51f, 0f, Space.Self);
            if (_age >= _flight)
            {
                Explosion.Blow(_target, _crews, null, _faction, _groundY);
                Destroy(gameObject);
            }
        }
    }

    /// <summary>
    /// A charge laid in front of a car and left armed. It sits and watches the one car
    /// it was set for; the moment that car is driven off - its wheels turning, the
    /// street opening under it - it goes off and tears the car to scrap (Explosion,
    /// CarShatter). Left alone it waits forever, which is the point: it is sprung by the
    /// driver, not by a clock.
    /// </summary>
    public sealed class PlantedBomb : MonoBehaviour
    {
        DemoCrews _crews;
        RoadCar _car;
        int _faction;
        float _groundY;

        /// <summary>Metres a second the car must be moving under its own power for the
        /// charge to read as "being driven off" and spring.</summary>
        const float TriggerSpeed = 1.2f;

        public static PlantedBomb Lay(Vector3 at, RoadCar car, DemoCrews crews, int faction, float groundY)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Planted Bomb";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.position = at + Vector3.up * 0.12f;
            go.transform.localScale = new Vector3(0.34f, 0.16f, 0.24f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.sharedMaterial = BombKit.CasingMaterial();

            var b = go.AddComponent<PlantedBomb>();
            b._crews = crews;
            b._car = car;
            b._faction = faction;
            b._groundY = groundY;
            return b;
        }

        void Update()
        {
            // the car it was laid for is gone (already wrecked, or removed): the charge
            // has nothing to spring on, so it clears itself rather than sit forever
            if (_car == null || _car.Tf == null || _car.Wrecked) { Destroy(gameObject); return; }

            if (_car.RoadSpeed >= TriggerSpeed)
            {
                Explosion.Blow(transform.position, _crews, _car, _faction, _groundY);
                Destroy(gameObject);
            }
        }
    }

    /// <summary>The shared dark casing the thrown bomb and the laid charge are both drawn
    /// with - one material, made once.</summary>
    static class BombKit
    {
        static Material _casing;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => _casing = null;

        public static Material CasingMaterial()
        {
            if (_casing != null) return _casing;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _casing = new Material(shader);
            var dark = new Color(0.08f, 0.08f, 0.09f);
            if (_casing.HasProperty(BaseColorId)) _casing.SetColor(BaseColorId, dark);
            else _casing.color = dark;
            return _casing;
        }
    }
}
