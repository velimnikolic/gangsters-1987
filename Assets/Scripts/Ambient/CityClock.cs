using UnityEngine;

namespace LivingCity.Ambient
{
    /// <summary>
    /// The city's clock. Holds an hour of the day and advances it, and nothing else - what the
    /// sky does with the number is CityWeather's problem.
    ///
    /// Split out rather than folded into CityWeather because time is going to be read by things
    /// that have no interest in lighting: shops that shut, patrols that change over, whatever
    /// wants to behave differently at two in the morning. One authority on what time it is.
    /// </summary>
    public sealed class CityClock : MonoBehaviour, IDayClock
    {
        public const float HoursPerDay = 24f;

        [Tooltip("Where the speed comes from. Assigned, the two fields below are ignored and the " +
                 "config wins - one place to change it, alongside every other city setting.")]
        [SerializeField] Data.CityConfig config;

        [Tooltip("Fallback for a scene with no config. Real seconds per game hour.")]
        [SerializeField, Min(0.02f)] float realSecondsPerGameHour = 60f;

        [SerializeField, Range(0f, 24f)] float startHour = 8f;

        [Tooltip("Untick to freeze the clock where it is - useful for judging one particular hour.")]
        [SerializeField] bool running = true;

        /// <summary>The game-speed ladder the HUD's buttons step through. Index 1 is normal.</summary>
        static readonly float[] Speeds = { 0.5f, 1f, 2f, 4f };

        int speedIndex = 1;
        bool paused;

        /// <summary>Hour of the day in [0, 24). Fractional: 8.5 is half past eight.</summary>
        public float Hour { get; private set; }

        /// <summary>Whole days elapsed since the clock started - the campaign calendar
        /// walks on this number (OutfitDirector), so a day here is a day in the books.</summary>
        public int Day { get; private set; }

        public bool Running
        {
            get => running;
            set => running = value;
        }

        /// <summary>
        /// The pause button's state. Drives Time.timeScale, so the WHOLE city freezes with
        /// the clock - traffic, pedestrians, smoke. The strategic map and both camera rigs
        /// pan on unscaled time on purpose, so the player can still look around a paused
        /// city.
        /// </summary>
        public bool Paused
        {
            get => paused;
            set
            {
                paused = value;
                ApplySpeed();
            }
        }

        /// <summary>Current step on the speed ladder - what the HUD prints as "2x".</summary>
        public float SpeedMultiplier => Speeds[speedIndex];

        public void SpeedUp()
        {
            if (speedIndex < Speeds.Length - 1)
                speedIndex++;
            ApplySpeed();
        }

        public void SlowDown()
        {
            if (speedIndex > 0)
                speedIndex--;
            ApplySpeed();
        }

        void ApplySpeed() => Time.timeScale = paused ? 0f : Speeds[speedIndex];

        /// <summary>Hour as "HH:MM", for logs and any on-screen clock.</summary>
        public string Display => $"{Mathf.FloorToInt(Hour):00}:{Mathf.FloorToInt(Hour % 1f * 60f):00}";

        /// <summary>Seconds per game hour, config first. Floored so a zero cannot divide by it.</summary>
        public float SecondsPerHour =>
            Mathf.Max(0.02f, config ? config.realSecondsPerGameHour : realSecondsPerGameHour);

        void Awake()
        {
            // Self-heal the wiring. A component added to the scene before this field existed
            // deserialises with it empty, and the failure that produces is silent and awful:
            // the config says 23:00, the clock quietly starts at its own default of 8, and the
            // scene shows a night that Play replaces with a morning. Borrow the builder's config
            // rather than sit there being wrong.
            if (!config)
            {
                var builder = FindAnyObjectByType<Generation.CityBuilder>();
                if (builder)
                    config = builder.Config;
            }

            Hour = Mathf.Repeat(config ? config.startHour : startHour, HoursPerDay);

            // The campaign calendar walks on whichever clock a scene runs.
            DayClock.Register(this);
        }

        void Update()
        {
            if (!running)
                return;

            // deltaTime, not unscaled: the HUD's speed buttons drive Time.timeScale, and the
            // whole point of them is that pause and fast-forward move the city AND its clock
            // together - a paused city whose hour kept walking would desync every routine.
            var advanced = Hour + Time.deltaTime / SecondsPerHour;

            if (advanced >= HoursPerDay)
                Day += Mathf.FloorToInt(advanced / HoursPerDay);

            Hour = Mathf.Repeat(advanced, HoursPerDay);
        }

        /// <summary>Jumps the clock. Used by the editor scrubber and by anything that skips ahead.</summary>
        public void SetHour(float hour) => Hour = Mathf.Repeat(hour, HoursPerDay);

        void OnDestroy()
        {
            DayClock.Unregister(this);

            // In the Editor, Time.timeScale SURVIVES leaving Play mode - a city paused at
            // stop would leave the next Play session frozen with no button on screen yet.
            Time.timeScale = 1f;
        }
    }
}
