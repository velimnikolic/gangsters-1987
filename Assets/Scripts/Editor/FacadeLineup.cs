using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Stands every building the city can put on a street in one row, turned exactly the way
    /// BlockBuilder would turn it, with a road in front of them. Whatever shows a blank wall to
    /// that road is authored off the pack's convention and wants an entry in facadeYawFixes.
    ///
    /// This exists because the offline oracles cannot answer the question for a FLAT terrace
    /// piece. They answered it for the corner pieces, where two elevations are genuinely blind
    /// and the margin is 3:1 to 11:1 - but the straight pieces are modular parts meant to be seen
    /// from the street AND from the alley, so both Z faces carry real geometry and every measure
    /// of vertex density lands between 0.77:1 and 1.19:1. That is not a weak signal, it is no
    /// signal, and the pack's demo scenes hold two to four instances of each, which is not enough
    /// to break the tie either. Looking at them is the honest answer, and it is one click.
    ///
    /// Each building is named after its prefab, so clicking the one that has its back to the
    /// street names the culprit in the Hierarchy.
    /// </summary>
    public static class FacadeLineup
    {
        const string RootName = "__FacadeLineup";
        const string DatabasePath = "Assets/Configs/PrefabDatabase.asset";

        /// <summary>Gap between two pieces. Wide enough to read them apart, not to lose the row.</summary>
        const float Gap = 4f;

        /// <summary>
        /// Kerb to building line, matching what BlockRect leaves. The row is not a real block, but
        /// standing the pieces at the distance they actually stand at is free.
        /// </summary>
        const float StreetOffset = 7f;

        [MenuItem("Tools/City/Debug - Facade Lineup", priority = 3)]
        public static void Build()
        {
            var db = AssetDatabase.LoadAssetAtPath<PrefabDatabase>(DatabasePath);
            if (!db)
            {
                Debug.LogWarning("[FacadeLineup] No PrefabDatabase.asset - run " +
                                 "Tools/City/Create or Refresh Config Assets first.");
                return;
            }

            var existing = GameObject.Find(RootName);
            if (existing)
                Object.DestroyImmediate(existing);

            var buildings = StreetFacingPrefabs(db);
            if (buildings.Count == 0)
            {
                Debug.LogWarning("[FacadeLineup] No street-facing prefabs in any zone palette.");
                return;
            }

            var root = new GameObject(RootName);

            // The street runs along +X and the buildings front -Z, which is BlockBuilder's south
            // side: outward = back, along = right. Using the real vectors rather than hand-picked
            // ones is the point - if the lineup and the generator disagreed the test would be
            // worthless.
            var along = Vector3.right;
            var outward = Vector3.back;
            var yawBase = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg;

            var cursor = 0f;
            var names = new List<string>();

            foreach (var prefab in buildings)
            {
                var yaw = yawBase + db.ExtraYawFor(prefab);
                var footprint = PrefabBounds.FootprintXZ(prefab, yaw);
                var width = footprint.x;
                var depth = footprint.y;

                // Same centring as BlockBuilder.Place: front face on the building line, and the
                // pivot offset taken out so the geometry lands where the box says it does.
                var centre = new Vector3(cursor + width * 0.5f, 0f, depth * 0.5f);
                var rotation = Quaternion.Euler(0f, yaw, 0f);
                var localCentre = PrefabBounds.Get(prefab).center;
                var offset = rotation * new Vector3(localCentre.x, 0f, localCentre.z);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                instance.transform.SetPositionAndRotation(
                    new Vector3(centre.x - offset.x, 0f, centre.z - offset.z), rotation);
                instance.name = prefab.name;

                names.Add($"{prefab.name} (x {cursor:F0}..{cursor + width:F0}, " +
                          $"extraYaw {db.ExtraYawFor(prefab):F0})");

                cursor += width + Gap;
            }

            LayRoad(db, root.transform, cursor);

            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.FrameSelected();

            Debug.Log($"[FacadeLineup] {buildings.Count} pieces along +X, all fronting the road " +
                      $"at -Z. Anything showing a blank wall to the road needs a facadeYawFixes " +
                      $"entry.\n  " + string.Join("\n  ", names));
        }

        /// <summary>
        /// Every distinct prefab that can end up on a street elevation: the groups' street pieces
        /// and their corner pieces, plus the landmarks, which take the head of a street run.
        ///
        /// rearPrefabs are deliberately in as well. An alley piece is chosen for its BLANK side,
        /// so it should be the one obviously facing the road here - that is the control, and if a
        /// -back piece shows windows the rear list is what is wrong, not the yaw.
        /// </summary>
        static List<GameObject> StreetFacingPrefabs(PrefabDatabase db)
        {
            var seen = new List<GameObject>();

            void Add(IEnumerable<GameObject> list)
            {
                if (list == null)
                    return;

                foreach (var prefab in list)
                    if (prefab && !seen.Contains(prefab))
                        seen.Add(prefab);
            }

            foreach (var palette in db.zonePalettes ?? System.Array.Empty<PrefabDatabase.ZonePalette>())
            {
                if (palette?.groups == null)
                    continue;

                foreach (var group in palette.groups)
                {
                    if (group == null)
                        continue;

                    Add(group.prefabs);
                    Add(group.rearPrefabs);
                    Add(group.cornerPrefabs);
                }

                Add(palette.landmarks);
            }

            return seen;
        }

        /// <summary>
        /// A strip of road along the whole row, one tile per CellSize, laid at the same offset off
        /// the building line that BlockRect leaves.
        /// </summary>
        static void LayRoad(PrefabDatabase db, Transform parent, float length)
        {
            if (!db.straight)
                return;

            // Tile centre one sidewalkWidth off the building line - the same relation BlockRect
            // produces, where the block is expanded INTO the road tile and the two overlap.
            // Yaw 90 is the straight running east-west; see RoadTileTable, where yaw 0 is the
            // north-south one.
            for (var x = 0f; x < length + CityGrid.CellSize; x += CityGrid.CellSize)
            {
                var tile = (GameObject)PrefabUtility.InstantiatePrefab(db.straight, parent);
                tile.transform.SetPositionAndRotation(
                    new Vector3(x, 0f, -StreetOffset), Quaternion.Euler(0f, 90f, 0f));
                tile.name = "road";
            }
        }
    }
}
