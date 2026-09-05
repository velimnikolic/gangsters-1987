namespace RoadDemo
{
    /// <summary>The catalog replacement and its baked prefab share one Forge recipe.</summary>
    public static class ResidentialForgeReplacement
    {
        public const string Name = "residential-13";
        public static ResidentialFacade.Sheet Roll() => ResidentialFacade.Roll(198713, 4, 4);

        public static ResidentialUnit Describe()
        {
            var unit = Roll().Unit;
            unit.Name = Name;
            return unit;
        }
    }
}
