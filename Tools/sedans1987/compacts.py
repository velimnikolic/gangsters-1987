"""Small three-door hatchback; shared shell, glazing, seating and lamp contracts."""
import copy
from geometry import Mesh
from bodywork import Coachwork,samples
from cabins import build_cabin
from interiors import build_interior
from wheels import build_wheels
from fascias import patch
from lenses import separate_lamps


def build_car(car):
    body=Mesh(car['id']+'_Body');form=Coachwork(car)
    form.shell(body);glass=build_cabin(body,form);build_interior(body,form)
    for end in (-1,1):form.bumper(body,end,'rubber')
    level=form.deck(form.end)-.094
    patch(body,form,1,0,level,.72,.13,'rubber',.006,radius=.007)
    for y in samples(level-.044,level+.044,4):
        patch(body,form,1,0,y,.66,.009,'wheelshade',.021,radius=.002)
    anchors=[]
    for side in (-1,1):
        x=side*.515
        patch(body,form,1,x,level,.255,.154,'rubber',.011,radius=.011)
        patch(body,form,1,x,level,.229,.122,'lamp_front',.024,radius=.007)
        anchors.append((x,level,form.skin_z(x,1,level)+.045))
        patch(body,form,1,side*.62,.435,.113,.046,'lamp_marker',.106,radius=.006)
        patch(body,form,-1,side*.54,.65,.265,.174,'rubber',.009,radius=.010)
        patch(body,form,-1,side*.51,.65,.185,.149,'lamp_tail',.025,radius=.006)
        patch(body,form,-1,side*.63,.65,.054,.149,'lamp_marker',.027,radius=.003)
        # One door per side. Rear access is through the hatch, no rear door latch.
        y=car['belt']-.09;z=-form.end+.29
        body.box((side*(form.side_x(y,z)+.009),y,z),(.015,.085,.12),'rubber')
    patch(body,form,-1,0,.56,.32,.125,'plate',.026,radius=.005)
    patch(body,form,-1,0,.73,.16,.025,'rubber',.022,radius=.004)
    # Tailgate seam and wiper are fitted to its sloping perimeter.
    body.ribbon([(x,car['belt']-.10,form.skin_z(x,-1,car['belt']-.10)-.008)
                 for x in samples(-.63,.63,6)],.005,'red_gap',(0,0,-1))
    form.belt_trim(body,.39,.028,'rubber')
    body.box((-.37,.245,-form.end+.035),(.065,.05,.17),'rubber')
    wheel_car=copy.deepcopy(car);wheel_car['style']='hikari'
    return body,build_wheels(Coachwork(wheel_car)),separate_lamps(body),anchors,glass
