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
        /// <summary>One lamp prefab the wiring recognises: its FULL name and its bulb points.</summary>
        public struct LampKind
        {
            public string Name;
            public Vector3[] Bulbs;
        }

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
        /// its own lantern.
        /// </summary>
        const float BulbHeight = 2.5f;

        /// <summary>
        /// The lamps the placers spawn, by FULL prefab name - matching used to be by "lamp-"
        /// prefix and that caught more than the lamps: an odd bulb total (133 in one city) and
        /// a stray light beside a parked car both traced to lamp-flavoured names that were
        /// never street lamps. StartsWith against each full name only so Unity's own suffixes
        /// still match - "(Clone)" at runtime, " (1)" on a duplicate. Public because the Lamp
        /// Report diagnostic must count by the same rule (see IsLampRoot).
        ///
        /// Bulb points are in the lamp's own local space (the placer's yaw is followed for
        /// free), FIXED rather than measured off the instance's mesh at runtime: the generator
        /// marks lamp geometry batching-static, and static batching REPLACES
        /// MeshFilter.sharedMesh with the combined mesh when Play starts - so measuring the
        /// instance put bulbs at 0.86 x the bounds of half the city. Known prefabs need no
        /// measuring (Synty numbers off the binary FBX, like everything else):
        ///  - SM_Prop_Street_Lamp_01: 5.4m post, single arm reaching +Z 1.66, head glass
        ///    centred about z 1.3 - one bulb under the head.
        ///  - SM_Prop_Street_Lamp_08: the 3.1m park post, symmetric about the axis, so one
        ///    centred bulb is right (the lamp-city deal exactly).
        /// </summary>
        public static readonly LampKind[] LampKinds =
        {
            new()
            {
                Name = "SM_Prop_Street_Lamp_01",
                Bulbs = new Vector3[] { new(0f, BulbHeight, 1.3f) },
            },
            new()
            {
                Name = "SM_Prop_Street_Lamp_08",
                Bulbs = new Vector3[] { new(0f, BulbHeight, 0f) },
            },
        };

        /// <summary>
        /// Whether this transform is the root of a recognised lamp instance - the shared rule
        /// for the wiring here and the Lamp Report diagnostic. Children of a lamp are excluded
        /// so a holder or a lamp somehow standing under another lamp is not double-counted.
        /// </summary>
        public static bool IsLampRoot(Transform transform, out Vector3[] bulbs)
        {
            bulbs = null;
            foreach (var kind in LampKinds)
            {
                if (!transform.name.StartsWith(kind.Name, System.StringComparison.Ordinal))
                    continue;
                if (transform.parent
                    && transform.parent.name.StartsWith(kind.Name, System.StringComparison.Ordinal))
                    return false;
                bulbs = kind.Bulbs;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Cone of the down-facing spot. A point at bulb height spends most of itself sideways
        /// into the air; a spot puts the same light on the pavement, where it reads.
        ///
        /// Sized against BulbHeight, not the lantern: from 2.5m up, 135 degrees paints a
        /// ~6m-radius pool per head, and the 65-degree inner angle keeps the hot core to
        /// ~1.6m. The heads hang 6.2m apart, so the result is two circles - one under each
        /// lantern - overlapping softly between while the cores stay separate. Wider angles
        /// at head height were tried and fused into a single wash; narrower ones read as
        /// torch spots, not street lighting.
        /// </summary>
        const float SpotOuterAngle = 135f;
        const float SpotInnerAngle = 65f;

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

        /// <summary>Per-lamp sort keys (squared metres from the camera's focus), stamped
        /// once per resort; a dead lamp keys to infinity and sorts last.</summary>
        readonly List<float> keys = new();

        /// <summary>The ranking, made once on first use rather than as a closure per
        /// resort - and without the null branch that made the old order inconsistent.</summary>
        System.Comparison<int> byKey;

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
                if (!IsLampRoot(transform, out var bulbs))
                    continue;

                for (var b = 0; b < bulbs.Length; b++)
                {
                    var holder = new GameObject(HolderName);

                    // Outside Play these are a preview, and a preview must not become part of the
                    // project. DontSave keeps them out of the saved scene and out of the undo
                    // stack, so a lamp lit in the Scene view can never end up committed. It does
                    // NOT make Unity clean them up - they survive domain reloads and hide from
                    // FindObjectsByType - which is why Teardown sweeps with FindObjectsOfTypeAll.
                    if (!Application.isPlaying)
                        holder.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;

                    holder.transform.SetParent(transform, false);
                    holder.transform.localPosition = bulbs[b];
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
        ///
        /// Resources.FindObjectsOfTypeAll rather than FindObjectsByType, and this is
        /// load-bearing: outside Play the holders carry HideFlags.DontSave, and
        /// FindObjectsByType does not return DontSave objects - so the sweep saw nothing,
        /// every editor preview stacked a fresh set of lights over the last, and the stale
        /// sets kept burning at whatever intensity their last Resort chose (they are not in
        /// the new lists, so nothing ever dims them). They also survive a domain reload, so
        /// recompiling made the pile-up worse, not better.
        ///
        /// No scene check here, deliberately: a DontSave holder even survives its scene being
        /// CLOSED - its lamp dies with the scene, the holder is quietly re-rooted with no valid
        /// scene, and it goes on burning in the next scene as a beam with no lamp above it.
        /// Requiring a valid scene skipped exactly those orphans. Assets are excluded by
        /// IsPersistent instead (editor-only; in a build every holder is a plain scene object).
        /// </summary>
        public void Teardown()
        {
            SweepStrays();
            lamps.Clear();
            order.Clear();
        }

        /// <summary>
        /// The destroy half of Teardown, static so the editor can also run it on scene open -
        /// the moment orphans from a previous scene would otherwise become visible.
        /// </summary>
        public static void SweepStrays()
        {
            foreach (var light in Resources.FindObjectsOfTypeAll<Light>())
            {
                if (light.name != HolderName)
                    continue;

#if UNITY_EDITOR
                if (UnityEditor.EditorUtility.IsPersistent(light))
                    continue;
#endif

                if (Application.isPlaying)
                    Destroy(light.gameObject);
                else
                    DestroyImmediate(light.gameObject);
            }
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

            // Sorting only matters when the budget has to choose; with the budget effectively
            // unlimited (the current setting), every lamp burns and the sort would be wasted
            // work every 0.4s.
            if (camera && order.Count > budget)
            {
                // Sort around where the camera LOOKS, not where it stands. The isometric rig
                // parks the camera 200m back along its boom, so raw camera distance ranked
                // the map edge nearest the camera above the middle of the screen - the budget
                // lit a corner the player was not even looking at while every lamp in frame
                // stayed dark. Projecting the view ray onto the ground puts the budget on
                // exactly what is on screen.
                var eye = camera.transform.position;
                var forward = camera.transform.forward;
                if (forward.y < -0.05f && eye.y > 0f)
                    eye += forward * (eye.y / -forward.y);

                // Keys once per lamp rather than twice per comparison.
                keys.Clear();
                for (var i = 0; i < lamps.Count; i++)
                {
                    var lamp = lamps[i];
                    keys.Add(lamp ? (lamp.transform.position - eye).sqrMagnitude
                                  : float.PositiveInfinity);
                }

                byKey ??= (a, b) => keys[a].CompareTo(keys[b]);
                order.Sort(byKey);
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
