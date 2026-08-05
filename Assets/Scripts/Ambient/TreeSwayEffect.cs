using UnityEngine;

namespace LivingCity.Ambient
{
    /// <summary>
    /// Gentle sway on a tree.
    ///
    /// WARNING: this moves the transform, which takes the tree OUT of static batching - the
    /// whole point of generating the city in the editor. Do not attach it to every tree.
    /// TreeSwayCoordinator applies it to a bounded subset near the camera; past that, the
    /// right fix is vertex displacement in the shader, which costs no batching at all.
    /// </summary>
    public sealed class TreeSwayEffect : MonoBehaviour
    {
        [SerializeField] float amplitude = 1.5f;
        [SerializeField] float frequency = 0.4f;

        float phase;
        Quaternion baseRotation;

        void Start()
        {
            baseRotation = transform.localRotation;

            // Randomised per tree, otherwise the whole street sways in lockstep and reads
            // as a single animated object rather than wind.
            phase = Random.Range(0f, Mathf.PI * 2f);
        }

        void Update()
        {
            var wobble = Mathf.Sin(Time.time * frequency * Mathf.PI * 2f + phase) * amplitude;
            var lean = Mathf.Cos(Time.time * frequency * Mathf.PI * 1.3f + phase) * amplitude * 0.6f;
            transform.localRotation = baseRotation * Quaternion.Euler(wobble, 0f, lean);
        }
    }
}
