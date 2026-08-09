using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Data marker on the placed school: the door its pupils use, the parents' bays in front of
    /// it, and the one long bay the school bus lives in. Attached by BlockBuilder when it places
    /// the school landmark, and the only thing SchoolBusDirector has to find in a saved scene.
    ///
    /// It is a StallHost, which it was not until the school got a forecourt. The old reasoning -
    /// "the school has no landmarkCars, so PlaceLandmark leaves it flush against the street wall
    /// and there is nothing here to park in" - was true and was the whole problem: with nowhere
    /// to stand, the school bus spent its entire life stopped in a live lane, and the class
    /// comment on SchoolBusAgent had to explain at length why that was acceptable. The School
    /// palette now carries landmarkCars, so the building is recessed a stall depth plus a
    /// walkway and the frontage it vacates is the yard.
    ///
    /// The BUS bay is not one of the StallHost stalls, and deliberately so. Those are a uniform
    /// row of StallWidth x StallDepth bays that any car may claim; the bus bay is a single
    /// 11.5m berth laid the other way round, owned by one vehicle that is never dispatched
    /// anywhere else. Putting it in the same list would mean every claimant had to ask how big
    /// its stall was.
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
    public sealed class SchoolMarker : StallHost, UI.IOverlaySubject
    {
        public const string PrefabName = "building-school";

        [SerializeField] Vector3 doorLocal;

        [SerializeField] Vector3 busStallLocal;

        [SerializeField] float busStallLocalYaw;

        /// <summary>
        /// False on a school that got no bus bay - either because the survey found the frontage
        /// already occupied, or because the recessed placement failed and BlockBuilder fell back
        /// to standing the school flush. SchoolBusDirector reads this and falls back to the old
        /// behaviour, a bus resting at the kerb, rather than parking it inside the building.
        /// </summary>
        [SerializeField] bool hasBusStall;

        public void SetDoor(Vector3 localDoor) => doorLocal = localDoor;

        public void SetBusStall(Vector3 localCentre, float localYaw)
        {
            busStallLocal = localCentre;
            busStallLocalYaw = localYaw;
            hasBusStall = true;
        }

        public bool HasBusStall => hasBusStall;

        /// <summary>Middle of the bus's berth, at ground level.</summary>
        public Vector3 BusStallWorld => transform.TransformPoint(busStallLocal);

        /// <summary>
        /// Which way the bus stands in its berth. The bay runs parallel to the street, so there
        /// are two answers and the geometry cannot pick between them - a bus must face the way
        /// the traffic beside it runs, and the lane direction is not known until a road waypoint
        /// is found at runtime. <paramref name="flip"/> is the caller's answer to that.
        /// </summary>
        public Quaternion BusStallRotation(bool flip) =>
            transform.rotation * Quaternion.Euler(0f, busStallLocalYaw + (flip ? 180f : 0f), 0f);

        /// <summary>Straight out of the berth, the way the bus drives off. Follows the flip.</summary>
        public Vector3 BusStallOut(bool flip) => BusStallRotation(flip) * Vector3.forward;

        /// <summary>On the facade plane at ground level - where a pupil vanishes inside.</summary>
        public Vector3 DoorWorld => transform.TransformPoint(doorLocal);

        /// <summary>A step out from the door, onto the walkway. The station's 1.2 rather than
        /// BuildingDoor's 1.1 now that there IS a forecourt walkway to step onto.</summary>
        public Vector3 StandWorld => DoorWorld + Facing * 1.2f;

        /// <summary>Along the facade, left to right. The axis pupils fan out on so that a whole
        /// class does not walk into the same square metre of doorstep.</summary>
        public Vector3 Tangent => Vector3.Cross(Vector3.up, Facing);

        // --------------------------------------------------------------- the overlay

        /// <summary>
        /// The director, handed over once it has found this marker. Null on a seed where the
        /// school run stood down - no bus prefab, no usable stops, no children - which is a real
        /// case rather than a defect, and the popup says so instead of saying nothing.
        /// </summary>
        SchoolBusDirector director;

        float markerHeight = -1f;

        public void BindDirector(SchoolBusDirector owner) => director = owner;

        void OnEnable() => UI.OverlayRegistry.Register(this);

        void OnDisable() => UI.OverlayRegistry.Unregister(this);

        Transform UI.IOverlaySubject.OverlayAnchor => transform;

        float UI.IOverlaySubject.OverlayHeight
        {
            get
            {
                // Once, on first read: the renderer sweep allocates and the school is not going
                // to change height. Not in OnEnable, because the marker is registered from a
                // saved scene and the renderers' world bounds are only meaningful after the
                // transform has settled.
                if (markerHeight < 0f)
                    markerHeight = UI.BuildingMarker.HeightFor(transform);

                return markerHeight;
            }
        }

        bool UI.IOverlaySubject.OverlayHidden => false;
        UI.OverlayShape UI.IOverlaySubject.MarkerShape => UI.OverlayShape.Square;
        Color UI.IOverlaySubject.OverlayColor => UI.IntentionPalette.Place;
        string UI.IOverlaySubject.OverlayTitle => UI.SchoolIntention.SchoolTitle();

        string UI.IOverlaySubject.OverlayLine
        {
            get
            {
                Census(out var waiting, out var inSchool, out var travelling);
                var bus = director ? director.Bus : null;
                return UI.SchoolIntention.SchoolLine(
                    waiting, inSchool, travelling,
                    bus, bus ? bus.CurrentState : default);
            }
        }

        long UI.IOverlaySubject.OverlayKey
        {
            get
            {
                Census(out var waiting, out var inSchool, out var travelling);
                var bus = director ? director.Bus : null;
                return ((long)(bus ? (int)bus.CurrentState + 1 : 0) << 32)
                     | (uint)(waiting * 10_000 + inSchool * 100 + travelling);
            }
        }

        /// <summary>
        /// Where this school's pupils are right now. Walked rather than cached: the roster is
        /// eight children, this runs only for the SELECTED building and only when the popup key
        /// changes, and a cache would be one more thing to invalidate from six call sites.
        /// </summary>
        void Census(out int waiting, out int inSchool, out int travelling)
        {
            waiting = inSchool = travelling = 0;
            if (!director)
                return;

            var roster = director.Roster;
            for (var i = 0; i < roster.Count; i++)
            {
                var child = roster[i];
                if (!child)
                    continue;

                switch (child.CurrentState)
                {
                    case SchoolChildAgent.State.Waiting: waiting++; break;
                    case SchoolChildAgent.State.InSchool: inSchool++; break;
                    default: travelling++; break;
                }
            }
        }
    }
}
