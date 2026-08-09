using UnityEngine;

namespace LivingCity.Data
{
    [CreateAssetMenu(fileName = "CityConfig", menuName = "Living City/City Config")]
    public sealed class CityConfig : ScriptableObject
    {
        /// <summary>
        /// HumanBehavior.SetRandomDestination() only accepts a destination between 60m and
        /// 300m away, so the grid must be big enough that even the most central pedestrian
        /// has somewhere 60m off. At 30m per cell the worst case is the centre cell, whose
        /// farthest corner sits sqrt(2) * (N-1)/2 * 30 metres away:
        ///
        ///   N=3 -> 42m  (too small: pedestrians strand, and pre-patch this hung the Editor)
        ///   N=5 -> 85m  (safe)
        ///   N=7 -> 127m (comfortable, and clears CarBehavior's 90m default too)
        ///
        /// 5 is the hard floor; 7 is the first size where cars also work without lowering
        /// CarBehavior.minDistance. Do not lower this.
        /// </summary>
        public const int MinGridSize = 5;

        [Header("Layout")]
        [Tooltip("Size of the map in cells; cells are 30m, so 9 x 7 is 240 x 180 metres. " +
                 "Streets reach the edge of this rectangle and stop there - there is no ring " +
                 "road round the outside, so the outer cells are built on. At the default 2-4 " +
                 "spacing about 45% of the map ends up as street, which puts 9 x 7 at around " +
                 "10 blocks.")]
        [Min(MinGridSize)] public int gridWidth = 9;
        [Min(MinGridSize)] public int gridHeight = 7;

        [Tooltip("Cells between two parallel streets. A gap of s leaves a block (s-1) cells " +
                 "wide, so at 30m cells the 2-4 default gives blocks anywhere from 30m to 90m " +
                 "on each axis. The layout is cut recursively rather than laid out as a grid, " +
                 "so a street stops instead of running the width of the map and the blocks do " +
                 "not line up into rows and columns; every size in the range appears, and the " +
                 "two axes of one block are drawn independently, so wide and narrow sit side " +
                 "by side. Setting both to the same value fixes the block size and brings back " +
                 "some of the regularity.")]
        [Min(2)] public int minArterialSpacing = 2;
        [Min(2)] public int maxArterialSpacing = 4;

        [Header("Determinism")]
        [Tooltip("Same seed must always produce the same city. Each subsystem derives its own " +
                 "System.Random from this so that one subsystem's draw count cannot shift another's.")]
        public int seed = 42;

        [Header("Buildings")]
        [Range(0.3f, 1f)]
        [Tooltip("Chance that each slot along a block's street wall is actually built. " +
                 "Keep it high - a city block should read as a continuous terrace, and the " +
                 "occasional gap is for relief, not for density. 1 = completely unbroken.")]
        public float blockFillRatio = 0.92f;

        [Range(0f, 1f)]
        [Tooltip("Share of RESIDENTIAL buildings that get a tint material instead of the plain " +
                 "atlas one. The untinted remainder is not waste - a street reads as varied " +
                 "because SOME facades differ, and leaving a slice at the original palette " +
                 "keeps the pack's art present. 0 disables tinting entirely.")]
        public float buildingTintChance = 0.85f;

        [Range(0f, 1f)]
        [Tooltip("The same roll for buildings from a group marked commercial. Near 1 on " +
                 "purpose: a period shopfront was painted, and an unpainted cafe is " +
                 "indistinguishable from the flats either side of it.")]
        public float commercialTintChance = 1f;

        [Header("Ground")]
        [Tooltip("Target side of one paving patch. Every lot and every alley of a block is cut " +
                 "into roughly square patches of about this size, and each rolls its own surface " +
                 "and shade - which is what stops a block reading as one flat rectangle of " +
                 "colour. This is the only dial on how many ground objects the city costs, and " +
                 "it is not linear: at 24 a 106m block comes out around 25 patches, at 12 nearer " +
                 "a hundred. Raise it if the ground object count becomes a problem.")]
        [Min(4f)] public float groundPatchSize = 24f;

        [Range(0f, 1f)]
        [Tooltip("Share of ground slabs that get a shade instead of the plain atlas one. Higher " +
                 "than buildingTintChance because a floor has no windows for the tint to reach - " +
                 "see BuildTintPalette on why ground shades may go far wider than facade ones. " +
                 "0 disables ground shading entirely and leaves every slab the pack's own colour.")]
        public float groundTintChance = 0.85f;

        [Tooltip("Paving joints, yard footpaths and faded repair patches, drawn as one flat mesh " +
                 "per block over the slabs. Off leaves the surfaces plain; the parking bay lines " +
                 "are separate and stay either way.")]
        public bool groundPaint = true;

        [Header("Block geometry")]
        [Tooltip("Distance kept clear between a block's street wall and the road centreline. " +
                 "BlockRect expands a block into its adjacent road tiles by (half a cell - this), " +
                 "so raising it pulls every facade back off the kerb at once. World units, so " +
                 "the default carries CityGrid.TileScale - the tile the wall stands beside is " +
                 "stretched by it and the pavement it must clear moved out with it.")]
        [Min(0f)] public float sidewalkWidth = 7f * Generation.CityGrid.TileScale;

        [Tooltip("The same distance where a block faces the dual carriageway, which is a wider " +
                 "road: its pavements sit at 7.25 from the centreline against a street's 4, so " +
                 "at the ordinary 7 the facades would stand ON the avenue's pavement. 10 clears " +
                 "it and still leaves a verge. Resolved per side, so a block between the avenue " +
                 "and a side street gets both setbacks. World units - see sidewalkWidth.")]
        [Min(0f)] public float mainSidewalkWidth = 10f * Generation.CityGrid.TileScale;

        [Tooltip("Service alley down the long axis of a block interior. Wide enough to read as a " +
                 "passage and to park a truck across, narrow enough that it never reads as a street.")]
        [Min(0f)] public float alleyWidth = 6f;

        [Tooltip("Separation between two touching facades in a terrace. This is NOT visual " +
                 "spacing - it exists only so coplanar walls do not z-fight, and so the " +
                 "occupancy test can tell 'flush neighbour' from 'genuine overlap'. " +
                 "Bounds.Intersects compares inclusively, so at exactly 0 a flush neighbour " +
                 "reports as a collision and is silently dropped. Do not set this to 0.")]
        [Min(0.01f)] public float partyWallGap = 0.05f;

        [Tooltip("Largest gap the run packer may open between two buildings when it spreads a " +
                 "run's leftover length. The leftover is divided equally across every joint " +
                 "rather than left as one hole at the end of the run - otherwise every block in " +
                 "the city has a visible notch at the same corner. Keep it small.")]
        [Min(0f)] public float maxFillerGap = 0.8f;

        [Header("Feature strip")]
        [Tooltip("Width range of the strip a featureStrip zone gives up along one street side " +
                 "of each block - the surface car lot or pocket park that absorbs the length " +
                 "the terrace kit cannot fill. Drawn per block. The floor of 8 holds one 5.6m " +
                 "parking row plus an apron; much past 14 the strip starts reading as a missing " +
                 "building rather than a deliberate lot.")]
        [Min(0f)] public float featureStripMin = 8f;
        [Min(0f)] public float featureStripMax = 12f;

        [Range(0f, 1f)]
        [Tooltip("Chance a feature strip is a pocket park - benches, trees, a kiosk - instead " +
                 "of parking. The rest of the strips get the car lot.")]
        public float pocketParkChance = 0.5f;

        [Header("Industrial")]
        [Tooltip("Carriageway through a works compound, between the rows of halls. Wide enough " +
                 "for two lorries to pass - alleyWidth (6) is deliberately NOT reused here: " +
                 "that one is the passage behind a terrace, and at 6m a works road reads as a " +
                 "gap between buildings rather than as the road the site is arranged around. " +
                 "Raising it eats into the rows, and past about 12 a block loses a row entirely.")]
        [Min(4f)] public float serviceRoadWidth = 8f;

        [Tooltip("Wall line, in from the block rect. Only has to stop the corner piers from " +
                 "overhanging their own edge; the pavement is metres further out.")]
        [Min(0f)] public float industrialWallInset = 1f;

        [Tooltip("Smoke from the city's chimneys - the works stacks and the terrace flues both. " +
                 "Runtime only - the city is generated into the scene and saved, and a baked " +
                 "particle system would be stamped BatchingStatic by MarkStaticForBatching and " +
                 "never move. Generation leaves a SmokeVent marker at each measured chimney mouth " +
                 "and SmokeStackSystem raises the plumes on Start, so this off costs nothing " +
                 "rather than leaving dead objects in the scene. To keep the works and drop only " +
                 "the houses, set the house profile's chance to 0 on SmokeStackSystem instead.")]
        [UnityEngine.Serialization.FormerlySerializedAs("industrialSmoke")]
        public bool chimneySmoke = true;

        [Header("Entities")]
        [Min(0)] public int carCount = 30;
        [Min(0)] public int pedestrianCount = 50;

        [Tooltip("Seconds between entity spawns. Entities only - road tiles must all be " +
                 "instantiated in one synchronous pass, never staggered.")]
        [Min(0f)] public float entitySpawnInterval = 0.1f;

        [Tooltip("How many random destinations a car serves before it is routed to a gap in the " +
                 "map's outline and driven off the map. Drawn per car, inclusive at both ends. " +
                 "Raising it makes traffic look more local and lowers the spawn churn; the " +
                 "trade is that a car lingers, and the population is finite, so at high values " +
                 "the same cars circulate all session. 0 sends a car straight out again, which " +
                 "reads as a motorway rather than a city.")]
        public Vector2Int wanderRoutesBeforeExit = new Vector2Int(1, 2);

        [Tooltip("Overrides CarBehavior.minDistance (prefab default 90m). 0 keeps the prefab " +
                 "value, which is correct at 7x7 and above. Below that, a car near the centre " +
                 "cannot find any tile 90m away and sits still logging " +
                 "'Target Tile not found farther then 90m' - lower this to about 50 there.")]
        [Min(0f)] public float carMinTravelDistance;

        [Tooltip("Overrides CarBehavior.maxspeed (prefab default 80-100 km/h). 0 keeps the prefab " +
                 "value. A tile is 30m, so 100 km/h is 27.8 m/s - a car crosses a whole city " +
                 "block in about a second, which leaves no room to react to anything and makes " +
                 "every junction a near miss. Lane limits on the road tiles run 30-80; this caps " +
                 "them all.")]
        [Min(0f)] public float carMaxSpeed = 45f;

        [Tooltip("Seconds of following distance a car keeps to the one in front. This is the " +
                 "time gap, so the metre gap grows with speed; the standstill gap is fixed " +
                 "separately in CarFollowing. Lower it for denser, more aggressive traffic - " +
                 "below about 0.5 cars tailgate hard enough that the hard clamp starts doing the " +
                 "braking instead of the model.")]
        [Min(0.2f)] public float carHeadway = Entities.CarFollowing.DefaultHeadway;

        [Range(0f, 1f)]
        [Tooltip("Share of eligible cars that get a paint colour instead of the body the pack " +
                 "baked in. Applies to parked and moving cars alike. Only the models with a " +
                 "NEUTRAL body in the atlas are eligible at all - see " +
                 "PrefabDatabase.paintableVehicles - so a taxi stays yellow whatever this says. " +
                 "The untinted remainder is not waste: it keeps the pack's own grey-white cars " +
                 "in the mix, which is what stops a car park reading as a colour chart. " +
                 "0 disables car paint entirely.")]
        public float vehicleTintChance = 0.85f;

        [Header("Pedestrian life")]
        [Tooltip("Master switch for the whole interaction layer: avoidance, chats, arguments, " +
                 "bench sitting, shop visits and the retargeted animations that carry them. " +
                 "Off restores the pack's original pedestrians - walking through each other, " +
                 "with only the old timer-based idler for variety.")]
        public bool pedestrianInteractions = true;

        [Tooltip("Radius inside which a walker starts steering around somebody, in metres. " +
                 "The soft half of avoidance - the hard floor below is what actually forbids " +
                 "overlap. Raise it for politer, emptier-feeling pavements.")]
        [Min(0.4f)] public float pedestrianPersonalSpace = 1.2f;

        [Tooltip("Surface-to-surface gap the movement clamp preserves between two bodies, in " +
                 "metres. The hard half: whatever the steering does, nobody closes inside " +
                 "this. It decays automatically for a walker held still for a few seconds, so " +
                 "a blocked pavement leaks instead of deadlocking.")]
        [Min(0.1f)] public float pedestrianMinSeparation = 0.6f;

        [Tooltip("Chance per second that two walkers passing within earshot stop for a " +
                 "conversation. Rolled per nearby PAIR by the director, so the felt frequency " +
                 "scales with how busy a street is. Both walk away on cooldown afterwards.")]
        [Range(0f, 1f)] public float chatChance = 0.06f;

        [Tooltip("How long a conversation runs, seconds, drawn per pair.")]
        public Vector2 chatDurationRange = new Vector2(6f, 20f);

        [Tooltip("Share of conversations that are arguments - shouting animation instead of " +
                 "talking, and the pair squares up slightly closer.")]
        [Range(0f, 1f)] public float argueFraction = 0.25f;

        [Tooltip("Chance a walker passing a bench with a free seat sits down on it. Rolled at " +
                 "most once per interaction cooldown, so benches gather people without every " +
                 "passer-by peeling off.")]
        [Range(0f, 1f)] public float benchChance = 0.35f;

        [Tooltip("How long a sitter stays down, seconds, drawn per sit.")]
        public Vector2 sitDurationRange = new Vector2(15f, 60f);

        [Tooltip("Chance a walker passing a shopfront goes in. Cafes, restaurants and the " +
                 "post office - whatever the generator flagged commercial with a street door.")]
        [Range(0f, 1f)] public float shopChance = 0.3f;

        [Tooltip("How long a visitor stays inside, seconds, drawn per visit.")]
        public Vector2 shopDurationRange = new Vector2(10f, 40f);

        [Tooltip("Seconds a walker stays plain after any activity before rolling for the next " +
                 "one, drawn per walker per activity. The master pacing dial - halve it for a " +
                 "street that never stops performing, double it for occasional colour.")]
        public Vector2 interactionCooldownRange = new Vector2(30f, 90f);

        [Tooltip("Chance the cooldown roll comes up as a plain pause - stand, look around, " +
                 "move on. Replaces the old PedestrianIdler for interaction-enabled walkers.")]
        [Range(0f, 1f)] public float idleChance = 0.3f;

        [Tooltip("How long a plain pause lasts, seconds.")]
        public Vector2 idleDurationRange = new Vector2(3f, 10f);

        [Tooltip("Chance a walker passing a street door goes into that building. This is what " +
                 "makes people come OUT of buildings too: the city's population is constant, " +
                 "so every walker who steps inside is answered by one stepping out somewhere. " +
                 "Doors sit on every street-facing building except the industrial ones, so a " +
                 "walker is nearly always next to one - this is the density dial for the " +
                 "whole in-and-out traffic.")]
        [Range(0f, 1f)] public float buildingVisitChance = 0.22f;

        [Tooltip("How long somebody stays inside a building, seconds, drawn per visit. Longer " +
                 "than a shop visit on purpose - this is going home, not buying a paper - and " +
                 "it is also what sets how many of the population are indoors at any moment.")]
        public Vector2 buildingStayRange = new Vector2(20f, 120f);

        [Tooltip("Chance somebody who went in comes out of a DIFFERENT door somewhere else in " +
                 "the city rather than the one they entered. This is what makes doors read as " +
                 "a city rather than as airlocks - the person you see leaving is never the " +
                 "one you watched go in. At 0 everyone returns to their own doorstep.")]
        [Range(0f, 1f)] public float buildingSwapChance = 0.6f;

        [Header("Police")]
        [Tooltip("Patrol cars in the station's fleet. These are persistent GameObjects - the " +
                 "same cars drive out, patrol, park in the forecourt and drive out again; " +
                 "none of the traffic system's spawn/exit churn applies to them. 0 disables " +
                 "car patrols. The forecourt has floor(width/2.7) stalls, so counts above " +
                 "that simply keep more of the fleet out on the street at once.")]
        [Min(0)] public int policeCarCount = 4;

        [Tooltip("Officers walking the beat. Persistent like the cars: they patrol the " +
                 "pavements and periodically return to the station through its door. Not " +
                 "counted in pedestrianCount. 0 disables foot patrols.")]
        [Min(0)] public int policeOfficerCount = 4;

        [Tooltip("Random routes a patrol car serves before it heads back to the station, " +
                 "drawn per patrol, inclusive at both ends. The same dial as " +
                 "wanderRoutesBeforeExit, but the destination is home, not the map edge.")]
        public Vector2Int policeCarPatrolRoutes = new Vector2Int(2, 4);

        [Tooltip("Seconds a car rests in its forecourt stall between patrols, drawn per " +
                 "visit. Also staggers the fleet - each car draws independently, so they " +
                 "never pull out in convoy.")]
        public Vector2 policeCarRestRange = new Vector2(20f, 60f);

        [Tooltip("Sidewalk routes an officer walks before returning to the station, drawn " +
                 "per patrol, inclusive at both ends.")]
        public Vector2Int policeOfficerPatrolRoutes = new Vector2Int(2, 4);

        [Tooltip("Seconds an officer stays inside the station between beats, drawn per " +
                 "visit. Same hide-and-reappear machinery as civilian building visits.")]
        public Vector2 policeOfficerStationStayRange = new Vector2(20f, 90f);

        [Tooltip("How many of the fleet begin a session parked at the station; the rest " +
                 "start already out on the road mid-patrol, so the city opens looking like " +
                 "the shift has been running for a while. Clamped to the fleet size and to " +
                 "the stalls actually available.")]
        [Min(0)] public int policeCarsStartAtStation = 1;

        [Tooltip("How many officers begin a session inside the station; the rest start " +
                 "mid-beat on random pavements. Clamped to the officer count.")]
        [Min(0)] public int policeOfficersStartAtStation = 1;

        [Header("Bank visitors")]
        [Tooltip("Customers driving to the bank at once. They arrive through the map edge, " +
                 "park in the bank's forecourt, stay a while and leave for good - a different " +
                 "car each time. Spawned outside carCount and outside VehicleSpawner's " +
                 "population. 0 disables them, leaving the forecourt's static cars alone.")]
        [Min(0)] public int bankVisitorCount = 2;

        [Tooltip("Seconds a customer's car stands in its forecourt bay, drawn per visit.")]
        public Vector2 bankVisitStayRange = new Vector2(30f, 90f);

        [Tooltip("Seconds between one departure and the next arrival, drawn per car. Also " +
                 "the poll interval that keeps the population topped up.")]
        public Vector2 bankVisitorGapRange = new Vector2(10f, 40f);

        [Header("School run")]
        [Tooltip("Schoolchildren the bus serves. A fixed, persistent roster spread across the " +
                 "stops below - the same children ride out and back every day, hidden at home " +
                 "and hidden inside the school in between, never spawned or destroyed. Not " +
                 "counted in pedestrianCount. 0 disables the school run entirely, including " +
                 "the bus.")]
        [Min(0)] public int schoolChildCount = 8;

        [Tooltip("Pickup stops between the school and the children. More stops with fewer " +
                 "children at each is better than the reverse: the bus halts in a live lane " +
                 "while they board, and a short queue is a short halt.")]
        [Min(1)] public int schoolBusStops = 3;

        [Tooltip("Metres from the school that pickup stops are looked for, nearest and " +
                 "furthest. Too near and the bus barely leaves; too far and the run outlasts " +
                 "any window you would set for it.")]
        public Vector2 schoolStopDistance = new Vector2(60f, 160f);

        [Tooltip("Hour of the CITY's clock the morning run may start at - see Time of day. " +
                 "Only the START is gated: once a run begins it drives to the end whatever " +
                 "the clock does, because at 1 real second per game hour a whole day passes " +
                 "in less time than one round trip takes.")]
        [Range(0f, 24f)] public float schoolMorningHour = 7.5f;

        [Tooltip("Hour of the city's clock the afternoon run home may start at.")]
        [Range(0f, 24f)] public float schoolAfternoonHour = 15f;

        [Tooltip("How many game hours each of those windows stays open. The run is latched to " +
                 "one per window per day, so a generous window only means the bus is less " +
                 "likely to miss its slot - never that it runs twice.")]
        [Min(0f)] public float schoolRunWindowHours = 1.5f;

        [Tooltip("Hard cap in real seconds on how long the bus stands at one stop. It is " +
                 "parked in a live lane and nothing can overtake it, so a halt much longer " +
                 "than this backs traffic into a junction. Any child still on the pavement " +
                 "when it expires walks to school on foot instead.")]
        [Min(1f)] public float schoolBusDwellSeconds = 8f;

        [Tooltip("Parents' cars using the school's forecourt at once - the same trip the bank's " +
                 "customers make, at a different building. Lower than the bank's because the " +
                 "school yard is four bays and one of them carries a static car, the bus berth " +
                 "having taken the rest of the frontage. 0 turns them off.")]
        [Min(0)] public int schoolParentCount = 2;

        [Tooltip("Real seconds a parent's car stays in the forecourt, shortest and longest. " +
                 "Shorter than a bank visit on purpose: a drop-off is a drop-off.")]
        public Vector2 schoolParentStayRange = new Vector2(20f, 60f);

        [Tooltip("Real seconds between one parent's car setting off from the map edge and the " +
                 "next, shortest and longest.")]
        public Vector2 schoolParentGapRange = new Vector2(15f, 50f);

        [Header("Docks")]
        [Tooltip("Chance the city is a port city at all - one roll per seed, the bank's " +
                 "mechanism. When it lands, EVERY block along one map side becomes the port " +
                 "and the sea runs the whole length of that side; when it misses, the city " +
                 "is landlocked and the whole system stands down. There is no in-between - " +
                 "a single port block on an otherwise dry edge read as a diorama.")]
        [Range(0f, 1f)] public float portChance = 0.5f;

        [Tooltip("Dockers on EACH port compound's shift. A fixed, persistent roster working " +
                 "the quay, the stacks and the warehouse doors - never spawned or destroyed, " +
                 "not counted in pedestrianCount, and never leaving the compound. 0 disables " +
                 "the shift; it also stands down by itself on a landlocked seed.")]
        [Min(0)] public int dockWorkerCount = 6;

        [Tooltip("Real seconds a cargo ship stays alongside before it sails, shortest and " +
                 "longest. The forklift works the quay for as long as the ship is in.")]
        public Vector2 portShipStayRange = new Vector2(45f, 90f);

        [Tooltip("Real seconds between one ship leaving and the next appearing up the coast, " +
                 "shortest and longest.")]
        public Vector2 portShipGapRange = new Vector2(20f, 60f);

        [Header("Crowd performance")]
        [Tooltip("Fixed steps between two avoidance probes per walker. 1 probes every step, " +
                 "the original cadence; at N each walker probes every Nth step (staggered " +
                 "across the crowd) and spends the cached movement allowance in between, so " +
                 "overlap stays impossible - the steering is merely up to N-1 steps stale. " +
                 "3 cuts the avoidance cost to a third and is visually indistinguishable.")]
        [Min(1)] public int pedestrianProbeInterval = 3;

        [Tooltip("Pedestrians spawned per entitySpawnInterval tick (per frame when the " +
                 "interval is 0). The old one-per-tick ramp takes a thousand seconds at " +
                 "crowd scale; batching keeps the ramp short without instantiating the whole " +
                 "population in one frame.")]
        [Min(1)] public int pedestrianSpawnBatch = 10;

        [Tooltip("Route re-paths granted per frame. A walker finishing its route queues for " +
                 "a new one instead of running A* on the spot - at crowd scale those " +
                 "searches land in unsynchronised bursts that read as hitches. Arrivals " +
                 "beyond the budget stand at the kerb a few frames longer, which is " +
                 "invisible; 0 is not a valid setting.")]
        [Min(1)] public int pedestrianRepathBudget = 24;

        [Tooltip("Camera orthographic size above which pedestrian animators tick at reduced " +
                 "rate (see the LOD rate below). The camera is orthographic, so zoom - not " +
                 "distance - is what shrinks people on screen; around 45 a pedestrian is " +
                 "under 20 pixels tall.")]
        [Min(0f)] public float pedestrianLodMidOrtho = 45f;

        [Tooltip("Orthographic size above which pedestrian animators freeze entirely and " +
                 "their shadows turn off. At 90 a person is a few pixels; the transform " +
                 "still moves, so the crowd still flows, it just stops pumping arms nobody " +
                 "can see.")]
        [Min(0f)] public float pedestrianLodFarOrtho = 90f;

        [Tooltip("Animator slowdown factor in the mid LOD band: each animator is advanced " +
                 "once every this many frames, round-robin, so the cost drops by the same " +
                 "factor. 3 at 60fps still animates everyone at 20fps.")]
        [Min(2)] public int pedestrianLodMidRate = 3;

        [Header("Time of day")]
        [Tooltip("Real seconds for one hour of game time. 60 runs a full day in 24 real minutes; " +
                 "drop it to 1 and the whole day passes in 24 seconds, which is how you watch a " +
                 "sunset without waiting for one.")]
        [Min(0.02f)] public float realSecondsPerGameHour = 60f;

        [Tooltip("Hour the clock starts at. 8 is mid-morning; 22 drops you straight into night.")]
        [Range(0f, 24f)] public float startHour = 8f;

        [Tooltip("Lifts the whole night - moonlight, ambient and window glow together. 1 is the " +
                 "pack's own night demo, which is authored dark on the assumption that street " +
                 "lamps carry the scene. Raise it if the city still reads as unlit.")]
        [Min(0f)] public float nightBrightness = 1f;

        [Header("Street lamps")]
        [Tooltip("How many lamp BULBS burn at once, nearest the camera's look-at point first. " +
                 "Bulbs, not lamps: StreetLampLights hangs a light under each of the double " +
                 "lamp's two lanterns, so a 92-lamp city spends 184 of these. This used to be " +
                 "effectively unlimited, which was safe only while the lamps were sparse. They " +
                 "are not any more - they stand on every cell seam - so the ceiling is real: " +
                 "URP Forward+ renders at most 256 additional lights per frame on desktop and " +
                 "quietly culls the rest, and 30 cars carry 60 headlight beams into the same " +
                 "256. 192 is sized so today's city fits entirely under it - the sort never has " +
                 "to choose and costs nothing - while a bigger map gets trimmed by distance " +
                 "rather than by whatever URP happens to drop. 0 turns them off.")]
        [Min(0)] public int litLampBudget = 192;

        [Tooltip("Reach of one car headlight, in metres. Short on purpose - the beam is " +
                 "steeply tilted and pools right in front of the bumper, so 12 covers it " +
                 "and keeps the light's culling sphere small.")]
        [Min(1f)] public float headlightRange = 12f;

        [Tooltip("Brightness of one car headlight at full night. Same 1/distance^2 rule as " +
                 "the lamps: the beam is tilted 24 degrees down from bumper height and pools " +
                 "~1.6m ahead, so the road sees roughly intensity/3 - a hot yellow core that " +
                 "reads much stronger than the street lamp pools.")]
        [Min(0f)] public float headlightIntensity = 16f;

        [Tooltip("Reach of one lamp bulb, in metres. The bulbs emit from 2.5m up (see " +
                 "StreetLampLights.BulbHeight - kept low so the 45-degree camera's parallax " +
                 "cannot detach a pool from its lantern), and a pool is ~4.3m across, so 10 " +
                 "covers it with margin while keeping the light's culling sphere small.")]
        [Min(1f)] public float lampRange = 10f;

        [Tooltip("Brightness of one lamp bulb at full night. URP attenuates punctual lights " +
                 "by 1/distance^2 and the bulbs emit from 2.5m up, so the pool centre sees " +
                 "intensity/6.25 - around 5 gives a clear amber pool. If the bulb height in " +
                 "StreetLampLights ever changes, rescale this by height^2.")]
        [Min(0f)] public float lampIntensity = 5f;

        [Header("Debug")]
        [Tooltip("Bypass ZonePlanner and stamp every block with Debug Zone. Isolates one " +
                 "palette's layout rules so a change to it shows on every block at once. " +
                 "Toggled from Tools/City; leave off for normal cities.")]
        public bool debugSingleZone;

        [Tooltip("The zone every block becomes while Debug Single Zone is on.")]
        public Generation.BlockZone debugZone = Generation.BlockZone.ResidentialHigh;

        public float WorldWidth => (gridWidth - 1) * Generation.CityGrid.CellSize;
        public float WorldHeight => (gridHeight - 1) * Generation.CityGrid.CellSize;

        void OnValidate()
        {
            gridWidth = Mathf.Max(MinGridSize, gridWidth);
            gridHeight = Mathf.Max(MinGridSize, gridHeight);
            maxArterialSpacing = Mathf.Max(minArterialSpacing, maxArterialSpacing);
            featureStripMax = Mathf.Max(featureStripMin, featureStripMax);
            pedestrianLodFarOrtho = Mathf.Max(pedestrianLodMidOrtho, pedestrianLodFarOrtho);
            policeCarsStartAtStation = Mathf.Clamp(policeCarsStartAtStation, 0, policeCarCount);
            policeOfficersStartAtStation =
                Mathf.Clamp(policeOfficersStartAtStation, 0, policeOfficerCount);
        }
    }
}
