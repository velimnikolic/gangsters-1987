# Offline scan of a Synty demo scene: every prefab instance with its WORLD position/yaw and
# root group, no editor needed.  python Tools/synty/scan_scene.py <scene.unity> <out.tsv>
# Used 2026-08-26 to measure the PalmCity waterfront for Docs/river-plan.md.
import sys, os, re, glob, math, collections
root = r"C:/Users/N/projects/gangsters-1987/Assets/Synty"
scene = sys.argv[1]; out = sys.argv[2]
g2n = {}
for meta in glob.glob(root + "/**/*.prefab.meta", recursive=True):
    with open(meta, encoding="utf-8", errors="ignore") as f:
        for line in f:
            if line.startswith("guid:"):
                g2n[line.split()[1]] = os.path.basename(meta)[:-len(".prefab.meta")]; break
txt = open(scene, encoding="utf-8", errors="ignore").read()
def yaw(qx,qy,qz,qw):
    s = 2*(qw*qy + qx*qz); c = 1-2*(qy*qy+qz*qz); return math.degrees(math.atan2(s,c))
gos={}; trs={}; inst=[]
for b in txt.split("--- !u!")[1:]:
    head,_,body = b.partition("\n"); tid,_,anchor = head.partition(" &"); tid=int(tid.split()[0]); anchor=anchor.strip()
    if tid==1001:
        gm = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]+)", body); guid = gm.group(1) if gm else ""
        mods = dict(re.findall(r"propertyPath: (m_LocalPosition\.[xyz]|m_LocalRotation\.[xyzw]|m_LocalScale\.[xyz]|m_Name)\n\s+value: ([^\n]*)", body))
        par = re.search(r"m_TransformParent: \{fileID: (-?\d+)", body)
        g=lambda k,d: float(mods.get(k,d))
        inst.append(dict(prefab=g2n.get(guid,"?"+guid[:8]), name=mods.get("m_Name", g2n.get(guid,"?")),
            x=g("m_LocalPosition.x",0), y=g("m_LocalPosition.y",0), z=g("m_LocalPosition.z",0),
            yaw=yaw(g("m_LocalRotation.x",0),g("m_LocalRotation.y",0),g("m_LocalRotation.z",0),g("m_LocalRotation.w",1)),
            sx=g("m_LocalScale.x",1), sz=g("m_LocalScale.z",1), parent=par.group(1) if par else "0"))
    elif tid==1:
        nm = re.search(r"m_Name: ([^\n]*)", body); gos[anchor]= nm.group(1) if nm else "?"
    elif tid==4:
        go = re.search(r"m_GameObject: \{fileID: (\d+)", body)
        pos = re.search(r"m_LocalPosition: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}", body)
        rot = re.search(r"m_LocalRotation: \{x: ([^,]+), y: ([^,]+), z: ([^,]+), w: ([^}]+)\}", body)
        fa = re.search(r"m_Father: \{fileID: (-?\d+)", body)
        if go and pos:
            r = [float(v) for v in rot.groups()] if rot else [0,0,0,1]
            trs[anchor]=dict(name=gos.get(go.group(1),"?"), x=float(pos.group(1)), y=float(pos.group(2)), z=float(pos.group(3)), yaw=yaw(*r), parent=fa.group(1) if fa else "0")
for t in trs.values(): t["name"]=gos.get(t["name"],t["name"])
def world(tid):
    # returns (x,y,z,yaw,rootname)
    t = trs.get(tid)
    if not t: return (0,0,0,0,"")
    if t["parent"]=="0": return (t["x"],t["y"],t["z"],t["yaw"],t["name"])
    px,py,pz,pyaw,rn = world(t["parent"])
    a=math.radians(pyaw); c,s=math.cos(a),math.sin(a)
    return (px + c*t["x"] + s*t["z"], py+t["y"], pz - s*t["x"] + c*t["z"], pyaw+t["yaw"], rn)
def chain(tid):
    names=[]
    while tid!="0" and tid in trs: names.append(trs[tid]["name"]); tid=trs[tid]["parent"]
    return "/".join(reversed(names))
with open(out,"w",encoding="utf-8") as f:
    f.write("prefab\tname\twx\twy\twz\twyaw\tsx\tsz\tgroup\tchain\n")
    for i in inst:
        px,py,pz,pyaw,rn = world(i["parent"]) if i["parent"]!="0" else (0,0,0,0,"")
        a=math.radians(pyaw); c,s=math.cos(a),math.sin(a)
        wx = px + c*i["x"] + s*i["z"]; wz = pz - s*i["x"] + c*i["z"]; wy = py+i["y"]
        f.write(f"{i['prefab']}\t{i['name']}\t{wx:.2f}\t{wy:.2f}\t{wz:.2f}\t{(pyaw+i['yaw'])%360:.0f}\t{i['sx']:.2f}\t{i['sz']:.2f}\t{rn}\t{chain(i['parent'])}\n")
print("ok", len(inst))
