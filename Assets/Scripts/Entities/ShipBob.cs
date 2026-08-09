using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// The water's motion, faked on the hull: a slow heave and a slower roll, sine-summed so
    /// the loop never visibly repeats. The sea itself is a static slab - animating thousands
    /// of water vertices for two moving ships is the wrong trade - so the ships carry the
    /// motion instead, which is also where the eye actually looks for it.
    ///
    /// Sits on the MODEL child, never on the root: PortShipDirector's Glide writes the root's
    /// position absolutely every frame, and a bob composed into that same transform would
    /// either be overwritten or accumulate. The child's local offset survives both.
    /// </summary>
    public sealed class ShipBob : MonoBehaviour
    {
        [SerializeField] float heave = 0.12f;
        [SerializeField] float rollDegrees = 0.9f;
        [SerializeField] float period = 6.5f;

        float phase;

        /// <summary>Randomised by the spawner so a row of ships does not nod in unison.</summary>
        public void Configure(float phaseOffset, float scale = 1f)
        {
            phase = phaseOffset;
            heave *= scale;
            rollDegrees *= scale;
        }

        void Update()
        {
            var t = (Time.time + phase) / period * (2f * Mathf.PI);

            // Two incommensurate frequencies per axis, so the combined motion drifts rather
            // than metronomes.
            var lift = Mathf.Sin(t) * 0.6f + Mathf.Sin(t * 0.37f) * 0.4f;
            var roll = Mathf.Sin(t * 0.73f) * 0.7f + Mathf.Sin(t * 0.21f) * 0.3f;

            transform.localPosition = new Vector3(0f, lift * heave, 0f);
            transform.localRotation = Quaternion.Euler(0f, 0f, roll * rollDegrees);
        }
    }
}
