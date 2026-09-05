using UnityEngine;
namespace LivingCity.Tests
{
    public static partial class MiniCoreRegressionTests
    {
        static void BlockRoundBelongsToPlayer()
        {
            for (int repetition = 0; repetition < 5; repetition++)
            {
                using var fixture = new Fixture();
                var root = fixture.Root;
                var runtime = root.AddComponent<RoadDemo.TerritoryRuntime>();
                var runtimeType = typeof(RoadDemo.TerritoryRuntime);
                var instanceFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                runtimeType.GetField("geography", instanceFlags).SetValue(runtime,
                    new LivingCity.Territory.TerritoryGeography(null, LivingCity.Territory.TerritoryGeographySettings.Default));
                runtimeType.GetField("racket", instanceFlags).SetValue(runtime, new LivingCity.Territory.TerritoryRacketLedger());
                var block = new LivingCity.Territory.TerritoryBlockId("round-view-" + repetition);
                var bodyType = runtimeType.GetNestedType("RoundBody", System.Reflection.BindingFlags.NonPublic);
                var bodies = (System.Collections.IList)runtimeType.GetField("bodies", instanceFlags).GetValue(runtime);
                object AddRound(int house, LivingCity.Territory.TerritoryBlockId target, int carried)
                {
                    var row = System.Activator.CreateInstance(bodyType, true);
                    bodyType.GetField("Round").SetValue(row, new LivingCity.Territory.TerritoryRound {
                        Kind = LivingCity.Territory.TerritoryRoundKind.Collect,
                        House = new LivingCity.Territory.TerritoryGangId(house), BlockId = target,
                        CrewId = house * 1000 + 1, Carried = carried,
                        Stage = LivingCity.Territory.TerritoryRoundStage.Walking
                    });
                    bodies.Add(row); return row;
                }
                var bindingType = runtimeType.GetNestedType("BlockRacketBinding", System.Reflection.BindingFlags.NonPublic);
                var binding = (LivingCity.UI.IBlockRacketSource)System.Activator.CreateInstance(bindingType, new object[] { runtime });
                var rival = AddRound(repetition + 1, block, 153);
                binding.TryGetBlock(block, out var foreignOnly);
                bool hiddenRival = !foreignOnly.RoundOut && foreignOnly.RoundCarried == 0 &&
                    foreignOnly.InTheBag == 0 && foreignOnly.RoundCollectorName == "";
                AddRound(0, new LivingCity.Territory.TerritoryBlockId(block.Value + "-other"), 99);
                binding.TryGetBlock(block, out var otherBlock);
                bool hiddenOtherBlock = !otherBlock.RoundOut && otherBlock.RoundCarried == 0;
                var own = AddRound(0, block, 23 + repetition);
                binding.TryGetBlock(block, out var shared);
                bool ownVisible = shared.RoundOut && shared.RoundCarried == 23 + repetition && shared.InTheBag == 23 + repetition;
                bodies.Remove(own);
                binding.TryGetBlock(block, out var ended);
                bool noStaleRival = !ended.RoundOut && ended.RoundCarried == 0 &&
                    ((LivingCity.Territory.TerritoryRound)bodyType.GetField("Round").GetValue(rival)).Carried == 153;
                Require(hiddenRival && hiddenOtherBlock && ownVisible && noStaleRival,
                    "the player's block card borrowed another house's active collector round or hid its own");
            }
        }
    }
}
