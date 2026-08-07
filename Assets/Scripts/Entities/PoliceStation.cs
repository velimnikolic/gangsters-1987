using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Data marker on the placed police station: the forecourt stalls the patrol fleet parks
    /// in and the door the beat officers use. Attached by BlockBuilder when it places the
    /// station landmark - the station is the one landmark whose forecourt is deliberately
    /// left EMPTY of static cars, because the bays belong to the four real patrol cars that
    /// PoliceDirector spawns and owns.
    ///
    /// Everything is stored in the station's LOCAL space and transformed on demand, for the
    /// same reason BuildingDoor derives rather than caches: the city is generated in the
    /// editor and SAVED, and a world-space cache would be one more thing to go stale if the
    /// instance ever moves. Claims are runtime-only, like seat and doorstep claims - a stall
    /// saved as occupied would be a stall no car could ever use again.
    ///
    /// Deliberately NOT a BuildingDoor: the station door serves a fixed, persistent
    /// population that must RETURN here, while BuildingDoor serves the opportunistic
    /// anyone-nearby traffic - and the recessed facade behind the forecourt can fail
    /// BuildingDoorRule's road-cell test anyway, so the civic door has to carry its own
    /// coordinates rather than depend on a rule written for terrace fronts.
    /// </summary>
    public sealed class PoliceStation : MonoBehaviour
    {
        public const string PrefabName = "building-policestation";

        [SerializeField] Vector3[] stallLocalPositions = System.Array.Empty<Vector3>();

        [SerializeField] float stallLocalYaw;

        [SerializeField] Vector3 doorLocal;

        [System.NonSerialized] bool[] stallClaimed;

        public void SetLayout(Vector3[] stallLocals, float localYaw, Vector3 localDoor)
        {
            stallLocalPositions = stallLocals ?? System.Array.Empty<Vector3>();
            stallLocalYaw = localYaw;
            doorLocal = localDoor;
        }

        public int StallCount => stallLocalPositions.Length;

        public Vector3 StallWorld(int stall) =>
            transform.TransformPoint(stallLocalPositions[stall]);

        /// <summary>World rotation of a car parked in a stall - nose to the street, the way
        /// ForStreetBay lays the bays out and the way the static bakes always sat.</summary>
        public Quaternion StallRotation(int stall) =>
            transform.rotation * Quaternion.Euler(0f, stallLocalYaw, 0f);

        /// <summary>On the facade plane at ground level - where an officer vanishes inside.</summary>
        public Vector3 DoorWorld => transform.TransformPoint(doorLocal);

        /// <summary>A step out from the door, across the forecourt walkway.</summary>
        public Vector3 StandWorld => DoorWorld + Facing * 1.2f;

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
