using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.City;
using LivingCity.Data;
using LivingCity.Entities;
using LivingCity.Gangs;
using LivingCity.Generation;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// Gives the city its underworld, phase 0: five gangs - the player's outfit and four
    /// AI families - each operating behind a commercial front (a cafe or restaurant,
    /// never the post office or the gun shop), with the crew loitering outside it. No
    /// missions yet, so everyone is standing by.
    ///
    /// Installed by GameplayBootstrap; runs AFTER two upstream passes whose Start order
    /// Unity does not guarantee, so it polls with a bounded wait (the PortDirector
    /// posture: every missing precondition is a logged stand-down, never an exception):
    /// PropertyDirector must have stamped the businesses this pass picks fronts from,
    /// and PersonnelDirector must hold the roster the player's crew mirrors.
    ///
    /// Front candidates are RE-SORTED here with PropertyDirector's own comparator rather
    /// than trusting PropertyRegistry order - registration order happens to match today,
    /// but the determinism of front picks must not hang on a lifecycle accident.
    /// </summary>
    public sealed class GangDirector : MonoBehaviour
    {
        /// <summary>Frames to wait for the upstream passes before standing down.</summary>
        const int WaitFrameLimit = 300;

        /// <summary>Front prefabs. building-post is excluded on purpose (a post office is
        /// no mafia front) and the gun shop was never a business at all.</summary>
        static readonly string[] FrontPrefabs = { "building-cafe", "building-restaurant" };

        /// <summary>Metres along the facade per stand slot, door outward; slot 0 (the
        /// lieutenant's) is the door itself. Staggered depth breaks the parade line.</summary>
        static readonly float[] SlotAlong = { 0f, 0.9f, -0.9f, 1.8f, -1.8f, 2.7f, -2.7f, 3.6f };

        IEnumerator Start()
        {
            var builder = FindAnyObjectByType<CityBuilder>();
            if (!builder || !builder.Config || !builder.Prefabs)
            {
                Debug.LogWarning("[Gangs] No generated city - the families stay home.", this);
                yield break;
            }

            for (var frames = 0;
                 (PropertyRegistry.Businesses.Count == 0 ||
                  PersonnelDirector.Instance == null ||
                  PersonnelDirector.Instance.Roster == null)
                 && frames < WaitFrameLimit;
                 frames++)
                yield return null;

            if (PropertyRegistry.Businesses.Count == 0)
            {
                Debug.LogWarning("[Gangs] No businesses appeared - PropertyDirector must " +
                                 "run first. The families stand down.", this);
                yield break;
            }

            var gangs = GangSeeder.Generate(
                builder.Config.seed, PersonnelDirector.Instance?.Roster);
            if (gangs[GangCatalog.PlayerGangId].Members.Count == 0)
                Debug.LogWarning("[Gangs] No personnel roster - the player's front will " +
                                 "stand empty.", this);

            var markers = CollectFrontCandidates(out var candidates);
            var picks = GangFronts.Select(
                candidates, gangs[GangCatalog.PlayerGangId].FrontRoll, gangs.Length);

            GangRegistry.Install(gangs);

            // The strategic map's colour seam: gang members get their family's colour,
            // everyone else keeps the fallback the hud already applies.
            UI.MapAffiliation.Provider = person =>
                person is GangMemberAgent member && member.GangId >= 0
                    ? UI.GangPalette.Of(member.GangId)
                    : (Color?)null;

            var spawned = 0;
            for (var i = 0; i < gangs.Length; i++)
            {
                if (picks[i] < 0)
                {
                    Debug.LogWarning($"[Gangs] Only {candidates.Count} commercial fronts " +
                                     $"for {gangs.Length} gangs - {gangs[i].Name} operates " +
                                     "without one.", this);
                    continue;
                }

                var front = markers[picks[i]];
                front.GangId = gangs[i].Id;
                GangRegistry.SetFrontBusiness(gangs[i].Id, front);
                spawned += SpawnCrew(gangs[i], front, builder.Prefabs);
            }

            Debug.Log($"[Gangs] {gangs.Length} gangs seated, {spawned} members on the " +
                      $"street (seed {builder.Config.seed}).");
        }

        /// <summary>Cafes and restaurants only, in PropertyDirector's sorted order.</summary>
        List<BusinessMarker> CollectFrontCandidates(
            out List<GangFronts.FrontCandidate> candidates)
        {
            var markers = new List<BusinessMarker>();
            foreach (var business in PropertyRegistry.Businesses)
            {
                if (!business || business.Category != BusinessCategory.Commercial)
                    continue;

                var prefabName = business.gameObject.name;
                foreach (var prefix in FrontPrefabs)
                    if (prefabName.StartsWith(prefix))
                    {
                        markers.Add(business);
                        break;
                    }
            }

            markers.Sort((a, b) =>
            {
                var ax = Mathf.RoundToInt(a.transform.position.x * 10f);
                var bx = Mathf.RoundToInt(b.transform.position.x * 10f);
                if (ax != bx)
                    return ax.CompareTo(bx);

                var az = Mathf.RoundToInt(a.transform.position.z * 10f);
                var bz = Mathf.RoundToInt(b.transform.position.z * 10f);
                if (az != bz)
                    return az.CompareTo(bz);

                return string.CompareOrdinal(a.gameObject.name, b.gameObject.name);
            });

            candidates = new List<GangFronts.FrontCandidate>(markers.Count);
            foreach (var marker in markers)
                candidates.Add(new GangFronts.FrontCandidate(
                    marker.BlockId,
                    marker.transform.position.x,
                    marker.transform.position.z));

            return markers;
        }

        int SpawnCrew(Gang gang, BusinessMarker front, PrefabDatabase prefabs)
        {
            // Every shopfront prefab this pass admits carries a ShopEntrance from
            // generation; a bare transform is the degrade path, not the design.
            var entrance = front.GetComponent<ShopEntrance>();
            var door = entrance ? entrance.DoorWorld : front.transform.position;
            var facing = entrance ? entrance.Facing : Flat(front.transform.forward);
            var tangent = Vector3.Cross(Vector3.up, facing);

            var soldierPrefab = FindPeoplePrefab(prefabs, GangCatalog.SoldierModels[gang.Id]);
            var lieutenantPrefab =
                FindPeoplePrefab(prefabs, GangCatalog.LieutenantModels[gang.Id]);

            var memberRng = new System.Random(gang.MemberSeed);
            var spawned = 0;

            for (var i = 0; i < gang.Members.Count; i++)
            {
                var identity = gang.Members[i];
                var prefab = identity.Lieutenant ? lieutenantPrefab : soldierPrefab;
                var seed = memberRng.Next(); // Drawn even when the model is missing, so
                                             // a fixed database never reshuffles a crew.
                if (!prefab)
                    continue;

                var slot = SlotAlong[i % SlotAlong.Length];
                var post = door + tangent * slot + facing * (1.1f + 0.25f * (i % 2));

                // Somebody already on the spot (a civilian mid-stroll, another crew's
                // overlap) - step out toward the kerb rather than inside a body.
                for (var nudge = 0;
                     nudge < 3 && !PedestrianRegistry.IsClear(null, post);
                     nudge++)
                    post += facing * 0.4f;

                var go = Instantiate(prefab, post, Quaternion.LookRotation(facing), transform);
                PedestrianSpawner.SetLayerRecursively(
                    go.transform, PedestrianSpawner.PedestrianLayer);
                PedestrianLodSystem.Register(go);

                var human = go.GetComponent<HumanBehavior>();
                if (!human)
                {
                    Debug.LogWarning($"[Gangs] '{prefab.name}' has no HumanBehavior - " +
                                     "re-run the asset bootstrap.", this);
                    Destroy(go);
                    continue;
                }

                var animator = go.GetComponent<Animator>();
                if (animator && prefabs.pedestrianController)
                    animator.runtimeAnimatorController = prefabs.pedestrianController;

                go.AddComponent<GangMemberAgent>().Bind(gang.Id, identity, seed, post, facing);
                spawned++;
            }

            return spawned;
        }

        /// <summary>Read-only name scan of the shipped groups - never a draw from their
        /// bags (a bag touch re-deals the whole crowd). Missing name warns and falls
        /// back to the wise-guy staple; missing even that skips the spawn.</summary>
        static GameObject FindPeoplePrefab(PrefabDatabase prefabs, string prefabName)
        {
            var found = ScanGroups(prefabs, prefabName);
            if (found)
                return found;

            Debug.LogWarning($"[Gangs] Model '{prefabName}' is not in the PrefabDatabase " +
                             "pedestrian groups - run the city asset bootstrap to refresh; " +
                             "using man-mafia_AI.");
            return ScanGroups(prefabs, "man-mafia_AI");
        }

        static GameObject ScanGroups(PrefabDatabase prefabs, string prefabName)
        {
            if (prefabs.pedestrianGroups == null)
                return null;

            foreach (var group in prefabs.pedestrianGroups)
            {
                if (group?.prefabs == null)
                    continue;

                foreach (var prefab in group.prefabs)
                    if (prefab && prefab.name == prefabName)
                        return prefab;
            }

            return null;
        }

        static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
        }
    }
}
