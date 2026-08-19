using UnityEngine;

namespace AirportDemo
{
    // Every dimension the airport is laid out to, in one place, so the kit-bash that
    // bakes the buildings at build time and the builder that lays the field at Play
    // are working off the same numbers.
    //
    // The field is a 1987 American regional airport: one runway long and wide enough
    // for the trijet that brings the morning flight in, a full-length parallel
    // taxiway, a continuous ramp behind it, a row of box hangars and a maintenance
    // shop at the west end for the light aeroplanes, the FBO and its fuel island, the
    // terminal and the control tower in the middle, the fire station, the freight
    // shed and the fuel farm at the east end, the perimeter fence, and the landside -
    // kerb loop, car park, approach road - beyond it.
    //
    // Geometry follows FAA AC 150/5300-13 for Airplane Design Group III (wingspans of
    // 79 to 118 ft, which is what a 727 and a Dash 8 are) and approach category C;
    // the marking dimensions are the runway markings circular's, AC 150/5340-1.
    //
    // Axes: the runway lies along X with its centreline at z = 0 and its west
    // threshold at -RunwayHalf; everything airside grows toward +Z. Ground is y = 0.
    public static class AirportSpec
    {
        // ------------------------------------------------------------ levels
        /// <summary>The grass. Everything paved sits a little over it, so a pavement
        /// edge is a step and not a fight between two coplanar meshes.</summary>
        public const float LandY = 0f;
        public const float PaveY = 0.06f;
        /// <summary>Painted markings, a finger over the pavement they lie on.</summary>
        public const float MarkY = PaveY + 0.012f;
        /// <summary>The lights and the reflectors: on the pavement, not in it.</summary>
        public const float LightY = PaveY + 0.02f;

        // ------------------------------------------------------------ the fleet
        //
        // Three classes work the field, and every one of them is a Simple Airport
        // model scaled at Play to the span below: that pack's import scale is not
        // this project's, and an aeroplane is recognised by its size against the
        // runway before anything else about it.

        /// <summary>The light singles on the tie-down rows - a Skyhawk, near enough.</summary>
        public const float GaSpan = 11f;
        public const float GaLength = 8.3f;
        public const float GaHeight = 2.7f;
        /// <summary>The commuter turboprop that works the scheduled runs: a Dash 8, an
        /// F27, whatever the local airline had in 1987.</summary>
        public const float CommuterSpan = 27f;
        public const float CommuterLength = 25f;
        public const float CommuterHeight = 7.5f;
        /// <summary>The trijet: the one aeroplane a field this size sees that needs
        /// the whole runway. A 727 is 108 ft across and 153 ft long.</summary>
        public const float JetSpan = 33f;
        public const float JetLength = 47f;
        public const float JetHeight = 10.5f;
        /// <summary>The light helicopter on the pad: rotor diameter, and the fuselage.</summary>
        public const float HeliRotor = 10f;
        public const float HeliLength = 11.5f;

        /// <summary>The biggest thing that uses a stand - what the ramp, the stands and
        /// the taxiway clearances are sized against.</summary>
        public const float BiggestSpan = JetSpan;
        public const float BiggestLength = JetLength;

        // ------------------------------------------------------------ runway
        /// <summary>6,000 ft: what a loaded trijet wants out of a regional field on a
        /// warm day, and what those fields were lengthened to when the jets came.</summary>
        public const float RunwayLength = 1800f;
        /// <summary>150 ft - ADG III.</summary>
        public const float RunwayWidth = 45f;
        public const float RunwayHalfWidth = RunwayWidth * 0.5f;
        /// <summary>Paved shoulder outside the runway edge, 25 ft.</summary>
        public const float RunwayShoulder = 7.5f;
        /// <summary>Half the runway's length: the thresholds sit at -/+ this.</summary>
        public const float RunwayHalf = RunwayLength * 0.5f;
        /// <summary>Runway safety area half width (500 ft wide RSA, ADG III): kept
        /// clear of everything but frangible lights.</summary>
        public const float SafetyHalf = 76f;

        // runway markings (AC 150/5340-1, visual runway)
        /// <summary>Threshold bar: on a 150 ft runway, twelve stripes 150 ft long and
        /// 5.75 ft wide, in two groups of six either side of the centreline.</summary>
        public const int ThresholdStripes = 12;
        public const float ThresholdStripeLength = 45f;
        public const float ThresholdStripeWidth = 1.75f;
        public const float ThresholdStripeGap = 1.75f;
        /// <summary>The wider gap over the centreline between the two groups.</summary>
        public const float ThresholdCentreGap = 3.5f;
        /// <summary>The threshold bar set back from the paved end.</summary>
        public const float ThresholdOffset = 6f;
        /// <summary>Runway designator ("09" / "27"), 60 ft figures.</summary>
        public const float DesignatorHeight = 18f;
        public const float DesignatorStroke = 3f;      // the pen the figures are drawn with
        public const float DesignatorOffset = 62f;     // threshold to the figures' near edge
        /// <summary>Centreline: 120 ft stripe, 80 ft gap, 3 ft wide.</summary>
        public const float CentrelineStripe = 36f;
        public const float CentrelineGap = 24f;
        public const float CentrelineWidth = 0.9f;
        /// <summary>Aiming point: a pair of bars 150 x 20 ft, 1,000 ft in.</summary>
        public const float AimingPointFrom = 300f;
        public const float AimingBarLength = 45f;
        public const float AimingBarWidth = 6f;
        /// <summary>Inner edge from the centreline: 72 ft between the two bars on a
        /// runway this wide, so the pair sits 11.25 to 17.25 m out.</summary>
        public const float AimingBarInner = 11.25f;
        /// <summary>Runway side stripe, 3 ft wide, its outer edge on the pavement edge.</summary>
        public const float EdgeStripeWidth = 0.9f;

        // ------------------------------------------------------------ taxiways
        /// <summary>Taxiway A: parallel, full length, 60 ft wide (ADG III), its
        /// centreline 400 ft from the runway's.</summary>
        public const float TaxiwayZ = 122f;
        public const float TaxiwayWidth = 18f;
        public const float TaxiwayHalf = TaxiwayWidth * 0.5f;
        public const float TaxiwayShoulder = 7.5f;
        public const float TaxiCentrelineWidth = 0.15f;
        /// <summary>Runway holding position, 250 ft from the runway centreline.</summary>
        public const float HoldShortZ = 76f;
        /// <summary>The holding position marking: two solid then two dashed 6 in lines,
        /// the solid pair on the side the aircraft holds.</summary>
        public const float HoldBarWidth = 0.15f;
        public const float HoldBarGap = 0.15f;
        /// <summary>Taxiway object free area half width, ADG III - nothing stands in
        /// it, so no stand may begin before TaxiwayZ plus this.</summary>
        public const float TaxiObjectFreeHalf = 40f;
        /// <summary>Where the connectors leave the runway: the two ends and two in the
        /// middle, so a landing rolls out onto whichever is nearest.</summary>
        public static readonly float[] ConnectorX = { -860f, -290f, 290f, 860f };
        public static readonly string[] ConnectorName = { "A1", "A2", "A3", "A4" };

        // ------------------------------------------------------------ apron
        /// <summary>The ramp: one slab of concrete behind the taxiway, from the west
        /// hangar line to the freight shed.</summary>
        public const float ApronZ0 = 150f;
        public const float ApronZ1 = 265f;
        public const float ApronX0 = -580f;
        public const float ApronX1 = 350f;
        /// <summary>Where aircraft may actually stand: clear of the taxiway's object
        /// free area, whose edge is 40 m from its centreline.</summary>
        public const float StandZ0 = 170f;
        /// <summary>Apron entrance taxilanes off the parallel taxiway.</summary>
        public static readonly float[] ApronEntryX = { -470f, -80f, 80f, 260f };
        public const float TaxilaneWidth = 18f;

        // tie-downs on the general aviation ramp - light aeroplanes only
        public const float TieDownX0 = -540f;
        public const float TieDownX1 = -375f;
        /// <summary>Wingtip to wingtip: a light single is 11 m across, so 15 m of pitch
        /// leaves 4 m between two of them standing side by side.</summary>
        public const float TieDownPitch = 15f;
        public const float TieDownRowPitch = 20f;   // nose to tail plus a walkway
        public const int TieDownRows = 3;
        public const float TieDownRowZ0 = 178f;

        // the airline stands, nose in to the terminal
        public static readonly float[] CommuterStandX = { -45f, 45f };
        /// <summary>Where an airliner's nosewheel stops on its stand.</summary>
        public const float CommuterStandZ = 246f;
        /// <summary>The helipad's centre, at the east end of the ramp.</summary>
        public const float HelipadX = 150f;
        public const float HelipadZ = 200f;
        public const float HelipadHalf = 4.5f;
        public const float HelipadCircle = 18f;

        // ------------------------------------------------------------ buildings
        /// <summary>The airside service road, between the ramp and the buildings.</summary>
        public const float ServiceRoadZ = 272f;
        public const float ServiceRoadWidth = 6f;
        /// <summary>The building line: every apron-facing wall stands here.</summary>
        public const float BuildingFrontZ = 280f;

        // box hangar row (west)
        public const int Hangars = 6;
        public const float HangarWidth = 21f;       // 7 metal modules of 3 m
        public const float HangarDepth = 20f;       // two 10 m roof spans
        public const float HangarHeight = 6f;       // two courses of 3 m wall
        public const float HangarDoorWidth = 18f;   // three 6 m sliding leaves
        public const float HangarPitch = 27f;
        public const float HangarRowX0 = -520f;

        public const float MaintHangarX = -300f;
        public const float MaintHangarWidth = 36f;
        public const float MaintHangarDepth = 26f;
        public const float MaintHangarDoorWidth = 30f;

        public const float FboX = -220f;
        public const float FboWidth = 20f;
        public const float FboDepth = 12f;
        /// <summary>The avgas island, out on the ramp in front of the FBO.</summary>
        public const float FuelIslandX = -220f;
        public const float FuelIslandZ = 240f;

        public const float TerminalX = 0f;
        /// <summary>Wide enough to front two airline stands and their gate doors.</summary>
        public const float TerminalWidth = 80f;
        public const float TerminalDepth = 30f;

        public const float TowerX = 70f;
        public const float TowerZ = 292f;
        public const float TowerBase = 12f;
        public const int TowerStoreys = 6;
        public const float StoreyHeight = 3.01f;

        public const float ArffX = 130f;
        public const float ArffWidth = 18f;
        public const float ArffDepth = 15f;

        public const float CargoX = 230f;
        public const float CargoWidth = 30f;
        public const float CargoDepth = 20f;

        public const float FuelFarmX = 312f;
        public const float FuelFarmZ = 286f;

        // ------------------------------------------------------------ fence, landside
        /// <summary>The wire: behind the buildings, tying into the terminal's back wall,
        /// which is the boundary itself - as it is at any small field.</summary>
        public const float FenceZ = 315f;
        public const float FenceHeight = 2.93f;     // the police pack's panel
        public const float FenceModule = 2.5f;
        public const float FenceX0 = -600f;
        public const float FenceX1 = 380f;
        /// <summary>Where the wire turns south down each flank of the field.</summary>
        public const float FenceSouthZ = -90f;

        /// <summary>The two gates through the wire: general aviation (west, by the
        /// hangars) and freight (east, by the shed).</summary>
        public const float GaGateX = -345f;
        public const float CargoGateX = 275f;
        public const float GateHalf = 5f;

        /// <summary>The kerb loop in front of the terminal: one-way, anticlockwise.</summary>
        public const float KerbZ = 326f;            // the drop-off kerb line
        public const float LoopRoadZ = 332f;        // the near leg's centre
        public const float LoopBackZ = 356f;        // the return leg's centre
        public const float LoopHalfX = 110f;
        public const float LoopRoadHalf = 5f;

        public const float ParkX0 = -190f;
        public const float ParkX1 = 170f;
        public const float ParkZ0 = 368f;
        public const float ParkZ1 = 412f;
        /// <summary>A car park bay, 9 x 18 ft.</summary>
        public const float BayWidth = 2.7f;
        public const float BayDepth = 5.4f;
        public const float ParkAisle = 6.5f;

        /// <summary>The approach road, StreetKit's own street, running out of the map.</summary>
        public const float StreetZ = 432f;
        public const float StreetX0 = -420f;
        public const float StreetX1 = 420f;
        /// <summary>Where the terminal loop meets the street.</summary>
        public const float ApproachX = 0f;

        // ------------------------------------------------------------ the field
        public const float MapX0 = -1050f;
        public const float MapX1 = 1050f;
        public const float MapZ0 = -300f;
        public const float MapZ1 = 500f;

        /// <summary>Where the windsock and its segmented circle stand: between the
        /// runway and the taxiway, out of everybody's way, seen from the whole field.</summary>
        public const float WindsockX = -180f;
        public const float WindsockZ = 96f;
        public const float SegmentedCircleRadius = 15f;

        /// <summary>PAPI: four boxes on the left of the runway as the pilot sees it on
        /// approach to 09 - which is the north side - abeam the aiming point.</summary>
        public const float PapiZ = 34f;
        public const float PapiFromThreshold = 300f;
        public const float PapiBoxPitch = 6f;

        // ------------------------------------------------------------ speeds
        /// <summary>Taxi: 15 kt on the straight, walking pace round a corner. The same
        /// for everybody - a trijet taxis no faster than a Cessna.</summary>
        public const float TaxiSpeed = 8f;
        public const float TaxiTurnSpeed = 3.5f;
        /// <summary>Rotate, climb and approach speeds by class: a light single leaves
        /// the ground at 60 kt and a trijet at 140, and that difference is most of what
        /// tells them apart from the terminal window.</summary>
        public const float GaRotate = 30f, GaClimb = 40f, GaApproach = 28f;
        public const float CommuterRotate = 48f, CommuterClimb = 62f, CommuterApproach = 45f;
        public const float JetRotate = 72f, JetClimb = 95f, JetApproach = 68f;
        /// <summary>What the machinery falls back on when nobody said.</summary>
        public const float RotateSpeed = GaRotate;
        public const float ClimbSpeed = GaClimb;
        public const float ApproachSpeed = GaApproach;
        /// <summary>Circuit height above the field, brought down a little so a light
        /// aeroplane going round stays in frame with the runway.</summary>
        public const float PatternAltitude = 220f;
        /// <summary>The downwind leg, from the runway centreline.</summary>
        public const float PatternWidth = 700f;
        public const float FinalLength = 1700f;
        public const float ClimbAngle = 6f;         // degrees
        public const float DescentAngle = 3f;

        /// <summary>The threshold an aircraft lands over, for the heading in use.</summary>
        public static float ThresholdX(bool westerly) => westerly ? RunwayHalf : -RunwayHalf;
        /// <summary>The end it leaves the ground at.</summary>
        public static float DepartureX(bool westerly) => westerly ? -RunwayHalf : RunwayHalf;
        /// <summary>Runway 27 is flown to the west, runway 09 to the east.</summary>
        public static float RunwayHeading(bool westerly) => westerly ? 270f : 90f;
    }
}
