#!/usr/bin/env python3
"""Check district map geometry against a successful, current offline compile.

Usage: python3 Tools/TurfMapSim/run.py <gangsters-compile-output-directory>
Never contacts Unity. Executes only managed value types and raster buffers.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
compiled = Path(sys.argv[1]).resolve()
verdict = json.loads((compiled / "result.json").read_text())
spec = importlib.util.spec_from_file_location("project_checks", ROOT / "Tools/project.py")
checks = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checks)
if not verdict.get("passed") or verdict["source"] != checks.fingerprint():
    raise SystemExit("UNVERIFIED: compile does not match current source")

version = (ROOT / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split()[1]
unity = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Unity/Hub/Editor" / version / "Editor/Data"
out = Path(tempfile.mkdtemp(prefix="gangsters-turf-map-"))
project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
props = ET.SubElement(project, "PropertyGroup")
for key, value in {"OutputType": "Exe", "TargetFramework": "net10.0", "EnableDefaultCompileItems": "false"}.items():
    ET.SubElement(props, key).text = value
items = ET.SubElement(project, "ItemGroup")
program = ROOT / "Tools/TurfMapSim/Program.cs"
program_hash = hashlib.sha256(program.read_bytes()).hexdigest()
ET.SubElement(items, "Compile", Include=str(program))
for name, path in {
    "Assembly-CSharp": compiled / "Assembly-CSharp.dll",
    "UnityEngine.CoreModule": unity / "Managed/UnityEngine/UnityEngine.CoreModule.dll",
}.items():
    reference = ET.SubElement(items, "Reference", Include=name)
    ET.SubElement(reference, "HintPath").text = str(path)
ET.ElementTree(project).write(out / "TurfMapSim.csproj", encoding="unicode")
print("Runtime snapshot:", verdict["source"], "Harness:", program_hash, "Output:", out, flush=True)
result = subprocess.run(["dotnet", "run", "--project", str(out / "TurfMapSim.csproj"), "-c", "Release"], cwd=ROOT)
if checks.fingerprint() != verdict["source"] or hashlib.sha256(program.read_bytes()).hexdigest() != program_hash:
    raise SystemExit("UNVERIFIED: source changed during the geometry checks")
raise SystemExit(result.returncode)
