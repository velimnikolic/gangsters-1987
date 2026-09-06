"""Curved roof crowns, varied pillar spacing and wraparound glazing."""
import math
from bodywork import lerp,samples


def build_cabin(mesh, form):
    car=form.car
    shape=form.shape
    bb,bt,ft,fb=car['cabin']
    roof,paint=car['roof'],car['paint']
    def roof_width(z):
        t=max(0,min(1,(z-bt)/(ft-bt)))
        return form.w-lerp(shape['roof_rear_inset'],shape['roof_front_inset'],t)
    def roof_height(u,z):
        t=max(0,min(1,(z-bt)/(ft-bt)))
        return car['height']-shape['roof_crown']*u*u-shape['roof_end_drop']*(2*t-1)**4
    def roof_point(u,z):
        return (u*roof_width(z),roof_height(u,z),z)
    mesh.surface(roof_point,samples(-1,1,16),samples(bt,ft,16),roof,(0,1,0))
    def side_point(side,z,t,offset=0):
        upper=roof_height(1,z)
        lower=form.deck(z)
        x=lerp(form.width(z)*.915,roof_width(z),t)+.012*math.sin(math.pi*t)
        return (side*(x+offset),lerp(lower,upper,t),z)
    for side in (-1,1):
        def side_panel(s,t):
            z=lerp(lerp(bb,bt,t),lerp(fb,ft,t),s)
            return side_point(side,z,t)
        def pillar_color(s,t):
            return roof if (s < .23 or t > .90) else paint
        mesh.surface(side_panel,samples(0,1,18),samples(0,1,10),pillar_color,(side,0,0))
        split=shape['pillar']
        # A separate rear quarter-light on the executive and everyday models.
        windows=[('rear',split-.045),('front',split+.045)]
        for where,seam in windows:
            def window(u,t):
                back=lerp(bb,bt,t)+shape['rear_pillar'] if where=='rear' else seam
                front=seam if where=='rear' else lerp(fb,ft,t)-.09
                return side_point(side,lerp(back,front,u),t,.016)
            glass_color=lambda u,t:'glasssky' if .80<t<.83 and .08<u<.92 else 'glass'
            mesh.surface(window,samples(0,1,10),sorted(set(samples(.12,.88,8)+[.80,.83])),glass_color,(side,0,0))
            outline(mesh,window,(0,1,.12,.88),.014,'rubber')
            if car['style'] not in ('hikari','bayside'):
                outline(mesh,window,(0,1,.105,.905),.009,'chrome')
            if where=='rear' and car['style'] in ('regent','kronen','bayside'):
                for a,b in zip(samples(.12,.88,10),samples(.12,.88,10)[1:]):
                    mesh.beam(window(.28,a),window(.28,b),.023,'rubber')
        # Door cuts follow the crowned side panel, including its changing width.
        for z in (bb+.1,split,fb-.16):
            start=max(.38,form.arch_y(z)+.025)
            end=form.deck(z)-.018
            if start>=end:
                continue
            points=[(side*(form.side_x(y,z)+.005),y,z) for y in samples(start,end,9)]
            for a,b in zip(points,points[1:]):
                mesh.beam(a,b,.007,'seam')
        for z in (split-.24,fb-.48):
            y=form.deck(z)-.12
            x=side*(form.side_x(y,z)+.018)
            mesh.box((x,y,z),(.025,.027,.14),'rubber' if car['style']=='hikari' else 'chrome')
        # Smaller integrated mirrors, tapered at the stalk, no oversized cubes.
        z=fb-.21
        y=form.deck(z)+.075
        x=side*(form.width(z)+.055)
        mesh.beam((side*(form.width(z)*.92),y-.035,z),(x,y,z-.03),.032,'rubber')
        mesh.box((x,y,z-.07),(.14,.085,.13),paint)
        mesh.box((x,y,z-.138),(.11,.058,.008),'glasslight')
    for base,top,end in [(bb,bt,-1),(fb,ft,1)]:
        def windscreen(u,t,offset=0):
            x=lerp(form.width(base)*.915,roof_width(top),t)*u
            y=lerp(form.deck(base)+form.shape['crown']*(1-u*u),roof_height(u,top),t)
            z=lerp(base,top,t)+end*.04*(1-u*u)*math.sin(math.pi*t)
            return (x,y+offset,z+end*offset)
        mesh.surface(windscreen,samples(-1,1,16),samples(0,1,10),paint,(0,1,end))
        glass=lambda u,t:windscreen(u,t,.01)
        mesh.surface(glass,samples(-.92,.92,16),samples(.1,.89,9),'glass',(0,1,end))
        outline(mesh,glass,(-.93,.93,.09,.90),.014,'rubber')
        if car['style'] not in ('hikari','bayside'):
            outline(mesh,glass,(-.945,.945,.075,.915),.009,'chrome')
        if end==1:
            for a,b in [(-.72,-.18),(.08,.63)]:
                mesh.beam(glass(a,.13),glass(b,.17),.013,'rubber')
        else:
            p=glass(0,.16)
            mesh.box((p[0],p[1]+.02,p[2]-.013),(.17,.04,.045),'red')


def outline(mesh, point, limits, width, color):
    left,right,bottom,top=limits
    edges=[[(u,bottom) for u in samples(left,right,8)],
           [(u,top) for u in samples(left,right,8)],
           [(left,t) for t in samples(bottom,top,5)],
           [(right,t) for t in samples(bottom,top,5)]]
    for edge in edges:
        for a,b in zip(edge,edge[1:]):
            mesh.beam(point(*a),point(*b),width,color)
