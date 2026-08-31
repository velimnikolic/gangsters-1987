using System.Collections.Generic;
using LivingCity.Territory;

namespace LivingCity.Business
{
    /// <summary>
    /// The business layer as territory geography needs to read it: every published site
    /// with its footprint, its doorstep, the block its provider claims, and the business
    /// dealt on it. Geography holds no reference to this layer - it is handed this list -
    /// so block membership can be resolved with nothing standing in the scene, and the
    /// answer cannot come to depend on which street the camera is looking at.
    ///
    /// Sites come out in the catalogue's ONE order (ascending SiteId, ordinal), which is
    /// also the order the geography sorts them into: the same city resolves the same way
    /// whichever district was built first.
    /// </summary>
    public sealed class BusinessGeographySites : ITerritoryBusinessSiteSource
    {
        readonly BusinessSiteCatalog catalog;
        readonly BusinessDirectory directory;

        public BusinessGeographySites(BusinessSiteCatalog catalog, BusinessDirectory directory)
        {
            this.catalog = catalog;
            this.directory = directory;
        }

        public IReadOnlyList<TerritoryBusinessSiteRecord> Sites()
        {
            var records = new List<TerritoryBusinessSiteRecord>();
            if (catalog == null)
                return records;

            var sites = catalog.Sites;
            for (var i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                var businessId = default(TerritoryBusinessId);
                if (directory != null && directory.TryGetBySite(site.SiteId, out var record))
                    businessId = record.Id;

                records.Add(new TerritoryBusinessSiteRecord(
                    site.SiteId.Value,
                    businessId,
                    site.BlockHint,
                    site.Footprint,
                    site.Approach,
                    site.Eligible,
                    site.Label,
                    site.ProviderId));
            }

            return records;
        }
    }
}
