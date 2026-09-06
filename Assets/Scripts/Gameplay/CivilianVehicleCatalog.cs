namespace LivingCity.Gameplay
{
    /// <summary>Passenger cars in ambient traffic and parking. Services retain their
    /// existing bodies; crew inventory and police have their own catalogs.</summary>
    public static class CivilianVehicleCatalog
    {
        public const string Folder = "Assets/Sedan1987/Prefabs/";
        public static readonly string[] Models =
        {
            "01_Regent_Bellavere",
            "02_Kronen_K58",
            "03_Albion_Six",
            "Vahren_Drei",
            "04_Calder_Marivelle",
            "05_Monarch_Townline",
            "06_Bayside_Classic",
            "07_Hikari_DX",
            "14_Borough_Mica",
            "08_Bayside_Trail",
            "09_Bayside_Ranger",
            "10_Albion_Highland",
            "13_Calder_Voyager"
        };
        public static readonly string[] PassengerPaths =
            System.Array.ConvertAll(Models, model => Folder + model + ".prefab");

        static readonly string[] DisplayNames =
        {
            "REGENT BELLAVERE",
            "KRONEN K58",
            "ALBION SIX",
            "VAHREN DREI",
            "CALDER MARIVELLE",
            "MONARCH TOWNLINE",
            "BAYSIDE CLASSIC",
            "HIKARI DX",
            "SRBO",
            "BAYSIDE TRAIL",
            "BAYSIDE RANGER",
            "ALBION HIGHLAND",
            "CALDER VOYAGER"
        };

        // The armoured crew truck is sold by Don, outside the civilian traffic pool.
        public const string ArmouredModel = "12_Monarch_Bastion";
        public static readonly string[] ServicePaths =
        {
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Sedan_01_Preset_Taxi.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Vehicles/SM_Veh_Car_Taxi_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Sedan_01_Preset_Food.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Pickup_01_Preset_Construction.prefab",
        };

        public static bool IsAuthored(string nameOrPath) => PathFor(nameOrPath) != null;

        /// <summary>Resolve either the showroom label or imported prefab name to
        /// the same asset. Unity can name the imported object after its file.</summary>
        public static string PathFor(string nameOrPath)
        {
            var name = VehicleCatalog.BareName(nameOrPath);
            for (int i = 0; i < Models.Length; i++)
                if (name == Models[i] || name == DisplayNames[i]) return PathAt(i);
            if (name == ArmouredModel || name == "MONARCH BASTION")
                return Folder + ArmouredModel + ".prefab";
            return null;
        }

        public static string PathAt(int index) => Folder + Models[index % Models.Length] + ".prefab";
    }
}
