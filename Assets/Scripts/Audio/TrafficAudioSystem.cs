using UnityEngine;
using LivingCity.Data;
using LivingCity.Entities;

namespace LivingCity.Audio
{
    /// <summary>
    /// Engine hum for the cars nearest the camera focus.
    ///
    /// A handful of looping voices, not a source per car: thirty cars needing at most six
    /// audible is the same shape as ten thousand pedestrians needing a murmur bed, just
    /// smaller. Every reassignment cadence the registry is scanned for the closest live
    /// bodies; between scans each voice simply follows its car and tracks its speed.
    ///
    /// Speed maps to PITCH on one loop rather than to clip swaps - an idle and a working
    /// engine are the same recording a few semitones apart, and a swap boundary would click
    /// every time a car pulled away from a light.
    ///
    /// Reads TrafficRegistry.All, which the project owns. CarBehavior - pack code, patched
    /// once already - is deliberately not touched again.
    /// </summary>
    public sealed class TrafficAudioSystem : MonoBehaviour
    {
        [Tooltip("Audible engines at once. Six covers everything a mid zoom actually shows.")]
        [SerializeField, Range(1, 12)] int maxVoices = 6;

        [Tooltip("Seconds between scans for which cars deserve the voices.")]
        [SerializeField, Min(0.1f)] float reassignInterval = 0.5f;

        [Tooltip("Speed in m/s that reaches the top of the pitch ramp.")]
        [SerializeField, Min(1f)] float fullSpeedMs = 12f;

        [SerializeField] SoundDatabase sounds;

        CityAudioDirector director;
        AudioSource[] voices;
        TrafficBody[] assigned;
        float nextReassign;

        void Awake()
        {
            director = GetComponent<CityAudioDirector>();

            voices = new AudioSource[maxVoices];
            assigned = new TrafficBody[maxVoices];

            for (var i = 0; i < maxVoices; i++)
            {
                var child = new GameObject($"Engine Voice {i}");
                child.transform.SetParent(transform, worldPositionStays: false);

                var source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 5f;
                source.maxDistance = sounds ? sounds.detailMaxDistance : 60f;
                voices[i] = source;
            }
        }

        void LateUpdate()
        {
            if (!director || sounds == null || sounds.engineLoops.Length == 0)
                return;

            if (Time.time >= nextReassign)
            {
                nextReassign = Time.time + reassignInterval;
                Reassign();
            }

            FollowCars();
        }

        /// <summary>
        /// Hands the voices to the nearest cars. A car already holding a voice competes with
        /// its distance scaled down - the hysteresis that stops a pair straddling the cutoff
        /// from trading a voice back and forth every scan, restarting the loop each time.
        /// </summary>
        void Reassign()
        {
            var focus = director.Focus;

            // Nothing beyond earshot of the current view competes at all.
            var radius = Mathf.Max(30f, director.Ortho) + (sounds ? sounds.detailMaxDistance : 60f);
            var radiusSqr = radius * radius;

            var bodies = TrafficRegistry.All;

            for (var slot = 0; slot < voices.Length; slot++)
            {
                TrafficBody best = null;
                var bestScore = radiusSqr;

                for (var i = 0; i < bodies.Count; i++)
                {
                    var body = bodies[i];
                    if (body == null || !body.Tf || AlreadyTaken(body, slot))
                        continue;

                    var delta = body.Tf.position - focus;
                    delta.y = 0f;
                    var score = delta.sqrMagnitude;

                    if (body == assigned[slot])
                        score *= 0.64f; // Holding the voice is worth a 20% distance edge.

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = body;
                    }
                }

                if (best != assigned[slot])
                    Attach(slot, best);
            }
        }

        bool AlreadyTaken(TrafficBody body, int beforeSlot)
        {
            for (var i = 0; i < beforeSlot; i++)
                if (assigned[i] == body)
                    return true;
            return false;
        }

        void Attach(int slot, TrafficBody body)
        {
            assigned[slot] = body;
            var voice = voices[slot];

            if (body == null)
            {
                voice.Stop();
                return;
            }

            var clip = sounds.engineLoops[
                Mathf.Abs(body.Id.GetHashCode()) % sounds.engineLoops.Length];

            voice.clip = clip;
            // Every car starts its loop somewhere else in the recording, or six engines from
            // one file play as one big engine with chorus.
            voice.time = Mathf.Abs(body.Id.GetHashCode() % 1000) / 1000f * clip.length;
            voice.Play();
        }

        void FollowCars()
        {
            var gain = sounds.engineVolume * sounds.masterVolume * director.DetailGain;

            for (var i = 0; i < voices.Length; i++)
            {
                var body = assigned[i];
                var voice = voices[i];

                if (body == null)
                    continue;

                if (!body.Tf)
                {
                    // The car was despawned out from under us; drop the voice now rather
                    // than leaving it playing at the graveyard position until the next scan.
                    Attach(i, null);
                    continue;
                }

                voice.transform.position = body.Tf.position;
                voice.pitch = Mathf.Lerp(sounds.enginePitchMin, sounds.enginePitchMax,
                    body.SpeedMs / fullSpeedMs);
                voice.volume = gain;
            }
        }
    }
}
