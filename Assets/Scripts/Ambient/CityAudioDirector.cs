using UnityEngine;
using LivingCity.Ambient;
using LivingCity.CameraRig;
using LivingCity.Data;

namespace LivingCity.Audio
{
    /// <summary>
    /// The city's ears and its background sound.
    ///
    /// Three jobs live here because they share one piece of geometry - where the listener is:
    ///
    ///   THE LISTENER RIG. The camera is orthographic and sits 200m up the boom, so a listener
    ///   on the camera hears everything at 200m - equally distant, equally quiet, no panning.
    ///   The rig is a child object holding the scene's ONLY AudioListener, parked every frame
    ///   at the camera's ground focus point with the camera's yaw. Down on the ground plane,
    ///   plain Unity 3D sources with linear rolloff just work: nearby cars are close, the far
    ///   side of the city is far, and rotating with Q/E swings the stereo field round.
    ///
    ///   THE ONE-SHOT POOL. A fixed set of positional AudioSources handed out on demand -
    ///   footsteps, gunshots, anything fire-and-forget. A full pool drops the sound instead of
    ///   growing: with thousands of pedestrians the pool IS the polyphony budget, exactly as
    ///   the LOD tiers are the animation budget.
    ///
    ///   THE AMBIENCE BEDS. Looping 2D sources: a day tone and a night tone crossfaded by the
    ///   same CityWeather.Nightness curve the lighting uses (one authority on what "night" is),
    ///   plus one layer per weather - rolled once per city, like the weather itself.
    ///
    /// Zoom is the fourth thing everyone asks this class about. DetailGain runs 1 to 0 across
    /// the SAME ortho boundaries as PedestrianLodSystem, so sound detail fades out exactly
    /// where animation detail does - when the walkers freeze at a distance, they also go quiet.
    /// </summary>
    public sealed class CityAudioDirector : MonoBehaviour
    {
        [SerializeField] CityConfig config;
        [SerializeField] SoundDatabase sounds;

        [Header("Scene wiring")]
        [Tooltip("Time of day, for the day/night crossfade. Found in the scene when empty.")]
        [SerializeField] CityClock clock;
        [Tooltip("Which weather layer plays. Found in the scene when empty.")]
        [SerializeField] CityWeather weather;

        /// <summary>The live director, for static one-shot calls. Null outside Play.</summary>
        public static CityAudioDirector Instance { get; private set; }

        /// <summary>
        /// How much close-up sound detail is in force at the current zoom: 1 fully zoomed in,
        /// 0 once the view is wide enough that individual people stop reading as individuals.
        /// </summary>
        public float DetailGain { get; private set; } = 1f;

        /// <summary>Where the listener stands - the camera's ground focus. World space.</summary>
        public Vector3 Focus { get; private set; }

        /// <summary>Current camera ortho size, for anything sizing a query to the view.</summary>
        public float Ortho { get; private set; } = 40f;

        Transform rig;
        IsometricCameraController cameraController;
        Camera cam;

        AudioSource[] oneShotPool;
        float[] oneShotBusyUntil;

        AudioSource dayBed;
        AudioSource nightBed;
        AudioSource weatherBed;
        WeatherKind weatherBedKind;
        bool weatherBedAssigned;

        // Audio picks its own randomness rather than borrowing the city's seeded stream: which
        // footstep variant plays must never be able to relayout a city.
        readonly System.Random rng = new System.Random(4842);

        void Awake()
        {
            AdoptMissingWiring();
            BuildRig();
            BuildOneShotPool();
            BuildBeds();
        }

        void OnEnable()
        {
            Instance = this;

            // The saved scene has a listener baked onto the camera from before the rig existed.
            // Two listeners is a per-frame Unity warning; the rig's must be the one that wins.
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (rig == null || listener.transform != rig)
                    listener.enabled = false;

            if (rig != null && rig.TryGetComponent<AudioListener>(out var ours))
                ours.enabled = true;
        }

        void OnDisable()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Same self-heal as CityWeather's: every reference here can be empty in a scene saved
        /// before the field existed, and the silent fallback would be a city with no sound and
        /// nothing in the Console explaining why.
        /// </summary>
        void AdoptMissingWiring()
        {
            if (!clock)
                clock = FindAnyObjectByType<CityClock>();

            if (!weather)
                weather = FindAnyObjectByType<CityWeather>();

            if (!config)
            {
                var builder = FindAnyObjectByType<Generation.CityBuilder>();
                if (builder)
                    config = builder.Config;
            }
        }

        void BuildRig()
        {
            var rigObject = new GameObject("Audio Listener Rig");
            rigObject.transform.SetParent(transform, worldPositionStays: false);
            rig = rigObject.transform;
            rigObject.AddComponent<AudioListener>();
        }

        void BuildOneShotPool()
        {
            var voices = sounds ? sounds.oneShotVoices : 16;
            oneShotPool = new AudioSource[voices];
            oneShotBusyUntil = new float[voices];

            for (var i = 0; i < voices; i++)
                oneShotPool[i] = CreateSource($"OneShot {i}", spatial: true, loop: false);
        }

        void BuildBeds()
        {
            dayBed = CreateSource("Bed Day", spatial: false, loop: true);
            nightBed = CreateSource("Bed Night", spatial: false, loop: true);
            weatherBed = CreateSource("Bed Weather", spatial: false, loop: true);

            if (!sounds)
                return;

            StartBed(dayBed, sounds.cityDayBed);
            StartBed(nightBed, sounds.cityNightBed);
            // The weather layer waits for LateUpdate: CityWeather rolls in its own Start and
            // the script order between the two is nobody's business to depend on.
        }

        static void StartBed(AudioSource source, SoundDatabase.Bed bed)
        {
            if (bed == null || !bed.clip)
                return;

            source.clip = bed.clip;
            source.volume = 0f;
            source.Play();
        }

        /// <summary>
        /// One AudioSource, configured the one way this project uses them. Linear rolloff
        /// rather than the default logarithmic: with the listener on the ground plane the
        /// audible radius should read as a circle around the focus, and the 1/d curve keeps
        /// sources audible far past where the eye has lost them - the same mismatch the
        /// street lamps hit with 1/d² light.
        /// </summary>
        AudioSource CreateSource(string name, bool spatial, bool loop)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, worldPositionStays: false);

            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.dopplerLevel = 0f; // The rig teleports on drag-pan; doppler would chirp.
            source.spatialBlend = spatial ? 1f : 0f;

            if (spatial)
            {
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 4f;
                source.maxDistance = sounds ? sounds.detailMaxDistance : 60f;
            }

            return source;
        }

        void LateUpdate()
        {
            PlaceRig();
            UpdateDetailGain();
            UpdateBeds();
        }

        /// <summary>
        /// The rig stands where the camera looks, turned to the camera's yaw only - panning
        /// should swing with Q/E, not tip 45 degrees into the ground with the pitch.
        /// </summary>
        void PlaceRig()
        {
            if (!ResolveCamera())
                return;

            if (cameraController)
            {
                Focus = cameraController.FocusPoint;
                Ortho = cameraController.OrthoSize;
            }
            else
            {
                // No controller - project the camera forward onto the ground plane instead.
                var tf = cam.transform;
                var forward = tf.forward;
                var toGround = Mathf.Abs(forward.y) > 1e-3f ? -tf.position.y / forward.y : 200f;
                Focus = tf.position + forward * Mathf.Clamp(toGround, 0f, 1000f);
                Ortho = cam.orthographicSize;
            }

            rig.SetPositionAndRotation(
                Focus, Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f));
        }

        bool ResolveCamera()
        {
            if (cam)
                return true;

            cam = Camera.main;
            if (!cam)
                return false;

            cameraController = cam.GetComponent<IsometricCameraController>();
            return true;
        }

        /// <summary>
        /// The zoom gate, on the pedestrian LOD's own boundaries. Not a coincidence and not a
        /// separate tuning: when the mid tier freezes walk cycles, their footsteps go with them.
        /// </summary>
        void UpdateDetailGain()
        {
            var mid = config ? config.pedestrianLodMidOrtho : 45f;
            var far = config ? config.pedestrianLodFarOrtho : 90f;
            DetailGain = 1f - Mathf.Clamp01(Mathf.InverseLerp(mid, far, Ortho));
        }

        void UpdateBeds()
        {
            if (!sounds)
                return;

            var master = sounds.masterVolume;
            var night = CityWeather.Nightness(clock ? clock.Hour : 15f);

            // Ambience dips slightly when zoomed right in, so close-up detail reads over it,
            // and opens back up on the wide view where it is the only thing left playing.
            var zoomShape = Mathf.Lerp(1f, 0.8f, DetailGain);

            if (sounds.cityDayBed != null)
                dayBed.volume = (1f - night) * sounds.cityDayBed.volume * master * zoomShape;
            if (sounds.cityNightBed != null)
                nightBed.volume = night * sounds.cityNightBed.volume * master * zoomShape;

            UpdateWeatherBed(master * zoomShape);
        }

        /// <summary>
        /// Starts (or swaps) the weather layer. The weather rolls once per city and then holds,
        /// but the editor's preview menu can swap it live, and re-checking one enum per frame
        /// is cheaper than being wrong in the preview.
        /// </summary>
        void UpdateWeatherBed(float gain)
        {
            var kind = weather ? weather.Current : WeatherKind.ClearDusk;

            if (!weatherBedAssigned || kind != weatherBedKind)
            {
                weatherBedAssigned = true;
                weatherBedKind = kind;

                var bed = BedFor(kind);
                weatherBed.Stop();
                if (bed != null && bed.clip)
                {
                    weatherBed.clip = bed.clip;
                    weatherBed.Play();
                }
            }

            var current = BedFor(kind);
            weatherBed.volume = current != null ? current.volume * gain : 0f;
        }

        SoundDatabase.Bed BedFor(WeatherKind kind) => kind switch
        {
            WeatherKind.Overcast => sounds.overcastLayer,
            WeatherKind.Smog => sounds.smogLayer,
            _ => sounds.clearDuskLayer,
        };

        // -- one-shots --------------------------------------------------------------------

        /// <summary>
        /// A positional fire-and-forget sound. Returns quietly when the pool is saturated or
        /// the clip is missing - the caller never needs to care.
        /// </summary>
        public static void PlayOneShot(AudioClip clip, Vector3 position,
            float volume = 1f, float pitchJitter = 0.05f)
        {
            if (Instance)
                Instance.PlayOneShotLocal(clip, position, volume, pitchJitter);
        }

        /// <summary>
        /// A gunshot at this muzzle. Deliberately NOT gated by DetailGain - a shot is an event,
        /// and hearing it from a wide view is information, not noise. Zoom clamps the volume
        /// instead of muting.
        /// </summary>
        public static void PlayGunshot(Vector3 muzzle)
        {
            var self = Instance;
            if (!self || !self.sounds)
                return;

            var clip = SoundDatabase.Pick(self.sounds.gunshots, self.rng);
            self.PlayOneShotLocal(clip, muzzle,
                self.sounds.gunshotVolume * Mathf.Lerp(0.55f, 1f, self.DetailGain),
                pitchJitter: 0.04f);
        }

        void PlayOneShotLocal(AudioClip clip, Vector3 position, float volume, float pitchJitter)
        {
            if (!clip || sounds == null)
                return;

            var now = Time.time;
            for (var i = 0; i < oneShotPool.Length; i++)
            {
                if (now < oneShotBusyUntil[i])
                    continue;

                var source = oneShotPool[i];
                source.transform.position = position;
                source.clip = clip;
                source.volume = volume * sounds.masterVolume;
                source.pitch = 1f + ((float)rng.NextDouble() * 2f - 1f) * pitchJitter;
                source.Play();

                // Pitch stretches playback time; book the voice for the worst case.
                oneShotBusyUntil[i] = now + clip.length / Mathf.Max(0.5f, source.pitch);
                return;
            }
            // Pool saturated: the sound is dropped. The budget is the point.
        }

        /// <summary>Random clip from a database array, on the director's own rng stream.</summary>
        public AudioClip PickClip(AudioClip[] clips) => SoundDatabase.Pick(clips, rng);
    }
}

