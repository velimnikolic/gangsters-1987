using RoadDemo;
using UnityEngine;

namespace AirportDemo
{
    // The drive in. An airport does not begin at its kerb: it begins a mile out, at
    // the filling station that sells the last tank before the long-term car park, the
    // roadhouse that feeds the men off the freight shift, the board that tells you how
    // far it still is, and the hire desk in the corner of the lot.
    //
    // Without any of that the approach road arrived out of nothing and stopped at a
    // terminal, which is the single loudest thing that said "this is a model of an
    // airport" rather than "this is a place". None of it is new art: the filling
    // station and the store are the Town pack's own dressed clusters, the same ones
    // the city stands beside every district road (RoadDemoBuilder.Wayside), so the
    // strip outside the wire reads as the same country the city is built out of.
    //
    // All of it stands on the ground the field ALREADY holds - the strip of grass
    // between the car park and the two ends of the approach road - so the context
    // costs the map nothing. That is deliberate: the whole point of the field's new
    // size is that it stops eating a shore, and buying atmosphere back with more
    // ground would have undone it.
    public partial class AirportDistrict
    {
        /// <summary>Where the roadside places stand, in the field's own frame: south of
        /// the approach road, west and east of the car park, clear of the two gate
        /// roads and of the terminal spur.</summary>
        const float StripZ0 = 364f, StripZ1 = 404f;
        const float GasX = -235f, DinerX = 300f;

        void BuildApproachStrip()
        {
            BuildRoadsideForecourts();
            StandCluster(SuburbDemo.TownClusters.GasStation, new Vector3(GasX, AirportSpec.PaveY, 388f), "Filling station");
            StandCluster(SuburbDemo.TownClusters.Shop, new Vector3(DinerX, AirportSpec.PaveY, 382f), "Roadhouse");
            BuildRoadsideSigns();
            BuildHireDesk();
        }

        /// <summary>The asphalt each roadside place stands on, run up to the approach
        /// road's own kerb strip so a forecourt meets the road rather than floating on
        /// the grass beside it.</summary>
        void BuildRoadsideForecourts()
        {
            float kerb = AirportSpec.StreetZ - StreetKit.OuterHalf;
            FlatPlane("Filling station forecourt", GasX - 46f, GasX + 42f, StripZ0, kerb, AirportSpec.PaveY, _asphaltMat, 12f, _landsideRoot);
            FlatPlane("Roadhouse forecourt", DinerX - 40f, DinerX + 40f, StripZ0 + 4f, kerb, AirportSpec.PaveY, _asphaltMat, 12f, _landsideRoot);
        }

        /// <summary>One of the Town pack's dressed clusters, stood with its front to the
        /// approach road. The anchor goes down first - the piece every offset in the
        /// cluster is measured from - and the rest follow in its frame.
        ///
        /// The y is pinned at the field's own paving level rather than left to
        /// <see cref="SuburbDemo.TownKit.GroundAt"/>: that hook belongs to whichever
        /// district set it last, and a suburb's heightfield still hanging off it would
        /// lift this forecourt onto a hillside that is nowhere near here.</summary>
        void StandCluster(SuburbDemo.TownClusters.Cluster cluster, Vector3 anchor, string name)
        {
            if (cluster == null) return;
            var rot = Quaternion.Euler(0f, SuburbDemo.TownKit.YawToFace(cluster.Front, Vector3.forward), 0f);
            int stood = 0;

            var anchorPrefab = SuburbDemo.TownKit.LoadByName(cluster.Anchor);
            if (anchorPrefab != null)
            {
                SuburbDemo.TownKit.Prop(anchorPrefab, anchor, rot, _landsideRoot, name, groundY: 0f);
                stood++;
            }
            foreach (var p in cluster.Pieces)
            {
                var prefab = SuburbDemo.TownKit.LoadByName(p.Name);
                if (prefab == null) continue;
                var go = SuburbDemo.TownKit.Prop(prefab, anchor + rot * new Vector3(p.X, p.Y, p.Z),
                                                 rot * p.Rot, _landsideRoot, name + " piece", groundY: 0f);
                // the same touch the suburb and the wayside give it: the pole sign is
                // stretched tall so it is read from the road and not from the forecourt
                if (p.Name == "SM_Prop_StreetSign_Pole_01")
                    go.transform.localScale = new Vector3(1.3f, 1.95f, 1.3f);
                if (p.Name.StartsWith("SM_Veh")) SuburbDemo.TownKit.StripForStatic(go);
                stood++;
            }
            if (stood == 0)
            {
                Debug.LogWarning("[Airport] the Town pack's " + cluster.Label + " is not where TownKit looks - the approach road stands bare.");
                return;
            }

            // a walker crossing the strip goes round the building and the forecourt kit
            WalkObstacles.Block(anchor.x - 12f, anchor.x + 12f, anchor.z - 20f, anchor.z + 4f);
            Debug.Log($"[Airport] {name} on the approach road, {stood} pieces");
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
                WalkObstacles.Block(x - 8f, x, z - 3f, z + 3f);
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
