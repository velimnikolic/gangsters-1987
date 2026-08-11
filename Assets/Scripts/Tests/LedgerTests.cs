using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.UI;

namespace LivingCity.Tests
{
    /// <summary>
    /// The ledger's money model: the campaign calendar, wage derivation, balance-sheet
    /// arithmetic, and the exact-cash formatter. Same discipline as
    /// <see cref="PersonnelTests"/>: plain static class, failures as data, no
    /// UnityEngine - the whole Outfit core is engine-free on purpose.
    /// </summary>
    public static class LedgerTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();

            CalendarDerivesYear(failures);
            WagesDeriveFromTheRoster(failures);
            HiringMovesThePayroll(failures);
            BalanceArithmetic(failures);
            TaxOnlyOnProfit(failures);
            RiskThresholds(failures);
            AssetsAreBookValue(failures);
            ReportUsesFrozenWagesWhenClosed(failures);
            CashFormatsExactly(failures);
            PurchaseGateDebitsAndBooks(failures);
            CataloguePricesMatchTheSheet(failures);
            NewStockEntersThePoolUnheld(failures);
            StancesTurnOverAtCommit(failures);
            TurfIsHeldPerBuilding(failures);
            StanceWordingIsExhaustive(failures);
            OrderTableCoversEveryType(failures);
            TravelDrivesTheCapacity(failures);
            PastTheLineFallsInListOrder(failures);
            CrewKitReadsVehiclesAndSkill(failures);

            return failures;
        }

        static void OrderTableCoversEveryType(List<string> failures)
        {
            foreach (OrderType type in System.Enum.GetValues(typeof(OrderType)))
            {
                var spec = OrderTable.SpecOf(type);
                if (spec.Type != type)
                    failures.Add($"OrderTableCoversEveryType: {type} has no spec row.");
                if (LedgerText.OrderLabel(type).Length == 0)
                    failures.Add($"OrderTableCoversEveryType: {type} has no label.");
                if (spec.Mode == TargetMode.Area && spec.BlocksPerManWeek <= 0f)
                    failures.Add($"OrderTableCoversEveryType: {type} area with no throughput.");
                if (spec.Mode == TargetMode.Point && spec.PointCost <= 0f)
                    failures.Add($"OrderTableCoversEveryType: {type} point with no cost.");
            }

            // The reference throughputs: extortion 2-3 blocks/man/week, collection ~8.
            var extort = OrderTable.SpecOf(OrderType.Extort);
            if (extort.BlocksPerManWeek < 2f || extort.BlocksPerManWeek > 3f)
                failures.Add("OrderTableCoversEveryType: extortion throughput off the sheet.");
            if (OrderTable.SpecOf(OrderType.CollectProtection).BlocksPerManWeek != 8f)
                failures.Add("OrderTableCoversEveryType: collection throughput off the sheet.");
        }

        static void TravelDrivesTheCapacity(List<string> failures)
        {
            // The same distance costs a foot crew five times what it costs a car.
            var foot = OrderMath.TravelFraction(600f, hasVehicle: false);
            var car = OrderMath.TravelFraction(600f, hasVehicle: true);
            if (foot != 0.5f || car != 0.1f)
                failures.Add($"TravelDrivesTheCapacity: foot {foot} / car {car}.");

            // Far on foot saturates at the cap - the crew spends the week walking.
            if (OrderMath.TravelFraction(50_000f, false) != OrderMath.MaxTravelFraction)
                failures.Add("TravelDrivesTheCapacity: no travel cap.");

            var extort = OrderTable.SpecOf(OrderType.Extort);
            // 5 blocks at 2.5/man-week = 2 man-weeks of work, travel-free: 2 men.
            if (OrderMath.MenNeeded(extort, 5, 0f) != 2)
                failures.Add("TravelDrivesTheCapacity: clean work costed wrong.");
            // Same job at 50% travel: each man delivers half a week - 4 men.
            if (OrderMath.MenNeeded(extort, 5, 0.5f) != 4)
                failures.Add("TravelDrivesTheCapacity: travel did not raise the crew.");

            if (!OrderMath.Undermanned(extort, 5, 0.5f, 2))
                failures.Add("TravelDrivesTheCapacity: an undermanned job read as fine.");
            if (OrderMath.Undermanned(extort, 5, 0.5f, 4))
                failures.Add("TravelDrivesTheCapacity: a manned job read as short.");
        }

        static void PastTheLineFallsInListOrder(List<string> failures)
        {
            var plan = new WeekPlan();
            for (var i = 0; i < 3; i++)
            {
                var order = new PlannedOrder { CrewId = 7, Men = 2, Type = OrderType.Patrol };
                order.Id = plan.NextOrderId();
                plan.Confirmed.Add(order);
            }

            if (plan.CommittedMen(7) != 6)
                failures.Add("PastTheLineFallsInListOrder: committed men miscounted.");

            // A crew of four: orders 0 and 1 fit (2+2); order 2 crosses the line.
            var past = new List<int>();
            OrderMath.PastTheLine(plan, 7, crewSize: 4, past);
            if (past.Count != 1 || past[0] != plan.Confirmed[2].Id)
                failures.Add("PastTheLineFallsInListOrder: the line fell on the wrong row.");

            // Reordering moves the line - priority is the list, nothing else.
            (plan.Confirmed[0], plan.Confirmed[2]) = (plan.Confirmed[2], plan.Confirmed[0]);
            OrderMath.PastTheLine(plan, 7, 4, past);
            if (past.Count != 1 || past[0] != plan.Confirmed[2].Id)
                failures.Add("PastTheLineFallsInListOrder: reordering did not move the line.");
        }

        static void CrewKitReadsVehiclesAndSkill(List<string> failures)
        {
            var roster = RosterSeeder.Generate(42);
            var crew = roster.Crews[0];

            if (CrewKit.MenOf(crew) != 3)
                failures.Add("CrewKitReadsVehiclesAndSkill: the lieutenant does not count.");
            if (CrewKit.HasVehicle(roster, crew))
                failures.Add("CrewKitReadsVehiclesAndSkill: a car out of nowhere.");

            // Sign the seeded car out to the crew's lieutenant - gear only issues
            // to him now - and the crew rides.
            RosterEquipment car = null;
            foreach (var item in roster.Equipment)
                if (item.Kind == EquipmentKind.Vehicle)
                    car = item;
            RosterOps.GiveEquipment(roster, car.Id, crew.LieutenantId);
            if (!CrewKit.HasVehicle(roster, crew))
                failures.Add("CrewKitReadsVehiclesAndSkill: the signed-out car is invisible.");

            var best = CrewKit.BestAt(roster, crew, CharacterAttribute.Firearms);
            var manual = 0;
            void Consider(int id)
            {
                var m = roster.Find(id);
                var v = m.GetHalfSteps(CharacterAttribute.Firearms);
                if (v > manual)
                    manual = v;
            }
            Consider(crew.LieutenantId);
            foreach (var id in crew.HoodIds)
                Consider(id);
            if (best != manual)
                failures.Add("CrewKitReadsVehiclesAndSkill: BestAt disagrees with the sum.");
        }

        static void StancesTurnOverAtCommit(List<string> failures)
        {
            var relations = new GangRelations();

            if (relations.StanceWith(1) != Stance.Peace)
                failures.Add("StancesTurnOverAtCommit: the outfit does not arrive quietly.");

            relations.SetPending(1, Stance.War);
            if (relations.StanceWith(1) != Stance.Peace)
                failures.Add("StancesTurnOverAtCommit: war landed mid-week.");
            if (!relations.TryGetPending(1, out var pending) || pending != Stance.War)
                failures.Add("StancesTurnOverAtCommit: the pending change vanished.");

            // "Never mind" - setting back to the current stance withdraws the change.
            relations.SetPending(1, Stance.Peace);
            if (relations.TryGetPending(1, out _))
                failures.Add("StancesTurnOverAtCommit: a withdrawn change survived.");

            relations.SetPending(1, Stance.Truce);
            relations.ApplyPending();
            if (relations.StanceWith(1) != Stance.Truce ||
                relations.TryGetPending(1, out _))
                failures.Add("StancesTurnOverAtCommit: the commit did not turn the stance.");
        }

        static void TurfIsHeldPerBuilding(List<string> failures)
        {
            // Day one, as the markers would present it: each family holds exactly its
            // own front premise - one BUILDING, never the block around it.
            var holdings = new List<Turf.Holding>
            {
                new Turf.Holding(0, 12),   // the player's front
                new Turf.Holding(1, 30),
                new Turf.Holding(2, 4),
            };

            for (var gang = 0; gang <= 2; gang++)
                if (Turf.CountOf(holdings, gang) != 1)
                    failures.Add($"TurfIsHeldPerBuilding: gang {gang} holds " +
                                 $"{Turf.CountOf(holdings, gang)} buildings day one.");

            if (Turf.DominantIn(holdings, 12) != 0 || Turf.DominantIn(holdings, 30) != 1)
                failures.Add("TurfIsHeldPerBuilding: a front premise answers for the wrong family.");
            if (Turf.DominantIn(holdings, 7) != -1)
                failures.Add("TurfIsHeldPerBuilding: empty ground found a controller.");

            // The takeover arithmetic ahead of its mechanic: premises are counted one
            // by one, two beat one, and a shared lead is contested - no controller.
            holdings.Add(new Turf.Holding(1, 12));
            holdings.Add(new Turf.Holding(1, 12));
            if (Turf.CountIn(holdings, 12, 1) != 2 || Turf.DominantIn(holdings, 12) != 1)
                failures.Add("TurfIsHeldPerBuilding: two premises did not out-hold one.");

            holdings.Add(new Turf.Holding(0, 12));
            if (Turf.DominantIn(holdings, 12) != -1)
                failures.Add("TurfIsHeldPerBuilding: contested ground found a controller.");
        }

        static void StanceWordingIsExhaustive(List<string> failures)
        {
            foreach (Stance stance in System.Enum.GetValues(typeof(Stance)))
            {
                if (LedgerText.StanceLabel(stance).Length == 0)
                    failures.Add($"StanceWordingIsExhaustive: {stance} has no label.");
                if (LedgerText.StanceEffect(stance).Length == 0)
                    failures.Add($"StanceWordingIsExhaustive: {stance} has no effect line.");
            }
            if (LedgerText.StanceTakesEffect.Length == 0 ||
                LedgerText.StrengthUnknown.Length == 0)
                failures.Add("StanceWordingIsExhaustive: a diplomacy line is empty.");
        }

        static void PurchaseGateDebitsAndBooks(List<string> failures)
        {
            var accounts = new Accounts();
            accounts.Sheets.Add(new WeekSheet { Week = 1 });

            if (BalanceMath.TryPurchase(accounts, 750) != null)
                failures.Add("PurchaseGateDebitsAndBooks: an affordable buy refused.");
            if (accounts.Safe != Accounts.StartingSafe - 750 ||
                accounts.Current.Purchases != 750)
                failures.Add("PurchaseGateDebitsAndBooks: the safe and the sheet disagree.");

            var refusal = BalanceMath.TryPurchase(accounts, 999_999);
            if (refusal == null || refusal.Length == 0)
                failures.Add("PurchaseGateDebitsAndBooks: short money bought anyway.");
            if (accounts.Safe != Accounts.StartingSafe - 750 ||
                accounts.Current.Purchases != 750)
                failures.Add("PurchaseGateDebitsAndBooks: a refusal touched the books.");
        }

        static void CataloguePricesMatchTheSheet(List<string> failures)
        {
            var expected = new Dictionary<string, int>
            {
                { ".38 Pistol", 100 }, { "Shotgun", 750 }, { "Rifle", 750 },
                { "Tommy Gun", 2000 }, { "Twin Pack Pistols", 3000 },
            };

            foreach (var item in ArmoryCatalog.Weapons)
            {
                if (!expected.TryGetValue(item.DisplayName, out var price))
                    failures.Add($"CataloguePricesMatchTheSheet: unexpected {item.DisplayName}.");
                else if (item.Price != price)
                    failures.Add($"CataloguePricesMatchTheSheet: {item.DisplayName} at " +
                                 $"{item.Price}.");
                if (item.Note.Length == 0)
                    failures.Add($"CataloguePricesMatchTheSheet: {item.DisplayName} " +
                                 "has no note.");
            }
            if (ArmoryCatalog.Weapons.Length != expected.Count)
                failures.Add("CataloguePricesMatchTheSheet: weapon count drifted.");
            if (ArmoryCatalog.Vehicles.Length == 0)
                failures.Add("CataloguePricesMatchTheSheet: no vehicles for sale.");
        }

        static void NewStockEntersThePoolUnheld(List<string> failures)
        {
            var roster = RosterSeeder.Generate(42);
            var before = roster.Equipment.Count;
            var assetsBefore = BalanceMath.AssetsOf(roster);

            var item = RosterOps.AddEquipment(roster, EquipmentKind.TommyGun,
                "Tommy Gun", 2000);

            if (roster.Equipment.Count != before + 1 ||
                item.HolderId != RosterEquipment.Unheld)
                failures.Add("NewStockEntersThePoolUnheld: the buy did not pool unheld.");
            if (BalanceMath.AssetsOf(roster) != assetsBefore + 2000)
                failures.Add("NewStockEntersThePoolUnheld: assets missed the book value.");

            // The exclusivity rules apply to bought stock like seeded stock - and a
            // weapon only lands on a lieutenant now, so the two would-be holders are
            // two crews' heads (the second promoted out of the pool for the occasion).
            var a = roster.Find(roster.Crews[0].LieutenantId);
            var pool = new List<int>();
            roster.PoolIds(pool);
            if (pool.Count == 0 || !RosterOps.Promote(roster, pool[0], out _).Ok)
            {
                failures.Add("NewStockEntersThePoolUnheld: no second lieutenant to " +
                             "test exclusivity with.");
                return;
            }
            var b = roster.Find(pool[0]);
            RosterOps.GiveEquipment(roster, item.Id, a.Id);
            if (RosterOps.GiveEquipment(roster, item.Id, b.Id).Ok)
                failures.Add("NewStockEntersThePoolUnheld: one tommy gun, two holders.");
        }

        static void CalendarDerivesYear(List<string> failures)
        {
            var campaign = new Campaign { Week = 1 };
            if (campaign.Year != Campaign.StartYear || campaign.WeekOfYear != 1)
                failures.Add("CalendarDerivesYear: week 1 misreads.");

            campaign.Week = 52;
            if (campaign.Year != Campaign.StartYear || campaign.WeekOfYear != 52)
                failures.Add("CalendarDerivesYear: week 52 misreads.");

            campaign.Week = 53;
            if (campaign.Year != Campaign.StartYear + 1 || campaign.WeekOfYear != 1)
                failures.Add("CalendarDerivesYear: the year does not roll at 53.");
        }

        static void WagesDeriveFromTheRoster(List<string> failures)
        {
            var roster = RosterSeeder.Generate(42);
            var expected = 0;
            foreach (var member in roster.Members)
                expected += Wages.WageFor(member);

            if (Wages.WeeklyPayroll(roster) != expected || expected <= 0)
                failures.Add("WagesDeriveFromTheRoster: payroll is not the member sum.");

            // The dead come off the books; the jailed stay on them.
            roster.Members[1].Status = CharacterStatus.Dead;
            var afterDeath = Wages.WeeklyPayroll(roster);
            roster.Members[2].Status = CharacterStatus.Jailed;
            var afterJail = Wages.WeeklyPayroll(roster);

            if (afterDeath >= expected)
                failures.Add("WagesDeriveFromTheRoster: a dead man is still paid.");
            if (afterJail != afterDeath)
                failures.Add("WagesDeriveFromTheRoster: jail changed the wage bill.");
        }

        static void HiringMovesThePayroll(List<string> failures)
        {
            var roster = RosterSeeder.Generate(7);
            var before = Wages.WeeklyPayroll(roster);

            var recruit = new Character { Id = roster.NextCharacterId() };
            for (var a = 0; a < AttributeScale.Count; a++)
                recruit.SetHalfSteps((CharacterAttribute)a, 6);
            roster.Members.Add(recruit);

            if (Wages.WeeklyPayroll(roster) <= before)
                failures.Add("HiringMovesThePayroll: a recruit did not raise the bill.");
        }

        static void BalanceArithmetic(List<string> failures)
        {
            var sheet = new WeekSheet
            {
                Week = 3,
                LegalIncome = 1000,
                IllegalIncome = 2500,
                SalesIncome = 500,
                Bribes = 300,
                Purchases = 750,
                OtherCosts = 50,
            };

            var report = FinanceReport.For(sheet, liveWages: 900, safe: 4200,
                riskyMoney: 0, assets: 1800);

            if (report.TotalIncome != 4000)
                failures.Add($"BalanceArithmetic: income {report.TotalIncome}.");
            if (report.TotalOutgoings != 2000)
                failures.Add($"BalanceArithmetic: outgoings {report.TotalOutgoings}.");
            if (report.Profit != 2000)
                failures.Add($"BalanceArithmetic: profit {report.Profit}.");
            if (report.TaxDue != 600)
                failures.Add($"BalanceArithmetic: tax due {report.TaxDue}.");
            if (report.TotalProfit != 2000)
                failures.Add($"BalanceArithmetic: total profit {report.TotalProfit} " +
                             "(no tax paid yet).");
            if (report.TotalWealth != 6000)
                failures.Add($"BalanceArithmetic: wealth {report.TotalWealth}.");
        }

        static void TaxOnlyOnProfit(List<string> failures)
        {
            if (BalanceMath.TaxDue(-500) != 0)
                failures.Add("TaxOnlyOnProfit: a losing week owed tax.");
            if (BalanceMath.TaxDue(1000) != 1000 * BalanceMath.TaxRatePercent / 100)
                failures.Add("TaxOnlyOnProfit: the rate is wrong.");
        }

        static void RiskThresholds(List<string> failures)
        {
            if (BalanceMath.RiskFor(0) != RiskRating.None ||
                BalanceMath.RiskFor(BalanceMath.RiskLowCeiling - 1) != RiskRating.Low ||
                BalanceMath.RiskFor(BalanceMath.RiskLowCeiling) != RiskRating.Moderate ||
                BalanceMath.RiskFor(BalanceMath.RiskModerateCeiling) != RiskRating.High)
                failures.Add("RiskThresholds: a boundary lands in the wrong band.");
        }

        static void AssetsAreBookValue(List<string> failures)
        {
            var roster = RosterSeeder.Generate(42);
            // Seeded stock: three $100 pistols and a $1,500 car.
            if (BalanceMath.AssetsOf(roster) != 1800)
                failures.Add($"AssetsAreBookValue: {BalanceMath.AssetsOf(roster)}.");
        }

        static void ReportUsesFrozenWagesWhenClosed(List<string> failures)
        {
            var open = new WeekSheet { Week = 1 };
            var closed = new WeekSheet { Week = 1, Closed = true, WagesPaid = 640 };

            if (FinanceReport.For(open, 555, 0, 0, 0).Wages != 555)
                failures.Add("ReportUsesFrozenWagesWhenClosed: open sheet ignores live wages.");
            if (FinanceReport.For(closed, 555, 0, 0, 0).Wages != 640)
                failures.Add("ReportUsesFrozenWagesWhenClosed: closed sheet re-derives.");
        }

        static void CashFormatsExactly(List<string> failures)
        {
            if (LedgerText.Cash(0) != "$0" ||
                LedgerText.Cash(850) != "$850" ||
                LedgerText.Cash(1247) != "$1,247" ||
                LedgerText.Cash(-300) != "-$300" ||
                LedgerText.Cash(1250000) != "$1,250,000")
                failures.Add("CashFormatsExactly: the exact formatter rounds or misplaces.");
        }
    }
}
