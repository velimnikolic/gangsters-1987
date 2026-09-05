namespace RoadDemo
{
    /// <summary>Shared envelope and walking surface of the used-car sales venue.</summary>
    public static class ResidentialCaryard
    {
        public const int WidthCells = 8, DepthCells = 5;
        public const float Deck = .04f;
        public static ResidentialUnit Describe() => new ResidentialUnit
        {
            Name = "caryard", CW = WidthCells, CD = DepthCells,
            Kind = ResidentialKind.Amenity, MaxH = 6f, Floor = 0f,
            Pieces = 160, Seats = 2, Trees = 0,
            Plan = new[] { "########", "########", "########", "########", "########" },
            Face = new[] { true, false, false, false }, Doors = new[] { 1, 0, 0, 0 },
            Shops = new int[4], Stoops = new int[4],
            ShopCells = new[] { "00000000", "00000", "00000000", "00000" },
            ShopBays = System.Array.Empty<ResidentialShopBay>(), Over = new float[4],
        };
    }
}
