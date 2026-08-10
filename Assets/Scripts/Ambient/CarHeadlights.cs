using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;
using LivingCity.City;

namespace LivingCity.Ambient
{
    /// <summary>
    /// Puts two real headlight spots on every AI car, driven by the same night curve as the
    /// street lamps.
    ///
    /// Attach-on-sight rather than attach-at-spawn: traffic cars come and go through the
    /// pack's own spawning, which this project does not own, so a periodic rescan for
    /// CarBehaviors without lights is the hook that needs no changes to the pack. The lights
    /// die with their car (they are plain children), so despawn needs no bookkeeping beyond
    /// dropping dead references.
    ///
    /// Budgeted since the 10x city: at carCount 300 an unlimited fleet is up to 600 spot
    /// lights, and URP Forward+ renders at most 256 additional lights per frame on desktop -
    /// past that the pipeline culls quietly, and what it culled first in practice was the
    /// street lamps the player was looking at. So the nearest <see cref="LitBeamBudget"/>
    /// beams to the camera's ground focus burn and the rest wait, same scheme and same
    /// focus-projection as StreetLampLights.Resort: 192 lamps + 48 beams = 240, under the
    /// ceiling with headroom for the odd extra light.
    ///
    /// Only CarBehavior cars qualify. Parked cars are static set dressing and stay dark -
    /// a parking lot of burning headlights at 3am would be its own bug report.
    ///
    /// That held for the STATIC bakes, which have no CarBehavior and were never seen here, and
    /// not at all for the cars this project parks itself: the patrol fleet in its stalls and
    /// the bank's visitors in theirs both keep a CarBehavior and both used to sit burning. See
    /// Parked for the test that closes it.
    /// </summary>
    public sealed class CarHeadlights : MonoBehaviour
    {
        public const string HolderName = "headlight";

        /// <summary>Seconds between rescans for newly spawned cars.</summary>
        const float RescanInterval = 1f;

        /// <summary>
        /// Beams allowed to burn at once: 24 cars' worth. Counted in BEAMS, sorted by the CAR's
        /// position - a pair shares one sort key, so the cut falls between cars, not between one
        /// car's left and right light. Sized against the shared URP ceiling, see the class note.
        /// </summary>
        const int LitBeamBudget = 48;

        /// <summary>
        /// Seconds between re-picks of the burning set. Cars move, so this is the lamp system's
        /// 0.4s rather than the 1s rescan: at 45km/h a car covers 5m per re-pick, which is
        /// within the beams' own throw and reads as continuous.
        /// </summary>
        const float ResortInterval = 0.4f;

        /// <summary>Period tungsten yellow - selenium-bulb headlights, not modern white.</summary>
        static readonly Color BeamColour = new(1f, 0.85f, 0.45f);

        /// <summary>
        /// Tilted down so the beam pools on the tarmac just ahead of the bumper instead of
        /// running flat forever; the cone half-width then paints the road, not the facades.
        /// At 24 degrees from 0.7m the beam centre lands ~1.6m ahead - the pool hugs the car,
        /// and the short throw is also what makes it bright (1/d^2 with d ~1.7).
        /// </summary>
        const float DownTilt = 24f;
        const float SpotOuterAngle = 70f;
        const float SpotInnerAngle = 35f;

        /// <summary>
        /// Fallbacks for a car without a usable body collider - a typical pack car is ~5m
        /// long, so lights at z 2.1 sit at the bumper.
        /// </summary>
        static readonly Vector3 FallbackLeft = new(-0.6f, 0.7f, 2.1f);
        static readonly Vector3 FallbackRight = new(0.6f, 0.7f, 2.1f);

        [SerializeField] CityConfig config;
        [SerializeField] CityClock clock;

        /// <summary>
        /// A beam and the car it belongs to. The car reference is what the flat List&lt;Light&gt;
        /// lacked: whether a beam burns is now a question about its car, not about the hour
        /// alone, and there is no way back from a Light to the CarBehavior above it that does
        /// not cost a GetComponentInParent per beam per pass.
        /// </summary>
        readonly struct Beam
        {
            public readonly Light Light;
            public readonly CarBehavior Car;

            public Beam(Light light, CarBehavior car)
            {
                Light = light;
                Car = car;
            }
        }

        readonly List<Beam> beams = new();

        float nextScan;
        float nextResort;
        float lit = -1f;

        void Start()
        {
            if (!config)
            {
                var builder = FindAnyObjectByType<Generation.CityBuilder>();
                if (builder)
                    config = builder.Config;
            }

            if (!clock)
                clock = FindAnyObjectByType<CityClock>();
        }

        void LateUpdate()
        {
            var night = CityWeather.Nightness(clock ? clock.Hour : 12f);
            var target = (config ? config.headlightIntensity : 8f) * night;

            // The scan takes the CURRENT target so a freshly fitted car lights up on the
            // spot. It used to rely on the change-driven loop below - which never runs while
            // the night is steady, so every car spawned after dusk drove dark: only the
            // handful alive during the transition frame ever got lit.
            var scanned = false;
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + RescanInterval;
                Scan(target);
                scanned = true;
            }

            // Once the hour alone decided this, so the pass could be skipped whenever the night
            // was steady. Parked-ness changes under a steady night - a car docks at 2am - and
            // since the budget, WHICH cars deserve their lights changes whenever the camera or
            // the traffic moves, so the pass also rides the resort tick. A car that stops,
            // pulls away, or drives into frame reacts within ResortInterval.
            var resortDue = Time.unscaledTime >= nextResort;
            if (resortDue)
                nextResort = Time.unscaledTime + ResortInterval;

            if (!scanned && !resortDue && Mathf.Approximately(target, lit))
                return;

            lit = target;
            Apply(target);
        }

        void Apply(float target)
        {
            var burn = target > 0.001f;

            for (var i = beams.Count - 1; i >= 0; i--)
                if (!beams[i].Light)
                    beams.RemoveAt(i);   // its car despawned

            // Only rank when the budget actually has to choose, and only when there is a camera
            // to rank against - without one the existing order stands and the budget comes off
            // the front of it, arbitrary but lit (the lamp system's rule, for the same reason).
            var camera = Camera.main;
            if (burn && camera && beams.Count > LitBeamBudget)
            {
                // Where the camera LOOKS, not where it stands - the boom parks it 200m back.
                var eye = camera.transform.position;
                var forward = camera.transform.forward;
                if (forward.y < -0.05f && eye.y > 0f)
                    eye += forward * (eye.y / -forward.y);

                beams.Sort((a, b) =>
                {
                    if (!a.Car || !b.Car)
                        return 0;

                    return (a.Car.transform.position - eye).sqrMagnitude
                           .CompareTo((b.Car.transform.position - eye).sqrMagnitude);
                });
            }

            var on = 0;
            for (var i = 0; i < beams.Count; i++)
            {
                var beam = beams[i];
                // Parked cars stay dark AND stay out of the count - a full station forecourt
                // must not spend the budget the moving traffic in front of it needs.
                var burns = burn && on < LitBeamBudget && !Parked(beam.Car);
                if (burns)
                    on++;

                beam.Light.enabled = burns;
                beam.Light.intensity = target;
            }
        }

        /// <summary>
        /// A car that is not going anywhere. Both halves are how this project stops a car it
        /// owns: the patrol fleet's session-start cars have their CarBehavior held disabled by
        /// PoliceDirector, and everything else parked - the rest of the fleet, the bank's
        /// visitors - is held with stopHere.
        ///
        /// Deliberately NOT a speed test. A car at a red light is stopped and should keep its
        /// lights on, and it is held through heldByCrosswalk/heldByLight rather than through
        /// either of these - so the two states stay distinguishable without asking the traffic
        /// model anything.
        ///
        /// Not covered by the headless suite: every term is a UnityEngine.Object member, and
        /// both `enabled` and the null check are native calls that throw outside the Unity
        /// runtime. This one is verified by eye, at night, in the station forecourt.
        /// </summary>
        static bool Parked(CarBehavior car) => !car || !car.enabled || car.stopHere;

        /// <summary>Finds cars that have no lights yet and fits them a pair, lit at the given
        /// intensity right away.</summary>
        void Scan(float intensity)
        {
            var fitted = 0;

            foreach (var car in FindObjectsByType<CarBehavior>(FindObjectsSortMode.None))
            {
                if (car.transform.Find(HolderName))
                    continue;

                var range = config ? config.headlightRange : 18f;

                Attach(car, LeftPosition(car), range, intensity);
                Attach(car, RightPosition(car), range, intensity);
                fitted++;
            }

            if (fitted > 0)
                Debug.Log($"[Headlights] {fitted} cars fitted, {beams.Count} beams total.", this);
        }

        void Attach(CarBehavior car, Vector3 localPosition, float range, float intensity)
        {
            var holder = new GameObject(HolderName);
            holder.transform.SetParent(car.transform, false);
            holder.transform.localPosition = localPosition;
            holder.transform.localRotation = Quaternion.Euler(DownTilt, 0f, 0f);

            var light = holder.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = SpotOuterAngle;
            light.innerSpotAngle = SpotInnerAngle;
            light.color = BeamColour;
            light.range = range;
            light.intensity = Mathf.Max(0f, intensity);
            light.shadows = LightShadows.None;
            // A car fitted while it stands in a stall must not flare even for the one frame
            // before Apply reaches it - Scan runs from the same LateUpdate, but "the pass right
            // after this one will fix it" is exactly the kind of ordering that stops being true.
            light.enabled = intensity > 0.001f && !Parked(car);
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.renderMode = LightRenderMode.ForcePixel;

            holder.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>()
                  .usePipelineSettings = true;

            beams.Add(new Beam(light, car));
        }

        // ------------------------------------------------------------------ placement

        /// <summary>
        /// Headlight positions from the car's own body collider, so a truck's lights sit on
        /// the truck's bumper and not on a sedan-sized guess. The AI prefabs carry two boxes -
        /// the body, centred near the origin, and a forward driving sensor hung ~3.5m ahead -
        /// so the pick is the largest NON-TRIGGER box, which is always the body.
        ///
        /// Colliders, not mesh bounds, on purpose: mesh bounds lie under static batching (see
        /// StreetLampLights.BulbOffsets for that story), and while moving cars are never
        /// batched, colliders are authored data and cannot be swapped out from under us.
        /// </summary>
        static BoxCollider Body(CarBehavior car)
        {
            BoxCollider best = null;
            var bestVolume = 0f;

            foreach (var box in car.GetComponents<BoxCollider>())
            {
                if (box.isTrigger)
                    continue;

                var volume = box.size.x * box.size.y * box.size.z;
                if (volume <= bestVolume)
                    continue;

                bestVolume = volume;
                best = box;
            }

            return best;
        }

        static Vector3 LeftPosition(CarBehavior car)
        {
            var body = Body(car);
            if (!body)
                return FallbackLeft;

            return new Vector3(-(body.size.x * 0.5f - 0.35f), 0.7f,
                               body.center.z + body.size.z * 0.5f - 0.3f);
        }

        static Vector3 RightPosition(CarBehavior car)
        {
            var left = LeftPosition(car);
            return new Vector3(-left.x, left.y, left.z);
        }
    }
}
