"""Regress the visible sidewall holes with topology and front/back ray coverage."""
from collections import Counter
import math
import numpy as np
from geometry import Mesh
from wheels import tire


def closed_tire(radius):
    mesh=Mesh('TireClosureCheck')
    tire(mesh,radius)
    edges=Counter()
    volume=0
    for points,_ in mesh.triangles():
        vertices=[tuple(round(float(x),6) for x in p) for p in points]
        for a,b in zip(vertices,vertices[1:]+vertices[:1]):
            edges[tuple(sorted((a,b)))]+=1
        volume+=np.dot(points[0],np.cross(points[1],points[2]))/6
    assert all(count==2 for count in edges.values()), 'Open or nonmanifold tire shell'
    assert volume>0, 'Tire faces point inward'


def sidewall_coverage(vertices,indices,radius):
    triangles=vertices[indices,:3]
    normals=np.cross(triangles[:,1]-triangles[:,0],triangles[:,2]-triangles[:,0])
    # Cast from each side, using geometric winding just as backface culling does.
    for side in (-1,1):
        faces=triangles[normals[:,0]*side>1e-10]
        a,b,c=faces[:,0,1:],faces[:,1,1:],faces[:,2,1:]
        determinant=(b[:,1]-c[:,1])*(a[:,0]-c[:,0])+(c[:,0]-b[:,0])*(a[:,1]-c[:,1])
        visible=np.abs(determinant)>1e-8
        a,b,c,determinant=a[visible],b[visible],c[visible],determinant[visible]
        for distance in (0,.45,.85,.96):
            for angle in range(0,360,15):
                y=radius*distance*math.cos(math.radians(angle))
                z=radius*distance*math.sin(math.radians(angle))
                u=((b[:,1]-c[:,1])*(y-c[:,0])+(c[:,0]-b[:,0])*(z-c[:,1]))/determinant
                v=((c[:,1]-a[:,1])*(y-c[:,0])+(a[:,0]-c[:,0])*(z-c[:,1]))/determinant
                assert ((u>=-1e-6)&(v>=-1e-6)&(u+v<=1.000001)).any(), (
                    'See-through wheel sidewall',side,distance,angle)
