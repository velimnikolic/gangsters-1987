using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>What a building is, as far as a survey sheet cares. The nine the design
    /// sheet names, each with its own fill out of the terrain palette.</summary>
    public enum MapBuildingKind
    {
        House,
        Apartments,
        Storefront,
        Tower,
        Warehouse,
        Factory,
        Civic,
        Hangar,
        Terminal,
    }

    /// <summary>
    /// One real building of the 3D city, as the map holds it. The footprint is the one
    /// the thing actually has in the world - not a blob, not a lot, not a block - which
    /// is the whole reason buildings are data here and not baked into the base layer.
    /// </summary>
    public sealed class MapBuilding
    {
        public int Id;

        /// <summary>The transform in the scene, or null for a quarter's own buildings -
        /// the port's sheds, a village's houses - which the district reported by
        /// footprint as it built them and which carry no picker collider.</summary>
        public Transform Tf;

        /// <summary>True footprint, world XZ, metres.</summary>
        public Rect World;

        public float Height;
        public int Floors;
        public MapBuildingKind Kind;
        public string Name;

        /// <summary>The district it stands in, held by index rather than looked up: the
        /// rail counts who holds what over every building in the city twice a second,
        /// and asking the turf table to find the district again for each of them is
        /// twenty rectangle tests per building for an answer that cannot change.</summary>
        public int DistrictId = -1;
        public string District;

        /// <summary>The family's front, when this building IS one. Non-null means the
        /// books below are real.</summary>
        public GangFront Front;

        /// <summary>Weekly takings and staff, off the front's own books. Both -1 for
        /// every other building in the city, because the project has no such figure for
        /// one and the map does not invent numbers.</summary>
        public int Takings = -1;
        public int Staff = -1;

        public float Area => World.width * World.height;
    }

    /// <summary>
    /// Every building in the city, and the cached raster of them.
    ///
    /// Collected the way the world's own picker sees them: the colliders under the
    /// blocks root, measured by their renderers, plus the buildings the outlying
    /// quarters reported as they built them. Nothing is generated - the design sheet's
    /// prototype rolled a plausible city so the map could be judged, and this reads the
    /// real one instead.
    ///
    /// The layer is a buffer with alpha that is baked when the framing changes or when
    /// somebody takes a building, and blitted every frame otherwise. That is the sheet's
    /// performance note applied where it matters: the prototype redrew three hundred and
    /// thirty buildings a frame, and this city has thousands.
    /// </summary>
    public sealed class MapBuildings
    {
        /// <summary>Metres to a storey. Nothing in the project records how many floors a
        /// building has, so it is read off the height the renderers actually measure -
        /// which is a derivation and is flagged as one on the card.</summary>
        public const float Storey = 3.2f;

        /// <summary>At and above this the sheet gives a building its one and only 3D
        /// cue: a copy of its roof offset a pixel up and left.</summary>
        const int TallFloors = 6;

        /// <summary>Under this, a collider is a prop and not a building.</summary>
        const float SmallestSide = 4f;
        const float SmallestRise = 2f;

        readonly List<MapBuilding> _all = new List<MapBuilding>();
        readonly MapRaster _layer = new MapRaster();

        MapSheet _baked;
        bool _dirty = true;
        int _paintedOwnership = -1;

        public IReadOnlyList<MapBuilding> All => _all;
        public int Count => _all.Count;

        // ------------------------------------------------------------------ collect

        public void Collect(RoadDemoBuilder builder, Transform blockRoot, MapTurf turf)
        {
            _all.Clear();

            if (blockRoot != null)
            {
                foreach (var collider in blockRoot.GetComponentsInChildren<Collider>(true))
                {
                    // Triggers are not buildings. They are how other layers make
                    // something answer a raycast without stopping anything - a parcel
                    // that opens a card, a feeler on a car - and one of them is often a
                    // box the size of a whole block. Measured as a footprint it would
                    // put a single building over an entire quarter. The world's own
                    // picker ignores them for the same reason.
                    if (collider.isTrigger)
                        continue;

                    var tf = collider.transform;
                    var renderers = tf.GetComponentsInChildren<Renderer>();
                    if (renderers.Length == 0)
                        continue;

                    var bounds = renderers[0].bounds;
                    for (var i = 1; i < renderers.Length; i++)
                        bounds.Encapsulate(renderers[i].bounds);

                    if (bounds.size.x < SmallestSide || bounds.size.z < SmallestSide ||
                        bounds.size.y < SmallestRise)
                        continue;   // a bin, a hydrant, a bench: not a building

                    Add(Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z),
                        bounds.size.y, tf.name, tf.parent != null ? tf.parent.name : null,
                        tf, turf);
                }
            }

            if (builder != null)
                foreach (var (area, rise, name) in builder.QuarterRoofs)
                    Add(area, rise, name, null, null, turf);

            // Biggest first, so a shed against a tower block still takes its own click:
            // the pick walks this list backwards and the small footprints are on top.
            _all.Sort((a, b) => b.Area.CompareTo(a.Area));
            for (var i = 0; i < _all.Count; i++)
                _all[i].Id = i;

            BindFronts();
            _dirty = true;
        }

        void Add(Rect world, float height, string name, string bake, Transform tf,
            MapTurf turf)
        {
            var district = turf?.At(world.center);
            var floors = Mathf.Max(1, Mathf.RoundToInt(height / Storey));
            _all.Add(new MapBuilding
            {
                Tf = tf,
                World = world,
                Height = height,
                Floors = floors,
                Kind = KindOf(name, bake, world, floors),
                Name = Friendly(name),
                DistrictId = district != null ? district.Id : -1,
                District = district != null && !string.IsNullOrEmpty(district.Name)
                    ? district.Name : "OUTSKIRTS",
            });
        }

        /// <summary>Which of the buildings a family keeps its door behind. The front is
        /// a component on the bake root and a building here may be a child of it, so the
        /// match is by transform ancestry and falls back to whichever footprint the door
        /// itself stands in.</summary>
        void BindFronts()
        {
            var fronts = GangFront.All;
            for (var i = 0; i < fronts.Count; i++)
            {
                var front = fronts[i];
                if (front == null)
                    continue;

                // The door first: a front is a storefront, and the building it is the
                // front OF is the one its doorstep stands against. Only if no footprint
                // owns the doorstep does the component's own bake root decide - which
                // it does badly, because every child of that bake answers to it.
                MapBuilding best = null;
                var smallest = float.MaxValue;
                var door = new Vector2(front.Door.x, front.Door.z);

                for (var b = 0; b < _all.Count; b++)
                {
                    var building = _all[b];
                    var grown = Rect.MinMaxRect(
                        building.World.xMin - 4f, building.World.yMin - 4f,
                        building.World.xMax + 4f, building.World.yMax + 4f);
                    if (!grown.Contains(door) || building.Area >= smallest)
                        continue;
                    smallest = building.Area;
                    best = building;
                }

                if (best == null)
                    for (var b = 0; b < _all.Count; b++)
                    {
                        var building = _all[b];
                        if (building.Tf == null || building.Area >= smallest ||
                            building.Tf.GetComponentInParent<GangFront>() != front)
                            continue;
                        smallest = building.Area;
                        best = building;
                    }

                if (best == null)
                    continue;
                best.Front = front;
                if (front.Books == null)
                    continue;
                best.Takings = front.Books.Takings;
                best.Staff = front.Books.Staff;
                if (!string.IsNullOrEmpty(front.Books.Sign))
                    best.Name = front.Books.Sign;
            }
        }

        // --------------------------------------------------------------- who holds it

        /// <summary>
        /// Whose building this is. A claim beats everything; a family's own front is
        /// always theirs; and failing both, a building belongs to whoever holds the
        /// ground it stands on - which is the design sheet's own rule, that a building
        /// takes its district's colour until somebody takes the building.
        /// </summary>
        public int GangOf(MapBuilding building, MapTurf turf, MapOwnership owned)
        {
            if (owned != null && owned.TryGet(building.Id, out var claimed))
                return claimed;
            if (building.Front != null)
                return building.Front.GangId;
            var district = turf?.Get(building.DistrictId);
            return district != null && !district.Contested ? district.Gang : -1;
        }

        // -------------------------------------------------------------------- picking

        /// <summary>The building under a raster pixel, with the sheet's own half-pixel
        /// slack so a one-pixel shed can still be hit. Walked backwards: small
        /// footprints are on top.</summary>
        public MapBuilding At(Vector2 px, MapSheet sheet)
        {
            for (var i = _all.Count - 1; i >= 0; i--)
            {
                var box = sheet.Box(_all[i].World);
                if (px.x >= box.xMin - 0.5f && px.x <= box.xMax + 0.5f &&
                    px.y >= box.yMin - 0.5f && px.y <= box.yMax + 0.5f)
                    return _all[i];
            }
            return null;
        }

        public MapBuilding Get(int id) => id >= 0 && id < _all.Count ? _all[id] : null;

        // ----------------------------------------------------------------------- bake

        public void Invalidate() => _dirty = true;

        public MapRaster Layer(MapSheet sheet, MapTurf turf, MapOwnership owned)
        {
            var ownership = owned?.Version ?? 0;
            if (!_dirty && ownership == _paintedOwnership && _baked.Matches(sheet))
                return _layer;

            _dirty = false;
            _paintedOwnership = ownership;
            _baked = sheet;
            _layer.Clear(new Color32(0, 0, 0, 0));

            // Every shadow first, then every building. The design sheet draws each
            // building's shadow immediately before the building itself, which is right
            // when a city has three hundred buildings with air between them - and wrong
            // when it has three thousand standing shoulder to shoulder, because then
            // each shadow lands on the roof of its neighbour and a block comes out as
            // black and white noise instead of as a block. Two passes cost one extra
            // walk of a list that is only walked when the framing changes.
            foreach (var building in _all)
            {
                if (!sheet.Sees(building.World))
                    continue;
                var box = sheet.Box(building.World);
                _layer.LayerFill(box.xMin + 1, box.yMin + 1, box.width, box.height,
                    MapPalette.Ink);
            }

            foreach (var building in _all)
            {
                if (!sheet.Sees(building.World))
                    continue;
                Draw(_layer, sheet.Box(building.World), building,
                    GangOf(building, turf, owned));
            }

            return _layer;
        }

        /// <summary>One building: the fake-height copy up and left if it is tall enough
        /// to earn one, its fill, a sparse roof speckle, and the owner's stripe along the
        /// top row. Its shadow was laid in the pass before this one.</summary>
        static void Draw(MapRaster into, RectInt box, MapBuilding building, int gang)
        {
            var fill = Fill(building.Kind);
            var x = box.xMin;
            var y = box.yMin;
            var w = box.width;
            var h = box.height;

            if (building.Floors >= TallFloors && w > 1 && h > 1)
            {
                into.LayerFill(x - 1, y - 1, w, h, MapPalette.Ink);
                into.LayerFill(x - 1, y - 1, w - 1, h - 1, fill);
            }

            into.LayerFill(x, y, w, h, fill);

            var speckle = Mathf.Max(1, w * h / 16);
            var spanX = Mathf.Max(1, w - 2);
            var spanY = Mathf.Max(1, h - 2);
            for (var i = 0; i < speckle; i++)
                into.LayerFill(x + 1 + i * 7 % spanX, y + 1 + i * 5 % spanY, 1, 1, MapPalette.Roof);

            if (gang < 0)
                return;

            var colour = MapPalette.Gang(gang);
            into.LayerFill(x, y, w, 1, colour);
            if (w > 3)
                into.LayerFill(x, y + h - 1, 1, 1, colour);
        }

        public static Color32 Fill(MapBuildingKind kind)
        {
            switch (kind)
            {
                case MapBuildingKind.House: return MapPalette.BldgB;
                case MapBuildingKind.Apartments: return MapPalette.BldgA;
                case MapBuildingKind.Tower: return MapPalette.BldgC;
                case MapBuildingKind.Warehouse: return MapPalette.Steel;
                case MapBuildingKind.Factory: return MapPalette.Roof;
                case MapBuildingKind.Civic: return MapPalette.Concrete;
                case MapBuildingKind.Hangar: return MapPalette.Steel;
                case MapBuildingKind.Terminal: return MapPalette.Steel;
                default: return MapPalette.BldgC;
            }
        }

        public static string Label(MapBuildingKind kind)
        {
            switch (kind)
            {
                case MapBuildingKind.House: return "ROW HOUSE";
                case MapBuildingKind.Apartments: return "APARTMENTS";
                case MapBuildingKind.Tower: return "OFFICE TOWER";
                case MapBuildingKind.Warehouse: return "WAREHOUSE";
                case MapBuildingKind.Factory: return "FACTORY HALL";
                case MapBuildingKind.Civic: return "CIVIC BUILDING";
                case MapBuildingKind.Hangar: return "HANGAR";
                case MapBuildingKind.Terminal: return "TERMINAL";
                default: return "STOREFRONT";
            }
        }

        // -------------------------------------------------------------------- naming

        /// <summary>
        /// What a building IS, read off the name of the thing it was built from. Nothing
        /// in the project records a type, so the prefab name is the evidence - and it is
        /// good evidence, because both catalogues name their pieces for what they are
        /// ("building-warehouse-large", "SM_Bld_OfficeSquare").
        ///
        /// Order matters: "policestation" has to reach Civic before "station" sends it
        /// anywhere else, and "depot-garage" is a depot before it is a garage.
        /// </summary>
        public static MapBuildingKind KindOf(string name, string bake, Rect world,
            int floors)
        {
            // The piece's own name first, then the bake it came out of: a Synty city
            // cluster names its pieces "City_07_I" and says nothing, while the block
            // they were baked into is called "warehouse-block" and says everything.
            if (Named(name, out var kind) || Named(bake, out kind))
                return kind;
            return Measured(world, floors);
        }

        /// <summary>
        /// What a building is when nothing has named it: read off the shape it
        /// actually has. A derivation, like the floor count, and flagged as one on the
        /// card - but a derivation off measured geometry rather than a guess at what is
        /// inside. Without it, a city whose catalogue names its pieces by index comes
        /// out in one colour and the map's whole type palette says nothing.
        /// </summary>
        static MapBuildingKind Measured(Rect world, int floors)
        {
            var area = world.width * world.height;
            if (floors >= TallFloors)
                return MapBuildingKind.Tower;
            if (area >= 400f && floors <= 2)
                return MapBuildingKind.Warehouse;
            if (area >= 150f && floors >= 3)
                return MapBuildingKind.Apartments;
            if (area <= 90f && floors <= 2)
                return MapBuildingKind.House;
            return MapBuildingKind.Storefront;
        }

        static bool Named(string name, out MapBuildingKind kind)
        {
            kind = MapBuildingKind.Storefront;
            if (string.IsNullOrEmpty(name))
                return false;
            var n = name.ToLowerInvariant();

            if (Has(n, "hangar")) { kind = MapBuildingKind.Hangar; return true; }
            if (Has(n, "terminal")) { kind = MapBuildingKind.Terminal; return true; }

            if (Has(n, "warehouse") || Has(n, "depot") || Has(n, "shed") ||
                Has(n, "storage") || Has(n, "workshop") || Has(n, "silo"))
            { kind = MapBuildingKind.Warehouse; return true; }

            if (Has(n, "factory") || Has(n, "foundry") || Has(n, "works") ||
                Has(n, "plant") || Has(n, "refinery") || Has(n, "industrial"))
            { kind = MapBuildingKind.Factory; return true; }

            if (Has(n, "hospital") || Has(n, "policestation") || Has(n, "police") ||
                Has(n, "firestation") || Has(n, "school") || Has(n, "bank") ||
                Has(n, "post") || Has(n, "hall") || Has(n, "church") || Has(n, "court") ||
                Has(n, "museum") || Has(n, "library") || Has(n, "civic") ||
                Has(n, "station") || Has(n, "parking") || Has(n, "toilet"))
            { kind = MapBuildingKind.Civic; return true; }

            if (Has(n, "tower") || Has(n, "skyscraper") || Has(n, "office"))
            { kind = MapBuildingKind.Tower; return true; }

            if (Has(n, "apartment") || Has(n, "condo") || Has(n, "res-") ||
                Has(n, "residential") || Has(n, "block-big"))
            { kind = MapBuildingKind.Apartments; return true; }

            if (Has(n, "house") || Has(n, "cottage") || Has(n, "bungalow") ||
                Has(n, "home") || Has(n, "villa"))
            { kind = MapBuildingKind.House; return true; }
            if (Has(n, "shop") || Has(n, "store") || Has(n, "cafe") ||
                Has(n, "diner") || Has(n, "restaurant") || Has(n, "burger") ||
                Has(n, "coffee") || Has(n, "casino") || Has(n, "hotel") ||
                Has(n, "club") || Has(n, "bar"))
            { kind = MapBuildingKind.Storefront; return true; }

            // Nothing in the name says what this is.
            return false;
        }

        static bool Has(string haystack, string needle) => haystack.Contains(needle);

        const string Clone = "(Clone)";

        /// <summary>"SM_Bld_Apartment_04(Clone)" reads as "APARTMENT 04", and
        /// "building-factory (works-02)" as "FACTORY (WORKS 02)". The prefab name IS the
        /// building's identity; it only needs the plumbing taken off.</summary>
        public static string Friendly(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return "BUILDING";

            // Unity's own instantiation suffix, and ONLY that. This used to cut the
            // name at the first bracket of any kind, which was harmless while the only
            // bracket a name ever carried was "(Clone)" - and started throwing away
            // information the day a district began reporting its buildings as
            // "building-factory (works-02)". The parcel is the half of that name which
            // says WHERE, and it went in the bin without a word.
            var name = prefabName.Trim();
            while (name.EndsWith(Clone, System.StringComparison.Ordinal))
                name = name.Substring(0, name.Length - Clone.Length).TrimEnd();

            if (name.StartsWith("SM_Bld_")) name = name.Substring("SM_Bld_".Length);
            else if (name.StartsWith("SM_Prop_")) name = name.Substring("SM_Prop_".Length);
            else if (name.StartsWith("SM_")) name = name.Substring("SM_".Length);
            if (name.StartsWith("building-")) name = name.Substring("building-".Length);

            name = name.Replace('-', ' ').Replace('_', ' ').Trim();
            return name.Length > 0 ? name.ToUpperInvariant() : "BUILDING";
        }
    }
}
