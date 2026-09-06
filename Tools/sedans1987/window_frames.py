"""Rounded apertures with a stamped outer lip, rubber gasket and recessed reveal."""
import math
from geometry import unit,sub,cross


def aperture(body,glass,point,limits,width,outward,paint,uv,corner=.065,across=(),along=(),glass_color='window_glass'):
    left,right,bottom,top=limits;cu=(left+right)/2;ct=(bottom+top)/2
    distance=lambda a,b:math.sqrt(sum((x-y)**2 for x,y in zip(a,b)))
    cross_stations=across
    across=distance(point(left,ct),point(right,ct))
    rise=distance(point(cu,bottom),point(cu,top))
    radii=[corner]*4 if isinstance(corner,(int,float)) else corner
    outline=[];corners=[]
    for (sign_u,sign_t,angle),radius in zip([(1,1,0),(-1,1,90),(-1,-1,180),(1,-1,270)],radii):
        ru=min((right-left)*.23,(right-left)*max(.002,radius)/max(.01,across))
        rt=min((top-bottom)*.26,(top-bottom)*max(.002,radius)/max(.01,rise))
        corner_uv=(right if sign_u>0 else left,top if sign_t>0 else bottom)
        u=corner_uv[0]-sign_u*ru;t=corner_uv[1]-sign_t*rt
        steps=1 if radius<.02 else 2
        arc=[(u+ru*math.cos(math.radians(angle+i*90/steps)),t+rt*math.sin(math.radians(angle+i*90/steps))) for i in range(steps+1)]
        outline.extend(arc);corners.append((corner_uv,arc))
    # A curved windshield needs the same transverse breaks as its metal rails.
    # Joining only its corner arcs leaves an open wedge under a crowned roof.
    sampled=[]
    for i,a in enumerate(outline):
        b=outline[(i+1)%len(outline)];sampled.append(a)
        if abs(a[1]-b[1])<1e-8 and abs(a[0]-b[0])>.01:
            stations=sorted((u for u in cross_stations if min(a[0],b[0])+1e-8<u<max(a[0],b[0])-1e-8),reverse=a[0]>b[0])
            sampled.extend((u,a[1]) for u in stations)
        if abs(a[0]-b[0])<1e-8 and abs(a[1]-b[1])>.01:
            stations=sorted((t for t in along if min(a[1],b[1])+1e-8<t<max(a[1],b[1])-1e-8),reverse=a[1]>b[1])
            sampled.extend((a[0],t) for t in stations)
    outline=sampled
    normal=unit(outward)
    def inset(coord,amount):
        u,t=coord
        return cu+(u-cu)*(1-2*amount/max(.01,across)),ct+(t-ct)*(1-2*amount/max(.01,rise))
    def place(coord,amount=0,depth=0):
        return tuple(x+depth*n for x,n in zip(point(*inset(coord,amount)),normal))
    center=point(cu,ct)
    def pane_normal(u,t):
        # Side panes are flat glass. Windscreens share a smoothly varying normal
        # across their crown; no triangle receives its own reflection plane.
        if abs(outward[0])>.5:u,t=cu,ct
        e=.045
        n=unit(cross(sub(point(u+e,t),point(u-e,t)),sub(point(u,t+e),point(u,t-e))))
        return tuple(-v for v in n) if sum(v*w for v,w in zip(n,outward))<0 else n
    # Triangulate in aperture coordinates, where the opening is convex. A 3D
    # area check admits folded slivers made from three points on one curved edge.
    # Keep every boundary station, but only clip ears with a real 2D area.
    indices=list(range(len(outline)))
    while len(indices)>2:
        def area(k):
            a,b,c=[outline[indices[j%len(indices)]] for j in (k-1,k,k+1)]
            return abs((b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]))
        ear=max(range(len(indices)),key=area)
        if area(ear)<1e-12:break
        tri=[indices[j%len(indices)] for j in (ear-1,ear,ear+1)]
        coords=[inset(outline[i],width) for i in tri]
        pts=[point(*p) for p in coords]
        glass.face(pts,glass_color,outward,normals=[pane_normal(*p) for p in coords],uvs=[uv(p[1]) for p in coords])
        del indices[ear]
    for i,a in enumerate(outline):
        b=outline[(i+1)%len(outline)]
        # Keep the metal lip at six millimetres. Only the black seal grows,
        # inward into the opening, never across the roof or neighbouring paint.
        body.face([place(a,0,0),place(b,0,0),
                   place(b,.006,.006),place(a,.006,.006)],paint,outward)
        body.face([place(a,.006,.006),place(b,.006,.006),
                   place(b,width,.002),place(a,width,.002)],'rubber',outward)
        body.face([place(a,width),place(b,width),place(b,width,-.034),place(a,width,-.034)],
                   'interior_trim',sub(center,point(*a)))
    # Fill only the material outside each curved corner. Glass has a true opening.
    for corner_uv,arc in corners:
        boundary=[corner_uv]+arc;filled=[]
        for a,b in zip(boundary,boundary[1:]+boundary[:1]):
            filled.append(a)
            if abs(a[1]-b[1])<1e-8:
                filled.extend((u,a[1]) for u in sorted((u for u in cross_stations if min(a[0],b[0])+1e-8<u<max(a[0],b[0])-1e-8),reverse=a[0]>b[0]))
            if abs(a[0]-b[0])<1e-8:
                filled.extend((a[0],t) for t in sorted((t for t in along if min(a[1],b[1])+1e-8<t<max(a[1],b[1])-1e-8),reverse=a[1]>b[1]))
        # Anchor on the curved edge so added boundary samples do not form
        # zero-area ears. This closes the tiny wedges at windscreen corners.
        pivot=filled.index(arc[len(arc)//2]);filled=filled[pivot:]+filled[:pivot]
        for i in range(1,len(filled)-1):
            pts=[place(filled[j]) for j in (0,i,i+1)]
            if sum(v*v for v in cross(sub(pts[1],pts[0]),sub(pts[2],pts[0])))<1e-16:continue
            body.face(pts,paint,outward)
            body.face(list(reversed(pts)),'interior_trim')

    return outline+[uv for uv,_ in corners]
