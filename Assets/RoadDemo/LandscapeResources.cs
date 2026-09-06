using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Releases runtime landscape meshes/materials when their views unload.</summary>
    public sealed class LandscapeResources : MonoBehaviour
    {
        readonly List<Object> _owned = new List<Object>();
        public Material Material(Material material) { _owned.Add(material); return material; }
        public void Mesh(GameObject view)
        {
            if (view != null && view.TryGetComponent<MeshFilter>(out var filter)) _owned.Add(filter.sharedMesh);
        }
        void OnDestroy()
        {
            foreach (var asset in _owned) if (asset != null) Destroy(asset);
            _owned.Clear();
        }
    }
}
