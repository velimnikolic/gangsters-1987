# The tactical map

The city's map is a **1987 municipal survey terminal**: a 320x200 raster blown up with
square pixels, with a gang-turf HUD over the top. It replaces the printed plan `DemoMap`
used to draw, and it is built from a design handoff (`City Map 1987 v2.dc.html` plus its
README, which is the source of truth for the palette, the raster rules, the data
contract, the turf overlay, hit-testing and the order menu).

## Where it lives

| file | what it is |
|---|---|
| `Assets/RoadDemo/DemoMap.cs` | the map itself: modes, camera coupling, picking, orders, the card |
| `Assets/RoadDemo/Map/MapRaster.cs` | the 320x200 buffer and every way of putting colour in it |
| `Assets/RoadDemo/Map/MapSheet.cs` | where the sheet is held over the city, and at what scale |
| `Assets/RoadDemo/Map/MapPalette.cs` | the exact hexes, and how a family's colour is cut |
| `Assets/RoadDemo/Map/MapBase.cs` | the static layer: terrain, water, roads, blocks, the airfield |
| `Assets/RoadDemo/Map/MapBuildings.cs` | every footprint in the city, and the cached raster of them |
| `Assets/RoadDemo/Map/MapTurf.cs` | the districts, who holds them, and the wash that says so |
| `Assets/RoadDemo/Map/MapAgents.cs` | everything that moves, drawn per frame |
| `Assets/RoadDemo/Map/MapOrders.cs` | the order book - and the rules that are NOT written yet |
| `Assets/RoadDemo/Map/MapOwnership.cs` | which buildings a family has taken |
| `Assets/RoadDemo/Map/MapSurface.cs` | screen position to raster pixel, and nothing else |
| `Assets/RoadDemo/Map/TacticalHud.cs` | the terminal around the raster |

## The rendering rules, which are not negotiable

- The internal target is **exactly 320x200**, whatever the display is.
- **No antialiasing anywhere.** Every entry point on `MapRaster` rounds to integers at the
  door and clips at the edges, so nothing can be half-covered. Alpha exists but only as a
  wash over whole pixels (the turf layer): the result is still one exact colour per pixel.
- The blow-up is a **point-filtered `RawImage`**. The texture is unmipped and
  `FilterMode.Point`; the HUD never sets a material on it.
- **The HUD is a layer over the picture, never inside it.** Masthead, rail, footer,
  district lettering, scanlines and vignette are all uGUI.
- Dithering is the only texture: `((x>>1)+(y>>1)) % n == 0`, never a gradient. Road
  markings are 2 on, 3 off.

## The one departure from the handoff

The sheet pins the scale at **1 px = 8 m** and never moves it. This map rides the camera's
boom instead, so the wheel shows more ground rather than bigger pixels:

```
m/px = boom * 1.15 / 200

boom  180 m  ->  1.0 m/px   (the terminal comes up over the street)
boom  470 m  ->  2.7 m/px
boom ~2000 m -> 11.5 m/px   (the whole city in frame)
```

The sheet's own 8 m sits near the zoomed-out end, where a building is the one or two
pixels the design drew it as. This was a deliberate decision: the map has always BEEN the
camera in this project, and a fixed sheet would have been a worse map than the one it
replaced. Everything else in the sheet's rendering section is kept exactly.

The cost is that the cached layers go stale on a zoom as well as on a pan, which is why
`MapSheet.Matches` exists and why every bake is culled to the window.

## What costs what

Three cached buffers - ground, buildings, turf - are re-rasterised only when the framing
changes or when somebody takes a building, and blitted in order every frame. Per frame the
map draws only what moves: crews, the crowd (budgeted at 900), cars, shipping, order
markers, the selection box. That is the handoff's performance note, and it is the
difference between a prototype with 330 buildings and a city with five thousand.

Draw order: **base blit -> turf wash -> marching borders -> buildings -> blink frame ->
vehicles -> people -> shipping -> order markers -> selection box.**

## Where the data comes from

Nothing is generated. The prototype rolled a plausible city so the map could be judged;
this reads the real one.

| the handoff's field | our source |
|---|---|
| footprint | `Renderer.bounds` of every collider under the Blocks root, plus `RoadDemoBuilder.QuarterRoofs` |
| name | the prefab's own name, or the front's sign |
| district | `RoadDemoBuilder.CityQuarters` + `DistrictPlans` |
| gang | `GangFront` - one door per family - resolved per district, then overridden per building by a claim |
| weekly take, staff | `FrontDossier`, and **only for a family's front** |
| terrain, roads, blocks | `LandHeight`, `SeamPlans`, `LotPlans`, `MergedYards`, `Reservations`, `Net.Roads`, `QuarterRoads` |

Two fields are **derived** and are flagged as such on the card:

- **Floors** - nothing records them, so they are `bounds.size.y / 3.2 m`, printed with a `~`.
- **Type** - nothing records it either. The piece's own name decides where it can
  ("building-warehouse-large", "SM_Bld_OfficeSquare"); failing that the bake it came out
  of ("warehouse-block"); failing both, the shape it actually has. A Synty city cluster
  names its pieces `City_07_I` and says nothing, and without the last step a city comes
  out in one colour.

Two fields the handoff asks for **do not exist** and are left off the card rather than
invented: occupants for an ordinary building, and weekly income for anything that is not a
family's front.

## The city is read until it answers

`DemoMap` is created by the builder and starts on the frame after - which is sometimes
before the blocks are in the scene. The old plan degraded quietly when that happened; this
map is MADE of the footprints, so it re-reads on a slow timer until a footprint turns up
and gives up after 25 seconds. If you ever see a map with no buildings on it, that is the
symptom to look for.

## The right button is the street's, not the map's

The map does NOT have an order vocabulary of its own. It had one - MOVE HERE, ATTACK
HERE, PATROL AREA, HOLD POSITION, FALL BACK, invented from the design sheet - and it was
the wrong shape for this game: the city already has orders, the player already knows
them, and a second set that exists only on the map is a second game to learn.

So the right button resolves exactly as `CrewOverlay.ReadRightClick` resolves it, in the
same order, calling the same verbs on `DemoCrews`:

- **nothing at all** unless one of the player's crews is selected (the street's rule:
  `_crews.Selected == null` and the click is dropped);
- a **rival's man** -> KILL / MOTO DRIVE-BY / BOMBA, with the refusal notes read off the
  crews themselves, so a card can never disagree with the order behind it;
- a **rival family's door** -> BOMBA;
- **anywhere else** -> the crew walks there; the same click twice, quickly, and it runs.

Picking a crew picks it in the street too (`DemoCrews.Select`), so there is one selected
crew in this game and not two - and that goes for the ROSTER as well as for a man on the
sheet. A box that sweeps up several takes the one with the most men inside it, because the
city gives orders to one crew.

The roster's own right button is not an order: right-clicking a lieutenant's name centres
the map on him. The crew list is the only place on this terminal that names a man who may
be right off the edge of the sheet, and hunting for him by dragging is no way to find him.
Because the camera's pivot IS the map's centre, that also leaves the player standing over
that ground when he drops back into the street. uGUI's `Button` answers the left click and
drops every other, so the row carries a small second handler for the right one.

## The map is the screen

The sheet fills the whole panel and everything else floats on it: masthead top left,
readouts top right, the rail down the right, the strips along the bottom. Each carries a
near-black scrim, because a panel with nothing behind it is a panel you cannot read over a
lit city block.

Filling means scaling to **cover**, not to fit: at 16:9 that cuts about ten rows off the
top and bottom of a 16:10 raster, which is the price of the map being the screen. Square
pixels are kept; a `RectMask2D` on the well cuts the overflow.

The city's own clock bar (`DemoTopBar`) is switched off while the map has the screen - the
terminal prints its own clock, and two over one picture is one too many. Its canvas is
disabled rather than its object, so it keeps ticking and comes back as it was.

### The chrome is type; only the map is pixels

The design sheet sets everything in Silkscreen, and that is right for a page which is
*entirely* a picture of a terminal. Here the overlays stand on a living map at sizes the
sheet never anticipated, and a bitmap face blown up over it reads as a blurred imitation
of pixel art rather than as pixel art. So the raster is the pixels and the chrome is type
- Oswald, the same face every other screen in this game is set in - with hairline rules
and flat colour, hard into the corners with no page margin.

`LedgerStyle.Pixel` (Silkscreen) stays in the project and stays available; nothing on this
map uses it now.

Both of the city's own bars (`DemoTopBar`, `CrewBar`) are switched off while the map has
the screen: the terminal prints its own clock and its own roster. Their CANVASES are
disabled, not their objects, so both keep working and come back untouched - and every
canvas in each tree, because the crew bar keeps one per crew block and none on its root.

### Contested ground does not march

The sheet crawls a contested border - marching ants, the dash phase pushed round with the
clock. Built that way, and wrong at this size: the sheet is blown up six times over a map
that is itself the screen, and four pixels of dash travelling round a district becomes a
band of glitter that drags the eye off everything else. Contested ground already says so -
it is the only ground cross-hatched in both directions, in bone white, with a bone-white
tag. So nothing marches: a held border is a solid rule in the family's colour, a contested
one is a solid rule that breathes on a slow cycle.

### One that bit hard

`Texture2D.SetPixels32` fills from the **bottom** left. The raster is written the way the
design sheet draws one and the way a screen scans - row nought at the top, north - so a
buffer handed to a `RawImage` as it stands comes out UPSIDE DOWN. A mirrored city looks
perfectly plausible; what gave it away was that W walked the camera down the map and every
click landed on the building reflected about the middle row. The fix is one line -
`uvRect = new Rect(0, 1, 1, -1)` - and it is the reason `MapSheet` can go on reasoning in
"row nought is north" like the prototype does.

## What is a stub

Every one of these has feedback, a state transition and a log line. None of them has a
rule, because none of the rules is written down anywhere in this project. They are
delegates on `MapOrders`, null by default:

- `Stakeout` - who watches, for how long, for what
- `Claimed` - what taking a building costs, who may, and what the family it was taken from
  does about it. The deed itself flips in `MapOwnership`, which is a seam and not a rule.
- `Extort`, `MakeHq` - log-only in the handoff too

Everything else the map can order is real, because it is the street's own verb.

What the roster prints in its order column is read off the men (`MapOrders.StateOf`) and
not remembered from the last thing the map told them: a crew moved by a job off the
ledger, by a fight it walked into or by a car it got into would otherwise go on being
described by an order it was given a minute ago.

## Known gaps

- **No freight train.** The handoff's ambient layer wants one; this city has no rail, and
  a railway was not going to be invented for a map.
- **No aircraft.** Ships are drawn (found by the `HarborBob` on every vessel's model); the
  airfield's aeroplanes are owned privately by `AirportDistrict` and carry no component the
  map can find without opening that district up.
- **No street names.** At these scales a block is a dozen pixels and a street name cannot
  be read. The handoff only letters districts, and so does this.
