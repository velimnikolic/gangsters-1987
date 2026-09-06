"""Readable window openings and slim pillars, with surface trim and atlas glass."""
import math
from bodywork import lerp,samples,profile
from palette import glass_uv
from geometry import Mesh,unit,sub
from window_frames import aperture
from roof_skin import RoofSkin
from screen_skin import sampled,cowl

SIDE_BOTTOM,SIDE_TOP=.11,.94
POST_HALF,FRONT_POST=.035,.095
SCREEN_EDGE,SCREEN_BOTTOM,SCREEN_TOP=.915,.095,.95


def frame(mesh,point,limits,width,color,outward):
    left,right,bottom,top=limits
    inward=tuple(-v*.028 for v in unit(outward))
    centre=point((left+right)/2,(bottom+top)/2)
    for coords in [[(u,bottom) for u in samples(left,right,4)],
                   [(u,top) for u in samples(left,right,4)],
                   [(left,t) for t in (bottom,top)],[(right,t) for t in (bottom,top)]]:
        edge=[point(u,t) for u,t in coords]
        mesh.ribbon(edge,width,color,outward)
        for a,b in zip(edge,edge[1:]):
            # The reveal is real thickness, facing into the window opening.
            inner_a=tuple(p+d for p,d in zip(a,inward))
            inner_b=tuple(p+d for p,d in zip(b,inward))
            mesh.face([a,b,inner_b,inner_a],'interior_trim',sub(centre,a))


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
    def raw_width(z):
        t=max(0,min(1,(z-bt)/(ft-bt)))
        return form.w-lerp(s['roof_rear_inset'],s['roof_front_inset'],t)-.035*(2*t-1)**4
    def raw_height(u,z):
        t=max(0,min(1,(z-bt)/(ft-bt)))
        roll=profile([(0,0),(.62,.008),(.84,.022),(1,.045)],abs(u))
        return car['height']-roll-s['roof_crown']*u*u*.3-s['roof_end_drop']*(2*t-1)**4-.018*abs(u)**3*(2*t-1)**4
    roof=RoofSkin(raw_width,raw_height,bt,ft,[-1,-SCREEN_EDGE,-.84,-.62,0,.62,.84,SCREEN_EDGE,1])
    roof_width,roof_height,roof_point=roof.width,roof.height,roof.point
    mesh.surface(roof_point,roof.us,roof.zs,car['roof'],(0,1,0),smooth=False)
    mesh.face([(x,y-.026,z) for x,y,z in [roof_point(-1,bt),roof_point(1,bt),roof_point(1,ft),roof_point(-1,ft)]],
              'upholstery',(0,-1,0))
    def side_point(side,z,t,offset=0):
        x=lerp(form.width(z)*.90,roof_width(z),t)+.012*math.sin(math.pi*t)
        return (side*(x+offset),lerp(form.deck(z),roof_height(1,z),t),
                lerp(form.position_z(x,z),z,t))
    split=s['pillar']
    def rear_mass(t):
        # Sporting saloons turn the rear lower corner forward into the door.
        kick=.085 if car['style']=='vahren' else .040 if car['style'] in ('kronen','hikari') else 0
        return s['rear_pillar']+kick*max(0,(.40-t)/(.40-SIDE_BOTTOM))

    for side in (-1,1):
        outward=(side,.25,0)
        def panel(u,t):
            z=lerp(lerp(bb,bt,t),lerp(fb,ft,t),u)
            return side_point(side,z,t)
        rail_stations={SIDE_BOTTOM:set(samples(0,1,6)),SIDE_TOP:set(samples(0,1,6))}
        for kind in ('rear','middle','front'):
            def pillar(u,t):
                if kind=='rear':a=lerp(bb,bt,t);b=a+rear_mass(t)
                elif kind=='front':b=lerp(fb,ft,t);a=b-FRONT_POST
                else:a,b=split-POST_HALF,split+POST_HALF
                return side_point(side,lerp(a,b,u),t)
            metal(pillar,[0,1],samples(SIDE_BOTTOM,SIDE_TOP,3),'rubber' if kind=='middle' else car['roof'],outward)
        # Large clean apertures: the C-pillar has a deliberate mass, the front
        # pillar is slim, and the B-pillar is dark instead of a double chrome tube.
        for where in ('rear','front'):
            def window_z(u,t):
                back=lerp(bb,bt,t)+rear_mass(t) if where=='rear' else split+POST_HALF
                front=split-POST_HALF if where=='rear' else lerp(fb,ft,t)-FRONT_POST
                return lerp(back,front,u)
            def window(u,t):return side_point(side,window_z(u,t),t)
            def window_aperture(limits,width,**kwargs):
                outline=aperture(mesh,glazing,window,limits,width,outward,car['roof'],glass_uv,**kwargs)
                for u,t in outline:
                    for edge in rail_stations:
                        if abs(t-edge)<1e-8:
                            a,b=lerp(bb,bt,t),lerp(fb,ft,t)
                            rail_stations[edge].add((window_z(u,t)-a)/(b-a))
            if where=='front' and car.get('hatchback'):
                window_aperture((0,.76,SIDE_BOTTOM,SIDE_TOP),.030,corner=(.008,.008,.008,.008),along=samples(SIDE_BOTTOM,SIDE_TOP,3))
                window_aperture((.79,1,SIDE_BOTTOM,SIDE_TOP),.028,corner=(.035,.008,.008,.012),along=samples(SIDE_BOTTOM,SIDE_TOP,3))
                metal(window,[.76,.79],samples(SIDE_BOTTOM,SIDE_TOP,3),'rubber',outward)
            elif where=='rear' and car['style'] in ('regent','kronen','vahren','bayside'):
                # A fixed quarterlight follows the C pillar; the main door pane
                # finishes square against the B pillar, unlike the sloping front.
                q=.27 if car['style']!='regent' else .22
                window_aperture((0,q-.012,SIDE_BOTTOM,SIDE_TOP),.030,
                         corner=(.008,.055,.065,.008),along=samples(SIDE_BOTTOM,SIDE_TOP,3))
                window_aperture((q+.012,1,SIDE_BOTTOM,SIDE_TOP),.030,
                         corner=(.008,.008,.008,.008),along=samples(SIDE_BOTTOM,SIDE_TOP,3))
                metal(window,[q-.012,q+.012],[SIDE_BOTTOM,SIDE_TOP],'rubber',outward)
            else:
                corners=(.055,.008,.008,.012) if where=='front' else (.008,.060,.045,.008)
                window_aperture((0,1,SIDE_BOTTOM,SIDE_TOP),.035,corner=corners,along=samples(SIDE_BOTTOM,SIDE_TOP,3))
            if car['style'] in ('regent','kronen','calder','monarch','vahren'):
                # Thin bright trim runs along the outside of the complete glazing.
                for t in (SIDE_BOTTOM-.018,):
                    mesh.ribbon([window(u,t) for u in samples(0,1,4)],.011,'chrome',outward)
        # The rails use every glazing/corner boundary station. Their upper edge
        # also samples the existing roof skin, closing both sides of the strip.
        for edge,outer in [(SIDE_BOTTOM,0),(SIDE_TOP,1)]:
            stations=sorted(rail_stations[edge])
            metal(panel,stations,sorted([edge,outer]),car['roof'],outward)
        mesh.ribbon([side_point(side,split,t,.016) for t in (SIDE_BOTTOM-.015,SIDE_TOP+.015)],POST_HALF*1.9,'rubber',outward)
        # Door seams are thin strips following the metal, not protruding bars.
        for z in ((split,fb-.13) if car.get('hatchback') else (bb+.07,split,fb-.13)):
            low=max(.27,form.arch_y(z)+.025);high=form.deck(z)-.018
            if low>=high:continue
            levels=sorted(set(samples(low,high,6)+[lerp(.21,form.deck(z),t) for t in (.16,.30,.42,.72,.86)
                                                 if low<lerp(.21,form.deck(z),t)<high]))
            points=[(side*(form.side_x(y,z)+.010),y,form.position_z(form.side_x(y,z),z,y)) for y in levels]
            mesh.ribbon(points,.0045,car['paint']+'_gap',(side,0,0))
        # Both doors are front-hinged (+Z): the latch/handle belongs at their
        # lower-Z trailing edge, just ahead of the C- and B-pillar door cuts.
        for trailing,leading in ([(split,fb-.13)] if car.get('hatchback') else [(bb+.07,split),(split,fb-.13)]):
            z=trailing+min(.17,(leading-trailing)*.18)
            y=form.deck(z)-.105;x=side*(form.side_x(y,z)+.009)
            mesh.box((x,y,z),(.018,.023,.125),'rubber' if car['style'] in ('hikari','vahren','mica') else 'chrome')
        mirror(mesh,form,side,fb-.18,form.deck(fb-.18)+.06)
    for base,top,end in [(bb,bt,-1),(fb,ft,1)]:
        # Screen and side pillars share their actual sampled outside edge.
        side_ts=[0]+samples(SIDE_BOTTOM,SIDE_TOP,3)+[1]
        def boundary(t):
            return sampled(lambda v,_:side_point(1,lerp(base,top,v),v),side_ts,t,0)
        def raw_screen(u,t):
            x,edge_y,edge_z=boundary(t)
            crown=lerp(form.top(u,base)[1]-form.top(1,base)[1],
                       roof_height(u,top)-roof_height(1,top),t)
            return x*u,edge_y+crown,edge_z+end*.018*(1-u*u)*math.sin(math.pi*t)
        screen=lambda u,t:sampled(raw_screen,roof.us,u,t)
        cowl(metal,form,screen,base,SCREEN_BOTTOM,roof.us,car['paint'],(0,1,end))
        metal(screen,[u for u in roof.us if -SCREEN_EDGE<=u<=SCREEN_EDGE],[SCREEN_TOP,1],car['paint'],(0,1,end))
        for left,right in [(-1,-SCREEN_EDGE),(SCREEN_EDGE,1)]:
            metal(screen,[left,right],side_ts[1:],car['paint'],(0,1,end))
        glass=lambda u,t:screen(u,t)
        aperture(mesh,glazing,glass,(-SCREEN_EDGE,SCREEN_EDGE,SCREEN_BOTTOM,SCREEN_TOP),.035,
                 (0,1,end),car['paint'],glass_uv,corner=.050,across=roof.us,along=side_ts[1:-1])
        if end==1:
            for a,b in [(-.75,-.18),(.10,.66)]:
                mesh.ribbon([glass(a,.10),glass(b,.135)],.012,'rubber',(0,1,1))
        else:
            x,y,z=glass(0,.115)
            mesh.box((x,y+.009,z-.011),(.14,.026,.025),'lamp_tail')
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
