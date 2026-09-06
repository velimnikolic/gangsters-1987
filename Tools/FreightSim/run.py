#!/usr/bin/env python3
"""Exercise the actual freight scheduler and road bodies with RoadSim's Unity stand-ins.

The regional composition entry point alone is excluded; joined-region geometry is
checked separately by IslandSim against the compiled runtime assembly.
"""
from pathlib import Path
import hashlib, subprocess, tempfile, xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[2]
out=Path(tempfile.mkdtemp(prefix="gangsters-freight-"))
tree=ET.parse(root/"Tools/RoadSim/RoadSim.csproj")
for item in tree.findall(".//Compile"):
    item.set("Include",str((root/"Tools/RoadSim"/item.get("Include")).resolve()))
props=tree.find("PropertyGroup")
props.find("TargetFramework").text="net10.0"
ET.SubElement(props,"StartupObject").text="FreightCheck"
src=(root/"Assets/RoadDemo/IndustrialFreight.cs").read_text()
src=src[:src.index("        public static void Connect(CoreRegion")] + "    }\n}\n"
(out/"IndustrialFreight.cs").write_text(src)
items=tree.find("ItemGroup")
for file in [out/"IndustrialFreight.cs",root/"Tools/FreightSim/Program.cs"]:
    ET.SubElement(items,"Compile",Include=str(file))
digest=hashlib.sha256()
for item in tree.findall(".//Compile"): digest.update(Path(item.get("Include")).read_bytes())
print("Freight model snapshot:",digest.hexdigest(),"Output:",out,flush=True)
tree.write(out/"FreightSim.csproj",encoding="unicode")
raise SystemExit(subprocess.run(["dotnet","run","--project",str(out/"FreightSim.csproj"),"-c","Release"],cwd=root).returncode)
