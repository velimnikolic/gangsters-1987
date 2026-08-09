using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Tests
{
    /// <summary>
    /// ParkLayout's contract, provable without a scene. Same discipline as HedgeLayoutTests -
    /// a plain static class holding no UnityEngine.Object, returning failures as data, so a
    /// bare .NET host can call Run() by reflection with no Editor and no Play mode. No
    /// Bounds.Contains/Intersects either - those are native ECalls in the bare host - all the
    /// geometry here is hand-rolled over Vector2.
    ///
    /// Assertions are written against the Tuning the plan was built with, not against magic
    /// numbers, so retuning ParkConfig does not require editing them.
    /// </summary>
    public static class ParkLayoutTests
    {
        const float Clearance = 7f * CityGrid.TileScale;
        const float MainClearance = 10f * CityGrid.TileScale;
        const float MapEdge = CityGrid.CellSize * 0.5f - 0.8f;
        const int SpeciesPool = 4;
        const float Eps = 1e-3f;

        public static List<string> Run()
        {
            var failures = new List<string>();
            SamePlanForSameInputs(failures);
            ArchetypesVaryAcrossBlocks(failures);
            EntrancesAreCappedAndOnTheirLines(failures);
            SpinesTerminateOnTheRoadGraphOrEachOther(failures);
            SampleSpacingSuitsTheFollower(failures);
            ACrosswalkAnchorWinsItsSideAndAlignsTheGate(failures);
            SpeciesStayWithinTheCaps(failures);
            TheUnkemptKindsAreInformalOnly(failures);
            GroveIsOneSpeciesInTheScaleBand(failures);
            NothingStandsOnAWalk(failures);
            LampsKeepTheirDistance(failures);
            BenchesFaceTheWalkTheyServe(failures);
            NoTwoStationsOverlap(failures);
            TheLawnExistsAndStaysEmpty(failures);
            DensityStaysInsideTheBounds(failures);
            AnOverTallKindIsDroppedWithAWarning(failures);
            ExplicitHedgeGatesOpenOnlyWhereAsked(failures);
            InvariantSweep(failures);
            return failures;
        }

        // ------------------------------------------------------------------ fixtures

        /// <summary>All road, with the park cells carved out as Block.</summary>
        static CityGrid Roads(int width, int height, params Vector2Int[] park)
        {
            var grid = new CityGrid(width, height);
            for (var x = 0; x < width; x++)
            for (var z = 0; z < height; z++)
                grid[x, z] = CellType.Road;
            foreach (var cell in park)
                grid[cell.x, cell.y] = CellType.Block;
            return grid;
        }

        static List<Vector2Int> Cells(params Vector2Int[] cells) => new(cells);

        static ParkLayout.Plan Plan(
            CityGrid grid, List<Vector2Int> cells, int seed, int blockId,
            ParkLayout.Tuning? tuning = null,
            IReadOnlyList<ParkLayout.EntranceAnchor> anchors = null,
            ParkLayout.Footprint[] footprints = null)
        {
            return ParkLayout.ForBlock(
                grid, cells, Clearance, MainClearance, MapEdge,
                anchors, SpeciesPool, seed, blockId,
                tuning ?? ParkLayout.Tuning.Default, footprints);
        }

        static ParkLayout.Plan OneCell(int seed = 11, int blockId = 3) =>
            Plan(Roads(5, 5, new Vector2Int(2, 2)), Cells(new Vector2Int(2, 2)), seed, blockId);

        static ParkLayout.Plan TwoCell(int seed = 11, int blockId = 3)
        {
            var park = new[] { new Vector2Int(2, 2), new Vector2Int(3, 2) };
            return Plan(Roads(6, 5, park), Cells(park), seed, blockId);
        }

        static IEnumerable<ParkLayout.Plan> Sweep()
        {
            for (var seed = 1; seed <= 8; seed++)
            {
                yield return OneCell(seed, seed * 7);
                yield return TwoCell(seed, seed * 7 + 1);
                var square = new[]
                {
                    new Vector2Int(2, 2), new Vector2Int(3, 2),
                    new Vector2Int(2, 3), new Vector2Int(3, 3),
                };
                yield return Plan(Roads(6, 6, square), Cells(square), seed, seed * 7 + 2);
                var strip = new[]
                {
                    new Vector2Int(1, 2), new Vector2Int(2, 2), new Vector2Int(3, 2),
                };
                yield return Plan(Roads(6, 5, strip), Cells(strip), seed, seed * 7 + 3);
            }
        }

        static bool IsTree(ParkLayout.StationKind kind) =>
            kind is ParkLayout.StationKind.Tree
                or ParkLayout.StationKind.AccentTree
                or ParkLayout.StationKind.DeadTree;

        // ------------------------------------------------------------------ tests

        static void SamePlanForSameInputs(List<string> failures)
        {
            var first = OneCell();
            var second = OneCell();

            if (first.Archetype != second.Archetype)
                failures.Add("determinism: archetype differs between identical calls");
            if (first.Entrances.Count != second.Entrances.Count
                || first.Spines.Count != second.Spines.Count
                || first.Stations.Count != second.Stations.Count)
            {
                failures.Add("determinism: plan shape differs between identical calls");
                return;
            }
            for (var i = 0; i < first.Stations.Count; i++)
                if ((first.Stations[i].Pos - second.Stations[i].Pos).sqrMagnitude > 0f
                    || first.Stations[i].Kind != second.Stations[i].Kind
                    || first.Stations[i].Yaw != second.Stations[i].Yaw)
                {
                    failures.Add($"determinism: station {i} differs between identical calls");
                    return;
                }
        }

        static void ArchetypesVaryAcrossBlocks(List<string> failures)
        {
            var seen = new HashSet<ParkLayout.Archetype>();
            for (var blockId = 0; blockId < 40; blockId++)
                seen.Add(OneCell(5, blockId).Archetype);
            if (seen.Count < 2)
                failures.Add("archetypes: 40 blocks produced a single archetype");
        }

        static void EntrancesAreCappedAndOnTheirLines(List<string> failures)
        {
            var tuning = ParkLayout.Tuning.Default;
            foreach (var plan in Sweep())
            {
                if (plan.Entrances.Count < 1 || plan.Entrances.Count > tuning.maxEntrances)
                    failures.Add($"entrances: count {plan.Entrances.Count} outside [1, {tuning.maxEntrances}]");

                var sides = new HashSet<int>();
                foreach (var entrance in plan.Entrances)
                {
                    if (!sides.Add(entrance.Side))
                        failures.Add($"entrances: two gates on side {entrance.Side}");

                    // The gate stands on its side's hedge line - the interior edge.
                    var expected = entrance.Side switch
                    {
                        0 => plan.Interior.Max.x,
                        1 => plan.Interior.Max.y,
                        2 => plan.Interior.Min.x,
                        _ => plan.Interior.Min.y,
                    };
                    var actual = entrance.Side % 2 == 0 ? entrance.Gate.x : entrance.Gate.y;
                    if (Mathf.Abs(actual - expected) > Eps)
                        failures.Add(
                            $"entrances: side {entrance.Side} gate at {actual:0.###}, line at {expected:0.###}");
                }
            }
        }

        static void SpinesTerminateOnTheRoadGraphOrEachOther(List<string> failures)
        {
            foreach (var plan in Sweep())
            foreach (var spine in plan.Spines)
            {
                if (spine.Points == null || spine.Points.Length < 2)
                {
                    failures.Add("spines: degenerate spine with fewer than 2 points");
                    continue;
                }
                foreach (var end in new[] { spine.Points[0], spine.Points[^1] })
                {
                    if (EndpointAccounted(plan, spine, end))
                        continue;
                    failures.Add(
                        $"spines: {spine.Kind} endpoint ({end.x:0.#},{end.y:0.#}) is neither an anchor, "
                        + "a junction on another spine, nor a closed ring");
                }
            }
        }

        static bool EndpointAccounted(ParkLayout.Plan plan, ParkLayout.Spine spine, Vector2 end)
        {
            foreach (var entrance in plan.Entrances)
                if ((entrance.Anchor - end).magnitude < 0.05f)
                    return true;

            // A ring is its own terminus.
            if (spine.Kind == ParkLayout.SpineKind.PlazaRing
                && (spine.Points[0] - spine.Points[^1]).magnitude < 0.05f)
                return true;

            foreach (var other in plan.Spines)
            {
                if (other.Points == spine.Points)
                    continue;
                if (ParkLayout.DistanceToPolyline(end, other.Points) < 0.25f)
                    return true;
            }
            return false;
        }

        static void SampleSpacingSuitsTheFollower(List<string> failures)
        {
            foreach (var plan in Sweep())
            foreach (var spine in plan.Spines)
                for (var i = 1; i < spine.Points.Length; i++)
                {
                    var spacing = (spine.Points[i] - spine.Points[i - 1]).magnitude;
                    if (spacing > ParkLayout.SampleStep + 0.6f)
                    {
                        failures.Add(
                            $"sampling: {spine.Kind} gap of {spacing:0.##} exceeds the step");
                        return;
                    }
                }
        }

        static void ACrosswalkAnchorWinsItsSideAndAlignsTheGate(List<string> failures)
        {
            var grid = Roads(5, 5, new Vector2Int(2, 2));
            var cells = Cells(new Vector2Int(2, 2));
            var centre = new Vector2(2f * CityGrid.CellSize, 2f * CityGrid.CellSize);
            var boundary = CityGrid.CellSize * 0.5f;

            // Hand the layout a crossing endpoint on the north boundary, alongside the
            // fallback corner anchors - the layout must prefer the side that has it and
            // open the hedge exactly on its line.
            var anchors = new List<ParkLayout.EntranceAnchor>(
                ParkLayout.FallbackAnchors(grid, cells, Clearance, MainClearance))
            {
                new()
                {
                    Pos = new Vector2(centre.x + 1.7f, centre.y + boundary),
                    Side = 1,
                    OnBoundary = true,
                },
            };

            for (var blockId = 0; blockId < 6; blockId++)
            {
                var plan = Plan(grid, cells, 3, blockId, anchors: anchors);
                var found = false;
                foreach (var entrance in plan.Entrances)
                {
                    if (entrance.Side != 1)
                        continue;
                    found = true;
                    if (!entrance.AnchorOnBoundary)
                        failures.Add("crosswalk: north side chose a corner anchor over the crossing");
                    else if (Mathf.Abs(entrance.Gate.x - (centre.x + 1.7f)) > Eps)
                        failures.Add("crosswalk: gate not aligned with the crossing endpoint");
                }
                if (!found)
                    failures.Add($"crosswalk: block {blockId} did not open the crosswalk side");
            }
        }

        static void SpeciesStayWithinTheCaps(List<string> failures)
        {
            foreach (var plan in Sweep())
            {
                if (plan.PrimarySpecies == plan.SecondarySpecies)
                    failures.Add("species: primary and secondary collapsed to one slot");
                if (plan.AccentSpecies == plan.PrimarySpecies
                    || plan.AccentSpecies == plan.SecondarySpecies)
                    failures.Add("species: accent collides with a main slot");

                int trees = 0, accents = 0;
                foreach (var station in plan.Stations)
                {
                    if (station.Kind == ParkLayout.StationKind.Tree)
                    {
                        trees++;
                        if (station.SpeciesSlot is not (0 or 1))
                            failures.Add("species: a Tree station outside slots 0/1");
                    }
                    if (station.Kind == ParkLayout.StationKind.AccentTree)
                        accents++;
                }
                if (trees > 0 && accents > Mathf.FloorToInt(trees * ParkLayout.Tuning.Default.accentShare) + 1)
                    failures.Add($"species: {accents} accents against {trees} trees breaks the share");
            }
        }

        static void TheUnkemptKindsAreInformalOnly(List<string> failures)
        {
            foreach (var plan in Sweep())
            {
                var dead = 0;
                foreach (var station in plan.Stations)
                {
                    var unkempt = station.Kind is ParkLayout.StationKind.DeadTree
                        or ParkLayout.StationKind.Boulder
                        or ParkLayout.StationKind.Knoll
                        or ParkLayout.StationKind.Carousel;
                    if (unkempt && plan.Archetype != ParkLayout.Archetype.Informal)
                        failures.Add($"unkempt: {station.Kind} in a {plan.Archetype} park");
                    if (station.Kind == ParkLayout.StationKind.DeadTree)
                        dead++;
                }
                if (dead > ParkLayout.Tuning.Default.maxDeadTrees)
                    failures.Add($"unkempt: {dead} dead trees over the cap");
            }
        }

        static void GroveIsOneSpeciesInTheScaleBand(List<string> failures)
        {
            var tuning = ParkLayout.Tuning.Default;
            foreach (var plan in Sweep())
            {
                ParkLayout.Rect? grove = null;
                foreach (var zone in plan.Zones)
                    if (zone.Kind == ParkLayout.ZoneKind.Grove)
                        grove = zone.Area;
                if (!grove.HasValue)
                    continue;

                foreach (var station in plan.Stations)
                {
                    if (station.Kind != ParkLayout.StationKind.Tree)
                        continue;
                    if (!grove.Value.Contains(station.Pos))
                        continue;
                    if (station.Scale < tuning.treeScaleMin - Eps
                        || station.Scale > tuning.treeScaleMax + Eps)
                        failures.Add($"grove: tree scale {station.Scale:0.##} outside the band");
                    if (station.SpeciesSlot != 0)
                        failures.Add("grove: a non-primary species inside the grove");
                }
            }
        }

        static void NothingStandsOnAWalk(List<string> failures)
        {
            foreach (var plan in Sweep())
            foreach (var station in plan.Stations)
            {
                if (!IsTree(station.Kind))
                    continue;
                foreach (var spine in plan.Spines)
                {
                    var clearance = ParkLayout.DistanceToPolyline(station.Pos, spine.Points);
                    if (clearance < spine.Width * 0.5f + station.Radius - Eps)
                    {
                        failures.Add(
                            $"walks: a {station.Kind} stands {clearance:0.##} from a "
                            + $"{spine.Kind} needing {spine.Width * 0.5f + station.Radius:0.##}");
                        return;
                    }
                }
            }
        }

        static void LampsKeepTheirDistance(List<string> failures)
        {
            var tuning = ParkLayout.Tuning.Default;
            foreach (var plan in Sweep())
            {
                var lamps = new List<Vector2>();
                foreach (var station in plan.Stations)
                    if (station.Kind == ParkLayout.StationKind.Lamp)
                        lamps.Add(station.Pos);

                for (var i = 0; i < lamps.Count; i++)
                for (var j = i + 1; j < lamps.Count; j++)
                    if ((lamps[i] - lamps[j]).magnitude < tuning.lampMinSeparation - Eps)
                    {
                        failures.Add("lamps: a pair closer than the minimum separation");
                        return;
                    }
            }
        }

        static void BenchesFaceTheWalkTheyServe(List<string> failures)
        {
            foreach (var plan in Sweep())
            foreach (var station in plan.Stations)
            {
                if (station.Kind != ParkLayout.StationKind.Bench)
                    continue;

                // Within reach of a walk - PedestrianAgent's opportunity range is 9m - and
                // facing it: the yaw vector must point back toward the nearest walkable thing.
                var facing = new Vector2(
                    Mathf.Sin(station.Yaw * Mathf.Deg2Rad),
                    Mathf.Cos(station.Yaw * Mathf.Deg2Rad));

                var toPlaza = plan.PlazaCentre - station.Pos;
                if (plan.PlazaRadius > 0f && toPlaza.magnitude < plan.PlazaRadius + 3f)
                {
                    if (Vector2.Dot(facing, toPlaza.normalized) < 0.9f)
                        failures.Add("benches: a plaza bench does not face the plaza");
                    continue;
                }

                var best = float.MaxValue;
                Vector2 nearest = default;
                foreach (var spine in plan.Spines)
                {
                    foreach (var point in spine.Points)
                    {
                        var d = (point - station.Pos).magnitude;
                        if (d >= best)
                            continue;
                        best = d;
                        nearest = point;
                    }
                }
                if (best > 9f)
                {
                    failures.Add($"benches: {best:0.#}m from the nearest walk, out of reach");
                    continue;
                }
                if (best > 0.5f && Vector2.Dot(facing, (nearest - station.Pos).normalized) < 0.3f)
                    failures.Add("benches: a path bench faces away from its walk");
            }
        }

        static void NoTwoStationsOverlap(List<string> failures)
        {
            foreach (var plan in Sweep())
                for (var i = 0; i < plan.Stations.Count; i++)
                for (var j = i + 1; j < plan.Stations.Count; j++)
                {
                    var a = plan.Stations[i];
                    var b = plan.Stations[j];
                    if ((a.Pos - b.Pos).magnitude < a.Radius + b.Radius - Eps)
                    {
                        failures.Add(
                            $"overlap: {a.Kind} and {b.Kind} at "
                            + $"{(a.Pos - b.Pos).magnitude:0.##} < {a.Radius + b.Radius:0.##}");
                        return;
                    }
                }
        }

        static void TheLawnExistsAndStaysEmpty(List<string> failures)
        {
            var lawns = 0;
            foreach (var plan in Sweep())
            {
                ParkLayout.Rect? lawn = null;
                foreach (var zone in plan.Zones)
                    if (zone.Kind == ParkLayout.ZoneKind.Lawn)
                        lawn = zone.Area;
                if (!lawn.HasValue)
                    continue;
                lawns++;

                foreach (var station in plan.Stations)
                    if (lawn.Value.Contains(station.Pos, -0.5f))
                    {
                        failures.Add($"lawn: a {station.Kind} stands on the open lawn");
                        return;
                    }
            }
            if (lawns == 0)
                failures.Add("lawn: no plan in the sweep produced an open lawn");
        }

        static void DensityStaysInsideTheBounds(List<string> failures)
        {
            var tuning = ParkLayout.Tuning.Default;
            foreach (var plan in Sweep())
            {
                var hundreds = plan.Interior.Area / 100f;
                var max = Mathf.Min(
                    Mathf.RoundToInt(tuning.densityMaxPer100 * hundreds), tuning.maxStations);
                var min = Mathf.RoundToInt(tuning.densityMinPer100 * hundreds);

                if (plan.Stations.Count > max)
                    failures.Add($"density: {plan.Stations.Count} stations over the cap {max}");

                var ranDry = false;
                foreach (var warning in plan.Warnings)
                    ranDry |= warning.Contains("density fill ran dry");
                if (plan.Stations.Count < min && !ranDry)
                    failures.Add(
                        $"density: {plan.Stations.Count} under the floor {min} with no warning");
            }
        }

        static void AnOverTallKindIsDroppedWithAWarning(List<string> failures)
        {
            var footprints = ParkLayout.DefaultFootprints();
            var lamp = footprints[(int)ParkLayout.StationKind.Lamp];
            lamp.Height = 99f;
            footprints[(int)ParkLayout.StationKind.Lamp] = lamp;

            var grid = Roads(5, 5, new Vector2Int(2, 2));
            var plan = Plan(grid, Cells(new Vector2Int(2, 2)), 11, 3, footprints: footprints);

            foreach (var station in plan.Stations)
                if (station.Kind == ParkLayout.StationKind.Lamp)
                {
                    failures.Add("height: a 99m lamp survived the sanity pass");
                    return;
                }
            var warned = false;
            foreach (var warning in plan.Warnings)
                warned |= warning.Contains("Lamp");
            if (!warned)
                failures.Add("height: the dropped kind produced no warning");
        }

        static void ExplicitHedgeGatesOpenOnlyWhereAsked(List<string> failures)
        {
            var grid = Roads(5, 5, new Vector2Int(2, 2));
            var cells = Cells(new Vector2Int(2, 2));
            const float GateHalf = 3f;
            const float Lift = 0.02f;

            // No gates: four unbroken runs, one per side.
            var closed = HedgeLayout.Plan(grid, cells, Clearance, MainClearance, MapEdge,
                new List<Vector2>(), GateHalf, Lift);
            if (closed.Count != 4)
                failures.Add($"hedge gates: gateless ring came back as {closed.Count} runs, not 4");
            var closedLength = 0f;
            foreach (var run in closed)
                closedLength += run.To - run.From;

            // One gate on the north hedge line, off-centre: exactly one side splits, the gap
            // is exactly the gate, and nothing else moves.
            var centre = grid.CellToWorld(2, 2);
            var northLine = centre.z + CityGrid.CellSize - Clearance;
            var gate = new Vector2(centre.x + 5f, northLine);
            var gated = HedgeLayout.Plan(grid, cells, Clearance, MainClearance, MapEdge,
                new List<Vector2> { gate }, GateHalf, Lift);

            if (gated.Count != 5)
                failures.Add($"hedge gates: one gate produced {gated.Count} runs, not 5");
            var gatedLength = 0f;
            foreach (var run in gated)
                gatedLength += run.To - run.From;
            if (Mathf.Abs(closedLength - gatedLength - 2f * GateHalf) > Eps)
                failures.Add(
                    $"hedge gates: gap removed {closedLength - gatedLength:0.###}m of hedge, "
                    + $"expected {2f * GateHalf}");

            // The two cut ends stand exactly gateHalf either side of the gate point.
            var endsAtGap = 0;
            foreach (var run in gated)
            foreach (var end in new[] { run.Start, run.End })
            {
                var d = new Vector2(end.x - gate.x, end.z - gate.y).magnitude;
                if (Mathf.Abs(d - GateHalf) < Eps)
                    endsAtGap++;
                else if (d < GateHalf - Eps)
                    failures.Add("hedge gates: a run reaches inside the gate opening");
            }
            if (endsAtGap != 2)
                failures.Add($"hedge gates: {endsAtGap} run ends on the gap edges, expected 2");
        }

        static void InvariantSweep(List<string> failures)
        {
            // The whole sweep already runs each invariant; this one checks the plan is usable
            // at all - a park with entrances must have spines, and every spine must be inside
            // the world the interior describes (with the road-side tails allowed out to the
            // anchors).
            foreach (var plan in Sweep())
            {
                if (plan.Entrances.Count == 0)
                {
                    failures.Add("sweep: a road-ringed park planned no entrances");
                    continue;
                }
                if (plan.Spines.Count == 0)
                    failures.Add("sweep: a park with entrances planned no spines");

                var roomy = new ParkLayout.Rect(
                    plan.Interior.Min - new Vector2(15f, 15f),
                    plan.Interior.Max + new Vector2(15f, 15f));
                foreach (var spine in plan.Spines)
                foreach (var point in spine.Points)
                    if (!roomy.Contains(point))
                    {
                        failures.Add("sweep: a spine escapes the block entirely");
                        return;
                    }
            }
        }
    }
}
