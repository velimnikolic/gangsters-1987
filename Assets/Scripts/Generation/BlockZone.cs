namespace LivingCity.Generation
{
    /// <summary>
    /// What a city block is FOR. Chicago in the 1920s, so the ceiling is masonry, not glass:
    /// the tallest thing in the catalogue that belongs here is the 5-storey terrace kit at
    /// 23.8m. Every office tower in the pack (39m to 92m) is out.
    ///
    /// Note what is NOT here: there is no Commercial zone. A block that is nothing but cafes
    /// reads as a theme park, and it is not how the period built. Shops are a low-weight group
    /// mixed INTO the residential palette instead - a storefront on the ground floor with
    /// flats above, and the corner slot biased toward the tavern. See PrefabDatabase.ZonePalette.
    ///
    /// Police, Hospital, School and FireStation are zones rather than one entry in some civic
    /// bucket because each is a whole block built around a single landmark building, with a
    /// yard and outbuildings - and each is capped at one per city by ZonePlanner.
    /// </summary>
    public enum BlockZone
    {
        /// <summary>
        /// The connective tissue, and now the whole residential fabric: 4- and 5-storey terrace
        /// kits mixed along one run, with the odd storefront between them.
        /// </summary>
        ResidentialHigh,

        Industrial,

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
