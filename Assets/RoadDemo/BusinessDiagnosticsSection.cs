using System.Collections.Generic;
using System.Text;
using LivingCity.Business;
using LivingCity.Territory;

namespace RoadDemo
{
    /// <summary>
    /// The businesses page of the F8 inspector (BIZ-012): the city's totals by provider and
    /// by trade, how many of them have a view standing right now, and - for the block under
    /// the cursor - every shop on it with its site, its sign, its trade and its gazda.
    ///
    /// Read-only, like every other section here. An audit that repaired its data would hide
    /// the faults it exists to show, so a site that was never populated, a sign that does not
    /// match the trade and a business with no owner are all PRINTED and never fixed.
    /// </summary>
    public sealed class BusinessDiagnosticsSection : ITerritoryDiagnosticsSection
    {
        readonly List<string> lines = new List<string>();

        public string Title => "Businesses";

        public void Append(StringBuilder text, TerritoryBlockTruth block, TerritoryRuntime runtime)
        {
            var business = BusinessRuntime.Instance;
            if (business == null || !business.Populated)
            {
                text.Append("no business simulation in this scene");
                return;
            }

            var report = business.Report;
            var catalog = business.Catalog;
            var directory = business.Directory;

            text.Append(directory.BusinessIds.Count).Append(" businesses, ")
                .Append(directory.OwnerIds.Count).Append(" owners, ")
                .Append(catalog.Sites.Count).Append(" sites (")
                .Append(catalog.EligibleCount).AppendLine(" eligible)")
                .Append("views bound: ").Append(business.BoundViews)
                .Append("   unsupported: ").Append(report.Unsupported.Count)
                .Append("   version: ").AppendLine(directory.Version.ToString());

            lines.Clear();
            foreach (var pair in report.ByProvider)
                lines.Add(pair.Key + " " + pair.Value);
            lines.Sort(System.StringComparer.Ordinal);
            text.Append("by provider: ").AppendLine(string.Join("  ", lines));

            AppendBlock(text, block, business);
        }

        void AppendBlock(StringBuilder text, TerritoryBlockTruth block, BusinessRuntime business)
        {
            if (block?.Definition == null)
                return;

            var blockId = block.Definition.Id;
            var directory = business.Directory;
            var catalog = business.Catalog;
            var shown = 0;

            text.Append("on this block:");
            foreach (var site in catalog.Sites)
            {
                if (site.BlockHint != blockId)
                    continue;

                text.AppendLine();
                if (shown++ >= 12)
                {
                    text.Append("  ... more, see gangsters_business_audit --rows");
                    return;
                }

                if (!site.Eligible)
                {
                    text.Append("  [no business] ").Append(site.Role).Append(" - ")
                        .Append(site.ExclusionReason);
                    continue;
                }

                if (!directory.TryGetBySite(site.SiteId, out var record))
                {
                    text.Append("  [UNPOPULATED] ").Append(site.SiteId.Value);
                    continue;
                }

                var owner = business.OwnerNameOf(record);
                var bound = BusinessViewBindings.TryGet(record.Id, out _) ? "bound" : "no view";
                text.Append("  ").Append(record.DisplayName)
                    .Append(" - ").Append(record.Archetype)
                    .Append(" - ").Append(owner)
                    .Append(" - ").Append(record.State)
                    .Append(" - $").Append(record.EstimatedWeeklyTurnover).Append("/wk [")
                    .Append(site.Role).Append(", ")
                    .Append(string.IsNullOrEmpty(site.ArchetypeHint) ? "no sign" : site.ArchetypeHint)
                    .Append(", ").Append(bound).Append(']');
                if (business.TryGetShutdown(record.Id, out var shutdown))
                    text.Append("  ")
                        .Append(BusinessShutdownText.Line(shutdown))
                        .Append("  repair $").Append(shutdown.RepairPrice)
                        .Append("  deadline ").Append(shutdown.RecoveryAt.ToString("0.0"))
                        .Append('h');
            }

            if (shown == 0)
                text.Append(" none");
        }
    }
}
