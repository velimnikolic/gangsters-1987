"""Distinct faces and tails, fitted to each curved body envelope."""
import math
from bodywork import samples,lerp


def patch(mesh,form,end,x,y,width,height,color,depth=.04,radius=.025):
    radius=min(radius,width*.2,height*.4)
    def panel(u,t):
        dx=u*width/2
        corner=max(0,abs(dx)-(width/2-radius))
        half=height/2-radius+math.sqrt(max(0,radius*radius-corner*corner))
        px=x+dx
        return (px,y+lerp(-half,half,t),form.end_z(px,end)+end*depth)
    mesh.surface(panel,samples(-1,1,8 if width>.25 else 2),[0,1],color,(0,0,end))


def grille(mesh,form,width,height,y,vertical=False):
    patch(mesh,form,1,0,y,width+.055,height+.045,'chrome',.025)
    patch(mesh,form,1,0,y,width,height,'rubber',.042)
    if vertical:
        for x in samples(-width/2+.028,width/2-.028,13):
            patch(mesh,form,1,x,y,.015,height-.025,'chrome',.049,radius=.004)
    else:
        for dy in samples(-height/2+.026,height/2-.026,4):
            patch(mesh,form,1,0,y+dy,width-.035,.012,'chrome',.049,radius=.003)


def front(mesh,form):
    car=form.car
    style=car['style']
    width=form.width(form.end)
    level=form.deck(form.end)-.205
    if style=='vahren':
        # A compact, upright twin-opening face with four independent round lamps.
        patch(mesh,form,1,0,level,1.38,.24,'rubber',.025,radius=.025)
        for side in (-1,1):
            patch(mesh,form,1,side*.095,level,.16,.22,'chrome',.042,radius=.025)
            patch(mesh,form,1,side*.095,level,.122,.188,'rubber',.052,radius=.02)
            for x in (.40,.64):
                z=form.end_z(side*x,1)
                mesh.cylinder((side*x,level+.006,z+.055),.114,.025,'chrome',axis=2,sides=24)
                mesh.cylinder((side*x,level+.006,z+.072),.095,.018,'headlight',axis=2,sides=24)
            for offset in (-.035,0,.035):
                patch(mesh,form,1,side*.095+offset,level,.009,.165,'chrome',.059,radius=.002)
            patch(mesh,form,1,side*.58,.39,.28,.065,'amber',.11,radius=.015)
        return
    layouts={
        'regent':(.59,.40),'kronen':(.67,.255),'albion':(.69,.14),
        'calder':(.56,.405),'monarch':(1.05,.23),'bayside':(.94,.16),'hikari':(.67,.105),
    }
    gw,gh=layouts[style]
    grille(mesh,form,gw,gh,level,style in ('regent','calder','monarch'))
    for side in (-1,1):
        if style=='albion':
            for x,r in ((.68,.125),(.42,.102)):
                z=form.end_z(side*x,1)
                mesh.cylinder((side*x,level+.028,z+.045),r+.022,.040,'chrome',axis=2,sides=24)
                mesh.cylinder((side*x,level+.028,z+.071),r,.020,'headlight',axis=2,sides=24)
            patch(mesh,form,1,side*.64,.405,.32,.055,'amber',.10)
        elif style=='calder':
            # Stacked sealed-beam lamps and tall outboard corner lenses.
            for dy in (-.068,.068):
                patch(mesh,form,1,side*.62,level+dy,.335,.116,'chrome',.04,radius=.016)
                patch(mesh,form,1,side*.62,level+dy,.293,.082,'headlight',.047,radius=.012)
        else:
            lw={'regent':.43,'kronen':.43,'monarch':.29,'bayside':.35,'hikari':.32}[style]
            lh={'regent':.175,'kronen':.19,'monarch':.205,'bayside':.18,'hikari':.135}[style]
            x=side*(gw/2+(width-gw/2)*.52)
            patch(mesh,form,1,x,level+.02,lw+.04,lh+.035,
                  'rubber' if style=='hikari' else 'chrome',.032)
            patch(mesh,form,1,x,level+.02,lw,lh,'headlight',.043)
            if style=='regent':
                patch(mesh,form,1,x,level+.02,.022,lh+.02,'chrome',.05,radius=.005)
            if style=='kronen':
                patch(mesh,form,1,x+side*.10,level+.02,.013,lh,'glasslight',.05,radius=.003)
        if style!='albion':
            patch(mesh,form,1,side*(width-.035),level,.055,.16,'amber',.05,radius=.012)
    if style in ('regent','kronen','calder'):
        y=form.deck(form.end-.19)+form.shape['crown']
        mesh.beam((0,y,form.end-.19),(0,y+.078,form.end-.19),.012,'chrome')
        mesh.box((0,y+.082,form.end-.19),(.033,.037,.016),'gold' if style=='regent' else 'chrome')


def rear(mesh,form):
    style=form.car['style']
    width=form.width(-form.end)
    y=form.deck(-form.end)-.205
    if style=='monarch':
        patch(mesh,form,-1,0,y+.02,1.63,.13,'chrome',.03)
        patch(mesh,form,-1,0,y+.02,1.56,.082,'red',.043)
        for x in (-.50,0,.50):
            patch(mesh,form,-1,x,y+.02,.025,.09,'chrome',.05,radius=.005)
    else:
        sizes={'regent':(.21,.27),'kronen':(.51,.17),'albion':(.48,.12),
               'calder':(.095,.39),'bayside':(.49,.16),'hikari':(.40,.14),'vahren':(.43,.16)}
        lw,lh=sizes[style]
        for side in (-1,1):
            x=side*(width-lw/2-.045)
            patch(mesh,form,-1,x,y,lw+.035,lh+.035,'chrome',.025,radius=.02)
            patch(mesh,form,-1,x,y,lw,lh,'red',.04,radius=.02)
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
    front(mesh,form)
    rear(mesh,form)
