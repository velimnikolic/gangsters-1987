using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>What kind of quarter a district slot holds.</summary>
    public enum DistrictKind { Pad, Suburb, Harbor, Airport }

    /// <summary>Which side of the city grid a district hangs off.</summary>
    // None is LAST on purpose: South must stay 0, so a CityEdge that was never written
    // - a scene serialised before this member existed, a default(CityEdge) in a struct -
    // still reads as the south shore and not as "no shore".
    public enum CityEdge { South, West, North, East, None }

    // ------------------------------------------------------------------ frame

    /// <summary>
    /// Where a district stands in the world. A district lays itself out in its OWN
    /// coordinates - the harbour with its quay along local X and the sea toward
    /// local -Z, the suburb with its grid from its own origin - and every point that
    /// touches the world passes through the frame: geometry, the lane graph, the
    /// pedestrian graph, routes, the things that move.
    ///
    /// The yaw is a quarter turn (0/90/180/270), so a district's rectangle stays
    /// axis-aligned in the world and the island can reserve plain Rects for it. A
    /// harbour is authored facing the sea at local -Z, which puts it on the city's
    /// south shore at yaw 0, west at 90, north at 180, east at 270.
    /// </summary>
    [System.Serializable]
    public struct DistrictFrame
    {
        [Tooltip("World point the district's local origin sits at.")]
        public Vector3 origin;
        [Tooltip("Quarter turn of the district about Y: 0, 90, 180 or 270 degrees.")]
        public int yaw;

        public static DistrictFrame Identity => new DistrictFrame { origin = Vector3.zero, yaw = 0 };

        public static DistrictFrame At(float x, float z, int yaw)
            => new DistrictFrame { origin = new Vector3(x, 0f, z), yaw = ((yaw % 360) + 360) % 360 };

        /// <summary>The turn alone, for rotations of prefabs and of local directions.</summary>
        public Quaternion Rotation => Quaternion.Euler(0f, yaw, 0f);

        /// <summary>A local point in the world.</summary>
        public Vector3 ToWorld(Vector3 local) => origin + Turn(local);

        /// <summary>A local direction in the world (no translation).</summary>
        public Vector3 ToWorldDir(Vector3 local) => Turn(local);

        /// <summary>A local heading (degrees about Y) in the world.</summary>
        public float ToWorldYaw(float localYaw) => localYaw + yaw;

        /// <summary>A world point back in the district's own coordinates.</summary>
        public Vector3 ToLocal(Vector3 world)
        {
            var d = world - origin;
            switch (((yaw % 360) + 360) % 360)
            {
                case 90: return new Vector3(-d.z, d.y, d.x);
                case 180: return new Vector3(-d.x, d.y, -d.z);
                case 270: return new Vector3(d.z, d.y, -d.x);
                default: return d;
            }
        }

        /// <summary>A local axis-aligned rectangle in the world - still axis-aligned,
        /// because the turn is a quarter of a circle.</summary>
        public Rect ToWorldRect(Rect local)
        {
            var a = ToWorld(new Vector3(local.xMin, 0f, local.yMin));
            var b = ToWorld(new Vector3(local.xMax, 0f, local.yMax));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z),
                                   Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
        }

        Vector3 Turn(Vector3 v)
        {
            switch (((yaw % 360) + 360) % 360)
            {
                case 90: return new Vector3(v.z, v.y, -v.x);
                case 180: return new Vector3(-v.x, v.y, -v.z);
                case 270: return new Vector3(-v.z, v.y, v.x);
                default: return v;
            }
        }

        public override string ToString() => $"({origin.x:F0}, {origin.z:F0}) yaw {yaw}";
    }

    // ----------------------------------------------------------- reservations

    /// <summary>
    /// What a district asks of the ground it stands on. It never builds terrain
    /// itself - the host's island does, once, for the whole world - so it says
    /// instead: this rectangle I pave myself (leave it out of the heightfield),
    /// this one must be flat at that level (my approach and my yard), this one must
    /// be open water (the basin a ship sails into), nothing grows in this one.
    /// All rectangles are WORLD space; the district passes them through its frame.
    /// </summary>
    public sealed class DistrictReservations
    {
        /// <summary>Ground the district tiles itself: the island skips these cells.</summary>
        public readonly List<Rect> Paved = new List<Rect>();
        /// <summary>Ground the island must hold flat, and at what height.</summary>
        public readonly List<(Rect area, float level)> Flat = new List<(Rect, float)>();
        /// <summary>Water: the coast may not cross into these - a harbour basin.</summary>
        public readonly List<Rect> Water = new List<Rect>();
        /// <summary>Whether the island may push that water's seaward end further out to
        /// reach the open sea. True of a BASIN, which has to be sailed into and cannot
        /// know where the coast runs; false of a long lane along the shore - pushing one
        /// of those out would take the whole coast off the island rather than opening a
        /// way to it (RoadDemoBuilder.OpenBasinsToSea).</summary>
        public readonly List<bool> WaterOpens = new List<bool>();
        /// <summary>Nothing wild grows here.</summary>
        public readonly List<Rect> Bare = new List<Rect>();

        public void Pave(Rect world) => Paved.Add(world);
        public void Level(Rect world, float y) => Flat.Add((world, y));
        public void Sea(Rect world) => Sea(world, true);

        public void Sea(Rect world, bool mayOpen)
        {
            Water.Add(world);
            WaterOpens.Add(mayOpen);
        }
        public void NoFlora(Rect world) => Bare.Add(world);

        public bool InPaved(float x, float z) => InPaved(x, z, 0f);

        /// <summary>Whether a point counts as paved once the rectangle is pulled in by
        /// <paramref name="inset"/> metres. The island tests the CENTRE of a heightfield
        /// cell and drops the cell whole, so a rectangle taken at face value drops cells
        /// that stick out of it by up to half a cell - a ragged hole of open air round
        /// every quarter, which from the water reads as the ground having been cut away.
        /// Pulling the rectangle in by half a cell turns the error the safe way about:
        /// the ground runs a little way UNDER the district's own tiles instead.</summary>
        public bool InPaved(float x, float z, float inset)
        {
            for (int i = 0; i < Paved.Count; i++)
            {
                var r = Paved[i];
                if (x > r.xMin + inset && x < r.xMax - inset &&
                    z > r.yMin + inset && z < r.yMax - inset) return true;
            }
            return false;
        }

        public bool InWater(float x, float z)
        {
            for (int i = 0; i < Water.Count; i++) if (Water[i].Contains(new Vector2(x, z))) return true;
            return false;
        }

        /// <summary>How much of a basin's water there is at (x, z): 1 inside the
        /// rectangle, easing to 0 over <paramref name="blend"/> metres outside it.
        /// A basin used to be a plain in-or-out test, which cut the sea out of the
        /// island as a rectangular pit - land at head height on one side of the line
        /// and seabed on the other, a wall going down into the water all round the
        /// port. With a blend the same rectangle reads as a bay: the ground slides
        /// down a beach into it.</summary>
        public bool WaterAt(float x, float z, float blend, out float weight)
        {
            weight = 0f;
            for (int i = 0; i < Water.Count; i++)
            {
                var r = Water[i];
                float dx = Mathf.Max(r.xMin - x, x - r.xMax, 0f);
                float dz = Mathf.Max(r.yMin - z, z - r.yMax, 0f);
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > blend) continue;
                float w = 1f - Mathf.SmoothStep(0f, 1f, d / Mathf.Max(0.001f, blend));
                if (w > weight) weight = w;
            }
            return weight > 0f;
        }

        public bool InBare(float x, float z)
        {
            for (int i = 0; i < Bare.Count; i++) if (Bare[i].Contains(new Vector2(x, z))) return true;
            return false;
        }

        /// <summary>The flat level at (x, z) and how firmly it is held: 1 inside the
        /// rectangle, tapering to 0 over <paramref name="blend"/> metres outside it,
        /// so the wild ground meets a district's apron without a step.</summary>
        public bool FlatAt(float x, float z, float blend, out float level, out float weight)
        {
            level = 0f; weight = 0f;
            for (int i = 0; i < Flat.Count; i++)
            {
                var (r, y) = Flat[i];
                float dx = Mathf.Max(r.xMin - x, x - r.xMax, 0f);
                float dz = Mathf.Max(r.yMin - z, z - r.yMax, 0f);
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > blend) continue;
                float w = 1f - Mathf.SmoothStep(0f, 1f, d / Mathf.Max(0.001f, blend));
                if (w > weight) { weight = w; level = y; }
            }
            return weight > 0f;
        }
    }

    // ---------------------------------------------------------------- portals

    /// <summary>
    /// Where a city street meets the district: the centre of the carriageway on the
    /// district's boundary, in LOCAL coordinates, and the outward direction (away
    /// from the district, toward the city). The host lays the connecting street from
    /// its own junction to this point and welds the two lane graphs there.
    /// </summary>
    public struct DistrictPortal
    {
        public Vector3 Local;      // on the boundary, centre line of the road
        public Vector3 LocalDir;   // outward, unit, axis-aligned
        public RoadNode Node;      // the district's own junction the connector welds onto
        public PedNode WalkLeft;   // pavement corners either side of that junction, or null
        public PedNode WalkRight;
        public string Tag;
    }

    // ------------------------------------------------------------------- host

    /// <summary>
    /// What a district may ask of whoever is hosting it - the city in RoadDemo, or
    /// the small standalone host in the district's own demo scene. Everything that
    /// belongs to the SCENE rather than to the quarter (the sun, the camera, the sky,
    /// the clock, the terrain, the perf pass, the crowd's tick) is the host's, so
    /// two districts in one scene cannot fight over it.
    /// </summary>
    public interface IDistrictHost
    {
        /// <summary>A root for still geometry: the host merges and distance-culls it.</summary>
        Transform StaticRoot(string name);

        /// <summary>A root for what moves: never merged.</summary>
        Transform LiveRoot(string name);

        /// <summary>The crowd's clips, loaded once for the scene.</summary>
        PedClips Clips { get; }

        /// <summary>Whether the host lays the ground under and around the district - the
        /// city's island does, a district's own demo scene does not, and there the
        /// district falls back on the ground it always built for itself.</summary>
        bool ProvidesGround { get; }

        /// <summary>The green the host's own ground is painted with, or null where the
        /// host has no ground of its own. A district tiles its lawns out of its own
        /// pack, and a pack's green is never the island's green: a quarter laid in the
        /// city with the pack's material reads as a rectangle of a different lawn
        /// dropped in the wild. So the plain grass a district lays takes THIS material
        /// where there is one - the island's triplanar ground, which needs no UVs and
        /// so paints a 5 m tile as seamlessly as it paints the heightfield.</summary>
        Material GroundMaterial { get; }

        /// <summary>The one city life: doors, benches, what a walker does at a stop.</summary>
        CityLife Life { get; }

        /// <summary>Hand over something that moves; the host ticks it from now on.</summary>
        void RegisterVehicle(DemoVehicle vehicle);
        void RegisterCivilian(CivilianAgent civilian);
        void RegisterWalker(PedestrianAgent walker);
        void RegisterSignal(TrafficSignal signal);

        /// <summary>Fold the district's road and pavement graphs into the city's, so a
        /// patrol car can route into the quarter and a walker can walk out of it.</summary>
        void RegisterRoads(IReadOnlyList<RoadEdge> edges);
        void RegisterPavement(IReadOnlyList<PedLink> links);

        /// <summary>Ground a walker off the graph may not cross (a house, a shed).</summary>
        void Blocked(Bounds box);

        /// <summary>The same, for a thing that has a NAME: a shed, a house, a hangar.
        /// The host keeps the name so the map can put a card on it - a port whose sheds
        /// cannot be clicked is scenery, and the city's own buildings all answer.</summary>
        void Blocked(Bounds box, string what);

        /// <summary>A prefab the district wanted and did not get.</summary>
        void ReportMissing(string what);
    }

    // --------------------------------------------------------------- district

    /// <summary>
    /// A quarter of the city that is not the grid: the port, a suburb. The same
    /// object builds it in its own demo scene and in the city - only the host and
    /// the frame differ - so what is changed in the demo IS what the city gets.
    ///
    /// The order is always: Plan (decide the layout and the bounds, touch nothing in
    /// the scene) → Reserve (tell the host what ground is wanted) → Build (make it) →
    /// Portals (weld the roads) → Tick.
    /// </summary>
    public interface IDistrict
    {
        string Name { get; }

        /// <summary>Where the district stands. Set before Plan.</summary>
        DistrictFrame Frame { get; set; }

        /// <summary>Work out the layout from the settings and the links asked for.
        /// No scene objects yet: the host needs the bounds to lay out the island.</summary>
        void Plan(float[] links, int seed);

        /// <summary>The district's own rectangle, in local coordinates, after Plan.</summary>
        Rect LocalBounds { get; }

        /// <summary>What the ground under and around it must be.</summary>
        void Reserve(DistrictReservations into);

        /// <summary>Make it.</summary>
        void Build(IDistrictHost host);

        /// <summary>Where the city's streets meet it, after Build.</summary>
        IReadOnlyList<DistrictPortal> Portals { get; }

        /// <summary>The district's own moving parts (ships, cranes, forklifts): what
        /// the host does not already tick through the registries.</summary>
        void Tick(float dt);

        void Dispose();
    }
}
