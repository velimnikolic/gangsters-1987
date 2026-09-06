"""Rounded tire shoulders and distinct hubcaps, alloys and steel wheels."""
import math
from geometry import Mesh


def build_wheels(form):
    car=form.car
    radius=car['radius']
    style=car['style']
    wheels=[]
    for side,label in [(-1,'L'),(1,'R')]:
        for axle,where in [(form.front,'F'),(form.rear,'R')]:
            mesh=Mesh(car['id']+'_Wheel_'+where+label)
            tire(mesh,radius)
            if style in ('regent','calder','monarch'):
                mesh.cylinder((side*.113,0,0),radius*.82,.008,'cream',sides=24)
                mesh.cylinder((side*.119,0,0),radius*.75,.009,'tireface',sides=24)
            wheel_color='wheelshade' if style=='hikari' else 'chrome'
            mesh.cylinder((side*.125,0,0),radius*.65,.023,wheel_color,sides=24)
            mesh.cylinder((side*.140,0,0),radius*.52,.014,'wheelshade',sides=24)
            if style=='regent':
                mesh.cylinder((side*.15,0,0),radius*.49,.021,'chrome',sides=24)
                mesh.cylinder((side*.168,0,0),radius*.13,.016,'gold',sides=12)
            elif style=='vahren':
                for i in range(16):
                    a=i*math.tau/16
                    for twist in (-.20,.20):
                        mesh.beam((side*.153,math.cos(a)*radius*.22,math.sin(a)*radius*.22),
                                  (side*.152,math.cos(a+twist)*radius*.53,math.sin(a+twist)*radius*.53),.013,'chrome')
                mesh.cylinder((side*.165,0,0),radius*.19,.017,'chrome',sides=16)
            elif style in ('kronen','hikari','bayside'):
                count=8 if style=='kronen' else 6
                for i in range(count):
                    angle=i*math.tau/count
                    y,z=math.cos(angle)*radius*.42,math.sin(angle)*radius*.42
                    mesh.cylinder((side*.151,y,z),radius*.07,.007,'rubber',sides=10)
                mesh.cylinder((side*.153,0,0),radius*(.31 if style=='bayside' else .24),.019,
                              'wheelshade' if style=='hikari' else 'chrome',sides=16)
            else:
                count=22 if style=='calder' else 15 if style=='monarch' else 10
                for i in range(count):
                    a=i*math.tau/count
                    b=a+(.18 if style=='monarch' else -.12 if style=='calder' else 0)
                    mesh.beam((side*.153,math.cos(a)*radius*.20,math.sin(a)*radius*.20),
                              (side*.152,math.cos(b)*radius*.51,math.sin(b)*radius*.51),
                              .015 if style=='calder' else .026,'chrome')
                mesh.cylinder((side*.165,0,0),radius*.18,.018,'chrome',sides=16)
            wheels.append((mesh,(side*(form.width(axle)-.12),radius,axle)))
    return wheels


def tire(mesh,radius):
    profile=[(-.095,.79),(-.113,.92),(-.078,1),(.078,1),(.113,.92),(.095,.79)]
    normals=[(-1,0),(-.85,.5),(-.25,.97),(.25,.97),(.85,.5),(1,0)]
    for row,((x0,r0),(x1,r1)) in enumerate(zip(profile,profile[1:])):
        for i in range(24):
            a,b=i*math.tau/24,(i+1)*math.tau/24
            points=[(x0,radius*r0*math.cos(a),radius*r0*math.sin(a)),
                    (x1,radius*r1*math.cos(a),radius*r1*math.sin(a)),
                    (x1,radius*r1*math.cos(b),radius*r1*math.sin(b)),
                    (x0,radius*r0*math.cos(b),radius*r0*math.sin(b))]
            ns=[(normals[j][0],normals[j][1]*math.cos(angle),normals[j][1]*math.sin(angle))
                for j,angle in [(row,a),(row+1,a),(row+1,b),(row,b)]]
            wanted=tuple(sum(n[k] for n in ns) for k in range(3))
            mesh.face(points,'rubber',wanted,ns)
    # Close both sidewalls at the bead. The outboard hubcap is decorative; neither
    # side of the tire may depend on it to hide an open hole through the wheel.
    for side in (-1,1):
        ring=[(side*.095,radius*.79*math.cos(i*math.tau/24),
               radius*.79*math.sin(i*math.tau/24)) for i in range(24)]
        mesh.face(ring,'rubber',(side,0,0))
