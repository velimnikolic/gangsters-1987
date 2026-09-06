"""Period SUV/van front ends, utility trim, cabin furniture and service fittings."""
import math
import re
from geometry import Mesh
from bodywork import samples,lerp
from interiors import cushion,steering
from lenses import round_lamp
from fascia_skin import surround,surface


def bevel_box(mesh,center,size,color,bevel=.04):
    x,y,z=center;w,h,d=size
    bevel=min(bevel,w*.20,h*.20)
    edge=[(-w/2+bevel,-h/2),(w/2-bevel,-h/2),(w/2,-h/2+bevel),
          (w/2,h/2-bevel),(w/2-bevel,h/2),(-w/2+bevel,h/2),(-w/2,h/2-bevel),(-w/2,-h/2+bevel)]
    rings=[[(x+a,y+b,z+depth*d/2) for a,b in edge] for depth in (-1,1)]
    for end,ring in zip((-1,1),rings):mesh.face(ring,color,(0,0,end))
    for i in range(8):
        j=(i+1)%8
        mesh.face([rings[0][i],rings[1][i],rings[1][j],rings[0][j]],color,(edge[i][0]+edge[j][0],edge[i][1]+edge[j][1],0))


def fascia(mesh,form):
    car=form.car;style=car['style'];van=style in ('warden','voyager')
    lamp_y=car['belt']-form.shape['hood_drop']-(.09 if van else .12)
    lamp_x=form.w*.70
    # Recesses follow the planform sweep. Lamps are thin inserts, below the hood.
    def plate(x,y,w,h,color,end=1,depth=.025):
        def pt(u,t):
            px=x+u*w/2;py=y+t*h/2
            return px,py,form.skin_z(px,end,py)+end*depth
        surface(mesh,form,pt,samples(-1,1,8 if w>.6 else 4),[-1,1],color,end)
    for side in (-1,1):
        x=side*lamp_x
        width=.34 if style in ('trail','ranger') else .40 if style=='highland' else .29 if van else .35
        height=.28 if style in ('trail','ranger') else .25 if style=='highland' else .15
        plate(x,lamp_y,width+.045,height+.046,'rubber')
        if style in ('trail','highland'):
            if style=='trail':round_lamp(mesh,form,x,lamp_y,.126)
            else:
                for dx in (-.104,.104):round_lamp(mesh,form,x+dx,lamp_y,.091)
        else:
            plate(x,lamp_y,width+.016,height+.017,'chrome',depth=.031)
            plate(x,lamp_y,width,height,'lamp_front',depth=.035)
        if van:
            plate(x,lamp_y-.215,width+.035,.183,'chrome',depth=.031)
            plate(x,lamp_y-.215,width,.145,'lamp_front',depth=.035)
            plate(side*(form.w*.88),lamp_y-.11,.062,.39,'lamp_marker',depth=.034)
        elif style=='ranger':
            plate(side*(form.w*.91),lamp_y,.056,.27,'lamp_marker',depth=.034)
        else:plate(x,lamp_y-height/2-.072,width*.82,.055,'lamp_marker',depth=.034)
        # Tall tail lights remain clear of a hatch-mounted spare.
        rear_x=side*form.w*.80
        plate(rear_x,.88 if van else .89,.14,.29,'rubber',-1)
        plate(rear_x,.92,.113,.18,'lamp_tail',-1,.033)
        plate(rear_x,.80,.113,.055,'lamp_marker',-1,.034)
    grille_width=form.w*.97
    gy=lamp_y-.10 if van else lamp_y
    gh=.38 if van else .31
    surround(mesh,form,0,gy,grille_width,gh,'chrome' if style in ('ranger','highland','voyager') else 'wheelshade')
    plate(0,gy,grille_width,gh,'rubber',depth=.008)
    for y in samples(gy-gh*.40,gy+gh*.40,6 if van else 5):
        plate(0,y,grille_width-.07,.014,'wheelshade' if style in ('trail','bastion','warden') else 'chrome',depth=.017)
    if style in ('ranger','voyager'):
        for x in (-.16,.16):plate(x,gy,.026,gh,'chrome',depth=.019)
    # Swept bumpers have a bevel on the top and a recessed rubber contact face.
    for end in (-1,1):
        form.bumper(mesh,end,'chrome' if style=='highland' else 'wheelshade')
        plate(0,.43,.37,.10,'plate',end,.106)
        plate(0,.43,.29,.026,'wheelshade',end,.108)
    if style in ('trail','bastion'):
        bevel_box(mesh,(0,.69,form.end+.13),(1.13,.075,.075),'rubber',.014)
        for side in (-1,1):
            mesh.beam((side*.48,.42,form.end+.14),(side*.48,.91,form.end+.14),.064,'rubber')
            mesh.beam((side*.36,.30,form.end+.08),(side*.36,.45,form.end+.15),.07,'wheelshade')
    beam_x=lamp_x+(.104 if style=='highland' else 0)
    return [(-beam_x,lamp_y,form.skin_z(-beam_x,1,lamp_y)+.055),
            (beam_x,lamp_y,form.skin_z(beam_x,1,lamp_y)+.055)]


def seat_layout(form):
    car=form.car;van=car['style'] in ('warden','voyager')
    top=.755 if van else .735
    z=car['posts'][-1]+.30
    width=car['width']*.40
    return [(side*width/2,top-.43,row) for row in (z,z-1.03) for side in (-1,1)]


def interior(mesh,form):
    car=form.car;style=car['style'];bb,bt,ft,fb=car['cabin'];van=style in ('warden','voyager')
    width=car['width']*.80;floor=.39 if van else .33
    mesh.box((0,floor,(bb+fb)/2),(width,.07,fb-bb),'interior_trim')
    top=.755 if van else .735;seat_y=top-.08
    front_z=car['posts'][-1]+.30
    back_top=min(car['height']-.40,1.55)
    fabric='dashboard' if style in ('trail','warden','bastion') else 'upholstery'
    for x in (-car['width']*.20,car['width']*.20):
        cushion(mesh,(x,seat_y,front_z),(width*.43,.16,.54),fabric)
        cushion(mesh,(x,(top+back_top)/2,front_z-.29),(width*.42,back_top-top,.14),fabric,lean=.07)
        mesh.box((x,back_top+.07,front_z-.32),(width*.24,.12,.12),fabric)
    if style=='warden':
        # Rear occupants face each other; a solid partition separates the cab.
        z=.19
        mesh.box((0,.84,z),(width,.84,.065),'wheelshade')
        for x in samples(-width*.47,width*.47,10):mesh.beam((x,1.26,z),(x,car['height']-.13,z),.025,'wheelshade')
        for side in (-1,1):
            cushion(mesh,(side*width*.34,.73,-1.02),(width*.27,.13,1.80),'dashboard')
            mesh.box((side*width*.46,1.02,-1.02),(.08,.51,1.8),'dashboard')
    else:
        rows=[front_z-1.03]
        if style=='voyager':rows.append(front_z-2.00)
        for z in rows:
            cushion(mesh,(0,seat_y,z),(width*.92,.16,.52),fabric)
            cushion(mesh,(0,(top+back_top)/2,z-.29),(width*.92,back_top-top,.14),fabric,lean=.07)
            for x in (-width*.31,0,width*.31):mesh.box((x,back_top+.06,z-.33),(width*.20,.11,.11),fabric)
    for side in (-1,1):
        mesh.box((side*(width/2-.02),(.42+car['belt'])/2,(bb+fb)/2),(.045,car['belt']-.42,fb-bb-.14),'interior_trim')
        mesh.box((side*(width/2-.065),car['belt']-.17,front_z),(.10,.085,.48),fabric)
    dash=car['belt']-.05
    cushion(mesh,(0,dash,fb-.22),(width,.20,.38),'dashboard')
    x=-car['width']*.20
    bevel_box(mesh,(x,dash+.12,fb-.36),(.34,.11,.14),'interior_trim',.025)
    steering(mesh,(x,car['belt']+.085,fb-.56),.16)
    mesh.box((0,.62,front_z+.14),(.20,.23,.58),'dashboard')
    mesh.beam((0,.72,front_z+.20),(0,.90,front_z+.13),.025,'rubber')
    mesh.box((0,.90,front_z+.13),(.07,.035,.04),'rubber')


FONT={
 'P':['11110','10001','10001','11110','10000','10000','10000'],
 'O':['01110','10001','10001','10001','10001','10001','01110'],
 'L':['10000','10000','10000','10000','10000','10000','11111'],
 'I':['11111','00100','00100','00100','00100','00100','11111'],
 'C':['01111','10000','10000','10000','10000','10000','01111'],
 'E':['11111','10000','10000','11110','10000','10000','11111'],
}


def police_letters(mesh,form):
    # Flat letter strokes are merged into the body, no text object/material per van.
    step=.033;text='POLICE';length=(len(text)*6-1)*step
    for side in (-1,1):
        for k,char in enumerate(text):
            for row,bits in enumerate(FONT[char]):
                for run in re.finditer('1+',bits):
                    col=(run.start()+run.end()-1)/2;span=run.end()-run.start()
                    z=-.58-side*(length/2-(k*6+col)*step);y=.88+(3-row)*step
                    points=[]
                    for dz,dy in [(-.5,-.5),(.5,-.5),(.5,.5),(-.5,.5)]:
                        pz=z+dz*step*span;py=y+dy*step
                        points.append((side*(form.side_x(py,pz)+.022),py,pz))
                    mesh.face(points,'plate',(side,0,0))



def trim(mesh,form,wheels):
    car=form.car;style=car['style'];bb,bt,ft,fb=car['cabin']
    # Broad fitted wheel-arch cladding and side steps make the tall body read as
    # a utility vehicle without stretching a sedan mesh vertically.
    for side in (-1,1):
        for axle in (form.front,form.rear):
            for a,b in zip(samples(0,math.pi,12),samples(0,math.pi,12)[1:]):
                points=[]
                for angle,r in [(a,form.arch),(b,form.arch),(b,form.arch+.049),(a,form.arch+.049)]:
                    z=axle+r*math.cos(angle);y=car['radius']+r*math.sin(angle)
                    x=form.side_x(y,z)
                    points.append((side*(x+.017),y,form.position_z(x,z,y)))
                mesh.face(points,'rubber',(side,0,0))
        length=car['wheelbase']-form.arch*2-.08
        bevel_box(mesh,(side*(form.w-.04),.30,(form.front+form.rear)/2),(.20,.095,length),'rubber',.025)
        # A recessed trim band spans the broad door face.
        if style in ('ranger','voyager','warden','bastion'):
            low,high=(.72,1.045) if style=='warden' else (.35,.51)
            color='navy' if style=='warden' else 'cream' if style in ('ranger','voyager') else 'interior_trim'
            zs=samples(form.rear+form.arch+.025,form.front-form.arch-.025,8)
            def band(z,t):
                y=lerp(low,high,t);return side*(form.side_x(y,z)+.012),y,z
            mesh.surface(band,zs,[0,1],color,(side,0,0),smooth=False)
        if style=='bastion':
            # Ballistic door overlays have a visible edge rather than rivet clutter.
            for a,b in [(-.89,.18),(.34,fb-.15)]:
                def plate(u,t):
                    z=lerp(a,b,u);y=lerp(.63,car['belt']-.07,t)
                    return side*(form.side_x(y,z)+.028),y,z
                mesh.surface(plate,[0,1],[0,1],'interior_trim',(side,0,0),smooth=False)
                for u in (.08,.92):
                    for t in (.10,.90):
                        x,y,z=plate(u,t);mesh.cylinder((x+side*.004,y,z),.013,.006,'wheelshade',sides=6)
    if style in ('ranger','highland','bastion'):
        for side in (-1,1):
            x=side*(form.w-.19)
            for z in (bt+.30,ft-.17):mesh.beam((x,car['height']-.055,z),(x,car['height']+.075,z),.037,'rubber')
            mesh.beam((x,car['height']+.075,bt+.22),(x,car['height']+.075,ft-.12),.035,'wheelshade')
        for z in (bt+.50,ft-.28):mesh.beam((-(form.w-.19),car['height']+.066,z),((form.w-.19),car['height']+.066,z),.034,'rubber')
    if style=='trail':
        wheel=next(wheel for wheel,_ in wheels if wheel.name.endswith('FR'))
        mesh.add(wheel,position=(.13,1.08,-form.end-.15),yaw=90)
        mesh.beam((-.30,.55,-form.end-.075),(.30,.55,-form.end-.075),.066,'wheelshade')
    if style=='warden':
        police_letters(mesh,form)
        bevel_box(mesh,(0,car['height']+.07,.74),(1.33,.075,.24),'wheelshade',.025)
        for side,color in [(-1,'red'),(1,'blue')]:
            mesh.cylinder((side*.43,car['height']+.17,.74),.135,.17,color,axis=1,sides=12)
            mesh.cylinder((side*.43,car['height']+.27,.74),.11,.028,color,axis=1,sides=12)
        mesh.beam((form.w*.72,car['height']-.03,-1.30),(form.w*.72,car['height']+.50,-1.30),.010,'rubber')
