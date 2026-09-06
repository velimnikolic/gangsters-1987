#!/usr/bin/env python3
"""Author or check the sedan lineup without launching/contacting Unity."""
import argparse
import hashlib
import json
from pathlib import Path
from artwork import make_palette, make_surface, font_path
from palette import COLORS
from sedans import build_car
from bodywork import Coachwork
from interiors import seat_roots
import showroom
import utilities
from utility_details import seat_layout
import synty_lamps
import unity_assets as ua

HERE = Path(__file__).resolve().parent
MANIFEST = HERE/'manifest.json'


def fingerprint():
    inputs = [HERE/name for name in ('artwork.py', 'build.py', 'geometry.py', 'palette.py',
              'sedans.py', 'bodywork.py', 'cabins.py', 'fascias.py', 'wheels.py', 'lenses.py',
              'showroom.py', 'unity_assets.py', 'lineup.json', 'scene_settings.txt',
              'requirements.txt','interiors.py','synty_lamps.py','utilities.py','utilities.json',
              'utility_cabin.py','utility_details.py','window_frames.py','roof_skin.py','fascia_skin.py')]
    inputs += [ua.ROOT/(showroom.REFERENCE+suffix) for suffix in ('','.meta')]
    inputs += [ua.ROOT/(path+suffix) for path in (synty_lamps.MATERIAL,synty_lamps.ALBEDO,synty_lamps.EMISSION,synty_lamps.SHADER) for suffix in ('','.meta')]
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
    assert (ua.ROOT/showroom.REFERENCE).is_file() and (ua.ROOT/(showroom.REFERENCE+'.meta')).is_file(), 'Missing original Synty reference prefab'
    cars = json.loads((HERE/'lineup.json').read_text())
    assert len(cars) == 8 and len({c['id'] for c in cars}) == 8
    assert all(a['price'] > b['price'] for a, b in zip(cars, cars[1:]))
    extra=json.loads((HERE/'utilities.json').read_text())
    assert len(extra)==6 and not ({c['id'] for c in extra}&{c['id'] for c in cars})
    cars += extra
    authored=[utilities.build_car(car) if car.get('utility') else build_car(car) for car in cars]
    for car,(body,wheels,lamps,_,glazing) in zip(cars,authored):
        count=sum(len(list(m.triangles())) for m in [body,lamps,glazing]+[w for w,_ in wheels])
        budget=7500 if car.get('utility') else 7000
        assert count<=budget, (car['id'],'Exceeded triangle budget including interior',count,budget)
    for script in ('SedanShowroom','VehicleLampRig','VehicleSeatRig'):
        ua.meta(f'Assets/RoadDemo/{script}.cs', 'MonoImporter', '''  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
''')
    material = ua.material('SedanPalette', make_palette(),surface=make_surface())
    lamp_material = synty_lamps.MATERIAL
    glass_material=ua.material('SedanGlass',f'{ua.ASSET}/Textures/SedanPalette.png',transparent=True)
    prefabs, stats = [], []
    for car,(body,wheels,lamps,anchors,glazing) in zip(cars,authored):
        prefab = ua.Hierarchy()
        root = prefab.node(car['name'])
        seats=seat_layout(Coachwork(car)) if car.get('utility') else seat_roots(Coachwork(car))
        prefab.mono(root,'Assets/RoadDemo/VehicleSeatRig.cs',''.join(
            f'  {name}: {ua.v3(point)}\n' for name,point in zip(
                ('frontLeft','frontRight','rearLeft','rearRight'),seats))+f'  ceiling: {car["height"]-.10:.4f}\n')
        hull = prefab.node('Body', parent=root['tf'])
        prefab.renderer(hull, ua.mesh_asset(body, list(COLORS)), material)
        # Wheel pivots are separate and centred at the axles for later shared rigging.
        for wheel, position in wheels:
            node = prefab.node('Wheel_'+wheel.name.rsplit('_', 1)[-1], parent=root['tf'], position=position)
            prefab.renderer(node, ua.mesh_asset(wheel, list(COLORS)), material)
        node=prefab.node('Lamp lenses', parent=root['tf'])
        lamp_renderer=prefab.renderer(node,ua.mesh_asset(lamps,list(COLORS)),lamp_material)
        prefab.mono(root,'Assets/RoadDemo/VehicleLampRig.cs',f'''  leftHeadlight: {ua.v3(anchors[0])}
  rightHeadlight: {ua.v3(anchors[1])}
  lenses: {{fileID: {lamp_renderer}}}
''')
        node=prefab.node('Transparent windows',parent=root['tf'])
        prefab.renderer(node,ua.mesh_asset(glazing,list(COLORS)),glass_material,shadows=False)
        path = f'{ua.ASSET}/Prefabs/{car["id"]}.prefab'
        ua.write(path, ua.HEADER+prefab.text())
        ua.meta(path, 'PrefabImporter', '')
        prefabs.append(path)
        triangles = sum(len(list(mesh.triangles())) for mesh in [body,lamps,glazing]+[w for w, _ in wheels])
        stats.append(dict(id=car['id'], triangles=triangles, renderers=7, materials=3,wheelbase=car['wheelbase']))
    showroom.build(cars[:8], prefabs[:8], material, cars[8:], prefabs[8:])
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
