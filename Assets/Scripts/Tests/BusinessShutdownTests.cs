using System.Collections.Generic;
using LivingCity.Business;
using LivingCity.Outfit;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>Headless WAR-001 contract: durations, racket loss, owner repair and save data.</summary>
    public static class BusinessShutdownTests
    {
        static readonly TerritoryGangId Us = new TerritoryGangId(0);

        public static List<string> Run()
        {
            var failures = new List<string>();
            DurationsAndBoundaryAreExact(failures);
            DamageCannotRepeatButSmashCanEscalateToArson(failures);
            ClosedDaysAddNoRacketDebt(failures);
            OnlyTheOwnerCanRepairAndPaymentIsOnce(failures);
            SnapshotRestoresCauseAndDeadline(failures);
            SharedOrdersGateCollectionAndOwnerRepair(failures);
            return failures;
        }

        static void DurationsAndBoundaryAreExact(List<string> failures)
        {
            Make(out var directory, out var id, out var shutdowns);
            if (!shutdowns.Shut(id, BusinessShutdownCause.SmashUp, 10d) ||
                !shutdowns.TryGet(id, 10d, out var smash) || smash.RecoveryAt != 82d)
                failures.Add("WAR-001: Smash Up did not close the business for exactly 72 hours.");
            if (!shutdowns.IsShutAt(id, 81.999d) || shutdowns.IsShutAt(id, 82d))
                failures.Add("WAR-001: the Smash Up recovery boundary is off by a tick.");
            shutdowns.AdvanceTo(82d);
            if (!directory.TryGet(id, out var record) ||
                record.State != BusinessOperationalState.Trading)
                failures.Add("WAR-001: natural Smash Up expiry did not reopen the business.");

            Make(out directory, out id, out shutdowns);
            shutdowns.Shut(id, BusinessShutdownCause.Arson, 10d);
            if (!shutdowns.TryGet(id, 10d, out var arson) || arson.RecoveryAt != 178d)
                failures.Add("WAR-001: arson did not close the business for exactly 168 hours.");
        }

        static void DamageCannotRepeatButSmashCanEscalateToArson(List<string> failures)
        {
            Make(out _, out var id, out var shutdowns);
            if (!shutdowns.Shut(id, BusinessShutdownCause.SmashUp, 0d) ||
                shutdowns.Shut(id, BusinessShutdownCause.SmashUp, 24d))
                failures.Add("WAR-001: an active Smash Up could be repeated.");

            if (!shutdowns.Shut(id, BusinessShutdownCause.Arson, 24d) ||
                !shutdowns.TryGet(id, 24d, out var arson) ||
                arson.Cause != BusinessShutdownCause.Arson ||
                arson.StartedAt != 24d || arson.RecoveryAt != 192d)
                failures.Add("WAR-001: Smash Up could not escalate into a fresh seven-day arson closure.");

            if (shutdowns.Shut(id, BusinessShutdownCause.Arson, 48d) ||
                shutdowns.Shut(id, BusinessShutdownCause.SmashUp, 48d))
                failures.Add("WAR-001: a torched premises accepted another damage job.");
        }

        static void ClosedDaysAddNoRacketDebt(List<string> failures)
        {
            Make(out _, out var id, out var shutdowns);
            var dues = new TerritoryDuesLedger();
            dues.AccrueDay(id, Us, 700);
            var before = dues.OwedOf(id, Us);
            shutdowns.Shut(id, BusinessShutdownCause.SmashUp, 10d); // closed at 24,48,72
            for (var day = 1; day <= 3; day++)
                if (shutdowns.ShouldAccrueRacketAt(id, day * 24d))
                    dues.AccrueDay(id, Us, 700);
            if (dues.OwedOf(id, Us) != before)
                failures.Add("WAR-001: a closed day created protection debt.");

            if (shutdowns.ShouldAccrueRacketAt(id, 82d))
                dues.AccrueDay(id, Us, 700);
            if (dues.OwedOf(id, Us) <= before)
                failures.Add("WAR-001: future dues did not resume at the recovery boundary.");
        }

        static void OnlyTheOwnerCanRepairAndPaymentIsOnce(List<string> failures)
        {
            Make(out var directory, out var id, out var shutdowns);
            shutdowns.Shut(id, BusinessShutdownCause.SmashUp, 0d);
            var accounts = new Accounts { Safe = 10_000 };
            accounts.Open(1);

            var refused = BusinessRepair.Try(
                shutdowns, id, payerGangId: 0, ownerGangId: 1, gameHour: 12d,
                accounts, out var charged);
            if (refused == null || charged != 0 || accounts.Safe != 10_000 ||
                !shutdowns.IsShutAt(id, 12d))
                failures.Add("WAR-001: a protector repaired an NPC-owned business.");

            var accepted = BusinessRepair.Try(
                shutdowns, id, payerGangId: 0, ownerGangId: 0, gameHour: 12d,
                accounts, out charged);
            if (accepted != null || charged != 1_000 || accounts.Safe != 9_000 ||
                accounts.Current.Purchases != 1_000 ||
                !directory.TryGet(id, out var record) ||
                record.State != BusinessOperationalState.Trading)
                failures.Add("WAR-001: an owner repair did not charge once and reopen the shop.");

            var safe = accounts.Safe;
            BusinessRepair.Try(
                shutdowns, id, 0, 0, 13d, accounts, out charged);
            if (charged != 0 || accounts.Safe != safe)
                failures.Add("WAR-001: repairing an already open business charged twice.");
        }

        static void SnapshotRestoresCauseAndDeadline(List<string> failures)
        {
            Make(out _, out var id, out var source);
            source.Shut(id, BusinessShutdownCause.Arson, 5d);
            var saved = new List<BusinessShutdownSnapshot>();
            source.CollectSnapshots(saved);

            Make(out var directory, out var restoredId, out var restored);
            restored.Restore(saved, 20d);
            if (restoredId != id || !restored.TryGet(id, 20d, out var status) ||
                status.Cause != BusinessShutdownCause.Arson || status.RecoveryAt != 173d ||
                !directory.TryGet(id, out var record) ||
                record.State != BusinessOperationalState.Shut)
                failures.Add("WAR-001: save/load did not preserve the shutdown cause and deadline.");
        }

        static void SharedOrdersGateCollectionAndOwnerRepair(List<string> failures)
        {
            var rows = new List<TerritoryRacketOrder>();
            var closed = new TerritoryDoorClosure(
                true, "closed - smashed up - reopens in 3 days",
                repairVisible: false, repairAvailable: false, repairPrice: 1_000,
                cause: BusinessShutdownCause.SmashUp);
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Compliant, DoorTenure.Paying,
                true, true, true, 80_000, rows, closure: closed);
            if (Available(rows, TerritoryDoorRowKind.Racket, TerritoryRacketIntent.Collect) ||
                HasKind(rows, TerritoryDoorRowKind.Repair))
                failures.Add("WAR-001: a closed NPC shop offered collection or player repair.");
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, DoorTenure.Rival,
                true, true, true, 80_000, rows, closure: closed);
            if (AvailableJob(rows, OrderType.SmashUp) ||
                !AvailableJob(rows, OrderType.Torch))
                failures.Add("WAR-001: a smashed shop did not block Smash Up while allowing Torch.");

            closed = new TerritoryDoorClosure(
                true, "closed - burned out - reopens in 7 days",
                repairVisible: true, repairAvailable: true, repairPrice: 5_000,
                cause: BusinessShutdownCause.Arson);
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, DoorTenure.Ours,
                true, false, false, 80_000, rows, closure: closed);
            if (!HasAvailableKind(rows, TerritoryDoorRowKind.Repair))
                failures.Add("WAR-001: a closed player-owned shop offered no repair action.");
            TerritoryRacketOrders.For(
                TerritoryProtectionState.Unaffiliated, DoorTenure.Rival,
                true, true, true, 80_000, rows, closure: closed);
            if (AvailableJob(rows, OrderType.SmashUp) ||
                AvailableJob(rows, OrderType.Torch))
                failures.Add("WAR-001: a torched shop offered another damage job.");
        }

        static bool Available(
            List<TerritoryRacketOrder> rows, TerritoryDoorRowKind kind,
            TerritoryRacketIntent intent)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Kind == kind && rows[i].Intent == intent)
                    return rows[i].Available;
            return false;
        }

        static bool HasKind(List<TerritoryRacketOrder> rows, TerritoryDoorRowKind kind)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Kind == kind)
                    return true;
            return false;
        }

        static bool AvailableJob(List<TerritoryRacketOrder> rows, OrderType type)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Kind == TerritoryDoorRowKind.Job &&
                    rows[i].Job == type)
                    return rows[i].Available;
            return false;
        }

        static bool HasAvailableKind(
            List<TerritoryRacketOrder> rows, TerritoryDoorRowKind kind)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Kind == kind && rows[i].Available)
                    return true;
            return false;
        }

        static void Make(
            out BusinessDirectory directory,
            out TerritoryBusinessId id,
            out BusinessShutdownLedger shutdowns)
        {
            directory = new BusinessDirectory();
            var site = BusinessIdentity.Site("test", "shutdown", "door");
            var ownerId = BusinessIdentity.Owner(site);
            directory.RegisterOwner(
                ownerId, BusinessOwnerKind.Individual, "Test Owner",
                BusinessOwnerAge.Middle, 1);
            var record = directory.Register(
                site, BusinessArchetypeId.Grocer, "Test Shop", ownerId,
                BusinessSiteSize.Small, 1_200, "test");
            id = record.Id;
            shutdowns = new BusinessShutdownLedger(directory);
        }
    }
}
