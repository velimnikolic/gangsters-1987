using System;
using System.Collections.Generic;
using System.Linq;
using LivingCity.Business;
using LivingCity.Territory;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>Pure contracts for GAN-294; safe to run without a scene or Play Mode.</summary>
    public static class StorefrontContractTests
    {
        static readonly int[] Seeds = { 1, 7, 87, 1987, 9091 };

        public sealed class Report
        {
            public int Profiles;
            public int HarvestedDoorBays;
            public int Sites;
            public int Seeds;
            public string[] Failures = Array.Empty<string>();
            public bool Passed => Failures.Length == 0;
        }

        public static Report Run()
        {
            var failures = new List<string>();
            var report = new Report { Profiles = StorefrontDoorCatalog.Count };
            Catalog(failures);
            Harvest(failures, report);
            Plans(failures, report);
            report.Failures = failures.ToArray();
            return report;
        }

        static void Catalog(List<string> failures)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < StorefrontDoorCatalog.Count; i++)
            {
                var profile = StorefrontDoorCatalog.At(i);
                if (!names.Add(profile.Module))
                    failures.Add("GAN-294: duplicate module profile " + profile.Module + ".");
                if (profile.Leaves < 0 || profile.Leaves > 2 ||
                    profile.Width < 0f || profile.Height < 0f)
                    failures.Add("GAN-294: invalid profile " + profile.Module + ".");
                if (profile.Leaves == 0 && profile.Module != "SM_Bld_Shop_05")
                    failures.Add("GAN-294: only Shop_05 may be doorless.");
            }
            if (StorefrontDoorCatalog.Count != 8)
                failures.Add("GAN-294: expected 8 source-module profiles.");
        }

        static void Harvest(List<string> failures, Report report)
        {
            bool shop05 = false, shop06 = false, corner01 = false, corner02 = false;
            foreach (var unit in ResidentialUnits.All)
            foreach (var bay in unit.ShopBays ?? Array.Empty<ResidentialShopBay>())
            {
                if (!StorefrontDoorCatalog.TryGet(bay.Module, out var profile)) continue;
                if (bay.Module == "SM_Bld_Shop_05") shop05 = true;
                if (bay.Module == "SM_Bld_Shop_06" && bay.Door.Leaves == 1) shop06 = true;
                if (bay.Module == "SM_Bld_Shop_Corner_01" && bay.Door.Leaves == 2) corner01 = true;
                if (bay.Module == "SM_Bld_Shop_Corner_02" && bay.Door.Leaves == 1) corner02 = true;

                if (bay.Door.Leaves == 0)
                {
                    if (profile.Leaves == 0 || bay.Module == "SM_Bld_Shop_03") continue;
                    failures.Add("GAN-294: unexpected doorless " + bay.Module +
                                 " bay in " + unit.Name + ".");
                    continue;
                }
                report.HarvestedDoorBays++;
                if (bay.Door.Leaves != profile.Leaves ||
                    Mathf.Abs(bay.Door.Width - profile.Width) > 0.01f)
                    failures.Add("GAN-294: harvested leaf profile drift in " + unit.Name + ".");
                float phase = Mathf.Repeat(bay.Door.Yaw - profile.Yaw, 90f);
                if (Mathf.Min(phase, 90f - phase) > 0.05f)
                    failures.Add("GAN-294: non-deterministic door yaw in " + unit.Name + ".");
            }
            if (!shop05 || !shop06 || !corner01 || !corner02)
                failures.Add("GAN-294: harvest lacks Shop_05/06 or first-round corner coverage.");
            if (report.HarvestedDoorBays == 0)
                failures.Add("GAN-294: harvest contains no measured storefront doors.");
        }

        static void Plans(List<string> failures, Report report)
        {
            var model = new ResidentialBlockModel();
            for (int i = 0; i < Seeds.Length; i++)
            {
                int seed = Seeds[i];
                var first = ResidentialBlocks.PlanStorefronts(12, seed);
                var second = ResidentialBlocks.PlanStorefronts(12, seed);
                if (first.Closed != 0 || second.Closed != 0 ||
                    !first.Styles.SequenceEqual(second.Styles))
                    failures.Add("GAN-294: storefront dressing is random/closed at seed " + seed + ".");

                var plan = ResidentialLot.Roll(14, 10, seed, artery: 0);
                if (plan == null)
                {
                    failures.Add("GAN-294: residential seed " + seed + " did not deal.");
                    continue;
                }
                var bounds = new Rect(i * 100f, 0f,
                    plan.W * ResidentialLot.Cell, plan.D * ResidentialLot.Cell);
                model.Add(new ResidentialBlockRecipe(
                    "storefront:test:" + seed, "Storefront " + seed,
                    bounds, plan, seed, i));
                report.Seeds++;
            }

            var firstSites = new List<BusinessSite>(
                new ResidentialBusinessSites(model, DistrictFrame.Identity).Sites());
            var secondSites = new List<BusinessSite>(
                new ResidentialBusinessSites(model, DistrictFrame.Identity).Sites());
            report.Sites = firstSites.Count;
            if (firstSites.Count != secondSites.Count)
                failures.Add("GAN-294: five-seed site counts are not stable.");
            var ids = new HashSet<string>();
            for (int i = 0; i < firstSites.Count; i++)
            {
                var site = firstSites[i];
                if (!ids.Add(site.SiteId.Value))
                    failures.Add("GAN-294: duplicate site " + site.SiteId + ".");
                if (i >= secondSites.Count || site.SiteId != secondSites[i].SiteId)
                    failures.Add("GAN-294: site order/identity drift at row " + i + ".");
                if (site.Role != ResidentialBusinessSites.FrontageRole &&
                    site.Role != ResidentialBusinessSites.ExtraFrontageRole) continue;
                float area = site.Footprint.Width * site.Footprint.Depth;
                bool five = Mathf.Abs(area - 25f) < 0.1f;
                bool ten = Mathf.Abs(area - 50f) < 0.1f;
                if (!five && !ten)
                    failures.Add("GAN-294: binding piece is not 5x5 or joined 10x5 at " +
                                 site.SiteId + " (" + site.Footprint.Width + "x" +
                                 site.Footprint.Depth + ").");
            }
        }
    }
}
