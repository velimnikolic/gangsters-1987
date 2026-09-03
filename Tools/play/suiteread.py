"""Reads one `unity command ... --json` envelope off stdin and says one word.

    unity command gangsters_wage_tests --json | python Tools/play/suiteread.py
    unity command editor_status --json | python Tools/play/suiteread.py --playmode

night.sh calls a suite over the pipeline port five times and needs a verdict it can
branch on. The port wraps every answer twice - an envelope with `data.result` inside
it - and a suite's own answer is `passed` plus a list of `failures`. A command that
never reached the editor has no result at all, and that is not a pass.
"""

import json
import sys


def envelope(text):
    try:
        return json.loads(text)
    except Exception:
        # the port prints its errors as plain lines before the JSON
        start = text.find("{")
        if start < 0:
            return None
        try:
            return json.loads(text[start:])
        except Exception:
            return None


def main():
    text = sys.stdin.read()
    doc = envelope(text)
    if doc is None:
        print("UNREADABLE")
        return 1

    result = (doc.get("data") or {}).get("result") or {}

    if "--playmode" in sys.argv:
        print(result.get("playMode") or "unknown")
        return 0

    if not doc.get("success", False):
        print("ERROR")
        return 1

    passed = result.get("passed")
    if passed is True:
        print("PASSED")
        return 0
    if passed is False:
        failures = result.get("failures") or []
        print("FAILED: " + "; ".join(str(f) for f in failures[:4]))
        return 1

    # an audit with no `passed` of its own: faults, if it counts any, are the verdict
    for key in ("faults", "problems", "errors"):
        value = result.get(key)
        if isinstance(value, list):
            print("PASSED" if not value else "FAILED: " + "; ".join(str(f) for f in value[:4]))
            return 0 if not value else 1
        if isinstance(value, int):
            print("PASSED" if value == 0 else f"FAILED: {value} {key}")
            return 0 if value == 0 else 1

    print("NO VERDICT")
    return 1


if __name__ == "__main__":
    sys.exit(main())
