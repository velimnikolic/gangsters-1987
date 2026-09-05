namespace RoadDemo
{
    /// <summary>Shared footprint of the authored outdoor training venue.</summary>
    public static class ResidentialGym
    {
        public const int Cells = 5;
        public const float Deck = 0.4f;
        public const float RampCentreZ = 7.2f;
        public static ResidentialUnit Describe() => new ResidentialUnit
        {
            Name = "gym", CW = Cells, CD = Cells,
            Kind = ResidentialKind.Amenity, MaxH = 6f, Floor = 0f,
            Pieces = 121, Seats = 6, Trees = 0,
            Plan = new[] { "#####", "#####", "#####", "#####", "#####" },
            Face = new[] { true, false, false, true },
            Doors = new[] { 1, 0, 0, 1 },
            Shops = new int[4], Stoops = new int[4],
            ShopCells = new[] { "00000", "00000", "00000", "00000" },
            ShopBays = System.Array.Empty<ResidentialShopBay>(), Over = new float[4],
        };
    }
}
