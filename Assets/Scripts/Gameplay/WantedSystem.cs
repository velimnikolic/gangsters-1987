using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The city's memory of the player's sins: a heat number, the star level derived from
    /// it, and the last place anybody trustworthy saw him.
    ///
    /// Heat only ever enters through <see cref="ReportCrime"/> - a delivered witness call,
    /// a police eyewitness, a body found - never straight from the crime itself. A murder
    /// with no witness and no discovered body is worth exactly nothing, which is the design:
    /// the alley kill stays free until someone trips over it.
    ///
    /// Decay is the other half. No contact (no officer seeing him, no witness mid-report)
    /// for decayDelaySeconds and heat drains; an officer's eyes on him pause the drain and
    /// add a slow rise. Contact is reported BY the officers (NotifySeen) rather than probed
    /// from here - they run line-of-sight checks for their own chase logic anyway, and one
    /// consumer of those results is cheaper than a second set of raycasts.
    /// </summary>
    public sealed class WantedSystem : MonoBehaviour
    {
        public static WantedSystem Instance { get; private set; }

        public float Heat { get; private set; }
        public int WantedLevel { get; private set; }

        /// <summary>Where the police believe the player is. Only meaningful while wanted.</summary>
        public Vector3 LastKnownPosition { get; private set; }
        public float LastKnownAt { get; private set; } = float.NegativeInfinity;

        /// <summary>An officer can see the player right now (within the last LOS tick).</summary>
        public bool HasContact => Time.time - lastContactAt < 0.6f;

        /// <summary>Heat is actively draining - the HUD fades the stars on it.</summary>
        public bool IsDecaying =>
            Heat > 0f && Time.time - lastContactAt > DecayDelay;

        /// <summary>For the debug panel: witnesses attached to the most recent crime.</summary>
        public int LastCrimeWitnesses { get; set; }

        public event System.Action<int> WantedLevelChanged;

        /// <summary>A report landed - the dispatcher's cue, carrying the star level.</summary>
        public event System.Action<Vector3> CrimeReported;

        float lastContactAt = float.NegativeInfinity;

        /// <summary>The star ladder when no config carries one.</summary>
        static readonly float[] DefaultStarThresholds = { 10f, 25f, 45f, 70f, 100f };

        WantedConfig Config => GameplayRuntime.Wanted;

        float DecayDelay { get { var c = Config; return c ? c.decayDelaySeconds : 20f; } }

        void Awake()
        {
            if (Instance && Instance != this)
                { enabled = false; return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Instance = null;

        /// <summary>
        /// A report that actually landed. witnessCount scales the heat (a street full of
        /// screaming witnesses is worse than one); knowsPerpetrator decides whether the
        /// police learn WHERE HE IS or only where it happened.
        /// </summary>
        public void ReportCrime(
            CrimeKind kind, Vector3 crimePosition, int witnessCount,
            bool knowsPerpetrator, Vector3 perpetratorPosition)
        {
            var config = Config;
            var baseHeat = kind switch
            {
                CrimeKind.Murder => config ? config.murderHeat : 30f,
                CrimeKind.Gunshot => config ? config.gunshotHeat : 4f,
                CrimeKind.BodyDiscovered => config ? config.bodyDiscoveredHeat : 12f,
                CrimeKind.AssaultingOfficer => config ? config.assaultOfficerHeat : 35f,
                _ => 0f,
            };
            var multiplier = config ? config.witnessMultiplier : 0.25f;

            Heat += baseHeat * (1f + Mathf.Max(0, witnessCount - 1) * multiplier);

            LastKnownPosition = knowsPerpetrator ? perpetratorPosition : crimePosition;
            LastKnownAt = Time.time;

            // A landing report IS contact - somebody just told the police about him, so
            // the trail is warm even if no officer has eyes on yet.
            lastContactAt = Time.time;

            RecalculateLevel();
            CrimeReported?.Invoke(LastKnownPosition);
        }

        /// <summary>An officer has eyes on the player - called on their LOS tick.</summary>
        public void NotifySeen(Vector3 playerPosition, float sinceLastTick)
        {
            lastContactAt = Time.time;
            LastKnownPosition = playerPosition;
            LastKnownAt = Time.time;

            if (WantedLevel > 0)
            {
                var config = Config;
                Heat += (config ? config.contactHeatPerSecond : 0.6f) *
                        Mathf.Clamp(sinceLastTick, 0f, 1f);
                RecalculateLevel();
            }
        }

        /// <summary>Arrest, death, respawn - the slate wiped.</summary>
        public void ClearWanted()
        {
            Heat = 0f;
            lastContactAt = float.NegativeInfinity;
            LastKnownAt = float.NegativeInfinity;
            RecalculateLevel();
        }

        void Update()
        {
            if (Heat <= 0f)
                return;

            if (Time.time - lastContactAt <= DecayDelay)
                return;

            var config = Config;
            Heat = Mathf.Max(0f, Heat - (config ? config.decayPerSecond : 1.5f) * Time.deltaTime);
            RecalculateLevel();
        }

        void RecalculateLevel()
        {
            var config = Config;
            var thresholds = config && config.starThresholds is { Length: > 0 }
                ? config.starThresholds
                : DefaultStarThresholds;

            var level = 0;
            foreach (var threshold in thresholds)
                if (Heat >= threshold)
                    level++;

            if (level == WantedLevel)
                return;

            WantedLevel = level;
            WantedLevelChanged?.Invoke(level);
        }

#if UNITY_EDITOR
        /// <summary>The debug panel's scene-view half: where the police think he is.</summary>
        void OnDrawGizmosSelected()
        {
            if (WantedLevel <= 0)
                return;

            Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.8f);
            Gizmos.DrawWireSphere(LastKnownPosition, 2f);
            Gizmos.DrawLine(LastKnownPosition, LastKnownPosition + Vector3.up * 6f);
        }
#endif
    }
}
