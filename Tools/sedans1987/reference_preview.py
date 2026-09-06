"""Read the installed Synty sedan for an offline shape comparison only.

This small reader supports this static FBX's layout, not arbitrary FBX imports.
It uses source normals/albedo and opaque glass; Unity's shader graphs, imported
normals, transparency and reflections must be judged in the actual showroom.
It never writes or converts the original pack assets.
"""
import math
import struct
import zlib
import numpy as np
from PIL import Image
import unity_assets as ua

PACK = 'Assets/Synty/PolygonPalmCity'
MODEL = PACK+'/Models/SM_Veh_Sedan_01.fbx'
MATERIALS = [PACK+'/Materials/Alts/PolygonPalmCity_03_A.mat',
             PACK+'/Materials/Buildings/Glass_01.mat']
TEXTURES = [PACK+'/Textures/Alts/PolygonPalmCity_03_A.png',
            PACK+'/Textures/Alts/PolygonPalmCity_01_A.png']


def read_fbx(path):
    raw=path.read_bytes()
    assert raw.startswith(b'Kaydara FBX Binary'), path
    version=struct.unpack_from('<I',raw,23)[0]
    header='<QQQB' if version>=7500 else '<IIIB'
    header_size=struct.calcsize(header)

    def prop(pos):
        tag=chr(raw[pos]); pos+=1
        scalars={'Y':'h','C':'?','I':'i','F':'f','D':'d','L':'q'}
        if tag in scalars:
            fmt='<'+scalars[tag]
            return struct.unpack_from(fmt,raw,pos)[0],pos+struct.calcsize(fmt)
        if tag in 'SR':
            size=struct.unpack_from('<I',raw,pos)[0]; pos+=4
            return raw[pos:pos+size],pos+size
        count,encoding,size=struct.unpack_from('<III',raw,pos); pos+=12
        buf=raw[pos:pos+size]
        if encoding: buf=zlib.decompress(buf)
        fmt={'f':'f','d':'d','i':'i','l':'q','b':'?','c':'B'}[tag]
        return struct.unpack('<'+str(count)+fmt,buf),pos+size

    def node(pos):
        end,count,_,nlen=struct.unpack_from(header,raw,pos)
        if not end: return None,pos+header_size
        pos+=header_size
        name=raw[pos:pos+nlen].decode(); pos+=nlen
        props=[]; children=[]
        for _ in range(count):
            value,pos=prop(pos); props.append(value)
        while pos<end:
            child,pos=node(pos)
            if child is None: break
            children.append(child)
        return (name,props,children),end

    roots=[]; pos=27
    while pos<len(raw):
        item,pos=node(pos)
        if item is None: break
        roots.append(item)
    return roots


def child(node,name):
    return next(c for c in node[2] if c[0]==name)


def properties(node):
    return {c[1][0].decode():c[1][4:] for c in child(node,'Properties70')[2]}


def reference_objects():
    from preview import documents
    roots=read_fbx(ua.ROOT/MODEL)
    settings=properties(next(r for r in roots if r[0]=='GlobalSettings'))
    assert [settings[k][0] for k in ('UpAxis','FrontAxis','CoordAxis')]==[1,2,0]
    objects=next(r for r in roots if r[0]=='Objects')[2]
    by_id={o[1][0]:o for o in objects}
    links=[c[1][1:3] for c in next(r for r in roots if r[0]=='Connections')[2] if c[1][0]==b'OO']
    parent={a:b for a,b in links if by_id.get(a,('',''))[0]=='Model'}
    materials=[]
    for path,texture in zip(MATERIALS,TEXTURES):
        mat=documents(ua.ROOT/path)[2100000][1]['Material']['m_SavedProperties']
        tex=next(t['_Albedo_Map']['m_Texture']['guid'] for t in mat['m_TexEnvs'] if '_Albedo_Map' in t)
        assert tex==ua.guid(texture), 'Reference material texture changed'
        tint=next(c['_BaseColor'] for c in mat['m_Colors'] if '_BaseColor' in c)
        pixels=np.asarray(Image.open(ua.ROOT/texture).convert('RGB'))
        materials.append(np.clip(pixels*np.array([tint[k] for k in 'rgb']),0,255).astype('uint8'))

    def matrix(model_id):
        if not model_id: return np.eye(4)
        p=properties(by_id[model_id])
        # The reference uses only identity transforms except the steering wheel's
        # X rotation with a pivot/offset. Reject silently unsupported layouts.
        assert all(np.allclose(p.get(k,[0,0,0]),0) for k in ('PreRotation','PostRotation','GeometricTranslation','GeometricRotation'))
        assert np.allclose(p.get('Lcl Scaling',[1,1,1]),1)
        angles=p.get('Lcl Rotation',[0,0,0]); assert angles[1:]==[0,0]
        a=math.radians(angles[0]); c,s=math.cos(a),math.sin(a)
        rot=np.array([[1,0,0],[0,c,-s],[0,s,c]])
        pivot=np.array(p.get('RotationPivot',[0,0,0]))
        m=np.eye(4); m[:3,:3]=rot
        m[:3,3]=np.array(p.get('Lcl Translation',[0,0,0]))+p.get('RotationOffset',[0,0,0])+pivot-rot@pivot
        return matrix(parent[model_id])@m

    result=[]
    for geo in (o for o in objects if o[0]=='Geometry'):
        model_id=next(b for a,b in links if a==geo[1][0])
        model=by_id[model_id]
        points=np.array(child(geo,'Vertices')[1][0]).reshape(-1,3)
        polygon=np.array(child(geo,'PolygonVertexIndex')[1][0])
        vertex_ids=np.where(polygon<0,-polygon-1,polygon)
        normal_layer=child(geo,'LayerElementNormal'); uv_layer=child(geo,'LayerElementUV')
        assert child(normal_layer,'MappingInformationType')[1][0]==b'ByPolygonVertex'
        assert child(normal_layer,'ReferenceInformationType')[1][0]==b'Direct'
        assert child(uv_layer,'MappingInformationType')[1][0]==b'ByPolygonVertex'
        assert child(uv_layer,'ReferenceInformationType')[1][0]==b'IndexToDirect'
        normals=np.array(child(normal_layer,'Normals')[1][0]).reshape(-1,3)
        uv=np.array(child(uv_layer,'UV')[1][0]).reshape(-1,2)[list(child(uv_layer,'UVIndex')[1][0])]
        points=points[vertex_ids]
        m=matrix(model_id)
        points=(points@m[:3,:3].T+m[:3,3])*settings['UnitScaleFactor'][0]*.01
        normals=normals@m[:3,:3].T
        # FBX right-handed X becomes Unity left-handed X; the car still faces +Z.
        points[:,0]*=-1; normals[:,0]*=-1
        indices=[]; start=0
        for end in np.flatnonzero(polygon<0):
            indices.extend((start,i+1,i) for i in range(start+1,end))
            start=end+1
        assert start==len(points)==len(normals)==len(uv)
        material=child(geo,'LayerElementMaterial')
        assert child(material,'MappingInformationType')[1][0]==b'AllSame'
        index=1 if b'Glass' in model[1][1] else 0
        result.append((points,normals,uv,np.array(indices),materials[index],False))
    assert len(result)==16
    return result
