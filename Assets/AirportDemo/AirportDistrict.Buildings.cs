using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace AirportDemo
{
    // Where the buildings stand and where the wire runs. Every building was baked
    // with its apron face on +Z, so every one of them goes down turned a half circle:
    // the hangar doors, the FBO's glass, the terminal's departure hall and the
    // freight shed's opening all look out at the ramp, and the terminal's landside
    // comes round to the kerb with the turn.
    //
    // The row is deliberately not a comb: the hangars are on a 27 m pitch but each is
    // jogged a metre or two off the line and one of them stands open, because a line
    // of identical sheds at identical spacing is what makes a place look laid out by
    // a machine (the harbour learnt this the hard way).
    public partial class AirportDistrict
    {
        /// <summary>Every building's footprint, for the walkers and the drivers to
        /// keep out of, and for the click-a-building card.</summary>
        readonly List<(string name, Bounds box)> _footprints = new List<(string, Bounds)>();

        void BuildBuildings()
        {
            PlaceHangars();
            PlaceOne(AirportKit.MaintHangar, AirportSpec.MaintHangarX, AirportSpec.MaintHangarDepth, "Maintenance hangar");
            PlaceOne(AirportKit.Fbo, AirportSpec.FboX, AirportSpec.FboDepth, "FBO");
            PlaceOne(AirportKit.Terminal, AirportSpec.TerminalX, AirportSpec.TerminalDepth, "Terminal");
            PlaceOne(AirportKit.Arff, AirportSpec.ArffX, AirportSpec.ArffDepth, "Fire station");
            PlaceOne(AirportKit.CargoShed, AirportSpec.CargoX, AirportSpec.CargoDepth, "Air freight");
            PlaceTower();
            PlaceFuelFarm();
        }

        /// <summary>A baked building on the building line: its apron face on
        /// <see cref="AirportSpec.BuildingFrontZ"/>, turned to look at the ramp.</summary>
        GameObject PlaceOne(string path, float x, float depth, string name, float jog = 0f, float extraTurn = 0f)
        {
            var prefab = AirportKit.TryLoad(path);
            if (prefab == null) { Debug.LogWarning("[AirportDemo] not baked yet: " + path); return null; }
            var b = AirportKit.PrefabBounds(prefab);
            // the bake recentred the pivot on the footprint, so the front face is half
            // the depth ahead of it; after the half turn that face looks down -Z
            float centreZ = AirportSpec.BuildingFrontZ + jog + b.size.z * 0.5f;
            var go = AirportKit.Prop(prefab, new Vector3(x, AirportSpec.PaveY, centreZ), 180f + extraTurn, _buildingRoot, name);
            RememberFootprint(name, go);
            return go;
        }

        void RememberFootprint(string name, GameObject go)
        {
            if (go == null) return;
            var b = AirportKit.BoundsOf(go);
            _footprints.Add((name, b));
        }

        void PlaceHangars()
        {
            var closed = AirportKit.TryLoad(AirportKit.BoxHangar);
            var open = AirportKit.TryLoad(AirportKit.BoxHangarOpen);
            if (closed == null && open == null) return;
            // which of the row stand open - never the two ends, so the row still reads
            // as a row from either side
            var openSet = new HashSet<int>();
            while (openSet.Count < Mathf.Min(openHangars, AirportSpec.Hangars - 2))
                openSet.Add(1 + Rnd(AirportSpec.Hangars - 2));

            for (int i = 0; i < AirportSpec.Hangars; i++)
            {
                float x = AirportSpec.HangarRowX0 + i * AirportSpec.HangarPitch;
                bool isOpen = openSet.Contains(i) && open != null;
                // a metre or two of jog: hangars are put up one at a time, over years
                float jog = Mathf.Round(Rnd(-1.6f, 2.2f) * 2f) * 0.5f;
                var go = PlaceOne(isOpen ? AirportKit.BoxHangarOpen : AirportKit.BoxHangar,
                                  x, AirportSpec.HangarDepth, (isOpen ? "Hangar open " : "Hangar ") + (i + 1), jog);
                if (isOpen && go != null) _openHangars.Add(new Vector3(x, AirportSpec.PaveY, AirportSpec.BuildingFrontZ + jog + 9f));
            }
        }

        readonly List<Vector3> _openHangars = new List<Vector3>();

        /// <summary>The tower, set back behind the building line so it is seen against
        /// the sky rather than against the terminal, with the field's comms mast beside
        /// it and its own little car park.</summary>
        void PlaceTower()
        {
            var prefab = AirportKit.TryLoad(AirportKit.Tower);
            if (prefab != null)
            {
                var go = AirportKit.Prop(prefab, new Vector3(AirportSpec.TowerX, AirportSpec.PaveY, AirportSpec.TowerZ), 180f, _buildingRoot, "Control tower");
                RememberFootprint("Control tower", go);
            }
            // the mast: twenty-three metres of the police pack's antenna, which is the
            // one piece in any pack that reads as an airfield's radio mast
            var mast = AirportKit.TryLoad(AirportKit.Antenna);
            if (mast != null)
                AirportKit.Sit(mast, new Vector3(AirportSpec.TowerX + 22f, AirportSpec.PaveY, AirportSpec.TowerZ + 4f), 0f, _buildingRoot, "Comms mast");
        }

        void PlaceFuelFarm()
        {
            var prefab = AirportKit.TryLoad(AirportKit.FuelFarm);
            if (prefab == null) return;
            var go = AirportKit.Prop(prefab, new Vector3(AirportSpec.FuelFarmX, AirportSpec.PaveY, AirportSpec.FuelFarmZ), 180f, _buildingRoot, "Fuel farm");
            RememberFootprint("Fuel farm", go);
            var sign = AirportKit.TryLoad(AirportKit.SignFuel);
            if (sign != null)
                AirportKit.Sit(sign, new Vector3(AirportSpec.FuelFarmX - 12f, AirportSpec.PaveY, AirportSpec.FuelFarmZ - 8f), 180f, _buildingRoot, "Fuel sign");
        }

        // ------------------------------------------------------------ the wire

        /// <summary>The perimeter: the police pack's fence panel by the module all the
        /// way along the back of the field, the razor coil on top of it, the two gates
        /// with their booms and gatehouses, and the returns down each flank. The
        /// terminal's own back wall is the boundary where it stands, which is how a
        /// small field's airside line actually runs.</summary>
        void BuildFence()
        {
            var panel = AirportKit.TryLoad(AirportKit.FencePanel);
            var pillar = AirportKit.TryLoad(AirportKit.FencePillar);
            var wire = AirportKit.TryLoad(AirportKit.BarbedWire);
            if (panel == null) return;

            float z = AirportSpec.FenceZ;
            float termHalf = AirportSpec.TerminalWidth * 0.5f + 1f;
            var gaps = new List<(float x0, float x1)>
            {
                (AirportSpec.GaGateX - AirportSpec.GateHalf, AirportSpec.GaGateX + AirportSpec.GateHalf),
                (AirportSpec.CargoGateX - AirportSpec.GateHalf, AirportSpec.CargoGateX + AirportSpec.GateHalf),
                (AirportSpec.TerminalX - termHalf, AirportSpec.TerminalX + termHalf),
            };
            gaps.Sort((a, b) => a.x0.CompareTo(b.x0));

            float cursor = AirportSpec.FenceX0;
            foreach (var gap in gaps)
            {
                if (gap.x0 > cursor) FenceRun(panel, wire, new Vector3(cursor, 0f, z), new Vector3(gap.x0, 0f, z));
                cursor = gap.x1;
            }
            FenceRun(panel, wire, new Vector3(cursor, 0f, z), new Vector3(AirportSpec.FenceX1, 0f, z));

            // the two flanks, run south far enough to close the field in every shot
            FenceRun(panel, wire, new Vector3(AirportSpec.FenceX0, 0f, z), new Vector3(AirportSpec.FenceX0, 0f, AirportSpec.FenceSouthZ));
            FenceRun(panel, wire, new Vector3(AirportSpec.FenceX1, 0f, z), new Vector3(AirportSpec.FenceX1, 0f, AirportSpec.FenceSouthZ));
            // and the terminal's returns, tying the wire into the building's flanks
            FenceRun(panel, wire, new Vector3(AirportSpec.TerminalX - termHalf, 0f, z),
                     new Vector3(AirportSpec.TerminalX - termHalf, 0f, AirportSpec.BuildingFrontZ + AirportSpec.TerminalDepth));
            FenceRun(panel, wire, new Vector3(AirportSpec.TerminalX + termHalf, 0f, z),
                     new Vector3(AirportSpec.TerminalX + termHalf, 0f, AirportSpec.BuildingFrontZ + AirportSpec.TerminalDepth));

            if (pillar != null)
                for (float x = AirportSpec.FenceX0; x <= AirportSpec.FenceX1 + 0.1f; x += 12.5f)
                    AirportKit.Sit(pillar, new Vector3(x, AirportSpec.LandY, z), 0f, _fenceRoot, "Fence post");

            BuildGate(AirportSpec.GaGateX, "General aviation gate");
            BuildGate(AirportSpec.CargoGateX, "Freight gate");
        }

        void FenceRun(GameObject panel, GameObject wire, Vector3 a, Vector3 b)
        {
            if ((b - a).sqrMagnitude < 1f) return;
            AirportKit.LayRun(panel, a, b, _fenceRoot, "Fence");
            if (wire != null)
            {
                var wa = a + Vector3.up * (AirportSpec.FenceHeight - 0.55f);
                var wb = b + Vector3.up * (AirportSpec.FenceHeight - 0.55f);
                AirportKit.LayRun(wire, wa, wb, _fenceRoot, "Razor wire");
            }
            // BlockTheField measures the completed run after the airport root has
            // reached world space; publishing this local line here leaves a ghost wire.
        }

        /// <summary>A gate: the boom, the gatehouse beside it, the authorised-vehicles
        /// board and the hazard chevrons across the road. Kept permanently up - the
        /// demo's lorries and trucks drive through it and a closed boom would only be a
        /// thing for them to stop at forever.</summary>
        void BuildGate(float x, string name)
        {
            float z = AirportSpec.FenceZ;
            var boom = AirportKit.TryLoad(AirportKit.BoomGate);
            var booth = AirportKit.TryLoad(AirportKit.GuardBooth);
            var sign = AirportKit.TryLoad(AirportKit.SignAuthorized);

            if (booth != null)
                AirportKit.Prop(booth, new Vector3(x + AirportSpec.GateHalf + 3.2f, AirportSpec.PaveY, z + 2f), 180f, _fenceRoot, name + " gatehouse");
            if (boom != null)
            {
                // both leaves swung back against the fence, out of the roadway
                AirportKit.Sit(boom, new Vector3(x - AirportSpec.GateHalf, AirportSpec.PaveY, z), 270f, _fenceRoot, name + " boom");
                AirportKit.Sit(boom, new Vector3(x + AirportSpec.GateHalf, AirportSpec.PaveY, z), 90f, _fenceRoot, name + " boom");
            }
            if (sign != null)
                AirportKit.Sit(sign, new Vector3(x - AirportSpec.GateHalf - 2.5f, AirportSpec.PaveY, z + 3f), 180f, _fenceRoot, name + " sign");
            // the road through the gate is laid landside, by BuildApproachRoad, out of
            // the same street kit as everything else that a lorry drives on
        }
    }
}
