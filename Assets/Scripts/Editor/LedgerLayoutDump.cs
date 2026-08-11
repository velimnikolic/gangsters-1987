using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The bridge between hand-editing the ledger in Play mode and the code that
    /// actually owns it. The book is built procedurally, so Scene-view tweaks die
    /// with Play mode - this menu snapshots the live hierarchy (every rect, colour
    /// and text style, inactive pages included) to a timestamped text file in the
    /// project root. Dump once right after opening the book (the baseline), tweak
    /// freely, dump again; the pair of files is the diff a person - or Claude -
    /// folds back into PersonnelAlmanac's constants.
    /// </summary>
    public static class LedgerLayoutDump
    {
        const string RootName = "Personnel Ledger";

        [MenuItem("Tools/City/Dump Ledger Layout")]
        static void Dump()
        {
            var root = GameObject.Find(RootName);
            if (root == null)
            {
                EditorUtility.DisplayDialog("Dump Ledger Layout",
                    "No live ledger found. Enter Play mode (the book builds itself) " +
                    "before dumping - it does not need to be open, only built.", "OK");
                return;
            }

            var text = new StringBuilder();
            text.AppendLine("# Ledger layout dump - " +
                System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("# path | rect x,y w,h | anchors min-max pivot | visuals");
            text.AppendLine("# lines starting with ~ are on an INACTIVE branch");
            Walk(root.transform, "", false, text);

            var file = Path.Combine(Directory.GetCurrentDirectory(),
                "LedgerDump_" + System.DateTime.Now.ToString("HHmmss") + ".txt");
            File.WriteAllText(file, text.ToString());
            Debug.Log("[LedgerLayoutDump] Wrote " + file);
        }

        [MenuItem("Tools/City/Dump Ledger Layout", true)]
        static bool DumpEnabled() => EditorApplication.isPlaying;

        static void Walk(Transform node, string path, bool inactiveBranch,
            StringBuilder text)
        {
            var name = path.Length == 0 ? node.name : path + "/" + node.name;
            inactiveBranch |= !node.gameObject.activeSelf;
            text.AppendLine(Describe(node, name, inactiveBranch));

            for (var i = 0; i < node.childCount; i++)
                Walk(node.GetChild(i), name, inactiveBranch, text);
        }

        static string Describe(Transform node, string path, bool inactive)
        {
            var line = new StringBuilder();
            if (inactive)
                line.Append('~');
            line.Append(path);

            if (node is RectTransform rect)
            {
                line.Append(" | ");
                line.Append(F(rect.anchoredPosition.x)).Append(',')
                    .Append(F(rect.anchoredPosition.y)).Append(' ')
                    .Append(F(rect.sizeDelta.x)).Append('x')
                    .Append(F(rect.sizeDelta.y));
                line.Append(" | a(")
                    .Append(F(rect.anchorMin.x)).Append(',')
                    .Append(F(rect.anchorMin.y)).Append(")-(")
                    .Append(F(rect.anchorMax.x)).Append(',')
                    .Append(F(rect.anchorMax.y)).Append(") p(")
                    .Append(F(rect.pivot.x)).Append(',')
                    .Append(F(rect.pivot.y)).Append(')');
            }

            if (node.TryGetComponent<Image>(out var image))
            {
                line.Append(" | Image #")
                    .Append(ColorUtility.ToHtmlStringRGBA(image.color));
                if (image.sprite != null)
                    line.Append(' ').Append(image.sprite.name);
            }

            if (node.TryGetComponent<TMP_Text>(out var tmp))
            {
                line.Append(" | TMP ").Append(F(tmp.fontSize))
                    .Append(" #").Append(ColorUtility.ToHtmlStringRGBA(tmp.color))
                    .Append(' ').Append(tmp.fontStyle)
                    .Append(' ').Append(tmp.alignment);
                if (tmp.characterSpacing != 0f)
                    line.Append(" sp").Append(F(tmp.characterSpacing));

                var copy = tmp.text ?? "";
                copy = copy.Replace("\n", "\\n");
                if (copy.Length > 40)
                    copy = copy.Substring(0, 40) + "…";
                line.Append(" \"").Append(copy).Append('"');
            }

            return line.ToString();
        }

        static string F(float value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
