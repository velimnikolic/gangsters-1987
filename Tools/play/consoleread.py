"""Reads `unity command console --json` off stdin: the cursor, or this compile's errors.

    unity command console --tail 1 --json    | python Tools/play/consoleread.py --mark
    unity command console --tail 400 --level error --json | python Tools/play/consoleread.py 78350

`--mark` prints the console's current sequence number, which is the line recompile.sh
draws before it asks for a compile. Given that number instead, it prints every C#
compiler error the console has recorded SINCE it - which is the only honest proof that
an `up_to_date` status is sitting on top of a tree that really does build, rather than
on top of a compile that failed while nobody was reading.
"""

import json
import sys


def main():
    # AN UNREAD CONSOLE IS NOT A CLEAN CONSOLE. Exiting 0 with no output for an answer
    # that never arrived made "the port was busy" indistinguishable from "there were no
    # compiler errors", and the caller certified a build on the strength of it.
    try:
        doc = json.load(sys.stdin)
    except Exception:
        return 2
    result = (doc.get("data") or {}).get("result") or {}
    if not isinstance(result, dict) or not doc.get("success", False):
        return 2

    if "--mark" in sys.argv:
        print(result.get("cursor") or 0)
        return 0

    try:
        since = int(sys.argv[1])
    except (IndexError, ValueError):
        since = 0

    for entry in result.get("entries") or []:
        if (entry.get("seq") or 0) <= since:
            continue
        message = str(entry.get("message") or "")
        # A compiler error, not a game error: "error CS0103: ..." is the only line that
        # says the assemblies on disk are not the source on disk.
        if "error CS" in message:
            print(" ".join(message.split())[:300])
    return 0


if __name__ == "__main__":
    sys.exit(main())
