#!/usr/bin/env python3
"""Author or check the seven sedan assets without launching/contacting Unity."""
import argparse
import hashlib
import json
from pathlib import Path
from artwork import make_palette, font_path
from palette import COLORS
from sedans import build_car
import showroom
import unity_assets as ua

HERE = Path(__file__).resolve().parent
MANIFEST = HERE/'manifest.json'


def fingerprint():
    inputs = list(HERE.glob('*.py')) + [HERE/'lineup.json', HERE/'scene_settings.txt']
    return {str(p.relative_to(ua.ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(inputs)}


def check():
    if not MANIFEST.exists():
        raise SystemExit('Missing manifest; generate assets first.')
    saved = json.loads(MANIFEST.read_text())
    errors = []
    if saved['inputs'] != fingerprint():
        errors.append('Generator inputs changed; regenerate assets.')
    for path, digest in saved['outputs'].items():
        p = ua.ROOT/path
        if not p.exists() or hashlib.sha256(p.read_bytes()).hexdigest() != digest:
            errors.append('Missing or stale: '+path)
    if errors:
        raise SystemExit('\n'.join(errors))
    print(f'FRESH: {len(saved["outputs"])} generated files match the recorded inputs.')


def generate():
    cars = json.loads((HERE/'lineup.json').read_text())
    assert len(cars) == 7 and len({c['id'] for c in cars}) == 7
    assert all(a['price'] > b['price'] for a, b in zip(cars, cars[1:]))
    ua.meta('Assets/RoadDemo/SedanShowroom.cs', 'MonoImporter', '''  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
''')
    material = ua.material('SedanPalette', make_palette())
    prefabs, stats = [], []
    for car in cars:
        body, wheels = build_car(car)
        prefab = ua.Hierarchy()
        root = prefab.node(car['name'])
        hull = prefab.node('Body', parent=root['tf'])
        prefab.renderer(hull, ua.mesh_asset(body, list(COLORS)), material)
        # Wheel pivots are separate and centred at the axles for later shared rigging.
        for wheel, position in wheels:
            node = prefab.node(wheel.name.rsplit('_', 1)[-1], parent=root['tf'], position=position)
            prefab.renderer(node, ua.mesh_asset(wheel, list(COLORS)), material)
        path = f'{ua.ASSET}/Prefabs/{car["id"]}.prefab'
        ua.write(path, ua.HEADER+prefab.text())
        ua.meta(path, 'PrefabImporter', '')
        prefabs.append(path)
        triangles = sum(len(list(mesh.triangles())) for mesh in [body]+[w for w, _ in wheels])
        stats.append(dict(id=car['id'], triangles=triangles, renderers=5, wheelbase=car['wheelbase']))
    showroom.build(cars, prefabs, material)
    # Include existing deterministic folder metas too, so every generated artifact
    # participates in freshness and repeated builds have the same manifest.
    for path in (ua.ROOT/ua.ASSET).rglob('*'):
        if path.is_file():
            ua.WRITTEN[str(path.relative_to(ua.ROOT))] = hashlib.sha256(path.read_bytes()).hexdigest()
    folder = ua.ROOT/(ua.ASSET+'.meta')
    ua.WRITTEN[str(folder.relative_to(ua.ROOT))] = hashlib.sha256(folder.read_bytes()).hexdigest()
    manifest = dict(inputs=fingerprint(), outputs=dict(sorted(ua.WRITTEN.items())), cars=stats,
                    font_sha256=hashlib.sha256(font_path().read_bytes()).hexdigest())
    MANIFEST.write_text(json.dumps(manifest, indent=2)+'\n')
    for car in stats:
        print(f'{car["id"]}: {car["triangles"]} triangles, {car["renderers"]} renderers')
    print('Authored '+showroom.SCENE)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='Read-only generated asset freshness check')
    args = parser.parse_args()
    check() if args.check else generate()
