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
            // Every bench that puts THE OUTFIT'S OWN men on the street has to be named
            // here: the crews are dealt off the ledger's roster (DemoCrews.Sync reads
            // PersonnelDirector), so a scene this gate does not know gets rivals and
            // nobody to play. That is a scene with no gang the player can control, and
            // it looks like a bug in the scene rather than a missing installer.
            if (!Object.FindAnyObjectByType<RoadDemoBuilder>() &&
                !Object.FindAnyObjectByType<CrewDemo.CrewDemoBuilder>() &&
                !Object.FindAnyObjectByType<CoverDemo.CoverDemoBuilder>())
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
