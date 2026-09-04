using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>Reads decoration from the fourteen harvested residential prefabs. Every
    /// direct nested prefab is either captured in an observed template or named in the report.</summary>
    public static class ResidentialDecorTable
    {
        public const string TablePath = "Assets/RoadDemo/ResidentialDecor.cs";
        public const string DocumentPath = "Docs/residential-decor-table.md";

        static readonly string[] RequiredFamilies =
        {
            "Aircon", "Billboard", "FireEscape", "LargeSign", "RoofAccess",
            "RoofAircon", "SatelliteDish", "ShopCover", "Sign", "Skylight",
            "Terrace", "Vent", "WashingLine", "Window", "WindowPlanter",
        };

        enum Anchor
        {
            Unbound, Shop, WindowFloor, RoofCell, FireEscapeColumn,
            WashingLine, VentChain, Billboard, Terrace,
        }

        enum Relation
        {
            Single, FireEscapeChain, WashingLinePair, VentChain,
            BillboardPair, TerraceGroup,
        }

        // Kept editor-local so the measurement command does not depend on its own
        // generated ResidentialModules.cs output. Names intentionally mirror the
        // generated ResidentialModuleKind contract.
        enum HostKind
        {
            Unknown, Shop, ShopCorner, ShopCover, Apartment, ApartmentStack,
            ApartmentCorner, ApartmentDoor, Roof, RoofCorner, RoofAccess,
            FireEscape, Decor,
        }

        sealed class Sample
        {
            public string Unit, Family, Prefab, SourceName, Column;
            public int ChildIndex, AnchorFloor, HostStyle;
            public Anchor Anchor;
            public HostKind HostKind;
            public Bounds Box;
            public Vector3 RootPosition, AnchorPosition, RelationPosition;
            public float RootYaw, AnchorYaw, RelationYaw;
            public int RelationFloor;
        }

        sealed class UnitReading
        {
            public string Name;
            public readonly Dictionary<Anchor, int> Denominators = new();
        }

        sealed class ColumnAnchor
        {
            public string Key;
            public Vector3 Position, AttachmentPosition;
            public float Yaw;
            public int Floor;
        }

        sealed class Pair
        {
            public ColumnAnchor First, Second;
        }

        sealed class Member
        {
            public Sample Sample;
            public Vector3 Offset;
            public float Yaw;
            public HostKind HostKind;
            public int HostStyle, ColumnOffset, RowOffset, FloorOffset, Role,
                       ObservedCount = 1;
            public bool Repeat;
        }

        sealed class Template
        {
            public string Variant, Unit, Family;
            public Anchor Anchor;
            public Relation Relation;
            public int Span = 1, Weight = 1;
            public readonly List<Member> Members = new();
        }

        sealed class Rule
        {
            public string Family, Prefab, Variant;
            public Anchor Anchor;
            public Relation Relation;
            public HostKind HostKind;
            public float X, Y, Z, Yaw, Min, Mean, Max,
                         FamilyMin, FamilyMean, FamilyMax, UnboundShare;
            public int HostStyle, Count, Buildings, Part, Parts, Span, ColumnOffset,
                       RowOffset, FloorOffset, Role, VariantWeight;
            public bool Repeat, Ready;
        }

        [CliCommand("gangsters_decor_table",
            "Read the fourteen residential prefabs and write the measured residential decor table.",
            MainThreadRequired = true, Tags = new[] { "gangsters", "residential", "forge" })]
        public static object Generate()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Leave Play mode before measuring residential decor.");

            var samples = new List<Sample>(1024);
            var units = new List<UnitReading>(ResidentialForgeSource.UnitNames.Length);
            var unresolved = new List<string>();
            var unboundByFamily = new Dictionary<string, int>(StringComparer.Ordinal);
            var styleWeights = new int[3];
            var escapeShares = new List<float>();
            int ignored = 0;
            int cornerEscapesIgnored = 0;

            foreach (string unitName in ResidentialForgeSource.UnitNames)
                ReadUnit(unitName, samples, units, unresolved, unboundByFamily,
                         styleWeights, ref ignored, ref cornerEscapesIgnored);

            foreach (UnitReading unit in units)
            {
                var unitSamples = samples.Where(s => s.Unit == unit.Name).ToList();
                int columns = Denominator(unit, Anchor.FireEscapeColumn);
                int occupied = unitSamples.Where(s => s.Family == "FireEscape")
                                          .Select(s => s.Column)
                                          .Distinct(StringComparer.Ordinal).Count();
                if (columns > 0) escapeShares.Add(occupied / (float)columns);
                unit.Denominators[Anchor.WashingLine] = AdjacentEscapePairs(unitSamples).Count;
            }

            List<Template> templates = BuildTemplates(samples, unresolved, unboundByFamily);
            var boundByFamily = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Template template in templates)
                foreach (Member member in template.Members)
                    AddCount(member.Sample.Family, member.ObservedCount, boundByFamily);

            List<Rule> rules = BuildRules(templates, units, boundByFamily, unboundByFamily);
            string[] notReadyFamilies = RequiredFamilies.Where(family =>
                !rules.Any(rule => rule.Family == family && rule.Ready) ||
                UnboundShare(family, boundByFamily, unboundByFamily) > 0.20f).ToArray();
            bool fireReady = templates.Any(t => t.Relation == Relation.FireEscapeChain &&
                                                CompleteFireTemplate(t));
            float escapeMin = escapeShares.Count == 0 ? 0f : escapeShares.Min();
            float escapeMax = escapeShares.Count == 0 ? 0f : escapeShares.Max();

            WriteTable(rules, styleWeights, escapeMin, escapeMax, notReadyFamilies, unresolved);
            WriteDocument(rules, templates, styleWeights, escapeMin, escapeMax,
                          cornerEscapesIgnored, notReadyFamilies, unresolved,
                          boundByFamily, unboundByFamily);
            AssetDatabase.Refresh();

            int bound = boundByFamily.Values.Sum();
            int unbound = unboundByFamily.Values.Sum();
            return new
            {
                passed = units.Count == ResidentialForgeSource.UnitNames.Length &&
                         rules.Count > 0 && fireReady && styleWeights.All(value => value > 0),
                allFamiliesReady = notReadyFamilies.Length == 0,
                units = units.Count,
                props = bound + unbound + ignored,
                bound,
                unbound,
                ignored,
                cornerEscapesIgnored,
                templates = templates.Count,
                rules = rules.Count,
                fireVariants = templates.Count(t => t.Relation == Relation.FireEscapeChain),
                notReadyFamilies,
                familyRates = rules.GroupBy(rule => new
                    {
                        rule.Family,
                        rule.Anchor,
                        rule.Relation,
                    })
                    .Select(group => new
                    {
                        family = group.Key.Family,
                        anchor = group.Key.Anchor.ToString(),
                        relation = group.Key.Relation.ToString(),
                        min = ResidentialForgeSource.Round(group.First().FamilyMin),
                        mean = ResidentialForgeSource.Round(group.First().FamilyMean),
                        max = ResidentialForgeSource.Round(group.First().FamilyMax),
                    })
                    .OrderBy(rate => rate.family, StringComparer.Ordinal)
                    .ThenBy(rate => rate.anchor, StringComparer.Ordinal).ToArray(),
                styleWeights,
                fireEscapeShare = new[]
                {
                    ResidentialForgeSource.Round(escapeMin),
                    ResidentialForgeSource.Round(escapeMax),
                },
                unresolved = unresolved.ToArray(),
                table = TablePath,
                report = DocumentPath,
            };
        }

        static void ReadUnit(
            string unitName, List<Sample> samples, List<UnitReading> units,
            List<string> unresolved, Dictionary<string, int> unboundByFamily,
            int[] styleWeights, ref int ignored, ref int cornerEscapesIgnored)
        {
            GameObject root = null;
            try
            {
                root = ResidentialForgeSource.OpenUnit(unitName);
                List<ResidentialForgeSource.Piece> pieces = ResidentialForgeSource.Pieces(root);
                var shell = pieces.Where(p => ResidentialForgeSource.IsShellAnchor(p.Name)).ToList();
                var reading = new UnitReading { Name = unitName };

                int shops = shell.Where(p => IsShop(p.Name)).Sum(ShopBayCount);
                int windows = shell.Where(p => ResidentialForgeSource.IsApartmentFacade(p.Name))
                                   .Sum(p => ResidentialForgeSource.FloorsFromName(p.Name));
                int roofs = shell.Count(p => IsRoof(p.Name));
                int columns = shell.Where(p => ResidentialForgeSource.IsApartmentFacade(p.Name))
                                   .Where(p => ClassifyHost(p.Name) != HostKind.ApartmentCorner)
                                   .Select(ColumnKey).Distinct(StringComparer.Ordinal).Count();
                reading.Denominators[Anchor.Shop] = shops;
                reading.Denominators[Anchor.WindowFloor] = windows;
                reading.Denominators[Anchor.RoofCell] = roofs;
                reading.Denominators[Anchor.FireEscapeColumn] = columns;
                reading.Denominators[Anchor.VentChain] = roofs;
                reading.Denominators[Anchor.Billboard] = roofs;
                reading.Denominators[Anchor.Terrace] = roofs;
                reading.Denominators[Anchor.Unbound] = 1;

                foreach (var column in shell.Where(p => ResidentialForgeSource.IsApartmentFacade(p.Name))
                                            .GroupBy(ColumnKey).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    // One observed vote per column. The lowest authored module owns the
                    // column's style; a Stack still contributes exactly one vote.
                    int style = column.OrderBy(p => p.Position.y)
                                      .ThenBy(p => p.ChildIndex)
                                      .Select(p => ResidentialForgeSource.SuffixNumber(p.Name))
                                      .FirstOrDefault(value => value >= 1 && value <= 3);
                    if (style >= 1 && style <= 3) styleWeights[style - 1]++;
                }

                foreach (ResidentialForgeSource.Piece piece in pieces)
                {
                    if (string.IsNullOrEmpty(piece.Path)) continue;
                    if (ResidentialForgeSource.IsShellAnchor(piece.Name) || IgnoredBrownstone(piece.Name))
                        continue;
                    if (!IsForgeDecor(piece))
                    {
                        ignored++;
                        ReportIgnored(unitName, piece, "outside the forge decor/module roots", unresolved);
                        continue;
                    }

                    string family = Family(piece.Name);
                    if (family == "Loose")
                    {
                        ignored++;
                        ReportIgnored(unitName, piece, "unsupported Unbound family", unresolved);
                        continue;
                    }

                    Anchor wanted = WantedAnchor(family);
                    var candidates = shell.Where(candidate => Eligible(candidate, wanted, family)).ToList();
                    bool firePart = family == "FireEscape";
                    Vector3 firePosition = firePart ? SnappedFirePosition(piece) : Vector3.zero;
                    float fireYaw = firePart ? SnappedFireYaw(piece) : 0f;
                    ResidentialForgeSource.Piece anchor = firePart
                        ? candidates.OrderBy(candidate => FireHostHorizontal(candidate, firePosition))
                                    .ThenBy(candidate => FireHostFloor(candidate, firePosition.y))
                                    .ThenBy(candidate => FireHostYaw(candidate, fireYaw))
                                    .ThenBy(candidate => ResidentialForgeSource.BoundsDistance(
                                        piece.Box, candidate.Box))
                                    .ThenBy(candidate => candidate.ChildIndex).FirstOrDefault()
                        : candidates.OrderBy(candidate => ResidentialForgeSource.BoundsDistance(
                                                piece.Box, candidate.Box))
                                    .ThenBy(candidate => candidate.Position.y)
                                    .ThenBy(candidate => candidate.ChildIndex).FirstOrDefault();
                    float distance = anchor == null ? float.MaxValue :
                        ResidentialForgeSource.BoundsDistance(piece.Box, anchor.Box);
                    if (anchor == null || distance > 5f)
                    {
                        AddCount(family, 1, unboundByFamily);
                        unresolved.Add($"{unitName} child {piece.ChildIndex}: {piece.Name} ({piece.Path}) could not bind to a supported {family} anchor (nearest {distance:0.###} m)");
                        continue;
                    }

                    Anchor actual = ActualAnchor(wanted, anchor.Name);
                    Vector3 anchorPosition = Round(anchor.Position);
                    int anchorFloor = Mathf.RoundToInt(anchor.Position.y / 3f);
                    if (actual == Anchor.WindowFloor || actual == Anchor.FireEscapeColumn ||
                        actual == Anchor.WashingLine)
                    {
                        int rise = Mathf.Max(1, ResidentialForgeSource.FloorsFromName(anchor.Name));
                        int logical = Mathf.Clamp(
                            Mathf.FloorToInt((piece.Position.y - anchor.Position.y + 0.001f) / 3f),
                            0, rise - 1);
                        anchorPosition.y = ResidentialForgeSource.Round(anchor.Position.y + logical * 3f);
                        anchorFloor += logical;
                    }
                    float anchorYaw = ResidentialForgeSource.NormalYaw(anchor.Yaw);
                    Vector3 relationPosition = firePart ? firePosition : anchorPosition;
                    float relationYaw = firePart ? fireYaw : anchorYaw;
                    int relationFloor = firePart
                        ? Mathf.RoundToInt(firePosition.y / 3f)
                        : anchorFloor;
                    HostKind hostKind = ClassifyHost(anchor.Name);
                    if (firePart && hostKind == HostKind.ApartmentCorner)
                    {
                        ignored++;
                        cornerEscapesIgnored++;
                        unresolved.Add($"{unitName} child {piece.ChildIndex}: {piece.Name} ({piece.Path}) ignored: measured on an ApartmentCorner excluded by the forge's interior-column fire-escape eligibility");
                        continue;
                    }

                    samples.Add(new Sample
                    {
                        Unit = unitName,
                        Family = family,
                        Prefab = piece.Path,
                        SourceName = piece.Name,
                        ChildIndex = piece.ChildIndex,
                        Anchor = actual,
                        Box = piece.Box,
                        RootPosition = Round(piece.Position),
                        RootYaw = ResidentialForgeSource.NormalYaw(piece.Yaw),
                        AnchorPosition = anchorPosition,
                        AnchorYaw = anchorYaw,
                        AnchorFloor = anchorFloor,
                        RelationPosition = relationPosition,
                        RelationYaw = relationYaw,
                        RelationFloor = relationFloor,
                        HostKind = hostKind,
                        HostStyle = HostStyle(anchor.Name),
                        Column = ColumnKey(relationPosition, relationYaw),
                    });
                }
                units.Add(reading);
            }
            catch (Exception ex)
            {
                unresolved.Add($"{unitName}: {ex.GetType().Name}: {ex.Message}");
            }
            finally { ResidentialForgeSource.CloseUnit(root); }
        }

        static List<Template> BuildTemplates(
            List<Sample> samples, List<string> unresolved,
            Dictionary<string, int> unboundByFamily)
        {
            var templates = new List<Template>();
            foreach (var unit in samples.GroupBy(s => s.Unit)
                                        .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                List<Sample> all = unit.OrderBy(s => s.ChildIndex).ToList();
                var escapePairs = AdjacentEscapePairs(all);

                foreach (var group in all.Where(s => s.Family == "FireEscape")
                                         .GroupBy(s => s.Column)
                                         .OrderBy(g => g.Key, StringComparer.Ordinal))
                    AddFireTemplate(group.ToList(), templates, unresolved, unboundByFamily);

                foreach (Sample sample in all.Where(s => s.Family == "WashingLine"))
                {
                    Pair pair = NearestPair(sample, escapePairs);
                    if (pair == null)
                    {
                        Reject(new[] { sample }, "has no measured adjacent fire-escape pair",
                               unresolved, unboundByFamily);
                        continue;
                    }
                    var template = NewTemplate(sample, Relation.WashingLinePair,
                                               $"child-{sample.ChildIndex:000}", Anchor.WashingLine);
                    int columnOffset = HorizontalDistance(sample.AnchorPosition,
                                                          pair.First.Position) <=
                                       HorizontalDistance(sample.AnchorPosition,
                                                          pair.Second.Position) ? 0 : 1;
                    template.Span = 2;
                    template.Members.Add(MemberInFrame(
                        sample, sample.AnchorPosition, sample.AnchorYaw, columnOffset,
                        sample.AnchorFloor - pair.First.Floor));
                    templates.Add(template);
                }

                AddSpatialGroups(all, "Vent", Relation.VentChain, Anchor.VentChain,
                                 0.35f, templates, unresolved, unboundByFamily);
                AddSpatialGroups(all, "Billboard", Relation.BillboardPair, Anchor.Billboard,
                                 0.35f, templates, unresolved, unboundByFamily);
                AddSpatialGroups(all, "Terrace", Relation.TerraceGroup, Anchor.Terrace,
                                 1.5f, templates, unresolved, unboundByFamily);

                foreach (Sample sample in all.Where(s => RelationFor(s.Family) == Relation.Single))
                {
                    var template = NewTemplate(sample, Relation.Single,
                                               $"child-{sample.ChildIndex:000}", sample.Anchor);
                    template.Span = sample.Family == "ShopCover"
                        ? Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(sample.Box.size.x,
                                                                 sample.Box.size.z) / 5f))
                        : 1;
                    template.Members.Add(MemberInOwnFrame(sample));
                    templates.Add(template);
                }
            }
            templates.Sort((a, b) => string.CompareOrdinal(a.Variant, b.Variant));
            return templates;
        }

        static void AddFireTemplate(
            List<Sample> source, List<Template> templates, List<string> unresolved,
            Dictionary<string, int> unboundByFamily)
        {
            if (source.Count < 3)
            {
                Reject(source, "does not contain measured bottom/middle/top members",
                       unresolved, unboundByFamily);
                return;
            }
            // Same geometric classification as the module atlas: shortest is top;
            // from the remainder the greatest measured box volume is bottom.
            Sample top = source.OrderBy(s => s.Box.size.y)
                               .ThenBy(s => s.ChildIndex).First();
            List<Sample> remaining = source.Where(s => s != top).ToList();
            Sample bottom = remaining.OrderByDescending(s =>
                s.Box.size.x * s.Box.size.y * s.Box.size.z)
                .ThenBy(s => s.ChildIndex).First();
            List<Sample> middles = remaining.Where(s => s != bottom).ToList();
            if (middles.Count == 0)
            {
                Reject(source, "does not contain a measured middle member",
                       unresolved, unboundByFamily);
                return;
            }
            var middleGroups = middles.GroupBy(ObservedTransformKey)
                                      .OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
            if (middleGroups.Count != 1)
            {
                Reject(source, "contains non-identical measured middle transforms",
                       unresolved, unboundByFamily);
                return;
            }
            Sample middle = middleGroups[0].OrderBy(s => s.ChildIndex).First();
            var template = NewTemplate(bottom, Relation.FireEscapeChain,
                                       bottom.Column, Anchor.FireEscapeColumn);
            template.Members.Add(MemberInOwnFrame(bottom, 1, false, 1));
            template.Members.Add(MemberInOwnFrame(middle, 2, true, middles.Count));
            template.Members.Add(MemberInOwnFrame(top, 3, false, 1));
            for (int i = 0; i < template.Members.Count; i++)
            {
                Member member = template.Members[i];
                // Fire roles are rebound to their target floor by the forge. Preserve
                // their measured horizontal attachment, but do not bake a source-host
                // floor delta into that second placement step.
                member.Offset = new Vector3(member.Offset.x, 0f, member.Offset.z);
                member.FloorOffset = member.Sample.AnchorFloor - bottom.AnchorFloor;
                member.ColumnOffset = 0;
            }
            templates.Add(template);
        }

        static void AddSpatialGroups(
            List<Sample> all, string family, Relation relation, Anchor anchor,
            float gap, List<Template> templates, List<string> unresolved,
            Dictionary<string, int> unboundByFamily)
        {
            var pending = all.Where(s => s.Family == family)
                             .OrderBy(s => s.ChildIndex).ToList();
            while (pending.Count > 0)
            {
                var source = new List<Sample> { pending[0] };
                pending.RemoveAt(0);
                for (int scan = 0; scan < source.Count; scan++)
                    for (int i = pending.Count - 1; i >= 0; i--)
                        if (ResidentialForgeSource.BoundsDistance(source[scan].Box,
                                                                  pending[i].Box) <= gap)
                        {
                            source.Add(pending[i]);
                            pending.RemoveAt(i);
                        }
                source.Sort((a, b) => a.ChildIndex.CompareTo(b.ChildIndex));
                if (relation == Relation.VentChain && source.Count < 2)
                {
                    Reject(source, "is an orphan vent rather than a measured chain",
                           unresolved, unboundByFamily);
                    continue;
                }
                if (relation == Relation.BillboardPair &&
                    (!source.Any(s => s.SourceName.Contains("Billboard_Roof")) ||
                     !source.Any(s => s.SourceName.Contains("Billboard_Sign"))))
                {
                    Reject(source, "does not contain both measured billboard base and sign",
                           unresolved, unboundByFamily);
                    continue;
                }

                Sample primary = source.OrderBy(s => s.AnchorPosition.x)
                                       .ThenBy(s => s.AnchorPosition.z)
                                       .ThenBy(s => s.ChildIndex).First();
                var template = NewTemplate(primary, relation,
                                           $"cluster-{source[0].ChildIndex:000}", anchor);
                foreach (Sample sample in source)
                {
                    Vector3 gridDelta = Quaternion.Euler(0f, -primary.AnchorYaw, 0f) *
                                        (sample.AnchorPosition - primary.AnchorPosition);
                    int columnOffset = Mathf.RoundToInt(gridDelta.x / 5f);
                    int rowOffset = Mathf.RoundToInt(gridDelta.z / 5f);
                    int floorOffset = Mathf.RoundToInt(gridDelta.y / 3f);
                    Vector3 gridPoint = new(columnOffset * 5f, floorOffset * 3f,
                                            rowOffset * 5f);
                    if ((gridDelta - gridPoint).sqrMagnitude > 0.25f * 0.25f)
                    {
                        Reject(source,
                               "has host anchors that do not share the measured 5 m / 3 m grid",
                               unresolved, unboundByFamily);
                        template = null;
                        break;
                    }
                    template.Members.Add(MemberInFrame(
                        sample, sample.AnchorPosition, sample.AnchorYaw,
                        columnOffset, floorOffset, rowOffset: rowOffset));
                }
                if (template == null) continue;
                SortMembers(template);
                int minColumn = template.Members.Min(member => member.ColumnOffset);
                int maxColumn = template.Members.Max(member => member.ColumnOffset);
                template.Span = Mathf.Max(1, maxColumn - minColumn + 1);
                if (template.Span > 13)
                {
                    Reject(source, $"measured group span {template.Span} exceeds forge maximum 13",
                           unresolved, unboundByFamily);
                    continue;
                }
                templates.Add(template);
            }
        }

        static Template NewTemplate(Sample sample, Relation relation, string token, Anchor anchor) =>
            new()
            {
                Variant = $"{sample.Unit}/{sample.Family}/{token.Replace('/', '_')}",
                Unit = sample.Unit,
                Family = sample.Family,
                Anchor = anchor,
                Relation = relation,
            };

        static Member MemberInOwnFrame(Sample sample, int role = 0,
                                       bool repeat = false, int observed = 1) =>
            MemberInFrame(sample, sample.AnchorPosition, sample.AnchorYaw,
                          0, 0, role, repeat, observed);

        static Member MemberInFrame(
            Sample sample, Vector3 framePosition, float frameYaw,
            int columnOffset, int floorOffset, int role = 0,
            bool repeat = false, int observed = 1, int rowOffset = 0)
        {
            Vector3 offset = Quaternion.Euler(0f, -frameYaw, 0f) *
                             (sample.RootPosition - framePosition);
            return new Member
            {
                Sample = sample,
                Offset = Round(offset),
                Yaw = ResidentialForgeSource.NormalYaw(sample.RootYaw - frameYaw),
                HostKind = sample.HostKind,
                HostStyle = sample.HostStyle,
                ColumnOffset = columnOffset,
                RowOffset = rowOffset,
                FloorOffset = floorOffset,
                Role = role,
                Repeat = repeat,
                ObservedCount = observed,
            };
        }

        static void SortMembers(Template template)
        {
            template.Members.Sort((a, b) =>
            {
                int column = a.ColumnOffset.CompareTo(b.ColumnOffset);
                if (column != 0) return column;
                int row = a.RowOffset.CompareTo(b.RowOffset);
                if (row != 0) return row;
                int floor = a.FloorOffset.CompareTo(b.FloorOffset);
                if (floor != 0) return floor;
                if (template.Relation == Relation.BillboardPair)
                {
                    bool aBase = a.Sample.SourceName.Contains("Billboard_Roof");
                    bool bBase = b.Sample.SourceName.Contains("Billboard_Roof");
                    if (aBase != bBase) return aBase ? -1 : 1;
                }
                int z = a.Offset.z.CompareTo(b.Offset.z);
                if (z != 0) return z;
                int x = a.Offset.x.CompareTo(b.Offset.x);
                if (x != 0) return x;
                return a.Sample.ChildIndex.CompareTo(b.Sample.ChildIndex);
            });
        }

        static List<Rule> BuildRules(
            List<Template> templates, List<UnitReading> units,
            Dictionary<string, int> boundByFamily,
            Dictionary<string, int> unboundByFamily)
        {
            var rules = new List<Rule>();
            var familyRates = new Dictionary<string, float[]>(StringComparer.Ordinal);
            foreach (var bucket in templates.GroupBy(RateBucketKey))
            {
                var rates = new List<float>(units.Count);
                foreach (UnitReading unit in units)
                {
                    int denominator = Denominator(unit, bucket.First().Anchor);
                    int count = bucket.Where(template => template.Unit == unit.Name)
                                      .Sum(template => template.Weight);
                    rates.Add(denominator <= 0 ? 0f : count / (float)denominator);
                }
                familyRates[bucket.Key] = new[]
                {
                    rates.Min(), rates.Average(), rates.Max(),
                };
            }
            foreach (Template template in templates)
            {
                var rates = new List<float>(units.Count);
                foreach (UnitReading unit in units)
                {
                    int denominator = Denominator(unit, template.Anchor);
                    float count = unit.Name == template.Unit ? template.Weight : 0f;
                    rates.Add(denominator <= 0 ? 0f : count / denominator);
                }
                float share = UnboundShare(template.Family, boundByFamily, unboundByFamily);
                float[] familyRate = familyRates[RateBucketKey(template)];
                for (int part = 0; part < template.Members.Count; part++)
                {
                    Member member = template.Members[part];
                    rules.Add(new Rule
                    {
                        Family = template.Family,
                        Prefab = member.Sample.Prefab,
                        Variant = template.Variant,
                        Anchor = template.Anchor,
                        Relation = template.Relation,
                        HostKind = member.HostKind,
                        HostStyle = member.HostStyle,
                        X = member.Offset.x,
                        Y = member.Offset.y,
                        Z = member.Offset.z,
                        Yaw = member.Yaw,
                        Min = rates.Min(),
                        Mean = rates.Average(),
                        Max = rates.Max(),
                        FamilyMin = familyRate[0],
                        FamilyMean = familyRate[1],
                        FamilyMax = familyRate[2],
                        Count = member.ObservedCount,
                        Buildings = rates.Count,
                        Part = part,
                        Parts = template.Members.Count,
                        Span = template.Span,
                        ColumnOffset = member.ColumnOffset,
                        RowOffset = member.RowOffset,
                        FloorOffset = member.FloorOffset,
                        Role = member.Role,
                        Repeat = member.Repeat,
                        VariantWeight = template.Weight,
                        UnboundShare = share,
                        Ready = share <= 0.20f,
                    });
                }
            }
            return rules.OrderBy(r => r.Variant, StringComparer.Ordinal)
                        .ThenBy(r => r.Part).ToList();
        }

        static string RateBucketKey(Template template) =>
            $"{template.Family}\n{template.Anchor}\n{template.Relation}";

        static List<Pair> AdjacentEscapePairs(List<Sample> samples)
        {
            var columns = samples.Where(s => s.Family == "FireEscape")
                .GroupBy(s => s.Column)
                .Select(g =>
                {
                    Sample first = g.OrderBy(s => s.AnchorFloor).ThenBy(s => s.ChildIndex).First();
                    return new ColumnAnchor
                    {
                        Key = g.Key,
                        Position = first.RelationPosition,
                        AttachmentPosition = first.RootPosition,
                        Yaw = first.RelationYaw,
                        Floor = first.RelationFloor,
                    };
                }).OrderBy(c => c.Key, StringComparer.Ordinal).ToList();
            var pairs = new List<Pair>();
            for (int i = 0; i < columns.Count; i++)
                for (int j = i + 1; j < columns.Count; j++)
                {
                    ColumnAnchor first = columns[i], second = columns[j];
                    if (Mathf.Abs(Mathf.DeltaAngle(first.Yaw, second.Yaw)) > 1f) continue;
                    Vector3 local = Quaternion.Euler(0f, -first.Yaw, 0f) *
                                    (second.Position - first.Position);
                    if (Mathf.Abs(Mathf.Abs(local.x) - 5f) > 0.25f ||
                        Mathf.Abs(local.z) > 0.25f) continue;
                    if (local.x < 0f) (first, second) = (second, first);
                    pairs.Add(new Pair { First = first, Second = second });
                }
            return pairs;
        }

        static Pair NearestPair(Sample sample, List<Pair> pairs)
        {
            Pair answer = null;
            float best = float.MaxValue;
            float bestWorstEndpoint = float.MaxValue;
            Vector3 endpointA = sample.RootPosition;
            Vector3 endpointB = sample.RootPosition +
                Quaternion.Euler(0f, sample.RootYaw, 0f) * Vector3.right * 5f;
            foreach (Pair pair in pairs)
            {
                float directA = HorizontalDistance(endpointA, pair.First.AttachmentPosition);
                float directB = HorizontalDistance(endpointB, pair.Second.AttachmentPosition);
                float reverseA = HorizontalDistance(endpointA, pair.Second.AttachmentPosition);
                float reverseB = HorizontalDistance(endpointB, pair.First.AttachmentPosition);
                float direct = directA + directB;
                float reverse = reverseA + reverseB;
                float score = Mathf.Min(direct, reverse);
                float worst = direct <= reverse
                    ? Mathf.Max(directA, directB)
                    : Mathf.Max(reverseA, reverseB);
                if (score < best)
                {
                    best = score;
                    bestWorstEndpoint = worst;
                    answer = pair;
                }
            }
            // The prefab's measured mesh runs almost exactly five metres from its
            // root along local +X. Both ends must land on distinct measured escape
            // anchors; an AABB-nearest match is not evidence of that relation.
            return answer != null && best <= 2f && bestWorstEndpoint <= 1.25f
                ? answer : null;
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        static bool CompleteFireTemplate(Template template) =>
            template.Members.Count == 3 &&
            template.Members.Count(m => m.Role == 1 && !m.Repeat) == 1 &&
            template.Members.Count(m => m.Role == 2 && m.Repeat) == 1 &&
            template.Members.Count(m => m.Role == 3 && !m.Repeat) == 1;

        static void Reject(
            IEnumerable<Sample> source, string reason, List<string> unresolved,
            Dictionary<string, int> unboundByFamily)
        {
            foreach (Sample sample in source)
            {
                AddCount(sample.Family, 1, unboundByFamily);
                unresolved.Add($"{sample.Unit} child {sample.ChildIndex}: {sample.SourceName} ({sample.Prefab}) {reason}");
            }
        }

        static Relation RelationFor(string family) => family switch
        {
            "FireEscape" => Relation.FireEscapeChain,
            "WashingLine" => Relation.WashingLinePair,
            "Vent" => Relation.VentChain,
            "Billboard" => Relation.BillboardPair,
            "Terrace" => Relation.TerraceGroup,
            _ => Relation.Single,
        };

        static bool IsForgeDecor(ResidentialForgeSource.Piece piece) =>
            piece.Path.StartsWith(ResidentialForgeSource.PropsDir + "/", StringComparison.Ordinal) ||
            piece.Name.StartsWith("SM_Bld_FireEscape_", StringComparison.Ordinal) ||
            piece.Name.StartsWith("SM_Bld_Shop_Cover_", StringComparison.Ordinal) ||
            piece.Name == "SM_Bld_Roof_Access_01";

        static bool IgnoredBrownstone(string name) =>
            name.StartsWith("SM_Bld_Apartment_Stairs", StringComparison.Ordinal) ||
            name.StartsWith("SM_Bld_Apartment_Door_Corner_", StringComparison.Ordinal);

        static string Family(string name)
        {
            if (name.StartsWith("SM_Bld_FireEscape_", StringComparison.Ordinal)) return "FireEscape";
            if (name.StartsWith("SM_Prop_Washingline_", StringComparison.Ordinal)) return "WashingLine";
            if (name.StartsWith("SM_Prop_Vents_", StringComparison.Ordinal)) return "Vent";
            if (name.StartsWith("SM_Prop_Billboard_", StringComparison.Ordinal)) return "Billboard";
            if (name.StartsWith("SM_Prop_Roof_Aircon_", StringComparison.Ordinal)) return "RoofAircon";
            if (name.StartsWith("SM_Prop_Aircon_", StringComparison.Ordinal)) return "Aircon";
            if (name.StartsWith("SM_Prop_SatDish_", StringComparison.Ordinal)) return "SatelliteDish";
            if (name.StartsWith("SM_Prop_Skylight_", StringComparison.Ordinal)) return "Skylight";
            if (name.StartsWith("SM_Prop_PlanterWindow_", StringComparison.Ordinal)) return "WindowPlanter";
            if (name.StartsWith("SM_Prop_PowerBox_", StringComparison.Ordinal)) return "PowerBox";
            if (name.StartsWith("SM_Prop_Window_", StringComparison.Ordinal)) return "Window";
            if (name == "SM_Bld_Roof_Access_01") return "RoofAccess";
            if (name.StartsWith("SM_Bld_Shop_Cover_", StringComparison.Ordinal)) return "ShopCover";
            if (name.StartsWith("SM_Prop_LargeSign_", StringComparison.Ordinal)) return "LargeSign";
            if (name.StartsWith("SM_Prop_Sign_", StringComparison.Ordinal)) return "Sign";
            if (name.StartsWith("SM_Prop_Couch_", StringComparison.Ordinal) ||
                name.StartsWith("SM_Prop_Table_", StringComparison.Ordinal) ||
                name.StartsWith("SM_Prop_Planter_", StringComparison.Ordinal) ||
                name.StartsWith("SM_Prop_PotPlant_", StringComparison.Ordinal)) return "Terrace";
            return "Loose";
        }

        static Anchor WantedAnchor(string family) => family switch
        {
            "FireEscape" => Anchor.FireEscapeColumn,
            "WashingLine" => Anchor.WashingLine,
            "Vent" => Anchor.VentChain,
            "Billboard" => Anchor.Billboard,
            "RoofAircon" or "SatelliteDish" or "Skylight" or "RoofAccess" => Anchor.RoofCell,
            "Aircon" or "WindowPlanter" or "Window" or "PowerBox" => Anchor.WindowFloor,
            "ShopCover" => Anchor.Shop,
            "Terrace" => Anchor.Terrace,
            _ => Anchor.Unbound,
        };

        static bool Eligible(ResidentialForgeSource.Piece piece, Anchor wanted, string family)
        {
            if (wanted == Anchor.Shop) return IsShop(piece.Name);
            if (wanted == Anchor.WindowFloor || wanted == Anchor.FireEscapeColumn ||
                wanted == Anchor.WashingLine) return ResidentialForgeSource.IsApartmentFacade(piece.Name);
            if (wanted == Anchor.RoofCell || wanted == Anchor.VentChain ||
                wanted == Anchor.Billboard || wanted == Anchor.Terrace) return IsRoof(piece.Name);
            if (family == "Sign" || family == "LargeSign")
                return IsShop(piece.Name) || IsRoof(piece.Name) ||
                       ResidentialForgeSource.IsApartmentFacade(piece.Name);
            return false;
        }

        static Anchor ActualAnchor(Anchor wanted, string anchorName)
        {
            if (wanted != Anchor.Unbound) return wanted;
            if (IsRoof(anchorName)) return Anchor.RoofCell;
            if (IsShop(anchorName)) return Anchor.Shop;
            return Anchor.WindowFloor;
        }

        static HostKind ClassifyHost(string name)
        {
            if (name.StartsWith("SM_Bld_Shop_Corner_", StringComparison.Ordinal))
                return HostKind.ShopCorner;
            if (name.StartsWith("SM_Bld_Shop_", StringComparison.Ordinal))
                return HostKind.Shop;
            if (name.StartsWith("SM_Bld_Apartment_Roof_Corner_", StringComparison.Ordinal))
                return HostKind.RoofCorner;
            if (name.StartsWith("SM_Bld_Apartment_Roof_", StringComparison.Ordinal))
                return HostKind.Roof;
            if (name.StartsWith("SM_Bld_Apartment_Stack_", StringComparison.Ordinal))
                return HostKind.ApartmentStack;
            if (name.StartsWith("SM_Bld_Apartment_Corner_", StringComparison.Ordinal))
                return HostKind.ApartmentCorner;
            if (name.StartsWith("SM_Bld_Apartment_Door_", StringComparison.Ordinal))
                return HostKind.ApartmentDoor;
            if (name.StartsWith("SM_Bld_Apartment_", StringComparison.Ordinal))
                return HostKind.Apartment;
            return HostKind.Unknown;
        }

        static int HostStyle(string name)
        {
            HostKind kind = ClassifyHost(name);
            if (kind != HostKind.Apartment && kind != HostKind.ApartmentStack &&
                kind != HostKind.ApartmentCorner && kind != HostKind.Roof &&
                kind != HostKind.RoofCorner) return 0;
            int style = ResidentialForgeSource.SuffixNumber(name);
            return style >= 1 && style <= 3 ? style : 0;
        }

        static bool IsShop(string name) =>
            name.StartsWith("SM_Bld_Shop_", StringComparison.Ordinal) &&
            !name.StartsWith("SM_Bld_Shop_Cover_", StringComparison.Ordinal);

        static bool IsRoof(string name) =>
            name.StartsWith("SM_Bld_Apartment_Roof_", StringComparison.Ordinal);

        static int ShopBayCount(ResidentialForgeSource.Piece piece) =>
            Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(piece.Box.size.x, piece.Box.size.z) / 5f));

        static Vector3 SnappedFirePosition(ResidentialForgeSource.Piece piece) => new(
            ResidentialForgeSource.Round(Mathf.Round(piece.Position.x / 5f) * 5f),
            ResidentialForgeSource.Round(Mathf.Round(piece.Position.y / 3f) * 3f),
            ResidentialForgeSource.Round(Mathf.Round(piece.Position.z / 5f) * 5f));

        static float SnappedFireYaw(ResidentialForgeSource.Piece piece) =>
            ResidentialForgeSource.NormalYaw(
                ResidentialForgeSource.Round(Mathf.Round(piece.Yaw / 90f) * 90f));

        static float FireHostHorizontal(
            ResidentialForgeSource.Piece host, Vector3 relationPosition) =>
            HorizontalDistance(host.Position, relationPosition);

        static float FireHostFloor(
            ResidentialForgeSource.Piece host, float relationY)
        {
            int floors = Mathf.Max(1, ResidentialForgeSource.FloorsFromName(host.Name));
            float best = float.MaxValue;
            for (int floor = 0; floor < floors; floor++)
                best = Mathf.Min(best, Mathf.Abs(host.Position.y + floor * 3f - relationY));
            return best;
        }

        static float FireHostYaw(ResidentialForgeSource.Piece host, float relationYaw)
        {
            float best = Mathf.Abs(Mathf.DeltaAngle(host.Yaw, relationYaw));
            if (ClassifyHost(host.Name) == HostKind.ApartmentCorner)
                best = Mathf.Min(best,
                    Mathf.Abs(Mathf.DeltaAngle(host.Yaw + 90f, relationYaw)));
            return best;
        }

        static string ColumnKey(ResidentialForgeSource.Piece piece) =>
            ColumnKey(piece.Position, piece.Yaw);

        static string ColumnKey(Vector3 position, float yaw) =>
            $"{Mathf.Round(position.x * 10f) / 10f:0.0}/" +
            $"{Mathf.Round(position.z * 10f) / 10f:0.0}/" +
            $"{Mathf.Round(yaw):0}";

        static string ObservedTransformKey(Sample sample)
        {
            Member member = MemberInOwnFrame(sample);
            return $"{sample.Prefab}|{sample.HostKind}|{sample.HostStyle}|" +
                   $"{member.Offset.x:0.###}|{member.Offset.y:0.###}|" +
                   $"{member.Offset.z:0.###}|{member.Yaw:0.###}";
        }

        static Vector3 Round(Vector3 value) => new(
            ResidentialForgeSource.Round(value.x),
            ResidentialForgeSource.Round(value.y),
            ResidentialForgeSource.Round(value.z));

        static int Denominator(UnitReading unit, Anchor anchor) =>
            unit.Denominators.TryGetValue(anchor, out int value) ? value : 0;

        static void AddCount(string family, int add, Dictionary<string, int> counts)
        {
            counts.TryGetValue(family, out int count);
            counts[family] = count + add;
        }

        static float UnboundShare(
            string family, Dictionary<string, int> bound,
            Dictionary<string, int> unbound)
        {
            bound.TryGetValue(family, out int yes);
            unbound.TryGetValue(family, out int no);
            return yes + no == 0 ? 0f : no / (float)(yes + no);
        }

        static void ReportIgnored(
            string unitName, ResidentialForgeSource.Piece piece, string reason,
            List<string> unresolved) =>
            unresolved.Add($"{unitName} child {piece.ChildIndex}: {piece.Name} ({piece.Path}) ignored: {reason}");

        static void WriteTable(List<Rule> rules, int[] styles, float escapeMin,
                               float escapeMax, string[] notReadyFamilies,
                               List<string> unresolved)
        {
            var text = new StringBuilder();
            text.AppendLine("// GENERATED by unity command gangsters_decor_table. Do not edit by hand.");
            text.AppendLine();
            text.AppendLine("namespace RoadDemo");
            text.AppendLine("{");
            text.AppendLine("    public enum ResidentialDecorAnchor { Unbound, Shop, WindowFloor, RoofCell, FireEscapeColumn, WashingLine, VentChain, Billboard, Terrace }");
            text.AppendLine("    public enum ResidentialDecorRelation { Single, FireEscapeChain, WashingLinePair, VentChain, BillboardPair, TerraceGroup }");
            text.AppendLine();
            text.AppendLine("    public sealed class ResidentialDecorRule");
            text.AppendLine("    {");
            text.AppendLine("        public string Family, Prefab, Variant;");
            text.AppendLine("        public ResidentialDecorAnchor Anchor;");
            text.AppendLine("        public ResidentialDecorRelation Relation;");
            text.AppendLine("        public ResidentialModuleKind HostKind;");
            text.AppendLine("        public float X, Y, Z, Yaw, Min, Mean, Max, FamilyMin, FamilyMean, FamilyMax;");
            text.AppendLine("        public int HostStyle, Count, Buildings, Part, Parts, Span, ColumnOffset, RowOffset, FloorOffset, Role, VariantWeight;");
            text.AppendLine("        public float UnboundShare;");
            text.AppendLine("        public bool Repeat, Ready;");
            text.AppendLine("    }");
            text.AppendLine();
            text.AppendLine("    public static class ResidentialDecor");
            text.AppendLine("    {");
            text.AppendLine($"        public static readonly int[] StyleWeights = {{ {styles[0]}, {styles[1]}, {styles[2]} }};");
            text.AppendLine($"        public const float FireEscapeShareMin = {ResidentialForgeSource.Float(escapeMin)};");
            text.AppendLine($"        public const float FireEscapeShareMax = {ResidentialForgeSource.Float(escapeMax)};");
            text.AppendLine("        public static readonly ResidentialDecorRule[] All =");
            text.AppendLine("        {");
            foreach (Rule rule in rules)
            {
                text.AppendLine("            new ResidentialDecorRule");
                text.AppendLine("            {");
                text.AppendLine($"                Family = {ResidentialForgeSource.Quote(rule.Family)}, Prefab = {ResidentialForgeSource.Quote(rule.Prefab)}, Variant = {ResidentialForgeSource.Quote(rule.Variant)},");
                text.AppendLine($"                Anchor = ResidentialDecorAnchor.{rule.Anchor}, Relation = ResidentialDecorRelation.{rule.Relation}, HostKind = ResidentialModuleKind.{rule.HostKind}, HostStyle = {rule.HostStyle},");
                text.AppendLine($"                X = {ResidentialForgeSource.Float(rule.X)}, Y = {ResidentialForgeSource.Float(rule.Y)}, Z = {ResidentialForgeSource.Float(rule.Z)}, Yaw = {ResidentialForgeSource.Float(rule.Yaw)},");
                text.AppendLine($"                Min = {ResidentialForgeSource.Float(rule.Min)}, Mean = {ResidentialForgeSource.Float(rule.Mean)}, Max = {ResidentialForgeSource.Float(rule.Max)}, Count = {rule.Count}, Buildings = {rule.Buildings},");
                text.AppendLine($"                FamilyMin = {ResidentialForgeSource.Float(rule.FamilyMin)}, FamilyMean = {ResidentialForgeSource.Float(rule.FamilyMean)}, FamilyMax = {ResidentialForgeSource.Float(rule.FamilyMax)},");
                text.AppendLine($"                Part = {rule.Part}, Parts = {rule.Parts}, Span = {rule.Span}, ColumnOffset = {rule.ColumnOffset}, RowOffset = {rule.RowOffset}, FloorOffset = {rule.FloorOffset}, Role = {rule.Role}, VariantWeight = {rule.VariantWeight},");
                text.AppendLine($"                Repeat = {(rule.Repeat ? "true" : "false")}, UnboundShare = {ResidentialForgeSource.Float(rule.UnboundShare)}, Ready = {(rule.Ready ? "true" : "false")},");
                text.AppendLine("            },");
            }
            text.AppendLine("        };");
            text.AppendLine("        public static readonly string[] NotReady =");
            text.AppendLine("        {");
            foreach (string family in notReadyFamilies)
                text.AppendLine($"            {ResidentialForgeSource.Quote(family)},");
            text.AppendLine("        };");
            text.AppendLine("        public static readonly string[] Unresolved =");
            text.AppendLine("        {");
            foreach (string failure in unresolved.Distinct().OrderBy(s => s, StringComparer.Ordinal))
                text.AppendLine($"            {ResidentialForgeSource.Quote(failure)},");
            text.AppendLine("        };");
            text.AppendLine("    }");
            text.AppendLine("}");
            File.WriteAllText(TablePath, text.ToString().Replace("\r\n", "\n"));
        }

        static void WriteDocument(
            List<Rule> rules, List<Template> templates, int[] styles,
            float escapeMin, float escapeMax, int cornerEscapesIgnored,
            string[] notReadyFamilies,
            List<string> unresolved,
            Dictionary<string, int> bound, Dictionary<string, int> unbound)
        {
            var text = new StringBuilder();
            text.AppendLine("# Residential decor table");
            text.AppendLine();
            text.AppendLine("Generated by `unity command gangsters_decor_table` from the direct nested-prefab children of the fourteen harvested residential units. Every XYZ/yaw tuple below is an observed transform rounded to 0.001 in its member anchor frame; the generator never uses a centroid or averaged transform.");
            text.AppendLine();
            text.AppendLine("Rows with the same `variant` are one atomic measured template. `part/parts` fixes member order; signed column/row/floor offsets select each member's host on the measured 5 m by 5 m by 3 m grid. XYZ/yaw then remain local to that actual host piece pivot/yaw. Host kind/style prevent reuse on an incompatible shell module. Fire-escape role 1/2/3 is bottom/middle/top; only the observed middle member has `repeat=yes`, so it may repeat unchanged on intermediate target floors. Min/mean/max are per-variant rates. Family min/mean/max sum unique templates once per building for the exact family+anchor+relation bucket, divide by that building's eligible anchors, and include all fourteen zero buildings; every member row repeats the same bucket rate.");
            text.AppendLine();
            text.AppendLine("Every ignored or geometrically unbound occurrence remains under Unresolved. Readiness is gated per supported family at no more than 20% unbound; ignored brownstone, environment and unsupported loose street dressing never enters a usable rule.");
            text.AppendLine("A washing-line relation is accepted only when both ends of its measured five-metre local-X mesh meet the actual measured fire-escape attachment roots of two host columns that are adjacent on the facade grid.");
            text.AppendLine();
            text.AppendLine($"Style votes by facade column 01/02/03: **{styles[0]}/{styles[1]}/{styles[2]}**. Fire-escape column share: **{escapeMin:0.###}–{escapeMax:0.###}**. Captured templates: **{templates.Count}**.");
            text.AppendLine($"Fire-escape eligibility matches the forge's interior long-row columns: ApartmentCorner hosts are excluded from both numerator and denominator; **{cornerEscapesIgnored}** such source occurrences were named as ignored-out-of-target.");
            text.AppendLine($"Families not ready for generation: **{(notReadyFamilies.Length == 0 ? "none" : string.Join(", ", notReadyFamilies))}**. A not-ready family remains measured and named below, but is omitted by the forge.");
            text.AppendLine();
            text.AppendLine("| variant | relation | part | family / prefab | host | anchor + offsets | observed transform | variant min/mean/max | family min/mean/max | weight/count | ready |");
            text.AppendLine("|---|---|---:|---|---|---|---|---:|---:|---:|---:|");
            foreach (Rule rule in rules)
                text.AppendLine($"| `{rule.Variant}` | {rule.Relation} | {rule.Part + 1}/{rule.Parts} role {rule.Role}{(rule.Repeat ? " repeat" : "")} | {rule.Family} / `{Path.GetFileNameWithoutExtension(rule.Prefab)}` | {rule.HostKind} style {rule.HostStyle} | {rule.Anchor}; span {rule.Span}; c{rule.ColumnOffset:+0;-0;0} r{rule.RowOffset:+0;-0;0} f{rule.FloorOffset:+0;-0;0} | ({rule.X:0.###},{rule.Y:0.###},{rule.Z:0.###}) / {rule.Yaw:0.###}° | {rule.Min:0.###}/{rule.Mean:0.###}/{rule.Max:0.###} | {rule.FamilyMin:0.###}/{rule.FamilyMean:0.###}/{rule.FamilyMax:0.###} | {rule.VariantWeight}/{rule.Count} | {(rule.Ready ? "yes" : "no")} |");
            text.AppendLine();
            text.AppendLine("## Family binding");
            text.AppendLine();
            foreach (string family in RequiredFamilies.Concat(bound.Keys).Concat(unbound.Keys)
                                           .Distinct(StringComparer.Ordinal)
                                           .OrderBy(s => s, StringComparer.Ordinal))
            {
                bound.TryGetValue(family, out int yes);
                unbound.TryGetValue(family, out int no);
                float share = yes + no == 0 ? 0f : no / (float)(yes + no);
                bool ready = !notReadyFamilies.Contains(family, StringComparer.Ordinal);
                string rates = string.Join("; ", rules.Where(rule => rule.Family == family)
                    .GroupBy(rule => new { rule.Anchor, rule.Relation })
                    .OrderBy(group => group.Key.Anchor)
                    .ThenBy(group => group.Key.Relation)
                    .Select(group =>
                    {
                        Rule first = group.First();
                        return $"{group.Key.Anchor}/{group.Key.Relation} " +
                               $"{first.FamilyMin:0.###}/{first.FamilyMean:0.###}/" +
                               $"{first.FamilyMax:0.###}";
                    }));
                if (string.IsNullOrEmpty(rates)) rates = "no usable rate";
                text.AppendLine($"- {family}: {yes} bound, {no} unbound ({share:P1}) — {(ready ? "ready" : "not ready; forge omits it")}; family min/mean/max: {rates}");
            }
            text.AppendLine();
            text.AppendLine("## Unresolved / ignored source occurrences");
            text.AppendLine();
            if (unresolved.Count == 0) text.AppendLine("None.");
            else foreach (string failure in unresolved.Distinct().OrderBy(s => s, StringComparer.Ordinal))
                text.AppendLine($"- {failure}");
            File.WriteAllText(DocumentPath, text.ToString().Replace("\r\n", "\n"));
        }
    }
}
