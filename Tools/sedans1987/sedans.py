"""Assemble each fictional sedan from its authored coachwork and period details."""
from geometry import Mesh
from bodywork import Coachwork
from cabins import build_cabin
from fascias import build_fascias
from wheels import build_wheels


def build_car(car):
    body=Mesh(car['id']+'_Body')
    form=Coachwork(car)
    form.shell(body)
    build_cabin(body,form)
    build_fascias(body,form)
    trim='rubber' if car['style']=='hikari' else 'chrome'
    form.belt_trim(body,.17,.022 if trim=='rubber' else .012,trim)
    if car['style'] in ('regent','calder','monarch'):
        form.belt_trim(body,.065,.005,'gold')
    # A discreet aerial, fuel flap and exhaust finish the physical model.
    z=-form.end+.48
    y=form.deck(z)
    x=form.width(z)*.77
    body.beam((x,y,z),(x,y+.31,z),.006,'chrome')
    x=form.side_x(y-.17,z)+.006
    body.box((x,y-.17,z),(.009,.12,.16),'seam')
    body.box((x+.006,y-.17,z),(.009,.106,.146),car['paint'])
    body.box((-.46,.29,-form.end+.055),(.074,.055,.19),'rubber')
    return body,build_wheels(form)
