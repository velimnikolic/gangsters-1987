using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Bakes Assets/Configs/Audio/Resources/VoiceDatabase.asset out of Assets/Audio/Voice,
    /// the way SoundAssetBootstrap bakes the city's sound database out of Assets/Audio:
    /// the folder is the source of truth, the asset is what ships, and runtime never reads
    /// a path.
    ///
    /// The folder is one directory per bank (VB01..VB08, VBOF) of WAVs named for the line
    /// they carry - VOX_ORD_MOVE_01.wav. The variant suffix is split off to make the KEY,
    /// so three files become three takes of one order, and the casting notes below say who
    /// each bank sounds like. Nothing else is read: adding a tenth actor is a folder and a
    /// row in <see cref="Casting"/>.
    ///
    /// It lives in Resources because a voice is asked for in the middle of Play, by code
    /// that has no scene reference to hang a serialized field on - the same reason
    /// WeaponCatalog and the gameplay configs are there.
    /// </summary>
    public static class VoiceAssetBootstrap
    {
        const string VoiceRoot = "Assets/Audio/Voice";
        const string ConfigDir = "Assets/Configs/Audio/Resources";
        const string AssetPath = ConfigDir + "/VoiceDatabase.asset";

        /// <summary>Who each bank sounds like, and how the roll should treat it. The
        /// recordings themselves carry no such note - this is the casting sheet, and it is
        /// the one thing about the folder that a person decided rather than a script.
        /// </summary>
        struct Note
        {
            public string Actor;
            public VoiceAge Age;
            public VoiceTemper Temper;
            public bool Office;
        }

        static readonly Dictionary<string, Note> Casting = new Dictionary<string, Note>
        {
            ["VB01"] = new Note { Actor = "Tony - Tough Cocky", Age = VoiceAge.Young, Temper = VoiceTemper.Hot },
            ["VB02"] = new Note { Actor = "Syndicate - Top G", Age = VoiceAge.Young, Temper = VoiceTemper.Steady },
            ["VB03"] = new Note { Actor = "Pauly - Brooklyn Wise Guy", Age = VoiceAge.Prime, Temper = VoiceTemper.Hot },
            ["VB04"] = new Note { Actor = "Dante - Dark Mafia", Age = VoiceAge.Prime, Temper = VoiceTemper.Steady },
            ["VB05"] = new Note { Actor = "David - Deep Southern", Age = VoiceAge.Prime, Temper = VoiceTemper.Any },
            ["VB06"] = new Note { Actor = "Antonio - Grumpy Grandpa", Age = VoiceAge.Old, Temper = VoiceTemper.Any },
            ["VB07"] = new Note { Actor = "Jerry B. - NY Italian", Age = VoiceAge.Old, Temper = VoiceTemper.Steady },
            ["VB08"] = new Note { Actor = "Austin - Deep Raspy", Age = VoiceAge.Prime, Temper = VoiceTemper.Hot },
            ["VBOF"] = new Note { Actor = "Gotham Boss", Age = VoiceAge.Old, Temper = VoiceTemper.Steady, Office = true },
        };

        /// <summary>Files whose trailing number is a MEANING and not a take. The two
        /// hideout lines say opposite things, so they stay two keys - everything else
        /// numbered is the same sentence said again.</summary>
        static readonly HashSet<string> WholeKeys = new HashSet<string>
        {
            "VOX_RKT_HIDEOUT_01",
            "VOX_RKT_HIDEOUT_02",
        };

        [MenuItem("Tools/City/Create or Refresh Voice Database", priority = 4)]
        public static void CreateVoiceDatabaseMenu()
        {
            var db = CreateVoiceDatabase();
            if (db != null)
                Selection.activeObject = db;
        }

        public static VoiceDatabase CreateVoiceDatabase()
        {
            if (!AssetDatabase.IsValidFolder(VoiceRoot))
            {
                Debug.LogWarning($"[voice] no {VoiceRoot} - nothing to bake.");
                return null;
            }

            SoundPackImportSettings.ReimportStragglers();
            EnsureFolder(ConfigDir);

            var db = AssetDatabase.LoadAssetAtPath<VoiceDatabase>(AssetPath);
            if (!db)
            {
                db = ScriptableObject.CreateInstance<VoiceDatabase>();
                AssetDatabase.CreateAsset(db, AssetPath);
            }

            var banks = new List<VoiceDatabase.Bank>();
            var clips = 0;

            foreach (var dir in Directory.GetDirectories(VoiceRoot))
            {
                var id = Path.GetFileName(dir);
                var bank = ReadBank(id, dir.Replace('\\', '/'));
                if (bank == null)
                    continue;
                banks.Add(bank);
                foreach (var line in bank.lines)
                    clips += line.takes.Length;
            }

            banks.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            db.banks = banks.ToArray();
            db.Invalidate();   // the running session must not answer out of the old index

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            Debug.Log($"[voice] {banks.Count} banks, {clips} clips baked into {AssetPath}.");
            return db;
        }

        static VoiceDatabase.Bank ReadBank(string id, string dir)
        {
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { dir });
            if (guids.Length == 0)
                return null;

            // key -> takes, gathered by name so VOX_ORD_MOVE_01..03 come out one line with
            // three takes and the order of them is the order of the files.
            var byKey = new Dictionary<string, List<AudioClip>>(guids.Length);

            var paths = new List<string>(guids.Length);
            foreach (var guid in guids)
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(string.CompareOrdinal);

            foreach (var path in paths)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                    continue;

                var key = Split(Path.GetFileNameWithoutExtension(path));
                if (!byKey.TryGetValue(key, out var takes))
                    byKey[key] = takes = new List<AudioClip>(4);
                takes.Add(clip);
            }

            var lines = new List<VoiceDatabase.Line>(byKey.Count);
            foreach (var pair in byKey)
                lines.Add(new VoiceDatabase.Line { key = pair.Key, takes = pair.Value.ToArray() });
            lines.Sort((a, b) => string.CompareOrdinal(a.key, b.key));

            var bank = new VoiceDatabase.Bank { id = id, lines = lines.ToArray() };
            if (Casting.TryGetValue(id, out var note))
            {
                bank.actor = note.Actor;
                bank.age = note.Age;
                bank.temper = note.Temper;
                bank.office = note.Office;
            }
            else
            {
                // A bank nobody cast still speaks - it simply has no pull in the roll.
                bank.actor = "(uncast)";
                bank.age = VoiceAge.Prime;
                bank.temper = VoiceTemper.Any;
            }
            return bank;
        }

        /// <summary>VOX_ORD_MOVE_01 -> VOX_ORD_MOVE. The trailing number is the take, not
        /// the line - except for the handful in <see cref="WholeKeys"/>.</summary>
        static string Split(string fileName)
        {
            if (WholeKeys.Contains(fileName))
                return fileName;

            var cut = fileName.LastIndexOf('_');
            if (cut <= 0 || cut == fileName.Length - 1)
                return fileName;

            for (var i = cut + 1; i < fileName.Length; i++)
                if (!char.IsDigit(fileName[i]))
                    return fileName;

            return fileName.Substring(0, cut);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parts = path.Split('/');
            var built = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }
    }
}
