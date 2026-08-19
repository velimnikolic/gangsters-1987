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
        public const float BoomY = 26f;
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
        const float BoxTop = 3.2f;

        const float GantryCreep = 7f, TrolleyCreep = 11f, HoistCreep = 7f;
        const float GantryRun = 12f, TrolleyRun = 18f, HoistRun = 12f;

        // ------------------------------------------------------------ state

        Transform _gantry, _trolley, _spreader;
        readonly Transform[] _falls = new Transform[4];
        float _fallRest = 3f;                 // the rope prefab's own length
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

        static Material _paint, _steel, _dark, _cab;

        static void LoadPaint()
        {
            if (_paint != null) return;
            _paint = HarborKit.Flat("CranePaint", new Color(0.72f, 0.31f, 0.13f), 0.25f);
            _steel = HarborKit.Flat("CraneSteel", new Color(0.44f, 0.46f, 0.49f), 0.35f);
            _dark = HarborKit.Flat("CraneRail", new Color(0.17f, 0.17f, 0.18f), 0.2f);
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

            // four legs on the two rails: bogies at their feet, a sill and a tie across
            foreach (float lz in new[] { SeaRailZ, LandRailZ })
            {
                foreach (float lx in new[] { -LegHalfX, LegHalfX })
                {
                    Member(root, pillar, new Vector3(lx, BoomY * 0.5f, lz), new Vector3(1.1f, BoomY, 1.1f), _paint, "Leg");
                    Member(root, pillar, new Vector3(lx, 0.5f, lz), new Vector3(1.7f, 1f, 2.6f), _steel, "Bogie");
                }
                Member(root, pillar, new Vector3(0f, 2.6f, lz), new Vector3(LegHalfX * 2f, 0.8f, 0.8f), _paint, "Sill");
                Member(root, pillar, new Vector3(0f, TieY, lz), new Vector3(LegHalfX * 2f + 1.1f, 1.2f, 1.2f), _paint, "PortalTie");
                Strut(root, pillar, new Vector3(-LegHalfX, 3.4f, lz), new Vector3(LegHalfX, TieY - 2.5f, lz), 0.5f, _steel, "Brace");
            }
            // the boom: two girders with ties between them
            float boomMid = (SeaTipZ + BackTipZ) * 0.5f, boomLen = BackTipZ - SeaTipZ;
            foreach (float gx in new[] { -LegHalfX, LegHalfX })
                Member(root, pillar, new Vector3(gx, BoomY, boomMid), new Vector3(1.1f, 1.6f, boomLen), _paint, "Girder");
            for (float bz = SeaTipZ + 3f; bz < BackTipZ; bz += 6f)
                Member(root, pillar, new Vector3(0f, BoomY + 0.9f, bz), new Vector3(LegHalfX * 2f, 0.4f, 0.4f), _steel, "BoomTie");
            // the apex over the landward legs and the stays that carry the boom
            var apex = new Vector3(0f, BoomY + 10f, LandRailZ);
            Member(root, pillar, new Vector3(0f, BoomY + 5f, LandRailZ), new Vector3(1f, 10f, 1f), _paint, "Apex");
            Strut(root, pillar, apex, new Vector3(0f, BoomY + 0.9f, SeaTipZ + 2f), 0.45f, _steel, "ForeStay");
            Strut(root, pillar, apex, new Vector3(0f, BoomY + 0.9f, BackTipZ - 2f), 0.45f, _steel, "BackStay");
            // the machinery house, back over the yard between the legs
            Member(root, pillar, new Vector3(0f, BoomY + 2.6f, (LandRailZ + BackTipZ) * 0.5f),
                   new Vector3(LegHalfX * 2f, 3.6f, 8f), _cab, "MachineryHouse");

            // the trolley under the girders, with the driver's cab hung off its sea side
            var trolley = new GameObject("Trolley").transform;
            trolley.SetParent(root, false);
            trolley.localPosition = new Vector3(0f, TrolleyY, ParkZ);
            crane._trolley = trolley;
            crane._z = ParkZ;
            Member(trolley, pillar, Vector3.zero, new Vector3(4.2f, 1.2f, 4.6f), _steel, "TrolleyFrame");
            Member(trolley, pillar, new Vector3(0f, -1.7f, -2.8f), new Vector3(2f, 2.2f, 2.2f), _cab, "DriverCab");

            // the spreader: a container-sized frame that hangs under the trolley
            var spreader = new GameObject("Spreader").transform;
            spreader.SetParent(root, false);
            crane._spreader = spreader;
            crane._y = BoomY - ParkClear;
            Member(spreader, pillar, Vector3.zero, new Vector3(6.8f, 0.5f, 0.9f), _paint, "SpreaderBeam");
            foreach (float sz in new[] { -1.6f, 1.6f })
                Member(spreader, pillar, new Vector3(0f, -0.2f, sz), new Vector3(6.8f, 0.35f, 0.4f), _steel, "SpreaderRail");
            foreach (float sx in new[] { -3.1f, 3.1f })
                Member(spreader, pillar, new Vector3(sx, 0.7f, 0f), new Vector3(0.5f, 1.4f, 3.4f), _steel, "SpreaderHead");

            // the falls: four ropes turned on their heads so they hang, stretched to
            // the spreader every frame
            var rope = HarborKit.TryLoad(HarborKit.Rope1);
            for (int i = 0; i < 4; i++)
            {
                var at = new Vector3(i < 2 ? -1.8f : 1.8f, -0.6f, i % 2 == 0 ? -1.9f : 1.9f);
                Transform fall;
                if (rope != null)
                {
                    crane._fallRest = Mathf.Max(0.5f, HarborKit.PrefabBounds(rope).size.y);
                    var go = Object.Instantiate(rope, trolley);
                    go.name = "Fall";
                    fall = go.transform;
                }
                else
                {
                    // no rope in the pack: a hair-thin pillar does the same work
                    crane._fallRest = HarborKit.PrefabBounds(pillar).size.y;
                    fall = Member(trolley, pillar, Vector3.zero, new Vector3(0.12f, 1f, 0.12f), _dark, "Fall").transform;
                }
                fall.localPosition = at;
                fall.localRotation = Quaternion.Euler(180f, 0f, 0f);   // +Y down
                crane._falls[i] = fall;
            }

            crane.Apply();
            return crane;
        }

        /// <summary>One member: a Base pillar stretched into a box of this size with its
        /// centre where asked, painted, hung on the rig.</summary>
        static GameObject Member(Transform parent, GameObject pillar, Vector3 centre, Vector3 size, Material mat, string name)
        {
            var b = HarborKit.PrefabBounds(pillar);
            var go = Object.Instantiate(pillar, parent);
            go.name = name;
            var scale = new Vector3(size.x / Mathf.Max(0.01f, b.size.x),
                                    size.y / Mathf.Max(0.01f, b.size.y),
                                    size.z / Mathf.Max(0.01f, b.size.z));
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localPosition = centre - Vector3.Scale(b.center, scale);
            Paint(go, mat);
            return go;
        }

        /// <summary>A diagonal member from A to B - the portal braces and the boom stays.</summary>
        static void Strut(Transform parent, GameObject pillar, Vector3 a, Vector3 b, float thick, Material mat, string name)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 0.05f) return;
            var go = Member(parent, pillar, (a + b) * 0.5f, new Vector3(thick, len, thick), mat, name);
            var rot = Quaternion.FromToRotation(Vector3.up, d / len);
            var bounds = HarborKit.PrefabBounds(pillar);
            go.transform.localRotation = rot;
            go.transform.localPosition = (a + b) * 0.5f - rot * Vector3.Scale(bounds.center, go.transform.localScale);
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
            float drop = Mathf.Max(0.25f, TrolleyY - 0.6f - spreaderY - 1.4f);
            for (int i = 0; i < _falls.Length; i++)
            {
                var f = _falls[i];
                if (f == null) continue;
                var s = f.localScale;
                f.localScale = new Vector3(s.x, drop / _fallRest, s.z);
            }
        }
    }
}
