#!/usr/bin/env python3
"""Check the actual runtime graph in an offline compile output; never contacts Unity."""
from pathlib import Path
import argparse
import hashlib
import subprocess
import tempfile
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("compile_output", type=Path, help="Output directory printed by Tools/project.py compile")
parser.add_argument("unity_core", type=Path, help="Installed UnityEngine.CoreModule.dll")
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
assembly = args.compile_output.resolve() / "Assembly-CSharp.dll"
unity = args.unity_core.resolve()
for source in (assembly, unity):
    if not source.is_file():
        parser.error(f"Missing assembly: {source}")
out = Path(tempfile.mkdtemp(prefix="gangsters-region-graph-"))
project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
props = ET.SubElement(project, "PropertyGroup")
for key, value in {"OutputType": "Exe", "TargetFramework": "net10.0", "EnableDefaultCompileItems": "false"}.items():
    ET.SubElement(props, key).text = value
items = ET.SubElement(project, "ItemGroup")
ET.SubElement(items, "Compile", Include=str(root / "Tools/RegionSim/GraphCheck.cs"))
for source in (assembly, unity):
    reference = ET.SubElement(items, "Reference", Include=source.stem)
    ET.SubElement(reference, "HintPath").text = str(source)
ET.ElementTree(project).write(out / "GraphCheck.csproj", encoding="unicode")
print("Runtime assembly:", assembly, "SHA256:", hashlib.sha256(assembly.read_bytes()).hexdigest(), flush=True)
raise SystemExit(subprocess.run(["dotnet", "run", "--project", str(out / "GraphCheck.csproj"), "-c", "Release"], cwd=root).returncode)
