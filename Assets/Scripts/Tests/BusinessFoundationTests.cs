using System.Collections.Generic;
using System.Linq;
using LivingCity.Business;
using LivingCity.Territory;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>
    /// Headless contracts for GAN-154 / BIZ-001 through BIZ-012: the registry's identity and
    /// ownership rules, the site catalogue's ordering and duplicate refusal, the archetype
    /// catalogue's totality, deterministic owners, and a population pass that gives the same
    /// city whatever order the providers ran in.
    ///
    /// Nothing here instantiates a GameObject: the whole point of the layer is that business
    /// truth exists with no view standing anywhere.
    /// </summary>
    public static class BusinessFoundationTests
    {
        const int Seed = 1987;

        public static List<string> Run()
        {
            var failures = new List<string>();

            RegistryOwnsIdentityAndRefusesDuplicates(failures);
            SiteEnumerationIsProviderOrderIndependent(failures);
            ArchetypeCatalogueIsTotal(failures);
            OwnersAreDeterministicAndSurviveTransfer(failures);
            SuccessorsArePureAndMinted(failures);
            PopulationIsDeterministicAndComplete(failures);
            AddingOneSiteLeavesTheRestAlone(failures);
            CompoundsAreOneBusinessWithAGate(failures);
            HarvestedResidentialShopBaysMatchPhysicalFixtures(failures);
            AmenityDoorAvoidsAnotherProgramme(failures);
            ResidentialPlanDataPublishesStableSites(failures);
            failures.AddRange(BusinessShutdownTests.Run());

            return failures;
        }

        // ------------------------------------------------------------------ BIZ-001

        static void RegistryOwnsIdentityAndRefusesDuplicates(List<string> failures)
        {
            var directory = new BusinessDirectory();
            var site = Site("a", "plan-1", "shop-1");
            var owner = directory.RegisterOwner(
                BusinessIdentity.Owner(site.SiteId), BusinessOwnerKind.Individual,
                "Ada Marino", BusinessOwnerAge.Middle, 7);

            var before = directory.Version;
            var record = directory.Register(
                site.SiteId, BusinessArchetypeId.Grocer, "Corner Market", owner.Id,
                BusinessSiteSize.Small, 1200, site.ProviderId);

            if (record == null)
                failures.Add("BIZ-001: a first registration was refused.");
            else if (!directory.TryGet(record.Id, out var back) || back != record)
                failures.Add("BIZ-001: a business cannot be looked up by its own ID.");
            if (!directory.TryGetBySite(site.SiteId, out _))
                failures.Add("BIZ-001: a business cannot be looked up by its site.");
            if (!directory.TryGetOwner(owner.Id, out _))
                failures.Add("BIZ-001: an owner cannot be looked up by its own ID.");
            if (directory.Version <= before)
                failures.Add("BIZ-001: registering a business published no version change.");

            var problemsBefore = directory.Problems.Count;
            var twice = directory.Register(
                site.SiteId, BusinessArchetypeId.Baker, "Sunrise Bakery", owner.Id,
                BusinessSiteSize.Small, 900, "other");
            if (twice != null)
                failures.Add("BIZ-001: the same site was populated twice.");
            if (directory.Problems.Count == problemsBefore)
                failures.Add("BIZ-001: a duplicate registration named no source.");

            // The record must not be able to reach a view. Checked by reflection so the
            // rule survives a later field being added in a hurry.
            foreach (var property in typeof(BusinessRecord).GetProperties())
                if (typeof(Object).IsAssignableFrom(property.PropertyType))
                    failures.Add("BIZ-001: BusinessRecord." + property.Name +
                                 " reaches a UnityEngine.Object - records are simulation truth.");
        }

        // ------------------------------------------------------------------ BIZ-003

        static void SiteEnumerationIsProviderOrderIndependent(List<string> failures)
        {
            var one = new FakeProvider("one",
                Site("one", "plan-b", "g2"), Site("one", "plan-a", "g1"));
            var two = new FakeProvider("two",
                Site("two", "plan-c", "g3"), Site("two", "plan-a", "g9"));

            var forward = new BusinessSiteCatalog();
            forward.Add(one);
            forward.Add(two);
            forward.Build();

            var backward = new BusinessSiteCatalog();
            backward.Add(two);
            backward.Add(one);
            backward.Build();

            if (forward.Sites.Count != backward.Sites.Count)
                failures.Add("BIZ-003: provider registration order changed the site count.");
            else
                for (var i = 0; i < forward.Sites.Count; i++)
                    if (forward.Sites[i].SiteId != backward.Sites[i].SiteId)
                    {
                        failures.Add("BIZ-003: site enumeration depends on provider order at " +
                                     i + " (" + forward.Sites[i].SiteId + " vs " +
                                     backward.Sites[i].SiteId + ").");
                        break;
                    }

            var clash = new BusinessSiteCatalog();
            clash.Add(new FakeProvider("one", Site("shared", "plan-a", "g1")));
            clash.Add(new FakeProvider("two", Site("shared", "plan-a", "g1")));
            clash.Build();
            if (clash.Sites.Count != 1)
                failures.Add("BIZ-003: a duplicate site ID was accepted instead of refused.");
            if (clash.Problems.Count == 0)
                failures.Add("BIZ-003: a duplicate site ID was dropped without naming both providers.");
        }

        // ------------------------------------------------------------------ BIZ-007

        static void ArchetypeCatalogueIsTotal(List<string> failures)
        {
            foreach (BusinessArchetypeId id in System.Enum.GetValues(typeof(BusinessArchetypeId)))
                if (!BusinessArchetypes.TryGet(id, out _))
                    failures.Add("BIZ-007: archetype " + id + " has no catalogue entry.");

            if (!BusinessArchetypes.TryFromSignage(BusinessSignage.Nightclub, out var club) ||
                club.Id != BusinessArchetypeId.Nightclub)
                failures.Add("BIZ-007: the nightclub sign does not resolve to the nightclub.");
            if (BusinessArchetypes.TryFromSignage("brothel", out _))
                failures.Add("BIZ-007: an unknown sign fell back instead of failing.");

            // A signed archetype must never be reachable by a random roll.
            foreach (var entry in BusinessArchetypes.All)
                if (entry.GenericWeight > 0 && entry.Signage != BusinessSignage.None)
                    failures.Add("BIZ-007: " + entry.Id +
                                 " is both signed and rollable - a sign would mean nothing.");

            var rollable = 0;
            foreach (var entry in BusinessArchetypes.All)
                if (entry.GenericWeight > 0 && entry.Accepts(BusinessSiteSize.Small))
                    rollable++;
            if (rollable < 5)
                failures.Add("BIZ-007: fewer than five trades can stand in a small shop.");

            if (BusinessArchetypes.RollGeneric(BusinessSiteSize.Small, new System.Random(3)) == null)
                failures.Add("BIZ-007: a small generic site rolled no trade at all.");
            if (BusinessArchetypes.RollGeneric(BusinessSiteSize.Compound, new System.Random(3)) != null)
                failures.Add("BIZ-007: a compound rolled an ordinary shop - only a sign puts a " +
                             "business on a compound.");

            var pub = BusinessArchetypes.Get(BusinessArchetypeId.Pub);
            if (BusinessArchetypes.Name(pub, new System.Random(11)) !=
                BusinessArchetypes.Name(pub, new System.Random(11)))
                failures.Add("BIZ-007: the same seed named the same pub twice differently.");
        }

        // ------------------------------------------------------------------ BIZ-008

        static void OwnersAreDeterministicAndSurviveTransfer(List<string> failures)
        {
            var site = Site(BusinessProviders.Residential, "core:1987:0:1:2:3:4:res", "spot:0:x:face:0");
            var grocer = BusinessArchetypes.Get(BusinessArchetypeId.Grocer);

            var first = new BusinessDirectory();
            var second = new BusinessDirectory();
            var a = BusinessOwners.ForSite(first, site, grocer, Seed, new HashSet<string>());
            var b = BusinessOwners.ForSite(second, site, grocer, Seed, new HashSet<string>());

            if (a.Id != b.Id || a.DisplayName != b.DisplayName || a.Kind != b.Kind ||
                a.PortraitSeed != b.PortraitSeed)
                failures.Add("BIZ-008: the same seed and site produced two different gazde.");
            if (a.Kind != BusinessOwnerKind.Individual)
                failures.Add("BIZ-008: a corner shop was given a company owner.");

            var works = BusinessArchetypes.Get(BusinessArchetypeId.Factory);
            var firm = BusinessOwners.ForSite(
                new BusinessDirectory(), Site(BusinessProviders.Compound, "p", "works"),
                works, Seed, new HashSet<string>());
            if (firm.Kind != BusinessOwnerKind.Company)
                failures.Add("BIZ-008: a factory was given a private proprietor.");

            var port = BusinessArchetypes.Get(BusinessArchetypeId.PortCompany);
            var directory = new BusinessDirectory();
            var one = BusinessOwners.ForSite(
                directory, Site(BusinessProviders.Compound, "harbor:1", "company"), port, Seed,
                new HashSet<string>());
            var other = BusinessOwners.ForSite(
                directory, Site(BusinessProviders.Compound, "harbor:2", "company"), port, Seed,
                new HashSet<string>());
            if (one.Id != other.Id)
                failures.Add("BIZ-008: two port sites minted two harbour companies.");

            // Names stay unique while the draw has luck, and a collision is resolved rather
            // than repeated.
            var taken = new HashSet<string>();
            var names = new HashSet<string>();
            var directory2 = new BusinessDirectory();
            for (var i = 0; i < 60; i++)
            {
                var owner = BusinessOwners.ForSite(
                    directory2, Site(BusinessProviders.Residential, "plan", "shop-" + i),
                    grocer, Seed, taken);
                if (!names.Add(owner.DisplayName))
                    failures.Add("BIZ-008: two deeds carry the same name (" +
                                 owner.DisplayName + ").");
            }

            // A transfer moves the deed and leaves the identity alone.
            var transfer = new BusinessDirectory();
            var seller = BusinessOwners.ForSite(transfer, site, grocer, Seed, new HashSet<string>());
            var record = transfer.Register(
                site.SiteId, BusinessArchetypeId.Grocer, "Corner Market", seller.Id,
                BusinessSiteSize.Small, 1000, site.ProviderId);
            var buyer = transfer.RegisterOwner(
                BusinessIdentity.SharedOwner("buyer"), BusinessOwnerKind.Company,
                "Meridian Holdings", BusinessOwnerAge.None, 1);
            var idBefore = record.Id;
            var siteBefore = record.SiteId;
            if (!transfer.SetOwner(record.Id, buyer.Id))
                failures.Add("BIZ-008: a business could not be sold on.");
            if (record.Id != idBefore || record.SiteId != siteBefore)
                failures.Add("BIZ-008: selling a business changed its identity.");
            if (record.OwnerId != buyer.Id)
                failures.Add("BIZ-008: the deed did not move to the buyer.");
        }

        static void SuccessorsArePureAndMinted(List<string> failures)
        {
            var site = Site(BusinessProviders.Residential, "successor", "counter");
            var businessId = BusinessIdentity.Business(site.SiteId);
            var oldProfile = TerritoryOwnerProfile.Deal(Seed, businessId);
            var generationZero = TerritoryOwnerProfile.Deal(Seed, businessId, 0);
            if (oldProfile.Trait != generationZero.Trait ||
                oldProfile.Nerve != generationZero.Nerve ||
                oldProfile.Greed != generationZero.Greed ||
                oldProfile.Connections != generationZero.Connections)
                failures.Add("EMPT-003: generation zero changed the existing owner's deal.");

            var successorProfile = TerritoryOwnerProfile.Deal(Seed, businessId, 1);
            if (successorProfile.Trait == oldProfile.Trait &&
                successorProfile.Nerve == oldProfile.Nerve)
                failures.Add("EMPT-003: a successor kept both the dead owner's trait and nerve.");

            var forwardName = BusinessOwners.SuccessorName(site, Seed, 1);
            // Ask unrelated generations between the two reads: no taken-name set or draw
            // order is allowed to leak into the answer.
            BusinessOwners.SuccessorName(
                Site(BusinessProviders.Residential, "successor", "other"), Seed, 3);
            var reverseName = BusinessOwners.SuccessorName(site, Seed, 1);
            if (string.IsNullOrEmpty(forwardName) || forwardName != reverseName)
                failures.Add("EMPT-003: the successor's name depends on replay order.");

            var directory = new BusinessDirectory();
            var original = BusinessOwners.ForSite(
                directory, site, BusinessArchetypes.Get(BusinessArchetypeId.Grocer),
                Seed, new HashSet<string>());
            var record = directory.Register(
                site.SiteId, BusinessArchetypeId.Grocer, "Successor Shop", original.Id,
                BusinessSiteSize.Small, 1_200, site.ProviderId);
            var invalidated = default(TerritoryBusinessId);
            var successor = BusinessSuccession.Replace(
                directory, site, record.Id, Seed, 1, id => invalidated = id);
            if (successor == null ||
                successor.Id != BusinessIdentity.Owner(site.SiteId, 1) ||
                record.OwnerId != successor.Id || invalidated != record.Id)
                failures.Add("EMPT-003: replacing the owner did not mint, set and invalidate through one seam.");
        }

        // ------------------------------------------------------------------ BIZ-009/010

        static void PopulationIsDeterministicAndComplete(List<string> failures)
        {
            var forward = Populate(false, out var forwardReport);
            var backward = Populate(true, out var backwardReport);

            if (forwardReport.Populated != backwardReport.Populated)
                failures.Add("BIZ-009: provider order changed how many businesses the city has.");

            var forwardIds = forward.BusinessIds;
            var backwardIds = backward.BusinessIds;
            if (forwardIds.Count != backwardIds.Count)
                failures.Add("BIZ-009: two passes over the same plan produced different cities.");
            else
                for (var i = 0; i < forwardIds.Count; i++)
                {
                    forward.TryGet(forwardIds[i], out var a);
                    backward.TryGet(backwardIds[i], out var b);
                    if (a == null || b == null || a.Id != b.Id || a.DisplayName != b.DisplayName ||
                        a.Archetype != b.Archetype || a.OwnerId != b.OwnerId ||
                        a.EstimatedWeeklyTurnover != b.EstimatedWeeklyTurnover)
                    {
                        failures.Add("BIZ-009: business " + i + " differs between two passes.");
                        break;
                    }
                }

            // Every eligible site, exactly one business; the excluded one reported.
            if (forwardReport.Populated != forwardReport.Eligible)
                failures.Add("BIZ-009: " + forwardReport.Eligible + " eligible sites produced " +
                             forwardReport.Populated + " businesses.");
            if (forwardReport.Unsupported.Count != 1)
                failures.Add("BIZ-009: the ineligible site was not reported as unsupported.");

            // A signed site never rolls into something else.
            foreach (var id in forwardIds)
            {
                forward.TryGet(id, out var record);
                if (record != null && record.SiteId.Value.EndsWith("|club") &&
                    record.Archetype != BusinessArchetypeId.Nightclub)
                    failures.Add("BIZ-009: the nightclub site became " + record.Archetype + ".");
            }

            // BIZ-010: binding and unbinding a view must not move the directory at all.
            var version = forward.Version;
            BusinessViewBindings.Bind(forwardIds[0], null);
            BusinessViewBindings.Unbind(forwardIds[0], null);
            if (forward.Version != version)
                failures.Add("BIZ-010: view binding changed the directory version.");
        }

        static void AddingOneSiteLeavesTheRestAlone(List<string> failures)
        {
            var plain = new BusinessSiteCatalog();
            plain.Add(new FakeProvider("shop", ShopSites()));
            plain.Build();
            var before = new BusinessDirectory();
            BusinessPopulation.Populate(plain, Seed, before);

            var catalog = new BusinessSiteCatalog();
            catalog.Add(new FakeProvider("shop", ShopSites()));
            catalog.Add(new FakeProvider("late", Site("late", "plan-z", "extra")));
            catalog.Build();
            var after = new BusinessDirectory();
            BusinessPopulation.Populate(catalog, Seed, after);

            foreach (var id in before.BusinessIds)
            {
                before.TryGet(id, out var was);
                if (!after.TryGet(id, out var now))
                {
                    failures.Add("BIZ-009: adding a site lost business " + id + ".");
                    break;
                }

                // Identity, trade and size, not the sign over the door: two shops that
                // roll the same name are numbered in catalogue order, so a new site
                // between them may turn a "Corner Market" into a "Corner Market II".
                // Nothing an existing business IS depends on the new one.
                if (was.Archetype != now.Archetype || was.SiteId != now.SiteId ||
                    was.EstimatedWeeklyTurnover != now.EstimatedWeeklyTurnover)
                {
                    failures.Add("BIZ-009: adding one site rerolled " + id + ".");
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ BIZ-006

        static void CompoundsAreOneBusinessWithAGate(List<string> failures)
        {
            // Six halls of one works: the provider publishes the compound, not the halls.
            var footprint = new TerritoryBounds(100f, 200f, 80f, 60f);
            var gate = new TerritoryPoint(140f, 200f);
            var compound = new BusinessSite(
                BusinessProviders.Compound, "works-plan", "compound:1", footprint, gate,
                new TerritoryPoint(0f, -1f), BusinessSignage.Factory, BusinessSiteSize.Compound,
                default, 4, "Union Works", "yard", 0);

            var catalog = new BusinessSiteCatalog();
            catalog.Add(new FakeProvider(BusinessProviders.Compound, compound));
            catalog.Build();
            if (catalog.Sites.Count != 1)
                failures.Add("BIZ-006: a compound published more than one site.");

            var gateInside =
                gate.X >= footprint.XMin - 0.01f && gate.X <= footprint.XMin + footprint.Width + 0.01f &&
                gate.Z >= footprint.ZMin - 0.01f && gate.Z <= footprint.ZMin + footprint.Depth + 0.01f;
            if (!gateInside)
                failures.Add("BIZ-006: the compound's gate falls outside its own footprint.");

            var directory = new BusinessDirectory();
            BusinessPopulation.Populate(catalog, Seed, directory);
            if (directory.BusinessIds.Count != 1)
                failures.Add("BIZ-006: a compound was populated with more than one firm.");
            else
            {
                directory.TryGet(directory.BusinessIds[0], out var record);
                if (record.Archetype != BusinessArchetypeId.Factory)
                    failures.Add("BIZ-006: a signed works became " + record.Archetype + ".");
            }
        }

        // ------------------------------------------------------------------ BIZ-004

        /// <summary>
        /// The three fixtures cover the original misses directly: a wide row with adjacent
        /// premises, a shop that exists only on an inward face, and a building containing
        /// genuine corner shops plus unrelated straight shops. Counts come from the source
        /// modules and deliberately fail if a later table refresh collapses them again.
        /// </summary>
        static void HarvestedResidentialShopBaysMatchPhysicalFixtures(List<string> failures)
        {
            ResidentialUnit row = null, inward = null, mixed = null;
            foreach (var unit in ResidentialUnits.All)
            {
                if (unit.Name == "residential-02") row = unit;
                else if (unit.Name == "residential-04") inward = unit;
                else if (unit.Name == "residential-10") mixed = unit;

                var seen = new HashSet<string>();
                var bays = unit.ShopBays;
                if (bays == null)
                    continue;
                foreach (var bay in bays)
                {
                    var key = bay.Side + ":" + Mathf.RoundToInt(bay.X * 100f) + ":" +
                              Mathf.RoundToInt(bay.Z * 100f);
                    if (!seen.Add(key))
                        failures.Add("BIZ-004: duplicate physical shop bay " +
                                     unit.Name + ":" + key + ".");
                    if (bay.Side < 0 || bay.Side > 3 || bay.X < -0.01f || bay.Z < -0.01f ||
                        bay.X > unit.CW * ResidentialLot.Cell + 0.01f ||
                        bay.Z > unit.CD * ResidentialLot.Cell + 0.01f)
                        failures.Add("BIZ-004: physical shop bay lies outside " +
                                     unit.Name + ".");
                }
            }

            if (row?.ShopBays == null || row.ShopBays.Length != 18)
                failures.Add("BIZ-004: residential-02 no longer exposes its 18 physical bays.");
            if (inward?.ShopBays == null || inward.ShopBays.Length != 1 ||
                inward.Shops[0] + inward.Shops[1] + inward.Shops[2] + inward.Shops[3] != 0)
                failures.Add("BIZ-004: residential-04 inward-only shop was lost.");
            if (mixed?.ShopBays == null || mixed.ShopBays.Length != 7)
                failures.Add("BIZ-004: residential-10 corner/straight shops were merged.");
        }

        /// <summary>A mixed-block amenity may have no authored AccessSide. Its business
        /// door must then use a real facade connected to pavement, not blindly inherit
        /// the block artery through a cafe patio.</summary>
        static void AmenityDoorAvoidsAnotherProgramme(List<string> failures)
        {
            ResidentialUnit gym = null;
            foreach (var unit in ResidentialUnits.All)
                if (unit.Name == "gym")
                {
                    gym = unit;
                    break;
                }
            if (gym == null)
            {
                failures.Add("BIZ-004: the gym fixture is missing.");
                return;
            }

            var plan = new ResidentialLot.Plan
            {
                W = 15,
                D = 15,
                Artery = 1,
                Ground = new ResidentialLot.Use[15, 15],
            };
            for (var side = 0; side < 4; side++)
                plan.Street[side] = true;
            for (var i = 0; i < plan.W; i++)
                for (var j = 0; j < plan.D; j++)
                    plan.Ground[i, j] =
                        i < ResidentialLot.Walk || j < ResidentialLot.Walk ||
                        i >= plan.W - ResidentialLot.Walk ||
                        j >= plan.D - ResidentialLot.Walk
                            ? ResidentialLot.Use.Walkway
                            : ResidentialLot.Use.Paved;

            var turn = ResidentialLot.Turn.Of(gym, 0);
            var spot = new ResidentialLot.Spot
            {
                Unit = gym,
                Yaw = 0,
                I = 6,
                J = 6,
                CW = turn.CW,
                CD = turn.CD,
            };
            var middleX = spot.I + (spot.CW - 1) / 2;
            var middleZ = spot.J + (spot.CD - 1) / 2;
            for (var i = spot.I + spot.CW; i < plan.W - ResidentialLot.Walk; i++)
                plan.Ground[i, middleZ] = ResidentialLot.Use.Cafe;
            for (var j = spot.J - 1; j >= ResidentialLot.Walk; j--)
                plan.Ground[middleX, j] = ResidentialLot.Use.Cafe;

            var sidePicked = BusinessCitySources.AmenityApproachSide(plan, spot);
            if (sidePicked != 3)
                failures.Add("BIZ-004: the gym door still points through the cafe programme.");
        }

        /// <summary>
        /// Over REAL residential plan data, dealt the way CoreDistrict deals it. Nothing is
        /// composed: the provider must answer with no view standing anywhere, which is the
        /// whole contract.
        /// </summary>
        static void ResidentialPlanDataPublishesStableSites(List<string> failures)
        {
            var model = new ResidentialBlockModel();
            var built = 0;
            for (var i = 0; i < 8 && built < 6; i++)
            {
                var dice = unchecked(Seed * 7919 + i * 104729);
                var plan = ResidentialLot.Roll(14, 10, dice, 0);
                if (plan == null)
                    continue;
                var bounds = new Rect(i * 100f, 0f, 14f * ResidentialLot.Cell,
                    10f * ResidentialLot.Cell);
                model.Add(new ResidentialBlockRecipe(
                    "test:block:" + i, "Test Block " + i, bounds, plan, dice, i));
                built++;
            }

            if (built == 0)
            {
                failures.Add("BIZ-004: no residential plan could be dealt for the test.");
                return;
            }

            var provider = new ResidentialBusinessSites(model, DistrictFrame.Identity);
            var first = new List<BusinessSite>(provider.Sites());
            var again = new List<BusinessSite>(
                new ResidentialBusinessSites(model, DistrictFrame.Identity).Sites());

            if (first.Count != again.Count)
                failures.Add("BIZ-004: two reads of the same recipes published different sites.");
            else
                for (var i = 0; i < first.Count; i++)
                    if (first[i].SiteId != again[i].SiteId)
                    {
                        failures.Add("BIZ-004: site IDs are not stable across reads.");
                        break;
                    }

            var seen = new HashSet<string>();
            foreach (var site in first)
                if (!seen.Add(site.SiteId.Value))
                    failures.Add("BIZ-004: duplicate site " + site.SiteId + ".");

            // Every doored premise is a site, on every side of the building. Doorless
            // Shop_05 and the display half of wide Shop_03 join their nearest doored bay.
            var runs = new List<(int At, int Len)>();
            foreach (var recipe in model.Blocks)
            {
                var plan = recipe.Plan;
                for (var index = 0; index < plan.Spots.Count; index++)
                {
                    var spot = plan.Spots[index];
                    if (spot?.Unit == null)
                        continue;

                    var expected = 0;
                    if (spot.Unit.Kind == ResidentialKind.Storefront)
                    {
                        expected = 1;
                    }
                    else if (ResidentialUnits.IsLot(spot.Unit))
                    {
                        expected = 0;
                    }
                    else if (spot.Unit.ShopBays != null &&
                             spot.Unit.ShopBays.Length > 0)
                    {
                        expected = spot.Unit.ShopBays.Count(bay => bay.Door.Leaves > 0);
                    }
                    else if (spot.Shop)
                    {
                        var turn = ResidentialLot.Turn.Of(spot.Unit, spot.Yaw);
                        for (var side = 0; side < 4; side++)
                        {
                            turn.ShopRuns(side, runs);
                            for (var run = 0; run < runs.Count; run++)
                                expected += runs[run].Len;
                        }
                    }

                    var actual = 0;
                    var primary = 0;
                    foreach (var site in first)
                        if (site.SourcePlanId == recipe.Id &&
                            site.GroupKey.StartsWith("spot:" + index + ":"))
                        {
                            actual++;
                            if (site.Role == ResidentialBusinessSites.FrontageRole)
                                primary++;
                        }
                    if (actual != expected)
                        failures.Add("BIZ-004: " + spot.Unit.Name + " on " + recipe.Id +
                                     " has " + expected + " physical shop bay(s), but " +
                                     actual + " business site(s).");
                    var expectedPrimary = spot.Shop && expected > 0 ? 1 : 0;
                    if (spot.Unit.Kind != ResidentialKind.Storefront &&
                        primary != expectedPrimary)
                        failures.Add("BIZ-004: " + spot.Unit.Name + " on " + recipe.Id +
                                     " published " + primary +
                                     " primary outfit frontage(s), expected " +
                                     expectedPrimary + ".");
                }
            }

            // Every published site must carry a doorstep inside or on its own footprint.
            foreach (var site in first)
            {
                var f = site.Footprint;
                if (site.Approach.X < f.XMin - 1.01f || site.Approach.X > f.XMin + f.Width + 1.01f ||
                    site.Approach.Z < f.ZMin - 1.01f || site.Approach.Z > f.ZMin + f.Depth + 1.01f)
                {
                    failures.Add("BIZ-004: " + site.SiteId + " has doorstep " +
                                 site.Approach.X + "," + site.Approach.Z +
                                 " off footprint " + f.XMin + "," + f.ZMin + " + " +
                                 f.Width + "x" + f.Depth + ".");
                    break;
                }

                if ((site.Role == ResidentialBusinessSites.FrontageRole ||
                     site.Role == ResidentialBusinessSites.ExtraFrontageRole) &&
                    !((Mathf.Abs(f.Width - ResidentialLot.Cell) <= 0.01f &&
                       Mathf.Abs(f.Depth - ResidentialLot.Cell) <= 0.01f) ||
                      (Mathf.Abs(f.Width - ResidentialLot.Cell * 2f) <= 0.01f &&
                       Mathf.Abs(f.Depth - ResidentialLot.Cell) <= 0.01f) ||
                      (Mathf.Abs(f.Width - ResidentialLot.Cell) <= 0.01f &&
                       Mathf.Abs(f.Depth - ResidentialLot.Cell * 2f) <= 0.01f)))
                {
                    failures.Add("BIZ-004: storefront binding piece " + site.SiteId + " is " +
                                 f.Width + "x" + f.Depth + " m instead of " +
                                 "5x5 or joined 10x5 m.");
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        static BusinessDirectory Populate(bool reversed, out BusinessPopulationReport report)
        {
            var shops = new FakeProvider("shop", ShopSites());
            var venues = new FakeProvider("venue",
                new BusinessSite("venue", "plan-n", "club",
                    new TerritoryBounds(0f, 0f, 40f, 30f), new TerritoryPoint(20f, 0f),
                    new TerritoryPoint(0f, -1f), BusinessSignage.Nightclub,
                    BusinessSiteSize.Large, default, 3, "Nightclub block", "venue", 0),
                new BusinessSite("venue", "plan-n", "police",
                    new TerritoryBounds(0f, 0f, 40f, 30f), new TerritoryPoint(20f, 0f),
                    new TerritoryPoint(0f, -1f), BusinessSignage.None,
                    BusinessSiteSize.Large, default, 3, "Police station", "venue", 1,
                    eligible: false, exclusionReason: "civic"));

            var catalog = new BusinessSiteCatalog();
            if (reversed)
            {
                catalog.Add(venues);
                catalog.Add(shops);
            }
            else
            {
                catalog.Add(shops);
                catalog.Add(venues);
            }

            catalog.Build();
            var directory = new BusinessDirectory();
            report = BusinessPopulation.Populate(catalog, Seed, directory);
            return directory;
        }

        static BusinessSite[] ShopSites()
        {
            var sites = new BusinessSite[12];
            for (var i = 0; i < sites.Length; i++)
                sites[i] = new BusinessSite(
                    "shop", "plan-" + (i % 3), "shop-" + i,
                    new TerritoryBounds(i * 20f, 0f, 12f, 10f),
                    new TerritoryPoint(i * 20f + 6f, 0f), new TerritoryPoint(0f, -1f),
                    BusinessSignage.None, BusinessSiteSize.Small, default, i % 3,
                    "Shop " + i, "frontage", i);
            return sites;
        }

        static BusinessSite Site(string provider, string plan, string group) =>
            new BusinessSite(
                provider, plan, group, new TerritoryBounds(0f, 0f, 10f, 10f),
                new TerritoryPoint(5f, 0f), new TerritoryPoint(0f, -1f),
                BusinessSignage.None, BusinessSiteSize.Small, default, 0,
                plan + "/" + group, "frontage", 0);

        sealed class FakeProvider : IBusinessSiteProvider
        {
            readonly BusinessSite[] sites;

            public FakeProvider(string providerId, params BusinessSite[] sites)
            {
                ProviderId = providerId;
                this.sites = sites;
            }

            public string ProviderId { get; }
            public IEnumerable<BusinessSite> Sites() => sites;
        }
    }
}
