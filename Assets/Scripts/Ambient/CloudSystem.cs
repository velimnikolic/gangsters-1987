using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Ambient
{
    /// <summary>
    /// Drifting clouds. Spawned at runtime rather than baked into the scene because they
    /// move - there is nothing for static batching to gain from them.
    /// </summary>
    public sealed class CloudSystem : MonoBehaviour
    {
        [SerializeField] CityConfig config;
        [SerializeField] PrefabDatabase prefabs;

        [Header("Field")]
        [SerializeField, Min(0)] int cloudCount = 10;
        [SerializeField] Vector2 altitudeRange = new(50f, 100f);
        [SerializeField] Vector2 scaleRange = new(1.5f, 3f);
        [SerializeField] float driftSpeed = 2f;

        [Tooltip("How far past the city edge clouds travel before wrapping to the far side.")]
        [SerializeField] float margin = 150f;

        readonly List<Transform> clouds = new();
        float minX, maxX;

        void Start()
        {
            if (!config || !prefabs || prefabs.clouds == null || prefabs.clouds.Length == 0)
            {
                Debug.LogWarning("[CloudSystem] Needs a CityConfig and a PrefabDatabase with cloud prefabs.", this);
                enabled = false;
                return;
            }

            var rng = new System.Random(config.seed + SeedOffsets.Ambient);

            var width = config.WorldWidth;
            var depth = config.WorldHeight;
            minX = -margin;
            maxX = width + margin;

            for (var i = 0; i < cloudCount; i++)
            {
                var prefab = prefabs.clouds[rng.Next(prefabs.clouds.Length)];

                var position = new Vector3(
                    Mathf.Lerp(minX, maxX, (float)rng.NextDouble()),
                    Mathf.Lerp(altitudeRange.x, altitudeRange.y, (float)rng.NextDouble()),
                    Mathf.Lerp(-margin, depth + margin, (float)rng.NextDouble()));

                var cloud = Instantiate(prefab, position, Quaternion.Euler(0f, rng.Next(4) * 90f, 0f), transform);
                cloud.transform.localScale *= Mathf.Lerp(scaleRange.x, scaleRange.y, (float)rng.NextDouble());
                clouds.Add(cloud.transform);
            }
        }

        void Update()
        {
            var step = driftSpeed * Time.deltaTime;
            var span = maxX - minX;

            foreach (var cloud in clouds)
            {
                if (!cloud) continue;

                var position = cloud.position;
                position.x += step;

                if (position.x > maxX)
                    position.x -= span;

                cloud.position = position;
            }
        }
    }
}
