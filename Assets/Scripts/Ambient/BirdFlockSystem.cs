using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Ambient
{
    /// <summary>
    /// Birds crossing the sky.
    ///
    /// The plan allowed for scaled quads if the package had no bird models. It does: Low Poly
    /// Animated People ships "- Particles/birds.prefab", an authored flock particle effect
    /// with its own sprite sheet. Driving that along a path is both cheaper and better looking
    /// than simulating individual boids at this camera distance.
    /// </summary>
    public sealed class BirdFlockSystem : MonoBehaviour
    {
        [SerializeField] CityConfig config;

        [Tooltip("polyperfect/Low Poly Animated People/- Particles/birds.prefab")]
        [SerializeField] GameObject birdFlockPrefab;

        [Header("Flight")]
        [SerializeField, Min(1)] int flockCount = 3;
        [SerializeField] Vector2 altitudeRange = new(35f, 60f);
        [SerializeField] Vector2 speedRange = new(6f, 12f);

        [Tooltip("Seconds a flock waits offscreen before making another pass.")]
        [SerializeField] Vector2 restRange = new(8f, 25f);

        [SerializeField] float margin = 120f;

        void Start()
        {
            if (!config || !birdFlockPrefab)
            {
                Debug.LogWarning("[BirdFlockSystem] Assign a CityConfig and the birds particle prefab.", this);
                enabled = false;
                return;
            }

            var rng = new System.Random(config.seed + SeedOffsets.Ambient + 1);

            for (var i = 0; i < flockCount; i++)
            {
                var flock = Instantiate(birdFlockPrefab, Vector3.zero, Quaternion.identity, transform);
                StartCoroutine(FlyRoutine(flock.transform, new System.Random(rng.Next())));
            }
        }

        IEnumerator FlyRoutine(Transform flock, System.Random rng)
        {
            var width = config.WorldWidth;
            var depth = config.WorldHeight;

            while (true)
            {
                // Cross the city on a random heading, entering and leaving well offscreen.
                var eastbound = rng.Next(2) == 0;
                var altitude = Mathf.Lerp(altitudeRange.x, altitudeRange.y, (float)rng.NextDouble());
                var z = Mathf.Lerp(-margin * 0.3f, depth + margin * 0.3f, (float)rng.NextDouble());
                var speed = Mathf.Lerp(speedRange.x, speedRange.y, (float)rng.NextDouble());

                var start = new Vector3(eastbound ? -margin : width + margin, altitude, z);
                var end = new Vector3(eastbound ? width + margin : -margin, altitude, z);

                flock.SetPositionAndRotation(start, Quaternion.LookRotation(end - start));
                flock.gameObject.SetActive(true);

                var travelled = 0f;
                var distance = Vector3.Distance(start, end);

                while (travelled < distance)
                {
                    var step = speed * Time.deltaTime;
                    travelled += step;
                    flock.position += flock.forward * step;
                    yield return null;
                }

                flock.gameObject.SetActive(false);
                yield return new WaitForSeconds(Mathf.Lerp(restRange.x, restRange.y, (float)rng.NextDouble()));
            }
        }
    }
}
