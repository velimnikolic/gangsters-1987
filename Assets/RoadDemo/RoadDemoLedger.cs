using UnityEngine;
using LivingCity.Gameplay;

namespace RoadDemo
{
    // Brings the outfit ledger into the road demo. GameplayBootstrap declines scenes
    // without a CityBuilder on purpose, so this installer (LedgerMenuScene's recipe,
    // scoped to the demo) seats the two directors the book reads from and the almanac
    // itself - and nothing else of the gameplay layer. The book stays closed until P,
    // exactly as in the city; without a CityBuilder's pedestrian groups the mugshots
    // keep their initials, and that is the expected dress here.
    public static class RoadDemoLedger
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureLedger()
        {
            if (!Object.FindAnyObjectByType<RoadDemoBuilder>())
                return;

            var host = GameObject.Find("Gameplay") ?? new GameObject("Gameplay");
            Ensure<PersonnelDirector>(host);
            Ensure<OutfitDirector>(host);
            Ensure<LivingCity.UI.PersonnelAlmanac>(host);
        }

        // Scene-wide checks, the GameplayBootstrap discipline: a scene someone
        // already wired must not get a second director.
        static void Ensure<T>(GameObject host) where T : Component
        {
            if (!Object.FindAnyObjectByType<T>())
                host.AddComponent<T>();
        }
    }
}
