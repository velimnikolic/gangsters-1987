using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Pure paper-side choice of which generated remainder parcels become amenities. The
    /// layout is deliberately separate from the scene composition: the same seed and raster
    /// always choose the same bounded set, and tests can judge the cap without loading a
    /// prefab. CoreRoads still owns every cell for topology; this class only assigns its view.
    /// </summary>
    public static class CoreAmenityLayout
    {
        const int MinimumParkingBays = 6;
        // The Core station is the exact shared ResidentialDemo block: full-size PumpDemo
        // forecourt plus the same two-cell pavement ring as every generated city block.
        public const float FuelFrontage = FuelStationBlock.BlockFrontage;
        public const float FuelDepth = FuelStationBlock.BlockDepth;

        public sealed class Site
        {
            public readonly Rect Box;
            public readonly ParkingEntrySide Entry;
            public readonly int Cells;

            public Site(Rect box, ParkingEntrySide entry, int cells)
            {
                Box = box;
                Entry = entry;
                Cells = cells;
            }
        }

        /// <summary>Select fuel first so a parking cap cannot consume the only parcel deep
        /// enough for PumpDemo's store, canopy and back-of-house dressing.</summary>
        public static void Select(
            CoreRoads.Raster raster, IEnumerable<Rect> plannedLots, int seed,
            int parkingCount, int fuelCount,
            List<Site> parking, List<Site> fuel, List<Site> development = null,
            IEnumerable<Rect> housing = null, List<Rect> repurposed = null)
        {
            parking.Clear();
            fuel.Clear();
            development?.Clear();
            repurposed?.Clear();
            if (raster == null || plannedLots == null) return;

            // Keep the original whole-lot candidates for parking/development. Fuel reserves
            // an exact full FuelStationBlock footprint against a road-facing edge; any ground
            // left in that source rectangle remains CoreRoads' ordinary painted parking.
            var lots = new List<Rect>(plannedLots);
            var candidates = Candidates(raster, lots);
            // Some residential-yard remainders are described as an L made from two
            // rectangles. A cross street cuts through that L in the accepted raster, so
            // neither source rectangle is entirely Parking and the old all-or-nothing
            // candidate filter silently discarded both. Recover the actual rectangular
            // parking runs so the large outer ones may become housing; shallow runs remain
            // the raster's ordinary painted parking rather than becoming fake frontages.
            var supplementalDevelopment = SupplementalDevelopment(raster, lots, candidates);
            var fuelCandidates = new List<Site>(candidates);
            fuelCandidates.AddRange(supplementalDevelopment);
            var housingCandidates = new List<Site>();
            if (housing != null)
                foreach (var box in housing)
                    housingCandidates.Add(new Site(box, ParkingEntrySide.South, 0));
            fuelCandidates.AddRange(housingCandidates);
            var used = new HashSet<Site>();
            for (int i = 0; i < fuelCount; i++)
            {
                var next = PickFuel(
                    raster, fuelCandidates, used, fuel, parking,
                    seed + i * 104729, out var source);
                if (next == null) break;
                used.Add(source);
                fuel.Add(next);
                if (housingCandidates.Contains(source))
                {
                    repurposed?.Add(source.Box);
                    MarkParking(raster, source.Box);
                }
            }
            for (int i = 0; i < parkingCount; i++)
            {
                var next = PickParking(candidates, used, fuel, parking, seed + i * 7919);
                if (next == null) break;
                used.Add(next);
                parking.Add(next);
            }

            if (development != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                    if (!used.Contains(candidates[i]) && CanCarryHousing(candidates[i]))
                        development.Add(candidates[i]);
                for (int i = 0; i < supplementalDevelopment.Count; i++)
                    if (!used.Contains(supplementalDevelopment[i]) &&
                        CanCarryHousing(supplementalDevelopment[i]))
                        development.Add(supplementalDevelopment[i]);
            }
        }

        public static void MarkParking(CoreRoads.Raster raster, Rect box)
        {
            CellBounds(raster, box, out int x0, out int z0, out int x1, out int z1);
            for (int x = x0; x < x1; x++)
                for (int z = z0; z < z1; z++)
                    if (raster.At(x, z) == CoreRoads.Kind.Block)
                        raster.Kinds[x, z] = CoreRoads.Kind.Parking;
        }

        /// <summary>Metres of frontage and depth a courthouse and its forecourt want.
        /// The building measures 20.1 x 17.6 m (SyntyKitExtractor's own bake report), so
        /// this is it plus about five metres of court all round: the floor a parcel has
        /// to clear before it is considered at all.</summary>
        public const float CourthouseFrontage = 30f;
        public const float CourthouseDepth = 28f;

        /// <summary>The complete shared block: a 42 m combined hall/quarters shell inside
        /// a 50 m frontage, with 35 m depth for the working apron and parked appliances.</summary>
        public const float FireStationFrontage = FireStationBlock.BlockFrontage;
        public const float FireStationDepth = FireStationBlock.BlockDepth;

        /// <summary>
        /// THE PARCEL THE COURTHOUSE TAKES (GAN-237), or null when nothing will hold one.
        ///
        /// The city needed a civic building the prisoner transfer could actually drive to,
        /// and it takes a leftover parcel the same way the filling stations do: downtown
        /// first, then the roomiest that clears the floor above - a court on the rim would
        /// be a court nobody drives past. The parcel is REMOVED from the development list,
        /// so it does not also become housing.
        ///
        /// Nothing big enough means no court, and the transfer keeps driving out of town
        /// on both legs: a leg does not pretend to arrive somewhere nobody built.
        /// </summary>
        public static Site PickCourthouse(List<Site> development, CoreTerritoryPlan territory)
        {
            if (development == null || development.Count == 0) return null;

            Site best = null;
            var bestArea = 0f;
            var bestDowntown = false;
            for (int i = 0; i < development.Count; i++)
            {
                var site = development[i];
                if (site.Box.width < CourthouseFrontage || site.Box.height < CourthouseDepth)
                    continue;
                var downtown = QuarterOf(territory, site.Box.center) == CoreQuarterId.Downtown;
                var area = site.Box.width * site.Box.height;
                if (best != null && bestDowntown && !downtown) continue;
                if (best != null && downtown == bestDowntown && area <= bestArea) continue;
                best = site;
                bestArea = area;
                bestDowntown = downtown;
            }

            if (best != null) development.Remove(best);
            return best;
        }

        /// <summary>
        /// Reserve one road-facing fire station from the development pool. The smallest
        /// suitable source wins so the civic building does not consume an 85 m housing lot
        /// when a 50 x 35 m remainder already fits it. On an oversized source only the exact
        /// road-edge footprint becomes the station; the balance remains ordinary raster
        /// parking rather than being falsely reported as station or housing ground.
        /// </summary>
        public static Site PickFireStation(List<Site> development)
        {
            if (development == null || development.Count == 0) return null;

            Site source = null;
            float bestArea = float.MaxValue;
            for (int i = 0; i < development.Count; i++)
            {
                var candidate = development[i];
                Dimensions(candidate.Box, candidate.Entry, out float frontage, out float depth);
                if (frontage + 0.01f < FireStationFrontage ||
                    depth + 0.01f < FireStationDepth)
                    continue;

                float area = candidate.Box.width * candidate.Box.height;
                if (source != null && area >= bestArea) continue;
                source = candidate;
                bestArea = area;
            }

            if (source == null) return null;
            development.Remove(source);
            var box = FireStationFootprint(source);
            int cells = Mathf.RoundToInt(box.width * box.height /
                                         (CoreLayout.Cell * CoreLayout.Cell));
            return new Site(box, source.Entry, cells);
        }

        /// <summary>The exact 50 x 35 m crop, held against the source parcel's served edge.</summary>
        public static Rect FireStationFootprint(Site source)
        {
            if (source == null) return default;
            var box = source.Box;
            switch (source.Entry)
            {
                case ParkingEntrySide.North:
                    return new Rect(
                        box.center.x - FireStationFrontage * 0.5f,
                        box.yMax - FireStationDepth,
                        FireStationFrontage, FireStationDepth);
                case ParkingEntrySide.East:
                    return new Rect(
                        box.xMax - FireStationDepth,
                        box.center.y - FireStationFrontage * 0.5f,
                        FireStationDepth, FireStationFrontage);
                case ParkingEntrySide.West:
                    return new Rect(
                        box.xMin,
                        box.center.y - FireStationFrontage * 0.5f,
                        FireStationDepth, FireStationFrontage);
                default:
                    return new Rect(
                        box.center.x - FireStationFrontage * 0.5f,
                        box.yMin,
                        FireStationFrontage, FireStationDepth);
            }
        }

        /// <summary>Which quarter a point falls in, or the nearest one's - the same
        /// reading CanCarryHousing makes, lifted out so the courthouse pick can make it
        /// too. Downtown is the answer where there is no territory to ask.</summary>
        static CoreQuarterId QuarterOf(CoreTerritoryPlan territory, Vector2 at)
        {
            var direct = territory?.QuarterAt(at);
            if (direct.HasValue) return direct.Value;
            if (territory == null || territory.Quarters.Count == 0)
                return CoreQuarterId.Downtown;

            var best = CoreQuarterId.Downtown;
            float nearest = float.MaxValue;
            for (int i = 0; i < territory.Quarters.Count; i++)
            {
                var candidate = territory.Quarters[i];
                float distance = (candidate.LocalAnchor - at).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                best = candidate.Id;
            }
            return best;
        }

        /// <summary>A development parcel must preserve the shared two-cell pavement ring.
        /// When territory is supplied, Downtown is additionally protected because its
        /// harvested Core prefabs already own that authored ground.</summary>
        public static bool CanCarryHousing(Site site, CoreTerritoryPlan territory = null)
        {
            if (site == null || !FitsResidential(site.Box.width, site.Box.height)) return false;
            var quarter = territory?.QuarterAt(site.Box.center);
            if (!quarter.HasValue && territory != null && territory.Quarters.Count > 0)
            {
                float nearest = float.MaxValue;
                for (int i = 0; i < territory.Quarters.Count; i++)
                {
                    var candidate = territory.Quarters[i];
                    float distance = (candidate.LocalAnchor - site.Box.center).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance;
                    quarter = candidate.Id;
                }
            }
            return !quarter.HasValue || quarter.Value != CoreQuarterId.Downtown;
        }

        readonly struct Run : System.IEquatable<Run>
        {
            public readonly int I0;
            public readonly int I1;

            public Run(int i0, int i1) { I0 = i0; I1 = i1; }
            public bool Equals(Run other) => I0 == other.I0 && I1 == other.I1;
            public override bool Equals(object obj) => obj is Run other && Equals(other);
            public override int GetHashCode() => unchecked(I0 * 397 ^ I1);
        }

        /// <summary>
        /// Returns the real parking rectangles inside planned lots which the legacy
        /// whole-rectangle filter could not see. Horizontal runs with the same span are
        /// joined vertically, so a 75 x 40 m city block remains one residential parcel
        /// instead of eight five-metre strips.
        /// </summary>
        static List<Site> SupplementalDevelopment(
            CoreRoads.Raster raster, IReadOnlyList<Rect> plannedLots,
            IReadOnlyList<Site> wholeCandidates)
        {
            var parking = new bool[raster.NX, raster.NZ];
            for (int n = 0; n < plannedLots.Count; n++)
            {
                CellBounds(raster, plannedLots[n], out int i0, out int j0,
                           out int i1, out int j1);
                for (int i = i0; i < i1; i++)
                    for (int j = j0; j < j1; j++)
                        if (raster.At(i, j) == CoreRoads.Kind.Parking)
                            parking[i, j] = true;
            }

            // Whole candidates are already assigned below to fuel, retained parking or
            // ordinary development. Only recover ground not owned by one of them.
            for (int n = 0; n < wholeCandidates.Count; n++)
            {
                CellBounds(raster, wholeCandidates[n].Box, out int i0, out int j0,
                           out int i1, out int j1);
                for (int i = i0; i < i1; i++)
                    for (int j = j0; j < j1; j++)
                        parking[i, j] = false;
            }

            var found = new List<Site>();
            var active = new Dictionary<Run, int>();
            var row = new List<Run>();
            var close = new List<Run>();
            for (int j = 0; j <= raster.NZ; j++)
            {
                row.Clear();
                if (j < raster.NZ)
                {
                    int i = 0;
                    while (i < raster.NX)
                    {
                        while (i < raster.NX && !parking[i, j]) i++;
                        int i0 = i;
                        while (i < raster.NX && parking[i, j]) i++;
                        if (i0 < i) row.Add(new Run(i0, i));
                    }
                }

                close.Clear();
                foreach (var pair in active)
                    if (!row.Contains(pair.Key)) close.Add(pair.Key);
                close.Sort((a, b) => a.I0 != b.I0 ? a.I0.CompareTo(b.I0) : a.I1.CompareTo(b.I1));
                for (int n = 0; n < close.Count; n++)
                {
                    var run = close[n];
                    AddSupplement(raster, run.I0, active[run], run.I1, j, found);
                    active.Remove(run);
                }
                foreach (var run in row)
                    if (!active.ContainsKey(run)) active.Add(run, j);
            }
            return found;
        }

        static void AddSupplement(CoreRoads.Raster raster, int i0, int j0, int i1, int j1,
                                  List<Site> into)
        {
            if (i1 <= i0 || j1 <= j0) return;
            // Record the best serving side for a run that proves large enough for housing.
            // A shallow run stays ordinary parking; CoreRoads independently reads which
            // edge meets the street when it lays those painted bays.
            RoadEntry(raster, i0, j0, i1, j1, out var entry);
            var box = Rect.MinMaxRect(raster.X(i0), raster.Z(j0),
                                      raster.X(i1), raster.Z(j1));
            into.Add(new Site(box, entry, (i1 - i0) * (j1 - j0)));
        }

        static void CellBounds(CoreRoads.Raster raster, Rect requested,
                               out int i0, out int j0, out int i1, out int j1)
        {
            i0 = Mathf.Clamp(Mathf.RoundToInt((requested.xMin - raster.X0) / CoreRoads.Cell),
                             0, raster.NX);
            i1 = Mathf.Clamp(Mathf.RoundToInt((requested.xMax - raster.X0) / CoreRoads.Cell),
                             0, raster.NX);
            j0 = Mathf.Clamp(Mathf.RoundToInt((requested.yMin - raster.Z0) / CoreRoads.Cell),
                             0, raster.NZ);
            j1 = Mathf.Clamp(Mathf.RoundToInt((requested.yMax - raster.Z0) / CoreRoads.Cell),
                             0, raster.NZ);
        }

        static List<Site> Candidates(CoreRoads.Raster raster, IEnumerable<Rect> plannedLots)
        {
            var found = new List<Site>();
            var seen = new HashSet<string>();
            foreach (var requested in plannedLots)
            {
                int i0 = Mathf.RoundToInt((requested.xMin - raster.X0) / CoreRoads.Cell);
                int i1 = Mathf.RoundToInt((requested.xMax - raster.X0) / CoreRoads.Cell);
                int j0 = Mathf.RoundToInt((requested.yMin - raster.Z0) / CoreRoads.Cell);
                int j1 = Mathf.RoundToInt((requested.yMax - raster.Z0) / CoreRoads.Cell);
                if (i0 < 0 || j0 < 0 || i1 > raster.NX || j1 > raster.NZ || i1 <= i0 || j1 <= j0)
                    continue;

                bool parking = true;
                for (int i = i0; i < i1 && parking; i++)
                    for (int j = j0; j < j1; j++)
                        if (raster.At(i, j) != CoreRoads.Kind.Parking) { parking = false; break; }
                if (!parking) continue;

                string key = $"{i0}:{j0}:{i1}:{j1}";
                if (!seen.Add(key)) continue;
                if (!RoadEntry(raster, i0, j0, i1, j1, out var entry)) continue;
                var box = Rect.MinMaxRect(raster.X(i0), raster.Z(j0),
                                          raster.X(i1), raster.Z(j1));
                found.Add(new Site(box, entry, (i1 - i0) * (j1 - j0)));
            }
            return found;
        }

        static bool RoadEntry(
            CoreRoads.Raster raster, int i0, int j0, int i1, int j1,
            out ParkingEntrySide entry)
        {
            int south = 0, east = 0, north = 0, west = 0;
            for (int i = i0; i < i1; i++)
            {
                if (ServedByRoad(raster.At(i, j0 - 1))) south++;
                if (ServedByRoad(raster.At(i, j1))) north++;
            }
            for (int j = j0; j < j1; j++)
            {
                if (ServedByRoad(raster.At(i1, j))) east++;
                if (ServedByRoad(raster.At(i0 - 1, j))) west++;
            }

            entry = ParkingEntrySide.South;
            int best = south;
            if (east > best) { best = east; entry = ParkingEntrySide.East; }
            if (north > best) { best = north; entry = ParkingEntrySide.North; }
            if (west > best) { best = west; entry = ParkingEntrySide.West; }
            return best > 0;
        }

        static bool ServedByRoad(CoreRoads.Kind kind)
        {
            switch (kind)
            {
                case CoreRoads.Kind.Bare:
                case CoreRoads.Kind.LaneEW:
                case CoreRoads.Kind.LaneNS:
                case CoreRoads.Kind.NarrowEW:
                case CoreRoads.Kind.NarrowNS:
                case CoreRoads.Kind.StreetEW:
                case CoreRoads.Kind.StreetNS:
                case CoreRoads.Kind.BlvdEW:
                case CoreRoads.Kind.BlvdNS:
                    return true;
                default:
                    return false;
            }
        }

        static Site PickFuel(
            CoreRoads.Raster raster, List<Site> candidates, HashSet<Site> used,
            List<Site> fuel, List<Site> parking, int seed, out Site source)
        {
            source = null;
            Site best = null;
            double bestScore = double.MinValue;
            foreach (var candidate in candidates)
            {
                if (used.Contains(candidate) ||
                    !TryFuelFootprint(raster, candidate, out var site) ||
                    Overlaps(site.Box, fuel) || Overlaps(site.Box, parking))
                    continue;

                if (CoreServicePlan.RoadWidth(raster, site.Box, site.Entry) < 2) continue;
                bool tooClose = fuel.Exists(other =>
                    Vector2.Distance(site.Box.center, other.Box.center) < 400f);
                if (tooClose) continue;

                // Prefer the source that wastes the least former parking ground, then spread
                // multiple stations instead of packing identical blocks side by side.
                double waste = candidate.Box.width * candidate.Box.height -
                               site.Box.width * site.Box.height;
                double score = -waste + 5000 * CoreServicePlan.RoadWidth(raster, site.Box, site.Entry);
                double distance = NearestDistance(site, fuel, parking);
                if (distance > 0d) score += distance * 0.04d;
                uint tie = unchecked((uint)(seed * 486187739 ^
                    Mathf.RoundToInt(site.Box.xMin) * 73856093 ^
                    Mathf.RoundToInt(site.Box.yMin) * 19349663));
                score += tie / (double)uint.MaxValue;
                if (score <= bestScore) continue;
                best = site;
                source = candidate;
                bestScore = score;
            }
            return best;
        }

        static Site PickParking(
            List<Site> candidates, HashSet<Site> used,
            List<Site> fuel, List<Site> parking, int seed)
        {
            Site best = null;
            double bestScore = double.MinValue;
            foreach (var site in candidates)
            {
                if (used.Contains(site) || !FitsParking(site) ||
                    Overlaps(site.Box, fuel) || Overlaps(site.Box, parking))
                    continue;

                double score = site.Box.width * site.Box.height;
                double distance = NearestDistance(site, fuel, parking);
                if (distance > 0d) score += distance * 0.04d;
                uint tie = unchecked((uint)(seed * 486187739 ^
                    Mathf.RoundToInt(site.Box.xMin) * 73856093 ^
                    Mathf.RoundToInt(site.Box.yMin) * 19349663));
                score += tie / (double)uint.MaxValue;
                if (score <= bestScore) continue;
                best = site;
                bestScore = score;
            }
            return best;
        }

        /// <summary>Crop the exact 60 x 55 m shared fuel block out of a larger parking
        /// remainder. Its 60 m frontage is aligned to one uninterrupted road edge, so both
        /// generated mouths open onto the same Core carriageway.</summary>
        static bool TryFuelFootprint(CoreRoads.Raster raster, Site source, out Site footprint)
        {
            footprint = null;
            if (raster == null || source == null) return false;

            var order = new[]
            {
                source.Entry,
                ParkingEntrySide.South,
                ParkingEntrySide.East,
                ParkingEntrySide.North,
                ParkingEntrySide.West,
            };
            int tried = 0;
            for (int i = 0; i < order.Length; i++)
            {
                var side = order[i];
                int bit = 1 << (int)side;
                if ((tried & bit) != 0) continue;
                tried |= bit;
                if (TryFuelFootprint(raster, source.Box, side, out footprint))
                    return true;
            }
            return false;
        }

        static bool TryFuelFootprint(
            CoreRoads.Raster raster, Rect source, ParkingEntrySide side, out Site footprint)
        {
            footprint = null;
            CellBounds(raster, source, out int i0, out int j0, out int i1, out int j1);
            int frontage = Mathf.RoundToInt(FuelFrontage / CoreRoads.Cell);
            int depth = Mathf.RoundToInt(FuelDepth / CoreRoads.Cell);
            bool horizontal = side == ParkingEntrySide.South || side == ParkingEntrySide.North;
            int availableFrontage = horizontal ? i1 - i0 : j1 - j0;
            int availableDepth = horizontal ? j1 - j0 : i1 - i0;
            if (availableFrontage < frontage || availableDepth < depth ||
                !TryRoadWindow(raster, i0, j0, i1, j1, side, frontage, out int start))
                return false;

            int fi0, fj0, fi1, fj1;
            switch (side)
            {
                case ParkingEntrySide.North:
                    fi0 = start; fi1 = start + frontage;
                    fj0 = j1 - depth; fj1 = j1;
                    break;
                case ParkingEntrySide.East:
                    fi0 = i1 - depth; fi1 = i1;
                    fj0 = start; fj1 = start + frontage;
                    break;
                case ParkingEntrySide.West:
                    fi0 = i0; fi1 = i0 + depth;
                    fj0 = start; fj1 = start + frontage;
                    break;
                default:
                    fi0 = start; fi1 = start + frontage;
                    fj0 = j0; fj1 = j0 + depth;
                    break;
            }

            var box = Rect.MinMaxRect(
                raster.X(fi0), raster.Z(fj0), raster.X(fi1), raster.Z(fj1));
            footprint = new Site(box, side, frontage * depth);
            return true;
        }

        static bool TryRoadWindow(
            CoreRoads.Raster raster, int i0, int j0, int i1, int j1,
            ParkingEntrySide side, int needed, out int windowStart)
        {
            bool horizontal = side == ParkingEntrySide.South || side == ParkingEntrySide.North;
            int from = horizontal ? i0 : j0;
            int to = horizontal ? i1 : j1;
            int ideal = (from + to - needed) / 2;
            int bestRun = -1;
            int bestDistance = int.MaxValue;
            windowStart = 0;

            int at = from;
            while (at < to)
            {
                while (at < to && !RoadBeside(raster, side, at, i0, j0, i1, j1)) at++;
                int run0 = at;
                while (at < to && RoadBeside(raster, side, at, i0, j0, i1, j1)) at++;
                int run = at - run0;
                if (run < needed) continue;

                int start = Mathf.Clamp(ideal, run0, at - needed);
                int distance = Mathf.Abs((start * 2 + needed) - (from + to));
                if (run < bestRun || (run == bestRun && distance >= bestDistance)) continue;
                bestRun = run;
                bestDistance = distance;
                windowStart = start;
            }
            return bestRun >= needed;
        }

        static bool RoadBeside(
            CoreRoads.Raster raster, ParkingEntrySide side, int along,
            int i0, int j0, int i1, int j1)
        {
            switch (side)
            {
                case ParkingEntrySide.North: return ServedByRoad(raster.At(along, j1));
                case ParkingEntrySide.East: return ServedByRoad(raster.At(i1, along));
                case ParkingEntrySide.West: return ServedByRoad(raster.At(i0 - 1, along));
                default: return ServedByRoad(raster.At(along, j0 - 1));
            }
        }

        static bool Overlaps(Rect box, IReadOnlyList<Site> sites)
        {
            if (sites == null) return false;
            for (int i = 0; i < sites.Count; i++)
                if (box.Overlaps(sites[i].Box)) return true;
            return false;
        }

        static double NearestDistance(Site site, List<Site> fuel, List<Site> parking)
        {
            double nearest = double.MaxValue;
            foreach (var other in fuel)
                nearest = System.Math.Min(nearest, (site.Box.center - other.Box.center).sqrMagnitude);
            foreach (var other in parking)
                nearest = System.Math.Min(nearest, (site.Box.center - other.Box.center).sqrMagnitude);
            return nearest == double.MaxValue ? 0d : nearest;
        }

        static bool FitsResidential(float width, float depth)
        {
            int w = Mathf.RoundToInt(width / CoreLayout.Cell);
            int d = Mathf.RoundToInt(depth / CoreLayout.Cell);
            return ResidentialLot.Classify(
                w - 2 * ResidentialLot.Walk, d - 2 * ResidentialLot.Walk) != null;
        }

        static bool FitsParking(Site site)
        {
            Dimensions(site.Box, site.Entry, out float width, out float depth);
            return ParkingBlockPlan.Generate(width, depth).Stalls.Count >= MinimumParkingBays;
        }

        static void Dimensions(Rect box, ParkingEntrySide entry, out float width, out float depth)
        {
            bool side = entry == ParkingEntrySide.East || entry == ParkingEntrySide.West;
            width = side ? box.height : box.width;
            depth = side ? box.width : box.height;
        }

        public static bool Contains(IReadOnlyList<Site> sites, Vector2 point)
        {
            if (sites == null) return false;
            for (int i = 0; i < sites.Count; i++)
                if (sites[i].Box.Contains(point)) return true;
            return false;
        }

        /// <summary>The exact full-size FuelStationBlock footprint, including its generated
        /// pavement ring. CoreRoads skips only this crop; any larger source remainder stays
        /// ordinary city parking.</summary>
        public static Rect FuelSurface(Site site) => site.Box;

        public static float FuelParcelDepth(Site site)
        {
            Dimensions(site.Box, site.Entry, out _, out float depth);
            return depth;
        }

        public static float FuelParcelFrontage(Site site)
        {
            Dimensions(site.Box, site.Entry, out float frontage, out _);
            return frontage;
        }

        /// <summary>Cycle through all three accepted ParkingDemo programmes where their
        /// parcel fits: public, urban-block, then long-stay.</summary>
        public static ParkingBlockStyle ParkingStyle(Site site, int index)
        {
            var first = index % 3 == 0 ? ParkingBlockStyle.Attended
                      : index % 3 == 1 ? ParkingBlockStyle.UrbanBlock
                                       : ParkingBlockStyle.LongStay;
            foreach (var style in new[]
            {
                first,
                ParkingBlockStyle.Attended,
                ParkingBlockStyle.LongStay,
                ParkingBlockStyle.UrbanBlock,
            })
            {
                var surface = ParkingBlockSite.Surface(site.Box, style);
                Dimensions(surface, site.Entry, out float width, out float depth);
                if (ParkingBlockPlan.Generate(width, depth).Stalls.Count >= MinimumParkingBays)
                    return style;
            }
            return ParkingBlockStyle.Attended;
        }

        /// <summary>Place FuelStationBlock's preview rectangle exactly over its reserved Core
        /// footprint. The block's local -Z edge is its road frontage.</summary>
        public static void FuelBlockPose(Site site, out Vector3 position, out int yaw)
        {
            switch (site.Entry)
            {
                case ParkingEntrySide.North:
                    yaw = 180;
                    break;
                case ParkingEntrySide.East:
                    yaw = 270;
                    break;
                case ParkingEntrySide.West:
                    yaw = 90;
                    break;
                default:
                    yaw = 0;
                    break;
            }

            var preview = FuelStationBlock.PreviewBounds;
            var localCentre = new Vector3(preview.center.x, 0f, preview.center.y);
            var turnedCentre = Quaternion.Euler(0f, yaw, 0f) * localCentre;
            position = new Vector3(site.Box.center.x, 0f, site.Box.center.y) - turnedCentre;
        }

        /// <summary>Place FireStationBlock's centred local footprint over its reserved crop,
        /// turning its +Z facade toward the road-serving side.</summary>
        public static void FireStationPose(Site site, out Vector3 position, out int yaw)
        {
            switch (site.Entry)
            {
                case ParkingEntrySide.North: yaw = 0; break;
                case ParkingEntrySide.East: yaw = 90; break;
                case ParkingEntrySide.South: yaw = 180; break;
                default: yaw = 270; break;
            }

            var localCentre = new Vector3(
                FireStationBlock.PreviewBounds.center.x, 0f,
                FireStationBlock.PreviewBounds.center.y);
            var turnedCentre = Quaternion.Euler(0f, yaw, 0f) * localCentre;
            position = new Vector3(site.Box.center.x, 0f, site.Box.center.y) - turnedCentre;
        }
    }
}
