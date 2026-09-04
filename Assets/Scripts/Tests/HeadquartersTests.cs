using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>Pure contracts for GAN-263: one safe, one HQ report, one armory gate.</summary>
    public static class HeadquartersTests
    {
        static readonly TerritoryBlockId Headquarters =
            new TerritoryBlockId("core:test:hq");
        static readonly TerritoryBlockId Kearny =
            new TerritoryBlockId("core:test:kearny");
        static readonly TerritoryBlockId Harbor =
            new TerritoryBlockId("core:test:harbor");

        public static List<string> Run()
        {
            var failures = new List<string>();
            MoneyKeepsOneSafeAndSpendsDirtyFirst(failures);
            ADescriptionIsNotAStreetAddress(failures);
            UnfundedOrdersDoNotResolve(failures);
            ReportCountsThePremisesOnce(failures);
            HeadquartersCanBeRepointedAndCleared(failures);
            AllFourTransfersUseThePhysicalGate(failures);
            return failures;
        }

        static void ADescriptionIsNotAStreetAddress(List<string> failures)
        {
            var recruit = new Job { TargetLabel = "a corner prospect" };
            if (recruit.HasPlace)
                failures.Add("Orders: a descriptive label was treated as world origin.");

            recruit.TargetBlockId = 0;
            if (!recruit.HasPlace)
                failures.Add("Orders: block zero was not treated as a real street address.");
        }

        static void MoneyKeepsOneSafeAndSpendsDirtyFirst(List<string> failures)
        {
            var accounts = new Accounts { Safe = 1_000, RiskyMoney = 400 };
            accounts.Open(1);
            var refusal = BalanceMath.TryPurchase(accounts, 250, out var dirtyPart);
            if (refusal != null || accounts.Safe != 750 || accounts.RiskyMoney != 150 ||
                dirtyPart != 250 || accounts.Current.Purchases != 250)
                failures.Add("Money: a purchase did not spend dirty cash first.");

            BalanceMath.RefundPurchase(accounts, 250, dirtyPart);
            if (accounts.Safe != 1_000 || accounts.RiskyMoney != 400 ||
                accounts.Current.Purchases != 0)
                failures.Add("Money: rollback did not restore the payment's composition.");

            BalanceMath.Receive(accounts, 100, MoneyKind.Dirty);
            BalanceMath.Receive(accounts, 100, MoneyKind.Clean);
            if (accounts.Safe != 1_200 || accounts.RiskyMoney != 500 ||
                BalanceMath.CleanOf(accounts) != 700)
                failures.Add("Money: clean and dirty receipts became separate balances.");

            var seized = BalanceMath.Seize(accounts);
            if (seized != 500 || accounts.Safe != 700 || accounts.RiskyMoney != 0)
                failures.Add("Money: a raid did not take exactly the dirty pile.");

            var before = accounts.Safe;
            if (BalanceMath.Pay(accounts, 701, out _) == null || accounts.Safe != before)
                failures.Add("Money: a refused payment moved the safe.");
            AssertInvariant(accounts, "Money", failures);
        }

        static void UnfundedOrdersDoNotResolve(List<string> failures)
        {
            var roster = Roster.Create(0);
            var lieutenant = Man(roster, Rank.Lieutenant, "James", "Byrne");
            var hood = Man(roster, Rank.Hood, "Frank", "Moran");
            var crew = new Crew { Id = roster.NextCrewId(), LieutenantId = lieutenant.Id };
            crew.HoodIds.Add(hood.Id);
            roster.Crews.Add(crew);

            var runner = new CampaignRunner { Seed = 263, DistanceOf = _ => 0f };
            runner.Accounts.Safe = 0;
            runner.OpenFirstSheet();
            var membersBefore = roster.Members.Count;
            var cityOutcome = OrderOutcome.Completed;
            runner.JobResolved += (_, outcome) => cityOutcome = outcome;

            var job = new Job
            {
                CrewId = crew.Id,
                Type = OrderType.Recruit,
                Men = 1,
                TargetLabel = "a corner prospect",
            };
            if (!runner.Issue(roster, job).Ok)
                failures.Add("Money: an underfunded attempt was refused before it could queue.");
            runner.AdvanceHours(roster, 200f);

            if (roster.Members.Count != membersBefore || runner.Accounts.Safe != 0 ||
                runner.Accounts.Current.Bribes != 0)
                failures.Add("Money: an unfunded Recruit changed personnel or the books.");
            if (runner.Records.Count != 1 ||
                runner.Records[0].Outcome != OrderOutcome.Failed ||
                cityOutcome != OrderOutcome.Failed)
                failures.Add("Money: an unfunded order was not reported as Failed.");

            // The lower bookkeeping seam must be atomic as well: a payout cannot fund
            // the cost of the very attempt which supposedly earned it.
            var direct = new CampaignRunner();
            direct.Accounts.Safe = 0;
            direct.OpenFirstSheet();
            var donation = OrderTable.SpecOf(OrderType.Donate);
            if (direct.BookMoney(donation, 1_000, donation.Cost) ||
                direct.Accounts.Safe != 0 || direct.Accounts.Current.Bribes != 0)
                failures.Add("Money: BookMoney let an unfunded job pay for itself.");

            // A purchase reserves the asking price before the roll but a failed sale
            // returns the same clean/dirty notes and never reaches the Purchases line.
            var buyer = new CampaignRunner { Seed = 263, DistanceOf = _ => 0f };
            buyer.Accounts.Safe = 5_000;
            buyer.Accounts.RiskyMoney = 2_000;
            buyer.OpenFirstSheet();
            var purchase = new Job
            {
                CrewId = crew.Id,
                Type = OrderType.BuyPremises,
                Men = 1,
                TargetWorth = 4_000,
                TargetLabel = "a rival front",
                StreetOutcome = OrderOutcome.Failed,
            };
            if (!buyer.Issue(roster, purchase).Ok)
                failures.Add("Money: an affordable purchase fixture was refused.");
            buyer.AdvanceHours(roster, 200f);
            if (buyer.Accounts.Safe != 5_000 || buyer.Accounts.RiskyMoney != 2_000 ||
                buyer.Accounts.Current.Purchases != 0)
                failures.Add("Money: a failed purchase did not refund its payment exactly.");
        }

        static void ReportCountsThePremisesOnce(List<string> failures)
        {
            var roster = Roster.Create(0);
            var boss = Man(roster, Rank.Boss, "Michael", "Sullivan");
            roster.Organization.BossId = boss.Id;
            var manager = Man(roster, Rank.Hood, "Patrick", "Doyle");
            roster.FrontId = manager.Id;
            Man(roster, Rank.Hood, "Sean", "Kelly");
            Man(roster, Rank.Hood, "Thomas", "Ryan");

            Item(roster, EquipmentKind.Pistol, RosterEquipment.Unheld,
                RosterEquipment.Unheld);
            Item(roster, EquipmentKind.Pistol, RosterEquipment.FrontArmory,
                RosterEquipment.FrontArmory);
            Item(roster, EquipmentKind.Pistol, RosterEquipment.FrontArmory, manager.Id);
            Item(roster, EquipmentKind.Shotgun, RosterEquipment.FrontArmory,
                RosterEquipment.FrontArmory);
            Item(roster, EquipmentKind.Grenade, RosterEquipment.Unheld,
                RosterEquipment.Unheld);
            Item(roster, EquipmentKind.Grenade, RosterEquipment.FrontArmory,
                RosterEquipment.FrontArmory);
            Item(roster, EquipmentKind.Vehicle, RosterEquipment.Unheld,
                RosterEquipment.Unheld);
            Item(roster, EquipmentKind.Vehicle, RosterEquipment.FrontArmory,
                RosterEquipment.FrontArmory);
            Item(roster, EquipmentKind.Motorcycle, RosterEquipment.Unheld,
                RosterEquipment.Unheld);

            var accounts = new Accounts { Safe = 9_000, RiskyMoney = 3_000 };
            var inside = new[] { new InsideCrew(55, "Edward Byrne", 5) };
            var report = HeadquartersReport.For(accounts, roster, inside);

            if (report.Safe != 9_000 || report.Dirty != 3_000 || report.Clean != 6_000 ||
                report.DeskManager != manager.FullName || report.Guards != 2)
                failures.Add("Report: cash, desk or guards do not match the roster.");
            if (HeadquartersText.Armory(report) != "2 pistols · 1 shotgun · 2 grenades" ||
                HeadquartersText.InHands(report) != "1 pistol" ||
                HeadquartersText.Vehicles(report) != "2 cars · 1 motorcycle" ||
                HeadquartersText.Inside(report) != "Byrne's crew, 5 men")
                failures.Add("Report: headquarters summaries do not match the stock split.");
        }

        static void HeadquartersCanBeRepointedAndCleared(List<string> failures)
        {
            var sites = new ArmorySites();
            sites.SetHeadquarters(Headquarters);
            sites.Add(Harbor);
            sites.SetHeadquarters(Kearny);

            if (sites.Headquarters != Kearny || sites.Contains(Headquarters) ||
                !sites.Contains(Kearny) || !sites.Contains(Harbor))
                failures.Add("Armory sites: moving HQ left the old front active.");

            if (!sites.ClearHeadquarters() || sites.Headquarters.IsValid ||
                sites.Contains(Kearny) || !sites.Contains(Harbor))
                failures.Add("Armory sites: clearing HQ promoted a secondary armory.");
        }

        static void AllFourTransfersUseThePhysicalGate(List<string> failures)
        {
            var roster = Roster.Create(0);
            var lieutenant = Man(roster, Rank.Lieutenant, "James", "Byrne");
            var hood = Man(roster, Rank.Hood, "Frank", "Moran");
            var crew = new Crew { Id = roster.NextCrewId(), LieutenantId = lieutenant.Id };
            crew.HoodIds.Add(hood.Id);
            roster.Crews.Add(crew);
            var target = Man(roster, Rank.Lieutenant, "Edward", "Burke");
            roster.Crews.Add(new Crew
                { Id = roster.NextCrewId(), LieutenantId = target.Id });
            var item = Item(roster, EquipmentKind.Pistol, lieutenant.Id, lieutenant.Id);
            var sites = new ArmorySites();
            sites.SetHeadquarters(Headquarters);
            var physical = new FakePhysicalSource(lieutenant.Id, Kearny);
            physical.Set(target.Id, Headquarters);

            var off = new[]
            {
                ArmoryGate.Give(roster, physical, sites, hood.Id),
                ArmoryGate.Move(roster, physical, sites, item.Id, target.Id),
                ArmoryGate.Return(roster, physical, sites, item.Id),
                ArmoryGate.GiveToFront(roster, physical, sites, item.Id),
            };
            for (var i = 0; i < off.Length; i++)
                if (off[i].Allowed || !off[i].Located || off[i].BlockId != Kearny)
                    failures.Add("Armory gate: transfer " + i + " ignored the away crew.");

            // The item cannot be moved to a remote receiving crew either, even when
            // its current owner is standing at the armory.
            physical.Set(lieutenant.Id, Headquarters);
            physical.Set(target.Id, Kearny);
            var remoteTarget = ArmoryGate.Move(
                roster, physical, sites, item.Id, target.Id);
            if (remoteTarget.Allowed || !remoteTarget.Located ||
                remoteTarget.BlockId != Kearny)
                failures.Add("Armory gate: Move ignored the away receiving crew.");

            physical.Set(target.Id, Headquarters);
            var at = new[]
            {
                ArmoryGate.Give(roster, physical, sites, hood.Id),
                ArmoryGate.Move(roster, physical, sites, item.Id, target.Id),
                ArmoryGate.Return(roster, physical, sites, item.Id),
                ArmoryGate.GiveToFront(roster, physical, sites, item.Id),
            };
            for (var i = 0; i < at.Length; i++)
                if (!at[i].Allowed)
                    failures.Add("Armory gate: transfer " + i + " failed at headquarters.");

            if (!ArmoryGate.Give(roster, null, sites, hood.Id).Allowed)
                failures.Add("Armory gate: a headless host without a physical source was gated.");
        }

        static Character Man(Roster roster, Rank rank, string first, string surname)
        {
            var man = new Character
            {
                Id = roster.NextCharacterId(), Rank = rank,
                FirstName = first, Surname = surname,
            };
            roster.Members.Add(man);
            return man;
        }

        static RosterEquipment Item(Roster roster, EquipmentKind kind, int owner, int holder)
        {
            var item = new RosterEquipment
            {
                Id = roster.NextEquipmentId(), Kind = kind,
                DisplayName = kind.ToString(), OwnerId = owner, HolderId = holder,
            };
            roster.Equipment.Add(item);
            return item;
        }

        static void AssertInvariant(Accounts accounts, string label, List<string> failures)
        {
            if (accounts.Safe < 0 || accounts.RiskyMoney < 0 ||
                accounts.RiskyMoney > accounts.Safe)
                failures.Add(label + ": 0 <= dirty <= safe was broken.");
        }

        sealed class FakePhysicalSource : IOrganizationPhysicalSource
        {
            readonly Dictionary<int, TerritoryBlockId> blocks =
                new Dictionary<int, TerritoryBlockId>();

            public FakePhysicalSource(int leaderId, TerritoryBlockId block)
            {
                Set(leaderId, block);
            }

            public void Set(int leaderId, TerritoryBlockId block) =>
                blocks[leaderId] = block;

            public void CollectPhysicalMappings(List<TacticalPersonnelMapping> into) { }

            public bool TryLocateGroup(int leader, out TerritoryBlockId blockId)
            {
                return blocks.TryGetValue(leader, out blockId) && blockId.IsValid;
            }
        }
    }
}
