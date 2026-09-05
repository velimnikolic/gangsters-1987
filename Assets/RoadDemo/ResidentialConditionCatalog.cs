using UnityEngine;

namespace RoadDemo
{
    /// <summary>Authored, collider-free cosmetic assets, available in player builds too.</summary>
    public sealed class ResidentialConditionCatalog : ScriptableObject
    {
        public Shader weatherShader;
        public GameObject[] litter;
        public GameObject shutter;
        static ResidentialConditionCatalog cached;
        public static ResidentialConditionCatalog Load() => cached ? cached :
            cached = Resources.Load<ResidentialConditionCatalog>("ResidentialConditionCatalog");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => cached = null;
    }
}
