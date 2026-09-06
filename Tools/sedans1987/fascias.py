"""Distinct faces and tails, fitted to each curved body envelope."""
import math
from bodywork import samples,lerp
from lenses import round_lamp
from fascia_skin import surround,surface,insert


def patch(mesh,form,end,x,y,width,height,color,depth=.04,radius=.025):
    radius=min(radius,width*.2,height*.4)
    def panel(u,t):
        dx=u*width/2
        corner=max(0,abs(dx)-(width/2-radius))
        half=height/2-radius+math.sqrt(max(0,radius*radius-corner*corner))
        px=x+dx
        py=y+lerp(-half,half,t)
        return (px,py,form.skin_z(px,end,py)+end*depth)
    # Clip a single convex outline into the actual cap facets. A second,
    # unrelated quad grid would bridge creases and expose painted triangles.
    outline=[]
    for sx,sy,angle in [(1,1,0),(-1,1,90),(-1,-1,180),(1,-1,270)]:
        for i in range(3):
            a=math.radians(angle+i*45)
            outline.append((x+sx*(width/2-radius)+radius*math.cos(a),
                            y+sy*(height/2-radius)+radius*math.sin(a)))
    insert(mesh,form,outline,color,depth,end)


def grille(mesh,form,width,height,y,vertical=False):
    surround(mesh,form,0,y,width,height)
    patch(mesh,form,1,0,y,width,height,'rubber',.008,radius=.002)
    if vertical:
        for x in samples(-width/2+.028,width/2-.028,13):
            patch(mesh,form,1,x,y,.015,height-.025,'chrome',.017,radius=.004)
    else:
        for dy in samples(-height/2+.026,height/2-.026,4):
            patch(mesh,form,1,0,y+dy,width-.035,.012,'chrome',.017,radius=.003)


def front(mesh,form):
    car=form.car
    style=car['style']
    width=form.width(form.end)
    level=form.deck(form.end)-.125
    if style=='vahren':
        level=form.deck(form.end)-.093
        # Slim bonnet lip, full-width recessed mask, shallow four-lamp face.
        patch(mesh,form,1,0,level-.022,1.43,.226,'rubber',.003,radius=.012)
        for side in (-1,1):
            surround(mesh,form,side*.081,level-.024,.112,.144,rim=.012)
            patch(mesh,form,1,side*.081,level-.024,.112,.144,'rubber',.014,radius=.004)
            for x in (.405,.595):
                round_lamp(mesh,form,side*x,level,.076)
            for offset in (-.035,0,.035):
                patch(mesh,form,1,side*.081+offset,level-.024,.005,.13,'chrome',.029,radius=.001)
            patch(mesh,form,1,side*.57,.463,.225,.051,'lamp_marker',.112,radius=.008)
        patch(mesh,form,1,0,.31,.81,.074,'rubber',.02,radius=.007)
        for side in (-1,1):
            patch(mesh,form,1,side*.56,.315,.18,.068,'rubber',.027,radius=.009)
        return [(-.595,level,form.skin_z(-.595,1,level)+.037),(.595,level,form.skin_z(.595,1,level)+.037)]
    layouts={
        'regent':(.59,.40),'kronen':(.67,.255),'albion':(.69,.14),
        'calder':(.56,.405),'monarch':(1.05,.23),'bayside':(.94,.16),'hikari':(.67,.105),
    }
    gw,gh=layouts[style]
    grille_y=min(level,form.deck(form.end)-gh/2-.028)
    grille(mesh,form,gw,gh,grille_y,style in ('regent','calder','monarch'))
    anchors=[]
    for side in (-1,1):
        if style=='albion':
            for x,r in ((.66,.090),(.44,.077)):
                round_lamp(mesh,form,side*x,level+.012,r)
            anchors.append((side*.66,level+.012,form.skin_z(side*.66,1,level+.012)+.038))
            patch(mesh,form,1,side*.64,.405,.32,.055,'lamp_marker',.10)
        elif style=='calder':
            # Stacked sealed-beam lamps and tall outboard corner lenses.
            for dy in (-.10,.01):
                patch(mesh,form,1,side*.62,level+dy,.31,.102,'chrome',.025,radius=.010)
                patch(mesh,form,1,side*.62,level+dy,.293,.082,'lamp_front',.031,radius=.007)
            anchors.append((side*.62,level+.01,form.skin_z(side*.62,1,level+.01)+.049))
        else:
            lw={'regent':.43,'kronen':.43,'monarch':.29,'bayside':.35,'hikari':.32}[style]
            lh={'regent':.175,'kronen':.19,'monarch':.205,'bayside':.18,'hikari':.135}[style]
            x=side*(gw/2+(width-gw/2)*.52)
            patch(mesh,form,1,x,level+.02,lw+.04,lh+.035,
                  'rubber' if style=='hikari' else 'chrome',.032)
            patch(mesh,form,1,x,level+.02,lw,lh,'lamp_front',.043)
            anchors.append((x,level+.02,form.skin_z(x,1,level+.02)+.062))
            if style=='regent':
                patch(mesh,form,1,x,level+.02,.022,lh+.02,'chrome',.05,radius=.005)
            if style=='kronen':
                patch(mesh,form,1,x+side*.10,level+.02,.013,lh,'glasslight',.05,radius=.003)
        if style!='albion':
            patch(mesh,form,1,side*(width-.035),level,.055,.16,'lamp_marker',.05,radius=.012)
    if style in ('regent','kronen','calder'):
        y=form.deck(form.end-.19)+form.shape['crown']
        mesh.beam((0,y,form.end-.19),(0,y+.078,form.end-.19),.012,'chrome')
        mesh.box((0,y+.082,form.end-.19),(.033,.037,.016),'gold' if style=='regent' else 'chrome')
    return anchors


def rear(mesh,form):
    style=form.car['style']
    width=form.width(-form.end)
    y=form.deck(-form.end)-.205
    if style=='monarch':
        patch(mesh,form,-1,0,y+.02,1.63,.13,'chrome',.03)
        patch(mesh,form,-1,0,y+.02,1.56,.082,'lamp_tail',.043)
        for x in (-.50,0,.50):
            patch(mesh,form,-1,x,y+.02,.025,.09,'chrome',.05,radius=.005)
    else:
        sizes={'regent':(.21,.27),'kronen':(.51,.17),'albion':(.48,.12),
               'calder':(.095,.39),'bayside':(.49,.16),'hikari':(.40,.14),'vahren':(.43,.16)}
        lw,lh=sizes[style]
        for side in (-1,1):
            x=side*(width-lw/2-.045)
            patch(mesh,form,-1,x,y,lw+.035,lh+.035,'chrome',.025,radius=.02)
            patch(mesh,form,-1,x,y,lw,lh,'lamp_tail',.04,radius=.02)
            if style=='kronen':
                for dy in (-.05,0,.05):
                    patch(mesh,form,-1,x,y+dy,lw,.012,'seam',.048,radius=.002)
            elif style=='regent':
                patch(mesh,form,-1,x,y,.15,.06,'headlight',.048,radius=.008)
            elif style not in ('calder',):
                patch(mesh,form,-1,x-side*lw*.32,y,.075,lh*.82,'headlight',.048,radius=.012)
                if style in ('bayside','hikari','vahren'):
                    patch(mesh,form,-1,x+side*lw*.32,y,.10,lh*.88,'amber',.048,radius=.012)
    # Small period plate and sculpted recess, carrying no marque or modern logo.
    plate_y=.58
    patch(mesh,form,-1,0,plate_y,.38,.185,'rubber',.025)
    patch(mesh,form,-1,0,plate_y,.31,.135,'plate',.036)
    z=-form.end-.046
    mesh.cylinder((0,plate_y+.018,z),.021,.005,'amber',axis=2,sides=12)
    for x in (-.11,-.07,.07,.11):
        mesh.box((x,plate_y-.028,z),(.013,.020,.007),'green')


def build_fascias(mesh,form):
    for end in (-1,1):
        form.bumper(mesh,end,'rubber' if form.car['style']=='hikari' else 'chrome')
    anchors=front(mesh,form)
    rear(mesh,form)
    if form.car['style'] not in ('vahren','regent','calder'):
        patch(mesh,form,1,0,.31,.82,.060,'rubber',.024,radius=.005)
    return anchors
