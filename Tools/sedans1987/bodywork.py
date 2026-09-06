"""Shaped sheet metal: tapered planforms, crowned decks and open wheel arches."""
import math


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


def profile(stations,value):
    """Stamped creases keep their slope changes instead of smoothing them away."""
    for (a,x),(b,y) in zip(stations,stations[1:]):
        if value<=b:return lerp(x,y,max(0,min(1,(value-a)/(b-a))))
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
        self.cap_faces={}

    def cap_z(self,x,end,y):
        """Project a fitting onto the actual triangulated end panel."""
        for a,b,c,limits in self.cap_faces.get(end,[]):
            if not (limits[0]-1e-7<=x<=limits[1]+1e-7 and limits[2]-1e-7<=y<=limits[3]+1e-7):continue
            det=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
            if abs(det)<1e-12:continue
            u=((b[1]-c[1])*(x-c[0])+(c[0]-b[0])*(y-c[1]))/det
            v=((c[1]-a[1])*(x-c[0])+(a[0]-c[0])*(y-c[1]))/det
            if u>=-1e-7 and v>=-1e-7 and u+v<=1+1e-7:return u*a[2]+v*b[2]+(1-u-v)*c[2]
        return None

    def skin_z(self,x,end,y):
        z=self.cap_z(x,end,y)
        return self.end_z(x,end,y) if z is None else z

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
        t=(y-.21)/(self.deck(z)-.21)
        # Rolled sill, recessed lower door, broad face, raised shoulder, then
        # an explicit bevel into the deck. These stations stay level at arches.
        return self.width(z)*profile([(0,.81),(.16,.90),(.30,.96),(.42,.949),
                                     (.72,.986),(.86,1.012),(1,.90)],t)

    def end_z(self, x, end, y=None):
        tuck=0 if y is None else max(0,(.48-y)/.27)*.15
        return end*(self.end-self.shape['corner']*(abs(x)/self.width(end*self.end))**3-tuck)

    def position_z(self, x, z, y=None):
        # Spread the turn far enough that swept side stations cannot double back.
        amount=max(0,(abs(z)-(self.end-.9))/.9)
        tuck=0 if y is None else max(0,(.48-y)/.27)*.15
        return z-math.copysign((self.shape['corner']*(abs(x)/self.width(z))**3+tuck)*amount*amount,z)

    def top(self, u, z):
        x=u*self.width(z)*.90
        # A shallow centre crown, hood shut shoulder and raised fender land.
        crown=profile([(0,1),(.52,.70),(.72,.04),(.84,.44),(1,0)],abs(u))
        rolloff=lerp(.35,1,min(1,(self.end-abs(z))/.55))
        return (x,self.deck(z)+self.shape['crown']*crown*rolloff,self.position_z(x,z))

    def arch_y(self, z):
        y=.21
        for axle in (self.rear,self.front):
            d=abs(z-axle)
            if d < self.arch:
                y=max(y,self.car['radius']+math.sqrt(max(0,self.arch*self.arch-d*d)))
        return y

    def shell(self, mesh):
        car=self.car
        bb,_,_,fb=car['cabin']
        rings=set(samples(-self.end,self.end,12)+[bb,fb,-self.end+.9,self.end-.9])
        for axle in (self.rear,self.front):
            rings.update(axle+self.arch*math.cos(i*math.pi/12) for i in range(13))
            rings.update((axle-self.arch-.035,axle+self.arch+.035))
        rings=sorted({round(z,7) for z in rings})
        mesh.box((0,.19,0),(self.w*1.4,.10,car['wheelbase']+1.0),'rubber')
        for side in (-1,1):
            bands=[0,.16,.30,.42,.72,.86,1]
            for a,b in zip(rings,rings[1:]):
                for low,high in zip(bands,bands[1:]):
                    points=[]
                    for z,t in [(a,low),(b,low),(b,high),(a,high)]:
                        y=max(self.arch_y(z),lerp(.21,self.deck(z),t))
                        x=side*self.side_x(y,z)
                        p=(x,y,self.position_z(x,z,y))
                        if not points or p!=points[-1]:points.append(p)
                    if len(points)>1 and points[-1]==points[0]:points.pop()
                    if len(points)<3:continue
                    y=sum(p[1] for p in points)/len(points)
                    color='cladding' if car['style']=='kronen' and y<.60 else car['paint']
                    if car['style']=='monarch' and y<.49:color='maroon'
                    mesh.face(points,color,(side,0,0))
            # Recessed wheel wells and rolled sheet-metal lips frame the tires.
            for axle in (self.rear,self.front):
                previous=None
                for angle in samples(0,math.pi,12):
                    z=axle+self.arch*math.cos(angle)
                    y=car['radius']+self.arch*math.sin(angle)
                    x=self.side_x(y,z)
                    here=(side*(x+.008),y,self.position_z(x,z,y))
                    inner=(side*(x-.12),y,self.position_z(x,z,y))
                    if previous:
                        mesh.face([previous[0],here,inner,previous[1]],'rubber',(0,-1,0))
                        mesh.ribbon([previous[0],here],.020,car['paint'],(side,0,0))
                    previous=(here,inner)
        across=[-1,-.84,-.72,-.52,0,.52,.72,.84,1]
        for za,zb in [(-self.end,bb),(fb,self.end)]:
            spans=[[-1,-.84],[.84,1]] if car.get('pickup') and za<0 else [across]
            for span in spans:mesh.surface(self.top,span,samples(za,zb,5),car['paint'],(0,1,0),smooth=False)
        # A stamped bonnet panel between the fenders, with a millimetre-scale
        # shut line. The hood should read as sheet metal rather than a solid block.
        def seam_point(u,z):
            x,y,pz=self.top(u,z)
            return (x,y+.006,pz)
        for side in (-1,1):
            line=[seam_point(side*.72,z) for z in samples(fb+.045,self.end-.035,6)]
            mesh.ribbon(line,.003,car['paint']+'_gap',(0,1,0))
        line=[seam_point(u,self.end-.035) for u in samples(-.72,.72,6)]
        mesh.ribbon(line,.003,car['paint']+'_gap',(0,1,0))
        for end in (-1,1):
            def cap(u,t):
                z=end*self.end
                y=lerp(.21,self.top(u,z)[1],t)
                x=u*self.side_x(min(y,self.deck(z)),z)
                return (x,y,self.end_z(x,end,y))
            start=len(mesh.faces)
            mesh.surface(cap,sorted(set(across+[-.95,-.30,.30,.95])),[0,.16,.30,.42,.72,.86,1],
                         car['paint'],(0,0,end),smooth=False)
            self.cap_faces[end]=[]
            for points,_ in mesh.faces[start:]:
                for i in range(1,len(points)-1):
                    a,b,c=points[0],points[i],points[i+1]
                    self.cap_faces[end].append((a,b,c,(min(p[0] for p in (a,b,c)),max(p[0] for p in (a,b,c)),
                                                     min(p[1] for p in (a,b,c)),max(p[1] for p in (a,b,c)))))

    def belt_trim(self, mesh, height, thickness, color):
        for side in (-1,1):
            points=[]
            for z in samples(-self.end+.35,self.end-.35,12):
                y=self.deck(z)-height
                if y < self.arch_y(z)+.025:
                    points=[]
                    continue
                point=(side*(self.side_x(y,z)+.012),y,self.position_z(self.side_x(y,z),z,y))
                if points:
                    mesh.ribbon([points[-1],point],thickness,color,(side,0,0))
                points.append(point)

    def bumper(self, mesh, end, color):
        w=self.width(end*self.end)*1.03
        bottom,top=(.405,.525) if self.car['style']=='vahren' else (.38,.55)
        def panel(u,t):
            x=u*w
            y=lerp(bottom,top,t)
            z=self.end_z(x,end,y)+end*(.07+.018*math.sin(math.pi*t))
            return (x,y,z)
        mesh.surface(panel,samples(-1,1,12),[0,.16,.84,1],color,(0,0,end),smooth=False)
        # Rubber contact strip follows the curved bumper instead of a straight box.
        mesh.ribbon([(x,.465,self.end_z(x,end,.465)+end*.092) for x in samples(-w,w,12)],
                    .052,'rubber',(0,0,end))
