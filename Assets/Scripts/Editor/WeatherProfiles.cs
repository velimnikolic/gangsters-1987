using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using LivingCity.Ambient;
using LivingCity.Data;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Authors the three weather grades as VolumeProfile assets, and previews them in the editor.
    ///
    /// The grades live in assets rather than in code so they can be dialled in against what is
    /// actually on screen - a colour grade is not something anyone gets right by reading numbers.
    /// Creation is therefore one-time and non-destructive: an existing profile is left exactly as
    /// it is, so tuning survives every later run of this.
    /// </summary>
    public static class WeatherProfiles
    {
        public const string Folder = "Assets/Settings/Weather";

        public const string ClearDuskPath = Folder + "/Chicago - Clear Dusk.asset";
        public const string OvercastPath = Folder + "/Chicago - Overcast.asset";
        public const string SmogPath = Folder + "/Chicago - Smog.asset";

        /// <summary>
        /// How far each grade travels from neutral, matching CityWeather.Strength so the post
        /// stack and the lighting are dialled back by the same amount. Values below are written
        /// at full strength and multiplied - or lerped out of neutral - by this on the way in,
        /// which keeps them readable as intent rather than as arithmetic.
        ///
        /// The vignette is the one exception: it is set to a flat figure, not scaled, because it
        /// was asked for as a specific amount rather than as a share of the whole look.
        /// </summary>
        const float Strength = 0.7f;

        /// <summary>Asked for directly, so it is a number rather than a proportion.</summary>
        const float VignetteIntensity = 0.2f;

        /// <summary>Creates any grade that is missing, and leaves tuned ones alone.</summary>
        [MenuItem("Tools/City/Weather/Create Profiles", priority = 20)]
        public static void CreateProfiles() => Build(overwrite: false);

        /// <summary>
        /// Rewrites all three from the values in this file, discarding Inspector tuning.
        ///
        /// Needed because Create deliberately skips existing assets: without this, changing a
        /// number here would have no effect on a project that had already made its profiles once,
        /// which is a very quiet way to waste an afternoon.
        /// </summary>
        [MenuItem("Tools/City/Weather/Rebuild Profiles (discards tuning)", priority = 25)]
        public static void RebuildProfiles()
        {
            var confirmed = EditorUtility.DisplayDialog(
                "Rebuild weather profiles",
                "This overwrites all three grades in " + Folder + " with the values in " +
                "WeatherProfiles.cs.\n\nAnything tuned in the Inspector is lost.",
                "Rebuild", "Cancel");

            if (confirmed)
                Build(overwrite: true);
        }

        static void Build(bool overwrite)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/Settings", "Weather");

            BuildClearDusk(overwrite);
            BuildOvercast(overwrite);
            BuildSmog(overwrite);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // -- the three grades -------------------------------------------------------------

        /// <summary>
        /// Sepia. ACES for the filmic shoulder, saturation well down, and split toning that pushes
        /// amber into the highlights against teal in the shadows - the single strongest lever for
        /// "old photograph" there is, and worth more here than any amount of colour filtering.
        /// </summary>
        static void BuildClearDusk(bool overwrite)
        {
            var profile = Fresh(ClearDuskPath, overwrite);
            if (!profile)
                return;

            Add<Tonemapping>(profile, t => t.mode.value = TonemappingMode.ACES);

            Add<WhiteBalance>(profile, w =>
            {
                w.temperature.value = 15f * Strength;
                w.tint.value = 5f * Strength;
            });

            Add<ColorAdjustments>(profile, c =>
            {
                c.postExposure.value = 0.1f * Strength;
                c.contrast.value = 20f * Strength;
                c.colorFilter.value = Filter(new Color(1f, 0.92f, 0.80f));
                c.saturation.value = -25f * Strength;
            });

            Add<SplitToning>(profile, s =>
            {
                s.shadows.value = Tone(new Color(0.16f, 0.28f, 0.36f));
                s.highlights.value = Tone(new Color(0.85f, 0.62f, 0.32f));
                s.balance.value = 10f * Strength;
            });

            Add<Bloom>(profile, b =>
            {
                // Threshold and scatter shape the bloom rather than set how much of it there is,
                // so only the intensity is dialled back.
                b.threshold.value = 1.1f;
                b.intensity.value = 0.6f * Strength;
                b.scatter.value = 0.65f;
            });

            Add<Vignette>(profile, v =>
            {
                v.intensity.value = VignetteIntensity;
                v.smoothness.value = 0.5f;
                v.color.value = new Color(0.08f, 0.05f, 0.03f);
            });

            Add<FilmGrain>(profile, g =>
            {
                g.type.value = FilmGrainLookup.Medium1;
                g.intensity.value = 0.35f * Strength;
                g.response.value = 0.8f;
            });

            Add<ChromaticAberration>(profile, a => a.intensity.value = 0.12f * Strength);

            Save(profile);
        }

        /// <summary>
        /// Noir. Neutral tonemapping and hard contrast with the colour almost gone - the point is
        /// a scene that reads as black-and-white without actually being desaturated to zero.
        /// </summary>
        static void BuildOvercast(bool overwrite)
        {
            var profile = Fresh(OvercastPath, overwrite);
            if (!profile)
                return;

            Add<Tonemapping>(profile, t => t.mode.value = TonemappingMode.Neutral);

            Add<WhiteBalance>(profile, w =>
            {
                w.temperature.value = -10f * Strength;
                w.tint.value = -3f * Strength;
            });

            Add<ColorAdjustments>(profile, c =>
            {
                c.postExposure.value = 0.05f * Strength;
                c.contrast.value = 35f * Strength;
                c.colorFilter.value = Filter(new Color(0.95f, 0.96f, 1f));
                c.saturation.value = -45f * Strength;
            });

            Add<SplitToning>(profile, s =>
            {
                s.shadows.value = Tone(new Color(0.20f, 0.24f, 0.30f));
                s.highlights.value = Tone(new Color(0.75f, 0.76f, 0.78f));
                s.balance.value = -15f * Strength;
            });

            Add<Bloom>(profile, b =>
            {
                b.threshold.value = 1.3f;
                b.intensity.value = 0.25f * Strength;
                b.scatter.value = 0.7f;
            });

            Add<Vignette>(profile, v =>
            {
                v.intensity.value = VignetteIntensity;
                v.smoothness.value = 0.45f;
                v.color.value = new Color(0.03f, 0.04f, 0.05f);
            });

            Add<FilmGrain>(profile, g =>
            {
                g.type.value = FilmGrainLookup.Medium3;
                g.intensity.value = 0.5f * Strength;
                g.response.value = 0.7f;
            });

            Add<ChromaticAberration>(profile, a => a.intensity.value = 0.08f * Strength);

            Save(profile);
        }

        /// <summary>
        /// Soot. Warm and slightly green, low contrast because haze eats contrast, and the bloom
        /// pushed hard with a low threshold so bright surfaces bleed the way they do through smoke.
        /// </summary>
        static void BuildSmog(bool overwrite)
        {
            var profile = Fresh(SmogPath, overwrite);
            if (!profile)
                return;

            Add<Tonemapping>(profile, t => t.mode.value = TonemappingMode.Neutral);

            Add<WhiteBalance>(profile, w =>
            {
                w.temperature.value = 8f * Strength;
                w.tint.value = 8f * Strength;
            });

            Add<ColorAdjustments>(profile, c =>
            {
                c.postExposure.value = 0f;
                c.contrast.value = 10f * Strength;
                c.colorFilter.value = Filter(new Color(1f, 0.95f, 0.82f));
                c.saturation.value = -35f * Strength;
            });

            Add<SplitToning>(profile, s =>
            {
                s.shadows.value = Tone(new Color(0.22f, 0.22f, 0.18f));
                s.highlights.value = Tone(new Color(0.80f, 0.70f, 0.45f));
                s.balance.value = 0f;
            });

            Add<Bloom>(profile, b =>
            {
                b.threshold.value = 0.95f;
                b.intensity.value = 0.85f * Strength;
                b.scatter.value = 0.8f;
            });

            Add<Vignette>(profile, v =>
            {
                v.intensity.value = VignetteIntensity;
                v.smoothness.value = 0.55f;
                v.color.value = new Color(0.07f, 0.06f, 0.04f);
            });

            Add<FilmGrain>(profile, g =>
            {
                g.type.value = FilmGrainLookup.Medium2;
                g.intensity.value = 0.45f * Strength;
                g.response.value = 0.8f;
            });

            Add<ChromaticAberration>(profile, a => a.intensity.value = 0.15f * Strength);

            Save(profile);
        }

        // -- preview ----------------------------------------------------------------------

        [MenuItem("Tools/City/Weather/Preview - Clear Dusk", priority = 21)]
        static void PreviewClearDusk() => Preview(WeatherKind.ClearDusk);

        [MenuItem("Tools/City/Weather/Preview - Overcast", priority = 22)]
        static void PreviewOvercast() => Preview(WeatherKind.Overcast);

        [MenuItem("Tools/City/Weather/Preview - Smog", priority = 23)]
        static void PreviewSmog() => Preview(WeatherKind.Smog);

        /// <summary>Runs the same roll Play would, so the seed's actual weather can be seen.</summary>
        [MenuItem("Tools/City/Weather/Preview - Roll from Seed", priority = 24)]
        static void PreviewRolled()
        {
            var weather = Ready();
            var kind = weather.Apply();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Weather] Seed rolled {kind}.", weather);
        }

        // -- time of day ------------------------------------------------------------------
        //
        // Four hours worth looking at rather than a scrubber: the two extremes and the two
        // twilights, which are where the blend either works or does not.

        [MenuItem("Tools/City/Weather/Hour - 08:00 Morning", priority = 40)]
        static void HourMorning() => SetHour(8f);

        [MenuItem("Tools/City/Weather/Hour - 13:00 Midday", priority = 41)]
        static void HourMidday() => SetHour(13f);

        [MenuItem("Tools/City/Weather/Hour - 19:30 Dusk", priority = 42)]
        static void HourDusk() => SetHour(19.5f);

        [MenuItem("Tools/City/Weather/Hour - 23:00 Night", priority = 43)]
        static void HourNight() => SetHour(23f);

        static void SetHour(float hour)
        {
            var weather = Ready();

            var clock = Object.FindAnyObjectByType<CityClock>();
            if (!clock)
            {
                Debug.LogWarning("[Weather] No CityClock in the scene.");
                return;
            }

            clock.SetHour(hour);
            weather.Apply(weather.Current);

            // The lamps answer to the same menu now. Their light objects are created DontSave, so
            // previewing them cannot leave anything behind in the scene.
            var night = CityWeather.Nightness(hour);
            var lamps = Object.FindAnyObjectByType<StreetLampLights>();
            if (lamps)
                lamps.ApplyForEditor(night);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Weather] {clock.Display}, {weather.Current}, nightness {night:F2}.", weather);
        }

        /// <summary>
        /// Reports the state of everything the lamps depend on, in the open scene, without
        /// entering Play.
        ///
        /// Written because the lamps only exist at runtime, which made every question about them
        /// answerable only by pressing Play and reading a log that might not appear - and a
        /// missing log is the least informative signal there is. This checks the same conditions
        /// from the editor and names the one that is false.
        /// </summary>
        [MenuItem("Tools/City/Weather/Lamp Report", priority = 50)]
        static void LampReport()
        {
            // Fixes what it is about to check. A diagnostic that answers "StreetLampLights is
            // missing" and then leaves it missing is a step in a loop, not the end of one - and
            // adding the component is exactly what the answer would have told you to do.
            CitySceneSetup.EnsureWeather();

            var report = new System.Text.StringBuilder("[Lamp Report]\n");

            var lampObjects = new List<GameObject>();
            var names = new SortedDictionary<string, int>();

            foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!transform.name.StartsWith("lamp-", System.StringComparison.Ordinal))
                    continue;

                if (transform.parent && transform.parent.name.StartsWith("lamp-", System.StringComparison.Ordinal))
                    continue;

                lampObjects.Add(transform.gameObject);
                names.TryGetValue(transform.name, out var count);
                names[transform.name] = count + 1;
            }

            report.AppendLine($"  lamp objects in scene : {lampObjects.Count}");
            foreach (var pair in names)
                report.AppendLine($"      {pair.Key} x{pair.Value}");

            if (lampObjects.Count == 0)
                report.AppendLine("      -> NOTHING NAMED lamp-*. StreetLampLights finds lamps by " +
                                  "name, so it can never light anything. Either the city has no " +
                                  "lamps, or they are named differently.");
            else
            {
                var sample = lampObjects[0];
                var renderers = sample.GetComponentsInChildren<Renderer>();
                var top = 0f;
                foreach (var renderer in renderers)
                    top = Mathf.Max(top, renderer.bounds.max.y);

                report.AppendLine($"  sample '{sample.name}' at {sample.transform.position}, " +
                                  $"scale {sample.transform.lossyScale.x:F2}, " +
                                  $"{renderers.Length} renderers, top y {top:F2}");
            }

            report.AppendLine($"  StreetLampLights      : {(Object.FindAnyObjectByType<StreetLampLights>() ? "present (added by this report if it was not)" : "MISSING - could not be added")}");
            var sceneClock = Object.FindAnyObjectByType<CityClock>();
            if (sceneClock)
            {
                var so = new SerializedObject(sceneClock);
                var wired = so.FindProperty("config").objectReferenceValue;

                report.AppendLine($"  CityClock             : present, hour {sceneClock.Display}, " +
                                  $"{sceneClock.SecondsPerHour:F2} real sec per game hour");
                report.AppendLine($"      Running           : {(sceneClock.Running ? "ON" : "OFF - the clock is FROZEN, time will not advance in Play")}");
                report.AppendLine($"      config wired      : {(wired ? wired.name : "no - falling back to the component's own startHour")}");
            }
            else
                report.AppendLine("  CityClock             : MISSING");
            report.AppendLine($"  CityWeather           : {(Object.FindAnyObjectByType<CityWeather>() ? "present" : "MISSING")}");
            report.AppendLine($"  Camera.main           : {(Camera.main ? Camera.main.name : "MISSING - nothing is tagged MainCamera")}");

            var config = AssetDatabase.LoadAssetAtPath<CityConfig>("Assets/Configs/CityConfig.asset");
            if (config)
                report.AppendLine($"  config                : startHour {config.startHour}, " +
                                  $"budget {config.litLampBudget}, range {config.lampRange}, " +
                                  $"intensity {config.lampIntensity}, nightBrightness {config.nightBrightness}");
            else
                report.AppendLine("  config                : MISSING");

            // Build and light them at full night, then report what the actual Light components
            // ended up as. Every earlier round of this guessed at their state from the outside;
            // reading it is the only thing that settles "the lamps do not project light".
            var rig = Object.FindAnyObjectByType<StreetLampLights>();
            if (rig)
            {
                rig.ApplyForEditor(1f);

                var lights = new List<Light>();
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.type == LightType.Spot && light.name == StreetLampLights.HolderName)
                        lights.Add(light);

                var enabled = 0;
                foreach (var light in lights)
                    if (light.enabled)
                        enabled++;

                report.AppendLine($"  lamp Lights created   : {lights.Count}, enabled {enabled}");

                for (var i = 0; i < Mathf.Min(3, lights.Count); i++)
                {
                    var light = lights[i];
                    report.AppendLine(
                        $"      [{i}] enabled {light.enabled}, intensity {light.intensity:F2}, " +
                        $"range {light.range:F1}, at {light.transform.position}, " +
                        $"bake {light.lightmapBakeType}, colour {light.color}");
                }

                if (lights.Count == 0)
                    report.AppendLine("      -> Build() created nothing. It matches transforms named " +
                                      "lamp-* whose parent is not also lamp-*.");
                else if (enabled == 0)
                    report.AppendLine("      -> created but all disabled: Resort never switched any on.");
            }

            var urp = UniversalRenderPipeline.asset;
            report.AppendLine(urp
                ? $"  URP additional lights : {(urp.additionalLightsRenderingMode == LightRenderingMode.Disabled ? "DISABLED - point lights cannot render at all" : urp.additionalLightsRenderingMode.ToString())}"
                : "  URP asset             : MISSING");

            Debug.Log(report.ToString());
        }

        static void Preview(WeatherKind kind)
        {
            var weather = Ready();
            weather.Apply(kind);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Weather] Previewing {kind}.", weather);
        }

        /// <summary>
        /// Everything a preview needs, made if absent: the three grade assets and a wired
        /// CityWeather. Previewing is how the look gets judged, so it should not be the one path
        /// that stops to tell you what to run first.
        /// </summary>
        static CityWeather Ready()
        {
            if (!AssetDatabase.LoadAssetAtPath<VolumeProfile>(ClearDuskPath))
                CreateProfiles();

            return CitySceneSetup.EnsureWeather();
        }

        // -- plumbing ---------------------------------------------------------------------

        /// <summary>
        /// A new empty profile at the path, or null if one is already there. Returning null for an
        /// existing asset is what keeps hand-tuning safe across repeated runs.
        /// </summary>
        static VolumeProfile Fresh(string path, bool overwrite)
        {
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path))
            {
                if (!overwrite)
                {
                    Debug.Log($"[Weather] {path} already exists - left untouched.");
                    return null;
                }

                // Deleting rather than clearing the existing profile's component list: the
                // overrides are sub-assets of the file, and an in-place rebuild leaves the old
                // ones orphaned inside it where they go on quietly applying.
                AssetDatabase.DeleteAsset(path);
            }

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        /// <summary>Neutral for a colour filter is white - no tint at all.</summary>
        static Color Filter(Color full) => Color.Lerp(Color.white, full, Strength);

        /// <summary>
        /// Neutral for a split-toning swatch is mid grey, which is what URP treats as "no push in
        /// either direction". Lerping toward it weakens the tint without shifting its hue.
        /// </summary>
        static Color Tone(Color full) => Color.Lerp(new Color(0.5f, 0.5f, 0.5f), full, Strength);

        /// <summary>
        /// Adds an override, set to affect every one of its parameters, and files it as a
        /// sub-asset. Without the AddObjectToAsset the component exists only in memory and the
        /// saved profile comes back empty on the next domain reload.
        /// </summary>
        static void Add<T>(VolumeProfile profile, System.Action<T> configure)
            where T : VolumeComponent
        {
            var component = profile.Add<T>(true);
            configure(component);

            component.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
        }

        static void Save(VolumeProfile profile)
        {
            EditorUtility.SetDirty(profile);
            Debug.Log($"[Weather] Created {AssetDatabase.GetAssetPath(profile)}.", profile);
        }
    }
}
