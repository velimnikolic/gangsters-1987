using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityEditor
{
    /// <summary>Retarget the owner-violence delivery to the city's humanoid walkers.</summary>
    sealed class OwnerBeatingAnimationImport : AssetPostprocessor
    {
        internal const string Root = "Assets/Animations/Mixamo/OwnerBeating/";
        bool IsOurs => assetPath.StartsWith(Root, StringComparison.Ordinal) &&
                       assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        void OnPreprocessModel()
        {
            if (!IsOurs) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = importer.importLights = importer.importBlendShapes = false;
        }

        void OnPreprocessAnimation()
        {
            if (!IsOurs) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            foreach (var clip in clips)
            {
                clip.name = name;
                clip.loopTime = name == "Crawling" || name == "Pulling A Rope";
                clip.loopPose = false;
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                // Extract travel: the shared walker owns translation through the door.
                clip.lockRootPositionXZ = false;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }
    }
}
