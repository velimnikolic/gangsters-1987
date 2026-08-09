using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The scene's handle on the gameplay configs, for the components that cannot afford a
    /// serialized reference each: NpcHealth sits on every spawned pedestrian, and wiring an
    /// asset into thousands of runtime AddComponent calls is exactly what a static accessor
    /// exists to avoid. One per scene, on the Gameplay object the setup tool creates.
    ///
    /// Static access is null-safe by contract: every reader must survive a missing config
    /// (the scene predates the tool, or someone deleted the asset) by falling back to the
    /// class defaults. That keeps the failure a wrong number rather than an exception storm.
    /// </summary>
    public sealed class GameplayRuntime : MonoBehaviour
    {
        [SerializeField] CombatConfig combat;
        [SerializeField] PlayerConfig player;
        [SerializeField] WantedConfig wanted;
        [SerializeField] PoliceConfig police;

        static GameplayRuntime instance;

        /// <summary>Null when no GameplayRuntime is in the scene - callers fall back.</summary>
        public static CombatConfig Combat => instance ? instance.combat : null;

        /// <summary>Null when no GameplayRuntime is in the scene - callers fall back.</summary>
        public static PlayerConfig Player => instance ? instance.player : null;

        /// <summary>Null when no GameplayRuntime is in the scene - callers fall back.</summary>
        public static WantedConfig Wanted => instance ? instance.wanted : null;

        /// <summary>Null when no GameplayRuntime is in the scene - callers fall back.</summary>
        public static PoliceConfig Police => instance ? instance.police : null;

        void Awake()
        {
            if (instance && instance != this)
            {
                Debug.LogWarning("[GameplayRuntime] Duplicate in the scene - keeping the first.", this);
                return;
            }

            instance = this;

            // Self-heal empty slots from Resources - the configs live in a Resources folder
            // precisely so a runtime-bootstrapped session (scene never re-wired, never even
            // saved) still gets the tuned numbers instead of the class defaults.
            if (!combat)
                combat = Resources.Load<CombatConfig>("CombatConfig");
            if (!player)
                player = Resources.Load<PlayerConfig>("PlayerConfig");
            if (!wanted)
                wanted = Resources.Load<WantedConfig>("WantedConfig");
            if (!police)
                police = Resources.Load<PoliceConfig>("PoliceConfig");
        }

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        /// <summary>
        /// Static state outlives Play when domain reload is off - same hole, same fix as
        /// OverlayRegistry.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => instance = null;
    }
}
