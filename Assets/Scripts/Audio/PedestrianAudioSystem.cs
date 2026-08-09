using UnityEngine;
using LivingCity.Data;
using LivingCity.Entities;

namespace LivingCity.Audio
{
    /// <summary>
    /// What a crowd sounds like, at crowd scale.
    ///
    /// Ten thousand pedestrians and not one of them owns an AudioSource. Two layers stand in
    /// for all of it:
    ///
    ///   MURMUR - one looping 2D bed whose volume follows how many people are near the camera
    ///   focus. One spatial-hash query a few times a second buys "a busy street sounds busy,
    ///   an empty one doesn't" at any population.
    ///
    ///   FOOTSTEPS - a budgeted trickle of positional one-shots on walkers sampled near the
    ///   focus. Not synced to any animation on purpose: from an isometric camera a plausible
    ///   patter at the right density is indistinguishable from exact foot-to-ground sync, and
    ///   sync would mean touching every pedestrian every frame - the exact cost the LOD pass
    ///   exists to avoid. The budget scales with DetailGain, so steps fade out across the same
    ///   zoom band where the LOD stops animating the walkers they would belong to.
    /// </summary>
    public sealed class PedestrianAudioSystem : MonoBehaviour
    {
        [Tooltip("Metres around the focus the murmur census counts.")]
        [SerializeField, Min(5f)] float murmurRadius = 25f;

        [Tooltip("People in earshot at which the murmur reaches full volume.")]
        [SerializeField, Min(1)] int fullCrowd = 30;

        [Tooltip("Seconds between crowd censuses. The volume itself is smoothed every frame.")]
        [SerializeField, Min(0.05f)] float censusInterval = 0.25f;

        [Tooltip("Footstep one-shots per second when fully zoomed in.")]
        [SerializeField, Min(0f)] float footstepsPerSecond = 4f;

        [SerializeField] SoundDatabase sounds;

        CityAudioDirector director;
        AudioSource murmur;
        float murmurTarget;
        float nextCensus;
        float stepBudget;

        // Its own stream, like the director's: audio randomness must never touch the city's.
        readonly System.Random rng = new System.Random(1177);

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
            EmitFootsteps();
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

        /// <summary>
        /// The footstep trickle. An accumulator rather than a timer: at 4 steps/second and
        /// DetailGain 0.3 it correctly emits 1.2 steps' worth, where a timer would emit either
        /// 4 or none.
        /// </summary>
        void EmitFootsteps()
        {
            if (sounds.footsteps.Length == 0)
                return;

            stepBudget += footstepsPerSecond * director.DetailGain * Time.deltaTime;
            if (stepBudget < 1f)
                return;

            stepBudget -= 1f;

            // Steps land on people the view can plausibly attribute them to - inside roughly
            // the visible half-frame, not the whole murmur radius.
            var reach = Mathf.Min(murmurRadius, director.Ortho * 0.5f);
            var body = PedestrianRegistry.SampleNear(director.Focus, reach, rng);

            if (body == null || !body.Tf || body.Stationary || body.SpeedMs < 0.3f)
                return; // This tick's step is forfeit, not deferred - the budget is a cap.

            CityAudioDirector.PlayOneShot(
                SoundDatabase.Pick(sounds.footsteps, rng),
                body.Tf.position,
                sounds.footstepVolume,
                pitchJitter: 0.12f);
        }
    }
}
