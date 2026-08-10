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
            // Only the generated city - the Shooting Demo and pack scenes have no
            // CityBuilder and want none of this.
            if (!Object.FindAnyObjectByType<CityBuilder>())
                return;

            var host = GameObject.Find("Gameplay") ?? new GameObject("Gameplay");

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
