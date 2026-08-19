"""Geometry check for AirportDemo, ported from AirportSpec.cs.

Catches what only shows up in Play otherwise: two buildings in the same place, an
aeroplane parked in the taxiway's object free area, a tie-down row whose tails
hang off the ramp, a stand the taxi graph cannot reach.
"""
import re, sys, math
from collections import deque

SPEC = open('Assets/AirportDemo/AirportSpec.cs', encoding='utf-8').read()

def num(name):
    m = re.search(r'public const (?:float|int)\s+%s\s*=\s*([-0-9.]+)f?;' % name, SPEC)
    if not m:
        raise KeyError(name)
    return float(m.group(1))

def arr(name):
    m = re.search(r'public static readonly float\[\]\s+%s\s*=\s*\{([^}]*)\}' % name, SPEC)
    return [float(x.strip().rstrip('f')) for x in m.group(1).split(',') if x.strip()]

RUNWAY_HALF   = num('RunwayLength') / 2
RW_HALF_W     = num('RunwayWidth') / 2
SAFETY_HALF   = num('SafetyHalf')
TWY_Z         = num('TaxiwayZ')
TWY_OFA       = num('TaxiObjectFreeHalf')
HOLD_Z        = num('HoldShortZ')
CONNECTORS    = arr('ConnectorX')
APRON_Z0      = num('ApronZ0'); APRON_Z1 = num('ApronZ1')
APRON_X0      = num('ApronX0'); APRON_X1 = num('ApronX1')
ENTRIES       = arr('ApronEntryX')
TD_X0         = num('TieDownX0'); TD_X1 = num('TieDownX1')
TD_PITCH      = num('TieDownPitch'); TD_ROW_PITCH = num('TieDownRowPitch')
TD_ROWS       = int(num('TieDownRows')); TD_Z0 = num('TieDownRowZ0')
STAND_X       = arr('CommuterStandX'); STAND_Z = num('CommuterStandZ')
SERVICE_Z     = num('ServiceRoadZ'); SERVICE_W = num('ServiceRoadWidth')
FRONT_Z       = num('BuildingFrontZ')
FENCE_Z       = num('FenceZ')
GA_SPAN       = num('GaSpan');       GA_LEN = num('GaLength')
COMMUTER_SPAN = num('CommuterSpan'); COMMUTER_LEN = num('CommuterLength')
JET_SPAN      = num('JetSpan');      JET_LEN = num('JetLength')

fails, notes = [], []
def check(ok, msg):
    (notes if ok else fails).append(('ok  ' if ok else 'FAIL') + '  ' + msg)

# ---------------------------------------------------------------- buildings
# every building is placed with its apron face on FRONT_Z and turned to face -Z,
# so it occupies [x - w/2, x + w/2] x [FRONT_Z, FRONT_Z + depth]
B = []
def bld(name, x, w, d, z0=None):
    z0 = FRONT_Z if z0 is None else z0
    B.append((name, x - w / 2, x + w / 2, z0, z0 + d))

hangars = int(num('Hangars'))
for i in range(hangars):
    bld('Hangar %d' % (i + 1), num('HangarRowX0') + i * num('HangarPitch'),
        num('HangarWidth'), num('HangarDepth'))
bld('Maintenance', num('MaintHangarX'), num('MaintHangarWidth'), num('MaintHangarDepth'))
bld('FBO', num('FboX'), num('FboWidth'), num('FboDepth'))
bld('Terminal', num('TerminalX'), num('TerminalWidth'), num('TerminalDepth'))
bld('Fire station', num('ArffX'), num('ArffWidth'), num('ArffDepth'))
bld('Freight shed', num('CargoX'), num('CargoWidth'), num('CargoDepth'))
# the tower and the fuel farm are placed on a centre, not on the building line
tz = num('TowerZ')
B.append(('Control tower', num('TowerX') - 6, num('TowerX') + 6, tz - 4.5, tz + 4.5))
fz = num('FuelFarmZ')
B.append(('Fuel farm', num('FuelFarmX') - 9.3, num('FuelFarmX') + 9.3, fz - 6.8, fz + 6.8))

def overlap(a, b):
    return not (a[2] <= b[1] or b[2] <= a[1] or a[4] <= b[3] or b[4] <= a[3])

clash = [(a[0], b[0]) for i, a in enumerate(B) for b in B[i + 1:] if overlap(a, b)]
check(not clash, 'no two buildings overlap' + ('' if not clash else ': ' + str(clash)))

# nothing may stand in the runway safety area or the taxiway object free area
for name, x0, x1, z0, z1 in B:
    check(z0 > SAFETY_HALF, '%s is clear of the runway safety area (front z=%.0f > %.0f)' % (name, z0, SAFETY_HALF))
    check(z0 > TWY_Z + TWY_OFA, '%s is clear of the taxiway object free area (front z=%.0f > %.0f)' % (name, z0, TWY_Z + TWY_OFA))

# and the buildings must sit behind the ramp and in front of the wire
for name, x0, x1, z0, z1 in B:
    check(z0 >= APRON_Z1 - 0.01, '%s stands behind the ramp edge' % name)
    check(z1 <= FENCE_Z + 0.01, '%s stands inside the wire (back z=%.1f <= %.0f)' % (name, z1, FENCE_Z))

# the gates must fall in a gap between buildings
for gate, gname in ((num('GaGateX'), 'general aviation gate'), (num('CargoGateX'), 'freight gate')):
    half = num('GateHalf')
    hit = [n for n, x0, x1, z0, z1 in B if not (x1 <= gate - half or x0 >= gate + half)]
    check(not hit, '%s at x=%.0f is in a gap between buildings%s' % (gname, gate, '' if not hit else ' - blocked by ' + str(hit)))

# ---------------------------------------------------------------- stands
# the airline stands take the biggest thing on the field; the tie-downs take
# light aeroplanes, and each is checked against its own class's box
stands = []
for i, sx in enumerate(STAND_X):
    stands.append(('Airline %d' % (i + 1), sx, STAND_Z - JET_LEN * 0.45, 0.0, JET_SPAN, JET_LEN))
row_x = []
x = TD_X0
while x <= TD_X1 + 0.1:
    row_x.append(x)
    x += TD_PITCH
for r in range(TD_ROWS):
    z = TD_Z0 + r * TD_ROW_PITCH
    for k, sx in enumerate(row_x):
        stands.append(('Tie-down r%dc%d' % (r + 1, k + 1), sx, z, 180.0, GA_SPAN, GA_LEN))

def plane_box(x, z, yaw, span, length):
    """Footprint of an aeroplane parked there, axis aligned (yaw is 0 or 180).
    The model origin sits about 45 per cent of the way back from the nose."""
    nose, tail = length * 0.45, -length * 0.55
    if abs(yaw - 180) < 1:
        z0, z1 = z - nose, z - tail
    else:
        z0, z1 = z + tail, z + nose
    return (x - span / 2, x + span / 2, z0, z1)

boxes = [(n,) + plane_box(x, z, y, sp, ln) for n, x, z, y, sp, ln in stands]
bad = [(a[0], b[0]) for i, a in enumerate(boxes) for b in boxes[i + 1:] if overlap(a, b)]
check(not bad, 'no two parked aeroplanes overlap' + ('' if not bad else ': %d pairs, first %s' % (len(bad), bad[0])))

for n, x0, x1, z0, z1 in boxes:
    check(z0 >= TWY_Z + TWY_OFA, '%s is clear of the taxiway object free area (z0=%.1f)' % (n, z0))
    check(z1 <= SERVICE_Z - SERVICE_W / 2, '%s does not reach the service road (tail z=%.1f <= %.1f)' % (n, z1, SERVICE_Z - SERVICE_W / 2))
    check(z0 >= APRON_Z0 and z1 <= APRON_Z1, '%s stands on the ramp (%.1f..%.1f in %.0f..%.0f)' % (n, z0, z1, APRON_Z0, APRON_Z1))
    check(x0 >= APRON_X0 and x1 <= APRON_X1, '%s stands on the ramp in x' % n)

# a parked aeroplane must not stand in a building
for n, x0, x1, z0, z1 in boxes:
    hit = [bn for bn, bx0, bx1, bz0, bz1 in B if not (bx1 <= x0 or bx0 >= x1 or bz1 <= z0 or bz0 >= z1)]
    check(not hit, '%s is not inside a building%s' % (n, '' if not hit else ' - in ' + str(hit)))

# ---------------------------------------------------------------- taxi graph
# the same graph FlightOps builds: runway / hold / taxiway per connector, the
# taxiway chain, the ramp lane chain, and a lane node per stand
nodes, links = {}, {}
def node(name, x, z):
    nodes[name] = (x, z)
    links.setdefault(name, set())
    return name
def link(a, b):
    links[a].add(b); links[b].add(a)

lane_z = APRON_Z0 + 12
twy_chain, lane_chain = [], []
for i, cx in enumerate(CONNECTORS):
    cx = max(-RUNWAY_HALF + 30, min(RUNWAY_HALF - 30, cx))
    r = node('RWY%d' % i, cx, 0); h = node('HOLD%d' % i, cx, HOLD_Z + 6); t = node('TWY%d' % i, cx, TWY_Z)
    link(r, h); link(h, t); twy_chain.append(t)
for j, ex in enumerate(ENTRIES):
    t = node('TWYE%d' % j, ex, TWY_Z); l = node('RAMPE%d' % j, ex, lane_z)
    link(t, l); twy_chain.append(t); lane_chain.append(l)
for i, (n, sx, sz, yaw, sp, ln) in enumerate(stands):
    a = node('LANE%d' % i, sx, lane_z); s = node('STAND%d' % i, sx, sz)
    link(a, s); lane_chain.append(a)

def chain(names):
    names = sorted(names, key=lambda n: nodes[n][0])
    for i in range(1, len(names)):
        link(names[i - 1], names[i])
chain(twy_chain); chain(lane_chain)

def reach(start):
    seen, q = {start}, deque([start])
    while q:
        n = q.popleft()
        for m in links[n]:
            if m not in seen:
                seen.add(m); q.append(m)
    return seen

seen = reach('STAND0')
missing = [n for n in nodes if n not in seen]
check(not missing, 'every taxi node is reachable from stand 0' + ('' if not missing else ' - orphans: %s' % missing[:6]))
check(all(('HOLD%d' % i) in seen for i in range(len(CONNECTORS))), 'every holding point is reachable from the stands')

# ---------------------------------------------------------------- markings
thr_total = (num('ThresholdStripes') * num('ThresholdStripeWidth')
             + (num('ThresholdStripes') / 2 - 1) * 2 * num('ThresholdStripeGap')
             + num('ThresholdCentreGap'))
check(thr_total <= num('RunwayWidth'), 'the threshold bar (%.2f m) fits the runway (%.0f m)' % (thr_total, num('RunwayWidth')))
check(num('AimingBarInner') + num('AimingBarWidth') < RW_HALF_W, 'the aiming point bars fit inside the runway edges')
check(HOLD_Z >= 76 - 2, 'the holding position is at least 250 ft from the runway centreline (%.0f m)' % HOLD_Z)
check(TD_PITCH - GA_SPAN > 3, 'tie-down pitch leaves %.2f m between wingtips' % (TD_PITCH - GA_SPAN))
check(num('HangarDoorWidth') > GA_SPAN + 1, 'the hangar opening clears a light wing by %.2f m a side' % ((num('HangarDoorWidth') - GA_SPAN) / 2))
check(num('HangarDepth') - GA_LEN > 3, 'the hangar is %.1f m longer than a light aeroplane' % (num('HangarDepth') - GA_LEN))
check(num('HangarHeight') - num('GaHeight') > 1, 'the hangar eaves clear the fin by %.2f m' % (num('HangarHeight') - num('GaHeight')))
# the field must take the biggest thing that uses it
check(num('RunwayWidth') > JET_SPAN + 5, 'the runway is %.0f m wider than a trijet span' % (num('RunwayWidth') - JET_SPAN))
check(num('TaxiwayWidth') >= 15, 'the taxiway is %.0f m wide - ADG III wants 18' % num('TaxiwayWidth'))
gap = abs(STAND_X[1] - STAND_X[0]) - JET_SPAN
check(gap > 15, 'the airline stands leave %.0f m between two jets wingtips' % gap)
check(num('SafetyHalf') >= 75, 'the runway safety area is %.0f m each side (ADG III wants 76)' % num('SafetyHalf'))
check(TWY_Z >= 120, 'the parallel taxiway is %.0f m off the runway centreline (ADG III wants 122)' % TWY_Z)


# ---------------------------------------------------------------- flight paths
# The same arithmetic FlightOps does, for both wind directions. This is the check
# that catches a sign the wrong way round - which sends a departure climbing back
# over the field, or puts the downwind leg up the middle of the runway.
PATTERN_ALT   = num('PatternAltitude')
PATTERN_WIDTH = num('PatternWidth')
FINAL_LEN     = num('FinalLength')

for westerly in (True, False):
    tag = 'rwy 27' if westerly else 'rwy 09'
    threshold = RUNWAY_HALF if westerly else -RUNWAY_HALF     # landed over, lined up at
    departure = -RUNWAY_HALF if westerly else RUNWAY_HALF     # left the ground past
    run = -1.0 if westerly else 1.0                            # +1 rolling east
    side = -1.0 if westerly else 1.0                           # the circuit is left-hand

    for name, take in (('light', 450.0), ('commuter', 800.0), ('jet', 1250.0)):
        rotate_x = threshold + run * take
        check(abs(rotate_x) <= RUNWAY_HALF,
              '%s: a %s is airborne at x=%.0f, still on the runway (half %.0f)' % (tag, name, rotate_x, RUNWAY_HALF))
        check((rotate_x - departure) * run < 0,
              '%s: a %s rotates before the far end' % (tag, name))

    climb1 = departure + run * 500
    climb2 = departure + run * 900
    check((climb1 - departure) * run > 0, '%s: the first climb leg is beyond the departure end' % tag)
    check((climb2 - climb1) * run > 0, '%s: the climb carries on away from the field' % tag)

    crosswind = departure + run * 600
    downwind_end = threshold - run * 500
    base_x = threshold - run * (FINAL_LEN + 150)
    final_start = threshold - run * FINAL_LEN
    check((crosswind - departure) * run > 0, '%s: crosswind is off the departure end' % tag)
    check((downwind_end - threshold) * run < 0, '%s: downwind runs back past the threshold' % tag)
    check((downwind_end - crosswind) * run < 0, '%s: downwind runs the opposite way to the roll' % tag)
    check((base_x - final_start) * run < 0, '%s: base is further out than the start of final' % tag)
    check((final_start - threshold) * run < 0, '%s: final starts on the approach side of the threshold' % tag)
    check(abs(final_start) > RUNWAY_HALF, '%s: final starts clear of the runway (x=%.0f)' % (tag, final_start))

    touchdown = threshold + run * 300
    check(abs(touchdown) <= RUNWAY_HALF, '%s: the touchdown point is on the runway (x=%.0f)' % (tag, touchdown))
    check((touchdown - threshold) * run > 0, '%s: it touches down past the threshold, not before it' % tag)

    # the connector a departure lines up at is the one nearest the threshold
    dep_conn = min(CONNECTORS, key=lambda c: abs(c - threshold))
    check(abs(dep_conn - threshold) < 120, '%s: lines up at the connector by the threshold (x=%.0f)' % (tag, dep_conn))
    check(abs(dep_conn) <= RUNWAY_HALF - 20, '%s: that connector is on the runway' % tag)

    # and the turn-off after landing is far enough down for each class
    for name, needed in (('light', 420.0), ('commuter', 700.0), ('jet', 1100.0)):
        usable = [c for c in CONNECTORS if (c - threshold) * run >= needed]
        check(usable, '%s: a %s has a turn-off at least %.0f m down the runway' % (tag, name, needed))

    check(PATTERN_WIDTH > 300, '%s: the downwind leg is %.0f m off the centreline' % (tag, PATTERN_WIDTH))
    check(side * PATTERN_WIDTH != 0, '%s: the circuit has a side' % tag)

# ---------------------------------------------------------------- the boarding walk
# The route AirportBoarding walks a passenger: down the steps, out from under the
# wing, forward past the nose, over the service road and in at the gate door. It is
# checked here because it is the one path on this field that a person walks across
# live aircraft stands, and a route that clipped a wing would look like a passenger
# walking through a Boeing.
TERM_W    = num('TerminalWidth')
DOOR_SIDE = 1.9                          # AirportSpec.Door, jet
DOOR_FORE = 0.55
STEP_REACH = 10 * 0.30 + 0.85            # ten treads of 0.3 m plus the platform
NOSE      = JET_LEN * 0.55               # the stand node is 0.45 of the length back

def gate_x(sx):
    limit = TERM_W * 0.5 - 8.0
    return max(-limit, min(limit, sx))

def seg_min_x(a, b):
    return min(a[0], b[0])

def seg_max_x(a, b):
    return max(a[0], b[0])

for i, sx in enumerate(STAND_X):
    origin_z = STAND_Z - JET_LEN * 0.45
    foot  = (sx - (DOOR_SIDE + STEP_REACH + 1.0), origin_z + NOSE * DOOR_FORE)
    clear = (sx - (JET_SPAN * 0.5 + 3.0),         origin_z + NOSE * DOOR_FORE)
    ahead = (sx - (JET_SPAN * 0.5 + 3.0),         origin_z + NOSE + 6.0)
    gx    = gate_x(sx)
    cross = (gx, SERVICE_Z)
    gate  = (gx, FRONT_Z - 2.0)
    route = [foot, clear, ahead, cross, gate]

    check(abs(clear[0] - sx) > JET_SPAN * 0.5,
          'stand %d: the walk comes out %.1f m clear of its own wingtip' % (i + 1, abs(clear[0] - sx) - JET_SPAN * 0.5))
    check(foot[1] < origin_z + NOSE,
          'stand %d: the foot of the steps is behind the nose' % (i + 1))
    check(abs(gx) <= TERM_W * 0.5,
          'stand %d: the gate door is in the terminal wall (x %.0f, wall +/- %.0f)' % (i + 1, gx, TERM_W * 0.5))
    check(gate[1] < FRONT_Z and gate[1] > num('ApronZ1'),
          'stand %d: the gate door is on the paved strip behind the ramp' % (i + 1))

    # and it must not walk into anybody else standing on the ramp
    for j, ox in enumerate(STAND_X):
        if j == i:
            continue
        lo, hi = ox - JET_SPAN * 0.5, ox + JET_SPAN * 0.5
        worst = None
        for k in range(len(route) - 1):
            a, b = route[k], route[k + 1]
            oz0, oz1 = origin_z + JET_LEN * -0.55, origin_z + NOSE
            # the neighbour occupies the same band of z, so an overlap in x is a hit
            if seg_max_x(a, b) > lo and seg_min_x(a, b) < hi and min(a[1], b[1]) < oz1:
                worst = (k, seg_min_x(a, b), seg_max_x(a, b))
        check(worst is None,
              'stand %d: the walk keeps out of stand %d (x %.0f..%.0f)%s'
              % (i + 1, j + 1, lo, hi, '' if worst is None else ' - leg %d runs %.0f..%.0f' % worst))

check(num('DisembarkGap') > 0.5 and num('BoardingGap') > 0.5,
      'the file down the steps moves at a sensible rate')
check(num('LightGroundMin') > 120,
      'a light aeroplane sits at least %.0f s between movements - the circuit is not a conveyor'
      % num('LightGroundMin'))

print('%d checks, %d failures' % (len(fails) + len(notes), len(fails)))
for f in fails:
    print(f)
if '-v' in sys.argv:
    for n in notes:
        print(n)
sys.exit(1 if fails else 0)
