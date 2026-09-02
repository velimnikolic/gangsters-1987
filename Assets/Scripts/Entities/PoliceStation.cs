using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Data marker on the placed police station: the forecourt stalls the patrol fleet parks
    /// in and the door the beat officers use. Attached by BlockBuilder when it places the
    /// station landmark - the station is the one landmark whose forecourt is deliberately
    /// left EMPTY of static cars, because the bays belong to the real patrol fleet.
    ///
    /// A MARKER AND NOTHING ELSE since 2026-09-02 (GAN-226, ROSTER-005). The generator's own
    /// police brain that used to read it was deleted; what still reads this component is the
    /// strategic map (which draws the station) and BlockBuilder (which attaches it). The
    /// force that actually patrols is the Game scene's - RoadDemoBuilder, PoliceDispatch,
    /// PoliceForce and the rosters in Assets/Scripts/Police.
    ///
    /// The stalls and their claim discipline live in StallHost, shared with the bank's
    /// forecourt; what stays here is the part that is actually about a police station. See
    /// StallHost for why the geometry is stored in local space and why the claims are not
    /// serialized.
    ///
    /// Deliberately NOT a BuildingDoor: the station door serves a fixed, persistent
    /// population that must RETURN here, while BuildingDoor serves the opportunistic
    /// anyone-nearby traffic - and the recessed facade behind the forecourt can fail
    /// BuildingDoorRule's road-cell test anyway, so the civic door has to carry its own
    /// coordinates rather than depend on a rule written for terrace fronts.
    /// </summary>
    public sealed class PoliceStation : StallHost
    {
        public const string PrefabName = "building-policestation";

        [SerializeField] Vector3 doorLocal;

        public void SetLayout(Vector3[] stallLocals, float localYaw, Vector3 localDoor)
        {
            SetStalls(stallLocals, localYaw);
            doorLocal = localDoor;
        }

        /// <summary>On the facade plane at ground level - where an officer vanishes inside.</summary>
        public Vector3 DoorWorld => transform.TransformPoint(doorLocal);

        /// <summary>A step out from the door, across the forecourt walkway.</summary>
        public Vector3 StandWorld => DoorWorld + Facing * 1.2f;
    }
}
