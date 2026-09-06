"""Visible low-poly cabin furniture, merged into the existing opaque body mesh."""
import math
from bodywork import lerp
from seating import seat_roots


def cushion(mesh,center,size,color,lean=0):
    x,y,z=center;w,h,d=size
    bevel=min(.045,h*.24,w*.12)
    outline=[(-w/2+bevel,-h/2),(w/2-bevel,-h/2),(w/2,-h/2+bevel),
             (w/2,h/2-bevel),(w/2-bevel,h/2),(-w/2+bevel,h/2),
             (-w/2,h/2-bevel),(-w/2,-h/2+bevel)]
    rings=[[(x+a,y+b,z+end*d/2-lean*b/h) for a,b in outline] for end in (-1,1)]
    for end,ring in zip((-1,1),rings):mesh.face(ring,color,(0,0,end))
    for i in range(8):
        j=(i+1)%8
        mesh.face([rings[0][i],rings[1][i],rings[1][j],rings[0][j]],color,
                  (outline[i][0]+outline[j][0],outline[i][1]+outline[j][1],0))


def build_interior(mesh,form):
    car=form.car;bb,bt,ft,fb=car['cabin'];split=form.shape['pillar']
    width=form.w*1.60
    seats=seat_roots(form)
    floor=.27;seat_y=seats[0][1]+.43-.085;back_top=car['height']-.32
    upholstery='upholstery' if car['style'] in ('regent','calder','monarch','bayside') else 'dashboard'
    panel='seat_panel' if upholstery=='upholstery' else 'interior_trim'
    mesh.box((0,floor,(bb+fb)/2),(width,.06,fb-bb),'interior_trim')
    # Opaque lining closes the doors below their window apertures.
    for side in (-1,1):
        mesh.box((side*(width/2-.02),(.36+car['belt'])/2,(bb+fb)/2),
                 (.045,car['belt']-.36,fb-bb-.10),upholstery)
        mesh.box((side*(width/2-.065),car['belt']-.13,split+.23),(.10,.07,.42),'interior_trim')
        mesh.box((side*(width/2-.10),car['belt']-.05,split+.38),(.018,.024,.10),'chrome')
    front_z=seats[0][2]
    for x in (seats[0][0],seats[1][0]):
        cushion(mesh,(x,seat_y,front_z),(width*.43,.17,.53),upholstery)
        back_z=front_z-.265
        cushion(mesh,(x,(seat_y+back_top)/2,back_z),
                (width*.42,back_top-seat_y,.135),upholstery,lean=.075)
        mesh.box((x,back_top+.075,back_z-.045),(width*.25,.12,.105),upholstery)
        # An inset centre panel makes the seat silhouette/readable face distinct.
        mesh.box((x,(seat_y+back_top)/2,back_z+.071),
                 (width*.30,(back_top-seat_y)*.72,.008),panel)
    rear_z=seats[2][2];rear_top_z=rear_z-.28
    cushion(mesh,(0,seat_y,rear_z),(width*.94,.17,.50),upholstery)
    cushion(mesh,(0,(seat_y+back_top)/2,rear_top_z+.06),
            (width*.92,back_top-seat_y,.13),upholstery,lean=.075)
    for x in (-width*.27,width*.27):
        mesh.box((x,back_top+.055,rear_top_z+.065),(width*.24,.10,.10),upholstery)
    # Parcel shelf and dashboard hide the non-playable trunk/engine cavities.
    mesh.box((0,car['belt']-.055,(bb+rear_top_z)/2),
             (width,.045,max(.08,rear_top_z-bb)),'interior_trim')
    dash_y=form.deck(fb)-.035
    cushion(mesh,(0,dash_y,fb-.235),(width,.18,.36),'dashboard')
    driver=-width*.25
    mesh.box((driver,dash_y+.092,fb-.34),(.33,.10,.14),'interior_trim')
    for x in (driver-.077,driver+.065):
        mesh.cylinder((x,dash_y+.09,fb-.414),.043,.004,'plate',axis=2,sides=8)
    mesh.box((0,dash_y-.038,fb-.423),(.18,.11,.015),'rubber')
    mesh.box((0,.47,front_z+.16),(.19,.15,.58),'interior_trim')
    mesh.beam((0,.54,front_z+.28),(0,.68,front_z+.26),.024,'rubber')
    mesh.box((0,.68,front_z+.26),(.05,.035,.05),'dashboard')
    steering(mesh,(driver,car['belt']+.055,fb-.60),.145)


def steering(mesh,center,radius):
    x,y,z=center
    def point(a,r,depth=0):
        return (x+r*math.cos(a),y+r*math.sin(a)*.87+depth*.5,
                z+r*math.sin(a)*.5-depth*.87)
    # A twelve-sided rim with square rounded-section shading, fixed to the dash.
    for i in range(12):
        a=i*math.tau/12;b=(i+1)*math.tau/12
        for j in range(4):
            c=j*math.tau/4;d=(j+1)*math.tau/4
            pts=[point(t,radius+.012*math.cos(v),.012*math.sin(v)) for t,v in [(a,c),(b,c),(b,d),(a,d)]]
            mid=(a+b)/2;section=(c+d)/2
            outward=(math.cos(mid)*math.cos(section),math.sin(mid)*.87*math.cos(section)+.5*math.sin(section),
                     math.sin(mid)*.5*math.cos(section)-.87*math.sin(section))
            mesh.face(pts,'interior_trim',outward)
    for angle in (0,math.pi,math.pi*1.5):
        mesh.beam(center,point(angle,radius-.013),.021,'interior_trim')
    mesh.box(center,(.07,.055,.05),'dashboard')
    mesh.beam((x,y,z+.02),(x,y-.09,z+.29),.055,'interior_trim')
