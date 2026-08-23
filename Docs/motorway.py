"""The new elevated freeway's arithmetic, checked without opening Unity.

    python3 Docs/motorway.py

Everything here is the same arithmetic the code does - RoadDemoBuilder.Freeway.cs for
the road, FreewayDemo/FreewayDemoBuilder.cs for the scene's grid - written out once
more and asserted. It answers the questions a run cannot answer cheaply: is the ramp a
ramp or a wall, do two interchanges have room for their ramps, does the toll plaza land
on a stretch of deck that both decks actually carry, does a pier stand in a road, does
the green belt reach the end of the road.

Keep this in step with the constants in those two files: a check that has drifted from
the code is worse than no check.
"""

# ---------------------------------------------------------------- the constants

STREET_HALF = 7.5          # RoadDemoBuilder.StreetHalf (two lanes and a parking strip)
BOULEVARD_HALF = 17.5      # RoadDemoBuilder.BoulevardHalf
PAVE = 6.5                 # SidewalkDressing.Width

DECK_OFF = 5.7             # FreeDeckOff: each deck's centre off the line
DECK_HALF = 5.7            # FreeDeckHalf
GORE_OFF = 11.4            # FreeGoreOff (the node)
GORE_DECK = 17.1           # FreeGoreDeck (the ramp's own asphalt)
FOOT_OFF = 30.0            # FreeFootOff
RAMP_RUN = 160.0           # FreeRampRun
OVERSHOOT = 45.0           # FreeOvershoot
DECK_PIECE = 20.0          # SM_Env_Road_Highway_01, along its own +Z

# the demo scene's own knobs (FreewayDemoBuilder's defaults)
COLUMNS = 2
ROWS = 2
BLOCK_W = 85.0
BLOCK_D = 70.0
APART = 600.0
FREEWAY_OFF = 120.0
DECK_Y = 9.0

fails = []
checks = 0


def check(ok, what):
    global checks
    checks += 1
    if not ok:
        fails.append(what)


def half(boulevard):
    return BOULEVARD_HALF if boulevard else STREET_HALF


def centrelines(boulevard, interiors):
    at = [0.0] * len(boulevard)
    for k in range(len(boulevard) - 1):
        at[k + 1] = (at[k] + half(boulevard[k]) + PAVE + interiors[k] +
                     PAVE + half(boulevard[k + 1]))
    return at


# ------------------------------------------------------------------- the scene

per_quarter = COLUMNS + 1
nv = per_quarter * 2
nh = ROWS + 1

v_blvd = [False] * nv
v_blvd[per_quarter // 2] = True
v_blvd[per_quarter + per_quarter // 2] = True
v_gaps = [BLOCK_W] * nv
v_gaps[per_quarter - 1] = APART
vx = centrelines(v_blvd, v_gaps)

h_blvd = [False] * nh
h_gaps = [BLOCK_D] * nh
hz = centrelines(h_blvd, h_gaps)

grid_north = hz[-1] + half(h_blvd[-1]) + PAVE
across = grid_north + FREEWAY_OFF

stations = [vx[per_quarter // 2], vx[per_quarter + per_quarter // 2]]

print(f"grid: {nv} north-south lines, {vx[0]:.0f} to {vx[-1]:.0f} m "
      f"({vx[-1]:.0f} m across, quarters {APART:.0f} m apart)")
print(f"the freeway: z = {across:.0f}, deck at {DECK_Y:.1f} m, "
      f"interchanges at x = {stations[0]:.0f} and {stations[1]:.0f}")

# ------------------------------------------------------------------ the checks

# 1. the gap between the quarters really is what the scene asked for: the seam takes
#    the interior between two lines, kerb to kerb, pavements included
gap = (vx[per_quarter] - half(v_blvd[per_quarter]) - PAVE
       - vx[per_quarter - 1] - half(v_blvd[per_quarter - 1]) - PAVE)
check(abs(gap - APART) < 0.5, f"the quarters stand {gap:.0f} m apart, not {APART:.0f}")

# 2. a ramp is a ramp: nine metres over a hundred and sixty is one in eighteen. Over 8%
#    is a wall, under 3% is a ramp so long it costs the whole run.
grade = DECK_Y / RAMP_RUN
check(0.03 <= grade <= 0.08, f"the ramp climbs at {grade * 100:.1f}%")

# 3. two interchanges have room for their ramps: each takes RAMP_RUN either side of its
#    street, and the ramps of neighbours may not meet
apart = abs(stations[1] - stations[0])
check(apart > 2 * RAMP_RUN + 40, f"the interchanges are {apart:.0f} m apart, "
                                 f"and two diamonds want {2 * RAMP_RUN + 40:.0f}")

# 4. the gores, deck by deck, exactly as BuildFreeway trims them. The +u deck: an exit
#    before each station and an entrance after, then nothing that leaves before anything
#    has joined and nothing that joins after the last way off.
def gores(forward):
    out = []
    for u in sorted(stations):
        out.append((u - RAMP_RUN if forward else u + RAMP_RUN, False, u))
        out.append((u + RAMP_RUN if forward else u - RAMP_RUN, True, u))
    out.sort(key=lambda g: g[0], reverse=not forward)
    while out and not out[0][1]:
        out.pop(0)
    while out and out[-1][1]:
        out.pop()
    return out


fwd = gores(True)
bwd = gores(False)
check(len(fwd) >= 2, "the +u deck came out with no way on and no way off")
check(len(bwd) >= 2, "the -u deck came out with no way on and no way off")
check(fwd[0][1] and not fwd[-1][1], "the +u deck does not begin at an entrance and end at an exit")
check(bwd[0][1] and not bwd[-1][1], "the -u deck does not begin at an entrance and end at an exit")

all_u = [g[0] for g in fwd + bwd]
deck_from, deck_to = min(all_u) - OVERSHOOT, max(all_u) + OVERSHOOT
print(f"the deck: {deck_from:.0f} to {deck_to:.0f} m "
      f"({(deck_to - deck_from) / DECK_PIECE:.0f} pieces a side), "
      f"+u carries {fwd[0][0]:.0f} to {fwd[-1][0]:.0f}, -u carries {bwd[0][0]:.0f} to {bwd[-1][0]:.0f}")

# 5. every gore stands on deck that was actually laid
for u, entry, st in fwd + bwd:
    check(deck_from <= u <= deck_to, f"a gore at {u:.0f} m is off the end of the deck")

# 6. the toll plaza: half way between the outermost interchanges, and it must fall on
#    both decks' own runs with room to stop either side of it
toll = (stations[0] + stations[-1]) * 0.5
for name, run in (("+u", fwd), ("-u", bwd)):
    lo, hi = min(run[0][0], run[-1][0]), max(run[0][0], run[-1][0])
    check(lo + 30 <= toll <= hi - 30,
          f"the toll plaza at {toll:.0f} m is not on the {name} deck's run ({lo:.0f}..{hi:.0f})")
print(f"the plaza: x = {toll:.0f}, {abs(toll - stations[0]):.0f} m from one interchange "
      f"and {abs(toll - stations[1]):.0f} from the other")

# 7. nothing of the plaza stands in a lane: the booths sit on an apron a whole deck
#    width outboard, and the boom posts on the edges
apron = DECK_OFF + DECK_HALF * 2
check(apron - DECK_HALF >= DECK_OFF + DECK_HALF - 0.01,
      "the plaza apron overlaps the carriageway")
booth = DECK_OFF + DECK_HALF + 3.0
check(booth > DECK_OFF + DECK_HALF, "a toll booth stands in the road")

# 8. the ramps clear the freeway: at the gore the ramp's asphalt stands ALONGSIDE the
#    deck, not in it. A deck runs from the line out to DECK_OFF + DECK_HALF.
check(GORE_DECK - DECK_HALF >= DECK_OFF + DECK_HALF - 0.01,
      f"the ramp's gore at {GORE_DECK} m cuts into the deck")
# and its node's box has to cover both, or a car cannot cross from one to the other
check(GORE_OFF - 8.0 <= DECK_OFF + DECK_HALF and GORE_OFF + 8.0 >= GORE_DECK - DECK_HALF,
      "the gore's junction box does not reach both the deck and the ramp")

# 9. the feet stand clear of the deck overhead and of each other
check(FOOT_OFF - 8.0 > DECK_OFF + DECK_HALF,
      "a ramp foot stands under the deck's own edge")
check(2 * FOOT_OFF > 2 * 8.0 + 4.0, "the two feet of one interchange overlap")

# 10. the freeway clears the city: the far foot, the near foot and the deck all stand
#     north of the last street's pavement
check(across - FOOT_OFF - 8.0 > grid_north + 4.0,
      f"the near foot at z = {across - FOOT_OFF:.0f} sits on the city's last street "
      f"(which ends at {grid_north:.0f})")

# 11. the green belt reaches the whole road: the deck ends past the grid at both ends
#     and the far foot stands north of it
margin = max(200.0, FREEWAY_OFF + 140.0)
check(vx[0] - margin <= deck_from, f"the deck starts {deck_from:.0f} m out, "
                                   f"and the belt only reaches {vx[0] - margin:.0f}")
check(vx[-1] + margin >= deck_to, f"the deck ends at {deck_to:.0f} m, "
                                  f"and the belt only reaches {vx[-1] + margin:.0f}")
north = max(margin, FREEWAY_OFF + 120.0)
check(hz[-1] + north >= across + FOOT_OFF + 20.0,
      "the belt does not reach the far side of the freeway")

# 12. a crossing is unmistakable in the trace: the two ways on stand far enough apart
#     that analyze.py's 300 m rule cannot call a shuffle a journey
check(apart >= 300.0, f"the interchanges are {apart:.0f} m apart and a crossing is "
                      "measured at 300")

# 13. the deck pieces close the run without being squeezed: LayDeck floors the count,
#     so every piece is stretched a little and never shrunk
for run in (deck_to - deck_from, RAMP_RUN):
    count = max(1, int(run // DECK_PIECE))
    check(run / count >= DECK_PIECE - 0.001,
          f"a run of {run:.0f} m lays pieces of {run / count:.1f} m - below the authored 20")

print()
if fails:
    print(f"{checks} checks, {len(fails)} FAILED:")
    for f in fails:
        print("  - " + f)
    raise SystemExit(1)
print(f"{checks} checks, 0 failures")
