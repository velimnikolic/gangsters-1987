using UnityEngine;

namespace RoadDemo
{
    /// <summary>Generated references keep Shader.Find calls available in a Player.</summary>
    public sealed class PlayerShaderLibrary : ScriptableObject
    {
        public const string ResourceName = "CoreDemoShaders";
        public Shader[] shaders;
        static PlayerShaderLibrary _loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Load()
        {
#if !UNITY_EDITOR
            _loaded = Resources.Load<PlayerShaderLibrary>(ResourceName);
            if (_loaded == null)
                throw new System.InvalidOperationException("CoreDemo shader library is missing; rebuild with CoreDemoReleaseBuilder.");
            Debug.Log($"[CoreDemo shaders] {_loaded.shaders.Length} procedural shaders loaded.");
#endif
        }
    }
}
