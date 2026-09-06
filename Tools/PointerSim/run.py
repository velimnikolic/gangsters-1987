#!/usr/bin/env python3
"""Exercise street right-click polling offline with scripted device/UI frames.

Compiles the production gesture, camera pointer block, CrewOverlay admission and
cover-aim cancellation prefixes.
The dispatch endpoint is observed; physics, menus and Unity input delivery are not
simulated. --baseline checks the same contracts against HEAD's admission code.
All compiler output goes to a temporary directory. Never contacts Unity.
"""
import argparse
import hashlib
import json
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--baseline', action='store_true')
args = parser.parse_args()
output = Path(tempfile.mkdtemp(prefix='gangsters-pointer-sim-'))
digest = hashlib.sha256()


def source(name, baseline=False):
    data = (subprocess.run(['git', 'show', 'HEAD:' + name], cwd=ROOT,
                           check=True, capture_output=True).stdout
            if baseline else (ROOT / name).read_bytes())
    digest.update(name.encode() + data)
    return data.decode('utf-8-sig')


(output / 'RightPointerGesture.cs').write_text(
    source('Assets/RoadDemo/RightPointerGesture.cs'), encoding='utf-8')
overlay = source('Assets/RoadDemo/CrewOverlay.cs', args.baseline)
start = overlay.index('        void ReadRightClick(Mouse mouse)')
end = overlay.index('            // With NO crew picked', start)
admission = overlay[start:end]
# Keep the real early returns and press/release ordering. Stop immediately before
# the world pick/dispatch chain and record that an order click reached it.
(output / 'Admission.cs').write_text('''
using UnityEngine;
using UnityEngine.InputSystem;
namespace RoadDemo {
    partial class CrewOverlay {
''' + admission + '''
            Orders++;
        }
    }
}
''', encoding='utf-8')
camera = source('Assets/RoadDemo/DemoCamera.cs')
start = camera.index('        void LateUpdate()')
sample = camera[start:camera.index('            float dt =', start)]
start = camera.index('            var mouse =', start)
camera_pointer = camera[start:camera.index('                // THE LEFT BUTTON', start)]
(output / 'CameraPointer.cs').write_text('''
using UnityEngine;
using UnityEngine.InputSystem;
namespace RoadDemo { partial class DemoCamera {
''' + sample + camera_pointer + '''
            }
        }
    }
}
''', encoding='utf-8')
cover = source('Assets/RoadDemo/CrewOverlay.CoverAim.cs')
start = cover.index('        void TickCoverAim(Mouse mouse)')
cancel = cover[start:cover.index('            var px =', start)]
start = cover.index('        void EndCoverAim(bool order)')
cleanup = cover[start:cover.index('            if (!order ||', start)]
(output / 'CoverCancellation.cs').write_text('''
using UnityEngine;
using UnityEngine.InputSystem;
namespace RoadDemo { partial class CrewOverlay {
''' + cancel + '''
        }
''' + cleanup + '''
            if (order) CoverOrders++;
        }
    }
}
''', encoding='utf-8')
for name in ('Program.cs', 'Stubs.cs'):
    (output / name).write_text(source('Tools/PointerSim/' + name), encoding='utf-8')
digest.update(Path(__file__).read_bytes())
sdk = int(subprocess.run(['dotnet', '--list-sdks'], check=True,
                         capture_output=True, text=True).stdout
          .splitlines()[-1].split()[0].split('.')[0])
(output / 'PointerSim.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net{sdk}.0</TargetFramework>
    <EnableNETAnalyzers>false</EnableNETAnalyzers><NoWarn>0169;0414;0649</NoWarn>
  </PropertyGroup>
</Project>''', encoding='utf-8')
snapshot = digest.hexdigest()
print('Source snapshot:', snapshot, 'Output:', output, flush=True)
result = subprocess.run(['dotnet', 'run', '--project', str(output / 'PointerSim.csproj'),
                         '--verbosity', 'quiet'], cwd=output)
(output / 'result.json').write_text(json.dumps({
    'source': snapshot, 'baseline_admission': args.baseline,
    'passed': result.returncode == 0,
    'scope': 'scripted pointer/UI frames and production order admission; no Unity/physics/Play',
}, indent=2), encoding='utf-8')
raise SystemExit(result.returncode)
