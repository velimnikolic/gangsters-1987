"""Shallow sealed-beam glass seated inside a gasket and thin metal retaining ring."""
import math
from bodywork import samples
from synty_lamps import LAMP_UV


def round_lamp(mesh, form, x, y, radius):
    # Annular rings leave a real opening for the glass instead of covering it
    # with a solid chrome cylinder. Total projection is under four centimetres.
    for inner,outer,depth,color in [(radius+.004,radius+.013,.026,'rubber'),
                                     (radius,radius+.005,.033,'chrome')]:
        for a,b in zip(samples(0,math.tau,16),samples(0,math.tau,16)[1:]):
            pts=[(x+r*math.cos(t),y+r*math.sin(t),form.end_z(x+r*math.cos(t),1)+depth)
                 for r,t in [(inner,a),(outer,a),(outer,b),(inner,b)]]
            mesh.face(pts,color,(0,0,1))
    # Shallow domed glass follows the nose's sweep and stays seated in the ring.
    def glass(u,t):
        px=radius*u
        half=radius*math.sqrt(max(.00001,1-u*u))
        py=half*t
        dome=.005*(1-(px*px+py*py)/(radius*radius))
        return (x+px,y+py,form.end_z(x+px,1)+.026+dome)
    mesh.surface(glass,samples(-.999,.999,12),samples(-1,1,4),
                 'lamp_front',(0,0,1))
    # Submillimetre rim slivers close the two ends of the sampled glass.
    for side in (-1,1):
        a=glass(side*.999,-1); b=glass(side*.999,1)
        mesh.face([a,(x+side*radius,y,form.end_z(x+side*radius,1)+.026),b],'lamp_front',(0,0,1))


def separate_lamps(body):
    from geometry import Mesh
    lamps=Mesh(body.name.removesuffix('_Body')+'_Lamps')
    retained=[]; normals={};uvs={}
    for i,(points,color) in enumerate(body.faces):
        if color.startswith('lamp_'):
            lamps.face(points,color,normals=body.normals.get(i),uvs=[LAMP_UV[color]]*len(points))
        else:
            if i in body.normals:
                normals[len(retained)]=body.normals[i]
            if i in body.uvs:
                uvs[len(retained)]=body.uvs[i]
            retained.append((points,color))
    body.faces,body.normals,body.uvs=retained,normals,uvs
    return lamps
