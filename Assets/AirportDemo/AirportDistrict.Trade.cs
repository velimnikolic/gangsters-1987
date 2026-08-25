using UnityEngine;

namespace AirportDemo
{
    // What the field is FOR, as far as the outfit is concerned. A county airport is not
    // scenery in a game about 1987: it is the reason a shipment that landed in Florida
    // in the morning is in the city by night, and the reason a man with a pilot's
    // licence is worth keeping on the books.
    //
    // Two things live here. The night run itself (AirportFreight), which is a van, a
    // sedan and a stack of bags telling the story in movement; and the PLACES - the
    // freight dock, the two gates, the tie-down corner - published in world
    // coordinates so anything else in the game can be laid against them without
    // knowing a thing about how the field is built or which shore it landed on.
    //
    // The places are the important half. The field is built in its own frame and
    // carried onto a shore, so its own numbers mean nothing outside it; a mission that
    // wants "the freight dock" has to be handed a point, and this is where that point
    // comes from. Everything below reads AFTER Build - W() needs the frame the roots
    // were moved into.
    public partial class AirportDistrict
    {
        AirportFreight _freight;
        GameObject _lawSedan;

        // ------------------------------------------------------------ the places

        /// <summary>The freight shed's dock, where a lorry backs onto it: the one door
        /// on the field that a crate goes through in daylight with paperwork.</summary>
        public Vector3 FreightDock => W(new Vector3(AirportSpec.CargoX, AirportSpec.PaveY, AirportSpec.BuildingFrontZ - 5f));

        /// <summary>The wire gate by the hangars - the way onto the ramp that is not
        /// watched, which is why the law parks where it can see it.</summary>
        public Vector3 GeneralAviationGate => W(new Vector3(AirportSpec.GaGateX, AirportSpec.PaveY, AirportSpec.FenceZ));

        /// <summary>The wire gate by the shed, which is watched, boomed and logged.</summary>
        public Vector3 FreightGate => W(new Vector3(AirportSpec.CargoGateX, AirportSpec.PaveY, AirportSpec.FenceZ));

        /// <summary>The fixed base operator's door: where a man charters an aeroplane,
        /// buys fuel, and is not asked what is in the back of it.</summary>
        public Vector3 FboDoor => W(new Vector3(AirportSpec.FboX, AirportSpec.PaveY, AirportSpec.BuildingFrontZ - 3f));

        /// <summary>The terminal's landside door, off the kerb.</summary>
        public Vector3 TerminalDoor =>
            W(new Vector3(AirportSpec.TerminalX, AirportSpec.PaveY + 0.14f,
                          AirportSpec.BuildingFrontZ + AirportSpec.TerminalDepth + 2f));

        /// <summary>The tank farm, which is a bomb somebody else has already built.</summary>
        public Vector3 FuelFarm => W(new Vector3(AirportSpec.FuelFarmX, AirportSpec.PaveY, AirportSpec.FuelFarmZ - 12f));

        /// <summary>The corner of the ramp the night run uses.</summary>
        public Vector3 NightTransfer => W(AirportFreight.TransferPoint);

        // ------------------------------------------------------------ the night run

        /// <summary>The van, the bags and the tail. Built after the landside traffic,
        /// because the sedan it sends after the van is the law's own car and there is
        /// no sense in standing a second one beside it.</summary>
        void BuildNightFreight()
        {
            if (!nightFreight) return;

            var vanPrefab = AirportKit.TryLoad(AirportKit.NightVan);
            if (vanPrefab == null)
            {
                Debug.LogWarning("[Airport] no van in the pack - the night run has nothing to run in.");
                return;
            }
            var van = Vehicle(vanPrefab, "Night van");

            // the bags: what comes off the aeroplane and goes in the van, on the
            // concrete only while the transfer is happening
            var bags = new System.Collections.Generic.List<GameObject>();
            var luggage = AirportKit.LoadAll(AirportKit.Luggage, quiet: true);
            var at = AirportFreight.TransferPoint;
            for (int i = 0; i < 5 && luggage.Count > 0; i++)
            {
                var go = AirportKit.Sit(Pick(luggage),
                                        at + new Vector3(Rnd(-2.4f, 2.4f), 0f, Rnd(2.6f, 5.4f)),
                                        Rnd(0f, 360f), _liveRoot, "Freight bag");
                if (go == null) continue;
                AirportKit.StripBehaviours(go, keepAnimator: false);
                AirportKit.SetLayerDeep(go, PropLayer);
                bags.Add(go);
            }

            _freight = new AirportFreight();
            _freight.Build(van, _lawSedan, bags, Object.FindAnyObjectByType<RoadDemo.DemoClock>());
            Debug.Log("[Airport] the night run is on the field" + (_lawSedan != null ? ", and the law is watching it" : ""));
        }
    }
}
