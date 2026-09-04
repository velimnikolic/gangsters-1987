"""What the open editor answers, without reading 38 KB to find out.

    python Tools/unity/cmds.py                 # this project's commands, names only
    python Tools/unity/cmds.py --gist          # ...and what each one answers, in a line
    python Tools/unity/cmds.py fear            # every match, in full, with its parameters
    python Tools/unity/cmds.py --all           # the editor's own commands as well

`unity command` prints a row per command with the whole description in it: 232 rows,
38 KB, of which this project's 82 commands are 14 KB. Reading that to remember whether
the fear audit is called `gangsters_fear_audit` or `gangsters_audit_fear` costs about
ten thousand tokens, and it is the same ten thousand every session.

The first cut of this printed a gist per command and saved almost nothing - the
descriptions are one long sentence apiece, so a "first sentence" is the whole thing. In
a repository whose commands are named `gangsters_wage_tests` and `gangsters_door_audit`,
the NAME is the gist. So the bare list is names, four to a line; the sentence is there
under `--gist` when the names are not enough, and the full text with parameters is paid
for only when something is actually being looked up.

Exit 0 when something was printed, 1 when nothing matched or the editor would not
answer - stderr tells those two apart, but either way it is not a list.
"""

import subprocess
import sys

PREFIX = "gangsters_"
GIST = 62
COLUMN = 32


def rows():
    """Every command the editor answers, as (name, description, parameters)."""
    try:
        answer = subprocess.run(
            ["unity", "command"], capture_output=True, text=True, timeout=60
        )
    except (OSError, subprocess.SubprocessError) as problem:
        print("the editor would not answer: %s" % problem, file=sys.stderr)
        return []
    if answer.returncode != 0:
        print(answer.stderr.strip() or "unity command failed", file=sys.stderr)
        return []

    out = []
    for line in answer.stdout.splitlines():
        parts = line.split("\t")
        if len(parts) < 2 or parts[0] in ("Command", ""):
            continue
        name = parts[0].strip()
        description = parts[1].strip()
        params = parts[3].strip() if len(parts) > 3 else ""
        if name:
            out.append((name, description, params))
    return out


def gist(description):
    """As much of the description as says what the command is for, and no more.

    These sentences announce themselves and then list: "Deal the city core from a seed
    and report the verdict on each: deals needed, faults, areas, roads". Everything
    after the colon is the shape of the answer, which is worth reading when the command
    is being used and not when it is being found.
    """
    text = " ".join(description.split())
    for mark in (". ", ": ", "; ", " - ", ", "):
        cut = text.find(mark)
        if cut > 0:
            text = text[:cut]
    if len(text) > GIST:
        text = text[:GIST].rsplit(" ", 1)[0] + "..."
    return text.rstrip(".")


def columns(names, width=COLUMN, per_line=4):
    for i in range(0, len(names), per_line):
        print("  " + "  ".join("%-*s" % (width, n) for n in names[i:i + per_line]).rstrip())


def main():
    args = sys.argv[1:]
    everything = "--all" in args
    gists = "--gist" in args
    terms = [a.lower() for a in args if not a.startswith("--")]

    found = rows()
    if not found:
        return 1

    mine = [r for r in found if r[0].startswith(PREFIX)]
    theirs = [r for r in found if not r[0].startswith(PREFIX)]

    if terms:
        # A LOOKUP, so pay for the full text: name, whole description, parameters. The
        # match runs over the description as well as the name, because the thing being
        # looked for is usually the subject ("fear", "wage", "storefront"), not a name
        # anybody remembers.
        pool = found if everything else mine
        hits = [r for r in pool if all(t in (r[0] + " " + r[1]).lower() for t in terms)]
        if not hits:
            print("no command matches %s" % " ".join(terms), file=sys.stderr)
            return 1
        for name, description, params in hits:
            print(name)
            print("    " + " ".join(description.split()))
            if params:
                print("    " + params)
        return 0

    print("this project (%d), `unity command <name>`:" % len(mine))
    if gists:
        for name, description, _ in mine:
            print("  %-*s %s" % (COLUMN, name, gist(description)))
    else:
        columns([r[0] for r in mine])

    if everything:
        print()
        print("the editor's own (%d), documented by Unity:" % len(theirs))
        columns([r[0] for r in theirs], width=26)
    return 0


if __name__ == "__main__":
    sys.exit(main())
