using UnityEngine;
using LivingCity.Outfit;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The scene's one owner of the outfit's strategic state - today the campaign
    /// calendar, and as the ledger's pages land, the safe, the weekly books, stances and
    /// the order queues. Same contract as PersonnelDirector: the UI reads through this
    /// class and mutates through its wrappers, which bump <see cref="Version"/> - the
    /// dirty key the ledger repaints on. A mutation that skipped the director would
    /// change the books without moving Version and the page would sit stale.
    /// </summary>
    public sealed class OutfitDirector : MonoBehaviour
    {
        public static OutfitDirector Instance { get; private set; }

        public Campaign Campaign { get; private set; } = new Campaign();

        public int Version { get; private set; }

        void Awake()
        {
            if (Instance && Instance != this)
                return;
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // Static state outlives Play when domain reload is off - same fix as OverlayRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Instance = null;
    }
}
