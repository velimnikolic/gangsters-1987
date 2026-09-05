"""Summarize live scenario evidence without mixing runtime builds or counting provisional flows."""
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
current = hashlib.sha256((ROOT / 'Library/ScriptAssemblies/Assembly-CSharp.dll').read_bytes()).hexdigest()
groups = defaultdict(lambda: defaultdict(list))
latest = {}
def run_order(path):
    folder = path.parent if path.name == 'result.json' else path
    return str(folder.parent), int(folder.name) if folder.name.isdigit() else -1


for path in sorted((ROOT / 'Temp/player-trials').glob('*/*/result.json'), key=run_order):
    build = path.parent / 'build-sha256.json'
    if not build.exists():
        continue
    try:
        result = json.loads(path.read_text(encoding='utf-8'))
        recorded = json.loads(build.read_text(encoding='utf-8'))
        sha = recorded['files']['Library/ScriptAssemblies/Assembly-CSharp.dll']
        template = recorded['template']
    except (ValueError, KeyError):
        continue
    kind = result['kind'] + ('_loaded' if result.get('loaded_campaign') else '_resumed' if result.get('resuming') else '')
    evidence = {'status': result['status'], 'reason': result['reason'],
                'path': str(path.parent.relative_to(ROOT)).replace('\\', '/'), 'runtime': sha,
                'template': template}
    groups[kind][(sha, template)].append(evidence)
    latest[kind] = evidence
rows = []
for kind in sorted(groups):
    current_runs = [run for (sha, template), runs in groups[kind].items() if sha == current for run in runs]
    qualifying = [(key, runs) for key, runs in groups[kind].items()
                  if sum(run['status'] == 'PASS' for run in runs) >= 5]
    best = max(qualifying, key=lambda item: max(run_order(Path(run['path'])) for run in item[1]), default=None)
    rows.append({'scenario': kind, 'current_runtime_counts': dict(Counter(r['status'] for r in current_runs)),
                 'five_passes_on_one_runtime': None if best is None else {
                     'runtime': best[0][0], 'template': best[0][1], 'is_current': best[0][0] == current,
                     'passes': [r['path'] for r in best[1] if r['status'] == 'PASS']},
                 'latest_result': latest[kind]})
print(json.dumps({'current_runtime': current, 'note': 'Only PASS counts. Runtime builds, scenario templates and loaded/fresh scenarios remain separate for five-pass qualification. Current runtime totals may include different templates. Historical passes do not certify the current build.', 'scenarios': rows}, indent=2))
