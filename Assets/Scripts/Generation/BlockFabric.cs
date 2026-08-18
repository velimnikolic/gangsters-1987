using System.Text.RegularExpressions;

namespace LivingCity.Generation
{
    /// <summary>
    /// Which buildings are the FABRIC of a city - the stuff any number of blocks may be
    /// made of - and which are PLACES, one to a city.
    ///
    /// The distinction is drawn by name, and both sides that need it read it from here:
    /// the roller that composes blocks (editor) and the road demo that lays them out
    /// (runtime). Two copies of the rule drifting apart is exactly what makes a block
    /// stand fine on the catalog pad and get passed over in the city.
    ///
    ///   * Anonymous terrace: a cluster the extractor found in the PolygonCity demo is
    ///     City_04, its buildings City_04_A..H. No one can tell two blocks of it apart.
    ///   * Commerce: the storefronts baked out of the PalmCity demo (cafe, burger joint,
    ///     restaurant, the stores) and the coffee shop kiosk. A city has more than one
    ///     cafe and more than one burger bar - they are the small commercial fill the
    ///     user asked for between the residential rows, and a block may not have the same
    ///     one twice, but the city may.
    ///
    /// Everything else the packs name is a place: the fairground, the hotel, the marina,
    /// the car yard, the police station, the palm tower. Two of those in one city reads
    /// as a mistake however far apart they stand.
    /// </summary>
    public static class BlockFabric
    {
        static readonly Regex Anonymous = new Regex(@"^City_\d+(_[A-Z]+)?$");

        /// <summary>The storefronts a rolled block fills its street frontage out with,
        /// by kit prefab name (Assets/CityKit/Buildings). Every one is baked with its
        /// front on +Z, so the roller can stand it facing the kerb.</summary>
        public static readonly string[] Commerce =
        {
            "building-cafe",            // PalmCity Restaurant_02
            "building-coffeeshop",      // the CoffeeShop pack's kiosk
            "building-burger-joint",    // PalmCity Diner
            "building-restaurant",      // PalmCity Restaurant_01
            "building-shop-china",      // PalmCity Store_03
            "building-store-01",        // PalmCity Store_01
            "building-post",            // PalmCity Store_02
        };

        public static bool IsAnonymous(string name) => name != null && Anonymous.IsMatch(name);

        public static bool IsCommerce(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < Commerce.Length; i++)
                if (Commerce[i] == name) return true;
            return false;
        }

        /// <summary>Whether a building of this name may stand in any number of blocks.</summary>
        public static bool IsFabric(string name) => IsAnonymous(name) || IsCommerce(name);
    }
}
