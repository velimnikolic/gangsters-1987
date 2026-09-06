#!/usr/bin/env python3
"""Exercise production sun motion offline; no Unity, rendering or Play access.

Extracts DemoSky's motion fields and methods verbatim. Unity clock/transform/math
are test doubles; System.Numerics supplies quaternion interpolation.
--baseline runs the same contracts against HEAD to reproduce the regression.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--baseline', action='store_true')
args = parser.parse_args()
output = Path(tempfile.mkdtemp(prefix='gangsters-sky-sim-'))
name = 'Assets/RoadDemo/DemoSky.cs'
data = (subprocess.run(['git', 'show', 'HEAD:' + name], cwd=ROOT, check=True,
                       capture_output=True).stdout
        if args.baseline else (ROOT / name).read_bytes())
source = data.decode('utf-8-sig')
fields = source[source.index('        // -- the day\'s shape'):
                source.index('        // -- night')]
methods = source[source.index('        void ApplySun('):
                 source.index('        // The moon comes up')]
(output / 'DemoSky.cs').write_text('''using UnityEngine;
namespace RoadDemo { public partial class DemoSky {
''' + fields + methods + '\n} }\n', encoding='utf-8')
digest = hashlib.sha256(name.encode() + data + Path(__file__).read_bytes())
for filename in ('Program.cs', 'Stubs.cs'):
    data = (ROOT / 'Tools/SkySim' / filename).read_bytes()
    digest.update(filename.encode() + data)
    (output / filename).write_bytes(data)
sdk = int(subprocess.run(['dotnet', '--list-sdks'], check=True,
                         capture_output=True, text=True).stdout
          .splitlines()[-1].split()[0].split('.')[0])
(output / 'SkySim.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net{sdk}.0</TargetFramework>
    <EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
</Project>''', encoding='utf-8')
snapshot = digest.hexdigest()
print('Source snapshot:', snapshot, 'Output:', output, flush=True)
result = subprocess.run(['dotnet', 'run', '--project', str(output / 'SkySim.csproj'),
                         '--verbosity', 'quiet'], cwd=output)
(output / 'result.json').write_text(json.dumps({
    'source': snapshot, 'baseline': args.baseline, 'passed': result.returncode == 0,
    'scope': 'production sun motion with scripted clock and quaternion doubles; no rendering/Unity/Play',
}, indent=2), encoding='utf-8')
raise SystemExit(result.returncode)
