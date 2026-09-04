using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A bomb goes off: a white flash and a ball of fire that swells and dies in half a
    /// second, everyone caught inside the blast killed where they stand, the car it was
    /// laid under torn to scrap (CarShatter), and a bang the whole quarter hears
    /// (StreetAlarm), so the crowd bolts and the law comes running.
    ///
    /// It is deliberately blunt about who it kills: a bomb does not pick sides, so
    /// anyone standing in the radius goes down, the outfit's own men included. Keeping
    /// clear of it is the order-giver's problem (the thrower stands off, the planter
    /// walks away before it blows) - not the blast's.
    /// </summary>
    public static class Explosion
    {
        /// <summary>Metres of the killing radius, and of the light it throws.</summary>
        public const float Radius = 6f;

        static Material _flash;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => _flash = null;

        /// <summary>Set it off at <paramref name="at"/>. <paramref name="crews"/> is asked
        /// for the gangsters to catch in the blast (null skips them); <paramref name="car"/>
        /// is the one to tear apart, if any; <paramref name="faction"/> is who is answerable
        /// for the bang, for the alarm.</summary>
        public static void Blow(Vector3 at, DemoCrews crews, RoadCar car, int faction, float groundY)
        {
            var force = PoliceForce.Instance;
            force?.BeginExplosion(car, at);
            try
            {
                Flash(at);
                KillCivilians(at, groundY);
                KillCrews(at, crews, groundY);
            }
            finally
            {
                // The precinct attribution hint is scoped to this blast even if an
                // optional visual/body callback throws. It must never leak into the next
                // unrelated officer death.
                force?.EndExplosion(car);
            }
            if (car != null) CarShatter.Shatter(car, at);
            ShopDamage.ScorchNear(at, groundY);   // a shopfront in the blast catches fire

            // and if that shopfront is somebody's business, the house that threw it has
            // just answered for it: the racket records the damage and the street files
            // the fear. A blast in open road finds no business and reports nothing.
            TerritoryRuntime.ReportViolenceAt(
                at, faction, LivingCity.Territory.TerritoryEscalationKind.PropertyDamage,
                ShopDamage.ScorchRange);

            // the loudest thing on the street by far: heard three streets over, so the
            // whole quarter reacts and the police are called to it. The deaths are not
            // reported here: each man killed reports his own (CivilianAgent.Kill), and
            // a blanket report on top of that was a death the heat counted twice - or
            // once for a blast that hit nobody.
            StreetAlarm.Report(at, null, faction, 130f);

            if (DriveTrace.On)
                DriveTrace.Event("bomb", "blast",
                    $"at ({at.x:F0},{at.z:F0}) r{Radius:F0}" + (car != null ? " on a car" : ""));
        }

        static void KillCivilians(Vector3 at, float groundY)
        {
            float r2 = Radius * Radius;
            var all = CivilianAgent.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c.Dead || c.Tf == null) continue;
                if ((c.Tf.position - at).sqrMagnitude > r2) continue;
                c.Kill();
                CrewGore.Death(c, groundY);
            }
        }

        static void KillCrews(Vector3 at, DemoCrews crews, float groundY)
        {
            if (crews == null) return;
            float r2 = Radius * Radius;
            foreach (var unit in crews.Units)
            {
                if (unit == null) continue;
                foreach (var man in unit.All())
                {
                    if (man == null || man.Dead || man.Tf == null) continue;
                    if ((man.Tf.position - at).sqrMagnitude > r2) continue;
                    crews.KilledByBlast(man);
                }
            }
        }

        // ------------------------------------------------------------------ the flash

        static void Flash(Vector3 at)
        {
            // the fireball, debris, embers, shockwave and smoke tail are one shared effect
            var fx = BombFx.Spawn(BombFx.Explosion, at + Vector3.up * 1.0f, Quaternion.identity, 1.3f, 3f);

            // and a punch of light on the street with it - thrown whether or not the
            // particle pack is present, so a bomb always lights the block for an instant
            var lightGo = new GameObject("Blast Light");
            lightGo.transform.position = at + Vector3.up * 1.2f;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.35f);
            light.range = Radius * 3f;
            light.intensity = 8f;
            lightGo.AddComponent<BlastFx>().Begin(Radius);

            // pack stripped out? fall back to the old primitive fireball so there is
            // still fire on the screen
            if (fx == null) FallbackBall(at, light.transform);
        }

        /// <summary>The pre-particle fireball: a glowing sphere that swells and dies. Kept
        /// only for a project that does not have the Synty FX pack.</summary>
        static void FallbackBall(Vector3 at, Transform lightTf)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Blast";
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);   // it is a light, not a wall
            go.transform.position = at + Vector3.up * 1.2f;
            go.transform.localScale = Vector3.one * 1.5f;

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = FlashMaterial();

            // ride the same clock as the light so both are gone together
            go.AddComponent<BlastFx>().Begin(Radius);
        }

        static Material FlashMaterial()
        {
            if (_flash != null) return _flash;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _flash = new Material(shader);
            if (_flash.HasProperty(BaseColorId)) _flash.SetColor(BaseColorId, new Color(1f, 0.78f, 0.4f));
            else _flash.color = new Color(1f, 0.78f, 0.4f);
            return _flash;
        }
    }

    /// <summary>The half-second life of a blast's flash: the ball swells fast, the light
    /// falls away, and it is gone. Its own clock, so it looks the same however the frame
    /// rate runs and needs no particle system.</summary>
    public sealed class BlastFx : MonoBehaviour
    {
        float _age;
        float _peak;
        Light _light;
        float _lightAt;

        const float Life = 0.55f;

        public void Begin(float radius)
        {
            _peak = radius * 1.6f;
            _light = GetComponent<Light>();
            _lightAt = _light != null ? _light.intensity : 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _age += dt;
            float k = _age / Life;
            if (k >= 1f) { Destroy(gameObject); return; }

            // out fast then hang: sqrt swells the ball early
            float scale = Mathf.Lerp(1.5f, _peak, Mathf.Sqrt(Mathf.Clamp01(k)));
            transform.localScale = Vector3.one * scale;
            if (_light != null) _light.intensity = Mathf.Lerp(_lightAt, 0f, k);
        }
    }
}
