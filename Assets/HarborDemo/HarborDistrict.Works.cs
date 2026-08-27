using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    // The works: the parts of a port that are not cargo and not buildings, and whose
    // absence is what made the yard read as a car park with containers on it.
    //
    //   - the harbourmaster's, at the far end of the quay, with a mast and a dish on
    //     it, because somebody has to be running the place;
    //   - the gate: a boom over each lane that lifts for a lorry and drops behind her,
    //     a weighbridge plate in the lane the lorries actually drive, a customs kiosk
    //     across the road from it, and a lay-by on the corridor's far flank with two
    //     lorries stood in it waiting their turn;
    //   - the tank farm in the widest of the back lots, bunded, piped and with the
    //     pump the machines are fuelled at.
    //
    // Everything here is measured off its own prefab and hung off the yard's own
    // levels (the fence line, the service road, the gate corridors), so it follows the
    // sheds wherever the kit puts them.
    public partial class HarborDistrict
    {
        Transform _worksRoot;
        readonly List<HarborBoom> _booms = new List<HarborBoom>();
        Vector3 _dieselPump;                 // where a machine is fuelled, if there is a pump
        bool _tankFarmRaised;

        Transform WorksRoot => _worksRoot ??= Root("Harbor Works");

        Material _warnMat, _paleMat;
        Material WarnMaterial() => _warnMat ??= Keep(HarborKit.Flat("Harbor Warning", new Color(0.72f, 0.16f, 0.13f), 0.2f));
        Material PaleMaterial() => _paleMat ??= Keep(HarborKit.Flat("Harbor Pale", new Color(0.86f, 0.85f, 0.8f), 0.2f));

        void BuildPortWorks()
        {
            BuildHarbourmaster();
            if (gateWorks) BuildGateWorks();
        }

        // ------------------------------------------------------------ the office

        /// <summary>The harbourmaster's: a hut on the quay at the end the gantries do not
        /// reach, its door to the water, a mast beside it and a dish and an aerial on the
        /// roof - a hut with an aerial on it is an office, and a hut without one is a
        /// store. Named, so the map can put a card on it.</summary>
        void BuildHarbourmaster()
        {
            if (_officeSpan == Vector2.zero) return;
            var shed = HarborKit.TryLoad(HarborKit.YardShed);
            if (shed == null) return;
            var sb = HarborKit.PrefabBounds(shed);
            if (_officeSpan.y - _officeSpan.x < sb.size.x + 1f) return;
            // in the middle of whatever gap it was given
            float x = (_officeSpan.x + _officeSpan.y - sb.size.x) * 0.5f;
            // its back to the yard and its door to the water, clear of the men's way
            // along the quay (QuayWalkZ) on one side and of the gantries' landward rail
            // on the other
            float front = LiveRowZ - 1f;

            var go = Instantiate(shed, Vector3.zero, Quaternion.Euler(0f, 180f, 0f), WorksRoot);
            go.name = "Harbourmaster's Office";
            HarborKit.StripBehaviours(go, keepAnimator: false);
            var b = HarborKit.BoundsOf(go);
            var p = go.transform.position;
            go.transform.position = new Vector3(p.x + (x - b.min.x), TileTop + ShedLift, p.z + (front - b.min.z));
            b = HarborKit.BoundsOf(go);
            _namedWorks.Add((go.transform, "Harbourmaster's Office"));

            // the aerial and the dish on the roof, the mast beside the door
            var aerial = HarborKit.TryLoad(HarborKit.Antenna);
            var dish = HarborKit.TryLoad(HarborKit.SatDish);
            var mast = HarborKit.TryLoad(HarborKit.Flagpole);
            if (aerial != null) HarborKit.Sit(aerial, new Vector3(b.center.x - 1.6f, b.max.y - 0.05f, b.center.z + 1f), 0f, WorksRoot, "Aerial");
            if (dish != null) HarborKit.Sit(dish, new Vector3(b.center.x + 1.6f, b.max.y - 0.05f, b.center.z), 200f, WorksRoot, "Dish");
            if (mast != null)
            {
                // the pack's beach flag is a man's height: a port's mast is three of him
                var flag = HarborKit.Sit(mast, new Vector3(b.max.x + 2.2f, TileTop, b.min.z + 1f), 90f, WorksRoot, "Flagpole");
                if (flag != null) flag.transform.localScale *= 2.6f;
            }
            // and the furniture of a place somebody works in
            var bin = HarborKit.TryLoad(HarborKit.YardBin);
            var bench = HarborKit.TryLoad(HarborKit.Workbench);
            if (bin != null) HarborKit.Sit(bin, new Vector3(b.min.x - 1.2f, TileTop, b.min.z + 1.4f), 15f, WorksRoot, "Bin");
            if (bench != null) HarborKit.Sit(bench, new Vector3(b.center.x + 4.5f, TileTop, b.max.z - 1f), -90f, WorksRoot, "Bench");
        }

        // ------------------------------------------------------------ the gate

        /// <summary>What a lorry actually meets at a port gate. A boom over each lane of
        /// both gate roads, just inside the wire; and at the west gate - the one the
        /// lorries come in at - the weighbridge in the inbound lane, the customs kiosk
        /// across the road from it, and the lay-by with two lorries stood in it waiting
        /// their turn.
        /// <para>All of it inside the gate CORRIDOR, the fourteen metres either side of
        /// each gate road that the shed line is forbidden - which is the only ground in
        /// the yard guaranteed to be free whatever the kit measured.</para></summary>
        void BuildGateWorks()
        {
            var camera = HarborKit.TryLoad(HarborKit.SecurityCamera);
            var pole = HarborKit.TryLoad(HarborKit.Powerpole);

            foreach (float gx in new[] { _gateWestX, _gateEastX })
            {
                // the booms: the inbound lane's post to the west of it, the outbound
                // lane's to the east, so neither arm sweeps over the other's lane. Under
                // the LIVE root, not the works root: the arm turns, and the perf pass
                // folds every renderer under a static root into one mesh - a boom built
                // there would be welded shut in the merged chunk on the first frame
                float bz = _fenceZ - 1.6f;
                var inbound = HarborBoom.Build(_liveRoot, new Vector3(gx - 2.5f, TileTop, bz), -1f, 5f,
                                               SteelMaterial(), WarnMaterial(), PaleMaterial());
                var outbound = HarborBoom.Build(_liveRoot, new Vector3(gx + 2.5f, TileTop, bz), 1f, 5f,
                                                SteelMaterial(), WarnMaterial(), PaleMaterial());
                if (inbound != null) _booms.Add(inbound);
                if (outbound != null) _booms.Add(outbound);

                // the camera on a pole, looking down on the mouth
                if (pole != null && camera != null)
                {
                    var post = HarborKit.Sit(pole, new Vector3(gx + 7.5f, TileTop, _fenceZ - 2.5f), 180f, WorksRoot, "GatePole");
                    if (post != null)
                    {
                        var pb = HarborKit.BoundsOf(post);
                        HarborKit.Prop(camera, new Vector3(gx + 7.2f, pb.max.y - 0.5f, _fenceZ - 2.9f), 200f, WorksRoot, "GateCamera");
                    }
                }
            }

            BuildCustomsPost(_gateWestX);
        }

        /// <summary>The weighbridge and the shed that reads it. The plate lies in the
        /// INBOUND lane, which is the lane every lorry in the port actually drives (see
        /// BuildTraffic) - a weighbridge nothing is ever driven over is a steel rectangle
        /// on a road. The kiosk stands across the road from it, on the flank the men's
        /// footway does not own: the footway takes the west side of every gate road
        /// (LayFootways).</summary>
        void BuildCustomsPost(float gx)
        {
            float y = TileTop;
            float z0 = YardLaneZ + 8f, z1 = z0 + 9f;
            if (z1 > _serviceRoadZ0 - 3f) return;      // no room between the two roads: no post

            // the plate: a steel deck in the lane, with a kerb rail down each side
            FlatPlane("Weighbridge", gx - 4.2f, gx - 0.8f, z0, z1, y + 0.05f, SteelMaterial(), 6f, WorksRoot);
            var pillar = HarborKit.TryLoad(HarborKit.GenBase + "SM_Bld_Base_Pillar_01.prefab");
            if (pillar != null)
                foreach (float side in new[] { -4.35f, -0.65f })
                    Paint(HarborKit.Span(pillar, new Vector3(gx + side, y + 0.02f, z0), new Vector3(gx + side, y + 0.02f, z1),
                                         0.1f, WorksRoot, "WeighbridgeRail"), WarnMaterial());

            // the kiosk beside it, its door to the road
            var shed = HarborKit.TryLoad(HarborKit.YardShed);
            if (shed != null)
            {
                var sb = HarborKit.PrefabBounds(shed);
                // turned a quarter, so what has to fit the strip between the road and
                // the corridor's edge is its DEPTH, not its width
                if (sb.size.z < GateLaneHalf - 6.5f)
                {
                    var go = Instantiate(shed, Vector3.zero, Quaternion.Euler(0f, -90f, 0f), WorksRoot);
                    go.name = "Customs Post";
                    HarborKit.StripBehaviours(go, keepAnimator: false);
                    var b = HarborKit.BoundsOf(go);
                    var p = go.transform.position;
                    go.transform.position = new Vector3(p.x + (gx + 6.5f - b.min.x), y + ShedLift, p.z + (z0 - b.min.z));
                    _namedWorks.Add((go.transform, "Customs Post"));
                    // where the officer stands: at his own door, and out at the plate
                    _customsDoor = new Vector3(gx + 7.5f, y, z0 - 1.6f);
                    _customsPost = new Vector3(gx - 0.2f, y, (z0 + z1) * 0.5f);
                }
            }

            // the boards and the blocks that funnel a lorry onto the plate
            var sign = HarborKit.TryLoad(HarborKit.DangerSign);
            var cone = HarborKit.TryLoad(HarborKit.Cone);
            var block = HarborKit.TryLoad(HarborKit.ConcreteBlock);
            if (sign != null)
            {
                var b = HarborKit.PrefabBounds(sign);
                float sy = b.size.y > 1.2f ? y - b.min.y : y + 1.5f - b.min.y - b.size.y * 0.5f;
                HarborKit.Prop(sign, new Vector3(gx + 5.4f, sy, z1 + 1.5f), 180f, WorksRoot, "WeighSign");
            }
            // cones funnelling a lorry onto the plate as she comes down the lane, and
            // blocks keeping her off the kiosk's corner
            if (cone != null)
                for (float cz = z1 + 1f; cz < z1 + 5.5f; cz += 1.6f)
                    HarborKit.Sit(cone, new Vector3(gx - 5.2f, y, cz), 0f, WorksRoot, "Cone");
            if (block != null)
                for (int k = 0; k < 2; k++)
                    HarborKit.Sit(block, new Vector3(gx + 5.6f, y, z1 + 3f + k * 1.6f), 0f, WorksRoot, "ConcreteBlock");

            // the lay-by on the west flank, clear of the footway, with two lorries in it
            float lx0 = gx - GateLaneHalf, lx1 = gx - 8f;
            FlatPlane("GateLayby", lx0, lx1, YardLaneZ + 6f, _serviceRoadZ0 - 3f, y + 0.012f,
                      AsphaltMaterial(), 10f, WorksRoot);
            var lorryBodies = HarborKit.LoadAll(HarborKit.Lorries, quiet: true);
            if (lorryBodies.Count > 0)
                for (int k = 0; k < 2; k++)
                {
                    var prefab = HarborKit.Pick(_rng, lorryBodies);
                    var go = HarborKit.Prop(prefab, Vector3.zero, HarborKit.Range(_rng, -2f, 2f), WorksRoot, "QueuedLorry");
                    HarborKit.StripBehaviours(go, keepAnimator: false);
                    var b = HarborKit.BoundsOf(go);
                    var p = go.transform.position;
                    go.transform.position = new Vector3(p.x + ((lx0 + lx1) * 0.5f - b.center.x), y - b.min.y + p.y,
                                                        p.z + (YardLaneZ + 10f + k * 10f - b.center.z));
                }
        }

        // ------------------------------------------------------------ the tank farm

        /// <summary>The bunkers: two storage tanks in a bunded compound with a pipe run
        /// between them and out to the road, a ladder up one of them, and the pump the
        /// forklifts and the lorries are fuelled at. Raised in the widest of the back
        /// lots, once to a port - a second tank farm is furniture.
        ///
        /// A tank is the gang pack's steel drum blown up to whatever the lot has room
        /// for - four to eight and a half metres across: a cylinder with two hoop rims,
        /// which is what a tank is from the far side of a yard, and there is no other
        /// cylinder in the project. The pipes are the same drum stretched thin, which
        /// gives them their flanges for nothing.</summary>
        void RaiseTankFarm(float x0, float x1, float z0, float z1)
        {
            if (_tankFarmRaised) return;
            var body = HarborKit.TryLoad(HarborKit.TankBody);
            if (body == null) return;
            _tankFarmRaised = true;
            var b = HarborKit.PrefabBounds(body);
            float y = TileTop;
            float across = Mathf.Max(0.05f, Mathf.Max(b.size.x, b.size.z)), tall = Mathf.Max(0.05f, b.size.y);

            float room = x1 - x0;
            int n = room > 20f ? 2 : 1;
            float dia = Mathf.Clamp((room - 4f) / n - 2.5f, 4f, 8.5f);
            float high = dia * 1.3f;
            float pitch = dia + 2.5f;
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;

            var tanks = new List<Vector3>();
            for (int k = 0; k < n; k++)
            {
                var at = new Vector3(cx + (k - (n - 1) * 0.5f) * pitch, y, cz);
                var go = HarborKit.Prop(body, Vector3.zero, HarborKit.Range(_rng, 0f, 90f), WorksRoot, "Tank");
                go.transform.localScale = new Vector3(dia / across, high / tall, dia / across);
                var scaled = Vector3.Scale(b.min, go.transform.localScale);
                go.transform.position = new Vector3(at.x, at.y - scaled.y, at.z);
                tanks.Add(at);
            }

            // the bund: a low wall round the compound, which is what makes it a tank
            // farm rather than two drums on a slab
            var pillar = HarborKit.TryLoad(HarborKit.GenBase + "SM_Bld_Base_Pillar_01.prefab");
            float bx0 = cx - (n * pitch) * 0.5f - 1.2f, bx1 = cx + (n * pitch) * 0.5f + 1.2f;
            float bz0 = cz - dia * 0.5f - 2.2f, bz1 = cz + dia * 0.5f + 2.2f;
            if (pillar != null)
            {
                void Wall(Vector3 a, Vector3 c) => Paint(HarborKit.Span(pillar, a, c, 0.42f, WorksRoot, "Bund"), PaleMaterial());
                Wall(new Vector3(bx0, y + 0.6f, bz0), new Vector3(bx1, y + 0.6f, bz0));
                Wall(new Vector3(bx0, y + 0.6f, bz1), new Vector3(bx1, y + 0.6f, bz1));
                Wall(new Vector3(bx0, y + 0.6f, bz0), new Vector3(bx0, y + 0.6f, bz1));
                Wall(new Vector3(bx1, y + 0.6f, bz0), new Vector3(bx1, y + 0.6f, bz1));
            }

            // the pipe run: tank to tank along the back, then out over the bund and down
            // the lot toward the road, on stools
            float py = y + 0.8f;
            var manifold = new Vector3(tanks[tanks.Count - 1].x, py, bz1 - 0.9f);
            foreach (var t in tanks)
                HarborKit.Span(body, new Vector3(t.x, py, t.z), new Vector3(t.x, py, manifold.z), 0.42f, WorksRoot, "Pipe");
            HarborKit.Span(body, new Vector3(tanks[0].x, py, manifold.z), manifold, 0.42f, WorksRoot, "Pipe");
            HarborKit.Span(body, manifold, new Vector3(manifold.x, py, z0 + 1.5f), 0.42f, WorksRoot, "Pipe");
            if (pillar != null)
                for (float sz = z0 + 3f; sz < manifold.z; sz += 4.5f)
                    Paint(HarborKit.Span(pillar, new Vector3(manifold.x, y, sz), new Vector3(manifold.x, py - 0.2f, sz),
                                         0.28f, WorksRoot, "PipeStool"), SteelMaterial());

            // the pump at the lot's mouth, where a machine can get at it, and the drums
            // and the board that go with it
            var pump = HarborKit.TryLoad(HarborKit.GasPump);
            if (pump != null)
            {
                _dieselPump = new Vector3(manifold.x + 3.5f, y, z0 + 2f);
                HarborKit.Sit(pump, _dieselPump, 180f, WorksRoot, "DieselPump");
            }
            var sign = HarborKit.TryLoad(HarborKit.DangerSign);
            if (sign != null)
            {
                var s = HarborKit.PrefabBounds(sign);
                float sy = s.size.y > 1.2f ? y - s.min.y : y + 1.5f - s.min.y - s.size.y * 0.5f;
                HarborKit.Prop(sign, new Vector3(bx0 + 1.5f, sy, bz0 - 0.4f), 180f, WorksRoot, "NoNakedLights");
            }
            var ladder = HarborKit.TryLoad(HarborKit.Ladder);
            if (ladder != null)
            {
                var lb = HarborKit.PrefabBounds(ladder);
                var rung = HarborKit.Prop(ladder, new Vector3(tanks[0].x, y, tanks[0].z - dia * 0.5f - 0.12f), 0f, WorksRoot, "TankLadder");
                var s = rung.transform.localScale;
                s.y *= high / Mathf.Max(0.5f, lb.size.y);
                rung.transform.localScale = s;
            }
            var steam = HarborKit.TryLoad(HarborKit.FxSteam);
            if (steam != null)
            {
                var go = Instantiate(steam, _liveRoot);
                go.name = "TankVent";
                go.transform.localPosition = new Vector3(tanks[tanks.Count - 1].x, y + high + 0.4f, tanks[tanks.Count - 1].z);
                go.transform.localScale = Vector3.one * 0.6f;
            }
        }
    }
}
