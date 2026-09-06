#!/usr/bin/env python3
"""Offline seed/land-use checks using the runtime planners; never contacts Unity.

Unity maths use CoreSim's stubs. Territory, district frames and parking planning
are extracted verbatim from their source files (their view/runtime tails are
excluded). Satellite views are dimension-contract doubles, not Play evidence.
"""
from pathlib import Path
import hashlib
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(tempfile.mkdtemp(prefix="gangsters-region-"))


def read(name):
    return (ROOT / name).read_text(encoding="utf-8-sig")


def extract(name, start, end):
    text = read(name)
    return text[text.index(start):text.index(end)]


stubs = read("Tools/CoreSim/Stubs.cs")
a = stubs.index("    public enum CoreQuarterId")
b = stubs.index("    public static class RoadDemoBuilder", a)
stubs = stubs[:a] + stubs[b:]
stubs = stubs.replace("public static float RoadHalf(bool boulevard)", "public const float BeltOut = 166f;\n        public static float RoadHalf(bool boulevard)")
stubs = stubs.replace("public float xMin => x;", "public float xMin { get => x; set { width += x-value; x=value; } }")
stubs = stubs.replace("public float yMin => y;", "public float yMin { get => y; set { height += y-value; y=value; } }")
stubs = stubs.replace("public float xMax => x + width;", "public float xMax { get => x+width; set => width=value-x; }")
stubs = stubs.replace("public float yMax => y + height;", "public float yMax { get => y+height; set => height=value-y; }")
stubs = stubs.replace("public struct Rect\n    {", """public struct Rect
    {
        public Vector2 min => new Vector2(xMin,yMin);
        public bool Overlaps(Rect r) => xMin<r.xMax && xMax>r.xMin && yMin<r.yMax && yMax>r.yMin;
        public static bool operator ==(Rect a, Rect b) => a.x==b.x && a.y==b.y && a.width==b.width && a.height==b.height;
        public static bool operator !=(Rect a, Rect b) => !(a==b);
""")
stubs = stubs.replace("public struct Vector2\n    {", """public struct Vector2
    {
        public float sqrMagnitude => x*x+y*y;
        public static float Distance(Vector2 a,Vector2 b) => (float)Math.Sqrt((a-b).sqrMagnitude);
""")
stubs = stubs.replace("public struct Vector3\n    {", """public struct Vector3
    {
        public static Vector3 left => new Vector3(-1,0,0);
        public static Vector3 back => new Vector3(0,0,-1);
""")
(OUT / "Stubs.cs").write_text(stubs)
head = "using System; using System.Collections.Generic; using UnityEngine;\nnamespace RoadDemo {\n"
(OUT / "Territory.cs").write_text(head + extract("Assets/RoadDemo/CoreTerritory.cs", "    public enum CoreQuarterId", "    public enum QuarterConflictState") + "}\n")
(OUT / "Frame.cs").write_text(head + extract("Assets/RoadDemo/District.cs", "    public enum DistrictKind", "    // ----------------------------------------------------------- reservations") + "}\n")
(OUT / "Parking.cs").write_text(head + extract("Assets/RoadDemo/ParkingBlock.cs", "    public sealed class ParkingBlockPlan", "    public sealed class ParkingBlockSite") + "}\n")
harbor_source = read("Assets/HarborDemo/HarborDistrict.cs")
landmark_source = read("Assets/HarborDemo/HarborDistrict.Landmarks.cs")
harbor_inputs = []
for source, marker in ((harbor_source, "public int berths ="),
                       (harbor_source, "public float berthPitch ="),
                       (harbor_source, "public float QuayHalf =>"),
                       (harbor_source, "public const float BasinReach ="),
                       (harbor_source, "const float PlannedStreetZ ="),
                       (landmark_source, "public const float BulkTerminalLength ="),
                       (landmark_source, "public float PlannedBulkTerminalEast =>")):
    harbor_inputs.append(next(line for line in source.splitlines() if marker in line))
harbor_plan = extract("Assets/HarborDemo/HarborDistrict.cs", "        public void Plan(", "        public void Reserve(")
harbor_plan = harbor_plan.replace("public void Plan", "public override void Plan")
harbor_plan = harbor_plan.replace("this.seed = seed;", "this.seed = seed; Publish(links);")
harbor_plan = harbor_plan.replace("_bounds =", "LocalBounds =")
(OUT / "HarborPlan.cs").write_text("using UnityEngine; namespace HarborDemo { public partial class HarborDistrict {\n" +
    "\n".join(harbor_inputs) + "\n" + harbor_plan + "} }\n")
tree = ET.parse(ROOT / "Tools/CoreSim/CoreSim.csproj")
sources = []
for item in tree.findall(".//Compile"):
    name = item.attrib["Include"]
    if name in ("Program.cs", "Stubs.cs"):
        continue
    sources.append((ROOT / "Tools/CoreSim" / name.replace("\\", "/")).resolve())
sources += [ROOT / "Assets/RoadDemo" / name for name in (
    "CoreBlockCatalog.cs", "CoreAmenityLayout.cs", "CoreServicePlan.cs", "RasterGateways.cs", "CoreRegion.cs", "PortIndustryLayout.cs", "StreetNames.cs")]
sources += list(OUT.glob("*.cs"))
sources += [ROOT / "Tools/RegionSim/Contracts.cs", ROOT / "Tools/RegionSim/Program.cs"]
digest = hashlib.sha256()
for source in sources:
    digest.update(source.read_bytes())
print("Region model snapshot:", digest.hexdigest(), "Output:", OUT, flush=True)
project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
props = ET.SubElement(project, "PropertyGroup")
for key, value in {"OutputType": "Exe", "TargetFramework": "net10.0", "EnableDefaultCompileItems": "false", "WarningLevel": "0"}.items():
    ET.SubElement(props, key).text = value
items = ET.SubElement(project, "ItemGroup")
for source in sources:
    ET.SubElement(items, "Compile", Include=str(source))
ET.SubElement(items, "EmbeddedResource", Include=str(ROOT / "Assets/Resources/ResidentialDecor.json"), LogicalName="ResidentialDecor.json")
ET.ElementTree(project).write(OUT / "RegionSim.csproj", encoding="unicode")
result = subprocess.run(["dotnet", "run", "--project", str(OUT / "RegionSim.csproj"), "-c", "Release"], cwd=ROOT)
raise SystemExit(result.returncode)
