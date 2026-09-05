using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    [RequireComponent(typeof(DemoCamera))]
    public sealed class ResidentialConditionDemo : MonoBehaviour
    {
        public GameObject[] blocks;
        [Range(0, 1)] public float neglect;
        public Bounds allBounds;
        readonly List<ResidentialConditionView> views = new List<ResidentialConditionView>();
        ResidentialPrefabPool pool;
        int cursor;
        void Start()
        {
            pool = new ResidentialPrefabPool(transform);
            pool.SetRetainedLimit(800);
            if (blocks != null)
                for (int i = 0; i < blocks.Length; i++)
                    if (blocks[i]) views.Add(new ResidentialConditionView(blocks[i].transform, 198700 + i, pool, true));
            CityConditionHud.Ensure(GetComponent<DemoCamera>(), this);
        }
        void Update()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int idle = 0, left = 192;
            while (views.Count > 0 && left-- > 0 && idle < views.Count && watch.Elapsed.TotalMilliseconds < 2)
            {
                cursor %= views.Count;
                var view = views[cursor++];
                if (view.Step(neglect, CityDecorationSettings.Density)) idle = 0;
                else idle++;
            }
            pool?.PrewarmStep(1, 1);
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.homeKey.wasPressedThisFrame && !LivingCity.UI.ModalGate.Any)
            {
                var rig = GetComponent<DemoCamera>();
                rig.pivot = new Vector3(allBounds.center.x, 0, allBounds.center.z);
                rig.FrameSpan(Mathf.Max(allBounds.size.x, allBounds.size.z), 1.1f);
            }
        }
        void OnDestroy()
        {
            foreach (var view in views) view.Dispose();
            views.Clear();
            pool?.Dispose(); pool = null;
        }
    }
}
