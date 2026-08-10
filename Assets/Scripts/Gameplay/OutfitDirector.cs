using UnityEngine;
using LivingCity.Outfit;
using LivingCity.Personnel;

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

        public Accounts Accounts { get; private set; } = new Accounts();

        public int Version { get; private set; }

        void Start()
        {
            if (Accounts.Sheets.Count == 0)
                Accounts.Sheets.Add(new WeekSheet { Week = Campaign.Week });
            Version++;
        }

        /// <summary>
        /// The one purchase gate: refuses with the shortfall spelled out, or moves the
        /// money and books it on the open week's Purchases line - so the Armory click
        /// and the Finances row can never disagree.
        /// </summary>
        public OpResult Purchase(int price, string what)
        {
            var refusal = BalanceMath.TryPurchase(Accounts, price);
            if (refusal != null)
                return OpResult.Fail(refusal);

            Version++;
            Debug.Log("[Outfit] Bought " + what + " for " + UI.LedgerText.Cash(price) +
                      "; safe at " + UI.LedgerText.Cash(Accounts.Safe) + ".");
            return OpResult.Success;
        }

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
