using UnityEngine;

namespace RoadDemo
{
    /// <summary>Seated roots and usable headroom authored with the cabin geometry.</summary>
    public sealed class VehicleSeatRig : MonoBehaviour
    {
        public Vector3 frontLeft, frontRight, rearLeft, rearRight;
        public Vector3[] additionalSeats = System.Array.Empty<Vector3>();
        public float[] seatYaw = System.Array.Empty<float>();
        public Vector4[] cabinPlanes = System.Array.Empty<Vector4>();
        public float ceiling = 1.3f;
        public const float CushionAboveRoot = 0.43f;

        public float YawAt(int index) => seatYaw != null && index >= 0 && index < seatYaw.Length
            ? seatYaw[index] : 0f;

        /// <summary>Fit an evaluated sitting pose once. Imported humanoids do not all
        /// put their hips the same distance above the animated root.</summary>
        public bool FitSeated(Transform sitter, Animator animator, Vector3 seat)
        {
            if (!sitter || !animator || !animator.isHuman) return false;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (!hips || !head) return false;
            var hip = transform.InverseTransformPoint(hips.position);
            var headAt = transform.InverseTransformPoint(head.position);
            var cushion = seat + Vector3.up * CushionAboveRoot;
            // Use the avatar's axes, not the head bone's import-dependent axes.
            float sy = transform.InverseTransformVector(sitter.TransformVector(Vector3.up)).magnitude;
            float sx = transform.InverseTransformVector(sitter.TransformVector(Vector3.right)).magnitude;
            float sz = transform.InverseTransformVector(sitter.TransformVector(Vector3.forward)).magnitude;
            float torso = headAt.y - hip.y;
            if (torso <= 0.1f) return false;
            float crown = .27f * sy;
            var delta = headAt - hip;
            // Horizontal envelope also covers turning to aim and a sitting loop's
            // modest lean. The sloping rear glass participates like the roof.
            float radius = .20f * Mathf.Max(sx, sz);
            SeatedHeadShape.Measure(sitter, transform, headAt, ref crown, ref radius);
            radius += new Vector2(delta.x, delta.z).magnitude + .07f;
            float factor = Mathf.Min(1f, (ceiling - cushion.y - .045f) / (torso + crown));
            if (cabinPlanes != null)
                foreach (var plane in cabinPlanes)
                {
                    float support = plane.y * torso + Mathf.Max(0f, plane.y) * crown +
                        (Mathf.Abs(plane.x) + Mathf.Abs(plane.z)) * radius;
                    float room = -(plane.x * cushion.x + plane.y * cushion.y + plane.z * cushion.z + plane.w) - .045f;
                    if (support > .001f) factor = Mathf.Min(factor, room / support);
                }
            if (factor <= 0f) return false;
            sitter.localScale *= factor;
            // Scale about the avatar root, then place its actual hips on the cushion.
            sitter.position += transform.TransformPoint(cushion) - hips.position;
            return true;
        }
    }
}
