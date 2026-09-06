#!/usr/bin/env python3
"""Read-only checks of serialized sedan output; no Unity import or Play claims."""
import json
from pathlib import Path
import re
import subprocess
import numpy as np
from build import check
from preview import Assets, documents
from showroom import SCENE, REFERENCE, REFERENCE_ROOT, REFERENCE_BAY, placement
from check_wheels import closed_tire,sidewall_coverage
from check_cabins import check_cabin
from check_utilities import validate_utilities,no_paint_behind_glass
from palette import COLORS
from bodywork import Coachwork
import unity_assets as ua
import synty_lamps


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
    retired = {
        '1ad5c2ecec2c507d953f58e33fe6fc2f': 'Assets/Sedan1987/Materials/SedanLamps.mat.meta',
        'ab0a7c1598df5b3596c90fbae858a96a': 'Assets/Sedan1987/Textures/SedanLampEmission.png.meta',
    }
    for guid in retired:
        refs=subprocess.run(['rg','-l',guid,'Assets'],cwd=ua.ROOT,capture_output=True,text=True)
        assert refs.returncode == 1, ('Retired lamp asset still referenced',guid,refs.stdout)
    retained = 0
    for path in deleted.stdout.splitlines():
        if not path.endswith('.meta'):
            continue
        old = subprocess.run(['git', 'show', 'HEAD:'+path], cwd=ua.ROOT,
                             check=True, capture_output=True, text=True).stdout
        guid = re.search(r'^guid: ([0-9a-f]{32})', old, re.M)[1]
        assert guid in retained_paths or retired.get(guid)==path, ('Unrecorded asset deletion', path)
        retained += int(guid in retained_paths)
    meshes = [p for p in files if p.parent.name == 'Meshes' and p.suffix == '.asset']
    total_triangles = 0
    for path in meshes:
        mesh = next(iter(documents(path).values()))[1]['Mesh']
        vertices, indices = assets.mesh(ua.guid(path.relative_to(ua.ROOT)))
        assert len(vertices) == mesh['m_VertexData']['m_VertexCount'], path
        assert mesh['m_VertexData']['m_DataSize'] == vertices.nbytes, path
        assert sum(part['indexCount'] for part in mesh['m_SubMeshes']) == indices.size, path
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
    compacts=json.loads((ua.ROOT/'Tools/sedans1987/compacts.json').read_text())
    passenger=lineup+compacts
    for car in passenger:
        closed_tire(car['radius'])
        for corner in ('FL','FR','RL','RR'):
            path=f'{ua.ASSET}/Meshes/{car["id"]}_Wheel_{corner}.asset'
            vertices,indices=assets.mesh(ua.guid(path))
            sidewall_coverage(vertices,indices,car['radius'])
        path = ua.ROOT/f'{ua.ASSET}/Prefabs/{car["id"]}.prefab'
        docs = documents(path)
        assert sum(t == 23 for t, _ in docs.values()) == 7, path
        fitting=next(d['MonoBehaviour'] for t,d in docs.values() if t==114 and
                     d['MonoBehaviour']['m_Script']['guid']==ua.guid('Assets/RoadDemo/VehicleLampRig.cs'))
        assert fitting['m_Script']['guid']==ua.guid('Assets/RoadDemo/VehicleLampRig.cs')
        assert docs[fitting['lenses']['fileID']][0]==23
        assert fitting['tailMaterialIndex']==1
        materials=docs[fitting['lenses']['fileID']][1]['MeshRenderer']['m_Materials']
        assert len(materials)==2 and materials[0]==materials[1]
        lamp_mesh=documents(ua.ROOT/f'{ua.ASSET}/Meshes/{car["id"]}_Lamps.asset')[4300000][1]['Mesh']
        assert len(lamp_mesh['m_SubMeshes'])==2
        assert lamp_mesh['m_SubMeshes'][1]['firstByte']==lamp_mesh['m_SubMeshes'][0]['indexCount']*2
        glass,glass_indices=assets.mesh(ua.guid(f'{ua.ASSET}/Meshes/{car["id"]}_Lamps.asset'))
        swatches=np.all(np.isclose(glass[:,10:12],synty_lamps.LAMP_UV['lamp_front'],atol=1e-7),axis=1)
        front=glass[swatches,:3]
        assert len(front)>0
        form=Coachwork(car)
        assert front[:,1].max()<form.top(0,form.end)[1]+.012, ('Lamp above hood',car['id'])
        for side,key in [(-1,'leftHeadlight'),(1,'rightHeadlight')]:
            anchor=np.array([fitting[key][a] for a in 'xyz'])
            lens=front[front[:,0]*side>0]
            assert side*anchor[0]>0
            assert lens[:,0].min()<=anchor[0]<=lens[:,0].max()
            assert lens[:,1].min()<=anchor[1]<=lens[:,1].max()
            faces=glass[glass_indices[swatches[glass_indices[:,0]]],:3]
            a,b,c=faces[:,0],faces[:,1],faces[:,2]
            det=(b[:,1]-c[:,1])*(a[:,0]-c[:,0])+(c[:,0]-b[:,0])*(a[:,1]-c[:,1])
            valid=np.abs(det)>1e-9
            a,b,c,det=a[valid],b[valid],c[valid],det[valid]
            x,y=anchor[:2]
            u=((b[:,1]-c[:,1])*(x-c[:,0])+(c[:,0]-b[:,0])*(y-c[:,1]))/det
            v=((c[:,1]-a[:,1])*(x-c[:,0])+(a[:,0]-c[:,0])*(y-c[:,1]))/det
            hit=(u>=-1e-6)&(v>=-1e-6)&(u+v<=1.000001)
            depth=u*a[:,2]+v*b[:,2]+(1-u-v)*c[:,2]
            assert hit.any() and .002<anchor[2]-depth[hit].max()<.065, ('Beam not ahead of glass',car['id'])
        if car['style']=='vahren':
            assert 0<form.top(0,form.end)[1]-front[:,1].max()<.04, 'Oversized bonnet lip'
            assert front[:,2].max()-form.end<.036, 'Protruding headlamp glass'
        transforms = [d['Transform'] for t, d in docs.values() if t == 4]
        wheels = [t for t in transforms if t['m_LocalPosition']['y'] > .1]
        assert len(wheels) == 4, path
        wheel_names={docs[t['m_GameObject']['fileID']][1]['GameObject']['m_Name'] for t in wheels}
        assert wheel_names=={'Wheel_FL','Wheel_FR','Wheel_RL','Wheel_RR'}, (path,wheel_names)
        seats=next(d['MonoBehaviour'] for t,d in docs.values() if t==114 and
                   d['MonoBehaviour']['m_Script']['guid']==ua.guid('Assets/RoadDemo/VehicleSeatRig.cs'))
        assert len(seats['cabinPlanes'])==5
        assert seats['frontLeft']['x']<0<seats['frontRight']['x']
        assert seats['frontLeft']['z']>seats['rearLeft']['z']
        assert abs(seats['frontLeft']['y']+.43-(min(.48,car['height']-.94)+.085))<1e-5, ('Occupant off cushion',path)
        z = sorted({t['m_LocalPosition']['z'] for t in wheels})
        assert abs(z[1]-z[0]-car['wheelbase']) < 1e-5, path
        objects = assets.objects(path)
        assert sum(len(o[3]) for o in objects)<=7000, ('Triangle budget with interior',car['id'])
        mats={d['MeshRenderer']['m_Materials'][0]['guid'] for t,d in docs.values() if t==23}
        assert len(mats)==3, ('Material budget',car['id'])
        assert docs[fitting['lenses']['fileID']][1]['MeshRenderer']['m_Materials'][0]['guid']==ua.guid(synty_lamps.MATERIAL)
        points = np.concatenate([o[0] for o in objects])
        assert abs(points[:, 1].min()) < 1e-5, path
        assert points[:, 1].max() < car['height']+.2, path
        assert points[:, 2].max()-points[:, 2].min() < car['length']+.35, path
    extra=validate_utilities(assets)
    for car in passenger:no_paint_behind_glass(assets,car)
    for car in passenger+extra:check_cabin(assets,car)
    print("PASS: all 15 roof/screen borders joined; glass reflection normals continuous across triangle edges.")
    extra += compacts
    scene = documents(ua.ROOT/SCENE)
    instances = [d['PrefabInstance'] for t, d in scene.values() if t == 1001]
    assert len(instances) == len(lineup)+len(extra)+1
    assert len({p['m_SourcePrefab']['guid'] for p in instances}) == len(lineup)+len(extra)+1
    reference_guid=ua.guid(REFERENCE)
    reference=next(p for p in instances if p['m_SourcePrefab']['guid']==reference_guid)
    targets=reference['m_Modification']['m_Modifications']
    assert len(targets)==7 and all(t['target']['fileID']==REFERENCE_ROOT for t in targets)
    assert all(t['propertyPath'].startswith(('m_LocalPosition.','m_LocalRotation.')) for t in targets), 'Reference must retain original scale/materials'
    props={t['propertyPath']:float(t['value']) for t in targets}
    assert np.allclose([props['m_LocalPosition.'+a] for a in 'xyz'],placement(REFERENCE_BAY,9)[0])
    monos = [d['MonoBehaviour'] for t, d in scene.values() if t == 114]
    camera = next(m for m in monos if m['m_Script']['guid'] == ua.guid('Assets/RoadDemo/DemoCamera.cs'))
    assert camera['mapTransition'] == 0 and camera['showHint'] == 1
    review = next(m for m in monos if m['m_Script']['guid'] == ua.guid('Assets/RoadDemo/SedanShowroom.cs'))
    assert len(review['cars']) == len(review['labels']) == len(lineup)+len(extra)+1
    for car,focus in zip(lineup,review['cars']):
        assert scene[focus['fileID']][1]['Transform']['m_CorrespondingSourceObject']['guid']==ua.guid(f'{ua.ASSET}/Prefabs/{car["id"]}.prefab')
    for car,focus in zip(extra,review['cars'][9:]):
        assert scene[focus['fileID']][1]['Transform']['m_CorrespondingSourceObject']['guid']==ua.guid(f'{ua.ASSET}/Prefabs/{car["id"]}.prefab')
    ref_transform=scene[review['cars'][8]['fileID']][1]['Transform']['m_CorrespondingSourceObject']
    assert ref_transform==dict(fileID=REFERENCE_ROOT,guid=ua.guid(REFERENCE),type=3)
    # Exercise the complete serialized hierarchy resolver used by the offline preview.
    reference_objects=len(assets.objects(ua.ROOT/REFERENCE))
    assert reference_objects==16
    assert len(assets.objects(ua.ROOT/SCENE)) == 8*(len(lineup)+len(extra))+4+reference_objects
    clock=next(m for m in monos if m['m_Script']['guid']==ua.guid('Assets/Scripts/Ambient/CityClock.cs'))
    assert clock['m_Enabled']==0 and clock['running']==0, 'Review clock must not handle number keys'
    assert scene[review['clock']['fileID']][1]['MonoBehaviour']==clock
    head=scene[review['headlights']['fileID']][1]['MonoBehaviour']
    assert head['m_Script']['guid']==ua.guid('Assets/RoadDemo/DemoHeadlights.cs')
    assert head['clock']==review['clock']
    lamp_mat=documents(ua.ROOT/synty_lamps.MATERIAL)[2100000][1]['Material']
    assert lamp_mat['m_Shader']['guid']==ua.guid(synty_lamps.SHADER)
    assert next(t['_Emission_Map']['m_Texture']['guid'] for t in lamp_mat['m_SavedProperties']['m_TexEnvs'] if '_Emission_Map' in t)==ua.guid(synty_lamps.EMISSION)
    from PIL import Image
    emit=np.asarray(Image.open(ua.ROOT/synty_lamps.EMISSION).convert('RGB'))
    for key,expected in [('lamp_front',(255,255,255)),('lamp_tail',(255,0,0)),('lamp_marker',(255,189,0))]:
        u,v=synty_lamps.LAMP_UV[key];x,y=int(u*emit.shape[1]),int((1-v)*emit.shape[0])
        assert np.all(emit[y-2:y+3,x-2:x+3]==expected), ('Synty emission swatch changed',key)
    window_mat=documents(ua.ROOT/f'{ua.ASSET}/Materials/SedanGlass.mat')[2100000][1]['Material']
    floats={k:v for d in window_mat['m_SavedProperties']['m_Floats'] for k,v in d.items()}
    assert window_mat['m_CustomRenderQueue']==3000 and '_SURFACE_TYPE_TRANSPARENT' in window_mat['m_ValidKeywords']
    assert (floats['_Surface'],floats['_ZWrite'],floats['_SrcBlend'],floats['_DstBlend'])==(1,0,5,10)
    assert 'shadowcaster' in {p.casefold() for p in window_mat['disabledShaderPasses']}
    tint=next(c['_BaseColor'] for c in window_mat['m_SavedProperties']['m_Colors'] if '_BaseColor' in c)
    assert .1<=tint['a']<=.3
    body_mat=next(iter(documents(ua.ROOT/f'{ua.ASSET}/Materials/SedanPalette.mat').values()))[1]['Material']
    assert '_METALLICSPECGLOSSMAP' in body_mat['m_ValidKeywords']
    surface=next(t['_MetallicGlossMap']['m_Texture']['guid'] for t in body_mat['m_SavedProperties']['m_TexEnvs'] if '_MetallicGlossMap' in t)
    surface_path=assets.paths[surface]
    assert re.search(r'^\s+sRGBTexture: 0$',Path(str(surface_path)+'.meta').read_text(),re.M), 'Surface data must import linearly'
    from PIL import Image
    data=np.asarray(Image.open(surface_path).convert('RGBA'))
    column=lambda key:round((list(COLORS).index(key)+.5)*data.shape[1]/len(COLORS))
    assert data[0,column('rubber'),3]<data[0,column('glass'),3]
    assert data[0,column('chrome'),0]>data[0,column('silver'),0]
    assert data.shape[0]*data.shape[1]<=65536, 'Shared surface atlas grew beyond its small-texture budget'
    print(f'PASS: {len(meshes)} meshes / {total_triangles} triangles; valid buffers, bounds, normals and UVs.')
    print(f'PASS: {checked_refs} GUID references; {len(lineup)} distinct prefabs, four grounded wheels each; camera and focus references.')
    print(f'PASS: 24 retained GUIDs resolve uniquely to their recorded destination assets; {retained} unstaged moves accounted for.')
    print(f'PASS: closed outward tire shells; {4*len(lineup)} serialized wheels opaque from both sides (6144 sample rays).')
    print('PASS: fitted headlamp anchors, separate emissive lenses and shared clock/headlight wiring; low compact bonnet lip.')
    print('PASS: each sedan with interior within 7000 triangles / 7 renderers / 3 shared materials; transparent glass and original Synty lamp material/UVs.')
    print('PASS: original Synty prefab in adjacent bay; variant root and sixteen focus targets; no scale/material overrides.')
    print('Not verified: Unity asset import, materials/lighting in URP, Play controls or manual visual acceptance.')


if __name__ == '__main__':
    validate()
