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
    /// Hospital, School and Bank are zones rather than entries in some civic bucket
    /// because each is a whole block built around a single landmark building, with a yard and
    /// outbuildings around it. Each is capped at ONE per city by ZonePlanner - and a cap is all
    /// it is. None of them is promised.
    ///
    /// That distinction is the one to hold on to when reading this file. A ceiling says the city
    /// may not have two hospitals; it does not say the city has a hospital. The two used to be
    /// conflated: ZonePlanner ran a rescue pass that INFERRED "the city is supposed to have this"
    /// from the shape of the caps - maxBlocks 1 together with a size cap - and so quietly promised
    /// a block to every zone that happened to be shaped like a civic one. The promise is an
    /// explicit per-palette switch now, PrefabDatabase.ZonePalette.guaranteed, and every palette
    /// in the shipped database sets it FALSE. So an unlucky seed can produce a city with no
    /// school in it, the same way it has always been able to produce one with no post office,
    /// and each of those absences is a roll of the dice rather than a bug. Weight is the only
    /// thing standing between a zone and never appearing.
    ///
    /// The Bank is the one exception, and it is not an exception to the rule above so much as a
    /// different mechanism: the city always has exactly one, but WHICH SHAPE it takes - its own
    /// block or a landmark in the street wall - is a single roll ZonePlanner makes per city and
    /// then fulfils by force. See ZonePlanner.Assign.
    ///
    /// The fire station used to be one of those and is not a zone any more. A whole block
    /// is the city's scarcest thing - about ten of them, measured - and a fire house does not need one:
    /// the period built them INTO the street wall, doors onto the pavement. It is an ordinary
    /// piece of the residential fabric now, capped at one per city through
    /// PrefabDatabase.uniqueBuildings the same way the post office is. See UniqueBuildings.
    ///
    /// The police station followed it, by the other door: too wide for a terrace slot, it is
    /// ResidentialHigh's LANDMARK rather than one of its shop prefabs - recessed off the street
    /// wall behind a forecourt bay of patrol cars (see BlockBuilder.PlaceLandmark and
    /// ZonePalette.landmarkCars), with the same uniqueBuildings cap keeping it to one per city.
    /// </summary>
    public enum BlockZone
    {
        /// <summary>
        /// The connective tissue, and now the whole residential fabric: 4- and 5-storey terrace
        /// kits mixed along one run, with the odd storefront between them.
        /// </summary>
        ResidentialHigh,

        Industrial,

        /// <summary>
        /// Retired - no palette builds this any more; the station is ResidentialHigh's
        /// landmark. The member stays because the enum is serialized as ints (CityConfig's
        /// debugZone, the palette assets) and deleting it would shift every zone below.
        /// </summary>
        Police,

        Hospital,
        School,

        /// <summary>No perimeter buildings - tile-park laid per cell, then planted.</summary>
        Park,

        /// <summary>No perimeter buildings - asphalt and rows of cars.</summary>
        Parking,

        /// <summary>
        /// The bank, and the city always has exactly one. building-bank had been sitting
        /// unreferenced in the pack the whole time - measured 17.10 x 20.53 and 13.70m tall,
        /// which is why it gets a zone of its own rather than a slot in the terrace: it is wider
        /// than a lot run and finished on all four elevations, so a street wall has nowhere to
        /// put it flush.
        ///
        /// It appears two ways and ZonePlanner picks between them with one roll per city, so the
        /// guarantee holds whichever way the roll falls (see BankOwnBlockChance). Usually it
        /// stands in the street wall as ResidentialHigh's landmark, the way the police station
        /// does. Sometimes it takes this zone instead: a single-cell block, the smallest the map
        /// offers, with its forecourt of cars in front of it.
        ///
        /// Deliberately NOT in SingleLandmarkZones - a bank block must not be able to cost
        /// the city its hospital. It has a non-empty groups[], so it builds its perimeter the
        /// ordinary way and needs no dresser and no zone branch in BlockBuilder at all - which is
        /// now true of every zone, the last such branch having gone with the church.
        /// </summary>
        Bank,

        /// <summary>
        /// The docks - a whole-block compound like Industrial, but asymmetric: everything in it
        /// faces the water, and the water is not part of the city at all. It exists only on a
        /// block touching the map boundary (ZonePalette.requiresMapEdge), because the sea is
        /// laid OUTSIDE the grid, beyond the outline - the first and only thing out there.
        ///
        /// This is also the first zone authored after the setting moved from the 1920s to the
        /// 1980s, which is why the container stacks are deliberate: ISO boxes, a container ship
        /// at the berth, forklifts on the apron. Not a promise - one per city at most, and only
        /// when the roll lands on an edge block (see ZonePlanner and PortLayout).
        /// </summary>
        Port,
    }
}
