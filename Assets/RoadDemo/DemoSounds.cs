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
    // The clips live in Assets/Audio, cut out of the Sonniss GDC library by
    // Tools/audio/import_sounds.py - which is where a bad cut gets fixed, not here.
    // Two things the library did not have: the guns, which come from the Krotos
    // Studio free gun pack, and a siren, which is synthesized. Neither carries an
    // attribution obligation - Tools/audio/sources/SOURCES.md keeps that ledger.
    //
    // Every slot may come back null and every array may come back empty. A missing
    // clip means that layer stays silent, never that anything throws - the crowd's
    // own rule for a missing animation clip.
    public static class DemoSounds
    {
        const string Root = "Assets/Audio/";

        // ------------------------------------------------------------- the levels
        //
        // Trims live here rather than on the component so the whole mix is one
        // screen. Chosen against the demo's default boom (190 m out, ear parked on
        // the focus) - a closer camera hears the same balance louder, not different.
        //
        // The clips are levelled at import (beds to a common RMS, one-shots to a
        // common peak), so these are balance decisions and not gain-staging: a bed
        // that comes back too loud is a wrong number here, not a wrong file.

        public const float Master = 1f;
        public const float DayBedVolume = 0.16f;
        public const float NightBedVolume = 0.16f;
        public const float TrafficHumVolume = 0.30f;
        public const float MurmurVolume = 0.22f;
        public const float EngineVolume = 0.32f;
        public const float PassByVolume = 0.45f;
        public const float StreetVoiceVolume = 0.24f;
        public const float DoorVolume = 0.35f;
        public const float UiVolume = 0.35f;
        public const float ScreamVolume = 0.7f;
        public const float PunchVolume = 0.78f;
        public const float SirenVolume = 0.55f;

        /// <summary>The gun, and it is meant to be the loudest thing in the city -
        /// a report played at half volume under a street of traffic is a door
        /// closing. It runs flat out; the reports are baked with the headroom to
        /// take it (import_sounds.slam) and the city's own SoundDatabase already
        /// fires its generic gunshot at 1.</summary>
        public const float GunVolume = 1f;

        /// <summary>The round going past the ear, which layers under the report and
        /// must stay under it - it is the whip, not the gun.</summary>
        public const float BulletCrackVolume = 0.6f;

        // ---------------------------------------------------------------- the beds

        /// <summary>Daylight bed: a calm courtyard street, distant traffic and
        /// children two blocks over.</summary>
        public static AudioClip DayBed => Load<AudioClip>(Root + "Ambience/city_day.wav");

        /// <summary>After dark: the same city with the traffic thinned and the walla
        /// come out of the bars.</summary>
        public static AudioClip NightBed => Load<AudioClip>(Root + "Ambience/city_night.wav");

        /// <summary>What a block of traffic sounds like from four streets away -
        /// a downtown bed with its top rolled off. It replaces the old trick of
        /// pitching an engine loop down an octave and a half, which sounded like
        /// one enormous car rather than like many small ones.</summary>
        public static AudioClip TrafficHum => Load<AudioClip>(Root + "Ambience/traffic_hum.wav");

        /// <summary>Crowd murmur, scaled by how many people are near the focus.
        /// Never a source per pedestrian.</summary>
        public static AudioClip Murmur => Load<AudioClip>(Root + "Ambience/crowd_walla.wav");

        /// <summary>The murmur from further off - low-passed until no word in it
        /// survives, which is the only way a bed of English-language walla is safe
        /// to loop under an English-language city.</summary>
        public static AudioClip MurmurFar => Load<AudioClip>(Root + "Ambience/crowd_walla_far.wav");

        // -------------------------------------------------------------- the street

        /// <summary>Engine idles, off a 5-litre Mercury loafing: a stretch of steady
        /// RPM looped on whole firing cycles. Two of them, the second a shade slower,
        /// so two cars side by side do not phase against one recording.</summary>
        public static AudioClip[] EngineLoops =>
            _engines ??= Gather(Load<AudioClip>(Root + "Traffic/engine_idle_a.wav"),
                                Load<AudioClip>(Root + "Traffic/engine_idle_b.wav"));

        /// <summary>The diesel idle, for anything with a flatbed on it.</summary>
        public static AudioClip EngineDiesel => Load<AudioClip>(Root + "Traffic/engine_diesel.wav");

        public static AudioClip Skid => Load<AudioClip>(Root + "Traffic/tyre_skid.wav");
        public static AudioClip CarPassBy => Load<AudioClip>(Root + "Traffic/car_pass_by.wav");
        public static AudioClip TruckPassBy => Load<AudioClip>(Root + "Traffic/truck_pass_by.wav");

        // The footstep clips in Assets/Audio/People are deliberately not listed. The
        // demo has no footstep layer: at any rate loud enough to hear, a 0.19 s
        // concrete crack repeated over a crowd reads as tapping rather than as
        // people, and the crowd murmur already carries the pavement.

        /// <summary>A man on the pavement, once in a while: a whistle, a cough, a
        /// laugh. Not chatter - the murmur bed is the chatter, and a per-body voice
        /// line at this camera distance only ever reads as one person shouting.</summary>
        public static AudioClip[] StreetVoices =>
            _voices ??= Gather(Load<AudioClip>(Root + "People/whistle.wav"),
                               Load<AudioClip>(Root + "People/cough.wav"),
                               Load<AudioClip>(Root + "People/laugh_m.wav"),
                               Load<AudioClip>(Root + "People/laugh_f.wav"),
                               Load<AudioClip>(Root + "People/dog_bark.wav"));

        /// <summary>The crowd under fire: a shocked gasp, two panic yells, and the
        /// hurts for whoever the round found.</summary>
        public static AudioClip[] Screams =>
            _screams ??= Gather(Load<AudioClip>(Root + "People/panic_gasp.wav"),
                                Load<AudioClip>(Root + "People/panic_yell_m.wav"),
                                Load<AudioClip>(Root + "People/panic_scream_f.wav"),
                                Load<AudioClip>(Root + "People/hurt_m.wav"),
                                Load<AudioClip>(Root + "People/hurt_f.wav"));

        /// <summary>Three distinct blows heard from behind the closed shop door.</summary>
        public static AudioClip[] Punches =>
            _punches ??= Gather(Load<AudioClip>(Root + "Weapons/punch_2.wav"),
                                Load<AudioClip>(Root + "Weapons/punch_3.wav"),
                                Load<AudioClip>(Root + "Weapons/punch_4.wav"));

        // -------------------------------------------------------------- the police

        /// <summary>The patrol car's wail - a Federal Signal style sweep, the
        /// American electronic siren of the period. Synthesized: the library has no
        /// siren of any kind. A loop; the car carries it.</summary>
        public static AudioClip Siren => Load<AudioClip>(Root + "Police/siren_loop.wav");

        /// <summary>Dispatch, band-limited to what a 1987 set passes.</summary>
        public static AudioClip[] RadioCalls =>
            _radio ??= Gather(Load<AudioClip>(Root + "Police/radio_call_1.wav"),
                              Load<AudioClip>(Root + "Police/radio_call_2.wav"),
                              Load<AudioClip>(Root + "Police/radio_call_3.wav"));

        public static AudioClip RadioSquelch => Load<AudioClip>(Root + "Police/radio_squelch.wav");
        public static AudioClip ShotsFired => Load<AudioClip>(Root + "Police/cop_shots_fired.wav");

        /// <summary>The door a walker goes through - a stairwell door, heavy and
        /// American. Not the car's: a Mercury's door shutting at a shop front is the
        /// wrong weight and the wrong century of hinge.</summary>
        public static AudioClip DoorOpen => Load<AudioClip>(Root + "People/door_open.wav");
        public static AudioClip DoorClose => Load<AudioClip>(Root + "People/door_close.wav");

        public static AudioClip CarDoorOpen => Load<AudioClip>(Root + "Traffic/car_door_open.wav");
        public static AudioClip CarDoorClose => Load<AudioClip>(Root + "Traffic/car_door_close.wav");

        // ------------------------------------------------------------------- the UI

        // Mechanisms and paper, not glass: a bakelite double-click, a radio's power
        // button, a sheet of the same paper the ledger is drawn on.
        public static AudioClip UiClick => Load<AudioClip>(Root + "Ui/click.wav");
        public static AudioClip UiToggleOn => Load<AudioClip>(Root + "Ui/toggle_on.wav");
        public static AudioClip UiToggleOff => Load<AudioClip>(Root + "Ui/toggle_off.wav");
        public static AudioClip MapOpen => Load<AudioClip>(Root + "Ui/map_open.wav");
        public static AudioClip MapClose => Load<AudioClip>(Root + "Ui/map_close.wav");
        /// <summary>The lot plan going over the city - paper, because that is what
        /// the overlay is drawn as.</summary>
        public static AudioClip Paper => Load<AudioClip>(Root + "Ui/paper_rustle.wav");
        /// <summary>The morning edition landing on the desk.</summary>
        public static AudioClip Newspaper => Load<AudioClip>(Root + "Ui/newspaper_slap.wav");

        static AudioClip[] _engines, _voices, _screams, _punches, _radio;

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
            return RoadDemo.DemoAssetLoad.Load<T>(path);
#else
            return null;
#endif
        }
    }
}
