using System;
using System.Collections.Generic;
using LivingCity.Territory;

namespace LivingCity.Business
{
    /// <summary>The provider names. Constants rather than an enum because a site ID carries
    /// one as text and a saved ID must not depend on enum ordering.</summary>
    public static class BusinessProviders
    {
        /// <summary>Ground-floor commerce inside residential plan data: shopfront groups,
        /// the named kit storefronts, and the cafes standing in the gaps of a row.</summary>
        public const string Residential = "res";

        /// <summary>Standalone commercial, hospitality and leisure venues: the nightclub
        /// block, the quay's terraces, diner and fairground, the gyms.</summary>
        public const string Standalone = "venue";

        /// <summary>Many structures, one firm: car yards, filling stations, works,
        /// warehouse compounds and the harbour.</summary>
        public const string Compound = "compound";
    }

    /// <summary>
    /// A business-capable place as the city PLAN describes it - never as the hierarchy
    /// currently shows it. Every field is read from persistent recipe/plan data, so the
    /// same site comes back with the same ID and the same pose whether its block is on
    /// camera, pooled, or has never been composed at all.
    ///
    /// One building may publish several sites (a row with shops on two streets); many
    /// structures may publish one (a works with six halls). Both shapes are representable
    /// because the site owns its own footprint and grouping key rather than borrowing a
    /// renderer's.
    /// </summary>
    public sealed class BusinessSite
    {
        public BusinessSite(
            string providerId,
            string sourcePlanId,
            string groupKey,
            TerritoryBounds footprint,
            TerritoryPoint approach,
            TerritoryPoint approachOutward,
            string archetypeHint,
            BusinessSiteSize size,
            TerritoryBlockId blockHint,
            int legacyBlockId,
            string label,
            string role,
            int publishOrder,
            bool eligible = true,
            string exclusionReason = "")
        {
            ProviderId = providerId ?? "";
            SourcePlanId = sourcePlanId ?? "";
            GroupKey = groupKey ?? "";
            SiteId = BusinessIdentity.Site(ProviderId, SourcePlanId, GroupKey);
            Footprint = footprint;
            Approach = approach;
            ApproachOutward = approachOutward;
            ArchetypeHint = archetypeHint ?? "";
            Size = size;
            BlockHint = blockHint;
            LegacyBlockId = legacyBlockId;
            Label = label ?? SiteId.Value;
            Role = role ?? "";
            PublishOrder = publishOrder;
            Eligible = eligible;
            ExclusionReason = exclusionReason ?? "";
        }

        public BusinessSiteId SiteId { get; }
        public string ProviderId { get; }

        /// <summary>The recipe or plan this site was read out of - a Core block StableId, a
        /// residential recipe ID, a quay stretch. Diagnostics quote it to answer "which
        /// source owns this shop".</summary>
        public string SourcePlanId { get; }

        /// <summary>What inside that plan the site is: a spot and a facade, a gap, a
        /// compound key. Two providers must never mint the same (plan, group) pair.</summary>
        public string GroupKey { get; }

        public TerritoryBounds Footprint { get; }

        /// <summary>Where a man walks up to it. For a compound this is the public gate or
        /// forecourt entry, not the middle of the yard.</summary>
        public TerritoryPoint Approach { get; }

        /// <summary>Unit direction the entrance faces, world XZ. Zero when the plan does
        /// not say - the caller then falls back on the footprint's centre.</summary>
        public TerritoryPoint ApproachOutward { get; }

        /// <summary>Authored physical purpose, or empty for generic glass. A nightclub
        /// block carries "nightclub" and can never be rolled into a bakery.</summary>
        public string ArchetypeHint { get; }

        public BusinessSiteSize Size { get; }
        public TerritoryBlockId BlockHint { get; }

        /// <summary>The existing integer block id the rest of the game still speaks
        /// (BusinessMarker.BlockId, GangFronts.FrontCandidate); -1 when the plan has none.</summary>
        public int LegacyBlockId { get; }

        public string Label { get; }

        /// <summary>A short provider-defined word for what kind of place this is inside its
        /// own source - "frontage", "cafe", "venue", "compound". Diagnostics print it, and a
        /// consumer that used to walk one particular kind of source (Core's outfit fronts)
        /// filters on it rather than on a hierarchy.</summary>
        public string Role { get; }

        /// <summary>The order this provider published it in. Enumeration is by SiteId -
        /// see <see cref="BusinessSiteCatalog"/> - but a consumer that must reproduce a
        /// legacy per-plan ordering has this rather than a hierarchy walk.</summary>
        public int PublishOrder { get; }

        /// <summary>False for a place the sweep found but which cannot carry a business -
        /// a civic building, a public park. Kept in the catalogue with its reason so the
        /// audit can say why it was passed over rather than losing it silently.</summary>
        public bool Eligible { get; }

        public string ExclusionReason { get; }
    }

    /// <summary>
    /// One city subsystem publishing its business-capable places. A provider reads
    /// persistent plan/recipe data; it must never derive a site from an active renderer,
    /// collider or recycled GameObject, because those come and go with the camera.
    /// </summary>
    public interface IBusinessSiteProvider
    {
        string ProviderId { get; }
        IEnumerable<BusinessSite> Sites();
    }

    /// <summary>
    /// Every provider's sites in one place, in ONE order: ascending SiteId, ordinal.
    /// Provider registration order is deliberately not the enumeration order - that is the
    /// streaming-order trap the epic names, and it would make the population pass depend
    /// on which district happened to build first.
    ///
    /// A duplicate site ID is a fault, not a merge: both providers are named and the second
    /// site is dropped, so a source claimed by two tickets shows up as a failure instead of
    /// quietly doubling a business.
    /// </summary>
    public sealed class BusinessSiteCatalog
    {
        readonly List<IBusinessSiteProvider> providers = new List<IBusinessSiteProvider>();
        readonly List<BusinessSite> sites = new List<BusinessSite>();
        readonly Dictionary<BusinessSiteId, BusinessSite> bySite =
            new Dictionary<BusinessSiteId, BusinessSite>();
        readonly List<string> problems = new List<string>();
        bool built;

        public IReadOnlyList<BusinessSite> Sites => sites;
        public IReadOnlyList<string> Problems => problems;
        public int ProviderCount => providers.Count;

        public void Add(IBusinessSiteProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));
            if (built)
                throw new InvalidOperationException(
                    "The site catalogue was already built; add every provider first.");
            providers.Add(provider);
        }

        public bool TryGet(BusinessSiteId siteId, out BusinessSite site) =>
            bySite.TryGetValue(siteId, out site);

        /// <summary>Collect, refuse duplicates, and sort. Safe to call twice: the second
        /// call rebuilds from the same providers and must produce the same list.</summary>
        public void Build()
        {
            sites.Clear();
            bySite.Clear();
            problems.Clear();

            foreach (var provider in providers)
            {
                var published = provider.Sites();
                if (published == null)
                    continue;

                foreach (var site in published)
                {
                    if (site == null)
                        continue;

                    if (!site.SiteId.IsValid)
                    {
                        problems.Add($"BIZ: provider '{provider.ProviderId}' published a " +
                                     "site with no ID.");
                        continue;
                    }

                    if (bySite.TryGetValue(site.SiteId, out var existing))
                    {
                        problems.Add(
                            $"BIZ: duplicate site '{site.SiteId}' - provider " +
                            $"'{existing.ProviderId}' published it from plan " +
                            $"'{existing.SourcePlanId}', provider '{site.ProviderId}' " +
                            $"published it again from plan '{site.SourcePlanId}'.");
                        continue;
                    }

                    bySite.Add(site.SiteId, site);
                    sites.Add(site);
                }
            }

            // The one ordering. Ordinal, so it cannot drift with the machine's culture.
            sites.Sort((a, b) => string.CompareOrdinal(a.SiteId.Value, b.SiteId.Value));
            built = true;
        }

        public int EligibleCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < sites.Count; i++)
                    if (sites[i].Eligible)
                        count++;
                return count;
            }
        }
    }
}
