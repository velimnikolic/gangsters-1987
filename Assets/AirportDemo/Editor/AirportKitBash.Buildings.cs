using System.Collections.Generic;
using UnityEngine;

namespace AirportDemo.EditorTools
{
    // The buildings. Every one is assembled with its APRON face on +Z and its ground
    // at y = 0, and the builder turns them all a half circle to face the ramp - one
    // rule for the whole field, the terminal included (its landside comes round to
    // the kerb with the turn).
    //
    // The sheds are the gang pack's industrial shell on its 3 m module, two or three
    // courses of wall high, closed with a generated gable roof; the terminal, the
    // tower's shaft, the FBO and the gatehouse are the Generic Base kit on its 2.5 m
    // module with the Plaza's glass for the glazing. The two modules are never mixed
    // in one wall.
    //
    // The pack pieces wear the pack's atlas material; anything generated here wears a
    // flat colour instead. That is not a style choice - a Synty atlas is one texture
    // page shared by a whole pack, and a mesh whose UVs run over its own surface in
    // metres tiles the entire page across itself. See the note on paint in
    // AirportKitBash.cs.
    public static partial class AirportKitBash
    {
        const float MetalModule = 3f;       // measured: SM_Bld_Wall_Metal_01 is 3.00 x 3.13
        const float MetalCourse = 3f;       // what one course of it stands
        const float BaseModule = 2.5f;      // SM_Bld_Base_Wall_01 is 2.50 x 3.01
        const float BaseStorey = 3.01f;
        const float BigLeaf = 6f;           // SM_Bld_Wall_Metal_Door_Slide_02 is 6.00 x 6.00

        // ------------------------------------------------------------ the sheds

        /// <summary>A shed: walls in courses round a rectangle, corner posts, a gable
        /// over it and a floor under it. The front wall (+Z) is left to the caller,
        /// because that is where the doors go and every shed's are different.</summary>
        static void Shell(Transform root, float w, float d, int courses, Material skin)
        {
            float hx = w * 0.5f, hz = d * 0.5f;
            var inside = Vector3.zero;
            for (int c = 0; c < courses; c++)
            {
                float y = c * MetalCourse;
                WallRun(root, AirportKit.MetalWall, new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), inside, y, skin);
                WallRun(root, AirportKit.MetalWall, new Vector3(-hx, 0f, -hz), new Vector3(-hx, 0f, hz), inside, y, skin);
                WallRun(root, AirportKit.MetalWall, new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), inside, y, skin);
            }
            // the corner posts hide the seam where two runs meet at right angles
            var corner = P(AirportKit.MetalCorner);
            if (corner != null)
                for (int c = 0; c < courses; c++)
                {
                    float y = c * MetalCourse;
                    for (int i = 0; i < 4; i++)
                    {
                        float sx = (i & 1) == 0 ? -1f : 1f, sz = (i & 2) == 0 ? -1f : 1f;
                        var go = Put(root, AirportKit.MetalCorner, new Vector3(sx * hx, y, sz * hz), sx * sz > 0f ? 0f : 90f);
                        if (go != null) Paint(go, skin);
                    }
                }
            Floor(root, -hx, hx, -hz, hz, 0f, Concrete);
        }

        /// <summary>A box hangar: 21 m of frontage, 20 m deep, six metres to the eaves,
        /// an eighteen-metre opening closed by three of the pack's six-metre sliding
        /// leaves. The one hangar in the row that stands open has the same shell with
        /// the leaves parked against the jambs and the opening clear, so the camera
        /// sees the aeroplane inside it.</summary>
        static void BuildBoxHangar(bool closed)
        {
            float w = AirportSpec.HangarWidth, d = AirportSpec.HangarDepth, h = AirportSpec.HangarHeight;
            float door = AirportSpec.HangarDoorWidth;
            var root = Scratch(closed ? "hangar-box" : "hangar-box-open");
            var t = root.transform;
            float hx = w * 0.5f, hz = d * 0.5f, hd = door * 0.5f;
            var inside = Vector3.zero;

            Shell(t, w, d, courses: 2, Metal);

            // the front: a jamb either side of the opening, both courses
            for (int c = 0; c < 2; c++)
            {
                float y = c * MetalCourse;
                WallRun(t, AirportKit.MetalWall, new Vector3(-hx, 0f, hz), new Vector3(-hd, 0f, hz), inside, y, Metal);
                WallRun(t, AirportKit.MetalWall, new Vector3(hd, 0f, hz), new Vector3(hx, 0f, hz), inside, y, Metal);
            }

            if (closed)
            {
                for (int i = 0; i < 3; i++)
                {
                    float x0 = -hd + i * BigLeaf;
                    One(t, AirportKit.MetalDoorBig, new Vector3(x0, 0f, hz), new Vector3(x0 + BigLeaf, 0f, hz), inside, 0f, Metal);
                }
            }
            else
            {
                // slid aside: the leaves stack flat against the jambs on the outside
                // face, one west and two east, which is how a three-leaf door parks
                Slab(t, "door stack W", new Vector3(-hd - 0.75f, h * 0.5f, hz + 0.22f), new Vector3(1.5f, h, 0.34f), Steel);
                Slab(t, "door stack E", new Vector3(hd + 0.75f, h * 0.5f, hz + 0.22f), new Vector3(1.5f, h, 0.44f), Steel);
                // the head beam over the clear opening
                Slab(t, "lintel", new Vector3(0f, h + 0.22f, hz + 0.05f), new Vector3(door + 3f, 0.45f, 0.5f), Steel);
                // the rail the leaves hang from
                Slab(t, "door rail", new Vector3(0f, h - 0.12f, hz + 0.34f), new Vector3(w + 3.5f, 0.16f, 0.16f), Steel);
            }

            Gable(t, "roof", -hx, hx, -hz, hz, h, 2.0f, RoofMetal);
            // the ridge vent every metal shed carries
            Slab(t, "ridge", new Vector3(0f, h + 2.05f, 0f), new Vector3(w * 0.7f, 0.3f, 1.1f), Steel);
            BuildHangarFinish(t, w, d, h, 2f, "GENERAL AVIATION");
            BuildHangarEquipment(t, w, d, h, 2f, door, open: !closed, workshop: false);
            Bake(root, closed ? "airport-hangar-box" : "airport-hangar-box-open", apronFace: hz);
        }

        /// <summary>The maintenance shop: half as wide again, nine metres to the eaves,
        /// a thirty-metre opening under a three-metre header, a lean-to office on its
        /// east flank and the roof lights a workshop has.</summary>
        static void BuildMaintHangar()
        {
            float w = AirportSpec.MaintHangarWidth, d = AirportSpec.MaintHangarDepth;
            float h = 9f, door = AirportSpec.MaintHangarDoorWidth;
            var root = Scratch("hangar-maint");
            var t = root.transform;
            float hx = w * 0.5f, hz = d * 0.5f, hd = door * 0.5f;
            var inside = Vector3.zero;

            Shell(t, w, d, courses: 3, Metal);

            for (int c = 0; c < 3; c++)
            {
                float y = c * MetalCourse;
                WallRun(t, AirportKit.MetalWall, new Vector3(-hx, 0f, hz), new Vector3(-hd, 0f, hz), inside, y, Metal);
                WallRun(t, AirportKit.MetalWall, new Vector3(hd, 0f, hz), new Vector3(hx, 0f, hz), inside, y, Metal);
            }
            // the header over the door, the top course only
            WallRun(t, AirportKit.MetalWall, new Vector3(-hd, 0f, hz), new Vector3(hd, 0f, hz), inside, 2f * MetalCourse, Metal);
            for (int i = 0; i < 5; i++)
            {
                float x0 = -hd + i * BigLeaf;
                One(t, AirportKit.MetalDoorBig, new Vector3(x0, 0f, hz), new Vector3(x0 + BigLeaf, 0f, hz), inside, 0f, Metal);
            }
            Slab(t, "door rail", new Vector3(0f, 6f - 0.12f, hz + 0.3f), new Vector3(w, 0.18f, 0.18f), Steel);

            // a lean-to along the east flank: the stores and the shop office
            float lw = 5f;
            Slab(t, "lean-to", new Vector3(hx + lw * 0.5f, 1.6f, 0f), new Vector3(lw, 3.2f, d * 0.6f), Plaster);
            Slab(t, "lean-to roof", new Vector3(hx + lw * 0.5f, 3.35f, 0f), new Vector3(lw + 0.5f, 0.2f, d * 0.6f + 0.5f), Steel);
            Put(t, AirportKit.MetalManDoor, new Vector3(hx + lw - 0.1f, 0f, -2f), 90f);

            Gable(t, "roof", -hx, hx, -hz, hz, h, 3.0f, RoofMetal);
            // a raised lantern along the ridge, which is how a workshop this deep is
            // daylit - the old roof lights sat INSIDE the roof, where nothing could
            // ever see them
            Slab(t, "ridge lantern", new Vector3(0f, h + 3.1f, 0f), new Vector3(w * 0.55f, 0.9f, 3.2f), RoofMetal);
            for (int s = -1; s <= 1; s += 2)
                Slab(t, "lantern glazing", new Vector3(0f, h + 3.1f, s * 1.62f), new Vector3(w * 0.5f, 0.55f, 0.06f), Glass);
            BuildHangarFinish(t, w, d, h, 3f, "AIRCRAFT SERVICE");
            BuildHangarEquipment(t, w, d, h, 3f, door, open: false, workshop: true);
            Bake(root, "airport-hangar-maint", apronFace: hz);
        }

        /// <summary>The fire station: two bays, doors on the ramp, the hose tower and
        /// the number on the wall.</summary>
        static void BuildArff()
        {
            float w = AirportSpec.ArffWidth, d = AirportSpec.ArffDepth, h = 6f;
            var root = Scratch("arff");
            var t = root.transform;
            float hx = w * 0.5f, hz = d * 0.5f;
            var inside = Vector3.zero;
            Shell(t, w, d, courses: 2, Metal);
            // two six-metre bay doors with a pier between them
            One(t, AirportKit.MetalDoorBig, new Vector3(-hx + 1.5f, 0f, hz), new Vector3(-hx + 7.5f, 0f, hz), inside, 0f, Red);
            One(t, AirportKit.MetalDoorBig, new Vector3(hx - 7.5f, 0f, hz), new Vector3(hx - 1.5f, 0f, hz), inside, 0f, Red);
            for (int c = 0; c < 2; c++)
            {
                float y = c * MetalCourse;
                WallRun(t, AirportKit.MetalWall, new Vector3(-hx, 0f, hz), new Vector3(-hx + 1.5f, 0f, hz), inside, y, Metal);
                WallRun(t, AirportKit.MetalWall, new Vector3(-hx + 7.5f, 0f, hz), new Vector3(hx - 7.5f, 0f, hz), inside, y, Metal);
                WallRun(t, AirportKit.MetalWall, new Vector3(hx - 1.5f, 0f, hz), new Vector3(hx, 0f, hz), inside, y, Metal);
            }
            Gable(t, "roof", -hx, hx, -hz, hz, h, 1.6f, RoofMetal);
            // the hose tower, and the red band a fire station wears
            Slab(t, "hose tower", new Vector3(hx - 2f, 4.5f, -hz + 2f), new Vector3(3.4f, 9f, 3.4f), Plaster);
            Slab(t, "tower cap", new Vector3(hx - 2f, 9.1f, -hz + 2f), new Vector3(3.8f, 0.25f, 3.8f), Steel);
            Slab(t, "band", new Vector3(0f, 3.05f, hz + 0.16f), new Vector3(w, 0.5f, 0.08f), Red);
            Legend(t, "1", new Vector3(0f, 4.4f, hz + 0.2f), 1.2f, White);
            Bake(root, "airport-arff");
        }

        /// <summary>The freight shed: a big door to the ramp, a truck dock on the other
        /// side, an office in the corner.</summary>
        static void BuildCargoShed()
        {
            float w = AirportSpec.CargoWidth, d = AirportSpec.CargoDepth, h = 6f;
            var root = Scratch("cargo");
            var t = root.transform;
            float hx = w * 0.5f, hz = d * 0.5f;
            var inside = Vector3.zero;
            Shell(t, w, d, courses: 2, Metal);
            One(t, AirportKit.MetalDoorBig, new Vector3(-6f, 0f, hz), new Vector3(0f, 0f, hz), inside, 0f, Metal);
            One(t, AirportKit.MetalDoorBig, new Vector3(0f, 0f, hz), new Vector3(6f, 0f, hz), inside, 0f, Metal);
            for (int c = 0; c < 2; c++)
            {
                float y = c * MetalCourse;
                WallRun(t, AirportKit.MetalWall, new Vector3(-hx, 0f, hz), new Vector3(-6f, 0f, hz), inside, y, Metal);
                WallRun(t, AirportKit.MetalWall, new Vector3(6f, 0f, hz), new Vector3(hx, 0f, hz), inside, y, Metal);
            }
            Gable(t, "roof", -hx, hx, -hz, hz, h, 1.8f, RoofMetal);
            // the dock on the landside, with its two roller doors and a canopy
            var dock = Put(t, AirportKit.LoadingDock, new Vector3(-3f, 0f, -hz), 180f);
            if (dock != null) Paint(dock, Concrete);
            var dock2 = Put(t, AirportKit.LoadingDock, new Vector3(9f, 0f, -hz), 180f);
            if (dock2 != null) Paint(dock2, Concrete);
            Slab(t, "dock canopy", new Vector3(0f, 4.6f, -hz - 1.6f), new Vector3(w * 0.8f, 0.22f, 3.4f), Steel);
            Slab(t, "office", new Vector3(hx - 4f, 1.6f, -hz - 2.6f), new Vector3(7f, 3.2f, 5f), Plaster);
            Slab(t, "office roof", new Vector3(hx - 4f, 3.35f, -hz - 2.6f), new Vector3(7.5f, 0.22f, 5.5f), Steel);
            Legend(t, "AIR", new Vector3(0f, 5.2f, hz + 0.2f), 1.1f, White);
            Bake(root, "airport-cargo");
        }

        // ------------------------------------------------------------ the Base kit

        /// <summary>One storey of Base wall round a rectangle, every module dealt from
        /// a chooser so a wall can be plain, windowed or glazed run by run.</summary>
        static void BaseCourse(Transform root, float x0, float x1, float z0, float z1, float y,
                               System.Func<int, int, GameObject> chooser)
        {
            var inside = new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);
            // side = 0 south (-Z), 1 east, 2 north (+Z), 3 west
            WallRun(root, AirportKit.BaseWall, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), inside, y, null, i => chooser(0, i));
            WallRun(root, AirportKit.BaseWall, new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), inside, y, null, i => chooser(1, i));
            WallRun(root, AirportKit.BaseWall, new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), inside, y, null, i => chooser(2, i));
            WallRun(root, AirportKit.BaseWall, new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0), inside, y, null, i => chooser(3, i));
        }

        /// <summary>Which modules of an apron frontage this wide are gate doors: one
        /// for each airline stand, worked from where the stands actually are, so the
        /// doors move when the stands do rather than sitting at module numbers that
        /// were right when there were two of them.
        ///
        /// Each door's mirror is taken as well. WallRun lays a run from whichever end
        /// puts the piece's front outward, so the index a module has depends on a flip
        /// this code cannot see - but the stands are symmetric about the centreline, so
        /// a set that is symmetric too is right whichever way round the wall was laid.</summary>
        static HashSet<int> GateModules(float w)
        {
            var set = new HashSet<int>();
            var wall = P(AirportKit.BaseWall);
            if (wall == null) return set;
            float module = AirportKit.RunOf(wall).magnitude;
            if (module < 0.01f) return set;
            // the same count LayRun will arrive at, or the doors land a module out
            int n = Mathf.Max(1, Mathf.RoundToInt(w / module));
            if (n * module < w - 0.01f && w / n > module * 1.15f) n++;
            float step = w / n;
            for (int s = 0; s < AirportSpec.CommuterStandX.Length; s++)
            {
                int i = Mathf.Clamp(Mathf.FloorToInt((AirportSpec.GateDoorX(s) + w * 0.5f) / step), 0, n - 1);
                set.Add(i);
                set.Add(n - 1 - i);
            }
            return set;
        }

        /// <summary>The commuter terminal: the frontage the airline stands need, thirty
        /// deep, a glazed departure hall looking out at the ramp with a gate door to
        /// each stand, offices over the middle of it, and the plain landside wall with
        /// its doors that the kerb pulls up to. Baked apron-face on +Z like every other
        /// building here.</summary>
        static void BuildTerminal()
        {
            float w = AirportSpec.TerminalWidth, d = AirportSpec.TerminalDepth;
            var root = Scratch("terminal");
            var t = root.transform;
            float hx = w * 0.5f, hz = d * 0.5f;

            var window = P(AirportKit.BaseWindowDouble);
            var door = P(AirportKit.BaseDoorDouble);
            var wall = P(AirportKit.BaseWall);
            // the Plaza glass if it will bake, the Base kit's own big window if not
            var glass = Usable(AirportKit.GlassWall, window);
            var glassDoor = Usable(AirportKit.GlassDoor, door);

            // ground storey: the ramp side all glass with a gate door out to each
            // stand, the landside plain with the entrance doors in the middle, the
            // flanks windowed
            var gates = GateModules(w);
            int mid = Mathf.Max(1, Mathf.RoundToInt(w / Mathf.Max(0.01f, AirportKit.RunOf(P(AirportKit.BaseWall)).magnitude))) / 2;
            GameObject Ground(int side, int i)
            {
                if (side == 2) return gates.Contains(i) ? glassDoor : glass;                  // apron
                if (side == 0) return (i >= mid - 2 && i <= mid + 1) ? door : window;          // landside
                return i % 2 == 0 ? window : wall;
            }
            BaseCourse(t, -hx, hx, -hz, hz, 0f, Ground);
            FlatRoof(t, "roof", -hx, hx, -hz, hz, BaseStorey, RoofDeck, Plaster);

            // THE HALL. A hundred metres of single storey with one window band from end
            // to end is a corridor, not a terminal - it has no middle to walk into and
            // nothing to see it by from the road. So the middle goes up two storeys and
            // out to nearly the full depth: the departures hall, glazed on BOTH faces,
            // which is what every county terminal built in the jet age actually is.
            float ux = 23f, uz = hz - 2f;
            GameObject Hall(int side, int i)
            {
                if (side == 2 || side == 0) return glass;      // glazed to the ramp AND to the kerb
                return i % 2 == 0 ? glass : wall;
            }
            BaseCourse(t, -ux, ux, -uz, uz, BaseStorey, Hall);
            FlatRoof(t, "hall roof", -ux, ux, -uz, uz, BaseStorey * 2f, RoofDeck, Plaster);

            // and the piers that break the ribbon: without them the ground storey is one
            // unbroken band of glass a hundred metres long, which is the other half of
            // why the thing read as a slab
            for (float px = -hx + 12f; px < hx - 6f; px += 12.5f)
            {
                if (Mathf.Abs(px) < ux + 1f) continue;
                Slab(t, "pier", new Vector3(px, BaseStorey * 0.5f, -hz - 0.16f), new Vector3(0.9f, BaseStorey, 0.32f), Plaster);
                Slab(t, "pier", new Vector3(px, BaseStorey * 0.5f, hz + 0.16f), new Vector3(0.9f, BaseStorey, 0.32f), Plaster);
            }

            // the plant a flat roof carries. Spread over the WHOLE roof rather than three
            // units on the middle: plant is what stops a roof reading as a painted plane,
            // and there is no such thing as a bare hundred-metre roof.
            for (int i = -3; i <= 3; i++)
            {
                float px = i * 13f;
                if (Mathf.Abs(px) < ux + 3f) continue;
                Slab(t, "aircon", new Vector3(px, BaseStorey + 0.55f, -6f), new Vector3(3.2f, 1.1f, 2.2f), Steel);
                Slab(t, "aircon", new Vector3(px + 4f, BaseStorey + 0.4f, 5f), new Vector3(2.2f, 0.8f, 1.6f), Steel);
                Tube(t, "vent", new Vector3(px - 3f, BaseStorey + 0.45f, 1.5f), 0.28f, 0.9f, Steel, 8);
            }
            // the hall's own roof carries the stair head, the tank and the dishes
            Slab(t, "stair head", new Vector3(-ux + 5f, BaseStorey * 2f + 1.2f, 0f), new Vector3(4.4f, 2.4f, 3.6f), Plaster);
            Slab(t, "roof tank", new Vector3(ux - 6f, BaseStorey * 2f + 1f, -3f), new Vector3(3f, 2f, 3f), Steel);
            for (int i = -1; i <= 1; i += 2)
                Tube(t, "hall vent", new Vector3(i * 9f, BaseStorey * 2f + 0.5f, 4f), 0.32f, 1f, Steel, 8);

            // the fascia and the name, now carried on the HALL where they are read from
            // the road, not tucked under a one-storey eaves
            Slab(t, "fascia", new Vector3(0f, BaseStorey * 2f - 0.7f, -uz - 0.16f), new Vector3(ux * 1.5f, 1.5f, 0.18f), Steel);
            Legend(t, "AIRPORT", new Vector3(0f, BaseStorey * 2f - 0.7f, -uz - 0.28f), 0.95f, White, 180f);
            // the kerbside canopy: a real slab on posts over the doors, which is the one
            // thing that says "this is the way in" from a hundred metres away
            Slab(t, "entrance canopy", new Vector3(0f, BaseStorey - 0.35f, -hz - 3.2f), new Vector3(26f, 0.35f, 6.8f), White);
            Slab(t, "canopy band", new Vector3(0f, BaseStorey - 0.05f, -hz - 6.5f), new Vector3(26f, 0.45f, 0.3f), Blue);
            for (int i = -2; i <= 2; i++)
                Tube(t, "canopy post", new Vector3(i * 6f, 0f, -hz - 6.2f), 0.12f, BaseStorey - 0.5f, Steel, 8);
            BuildTerminalArchitecture(t, hx, hz, ux, uz);
            Bake(root, "airport-terminal", hz);
        }

        /// <summary>The fixed-base operator: a small block with a lounge looking at the
        /// ramp, the pilots' door and a shallow canopy over it.</summary>
        static void BuildFbo()
        {
            float w = AirportSpec.FboWidth, d = AirportSpec.FboDepth;
            var root = Scratch("fbo");
            var t = root.transform;
            float hx = w * 0.5f, hz = d * 0.5f;
            var window = P(AirportKit.BaseWindow);
            var wall = P(AirportKit.BaseWall);
            var door = P(AirportKit.BaseDoor);
            var glass = Usable(AirportKit.GlassWall, P(AirportKit.BaseWindowDouble) ?? window);
            var glassDoor = Usable(AirportKit.GlassDoor, door);
            GameObject Choose(int side, int i)
            {
                if (side == 2) return i == 3 ? glassDoor : glass;
                if (side == 0) return i == 3 ? door : wall;
                return i % 2 == 0 ? window : wall;
            }
            BaseCourse(t, -hx, hx, -hz, hz, 0f, Choose);
            FlatRoof(t, "roof", -hx, hx, -hz, hz, BaseStorey, RoofDeck, Plaster);
            Slab(t, "canopy", new Vector3(0f, BaseStorey - 0.3f, hz + 1.4f), new Vector3(w * 0.6f, 0.22f, 3f), Steel);
            for (int i = -1; i <= 1; i += 2)
                Tube(t, "canopy post", new Vector3(i * w * 0.25f, 0f, hz + 2.7f), 0.09f, BaseStorey - 0.4f, Steel, 8);
            Slab(t, "aircon", new Vector3(4f, BaseStorey + 0.5f, 0f), new Vector3(2.2f, 1f, 1.6f), Steel);
            Bake(root, "airport-fbo");
        }

        /// <summary>The tower: the manager's office at its foot, a square Base shaft
        /// rising out of it, a gallery, and an octagonal cab with sloped glazing.
        /// The beacon rides the cab roof.</summary>
        static void BuildTower()
        {
            var root = Scratch("tower");
            var t = root.transform;
            float ox = 6f, oz = 4.5f;      // the office block, 12 x 9
            float sx = 2.5f, sz = 2.5f;    // the shaft, 5 x 5
            float shaftTop = AirportSpec.TowerStoreys * BaseStorey;

            var window = P(AirportKit.BaseWindow);
            var wall = P(AirportKit.BaseWall);
            var door = P(AirportKit.BaseDoor);
            GameObject Office(int side, int i)
            {
                if (side == 0) return i == 2 ? door : window;
                return i % 2 == 0 ? window : wall;
            }
            BaseCourse(t, -ox, ox, -oz, oz, 0f, Office);
            FlatRoof(t, "office roof", -ox, ox, -oz, oz, BaseStorey, Concrete, Plaster);

            GameObject Shaft(int side, int i) => i == 0 ? window : wall;
            for (int s = 0; s < AirportSpec.TowerStoreys; s++)
                BaseCourse(t, -sx, sx, -sz, sz, s * BaseStorey, Shaft);

            // the gallery the cab sits on, a little wider than the shaft
            Slab(t, "gallery", new Vector3(0f, shaftTop + 0.12f, 0f), new Vector3(7.4f, 0.24f, 7.4f), Concrete);
            Slab(t, "gallery rail N", new Vector3(0f, shaftTop + 0.7f, 3.6f), new Vector3(7.4f, 0.9f, 0.1f), Steel);
            Slab(t, "gallery rail S", new Vector3(0f, shaftTop + 0.7f, -3.6f), new Vector3(7.4f, 0.9f, 0.1f), Steel);
            Slab(t, "gallery rail E", new Vector3(3.6f, shaftTop + 0.7f, 0f), new Vector3(0.1f, 0.9f, 7.4f), Steel);
            Slab(t, "gallery rail W", new Vector3(-3.6f, shaftTop + 0.7f, 0f), new Vector3(0.1f, 0.9f, 7.4f), Steel);

            BuildControlCab(t, shaftTop);
            float cabTop = shaftTop + 4.7f;
            // the beacon: the light that says "airport" from ten miles out
            Tube(t, "beacon post", new Vector3(0f, cabTop, 0f), 0.12f, 1.1f, Steel, 8);
            Slab(t, "beacon", new Vector3(0f, cabTop + 1.4f, 0f), new Vector3(0.9f, 0.55f, 0.5f), White);
            Slab(t, "beacon green", new Vector3(0f, cabTop + 1.4f, 0.27f), new Vector3(0.6f, 0.36f, 0.05f), Green);
            // the shop antennas, small enough not to fight the mast on the ground
            Tube(t, "aerial", new Vector3(2.6f, cabTop - 0.5f, -2.6f), 0.05f, 3.2f, Steel, 6);
            var dish = Put(t, AirportKit.SatDishSmall, new Vector3(-2.6f, shaftTop + 0.36f, -2.6f), 200f);
            if (dish != null) Paint(dish, White);
            Bake(root, "airport-tower");
        }

        /// <summary>The gatehouse: three metres square, glazed on all four sides so the
        /// man in it can see both ways down the fence.</summary>
        static void BuildGuardBooth()
        {
            var root = Scratch("guard-booth");
            var t = root.transform;
            float h = 1.25f;
            var window = P(AirportKit.BaseWindow);
            var door = P(AirportKit.BaseDoor);
            GameObject Choose(int side, int i) => side == 3 ? door : window;
            BaseCourse(t, -h, h, -h, h, 0f, Choose);
            FlatRoof(t, "roof", -h - 0.35f, h + 0.35f, -h - 0.35f, h + 0.35f, BaseStorey, Steel, Steel, 0.25f, 0.18f);
            Slab(t, "sill", new Vector3(0f, 1.15f, h + 0.06f), new Vector3(h * 2f, 0.12f, 0.35f), Concrete);
            Bake(root, "airport-guard-booth");
        }

        /// <summary>The fuel farm: two horizontal tanks on their saddles inside a bund,
        /// the delivery stand and its pipework. Avgas and jet A, the way a county field
        /// keeps them - out at the back, behind a wall, with a sign on it.</summary>
        static void BuildFuelFarm()
        {
            var root = Scratch("fuel-farm");
            var t = root.transform;
            float bx = 9f, bz = 6.5f;
            // the bund: a low concrete wall round the pair, which is the point of a farm
            Slab(t, "bund N", new Vector3(0f, 0.55f, bz), new Vector3(bx * 2f + 0.6f, 1.1f, 0.3f), Concrete);
            Slab(t, "bund S", new Vector3(0f, 0.55f, -bz), new Vector3(bx * 2f + 0.6f, 1.1f, 0.3f), Concrete);
            Slab(t, "bund E", new Vector3(bx, 0.55f, 0f), new Vector3(0.3f, 1.1f, bz * 2f), Concrete);
            Slab(t, "bund W", new Vector3(-bx, 0.55f, 0f), new Vector3(0.3f, 1.1f, bz * 2f), Concrete);
            Floor(t, -bx, bx, -bz, bz, 0.05f, Concrete);

            for (int i = 0; i < 2; i++)
            {
                float z = i == 0 ? 3f : -3f;
                var tank = Tube(t, "tank", new Vector3(-6.5f, 2.1f, z), 1.6f, 13f, i == 0 ? Steel : White, 14);
                tank.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
                for (int s = -1; s <= 1; s++)
                    Slab(t, "saddle", new Vector3(s * 4.5f, 0.5f, z), new Vector3(0.8f, 1.0f, 3.4f), Concrete);
                Tube(t, "vent", new Vector3(0f, 3.7f, z), 0.09f, 1.4f, Steel, 6);
            }
            // the pump stand and its pipe run
            Slab(t, "pump house", new Vector3(bx - 2.4f, 1.1f, 0f), new Vector3(2.2f, 2.2f, 2.6f), Plaster);
            Slab(t, "pipe", new Vector3(0f, 1.35f, 0f), new Vector3(bx * 1.6f, 0.16f, 0.16f), Steel);
            Slab(t, "no smoking", new Vector3(0f, 1.5f, -bz - 0.2f), new Vector3(2.4f, 0.9f, 0.08f), Red);
            Legend(t, "0", new Vector3(0f, 1.5f, -bz - 0.28f), 0.55f, White, 180f);
            Bake(root, "airport-fuel-farm");
        }
    }
}
