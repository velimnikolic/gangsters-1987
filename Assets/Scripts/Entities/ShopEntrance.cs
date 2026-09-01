using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Data marker on a placed shopfront: where its street door is. Attached by
    /// InteractionMarkers to the commercial buildings whose facade faces the pavement -
    /// corner pieces are excluded there, because a corner piece's forward aligns a quadrant,
    /// not a face, and its "door" would be inside a wall.
    ///
    /// The door is derived, not authored: for every placed non-corner building the street
    /// facade is the local +Z plane, so the door sits at the facade's centre at ground
    /// level. Same persistence story as BenchSeats.
    /// </summary>
    public sealed class ShopEntrance : MonoBehaviour
    {
        [SerializeField] Vector3 doorOffset;
        [SerializeField] float frontage;

        public void SetDoor(Vector3 localDoor) => doorOffset = localDoor;

        /// <summary>How wide this shop's front is, in world metres, when whoever derived
        /// the door measured it - 0 when nobody did. It rides on the marker rather than
        /// in a table beside it so that a block streamed out takes its measurement with
        /// it instead of leaving a dead entry behind.</summary>
        public float Frontage => frontage;

        public void SetFrontage(float metres) => frontage = metres;

        /// <summary>On the facade plane, at ground level.</summary>
        public Vector3 DoorWorld => transform.TransformPoint(doorOffset);

        /// <summary>A step out from the door, where a visitor pauses before going in.</summary>
        public Vector3 StandWorld => DoorWorld + Facing * 1.1f;

        /// <summary>Out of the shop toward the street - the facade normal.</summary>
        public Vector3 Facing
        {
            get
            {
                var forward = transform.forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            }
        }
    }
}
