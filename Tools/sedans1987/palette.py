"""Shared sRGB swatches for the car meshes and the Miami review forecourt."""
COLORS = {
    'ivory': '#e8dec3', 'navy': '#253d59', 'green': '#244d40',
    'champagne': '#ba9b6b', 'cream': '#eadfc7', 'wine': '#722e3e',
    'maroon': '#45212e', 'blue': '#719fb6', 'sand': '#c5b191',
    'rubber': '#1e242a', 'tireface': '#2b3036', 'chrome': '#c2d0d0',
    'wheelshade': '#485057', 'glass': '#34454d', 'glasslight': '#899d9f',
    'glasssky': '#415963',
    'silver': '#a9b2b7',
    'seam': '#303439', 'cladding': '#667781', 'gold': '#d5b671',
    'amber': '#d88a3c', 'red': '#a83436', 'plate': '#f1e9cf',
    'headlight': '#e6e4cf', 'asphalt': '#697779', 'concrete': '#c1bca7',
    'lamp_front': '#bac7c6', 'lamp_tail': '#9e2024', 'lamp_marker': '#bf7025',
    'tile': '#dfd2b9', 'coral': '#bf8278', 'teal': '#407a78',
    'wall': '#ded5b9', 'soil': '#584d3d', 'trunk': '#8b7755',
    'leaf': '#4e7250', 'leaflight': '#6d8959', 'line': '#e1cf9a',
}

# Surface identity and glass reflections stay in the shared atlas.
PAINTS=('ivory','navy','green','champagne','wine','blue','sand','silver')
for paint in PAINTS:
    rgb=[int(COLORS[paint][i:i+2],16) for i in (1,3,5)]
    COLORS[paint+'_gap']='#'+''.join(f'{round(c*.60):02x}' for c in rgb)


def glass_uv(height):
    return ((list(COLORS).index('glass')+.5)/len(COLORS),.06+.88*height)
