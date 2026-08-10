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

        public GangRelations Relations { get; } = new GangRelations();

        public int Version { get; private set; }

        /// <summary>
        /// The live holdings, one entry per gang-held BUILDING, read straight off the
        /// markers - BusinessMarker.GangId is the single source of ownership, and day
        /// one GangDirector stamps exactly one front premise per family. Derived on
        /// every call: nothing seeds and no cache can go stale when the takeover layer
        /// starts flipping GangId building by building.
        /// </summary>
        public void CollectHoldings(System.Collections.Generic.List<Turf.Holding> into)
        {
            into.Clear();
            foreach (var business in PropertyRegistry.Businesses)
                if (business && business.GangId >= 0)
                    into.Add(new Turf.Holding(business.GangId, business.BlockId));
        }

        public WeekPlan Plan { get; private set; } = new WeekPlan();

        /// <summary>Last week's record rows - snapshots, read-only by construction.</summary>
        public readonly System.Collections.Generic.List<OrderRecord> LastWeek =
            new System.Collections.Generic.List<OrderRecord>();

        /// <summary>The outfit's front - its headquarters. False until the gang layer
        /// has seated the families.</summary>
        public bool TryGetHeadquarters(out Vector3 position, out int blockId)
        {
            position = Vector3.zero;
            blockId = -1;
            var front = Gangs.GangRegistry.FrontBusinessOf(Gangs.GangCatalog.PlayerGangId);
            if (!front)
                return false;
            position = front.transform.position;
            blockId = front.BlockId;
            return true;
        }

        /// <summary>Step 5 of the flow - only here does the order exist: it enters the
        /// queue and the budget fills. Everything before this call is a draft on the
        /// job card with no effect on anything.</summary>
        public OpResult ConfirmOrder(PlannedOrder order)
        {
            if (order == null || order.TargetCount == 0)
                return OpResult.Fail(UI.LedgerText.ReasonNoTargets);
            if (order.Men < 1)
                order.Men = 1;

            order.Id = Plan.NextOrderId();
            Plan.Confirmed.Add(order);
            Version++;
            return OpResult.Success;
        }

        /// <summary>Removing returns the order's cost - the men come free again.</summary>
        public OpResult RemoveOrder(int orderId)
        {
            for (var i = 0; i < Plan.Confirmed.Count; i++)
                if (Plan.Confirmed[i].Id == orderId)
                {
                    Plan.Confirmed.RemoveAt(i);
                    Version++;
                    return OpResult.Success;
                }
            return OpResult.Fail(UI.LedgerText.ReasonNoSuchOrder);
        }

        /// <summary>List order IS execution priority - moving a row moves the line
        /// the never-reached mark falls on.</summary>
        public OpResult MoveOrder(int orderId, int direction)
        {
            for (var i = 0; i < Plan.Confirmed.Count; i++)
                if (Plan.Confirmed[i].Id == orderId)
                {
                    var to = i + (direction < 0 ? -1 : 1);
                    if (to < 0 || to >= Plan.Confirmed.Count)
                        return OpResult.Fail(UI.LedgerText.ReasonNoSuchOrder);
                    (Plan.Confirmed[i], Plan.Confirmed[to]) =
                        (Plan.Confirmed[to], Plan.Confirmed[i]);
                    Version++;
                    return OpResult.Success;
                }
            return OpResult.Fail(UI.LedgerText.ReasonNoSuchOrder);
        }

        /// <summary>
        /// The commit boundary - where the working-week simulation will attach. Today
        /// it does everything EXCEPT execute: freezes the open sheet (wages actually
        /// paid, out of the safe), stamps last week's record (reached orders stubbed
        /// Completed; orders past a crew's labour line marked NeverReached - running
        /// out of week is a different problem from failing, and the rows say which),
        /// applies pending stances, turns the calendar, opens a fresh sheet and an
        /// empty plan.
        /// </summary>
        public void CommitWeek()
        {
            var roster = PersonnelDirector.Instance ? PersonnelDirector.Instance.Roster : null;

            var sheet = Accounts.Current;
            if (sheet != null && !sheet.Closed)
            {
                sheet.WagesPaid = Wages.WeeklyPayroll(roster);
                Accounts.Safe -= sheet.WagesPaid;
                Accounts.RiskyMoney += sheet.IllegalIncome;
                sheet.Closed = true;
            }

            LastWeek.Clear();
            var past = new System.Collections.Generic.List<int>();
            var pastAll = new System.Collections.Generic.HashSet<int>();
            if (roster != null)
                foreach (var crew in roster.Crews)
                {
                    OrderMath.PastTheLine(Plan, crew.Id, CrewKit.MenOf(crew), past);
                    foreach (var id in past)
                        pastAll.Add(id);
                }

            foreach (var order in Plan.Confirmed)
            {
                var crew = roster?.FindCrew(order.CrewId);
                var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
                LastWeek.Add(new OrderRecord
                {
                    Lieutenant = lieutenant != null ? lieutenant.FullName : "?",
                    Type = order.Type,
                    TargetSummary = order.BlockTargets.Count > 0
                        ? order.BlockTargets.Count + " blocks"
                        : order.TargetLabel,
                    Men = order.Men,
                    Outcome = pastAll.Contains(order.Id)
                        ? OrderOutcome.NeverReached
                        : OrderOutcome.Completed,
                });
            }

            Relations.ApplyPending();
            Campaign.Week++;
            Accounts.Sheets.Add(new WeekSheet { Week = Campaign.Week });
            Plan = new WeekPlan();
            Version++;
            Debug.Log("[Outfit] Week committed - week " + Campaign.Week + " opens.");
        }

        /// <summary>Stores the change as pending - stances turn over at the week
        /// commit, never mid-plan.</summary>
        public OpResult SetStance(int gangId, Stance stance)
        {
            if (gangId == Gangs.GangCatalog.PlayerGangId)
                return OpResult.Fail(UI.LedgerText.ReasonOwnOutfit);

            Relations.SetPending(gangId, stance);
            Version++;
            return OpResult.Success;
        }

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
