#!/usr/bin/env python3
"""Offline software preview of the serialized meshes. This does not render Unity."""
import argparse
import json
import math
from pathlib import Path
import re
import numpy as np
from PIL import Image, ImageDraw, ImageFont
import yaml
from artwork import font_path
from showroom import CAMERA, SCENE
import unity_assets as ua


def documents(path):
    text = Path(path).read_text()
    # Unity treats raw mesh buffers as hex strings even when they contain digits only.
    text = re.sub(r'^(\s+(?:_typelessdata|m_IndexBuffer):) ([0-9a-f]+)$',
                  r'\1 "\2"', text, flags=re.M)
    pattern = r'^--- !u!(\d+) &(\d+)(?: stripped)?\n'
    parts = re.split(pattern, text, flags=re.M)
    return {int(parts[i+1]): (int(parts[i]), yaml.safe_load(parts[i+2]))
            for i in range(1, len(parts), 3)}


def rotation(q):
    x, y, z, w = [q[k] for k in 'xyzw']
    return np.array([[1-2*y*y-2*z*z, 2*x*y-2*z*w, 2*x*z+2*y*w],
                     [2*x*y+2*z*w, 1-2*x*x-2*z*z, 2*y*z-2*x*w],
                     [2*x*z-2*y*w, 2*y*z+2*x*w, 1-2*x*x-2*y*y]])


class Assets:
    def __init__(self):
        paths = [p for p in (ua.ROOT/ua.ASSET).rglob('*') if p.is_file() and p.suffix != '.meta']
        self.paths = {ua.guid(p.relative_to(ua.ROOT)): p for p in paths}
        self.meshes, self.textures = {}, {}

    def mesh(self, guid):
        if guid not in self.meshes:
            mesh = next(iter(documents(self.paths[guid]).values()))[1]['Mesh']
            data = mesh['m_VertexData']
            vertices = np.frombuffer(bytes.fromhex(data['_typelessdata']), '<f4').reshape(-1, 12)
            indices = np.frombuffer(bytes.fromhex(mesh['m_IndexBuffer']), '<u2').reshape(-1, 3)
            self.meshes[guid] = vertices, indices
        return self.meshes[guid]

    def material(self, guid):
        if guid not in self.textures:
            mat = next(iter(documents(self.paths[guid]).values()))[1]['Material']
            tex = mat['m_SavedProperties']['m_TexEnvs'][0]['_BaseMap']['m_Texture']['guid']
            image = np.asarray(Image.open(self.paths[tex]).convert('RGB'))
            unlit = mat['m_Shader']['guid'] == '650dd9526735d5b46b79224bc6e94025'
            self.textures[guid] = image, unlit
        return self.textures[guid]

    def objects(self, path, outer=None):
        outer = np.eye(4) if outer is None else outer
        docs = documents(path)
        transforms = {k: d['Transform'] for k, (t, d) in docs.items() if t == 4 and 'm_GameObject' in d['Transform']}
        by_go = {t['m_GameObject']['fileID']: key for key, t in transforms.items()}
        filters = {d['MeshFilter']['m_GameObject']['fileID']: d['MeshFilter']['m_Mesh']['guid']
                   for t, d in docs.values() if t == 33}
        def matrix(key):
            t = transforms[key]
            m = np.eye(4)
            m[:3, :3] = rotation(t['m_LocalRotation']) @ np.diag([t['m_LocalScale'][k] for k in 'xyz'])
            m[:3, 3] = [t['m_LocalPosition'][k] for k in 'xyz']
            parent = t['m_Father']['fileID']
            return (matrix(parent) if parent else outer) @ m
        objects = []
        for kind, doc in docs.values():
            if kind == 23:
                rend = doc['MeshRenderer']
                go = rend['m_GameObject']['fileID']
                vertex, index = self.mesh(filters[go])
                transform = matrix(by_go[go])
                points = vertex[:, :3] @ transform[:3, :3].T + transform[:3, 3]
                normals = vertex[:, 3:6] @ np.linalg.inv(transform[:3, :3])
                texture, unlit = self.material(rend['m_Materials'][0]['guid'])
                objects.append((points, normals, vertex[:, 10:12], index, texture, unlit))
            elif kind == 1001:
                instance = doc['PrefabInstance']
                props = {m['propertyPath']: float(m['value']) for m in instance['m_Modification']['m_Modifications']}
                transform = np.eye(4)
                transform[:3, :3] = rotation({k: props['m_LocalRotation.'+k] for k in 'xyzw'})
                transform[:3, 3] = [props['m_LocalPosition.'+k] for k in 'xyz']
                objects += self.objects(self.paths[instance['m_SourcePrefab']['guid']], outer @ transform)
        return objects


def render(objects, config, size=(1600, 900)):
    width, height = size
    pitch, yaw = math.radians(config['pitch']), math.radians(config['yaw'])
    forward = np.array([math.sin(yaw)*math.cos(pitch), -math.sin(pitch), math.cos(yaw)*math.cos(pitch)])
    right = np.array([math.cos(yaw), 0, -math.sin(yaw)])
    up = np.cross(forward, right)
    eye = np.array(config['pivot'])-forward*config['distance']
    basis = np.stack((right, up, forward), axis=1)
    focal = height/(2*math.tan(math.radians(config.get('fov', 42))/2))
    canvas = np.empty((height, width, 3), dtype=np.uint8)
    canvas[:] = (165, 190, 188)
    depth = np.full((height, width), np.inf)
    light = np.array([-.38, .80, .46])
    for points, normals, uv, indices, texture, unlit in objects:
        camera = (points-eye) @ basis
        screen = np.stack((width/2+camera[:, 0]*focal/camera[:, 2],
                           height/2-camera[:, 1]*focal/camera[:, 2], camera[:, 2]), axis=1)
        for tri in indices:
            if np.dot(normals[tri[0]], eye-points[tri[0]]) <= 0 or min(screen[tri, 2]) <= .1:
                continue
            p = screen[tri]
            x0, y0 = np.maximum(np.floor(p[:, :2].min(axis=0)).astype(int), (0, 0))
            x1, y1 = np.minimum(np.ceil(p[:, :2].max(axis=0)).astype(int), (width-1, height-1))
            if x0 > x1 or y0 > y1:
                continue
            denominator = (p[1, 1]-p[2, 1])*(p[0, 0]-p[2, 0])+(p[2, 0]-p[1, 0])*(p[0, 1]-p[2, 1])
            if abs(denominator) < 1e-9:
                continue
            yy, xx = np.mgrid[y0:y1+1, x0:x1+1]
            a = ((p[1, 1]-p[2, 1])*(xx+.5-p[2, 0])+(p[2, 0]-p[1, 0])*(yy+.5-p[2, 1]))/denominator
            b = ((p[2, 1]-p[0, 1])*(xx+.5-p[2, 0])+(p[0, 0]-p[2, 0])*(yy+.5-p[2, 1]))/denominator
            c = 1-a-b
            iz = a/p[0, 2]+b/p[1, 2]+c/p[2, 2]
            z = np.divide(1, iz, out=np.full_like(iz, np.inf), where=iz>0)
            local = depth[y0:y1+1, x0:x1+1]
            mask = (a>=-1e-6)&(b>=-1e-6)&(c>=-1e-6)&(z<local)
            if not mask.any():
                continue
            tex = (a[mask, None]*uv[tri[0]]/p[0, 2]+b[mask, None]*uv[tri[1]]/p[1, 2]+c[mask, None]*uv[tri[2]]/p[2, 2])*z[mask, None]
            tx = np.clip((tex[:, 0]*texture.shape[1]).astype(int), 0, texture.shape[1]-1)
            ty = np.clip(((1-tex[:, 1])*texture.shape[0]).astype(int), 0, texture.shape[0]-1)
            shade = 1
            if not unlit:
                ns=(a[mask,None]*normals[tri[0]]/p[0,2]+b[mask,None]*normals[tri[1]]/p[1,2]+c[mask,None]*normals[tri[2]]/p[2,2])*z[mask,None]
                ns/=np.maximum(np.linalg.norm(ns,axis=1,keepdims=True),1e-9)
                shade=(.57+.43*np.maximum(0,ns@light))[:,None]
            canvas[y0:y1+1, x0:x1+1][mask] = np.clip(texture[ty, tx]*shade, 0, 255)
            local[mask] = z[mask]
    return Image.fromarray(canvas)


def main(output):
    output.mkdir(parents=True, exist_ok=True)
    assets = Assets()
    overview = render(assets.objects(ua.ROOT/SCENE), CAMERA)
    overview.save(output/'overview.png')
    lineup = json.loads((ua.ROOT/'Tools/sedans1987/lineup.json').read_text())
    sheet = Image.new('RGB', (1600, len(lineup)*380), '#233e43')
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.truetype(str(font_path()), 24)
    for i, car in enumerate(lineup):
        objects = assets.objects(ua.ROOT/f'{ua.ASSET}/Prefabs/{car["id"]}.prefab')
        for column, yaw in enumerate((145, -35)):
            view = render(objects, dict(pivot=(0, .7, 0), distance=9.5, pitch=22, yaw=yaw), (800, 340))
            sheet.paste(view, (column*800, i*380))
        draw.text((24, i*380+343), f'{i+1:02d} / {car["name"]}', font=font, fill='#eadfc7')
    sheet.save(output/'cars.png')
    print('Offline serialized-mesh previews: '+str(output))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=Path('/tmp/sedans1987-preview'))
    main(parser.parse_args().output)
