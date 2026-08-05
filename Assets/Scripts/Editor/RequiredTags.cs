using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The polyperfect Epic City prefabs and scripts reference tags that a fresh
    /// project does not define. CompareTag() against an unregistered tag throws
    /// UnityException, so every CarBehavior / HumanBehavior trigger contact would
    /// except on contact with a traffic light, crosswalk or another car.
    ///
    /// This runs on load and adds any missing tag. It is idempotent, and it exists
    /// as code rather than a hand-edit of ProjectSettings/TagManager.asset so the
    /// requirement survives the file being overwritten or the project being cloned.
    /// </summary>
    [InitializeOnLoad]
    internal static class RequiredTags
    {
        // Referenced by CarBehavior.OnTriggerEnter/Exit, HumanBehavior.OnTriggerEnter/Exit,
        // and set on prefabs across Traffic_T, Vehicles_T, People_T and the tile meshes.
        static readonly string[] Tags =
        {
            "TrafficLight",
            "TrafficLightCrosswalk",
            "Crosswalk",
            "Car",
            "Human",
            "Train",
            "Terrain",
            "LevelCrossing",
        };

        static RequiredTags()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
                return;

            var tagManager = new SerializedObject(assets[0]);
            var tagsProp = tagManager.FindProperty("tags");
            if (tagsProp == null)
                return;

            var added = 0;
            foreach (var tag in Tags)
            {
                if (HasTag(tagsProp, tag))
                    continue;

                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
                added++;
            }

            if (added == 0)
                return;

            tagManager.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[RequiredTags] Registered {added} missing tag(s) required by the Epic City package.");
        }

        static bool HasTag(SerializedProperty tagsProp, string tag)
        {
            for (var i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag)
                    return true;
            }
            return false;
        }
    }
}
