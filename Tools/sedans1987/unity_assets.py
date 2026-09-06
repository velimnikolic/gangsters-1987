"""Deterministic Unity text assets, authored offline without touching an Editor."""
import hashlib
import math
from pathlib import Path
import re
import struct
import uuid
from geometry import unit, cross, sub

ROOT=Path(__file__).resolve().parents[2]
ASSET='Assets/Sedan1987'
HEADER='%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
COMMON='  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n'
WRITTEN={}


def guid(path):
    meta=ROOT/(str(path)+'.meta')
    if meta.exists():
        return re.search(r'^guid: ([0-9a-f]{32})',meta.read_text(),re.M)[1]
    return uuid.uuid5(uuid.NAMESPACE_URL,'gangsters/miami1987/'+str(path)).hex


def ref(path,file_id,kind=2):
    return f'{{fileID: {file_id}, guid: {guid(path)}, type: {kind}}}'


def v3(v):
    return '{'+', '.join(f'{k}: {x:.7g}' for k,x in zip('xyz',v))+'}'


def quat(pitch=0,yaw=0):
    p,y=math.radians(pitch)/2,math.radians(yaw)/2
    return '{'+', '.join(f'{k}: {x:.8g}' for k,x in zip('xyzw',
           [math.sin(p)*math.cos(y),math.cos(p)*math.sin(y),-math.sin(p)*math.sin(y),math.cos(p)*math.cos(y)]))+'}'


def write(path,data):
    path=str(path)
    dest=ROOT/path
    dest.parent.mkdir(parents=True,exist_ok=True)
    data=data.encode() if isinstance(data,str) else data
    WRITTEN[path]=hashlib.sha256(data).hexdigest()
    if not dest.exists() or dest.read_bytes()!=data:
        dest.write_bytes(data)


def meta(path,importer='NativeFormatImporter',extra='  mainObjectFileID: 4300000\n'):
    write(str(path)+'.meta',f'fileFormatVersion: 2\nguid: {guid(path)}\n{importer}:\n  externalObjects: {{}}\n'+extra+'  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
    parent=Path(path).parent
    while str(parent).startswith(ASSET) or str(parent)==ASSET:
        target=str(parent)+'.meta'
        if not (ROOT/target).exists():
            write(target,f'fileFormatVersion: 2\nguid: {guid(parent)}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
        else:
            write(target,(ROOT/target).read_bytes())
        parent=parent.parent


def mesh_asset(mesh,palette,full_uv=False):
    path=f'{ASSET}/Meshes/{mesh.name}.asset'
    vertices,indices=[],[]
    welded={}
    for face_index,(points,color) in enumerate(mesh.faces):
        normal=unit(cross(sub(points[1],points[0]),sub(points[2],points[0])))
        tangent=unit(sub(points[1],points[0]))
        face_indices=[]
        uv=((palette.index(color)+.5)/len(palette),.5)
        for i,p in enumerate(points):
            # Front-facing +Z signs: viewer's right is world -X.
            tex=[(1,0),(0,0),(0,1),(1,1)][i] if full_uv else uv
            if face_index in mesh.uvs:
                tex=mesh.uvs[face_index][i]
            vertex_normal=mesh.normals.get(face_index,[normal]*len(points))[i]
            # No normal maps: a stable orthogonal basis lets coincident smooth
            # corners share the exact same packed vertex instead of per-face tangents.
            axis=(0,1,0) if abs(vertex_normal[1])<.95 else (1,0,0)
            vertex_tangent=unit(cross(axis,vertex_normal))
            vertex=(*p,*vertex_normal,*vertex_tangent,1,*tex)
            key=struct.pack('<12f',*vertex)
            if key not in welded:
                welded[key]=len(vertices)
                vertices.append(vertex)
            face_indices.append(welded[key])
        for i in range(1,len(points)-1):
            indices.extend((face_indices[0],face_indices[i],face_indices[i+1]))
    assert len(vertices)<65536
    lo,hi=mesh.bounds
    bounds='      m_Center: '+v3([(a+b)/2 for a,b in zip(lo,hi)])+'\n      m_Extent: '+v3([(b-a)/2 for a,b in zip(lo,hi)])+'\n'
    channels=''
    for offset,dimension in [(0,3),(12,3),(24,4),(0,0),(40,2)]+[(0,0)]*9:
        channels+=f'    - stream: 0\n      offset: {offset}\n      format: 0\n      dimension: {dimension}\n'
    compressed=''
    for name in ('Vertices','UV','Normals','Tangents','Weights','NormalSigns','TangentSigns','FloatColors','BoneIndices','Triangles'):
        compressed+=f'    m_{name}:\n      m_NumItems: 0\n'
        if name in ('Vertices','UV','Normals','Tangents','FloatColors'):
            compressed+='      m_Range: 0\n      m_Start: 0\n'
        compressed+='      m_Data: \n      m_BitSize: 0\n'
    text=HEADER+'--- !u!43 &4300000\nMesh:\n'+COMMON+f'''  m_Name: {mesh.name}
  serializedVersion: 12
  m_SubMeshes:
  - serializedVersion: 2
    firstByte: 0
    indexCount: {len(indices)}
    topology: 0
    baseVertex: 0
    firstVertex: 0
    vertexCount: {len(vertices)}
    localAABB:
{bounds}  m_Shapes:
    vertices: []
    shapes: []
    channels: []
    fullWeights: []
  m_BindPose: []
  m_BoneNameHashes:
  m_RootBoneNameHash: 0
  m_BonesAABB: []
  m_VariableBoneCountWeights:
    m_Data:
  m_MeshCompression: 0
  m_IsReadable: 1
  m_KeepVertices: 0
  m_KeepIndices: 0
  m_IndexFormat: 0
  m_IndexBuffer: {struct.pack('<'+'H'*len(indices),*indices).hex()}
  m_VertexData:
    serializedVersion: 3
    m_VertexCount: {len(vertices)}
    m_Channels:
{channels}    m_DataSize: {len(vertices)*48}
    _typelessdata: {b''.join(struct.pack('<12f',*v) for v in vertices).hex()}
  m_CompressedMesh:
{compressed}    m_UVInfo: 0
  m_LocalAABB:
{bounds.replace('      ','    ')}  m_MeshUsageFlags: 0
  m_CookingOptions: 30
  m_BakedConvexCollisionMesh:
  m_BakedTriangleCollisionMesh:
  'm_MeshMetrics[0]': 1
  'm_MeshMetrics[1]': 1
  m_MeshOptimizationFlags: 1
  m_StreamData:
    serializedVersion: 2
    offset: 0
    size: 0
    path:
'''
    write(path,text)
    meta(path)
    return path


def texture_meta(path,srgb=True):
    meta(path,'TextureImporter',f'''  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: {int(srgb)}
  isReadable: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  textureCompression: 0
  compressionQuality: 100
  alphaIsTransparency: 0
  textureType: 0
  textureShape: 1
''')


def material(name,texture,unlit=False,emission=None,surface=None):
    path=f'{ASSET}/Materials/{name}.mat'
    # URP Lit and Unlit package assets; no custom shader or runtime Shader.Find.
    shader='650dd9526735d5b46b79224bc6e94025' if unlit else '933532a4fcc9baf4fa0491de14d08ed7'
    # Preserve the version subasset's local ID if Unity has already saved this file.
    # Our package's MaterialPostprocessor otherwise adds this metadata on import,
    # invalidating a byte-level freshness check despite unchanged material settings.
    previous=(ROOT/path).read_text() if (ROOT/path).exists() else ''
    version_id=re.search(r'^--- !u!114 &(\d+)',previous,re.M)
    version_id=version_id[1] if version_id else '11400000'
    text=HEADER+'--- !u!21 &2100000\nMaterial:\n  serializedVersion: 8\n'+COMMON+f'''  m_Name: {name}
  m_Shader: {{fileID: 4800000, guid: {shader}, type: 3}}
  m_Parent: {{fileID: 0}}
  m_ModifiedSerializedProperties: 0
  m_ValidKeywords: [{', '.join(k for k,enabled in [('_EMISSION',emission),('_METALLICSPECGLOSSMAP',surface)] if enabled)}]
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 1
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap:
    RenderType: Opaque
  disabledShaderPasses: []
  m_LockedProperties: {''}
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _BaseMap:
        m_Texture: {ref(texture,2800000,3)}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    m_Ints: []
    m_Floats:
    - _AlphaClip: 0
    - _Blend: 0
    - _BumpScale: 1
    - _Cull: 2
    - _Cutoff: 0.5
    - _DstBlend: 0
    - _EnvironmentReflections: 1
    - _Metallic: 0.08
    - _OcclusionStrength: 1
    - _ReceiveShadows: 1
    - _Smoothness: {0.85 if surface else 0.65 if emission else 0.26}
    - _SmoothnessTextureChannel: 0
    - _SpecularHighlights: 1
    - _SrcBlend: 1
    - _Surface: 0
    - _WorkflowMode: 1
    - _ZWrite: 1
    m_Colors:
    - _BaseColor: {{r: 1, g: 1, b: 1, a: 1}}
    - _EmissionColor: {{r: {0.0001 if emission else 0}, g: {0.0001 if emission else 0}, b: {0.0001 if emission else 0}, a: {1 if emission else 0}}}
  m_BuildTextureStacks: []
  m_AllowLocking: 1
--- !u!114 &{version_id}
MonoBehaviour:
  m_ObjectHideFlags: 11
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: d0353a89b1f911e48b9e16bdc9f2e058, type: 3}}
  m_Name: {''}
  m_EditorClassIdentifier: Unity.RenderPipelines.Universal.Editor::UnityEditor.Rendering.Universal.AssetVersion
  version: 10
'''
    if emission:
        # Keyword is baked so the Player keeps the emissive variant. The runtime
        # varies only _EmissionColor through a renderer property block.
        text=text.replace('    m_Ints: []',f'''    - _EmissionMap:
        m_Texture: {ref(emission,2800000,3)}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    m_Ints: []''')
    if surface:
        text=text.replace('    m_Ints: []',f'''    - _MetallicGlossMap:
        m_Texture: {ref(surface,2800000,3)}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    m_Ints: []''')
    write(path,text)
    meta(path,extra='  mainObjectFileID: 2100000\n')
    return path


class Hierarchy:
    def __init__(self):
        self.next_id=1000
        self.nodes=[]
        self.extra=[]
        self.instances=[]

    def alloc(self):
        self.next_id+=1
        return self.next_id

    def node(self,name,parent=0,position=(0,0,0),yaw=0,pitch=0,scale=(1,1,1),tag='Untagged'):
        n=dict(go=self.alloc(),tf=self.alloc(),name=name,parent=parent,position=position,
               yaw=yaw,pitch=pitch,scale=scale,tag=tag,components=[])
        self.nodes.append(n)
        return n

    def component(self,n,kind,name,body):
        cid=self.alloc()
        n['components'].append(cid)
        self.extra.append(f'--- !u!{kind} &{cid}\n{name}:\n'+COMMON+f'  m_GameObject: {{fileID: {n["go"]}}}\n'+body)
        return cid

    def renderer(self,n,mesh,mat):
        self.component(n,33,'MeshFilter',f'  m_Mesh: {ref(mesh,4300000)}\n')
        return self.component(n,23,'MeshRenderer',f'''  m_Enabled: 1
  m_CastShadows: 1
  m_ReceiveShadows: 1
  m_DynamicOccludee: 1
  m_MotionVectors: 1
  m_LightProbeUsage: 1
  m_ReflectionProbeUsage: 1
  m_RenderingLayerMask: 1
  m_Materials:
  - {ref(mat,2100000)}
  m_StaticBatchInfo:
    firstSubMesh: 0
    subMeshCount: 0
  m_StaticBatchRoot: {{fileID: 0}}
  m_ProbeAnchor: {{fileID: 0}}
  m_LightProbeVolumeOverride: {{fileID: 0}}
  m_ScaleInLightmap: 1
  m_ReceiveGI: 1
  m_LightmapParameters: {{fileID: 0}}
  m_SortingLayerID: 0
  m_SortingLayer: 0
  m_SortingOrder: 0
  m_AdditionalVertexStreams: {{fileID: 0}}
''')

    def mono(self,n,path,body,enabled=True):
        return self.component(n,114,'MonoBehaviour',f'  m_Enabled: {int(enabled)}\n  m_EditorHideFlags: 0\n'+
                f'  m_Script: {ref(path,11500000,3)}\n  m_Name: \n  m_EditorClassIdentifier: \n'+body)

    def prefab_instance(self,path,position,yaw,root_file_id=1002):
        cid,tid=self.alloc(),self.alloc()
        q=quat(yaw=yaw)
        props={'m_LocalPosition.'+axis:value for axis,value in zip('xyz',position)}
        props.update({'m_LocalRotation.'+axis:float(value) for axis,value in re.findall(r'(\w): ([-.\d]+)',q)})
        mods=''
        for key,value in props.items():
            mods+=f'    - target: {ref(path,root_file_id,3)}\n      propertyPath: {key}\n      value: {value}\n      objectReference: {{fileID: 0}}\n'
        self.instances.append((tid,f'''--- !u!1001 &{cid}
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    serializedVersion: 3
    m_TransformParent: {{fileID: 0}}
    m_Modifications:
{mods}    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {ref(path,100100000,3)}
--- !u!4 &{tid} stripped
Transform:
  m_CorrespondingSourceObject: {ref(path,root_file_id,3)}
  m_PrefabInstance: {{fileID: {cid}}}
  m_PrefabAsset: {{fileID: 0}}
'''))
        return tid

    def text(self,scene=False):
        text=''
        for n in self.nodes:
            components=[n['tf']]+n['components']
            text+=f'--- !u!1 &{n["go"]}\nGameObject:\n'+COMMON+'  serializedVersion: 6\n  m_Component:\n'
            text+=''.join(f'  - component: {{fileID: {c}}}\n' for c in components)
            text+=f'''  m_Layer: 0
  m_Name: {n['name']}
  m_TagString: {n['tag']}
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &{n['tf']}
Transform:
'''+COMMON+f'''  m_GameObject: {{fileID: {n['go']}}}
  serializedVersion: 2
  m_LocalRotation: {quat(n['pitch'],n['yaw'])}
  m_LocalPosition: {v3(n['position'])}
  m_LocalScale: {v3(n['scale'])}
  m_ConstrainProportionsScale: 0
'''
            children=[x['tf'] for x in self.nodes if x['parent']==n['tf']]
            text+='  m_Children:'+ ('\n'+''.join(f'  - {{fileID: {c}}}\n' for c in children) if children else ' []\n')
            text+=f'  m_Father: {{fileID: {n["parent"]}}}\n  m_LocalEulerAnglesHint: {v3((n["pitch"],n["yaw"],0))}\n'
        text+=''.join(self.extra)+''.join(t for _,t in self.instances)
        if scene:
            text+='--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_ObjectHideFlags: 0\n  m_Roots:\n'
            text+=''.join(f'  - {{fileID: {n["tf"]}}}\n' for n in self.nodes if n['parent']==0)
            text+=''.join(f'  - {{fileID: {t}}}\n' for t,_ in self.instances)
        return text
