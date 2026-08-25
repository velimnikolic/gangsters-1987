"""The Inner Loop's arithmetic, checked without opening Unity.

    python Docs/innerloop.py [-v]

The numbers in Docs/inner-loop-plan.md, written out once more and asserted: does the
ramp come down to the ground on the length the plan gives it, does the corner arc clear
the grid, do two diamonds fit on a side, does the ring push the villages off the island,
does a pier stand in the street it is meant to span. When the C# lands, its constants
must equal these (the header of each section names the future home of each number).

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

# ---------------------------------------------------------------- the ring (RoadDemoBuilder.Loop.cs)
D = 120.0                  # axis of the ring off the grid's outer kerb
R = 260.0                  # corner arc, 50 mph at e = 6 %
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

# ---------------------------------------------------------------- the ramp
RAMP_W = 7.3               # one lane and two shoulders
RAMP_OFF = 18.0            # ramp axis off the ring axis
RAMP_GRADE = 0.06          # 6 %: the urban maximum
K_RAMP = 5.0               # m per % of grade change, 25 mph ramps
DECEL_TAPER, DECEL_PAR = 50.0, 70.0     # deceleration lane on the deck
ACCEL_PAR, ACCEL_TAPER = 120.0, 60.0    # acceleration lane on the deck
NOSE = 35.0                # gore: the ramp gets its own parapet and slides from the aux lane to RAMP_OFF
GRADE_RUN = 85.0           # the straight 6 % run
R_TERMINAL = 40.0          # the quarter circle down to the arterial, 20 mph
V_RAMP_END = 13.0          # speed at the end of the deceleration lane
V_ACCEL_START = 13.0
TERMINAL_OFF = RAMP_OFF + R_TERMINAL     # T on the arterial at +-58 from the axis
GATE_MIN = 90.0            # outer terminal to the village gate
EDGE_MIN = 70.0            # inner terminal to the grid's edge junction (a short downtown block)
SPACING = 850.0            # interchange centre to centre
NOSE_MIN = 400.0           # entrance nose to the next exit nose
TANGENT_MARGIN = 100.0     # an arterial's station keeps this far from an arc's tangent point
IC_ALONG, IC_ACROSS = 840.0, 140.0       # the rect an interchange reserves in CityLayout
NEAR_STRIP = 200.0         # ring-0 villages start here with the loop
NEAR_STRIP_IC = 280.0      # ... and here on an interchange line
COAST_GROW = 90.0          # the island grows by what the villages were pushed

fails, notes = [], []


def check(ok, msg):
    (notes if ok else fails).append(('ok    ' if ok else 'FAIL  ') + msg)


# ---------------------------------------------------------------- the deck
clearance = DECK_Y - SLAB - GRADE_Y
check(clearance >= CLEARANCE_MIN, 'clearance under the deck %.2f m (>= %.1f)' % (clearance, CLEARANCE_MIN))
check(PILLAR_H >= DECK_Y + 2, 'pillar %.2f m reaches the ground from a %.1f m deck with room to bury' % (PILLAR_H, DECK_Y))
twin = 2 * DECK_W + GAP
check(abs(twin - BRIDGE_SUPPORT_W) <= 1.5, 'river bent %.2f m under a %.1f m twin deck (scale <= 7%%)' % (BRIDGE_SUPPORT_W, twin))
bank = E_MAX * (DECK_OFF + DECK_W / 2)
check(bank < 1.0, 'superelevation lifts the outer parapet %.2f m' % bank)
v_arc = math.sqrt(A_LAT * R)
check(v_arc >= ARC_ADVISORY, 'Traffic holds %.1f m/s in the R=%.0f arc (advisory %.1f)' % (v_arc, R, ARC_ADVISORY))
lane_edge = DECK_OFF + LANE_OFF + AUX_W / 2 + AUX_W / 2
check(DECK_OFF + DECK_W / 2 + AUX_W >= lane_edge, 'the widened deck holds the auxiliary lane')

# ---------------------------------------------------------------- the ramp comes down
curve = K_RAMP * RAMP_GRADE * 100          # length of each vertical curve (K x A)
curve_drop = RAMP_GRADE * curve / 2        # a curve from 0 to g drops g*L/2
drop = 2 * curve_drop + RAMP_GRADE * GRADE_RUN
need = DECK_Y - GRADE_Y
check(abs(drop - need) < 0.05, 'ramp drops %.2f m over %.0f m; the deck is %.2f m up' % (drop, 2 * curve + GRADE_RUN, need))
ramp_len = NOSE + 2 * curve + GRADE_RUN + math.pi * R_TERMINAL / 2
check(ramp_len >= 150, 'ramp is %.0f m nose to terminal (>= 150)' % ramp_len)
check(RAMP_GRADE <= 0.06 + 1e-9, 'ramp grade %.0f %% (<= 6)' % (RAMP_GRADE * 100))
check(K_RAMP >= 5, 'vertical curves K=%.0f' % K_RAMP)
decel = DECEL_TAPER + DECEL_PAR
a_decel = (SPEED_LIMIT ** 2 - V_RAMP_END ** 2) / (2 * decel)
check(a_decel <= 2.5, 'deceleration lane %.0f m: %.1f -> %.1f m/s at %.2f m/s^2' % (decel, SPEED_LIMIT, V_RAMP_END, a_decel))
accel = ACCEL_PAR + ACCEL_TAPER
for name, cruise, acc in (('Traffic', 23.0, 3.5), ('Lorry', 20.0, 2.0)):
    a_need = (cruise ** 2 - V_ACCEL_START ** 2) / (2 * accel)
    check(a_need <= acc * 0.8, '%s reaches %.0f m/s on the %.0f m acceleration lane (%.2f of %.1f m/s^2)' % (name, cruise, accel, a_need, acc))
v_term = math.sqrt(A_LAT * R_TERMINAL)
check(v_term >= 8.0, 'the R=%.0f terminal curve is taken at %.1f m/s' % (R_TERMINAL, v_term))
aux_centre = DECK_OFF + LANE_OFF + AUX_W        # 14.1: the aux lane's centre off the axis
drift = RAMP_OFF - aux_centre
check(math.degrees(math.atan2(drift, NOSE)) <= 8, 'nose slides the ramp %.1f m over %.0f m (%.1f deg)' % (drift, NOSE, math.degrees(math.atan2(drift, NOSE))))
air = (RAMP_OFF - RAMP_W / 2) - (DECK_OFF + DECK_W / 2)
check(air >= 2.0, '%.2f m of air between the deck parapet and the ramp' % air)
check(TERMINAL_OFF + RAMP_W / 2 + 1 <= IC_ACROSS / 2, 'terminal curve at %.0f m stays inside the interchange rect (+-%.0f)' % (TERMINAL_OFF, IC_ACROSS / 2))

# ---------------------------------------------------------------- the diamond on the arterial
exit_nose = decel + NOSE + 2 * curve + GRADE_RUN + R_TERMINAL      # station of the decel start, back from the arterial
nose_station = NOSE + 2 * curve + GRADE_RUN + R_TERMINAL           # station of the exit nose
entry_end = R_TERMINAL + 2 * curve + GRADE_RUN + NOSE + accel
check(max(exit_nose, entry_end) <= IC_ALONG / 2, 'ramps span -%.0f..+%.0f, inside the %.0f m rect' % (exit_nose, entry_end, IC_ALONG))
inner_terminal_from_kerb = D - TERMINAL_OFF
check(inner_terminal_from_kerb + OUTER_HALF >= EDGE_MIN, 'inner terminal %.0f m from the edge junction (>= %.0f)' % (inner_terminal_from_kerb + OUTER_HALF, EDGE_MIN))
outer_terminal_from_kerb = D + TERMINAL_OFF
check(NEAR_STRIP_IC - outer_terminal_from_kerb >= GATE_MIN, 'village gate %.0f m past the outer terminal (>= %.0f)' % (NEAR_STRIP_IC - outer_terminal_from_kerb, GATE_MIN))
check(NEAR_STRIP - (D + ROW_HALF) >= 40, 'ring-0 village %.0f m clear of the right-of-way on a plain line' % (NEAR_STRIP - (D + ROW_HALF)))
check(NEAR_STRIP >= D + IC_ACROSS / 2 + 10, 'ring-0 villages on neighbouring lines clear the interchange rect')
noses = SPACING - 2 * nose_station
check(noses >= NOSE_MIN, 'entrance nose to next exit nose %.0f m at %.0f m spacing (>= %.0f)' % (noses, SPACING, NOSE_MIN))
check(SPACING - entry_end - exit_nose >= 0, 'auxiliary lanes of neighbours do not overlap (%.0f m between)' % (SPACING - entry_end - exit_nose))
check(PIER_OFF_STREET - OUTER_HALF >= 1.0, 'piers stand %.1f m outside the street\'s pavement' % (PIER_OFF_STREET - OUTER_HALF))
check(PIER_OFF_STREET >= CONNECTOR_HALF - 5, 'piers sit at the edge of the connector\'s flattened strip')

# ---------------------------------------------------------------- the corner arc
tangent = R - D                  # the arc begins this far before the grid corner, along the side
corner_clear = R - math.sqrt(2) * (R - D)
check(tangent > 0, 'arc tangent %.0f m before the corner' % tangent)
check(corner_clear >= 30, 'the arc passes %.0f m from the grid corner (>= 30)' % corner_clear)
arc_len = math.pi * R / 2

# ---------------------------------------------------------------- the ring on a grid
for lx, lz in GRIDS:
    legs = [lx - 2 * tangent, lz - 2 * tangent]
    ring = 2 * sum(legs) + 4 * arc_len
    check(all(l > 0 for l in legs), 'grid %dx%d: straight legs %.0f / %.0f m' % (lx, lz, legs[0], legs[1]))
    usable = [max(0.0, l - 2 * TANGENT_MARGIN) for l in legs]
    per_side = [int(u // SPACING) + 1 if u > 0 else 0 for u in usable]
    total = 2 * sum(per_side)
    check(total >= 3, 'grid %dx%d: ring %.0f m, room for %d interchanges (%d per long side, %d per short)' % (lx, lz, ring, total, per_side[0], per_side[1]))
    cars = int(ring / CAR_PITCH) * 4
    notes.append('note  grid %dx%d: %d cars on the loop at spawn' % (lx, lz, cars))

# ---------------------------------------------------------------- the island still holds its villages
def ring_fits(coast, near):
    return [coast >= near + k * RING_STEP + SUBURB_DEPTH + STRAND for k in range(3)]

for c in COASTS_TODAY:
    before = ring_fits(c, NEAR_STRIP_TODAY)
    after = ring_fits(c + COAST_GROW, NEAR_STRIP)
    check(before == after, 'coast %d m: rings that fit today %s still fit at %d m with the loop %s' % (c, before, c + COAST_GROW, after))
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
