using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Continue both existing urban riverbanks along the channel to its coastal mouths.</summary>
    public static class RegionalQuayView
    {
        public static void Build(IslandLandform land, Transform parent, LandscapeResources owned)
        {
            var stone = owned.Material(new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { name = "River quay stone", color = new Color(0.53f, 0.51f, 0.46f) });
            foreach (float bank in new[] { -1f, 1f }) foreach (float reach in new[] { -1f, 1f })
            {
                var points = new List<Vector3>();
                float start = reach < 0 ? land.UrbanRiver.yMin : land.UrbanRiver.yMax;
                float limit = reach < 0 ? land.Bounds.yMin : land.Bounds.yMax;
                for (float z = start; reach * (z - limit) <= 0f; z += reach * 10f)
                {
                    if (!land.RiverBanks(z, out var banks)) break;
                    float x = (bank < 0 ? banks.x : banks.y) + bank * IslandLandform.QuayWidth * 0.5f;
                    if (land.Coast(x, z) < 30f) break;
                    points.Add(new Vector3(x, 0f, z));
                }
                if (points.Count < 2) continue;
                RegionalRoadView.Ribbon(RoadLine.Through(points), -IslandLandform.QuayWidth * 0.5f,
                    IslandLandform.QuayWidth * 0.5f, 0.13f, stone, Vector2.zero, parent, owned,
                    3f - RoadDemoBuilder.WaterY);
            }
        }
    }
}
