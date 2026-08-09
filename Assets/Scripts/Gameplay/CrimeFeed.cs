using UnityEngine;

namespace LivingCity.Gameplay
{
    public enum CrimeKind
    {
        /// <summary>A shot was fired - heard further than it is seen.</summary>
        Gunshot,

        /// <summary>Somebody went down. Carries the perpetrator for witnesses who SAW it.</summary>
        Murder,

        /// <summary>A corpse found after the fact. No perpetrator attached - the trail is
        /// the body, not the man.</summary>
        BodyDiscovered,

        /// <summary>A police officer went down. The force hears about its own instantly.</summary>
        AssaultingOfficer,
    }

    /// <summary>One reported crime. A struct because reports are fire-and-forget data.</summary>
    public readonly struct CrimeEvent
    {
        public readonly CrimeKind Kind;
        public readonly Vector3 Position;
        public readonly float Time;
        public readonly Transform Perpetrator;
        public readonly Transform Victim;

        public CrimeEvent(CrimeKind kind, Vector3 position, Transform perpetrator, Transform victim)
        {
            Kind = kind;
            Position = position;
            Time = UnityEngine.Time.time;
            Perpetrator = perpetrator;
            Victim = victim;
        }
    }

    /// <summary>
    /// Where crimes are announced and - in Phase 2 - where witnesses and the wanted level
    /// will hear about them. WeaponController reports every shot and every kill through here
    /// already, so the witness system plugs in by subscribing, without touching the combat
    /// path. Zero subscribers today is the intended state, not an oversight.
    /// </summary>
    public static class CrimeFeed
    {
        /// <summary>Phase-2 subscribers: witness scan, wanted heat, police dispatch.</summary>
        public static event System.Action<CrimeEvent> Reported;

        public static void Report(in CrimeEvent crime) => Reported?.Invoke(crime);

        /// <summary>
        /// Static state outlives Play when domain reload is off - a stale subscriber from the
        /// previous session would receive next session's crimes. Same fix as OverlayRegistry.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Reported = null;
    }
}
