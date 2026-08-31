using System;
using System.Collections.Generic;
using System.Globalization;
using LivingCity.Generation;

namespace LivingCity.Business
{
    /// <summary>What one population pass did, per provider and per archetype, and what it
    /// could not do. The audit prints this; nothing repairs data off it.</summary>
    public sealed class BusinessPopulationReport
    {
        public int Sites { get; internal set; }
        public int Eligible { get; internal set; }
        public int Populated { get; internal set; }
        public int Owners { get; internal set; }

        public Dictionary<string, int> ByProvider { get; } =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public Dictionary<BusinessArchetypeId, int> ByArchetype { get; } =
            new Dictionary<BusinessArchetypeId, int>();

        /// <summary>Sites the plan published but nothing could stand on: an unknown sign, a
        /// size no archetype accepts, an excluded civic building.</summary>
        public List<string> Unsupported { get; } = new List<string>();

        public List<string> Problems { get; } = new List<string>();

        public string Summary()
        {
            var providers = new List<string>(ByProvider.Keys);
            providers.Sort(StringComparer.Ordinal);
            var text = new System.Text.StringBuilder();
            text.Append(Populated).Append(" businesses, ").Append(Owners).Append(" owners, ")
                .Append(Sites).Append(" sites (");
            for (var i = 0; i < providers.Count; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(providers[i]).Append(' ').Append(ByProvider[providers[i]]);
            }
            text.Append(')');
            if (Unsupported.Count > 0)
                text.Append("; ").Append(Unsupported.Count).Append(" unsupported");
            return text.ToString();
        }
    }

    /// <summary>
    /// Every eligible site gets exactly one business, and the same seed gives the same city
    /// whatever order the providers or the camera happened to run in.
    ///
    /// The pass walks the catalogue in ITS order - ascending SiteId - and draws each site
    /// from its OWN stream, <c>MixSeed(citySeed + SeedOffsets.Business, siteId, stream)</c>.
    /// One shared sequential RNG was the obvious alternative and is the trap: with it, a
    /// site added anywhere would shift every draw after it and reroll half the city.
    ///
    /// Phase 1 fills every eligible site. There are no invented vacancies: an empty shop is
    /// a thing the racket and economy layers may later cause, not something generation
    /// sprinkles.
    /// </summary>
    public static class BusinessPopulation
    {
        /// <summary>The pass's own stream within a site: archetype, name and turnover.
        /// The deed draws from stream 2 - see BusinessOwners.OwnerStream.</summary>
        public const int PopulationStream = 1;

        public static BusinessPopulationReport Populate(
            BusinessSiteCatalog catalog, int citySeed, BusinessDirectory directory)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (directory == null)
                throw new ArgumentNullException(nameof(directory));

            var report = new BusinessPopulationReport();
            var takenOwnerNames = new HashSet<string>(StringComparer.Ordinal);
            var takenBusinessNames = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var problem in catalog.Problems)
                report.Problems.Add(problem);

            var sites = catalog.Sites;
            report.Sites = sites.Count;

            for (var i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                if (!site.Eligible)
                {
                    report.Unsupported.Add(
                        $"{site.SiteId}: excluded - " +
                        (string.IsNullOrEmpty(site.ExclusionReason)
                            ? "no reason recorded"
                            : site.ExclusionReason));
                    continue;
                }

                report.Eligible++;

                var rng = new System.Random(BusinessIdentity.MixSeed(
                    citySeed + SeedOffsets.Business, site.SiteId, PopulationStream));

                // 1. Explicit physical purpose wins. An unknown sign is a provider bug and
                //    is reported rather than quietly rolled into a bakery.
                BusinessArchetype archetype;
                if (!string.IsNullOrEmpty(site.ArchetypeHint))
                {
                    if (!BusinessArchetypes.TryFromSignage(site.ArchetypeHint, out archetype))
                    {
                        report.Unsupported.Add(
                            $"{site.SiteId}: unknown archetype hint '{site.ArchetypeHint}' " +
                            $"from provider '{site.ProviderId}' (plan '{site.SourcePlanId}').");
                        continue;
                    }
                }
                else
                {
                    // 2. Only unlabelled glass rolls, and only over archetypes its size
                    //    can carry.
                    archetype = BusinessArchetypes.RollGeneric(site.Size, rng);
                    if (archetype == null)
                    {
                        report.Unsupported.Add(
                            $"{site.SiteId}: no archetype accepts a {site.Size} generic site " +
                            $"(provider '{site.ProviderId}').");
                        continue;
                    }
                }

                // 3. Name, 4. deed, 5. size facts - in this fixed order. Inserting a draw
                //    between them changes every business in the city.
                var name = UniqueBusinessName(archetype, rng, takenBusinessNames);
                var turnover = Turnover(archetype, rng);
                var owner = BusinessOwners.ForSite(
                    directory, site, archetype, citySeed, takenOwnerNames);

                var record = directory.Register(
                    site.SiteId, archetype.Id, name, owner.Id, site.Size, turnover,
                    site.ProviderId);
                if (record == null)
                    continue;

                report.Populated++;
                Bump(report.ByProvider, site.ProviderId);
                report.ByArchetype.TryGetValue(archetype.Id, out var archetypeCount);
                report.ByArchetype[archetype.Id] = archetypeCount + 1;
            }

            report.Owners = directory.OwnerIds.Count;
            foreach (var problem in directory.Problems)
                report.Problems.Add(problem);
            return report;
        }

        /// <summary>Weekly takings to the nearest fifty - a size fact, not a payment.</summary>
        static int Turnover(BusinessArchetype archetype, System.Random rng)
        {
            var raw = rng.Next(archetype.TurnoverLow, archetype.TurnoverHigh + 1);
            return Math.Max(50, (raw / 50) * 50);
        }

        /// <summary>
        /// Two "Corner Market"s across town read as a bug, so a taken sign is redrawn from
        /// the site's OWN stream before it is numbered. Eight tries: a city of eight hundred
        /// shops draws far more names than the tables hold, and numbering every repeat gave
        /// "Bella Notte Cafe 26" - a sign no street ever carried. The count is kept per name,
        /// so a repeat changes only its own sign and never anybody else's.
        /// </summary>
        static string UniqueBusinessName(
            BusinessArchetype archetype, System.Random rng, Dictionary<string, int> taken)
        {
            var name = BusinessArchetypes.Name(archetype, rng);
            for (var guard = 0; guard < 8 && taken.ContainsKey(name); guard++)
                name = BusinessArchetypes.Name(archetype, rng);

            taken.TryGetValue(name, out var seen);
            taken[name] = seen + 1;
            return seen == 0 ? name : name + " " + Numeral(seen + 1);
        }

        static string Numeral(int n)
        {
            switch (n)
            {
                case 2: return "II";
                case 3: return "III";
                case 4: return "IV";
                case 5: return "V";
                case 6: return "VI";
                case 7: return "VII";
                case 8: return "VIII";
                case 9: return "IX";
                case 10: return "X";
                default: return "No. " + n.ToString(CultureInfo.InvariantCulture);
            }
        }

        static void Bump(Dictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
        }
    }
}
