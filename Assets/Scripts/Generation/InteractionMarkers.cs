using UnityEngine;
using LivingCity.Entities;

namespace LivingCity.Generation
{
    /// <summary>
    /// The post-pass that stamps interaction markers onto the generated city: BenchSeats on
    /// every bench, ShopEntrance on every street-facing shopfront, BuildingDoor on every
    /// street-facing building people would go in and out of. One sweep over the
    /// finished hierarchy by prefab name, rather than edits at each of the five separate
    /// places that spawn a bench - and it works identically for the editor build (clean
    /// prefab names) and a runtime rebuild ("(Clone)" suffix), which StartsWith absorbs.
    ///
    /// The doors need the CityGrid to decide which facades front a pavement, which is why
    /// Attach takes one. This is the only moment it is available: CityGrid is not serialized,
    /// so CityBuilder.Grid is null at Play in the shipping path - the answer has to be baked
    /// here or not at all.
    ///
    /// Runs at the end of CityBuilder.Build, so the markers are baked into the saved scene
    /// (the SmokeVent persistence pattern) and precede the editor's MarkStaticForBatching
    /// sweep - which they do not need a skip from, being data-only.
    /// </summary>
    public static class InteractionMarkers
    {
        /// <summary>
        /// Bench seat geometry by prefab, measured off the binary FBXs rather than eyeballed
        /// (vertex y-histogram; the seat plane is the densest horizontal band):
        ///
        ///                SM_Prop_Bench_Seat_01   SM_Prop_Bench_Seat_02   SM_Prop_Bench_Seat_03
        ///   bounds       2.21 x 1.04 x 0.58      2.21 x 1.05 x 0.69      2.07 x 0.47 x 0.61
        ///   seat top Y   0.50                    0.50                    0.42
        ///   seat slab Z  -0.23 .. +0.26          -0.36 .. +0.30          -0.30 .. +0.30
        ///   backrest     on -Z                   on -Z                   none
        ///
        /// The backrest on -Z confirms front = +Z, the same convention the polyperfect
        /// benches carried. X spreads sitters along the slats (2.2 m carries two adults).
        /// Y is the seat TOP, not the ground - the agent subtracts
        /// PedestrianAnimation.SitContactHeight scaled to the rig (a CLIP-derived constant,
        /// unchanged by the bench swap). Z is derived, not tuned: the seated pelvis sits
        /// SitPelvisBack behind the root, so the root goes that far in FRONT of wherever
        /// the pelvis has to land.
        /// </summary>
        const float SeatTop = 0.50f;
        const float BacklessSeatTop = 0.42f;

        /// <summary>Just clear of the backed benches' rest, whose base is the slab's back edge.</summary>
        const float BackedPelvisZ = -0.18f;

        /// <summary>Centred on Bench_Seat_03's backless slab - nothing behind to keep clear of.</summary>
        const float BacklessPelvisZ = 0f;

        const float BackedSeatForward = PedestrianAnimation.SitPelvisBack + BackedPelvisZ;
        const float BacklessSeatForward = PedestrianAnimation.SitPelvisBack + BacklessPelvisZ;

        static readonly Vector3[] TwoSeats =
        {
            new Vector3(-0.55f, SeatTop, BackedSeatForward),
            new Vector3(0.55f, SeatTop, BackedSeatForward),
        };

        static readonly Vector3[] TwoSeatsBackless =
        {
            new Vector3(-0.55f, BacklessSeatTop, BacklessSeatForward),
            new Vector3(0.55f, BacklessSeatTop, BacklessSeatForward),
        };

        /// <summary>
        /// The commercial prefabs whose facade actually holds a street door. The Shops group
        /// also contains building-firestation - engine doors, nobody pops in - and corner
        /// pieces are excluded below because their forward aligns a quadrant, not a facade.
        /// </summary>
        static readonly string[] ShopFronts =
            { "building-cafe", "building-restaurant", "building-post", "building-burger-joint" };

        /// <summary>
        /// The seat table for a bench by prefab name, or null if the name is not a bench.
        /// Public so PedestrianModelTests can check the offsets against the measured bench
        /// geometry without a scene - these numbers are only as good as their derivation, and
        /// nothing else in the build would notice if one drifted.
        /// </summary>
        public static Vector3[] SeatsFor(string prefabName)
        {
            // Bench_Seat_01/02 share a slab length and a backrest; _03 is the backless one.
            // Planter_Bench and Pier_Bench are deliberately NOT here - they dress the park
            // visually but have not been measured for sitters yet.
            if (prefabName.StartsWith("SM_Prop_Bench_Seat_03"))
                return TwoSeatsBackless;
            if (prefabName.StartsWith("SM_Prop_Bench_Seat_"))
                return TwoSeats;
            return null;
        }

        public static int Attach(Transform generatedRoot, CityGrid grid = null)
        {
            var attached = 0;

            foreach (var tf in generatedRoot.GetComponentsInChildren<Transform>(true))
            {
                var name = tf.name;

                var seats = SeatsFor(name);
                if (seats != null)
                {
                    attached += AddBench(tf, seats);
                    continue;
                }

                // Not an else-if against the door: a cafe is both. Its shopfront is a place to
                // pop into and come straight back out of, its door is a way in and out of the
                // city - PedestrianAgent rolls for them separately.
                if (IsShopFront(name))
                    attached += AddShop(tf);

                // The gun shop is a Shops-group storefront like the post office, but its
                // marker is the PLAYER's counter (right-click target + overlay face), not a
                // pedestrian ShopEntrance - see GunShopMarker. Same corner exclusion, same
                // door derivation.
                if (name.StartsWith(GunShopMarker.PrefabName) && !name.Contains("corner"))
                    attached += AddGunShop(tf);

                if (BuildingDoorRule.NameQualifies(name))
                    attached += AddDoor(tf, grid);
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

        /// <summary>
        /// Stamps the seats, OVERWRITING any already there. Returning early on an existing
        /// component would mean a re-run over a saved scene silently keeps whatever offsets
        /// were baked the first time - and since the offsets live here, an edit to the table
        /// would appear to do nothing. Same overwrite discipline as the generated animator
        /// controller: the table is the source, the scene is output.
        /// </summary>
        static int AddBench(Transform tf, Vector3[] seats)
        {
            var existing = tf.GetComponent<BenchSeats>();
            if (existing)
            {
                existing.SetSeats(seats);
                return 0;
            }

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

        static int AddGunShop(Transform tf)
        {
            if (tf.GetComponent<GunShopMarker>())
                return 0;

            var bounds = LocalBounds(tf);
            tf.gameObject.AddComponent<GunShopMarker>()
                .SetDoor(new Vector3(bounds.center.x, 0f, bounds.max.z));
            return 1;
        }

        /// <summary>
        /// A street door on an ordinary building, if BuildingDoorRule says this facade fronts
        /// a pavement in a zone people walk into. The offset is derived the same way a
        /// shopfront's is - the local +Z facade centre at ground level - because the placement
        /// convention behind that is the same for every non-corner building in the city.
        /// </summary>
        static int AddDoor(Transform tf, CityGrid grid)
        {
            if (tf.GetComponent<BuildingDoor>())
                return 0;

            var bounds = LocalBounds(tf);
            var offset = new Vector3(bounds.center.x, 0f, bounds.max.z);

            if (!BuildingDoorRule.Eligible(grid, tf.name, tf.TransformPoint(offset), tf.forward))
                return 0;

            tf.gameObject.AddComponent<BuildingDoor>().SetDoor(offset);
            return 1;
        }

        /// <summary>
        /// The instance's mesh bounds in its own root space - PrefabBounds.Get for a live
        /// instance, minus the cache (each building is measured exactly once, here).
        /// Public for BlockBuilder, which derives the police station's door the same way at
        /// placement time - the station cannot wait for the name sweep because its recessed
        /// facade may fail BuildingDoorRule and it must have a door regardless.
        /// </summary>
        public static Bounds LocalBounds(Transform root)
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
