#!/usr/bin/env python3
"""Run actual managed regional road/terrain models without Unity or Editor access."""
import argparse
import hashlib
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("compile_output", type=Path)
parser.add_argument("unity_core", type=Path)
parser.add_argument("fixtures", type=Path, help="RegionSim output from GANGSTERS_REGION_FIXTURES")
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
out = Path(tempfile.mkdtemp(prefix="gangsters-island-model-"))
project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
props = ET.SubElement(project, "PropertyGroup")
for key, value in {"OutputType":"Exe", "TargetFramework":"net10.0", "EnableDefaultCompileItems":"false"}.items():
    ET.SubElement(props, key).text = value
items = ET.SubElement(project, "ItemGroup")
ET.SubElement(items, "Compile", Include=str(root / "Tools/IslandSim/Program.cs"))
ET.SubElement(items, "Compile", Include=str(root / "Tools/IslandSim/Regression.cs"))
ET.SubElement(items, "Compile", Include=str(root / "Tools/IslandSim/FreightNetwork.cs"))
runtime = args.compile_output.resolve() / "Assembly-CSharp.dll"
for source in [runtime, *sorted(args.unity_core.resolve().parent.glob("UnityEngine*.dll"))]:
    reference = ET.SubElement(items, "Reference", Include=source.stem)
    ET.SubElement(reference, "HintPath").text = str(source)
    if source in (runtime, args.unity_core.resolve()):
        print(source.name, hashlib.sha256(source.read_bytes()).hexdigest(), flush=True)
ET.ElementTree(project).write(out / "IslandSim.csproj", encoding="unicode")
print("Output:", out, flush=True)
raise SystemExit(subprocess.run(["dotnet","run","--project",str(out/"IslandSim.csproj"),"-c","Release","--",
    str(args.fixtures.resolve()),str(out/"island.csv")], cwd=root).returncode)
