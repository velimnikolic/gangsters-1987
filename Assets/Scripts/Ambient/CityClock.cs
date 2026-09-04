using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

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

        [Tooltip("Fallback for a scene with no config. 600 makes one game minute last 10 real seconds.")]
        [SerializeField, Min(0.02f)] float realSecondsPerGameHour = 600f;

        [SerializeField, Range(0f, 24f)] float startHour = 6f;

        [Tooltip("Untick to freeze the clock where it is - useful for judging one particular hour.")]
        [SerializeField] bool running = true;

        /// <summary>
        /// The game-speed ladder every clock face in the game prints, in the order it
        /// prints it: 1x first, then a doubling per rung. The number keys pick the rungs
        /// by that order - 1 is 1x, 2 is 2x, 3 is 4x, 4 is 8x - so the key and the button
        /// under it are always the same answer. No half speed: the ladder starts at the
        /// speed the game is played at.
        /// </summary>
        static readonly float[] Speeds = { 1f, 2f, 4f, 8f, 16f };

        /// <summary>The number-key row, rung for rung with <see cref="Speeds"/>.</summary>
        static readonly Key[] SpeedKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
        };

        static readonly Key[] SpeedKeypadKeys =
        {
            Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5,
        };

        int speedIndex;
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

        /// <summary>Which rung of the speed ladder is selected.</summary>
        public int SpeedIndex => speedIndex;

        public int SpeedCount => Speeds.Length;

        public float SpeedAt(int index) => Speeds[Mathf.Clamp(index, 0, Speeds.Length - 1)];

        public void SetSpeed(int index)
        {
            speedIndex = Mathf.Clamp(index, 0, Speeds.Length - 1);
            ApplySpeed();
        }

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

        /// <summary>
        /// Configures a clock created by a runtime-built scene. AddComponent invokes
        /// Awake before the builder can assign values, so this also moves the live hour
        /// immediately. An explicit runtime configuration takes precedence over any
        /// CityBuilder that happened to be present in the scene.
        /// </summary>
        public void Configure(float initialHour, float realSecondsPerHour)
        {
            config = null;
            startHour = Mathf.Repeat(initialHour, HoursPerDay);
            this.realSecondsPerGameHour = Mathf.Max(0.02f, realSecondsPerHour);
            SetHour(startHour);
        }

        void Update()
        {
            ReadTimeKeys();

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

        /// <summary>
        /// THE TIME KEYS, and they live on the clock rather than on any one HUD: the
        /// street, the turf map and the book all show the same rung, so they all have to
        /// read the same keys. Space holds the city and lets it go again; 1-5 pick a rung
        /// off the ladder and let a held city go at that rung.
        /// </summary>
        void ReadTimeKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || Typing)
                return;

            // Space with the left button down is the camera's drag-pan on the scenes that
            // have one (IsometricCameraController). A pan must not also stop the city.
            var mouse = Mouse.current;
            if (kb.spaceKey.wasPressedThisFrame &&
                (mouse == null || !mouse.leftButton.isPressed))
                Paused = !paused;

            for (var i = 0; i < Speeds.Length; i++)
            {
                if (!kb[SpeedKeys[i]].wasPressedThisFrame &&
                    !kb[SpeedKeypadKeys[i]].wasPressedThisFrame)
                    continue;
                // Naming a speed is also the way off a hold: the player who wants the city
                // moving again at 4x should not have to unpause it first.
                paused = false;
                SetSpeed(i);
                break;
            }
        }

        /// <summary>The book has one typed line in it (the blueprint's room name). While
        /// the caret is in it a 4 is a character, not a speed.</summary>
        static bool Typing
        {
            get
            {
                var focused = EventSystem.current
                    ? EventSystem.current.currentSelectedGameObject
                    : null;
                return focused &&
                       focused.TryGetComponent(out TMPro.TMP_InputField field) &&
                       field.isFocused;
            }
        }

        /// <summary>Jumps the clock. Used by the editor scrubber and by anything that skips ahead.</summary>
        public void SetHour(float hour) => Hour = Mathf.Repeat(hour, HoursPerDay);

        /// <summary>The inverse of OutfitDirector's clock.Day + 1 calendar bridge.
        /// Saves speak in one-based campaign days; this clock stores whole elapsed days.</summary>
        public static int ElapsedDayOfCampaignDay(int campaignDay) =>
            campaignDay > 1 ? campaignDay - 1 : 0;

        /// <summary>The absolute hour used by deadline ledgers when reading a saved
        /// one-based campaign date.</summary>
        public static double GameHourOfCampaignTime(int campaignDay, float hour) =>
            ElapsedDayOfCampaignDay(campaignDay) * (double)HoursPerDay +
            Mathf.Repeat(hour, HoursPerDay);

        /// <summary>
        /// THE LOAD BOUNDARY (RIVAL-010). The only way the day itself is ever set: a
        /// campaign displayed as day 30 at half past nine restores the clock to 29 whole
        /// elapsed days and 09:30, and nothing else in the game may wind the calendar.
        /// </summary>
        public void Restore(int campaignDay, float hour)
        {
            Day = ElapsedDayOfCampaignDay(campaignDay);
            Hour = Mathf.Repeat(hour, HoursPerDay);
        }

        void OnDestroy()
        {
            DayClock.Unregister(this);

            // In the Editor, Time.timeScale SURVIVES leaving Play mode - a city paused at
            // stop would leave the next Play session frozen with no button on screen yet.
            Time.timeScale = 1f;
        }
    }
}
