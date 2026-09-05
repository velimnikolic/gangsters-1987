using UnityEngine;

namespace HarborDemo
{
    /// <summary>What a cargo handler needs of the gear that moves its boxes: put the
    /// hook over this spot and say when it is there, follow the box while it flies,
    /// let go when it lands. A handler with no hook does the work invisibly, as the
    /// port did before it had cranes.</summary>
    public interface IHarborHook
    {
        /// <summary>Drive the hook down onto a box whose foot stands at this point;
        /// true once the spreader is on it and the lift may begin.</summary>
        bool Reach(Vector3 boxFoot);
        /// <summary>The box is in flight and its foot is here now: hold station on it.</summary>
        void Carry(Vector3 boxFoot);
        /// <summary>The box is down: the hook is free to park.</summary>
        void Release();
    }

    // The berth's ship-to-shore crane: a portal on two rails astride the quay, a boom
    // reaching out over the ship one way and back over the yard the other, a trolley
    // running the boom and a spreader on four falls beneath it. The gantry travels
    // along the quay, the trolley across it, the spreader up and down - which is
    // exactly the three axes a box's flight is made of, so the crane needs no plan of
    // its own: HarborCargo tells it where the next box stands, holds off until the
    // spreader is on it, and the crane then keeps station over the box until it is
    // down. Idle, the trolley parks mid-span with the spreader hoisted clear.
    //
    // Built at Play out of stretched Base-kit pillars, the ships' way - a container
    // crane is steel lattice and no Synty pack ships one - and driven as three nested
    // transforms, so the whole rig is two dozen renderers that move together.
    public sealed class HarborCrane : IHarborHook
    {
        // ------------------------------------------------------------ dimensions

        /// <summary>The waterside rail, clear of the coping's bollards and lamps, and
        /// the landward one, behind the live container row and before the standing
        /// stacks.</summary>
        public const float SeaRailZ = 5.6f, LandRailZ = 22f;
        /// <summary>The boom's sea end - out over a ship lying alongside - and its
        /// back end over the yard.</summary>
        public const float SeaTipZ = -23f, BackTipZ = 31f;
        public const float BoomY = 31f;
        public const float LegHalfX = 4.5f;
        /// <summary>How far the gantry may travel either side of its berth.</summary>
        public const float TravelHalf = 34f;

        /// <summary>Where the trolley waits between boxes - between the rails, clear of
        /// both portal frames - and how far under the boom the spreader is hoisted.</summary>
        const float ParkZ = 10f, ParkClear = 9f;
        /// <summary>The trolley runs just under the girders; the portal ties are set
        /// low enough for it and the spreader to pass over them.</summary>
        const float TrolleyY = BoomY - 1.4f, TieY = BoomY - 3.5f;
        /// <summary>A container's height: the spreader rides the box's top, not its foot.</summary>
        const float BoxTop = HarborShipSpec.BoxHeight + 0.025f;

        const float GantryCreep = 7f, TrolleyCreep = 11f, HoistCreep = 7f;
        const float GantryRun = 12f, TrolleyRun = 18f, HoistRun = 12f;

        // ------------------------------------------------------------ state

        Transform _gantry, _trolley, _spreader;
        readonly Transform[] _falls = new Transform[4];
        float _groundY;
        float _x, _z, _y;                     // gantry along the quay, trolley across, spreader top
        float _minX, _maxX;
        float _dt;
        bool _driven;                         // the cargo handler drove it this frame

        public Transform Root => _gantry;

        // ------------------------------------------------------------ paint
        //
        // One locker for the whole port: three cranes in the same livery, and three
        // materials rather than twelve.

        static Material _paint, _steel, _dark, _cab, _glass;

        static void LoadPaint()
        {
            if (_paint != null) return;
            _paint = HarborKit.Flat("CranePaint", new Color(0.56f, 0.38f, 0.17f), 0.25f);
            _steel = HarborKit.Flat("CraneSteel", new Color(0.27f, 0.30f, 0.31f), 0.35f);
            _dark = HarborKit.Flat("CraneRail", new Color(0.17f, 0.17f, 0.18f), 0.2f);
            _glass = HarborKit.Flat("Crane glazing", new Color(0.07f, 0.16f, 0.19f), 0.65f);
            _cab = HarborKit.Flat("CraneCab", new Color(0.82f, 0.80f, 0.74f), 0.3f);
        }

        // ------------------------------------------------------------ building

        /// <summary>The crane for the berth at <paramref name="berthX"/>, with its rails
        /// laid on the quay under it. Null if the Base kit is missing.</summary>
        public static HarborCrane Build(Transform live, Transform quay, float berthX, float groundY, int index)
        {
            var pillar = HarborKit.TryLoad(HarborKit.GenBase + "SM_Bld_Base_Pillar_01.prefab");
            if (pillar == null)
            {
                Debug.LogWarning("[HarborDemo] the Generic Base pillar is missing - the berths have no cranes.");
                return null;
            }
            LoadPaint();
            var crane = new HarborCrane { _groundY = groundY, _x = berthX };
            crane._minX = berthX - TravelHalf;
            crane._maxX = berthX + TravelHalf;

            // the rails stay on the quay; the gantry runs along them
            float railHalf = TravelHalf + LegHalfX + 3f;
            foreach (float rz in new[] { SeaRailZ, LandRailZ })
                Member(quay, pillar, new Vector3(berthX, groundY + 0.06f, rz),
                       new Vector3(railHalf * 2f, 0.12f, 0.5f), _dark, "CraneRail");

            var root = new GameObject("Crane " + index).transform;
            root.SetParent(live, false);
            root.localPosition = new Vector3(berthX, groundY, 0f);
            crane._gantry = root;

            // Tapered portal frames leave the transfer lane open under the crane.
            foreach (float lz in new[] { SeaRailZ, LandRailZ })
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    float lx = side * LegHalfX;
                    Strut(root, pillar, new Vector3(lx, 1.1f, lz), new Vector3(side * 3.2f, BoomY, lz),
                        1.25f, _paint, "Tapered portal leg");
                    Member(root, pillar, new Vector3(lx, 0.7f, lz), new Vector3(4.8f, 1.1f, 1.7f), _steel, "Travelling bogie");
                    for (int wheel = 0; wheel < 4; wheel++)
                    {
                        var tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        Object.Destroy(tyre.GetComponent<Collider>());
                        tyre.name = "Rail wheel";
                        tyre.transform.SetParent(root, false);
                        tyre.transform.localPosition = new Vector3(lx - 1.65f + wheel * 1.1f, 0.42f, lz);
                        tyre.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                        tyre.transform.localScale = new Vector3(0.8f, 0.25f, 0.8f);
                        Paint(tyre, _dark);
                    }
                    Strut(root, pillar, new Vector3(lx, BoomY - 8f, lz), new Vector3(0f, TieY, lz),
                        0.55f, _paint, "Portal knee brace");
                }
                Member(root, pillar, new Vector3(0f, TieY, lz), new Vector3(8f, 1.5f, 1.5f), _paint, "Portal header");
            }
            // Deep Warren trusses: the boom reads as structural steel even at district scale.
            float boomMid = (SeaTipZ + BackTipZ) * 0.5f, boomLen = BackTipZ - SeaTipZ;
            foreach (float gx in new[] { -3.2f, 3.2f })
            {
                foreach (float gy in new[] { BoomY, BoomY + 3.6f })
                    Member(root, pillar, new Vector3(gx, gy, boomMid), new Vector3(0.6f, 0.65f, boomLen), _paint, "Truss chord");
                int panel = 0;
                for (float z = SeaTipZ; z < BackTipZ - 0.01f; z += 4.5f, panel++)
                {
                    float end = Mathf.Min(z + 4.5f, BackTipZ);
                    Strut(root, pillar, new Vector3(gx, BoomY + (panel % 2 == 0 ? 0f : 3.6f), z),
                        new Vector3(gx, BoomY + (panel % 2 == 0 ? 3.6f : 0f), end), 0.26f, _paint, "Truss diagonal");
                }
                // Maintenance walk and handrails along the outside of the boom.
                Member(root, pillar, new Vector3(gx * 1.23f, BoomY + 0.5f, boomMid), new Vector3(0.9f, 0.12f, boomLen), _steel, "Catwalk");
                Member(root, pillar, new Vector3(gx * 1.37f, BoomY + 1.6f, boomMid), new Vector3(0.07f, 0.07f, boomLen), _steel, "Catwalk rail");
                for (float z = SeaTipZ; z <= BackTipZ; z += 3f)
                    Member(root, pillar, new Vector3(gx * 1.37f, BoomY + 1.05f, z), new Vector3(0.07f, 1.1f, 0.07f), _steel, "Stanchion");
            }
            for (float z = SeaTipZ; z <= BackTipZ; z += 4.5f)
                Member(root, pillar, new Vector3(0f, BoomY + 3.6f, z), new Vector3(6.4f, 0.3f, 0.3f), _paint, "Boom cross tie");
            foreach (float x in new[] { -3.2f, 3.2f })
            {
                var apex = new Vector3(x, BoomY + 13f, LandRailZ - 2f);
                Strut(root, pillar, new Vector3(x, BoomY + 3.6f, SeaRailZ), apex, 0.65f, _paint, "A frame sea leg");
                Strut(root, pillar, new Vector3(x, BoomY + 3.6f, BackTipZ), apex, 0.65f, _paint, "A frame back leg");
                Strut(root, pillar, apex, new Vector3(x, BoomY + 3.6f, SeaTipZ + 1f), 0.15f, _steel, "Suspension stay");
            }
            Member(root, pillar, new Vector3(0f, BoomY + 13f, LandRailZ - 2f), new Vector3(6.8f, 0.6f, 0.6f), _paint, "A frame crown");
            Member(root, pillar, new Vector3(0f, BoomY + 2.2f, BackTipZ - 4f), new Vector3(5.8f, 3.7f, 7f), _cab, "Machinery house");
            for (int vent = 0; vent < 8; vent++)
                Member(root, pillar, new Vector3(-2.92f, BoomY + 1.4f + vent * 0.22f, BackTipZ - 4f),
                    new Vector3(0.04f, 0.09f, 4f), _dark, "Machinery louvers");
            // Access ladder on the landward outer face, with intermittent resting platforms.
            for (float h = 1f; h < BoomY; h += 0.4f)
                Member(root, pillar, new Vector3(4.7f, h, LandRailZ + 0.85f), new Vector3(0.7f, 0.055f, 0.08f), _steel, "Ladder rung");
            foreach (float x in new[] { 4.32f, 5.08f })
                Member(root, pillar, new Vector3(x, BoomY * 0.5f, LandRailZ + 0.85f), new Vector3(0.08f, BoomY, 0.08f), _steel, "Ladder stile");
            crane.CombineStructure(root);

            // the trolley under the girders, with the driver's cab hung off its sea side
            var trolley = new GameObject("Trolley").transform;
            trolley.SetParent(root, false);
            trolley.localPosition = new Vector3(0f, TrolleyY, ParkZ);
            crane._trolley = trolley;
            crane._z = ParkZ;
            Member(trolley, pillar, Vector3.zero, new Vector3(6.4f, 1.2f, 2.8f), _steel, "TrolleyFrame");
            Member(trolley, pillar, new Vector3(0f, -1.7f, -2.8f), new Vector3(2f, 2.2f, 2.2f), _cab, "DriverCab");

            Member(trolley, pillar, new Vector3(0f, -1.5f, -3.92f), new Vector3(1.7f, 1.1f, 0.035f), _glass, "Cab windscreen");
            foreach (float x in new[] { -1.02f, 1.02f })
                Member(trolley, pillar, new Vector3(x, -1.5f, -2.8f), new Vector3(0.035f, 1.1f, 1.85f), _glass, "Cab side glass");

            // the spreader: a container-sized frame that hangs under the trolley
            var spreader = new GameObject("Spreader").transform;
            spreader.SetParent(root, false);
            crane._spreader = spreader;
            crane._y = BoomY - ParkClear;
            Member(spreader, pillar, Vector3.zero, new Vector3(6.8f, 0.5f, 0.9f), _paint, "SpreaderBeam");
            foreach (float sz in new[] { -1.12f, 1.12f })
                Member(spreader, pillar, new Vector3(0f, -0.2f, sz), new Vector3(6.25f, 0.25f, 0.22f), _steel, "SpreaderRail");
            foreach (float sx in new[] { -3.1f, 3.1f })
                Member(spreader, pillar, new Vector3(sx, 0.7f, 0f), new Vector3(0.3f, 0.7f, 2.5f), _steel, "SpreaderHead");

            // Four steel hoist cables, centred between the trolley and spreader heads.
            for (int i = 0; i < 4; i++)
                crane._falls[i] = Member(trolley, pillar, Vector3.zero,
                    new Vector3(0.055f, 1f, 0.055f), _dark, "Hoist cable").transform;

            crane.Apply();
            return crane;
        }

        /// <summary>One member: a Base pillar stretched into a box of this size with its
        /// centre where asked, painted, hung on the rig.</summary>
        static GameObject Member(Transform parent, GameObject pillar, Vector3 centre, Vector3 size, Material mat, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = size;
            go.transform.localPosition = centre;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static void Strut(Transform parent, GameObject pillar, Vector3 a, Vector3 b, float thick, Material mat, string name)
        {
            var d = b - a;
            if (d.sqrMagnitude < 0.0025f) return;
            var go = Member(parent, pillar, (a + b) * 0.5f, new Vector3(thick, d.magnitude, thick), mat, name);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        }

        readonly System.Collections.Generic.List<Mesh> _meshes = new System.Collections.Generic.List<Mesh>();

        // Only the fixed steelwork is combined. Trolley, spreader and falls remain articulated.
        void CombineStructure(Transform root)
        {
            var groups = new System.Collections.Generic.Dictionary<Material, System.Collections.Generic.List<CombineInstance>>();
            var filters = root.GetComponentsInChildren<MeshFilter>();
            foreach (var filter in filters)
            {
                var mat = filter.GetComponent<Renderer>().sharedMaterial;
                if (!groups.TryGetValue(mat, out var group))
                    groups[mat] = group = new System.Collections.Generic.List<CombineInstance>();
                group.Add(new CombineInstance { mesh = filter.sharedMesh,
                    transform = root.worldToLocalMatrix * filter.transform.localToWorldMatrix });
            }
            foreach (var group in groups)
            {
                var mesh = new Mesh { name = "Crane structure / " + group.Key.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(group.Value.ToArray());
                _meshes.Add(mesh);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = group.Key;
            }
            foreach (var filter in filters) Object.Destroy(filter.gameObject);
        }

        public void Dispose()
        {
            foreach (var mesh in _meshes) if (mesh != null) Object.Destroy(mesh);
            _meshes.Clear();
        }

        static void Paint(GameObject piece, Material mat)
        {
            foreach (var r in piece.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
        }

        // ------------------------------------------------------------ driving

        /// <summary>The frame opens: nobody has driven the crane yet.</summary>
        public void BeginFrame(float dt)
        {
            _dt = dt;
            _driven = false;
        }

        /// <summary>The frame closes: if no box wanted the hook, park the gear.</summary>
        public void EndFrame()
        {
            if (_driven) return;
            Drive(new Vector3(_x, BoomY - ParkClear, ParkZ), GantryCreep * 0.4f, TrolleyCreep * 0.5f, HoistCreep * 0.6f);
        }

        /// <summary>Where the spreader must stand for a box whose foot is at this point.</summary>
        Vector3 Want(Vector3 boxFoot) => new Vector3(
            Mathf.Clamp(boxFoot.x, _minX, _maxX),
            boxFoot.y + BoxTop,
            Mathf.Clamp(boxFoot.z, SeaTipZ + 2.5f, BackTipZ - 2.5f));

        /// <summary>Where the port stands in the world. The boxes are handed over in
        /// world coordinates (they are reparented between ship and yard as they fly),
        /// and the crane works in the port's own, so they come back through this.</summary>
        public RoadDemo.DistrictFrame Frame = RoadDemo.DistrictFrame.Identity;

        public bool Reach(Vector3 boxFoot)
        {
            _driven = true;
            return Drive(Want(Frame.ToLocal(boxFoot)), GantryCreep, TrolleyCreep, HoistCreep);
        }

        public void Carry(Vector3 boxFoot)
        {
            _driven = true;
            Drive(Want(Frame.ToLocal(boxFoot)), GantryRun, TrolleyRun, HoistRun);
        }

        public void Release() { }

        bool Drive(Vector3 want, float gantry, float trolley, float hoist)
        {
            _x = Mathf.MoveTowards(_x, want.x, gantry * _dt);
            _z = Mathf.MoveTowards(_z, want.z, trolley * _dt);
            _y = Mathf.MoveTowards(_y, want.y, hoist * _dt);
            Apply();
            return Mathf.Abs(_x - want.x) < 0.3f && Mathf.Abs(_z - want.z) < 0.3f && Mathf.Abs(_y - want.y) < 0.3f;
        }

        void Apply()
        {
            if (_gantry == null) return;
            var p = _gantry.localPosition;
            _gantry.localPosition = new Vector3(_x, p.y, p.z);
            if (_trolley != null) _trolley.localPosition = new Vector3(0f, TrolleyY, _z);
            float spreaderY = _y - _groundY + 0.3f;             // the beam sits just over the box
            if (_spreader != null) _spreader.localPosition = new Vector3(0f, spreaderY, _z);

            // the falls run from the trolley's underside down to the spreader's heads
            float drop = Mathf.Max(0.25f, TrolleyY - 0.6f - spreaderY - 1.05f);
            for (int i = 0; i < _falls.Length; i++)
            {
                var f = _falls[i];
                if (f == null) continue;
                f.localPosition = new Vector3(i < 2 ? -2.9f : 2.9f, -0.6f - drop * 0.5f,
                    i % 2 == 0 ? -1.12f : 1.12f);
                f.localScale = new Vector3(0.055f, drop, 0.055f);
            }
        }
    }
}
