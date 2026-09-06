#!/usr/bin/env python3
"""Exercise the actual crew visibility policy offline, with managed actor fixtures.

Extracts the visibility methods verbatim. Scene lifetime and renderers are doubles; block collection and spatial queries
are extracted too; this is not a Unity/Play verdict. --baseline reads HEAD.
"""
from pathlib import Path
import hashlib
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[2]
name = "Assets/RoadDemo/DemoCrews.cs"
source = (subprocess.check_output(["git", "show", "HEAD:" + name], cwd=root).decode("utf-8-sig")
          if "--baseline" in sys.argv else (root / name).read_text(encoding="utf-8-sig"))


def method(signature, source=source):
    start = source.index(signature)
    brace = source.index("{", start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


signatures = ["bool IMapVisionAreaSource.IsVisible(", "void RefreshMapVisionBlocks(",
              "bool SameMapVisionBlocks(", "void CollectMapVisionBlocks(",
              "bool FreeRoamMapVisible(", "static CityBlocks.BlockInfo ClosestBlock(",
              "static float SqrDistanceTo("]
if "void CacheMapVisionEye(" in source:
    signatures.append("void CacheMapVisionEye(")
methods = "\n".join(method(signature) for signature in signatures)
a = source.index("        const float MapStreetVisionDepth")
b = source.index("        int _mapVisionFrame", a)
fields = source[a:source.index(";", b) + 1]
block_source = (root / "Assets/Scripts/Gameplay/CityBlocks.cs").read_text()
block_fields = block_source[block_source.index("        const float CellSize"):block_source.index("        public static IReadOnlyList<BlockInfo> Blocks")]
block_methods = "\n".join(method(signature, block_source) for signature in
                          ("public sealed class BlockInfo", "public static BlockInfo At(", "static void BuildGrid("))
program = r'''
using System;
using System.Collections.Generic;
using UnityEngine;
interface IMapVisionAreaSource { bool IsVisible(Vector3 at); }
enum BlockZone { Residential }
static class Time { public static int frameCount; }
class Actor { public bool Dead; public Body Tf = new Body(); }
class Body { public Vector3 position; public Activity gameObject = new Activity(); }
class Activity { public bool activeInHierarchy = true; }
class Unit {
    public int Faction; public Vector3? Billet;
    public List<Actor> Members = new List<Actor>();
    public int Reads;
    public IEnumerable<Actor> All() { Reads++; return Members; }
}
static class CrewQuarters {
    public static bool TryGetDoorstep(Unit u, out Vector3 at) {
        at = u.Billet ?? Vector3.zero; return u.Billet.HasValue;
    }
}
static class CityBlocks {
    public static List<BlockInfo> Known = new List<BlockInfo>();
    public static IReadOnlyList<BlockInfo> Blocks => Known;
    static int builtFrame = -1;
    static void EnsureCollected() {
        if (builtFrame == Time.frameCount) return;
        BuildGrid(); builtFrame = Time.frameCount;
    }
__BLOCKS__
}
class Policy : IMapVisionAreaSource {
    public List<Unit> Units = new List<Unit>();
    int _mapVisionVersion;
    public bool Visible(float x, float z, bool nextFrame = true) {
        if (nextFrame) Time.frameCount++;
        return ((IMapVisionAreaSource)this).IsVisible(new Vector3(x, 0, z));
    }
__POLICY__
}
class Program {
    static int checks;
    static void Check(bool value, string scenario) {
        if (!value) throw new Exception(scenario); checks++;
    }
    static void Main() {
        var p = new Policy();
        var home = new CityBlocks.BlockInfo { Id = 1, Union = new Rect(0, 0, 100, 100) };
        home.Slabs.Add(home.Union);
        var hidden = new CityBlocks.BlockInfo { Id = 2, Union = new Rect(145, 0, 100, 100) };
        hidden.Slabs.Add(new Rect(145, 0, 100, 20)); hidden.Slabs.Add(new Rect(145, 80, 100, 20));
        hidden.Slabs.Add(new Rect(145, 20, 10, 60)); hidden.Slabs.Add(new Rect(235, 20, 10, 60));
        CityBlocks.Known.Add(home); CityBlocks.Known.Add(hidden);
        var rider = new Actor(); rider.Tf.position = new Vector3(1000, 7, 50);
        var unit = new Unit(); unit.Members.Add(rider); p.Units.Add(unit);
        Check(p.Visible(1000, 50), "crew car disappeared over 500 m from every block");
        Check(p.Visible(1059, 50), "nearby traffic hidden on regional road");
        Check(!p.Visible(1061, 50), "sight escaped the crew radius");
        rider.Tf.position = new Vector3(1200, 0, 50);
        Check(!p.Visible(1000, 50), "old road position remained revealed after departure");
        Check(p.Visible(1200, 50), "sight did not move with the rider");
        rider.Dead = true; Check(!p.Visible(1200, 50), "dead rider supplies sight");
        rider.Dead = false; rider.Tf.gameObject.activeInHierarchy = false;
        Check(!p.Visible(1200, 50), "inactive actor supplies roadside sight");
        unit.Billet = new Vector3(1200, 0, 50);
        Check(p.Visible(1200, 50), "indoor crew lost its doorstep sight");
        unit.Billet = null; rider.Tf.gameObject.activeInHierarchy = true; unit.Faction = 1;
        Check(!p.Visible(1200, 50), "rival supplies player sight");
        unit.Faction = 0; rider.Tf.position = new Vector3(105, 0, 50);
        Check(p.Visible(50, 50), "nearest held block lost its intelligence");
        Check(p.Visible(125, 50), "held block street lost its fringe");
        Check(p.Visible(140, 50), "road gap outside block bounds lost crew sight");
        Check(!p.Visible(149, 10), "unrevealed block slab leaked through radial sight");
        Check(!p.Visible(164, 50), "unrevealed multi-slab courtyard leaked through radial sight");
        Check(CityBlocks.At(new Vector2(164, 50)) == null, "courtyard fixture is a slab");
        int reads = unit.Reads;
        for (int i = 0; i < 100; i++)
            if (p.Visible(1000, 50, false)) throw new Exception("far actor is visible");
        Check(unit.Reads == reads, "fog queries enumerated personnel again in the same frame");
        CityBlocks.Known.Clear();
        Check(p.Visible(150, 50), "open-floor scene lost free-roam sight");
        p.Units.Clear(); Check(!p.Visible(150, 50), "absent crew supplies sight");
        Console.WriteLine($"PASSED {checks} crew visibility cases; managed fixtures, no Unity/Play verdict.");
    }
}
'''.replace("__POLICY__", fields + "\n" + methods).replace("__BLOCKS__", block_fields + block_methods)
out = Path(tempfile.mkdtemp(prefix="gangsters-vision-"))
(out / "Program.cs").write_text(program)
version = (root / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split()[1]
scripting = Path("/Applications/Unity/Hub/Editor") / version / "Unity.app/Contents/Resources/Scripting"
project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
props = ET.SubElement(project, "PropertyGroup")
for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0", "WarningLevel": "0"}.items():
    ET.SubElement(props, key).text = value
items = ET.SubElement(project, "ItemGroup")
reference = ET.SubElement(items, "Reference", Include="UnityEngine.CoreModule")
ET.SubElement(reference, "HintPath").text = str(scripting / "Managed/UnityEngine/UnityEngine.CoreModule.dll")
ET.ElementTree(project).write(out / "VisionSim.csproj", encoding="unicode")
print("Crew source SHA256:", hashlib.sha256(source.encode()).hexdigest(), "Output:", out, flush=True)
print("Block source SHA256:", hashlib.sha256(block_source.encode()).hexdigest(), flush=True)
raise SystemExit(subprocess.run([str(scripting / "DotNetSdk/dotnet"), "run", "--project",
                                str(out / "VisionSim.csproj"), "-c", "Release"], cwd=root).returncode)
