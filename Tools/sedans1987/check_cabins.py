"""Regression checks on serialized roof seams and shared glass normals."""
from collections import Counter,defaultdict
import numpy as np
from palette import COLORS
import unity_assets as ua


def check_cabin(assets,car):
    vertices,indices=assets.mesh(ua.guid(f'{ua.ASSET}/Meshes/{car["id"]}_Body.asset'))
    paint={car['paint'],car['roof']} if car['style']!='bastion' else {'rubber'}
    swatches=np.floor(vertices[indices[:,0],10]*len(COLORS)).astype(int)
    faces=vertices[indices[np.isin(swatches,[list(COLORS).index(c) for c in paint])],:3]
    edges=Counter();joins=set()
    for face in faces:
        for a,b in zip(face,np.roll(face,-1,axis=0)):
            key=tuple(sorted((tuple(np.round(a,5)),tuple(np.round(b,5)))))
            edges[key]+=1
            if (min(a[1],b[1])>car['height']-.11 and abs(a[0]-b[0])>.005 and
                any(abs(a[2]-z)<1e-5 and abs(b[2]-z)<1e-5 for z in (car['cabin'][1],car['cabin'][2]))):
                joins.add(key)
    assert len(joins)>=12,('No roof/screen boundary found',car['id'])
    assert all(edges[k]>=2 for k in joins),('Open roof/screen seam',car['id'])
    glass,_=assets.mesh(ua.guid(f'{ua.ASSET}/Meshes/{car["id"]}_Glass.asset'))
    normals=defaultdict(list)
    for vertex in glass:normals[tuple(vertex[:3])].append(vertex[3:6])
    for position,group in normals.items():
        assert np.max(np.linalg.norm(np.array(group)-group[0],axis=1))<1e-5,('Split glass reflection normal',car['id'],position)
