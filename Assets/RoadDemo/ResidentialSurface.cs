using System;
using System.Collections.Generic;
using System.Linq;

namespace RoadDemo
{
    /// <summary>Deterministic, non-blocking surface dressing for a finished residential lot.
    /// It changes no ground use and books no space; the Unity composer decides how each mark
    /// is rendered. The sparse edge clustering follows the useful part of Palm City's demo
    /// without replacing Polygon City's pavement, road or kerb pieces.</summary>
    public static class ResidentialSurface
    {
        public enum Profile { Clean, LivedIn, Worn }
        public enum Kind { Manhole, RoadPatch, Grate, CrackA, CrackB, Grunge, Paper, Newspaper }

        public sealed class Mark
        {
            public Kind Kind;
            public int I, J;
            public float OffsetX, OffsetZ;
            public int Yaw;
            public float Scale = 1f;
            public int Count = 1;
            public bool Flush;
        }

        public sealed class DetailPlan
        {
            public Profile Wear;
            public List<Mark> Marks = new List<Mark>();
            public int Flush => Marks.Count(mark => mark.Flush);
            public int Clusters => Marks.Count(mark => !mark.Flush);
        }

        public static DetailPlan Lay(ResidentialLot.Plan lot)
        {
            int seed = unchecked(lot.Seed * 486187739 ^ lot.W * 73856093 ^ lot.D * 19349663 ^ 0x51ED270B);
            var rng = new Random(seed);
            int roll = rng.Next(100);
            var result = new DetailPlan
            {
                Wear = roll < 25 ? Profile.Clean : roll < 80 ? Profile.LivedIn : Profile.Worn,
            };

            var flat = Cells(lot, use => Surface(use)).OrderByDescending(cell => Score(lot, cell.I, cell.J))
                .ThenBy(_ => rng.Next()).ToList();
            var tarmac = Cells(lot, use => ResidentialLot.Drives(use))
                .Where(cell => cell.I > 0 && cell.J > 0 && cell.I < lot.W - 1 && cell.J < lot.D - 1)
                .OrderByDescending(cell => Score(lot, cell.I, cell.J)).ThenBy(_ => rng.Next()).ToList();
            var drains = flat.Where(cell => Paved(lot.Ground[cell.I, cell.J]) && ByTarmac(lot, cell.I, cell.J))
                .OrderBy(_ => rng.Next()).ToList();

            if (tarmac.Count > 2) Add(result, tarmac[0], Kind.Manhole, rng, 0.85f, true);
            if (tarmac.Count > 5 && result.Wear != Profile.Clean)
                Add(result, tarmac[Math.Min(2, tarmac.Count - 1)], Kind.RoadPatch, rng, 0.9f, true);
            if (drains.Count > 0) Add(result, drains[0], Kind.Grate, rng, 0.8f, true);

            // A couple of flush threshold marks reinforce entrances while their physical
            // approach remains completely clear.
            foreach (var access in lot.Accesses.Where(one => !one.Vehicle).Take(2))
            {
                var cell = RingCell(lot, access.Side, access.At);
                Add(result, cell, Kind.CrackA, rng, 0.55f, true, edgeOffset: true);
            }

            int flatTarget = result.Wear == Profile.Clean ? 6 : result.Wear == Profile.LivedIn ? 9 : 13;
            flatTarget = Math.Min(flatTarget, Math.Max(4, lot.W * lot.D / 10));
            int cursor = 0;
            while (result.Flush < flatTarget && cursor < flat.Count)
            {
                var cell = flat[cursor++];
                if (result.Marks.Any(mark => mark.Flush && mark.I == cell.I && mark.J == cell.J)) continue;
                Kind kind = rng.Next(2) == 0 ? Kind.CrackA : Kind.CrackB;
                Add(result, cell, kind, rng, Between(rng, 0.55f, 1.05f), true,
                    edgeOffset: Adjacent(lot, cell.I, cell.J, ResidentialLot.Use.Building));
            }

            int clusterTarget = result.Wear == Profile.Clean ? 2 : result.Wear == Profile.LivedIn ? 3 : 4;
            var litter = flat.Where(cell => PhysicalRoom(lot, cell.I, cell.J) &&
                                            (ByService(lot, cell.I, cell.J) ||
                                             Adjacent(lot, cell.I, cell.J, ResidentialLot.Use.Building) ||
                                             Adjacent(lot, cell.I, cell.J, ResidentialLot.Use.Park)))
                             .OrderByDescending(cell => Score(lot, cell.I, cell.J))
                             .ThenBy(_ => rng.Next()).ToList();
            foreach (var cell in litter)
            {
                if (result.Clusters >= clusterTarget) break;
                if (result.Marks.Any(mark => !mark.Flush && Math.Abs(mark.I - cell.I) + Math.Abs(mark.J - cell.J) < 2))
                    continue;
                Add(result, cell, rng.Next(4) == 0 ? Kind.Newspaper : Kind.Paper, rng,
                    Between(rng, 0.85f, 1.1f), false, edgeOffset: true,
                    count: result.Wear == Profile.Worn && rng.Next(2) == 0 ? 2 : 1);
            }
            return result;
        }

        static IEnumerable<(int I, int J)> Cells(ResidentialLot.Plan lot, Func<ResidentialLot.Use, bool> take)
        {
            for (int i = 0; i < lot.W; i++)
                for (int j = 0; j < lot.D; j++)
                    if (take(lot.Ground[i, j])) yield return (i, j);
        }

        static bool Surface(ResidentialLot.Use use) => Paved(use) || ResidentialLot.Drives(use);

        static bool Paved(ResidentialLot.Use use) => use == ResidentialLot.Use.Walkway ||
            use == ResidentialLot.Use.Verge || use == ResidentialLot.Use.Paved ||
            use == ResidentialLot.Use.Yard || use == ResidentialLot.Use.Court;

        static bool PhysicalRoom(ResidentialLot.Plan lot, int i, int j)
        {
            if (!Paved(lot.Ground[i, j]) || lot.Ground[i, j] == ResidentialLot.Use.Court) return false;
            foreach (var access in lot.Accesses)
            {
                var cell = RingCell(lot, access.Side, access.At);
                int keep = access.Vehicle ? 2 : 1;
                if (Math.Abs(cell.I - i) + Math.Abs(cell.J - j) <= keep) return false;
            }
            return true;
        }

        static bool ByTarmac(ResidentialLot.Plan lot, int i, int j) =>
            Neighbours(lot, i, j).Any(cell => ResidentialLot.Drives(lot.Ground[cell.I, cell.J]));

        static bool ByService(ResidentialLot.Plan lot, int i, int j) =>
            Neighbours(lot, i, j).Any(cell => lot.Ground[cell.I, cell.J] == ResidentialLot.Use.Alley ||
                                                lot.Ground[cell.I, cell.J] == ResidentialLot.Use.Parking);

        static bool Adjacent(ResidentialLot.Plan lot, int i, int j, ResidentialLot.Use use) =>
            Neighbours(lot, i, j).Any(cell => lot.Ground[cell.I, cell.J] == use);

        static IEnumerable<(int I, int J)> Neighbours(ResidentialLot.Plan lot, int i, int j)
        {
            for (int side = 0; side < 4; side++)
            {
                int x = i + ResidentialLot.Step[side, 0], y = j + ResidentialLot.Step[side, 1];
                if (x >= 0 && y >= 0 && x < lot.W && y < lot.D) yield return (x, y);
            }
        }

        static int Score(ResidentialLot.Plan lot, int i, int j)
        {
            int score = ByService(lot, i, j) ? 8 : 0;
            if (Adjacent(lot, i, j, ResidentialLot.Use.Building)) score += 6;
            if (Adjacent(lot, i, j, ResidentialLot.Use.Forecourt)) score += 3;
            if (i == 0 || j == 0 || i == lot.W - 1 || j == lot.D - 1) score += 2;
            return score;
        }

        static (int I, int J) RingCell(ResidentialLot.Plan lot, int side, int at) => side switch
        {
            0 => (at, 0),
            2 => (at, lot.D - 1),
            1 => (lot.W - 1, at),
            _ => (0, at),
        };

        static void Add(DetailPlan plan, (int I, int J) cell, Kind kind, Random rng,
                        float scale, bool flush, bool edgeOffset = false, int count = 1)
        {
            float spread = edgeOffset ? 1.45f : 0.85f;
            plan.Marks.Add(new Mark
            {
                Kind = kind,
                I = cell.I,
                J = cell.J,
                OffsetX = Between(rng, -spread, spread),
                OffsetZ = Between(rng, -spread, spread),
                Yaw = rng.Next(4) * 90,
                Scale = scale,
                Flush = flush,
                Count = count,
            });
        }

        static float Between(Random rng, float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
    }
}
