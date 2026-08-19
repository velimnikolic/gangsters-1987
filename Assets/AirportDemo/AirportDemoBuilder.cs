using RoadDemo;
using UnityEngine;

namespace AirportDemo
{
    // The airport's own scene: one component that stands the district up at the
    // origin and hands it a StandaloneDistrictHost for the sun, the camera, the pause
    // keys and the perf pass. The field ITSELF is AirportDistrict, the same object the
    // city builds off one of its shores (RoadDemoBuilder.Districts.cs) - so anything
    // changed here, in this scene, is what the city gets.
    //
    // The fields below are the district's own defaults, out on the inspector for
    // trying things: they are copied onto the district before it is planned.
    public class AirportDemoBuilder : MonoBehaviour
    {
        [Header("The field")]
        public int seed = 1987;
        [Tooltip("Runway length in metres. 1800 m (6,000 ft) takes the trijet; 1200 is a plain general aviation strip and the jet will not use it.")]
        public float runwayLength = AirportSpec.RunwayLength;
        [Tooltip("Which way the wind is blowing: with it westerly, runway 27 is in use and every circuit is flown to the west.")]
        public bool westerlyWind = true;
        [Tooltip("Edge lights, threshold bars, PAPI and the beacon. Off saves a hundred and thirty small renderers.")]
        public bool airfieldLighting = true;
        [Tooltip("How many of the six box hangars stand open with an aeroplane inside.")]
        [Range(0, 3)] public int openHangars = 1;

        [Header("Flying")]
        [Tooltip("Scheduled aeroplanes, one to each airline stand: they land, the passengers walk off, the next lot walk on, and they go. Half of them are already inbound when the field opens.")]
        [Range(0, 4)] public int airlineAircraft = 4;
        [Tooltip("Light singles that actually fly. Few on purpose - a county field sees a handful of light movements an hour, not one a minute.")]
        [Range(0, 6)] public int lightAircraft = 2;
        [Tooltip("Aeroplanes tied down on the general aviation ramp, going nowhere.")]
        [Range(0, 30)] public int parkedAircraft = 18;
        [Tooltip("Seconds an airline aeroplane spends off the map between its departure and its next arrival.")]
        public float commuterInterval = 220f;
        [Tooltip("Three: the sheriff's, which keeps its pad and flies a patrol; a charter that comes and goes; and an air ambulance that drops in off the country.")]
        public bool helicopters = true;

        [Header("Boarding")]
        [Tooltip("The passengers walk off an arrival and on to the departure, and the aeroplane does not start up until the last of them is up the steps.")]
        public bool boarding = true;
        [Tooltip("Bodies the turnarounds share. A passenger only exists while he is walking.")]
        [Range(0, 40)] public int boardingPool = 20;

        [Header("The ground")]
        [Tooltip("A bowser out to every aeroplane that shuts down, a baggage train to the commuter stand, a follow-me to lead an arrival in.")]
        public bool groundEquipment = true;
        [Range(0, 20)] public int rampCrew = 8;
        [Tooltip("Lorries in through the freight gate to the shed and out again.")]
        [Range(0, 4)] public int lorries = 1;

        [Header("Landside")]
        [Range(0, 40)] public int cars = 13;
        [Range(0, 120)] public int parkedCars = 60;
        [Range(0, 80)] public int passengers = 34;
        [Tooltip("A sheriff's car on the kerb and a plain sedan watching the general aviation gate - 1987, and this is how the cocaine came north.")]
        public bool theLaw = true;

        void Awake()
        {
#if UNITY_EDITOR
            // the hangars, the tower and the field furniture are baked before Play by
            // Editor/AirportDemoAutoBake
            var district = new AirportDistrict
            {
                runwayLength = runwayLength,
                westerlyWind = westerlyWind,
                airfieldLighting = airfieldLighting,
                openHangars = openHangars,
                airlineAircraft = airlineAircraft,
                lightAircraft = lightAircraft,
                parkedAircraft = parkedAircraft,
                boarding = boarding,
                boardingPool = boardingPool,
                commuterInterval = commuterInterval,
                helicopters = helicopters,
                groundEquipment = groundEquipment,
                rampCrew = rampCrew,
                lorries = lorries,
                cars = cars,
                parkedCars = parkedCars,
                passengers = passengers,
                theLaw = theLaw,
            };

            var host = gameObject.AddComponent<StandaloneDistrictHost>();
            // over the ramp looking down the field: the terminal and the tower in the
            // middle distance, the runway beyond them, the hangars off to the left
            host.cameraPivot = AirportDistrict.StandaloneWorld(new Vector3(-20f, 0f, 200f));
            host.cameraDistance = 330f;
            host.cameraYaw = 12f;
            host.cameraPitch = 32f;
            // the far end of the runway is 750 m off and the circuit goes further
            host.cameraFar = 4000f;
            host.fogRange = new Vector2(900f, 2600f);
            host.fogColour = new Color(0.70f, 0.78f, 0.86f);
            host.clearColour = new Color(0.58f, 0.70f, 0.84f);
            // late afternoon out of the south-west: the hangar fronts and the terminal
            // glass are lit, and everything on the ramp throws a shadow across it
            host.sunAngles = new Vector3(46f, -35f, 0f);
            host.sunIntensity = 1.28f;
            host.reflectionProbe = false;
            host.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                        "Space: pause   , . : slower/faster";
            host.HostSeeded(district, seed);
#else
            Debug.LogError("[AirportDemo] This demo loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }
    }
}
