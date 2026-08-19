"""Offline geometry check for the island, the shore, the river crossings and the
belt freeway's slip roads.

Everything here is read out of the C# so the numbers cannot drift apart, and every
check is one thing that would be visibly wrong in Play - a wall of ground going
down into the sea, a road tearing against the grass it lies on, a freeway you
cannot get onto, a river dammed by the road that crosses it.

    python Docs/island.py [-v]
"""
import re, sys, os, math

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def read(rel):
    with open(os.path.join(ROOT, rel), encoding='utf-8') as f:
        return f.read()


ISLAND = read('Assets/RoadDemo/RoadDemoBuilder.Island.cs')
BELT = read('Assets/RoadDemo/RoadDemoBuilder.Belt.cs')
SEAMS = read('Assets/RoadDemo/RoadDemoBuilder.Seams.cs')
LAYOUT = read('Assets/RoadDemo/CityLayout.cs')
KIT = read('Assets/RoadDemo/StreetKit.cs')
HARBOR = read('Assets/HarborDemo/HarborDistrict.cs')
LANES = read('Assets/RoadDemo/LaneNet.cs')


def num(src, name):
    m = re.search(r'\b' + name + r'\s*=\s*(-?[0-9.]+)f', src)
    if not m:
        raise KeyError(name)
    return float(m.group(1))


# ---------------------------------------------------------------- the numbers
ISLAND_W = num(ISLAND, 'islandWest')
ISLAND_E = num(ISLAND, 'islandEast')
ISLAND_N = num(ISLAND, 'islandNorth')
ISLAND_S = num(ISLAND, 'islandSouth')
WANDER = num(ISLAND, 'coastWander')
HILL = num(ISLAND, 'hillHeight')
GROUND_STEP = num(ISLAND, 'GroundStep')
TILE_SPAN = num(ISLAND, 'GroundTileSpan')
SEABED = num(ISLAND, 'SeabedY')
SEA_MARGIN = num(ISLAND, 'SeaMargin')
SHORE_BLEND = num(ISLAND, 'ShoreBlend')
BASIN_FADE = num(ISLAND, 'BasinFade')
ROAD_BED = num(ISLAND, 'RoadBed')
FLAT_BLEND = num(ISLAND, 'FlatBlend')
NEAR_WILD, FAR_WILD, FAR_DENS = (float(x) for x in re.search(
    r'NearWild\s*=\s*([0-9.]+)f,\s*FarWild\s*=\s*([0-9.]+)f,\s*FarWildDensity\s*=\s*([0-9.]+)f',
    ISLAND).groups())

BELT_OUT = num(BELT, 'BeltOut')
BELT_HALF = num(BELT, 'BeltHalf')
BELT_PAD = num(BELT, 'BeltPadHalf')
BELT_CORNER = num(BELT, 'BeltCornerHalf')
GORE_OUT = num(BELT, 'BeltGoreOut')
FOOT_OUT = num(BELT, 'BeltFootOut')
GORE_HALF, FOOT_HALF = (float(x) for x in re.search(
    r'BeltGoreHalf\s*=\s*([0-9.]+)f,\s*BeltFootHalf\s*=\s*([0-9.]+)f', BELT).groups())
OUTER_LANE = num(BELT, 'BeltOuterLane')
STREET_LANE = num(BELT, 'BeltStreetLane')
SLIP_TAIL = float(re.search(
    r'SlipStreetRoom\s*=\s*BeltFootOut \+ BeltFootHalf \+ ([0-9.]+)f', BELT).group(1))
SLIP_ROOM = FOOT_OUT + FOOT_HALF + SLIP_TAIL
BELT_LANES = [float(x) for x in re.findall(
    r'[0-9.]+', re.search(r'BeltLanes\s*=\s*\{([^}]*)\}', BELT).group(1))]

RIVER_BANK = num(SEAMS, 'RiverBank')
RIVER_CLEAR = num(SEAMS, 'RiverClear')
GRADE_Y = num(SEAMS, 'GradeY')

STREET_HALF = num(KIT, 'ParkLane') + num(KIT, 'RoadHalf')
KERB_HEIGHT = 0.1          # the sidewalk tile stands this proud of the road

RIVER_GAP = num(LAYOUT, 'RiverGap')
STRIPS = [float(x) for x in re.findall(r'strip = ([0-9.]+)f \+ rng\.Next', LAYOUT)]

BASIN_REACH = num(HARBOR, 'BasinReach')
STREET_Z = num(HARBOR, 'PlannedStreetZ')
OPEN_SEA = float(re.search(r'OpenSeaReach\s*=\s*([0-9.]+)f', HARBOR).group(1))

# LaneNet drops a movement whose heading changes by more than this
TURN_DOT = float(re.search(r'Vector3\.Dot\(a\.Dir, b\.Dir\) < (-[0-9.]+)f', LANES).group(1))

# the grid the demo ships with, for the one check that needs a real river
RIVER_WIDTH = 90.0         # RoadDemoBuilder.seams, the default river

fails, notes = [], []


def check(ok, what, detail=''):
    line = ('ok    ' if ok else 'FAIL  ') + what + (('  - ' + detail) if detail else '')
    (notes if ok else fails).append(line)


# ------------------------------------------------------- the shore, not a wall
#
# The port's basin is a rectangle. Taken as in-or-out it is a pit with vertical
# walls; eased over ShoreBlend it is a bay. What matters is the STEEPEST ground
# the blend can produce, and that is the land beside the basin dropping over the
# blend. Round a basin the hills are held down (BasinFade), so the land there is
# the plain - a metre or two.
PLAIN = 1.5
check((PLAIN - SEABED) / SHORE_BLEND < 0.25,
      'the shore off a basin is a beach, not a wall',
      '%.1f m over %.0f m, 1 in %.0f' % (PLAIN - SEABED, SHORE_BLEND, SHORE_BLEND / (PLAIN - SEABED)))
check(BASIN_FADE > SHORE_BLEND,
      'the hills stand back further than the beach is wide',
      'fade %.0f m vs blend %.0f m' % (BASIN_FADE, SHORE_BLEND))
check((HILL - SEABED) / SHORE_BLEND > 0.25,
      'the hill fade is what keeps that coast walkable (unfaded it would be a cliff)',
      '%.2f m of rise per metre unfaded' % ((HILL - SEABED) / SHORE_BLEND))

inset = re.search(r'InPaved\(cx, cz, GroundStep \* ([0-9.]+)f\)', ISLAND)
check(inset is not None and abs(float(inset.group(1)) - 0.5) < 1e-6,
      'a paved cell is dropped on its corners, not its centre - no holes round a quarter')

# ------------------------------------------------------------ the road bed
check(ROAD_BED < 0,
      'the ground under a road is held BELOW the asphalt, so the two do not tear',
      '%.0f cm' % (ROAD_BED * 100))
check(abs(ROAD_BED) < KERB_HEIGHT,
      'and not so far below that the kerb tile hangs in the air',
      '%.0f cm against a %.0f cm kerb' % (abs(ROAD_BED) * 100, KERB_HEIGHT * 100))
check(abs(ROAD_BED) > 0.02,
      'and far enough below to part the two planes in the depth buffer')
check(GRADE_Y > 0, 'the freeway decks still stand over their own ground')

# ------------------------------------------------------- rivers and bridges
to_middle = RIVER_WIDTH * 0.5 + RIVER_BANK + RIVER_CLEAR
check(to_middle > FLAT_BLEND,
      "flat ground held for the belt cannot reach the middle of the channel it bridges",
      '%.0f m to the nearest flat rectangle, blend %.0f m' % (to_middle, FLAT_BLEND))
check(RIVER_CLEAR > RIVER_BANK * 0.5,
      'the flat ground stops well clear of the bank the island carves')
check(RIVER_GAP > 0 and 'grid.Rivers' in LAYOUT,
      'the roll keeps a quarter off the river on its way out of town',
      '%.0f m of clear shore' % RIVER_GAP)
check('BeltRiverBridge' in BELT and 'ClearOfRivers' in BELT,
      'the belt bridges a channel instead of damming it')

# ------------------------------------------------------- the belt's slips
gore = GORE_OUT - GORE_HALF          # the gore's box edge, toward the crossroads
foot = FOOT_OUT - FOOT_HALF          # the foot's box edge, toward the belt
run_along = gore - STREET_LANE
run_across = foot - OUTER_LANE
theta = math.degrees(math.atan2(run_across, run_along))
# The exit and the entrance of one quadrant point across each other: the dot of
# their directions is -cos(2 theta). LaneNet keeps a movement only while the dot
# is at least TURN_DOT, so under thirty degrees the pair is dropped - which is
# what stops a car leaving the belt and turning straight back onto it.
pair_dot = -math.cos(math.radians(2 * theta))
check(pair_dot < TURN_DOT,
      'a slip lies flat enough on the belt that its exit and entrance are not a U-turn',
      'ramp at %.1f deg, pair dot %.2f (dropped under %.2f)' % (theta, pair_dot, TURN_DOT))
check(theta > 10, 'and steep enough to actually reach the street', '%.1f deg' % theta)
check(gore > BELT_PAD + 5, 'the gore stands clear of the crossroads box',
      '%.0f m out, pad %.0f m' % (gore, BELT_PAD))
check(foot > BELT_PAD + 5, "the foot stands clear of the belt's pad",
      '%.0f m out, pad %.0f m' % (foot, BELT_PAD))
check(OUTER_LANE == max(BELT_LANES), "a slip leaves the belt's OUTER lane",
      '%.1f of %s' % (OUTER_LANE, BELT_LANES))
check(OUTER_LANE < BELT_HALF, 'and that lane is on the carriageway')
check(STREET_LANE < STREET_HALF, "the slip lands in the crossing street's own lane")

want_belt = GORE_OUT + GORE_HALF + 8
check(BELT_CORNER + want_belt < BELT_OUT,
      'the belt has room between a corner and the grid for a crossing with slips',
      '%.0f m wanted, %.0f m of belt' % (want_belt, BELT_OUT))
for s in STRIPS:
    check(s >= BELT_OUT + SLIP_ROOM,
          'a quarter rolled at its shortest strip (%.0f m) still clears the slips' % s,
          '%.0f m wanted' % (BELT_OUT + SLIP_ROOM))

# ------------------------------------------------------------ the island
for name, w in (('west', ISLAND_W), ('east', ISLAND_E), ('north', ISLAND_N), ('south', ISLAND_S)):
    check(w > 900, 'the %s shore is a drive out of town, not a verge' % name, '%.0f m' % w)
    check(w > 2 * WANDER, 'the %s coast cannot wander back into the city' % name)
check('OpenBasinsToSea' in ISLAND,
      'the island opens a basin out past its own coast (the port cannot know where that is)')
widest = max(ISLAND_W, ISLAND_E, ISLAND_N, ISLAND_S) + 1.2 * WANDER
check(BASIN_REACH + STREET_Z + OPEN_SEA < widest,
      "the port's own reach is short of this island's coast - which is what the island tops up",
      '%.0f m reserved, coast up to %.0f m out' % (BASIN_REACH + STREET_Z + OPEN_SEA, widest))

per_tile = (TILE_SPAN / GROUND_STEP + 1) ** 2
check(per_tile < 65535, 'a ground tile fits a 16-bit index buffer', '%.0f vertices' % per_tile)
check(TILE_SPAN % GROUND_STEP == 0, 'a tile is a whole number of ground steps')
check(TILE_SPAN >= 120, 'a tile is at least one merge cell across, so they do not all merge into one')

check(FAR_DENS < 0.4 and FAR_WILD > NEAR_WILD, 'the wood thins with the drive out',
      '%.0f%% of the near density past %.0f m' % (FAR_DENS * 100, FAR_WILD))

# --------------------------------------------------------------- the report
if '-v' in sys.argv:
    for line in notes:
        print(line)
for line in fails:
    print(line)
print('\n%d checks passed, %d failed' % (len(notes), len(fails)))
sys.exit(1 if fails else 0)
