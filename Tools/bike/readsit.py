#!/usr/bin/env python3
"""Read a dragged man out of a saved BikeSit scene.

    nudge = bakedNudge + (his m_LocalPosition now - where the bake left him)

The machine stands at the origin unrotated, so local is world, and a rigid drag moves
his hips by exactly what it moves his root by."""
import re, sys

TAG_GUID = "5d1c8a3f9e6b427081a2b3c4d5e6f708"

def docs(text):
    """Every YAML document as (unity class, fileID, body)."""
    out, cur = [], None
    for line in text.splitlines():
        m = re.match(r"^--- !u!(\d+) &(-?\d+)", line)
        if m:
            if cur: out.append(cur)
            cur = [int(m.group(1)), int(m.group(2)), []]
        elif cur:
            cur[2].append(line)
    if cur: out.append(cur)
    return [(c, i, "\n".join(b)) for c, i, b in out]

def vec(body, key):
    m = re.search(r"^\s*%s:\s*\{x:\s*(-?[\d.eE+-]+),\s*y:\s*(-?[\d.eE+-]+),\s*z:\s*(-?[\d.eE+-]+)" % key,
                  body, re.M)
    return tuple(float(g) for g in m.groups()) if m else None

def num(body, key):
    m = re.search(r"^\s*%s:\s*(-?[\d.eE+-]+)\s*$" % key, body, re.M)
    return float(m.group(1)) if m else None

def word(body, key):
    m = re.search(r"^\s*%s:\s*(.*)$" % key, body, re.M)
    return m.group(1).strip() if m else ""

def main(path):
    try:
        text = open(path).read()
    except FileNotFoundError:
        print("no scene at", path)
        print("Bake one first: Unity menu Tools > Bike bench > Sit two men on a bike (no Play).")
        return 2

    d = docs(text)
    tag = next((b for c, i, b in d if c == 114 and TAG_GUID in b), None)
    if tag is None:
        print("no BikeSitTag in", path, "- is this a BikeSit scene?"); return 2

    names = {i: word(b, "m_Name") for c, i, b in d if c == 1}
    tf = {}
    for c, i, b in d:
        if c != 4: continue
        m = re.search(r"m_GameObject:\s*\{fileID:\s*(-?\d+)\}", b)
        if not m: continue
        tf[names.get(int(m.group(1)), "")] = b

    machine = word(tag, "machine")
    print("machine       ", machine)
    # the men are measured against world points recorded at bake time, and that only
    # holds while the machine itself has not been moved
    bike = tf.get(machine)
    if bike is not None:
        at = vec(bike, "m_LocalPosition")
        if at and max(abs(x) for x in at) > 1e-3:
            print("  !! the machine has been moved to (%.3f, %.3f, %.3f)." % at)
            print("     Move it back to 0,0,0 (or re-bake) - the men are measured against")
            print("     world points recorded when it stood at the origin.")
    print("measured       wheelbase %.2f  wheel r %.2f  grip y %.2f" %
          (num(tag, "wheelbase") or 0, num(tag, "wheelRadius") or 0, num(tag, "gripY") or 0))
    for who, label, nud, bake in (("DRIVER", "RiderNudge", "riderNudge", "riderAtBake"),
                                  ("SHOOTER", "PillionNudge", "pillionNudge", "pillionAtBake")):
        body = tf.get(who)
        if body is None:
            print(who, "- not in the scene"); continue
        now, was, baked = vec(body, "m_LocalPosition"), vec(tag, bake), vec(tag, nud)
        if None in (now, was, baked):
            print(who, "- could not read his numbers"); continue
        d3 = tuple(n - w for n, w in zip(now, was))
        out = tuple(b + x for b, x in zip(baked, d3))
        moved = max(abs(x) for x in d3) > 1e-4
        scale = vec(body, "m_LocalScale")
        print("%-8s dragged (%+.3f, %+.3f, %+.3f)%s   size %.2f" %
              (who, d3[0], d3[1], d3[2], "" if moved else "  (not moved)",
               scale[0] if scale else 1.0))
        print("         %s = new Vector3(%.3ff, %.3ff, %.3ff)" % (label, out[0], out[1], out[2]))
    return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else
                  "/Users/velimirovixxx/Gangsters/Assets/Scenes/BikeSit.unity"))
