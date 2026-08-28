namespace RoadDemo
{
    /// <summary>
    /// Shared dimensions for every generated block in CoreDemo. Keeping the pavement rule
    /// here prevents the authored core blocks and the residential, park, quay and parking
    /// composers from quietly drifting to different widths.
    /// </summary>
    public static class CoreBlockMetrics
    {
        public const int Cell = 5;
        public const int PavementTiles = 2;
        public const float PavementWidth = Cell * PavementTiles;
    }
}
