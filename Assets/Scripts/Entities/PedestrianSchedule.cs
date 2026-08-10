using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>Where in the day's arc a pedestrian is. Not what they are doing - the
    /// activity coroutines own that - but what the hour asks of them.</summary>
    public enum DayPhase
    {
        /// <summary>Indoors overnight. The streets thin out to whoever never went home.</summary>
        AtHomeNight,

        /// <summary>The window for setting off to work. A window rather than an instant,
        /// served once per day by an agent-side latch - the clock can outrun any poll.</summary>
        MorningCommute,

        /// <summary>At work. A text flavour, not a cage: the walker keeps living the
        /// street - errands, benches, chats - which is what keeps daytime pavements full.</summary>
        WorkDay,

        /// <summary>The window for heading home after work.</summary>
        EveningCommute,

        /// <summary>Home behind them, night not yet arrived: the evening stroll.</summary>
        EveningOut,
    }

    /// <summary>
    /// The day's arc as a pure function of the clock: hour in, phase out. No
    /// UnityEngine.Object and no state, so the headless suite can prove the two properties
    /// everything downstream leans on - every hour maps to exactly one phase, and a
    /// non-worker never gets a commute.
    ///
    /// The per-agent offset is the first of the three stagger layers (the others: the
    /// director's sliced rolls, and no pathfinding at the boundary): everyone's personal
    /// clock runs a hashed fraction of the commute band early or late, so a threshold
    /// crossing is a tide over minutes, never a stampede on one frame.
    /// </summary>
    public static class PedestrianSchedule
    {
        /// <summary>Width of each commute window in personal hours. Generous on purpose:
        /// an agent mid-activity when their window opens catches it when they finish.</summary>
        public const float CommuteWindowHours = 1.5f;

        /// <summary>This agent's personal clock shift, +/- half the configured band,
        /// hashed off the identity seed - deterministic, and never the live rng.</summary>
        public static float OffsetHours(int seed, float staggerHours)
        {
            var z = (uint)seed * 0x9E3779B9u;
            z ^= z >> 16; z *= 0x85EBCA6Bu;
            z ^= z >> 13;
            var unit = (z & 0xFFFFFFu) / (float)0xFFFFFF;
            return (unit - 0.5f) * Mathf.Max(0f, staggerHours);
        }

        /// <summary>
        /// The phase at <paramref name="hour"/> for an agent shifted by
        /// <paramref name="offsetHours"/>. Workers walk the full arc; everyone else
        /// (children, drifters) only splits night from day - they have nowhere to be.
        /// </summary>
        public static DayPhase PhaseFor(float hour, float offsetHours, bool worksDays,
                                        float workStart, float workEnd, float nightHome)
        {
            var p = Mathf.Repeat(hour - offsetHours, 24f);

            if (!worksDays)
                return p >= nightHome || p < workStart ? DayPhase.AtHomeNight : DayPhase.EveningOut;

            if (p >= nightHome || p < workStart)
                return DayPhase.AtHomeNight;
            if (p < workStart + CommuteWindowHours)
                return DayPhase.MorningCommute;
            if (p < workEnd)
                return DayPhase.WorkDay;
            if (p < workEnd + CommuteWindowHours)
                return DayPhase.EveningCommute;
            return DayPhase.EveningOut;
        }
    }
}
