using UnityEngine;

namespace RoadDemo
{
    /// <summary>Seated roots and usable headroom authored with the cabin geometry.</summary>
    public sealed class VehicleSeatRig : MonoBehaviour
    {
        public Vector3 frontLeft, frontRight, rearLeft, rearRight;
        public float ceiling = 1.3f;
        public const float CushionAboveRoot = 0.43f;

        /// <summary>Fit an evaluated sitting pose once. Imported humanoids do not all
        /// put their hips the same distance above the animated root.</summary>
        public void FitSeated(Transform sitter, Animator animator, Vector3 seat)
        {
            if (!sitter || !animator || !animator.isHuman) return;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (!hips || !head) return;
            var hip = transform.InverseTransformPoint(hips.position);
            // Head bone is below the crown; reserve space for hair and loop motion.
            var crown = transform.InverseTransformPoint(head.TransformPoint(Vector3.up * 0.24f));
            var cushion = seat + Vector3.up * CushionAboveRoot;
            float torso = crown.y - hip.y;
            float room = ceiling - cushion.y - 0.035f;
            if (torso > 0.1f && room > 0.1f)
                sitter.localScale *= Mathf.Min(1f, room / torso);
            // Scale about the avatar root, then place its actual hips on the cushion.
            sitter.position += transform.TransformPoint(cushion) - hips.position;
        }
    }
}
