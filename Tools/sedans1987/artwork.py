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


def save_texture(name, image):
    path = f'{ua.ASSET}/Textures/{name}.png'
    stream = BytesIO()
    image.save(stream, format='PNG')
    ua.write(path, stream.getvalue())
    ua.texture_meta(path)
    return path


def make_palette():
    image = Image.new('RGB', (len(COLORS)*32, 32))
    draw = ImageDraw.Draw(image)
    for i, color in enumerate(COLORS.values()):
        draw.rectangle((i*32, 0, (i+1)*32-1, 31), fill=color)
    return save_texture('SedanPalette', image)


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
