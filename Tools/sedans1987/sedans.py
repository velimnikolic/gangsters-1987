"""Seven authored silhouettes, with shared construction for period sedan details."""
import math
from geometry import Mesh


def build_car(car):
    body=Mesh(car['id']+'_Body')
    w=car['width']/2
    length=car['length']/2
    belt=car['belt']
    height=car['height']
    radius=car['radius']
    front=car['wheelbase']/2+.06
    rear=front-car['wheelbase']
    back_base,back_top,front_top,front_base=car['cabin']
    paint,roof,style=car['paint'],car['roof'],car['style']
    # A chamfered shoulder and lower panels with actual open wheel arches.
    body.box((0,.34,0),(w*1.54,.14,length*1.85),'rubber')
    arch=radius+.065
    breaks=[(-length+.13,.38)]
    for axle in (rear,front):
        for step in range(9):
            angle=math.pi-step*math.pi/8
            breaks.append((axle+arch*math.cos(angle),radius+arch*math.sin(angle)))
    breaks.append((length-.13,.38))
    for side in (-1,1):
        for (z0,y0),(z1,y1) in zip(breaks,breaks[1:]):
            body.face([(side*w,y0,z0),(side*w,y1,z1),(side*w,belt-.075,z1),
                       (side*w,belt-.075,z0)],paint,(side,0,0))
            body.face([(side*w,y0,z0),(side*w,y1,z1),
                       (side*(w-.14),y1,z1),(side*(w-.14),y0,z0)],'rubber',(0,-1,0))
        body.face([(side*w,belt-.075,-length+.13),(side*w,belt-.075,length-.13),
                   (side*(w-.09),belt,length-.13),(side*(w-.09),belt,-length+.13)],paint,(side,1,0))
        for end in (-1,1):
            body.face([(side*w,.38,end*(length-.13)),(side*w,belt-.075,end*(length-.13)),
                       (side*(w-.09),belt-.075,end*length),(side*(w-.09),.38,end*length)],paint,(side,0,end))
    for end in (-1,1):
        body.face([(-w+.09,.38,end*length),(w-.09,.38,end*length),
                   (w-.09,belt-.075,end*length),(-w+.09,belt-.075,end*length)],paint,(0,0,end))
        body.face([(-w+.09,belt-.075,end*length),(w-.09,belt-.075,end*length),
                   (w-.09,belt,end*(length-.13)),(-w+.09,belt,end*(length-.13))],paint,(0,1,end))
    # Hood and deck are separate three-box volumes, not hatchbacks.
    for za,zb in [(-length+.13,back_base),(front_base,length-.13)]:
        body.face([(-w+.09,belt,za),(-w+.09,belt,zb),(w-.09,belt,zb),(w-.09,belt,za)],paint,(0,1,0))
    roof_w=w-.22
    base_w=w-.105
    for side in (-1,1):
        shell=[(side*base_w,belt,back_base),(side*base_w,belt,front_base),
               (side*roof_w,height-.045,front_top),(side*roof_w,height-.045,back_top)]
        body.face(shell,roof,(side,0,0))
        # Inset glazing leaves A/B/C pillars visible. Two independent door windows per side.
        def window_point(z,t):
            return (side*(base_w+(roof_w-base_w)*t+.005),belt+(height-.045-belt)*t,z)
        split=-.24 if style!='hikari' else -.19
        windows=[[(back_base+.15,.1),(split-.055,.1),(split-.055,.87),(back_top+.12,.87)],
                 [(split+.055,.1),(front_base-.16,.1),(front_top-.10,.87),(split+.055,.87)]]
        for window in windows:
            pts=[window_point(z,t) for z,t in window]
            body.face(pts,'glass',(side,0,0))
            for a,b in zip(pts,pts[1:]+pts[:1]):
                body.beam(a,b,.017,'chrome' if style!='hikari' else 'rubber')
        # All seven have four visible door outlines and four handles.
        for z in (back_base+.12,split,front_base-.12):
            body.beam((side*(w+.003),.42,z),(side*(w+.003),belt-.095,z),.009,'seam')
        body.beam((side*(w+.005),.42,back_base+.12),(side*(w+.005),.42,front_base-.12),.009,'seam')
        for z in (split-.23,front_base-.45):
            body.box((side*(w+.021),belt-.16,z),(.035,.034,.155),'chrome' if style!='hikari' else 'rubber')
        trim='cladding' if style=='kronen' else 'chrome'
        body.box((side*(w+.009),belt-.22,0),(.022,.032 if style!='kronen' else .085,length*1.86),trim)
        if style in ('regent','calder','monarch'):
            body.box((side*(w+.008),belt-.11,0),(.012,.01,length*1.84),'gold')
        # Side mirror stalks and dark reflective faces.
        body.beam((side*(w-.03),belt+.045,front_base-.16),(side*(w+.12),belt+.06,front_base-.22),.04,'rubber')
        body.box((side*(w+.15),belt+.09,front_base-.25),(.19,.115,.16),paint)
        body.box((side*(w+.15),belt+.09,front_base-.334),(.155,.075,.012),'glasslight')
        body.box((side*(w+.012),.63,length-.28),(.024,.067,.15),'amber')
        body.box((side*(w+.012),.63,-length+.28),(.024,.067,.15),'red')
    # Roof slab, chamfered along both sides.
    body.box((0,height-.028,(front_top+back_top)/2),(roof_w*2,.056,front_top-back_top),roof)
    for za,zb,end in [(back_base,back_top,-1),(front_base,front_top,1)]:
        frame=[(-base_w,belt,za),(base_w,belt,za),(roof_w,height-.045,zb),(-roof_w,height-.045,zb)]
        body.face(frame,paint,(0,1,end))
        def windshield(x,t):
            width=base_w+(roof_w-base_w)*t
            return (x*(width-.055),belt+(height-.045-belt)*t+.006,za+(zb-za)*t+end*.004)
        pts=[windshield(-1,.10),windshield(1,.10),windshield(1,.88),windshield(-1,.88)]
        body.face(pts,'glass',(0,1,end))
        for a,b in zip(pts,pts[1:]+pts[:1]):
            body.beam(a,b,.018,'chrome' if style!='hikari' else 'rubber')
        if end==1:
            for x in (-.35,.32):
                body.beam((x-.15,belt+.048,za-.057),(x+.11,belt+.066,za-.089),.013,'rubber')
        else:
            # Raised centre brake lamp, visible through/on the dark rear glass.
            p=windshield(0,.17)
            body.box((p[0],p[1]+.018,p[2]-.022),(.19,.047,.07),'red')
    if style in ('kronen','albion'):
        body.box((0,height+.003,-.04),(roof_w*1.45,.007,.61),'seam')
        body.box((0,height+.008,-.04),(roof_w*1.40,.007,.57),paint)
    if style=='albion':
        # Subtle raised centre bonnet and separate fender ridges.
        body.box((0,belt+.024,(front_base+length-.18)/2),(.56,.048,length-.18-front_base),paint)
    fascia(body,car,w,length,belt)
    # Florida-inspired rear plate with an orange emblem; no modern plates or LED strips.
    body.box((0,.62,-length-.029),(.32,.145,.025),'plate')
    body.cylinder((0,.63,-length-.047),.026,.006,'amber',axis=2,sides=8)
    for x in (-.115,-.08,.08,.115):
        body.box((x,.605,-length-.047),(.017,.028,.008),'green')
    body.beam((w-.12,belt,length-.55),(w-.12,belt+.49,length-.55),.009,'chrome')
    body.box((-.45,.31,-length-.04),(.10,.065,.19),'rubber')
    wheels=[]
    for side,label in [(-1,'L'),(1,'R')]:
        for axle,where in [(front,'F'),(rear,'R')]:
            wheel=Mesh(car['id']+'_Wheel_'+where+label)
            wheel.cylinder((0,0,0),radius,.21,'rubber')
            wheel.cylinder((side*.11,0,0),radius*.79,.016,'tireface')
            if style in ('regent','calder','monarch'):
                wheel.cylinder((side*.123,0,0),radius*.77,.014,'cream')
                wheel.cylinder((side*.135,0,0),radius*.69,.015,'tireface')
            wheel.cylinder((side*.147,0,0),radius*.59,.022,'chrome')
            wheel.cylinder((side*.164,0,0),radius*.43,.017,'wheelshade')
            count=12 if style=='monarch' else 8 if style in ('kronen','albion') else 6
            for i in range(count):
                angle=i*math.tau/count
                y,z=math.sin(angle)*radius*.38,math.cos(angle)*radius*.38
                wheel.beam((side*.18,y*.6,z*.6),(side*.18,y*1.36,z*1.36),.022,'chrome')
            wheel.cylinder((side*.18,0,0),radius*.18,.02,'chrome' if style!='hikari' else 'rubber')
            wheels.append((wheel,(side*(w-.065),radius,axle)))
    return body,wheels


def fascia(mesh,car,w,length,belt):
    style=car['style']
    for end in (-1,1):
        mesh.box((0,.49,end*(length+.025)),(w*1.98,.15,.15),'rubber' if style=='hikari' else 'chrome')
        mesh.box((0,.485,end*(length+.106)),(w*1.85,.062,.019),'rubber')
    front=length+.016
    y=belt-.22
    if style=='regent':
        grille_w,grille_h=.68,.47
    elif style=='kronen':
        grille_w,grille_h=.69,.32
    elif style=='calder':
        grille_w,grille_h=.62,.41
    elif style=='albion':
        grille_w,grille_h=.68,.21
    elif style=='monarch':
        grille_w,grille_h=.81,.32
    else:
        grille_w,grille_h=.80,.18
    mesh.box((0,y,front),(grille_w+.05,grille_h+.045,.045),'chrome' if style!='hikari' else 'rubber')
    mesh.box((0,y,front+.028),(grille_w,grille_h,.018),'rubber')
    vertical=style in ('regent','calder','monarch')
    for i in range(1,12 if vertical else 5):
        if vertical:
            mesh.box((-grille_w/2+grille_w*i/12,y,front+.042),(.019,grille_h,.011),'chrome')
        else:
            mesh.box((0,y-grille_h/2+grille_h*i/5,front+.042),(grille_w,.016,.011),'chrome')
    if style in ('regent','kronen','calder','monarch'):
        mesh.beam((0,belt, length-.16),(0,belt+.12,length-.16),.018,'chrome')
        mesh.box((0,belt+.12,length-.16),(.058,.045,.018),'gold' if style=='regent' else 'chrome')
    for side in (-1,1):
        if style=='albion':
            for x,r in [(w-.18,.135),(w-.45,.105)]:
                mesh.cylinder((side*x,y+.015,front+.015),r+.027,.064,'chrome',axis=2)
                mesh.cylinder((side*x,y+.015,front+.054),r,.02,'headlight',axis=2)
        else:
            x=(grille_w/2+w-.08)/2
            lamp_width=w-.09-grille_w/2-.08
            mesh.box((side*x,y+.012,front),(lamp_width+.04,.255,.04),'chrome')
            mesh.box((side*x,y+.012,front+.029),(lamp_width,.196,.025),'headlight')
            if style in ('regent','calder'):
                mesh.box((side*x,y+.012,front+.047),(.036,.22,.015),'chrome')
            else:
                for line in (-.055,0,.055):
                    mesh.box((side*x,y+.012+line,front+.044),(lamp_width,.006,.008),'glasslight')
        mesh.box((side*(w-.055),y,front),(.07,.19,.044),'amber')
        if style=='calder':
            mesh.box((side*(w-.085),.77,-length-.028),(.11,.37,.05),'chrome')
            mesh.box((side*(w-.085),.77,-length-.058),(.07,.30,.014),'red')
        else:
            mesh.box((side*(w-.30),.77,-length-.024),(.45,.19,.042),'chrome')
            mesh.box((side*(w-.30),.77,-length-.05),(.40,.15,.022),'red')
            mesh.box((side*(w-.46),.77,-length-.065),(.07,.12,.009),'headlight')
