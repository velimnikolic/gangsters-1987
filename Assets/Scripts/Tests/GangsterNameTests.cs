using System;
using System.Collections.Generic;
using LivingCity.Gangs;
using LivingCity.Outfit;
using LivingCity.Personnel;

namespace LivingCity.Tests
{
    /// <summary>Pure model checks: the name describes the dealt man and stays his.</summary>
    public static class GangsterNameTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();
            EverySkillHasNames(failures);
            SkilledDriversGetDrivingNames(failures);
            WeakHandsDoNotGetExpertNames(failures);
            CrowdedNamesStayUnique(failures);
            CreationDoorsUseStreetNames(failures);
            AdHeadlinesAgreeWithNames(failures);
            GrowthAndSaveKeepTheName(failures);
            return failures;
        }

        static Character Driver(int id)
        {
            var man = new Character { Id = id };
            man.SetHalfSteps(CharacterAttribute.Driving, 10);
            return man;
        }

        static void EverySkillHasNames(List<string> failures)
        {
            foreach (CharacterAttribute attribute in Enum.GetValues(typeof(CharacterAttribute)))
            {
                var man = new Character();
                man.SetHalfSteps(attribute, 10);
                var trade = GangsterNames.Assign(new Roster(), man, 7);
                if (trade != attribute || !new HashSet<string>(GangsterNames.NicknamesFor(attribute))
                        .Contains(man.FirstName))
                    failures.Add("EverySkill: missing or incorrect pool for " + attribute);
            }
        }

        static void SkilledDriversGetDrivingNames(List<string> failures)
        {
            var driving = new HashSet<string>
                { "Speedy", "Leadfoot", "Skidmark", "Burnout", "Roadkill", "Tailpipe" };
            var seen = new HashSet<string>();
            for (var seed = 0; seed < 100; seed++)
            {
                var roster = new Roster();
                var man = Driver(0);
                GangsterNames.Assign(roster, man, seed);
                var copy = Driver(0);
                GangsterNames.Assign(roster, copy, seed);
                if (!driving.Contains(man.FirstName) || man.FullName != copy.FullName)
                    failures.Add("SkilledDrivers: wrong trade or unstable identity at " + seed);
                seen.Add(man.FirstName);
            }
            if (seen.Count != driving.Count)
                failures.Add("SkilledDrivers: name variation collapsed.");
        }

        static void WeakHandsDoNotGetExpertNames(List<string> failures)
        {
            var weak = new HashSet<string>
                { "Fuckup", "Dipshit", "Shitshow", "Deadbeat", "Halfwit", "Meathead" };
            var man = new Character();
            for (var a = 0; a < AttributeScale.Count; a++)
                man.SetHalfSteps((CharacterAttribute)a, 5);
            GangsterNames.Assign(new Roster(), man, 7);
            if (!weak.Contains(man.FirstName))
                failures.Add("WeakHands: under three stars received an expert name.");
        }

        static void CrowdedNamesStayUnique(List<string> failures)
        {
            var roster = new Roster();
            var names = new HashSet<string>();
            var surnames = new HashSet<string>(GangsterNames.AllSurnames);
            // Identical seed, identical trade, more people than the 288 basic pairs.
            for (var id = 0; id < 300; id++)
            {
                var man = Driver(id);
                GangsterNames.Assign(roster, man, 3);
                roster.Members.Add(man);
                if (!names.Add(man.FullName))
                    failures.Add("CrowdedNames: duplicate " + man.FullName);
                if (!surnames.Contains(man.Surname))
                    failures.Add("CrowdedNames: collision changed the surname.");
            }
            for (var id = 300; id < 308; id++)
            {
                var man = Driver(id);
                GangsterNames.Assign(roster, man, 3, "Falcone");
                roster.Members.Add(man);
                if (man.Surname != "Falcone" || !names.Add(man.FullName))
                    failures.Add("CrowdedNames: family surname changed or name repeated.");
            }
        }

        static void CreationDoorsUseStreetNames(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(7);
            var names = new HashSet<string>();
            foreach (var man in roster.Members)
            {
                if (!names.Add(man.FullName)) failures.Add("CreationDoors: duplicate name.");
                CheckName(man, failures);
            }
            CheckName(RosterSeeder.Recruit(roster, new Random(9), 10), failures);
            CheckName(RosterSeeder.Deal(roster, new Random(11), 8, 12), failures);
            foreach (var man in RosterSeeder.GenerateLarge(7, 60).Members)
                CheckName(man, failures);
            foreach (var man in RosterSeeder.Generate(7, 1).Members)
                CheckName(man, failures);
            var market = new HireMarket();
            market.EnsureDealt(roster, 7, 1);
            foreach (var ad in market.Ads) CheckName(ad.Man, failures);

            var counsel = HireMarket.CounselFor(roster, 7, 1).Man;
            var lawyerNames = new HashSet<string>
                { "Loophole", "Sleazebag", "Slimy", "Fineprint", "Shark", "Ambulance" };
            if (!lawyerNames.Contains(counsel.FirstName))
                failures.Add("CreationDoors: lawyer named before specialty was assigned.");
        }

        static void CheckName(Character man, List<string> failures)
        {
            if (man.FullName == GangCatalog.BossName) return; // Authored story character.
            if (string.IsNullOrWhiteSpace(man.FirstName) || string.IsNullOrWhiteSpace(man.Surname) ||
                man.FirstName.Length > 10 || man.FullName.Length > 22)
                failures.Add("CreationDoors: missing or oversized name: " + man.FullName);
            // No hidden ceilings are used: flattening them must not change a fresh name.
            var roster = new Roster();
            var check = new Character { Specialty = man.Specialty };
            for (var a = 0; a < AttributeScale.Count; a++)
                check.SetHalfSteps((CharacterAttribute)a, man.GetHalfSteps((CharacterAttribute)a));
            var possible = new HashSet<string>();
            for (var seed = 0; seed < 256; seed++)
            {
                GangsterNames.Assign(roster, check, seed);
                possible.Add(check.FirstName);
            }
            if (!possible.Contains(man.FirstName))
                failures.Add("CreationDoors: name does not describe final skills: " + man.FullName);
        }

        static void AdHeadlinesAgreeWithNames(List<string> failures)
        {
            for (var seed = 0; seed < 8; seed++)
            {
                var roster = RosterSeeder.Generate(seed);
                var market = new HireMarket();
                for (var day = 1; day <= 8; day++)
                {
                    market.EnsureDealt(roster, seed, day);
                    foreach (var ad in market.Ads)
                    {
                        if (ad.Specialty != Specialty.None) continue;
                        var names = new HashSet<string>(GangsterNames.NicknamesFor(ad.Trade));
                        if (!names.Contains(ad.Man.FirstName))
                            failures.Add("AdHeadlines: " + ad.Man.FullName + " advertised as " + ad.Trade);
                        for (var a = 0; a < AttributeScale.Count; a++)
                            if (ad.Man.GetHalfSteps((CharacterAttribute)a) > ad.Man.GetHalfSteps(ad.Trade))
                                failures.Add("AdHeadlines: advertised a weaker skill.");
                    }
                }
            }
        }

        static void GrowthAndSaveKeepTheName(List<string> failures)
        {
            var roster = new Roster();
            var man = Driver(roster.NextCharacterId());
            GangsterNames.Assign(roster, man, 7);
            roster.Members.Add(man);
            string name = man.FullName;
            man.SetHalfSteps(CharacterAttribute.Persuasion, 10);
            man.SetHalfSteps(CharacterAttribute.Driving, 2);
            man.Rank = Rank.Lieutenant;
            var restored = new Roster();
            RosterSnapshot.Restore(restored, RosterSnapshot.Snapshot(roster));
            if (man.FullName != name || restored.Find(man.Id)?.FullName != name)
                failures.Add("GrowthAndSave: a known man was renamed.");
        }
    }
}
