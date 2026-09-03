"""EPIC 28's number against the brief's floor, read off the counts and not the label.

    python Tools/play/analyze.py DIR --cover-tally | python Tools/play/coverfloor.py 80

analyze.py prints `== cover first: 39/49 men over 5 runs (80%)`. The percentage on the
end is rounded, so 39 of 49 - 79.6 % - prints as 80 and would clear an 80 % floor it is
actually under; and a soak that measured nobody prints `0/0 ... (0%)` or nothing at all,
which read as "no reason to fail" when it is the worst reading there is.

Prints one line when the soak is under the floor and nothing when it is over it, so the
caller can test the output for emptiness. A tally it cannot read is always a failure.
"""

import re
import sys


def main():
    floor = float(sys.argv[1]) if len(sys.argv) > 1 else 80.0
    text = sys.stdin.read()

    hit = re.search(r"cover first:\s*(\d+)\s*/\s*(\d+)\b", text)
    if not hit:
        print("the cover tally could not be read at all, so nothing measured the "
              "cover-first share: " + " ".join(text.split())[:160])
        return

    covered, fired = int(hit.group(1)), int(hit.group(2))
    if fired <= 0:
        print("the cover tally measured nobody (0 men fired a first round), so the "
              f"{floor:.0f}% cover-first floor is unproven")
        return

    share = covered * 100.0 / fired
    if share < floor:
        print(f"cover first {covered}/{fired} = {share:.1f}% is under the "
              f"{floor:.0f}% floor")


if __name__ == "__main__":
    main()
