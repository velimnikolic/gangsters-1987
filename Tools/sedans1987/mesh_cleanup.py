"""Remove redundant coplanar divisions without changing the visible surface."""
from collections import defaultdict
from geometry import Mesh,sub,cross,unit


def compact(mesh):
    groups=defaultdict(list);result=Mesh(mesh.name)
    for index,(points,color) in enumerate(mesh.faces):
        if index in mesh.normals or index in mesh.uvs:
            result.face(points,color,normals=mesh.normals.get(index),uvs=mesh.uvs.get(index));continue
        n=unit(cross(sub(points[1],points[0]),sub(points[2],points[0])))
        d=sum(a*b for a,b in zip(n,points[0]))
        if any(abs(sum(a*b for a,b in zip(n,p))-d)>1e-7 for p in points[3:]):
            result.face(points,color);continue
        groups[(color,tuple(round(v,6) for v in n),round(d,6))].append(points)
    key=lambda p:tuple(round(v,7) for v in p)
    for (color,n,_),polys in groups.items():
        # Greedy convex unions only. Curved faces, aperture boundaries and normals
        # stay intact; this removes subdivisions on flat floors and cap inserts.
        while True:
            edges={};merge=None
            for i,poly in enumerate(polys):
                for j,(a,b) in enumerate(zip(poly,poly[1:]+poly[:1])):
                    other=edges.get((key(b),key(a)))
                    if other:
                        k,l=other;old=polys[k]
                        other_cycle=old[l:]+old[:l]
                        joined=poly[j+1:]+poly[:j+1]+other_cycle[2:]
                        cleaned=[]
                        for q in range(len(joined)):
                            a,b,c=joined[q-1],joined[q],joined[(q+1)%len(joined)]
                            turn=sum(x*y for x,y in zip(cross(sub(b,a),sub(c,b)),n))
                            if turn < -1e-9:break
                            if turn>1e-10:cleaned.append(b)
                        else:
                            if len(cleaned)>=3:merge=(i,k,cleaned);break
                    edges[(key(a),key(b))]=(i,j)
                if merge:break
            if not merge:break
            i,k,joined=merge
            polys[k]=joined;polys.pop(i)
        for points in polys:result.face(points,color)
    return result
