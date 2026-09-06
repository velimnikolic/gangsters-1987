"""Glazing and its cowl meet the same sampled boundaries as the roof and hood."""
from bodywork import lerp


DECK_US=[-1,-.84,-.72,-.52,0,.52,.72,.84,1]


def sampled(point,us,u,t):
    for a,b in zip(us,us[1:]):
        if u<=b:
            amount=max(0,min(1,(u-a)/(b-a)))
            return tuple(lerp(x,y,amount) for x,y in zip(point(a,t),point(b,t)))
    return point(us[-1],t)


def cowl(metal,form,screen,base,low,us,color,outward):
    def band(u,t):
        # The hood has fender creases that do not belong in a windshield.
        # This short metal transition joins its real edge to the glass rail.
        lower=sampled(lambda x,_:form.top(x,base),DECK_US,u,0)
        upper=screen(u,low)
        return tuple(lerp(a,b,t) for a,b in zip(lower,upper))
    metal(band,sorted(set(DECK_US+us)),[0,1],color,outward)
