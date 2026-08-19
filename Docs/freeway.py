"""Offline geometry check for the elevated freeway's corridor: the frontage roads,
the diamond interchange and the decks.

Everything here is read out of the C# so the numbers cannot drift apart, and every
check is one thing that would be visibly wrong in Play - two carriageways in the
same place, a junction box overlapping the next, a ramp climbing like a wall, a
service road that does not fit between the deck and the bounding street.

    python Docs/freeway.py [-v]
"""
import re, sys, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def read(rel):
    with open(os.path.join(ROOT, rel), encoding='utf-8') as f:
        return f.read()

INTER = read('Assets/RoadDemo/RoadDemoBuilder.Interchange.cs')
SEAMS = read('Assets/RoadDemo/RoadDemoBuilder.Seams.cs')
BUILD = read('Assets/RoadDemo/RoadDemoBuilder.cs')
BELT  = read('Assets/RoadDemo/RoadDemoBuilder.Belt.cs')
KIT   = read('Assets/RoadDemo/StreetKit.cs')

def num(src, name):
    m = re.search(r'\b' + name + r'\s*=\s*(-?[0-9.]+)f', src)
    if not m:
        raise KeyError(name)
    return float(m.group(1))

# ---------------------------------------------------------------- the numbers
FRONTAGE_OFF = num(INTER, 'FrontageOff')
UNDER_HALF   = num(INTER, 'UnderHalf')
DECK_OFF     = num(INTER, 'DeckOff')
RAMP_FOOT_OFF, RAMP_GORE_OFF = (float(x) for x in
    re.search(r'RampFootOff\s*=\s*([0-9.]+)f,\s*RampGoreOff\s*=\s*([0-9.]+)f', INTER).groups())
RAMP_RUN     = num(INTER, 'RampRun')
RAMP_FOOT    = num(INTER, 'RampFoot')

ROAD_HALF    = num(KIT, 'RoadHalf')          # StreetKit.RoadHalf - half a frontage road
FRONTAGE_HALF = ROAD_HALF

PARK_LANE    = num(BUILD, 'ParkLane')
STREET_HALF  = 5.0 + PARK_LANE
BLVD_HALF    = 15.0 + PARK_LANE
SIDEWALK     = 6.5                            # SidewalkDressing.Width
CELL         = num(BUILD, 'Cell')

DECK_Y       = num(SEAMS, 'DeckY')
GRADE_Y      = num(SEAMS, 'GradeY')
DECK_HALF    = 5.7                            # half one deck piece: 11.4 m across
BELT_OUT     = num(BELT, 'BeltOut')
BELT_HALF    = num(BELT, 'BeltHalf')

SEAM_W = float(re.search(r'kind\s*=\s*SeamKind\.Highway,\s*width\s*=\s*([0-9.]+)f', BUILD).group(1))
HALF_W = SEAM_W / 2.0

fails, notes = [], []
def check(ok, msg):
    (notes if ok else fails).append(('ok    ' if ok else 'FAIL  ') + msg)

def band(centre, half):
    return (centre - half, centre + half)

def overlap(a, b, slack=0.0):
    return min(a[1], b[1]) - max(a[0], b[0]) > slack

# ---------------------------------------------------------------- across the corridor
deck     = band(0.0, DECK_HALF * 2)           # the twin deck, both carriageways
wire     = band(0.0, UNDER_HALF)              # the fenced waste ground under it
ramp_gore = band(RAMP_GORE_OFF, DECK_HALF)    # a ramp's deck where it meets the freeway
ramp_foot = band(RAMP_FOOT_OFF, DECK_HALF)    # and where it meets the cross street
frontage = band(FRONTAGE_OFF, FRONTAGE_HALF)

check(deck[1] <= UNDER_HALF,
      'the twin deck (%.1f m each side) stands inside the fenced ground (%.0f m)' % (deck[1], UNDER_HALF))
check(not overlap(ramp_gore, deck, 0.05),
      'a ramp at its gore (%.1f..%.1f) runs ALONGSIDE the freeway deck (%.1f..%.1f), not through it'
      % (ramp_gore[0], ramp_gore[1], deck[0], deck[1]))
check(not overlap(ramp_foot, frontage, 0.05),
      'a ramp at its foot (%.1f..%.1f) is clear of the frontage road (%.1f..%.1f)'
      % (ramp_foot[0], ramp_foot[1], frontage[0], frontage[1]))
check(not overlap(ramp_foot, deck, 0.05),
      'a ramp at its foot is clear of the deck overhead')
check(frontage[1] <= HALF_W + 0.05,
      'the frontage road (out to %.1f m) fits the corridor (%.0f m half width)' % (frontage[1], HALF_W))
check(ramp_foot[0] > wire[1] - 0.05 or ramp_foot[0] >= UNDER_HALF,
      'the wire (%.0f m) stands between the waste ground and the ramps (from %.1f m)' % (UNDER_HALF, ramp_foot[0]))

# ---------------------------------------------------------------- junction boxes
# The cross street through the corridor runs: grid junction, frontage crossroads,
# ramp terminal, ramp terminal, frontage crossroads, grid junction. No two of those
# boxes may touch, and the first hop off the grid has to be long enough to hold a car.
for name, half in (('street', STREET_HALF), ('boulevard', BLVD_HALF)):
    grid_edge = HALF_W + SIDEWALK              # the grid junction's box edge, from the corridor's centre
    boxes = [
        ('frontage', band(FRONTAGE_OFF, FRONTAGE_HALF)),
        ('ramp foot', band(RAMP_FOOT_OFF, FRONTAGE_HALF)),
    ]
    for i in range(len(boxes) - 1):
        a, b = boxes[i], boxes[i + 1]
        gap = a[1][0] - b[1][1]
        check(gap > 4.0, '%s: %.1f m between the %s box and the %s box'
              % (name, gap, b[0], a[0]))
    hop = grid_edge - frontage[1]
    check(hop > 10.0,
          '%s: %.1f m of carriageway between the grid junction and the frontage crossroads' % (name, hop))
    # the two ramp terminals face each other across the deck
    inner = RAMP_FOOT_OFF - FRONTAGE_HALF
    check(inner * 2 > 20.0,
          '%s: %.0f m between the two ramp terminals, under the deck' % (name, inner * 2))

# ---------------------------------------------------------------- the ramps
rise = DECK_Y - GRADE_Y
for half in (STREET_HALF, BLVD_HALF):
    run = RAMP_RUN - max(RAMP_FOOT, half)
    grade = rise / run * 100.0
    check(3.0 < grade < 9.0,
          'the ramp climbs %.1f m in %.0f m - %.1f%%, which is a ramp (3-9%%)' % (rise, run, grade))
    taper = RAMP_FOOT_OFF - RAMP_GORE_OFF
    check(taper / run < 0.25,
          'it tapers %.1f m across in %.0f m along (1 in %.0f)' % (taper, run, run / max(taper, 0.01)))

# ---------------------------------------------------------------- is there one at all
# PickInterchange, run on the demo's own default grid. If it finds nothing there are
# no ramps AT ALL and the whole diamond silently does not happen, which is the one
# failure of this feature that would look exactly like the bug it was meant to fix.
# (Respace shuffles the spacings at Play, so the line it lands on moves - what is
# checked here is that a line CAN be found and that its gores fall on level deck.)
def floats(name):
    m = re.search(r'public float\[\] ' + name + r'\s*=\s*\{([^}]*)\}', BUILD, re.S)
    return [float(x.strip().rstrip('f')) for x in m.group(1).split(',') if x.strip()]

def bools(name):
    m = re.search(r'public bool\[\] ' + name + r'\s*=\s*\{([^}]*)\}', BUILD, re.S)
    return [x.strip() == 'true' for x in m.group(1).split(',') if x.strip()]

HZ, HB = floats('horizontalRoadZ'), bools('horizontalIsBoulevard')
half_of = lambda k: BLVD_HALF if HB[k] else STREET_HALF
ext_lo = HZ[0] - half_of(0) - SIDEWALK
ext_hi = HZ[-1] + half_of(len(HZ) - 1) + SIDEWALK
middle = (ext_lo + ext_hi) / 2.0

pick, pick_blvd = None, False
for want_blvd in (True, False):
    best, bestd = None, 1e9
    for k, z in enumerate(HZ):
        if want_blvd and not HB[k]:
            continue
        if z - RAMP_RUN < ext_lo + 10 or z + RAMP_RUN > ext_hi - 10:
            continue
        if abs(z - middle) < bestd:
            bestd, best = abs(z - middle), z
    if best is not None:
        pick, pick_blvd = best, want_blvd
        break

check(pick is not None,
      'the corridor finds a cross street to hang its diamond off (grid %.0f..%.0f, ramps %.0f m long)'
      % (ext_lo, ext_hi, RAMP_RUN))
if pick is not None:
    check(pick_blvd, 'it is a BOULEVARD (%.0f) - what comes off a freeway needs a road that can take it' % pick)
    # the deck is at full height from ext.lo - 20 to ext.hi + 20 (BuildHighway)
    for gore in (pick - RAMP_RUN, pick + RAMP_RUN):
        check(ext_lo - 20 <= gore <= ext_hi + 20,
              'the gore at %.0f is on the level deck, not on the freeway run-down' % gore)

# ---------------------------------------------------------------- the belt
check(BELT_OUT > 100, 'the belt stands %.0f m out from the grid' % BELT_OUT)
check(BELT_HALF * 2 >= DECK_HALF * 4 - 0.1,
      'the belt is as wide as a twin deck (%.1f m)' % (BELT_HALF * 2))

# ---------------------------------------------------------------- the repairs
check('PaveFreewayJunction' in BELT and 'BuildBlockFloor' not in
      BELT.split('int BuildBeltSide')[1].split('void LayBeltDecks')[0],
      'no belt junction is laid with the LOT floor any more')
check('BuildBlockFloor(mid - 17.5f' not in SEAMS,
      'no freeway terminal pad is laid with the lot floor any more')
# The deck's own private traffic is now the FALLBACK, not the normal case: with a
# belt the decks are carriageways of the lane graph and the city's cars drive them.
check('if (!BeltOn && _carPrefabs.Count > 0)' in SEAMS,
      'the deck only keeps a private traffic when there is no belt for it to end on')
check(SEAMS.count('AddComponent<HighwayTraffic>') == 1,
      'and it is raised in one place only')
check('AddOneWay' in INTER, 'the decks are one-way carriageways, so a ramp can join one of them')
check('SurfaceAt' in INTER, 'the ramps carry a climbing surface')

print('%d checks, %d failures' % (len(fails) + len(notes), len(fails)))
for f in fails:
    print(f)
if '-v' in sys.argv:
    for n in notes:
        print(n)
sys.exit(1 if fails else 0)
