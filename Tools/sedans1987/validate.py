#!/usr/bin/env python3
"""Read-only checks of serialized sedan output; no Unity import or Play claims."""
import json
from pathlib import Path
import re
import subprocess
import numpy as np
from build import check
from preview import Assets, documents
from showroom import SCENE
from check_wheels import closed_tire,sidewall_coverage
import unity_assets as ua


def validate():
    check()
    manifest = json.loads((ua.ROOT/'Tools/sedans1987/manifest.json').read_text())
    files = [ua.ROOT/p for p in manifest['outputs']]
    # Check new external GUIDs against actual project/package metas, not invented paths.
    result = subprocess.run(['rg', '--files', '--hidden', '--no-ignore', '-g', '*.meta',
                             'Assets', 'Packages', 'Library/PackageCache'], cwd=ua.ROOT,
                            check=True, capture_output=True, text=True)
    guids = set()
    guid_paths = {}
    for name in result.stdout.splitlines():
        match = re.search(r'^guid: ([0-9a-f]{32})', (ua.ROOT/name).read_text(), re.M)
        if match:
            guids.add(match[1])
            guid_paths.setdefault(match[1], []).append(name)
    checked_refs = 0
    for path in files:
        if path.suffix in ('.asset', '.prefab', '.unity', '.mat', '.meta'):
            text = path.read_text()
            for guid in re.findall(r'guid: ([0-9a-f]{32})', text):
                assert guid in guids or guid.startswith('0000000000000000'), (path, guid)
                checked_refs += 1
        if path.suffix in ('.prefab', '.unity'):
            docs = documents(path)
            assert len(docs) == len(re.findall(r'^--- !u!', path.read_text(), re.M)), path
            for fileid in re.findall(r'\{fileID: (\d+)\}', path.read_text()):
                assert int(fileid) == 0 or int(fileid) in docs, (path, fileid)
    assets = Assets()
    # This fixed map survives Git staging/commits and proves exact destinations,
    # including uniqueness, for the 24 GUIDs retained by the fictional-name moves.
    retained_paths = json.loads((ua.ROOT/'Tools/sedans1987/retained_guids.json').read_text())
    assert len(retained_paths) == 24
    for guid, path in retained_paths.items():
        assert guid_paths.get(guid) == [path+'.meta'], ('Moved GUID has wrong or duplicate metadata', guid, path)
        assert assets.paths.get(guid) == ua.ROOT/path, ('Moved GUID has wrong destination asset', guid, path)
    # project.py audit treats unstaged asset moves as deletions. Only the known,
    # exact moves above explain this signal; an additional deletion must fail.
    deleted = subprocess.run(['git', 'diff', '--name-only', '--diff-filter=D', 'HEAD', '--', ua.ASSET],
                             cwd=ua.ROOT, check=True, capture_output=True, text=True)
    retained = 0
    for path in deleted.stdout.splitlines():
        if not path.endswith('.meta'):
            continue
        old = subprocess.run(['git', 'show', 'HEAD:'+path], cwd=ua.ROOT,
                             check=True, capture_output=True, text=True).stdout
        guid = re.search(r'^guid: ([0-9a-f]{32})', old, re.M)[1]
        assert guid in retained_paths, ('Unrecorded asset deletion', path)
        retained += 1
    meshes = [p for p in files if p.parent.name == 'Meshes' and p.suffix == '.asset']
    total_triangles = 0
    for path in meshes:
        mesh = next(iter(documents(path).values()))[1]['Mesh']
        vertices, indices = assets.mesh(ua.guid(path.relative_to(ua.ROOT)))
        assert len(vertices) == mesh['m_VertexData']['m_VertexCount'], path
        assert mesh['m_VertexData']['m_DataSize'] == vertices.nbytes, path
        assert mesh['m_SubMeshes'][0]['indexCount'] == indices.size, path
        assert indices.max() < len(vertices), path
        assert np.isfinite(vertices).all(), path
        assert np.allclose(np.linalg.norm(vertices[:, 3:6], axis=1), 1, atol=1e-5), path
        assert ((vertices[:, 10:12] >= 0) & (vertices[:, 10:12] <= 1)).all(), path
        faces = vertices[indices, :3]
        normal = np.cross(faces[:, 1]-faces[:, 0], faces[:, 2]-faces[:, 0])
        assert (np.linalg.norm(normal, axis=1) > 1e-8).all(), path
        assert ((normal*vertices[indices[:, 0], 3:6]).sum(axis=1) > 0).all(), path
        bounds = mesh['m_LocalAABB']
        center = np.array([bounds['m_Center'][a] for a in 'xyz'])
        extent = np.array([bounds['m_Extent'][a] for a in 'xyz'])
        assert np.all(np.abs(vertices[:, :3]-center) <= extent+1e-5), path
        total_triangles += len(indices)
    lineup = json.loads((ua.ROOT/'Tools/sedans1987/lineup.json').read_text())
    assert len(lineup) == 8 and all(a['price'] > b['price'] for a, b in zip(lineup, lineup[1:]))
    assert len({c['paint'] for c in lineup}) == len(lineup)
    for car in lineup:
        closed_tire(car['radius'])
        for corner in ('FL','FR','RL','RR'):
            path=f'{ua.ASSET}/Meshes/{car["id"]}_Wheel_{corner}.asset'
            vertices,indices=assets.mesh(ua.guid(path))
            sidewall_coverage(vertices,indices,car['radius'])
        path = ua.ROOT/f'{ua.ASSET}/Prefabs/{car["id"]}.prefab'
        docs = documents(path)
        assert sum(t == 23 for t, _ in docs.values()) == 5, path
        transforms = [d['Transform'] for t, d in docs.values() if t == 4]
        wheels = [t for t in transforms if t['m_LocalPosition']['y'] > .1]
        assert len(wheels) == 4, path
        z = sorted({t['m_LocalPosition']['z'] for t in wheels})
        assert abs(z[1]-z[0]-car['wheelbase']) < 1e-5, path
        objects = assets.objects(path)
        points = np.concatenate([o[0] for o in objects])
        assert abs(points[:, 1].min()) < 1e-5, path
        assert points[:, 1].max() < car['height']+.2, path
        assert points[:, 2].max()-points[:, 2].min() < car['length']+.35, path
    scene = documents(ua.ROOT/SCENE)
    instances = [d['PrefabInstance'] for t, d in scene.values() if t == 1001]
    assert len(instances) == len(lineup)
    assert len({p['m_SourcePrefab']['guid'] for p in instances}) == len(lineup)
    monos = [d['MonoBehaviour'] for t, d in scene.values() if t == 114]
    camera = next(m for m in monos if m['m_Script']['guid'] == ua.guid('Assets/RoadDemo/DemoCamera.cs'))
    assert camera['mapTransition'] == 0 and camera['showHint'] == 1
    review = next(m for m in monos if m['m_Script']['guid'] == ua.guid('Assets/RoadDemo/SedanShowroom.cs'))
    assert len(review['cars']) == len(review['labels']) == len(lineup)
    # Exercise the complete serialized hierarchy resolver used by the offline preview.
    assert len(assets.objects(ua.ROOT/SCENE)) == 6*len(lineup)+2
    print(f'PASS: {len(meshes)} meshes / {total_triangles} triangles; valid buffers, bounds, normals and UVs.')
    print(f'PASS: {checked_refs} GUID references; {len(lineup)} distinct prefabs, four grounded wheels each; camera and focus references.')
    print(f'PASS: 24 retained GUIDs resolve uniquely to their recorded destination assets; {retained} unstaged moves accounted for.')
    print(f'PASS: closed outward tire shells; {4*len(lineup)} serialized wheels opaque from both sides (6144 sample rays).')
    print('Not verified: Unity asset import, materials/lighting in URP, Play controls or manual visual acceptance.')


if __name__ == '__main__':
    validate()
