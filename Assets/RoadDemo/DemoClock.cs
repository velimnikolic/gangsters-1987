using UnityEngine;

namespace RoadDemo
{
    // The demo's own clock - an hour of the day that advances, a speed ladder and
    // a pause. Deliberately not the city's CityClock: the demo is self-contained
    // and owns its whole day/night stack (see DemoSky).
    //
    // Pause and speed go through Time.timeScale so the WHOLE demo freezes and
    // accelerates with the clock - traffic, pedestrians, signals. The camera pans
    // on unscaled time (DemoCamera), so a paused demo can still be looked around.
    public class DemoClock : MonoBehaviour, LivingCity.Ambient.IDayClock
    {
        public const float HoursPerDay = 24f;

        [Min(0.02f)] public float secondsPerGameHour = 15f;
        [Range(0f, 24f)] public float startHour = 16f;

        static readonly float[] Speeds = { 0.5f, 1f, 2f, 4f };

        int _speedIndex = 1;
        bool _paused;
        bool _started;

        /// <summary>Hour of the day in [0, 24). Fractional: 8.5 is half past eight.</summary>
        public float Hour { get; private set; }

        public int Day { get; private set; }

        public bool Paused
        {
            get => _paused;
            set
            {
                _paused = value;
                ApplySpeed();
            }
        }

        public float SpeedMultiplier => Speeds[_speedIndex];

        public string Display
        {
            get
            {
                int minute = Mathf.FloorToInt(Hour * 60f);
                return $"{minute / 60:00}:{minute % 60:00}";
            }
        }

        public void SpeedUp()
        {
            if (_speedIndex < Speeds.Length - 1)
                _speedIndex++;
            ApplySpeed();
        }

        public void SlowDown()
        {
            if (_speedIndex > 0)
                _speedIndex--;
            ApplySpeed();
        }

        void ApplySpeed() => Time.timeScale = _paused ? 0f : Speeds[_speedIndex];

        // Awake AND Start: Awake so anything reading the clock on the build frame
        // sees an hour instead of midnight, Start because the builder assigns
        // startHour right after AddComponent - which is after Awake already ran.
        void Awake()
        {
            Hour = Mathf.Repeat(startHour, HoursPerDay);
            // The campaign calendar walks on whichever clock a scene runs, and this is
            // the one the shipping scene builds.
            LivingCity.Ambient.DayClock.Register(this);
        }

        void Start()
        {
            Hour = Mathf.Repeat(startHour, HoursPerDay);
            _started = true;
        }

        void Update()
        {
            if (!_started)
                return;

            // deltaTime, not unscaled: pause and fast-forward must move the demo
            // and its clock together.
            float advanced = Hour + Time.deltaTime / Mathf.Max(0.02f, secondsPerGameHour);

            if (advanced >= HoursPerDay)
                Day += Mathf.FloorToInt(advanced / HoursPerDay);

            Hour = Mathf.Repeat(advanced, HoursPerDay);
        }

        void OnDestroy()
        {
            LivingCity.Ambient.DayClock.Unregister(this);

            // In the Editor Time.timeScale survives leaving Play mode - a demo
            // stopped while paused would leave the next session frozen.
            Time.timeScale = 1f;
        }
    }
}
