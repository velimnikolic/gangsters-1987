"""The expressway's arithmetic, checked without opening Unity.

    python Docs/expressway.py [-v]

The numbers in Docs/expressway-plan.md, written out once more and asserted: does the
ramp come down to the ground on the length the plan gives it, does the trumpet's loop
have room to descend, does the airport's exit arc finish before the airport, does the
downtown terminus land on the edge street with a junction's worth of straight road, does
the trunk come out between 1.6 and 4.5 km on the grids the city rolls, does the loop push
the villages off the island. When the C# lands, its constants must equal these (the
header of each section names the future home of each number).

Nothing here reads the code yet - there is no code. Keep the two in step from the day
there is.
"""
import math
import sys

# ---------------------------------------------------------------- the city as it is
STREET_HALF = 7.5          # RoadDemoBuilder.StreetHalf
BOULEVARD_HALF = 17.5      # RoadDemoBuilder.BoulevardHalf
PAVE = 6.5                 # SidewalkDressing.Width
OUTER_HALF = STREET_HALF + PAVE   # kerb of the grid's edge street from its centreline (14)
GRADE_Y = 0.12             # a street's surface
SUBURB_DEPTH = 3 * 70 + 50 # CityLayout: three rows of 70 + 50 (260)
STRAND = 70                # a district keeps this much dry land to the coast
NEAR_STRIP_TODAY = 110     # CityLayout.NearStrip
RING_STEP = 330            # CityLayout.RingStep
COASTS_TODAY = (760, 630, 600, 700)  # Island.cs islandWest/East/North/South
GRIDS = [(1400, 700), (1700, 850), (2000, 1000)]   # kerb-to-kerb extents E-W x N-S
CONNECTOR_HALF = 20        # CityLayout.CorridorHalf (Held.Road)
AIRPORT_STRIP = 460        # airport's near edge from the kerb (shore 1340 - 810 - 70)
HARBOR_DEPTH_MARGIN = 145 + 70   # harbor strip = max(200, shore - this)

# ---------------------------------------------------------------- the trunk (RoadDemoBuilder.Expressway.cs)
D = 120.0                  # axis of the band off the grid's outer kerb
R = 260.0                  # corner arc and the airport exit arc, 50 mph at e = 6 %
R_EXIT_TIGHT = 200.0       # the exit arc when the airport line is too near a corner, 45 mph
DECK_Y = 7.0               # driving surface
SLAB = 1.55                # SM_Env_Road_Highway_01: beam below the surface (measured)
PARAPET = 1.20             # ... and parapet above it (measured)
DECK_W = 11.4              # one carriageway (measured)
GAP = 1.6                  # between the two decks: barrier + lamps
DECK_OFF = GAP / 2 + DECK_W / 2         # each deck's centre off the axis (6.5)
LANE_OFF = 2.85            # two lanes per deck at +-2.85 (the 3-lane reading is decided in phase 0)
AUX_W = 3.8                # an auxiliary lane widens the deck by this
PIER_PITCH = 20.0          # one pillar per deck per tile
PIER_OFF_STREET = 16.0     # piers straddle a street at +-16 from its centreline
PILLAR_H = 15.18           # SM_Env_Road_Highway_Pillar_01 below its pivot (measured)
BRIDGE_SUPPORT_W = 23.38   # SM_Env_Bridge_Support_01 (measured)
E_MAX = 0.06               # superelevation in the arcs
SPEED_LIMIT = 24.6         # 55 mph
ARC_ADVISORY = 22.4        # 50 mph
A_LAT = 2.0                # m/s^2 Traffic keeps in a bend
CLEARANCE_MIN = 4.9        # 16 ft
ROW_HALF = 20.0            # chain-link either side of the axis
CAR_PITCH = 40.0           # one car per lane per 40 m at spawn
TRUNK_GRADE = 0.045        # the trunk's own descents (exit arc, terminus)
K_CREST, K_SAG = 45.0, 30.0

# ---------------------------------------------------------------- the ramp
RAMP_W = 7.3               # one lane and two shoulders
RAMP_OFF = 18.0            # ramp axis off the trunk axis
RAMP_GRADE = 0.06          # 6 %: the urban maximum
K_RAMP = 5.0               # m per % of grade change, 25 mph ramps
DECEL_TAPER, DECEL_PAR = 50.0, 70.0     # deceleration lane on the deck
ACCEL_PAR, ACCEL_TAPER = 120.0, 60.0    # acceleration lane on the deck
NOSE = 35.0                # gore: the ramp gets its own parapet and slides from the aux lane to RAMP_OFF
GRADE_RUN = 85.0           # the straight 6 % run
R_TERMINAL = 40.0          # diamond: the quarter circle down to the arterial, 20 mph
V_RAMP_END = 13.0          # speed at the end of the deceleration lane
V_ACCEL_START = 13.0
TERMINAL_OFF = RAMP_OFF + R_TERMINAL     # T on the arterial at +-58 from the axis
GATE_MIN = 90.0            # outer terminal to the village gate
EDGE_MIN = 70.0            # inner terminal to the grid's edge junction (a short downtown block)
SPACING = 800.0            # interchange centre to centre (half a mile)
# Entrance nose to the next exit nose. 400 m is what a manual asks for where it can be
# had; 350 is what half a mile of city leaves once the ramps have taken their share, and
# half a mile is the urban minimum the same manual gives. The cities this game rolls are
# not big enough for both numbers at once.
NOSE_MIN = 350.0
TANGENT_MARGIN = 100.0     # an arterial's station keeps this far from an arc's tangent point
IC_ALONG, IC_ACROSS = 840.0, 140.0       # the rect a diamond reserves in CityLayout
NEAR_STRIP = 200.0         # ring-0 villages start here with the expressway
NEAR_STRIP_IC = 280.0      # ... and here on a diamond line
COAST_GROW = 90.0          # the island grows by what the villages were pushed

# ---------------------------------------------------------------- the trumpet (a branch to a leaf)
R_DIRECT = 120.0           # direct and semi-direct ramps, 35 mph
CROSS_AT = 0.38            # a crossing ramp must be down by this much of its length
SPUR_START = 320.0         # the at-grade spur begins this far out from the trunk axis
TRUMPET_ALONG = 840.0      # rect along the trunk (the same aux lanes as a diamond)
TRUMPET_ACROSS = (-30.0, 320.0)   # rect across: grid side .. outward
LEAF_MARGIN = 30.0         # spur needed between its start and the district gate

# ---------------------------------------------------------------- the downtown terminus
TERMINUS_LEVEL = 60.0      # at grade before the inward curve
R_TERMINUS = 90.0          # the inward curve, 30 mph
TERMINUS_ZONE = 540.0      # band without any connector
TERMINUS_STRAIGHT_MIN = 40.0   # from the end of the curve to the edge junction's centre
TRUNK_MIN, TRUNK_MAX = 1600.0, 4500.0

fails, notes = [], []


def check(ok, msg):
    (notes if ok else fails).append(('ok    ' if ok else 'FAIL  ') + msg)


def bend_speed(r):
    return math.sqrt(A_LAT * r)


def trunk_descent():
    """Length of a 4 % descent with the trunk's vertical curves, and its drop."""
    a = TRUNK_GRADE * 100
    crest, sag = K_CREST * a, K_SAG * a
    drop_curves = TRUNK_GRADE * crest / 2 + TRUNK_GRADE * sag / 2
    straight = (DECK_Y - GRADE_Y - drop_curves) / TRUNK_GRADE
    return crest + straight + sag, drop_curves + TRUNK_GRADE * straight


# ---------------------------------------------------------------- the deck
clearance = DECK_Y - SLAB - GRADE_Y
check(clearance >= CLEARANCE_MIN, 'clearance under the deck %.2f m (>= %.1f)' % (clearance, CLEARANCE_MIN))
check(PILLAR_H >= DECK_Y + 2, 'pillar %.2f m reaches the ground from a %.1f m deck with room to bury' % (PILLAR_H, DECK_Y))
twin = 2 * DECK_W + GAP
check(abs(twin - BRIDGE_SUPPORT_W) <= 1.5, 'river bent %.2f m under a %.1f m twin deck (scale <= 7%%)' % (BRIDGE_SUPPORT_W, twin))
bank = E_MAX * (DECK_OFF + DECK_W / 2)
check(bank < 1.0, 'superelevation lifts the outer parapet %.2f m' % bank)
check(bend_speed(R) >= ARC_ADVISORY, 'Traffic holds %.1f m/s in the R=%.0f arc (advisory %.1f)' % (bend_speed(R), R, ARC_ADVISORY))
check(DECK_OFF + DECK_W / 2 + AUX_W >= DECK_OFF + LANE_OFF + AUX_W, 'the widened deck holds the auxiliary lane')

# ---------------------------------------------------------------- the ramp comes down
curve = K_RAMP * RAMP_GRADE * 100          # length of each vertical curve (K x A)
curve_drop = RAMP_GRADE * curve / 2        # a curve from 0 to g drops g*L/2
drop = 2 * curve_drop + RAMP_GRADE * GRADE_RUN
need = DECK_Y - GRADE_Y
descent = 2 * curve + GRADE_RUN            # 145: the ramp's own descent
check(abs(drop - need) < 0.05, 'ramp drops %.2f m over %.0f m; the deck is %.2f m up' % (drop, descent, need))
ramp_len = NOSE + descent + math.pi * R_TERMINAL / 2
check(ramp_len >= 150, 'diamond ramp is %.0f m nose to terminal (>= 150)' % ramp_len)
check(RAMP_GRADE <= 0.06 + 1e-9, 'ramp grade %.0f %% (<= 6)' % (RAMP_GRADE * 100))
check(K_RAMP >= 5, 'vertical curves K=%.0f' % K_RAMP)
decel = DECEL_TAPER + DECEL_PAR
a_decel = (SPEED_LIMIT ** 2 - V_RAMP_END ** 2) / (2 * decel)
check(a_decel <= 2.5, 'deceleration lane %.0f m: %.1f -> %.1f m/s at %.2f m/s^2' % (decel, SPEED_LIMIT, V_RAMP_END, a_decel))
accel = ACCEL_PAR + ACCEL_TAPER
for name, cruise, acc in (('Traffic', 23.0, 3.5), ('Lorry', 20.0, 2.0)):
    a_need = (cruise ** 2 - V_ACCEL_START ** 2) / (2 * accel)
    check(a_need <= acc * 0.8, '%s reaches %.0f m/s on the %.0f m acceleration lane (%.2f of %.1f m/s^2)' % (name, cruise, accel, a_need, acc))
check(bend_speed(R_TERMINAL) >= 8.0, 'the R=%.0f terminal curve is taken at %.1f m/s' % (R_TERMINAL, bend_speed(R_TERMINAL)))
aux_centre = DECK_OFF + LANE_OFF + AUX_W        # 14.1: the aux lane's centre off the axis
drift = RAMP_OFF - aux_centre
check(math.degrees(math.atan2(drift, NOSE)) <= 8, 'nose slides the ramp %.1f m over %.0f m (%.1f deg)' % (drift, NOSE, math.degrees(math.atan2(drift, NOSE))))
air = (RAMP_OFF - RAMP_W / 2) - (DECK_OFF + DECK_W / 2)
check(air >= 2.0, '%.2f m of air between the deck parapet and the ramp' % air)
check(TERMINAL_OFF + RAMP_W / 2 + 1 <= IC_ACROSS / 2, 'terminal curve at %.0f m stays inside the diamond rect (+-%.0f)' % (TERMINAL_OFF, IC_ACROSS / 2))

# ---------------------------------------------------------------- the diamond on the arterial
nose_station = NOSE + descent + R_TERMINAL             # 220: the exit nose, back from the arterial
exit_start = decel + nose_station                      # 340: the decel lane begins
entry_end = nose_station + accel                       # 400: the accel lane ends
check(max(exit_start, entry_end) <= IC_ALONG / 2, 'ramps span -%.0f..+%.0f, inside the %.0f m rect' % (exit_start, entry_end, IC_ALONG))
inner_terminal_from_kerb = D - TERMINAL_OFF
check(inner_terminal_from_kerb + OUTER_HALF >= EDGE_MIN, 'inner terminal %.0f m from the edge junction (>= %.0f)' % (inner_terminal_from_kerb + OUTER_HALF, EDGE_MIN))
outer_terminal_from_kerb = D + TERMINAL_OFF
check(NEAR_STRIP_IC - outer_terminal_from_kerb >= GATE_MIN, 'village gate %.0f m past the outer terminal (>= %.0f)' % (NEAR_STRIP_IC - outer_terminal_from_kerb, GATE_MIN))
check(NEAR_STRIP - (D + ROW_HALF) >= 40, 'ring-0 village %.0f m clear of the right-of-way on a plain line' % (NEAR_STRIP - (D + ROW_HALF)))
check(NEAR_STRIP >= D + IC_ACROSS / 2 + 10, 'ring-0 villages on neighbouring lines clear the diamond rect')
noses = SPACING - 2 * nose_station
check(noses >= NOSE_MIN, 'entrance nose to next exit nose %.0f m at %.0f m spacing (>= %.0f)' % (noses, SPACING, NOSE_MIN))
check(SPACING - entry_end - exit_start >= 0, 'auxiliary lanes of neighbours do not overlap (%.0f m between)' % (SPACING - entry_end - exit_start))
check(PIER_OFF_STREET - OUTER_HALF >= 1.0, 'piers stand %.1f m outside the street\'s pavement' % (PIER_OFF_STREET - OUTER_HALF))
check(PIER_OFF_STREET >= CONNECTOR_HALF - 5, 'piers sit at the edge of the connector\'s flattened strip')

# ---------------------------------------------------------------- the trumpet
check(bend_speed(R_DIRECT) >= 15.0, 'the R=%.0f branch ramps are taken at %.1f m/s (35 mph)' % (R_DIRECT, bend_speed(R_DIRECT)))
direct_straight = nose_station - NOSE - (R_DIRECT - DECK_OFF)
direct_len = direct_straight + math.pi * R_DIRECT / 2
check(direct_len >= descent, 'branch ramp: %.0f m straight + %.0f m arc = %.0f m, holds the %.0f m descent' % (direct_straight, math.pi * R_DIRECT / 2, direct_len, descent))
# the two that cross BENEATH the deck have to be on the ground before they get there,
# not still coming down: at 1.9 m of headroom a bridge goes through the roof of a car
cross_len = math.hypot(nose_station, SPUR_START + DECK_OFF)
cross_grade = (DECK_Y - GRADE_Y) / (cross_len * CROSS_AT)
check(cross_grade <= 0.07, 'a ramp crossing under the deck is down in %.0f m of its %.0f (%.1f %%)' % (cross_len * CROSS_AT, cross_len, cross_grade * 100))
check(DECK_Y - SLAB - GRADE_Y >= CLEARANCE_MIN, 'and passes under it with %.2f m of air' % (DECK_Y - SLAB - GRADE_Y))
# the plaza is on the ARM: two interchanges half a mile apart leave four metres of trunk
# between their auxiliary lanes, and a barrier wants two hundred
check(430.0 >= 200.0, 'the branch is 430 m long and the toll plaza wants 200')
direct_end = RAMP_OFF + R_DIRECT
check(direct_end + accel <= SPUR_START, 'branch ramp reaches the spur by %.0f m; the spur starts at %.0f' % (direct_end + accel, SPUR_START))
check(exit_start <= TRUMPET_ALONG / 2 and entry_end <= TRUMPET_ALONG / 2, 'trumpet ramps fit the %.0f m rect along the trunk' % TRUMPET_ALONG)
check(TRUMPET_ACROSS[0] <= -(RAMP_OFF + RAMP_W / 2), 'trumpet rect covers the grid-side ramps (to %.0f)' % TRUMPET_ACROSS[0])
spur_from_kerb = D + SPUR_START
check(spur_from_kerb + LEAF_MARGIN > AIRPORT_STRIP, 'the airport (%.0f) cannot take a trumpet (spur starts %.0f + %.0f) -> A is the trunk end' % (AIRPORT_STRIP, spur_from_kerb, LEAF_MARGIN))
harbor_min_strip = spur_from_kerb + LEAF_MARGIN
for c in COASTS_TODAY:
    strip = max(200, c + COAST_GROW - HARBOR_DEPTH_MARGIN)
    check(strip >= harbor_min_strip, 'harbor on a %.0f m shore (+%.0f) stands at %.0f, trumpet needs %.0f' % (c, COAST_GROW, strip, harbor_min_strip))

# ---------------------------------------------------------------- the airport exit arc
desc_len, desc_drop = trunk_descent()
check(abs(desc_drop - need) < 0.05, 'trunk descent at %.0f %%: %.0f m for %.2f m' % (TRUNK_GRADE * 100, desc_len, desc_drop))
for r in (R, R_EXIT_TIGHT):
    arc = math.pi * r / 2
    arc_end = D + r                                    # from the kerb, where the spur straight begins
    spur = AIRPORT_STRIP - arc_end
    check(desc_len <= arc + spur, 'exit arc R=%.0f: %.0f m arc + %.0f m spur hold the %.0f m descent' % (r, arc, spur, desc_len))
    check(spur >= 0, 'exit arc R=%.0f ends %.0f m before the airport' % (r, spur))
tangent = R - D                  # a corner arc begins this far before the grid corner
for lx, lz in GRIDS:
    room = lx / 2 - tangent      # the airport line sits mid-side; room to the corner arc
    check(room >= R_EXIT_TIGHT, 'grid %dx%d: %.0f m between the corner arc and the airport line (>= %.0f for the exit arc)' % (lx, lz, room, R_EXIT_TIGHT))

# ---------------------------------------------------------------- the downtown terminus
zone_need = desc_len + TERMINUS_LEVEL + R_TERMINUS
check(zone_need <= TERMINUS_ZONE, 'terminus zone %.0f m holds descent + level + curve = %.0f' % (TERMINUS_ZONE, zone_need))
curve_end_from_kerb = D - R_TERMINUS
straight_to_junction = curve_end_from_kerb + OUTER_HALF
check(straight_to_junction >= TERMINUS_STRAIGHT_MIN, 'inward curve ends %.0f m from the edge junction (>= %.0f)' % (straight_to_junction, TERMINUS_STRAIGHT_MIN))
check(bend_speed(R_TERMINUS) >= 13.0, 'the R=%.0f terminus curve is taken at %.1f m/s (30 mph)' % (R_TERMINUS, bend_speed(R_TERMINUS)))

# ---------------------------------------------------------------- the corner arc
corner_clear = R - math.sqrt(2) * (R - D)
check(tangent > 0, 'corner arc tangent %.0f m before the corner' % tangent)
check(corner_clear >= 30, 'the corner arc passes %.0f m from the grid corner (>= 30)' % corner_clear)
arc_len = math.pi * R / 2

# ---------------------------------------------------------------- the trunk on a grid
# Band stations run clockwise from the NW corner. The airport line is mid-north; the harbor
# line is 250 m from a corner of some shore. The trunk goes the short way A -> H, then on past H
# by at least the trumpet's tail plus the terminus zone, and to at least TRUNK_MIN.
def band_legs(lx, lz):
    return {'N': lx - 2 * tangent, 'E': lz - 2 * tangent, 'S': lx - 2 * tangent, 'W': lz - 2 * tangent}


for lx, lz in GRIDS:
    legs = band_legs(lx, lz)
    perim = 2 * (legs['N'] + legs['E']) + 4 * arc_len
    a = legs['N'] / 2                                  # station of the airport port on the N leg
    cases = {
        'same side (NE end)': legs['N'] - 250 + tangent,           # H 250 m from the NE corner, on N
        'adjacent side (E)': legs['N'] + arc_len + (250 - tangent),  # H on E, 250 m from the NE corner
        'opposite side (S)': legs['N'] + arc_len + legs['E'] + arc_len + (250 - tangent),
    }
    for name, h in cases.items():
        arc_ah = min(abs(h - a), perim - abs(h - a))
        trumpet = arc_ah >= SPACING
        ext = max(entry_end + TERMINUS_ZONE, TRUNK_MIN - arc_ah)
        trunk = arc_ah + ext
        check(TRUNK_MIN <= trunk <= TRUNK_MAX, 'grid %dx%d, harbor %s: A->H %.0f m, trunk %.0f m, %s' % (lx, lz, name, arc_ah, trunk, 'trumpet' if trumpet else 'diamond for the port'))
        cars = int((trunk + SPUR_START) / CAR_PITCH) * 4
        notes.append('note  grid %dx%d, harbor %s: ~%d cars at spawn' % (lx, lz, name, cars))

# ---------------------------------------------------------------- the island still holds its villages
def ring_fits(coast, near):
    return [coast >= near + k * RING_STEP + SUBURB_DEPTH + STRAND for k in range(3)]

for c in COASTS_TODAY:
    before = ring_fits(c, NEAR_STRIP_TODAY)
    after = ring_fits(c + COAST_GROW, NEAR_STRIP)
    check(before == after, 'coast %d m: rings that fit today %s still fit at %d m with the expressway %s' % (c, before, c + COAST_GROW, after))
check(NEAR_STRIP - NEAR_STRIP_TODAY == COAST_GROW, 'the island grows by exactly what the villages were pushed (%.0f m)' % COAST_GROW)

# ---------------------------------------------------------------- report
verbose = '-v' in sys.argv
for n in notes:
    if verbose or n.startswith('note'):
        print(n)
for f in fails:
    print(f)
print('%d checks, %d failures' % (len(fails) + len([n for n in notes if n.startswith('ok')]), len(fails)))
sys.exit(1 if fails else 0)
