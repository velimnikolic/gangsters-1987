"""Shaped sheet metal: tapered planforms, crowned decks and open wheel arches."""
import math
from geometry import cross,sub


def lerp(a, b, t):
    return a+(b-a)*t


def samples(a, b, count):
    return [lerp(a,b,i/count) for i in range(count+1)]


def interpolate(stations, value):
    for (a,x),(b,y) in zip(stations,stations[1:]):
        if value <= b:
            t=max(0,min(1,(value-a)/(b-a)))
            return lerp(x,y,t*t*(3-2*t))
    return stations[-1][1]


class Coachwork:
    def __init__(self, car):
        self.car=car
        self.shape=car['shape']
        self.w=car['width']/2
        self.end=car['length']/2
        self.front=car['wheelbase']/2+self.shape['axle_offset']
        self.rear=self.front-car['wheelbase']
        self.arch=car['radius']+.055

    def width(self, z):
        s=self.shape
        return self.w*interpolate([
            (-self.end,1-s['tail_taper']),(-self.end+.38,.98),
            (self.rear,1),(-.2,s['waist']),(self.front,1),
            (self.end-.45,.97),(self.end,1-s['nose_taper'])],z)

    def deck(self, z):
        bb,_,_,fb=self.car['cabin']
        return self.car['belt']+interpolate([
            (-self.end,-self.shape['tail_drop']),(bb,0),(fb,0),
            (self.end,-self.shape['hood_drop'])],z)

    def side_x(self, y, z):
        t=(y-.30)/(self.deck(z)-.30)
        return self.width(z)*interpolate([(0,.88),(.2,.96),(.62,1),(.85,.99),(1,.92)],t)

    def end_z(self, x, end):
        return end*(self.end-self.shape['corner']*(abs(x)/self.width(end*self.end))**6)

    def position_z(self, x, z):
        amount=max(0,(abs(z)-(self.end-.6))/.6)
        return z-math.copysign(self.shape['corner']*(abs(x)/self.width(z))**6*amount*amount,z)

    def top(self, u, z):
        x=u*self.width(z)*.92
        return (x,self.deck(z)+self.shape['crown']*(1-u*u),self.position_z(x,z))

    def arch_y(self, z):
        y=.32
        for axle in (self.rear,self.front):
            d=abs(z-axle)
            if d < self.arch:
                y=max(y,self.car['radius']+math.sqrt(max(0,self.arch*self.arch-d*d)))
        return y

    def side(self, side, z, t):
        y=lerp(self.arch_y(z),self.deck(z),t)
        x=side*self.side_x(y,z)
        return (x,y,self.position_z(x,z))

    def side_normal(self,side,z,t):
        # Surface orientation is independent of where the wheel opening clips it.
        # Differentiating the clipped parameterisation at an arch tip is unstable.
        y=lerp(self.arch_y(z),self.deck(z),t)
        def skin(y,z):
            x=side*self.side_x(y,z)
            return (x,y,self.position_z(x,z))
        e=.001
        return cross(sub(skin(y+e,z),skin(y-e,z)),sub(skin(y,z+e),skin(y,z-e)))

    def shell(self, mesh):
        car=self.car
        bb,_,_,fb=car['cabin']
        rings=set(samples(-self.end,self.end,32)+[bb,fb,-self.end+.6,self.end-.6])
        for axle in (self.rear,self.front):
            rings.update(axle+self.arch*math.cos(i*math.pi/18) for i in range(19))
            rings.update((axle-self.arch-.035,axle+self.arch+.035))
        rings=sorted({round(z,7) for z in rings})
        mesh.box((0,.29,0),(self.w*1.6,.09,car['wheelbase']+1.0),'rubber')
        for side in (-1,1):
            def paint(z,t):
                y=lerp(self.arch_y(z),self.deck(z),t)
                if car['style']=='kronen' and y<.60:
                    return 'cladding'
                if car['style']=='monarch' and y<.49:
                    return 'maroon'
                return car['paint']
            mesh.surface(lambda z,t:self.side(side,z,t),rings,[0,.12,.28,.52,.75,.9,1],
                         paint,(side,0,0),normal_at=lambda z,t:self.side_normal(side,z,t))
            # Recessed wheel wells and rolled sheet-metal lips frame the tires.
            for axle in (self.rear,self.front):
                previous=None
                for angle in samples(0,math.pi,24):
                    z=axle+self.arch*math.cos(angle)
                    y=car['radius']+self.arch*math.sin(angle)
                    x=self.side_x(y,z)
                    here=(side*(x+.008),y,z)
                    inner=(side*(x-.12),y,z)
                    if previous:
                        mesh.face([previous[0],here,inner,previous[1]],'rubber',(0,-1,0))
                        mesh.beam(previous[0],here,.021,car['paint'])
                    previous=(here,inner)
        for za,zb in [(-self.end,bb),(fb,self.end)]:
            mesh.surface(self.top,samples(-1,1,12),samples(za,zb,12),car['paint'],(0,1,0))
        for end in (-1,1):
            def cap(u,t):
                z=end*self.end
                y=lerp(.32,self.deck(z)+self.shape['crown']*(1-u*u),t)
                x=u*self.side_x(min(y,self.deck(z)),z)
                return (x,y,self.end_z(x,end))
            mesh.surface(cap,samples(-1,1,18),[0,.18,.5,.8,1],car['paint'],(0,0,end))

    def belt_trim(self, mesh, height, thickness, color):
        for side in (-1,1):
            points=[]
            for z in samples(-self.end+.35,self.end-.35,24):
                y=self.deck(z)-height
                if y < self.arch_y(z)+.025:
                    points=[]
                    continue
                point=(side*(self.side_x(y,z)+.012),y,self.position_z(self.side_x(y,z),z))
                if points:
                    mesh.beam(points[-1],point,thickness,color)
                points.append(point)

    def bumper(self, mesh, end, color):
        w=self.width(end*self.end)*1.03
        def panel(u,t):
            x=u*w
            y=lerp(.38,.56,t)
            z=self.end_z(x,end)+end*(.07+.018*math.sin(math.pi*t))
            return (x,y,z)
        mesh.surface(panel,samples(-1,1,24),[0,.12,.4,.8,1],color,(0,0,end))
        for height in (.385,.56):
            for xa,xb in zip(samples(-w,w,24),samples(-w,w,24)[1:]):
                a=(xa,height,self.end_z(xa,end)+end*.07)
                b=(xb,height,self.end_z(xb,end)+end*.07)
                mesh.beam(a,b,.025,color)
        # Rubber contact strip follows the curved bumper instead of a straight box.
        for xa,xb in zip(samples(-w,w,24),samples(-w,w,24)[1:]):
            mesh.beam((xa,.465,self.end_z(xa,end)+end*.09),
                      (xb,.465,self.end_z(xb,end)+end*.09),.035,'rubber')
