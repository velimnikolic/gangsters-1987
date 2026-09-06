using System.Collections.Generic;
using LivingCity.Gameplay;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>The shared ambient car pool. Explicit asset paths prevent a pack
    /// import from silently adding old civilian bodies back into the game.</summary>
    public static class CivilianFleet
    {
        public static List<GameObject> Load(bool weighted = true, bool services = true)
        {
            var cars = new List<GameObject>();
            for (int i = 0; i < CivilianVehicleCatalog.Models.Length; i++)
                Add(cars, CivilianVehicleCatalog.PathAt(i), weighted);
            if (services)
                foreach (var path in CivilianVehicleCatalog.ServicePaths) Add(cars, path, weighted);
            return cars;
        }

        static void Add(List<GameObject> cars, string path, bool weighted)
        {
            var prefab = DemoAssetLoad.Load<GameObject>(path);
            if (!prefab)
            {
                Debug.LogError("[CivilianFleet] Missing required vehicle: " + path);
                return;
            }
            for (int i = 0, count = weighted ? VehicleCatalog.PoolWeight(path) : 1; i < count; i++)
                cars.Add(prefab);
        }
    }
}
