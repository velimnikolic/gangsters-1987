using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Ambient
{
    /// <summary>
    /// Puts a real light in every street lamp, and burns only the ones near the camera.
    ///
    /// The pack's night demo turns out to rest on exactly this. Its night rig is two weak
    /// directional lights - a purple fill at 1.24 and a cold key at 0.24 - and then SIXTEEN
    /// hand-placed point lights that do the actual illuminating. Copying only the directionals,
    /// as this first did, reproduces their ambient and none of their light, which is why the
    /// first night came out unreadable.
    ///
    /// The demo could place sixteen by hand because its city is fixed. Ours is generated, and a
    /// full grid runs to several hundred lamps - far past what a forward renderer will take. So
    /// every lamp gets a light and only the nearest litLampBudget of them are switched on, which
    /// makes the cost a constant the config picks rather than a function of city size.
    ///
    /// Lamps are found by name. The placers instantiate the prefabs directly, so the instances
    /// carry the prefab's own name, and that is the only marker there is without changing the
    /// generation to tag them.
    /// </summary>
    public sealed class StreetLampLights : MonoBehaviour
    {
        /// <summary>The one lamp prefab left in the database is named this way; see CityAssetBootstrap.</summary>
        const string LampNamePrefix = "lamp-";

        /// <summary>
        /// Name of the light holder objects. Public because the Lamp Report diagnostic filters
        /// by it - a renamed holder would silently read as "no lights created".
        /// </summary>
        public const string HolderName = "lamp-light";

        /// <summary>The pack's own street-lamp amber, taken off the night demo's point lights.</summary>
        static readonly Color LampColour = new(1f, 0.655f, 0.189f);

        /// <summary>
        /// How far above the ground the light is EMITTED from - deliberately far below the
        /// lantern glass. The camera looks down at 45 degrees, so a source at height h paints
        /// its pool h metres up-view of where the glass appears on screen: from the double
        /// lamp's 8.7m heads that is 8.7m of parallax against heads only 6.2m apart, and each
        /// pool visually attached itself to the NEXT lantern down the street. The bulb itself
        /// is invisible (no volumetrics), so it can sit at any height; 2.5m keeps the apparent
        /// slip under half the head spacing, which is what makes a pool read as belonging to
        /// its own lantern. The mesh-measured fraction below is only a ceiling for short lamps.
        /// </summary>
        const float BulbHeight = 2.5f;

        /// <summary>
        /// Cap on the bulb height for lamps shorter than BulbHeight is tall, as a fraction of
        /// the lamp's mesh height - so a knee-high bollard could never emit from above itself.
        /// </summary>
        const float BulbHeightFraction = 0.92f;

        /// <summary>
        /// A lamp whose mesh reaches further than this from the post sideways has a lantern
        /// hanging on an arm there, not just a wide base - lamp-city, the widest armless lamp,
        /// is 0.41 across, the double lamp's arms reach 3.60.
        /// </summary>
        const float ArmThreshold = 1.5f;

        /// <summary>
        /// Where along an arm the lantern head hangs, as a fraction of the arm's reach. Off the
        /// double lamp's mesh: head glass centres at ±3.10 against bounds of ±3.60.
        /// </summary>
        const float HeadReachFraction = 0.86f;

        /// <summary>
        /// Cone of the down-facing spot. A point at bulb height spends most of itself sideways
        /// into the air; a spot puts the same light on the pavement, where it reads.
        ///
        /// Sized against BulbHeight, not the lantern: from 2.5m up, 120 degrees paints a
        /// ~4.3m-radius pool per head, and the 60-degree inner angle keeps the hot core to
        /// ~1.4m. The heads hang 6.2m apart, so the result is two distinct circles - one under
        /// each lantern - touching softly between. Wider angles at head height were tried and
        /// fused into a single wash; narrower ones read as torch spots, not street lighting.
        /// </summary>
        const float SpotOuterAngle = 120f;
        const float SpotInnerAngle = 60f;

        /// <summary>
        /// Seconds between re-sorts of which lamps are closest. The camera moves slowly enough at
        /// this zoom that doing it every frame would be several hundred distance checks to change
        /// nothing.
        /// </summary>
        const float ResortInterval = 0.4f;

        [SerializeField] CityConfig config;
        [SerializeField] CityClock clock;

        readonly List<Light> lamps = new();
        readonly List<int> order = new();

        /// <summary>Scratch for BulbPositions - at most one bulb per side of the post.</summary>
        static readonly Vector3[] bulbScratch = new Vector3[4];

        float nextResort;
        float lit = -1f;

        void Start()
        {
            // Same self-healing as CityWeather, for the same reason: an empty config here means
            // the lamp budget and range silently fall back to code defaults instead of the ones
            // in the asset the user is actually editing.
            if (!config)
            {
                var builder = FindAnyObjectByType<Generation.CityBuilder>();
                if (builder)
                    config = builder.Config;
            }

            if (!clock)
                clock = FindAnyObjectByType<CityClock>();

            Build();
        }

        /// <summary>
        /// Walks the scene once, finds the lamps and gives each a child down-facing spot light
        /// per lantern head - one for a plain post, two for the double lamp.
        ///
        /// A child rather than a component on the lamp itself: the lamp geometry is marked
        /// batching-static by the generator, and hanging the light off a separate transform keeps
        /// that untouched.
        /// </summary>
        void Build()
        {
            // A rebuild, not just a build. Regenerating the city destroys the lamps but not this
            // component, so the lists go stale and - if anything interrupted the destroy - a
            // holder can survive as an orphan hanging in the air where a lamp used to stand.
            // Sweeping first makes Build honest whenever it runs.
            Teardown();

            var range = config ? config.lampRange : 22f;

            foreach (var transform in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!transform.name.StartsWith(LampNamePrefix, System.StringComparison.Ordinal))
                    continue;

                // Not the holders themselves: their name shares the lamp- prefix, and in Play the
                // sweep's Destroy is deferred, so this same-frame walk still sees them. An orphan
                // whose lamp is gone has no parent to fail the check below.
                if (transform.name == HolderName)
                    continue;

                // Only the lamp root, not anything parented under one.
                if (transform.parent && transform.parent.name.StartsWith(LampNamePrefix, System.StringComparison.Ordinal))
                    continue;

                var bulbs = BulbPositions(transform, bulbScratch);

                for (var b = 0; b < bulbs; b++)
                {
                    var holder = new GameObject(HolderName);

                    // Outside Play these are a preview, and a preview must not become part of the
                    // project. DontSave keeps them out of the saved scene and out of the undo stack,
                    // and Unity drops them on the next reload - so a lamp lit in the Scene view can
                    // never end up committed, which is the failure this whole session opened with.
                    if (!Application.isPlaying)
                        holder.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;

                    holder.transform.SetParent(transform, false);
                    holder.transform.localPosition = bulbScratch[b];
                    holder.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // cone straight down

                    var light = holder.AddComponent<Light>();
                    light.type = LightType.Spot;
                    light.spotAngle = SpotOuterAngle;
                    light.innerSpotAngle = SpotInnerAngle;
                    light.color = LampColour;
                    light.range = range;
                    light.intensity = 0f;
                    light.shadows = LightShadows.None;   // several dozen shadow-casting points is not affordable
                    light.enabled = false;

                    // Explicit because the default is not something to bet on: a light left on Baked
                    // contributes nothing at all until a lightmap bake has been run, which looks
                    // exactly like a light that is not working.
                    light.lightmapBakeType = LightmapBakeType.Realtime;
                    light.renderMode = LightRenderMode.ForcePixel;

                    // URP would add this itself on first use, but adding it here means the lamps
                    // carry the same data component the key light does rather than whatever the
                    // pipeline decides to default them to.
                    holder.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>()
                          .usePipelineSettings = true;

                    lamps.Add(light);
                }
            }

            for (var i = 0; i < lamps.Count; i++)
                order.Add(i);

            if (lamps.Count == 0)
                Debug.LogWarning("[StreetLamps] No lamps found - generate the city first.", this);
            else
                Debug.Log($"[StreetLamps] {lamps.Count} lamp bulbs wired.", this);
        }

        /// <summary>Re-wires the lights against the current city. For the generator to call.</summary>
        public void Rebuild() => Build();

        /// <summary>
        /// Destroys every lamp-light holder in the scene - by name, not from the lists, so it
        /// also catches orphans the lists have forgotten - and empties the bookkeeping.
        /// </summary>
        public void Teardown()
        {
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (light.name != HolderName)
                    continue;

                if (Application.isPlaying)
                    Destroy(light.gameObject);
                else
                    DestroyImmediate(light.gameObject);
            }

            lamps.Clear();
            order.Clear();
        }

        /// <summary>
        /// True when the city has been regenerated under this component: the lists still count
        /// lights, but the lamps they hung from are gone and the references are dead.
        /// </summary>
        bool Stale()
        {
            for (var i = 0; i < lamps.Count; i++)
                if (!lamps[i])
                    return true;

            return false;
        }

        /// <summary>
        /// Where the bulbs are, in the lamp's own local space, from its mesh bounds. One centred
        /// bulb for a plain post, one per arm for lamps that hang their lanterns out sideways -
        /// the double lamp used to get a single light floating in mid-air between its two heads.
        /// Local space, so the offsets follow whatever yaw the placer chose for free.
        /// </summary>
        static int BulbPositions(Transform lamp, Vector3[] result)
        {
            var filter = lamp.GetComponentInChildren<MeshFilter>();

            // The pack's lamps are single-object prefabs, mesh on the root. Anything else has
            // no local frame to measure in, so fall back to the centred world-bounds bulb.
            if (!filter || !filter.sharedMesh || filter.transform != lamp)
            {
                result[0] = new Vector3(0f, FallbackHeight(lamp), 0f);
                return 1;
            }

            var bounds = filter.sharedMesh.bounds;
            var height = Mathf.Max(1f, Mathf.Min(BulbHeight, bounds.max.y * BulbHeightFraction));
            var count = 0;

            // Each side that reaches past the post is an arm with a head hung near its end.
            // Checked per side, not per axis: lamp-road carries its single head on +X only.
            if (bounds.min.x < -ArmThreshold)
                result[count++] = new Vector3(bounds.min.x * HeadReachFraction, height, 0f);
            if (bounds.max.x > ArmThreshold)
                result[count++] = new Vector3(bounds.max.x * HeadReachFraction, height, 0f);
            if (bounds.min.z < -ArmThreshold)
                result[count++] = new Vector3(0f, height, bounds.min.z * HeadReachFraction);
            if (bounds.max.z > ArmThreshold)
                result[count++] = new Vector3(0f, height, bounds.max.z * HeadReachFraction);

            if (count == 0)
                result[count++] = new Vector3(0f, height, 0f);

            return count;
        }

        /// <summary>
        /// A plausible bulb height from world-space renderer bounds, for a lamp whose mesh could
        /// not be measured - better than dropping the light on the pavement.
        /// </summary>
        static float FallbackHeight(Transform lamp)
        {
            var renderers = lamp.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 6f;

            var top = renderers[0].bounds.max.y;
            foreach (var renderer in renderers)
                top = Mathf.Max(top, renderer.bounds.max.y);

            return Mathf.Max(1f, Mathf.Min(BulbHeight, (top - lamp.position.y) * BulbHeightFraction));
        }

        /// <summary>
        /// Lights the lamps for a given nightness from the editor, without entering Play.
        ///
        /// The lamps used to exist only at runtime, which meant the Scene view showed a night with
        /// unlit lamp posts standing in it and the only way to check them was to press Play and
        /// read a log. Since the hour is previewed from a menu, the lamps have to answer to the
        /// same menu.
        /// </summary>
        public void ApplyForEditor(float night)
        {
            if (!config)
            {
                var builder = FindAnyObjectByType<Generation.CityBuilder>();
                if (builder)
                    config = builder.Config;
            }

            if (lamps.Count == 0 || Stale())
                Build();

            Resort((config ? config.lampIntensity : 3f) * night *
                   (config ? config.nightBrightness : 1f));
        }

        void LateUpdate()
        {
            if (lamps.Count == 0)
                return;

            // The city was regenerated while running - the old lights died with the old lamps.
            // Rebuild against whatever stands now; if nothing does, the next frames return above.
            if (Stale())
                Build();

            var night = CityWeather.Nightness(clock ? clock.Hour : 12f);
            var brightness = config ? config.nightBrightness : 1f;
            var target = (config ? config.lampIntensity : 3f) * night * brightness;

            if (Time.unscaledTime >= nextResort)
            {
                nextResort = Time.unscaledTime + ResortInterval;
                Resort(target);
            }
            else if (!Mathf.Approximately(target, lit))
            {
                foreach (var light in lamps)
                    if (light && light.enabled)
                        light.intensity = target;
            }

            lit = target;
        }

        /// <summary>
        /// Re-picks the budget of lamps nearest the camera, sets their intensity and switches the
        /// rest off.
        ///
        /// Takes the intensity as an argument rather than reading the field. It used to read it,
        /// and on the first frame after dark the field still held its "nothing applied yet"
        /// sentinel - so the lamps were dutifully switched on at zero brightness and, because the
        /// field then matched the target on every later frame, never corrected. Lamps that exist,
        /// are enabled, and emit nothing.
        ///
        /// A missing camera no longer aborts this either. It used to return early, which in a
        /// scene whose camera had lost its MainCamera tag left every lamp off permanently and
        /// said nothing about why. Without a camera there is no meaningful nearest, so the
        /// existing order stands and the budget is taken off the front of it - arbitrary, but lit.
        ///
        /// A full sort of every lamp each time, which is fine at this scale and honest about what
        /// it does. If the city grows enough for that to matter, the fix is a spatial partition,
        /// not a smaller budget.
        /// </summary>
        void Resort(float intensity)
        {
            var budget = config ? config.litLampBudget : 48;
            var camera = Camera.main;

            if (camera)
            {
                var eye = camera.transform.position;

                order.Sort((a, b) =>
                {
                    var lampA = lamps[a];
                    var lampB = lamps[b];

                    if (!lampA || !lampB)
                        return 0;

                    return (lampA.transform.position - eye).sqrMagnitude
                           .CompareTo((lampB.transform.position - eye).sqrMagnitude);
                });
            }

            var on = 0;

            for (var i = 0; i < order.Count; i++)
            {
                var light = lamps[order[i]];
                if (!light)
                    continue;

                var burn = i < budget && intensity > 0.001f;
                light.enabled = burn;

                if (!burn)
                    continue;

                light.intensity = intensity;
                on++;
            }

            // One line the first time they actually light, so "the lamps do nothing" can be
            // answered with a number instead of a guess.
            if (on > 0 && lit <= 0.001f)
                Debug.Log($"[StreetLamps] {on}/{lamps.Count} burning at intensity {intensity:F2}, " +
                          $"range {(config ? config.lampRange : 22f):F0}m" +
                          (camera ? "." : " - NO MainCamera, so the choice of which is arbitrary."), this);
        }
    }
}
