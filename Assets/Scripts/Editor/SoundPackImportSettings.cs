using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Import settings for Assets/Audio, applied the moment each WAV lands.
    ///
    /// A postprocessor rather than a by-hand pass for the same reason the yard-stock
    /// texture importer is one: a reimport silently reverts hand-edited settings, and a
    /// folder that is rebuilt by a script is a folder that gets reimported. Compiled
    /// BEFORE the audio is baked, so even the very first import comes in right.
    ///
    /// The folder is already conditioned - Tools/audio/import_sounds.py cuts everything to
    /// 44.1 kHz 16 bit and folds the one-shots to mono - so the only decision left here is
    /// how each clip is held in memory, and that follows how the city plays it:
    ///
    ///   BEDS (Ambience, plus the police radio's static loop) play through 2D looping
    ///   sources - stereo is the point, and CompressedInMemory keeps half a minute of
    ///   ambience from sitting decompressed in RAM.
    ///
    ///   ONE-SHOTS and the engine loops (everything else) play through positional sources.
    ///   A 3D source collapses to mono anyway, so forcing it at import halves the memory
    ///   instead of paying it twice, and DecompressOnLoad keeps a footstep from paying a
    ///   decode latency per step.
    /// </summary>
    sealed class SoundPackImportSettings : AssetPostprocessor
    {
        internal const string AudioRoot = "Assets/Audio/";

        static readonly string[] BedFolders =
        {
            AudioRoot + "Ambience/",
        };

        /// <summary>The one bed that does not live in Ambience: dispatch static, which is
        /// a loop under a scene rather than a one-shot in it.</summary>
        const string StaticLoop = AudioRoot + "Police/radio_static.wav";

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(AudioRoot))
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
            if (path == StaticLoop)
                return true;
            foreach (var folder in BedFolders)
                if (path.StartsWith(folder))
                    return true;
            return false;
        }

        /// <summary>
        /// Re-imports any clip that came in before this class had compiled.
        ///
        /// The one refresh this postprocessor cannot cover is the refresh that delivers it:
        /// when the scripts and the WAVs land in the same batch, Unity may import the audio
        /// before the new domain exists. Vorbis is the tell - no clip this class processed
        /// can be anything else - so anything non-Vorbis gets one SaveAndReimport, which
        /// comes back through OnPreprocessAudio properly this time.
        /// </summary>
        internal static void ReimportStragglers()
        {
            if (!AssetDatabase.IsValidFolder(AudioRoot.TrimEnd('/')))
                return;

            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot.TrimEnd('/') });
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
                Debug.Log($"[SoundImport] Re-imported {fixedCount} clip(s) that arrived " +
                          "before the import settings had compiled.");
        }
    }
}
