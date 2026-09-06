#!/usr/bin/env python3
"""Check actual harbor route/reservation models offline. Never contacts Unity."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("compile_output", type=Path)
parser.add_argument("unity_core", type=Path)
parser.add_argument("--fixtures", type=Path, help="optional current RegionSim fixtures")
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
out = Path(tempfile.mkdtemp(prefix="gangsters-harbor-model-"))
project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
props = ET.SubElement(project, "PropertyGroup")
for key, value in {"OutputType": "Exe", "TargetFramework": "net10.0", "EnableDefaultCompileItems": "false"}.items():
    ET.SubElement(props, key).text = value
items = ET.SubElement(project, "ItemGroup")
source = root / "Tools/HarborSim/Program.cs"
ET.SubElement(items, "Compile", Include=str(source))
runtime = args.compile_output.resolve() / "Assembly-CSharp.dll"
proof = {"harness": hashlib.sha256(source.read_bytes()).hexdigest()}
for assembly in [runtime, *sorted(args.unity_core.resolve().parent.glob("UnityEngine*.dll"))]:
    reference = ET.SubElement(items, "Reference", Include=assembly.stem)
    ET.SubElement(reference, "HintPath").text = str(assembly)
    if assembly in (runtime, args.unity_core.resolve()):
        proof[assembly.name] = hashlib.sha256(assembly.read_bytes()).hexdigest()
if args.fixtures:
    proof["fixtures"] = hashlib.sha256(args.fixtures.read_bytes()).hexdigest()
summary = args.compile_output / "result.json"
if summary.is_file():
    proof["compilation"] = json.loads(summary.read_text())
ET.ElementTree(project).write(out / "HarborSim.csproj", encoding="unicode")
print("Output:", out, "Snapshot:", json.dumps(proof), flush=True)
command = ["dotnet", "run", "--project", str(out / "HarborSim.csproj"), "-c", "Release", "--"]
if args.fixtures:
    command.append(str(args.fixtures.resolve()))
result = subprocess.run(command, cwd=root, capture_output=True, text=True)
(out / "run.log").write_text(result.stdout + result.stderr)
print(result.stdout + result.stderr, end="")
proof["passed"] = result.returncode == 0
(out / "result.json").write_text(json.dumps(proof, indent=2))
raise SystemExit(result.returncode)
