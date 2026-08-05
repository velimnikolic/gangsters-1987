using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Everything on a car park that is not asphalt, paint or a car: the boundary fence, the
    /// piers that terminate it, the attendant's booth and the barrier across the entrance.
    ///
    /// The pack has no parking booth under that name - "Amusement Park_T/ticket-ride-booth" is a
    /// generic 2.24 x 2.80 x 1.71 kiosk and reads as one the moment it is not standing next to a
    /// carousel. The fence is Fence_T's 2m railing rather than the 4m stone wall: the wall is
    /// 1.5m of solid masonry and from the default isometric camera it hides the front row of
    /// cars, which is the thing being added. Railing shows through; the 1.83m stone piers at the
    /// corners and either side of the gate give it the weight the railing alone lacks.
    ///
    /// There is no barrier prefab at all, so the boom is one white box resting on the gate pier.
    /// It is the single most droppable thing here - one call, one method.
    /// </summary>
    public static class ParkingLotDresser
    {
        /// <summary>
        /// Fence line, in from the block rect. The rect already reaches into the road tile as far
        /// as SidewalkClearance, so this only has to stop the piers from overhanging their own
        /// edge - the pavement is another 6.4 away.
        /// </summary>
        const float FenceInset = 0.6f;

        /// <summary>Half a pier, so a run stops short of the one on its corner.</summary>
        const float PierHalf = 0.4f;

        /// <summary>How far inside the gate the booth stands.</summary>
        const float BoothSetback = 3f;

        /// <summary>Clear of the opening, so the booth never narrows the way in.</summary>
        const float BoothClearance = 1.6f;

        const float BoomHeight = 1f;
        const float BoomThickness = 0.14f;

        public static void Build(
            Vector2 min,
            Vector2 max,
            ParkingLayout.Layout layout,
            PrefabDatabase.ZonePalette palette,
            Material boomMaterial,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            var fenceMin = new Vector2(min.x + FenceInset, min.y + FenceInset);
            var fenceMax = new Vector2(max.x - FenceInset, max.y - FenceInset);

            if (fenceMax.x - fenceMin.x < 4f || fenceMax.y - fenceMin.y < 4f)
                return;

            BuildFence(fenceMin, fenceMax, layout, palette, parent, spawn, placed);

            if (!layout.HasGate)
                return;

            BuildBooth(layout, palette, parent, spawn, occupied, placed);
            BuildBoom(layout, boomMaterial, parent, placed);
        }

        /// <summary>
        /// The four runs and the six piers. Each side is walked as a parameter from one corner to
        /// the next, with the gate subtracted from whichever side it falls on, so the opening is
        /// a gap in the fence rather than a separate object placed on top of it.
        /// </summary>
        static void BuildFence(
            Vector2 min,
            Vector2 max,
            ParkingLayout.Layout layout,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            List<GameObject> placed)
        {
            if (!palette.fenceSegment)
                return;

            var corners = new[]
            {
                new Vector3(min.x, 0f, min.y),
                new Vector3(max.x, 0f, min.y),
                new Vector3(max.x, 0f, max.y),
                new Vector3(min.x, 0f, max.y),
            };

            var outwards = new[] { Vector3.back, Vector3.right, Vector3.forward, Vector3.left };

            for (var i = 0; i < 4; i++)
            {
                var from = corners[i];
                var to = corners[(i + 1) % 4];
                var span = to - from;
                var length = span.magnitude;
                var along = span / length;

                var start = PierHalf;
                var end = length - PierHalf;

                // Does the gate open through this side? Its outward normal is the test - the
                // layout picked one side of the block and only that one gets a hole in it.
                var gated = layout.HasGate && Vector3.Dot(outwards[i], layout.GateOutward) > 0.9f;

                if (!gated)
                {
                    FenceRun.Lay(palette.fenceSegment, from, along, start, end, parent, spawn, placed);
                }
                else
                {
                    var centre = Vector3.Dot(layout.GateCentre - from, along);
                    var gapFrom = centre - ParkingLayout.GateWidth * 0.5f;
                    var gapTo = centre + ParkingLayout.GateWidth * 0.5f;

                    FenceRun.Lay(palette.fenceSegment, from, along, start, gapFrom - PierHalf, parent, spawn, placed);
                    FenceRun.Lay(palette.fenceSegment, from, along, gapTo + PierHalf, end, parent, spawn, placed);

                    Pier(palette.fencePost, from + along * gapFrom, parent, spawn, placed);
                    Pier(palette.fencePost, from + along * gapTo, parent, spawn, placed);
                }
            }

            foreach (var corner in corners)
                Pier(palette.fencePost, corner, parent, spawn, placed);
        }

        static void Pier(
            GameObject prefab, Vector3 position, Transform parent, SpawnPrefab spawn, List<GameObject> placed)
        {
            if (!prefab)
                return;

            placed.Add(spawn(prefab, position, Quaternion.identity, parent));
        }

        /// <summary>
        /// The attendant's kiosk, beside the entrance and facing across it. Its bounds go into the
        /// block's occupancy list so BuildScatter cannot plant a lamp inside it.
        /// </summary>
        static void BuildBooth(
            ParkingLayout.Layout layout,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (!palette.parkingBooth)
                return;

            var lateral = Vector3.Cross(Vector3.up, layout.GateOutward);

            var position = layout.GateCentre
                         - layout.GateOutward * BoothSetback
                         + lateral * (ParkingLayout.GateWidth * 0.5f + BoothClearance);

            // Front is the pack's local +Z, so looking at the middle of the opening puts the
            // window on the driveway rather than on the fence.
            var facing = layout.GateCentre - position;
            facing.y = 0f;
            var rotation = facing.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(facing.normalized)
                : Quaternion.identity;

            placed.Add(spawn(palette.parkingBooth, position, rotation, parent));

            var footprint = PrefabBounds.FootprintXZ(palette.parkingBooth, rotation.eulerAngles.y);
            occupied.Add(new Bounds(
                new Vector3(position.x, 0f, position.z),
                new Vector3(footprint.x, 1f, footprint.y)));
        }

        /// <summary>
        /// The barrier across the entrance, lowered. One box, because the pack ships nothing that
        /// serves and a striped arm would need two more materials for a prop nobody looks at
        /// twice. Drop this call and the gate simply stands open.
        /// </summary>
        static void BuildBoom(
            ParkingLayout.Layout layout, Material material, Transform parent, List<GameObject> placed)
        {
            if (!material)
                return;

            var lateral = Vector3.Cross(Vector3.up, layout.GateOutward);

            var boom = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boom.name = "parking_boom";
            boom.transform.SetParent(parent, false);
            boom.transform.SetPositionAndRotation(
                layout.GateCentre - layout.GateOutward * 1.2f + Vector3.up * BoomHeight,
                Quaternion.LookRotation(lateral));

            boom.transform.localScale =
                new Vector3(BoomThickness, BoomThickness, ParkingLayout.GateWidth - 2f * PierHalf);

            boom.GetComponent<MeshRenderer>().sharedMaterial = material;

            // CreatePrimitive hands out a BoxCollider. Nothing needs it, and stray colliders near
            // a road are the sort of thing Tile's OverlapBox neighbour probe trips over.
            var collider = boom.GetComponent<Collider>();
            if (collider)
            {
                if (Application.isPlaying)
                    Object.Destroy(collider);
                else
                    Object.DestroyImmediate(collider);
            }

            placed.Add(boom);
        }
    }
}
