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
               "formationheading", "formationspread", "proppenetration")


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
    ok = not defects

    print(f"== {dirpath}")
    print(f"   {'PASSED' if ok else 'FAULTS: ' + '; '.join(defects)}")
    print(f"   the run ended {end.get('who', '?')}: {end.get('what', '?')}")
    shots = len([r for r in rows if r["k"] == "shot"])
    hits = [r for r in rows if r["k"] == "hit"]
    print(f"   the fight    : {shots} shots, {len(hits)} hits, "
          f"{len([h for h in hits if h.get('dead')])} men down")
    print(f"   the walk     : {len(stalls)} crew walkstalls")
    for f in (broke + mission)[:10]:
        print(f"   FAULT {secs(f['t'])} {f.get('who', f.get('id', ''))} "
              f"{f.get('fault')}: {f.get('what')}")
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
    if "--freeway" in args:
        sys.exit(freeway(path))
    if "--story" in args:
        sys.exit(story(path))
    sys.exit(report(path, only_car=car, show_why="--why" in args))
