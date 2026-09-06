#!/usr/bin/env python3
"""Run the actual graph-detour C# with managed Unity math, without an Editor.

Only unused scene dependencies are stubbed; SidewalkPlan collision math is real. The clearance oracle and
scenarios live in PedestrianGraphDetourTests; this is not scene/Play acceptance.
"""
import hashlib
import os
import re
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
version = (ROOT / 'ProjectSettings/ProjectVersion.txt').read_text().split()[1]
if os.name == 'nt':
    unity = Path(os.environ.get('ProgramFiles', 'C:/Program Files')) / 'Unity/Hub/Editor' / version / 'Editor/Data'
    sdk = unity / 'DotNetSdk'
    managed = unity / 'Managed/UnityEngine'
else:
    unity = Path('/Applications/Unity/Hub/Editor') / version / 'Unity.app/Contents'
    sdk = unity / 'Resources/Scripting/DotNetSdk'
    managed = unity / 'Resources/Scripting/Managed/UnityEngine'
output = Path(tempfile.mkdtemp(prefix='gangsters-pedestrian-sim-'))
sources = [
    'Assets/RoadDemo/PedestrianGraph.cs',
    'Assets/RoadDemo/PedestrianGraphDetour.cs',
    'Assets/RoadDemo/WalkHeap.cs',
    'Assets/RoadDemo/SidewalkPlan.cs',
    'Assets/Scripts/Tests/PedestrianGraphDetourTests.cs',
]
digest = hashlib.sha256()
for name in sources:
    data = (ROOT / name).read_bytes()
    digest.update(name.encode() + data)
    (output / Path(name).name).write_bytes(data)

# Keep the production corridor limits verbatim; do not maintain a second rule.
agent = (ROOT / 'Assets/RoadDemo/PedestrianAgent.cs').read_text(encoding='utf-8-sig')
constants = []
for name in ('PavementLane', 'ZebraSlip'):
    constants.append(re.search(r'public const float ' + name + r' = [^;]+;', agent)[0])
digest.update(agent.encode()) # integration edits invalidate this snapshot too; they still require Unity to execute
digest.update((ROOT / 'Assets/RoadDemo/WalkObstacles.cs').read_bytes())
digest.update(Path(__file__).read_bytes())
(output / 'Support.cs').write_text('''
namespace RoadDemo {
    public class TrafficSignal { }
    public static class DemoParkedCarGlow {
        public static string MarkerRootName => throw new System.NotSupportedException("Mesh scanning is not simulated");
    }
    public class PedestrianAgent { ''' + '\n'.join(constants) + ''' }
}
class Program {
    static int Main() {
        var failures = LivingCity.Tests.PedestrianGraphDetourTests.Run();
        foreach (var failure in failures) System.Console.WriteLine(failure);
        System.Console.WriteLine(failures.Count == 0 ? "PASSED: 33 graph-detour scenarios" : "FAILED");
        return failures.Count == 0 ? 0 : 1;
    }
}
''')
core = managed / 'UnityEngine.CoreModule.dll'
references = ''.join(f'<Reference Include="{p.stem}"><HintPath>{p}</HintPath></Reference>' for p in sorted(core.parent.glob('*.dll')))
runtime = sorted((sdk / 'shared/Microsoft.NETCore.App').iterdir())[-1].name
framework = 'net' + runtime.split('.')[0] + '.0'
(output / 'Sim.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>{framework}</TargetFramework>
<EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
<ItemGroup>{references}</ItemGroup>
</Project>''')
print('Source snapshot:', digest.hexdigest(), 'Output:', output, flush=True)
result = subprocess.run([str(sdk / ('dotnet.exe' if os.name == 'nt' else 'dotnet')), 'run', '--project', str(output / 'Sim.csproj'),
                         '--verbosity', 'quiet'], cwd=output)
raise SystemExit(result.returncode)
