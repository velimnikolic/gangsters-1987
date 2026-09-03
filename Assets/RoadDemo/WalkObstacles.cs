using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>What a man on foot walks round when he is OFF the sidewalk graph:
    /// a crew's free stride over the demo floor, the dash across the road to a
    /// fight or away from one, the walk to the car door, the police coming up
    /// from their cruiser. The graph's walkers are kept off the furniture by the
    /// clearance sampled into their stretches (PedLink.Free); off the graph there
    /// is no stretch, so the ground is asked directly, and the answer is made of
    /// three things:
    ///
    ///   the pavement plans   - every prop a scene laid, as its measured footprint
    ///                          (the same SidewalkPlan the dressing wrote into);
    ///                          a scene hands its plan over once at build
    ///   the solids           - buildings, mostly: whatever a scene blocks off by
    ///                          hand, as a box on the ground
    ///   the road             - everyone on it this frame (StreetTraffic.Users:
    ///                          the traffic, the outfit's cars, the police), each
    ///                          the box he stands in, read live
    ///
    /// Kinematic like everything else here - boxes and circles, no physics, no
    /// colliders. A walker asks two things of it: may I stand here (Occupied), and
    /// which way may I go (Steer): the line he wants if it is clear, else the
    /// nearest line off it that is, to either side, and how far it runs.</summary>
    public static class WalkObstacles
    {
        /// <summary>Half a man, shoulder to shoulder - what must clear a thing.</summary>
        public const float Radius = SidewalkDressing.WalkRadius;

        /// <summary>The outfit's footprint while a free-ground route is planned and
        /// walked. The pavement graph still keeps the ordinary shoulder berth above;
        /// this narrower profile is what lets a crew use the real gaps between street
        /// furniture instead of declaring a clear passage closed.</summary>
        public const float CrewTravelRadius = Radius * 0.5f;

        /// <summary>A centre probe for exceptional recovery. A man merely brushing the
        /// edge of his travel berth is not inside the prop and must not be teleported.</summary>
        public const float OverlapProbeRadius = 0.1f;

        static readonly List<SidewalkPlan> _props = new List<SidewalkPlan>();
        static readonly IReadOnlyList<SidewalkPlan> PropsView = _props.AsReadOnly();
        static readonly string[] DecorativeGenericTokens =
        {
            "Bottle_", "Button_", "Chain_", "Clock_", "Coin_", "Food_",
            "Hook_", "Key_", "Keypad_", "Lever_", "Light_", "Manhole_",
            "Medkit_", "Mug_", "Papers_", "Plate_", "Pot_", "Potion_", "Rope_",
            "Screen_", "Skull_", "Switch_",
        };

        /// <summary>The registered pavement plans in the scene, exposed read-only so a
        /// caller cannot add one without also invalidating the route lattice.</summary>
        public static IReadOnlyList<SidewalkPlan> Props => PropsView;

        // Props that arrive already composed inside a block prefab, rather than through
        // StreetKit/SidewalkDressing. They are still furniture, not walls: walkers and
        // cover see them, while sight lines keep the existing wall-only policy.
        static SidewalkPlan _composedProps = new SidewalkPlan();

        // the scene's own solids, kept in a plan of their own so they bucket the same way
        static SidewalkPlan _solids = new SidewalkPlan();

        /// <summary>The corners of everything blocked off so far - the ground a man on
        /// foot has to find his way across. Empty until something is blocked, which is
        /// why Max is below Min to start with.</summary>
        public static Vector2 Min = new Vector2(float.MaxValue, float.MaxValue);
        public static Vector2 Max = new Vector2(float.MinValue, float.MinValue);

        /// <summary>Bumped every time the ground changes, so anything that reads the
        /// map (WalkRoute) can tell its own copy is out of date without comparing it.
        /// </summary>
        public static int Version;

        /// <summary>The ground that is the CITY, rectangle by rectangle: the street
        /// grid and every quarter hung off it. This is a fence, not an obstacle - the
        /// walls above stop a man walking THROUGH something, and this stops him walking
        /// OUT of the town altogether, off into the wilderness and down to the sea,
        /// which past the last road is all there is. Only whoever chooses where to go
        /// asks it; the crowd never leaves the graph, so the graph already answers for
        /// them. Empty means no fence was ever set - a lab scene with one block in it -
        /// and then everywhere is in, which is the only sensible reading of "nobody said
        /// where the town ends".</summary>
        public static readonly List<Rect> City = new List<Rect>();

        /// <summary>Is this ground the city's at all?</summary>
        public static bool InCity(Vector3 p)
        {
            if (City.Count == 0) return true;
            for (int i = 0; i < City.Count; i++)
            {
                var r = City[i];
                if (p.x >= r.xMin && p.x <= r.xMax && p.z >= r.yMin && p.z <= r.yMax) return true;
            }
            return false;
        }

        /// <summary>The nearest ground that IS the city's - a point ordered out in the
        /// wilderness (a click past the last street, a spot reckoned off the hem) is
        /// pulled back to the fence, half a metre inside it so the man stood there is
        /// stood ON the floor and not balanced on its edge.</summary>
        public static Vector3 ClampToCity(Vector3 p)
        {
            if (City.Count == 0 || InCity(p)) return p;
            var best = p;
            float bestD = float.MaxValue;
            for (int i = 0; i < City.Count; i++)
            {
                var r = City[i];
                var q = new Vector3(
                    Mathf.Clamp(p.x, r.xMin + 0.5f, r.xMax - 0.5f), p.y,
                    Mathf.Clamp(p.z, r.yMin + 0.5f, r.yMax - 0.5f));
                float d = (q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z);
                if (d < bestD) { bestD = d; best = q; }
            }
            return best;
        }

        static void Include(float xMin, float xMax, float zMin, float zMax)
        {
            Min.x = Mathf.Min(Min.x, xMin); Min.y = Mathf.Min(Min.y, zMin);
            Max.x = Mathf.Max(Max.x, xMax); Max.y = Mathf.Max(Max.y, zMax);
        }

        static void Grew(float xMin, float xMax, float zMin, float zMax)
        {
            Include(xMin, xMax, zMin, zMax);
            Version++;
        }

        /// <summary>
        /// Put a mutable pavement plan into the walking ledger. Existing solid boxes
        /// establish the route lattice bounds now; later Take/Pop/Reframe operations
        /// invalidate it through the plan's change signal.
        /// </summary>
        public static bool RegisterPlan(SidewalkPlan plan)
        {
            // A dressing plan is deliberately registered before it is populated. Its
            // later Take/Pop calls are how the walking ledger learns that furniture
            // appeared; rejecting an empty mutable plan loses that subscription and
            // makes every subsequently placed table/chair invisible to the crew.
            if (plan == null || _props.Contains(plan)) return false;
            _props.Add(plan);
            plan.Changed += PlanChanged;
            Include(plan);
            Version++;
            return true;
        }

        /// <summary>Remove a plan whose owning scene/root is going away.</summary>
        public static bool UnregisterPlan(SidewalkPlan plan)
        {
            if (plan == null || !_props.Remove(plan)) return false;
            plan.Changed -= PlanChanged;
            // Bounds are deliberately a high-water mark for this play session. They
            // need not shrink to answer occupancy correctly, and retaining them avoids
            // a full-city rescan plus a navigation-grid address change every time a
            // cached residential view leaves the camera window.
            Version++;
            return true;
        }

        static void PlanChanged(SidewalkPlan plan, SidewalkPlan.Box box, SidewalkPlan.Change change)
        {
            if (change == SidewalkPlan.Change.Added) Include(box);
            // Reframe reports one aggregate change rather than one event per box, so
            // its payload is intentionally default. Include the moved plan itself.
            else if (change == SidewalkPlan.Change.Reframed) Include(plan);
            // A removal can leave conservative bounds behind, which is harmless and
            // avoids rescanning a city while SidewalkDressing tries and rejects props.
            Version++;
        }

        static void Include(SidewalkPlan plan)
        {
            if (plan == null) return;
            var boxes = plan.Boxes;
            for (int i = 0; i < boxes.Count; i++) Include(boxes[i]);
        }

        static void Include(in SidewalkPlan.Box box)
        {
            if (!box.Solid || box.KeepClear || box.H.x <= 0f || box.H.y <= 0f) return;
            float rx = Mathf.Abs(box.Ax.x) * box.H.x + Mathf.Abs(box.Az.x) * box.H.y;
            float rz = Mathf.Abs(box.Ax.y) * box.H.x + Mathf.Abs(box.Az.y) * box.H.y;
            Include(box.C.x - rx, box.C.x + rx, box.C.y - rz, box.C.y + rz);
        }

        static void RebuildBounds()
        {
            Min = new Vector2(float.MaxValue, float.MaxValue);
            Max = new Vector2(float.MinValue, float.MinValue);
            Include(_solids);
            Include(_composedProps);
            for (int i = 0; i < _props.Count; i++) Include(_props[i]);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Forget()
        {
            for (int i = 0; i < _props.Count; i++)
                if (_props[i] != null) _props[i].Changed -= PlanChanged;
            _props.Clear();
            _solids = new SidewalkPlan();
            _composedProps = new SidewalkPlan();
            Near.Clear();
            City.Clear();
            Min = new Vector2(float.MaxValue, float.MaxValue);
            Max = new Vector2(float.MinValue, float.MinValue);
            Version++;
        }

        // ------------------------------------------------------------------ the solids

        /// <summary>Block off this ground - a building's footprint, world axes. The
        /// rise is how HIGH the thing stands, and the only thing that reads it is the
        /// sight line (<see cref="Sees"/>): a man sees over a parked car and not over a
        /// wall. Left out, the thing is taken for a wall, which is what a caller that
        /// never measured a height is nearly always blocking off.</summary>
        public static void Block(float xMin, float xMax, float zMin, float zMax, float rise = 0f)
        {
            if (xMax <= xMin || zMax <= zMin) return;
            Grew(xMin, xMax, zMin, zMax);
            var box = SidewalkPlan.Make(
                new Vector2((xMin + xMax) * 0.5f, (zMin + zMax) * 0.5f), 0f,
                new Vector2((xMax - xMin) * 0.5f, (zMax - zMin) * 0.5f), solid: true);
            box.Rise = rise;
            _solids.Take(box);
        }

        /// <summary>Block off the ground under these world bounds - which carry the
        /// thing's height, so the sight line gets it for nothing.</summary>
        public static void Block(Bounds b) =>
            Block(b.min.x, b.max.x, b.min.z, b.max.z, b.size.y);

        /// <summary>Block off an oriented box: centre, yaw in degrees, half extents
        /// in its own frame.</summary>
        public static void Block(Vector3 centre, float yaw, Vector2 half)
        {
            float reach = half.magnitude;
            Grew(centre.x - reach, centre.x + reach, centre.z - reach, centre.z + reach);
            _solids.Take(SidewalkPlan.Make(new Vector2(centre.x, centre.z), yaw, half, solid: true));
        }

        /// <summary>Register one fixed piece of furniture without turning it into a
        /// wall for visibility/cover rules.</summary>
        public static void BlockProp(in SidewalkPlan.Box box)
        {
            if (!box.Solid || box.H.x <= 0f || box.H.y <= 0f) return;
            Include(box);
            Version++;
            _composedProps.Take(box);
        }

        /// <summary>Register physical props already nested in a composed block prefab.
        /// Ground slabs are deliberately excluded by name; rooftop attachments are
        /// excluded by the walk-height slice. Returns the number registered.</summary>
        public static int BlockComposedProps(Transform root, float groundY)
            => BlockComposedProps(root, _ => groundY);

        /// <summary>Finalize several static prop roots in one pass.</summary>
        public static int BlockComposedProps(float groundY, params Transform[] roots)
            => BlockComposedProps(_ => groundY, roots);

        /// <summary>Terrain-aware finalization for several static prop roots.</summary>
        public static int BlockComposedProps(System.Func<Vector3, float> groundAt,
                                             params Transform[] roots)
        {
            if (groundAt == null || roots == null) return 0;
            int taken = 0;
            for (int i = 0; i < roots.Length; i++)
                taken += BlockComposedProps(roots[i], groundAt);
            return taken;
        }

        /// <summary>Terrain-aware form of <see cref="BlockComposedProps(Transform,float)"/>.</summary>
        public static int BlockComposedProps(Transform root, System.Func<Vector3, float> groundAt)
        {
            if (root == null || groundAt == null) return 0;
            return CollectComposedProps(root, groundAt, null);
        }

        /// <summary>
        /// Measure furniture owned by a streamed visual root without publishing it as
        /// permanent city state. The caller registers the returned plan while the visual
        /// is standing and unregisters it before that visual is cached or recycled.
        /// </summary>
        public static SidewalkPlan ComposedPropPlan(Transform root, float groundY) =>
            ComposedPropPlan(root, _ => groundY);

        /// <summary>Terrain-aware form of <see cref="ComposedPropPlan(Transform,float)"/>.</summary>
        public static SidewalkPlan ComposedPropPlan(Transform root,
                                                     System.Func<Vector3, float> groundAt)
        {
            var plan = new SidewalkPlan();
            if (root != null && groundAt != null) CollectComposedProps(root, groundAt, plan);
            return plan;
        }

        // A null destination means the old, permanent composed-prop ledger. A supplied
        // destination belongs to a streamed view and is deliberately not registered until
        // its complete payload has been measured.
        static int CollectComposedProps(Transform root, System.Func<Vector3, float> groundAt,
                                        SidewalkPlan destination)
        {
            int taken = 0;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;
                float groundY = groundAt(t.position);
                // Harvested residential roots already carry a compact, baked outline of
                // their STRUCTURE. Their root BoxCollider is deliberately the complete
                // lot (terrace, yards and holes included), so it is the wrong answer for
                // walking. The same proxy the TurfMap reads gives us the actual walls
                // without inspecting meshes at runtime. Child furniture is still visited
                // by later iterations and remains independently walk-blocking.
                if (ResidentialStructureFootprints(t, groundY, destination,
                                                    out int structures))
                {
                    taken += structures;
                    continue;
                }
                // CityKit cafes are deliberately single-mesh prefabs named
                // `building-diner` / `building-coffeeshop`. They are not SM_Prop_* and
                // carry no harvested structural proxy, so the furniture-only pass used
                // to register every outside chair and leave the venue shell walkable.
                // Their authored root BoxCollider is the exact shell footprint.
                bool venue = VenueFootprint(t, groundY, out var box);
                if (!venue)
                {
                    if (!PhysicalProp(t) || HasPhysicalPropParent(t, root)) continue;
                    if (!TouchesWalkHeight(t, groundY)) continue;
                    if (!SidewalkPlan.Footprint(t.gameObject, t.position,
                                                t.eulerAngles.y, out box) || !box.Solid)
                        continue;
                }
                // Buildings, hand-authored yards and StreetKit plans are published first.
                // Do not add their child meshes again as furniture: apart from wasting
                // buckets, that would turn building pieces into prop cover.
                if (CoveredByObstacle(box, destination)) continue;
                if (destination != null) destination.Take(box);
                else BlockProp(box);
                taken++;
            }
            return taken;
        }

        /// <summary>Names of composed restaurant shells whose authored collider is a
        /// walking footprint. Kept narrow: parks, courts and car yards are complete
        /// amenity lots too, but their open ground must remain walkable.</summary>
        internal static bool PhysicalVenueName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return ColliderVenueName(name) ||
                   name.EndsWith(" (cafe)", System.StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("dinner", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("dinner (", System.StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("dinner2", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("dinner2 (", System.StringComparison.OrdinalIgnoreCase);
        }

        static bool ColliderVenueName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.StartsWith("building-cafe", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("building-coffeeshop", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("building-diner", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("building-burger-joint", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("building-restaurant", System.StringComparison.OrdinalIgnoreCase);
        }

        static bool PhysicalVenue(Transform t)
        {
            if (t == null) return false;
            // Only CityKit's single-mesh venues have a collider baked from the actual
            // shell. Harvested dinner/pizza/radnja roots use a whole-LOT collider and
            // are deliberately accepted only by ResidentialStructureFootprints.
            if (ColliderVenueName(t.name)) return true;
            // Quay's authored restoran1/restoran2 roots rename building-restaurant and
            // building-cafe. The mesh keeps its source identity on the same transform.
            var filter = t.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null &&
                   ColliderVenueName(filter.sharedMesh.name);
        }

        static bool VenueFootprint(Transform t, float groundY, out SidewalkPlan.Box box)
        {
            box = default;
            if (t == null || !PhysicalVenue(t)) return false;
            var collider = t.GetComponent<BoxCollider>();
            if (collider == null || collider.isTrigger) return false;
            // FinishBuild measures a streamed holder while that holder is inactive.
            // Collider.bounds is a zero box then, so derive the world slice from authored
            // centre/size and the transform instead; this is valid active or inactive.
            var centre = t.TransformPoint(collider.center);
            var scale = t.lossyScale;
            float verticalHalf = Mathf.Abs(collider.size.y * scale.y) * 0.5f;
            const float ankle = 0.06f;
            const float shoulder = 1.9f;
            if (centre.y + verticalHalf < groundY + ankle ||
                centre.y - verticalHalf > groundY + shoulder)
                return false;

            var half = new Vector2(
                Mathf.Abs(collider.size.x * scale.x) * 0.5f,
                Mathf.Abs(collider.size.z * scale.z) * 0.5f);
            // A nested chair or till can inherit the harvested venue's display name.
            // Only the shell-sized collider takes this path; ordinary furniture keeps
            // its measured SM_Prop footprint below.
            if (half.x < 1.25f && half.y < 1.25f) return false;
            box = SidewalkPlan.Make(new Vector2(centre.x, centre.z),
                                    t.eulerAngles.y, half, solid: true);
            box.Rise = verticalHalf * 2f;
            return true;
        }

        // True means this transform owns a structural proxy, even when every one of its
        // masses was already covered by a permanent obstacle. That distinction prevents
        // a covered harvested venue falling through to its deliberately broad lot box.
        static bool ResidentialStructureFootprints(Transform t, float groundY,
                                                    SidewalkPlan destination,
                                                    out int taken)
        {
            taken = 0;
            if (t == null) return false;
            var proxy = t.GetComponent<ResidentialTurfPrefab>();
            // A TurfMap proxy exists on every harvested residential/amenity prefab,
            // including open courts and car yards. It is suitable here only for the
            // named restaurant shells; broad use would turn a roof or grandstand over
            // otherwise open ground into an invisible walking wall.
            if (proxy == null || proxy.MassCount == 0 || !PhysicalVenueName(t.name))
                return false;

            var scale = t.lossyScale;
            for (int i = 0; i < proxy.MassCount; i++)
            {
                var mass = proxy.MassAt(i);
                var footprint = mass.Footprint;
                if (footprint.width <= 0.02f || footprint.height <= 0.02f) continue;

                float localY = (mass.Bottom + mass.Top) * 0.5f;
                var centre = t.TransformPoint(new Vector3(
                    footprint.center.x, localY, footprint.center.y));
                float verticalHalf = Mathf.Abs((mass.Top - mass.Bottom) * scale.y) * 0.5f;
                const float ankle = 0.06f;
                const float shoulder = 1.9f;
                if (centre.y + verticalHalf < groundY + ankle ||
                    centre.y - verticalHalf > groundY + shoulder) continue;

                var half = new Vector2(
                    Mathf.Abs(footprint.width * scale.x) * 0.5f,
                    Mathf.Abs(footprint.height * scale.z) * 0.5f);
                var box = SidewalkPlan.Make(new Vector2(centre.x, centre.z),
                                            t.eulerAngles.y, half, solid: true);
                box.Rise = verticalHalf * 2f;
                if (CoveredByObstacle(box, destination)) continue;
                if (destination != null) destination.Take(box);
                else BlockProp(box);
                taken++;
            }
            return true;
        }

        internal static bool PhysicalPropName(string name)
        {
            if (string.IsNullOrEmpty(name) || DecorativePropName(name)) return false;
            return name.StartsWith("SM_Prop_", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("SM_Gen_Prop_", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("SM_Veh_", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("SM_Gen_Veh_", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("SM_Env_Tree_", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("SM_Env_Fence_", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("SM_Env_Hedge_", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("SM_Bld_Fence", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("SM_Env_SubwayEntrance_", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("container-20", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Garage ", System.StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Parked ", System.StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Container", System.StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Sealed Container", System.StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("tank", System.StringComparison.OrdinalIgnoreCase) ||
                   name.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Polygon Generic's prop family also contains table dressing and wall controls.
        // They may stand over FlatTop, but a crew should not route around a bottle, coin
        // or light switch as though it were street furniture.
        static bool DecorativePropName(string name)
        {
            if (!name.StartsWith("SM_Gen_Prop_", System.StringComparison.OrdinalIgnoreCase))
                return false;
            for (int i = 0; i < DecorativeGenericTokens.Length; i++)
                if (name.IndexOf(DecorativeGenericTokens[i],
                                 System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        // District composers give useful scene names to props ("Pallet", "Bench",
        // "Bollard"), replacing the prefab root name. The shared mesh retains its
        // source-family name, so it is a conservative way to recognise that renamed
        // root without treating every renderer in a prop-oriented root as an obstacle.
        static bool PhysicalProp(Transform t)
        {
            if (t == null) return false;
            if (PhysicalPropName(t.name)) return true;
            var filter = t.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null &&
                PhysicalPropName(filter.sharedMesh.name)) return true;
            var skinned = t.GetComponent<SkinnedMeshRenderer>();
            return skinned != null && skinned.sharedMesh != null &&
                   PhysicalPropName(skinned.sharedMesh.name);
        }

        static bool HasPhysicalPropParent(Transform t, Transform root)
        {
            if (t == root) return false;
            for (var p = t.parent; p != null && p != root; p = p.parent)
                if (PhysicalProp(p)) return true;
            return false;
        }

        static bool CoveredByObstacle(in SidewalkPlan.Box box, SidewalkPlan destination = null)
        {
            if (!StaticOccupied(box.C, destination)) return false;
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    if (!StaticOccupied(box.C + box.Ax * (box.H.x * x) +
                                        box.Az * (box.H.y * z), destination))
                        return false;
            return true;
        }

        static bool StaticOccupied(Vector2 point, SidewalkPlan destination = null)
        {
            const float seam = 0.02f;
            if (destination != null && destination.Occupied(point, seam)) return true;
            if (_solids.Occupied(point, seam) || _composedProps.Occupied(point, seam))
                return true;
            for (int i = 0; i < _props.Count; i++)
                if (_props[i].Occupied(point, seam)) return true;
            return false;
        }

        static bool TouchesWalkHeight(Transform root, float groundY)
        {
            const float ankle = 0.06f;
            const float shoulder = 1.9f;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var b = renderers[i].bounds;
                if (b.max.y >= groundY + ankle && b.min.y <= groundY + shoulder) return true;
            }
            return false;
        }

        /// <summary>Read a stretch of walk against EVERYTHING that has been blocked -
        /// what <see cref="Block"/> was told about as well as every plan a kit registered
        /// in <see cref="Props"/>.
        ///
        /// A crowd on a link only ever knows what its link was sampled against
        /// (PedLink.SampleClearance), and a walk laid across ground more than one pass
        /// furnished has to be read against all of them. A filling station's forecourt is
        /// the case that showed it: the walk runs along the shop front, and the shop, the
        /// hedge, the gas cage and the cars in the parking row are all blocked HERE and
        /// in no street kit's plan - so a walk sampled against the kit alone is a walk
        /// whose crowd goes through a wall.
        ///
        /// Build-time only, like the sampling it wraps. It is deliberately NOT what the
        /// city's own pavements are read against: those are thousands of links against
        /// thousands of building boxes, and the sampling is already the dearest thing in
        /// the load. It is for the short, furnished walks a place lays for itself.</summary>
        public static void SampleWalk(PedLink link, float radius)
        {
            if (link == null) return;
            _against.Clear();
            _against.Add(_solids);
            _against.Add(_composedProps);
            _against.AddRange(Props);
            link.SampleClearance(_against, radius);
        }

        static readonly List<SidewalkPlan> _against = new List<SidewalkPlan>();

        // ------------------------------------------------------------------ the road

        // A road user as the box he stands in, flat. Gathered once per query for
        // the few near enough to matter, so a city's worth of traffic is walked
        // through once and not once per probe.
        struct Box
        {
            public Vector2 C, F, R;
            public float HL, HW;
        }

        static readonly List<Box> Near = new List<Box>();

        static void GatherRoad(Vector2 around, float reach)
        {
            Near.Clear();
            var users = StreetTraffic.Users;
            for (int i = 0; i < users.Count; i++)
            {
                var u = users[i];
                var p = u.RoadPosition;
                var c = new Vector2(p.x, p.z);
                float span = reach + Mathf.Max(u.HalfLength, u.HalfWidth);
                if ((c - around).sqrMagnitude > span * span) continue;
                // StoodCar is entered in StreetTraffic so DRIVERS keep clear, but both
                // of its current producers also enter its measured body in the fixed
                // walking ledger. Counting that same parked body here a second time at
                // the wider live-traffic berth makes a route proved at CrewTravelRadius
                // impossible for the feet to traverse. Moving/temporarily stopped road
                // users retain the full traffic berth. Every StoodCar producer first
                // registers that same measured body in the fixed walking ledger; its
                // transform pivot is not necessarily inside those renderer bounds, so
                // probing the pivot before skipping it can accidentally count the car
                // twice with two different centres and two different footprints.
                if (u is StoodCar) continue;
                var f = u.RoadForward;
                f.y = 0f;
                var fwd = f.sqrMagnitude > 1e-4f ? new Vector2(f.x, f.z).normalized : Vector2.right;
                Near.Add(new Box
                {
                    C = c, F = fwd, R = new Vector2(fwd.y, -fwd.x),
                    HL = u.HalfLength, HW = u.HalfWidth,
                });
            }
        }

        static bool InRoad(Vector2 q, float radius)
        {
            for (int i = 0; i < Near.Count; i++)
            {
                var b = Near[i];
                var d = q - b.C;
                float ox = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, b.F)) - b.HL);
                float oz = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, b.R)) - b.HW);
                if (ox * ox + oz * oz <= radius * radius) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ asking

        static bool OnGround(Vector2 q, float radius) => OnGround(q, radius, 0f);

        static bool OnGround(Vector2 q, float radius, float tallBerth)
        {
            if (_solids.Occupied(q, radius, tallBerth)) return true;
            if (_composedProps.Occupied(q, radius, tallBerth)) return true;
            for (int i = 0; i < _props.Count; i++)
                if (_props[i].Occupied(q, radius, tallBerth)) return true;
            return false;
        }

        /// <summary>Would a man of this radius stood here be inside something that does
        /// not move - a wall, a lot, a bin? The traffic is left out on purpose: this is
        /// the map a way across the city is worked out on, and a way that went round
        /// wherever the cars happened to be standing when it was drawn would be a way
        /// round nothing a moment later. The cars are what the walking itself steers
        /// past, frame by frame (Steer).</summary>
        public static bool Standing(Vector3 p, float radius) =>
            OnGround(new Vector2(p.x, p.z), radius);

        /// <summary>Can this point SEE that one - is there nothing but air between
        /// them? Only the city's own walls are asked (the blocks a scene laid: buildings,
        /// lots, yards). The furniture is deliberately left out: a bin is cover, not a
        /// hiding place, and a sight line that broke on every one of them would flicker
        /// a fight on and off down the length of a dressed street.
        ///
        /// This exists for the crews' eyes (DemoCrews.InSight). Before it, "in sight"
        /// was a RADIUS, so a mob shot at by a car going past kept the car in view
        /// through a block of flats and ran at wherever it actually was - the player
        /// watched crews come to him across a quarter they could not possibly have seen
        /// him cross. A scene that blocked nothing off has nothing in the way and
        /// everything is in sight, which is the only sensible reading of an empty lab
        /// floor.</summary>
        public static bool Sees(Vector3 from, Vector3 to) =>
            !_solids.Blocks(new Vector2(from.x, from.z), new Vector2(to.x, to.z));

        /// <summary>
        /// HOW FAR A ROUND GETS BEFORE IT MEETS A WALL, along a horizontal heading and
        /// out to a limit. The same map the sight lines are drawn against - the city's
        /// own blocks and nothing out of any sidewalk plan - because that is the honest
        /// answer for a bullet: a building face stops it, a bin does not. What a bin is
        /// worth against a round is already paid in the hit chance
        /// (DemoCrews.HitChance's cover multipliers), and counting it twice would put
        /// every impact puff in a fight against a dressed pavement on the near side of
        /// the furniture.
        ///
        /// Returns the limit when the line is clear the whole way, which the caller
        /// reads as "nothing in the way": one segment test to find that out, and a short
        /// bisection only when there is something to place.
        /// </summary>
        public static float ClearOfWalls(Vector3 from, Vector3 dir, float ahead)
        {
            var a = new Vector2(from.x, from.z);
            var h = new Vector2(dir.x, dir.z);
            if (h.sqrMagnitude < 1e-6f) return ahead;
            h.Normalize();
            if (!_solids.Blocks(a, a + h * ahead)) return ahead;
            float free = 0f, hit = ahead;
            for (int i = 0; i < 9; i++)
            {
                float mid = (free + hit) * 0.5f;
                if (_solids.Blocks(a, a + h * mid)) hit = mid;
                else free = mid;
            }
            return free;
        }

        /// <summary>Is there a WALL here - a building face, a lot's edge - as opposed
        /// to a piece of furniture? The blocks laid by the builder, and nothing out of
        /// any sidewalk plan.
        ///
        /// This exists for the one thing that cares about the difference: a man
        /// putting his BACK against something. A lean is authored against a flat
        /// vertical face at a fixed distance, and played with a bin or a car boot
        /// behind him instead the pose has nothing to rest on - he reads as a man
        /// sagging backwards into a squat. Walking round a bin and leaning on a bin
        /// are not the same question, so they do not share an answer.</summary>
        public static bool WallAt(Vector3 p, float radius) =>
            _solids.Occupied(new Vector2(p.x, p.z), radius, 0f);

        /// <summary>Would a man of this radius stood here be inside something -
        /// furniture, a wall, a car?</summary>
        public static bool Occupied(Vector3 p, float radius) => Occupied(p, radius, 0f);

        /// <summary>The same, giving the TALL props the berth their canopies want.
        /// Anything that CHOOSES a spot for a man to stand at - where he is dealt in,
        /// where he is sent to get behind something - asks with
        /// <see cref="CanopyBerth"/>; anything asking whether he can walk THROUGH a
        /// point asks without, because walking under a palm is free and always was.
        /// A trunk's box is the trunk, so without the berth a man can be stood a
        /// hand's width from the bark with two metres of fronds over his head, and
        /// what the player sees is a man who has been posted inside a tree.</summary>
        public static bool Occupied(Vector3 p, float radius, float tallBerth)
        {
            var q = new Vector2(p.x, p.z);
            GatherRoad(q, radius);
            return OnGround(q, radius, tallBerth) || InRoad(q, radius);
        }

        /// <summary>How wide a berth a man being STOOD somewhere gives a tall prop,
        /// over and above his own shoulders. A palm's footprint is its trunk, because
        /// that is all there is of it where a walker's shoulders are - but the fronds
        /// come down to head height a metre and a half out, and a man dealt under them
        /// is a man standing in a tree as far as anybody watching is concerned. Walking
        /// past one is still free: this is asked when a spot is CHOSEN, not stepped
        /// through.</summary>
        public const float CanopyBerth = 1.2f;

        /// <summary>The nearest ground to <paramref name="wanted"/> that a man of this
        /// radius can actually be left standing on: the point itself when it is clear,
        /// else the closest clear one within <paramref name="reach"/>, else the point
        /// given back unchanged because there is nothing better to offer.
        ///
        /// This exists because a spot is not a step. A walker who is dealt inside a
        /// bin walks out of it (Steer lets a man inside something leave it), but a man
        /// dealt inside one and told to STAND there stands there, shoulders in the
        /// tin, for as long as the scene lasts - and the tall props are worse, because
        /// their box is a trunk and their canopy is not, so he is not even blocked, he
        /// merely looks planted. Every crew, mob and squad in every scene is stood up
        /// through DemoCrews, so this is asked once, there.
        ///
        /// The rings are walked nearest first and the headings are staggered from ring
        /// to ring so a man pushed off a kerb by one prop does not land on the next
        /// one out along the same line. NOTHING HERE DRAWS A RANDOM NUMBER: the arena
        /// shares one stream and a spawn-time draw would relay every seed in the lab.
        /// A spot in the carriageway is taken only when the pavement offers none -
        /// men stood in a live lane are their own kind of stupid scene.</summary>
        public static Vector3 FreeSpot(Vector3 wanted, float radius, float reach = 4f)
        {
            int wantedGrade = Grade(wanted, radius);
            if (wantedGrade == 0) return wanted;

            // Nearest first, and of two spots at the same distance the better-graded
            // one - but a grade is only settled for by finishing the search, so each
            // grade keeps its first (nearest) find and grade 0 returns at once. The
            // spot asked for is entered as its own grade's first find, so a man who is
            // merely under a canopy is not walked across the pavement to another spot
            // just as good: he is moved for a better one or not at all.
            var found = new Vector3[3];
            var have = new bool[3];
            if (wantedGrade > 0) { found[wantedGrade] = wanted; have[wantedGrade] = true; }
            const int Headings = 12;
            for (float r = 0.5f; r <= reach + 1e-3f; r += 0.5f)
                for (int i = 0; i < Headings; i++)
                {
                    float a = (i * (360f / Headings) + r * 23f) * Mathf.Deg2Rad;
                    var p = wanted + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    int grade = Grade(p, radius);
                    if (grade == 0) return p;
                    if (grade < 0) continue;
                    if (!have[grade]) { found[grade] = p; have[grade] = true; }
                }
            for (int grade = 1; grade < 3; grade++)
                if (have[grade]) return found[grade];
            return wanted;
        }

        /// <summary>Somewhere near <paramref name="wanted"/> that a man can be left
        /// standing WITHOUT being inside anything - and nothing else asked of it.
        ///
        /// This is <see cref="FreeSpot"/> with its opinions removed, and the two are
        /// wanted for different jobs. FreeSpot chooses ground for a man who is being
        /// put SOMEWHERE - dealt into a scene, set down out of a car - and it prefers a
        /// pavement to a live lane, because nobody means to leave a man in the traffic.
        /// A hood taking his place beside his lieutenant is not being put somewhere: he
        /// is being put BESIDE HIM, and where his boss stands is the player's business.
        /// Asked with FreeSpot, the man whose slot fell within a few metres of a kerb
        /// was quietly pulled up onto the pavement while his boss held the road, and
        /// the crew came apart the moment it was told to stand anywhere but a footway.
        ///
        /// So: solid things only. Furniture, walls, lots and cars are gone round, the
        /// tall props get their canopy berth when there is a spot to spare for it, and
        /// the asphalt is ground like any other. Nearest first, headings staggered ring
        /// to ring, and NO RANDOM NUMBER (the arena shares one stream). Nothing clear
        /// within reach: the point comes back as it went in.</summary>
        public static Vector3 ClearSpot(Vector3 wanted, float radius, float reach = 4f)
        {
            TryClearSpot(wanted, radius, out var spot, reach);
            return spot;
        }

        /// <summary>The fallible form of <see cref="ClearSpot"/>. False means the
        /// returned point is still the original occupied request; recovery code must
        /// not mistake that unchanged value for a valid place.</summary>
        public static bool TryClearSpot(Vector3 wanted, float radius, out Vector3 spot,
            float reach = 4f)
        {
            bool here = InCity(wanted);
            if (here && !Occupied(wanted, radius, CanopyBerth))
            {
                spot = wanted;
                return true;
            }
            // clear of the solids but under a canopy: kept as second best, so a man is
            // never walked half a street for the sake of a palm's fronds
            var loose = wanted;
            bool haveLoose = here && !Occupied(wanted, radius);
            const int Headings = 12;
            for (float r = 0.5f; r <= reach + 1e-3f; r += 0.5f)
                for (int i = 0; i < Headings; i++)
                {
                    float a = (i * (360f / Headings) + r * 23f) * Mathf.Deg2Rad;
                    var p = wanted + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    if (!InCity(p) || Occupied(p, radius)) continue;
                    if (!Occupied(p, radius, CanopyBerth))
                    {
                        spot = p;
                        return true;
                    }
                    if (!haveLoose) { loose = p; haveLoose = true; }
                }
            spot = loose;
            return haveLoose;
        }

        /// <summary>A deterministic command/formation spot against fixed geometry
        /// only. Passing traffic is avoided while walking; it must not permanently
        /// move the destination merely because a car crossed the mouse this frame.</summary>
        public static bool TryClearStandingSpot(Vector3 wanted, float radius,
            out Vector3 spot, float reach = 4f) =>
            TryClearStandingSpot(wanted, radius, default, false,
                default, false, out spot, reach);

        /// <summary>Resolve an occupied command point on the side facing
        /// <paramref name="approachFrom"/>. A diner's short end can be nearer to the
        /// click than its entrance-side wall while still requiring a lap of the whole
        /// building. Search the approach-facing cone across successive rings before
        /// accepting that radially-nearer far face.</summary>
        public static bool TryClearStandingSpot(Vector3 wanted, float radius,
            Vector3 approachFrom, out Vector3 spot, float reach = 4f)
        {
            // A clear requested point is the order; the route planner may legitimately
            // need to go round something to reach it. When the request is occupied,
            // first look for an approach-side replacement joined by a real clear chord.
            // Merely ranking candidates by distance to the crew could still select a
            // near-looking pocket behind another structural mass.
            if (StandingSpot(wanted, radius, default, false))
            {
                spot = wanted;
                return true;
            }
            // A straight connector is useful only for a LOCAL adjustment around the
            // requested mark. Searching the entire recovery radius for one can turn a
            // doorstep order into an order for the near side of the intervening block:
            // the first point visible from a distant crew may be thirty metres from the
            // door. Past the ordinary four-metre standing adjustment, keep the nearest
            // approach-facing replacement and let WalkRoute prove the trip around the
            // buildings instead.
            const float ConnectedAdjustmentReach = 4f;
            if (TryClearStandingSpot(wanted, radius, approachFrom, true,
                    approachFrom, true, out spot,
                    Mathf.Min(reach, ConnectedAdjustmentReach)))
                return true;
            // A distant order may have other buildings between it and the crew. In that
            // case retain the approach-facing choice and let WalkRoute prove the trip.
            return TryClearStandingSpot(wanted, radius, default, false,
                approachFrom, true, out spot, reach);
        }

        /// <summary>A fixed-geometry spot which is also joined to
        /// <paramref name="connectedTo"/> by one clear chord. Formation slots use this
        /// so a blocked offset cannot be silently moved to the far side of a wall.</summary>
        public static bool TryConnectedStandingSpot(Vector3 wanted, Vector3 connectedTo,
            float radius, out Vector3 spot, float reach = 4f) =>
            TryClearStandingSpot(wanted, radius, connectedTo, true,
                connectedTo, true, out spot, reach);

        /// <summary>Whether a failed route start is eligible for a local repair. Besides
        /// the narrow clearance shell, a radius-clear point can sit in a corner pocket
        /// which sees no lattice anchor. A true centre overlap is never teleported.</summary>
        internal static bool RouteStartNeedsRecoveryModel(bool clearanceBlocked,
            bool centreOverlapping, bool hasValidator, bool validatorAccepts) =>
            !centreOverlapping &&
            (clearanceBlocked || (hasValidator && !validatorAccepts));

        /// <summary>Repair a failed route endpoint whose centre is physically free but
        /// whose full footprint or optional route-anchor validator rejects it. This is
        /// deliberately fallible and is intended only after route construction has
        /// failed: ordinary near-wall walking is not relocated. The replacement must be
        /// clear at the route radius and joined to the old centre by a chord clear at
        /// the physical-overlap probe, so the correction cannot jump through a wall.</summary>
        internal static bool TryClearRouteStart(Vector3 wanted, float radius,
            Vector3 toward, out Vector3 spot, float reach = 2.5f,
            System.Predicate<Vector3> accepts = null)
        {
            spot = wanted;
            radius = Mathf.Max(OverlapProbeRadius, radius);
            reach = Mathf.Max(0f, reach);
            if (!InCity(wanted)) return false;
            bool centreOverlapping = Standing(wanted, OverlapProbeRadius);
            bool clearanceBlocked = Standing(wanted, radius);
            bool validatorAccepts = accepts == null || accepts(wanted);
            if (!RouteStartNeedsRecoveryModel(clearanceBlocked, centreOverlapping,
                    accepts != null, validatorAccepts)) return false;

            var forward = toward - wanted;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-5f) forward = Vector3.forward;
            else forward.Normalize();

            const int Headings = 16;
            const float RingStep = 0.125f;
            for (float ring = RingStep; ring <= reach + 1e-4f; ring += RingStep)
            {
                // Search toward the errand first, then alternate left and right. The
                // last spoke is the exact retreat direction, so a prop on the target
                // side cannot prevent a same-side recovery when that is the only one.
                for (int slot = 0; slot < Headings; slot++)
                {
                    int spoke = slot == 0 ? 0 : ((slot + 1) / 2) *
                        ((slot & 1) == 1 ? 1 : -1);
                    var dir = Quaternion.AngleAxis(
                        spoke * (360f / Headings), Vector3.up) * forward;
                    var candidate = wanted + dir * ring;
                    candidate.y = wanted.y;
                    if (!InCity(candidate) || Standing(candidate, radius) ||
                        BlocksStanding(wanted, candidate, OverlapProbeRadius) ||
                        (accepts != null && !accepts(candidate))) continue;

                    var chord = candidate - wanted;
                    chord.y = 0f;
                    int samples = Mathf.CeilToInt(chord.magnitude / 0.25f);
                    bool inCity = true;
                    for (int i = 1; i < samples; i++)
                        if (!InCity(wanted + chord * (i / (float)samples)))
                        { inCity = false; break; }
                    if (!inCity) continue;

                    spot = candidate;
                    return true;
                }
            }
            return false;
        }

        static bool TryClearStandingSpot(Vector3 wanted, float radius,
            Vector3 connectedTo, bool requireConnection,
            Vector3 approachFrom, bool preferApproach, out Vector3 spot, float reach)
        {
            if (StandingSpot(wanted, radius, connectedTo, requireConnection))
            {
                spot = wanted;
                return true;
            }
            const int Headings = 12;
            const float FacingCone = 0.5f; // within 60 degrees of the side we came from
            var approach = approachFrom - wanted;
            approach.y = 0f;
            bool haveApproach = preferApproach && approach.sqrMagnitude > 0.01f;
            if (haveApproach) approach.Normalize();
            bool haveFallback = false;
            float fallbackRing = float.MaxValue;
            float fallbackDistance = float.MaxValue;
            var fallback = wanted;
            for (float r = 0.5f; r <= reach + 1e-3f; r += 0.5f)
            {
                bool foundFacing = false;
                float bestFacing = float.MaxValue;
                var facingSpot = wanted;

                // Do not make the twelve angular samples approximate the one direction
                // which matters most. This ray reaches the face looking at the crew;
                // for a click inside a long cafe it is normally the locally correct
                // exit even when an end wall is radially closer to the click.
                if (haveApproach)
                {
                    var toward = wanted + approach * r;
                    if (StandingSpot(toward, radius, connectedTo, requireConnection))
                    {
                        spot = toward;
                        return true;
                    }
                }

                for (int i = 0; i < Headings; i++)
                {
                    float a = (i * (360f / Headings) + r * 23f) * Mathf.Deg2Rad;
                    var p = wanted + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    if (!StandingSpot(p, radius, connectedTo, requireConnection)) continue;
                    if (!preferApproach)
                    {
                        spot = p;
                        return true;
                    }

                    var fromApproach = p - approachFrom;
                    fromApproach.y = 0f;
                    float square = fromApproach.sqrMagnitude;
                    // Preserve the old nearest-ring answer as a last resort. What we
                    // no longer do is return it before looking for the face toward the
                    // crew on the next few rings.
                    if (!haveFallback || r < fallbackRing - 1e-4f ||
                        (Mathf.Abs(r - fallbackRing) <= 1e-4f &&
                         square < fallbackDistance - 1e-4f))
                    {
                        haveFallback = true;
                        fallbackRing = r;
                        fallbackDistance = square;
                        fallback = p;
                    }

                    if (!haveApproach) continue;
                    var radial = p - wanted;
                    radial.y = 0f;
                    float alignment = radial.sqrMagnitude > 1e-4f
                        ? Vector3.Dot(radial.normalized, approach)
                        : 1f;
                    if (alignment < FacingCone ||
                        (foundFacing && square >= bestFacing - 1e-4f)) continue;
                    foundFacing = true;
                    bestFacing = square;
                    facingSpot = p;
                }
                if (foundFacing)
                {
                    spot = facingSpot;
                    return true;
                }
            }
            if (haveFallback)
            {
                spot = fallback;
                return true;
            }
            spot = wanted;
            return false;
        }

        static bool StandingSpot(Vector3 p, float radius, Vector3 connectedTo,
            bool requireConnection)
        {
            if (!InCity(p) || Standing(p, radius)) return false;
            if (!requireConnection) return true;
            if (!InCity(connectedTo) || Standing(connectedTo, radius) ||
                BlocksStanding(connectedTo, p, radius)) return false;
            var d = p - connectedTo;
            d.y = 0f;
            int samples = Mathf.CeilToInt(d.magnitude / 0.5f);
            for (int i = 1; i < samples; i++)
                if (!InCity(connectedTo + d * (i / (float)samples))) return false;
            return true;
        }

        // How good a spot is to be left standing on, best first:
        //   0  clear pavement, and clear of the canopies
        //   1  clear pavement, under a canopy - he is not blocked, he only looks it,
        //      and looking planted beats standing in a live lane
        //   2  clear ground, but in the carriageway
        //  -1  no good at all: inside furniture, a wall, or a car
        // The order matters and is not obvious. Pushing a man off a kerb because a
        // palm's fronds reach over it would swap a cosmetic fault for the one the
        // player already complained about - enemies out in the road, jamming the
        // traffic. The berth is a preference; the pavement is not.
        static int Grade(Vector3 p, float radius)
        {
            // off the floor altogether is no spot at all - nobody is ever CHOSEN a
            // place to stand out in the wilderness, whatever asked
            if (!InCity(p)) return -1;
            var q = new Vector2(p.x, p.z);
            GatherRoad(q, radius);
            if (OnGround(q, radius, 0f) || InRoad(q, radius)) return -1;
            if (InCarriageway(p)) return 2;
            return OnGround(q, radius, CanopyBerth) ? 1 : 0;
        }

        static bool InCarriageway(Vector3 p)
        {
            var net = LaneNet.Active;
            if (net == null) return false;
            var road = net.Locate(p, out _, out float d, 6f);
            return road != null && Mathf.Abs(d) < road.HalfRoad;
        }

        /// <summary>The solid furniture within reach of a point - the PROPS only. The
        /// walls are left out on purpose (a building face is not something a man gets
        /// behind and shoots over), and so is the traffic, which whoever wants a car's
        /// flank asks StreetTraffic for itself. What DemoCrews.CoverNear offers a
        /// pressed man beyond the parked cars.</summary>
        public static void PropsNear(Vector3 p, float reach, List<SidewalkPlan.Box> into)
        {
            into.Clear();
            var q = new Vector2(p.x, p.z);
            _composedProps.SolidNear(q, reach, into);
            for (int i = 0; i < _props.Count; i++) _props[i].SolidNear(q, reach, into);
        }

        /// <summary>The same run as <see cref="Clear"/>, but past the FIXED things only -
        /// no traffic. What a way across the city is drawn against (WalkRoute): a line
        /// that dodged wherever the cars happened to be standing would be a line round
        /// nothing a moment later.</summary>
        public static float ClearStanding(Vector3 from, Vector3 dir, float radius, float ahead)
        {
            var p = new Vector2(from.x, from.z);
            var h = new Vector2(dir.x, dir.z);
            if (h.sqrMagnitude < 1e-6f) return 0f;
            h.Normalize();
            Near.Clear();          // the road is nobody's business here
            return Run(p, h, radius, radius, ahead);
        }

        /// <summary>Is a complete straight walk blocked by anything fixed? Uses each
        /// plan's spatial segment query rather than stepping along the line. Traffic is
        /// deliberately excluded for the same reason as <see cref="ClearStanding"/>.</summary>
        public static bool BlocksStanding(Vector3 from, Vector3 to, float radius)
        {
            var a = new Vector2(from.x, from.z);
            var b = new Vector2(to.x, to.z);
            if (_solids.Obstructs(a, b, radius) || _composedProps.Obstructs(a, b, radius))
                return true;
            for (int i = 0; i < _props.Count; i++)
                if (_props[i].Obstructs(a, b, radius)) return true;
            return false;
        }

        /// <summary>How far a man of this radius can walk from <paramref name="from"/>
        /// along <paramref name="dir"/> before he is into something, up to
        /// <paramref name="ahead"/> metres.</summary>
        public static float Clear(Vector3 from, Vector3 dir, float radius, float ahead)
            => Clear(from, dir, radius, radius, ahead);

        /// <summary>The same live step with separate berths for fixed furniture and
        /// traffic. Routed crews use a narrow prop footprint without putting their
        /// shoulders through the flank of a car.</summary>
        public static float Clear(Vector3 from, Vector3 dir, float fixedRadius,
            float trafficRadius, float ahead)
        {
            var p = new Vector2(from.x, from.z);
            var h = new Vector2(dir.x, dir.z);
            if (h.sqrMagnitude < 1e-6f) return 0f;
            h.Normalize();
            fixedRadius = Mathf.Max(0f, fixedRadius);
            trafficRadius = Mathf.Max(0f, trafficRadius);
            GatherRoad(p, ahead + trafficRadius + 0.5f);
            float sampled = Run(p, h, fixedRadius, trafficRadius, ahead);
            if (sampled <= 0f) return 0f;

            // Point samples overlap, but their swept lateral coverage still leaves a
            // few centimetres at the midpoint. Prove the actual fixed-geometry capsule
            // before a transform write, then bisect to the longest exact prefix.
            var heading = new Vector3(h.x, 0f, h.y);
            if (!BlocksStanding(from, from + heading * sampled, fixedRadius)) return sampled;
            float lo = 0f, hi = sampled;
            for (int i = 0; i < 7; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (BlocksStanding(from, from + heading * mid, fixedRadius)) hi = mid;
                else lo = mid;
            }
            return lo;
        }

        // the probe's pitch: circles of the walker's radius a third of a metre
        // apart overlap, so a lamp post between two of them is still seen
        const float Step = 0.35f;

        // Metres along h from p that are free, to ahead. Refines the last pitch
        // by halves so a man stopped short of a car stops AT it, not a stride off.
        static float Run(Vector2 p, Vector2 h, float fixedRadius,
            float trafficRadius, float ahead)
        {
            float u = Step;
            for (; u < ahead; u += Step)
                if (OnGround(p + h * u, fixedRadius) ||
                    InRoad(p + h * u, trafficRadius))
                    return Refine(p, h, fixedRadius, trafficRadius, u - Step, u);
            if (OnGround(p + h * ahead, fixedRadius) ||
                InRoad(p + h * ahead, trafficRadius))
                return Refine(p, h, fixedRadius, trafficRadius,
                    Mathf.Max(0f, ahead - Step), ahead);
            return ahead;
        }

        static float Refine(Vector2 p, Vector2 h, float fixedRadius,
            float trafficRadius, float free, float hit)
        {
            for (int i = 0; i < 3; i++)
            {
                float mid = (free + hit) * 0.5f;
                if (OnGround(p + h * mid, fixedRadius) ||
                    InRoad(p + h * mid, trafficRadius)) hit = mid;
                else free = mid;
            }
            return free;
        }

        // The lines tried off the wanted one, nearest first: out to the beam, and
        // the two behind it for a man boxed in at the front.
        static readonly float[] Angles = { 11f, 22f, 34f, 46f, 60f, 75f, 90f, 110f, 135f, 160f };

        /// <summary>The heading a man at <paramref name="from"/> who wants to go
        /// <paramref name="want"/> (flat) can actually take, looked
        /// <paramref name="ahead"/> metres down: the wanted line if it runs clear
        /// that far; else the nearest line off it that does, with two rules about
        /// which. He keeps his side: <paramref name="side"/> is his to hold between
        /// frames, and a man already going round something to his right tries every
        /// line to his right before the first one to his left, so he does not change
        /// his mind at every bin and dither in front of a car, and follows a long
        /// wall to its end instead of turning back half way. And he does not turn
        /// round if he can help it: a line that runs back against the way he is
        /// going (<paramref name="going"/>, zero when he is not yet) is taken only
        /// when nothing else is clear - so a man passing one car who finds another
        /// ahead goes round it the open way, not back the way he came. Boxed in,
        /// whichever line runs furthest. <paramref name="clear"/> is how far the
        /// line given runs before it hits something: the most he may step this
        /// frame. A man stood inside something already (dealt there, shoved there)
        /// is let walk straight out of it.</summary>
        public static Vector3 Steer(Vector3 from, Vector3 want, Vector3 going, float radius, float ahead,
            ref int side, out float clear, int preferredSide = 0,
            float trafficRadius = -1f, float minForwardDot = -1f)
        {
            var p = new Vector2(from.x, from.z);
            var w = new Vector2(want.x, want.z);
            if (w.sqrMagnitude < 1e-6f) { clear = 0f; return want; }
            w.Normalize();
            radius = Mathf.Max(0f, radius);
            if (trafficRadius < 0f) trafficRadius = radius;
            trafficRadius = Mathf.Max(0f, trafficRadius);
            minForwardDot = Mathf.Clamp(minForwardDot, -1f, 1f);
            GatherRoad(p, ahead + trafficRadius + 0.5f);

            // A moving car can overlap a walker between frames. Push him toward the
            // nearest flank/end until his shoulders are outside it; continuing along
            // the wanted line here used to let him traverse the whole car.
            if (RoadEscape(p, w, trafficRadius, ahead, out var escape, out clear))
            {
                side = 0;
                return new Vector3(escape.x, 0f, escape.y);
            }

            // Already inside fixed furniture means bad placement or newly registered
            // streamed geometry. Never solve it by walking through the object.
            if (OnGround(p, 0.1f))
            {
                clear = 0f;
                return new Vector3(w.x, 0f, w.y);
            }

            // the line he wants
            float straight = Run(p, w, radius, trafficRadius, ahead);
            if (straight >= ahead - 1e-3f)
            {
                side = 0;
                clear = straight;
                return new Vector3(w.x, 0f, w.y);
            }

            var g = new Vector2(going.x, going.z);
            bool hasWay = g.sqrMagnitude > 1e-4f;
            if (hasWay) g.Normalize();

            Vector2 best = w;
            float bestClear = straight;
            int bestSide = 0;

            // two passes: lines that carry on roughly the way he is going, then any
            for (int pass = 0; pass < 2; pass++)
            {
                bool carryOn = pass == 0 && hasWay;
                if (side != 0)
                {
                    // committed: the whole of his side, nearest first, then the other
                    for (int s = 0; s < 2; s++)
                    {
                        int sign = s == 0 ? side : -side;
                        for (int i = 0; i < Angles.Length; i++)
                            if (Try(Angles[i], sign, carryOn)) goto Found;
                    }
                }
                else
                {
                    // no committed side yet: a crew may share a TIE-BREAK, but a
                    // preference is not permission to try a ninety-degree detour before
                    // an eleven-degree opening on the other side.
                    int first = preferredSide != 0 ? (preferredSide > 0 ? 1 : -1) : 1;
                    for (int i = 0; i < Angles.Length; i++)
                        if (Try(Angles[i], first, carryOn) ||
                            Try(Angles[i], -first, carryOn)) goto Found;
                }
                // nothing ahead runs clear to the horizon, but a good step of it does:
                // he takes that before he turns round - through the gap between two
                // cars, which no line at these angles threads end to end, he goes a
                // car's width at a time
                if (carryOn && bestClear >= Mathf.Min(1f, ahead * 0.5f)) break;
            }

            // nothing runs clear: the line that runs furthest; the side he was on
            // stays his unless a side did better
            if (bestSide != 0) side = bestSide;
            clear = Mathf.Max(0f, bestClear);
            return new Vector3(best.x, 0f, best.y);

            Found:
            side = bestSide;
            clear = bestClear;
            return new Vector3(best.x, 0f, best.y);

            // one line: true when it runs clear to the horizon (taken at once);
            // otherwise remembered if it runs further than anything yet. With
            // carryOn, a line turning back against the way he is going is skipped.
            bool Try(float a, int sign, bool carryOn)
            {
                var line = Turn(w, a * sign);
                if (Vector2.Dot(line, w) < minForwardDot) return false;
                if (carryOn && Vector2.Dot(line, g) < -0.25f) return false;
                float c = Run(p, line, radius, trafficRadius, ahead);
                if (c >= ahead - 1e-3f) { best = line; bestClear = c; bestSide = sign; return true; }
                if (c > bestClear) { bestClear = c; best = line; bestSide = sign; }
                return false;
            }
        }

        static bool RoadEscape(Vector2 p, Vector2 wanted, float radius, float ahead,
            out Vector2 escape, out float clear)
        {
            escape = wanted;
            clear = 0f;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < Near.Count; i++)
            {
                var b = Near[i];
                var d = p - b.C;
                float along = Vector2.Dot(d, b.F);
                float across = Vector2.Dot(d, b.R);
                float longHalf = b.HL + radius;
                float wideHalf = b.HW + radius;
                if (Mathf.Abs(along) >= longHalf || Mathf.Abs(across) >= wideHalf) continue;

                float outLong = longHalf - Mathf.Abs(along);
                float outWide = wideHalf - Mathf.Abs(across);
                Vector2 dir;
                float distance;
                if (outWide <= outLong)
                {
                    float sign = Mathf.Abs(across) > 1e-4f
                        ? Mathf.Sign(across)
                        : (Vector2.Dot(wanted, b.R) >= 0f ? 1f : -1f);
                    dir = b.R * sign;
                    distance = outWide;
                }
                else
                {
                    float sign = Mathf.Abs(along) > 1e-4f
                        ? Mathf.Sign(along)
                        : (Vector2.Dot(wanted, b.F) >= 0f ? 1f : -1f);
                    dir = b.F * sign;
                    distance = outLong;
                }
                if (distance >= best) continue;
                best = distance;
                escape = dir;
                found = true;
            }
            if (!found) return false;
            clear = Mathf.Min(ahead, Mathf.Max(0.1f, best + 0.05f));
            return true;
        }

        // a flat heading turned by deg (positive = to its right, as a man turns)
        static Vector2 Turn(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c + v.y * s, -v.x * s + v.y * c);
        }
    }
}
