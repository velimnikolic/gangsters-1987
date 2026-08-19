using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Bakes Assets/Configs/SoundDatabase.asset from Assets/Audio, exactly as
    /// CityAssetBootstrap bakes PrefabDatabase from the model packs: paths are consts here,
    /// missing files collect into one warning, runtime only ever sees the asset.
    ///
    /// Its own file rather than another region of CityAssetBootstrap - that file is 2,600
    /// lines and the two bakers share nothing but a habit.
    ///
    /// Assets/Audio is itself baked, out of the Sonniss GDC library by
    /// Tools/audio/import_sounds.py. That script is where a clip is chosen, cut and
    /// levelled, and where the two re-purposings it had to make are argued; this one only
    /// says which of its clips fills which of the city's slots.
    ///
    /// CLIP choices are re-baked on every run; VOLUME/PITCH trims are seeded only when the
    /// asset is first created, so tuning done in the Inspector survives a re-bake - the
    /// same contract GetOrCreate gives CityConfig.
    /// </summary>
    public static class SoundAssetBootstrap
    {
        const string AudioRoot = "Assets/Audio/";
        const string ConfigDir = "Assets/Configs";

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
            db.cityDayBed.clip = Clip("Ambience/city_day.wav");
            db.cityNightBed.clip = Clip("Ambience/city_night.wav");

            // Dusk keeps the ballast hum off the streetlights coming on; overcast gets the
            // wind through the trees, and smog the rain bed run very low, which is the only
            // layer in the library that reads as air you can see.
            db.clearDuskLayer.clip = Clip("Ambience/neon_hum.wav");
            db.overcastLayer.clip = Clip("Ambience/wind_gusts.wav");
            db.smogLayer.clip = Clip("Ambience/rain_city.wav");

            // -- traffic ----------------------------------------------------------------
            // A 5-litre Mercury loafing, looped on whole firing cycles, and the same take a
            // shade slower so two adjacent cars do not phase against one recording. Unity
            // pitch on top of these stays inside about 0.85-1.35; past that a car changes
            // displacement rather than speed.
            db.engineLoops = ClipArray(
                "Traffic/engine_idle_a.wav",
                "Traffic/engine_idle_b.wav");

            // -- weapons ----------------------------------------------------------------
            // Four renderings of one light machine gun take off OpenGameArt - the only
            // firearm the project has, and the only clip in it that must be credited.
            db.gunshots = ClipArray(
                "Weapons/gunshot_1.wav",
                "Weapons/gunshot_2.wav",
                "Weapons/gunshot_3.wav",
                "Weapons/gunshot_4.wav");

            // -- pedestrians ------------------------------------------------------------
            db.footsteps = ClipArray(
                "People/footstep_concrete_1.wav",
                "People/footstep_concrete_2.wav",
                "People/footstep_concrete_3.wav",
                "People/footstep_concrete_4.wav",
                "People/footstep_concrete_5.wav",
                "People/footstep_concrete_6.wav");

            // The murmur slot, empty since the polyperfect people pack left with the only
            // crowd recording the project had. Two loops: a hall of walla and footsteps for
            // near, and a far one low-passed until no word in it survives - which is what
            // makes a bed of somebody else's language safe to loop under this city.
            db.crowdMurmurLoops = ClipArray(
                "Ambience/crowd_walla.wav",
                "Ambience/crowd_walla_far.wav");

            if (created)
                SeedTrims(db);

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            if (Missing.Count > 0)
                Debug.LogWarning(
                    $"[SoundBootstrap] {Missing.Count} clip(s) not found - those layers stay " +
                    $"silent:\n{string.Join("\n", Missing)}\n" +
                    "Run 'python Tools/audio/import_sounds.py' to bake Assets/Audio.");
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
            db.cityDayBed.volume = 0.18f;
            db.cityNightBed.volume = 0.18f;
            db.clearDuskLayer.volume = 0.06f;
            db.overcastLayer.volume = 0.1f;
            db.smogLayer.volume = 0.05f;

            db.engineVolume = 0.4f;
            db.enginePitchMin = 0.95f;
            db.enginePitchMax = 1.35f;

            db.gunshotVolume = 1f;
            db.footstepVolume = 0.35f;
            db.murmurVolume = 0.22f;
            db.masterVolume = 1f;
        }

        static AudioClip Clip(string relative)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioRoot + relative);
            if (!clip)
                Missing.Add(AudioRoot + relative);
            return clip;
        }

        static AudioClip[] ClipArray(params string[] relatives)
        {
            var clips = new List<AudioClip>(relatives.Length);
            foreach (var relative in relatives)
            {
                var clip = Clip(relative);
                if (clip)
                    clips.Add(clip);
            }
            return clips.ToArray();
        }
    }
}
