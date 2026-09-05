using Unity.Pipeline.Commands;

namespace LivingCity.EditorTools
{
    /// <summary>Compatibility for the old authoring command. There is only one block set now.</summary>
    public static class NeglectedResidentialSketch
    {
        public const string ScenePath = ResidentialSketch.DemoScene;
        public const string ComparisonRoot = "RESIDENTIAL NEGLECTED COMPARISON";
        public static bool Excluded(string name) => ResidentialConditionSketch.Excluded(name);
        [CliCommand("gangsters_residential_neglected", "Prepare the shared dynamic residential condition demo.", MainThreadRequired = true)]
        public static object Build() => ResidentialConditionSketch.Build();
    }
}
