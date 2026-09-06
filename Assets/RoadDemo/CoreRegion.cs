using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Composition of the seeded region; the shared districts and belt own their behaviour.</summary>
    public sealed class CoreRegion
    {
        public sealed class Quarter
        {
            public IDistrict District;
            public DistrictSlot Slot;
            /// <summary>The landward end of the works' artery: where the city's road arrives.</summary>
            public RasterGateways.Gateway IndustryGateway;
            public Vector3 IndustryOrigin;
            public Rect World => District.Frame.ToWorldRect(District.LocalBounds);
        }
        public sealed class Connection
        {
            public CityEdge Edge;
            public float Across;
            public RoadNode CityNode;
            public Vector3 CityFace;
            public Quarter Quarter;
            public int Portal;
            /// <summary>The quarter whose road carries this portal's traffic: the port's
            /// main gate is reached along the works' artery, so no approach of its own is
            /// laid from the expressway.</summary>
            public Quarter Via;
            public bool Through => CityNode != null && Quarter != null;
        }

        public readonly List<Quarter> Quarters = new List<Quarter>();
        public readonly List<Connection> Connections = new List<Connection>();
        public Rect BeltBounds { get; private set; }
        public Rect IndustryGroundBounds => PortIndustryLayout.GroundBounds(Quarters);
        public Rect WorldBounds
        {
            get
            {
                var box = BeltBounds;
                const float margin = RoadDemoBuilder.BeltOut + 160f;
                box.xMin -= margin; box.xMax += margin; box.yMin -= margin; box.yMax += margin;
                foreach (var q in Quarters)
                {
                    box.xMin = Mathf.Min(box.xMin, q.World.xMin); box.xMax = Mathf.Max(box.xMax, q.World.xMax);
                    box.yMin = Mathf.Min(box.yMin, q.World.yMin); box.yMax = Mathf.Max(box.yMax, q.World.yMax);
                }
                return box;
            }
        }
        readonly CoreDistrict _core;
        readonly List<Rect> _fuel = new List<Rect>();

        public bool ReserveFuel(Rect ground)
        {
            if (_core.Frame.ToWorldRect(_core.LocalBounds).Overlaps(ground)) return false;
            foreach (var q in Quarters) if (q.World.Overlaps(ground)) return false;
            foreach (var site in _core.FuelSites)
                if (Vector2.Distance(_core.Frame.ToWorldRect(site.Box).center, ground.center) < 400f) return false;
            foreach (var other in _fuel)
                if (Vector2.Distance(other.center, ground.center) < 400f) return false;
            _fuel.Add(ground);
            return true;
        }

        public CoreRegion(CoreDistrict core, Rect city, int seed, int suburbCount)
        {
            _core = core;
            var dice = new System.Random(seed ^ 0x52454749);
            var names = new StreetNames(seed, Array.Empty<bool>(), Array.Empty<bool>());
            int suburbName = 0;
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (core.Layout?.Territory != null)
                foreach (var quarter in core.Layout.Territory.Quarters) usedNames.Add(quarter.Name);
            var access = core.RegionGateways ?? RasterGateways.Select(core.Raster, seed);
            foreach (var chosen in access)
            {
                var edge = chosen.Edge;
                var face = core.Frame.ToWorld(chosen.Face);
                Connections.Add(new Connection { Edge = edge, Across = Across(edge, face),
                    CityNode = core.Net.Nodes[chosen.Junction], CityFace = face });
            }
            if (Connections.Count == 0)
                throw new InvalidOperationException("Core has no clear outer road junction for the region.");

            // Keep the port and its sea on a lateral shore, away from the Core river's
            // north/south mouths. Which shore and the position on it are seed choices.
            CityEdge harborSide = dice.Next(2) == 0 ? CityEdge.West : CityEdge.East;
            CityEdge airportSide = harborSide == CityEdge.West ? CityEdge.East : CityEdge.West;
            float harborAt = Anchor(harborSide);
            var harbor = new HarborDemo.HarborDistrict { berths = 3 + dice.Next(2) };
            Add(harbor, DistrictKind.Harbor, harborSide, harborAt, new[] { 0f, 240f }, 0);
            // One works zone behind the port, on the port's own road. A deal whose artery
            // has no clear mouth at either end is thrown away and the next seed dealt.
            int industrySeed = dice.Next();
            bool works = false;
            for (int attempt = 0; attempt < 24 && !works; attempt++)
            {
                works = Add(new IndustrialDistrict { portZone = true }, DistrictKind.Pad, harborSide,
                            harborAt + 360f, null, industrySeed);
                industrySeed = dice.Next();
            }
            if (!works)
                Debug.LogWarning("[Region] No port works zone after 24 layout attempts; the port stands alone.");
            Add(new AirportDemo.AirportDistrict(), DistrictKind.Airport, airportSide,
                Anchor(airportSide), new[] { 0f }, 0);
            for (int i = 0; i < Mathf.Clamp(suburbCount, 0, 4); i++)
            {
                var side = i % 2 == 0 ? CityEdge.North : CityEdge.South;
                var slot = new DistrictSlot { seed = dice.Next(), sizeAcross = 3 + dice.Next(2), sizeDeep = 3 + dice.Next(2) };
                float anchor = Anchor(side);
                var river = _core.Frame.ToWorldRect(_core.Layout.Water);
                float bank = _core.Layout.Water.width > 0f && anchor < river.center.x ? -1f : 1f;
                Add(SuburbDemo.SuburbDistrict.ForCity(slot, 2f), DistrictKind.Suburb, side,
                    anchor + bank * (i / 2 * 650f), new[] { 0f }, slot.seed);
            }

            PortIndustryLayout.Arrange(Quarters, Connections);

            // The belt contains every access line even when a small city receives a wide
            // airport or the industrial estate extends past its original rectangle.
            float x0 = city.xMin, x1 = city.xMax, z0 = city.yMin, z1 = city.yMax;
            foreach (var c in Connections)
                if (Vertical(c.Edge)) { x0 = Mathf.Min(x0, c.Across - 140); x1 = Mathf.Max(x1, c.Across + 140); }
                else { z0 = Mathf.Min(z0, c.Across - 140); z1 = Mathf.Max(z1, c.Across + 140); }
            BeltBounds = Rect.MinMaxRect(x0, z0, x1, z1);

            // The port and its works were packed together. Preserve their shared back
            // edge and plot gaps instead of separating each estate independently. Every
            // other district stands at least clear of the expressway's outer collector,
            // which a river mouth can push well past the belt on the east or west.
            var water = _core.Frame.ToWorldRect(_core.Layout.Water);
            foreach (var q in Quarters)
            {
                float strip = 520f + dice.Next(5) * 30f;
                bool portArea = q.District is HarborDemo.HarborDistrict || q.District is IndustrialDistrict;
                Place(q, portArea ? q.Slot.strip
                                  : Mathf.Max(strip, RegionalRing.ClearOf(BeltBounds, water, seed, q.Slot.edge)));
                if (!portArea) Separate(q);
            }

            float Anchor(CityEdge edge)
            {
                var connection = Connections.Find(c => c.Edge == edge && c.CityNode != null);
                if (connection != null) return connection.Across;
                float lo = Vertical(edge) ? city.xMin : city.yMin;
                float hi = Vertical(edge) ? city.xMax : city.yMax;
                return Mathf.Round((lo + (hi - lo) * (0.35f + (float)dice.NextDouble() * 0.3f)) / 5f) * 5f;
            }

            bool Add(IDistrict district, DistrictKind kind, CityEdge edge, float at, float[] links, int districtSeed)
            {
                var q = new Quarter { District = district, Slot = new DistrictSlot { kind = kind,
                    edge = edge, seed = districtSeed == 0 ? dice.Next() : districtSeed,
                    name = kind == DistrictKind.Suburb ? NextSuburbName()
                         : kind == DistrictKind.Harbor ? names.City + " Docks"
                         : kind == DistrictKind.Airport ? names.City + " Regional Airport" : "Port Industry" } };
                district.Plan(links, q.Slot.seed);
                if (kind == DistrictKind.Suburb && _core.Layout.Water.width > 0f)
                {
                    var water = _core.Frame.ToWorldRect(_core.Layout.Water);
                    var turned = DistrictFrame.At(0f, 0f, RasterGateways.Yaw(edge)).ToWorldRect(district.LocalBounds);
                    if (at + turned.xMin < water.xMax + 60f && at + turned.xMax > water.xMin - 60f)
                        at = at < water.center.x ? water.xMin - 80f - turned.xMax
                                                 : water.xMax + 80f - turned.xMin;
                }
                // Run the works' artery inland from the quay. After a quarter turn in the
                // harbour's frame its western mouth faces the city; it must be a real
                // junction mouth on the artery itself, not on a service street behind a
                // tier. The seaward end is a dead end at the cut, joined to the port's
                // back street by the builder.
                if (district is IndustrialDistrict industrial)
                {
                    var gateways = RasterGateways.Find(industrial.Raster);
                    float arteryTo = industrial.Layout?.Roads.MainRoad.y ?? 0f;
                    var west = gateways.FindAll(g => g.Edge == CityEdge.West && g.Face.z > 0f && g.Face.z < arteryTo);
                    if (west.Count == 0) return false; // reject this deal before publishing any quarter or connection
                    q.IndustryGateway = west[0];
                    q.IndustryOrigin = west[0].Face;
                }
                if (kind == DistrictKind.Suburb)
                {
                    var relative = DistrictFrame.At(0f, 0f, RasterGateways.Yaw(edge)).ToWorldRect(district.LocalBounds);
                    foreach (var previous in Quarters)
                    {
                        if (previous.Slot.edge != edge || previous.Slot.kind != DistrictKind.Suburb) continue;
                        var old = Connections.Find(c => c.Quarter == previous);
                        var oldBounds = DistrictFrame.At(0f, 0f, RasterGateways.Yaw(edge)).ToWorldRect(previous.District.LocalBounds);
                        if (at + relative.xMin < old.Across + oldBounds.xMax + 100f &&
                            at + relative.xMax > old.Across + oldBounds.xMin - 100f)
                            at = at < _core.Frame.ToWorldRect(_core.Layout.Water).center.x
                                ? old.Across + oldBounds.xMin - 100f - relative.xMax
                                : old.Across + oldBounds.xMax + 100f - relative.xMin;
                    }
                }
                Quarters.Add(q);
                var values = links ?? new[] { 0f };
                var frame = DistrictFrame.At(0f, 0f, RasterGateways.Yaw(edge));
                for (int p = 0; p < values.Length; p++)
                {
                    float across = at + Across(edge, frame.ToWorldDir(new Vector3(values[p], 0f, 0f)));
                    var existing = district is IndustrialDistrict ? null :
                        Connections.Find(c => c.Edge == edge && c.Quarter == null && Mathf.Abs(c.Across - across) < 0.1f);
                    if (existing == null)
                    { existing = new Connection { Edge = edge, Across = across }; Connections.Add(existing); }
                    existing.Quarter = q;
                    existing.Portal = p;
                }
                return true;
            }

            string NextSuburbName()
            {
                string name;
                do { name = names.Quarter(suburbName++); } while (!usedNames.Add(name));
                return name;
            }
        }

        void Place(Quarter q, float strip)
        {
            q.Slot.strip = strip;
            var first = Connections.Find(c => c.Quarter == q && c.Portal == 0);
            var edge = q.Slot.edge;
            float u = edge == CityEdge.South ? BeltBounds.yMin - strip : edge == CityEdge.North ? BeltBounds.yMax + strip
                    : edge == CityEdge.West ? BeltBounds.xMin - strip : BeltBounds.xMax + strip;
            var origin = Vertical(edge) ? new Vector3(first.Across, 0f, u) : new Vector3(u, 0f, first.Across);
            var frame = new DistrictFrame { origin = origin, yaw = RasterGateways.Yaw(edge) };
            if (q.District is IndustrialDistrict)
            {
                frame.yaw = (frame.yaw + 90) % 360;
                frame.origin -= frame.ToWorldDir(q.IndustryOrigin);
            }
            q.District.Frame = frame;
        }

        void Separate(Quarter q)
        {
            // Every correction passes the far edge of an earlier rectangle. A district
            // cannot encounter that rectangle again while travelling outward, so this
            // terminates without an arbitrary escape distance or a failed whole city.
            bool moved;
            do
            {
                moved = false;
                foreach (var other in Quarters)
                {
                    if (other == q) break;
                    var box = other.World;
                    box.xMin -= 60; box.xMax += 60; box.yMin -= 60; box.yMax += 60;
                    var own = q.World;
                    if (!box.Overlaps(own)) continue;
                    float distance = q.Slot.edge == CityEdge.South ? own.yMax - box.yMin
                        : q.Slot.edge == CityEdge.North ? box.yMax - own.yMin
                        : q.Slot.edge == CityEdge.West ? own.xMax - box.xMin : box.xMax - own.xMin;
                    Place(q, q.Slot.strip + distance + 5f);
                    moved = true;
                }
            } while (moved);
        }

        public static CoreRegion TryCreate(CoreDistrict core, Rect world, int seed, int suburbs)
        {
            try { return new CoreRegion(core, world, seed, suburbs); }
            catch (Exception error)
            {
                Debug.LogError($"[CoreDemo] region seed {seed} could not be planned; continuing the Core city. {error}");
                return null;
            }
        }

        public bool TryPortal(Connection c, out DistrictPortal portal)
        {
            portal = default;
            if (c.Quarter == null) return true;
            if (c.Quarter.District is IndustrialDistrict industry)
            {
                if (industry.Net == null || c.Quarter.IndustryGateway.Junction >= industry.Net.Nodes.Count) return false;
                portal = new DistrictPortal { Local = c.Quarter.IndustryOrigin,
                    LocalDir = RasterGateways.Outward(c.Quarter.IndustryGateway.Edge),
                    Node = industry.Net.Nodes[c.Quarter.IndustryGateway.Junction], Tag = "industrial service road" };
                return portal.Node != null;
            }
            var district = c.Quarter.District;
            if (district.Portals == null) return false;
            foreach (var actual in district.Portals)
            {
                if (actual.Node == null || Mathf.Abs(Across(c.Edge, district.Frame.ToWorld(actual.Local)) - c.Across) > 0.5f) continue;
                portal = actual;
                return true;
            }
            return false;
        }

        public void ReconcilePortals()
        {
            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                var c = Connections[i];
                if (TryPortal(c, out _)) continue;
                Debug.LogError($"[CoreDemo] {c.Quarter?.Slot.name} did not build its road portal at {c.Across}; access omitted.");
                c.Quarter = null;
                if (c.CityNode == null) Connections.RemoveAt(i);
            }
            // a gate reached through works that never built their road gets an approach
            // of its own after all
            foreach (var c in Connections)
                if (c.Via != null && !Connections.Exists(o => o.Quarter == c.Via)) c.Via = null;
        }

        public void ReportConnections(int built, int lanes)
        {
            var bounds = WorldBounds;
            Debug.Log($"[CoreDemo] region seed {_core.Layout.Seed}: {Quarters.Count} districts, " +
                $"{built}/{Connections.Count} freeway connections built, {lanes} shared lanes; " +
                $"region span {bounds.width:F0} x {bounds.height:F0} m.");
        }

        public static bool Vertical(CityEdge edge) => edge == CityEdge.South || edge == CityEdge.North;
        public static float Across(CityEdge edge, Vector3 at) => Vertical(edge) ? at.x : at.z;
    }
}
