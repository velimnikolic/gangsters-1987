using UnityEngine;

namespace RoadDemo
{
    /// <summary>A lightweight, real-time frame counter for builds that request it.</summary>
    public sealed class FpsCounter : MonoBehaviour
    {
        const double SampleSeconds = 0.5;
        double _sampleStart;
        int _frames;
        string _label = "FPS ...";
        GUIStyle _style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
#if GANGSTERS_FPS_COUNTER && UNITY_STANDALONE && !UNITY_EDITOR
            var counter = new GameObject("FPS Counter");
            DontDestroyOnLoad(counter);
            counter.AddComponent<FpsCounter>();
#endif
        }

        void OnEnable() => ResetSample();

        void OnApplicationFocus(bool focused) => ResetSample();

        void ResetSample()
        {
            _sampleStart = Time.realtimeSinceStartupAsDouble;
            _frames = 0;
            _label = "FPS ...";
        }

        void Update()
        {
            _frames++;
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = now - _sampleStart;
            if (elapsed < SampleSeconds) return;

            _label = $"{_frames / elapsed:F0} FPS  |  {elapsed * 1000 / _frames:F1} ms";
            _sampleStart = now;
            _frames = 0;
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                _style.normal.textColor = Color.white;
            }

            float scale = Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            var rect = new Rect(Screen.width - 240f * scale, Screen.height - 44f * scale,
                230f * scale, 34f * scale);
            _style.fontSize = Mathf.RoundToInt(18f * scale);
            var previousColor = GUI.color;
            var previousMatrix = GUI.matrix;
            int previousDepth = GUI.depth;
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = -10000;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, _label, _style);
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
            GUI.depth = previousDepth;
        }
    }
}
