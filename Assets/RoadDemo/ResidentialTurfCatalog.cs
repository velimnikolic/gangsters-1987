using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    [Serializable]
    public sealed class ResidentialTurfCatalogEntry
    {
        public string Name;
        public ResidentialTurfPrefabMass[] Masses =
            Array.Empty<ResidentialTurfPrefabMass>();
    }

    /// <summary>A lightweight index copied from the baked prefab companions. Loading
    /// this one small asset during city planning does not pull any renderer, material,
    /// texture or source mesh into memory.</summary>
    public sealed class ResidentialTurfCatalog : ScriptableObject
    {
        public const string AssetPath = "Assets/Configs/ResidentialTurfCatalog.asset";

        [SerializeField] List<ResidentialTurfCatalogEntry> _entries =
            new List<ResidentialTurfCatalogEntry>();

        static ResidentialTurfCatalog _loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => _loaded = null;

        public static bool TryGet(string name, out ResidentialTurfPrefabMass[] masses)
        {
            _loaded ??= DemoAssetLoad.Load<ResidentialTurfCatalog>(AssetPath);
            if (_loaded != null)
                for (int i = 0; i < _loaded._entries.Count; i++)
                {
                    var entry = _loaded._entries[i];
                    if (entry != null && entry.Name == name)
                    {
                        masses = entry.Masses;
                        return masses != null && masses.Length > 0;
                    }
                }
            masses = null;
            return false;
        }

#if UNITY_EDITOR
        public void Replace(List<ResidentialTurfCatalogEntry> entries)
        {
            _entries = entries ?? new List<ResidentialTurfCatalogEntry>();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
