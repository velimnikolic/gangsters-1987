using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// Where the park's boundary hedge goes. Pure geometry - this class instantiates nothing, the
    /// same discipline ParkingLayout is written under and for a sharper reason here: the corner
    /// mitre is the whole difficulty, it is wrong by eight metres when it is wrong, and a rule
    /// that spawns prefabs cannot be checked without a scene. ParkDresser lays FenceRun segments
    /// along whatever this returns.
    ///
    /// The line it runs is the BLOCK LINE, not the cell boundary: BlockBuilder.BlockRect expands
    /// every ordinary block out of its cells and into the road tile as far as the sidewalk
    /// clearance, putting the city's street walls at 7 from a road centreline (10 on the dual
    /// carriageway) - authored figures, both carrying CityGrid.TileScale like everything else in
    /// world space. The park cannot do that with its ground - tile-park carries a Tile whose path
    /// nodes are authored to its own cell, so it is laid one per cell at the city's tile scale
    /// and stops at the cell boundary, a clearance further back. With the hedge on that boundary the
    /// park's visible edge sat 8m behind every facade around it and read as set back from its own
    /// street. The hedge has no such constraint, so it takes the block line and the park lines up.
    ///
    /// Two rules do all the work:
    ///
    ///   1. A side facing a road stands on that road's block line. A side facing the map edge has
    ///      no road tile to stand on and no reach applied by BlockRect either, so it stays just
    ///      inside the cell as it always did.
    ///
    ///   2. A run ends on the PERPENDICULAR side's own line, which is what makes two sides meet
    ///      in an exact mitre. That used to hold by coincidence - every side sat at the same
    ///      inset from the cell edge and every run ended at that same number, so one constant did
    ///      both jobs - and the coincidence dies the moment the sides move out by a per-side
    ///      amount. Where the park carries on along the side there is no corner to mitre and the
    ///      run goes to the cell boundary, which is exactly where the next cell's run starts.
    ///
    /// The gate follows from rule 2 rather than being placed: it sits on the cell's own centre
    /// line, which is the START end's offset along the run and NOT the run's midpoint. The two
    /// are equal only while both ends are, so a gate left on the midpoint drifts off the walk it
    /// exists for as soon as one end of the side is a corner and the other is not.
    /// </summary>
    public static class HedgeLayout
    {
        /// <summary>One straight stretch of hedge, in the parameters FenceRun.Lay takes.</summary>
        public readonly struct Run
        {
            public readonly Vector3 Origin;
            public readonly Vector3 Along;
            public readonly float From;
            public readonly float To;

            public Run(Vector3 origin, Vector3 along, float from, float to)
            {
                Origin = origin;
                Along = along;
                From = from;
                To = to;
            }

            /// <summary>Both ends in world space - what a check on continuity wants to read.</summary>
            public Vector3 Start => Origin + Along * From;
            public Vector3 End => Origin + Along * To;
        }

        /// <summary>The four outward directions, as unit vectors in XZ.</summary>
        static readonly Vector2Int[] Legs =
        {
            new(1, 0), new(0, 1), new(-1, 0), new(0, -1),
        };

        /// <summary>
        /// How far out from a cell centre the hedge stands on the side facing (nx, nz). Against a
        /// road that is the block line - CityGrid.CellSize minus the clearance, i.e. 8m PAST the
        /// cell boundary on an ordinary street - and against anything else, mapEdgeOffset.
        ///
        /// Takes the two clearances rather than reading CityConfig so that ParkDresser can hand
        /// it the very values BlockBuilder.ClearanceFor gives BlockRect. Two callers deriving the
        /// same line separately is the drift BlockRect's own doc warns about.
        /// </summary>
        public static float SideOffset(
            CityGrid grid, int nx, int nz,
            float clearance, float mainClearance, float mapEdgeOffset) =>
            grid.IsRoad(nx, nz)
                ? CityGrid.CellSize - (grid.IsMainRoad(nx, nz) ? mainClearance : clearance)
                : mapEdgeOffset;

        /// <summary>
        /// Every stretch of hedge around a block, gates already cut. Interior edges between two
        /// of the block's own cells produce nothing - the park is one space, not a set of walled
        /// compartments.
        ///
        /// Runs shorter than a segment come back anyway; FenceRun drops them. Filtering here
        /// would hide a gate that had eaten its whole side.
        /// </summary>
        public static List<Run> Plan(
            CityGrid grid,
            List<Vector2Int> cells,
            float clearance,
            float mainClearance,
            float mapEdgeOffset,
            float gateHalf,
            float lift)
        {
            var runs = new List<Run>();
            foreach (var stretch in Stretches(grid, cells, clearance, mainClearance,
                         mapEdgeOffset, lift))
            {
                // The gate is at the cell centre line, GateAlong along the run - see the note
                // on this class. No inset at either end: that lives in mapEdgeOffset now, and
                // applying it twice would reopen the corner it exists to close.
                runs.Add(new Run(stretch.Origin, stretch.Along, 0f, stretch.GateAlong - gateHalf));
                runs.Add(new Run(stretch.Origin, stretch.Along, stretch.GateAlong + gateHalf,
                    stretch.End));
            }

            return runs;
        }

        /// <summary>
        /// The explicit-gate overload: the hedge opens where <paramref name="gates"/> say - the
        /// world-XZ points ParkLayout planned its entrances at - and NOWHERE else. A side no
        /// gate projects onto comes back as one unbroken run; a gate that lands near a run
        /// (within a lane of perpendicular drift, because the layout's conservative interior
        /// line and this cell's own line can disagree by a clearance) cuts its gap at the
        /// projected spot. The cell-centre rule above becomes the legacy behaviour.
        /// </summary>
        public static List<Run> Plan(
            CityGrid grid,
            List<Vector2Int> cells,
            float clearance,
            float mainClearance,
            float mapEdgeOffset,
            IReadOnlyList<Vector2> gates,
            float gateHalf,
            float lift)
        {
            const float PerpendicularTolerance = 4.5f;

            var runs = new List<Run>();
            foreach (var stretch in Stretches(grid, cells, clearance, mainClearance,
                         mapEdgeOffset, lift))
            {
                var cuts = new List<float>();
                if (gates != null)
                    foreach (var gate in gates)
                    {
                        var offset = new Vector2(
                            gate.x - stretch.Origin.x, gate.y - stretch.Origin.z);
                        var alongAxis = new Vector2(stretch.Along.x, stretch.Along.z);
                        var at = Vector2.Dot(offset, alongAxis);
                        var perpendicular = (offset - alongAxis * at).magnitude;
                        if (perpendicular > PerpendicularTolerance)
                            continue;
                        if (at <= 0f || at >= stretch.End)
                            continue;
                        cuts.Add(at);
                    }

                cuts.Sort();
                var from = 0f;
                foreach (var cut in cuts)
                {
                    runs.Add(new Run(stretch.Origin, stretch.Along, from, cut - gateHalf));
                    from = cut + gateHalf;
                }
                runs.Add(new Run(stretch.Origin, stretch.Along, from, stretch.End));
            }

            return runs;
        }

        /// <summary>One exposed side of one cell, before any gate is cut.</summary>
        readonly struct Stretch
        {
            public readonly Vector3 Origin;
            public readonly Vector3 Along;
            public readonly float End;

            /// <summary>The cell's centre line along the run - the legacy gate spot.</summary>
            public readonly float GateAlong;

            public Stretch(Vector3 origin, Vector3 along, float end, float gateAlong)
            {
                Origin = origin;
                Along = along;
                End = end;
                GateAlong = gateAlong;
            }
        }

        static IEnumerable<Stretch> Stretches(
            CityGrid grid,
            List<Vector2Int> cells,
            float clearance,
            float mainClearance,
            float mapEdgeOffset,
            float lift)
        {
            if (cells == null || cells.Count == 0)
                yield break;

            var inBlock = new HashSet<Vector2Int>(cells);
            var cellHalf = CityGrid.CellSize * 0.5f;

            float Offset(Vector2Int at) =>
                SideOffset(grid, at.x, at.y, clearance, mainClearance, mapEdgeOffset);

            foreach (var cell in cells)
            {
                foreach (var leg in Legs)
                {
                    if (inBlock.Contains(cell + leg))
                        continue;

                    var side = new Vector2Int(-leg.y, leg.x);
                    var line = Offset(cell + leg);

                    // Rule 2. The cell boundary where the park continues, the perpendicular
                    // side's own line where it does not.
                    var endPlus = inBlock.Contains(cell + side) ? cellHalf : Offset(cell + side);
                    var endMinus = inBlock.Contains(cell - side) ? cellHalf : Offset(cell - side);

                    var centre = grid.CellToWorld(cell);
                    var origin = new Vector3(
                        centre.x + leg.x * line - side.x * endMinus,
                        lift,
                        centre.z + leg.y * line - side.y * endMinus);

                    var along = new Vector3(side.x, 0f, side.y);

                    yield return new Stretch(origin, along, endMinus + endPlus, endMinus);
                }
            }
        }
    }
}
