using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RoadDemo
{
    // Self-contained road-network demo. Everything is built at Play time from the
    // Synty POLYGON City road kit (5 m lattice pieces) and POLYGON Palm City
    // vehicles: a grid of boulevards and ordinary two-way streets with sidewalks,
    // signalised intersections and cars driving the lane graph.
    //
    // The grid is the whole city, districts and all: some of the gaps between two
    // road lines are not blocks but SEAMS - a river with the boulevards bridging it
    // and the streets ending on its quays, a park the streets stop at and the
    // boulevards drive through (RoadDemoBuilder.Seams.cs). What lies between two
    // seams is a district; nothing but the seams tells one from the next.
    //
    // Piece convention (measured against the pack's own demo scene): every kit
    // piece has its pivot on a corner and covers the 5x5 m square towards local
    // -X/-Z, so a cell (min corner mx,mz) is filled by pivots:
    //   yaw 0 -> (mx+5, mz+5), yaw 90 -> (mx+5, mz), yaw 180 -> (mx, mz),
    //   yaw 270 -> (mx, mz+5).
    public partial class RoadDemoBuilder : MonoBehaviour
    {
        [Header("Grid (centreline positions, multiples of 5)")]
        // How many roads there are and which of them are boulevards is authored
        // here; where they land is re-spaced at Play time unless the randomiser
        // below is switched off. The authored X/Z are then the even fallback
        // spacing, one residentialblock1 bake per interior (70 x 50 m). Re-spaced,
        // the interiors take their sizes from blockWidths / blockDepths below.
        //
        // The default plan is a city of some sixty-six blocks in a dozen districts
        // (a fifth smaller than it was: the suburbs grew and downtown gave the
        // ground): west to east, four columns of blocks, a park, five columns, a
        // wild strip, one more column, the elevated freeway, one last column; south
        // to north, three rows, the river, two rows, a second park, one more row.
        // Vertical roads 1, 5, 8 and 11 are boulevards (5 the first park's east
        // edge, 11 the wild strip's, all bridging the river); horizontal 1, 5 and 7
        // cross the halves. The freeway rides over every street on pillars, ramps
        // down past the last junction and ends ON the island both ways, in a T with
        // a link road out to the district roads on that shore.
        public float[] verticalRoadX =
            { 0f, 100f, 200f, 300f, 400f, 500f, 600f, 700f, 800f, 900f, 1000f, 1100f, 1200f, 1300f, 1400f };
        public bool[] verticalIsBoulevard =
            { false, true, false, false, false, true, false, false, true, false, false, true, false, false, false };
        public float[] horizontalRoadZ = { 0f, 80f, 160f, 240f, 320f, 400f, 480f, 560f, 640f };
        public bool[] horizontalIsBoulevard = { false, true, false, false, false, true, false, true, false };

        [Header("Freeways")]
        [Tooltip("Freeways at all: the elevated highway through the town (a Highway seam) " +
                 "and the belt round it. OFF is the town as it stands - streets, and the " +
                 "approach roads out to the quarters, and nothing else. It is a MASTER " +
                 "switch and not a default: with it off every Highway seam is taken out of " +
                 "the list before the grid is spaced, and the belt is forced off, so a seam " +
                 "left on the inspector or serialised into an old scene cannot put a " +
                 "carriageway back through the suburbs.")]
        public bool freeways = false;

        /// <summary>Take the freeways out. Called first of all, before Respace, because
        /// the grid's own spacing is laid on the seam list.</summary>
        void NoFreeways()
        {
            if (freeways) return;
            beltFreeway = false;
            if (seams == null) return;
            var kept = new List<Seam>();
            int dropped = 0;
            foreach (var s in seams)
            {
                if (s != null && s.kind == SeamKind.Highway) { dropped++; continue; }
                kept.Add(s);
            }
            if (dropped == 0) return;
            seams = kept.ToArray();
            Debug.Log($"[RoadDemo] freeways off: {dropped} Highway seam(s) dropped and the belt " +
                      "with them; the gap they held is an ordinary block now.");
        }

        [Header("Seams between districts")]
        [Tooltip("The gaps between two road lines that are not blocks: a river (the boulevards " +
                 "bridge it, the streets end on its quays), a park (boulevards through, streets " +
                 "stop) or an elevated highway (every road passes under it). 'gap' counts the " +
                 "spaces between lines from the south / west; a vertical seam runs north-south " +
                 "between two vertical roads.")]
        public Seam[] seams =
        {
            // Two rivers, not one, and they meet. A single channel across the middle
            // cuts a town in half; two crossing ones cut it into four quarters that
            // have to be driven between, which is what a river actually does to a
            // city's shape. They meet in column gap 7 / row gap 3, and at the
            // confluence neither lays a quay across the other's mouth (BuildRiver).
            // The main one is the wide one - 130 m of water is a river you look at
            // rather than a canal you step over.
            new Seam { vertical = false, gap = 3, kind = SeamKind.River, width = 130f },
            // a TRIBUTARY, not a second river across the whole map: it comes down from
            // the north shore and stops in the main channel (fromRoad 4 is the river's
            // own north quay road). Run it the full length and it leaves the south shore
            // cut in half as well - and the airport, whose field is a mile of runway,
            // then fits on no shore in the city and simply does not get built. 85 m so
            // the column south of the confluence, which is dry ground, comes out a lot
            // size the catalog has blocks for.
            new Seam { vertical = true, gap = 7, kind = SeamKind.River, width = 85f, fromRoad = 4 },
            new Seam { vertical = true, gap = 4, kind = SeamKind.Park, width = 60f },
            new Seam { vertical = true, gap = 10, kind = SeamKind.Wild, width = 80f },
            new Seam { vertical = false, gap = 6, kind = SeamKind.Park, width = 60f },
            // No freeway through the town, and no belt round it (beltFreeway): a city of
            // this size is streets, and an elevated highway down the middle of it took a
            // hundred and forty metres of grid, four ramps and a whole seam to be a road
            // nothing drove to. The Highway seam and the interchange it builds are still
            // here - put one back on a gap and it stands up again.
        };

        [Header("Block sizes")]
        [Tooltip("Re-space the grid so columns and rows differ in size instead of " +
                 "every block coming out the same. The roads stay a full grid - only " +
                 "how far apart they sit changes.")]
        public bool randomiseBlockSizes = true;

        [Tooltip("Which spread of sizes gets drawn. Same seed, same city.")]
        public int spacingSeed = 7;

        [Tooltip("Interior widths a column of blocks may take, kerb to kerb. Only " +
                 "sizes that have a lot pad in the catalog scene belong here: a block " +
                 "is composed ON a pad, so a width with no pad gets a bake that cannot " +
                 "fill it. Handed out in order and then shuffled, so with more columns " +
                 "than sizes one width comes up twice. The narrowest is the residential " +
                 "bake's own 70 m - go under it and those lots turn into pocket courts " +
                 "instead, since the bake cannot shrink.")]
        public float[] blockWidths = { 70f, 85f, 100f }; // lot pad columns A, B, C

        [Tooltip("The same for interior depth, row by row. The bake needs 50 m.")]
        public float[] blockDepths = { 50f, 70f, 95f };  // lot pad rows 1, 2, 3

        [Tooltip("Which of the blocks filed under a lot code the city starts handing " +
                 "out at. 0 is the city's own order. The block lab (BlockDemo.unity) " +
                 "steps it to walk through every block composed or rolled for one pad.")]
        [Min(0)] public int blockCycle = 0;

        [Header("Traffic")]
        public int carCount = 70;
        [Tooltip("Two-wheelers in the traffic, each with a rider and now and then a " +
                 "mate behind him, plus about half as many again stood on their stands " +
                 "at the kerbs. A handful is the point: a bike is a rider nobody can " +
                 "hide behind glass, and a street reads as having motorcycles on it at " +
                 "four of them, not forty.")]
        [Min(0)] public int bikeCount = 8;
        public float streetSpeed = 9f;
        public float boulevardSpeed = 13f;
        public int pedestrianCount = 170;

        [Header("Day/night")]
        [Tooltip("Real seconds per game hour. 15 runs a whole day in 6 minutes; " +
                 "the city's own default of 60 takes 24.")]
        public float realSecondsPerGameHour = 15f;
        [Tooltip("Hour the demo starts at. 16 puts dusk about 40 seconds in.")]
        [Range(0f, 24f)] public float startHour = 16f;

        [Header("Police patrol")]
        public int policeCarCount = 3;
        public int policeOfficerCount = 2;
        [Tooltip("Beat PAIRS dealt over the city's blocks, each walking its own block's " +
                 "ring from the first frame - the law the player sees everywhere, not " +
                 "just at the station door. -1 scales it: one pair to every four blocks. " +
                 "0 leaves only the station pair.")]
        public int policeBeatPairs = -1;
        public Vector2 policeRestSeconds = new Vector2(6f, 16f);
        // waypoints per patrol, drawn across the whole map (cars) or the beat
        // radius (officers) - each one is a routed trip, not a wandered block
        public Vector2Int policePatrolWaypoints = new Vector2Int(2, 4);

        [Header("Rivals")]
        [Tooltip("Rival FAMILIES on the street (the ledger deals none), in GangCatalog " +
                 "order and spread across the whole map: twenty of them is a city with a " +
                 "mob on it rather than one rival somewhere. 0 for a quiet town.")]
        [Range(0, 20)] public int rivalCrewsInCity = 20;

        [Tooltip("The most rival crews the street will hold, over all families together. " +
                 "A family runs one to three capos (GangSeeder) and each of them holds a " +
                 "corner of his own, so this is the ceiling on rival MEN: about four to " +
                 "the crew. Rounds: every family is standing somewhere before any family " +
                 "gets a second corner.")]
        [Range(1, 60)] public int rivalCrewCap = 26;

        [Tooltip("The most soldiers behind one capo. The seeder deals two or three; this " +
                 "cuts a crew shorter, it never pads one out.")]
        [Range(0, 4)] public int rivalHoodsInCity = 3;

        [Tooltip("Grenades each outfit crew starts with ONLY in a scene with no ledger " +
                 "behind it. In the city the ledger is the truth - grenades are bought " +
                 "on the armory's EXPLOSIVES shelf and given to a lieutenant, and " +
                 "DemoCrews.BindBombs counts them onto the crews - so this stays 0.")]
        [Min(0)] public int bombsPerCrew = 0;

        /// <summary>The stream the mobs are dealt from - the whole underworld, names and
        /// all, off one number. Not the demo's own 1987 seed by accident: it IS that
        /// seed, so the city and the men standing on it move together.</summary>
        const int RivalSeed = 1987;

        [Header("The monkey")]
        [Tooltip("Nobody at the mouse and the whole underworld at each other's throats: " +
                 "the mobs are set at one another every few seconds and the outfit is " +
                 "sent out by car, on foot and on the machine, while everything " +
                 "impossible that happens is written down (MonkeyRunner). For headless " +
                 "runs - leave it off for a Play session.")]
        public bool monkey = false;
        [Tooltip("Same seed, same run of orders.")]
        public int monkeySeed = 1;
        [Tooltip("Sim seconds between the monkey's orders.")]
        public float monkeyOrderEvery = 5f;
        [Tooltip("Sim seconds before the monkey's first order - the city has to finish " +
                 "standing up first.")]
        public float monkeyStartAfter = 20f;

        [Tooltip("Every man of a mob his own piece, drawn off the armory's ladder, " +
                 "instead of a crew all carrying the same gun. A shotgun man walks in " +
                 "close and a rifleman opens up from across the street, so a mixed mob " +
                 "strings itself out by what it is holding.")]
        public bool mixedArms = false;

        [Header("City life")]
        [Tooltip("Share of the crowd that starts indoors and streams out of the doors over the first minute.")]
        [Range(0f, 1f)] public float insideAtStart = 0.45f;
        [Tooltip("Chance to slip into a door being walked past; entering despawns until the stay ends.")]
        [Range(0f, 1f)] public float enterChance = 0.3f;
        [Range(0f, 1f)] public float sitChance = 0.45f;
        public Vector2 insideSeconds = new Vector2(10f, 45f);
        public Vector2 sitSeconds = new Vector2(10f, 35f);
        public Vector2 chatSeconds = new Vector2(6f, 14f);

        const float Cell = 5f;
        // The kerb strip a car is left on. Without it a car parked at the kerb stands
        // in the driving lane - its flank reaches to within a foot of the lane centre -
        // and anything coming up behind has to cross the crown to get by, which it may
        // only do with the far lane empty (StreetTraffic.ParkedAhead): two cars meeting
        // with one parked between them is a jam. The strip takes the parked car off the
        // lane, and nothing that drives moves an inch - the lane centres (LaneOffsets,
        // IRoadModel.LaneZ) are measured off the crown and are where they always were.
        const float ParkLane = 2.5f;
        const float MedianHalf = 5f;     // the boulevard's planted divider, half width
        const float StreetHalf = 5f + ParkLane;      // 2 lanes and a parking strip each side
        const float BoulevardHalf = 15f + ParkLane;  // 2+2 lanes, the median, a strip each side
        // The pavement, kerb to building line. Wider than the kit's 5 m tile: the
        // tile is stretched across to it, the lots sit that much further from the
        // kerb, and the few metres it leaves a carriageway short of the 5 m beat
        // between two crossings are closed by stretching the road tiles a hair.
        const float Sidewalk = SidewalkDressing.Width;
        // Narrowest interior worth calling a block: below this the courtyard pass
        // has no room to dress it and it reads as a gap between two streets.
        const float MinInterior = 20f;

        [Header("The light")]
        [Tooltip("Where the sun stands: pitch, then compass yaw. The city's own is " +
                 "(52, 38); PolygonCity's demo scene is (50, 212), which is the same " +
                 "height of sun coming from the OTHER side - which side of a facade " +
                 "is lit is most of the difference between the two scenes' pictures.")]
        public Vector3 sunAngles = new Vector3(52f, 38f, 0f);
        [Tooltip("The city's own is 1.25; PolygonCity's demo runs 1.5.")]
        public float sunIntensity = 1.25f;
        [Tooltip("How hard the sun's shadows read. PolygonCity's demo runs 0.8.")]
        [Range(0f, 1f)] public float sunShadowStrength = 1f;
        [Tooltip("Whose demo scene the colour grade copies (DemoGrade.Look).")]
        public DemoGrade.Look look = DemoGrade.Look.PalmCity;
        [Tooltip("PolygonCity's linear haze in metres, start then end. Zero leaves " +
                 "this sky's own exponential falloff, which is what a city this wide " +
                 "needs; a small scene can afford their 50 -> 400.")]
        public Vector2 linearHaze = Vector2.zero;

        /// <summary>Half a carriageway, crown to kerb - a boulevard's or an ordinary
        /// street's. Public because a scene that lays out its own grid (the block lab)
        /// has to place its road lines by the same measurements the city uses.</summary>
        public static float RoadHalf(bool boulevard) => boulevard ? BoulevardHalf : StreetHalf;

        /// <summary>The pavement, kerb to building line.</summary>
        public static float PavementWidth => Sidewalk;

        /// <summary>Road centrelines for one axis: the palette dealt across the gaps in
        /// order, each line a pavement, an interior and a pavement on from the last.
        ///
        /// The same formula as PlanLine, which may re-deal them later - and the reason
        /// this is here rather than in a scene is that two scenes now lay out their own
        /// grid (the quarter and the block lab), and a grid arithmetic that lives in one
        /// of them drifts from the other the first time the pavement changes width.</summary>
        /// <summary>Which of the road lines on one axis are boulevards, out of the line
        /// numbers a scene names. Shared with Centrelines because the two are always
        /// used together: the boulevard flags decide half the spacing.</summary>
        public static bool[] Avenues(int count, int[] named)
        {
            var blvd = new bool[count];
            if (named != null)
                foreach (int at in named)
                    if (at >= 0 && at < count) blvd[at] = true;
            return blvd;
        }

        public static float[] Centrelines(int count, bool[] boulevard, float[] palette)
        {
            var at = new float[count];
            float pave = PavementWidth;
            for (int k = 0; k + 1 < count; k++)
            {
                float interior = palette != null && palette.Length > 0
                    ? palette[k % palette.Length] : 85f;
                at[k + 1] = at[k] + RoadHalf(boulevard[k]) + pave +
                            interior + pave + RoadHalf(boulevard[k + 1]);
            }
            return at;
        }

        // The generic terrace: the last-resort filler for a lot no other bake wanted.
        // It is the one block named here; everything else in the folder is found by
        // the scan (see LoadBlockBakes), so composing a new one needs no edit here.
        const string BlockPrefabPath = "Assets/CityKit/Blocks/residentialblock1.prefab";
        // A lot counts as a pad's when both sides land within this tolerance of the
        // pad's size (spacing rounds to 5 m).
        const float LotMatchTolerance = 1f;
        const string BlocksDir = "Assets/CityKit/Blocks/";
        // The auto-extracted PalmCity candidates, taken by number: 02..08 were kept,
        // 01 and 09 passed over. The scan leaves this family to the loop below.
        const string PalmBlockPrefix = "PalmBlock_";
        // What the catalog's batch roller names its output (BlockLotStock.Prefix - the
        // editor assembly cannot be referenced from here, so the string is repeated
        // rather than shared). A bake named this way was composed by the machine and
        // stands in a lot only where no hand-made block wants it.
        const string AutoBlockPrefix = "auto_";
        // A block is a lot's worth of buildings. A bake measuring less than this on
        // either side is a stray prop saved into the folder, not an interior, and
        // standing it alone in a lot would read as an empty block with litter in it.
        const float MinBakeFootprint = 12f;
        const string PoliceStationPath = "Assets/CityKit/Buildings/building-policestation.prefab";
        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string CityProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string PalmEnv = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        const string PalmVeh = "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/";
        // PalmCity's triplanar ground: grass on top, sand on the sides, tiled in
        // world space - so one material covers a patch of any size, with the pack's
        // own normal maps on it, and no UVs to get wrong
        const string PalmGround =
            "Assets/Synty/PolygonPalmCity/Materials/Env/Grass_Triplanar_01.mat";

        // One half of a two-way street. YellowLines_02 carries the yellow centre
        // line at one edge and the white kerb line - the one a car parks outside of -
        // at the other, so a street wants it twice, the second half turned about:
        // YellowLines_01 is the same tile without the kerb line, which left the
        // parking line painted down one side of every street only.
        GameObject _roadHalf;
        GameObject _laneEdge, _laneDash;    // boulevard kerb lane / inner dashed lane
        GameObject _median, _bare, _crossing;
        GameObject _bareCracked, _roadPatch; // block-floor variation tiles
        GameObject _swStraight, _swCorner, _divider;
        GameObject _poleBase, _poleArm, _poleLights;
        readonly List<GameObject> _palms = new List<GameObject>();
        readonly List<GameObject> _carPrefabs = new List<GameObject>();
        readonly List<GameObject> _pedPrefabs = new List<GameObject>();
        AnimationClip _walkClip, _idleClip;
        AnimationClip _sitDownClip, _sitLoopClip, _standUpClip, _talkClip, _shoutClip;

        // street dressing (PalmCity prop vocabulary; what goes where and how often
        // was read off the POLYGON City demo's pavements - see SidewalkDressing)
        readonly List<GameObject> _grates = new List<GameObject>();
        readonly List<GameObject> _lamps = new List<GameObject>();
        readonly List<GameObject> _bins = new List<GameObject>();      // the public litter bin, at the kerb
        readonly List<GameObject> _wallBins = new List<GameObject>();  // the building's own, against the wall
        readonly List<GameObject> _benches = new List<GameObject>();
        readonly List<GameObject> _planters = new List<GameObject>();
        readonly List<GameObject> _powerboxes = new List<GameObject>();
        readonly List<GameObject> _bushes = new List<GameObject>();
        readonly List<GameObject> _wires = new List<GameObject>();
        readonly List<GameObject> _chairs = new List<GameObject>();
        readonly List<GameObject> _tables = new List<GameObject>();
        readonly List<GameObject> _umbrellas = new List<GameObject>();
        GameObject _bag, _bagOpen, _bollard, _hydrant, _mailbox, _newsstand, _powerpole;
        GameObject _bikeStand, _signPole, _manhole;
        GameObject _treeCage, _banner, _meter, _payPhone, _menuStand;
        GameObject _pave;              // PalmCity 2.5 m concrete plate, the demo's court floor
        bool _paveMeasured;
        Vector3 _paveSize, _paveOffset;
        float _paveTop;
        /// <summary>The marked cruisers the patrol fleet draws from - the approved
        /// pair (VehicleCatalog.PoliceCars), one per stall in turn, so a station yard
        /// is a fleet rather than one car photocopied down the row.</summary>
        readonly List<GameObject> _policeCarPrefabs = new List<GameObject>();
        readonly List<GameObject> _officerPrefabs = new List<GameObject>();
        GameObject _policeStation;     // the packed station instance, found at placement
        bool _forecourtPlanned;
        Vector3 _stallCentre, _stallOut, _stallAlong;
        float _stallRowHalf, _stallLift;
        readonly List<PolicePatrolCar> _policeCars = new List<PolicePatrolCar>();
        readonly List<PoliceFootPatrol> _policeOfficers = new List<PoliceFootPatrol>();
        DemoCrews _crews;
        GameObject _blockPrefab;
        // Blocks composed on a catalog lot pad, filed under that pad's code ("B2").
        // Several bakes may share a code; a lot takes the next one in turn, so two
        // B2 interiors in the same city are not the same block twice.
        readonly Dictionary<string, List<GameObject>> _lotBakes =
            new Dictionary<string, List<GameObject>>();
        readonly Dictionary<string, int> _lotBakeCursor = new Dictionary<string, int>();
        readonly HashSet<string> _lotOverflowLogged = new HashSet<string>();
        // The rolled stock: blocks the catalog's randomiser composed for a pad size, one
        // pool per code, kept apart from the hand-made blocks above because they rank
        // below them - a lot takes a roll only once no composed block wants it. Empty
        // until Tools/City/Catalog/Randomise Blocks For Every Lot has been run.
        readonly Dictionary<string, List<GameObject>> _autoBakes =
            new Dictionary<string, List<GameObject>>();
        readonly Dictionary<string, int> _autoBakeCursor = new Dictionary<string, int>();
        readonly HashSet<GameObject> _autoBakesPlaced = new HashSet<GameObject>();
        readonly HashSet<string> _autoMissingLogged = new HashSet<string>();
        // Which of them have gone down. A hand-made block stands on its own pad or
        // not at all - one whose pad the spacing never rolled sits this city out.
        readonly HashSet<GameObject> _lotBakesPlaced = new HashSet<GameObject>();
        // A roll the stock roller SEEDED with a hand-made block - the block stood in a
        // corner of a bigger pad and the rest randomised round it (BlockLotStock) -
        // and the block it carries. The same block must not stand on its own pad as
        // well: a roll is passed over while its seed's own pad is still to come or
        // already holds it, and a seed that stood inside a roll is not laid again.
        readonly Dictionary<GameObject, List<GameObject>> _seedsIn =
            new Dictionary<GameObject, List<GameObject>>();
        readonly HashSet<GameObject> _seedsStanding = new HashSet<GameObject>();
        readonly HashSet<GameObject> _ownPadComing = new HashSet<GameObject>();
        // A PLACE stands once in the whole city: one fairground, one palm tower, one
        // hotel. Which places already stand, by member name, and which bake brought
        // each - so a bake carrying one of them is passed over, whichever pool it is
        // in. The stock roller keeps the same rule while composing; this is the same
        // rule at placement, where hand-made blocks, rolls and the loose feature pool
        // meet and can double each other.
        readonly Dictionary<string, string> _landmarkOwner = new Dictionary<string, string>();
        readonly Dictionary<GameObject, string[]> _landmarkCache = new Dictionary<GameObject, string[]>();
        // Every prefab in the blocks folder by name, so a nested composed block inside
        // a bake (b2block1 stands residentialblock2 in its yard) is read as fabric
        // rather than taken for a place of its own.
        readonly HashSet<string> _bakeNames = new HashSet<string>();
        // Which members are FABRIC (the anonymous City_XX terrace, the storefronts) and
        // which are places is LivingCity.Generation.BlockFabric's rule, shared with the
        // roller so a block that stood on the catalog pad is never refused here.
        // What every prop clone in a bake is called - dressing, never a place.
        const string PropPrefix = "SM_";
        readonly List<GameObject> _featureBlocks = new List<GameObject>();
        readonly List<LotInfo> _lotPlans = new List<LotInfo>();
        LivingCity.CameraRig.BuildingCardPicker _picker;
        readonly Dictionary<GameObject, Bounds> _prefabBoundsCache = new Dictionary<GameObject, Bounds>();

        readonly HashSet<long> _cells = new HashSet<long>();
        RoadNode[,] _nodes;
        readonly List<RoadEdge> _edges = new List<RoadEdge>();
        /// <summary>The city's lane network: the carriageways (both ways, with their
        /// lanes and the kerbs cars park at), the junctions and their connectors -
        /// what every car drives (RoadCar), and what the men on foot read to keep
        /// off the road's users.</summary>
        public LaneNet Net { get; private set; }
        readonly List<TrafficSignal> _signals = new List<TrafficSignal>();
        readonly List<DemoVehicle> _vehicles = new List<DemoVehicle>();
        readonly List<CivilianAgent> _pedestrians = new List<CivilianAgent>();
        readonly List<PedLink> _pedLinks = new List<PedLink>();
        SignalMaterials _signalMats;

        // street-life bookkeeping: facade doors and bench spots noted while the
        // geometry goes down, wired to the sidewalk graph once it exists
        // The owner rides along because a door is the one thing in this city that knows
        // WHICH BUILDING it belongs to - the sidewalk graph, the crowd and the front
        // card all reach a building through its door, and nothing else here does.
        readonly List<(Vector3 pos, Vector3 outward, GameObject owner)> _pendingDoors =
            new List<(Vector3, Vector3, GameObject)>();
        readonly List<(Vector3 pos, float yaw)> _pendingBenches =
            new List<(Vector3, float)>();
        CityLife _life;
        float _chatScan;

        Transform _geometry, _flora, _traffic, _cars, _blocks;

        // Where the load goes, pass by pass. The city is thirty-odd passes deep and it
        // takes the better part of a minute to stand up; which of them is the minute is
        // not a thing to guess at, so each one is timed and the lot is printed once,
        // dearest first. Costs nothing to leave in - one Stopwatch read per pass.
        readonly System.Collections.Generic.List<(string Name, long Ms)> _passMs =
            new System.Collections.Generic.List<(string, long)>();
        readonly System.Diagnostics.Stopwatch _buildClock = new System.Diagnostics.Stopwatch();

        void Pass(string name, System.Action run)
        {
            long at = _buildClock.ElapsedMilliseconds;
            run();
            _passMs.Add((name, _buildClock.ElapsedMilliseconds - at));
        }

        /// <summary>The pass table, dearest first, with everything under a twentieth of
        /// the load rolled into one line: the short passes are two dozen and none of them
        /// is the answer.</summary>
        void ReportBuildTime()
        {
            long total = _buildClock.ElapsedMilliseconds;
            _passMs.Sort((a, b) => b.Ms.CompareTo(a.Ms));
            var line = new System.Text.StringBuilder();
            line.Append($"[RoadDemo] the city stood up in {total} ms:");
            long rest = 0;
            foreach (var (name, ms) in _passMs)
            {
                if (total > 0 && ms * 20 < total) { rest += ms; continue; }
                line.Append($" {name} {ms}");
            }
            line.Append($" | everything else {rest} (ms)");
            Debug.Log(line.ToString());
        }

        void Awake()
        {
#if UNITY_EDITOR
            _buildClock.Start();
            long prefabsAt = _buildClock.ElapsedMilliseconds;
            if (!LoadPrefabs()) return;
            _passMs.Add(("LoadPrefabs", _buildClock.ElapsedMilliseconds - prefabsAt));
            _geometry = new GameObject("Geometry").transform;
            // palms live outside the static-batched root: their wind shader displaces
            // vertices in object space, and a combined mesh would swing them around
            // the batch pivot instead of their own trunks
            _flora = new GameObject("Flora").transform;
            // block bakes carry PalmCity palms/bushes whose wind shader displaces
            // vertices in object space â€” the root must stay out of static batching
            _blocks = new GameObject("Blocks").transform;
            _traffic = new GameObject("Traffic").transform;
            _cars = new GameObject("Cars").transform;

            // the number of the city, and the street plan and the seams it draws:
            // first of everything, because every pass below reads what it writes
            Pass("PlanCity", PlanCity);
            // no freeways in this town, whatever the inspector or an old scene says:
            // the Highway seams come out of the list before the grid is spaced on them
            Pass("NoFreeways", NoFreeways);
            Pass("Respace", Respace);
            // which blocks are downtown and which are the rim: nothing is built off the
            // zoning, but the closures, the bakes and the pocket parks all ask it
            Pass("PlanZones", PlanZones);
            // and which streets simply stop. Before ANY geometry: the junctions cap
            // themselves off it, the lane graph skips the segments it shuts, and the
            // quarters and the map read the same predicate the rest of the city does.
            Pass("PlanCloses", PlanCloses);
            // island or peninsula, and which way the country lies - before the quarters
            // are rolled, because the port refuses a landlocked shore
            Pass("PlanShoreline", PlanShoreline);
            // the belt freeway's line round the grid, which the freeway lands on and the
            // quarters stand outside of
            Pass("PlanBelt", PlanBelt);
            // the quarters that are not the grid - the port, the suburbs, the airport -
            // decide where they stand before anything is laid: the island has to ring
            // them, and the junctions they hang off have to know their streets run on out
            Pass("PlanDistricts", PlanDistricts);
            // and the city's own parts get their names: nothing is built for them, but
            // the map prints them and the ledger will want to say which one a block is in
            Pass("PlanQuarters", PlanQuarters);
            Pass("FenceCity", FenceCity);
            Pass("ScaleLifeToCity", ScaleLifeToCity);
            Pass("BuildNodes", BuildNodes);
            Pass("BuildRoadsAndSidewalks", BuildRoadsAndSidewalks);
            Pass("BuildBlocks", BuildBlocks);
            Pass("BuildSeams", BuildSeams);
            // the closed streets, grassed over into walks - after the seams, since a
            // close is a pocket park and is made of the parks' own kit
            Pass("BuildCloses", BuildCloses);
            Pass("DressStreets", DressStreets);
            // the elevated freeway between two quarters: its ground works and its own
            // junctions, before the graph, which welds them to the grid
            // (RoadDemoBuilder.Freeway.cs)
            Pass("BuildFreeway", BuildFreeway);
            // and the expressway: its line, its decks on their piers, its ramps and the
            // streets its interchanges need (RoadDemoBuilder.Expressway.cs)
            Pass("BuildExpressway", BuildExpressway);
            Pass("BuildGraph", BuildGraph);
            Pass("BuildSignals", BuildSignals);
            // the ramp terminals and the gates the branches die on are junctions like
            // any other, and they are not in the grid's own array
            Pass("SignalExpressway", SignalExpressway);
            Pass("DressExpressway", DressExpressway);
            Pass("BuildPedGraph", BuildPedGraph);
            Pass("BuildWalkClearance", BuildWalkClearance);
            // the belt freeway round the city: into the lane graph before the quarters'
            // streets are welded on, because those cross it at its junctions
            Pass("BuildBelt", BuildBelt);
            // the quarters themselves, and the streets that weld them to the grid
            Pass("BuildDistricts", BuildDistricts);
            // the freeway's terminal link roads (a city with no belt), once the
            // connectors they cross are in
            Pass("BuildHighwayLinks", BuildHighwayLinks);
            Pass("BuildCityLife", BuildCityLife);
            Pass("SpawnCars", SpawnCars);
            Pass("SpawnBikes", SpawnBikes);
            Pass("SpawnPolice", SpawnPolice);
            Pass("SpawnPedestrians", SpawnPedestrians);
            Pass("SpawnCrews", SpawnCrews);
            Pass("BuildEnvironment", BuildEnvironment);
            Pass("BuildDayNight", BuildDayNight);
            Pass("BuildExhaust", BuildExhaust);
            Pass("BuildAudio", BuildAudio);
            Pass("BuildMap", BuildMap);
            Pass("BuildLotOverlay", BuildLotOverlay);

            Pass("OptimiseScene", OptimiseScene);
            Pass("AssignCullLayers", AssignCullLayers);
            ReportBuildTime();
            // the merge itself waits for the first Update: every Start (the night
            // windows, the map, the lamps) must see the pieces first
#else
            Debug.LogError("[RoadDemo] This demo loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }

        void Update()
        {
            if (!_merged) MergeStaticGeometry();
            float dt = Time.deltaTime;
            // where the frame goes, section by section, logged every few seconds while
            // updateProfile is on: the crowd and the traffic are the two that scale with
            // the city, and one of them being ten times the other is the whole story
            TickTimer.Frame();
            for (int i = 0; i < _signals.Count; i++) _signals[i].UpdateBulbs(_signalMats);
            TickTimer.Mark(0, "signals");
            for (int i = 0; i < _vehicles.Count; i++) _vehicles[i].Tick(dt);
            // and where a job has been ordered, the street is thinned out for it: the
            // cars nobody is looking at are lifted off it (StreetTraffic.Thin), one
            // every second or so, while the ones in shot drive out on their own
            StreetTraffic.Thin(_vehicles, _edges, Camera.main);
            TickTimer.Mark(1, "cars");
            for (int i = 0; i < _policeCars.Count; i++) _policeCars[i].TickPatrol(dt);
            // the cars that want petrol drive with the traffic above (they are in
            // _vehicles) and this is only the errand on top of it - the booking, the two
            // curves across the forecourt, and the man who gets out
            for (int i = 0; i < _fuelCustomers.Count; i++) _fuelCustomers[i].TickErrand(dt);
            TickTimer.Mark(2, "patrol cars");
            for (int i = 0; i < _pedestrians.Count; i++) _pedestrians[i].TickCivilian(dt);
            TickTimer.Mark(3, "civilians");
            CivilianAgent.TickCrowd(dt); // who is in the road, who drifts over to stare
            TickTimer.Mark(4, "crowd");
            for (int i = 0; i < _policeOfficers.Count; i++) _policeOfficers[i].TickPatrol(dt);
            TickTimer.Mark(5, "officers");
            TickDistricts(dt);           // the ships, the cranes, the forklifts, the yard hands
            TickTimer.Mark(6, "districts");

            // two civilians meeting head-on may stop for a word; scanned on a
            // slow throttle, not per frame
            _chatScan -= dt;
            if (_chatScan <= 0f && _life != null && _life.CanChat)
            {
                _chatScan = 1.5f;
                CivilianAgent.PairChats(_pedestrians, chatSeconds);
            }
            TickTimer.Mark(7, "chats");
            TickWaysideWatch(dt);
            TickTimer.Report(updateProfile, dt,
                $"{_vehicles.Count} cars, {_pedestrians.Count} civilians, " +
                $"{_policeCars.Count + _policeOfficers.Count} police, {_districtWalkers.Count} district hands");
        }

        void OnDestroy()
        {
            for (int i = 0; i < _pedestrians.Count; i++) _pedestrians[i].Dispose();
            for (int i = 0; i < _policeOfficers.Count; i++) _policeOfficers[i].Dispose();
            DisposeWayside();
            DisposeDistricts();
        }

        // Edit-mode sketch of the network: the real geometry only exists after
        // pressing Play, so the Scene view shows the planned layout instead.
        void OnDrawGizmos()
        {
            if (Application.isPlaying) return;
            if (verticalRoadX == null || horizontalRoadZ == null ||
                verticalRoadX.Length == 0 || horizontalRoadZ.Length == 0) return;

            int nv = verticalRoadX.Length, nh = horizontalRoadZ.Length;
            if (verticalIsBoulevard == null || verticalIsBoulevard.Length < nv ||
                horizontalIsBoulevard == null || horizontalIsBoulevard.Length < nh) return;

            // the sketch draws the plan Play would build, not the authored spacing
            float[] vx = PlanLine(verticalRoadX, verticalIsBoulevard, blockWidths, 0, true);
            float[] hz = PlanLine(horizontalRoadZ, horizontalIsBoulevard, blockDepths, 1, false);

            var street = new Color(1f, 1f, 1f, 0.35f);
            var avenue = new Color(1f, 0.8f, 0.2f, 0.5f);
            var water = new Color(0.25f, 0.5f, 0.9f, 0.45f);
            var lawn = new Color(0.3f, 0.7f, 0.3f, 0.45f);
            var deck = new Color(0.6f, 0.6f, 0.65f, 0.5f);
            Color SeamColour(Seam s) => s.kind == SeamKind.River ? water : s.kind == SeamKind.Highway ? deck : lawn;

            // the seams first, under the roads: the river's water, the park's lawn
            for (int j = 0; j + 1 < nh; j++)
                if (SeamAt(false, j) is Seam s)
                {
                    float a = hz[j] + HHalf(j) + Sidewalk, b = hz[j + 1] - HHalf(j + 1) - Sidewalk;
                    Gizmos.color = SeamColour(s);
                    Gizmos.DrawCube(new Vector3((vx[0] + vx[nv - 1]) * 0.5f, -0.05f, (a + b) * 0.5f),
                        new Vector3(vx[nv - 1] - vx[0] + 2f * BoulevardHalf, 0.1f, b - a));
                }
            for (int i = 0; i + 1 < nv; i++)
                if (SeamAt(true, i) is Seam s)
                {
                    float a = vx[i] + VHalf(i) + Sidewalk, b = vx[i + 1] - VHalf(i + 1) - Sidewalk;
                    Gizmos.color = SeamColour(s);
                    Gizmos.DrawCube(new Vector3((a + b) * 0.5f, -0.05f, (hz[0] + hz[nh - 1]) * 0.5f),
                        new Vector3(b - a, 0.1f, hz[nh - 1] - hz[0] + 2f * BoulevardHalf));
                }

            for (int i = 0; i < nv; i++)
                for (int j = 0; j < nh; j++)
                {
                    Gizmos.color = verticalIsBoulevard[i] || horizontalIsBoulevard[j] ? avenue : street;
                    Gizmos.DrawCube(new Vector3(vx[i], 0f, hz[j]),
                        new Vector3(VHalf(i) * 2f, 0.1f, HHalf(j) * 2f));
                }

            for (int i = 0; i < nv; i++)
                for (int j = 0; j + 1 < nh; j++)
                {
                    if (!SegmentOpen(true, i, j)) continue;
                    float a = hz[j] + HHalf(j), b = hz[j + 1] - HHalf(j + 1);
                    Gizmos.color = verticalIsBoulevard[i] ? avenue : street;
                    Gizmos.DrawCube(new Vector3(vx[i], 0f, (a + b) * 0.5f),
                        new Vector3(VHalf(i) * 2f, 0.1f, b - a));
                }

            for (int j = 0; j < nh; j++)
                for (int i = 0; i + 1 < nv; i++)
                {
                    if (!SegmentOpen(false, j, i)) continue;
                    float a = vx[i] + VHalf(i), b = vx[i + 1] - VHalf(i + 1);
                    Gizmos.color = horizontalIsBoulevard[j] ? avenue : street;
                    Gizmos.DrawCube(new Vector3((a + b) * 0.5f, 0f, hz[j]),
                        new Vector3(b - a, 0.1f, HHalf(j) * 2f));
                }
        }

        // A pack material as a runtime instance, so the demo can retune it without
        // dirtying the shared asset. Null when the asset is gone (or in a player
        // build) - every caller falls back to plain colour.
        static Material LoadMaterial(string path)
        {
#if UNITY_EDITOR
            var src = RoadDemo.DemoAssetLoad.Load<Material>(path);
            return src != null ? new Material(src) : null;
#else
            return null;
#endif
        }

#if UNITY_EDITOR
        static GameObject Load(string path)
        {
            var go = RoadDemo.DemoAssetLoad.Load<GameObject>(path);
            if (go == null) Debug.LogError("[RoadDemo] missing prefab: " + path);
            return go;
        }

        static List<string> ScanPrefabPaths(string[] folders, string[] denySubstrings)
        {
            var paths = new List<string>();
            foreach (var guid in RoadDemo.DemoAssetLoad.Find("t:Prefab", folders))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                string low = path.ToLowerInvariant();
                bool denied = false;
                foreach (var deny in denySubstrings)
                    if (low.Contains(deny)) { denied = true; break; }
                if (!denied) paths.Add(path);
            }
            return paths;
        }

        bool LoadPrefabs()
        {
            _roadHalf = Load(CityEnv + "SM_Env_Road_YellowLines_02.prefab");
            _laneEdge = Load(CityEnv + "SM_Env_Road_02.prefab");
            _laneDash = Load(CityEnv + "SM_Env_Road_Lines_01.prefab");
            _median = Load(CityEnv + "SM_Env_Road_Median_01.prefab");
            _bare = Load(CityEnv + "SM_Env_Road_Bare_01.prefab");
            _crossing = Load(CityEnv + "SM_Env_Road_Crossing_01.prefab");
            _bareCracked = Load(CityEnv + "SM_Env_Road_03.prefab");
            _roadPatch = Load(CityEnv + "SM_Env_Road_Patch_01.prefab");
            _swStraight = Load(CityEnv + "SM_Env_Sidewalk_Straight_01.prefab");
            _swCorner = Load(CityEnv + "SM_Env_Sidewalk_Corner_01.prefab");
            _divider = Load(CityEnv + "SM_Env_Street_Divider_01.prefab");
            _poleBase = Load(CityProps + "SM_Prop_LightPole_Base_01.prefab");
            _poleArm = Load(CityProps + "SM_Prop_LightPole_Arm_01.prefab");
            _poleLights = Load(CityProps + "SM_Prop_LightPole_Lights_01.prefab");

            for (int i = 1; i <= 6; i++)
            {
                var palm = RoadDemo.DemoAssetLoad.Load<GameObject>(
                    PalmEnv + "SM_Env_Tree_Palm_0" + i + ".prefab");
                if (palm != null) _palms.Add(palm);
            }

            // road vehicles from every Synty pack in the project; boats, aircraft,
            // two-wheelers and attachment parts are filtered out by name
            string[] vehicleFolders =
            {
                "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles",
                "Assets/Synty/PolygonCity/Prefabs/Vehicles",
                "Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles",
            };
            string[] vehicleDeny =
            {
                "boat", "yacht", "jetski", "helicopter", "plane", "cart", "scooter",
                "bike", "moped", "bot", "steering", "wheel", "trailer", "monster",
                "quad", "attach",
            };
            foreach (var path in ScanPrefabPaths(vehicleFolders, vehicleDeny))
            {
                if (!System.IO.Path.GetFileName(path).StartsWith("SM_Veh")) continue;
                // bodies that may not reach a scene at all, whatever the scan turns up
                if (LivingCity.Gameplay.VehicleCatalog.IsBarred(path)) continue;
                // anybody's marked vehicle - the law, the ambulance, the coastguard -
                // is on a call, and a car on a call does not queue at a light with the
                // rest of the traffic. Asked with the PATH, because the police pack's
                // own names give nothing away: "SM_Veh_Car_01" and "SM_Veh_Van_01" are
                // liveried cruisers, and the old name filter ("police" in the name)
                // drove all four of them as ordinary traffic
                if (LivingCity.Gameplay.VehicleCatalog.IsMarkedService(path)) continue;
                var v = RoadDemo.DemoAssetLoad.Load<GameObject>(path);
                if (v == null) continue;
                // duplicate-as-weight: every pool in the demo is drawn from uniformly, so the
                // only place a mix can be tuned is the list itself. An exotic takes one seat
                // where a saloon takes six (VehicleCatalog.PoolWeight)
                for (int seat = 0, seats = LivingCity.Gameplay.VehicleCatalog.PoolWeight(path);
                     seat < seats; seat++)
                    _carPrefabs.Add(v);
            }

            foreach (var name in LivingCity.Gameplay.VehicleCatalog.PoliceCars)
                foreach (var folder in vehicleFolders)
                {
                    var car = RoadDemo.DemoAssetLoad.Load<GameObject>(
                        folder + "/" + name + ".prefab");
                    if (car == null) continue;
                    _policeCarPrefabs.Add(car);
                    break; // the first pack that has it; the catalog names one body
                }
            if (_policeCarPrefabs.Count == 0)
                Debug.LogWarning("[RoadDemo] No marked cruiser out of VehicleCatalog.PoliceCars; " +
                                 "police patrol disabled");

            // the patrol overlay dresses itself out of DemoUi - the demo's one
            // wardrobe, so its dot and popup match the top bar and the ledger

            // people from every Synty pack; only humanoid-rigged prefabs qualify
            // (the walk clip retargets onto any humanoid avatar)
            string[] characterFolders =
            {
                "Assets/Synty/PolygonPalmCity/Prefabs/Characters",
                "Assets/Synty/PolygonCity/Prefabs/Characters",
                "Assets/Synty/PolygonPoliceStation/Prefabs/Characters",
                "Assets/Synty/PolygonGeneric/Prefabs/Characters",
            };
            string[] characterDeny = { "attach", "charred", "skeleton", "robot", "space", "underwear" };
            foreach (var path in ScanPrefabPaths(characterFolders, characterDeny))
            {
                var chr = RoadDemo.DemoAssetLoad.Load<GameObject>(path);
                if (chr == null) continue;
                var animator = chr.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman) continue;
                // The force is the police station pack and nothing else - one uniform,
                // the way the patrols are one fleet (VehicleCatalog.PoliceCars). Its
                // officers walk the beat.
                if (System.IO.Path.GetFileName(path).StartsWith("SM_Chr_Officer"))
                    _officerPrefabs.Add(chr);
                // costumes the scan drags in with the people - a prisoner, a forensic
                // technician, a sea captain, the city pack's second uniform
                else if (LivingCity.Entities.CrowdLooks.IsBarred(path))
                    continue;
                // a body the mob may be dealt is nobody's passer-by: a coat that stands
                // on one corner as one of Falcone's men must not walk past on the next
                // as a nobody (GangLooks.IsGangBody - the two cast tables are the rule)
                else if (!LivingCity.Gangs.GangLooks.IsGangBody(chr.name))
                    _pedPrefabs.Add(chr);
            }
            const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
            void Bag(List<GameObject> into, params string[] names)
            {
                foreach (var n in names)
                {
                    var g = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + n + ".prefab");
                    if (g == null)
                        g = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmEnv + n + ".prefab");
                    if (g != null) into.Add(g);
                }
            }
            Bag(_grates, "SM_Env_Plant_Grate_01", "SM_Env_Plant_Grate_02");
            // Lamp_01 only: the tall arm post that hangs its head over the carriageway.
            // Lamp_08 is the short symmetric park/promenade post - it reads as pier
            // furniture on a kerb, and the other Lamp_0x models have no bulb point in
            // DemoStreetLamps.LampKinds, so they would stand dark while neighbours burn.
            Bag(_lamps, "SM_Prop_Street_Lamp_01");
            // Bin_01 and Bin_04 are the public litter bins the palm city keeps at
            // its kerbs; Bin_02 is a building's own bin (against the wall, with the
            // bags); Bin_03 is a dumpster and belongs in an alley, not on a pavement
            Bag(_bins, "SM_Prop_Trash_Bin_01", "SM_Prop_Trash_Bin_04");
            Bag(_wallBins, "SM_Prop_Trash_Bin_02");
            Bag(_benches, "SM_Prop_Bench_Seat_01", "SM_Prop_Bench_Seat_02");
            Bag(_planters, "SM_Prop_Planter_01", "SM_Prop_Planter_02", "SM_Prop_Planter_03", "SM_Prop_Planter_04");
            // only the free-standing cabinet: PowerBox_02 hangs from its pivot (laid on
            // the ground it is sunk into it) and the PowerBoxes_0x are wall boards
            Bag(_powerboxes, "SM_Prop_Powerbox_01");
            Bag(_bushes, "SM_Env_Bush_01", "SM_Env_Bush_02", "SM_Env_Bush_03");
            Bag(_wires, "SM_Prop_Powerline_02", "SM_Prop_Powerline_03");
            Bag(_chairs, "SM_Prop_Chair_01", "SM_Prop_Chair_03", "SM_Prop_Chair_04");
            Bag(_tables, "SM_Prop_Table_01", "SM_Prop_Table_Outdoor_01");
            Bag(_umbrellas, "SM_Prop_Umbrella_01", "SM_Prop_Umbrella_02", "SM_Prop_Umbrella_03");
            _bag = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Trash_Bag_01.prefab");
            _bagOpen = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Trash_Bag_Open_01.prefab");
            _bollard = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Bollard_02.prefab");
            _hydrant = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Fire_Hydrant_01.prefab");
            _mailbox = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Mailbox_01.prefab");
            _newsstand = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Newspaper_Stand_01.prefab");
            _powerpole = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Powerpole_01.prefab");
            _bikeStand = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Bike_Stand_02.prefab");
            _signPole = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Sign_Pole_02.prefab");
            _manhole = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Manhole_01.prefab");
            // the rest of what a 1987 kerb carries, all of it in the palm city's props
            _treeCage = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Tree_Cage_01.prefab");
            _banner = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Street_Flag_Sign_02.prefab");
            _meter = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Parking_Meter_01.prefab");
            _payPhone = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Pay_Phone_01.prefab");
            _menuStand = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmProps + "SM_Prop_Menu_Stand_01.prefab");
            _pave = RoadDemo.DemoAssetLoad.Load<GameObject>(PalmEnv + "SM_Env_Sidewalk_01.prefab");
            if (_pave == null) Debug.LogWarning("[RoadDemo] SM_Env_Sidewalk_01 missing; courts fall back to asphalt");

            _blockPrefab = RoadDemo.DemoAssetLoad.Load<GameObject>(BlockPrefabPath);
            if (_blockPrefab == null)
                Debug.LogWarning("[RoadDemo] block bake missing (" + BlockPrefabPath + "); interiors stay empty");

            // feature interiors: the auto-extracted palm block bakes plus the
            // police station from the building catalog
            for (int i = 2; i <= 8; i++)
            {
                var block = RoadDemo.DemoAssetLoad.Load<GameObject>(
                    BlocksDir + PalmBlockPrefix + "0" + i + ".prefab");
                if (block != null) _featureBlocks.Add(block);
                else Debug.LogWarning("[RoadDemo] missing block bake: " + PalmBlockPrefix + "0" + i);
            }
            var police = RoadDemo.DemoAssetLoad.Load<GameObject>(PoliceStationPath);
            if (police != null) _featureBlocks.Add(police);
            else Debug.LogWarning("[RoadDemo] missing prefab: " + PoliceStationPath);

            // and every block composed by hand into the folder, on top of those
            LoadBlockBakes();

            AnimationClip PeopleClip(string name) =>
                RoadDemo.DemoAssetLoad.Load<AnimationClip>(
                    "Assets/Animations/People/" + name + ".anim");
            _walkClip = CrewKit.StockWalk;
            _idleClip = CrewKit.StockIdle;
            if (_walkClip == null || _idleClip == null || _pedPrefabs.Count == 0)
                Debug.LogWarning("[RoadDemo] pedestrian assets missing; spawning without people");

            // street life: the sit chain (down / loop / up) and the chat loops.
            // All optional - a missing clip just switches that behaviour off.
            _sitDownClip = PeopleClip("Idle-Sitting_Bench");
            _sitLoopClip = PeopleClip("Sitting_Bench_Idle");
            _standUpClip = PeopleClip("Sitting-Idle");
            _talkClip = PeopleClip("Standing_Talking");
            _shoutClip = PeopleClip("Standing_Shouting");
            if (_sitDownClip == null || _sitLoopClip == null || _standUpClip == null)
                Debug.LogWarning("[RoadDemo] bench sit clips missing; nobody will sit down");
            if (_talkClip == null)
                Debug.LogWarning("[RoadDemo] Standing_Talking missing; nobody will stop to chat");

            return _roadHalf && _laneEdge && _laneDash && _median && _bare &&
                   _crossing && _swStraight && _swCorner && _divider &&
                   _poleBase && _poleArm && _poleLights && _carPrefabs.Count > 0;
        }

        // The whole blocks folder, sorted into the two pools the city draws from.
        // Nothing composed into that folder is left out of the generated city: it is
        // read wholesale rather than from a list of paths kept here, so a block built
        // by hand in the catalog scene and saved beside the others is in the next city
        // with no edit to this file.
        //
        //   BlockLotTag on the root -> filed under that pad's code, and it goes in an
        //     interior of that size and nowhere else. A block captured off a pad is
        //     baked with the tag (SyntyCityBlocks); a block saved out of the catalog
        //     scene by hand has none unless someone added it, and a name that starts
        //     with a pad code ("b2block1" -> B2) is taken as saying the same thing.
        //     Named with the roller's prefix ("auto_") it is a ROLL rather than an
        //     arrangement, and it is filed in the stock pool, which ranks below.
        //   no lot of its own -> the feature pool, laid in any interior it fits.
        //
        // Two families are left out because the loader placed them already: the
        // generic terrace, which is the fallback rather than a pool member, and the
        // PalmBlock candidates, of which only the kept numbers are wanted.
        void LoadBlockBakes()
        {
            _lotBakes.Clear();
            _lotBakeCursor.Clear();
            _autoBakes.Clear();
            _autoBakeCursor.Clear();
            _bakeNames.Clear();

            var guids = RoadDemo.DemoAssetLoad.Find(
                "t:Prefab", new[] { BlocksDir.TrimEnd('/') });
            System.Array.Sort(guids, (a, b) => string.CompareOrdinal(
                UnityEditor.AssetDatabase.GUIDToAssetPath(a),
                UnityEditor.AssetDatabase.GUIDToAssetPath(b)));

            var loose = new List<string>();
            foreach (var guid in guids)
            {
                var prefab = RoadDemo.DemoAssetLoad.Load<GameObject>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null) continue;
                _bakeNames.Add(prefab.name);
                if (prefab == _blockPrefab) continue;
                if (prefab.name.StartsWith(PalmBlockPrefix)) continue;

                var size = PrefabBoundsOf(prefab).size;
                if (size.x < MinBakeFootprint || size.z < MinBakeFootprint)
                {
                    Debug.LogWarning($"[RoadDemo] {prefab.name} measures {size.x:F1} x {size.z:F1} m " +
                                     "- too small to stand in a lot as a block, so it is left out of " +
                                     "the city. Recompose it in the catalog scene if it lost its buildings.");
                    continue;
                }

                var code = LotCodeOf(prefab);
                if (code == null)
                {
                    _featureBlocks.Add(prefab);
                    loose.Add($"{prefab.name} ({size.x:F0} x {size.z:F0} m)");
                    continue;
                }

                // Rolled stock is filed apart from the blocks somebody composed: both
                // are authored for a pad, but a roll is only ever there to fill an
                // interior no hand-made block wanted.
                var pool = prefab.name.StartsWith(AutoBlockPrefix) ? _autoBakes : _lotBakes;
                if (!pool.TryGetValue(code, out var list))
                    pool[code] = list = new List<GameObject>();
                list.Add(prefab);
            }

            // Biggest first: the pool is handed to the roomiest interiors in order,
            // and a hand-made block composed on a 100 x 95 pad has to meet a lot that
            // can hold it before a small palm bake takes the space.
            _featureBlocks.Sort((a, b) =>
            {
                var sa = PrefabBoundsOf(a).size;
                var sb = PrefabBoundsOf(b).size;
                int byArea = (sb.x * sb.z).CompareTo(sa.x * sa.z);
                return byArea != 0 ? byArea : string.CompareOrdinal(a.name, b.name);
            });

            DropLooseStationIfComposed();

            // Which rolls carry a hand-made block inside them (a seeded roll stands one
            // as a member, under the block's own name), read off the bakes: the demo
            // must not stand that block twice, once as itself and once as the seed.
            _seedsIn.Clear();
            var handMade = new Dictionary<string, GameObject>();
            foreach (var pair in _lotBakes)
                foreach (var bake in pair.Value)
                    handMade[bake.name] = bake;
            var seeded = new List<string>();
            foreach (var pair in _autoBakes)
                foreach (var roll in pair.Value)
                    foreach (Transform child in roll.transform)
                        if (handMade.TryGetValue(child.name, out var seed))
                        {
                            if (!_seedsIn.TryGetValue(roll, out var list))
                                _seedsIn[roll] = list = new List<GameObject>();
                            if (!list.Contains(seed)) list.Add(seed);
                            seeded.Add($"{roll.name} carries {seed.name}");
                        }

            if (loose.Count > 0)
                Debug.Log("[RoadDemo] free block bakes - " + string.Join("; ", loose));
            if (seeded.Count > 0)
                Debug.Log("[RoadDemo] seeded rolls - " + string.Join("; ", seeded));
            Report("lot bakes", _lotBakes);
            Report("rolled stock", _autoBakes);
            if (_autoBakes.Count == 0)
                Debug.LogWarning("[RoadDemo] no rolled stock in " + BlocksDir + " - interiors with " +
                                 "no block composed for them fall back to the feature pool and the " +
                                 "generic terrace. Run Tools/City/Catalog/Randomise Blocks For Every " +
                                 "Lot to give every lot size a block of its own.");
        }

        // One station to a city. It reaches the streets either inside the block composed
        // round it (c2policestation) or loose out of the feature pool - with both in play,
        // a grid that holds that block gets a second station standing in a lot of its own.
        void DropLooseStationIfComposed()
        {
            foreach (var pair in _lotBakes)
                foreach (var bake in pair.Value)
                {
                    if (StationIn(bake) == null) continue;
                    if (_featureBlocks.RemoveAll(b => b.name.StartsWith(StationName)) > 0)
                        Debug.Log("[RoadDemo] the station comes with " + bake.name +
                                  ", so it is not packed loose as well");
                    return;
                }
        }

        void Report(string what, Dictionary<string, List<GameObject>> pools)
        {
            if (pools.Count == 0) return;
            var filed = new List<string>();
            foreach (var pair in pools)
                filed.Add(pair.Key + ": " + string.Join(", ", pair.Value.ConvertAll(p => p.name)));
            filed.Sort(System.StringComparer.Ordinal);
            Debug.Log("[RoadDemo] " + what + " - " + string.Join("; ", filed));
        }

        // The pad a bake belongs to, or null for one that belongs to no pad in
        // particular. The tag is the authority; a name that opens with a pad code is
        // read as the same claim, so a block saved as "b2block1" lands on the B2 pads
        // without anyone having to add the component by hand.
        string LotCodeOf(GameObject prefab)
        {
            var tag = prefab.GetComponent<LivingCity.Generation.BlockLotTag>();
            if (tag != null && !string.IsNullOrEmpty(tag.lot))
                return tag.lot.Trim().ToUpperInvariant();

            var name = prefab.name;
            if (name.Length < 2) return null;
            int column = char.ToUpperInvariant(name[0]) - 'A';
            int row = name[1] - '1';
            bool inPalette = blockWidths != null && blockDepths != null &&
                             column >= 0 && column < blockWidths.Length &&
                             row >= 0 && row < blockDepths.Length;
            return inPalette ? $"{(char)('A' + column)}{row + 1}" : null;
        }
#endif

        // The police station inside a block, or null. A bake's members are its direct
        // children and keep the prefab's name, which is the same test the feature packer
        // makes on what it lays - so a station reaches the city the same way whether it
        // was packed loose or composed into a block. Outside the editor-only block above
        // because the placement reads it, and placement is not editor-only.
        const string StationName = "building-policestation";

        static GameObject StationIn(GameObject block)
        {
            if (block == null) return null;
            foreach (Transform child in block.transform)
                if (child.name.StartsWith(StationName)) return child.gameObject;
            return null;
        }

        // ------------------------------------------------------------------ layout

        // Every road runs the full width of the map: the network is the plain grid
        // the verticalRoadX / horizontalRoadZ arrays describe. A junction only lacks
        // a leg where the map itself ends - or where a seam lies that this road does
        // not cross: a street ends on the river's quay and at the park's edge, a
        // boulevard bridges the one and drives through the other (SegmentOpen).
        // - or where a district hangs off this edge of the grid and its street runs on
        // out of the city to it (OutwardArm): there the junction gets its zebra and its
        // crossing like any other, and the connecting street is laid across the strip.
        bool NorthOpen(int i, int j) => j + 1 < horizontalRoadZ.Length
            ? SegmentOpen(true, i, j) : OutwardArm(i, j, 0);
        bool SouthOpen(int i, int j) => j > 0
            ? SegmentOpen(true, i, j - 1) : OutwardArm(i, j, 2);
        bool EastOpen(int i, int j) => i + 1 < verticalRoadX.Length
            ? SegmentOpen(false, j, i) : OutwardArm(i, j, 1);
        bool WestOpen(int i, int j) => i > 0
            ? SegmentOpen(false, j, i - 1) : OutwardArm(i, j, 3);

        float VHalf(int i) => verticalIsBoulevard[i] ? BoulevardHalf : StreetHalf;
        float HHalf(int j) => horizontalIsBoulevard[j] ? BoulevardHalf : StreetHalf;

        // ---- what the top-down map reads the city off ----
        //
        // DemoMap draws the plan rather than photographing it, so it needs the same
        // three numbers the kit is laid on: where a carriageway ends, where a block
        // interior begins, and how wide the sidewalk ring between them runs.

        /// <summary>Carriageway half-width of vertical road i (boulevards are wider).</summary>
        public float VerticalHalfWidth(int i) => VHalf(i);

        /// <summary>Carriageway half-width of horizontal road j.</summary>
        public float HorizontalHalfWidth(int j) => HHalf(j);

        /// <summary>The sidewalk ring between a carriageway and a block interior.</summary>
        public static float SidewalkWidth => Sidewalk;

        /// <summary>Every block the demo laid out. Filled by BuildBlocks and never
        /// touched again: the map draws the slabs, the O overlay prints the rest.</summary>
        public IReadOnlyList<LotInfo> LotPlans => _lotPlans;

        /// <summary>Every stretch of pavement in the city and every zebra across a
        /// carriageway - the graph the crowd itself walks, the quarters' own walks
        /// folded in (RegisterPavement). Both directions of each stretch are in here:
        /// anything drawing it has to take one of the pair. The map draws these as the
        /// pavements, because a walk the crowd does not use is a walk that is not
        /// there.</summary>
        public IReadOnlyList<PedLink> Pavement => _pedLinks;

        /// <summary>What the city calls its streets, rolled off the grid's own seed:
        /// one name per road line, the same names every time this city is built. The
        /// map letters them along the streets; anything else that has to name a place
        /// (a card, a job in the ledger) should ask here rather than roll its own.</summary>
        public StreetNames Streets => _streets ??
            (_streets = new StreetNames(spacingSeed * 31 + cityLayoutSeed,
                verticalIsBoulevard, horizontalIsBoulevard));

        StreetNames _streets;

        /// <summary>
        /// One block interior as it was PLANNED - the rectangle a bake has to stay
        /// inside, the catalog pad that rectangle answers to, and what ended up
        /// standing on it.
        ///
        /// The sizes are the plan's own, not a measurement of the geometry afterwards:
        /// a bake is allowed a metre of overhang onto the sidewalk, so measuring the
        /// result back would answer with the building instead of the lot.
        /// </summary>
        public readonly struct LotInfo
        {
            /// <summary>Grid cell: the interior between vertical roads Column and
            /// Column+1, horizontal roads Row and Row+1.</summary>
            public readonly int Column, Row;

            /// <summary>Kerb to kerb minus the sidewalk ring - the lot pad rectangle
            /// itself, in world XZ.</summary>
            public readonly Rect Interior;

            /// <summary>The interior plus its sidewalk ring, which is what the eye
            /// reads as one block; the map's slab.</summary>
            public readonly Rect Slab;

            /// <summary>The catalog scene's pad code for this size ("B2"), or null
            /// when the interior is not one of the palette sizes and so has no pad
            /// with anything composed on it.</summary>
            public readonly string Code;

            /// <summary>What was built here, in the words the overlay prints.</summary>
            public readonly string Contents;

            /// <summary>The lot was left as a pocket park rather than built on: lawn,
            /// paths and trees where a bake would have stood (RoadDemoBuilder.Zones.cs).
            /// The plan draws it green, and it carries no frontage and no business.</summary>
            public readonly bool Green;

            public LotInfo(int column, int row, Rect interior, Rect slab, string code,
                string contents, bool green = false)
            {
                Column = column;
                Row = row;
                Interior = interior;
                Slab = slab;
                Code = code;
                Contents = contents;
                Green = green;
            }

            public float Width => Interior.width;
            public float Depth => Interior.height;

            /// <summary>The interior in kit cells - the unit a block is composed in.</summary>
            public float CellsWide => Interior.width / Cell;
            public float CellsDeep => Interior.height / Cell;
        }

        // The catalog's pad code for an interior size: a letter for the width column
        // (A is the first palette entry) and a number for the depth row, which is
        // exactly what the pads in the catalog scene are named ("Lot B2 (85x70)").
        // Derived from the palettes here rather than read off BlockLotPads, which
        // lives in the editor assembly runtime code cannot reference - the two lists
        // are kept the same list by hand, and that tool's own comment says so.
        // Null = a size with no pad, which is worth seeing in the overlay: nothing was
        // ever composed for it.
        string LotCode(float width, float depth)
        {
            int w = PaletteIndex(blockWidths, width);
            int d = PaletteIndex(blockDepths, depth);
            return w < 0 || d < 0 ? null : $"{(char)('A' + w)}{d + 1}";
        }

        static int PaletteIndex(float[] palette, float size)
        {
            if (palette == null) return -1;
            for (int k = 0; k < palette.Length; k++)
                if (Mathf.Abs(palette[k] - size) <= LotMatchTolerance) return k;
            return -1;
        }

        // ---------------------------------------------------------- block sizing

        // Moves the roads onto the planned spacing. Play only, and it writes the
        // public arrays: everything downstream reads the grid off them, and Unity
        // hands them back the authored values when Play stops.
        void Respace()
        {
            if (!randomiseBlockSizes && !AnySeam(true) && !AnySeam(false)) return;
            verticalRoadX = PlanLine(verticalRoadX, verticalIsBoulevard, blockWidths, 0, true);
            horizontalRoadZ = PlanLine(horizontalRoadZ, horizontalIsBoulevard, blockDepths, 1, false);

            var sizes = new List<string>();
            for (int i = 0; i + 1 < verticalRoadX.Length; i++)
                for (int j = 0; j + 1 < horizontalRoadZ.Length; j++)
                {
                    if (InSeam(i, j)) continue;
                    sizes.Add((verticalRoadX[i + 1] - VHalf(i + 1) - Sidewalk - verticalRoadX[i] - VHalf(i) - Sidewalk)
                              .ToString("F0") + "x" +
                              (horizontalRoadZ[j + 1] - HHalf(j + 1) - Sidewalk - horizontalRoadZ[j] - HHalf(j) - Sidewalk)
                              .ToString("F0"));
                }
            Debug.Log($"[RoadDemo] block interiors (seed {spacingSeed}): " + string.Join(", ", sizes) +
                      SeamStory());
        }

        // The interiors between two roads are what the eye reads as a block, so the
        // sizes are drawn first and the centrelines follow from them:
        //
        //   x[i+1] = x[i] + half(i) + sidewalk + interior + sidewalk + half(i+1)
        //
        // The sizes are dealt from the palette in order and then shuffled rather
        // than rolled per gap - a handful of uniform rolls clusters around the
        // middle, which is exactly the "every block the same width" look this is
        // here to break. A palette rather than a low-to-high range because the
        // sizes are not free: each one has a lot pad in the catalog scene and a
        // bake composed on it, and interpolating between two of them would invent
        // widths nothing was ever built for. More gaps than palette entries wraps,
        // so a size comes up twice. Every size is snapped to the 5 m cell, so the
        // kit's pieces tile the lot frontages; the pavements are wider than the
        // cell, so the centrelines are not multiples of 5, and a carriageway
        // closes the odd metres between its crossings by stretching its tiles.
        //
        // A gap that is a seam (the river, the park) is not dealt from the palette at
        // all: it takes the seam's own width, and the palette is dealt over the block
        // gaps only, so a river through the middle costs no block its size.
        float[] PlanLine(float[] authored, bool[] boulevard, float[] palette, int salt, bool verticalLines)
        {
            int n = authored == null ? 0 : authored.Length;
            if (n == 0 || boulevard == null || boulevard.Length < n) return authored;
            if (n < 2) return (float[])authored.Clone();
            if (!randomiseBlockSizes && !AnySeam(verticalLines)) return (float[])authored.Clone();
            if (palette == null || palette.Length == 0) return (float[])authored.Clone();

            int gaps = n - 1;
            var spans = new float[gaps];
            var seamAt = new bool[gaps];
            var blockGaps = new List<int>();
            for (int k = 0; k < gaps; k++)
            {
                var seam = SeamAt(verticalLines, k);
                if (seam != null)
                {
                    seamAt[k] = true;
                    spans[k] = Mathf.Max(MinInterior, Mathf.Round(seam.width / Cell) * Cell);
                }
                else blockGaps.Add(k);
            }
            int free = blockGaps.Count;
            var dealt = new float[free];
            for (int k = 0; k < free; k++)
                dealt[k] = Mathf.Max(MinInterior,
                    Mathf.Round(palette[k % palette.Length] / Cell) * Cell);
            if (!randomiseBlockSizes)
            {
                // seams only, no shuffle: the authored spacing for the blocks
                for (int k = 0; k < free; k++)
                {
                    int g = blockGaps[k];
                    float halfA = boulevard[g] ? BoulevardHalf : StreetHalf;
                    float halfB = boulevard[g + 1] ? BoulevardHalf : StreetHalf;
                    dealt[k] = Mathf.Max(MinInterior, authored[g + 1] - authored[g] - halfA - halfB - 2f * Sidewalk);
                }
            }

            // its own generator rather than UnityEngine.Random: the street plan must
            // not shift because some later pass drew one more bush
            var rng = new System.Random(spacingSeed * 397 + salt);
            if (randomiseBlockSizes)
                for (int k = free - 1; k > 0; k--)
                {
                    int swap = rng.Next(k + 1);
                    (dealt[k], dealt[swap]) = (dealt[swap], dealt[k]);
                }

            // Two identical sizes side by side read as one overlong block sliced in
            // half - the very look the shuffle is here to break. With more gaps than
            // palette entries the repeat itself is unavoidable, so it gets pushed
            // apart rather than removed: the second of the pair trades places with
            // the first later span that leaves neither of them beside its own size.
            if (randomiseBlockSizes)
                for (int k = 1; k < free; k++)
                {
                    if (dealt[k] != dealt[k - 1]) continue;
                    for (int m = k + 1; m < free; m++)
                    {
                        if (dealt[m] == dealt[k]) continue;                  // nothing gained
                        if (m - 1 > k && dealt[m - 1] == dealt[k]) continue; // pair only moves
                        if (m + 1 < free && dealt[m + 1] == dealt[k]) continue;
                        (dealt[k], dealt[m]) = (dealt[m], dealt[k]);
                        break;
                    }
                }
            for (int k = 0; k < free; k++) spans[blockGaps[k]] = dealt[k];

            var line = new float[n];
            line[0] = Mathf.Round(authored[0] / Cell) * Cell;
            for (int k = 0; k < gaps; k++)
            {
                float halfHere = boulevard[k] ? BoulevardHalf : StreetHalf;
                float halfNext = boulevard[k + 1] ? BoulevardHalf : StreetHalf;
                line[k + 1] = line[k] + halfHere + Sidewalk + spans[k] + Sidewalk + halfNext;
            }
            return line;
        }

        // a tenth of a metre: the centrelines are off the 5 m grid by the pavements'
        // odd half metres, so two cells can sit closer than a cell apart
        static long CellKey(float mx, float mz)
            => ((long)Mathf.RoundToInt(mx * 10f) << 32) ^ (uint)Mathf.RoundToInt(mz * 10f);

        /// <summary>The kit's 5 m piece laid to cover [mx, mx+sizeX] x [mz, mz+sizeZ]
        /// exactly: pivot at its +X/+Z corner (turned by the yaw), scaled to the
        /// size. A pavement wider than the tile, a carriageway run a few per cent
        /// off the beat - the same piece, stretched.</summary>
        void PlaceTile(GameObject prefab, float mx, float mz, int yaw, float sizeX, float sizeZ, float y = 0f)
        {
            if (prefab == null) return;
            Vector3 pivot, scale;
            switch (yaw)
            {
                case 0:
                    pivot = new Vector3(mx + sizeX, y, mz + sizeZ);
                    scale = new Vector3(sizeX / Cell, 1f, sizeZ / Cell);
                    break;
                case 90:   // local +Z -> world +X, local +X -> world -Z
                    pivot = new Vector3(mx + sizeX, y, mz);
                    scale = new Vector3(sizeZ / Cell, 1f, sizeX / Cell);
                    break;
                case 180:
                    pivot = new Vector3(mx, y, mz);
                    scale = new Vector3(sizeX / Cell, 1f, sizeZ / Cell);
                    break;
                default:   // 270: local +Z -> world -X, local +X -> world +Z
                    pivot = new Vector3(mx, y, mz + sizeZ);
                    scale = new Vector3(sizeZ / Cell, 1f, sizeX / Cell);
                    break;
            }
            var go = Instantiate(prefab, pivot, Quaternion.Euler(0f, yaw, 0f), _geometry);
            if ((scale - Vector3.one).sqrMagnitude > 1e-6f) go.transform.localScale = scale;
        }

        /// <summary>How many tiles close [from, to] at nearest to the 5 m beat.</summary>
        static int TileCount(float from, float to) => Mathf.Max(1, Mathf.RoundToInt((to - from) / Cell));

        void PlaceCell(GameObject prefab, float mx, float mz, int yaw, float y = 0f)
        {
            Vector3 pivot;
            switch (yaw)
            {
                case 0: pivot = new Vector3(mx + Cell, y, mz + Cell); break;
                case 90: pivot = new Vector3(mx + Cell, y, mz); break;
                case 180: pivot = new Vector3(mx, y, mz); break;
                default: pivot = new Vector3(mx, y, mz + Cell); break;
            }
            Instantiate(prefab, pivot, Quaternion.Euler(0f, yaw, 0f), _geometry);
        }

        void PlaceCellOnce(GameObject prefab, float mx, float mz, int yaw)
        {
            if (_cells.Add(CellKey(mx, mz))) PlaceCell(prefab, mx, mz, yaw);
        }

        void PlaceTileOnce(GameObject prefab, float mx, float mz, int yaw, float sizeX, float sizeZ)
        {
            if (_cells.Add(CellKey(mx, mz))) PlaceTile(prefab, mx, mz, yaw, sizeX, sizeZ);
        }

        /// <summary>Tiles that close [lo, hi] across a road at nearest to the 5 m
        /// beat: each one's near edge (off the axis) and its width.</summary>
        static (float off, float w)[] Band(float lo, float hi)
        {
            int n = TileCount(lo, hi);
            float w = (hi - lo) / n;
            var band = new (float, float)[n];
            for (int k = 0; k < n; k++) band[k] = (lo + k * w, w);
            return band;
        }

        /// <summary>The zebra across a carriageway, kerb to kerb - the parking strips
        /// included, a crossing runs to the stone. A street is three 5 m tiles; a
        /// boulevard is a band either side of the median, stretched a little since
        /// 12.5 m does not sit on the beat (the median has its own pieces).</summary>
        static (float off, float w)[] CrossingTiles(bool boulevard)
        {
            if (!boulevard) return Band(-StreetHalf, StreetHalf);
            var west = Band(-BoulevardHalf, -MedianHalf);
            var east = Band(MedianHalf, BoulevardHalf);
            var both = new (float, float)[west.Length + east.Length];
            west.CopyTo(both, 0);
            east.CopyTo(both, west.Length);
            return both;
        }

        void BuildNodes()
        {
            _nodes = new RoadNode[verticalRoadX.Length, horizontalRoadZ.Length];
            for (int i = 0; i < verticalRoadX.Length; i++)
                for (int j = 0; j < horizontalRoadZ.Length; j++)
                {
                    var n = new RoadNode
                    {
                        I = i, J = j,
                        X = verticalRoadX[i], Z = horizontalRoadZ[j],
                        XMin = verticalRoadX[i] - VHalf(i), XMax = verticalRoadX[i] + VHalf(i),
                        ZMin = horizontalRoadZ[j] - HHalf(j), ZMax = horizontalRoadZ[j] + HHalf(j),
                    };
                    _nodes[i, j] = n;
                    BuildNodeGeometry(n);
                }
        }

        void BuildNodeGeometry(RoadNode n)
        {
            // "has" means a road actually continues that way - the map edge and a
            // closed segment read the same here, and both get the sidewalk cap that
            // turns this junction into a T or a bend
            bool north = NorthOpen(n.I, n.J);
            bool south = SouthOpen(n.I, n.J);
            bool east = EastOpen(n.I, n.J);
            bool west = WestOpen(n.I, n.J);
            bool vBlvd = verticalIsBoulevard[n.I];
            bool hBlvd = horizontalIsBoulevard[n.J];

            for (float mx = n.XMin; mx < n.XMax - 0.1f; mx += Cell)
                for (float mz = n.ZMin; mz < n.ZMax - 0.1f; mz += Cell)
                    PlaceCellOnce(_bare, mx, mz, 0);

            // north / south: zebra across the vertical road, or a sidewalk cap
            foreach (var side in new[] { (edge: n.ZMax, capEdge: n.ZMax, has: north, capYaw: 180), (edge: n.ZMin - Cell, capEdge: n.ZMin - Sidewalk, has: south, capYaw: 0) })
            {
                if (side.has)
                {
                    foreach (var t in CrossingTiles(vBlvd))
                        PlaceTileOnce(_crossing, n.X + t.off, side.edge, 90, t.w, Cell);
                    if (vBlvd)
                    {
                        PlaceCellOnce(_median, n.X - Cell, side.edge, 180);
                        PlaceCellOnce(_median, n.X, side.edge, 0);
                    }
                }
                else
                {
                    // the cap is pavement, as deep as the pavement
                    for (float mx = n.XMin; mx < n.XMax - 0.1f; mx += Cell)
                        PlaceTile(_swStraight, mx, side.capEdge, side.capYaw, Cell, Sidewalk);
                }
            }

            // east / west: zebra across the horizontal road, or a sidewalk cap
            foreach (var side in new[] { (edge: n.XMax, capEdge: n.XMax, has: east, capYaw: 270), (edge: n.XMin - Cell, capEdge: n.XMin - Sidewalk, has: west, capYaw: 90) })
            {
                if (side.has)
                {
                    foreach (var t in CrossingTiles(hBlvd))
                        PlaceTileOnce(_crossing, side.edge, n.Z + t.off, 0, Cell, t.w);
                    if (hBlvd)
                    {
                        PlaceCellOnce(_median, side.edge, n.Z - Cell, 90);
                        PlaceCellOnce(_median, side.edge, n.Z, 270);
                    }
                }
                else
                {
                    for (float mz = n.ZMin; mz < n.ZMax - 0.1f; mz += Cell)
                        PlaceTile(_swStraight, side.capEdge, mz, side.capYaw, Sidewalk, Cell);
                }
            }

            // corner slabs, kerb turned towards the intersection centre, as wide
            // and as deep as the pavements they join
            PlaceTile(_swCorner, n.XMin - Sidewalk, n.ZMin - Sidewalk, 0, Sidewalk, Sidewalk);
            PlaceTile(_swCorner, n.XMin - Sidewalk, n.ZMax, 90, Sidewalk, Sidewalk);
            PlaceTile(_swCorner, n.XMax, n.ZMax, 180, Sidewalk, Sidewalk);
            PlaceTile(_swCorner, n.XMax, n.ZMin - Sidewalk, 270, Sidewalk, Sidewalk);
        }

        void BuildRoadsAndSidewalks()
        {
            for (int i = 0; i < verticalRoadX.Length; i++)
                for (int j = 0; j + 1 < horizontalRoadZ.Length; j++)
                    FillVerticalSegment(i, _nodes[i, j], _nodes[i, j + 1]);

            for (int j = 0; j < horizontalRoadZ.Length; j++)
                for (int i = 0; i + 1 < verticalRoadX.Length; i++)
                    FillHorizontalSegment(j, _nodes[i, j], _nodes[i + 1, j]);
        }

        void FillVerticalSegment(int i, RoadNode a, RoadNode b)
        {
            // a street that ends on the river or at the park lays nothing here: the
            // junction caps at both ends already closed it (BuildNodeGeometry)
            if (!SegmentOpen(true, i, a.J)) return;
            // a boulevard over the river is a bridge: the same carriageway, but the
            // sidewalks are the bridge kit's walkway-and-parapet, and the median
            // carries no palms out over the water
            bool bridge = IsBridge(true, i, a.J);
            float cx = verticalRoadX[i];
            bool blvd = verticalIsBoulevard[i];
            float half = VHalf(i);

            // the carriageway between the two zebra bands, closed exactly: the
            // lot's frontage plus the pavements' odd metres, a few per cent off
            // the 5 m beat, so the tiles are stretched a hair rather than left
            // a gap or lapped over the crossing
            float from = a.ZMax + Cell, to = b.ZMin - Cell;
            int tiles = TileCount(from, to);
            float len = (to - from) / tiles;
            for (int k = 0; k < tiles; k++)
            {
                float mz = from + k * len;
                if (blvd)
                {
                    PlaceTile(_laneEdge, cx - 15f, mz, 0, Cell, len);
                    PlaceTile(_laneDash, cx - 10f, mz, 180, Cell, len);
                    PlaceTile(_median, cx - 5f, mz, 180, Cell, len);
                    PlaceTile(_median, cx, mz, 0, Cell, len);
                    PlaceTile(_laneDash, cx + 5f, mz, 0, Cell, len);
                    PlaceTile(_laneEdge, cx + 10f, mz, 180, Cell, len);
                }
                else
                {
                    // the two halves face each other: each lays its yellow line on the
                    // crown and its white line on its own kerb
                    PlaceTile(_roadHalf, cx - 5f, mz, 0, Cell, len);
                    PlaceTile(_roadHalf, cx, mz, 180, Cell, len);
                }
                // the kerb strips, outside the last marked lane on either side: plain
                // asphalt, where a car is left standing (the meters on the pavement
                // beside them are the road demo's, and always were)
                PlaceTile(_bare, cx - half, mz, 0, ParkLane, len);
                PlaceTile(_bare, cx + half - ParkLane, mz, 0, ParkLane, len);
            }
            // the pavements down the lot frontage, between the corner slabs: the
            // lot is on the 5 m beat, so these tile it exactly, wide as the pavement
            if (!bridge)
                for (float mz = a.ZMax + Sidewalk; mz < b.ZMin - Sidewalk - 0.1f; mz += Cell)
                {
                    PlaceTile(_swStraight, cx - half - Sidewalk, mz, 90, Sidewalk, Cell);
                    PlaceTile(_swStraight, cx + half, mz, 270, Sidewalk, Cell);
                }
            if (bridge) { DressBridge(true, i, a, b); return; }

            if (!blvd) return;
            int step = 0;
            for (float mz = a.ZMax + 2f * Cell; mz <= b.ZMin - 3f * Cell; mz += Cell, step++)
            {
                Instantiate(_divider, new Vector3(cx, 0f, mz), Quaternion.identity, _geometry);
                if (step % 3 == 1 && _palms.Count > 0)
                    Prop(_palms[step % _palms.Count],
                        new Vector3(cx, 0.18f, mz + 2.5f), step * 77f, _flora);
                else if (step % 3 == 2 && _bushes.Count > 0)
                    Prop(_bushes[step % _bushes.Count],
                        new Vector3(cx + Random.Range(-0.4f, 0.4f), 0.15f, mz + 2.5f),
                        step * 53f, _flora);
            }
            // the palm rows on the two pavements: laid through Prop like any other
            // tree, so the people walking under them know they are there
            for (float mz = a.ZMax + 2f * Cell; mz <= b.ZMin - 2f * Cell; mz += 4f * Cell)
                if (_palms.Count > 0)
                {
                    Prop(_palms[(int)(mz / Cell) % _palms.Count],
                        new Vector3(cx - (BoulevardHalf + Sidewalk - 1.1f), 0.1f, mz), mz * 13f, _flora);
                    Prop(_palms[(int)(mz / Cell + 3) % _palms.Count],
                        new Vector3(cx + (BoulevardHalf + Sidewalk - 1.1f), 0.1f, mz), mz * 29f, _flora);
                }
        }

        void FillHorizontalSegment(int j, RoadNode a, RoadNode b)
        {
            if (!SegmentOpen(false, j, a.I)) return;
            bool bridge = IsBridge(false, j, a.I);
            float cz = horizontalRoadZ[j];
            bool blvd = horizontalIsBoulevard[j];
            float half = HHalf(j);

            float from = a.XMax + Cell, to = b.XMin - Cell;
            int tiles = TileCount(from, to);
            float len = (to - from) / tiles;
            for (int k = 0; k < tiles; k++)
            {
                float mx = from + k * len;
                if (blvd)
                {
                    PlaceTile(_laneEdge, mx, cz - 15f, 270, len, Cell);
                    PlaceTile(_laneDash, mx, cz - 10f, 90, len, Cell);
                    PlaceTile(_median, mx, cz - 5f, 90, len, Cell);
                    PlaceTile(_median, mx, cz, 270, len, Cell);
                    PlaceTile(_laneDash, mx, cz + 5f, 270, len, Cell);
                    PlaceTile(_laneEdge, mx, cz + 10f, 90, len, Cell);
                }
                else
                {
                    PlaceTile(_roadHalf, mx, cz - 5f, 270, len, Cell);
                    PlaceTile(_roadHalf, mx, cz, 90, len, Cell);
                }
                PlaceTile(_bare, mx, cz - half, 90, len, ParkLane);
                PlaceTile(_bare, mx, cz + half - ParkLane, 90, len, ParkLane);
            }
            if (!bridge)
                for (float mx = a.XMax + Sidewalk; mx < b.XMin - Sidewalk - 0.1f; mx += Cell)
                {
                    PlaceTile(_swStraight, mx, cz - half - Sidewalk, 0, Cell, Sidewalk);
                    PlaceTile(_swStraight, mx, cz + half, 180, Cell, Sidewalk);
                }
            if (bridge) { DressBridge(false, j, a, b); return; }

            if (!blvd) return;
            int step = 0;
            for (float mx = a.XMax + 2f * Cell; mx <= b.XMin - 3f * Cell; mx += Cell, step++)
            {
                Instantiate(_divider, new Vector3(mx, 0f, cz), Quaternion.Euler(0f, 90f, 0f), _geometry);
                if (step % 3 == 1 && _palms.Count > 0)
                    Prop(_palms[step % _palms.Count],
                        new Vector3(mx + 2.5f, 0.18f, cz), step * 61f, _flora);
                else if (step % 3 == 2 && _bushes.Count > 0)
                    Prop(_bushes[step % _bushes.Count],
                        new Vector3(mx + 2.5f, 0.15f, cz + Random.Range(-0.4f, 0.4f)),
                        step * 53f, _flora);
            }
            for (float mx = a.XMax + 2f * Cell; mx <= b.XMin - 2f * Cell; mx += 4f * Cell)
                if (_palms.Count > 0)
                {
                    Prop(_palms[(int)(mx / Cell) % _palms.Count],
                        new Vector3(mx, 0.1f, cz - (BoulevardHalf + Sidewalk - 1.1f)), mx * 13f, _flora);
                    Prop(_palms[(int)(mx / Cell + 3) % _palms.Count],
                        new Vector3(mx, 0.1f, cz + (BoulevardHalf + Sidewalk - 1.1f)), mx * 29f, _flora);
                }
        }

        // --------------------------------------------------------- block interiors

        // A bake's pivot is not its footprint centre - residentialblock1's is the
        // midpoint of its two cluster pivots, and its AABB spans X[-36.15, 34.16],
        // Z[-30.11, 20.34], so the centre sits (-0.99, 0, -4.88) off the pivot.
        // Measuring it per bake keeps that compensation right for block2 as well.
        Vector3 BlockPivotToCentre(GameObject bake)
        {
            var c = PrefabBoundsOf(bake).center;
            return new Vector3(c.x, 0f, c.z);
        }

        const float BlockAlley = 6f; // service gap between two packed blocks

        float _floorLevel = -1f;

        // Interior floors run flush with the street sidewalk's top surface, the
        // way the PalmCity demo's blocks continue at pavement level â€” a court
        // left at carriageway height reads as a sunken pit behind the kerb.
        float FloorLevel()
        {
            if (_floorLevel < 0f) _floorLevel = PrefabBoundsOf(_swStraight).max.y;
            return _floorLevel;
        }

        // Whether a bake may stand in an interior of this size.
        //
        // A bake composed on the catalog pad of this very size fits by definition: the
        // roller keeps every BUILDING inside the pad, and what its renderer box shows
        // past the kerb is dressing - a palm canopy, a lamp arm, a bay marking - which
        // the 5 m sidewalk ring has room for. Measuring that box against the interior
        // is what once refused nearly the whole stock by a few centimetres (auto_A2_1
        // at 71.02 m for a 70 m pad, and so on down the list) and left A2 lots empty
        // and A3 lots half-packed out of the feature pool instead.
        //
        // Anything else - the terrace, the loose palm bakes, a block with no pad -
        // is one fixed footprint that has to be measured, and a metre of overhang is
        // allowed: the terrace ends meeting the kerb read better than a gap.
        bool Fits(GameObject bake, float width, float depth)
        {
            if (bake == null) return false;
            var tag = bake.GetComponent<LivingCity.Generation.BlockLotTag>();
            if (tag != null && !string.IsNullOrEmpty(tag.lot) &&
                tag.lot.Trim().ToUpperInvariant() == LotCode(width, depth))
                return true;
            var size = PrefabBoundsOf(bake).size;
            return width + 1f >= size.x && depth + 1f >= size.z;
        }

        // The places a bake stands, by member name, read off its hierarchy: a bake's
        // members are its direct children under the prefab's own name, its props are
        // clones named SM_*. A PalmBlock counts as a place itself AND brings its
        // buildings, so the loose PalmBlock_07 in the feature pool and the one
        // composed into a rolled block meet on "Fairground" whichever way round they
        // come. A composed block nested in another (b2block1 stands residentialblock2
        // in its yard) is fabric - it is neither a place nor walked into, or the
        // terrace standing on its own A1 pad would be refused for the coffee shop
        // inside its twin. A single building loose in the feature pool (the station)
        // is its own name.
        string[] LandmarksOf(GameObject bake)
        {
            if (!_landmarkCache.TryGetValue(bake, out var found))
            {
                var set = new HashSet<string>();
                bool palm = bake.name.StartsWith(PalmBlockPrefix);
                bool composed = _bakeNames.Contains(bake.name) && !palm;
                if (!composed) set.Add(bake.name);
                if (composed || palm) CollectLandmarks(bake.transform, set);
                // a seeded roll carries its seed's places too - the one nested block
                // that is known for what it is
                if (_seedsIn.TryGetValue(bake, out var seeds))
                    foreach (var seed in seeds)
                        set.UnionWith(LandmarksOf(seed));
                found = new string[set.Count];
                set.CopyTo(found);
                _landmarkCache[bake] = found;
            }
            return found;
        }

        void CollectLandmarks(Transform root, HashSet<string> into)
        {
            foreach (Transform child in root)
            {
                string name = child.name;
                if (name.StartsWith(PropPrefix) ||
                    LivingCity.Generation.BlockFabric.IsFabric(name)) continue;
                if (_bakeNames.Contains(name))
                {
                    if (!name.StartsWith(PalmBlockPrefix)) continue;
                    into.Add(name);
                    CollectLandmarks(child, into);
                    continue;
                }
                into.Add(name);
            }
        }

        // The place in this bake that another bake already brought to the city, or
        // null when nothing in it is spoken for. A place this bake claimed itself
        // (PlanOwnPads claims ahead for the hand-made blocks that will stand) does
        // not count against it. Logged once per bake and pool, so the console says
        // why a block was passed over rather than the lot just coming up different.
        string SpentLandmark(GameObject bake)
        {
            foreach (var mark in LandmarksOf(bake))
                if (_landmarkOwner.TryGetValue(mark, out var owner) && owner != bake.name)
                    return mark;
            return null;
        }

        readonly HashSet<GameObject> _landmarkSkipLogged = new HashSet<GameObject>();

        bool CarriesSpentLandmark(GameObject bake, string where)
        {
            var mark = SpentLandmark(bake);
            if (mark == null) return false;
            if (_landmarkSkipLogged.Add(bake))
                Debug.Log($"[RoadDemo] {bake.name} passed over for {where}: {mark} already " +
                          $"stands in {_landmarkOwner[mark]} - one to a city");
            return true;
        }

        void ClaimLandmarks(GameObject bake)
        {
            foreach (var mark in LandmarksOf(bake))
                if (!_landmarkOwner.ContainsKey(mark)) _landmarkOwner[mark] = bake.name;
        }

        // Whether every place this bake carries is its own - true for one that stood
        // (it claimed them) or has none, false for one passed over because another
        // block got there first.
        bool OwnsItsPlaces(GameObject bake)
        {
            foreach (var mark in LandmarksOf(bake))
                if (_landmarkOwner.TryGetValue(mark, out var owner) && owner != bake.name)
                    return false;
            return true;
        }

        // PalmBlock_07 bakes in the Synty ferris wheel as dead geometry; its rotate
        // pivot gets the demo's own spin, whether the fairground came loose out of
        // the feature pool or composed inside a block.
        static void SpinFerrisWheels(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>())
                if (t.name.Contains("Ferris") && t.name.Contains("_Rotate"))
                    t.gameObject.AddComponent<DemoFerrisWheel>();
        }

        // The block authored FOR this interior, if one was ever composed on its pad.
        // An interior of 85 x 70 m is the catalog's B2 pad, and a bake that carries
        // "B2" was arranged against exactly that rectangle - so it goes there and
        // nowhere else, ahead of the feature bakes and the generic terrace.
        //
        // Several bakes may carry the same code; they are handed out in turn, so a
        // city with four B2 interiors shows four different blocks before it repeats.
        // Null = nothing was composed for this size (or what was overflows it).
        GameObject LotBakeFor(float width, float depth)
        {
            var code = LotCode(width, depth);
            if (code == null || !_lotBakes.TryGetValue(code, out var bakes) || bakes.Count == 0)
                return null;

            // With a stock to fill the rest of the city, a composed block goes down ONCE:
            // showing the same one twice while a roll waits unused wastes the only blocks
            // in the folder somebody actually arranged. With no stock the pool still
            // wraps, because the alternative there is the generic terrace.
            bool once = _autoBakes.Count > 0;

            // the pool is handed out in turn from blockCycle, which is 0 for the city
            // and stepped by the block lab to look at the next one on the same pad
            if (!_lotBakeCursor.TryGetValue(code, out int cursor)) cursor = blockCycle;
            for (int k = 0; k < bakes.Count; k++)
            {
                var bake = bakes[(cursor + k) % bakes.Count];
                if (once && _lotBakesPlaced.Contains(bake)) continue;
                if (!Fits(bake, width, depth)) continue;
                if (CarriesSpentLandmark(bake, $"lot {code}")) continue;
                if (_seedsStanding.Contains(bake))
                {
                    if (_landmarkSkipLogged.Add(bake))
                        Debug.Log($"[RoadDemo] {bake.name} passed over for lot {code}: it already " +
                                  "stands in the corner of a seeded roll");
                    continue;
                }
                _lotBakeCursor[code] = (cursor + k + 1) % bakes.Count;
                _lotBakesPlaced.Add(bake);
                ClaimLandmarks(bake);
                return bake;
            }

            // Nothing left to hand out is the ordinary end of the pool, not a fault -
            // the roll below takes over. Only a pool that fits NOWHERE is worth a word.
            for (int k = 0; k < bakes.Count; k++)
                if (Fits(bakes[k], width, depth))
                    return null;

            // Every bake filed under this code was composed ON that pad, so not
            // fitting means the arrangement grew past it - say so rather than
            // quietly substituting the terrace and leaving the lot looking untouched.
            if (_lotOverflowLogged.Add(code))
            {
                var names = new List<string>();
                foreach (var bake in bakes)
                {
                    var size = PrefabBoundsOf(bake).size;
                    names.Add($"{bake.name} ({size.x:F1} x {size.z:F1} m)");
                }
                Debug.LogWarning($"[RoadDemo] no bake for lot {code} fits its {width:F0} x " +
                                 $"{depth:F0} m interior: {string.Join(", ", names)}. Recapture " +
                                 "them inside the pad; the lot takes a rolled block instead.");
            }
            return null;
        }

        // The catalog's own roll for this pad size, handed out in turn. Everything the
        // hand-made pool could not cover comes from here: one run of Tools/City/Catalog/
        // Randomise Blocks For Every Lot leaves three blocks for every pad size, which is
        // more than any one grid has interiors, so a lot of this size is never the same
        // block twice. Null = nothing was rolled for this size, or what was overflows it.
        GameObject StockBakeFor(float width, float depth)
        {
            var code = LotCode(width, depth);
            if (code == null) return null;
            if (!_autoBakes.TryGetValue(code, out var bakes) || bakes.Count == 0)
            {
                if (_autoBakes.Count > 0 && _autoMissingLogged.Add(code))
                    Debug.LogWarning($"[RoadDemo] nothing was rolled for lot {code} ({width:F0} x " +
                                     $"{depth:F0} m) - that pad may be missing from the catalog " +
                                     "scene. Run Draw Block Lot Pads, then Randomise Blocks For " +
                                     "Every Lot.");
                return null;
            }

            // One that has not stood yet, before one that has. A place stands once in
            // the city whichever block brings it, so a roll carrying a fairground that
            // is already up is passed over in both passes - a stock block laid twice
            // is allowed only when it is all terrace, which nobody can tell apart.
            if (!_autoBakeCursor.TryGetValue(code, out int cursor)) cursor = blockCycle;
            int spent = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int k = 0; k < bakes.Count; k++)
                {
                    var bake = bakes[(cursor + k) % bakes.Count];
                    if (pass == 0 && _autoBakesPlaced.Contains(bake)) continue;
                    // laid again only when it is all terrace: a roll that brought a
                    // place owns it now, and owning it is no licence to show it twice
                    if (pass == 1 && LandmarksOf(bake).Length > 0) continue;
                    if (!Fits(bake, width, depth)) continue;
                    if (CarriesSpentLandmark(bake, $"lot {code}")) { spent++; continue; }
                    // a seeded roll waits while its hand-made block has a pad of its
                    // own coming in this grid, or already stands on it
                    if (SeedElsewhere(bake) is GameObject seed)
                    {
                        if (_landmarkSkipLogged.Add(bake))
                            Debug.Log($"[RoadDemo] {bake.name} passed over for lot {code}: its seed " +
                                      $"{seed.name} stands on its own pad in this city");
                        continue;
                    }
                    _autoBakeCursor[code] = (cursor + k + 1) % bakes.Count;
                    _autoBakesPlaced.Add(bake);
                    ClaimLandmarks(bake);
                    if (_seedsIn.TryGetValue(bake, out var seeds))
                        foreach (var s in seeds) _seedsStanding.Add(s);
                    return bake;
                }
            if (spent > 0 && _autoMissingLogged.Add(code))
                Debug.LogWarning($"[RoadDemo] every block rolled for lot {code} that fits it carries " +
                                 "a place already standing in the city. Re-roll the stock (Tools/" +
                                 "City/Catalog/Randomise Blocks For Every Lot): a roll made under " +
                                 "the one-place-per-city rule puts each place in one block only.");
            return null;
        }

        // The hand-made block inside a seeded roll that stands, or will stand, on its
        // own pad in this city - null when the roll is free to go down. Interiors are
        // built largest first, so the roll (a bigger pad) is asked before the seed's
        // own pad comes up; _ownPadComing is what answers for the pads still to come.
        GameObject SeedElsewhere(GameObject roll)
        {
            if (!_seedsIn.TryGetValue(roll, out var seeds)) return null;
            foreach (var seed in seeds)
                if (_lotBakesPlaced.Contains(seed) || _ownPadComing.Contains(seed))
                    return seed;
            return null;
        }

        // The hand-made blocks this grid will stand on their own pads: for each pad
        // code with interiors in the plan, the first bakes filed under it that fit,
        // as many as there are interiors - the same order LotBakeFor hands them out
        // in. Everything else filed under a code sits the city out, unless a seeded
        // roll happens to bring it in the corner of a bigger lot.
        //
        // Their places are claimed here and now, ahead of every roll and of the loose
        // feature pool: interiors are built largest first, so a bigger lot is filled
        // before a hand-made block's own pad comes up, and without this a roll or a
        // loose palm bake standing the diner first would cost b2block1 its pad.
        void PlanOwnPads(List<(int i, int j, float xMin, float xMax, float zMin, float zMax, Rect slab)> lots)
        {
            _ownPadComing.Clear();
            var interiors = new Dictionary<string, (int count, float width, float depth)>();
            foreach (var lot in lots)
            {
                float width = lot.xMax - lot.xMin, depth = lot.zMax - lot.zMin;
                var code = LotCode(width, depth);
                if (code == null) continue;
                interiors.TryGetValue(code, out var have);
                interiors[code] = (have.count + 1, width, depth);
            }
            foreach (var pair in interiors)
            {
                if (!_lotBakes.TryGetValue(pair.Key, out var bakes)) continue;
                int taken = 0;
                foreach (var bake in bakes)
                {
                    if (taken >= pair.Value.count) break;
                    if (!Fits(bake, pair.Value.width, pair.Value.depth)) continue;
                    if (SpentLandmark(bake) != null) continue;
                    _ownPadComing.Add(bake);
                    ClaimLandmarks(bake);
                    taken++;
                }
            }
        }

        // What a lot with no block of its own gets: the generic terrace, wherever it
        // fits. Null = the lot keeps its floor only.
        GameObject ResidentialBakeFor(float width, float depth) =>
            Fits(_blockPrefab, width, depth) ? _blockPrefab : null;

        // Interiors are handed out largest first, and each one takes the first of
        // these that it can have:
        //
        //   1. the block composed on ITS pad - authored against this exact rectangle
        //   2. the catalog's roll for this pad size - the stock BlockLotStock writes,
        //      which is what every lot nobody composed a block for is filled with
        //   3. no stock at all: the city as it was built before there was one - the
        //      feature pool packed in rows, then the residential terrace
        //
        // A hand-made block stands on its own pad or not at all. Not every one has to
        // be in every city: a pad the spacing did not roll leaves its block out, and
        // that is right - a block dropped centred into a bigger lot is a ring of bare
        // court, which is worse than the roll the lot gets instead. Where a hand-made
        // block is wanted in a bigger lot the roller does it properly: the block is
        // stood in a corner and the rest of the pad randomised round it (a seeded
        // roll, BlockLotStock), and that roll comes here as stock like any other.
        //
        // Across all of it a PLACE stands once: the fairground, the palm tower, the
        // hotel, the station. Whichever pool a bake comes from, one carrying a place
        // already up is passed over (LandmarksOf and CarriesSpentLandmark), and the
        // console says which and why.
        //
        // A lot too small even for the terrace keeps its floor and nothing else,
        // rather than a bake spilling over the kerb.
        //
        // Nothing else goes inside a block. Interiors carry catalogue bakes and the
        // lot floor only - no scattered greenery, furniture or lawns. Street furniture
        // still belongs to the streets, which are not block interiors (DressStreets).
        void BuildBlocks()
        {
            var lots = new List<(int i, int j, float xMin, float xMax, float zMin, float zMax,
                Rect slab)>();
            for (int i = 0; i + 1 < verticalRoadX.Length; i++)
                for (int j = 0; j + 1 < horizontalRoadZ.Length; j++)
                {
                    // the river and the park are not lots: BuildSeams lays them
                    if (InSeam(i, j)) continue;
                    lots.Add((i, j,
                        verticalRoadX[i] + VHalf(i) + Sidewalk,
                        verticalRoadX[i + 1] - VHalf(i + 1) - Sidewalk,
                        horizontalRoadZ[j] + HHalf(j) + Sidewalk,
                        horizontalRoadZ[j + 1] - HHalf(j + 1) - Sidewalk,
                        // the map's slab: kerb to kerb, so the sidewalk ring reads as
                        // part of the block it belongs to rather than as road
                        Rect.MinMaxRect(
                            verticalRoadX[i] + VHalf(i),
                            horizontalRoadZ[j] + HHalf(j),
                            verticalRoadX[i + 1] - VHalf(i + 1),
                            horizontalRoadZ[j + 1] - HHalf(j + 1))));
                }

            // Downtown first, then biggest, ties broken by position so the order never
            // wobbles. The zone leads because being served first is what a lot gets out
            // of it: LotBakeFor prefers a composed block nothing has stood yet, so
            // whoever asks early carries what somebody arranged by hand and whoever
            // asks late carries the rolled stock or the generic terrace. Ordering
            // across two different sizes changes nothing either way - the bakes are
            // filed by lot code and a lot can only ever be offered its own code's pool.
            lots.Sort((a, b) =>
            {
                int zoneA = ZoneServingOrder(ZoneAt(a.i, a.j));
                int zoneB = ZoneServingOrder(ZoneAt(b.i, b.j));
                if (zoneA != zoneB) return zoneA.CompareTo(zoneB);
                float areaA = (a.xMax - a.xMin) * (a.zMax - a.zMin);
                float areaB = (b.xMax - b.xMin) * (b.zMax - b.zMin);
                if (!Mathf.Approximately(areaA, areaB)) return areaB.CompareTo(areaA);
                return a.i != b.i ? a.i.CompareTo(b.i) : a.j.CompareTo(b.j);
            });

            // The pocket parks leave the list before anything is served. A lot left as
            // grass must not reserve a hand-composed block it will never stand (
            // PlanOwnPads counts interiors by lot code and holds one bake per count),
            // and it must not draw a feature bake off the loose pool either.
            for (int k = lots.Count - 1; k >= 0; k--)
            {
                var green = lots[k];
                if (!IsPocketPark(green.i, green.j)) continue;
                string what = BuildPocketPark(green.xMin, green.xMax, green.zMin, green.zMax);
                _lotPlans.Add(new LotInfo(green.i, green.j,
                    Rect.MinMaxRect(green.xMin, green.zMin, green.xMax, green.zMax), green.slab,
                    LotCode(green.xMax - green.xMin, green.zMax - green.zMin), what, true));
                lots.RemoveAt(k);
            }

            PlanOwnPads(lots);

            int feature = 0;
            foreach (var lot in lots)
            {
                float xMin = lot.xMin, xMax = lot.xMax, zMin = lot.zMin, zMax = lot.zMax;
                int i = lot.i, j = lot.j;
                var centre = new Vector3((xMin + xMax) * 0.5f, FloorLevel() + 0.02f, (zMin + zMax) * 0.5f);

                // What is blocked off inside a block is the BUILDINGS, and it is done
                // once the interior stands (BlockLotSolids, at the end of this). The
                // whole lot used to go down as one rectangle, kerb to kerb, so a man
                // off the pavement went round the block rather than into it - which is
                // not how a crew crosses a quarter. It cuts through the yard, down the
                // gap between two buildings, over the forecourt.

                string contents;

                // A block composed for this exact lot outranks everything: it was
                // arranged against this rectangle, and no other interior is its.
                var authored = LotBakeFor(xMax - xMin, zMax - zMin);
                // then the catalog's own roll for this pad size, which is what fills
                // every interior nobody composed a block for
                if (authored == null)
                    authored = StockBakeFor(xMax - xMin, zMax - zMin);
                var bake = authored ?? (feature >= _featureBlocks.Count
                    ? ResidentialBakeFor(xMax - xMin, zMax - zMin)
                    : null);
                if (bake != null)
                {
                    contents = bake.name;

                    // The terrace's two facade rows both front the E-W streets, so a
                    // half-turn is a valid orientation - alternate it to break up the
                    // cloning. An authored block keeps the facing it was composed at:
                    // its own frontage, forecourt and parking were laid out against
                    // one particular street, and a half-turn puts them on the other.
                    float yaw = authored != null ? 0f : ((i + j) % 2 == 0 ? 0f : 180f);
                    var rot = Quaternion.Euler(0f, yaw, 0f);
                    var block = Instantiate(bake, centre - rot * BlockPivotToCentre(bake), rot, _blocks);
                    SpinFerrisWheels(block);

                    // The lot's own ground goes down AFTER the block, so what the block
                    // digs out (a skatepark bowl) is left open rather than plated over.
                    // A composed block brings its floor with it (BlockFloorFiller), so
                    // this is the court showing round its edges and through its gaps:
                    // the concrete plate carpet, the same as the sidewalk it meets, and
                    // never the black asphalt lot - that read as an unfinished block.
                    BuildBlockFloor(xMin, xMax, zMin, zMax, SunkenRects(block), _pave != null);

                    // The station is a fixture, not decoration: the patrol cars dock at
                    // its forecourt, and it reaches the city inside a composed block
                    // (c2policestation) as readily as it does packed loose. So it is
                    // looked for here too, and the block's own lot is the forecourt's.
                    if (_policeStation == null)
                    {
                        var station = StationIn(block);
                        if (station != null)
                        {
                            _policeStation = station;
                            PlanForecourt(xMin, xMax, zMin, zMax, FloorLevel());
                        }
                    }

                    // street doors for the crowd: the bake's two terrace rows
                    // front the E-W streets, so the north and south faces of
                    // its AABB are facade planes - two doorways per row keeps
                    // people coming and going along the whole frontage
                    var bb = BoundsOf(block);
                    foreach (float fx in new[] { 0.3f, 0.7f })
                    {
                        float dx = Mathf.Lerp(bb.min.x, bb.max.x, fx);
                        _pendingDoors.Add((new Vector3(dx, centre.y, bb.min.z), Vector3.back, block));
                        _pendingDoors.Add((new Vector3(dx, centre.y, bb.max.z), Vector3.forward, block));
                    }
                }
                else
                {
                    // blocks first: a bake that digs below street level (the
                    // skatepark bowl reaches -2 m) must get NO floor beneath it.
                    // Courts follow the PalmCity demo's own floor: concrete
                    // plate carpets. The worn-asphalt lot is kept only for a
                    // bake that digs below ground, since plates cannot ring a
                    // bowl - as a look it is not wanted anywhere else.
                    bool digs = feature < _featureBlocks.Count &&
                        PrefabBoundsOf(_featureBlocks[feature]).min.y < -0.2f;
                    bool paved = !digs && _pave != null;
                    float floorTop = FloorLevel();
                    bool northRow = (i + j) % 2 == 1;
                    var holes = new List<Rect>();
                    // a bake is only consumed once it stands, so what the packer moved
                    // the cursor past IS what this interior carries
                    int firstFeature = feature;
                    PackFeatureBlocks(ref feature, xMin, xMax, zMin, zMax,
                        holes, floorTop, paved, northRow);
                    contents = FeatureContents(firstFeature, feature, paved);
                    // the stall row and the driveway out to the street: geometry
                    // the patrol cars dock against, so it is planned even though
                    // nothing decorative is laid over the rest of the lot
                    if (_policeStation != null && !_forecourtPlanned)
                        PlanForecourt(xMin, xMax, zMin, zMax, floorTop);
                    BuildBlockFloor(xMin, xMax, zMin, zMax, holes, paved);
                }

                float width = xMax - xMin, depth = zMax - zMin;
                _lotPlans.Add(new LotInfo(i, j,
                    Rect.MinMaxRect(xMin, zMin, xMax, zMax), lot.slab,
                    LotCode(width, depth), contents));
            }

            BlockLotSolids();
        }

        /// <summary>What a man on foot cannot walk through, inside the blocks: every
        /// thing in an interior that STANDS UP, one footprint each.
        ///
        /// Only what stands up. An interior's floor, its plates, its painted lines and
        /// its flat dressing all have bounds too, and blocking those would wall off the
        /// very ground this is opening up - so anything under a man's knee is ground.
        ///
        /// And each footprint is clipped to its own lot. A building's box is its widest
        /// point at any height: eaves, balconies and signs hang out over the pavement,
        /// and a wall drawn round those would put the sidewalk itself out of bounds and
        /// stop the crowd dead along every frontage in the city.</summary>
        void BlockLotSolids()
        {
            if (_blocks == null) return;
            var solids = _blocks.GetComponentsInChildren<Renderer>(false);
            foreach (var r in solids)
            {
                if (r == null) continue;
                var b = r.bounds;
                if (b.size.y < 0.8f) continue;                       // flat: ground, not a wall
                if (b.size.x < 0.05f || b.size.z < 0.05f) continue;  // a plane on edge
                float x0 = b.min.x, x1 = b.max.x, z0 = b.min.z, z1 = b.max.z;
                if (!ClipToLot(ref x0, ref x1, ref z0, ref z1)) continue;
                WalkObstacles.Block(x0, x1, z0, z1);
            }
        }

        /// <summary>Cuts a footprint down to the lot its middle stands in. False when it
        /// stands in no lot at all (a seam piece, a wayside prop) - those are the street's
        /// own furniture and the pavement plans already speak for them.</summary>
        bool ClipToLot(ref float x0, ref float x1, ref float z0, ref float z1)
        {
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            for (int i = 0; i < _lotPlans.Count; i++)
            {
                var lot = _lotPlans[i].Interior;
                if (cx < lot.xMin || cx > lot.xMax || cz < lot.yMin || cz > lot.yMax) continue;
                x0 = Mathf.Max(x0, lot.xMin); x1 = Mathf.Min(x1, lot.xMax);
                z0 = Mathf.Max(z0, lot.yMin); z1 = Mathf.Min(z1, lot.yMax);
                return x1 > x0 && z1 > z0;
            }
            return false;
        }

        // What an interior ended up carrying, worded for the O overlay: the bakes the
        // packer laid in this one, or the empty court's own floor when it took none.
        string FeatureContents(int first, int last, bool paved)
        {
            // the packer moves past a bake whose place already stands elsewhere
            // without laying it, so those are not this interior's contents
            var names = new List<string>();
            for (int k = first; k < last; k++)
                if (OwnsItsPlaces(_featureBlocks[k]))
                    names.Add(_featureBlocks[k].name);
            if (names.Count == 0)
                return paved ? "empty - concrete court" : "empty - asphalt court";
            return string.Join(", ", names);
        }

        // Frontage dictation: extra yaw that turns a feature bake's entrance row
        // to face world -Z. The bakes keep their PalmCity-demo orientation baked
        // into the mesh, so which way the doors point cannot be computed â€” it is
        // read off the scene per block, and a correction lands here by name.
        static readonly Dictionary<string, float> FrontageYaw = new Dictionary<string, float>();

        static float FrontageOf(string name)
            => FrontageYaw.TryGetValue(name, out var y) ? y : 0f;

        // Feature bakes carry varying pivots and footprints, so one bake rarely
        // fills the 70x50 interior on its own. Pack a row instead: measure each
        // candidate's renderer AABB and lay blocks west to east along the street
        // frontage with a service alley between them until the row is full.
        // Every interior edge borders a street, and rows alternate between the
        // south and north frontage per interior; each bake is turned so its
        // entrance row (FrontageYaw) faces the street it is packed against.
        // A quarter turn is used only when it saves the fit AND the doors still
        // land on a kerb â€” flush against the west side street at the row start,
        // or the east one as the row's closer. A block that no longer fits the
        // remaining width stays in the pool for the next feature interior.
        // Footprints of blocks whose geometry dips below street level
        // (the skatepark bowl) are reported via holes so the floor pass leaves
        // the ground under them open.
        List<Rect> PackFeatureBlocks(ref int feature, float xMin, float xMax, float zMin, float zMax,
            List<Rect> holes, float floorTop, bool paved, bool northRow)
        {
            float depth = zMax - zMin;
            var rects = new List<Rect>();
            var placed = new List<GameObject>();
            var sunken = new List<int>();
            float cursor = xMin;

            while (feature < _featureBlocks.Count)
            {
                float remaining = xMax - cursor;
                if (remaining < 15f) break;

                var prefab = _featureBlocks[feature];
                // a place already standing inside some composed block is not laid
                // loose as well: the pool moves on past it
                if (CarriesSpentLandmark(prefab, "the feature pool")) { feature++; continue; }
                float baseYaw = FrontageOf(prefab.name);
                float yaw = baseYaw + (northRow ? 180f : 0f);
                var go = Instantiate(prefab, Vector3.zero, Quaternion.Euler(0f, yaw, 0f), _blocks);
                var b = BoundsOf(go);
                bool eastEnd = false;
                Vector3 doorOut = northRow ? Vector3.forward : Vector3.back;
                if (b.size.x > remaining || b.size.z > depth)
                {
                    // a quarter turn saves the fit WITHOUT taking the doors off a
                    // kerb only because the side edges are streets too: the turned
                    // block stands flush against the west side street at the row
                    // start (doors west), or closes the row against the east one
                    // (doors east)
                    bool startOfRow = cursor <= xMin + 0.01f;
                    if (b.size.z <= remaining && b.size.x <= depth)
                    {
                        eastEnd = !startOfRow;
                        yaw = baseYaw + (eastEnd ? 270f : 90f);
                        doorOut = eastEnd ? Vector3.right : Vector3.left;
                        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                        b = BoundsOf(go);
                    }
                    else if (placed.Count > 0) { Destroy(go); break; }
                    else Debug.LogWarning("[RoadDemo] " + prefab.name + " overflows its interior ("
                        + b.size.x.ToString("F0") + "x" + b.size.z.ToString("F0")
                        + " m into " + (xMax - xMin) + "x" + depth + " m)");
                }

                // flush against the street frontage: footprint edge on the interior
                // boundary so the bake meets the kerb instead of floating mid-lot
                float xStart = eastEnd ? xMax - b.size.x : cursor;
                float zStart = northRow ? zMax - b.size.z : zMin;
                var target = new Vector3(xStart + b.size.x * 0.5f, 0f, zStart + b.size.z * 0.5f);
                var shift = target - b.center;
                // a bake that digs below ground (the skatepark bowl) can never
                // stand on a plate court â€” the carpet cannot ring a bowl â€” so on
                // a paved court it goes back in the pool for the next, asphalt
                // interior; there its floor hole keeps the ground open below.
                bool digs = b.min.y < -0.2f;
                if (digs && paved) { Destroy(go); break; }
                go.transform.position += new Vector3(shift.x, floorTop + 0.02f, shift.z);
                if (digs) sunken.Add(rects.Count);
                rects.Add(new Rect(xStart, zStart, b.size.x, b.size.z));
                placed.Add(go);
                ClaimLandmarks(prefab);
                SpinFerrisWheels(go);
                if (prefab.name.StartsWith(StationName)) _policeStation = go;
                // a civilian door on whichever face fronts this bake's street.
                // The station keeps its own door (the beat officers'), and a
                // sunken bake (skatepark) has no facade on the frontage line.
                else if (!digs)
                {
                    float doorY = floorTop + 0.02f;
                    Vector3 doorPos;
                    if (doorOut == Vector3.back)
                        doorPos = new Vector3(xStart + b.size.x * 0.5f, doorY, zStart);
                    else if (doorOut == Vector3.forward)
                        doorPos = new Vector3(xStart + b.size.x * 0.5f, doorY, zStart + b.size.z);
                    else if (doorOut == Vector3.left)
                        doorPos = new Vector3(xStart, doorY, zStart + b.size.z * 0.5f);
                    else
                        doorPos = new Vector3(xStart + b.size.x, doorY, zStart + b.size.z * 0.5f);
                    _pendingDoors.Add((doorPos, doorOut, go));
                }
                cursor = eastEnd ? xMax : cursor + b.size.x + BlockAlley;
                feature++;
            }

            foreach (int k in sunken) holes.Add(rects[k]);
            return rects;
        }

        // Renderer AABB of a prefab measured at the origin (so min.y is relative
        // to the pivot); instantiated once, cached, destroyed before render.
        Bounds PrefabBoundsOf(GameObject prefab)
        {
            if (!_prefabBoundsCache.TryGetValue(prefab, out var b))
            {
                var tmp = Instantiate(prefab, Vector3.zero, Quaternion.identity);
                b = BoundsOf(tmp);
                _prefabBoundsCache[prefab] = b;
                Destroy(tmp);
            }
            return b;
        }

        void EnsurePaveMeasured()
        {
            if (_paveMeasured || _pave == null) return;
            var b = PrefabBoundsOf(_pave);
            _paveSize = b.size;
            _paveOffset = b.center;
            _paveTop = b.max.y;
            _paveMeasured = true;
        }

        static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        // Where a standing block reaches below street level - the skatepark bowl,
        // a sunken garage - as XZ rectangles the lot floor must leave open. Read
        // off the instance's renderers, half a metre down or more so foundation
        // skirts and floor tiles do not count; a plate under a piece that merely
        // sits low is hidden by it, a plate over a bowl fills the bowl in.
        List<Rect> SunkenRects(GameObject block)
        {
            List<Rect> holes = null;
            float ground = FloorLevel() - 0.5f;
            foreach (var r in block.GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                if (b.min.y >= ground) continue;
                holes ??= new List<Rect>();
                holes.Add(Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z));
            }
            return holes;
        }

        // The lot's ground. Paved interiors follow the PalmCity demo's court floor:
        // a carpet of 2.5 m SM_Env_Sidewalk_01 concrete plates at random quarter-
        // turns, flush with the sidewalk - what every block interior gets now.
        // The asphalt lot is kept only for a court holding a bake that digs below
        // ground, which plates cannot ring: Road_Bare_01 at random yaws with the
        // cracked Road_03 mixed in, tar patches sunk so only the raised blob
        // shows, plus a couple of manholes, at a wear level rolled per interior.
        // A plate that would lap over a sunken bake (the skatepark bowl, and the
        // City_04/City_07 brownstones whose lowest storey and area door sit 1.5 m
        // under the pavement) is not laid at all - see InHole.
        void BuildBlockFloor(float xMin, float xMax, float zMin, float zMax, List<Rect> holes,
            bool paved)
        {
            // Not "fully inside the hole" but "touches it at all": a plate lapping half
            // over a brownstone's wall line runs straight across its area door, the thing
            // the block's own floor is careful about too (BlockFloorFiller). The rect is
            // pulled in by the width of the wall standing over it, so a plate may still
            // reach as far under a wall as the wall hides - and no further.
            const float wallHide = 0.3f;
            bool InHole(float x, float z, float w, float d)
            {
                if (holes == null) return false;
                foreach (var h in holes)
                    if (x + w > h.xMin + wallHide && x < h.xMax - wallHide &&
                        z + d > h.yMin + wallHide && z < h.yMax - wallHide) return true;
                return false;
            }

            if (paved && _pave != null)
            {
                EnsurePaveMeasured();
                float baseY = FloorLevel() - _paveTop; // plate tops flush with the kerb
                float sx = Mathf.Max(_paveSize.x, 0.5f), sz = Mathf.Max(_paveSize.z, 0.5f);
                bool square = Mathf.Abs(sx - sz) < 0.01f;
                for (float mx = xMin; mx < xMax - 0.1f; mx += sx)
                    for (float mz = zMin; mz < zMax - 0.1f; mz += sz)
                    {
                        if (InHole(mx, mz, sx, sz)) continue;
                        var cover = new Vector3(mx + sx * 0.5f, baseY, mz + sz * 0.5f);
                        var tile = Instantiate(_pave,
                            cover - new Vector3(_paveOffset.x, 0f, _paveOffset.z),
                            Quaternion.identity, _geometry);
                        if (square)
                            tile.transform.RotateAround(cover, Vector3.up, 90f * Random.Range(0, 4));
                    }
                return;
            }

            float wear = Random.Range(0.04f, 0.4f);
            float lotY = FloorLevel() - PrefabBoundsOf(_bare).max.y; // lot flush with the kerb too
            for (float mx = xMin; mx < xMax - 0.1f; mx += Cell)
                for (float mz = zMin; mz < zMax - 0.1f; mz += Cell)
                {
                    if (InHole(mx, mz, Cell, Cell)) continue;
                    var tile = _bareCracked != null && Random.value < wear ? _bareCracked : _bare;
                    PlaceCell(tile, mx, mz, 90 * Random.Range(0, 4), lotY);
                }

            if (_roadPatch != null)
            {
                int patches = Random.Range(4, 4 + (int)(wear * 50f));
                for (int p = 0; p < patches; p++)
                {
                    var pos = new Vector3(Random.Range(xMin + 1.5f, xMax - 1.5f),
                        FloorLevel() + Random.Range(-0.05f, -0.02f),
                        Random.Range(zMin + 1.5f, zMax - 1.5f));
                    if (InHole(pos.x, pos.z, 0f, 0f)) continue;
                    Instantiate(_roadPatch, pos,
                        Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), _geometry);
                }
            }

            for (int p = 0; p < 3; p++)
            {
                var pos = new Vector3(Random.Range(xMin + 2f, xMax - 2f), FloorLevel() + 0.02f,
                    Random.Range(zMin + 2f, zMax - 2f));
                if (InHole(pos.x, pos.z, 0f, 0f)) continue;
                Prop(_manhole, pos, Random.value * 360f, _geometry);
            }
        }

        // --------------------------------------------------------- street dressing
        // Densities and the prop mix follow the PalmCity demo scene: palm-in-grate
        // kerb planting, benches with bins, trash-bag clusters, planters, power
        // boxes, bollarded corners and powerline poles carrying scaled wire spans.

        static float YawOf(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;

        static T Pick<T>(List<T> l) => l[Random.Range(0, l.Count)];

        // Every prop laid claims the ground it measures out, so the dressing that
        // comes after it and the walkers that come after that both know it is there.
        GameObject Prop(GameObject prefab, Vector3 pos, float yaw, Transform parent)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent);
            if (SidewalkPlan.Footprint(prefab, pos, yaw, out var box)) _plan.Take(box);
            return go;
        }

        // A bench that people can actually use: placed like any prop, and noted
        // for the street-life wiring. Courtyard benches register too and drop
        // out later - only spots near a sidewalk link get sitters.
        void PlaceBench(Vector3 pos, float yaw)
        {
            Prop(Pick(_benches), pos, yaw, _geometry);
            _pendingBenches.Add((pos, yaw));
        }

        void DressStreets()
        {
            PrepareDressing();
            // the poles go in first: they stand on the frontage strip, and the
            // dressing that follows has to work round them, not through them
            PowerlinePass();

            // a closed segment (a street ending on the river or at the park) has no
            // sides to dress; a bridge dresses itself (DressBridge) - no palm grates
            // out over the water
            for (int i = 0; i < verticalRoadX.Length; i++)
                for (int j = 0; j + 1 < horizontalRoadZ.Length; j++)
                {
                    if (!SegmentOpen(true, i, j) || IsBridge(true, i, j)) continue;
                    var a = _nodes[i, j];
                    var b = _nodes[i, j + 1];
                    var start = new Vector3(verticalRoadX[i], 0f, a.ZMax);
                    float len = b.ZMin - a.ZMax;
                    // an embankment road's water side is the promenade, dressed by the
                    // river itself (BuildRiver): lamps at the coping, benches to the water
                    if (!RiverBeside(true, i, +1)) DressSide(start, Vector3.forward, len, Vector3.right, verticalIsBoulevard[i]);
                    if (!RiverBeside(true, i, -1)) DressSide(start, Vector3.forward, len, Vector3.left, verticalIsBoulevard[i]);
                }
            for (int j = 0; j < horizontalRoadZ.Length; j++)
                for (int i = 0; i + 1 < verticalRoadX.Length; i++)
                {
                    if (!SegmentOpen(false, j, i) || IsBridge(false, j, i)) continue;
                    var a = _nodes[i, j];
                    var b = _nodes[i + 1, j];
                    var start = new Vector3(a.XMax, 0f, horizontalRoadZ[j]);
                    float len = b.XMin - a.XMax;
                    if (!RiverBeside(false, j, +1)) DressSide(start, Vector3.right, len, Vector3.forward, horizontalIsBoulevard[j]);
                    if (!RiverBeside(false, j, -1)) DressSide(start, Vector3.right, len, Vector3.back, horizontalIsBoulevard[j]);
                }

            CornerProps();
            ManholePass();
        }

        void DressSide(Vector3 start, Vector3 dir, float len, Vector3 outward, bool boulevard)
        {
            if (!boulevard)
            {
                // an ordinary street: kerb strip, a clear walk, the frontage - and a
                // terrace in front of whatever cafe or diner fronts this stretch
                _dressing.Dress(Vocabulary(), start, dir, outward, len, StreetHalf,
                    TerracesAlong(start, dir, outward, len, StreetHalf));
                return;
            }

            // boulevard: lamps on the kerb strip, benches set back near the palm
            // row by the wall so they do not crowd the kerb; the palms out there
            // repeat every 20 m starting 10 m in, so benches dodge those spots
            float half = BoulevardHalf;
            float faceRoad = YawOf(-outward);
            Vector3 At(float t, float lat) => start + dir * t + outward * lat + Vector3.up * 0.1f;

            int slot = 0;
            for (float t = 6f; t < len - 6f; t += 12.5f, slot++)
            {
                if (slot % 2 == 0 && _lamps.Count > 0)
                {
                    Prop(Pick(_lamps), At(t, half + 1.2f), faceRoad, _geometry);
                }
                else if (_benches.Count > 0)
                {
                    float bt = t;
                    float nearestPalm = Mathf.Round((bt - 10f) / 20f) * 20f + 10f;
                    if (Mathf.Abs(bt - nearestPalm) < 2.5f)
                        bt = nearestPalm + (bt >= nearestPalm ? 3f : -3f);
                    PlaceBench(At(bt, half + Sidewalk - 1.6f), faceRoad);
                    // a bin at the far end of the bench, not at the sitter's elbow
                    if (Random.value < 0.5f && _bins.Count > 0)
                        Prop(Pick(_bins), At(bt + 3.4f, half + Sidewalk - 1.6f), faceRoad, _geometry);
                }
            }
        }

        // manhole covers scattered over the carriageways
        void ManholePass()
        {
            if (_manhole == null) return;
            for (int i = 0; i < verticalRoadX.Length; i++)
                for (int j = 0; j + 1 < horizontalRoadZ.Length; j++)
                {
                    if (!SegmentOpen(true, i, j) || IsBridge(true, i, j)) continue;
                    var a = _nodes[i, j];
                    var b = _nodes[i, j + 1];
                    int count = verticalIsBoulevard[i] ? 3 : Random.Range(1, 3);
                    for (int k = 0; k < count; k++)
                    {
                        float lat = verticalIsBoulevard[i]
                            ? (Random.value < 0.5f ? -1f : 1f) * Random.Range(6f, 13f)
                            : Random.Range(-3f, 3f);
                        Prop(_manhole, new Vector3(verticalRoadX[i] + lat, 0.02f,
                            Random.Range(a.ZMax + 4f, b.ZMin - 4f)), Random.value * 360f, _geometry);
                    }
                }
            for (int j = 0; j < horizontalRoadZ.Length; j++)
                for (int i = 0; i + 1 < verticalRoadX.Length; i++)
                {
                    if (!SegmentOpen(false, j, i) || IsBridge(false, j, i)) continue;
                    var a = _nodes[i, j];
                    var b = _nodes[i + 1, j];
                    int count = horizontalIsBoulevard[j] ? 3 : Random.Range(1, 3);
                    for (int k = 0; k < count; k++)
                    {
                        float lat = horizontalIsBoulevard[j]
                            ? (Random.value < 0.5f ? -1f : 1f) * Random.Range(6f, 13f)
                            : Random.Range(-3f, 3f);
                        Prop(_manhole, new Vector3(Random.Range(a.XMax + 4f, b.XMin - 4f), 0.02f,
                            horizontalRoadZ[j] + lat), Random.value * 360f, _geometry);
                    }
                }
        }


        // One line of poles along each ordinary street (east / south side), wires
        // scaled to whatever span the pole spacing produces. Pole positions inside
        // intersection or zebra zones are skipped, so spans stretch across them.
        void PowerlinePass()
        {
            if (_powerpole == null || _wires.Count == 0) return;

            int nv = verticalRoadX.Length, nh = horizontalRoadZ.Length;

            // one line per unbroken stretch of street: a street that ends on the
            // river starts its poles again on the far bank, and no wire spans the water
            for (int i = 0; i < nv; i++)
            {
                if (verticalIsBoulevard[i]) continue;
                int j = 0;
                while (j + 1 < nh)
                {
                    if (!SegmentOpen(true, i, j)) { j++; continue; }
                    int first = j;
                    while (j + 1 < nh && SegmentOpen(true, i, j)) j++;
                    PoleRun(PoleSpots(_nodes[i, first].ZMax + 2f, _nodes[i, j].ZMin - 2f,
                                      z => InsideNodeZoneZ(i, z)),
                            verticalRoadX[i] + StreetHalf + Sidewalk - 0.7f, true);
                }
            }
            for (int j = 0; j < nh; j++)
            {
                if (horizontalIsBoulevard[j]) continue;
                int i = 0;
                while (i + 1 < nv)
                {
                    if (!SegmentOpen(false, j, i)) { i++; continue; }
                    int first = i;
                    while (i + 1 < nv && SegmentOpen(false, j, i)) i++;
                    PoleRun(PoleSpots(_nodes[first, j].XMax + 2f, _nodes[i, j].XMin - 2f,
                                      x => InsideNodeZoneX(j, x)),
                            horizontalRoadZ[j] - StreetHalf - Sidewalk + 0.7f, false);
                }
            }
        }

        /// One unbroken line of poles: spots are positions ALONG the run, lateral
        /// is the fixed road-side offset on the other axis.
        void PoleRun(List<float> spots, float lateral, bool vertical)
        {
            const float WireLen = 7.696f;
            const float WireY = 8.33f;
            float[] strand = { -0.85f, 0f, 0.85f };
            float yaw = vertical ? 0f : 90f;

            Vector3 At(float along, float side) => vertical
                ? new Vector3(lateral + side, 0.1f, along)
                : new Vector3(along, 0.1f, lateral + side);

            foreach (float along in spots)
                Prop(_powerpole, At(along, 0f), yaw, _geometry);

            for (int k = 0; k + 1 < spots.Count; k++)
                foreach (float off in strand)
                {
                    var seat = At(spots[k], off);
                    var wire = Instantiate(Pick(_wires),
                        new Vector3(seat.x, WireY, seat.z),
                        Quaternion.Euler(0f, yaw, 0f), _geometry);
                    wire.transform.localScale =
                        new Vector3(1f, 1f, (spots[k + 1] - spots[k]) / WireLen);
                }
        }

        List<float> PoleSpots(float from, float to, System.Func<float, bool> blocked)
        {
            var spots = new List<float>();
            float p = from;
            while (p < to)
            {
                if (blocked(p)) { p += 3f; continue; }
                spots.Add(p);
                p += 21f;
            }
            return spots;
        }

        bool InsideNodeZoneZ(int i, float z)
        {
            for (int j = 0; j < horizontalRoadZ.Length; j++)
                if (z > _nodes[i, j].ZMin - 7f && z < _nodes[i, j].ZMax + 7f) return true;
            return false;
        }

        bool InsideNodeZoneX(int j, float x)
        {
            for (int i = 0; i < verticalRoadX.Length; i++)
                if (x > _nodes[i, j].XMin - 7f && x < _nodes[i, j].XMax + 7f) return true;
            return false;
        }

        // A corner is where two crossings meet: the L of pavement between the
        // kerb and the corner node is walking room, reserved in PrepareDressing
        // and left bare. What a corner carries stands OFF that L - bollards and a
        // hydrant down at the kerb corner, the box and the stand back against the
        // buildings - and each of them still asks the plan before it stands.
        void CornerProps()
        {
            foreach (var n in _nodes)
                foreach (var (sx, sz) in new[] { (1f, 1f), (-1f, 1f), (-1f, -1f), (1f, -1f) })
                {
                    float bx = sx > 0f ? n.XMax : n.XMin;
                    float bz = sz > 0f ? n.ZMax : n.ZMin;
                    Vector3 C(float ox, float oz) => new Vector3(bx + sx * ox, 0.1f, bz + sz * oz);
                    float faceIn = YawOf(new Vector3(-sx, 0f, -sz));
                    // the slab is as deep as the pavement: the kerb-side seat is just
                    // past the kerb stone, the rest scales with the slab
                    float kerb = SidewalkDressing.KerbSeat + 0.15f;
                    float deep = Sidewalk - 0.9f;    // the building corner's side

                    // the City demo's corners: a pair of bollards on a third of them,
                    // a sign on a quarter, and one piece of kerb furniture or none
                    if (Random.value < 0.35f && _bollard != null)
                    {
                        Stand(_bollard, C(kerb, deep), 0f, _geometry);
                        Stand(_bollard, C(deep, kerb), 0f, _geometry);
                    }
                    if (Random.value < 0.25f && _signPole != null)
                        Stand(_signPole, C(kerb, deep + 0.2f), faceIn + Random.Range(-10f, 10f), _geometry);

                    float r = Random.value;
                    if (r < 0.18f)
                        Stand(_hydrant, C(kerb, kerb), Random.value * 360f, _geometry);
                    else if (r < 0.42f && _bins.Count > 0)
                        Stand(Pick(_bins), C(deep, deep), faceIn, _geometry);
                    else if (r < 0.54f)
                        Stand(_newsstand, C(deep, kerb), faceIn, _geometry);
                    else if (r < 0.64f)
                        Stand(_payPhone, C(kerb, deep), faceIn, _geometry);
                    else if (r < 0.72f && _powerboxes.Count > 0)
                        Stand(Pick(_powerboxes), C(kerb, deep), faceIn, _geometry);
                }
        }

        /// <summary>Lay a prop only where the ground is free - no walking room to
        /// keep here, just no standing one prop inside another.</summary>
        bool Stand(GameObject prefab, Vector3 pos, float yaw, Transform parent)
        {
            if (prefab == null) return false;
            if (!SidewalkPlan.Footprint(prefab, pos, yaw, out var box))
            {
                Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent);
                return true;
            }
            if (!_plan.Free(box, 0.12f)) return false;
            _plan.Take(box);
            Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent);
            return true;
        }

        // ------------------------------------------------------------------ graph

        static float[] LaneOffsets(bool boulevard)
            => boulevard ? new[] { 7.5f, 12.5f } : new[] { 2.5f };

        // The lane network: one carriageway per street segment between two
        // junctions, its lanes at the offsets either side of the crown (one each way on
        // a street, two each way on a boulevard with the median between), the kerbs
        // beyond the outer lanes where cars park; the junctions' connectors and their
        // conflict tables laid last (LaneNet.Finish). The edges list the rest of the
        // builder reads (the spawns, the patrol routes) is the network's own.
        void BuildGraph()
        {
            var net = new LaneNet();
            foreach (var n in _nodes) if (n != null) net.Nodes.Add(n);
            for (int i = 0; i < verticalRoadX.Length; i++)
            {
                float cx = verticalRoadX[i];
                bool blvd = verticalIsBoulevard[i];
                float limit = blvd ? boulevardSpeed : streetSpeed;
                for (int j = 0; j + 1 < horizontalRoadZ.Length; j++)
                {
                    if (!SegmentOpen(true, i, j)) continue; // ends on the quay: no lane
                    // one carriageway, or a chain of them through the crossroads a
                    // freeway corridor stands in the middle of this segment
                    LaneSegment(net, true, i, j, _nodes[i, j], _nodes[i, j + 1], cx,
                        blvd ? BoulevardHalf : StreetHalf, LaneOffsets(blvd), limit, blvd ? 5f : 0f);
                }
            }
            for (int j = 0; j < horizontalRoadZ.Length; j++)
            {
                float cz = horizontalRoadZ[j];
                bool blvd = horizontalIsBoulevard[j];
                float limit = blvd ? boulevardSpeed : streetSpeed;
                for (int i = 0; i + 1 < verticalRoadX.Length; i++)
                {
                    if (!SegmentOpen(false, j, i)) continue;
                    LaneSegment(net, false, j, i, _nodes[i, j], _nodes[i + 1, j], cz,
                        blvd ? BoulevardHalf : StreetHalf, LaneOffsets(blvd), limit, blvd ? 5f : 0f);
                }
            }
            // the freeway's own: its frontage roads, its decks as one-way carriageways
            // that climb their profile, and the slip roads between them
            BuildFreewayLanes(net);
            // and the elevated freeway between two quarters, whose decks, ramps and link
            // roads are the same graph as the streets they come down to
            WireFreeway(net);
            // and the expressway, off the same nodes its geometry was laid on
            WireExpressway(net);
            net.Finish();
            _edges.Clear();
            _edges.AddRange(net.Edges);
            Net = net;
            LaneNet.Active = net;
        }

        // ---------------------------------------------------------------- signals

        void BuildSignals()
        {
            _signalMats = new SignalMaterials();
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Material Mk(float r, float g, float b)
                => new Material(unlit) { color = new Color(r, g, b) };
            _signalMats.RedOn = Mk(1f, 0.1f, 0.08f);
            _signalMats.RedOff = Mk(0.2f, 0.04f, 0.04f);
            _signalMats.YellowOn = Mk(1f, 0.8f, 0.12f);
            _signalMats.YellowOff = Mk(0.22f, 0.17f, 0.04f);
            _signalMats.GreenOn = Mk(0.2f, 1f, 0.3f);
            _signalMats.GreenOff = Mk(0.04f, 0.2f, 0.07f);

            // Left as a plain dark box on purpose: the kit's materials are atlases,
            // and a primitive cube's 0-1 UVs would sample the whole sheet.
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var housing = new Material(lit) { color = new Color(0.16f, 0.16f, 0.17f) };
            housing.SetFloat("_Smoothness", 0.35f);

            // Every junction is signalled, the Ts and the dead ends included - and the
            // red phase there is NOT wasted, whatever it looks like from a car. At a
            // junction only one axis reaches, the crossings over that axis are the ones
            // the walk graph gates (BuildPedGraph): the phase that stops the cars is the
            // phase the crowd crosses in. Take the signal off a T and the pavement on
            // the far side becomes unreachable.
            foreach (var n in _nodes)
            {
                var sig = new TrafficSignal(((n.I * 31 + n.J * 17) % 13) / 13f * TrafficSignal.Cycle);
                n.Signal = sig;
                _signals.Add(sig);

                var dirs = new HashSet<Vector3>();
                foreach (var e in n.Incoming) dirs.Add(e.Dir);
                foreach (var d in dirs) BuildApproachPole(n, d, sig, housing);
            }
        }

        void BuildApproachPole(RoadNode n, Vector3 d, TrafficSignal sig, Material housing)
        {
            float vh = VHalf(n.I), hh = HHalf(n.J);
            Vector3 pos;
            int yaw;
            if (d.z > 0.5f) { pos = new Vector3(n.X + vh + 1.6f, 0.1f, n.ZMin - 1.6f); yaw = 270; }
            else if (d.z < -0.5f) { pos = new Vector3(n.X - vh - 1.6f, 0.1f, n.ZMax + 1.6f); yaw = 90; }
            else if (d.x > 0.5f) { pos = new Vector3(n.XMin - 1.6f, 0.1f, n.Z - hh - 1.6f); yaw = 0; }
            else { pos = new Vector3(n.XMax + 1.6f, 0.1f, n.Z + hh + 1.6f); yaw = 180; }

            var rot = Quaternion.Euler(0f, yaw, 0f);
            var pole = new GameObject("Signal " + d).transform;
            pole.SetParent(_traffic, false);
            pole.SetPositionAndRotation(pos, rot);
            Instantiate(_poleBase, pole);
            Instantiate(_poleArm, pole);
            Instantiate(_poleLights, pole);

            // bulb head hanging from the arm, facing the approaching cars
            Vector3 armDir = rot * Vector3.forward;
            Vector3 face = -d;
            Vector3 head = pos + armDir * 4.6f + Vector3.up * 4.05f;

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Head";
            box.transform.SetParent(pole, false);
            box.transform.SetPositionAndRotation(head, Quaternion.LookRotation(face));
            box.transform.localScale = new Vector3(0.55f, 1.55f, 0.22f);
            Destroy(box.GetComponent<Collider>());
            box.GetComponent<MeshRenderer>().sharedMaterial = housing;

            var set = new TrafficSignal.BulbSet { NorthSouth = Mathf.Abs(d.z) > 0.5f };
            for (int k = 0; k < 3; k++)
            {
                var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bulb.name = k == 0 ? "Red" : k == 1 ? "Yellow" : "Green";
                bulb.transform.SetParent(pole, false);
                bulb.transform.position = head + Vector3.up * (0.45f - 0.45f * k) + face * 0.16f;
                bulb.transform.localScale = Vector3.one * 0.34f;
                Destroy(bulb.GetComponent<Collider>());
                var r = bulb.GetComponent<MeshRenderer>();
                if (k == 0) set.R = r; else if (k == 1) set.Y = r; else set.G = r;
            }
            sig.AddBulbs(set);
        }

        // ------------------------------------------------------------- pedestrians

        PedNode[,,] _corners; // per intersection: NE, NW, SW, SE

        void AddPedLink(PedNode a, PedNode b, bool gated, bool blocksNS, TrafficSignal sig)
        {
            float len = (b.Pos - a.Pos).magnitude;
            var ab = new PedLink { From = a, To = b, Length = len, Gated = gated, BlocksNorthSouth = blocksNS, Signal = sig };
            var ba = new PedLink { From = b, To = a, Length = len, Gated = gated, BlocksNorthSouth = blocksNS, Signal = sig };
            a.Links.Add(ab);
            b.Links.Add(ba);
            _pedLinks.Add(ab);
            _pedLinks.Add(ba);
        }

        // A zebra between two corners; boulevards get a median refuge halfway so
        // each half is short enough to fit inside one red window. Where no road
        // continues past this side, the same span is just sidewalk (the cap band).
        void AddCrossing(PedNode a, PedNode b, bool roadExists, bool boulevard,
            bool blocksNS, Vector3 refugePos, TrafficSignal sig)
        {
            if (!roadExists)
            {
                AddPedLink(a, b, false, false, null);
            }
            else if (boulevard)
            {
                var refuge = new PedNode { Pos = refugePos };
                AddPedLink(a, refuge, true, blocksNS, sig);
                AddPedLink(refuge, b, true, blocksNS, sig);
            }
            else
            {
                AddPedLink(a, b, true, blocksNS, sig);
            }
        }

        void BuildPedGraph()
        {
            const int NE = 0, NW = 1, SW = 2, SE = 3;
            float Off = Sidewalk * 0.5f;   // middle of the corner slab - and of the zebra, which is 5 m deep
            const float WalkY = 0.1f; // sidewalk surface

            int nv = verticalRoadX.Length, nh = horizontalRoadZ.Length;
            _corners = new PedNode[nv, nh, 4];
            for (int i = 0; i < nv; i++)
                for (int j = 0; j < nh; j++)
                {
                    var n = _nodes[i, j];
                    _corners[i, j, NE] = new PedNode { Pos = new Vector3(n.XMax + Off, WalkY, n.ZMax + Off) };
                    _corners[i, j, NW] = new PedNode { Pos = new Vector3(n.XMin - Off, WalkY, n.ZMax + Off) };
                    _corners[i, j, SW] = new PedNode { Pos = new Vector3(n.XMin - Off, WalkY, n.ZMin - Off) };
                    _corners[i, j, SE] = new PedNode { Pos = new Vector3(n.XMax + Off, WalkY, n.ZMin - Off) };
                }

            for (int i = 0; i < nv; i++)
                for (int j = 0; j < nh; j++)
                {
                    var n = _nodes[i, j];
                    bool vBlvd = verticalIsBoulevard[i], hBlvd = horizontalIsBoulevard[j];
                    AddCrossing(_corners[i, j, NW], _corners[i, j, NE], NorthOpen(i, j), vBlvd, true,
                        new Vector3(n.X, 0.02f, n.ZMax + Off), n.Signal);
                    AddCrossing(_corners[i, j, SW], _corners[i, j, SE], SouthOpen(i, j), vBlvd, true,
                        new Vector3(n.X, 0.02f, n.ZMin - Off), n.Signal);
                    AddCrossing(_corners[i, j, NE], _corners[i, j, SE], EastOpen(i, j), hBlvd, false,
                        new Vector3(n.XMax + Off, 0.02f, n.Z), n.Signal);
                    AddCrossing(_corners[i, j, NW], _corners[i, j, SW], WestOpen(i, j), hBlvd, false,
                        new Vector3(n.XMin - Off, 0.02f, n.Z), n.Signal);
                }

            // the pavement down both sides of every segment, linking one
            // intersection's corners to the next - a segment closed by a SEAM has no
            // pavement (its junctions were capped and the water or the lawn is beyond
            // them), a bridge's walkways are pavement like any. A CLOSE keeps both its
            // pavements: the cars are stopped, the crowd is not, and the walk down it
            // is the whole point of grassing the carriageway over (BuildCloses).
            for (int i = 0; i < nv; i++)
                for (int j = 0; j + 1 < nh; j++)
                {
                    if (!WalkThrough(true, i, j)) continue;
                    AddPedLink(_corners[i, j, NE], _corners[i, j + 1, SE], false, false, null);
                    AddPedLink(_corners[i, j, NW], _corners[i, j + 1, SW], false, false, null);
                }
            for (int j = 0; j < nh; j++)
                for (int i = 0; i + 1 < nv; i++)
                {
                    if (!WalkThrough(false, j, i)) continue;
                    AddPedLink(_corners[i, j, NE], _corners[i + 1, j, NW], false, false, null);
                    AddPedLink(_corners[i, j, SE], _corners[i + 1, j, SW], false, false, null);
                }

            // the quays and the park paths: pavement of the seams' own
            BuildSeamPaths();
            // and the same for the squares inside the blocks, so the crowd walks
            // through them instead of round them
            BuildPocketParkPaths();
        }

        // ------------------------------------------------------------- city life

        // Bench seat geometry from the city's measured table (InteractionMarkers):
        // Seat_01/02 - the two placed here - share a 0.50 seat top, sitters at
        // x +-0.55 along the slats, and the root 0.258 in front of the bench
        // origin (SitPelvisBack 0.438 less the backed slab's -0.18 pelvis Z).
        static readonly Vector3[] BenchSeatOffsets =
        {
            new Vector3(-0.55f, 0.50f, 0.258f),
            new Vector3(0.55f, 0.50f, 0.258f),
        };

        // Wire every noted door and bench to the sidewalk graph: nearest
        // non-gated link, joined mid-stretch - the same join the beat officers
        // use for the station forecourt. Whatever lands too far from a sidewalk
        // (courtyard benches deep in an interior) simply stays decorative.
        /// <summary>The one city life, made before anyone needs it: the districts wire
        /// their doors into the same one the grid's blocks use, so a walker out of a
        /// suburb house is the same crowd as the one on the boulevard.</summary>
        void EnsureLife()
        {
            if (_life != null) return;
            _life = new CityLife
            {
                SitChance = sitChance,
                EnterChance = enterChance,
                InsideSeconds = insideSeconds,
                SitSeconds = sitSeconds,
                CanSit = _sitDownClip != null && _sitLoopClip != null && _standUpClip != null,
                CanChat = _talkClip != null,
            };
        }

        void BuildCityLife()
        {
            EnsureLife();

            bool NearestLink(Vector3 p, float maxDist, out PedLink fwd, out float t)
            {
                fwd = null;
                t = 0f;
                float bestD = maxDist * maxDist;
                foreach (var l in _pedLinks)
                {
                    if (l.Gated || l.Length < 6f) continue;
                    var dir = (l.To.Pos - l.From.Pos) / l.Length;
                    float s = Mathf.Clamp(Vector3.Dot(p - l.From.Pos, dir), 2f, l.Length - 2f);
                    var q = l.From.Pos + dir * s;
                    float dx = q.x - p.x, dz = q.z - p.z;
                    float d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; fwd = l; t = s; }
                }
                return fwd != null;
            }

            PedLink Reverse(PedLink l)
            {
                foreach (var r in l.To.Links)
                    if (r.To == l.From) return r;
                return null;
            }

            foreach (var (pos, outward, owner) in _pendingDoors)
            {
                if (!NearestLink(pos, 14f, out var fwd, out var t)) continue;
                var back = Reverse(fwd);
                if (back == null) continue;
                var door = new DemoDoor
                {
                    Pos = pos, Outward = outward, Building = owner,
                    LinkFwd = fwd, LinkBack = back, EntryT = t,
                    EntryPos = Vector3.Lerp(fwd.From.Pos, fwd.To.Pos, t / fwd.Length),
                };
                _life.Doors.Add(door);
                _life.AddStop(fwd, t, door, null);
                _life.AddStop(back, fwd.Length - t, door, null);
            }

            foreach (var (pos, yaw) in _pendingBenches)
            {
                if (!NearestLink(pos, 5f, out var fwd, out var t)) continue;
                var back = Reverse(fwd);
                if (back == null) continue;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var bench = new DemoBench
                {
                    SeatTops = new[]
                    {
                        pos + rot * BenchSeatOffsets[0],
                        pos + rot * BenchSeatOffsets[1],
                    },
                    Facing = rot * Vector3.forward,
                    GroundY = pos.y,
                };
                _life.AddStop(fwd, t, null, bench);
                _life.AddStop(back, fwd.Length - t, null, bench);
            }

            _life.SortStops();
        }

        void SpawnPedestrians()
        {
            if (_walkClip == null || _idleClip == null || _pedPrefabs.Count == 0) return;
            var root = new GameObject("People").transform;
            var sidewalks = _pedLinks.FindAll(l => !l.Gated);
            if (sidewalks.Count == 0) return;

            var clips = new PedClips
            {
                Walk = _walkClip, Idle = _idleClip,
                SitDown = _sitDownClip, SitLoop = _sitLoopClip, StandUp = _standUpClip,
                Talk = _talkClip, Shout = _shoutClip,
            };

            // the doorstep share starts indoors and streams out over the first
            // minute - the city fills from its buildings, not from thin air
            int fromDoors = _life != null && _life.Doors.Count > 0
                ? Mathf.RoundToInt(pedestrianCount * insideAtStart)
                : 0;

            // the nerve's wardrobe - a run, a flinch, a fall, the cower - dealt per
            // walker so a crowd running from gunfire is not one runner copied
            var variety = new System.Random(1987);

            for (int k = 0; k < pedestrianCount; k++)
            {
                var link = sidewalks[Random.Range(0, sidewalks.Count)];
                var prefab = _pedPrefabs[Random.Range(0, _pedPrefabs.Count)];
                var go = Instantiate(prefab, root);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                // the crowd casts no shadow: a few pixels of shadow under three hundred
                // walkers is four cascade passes of skinned meshes for nothing
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                SetLayerDeep(go, CrowdLayer); // drawn only within CrowdCullDistance

                var agent = new CivilianAgent { Speed = Random.Range(1.25f, 1.85f) };
                agent.Init(go.transform, CrewKit.ForCrowd(clips, variety), link, Random.value * link.Length * 0.9f);
                agent.Setup(_life);
                if (k < fromDoors)
                    agent.SpawnInside(Random.Range(2f, 60f));
                _pedestrians.Add(agent);
            }
        }

        // The outfit's crews - the ledger's lieutenants and their hoods - out on the
        // sidewalks under the player's command. Dealt by DemoCrews once the
        // PersonnelDirector has seeded the roster (its Start, a frame after this
        // Awake); RoadDemoLedger seats that director in this scene.
        void SpawnCrews()
        {
            if (_walkClip == null || _idleClip == null || _pedLinks.Count == 0) return;
            var clips = CrewKit.WithArms(new PedClips { Walk = _walkClip, Idle = _idleClip });
            _crews = gameObject.AddComponent<DemoCrews>();
            _crews.MuzzleFlashPrefab = CrewKit.MuzzleFlash;
            _crews.BloodPrefab = CrewKit.Blood;
            _crews.ImpactPrefab = CrewKit.Impact;
            _crews.GunshotSets = CrewKit.GunshotSets();
            _crews.CrackClip = CrewKit.Crack;
            _crews.BarTopInset = 52f; // under the top bar (42) with a little air
            _crews.BombsPerCrew = bombsPerCrew;
            _crews.Init(_pedLinks, clips, _pedPrefabs);

            // the law: the patrol cars and beat officers already out answer the
            // dispatcher's calls; the men who get out of a car are dealt by it
            var dispatch = gameObject.AddComponent<PoliceDispatch>();
            dispatch.Init(_crews, clips, _officerPrefabs, CrewKit.Weapon(CrewArms.DefaultSidearm));
            foreach (var car in _policeCars) dispatch.Register(car);
            // only the LEADS stand on the books: a pair answers a call as one unit,
            // the wingman goes wherever his lead is sent
            foreach (var officer in _policeOfficers)
                if (officer.Lead == null) dispatch.Register(officer);

            SpawnRivals();

            // and, for a headless run, the thing that plays all of them at once
            if (monkey)
            {
                var outfit = gameObject.AddComponent<MonkeyOutfit>();
                outfit.seed = monkeySeed * 31 + 7;

                var runner = gameObject.AddComponent<MonkeyRunner>();
                runner.seed = monkeySeed;
                runner.orderEvery = monkeyOrderEvery;
                runner.startAfter = monkeyStartAfter;

                // and the city itself looked over once, while the men are still
                // walking out of their doors
                var audit = gameObject.AddComponent<CityAudit>();
                audit.blocks = _blocks;
                audit.geometry = _geometry;
                Debug.Log($"[monkey] armed: seed {monkeySeed}, an order every " +
                          $"{monkeyOrderEvery:F0}s from {monkeyStartAfter:F0}s");
            }
        }

        // The city's rival mobs, out on sidewalks of their own, so there is somebody
        // for the outfit to shoot it out with (the ledger deals none).
        //
        // Who they are is NOT invented here: GangSeeder deals the families - how many
        // crews each one runs and every man's name - and this pass stands them up and
        // registers what it dealt (GangRegistry), so the ledger's FAMILIES page names
        // the capo the player is actually looking at across the street. One knot of men
        // per LIEUTENANT: a family with three capos holds three corners, in three
        // different quarters, under one name and one colour.
        //
        // The crews are placed in rounds - every family gets its first corner before any
        // family gets its second - so a budget that runs out takes second crews off the
        // biggest mobs and never leaves a family out of the city altogether.
        void SpawnRivals()
        {
            // The books first, and unconditionally: the families exist whether or not
            // this pass finds pavement to stand them on, and the ledger's FAMILIES page
            // reads the registry, not the street.
            var gangs = LivingCity.Gangs.GangSeeder.Generate(RivalSeed, null);
            LivingCity.Gangs.GangRegistry.Install(gangs);

            if (rivalCrewsInCity <= 0) return;
            var sidewalks = _pedLinks.FindAll(l => !l.Gated && l.Length >= 24f);
            if (sidewalks.Count == 0) return;
            var rng = new System.Random(RivalSeed);

            var arms = new[]
            {
                ("SM_Wep_Pistol_Revolver_01", LivingCity.Personnel.EquipmentKind.Pistol),
                ("SM_Wep_Machine_Pistol_01", LivingCity.Personnel.EquipmentKind.MachinePistol),
                ("SM_Wep_Shotgun_01", LivingCity.Personnel.EquipmentKind.Shotgun),
            };

            // The books, cut into crews: one entry per capo, with the soldiers standing
            // behind him in the seeder's flat member list (a Lieutenant opens a crew).
            int families = Mathf.Min(rivalCrewsInCity, gangs.Length - 1);
            var byFamily = new List<List<(string boss, List<string> hoods)>>();
            for (int i = 0; i < families; i++)
            {
                var crews = new List<(string, List<string>)>();
                foreach (var man in gangs[1 + i].Members)
                {
                    if (man.Lieutenant) crews.Add((man.FullName, new List<string>()));
                    else if (crews.Count > 0 &&
                             crews[crews.Count - 1].Item2.Count < rivalHoodsInCity)
                        crews[crews.Count - 1].Item2.Add(man.FullName);
                }
                byFamily.Add(crews);
            }

            // Every family gets PREMISES first - the player's outfit included - and the
            // capo's own crew stands outside its door. The rest of a family's crews hold
            // corners, which is what the sidewalk pass below is for.
            var taken = new List<Vector3>();
            var fronts = SeatFronts(gangs, families, byFamily, taken);

            int placed = 0;
            for (int round = 0; placed < rivalCrewCap; round++)
            {
                bool any = false;
                for (int i = 0; i < families && placed < rivalCrewCap; i++)
                {
                    if (round >= byFamily[i].Count) continue;
                    any = true;
                    if (StandUpCrew(1 + i, round, byFamily[i][round], sidewalks, taken,
                                    arms, rng, round == 0 ? fronts[1 + i] : null))
                        placed++;
                }
                if (!any) break;
            }
        }

        /// <summary>One door per family, spread across the city, with the family's books
        /// stuck on the building behind it (<see cref="GangFront"/>) - that component is
        /// what makes the door clickable as a front.
        ///
        /// The player's outfit is seated FIRST and by the same rule as everybody else: a
        /// don without a place of his own is the one man in the city with nowhere to be
        /// found. His premises carries his own name over the door, and no crew - the men
        /// outside his door are the ledger's, and they come and go with it.
        ///
        /// A building is claimed once. A composed block cuts four doors into one bake,
        /// and two families behind the same wall would open the same card.</summary>
        DemoDoor[] SeatFronts(LivingCity.Gangs.Gang[] gangs, int families,
            List<List<(string boss, List<string> hoods)>> byFamily, List<Vector3> taken)
        {
            var fronts = new DemoDoor[gangs.Length];
            var doors = new List<DemoDoor>();
            if (_life != null)
                foreach (var door in _life.Doors)
                    if (door.Building != null)
                        doors.Add(door);

            if (doors.Count == 0)
            {
                Debug.LogWarning("[RoadDemo] No street door has a building behind it - " +
                                 "the families operate out of nowhere.");
                return fronts;
            }

            var claimed = new HashSet<GameObject>();
            for (int id = 0; id <= families; id++)
            {
                var door = FarthestDoor(doors, claimed, taken);
                if (door == null) break;

                claimed.Add(door.Building);
                taken.Add(door.EntryPos);
                fronts[id] = door;

                var crew = id > 0 && byFamily[id - 1].Count > 0 ? byFamily[id - 1][0] : default;
                var capo = id == 0 ? LivingCity.Gangs.GangCatalog.BossName : crew.boss;
                int men = id == 0 ? 0 : (crew.hoods?.Count ?? 0) + 1;

                var books = LivingCity.Gangs.FrontBooks.Open(
                    gangs[id].Name, capo, men, gangs[id].MemberSeed);
                books.Address = AddressOf(door);

                door.Building.AddComponent<GangFront>()
                    .Bind(id, gangs[id].Name, books, door.Pos, door.Outward);
                // and into the registry, so the ledger's FAMILIES page names the same
                // door the street card does
                LivingCity.Gangs.GangRegistry.SetFrontBooks(id, books);
            }

            return fronts;
        }

        /// <summary>The free door furthest from everything already seated - the same
        /// min-squared-distance argmax the city path picks fronts with (GangFronts), so
        /// the families end up in different quarters rather than on one street.</summary>
        static DemoDoor FarthestDoor(
            List<DemoDoor> doors, HashSet<GameObject> claimed, List<Vector3> taken)
        {
            DemoDoor best = null;
            float bestScore = -1f;
            foreach (var door in doors)
            {
                if (claimed.Contains(door.Building)) continue;

                float score = float.MaxValue;
                foreach (var seat in taken)
                {
                    float dx = door.EntryPos.x - seat.x, dz = door.EntryPos.z - seat.z;
                    float sqr = dx * dx + dz * dz;
                    if (sqr < score) score = sqr;
                }

                if (score > bestScore) { bestScore = score; best = door; }
            }

            return best;
        }

        /// <summary>Number and street for a door - the line the licence is issued to.
        /// The street it fronts is the one its facade faces (a door on an east wall is
        /// on the north-south street), and the number counts off the far end of that
        /// street in the city's own metres, evens on one side as they are everywhere.</summary>
        string AddressOf(DemoDoor door)
        {
            bool onVertical = Mathf.Abs(door.Outward.x) > Mathf.Abs(door.Outward.z);
            string name = null;
            float along = 0f;

            if (onVertical && verticalRoadX != null && verticalRoadX.Length > 0)
            {
                int line = 0;
                for (int i = 1; i < verticalRoadX.Length; i++)
                    if (Mathf.Abs(verticalRoadX[i] - door.Pos.x) <
                        Mathf.Abs(verticalRoadX[line] - door.Pos.x))
                        line = i;
                name = Streets.Vertical(line);
                along = door.Pos.z;
            }
            else if (!onVertical && horizontalRoadZ != null && horizontalRoadZ.Length > 0)
            {
                int line = 0;
                for (int j = 1; j < horizontalRoadZ.Length; j++)
                    if (Mathf.Abs(horizontalRoadZ[j] - door.Pos.z) <
                        Mathf.Abs(horizontalRoadZ[line] - door.Pos.z))
                        line = j;
                name = Streets.Horizontal(line);
                along = door.Pos.x;
            }

            if (string.IsNullOrEmpty(name)) return "";
            int number = Mathf.Max(2, Mathf.RoundToInt(Mathf.Abs(along) / 3f));
            // the side of the street decides odds or evens, the way a real block does
            bool evens = door.Outward.x + door.Outward.z > 0f;
            if (evens != (number % 2 == 0)) number++;
            return number + " " + name;
        }

        /// <summary>One capo and his men on a corner of their own - or, when
        /// <paramref name="front"/> is his family's premises, on the pavement outside its
        /// door, facing the street. Returns whether they reached the pavement - a family
        /// whose coat is missing from the baked cast is skipped, never
        /// half-stood-up.</summary>
        bool StandUpCrew(int gang, int crewIndex,
            (string boss, List<string> hoods) crew, List<PedLink> sidewalks,
            List<Vector3> taken,
            (string, LivingCity.Personnel.EquipmentKind)[] arms, System.Random rng,
            DemoDoor front = null)
        {
            // the family's own bodies: the catalog is as long as the city has mobs, so
            // id 12 is Greco's coat and nobody else's
            var bossModel = LivingCity.Gangs.GangCatalog.LieutenantModels[gang];
            var staple = LivingCity.Gangs.GangCatalog.SoldierModels[gang];
            var bossPrefab = Cast(bossModel);
            if (bossPrefab == null) return false;

            // A body per man, all different and none of them the lieutenant's - a rival
            // crew is four men, not one man standing four times. A family's SECOND crew
            // starts its walk further along the stock, so the two corners are not the
            // same three coats twice over.
            var hoods = LivingCity.Gangs.GangLooks.Hoods;
            var from = hoods[(LivingCity.Gangs.GangLooks.IndexOf(staple) +
                              3 * crewIndex) % hoods.Length];
            var hoodPrefabs = new List<GameObject>();
            foreach (var look in LivingCity.Gangs.GangLooks.HoodsFor(
                         bossModel, from, crew.hoods.Count))
            {
                var body = Cast(look);
                if (body) hoodPrefabs.Add(body);
            }

            Vector3 anchor, facing;
            if (front != null)
            {
                // Outside his own door, backs to the shop: the entry point is the
                // pavement spot the crowd uses for that door, so the men stand where a
                // man waiting for somebody inside would stand, not in the wall or the
                // road. The line runs along the facade (LineOffset spreads across the
                // facing), which is the shopfront loafing the sidewalk pass already does.
                anchor = front.EntryPos;
                facing = front.Outward;
            }
            else
            {
                // A sidewalk far from every crew already standing (the outfit is dealt
                // later, spread by its own rule). Farthest-of-N random pavements: with a
                // mob on every third corner, twelve looks is not enough of the map to
                // keep the last of them off each other, so the sample grows with the crowd.
                PedLink link = null;
                float bestD = -1f;
                int looks = Mathf.Clamp(6 * rivalCrewCap, 12, 96);
                for (int tries = 0; tries < looks; tries++)
                {
                    var l = sidewalks[rng.Next(sidewalks.Count)];
                    var mid = (l.From.Pos + l.To.Pos) * 0.5f;
                    float near = float.MaxValue;
                    foreach (var t in taken) near = Mathf.Min(near, (t - mid).sqrMagnitude);
                    if (near > bestD) { bestD = near; link = l; }
                }
                anchor = (link.From.Pos + link.To.Pos) * 0.5f;
                var along = (link.To.Pos - link.From.Pos).normalized;
                facing = Vector3.Cross(Vector3.up, along); // across the pavement: the line runs along it
            }

            taken.Add(anchor);

            var (weaponName, kind) = arms[(gang + crewIndex) % arms.Length];
            // mixed arms: the crew is not four copies of one gun - each man is asked for
            // separately as he is stood up, and draws his own off the counter
            System.Func<int, (GameObject, LivingCity.Personnel.EquipmentKind)> armsFor = null;
            if (mixedArms)
                armsFor = _ =>
                {
                    var (model, k) = MobArm(rng);
                    return (CrewKit.Weapon(model), k);
                };

            _crews.AddRival(gang, LivingCity.Gangs.GangCatalog.Names[gang], crew.boss,
                bossPrefab, crew.hoods, hoodPrefabs, anchor, facing,
                CrewKit.Weapon(weaponName), kind, lineUp: true, armsFor: armsFor);
            return true;
        }

        /// <summary>What one man of a mob is holding when the arms are mixed: a piece off
        /// the armory's own counter, or the .38 that is in every coat under it. One
        /// table for the street and the ledger both - the counter is where guns come
        /// from, here as on the armory page.</summary>
        static (string model, LivingCity.Personnel.EquipmentKind kind) MobArm(System.Random rng)
        {
            var counter = LivingCity.Outfit.ArmoryCatalog.Weapons;
            int pick = rng.Next(counter.Length + 1);
            if (pick >= counter.Length)
                return (CrewArms.DefaultSidearm, LivingCity.Personnel.EquipmentKind.Pistol);
            var item = counter[pick];
            return (item.ModelName ?? CrewArms.DefaultSidearm, item.Kind);
        }

        /// <summary>The plain pack body of this name - the ledger's baked cast first,
        /// the picture desk's resolver behind it.</summary>
        static GameObject Cast(string name) =>
            LivingCity.UI.LedgerModelSet.PersonNamed(name) ??
            LivingCity.UI.PortraitStudio.FindPeoplePrefab(name);

        static string DrawName(System.Random rng)
        {
            var firsts = LivingCity.Entities.PedestrianIdentity.AllMaleNames;
            var surnames = LivingCity.Entities.PedestrianIdentity.AllSurnames;
            return firsts[rng.Next(firsts.Count)] + " " + surnames[rng.Next(surnames.Count)];
        }

        // ------------------------------------------------------------------- cars

        void SpawnCars()
        {
            int placed = 0;
            // Every lane in turn, in a shuffled order. Walking _edges as it was built
            // gave the city's cars to the first few hundred lanes of the grid and left
            // whatever was wired last - a district's streets, a motorway's decks - with
            // none at all: the road existed and nothing was ever on it.
            var lanes = new List<RoadEdge>(_edges.Count);
            foreach (var e in _edges)
            {
                // never on an auxiliary lane: it begins or ends in the middle of a
                // motorway, and a car put there at build time has nowhere to go but
                // sideways before it has moved at all
                if (e.Auxiliary) continue;
                lanes.Add(e);
            }
            var shuffle = new System.Random(spacingSeed * 977 + 13);
            for (int i = lanes.Count - 1; i > 0; i--)
            {
                int j = shuffle.Next(i + 1);
                (lanes[i], lanes[j]) = (lanes[j], lanes[i]);
            }
            for (int round = 0; placed < carCount && round < 40; round++)
            {
                bool any = false;
                foreach (var e in lanes)
                {
                    if (placed >= carCount) break;
                    float s = 6f + round * 18f;
                    if (s > e.Length - 12f) continue;
                    any = true;

                    var prefab = _carPrefabs[Random.Range(0, _carPrefabs.Count)];
                    var go = Instantiate(prefab, Vector3.zero, Quaternion.identity, _cars);
                    // a colour of its own, unless the body carries somebody's livery
                    LivingCity.Gameplay.VehiclePaint.Apply(go, prefab);
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                    foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);

                    var bounds = new Bounds(go.transform.position, Vector3.zero);
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                        bounds.Encapsulate(r.bounds);

                    var v = new DemoVehicle
                    {
                        Tf = go.transform,
                        HalfLen = bounds.extents.z + 0.3f,
                        HalfWide = Mathf.Clamp(bounds.extents.x, 0.7f, 1.3f),
                    };
                    v.Spawn(e, s);
                    // somebody at the wheel, now and then somebody beside him - bodies
                    // out of the crowd's wardrobe, culled with the crowd
                    CarOccupant.Crew(go.transform, _pedPrefabs, _sitLoopClip, passengerChance: 0.3f, layer: CrowdLayer);
                    _vehicles.Add(v);
                    StreetTraffic.Users.Add(v); // the men on foot, and the outfit's drivers, see it
                    placed++;
                }
                if (!any) break;
            }
        }

        // ----------------------------------------------------------------- bikes

        StreetBikes _bikes;

        // The city's two-wheelers. They are not in the car scan and must not be: every
        // folder scan here denies "bike", "moped" and "scooter" by name, and did so for
        // a good reason - a two-wheeler dropped into the traffic was a thing that slid
        // along the road with nobody on it and no way to sit anybody on it. What has
        // changed is that there is now somewhere to put the man (BikePose), so they are
        // asked for by name out of the catalogue instead, exactly as a marked cruiser is.
        //
        // Two kinds go down: the ones riding, which StreetBikes owns and ticks, and the
        // ones left on their stands along a kerb, which are furniture with a body - the
        // traffic plans round them like any parked car.
        void SpawnBikes()
        {
            if (bikeCount <= 0 || Net == null) return;
            var bodies = StreetBikes.Bodies();
            if (bodies.Count == 0)
            {
                Debug.LogWarning("[RoadDemo] No two-wheeler out of VehicleCatalog.Motorcycles; no bikes on the street");
                return;
            }

            _bikes = gameObject.AddComponent<StreetBikes>();
            // the city's asphalt is the origin's own level - the cars here leave
            // RoadCar.RoadY at nought and sit on it
            const float roadY = 0f;
            _bikes.Init(Net, bikeCount, roadY, _pedPrefabs, CrewKit.Ride,
                pillionChance: 0.3f, layer: CrowdLayer, roads: null, bodies: bodies);

            // and a few stood at kerbs, off the roads the cars were laid along
            int stood = Mathf.Max(1, bikeCount / 2);
            var spots = new List<Vector3>(stood);
            for (int i = 0; i < stood && _edges.Count > 0; i++)
            {
                var e = _edges[Random.Range(0, _edges.Count)];
                if (e.Length < 30f) continue;
                spots.Add(e.Start + e.Dir * Random.Range(12f, e.Length - 12f));
            }
            StreetBikes.ParkSeveral(Net, _cars, spots, roadY, bodies);
        }

        // ---------------------------------------------------------------- police

        const float StallSetback = 3.4f; // stall centre off the station face
        const float StallSpacing = 4f;   // bay pitch along the face

        // Called by BuildBlocks the moment the station lands in an interior: pick
        // the forecourt side (the widest strip of court between the building and
        // its street) and lay the stall row against the face there, which is what
        // the patrol cars dock against.
        void PlanForecourt(float xMin, float xMax, float zMin, float zMax, float floorTop)
        {
            var b = BoundsOf(_policeStation);
            var sides = new (float gap, Vector3 outDir, float face, float edge)[]
            {
                (xMax - b.max.x, Vector3.right, b.max.x, xMax),
                (b.min.x - xMin, Vector3.left, b.min.x, xMin),
                (zMax - b.max.z, Vector3.forward, b.max.z, zMax),
                (b.min.z - zMin, Vector3.back, b.min.z, zMin),
            };
            var best = sides[0];
            for (int k = 1; k < sides.Length; k++)
                if (sides[k].gap > best.gap) best = sides[k];

            _stallOut = best.outDir;
            _stallAlong = Vector3.Cross(Vector3.up, best.outDir);
            _stallLift = floorTop > 0f ? floorTop + 0.02f : 0.02f;
            _stallRowHalf = Mathf.Max(1, policeCarCount) * StallSpacing * 0.5f;

            var centre = b.center;
            _stallCentre = new Vector3(
                best.outDir.x != 0f ? best.face + best.outDir.x * StallSetback : centre.x,
                _stallLift,
                best.outDir.z != 0f ? best.face + best.outDir.z * StallSetback : centre.z);
            _forecourtPlanned = true;
        }

        void SpawnPolice()
        {
            var policeRoot = new GameObject("Police").transform;
            var markers = new List<IPatrolMarker>();

            // the station's own layer - the cars docked at its forecourt and the
            // pair that rests inside it - only where a station actually stands
            if (_policeStation != null && _forecourtPlanned)
            {
                SpawnPatrolCars(policeRoot, markers);
                SpawnFootPatrols(policeRoot, markers);
            }

            // and the beat pairs over the blocks, station or no station: the law
            // the player sees on the first frame, wherever he looks
            SpawnBlockBeats(policeRoot, markers);

            if (markers.Count == 0) return;
            gameObject.AddComponent<PolicePatrolOverlay>().Init(markers);
        }

        void SpawnPatrolCars(Transform parent, List<IPatrolMarker> markers)
        {
            if (_policeCarPrefabs.Count == 0 || policeCarCount <= 0) return;

            // the kerb: the nearest lane point, where the fleet undocks onto the
            // graph and rolls to a stop coming home
            RoadEdge home = null;
            float homeS = 0f, bestD = float.MaxValue;
            foreach (var e in _edges)
            {
                if (e.Length < 26f) continue;
                float s = Mathf.Clamp(Vector3.Dot(_stallCentre - e.Start, e.Dir), 10f, e.Length - 14f);
                var p = e.Start + e.Dir * s;
                float d = (p - _stallCentre).sqrMagnitude;
                if (d < bestD) { bestD = d; home = e; homeS = s; }
            }
            if (home == null)
            {
                Debug.LogWarning("[RoadDemo] no lane near the police forecourt; fleet stays parked");
                return;
            }

            var stallRot = Quaternion.LookRotation(_stallOut);
            // where each car sits at rest: the first in the forecourt, the rest pushed
            // out across the city so the force is spread, not poured out of one gate
            var homes = SpreadPatrolHomes(policeCarCount, home, homeS, stallRot);

            for (int i = 0; i < policeCarCount; i++)
            {
                var hi = homes[i];
                var policePrefab = _policeCarPrefabs[i % _policeCarPrefabs.Count];
                var go = Instantiate(policePrefab, hi.stall, Quaternion.identity, parent);
                go.name = "Patrol Car " + (i + 1);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);

                // half length measured at identity yaw, before the stall rotation
                var bounds = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    bounds.Encapsulate(r.bounds);
                go.transform.rotation = hi.rot;

                var car = new PolicePatrolCar
                {
                    Tf = go.transform,
                    HalfLen = bounds.extents.z + 0.3f,
                    HalfWide = Mathf.Clamp(bounds.extents.x, 0.7f, 1.3f),
                    UnitNumber = i + 1,
                    // the body is named for the fleet list and not for the pack, so the
                    // machine is handed over rather than read off the transform
                    Machine = LivingCity.Gameplay.VehiclePerformance.For(policePrefab.name),
                };
                // each car returns to its own kerb and patrols its own quarter
                var carRouteHome = PolicePatrolCar.RouteToward(_edges, hi.home);
                car.InitParked(hi.stall, hi.rot, hi.home, hi.homeS, _edges, carRouteHome,
                    policeRestSeconds, policePatrolWaypoints, Random.Range(3f, 8f) + i * 5f);
                // an officer at the wheel - the force's own uniform; he is indoors
                // while the car stands in its stall (PolicePatrolCar shows him)
                var officers = CarOccupant.Crew(go.transform, _officerPrefabs, _sitLoopClip, layer: CrowdLayer);
                if (officers.Count > 0) car.Officer = officers[0];
                _policeCars.Add(car);
                StreetTraffic.Users.Add(car);
                markers.Add(car);
            }
        }

        /// <summary>Where each patrol car stands at rest. The first keeps the station
        /// forecourt, so the house is never empty; the rest are pushed OUT across the map
        /// by farthest-point sampling over the long lanes - each rests at a kerb of its
        /// own and patrols its own quarter, so at any moment the force is scattered over
        /// the city instead of clustered against one station face. (Their patrol beats
        /// already reached the whole map; only the standing start was in one place.)</summary>
        List<(RoadEdge home, float homeS, Vector3 stall, Quaternion rot)> SpreadPatrolHomes(
            int count, RoadEdge forecourtHome, float forecourtHomeS, Quaternion forecourtRot)
        {
            var homes = new List<(RoadEdge, float, Vector3, Quaternion)>(count);
            homes.Add((forecourtHome, forecourtHomeS, _stallCentre, forecourtRot));
            if (count <= 1) return homes;

            // Scattering the RESTING cars over the city jams the traffic: a patrol left at
            // a kerb is a registered obstacle, and even set off the running lane (KerbClear)
            // it gridlocked the ambient cars in ~a quarter of seeds (car soak: worst 14k+
            // belt refusals with the spread; 0 in every one of 12 with the cars docked).
            // So the spread is OFF until the resting spots are provably traffic-safe (a
            // known parking bay, not a computed kerb point). The city still gets its police
            // presence from the patrols, whose beats already cover the whole map, and the
            // reaction to fights stays LOCAL (PoliceDispatch.ResponseRange), which is what
            // the user actually asked for. Flip to true again once placement is safe.
            const bool SPREAD = false;
            if (!SPREAD)
            {
                for (int i = 1; i < count; i++) homes.Add(homes[0]);
                return homes;
            }

            var longs = new List<RoadEdge>();
            foreach (var e in _edges) if (e.Length >= 30f) longs.Add(e);
            if (longs.Count == 0)
            {
                for (int i = 1; i < count; i++) homes.Add(homes[0]);
                return homes;
            }

            // farthest-point sampling: each new home is the long lane whose middle is
            // farthest from the station and from every home already placed
            var anchors = new List<Vector3> { _stallCentre };
            for (int n = 1; n < count; n++)
            {
                RoadEdge best = null;
                float bestNear = -1f;
                foreach (var e in longs)
                {
                    var mid = e.Start + e.Dir * (e.Length * 0.5f);
                    float near = float.MaxValue;
                    for (int a = 0; a < anchors.Count; a++)
                        near = Mathf.Min(near, (mid - anchors[a]).sqrMagnitude);
                    if (near > bestNear) { bestNear = near; best = e; }
                }
                if (best == null) best = longs[Random.Range(0, longs.Count)];
                float s = best.Length * 0.5f;
                var on = best.Start + best.Dir * s;
                // rest CLEAR of the running lane, not stood in it: a patrol car left in a
                // live lane is a registered obstacle every car has to thread past, and in
                // a jammed street (the roadblock) that tips the traffic into gridlock.
                // Walk out to the far side of the kerb (past the outermost lane band, so
                // LaneNet marks it parked-and-passed, not a wreck-in-lane); the undock
                // curve pulls it back onto the lane when its rest is up.
                var right = new Vector3(best.Dir.z, 0f, -best.Dir.x);
                var stall = KerbClear(on, right);
                stall.y = _stallLift;
                anchors.Add(on);
                homes.Add((best, s, stall, Quaternion.LookRotation(best.Dir, Vector3.up)));
            }
            return homes;
        }

        /// <summary>The nearest point off the carriageway to rest a car - out past a kerb
        /// far enough that its body clears the outermost lane band (so LaneNet counts it
        /// parked-and-passed, not a wreck-in-lane the traffic must plan a way round).
        /// Both kerbs are tried; the nearer clear point wins. Falls back to a plain 4 m
        /// step if there is no net to measure against.</summary>
        Vector3 KerbClear(Vector3 on, Vector3 right)
        {
            var net = LaneNet.Active;
            if (net == null) return on + right * 4f;
            float bestOff = float.MaxValue;
            var best = on + right * 4f;
            for (int sign = -1; sign <= 1; sign += 2)
                for (float off = 3f; off <= 8f; off += 0.5f)
                {
                    var cand = on + right * (sign * off);
                    var road = net.Locate(cand, out _, out float dd, 1.0f);
                    bool clear = road == null || Mathf.Abs(dd) > road.HalfRoad + 1.3f;
                    if (clear) { if (off < bestOff) { bestOff = off; best = cand; } break; }
                }
            return best;
        }

        void SpawnFootPatrols(Transform parent, List<IPatrolMarker> markers)
        {
            if (_officerPrefabs.Count == 0 || policeOfficerCount <= 0 ||
                _walkClip == null || _idleClip == null) return;

            // the station door: on the forecourt face, past the end of the stall
            // row so the walk out does not thread between parked cars
            var faceCentre = _stallCentre - _stallOut * StallSetback;
            var door = faceCentre + _stallAlong * (_stallRowHalf + 2.5f) + _stallOut * 0.8f;
            door.y = _stallLift;

            // the home stretch: the nearest sidewalk link, joined mid-way exactly
            // like the cars join their kerb lane
            PedLink homeFwd = null;
            float entryT = 0f, bestD = float.MaxValue;
            foreach (var l in _pedLinks)
            {
                if (l.Gated || l.Length < 6f) continue;
                var ab = l.To.Pos - l.From.Pos;
                var dir = ab.normalized;
                float t = Mathf.Clamp(Vector3.Dot(door - l.From.Pos, dir), 2f, l.Length - 2f);
                var p = l.From.Pos + dir * t;
                float d = (p - door).sqrMagnitude;
                if (d < bestD) { bestD = d; homeFwd = l; entryT = t; }
            }
            if (homeFwd == null) return;

            PedLink homeBack = null;
            foreach (var l in homeFwd.To.Links)
                if (l.To == homeFwd.From) { homeBack = l; break; }
            if (homeBack == null) return;

            var routeHome = PoliceFootPatrol.RouteHome(homeFwd);

            // the station block's own pavement ring: the pair's beat. Where no ring
            // closes (a torn graph) it comes back null and the officers keep the
            // old wander over the quarter.
            var ring = PoliceFootPatrol.BeatRing(homeFwd, homeBack, door);

            // every walkable corner, the officers' waypoint pool
            var nodeSet = new HashSet<PedNode>();
            foreach (var l in _pedLinks) { nodeSet.Add(l.From); nodeSet.Add(l.To); }
            var nodes = new List<PedNode>(nodeSet);

            // dealt in PAIRS: the even man leads the beat and stands on the
            // dispatcher's books, the odd man behind him is his wingman. An odd
            // count leaves the last man walking his round alone.
            PoliceFootPatrol lead = null;
            for (int i = 0; i < policeOfficerCount; i++)
            {
                var prefab = _officerPrefabs[i % _officerPrefabs.Count];
                var go = Instantiate(prefab, door, Quaternion.identity, parent);
                go.name = "Beat Officer " + (i + 1);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);

                var officer = new PoliceFootPatrol
                    { Speed = Random.Range(1.3f, 1.5f), UnitNumber = i + 1 };
                // the beat's whole wardrobe: the walk and the stand, the JOG he answers
                // a call at, and the PISTOL IDLE he stands over an arrest in
                officer.Init(go.transform,
                    CrewKit.WithArms(new PedClips { Walk = _walkClip, Idle = _idleClip }),
                    homeFwd, entryT);
                officer.Configure(door, homeFwd, homeBack, entryT, nodes, routeHome,
                    policeRestSeconds, policePatrolWaypoints,
                    Random.Range(4f, 10f) + (i / 2) * 6f);
                if (i % 2 == 0)
                {
                    officer.SetBeat(ring);
                    lead = officer;
                }
                else officer.FollowLead(lead);
                _policeOfficers.Add(officer);
                markers.Add(officer);
            }
        }

        // The beat pairs over the blocks: one block each, dealt in a stride down the
        // lot list so they land spread over the whole map, and stood ON their round
        // from the first frame - the player never watches the law file out of one door.
        void SpawnBlockBeats(Transform parent, List<IPatrolMarker> markers)
        {
            if (_officerPrefabs.Count == 0 || _walkClip == null || _idleClip == null) return;
            int pairs = policeBeatPairs < 0
                ? Mathf.Max(1, _lotPlans.Count / 4)
                : policeBeatPairs;
            if (pairs <= 0 || _lotPlans.Count == 0 || _pedLinks.Count == 0) return;
            pairs = Mathf.Min(pairs, _lotPlans.Count);

            // the waypoint pool a call routes over - every walkable corner
            var nodeSet = new HashSet<PedNode>();
            foreach (var l in _pedLinks) { nodeSet.Add(l.From); nodeSet.Add(l.To); }
            var nodes = new List<PedNode>(nodeSet);

            int unit = _policeOfficers.Count;
            for (int p = 0; p < pairs; p++)
            {
                var lot = _lotPlans[p * _lotPlans.Count / pairs];
                var centre = new Vector3(lot.Interior.center.x, 0f, lot.Interior.center.y);

                // the block's nearest stretch of pavement, and its reverse
                PedLink front = null;
                float bestD = float.MaxValue;
                foreach (var l in _pedLinks)
                {
                    if (l.Gated || l.Length < 6f) continue;
                    var ab = l.To.Pos - l.From.Pos;
                    var dir = ab.normalized;
                    float t = Mathf.Clamp(Vector3.Dot(centre - l.From.Pos, dir), 0f, l.Length);
                    float d = (l.From.Pos + dir * t - centre).sqrMagnitude;
                    if (d < bestD) { bestD = d; front = l; }
                }
                if (front == null) continue;
                PedLink back = null;
                foreach (var l in front.To.Links)
                    if (l.To == front.From) { back = l; break; }
                if (back == null) continue;

                var ring = PoliceFootPatrol.BeatRing(front, back, centre);
                if (ring == null) continue;

                // the ring's own first stretch, to be stood on mid-stride
                PedLink start = null;
                foreach (var l in ring[0].Links)
                    if (l.To == ring[1 % ring.Count]) { start = l; break; }
                if (start == null) continue;

                PoliceFootPatrol lead = null;
                for (int i = 0; i < 2; i++)
                {
                    var prefab = _officerPrefabs[(unit + i) % _officerPrefabs.Count];
                    var go = Instantiate(prefab, start.From.Pos, Quaternion.identity, parent);
                    go.name = "Beat Officer " + (unit + i + 1);
                    foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);

                    var officer = new PoliceFootPatrol
                        { Speed = Random.Range(1.3f, 1.5f), UnitNumber = unit + i + 1 };
                    officer.Init(go.transform,
                        CrewKit.WithArms(new PedClips { Walk = _walkClip, Idle = _idleClip }),
                        start, i == 0 ? 1.5f : 0.3f);
                    officer.ConfigureBeat(nodes, policePatrolWaypoints);
                    if (i == 0)
                    {
                        officer.SetBeat(ring);
                        lead = officer;
                    }
                    else officer.FollowLead(lead);
                    _policeOfficers.Add(officer);
                    markers.Add(officer);
                }
                unit += 2;
            }
        }

        // ------------------------------------------------------------ environment

        void BuildEnvironment()
        {
            float minX = verticalRoadX[0], maxX = verticalRoadX[verticalRoadX.Length - 1];
            float minZ = horizontalRoadZ[0], maxZ = horizontalRoadZ[horizontalRoadZ.Length - 1];
            // the town is the grid AND its quarters: the camera must be able to look at
            // the port and the suburbs too, so the boom is measured over all of it
            foreach (var r in _landRects)
            {
                minX = Mathf.Min(minX, r.xMin); maxX = Mathf.Max(maxX, r.xMax);
                minZ = Mathf.Min(minZ, r.yMin); maxZ = Mathf.Max(maxZ, r.yMax);
            }
            var centre = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);

            // The city stands on an island: past the last road the ground goes on as
            // wilderness - grass, hills, woods, rock - down to a beach and into the sea
            // that lies all round it (RoadDemoBuilder.Island.cs). The grid rectangle
            // itself is fully tiled by carriageways, sidewalks and the interior pads,
            // so the island's ground rings it and never runs beneath it.
            float gx0 = verticalRoadX[0] - VHalf(0) - Sidewalk;
            float gx1 = verticalRoadX[verticalRoadX.Length - 1] + VHalf(verticalRoadX.Length - 1) + Sidewalk;
            float gz0 = horizontalRoadZ[0] - HHalf(0) - Sidewalk;
            float gz1 = horizontalRoadZ[horizontalRoadZ.Length - 1] + HHalf(horizontalRoadZ.Length - 1) + Sidewalk;
            BuildIsland(gx0, gx1, gz0, gz1);

            var sunGo = new GameObject("Sun");
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.intensity = sunIntensity;
            _sun.color = new Color(1f, 0.96f, 0.87f);
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = Mathf.Clamp01(sunShadowStrength);
            sunGo.transform.rotation = Quaternion.Euler(sunAngles.x, sunAngles.y, sunAngles.z);

            var camGo = new GameObject("Demo Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            // the island is kilometres across now and the map's last click booms the
            // camera five of them up; the fog eats everything past two, but the far
            // plane must still be beyond the boom or the whole world clips away
            cam.farClipPlane = 8000f;
            // only what is worth drawing at the distance: the small stuff, the crowd
            // and the trees drop out past their ranges (AssignCullLayers puts them on
            // the layers) - a bin at four hundred metres is not a pixel
            var cull = new float[32];
            cull[PropLayer] = PropCullDistance;
            cull[CrowdLayer] = CrowdCullDistance;
            cull[MidLayer] = MidCullDistance;
            cam.layerCullDistances = cull;
            cam.layerCullSpherical = true;

            // without this the DemoGrade volume renders to nothing: a URP camera
            // opts into post-processing per camera. SMAA rather than MSAA because
            // the PC renderer runs deferred, where MSAA does not apply.
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;

            // no AudioListener here: at a 190 m boom a listener on the lens puts the
            // whole street outside any sane rolloff. DemoAudio parks the scene's one
            // ear on the camera's FOCUS instead.
            var dc = camGo.AddComponent<DemoCamera>();
            dc.pivot = centre;
            // In the street, not over it: past dc.mapAt the printed map takes the
            // screen, so the city has to open on THIS side of that line - a few
            // blocks in the frame, the map one pull of the wheel away.
            dc.distance = Mathf.Min(165f, dc.mapAt - 15f);
            dc.yaw = 33f;
            dc.pitch = 52f;

            // the project's pipeline asset stops shadows 50 m from the camera -
            // which is inside the boom, so from up here nothing cast one at all
            camGo.AddComponent<DemoShadows>().rig = dc;
            _rig = dc;

            // catalog-style building card on click; only the block bakes answer,
            // the street kit's own colliders stay mute
            _picker = camGo.AddComponent<LivingCity.CameraRig.BuildingCardPicker>();
            _picker.pickRoot = _blocks;

            // and down in the street, the near facades get out of the way of it: the
            // block bakes and the quarters are what may be seen through, and nothing
            // else is - the harbour's ships and the airfield's hangars are the picture
            // where they stand, not something between the player and a pavement.
            var cutaway = camGo.AddComponent<StreetCutaway>();
            cutaway.rig = dc;
            var seeThrough = new List<Transform> { _blocks };
            seeThrough.AddRange(_districtStatic);
            cutaway.roots = seeThrough.ToArray();
        }

        // ------------------------------------------------------------- day/night

        Light _sun;
        DemoCamera _rig;
        DemoClock _clock;

        // The demo's own day/night stack, self-contained in this folder: DemoClock
        // advances the hour and owns pause/speed, DemoSky swings the sun and moon
        // under a procedural skybox with the PalmCity cloud ring, DemoStreetLamps
        // and DemoHeadlights light the street after dark, DemoNightWindows lights
        // the window panes and signage on the facades, and DemoTopBar puts the
        // clock and the time controls across the top of the screen.
        void BuildDayNight()
        {
            var go = new GameObject("DayNight");

            var clock = go.AddComponent<DemoClock>();
            clock.secondsPerGameHour = Mathf.Max(0.02f, realSecondsPerGameHour);
            clock.startHour = startHour;

            var sky = go.AddComponent<DemoSky>();
            sky.clock = clock;
            sky.sun = _sun;
            sky.linearHaze = linearHaze;

#if UNITY_EDITOR
            // (no cloud ring: the PalmCity ring was a slab of geometry turning over
            // the whole city for a few painted clouds, and it was asked off)

            // the sky sphere the PalmCity demo scene itself stands under - a Synty
            // dome wearing a painted gradient, which is what makes their sky read
            // as the same art as the buildings under it. DemoSky sizes it, walks it
            // with the camera and tints it through the day.
            var domePrefab = RoadDemo.DemoAssetLoad.Load<GameObject>(
                "Assets/Synty/PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Skydome_01.prefab");
            if (domePrefab != null)
            {
                var dome = Instantiate(domePrefab, Vector3.zero, Quaternion.identity);
                dome.name = "SkyDome";
                sky.skyDome = dome.transform;
                sky.skyDomeRenderer = dome.GetComponentInChildren<Renderer>();
            }
#endif

            // PalmCity's own colour grade over the top of all of it (the component
            // brings its own global Volume in)
            var grade = go.AddComponent<DemoGrade>();
            grade.clock = clock;
            grade.look = look;

            var lamps = go.AddComponent<DemoStreetLamps>();
            lamps.clock = clock;

            var windows = go.AddComponent<DemoNightWindows>();
            windows.clock = clock;
            // window panes are lit inside the block bakes only - the traffic's
            // windscreens use the same glass materials and must stay dark
            windows.facadeRoot = _blocks;

            var headlights = go.AddComponent<DemoHeadlights>();
            headlights.clock = clock;
            foreach (var v in _vehicles)
                headlights.Register(v.Tf, v.HalfLen);
            foreach (var traffic in _highwayTraffic)
                foreach (var car in traffic.Cars())
                    headlights.Register(car, 2.3f);

            var barGo = new GameObject("TopBar");
            var bar = barGo.AddComponent<DemoTopBar>();
            bar.clock = clock;

            _clock = clock;
        }

        // --------------------------------------------------------------- exhaust

        // The smoke out of the tailpipes. One rig for the whole city and no
        // registration at all: it reads the cars off StreetTraffic.Users itself, and
        // smokes the few nearest the camera (CarExhaust).
        void BuildExhaust() => CarExhaust.Install();

        // ----------------------------------------------------------------- audio

        // The demo's mix, self-contained in this folder like everything else here:
        // wind and a traffic hum under the whole city, engines on the cars nearest
        // the camera's focus, and a pooled trickle of footsteps, voices, doors and
        // voices over the top. Built last of the world layers, because it reads the
        // builder's own live lists - anything spawned after this is heard too.
        void BuildAudio()
        {
            var go = new GameObject("Audio");
            go.AddComponent<DemoAudio>().Init(_clock, _rig, _vehicles, _policeCars, _pedestrians);
            // and the frame-time probe, writing Logs/perf-probe.txt every few seconds
            new GameObject("Perf Probe").AddComponent<DemoPerfProbe>();
        }

        // ------------------------------------------------------------------- map
        //
        // The war-room half: the ledger the demo installs takes the left of the
        // screen and leaves the right empty, so the top-down map moves in there for
        // as long as the book stands open. Built last, when there is a city to draw
        // and a crowd to plot.
        void BuildMap()
        {
            var go = new GameObject("Map");
            go.AddComponent<DemoMap>().Init(this, _blocks, _picker, _rig,
                _pedestrians, _policeOfficers, _vehicles, _policeCars, _crews);

            // And the turf map: the same city as a 1987 survey plate, on T. Its own
            // screen, not a mode of the plan above - it draws the city onto paper
            // rather than photographing it, and everything on it is the outfit's
            // business rather than the street's.
            var turf = new GameObject("Turf Map");
            turf.AddComponent<TurfMapHud>().Init(this, _blocks, _picker, _rig, _crews,
                _clock, _vehicles, _policeCars);
        }

        // ------------------------------------------------------- the lot overlay
        //
        // The O key's answer to "what is this block, and what was it built for":
        // the plan BuildBlocks just worked from, printed over the lots themselves.
        void BuildLotOverlay()
        {
            var go = new GameObject("Lot Overlay");
            go.AddComponent<DemoLotOverlay>().Init(this);
        }
    }
}
