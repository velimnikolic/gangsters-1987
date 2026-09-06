"""Rounded apertures with a stamped outer lip, rubber gasket and recessed reveal."""
import math
from geometry import unit,sub,cross


def aperture(body,glass,point,limits,width,outward,paint,uv,corner=.065,across=()):
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
    outline=sampled
    normal=unit(outward)
    def place(coord,expand=0,depth=0):
        u,t=coord
        u=cu+(u-cu)*(1+2*expand/max(.01,across))
        t=ct+(t-ct)*(1+2*expand/max(.01,rise))
        return tuple(x+depth*n for x,n in zip(point(u,t),normal))
    center=point(cu,ct)
    def pane_normal(u,t):
        # Side panes are flat glass. Windscreens share a smoothly varying normal
        # across their crown; no triangle receives its own reflection plane.
        if abs(outward[0])>.5:u,t=cu,ct
        e=.045
        n=unit(cross(sub(point(u+e,t),point(u-e,t)),sub(point(u,t+e),point(u,t-e))))
        return tuple(-v for v in n) if sum(v*w for v,w in zip(n,outward))<0 else n
    # Alternate ears of the convex opening into a strip. There is no central
    # vertex or four-way diagonal convergence in the visible glass.
    indices=list(range(len(outline)));alternate=False
    while len(indices)>2:
        tri=[indices[0],indices[1],indices[-1]] if not alternate else [indices[0],indices[-2],indices[-1]]
        coords=[outline[i] for i in tri]
        pts=[point(*p) for p in coords]
        if sum(v*v for v in cross(sub(pts[1],pts[0]),sub(pts[2],pts[0])))>1e-16:
            glass.face(pts,'window_glass',outward,normals=[pane_normal(*p) for p in coords],uvs=[uv(p[1]) for p in coords])
        del indices[0 if not alternate else -1]
        alternate=not alternate
    for i,a in enumerate(outline):
        b=outline[(i+1)%len(outline)]
        # Small slope changes catch light at the edge of the stamped window frame.
        body.face([place(a,width,.002),place(b,width,.002),
                   place(b,width*.60,.006),place(a,width*.60,.006)],paint,outward)
        body.face([place(a,width*.60,.006),place(b,width*.60,.006),
                   place(b,0,.002),place(a,0,.002)],'rubber',outward)
        body.face([place(a),place(b),place(b,0,-.034),place(a,0,-.034)],
                   'interior_trim',sub(center,point(*a)))
    # Fill only the material outside each curved corner. Glass has a true opening.
    for corner_uv,arc in corners:
        for a,b in zip(arc,arc[1:]):
            pts=[place(corner_uv,depth=-.001),place(a,depth=-.001),place(b,depth=-.001)]
            body.face(pts,paint,outward)
            body.face(list(reversed(pts)),'interior_trim')
