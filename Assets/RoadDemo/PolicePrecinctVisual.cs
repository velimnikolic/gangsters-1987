using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The authored topology of the full-detail police precinct.  This component owns no
    /// police state: rosters, dispatch, custody and booking remain in PoliceForce and
    /// PoliceDispatch.  It only names the expensive visual layers and the physical places
    /// that a review scene (or a future close-range streamed view) needs to expose.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PolicePrecinctVisual : MonoBehaviour
    {
        [Header("Visual layers")]
        [SerializeField] GameObject siteAndAccess;
        [SerializeField] GameObject undergroundGarage;
        [SerializeField] GameObject groundFloor;
        [SerializeField] GameObject upperFloor;
        [SerializeField] GameObject exteriorVehicles;
        [SerializeField] GameObject interiorLighting;

        [Header("Places")]
        [SerializeField] Transform publicEntrance;
        [SerializeField] Transform bookingDesk;
        [SerializeField] Transform holdingCells;
        [SerializeField] Transform garageRampTop;
        [SerializeField] Transform garageRampBottom;

        [Header("Parking security")]
        [SerializeField] Transform[] parkingBarrierArms = System.Array.Empty<Transform>();
        [SerializeField] Vector3 parkingBarrierLocalAxis = Vector3.forward;
        [SerializeField] float parkingBarrierLift = 75f;

        [Header("Build audit")]
        [SerializeField] int authoredPropCount;
        [SerializeField] int rendererCount;
        [SerializeField] int lightCount;

        public GameObject SiteAndAccess => siteAndAccess;
        public GameObject UndergroundGarage => undergroundGarage;
        public GameObject GroundFloor => groundFloor;
        public GameObject UpperFloor => upperFloor;
        public GameObject ExteriorVehicles => exteriorVehicles;
        public GameObject InteriorLighting => interiorLighting;

        public Transform PublicEntrance => publicEntrance;
        public Transform BookingDesk => bookingDesk;
        public Transform HoldingCells => holdingCells;
        public Transform GarageRampTop => garageRampTop;
        public Transform GarageRampBottom => garageRampBottom;

        public Transform[] ParkingBarrierArms => parkingBarrierArms;
        public Vector3 ParkingBarrierLocalAxis => parkingBarrierLocalAxis;
        public float ParkingBarrierLift => parkingBarrierLift;

        public int AuthoredPropCount => authoredPropCount;
        public int RendererCount => rendererCount;
        public int LightCount => lightCount;

        /// <summary>Editor construction seam; generated prefabs keep these references.</summary>
        public void Configure(
            GameObject site, GameObject garage, GameObject ground, GameObject upper,
            GameObject vehicles, GameObject lighting,
            Transform entrance, Transform booking, Transform cells,
            Transform rampTop, Transform rampBottom,
            Transform[] barrierArms, Vector3 barrierAxis, float barrierLift,
            int props, int renderers, int lights)
        {
            siteAndAccess = site;
            undergroundGarage = garage;
            groundFloor = ground;
            upperFloor = upper;
            exteriorVehicles = vehicles;
            interiorLighting = lighting;
            publicEntrance = entrance;
            bookingDesk = booking;
            holdingCells = cells;
            garageRampTop = rampTop;
            garageRampBottom = rampBottom;
            parkingBarrierArms = barrierArms ?? System.Array.Empty<Transform>();
            parkingBarrierLocalAxis = barrierAxis.sqrMagnitude > 0.0001f
                ? barrierAxis.normalized
                : Vector3.forward;
            parkingBarrierLift = barrierLift;
            authoredPropCount = Mathf.Max(0, props);
            rendererCount = Mathf.Max(0, renderers);
            lightCount = Mathf.Max(0, lights);
        }
    }
}
