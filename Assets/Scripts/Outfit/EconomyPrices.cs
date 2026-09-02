using System;
using LivingCity.Business;

namespace LivingCity.Outfit
{
    /// <summary>What a business is worth to the men leaning on it.</summary>
    public enum BusinessTier
    {
        /// <summary>A shopfront: a barber, a cafe, a terrace. Anybody can lean on one.</summary>
        Street = 1,

        /// <summary>A bar, a diner, a gym, a garage. Wants men standing there.</summary>
        Solid = 2,

        /// <summary>A restaurant, a filling station, a car yard, a warehouse. Wants a name.</summary>
        Heavy = 3,

        /// <summary>A club, a hotel, the harbour, a casino. An endgame move, not week one.</summary>
        Endgame = 4,
    }

    /// <summary>Everything one kind of business is worth, in 1987 dollars.</summary>
    public readonly struct BusinessPrice
    {
        public BusinessPrice(
            BusinessTier tier, int weeklyTurnover, int protectionPerWeek, int buyPrice)
        {
            Tier = tier;
            WeeklyTurnover = weeklyTurnover;
            ProtectionPerWeek = protectionPerWeek;
            BuyPrice = buyPrice;
        }

        public BusinessTier Tier { get; }

        /// <summary>What passes through its till in a week.</summary>
        public int WeeklyTurnover { get; }

        /// <summary>What it pays for peace - roughly a twentieth to a tenth of the till.</summary>
        public int ProtectionPerWeek { get; }

        /// <summary>What it costs to buy the place outright.</summary>
        public int BuyPrice { get; }

        /// <summary>What OWNING it nets in a day: a tenth of the till, over a week.</summary>
        public int NetPerDay => Math.Max(1, (int)(WeeklyTurnover * 0.12f / 7f));
    }

    /// <summary>
    /// The price of everything, in nominal 1987 dollars.
    ///
    /// Every number here is copied from `Docs/economy-prices.md`, which is anchored to
    /// documented Miami and US prices of 1985-1988 - the DEA's cocaine series, the gang
    /// account books Levitt and Venkatesh published, the Knapp Commission's pad rates,
    /// court-reported hit fees, the 1985 metro home survey, per-unit business revenue and
    /// 1987 list prices. The DOC is the authority and the code follows it; a number
    /// changed here without changing the doc is a number nobody can defend later.
    ///
    /// The reason the table exists at all: a flat "collect protection pays 60" cannot tell
    /// a barber from a casino, and the whole economy of this game is that it can.
    /// </summary>
    public static class EconomyPrices
    {
        /// <summary>A place nobody has a price for yet: treated as the smallest shopfront,
        /// never as free money.</summary>
        public static BusinessPrice Unknown { get; } =
            new BusinessPrice(BusinessTier.Street, 1_200, 100, 60_000);

        /// <summary>
        /// What this kind of place turns over, pays and costs. Turnovers are the doc's
        /// anchors: a barber-class shop $800/wk, a bar $3,300, a diner $5,000, a corner
        /// store $8,000, a filling station $17,000 with fuel, a hot Miami club $30,000+.
        /// </summary>
        public static BusinessPrice Of(BusinessArchetypeId archetype)
        {
            switch (archetype)
            {
                // ---- tier 1: the shopfronts. A hundred a week, and no preconditions.
                case BusinessArchetypeId.Barber:
                case BusinessArchetypeId.Cobbler:
                case BusinessArchetypeId.Locksmith:
                case BusinessArchetypeId.Newsstand:
                case BusinessArchetypeId.Florist:
                case BusinessArchetypeId.Tailor:
                    return new BusinessPrice(BusinessTier.Street, 900, 100, 60_000);

                case BusinessArchetypeId.Cafe:
                    return new BusinessPrice(BusinessTier.Street, 2_200, 100, 60_000);

                case BusinessArchetypeId.Baker:
                case BusinessArchetypeId.Butcher:
                case BusinessArchetypeId.Laundry:
                case BusinessArchetypeId.Bookshop:
                case BusinessArchetypeId.RecordShop:
                case BusinessArchetypeId.TravelAgent:
                    return new BusinessPrice(BusinessTier.Street, 2_600, 100, 70_000);

                case BusinessArchetypeId.Grocer:
                case BusinessArchetypeId.Hardware:
                case BusinessArchetypeId.ElectricalShop:
                    return new BusinessPrice(BusinessTier.Street, 3_000, 100, 80_000);

                // ---- tier 2: the solid trades. Wants men on the street outside.
                case BusinessArchetypeId.Pub:
                    return new BusinessPrice(BusinessTier.Solid, 3_300, 300, 75_000);
                case BusinessArchetypeId.Pizzeria:
                    return new BusinessPrice(BusinessTier.Solid, 3_800, 300, 75_000);
                case BusinessArchetypeId.Gym:
                    return new BusinessPrice(BusinessTier.Solid, 4_500, 400, 100_000);
                case BusinessArchetypeId.Diner:
                    return new BusinessPrice(BusinessTier.Solid, 5_000, 400, 110_000);

                // A pawnbroker and a betting shop take cash all day and can afford more
                // than the glass either side of them.
                case BusinessArchetypeId.PawnShop:
                case BusinessArchetypeId.BettingShop:
                    return new BusinessPrice(BusinessTier.Solid, 6_000, 500, 90_000);
                case BusinessArchetypeId.Pharmacy:
                    return new BusinessPrice(BusinessTier.Solid, 7_000, 500, 120_000);

                // ---- tier 3: the heavy places. Wants a name on the street first.
                case BusinessArchetypeId.Restaurant:
                    return new BusinessPrice(BusinessTier.Heavy, 9_000, 1_000, 250_000);
                case BusinessArchetypeId.CarYard:
                    return new BusinessPrice(BusinessTier.Heavy, 12_000, 1_200, 150_000);
                case BusinessArchetypeId.Warehouse:
                    return new BusinessPrice(BusinessTier.Heavy, 14_000, 1_500, 250_000);
                case BusinessArchetypeId.FuelStation:
                    return new BusinessPrice(BusinessTier.Heavy, 17_000, 2_000, 400_000);
                case BusinessArchetypeId.Fairground:
                    return new BusinessPrice(BusinessTier.Heavy, 18_000, 2_000, 500_000);

                // ---- tier 4: the endgame. High standing, real retaliation, real heat.
                case BusinessArchetypeId.Factory:
                case BusinessArchetypeId.Works:
                case BusinessArchetypeId.Refinery:
                    return new BusinessPrice(BusinessTier.Endgame, 30_000, 5_000, 1_500_000);
                case BusinessArchetypeId.Nightclub:
                    return new BusinessPrice(BusinessTier.Endgame, 40_000, 5_000, 750_000);
                case BusinessArchetypeId.Hotel:
                    return new BusinessPrice(BusinessTier.Endgame, 45_000, 6_000, 1_000_000);
                case BusinessArchetypeId.PortCompany:
                    return new BusinessPrice(BusinessTier.Endgame, 60_000, 8_000, 3_000_000);
                case BusinessArchetypeId.Casino:
                    return new BusinessPrice(BusinessTier.Endgame, 80_000, 10_000, 5_000_000);

                default:
                    return Unknown;
            }
        }

        /// <summary>
        /// What a family may take from this place in a week. Reads the table rather than a
        /// flat constant, because the difference between a barber and a casino IS the
        /// economy of this game.
        /// </summary>
        public static int ProtectionPerWeek(BusinessArchetypeId archetype) =>
            Of(archetype).ProtectionPerWeek;

        /// <summary>What owning it nets in a day.</summary>
        public static int NetPerDay(BusinessArchetypeId archetype) => Of(archetype).NetPerDay;

        /// <summary>What it costs to buy the place.</summary>
        public static int BuyPrice(BusinessArchetypeId archetype) => Of(archetype).BuyPrice;

        // ------------------------------------------------------- premises with no trade

        /// <summary>An apartment - a bolt-hole, not a business.</summary>
        public const int Apartment = 55_000;

        /// <summary>A house.</summary>
        public const int House = 85_000;

        /// <summary>An empty storefront, before anybody trades out of it.</summary>
        public const int EmptyStorefront = 90_000;

        /// <summary>Fitting one out and getting it licensed.</summary>
        public const int SetUpBusiness = 20_000;

        // -------------------------------------------------------------- crime services

        /// <summary>Court-documented range was $2,000-40,000; this is the middle of it.</summary>
        public const int ContractKilling = 15_000;

        /// <summary>What a kidnapping brings in - the ransom cut, not the ransom.</summary>
        public const int KidnapCut = 5_000;

        /// <summary>An officer on the pad, per month. Knapp Commission: $400-1,500.</summary>
        public const int PoliceOnThePad = 800;

        /// <summary>A word in the right ear.</summary>
        public const int Bribe = 500;

        /// <summary>A donation nobody asked about.</summary>
        public const int Donation = 1_000;

        /// <summary>A shakedown of a till and a drawer.</summary>
        public const int Shakedown = 200;

        /// <summary>A raid: the register and what is on the shelves.</summary>
        public const int Raid = 500;

        /// <summary>
        /// What it costs to put a man on the books, through every door there is: the
        /// ledger's own HIRE A MAN and the Recruit order both. There used to be two
        /// prices - fifty dollars over the counter and five hundred out on the corner -
        /// and the fifty made the counter the only sane way to grow, for no reason
        /// anybody had decided. One signing, one price; the ORDER differs only by
        /// taking twelve hours and letting the recruiter's eye find a better man.
        /// (Docs/economy-prices.md Â§8.)
        /// </summary>
        public const int RecruitSigning = 500;
    }
}
