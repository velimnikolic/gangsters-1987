using UnityEngine;
using LivingCity.Entities;

namespace LivingCity.Generation
{
    /// <summary>
    /// The post-pass that stamps interaction markers onto the generated city: BenchSeats on
    /// every bench, ShopEntrance on every street-facing shopfront. One sweep over the
    /// finished hierarchy by prefab name, rather than edits at each of the five separate
    /// places that spawn a bench - and it works identically for the editor build (clean
    /// prefab names) and a runtime rebuild ("(Clone)" suffix), which StartsWith absorbs.
    ///
    /// Runs at the end of CityBuilder.Build, so the markers are baked into the saved scene
    /// (the SmokeVent persistence pattern) and precede the editor's MarkStaticForBatching
    /// sweep - which they do not need a skip from, being data-only.
    /// </summary>
    public static class InteractionMarkers
    {
        /// <summary>
        /// Bench seat geometry by prefab, measured: bench-old is 1.67 wide (2 sitters),
        /// bench-forest 2.93 (3). X spreads along the slats, Z puts the sit root just ahead
        /// of the front edge - the sit-down clip carries the hips back onto the seat.
        /// </summary>
        const float SeatForward = 0.35f;

        static readonly Vector3[] TwoSeats =
        {
            new Vector3(-0.42f, 0f, SeatForward),
            new Vector3(0.42f, 0f, SeatForward),
        };

        static readonly Vector3[] ThreeSeats =
        {
            new Vector3(-0.95f, 0f, SeatForward),
            new Vector3(0f, 0f, SeatForward),
            new Vector3(0.95f, 0f, SeatForward),
        };

        /// <summary>
        /// The commercial prefabs whose facade actually holds a street door. The Shops group
        /// also contains building-firestation - engine doors, nobody pops in - and corner
        /// pieces are excluded below because their forward aligns a quadrant, not a facade.
        /// </summary>
        static readonly string[] ShopFronts = { "building-cafe", "building-restaurant", "building-post" };

        public static int Attach(Transform generatedRoot)
        {
            var attached = 0;

            foreach (var tf in generatedRoot.GetComponentsInChildren<Transform>(true))
            {
                var name = tf.name;

                if (name.StartsWith("bench-old"))
                    attached += AddBench(tf, TwoSeats);
                else if (name.StartsWith("bench-forest"))
                    attached += AddBench(tf, ThreeSeats);
                else if (IsShopFront(name))
                    attached += AddShop(tf);
            }

            return attached;
        }

        static bool IsShopFront(string name)
        {
            if (name.Contains("corner"))
                return false;

            foreach (var prefix in ShopFronts)
                if (name.StartsWith(prefix))
                    return true;

            return false;
        }

        static int AddBench(Transform tf, Vector3[] seats)
        {
            if (tf.GetComponent<BenchSeats>())
                return 0;

            tf.gameObject.AddComponent<BenchSeats>().SetSeats(seats);
            return 1;
        }

        static int AddShop(Transform tf)
        {
            if (tf.GetComponent<ShopEntrance>())
                return 0;

            // The facade plane is local +Z for every placed non-corner building; the door
            // goes at its centre, on the ground. Measured off the instance's own meshes
            // rather than assumed, so a deeper prefab still gets its door on the wall.
            var bounds = LocalBounds(tf);
            tf.gameObject.AddComponent<ShopEntrance>()
                .SetDoor(new Vector3(bounds.center.x, 0f, bounds.max.z));
            return 1;
        }

        /// <summary>
        /// The instance's mesh bounds in its own root space - PrefabBounds.Get for a live
        /// instance, minus the cache (each building is measured exactly once, here).
        /// </summary>
        static Bounds LocalBounds(Transform root)
        {
            var toLocal = root.worldToLocalMatrix;
            var bounds = new Bounds();
            var initialised = false;

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (!mesh)
                    continue;

                var matrix = toLocal * filter.transform.localToWorldMatrix;
                var centre = mesh.bounds.center;
                var extents = mesh.bounds.extents;

                for (var corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);

                    var point = matrix.MultiplyPoint3x4(centre + offset);

                    if (!initialised)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        initialised = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return initialised ? bounds : new Bounds(Vector3.zero, Vector3.one);
        }
    }
}
