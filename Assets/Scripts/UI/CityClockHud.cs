using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LivingCity.UI
{
    /// <summary>
    /// The clock readout across the top of the screen: a day/night disc, the hour as HH:MM, and
    /// which day it is.
    ///
    /// Reads CityClock and nothing else - it owns no notion of time, and deliberately does not
    /// own a notion of "night" either. The disc is tinted by CityWeather.Nightness, the same
    /// static the street lamps and the lit windows ramp on, so the icon cools at exactly the
    /// moment the city does. A second threshold here would drift away from the lighting the
    /// first time either was tuned.
    ///
    /// The canvas this drives is built by Tools/City/Add Clock HUD rather than authored by hand;
    /// see CityHudSetup.
    /// </summary>
    public sealed class CityClockHud : MonoBehaviour
    {
        [Tooltip("Left empty, this is found in the scene on Awake.")]
        [SerializeField] Ambient.CityClock clock;

        [SerializeField] TMP_Text timeLabel;
        [SerializeField] TMP_Text dayLabel;

        [Tooltip("The disc left of the time. Tinted between the two colours below by how dark it is.")]
        [SerializeField] Image icon;

        [SerializeField] Color dayTint = new(1f, 0.85f, 0.45f);
        [SerializeField] Color nightTint = new(0.72f, 0.80f, 0.95f);

        // What is currently on screen. Whole game minutes, not the raw hour: the label only shows
        // minutes, so redrawing on every fractional change would be a hundred wasted mesh
        // rebuilds a second for a number that has not moved.
        int shownMinute = -1;
        int shownDay = -1;

        void Awake()
        {
            // Same self-heal as StreetLampLights and NightWindows (StreetLampLights.cs:118).
            // The setup menu wires this, but a component added to a scene by hand deserialises
            // with the field empty and would otherwise sit there showing nothing.
            if (!clock)
                clock = FindAnyObjectByType<Ambient.CityClock>();

            if (clock)
                return;

            // Once, then stop. A missing clock is a setup mistake, not a per-frame event, and a
            // warning in LateUpdate would bury every other message in the Console.
            Debug.LogWarning("[CityClockHud] No CityClock in the scene - the readout is off. " +
                             "Run Tools/City/Add Clock HUD.", this);
            enabled = false;
        }

        // LateUpdate to match the rest of the ambient stack: CityClock advances in Update, so
        // reading it late means the HUD and the sky are showing the same frame's hour.
        void LateUpdate()
        {
            var hour = clock.Hour;
            var minute = Mathf.FloorToInt(hour * 60f);

            if (minute == shownMinute)
                return;

            shownMinute = minute;

            // TMP's SetText writes the formatted value straight into its own char buffer -
            // "{0:00}" means zero-padded to two digits. Assigning .text with an interpolated
            // string instead would allocate a string every game minute, which at a low
            // realSecondsPerGameHour is every frame.
            if (timeLabel)
                timeLabel.SetText("{0:00}:{1:00}", minute / 60, minute % 60);

            if (icon)
                icon.color = Color.Lerp(dayTint, nightTint, Ambient.CityWeather.Nightness(hour));

            // Day 0 is the first day; nobody counts from zero on a clock face.
            if (dayLabel && clock.Day != shownDay)
            {
                shownDay = clock.Day;
                dayLabel.SetText("Dan {0}", shownDay + 1);
            }
        }
    }
}
