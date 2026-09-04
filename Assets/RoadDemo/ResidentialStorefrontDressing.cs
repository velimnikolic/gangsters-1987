using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static RoadDemo.Composer;

namespace RoadDemo
{
    public static partial class ResidentialBlocks
    {
        const string StorefrontShellName = "storefront shallow interiors";
        const string StorefrontShellMaterial =
            "Assets/Synty/PolygonCoffeeShop/Materials/Background.mat";
        const string StorefrontGlassMaterial =
            "Assets/Synty/PolygonCity/Materials/Misc/Glass_01.mat";
        const string StorefrontWallMaterial =
            "Assets/Synty/PolygonCity/Materials/Alts/PolygonCity_01_A.mat";
        const string StorefrontProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string StorefrontPropName = "storefront SM_Prop_ShopInterior_";

        /// <summary>
        /// Compact, upright display cabinets. Desks and the long cafe/shelf modules looked
        /// like loose boards from the game's high camera and could bridge a cut corner, so
        /// they are deliberately excluded.
        /// </summary>
        static readonly string[] StorefrontInteriorProps =
        {
            StorefrontProps + "SM_Prop_ShopInterior_Display_01.prefab",
            StorefrontProps + "SM_Prop_ShopInterior_Display_02.prefab",
        };

        sealed class StorefrontLayout
        {
            public ResidentialStorefrontOpening[] Openings =
                Array.Empty<ResidentialStorefrontOpening>();
        }

        internal readonly struct StorefrontDecorationPlan
        {
            public readonly int[] Styles;

            public StorefrontDecorationPlan(int[] styles)
            {
                Styles = styles;
            }
        }

        struct StorefrontDice
        {
            uint state;

            public StorefrontDice(int seed)
            {
                state = unchecked((uint)seed) ^ 0xA341316Cu;
                if (state == 0) state = 0x9E3779B9u;
            }

            uint Next()
            {
                // A fixed xorshift sequence, so storefront identity does not change when
                // Unity moves between Mono/.NET random implementations.
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return state;
            }

            public int Next(int max) => max <= 1 ? 0 : (int)(Next() % (uint)max);
        }

        static readonly Dictionary<string, StorefrontLayout> StorefrontLayouts =
            new Dictionary<string, StorefrontLayout>(StringComparer.Ordinal);
        static Material storefrontShellMaterial;

        /// <summary>The shallow rooms and display silhouettes behind the glass. The cutaway
        /// treats them as it treats the live facade in front of them (which marks itself
        /// with <see cref="StorefrontLive"/>): shown whole, in their own materials, while
        /// their facade is the camera-facing ground floor, hidden whole otherwise - never
        /// faded, so nothing stands about as free-standing see-through furniture.</summary>
        internal static bool IsGeneratedStorefrontVisual(Transform candidate,
                                                           Transform buildingRoot)
        {
            for (Transform at = candidate; at != null && at != buildingRoot; at = at.parent)
                if (at.name == StorefrontShellName ||
                    at.name.StartsWith(StorefrontPropName, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>
        /// Add one combined shallow-room renderer to a shop-bearing building and one pooled,
        /// single-renderer silhouette to every open facade. This is intentionally one cheap
        /// object per visible shop face rather than a furnished room, but no transparent bay
        /// is left looking vacant. A corner business receives one on both cardinal faces.
        /// Each yielded value still represents at most one renderer/prefab attachment.
        /// </summary>
        static IEnumerable<int> StorefrontDressingSteps(
            GameObject building, ResidentialUnit unit, string layoutKey,
            int seed, Vector3? fallbackOutward, Stood stood)
        {
            if (building == null || stood == null) yield break;

            string key = StorefrontLayoutKey(layoutKey, fallbackOutward);
            if (!StorefrontLayouts.TryGetValue(key, out var layout))
            {
                layout = new StorefrontLayout
                {
                    Openings = DiscoverStorefronts(building, unit, fallbackOutward),
                };
                StorefrontLayouts[key] = layout;
            }
            if (layout.Openings.Length == 0)
            {
                // A harvested unit can be labelled as a shop yet expose no usable panes.
                // Its caller deliberately deferred the ordinary prepare, so finish that
                // one required registration here without doing a second scan.
                if (building.GetComponent<BuildingCutaway>() == null)
                {
                    BuildingCutaway.Prepare(building, unit);
                    yield return 0;
                }
                yield break;
            }

            var plan = PlanStorefronts(layout.Openings, seed);
            ClearGeneratedStorefrontProps(building, out _);
            var shell = building.transform.Find(StorefrontShellName);
            if (shell == null)
            {
                shell = new GameObject(StorefrontShellName).transform;
                shell.SetParent(building.transform, false);
            }
            shell.gameObject.layer = building.layer;
            var rooms = shell.GetComponent<ResidentialStorefrontShell>();
            if (rooms == null) rooms = shell.gameObject.AddComponent<ResidentialStorefrontShell>();
            storefrontShellMaterial ??= DemoAssetLoad.Load<Material>(StorefrontShellMaterial);
            rooms.Configure(layout.Openings, storefrontShellMaterial);
            BuildLiveStorefronts(building, unit, layout.Openings, stood);

            stood.Storefronts += layout.Openings.Length;
            yield return 0;

            for (int i = 0; i < plan.Styles.Length; i++)
            {
                int style = plan.Styles[i];
                if (style < 0 || style >= StorefrontInteriorProps.Length) continue;
                if (PlaceStorefrontProp(building, layout.Openings[i],
                                        StorefrontInteriorProps[style]))
                {
                    stood.StorefrontProps++;
                    stood.Props++;
                }
                yield return i + 1;
            }

            // Cutaway preparation is deliberately deferred until the fake room and display
            // props exist, so a recycled building scans its final topology exactly once on
            // both eager and incremental composition paths.
            BuildingCutaway.Prepare(building, unit);
            yield return plan.Styles.Length + 1;
        }

        sealed class LiveBay
        {
            public int Primary;
            public Rect Footprint;
            public readonly List<int> Members = new List<int>(2);
            public readonly List<ResidentialStorefrontOpening> Openings =
                new List<ResidentialStorefrontOpening>(3);
        }

        /// <summary>Create/rebind one independently live facade per doored logical bay.</summary>
        static void BuildLiveStorefronts(
            GameObject building, ResidentialUnit unit,
            ResidentialStorefrontOpening[] measured, Stood stood = null)
        {
            // Named kit venues (pizzapub/radnja) keep the legacy damage and doorway path;
            // EPIC 32 addresses shop bays embedded in residential buildings only.
            if (building == null || unit == null || unit.Kind == ResidentialKind.Storefront ||
                unit.ShopBays == null || unit.ShopBays.Length == 0)
                return;

            var bays = unit.ShopBays;
            var logical = new List<LiveBay>(bays.Length);
            for (int i = 0; i < bays.Length; i++)
            {
                if (bays[i].Door.Leaves == 0) continue;
                var live = new LiveBay { Primary = i, Footprint = LocalBayRect(unit, bays[i]) };
                live.Members.Add(i);
                logical.Add(live);
            }

            // Display-only Shop_05 and the second half of wide Shop_03 are part of the
            // nearest doored premise on the same face.
            for (int i = 0; i < bays.Length; i++)
            {
                if (bays[i].Door.Leaves != 0) continue;
                if (!StorefrontDoorCatalog.TryGet(bays[i].Module, out _)) continue;
                int nearest = -1;
                float best = float.MaxValue;
                var footprint = LocalBayRect(unit, bays[i]);
                for (int n = 0; n < logical.Count; n++)
                {
                    var owner = bays[logical[n].Primary];
                    float distance = (logical[n].Footprint.center -
                                      footprint.center).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    nearest = n;
                }
                if (nearest < 0) continue;
                logical[nearest].Members.Add(i);
                var merged = Rect.MinMaxRect(
                    Mathf.Min(logical[nearest].Footprint.xMin, footprint.xMin),
                    Mathf.Min(logical[nearest].Footprint.yMin, footprint.yMin),
                    Mathf.Max(logical[nearest].Footprint.xMax, footprint.xMax),
                    Mathf.Max(logical[nearest].Footprint.yMax, footprint.yMax));
                float cell = ResidentialLot.Cell;
                if (bays[i].Side == 0 || bays[i].Side == 2)
                    logical[nearest].Footprint = new Rect(
                        Mathf.Clamp(merged.center.x - cell, 0f,
                            unit.CW * cell - cell * 2f),
                        logical[nearest].Footprint.y, cell * 2f, cell);
                else
                    logical[nearest].Footprint = new Rect(
                        logical[nearest].Footprint.x,
                        Mathf.Clamp(merged.center.y - cell, 0f,
                            unit.CD * cell - cell * 2f),
                        cell, cell * 2f);
            }

            // The harvest and DiscoverStorefronts both emit one entry per source module.
            // Match those entries to the source bays first, then follow the logical owner
            // (which may be the neighbour of Shop_05 / the display half of Shop_03).
            // Matching directly to doored footprints made chamfer normals steal a nearby
            // corner's pane and left another bay with a synthetic replacement.
            var usedOpenings = new bool[measured?.Length ?? 0];
            for (int bayIndex = 0; bayIndex < bays.Length; bayIndex++)
            {
                if (!StorefrontDoorCatalog.TryGet(bays[bayIndex].Module, out _)) continue;
                // Shop_03 is a single 10 m source module represented by two planner
                // cells. Its doorless second half shares the first half's glass child;
                // it must not consume another module's measured opening.
                if (bays[bayIndex].Door.Leaves == 0 &&
                    bays[bayIndex].Module == "SM_Bld_Shop_03") continue;
                int owner = -1;
                for (int n = 0; n < logical.Count; n++)
                    if (logical[n].Members.Contains(bayIndex)) { owner = n; break; }
                if (owner < 0) continue;

                int opening = NearestOpening(bayIndex, requireFacing: true);
                if (opening < 0) opening = NearestOpening(bayIndex, requireFacing: false);
                if (opening < 0) continue;
                usedOpenings[opening] = true;
                logical[owner].Openings.Add(measured[opening]);
            }

            // A future source mesh may expose an extra pane group. It still belongs to the
            // closest logical premise and must not be silently discarded.
            for (int i = 0; measured != null && i < measured.Length; i++)
            {
                if (usedOpenings[i]) continue;
                int nearest = -1;
                float best = float.MaxValue;
                for (int n = 0; n < logical.Count; n++)
                {
                    Vector3 target = new Vector3(logical[n].Footprint.center.x,
                        measured[i].Front.y, logical[n].Footprint.center.y);
                    float distance = (measured[i].Front - target).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    nearest = n;
                }
                if (nearest >= 0) logical[nearest].Openings.Add(measured[i]);
            }

            int NearestOpening(int bayIndex, bool requireFacing)
            {
                int nearest = -1;
                float best = float.MaxValue;
                Vector3 centre = new Vector3(bays[bayIndex].X, 0f, bays[bayIndex].Z);
                Vector3 outward = SideOutward(bays[bayIndex].Side);
                for (int i = 0; measured != null && i < measured.Length; i++)
                {
                    if (usedOpenings[i] || requireFacing &&
                        Vector3.Dot(outward, measured[i].Outward) < 0.35f)
                        continue;
                    Vector3 delta = measured[i].Front - centre;
                    delta.y = 0f;
                    float distance = delta.sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    nearest = i;
                }
                return nearest;
            }

            var filters = new List<MeshFilter>(64);
            building.GetComponentsInChildren(true, filters);
            Material fallbackGlass = DemoAssetLoad.Load<Material>(StorefrontGlassMaterial);
            Material fallbackWall = DemoAssetLoad.Load<Material>(StorefrontWallMaterial);

            var existing = new List<Storefront>();
            building.GetComponentsInChildren(true, existing);
            for (int n = 0; n < logical.Count; n++)
            {
                var live = logical[n];
                var bay = bays[live.Primary];
                Storefront storefront = n < existing.Count ? existing[n] : null;
                if (storefront == null || storefront.transform.parent != building.transform)
                {
                    var root = new GameObject("Storefront bay " + n);
                    root.transform.SetParent(building.transform, false);
                    root.layer = building.layer;
                    storefront = root.AddComponent<Storefront>();
                }
                storefront.name = "Storefront bay " + n;

                var walls = new List<MeshFilter>(live.Members.Count);
                var glasses = new List<MeshRenderer>(live.Members.Count);
                Material wallMaterial = fallbackWall;
                Material glassMaterial = fallbackGlass;
                for (int member = 0; member < live.Members.Count; member++)
                {
                    var sourceBay = bays[live.Members[member]];
                    var wall = FindModuleFilter(building.transform, filters, sourceBay);
                    if (wall == null || walls.Contains(wall)) continue;
                    walls.Add(wall);
                    var wallRenderer = wall.GetComponent<MeshRenderer>();
                    if (wallRenderer != null && wallRenderer.sharedMaterial != null &&
                        member == 0) wallMaterial = wallRenderer.sharedMaterial;
                    foreach (var renderer in wall.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (renderer == null || renderer == wallRenderer ||
                            !renderer.name.EndsWith("_Glass", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!glasses.Contains(renderer)) glasses.Add(renderer);
                        if (renderer.sharedMaterial != null) glassMaterial = renderer.sharedMaterial;
                    }
                }

                if (live.Openings.Count == 0)
                    live.Openings.Add(SyntheticOpening(bay, live.Footprint));
                storefront.Configure(
                    bay.Module,
                    new Vector3(bay.Door.X, 0f, bay.Door.Z),
                    bay.Door.Yaw,
                    live.Footprint,
                    live.Openings.ToArray(), walls.ToArray(), glasses.ToArray(),
                    glassMaterial, wallMaterial);
                // Refresh and authoring passes reuse inactive surplus children. Configure
                // them before enabling so OnEnable sees the new bay, not the old lease.
                if (!storefront.gameObject.activeSelf)
                    storefront.gameObject.SetActive(true);
            }

            // A refresh can reduce the count; old pooled children must not remain bindable.
            for (int i = logical.Count; i < existing.Count; i++)
                if (existing[i] != null && existing[i].transform.parent == building.transform)
                    existing[i].gameObject.SetActive(false);
            if (stood != null)
                for (int i = 0; i < logical.Count; i++)
                    stood.StorefrontBays += logical[i].Members.Count;
        }

        static MeshFilter FindModuleFilter(
            Transform building, List<MeshFilter> filters, ResidentialShopBay bay)
        {
            if (string.IsNullOrEmpty(bay.Module)) return null;
            Vector3 target = new Vector3(
                bay.Door.Leaves > 0 ? bay.Door.X : bay.X, 0f,
                bay.Door.Leaves > 0 ? bay.Door.Z : bay.Z);
            MeshFilter best = null;
            float bestDistance = float.MaxValue;
            StorefrontDoorCatalog.TryGet(bay.Module, out var profile);
            for (int i = 0; i < filters.Count; i++)
            {
                var filter = filters[i];
                if (filter == null || filter.sharedMesh == null) continue;
                string meshName = filter.sharedMesh.name;
                if (!meshName.StartsWith(bay.Module, StringComparison.OrdinalIgnoreCase) ||
                    meshName.EndsWith("_Glass", StringComparison.OrdinalIgnoreCase))
                    continue;
                Vector3 sourcePoint = building.InverseTransformPoint(
                    filter.transform.TransformPoint(profile.Centre));
                sourcePoint.y = target.y = 0f;
                float distance = (sourcePoint - target).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = filter;
            }
            return best;
        }

        static Rect LocalBayRect(ResidentialUnit unit, ResidentialShopBay bay)
        {
            float cell = ResidentialLot.Cell;
            float width = unit.CW * cell;
            float depth = unit.CD * cell;
            int side = bay.Door.Leaves > 0
                ? SideOfVector(Quaternion.Euler(0f, bay.Door.Yaw, 0f) * Vector3.forward)
                : bay.Side;
            float alongX = bay.Door.Leaves > 0 ? bay.Door.X : bay.X;
            float alongZ = bay.Door.Leaves > 0 ? bay.Door.Z : bay.Z;
            float x = side switch
            {
                1 => alongX - cell,
                3 => alongX,
                _ => alongX - cell * 0.5f,
            };
            float z = side switch
            {
                0 => alongZ,
                2 => alongZ - cell,
                _ => alongZ - cell * 0.5f,
            };
            if (bay.Door.Leaves == 0)
            {
                x = Mathf.Clamp(x, 0f, Mathf.Max(0f, width - cell));
                z = Mathf.Clamp(z, 0f, Mathf.Max(0f, depth - cell));
            }
            return new Rect(x, z, cell, cell);
        }

        static int SideOfVector(Vector3 outward)
        {
            if (Mathf.Abs(outward.x) > Mathf.Abs(outward.z))
                return outward.x >= 0f ? 1 : 3;
            return outward.z >= 0f ? 2 : 0;
        }

        static ResidentialStorefrontOpening SyntheticOpening(
            ResidentialShopBay bay, Rect footprint)
        {
            Vector3 outward = Quaternion.Euler(0f, bay.Door.Yaw, 0f) * Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, outward).normalized;
            float width = bay.Side == 0 || bay.Side == 2
                ? footprint.width : footprint.height;
            return new ResidentialStorefrontOpening(
                new Vector3(bay.X, 0f, bay.Z), outward, right,
                Mathf.Max(1.2f, width - 0.2f), 3f);
        }

        internal static StorefrontDecorationPlan PlanStorefronts(int count, int seed)
        {
            count = Mathf.Clamp(count, 0, 30);
            var openings = new ResidentialStorefrontOpening[count];
            for (int i = 0; i < count; i++)
                openings[i] = new ResidentialStorefrontOpening(
                    Vector3.zero, SideOutward(i & 3), Vector3.right, 2f, 2.5f, i);
            return PlanStorefronts(openings, seed);
        }

        internal static StorefrontDecorationPlan PlanStorefronts(
            ResidentialStorefrontOpening[] openings, int seed)
        {
            openings ??= Array.Empty<ResidentialStorefrontOpening>();
            int count = Mathf.Clamp(openings.Length, 0, 30);
            var styles = new int[count];
            for (int i = 0; i < count; i++) styles[i] = -1;
            if (count == 0) return new StorefrontDecorationPlan(styles);

            var dice = new StorefrontDice(seed);
            var groups = new List<List<int>>(count);
            var groupByKey = new Dictionary<int, int>();
            for (int i = 0; i < count; i++)
            {
                int key = openings[i].Group >= 0 ? openings[i].Group : int.MinValue + i;
                if (!groupByKey.TryGetValue(key, out int at))
                {
                    at = groups.Count;
                    groupByKey.Add(key, at);
                    groups.Add(new List<int>(3));
                }
                groups[at].Add(i);
            }

            var openGroups = new List<int>(groups.Count);
            for (int group = 0; group < groups.Count; group++)
                openGroups.Add(group);

            var candidates = new List<int>(count);
            // A two-sided corner shop gets one compact display on each main facade before
            // ordinary single-sided bays are shuffled. Diagonal glass is the cut entrance
            // and deliberately receives no object.
            foreach (int group in openGroups)
            {
                bool authoredCorner = false;
                foreach (int opening in groups[group])
                    if (openings[opening].Corner) { authoredCorner = true; break; }
                if (!authoredCorner) continue;
                var perSide = new Dictionary<int, int>();
                foreach (int opening in groups[group])
                {
                    if (openings[opening].Entrance) continue;
                    int side = Direction(openings[opening].Outward);
                    if (!perSide.TryGetValue(side, out int prior) ||
                        openings[opening].Width > openings[prior].Width)
                        perSide[side] = opening;
                }
                if (perSide.Count < 2) continue;
                for (int side = 0; side < 4; side++)
                    if (perSide.TryGetValue(side, out int opening))
                        candidates.Add(opening);
            }

            var remainder = new List<int>(count);
            foreach (int group in openGroups)
                foreach (int opening in groups[group])
                    if (!openings[opening].Entrance && !candidates.Contains(opening))
                        remainder.Add(opening);
            for (int i = remainder.Count - 1; i > 0; i--)
            {
                int other = dice.Next(i + 1);
                (remainder[i], remainder[other]) = (remainder[other], remainder[i]);
            }
            candidates.AddRange(remainder);

            // A compact display is the cheap visual promise that this facade is a real
            // shop. Capping these at four was inexpensive but visibly left most of a large
            // residential unit empty, so every open, non-entrance facade receives one.
            for (int n = 0; n < candidates.Count; n++)
            {
                int at = candidates[n];
                int style = dice.Next(StorefrontInteriorProps.Length);
                if (at > 0 && styles[at - 1] == style)
                    style = (style + 1 + dice.Next(StorefrontInteriorProps.Length - 1)) %
                            StorefrontInteriorProps.Length;
                styles[at] = style;
            }
            return new StorefrontDecorationPlan(styles);
        }

        static int StorefrontSeed(int planSeed, string key, int i, int j, int turn)
        {
            uint hash = 2166136261u;
            key ??= string.Empty;
            for (int n = 0; n < key.Length; n++) hash = (hash ^ key[n]) * 16777619u;
            hash = (hash ^ unchecked((uint)planSeed)) * 16777619u;
            hash = (hash ^ unchecked((uint)i)) * 16777619u;
            hash = (hash ^ unchecked((uint)j)) * 16777619u;
            hash = (hash ^ unchecked((uint)turn)) * 16777619u;
            return unchecked((int)hash);
        }

        static string StorefrontLayoutKey(string key, Vector3? hint)
        {
            if (!hint.HasValue || hint.Value.sqrMagnitude < 0.25f) return key ?? string.Empty;
            return (key ?? string.Empty) + "#" + Direction(hint.Value);
        }

        static bool NeedsStorefrontDressing(ResidentialUnit unit)
        {
            if (unit == null || ResidentialUnits.IsLot(unit)) return false;
            if (unit.Kind == ResidentialKind.Storefront) return true;
            if (unit.Shops == null) return false;
            for (int i = 0; i < unit.Shops.Length; i++)
                if (unit.Shops[i] > 0) return true;
            return false;
        }

        static Vector3 CafeLocalOutward(ResidentialLot.Gap gap, Transform block,
                                        Transform building)
        {
            if (gap == null || block == null || building == null) return Vector3.forward;
            Vector3 world = block.TransformDirection(SideOutward(gap.Side));
            Vector3 local = building.InverseTransformDirection(world);
            local.y = 0f;
            return local.sqrMagnitude >= 0.25f ? local.normalized : Vector3.forward;
        }

        static ResidentialStorefrontOpening[] DiscoverStorefronts(
            GameObject building, ResidentialUnit unit, Vector3? fallbackOutward)
        {
            var found = new List<ResidentialStorefrontOpening>(8);
            var filters = new List<MeshFilter>(64);
            int nextGroup = 0;
            building.GetComponentsInChildren(true, filters);
            for (int i = 0; i < filters.Count; i++)
            {
                var filter = filters[i];
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || !StorefrontMesh(mesh.name)) continue;
                MeasureStorefrontOpenings(building.transform, filter.transform, mesh,
                                          nextGroup++, found);
            }

            if (found.Count == 0)
            {
                Vector3? hint = fallbackOutward ?? StorefrontFallback(unit);
                if (hint.HasValue && hint.Value.sqrMagnitude >= 0.25f &&
                    MeasureFallback(building.transform, filters, hint.Value,
                                    nextGroup, out var fallback))
                    found.Add(fallback);
            }
            // Metadata is only a last-resort safety net. Synthesizing it before the real
            // fallback made cafe shells span a whole 25-40 m unit and read as loose boards.
            if (found.Count == 0) AddMissingUnitFaces(found, unit, ref nextGroup);

            found.Sort(CompareOpenings);
            return found.ToArray();
        }

        static bool StorefrontMesh(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            // Shop prefabs carry a full wall mesh and a separate glass mesh. Measuring
            // both doubles the bay and the generic Wall_Window modules are ordinary
            // apartment windows, so the transparent shop pane is the only authoritative
            // opening. Harvest metadata fills a facade only when its pane is absent.
            return name.StartsWith("SM_Bld_Shop", StringComparison.OrdinalIgnoreCase) &&
                   name.EndsWith("_Glass", StringComparison.OrdinalIgnoreCase);
        }

        static Vector3? StorefrontFallback(ResidentialUnit unit)
        {
            if (unit == null || unit.Kind != ResidentialKind.Storefront) return null;
            int bestSide = -1, bestScore = int.MinValue;
            for (int side = 0; side < 4; side++)
            {
                int shops = unit.Shops != null && side < unit.Shops.Length
                    ? unit.Shops[side]
                    : 0;
                int doors = unit.Doors != null && side < unit.Doors.Length
                    ? unit.Doors[side]
                    : 0;
                bool face = unit.Face != null && side < unit.Face.Length && unit.Face[side];
                if (!face && shops == 0 && doors == 0) continue;
                int score = shops * 100 + doors * 10 + (face ? 1 : 0);
                if (score <= bestScore) continue;
                bestScore = score;
                bestSide = side;
            }
            return bestSide >= 0 ? SideOutward(bestSide) : Vector3.forward;
        }

        sealed class StorefrontPlane
        {
            public Vector3 Outward;
            public float Distance;
            public readonly List<Vector3> Points = new List<Vector3>(24);
        }

        /// <summary>
        /// Split glass by its authored face normals. A normal module produces one plane;
        /// a corner module produces two shop faces and its diagonal entrance. Treating the
        /// old combined bounds as one rectangle is what sent props through the cut corner.
        /// </summary>
        internal static void MeasureStorefrontOpenings(
            Transform root, Transform piece, Mesh mesh, int group,
            List<ResidentialStorefrontOpening> found)
        {
            bool corner = mesh.name.IndexOf("_Corner_", StringComparison.OrdinalIgnoreCase) >= 0;
            string sourceModule = mesh.name.EndsWith("_Glass", StringComparison.OrdinalIgnoreCase)
                ? mesh.name.Substring(0, mesh.name.Length - "_Glass".Length)
                : mesh.name;
            bool hasDoor = StorefrontDoorCatalog.TryGet(
                sourceModule, out var doorProfile) && doorProfile.Leaves > 0;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            if (vertices == null || normals == null || vertices.Length == 0 ||
                normals.Length != vertices.Length)
            {
                if (MeasureOpeningBounds(root, piece, mesh.bounds, group, out var fallback))
                    AddUnique(found, fallback);
                return;
            }

            Matrix4x4 intoRoot = root.worldToLocalMatrix * piece.localToWorldMatrix;
            var planes = new List<StorefrontPlane>(3);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 point = intoRoot.MultiplyPoint3x4(vertices[i]);
                Vector3 outward = intoRoot.MultiplyVector(normals[i]);
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.25f) continue;
                outward.Normalize();

                StorefrontPlane plane = null;
                for (int n = 0; n < planes.Count; n++)
                    if (Vector3.Dot(planes[n].Outward, outward) >= 0.97f &&
                        Mathf.Abs(Vector3.Dot(planes[n].Outward, point) -
                                  planes[n].Distance) <= 0.08f)
                    {
                        plane = planes[n];
                        break;
                    }
                if (plane == null)
                {
                    plane = new StorefrontPlane
                    {
                        Outward = outward,
                        Distance = Vector3.Dot(outward, point),
                    };
                    planes.Add(plane);
                }
                plane.Points.Add(point);
            }

            int before = found.Count;
            for (int i = 0; i < planes.Count; i++)
            {
                var plane = planes[i];
                if (plane.Points.Count < 4) continue;
                Vector3 outward = plane.Outward.normalized;
                Vector3 right = Vector3.Cross(Vector3.up, outward).normalized;
                float along0 = float.MaxValue, along1 = float.MinValue;
                float front = float.MinValue, low = float.MaxValue, high = float.MinValue;
                for (int n = 0; n < plane.Points.Count; n++)
                {
                    Vector3 point = plane.Points[n];
                    float along = Vector3.Dot(point, right);
                    along0 = Mathf.Min(along0, along);
                    along1 = Mathf.Max(along1, along);
                    front = Mathf.Max(front, Vector3.Dot(point, outward));
                    low = Mathf.Min(low, point.y);
                    high = Mathf.Max(high, point.y);
                }

                float width = along1 - along0;
                if (width < 0.55f || high - low < 0.45f || high < -0.1f || low > 1.35f)
                    continue;
                float floor = Mathf.Max(-1.55f, Mathf.Min(0.05f, low));
                float height = Mathf.Clamp(high - floor, 2.15f, 3.15f);
                Vector3 at = right * ((along0 + along1) * 0.5f) + outward * front +
                             Vector3.up * floor;
                bool entrance = Mathf.Abs(outward.x) > 0.5f &&
                                Mathf.Abs(outward.z) > 0.5f;
                if (entrance || hasDoor && IsDoorGlassPlane(
                        intoRoot, doorProfile, outward, right, along0, along1))
                    continue;
                AddUnique(found, new ResidentialStorefrontOpening(
                    at, outward, right, width, height, group, entrance, corner));
            }

            if (found.Count == before &&
                MeasureOpeningBounds(root, piece, mesh.bounds, group, out var boundsOpening))
                AddUnique(found, boundsOpening);
        }

        static bool IsDoorGlassPlane(Matrix4x4 intoRoot,
                                     StorefrontDoorProfile profile,
                                     Vector3 outward, Vector3 right,
                                     float along0, float along1)
        {
            Vector3 doorOutward = intoRoot.MultiplyVector(profile.Outward);
            doorOutward.y = 0f;
            if (doorOutward.sqrMagnitude < 0.25f ||
                Vector3.Dot(outward, doorOutward.normalized) < 0.96f)
                return false;

            Vector3 doorPoint = intoRoot.MultiplyPoint3x4(profile.Centre);
            float centre = Vector3.Dot(doorPoint, right);
            float scale = intoRoot.MultiplyVector(profile.Right).magnitude;
            float half = profile.Width * Mathf.Max(0.01f, scale) * 0.5f + 0.10f;
            // The separate door-glass plane rides on the baked leaf. Only the wider
            // display-window run is rebuilt as static storefront glass.
            return along0 >= centre - half && along1 <= centre + half;
        }

        static bool MeasureOpeningBounds(Transform root, Transform piece, Bounds bounds,
                                         int group,
                                         out ResidentialStorefrontOpening opening)
        {
            opening = default;
            Matrix4x4 intoRoot = root.worldToLocalMatrix * piece.localToWorldMatrix;
            Vector3 outward = intoRoot.MultiplyVector(Vector3.forward);
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.25f) return false;
            outward.Normalize();

            Vector3 rawRight = intoRoot.MultiplyVector(Vector3.right);
            rawRight.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, outward).normalized;
            if (rawRight.sqrMagnitude > 0.25f && Vector3.Dot(right, rawRight) < 0f) right = -right;

            ProjectBounds(intoRoot, bounds, right, outward,
                out float along0, out float along1, out _, out float front,
                out float low, out float high);
            float width = along1 - along0;
            if (width < 0.8f || high < -0.1f || low > 1.35f) return false;

            float floor = Mathf.Max(-1.55f, low);
            float height = Mathf.Clamp(high - floor, 2.15f, 3.15f);
            Vector3 at = right * ((along0 + along1) * 0.5f) + outward * front +
                         Vector3.up * floor;
            opening = new ResidentialStorefrontOpening(
                at, outward, right, width, height, group);
            return true;
        }

        static bool MeasureFallback(Transform root, List<MeshFilter> filters, Vector3 outwardHint,
                                    int group,
                                    out ResidentialStorefrontOpening opening)
        {
            opening = default;
            outwardHint.y = 0f;
            if (outwardHint.sqrMagnitude < 0.25f) return false;
            Vector3 outward = outwardHint.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, outward).normalized;
            bool any = false;
            float along0 = float.MaxValue, along1 = float.MinValue;
            float front = float.MinValue, low = float.MaxValue, high = float.MinValue;

            for (int i = 0; i < filters.Count; i++)
            {
                var filter = filters[i];
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || StorefrontShellName == filter.transform.name ||
                    filter.transform.name.StartsWith(StorefrontPropName,
                        StringComparison.Ordinal)) continue;
                Matrix4x4 intoRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                ProjectBounds(intoRoot, mesh.bounds, right, outward,
                    out float a0, out float a1, out _, out float f, out float y0, out float y1);
                along0 = Mathf.Min(along0, a0);
                along1 = Mathf.Max(along1, a1);
                front = Mathf.Max(front, f);
                low = Mathf.Min(low, y0);
                high = Mathf.Max(high, y1);
                any = true;
            }
            if (!any || along1 - along0 < 1f) return false;

            float full = along1 - along0;
            float width = Mathf.Clamp(full * 0.82f, 2.2f, 12f);
            float floor = Mathf.Max(0f, low);
            float height = Mathf.Clamp(high - floor, 2.2f, 3.1f);
            Vector3 at = right * ((along0 + along1) * 0.5f) + outward * front +
                         Vector3.up * floor;
            opening = new ResidentialStorefrontOpening(
                at, outward, right, width, height, group);
            return true;
        }

        static void ProjectBounds(Matrix4x4 matrix, Bounds bounds, Vector3 right, Vector3 outward,
                                  out float along0, out float along1,
                                  out float depth0, out float depth1,
                                  out float low, out float high)
        {
            along0 = depth0 = low = float.MaxValue;
            along1 = depth1 = high = float.MinValue;
            Vector3 min = bounds.min, max = bounds.max;
            for (int mask = 0; mask < 8; mask++)
            {
                var local = new Vector3(
                    (mask & 1) == 0 ? min.x : max.x,
                    (mask & 2) == 0 ? min.y : max.y,
                    (mask & 4) == 0 ? min.z : max.z);
                Vector3 point = matrix.MultiplyPoint3x4(local);
                float along = Vector3.Dot(point, right);
                float depth = Vector3.Dot(point, outward);
                along0 = Mathf.Min(along0, along);
                along1 = Mathf.Max(along1, along);
                depth0 = Mathf.Min(depth0, depth);
                depth1 = Mathf.Max(depth1, depth);
                low = Mathf.Min(low, point.y);
                high = Mathf.Max(high, point.y);
            }
        }

        /// <summary>Editor/audit entry point which exercises the same dressing pass as a
        /// streamed residential unit. The caller owns the temporary or drawn instance.</summary>
        public static void BuildStorefrontPreview(
            GameObject building, ResidentialUnit unit, int seed)
        {
            if (building == null || unit == null)
                return;
            var openings = DiscoverStorefronts(building, unit, null);
            if (openings.Length == 0)
                return;
            var plan = PlanStorefronts(openings, seed);
            var shell = building.transform.Find(StorefrontShellName);
            if (shell == null)
            {
                shell = new GameObject(StorefrontShellName).transform;
                shell.SetParent(building.transform, false);
            }
            shell.gameObject.layer = building.layer;
            var rooms = shell.GetComponent<ResidentialStorefrontShell>();
            if (rooms == null)
                rooms = shell.gameObject.AddComponent<ResidentialStorefrontShell>();
            storefrontShellMaterial ??= DemoAssetLoad.Load<Material>(StorefrontShellMaterial);
            rooms.Configure(openings, storefrontShellMaterial);
            BuildLiveStorefronts(building, unit, openings);
            BuildingCutaway.Prepare(building, unit);
        }

        static void AddMissingUnitFaces(List<ResidentialStorefrontOpening> found,
                                        ResidentialUnit unit, ref int nextGroup)
        {
            if (unit == null || unit.Shops == null || unit.Shops.Length < 4) return;
            float width = unit.CW * ResidentialLot.Cell;
            float depth = unit.CD * ResidentialLot.Cell;
            for (int side = 0; side < 4; side++)
            {
                int wanted = Mathf.Clamp(unit.Shops[side], 0, 12);
                if (wanted == 0) continue;
                bool present = false;
                for (int i = 0; i < found.Count; i++)
                    if (Direction(found[i].Outward) == side) { present = true; break; }
                if (present) continue;

                Vector3 outward = SideOutward(side);
                Vector3 right = Vector3.Cross(Vector3.up, outward);
                float run = side == 0 || side == 2 ? width : depth;
                float bay = run / wanted;
                float openingWidth = Mathf.Clamp(bay - 0.16f, 1.2f, 5.2f);
                for (int n = 0; n < wanted; n++)
                {
                    float along = -run * 0.5f + (n + 0.5f) * bay;
                    Vector3 edge = side switch
                    {
                        0 => new Vector3(width * 0.5f, 0f, 0f),
                        1 => new Vector3(width, 0f, depth * 0.5f),
                        2 => new Vector3(width * 0.5f, 0f, depth),
                        _ => new Vector3(0f, 0f, depth * 0.5f),
                    };
                    found.Add(new ResidentialStorefrontOpening(
                        edge + right * along, outward, right, openingWidth, 3f,
                        nextGroup++));
                }
            }
        }

        static void AddUnique(List<ResidentialStorefrontOpening> found,
                              ResidentialStorefrontOpening opening)
        {
            for (int i = 0; i < found.Count; i++)
            {
                var other = found[i];
                if (Vector3.Dot(other.Outward, opening.Outward) < 0.98f ||
                    Mathf.Abs(other.Front.y - opening.Front.y) > 0.2f ||
                    Vector3.Distance(other.Front, opening.Front) > 0.22f) continue;
                if (opening.Width > other.Width) found[i] = opening;
                return;
            }
            found.Add(opening);
        }

        static int CompareOpenings(ResidentialStorefrontOpening a,
                                   ResidentialStorefrontOpening b)
        {
            int side = Direction(a.Outward).CompareTo(Direction(b.Outward));
            if (side != 0) return side;
            float aa = Vector3.Dot(a.Front, a.Right);
            float bb = Vector3.Dot(b.Front, b.Right);
            return aa.CompareTo(bb);
        }

        static int Direction(Vector3 outward)
        {
            if (Mathf.Abs(outward.x) > Mathf.Abs(outward.z)) return outward.x >= 0f ? 1 : 3;
            return outward.z >= 0f ? 2 : 0;
        }

        static Vector3 SideOutward(int side) => side switch
        {
            0 => Vector3.back,
            1 => Vector3.right,
            2 => Vector3.forward,
            _ => Vector3.left,
        };

        static bool PlaceStorefrontProp(GameObject building,
                                        ResidentialStorefrontOpening opening,
                                        string path,
                                        Func<string, Transform, GameObject> instantiate = null)
        {
            if (opening.Entrance) return false;
            var piece = instantiate != null
                ? instantiate(path, building.transform)
                : Raise(path, building.transform);
            if (piece == null) return false;

            Vector3 outward = opening.Outward.normalized;
            Vector3 right = opening.Right.normalized;
            Vector3 worldOut = building.transform.TransformDirection(outward).normalized;
            Vector3 worldRight = building.transform.TransformDirection(right).normalized;

            piece.transform.SetPositionAndRotation(Vector3.zero,
                Quaternion.LookRotation(worldOut, Vector3.up));
            float deep = 0.5f;
            if (WorldBox(piece, out var box))
            {
                float along = Mathf.Abs(worldRight.x) * box.size.x +
                              Mathf.Abs(worldRight.z) * box.size.z;
                deep = Mathf.Abs(worldOut.x) * box.size.x +
                       Mathf.Abs(worldOut.z) * box.size.z;
                float widthLimit = Mathf.Max(0.35f,
                    Mathf.Min(1.75f, opening.Width - 0.7f));
                float fit = Mathf.Min(1f,
                    widthLimit / Mathf.Max(0.1f, along),
                    0.65f / Mathf.Max(0.1f, deep),
                    (opening.Height - 0.4f) / Mathf.Max(0.1f, box.size.y));
                if (fit < 0.995f)
                {
                    piece.transform.localScale *= Mathf.Max(0.35f, fit);
                    WorldBox(piece, out box);
                    deep = Mathf.Abs(worldOut.x) * box.size.x +
                           Mathf.Abs(worldOut.z) * box.size.z;
                }
                float inside = 0.3f + deep * 0.5f;
                Vector3 local = opening.Front - outward * inside;
                Vector3 target = building.transform.TransformPoint(local + Vector3.up * 0.025f);
                piece.transform.position += target -
                    new Vector3(box.center.x, box.min.y, box.center.z);
            }
            else
            {
                Vector3 local = opening.Front - outward * 0.65f;
                piece.transform.position = building.transform.TransformPoint(
                    local + Vector3.up * 0.025f);
            }

            piece.name = $"storefront {System.IO.Path.GetFileNameWithoutExtension(path)}";
            foreach (var collider in piece.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (var renderer in piece.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
            return true;
        }

        static int ClearGeneratedStorefrontProps(GameObject building, out int longPieces)
        {
            longPieces = 0;
            if (building == null) return 0;
            int removed = 0;
            Transform root = building.transform;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (!child.name.StartsWith(StorefrontPropName, StringComparison.Ordinal))
                    continue;
                if (child.name.IndexOf("Cafe_01", StringComparison.Ordinal) >= 0 ||
                    child.name.IndexOf("Shelf_", StringComparison.Ordinal) >= 0)
                    longPieces++;
                removed++;
                if (Application.isPlaying) UnityEngine.Object.Destroy(child.gameObject);
                else UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            return removed;
        }

        static GameObject InstantiateStorefrontProp(string path, Transform parent)
        {
            var prefab = DemoAssetLoad.Load<GameObject>(path);
            return prefab != null ? UnityEngine.Object.Instantiate(prefab, parent) : null;
        }

        static ResidentialUnit ExistingStorefrontUnit(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return null;
            foreach (var unit in ResidentialUnits.Known)
            {
                if (objectName == unit.Name ||
                    objectName.StartsWith(unit.Name + " (", StringComparison.Ordinal))
                    return unit;
            }
            return null;
        }

        static string StorefrontHierarchyKey(Transform at)
        {
            string key = at != null ? at.name : string.Empty;
            while (at != null && at.parent != null)
            {
                at = at.parent;
                key = at.name + "/" + key;
            }
            return key;
        }

        public sealed class StorefrontRefreshReport
        {
            public int Buildings;
            public int Openings;
            public int Displays;
            public int RemovedGeneratedProps;
            public int RemovedLongPieces;
            public string[] Failures = Array.Empty<string>();
        }

        /// <summary>
        /// Upgrade only the generated storefront children already present in a review
        /// scene. The block recipes, roots, transforms and all manually placed content are
        /// untouched; the caller decides whether the scene is ever saved.
        /// </summary>
        public static StorefrontRefreshReport RefreshExistingStorefronts(Scene scene)
        {
            var report = new StorefrontRefreshReport();
            var failures = new List<string>();
            var shells = new List<ResidentialStorefrontShell>();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                report.Failures = new[] { "No loaded scene was supplied." };
                return report;
            }

            foreach (var root in scene.GetRootGameObjects())
                shells.AddRange(root.GetComponentsInChildren<ResidentialStorefrontShell>(true));

            var seen = new HashSet<GameObject>();
            for (int i = 0; i < shells.Count; i++)
            {
                var oldShell = shells[i];
                if (oldShell == null || oldShell.transform.parent == null) continue;
                var building = oldShell.transform.parent.gameObject;
                if (!seen.Add(building)) continue;

                var oldOpenings = oldShell.CopyOpenings();
                Vector3? fallback = oldOpenings.Length > 0
                    ? oldOpenings[0].Outward
                    : (Vector3?)null;
                var unit = ExistingStorefrontUnit(building.name);

                var openings = DiscoverStorefronts(building, unit, fallback);
                if (openings.Length == 0)
                {
                    failures.Add(StorefrontHierarchyKey(building.transform) +
                                 ": no storefront opening found");
                    continue;
                }

                // Remove only children created by this system. In particular, this clears
                // stale Cafe/Shelf instances left in a scene composed by an older script.
                int removed = ClearGeneratedStorefrontProps(building, out int longPieces);

                int seed = StorefrontSeed(1987,
                    StorefrontHierarchyKey(building.transform), 0, 0, 0);
                var plan = PlanStorefronts(openings, seed);
                storefrontShellMaterial ??= DemoAssetLoad.Load<Material>(StorefrontShellMaterial);
                oldShell.Configure(openings, storefrontShellMaterial);
                BuildLiveStorefronts(building, unit, openings);

                int displays = 0;
                for (int opening = 0; opening < plan.Styles.Length; opening++)
                {
                    int style = plan.Styles[opening];
                    if (style < 0 || style >= StorefrontInteriorProps.Length) continue;
                    if (PlaceStorefrontProp(building, openings[opening],
                            StorefrontInteriorProps[style], InstantiateStorefrontProp))
                        displays++;
                }

                report.Buildings++;
                report.Openings += openings.Length;
                report.Displays += displays;
                report.RemovedGeneratedProps += removed;
                report.RemovedLongPieces += longPieces;
            }

            report.Failures = failures.ToArray();
            return report;
        }

        /// <summary>Read-only editor/audit hook; it creates no dressing and changes no prefab.</summary>
        public static int AuditStorefrontOpeningCount(GameObject building, ResidentialUnit unit,
                                                      Vector3 fallbackOutward)
        {
            if (building == null) return 0;
            Vector3? hint = fallbackOutward.sqrMagnitude >= 0.25f
                ? fallbackOutward.normalized
                : (Vector3?)null;
            return DiscoverStorefronts(building, unit, hint).Length;
        }
    }
}
