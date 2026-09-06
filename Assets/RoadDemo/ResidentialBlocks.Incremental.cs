using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static RoadDemo.Composer;

namespace RoadDemo
{
    public static partial class ResidentialBlocks
    {
        /// <summary>
        /// A residential bind that can be advanced against the recycler's frame budget.
        /// The ordinary Compose path remains for editor baking and hidden first-frame
        /// warmup; camera-driven binds use this path so one generated block is no longer
        /// one indivisible 50 ms Update.
        /// </summary>
        public sealed class IncrementalComposition : IDisposable
        {
            readonly IEnumerator _steps;
            bool _disposed;

            internal IncrementalComposition(IEnumerator steps, Stood result)
            {
                _steps = steps;
                Result = result;
            }

            public Stood Result { get; }
            public bool Complete { get; private set; }

            /// <summary>Advance one bounded placement. False means the bind is complete.</summary>
            public bool Step()
            {
                if (Complete || _disposed) return false;
                try
                {
                    if (_steps.MoveNext()) return true;
                    Complete = true;
                    Dispose();
                    return false;
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                (_steps as IDisposable)?.Dispose();
            }
        }

        public static IncrementalComposition ComposeIncremental(
            ResidentialLot.Plan plan, Transform root, System.Random rng,
            Func<GameObject, Transform, GameObject> raise)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (raise == null) throw new ArgumentNullException(nameof(raise));
            var stood = new Stood();
            return new IncrementalComposition(ComposeIncrementalBody(plan, root, rng, raise, stood), stood);
        }

        static IEnumerator ComposeIncrementalBody(
            ResidentialLot.Plan plan, Transform root, System.Random rng,
            Func<GameObject, Transform, GameObject> raise, Stood stood)
        {
            Begin(raise);
            ForgetMissing();

            var cafes = new List<(ResidentialLot.Gap Gap, CafeSpot Spot)>();
            var cafeSpots = new List<CafeSpot>();
            if (plan.Cafes.Count > 0)
            {
                for (int i = 0; i < plan.Cafes.Count; i++)
                {
                    var gap = plan.Cafes[i];
                    var spot = CafeOf(plan, gap, rng, stood);
                    if (spot != null) { cafes.Add((gap, spot)); cafeSpots.Add(spot); }
                    yield return null;
                }
            }
            else if (plan.Cafe != null)
            {
                var spot = CafeOf(plan, plan.Cafe, rng, stood);
                if (spot != null) { cafes.Add((plan.Cafe, spot)); cafeSpots.Add(spot); }
                yield return null;
            }

            ReserveBusinessAccess(plan);
            var ring = Ring(plan, rng);
            var kerbs = new List<CorePavement.Kerbstone>();
            var stalls = new List<Stall>();
            var standing = new List<Vector3>();
            yield return null;

            // Amenity backing, one tile per step.
            float cell = ResidentialLot.Cell;
            foreach (var amenity in plan.Spots)
            {
                if (amenity.Unit.Kind != ResidentialKind.Amenity) continue;
                float below = AmenityBackingHeight(amenity.Unit);
                var turn = ResidentialLot.Turn.Of(amenity.Unit, amenity.Yaw);
                for (int u = 0; u < turn.CW; u++)
                    for (int v = 0; v < turn.CD; v++)
                    {
                        if (Lay(Paving, root, (amenity.I + u) * cell, (amenity.J + v) * cell,
                                cell, cell, 0f, below) != null) stood.Tiles++;
                        yield return null;
                    }
            }

            var laid = new bool[plan.W, plan.D];
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    GroundCell(plan, root, cafeSpots, ring, kerbs, stalls, laid, i, j, stood);
                    yield return null;
                }

            CaryardParking(plan, root, raise, stalls);
            yield return null;
            CaryardVenueArrow(plan, root);
            yield return null;

            int nth = 0;
            foreach (var spot in plan.Spots)
            {
                int mix = unchecked((plan.W * 73856093) ^ (plan.D * 19349663) ^
                                    (spot.I * 83492791) ^ (spot.J * 486187739) ^
                                    (nth++ * 2038074743));
                int colourway = ResidentialUnits.IsLot(spot.Unit)
                    ? 0
                    : ((mix % 3) + 3) % 3;
                bool dressStorefront = NeedsStorefrontDressing(spot.Unit);
                var go = StandUnit(spot.Unit, spot.Yaw, spot.I, spot.J, root, colourway,
                                   dressStorefront);
                if (go != null)
                {
                    if (ResidentialUnits.IsLot(spot.Unit)) stood.Parks++;
                    else stood.Units++;
                }
                yield return null;
                if (go != null && dressStorefront)
                {
                    int interiorSeed = StorefrontSeed(
                        plan.Seed, spot.Unit.Name, spot.I, spot.J, spot.Yaw);
                    foreach (int _ in StorefrontDressingSteps(
                        go, spot.Unit, "unit:" + spot.Unit.Name,
                        interiorSeed, null, stood))
                        yield return null;
                }
            }

            Subway(plan, root, stood);
            yield return null;
            int cafeNth = 0;
            foreach (var placed in cafes)
            {
                var cafe = CafeStand(placed.Spot, root, stood);
                yield return null;
                if (cafe != null)
                {
                    // As on the eager path: only a harvested storefront takes the shallow
                    // rooms. A kit venue on its own ground keeps its authored front.
                    if (NeedsStorefrontDressing(placed.Spot.Unit))
                    {
                        int interiorSeed = StorefrontSeed(
                            plan.Seed, placed.Spot.Name, placed.Gap.At, placed.Gap.Side, cafeNth++);
                        Vector3 outward = CafeLocalOutward(placed.Gap, root, cafe.transform);
                        foreach (int _ in StorefrontDressingSteps(
                            cafe, placed.Spot.Unit,
                            "cafe:" + (placed.Spot.Path ?? placed.Spot.Unit?.Name ?? placed.Spot.Name),
                            interiorSeed, outward, stood))
                            yield return null;
                    }
                    else if (placed.Spot.Unit != null)
                    {
                        BuildingCutaway.Prepare(cafe, placed.Spot.Unit);
                        yield return null;
                    }

                    if ((placed.Spot.Unit?.Seats ?? 0) < OwnSeats)
                    {
                        Patio(plan, placed.Gap, placed.Spot, root, rng, standing, stood);
                        yield return null;
                        Terraces(plan, placed.Gap, placed.Spot, root, rng, standing, stood);
                    }
                }
                yield return null;
            }

            PlazaClusters(plan, root, standing, stood);
            yield return null;
            Courtyard(plan, root, rng, standing, stood);
            yield return null;
            SharedYards(plan, root, rng, standing, stood);
            yield return null;

            stood.Stalls = stalls.Count;
            if (stalls.Count > 0)
            {
                var parked = new GameObject("Parked").transform;
                parked.SetParent(root, false);
                foreach (var stall in stalls)
                {
                    if (Chance(rng, Parked))
                    {
                        var prefab = CoreRoads.PickCar(rng);
                        if (prefab != null)
                        {
                            var car = raise(prefab, parked);
                            if (car != null)
                            {
                                car.transform.SetPositionAndRotation(
                                    stall.At, Quaternion.Euler(0f, stall.Into, 0f));
                                CoreRoads.InBay(car, stall.At, stall.Into, stall.Depth);
                                stood.Cars++;
                            }
                        }
                    }
                    yield return null;
                }
            }

            if (Dressed)
            {
                Dress(plan, root, rng, stood);
                yield return null;
                Yards(plan, root, rng, stood);
                yield return null;
                Plazas(plan, root, rng, stood);
                yield return null;
            }

            MainPlazaTables(plan, root, standing, stood);
            yield return null;
            Lamps(plan, root, standing, stood);
            yield return null;
            PavementEssentials(plan, root, standing, stood);
            yield return null;
            if (Dressed)
            {
                Street(plan, root, rng, standing, stood);
                yield return null;
            }
            Palms(plan, kerbs, standing, root, raise, rng.Next(), stood);
            yield return null;

            foreach (int n in ResidentialLandscaping.Compose(plan, root, raise))
            { stood.Props += n; yield return null; }

            var details = ResidentialSurface.Lay(plan);
            stood.SurfaceProfile = details.Wear.ToString();
            var detailRoot = new GameObject($"Surface details ({details.Wear})").transform;
            detailRoot.SetParent(root, false);
            int detailIndex = 0;
            foreach (var mark in details.Marks)
            {
                float x = (mark.I + 0.5f) * ResidentialLot.Cell + mark.OffsetX;
                float z = (mark.J + 0.5f) * ResidentialLot.Cell + mark.OffsetZ;
                bool made = mark.Kind switch
                {
                    // URP builds a private mesh whenever a DecalProjector enters the
                    // scene. That is fine in an authored/baked block, but camera-driven
                    // recycling produced dozens of new meshes and a visible hitch. The
                    // ordinary Compose path keeps wear decals; streamed views keep the
                    // pooled physical surface details only.
                    ResidentialSurface.Kind.CrackA => true,
                    ResidentialSurface.Kind.CrackB => true,
                    ResidentialSurface.Kind.Grunge => true,
                    ResidentialSurface.Kind.RoadPatch => Mesh(RoadPatch, detailRoot, x, z, mark.Yaw, mark.Scale),
                    ResidentialSurface.Kind.Manhole => Mesh((detailIndex++ & 1) == 0 ? ManholeA : ManholeB,
                                                            detailRoot, x, z, mark.Yaw, mark.Scale),
                    ResidentialSurface.Kind.Grate => Mesh((detailIndex++ & 1) == 0 ? GrateA : GrateB,
                                                          detailRoot, x, z, mark.Yaw, mark.Scale),
                    ResidentialSurface.Kind.Newspaper => Cluster(Newspapers, detailRoot, x, z, mark),
                    _ => Cluster(Papers, detailRoot, x, z, mark),
                };
                if (!made) stood.SurfaceMissing++;
                else if (mark.Flush) stood.SurfaceFlush++;
                else stood.SurfaceClusters++;
                yield return null;
            }

            AmbientPeople(plan, root, stood);
            yield return null;

            stood.Absent.AddRange(Missing);
            stood.Missing = Missing.Count;
            stood.Refused = Worst();
        }

        static void GroundCell(
            ResidentialLot.Plan plan, Transform root, List<CafeSpot> cafes,
            Dictionary<(int, int), RingTile> ring, List<CorePavement.Kerbstone> kerbs,
            List<Stall> stalls, bool[,] laid, int i, int j, Stood stood)
        {
            if (laid[i, j] || ResidentialLot.CaryardParkingCell(plan, i, j)) return;
            string tile;
            float yaw = 0f;
            float cell = ResidentialLot.Cell;
            switch (plan.Ground[i, j])
            {
                case ResidentialLot.Use.Walkway:
                    Pavement(plan, root, i, j, ring, kerbs, stood);
                    return;
                case ResidentialLot.Use.Building:
                    return;
                case ResidentialLot.Use.Forecourt:
                    if (Forecourt(plan, i, j, out float floor) &&
                        Lay(Paving, root, i * cell, j * cell, cell, cell, 0f, floor) != null)
                        stood.Tiles++;
                    return;
                case ResidentialLot.Use.Park:
                case ResidentialLot.Use.Subway:
                    return;
                case ResidentialLot.Use.Yard:
                case ResidentialLot.Use.Court:
                case ResidentialLot.Use.Paved:
                    tile = Paving;
                    break;
                case ResidentialLot.Use.Cafe:
                    for (int n = 0; n < cafes.Count; n++)
                        if (cafes[n].Sunk && cafes[n].Foot.Overlaps(
                                new Rect(i * cell, j * cell, cell, cell))) return;
                    tile = Paving;
                    break;
                case ResidentialLot.Use.Verge:
                    Verge(plan, root, i, j, stood);
                    return;
                case ResidentialLot.Use.Drive:
                case ResidentialLot.Use.Alley:
                    tile = Bare;
                    break;
                case ResidentialLot.Use.Parking:
                    if (Bay(plan, root, laid, i, j, stalls, stood)) return;
                    tile = Bare;
                    break;
                default:
                    return;
            }
            if (Tile(tile, root, i, j, yaw) != null) stood.Tiles++;
        }
    }
}
