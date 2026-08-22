using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The bomb layer's window onto the project's own particle art. The blast fireball and
    /// the fire that eats a bombed shopfront are no longer a handful of primitive quads -
    /// they are the Synty PolygonParticleFX prefabs the project already ships (FX_Explosion,
    /// FX_Fire_Big), spawned at the point that needs them.
    ///
    /// Like the rest of this editor-only demo, the prefabs come straight out of the
    /// AssetDatabase (DemoAssetLoad), so the load is cached the first time each is asked
    /// for and never pulled twice. A one-shot burst has no stop action of its own, so we
    /// hang an FxAutoKill on it to clear it once it has played; a looping fire is handed
    /// back for its caller to keep and destroy on its own clock.
    ///
    /// If the pack is absent Spawn returns null and the caller falls back to the old
    /// procedural fire, so a stripped project still shows *something* burn.
    /// </summary>
    public static class BombFx
    {
        /// <summary>The one-shot blast: a ball of fire, sparks and smoke that plays once.</summary>
        public const string Explosion = "Assets/Synty/PolygonParticleFX/Prefabs/FX_Explosion_01.prefab";

        /// <summary>The looping fire strung across a burning shopfront.</summary>
        public const string Fire = "Assets/Synty/PolygonParticleFX/Prefabs/FX_Fire_Big_01.prefab";

        /// <summary>The looping black smoke that rises off a burning shopfront.</summary>
        public const string Smoke = "Assets/Synty/PolygonParticleFX/Prefabs/FX_Smoke_Black_01.prefab";

        static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Cache.Clear();

        static GameObject Prefab(string path)
        {
            if (Cache.TryGetValue(path, out var p) && p != null) return p;
            p = DemoAssetLoad.Load<GameObject>(path);
            Cache[path] = p;
            return p;
        }

        /// <summary>Instantiate the FX prefab at <paramref name="path"/> facing
        /// <paramref name="rot"/>. <paramref name="scale"/> multiplies the prefab's own
        /// scale; <paramref name="autoKill"/> &gt; 0 clears it after that many seconds (for
        /// a one-shot), 0 leaves its life to the caller (for a looping fire). Returns null
        /// if the pack is not present, so the caller can fall back.</summary>
        public static GameObject Spawn(string path, Vector3 pos, Quaternion rot, float scale, float autoKill, Transform parent = null)
        {
            var prefab = Prefab(path);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, pos, rot, parent);
            if (!Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
            if (autoKill > 0f) go.AddComponent<FxAutoKill>().Life = autoKill;
            return go;
        }
    }

    /// <summary>Clears a one-shot particle instance once it has had time to play. Synty's
    /// bursts carry no stop action, so without this they would linger, spent, forever.</summary>
    public sealed class FxAutoKill : MonoBehaviour
    {
        public float Life = 3f;
        float _age;

        void Update()
        {
            _age += Time.deltaTime;
            if (_age >= Life) Destroy(gameObject);
        }
    }
}
