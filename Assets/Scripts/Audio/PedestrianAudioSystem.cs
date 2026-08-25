using UnityEngine;
using LivingCity.Data;
using LivingCity.Entities;

namespace LivingCity.Audio
{
    /// <summary>
    /// What a crowd sounds like, at crowd scale.
    ///
    /// Ten thousand pedestrians and not one of them owns an AudioSource. One layer stands in
    /// for all of it:
    ///
    ///   MURMUR - one looping 2D bed whose volume follows how many people are near the camera
    ///   focus. One spatial-hash query a few times a second buys "a busy street sounds busy,
    ///   an empty one doesn't" at any population.
    ///
    /// There is no footstep layer. A trickle of positional one-shots over the crowd was the
    /// obvious second half of this and it was wrong in the ear: the concrete clip is 0.19 s,
    /// so any rate loud enough to register lays the cracks end to end and the street taps
    /// instead of walking. The mass of a crowd is a bed, not a set of events.
    /// </summary>
    public sealed class PedestrianAudioSystem : MonoBehaviour
    {
        [Tooltip("Metres around the focus the murmur census counts.")]
        [SerializeField, Min(5f)] float murmurRadius = 25f;

        [Tooltip("People in earshot at which the murmur reaches full volume.")]
        [SerializeField, Min(1)] int fullCrowd = 30;

        [Tooltip("Seconds between crowd censuses. The volume itself is smoothed every frame.")]
        [SerializeField, Min(0.05f)] float censusInterval = 0.25f;

        [SerializeField] SoundDatabase sounds;

        CityAudioDirector director;
        AudioSource murmur;
        float murmurTarget;
        float nextCensus;

        void Awake()
        {
            director = GetComponent<CityAudioDirector>();

            var child = new GameObject("Crowd Murmur");
            child.transform.SetParent(transform, worldPositionStays: false);

            murmur = child.AddComponent<AudioSource>();
            murmur.playOnAwake = false;
            murmur.loop = true;
            murmur.spatialBlend = 0f;
            murmur.volume = 0f;
        }

        void LateUpdate()
        {
            if (!director || sounds == null)
                return;

            UpdateMurmur();
        }

        void UpdateMurmur()
        {
            if (sounds.crowdMurmurLoops.Length == 0)
                return;

            if (!murmur.isPlaying)
            {
                murmur.clip = sounds.crowdMurmurLoops[0];
                murmur.Play();
            }

            if (Time.time >= nextCensus)
            {
                nextCensus = Time.time + censusInterval;

                var count = PedestrianRegistry.CountNear(director.Focus, murmurRadius);

                // Square root: the tenth person added to a street changes its sound far more
                // than the thirtieth. Linear reads as silence until the street is packed.
                var crowd = Mathf.Sqrt(Mathf.Clamp01(count / (float)fullCrowd));

                // The murmur thins on the wide view but never vanishes - a city seen from
                // above still hums. Detail sounds die at far zoom; the crowd as a MASS is
                // ambience and stays.
                var zoom = Mathf.Lerp(0.4f, 1f, director.DetailGain);
                murmurTarget = crowd * zoom * sounds.murmurVolume * sounds.masterVolume;
            }

            // One smoothed value between censuses, so a burst of spawns cannot click the bed.
            murmur.volume = Mathf.MoveTowards(murmur.volume, murmurTarget, Time.deltaTime * 0.5f);
        }
    }
}
