"""Rasterize crisp dealership signs and the single flat-color mesh palette."""
from io import BytesIO
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from palette import COLORS
import unity_assets as ua


def font_path():
    candidates = [
        '/System/Library/Fonts/Supplemental/Arial.ttf',
        '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',
        'C:/Windows/Fonts/arial.ttf',
    ]
    for path in candidates:
        if Path(path).exists():
            return Path(path)
    raise RuntimeError('Install Arial or DejaVu Sans to regenerate the sign textures.')


def save_texture(name, image, linear=False):
    path = f'{ua.ASSET}/Textures/{name}.png'
    stream = BytesIO()
    image.save(stream, format='PNG')
    ua.write(path, stream.getvalue())
    ua.texture_meta(path,srgb=not linear)
    return path


def make_palette(emission=False):
    image = Image.new('RGB', (len(COLORS)*8, 128))
    draw = ImageDraw.Draw(image)
    emit={'lamp_front':'#ffd973','lamp_tail':'#ff1710','lamp_marker':'#ff7514'}
    for i, (key, color) in enumerate(COLORS.items()):
        if emission:
            color=emit.get(key,'#000000')
        draw.rectangle((i*8, 0, (i+1)*8-1, 127), fill=color)
        if key=='glass' and not emission:
            stops=[(0,(48,67,81)),(.42,(89,113,123)),(.56,(41,57,65)),(1,(22,30,37))]
            for y in range(128):
                t=y/127
                for (a,ca),(b,cb) in zip(stops,stops[1:]):
                    if t<=b:
                        k=(t-a)/(b-a)
                        shade=tuple(round(x+(z-x)*k) for x,z in zip(ca,cb));break
                draw.line((i*8,y,(i+1)*8-1,y),fill=shade)
    return save_texture('SedanLampEmission' if emission else 'SedanPalette', image)


def make_surface():
    """Packed metal/smoothness atlas: one shared material, correct rubber/glass/paint."""
    image=Image.new('RGBA',(len(COLORS)*8,128));draw=ImageDraw.Draw(image)
    for i,key in enumerate(COLORS):
        metal,smooth=(25,153)
        if key in ('rubber','tireface','seam') or key.endswith('_gap'):metal,smooth=0,35
        elif key in ('chrome','wheelshade','gold'):metal,smooth=205,220
        elif key in ('glass','glasslight','glasssky'):metal,smooth=30,245
        elif key in ('cream','maroon','cladding'):metal,smooth=0,90
        draw.rectangle((i*8,0,(i+1)*8-1,127),fill=(metal,0,0,smooth))
    return save_texture('SedanSurface',image,linear=True)


def sign(name, lines, title=False):
    width, height = (2048, 512) if title else (1024, 256)
    image = Image.new('RGB', (width, height), '#233e43')
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, width-1, height-1), outline='#c6aa71', width=6)
    draw.rectangle((20, 18, 29, height-19), fill='#d3b879')
    y = 33 if title else 22
    for text, size, color in lines:
        font = ImageFont.truetype(str(font_path()), size)
        while draw.textlength(text, font=font) > width-110:
            size -= 1
            font = ImageFont.truetype(str(font_path()), size)
        draw.text((56, y), text, font=font, fill=color)
        y += size*1.3
    return save_texture(name, image)
