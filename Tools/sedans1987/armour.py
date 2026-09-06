"""Visible ballistic overlays and reinforced bumpers, merged with the body."""
import math
from bodywork import samples
from geometry import Mesh
from wheels import tire,annulus


def fit_armour(mesh,form):
    car=form.car;bb,bt,ft,fb=car['cabin']
    rear,post=bb+.09,car['posts'][-1]
    for side in (-1,1):
        for a,b in [(rear+.09,post-.08),(post+.09,fb-.15)]:
            low,high=.58,car['belt']-.05;ch=.065
            outline=[(a+ch,low),(b-ch,low),(b,low+ch),(b,high-ch),
                     (b-ch,high),(a+ch,high),(a,high-ch),(a,low+ch)]
            def p(z,y,depth):return side*(form.side_x(y,z)+depth),y,z
            front=[p(z,y,.067) for z,y in outline]
            back=[p(z,y,.008) for z,y in outline]
            mesh.face(front,'wheelshade',(side,0,0))
            for i in range(8):
                j=(i+1)%8
                mesh.face([back[i],back[j],front[j],front[i]],'interior_trim',(side,0,0))
            # Pressed diagonal ribs, plus chunky visible external hinges.
            for za,ya,zb,yb in [(a+.12,low+.11,b-.12,high-.11),(a+.12,high-.11,b-.12,low+.11)]:
                mesh.beam(p(za,ya,.086),p(zb,yb,.086),.030,'interior_trim')
            for y in (low+.09,high-.09):
                mesh.box(p(b-.03,y,.09),(.095,.08,.10),'wheelshade')
            for z,y in [(a+.12,low+.085),(b-.12,low+.085),(a+.12,high-.085),(b-.12,high-.085)]:
                mesh.face([p(z+.018*math.cos(t),y+.018*math.sin(t),.069) for t in samples(0,math.tau,6)[:-1]],'chrome',(side,0,0))
        length=car['wheelbase']-form.arch*2-.05
        mesh.box((side*(form.w+.015),.45,(form.front+form.rear)/2),(.15,.16,length),'wheelshade')
    # A welded full-width guard stays outside both round headlamp apertures.
    mesh.box((0,.49,form.end+.09),(1.88,.19,.19),'interior_trim')
    guard=[(-.86,.66),(-.96,.76),(-.96,1.04),(-.86,1.14),(.86,1.14),(.96,1.04),(.96,.76),(.86,.66)]
    for a,b in zip(guard,guard[1:]+guard[:1]):
        mesh.beam((a[0],a[1],form.end+.16),(b[0],b[1],form.end+.16),.055,'wheelshade')
    for x in (-.49,0,.49):
        mesh.beam((x,.68,form.end+.16),(x,1.12,form.end+.16),.043,'wheelshade')
    for x in (-.38,.38):mesh.box((x,.38,form.end+.17),(.13,.065,.12),'chrome')
    # Raised armoured intake, broad fender shoulders and a simple period aerial.
    from utility_details import bevel_box
    bevel_box(mesh,(0,car['belt']+.06,fb+.48),(.79,.13,.47),'interior_trim')
    for x in samples(-.30,.30,7):
        mesh.box((x,car['belt']+.058,fb+.719),(.045,.045,.009),'rubber')
    for side in (-1,1):
        mesh.box((side*(form.w-.055),car['belt']-.03,form.front),(.18,.095,.97),'wheelshade')
    mesh.beam((form.w-.17,car['height']-.08,bt+.18),(form.w-.17,car['height']+.30,bt+.18),.013,'rubber')
    # Rear quarter plates remain below the glazing and clear of the wheel arches.
    for side in (-1,1):
        mesh.box((side*(form.w*.90),car['belt']-.12,-form.end+.36),(.10,.19,.33),'interior_trim')


def armoured_wheels(form):
    wheels=[];r=form.car['radius']
    for side,label in [(-1,'L'),(1,'R')]:
        for axle,where in [(form.front,'F'),(form.rear,'R')]:
            mesh=Mesh(form.car['id']+'_Wheel_'+where+label)
            tire(mesh,r)
            mesh.faces=[([(x*1.38,y,z) for x,y,z in points],color) for points,color in mesh.faces]
            for x,rad,color in [(.151,.61,'wheelshade'),(.155,.46,'interior_trim')]:
                mesh.face([(side*x,r*rad*math.cos(a),r*rad*math.sin(a)) for a in samples(0,math.tau,16)[:-1]],color,(side,0,0))
            annulus(mesh,side,.156,r*.56,r*.62,'wheelshade')
            mesh.cylinder((side*.162,0,0),r*.24,.025,'wheelshade',sides=8)
            for i in range(8):
                a=i*math.tau/8
                for rad,size,color in [(.50,.013,'chrome'),(.36,.036,'rubber')]:
                    cy,cz=r*rad*math.cos(a),r*rad*math.sin(a)
                    mesh.face([(side*.159,cy+size*math.cos(t),cz+size*math.sin(t)) for t in samples(0,math.tau,6)[:-1]],color,(side,0,0))
            # Moulded shoulder grooves use existing tire facets, no extra boxes.
            for i in range(16):
                a,b=i*math.tau/16,(i+1)*math.tau/16
                for sign in (-1,1):
                    points=[]
                    for x,t in [(sign*.025,.22),(sign*.142,.45),(sign*.142,.65),(sign*.025,.42)]:
                        y=r*((1-t)*math.cos(a)+t*math.cos(b));z=r*((1-t)*math.sin(a)+t*math.sin(b))
                        points.append((x,y*1.001,z*1.001))
                    mesh.face(points,'tireface',(0,math.cos(a),math.sin(a)))
            wheels.append((mesh,(side*(form.width(axle)-.11),r,axle)))
    return wheels


def truck_bed(mesh,form):
    car=form.car;bb=car['cabin'][0];back=-form.end+.105;front=bb-.025;floor=.72
    mesh.box((0,floor,(front+back)/2),(form.w*1.50,.055,front-back),'interior_trim')
    for side in (-1,1):
        inner=[]
        for z,y in [(back,floor),(front,floor),(front,form.deck(front)),(back,form.deck(back))]:
            inner.append((side*form.width(z)*.756,y,form.position_z(side*form.width(z)*.756,z)))
        mesh.face(inner,'interior_trim',(-side,0,0))
        mesh.ribbon([(side*form.width(z)*.82,form.deck(z)+.015,form.position_z(side*form.width(z)*.82,z)) for z in samples(back,front,4)],.07,'wheelshade',(0,1,0))
    for z,normal in [(back,(0,0,1)),(front,(0,0,-1))]:
        mesh.face([(-form.w*.756,floor,z),(form.w*.756,floor,z),(form.w*.756,car['belt'],z),(-form.w*.756,car['belt'],z)],'interior_trim',normal)
    for x in samples(-.62,.62,7):mesh.box((x,floor+.031,(front+back)/2),(.018,.018,front-back-.03),'wheelshade')


def window_guard(mesh,point,low,high,side):
    # Recessed horizontal slats within a bolted frame, clear of door handles.
    def p(u,t):
        x,y,z=point(u,t);return x+side*.048,y,z
    for t in (low+.028,high-.028):
        mesh.beam(p(.065,t),p(.935,t),.032,'wheelshade')
    for u in (.065,.935):mesh.beam(p(u,low+.028),p(u,high-.028),.032,'wheelshade')
    for t in samples(low+.13,high-.13,3):mesh.beam(p(.075,t),p(.925,t),.026,'interior_trim')
    for u in (.075,.925):
        for t in (low+.028,high-.028):
            x,y,z=p(u,t)
            mesh.face([(x+side*.018,y+.012*math.cos(a),z+.012*math.sin(a)) for a in samples(0,math.tau,6)[:-1]],'chrome',(side,0,0))


def lightbar(mesh,form):
    height=form.car['height'];z=form.car['cabin'][2]-.045
    for x in (-.56,.56):mesh.box((x,height+.045,z),(.055,.13,.08),'wheelshade')
    mesh.box((0,height+.10,z),(1.39,.065,.19),'interior_trim')
    # Four enclosed rectangular halogen units, appropriate to the period.
    for x in (-.49,-.165,.165,.49):
        mesh.box((x,height+.18,z+.025),(.295,.16,.14),'rubber')
        mesh.face([(x+u*.13,height+.18+t*.058,z+.097) for u,t in [(-1,-1),(1,-1),(1,1),(-1,1)]],'lamp_front',(0,0,1))
    return [(-.34,height+.18,z+.115),(.34,height+.18,z+.115)]
