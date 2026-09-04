"""The editor's console since the last time anyone looked.

    python Tools/play/console.py            # errors and exceptions since the mark
    python Tools/play/console.py --all      # every level since the mark, not only errors
    python Tools/play/console.py --mark     # move the mark to now, print nothing
    python Tools/play/console.py --tail 50  # the last 50, mark ignored and left alone
    python Tools/play/console.py --trace    # with the stack traces, not just the frame
    python Tools/play/console.py --selftest # the shaping rules, with no editor needed

Exit 0: the console was read whole and held no error. Exit 1: it held one. Exit 2: the
read was refused, unparseable or INCOMPLETE, which is not the same as clean and must
never be taken for it.

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

And the rest of the file is three rounds of adversarial review on one theme, because a
reader that MOVES THE MARK has one chance at every entry and anything it does not look
at is gone for good:

  * The verdict counts every entry read, not the forty that get printed. Forty distinct
    logs ahead of an error used to print no error and exit 0, and then eat it.
  * A `--tail N` that comes back with exactly N rows, or with `dropped`, is a window on
    the newest entries with the older ones - the ones nearer the mark, which is to say
    the ones that STARTED the trouble - left outside it. Reported, and exit 2.
  * That report used to be one-shot: the mark advanced past the hole, so the next run
    found nothing newer and said clean. The hole is now written down, it outlives the
    mark, it is honoured in every reading mode, and only `--mark` clears it.
  * Every piece of state fails closed. Zero is a real cursor and a real gap, not an
    empty one; unreadable state is a gap, not the absence of one; the gap is on disk
    before the mark moves, and if it will not go down durably the mark does not move at
    all. Each of those was a live false-clean before it was a rule.
"""

import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
MARK = os.path.join(ROOT, "Temp", "play", "console.mark")
GAP = os.path.join(ROOT, "Temp", "play", "console.gap")
LOUD = ("error", "exception", "assert")
LINES = 40
BUFFER = 2000


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
    """('absent', 0) | ('bad', 0) | ('ok', n) - and zero is a perfectly good n.

    A mark of 0 is what `--mark` writes against a buffer that has captured nothing, so
    "no mark" and "mark at zero" cannot be the same answer: the editor only reports its
    own `dropped` overrun when a cursor is sent, and a zero that reads as absent sends
    none. That is a whole class of eviction going unnoticed at exactly the moment - a
    fresh buffer filling up - when it is most likely.
    """
    if not os.path.exists(MARK):
        return "absent", 0
    try:
        with open(MARK) as handle:
            return "ok", int(handle.read().strip())
    except (OSError, ValueError):
        return "bad", 0


def write_mark(cursor):
    """Move the mark forward. NEVER backward.

    Two sessions share this checkout, so two readers can be in here at once. The one
    that started earlier finishes with an older cursor in hand, and writing it would
    hand the other reader's already-read entries back as new - harmless - or, worse,
    move the mark behind a gap that was just recorded. Forward only costs one read.

    Answers whether this cursor is now the mark - false when a newer one already was,
    which is also the caller's warning that it is not the reader speaking last.
    """
    state, standing = read_mark()
    if state == "ok" and standing >= int(cursor):
        return False
    try:
        os.makedirs(os.path.dirname(MARK), exist_ok=True)
        with open(MARK, "w") as handle:
            handle.write(str(int(cursor)))
    except OSError as problem:
        print("the mark could not be written: %s" % problem, file=sys.stderr)
        return False
    return True


def read_gap():
    """(standing, seq) - and a gap recorded at seq 0 is still a gap.

    Everything here fails CLOSED: a gap file that cannot be read, or holds nonsense, is
    a gap. The mark makes the opposite trade (an unreadable mark is exit 2 and no read
    at all), because a mark decides what is new while a gap only ever withholds a pass.
    """
    try:
        with open(GAP) as handle:
            return True, int(handle.read().strip())
    except FileNotFoundError:
        # The ONLY answer that means there is no gap. Not `os.path.exists`, which says
        # false for a stat that failed, a dangling link, a directory it may not walk -
        # every one of which is "cannot tell", and cannot tell is a gap.
        return False, 0
    except (OSError, ValueError):
        return True, -1


def write_gap(cursor):
    """Remember an incomplete read until somebody says they have seen it.

    The mark has to advance even on an incomplete read, or a buffer that overran once
    would be re-read and re-truncated forever with no way forward. But advancing it is
    exactly what makes the hole invisible to the NEXT run, which then finds nothing new
    and answers 0 - the incomplete warning would be one-shot, and a burst that lost its
    first error would go green on the retry. So the hole outlives the mark, and only
    `--mark` (a person saying "seen") clears it.

    Returns whether the record is DURABLE, read back from disk - the caller must not
    advance the mark on a false, or the cursor would move past unread entries with
    nothing left behind to say so.
    """
    try:
        os.makedirs(os.path.dirname(GAP), exist_ok=True)
        with open(GAP, "w") as handle:
            handle.write(str(int(cursor)))
            handle.flush()
            os.fsync(handle.fileno())
    except OSError as problem:
        print("the gap could not be recorded: %s" % problem, file=sys.stderr)
        return False
    return read_gap()[0]


def clear_gap():
    try:
        os.remove(GAP)
    except OSError:
        pass


def verdict(loud, whole, standing):
    """0 read whole and quiet, 1 read whole and loud, 2 not read whole."""
    if not whole or standing:
        return 2
    return 1 if loud else 0


def frame(trace):
    """The first line of the trace that names a file in this project."""
    for line in str(trace or "").splitlines():
        if "Assets/" in line:
            cut = line.find("(at ")
            return line[cut + 4:].rstrip(") ") if cut >= 0 else line.strip()
    return ""


def digest(entries):
    """The entries as distinct kinds, in order of first sight, each with its tally.

    The same message from the same place is ONE fact. First sight orders them because
    the first error is usually the one that caused the rest.
    """
    seen = {}
    order = []
    for entry in entries:
        level = str(entry.get("level") or "log")
        message = " ".join(str(entry.get("message") or "").split())[:300]
        where = frame(entry.get("stackTrace"))
        key = (level, message, where)
        if key not in seen:
            seen[key] = {"count": 0, "trace": entry.get("stackTrace") or ""}
            order.append(key)
        seen[key]["count"] += 1
    return [(k[0], k[1], k[2], seen[k]["count"], seen[k]["trace"]) for k in order]


def is_loud(level):
    return str(level).lower() in LOUD


def complete(result, requested):
    """Did this answer hold everything the window was asked for?

    A response of exactly as many rows as were asked for is a window on the NEWEST
    entries, and the ones it cut off are the older ones - nearest the mark, which is
    where the cause of a burst lives. `dropped` says the editor's own ring overran.
    Neither can be read as "and there was nothing else".
    """
    returned = result.get("returned")
    if returned is None:
        returned = len(result.get("entries") or [])
    return not result.get("dropped") and returned < requested


def show(kinds, traces):
    """Print at most LINES kinds, the loud ones first so a cap can never hide one."""
    ordered = [k for k in kinds if is_loud(k[0])] + [k for k in kinds if not is_loud(k[0])]
    for level, message, where, count, trace in ordered[:LINES]:
        tally = "  x%d" % count if count > 1 else ""
        place = "  @ %s" % where if where else ""
        print("%-9s %s%s%s" % (level, message, place, tally))
        if traces and trace:
            for line in str(trace).splitlines():
                print("          " + line.strip())
    if len(ordered) > LINES:
        print("... and %d more kinds of entry (raise --tail or read the editor)"
              % (len(ordered) - LINES))


def selftest():
    """The two false negatives that shipped once, held down in code."""
    quiet = [{"level": "log", "message": "line %d" % i} for i in range(LINES)]
    kinds = digest(quiet + [{"level": "error", "message": "the one that matters"}])
    assert len(kinds) == LINES + 1, len(kinds)
    assert any(is_loud(k[0]) for k in kinds), "an error past the display cap was lost"
    assert is_loud(([k for k in kinds if is_loud(k[0])] + [k for k in kinds if not is_loud(k[0])])[0][0])

    same = [{"level": "warn", "message": "leak", "stackTrace": "at Assets/A.cs:1"}] * 22
    folded = digest(same)
    assert len(folded) == 1 and folded[0][3] == 22, folded

    assert complete({"returned": 399, "dropped": False}, 400)
    assert not complete({"returned": 400, "dropped": False}, 400), "a full window is not a whole read"
    assert not complete({"returned": 5, "dropped": True}, 400), "dropped is not a whole read"

    # A gap survives the read that found it: the SECOND run, which sees a whole quiet
    # window, must still refuse to say clean.
    assert verdict(0, True, False) == 0
    assert verdict(1, True, False) == 1
    assert verdict(0, False, False) == 2
    assert verdict(0, True, True) == 2, "an unacknowledged gap went green on the retry"

    # A mark of zero is a mark. It used to read as no mark at all, which meant no
    # `--since`, which meant the editor never reported its own overrun.
    keep = globals()["MARK"]
    try:
        globals()["MARK"] = os.path.join(ROOT, "Temp", "play", "console.selftest.mark")
        write_mark(0)
        assert read_mark() == ("ok", 0), read_mark()
        with open(MARK, "w") as handle:
            handle.write("not a number")
        assert read_mark()[0] == "bad", read_mark()
        os.remove(MARK)
        assert read_mark() == ("absent", 0), read_mark()
        write_mark(500)
        write_mark(200)
        assert read_mark() == ("ok", 500), "the mark went backwards"
        write_mark(900)
        assert read_mark() == ("ok", 900), read_mark()
        os.remove(MARK)
    finally:
        globals()["MARK"] = keep

    # And a gap at seq 0 is a gap. It was written as `0`, read back as falsy, and the
    # retry went green - the zero trap again, one file over.
    keep = globals()["GAP"]
    try:
        globals()["GAP"] = os.path.join(ROOT, "Temp", "play", "console.selftest.gap")
        clear_gap()
        assert read_gap() == (False, 0), read_gap()
        assert write_gap(0), "a gap must be durable before the mark is allowed to move"
        assert read_gap() == (True, 0), read_gap()
        assert verdict(0, True, read_gap()[0]) == 2, "a gap at seq 0 read as no gap"
        with open(GAP, "w") as handle:
            handle.write("scribble")
        assert read_gap()[0], "unreadable gap state must fail closed"
        clear_gap()
        assert read_gap() == (False, 0), read_gap()
    finally:
        globals()["GAP"] = keep
    print("selftest ok")
    return 0


def main():
    args = sys.argv[1:]
    if "--selftest" in args:
        return selftest()

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
        cursor = result.get("cursor") or 0
        state, mark_now = read_mark()
        covered = state != "ok" or cursor >= mark_now
        write_mark(cursor)
        # "SEEN" ONLY COVERS WHAT THIS READER SAW. If another session moved the mark on
        # past this cursor while this call was in flight, it may have recorded a hole
        # out there too - and clearing it here would bury entries that are already
        # behind the mark, where no later read can reach them. A gap this
        # acknowledgement cannot prove it covers is left standing; run --mark again and
        # it will cover it.
        standing, gap_at = read_gap()
        if standing and (not covered or gap_at > cursor):
            print("a gap at seq %s is NOT covered by this mark (cursor %d) and stays "
                  "standing" % ("(unreadable)" if gap_at < 0 else gap_at, cursor),
                  file=sys.stderr)
            return 2
        clear_gap()
        return 0

    # The whole buffer by default: the cost of a wide window is paid in the editor, not
    # here, because what comes back is folded to kinds before anything is printed.
    requested = tail or BUFFER
    call = ["--tail", str(requested)]
    if not everything:
        call += ["--level", "error"]
    since = 0
    # A standing gap is honoured in EVERY reading mode. `--tail` moves no mark, but it
    # is still somebody asking "is the console clean", and the answer while entries are
    # known to be missing is no.
    standing, gap_at = read_gap()
    if not tail:
        state, since = read_mark()
        if state == "bad":
            print("the mark at %s cannot be read, so nothing can be called new" % MARK,
                  file=sys.stderr)
            return 2
        if state == "ok":
            call += ["--since", str(since)]

    result = ask(call)
    if result is None:
        return 2

    entries = [e for e in (result.get("entries") or []) if (e.get("seq") or 0) > since]
    # An explicit --tail ASKED for a window and moves no mark, so a full one is the
    # answer to the question rather than a hole in it; only the editor's own overrun
    # can make that read incomplete. A marked read has no second chance and is held to
    # the stricter test.
    whole = complete(result, requested) if not tail else not result.get("dropped")
    kinds = digest(entries)

    # THE VERDICT IS TAKEN OVER EVERYTHING READ, before any of it is thrown away for
    # the sake of a readable page.
    loud = sum(1 for k in kinds if is_loud(k[0]))

    if not whole:
        print("INCOMPLETE: the window came back full (%d) or dropped, so entries "
              "between the mark and the oldest line below were never read"
              % len(entries))
    # ASK AGAIN, because `ask()` can block for ninety seconds and the other session in
    # this checkout may have recorded a gap inside that window. This is not a lock and
    # does not pretend to be one: two readers can still interleave. It costs one stat to
    # close the wide window, the mark only ever moves forward, and the gap file itself
    # outlives any single run - which is the whole reason the state is on disk.
    if not standing:
        standing, gap_at = read_gap()

    if standing:
        print("UNREAD GAP still standing from seq %s: entries were lost there and "
              "nobody has said they saw it. `--mark` acknowledges and clears it."
              % ("(unreadable)" if gap_at < 0 else gap_at))
    show(kinds, traces)
    if not kinds:
        print("nothing new%s" % ("" if everything else " at error level"))

    if not tail:
        # THE GAP GOES DOWN BEFORE THE MARK MOVES. The other order leaves a window in
        # which the cursor has advanced past entries nobody read and the thing that
        # would have said so was never written; if it cannot be written durably, the
        # mark stays where it is and the next read finds the same hole again.
        if not whole and not write_gap(since):
            print("the gap could not be recorded, so the mark stays put", file=sys.stderr)
        else:
            write_mark(result.get("cursor") or since)
    return verdict(loud, whole, standing)


if __name__ == "__main__":
    sys.exit(main())
