using System;
using System.Collections.Generic;
using System.Globalization;

namespace LivingCity.Business
{
    /// <summary>
    /// What kind of establishment a business is. The list covers the whole city inventory
    /// (Docs/business-inventory.md) - ordinary retail and services, the named eating and
    /// drinking trades, the leisure venues, and the industrial and port firms.
    ///
    /// Values are never persisted as numbers; the archetype is written into a save as its
    /// name, so entries may be added anywhere without breaking an existing city.
    /// </summary>
    public enum BusinessArchetypeId
    {
        // ---- generic ground-floor commerce: the weighted pool for unlabelled glass
        Grocer,
        Butcher,
        Baker,
        Barber,
        Tailor,
        Laundry,
        Pharmacy,
        Hardware,
        Bookshop,
        RecordShop,
        Florist,
        Newsstand,
        Cobbler,
        Locksmith,
        PawnShop,
        ElectricalShop,
        TravelAgent,
        BettingShop,

        // ---- the named trades: only an authored sign puts a business here
        Pub,
        Pizzeria,
        Cafe,
        Diner,
        Restaurant,

        // ---- leisure and hospitality
        Nightclub,
        Casino,
        Hotel,
        Gym,
        Fairground,

        // ---- forecourt and compound trades
        CarYard,
        FuelStation,

        // ---- industry and the water
        Factory,
        Works,
        Refinery,
        Warehouse,
        PortCompany,
    }

    /// <summary>
    /// The authored signage hints a site may carry. They are string constants and not an
    /// enum so that a provider, the catalogue and the population pass agree on one spelling
    /// and a typo fails loudly at the hint lookup instead of silently rolling a bakery onto
    /// a nightclub. An empty hint means "generic glass": the only case that rolls.
    /// </summary>
    public static class BusinessSignage
    {
        public const string None = "";
        public const string Pub = "pub";
        public const string Pizza = "pizza";
        public const string Cafe = "cafe";
        public const string Diner = "diner";
        public const string Restaurant = "restaurant";
        public const string Nightclub = "nightclub";
        public const string Casino = "casino";
        public const string Hotel = "hotel";
        public const string Gym = "gym";
        public const string Fairground = "fairground";
        public const string CarYard = "caryard";
        public const string Fuel = "fuel";
        public const string Factory = "factory";
        public const string Works = "works";
        public const string Refinery = "refinery";
        public const string Warehouse = "warehouse";
        public const string Port = "port";
    }

    /// <summary>One archetype's authored facts. No cash flow lives here: the turnover band
    /// is a size fact the economy tickets will read, not money anybody is paid.</summary>
    public sealed class BusinessArchetype
    {
        public BusinessArchetype(
            BusinessArchetypeId id,
            string category,
            string signage,
            int genericWeight,
            BusinessSiteSize[] sizes,
            int turnoverLow,
            int turnoverHigh,
            bool companyOwned,
            string[] nameTemplates)
        {
            Id = id;
            Category = category;
            Signage = signage ?? BusinessSignage.None;
            GenericWeight = Math.Max(0, genericWeight);
            AllowedSizes = sizes ?? Array.Empty<BusinessSiteSize>();
            TurnoverLow = turnoverLow;
            TurnoverHigh = Math.Max(turnoverLow, turnoverHigh);
            CompanyOwned = companyOwned;
            NameTemplates = nameTemplates ?? Array.Empty<string>();
        }

        public BusinessArchetypeId Id { get; }

        /// <summary>Which of the three existing overlay categories the popup should read it
        /// as: "commercial", "industrial" or "port". Kept as text so this catalogue does not
        /// have to depend on the Gameplay assembly's enum.</summary>
        public string Category { get; }

        /// <summary>The authored hint that PINS this archetype, or empty when only the
        /// weighted roll can reach it.</summary>
        public string Signage { get; }

        /// <summary>Weight in the unlabelled-storefront roll. Zero means a site can only
        /// become this through an authored sign - a nightclub is never rolled.</summary>
        public int GenericWeight { get; }

        public IReadOnlyList<BusinessSiteSize> AllowedSizes { get; }
        public int TurnoverLow { get; }
        public int TurnoverHigh { get; }

        /// <summary>A firm rather than a proprietor: the works, the harbour, the big yards.
        /// Read by the owner generator, which gives those a company deed.</summary>
        public bool CompanyOwned { get; }

        public IReadOnlyList<string> NameTemplates { get; }

        public bool Accepts(BusinessSiteSize size)
        {
            for (var i = 0; i < AllowedSizes.Count; i++)
                if (AllowedSizes[i] == size)
                    return true;
            return false;
        }
    }

    /// <summary>
    /// The city's business archetypes as authored data - WeaponCatalog's pattern: a static
    /// table with explicit entries, read by everything and rolled by nothing but the
    /// population pass.
    ///
    /// Three rules decide what a site becomes, in this order:
    ///   1. explicit physical purpose wins - a nightclub block stays a nightclub;
    ///   2. an explicit sign (PUB, PIZZA) constrains the type;
    ///   3. only unlabelled glass takes the weighted roll.
    /// A runtime GameObject name is never consulted: the hint comes off the plan.
    /// </summary>
    public static class BusinessArchetypes
    {
        static readonly BusinessSiteSize[] SmallMedium =
            { BusinessSiteSize.Small, BusinessSiteSize.Medium };
        static readonly BusinessSiteSize[] MediumLarge =
            { BusinessSiteSize.Medium, BusinessSiteSize.Large };
        static readonly BusinessSiteSize[] AnyBuilt =
        {
            BusinessSiteSize.Small, BusinessSiteSize.Medium, BusinessSiteSize.Large,
        };
        static readonly BusinessSiteSize[] LargeCompound =
            { BusinessSiteSize.Large, BusinessSiteSize.Compound };

        static readonly BusinessArchetype[] Entries =
        {
            // ---------------------------------------------------- generic ground floor
            // Weights read as "shops per hundred": the trades a 1987 high street actually
            // repeats stand highest, the specialists lowest.
            Retail(BusinessArchetypeId.Grocer, 14, 700, 2400,
                "{0} Grocery", "{0} & Sons Grocers", "Corner Market", "{0}'s Provisions"),
            Retail(BusinessArchetypeId.Butcher, 8, 800, 2600,
                "{0} Butchers", "{0}'s Meats", "The Family Butcher"),
            Retail(BusinessArchetypeId.Baker, 8, 600, 2100,
                "{0} Bakery", "{0}'s Bread", "Sunrise Bakery"),
            Retail(BusinessArchetypeId.Barber, 9, 400, 1200,
                "{0}'s Barbershop", "The Chair", "{0} Barbers"),
            Retail(BusinessArchetypeId.Tailor, 6, 500, 1800,
                "{0} Tailoring", "{0} & Co. Tailors", "The Cutting Room"),
            Retail(BusinessArchetypeId.Laundry, 7, 450, 1500,
                "{0} Laundry", "Bright Wash", "{0}'s Cleaners"),
            Retail(BusinessArchetypeId.Pharmacy, 6, 900, 3000,
                "{0} Pharmacy", "City Drugs", "{0}'s Chemist"),
            Retail(BusinessArchetypeId.Hardware, 7, 800, 2800,
                "{0} Hardware", "{0} & Son Ironmongers", "Tools & Trade"),
            Retail(BusinessArchetypeId.Bookshop, 4, 350, 1300,
                "{0} Books", "The Reading Room", "{0}'s Bookshop"),
            Retail(BusinessArchetypeId.RecordShop, 5, 500, 1900,
                "{0} Records", "Vinyl Row", "Sound City Records"),
            Retail(BusinessArchetypeId.Florist, 4, 300, 1100,
                "{0} Florist", "The Flower Stall", "Blossom & Co."),
            Retail(BusinessArchetypeId.Newsstand, 5, 250, 900,
                "{0}'s Newsstand", "City News", "The Kiosk"),
            Retail(BusinessArchetypeId.Cobbler, 3, 300, 1000,
                "{0} Shoe Repair", "The Cobbler", "{0}'s Shoes"),
            Retail(BusinessArchetypeId.Locksmith, 3, 350, 1200,
                "{0} Locksmith", "City Keys", "{0} & Bolt"),
            Retail(BusinessArchetypeId.PawnShop, 4, 900, 3400,
                "{0}'s Pawnbrokers", "Three Balls Pawn", "Cash Counter"),
            Retail(BusinessArchetypeId.ElectricalShop, 5, 900, 3200,
                "{0} Electrical", "Tube & Wire", "{0}'s TV & Radio"),
            Retail(BusinessArchetypeId.TravelAgent, 3, 700, 2500,
                "{0} Travel", "Blue Horizon Travel", "{0}'s Tours"),
            Retail(BusinessArchetypeId.BettingShop, 5, 1200, 4200,
                "{0}'s Turf Accountants", "The Form Book", "Lucky Line Betting"),

            // ---------------------------------------------------- the named trades
            new BusinessArchetype(BusinessArchetypeId.Pub, "commercial", BusinessSignage.Pub,
                0, SmallMedium, 1400, 4200, false,
                new[] { "The {0} Arms", "{0}'s Tavern", "The {0} Anchor", "The {0} Bell",
                        "{0}'s Public House", "The {0} Crown" }),
            new BusinessArchetype(BusinessArchetypeId.Pizzeria, "commercial",
                BusinessSignage.Pizza, 0, SmallMedium, 1200, 3800, false,
                new[] { "Pizzeria {0}", "{0}'s Pizza", "{0} Slice House", "Vesuvio {0}",
                        "{0} Pizza Parlour" }),
            new BusinessArchetype(BusinessArchetypeId.Cafe, "commercial", BusinessSignage.Cafe,
                0, SmallMedium, 800, 2600, false,
                new[] { "Café {0}", "{0}'s Coffee House", "Café Bella {0}", "{0} Espresso Bar",
                        "The {0} Perk", "Caffè {0}", "{0}'s Coffee Counter" }),
            new BusinessArchetype(BusinessArchetypeId.Diner, "commercial", BusinessSignage.Diner,
                0, SmallMedium, 1300, 3900, false,
                new[] { "{0}'s Diner", "Blue Moon {0}", "The {0} Counter", "{0} Route Diner",
                        "{0}'s Grill" }),
            new BusinessArchetype(BusinessArchetypeId.Restaurant, "commercial",
                BusinessSignage.Restaurant, 0, SmallMedium, 1800, 5600, false,
                new[] { "Trattoria {0}", "{0}'s Restaurant", "The {0} Fork", "Little {0}",
                        "{0} Supper Club" }),

            // ---------------------------------------------------- leisure, hospitality
            new BusinessArchetype(BusinessArchetypeId.Nightclub, "commercial",
                BusinessSignage.Nightclub, 0, MediumLarge, 4000, 12000, false,
                new[] { "Club {0}", "The {0} Velvet Room", "Neon {0}", "Studio {0}" }),
            new BusinessArchetype(BusinessArchetypeId.Casino, "commercial",
                BusinessSignage.Casino, 0, MediumLarge, 9000, 30000, true,
                new[] { "The Golden Ace", "Lucky Seven Casino", "Club Paradiso", "The Velvet Dice" }),
            new BusinessArchetype(BusinessArchetypeId.Hotel, "commercial",
                BusinessSignage.Hotel, 0, MediumLarge, 5000, 16000, true,
                new[] { "Hotel {0}", "The {0} Grand", "Bayview Hotel", "The Regent" }),
            new BusinessArchetype(BusinessArchetypeId.Gym, "commercial", BusinessSignage.Gym,
                0, SmallMedium, 900, 3000, false,
                new[] { "{0}'s Gym", "{0} Iron Yard", "{0} Boxing Club", "The {0} Weight Room" }),
            new BusinessArchetype(BusinessArchetypeId.Fairground, "commercial",
                BusinessSignage.Fairground, 0, LargeCompound, 3000, 9000, true,
                new[] { "{0}'s Fairground", "Pier Amusements", "The Big Wheel", "Sunset Funfair" }),

            // ---------------------------------------------------- forecourt trades
            new BusinessArchetype(BusinessArchetypeId.CarYard, "commercial",
                BusinessSignage.CarYard, 0, LargeCompound, 3500, 11000, true,
                new[] { "{0} Motors", "{0}'s Auto Sales", "Highway Motors", "Star Auto" }),
            new BusinessArchetype(BusinessArchetypeId.FuelStation, "commercial",
                BusinessSignage.Fuel, 0, LargeCompound, 2600, 8000, true,
                new[] { "{0} Filling Station", "Gas & Go", "{0} Fuel", "Crossroads Service" }),

            // ---------------------------------------------------- industry and water
            new BusinessArchetype(BusinessArchetypeId.Factory, "industrial",
                BusinessSignage.Factory, 0, LargeCompound, 6000, 20000, true,
                new[] { "{0} Manufacturing", "{0} & Co. Works", "Union Factory", "Meridian Industries" }),
            new BusinessArchetype(BusinessArchetypeId.Works, "industrial",
                BusinessSignage.Works, 0, LargeCompound, 5000, 17000, true,
                new[] { "{0} Works", "City Works", "{0} Engineering", "Ironside Works" }),
            new BusinessArchetype(BusinessArchetypeId.Refinery, "industrial",
                BusinessSignage.Refinery, 0, LargeCompound, 8000, 26000, true,
                new[] { "{0} Refining", "Delta Refinery", "{0} Petrochemical" }),
            new BusinessArchetype(BusinessArchetypeId.Warehouse, "industrial",
                BusinessSignage.Warehouse, 0, LargeCompound, 3000, 11000, true,
                new[] { "{0} Storage", "{0} Freight Depot", "Bonded Warehouse", "Dockside Storage" }),
            new BusinessArchetype(BusinessArchetypeId.PortCompany, "port",
                BusinessSignage.Port, 0, LargeCompound, 9000, 28000, true,
                new[] { "Harbor Company", "{0} Shipping", "{0} Stevedoring", "City Docks" }),
        };

        static readonly Dictionary<BusinessArchetypeId, BusinessArchetype> ById = BuildById();
        static readonly Dictionary<string, BusinessArchetype> BySignage = BuildBySignage();

        /// <summary>Names slotted into the "{0}" of a template: 1987 street surnames and a
        /// few city words. Kept apart from the civilian surname tables so that retuning the
        /// crowd cannot rename every shop in town.</summary>
        static readonly string[] TradeNames =
        {
            "Marino", "Kowalski", "Delaney", "Okafor", "Vance", "Bruno", "Castellano",
            "Haddad", "Sorrentino", "Petrov", "Nakamura", "Fischer", "Duval", "Moreau",
            "Alvarez", "Quinn", "Rossi", "Novak", "Bergman", "Tanaka", "Mercer",
            "Vitale", "Kaminski", "Osei", "Lindqvist", "Barese", "Colombo", "Reyes",
            "Abbate", "Bianchi", "Cavallo", "Dolan", "Esposito", "Falcone", "Gallo",
            "Hoffman", "Ivanov", "Jankovic", "Keller", "Lombardi", "Mancini", "Nowak",
            "Ortega", "Pappas", "Ruiz", "Salvatore", "Tremblay", "Ueda", "Valenti",
            "Weiss", "Yilmaz", "Zangari", "Brennan", "Carbone", "Doyle", "Ferraro",
            "Grimaldi", "Halloran", "Iversen", "Kovacs", "Larsen", "Moretti", "Nunez",
            "Harbor", "Riverside", "Lakeside", "Central", "Union", "Empire", "Meridian",
            "Bayside", "Eastgate", "Westport", "Northshore", "Old Town", "Steelyard",
            "Grand", "Liberty", "Ironside", "Corner", "Market", "Station", "Parkside",
        };

        public static IReadOnlyList<BusinessArchetype> All => Entries;

        public static bool TryGet(BusinessArchetypeId id, out BusinessArchetype archetype) =>
            ById.TryGetValue(id, out archetype);

        public static BusinessArchetype Get(BusinessArchetypeId id) =>
            ById.TryGetValue(id, out var archetype)
                ? archetype
                : throw new InvalidOperationException($"No archetype entry for {id}.");

        /// <summary>
        /// The authored hint's archetype. Total on purpose: an unknown hint is a mistake in
        /// a provider, and returning false lets the population pass report the site instead
        /// of quietly rolling a random shop onto a signed door.
        /// </summary>
        public static bool TryFromSignage(string signage, out BusinessArchetype archetype)
        {
            archetype = null;
            return !string.IsNullOrEmpty(signage) &&
                   BySignage.TryGetValue(signage, out archetype);
        }

        /// <summary>
        /// The weighted roll for unlabelled glass. The draw is taken from the caller's
        /// per-site stream, so table size and entry order affect what a given site becomes
        /// but can never move any other site.
        /// </summary>
        public static BusinessArchetype RollGeneric(BusinessSiteSize size, System.Random rng)
        {
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));

            var total = 0;
            for (var i = 0; i < Entries.Length; i++)
                if (Entries[i].GenericWeight > 0 && Entries[i].Accepts(size))
                    total += Entries[i].GenericWeight;

            if (total <= 0)
                return null;

            var roll = rng.Next(total);
            for (var i = 0; i < Entries.Length; i++)
            {
                var entry = Entries[i];
                if (entry.GenericWeight <= 0 || !entry.Accepts(size))
                    continue;
                roll -= entry.GenericWeight;
                if (roll < 0)
                    return entry;
            }

            return null;
        }

        /// <summary>
        /// A business name off the archetype's templates. Same seed, same name - the draw
        /// order inside is fixed (template, then trade name), so adding a template changes
        /// what one site is called and nothing else.
        /// </summary>
        public static string Name(BusinessArchetype archetype, System.Random rng)
        {
            if (archetype == null)
                throw new ArgumentNullException(nameof(archetype));
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));
            if (archetype.NameTemplates.Count == 0)
                return archetype.Id.ToString();

            var template = archetype.NameTemplates[rng.Next(archetype.NameTemplates.Count)];
            var trade = TradeNames[rng.Next(TradeNames.Length)];
            return template.IndexOf("{0}", StringComparison.Ordinal) >= 0
                ? string.Format(CultureInfo.InvariantCulture, template, trade)
                : template;
        }

        static BusinessArchetype Retail(
            BusinessArchetypeId id, int weight, int low, int high, params string[] templates) =>
            new BusinessArchetype(id, "commercial", BusinessSignage.None, weight,
                AnyBuilt, low, high, companyOwned: false, nameTemplates: templates);

        static Dictionary<BusinessArchetypeId, BusinessArchetype> BuildById()
        {
            var map = new Dictionary<BusinessArchetypeId, BusinessArchetype>(Entries.Length);
            foreach (var entry in Entries)
                map.Add(entry.Id, entry);
            return map;
        }

        static Dictionary<string, BusinessArchetype> BuildBySignage()
        {
            var map = new Dictionary<string, BusinessArchetype>(StringComparer.Ordinal);
            foreach (var entry in Entries)
            {
                if (string.IsNullOrEmpty(entry.Signage))
                    continue;
                map.Add(entry.Signage, entry);
            }
            return map;
        }
    }
}
