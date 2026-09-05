"""Summarize recorded live player trials against the currently loaded runtime DLL."""
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
runtime = ROOT / 'Library/ScriptAssemblies/Assembly-CSharp.dll'
fingerprint = hashlib.sha256(runtime.read_bytes()).hexdigest()
groups = defaultdict(Counter)
passing = defaultdict(list)
failures = []
for result_path in sorted((ROOT / 'Temp/player-trials').glob('*/*/result.json')):
    build_path = result_path.parent / 'build-sha256.json'
    if not build_path.exists():
        continue
    try:
        build = json.loads(build_path.read_text(encoding='utf-8'))
        if build['files'].get('Library/ScriptAssemblies/Assembly-CSharp.dll') != fingerprint:
            continue
        result = json.loads(result_path.read_text(encoding='utf-8'))
    except (json.JSONDecodeError, KeyError):
        continue
    kind, status = result['kind'], result['status']
    if result.get('loaded_campaign'):
        kind += '_loaded'
    elif result.get('resuming'):
        kind += '_resumed'
    groups[kind][status] += 1
    evidence = str(result_path.parent.relative_to(ROOT)).replace('\\', '/')
    if status == 'PASS':
        passing[kind].append(evidence)
    elif status == 'FAIL':
        failures.append({'kind': kind, 'reason': result['reason'], 'evidence': evidence})
print(json.dumps({'runtime_sha256': fingerprint, 'counts': groups,
                  'passing_evidence': passing, 'failures': failures}, indent=2))
