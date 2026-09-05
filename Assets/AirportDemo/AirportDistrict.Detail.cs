using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace AirportDemo
{
    // The clutter that says the place is worked: the avgas island in front of the
    // FBO, the chocks and cones round the tie-downs, the pallets and the drums at the
    // freight shed, the ladders and the toolboxes at the shop door, the flag on its
    // pole by the terminal, the birds over the grass.
    //
    // All of it is on the concrete or against a wall - nothing is scattered over the
    // movement area, because everything loose on an airfield is either tied down or
    // somebody's job to remove.
    public partial class AirportDistrict
    {
        void BuildDetail()
        {
            BuildFuelIsland();
            BuildTieDownFurniture();
            BuildFreightYard();
            BuildShopYard();
            BuildStandFurniture();
            BuildFuelFarmYard();
            BuildBoneyard();
            BuildFenceScrub();
            BuildFlagsAndSigns();
            BuildBirds();
        }

        /// <summary>The aviation hose point; the fuel farm and bowser supply aircraft.
        /// Roadside gas pumps and their canopy belong to the public filling station.</summary>
        void BuildFuelIsland()
        {
            var bollard = AirportKit.TryLoad(AirportKit.Bollard);
            var hose = AirportKit.TryLoad(AirportKit.HoseReel);
            float x = AirportSpec.FuelIslandX, z = AirportSpec.FuelIslandZ;

            FlatPlane("Fuel island", x - 7f, x + 7f, z - 2.5f, z + 2.5f, AirportSpec.PaveY + 0.14f, _concreteMat, 7f, _detailRoot);
            if (hose != null) AirportKit.Sit(hose, new Vector3(x + 5.5f, AirportSpec.PaveY + 0.14f, z - 1.2f), 180f, _detailRoot, "Hose reel");
            if (bollard != null)
                for (int i = -1; i <= 1; i += 2)
                    for (int j = -1; j <= 1; j += 2)
                        AirportKit.Sit(bollard, new Vector3(x + i * 6.4f, AirportSpec.PaveY + 0.14f, z + j * 1.9f), 0f, _detailRoot, "Bollard");
            var sign = AirportKit.TryLoad(AirportKit.SignFuel);
            if (sign != null) AirportKit.Sit(sign, new Vector3(x - 9f, AirportSpec.PaveY, z + 2f), 180f, _detailRoot, "Fuel sign");
            BlockLocal(x - 7f, x + 7f, z - 2.5f, z + 2.5f);
        }

        /// <summary>Cones down the tie-down rows and a fire extinguisher stand or two -
        /// the small change of a general aviation ramp.</summary>
        void BuildTieDownFurniture()
        {
            var cone = AirportKit.TryLoad(AirportKit.Cone);
            var coneAlt = AirportKit.TryLoad(AirportKit.ConeAlt) ?? cone;
            if (cone == null) return;
            for (int row = 0; row < AirportSpec.TieDownRows; row++)
            {
                float z = AirportSpec.TieDownRowZ0 + row * AirportSpec.TieDownRowPitch - 13f;
                for (float x = AirportSpec.TieDownX0 - 6f; x <= AirportSpec.TieDownX1 + 6f; x += AirportSpec.TieDownPitch)
                {
                    if (!Chance(0.35f)) continue;
                    AirportKit.Sit(Chance(0.5f) ? cone : coneAlt,
                                   new Vector3(x + Rnd(-1.5f, 1.5f), AirportSpec.PaveY, z + Rnd(-1f, 1f)), Rnd(0f, 360f), _detailRoot, "Cone");
                }
            }
        }

        /// <summary>The freight yard: pallets on the dock, drums against the wall, a
        /// couple of containers standing about, which is what an air freight shed
        /// looks like at a county field.</summary>
        void BuildFreightYard()
        {
            var pallet = AirportKit.TryLoad(AirportKit.Pallet);
            var freight = AirportKit.LoadAll(AirportKit.Freight, quiet: true);
            var drum = AirportKit.TryLoad(AirportKit.BarrelMetal);
            float x = AirportSpec.CargoX, front = AirportSpec.BuildingFrontZ - 4f;

            for (int i = 0; i < 7; i++)
            {
                var at = new Vector3(x - 12f + i * 4f + Rnd(-0.8f, 0.8f), AirportSpec.PaveY, front + Rnd(-2f, 2f));
                if (pallet != null) AirportKit.Sit(pallet, at, Rnd(0f, 360f), _detailRoot, "Pallet");
                if (freight.Count > 0 && Chance(0.75f))
                    AirportKit.Sit(Pick(freight), at + new Vector3(0f, 0.16f, 0f), Rnd(0f, 360f), _detailRoot, "Freight");
            }
            if (drum != null)
                for (int i = 0; i < 6; i++)
                    AirportKit.Sit(drum, new Vector3(x + 15f + (i % 3) * 0.9f, AirportSpec.PaveY, front - 2f + (i / 3) * 0.9f),
                                   Rnd(0f, 360f), _detailRoot, "Drum");
            BlockLocal(x - 14f, x + 18f, front - 4f, front + 3f);
        }

        /// <summary>The maintenance shop's yard: ladders, boxes, a drum of something,
        /// and the aeroplane parts nobody has thrown away.</summary>
        void BuildShopYard()
        {
            var ladder = AirportKit.TryLoad(AirportKit.Ladder);
            var box = AirportKit.TryLoad(AirportKit.ToolBox);
            var drum = AirportKit.TryLoad(AirportKit.BarrelMetal);
            float x = AirportSpec.MaintHangarX, front = AirportSpec.BuildingFrontZ - 3f;
            if (ladder != null)
            {
                AirportKit.Sit(ladder, new Vector3(x - 20f, AirportSpec.PaveY, front), 170f, _detailRoot, "Ladder");
                AirportKit.Sit(ladder, new Vector3(x + 21f, AirportSpec.PaveY, front - 1f), 190f, _detailRoot, "Ladder");
            }
            if (box != null)
                for (int i = 0; i < 3; i++)
                    AirportKit.Sit(box, new Vector3(x - 16f + i * 1.4f, AirportSpec.PaveY, front - 1.4f), Rnd(0f, 360f), _detailRoot, "Tool box");
            if (drum != null)
                for (int i = 0; i < 4; i++)
                    AirportKit.Sit(drum, new Vector3(x + 15f + (i % 2) * 0.85f, AirportSpec.PaveY, front - 2.2f + (i / 2) * 0.85f),
                                   Rnd(0f, 360f), _detailRoot, "Drum");
        }

        /// <summary>The flag by the terminal, the security cameras on the fence and the
        /// board on the approach - the signs a place is run by somebody.</summary>
        void BuildFlagsAndSigns()
        {
            var flag = AirportKit.TryLoad(AirportKit.FlagStand);
            float doorZ = AirportSpec.BuildingFrontZ + AirportSpec.TerminalDepth + 3f;
            if (flag != null)
            {
                AirportKit.Sit(flag, new Vector3(-14f, AirportSpec.PaveY + 0.14f, doorZ), 180f, _landsideRoot, "Flag");
                AirportKit.Sit(flag, new Vector3(14f, AirportSpec.PaveY + 0.14f, doorZ), 180f, _landsideRoot, "Flag");
            }
            var camera = AirportKit.TryLoad(AirportKit.SecurityCamera);
            if (camera != null)
                for (float x = AirportSpec.FenceX0 + 60f; x < AirportSpec.FenceX1; x += 140f)
                    AirportKit.Prop(camera, new Vector3(x, AirportSpec.FenceHeight - 0.2f, AirportSpec.FenceZ), 180f, _fenceRoot, "Camera");

            // and the boards on the ramp side of the fence that say what is beyond it
            var noEntry = AirportKit.TryLoad(AirportKit.SignNoEntry);
            if (noEntry != null)
                for (float x = AirportSpec.FenceX0 + 100f; x < AirportSpec.FenceX1; x += 200f)
                    AirportKit.Sit(noEntry, new Vector3(x, AirportSpec.PaveY, AirportSpec.FenceZ - 1.2f), 0f, _fenceRoot, "Sign");
        }

        // ------------------------------------------------------------ the stands

        /// <summary>The stand furniture that is there whether an aeroplane is or not:
        /// the chocks stacked at the stand line, the cones that mark the engine arcs,
        /// and - at the stands the schedule is not using - a set of steps and a string
        /// of baggage carts left where the last turnaround finished with them.
        ///
        /// This is the difference between a stand and a rectangle of concrete. The
        /// ground equipment the demo DRIVES (GroundOps) is out of sight most of the
        /// time; what stands still on a ramp is what makes it read as a ramp.</summary>
        void BuildStandFurniture()
        {
            var cone = AirportKit.TryLoad(AirportKit.Cone);
            var coneAlt = AirportKit.TryLoad(AirportKit.ConeAlt) ?? cone;
            var chock = AirportKit.TryLoad(AirportKit.Chock);
            var steps = AirportKit.TryLoad(AirportKit.AirStairs);
            var cart = AirportKit.TryLoad(AirportKit.BaggageCart);
            var belt = AirportKit.TryLoad(AirportKit.BeltBarrier);

            for (int i = 0; i < AirportSpec.CommuterStandX.Length; i++)
            {
                float sx = AirportSpec.CommuterStandX[i];
                float stop = AirportSpec.CommuterStandZ;
                // the cones that keep a tug off a wingtip, at the stand's own corners
                if (cone != null)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        AirportKit.Sit(cone, new Vector3(sx + s * 9f, AirportSpec.PaveY, stop - 13.5f), Rnd(0f, 360f), _detailRoot, "Cone");
                        AirportKit.Sit(coneAlt, new Vector3(sx + s * 9f, AirportSpec.PaveY, stop - 1.5f), Rnd(0f, 360f), _detailRoot, "Cone");
                    }
                // chocks stacked at the stand line, where the lineman leaves them
                if (chock != null)
                    for (int k = 0; k < 2; k++)
                        AirportKit.Sit(chock, new Vector3(sx + 11f + k * 0.9f, AirportSpec.PaveY, stop - 6f + Rnd(-0.6f, 0.6f)),
                                       Rnd(0f, 360f), _detailRoot, "Chocks");

                // the stands the schedule is not working keep last night's kit
                if (i % 2 != 0) continue;
                if (steps != null && Chance(0.75f))
                    AirportKit.Sit(steps, new Vector3(sx + 13f, AirportSpec.PaveY, stop - 10f), 250f + Rnd(-12f, 12f), _detailRoot, "Spare steps");
                if (cart != null)
                    for (int k = 0; k < 3; k++)
                        AirportKit.Sit(cart, new Vector3(sx - 13f, AirportSpec.PaveY, stop - 4f - k * 3.4f), 182f + Rnd(-3f, 3f), _detailRoot, "Baggage cart");
            }

            // the belt barrier the passengers are walked between, stacked against the
            // terminal wall where the gate door is
            if (belt != null)
                for (int k = 0; k < 4; k++)
                    AirportKit.Sit(belt, new Vector3(-34f + k * 1.1f, AirportSpec.PaveY, AirportSpec.BuildingFrontZ - 3.2f),
                                   90f, _detailRoot, "Barrier");
        }

        // ------------------------------------------------------------ the fuel farm

        /// <summary>The tank farm's yard: the bund of bollards and chain that keeps a
        /// lorry off the tanks, the drums stacked against it, the danger boards, and the
        /// spill kit. A fuel farm with nothing round it reads as two tanks in a field.</summary>
        void BuildFuelFarmYard()
        {
            var bollard = AirportKit.TryLoad(AirportKit.BollardChain) ?? AirportKit.TryLoad(AirportKit.Bollard);
            var drum = AirportKit.TryLoad(AirportKit.BarrelMetal);
            var danger = AirportKit.TryLoad(AirportKit.DangerSign);
            var tank = AirportKit.TryLoad(AirportKit.LabTank);
            float x = AirportSpec.FuelFarmX, z = AirportSpec.FuelFarmZ;

            if (bollard != null)
            {
                for (float bx = x - 15f; bx <= x + 15.1f; bx += 3.75f)
                    AirportKit.Sit(bollard, new Vector3(bx, AirportSpec.PaveY, z - 11f), 90f, _detailRoot, "Bollard");
                for (float bz = z - 11f; bz <= z + 4.1f; bz += 3.75f)
                {
                    AirportKit.Sit(bollard, new Vector3(x - 15f, AirportSpec.PaveY, bz), 0f, _detailRoot, "Bollard");
                    AirportKit.Sit(bollard, new Vector3(x + 15f, AirportSpec.PaveY, bz), 0f, _detailRoot, "Bollard");
                }
            }
            if (drum != null)
                for (int i = 0; i < 9; i++)
                    AirportKit.Sit(drum, new Vector3(x - 13f + (i % 3) * 0.95f, AirportSpec.PaveY, z - 8f + (i / 3) * 0.95f),
                                   Rnd(0f, 360f), _detailRoot, "Drum");
            if (tank != null)
                AirportKit.Sit(tank, new Vector3(x + 11f, AirportSpec.PaveY, z - 7f), Rnd(0f, 360f), _detailRoot, "Waste tank");
            if (danger != null)
            {
                AirportKit.Sit(danger, new Vector3(x - 15f, AirportSpec.PaveY, z - 12f), 200f, _detailRoot, "Danger sign");
                AirportKit.Sit(danger, new Vector3(x + 15f, AirportSpec.PaveY, z - 12f), 160f, _detailRoot, "Danger sign");
            }
            BlockLocal(x - 16f, x + 16f, z - 12f, z + 6f);
        }

        // ------------------------------------------------------------ the boneyard

        /// <summary>The corner of the field nobody has tidied since about 1974: two
        /// aeroplanes standing on flat tyres in the grass behind the freight shed, and
        /// a stripped fuselage at the shop door that has been somebody's project for
        /// four years. It is the cheapest character on the field - three prefabs the
        /// demo already owns, put down at angles nothing airworthy would ever sit at -
        /// and it is the thing that most says this is a county airport and not a model
        /// of one.</summary>
        void BuildBoneyard()
        {
            LoadFleet();
            // the grass corner east of the fuel farm, inside the wire, off the yard road
            Derelict(_lightPrefabs, AirportSpec.GaSpan,
                     new Vector3(248f, AirportSpec.LandY, 262f), 24f, roll: 4.5f, pitch: -2.2f, "Derelict single");
            Derelict(_lightPrefabs, AirportSpec.GaSpan * 1.1f,
                     new Vector3(266f, AirportSpec.LandY, 280f), 112f, roll: -3f, pitch: 1.4f, "Derelict single");
            // and the project, parked in the gap between the hangar line and the
            // maintenance shop - clear of the shop's own yard kit and well north of the
            // tie-down rows, which is the only piece of ramp nobody else wants
            Derelict(_commuterPrefabs, AirportSpec.CommuterSpan,
                     new Vector3(-175f, AirportSpec.PaveY, AirportSpec.BuildingFrontZ - 13f),
                     195f, roll: 0f, pitch: 2.6f, "Stripped airframe");
        }

        /// <summary>One aeroplane that will not fly again: scaled to its class the way a
        /// live one is, then set down at an attitude no undercarriage holds - a flat
        /// tyre one side, a nose that has settled. Static, under the detail root, with
        /// none of the machinery a flying aeroplane carries.</summary>
        void Derelict(List<GameObject> bag, float span, Vector3 at,
                      float yaw, float roll, float pitch, string name)
        {
            if (bag == null || bag.Count == 0) return;
            var go = Instantiate(Pick(bag), _detailRoot);
            go.name = name;
            AirportKit.StripBehaviours(go, keepAnimator: false);

            // scale to the span its class flies at, measured square at the origin the
            // way Aircraft.Bind does - the pack's import scale is not this project's
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var raw = AirportKit.BoundsOf(go);
            float k = span / Mathf.Max(0.001f, raw.size.x);
            go.transform.localScale *= k;

            var b = AirportKit.BoundsOf(go);
            go.transform.SetPositionAndRotation(at + new Vector3(0f, -b.min.y - 0.15f, 0f),
                                                Quaternion.Euler(pitch, yaw, roll));
            AirportKit.SetLayerDeep(go, MidLayer);
            var world = AirportKit.BoundsOf(go);
            BlockLocal(world.min.x, world.max.x, world.min.z, world.max.z);
        }

        // ------------------------------------------------------------ the fence line

        /// <summary>Scrub outside the wire. Nothing is mown on the far side of an
        /// airfield fence, and a fence with cut grass on both sides of it reads as a
        /// garden. Kept OUTSIDE the boundary and off the flanks' safety areas, so
        /// nothing grows anywhere an aeroplane could reach.</summary>
        void BuildFenceScrub()
        {
            var bushes = AirportKit.LoadAll(AirportKit.Bushes, quiet: true);
            var trees = AirportKit.LoadAll(AirportKit.Trees, quiet: true);
            if (bushes.Count == 0) return;

            for (float x = AirportSpec.FenceX0; x < AirportSpec.FenceX1; x += Rnd(5f, 15f))
            {
                // not across the two gate roads
                if (Mathf.Abs(x - AirportSpec.GaGateX) < 14f || Mathf.Abs(x - AirportSpec.CargoGateX) < 14f) continue;
                if (Mathf.Abs(x - AirportSpec.TerminalX) < AirportSpec.TerminalWidth * 0.5f + 10f) continue;
                AirportKit.Sit(Pick(bushes), new Vector3(x + Rnd(-2f, 2f), AirportSpec.LandY, AirportSpec.FenceZ + Rnd(1.5f, 5f)),
                               Rnd(0f, 360f), _floraRoot, "Scrub");
                if (trees.Count > 0 && Chance(0.12f))
                    AirportKit.Sit(Pick(trees), new Vector3(x + Rnd(-4f, 4f), AirportSpec.LandY, AirportSpec.FenceZ + Rnd(6f, 12f)),
                                   Rnd(0f, 360f), _floraRoot, "Tree");
            }

            // and down the outside of each flank, clear of the runway safety area
            for (int s = 0; s < 2; s++)
            {
                float x = (s == 0 ? AirportSpec.FenceX0 : AirportSpec.FenceX1) + (s == 0 ? -4f : 4f);
                for (float z = AirportSpec.FenceSouthZ; z < AirportSpec.FenceZ - 20f; z += Rnd(9f, 26f))
                    AirportKit.Sit(Pick(bushes), new Vector3(x + Rnd(-2.5f, 2.5f), AirportSpec.LandY, z), Rnd(0f, 360f), _floraRoot, "Scrub");
            }
        }

        void BuildBirds()
        {
            var birds = AirportKit.TryLoad(AirportKit.FxBirds);
            if (birds == null) return;
            // well clear of the runway, which is the one thing an airfield's bird
            // control is for
            AirportKit.Prop(birds, new Vector3(-400f, 40f, -110f), 0f, _floraRoot, "Birds");
            AirportKit.Prop(birds, new Vector3(430f, 52f, 60f), 0f, _floraRoot, "Birds");
        }
    }
}
