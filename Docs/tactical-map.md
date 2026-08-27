# The tactical map

The city's one map is the **turf map**: the whole city as a 1987 survey plate, full
screen, with the outfit's crews live on top of it. It is a ZOOM LEVEL of the street
camera, not a screen of its own, and it is the only map - the printed plan (`DemoMap`)
and the 320x200 raster terminal that used to sit beside it were removed on 2026-08-27,
docked mode and all.

## Where it lives

| file | what it is |
|---|---|
| `Assets/RoadDemo/TurfMapHud.cs` | the map: the four layers, the view, crews, orders, the pointer, the ceiling it hands the camera |
| `Assets/RoadDemo/TurfMapSurvey.cs` | reads the city and draws the ground, turf and built plates on a worker thread |
| `Assets/RoadDemo/TurfMapPanel.cs` | the paper: date plate, dossier, property and district files, roster, turf key, context menu, place chips |
| `Assets/RoadDemo/TurfMapLabels.cs` | street names as real type floating over the plate, never baked into it |
| `Assets/RoadDemo/TurfMinimap.cs` | the same plate printed small in the bottom right corner while the player is in the street |
| `Assets/RoadDemo/TurfPlate.cs` | the 960x600 raster buffer (320x200 authored units x 3) and every way of putting colour in it |
| `Assets/RoadDemo/TurfMapModel.cs` | `TurfInk`, `TurfHouses`, `TurfBuilding`, `TurfCrew`, `TurfMan`, `TurfDistrict`, `TurfOrder` |

## How it opens

There is no key. Pull the wheel back past `DemoCamera.mapAt` (180 m) and the city
becomes the plan; push in past it and the streets come back where they were. Esc puts
the boom back on the street side of the line, which is the only other way down.
`TurfMapHud.IsOpen` is the flag every other screen reads (the top bar retracts, the
camera's hint stays off, the minimap stands down); it is cleared when the map is
disabled or destroyed, so an unloaded scene never leaves it standing.

The boom drives the plate's scale: the screen shows `distance * DemoCamera.BoomToMetres`
(1.15) metres down its height. The ceiling of the wheel, `DemoCamera.mapCeiling`, is set
by `TurfMapHud` at build from the survey's own city view - the grid with a margin,
fitted to the plate's 8:5 - times a quarter of country, so the last click of the wheel
is the town filling the frame and not the whole island.

While the map is up the world camera renders nothing (`Blank`), so the frame costs a
clear rather than the whole city. The wheel over the paper panel scrolls the roster and
does NOT move the boom (`TurfMapHud.PointerOverChrome`), so a scroll cannot drop under
the map line and close the map by accident.

## The four layers

ground, turf, built, live - stacked `RawImage`s on one sheet that is SCALED, not
resized, so the street names can be children of it. The first three are drawn by the
survey on the thread pool and uploaded on the frame they come back; while the boom is
moving the sheet already on screen is scaled and slid to stand in. The turf wash is a
multiply material, so TURF ON/OFF in the panel is one `SetActive`. Only the live layer
(crews, traffic, order markers, the picked footprint, the marquee) is redrawn per frame.

## Where the data comes from

Nothing is generated. A field the city does not keep is left off the file, not rolled.

| field | source |
|---|---|
| footprint | `Renderer.bounds` of every collider under the Blocks root, plus `RoadDemoBuilder.QuarterRoofs` |
| name | `BusinessMarker.BusinessName`, else the type's label and an index |
| who holds a building | **`BusinessMarker.GangId`**, the project's single source for ownership; read on the main thread (`ReadOwners`) before a plate is handed to a worker |
| who holds a district | the majority of held fronts inside it; a tie is contested |
| districts | `RoadDemoBuilder.DistrictPlans` |
| weekly take | `BusinessMarker.WeeklyIncome`, printed only when it is above nought |
| a lieutenant's rank, stars, loyalty | the personnel roster (`Character.Rank`, attributes) via `CrewWalker.CharacterId`; a rival's crew has none and prints none |
| terrain, roads, water | `LandHeight`, the builder's road tables, `Reservations` |

Floors are derived from height (`/ 3.2 m`) and printed with a `~`. There are no file
numbers and no tenant counts: nothing in the project records either.

Taking a building writes `BusinessMarker.GangId` through `TurfMapHud.ClaimRule`, a
deliberate stub (the crew has to be standing on it and nobody else's men may be) that a
campaign rule replaces wholesale.

## The right button

Right-click on the plate opens the context menu at the cursor: move here, walk in hard,
walk the block, hold position, pull back to post; over a footprint, take it or read its
property file; over a district, read its file. Every order goes through `DemoCrews`
(`MarchTo`, `Sic`, `BoardCar`) - the map moves nobody itself. Orders and patrol boxes
are remembered in WORLD METRES, never in plate units, because every survey publishes a
new projection.

Selecting a crew on the map selects it in the street too (`DemoCrews.Select`): one
selected crew in the game, not two. The map opens on whatever the street had picked.

## The minimap

`TurfMinimap` is the same survey, one whole-city plate, redrawn only when ownership
changes hands (`TurfMapHud.OwnershipStamp`). Crews are pooled UI `Image`s in the
family's ink; the camera's frame is four hairlines. It borrows the full map's
heightfield rather than sampling the island twice. Off while the map, the book or the
strategic map has the screen.

## Things that bit

- The plate is fitted to the window as `object-fit: cover`, so every pointer position
  has to undo the crop (`ToPlan`) before it means anything. Getting it wrong offsets
  every click silently.
- A RectMask2D on the sheet would re-materialise the turf wash and lose the multiply;
  names are placed along the run of street on the sheet instead and overhang half a
  word at most.
- The canvas is scaled against 720 lines, not 1080, or the panel's 8 px floor comes out
  under six real pixels on a small window.
