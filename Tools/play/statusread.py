"""Reads `unity command recompile_status --json` off stdin and says how it went.

    unity command recompile_status --json | python Tools/play/statusread.py

The port answers with an envelope whose `data.result` is itself a JSON STRING, so the
document has to be parsed twice. Prints one of:

    completed | failed:<the error lines> | compiling | triggered | up_to_date | unknown

`up_to_date` right after a real edit means the AssetDatabase refresh never happened -
recompile.sh treats it as "ask again", never as a pass.
"""

import json
import sys


def main():
    text = sys.stdin.read()
    try:
        doc = json.loads(text)
    except Exception:
        print("unknown")
        return

    result = (doc.get("data") or {}).get("result")
    if isinstance(result, str):
        try:
            result = json.loads(result)
        except Exception:
            print("unknown")
            return
    if not isinstance(result, dict):
        print("unknown")
        return

    status = result.get("status") or "unknown"
    errors = result.get("errors") or []
    if result.get("failed") or errors:
        print("failed:" + "\n".join(str(e) for e in errors))
        return
    print(status)


if __name__ == "__main__":
    main()
