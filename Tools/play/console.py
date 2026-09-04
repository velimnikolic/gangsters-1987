"""The editor's console since the last time anyone looked.

    python Tools/play/console.py            # errors and exceptions since the mark
    python Tools/play/console.py --all      # every level since the mark, not only errors
    python Tools/play/console.py --mark     # move the mark to now, print nothing
    python Tools/play/console.py --tail 50  # the last 50, mark ignored and left alone
    python Tools/play/console.py --trace    # with the stack traces, not just the frame

Exit 0: the console was read and held no error. Exit 1: it held one. Exit 2: it could
not be read, which is NOT the same as clean and must never be taken for it.

Three things this exists to stop, each of which has already cost a session:

  * `clear_console` does not empty the buffer `console` reads. After a clear, `--tail`
    still hands back thousands of old entries, so the natural way to ask "what just
    happened" answers with history. The only honest question is "what is new since this
    number", and the number has to be written down somewhere - here, `Temp/play/`.
  * A stack trace is five to twenty lines of engine frames around the one frame that
    names a file in this project. That frame is the answer; the rest is weight. So the
    trace is folded to its first `Assets/...` line unless `--trace` asks for all of it.
  * The same message repeats. Twenty-two identical leak warnings are one fact and one
    line - `x22` - not twenty-two lines, and counting them by eye across a scroll is
    how the same fact gets fixed twice.
"""

import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
MARK = os.path.join(ROOT, "Temp", "play", "console.mark")
LOUD = ("error", "exception", "assert")
LINES = 40


def ask(args):
    """One `unity command console` call, parsed, or None when it would not answer."""
    try:
        answer = subprocess.run(
            ["unity", "command", "console", "--json"] + args,
            capture_output=True, text=True, timeout=90,
        )
    except (OSError, subprocess.SubprocessError) as problem:
        print("the console would not answer: %s" % problem, file=sys.stderr)
        return None
    try:
        doc = json.loads(answer.stdout)
    except ValueError:
        print(answer.stderr.strip() or "the console answered nothing parseable",
              file=sys.stderr)
        return None
    result = (doc.get("data") or {}).get("result")
    if not doc.get("success") or not isinstance(result, dict):
        print("the console refused: %s" % (doc.get("errors") or "unknown"),
              file=sys.stderr)
        return None
    return result


def read_mark():
    try:
        with open(MARK) as handle:
            return int(handle.read().strip() or 0)
    except (OSError, ValueError):
        return 0


def write_mark(cursor):
    try:
        os.makedirs(os.path.dirname(MARK), exist_ok=True)
        with open(MARK, "w") as handle:
            handle.write(str(int(cursor)))
    except OSError as problem:
        print("the mark could not be written: %s" % problem, file=sys.stderr)


def frame(trace):
    """The first line of the trace that names a file in this project."""
    for line in str(trace or "").splitlines():
        if "Assets/" in line:
            cut = line.find("(at ")
            return line[cut + 4:].rstrip(") ") if cut >= 0 else line.strip()
    return ""


def main():
    args = sys.argv[1:]
    everything = "--all" in args
    traces = "--trace" in args
    tail = 0
    if "--tail" in args:
        try:
            tail = int(args[args.index("--tail") + 1])
        except (IndexError, ValueError):
            print("--tail wants a number", file=sys.stderr)
            return 2

    if "--mark" in args:
        result = ask(["--tail", "1"])
        if result is None:
            return 2
        write_mark(result.get("cursor") or 0)
        return 0

    call = ["--tail", str(tail or 400)]
    if not everything:
        call += ["--level", "error"]
    since = 0
    if not tail:
        since = read_mark()
        if since:
            call += ["--since", str(since)]

    result = ask(call)
    if result is None:
        return 2

    entries = [e for e in (result.get("entries") or [])
               if (e.get("seq") or 0) > since]
    cursor = result.get("cursor") or since
    if not tail:
        write_mark(cursor)

    # The same message from the same place is ONE fact. Order of first sight is kept,
    # because the first error is usually the one that caused the rest.
    seen = {}
    order = []
    for entry in entries:
        level = str(entry.get("level") or "log")
        message = " ".join(str(entry.get("message") or "").split())
        where = frame(entry.get("stackTrace"))
        key = (level, message[:300], where)
        if key not in seen:
            seen[key] = [0, entry.get("stackTrace") or ""]
            order.append(key)
        seen[key][0] += 1

    loud = 0
    for level, message, where in order[:LINES]:
        count, trace = seen[(level, message, where)]
        if level.lower() in LOUD:
            loud += 1
        tally = "  x%d" % count if count > 1 else ""
        place = "  @ %s" % where if where else ""
        print("%-9s %s%s%s" % (level, message[:300], place, tally))
        if traces and trace:
            for line in str(trace).splitlines():
                print("          " + line.strip())

    if len(order) > LINES:
        print("... and %d more kinds of entry (raise --tail or read the editor)"
              % (len(order) - LINES))
    if not order:
        print("nothing new%s" % ("" if everything else " at error level"))
    return 1 if loud else 0


if __name__ == "__main__":
    sys.exit(main())
