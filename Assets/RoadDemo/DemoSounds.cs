using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // Every clip the road demo plays, by role, loaded straight off disk through the
    // AssetDatabase - the same editor-only contract as CrewKit: nothing here ships,
    // so nothing needs Resources or a baked database. The city's own SoundDatabase
    // is deliberately not read: the demo owns its whole audio stack the way it owns
    // its clock and its sky.
    //
    // The 400 Sounds Pack is a general-purpose library, not a city soundscape, so a
    // few slots are honest re-purposings (noted per slot). What the pack simply does
    // not have - an engine idle, a car horn - is synthesized into
    // Assets/ci/Generated Sounds/ instead (scratchpad gen_engine.py, gen_horn.py);
    // the project's audio postprocessor already covers that folder.
    //
    // Every slot may come back null and every array may come back empty. A missing
    // clip means that layer stays silent, never that anything throws - the crowd's
    // own rule for a missing animation clip.
    public static class DemoSounds
    {
        const string Pack = "Assets/ci/400 Sounds Pack/";
        const string Gen = "Assets/ci/Generated Sounds/";

        // ------------------------------------------------------------- the levels
        //
        // Trims live here rather than on the component so the whole mix is one
        // screen. Chosen against the demo's default boom (190 m out, ear parked on
        // the focus) - a closer camera hears the same balance louder, not different.

        public const float Master = 1f;
        public const float DayBedVolume = 0.16f;
        public const float NightBedVolume = 0.05f;
        public const float TrafficHumVolume = 0.13f;
        public const float EngineVolume = 0.32f;
        public const float HornVolume = 0.5f;
        public const float FootstepVolume = 0.3f;
        public const float StreetVoiceVolume = 0.24f;
        public const float DoorVolume = 0.35f;
        public const float UiVolume = 0.35f;

        // ---------------------------------------------------------------- the beds

        /// <summary>Daylight bed. The pack's one outdoor loop, and the only honest
        /// ambience in it.</summary>
        public static AudioClip Wind => Load<AudioClip>(Pack + "Environment/ambient_wind.wav");

        /// <summary>After dark. Low white noise standing in for the air over a city
        /// at night - it lives or dies by its trim, which is why NightBedVolume is a
        /// third of the day bed's.</summary>
        public static AudioClip NightAir => Load<AudioClip>(Pack + "Other/white_noise_long.wav");

        // -------------------------------------------------------------- the street

        /// <summary>Engine idles. Synthesized: no recording in the pack is an engine,
        /// and appliances pitched down read as mosquitoes - their energy sits in the
        /// kHz band however far the pitch drops. Two of them so two cars side by side
        /// do not phase against one recording.</summary>
        public static AudioClip[] EngineLoops =>
            _engines ??= Gather(Load<AudioClip>(Gen + "engine_idle_a.wav"),
                                Load<AudioClip>(Gen + "engine_idle_b.wav"));

        /// <summary>Horns, also synthesized - two reeds a rough major third apart,
        /// a tap and a lean.</summary>
        public static AudioClip[] Horns =>
            _horns ??= Gather(Load<AudioClip>(Gen + "horn_short.wav"),
                              Load<AudioClip>(Gen + "horn_long.wav"));

        public static AudioClip[] Footsteps =>
            _footsteps ??= Gather(
                Load<AudioClip>(Pack + "Footsteps/foley_footstep_concrete_1.wav"),
                Load<AudioClip>(Pack + "Footsteps/foley_footstep_concrete_2.wav"),
                Load<AudioClip>(Pack + "Footsteps/foley_footstep_concrete_3.wav"),
                Load<AudioClip>(Pack + "Footsteps/foley_footstep_concrete_4.wav"));

        /// <summary>A man on the pavement, once in a while. The pack has no crowd
        /// walla at all, so there is no murmur bed to point at - what it does have is
        /// a whistle and a cough, which are unambiguous enough to carry the street on
        /// their own. The male grunts next to them (Human/man_*) are game hurt
        /// sounds, not chatter, and are left alone on purpose.</summary>
        public static AudioClip[] StreetVoices =>
            _voices ??= Gather(Load<AudioClip>(Pack + "Human/whistle.wav"),
                               Load<AudioClip>(Pack + "Human/cough_short.wav"),
                               Load<AudioClip>(Pack + "Human/cough_double.wav"));

        public static AudioClip DoorOpen => Load<AudioClip>(Pack + "Environment/door_open.wav");
        public static AudioClip DoorClose => Load<AudioClip>(Pack + "Environment/door_close.wav");

        // ------------------------------------------------------------------- the UI

        public static AudioClip UiClick => Load<AudioClip>(Pack + "UI/select_1.wav");
        public static AudioClip UiToggleOn => Load<AudioClip>(Pack + "UI/toggle_on.wav");
        public static AudioClip UiToggleOff => Load<AudioClip>(Pack + "UI/toggle_off.wav");
        public static AudioClip MapOpen => Load<AudioClip>(Pack + "Items/map_open.wav");
        public static AudioClip MapClose => Load<AudioClip>(Pack + "Items/map_close.wav");
        /// <summary>The lot plan going over the city - paper, because that is what
        /// the overlay is drawn as.</summary>
        public static AudioClip Paper => Load<AudioClip>(Pack + "Materials/paper_move.wav");

        static AudioClip[] _engines, _horns, _footsteps, _voices;

        public static AudioClip Pick(AudioClip[] clips) =>
            clips == null || clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];

        static AudioClip[] Gather(params AudioClip[] candidates)
        {
            var list = new List<AudioClip>(candidates.Length);
            foreach (var clip in candidates)
                if (clip != null)
                    list.Add(clip);
            return list.ToArray();
        }

        static T Load<T>(string path) where T : Object
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
#else
            return null;
#endif
        }
    }
}
