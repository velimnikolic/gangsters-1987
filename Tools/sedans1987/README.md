# Miami 1987 sedan showroom

Open `Assets/Scenes/Sedan1987Showroom.unity` manually in Unity. Eight original,
fictional sedans stand in descending price class from left to right, with the
original Palm City Synty sedan inserted beside Vahren and Calder for comparison. Shared
`RoadDemo.DemoCamera` provides orbit, pan and zoom. Press **1–8** to inspect a car,
**9** for Synty, **C** for the selected car alongside Synty (Vahren by default),
**0** for the full lineup, **WASD/arrows** to pan, **Q/E or right-drag** to orbit,
and the **wheel** to zoom. **F1–F6** focus the six utility vehicles; **F7** selects **SRBO**. **U** shows this second row, which is the initial view. The scene displays its controls during Play.
Press **L** for day/night. This drives the game's `CityClock`, `DemoSky` and
`DemoHeadlights`; the frozen clock's own number-key handler is disabled.
**B** previews brake lights. **L** also switches Bastion's roof lamps at night.

| Rank | Fictional model | Character |
| --- | --- | --- |
| 1 | Regent Bellavere | Ivory, long upright cabin, domed roof, disc wheel covers |
| 2 | Kronen K58 | Navy executive wedge, lower cladding, restrained alloys |
| 3 | Albion Six | Low narrow roof, long crowned hood, rounded haunches, four lamps |
| 4 | Vahren Drei | Silver compact sports sedan, upright twin grille, four round lamps, cross-spoke wheels |
| 5 | Calder Marivelle | Long champagne body, formal vinyl roof, stacked front lamps, tall rear lamps |
| 6 | Monarch Townline | Wide burgundy cabin, padded roof, turbine covers, segmented rear light band |
| 7 | Bayside Classic | Powder blue, tall airy cabin, broad curved shoulders |
| 8 | Hikari DX | Short beige wedge, sloping hood, dark bumpers and small steel wheels |

Seven additional vehicles occupy the second row:

| Key | Fictional model | Character |
| --- | --- | --- |
| F1 | Bayside Trail | Short three-door 4x4, two-tone roof, rear spare, round lamps |
| F2 | Bayside Ranger | Full-size two-door SUV, long hood, two-tone sides |
| F3 | Albion Highland | Five-door SUV, narrow cabin, four round lamps, bright trim |
| F4 | Borough Warden | Police prisoner van, partition, facing benches, barred rear windows |
| F5 | Monarch Bastion | Four-door armoured truck, open bed, protected dark side glass, bolted window grilles, roof lamps |
| F6 | Calder Voyager | Passenger van, three seat rows, rear side and rear door windows |
| F7 | SRBO | Red three-door economy hatchback, short hood and small steel wheels |

`utilities.json` owns these proportions; `utilities.py`, `utility_cabin.py` and
`utility_details.py` build the models. The civilian SUVs and passenger van also
join ambient traffic; the prisoner van and armoured crew truck remain reserved
for police and outfit use. The showroom's visual approval state does not control
catalog availability.

The shapes use period proportions, body-specific door counts, distinct pillar spacing,
separate hood/cabin/trunk volumes, rear plates and centre brake lights. Each has
its own planform taper, hood slope, roof crown and overhangs. Curved panels carry
deliberate hood/fender creases, shoulder bevels and inset sill bands. The roof has
a rolled perimeter; broad panel planes retain their slope changes instead of
smoothing the whole shell into one highlight. Tires have rounded
shoulders and closed sidewalls on both faces, independently of their decorative rims.
Windows are transparent, with real openings in the body, painted pillars, thick
rubber seals and inner reveals. Seats, headrests, dashboard, steering wheel,
console and inner door panels share the opaque body mesh. A packed
metal/smoothness map distinguishes paint, chrome, rubber and upholstery.
Names and emblems are fictional. Displayed prices are rounded design references
for the lineup's economic classes; the same values price the new merchandise,
without claiming historical invoices.

The fictional-name revision moved 24 assets across the first, second and fifth
cars' meshes, prefabs, sign materials and textures. `retained_guids.json` records
each original GUID and its exact current destination. `validate.py` checks those
destinations and rejects duplicate GUIDs, including after the moves are committed.
While the moves remain unstaged, `Tools/project.py audit` reports their old paths
as deleted: its deleted-path check does not recognize these GUID-preserving moves.
Only these 24 recorded moves are explained by that limitation; any additional
deletion or reference failure still requires investigation. Do not ignore other
audit findings.

Each reusable prefab lives under `Assets/Sedan1987/Prefabs`, faces +Z, has unit
scale and a ground-level root, and contains a body plus four wheel pivots.
The body and four wheels share one URP Lit material and palette texture. A sixth
renderer holds the lamp lenses and uses the **original Synty**
`PolygonPalmCity_03_A.mat`, with UVs sampled from its real emissive atlas.
The seventh renderer holds transparent glass with depth writes and shadows off.
`VehicleLampRig` supplies exact beam origins and receives per-instance emission
from `DemoHeadlights`, retaining its night/engine/parking/wreck/visibility rules
and existing 48-beam limit. Brake emission uses the second lamp submesh/material
slot, independently of the headlights, with the same shared Synty material in
both slots. The shared lighting owner reads deceleration/stopping during daytime
and nighttime. Turn/reverse actuation is not fitted. Bastion adds two roof beams
inside the same 48-beam limit, and a darker side-glass submesh using
`ArmouredSideGlass.mat`; its front/rear glass remains clear.

The sedans have 6,314–6,960 triangles and SRBO has 6,441, including interiors,
within a hard 7,000-triangle budget. Utilities have 5,688–7,376 triangles within
a 7,500 budget. They use seven renderers and three shared materials per car,
except Bastion with four shared materials. The separate brake slot adds one
material draw per vehicle; Bastion's tinted side glass adds another. Coincident vertices with identical packed data are welded.
Transparent glass adds overdraw; these are resource counts, not an FPS result.
The user owns Unity import, Play and visual/performance acceptance.

## Game fleet

`CivilianVehicleCatalog` owns the thirteen ambient passenger model paths and explicit
retained taxi, food delivery and works pickup paths. The passenger van replaces
the unmarked pack vans in this pool.
`CivilianFleet` loads that catalog for RoadDemo/CoreDemo traffic, CoreRoads
parking, fuel customers, harbor/airport parking, industrial staff cars and focused benches. It never falls back
to an old passenger model. `VehicleCatalog.PoliceCars` retains the marked pickup
and adds Borough Warden for the existing police dispatch/custody lifecycle. Its
eight authored seats accommodate two officers and six inward-facing prisoners.
Other emergency services retain their models. `ArmoryCatalog` contains fourteen
vehicles for purchase and delivery: all thirteen civilian models plus Bastion.
Legacy inventory names resolve to the new bodies, and the mob fallback uses
Bayside Classic/Ranger. Synty's comparison sedan remains showroom-only. This uses the game's current Editor-only `DemoAssetLoad`
path; a standalone Player asset-loading migration is outside this change.

`VehicleSeatRig` supplies driver/passenger roots aligned to the cushions and
a usable cabin ceiling and the four side/screen boundary planes. NPC occupants
and real crew riders fit an evaluated sitting pose to the actual cushion and
head envelope. `SeatedHeadShape` measures head/hair geometry with one boarding-time
CPU mesh bake. `VehicleSeatBinding` caches the fit during travel and restores
original scale on exit. There is no per-frame fitting/mesh-bake pass. The shared
`CarBody` keeps its five-seat default, Voyager has six and Warden eight.

`Wheel_FL/FR/RL/RR` names support the shared wheel rig. Preparation retains these
visual rig components. `VehiclePaint` preserves the approved palette and original
lamp material. Traffic collision/parking bounds still use `CarBody` measurements.
No per-car update component or new light budget was introduced.

Future parking/venue bakes use the catalog. `adopt_parked.py` retargets 14 old
passenger instances in the existing nightclub and car-yard prefabs, preserving
parent/root IDs and placement while discarding obsolete source-part overrides.
It leaves service variants and source packs intact; `--check` rejects remaining
old passenger instances in those lots. This is offline serialized authoring,
not an Editor rebake or a live scene update. Re-enter Play manually to build the
city with the new fleet.

## Offline authoring

```sh
python3 -m venv /tmp/gangsters-sedans-venv
/tmp/gangsters-sedans-venv/bin/python3 -m pip install -r Tools/sedans1987/requirements.txt
/tmp/gangsters-sedans-venv/bin/python3 Tools/sedans1987/build.py
/tmp/gangsters-sedans-venv/bin/python3 Tools/sedans1987/validate.py
/tmp/gangsters-sedans-venv/bin/python3 Tools/sedans1987/preview.py
```

`lineup.json` owns dimensions and shape profiles; `bodywork.py`, `cabins.py`,
`fascias.py`, `lenses.py`, and `wheels.py` author their respective surfaces. `sedans.py`
assembles the car and `showroom.py` owns the static composition. `roof_skin.py`
shares exact sampled roof/screen edges. `window_frames.py` grows black seals
inward around distinct front/rear openings, triangulates glass without a central
fan and shares reflection normals. `fascia_skin.py` clips grille/lamp fittings to
the actual end cap, with shallow closed grille surrounds. `screen_skin.py` joins
the hood/cowl to the screen using matching boundary samples; side roof rails
reuse the glazing boundary stations. `compacts.json`/`compacts.py` own SRBO,
`armour.py` owns Bastion's protection and `seating.py` owns visible cushions and
occupant anchors. `material_contract.py` preserves equivalent Unity-normalized
materials without overwriting unrelated imported default properties.
Edit those inputs and regenerate; do not hand-edit the generated mesh assets.
Existing GUIDs are preserved. `build.py --check` is read-only and compares exact
generator input and output hashes in `manifest.json`. Repeated builds on the same
font/Pillow environment are byte-identical. Sign generation uses Arial or DejaVu
Sans; the manifest records the font's hash.

`preview.py` reads the **serialized output** and writes a software-rendered
overview and front/rear contact sheet to `/tmp/sedans1987-preview`. Its simple
lighting does not reproduce URP, shadows, anti-aliasing or Unity import behavior.
`check_cabins.py` checks serialized roof boundary joins and glass-normal continuity;
`check_utilities.py` casts rays through glazing to reject painted panels behind it.
The mesh/prefab checks and `python3 Tools/project.py compile` run without Unity.
Asset import, actual scene rendering and keyboard/mouse controls require the
user's manual Unity review.

The reference uses the existing
`Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Sedan_01.prefab` at its
original scale, with no material or geometry overrides. Its Transform file ID
comes from existing PalmCityDemo/Overview instances of the same prefab variant.
Its source FBX has 9,046 triangles across 16 meshes, including interior and
separate doors; this is not an imported draw-call count or equal feature budget.
`reference_preview.py` reads that installed FBX for offline shape comparisons.
It uses source normals/albedo and opaque glass, so it cannot judge Synty's shader
graphs, transparency, reflections or Unity's calculated normals.
