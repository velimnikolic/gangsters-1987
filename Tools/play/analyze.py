"""Reads a run the play harness left behind and says what the traffic did.

    python Tools/play/analyze.py Temp/play/003
    python Tools/play/analyze.py Temp/play/003 --car 42     one car, second by second
    python Tools/play/analyze.py Temp/play/003 --why        every reason given, counted
    python Tools/play/analyze.py Temp/play/003 --freeway    the motorway: journeys, tolls, faults

The trace is one JSON object a line (DriveTrace): "car"/"ped" samples, "fault" rows
the driving code flagged against itself, "man" manoeuvre changes, "shot"/"hit", and
"belt" for a step the belt had to refuse. Nothing here judges style - it counts what
happened, and the counts are what a run is compared by.
"""

import json
import os
import sys
from collections import Counter, defaultdict


def load(path):
    rows = []
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            try:
                rows.append(json.loads(line))
            except json.JSONDecodeError:
                pass  # a run cut off mid-line: the last row is no loss
    return rows


def secs(v):
    return f"{v:6.1f}s"


def report(dirpath, only_car=None, show_why=False, top=12):
    import os

    trace = os.path.join(dirpath, "trace.jsonl")
    if not os.path.exists(trace):
        print(f"no trace in {dirpath}")
        return 2
    rows = load(trace)
    if not rows:
        print("the trace is empty")
        return 2

    cars = [r for r in rows if r["k"] == "car"]
    peds = [r for r in rows if r["k"] == "ped"]
    faults = [r for r in rows if r["k"] == "fault"]
    mans = [r for r in rows if r["k"] == "man"]
    shots = [r for r in rows if r["k"] == "shot"]
    hits = [r for r in rows if r["k"] == "hit"]
    belts = [r for r in rows if r["k"] == "belt"]
    logs = [r for r in rows if r["k"] == "log"]

    t0 = rows[0]["t"]
    t1 = rows[-1]["t"]
    print(f"== {dirpath}")
    summary = os.path.join(dirpath, "summary.json")
    if os.path.exists(summary):
        print("   " + open(summary, encoding="utf-8").read().strip())
    print(f"   {t1 - t0:.0f}s of sim, {len(rows)} rows: "
          f"{len(cars)} car, {len(peds)} man, {len(faults)} fault, {len(mans)} manoeuvre, "
          f"{len(shots)} shot, {len(belts)} belt, {len(logs)} log")

    if only_car is not None:
        for r in rows:
            if r.get("id") == only_car and r["k"] in ("car", "fault", "man", "belt"):
                bits = " ".join(f"{k}={v}" for k, v in r.items() if k not in ("t", "k", "id", "p"))
                print(f"{secs(r['t'])} {r['k']:6} {bits}")
        return 0

    # ---------------------------------------------------------------- the faults
    if faults:
        print("\n-- what the code flagged against itself")
        by_kind = Counter(f.get("fault", "?") for f in faults)
        for kind, n in by_kind.most_common():
            worst = Counter(f.get("id") for f in faults if f.get("fault") == kind)
            who = ", ".join(f"#{i}x{c}" for i, c in worst.most_common(4))
            print(f"   {kind:10} {n:5}   worst: {who}")
        print("   first few:")
        for f in faults[:8]:
            print(f"     {secs(f['t'])} #{f.get('id')} {f.get('tag','')} {f.get('fault')}: {f.get('what')}"
                  f"  v={f.get('v')} want={f.get('want')} why={f.get('why','')!r}")
    else:
        print("\n-- nothing flagged")

    if belts:
        print(f"\n-- the belt refused {len(belts)} steps (it should refuse none)")
        for b in belts[:5]:
            print(f"     {secs(b['t'])} #{b.get('id')} into {b.get('hit')} v={b.get('v')} why={b.get('why','')!r}")

    if logs:
        print(f"\n-- {len(logs)} errors/exceptions in the log")
        for l in Counter(l.get("what", "")[:110] for l in logs).most_common(5):
            print(f"     {l[1]:4}x {l[0]}")

    # ---------------------------------------------------------------- the driving
    if cars:
        print("\n-- the driving")
        per = defaultdict(list)
        for c in cars:
            per[c["id"]].append(c)
        stopped_total = sum(1 for c in cars if abs(c["v"]) < 0.3)
        wanted_but_still = sum(1 for c in cars if abs(c["v"]) < 0.3 and c["want"] > 0.5)
        print(f"   {len(per)} cars, {stopped_total * 100 // max(1, len(cars))}% of samples stood still"
              f" ({wanted_but_still} of those were asked to move)")

        speeds = sorted(abs(c["v"]) for c in cars)
        print(f"   speed: median {speeds[len(speeds)//2]:.1f}, "
              f"90th {speeds[int(len(speeds)*0.9)]:.1f}, top {speeds[-1]:.1f} m/s")
        accs = sorted(c["acc"] for c in cars)
        print(f"   accel: hardest brake {accs[0]:.1f}, hardest push {accs[-1]:.1f} m/s2")

        # a car that never got anywhere is the thing to look at first
        rank = []
        for cid, samples in per.items():
            still = max((s.get("quiet", 0) for s in samples), default=0)
            moving = sum(1 for s in samples if abs(s["v"]) > 0.5)
            rank.append((still, -moving, cid, samples))
        rank.sort(reverse=True)
        print("   the ones that stood the longest:")
        for still, negmoving, cid, samples in rank[:top]:
            if still < 3:
                break
            last = max(samples, key=lambda s: s.get("quiet", 0))
            print(f"     #{cid:<4} {last.get('tag',''):8} still {still:5.1f}s  "
                  f"road {last.get('road')} s={last.get('s')} man={last.get('man')} "
                  f"queue={last.get('queue')} why={last.get('why','')!r}")

        whys = Counter(c.get("why", "") for c in cars if abs(c["v"]) < 0.5 and c.get("why"))
        if whys:
            print("   why they were stopped (the reason the code gave):")
            for why, n in whys.most_common(30 if show_why else 8):
                print(f"     {n:5}x {why[:150]}")
            unnamed = sum(1 for c in cars if abs(c["v"]) < 0.3 and not c.get("why") and c["want"] > 0.5)
            if unnamed:
                print(f"     {unnamed:5}x (no reason given at all - the bad kind)")

        if mans:
            print("   manoeuvres: " + ", ".join(
                f"{w} x{n}" for w, n in Counter(m.get("what", "") for m in mans).most_common(10)))

    # ---------------------------------------------------------------- the people
    if peds:
        print("\n-- the people")
        pper = defaultdict(list)
        for p in peds:
            pper[p["id"]].append(p)
        stuck = [(max(s.get("still", 0) for s in ss), pid, ss[-1]) for pid, ss in pper.items()]
        stuck.sort(reverse=True)
        paces = sorted(p["pace"] for p in peds)
        print(f"   {len(pper)} on foot, median pace {paces[len(paces)//2]:.2f} m/s, "
              f"{sum(1 for p in peds if p['pace'] < 0.05) * 100 // max(1, len(peds))}% of samples not moving")
        print("   states: " + ", ".join(f"{s} x{n}" for s, n in
                                        Counter(p.get("state", "?") for p in peds).most_common(8)))
        for still, pid, last in stuck[:6]:
            if still < 5:
                break
            print(f"     #{pid:<4} {last.get('tag',''):6} still {still:5.1f}s state={last.get('state')} "
                  f"wait={last.get('wait')} link={last.get('link')}")

    # ---------------------------------------------------------------- the mission
    mission = [r for r in rows if r["k"] == "mission"]
    if mission:
        print("\n-- the run (the lab playing the player)")
        told = [r for r in rows if r["k"] == "mission" and "what" in r]
        for r in told:
            print(f"     {secs(r['t'])} {r.get('who','')} {r.get('what','')}")
        last = mission[-1]
        print(f"   ended in {last.get('state')}, {last.get('killed', 0)} crews down")
        # the crew car second by second: how much of the run it spent stood still
        rolling = [m for m in mission if "v" in m]
        if rolling:
            still = sum(1 for m in rolling if abs(m["v"]) < 0.3)
            print(f"   the car stood still for {still} of {len(rolling)} seconds"
                  f" ({still * 100 // max(1, len(rolling))}%)")
            worst = sorted(rolling, key=lambda m: -m.get("still", 0))[:5]
            for m in worst:
                if m.get("still", 0) < 3:
                    break
                print(f"     {secs(m['t'])} {m.get('state')} still {m.get('still')}s"
                      f" mode={m.get('mode')} toGo={m.get('toGo')} why={m.get('why','')!r}")

    # ---------------------------------------------------------------- the shooting
    if shots:
        print("\n-- the shooting")
        hitn = len(hits)
        dists = sorted(s["dist"] for s in shots)
        print(f"   {len(shots)} shots, {hitn} hits ({hitn * 100 // max(1, len(shots))}%), "
              f"range median {dists[len(dists)//2]:.0f} m, longest {dists[-1]:.0f} m")
        print("   guns: " + ", ".join(f"{g} x{n}" for g, n in
                                      Counter(s.get("gun", "?") for s in shots).most_common(6)))
        dead = [h for h in hits if h.get("dead")]
        print(f"   {len(dead)} men went down")
        stray = [r for r in rows if r["k"] == "stray"]
        if stray:
            print(f"   {len(stray)} stray rounds found a civilian")
    return 0


def street_extras(rows):
    """The two things a car does to the street that are not driving: who it ran down,
    and what the street did to it. Neither is a defect - a man under a bonnet is the
    game working - so they are counted and printed, never failed on."""
    downs = [r for r in rows if r["k"] == "rundown"]
    killed = len([r for r in downs if r.get("dead")])
    engines = [r for r in rows
               if r["k"] == "crewcar" and "engine dead" in str(r.get("what", ""))]
    return downs, killed, engines


def moto(dirpath):
    """The run on two wheels, against what was asked of it: two men walk to the machine,
    ride one pass at a rival, bring it home, get off, and do it again - and every pass
    is ridden without one of the ways that loop can stop for good.

    Judged on DEFECTS, never on who won. A pass that kills nobody is a drive-by being a
    drive-by; a pass nobody could get on, one that never ran out, a machine that never
    came back, a man left on the saddle - those are faults, and none of them may happen.
    """
    import os

    trace = os.path.join(dirpath, "trace.jsonl")
    if not os.path.exists(trace):
        print(f"== {dirpath}")
        print("   NO TRACE - the run never got as far as playing")
        return 3
    rows = load(trace)
    if not rows:
        print("the trace is empty")
        return 2

    mission = [r for r in rows if r["k"] == "mission"]
    rides = [r for r in rows if r["k"] == "driveby"]
    faults = [r for r in rows if r["k"] == "fault"]
    told = [r for r in mission if "what" in r]
    end = told[-1] if told else {}
    state = end.get("who", "?")

    over = [r for r in rides if str(r.get("what", "")).startswith("drive-by over")]
    ordered = [r for r in rides if "ordered on" in str(r.get("what", ""))]
    shots = sum(r.get("shots", 0) for r in over)
    fired = len([r for r in over if r.get("shots", 0) > 0])
    lost = len([r for r in over if r.get("bothup") is False])

    # The ways the loop stops for good, each named where it is raised: the crews'
    # (DemoCrews.Fault) and the lab's (BlockDemoMission.Fault).
    kinds = ("mountstall", "mountrefused", "passstall", "homestall", "raidstall",
             "noshot", "notback")
    broke = [f for f in faults if f.get("fault") in kinds]
    missed = [f for f in faults if f.get("fault") == "mission"]

    # Belt refusals, ATTRIBUTED. The belt is the last safety net under the whole
    # traffic model and one refusal is one vehicle that would have driven through
    # another - but whose refusal it is decides whose bug it is. The machine the crew
    # rides is this verdict's business; a delivery van reversing into a moped at the
    # far end of the quarter is the traffic model's, it happens in runs with no
    # motorcycle of the outfit's in them at all, and counting it here would put a
    # pre-existing fault on the drive-by's tab. Both are printed; only the machine's
    # fails the run.
    belts = [r for r in rows if r["k"] == "belt"]
    ours = len([r for r in belts if r.get("tag") == "crewbike"])
    theirs = len(belts) - ours

    summary_path = os.path.join(dirpath, "summary.json")
    thrown = 0
    if os.path.exists(summary_path):
        try:
            thrown = json.load(open(summary_path, encoding="utf-8")).get("exceptions", 0)
        except Exception:
            pass

    defects = []
    if not ordered:
        defects.append("no drive-by was ever ordered")
    if ordered and not over:
        defects.append(f"{len(ordered)} ordered, none came back")
    if broke:
        defects.append(f"{len(broke)} drive-by faults ({', '.join(sorted({f.get('fault') for f in broke}))})")
    if missed:
        defects.append("; ".join(str(f.get("what", "the run gave up")) for f in missed))
    if ours:
        defects.append(f"{ours} belt refusals by the machine")
    if thrown:
        defects.append(f"{thrown} exceptions")
    ok = not defects

    print(f"== {dirpath}")
    print(f"   {'PASSED' if ok else 'FAULTS: ' + '; '.join(defects)}")
    print(f"   the run ended {state}")
    print(f"   the machine  : {len(over)} of {len(ordered)} passes ridden home, "
          f"{fired} with shots fired, {shots} rounds")
    if theirs:
        print(f"   the quarter  : {theirs} belt refusals by ordinary traffic "
              "(not the drive-by's - see --why)")
    if lost:
        print(f"   the two men  : {lost} pass(es) came back a man short")
    downs, killed, engines = street_extras(rows)
    if downs or engines:
        print(f"   the bonnet   : {len(downs)} run down ({killed} killed), "
              f"{len(engines)} engine(s) shot out")
    return 0 if ok else 1


def verdict(dirpath):
    """The run against what was asked of it: the outfit gets in the car, wipes the
    other mobs out, is never stuck with somewhere to be, and parks at the end."""
    import os

    trace = os.path.join(dirpath, "trace.jsonl")
    if not os.path.exists(trace):
        print(f"== {dirpath}")
        print("   NO TRACE - the run never got as far as playing")
        return 3
    rows = load(trace)
    if not rows:
        print("no trace")
        return 2
    mission = [r for r in rows if r["k"] == "mission"]
    cars = [r for r in rows if r["k"] == "car"]
    crew = [r for r in cars if r.get("tag") == "crew"]
    faults = [r for r in rows if r["k"] == "fault"]
    told = [r for r in mission if "what" in r]
    end = told[-1] if told else {}
    state = end.get("who", "?")
    kills = max((r.get("killed", 0) for r in mission), default=0)

    stuck = [f for f in faults if f.get("fault") == "carstuck"]
    worst_crew = max((abs(r.get("quiet", 0)) for r in crew), default=0)
    traffic = [r for r in cars if r.get("tag") != "crew"]
    worst_traffic = max((r.get("quiet", 0) for r in traffic), default=0)
    belts = len([r for r in rows if r["k"] == "belt"])
    walkstalls = len([f for f in faults if f.get("fault") == "walkstall"])

    # A run is judged on DEFECTS, not on who won. Losing a gunfight three men to two
    # is the game being a game; a car that cannot get anywhere, a junction that jams,
    # two bodies in the same place, a crew that cannot get back into its own car - those
    # are faults, and none of them may happen.
    broke = [f for f in faults
             if f.get("fault") in ("nopark", "carstuck")
             or (f.get("fault") == "mission" and "wiped out" not in str(f.get("what", "")))]
    summary_path = os.path.join(dirpath, "summary.json")
    thrown = 0
    if os.path.exists(summary_path):
        try:
            thrown = json.load(open(summary_path, encoding="utf-8")).get("exceptions", 0)
        except Exception:
            pass
    defects = []
    if broke:
        defects.append(f"{len(broke)} mission faults ({', '.join(sorted({f.get('fault') for f in broke}))})")
    if belts:
        defects.append(f"{belts} belt refusals")
    if worst_traffic >= 90:
        defects.append(f"a car stood {worst_traffic:.0f}s")
    if thrown:
        defects.append(f"{thrown} exceptions")
    ok = not defects

    print(f"== {dirpath}")
    print(f"   {'PASSED' if ok else 'FAULTS: ' + '; '.join(defects)}")
    print(f"   the run ended {state}, {kills} crews down")
    print(f"   the crew car : stood still {worst_crew:.0f}s at worst, {len(stuck)} spells counted stuck")
    print(f"   the traffic  : worst car stood {worst_traffic:.0f}s, {belts} belt refusals")
    downs, killed, engines = street_extras(rows)
    if downs or engines:
        print(f"   the bonnet   : {len(downs)} run down ({killed} killed), "
              f"{len(engines)} engine(s) shot out")
    # a run fought on foot counts heads instead of wheels: who was left standing
    war = [r for r in mission if "ours" in r]
    tail = ""
    if war:
        # the field as it stood at its fullest, and as it ended: the rows before the
        # crews are dealt count nobody, and the row after the last man falls is never
        # written - so it is the most and the least either side ever had
        tail = (f"; {min(r['ours'] for r in war)} of ours left of "
                f"{max(r['ours'] for r in war)}, "
                f"{min(r['theirs'] for r in war)} of theirs left of "
                f"{max(r['theirs'] for r in war)}")
    print(f"   on foot      : {walkstalls} men flagged stuck{tail}")
    if told:
        for r in told[-6:]:
            print(f"   {secs(r['t'])} {r.get('who','')}: {r.get('what','')}")
    for f in faults:
        if f.get("fault") in ("carstuck", "nopark", "nokill", "mission"):
            print(f"   FAULT {secs(f['t'])} {f.get('fault')}: {f.get('what')}")
    return 0 if ok else 1



def freeway(dirpath):
    """The motorway against what it is for: cars get on it, pay at the plaza, cross
    to the other quarter and get off. Nothing here judges the look of it - it counts
    journeys, payments and the faults the gate and the driving code flag."""
    import os

    trace = os.path.join(dirpath, "trace.jsonl")
    if not os.path.exists(trace):
        print(f"== {dirpath}")
        print("   NO TRACE - the run never got as far as playing")
        return 3
    rows = load(trace)
    if not rows:
        print("no trace")
        return 2

    cars = [r for r in rows if r["k"] == "car"]
    faults = [r for r in rows if r["k"] == "fault"]
    deck = [r for r in rows if r["k"] == "deck"]
    tolls = [r for r in rows if r["k"] == "toll"]
    belts = len([r for r in rows if r["k"] == "belt"])

    # a JOURNEY: on at one end of the line, off at the other. The two interchanges
    # are hundreds of metres apart, so a crossing is not a matter of opinion - it is
    # the distance between where a car joined the road and where it left it.
    joined = {}
    crossings = []
    rode = []
    for r in deck:
        who = r.get("id")
        p = r.get("p") or [0, 0]
        if r.get("what") == "on":
            joined[who] = (r["t"], p)
            continue
        was = joined.pop(who, None)
        if was is None:
            continue
        t0, p0 = was
        far = max(abs(p[0] - p0[0]), abs(p[1] - p0[1]))
        rode.append(far)
        if far >= 300:
            crossings.append((who, r["t"] - t0, far))

    paid = len(tolls)
    waits = [r.get("wait", 0) for r in tolls]
    # which ways on were used: the ramps stand hundreds of metres apart down the
    # line, so the places cars joined, bucketed at 200 m, ARE the interchanges. The
    # line's own axis is whichever the joins are spread widest along.
    ons = [(r.get("p") or [0, 0]) for r in deck if r.get("what") == "on"]
    ends = Counter()
    if ons:
        spread_x = max(p[0] for p in ons) - min(p[0] for p in ons)
        spread_z = max(p[1] for p in ons) - min(p[1] for p in ons)
        axis = 0 if spread_x >= spread_z else 1
        for p in ons:
            ends[round(p[axis] / 200.0)] += 1

    traffic = [r for r in cars if r.get("tag") != "crew"]
    worst_traffic = max((r.get("quiet", 0) for r in traffic), default=0)
    summary_path = os.path.join(dirpath, "summary.json")
    thrown = 0
    if os.path.exists(summary_path):
        try:
            thrown = json.load(open(summary_path, encoding="utf-8")).get("exceptions", 0)
        except Exception:
            pass

    broke = [f for f in faults if f.get("fault") in ("tollrun", "tollstuck", "carstuck", "nopark")]
    defects = []
    if broke:
        defects.append(f"{len(broke)} faults ({', '.join(sorted({f.get('fault') for f in broke}))})")
    if belts:
        defects.append(f"{belts} belt refusals")
    if worst_traffic >= 90:
        defects.append(f"a car stood {worst_traffic:.0f}s")
    if thrown:
        defects.append(f"{thrown} exceptions")
    if not crossings:
        defects.append("nobody crossed: the freeway carried no journey end to end")
    if paid == 0:
        defects.append("nobody paid: the toll plaza took nothing")
    if len(ends) < 2:
        defects.append(f"only {len(ends)} way(s) on were used - one end of the road is dead")
    ok = not defects

    print(f"== {dirpath}")
    print(f"   {'PASSED' if ok else 'FAULTS: ' + '; '.join(defects)}")
    print(f"   the deck   : {len(deck)} on/off, {len(crossings)} crossings, "
          f"longest ride {max(rode) if rode else 0:.0f} m")
    print(f"   the plaza  : {paid} paid, longest wait {max(waits) if waits else 0:.1f}s, "
          f"average {sum(waits)/len(waits) if waits else 0:.1f}s")
    print(f"   the traffic: worst car stood {worst_traffic:.0f}s, {belts} belt refusals")
    by_kind = Counter(f.get("fault", "?") for f in faults)
    if by_kind:
        print("   faults     : " + ", ".join(f"{k} x{n}" for k, n in by_kind.most_common(8)))
    for f in faults:
        if f.get("fault") in ("tollrun", "tollstuck", "carstuck"):
            print(f"   FAULT {secs(f['t'])} {f.get('fault')}: {f.get('what')}")
    return 0 if ok else 1


# Every fault kind emitted by CrewAudit. Keep this explicit so a report remains
# readable without importing/parsing game source; walkstall is emitted by the trace
# watchdog and has its own three-strike rule below.
CREW_FAULTS = ("teleport", "offcity", "strayman", "singlefile",
               "aimlow", "zebrastuck", "runnerchase", "roadwalk",
               "skate", "leftbehind", "noaim", "firewalk",
               "formationheading", "formationspread", "proppenetration",
               "routestall", "routeorbit", "routeoverlap",
               # EPIC 28: a round from the open with a free flank on the fire line,
               # and the ambush's own five
               "openfire", "noambush", "nolurk", "seenfirst", "openambush", "nospring")


def first_from_cover(rows):
    """The number EPIC 28 is tuned on: of the men who fired at all, what share had
    a flank in hand when their FIRST round left.

    Read off the `shot` row's `fromcover`, which is the SHOOTER's own cover state at
    the moment the round left (the row's older `cover` field is about the man being
    shot AT and answers a different question). Only the first round each man fires is
    counted: what is being measured is the ORDER of the two things, not how much of the
    fight was spent behind something - CoverWatch already reports that."""
    first_shot, from_cover = set(), set()
    for r in rows:
        if r.get("k") != "shot":
            continue
        who = r.get("from")
        if not who or who in first_shot:
            continue
        first_shot.add(who)
        if r.get("fromcover"):
            from_cover.add(who)
    return len(from_cover), len(first_shot)


def retargets(rows):
    """EPIC 33's first yardstick: how the closer-threat rule behaved over a run.

    Three numbers, and the third is the one that matters. `switches` is how many
    times a man turned onto a nearer enemy - a fight with none at all in a furnished
    street usually means the rule never fired. `kept` is how many of those left him
    in the cover he was already in (D1: a switch must not stand a man up at the
    moment the danger is nearest). `flicker` counts a man turning back onto a mark he
    held less than two seconds earlier, which is the A/B/A/B the margin and the dwell
    exist to make impossible: it is expected to be zero, and one of them is a bug and
    not a tuning matter."""
    switches = kept = flicker = 0
    last = {}          # man -> (the mark he left, the mark he took, when)
    for r in rows:
        if r.get("k") != "switch":
            continue
        switches += 1
        if r.get("kept"):
            kept += 1
        who, left, onto, at = (r.get("who"), r.get("left"), r.get("onto"),
                               r.get("t", 0))
        # A FLICKER IS A REVERSAL, and only a reversal: this switch undoes the last
        # one this man made. Counting every return to a mark he once held was wrong
        # and said so in a CoverDemo run - the uncovered pass (AIM-004) deliberately
        # moves a duplicate shooter onto the man somebody abandoned, so a man can
        # legitimately end up back on an old mark without the closer-threat rule
        # having oscillated at all. What the margin and the dwell make impossible is
        # A to B and straight back to A, and that is what this counts.
        was = last.get(who)
        if was and was[1] == left and was[0] == onto and at - was[2] < 2.0:
            flicker += 1
        last[who] = (left, onto, at)
    return switches, kept, flicker


def scatter(rows):
    """EPIC 33's second yardstick: the mean angle a MISSED round left the aim line
    at, in degrees, by the shooter's Combat half-steps. The monotonicity acceptance
    (9) read off the run rather than off the table - a build whose cone stopped
    reaching the round would print the same number in every bucket."""
    tally = {}
    for r in rows:
        # a round that went into the tin is not a scattered round: it has a direction
        # (the hole) and no cone at all, and averaging it in would flatten the reading
        if (r.get("k") != "shot" or r.get("hit") or r.get("tin")
                or not r.get("cone") or "off" not in r):
            continue
        bucket = tally.setdefault(r.get("combat", 6), [0, 0.0])
        bucket[0] += 1
        bucket[1] += float(r["off"])
    return {hs: (n, total / n) for hs, (n, total) in tally.items() if n}


def crew(dirpath):
    """The walk and the fight against the crews' own rules (CrewAudit): nobody
    snaps, nobody leaves the floor, nobody strays off his crew or queues down one
    line, no round leaves a lowered gun, nobody is left on a zebra, and nobody
    chases a runner past the man still shooting. The mission's own faults (a leg
    not walked, a march that never arrived) fail the run the same way."""
    import os

    trace = os.path.join(dirpath, "trace.jsonl")
    if not os.path.exists(trace):
        print(f"== {dirpath}")
        print("   NO TRACE - the run never got as far as playing")
        return 3
    rows = load(trace)
    if not rows:
        print("no trace")
        return 2

    faults = [r for r in rows if r["k"] == "fault"]
    broke = [f for f in faults if f.get("fault") in CREW_FAULTS]
    mission = [f for f in faults
               if f.get("fault") == "mission" and "wiped out" not in str(f.get("what", ""))]
    stalls = [f for f in faults if f.get("fault") == "walkstall" and f.get("tag") == "crew"]
    switched, kept, flickered = retargets(rows)
    told = [r for r in rows if r["k"] == "mission" and "what" in r]
    end = told[-1] if told else {}

    thrown = 0
    summary_path = os.path.join(dirpath, "summary.json")
    if os.path.exists(summary_path):
        try:
            thrown = json.load(open(summary_path, encoding="utf-8")).get("exceptions", 0)
        except Exception:
            pass

    defects = []
    if broke:
        by = Counter(f.get("fault") for f in broke)
        defects.append(", ".join(f"{n} {kind}" for kind, n in by.most_common()))
    if mission:
        defects.append(f"{len(mission)} mission faults")
    # one man boxed in once is the street being a street; a pattern is a fault
    if len(stalls) >= 3:
        defects.append(f"{len(stalls)} crew walkstalls")
    if thrown:
        defects.append(f"{thrown} exceptions")
    # A man turning back onto a mark he had two seconds ago is the ping-pong the
    # margin and the dwell were built to make impossible (EPIC 33, acceptance 4).
    if flickered:
        defects.append(f"{flickered} target flickers")
    ok = not defects

    print(f"== {dirpath}")
    print(f"   {'PASSED' if ok else 'FAULTS: ' + '; '.join(defects)}")
    print(f"   the run ended {end.get('who', '?')}: {end.get('what', '?')}")
    shots = len([r for r in rows if r["k"] == "shot"])
    hits = [r for r in rows if r["k"] == "hit"]
    print(f"   the fight    : {shots} shots, {len(hits)} hits, "
          f"{len([h for h in hits if h.get('dead')])} men down")
    print(f"   the walk     : {len(stalls)} crew walkstalls")
    # EPIC 28's yardstick: the share of men whose FIRST round left from cover. Judged
    # over the thirty runs of a soak and never off one of them (tally.sh sums these).
    covered, fired = first_from_cover(rows)
    print(f"   cover first  : {covered}/{fired} men opened up from behind something"
          + (f" ({100.0 * covered / fired:.0f}%)" if fired else ""))
    # EPIC 33's yardstick: the retarget and the scatter. Judged over a soak like the
    # rest; one run says only whether the rule fired at all.
    print(f"   the retarget : {switched} switches onto a closer man, "
          f"{kept} kept the flank, {flickered} flickered")
    spread = scatter(rows)
    if spread:
        print("   the scatter  : " + ", ".join(
            f"{hs / 2:g} star {mean:.1f} deg over {n}"
            for hs, (n, mean) in sorted(spread.items())))
    for f in (broke + mission)[:10]:
        print(f"   FAULT {secs(f['t'])} {f.get('who', f.get('id', ''))} "
              f"{f.get('fault')}: {f.get('what')}")
    return 0 if ok else 1


def cover_route(dirpath):
    """Strict furnished-street gate: one direct attack on Falcone, then Santoro,
    every original attacker proving contact/progress on both legs, with no tagged
    route/crew fault, no tolerated stall, and a clean completed harness summary."""
    trace = os.path.join(dirpath, "trace.jsonl")
    summary_path = os.path.join(dirpath, "summary.json")
    if not os.path.exists(trace) or not os.path.exists(summary_path):
        print(f"== {dirpath}")
        print("   NO RUN - trace or summary is missing")
        return 3
    rows = load(trace)
    if not rows:
        print(f"== {dirpath}\n   NO RUN - trace is empty")
        return 3
    try:
        summary = json.load(open(summary_path, encoding="utf-8"))
    except Exception as exc:
        print(f"== {dirpath}\n   NO RUN - unreadable summary: {exc}")
        return 3

    events = [r for r in rows if r.get("k") == "coverroute"]
    ordered = [r.get("who") for r in events
               if r.get("what") == "ordered direct attack"]
    down = [r.get("who") for r in events if r.get("what") == "down"]
    complete = any(r.get("who") == "complete" and r.get("what") == "2 mobs down"
                   for r in events)
    faults = [r for r in rows if r.get("k") == "fault"]
    # This gate must follow ownership, not a frozen name whitelist. A newly added
    # CrewAudit or CoverRouteMission fault is a failure on its first run too.
    scoped_faults = [r for r in faults
                     if r.get("tag") in ("crew", "coverroute")]

    snapshots = {r.get("id") for r in events
                 if r.get("what") == "member snapshot" and r.get("id") is not None}
    proved = defaultdict(set)
    for row in events:
        if row.get("what") == "member proved" and row.get("id") is not None:
            proved[row.get("id")].add(row.get("leg"))
    missing_proof = [(member, leg) for member in sorted(snapshots)
                     for leg in (1, 2) if leg not in proved.get(member, set())]

    defects = []
    if ordered != ["Falcone", "Santoro"]:
        defects.append("orders were " + repr(ordered))
    if down != ["Falcone", "Santoro"]:
        defects.append("downs were " + repr(down))
    if not complete:
        defects.append("no 2-mob completion")
    if not snapshots:
        defects.append("no original-attacker snapshot")
    if missing_proof:
        defects.append("missing member proof " + repr(missing_proof))
    if scoped_faults:
        by = Counter(r.get("fault") for r in scoped_faults)
        defects.append(", ".join(f"{n} {kind}" for kind, n in by.most_common()))
    if summary.get("why") != "done":
        defects.append("run ended " + repr(summary.get("why")))
    if summary.get("errors", 0):
        defects.append(f"{summary.get('errors')} errors")
    if summary.get("exceptions", 0):
        defects.append(f"{summary.get('exceptions')} exceptions")
    ok = not defects

    print(f"== {dirpath}")
    print("   " + ("PASSED" if ok else "FAULTS: " + "; ".join(defects)))
    print(f"   direct orders: {ordered}")
    print(f"   crews down   : {down}; complete={complete}")
    print(f"   member proof : {len(snapshots)} original, "
          f"{sum(len(proved.get(member, set()) & {1, 2}) for member in snapshots)}/"
          f"{len(snapshots) * 2} leg proofs")
    print(f"   route faults : {len(scoped_faults)}, summary={summary.get('why')!r}, "
          f"errors={summary.get('errors', 0)}/{summary.get('exceptions', 0)}")
    for fault in scoped_faults[:10]:
        print(f"   FAULT {secs(fault.get('t', 0))} {fault.get('who', '')} "
              f"{fault.get('fault')}: {fault.get('what')}")
    return 0 if ok else 1


def story(dirpath, every=2.0):
    """The run as a story: what the lab ordered, what the crew car did, who shot whom."""
    import os

    rows = load(os.path.join(dirpath, "trace.jsonl"))
    print(f"== {dirpath}: the run second by second")
    at = -1e9
    for r in rows:
        k = r["k"]
        if k == "mission" and "what" in r:
            print(f"{secs(r['t'])} ORDER   {r.get('who','')}: {r.get('what')}")
        elif k == "fault" and r.get("tag") == "mission":
            print(f"{secs(r['t'])} FAULT   {r.get('fault')}: {r.get('what')}")
        elif k == "fault" and r.get("tag") == "crew":
            print(f"{secs(r['t'])} FAULT   car {r.get('fault')}: {r.get('what')} why={r.get('why','')!r}")
        elif k == "man" and r.get("tag") == "crew":
            print(f"{secs(r['t'])} car     {r.get('what')} v={r.get('v')} road={r.get('road')} s={r.get('s')}")
        elif k == "car" and r.get("tag") == "crew" and r["t"] - at >= every:
            at = r["t"]
            print(f"{secs(r['t'])} car     v={r['v']:5.2f} want={r['want']:5.2f} {r['man']:7} road={r['road']:3} "
                  f"s={r['s']:6.1f} d={r['d']:5.1f} h={r['h']:2} prof={r['prof']:8} why={r.get('why','')[:50]}")
    shots = [r for r in rows if r["k"] == "shot"]
    if shots:
        by = Counter((s.get("fac"), s.get("atfac")) for s in shots)
        print("   shots: " + ", ".join(f"faction {a} at {b}: {n}" for (a, b), n in by.most_common()))
        hits = [r for r in rows if r["k"] == "hit"]
        print(f"   {len(hits)} hits, {len([h for h in hits if h.get('dead')])} men down")


def cover_tally(dirpath):
    """EPIC 28's number over a whole soak: of every man who fired at all, in every run
    under this directory, what share had a flank in hand when his FIRST round left.

    A RATE OVER RUNS AND NEVER A SEED. The cover code draws from the shared Random, so
    one seed against another says nothing; this is the figure the epic is tuned on."""
    import glob
    import os

    covered = fired = runs = 0
    for d in sorted(glob.glob(os.path.join(dirpath, "run-*"))) or [dirpath]:
        trace = os.path.join(d, "trace.jsonl")
        if not os.path.exists(trace):
            continue
        runs += 1
        c, n = first_from_cover(load(trace))
        covered += c
        fired += n
    share = f" ({100.0 * covered / fired:.0f}%)" if fired else ""
    print(f"== cover first: {covered}/{fired} men over {runs} runs{share}")
    return 0


YARD_METRES = 60.0   # RoadDemoBuilder.StationYardMetres: a station's own ground


def belt_split(rows, police_cars):
    """Whose refusal each belt row is. 'yard' and 'road' are police-on-police, told
    apart by whether it happened within a house's reach of a 'precinct' row (the force
    writes one per house once the trace is open); 'other' is any other refusal with a
    police car on either side; 'crew' is the outfit's own machines; the rest is
    civilian traffic, which is the traffic model's ticket and never the depot's."""
    houses = [r for r in rows if r["k"] == "precinct"]
    yards = [(r.get("p") or [0.0, 0.0]) for r in houses]
    # every car a house owns, by id - a car that never left its bay wrote no car row,
    # and a civilian driving into it must still be the law's business
    police_cars = set(police_cars)
    for h in houses:
        for u in str(h.get("units", "")).split(","):
            if u.strip().isdigit():
                police_cars.add(int(u))
    out = {"yard": 0, "road": 0, "other": 0, "crew": 0, "civil": 0, "houses": houses}
    for r in rows:
        if r["k"] != "belt":
            continue
        hit = str(r.get("hit", ""))
        hit_id = int(hit[4:]) if hit.startswith("car ") and hit[4:].isdigit() else -1
        mine = r.get("tag") == "police"
        theirs = hit_id in police_cars
        if mine and theirs:
            p = r.get("p") or [0.0, 0.0]
            in_yard = any((p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2 <= YARD_METRES ** 2
                          for q in yards)
            out["yard" if in_yard else "road"] += 1
        elif mine or theirs:
            out["other"] += 1
        elif r.get("tag") in ("crew", "crewbike"):
            out["crew"] += 1
        else:
            out["civil"] += 1
    return out


def core(dirpath, want=""):
    """THE FORCED SCENARIOS' READER (EPIC 31 NIGHT-013).

    The mini core run judged as a CITY rather than as a piece of driving: which houses
    thought and at what tier, what changed hands, who fired at whom, what the law did -
    plus the ordinary rules that apply to every run (the harness finished, nothing
    threw, no crew fault epidemic).

    `want` is a comma-separated list of demands the caller's scenario makes of it, so
    one reader serves all five and each scenario's verdict is written where the
    scenario is:

        expand      at least one house fired tier 7 (expand)
        turf        at least one block changed leader
        allthink    every gang that stands in the city had a turn of mind
        war         some pair reached a war intent AND both sides fired
        short       a short envelope was paid, somebody left, the safe never went under
        police      police cars ran, nobody stood 90 s, no belt refusal
        law         a complaint rang, a car came, a verdict landed (needs NIGHT-009)
    """
    import os

    trace = os.path.join(dirpath, "trace.jsonl")
    summary_path = os.path.join(dirpath, "summary.json")
    if not os.path.exists(trace) or not os.path.exists(summary_path):
        print(f"== {dirpath}")
        print("   NO RUN - the editor never played it")
        return 3
    summary = json.load(open(summary_path, encoding="utf-8"))
    rows = load(trace)

    houses = [r for r in rows if r["k"] == "house"]
    turf = [r for r in rows if r["k"] == "turf"]
    shots = [r for r in rows if r["k"] == "shot"]
    faults = [r for r in rows if r["k"] == "fault"]
    crew_faults = [r for r in faults if r.get("tag") in ("crew", "coverroute")]
    cars = [r for r in rows if r["k"] == "car"]
    police_cars = {r.get("id") for r in cars if r.get("tag") == "police"}
    belts = len([r for r in rows if r["k"] == "belt"])
    split = belt_split(rows, police_cars)
    worst_stand = max((r.get("quiet", 0) or 0 for r in cars if r.get("tag") != "crew"),
                      default=0)
    complaints = [r for r in rows if r["k"] == "complaint"]
    courts = [r for r in rows if r["k"] == "court"]

    thinkers = {r.get("gang") for r in houses}
    tiers = Counter(r.get("tier") for r in houses)
    firing = {r.get("fac") for r in shots} | {r.get("atfac") for r in shots}
    war_rows = [r for r in houses
                if "war" in str(r.get("intent", "")).lower()
                or "war" in str(r.get("why", "")).lower()]
    short_rows = [r for r in rows
                  if "short" in str(r.get("what", "")).lower()
                  or "short" in str(r.get("why", "")).lower()]
    safes = [r.get("safe") for r in houses if isinstance(r.get("safe"), int)]

    defects = []
    if summary.get("why") != "done":
        defects.append("the run ended " + repr(summary.get("why")))
    if summary.get("errors", 0):
        defects.append(f"{summary.get('errors')} errors")
    if summary.get("exceptions", 0):
        defects.append(f"{summary.get('exceptions')} exceptions")

    demands = [w.strip() for w in want.split(",") if w.strip()]
    if "expand" in demands and 7 not in tiers:
        defects.append("no house reached tier 7 (expand)")
    if "turf" in demands and not turf:
        defects.append("no block changed hands")
    if "allthink" in demands:
        # every gang that fired, held ground or thought at all is a gang that stands
        standing = thinkers | {g for g in firing if isinstance(g, int) and g >= 0}
        missing = sorted(g for g in standing if g not in thinkers)
        if missing:
            defects.append(f"{len(missing)} standing gang(s) never thought: {missing[:8]}")
    if "war" in demands:
        if not war_rows:
            defects.append("no house ever formed a war intent")
        elif len({r.get("fac") for r in shots}) < 2:
            defects.append("only one side ever fired")
    if "short" in demands:
        if not short_rows:
            defects.append("no short envelope was paid")
        if any(s < 0 for s in safes):
            defects.append("a safe read below zero")
    if "police" in demands:
        if not police_cars:
            defects.append("no police car ever ran")
        if worst_stand >= 90:
            defects.append(f"a car stood {worst_stand:.0f}s")
        # ATTRIBUTED. A police car driving into a police car in its own yard is the
        # yard's fault (the depot ticket); anywhere else it is the patrol's; a police
        # car in any other collision is still the law's business. Two civilians at
        # the far end of town are the traffic model's, and go on that ticket, not this.
        if split["yard"]:
            defects.append(f"{split['yard']} police-on-police belt refusals in a yard")
        if split["road"]:
            defects.append(f"{split['road']} police-on-police belt refusals on the road")
        if split["other"]:
            defects.append(f"{split['other']} belt refusals with a police car")
    if "law" in demands:
        if not complaints:
            defects.append("no complaint was ever rung in")
        if not courts:
            defects.append("no verdict ever landed")

    ok = not defects
    print(f"== {dirpath}")
    print("   " + ("PASSED" if ok else "FAULTS: " + "; ".join(defects)))
    print(f"   the city    : {len(houses)} turns of mind by {len(thinkers)} house(s), "
          f"tiers {dict(sorted(tiers.items(), key=lambda kv: str(kv[0])))}")
    print(f"   the turf    : {len(turf)} block(s) changed hands"
          + ("; " + ", ".join(f"{r.get('block')} {r.get('from')}->{r.get('to')} "
                              f"({r.get('state')})" for r in turf[:4]) if turf else ""))
    print(f"   the street  : {len(shots)} shots by factions "
          f"{sorted(g for g in {r.get('fac') for r in shots} if g is not None)}, "
          f"{len(police_cars)} police car(s), worst stand {worst_stand:.0f}s, {belts} belt")
    if belts:
        print(f"   the belt    : police-on-police {split['yard']} in a yard, "
              f"{split['road']} on the road; {split['other']} with a police car; "
              f"{split['crew']} crew; {split['civil']} civilian")
    for r in split["houses"]:
        print(f"   the house   : {r.get('name')} owns {r.get('cars')} car(s), "
              f"{r.get('bodies')} in the yard")
    print(f"   the law     : {len(complaints)} complaint(s), {len(courts)} verdict(s)")
    if crew_faults:
        by = Counter(r.get("fault") for r in crew_faults)
        print("   crew faults : " +
              ", ".join(f"{n} {kind}" for kind, n in by.most_common(8)))
    print(f"   summary     : why={summary.get('why')!r}, "
          f"errors={summary.get('errors', 0)}/{summary.get('exceptions', 0)}, "
          f"sim={summary.get('sim')}s")
    return 0 if ok else 1


def court(dirpath, expected):
    """ROAD-006's strict per-scenario reader.

    The mission row names both the stable personnel id and the exact live walker id.
    That lets this gate reject a transfer that only moved ledger state while a generic
    pedestrian happened to walk nearby. Scenarios which reach either door must show
    that same walker in the ordinary pedestrian trace during the matching carriage
    stage; every scenario must reach its own explicit Done row with a clean harness.
    """
    trace_path = os.path.join(dirpath, "trace.jsonl")
    summary_path = os.path.join(dirpath, "summary.json")
    if not os.path.exists(trace_path) or not os.path.exists(summary_path):
        print(f"== {dirpath}")
        print("   NO RUN - trace or summary is missing")
        return 3
    try:
        summary = json.load(open(summary_path, encoding="utf-8"))
    except Exception as exc:
        print(f"== {dirpath}\n   NO RUN - unreadable summary: {exc}")
        return 3
    rows = load(trace_path)
    mission = [r for r in rows
               if r.get("k") == "mission" and r.get("scenario") == expected]
    scoped_faults = [r for r in rows if r.get("k") == "fault" and
                     (r.get("tag") == "court-transfer" or
                      r.get("fault") == "court-transfer")]
    stages = Counter(r.get("stage") for r in mission if r.get("stage"))
    states = Counter(r.get("state") for r in mission if r.get("state"))
    done = [r for r in mission if r.get("state") == "Done"]
    failed = [r for r in mission if r.get("state") == "Failed"]
    walkers = {r.get("walker") for r in mission
               if isinstance(r.get("walker"), int) and r.get("walker") >= 0}
    prisoner_ids = {r.get("prisoner") for r in mission
                    if isinstance(r.get("prisoner"), int) and r.get("prisoner") >= 0}
    ped_rows = [r for r in rows if r.get("k") == "ped" and r.get("id") in walkers]
    jeopardy = [r for r in rows if r.get("k") == "jeopardy" and
                r.get("prisoner") in prisoner_ids]

    def has_exact_walk(stage):
        stage_rows = [r for r in mission if r.get("stage") == stage]
        return bool(stage_rows) and any(
            p.get("id") == m.get("walker") and
            float(p.get("pace", 0)) > 0.05 and
            abs(float(p.get("t", 0)) - float(m.get("t", 0))) <= 2.5
            for m in stage_rows for p in ped_rows)

    defects = []
    if summary.get("why") != "done":
        defects.append("the run ended " + repr(summary.get("why")))
    if summary.get("errors", 0):
        defects.append(f"{summary.get('errors')} errors")
    if summary.get("exceptions", 0):
        defects.append(f"{summary.get('exceptions')} exceptions")
    if not mission:
        defects.append(f"no mission rows for scenario {expected}")
    if failed:
        defects.append("the scenario entered Failed")
    if scoped_faults:
        defects.append(f"{len(scoped_faults)} court-transfer fault row(s)")
    if not done:
        defects.append("no explicit Done verdict")

    # Every case except the pre-pickup bombing calls the prisoner out of the cells.
    # Only these two cases deliberately run all the way to the courthouse door before
    # their own verdict is decided. Scenario 11 has no courthouse by definition: its
    # one promised visible leg is the walk out of the station.
    if expected != 5 and not has_exact_walk("WalkingOut"):
        defects.append("the named prisoner has no exact walking-out trace")
    if expected in (9, 10) and not has_exact_walk("WalkingIn"):
        defects.append("the named prisoner has no exact walking-in trace")

    ok = not defects
    last = done[-1] if done else (mission[-1] if mission else {})
    print(f"== {dirpath}")
    print("   " + ("PASSED" if ok else "FAULTS: " + "; ".join(defects)))
    print(f"   scenario {expected:02d}: state={last.get('state', '?')}, "
          f"stage={last.get('stage', '?')}, {last.get('what', '?')}")
    print("   carriage: " + (", ".join(f"{k} x{n}" for k, n in stages.items())
                              if stages else "no stages recorded"))
    print(f"   exact body: walker(s) {sorted(walkers)}, {len(ped_rows)} pedestrian rows; "
          f"faults={len(scoped_faults)}, errors="
          f"{summary.get('errors', 0)}/{summary.get('exceptions', 0)}")
    if jeopardy:
        print(f"   jeopardy  : {len(jeopardy)} capped roll(s), "
              f"{sum(1 for r in jeopardy if r.get('hit'))} hit")
    for fault in (failed + scoped_faults)[:6]:
        print(f"   FAULT {secs(fault.get('t', 0))} {fault.get('what', '')}")
    return 0 if ok else 1


if __name__ == "__main__":
    args = [a for a in sys.argv[1:]]
    if not args:
        print(__doc__)
        sys.exit(1)
    path = args[0]
    car = None
    if "--car" in args:
        car = int(args[args.index("--car") + 1])
    if "--moto" in args:
        sys.exit(moto(path))
    if "--verdict" in args:
        sys.exit(verdict(path))
    if "--crew" in args:
        sys.exit(crew(path))
    if "--cover-route" in args:
        sys.exit(cover_route(path))
    if "--cover-tally" in args:
        sys.exit(cover_tally(path))
    if "--core" in args:
        at = args.index("--core")
        demands = args[at + 1] if len(args) > at + 1 and not args[at + 1].startswith("--") else ""
        sys.exit(core(path, demands))
    if "--court" in args:
        at = args.index("--court")
        if len(args) <= at + 1:
            print("--court needs a scenario number from 1 to 12")
            sys.exit(2)
        expected = int(args[at + 1])
        if expected < 1 or expected > 12:
            print("--court needs a scenario number from 1 to 12")
            sys.exit(2)
        sys.exit(court(path, expected))
    if "--freeway" in args:
        sys.exit(freeway(path))
    if "--story" in args:
        sys.exit(story(path))
    sys.exit(report(path, only_car=car, show_why="--why" in args))
