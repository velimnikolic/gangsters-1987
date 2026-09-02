namespace LivingCity.Ambient
{
    /// <summary>
    /// What the strategy layer needs from whichever clock a scene happens to run: the
    /// day and the hour, and nothing else. The registry keeps callers from hunting the
    /// scene for the one CityClock that owns those values.
    ///
    /// Deliberately read-only: the campaign follows the clock, never sets it. A layer
    /// that could push the hour around would let a strategy bug desync every routine
    /// in the city.
    /// </summary>
    public interface IDayClock
    {
        /// <summary>Whole days elapsed since the clock started.</summary>
        int Day { get; }

        /// <summary>Hour of the day in [0, 24). Fractional: 8.5 is half past eight.</summary>
        float Hour { get; }
    }

    /// <summary>
    /// Whichever clock this scene is running. The clocks put themselves here at Awake
    /// rather than being hunted for: a scan for a component implementing an interface
    /// means walking every MonoBehaviour in a city of tens of thousands of objects, and
    /// the search would have to be repeated every time it came back empty - which, in a
    /// scene built at runtime, is every frame until the builder gets to the clock.
    ///
    /// Same registry convention as OverlayRegistry and PropertyRegistry, including the
    /// reset: static state outlives Play when domain reload is off, and a stale clock
    /// from the last session would hand the campaign a day that never ticks.
    /// </summary>
    public static class DayClock
    {
        public static IDayClock Current { get; private set; }

        public static void Register(IDayClock clock)
        {
            if (clock != null)
                Current = clock;
        }

        /// <summary>Only the clock that is actually standing there clears the post - a
        /// second clock destroyed after the first registered must not blank it.</summary>
        public static void Unregister(IDayClock clock)
        {
            if (ReferenceEquals(Current, clock))
                Current = null;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Current = null;
    }
}
