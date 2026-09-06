"""Six review-only 1987 utilities sharing the sedan materials and rig contracts."""
import copy
from geometry import Mesh
from bodywork import Coachwork
from wheels import build_wheels
from lenses import separate_lamps
from utility_cabin import build_cabin
from utility_details import fascia,interior,trim,seat_layout
from armour import fit_armour,armoured_wheels,truck_bed,lightbar


def build_car(car):
    body=Mesh(car['id']+'_Body');form=Coachwork(car)
    form.shell(body)
    glass=build_cabin(body,form)
    interior(body,form)
    anchors=fascia(body,form)
    wheel_car=copy.deepcopy(car)
    wheel_car['style']={'trail':'hikari','ranger':'bayside','highland':'kronen',
                        'warden':'hikari','bastion':'bayside','voyager':'hikari'}[car['style']]
    wheels=armoured_wheels(form) if car['style']=='bastion' else build_wheels(Coachwork(wheel_car))
    trim(body,form,wheels)
    if car['style']=='bastion':
        fit_armour(body,form)
        if car.get('pickup'):truck_bed(body,form)
        anchors += lightbar(body,form)
        body.faces=[(points,'rubber' if color=='navy' else 'seam' if color=='navy_gap' else color)
                    for points,color in body.faces]
    lift=.11 if car['style'] not in ('warden','voyager') else .055
    def ground_clearance(y):return y+lift*max(0,min(1,(.62-y)/.41))
    body.faces=[([(x,ground_clearance(y),z) for x,y,z in points],color) for points,color in body.faces]
    return body,wheels,separate_lamps(body),anchors,glass
