using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Data marker on the placed school: the door its pupils use, and the stretch of facade
    /// they queue along to get through it. Attached by BlockBuilder when it places the school
    /// landmark, and the only thing SchoolBusDirector has to find in a saved scene.
    ///
    /// Deliberately NOT a StallHost. The school has no landmarkCars, so PlaceLandmark leaves
    /// it FLUSH against the street wall with no forecourt and no bays - the bus stops at the
    /// kerb in the lane, the way a bus actually does, and there is nothing here to park in.
    /// That flushness is also why the door needs no walkway offset: the pavement starts at
    /// the facade.
    ///
    /// Deliberately NOT a BuildingDoor either, for PoliceStation's reason: BuildingDoor serves
    /// the opportunistic anyone-nearby traffic and is granted by BuildingDoorRule, which can
    /// decline. The school door serves a fixed, persistent population that must ARRIVE here,
    /// so it carries its own coordinates rather than depending on a rule written for terrace
    /// fronts.
    ///
    /// Stored in the building's LOCAL space and transformed on demand, for the reason spelled
    /// out in StallHost: the city is generated in the editor and SAVED, and a world-space
    /// cache is one more thing to go stale if the instance ever moves.
    /// </summary>
    public sealed class SchoolMarker : MonoBehaviour
    {
        public const string PrefabName = "building-school";

        [SerializeField] Vector3 doorLocal;

        public void SetDoor(Vector3 localDoor) => doorLocal = localDoor;

        /// <summary>On the facade plane at ground level - where a pupil vanishes inside.</summary>
        public Vector3 DoorWorld => transform.TransformPoint(doorLocal);

        /// <summary>A step out from the door, onto the pavement. Matches BuildingDoor's 1.1
        /// rather than the station's 1.2: there is no forecourt walkway to cross.</summary>
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

        /// <summary>Along the facade, left to right. The axis pupils fan out on so that a whole
        /// class does not walk into the same square metre of doorstep.</summary>
        public Vector3 Tangent => Vector3.Cross(Vector3.up, Facing);
    }
}
