using UnityEngine;
using LivingCity.Entities;
using LivingCity.Generation;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// Makes pressing Play sufficient: the city-life, ownership and UI layers self-install
    /// after the scene loads, so a session where an editor menu was never re-run (or the
    /// scene never saved) cannot silently miss systems. That exact failure shipped once;
    /// this class exists so it cannot ship twice.
    ///
    /// The playable-mafioso layer (PlayerMafioso, context menu, wanted/witness/police
    /// response, WantedHud) is PARKED: its code and the Resources prefab remain in the
    /// project but nothing installs them here anymore. Kill mechanics live on in the
    /// standalone Shooting Demo scene until they are reworked for the map. The occlusion
    /// hider left the parked list on its own merits: its zoom sweep reveals streets
    /// behind foreground buildings for the camera, player or no player.
    /// </summary>
    public static class GameplayBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureGameplay()
        {
            // Two kinds of city are served here. The older GENERATED city is a CityBuilder
            // and gets the whole layer. A city built on a canonical TERRITORY PLAN - every
            // RoadDemoBuilder scene, CoreDemo the game among them - has no CityBuilder and
            // none of the ground-slab machinery, but it does have blocks, an outfit and a
            // ledger, so it gets the organization and map layer. Anything else (the pack
            // scenes, the shooting bench) gets nothing.
            //
            // The canonical branch is why the map is here at all: the ledger's ASSIGN BLOCK
            // needs a StrategicMapHud to pick a block on, and gating the map on CityBuilder
            // left that button dead in the one scene that is the game. Per the project's
            // scenes-are-test-rigs rule the widening belongs in this shared bootstrap and
            // not in a CoreDemo adapter.
            var city = Object.FindAnyObjectByType<CityBuilder>();
            var planned = !city && Object.FindAnyObjectByType<RoadDemo.RoadDemoBuilder>();
            if (!city && !planned)
            {
                EnsureBenchLedger();
                return;
            }

            var host = GameObject.Find("Gameplay") ?? new GameObject("Gameplay");

            if (planned)
            {
                // The two directors the book reads from, the book, and the map it picks
                // blocks on. No PropertyDirector, GangDirector or pedestrian director:
                // those measure a generated city's hierarchy, which this city has not got.
                EnsureUnique<PersonnelDirector>(host);
                EnsureUnique<OutfitDirector>(host);
                EnsureUnique<UI.PersonnelAlmanac>(host);
                EnsureUnique<UI.StrategicMapHud>(host);
                return;
            }

            Ensure<GameplayRuntime>(host);
            Ensure<PropertyDirector>(host);
            Ensure<PersonnelDirector>(host);
            Ensure<OutfitDirector>(host);

            // After both directors above: gang fronts are picked from PropertyDirector's
            // businesses and the player's crew mirrors PersonnelDirector's roster. Same-
            // frame Start order is not guaranteed, so GangDirector also polls for both.
            Ensure<GangDirector>(host);

            // Zoom-gated street reveal. The player sweep inside stays dormant while
            // no PlayerMafioso exists (the parked layer); the zoom sweep only needs
            // the camera and the Buildings root.
            Ensure<PlayerOcclusionHider>(host);

            // The pedestrian life layer. Historically added only by the editor menu
            // (Tools/City/Set Up City Scene) - the exact class of silent absence this
            // bootstrap exists for: without the director there are no chats, sits, shop
            // visits or daily routine, and every civilian popup reads "Out for a walk"
            // forever. Scene-wide checks, not host-local ones, because the menu parks
            // these on the Spawners object - a scene the menu DID wire must not get a
            // second director pacing every roll twice.
            EnsureUnique<PedestrianInteractionDirector>(host);
            EnsureUnique<UI.CityOverlayHud>(host);
            EnsureUnique<UI.PersonnelAlmanac>(host);
            EnsureUnique<UI.StrategicMapHud>(host);
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

        static void Ensure<T>(GameObject host) where T : Component
        {
            if (!host.GetComponent<T>())
                host.AddComponent<T>();
        }

        static void EnsureUnique<T>(GameObject host) where T : Component
        {
            if (!Object.FindAnyObjectByType<T>())
                host.AddComponent<T>();
        }
    }
}
