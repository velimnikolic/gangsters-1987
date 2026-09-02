using System;
using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Territory;

namespace LivingCity.Business
{
    /// <summary>The successful property attack that put a business out of action.</summary>
    public enum BusinessShutdownCause
    {
        None,
        SmashUp,
        Arson,

        /// <summary>Powder. A week shut, the same as a fire, and the same repair bill -
        /// a blown-out front is a blown-out front however it went (D12).</summary>
        Bomb,
    }

    /// <summary>
    /// Tunable WAR-001 rules. Durations are game hours so the clock boundary is exact;
    /// prices live beside them so a balance pass can replace the defaults without
    /// changing the shutdown state machine.
    /// </summary>
    public sealed class BusinessShutdownConfig
    {
        public BusinessShutdownConfig(
            double smashHours = 3d * 24d,
            double arsonHours = 7d * 24d,
            int smashRepairPrice = 1_000,
            int arsonRepairPrice = 5_000,
            double bombHours = 7d * 24d,
            int bombRepairPrice = 5_000)
        {
            SmashHours = Math.Max(0d, smashHours);
            ArsonHours = Math.Max(0d, arsonHours);
            SmashRepairPrice = Math.Max(0, smashRepairPrice);
            ArsonRepairPrice = Math.Max(0, arsonRepairPrice);
            BombHours = Math.Max(0d, bombHours);
            BombRepairPrice = Math.Max(0, bombRepairPrice);
        }

        public double SmashHours { get; }
        public double ArsonHours { get; }
        public int SmashRepairPrice { get; }
        public int ArsonRepairPrice { get; }

        /// <summary>D12. Seven days, as a fire - the shop is not there any more either
        /// way.</summary>
        public double BombHours { get; }

        public int BombRepairPrice { get; }

        public double DurationOf(BusinessShutdownCause cause) =>
            cause == BusinessShutdownCause.Arson ? ArsonHours
            : cause == BusinessShutdownCause.Bomb ? BombHours
            : cause == BusinessShutdownCause.SmashUp ? SmashHours
            : 0d;

        public int RepairPriceOf(BusinessShutdownCause cause) =>
            cause == BusinessShutdownCause.Arson ? ArsonRepairPrice
            : cause == BusinessShutdownCause.Bomb ? BombRepairPrice
            : cause == BusinessShutdownCause.SmashUp ? SmashRepairPrice
            : 0;

        public static BusinessShutdownConfig Default { get; } =
            new BusinessShutdownConfig();
    }

    /// <summary>Read-only shutdown truth at a particular campaign hour.</summary>
    public readonly struct BusinessShutdownStatus
    {
        public BusinessShutdownStatus(
            TerritoryBusinessId businessId,
            BusinessShutdownCause cause,
            double startedAt,
            double recoveryAt,
            int repairPrice,
            double readAt)
        {
            BusinessId = businessId;
            Cause = cause;
            StartedAt = startedAt;
            RecoveryAt = recoveryAt;
            RepairPrice = Math.Max(0, repairPrice);
            RemainingHours = Math.Max(0d, recoveryAt - readAt);
        }

        public TerritoryBusinessId BusinessId { get; }
        public BusinessShutdownCause Cause { get; }
        public double StartedAt { get; }
        public double RecoveryAt { get; }
        public int RepairPrice { get; }
        public double RemainingHours { get; }
        public int RemainingDays => Math.Max(1, (int)Math.Ceiling(RemainingHours / 24d));
    }

    /// <summary>Serializable pure-data form used by the campaign save boundary.</summary>
    [Serializable]
    public struct BusinessShutdownSnapshot
    {
        public string BusinessId;
        public BusinessShutdownCause Cause;
        public double StartedAt;
        public double RecoveryAt;
    }

    public enum BusinessShutdownChangeKind
    {
        Started,
        Extended,
        Repaired,
        Expired,
        Restored,
    }

    public readonly struct BusinessShutdownChange
    {
        public BusinessShutdownChange(
            BusinessShutdownChangeKind kind, TerritoryBusinessId businessId,
            BusinessShutdownCause cause, double gameHour, double recoveryAt)
        {
            Kind = kind;
            BusinessId = businessId;
            Cause = cause;
            GameHour = gameHour;
            RecoveryAt = recoveryAt;
        }

        public BusinessShutdownChangeKind Kind { get; }
        public TerritoryBusinessId BusinessId { get; }
        public BusinessShutdownCause Cause { get; }
        public double GameHour { get; }
        public double RecoveryAt { get; }
    }

    /// <summary>
    /// Authoritative temporary closure state. The directory remains the authority for
    /// whether a business is Trading or Shut; this ledger supplies the cause and absolute
    /// recovery deadline that the record deliberately did not have in the foundation.
    /// No scene object, streamed marker or UI timer participates in the rule.
    /// </summary>
    public sealed class BusinessShutdownLedger
    {
        sealed class Entry
        {
            public BusinessShutdownCause Cause;
            public double StartedAt;
            public double RecoveryAt;
        }

        readonly BusinessDirectory directory;
        readonly Dictionary<TerritoryBusinessId, Entry> entries =
            new Dictionary<TerritoryBusinessId, Entry>();
        readonly List<TerritoryBusinessId> scratch = new List<TerritoryBusinessId>();

        public BusinessShutdownLedger(
            BusinessDirectory directory, BusinessShutdownConfig config = null)
        {
            this.directory = directory ??
                throw new ArgumentNullException(nameof(directory));
            Config = config ?? BusinessShutdownConfig.Default;
        }

        public BusinessShutdownConfig Config { get; }
        public int Version { get; private set; }
        public event Action<BusinessShutdownChange> Changed;

        public bool Shut(
            TerritoryBusinessId businessId, BusinessShutdownCause cause, double gameHour)
        {
            if (DamageRefusal(businessId, cause, gameHour) != null)
                return false;

            var proposed = gameHour + Config.DurationOf(cause);
            if (proposed <= gameHour)
                return false;

            var active = entries.TryGetValue(businessId, out var entry) &&
                         IsActive(entry, gameHour);
            var kind = active ? BusinessShutdownChangeKind.Extended
                              : BusinessShutdownChangeKind.Started;
            if (!active)
            {
                entry = new Entry
                {
                    Cause = cause,
                    StartedAt = gameHour,
                    RecoveryAt = proposed,
                };
                entries[businessId] = entry;
            }
            else
            {
                // The only legal transition while damaged is Smash Up -> Arson. It starts
                // a fresh seven-day burn-out period; same-state attacks and any attack on
                // an already torched shop are rejected by DamageRefusal above.
                entry.Cause = cause;
                entry.StartedAt = gameHour;
                entry.RecoveryAt = proposed;
            }

            directory.SetState(businessId, BusinessOperationalState.Shut);
            Version++;
            Changed?.Invoke(new BusinessShutdownChange(
                kind, businessId, entry.Cause, gameHour, entry.RecoveryAt));
            return true;
        }

        /// <summary>The authoritative damage-state gate used both when an order is filed
        /// and when its street outcome arrives. Smashed may escalate to arson; nothing may
        /// repeat the same damage or act on an already torched premises until it reopens.</summary>
        public string DamageRefusal(
            TerritoryBusinessId businessId, BusinessShutdownCause cause, double gameHour)
        {
            if (!businessId.IsValid || cause == BusinessShutdownCause.None ||
                !directory.TryGet(businessId, out _))
                return "the business cannot be damaged";
            if (!entries.TryGetValue(businessId, out var entry) ||
                !IsActive(entry, gameHour))
                return null;
            // A shop already blown out or burned out cannot be damaged again until it
            // reopens: there is nothing left standing to damage.
            if (entry.Cause == BusinessShutdownCause.Arson ||
                entry.Cause == BusinessShutdownCause.Bomb)
                return cause == entry.Cause
                    ? (entry.Cause == BusinessShutdownCause.Bomb
                        ? "the premises are already blown out"
                        : "the premises are already torched")
                    : "the premises are burned out";
            if (entry.Cause == BusinessShutdownCause.SmashUp &&
                cause == BusinessShutdownCause.SmashUp)
                return "the premises are already smashed up";
            return null;
        }

        public bool IsShutAt(TerritoryBusinessId businessId, double gameHour) =>
            entries.TryGetValue(businessId, out var entry) && IsActive(entry, gameHour);

        /// <summary>The racket meter samples its daily boundary through this method.
        /// Existing debt is retained, but a closed boundary adds no new seventh.</summary>
        public bool ShouldAccrueRacketAt(
            TerritoryBusinessId businessId, double gameHour) =>
            !IsShutAt(businessId, gameHour);

        public bool TryGet(
            TerritoryBusinessId businessId, double gameHour,
            out BusinessShutdownStatus status)
        {
            status = default;
            if (!entries.TryGetValue(businessId, out var entry) ||
                !IsActive(entry, gameHour))
                return false;
            status = Status(businessId, entry, gameHour);
            return true;
        }

        public bool Repair(TerritoryBusinessId businessId, double gameHour)
        {
            if (!entries.TryGetValue(businessId, out var entry) ||
                !IsActive(entry, gameHour))
                return false;

            entries.Remove(businessId);
            directory.SetState(businessId, BusinessOperationalState.Trading);
            Version++;
            Changed?.Invoke(new BusinessShutdownChange(
                BusinessShutdownChangeKind.Repaired, businessId, entry.Cause,
                gameHour, gameHour));
            return true;
        }

        public void AdvanceTo(double gameHour)
        {
            scratch.Clear();
            foreach (var pair in entries)
                if (gameHour >= pair.Value.RecoveryAt)
                    scratch.Add(pair.Key);

            for (var i = 0; i < scratch.Count; i++)
            {
                var id = scratch[i];
                var entry = entries[id];
                entries.Remove(id);
                directory.SetState(id, BusinessOperationalState.Trading);
                Version++;
                Changed?.Invoke(new BusinessShutdownChange(
                    BusinessShutdownChangeKind.Expired, id, entry.Cause,
                    gameHour, entry.RecoveryAt));
            }
        }

        public void CollectSnapshots(List<BusinessShutdownSnapshot> into)
        {
            if (into == null)
                return;
            into.Clear();
            foreach (var pair in entries)
                into.Add(new BusinessShutdownSnapshot
                {
                    BusinessId = pair.Key.Value,
                    Cause = pair.Value.Cause,
                    StartedAt = pair.Value.StartedAt,
                    RecoveryAt = pair.Value.RecoveryAt,
                });
            into.Sort((a, b) => string.CompareOrdinal(a.BusinessId, b.BusinessId));
        }

        public void Restore(
            IReadOnlyList<BusinessShutdownSnapshot> snapshots, double gameHour)
        {
            foreach (var pair in entries)
                directory.SetState(pair.Key, BusinessOperationalState.Trading);
            entries.Clear();
            if (snapshots != null)
                for (var i = 0; i < snapshots.Count; i++)
                {
                    var saved = snapshots[i];
                    var id = new TerritoryBusinessId(saved.BusinessId);
                    if (!id.IsValid || saved.Cause == BusinessShutdownCause.None ||
                        saved.RecoveryAt <= gameHour ||
                        !directory.TryGet(id, out _))
                        continue;
                    entries[id] = new Entry
                    {
                        Cause = saved.Cause,
                        StartedAt = saved.StartedAt,
                        RecoveryAt = saved.RecoveryAt,
                    };
                    directory.SetState(id, BusinessOperationalState.Shut);
                    Changed?.Invoke(new BusinessShutdownChange(
                        BusinessShutdownChangeKind.Restored, id, saved.Cause,
                        gameHour, saved.RecoveryAt));
                }
            Version++;
        }

        static bool IsActive(Entry entry, double gameHour) =>
            entry != null && gameHour >= entry.StartedAt && gameHour < entry.RecoveryAt;

        BusinessShutdownStatus Status(
            TerritoryBusinessId id, Entry entry, double gameHour) =>
            new BusinessShutdownStatus(
                id, entry.Cause, entry.StartedAt, entry.RecoveryAt,
                Config.RepairPriceOf(entry.Cause), gameHour);
    }

    /// <summary>The pure transaction used by OutfitDirector's repair command.</summary>
    public static class BusinessRepair
    {
        public static string Try(
            BusinessShutdownLedger shutdowns,
            TerritoryBusinessId businessId,
            int payerGangId,
            int ownerGangId,
            double gameHour,
            Accounts accounts,
            out int charged)
        {
            charged = 0;
            if (shutdowns == null || !shutdowns.TryGet(businessId, gameHour, out var status))
                return "the business is not closed";
            if (payerGangId != ownerGangId)
                return "we protect this place, but we do not own its deed";

            var refusal = BalanceMath.TryPurchase(accounts, status.RepairPrice);
            if (refusal != null)
                return refusal;

            if (!shutdowns.Repair(businessId, gameHour))
            {
                // Defensive rollback: a transaction that repaired nothing cannot remain
                // on the books, even if a future caller introduces re-entrancy here.
                accounts.Safe += status.RepairPrice;
                if (accounts.Current != null)
                    accounts.Current.Purchases -= status.RepairPrice;
                return "the repair could not be completed";
            }

            charged = status.RepairPrice;
            return null;
        }
    }

    public static class BusinessShutdownText
    {
        public static string Line(BusinessShutdownStatus status)
        {
            var cause = status.Cause == BusinessShutdownCause.Arson
                ? "burned out"
                : "smashed up";
            var days = status.RemainingDays;
            return "closed - " + cause + " - reopens in " + days +
                   (days == 1 ? " day" : " days");
        }
    }
}
