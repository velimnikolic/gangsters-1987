using UnityEngine;

namespace RoadDemo
{
    public static class CityDecorationSettings
    {
        const string Key = "Graphics.DecorationDensity";
        static float density = -1;
        public static float Density
        {
            get => density < 0 ? density = Mathf.Clamp01(PlayerPrefs.GetFloat(Key, 1)) : density;
            set
            {
                float next = float.IsNaN(value) ? 0 : Mathf.Clamp01(value);
                if (Density == next) return;
                density = next;
                PlayerPrefs.SetFloat(Key, density);
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => density = -1;
    }
}
