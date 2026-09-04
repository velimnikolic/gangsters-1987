using System;
using System.Collections.Generic;
using LivingCity.Entities;

namespace LivingCity.Business
{
    /// <summary>
    /// Who the deeds name. One gazda per small storefront drawn from the same civilian name
    /// tables the crowd uses - a grocer and a passer-by may share a surname, exactly as two
    /// civilians already can - and a firm behind every gate: the works, the yards, the
    /// harbour. City Hall keeps its own civic deed.
    ///
    /// Determinism is per SITE, never per pass: the stream is
    /// <c>MixSeed(citySeed + SeedOffsets.Business, siteId)</c>, so the same seed and the same
    /// site produce the same owner however many other sites the city happens to have.
    ///
    /// Nothing here is a person on the street. An owner record is a name, an age band and a
    /// portrait seed the ledger's rig can draw from later; no NPC is spawned, and no
    /// personality, courage, fear or compliance value exists yet - those are EPIC 6 and 9.
    /// </summary>
    public static class BusinessOwners
    {
        /// <summary>Chance in a hundred that a proprietor is a woman. It is 1987 and the
        /// deeds skew male, but not absurdly so - PropertyDirector's figure, kept so the
        /// city reads the same either side of this migration.</summary>
        const int FemaleOwnerChance = 45;

        /// <summary>The deed's stream number within a site. The population pass owns
        /// stream 1 (archetype, name, turnover); the deed owns this one, so retuning the
        /// shop tables cannot rename a single gazda.</summary>
        public const int OwnerStream = 2;

        /// <summary>The one firm behind the whole waterfront: clicking any shed means "the
        /// port", so every port site resolves to this deed. The display name matches
        /// PropertyDirector's existing "Harbor Company" for the same reason.</summary>
        public const string HarborCompanyKey = "harbor";

        public const string CityHallKey = "city-hall";

        static readonly string[] CompanySuffixes =
        {
            "& Co.", "Holdings", "Trading Co.", "Industries", "Brothers", "Group",
            "Enterprises", "& Sons",
        };

        static readonly string[] CompanyStems =
        {
            "Marino", "Kowalski", "Delaney", "Okafor", "Vance", "Bruno", "Haddad",
            "Sorrentino", "Petrov", "Nakamura", "Fischer", "Duval", "Alvarez", "Quinn",
            "Harbor", "Riverside", "Meridian", "Union", "Empire", "Atlas", "Beacon",
        };

        /// <summary>
        /// The deed for one site. Shared deeds (the harbour, the city) are returned as they
        /// are; everything else gets its own owner keyed to the site, so a later sale can
        /// move the deed without touching the business or the site identity.
        /// </summary>
        public static BusinessOwnerRecord ForSite(
            BusinessDirectory directory,
            BusinessSite site,
            BusinessArchetype archetype,
            int citySeed,
            HashSet<string> takenNames)
        {
            if (directory == null)
                throw new ArgumentNullException(nameof(directory));
            if (site == null)
                throw new ArgumentNullException(nameof(site));
            if (archetype == null)
                throw new ArgumentNullException(nameof(archetype));

            if (archetype.Id == BusinessArchetypeId.PortCompany)
                return directory.RegisterOwner(
                    BusinessIdentity.SharedOwner(HarborCompanyKey),
                    BusinessOwnerKind.Company, "Harbor Company", BusinessOwnerAge.None,
                    BusinessIdentity.MixSeed(citySeed, BusinessIdentity.StableHash(HarborCompanyKey)));

            var seed = BusinessIdentity.MixSeed(
                citySeed + LivingCity.Generation.SeedOffsets.Business, site.SiteId, OwnerStream);
            var rng = new System.Random(seed);
            var ownerId = BusinessIdentity.Owner(site.SiteId);

            // Draw order per owner is FROZEN: kind first, then the name. Inserting a draw
            // above the name reshuffles every deed in the city.
            if (archetype.CompanyOwned)
            {
                var name = UniqueName(takenNames,
                    () => CompanyStems[rng.Next(CompanyStems.Length)] + " " +
                          CompanySuffixes[rng.Next(CompanySuffixes.Length)],
                    site.SiteId);
                return directory.RegisterOwner(
                    ownerId, BusinessOwnerKind.Company, name, BusinessOwnerAge.None, seed);
            }

            var female = rng.Next(100) < FemaleOwnerChance;
            var age = AgeBand(rng.Next(100));
            var proprietor = UniqueName(takenNames, () =>
            {
                var firstNames = female
                    ? PedestrianIdentity.AllFemaleNames
                    : PedestrianIdentity.AllMaleNames;
                var first = firstNames[rng.Next(firstNames.Count)];
                var surname =
                    PedestrianIdentity.AllSurnames[rng.Next(PedestrianIdentity.AllSurnames.Count)];
                return first + " " + surname;
            }, site.SiteId);

            return directory.RegisterOwner(
                ownerId, BusinessOwnerKind.Individual, proprietor, age, seed);
        }

        /// <summary>The city's own deed, for a site the plan marks civic.</summary>
        public static BusinessOwnerRecord Civic(BusinessDirectory directory, int citySeed) =>
            directory.RegisterOwner(
                BusinessIdentity.SharedOwner(CityHallKey), BusinessOwnerKind.Civic,
                "City Hall", BusinessOwnerAge.None,
                BusinessIdentity.MixSeed(citySeed, BusinessIdentity.StableHash(CityHallKey)));

        /// <summary>A successor dealt without the city's running uniqueness set. His
        /// name and portrait are a pure function of seed, site and generation, so load
        /// order cannot swap two dead proprietors' replacements.</summary>
        public static BusinessOwnerRecord Successor(
            BusinessDirectory directory, BusinessSite site, int citySeed, int generation)
        {
            if (directory == null)
                throw new ArgumentNullException(nameof(directory));
            if (site == null)
                throw new ArgumentNullException(nameof(site));
            generation = Math.Max(1, generation);
            var seed = BusinessIdentity.MixSeed(
                citySeed + LivingCity.Generation.SeedOffsets.Business,
                site.SiteId, unchecked(OwnerStream + generation * 7919));
            var rng = new System.Random(seed);
            var female = rng.Next(100) < FemaleOwnerChance;
            var age = AgeBand(rng.Next(100));
            var firstNames = female
                ? PedestrianIdentity.AllFemaleNames
                : PedestrianIdentity.AllMaleNames;
            var name = firstNames[rng.Next(firstNames.Count)] + " " +
                PedestrianIdentity.AllSurnames[
                    rng.Next(PedestrianIdentity.AllSurnames.Count)];
            return directory.RegisterOwner(
                BusinessIdentity.Owner(site.SiteId, generation),
                BusinessOwnerKind.Individual, name, age, seed);
        }

        public static string SuccessorName(
            BusinessSite site, int citySeed, int generation)
        {
            if (site == null)
                return "";
            var directory = new BusinessDirectory();
            return Successor(directory, site, citySeed, generation).DisplayName;
        }

        static BusinessOwnerAge AgeBand(int roll) =>
            roll < 22 ? BusinessOwnerAge.Young
            : roll < 72 ? BusinessOwnerAge.Middle
            : BusinessOwnerAge.Old;

        /// <summary>
        /// Deeds stay unique while the draw has luck: sixteen redraws off the site's OWN
        /// stream, then a stable initial derived from the site rather than a running
        /// counter - so the fallback name is a pure function of the site, not of how many
        /// businesses happened to be dealt before it.
        /// </summary>
        static string UniqueName(
            HashSet<string> takenNames, Func<string> draw, BusinessSiteId siteId)
        {
            var name = draw();
            if (takenNames == null)
                return name;

            for (var guard = 0; guard < 16; guard++)
            {
                if (takenNames.Add(name))
                    return name;
                name = draw();
            }

            var initial = (char)('A' +
                (BusinessIdentity.StableHash(siteId.Value) & int.MaxValue) % 26);
            var space = name.IndexOf(' ');
            name = space > 0
                ? name.Substring(0, space + 1) + initial + ". " + name.Substring(space + 1)
                : name + " " + initial + ".";
            takenNames.Add(name);
            return name;
        }
    }

    /// <summary>
    /// The one atomic successor seam used both at the killing and while replaying a
    /// save: deal the same man, register the minted identity, move the deed, then evict
    /// whatever owner profile was cached for this door.
    /// </summary>
    public static class BusinessSuccession
    {
        /// <summary>The one sentence shared by both live door surfaces. The successor
        /// gets a fresh identity; the building keeps its standing and fear.</summary>
        public const string MemoryLine =
            "NEW MAN AT THE COUNTER · the street's memory of us here is his to inherit";

        public static BusinessOwnerRecord Replace(
            BusinessDirectory directory, BusinessSite site,
            LivingCity.Territory.TerritoryBusinessId businessId,
            int citySeed, int generation,
            Action<LivingCity.Territory.TerritoryBusinessId> invalidateProfile = null)
        {
            if (directory == null || site == null || !businessId.IsValid || generation <= 0 ||
                !directory.TryGet(businessId, out var business) ||
                business.SiteId != site.SiteId)
                return null;
            var owner = BusinessOwners.Successor(directory, site, citySeed, generation);
            if (owner == null || !directory.SetOwner(businessId, owner.Id))
                return null;
            invalidateProfile?.Invoke(businessId);
            return owner;
        }
    }
}
