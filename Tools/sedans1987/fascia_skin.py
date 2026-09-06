"""Closed shallow grille surrounds projected onto the body's stamped end cap."""
from bodywork import samples
from geometry import Mesh, cross, sub


def fitted_face(mesh,form,points,color,end=1):
    """Clip edge fittings to the real cap silhouette, including its shoulder."""
    if all(form.cap_z(p[0],end,p[1]) is not None for p in points):
        mesh.face(points,color,(0,0,end))
        return
    # Interior fittings retain their small mesh; only parts crossing the body
    # boundary are intersected with its existing triangles.
    lo_x,hi_x=min(p[0] for p in points),max(p[0] for p in points)
    lo_y,hi_y=min(p[1] for p in points),max(p[1] for p in points)
    for a,b,c,box in form.cap_faces[end]:
        if box[0]>hi_x or box[1]<lo_x or box[2]>hi_y or box[3]<lo_y:continue
        direction=1 if cross(sub(b,a),sub(c,a))[2]>0 else -1
        poly=[(p[0],p[1],p[2]-form.skin_z(p[0],end,p[1])) for p in points]
        for first,last in ((a,b),(b,c),(c,a)):
            def signed(p):return direction*((last[0]-first[0])*(p[1]-first[1])-(last[1]-first[1])*(p[0]-first[0]))
            output=[]
            for p,q in zip(poly,poly[1:]+poly[:1]):
                sp,sq=signed(p),signed(q)
                if sp>=0:output.append(p)
                if (sp>=0)!=(sq>=0):
                    t=sp/(sp-sq);output.append(tuple(p[i]+t*(q[i]-p[i]) for i in range(3)))
            poly=output
            if len(poly)<3:break
        if len(poly)<3:continue
        pts=[]
        for x,y,depth in poly:
            p=(x,y,form.skin_z(x,end,y)+depth)
            if not pts or sum((v-w)**2 for v,w in zip(p,pts[-1]))>1e-14:pts.append(p)
        if len(pts)>2 and sum((v-w)**2 for v,w in zip(pts[0],pts[-1]))<1e-14:pts.pop()
        for i in range(1,len(pts)-1):
            tri=[pts[0],pts[i],pts[i+1]]
            if abs(cross(sub(tri[1],tri[0]),sub(tri[2],tri[0]))[2])>1e-8:
                mesh.face(tri,color,(0,0,end))


def surface(mesh,form,point,us,vs,color,end):
    panel=Mesh('Fitted panel')
    panel.surface(point,us,vs,color,(0,0,end),smooth=False)
    for points,shade in panel.faces:fitted_face(mesh,form,points,shade,end)


def surround(mesh,form,x,y,width,height,color='chrome',rim=.024):
    def p(u,v,expand,depth):
        px=x+u*(width/2+expand);py=y+v*(height/2+expand)
        return px,py,form.skin_z(px,1,py)+depth
    edges=[[(u,v) for u in samples(-1,1,8)] for v in (-1,1)]
    edges += [[(u,v) for v in samples(-1,1,4)] for u in (-1,1)]
    for edge in edges:
        for a,b in zip(edge,edge[1:]):
            # The outer return meets the body; the inner lip rises over the
            # recessed black insert. No full chrome plate behind the insert.
            fitted_face(mesh,form,[p(*a,rim,.001),p(*b,rim,.001),p(*b,rim*.70,.014),p(*a,rim*.70,.014)],color)
            fitted_face(mesh,form,[p(*a,rim*.70,.014),p(*b,rim*.70,.014),p(*b,0,.021),p(*a,0,.021)],color)
