namespace LivingCity.Generation
{
    /// <summary>
    /// What a city block is FOR. Chicago in the 1920s, so the ceiling is masonry, not glass:
    /// the tallest thing in the catalogue that belongs here is the 5-storey terrace kit at
    /// 23.8m. Every office tower in the pack (39m to 92m) is out.
    ///
    /// Note what is NOT here: there is no Commercial zone. A block that is nothing but cafes
    /// reads as a theme park, and it is not how the period built. Shops are a low-weight group
    /// mixed INTO the residential palettes instead - a storefront on the ground floor with
    /// flats above, and the corner slot biased toward the tavern. See PrefabDatabase.ZonePalette.
    ///
    /// Police, Hospital, School and FireStation are zones rather than one entry in some civic
    /// bucket because each is a whole block built around a single landmark building, with a
    /// yard and outbuildings - and each is capped at one per city by ZonePlanner.
    /// </summary>
    public enum BlockZone
    {
        /// <summary>The connective tissue: 4- and 5-storey terrace kits mixed along one run.</summary>
        ResidentialHigh,

        /// <summary>Detached houses and workers' cottages, with the odd corner store.</summary>
        ResidentialLow,

        /// <summary>Same masonry fabric at its densest, plus a bank / hotel / cinema landmark.</summary>
        Downtown,

        Industrial,

        /// <summary>Chicago's Chinatown dates from 1912, so the pack's china set is in period.</summary>
        Chinatown,

        Police,
        Hospital,
        School,
        FireStation,

        /// <summary>No perimeter buildings - tile-park laid per cell, then planted.</summary>
        Park,

        /// <summary>No perimeter buildings - asphalt and rows of cars.</summary>
        Parking,
    }
}
