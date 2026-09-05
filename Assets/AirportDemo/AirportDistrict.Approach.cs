using RoadDemo;
using UnityEngine;

namespace AirportDemo
{
    // Shared fuel parcel, approach signs and the car-hire desk.
    public partial class AirportDistrict
    {
        /// <summary>Where the roadside places stand, in the field's own frame: south of
        /// the approach road, west and east of the car park, clear of the two gate
        /// roads and of the terminal spur.</summary>
        const float StripZ1 = 404f;
        const float GasX = -235f;
        static float GasAnchorZ => AirportSpec.StreetZ - AirportLandsidePlan.RoadHalf - FuelStationBlock.KerbZ;

        void BuildApproachStrip()
        {
            BuildPublicFuelStation();
            BuildRoadsideSigns();
            BuildHireDesk();
        }

        void BuildPublicFuelStation()
        {
            var root = new GameObject("Public fuel station (PumpDemo)").transform;
            root.gameObject.SetActive(false);
            // Compose the complete shared parcel at the origin, as CoreDemo does.
            // The airport's roadside station is scenery; it must not create the
            // residential preview's invisible traffic circuit in this district.
            FuelStationBlock.Compose(root, seed, createRuntime: false);
            root.SetParent(_landsideRoot, false);
            root.SetLocalPositionAndRotation(new Vector3(GasX,
                AirportSpec.PaveY - FuelStationBlock.RoadY, GasAnchorZ), Quaternion.Euler(0, 180, 0));
            root.gameObject.SetActive(true);
            BlockLocal(GasX - 8f, GasX + 8f, GasAnchorZ - 23f, GasAnchorZ - 5f);
            BlockLocal(GasX - 1.5f, GasX + 1.5f, GasAnchorZ - 4f, GasAnchorZ + 4f);
        }

        /// <summary>The boards on the drive in: the airport's own, and the two the
        /// roadside places bought. A billboard beside a road is the one prop that turns
        /// a verge into somewhere on the way to somewhere.</summary>
        void BuildRoadsideSigns()
        {
            var billboard = AirportKit.TryLoad(AirportKit.PlazaBillboard);
            float z = StripZ1 + 2f;
            if (billboard != null)
            {
                AirportKit.Sit(billboard, new Vector3(-172f, AirportSpec.PaveY, z), 180f, _landsideRoot, "Billboard");
                AirportKit.Sit(billboard, new Vector3(168f, AirportSpec.PaveY, z), 180f, _landsideRoot, "Billboard");
            }
            // and the board on the way OUT, on the far shoulder, so the drive reads the
            // same in both directions
            var planeSign = AirportKit.TryLoad(AirportKit.SignPlane);
            if (planeSign != null)
            {
                AirportKit.Sit(planeSign, new Vector3(-96f, AirportSpec.PaveY, AirportSpec.StreetZ - 13f), 180f, _landsideRoot, "Airport sign");
                AirportKit.Sit(planeSign, new Vector3(112f, AirportSpec.PaveY, AirportSpec.StreetZ - 13f), 180f, _landsideRoot, "Airport sign");
            }
        }

        /// <summary>The hire desk: a booth in the west corner of the car park with its
        /// own row of bays and a board over it. In 1987 that is exactly what a county
        /// field's car hire was - a hut, a phone and eight cars in a corner of the
        /// long-term lot.</summary>
        void BuildHireDesk()
        {
            float x = AirportSpec.ParkX0 - 6f;
            float z = (AirportSpec.ParkZ0 + AirportSpec.ParkZ1) * 0.5f;

            var booth = AirportKit.TryLoad(AirportKit.GuardBooth);
            if (booth != null)
            {
                AirportKit.Prop(booth, new Vector3(x - 4f, AirportSpec.PaveY, z), 90f, _landsideRoot, "Hire desk");
                BlockLocal(x - 8f, x, z - 3f, z + 3f);
            }
            var sign = AirportKit.TryLoad(AirportKit.SignInfo) ?? AirportKit.TryLoad(AirportKit.SignParking);
            if (sign != null) AirportKit.Sit(sign, new Vector3(x - 4f, AirportSpec.PaveY, z + 6f), 90f, _landsideRoot, "Hire sign");

            var lamp = AirportKit.TryLoad(AirportKit.StreetLamp);
            if (lamp != null)
                AirportKit.Sit(lamp, new Vector3(x - 2f, AirportSpec.PaveY, z - 8f), 90f, _landsideRoot, "Hire lamp");

            // the hut's own strip of tarmac, so it is not a booth standing in the grass
            FlatPlane("Hire forecourt", x - 9f, AirportSpec.ParkX0, z - 12f, z + 12f, AirportSpec.PaveY, _asphaltMat, 8f, _landsideRoot);
        }
    }
}
