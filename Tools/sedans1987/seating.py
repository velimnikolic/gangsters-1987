"""Seat cushions, occupant roots and the cabin's usable head envelope."""
import math


def seat_roots(form):
    car=form.car;bb,bt,ft,fb=car['cabin']
    if car.get('utility'):
        top=.725 if car['style'] in ('warden','voyager') else .695
        front=fb-.92
        if car['style']=='warden':
            # Two cab seats and three riders on each inward-facing custody bench.
            return [(side*car['width']*.19,top-.43,front) for side in (-1,1)]+[
                (side*car['width']*.80*.34,.795-.43,z)
                for z in (-.40,-1.02,-1.64) for side in (-1,1)]
        rows=[front,max(front-.98,bt+.42)]
        if car['style']=='voyager':rows.append(front-1.96)
    else:
        top=min(.48,car['height']-.94)+.085
        front=form.shape['pillar']+.24
        rear=min(front-.68,bt+.37)
        rows=[front,rear]
    return [(side*car['width']*.19,top-.43,z) for z in rows for side in (-1,1)]


def cabin_planes(form):
    car=form.car;bb,bt,ft,fb=car['cabin'];h=car['height']-.075
    w=form.w-max(form.shape['roof_rear_inset'],form.shape['roof_front_inset'])-.04
    rear_y,front_y=form.deck(bb),form.deck(fb)
    planes=[(0,1,0,-(car['height']-.10)),(1,0,0,-w),(-1,0,0,-w),
            (0,bt-bb,-(h-rear_y),(h-rear_y)*bb-(bt-bb)*rear_y),
            (0,fb-ft,h-front_y,-(h-front_y)*fb-(fb-ft)*front_y)]
    return [tuple(v/math.sqrt(sum(n*n for n in p[:3])) for v in p) for p in planes]
