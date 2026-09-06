"""Readable window openings and slim pillars, with surface trim and atlas glass."""
import math
from bodywork import lerp,samples,profile
from palette import glass_uv
from geometry import Mesh


def frame(mesh,point,limits,width,color,outward):
    left,right,bottom,top=limits
    for coords in [[(u,bottom) for u in samples(left,right,4)],
                   [(u,top) for u in samples(left,right,4)],
                   [(left,t) for t in (bottom,top)],[(right,t) for t in (bottom,top)]]:
        mesh.ribbon([point(u,t) for u,t in coords],width,color,outward)


def build_cabin(mesh,form):
    car=form.car;s=form.shape
    glazing=Mesh(car['id']+'_Glass')
    def metal(point,us,vs,color,outward):
        start=len(mesh.faces)
        mesh.surface(point,us,vs,color,outward,smooth=False)
        # A dark reverse face gives the visible pillars an interior lining.
        for points,_ in list(mesh.faces[start:]):
            mesh.face(list(reversed(points)),'interior_trim')
    bb,bt,ft,fb=car['cabin']
    def roof_width(z):
        t=max(0,min(1,(z-bt)/(ft-bt)))
        return form.w-lerp(s['roof_rear_inset'],s['roof_front_inset'],t)-.035*(2*t-1)**4
    def roof_height(u,z):
        t=max(0,min(1,(z-bt)/(ft-bt)))
        roll=profile([(0,0),(.62,.008),(.84,.022),(1,.065)],abs(u))
        return car['height']-roll-s['roof_crown']*u*u*.3-s['roof_end_drop']*(2*t-1)**4
    def roof_point(u,z):
        return (u*roof_width(z),roof_height(u,z),z)
    mesh.surface(roof_point,[-1,-.84,-.62,0,.62,.84,1],samples(bt,ft,6),car['roof'],(0,1,0),smooth=False)
    mesh.face([(x,y-.026,z) for x,y,z in [roof_point(-1,bt),roof_point(1,bt),roof_point(1,ft),roof_point(-1,ft)]],
              'upholstery',(0,-1,0))
    def side_point(side,z,t,offset=0):
        x=lerp(form.width(z)*.90,roof_width(z),t)+.012*math.sin(math.pi*t)
        return (side*(x+offset),lerp(form.deck(z),roof_height(1,z),t),z)
    split=s['pillar']
    for side in (-1,1):
        outward=(side,.25,0)
        def panel(u,t):
            z=lerp(lerp(bb,bt,t),lerp(fb,ft,t),u)
            return side_point(side,z,t)
        # Only frame bands are opaque. There is no painted panel behind glass.
        for low,high in [(0,.08),(.94,1)]:
            metal(panel,samples(0,1,6),[low,high],car['roof'],outward)
        for kind in ('rear','middle','front'):
            def pillar(u,t):
                if kind=='rear':a=lerp(bb,bt,t);b=a+s['rear_pillar']
                elif kind=='front':b=lerp(fb,ft,t);a=b-.065
                else:a,b=split-.035,split+.035
                return side_point(side,lerp(a,b,u),t)
            metal(pillar,[0,1],samples(.08,.94,3),'rubber' if kind=='middle' else car['roof'],outward)
        # Large clean apertures: the C-pillar has a deliberate mass, the front
        # pillar is slim, and the B-pillar is dark instead of a double chrome tube.
        for where in ('rear','front'):
            def window(u,t):
                back=lerp(bb,bt,t)+s['rear_pillar'] if where=='rear' else split+.035
                front=split-.035 if where=='rear' else lerp(fb,ft,t)-.065
                return side_point(side,lerp(back,front,u),t,.014)
            glazing.surface(window,samples(0,1,4),[.08,.46,.72,.94],'window_glass',outward,
                         uv=lambda u,t:glass_uv(t))
            frame(mesh,window,(0,1,.08,.94),.012,'rubber',outward)
            if car['style'] in ('regent','kronen','calder','monarch'):
                # One narrow bright lower edge is enough to identify plated trim.
                mesh.ribbon([window(u,.066) for u in samples(0,1,4)],.012,'chrome',outward)
            if where=='rear' and car['style'] in ('regent','kronen','bayside'):
                mesh.ribbon([window(.23,t) for t in (.08,.94)],.022,'rubber',outward)
        mesh.ribbon([side_point(side,split,t,.016) for t in (.07,.95)],.066,'rubber',outward)
        # Door seams are thin strips following the metal, not protruding bars.
        for z in (bb+.07,split,fb-.13):
            low=max(.27,form.arch_y(z)+.025);high=form.deck(z)-.018
            if low>=high:continue
            levels=sorted(set(samples(low,high,6)+[lerp(.21,form.deck(z),t) for t in (.16,.30,.42,.72,.86)
                                                 if low<lerp(.21,form.deck(z),t)<high]))
            points=[(side*(form.side_x(y,z)+.010),y,form.position_z(form.side_x(y,z),z,y)) for y in levels]
            mesh.ribbon(points,.0045,car['paint']+'_gap',(side,0,0))
        for z in (split-.23,fb-.44):
            y=form.deck(z)-.105;x=side*(form.side_x(y,z)+.009)
            mesh.box((x,y,z),(.018,.023,.125),'rubber' if car['style'] in ('hikari','vahren') else 'chrome')
        mirror(mesh,form,side,fb-.18,form.deck(fb-.18)+.06)
    for base,top,end in [(bb,bt,-1),(fb,ft,1)]:
        def screen(u,t,offset=0):
            x=lerp(form.width(base)*.90,roof_width(top),t)*u
            y=lerp(form.top(u,base)[1],roof_height(u,top),t)
            z=lerp(base,top,t)+end*.018*(1-u*u)*math.sin(math.pi*t)
            return (x,y+offset,z+end*offset)
        for low,high in [(0,.065),(.93,1)]:
            metal(screen,samples(-.945,.945,8),[low,high],car['paint'],(0,1,end))
        for left,right in [(-1,-.945),(.945,1)]:
            metal(screen,[left,right],samples(0,1,3),car['paint'],(0,1,end))
        glass=lambda u,t:screen(u,t,.012)
        glazing.surface(glass,samples(-.945,.945,8),[.065,.42,.72,.93],'window_glass',(0,1,end),
                     uv=lambda u,t:glass_uv(t))
        frame(mesh,glass,(-.945,.945,.065,.93),.018,'rubber',(0,1,end))
        if end==1:
            for a,b in [(-.75,-.18),(.10,.66)]:
                mesh.ribbon([glass(a,.10),glass(b,.135)],.012,'rubber',(0,1,1))
        else:
            x,y,z=glass(0,.115)
            mesh.box((x,y+.009,z-.011),(.14,.026,.025),'red')
    return glazing


def mirror(mesh,form,side,z,y):
    x=side*(form.width(z)+.04)
    mesh.beam((side*(form.width(z)*.92),y-.018,z),(x,y,z-.025),.025,'rubber')
    outline=[(-.07,-.026),(-.055,-.038),(.07,-.029),(.087,.013),(.064,.034),(-.06,.029)]
    rings=[[(x+side*px,y+py,z+depth) for px,py in outline] for depth in (-.012,-.115)]
    mesh.face(rings[0],form.car['paint'],(0,0,1))
    for i in range(len(outline)):
        j=(i+1)%len(outline)
        mesh.face([rings[0][i],rings[1][i],rings[1][j],rings[0][j]],form.car['paint'],
                  (side*(outline[i][0]+outline[j][0]),outline[i][1]+outline[j][1],0))
    glass=[(x+side*px*.82,y+py*.77,z-.117) for px,py in outline]
    mesh.face(glass,'glasslight',(0,0,-1))
