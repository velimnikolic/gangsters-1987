using UnityEngine;

namespace AirportDemo
{
    // Every dimension the airport is laid out to, in one place, so the kit-bash that
    // bakes the buildings at build time and the builder that lays the field at Play
    // are working off the same numbers.
    //
    // The field is a 1987 American COUNTY airport: one runway long enough for the
    // turboprop that works the scheduled runs, a full-length parallel taxiway, a
    // continuous ramp behind it, a row of box hangars and a maintenance shop at the
    // west end for the light aeroplanes, the FBO and its fuel island, the terminal and
    // the control tower in the middle, the fire station, the freight shed and the fuel
    // farm at the east end, the perimeter fence, and the landside - kerb loop, car
    // park, approach road - beyond it.
    //
    // It is deliberately NOT an international: 4,000 ft of runway and a hundred feet
    // of width is what the county built when the airlines came, and it is a third of
    // the ground a 6,000 ft trijet field takes. On the city map that difference is the
    // whole point - the field used to eat an entire shore.
    //
    // Geometry follows FAA AC 150/5300-13 for Airplane Design Group II (wingspans of
    // 49 to 79 ft, which is what a Dash 8 and a King Air are) and approach category B;
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
        /// <summary>The jet. A county field does not have one on the schedule - it sees
        /// one when a charter or a company aeroplane comes in, and only if the runway is
        /// long enough for it (<see cref="JetRunwayMin"/>).</summary>
        public const float JetSpan = 33f;
        public const float JetLength = 47f;
        public const float JetHeight = 10.5f;
        /// <summary>The shortest runway a loaded jet will use out of a field like this.
        /// Below it the schedule is turboprops and the jets go to the city's field -
        /// which at 1,200 m is exactly what this one is.</summary>
        public const float JetRunwayMin = 1500f;
        /// <summary>The light helicopter on the pad: rotor diameter, and the fuselage.</summary>
        public const float HeliRotor = 10f;
        public const float HeliLength = 11.5f;

        /// <summary>The biggest thing that uses a stand - what the ramp, the stands and
        /// the taxiway clearances are sized against.</summary>
        public const float BiggestSpan = JetSpan;
        public const float BiggestLength = JetLength;

        // ------------------------------------------------------------ runway
        /// <summary>4,000 ft: what the county lengthened the strip to when the airline
        /// began the scheduled run, and all a loaded turboprop wants. A jet needs half
        /// as much again (<see cref="JetRunwayMin"/>), which is why one is a visitor
        /// here and not a timetable.</summary>
        public const float RunwayLength = 1200f;
        /// <summary>100 ft - ADG II.</summary>
        public const float RunwayWidth = 30f;
        public const float RunwayHalfWidth = RunwayWidth * 0.5f;
        /// <summary>Paved shoulder outside the runway edge, 10 ft.</summary>
        public const float RunwayShoulder = 3f;
        /// <summary>Half the runway's length: the thresholds sit at -/+ this.</summary>
        public const float RunwayHalf = RunwayLength * 0.5f;
        /// <summary>Runway safety area half width (300 ft wide RSA, ADG II): kept
        /// clear of everything but frangible lights.</summary>
        public const float SafetyHalf = 45f;
        /// <summary>Grass beyond each threshold and out to the map's edge. The field is
        /// this much wider than its runway either side, and nothing else decides how
        /// much of a shore it takes (<see cref="MapX1"/>).</summary>
        public const float EndGrass = 110f;

        // runway markings (AC 150/5340-1, visual runway)
        /// <summary>Threshold bar: on a 100 ft runway, eight stripes 80 ft long and
        /// 5.75 ft wide, in two groups of four either side of the centreline.</summary>
        public const int ThresholdStripes = 8;
        public const float ThresholdStripeLength = 24f;
        public const float ThresholdStripeWidth = 1.75f;
        public const float ThresholdStripeGap = 1.75f;
        /// <summary>The wider gap over the centreline between the two groups.</summary>
        public const float ThresholdCentreGap = 3.5f;
        /// <summary>The threshold bar set back from the paved end.</summary>
        public const float ThresholdOffset = 5f;
        /// <summary>Runway designator ("09" / "27"), 40 ft figures.</summary>
        public const float DesignatorHeight = 12f;
        public const float DesignatorStroke = 2f;      // the pen the figures are drawn with
        public const float DesignatorOffset = 40f;     // threshold to the figures' near edge
        /// <summary>Centreline: 100 ft stripe, 65 ft gap, 3 ft wide.</summary>
        public const float CentrelineStripe = 30f;
        public const float CentrelineGap = 20f;
        public const float CentrelineWidth = 0.9f;
        /// <summary>Aiming point: a pair of bars 100 x 15 ft, 1,000 ft in.</summary>
        public const float AimingPointFrom = 300f;
        public const float AimingBarLength = 30f;
        public const float AimingBarWidth = 4.5f;
        /// <summary>Inner edge from the centreline: 57 ft between the two bars on a
        /// runway this wide, so the pair sits 8.5 to 13 m out.</summary>
        public const float AimingBarInner = 8.5f;
        /// <summary>Runway side stripe, 3 ft wide, its outer edge on the pavement edge.</summary>
        public const float EdgeStripeWidth = 0.9f;
        /// <summary>How much of each end carries amber edge lights instead of white -
        /// the runway shortening ahead of the pilot. A quarter of the runway either
        /// end; on 1,200 m that is the last 300.</summary>
        public const float AmberZone = RunwayLength * 0.25f;

        // ------------------------------------------------------------ taxiways
        /// <summary>Taxiway A: parallel, full length, 50 ft wide (ADG II), its
        /// centreline 345 ft from the runway's.</summary>
        public const float TaxiwayZ = 105f;
        public const float TaxiwayWidth = 15f;
        public const float TaxiwayHalf = TaxiwayWidth * 0.5f;
        public const float TaxiwayShoulder = 3f;
        public const float TaxiCentrelineWidth = 0.15f;
        /// <summary>Runway holding position, 200 ft from the runway centreline.</summary>
        public const float HoldShortZ = 60f;
        /// <summary>The holding position marking: two solid then two dashed 6 in lines,
        /// the solid pair on the side the aircraft holds.</summary>
        public const float HoldBarWidth = 0.15f;
        public const float HoldBarGap = 0.15f;
        /// <summary>Taxiway object free area half width, ADG II - nothing stands in
        /// it, so no stand may begin before TaxiwayZ plus this.</summary>
        public const float TaxiObjectFreeHalf = 26f;
        /// <summary>Where the connectors leave the runway: the two ends and one at the
        /// middle, abeam the terminal, so a landing rolls out onto whichever is nearest.
        /// Three is what 4,000 ft of runway is given; the four-exit field was the
        /// 6,000 ft one.</summary>
        public static readonly float[] ConnectorX = { -570f, 0f, 570f };
        public static readonly string[] ConnectorName = { "A1", "A2", "A3" };

        // ------------------------------------------------------------ apron
        /// <summary>The ramp: one slab of concrete behind the taxiway, from the west
        /// hangar line to the freight shed.</summary>
        public const float ApronZ0 = 145f;
        public const float ApronZ1 = 260f;
        public const float ApronX0 = -360f;
        public const float ApronX1 = 210f;
        /// <summary>Where aircraft may actually stand: clear of the taxiway's object
        /// free area, whose edge is 26 m from its centreline.</summary>
        public const float StandZ0 = 165f;
        /// <summary>Apron entrance taxilanes off the parallel taxiway.</summary>
        public static readonly float[] ApronEntryX = { -280f, -120f, 60f, 180f };
        public const float TaxilaneWidth = 15f;

        // tie-downs on the general aviation ramp - light aeroplanes only
        public const float TieDownX0 = -330f;
        public const float TieDownX1 = -195f;
        /// <summary>Wingtip to wingtip: a light single is 11 m across, so 15 m of pitch
        /// leaves 4 m between two of them standing side by side.</summary>
        public const float TieDownPitch = 15f;
        public const float TieDownRowPitch = 20f;   // nose to tail plus a walkway
        public const int TieDownRows = 3;
        public const float TieDownRowZ0 = 173f;

        // the airline stands, nose in to the terminal. Four of them: fifty metres of
        // pitch leaves twenty-three between two turboprops' wingtips, which is what a
        // county field allowed itself, and four stands is what makes the ramp look
        // worked rather than visited.
        public static readonly float[] CommuterStandX = { -75f, -25f, 25f, 75f };
        /// <summary>Where an airliner's nosewheel stops on its stand.</summary>
        public const float CommuterStandZ = 241f;
        /// <summary>The gate door a stand's passengers walk to and from, in the
        /// terminal's apron wall. The outer stands are wider apart than the building,
        /// so their walk is a diagonal to the end door - which is what it was.</summary>
        public static float GateDoorX(int stand)
        {
            float x = CommuterStandX[Mathf.Clamp(stand, 0, CommuterStandX.Length - 1)];
            float limit = TerminalWidth * 0.5f - 8f;
            return Mathf.Clamp(x, -limit, limit);
        }
        public const float GateDoorZ = BuildingFrontZ - 2f;
        /// <summary>The helipad's centre, at the east end of the ramp.</summary>
        public const float HelipadX = 140f;
        public const float HelipadZ = 195f;
        public const float HelipadHalf = 4.5f;
        public const float HelipadCircle = 18f;

        // ------------------------------------------------------------ buildings
        /// <summary>The airside service road, between the ramp and the buildings.</summary>
        public const float ServiceRoadZ = 267f;
        public const float ServiceRoadWidth = 6f;
        /// <summary>The building line: every apron-facing wall stands here.</summary>
        public const float BuildingFrontZ = 275f;

        // box hangar row (west). Five sheds, not six: a county field's T-hangar line is
        // short, and the sixth was only ever there to fill a ramp that was too long.
        public const int Hangars = 5;
        public const float HangarWidth = 21f;       // 7 metal modules of 3 m
        public const float HangarDepth = 20f;       // two 10 m roof spans
        public const float HangarHeight = 6f;       // two courses of 3 m wall
        public const float HangarDoorWidth = 18f;   // three 6 m sliding leaves
        public const float HangarPitch = 27f;
        public const float HangarRowX0 = -320f;

        public const float MaintHangarX = -130f;
        public const float MaintHangarWidth = 36f;
        public const float MaintHangarDepth = 26f;
        public const float MaintHangarDoorWidth = 30f;

        public const float FboX = -70f;
        public const float FboWidth = 20f;
        public const float FboDepth = 12f;
        /// <summary>The avgas island, out on the ramp in front of the FBO.</summary>
        public const float FuelIslandX = -70f;
        public const float FuelIslandZ = 235f;

        public const float TerminalX = 0f;
        /// <summary>Wide enough to front the airline stands and their gate doors.</summary>
        public const float TerminalWidth = 100f;
        public const float TerminalDepth = 30f;

        public const float TowerX = 62f;
        public const float TowerZ = 287f;
        public const float TowerBase = 12f;
        public const int TowerStoreys = 6;
        public const float StoreyHeight = 3.01f;

        public const float ArffX = 105f;
        public const float ArffWidth = 18f;
        public const float ArffDepth = 15f;

        public const float CargoX = 165f;
        public const float CargoWidth = 30f;
        public const float CargoDepth = 20f;

        public const float FuelFarmX = 225f;
        public const float FuelFarmZ = 281f;

        // ------------------------------------------------------------ fence, landside
        /// <summary>The wire: behind the buildings, tying into the terminal's back wall,
        /// which is the boundary itself - as it is at any small field.</summary>
        public const float FenceZ = 310f;
        public const float FenceHeight = 2.93f;     // the police pack's panel
        public const float FenceModule = 2.5f;
        public const float FenceX0 = -400f;
        public const float FenceX1 = 280f;
        /// <summary>Where the wire turns south down each flank of the field. Outside the
        /// runway safety area, which nothing but a frangible light may stand in.</summary>
        public const float FenceSouthZ = -120f;

        /// <summary>The two gates through the wire: general aviation (west, past the
        /// hangar line) and freight (east, by the shed).</summary>
        public const float GaGateX = -350f;
        /// <summary>Between the freight shed's east wall (x 180) and the tank farm's
        /// bund (x 210): a gate road that ran up against the bollards would have a
        /// lorry turning in with two metres to spare.</summary>
        public const float CargoGateX = 197f;
        public const float GateHalf = 5f;

        /// <summary>The kerb loop in front of the terminal: one-way, anticlockwise.</summary>
        public const float KerbZ = 321f;            // the drop-off kerb line
        public const float LoopRoadZ = 327f;        // the near leg's centre
        public const float LoopBackZ = 351f;        // the return leg's centre
        public const float LoopHalfX = 90f;
        public const float LoopRoadHalf = 5f;

        public const float ParkX0 = -140f;
        public const float ParkX1 = 120f;
        public const float ParkZ0 = 363f;
        public const float ParkZ1 = 401f;
        /// <summary>A car park bay, 9 x 18 ft.</summary>
        public const float BayWidth = 2.7f;
        public const float BayDepth = 5.4f;
        public const float ParkAisle = 6.5f;

        /// <summary>The approach road, StreetKit's own street, running out of the map.</summary>
        public const float StreetZ = 415f;
        public const float StreetX0 = -380f;
        public const float StreetX1 = 380f;
        /// <summary>Where the terminal loop meets the street.</summary>
        public const float ApproachX = 0f;

        // ------------------------------------------------------------ the field
        /// <summary>The field's own ground, and with it how much shore the city has to
        /// hand over (CityLayout.AirportFlank reads these): the runway, and
        /// <see cref="EndGrass"/> of grass past each threshold.</summary>
        public const float MapX1 = RunwayHalf + EndGrass;
        public const float MapX0 = -MapX1;
        public const float MapZ0 = -170f;
        public const float MapZ1 = 440f;

        /// <summary>Where the windsock and its segmented circle stand: between the
        /// runway and the taxiway, outside the safety area, out of everybody's way and
        /// seen from the whole field.</summary>
        public const float WindsockX = -180f;
        public const float WindsockZ = 62f;
        public const float SegmentedCircleRadius = 12f;

        /// <summary>PAPI: four boxes on the left of the runway as the pilot sees it on
        /// approach to 09 - which is the north side - abeam the aiming point.</summary>
        public const float PapiZ = 26f;
        public const float PapiFromThreshold = 300f;
        public const float PapiBoxPitch = 4.5f;

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
        public const float PatternWidth = 550f;
        public const float FinalLength = 1400f;
        public const float ClimbAngle = 6f;         // degrees
        public const float DescentAngle = 3f;

        // ------------------------------------------------------------ the turnaround
        //
        // What happens on the stand, which is the whole point of an airline aeroplane:
        // it lands, it is met, the passengers walk off across the concrete, the next
        // lot walk out and up the steps, and it goes. In 1987 at a county field there
        // is no airbridge and no bus - the walk IS the boarding.

        /// <summary>Seats, which is how many people walk off and how many walk on.
        /// A trijet out of a field like this is half empty; a turboprop is not.</summary>
        public const int JetSeats = 24;
        public const int CommuterSeats = 15;
        /// <summary>Engines stopped to the first passenger on the steps.</summary>
        public const float DoorsToFirstOff = 14f;
        /// <summary>One passenger down the steps every so many seconds - a queue on a
        /// set of airstairs moves about this fast.</summary>
        public const float DisembarkGap = 1.5f;
        /// <summary>The last one off to the first one on: the aeroplane is cleaned,
        /// the bags are changed over, the bowser comes and goes.</summary>
        public const float TurnaroundGap = 45f;
        public const float BoardingGap = 2.2f;
        /// <summary>The last one up the steps to the start-up.</summary>
        public const float DoorsToStartUp = 22f;
        /// <summary>How long a light aeroplane sits between its own movements. A field
        /// like this sees a handful of light movements an hour, not one a minute -
        /// which is why only a couple of the singles on the ramp ever fly.</summary>
        public const float LightGroundMin = 260f;
        public const float LightGroundMax = 620f;
        /// <summary>Where the boarding door is, by class: out to the left of the
        /// centreline, forward along the fuselage as a fraction of the nose, and its
        /// sill above the concrete.</summary>
        public static void Door(Aircraft.Kind kind, out float side, out float fore, out float height)
        {
            switch (kind)
            {
                case Aircraft.Kind.Jet: side = 1.9f; fore = 0.55f; height = 2.6f; break;
                case Aircraft.Kind.Commuter: side = 1.5f; fore = 0.45f; height = 1.6f; break;
                default: side = 0.7f; fore = 0.15f; height = 0.6f; break;
            }
        }

        /// <summary>The threshold an aircraft lands over, for the heading in use.</summary>
        public static float ThresholdX(bool westerly) => westerly ? RunwayHalf : -RunwayHalf;
        /// <summary>The end it leaves the ground at.</summary>
        public static float DepartureX(bool westerly) => westerly ? -RunwayHalf : RunwayHalf;
        /// <summary>Runway 27 is flown to the west, runway 09 to the east.</summary>
        public static float RunwayHeading(bool westerly) => westerly ? 270f : 90f;
    }
}
