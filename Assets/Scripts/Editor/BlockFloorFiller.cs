using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Lays the GROUND of a block being composed on a lot pad: the courtyard paving, the
    /// asphalt of a working yard and of its parking, the lawns under its greenery.
    ///
    /// The floor is planned from what is already standing there, which is the whole point
    /// of doing it here instead of in the demo:
    ///
    ///   * the BUILDINGS decide the character - a block of warehouses, car yards and
    ///     garages gets asphalt, apartments and shopfronts get concrete plates, a mansion
    ///     gets lawn - and each one gets a hard apron against its own walls, so no lawn
    ///     ever runs up to a front door;
    ///   * the PROPS decide the detail - a tree, a bush or a hedge run stands in grass
    ///     (and two of them close together share one lawn rather than two patches), while
    ///     bins, pallets, crates and power boxes are pulled back onto hard ground, because
    ///     nobody wheels a dumpster onto a lawn. Where the prop pass parked cars, the bays
    ///     and the room to swing into them are laid in asphalt whatever the rest of the
    ///     block is made of - the bay paint itself belongs to BlockParkingBay and is left
    ///     to it.
    ///
    /// So the order the passes run in does not matter. Dress the props first and the floor
    /// beds them in; lay the floor first and the prop pass ignores it (it is ground, not an
    /// obstacle - see BlockPad) and you re-run this afterwards to catch up.
    ///
    /// Same contract as BlockPropFiller: this is an authoring aid in the catalog scene, not
    /// city generation. The result stands in the scene under "auto floor", the user throws
    /// out what they dislike, and the capture pass writes the survivors into the block's
    /// recipe - so what ships is still a predefined bake. Nothing here runs at play time.
    ///
    /// A surface is planned on a 2.5 m grid, merged into rectangles, and each rectangle is
    /// divided into patches at that surface's own pitch - a slab's width for paving, wider
    /// for asphalt and grass - with every patch stretched from the block's tile for that
    /// surface. ONE tile per surface per block, laid square, from a short list of plates
    /// that read as the same city: the first version of this pass mixed two tiles per
    /// surface at random quarter turns out of four packs' worth of plates, and rolled
    /// patches of the other material into every yard, and the user's word for the result
    /// was "insane" - a checkerboard of orange, grey and black. Calm ground reads as
    /// ground; the variety belongs to the buildings and the props standing on it.
    /// </summary>
    public static class BlockFloorFiller
    {
        /// <summary>Everything laid here lands under this one child, so a re-roll cannot
        /// touch a piece the user placed or moved by hand.</summary>
        internal const string AutoRoot = BlockPad.FloorRoot;

        /// <summary>Planning grid. Half the kit module: fine enough for a bed round a
        /// single bush, coarse enough that a pad is a few hundred cells.</summary>
        const float Cell = 2.5f;

        /// <summary>How wide a patch of one surface reads: paving as slabs, asphalt and
        /// grass in wider stretches. Every rectangle is divided into roughly this pitch and
        /// EVERY patch rolls its own tile - which is what keeps a yard from coming out as
        /// one prefab repeated four hundred times.</summary>
        static float Pitch(Surface surface) => surface switch
        {
            Surface.Paving => 5f,
            Surface.Asphalt => 7.5f,
            _ => 10f,
        };

        const float Apron = 2.5f;      // hard band round every building
        const float GreenRing = 1.5f;  // how far a bed reaches past its plant
        const float LawnJoin = 9f;     // two plants closer than this share one lawn
        const float HardRing = 1f;     // hard ground pulled out round a bin or a pallet
        const float ParkRing = 2.5f;   // asphalt round a parked car: the room to get out

        const float AccentLift = 0.01f;   // cracks and clumps sit ON the surface
        const float PatchSink = 0.03f;    // tar patches show only their raised blob

        /// <summary>How far under the pad a piece of a bake has to lie before it counts as
        /// a hole rather than a foundation skirt. The same line BlockPad.Digs draws, so the
        /// building the box flags as sunken and the cells the mesh reports cannot
        /// disagree.</summary>
        const float Sunk = -0.2f;

        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string CityProp = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string PalmEnv = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        const string PalmProp = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        const string CopEnv = "Assets/Synty/PolygonPoliceStation/Prefabs/Environment/";
        const string GangGen = "Assets/Synty/PolygonGangWarfare/Prefabs/Generic/";
        const string GangBld = "Assets/Synty/PolygonGangWarfare/Prefabs/Buildings/";
        const string GangProp = "Assets/Synty/PolygonGangWarfare/Prefabs/Props/";

        enum Surface : byte { Hole, Paving, Asphalt, Grass }

        /// <summary>What a block is FOR, read off its buildings. It decides the ground the
        /// yard is made of and the apron each building keeps round itself.</summary>
        enum Kind { Court, Yard, Garden }

        /// <summary>Buildings that keep vehicles, deliveries or machinery: their ground is
        /// asphalt and their apron is asphalt. Checked before the garden list, so a
        /// ParkingGarage is a yard and not a park.</summary>
        static readonly string[] YardNames =
        {
            "CarYard", "ParkingGarage", "parking-garage", "carwash", "warehouse", "factory",
            "workshop", "Wharf", "Marina", "policestation", "firestation", "post", "Skatepark",
            "Fairground", "Depot", "Garage", "Industrial",
        };

        /// <summary>Buildings that stand in their own grounds rather than on a street
        /// frontage.</summary>
        static readonly string[] GardenNames = { "Mansion", "Toilet", "park-toilet" };

        /// <summary>Props that grow out of the ground: whatever they stand on becomes a
        /// bed. Planters and pot plants are deliberately NOT here - they are containers,
        /// and they stand on the hard.</summary>
        static readonly string[] GreenNames =
        {
            "Tree", "Palm", "Bush", "Hedge", "Topiary", "Sapling", "Shrub", "Flower", "Grass",
        };

        /// <summary>Props that are never found standing on a lawn. They pull hard ground
        /// out from under themselves - the back-of-house of a yard, in other words.</summary>
        static readonly string[] HardNames =
        {
            "Trash", "Rubbish", "Bin", "Bag", "Skip", "Crate", "Pallet", "Cardboard", "Junk",
            "PowerBox", "Powerbox", "Aircon", "Dumpster", "Barrel", "Vent", "Generator",
            "Container", "Forklift",
            // The frontage, which is pavement by definition: nobody sinks a hydrant, a
            // mailbox or a bus shelter into a lawn, and a pole is planted through the
            // kerb. Same rule as the back of house, opposite end of the block.
            "Hydrant", "Mailbox", "BusStop", "Pay_Phone", "Phones_", "Newspaper", "ATM",
            "HotdogStand", "Powerpole", "Bollard", "Billboard", "Taxi_Stand", "Bike_Stand",
            "SidewalkPoles", "Barrier", "Cone",
        };

        /// <summary>The parking pass's own pieces, read back off the pad: the painted bays,
        /// the lane arrow in the entrance drive, the wheel stops and the machines and signs
        /// that go with them, the meters that stand for an unpainted kerb, and the cars
        /// themselves. Ground a car stands on is asphalt in any block, and the asphalt
        /// reaches far enough past it for a door to open.
        ///
        /// Bollards and lamps are deliberately NOT here even though the parking stands some:
        /// the prop scatter puts those all round a yard, and naming them would pull an
        /// asphalt patch out from under every one of them.</summary>
        static readonly string[] ParkNames =
        {
            "ParkingLines", "Parking_Meter", "ParkingMeter", "Parking_Divider", "Parking_Stand",
            "Parking_Console", "Sign_Parking", "Road_Arrow", "Veh_",
        };

        /// <summary>
        /// The ground plates a block may be floored with, by surface: the plates the two
        /// city packs' own demo scenes lay under their courts, and nothing from the
        /// prison, gang or generic packs - their orange slabs and black floors are what
        /// turned a yard into a sample card. A block rolls ONE of these per surface and
        /// keeps to it; the list only exists so two blocks need not share the same plate.
        ///
        /// Nothing here is assumed to be any particular size - every candidate is measured
        /// after loading and stretched to the patch it is laid on.
        /// </summary>
        static readonly Dictionary<Surface, string[]> TilePaths = new()
        {
            [Surface.Paving] = new[]
            {
                PalmEnv + "SM_Env_Sidewalk_01",      // the PalmCity demo's own court plate
                CityEnv + "SM_Env_Sidewalk_01",
            },
            [Surface.Asphalt] = new[]
            {
                CityEnv + "SM_Env_Road_Bare_01",     // the same asphalt the streets are made of
            },
            [Surface.Grass] = new[]
            {
                CityEnv + "SM_Env_Grass_01",
                CopEnv + "SM_Env_Grass_01",
            },
        };

        // The weathering, one list per job and every one of them rolled per piece: the
        // same repetition that makes a floor look printed makes a scatter look printed.
        static readonly string[] PatchPaths =
        {
            CityEnv + "SM_Env_Road_Patch_01",
            CopEnv + "SM_Env_Ground_Patch_01",
        };

        /// <summary>What grows through a hard floor nobody has resurfaced since 1979.</summary>
        static readonly string[] WeedPaths =
        {
            GangProp + "SM_Prop_Grass_Cracks_01",
            GangProp + "SM_Prop_Grass_Cracks_02",
            GangProp + "SM_Prop_Grass_Cracks_03",
            GangProp + "SM_Prop_Grass_Cracks_04",
            GangProp + "SM_Prop_Grass_Cracks_05",
        };

        static readonly string[] DrainPaths =
        {
            PalmProp + "SM_Prop_Manhole_01",
            CityProp + "SM_Prop_Manhole_01",
            CopEnv + "SM_Env_Ground_Manhole_01",
            CityEnv + "SM_Env_Sidewalk_Grate_01",
            CityEnv + "SM_Env_Sidewalk_Grate_02",
            PalmEnv + "SM_Env_Road_Grate_01",
        };

        static readonly string[] ClumpPaths =
        {
            PalmEnv + "SM_Env_Grass_Clump_01",
            CityEnv + "SM_Env_Flower_01",
            GangGen + "SM_Gerneric_Grass_Patch_02",
        };

        [MenuItem("Tools/City/Catalog/Lay Block Floor", priority = 62)]
        public static void Lay()
        {
            if (!BlockLotCapture.OpenCatalogScene())
                return;
            if (!BlockPad.TryPick(out var pad))
                return;

            if (Lay(pad, out var root) < 0)
                EditorUtility.DisplayDialog(
                    "No ground tiles",
                    "Neither the paving plate nor the asphalt tile could be loaded - are both " +
                    "Synty city packs still under Assets/Synty? See the Console.", "OK");
            else
                Selection.activeGameObject = root.gameObject;
        }

        /// <summary>
        /// The pass itself, on a pad already decided: for the menu command above, and for
        /// <see cref="BlockRandomiser"/>, which rolls a whole block and runs every dressing
        /// pass over it in one go. Returns how many ground pieces were laid, or -1 when the
        /// packs' own tiles could not be loaded and there is nothing to lay a floor out of.
        /// </summary>
        internal static int Lay(BlockPad pad, out Transform root)
        {
            root = null;

            var tiles = LoadTiles();
            if (!tiles.ContainsKey(Surface.Paving) && !tiles.ContainsKey(Surface.Asphalt))
                return -1;

            // Read the pad BEFORE the old floor is cleared: the props are input to this
            // pass, the previous floor is not.
            var content = pad.Contents(withAutoProps: true);
            root = pad.ResetAuto(AutoRoot);

            Random.InitState(System.Environment.TickCount);
            var plan = Plan(pad, content, out var basis, out var story);
            var mix = Mix(tiles);
            var laid = Emit(pad, plan, mix, root);
            laid += Weather(pad, plan, content, root);

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[Floor] {laid} ground pieces laid over {pad.label} " +
                      $"({pad.width:F0} x {pad.depth:F0} m), base {Name(basis)}. {story}\n" +
                      $"[Floor] this block is made of: {Recipe(mix)}.\n" +
                      $"They stand under \"{root.name}\" - delete what you do not want, then run " +
                      "Tools/City/Catalog/Capture Blocks From Lot Pads to save the block. Run it again " +
                      "for another roll of the same plan.");
            return laid;
        }

        // ---------------------------------------------------------------- the planning

        /// <summary>
        /// The surface of every cell on the pad. Read in order: the block's own character
        /// carpets it, each building lays its apron, the greenery grows its beds, the
        /// service props and the parking take their hard ground back, and anything digging
        /// below the pad (a skatepark bowl) leaves a hole. Later steps overrule earlier
        /// ones, which is the whole ordering: a lawn may cover an apron, a bin may not
        /// stand on the lawn, and a car parks on asphalt whatever else was decided.
        /// </summary>
        static Surface[,] Plan(BlockPad pad, List<BlockPad.Item> content, out Surface basis,
                               out string story)
        {
            var nx = Mathf.Max(1, Mathf.FloorToInt(pad.width / Cell));
            var nz = Mathf.Max(1, Mathf.FloorToInt(pad.depth / Cell));
            var surf = new Surface[nx, nz];

            var buildings = content.Where(c => c.building).ToList();
            var props = content.Where(c => !c.building).ToList();

            // 1. what the block IS decides what its ground is made of
            float yard = 0f, garden = 0f, court = 0f;
            foreach (var b in buildings)
            {
                var area = b.Footprint.width * b.Footprint.height;
                switch (KindOf(b))
                {
                    case Kind.Yard: yard += area; break;
                    case Kind.Garden: garden += area; break;
                    default: court += area; break;
                }
            }
            basis = yard > court + garden ? Surface.Asphalt
                  : garden > court + yard ? Surface.Grass
                  : Surface.Paving;
            for (var i = 0; i < nx; i++)
                for (var j = 0; j < nz; j++)
                    surf[i, j] = basis;

            // 2. an apron against every wall, in that building's own material: a working
            //    yard keeps asphalt to its doors, a frontage keeps paving
            foreach (var b in buildings)
                Stamp(surf, pad, Grow(b.Footprint, Apron),
                      KindOf(b) == Kind.Yard ? Surface.Asphalt : Surface.Paving);

            // 3. (there used to be random patches of the other hard material here, "so
            //    no yard reads as poured in one go" - they read as a checkerboard, and
            //    they are gone. Open ground stays one surface; what breaks it up is what
            //    stands on it.)

            // 4. greenery grows a bed, and neighbouring plants share one lawn rather than
            //    each standing in a saucer of its own
            var greens = props.Where(IsGreen).ToList();
            foreach (var g in greens)
                Stamp(surf, pad, Grow(g.Footprint, GreenRing), Surface.Grass);
            for (var a = 0; a < greens.Count; a++)
                for (var b = a + 1; b < greens.Count; b++)
                {
                    var ca = greens[a].bounds.center;
                    var cb = greens[b].bounds.center;
                    if (Vector2.Distance(new Vector2(ca.x, ca.z), new Vector2(cb.x, cb.z)) > LawnJoin)
                        continue;
                    var between = Union(greens[a].Footprint, greens[b].Footprint);
                    // never across a building: two trees either side of a wing are two
                    // gardens, not one lawn through the middle of the building
                    if (buildings.Any(x => x.Footprint.Overlaps(between)))
                        continue;
                    Stamp(surf, pad, between, Surface.Grass);
                }

            // A lawn stops at the wall: whatever the beds did, every building keeps the
            // apron material under its own footprint.
            foreach (var b in buildings)
                Stamp(surf, pad, b.Footprint,
                      KindOf(b) == Kind.Yard ? Surface.Asphalt : Surface.Paving);

            // 5. bins, pallets and power boxes are never found on a lawn - they take a
            //    patch of hard ground back, and only from the grass
            var hard = basis == Surface.Grass ? Surface.Paving : basis;
            foreach (var p in props.Where(IsHard))
                Stamp(surf, pad, Grow(p.Footprint, HardRing), hard, only: Surface.Grass);

            // 6. the parking pass's lot, if it ran: asphalt under the bays and round every
            //    car, whatever the block is otherwise made of, and reaching far enough for
            //    a door to open. The paint is already down - BlockParkingBay lays it three
            //    centimetres up - and this is the ground it should have been painted on.
            //
            //    The auto parking's WHOLE rectangle is stamped, not just its pieces: a car
            //    park is one slab from the kerb to the back of the last row, and its aisle
            //    and its entrance drive carry nothing that would pull ground out for
            //    themselves. The parking lays asphalt of its own over that rectangle, so this
            //    is what makes the slab's edges disappear into ground of the same material.
            //
            //    Measured off that pass's own root and EVERYTHING under it, fence and slab
            //    included, rather than off the pieces a name recognises: it is the one root
            //    that holds nothing but parking. A car the user dragged in at the other end
            //    of the yard is deliberately not in it - that would asphalt the whole
            //    distance between the two - but it still takes its own ring below, which is
            //    also what a kerbside run needs to be wider than the cars standing in it.
            var parked = props.Where(IsParked).ToList();
            var lot = Bound(props.Where(p => BlockPad.Under(p.node, BlockPad.ParkingRoot)));
            if (lot.HasValue)
                Stamp(surf, pad, Grow(lot.Value, HardRing), Surface.Asphalt);
            foreach (var car in parked)
                Stamp(surf, pad, Grow(car.Footprint, ParkRing), Surface.Asphalt);

            // 7. a bake that digs below the pad must get no floor over the HOLE it needs -
            //    and over nothing else. A bake is one mesh: the police station carries its
            //    sunken garage, its shell and its forecourt in the same one, so its box
            //    reaches four metres under the pad across the whole 56 x 39 m of it, and
            //    holing the box left the entire station standing on bare lot pad. The hole
            //    is therefore read cell by cell off the geometry - see Sunken - and only
            //    where the bake really is below ground.
            var holes = 0;
            var open = 0;
            foreach (var b in buildings.Where(b => b.Digs))
            {
                holes++;
                var sunken = Sunken(pad, b, nx, nz);
                if (sunken == null)
                {
                    // Nothing could be read: hole the box, as this pass always did. A floor
                    // over a hole is a mistake; one missing under a building is only hidden.
                    Stamp(surf, pad, Grow(b.Footprint, -0.5f), Surface.Hole);
                    continue;
                }
                for (var i = 0; i < nx; i++)
                    for (var j = 0; j < nz; j++)
                        if (sunken[i, j])
                        {
                            surf[i, j] = Surface.Hole;
                            open++;
                        }
            }

            story = $"{buildings.Count} buildings ({Kinds(buildings)}), {greens.Count} plants bedded, " +
                    $"{props.Count(IsHard)} service props kept off the grass" +
                    (parked.Count > 0 ? $", {parked.Count} parking pieces on asphalt" : "") +
                    (holes > 0 ? $", {holes} sunken bake left open over {open} cells" : "") + ".";
            return surf;
        }

        static string Kinds(List<BlockPad.Item> buildings)
        {
            if (buildings.Count == 0)
                return "none";
            return string.Join(", ", buildings.GroupBy(KindOf)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"));
        }

        static Kind KindOf(BlockPad.Item item)
        {
            var name = NameOf(item);
            if (YardNames.Any(t => name.Contains(t, System.StringComparison.OrdinalIgnoreCase)))
                return Kind.Yard;
            if (GardenNames.Any(t => name.Contains(t, System.StringComparison.OrdinalIgnoreCase)))
                return Kind.Garden;
            return Kind.Court;
        }

        static bool IsGreen(BlockPad.Item item)
        {
            var name = NameOf(item);
            if (name.Contains("Planter", System.StringComparison.OrdinalIgnoreCase) ||
                name.Contains("PotPlant", System.StringComparison.OrdinalIgnoreCase))
                return false;   // a container, and it stands on the hard
            return GreenNames.Any(t => name.Contains(t, System.StringComparison.OrdinalIgnoreCase));
        }

        static bool IsHard(BlockPad.Item item) =>
            !IsGreen(item) &&
            HardNames.Any(t => NameOf(item).Contains(t, System.StringComparison.OrdinalIgnoreCase));

        static bool IsParked(BlockPad.Item item) =>
            ParkNames.Any(t => NameOf(item).Contains(t, System.StringComparison.OrdinalIgnoreCase));

        /// <summary>The asset name where one resolved, the scene name otherwise - a prop
        /// dragged in from the showroom carries the prefab's name either way.</summary>
        static string NameOf(BlockPad.Item item) =>
            string.IsNullOrEmpty(item.path)
                ? item.node.name
                : System.IO.Path.GetFileNameWithoutExtension(item.path);

        /// <summary>Cells with nothing standing on them, buildings and props alike.</summary>
        static bool[,] FreeMask(BlockPad pad, List<BlockPad.Item> content, int nx, int nz,
                                float clearance)
        {
            var free = new bool[nx, nz];
            for (var i = 0; i < nx; i++)
                for (var j = 0; j < nz; j++)
                    free[i, j] = true;

            foreach (var item in content)
                foreach (var (i, j) in Cells(pad, Grow(item.Footprint, clearance), nx, nz))
                    free[i, j] = false;
            return free;
        }

        /// <summary>
        /// The cells one bake digs out from under itself, read off its MESH rather than off
        /// its bounding box.
        ///
        /// The box is useless for this: an extracted building is a single baked mesh, and
        /// the police station's is its ground floor, its top floor AND the garage sunk three
        /// metres below the forecourt beside it - one box, four metres deep, over the whole
        /// station. What is wanted is the garage's own floor, so every triangle lying WHOLLY
        /// below the pad is rasterised and a cell is left open only when its centre falls in
        /// one. A triangle that breaks the surface - a wall, a ramp, the shell itself -
        /// carries no hole of its own: whatever it encloses is floored by the triangles at
        /// the bottom of it.
        ///
        /// Null when a mesh cannot be read at all, which the caller answers the old way.
        /// </summary>
        static bool[,] Sunken(BlockPad pad, BlockPad.Item item, int nx, int nz)
        {
            var mask = new bool[nx, nz];
            foreach (var filter in item.node.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (!mesh)
                    continue;

                Vector3[] vertices;
                int[] triangles;
                try
                {
                    // Legal in the editor whatever the importer says about Read/Write - the
                    // same read ChimneyVents makes of the kit's meshes.
                    vertices = mesh.vertices;
                    triangles = mesh.triangles;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Floor] {item.node.name}: mesh unreadable ({e.Message}) - " +
                                     "its whole footprint is left open instead of just its hole.");
                    return null;
                }

                // To world once per vertex rather than once per triangle corner: a bake is
                // hundreds of thousands of triangles and each vertex carries several.
                var matrix = filter.transform.localToWorldMatrix;
                var world = new Vector3[vertices.Length];
                for (var v = 0; v < vertices.Length; v++)
                    world[v] = matrix.MultiplyPoint3x4(vertices[v]);

                for (var t = 0; t + 2 < triangles.Length; t += 3)
                {
                    var a = world[triangles[t]];
                    var b = world[triangles[t + 1]];
                    var c = world[triangles[t + 2]];
                    if (a.y > Sunk || b.y > Sunk || c.y > Sunk)
                        continue;
                    Mark(mask, pad, a, b, c, nx, nz);
                }
            }
            return mask;
        }

        /// <summary>Marks every cell whose CENTRE falls inside one below-pad triangle.
        /// Centres and not touched cells: a hole that spread half a cell in each direction
        /// would leave a bare ring round every sunken bake.</summary>
        static void Mark(bool[,] mask, BlockPad pad, Vector3 a, Vector3 b, Vector3 c,
                         int nx, int nz)
        {
            var box = Rect.MinMaxRect(Mathf.Min(a.x, Mathf.Min(b.x, c.x)),
                                      Mathf.Min(a.z, Mathf.Min(b.z, c.z)),
                                      Mathf.Max(a.x, Mathf.Max(b.x, c.x)),
                                      Mathf.Max(a.z, Mathf.Max(b.z, c.z)));
            foreach (var (i, j) in Cells(pad, box, nx, nz))
            {
                if (mask[i, j])
                    continue;
                var centre = new Vector2(pad.MinX + (i + 0.5f) * Cell,
                                         pad.MinZ + (j + 0.5f) * Cell);
                if (Inside(centre, a, b, c))
                    mask[i, j] = true;
            }
        }

        /// <summary>Point in triangle, in plan. On an edge counts as in, so a centre landing
        /// exactly on the seam between two floor tiles is not missed by both.</summary>
        static bool Inside(Vector2 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = Side(p, a, b);
            var bc = Side(p, b, c);
            var ca = Side(p, c, a);
            return (ab >= 0f && bc >= 0f && ca >= 0f) || (ab <= 0f && bc <= 0f && ca <= 0f);
        }

        static float Side(Vector2 p, Vector3 a, Vector3 b) =>
            (b.x - a.x) * (p.y - a.z) - (b.z - a.z) * (p.x - a.x);

        // ----------------------------------------------------------------- the laying

        /// <summary>Merges the plan into as few rectangles as it can and lays a surface on
        /// each. Greedy: a run east, then as far north as the whole run keeps going.</summary>
        static int Emit(BlockPad pad, Surface[,] surf, Dictionary<Surface, Pair> mix, Transform root)
        {
            var nx = surf.GetLength(0);
            var nz = surf.GetLength(1);
            var done = new bool[nx, nz];
            var laid = 0;

            for (var j = 0; j < nz; j++)
                for (var i = 0; i < nx; i++)
                {
                    if (done[i, j] || surf[i, j] == Surface.Hole)
                        continue;
                    var kind = surf[i, j];

                    var i1 = i;
                    while (i1 + 1 < nx && !done[i1 + 1, j] && surf[i1 + 1, j] == kind)
                        i1++;

                    var j1 = j;
                    while (j1 + 1 < nz && Row(surf, done, i, i1, j1 + 1, kind))
                        j1++;

                    for (var a = i; a <= i1; a++)
                        for (var b = j; b <= j1; b++)
                            done[a, b] = true;

                    if (!mix.TryGetValue(kind, out var pair))
                        continue;
                    laid += LayRect(pair, Pitch(kind),
                                    new Rect(pad.MinX + i * Cell, pad.MinZ + j * Cell,
                                             (i1 - i + 1) * Cell, (j1 - j + 1) * Cell), root);
                }
            return laid;
        }

        static bool Row(Surface[,] surf, bool[,] done, int i0, int i1, int j, Surface kind)
        {
            for (var i = i0; i <= i1; i++)
                if (done[i, j] || surf[i, j] != kind)
                    return false;
            return true;
        }

        /// <summary>
        /// One rectangle of one surface, divided into patches at that surface's pitch and
        /// laid patch by patch, every patch the block's one tile for that surface, laid
        /// square - so the field reads as one poured surface with a regular joint, the way
        /// the packs' own demo courts do.
        ///
        /// The division is by ROUNDING, never by truncating: the patches are stretched to
        /// divide the rectangle exactly, so nothing overhangs the pad and no seam opens
        /// between two rectangles.
        /// </summary>
        static int LayRect(Pair pair, float pitch, Rect area, Transform root)
        {
            var cx = Mathf.Max(1, Mathf.RoundToInt(area.width / pitch));
            var cz = Mathf.Max(1, Mathf.RoundToInt(area.height / pitch));
            var step = new Vector2(area.width / cx, area.height / cz);

            for (var i = 0; i < cx; i++)
                for (var j = 0; j < cz; j++)
                {
                    var centre = new Vector2(area.xMin + step.x * (i + 0.5f),
                                             area.yMin + step.y * (j + 0.5f));
                    Place(pair.house, centre, step, 0, root);
                }
            return cx * cz;
        }

        /// <summary>Instantiates one tile so that it covers <paramref name="span"/> exactly
        /// and its top surface lands on the pad (y = 0), whatever the prefab's own size,
        /// pivot offset and thickness are.</summary>
        static void Place(Tile tile, Vector2 centre, Vector2 span, int quarter, Transform root)
        {
            // A quarter turn swaps which of the tile's own sides faces which world axis,
            // so the scale has to be worked out against the TURNED footprint.
            var odd = quarter % 2 == 1;
            var scale = new Vector3(odd ? span.y / tile.size.x : span.x / tile.size.x, 1f,
                                    odd ? span.x / tile.size.y : span.y / tile.size.y);
            var rotation = Quaternion.Euler(0f, 90f * quarter, 0f);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(tile.prefab);
            instance.transform.SetParent(root, worldPositionStays: false);
            instance.transform.localScale = scale;
            instance.transform.rotation = rotation;

            // The prefab's renderers are not centred on its pivot, so the pivot goes
            // wherever it has to for the MESH to land on the rectangle.
            var offset = rotation * Vector3.Scale(scale, tile.offset);
            instance.transform.position = new Vector3(centre.x - offset.x, -tile.top * scale.y,
                                                      centre.y - offset.z);
        }

        // ------------------------------------------------------------------- weathering

        /// <summary>
        /// What keeps a floor from reading as a bathroom: tar patches on the asphalt, weeds
        /// through the cracks of anything hard, a drain or two, clumps in the lawns. All at
        /// their own size on top of the surface, all on ground nothing is standing on, and
        /// every one of them rolled from its list rather than repeated.
        /// </summary>
        static int Weather(BlockPad pad, Surface[,] surf, List<BlockPad.Item> content, Transform root)
        {
            var nx = surf.GetLength(0);
            var nz = surf.GetLength(1);
            var free = FreeMask(pad, content, nx, nz, clearance: 0.5f);

            var patches = Load(PatchPaths);
            var weeds = Load(WeedPaths);
            var drains = Load(DrainPaths);
            var clumps = Load(ClumpPaths);

            var hard = new List<(int i, int j)>();
            var asphalt = new List<(int i, int j)>();
            var grass = new List<(int i, int j)>();
            for (var i = 0; i < nx; i++)
                for (var j = 0; j < nz; j++)
                {
                    if (!free[i, j])
                        continue;
                    switch (surf[i, j])
                    {
                        case Surface.Asphalt: asphalt.Add((i, j)); hard.Add((i, j)); break;
                        case Surface.Paving: hard.Add((i, j)); break;
                        case Surface.Grass: grass.Add((i, j)); break;
                    }
                }

            // The wear level rolls per block, so two blocks side by side stop sharing one
            // grey - the same trick the demo's own lot floors use.
            var wear = Random.Range(0.1f, 0.6f);
            var placed = 0;
            placed += Sprinkle(patches, asphalt, pad, Mathf.RoundToInt(asphalt.Count * wear * 0.12f), 14,
                               -PatchSink, root);
            placed += Sprinkle(weeds, hard, pad, Mathf.RoundToInt(hard.Count * wear * 0.10f), 16,
                               AccentLift, root);
            placed += Sprinkle(drains, hard, pad, Random.Range(0, 3), 3, AccentLift, root);
            placed += Sprinkle(clumps, grass, pad, grass.Count / 3, 12, AccentLift, root);
            return placed;
        }

        static int Sprinkle(List<GameObject> prefabs, List<(int i, int j)> cells, BlockPad pad,
                            int count, int cap, float lift, Transform root)
        {
            if (prefabs.Count == 0 || cells.Count == 0)
                return 0;

            count = Mathf.Clamp(count, 0, Mathf.Min(cap, cells.Count));
            var taken = new HashSet<(int, int)>();
            var placed = 0;
            for (var attempt = 0; placed < count && attempt < count * 6 + 10; attempt++)
            {
                var cell = cells[Random.Range(0, cells.Count)];
                if (!taken.Add(cell))
                    continue;

                // A piece of the list, not the list's first: a yard patched with one tar
                // blob repeated is the same complaint as a floor of one tile.
                var prefab = prefabs[Random.Range(0, prefabs.Count)];
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(root, worldPositionStays: false);
                instance.transform.SetPositionAndRotation(
                    new Vector3(pad.MinX + (cell.i + 0.5f) * Cell, lift,
                                pad.MinZ + (cell.j + 0.5f) * Cell),
                    Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                placed++;
            }
            return placed;
        }

        // ------------------------------------------------------------------- the tiles

        /// <summary>One measured ground tile.</summary>
        sealed class Tile
        {
            public GameObject prefab;
            public Vector2 size;        // its own footprint, measured
            public Vector3 offset;      // where its mesh sits relative to its pivot, XZ
            public float top;           // how far its surface stands above its pivot
        }

        /// <summary>The tile ONE block lays a surface from. Rolled per run from the short
        /// list, so re-running the pass gives the same plan in different concrete.</summary>
        sealed class Pair
        {
            public Tile house;

            public override string ToString() => house == null ? "-" : house.prefab.name;
        }

        /// <summary>What a surface settles for when its own tile is not in the project.
        /// A wrong-looking floor is a note to the author; a MISSING one is a hole in the
        /// block through which the lot pad shows.</summary>
        static readonly (Surface want, Surface instead)[] Fallbacks =
        {
            (Surface.Grass, Surface.Paving),
            (Surface.Paving, Surface.Asphalt),
            (Surface.Asphalt, Surface.Paving),
        };

        /// <summary>Every candidate that loads and measures flat, per surface - not the
        /// first one that does. The choosing happens per block, in <see cref="Mix"/>.</summary>
        static Dictionary<Surface, List<Tile>> LoadTiles()
        {
            var tiles = new Dictionary<Surface, List<Tile>>();
            var skipped = 0;
            foreach (var (surface, paths) in TilePaths.Select(p => (p.Key, p.Value)))
            {
                var found = new List<Tile>();
                foreach (var path in paths)
                {
                    var tile = Measure(path);
                    if (tile != null)
                        found.Add(tile);
                    else
                        skipped++;
                }
                if (found.Count > 0)
                    tiles[surface] = found;
            }

            var stood = new List<string>();
            // Twice round, so grass can fall back to paving and paving on to asphalt.
            for (var pass = 0; pass < 2; pass++)
                foreach (var (want, instead) in Fallbacks)
                    if (!tiles.ContainsKey(want) && tiles.TryGetValue(instead, out var list))
                    {
                        tiles[want] = list;
                        stood.Add($"{Name(want)} laid as {Name(instead)}");
                    }
            if (stood.Count > 0)
                Debug.LogWarning("[Floor] no tile of its own for: " + string.Join(", ", stood) + ".");
            Debug.Log("[Floor] ground tiles available: " +
                      string.Join(", ", tiles.Select(t => $"{Name(t.Key)} {t.Value.Count}")) +
                      $" ({skipped} candidates missing or not flat).");
            return tiles;
        }

        /// <summary>What THIS block is made of: one tile per surface, drawn from the short
        /// list, so two blocks side by side need not share a plate but each is one plate
        /// throughout.</summary>
        static Dictionary<Surface, Pair> Mix(Dictionary<Surface, List<Tile>> tiles)
        {
            var mix = new Dictionary<Surface, Pair>();
            foreach (var (surface, variants) in tiles.Select(t => (t.Key, t.Value)))
            {
                if (variants.Count == 0)
                    continue;
                mix[surface] = new Pair { house = variants[Random.Range(0, variants.Count)] };
            }
            return mix;
        }

        static string Recipe(Dictionary<Surface, Pair> mix) =>
            mix.Count == 0
                ? "nothing"
                : string.Join("; ", mix.Select(m => $"{Name(m.Key)} {m.Value}"));

        /// <summary>Loads a tile and measures it. A prefab that is not a flat plate (a
        /// tuft, a kerb, a whole road junction) is refused here rather than stretched over
        /// a courtyard, which is what makes the candidate lists above safe.</summary>
        static Tile Measure(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path + ".prefab");
            if (!prefab)
                return null;

            var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            probe.transform.localScale = Vector3.one;
            var measured = BlockLotCapture.RendererBounds(probe);
            Object.DestroyImmediate(probe);
            if (!measured.HasValue)
                return null;

            var bounds = measured.Value;
            if (bounds.size.x < 1f || bounds.size.z < 1f || bounds.size.y > 0.8f)
            {
                Debug.Log($"[Floor] {System.IO.Path.GetFileName(path)} is not a flat ground tile " +
                          $"({bounds.size.x:F1} x {bounds.size.z:F1} x {bounds.size.y:F1} m) - skipped.");
                return null;
            }

            return new Tile
            {
                prefab = prefab,
                size = new Vector2(bounds.size.x, bounds.size.z),
                offset = new Vector3(bounds.center.x, 0f, bounds.center.z),
                top = bounds.max.y,
            };
        }

        /// <summary>Every prefab of a weathering list a pack actually ships, so the scatter
        /// rolls a piece rather than repeating one.</summary>
        static List<GameObject> Load(string[] paths)
        {
            var found = new List<GameObject>();
            foreach (var path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path + ".prefab");
                if (prefab)
                    found.Add(prefab);
            }
            return found;
        }

        // ------------------------------------------------------------------ small work

        static string Name(Surface surface) => surface.ToString().ToLowerInvariant();

        static Rect Grow(Rect rect, float by) =>
            Rect.MinMaxRect(rect.xMin - by, rect.yMin - by, rect.xMax + by, rect.yMax + by);

        static Rect Union(Rect a, Rect b) =>
            Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                            Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        /// <summary>The one rectangle a set of items stands in, or nothing when the set is
        /// empty - which is not the same answer as an empty rectangle at the origin.</summary>
        static Rect? Bound(IEnumerable<BlockPad.Item> items)
        {
            Rect? all = null;
            foreach (var item in items)
                all = all.HasValue ? Union(all.Value, item.Footprint) : item.Footprint;
            return all;
        }

        /// <summary>Paints a world rectangle onto the plan, optionally only over cells that
        /// currently hold one particular surface.</summary>
        static void Stamp(Surface[,] surf, BlockPad pad, Rect area, Surface with,
                          Surface? only = null)
        {
            foreach (var (i, j) in Cells(pad, area, surf.GetLength(0), surf.GetLength(1)))
                if (only == null || surf[i, j] == only.Value)
                    surf[i, j] = with;
        }

        /// <summary>Every cell a world rectangle touches, clipped to the pad.</summary>
        static IEnumerable<(int i, int j)> Cells(BlockPad pad, Rect area, int nx, int nz)
        {
            var i0 = Mathf.Clamp(Mathf.FloorToInt((area.xMin - pad.MinX) / Cell), 0, nx - 1);
            var i1 = Mathf.Clamp(Mathf.CeilToInt((area.xMax - pad.MinX) / Cell) - 1, 0, nx - 1);
            var j0 = Mathf.Clamp(Mathf.FloorToInt((area.yMin - pad.MinZ) / Cell), 0, nz - 1);
            var j1 = Mathf.Clamp(Mathf.CeilToInt((area.yMax - pad.MinZ) / Cell) - 1, 0, nz - 1);

            // An area entirely off the pad clamps to a single edge cell; drop it rather
            // than painting the corner.
            if (area.xMax < pad.MinX || area.xMin > pad.MaxX ||
                area.yMax < pad.MinZ || area.yMin > pad.MaxZ)
                yield break;

            for (var i = i0; i <= i1; i++)
                for (var j = j0; j <= j1; j++)
                    yield return (i, j);
        }
    }
}
