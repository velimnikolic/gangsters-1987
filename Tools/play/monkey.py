"""Reads a monkey soak and says what the city did wrong.

    python3 Tools/play/monkey.py <soak dir>

Three sources, one table:
  * the trace   - every fault row the driving code raised against itself (a car stuck,
                  a man striding nowhere, a wheel wound over at speed), and the walk
                  planner's own "NO WAY across" note;
  * the log     - the monkey's findings, the city audit's, and the handful of engine
                  warnings that mean something is not reaching the street at all;
  * the summary - errors, exceptions, and whether the run finished.

What matters is the RUNS column, not the count: a fault in forty of fifty runs is the
city, a fault in one is that run's bad luck.
"""

import json
import os
import re
import sys
from collections import Counter, defaultdict

MONKEY = re.compile(r"\[monkey\] ([a-z-]+): (.*?) - (.*)$")
TALLY = re.compile(r"\[monkey\] (\d+) orders: (\d+) wars, (\d+) attacks, "
                   r"(\d+) drive-bys, (\d+) moto passes, (\d+) marches; (\d+) men down")
AUDIT = re.compile(r"\[audit\] ([a-z-]+): (.*?) - (.*)$")

# Engine lines that mean a thing the ledger sold never reached the street, or a body
# is missing - silent in play, and exactly what a soak is for.
LOG_SIGNS = [
    ("vehicle-never-parked", re.compile(r"\[Crews\] nowhere to leave (.*)$")),
    ("no-body-for-listing", re.compile(r"\[Crews\] no body for (.*)$")),
    ("front-without-door", re.compile(r"\[RoadDemo\] No street door (.*)$")),
    ("gang-model-missing", re.compile(r"\[Gangs\] Model (.*)$")),
]


def runs(root):
    for name in sorted(os.listdir(root)):
        path = os.path.join(root, name)
        if os.path.isdir(path) and name.startswith("run-"):
            yield name, path


def trace_faults(path):
    """(tag, fault) -> count, plus 'NO WAY' walk rows and a sample of each."""
    counts = Counter()
    samples = {}
    trace = os.path.join(path, "trace.jsonl")
    if not os.path.exists(trace):
        return counts, samples
    with open(trace, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if '"fault"' not in line and '"walk"' not in line:
                continue
            try:
                r = json.loads(line)
            except json.JSONDecodeError:
                continue
            k = r.get("k")
            if k == "fault":
                kind = r.get("fault")
                if kind is None:          # the monkey's and the audit's own rows
                    kind = r.get("what", "?")
                key = f"{r.get('tag', '?')}/{kind}"
            elif k == "walk" and "NO WAY" in str(r.get("what", "")):
                key = "crew/no-way-across"
            else:
                continue
            counts[key] += 1
            samples.setdefault(key, r)
    return counts, samples


def main(root):
    kinds = defaultdict(Counter)      # key -> Counter(run -> times)
    said = defaultdict(list)          # key -> [(run, text)]
    totals = Counter()
    played, no_run = 0, []
    exceptions = defaultdict(list)

    for name, path in runs(root):
        if not os.path.exists(os.path.join(path, "summary.json")):
            no_run.append(name)
            continue
        played += 1
        with open(os.path.join(path, "summary.json")) as fh:
            data = json.load(fh)
        for k in ("sim", "errors", "exceptions"):
            totals[k] += data.get(k, 0)
        if data.get("why") != "done":
            kinds["run/" + str(data.get("why"))][name] += 1

        log = os.path.join(path, "unity.log")
        if os.path.exists(log):
            with open(log, "r", encoding="utf-8", errors="replace") as fh:
                for line in fh:
                    m = TALLY.search(line)
                    if m:
                        for key, v in zip(("orders", "wars", "attacks", "drivebys",
                                           "motos", "marches", "kills"), m.groups()):
                            totals[key] += int(v)
                        continue
                    m = MONKEY.search(line)
                    if m:
                        kinds["monkey/" + m.group(1)][name] += 1
                        said["monkey/" + m.group(1)].append((name, f"{m.group(2)}: {m.group(3)}"))
                        continue
                    m = AUDIT.search(line)
                    if m:
                        kinds["audit/" + m.group(1)][name] += 1
                        said["audit/" + m.group(1)].append((name, f"{m.group(2)}: {m.group(3)}"))
                        continue
                    for label, rx in LOG_SIGNS:
                        m = rx.search(line)
                        if m:
                            kinds["log/" + label][name] += 1
                            said["log/" + label].append((name, m.group(1).strip()))
                            break
                    if "Exception" in line and "at " != line[:3]:
                        exceptions[line.strip()[:150]].append(name)

        counts, samples = trace_faults(path)
        for key, n in counts.items():
            kinds[key][name] += n
            if len(said[key]) < 6:
                said[key].append((name, json.dumps(samples[key])[:200]))

    print(f"== {root}")
    print(f"{played} runs played, {len(no_run)} never ran"
          + (f" ({', '.join(no_run)})" if no_run else ""))
    print(f"   {totals['sim']:.0f}s of city; {totals['orders']} orders - "
          f"{totals['wars']} wars, {totals['attacks']} foot attacks, "
          f"{totals['drivebys']} drive-bys, {totals['motos']} moto passes, "
          f"{totals['marches']} marches; {totals['kills']} men down")
    print(f"   {totals['errors']} unity errors, {totals['exceptions']} exceptions")

    if exceptions:
        print("\n== exceptions")
        for line, where in sorted(exceptions.items(), key=lambda kv: -len(kv[1]))[:10]:
            print(f"   {len(set(where)):3d} runs  {line}")

    print("\n== findings (runs, then times)")
    order = sorted(kinds.items(), key=lambda kv: (-len(kv[1]), -sum(kv[1].values())))
    for key, per_run in order:
        print(f"   {key:34s} {len(per_run):3d} runs {sum(per_run.values()):7d} times")

    print("\n== what they said")
    for key, per_run in order:
        if key not in said or not said[key]:
            continue
        print(f"\n-- {key}")
        for run, text in said[key][:4]:
            print(f"   {run}  {text}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "."))
