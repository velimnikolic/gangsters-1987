"""Small flat-shaded mesh authoring primitives; +Z is the vehicle's nose."""
import math


def sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def unit(v):
    length = math.sqrt(sum(x*x for x in v))
    if length < 1e-10:
        raise ValueError("Degenerate face")
    return tuple(x / length for x in v)


class Mesh:
    def __init__(self, name):
        self.name, self.faces = name, []
        self.normals = {}
        self.uvs = {}

    def face(self, points, color, outward=None, normals=None, uvs=None):
        points = list(points)
        normal = unit(cross(sub(points[1], points[0]), sub(points[2], points[0])))
        if outward and sum(a*b for a, b in zip(normal, outward)) < 0:
            points.reverse()
            if normals is not None:
                normals = list(reversed(normals))
            if uvs is not None:
                uvs = list(reversed(uvs))
        if normals is not None:
            self.normals[len(self.faces)] = [unit(n) for n in normals]
        if uvs is not None:
            self.uvs[len(self.faces)] = list(uvs)
        self.faces.append((points, color))

    def surface(self, point, us, vs, color, outward, smooth=True, normal_at=None, uv=None):
        """Sample a curved panel with analytic-position finite-difference normals."""
        def normal(u, v):
            e = .0001
            if normal_at is not None:
                n=unit(normal_at(u,v))
            else:
                du = tuple(x/(2*e) for x in sub(point(u+e, v), point(u-e, v)))
                dv = tuple(x/(2*e) for x in sub(point(u, v+e), point(u, v-e)))
                n = unit(cross(du, dv))
            wanted = outward(point(u, v)) if callable(outward) else outward
            return tuple(-x for x in n) if sum(a*b for a,b in zip(n,wanted)) < 0 else n
        for u0, u1 in zip(us, us[1:]):
            for v0, v1 in zip(vs, vs[1:]):
                coords = [(u0,v0),(u1,v0),(u1,v1),(u0,v1)]
                pts = [point(u,v) for u,v in coords]
                wanted = outward(pts[0]) if callable(outward) else outward
                shade = color((u0+u1)/2,(v0+v1)/2) if callable(color) else color
                self.face(pts,shade,wanted,[normal(u,v) for u,v in coords] if smooth else None,
                          [uv(u,v) for u,v in coords] if uv else None)

    def ribbon(self, points, width, color, outward):
        """A surface strip for panel gaps/trim, not a stack of capped cuboids."""
        for a,b in zip(points,points[1:]):
            direction=unit(sub(b,a))
            n=outward(a) if callable(outward) else outward
            across=unit(cross(n,direction))
            pts=[tuple(p[i]+s*width*.5*across[i] for i in range(3))
                 for p,s in [(a,-1),(b,-1),(b,1),(a,1)]]
            self.face(pts,color,n)

    def box(self, center, size, color):
        x, y, z = center
        a, b, c = [v/2 for v in size]
        points = [(x+i*a, y+j*b, z+k*c) for i in (-1, 1)
                  for j in (-1, 1) for k in (-1, 1)]
        for ids, direction in [((0,1,3,2),(-1,0,0)), ((4,6,7,5),(1,0,0)),
                               ((0,4,5,1),(0,-1,0)), ((2,3,7,6),(0,1,0)),
                               ((0,2,6,4),(0,0,-1)), ((1,5,7,3),(0,0,1))]:
            self.face([points[i] for i in ids], color, direction)

    def beam(self, a, b, width, color):
        direction = unit(sub(b, a))
        across = unit(cross(direction, (0,1,0) if abs(direction[1]) < .95 else (1,0,0)))
        up = cross(direction, across)
        rings = [[tuple(p[i]+width*.5*(j*across[i]+k*up[i]) for i in range(3))
                  for j,k in [(-1,-1),(1,-1),(1,1),(-1,1)]] for p in (a,b)]
        for i in range(4):
            j=(i+1)%4
            outward=tuple((rings[0][i][k]+rings[0][j][k])*.5-a[k] for k in range(3))
            self.face([rings[0][i],rings[0][j],rings[1][j],rings[1][i]],color,outward)
        self.face(rings[0],color,tuple(-v for v in direction))
        self.face(rings[1],color,direction)

    def cylinder(self, center, radius, depth, color, axis=0, sides=12):
        others = [i for i in range(3) if i != axis]
        rings=[]
        for offset in (-depth/2, depth/2):
            ring=[]
            for i in range(sides):
                p=list(center)
                p[axis]+=offset
                p[others[0]]+=radius*math.cos(i*math.tau/sides)
                p[others[1]]+=radius*math.sin(i*math.tau/sides)
                ring.append(tuple(p))
            rings.append(ring)
        for i in range(sides):
            j=(i+1)%sides
            outward=tuple((rings[0][i][k]+rings[0][j][k])*.5-center[k] if k != axis else 0 for k in range(3))
            self.face([rings[0][i],rings[0][j],rings[1][j],rings[1][i]],color,outward)
        for i,sign in enumerate((-1,1)):
            self.face(rings[i],color,tuple(sign if k==axis else 0 for k in range(3)))

    def add(self, other, position=(0,0,0), yaw=0):
        s,c=math.sin(math.radians(yaw)),math.cos(math.radians(yaw))
        for index,(points,color) in enumerate(other.faces):
            if index in other.uvs:
                self.uvs[len(self.faces)] = other.uvs[index]
            if index in other.normals:
                self.normals[len(self.faces)] = [(n[0]*c+n[2]*s,n[1],-n[0]*s+n[2]*c)
                                                for n in other.normals[index]]
            self.faces.append(([(p[0]*c+p[2]*s+position[0], p[1]+position[1],
                                 -p[0]*s+p[2]*c+position[2]) for p in points],color))

    @property
    def bounds(self):
        pts=[p for face,_ in self.faces for p in face]
        return [tuple(fn(p[i] for p in pts) for i in range(3)) for fn in (min,max)]

    def triangles(self):
        for face,color in self.faces:
            for i in range(1,len(face)-1):
                yield [face[0],face[i],face[i+1]],color
