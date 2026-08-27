using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Builds a works compound onto the rectangles IndustrialLayout planned: the wall and its
    /// gate, a hall on each pad with its doors on the service road, staff parking by the gate, a
    /// lorry at a loading face, chimneys on the skyline and material stacked against the walls
    /// that use it.
    ///
    /// This REPLACES the perimeter path and the scatter for an industrial block rather than
    /// adding to them. That is the whole point. The old block ran BlockLots and WalkSide, which
    /// are built to wrap a terrace round a lot, so factories came out lining the block edge like
    /// houses; then BuildScatter dropped bricks, pallets and a chimney at uniform density and
    /// random yaw across whatever was left. Both passes were working correctly. Neither of them
    /// describes a factory.
    ///
    /// Sits alongside ParkDresser, which does the same job for the one other zone that is laid
    /// out rather than built up.
    /// </summary>
    public static class IndustrialDresser
    {
        /// <summary>Front of a hall to the kerb of the road it faces.</summary>
        const float RoadSetback = 1.5f;

        /// <summary>
        /// Clearance a hall keeps inside its own pad, half of it on each side. Two adjacent
        /// halls are each centred in their own pad, so the alley between them is this plus
        /// whatever slack their two pads happen to have - it is never actually 2m in practice.
        ///
        /// It was 4, and that quietly cost the works its best building. Pads come out at most
        /// about 27m, so a 4m demand meant nothing wider than 23m could ever be drawn - and
        /// industry-factory is 24.71m. That is the twin-stacked one the smoke hangs off, so the
        /// single most recognisable factory in the pack was unplaceable and the zone lost its
        /// chimneys along with it.
        /// </summary>
        const float HallClearance = 2f;

        /// <summary>
        /// Buildings one pad may hold. Two, because the narrowest piece in the kit is 6m and the
        /// widest 24.7m: a pad sized for the factory has room for a hall and a store beside it,
        /// and without this the pad would have to be cut to the AVERAGE building instead - which
        /// is what made the two biggest factories unplaceable.
        /// </summary>
        const int MaxHallsPerPad = 2;

        /// <summary>
        /// How much worse than the best packing a candidate may score and still be considered.
        /// Without it the chooser is deterministic in the pad width alone, and since a works kit
        /// is seven pieces the whole city ends up building the same pair on every pad of a given
        /// size. Three metres is enough spread to break that and too little to strand ground.
        /// </summary>
        const float NearTie = 3f;

        /// <summary>How far a stack sits off the wall it leans against.</summary>
        const float StackOffset = 1.6f;

        /// <summary>Pieces in one stack group. A works stores in heaps, not in single items.</summary>
        const int StackGroupMin = 2;
        const int StackGroupMax = 4;

        public static void Build(
            Vector2 min,
            Vector2 max,
            Sides roadSides,
            int blockId,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            CityConfig config,
            VehiclePicker vehicles,
            VehicleTinter tinter,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<ParkingLayout.Line> markings,
            List<GameObject> placed,
            List<BuildingTinter.Target> tints,
            List<Bounds> gateKeepOuts)
        {
            var layout = IndustrialLayout.ForBlock(
                min, max, roadSides,
                config.serviceRoadWidth, config.industrialWallInset,
                config.seed, blockId);

            // The wall goes up first so its line is on the ground before anything is measured
            // against it, and the gate before the parking, for the reason ParkingLotDresser runs
            // ahead of FillStalls: the booth and the piers have to claim their ground before
            // something else parks on it.
            var gate = new PerimeterFence.Gate
            {
                Has = layout.HasGate,
                Centre = layout.GateCentre,
                Outward = layout.GateOutward,
                Width = IndustrialLayout.GateWidth,
            };

            PerimeterFence.Build(
                layout.Wall.Min, layout.Wall.Max, gate,
                palette.fenceSegment, palette.fencePost, parent, spawn, placed);

            // Before the Usable early-out: the wall and its hole exist even when the yard is
            // not worth dressing, and an open gap needs its street kept clear as much as a
            // gated one. The Outward guard matters - ForBlock decides HasGate before several
            // early returns that leave GateCentre/GateOutward unset, and PerimeterFence's own
            // Dot test already treats that layout as gateless.
            if (gate.Has && gate.Outward.sqrMagnitude > 0.5f)
                gateKeepOuts?.Add(PerimeterFence.Approach(gate));

            if (!layout.Usable)
                return;

            BuildGate(layout, palette, parent, spawn, occupied, placed);
            BuildParking(layout, vehicles, tinter, config, parent, spawn, rng, occupied, markings, placed);
            BuildLorries(layout, vehicles, tinter, parent, spawn, occupied, placed);

            var halls = BuildHalls(layout, palette, prefabs, config, parent, spawn, rng,
                                   occupied, placed, tints);

            // Collected separately from `occupied`, which by this point also holds the buildings
            // of whatever else shares the block. These are the pieces the ZONE PARTITION could
            // not have known about: IndustrialLotPlanner runs at the bottom of this method,
            // against the halls, and everything below is stood inside the compound after it.
            var obstacles = new List<Bounds>();

            BuildBackYard(halls, palette, prefabs, parent, spawn, rng, occupied, placed, obstacles);
            BuildStacks(halls, palette, parent, spawn, rng, occupied, placed, obstacles);

            Publish(layout, halls, obstacles, blockId, config, parent);
        }

        /// <summary>
        /// Hands the yard on to the dressing pass, as a marker component on an empty.
        ///
        /// Only the HALLS travel. Everything else about a compound - the wall, the carriageways,
        /// the pads, the gate - comes back out of IndustrialLayout.ForBlock, which is
        /// deterministic in (seed, blockId) alone and which GroundPlacer already replays for
        /// exactly this reason. The halls are the one thing that cannot be replayed: their
        /// prefabs are drawn from BlockBuilder's SHARED Buildings stream, after however many
        /// draws every earlier block happened to take.
        ///
        /// Takes nothing from that stream itself. IndustrialLotPlanner seeds its own System.Random
        /// from SeedOffsets.IndustrialLot, so planning a yard - or retuning how one is planned -
        /// cannot move a single hall anywhere in the city.
        ///
        /// The zones are planned HERE rather than in the dressing pass so the marker is complete
        /// the moment it exists: the scene-view gizmos, the ground pass, the prop pass and the
        /// runtime director then all read one partition instead of each re-deriving it and
        /// risking three answers.
        /// </summary>
        static void Publish(
            IndustrialLayout.Layout layout,
            List<Placed> halls,
            List<Bounds> obstacles,
            int blockId,
            CityConfig config,
            Transform parent)
        {
            var published = new Entities.WorksHall[halls.Count];

            for (var i = 0; i < halls.Count; i++)
            {
                var hall = halls[i];
                published[i] = new Entities.WorksHall
                {
                    Centre = hall.Position,
                    Outward = hall.Outward,
                    HalfWidth = hall.HalfWidth,
                    HalfDepth = hall.HalfDepth,
                    PadMin = hall.Pad.Area.Min,
                    PadMax = hall.Pad.Area.Max,
                };
            }

            var marker = new GameObject($"works_yard_{blockId}");
            marker.transform.SetParent(parent, false);
            marker.transform.SetPositionAndRotation(
                new Vector3(layout.Wall.Centre.x, 0f, layout.Wall.Centre.y), Quaternion.identity);

            // The one thing here that is neither replayable nor derivable. The zones below are a
            // function of (seed, blockId, halls); the wall, roads, pads and gate all come back out
            // of ForBlock. But the stacks and the back-yard pieces were placed from BlockBuilder's
            // shared stream, against a `occupied` list that dies with this call - and the prop
            // pass, running an hour later off the saved scene, has no other way to learn they are
            // there. Without this it stacks barrels inside the brick piles.
            var footprints = new IndustrialLayout.Rect[obstacles?.Count ?? 0];

            for (var i = 0; i < footprints.Length; i++)
            {
                var box = obstacles[i];
                footprints[i] = new IndustrialLayout.Rect
                {
                    Min = new Vector2(box.min.x, box.min.z),
                    Max = new Vector2(box.max.x, box.max.z),
                };
            }

            var yard = marker.AddComponent<Entities.WorksYard>();
            yard.SetCompound(blockId, layout.Wall, layout.HasGate, layout.GateCentre,
                             layout.GateOutward, published,
                             IndustrialLotPlanner.Lanes(layout),
                             IndustrialLotPlanner.Bays(layout),
                             footprints);

            yard.SetZones(IndustrialLotPlanner
                .Plan(layout, published, IndustrialLotPlanner.Tuning.Default, config.seed, blockId)
                .ToArray());

            // Deliberately joins neither `occupied` nor `placed`. It has no renderer, so the
            // tinter would find nothing to tint and the caller's count would report a building
            // that does not exist - and an empty must not reserve ground the props can use.
        }

        /// <summary>The gate itself, standing in the gap the wall left, facing out.</summary>
        static void BuildGate(
            IndustrialLayout.Layout layout,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (!palette.gatePrefab || !layout.HasGate)
                return;

            var yaw = Mathf.Atan2(layout.GateOutward.x, layout.GateOutward.z) * Mathf.Rad2Deg;
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var bounds = PrefabBounds.Get(palette.gatePrefab);

            // The kit's gate is 7.4m of frame and leaf against a 9m opening, so placed as-is
            // it leaves daylight either side of itself. Stretched along the wall to the clear
            // opening between the pier faces - the same span ParkingLotDresser gives its boom.
            var stretch = GateStretch(bounds.size.x);

            // Not through Spawn: it only knows uniform scale, and its own comment sets the
            // precedent for not widening shared helpers while fixing one caller. Same
            // recentring maths, with the stretch folded into the local-x term, and no overlap
            // rejection - the gate is the first claim on this block's ground, and a gate that
            // silently vanished for overlap would be the worse bug.
            var offset = rotation * new Vector3(bounds.center.x * stretch, 0f, bounds.center.z);

            var instance = spawn(palette.gatePrefab,
                                 layout.GateCentre - new Vector3(offset.x, 0f, offset.z),
                                 rotation, parent);
            instance.transform.localScale = new Vector3(stretch, 1f, 1f);

            // FootprintXZ already performed the quarter-turn swap, so the along-wall component
            // is y when the gate faces along world x - mirror of Extent()'s mapping.
            var footprint = PrefabBounds.FootprintXZ(palette.gatePrefab, yaw);
            if (Mathf.Abs(layout.GateOutward.x) > 0.5f) footprint.y *= stretch;
            else footprint.x *= stretch;

            occupied.Add(new Bounds(new Vector3(layout.GateCentre.x, 0f, layout.GateCentre.z),
                                    new Vector3(footprint.x, 1f, footprint.y)));
            placed.Add(instance);
        }

        /// <summary>
        /// How far the gate is stretched along the wall to fill the clear opening between the
        /// pier faces. Never below 1: if the art ever comes out wider than the hole, the gate
        /// overlaps the piers rather than shrinking.
        /// </summary>
        internal static float GateStretch(float prefabWidth) =>
            Mathf.Max(1f, (IndustrialLayout.GateWidth - 2f * PerimeterFence.PierHalf) / prefabWidth);

        /// <summary>
        /// Staff parking on the pad by the gate. The bays and their paint both come out of
        /// ParkingLayout.ForStreetBay, so a works bay is the same 2.7 x 5.6 rectangle as one in a
        /// city car park and the two can never drift apart.
        /// </summary>
        static void BuildParking(
            IndustrialLayout.Layout layout,
            VehiclePicker vehicles,
            VehicleTinter tinter,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<ParkingLayout.Line> markings,
            List<GameObject> placed)
        {
            foreach (var run in layout.ParkingRuns)
            {
                var bays = ParkingLayout.ForStreetBay(
                    run.Origin, run.Along, run.Outward, run.Cursor, run.Width);

                markings.AddRange(bays.Markings);

                foreach (var stall in bays.Stalls)
                {
                    // A works car park is not full at any hour, and a gap reads as a working
                    // site where a solid rank of cars reads as a showroom.
                    if (rng.NextDouble() < 0.25)
                        continue;

                    var vehicle = vehicles?.Next(ParkingLayout.StallDepth,
                                                 ParkingLayout.StallWidth - 0.3f);
                    if (!vehicle)
                        continue;

                    tinter?.Paint(
                        OverlapSpawn.Place(vehicle, stall.Centre, stall.Yaw, 1f, parent, spawn, occupied, placed),
                        vehicle);
                }
            }
        }

        /// <summary>
        /// The lorry at the loading face. Placed here rather than through the bay filler because
        /// ParkingLayout.StallDepth is 5.6 and `truck` is 6.25 - VehiclePicker rejects it from
        /// every marked bay in the city, correctly, and a works with no lorry in it is missing
        /// the one vehicle that says what the place does.
        /// </summary>
        static void BuildLorries(
            IndustrialLayout.Layout layout,
            VehiclePicker vehicles,
            VehicleTinter tinter,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            foreach (var stand in layout.LorryStands)
            {
                var lorry = vehicles?.Next(IndustrialLayout.LorryStandDepth,
                                           IndustrialLayout.LorryStandWidth);
                if (!lorry)
                    continue;

                var centre = stand.Origin
                           + stand.Along * (stand.Cursor + stand.Width * 0.5f)
                           - stand.Outward * (IndustrialLayout.LorryStandDepth * 0.5f);

                var yaw = Mathf.Atan2(stand.Outward.x, stand.Outward.z) * Mathf.Rad2Deg;

                // Place returns null when the stand was already occupied; Paint no-ops on that,
                // but the roll is still taken, which is what keeps the stream stable.
                tinter?.Paint(OverlapSpawn.Place(lorry, centre, yaw, 1f, parent, spawn, occupied, placed), lorry);
            }
        }

        /// <summary>
        /// A hall on each pad, turned so its front faces the road that serves it.
        ///
        /// The candidate is chosen by measuring, the way BestCloser does: the pad is a budget and
        /// the catalogue runs 12.35m to 24.71m wide, so picking blind puts a 24m factory on a 14m
        /// pad and Place silently rejects it, leaving a hole in the row. Several draws are taken
        /// and the widest that FITS wins - a works should look built out, not sparse.
        /// </summary>
        static List<Placed> BuildHalls(
            IndustrialLayout.Layout layout,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed,
            List<BuildingTinter.Target> tints)
        {
            var halls = new List<Placed>();

            var source = PickGroup(palette, rng);
            if (source == null || source.prefabs == null || source.prefabs.Length == 0)
                return halls;

            foreach (var pad in layout.Pads)
            {
                var alongX = Mathf.Abs(pad.Outward.z) > 0.5f;
                var padWidth = alongX ? pad.Area.Size.x : pad.Area.Size.y;
                var padDepth = alongX ? pad.Area.Size.y : pad.Area.Size.x;

                var runDepth = padDepth - RoadSetback;
                if (padWidth < 8f || runDepth < 6f)
                    continue;

                var yawBase = Mathf.Atan2(pad.Outward.x, pad.Outward.z) * Mathf.Rad2Deg;
                var lateral = Vector3.Cross(Vector3.up, pad.Outward);
                var padCentre = new Vector3(pad.Area.Centre.x, 0f, pad.Area.Centre.y);

                // Walk the pad's width, filling it with one or two buildings. Filling the
                // leftover is what lets pads be wide enough for the big factory WITHOUT the
                // block losing buildings - sized the other way round, cut to the average
                // building, the two widest pieces in the kit could never be drawn at all and the
                // zone came out as nothing but sheds.
                var cursor = HallClearance * 0.5f;
                var remaining = padWidth - HallClearance;

                for (var slot = 0; slot < MaxHallsPerPad && remaining >= 6f; slot++)
                {
                    var pick = Choose(source.prefabs, prefabs, yawBase, lateral, pad.Outward,
                                      remaining, runDepth, slot == 0, rng);

                    if (!pick.Prefab)
                        break;

                    var best = pick.Prefab;
                    var bestWidth = pick.Width;
                    var bestFootprint = pick.Footprint;
                    var bestYaw = pick.Yaw;

                    var depthUsed = Extent(bestFootprint, pad.Outward);

                    // Along the pad: from its low edge by the cursor. Across it: pushed back off
                    // the road by the setback, so the doors stand clear of the carriageway.
                    var position = padCentre
                                 + lateral * (cursor + bestWidth * 0.5f - padWidth * 0.5f)
                                 - pad.Outward * ((padDepth - depthUsed) * 0.5f - RoadSetback);

                    var instance = OverlapSpawn.Place(best, position, bestYaw, 1f, parent, spawn, occupied, placed);

                    cursor += bestWidth + HallClearance;
                    remaining -= bestWidth + HallClearance;

                    if (!instance)
                        continue;

                    Ambient.SmokeVent.Mark(instance, best, prefabs, Ambient.VentKind.Works);
                    tints.Add(new BuildingTinter.Target(instance, commercial: false));

                    halls.Add(new Placed
                    {
                        Position = position,
                        Outward = pad.Outward,
                        HalfWidth = bestWidth * 0.5f,
                        HalfDepth = depthUsed * 0.5f,
                        Pad = pad,
                    });
                }
            }

            return halls;
        }

        /// <summary>
        /// The back yard of every hall: the outbuildings and the chimney.
        ///
        /// This is where the works stops looking empty. A row is 26m deep and the halls in it
        /// are 6m to 23m, so behind most of them sits a band of bare ground running the length of
        /// the row - and the deeper the block, the more of it there is, because the layout hands
        /// its leftover depth to the rows. Filling that band is what turns three sheds on a slab
        /// into a site.
        ///
        /// The chimney goes here too rather than in the middle of the yard. A stack stands behind
        /// the shed it serves, and putting it in the back band also means it is measured against
        /// the same strip everything else here has to fit, instead of being dropped at a pad
        /// centre and silently rejected for landing on the hall.
        /// </summary>
        static void BuildBackYard(
            List<Placed> halls,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed,
            List<Bounds> obstacles)
        {
            var chimneys = 0;

            for (var i = 0; i < halls.Count; i++)
            {
                var hall = halls[i];

                // The band between the back of the hall and the back of its pad.
                var padDepth = Extent(hall.Pad.Area.Size, hall.Outward);
                var padWidth = Extent(hall.Pad.Area.Size, Vector3.Cross(Vector3.up, hall.Outward));

                var padCentre = new Vector3(hall.Pad.Area.Centre.x, 0f, hall.Pad.Area.Centre.y);
                var hallBack = Vector3.Dot(hall.Position - padCentre, -hall.Outward) + hall.HalfDepth;
                var band = padDepth * 0.5f - hallBack;

                if (band < 5f)
                    continue;

                var bandCentre = padCentre - hall.Outward * (padDepth * 0.5f - band * 0.5f);
                var lateral = Vector3.Cross(Vector3.up, hall.Outward);

                // A stack on every third hall or so, and always on the first that has room -
                // a compound with no chimney has no smoke, which is the one thing about this
                // zone anybody actually looks for.
                var wantsChimney = chimneys == 0 || rng.NextDouble() < 0.35;

                if (wantsChimney && palette.chimneyProps != null && palette.chimneyProps.Length > 0)
                {
                    // The FIRST stack on a compound must be one that actually smokes. The list
                    // holds chimney-big and water-tower-medium, and only the first of those has
                    // a measured vent - drawing blind gave half the works a water tower and no
                    // plume at all, which reads as the smoke being broken rather than as that
                    // works not having a chimney.
                    var stack = chimneys == 0
                        ? FirstWithVents(palette.chimneyProps, prefabs, rng)
                        : palette.chimneyProps[rng.Next(palette.chimneyProps.Length)];

                    var yaw = rng.Next(4) * 90f;
                    var footprint = PrefabBounds.FootprintXZ(stack, yaw);

                    if (Extent(footprint, hall.Outward) <= band
                     && Extent(footprint, lateral) <= padWidth - 1f)
                    {
                        // Off to one side, so it stands beside the hall's back wall rather than
                        // centred behind it like a chimney on a house.
                        var side = (i % 2 == 0 ? 1f : -1f)
                                 * (padWidth * 0.5f - Extent(footprint, lateral) * 0.5f - 1f);

                        var instance = OverlapSpawn.Place(stack, bandCentre + lateral * side, yaw, 1f,
                                                          parent, spawn, occupied, placed, obstacles);
                        if (instance)
                        {
                            Ambient.SmokeVent.Mark(instance, stack, prefabs, Ambient.VentKind.Works);
                            chimneys++;
                        }
                    }
                }

                if (palette.auxBuildings == null || palette.auxBuildings.Length == 0)
                    continue;

                // One or two outbuildings along the band, spread across its width.
                var wanted = padWidth > 22f ? 2 : 1;

                for (var slot = 0; slot < wanted; slot++)
                {
                    var aux = palette.auxBuildings[rng.Next(palette.auxBuildings.Length)];
                    if (!aux)
                        continue;

                    var yaw = rng.Next(4) * 90f;
                    var footprint = PrefabBounds.FootprintXZ(aux, yaw);

                    if (Extent(footprint, hall.Outward) > band - 1f)
                        continue;

                    // Slot centres across the band, so two outbuildings do not both land in the
                    // middle and lose one of themselves to the occupancy test.
                    var t = (slot + 0.5f) / wanted;
                    var offset = Mathf.Lerp(-padWidth * 0.5f + 2f, padWidth * 0.5f - 2f, t);

                    OverlapSpawn.Place(aux, bandCentre + lateral * offset, yaw, 1f,
                                       parent, spawn, occupied, placed, obstacles);
                }
            }
        }

        /// <summary>
        /// The first prefab in the list that has a measured chimney mouth, or a random one when
        /// none has - which is the honest fallback, because it means the vent table was never
        /// built and no choice here would produce smoke anyway.
        /// </summary>
        static GameObject FirstWithVents(
            GameObject[] candidates, PrefabDatabase prefabs, System.Random rng)
        {
            var mouths = new List<Vector3>();

            foreach (var candidate in candidates)
            {
                if (!candidate)
                    continue;

                prefabs.ChimneyVentsFor(candidate, mouths);
                if (mouths.Count > 0)
                    return candidate;
            }

            return candidates[rng.Next(candidates.Length)];
        }


        /// <summary>
        /// Material stacked in the gaps between halls.
        ///
        /// The gap is where it goes, and getting that wrong is easy: the obvious spot is the
        /// strip the road setback leaves in front of a hall, but that strip is 1.5m and a brick
        /// stack is wider, so every group landed inside the building it was supposed to lean
        /// against and Spawn rejected the lot silently. What there is actually room in is the
        /// alley beside each hall, which is where a works stores things
        /// anyway.
        ///
        /// One prefab per group, repeated in a short line at the hall's own yaw. The old pass
        /// drew a different prefab per attempt at uniform density across the whole block, so no
        /// two adjacent items agreed about anything - which is what reads as tipped out.
        /// </summary>
        static void BuildStacks(
            List<Placed> halls,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed,
            List<Bounds> obstacles)
        {
            if (palette.stackProps == null || palette.stackProps.Length == 0)
                return;

            foreach (var hall in halls)
            {
                if (rng.NextDouble() > 0.65)
                    continue;

                var prefab = palette.stackProps[rng.Next(palette.stackProps.Length)];
                if (!prefab)
                    continue;

                var yaw = Mathf.Atan2(hall.Outward.x, hall.Outward.z) * Mathf.Rad2Deg;
                var footprint = PrefabBounds.FootprintXZ(prefab, yaw);

                // The group runs back from the road, down the alley, so its length is limited by
                // the hall's depth rather than by the 4m gap.
                var step = Mathf.Max(0.5f, Extent(footprint, hall.Outward)) + 0.4f;
                var lateral = Vector3.Cross(Vector3.up, hall.Outward);
                var half = Extent(footprint, lateral) * 0.5f;

                var count = StackGroupMin + rng.Next(StackGroupMax - StackGroupMin + 1);
                var room = hall.HalfDepth * 2f - 1f;
                if (step * count > room)
                    count = Mathf.FloorToInt(room / step);
                if (count < 1)
                    continue;

                // Against one side wall, picked per hall so a row of works does not stack
                // everything down the same flank.
                var side = rng.Next(2) == 0 ? 1f : -1f;
                var start = hall.Position
                          + lateral * side * (hall.HalfWidth + half + StackOffset)
                          + hall.Outward * (hall.HalfDepth - step * 0.5f);

                for (var i = 0; i < count; i++)
                    OverlapSpawn.Place(prefab, start - hall.Outward * (step * i), yaw, 1f,
                                       parent, spawn, occupied, placed, obstacles);
            }
        }

        /// <summary>Size of an XZ footprint along an axis-aligned direction.</summary>
        static float Extent(Vector2 footprint, Vector3 direction) =>
            Mathf.Abs(direction.x) > 0.5f ? footprint.x : footprint.y;

        /// <summary>What Choose settles on for one slot on a pad.</summary>
        struct Pick
        {
            public GameObject Prefab;
            public float Width;
            public float Yaw;
            public Vector2 Footprint;
        }

        /// <summary>
        /// Picks the building for one slot, scoring candidates by how much of the pad's width the
        /// pad ends up using rather than by how wide the candidate itself is.
        ///
        /// Greedy "widest that fits" is the obvious rule and it strands ground. On the single 33m
        /// pad of a one-cell block it takes industry-factory at 24.71 and leaves 4.29m - under
        /// the 6m narrowest piece in the kit - so the whole block comes out as one building
        /// behind a wall, which is exactly what "the industrial zone looks empty" looked like.
        /// Scoring one step ahead picks industry-refinery at 22.0 instead and puts a 6m store
        /// beside it: 28 of the 31m used, and two buildings rather than one.
        ///
        /// Only the first slot looks ahead - the second has nothing to look ahead to - and near
        /// ties are broken by draw so that two pads of the same width do not come out as the same
        /// pair of buildings across the whole city.
        /// </summary>
        static Pick Choose(
            GameObject[] candidates,
            PrefabDatabase prefabs,
            float yawBase,
            Vector3 lateral,
            Vector3 outward,
            float remaining,
            float runDepth,
            bool lookAhead,
            System.Random rng)
        {
            var best = new List<Pick>();
            var bestScore = 0f;

            foreach (var candidate in candidates)
            {
                if (!candidate)
                    continue;

                var yaw = yawBase + prefabs.ExtraYawFor(candidate);
                var footprint = PrefabBounds.FootprintXZ(candidate, yaw);

                var width = Extent(footprint, lateral);
                if (width > remaining || Extent(footprint, outward) > runDepth)
                    continue;

                var score = width;

                if (lookAhead)
                {
                    // The widest thing that would still fit beside it.
                    var left = remaining - width - HallClearance;
                    foreach (var other in candidates)
                    {
                        if (!other)
                            continue;

                        var otherYaw = yawBase + prefabs.ExtraYawFor(other);
                        var otherPrint = PrefabBounds.FootprintXZ(other, otherYaw);
                        var otherWidth = Extent(otherPrint, lateral);

                        if (otherWidth <= left && Extent(otherPrint, outward) <= runDepth)
                            score = Mathf.Max(score, width + otherWidth);
                    }
                }

                var pick = new Pick
                {
                    Prefab = candidate,
                    Width = width,
                    Yaw = yaw,
                    Footprint = footprint,
                };

                // Within NearTie of the best counts as the best, so the choice keeps some spread
                // instead of every 30m pad in the city holding the identical pair.
                if (score > bestScore + NearTie)
                {
                    bestScore = score;
                    best.Clear();
                    best.Add(pick);
                }
                else if (score >= bestScore - NearTie)
                {
                    bestScore = Mathf.Max(bestScore, score);
                    best.Add(pick);
                }
            }

            return best.Count == 0 ? default : best[rng.Next(best.Count)];
        }

        /// <summary>Which building group this block draws its halls from.</summary>
        static PrefabDatabase.WeightedGroup PickGroup(
            PrefabDatabase.ZonePalette palette, System.Random rng)
        {
            if (palette.groups == null || palette.groups.Length == 0)
                return null;

            var total = 0f;
            foreach (var group in palette.groups)
                if (group != null && group.IsUsable)
                    total += group.weight;

            if (total <= 0f)
                return null;

            var roll = (float)rng.NextDouble() * total;
            foreach (var group in palette.groups)
            {
                if (group == null || !group.IsUsable)
                    continue;

                roll -= group.weight;
                if (roll <= 0f)
                    return group;
            }

            return null;
        }

        /// <summary>
        /// A hall that actually stood, in the terms the stack pass needs: where it is, which way
        /// it faces, and how big it came out. Half extents rather than a footprint because every
        /// use is "from the centre out to the wall".
        /// </summary>
        struct Placed
        {
            public Vector3 Position;
            public Vector3 Outward;
            public float HalfWidth;
            public float HalfDepth;

            /// <summary>The ground this hall was given, so the back-yard pass knows its bounds.</summary>
            public IndustrialLayout.Pad Pad;
        }
    }
}
