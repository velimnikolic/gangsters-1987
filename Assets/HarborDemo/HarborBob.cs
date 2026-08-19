using UnityEngine;

namespace HarborDemo
{
    // The sea's small talk: a hull heaves a hand's breadth and rolls a degree on two
    // sines that never quite repeat. Lives on the MODEL child of a vessel, writing
    // its local position and rotation - the root is what the shipping moves, and the
    // two never fight because they are different transforms. Scale it up for a small
    // boat that a swell tosses, down for a freighter made fast to a quay.
    public sealed class HarborBob : MonoBehaviour
    {
        const float Heave = 0.12f;
        const float RollDeg = 0.9f;
        const float PitchDeg = 0.35f;
        const float Period = 6.5f;

        float _phase, _scale = 1f;
        Vector3 _restPos;
        Quaternion _restRot;
        bool _rested;

        public void Configure(float phaseOffset, float scale = 1f)
        {
            _phase = phaseOffset;
            _scale = scale;
        }

        /// <summary>A moored ship settles: the scale eases toward this over a few seconds.</summary>
        public float TargetScale = -1f;

        void Start()
        {
            _restPos = transform.localPosition;
            _restRot = transform.localRotation;
            _rested = true;
        }

        void Update()
        {
            if (!_rested) return;
            if (TargetScale >= 0f) _scale = Mathf.MoveTowards(_scale, TargetScale, Time.deltaTime * 0.25f);
            float t = Time.time + _phase;
            float w = Mathf.PI * 2f / Period;
            float heave = (Mathf.Sin(t * w) * 0.7f + Mathf.Sin(t * w * 1.37f + 1.3f) * 0.3f) * Heave * _scale;
            float roll = (Mathf.Sin(t * w * 0.83f + 0.7f) * 0.8f + Mathf.Sin(t * w * 1.61f) * 0.2f) * RollDeg * _scale;
            float pitch = Mathf.Sin(t * w * 0.57f + 2.1f) * PitchDeg * _scale;
            transform.localPosition = _restPos + new Vector3(0f, heave, 0f);
            transform.localRotation = _restRot * Quaternion.Euler(pitch, 0f, roll);
        }
    }
}
