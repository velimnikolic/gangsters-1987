using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Data marker on a placed building: the street door people come out of and go in through.
    /// The city's population no longer just walks - it enters and leaves the buildings that
    /// line the street, and this is the pair of coordinates that makes that possible.
    ///
    /// Deliberately NOT a widening of <see cref="ShopEntrance"/>. A shopfront is a place to
    /// pop into and come back out of at the same spot; a door is a way IN and OUT of the city
    /// itself, and a pedestrian who goes in one may come out of another one streets away. The
    /// two live side by side: a cafe carries both, and its shop routine is untouched.
    ///
    /// The offset is derived, not authored, exactly as ShopEntrance's is: every non-corner
    /// building is placed with its street facade on local +Z (BlockBuilder.YawFor builds the
    /// rotation out of the outward normal), so the door is the facade centre at ground level.
    /// Which buildings qualify at all is BuildingDoorRule's question, not this component's.
    ///
    /// Same persistence story as BenchSeats: the city is generated in the editor and SAVED, so
    /// the marker has to ride on the instance rather than live in a list built at generation
    /// time. Occupancy is runtime-only for the same reason a saved seat claim would be a bug -
    /// a door saved as busy is a door nobody could ever use again.
    /// </summary>
    public sealed class BuildingDoor : MonoBehaviour
    {
        [SerializeField] Vector3 doorOffset;

        /// <summary>
        /// The doorstep, not the interior. Held only while somebody is walking up to the door
        /// or stepping back out of it - a few seconds - so that two pedestrians never converge
        /// on the same threshold. Whoever is INSIDE does not hold it: the door has to stay
        /// usable while they are away, or a long visit would padlock the building.
        /// </summary>
        [System.NonSerialized] bool stepBusy;

        /// <summary>How many are inside right now. Diagnostic only; nothing gates on it.</summary>
        [System.NonSerialized] int occupants;

        public void SetDoor(Vector3 localDoor) => doorOffset = localDoor;

        /// <summary>On the facade plane, at ground level - where a walker vanishes inside.</summary>
        public Vector3 DoorWorld => transform.TransformPoint(doorOffset);

        /// <summary>A step out onto the pavement, where somebody pauses before going in.</summary>
        public Vector3 StandWorld => DoorWorld + Facing * 1.1f;

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

        public bool StepFree => !stepBusy;

        public int Occupants => occupants;

        public bool TryClaimStep()
        {
            if (stepBusy)
                return false;

            stepBusy = true;
            return true;
        }

        public void ReleaseStep() => stepBusy = false;

        public void Entered() => occupants++;

        public void Left()
        {
            if (occupants > 0)
                occupants--;
        }
    }
}
