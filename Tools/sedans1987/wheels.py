"""Closed low-cost tires and readable inset wheel faces, not stacks of cylinders."""
import math
from geometry import Mesh

SEGMENTS=16


def annulus(mesh,side,x,inner,outer,color,segments=SEGMENTS):
    for i in range(segments):
        a,b=i*math.tau/segments,(i+1)*math.tau/segments
        mesh.face([(side*x,r*math.cos(t),r*math.sin(t))
                   for r,t in [(inner,a),(outer,a),(outer,b),(inner,b)]],color,(side,0,0))


def build_wheels(form):
    car=form.car;radius=car['radius'];style=car['style'];wheels=[]
    for side,label in [(-1,'L'),(1,'R')]:
        for axle,where in [(form.front,'F'),(form.rear,'R')]:
            mesh=Mesh(car['id']+'_Wheel_'+where+label)
            tire(mesh,radius)
            if style in ('regent','calder','monarch'):
                annulus(mesh,side,.114,radius*.76,radius*.81,'cream')
            # A dished face stays inside the rubber shoulder; the rim is a thin
            # bright outline and dark spoke wells give the wheel depth at game scale.
            # The rubber shell closes the back; this visible dish needs one face.
            mesh.face([(side*.110,radius*.64*math.cos(i*math.tau/SEGMENTS),
                        radius*.64*math.sin(i*math.tau/SEGMENTS)) for i in range(SEGMENTS)],'wheelshade',(side,0,0))
            annulus(mesh,side,.113,radius*.59,radius*.66,'chrome')
            if style=='regent':
                mesh.cylinder((side*.115,0,0),radius*.54,.008,'chrome',sides=SEGMENTS)
                annulus(mesh,side,.12,radius*.27,radius*.29,'wheelshade')
                mesh.cylinder((side*.122,0,0),radius*.10,.005,'gold',sides=8)
            elif style=='hikari':
                annulus(mesh,side,.115,radius*.37,radius*.54,'wheelshade')
                for i in range(6):
                    a=i*math.tau/6
                    mesh.cylinder((side*.121,radius*.43*math.cos(a),radius*.43*math.sin(a)),
                                  radius*.066,.004,'rubber',sides=6)
                mesh.cylinder((side*.12,0,0),radius*.25,.008,'wheelshade',sides=12)
            else:
                count={'vahren':10,'kronen':8,'albion':10,'calder':16,'monarch':12,'bayside':6}[style]
                broad=.20 if style=='bayside' else .11 if style in ('kronen','monarch') else .055
                for i in range(count):
                    a=i*math.tau/count
                    twists=(-.13,.13) if style in ('vahren','calder') else (.15 if style=='monarch' else 0,)
                    for twist in twists:
                        points=[(side*x,radius*r*math.cos(t),radius*r*math.sin(t))
                                for x,r,t in [(.119,.23,a-broad),(.113,.58,a+twist-broad*.5),
                                              (.113,.58,a+twist+broad*.5),(.119,.23,a+broad)]]
                        mesh.face(points,'chrome',(side,0,0))
                mesh.cylinder((side*.12,0,0),radius*.24,.010,'chrome',sides=12)
                mesh.cylinder((side*.127,0,0),radius*.065,.003,'wheelshade',sides=8)
            wheels.append((mesh,(side*(form.width(axle)-.13),radius,axle)))
    return wheels


def tire(mesh,radius):
    profile=[(-.095,.79),(-.113,.92),(-.078,1),(.078,1),(.113,.92),(.095,.79)]
    normals=[(-1,0),(-.85,.5),(-.25,.97),(.25,.97),(.85,.5),(1,0)]
    for row,((x0,r0),(x1,r1)) in enumerate(zip(profile,profile[1:])):
        for i in range(SEGMENTS):
            a,b=i*math.tau/SEGMENTS,(i+1)*math.tau/SEGMENTS
            points=[(x0,radius*r0*math.cos(a),radius*r0*math.sin(a)),
                    (x1,radius*r1*math.cos(a),radius*r1*math.sin(a)),
                    (x1,radius*r1*math.cos(b),radius*r1*math.sin(b)),
                    (x0,radius*r0*math.cos(b),radius*r0*math.sin(b))]
            ns=[(normals[j][0],normals[j][1]*math.cos(angle),normals[j][1]*math.sin(angle))
                for j,angle in [(row,a),(row+1,a),(row+1,b),(row,b)]]
            mesh.face(points,'rubber',tuple(sum(n[k] for n in ns) for k in range(3)),ns)
    for side in (-1,1):
        mesh.face([(side*.095,radius*.79*math.cos(i*math.tau/SEGMENTS),
                    radius*.79*math.sin(i*math.tau/SEGMENTS)) for i in range(SEGMENTS)],
                  'rubber',(side,0,0))
