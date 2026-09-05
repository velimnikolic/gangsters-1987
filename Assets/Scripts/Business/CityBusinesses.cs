using System.Collections.Generic;
using LivingCity.Entities;
using LivingCity.Gameplay;
using LivingCity.Territory;
using UnityEngine;

namespace LivingCity.Business
{
    /// <summary>One business as a consumer needs to see it, whether or not its street is
    /// currently standing. Marker is the live view when there is one and null when the block
    /// is streamed out - which is exactly the case the old marker sweep could not answer.</summary>
    public readonly struct CityBusinessRow
    {
        public CityBusinessRow(
            TerritoryBusinessId id, string name, int blockId, TerritoryBlockId canonicalBlockId,
            Vector3 position, BusinessMarker marker)
        {
            Id = id;
            Name = name ?? "";
            BlockId = blockId;
            CanonicalBlockId = canonicalBlockId;
            Position = position;
            Marker = marker;
        }

        public TerritoryBusinessId Id { get; }
        public string Name { get; }
        public int BlockId { get; }
        public TerritoryBlockId CanonicalBlockId { get; }

        /// <summary>The doorstep, from the site when the simulation owns it and from the
        /// mesh when only a legacy marker does.</summary>
        public Vector3 Position { get; }

        public BusinessMarker Marker { get; }
    }

    /// <summary>
    /// The one place to ask "what businesses does this city have". It reads the simulated
    /// directory when a <see cref="BusinessRuntime"/> has dealt one, and falls back to
    /// PropertyRegistry's live markers in the older generated-city scenes, which have no
    /// plan-level business data to deal from.
    ///
    /// This exists because the two answers differ in the case that matters: a marker sweep
    /// can only see the shops whose block is on camera, so a consumer built on it silently
    /// loses half the city whenever the player walks away from it. Nothing here mutates
    /// anything; PropertyRegistry stays exactly as it was for the scenes that still own it.
    /// </summary>
    public static class CityBusinesses
    {
        static readonly List<CityBusinessRow> Rows = new List<CityBusinessRow>();
        static int rowsVersion = -1;
        static bool rowsBuilt;
        static BusinessRuntime rowsRuntime;

        /// <summary>True when a simulated directory is the authority in this scene.</summary>
        public static bool Simulated =>
            BusinessRuntime.Instance != null && BusinessRuntime.Instance.Populated;

        /// <summary>A repaint key: it moves when a business is registered, sold or shut, and
        /// when a legacy marker is added or removed.</summary>
        public static int Version
        {
            get
            {
                var runtime = BusinessRuntime.Instance;
                var simulated = runtime != null && runtime.Directory != null
                    ? runtime.Directory.Version
                    : 0;
                return simulated * 397 ^ PropertyRegistry.Version;
            }
        }

        public static IReadOnlyList<CityBusinessRow> All
        {
            get
            {
                Rebuild();
                return Rows;
            }
        }

        public static int Count => All.Count;

        /// <summary>Does this block have a business on it at all - streamed out or not?</summary>
        public static bool AnyOnBlock(int blockId)
        {
            var rows = All;
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].BlockId == blockId)
                    return true;
            return false;
        }

        /// <summary>The nearest business to a point on the map, within a radius in metres.</summary>
        public static bool TryNearest(Vector3 world, float radius, out CityBusinessRow row)
        {
            row = default;
            var best = radius * radius;
            var found = false;
            var rows = All;
            for (var i = 0; i < rows.Count; i++)
            {
                var dx = rows[i].Position.x - world.x;
                var dz = rows[i].Position.z - world.z;
                var sqr = dx * dx + dz * dz;
                if (sqr >= best)
                    continue;
                best = sqr;
                row = rows[i];
                found = true;
            }

            return found;
        }

        /// <summary>How far off the door a caller stands, metres. A site's Approach is the
        /// door ITSELF - a point on the line of the facade (BusinessSite.Approach) - and a
        /// man put down on that line is standing in the wall: off the walk lattice, unable
        /// to take a step, and stuck there for good. The doorstep is the pavement in front
        /// of it, so every walker is sent this far out along the entrance's own normal.</summary>
        public const float DoorstepClearanceMetres = 2f;

        /// <summary>Where a crew walks to reach a business - the pavement at the door, never
        /// the door line itself. The site's approach point when the simulation published one,
        /// the live mesh when only a marker exists.</summary>
        public static bool TryApproachPoint(TerritoryBusinessId id, out Vector3 point)
        {
            point = Vector3.zero;
            var runtime = BusinessRuntime.Instance;
            if (runtime != null && runtime.TryGetSite(id, out var site))
            {
                var y = BusinessViewBindings.TryGet(id, out var bound) && bound != null
                    ? bound.transform.position.y
                    : 0f;
                point = Doorstep(site, y);
                return true;
            }

            if (BusinessViewBindings.TryGet(id, out var marker) && marker != null)
            {
                point = marker.transform.position;
                return true;
            }

            var businesses = PropertyRegistry.Businesses;
            for (var i = 0; i < businesses.Count; i++)
            {
                if (businesses[i] == null || businesses[i].BusinessId != id)
                    continue;
                point = businesses[i].transform.position;
                return true;
            }

            return false;
        }

        /// <summary>The pavement in front of a site's door. Stepped out along the entrance's
        /// own outward normal when the plan gave one; away from the middle of the footprint
        /// when it did not, which is the same direction for any door on a facade.</summary>
        public static Vector3 Doorstep(BusinessSite site, float y = 0f)
        {
            var door = new Vector3(site.Approach.X, y, site.Approach.Z);
            var outward = new Vector3(site.ApproachOutward.X, 0f, site.ApproachOutward.Z);
            if (outward.sqrMagnitude < 0.0001f)
            {
                var centre = site.Footprint.Center;
                outward = new Vector3(site.Approach.X - centre.X, 0f, site.Approach.Z - centre.Z);
            }

            if (outward.sqrMagnitude < 0.0001f)
                return door;

            return door + outward.normalized * DoorstepClearanceMetres;
        }

        static void Rebuild()
        {
            var runtime = BusinessRuntime.Instance;
            var version = Version;
            if (rowsBuilt && version == rowsVersion && runtime == rowsRuntime)
            {
                RefreshMarkers();
                return;
            }

            rowsVersion = version;
            rowsRuntime = runtime;
            rowsBuilt = true;
            Rows.Clear();

            if (runtime != null && runtime.Populated)
            {
                var ids = runtime.Directory.BusinessIds;
                for (var i = 0; i < ids.Count; i++)
                {
                    if (!runtime.Directory.TryGet(ids[i], out var record) ||
                        !runtime.Catalog.TryGet(record.SiteId, out var site))
                        continue;

                    BusinessViewBindings.TryGet(ids[i], out var marker);
                    Rows.Add(new CityBusinessRow(
                        ids[i], record.DisplayName, site.LegacyBlockId, site.BlockHint,
                        new Vector3(site.Approach.X, 0f, site.Approach.Z), marker));
                }

                return;
            }

            var businesses = PropertyRegistry.Businesses;
            for (var i = 0; i < businesses.Count; i++)
            {
                var marker = businesses[i];
                if (marker == null)
                    continue;
                Rows.Add(new CityBusinessRow(
                    marker.BusinessId, marker.BusinessName, marker.BlockId,
                    marker.CanonicalBlockId, marker.transform.position, marker));
            }
        }

        /// <summary>
        /// Views come and go without the directory moving, so the cached rows have to pick
        /// up a marker that has just been bound (or lost one that streamed out) even when
        /// the version has not changed. Cheap: a dictionary probe per row, only for rows the
        /// simulation owns.
        /// </summary>
        static void RefreshMarkers()
        {
            var runtime = BusinessRuntime.Instance;
            if (runtime == null || !runtime.Populated)
                return;

            for (var i = 0; i < Rows.Count; i++)
            {
                BusinessViewBindings.TryGet(Rows[i].Id, out var marker);
                if (marker == Rows[i].Marker)
                    continue;
                Rows[i] = new CityBusinessRow(
                    Rows[i].Id, Rows[i].Name, Rows[i].BlockId, Rows[i].CanonicalBlockId,
                    Rows[i].Position, marker);
            }
        }

        /// <summary>Static state outlives Play when domain reload is off - PropertyRegistry's
        /// reason, closed the same way. A cached row list from the last session would
        /// otherwise be handed to the first consumer of the next one.</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Rows.Clear();
            rowsVersion = -1;
            rowsBuilt = false;
            rowsRuntime = null;
        }
    }
}
