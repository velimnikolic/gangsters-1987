using UnityEngine;

namespace RoadDemo
{
    /// <summary>Places ResidentialDemo's complete service blocks on the city's plan.</summary>
    public static class CoreServiceViews
    {
        public static void Build(CoreServicePlan plan, Transform quarter, IDistrictHost host, LaneNet net, int seed)
        {
            int index = 0;
            foreach (var site in plan.Sites)
            {
                var go = new GameObject($"Core {(site.Police ? "Precinct" : "Fire Station")} {++index:00}");
                go.SetActive(false);
                var root = go.transform;
                GameObject shell;
                var parked = new System.Collections.Generic.List<GameObject>();
                if (site.Police)
                    shell = PolicePrecinctBlock.Compose(root, seed + index * 7919).Shell;
                else
                {
                    var stood = FireStationBlock.ComposeBlock(root, seed + index * 7919);
                    shell = stood.Shell;
                    var live = go.AddComponent<FireStationBlockRuntime>();
                    live.Configure(seed + index * 7919, stood.Vehicles, stood.FireEngines, stood.BayDoors);
                    live.BindCityRoad(net);
                    for (int i = stood.FireEngines; i < stood.Vehicles.Count; i++) parked.Add(stood.Vehicles[i]);
                }
                foreach (var missing in Composer.Missing) host.ReportMissing(missing);

                var entry = site.Parcel.Entry;
                int yaw = entry == ParkingEntrySide.North ? 0 : entry == ParkingEntrySide.East ? 90
                        : entry == ParkingEntrySide.South ? 180 : 270;
                if (site.Police) yaw = (yaw + 270) % 360; // driveway is authored east
                var template = site.Police ? PolicePrecinctBlock.PreviewBounds : FireStationBlock.BlockBounds;
                var centre = Quaternion.Euler(0f, yaw, 0f) * new Vector3(template.center.x, 0f, template.center.y);
                root.SetParent(quarter, false);
                root.localPosition = new Vector3(site.Parcel.Box.center.x, 0f, site.Parcel.Box.center.y) - centre;
                root.localRotation = Quaternion.Euler(0f, yaw, 0f);
                // Engines and their doors must remain outside the host's static merge.
                root.SetParent(host.LiveRoot(go.name), true);
                go.SetActive(true);
                foreach (var car in parked)
                    if (car != null) host.Blocked(FireStationBlock.BoundsOf(car));
                if (shell != null)
                    host.Blocked(FireStationBlock.BoundsOf(shell),
                        site.Police ? "building-policestation-compact" : CoreDistrict.FireStationName);
            }
        }
    }
}
