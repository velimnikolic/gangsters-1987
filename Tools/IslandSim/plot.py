#!/usr/bin/env python3
"""Optional Pillow plot of IslandSim's exported height/road data, never a Unity render."""
import csv
import json
from pathlib import Path
import sys
from PIL import Image, ImageDraw

path = Path(sys.argv[1])
rows = [tuple(map(float, (r['x'], r['z'], r['height']))) for r in csv.DictReader(path.open())]
xs, zs = sorted({r[0] for r in rows}), sorted({r[1] for r in rows})
xi, zi = {x:i for i,x in enumerate(xs)}, {z:i for i,z in enumerate(zs)}
image = Image.new('RGB', (len(xs),len(zs)))
stops = [(-28,(18,48,70)),(-2.65,(30,92,110)),(-2.64,(175,166,118)),
         (0.5,(161,163,112)),(12,(113,148,78)),(65,(73,115,65)),(200,(79,94,69)),(330,(139,142,130)),(420,(184,182,167))]
for x,z,h in rows:
    color = stops[-1][1]
    for (a,ca),(b,cb) in zip(stops,stops[1:]):
        if h <= b:
            t = max(0,min(1,(h-a)/(b-a)))
            color = tuple(round(v+(w-v)*t) for v,w in zip(ca,cb)); break
    image.putpixel((xi[x],len(zs)-1-zi[z]),color)
image = image.resize((1105,905),Image.Resampling.BILINEAR)
draw = ImageDraw.Draw(image)
def point(p): return ((p[0]-xs[0])/(xs[-1]-xs[0])*1104,904-(p[1]-zs[0])/(zs[-1]-zs[0])*904)
roads = json.loads(Path(str(path)+'.roads.json').read_text())
for line in roads['lines']: draw.line([point(p) for p in line],fill=(230,224,202),width=2)
draw.rectangle((12,12,760,39),fill=(13,24,28))
draw.text((20,20),'OFFLINE TERRAIN / ROAD MODEL - NOT A UNITY RENDER',fill='white')
output = path.with_suffix('.png'); image.save(output)
print(output)
