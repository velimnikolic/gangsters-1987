using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What the city calls its streets. The grid is laid out as numbered lines
    /// (verticalRoadX, horizontalRoadZ) and nothing in the world needs a name to
    /// stand up - but the map does: the printed plan the player pulls back into is
    /// mostly LETTERING, streets named along their length, and a city with no
    /// street names reads as a diagram rather than a town.
    ///
    /// Seeded off the same number the grid's spacing is rolled from, so one city
    /// keeps its names for as long as its seed stands: the block you were told to
    /// burn is on the same corner next session.
    ///
    /// American, and of the period - the pool is the ordinary stock of a mid-sized
    /// US city in 1987: presidents and generals, trees, the old mills and the
    /// railroad. No two lines share a name.
    /// </summary>
    public sealed class StreetNames
    {
        // The stems. Long enough that a ninety-block city never runs out and never
        // repeats: the roll takes them without replacement.
        static readonly string[] Stems =
        {
            "JERICO", "PROVIDENCE", "ROCK", "BARROW", "NOWELL", "WHITNEY", "GRANT",
            "ROMANA", "ESTES", "AMITY", "LAWNDALE", "JESSUP", "TAMPA", "LINCOLN",
            "MADISON", "GARFIELD", "HARLAN", "DEVON", "MERCER", "ASHLAND", "CANAL",
            "DELANEY", "FULTON", "HALSTED", "KEELER", "LARIMER", "OSAGE", "PULASKI",
            "QUINCY", "REDWOOD", "SEDGWICK", "TILDEN", "UNION", "VERNON", "WABASH",
            "YATES", "ZANE", "CLARK", "DEARBORN", "ELM", "FRANKLIN", "GALENA",
            "HURON", "IRVING", "KENMORE", "LOWELL", "MARQUETTE", "NAVARRE", "ORCHARD",
            "PEMBROKE", "RIDGEWAY", "SAWMILL", "TREMONT", "WAVERLY", "YORK", "ALDEN",
            "BEDFORD", "CORTLAND", "DUNLAP", "EASTON", "FAIRVIEW", "GRISWOLD",
            "HOLLIS", "IRONDALE", "JEFFERSON", "KILBOURN", "LANDRY", "MONROE",
        };

        // What kind of street it is. A wide line gets the grand suffix - a boulevard
        // or an avenue is what a city calls the road it laid four lanes wide; the
        // ordinary lines are streets and roads.
        static readonly string[] Grand = { "BLVD", "AVE", "PKWY" };
        static readonly string[] Plain = { "ST", "RD", "ST", "AVE", "ST", "LN" };

        // The town itself. Printed once across the middle of the map, the way the
        // original prints "Buffalo Falls" over its own.
        static readonly string[] Towns =
        {
            "Buffalo Falls", "Port Alden", "Cedar Harbor", "New Eldridge",
            "Rockmill", "Lake Carroll", "Atlas Bay", "Delmore", "Fort Yates",
            "Kingsport", "Marrowdale", "Saint Cloud",
        };

        // What the town calls its own parts. Not districts hanging off the grid - those
        // are places of their own with their own names (CityLayout) - but the blocks
        // inside it: what a man means when he says he works the Flats, or that the
        // Bricktown crew took a shipment. Ordinary American neighbourhood stock of the
        // period: the trades that were there before the houses, the hills, the wards.
        static readonly string[] Quarters =
        {
            "Riverside", "The Flats", "Bricktown", "Ironbound", "Cannery Row",
            "Old Town", "The Heights", "Sawmill", "Union Square", "Northside",
            "West End", "Southbank", "Printer's Row", "Tannery Hill", "Gasworks",
            "Stockyards", "Beacon Hill", "Fairgrounds", "Little Italy", "Rosewood",
            "Carver Park", "Lakeview", "Kingsley Park", "The Palisade", "Bishop Hill",
            "Warehouse Row", "Belmont", "Harrow Green",
        };

        readonly string[] _vertical, _horizontal;
        readonly List<string> _quarters;

        /// <summary>The town's own name.</summary>
        public string City { get; }

        public StreetNames(int seed, bool[] verticalIsBoulevard, bool[] horizontalIsBoulevard)
        {
            var rng = new System.Random(seed * 7919 + 13);
            City = Towns[rng.Next(Towns.Length)];

            // one bag of stems for the whole city, drawn without replacement, so no
            // two lines can end up "GRANT ST" and "GRANT RD" two blocks apart
            var bag = new List<string>(Stems);
            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
            int taken = 0;

            _vertical = Roll(verticalIsBoulevard, bag, ref taken, rng);
            _horizontal = Roll(horizontalIsBoulevard, bag, ref taken, rng);

            // the quarters, drawn without replacement off the same seed
            _quarters = new List<string>(Quarters);
            for (int i = _quarters.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (_quarters[i], _quarters[j]) = (_quarters[j], _quarters[i]);
            }
        }

        /// <summary>The name of the city's <paramref name="index"/>th quarter - the
        /// blocks inside the grid, not the places hanging off it.</summary>
        public string Quarter(int index)
            => _quarters.Count == 0 ? "Quarter" : _quarters[Mathf.Abs(index) % _quarters.Count];

        static string[] Roll(bool[] boulevard, List<string> bag, ref int taken, System.Random rng)
        {
            int count = boulevard != null ? boulevard.Length : 0;
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                string stem = bag[taken++ % bag.Count];
                string tail = boulevard[i]
                    ? Grand[rng.Next(Grand.Length)]
                    : Plain[rng.Next(Plain.Length)];
                names[i] = stem + " " + tail;
            }
            return names;
        }

        /// <summary>The name of vertical road line <paramref name="i"/> (north-south).</summary>
        public string Vertical(int i) =>
            _vertical != null && i >= 0 && i < _vertical.Length ? _vertical[i] : null;

        /// <summary>The name of horizontal road line <paramref name="j"/> (east-west).</summary>
        public string Horizontal(int j) =>
            _horizontal != null && j >= 0 && j < _horizontal.Length ? _horizontal[j] : null;
    }
}
