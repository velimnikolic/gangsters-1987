using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Builds the port onto the rectangles PortLayout planned: the wall and its gate on the
    /// street sides, warehouses with their doors to the water, container stacks parted at the
    /// aisle, cranes on the quay, the ship at the berth, and the walk graph published for the
    /// shift that works it all.
    ///
    /// REPLACES the perimeter path and the scatter, the way IndustrialDresser does and for the
    /// same reason: BlockLots wraps a terrace round a lot, and a container terminal is the
    /// least terrace-shaped thing in the city. Third of its kind, after the works and the park.
    ///
    /// The water and the quay strip are NOT laid here - GroundPlacer lays them by replaying
    /// PortLayout from its own pass, the same split the works uses for its carriageways.
    /// </summary>
    public static class PortDresser
    {
        /// <summary>Front of a warehouse to the edge of its pad, doors clear of the apron.</summary>
        const float RoadSetback = 1.5f;

        /// <summary>Clearance a warehouse keeps inside its pad - the works figure, same kit.</summary>
        const float HallClearance = 2f;

        const int MaxHallsPerPad = 2;

        /// <summary>Same near-tie spread as the works chooser, same reason.</summary>
        const float NearTie = 3f;

        /// <summary>Gap between two boxes in a stack run.</summary>
        const float ContainerGap = 0.15f;

        /// <summary>Mooring bollards down the coping, and lanterns down the inland edge.</summary>
        const float BollardPitch = 9f;
        const float QuayLampPitch = 24f;

        /// <summary>Chance a stack carries a second tier, and how much of the run it covers.</summary>
        const double SecondTierChance = 0.45;

        /// <summary>Buoys bobbing off the quay, and how far out they may drift.</summary>
        const int BuoyCount = 3;
        const float BuoyReach = 30f;

        public static void Build(
            Vector2 min,
            Vector2 max,
            Sides roadSides,
            Sides edgeSides,
            int blockId,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            CityConfig config,
            VehiclePicker vehicles,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<ParkingLayout.Line> markings,
            List<GameObject> placed,
            List<BuildingTinter.Target> tints,
            List<Bounds> gateKeepOuts,
            Sides quaySide = Sides.None,
            PortLayout.Continuation continuation = default)
        {
            var layout = PortLayout.ForBlock(
                min, max, roadSides, edgeSides,
                config.industrialWallInset, config.seed, blockId, quaySide, continuation);

            // Wall first, gate keep-out before the Usable early-out - the same order and the
            // same reasoning as the works dresser, with one addition: the quay side is left
            // OPEN. The wall stops where the water starts.
            var gate = new PerimeterFence.Gate
            {
                Has = layout.HasGate,
                Centre = layout.GateCentre,
                Outward = layout.GateOutward,
                Width = PortLayout.GateWidth,
            };

            PerimeterFence.Build(
                layout.Wall.Min, layout.Wall.Max, gate,
                palette.fenceSegment, palette.fencePost, parent, spawn, placed,
                openSides: layout.QuaySide);

            if (gate.Has && gate.Outward.sqrMagnitude > 0.5f)
                gateKeepOuts?.Add(PerimeterFence.Approach(gate));

            if (!layout.Usable)
                return;

            BuildGate(layout, gate, palette, parent, spawn, occupied, placed);

            var warehouses = BuildWarehouses(layout, palette, prefabs, parent, spawn, rng,
                                             occupied, placed, tints);

            BuildCranes(layout, palette, parent, spawn, occupied, placed);
            BuildStacks(layout, palette, rng, parent, spawn, occupied, placed);
            BuildBerthPreview(layout, palette, blockId, rng, parent, spawn, occupied, placed);
            BuildPier(layout, palette, rng, parent, spawn, occupied, placed);
            BuildLorries(layout, vehicles, parent, spawn, occupied, placed);
            BuildBollards(layout, palette, parent, spawn, placed);
            BuildQuayLamps(layout, palette, parent, spawn, occupied, placed);
            BuildApronStacks(warehouses, palette, rng, parent, spawn, occupied, placed);
            BuildLooseBoxes(layout, palette, rng, parent, spawn, occupied, placed);
            BuildQuayProps(layout, palette, rng, parent, spawn, occupied, placed);

            Publish(layout, blockId, parent);
        }

        /// <summary>The gate in the wall's gap - the works gate verbatim, stretch and all.</summary>
        static void BuildGate(
            PortLayout.Layout layout,
            PerimeterFence.Gate gate,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (!palette.gatePrefab || !gate.Has)
                return;

            var yaw = Mathf.Atan2(gate.Outward.x, gate.Outward.z) * Mathf.Rad2Deg;
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var bounds = PrefabBounds.Get(palette.gatePrefab);
            var stretch = IndustrialDresser.GateStretch(bounds.size.x);

            var offset = rotation * new Vector3(bounds.center.x * stretch, 0f, bounds.center.z);
            var instance = spawn(palette.gatePrefab,
                                 gate.Centre - new Vector3(offset.x, 0f, offset.z),
                                 rotation, parent);
            instance.transform.localScale = new Vector3(stretch, 1f, 1f);

            var footprint = PrefabBounds.FootprintXZ(palette.gatePrefab, yaw);
            if (Mathf.Abs(gate.Outward.x) > 0.5f) footprint.y *= stretch;
            else footprint.x *= stretch;

            occupied.Add(new Bounds(new Vector3(gate.Centre.x, 0f, gate.Centre.z),
                                    new Vector3(footprint.x, 1f, footprint.y)));
            placed.Add(instance);
        }

        /// <summary>
        /// A warehouse on each pad, doors to the water. The chooser is the works chooser's
        /// shape without its look-ahead: the port kit is three or four pieces, and near-tie
        /// spread alone keeps a row from repeating. Landmark first - building-port-sea takes
        /// the widest pad, because the terminal building IS the elevation the port shows the
        /// city, and left to the width-fit roll it lost that pad to a warehouse half the time.
        /// </summary>
        /// <summary>A warehouse that actually stood, in the terms the apron-stack pass needs.</summary>
        struct Stood
        {
            public Vector3 Position;
            public Vector3 Outward;
            public float HalfWidth;
            public float HalfDepth;
        }

        static List<Stood> BuildWarehouses(
            PortLayout.Layout layout,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed,
            List<BuildingTinter.Target> tints)
        {
            var stood = new List<Stood>();
            if (layout.Pads.Count == 0)
                return stood;

            // The widest pad, for the landmark.
            var widest = 0;
            for (var i = 1; i < layout.Pads.Count; i++)
                if (PadWidth(layout.Pads[i]) > PadWidth(layout.Pads[widest]))
                    widest = i;

            var landmark = palette.landmarks != null && palette.landmarks.Length > 0
                ? palette.landmarks[rng.Next(palette.landmarks.Length)]
                : null;

            for (var i = 0; i < layout.Pads.Count; i++)
            {
                var pad = layout.Pads[i];
                var padWidth = PadWidth(pad);
                var padDepth = PadDepth(pad);
                var runDepth = padDepth - RoadSetback;

                if (padWidth < 8f || runDepth < 6f)
                    continue;

                var yawBase = Mathf.Atan2(pad.Outward.x, pad.Outward.z) * Mathf.Rad2Deg;
                var lateral = Vector3.Cross(Vector3.up, pad.Outward);
                var padCentre = new Vector3(pad.Area.Centre.x, 0f, pad.Area.Centre.y);

                var cursor = HallClearance * 0.5f;
                var remaining = padWidth - HallClearance;

                for (var slot = 0; slot < MaxHallsPerPad && remaining >= 6f; slot++)
                {
                    GameObject pick = null;
                    var pickYaw = 0f;
                    Vector2 pickPrint = default;

                    // The landmark claims the first slot of the widest pad, if it fits.
                    if (i == widest && slot == 0 && landmark)
                    {
                        var yaw = yawBase + prefabs.ExtraYawFor(landmark);
                        var print = PrefabBounds.FootprintXZ(landmark, yaw);
                        if (Extent(print, lateral) <= remaining
                         && Extent(print, pad.Outward) <= runDepth)
                        {
                            pick = landmark;
                            pickYaw = yaw;
                            pickPrint = print;
                        }
                    }

                    if (!pick)
                        (pick, pickYaw, pickPrint) =
                            Choose(palette, prefabs, yawBase, lateral, pad.Outward,
                                   remaining, runDepth, rng);

                    if (!pick)
                        break;

                    var width = Extent(pickPrint, lateral);
                    var depthUsed = Extent(pickPrint, pad.Outward);

                    var position = padCentre
                                 + lateral * (cursor + width * 0.5f - padWidth * 0.5f)
                                 - pad.Outward * ((padDepth - depthUsed) * 0.5f - RoadSetback);

                    var instance = Spawn(pick, position, pickYaw, parent, spawn, occupied, placed);

                    cursor += width + HallClearance;
                    remaining -= width + HallClearance;

                    if (!instance)
                        continue;

                    tints.Add(new BuildingTinter.Target(instance, commercial: false));
                    stood.Add(new Stood
                    {
                        Position = position,
                        Outward = pad.Outward,
                        HalfWidth = width * 0.5f,
                        HalfDepth = depthUsed * 0.5f,
                    });
                }
            }

            return stood;
        }

        static (GameObject, float, Vector2) Choose(
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            float yawBase,
            Vector3 lateral,
            Vector3 outward,
            float remaining,
            float runDepth,
            System.Random rng)
        {
            var best = new List<(GameObject, float, Vector2)>();
            var bestWidth = 0f;

            foreach (var group in palette.groups ?? System.Array.Empty<PrefabDatabase.WeightedGroup>())
            {
                if (group?.prefabs == null)
                    continue;

                foreach (var candidate in group.prefabs)
                {
                    if (!candidate)
                        continue;

                    var yaw = yawBase + prefabs.ExtraYawFor(candidate);
                    var print = PrefabBounds.FootprintXZ(candidate, yaw);
                    var width = Extent(print, lateral);

                    if (width > remaining || Extent(print, outward) > runDepth)
                        continue;

                    if (width > bestWidth + NearTie)
                    {
                        bestWidth = width;
                        best.Clear();
                        best.Add((candidate, yaw, print));
                    }
                    else if (width >= bestWidth - NearTie)
                    {
                        bestWidth = Mathf.Max(bestWidth, width);
                        best.Add((candidate, yaw, print));
                    }
                }
            }

            return best.Count == 0 ? default : best[rng.Next(best.Count)];
        }

        /// <summary>
        /// The cranes, long axis along the water. crane-port spans 20.92m along its local X,
        /// so yawing its +Z at the sea lays the gantry down the quay - which is how a portal
        /// crane stands, and why the spot's Outward is all the orientation needed.
        /// </summary>
        static void BuildCranes(
            PortLayout.Layout layout,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (palette.portCranes == null || palette.portCranes.Length == 0)
                return;

            for (var i = 0; i < layout.Cranes.Count; i++)
            {
                var spot = layout.Cranes[i];
                var crane = palette.portCranes[i % palette.portCranes.Length];
                if (!crane)
                    continue;

                var yaw = Mathf.Atan2(spot.Outward.x, spot.Outward.z) * Mathf.Rad2Deg;
                Spawn(crane, spot.Centre, yaw, parent, spawn, occupied, placed);
            }
        }

        /// <summary>
        /// The container stacks: one colour per stack, ranked at a fixed pitch, the strongest
        /// picture a container yard has and the exact opposite of scatter. A second tier on
        /// some stacks, stepped at the box's own measured height - never scaled.
        /// </summary>
        static void BuildStacks(
            PortLayout.Layout layout,
            PrefabDatabase.ZonePalette palette,
            System.Random rng,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (palette.portContainers == null || palette.portContainers.Length == 0)
                return;

            foreach (var stack in layout.Stacks)
            {
                var box = palette.portContainers[rng.Next(palette.portContainers.Length)];
                if (!box)
                    continue;

                // Long side across the band: the box's +Z faces the water, its 2.5m width
                // ranks along the quay.
                var yaw = Mathf.Atan2(layout.Seaward.x, layout.Seaward.z) * Mathf.Rad2Deg;
                var print = PrefabBounds.FootprintXZ(box, yaw);
                var along = stack.Along;
                var step = Extent(print, along) + ContainerGap;

                var length = Extent(new Vector2(stack.Area.Size.x, stack.Area.Size.y), along);
                var count = Mathf.FloorToInt((length - ContainerGap) / step);
                if (count < 1)
                    continue;

                var centre = new Vector3(stack.Area.Centre.x, 0f, stack.Area.Centre.y);
                var start = centre - along * ((count - 1) * 0.5f * step);

                for (var i = 0; i < count; i++)
                    Spawn(box, start + along * (step * i), yaw, parent, spawn, occupied, placed);

                // The second tier is a shorter run on top, one end flush - how a real stack
                // is worked down. Placed WITHOUT the occupancy test: it deliberately stands
                // over the boxes below it, which the ground-plane bounds would veto.
                if (count >= 3 && rng.NextDouble() < SecondTierChance)
                {
                    var tier = PrefabBounds.Get(box).size.y;
                    var upper = 1 + rng.Next(count - 1);
                    for (var i = 0; i < upper; i++)
                    {
                        var position = start + along * (step * i) + Vector3.up * tier;
                        SpawnFree(box, position, yaw, parent, spawn, placed);
                    }
                }
            }
        }

        /// <summary>
        /// The ship, moored parallel, sunk to the water plane - as an EDITOR PREVIEW. The name
        /// is the contract: PortShipDirector destroys every port_ship_preview_* at Play and
        /// runs live ships against the marker's berth instead, so the scene view shows a
        /// worked berth and the running game shows ships that actually come and go.
        /// </summary>
        static void BuildBerthPreview(
            PortLayout.Layout layout,
            PrefabDatabase.ZonePalette palette,
            int blockId,
            System.Random rng,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (!layout.HasBerth || palette.portShips == null || palette.portShips.Length == 0)
                return;

            var ship = palette.portShips[rng.Next(palette.portShips.Length)];
            if (!ship)
                return;

            var yaw = layout.BerthYaw + (rng.Next(2) == 0 ? 0f : 180f);
            var instance = SpawnFree(ship, layout.BerthCentre, yaw, parent, spawn, placed);

            // The hull still claims its water, so the pier pass and the buoys cannot land in
            // it - even though nothing on land could reach it through the occupancy test.
            if (instance)
            {
                instance.name = $"port_ship_preview_{blockId}";

                var print = PrefabBounds.FootprintXZ(ship, yaw);
                occupied.Add(new Bounds(
                    new Vector3(layout.BerthCentre.x, 0f, layout.BerthCentre.z),
                    new Vector3(print.x, 50f, print.y)));
            }
        }

        /// <summary>
        /// Mooring bollards down the coping at a fixed pitch - the smallest thing on the quay
        /// and the one that most says "ships tie up here". The fence pier stands in for a
        /// bollard: 0.66 x 1.83, a squat stone post. No occupancy test and no reservation -
        /// they stand at the very lip, 0.8m from the edge, where nothing else is ever placed,
        /// and a bollard missing because a crane's ground-plane bounds brushed it would be
        /// the wrong outcome.
        /// </summary>
        static void BuildBollards(
            PortLayout.Layout layout,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            List<GameObject> placed)
        {
            if (!palette.fencePost)
                return;

            var quay = layout.Quay;
            var along = Mathf.Abs(layout.Seaward.z) > 0.5f ? Vector3.right : Vector3.forward;
            var span = Extent(new Vector2(quay.Size.x, quay.Size.y), along);
            var centre = new Vector3(quay.Centre.x, 0f, quay.Centre.y);
            var lip = centre + layout.Seaward
                    * (Extent(new Vector2(quay.Size.x, quay.Size.y), layout.Seaward) * 0.5f - 0.8f);

            var count = Mathf.Max(2, Mathf.FloorToInt(span / BollardPitch));
            for (var i = 0; i < count; i++)
            {
                var t = (i + 0.5f) / count - 0.5f;
                placed.Add(spawn(palette.fencePost, lip + along * (t * span),
                                 Quaternion.identity, parent));
            }
        }

        /// <summary>
        /// Lanterns down the inland edge of the quay strip, between the walk lane and the
        /// stacks, at a fixed pitch offset from the bollards' so the two rows interleave.
        /// </summary>
        static void BuildQuayLamps(
            PortLayout.Layout layout,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (!palette.portQuayLamp)
                return;

            var quay = layout.Quay;
            var along = Mathf.Abs(layout.Seaward.z) > 0.5f ? Vector3.right : Vector3.forward;
            var span = Extent(new Vector2(quay.Size.x, quay.Size.y), along);
            var centre = new Vector3(quay.Centre.x, 0f, quay.Centre.y);
            var line = centre - layout.Seaward
                     * (Extent(new Vector2(quay.Size.x, quay.Size.y), layout.Seaward) * 0.5f - 1f);

            var count = Mathf.Max(1, Mathf.FloorToInt(span / QuayLampPitch));
            for (var i = 0; i < count; i++)
            {
                var t = (i + 0.5f) / count - 0.5f;
                Spawn(palette.portQuayLamp, line + along * (t * span), 0f,
                      parent, spawn, occupied, placed);
            }
        }

        /// <summary>
        /// Goods rowed against the warehouse flanks - the works' BuildStacks picture at the
        /// waterfront: one prefab per group, repeated in a short line down the alley beside
        /// the building, where a port actually stages what the forklift moves.
        /// </summary>
        static void BuildApronStacks(
            List<Stood> warehouses,
            PrefabDatabase.ZonePalette palette,
            System.Random rng,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (palette.stackProps == null || palette.stackProps.Length == 0)
                return;

            foreach (var hall in warehouses)
            {
                if (rng.NextDouble() > 0.7)
                    continue;

                var prefab = palette.stackProps[rng.Next(palette.stackProps.Length)];
                if (!prefab)
                    continue;

                var yaw = Mathf.Atan2(hall.Outward.x, hall.Outward.z) * Mathf.Rad2Deg;
                var footprint = PrefabBounds.FootprintXZ(prefab, yaw);

                var step = Mathf.Max(0.5f, Extent(footprint, hall.Outward)) + 0.4f;
                var lateral = Vector3.Cross(Vector3.up, hall.Outward);
                var half = Extent(footprint, lateral) * 0.5f;

                var count = 2 + rng.Next(3);
                var room = hall.HalfDepth * 2f - 1f;
                if (step * count > room)
                    count = Mathf.FloorToInt(room / step);
                if (count < 1)
                    continue;

                var side = rng.Next(2) == 0 ? 1f : -1f;
                var start = hall.Position
                          + lateral * side * (hall.HalfWidth + half + 1.4f)
                          + hall.Outward * (hall.HalfDepth - step * 0.5f);

                for (var i = 0; i < count; i++)
                    Spawn(prefab, start - hall.Outward * (step * i), yaw,
                          parent, spawn, occupied, placed);
            }
        }

        /// <summary>
        /// A few boxes off their stacks - singles dropped in the working lanes, mid-move.
        /// A yard where every container is squared away reads as closed for the weekend.
        /// </summary>
        static void BuildLooseBoxes(
            PortLayout.Layout layout,
            PrefabDatabase.ZonePalette palette,
            System.Random rng,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (palette.portContainers == null || palette.portContainers.Length == 0
                || layout.Stacks.Count == 0)
                return;

            var along = Mathf.Abs(layout.Seaward.z) > 0.5f ? Vector3.right : Vector3.forward;
            var loose = 2 + rng.Next(3);

            for (var i = 0; i < loose; i++)
            {
                var box = palette.portContainers[rng.Next(palette.portContainers.Length)];
                if (!box)
                    continue;

                // Beside a random stack, shifted into the working lane seaward of it and
                // skewed a few degrees - a box set down, not a box racked.
                var stack = layout.Stacks[rng.Next(layout.Stacks.Count)];
                var centre = new Vector3(stack.Area.Centre.x, 0f, stack.Area.Centre.y);
                var slide = ((float)rng.NextDouble() - 0.5f)
                          * Extent(new Vector2(stack.Area.Size.x, stack.Area.Size.y), along);

                var yaw = Mathf.Atan2(layout.Seaward.x, layout.Seaward.z) * Mathf.Rad2Deg
                        + ((float)rng.NextDouble() - 0.5f) * 16f;
                var position = centre + along * slide
                             - layout.Seaward * (PortLayout.StackBandDepth * 0.5f + 4f);

                Spawn(box, position, yaw, parent, spawn, occupied, placed);
            }
        }

        /// <summary>
        /// The pier finger: straight modules marching seaward, small boats moored alongside.
        /// The deck rides at the module's own height - its legs were authored to reach 3m
        /// down, which is below the sunken water by construction.
        /// </summary>
        static void BuildPier(
            PortLayout.Layout layout,
            PrefabDatabase.ZonePalette palette,
            System.Random rng,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (!layout.HasPier || !palette.pierSegment)
                return;

            var yaw = Mathf.Atan2(layout.Seaward.x, layout.Seaward.z) * Mathf.Rad2Deg;

            for (var i = 0; i < PortLayout.PierSegments; i++)
            {
                var centre = layout.PierRoot
                           + layout.Seaward * (PortLayout.PierModule * (i + 0.5f));
                SpawnFree(palette.pierSegment, centre, yaw, parent, spawn, placed);
            }

            if (palette.portBoats == null || palette.portBoats.Length == 0)
                return;

            // One boat either side, nose to the sea, staggered down the finger.
            var lateral = Vector3.Cross(Vector3.up, layout.Seaward);
            var sides = rng.Next(2) == 0 ? 1 : 2;

            for (var i = 0; i < sides; i++)
            {
                var boat = palette.portBoats[rng.Next(palette.portBoats.Length)];
                if (!boat)
                    continue;

                var side = i == 0 ? 1f : -1f;
                var print = PrefabBounds.FootprintXZ(boat, yaw);
                var offset = Extent(print, lateral) * 0.5f + PortLayout.PierModule * 0.5f + 0.8f;

                var centre = layout.PierRoot
                           + layout.Seaward * (PortLayout.PierModule * (2f + i * 2f))
                           + lateral * side * offset;
                centre.y = -PortLayout.WaterDrop;

                var bounds = new Bounds(new Vector3(centre.x, 0f, centre.z),
                                        new Vector3(print.x, 4f, print.y));
                var blocked = false;
                foreach (var existing in occupied)
                    if (existing.Intersects(bounds)) { blocked = true; break; }
                if (blocked)
                    continue;

                SpawnFree(boat, centre, yaw, parent, spawn, placed);
                occupied.Add(bounds);
            }
        }

        /// <summary>The lorry at the stack face - BuildLorries from the works, verbatim.</summary>
        static void BuildLorries(
            PortLayout.Layout layout,
            VehiclePicker vehicles,
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
                Spawn(lorry, centre, yaw, parent, spawn, occupied, placed);
            }
        }

        /// <summary>
        /// The small stuff that says working waterfront: pallets, timber, the anchor, stood
        /// singly along the coping between the cranes - and buoys out on the water, the one
        /// prop that goes to sea.
        /// </summary>
        static void BuildQuayProps(
            PortLayout.Layout layout,
            PrefabDatabase.ZonePalette palette,
            System.Random rng,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (palette.portProps == null || palette.portProps.Length == 0)
                return;

            var quay = layout.Quay;
            var along = Mathf.Abs(layout.Seaward.z) > 0.5f ? Vector3.right : Vector3.forward;
            var span = Extent(new Vector2(quay.Size.x, quay.Size.y), along);
            var quayCentre = new Vector3(quay.Centre.x, 0f, quay.Centre.y);

            // The coping line, 1.5m inside the water's edge.
            var coping = quayCentre + layout.Seaward
                       * (Extent(new Vector2(quay.Size.x, quay.Size.y), layout.Seaward) * 0.5f - 1.5f);

            var count = Mathf.Clamp(Mathf.RoundToInt(span / 18f), 2, 6);
            for (var i = 0; i < count; i++)
            {
                var prop = palette.portProps[rng.Next(palette.portProps.Length)];
                if (!prop)
                    continue;

                var t = (i + 0.5f) / count + (float)(rng.NextDouble() - 0.5) * 0.4f / count;
                var position = coping + along * ((t - 0.5f) * span);
                var yaw = (float)rng.NextDouble() * 360f;

                Spawn(prop, position, yaw, parent, spawn, occupied, placed);
            }

            // Buoys: over the water, past the ship, sunk to the same plane it floats on.
            var buoy = FindByName(palette.portProps, "buoy");
            if (!buoy)
                return;

            for (var i = 0; i < BuoyCount; i++)
            {
                var a = ((float)rng.NextDouble() - 0.5f) * span;
                var reach = PortLayout.ShipStandoff + 12f + (float)rng.NextDouble() * BuoyReach;
                var position = quayCentre + along * a + layout.Seaward
                             * (Extent(new Vector2(quay.Size.x, quay.Size.y), layout.Seaward) * 0.5f + reach);
                position.y = -PortLayout.WaterDrop;

                var bounds = new Bounds(new Vector3(position.x, 0f, position.z), Vector3.one);
                var blocked = false;
                foreach (var existing in occupied)
                    if (existing.Intersects(bounds)) { blocked = true; break; }
                if (blocked)
                    continue;

                SpawnFree(buoy, position, (float)rng.NextDouble() * 360f, parent, spawn, placed);
            }
        }

        static GameObject FindByName(GameObject[] props, string needle)
        {
            foreach (var prop in props)
                if (prop && prop.name.ToLowerInvariant().Contains(needle))
                    return prop;
            return null;
        }

        /// <summary>
        /// Hands the walk graph and the compound to the director, as a marker on an empty -
        /// the WorksYard pattern. The layout is replayable; the marker exists so the RUNTIME
        /// never needs to replay it.
        /// </summary>
        static void Publish(PortLayout.Layout layout, int blockId, Transform parent)
        {
            var points = new List<Entities.PortMarker.WorkPoint>();

            foreach (var p in layout.QuayLanePoints)
                points.Add(new Entities.PortMarker.WorkPoint
                {
                    Position = p,
                    Lane = Entities.PortMarker.WorkLane.Quay,
                });

            foreach (var p in layout.ApronLanePoints)
                points.Add(new Entities.PortMarker.WorkPoint
                {
                    Position = p,
                    Lane = Entities.PortMarker.WorkLane.Apron,
                });

            var marker = new GameObject($"port_yard_{blockId}");
            marker.transform.SetParent(parent, false);
            marker.transform.SetPositionAndRotation(
                new Vector3(layout.Wall.Centre.x, 0f, layout.Wall.Centre.y), Quaternion.identity);

            // The coping line: the quay rect's water-side edge, end to end.
            var quay = layout.Quay;
            var alongX = Mathf.Abs(layout.Seaward.z) > 0.5f;
            var edge = alongX
                ? (layout.Seaward.z > 0f ? quay.Max.y : quay.Min.y)
                : (layout.Seaward.x > 0f ? quay.Max.x : quay.Min.x);
            var from = alongX ? new Vector3(quay.Min.x, 0f, edge) : new Vector3(edge, 0f, quay.Min.y);
            var to = alongX ? new Vector3(quay.Max.x, 0f, edge) : new Vector3(edge, 0f, quay.Max.y);

            var port = marker.AddComponent<Entities.PortMarker>();
            port.SetCompound(
                blockId, layout.Wall, layout.HasGate, layout.GateCentre, layout.GateOutward,
                from, to, points.ToArray(),
                layout.AlongX, layout.QuayLaneC, layout.ApronLaneC, layout.AisleA);
            port.SetBerth(layout.HasBerth, layout.BerthCentre, layout.BerthYaw);
        }

        static float PadWidth(IndustrialLayout.Pad pad) =>
            Extent(new Vector2(pad.Area.Size.x, pad.Area.Size.y),
                   Vector3.Cross(Vector3.up, pad.Outward));

        static float PadDepth(IndustrialLayout.Pad pad) =>
            Extent(new Vector2(pad.Area.Size.x, pad.Area.Size.y), pad.Outward);

        /// <summary>Size of an XZ footprint along an axis-aligned direction.</summary>
        static float Extent(Vector2 footprint, Vector3 direction) =>
            Mathf.Abs(direction.x) > 0.5f ? footprint.x : footprint.y;

        /// <summary>
        /// Overlap-checked placement - IndustrialDresser.Spawn's shape, kept as its own copy
        /// under the same not-while-adding-a-zone precedent that copy itself cites.
        /// </summary>
        static GameObject Spawn(
            GameObject prefab,
            Vector3 position,
            float yaw,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (!prefab)
                return null;

            var footprint = PrefabBounds.FootprintXZ(prefab, yaw);
            var bounds = new Bounds(new Vector3(position.x, 0f, position.z),
                                    new Vector3(footprint.x, 1f, footprint.y));

            foreach (var existing in occupied)
                if (existing.Intersects(bounds))
                    return null;

            var instance = SpawnFree(prefab, position, yaw, parent, spawn, placed);
            occupied.Add(bounds);
            return instance;
        }

        /// <summary>
        /// Placement with no occupancy test, for the pieces that legitimately overlap the
        /// ground plane's idea of "taken": the second container tier, the ship and boats in
        /// the water, the pier over it.
        /// </summary>
        static GameObject SpawnFree(
            GameObject prefab,
            Vector3 position,
            float yaw,
            Transform parent,
            SpawnPrefab spawn,
            List<GameObject> placed)
        {
            if (!prefab)
                return null;

            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var localCentre = PrefabBounds.Get(prefab).center;
            var offset = rotation * new Vector3(localCentre.x, 0f, localCentre.z);

            var instance = spawn(prefab,
                                 new Vector3(position.x - offset.x, position.y, position.z - offset.z),
                                 rotation, parent);
            placed.Add(instance);
            return instance;
        }
    }
}
