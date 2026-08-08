using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// A building that owns a row of parking stalls and hands them out one at a time. The
    /// bookkeeping half of PoliceStation, lifted out the day the bank grew a forecourt of its
    /// own: two buildings, the same stalls, the same claim discipline, and nothing police about
    /// any of it.
    ///
    /// Everything is stored in the building's LOCAL space and transformed on demand, for the
    /// same reason BuildingDoor derives rather than caches: the city is generated in the editor
    /// and SAVED, and a world-space cache would be one more thing to go stale if the instance
    /// ever moves. Claims are runtime-only, like seat and doorstep claims - a stall saved as
    /// occupied would be a stall no car could ever use again.
    ///
    /// The field NAMES are load-bearing and must not be renamed: they were PoliceStation's
    /// before they were this class's, and every station baked into an existing scene binds its
    /// serialized data by name.
    /// </summary>
    public abstract class StallHost : MonoBehaviour
    {
        [SerializeField] Vector3[] stallLocalPositions = System.Array.Empty<Vector3>();

        [SerializeField] float stallLocalYaw;

        [System.NonSerialized] bool[] stallClaimed;

        public void SetStalls(Vector3[] stallLocals, float localYaw)
        {
            stallLocalPositions = stallLocals ?? System.Array.Empty<Vector3>();
            stallLocalYaw = localYaw;
        }

        public int StallCount => stallLocalPositions.Length;

        public Vector3 StallWorld(int stall) =>
            transform.TransformPoint(stallLocalPositions[stall]);

        /// <summary>World rotation of a car parked in a stall - nose to the street, the way
        /// ForStreetBay lays the bays out and the way the static bakes always sat.</summary>
        public Quaternion StallRotation(int stall) =>
            transform.rotation * Quaternion.Euler(0f, stallLocalYaw, 0f);

        /// <summary>Out of the building toward the street - the facade normal.</summary>
        public Vector3 Facing
        {
            get
            {
                var forward = transform.forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            }
        }

        public bool TryClaimStall(out int stall)
        {
            stallClaimed ??= new bool[StallCount];
            for (var i = 0; i < stallClaimed.Length; i++)
            {
                if (stallClaimed[i])
                    continue;

                stallClaimed[i] = true;
                stall = i;
                return true;
            }

            stall = -1;
            return false;
        }

        public void ReleaseStall(int stall)
        {
            if (stallClaimed != null && stall >= 0 && stall < stallClaimed.Length)
                stallClaimed[stall] = false;
        }
    }
}
