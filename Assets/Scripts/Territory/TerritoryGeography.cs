using System;
using System.Collections.Generic;

namespace LivingCity.Territory
{
    /// <summary>
    /// One named neighborhood as an aggregate of canonical blocks. A neighborhood has no
    /// mutable gang owner and is never separately captured: it is a name, a rectangle and
    /// a list of member blocks, so that "the Landings" can be printed on a map and asked
    /// for its blocks without becoming a second unit of territory.
    /// </summary>
    public sealed class TerritoryNeighborhoodDefinition
    {
        public TerritoryNeighborhoodDefinition(
            TerritoryNeighborhoodId id,
            string name,
            TerritoryBounds worldBounds,
            IReadOnlyList<TerritoryBlockId> blockIds,
            IReadOnlyList<TerritoryNeighborhoodId> neighbours)
        {
            Id = id;
            Name = name ?? "";
            WorldBounds = worldBounds;
            BlockIds = blockIds ?? Array.Empty<TerritoryBlockId>();
            Neighbours = neighbours ?? Array.Empty<TerritoryNeighborhoodId>();
        }

        public TerritoryNeighborhoodId Id { get; }
        public string Name { get; }
        public TerritoryBounds WorldBounds { get; }
        public IReadOnlyList<TerritoryBlockId> BlockIds { get; }

        /// <summary>Neighborhoods that share a block-level edge with this one. Derived
        /// from the block graph, so map, simulation and UI cannot disagree about which
        /// quarters touch.</summary>
        public IReadOnlyList<TerritoryNeighborhoodId> Neighbours { get; }
    }

    /// <summary>
    /// Ground the city stands on that carries NO canonical block - the harbour, the
    /// airfield, a suburb the territory plan does not describe. It is published rather
    /// than left silent so that "this place belongs to nobody" is a stated classification
    /// a diagnostic can print, not an assumption hidden in a failed lookup.
    /// </summary>
    public sealed class TerritoryOffGridArea
    {
        public TerritoryOffGridArea(string name, string kind, TerritoryBounds worldBounds,
                                    string classification)
        {
            Name = name ?? "";
            Kind = kind ?? "";
            WorldBounds = worldBounds;
            Classification = classification ?? "";
        }

        public string Name { get; }
        public string Kind { get; }
        public TerritoryBounds WorldBounds { get; }

        /// <summary>Why it is not territory, in one phrase, for the validation output.</summary>
        public string Classification { get; }
    }

    /// <summary>
    /// The street widths the geography measures with. They are PLAN data - the city's own
    /// street, alley and boulevard widths - handed in by the adapter that knows the plan,
    /// never a constant chosen here: a city whose streets were widened would otherwise
    /// quietly lose half its block adjacencies.
    /// </summary>
    public readonly struct TerritoryGeographySettings
    {
        public TerritoryGeographySettings(float alleyWidth, float streetWidth,
                                          float boulevardWidth)
        {
            AlleyWidth = Math.Max(0.5f, alleyWidth);
            StreetWidth = Math.Max(AlleyWidth, streetWidth);
            BoulevardWidth = Math.Max(StreetWidth, boulevardWidth);
        }

        /// <summary>Core's own measures: a 5 m alley, a 15 m street, the boulevard's
        /// kerb-to-kerb width. Used by tests and by any scene with no plan to ask.</summary>
        public static TerritoryGeographySettings Default =>
            new TerritoryGeographySettings(5f, 15f, 35f);

        public float AlleyWidth { get; }
        public float StreetWidth { get; }
        public float BoulevardWidth { get; }

        /// <summary>The widest gap two blocks may face each other across and still be
        /// neighbours: the widest street in the city, plus a kerb's worth of slack for
        /// bounds that stop at the pavement rather than at the carriageway.</summary>
        public float NeighbourGap => BoulevardWidth + AlleyWidth;

        /// <summary>Two blocks must face each other along at least this much shared
        /// frontage - a corner clipping another block's corner is not a neighbour.</summary>
        public float MinimumFrontage => AlleyWidth;

        /// <summary>How far off a block a man may stand and still count as being on it.
        /// Half the widest street: a man crossing an ordinary street never leaves the
        /// block he came from before entering the one opposite, and a man in the middle
        /// of a boulevard is on neither.</summary>
        public float RoadHysteresis => BoulevardWidth * 0.5f;
    }

    /// <summary>Which authority actually placed a business on its block.</summary>
    public enum TerritoryBusinessBinding
    {
        /// <summary>Never resolved - reported, never guessed at.</summary>
        Unresolved = 0,

        /// <summary>The provider published the block itself; nothing was measured.</summary>
        PlanHint,

        /// <summary>The site's footprint lies on the block.</summary>
        Footprint,

        /// <summary>Only the doorstep resolved - a footprint that straddles a pavement.</summary>
        Approach,
    }

    /// <summary>
    /// One business-capable place as geography needs to see it. The Business layer builds
    /// these out of its site catalogue; geography never reaches into that layer, so the
    /// mapping cannot come to depend on which shops happen to be standing.
    /// </summary>
    public readonly struct TerritoryBusinessSiteRecord
    {
        public TerritoryBusinessSiteRecord(
            string siteId,
            TerritoryBusinessId businessId,
            TerritoryBlockId blockHint,
            TerritoryBounds footprint,
            TerritoryPoint approach,
            bool eligible,
            string label,
            string provider)
        {
            SiteId = siteId ?? "";
            BusinessId = businessId;
            BlockHint = blockHint;
            Footprint = footprint;
            Approach = approach;
            Eligible = eligible;
            Label = label ?? "";
            Provider = provider ?? "";
        }

        public string SiteId { get; }
        public TerritoryBusinessId BusinessId { get; }
        public TerritoryBlockId BlockHint { get; }
        public TerritoryBounds Footprint { get; }
        public TerritoryPoint Approach { get; }
        public bool Eligible { get; }
        public string Label { get; }
        public string Provider { get; }
    }

    public interface ITerritoryBusinessSiteSource
    {
        IReadOnlyList<TerritoryBusinessSiteRecord> Sites();
    }

    /// <summary>Where one business sits, and on whose word.</summary>
    public readonly struct TerritoryBusinessPlacement
    {
        public TerritoryBusinessPlacement(
            string siteId, TerritoryBusinessId businessId, TerritoryBlockId blockId,
            TerritoryBusinessBinding binding, string label, string provider)
        {
            SiteId = siteId ?? "";
            BusinessId = businessId;
            BlockId = blockId;
            Binding = binding;
            Label = label ?? "";
            Provider = provider ?? "";
        }

        public string SiteId { get; }
        public TerritoryBusinessId BusinessId { get; }
        public TerritoryBlockId BlockId { get; }
        public TerritoryBusinessBinding Binding { get; }
        public string Label { get; }
        public string Provider { get; }
    }

    /// <summary>
    /// The one geography query surface. Simulation, UI and maps ask THIS what a block is,
    /// where it is, what it touches and what trades on it; nothing outside the adapter
    /// that builds it may read RoadDemo's plan directly, and nothing may compute block
    /// rectangles of its own.
    /// </summary>
    public interface ITerritoryGeography
    {
        TerritoryGeographySettings Settings { get; }
        IReadOnlyList<TerritoryBlockId> BlockIds { get; }
        IReadOnlyList<TerritoryNeighborhoodId> NeighborhoodIds { get; }
        IReadOnlyList<TerritoryOffGridArea> OffGridAreas { get; }
        TerritoryBounds WorldBounds { get; }

        bool TryGetBlock(TerritoryBlockId blockId, out TerritoryBlockDefinition block);
        bool TryGetNeighborhood(
            TerritoryNeighborhoodId neighborhoodId, out TerritoryNeighborhoodDefinition hood);

        /// <summary>The block a point is ON: the smallest containing rectangle, so a
        /// nested downtown block wins over the larger one it sits inside.</summary>
        bool TryGetBlockAt(TerritoryPoint point, out TerritoryBlockId blockId);

        /// <summary>Where a body standing at this point belongs. See
        /// <see cref="TerritoryGeographySettings.RoadHysteresis"/>: on a block it is that
        /// block, off it the previous block while the man is still within half a street,
        /// and otherwise NOTHING - road space belongs to nobody and is never guessed at.</summary>
        bool TryResolveStanding(
            TerritoryPoint point, TerritoryBlockId previous, out TerritoryBlockId blockId);

        IReadOnlyList<TerritoryBlockId> Neighbours(TerritoryBlockId blockId);
        bool AreNeighbours(TerritoryBlockId one, TerritoryBlockId other);

        bool TryGetBusinessBlock(TerritoryBusinessId businessId, out TerritoryBlockId blockId);
        IReadOnlyList<TerritoryBusinessPlacement> BusinessesOf(TerritoryBlockId blockId);
        IReadOnlyList<TerritoryBusinessPlacement> UnplacedBusinesses { get; }
        TerritoryGeographyReport Report { get; }
    }

    /// <summary>
    /// What the geography could and could not account for. Every number here is meant to
    /// be printed - by the geography audit, by the debug overlay - because the failures
    /// this catches (a block in no neighborhood, a shop on no block, two blocks claiming
    /// the same ground) are exactly the ones that otherwise show up months later as a
    /// gang holding a pavement.
    /// </summary>
    public sealed class TerritoryGeographyReport
    {
        readonly List<string> faults = new List<string>();
        readonly List<string> notes = new List<string>();

        public IReadOnlyList<string> Faults => faults;
        public IReadOnlyList<string> Notes => notes;
        public int Blocks { get; internal set; }
        public int Neighborhoods { get; internal set; }
        public int Edges { get; internal set; }
        public int IsolatedBlocks { get; internal set; }
        public int NestedBlocks { get; internal set; }
        public int PlacedBusinesses { get; internal set; }
        public int UnplacedBusinesses { get; internal set; }
        public bool Passed => faults.Count == 0;

        internal void Fault(string line)
        {
            if (!string.IsNullOrEmpty(line))
                faults.Add(line);
        }

        internal void Note(string line)
        {
            if (!string.IsNullOrEmpty(line))
                notes.Add(line);
        }
    }

    /// <summary>
    /// Canonical city geography: blocks, the neighborhoods they aggregate into, the
    /// block-level neighbor graph, and the businesses standing on them. Pure data - it
    /// owns no GameObject and calls no engine API - so the same city can be dealt and
    /// judged from the terminal with the editor idle.
    ///
    /// It is built ONCE from <see cref="TerritoryBlockDefinition"/>s that the scene
    /// adapter has already read out of the immutable city plan. It derives; it never
    /// measures a renderer, and it never mints identity of its own.
    /// </summary>
    public sealed class TerritoryGeography : ITerritoryGeography
    {
        static readonly TerritoryBlockId[] NoBlocks = Array.Empty<TerritoryBlockId>();
        static readonly TerritoryBusinessPlacement[] NoBusinesses =
            Array.Empty<TerritoryBusinessPlacement>();

        readonly List<TerritoryBlockId> blockIds = new List<TerritoryBlockId>();
        readonly Dictionary<TerritoryBlockId, TerritoryBlockDefinition> blocks =
            new Dictionary<TerritoryBlockId, TerritoryBlockDefinition>();
        readonly List<TerritoryNeighborhoodId> neighborhoodIds =
            new List<TerritoryNeighborhoodId>();
        readonly Dictionary<TerritoryNeighborhoodId, TerritoryNeighborhoodDefinition> hoods =
            new Dictionary<TerritoryNeighborhoodId, TerritoryNeighborhoodDefinition>();
        readonly Dictionary<TerritoryBlockId, List<TerritoryBlockId>> neighbours =
            new Dictionary<TerritoryBlockId, List<TerritoryBlockId>>();
        readonly Dictionary<TerritoryBlockId, List<TerritoryBusinessPlacement>> businesses =
            new Dictionary<TerritoryBlockId, List<TerritoryBusinessPlacement>>();
        readonly Dictionary<TerritoryBusinessId, TerritoryBlockId> businessBlocks =
            new Dictionary<TerritoryBusinessId, TerritoryBlockId>();
        readonly List<TerritoryBusinessPlacement> unplaced =
            new List<TerritoryBusinessPlacement>();
        readonly List<TerritoryOffGridArea> offGrid = new List<TerritoryOffGridArea>();
        readonly TerritoryGeographyReport report = new TerritoryGeographyReport();

        public TerritoryGeography(
            IEnumerable<TerritoryBlockDefinition> definitions,
            TerritoryGeographySettings settings,
            IEnumerable<TerritoryOffGridArea> offGridAreas = null)
        {
            Settings = settings.NeighbourGap > 0f ? settings : TerritoryGeographySettings.Default;

            if (definitions != null)
            {
                foreach (var block in definitions)
                {
                    if (block == null || !block.Id.IsValid)
                        continue;
                    if (blocks.ContainsKey(block.Id))
                    {
                        report.Fault("GEO: duplicate canonical block id '" + block.Id.Value +
                                     "' - the second definition was dropped.");
                        continue;
                    }

                    blocks.Add(block.Id, block);
                    blockIds.Add(block.Id);
                }
            }

            // ONE enumeration order, ordinal by canonical id. Definition order is the
            // plan's, and a pass that added a block would otherwise renumber everything
            // downstream of it - the streaming-order trap, in its geography shape.
            blockIds.Sort(CompareBlockIds);

            if (offGridAreas != null)
                foreach (var area in offGridAreas)
                    if (area != null)
                        offGrid.Add(area);
            offGrid.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            BuildNeighbourGraph();
            BuildNeighborhoods();
            Validate();
        }

        public TerritoryGeographySettings Settings { get; }
        public IReadOnlyList<TerritoryBlockId> BlockIds => blockIds;
        public IReadOnlyList<TerritoryNeighborhoodId> NeighborhoodIds => neighborhoodIds;
        public IReadOnlyList<TerritoryOffGridArea> OffGridAreas => offGrid;
        public TerritoryBounds WorldBounds { get; private set; }
        public TerritoryGeographyReport Report => report;
        public IReadOnlyList<TerritoryBusinessPlacement> UnplacedBusinesses => unplaced;

        public bool TryGetBlock(TerritoryBlockId blockId, out TerritoryBlockDefinition block) =>
            blocks.TryGetValue(blockId, out block);

        public bool TryGetNeighborhood(
            TerritoryNeighborhoodId neighborhoodId, out TerritoryNeighborhoodDefinition hood) =>
            hoods.TryGetValue(neighborhoodId, out hood);

        public bool TryGetBlockAt(TerritoryPoint point, out TerritoryBlockId blockId)
        {
            blockId = default;
            var bestArea = float.MaxValue;
            for (var i = 0; i < blockIds.Count; i++)
            {
                var definition = blocks[blockIds[i]];
                if (!definition.WorldBounds.Contains(point))
                    continue;
                var area = definition.WorldBounds.Area;
                if (area >= bestArea)
                    continue;
                bestArea = area;
                blockId = definition.Id;
            }

            return blockId.IsValid;
        }

        public bool TryResolveStanding(
            TerritoryPoint point, TerritoryBlockId previous, out TerritoryBlockId blockId)
        {
            if (TryGetBlockAt(point, out blockId))
                return true;

            // Street, sidewalk, kerb: the man keeps the block he was last on while he is
            // still within half the widest street of it. Crossing an ordinary street he
            // therefore leaves A exactly when he arrives on B - one leave, one enter.
            if (previous.IsValid && blocks.TryGetValue(previous, out var last) &&
                last.WorldBounds.DistanceTo(point) <= Settings.RoadHysteresis)
            {
                blockId = previous;
                return true;
            }

            blockId = default;
            return false;
        }

        public IReadOnlyList<TerritoryBlockId> Neighbours(TerritoryBlockId blockId) =>
            neighbours.TryGetValue(blockId, out var list)
                ? (IReadOnlyList<TerritoryBlockId>)list
                : NoBlocks;

        public bool AreNeighbours(TerritoryBlockId one, TerritoryBlockId other)
        {
            if (!neighbours.TryGetValue(one, out var list))
                return false;
            for (var i = 0; i < list.Count; i++)
                if (list[i] == other)
                    return true;
            return false;
        }

        public bool TryGetBusinessBlock(
            TerritoryBusinessId businessId, out TerritoryBlockId blockId) =>
            businessBlocks.TryGetValue(businessId, out blockId);

        public IReadOnlyList<TerritoryBusinessPlacement> BusinessesOf(TerritoryBlockId blockId) =>
            businesses.TryGetValue(blockId, out var list)
                ? (IReadOnlyList<TerritoryBusinessPlacement>)list
                : NoBusinesses;

        /// <summary>
        /// Resolve every published business site to exactly one canonical block, once.
        /// The provider's own block hint is preferred - it comes from the same plan the
        /// blocks do - then the footprint, then the doorstep (a shop's doorstep lies on
        /// the pavement, which belongs to no block, so it is the last word rather than
        /// the first). A site that resolves to nothing is REPORTED and left unplaced: a
        /// business hung on the wrong block would tell the player a pavement pays rent.
        /// </summary>
        public void BindBusinesses(ITerritoryBusinessSiteSource source)
        {
            businesses.Clear();
            businessBlocks.Clear();
            unplaced.Clear();
            report.PlacedBusinesses = 0;
            report.UnplacedBusinesses = 0;

            var sites = source?.Sites();
            if (sites == null || sites.Count == 0)
                return;

            var ordered = new List<TerritoryBusinessSiteRecord>(sites);
            ordered.Sort((a, b) => string.CompareOrdinal(a.SiteId, b.SiteId));

            for (var i = 0; i < ordered.Count; i++)
            {
                var site = ordered[i];
                if (!site.Eligible)
                    continue;

                var binding = Resolve(site, out var blockId);
                var placement = new TerritoryBusinessPlacement(
                    site.SiteId, site.BusinessId, blockId, binding, site.Label, site.Provider);

                if (binding == TerritoryBusinessBinding.Unresolved)
                {
                    unplaced.Add(placement);
                    report.UnplacedBusinesses++;
                    report.Fault("GEO: business site '" + site.SiteId +
                                 "' (" + site.Provider + ") sits on no canonical block.");
                    continue;
                }

                if (!businesses.TryGetValue(blockId, out var list))
                {
                    list = new List<TerritoryBusinessPlacement>();
                    businesses.Add(blockId, list);
                }

                list.Add(placement);
                if (site.BusinessId.IsValid)
                    businessBlocks[site.BusinessId] = blockId;
                report.PlacedBusinesses++;
            }

            report.Note("GEO: " + report.PlacedBusinesses + " business sites placed, " +
                        report.UnplacedBusinesses + " unplaced.");
        }

        TerritoryBusinessBinding Resolve(
            TerritoryBusinessSiteRecord site, out TerritoryBlockId blockId)
        {
            if (site.BlockHint.IsValid && blocks.ContainsKey(site.BlockHint))
            {
                blockId = site.BlockHint;
                return TerritoryBusinessBinding.PlanHint;
            }

            if (!site.Footprint.IsEmpty)
            {
                // The block the footprint lies MOST on. A shop unit is smaller than its
                // block, so the winner is unambiguous; a compound that straddles two
                // takes the one it covers more of, and does so identically every run.
                var bestOverlap = 0f;
                var bestArea = float.MaxValue;
                var best = default(TerritoryBlockId);
                for (var i = 0; i < blockIds.Count; i++)
                {
                    var definition = blocks[blockIds[i]];
                    var overlap = definition.WorldBounds.OverlapArea(site.Footprint);
                    if (overlap <= 0f)
                        continue;
                    var area = definition.WorldBounds.Area;
                    if (overlap < bestOverlap || (overlap == bestOverlap && area >= bestArea))
                        continue;
                    bestOverlap = overlap;
                    bestArea = area;
                    best = definition.Id;
                }

                if (best.IsValid)
                {
                    blockId = best;
                    return TerritoryBusinessBinding.Footprint;
                }
            }

            if (TryResolveStanding(site.Approach, default, out blockId))
                return TerritoryBusinessBinding.Approach;

            // The doorstep is on the pavement by construction, so one last reach of half
            // a street inward - the same rule a man standing there gets.
            var nearest = default(TerritoryBlockId);
            var bestDistance = Settings.RoadHysteresis;
            for (var i = 0; i < blockIds.Count; i++)
            {
                var definition = blocks[blockIds[i]];
                var distance = definition.WorldBounds.DistanceTo(site.Approach);
                if (distance > bestDistance)
                    continue;
                if (distance == bestDistance && nearest.IsValid)
                    continue;
                bestDistance = distance;
                nearest = definition.Id;
            }

            if (nearest.IsValid)
            {
                blockId = nearest;
                return TerritoryBusinessBinding.Approach;
            }

            blockId = default;
            return TerritoryBusinessBinding.Unresolved;
        }

        void BuildNeighborhoods()
        {
            var members = new Dictionary<TerritoryNeighborhoodId, List<TerritoryBlockId>>();
            var names = new Dictionary<TerritoryNeighborhoodId, string>();
            var bounds = new Dictionary<TerritoryNeighborhoodId, TerritoryBounds>();
            var any = false;
            var whole = default(TerritoryBounds);

            for (var i = 0; i < blockIds.Count; i++)
            {
                var block = blocks[blockIds[i]];
                whole = any ? TerritoryBounds.Union(whole, block.WorldBounds) : block.WorldBounds;
                any = true;

                if (!block.NeighborhoodId.IsValid)
                {
                    report.Fault("GEO: block '" + block.Id.Value +
                                 "' belongs to no neighborhood.");
                    continue;
                }

                if (!members.TryGetValue(block.NeighborhoodId, out var list))
                {
                    list = new List<TerritoryBlockId>();
                    members.Add(block.NeighborhoodId, list);
                    names.Add(block.NeighborhoodId, block.NeighborhoodName);
                    bounds.Add(block.NeighborhoodId, block.WorldBounds);
                }
                else
                {
                    bounds[block.NeighborhoodId] =
                        TerritoryBounds.Union(bounds[block.NeighborhoodId], block.WorldBounds);
                }

                list.Add(block.Id);
            }

            WorldBounds = whole;

            foreach (var pair in members)
                neighborhoodIds.Add(pair.Key);
            neighborhoodIds.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));

            for (var i = 0; i < neighborhoodIds.Count; i++)
            {
                var id = neighborhoodIds[i];
                var list = members[id];
                list.Sort(CompareBlockIds);

                // Which quarters touch which is READ OFF the block graph rather than
                // declared a second time: one adjacency rule, one answer, so the map and
                // the simulation cannot disagree about whether two quarters meet.
                var adjacent = new List<TerritoryNeighborhoodId>();
                for (var b = 0; b < list.Count; b++)
                {
                    var edges = neighbours[list[b]];
                    for (var e = 0; e < edges.Count; e++)
                    {
                        var other = blocks[edges[e]].NeighborhoodId;
                        if (!other.IsValid || other == id || adjacent.Contains(other))
                            continue;
                        adjacent.Add(other);
                    }
                }

                adjacent.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
                hoods.Add(id, new TerritoryNeighborhoodDefinition(
                    id, names[id], bounds[id], list, adjacent));
            }
        }

        /// <summary>
        /// Block adjacency, measured off the plan's own rectangles: two blocks are
        /// neighbours when they face each other along a shared frontage across a gap no
        /// wider than the city's widest street, or when one stands inside the other (the
        /// nested downtown blocks). Never inferred from ownership, influence or colour.
        /// </summary>
        void BuildNeighbourGraph()
        {
            for (var i = 0; i < blockIds.Count; i++)
                neighbours.Add(blockIds[i], new List<TerritoryBlockId>());

            var edges = 0;
            var nested = 0;
            for (var i = 0; i < blockIds.Count; i++)
            {
                var a = blocks[blockIds[i]];
                for (var j = i + 1; j < blockIds.Count; j++)
                {
                    var b = blocks[blockIds[j]];
                    if (!Faces(a.WorldBounds, b.WorldBounds, Settings, out var overlapping))
                        continue;

                    neighbours[a.Id].Add(b.Id);
                    neighbours[b.Id].Add(a.Id);
                    edges++;
                    if (overlapping)
                        nested++;
                }
            }

            for (var i = 0; i < blockIds.Count; i++)
                neighbours[blockIds[i]].Sort(CompareBlockIds);

            report.Edges = edges;
            report.NestedBlocks = nested;
        }

        /// <summary>The rule, in one place: shared frontage across a street's width, or
        /// overlapping ground. Symmetric by construction - it reads both rectangles and
        /// nothing else - and free of self-edges, which the i&lt;j loop cannot make.</summary>
        public static bool Faces(
            TerritoryBounds a, TerritoryBounds b, TerritoryGeographySettings settings,
            out bool overlapping)
        {
            overlapping = false;
            var overlapX = Math.Min(a.XMax, b.XMax) - Math.Max(a.XMin, b.XMin);
            var overlapZ = Math.Min(a.ZMax, b.ZMax) - Math.Max(a.ZMin, b.ZMin);

            if (overlapX > 0f && overlapZ > 0f)
            {
                // A block standing inside or across another is adjacent to it by ground,
                // not by street. Downtown's nested blocks are the case this exists for.
                overlapping = true;
                return true;
            }

            // The gap on the axis the two are apart on. A pair apart on BOTH axes is
            // diagonal - two corners across a junction - and a junction is not a
            // frontage, so those are never neighbours however close they stand.
            var gapX = -overlapX;
            var gapZ = -overlapZ;

            if (gapX >= 0f && gapX <= settings.NeighbourGap &&
                overlapZ >= settings.MinimumFrontage)
                return true;

            return gapZ >= 0f && gapZ <= settings.NeighbourGap &&
                   overlapX >= settings.MinimumFrontage;
        }

        void Validate()
        {
            report.Blocks = blockIds.Count;
            report.Neighborhoods = neighborhoodIds.Count;

            var isolated = 0;
            for (var i = 0; i < blockIds.Count; i++)
            {
                var id = blockIds[i];
                if (neighbours[id].Count == 0)
                {
                    isolated++;
                    report.Note("GEO: block '" + id.Value + "' has no neighbour - it stands " +
                                "alone across more than " +
                                Settings.NeighbourGap.ToString("0.0") + " m of open ground.");
                }

                var block = blocks[id];
                if (block.WorldBounds.IsEmpty)
                    report.Fault("GEO: block '" + id.Value + "' has an empty world footprint.");
            }

            report.IsolatedBlocks = isolated;

            for (var i = 0; i < neighborhoodIds.Count; i++)
            {
                var hood = hoods[neighborhoodIds[i]];
                if (hood.BlockIds.Count == 0)
                {
                    report.Fault("GEO: neighborhood '" + hood.Id.Value + "' holds no block.");
                    continue;
                }

                var covered = 0f;
                for (var b = 0; b < hood.BlockIds.Count; b++)
                    covered += blocks[hood.BlockIds[b]].WorldBounds.Area;

                var frame = hood.WorldBounds.Area;
                var share = frame > 0f ? covered / frame : 0f;
                report.Note("GEO: neighborhood '" + hood.Name + "' [" + hood.Id.Value + "] " +
                            hood.BlockIds.Count + " blocks, " + (share * 100f).ToString("0") +
                            "% of its frame is block ground (the rest is street).");
            }

            for (var i = 0; i < offGrid.Count; i++)
                report.Note("GEO: off-grid '" + offGrid[i].Name + "' (" + offGrid[i].Kind +
                            ") is NOT territory - " + offGrid[i].Classification + ".");

            // Two blocks sharing ground without one containing the other means the plan
            // handed out the same metre twice; nesting is legitimate and stays a note.
            for (var i = 0; i < blockIds.Count; i++)
            {
                var a = blocks[blockIds[i]].WorldBounds;
                for (var j = i + 1; j < blockIds.Count; j++)
                {
                    var b = blocks[blockIds[j]].WorldBounds;
                    var overlap = a.OverlapArea(b);
                    if (overlap <= 0f)
                        continue;
                    var smaller = Math.Min(a.Area, b.Area);
                    if (smaller > 0f && overlap >= smaller - 0.01f)
                        continue;
                    report.Fault("GEO: blocks '" + blockIds[i].Value + "' and '" +
                                 blockIds[j].Value + "' overlap by " +
                                 overlap.ToString("0.0") + " m² without nesting.");
                }
            }
        }

        static int CompareBlockIds(TerritoryBlockId a, TerritoryBlockId b) =>
            string.CompareOrdinal(a.Value, b.Value);
    }
}
