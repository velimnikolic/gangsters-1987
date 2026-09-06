# Miami 1987 sedan showroom

Open `Assets/Scenes/Sedan1987Showroom.unity` manually in Unity. Eight original,
fictional sedans stand in descending price class from left to right. Shared
`RoadDemo.DemoCamera` provides orbit, pan and zoom. Press **1–8** to inspect a car,
**0** for the full lineup, **WASD/arrows** to pan, **Q/E or right-drag** to orbit,
and the **wheel** to zoom. The scene displays its controls during Play.

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

The shapes use 1980s US-market proportions, four doors, distinct pillar spacing,
separate hood/cabin/trunk volumes, rear plates and centre brake lights. Each has
its own planform taper, hood slope, roof crown and overhangs. Curved panels carry
smooth vertex normals; seams and chrome retain sharp edges. Tires have rounded
shoulders and closed sidewalls on both faces, independently of their decorative rims.
Names and emblems are fictional. Displayed prices are rounded design references
for the lineup's economic classes, not campaign prices or historical invoices.

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
All five renderers share one URP Lit material and palette texture. The models have
roughly 16,000–20,000 triangles each. They are static review assets; traffic admission,
colliders, vehicle configuration and moving door/interior rigs are not installed.

## Offline authoring

```sh
python3 -m venv /tmp/gangsters-sedans-venv
/tmp/gangsters-sedans-venv/bin/python3 -m pip install -r Tools/sedans1987/requirements.txt
/tmp/gangsters-sedans-venv/bin/python3 Tools/sedans1987/build.py
/tmp/gangsters-sedans-venv/bin/python3 Tools/sedans1987/validate.py
/tmp/gangsters-sedans-venv/bin/python3 Tools/sedans1987/preview.py
```

`lineup.json` owns dimensions and shape profiles; `bodywork.py`, `cabins.py`,
`fascias.py`, and `wheels.py` author their respective surfaces. `sedans.py`
assembles the car and `showroom.py` owns the static composition.
Edit those inputs and regenerate; do not hand-edit the generated mesh assets.
Existing GUIDs are preserved. `build.py --check` is read-only and compares exact
generator input and output hashes in `manifest.json`. Repeated builds on the same
font/Pillow environment are byte-identical. Sign generation uses Arial or DejaVu
Sans; the manifest records the font's hash.

`preview.py` reads the **serialized output** and writes a software-rendered
overview and front/rear contact sheet to `/tmp/sedans1987-preview`. Its simple
lighting does not reproduce URP, shadows, anti-aliasing or Unity import behavior.
The mesh/prefab checks and `python3 Tools/project.py compile` run without Unity.
Asset import, actual scene rendering and keyboard/mouse controls require the
user's manual Unity review.
