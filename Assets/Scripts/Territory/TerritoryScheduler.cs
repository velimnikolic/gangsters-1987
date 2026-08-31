using System;

namespace LivingCity.Territory
{
    public enum TerritoryTickChannel
    {
        PhysicalPresence,
        ResidualPresence,
        Fear,
        Business,
        DerivedControl,
    }

    public readonly struct TerritorySimulationTick
    {
        public TerritorySimulationTick(
            TerritoryTickChannel channel,
            long sequence,
            double gameHour,
            double cadenceHours,
            bool forced)
        {
            Channel = channel;
            Sequence = sequence;
            GameHour = gameHour;
            CadenceHours = cadenceHours;
            Forced = forced;
        }

        public TerritoryTickChannel Channel { get; }
        public long Sequence { get; }
        public double GameHour { get; }
        public double CadenceHours { get; }
        public bool Forced { get; }
    }

    /// <summary>
    /// Fixed game-time scheduler for territory only. It catches up every due boundary,
    /// so one large frame and many small frames produce the same tick sequence.
    /// </summary>
    public sealed class TerritorySimulationScheduler
    {
        const double Epsilon = 0.0000001;
        const int ChannelCount = 5;

        readonly double[] cadence = new double[ChannelCount];
        readonly double[] nextDue = new double[ChannelCount];
        readonly long[] sequences = new long[ChannelCount];
        bool initialized;

        public TerritorySimulationScheduler()
        {
            // Short physical sampling, slower memory/fear/business work, and an
            // independent derived-control cadence. These are configuration defaults,
            // not mechanics or balancing constants.
            cadence[(int)TerritoryTickChannel.PhysicalPresence] = 1.0 / 60.0;
            cadence[(int)TerritoryTickChannel.ResidualPresence] = 0.25;
            cadence[(int)TerritoryTickChannel.Fear] = 1.0;
            cadence[(int)TerritoryTickChannel.Business] = 4.0;
            cadence[(int)TerritoryTickChannel.DerivedControl] = 0.25;
        }

        public event Action<TerritorySimulationTick> Ticked;

        public double LastObservedGameHour { get; private set; }

        public double CadenceOf(TerritoryTickChannel channel) => cadence[(int)channel];

        public void SetCadence(TerritoryTickChannel channel, double gameHours)
        {
            if (double.IsNaN(gameHours) || double.IsInfinity(gameHours) || gameHours <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(gameHours),
                    "Territory cadence must be a finite positive number of game hours.");

            var index = (int)channel;
            cadence[index] = gameHours;
            if (initialized)
                nextDue[index] = LastObservedGameHour + gameHours;
        }

        /// <summary>
        /// Observe absolute game time (day * 24 + hour). Re-observing the same value is a
        /// pause and emits nothing. A backwards editor scrub re-anchors without replay.
        /// </summary>
        public int AdvanceTo(double absoluteGameHour)
        {
            if (double.IsNaN(absoluteGameHour) || double.IsInfinity(absoluteGameHour))
                throw new ArgumentOutOfRangeException(nameof(absoluteGameHour));

            if (!initialized)
            {
                ResetTo(absoluteGameHour);
                return 0;
            }

            if (absoluteGameHour + Epsilon < LastObservedGameHour)
            {
                ResetTo(absoluteGameHour);
                return 0;
            }

            var fired = 0;
            while (true)
            {
                var dueChannel = -1;
                var dueAt = double.MaxValue;

                // Earliest boundary first; enum order is the deterministic tie-break.
                for (var i = 0; i < ChannelCount; i++)
                {
                    if (nextDue[i] > absoluteGameHour + Epsilon || nextDue[i] >= dueAt)
                        continue;
                    dueAt = nextDue[i];
                    dueChannel = i;
                }

                if (dueChannel < 0)
                    break;

                var channel = (TerritoryTickChannel)dueChannel;
                var tick = new TerritorySimulationTick(
                    channel, ++sequences[dueChannel], dueAt, cadence[dueChannel], false);
                nextDue[dueChannel] += cadence[dueChannel];
                fired++;
                Ticked?.Invoke(tick);
            }

            LastObservedGameHour = absoluteGameHour;
            return fired;
        }

        public int AdvanceBy(double gameHours)
        {
            if (gameHours < 0.0 || double.IsNaN(gameHours) || double.IsInfinity(gameHours))
                throw new ArgumentOutOfRangeException(nameof(gameHours));
            if (!initialized)
                ResetTo(0.0);
            return AdvanceTo(LastObservedGameHour + gameHours);
        }

        /// <summary>Developer evaluation without changing cadence or authoritative time.</summary>
        public void Force(TerritoryTickChannel channel)
        {
            if (!initialized)
                ResetTo(0.0);
            var index = (int)channel;
            Ticked?.Invoke(new TerritorySimulationTick(
                channel, ++sequences[index], LastObservedGameHour, cadence[index], true));
        }

        public void ResetTo(double absoluteGameHour)
        {
            initialized = true;
            LastObservedGameHour = absoluteGameHour;
            for (var i = 0; i < ChannelCount; i++)
                nextDue[i] = absoluteGameHour + cadence[i];
        }
    }
}
