using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Import settings for the 400 Sounds Pack, applied the moment each WAV lands.
    ///
    /// A postprocessor rather than a by-hand pass for the same reason the yard-stock texture
    /// importer is one: a reimport silently reverts hand-edited settings, and 400 files is
    /// 400 chances to not notice. Compiled BEFORE the pack is extracted, so even the very
    /// first import comes in right.
    ///
    /// The split is by how the city plays each folder, not by what it contains:
    ///
    ///   BEDS (Environment, Musical Effects) play through 2D looping sources - stereo is the
    ///   point, and CompressedInMemory keeps a minute of ambience from sitting decompressed
    ///   in RAM.
    ///
    ///   ONE-SHOTS (everything else) play through the positional pool - a 3D source collapses
    ///   to mono anyway, so forcing it at import halves the memory instead of paying it twice,
    ///   and DecompressOnLoad keeps a footstep from paying a decode latency per step.
    /// </summary>
    sealed class SoundPackImportSettings : AssetPostprocessor
    {
        internal const string PackRoot = "Assets/ci/400 Sounds Pack/";

        /// <summary>Loops synthesized for this project (engine idles). Same regime as the
        /// pack's one-shots: they play through 3D sources, so mono.</summary>
        internal const string GeneratedRoot = "Assets/ci/Generated Sounds/";

        static readonly string[] BedFolders =
        {
            PackRoot + "Environment/",
            PackRoot + "Musical Effects/",
        };

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(PackRoot) && !assetPath.StartsWith(GeneratedRoot))
                return;

            var importer = (AudioImporter)assetImporter;
            var isBed = IsBed(assetPath);

            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.35f;
            settings.loadType = isBed
                ? AudioClipLoadType.CompressedInMemory
                : AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = settings;

            importer.forceToMono = !isBed;
            importer.loadInBackground = isBed;
        }

        static bool IsBed(string path)
        {
            foreach (var folder in BedFolders)
                if (path.StartsWith(folder))
                    return true;
            return false;
        }

        /// <summary>
        /// Re-imports any pack clip that came in before this class had compiled.
        ///
        /// The one refresh this postprocessor cannot cover is the refresh that delivers it:
        /// when the scripts and the WAVs land in the same batch, Unity may import the audio
        /// before the new domain exists. Vorbis is the tell - no clip this class processed
        /// can be anything else - so anything non-Vorbis gets one SaveAndReimport, which
        /// comes back through OnPreprocessAudio properly this time.
        /// </summary>
        internal static void ReimportStragglers()
        {
            var guids = AssetDatabase.FindAssets("t:AudioClip",
                new[] { PackRoot.TrimEnd('/'), GeneratedRoot.TrimEnd('/') });
            var fixedCount = 0;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not AudioImporter importer)
                    continue;

                if (importer.defaultSampleSettings.compressionFormat
                    == AudioCompressionFormat.Vorbis)
                    continue;

                importer.SaveAndReimport();
                fixedCount++;
            }

            if (fixedCount > 0)
                Debug.Log($"[SoundImport] Re-imported {fixedCount} pack clip(s) that " +
                          "arrived before the import settings had compiled.");
        }
    }
}
