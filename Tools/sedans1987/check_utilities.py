"""Serialized cabin openings, service isolation and native utility rig checks."""
import json
import re
import numpy as np
from preview import documents
from palette import COLORS
import unity_assets as ua
import synty_lamps
from check_wheels import sidewall_coverage


def no_paint_behind_glass(assets,car):
    v,idx=assets.mesh(ua.guid(f'{ua.ASSET}/Meshes/{car["id"]}_Body.asset'))
    paint={car['paint'],car['roof']}
    if car['style']=='bastion':paint={'rubber'}
    swatches=np.floor(v[:,10]*len(COLORS)).astype(int)
    mask=np.isin(swatches[idx[:,0]],[list(COLORS).index(c) for c in paint])
    triangles=v[idx[mask],:3].astype(float)
    a=triangles[:,0];e1=triangles[:,1]-a;e2=triangles[:,2]-a
    gv,gi=assets.mesh(ua.guid(f'{ua.ASSET}/Meshes/{car["id"]}_Glass.asset'))
    for face in gv[gi,:3].astype(float):
        normal=np.cross(face[1]-face[0],face[2]-face[0]);normal/=np.linalg.norm(normal)
        origin=face.mean(axis=0)+normal*.002;direction=-normal
        p=np.cross(np.broadcast_to(direction,e2.shape),e2);det=(e1*p).sum(axis=1)
        valid=np.abs(det)>1e-9
        inverse=np.divide(1,det,out=np.zeros_like(det),where=valid)
        t=origin-a;u=(t*p).sum(axis=1)*inverse;q=np.cross(t,e1)
        w=(q*direction).sum(axis=1)*inverse;distance=(e2*q).sum(axis=1)*inverse
        hits=valid&(u>=0)&(w>=0)&(u+w<=1)&(distance>.003)&(distance<.038)
        assert not hits.any(), ('Opaque paint behind window',car['id'],origin.tolist())


def validate_utilities(assets):
    cars=json.loads((ua.ROOT/'Tools/sedans1987/utilities.json').read_text())
    catalog=(ua.ROOT/'Assets/Scripts/Gameplay/CivilianVehicleCatalog.cs').read_text()
    # Civilian SUVs and the passenger van belong in ambient traffic. The police
    # prisoner van and armoured crew truck remain reserved for their own systems.
    ambient=re.search(r'string\[\] Models\s*=\s*\{(.*?)\}',catalog,re.S)[1]
    for car in cars:
        reserved=car['style'] in ('warden','bastion')
        assert (car['id'] in ambient) != reserved and car['name'] not in ambient
        path=ua.ROOT/f'{ua.ASSET}/Prefabs/{car["id"]}.prefab';docs=documents(path)
        renderers=[d['MeshRenderer'] for t,d in docs.values() if t==23]
        assert len(renderers)==7 and len({m['guid'] for r in renderers for m in r['m_Materials']})==(4 if car['style']=='bastion' else 3)
        parts=assets.objects(path);points=np.concatenate([p[0] for p in parts])
        assert sum(len(p[3]) for p in parts)<=7500
        assert abs(points[:,1].min())<1e-5
        assert points[:,2].max()-points[:,2].min() < car['length']+.60
        seats=next(d['MonoBehaviour'] for t,d in docs.values() if t==114 and d['MonoBehaviour']['m_Script']['guid']==ua.guid('Assets/RoadDemo/VehicleSeatRig.cs'))
        assert abs(seats['ceiling']-(car['height']-.10))<1e-5
        if car['style']=='warden':
            assert len(seats['additionalSeats'])==4
            assert seats['seatYaw']==[0,0,90,-90,90,-90,90,-90]
        for key in ('frontLeft','frontRight','rearLeft','rearRight'):
            assert seats[key]['y']+.43+.30<seats['ceiling']
        for corner in ('FL','FR','RL','RR'):
            v,idx=assets.mesh(ua.guid(f'{ua.ASSET}/Meshes/{car["id"]}_Wheel_{corner}.asset'))
            sidewall_coverage(v,idx,car['radius'])
        lamp=next(d['MonoBehaviour'] for t,d in docs.values() if t==114 and d['MonoBehaviour']['m_Script']['guid']==ua.guid('Assets/RoadDemo/VehicleLampRig.cs'))
        assert docs[lamp['lenses']['fileID']][1]['MeshRenderer']['m_Materials'][0]['guid']==ua.guid(synty_lamps.MATERIAL)
        if car['style']=='bastion':
            assert len(lamp['auxiliaryHeadlights'])==2
            assert all(p['y']>car['height'] for p in lamp['auxiliaryHeadlights'])
        no_paint_behind_glass(assets,car)
    print('PASS: 6 showroom utilities, closed tires, open glazed apertures, cabin ceilings and shared materials; civilian models in ambient pool, police and armoured models reserved.')
    return cars
