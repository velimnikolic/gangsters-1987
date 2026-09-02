using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>One import contract for the Mixamo locomotion deliveries used by
    /// CoverDemo. The FBXs are animation sources only: no preview mesh, material or
    /// texture is needed at runtime, and every take is retargeted through Humanoid.
    ///
    /// Kept as a postprocessor because these packs arrive as loose FBXs. Replacing or
    /// re-downloading one must not silently turn it back into a Generic, non-looping
    /// clip and make only one body in the review scene skate or freeze.</summary>
    sealed class MixamoLocomotionImportSettings : AssetPostprocessor
    {
        internal const string Root = "Assets/Animations/Mixamo/Locomotion/";

        void OnPreprocessModel()
        {
            if (!IsOurs(assetPath)) return;

            var importer = (ModelImporter)assetImporter;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
        }

        void OnPreprocessAnimation()
        {
            if (!IsOurs(assetPath)) return;

            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;

            var file = Path.GetFileNameWithoutExtension(assetPath);
            foreach (var clip in clips)
            {
                // Mixamo calls the take inside every separate file "mixamo.com". The
                // delivery's file name is the stable, human-readable address.
                clip.name = file;
                clip.loopTime = IsLoop(file);
                clip.loopPose = false;

                // Keep the take's travel as root-motion metadata. The walkers do not
                // apply root motion, but AnimationClip.averageSpeed is their foot-rate
                // oracle, so baking XZ into the pose would discard the useful number.
                clip.lockRootHeightY = false;
                clip.keepOriginalPositionY = true;
                clip.lockRootRotation = false;
                clip.keepOriginalOrientation = true;
                clip.lockRootPositionXZ = false;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        static bool IsOurs(string path) =>
            path.StartsWith(Root, StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        internal static bool IsLoop(string file)
        {
            var lower = file.ToLowerInvariant();
            if (lower.Contains("jump") || lower.Contains("turn") ||
                lower.Contains("stand to kneel") || lower.Contains("kneel to stand"))
                return false;
            return lower.Contains("idle") || lower.Contains("walk") ||
                   lower.Contains("run") || lower.Contains("strafe");
        }

        /// <summary>The FBXs may land in the same refresh as this postprocessor and be
        /// imported before its assembly exists. Once the assembly is live, reimport
        /// only assets that still expose the raw Mixamo take or the wrong rig.</summary>
        [InitializeOnLoadMethod]
        static void ReimportStragglers()
        {
            EditorApplication.delayCall += () =>
            {
                if (!AssetDatabase.IsValidFolder(Root.TrimEnd('/'))) return;
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root.TrimEnd('/') }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!IsOurs(path) || AssetImporter.GetAtPath(path) is not ModelImporter importer)
                        continue;
                    var clips = importer.clipAnimations;
                    bool named = clips != null && clips.Length > 0 &&
                                 clips[0].name == Path.GetFileNameWithoutExtension(path);
                    if (importer.animationType == ModelImporterAnimationType.Human && named)
                        continue;
                    importer.SaveAndReimport();
                }
            };
        }
    }
}
