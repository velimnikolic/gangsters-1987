"""One sampled roof boundary shared by the roof, side rails and both screens."""
from bodywork import samples, profile


class RoofSkin:
    def __init__(self, width, height, back, front, across):
        self.us=across
        self.zs=samples(back,front,6)
        self.widths=[(z,width(z)) for z in self.zs]
        self.heights={u:[(z,height(u,z)) for z in self.zs] for u in across}

    def width(self,z):
        return profile(self.widths,z)

    def height(self,u,z):
        return profile([(p,profile(self.heights[p],z)) for p in self.us],u)

    def point(self,u,z):
        return u*self.width(z),self.height(u,z),z
