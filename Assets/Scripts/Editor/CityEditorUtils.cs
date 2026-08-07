using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>Shared editor-side helpers used by both the generator window and the one-click setup.</summary>
    internal static class CityEditorUtils
    {
        /// <summary>
        /// PrefabUtility.InstantiatePrefab - NOT Object.Instantiate - so generated objects stay
        /// linked to their source prefabs in the saved scene.
        /// </summary>
        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.SetPositionAndRotation(position, rotation);
            return instance;
        }

        /// <summary>
        /// Flags generated geometry as batching-static.
        ///
        /// Two things are skipped. Traffic lights swap emission colour through a material
        /// instance at runtime, which opts that renderer out of the batch anyway. And a factory
        /// carrying a SmokeVent must stay non-static: SmokeStackSystem parents a live
        /// ParticleSystem at that mouth, and a static-flagged ancestor is exactly how you get a
        /// plume that renders once and then never moves again.
        /// </summary>
        public static void MarkStaticForBatching(Transform root)
        {
            const StaticEditorFlags Flags =
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ContributeGI;

            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.GetComponentInParent<TrafficLightsControl>())
                    continue;

                if (child.GetComponentInParent<Ambient.SmokeVent>())
                    continue;

                GameObjectUtility.SetStaticEditorFlags(child.gameObject, Flags);
            }
        }

        /// <summary>
        /// Assigns a [SerializeField] private field by name. Going through SerializedObject is
        /// what makes private serialized fields writable from editor code.
        /// </summary>
        public static void SetField(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogWarning($"[CitySetup] {target.GetType().Name} has no serialized field '{fieldName}'.");
                return;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetBool(Object target, string fieldName, bool value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(fieldName);
            if (property == null) return;
            property.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetFloat(Object target, string fieldName, float value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(fieldName);
            if (property == null) return;
            property.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
