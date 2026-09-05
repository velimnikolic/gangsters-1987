using UnityEngine;
using LivingCity.Entities;
using UnityEngine.SceneManagement;

namespace LivingCity.Gameplay
{
    /// <summary>Installs the shared game services for CoreDemo and its focused test rigs.</summary>
    public static class GameplayBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            // Runtime initialization runs once per Play, not after a save reloads
            // the scene. Reinstall the scene-owned directors on every scene load.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureGameplay();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureGameplay()
        {
            if (!Object.FindAnyObjectByType<RoadDemo.RoadDemoBuilder>())
            {
                EnsureBenchLedger();
                return;
            }
            var host = GameObject.Find("Gameplay") ?? new GameObject("Gameplay");
            EnsureUnique<PersonnelDirector>(host);
            EnsureUnique<OutfitDirector>(host);
            EnsureUnique<UI.PersonnelAlmanac>(host);
        }

        /// <summary>
        /// The combat benches - the crew and cover demos - put the OUTFIT'S OWN men on
        /// the street (DemoCrews reads PersonnelDirector), so they need the two directors
        /// and the book, and nothing else. They have no city, so no map.
        /// </summary>
        static void EnsureBenchLedger()
        {
            if (!Object.FindAnyObjectByType<CrewDemo.CrewDemoBuilder>() &&
                !Object.FindAnyObjectByType<CoverDemo.CoverDemoBuilder>())
                return;

            var host = GameObject.Find("Gameplay") ?? new GameObject("Gameplay");
            EnsureUnique<PersonnelDirector>(host);
            EnsureUnique<OutfitDirector>(host);
            EnsureUnique<UI.PersonnelAlmanac>(host);
        }

        static void EnsureUnique<T>(GameObject host) where T : Component
        {
            if (!Object.FindAnyObjectByType<T>())
                host.AddComponent<T>();
        }
    }
}
