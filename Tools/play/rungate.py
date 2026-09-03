"""Did this run actually RUN what it was asked to run?

    python Tools/play/rungate.py <run dir> [--mission] [--min-mission-rows N]

night.sh asks analyze.py whether the driving/fighting was any good. That question is
only worth asking of a run that finished; analyze.py's `--verdict` and `--moto` read
the trace and the exception count but never look at `summary.json`'s `why` or its
`errors`, so a run that ended on the wall clock, or whose `-hSet` override never
landed and left the mission unbuilt, came back PASSED off an ambient trace of traffic.
This is the gate in front of that: it says nothing about quality and everything about
whether there is a run here at all.

Exits 0 (there is a run), 1 (there is a run and it is bad), 3 (there is no run), and
prints one line saying which.
"""

import json
import os
import sys


def main():
    if len(sys.argv) < 2:
        print("rungate: no run directory given")
        return 3
    path = sys.argv[1]
    want_mission = "--mission" in sys.argv
    least = 1
    if "--min-mission-rows" in sys.argv:
        least = int(sys.argv[sys.argv.index("--min-mission-rows") + 1])
    wanted_sets = ""
    if "--sets" in sys.argv:
        wanted_sets = sys.argv[sys.argv.index("--sets") + 1]

    summary_path = os.path.join(path, "summary.json")
    trace_path = os.path.join(path, "trace.jsonl")
    if not os.path.exists(summary_path):
        print("NO RUN: no summary.json - the editor never played it")
        return 3
    if not os.path.exists(trace_path):
        print("NO RUN: no trace.jsonl - the harness wrote no rows")
        return 3

    try:
        summary = json.load(open(summary_path, encoding="utf-8"))
    except Exception as err:
        print(f"NO RUN: summary.json will not parse ({err})")
        return 3

    bad = []
    # "done" is the harness saying it played every second it was asked for. Anything
    # else - a wall-clock abort, an error stop - is a run that was cut off, and the
    # trace it left is a fragment, not a verdict.
    if summary.get("why") != "done":
        bad.append(f"the run ended {summary.get('why')!r}, not 'done'")
    if summary.get("errors", 0):
        bad.append(f"{summary.get('errors')} errors in the log")
    if summary.get("exceptions", 0):
        bad.append(f"{summary.get('exceptions')} exceptions")

    # EVERY OVERRIDE THE RUN WAS ASKED FOR ACTUALLY LANDED. PlayHarness.ApplySet writes
    # one line per -hSet into harness.log - "set Type.field=value on N", or "has no
    # field" / "nothing of type T in the scene" when it could not - and that log is the
    # only place the run says what it was really configured with. A renamed knob, a
    # scene without the builder in it, a mode flag the caller's own --sets replaced:
    # every one of them plays an ordinary session and leaves a trace the quality
    # verdicts are perfectly happy with. So the run's own record is read back.
    if wanted_sets:
        log_path = os.path.join(path, "harness.log")
        log = ""
        if os.path.exists(log_path):
            log = open(log_path, encoding="utf-8", errors="replace").read()
        missing = []
        for one in wanted_sets.split(";"):
            one = one.strip()
            if not one or "=" not in one:
                continue
            landed = False
            for line in log.splitlines():
                if line.startswith(f"[harness] set {one} on "):
                    try:
                        landed = int(line.rsplit(" on ", 1)[1]) > 0
                    except ValueError:
                        landed = False
                    break
            if not landed:
                missing.append(one)
        if missing:
            bad.append("the run was never configured as asked - " +
                       str(len(missing)) + " override(s) did not land: " +
                       ", ".join(missing[:6]))

    if want_mission:
        # A MISSION THAT NEVER LEFT THE KERB IS NOT A MISSION THAT RAN. BlockDemoMission
        # writes a row every second from the first frame, and for the whole of
        # `missionAfter` that row says Waiting - so counting rows counted the clock. What
        # proves the lab's player actually took the wheel is a row in any other state.
        rows = 0
        states = {}
        with open(trace_path, encoding="utf-8", errors="replace") as fh:
            for line in fh:
                line = line.strip()
                if not line:
                    continue
                try:
                    row = json.loads(line)
                except Exception:
                    continue
                if row.get("k") != "mission":
                    continue
                state = row.get("state") or ""
                states[state] = states.get(state, 0) + 1
                if state and state != "Waiting":
                    rows += 1
        seen = ", ".join(f"{k or '(none)'}x{v}" for k, v in sorted(states.items()))
        if rows < least:
            bad.append(f"{rows} mission rows past Waiting (wanted at least {least}; "
                       f"states seen: {seen or 'none at all'}) - the mission never "
                       "started, so nothing here judges it")

    if bad:
        print("NOT A RUN TO JUDGE: " + "; ".join(bad))
        return 1
    print("run complete: why=done, 0 errors, 0 exceptions")
    return 0


if __name__ == "__main__":
    sys.exit(main())
