using UnityEngine;
using UnityEngine.InputSystem;

namespace CrewDemo
{
    // The arena's clock: Space holds the whole fight (Time.timeScale = 0), comma and
    // period step it slower and faster - so a shoot-out can be watched, frozen mid
    // shot, and looked around while it stands still. The camera runs on unscaled
    // time and never notices. This is what the Editor's own Pause button cannot
    // give: paused there, nothing runs, the camera included.
    public class CrewDemoPace : MonoBehaviour
    {
        static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f };

        int _speed = 2;
        bool _paused;

        public bool Paused => _paused;
        public float Multiplier => Speeds[_speed];

        void Update()
        {
            if (LivingCity.UI.PersonnelAlmanac.IsOpen) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.spaceKey.wasPressedThisFrame) { _paused = !_paused; Apply(); }
            if (kb.commaKey.wasPressedThisFrame && _speed > 0) { _speed--; Apply(); }
            if (kb.periodKey.wasPressedThisFrame && _speed < Speeds.Length - 1) { _speed++; Apply(); }
        }

        void Apply() => Time.timeScale = _paused ? 0f : Speeds[_speed];

        void OnDestroy()
        {
            // In the Editor Time.timeScale survives leaving Play mode - a demo
            // stopped while held would leave the next session frozen.
            Time.timeScale = 1f;
        }

        void OnGUI()
        {
            if (LivingCity.UI.PersonnelAlmanac.IsOpen) return;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight,
            };
            style.normal.textColor = _paused ? new Color(1f, 0.78f, 0.32f) : new Color(0.9f, 0.95f, 1f);
            string text = _paused ? "PAUSED  ·  Space to resume" : Multiplier + "x";
            GUI.Label(new Rect(Screen.width - 420f, 8f, 400f, 32f), text, style);
            var hint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight };
            hint.normal.textColor = new Color(0.75f, 0.82f, 0.9f);
            GUI.Label(new Rect(Screen.width - 420f, 40f, 400f, 22f),
                "Space: hold   , / . : slower / faster", hint);
        }
    }
}
