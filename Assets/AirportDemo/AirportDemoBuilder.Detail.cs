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
    public partial class AirportDemoBuilder
    {
        void BuildDetail()
        {
            BuildFuelIsland();
            BuildTieDownFurniture();
            BuildFreightYard();
            BuildShopYard();
            BuildFlagsAndSigns();
            BuildBirds();
        }

        /// <summary>The avgas island: two pumps under a small canopy on a raised kerb,
        /// with the bollards that stop somebody driving into them.</summary>
        void BuildFuelIsland()
        {
            var pump = AirportKit.TryLoad(AirportKit.GasPump);
            var pumpBase = AirportKit.TryLoad(AirportKit.GasPumpBase);
            var bollard = AirportKit.TryLoad(AirportKit.Bollard);
            var hose = AirportKit.TryLoad(AirportKit.HoseReel);
            float x = AirportSpec.FuelIslandX, z = AirportSpec.FuelIslandZ;

            FlatPlane("Fuel island", x - 7f, x + 7f, z - 2.5f, z + 2.5f, AirportSpec.PaveY + 0.14f, _concreteMat, 7f, _detailRoot);
            if (pumpBase != null) AirportKit.Sit(pumpBase, new Vector3(x, AirportSpec.PaveY + 0.14f, z), 180f, _detailRoot, "Pump base");
            if (pump != null)
            {
                AirportKit.Sit(pump, new Vector3(x - 2.6f, AirportSpec.PaveY + 0.14f, z), 180f, _detailRoot, "Avgas pump");
                AirportKit.Sit(pump, new Vector3(x + 2.6f, AirportSpec.PaveY + 0.14f, z), 0f, _detailRoot, "Jet A pump");
            }
            if (hose != null) AirportKit.Sit(hose, new Vector3(x + 5.5f, AirportSpec.PaveY + 0.14f, z - 1.2f), 180f, _detailRoot, "Hose reel");
            if (bollard != null)
                for (int i = -1; i <= 1; i += 2)
                    for (int j = -1; j <= 1; j += 2)
                        AirportKit.Sit(bollard, new Vector3(x + i * 6.4f, AirportSpec.PaveY + 0.14f, z + j * 1.9f), 0f, _detailRoot, "Bollard");
            var sign = AirportKit.TryLoad(AirportKit.SignFuel);
            if (sign != null) AirportKit.Sit(sign, new Vector3(x - 9f, AirportSpec.PaveY, z + 2f), 180f, _detailRoot, "Fuel sign");
            WalkObstacles.Block(x - 7f, x + 7f, z - 2.5f, z + 2.5f);
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
            WalkObstacles.Block(x - 14f, x + 18f, front - 4f, front + 3f);
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

        void BuildBirds()
        {
            var birds = AirportKit.TryLoad(AirportKit.FxBirds);
            if (birds == null) return;
            // well clear of the runway, which is the one thing an airfield's bird
            // control is for
            AirportKit.Prop(birds, new Vector3(-420f, 40f, -140f), 0f, _floraRoot, "Birds");
            AirportKit.Prop(birds, new Vector3(360f, 52f, 300f), 0f, _floraRoot, "Birds");
        }
    }
}
