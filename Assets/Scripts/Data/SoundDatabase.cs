using System;
using UnityEngine;

namespace LivingCity.Data
{
    /// <summary>
    /// Every sound the city plays, by role. Populated from the 400 Sounds Pack by
    /// SoundAssetBootstrap the same way PrefabDatabase is populated from the model packs -
    /// paths live in the editor baker, runtime only ever reads this asset.
    ///
    /// Every slot is allowed to be empty. A missing clip means that layer stays silent, not
    /// that anything throws - the same non-fatal discipline as the prefab baker's Missing
    /// list, and it is what lets this compile and run before the pack is even downloaded.
    /// </summary>
    [CreateAssetMenu(fileName = "SoundDatabase", menuName = "Living City/Sound Database")]
    public sealed class SoundDatabase : ScriptableObject
    {
        /// <summary>A looping bed: one clip and how loud it sits in the mix.</summary>
        [Serializable]
        public sealed class Bed
        {
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 1f;
        }

        [Header("Ambience")]
        [Tooltip("The city's daylight tone. Crossfaded with the night bed by CityWeather.Nightness.")]
        public Bed cityDayBed = new Bed();
        [Tooltip("The city after dark. The other end of the same crossfade.")]
        public Bed cityNightBed = new Bed();

        [Tooltip("Extra layer per weather, chosen once per city like the weather itself.")]
        public Bed clearDuskLayer = new Bed();
        public Bed overcastLayer = new Bed();
        public Bed smogLayer = new Bed();

        [Header("Traffic")]
        [Tooltip("Engine hum loops. Several variants so two adjacent cars do not phase " +
                 "against each other on the same recording.")]
        public AudioClip[] engineLoops = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float engineVolume = 0.6f;
        [Tooltip("Pitch at standstill and at full speed. The hum is one loop; speed is pitch.")]
        public float enginePitchMin = 0.85f;
        public float enginePitchMax = 1.25f;

        [Header("Weapons")]
        [Tooltip("Gunshot variants, drawn at random per shot.")]
        public AudioClip[] gunshots = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float gunshotVolume = 1f;

        [Header("Pedestrians")]
        [Tooltip("Footstep one-shots on paving. Variants, drawn at random per step.")]
        public AudioClip[] footsteps = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float footstepVolume = 0.35f;

        [Tooltip("Crowd murmur loop. One 2D bed scaled by how many people are near the " +
                 "camera focus - never a source per pedestrian.")]
        public AudioClip[] crowdMurmurLoops = Array.Empty<AudioClip>();
        [Range(0f, 1f)] public float murmurVolume = 0.5f;

        [Header("Global")]
        [Range(0f, 1f)] public float masterVolume = 1f;

        [Tooltip("How far a positional one-shot carries, metres from the listener rig.")]
        [Min(1f)] public float detailMaxDistance = 60f;

        [Tooltip("Positional one-shot voices kept pre-created. When all are busy a new " +
                 "sound is dropped rather than allocated.")]
        [Range(1, 64)] public int oneShotVoices = 16;

        /// <summary>Random element of an array, or null when the slot is empty.</summary>
        public static AudioClip Pick(AudioClip[] clips, System.Random rng)
        {
            if (clips == null || clips.Length == 0)
                return null;
            return clips[rng.Next(clips.Length)];
        }
    }
}
