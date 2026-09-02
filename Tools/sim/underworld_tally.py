#!/usr/bin/env python3
"""RIVAL-011. The tally over the underworld sweep.

    unity command gangsters_underworld_sim --sweep 30 --days 90 --houses 21 --json > sweep.json
    python Tools/sim/underworld_tally.py sweep.json

One seed proves nothing; thirty is the verdict. This reads the sweep's own lines and
answers the questions the epic asks: how long a family takes to bank its first bag, how
many are still standing at day 90, how many wars there were, and who ended richest and
poorest.

Nothing here computes a rule. It counts what the sim printed.
"""
import json
import statistics
import sys


def rows(payload):
    """One dict per printed line: seed, day, house, and every figure on it."""
    for line in payload["lines"]:
        parts = line.split()
        row = {"seed": int(parts[1])}
        i = 2
        while i < len(parts) - 1:
            key, value = parts[i], parts[i + 1]
            row[key] = value
            i += 2
        yield row


def main(path):
    with open(path, encoding="utf-8") as handle:
        payload = json.load(handle)

    result = payload.get("data", {}).get("result", payload)
    if "lines" not in result:
        print("that file has no sweep in it")
        return 2

    last = {}          # (seed, house) -> the last line for that family
    first_bag = []     # days to the first banked bag
    for row in rows(result):
        steps = row.get("steps", "").split("/")
        key = (row["seed"], row["house"])
        last[key] = row
        if len(steps) >= 6 and steps[5] not in ("-1", ""):
            first_bag.append((key, int(steps[5])))

    seen = {}
    for key, day in first_bag:
        if key not in seen or day < seen[key]:
            seen[key] = day
    days_to_bag = sorted(seen.values())

    alive = 0
    solvent = 0
    wars = 0
    safes = []
    banked = []
    for key, row in last.items():
        men = row.get("men", "0/0/0/0").split("/")
        if int(men[0]) > 0:
            alive += 1
        safe = int(row.get("safe", 0))
        safes.append((safe, key))
        banked.append((int(row.get("banked", 0)), key))
        if safe >= 0:
            solvent += 1
        wars += int(row.get("wars", 0))

    families = len(last)
    print(f"seeds            {result.get('seeds', '?')}")
    print(f"days             {result.get('days', '?')}")
    print(f"families         {families}")
    print()
    print(f"first bag home   median {statistics.median(days_to_bag) if days_to_bag else '-'}"
          f" days, worst {max(days_to_bag) if days_to_bag else '-'},"
          f" never {families - len(days_to_bag)}")
    print(f"still standing   {alive} of {families} at day {result.get('days', '?')}")
    print(f"solvent          {solvent} of {families}")
    print(f"wars             {wars // 2} pairs at war on the last day")
    print(f"negative days    {result.get('negatives', 0)}"
          f"  (EPIC 24 owns the short envelope)")
    print(f"refused (owner)  {result.get('ownershipRefusals', 0)}   <- must be 0")

    safes.sort()
    banked.sort()
    if safes:
        poor, poorest = safes[0]
        rich, richest = safes[-1]
        print()
        print(f"richest          seed {richest[0]} house {richest[1]}: ${rich}")
        print(f"poorest          seed {poorest[0]} house {poorest[1]}: ${poor}")
    if banked:
        best, who = banked[-1]
        print(f"most collected   seed {who[0]} house {who[1]}: ${best}")

    errors = result.get("errors") or []
    for error in errors:
        print("ERROR", error)
    return 0 if not errors and result.get("ownershipRefusals", 0) == 0 else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "sweep.json"))
