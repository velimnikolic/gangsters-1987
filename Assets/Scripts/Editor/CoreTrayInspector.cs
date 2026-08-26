using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The tray's own panel: what it is holding, what it will be filed as, and the two
    /// buttons that do the work - so composing a block is dragging things onto a rectangle
    /// and clicking, rather than dragging things onto a rectangle and then going to look
    /// for the menu that acts on it.
    ///
    /// The reading costs a sweep of the scene, and a scene of five thousand instances is
    /// not worth sweeping sixty times a second, so it is taken when the tray is selected
    /// and whenever the hierarchy changes under it - never on a repaint.
    /// </summary>
    [CustomEditor(typeof(RoadDemo.CoreTray))]
    public class CoreTrayInspector : Editor
    {
        int _pieces, _buildings;
        bool _paved;
        Vector2 _size;
        string _said;

        void OnEnable()
        {
            Read();
            EditorApplication.hierarchyChanged += Read;
        }

        void OnDisable() => EditorApplication.hierarchyChanged -= Read;

        /// <summary>The tray this panel is for. The component sits on the pad as well as
        /// on the tray, because the pad is the blue rectangle and the blue rectangle is what
        /// gets clicked; either way the panel answers for the tray itself.</summary>
        Transform Tray()
        {
            var mine = (RoadDemo.CoreTray)target;
            return mine ? CoreBlockTray.TrayOf(mine.transform) : null;
        }

        void Read()
        {
            var tray = Tray();
            if (!tray) return;
            CoreBlockTray.Holding(tray, out _pieces, out _buildings, out _paved, out _size);
        }

        public override void OnInspectorGUI()
        {
            var tray = Tray();
            if (!tray)
            {
                EditorGUILayout.HelpBox(
                    "This is not a block tray - a tray is an object with a \"pad\" under it. " +
                    "Lay one down with Tools/City/Core/Add Block Tray.", MessageType.Warning);
                return;
            }

            // the name IS the prefab's name, and two trays called the same thing write over
            // each other - worth having under the hand rather than up in the hierarchy
            EditorGUI.BeginChangeCheck();
            string named = EditorGUILayout.DelayedTextField("Block name", tray.name);
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrWhiteSpace(named) && named != tray.name)
            {
                Undo.RecordObject(tray.gameObject, "Rename block tray");
                tray.name = named.Trim();
                Read();
            }
            EditorGUILayout.LabelField(" ", $"bakes to {CoreBlockTray.PrefabPath(tray)}",
                                       EditorStyles.miniLabel);

            // the setting lives on the TRAY even when the pad is what was clicked, so there
            // is one of it and not two
            var panel = tray.GetComponent<RoadDemo.CoreTray>();
            if (panel)
            {
                EditorGUI.BeginChangeCheck();
                int tiles = EditorGUILayout.IntSlider("Pavement tiles", panel.pavementTiles, 1, 4);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(panel, "Pavement width");
                    panel.pavementTiles = tiles;
                    EditorUtility.SetDirty(panel);
                }
                EditorGUILayout.LabelField(" ", $"{panel.pavementTiles * 5} m round the buildings " +
                                                "(the pack's own blocks use 5)", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                _pieces == 0
                    ? "Nothing on the tray. Drag buildings onto the rectangle, or onto this " +
                      "object in the Hierarchy."
                    : $"{_pieces} piece(s) standing, {_buildings} of them buildings\n" +
                      $"{_size.x:F0} x {_size.y:F0} m on the ground\n" +
                      (_paved ? "paved" : "not paved yet - saving the scene will pave it"),
                _pieces == 0 ? MessageType.Info : MessageType.None);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_buildings == 0))
                if (GUILayout.Button(_paved ? "Pave again" : "Pave", GUILayout.Height(26f)))
                {
                    CoreBlockTray.PaveOne(tray, out _said);
                    Read();
                }
            EditorGUILayout.LabelField(" ",
                "the kerb, its corners and the pavement inside it, laid round the buildings " +
                "themselves - never over the rectangle", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_pieces == 0))
            {
                if (GUILayout.Button("Bake to a prefab now", GUILayout.Height(22f)))
                {
                    CoreBlockTray.BakeNow();
                    Read();
                }
                if (GUILayout.Button("Empty this tray (no bake)"))
                {
                    int gone = CoreBlockTray.EmptyOne(tray);
                    _said = gone > 0
                        ? $"{gone} piece(s) taken off - Ctrl+Z puts them back"
                        : "there was nothing on it";
                    Read();
                }
            }

            if (string.IsNullOrEmpty(_said)) return;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_said, MessageType.Info);
        }
    }
}
