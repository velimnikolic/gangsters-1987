using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    /// <summary>Jump between matching places in the two sets while retaining the view angle and zoom.</summary>
    public sealed class ResidentialComparisonView : MonoBehaviour
    {
        public DemoCamera rig;
        public Vector3 offset;
        public float splitX;
        public Bounds allBounds;
        public Transform[] setLabels;
        GUIStyle _hint;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (!rig || keyboard == null) return;
            if (keyboard.tabKey.wasPressedThisFrame)
                rig.pivot += rig.pivot.x >= splitX ? -offset : offset;
            if (keyboard.homeKey.wasPressedThisFrame)
            {
                rig.pivot = new Vector3(allBounds.center.x, 0, allBounds.center.z);
                rig.FrameSpan(Mathf.Max(allBounds.size.x, allBounds.size.z), 1.5f);
            }
        }

        void LateUpdate()
        {
            if (!rig || setLabels == null) return;
            foreach (var label in setLabels)
                if (label) label.rotation = rig.transform.rotation;
        }

        void OnGUI()
        {
            if (!rig) return;
            if (_hint == null) _hint = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18, alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 12, 7, 7)
            };
            string side = rig.pivot.x >= splitX ? "NEGLECTED" : "NORMAL";
            GUI.Box(new Rect(12, 12, Mathf.Min(Screen.width - 24, 1000), 68),
                side + "  |  TAB: compare same location  |  HOME: both sets\n" +
                "WASD: move   Q/E or right drag: rotate   Wheel: zoom", _hint);
        }
    }
}
