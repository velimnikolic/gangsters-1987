using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Bakes Assets/Configs/SoundDatabase.asset from the 400 Sounds Pack, exactly as
    /// CityAssetBootstrap bakes PrefabDatabase from the model packs: paths are consts here,
    /// missing files collect into one warning, runtime only ever sees the asset.
    ///
    /// Its own file rather than another region of CityAssetBootstrap - that file is 2,600
    /// lines and the two bakers share nothing but a habit.
    ///
    /// The pack is a general-purpose library, not a city soundscape, so some slots are
    /// honest re-purposings rather than literal matches (documented per slot below). CLIP
    /// choices are re-baked on every run; VOLUME/PITCH trims are seeded only when the asset
    /// is first created, so tuning done in the Inspector survives a re-bake - the same
    /// contract GetOrCreate gives CityConfig.
    /// </summary>
    public static class SoundAssetBootstrap
    {
        const string PackRoot = "Assets/ci/400 Sounds Pack/";
        const string GeneratedRoot = "Assets/ci/Generated Sounds/";
        const string ConfigDir = "Assets/Configs";

        // The one crowd recording anywhere in the project. It ships with the people pack and
        // sat unused; the 400 pack has no walla at all, so this IS the murmur.
        const string CrowdLoop =
            "Assets/polyperfect/Low Poly Animated People/- Sounds/crowd-walking.ogg";

        static readonly List<string> Missing = new List<string>();

        [MenuItem("Tools/City/Create or Refresh Sound Database", priority = 3)]
        public static void CreateSoundDatabaseMenu()
        {
            var db = CreateSoundDatabase();
            Selection.activeObject = db;
        }

        public static SoundDatabase CreateSoundDatabase()
        {
            Missing.Clear();
            SoundPackImportSettings.ReimportStragglers();

            var path = $"{ConfigDir}/SoundDatabase.asset";
            var db = AssetDatabase.LoadAssetAtPath<SoundDatabase>(path);
            var created = !db;

            if (created)
            {
                if (!AssetDatabase.IsValidFolder(ConfigDir))
                    AssetDatabase.CreateFolder("Assets", "Configs");
                db = ScriptableObject.CreateInstance<SoundDatabase>();
                AssetDatabase.CreateAsset(db, path);
            }

            // -- ambience ---------------------------------------------------------------
            // The pack has exactly one outdoor bed, so day gets the wind and night gets
            // silence on purpose: a city that goes quiet after dark reads as night, and the
            // murmur and engines still carry the scene.
            db.cityDayBed.clip = Clip("Environment/ambient_wind.wav");
            db.cityNightBed.clip = null;

            db.clearDuskLayer.clip = null;
            db.overcastLayer.clip = null;
            // Low white noise under the smog: the closest thing anywhere in the pack to an
            // industrial haze hum. Lives or dies by its trim - seeded very low below.
            db.smogLayer.clip = Clip("Other/white_noise_long.wav");

            // -- traffic ----------------------------------------------------------------
            // No engine recording exists in the pack, and appliances pitched down read as
            // mosquitoes - their energy lives in the kHz band however far the pitch drops.
            // These two are SYNTHESIZED idle loops (scratchpad gen_engine.py): a ~30 Hz and
            // a ~41 Hz firing fundamental with harmonic tails and lope wobble, seamless by
            // construction. Regenerate with different parameters rather than re-tuning
            // Unity-side pitch past ~0.8-1.4.
            db.engineLoops = ClipArrayAt(GeneratedRoot,
                "engine_idle_a.wav",
                "engine_idle_b.wav");

            // -- weapons ----------------------------------------------------------------
            // The only firearm in the pack. Variants come from the one-shot pool's pitch
            // jitter instead of from files.
            db.gunshots = ClipArray("Weapons/shot_muffled.wav");

            // -- pedestrians ------------------------------------------------------------
            db.footsteps = ClipArray(
                "Footsteps/foley_footstep_concrete_1.wav",
                "Footsteps/foley_footstep_concrete_2.wav",
                "Footsteps/foley_footstep_concrete_3.wav",
                "Footsteps/foley_footstep_concrete_4.wav");

            var crowd = AssetDatabase.LoadAssetAtPath<AudioClip>(CrowdLoop);
            if (!crowd)
                Missing.Add(CrowdLoop);
            db.crowdMurmurLoops = crowd
                ? new[] { crowd }
                : System.Array.Empty<AudioClip>();

            if (created)
                SeedTrims(db);

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            if (Missing.Count > 0)
                Debug.LogWarning(
                    $"[SoundBootstrap] {Missing.Count} clip(s) not found - those layers stay " +
                    $"silent:\n{string.Join("\n", Missing)}\n" +
                    "Is the pack extracted at 'Assets/ci/400 Sounds Pack/'?");
            else
                Debug.Log("[SoundBootstrap] SoundDatabase refreshed, all slots filled.", db);

            return db;
        }

        /// <summary>
        /// First-creation levels only. Chosen at the keyboard, to be finished by ear in the
        /// Inspector - which is why a re-bake never touches them again.
        /// </summary>
        static void SeedTrims(SoundDatabase db)
        {
            db.cityDayBed.volume = 0.35f;
            db.cityNightBed.volume = 0.35f;
            db.smogLayer.volume = 0.08f;

            db.engineVolume = 0.4f;
            db.enginePitchMin = 0.95f;
            db.enginePitchMax = 1.35f;

            db.gunshotVolume = 1f;
            db.footstepVolume = 0.35f;
            db.murmurVolume = 0.5f;
            db.masterVolume = 1f;
        }

        static AudioClip Clip(string packRelative) => ClipAt(PackRoot, packRelative);

        static AudioClip ClipAt(string root, string relative)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(root + relative);
            if (!clip)
                Missing.Add(root + relative);
            return clip;
        }

        static AudioClip[] ClipArrayAt(string root, params string[] relatives)
        {
            var clips = new List<AudioClip>(relatives.Length);
            foreach (var relative in relatives)
            {
                var clip = ClipAt(root, relative);
                if (clip)
                    clips.Add(clip);
            }
            return clips.ToArray();
        }

        static AudioClip[] ClipArray(params string[] packRelatives)
        {
            var clips = new List<AudioClip>(packRelatives.Length);
            foreach (var relative in packRelatives)
            {
                var clip = Clip(relative);
                if (clip)
                    clips.Add(clip);
            }
            return clips.ToArray();
        }
    }
}
