"""A static Miami forecourt, with shared DemoCamera controls for review."""
import json
import math
from geometry import Mesh
from palette import COLORS
from artwork import sign
import unity_assets as ua

SCENE = 'Assets/Scenes/Sedan1987Showroom.unity'
CAMERA = dict(pivot=(0, .8, 6.5), distance=54, pitch=34, yaw=180, fov=42)
REFERENCE = 'Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Sedan_01.prefab'
# Existing PalmCityDemo/Overview instances serialize this variant's root Transform.
REFERENCE_ROOT = 6977666779751257434
REFERENCE_BAY = 4


def placement(index,count=8):
    # +Z is towards the viewer: descending X reads left to right from this camera.
    return (((count-1)/2-index)*4.8, 0, 0), -18


def utility_placement(index):
    return ((2.5-index)*5.7,0,14),-18


def forecourt(count=8,utilities=0):
    mesh = Mesh('MiamiForecourt')
    extra=(count-7)*4.8
    extension=16 if utilities else 0
    mesh.box((0, -.16, extension/2), (41+extra, .3, 21+extension), 'concrete')
    mesh.box((0, -.025, .5+extension/2), (37+extra, .05, 13+extension), 'asphalt')
    mesh.box((0, .035, 7.1+extension), (39+extra, .07, 2), 'tile')
    mesh.box((0, .11, -6.5), (39+extra, .22, 2), 'tile')
    for x in (-19-extra/2, 19+extra/2):
        mesh.box((x, .095, .5+extension/2), (.25, .19, 13.5+extension), 'wall')
    for i in range(count+1):
        x = (count/2-i)*4.8
        mesh.box((x, .012, 0), (.055, .018, 7.1), 'line')
    for i in range(count):
        x = placement(i,count)[0][0]
        mesh.box((x, .014, -3.6), (3.9, .023, .055), 'line')
        mesh.box((x, .10, -3.45), (1.75, .20, .18), 'concrete')
    if utilities:
        for i in range(utilities+1):
            mesh.box(((3-i)*5.7,.012,14),(.055,.018,7.8),'line')
        for i in range(utilities):
            x=utility_placement(i)[0][0]
            mesh.box((x,.014,10.1),(4.8,.023,.055),'line')
            mesh.box((x,.10,10.25),(1.75,.20,.18),'concrete')
    # A low pastel dealership wall keeps every silhouette against a quiet backdrop.
    mesh.box((0, 1.04, -7.1), (39+extra, 2.08, .5), 'wall')
    mesh.box((0, .3, -6.8), (39+extra, .26, .12), 'coral')
    mesh.box((0, 1.78, -6.81), (39+extra, .1, .13), 'teal')
    mesh.box((0, 2.09, -7.1), (39.3+extra, .13, .8), 'coral')
    mesh.box((0, 3.5, -7.2), (12.2, 3.0, .7), 'teal')
    for side in (-1, 1):
        for step in range(3):
            mesh.box((side*(6.4+step*.4), 2.4-step*.2, -7.15), (.6, 1.4-step*.4, .7), 'coral')
    for x in range(-18, 19, 3):
        mesh.box((x, .077, 7.1+extension), (.016, .01, 2), 'concrete')
    for x in (-17.6-extra/2, 17.6+extra/2):
        mesh.box((x, .3, -4.7), (1.8, .6, 1.8), 'coral')
        mesh.box((x, .615, -4.7), (1.55, .025, 1.55), 'soil')
        palm(mesh, x, -4.7)
    return mesh


def palm(mesh, x, z):
    points = [(x, .6, z), (x+.15, 2.2, z), (x+.35, 3.7, z+.04), (x+.6, 5.3, z+.1)]
    for a, b in zip(points, points[1:]):
        mesh.beam(a, b, .24, 'trunk')
    crown = points[-1]
    for i in range(9):
        angle = i*math.tau/9
        direction = (math.cos(angle), 0, math.sin(angle))
        across = (-direction[2], 0, direction[0])
        rings = []
        for distance, y, width in [(0, 0, .04), (.8, .45, .38), (1.7, .1, .34), (2.3, -.5, .015)]:
            center = (crown[0]+direction[0]*distance, crown[1]+y, crown[2]+direction[2]*distance)
            rings.append([tuple(center[j]+across[j]*width*s for j in range(3)) for s in (-1, 0, 1)])
        for near, far in zip(rings, rings[1:]):
            for half in range(2):
                points = [near[half], far[half], far[half+1], near[half+1]]
                mesh.face(points, 'leaf' if half else 'leaflight', (0, 1, 0))
                mesh.face(points, 'leaf', (0, -1, 0))


def sign_plane(name, width, height):
    mesh = Mesh(name)
    mesh.face([(-width/2, -height/2, 0), (width/2, -height/2, 0),
               (width/2, height/2, 0), (-width/2, height/2, 0)], 'teal', (0, 0, 1))
    return ua.mesh_asset(mesh, list(COLORS), full_uv=True)


def build(cars, prefabs, material, extra_cars=(), extra_prefabs=()):
    scene = ua.Hierarchy()
    court = scene.node('Miami 1987 - dealership forecourt')
    count=len(cars)+1
    bay=lambda i:i if i<REFERENCE_BAY else i+1
    ua_mesh = ua.mesh_asset(forecourt(count,len(extra_cars)), list(COLORS))
    scene.renderer(court, ua_mesh, material)
    car_ids = [scene.prefab_instance(path, *placement(bay(i),count)) for i, path in enumerate(prefabs)]
    reference=scene.prefab_instance(REFERENCE,*placement(REFERENCE_BAY,count),root_file_id=REFERENCE_ROOT)
    extra_ids=[scene.prefab_instance(path,*utility_placement(i)) for i,path in enumerate(extra_prefabs)]
    placard = sign_plane('CarPlacard', 3.65, .9125)
    for i, car in enumerate(cars):
        texture = sign(car['id']+'_Sign', [
            (f'{i+1:02d} / {car["role"]}', 23, '#d3b879'),
            (car['name'], 49, '#f3eddc'),
            (car['era'], 28, '#b9ccca'),
            (f'1987 price class: ${car["price"]:,}', 32, '#d3b879'),
        ])
        mat = ua.material(car['id']+'_Sign', texture, unlit=True)
        x = placement(bay(i),count)[0][0]
        label = scene.node(car['name']+' - display card', position=(x, .5, 4.5), pitch=-64)
        scene.renderer(label, placard, mat)
    for i,car in enumerate(extra_cars):
        texture=sign(car['id']+'_Sign',[
            (f'F{i+1} / {car["role"]}',23,'#d3b879'),
            (car['name'],49,'#f3eddc'),
            ({'trail':'Compact three-door / rear spare','ranger':'Full-size two-door / wide stance',
              'highland':'Refined four-wheel drive','warden':'Partition / custody benches',
              'bastion':'Armour / protected cabin','voyager':'Three rows / rear side glass','mica':'Three doors / small steel wheels'}[car['style']],28,'#b9ccca'),
            ('POLICE TRANSPORT' if car['style']=='warden' else 'CREW VEHICLE / available at Don' if car['style']=='bastion' else 'CIVILIAN FLEET / available at Don',29,'#d3b879')])
        label=scene.node(car['name']+' - review card',position=(utility_placement(i)[0][0],.5,18.7),pitch=-64)
        scene.renderer(label,placard,ua.material(car['id']+'_Sign',texture,unlit=True))
    reference_tex=sign('SyntyReference', [
        ('09 / COMPARISON', 23, '#d3b879'),
        ('SYNTY SEDAN', 49, '#f3eddc'),
        ('Original Palm City prefab', 28, '#b9ccca'),
        ('Select 1-8, then C to compare', 32, '#d3b879'),
    ])
    label=scene.node('Synty reference - display card',position=(placement(REFERENCE_BAY,count)[0][0],.5,4.5),pitch=-64)
    scene.renderer(label,placard,ua.material('SyntyReference',reference_tex,unlit=True))
    title_tex = sign('MiamiTitle', [
        ('BISCAYNE MOTOR CLUB', 68, '#d3b879'),
        ('MIAMI / 1987', 139, '#f3eddc'),
        ('SEDANS / UTILITY COLLECTION / 1987', 50, '#b9ccca'),
    ], title=True)
    title = scene.node('Biscayne Motor Club sign', position=(0, 3.6, -6.83))
    scene.renderer(title, sign_plane('ShowroomTitle', 10.5, 2.625),
                   ua.material('MiamiTitle', title_tex, unlit=True))
    sun = scene.node('Warm afternoon sun', pitch=50, yaw=-32)
    sun_id = scene.component(sun, 108, 'Light', light_body(1.4, '{r: 1, g: 0.95, b: 0.86, a: 1}', 2))
    fill = scene.node('Soft sky fill', pitch=65, yaw=148)
    fill_id=scene.component(fill, 108, 'Light', light_body(.32, '{r: 0.72, g: 0.84, b: 1, a: 1}', 0))
    lighting=scene.node('Shared vehicle lighting')
    clock_id=scene.mono(lighting,'Assets/Scripts/Ambient/CityClock.cs',
                       '  startHour: 14\n  running: 0\n',enabled=False)
    headlights_id=scene.mono(lighting,'Assets/RoadDemo/DemoHeadlights.cs',
                            f'  clock: {{fileID: {clock_id}}}\n')
    scene.mono(lighting,'Assets/RoadDemo/DemoSky.cs',
               f'  clock: {{fileID: {clock_id}}}\n  sun: {{fileID: {sun_id}}}\n')
    camera = add_camera(scene, car_ids+[reference]+extra_ids, cars,clock_id,headlights_id,fill_id,extra_cars)
    settings = (ua.ROOT/'Tools/sedans1987/scene_settings.txt').read_text()
    settings = settings.replace('m_Sun: {fileID: 0}', f'm_Sun: {{fileID: {sun_id}}}')
    ua.write(SCENE, settings+scene.text(scene=True))
    ua.meta(SCENE, 'DefaultImporter', '')
    return camera


def add_camera(scene, cars, lineup,clock_id,headlights_id,fill_id,extra_cars=()):
    cfg = CAMERA
    pitch = math.radians(cfg['pitch'])
    position = (0, cfg['pivot'][1]+math.sin(pitch)*cfg['distance'],
                cfg['pivot'][2]+math.cos(pitch)*cfg['distance'])
    camera = scene.node('Main Camera', position=position, pitch=cfg['pitch'], yaw=180, tag='MainCamera')
    scene.component(camera, 20, 'Camera', '''  m_Enabled: 1
  serializedVersion: 2
  m_ClearFlags: 2
  m_BackGroundColor: {r: 0.44, g: 0.64, b: 0.67, a: 1}
  m_projectionMatrixMode: 1
  m_GateFitMode: 2
  m_FOVAxisMode: 0
  m_SensorSize: {x: 36, y: 24}
  m_LensShift: {x: 0, y: 0}
  m_NormalizedViewPortRect:
    serializedVersion: 2
    x: 0
    y: 0
    width: 1
    height: 1
  near clip plane: 0.1
  far clip plane: 200
  field of view: 42
  orthographic: 0
  orthographic size: 5
  m_Depth: -1
  m_CullingMask:
    serializedVersion: 2
    m_Bits: 4294967295
  m_RenderingPath: -1
  m_TargetTexture: {fileID: 0}
  m_TargetDisplay: 0
  m_TargetEye: 3
  m_HDR: 1
  m_AllowMSAA: 1
  m_AllowDynamicResolution: 0
  m_OcclusionCulling: 0
''')
    scene.component(camera, 81, 'AudioListener', '  m_Enabled: 1\n')
    scene.mono(camera, 'Assets/RoadDemo/DemoCamera.cs', f'''  pivot: {ua.v3(cfg['pivot'])}
  distance: {cfg['distance']}
  yaw: {cfg['yaw']}
  pitch: {cfg['pitch']}
  _minimumPitch: 16
  _maximumPitch: 78
  mapAt: 500
  mapTransition: 0
  minDistance: 4.5
  mapCeiling: 80
  hint: "F1-F7: new vehicles / U: new row / 1-8: sedans / 9: Synty / C: compare / 0: all / L: day-night / B: brakes"
  hintTopPx: 12
  showHint: 1
  showZoom: 0
''')
    body = '  cars:\n'+''.join(f'  - {{fileID: {car}}}\n' for car in cars)
    body += '  labels:\n'+''.join('  - '+json.dumps(f'{i+1:02d} / {car["name"]} / {car["role"]}')+'\n'
                                  for i, car in enumerate(lineup))
    body += '  - "09 / SYNTY SEDAN / Original Palm City prefab"\n'
    body += ''.join('  - '+json.dumps(f'F{i+1} / {car["name"]} / {car["role"]}')+'\n' for i,car in enumerate(extra_cars))
    body += f'''  clock: {{fileID: {clock_id}}}
  headlights: {{fileID: {headlights_id}}}
  editorFill: {{fileID: {fill_id}}}
'''
    scene.mono(camera, 'Assets/RoadDemo/SedanShowroom.cs', body)
    return camera


def light_body(intensity, color, shadows):
    return f'''  m_Enabled: 1
  serializedVersion: 13
  m_Type: 1
  m_Color: {color}
  m_Intensity: {intensity}
  m_Range: 10
  m_SpotAngle: 30
  m_InnerSpotAngle: 21.80208
  m_Shadows:
    m_Type: {shadows}
    m_Resolution: -1
    m_CustomResolution: -1
    m_Strength: 0.8
    m_Bias: 0.03
    m_NormalBias: 0.15
    m_NearPlane: 0.2
  m_Cookie: {{fileID: 0}}
  m_DrawHalo: 0
  m_Flare: {{fileID: 0}}
  m_RenderMode: 0
  m_CullingMask:
    serializedVersion: 2
    m_Bits: 4294967295
  m_RenderingLayerMask: 1
  m_Lightmapping: 4
  m_BounceIntensity: 1
  m_UseColorTemperature: 0
  m_UseViewFrustumForShadowCasterCull: 1
'''
