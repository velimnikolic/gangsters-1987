using System.Collections.Generic;

namespace LivingCity.Outfit
{
    /// <summary>
    /// WHAT A HOUSE WAS REFUSED, AND UNTIL WHEN IT STOPS ASKING (AI-005 P4, ruling A24).
    ///
    /// <see cref="HouseView.LastRefusals"/> was filled every think and read by nobody,
    /// so every impossible intent - bail with no counsel, a promotion past the span
    /// cap, an order to a crew that no longer exists - was re-proposed every think for
    /// the rest of the campaign. This is the memory the view reads before the mind
    /// proposes a thing twice.
    ///
    /// Keyed by <see cref="HouseIntent.Key"/> - the kind of thing AND what it was aimed
    /// at - so a refused bail for one man does not silence a bail for another. Expressed
    /// in GAME HOURS and not in thinks, on purpose: ruling A19 (a faster mind) must not
    /// silently shorten what "not again for a while" means.
    ///
    /// A refusal that can never clear is permanent, not slow: no bail for a cop-killing
    /// is not a thing to retry at noon. The key carries the case, so a re-booking on a
    /// different deed is a different key and is asked afresh.
    ///
    /// Memory only, never saved (review finding C5). Pure and free of UnityEngine; the
    /// runtime keeps one per house and the paper benches keep their own.
    /// </summary>
    public sealed class HouseBackoffs
    {
        readonly Dictionary<string, double> until = new Dictionary<string, double>();
        readonly List<string> stale = new List<string>();

        /// <summary>How many keys are currently held back.</summary>
        public int Count => until.Count;

        /// <summary>The gateway said no. The key is held back for the config's window,
        /// or for ever when the refusal is one nothing can change.</summary>
        public void Note(string key, string refusal, double gameHour, HouseMindConfig config)
        {
            if (string.IsNullOrEmpty(key))
                return;
            config = config ?? HouseMindConfig.Default;
            until[key] = Permanent(refusal)
                ? double.PositiveInfinity
                : gameHour + config.RefusalBackoffHours;
        }

        /// <summary>Whether this key is still held back at this hour.</summary>
        public bool Blocked(string key, double gameHour) =>
            !string.IsNullOrEmpty(key) &&
            until.TryGetValue(key, out var at) && gameHour < at;

        /// <summary>When the hold on a key lifts, or negative when there is none.
        /// The probe prints it.</summary>
        public double UntilOf(string key) =>
            !string.IsNullOrEmpty(key) && until.TryGetValue(key, out var at) ? at : -1.0;

        /// <summary>Every key still held, for the probe.</summary>
        public void Collect(List<(string key, double until)> into)
        {
            into?.Clear();
            if (into == null)
                return;
            foreach (var pair in until)
                into.Add((pair.Key, pair.Value));
        }

        /// <summary>Drops the holds whose hour has passed, so a long campaign does not
        /// carry every refusal it ever heard.</summary>
        public void Sweep(double gameHour)
        {
            stale.Clear();
            foreach (var pair in until)
                if (gameHour >= pair.Value)
                    stale.Add(pair.Key);
            for (var i = 0; i < stale.Count; i++)
                until.Remove(stale[i]);
        }

        /// <summary>Lets a key be asked again - the thing it was about has changed.
        /// </summary>
        public void Forget(string key)
        {
            if (!string.IsNullOrEmpty(key))
                until.Remove(key);
        }

        /// <summary>
        /// A refusal the clock cannot cure. Only the ones the ledger words as absolute
        /// are here: a case with no bail on it at any price. Everything else - no
        /// counsel, no money, the span full - can change by tomorrow and is only slowed.
        /// </summary>
        public static bool Permanent(string refusal) =>
            !string.IsNullOrEmpty(refusal) && refusal == UI.LedgerText.ReasonNoBail;
    }
}
