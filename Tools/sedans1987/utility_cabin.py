"""Wagon/van apertures, thick pillars and rear glazing; no solid panel behind glass."""
import math
from geometry import Mesh
from bodywork import lerp, samples, profile
from cabins import mirror
from window_frames import aperture
from roof_skin import RoofSkin
from palette import COLORS


def van_style(style):return style in ('warden','voyager')


def build_cabin(mesh, form):
    car=form.car;shape=form.shape;style=car['style']
    glass=Mesh(car['id']+'_Glass')
    bb,bt,ft,fb=car['cabin']
    armoured=style=='bastion';police=style=='warden'
    lower,upper=(.20,.90) if armoured else (.12,.94)
    post=.08 if armoured else .050
    sill_color=car['paint'];roof_color=car['roof']
    def raw_width(z):
        t=max(0,min(1,(z-bt)/(ft-bt)))
        return form.w-lerp(shape['roof_rear_inset'],shape['roof_front_inset'],t)-.040*(2*t-1)**4
    def raw_height(u,z):
        t=max(0,min(1,(z-bt)/(ft-bt)))
        return car['height']-profile([(0,0),(.60,.01),(.84,.024),(1,.055)],abs(u))-.03*(2*t-1)**4-.014*abs(u)**3*(2*t-1)**4
    edge=.88 if armoured else .91
    skin=RoofSkin(raw_width,raw_height,bt,ft,[-1,-edge,-.84,-.60,-.045,0,.045,.60,.84,edge,1])
    rw,rh,roof=skin.width,skin.height,skin.point
    mesh.surface(roof,skin.us,skin.zs,roof_color,(0,1,0),smooth=False)
    mesh.face([(x,y-.032,z) for x,y,z in [roof(-1,bt),roof(1,bt),roof(1,ft),roof(-1,ft)]],'upholstery',(0,-1,0))
    def side_point(side,z,t,offset=0):
        return side*(lerp(form.width(z)*.90,rw(z),t)+offset),lerp(form.deck(z),rh(1,z),t),z
    def metal(point,us,vs,color,out):
        start=len(mesh.faces)
        mesh.surface(point,us,vs,color,out,smooth=False)
        for points,_ in list(mesh.faces[start:]):mesh.face(list(reversed(points)),'interior_trim')
    def tex(t):
        color='glass' if armoured else 'window_glass'
        return ((list(COLORS).index(color)+.5)/len(COLORS),.5)
    for side in (-1,1):
        out=(side,.15,0)
        def edge(u,t):return side_point(side,lerp(lerp(bb,bt,t),lerp(fb,ft,t),u),t)
        for lo,hi in [(0,lower),(upper,1)]:
            metal(edge,samples(0,1,6),[lo,hi],roof_color,out)
        bounds=[None]+car['posts']+[None]
        for k in range(len(bounds)-1):
            first=k==0;last=k==len(bounds)-2
            def window(u,t):
                a=lerp(bb,bt,t)+shape['rear_pillar'] if first else bounds[k]+post
                b=lerp(fb,ft,t)-(.15 if armoured else .11) if last else bounds[k+1]-post
                return side_point(side,lerp(a,b,u),t,.012)
            # Police custody windows are small inset apertures in full metal panels.
            lo,hi=(.43,.71) if police and not last else (lower,upper)
            left,right=(.18,.82) if police and not last else (0,1)
            if lo>lower:
                metal(window,[0,1],[lower,lo],roof_color,out)
                metal(window,[0,1],[hi,upper],roof_color,out)
                metal(window,[0,left],[lo,hi],roof_color,out)
                metal(window,[right,1],[lo,hi],roof_color,out)
            aperture(mesh,glass,window,(left,right,lo,hi),.032 if armoured else .021,out,roof_color,tex,
                     corner=(.055,.010,.010,.020) if last else (.012,.045,.035,.012))
            if police and not last:
                for u in samples(left+.07,right-.07,4):
                    a=window(u,lo);b=window(u,hi)
                    mesh.beam((a[0]+side*.014,a[1],a[2]),(b[0]+side*.014,b[1],b[2]),.019,'wheelshade')
            if style=='highland':
                mesh.ribbon([window(u,lo) for u in samples(0,1,4)],.018,'chrome',out)
        # A/B/C/D pillars have visible reverse faces and continuous painted rails.
        for k in range(len(bounds)):
            def pillar(u,t):
                if k==0:a=lerp(bb,bt,t);b=a+shape['rear_pillar']
                elif k==len(bounds)-1:b=lerp(fb,ft,t);a=b-(.15 if armoured else .11)
                else:a,b=bounds[k]-post,bounds[k]+post
                return side_point(side,lerp(a,b,u),t)
            metal(pillar,[0,1],[lower,upper],roof_color if k in (0,len(bounds)-1) else 'rubber',out)
        # Passenger doors: handles sit just ahead of each trailing edge (+Z nose).
        rear=car['posts'][-2] if len(car['posts'])>1 else car['posts'][0]
        cuts=[car['posts'][-1],fb-.12]
        if len(car['posts'])>1:cuts.insert(0,rear)
        for z in cuts:
            low=max(.28,form.arch_y(z)+.026);high=form.deck(z)-.025
            if low>=high:continue
            points=[]
            for y in samples(low,high,6):
                x=form.side_x(y,z);points.append((side*(x+.010),y,form.position_z(x,z,y)))
            mesh.ribbon(points,.005,car['paint']+'_gap',out)
        for z in cuts[:-1]:
            hz=z+.17;y=form.deck(hz)-.13
            mesh.box((side*(form.side_x(y,hz)+.018),y,hz),(.030,.035,.17),'rubber')
        mirror(mesh,form,side,fb-.20,form.deck(fb-.20)+.11)
        if style in ('voyager','warden'):
            # Recessed sliding-door rail, not a bar floating beside the body.
            mesh.ribbon([(side*(form.side_x(car['belt']-.08,z)+.012),car['belt']-.08,z)
                         for z in samples(bb+.22,car['posts'][-1]-.10,5)],.015,'wheelshade',out)
    # Windshield, plus a large rear hatch or two glazed rear van doors.
    for base,top,end in [(fb,ft,1),(bb,bt,-1)]:
        def screen(u,t):
            x=lerp(form.width(base)*.90,rw(top),t)*u
            y=lerp(form.top(u,base)[1],rh(u,top),t)
            z=lerp(base,top,t)+end*.015*(1-u*u)*math.sin(math.pi*t)
            return x,y,z
        lo,hi=(.21,.90) if armoured else (.12,.955)
        if police and end<0:lo,hi=.42,.76
        edge=.88 if armoured else .91
        out=(0,.2,end)
        for low,high in [(0,lo),(hi,1)]:metal(screen,skin.us,[low,high],roof_color,out)
        for left,right in [(-1,-edge),(edge,1)]:metal(screen,[left,right],[lo,hi],roof_color,out)
        split=end<0 and style in ('warden','voyager')
        panes=[(-edge,-.045),(.045,edge)] if split else [(-edge,edge)]
        if split:metal(screen,[-.045,.045],[lo,hi],roof_color,out)
        for left,right in panes:
            pane=lambda u,t:tuple(v+d for v,d in zip(screen(u,t),(0,0,end*.012)))
            aperture(mesh,glass,pane,(left,right,lo,hi),.024 if armoured else .020,out,roof_color,tex,corner=.095,across=skin.us)
            if police and end<0:
                for u in samples(left+.08,right-.08,3):mesh.beam(pane(u,lo),pane(u,hi),.018,'wheelshade')
        if end>0:
            for a,b in [(-.72,-.10),(.12,.73)]:mesh.ribbon([screen(a,lo+.035),screen(b,lo+.055)],.015,'rubber',out)
        else:
            # The tailgate seam continues down into the stamped metal and closes
            # below a separate handle/number-plate recess.
            if split:mesh.ribbon([(0,.55,-form.end-.012),screen(0,lo)],.009,'seam',(0,0,-1))
            else:
                y=car['belt']-.10
                mesh.box((0,y,-form.end-.016),(.23,.035,.026),'rubber')
                mesh.ribbon([screen(-.48,lo+.025),screen(.12,lo+.035)],.018,'rubber',out)
    return glass
