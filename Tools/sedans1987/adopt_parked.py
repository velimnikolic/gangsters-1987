#!/usr/bin/env python3
"""Retarget passenger-car instances in existing game dressing, without Unity.

Only the two baked lots that contain passenger cars are touched. Root poses and
local file IDs survive; obsolete per-part Synty paint/static overrides do not.
Police/service variants, vans, source packs and the comparison scene are excluded.
Future bakes use CivilianVehicleCatalog through their existing generators.
"""
import argparse
import json
import re
from pathlib import Path
import unity_assets as ua
from preview import Assets
import numpy as np

TARGETS = ('Assets/Prefabs/CoreBlocks/nightclub-block.prefab',
           'Assets/Prefabs/Residential/caryard.prefab')
LEGACY = ('SM_Veh_Sedan_01', 'SM_Veh_Suv_01', 'SM_Veh_Pickup_01', 'SM_Veh_Supercar_01')
PARTS = ('PolygonPalmCity', 'PolygonCity', 'PolygonGangWarfare')


def adopt(check=False):
    legacy = {}
    for pack in PARTS:
        for name in LEGACY:
            path=f'Assets/Synty/{pack}/Prefabs/Vehicles/{name}.prefab'
            if (ua.ROOT/path).exists(): legacy[ua.guid(path)] = path
    cars=json.loads((ua.ROOT/'Tools/sedans1987/lineup.json').read_text())
    authored={ua.guid(f'{ua.ASSET}/Prefabs/{car["id"]}.prefab'):car for car in cars}
    assets=Assets();sizes={}
    for car in cars:
        points=np.concatenate([obj[0] for obj in assets.objects(ua.ROOT/f'{ua.ASSET}/Prefabs/{car["id"]}.prefab')])
        sizes[car['id']]=np.ptp(points,axis=0)
    count=0
    for target in TARGETS:
        path=ua.ROOT/target
        original=path.read_text()
        docs=re.split(r'(?=^--- !u!)',original,flags=re.M)
        instances={}
        for i,doc in enumerate(docs):
            match=re.search(r'm_SourcePrefab: \{fileID: \d+, guid: (\w+)',doc)
            if not match or match[1] not in legacy and match[1] not in authored: continue
            if check:
                assert match[1] not in legacy, f'Legacy passenger car remains in {target}: {legacy[match[1]]}'
                continue
            instance=re.search(r'^--- !u!1001 &(\d+)',doc)[1]
            car=authored.get(match[1],cars[count % len(cars)]);count+=1
            prefab=f'{ua.ASSET}/Prefabs/{car["id"]}.prefab';guid=ua.guid(prefab)
            # Generated prefab roots are native objects. Read their IDs instead of
            # carrying a source-FBX transform ID into the replacement prefab.
            native=(ua.ROOT/prefab).read_text()
            root_tf=re.search(r'^--- !u!4 &(\d+)',native,re.M)[1]
            parent=re.search(r'm_TransformParent: \{fileID: (\d+)\}',doc)[1]
            mods=[]
            for prop,value in re.findall(r'propertyPath: (m_Local(?:Position|Rotation|EulerAnglesHint)\.\w)\n      value: ([^\n]*)',doc):
                mods.append(f'    - target: {{fileID: {root_tf}, guid: {guid}, type: 3}}\n      propertyPath: {prop}\n      value: {value}\n      objectReference: {{fileID: 0}}\n')
            # The venue bakes reserve 2.25 x 5.05 m. Never inherit a Synty-specific
            # scale (one old supercar was .68); fit uniformly to the same reservation.
            size=sizes[car['id']]
            scale=min(1,5.05/size[2],2.25/size[0])
            for axis in 'xyz':
                mods.append(f'    - target: {{fileID: {root_tf}, guid: {guid}, type: 3}}\n      propertyPath: m_LocalScale.{axis}\n      value: {scale:.8g}\n      objectReference: {{fileID: 0}}\n')
            for field in ('m_RemovedComponents','m_RemovedGameObjects','m_AddedGameObjects','m_AddedComponents'):
                assert re.search(rf'{field}: \[\]',doc), (target,instance,field)
            docs[i]=f'''--- !u!1001 &{instance}
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    serializedVersion: 3
    m_TransformParent: {{fileID: {parent}}}
    m_Modifications:
{''.join(mods)}    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {{fileID: 100100000, guid: {guid}, type: 3}}
'''
            instances[instance]=(root_tf,guid)
        for i,doc in enumerate(docs):
            match=re.search(r'm_PrefabInstance: \{fileID: (\d+)\}',doc)
            if not match or match[1] not in instances: continue
            assert re.match(r'--- !u!4 &\d+ stripped',doc), (target,'Unexpected referenced part',doc[:100])
            root_tf,guid=instances[match[1]]
            docs[i]=re.sub(r'm_CorrespondingSourceObject: \{[^}]*\}',
                f'm_CorrespondingSourceObject: {{fileID: {root_tf}, guid: {guid}, type: 3}}',doc)
        changed=''.join(docs)
        if changed != original:
            backup=Path('/tmp/sedans1987-parked-before')/target
            backup.parent.mkdir(parents=True,exist_ok=True)
            if not backup.exists():backup.write_text(original)
            path.write_text(changed)
    print(f'PASS: {"no legacy passenger instances" if check else str(count)+" parked cars adopted"}; taxis and service assets excluded.')


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--check',action='store_true')
    adopt(parser.parse_args().check)
