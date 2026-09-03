# The Blueprint — the apartment sheet of a building (EPIC 27, FLAT-005/006)

> Design brief, 2026-09-03, written from the `blueprint.zip` handoff (an interactive HTML
> prototype, `Outfit Ledger v2.dc.html`, plus its README). Standalone: an agent reads this,
> `Docs/design-briefs/apartments-brief.md` and the Linear epic GAN-246, and touches code.
>
> **The prototype is a picture, not an authority.** Where it disagrees with what the code and
> the mechanics brief already say, the code wins — that is the user's ruling of 2026-09-03, and
> §7 lists every place the prototype was overruled. What the prototype IS authoritative about is
> the shape of the screen: which panels exist, what they say, in what order, and what happens
> when the reader clicks.

In one sentence: **a building's flats are one sheet — floors as rows, doors as cells — opened as
a popup over whatever ledger page is showing, by clicking that building in the live block film
on the block file.**

## 1. Where it opens from

The prototype hangs the sheet off a `BLUEPRINT` tab in the tab bar. **We do not add a tab.** The
entry point is the building itself:

* The block file (`Assets/Scripts/UI/PersonnelAlmanac.BlockFile.cs`, `BuildBlockModel` ~line 589)
  already films the real block into a plate and already turns a click on a building into a pick:
  `BlockFilmView.OnPointerClick` → `At()` raycasts the filmed geometry, resolves the stand-in
  back to the real transform through `BlockFilm.Original`, and matches it against the doors it
  was given → `Picked` → `PickTrade`.
* Those doors are read from `CityBusinesses.All` only (`ReadBlockTrades`, ~line 317), so the film
  knows shops and nothing else. The block file must also enumerate the block's residential
  buildings before anything about a building can be pointed at.
* The building is reached through **a mast of its own** (§1.1), not by clicking its walls: a line
  from the building's centre up past its roof, ending in the same square mark the shops use, and
  that square opens the blueprint. A click on the walls keeps doing exactly what it does now
  (`PickTrade` → `DoorMenu`).
* The same building is also reachable from the sheet under the plate: WHAT TRADES HERE is grouped
  by building and its header opens the blueprint (§1.5).

### 1.1 The building gets a MAST, and the mast is what is clicked

The contrarian pass of 2026-09-03 measured this in the live editor and the user confirmed it from
play: **today only a shop can be clicked on the plate, never the building itself.** The reason is
that they are the same transform.

* A business's `Door.View` is `trade.View = marker.transform` (`PersonnelAlmanac.BlockFile.cs:336`)
  and `BusinessRuntime.BindBlockView` hangs that marker on a **direct child of the block content —
  the whole building** (`BusinessRuntime.cs:270-300`; `MeasurePieces` only ever considers
  `content.GetChild(i)`). Measured live: 26 bound views on the open quarter, every one of them
  named like `residential-07 (13,8) 180` — a building, not a bay.
* `At()` then resolves by **first ancestry match** (`BlockFilmView.cs:276-281`), so any hit
  anywhere on the building belongs to its shop.
* This is not an edge case: 13 of the 14 apartment units in `Assets/RoadDemo/ResidentialUnits.cs`
  carry between 1 and 22 `ShopBays`, and `BusinessPopulation.cs:57-59` fills every eligible bay.

So appending residential buildings to the door list changes nothing — the shop wins every click —
and reversing the order would make shops on residential buildings unclickable.

**The user's ruling, 2026-09-03: do not fight over the geometry. Give the building its own mast.**

A line rises from the **centre of the building** and ends in **the same small square mark the
shops already use** (`BuildMarks`, `BlockFilmView.cs:106-135`). That square is the building's, and
clicking it opens the blueprint. Clicking the building's walls keeps doing exactly what it does
today: it opens the shop that is in it. Nothing about the shipped racket interaction moves.

What that costs, in order:

* **The mast is drawn, not modelled.** Both ends are world points projected into the plate the way
  the marks already are (`PlaceMarks`), so it survives a turn without allocating: the base is the
  building's footprint centre on the ground, the head is that point plus the building's own rise
  plus a clearance. A thin 1–2 px `Image` is stretched and rotated between the two projected
  points; the square sits at the head.
* **The head must clear the roof at EVERY yaw.** The lens turns round the block at the city's own
  pitch (`BlockFilm.Look`), so a mast that only clears the roof from the front will be swallowed
  by the building from behind. The clearance is measured off the building's rise — `RiseOf`
  already computes one for a bound marker (`PersonnelAlmanac.BlockFile.cs:657-...`), and the
  harvested `MaxH` gives it for a building with no marker — never a guessed constant.
* **The head must not sit on top of the shop marks.** Shop marks hang at `door + 2.2 m`
  (`:653-656`), i.e. at street level; the mast's head is above the roof, so the two bands do not
  meet. The mast LINE crosses the facade on its way up, which is why it is thin and why it takes
  the flats ink rather than a tenure colour (§1.3).
* **Resolution order changes, not the raycast.** In `At()`, the building masts' heads are tested
  FIRST, within their own hit radius; if none is hit, the geometry raycast runs exactly as today
  and the shop answers. The existing 22 px nearest-mark fallback (`:284-299`) then applies to shop
  marks only — with a measured median of 41 doors a block (max 58), it must never arbitrate
  between a shopfront mark and a mast head.
* **A mast whose head falls outside the plate rect is hidden**, mast and square together, the way
  a mark off the plate already is.

### 1.2 The two lists stay separate

`Door.Key` is an index into `blockCardTrades`, and four places depend on that: the pick
(`PersonnelAlmanac.BlockFile.cs:673-676`), the hover note (`:789-796`), and the PREMISES count
printed twice (`:681`, `:984`) — plus the racket standings keyed by `TerritoryBusinessId`
(`:287-291`) and `BuildTradePopup` handing a door to `DoorMenu.Open` (`:1266-1281`).

Residential buildings therefore join **`blockCardDoors` only**, in their own list, with keys in a
disjoint range (`key = -2 - index`; `-1` already means bare street). `Picked` and `Hovered` branch
on the sign. `blockCardTrades` keeps meaning premises, and the premises count keeps telling the
truth.

### 1.3 The mark, and the word for a house

The mark's fallback position is `trade.View.position` (`:654-656`), which for a residential unit
is the **lot corner**, not the middle of the building — the transform is placed at the yaw-shifted
corner (`ResidentialBlocks.cs:966-977`). A mast standing on that corner would lean off the roof
and cross a neighbour. **The mast's base is the measured footprint centre**, computable from
`SpotRect` (`ResidentialBusinessSites.cs:438-479`), never the transform's position.

The film's key has four words and all four are racket states — `OURS`, `PAYS US`,
`ANOTHER HOUSE`, `NOBODY LEANS` (`:810-811`). A building we hold two rooms in is none of them, so
the key gets a fifth entry with its own ink for flats (ours / none) rather than borrowing a tenure
colour that means something else.

### 1.4 Where the entry point does NOT exist

Sixteen downtown blocks (`block-01` … `block-16`) are monolithic harvested prefabs with no
plan-level grouping at all — `gangsters_business_audit` says so in words, and
`Docs/business-inventory.md:77` records the same gap. There is no building object on them to
click. FLAT-005 is scoped to plan-backed residential blocks; on the others the film keeps printing
the honest line it already prints for uncomposed ground
(`PersonnelAlmanac.BlockFile.cs:601-604`). A block-interior harvest is a separate job, not a
silent prerequisite.

If the film were the only door the sheet could not be opened in `Ledger.unity` at all — that scene
is deliberately city-less (`LedgerMenuScene.cs:8-19`), so `BuildBlockModel` prints "Nothing to
film". **The grouped WHAT TRADES HERE header (§1.5) is the second way in**, and it needs no
camera: it is what makes the sheet reviewable where this project reviews sheets, and it keeps the
whole feature from hanging off one raycast.

The sheet lies over whatever page the ledger is showing; it does not switch pages. Backdrop click
or the ✕ closes it and gives the page back. §2.1 says what it is actually built as.

### 1.5 WHAT TRADES HERE is grouped by building, and the header opens the blueprint

User's ruling, 2026-09-03. The list under the plate is one flat alphabet of shop names today
(`PersonnelAlmanac.BlockFile.cs`, the trades column, sorted at `:292-299`). It becomes **grouped
by building**: a header row carrying the building's name, then that building's shops under it as
they read now. **Clicking the header opens the blueprint for that building** — the second, and
filmless, way in that §1.4 says the sheet needs.

* Grouping is by the same plan-derived building id the deed book is keyed on (FLAT-001), not by
  anything read off the composed scene.
* **A building has no name today.** `StreetNames` (`Assets/RoadDemo/StreetNames.cs`) names the
  city's streets — `Vertical(i)`, `Horizontal(j)`, `Quarter(index)` — but nothing anywhere carries
  a house number or an address, so "318 FOURTH ST." has to be minted: the street the building
  fronts, plus a deterministic number off the building's own stream. It is the same string the
  blueprint's header and the flat form's `PREMISES FORM · …` line print, so it is one function,
  written once, in the building record — not three places that agree by luck.
* A building with flats but no shop still gets a header, with nothing under it. That is how a
  block of flats becomes visible in a list that has only ever listed trades.
* The existing severity sort stays INSIDE a group; the groups themselves order by address. The
  PREMISES count and "N ORDERS ON THIS BLOCK" keep counting premises, not headers.

## 2. The blueprint popup

Full-viewport dark backdrop over the book; one centred sheet, hard-edged, no rounded corners
anywhere in this design, no open/close animation. Widths are MEASURED, type through
`LedgerStyle`, chrome through `UiSkin` — as everywhere else in the ledger.

### 2.1 What it is built as, and the two things the book has never had

The book has no modal-over-a-page pattern. Its only full-screen modal is the book itself
(`PersonnelAlmanac.cs:1013-1027`); every in-book popup is parented into the page and re-laid on
each paint (`DoorMenu.Open(card, …)` at `PersonnelAlmanac.BlockFile.cs:1276-1288`), and the
nearest thing to a dialog is the inline two-step `pendingConfirm`
(`PersonnelAlmanac.Personnel.cs:226`, `:2156-2211`). Esc is page-scoped too: `CloseBlocksTransient`
runs only while `currentPage == LedgerPage.Blocks` (`PersonnelAlmanac.cs:429`).

**So the blueprint is built as a tab-less PAGE, not as the book's first nested modal.**
`LedgerPage.Orders` is the precedent and it is documented as one: last in the enum, deliberately
no tab, its root still builds so `SetPage` can reach it from code (`PersonnelAlmanac.cs:196-198`).
The blueprint can only ever be opened from the Blocks page, and a full-viewport backdrop makes the
tab bar unclickable anyway, so "a popup over any page" is a page in costume. It keeps the popup
LOOK — backdrop, centred sheet, ✕ — and takes the page's own lifecycle, Esc chain and
back-to-where-you-were.

The flat form on top of it is a second sheet within that page, not a second modal.

**Typing a name is a new primitive.** There is no text input anywhere in this project —
`TMP_InputField` and `InputField` appear in no file under `Assets/Scripts/UI` or
`Assets/RoadDemo`. Worse, the page is destroyed and rebuilt on world events, not only on clicks:
`territoryObservationVersion`, `paintedGangVersion`, `director.Version` and four more force a
`Repaint()` (`PersonnelAlmanac.cs:498-518`) — "an observation tick, a man moving, a gang stirring".
A field built the ordinary way loses focus and half the word within seconds of a living city
ticking. Two things follow, and both are FLAT-006 work, not a detail:

* the input is a shared `LedgerKit` primitive, so the next sheet that needs one does not invent a
  second;
* it survives the repaint — either parked across the rebuild the way the film is
  (`ParkBlockModelForRebuild`, `PersonnelAlmanac.BlockFile.cs:714-732`), or by gating `Repaint()`
  while the field holds the caret, the way it is already gated while the film is being turned
  (`PersonnelAlmanac.cs:495-496`).

**Header.** Title `BLUEPRINT`, and under it the address line of the building —
`318 FOURTH ST. · SIX FLOORS · TEN DOORS TO A LANDING` in the prototype, built from the real
building's storey and door counts here. On the right, a row of quick-fact chips (label over
value): `ON OUR DEED`, `OPEN`, `DARK`, `SHUT`, `HEAT`. A heavy rule closes the header. The ✕ sits
in the sheet's top-right corner and reddens on hover.

**THE BUILDING — the plan.** Sub-head `THE BUILDING` with the helper line *"click a door · hover
reads the name off it · the form on the right is the clerk's paper"*.

* Column headers, one per door on a landing, lettered `A`, `B`, … from the street corner.
* One row per floor, **top floor first, ground floor last**. Each row is a floor-number cell, the
  door cells, and a floor-summary column (`OURS n/m`, the OPEN/DARK/SHUT counts, `HEAT n/DAY`).
* A door cell carries: the door badge (`3C`), a state square, the role/tenant line, and then
  either a half-star fit rating, a stamp (`DARK`, `RAID 214`, `NO BANK`), or a plain tag
  (`OPEN`, `EMPTY`, `TENANT`, `COMMON`).
* The entrance and any common ground is drawn hatched and is not clickable. Flats we do not own
  are drawn hatched too, with the sitting tenant's name on them, and ARE clickable — that is how
  a flat is bought.
* A caption bar under the plan reads the hovered or selected door: its name, a one-line summary,
  its state, with a state dot.
* A legend of five: `OPEN · EARNING`, `DARK · NO KEEPER`, `CLOSED · NO BANK`, `RAIDED · SEALED`,
  `HATCHED · NOT OURS`.

**OUR FLATS.** Under the plan, full width: one row per flat we own in this building. Columns:
door · `ROLE` · `NAME` · `KEEPER` · `EARN` · `HEAT` · `STATUS`. Clicking the `ROLE` or `NAME`
header sorts alphabetically by it, clicking again reverses; the live column shows ▲/▼ and the
other column's arrow clears. A flat with no role reads `NO ROLE` in red. Rows open that flat's
form.

## 3. The flat form (a second popup, over the first)

Same treatment: its own backdrop, its own ✕, closing it leaves the blueprint standing. Dark
header bar: the small line `PREMISES FORM · 318 FOURTH ST.`, the big title (`FLAT 3C`, `SHOP GA`,
`ENTRANCE`), the subline `THIRD FLOOR · DOOR C`, and a state dot with its word (`OPEN`, `DARK`,
`RAIDED — CLOSED UNTIL DAY 214`).

The body is two columns, evenly split, with a rule between them. It has **three modes**:

| Mode | When | Left column | Right column | Button |
|---|---|---|---|---|
| **Buy** | flat is not ours | 1 · THE DEED (the price, the note *"cash, at the table, the day it is signed"*), 2 · THE NAME ON THE DOOR (locked, showing the sitting tenant), 3 · WHAT RUNS OUT OF IT (italic note that nothing is set until it is bought, then a one-line preview of what each role takes a day) | short italic note: *"Buy the deed and this same paper reopens straight to the role and keeper — one form, no second trip."* | `BUY $X` |
| **Detail** | ours, role set | 1 · THE DEED (`ON OUR DEED`, green), 2 · THE NAME ON THE DOOR (editable), 3 · the role name big, with `FIT-OUT PAID` and `HEAT WHILE OPEN` beside it (red ≥3, amber >0, green 0) | WHAT'S IN THE ROOM, then 4 · WHO KEEPS IT — keeper name, rank and duty under it, his half-star fit on the right; or the red line `NO KEEPER — THE FLAT READS DARK` | `EDIT ROLE & KEEPER` |
| **Edit** | ours, no role yet, or EDIT pressed | 1, 2 as above, then 3 · the role picker: a row per role — mark · `ROLE` · `EARN` · `FIT-OUT` · `HEAT` — clickable | WHAT'S IN THE ROOM (only once a role is drafted), then 4 · WHO KEEPS IT as a scrolling table — mark · `OUR MEN` · `FIT` · `STANDING` — the current keeper first, each man with rank + present post, his half-star fit for THIS role, and his note (`KEEPER`, `keeps 3C`, `jailed`, `hurt`); men who are elsewhere are greyed and unclickable | `SAVE`, live only once something changed |

Both the detail and the edit keeper panel end with the same footnote, italic:
*"A keeper is off the street. Pull him into a crew and the flat goes dark that moment."*

**WHAT'S IN THE ROOM** is a tinted block on the right whenever a role is set, with a per-role
line of facts and, top-right, either the earn (`$950/DAY WHILE OPEN`, green) or
`NO DIRECT TAKE — JUST HOLDS IT` (muted):

| Role | What the panel says |
|---|---|
| Armory | guns racked, cash on hand, last counted (days ago) |
| Cash stash | cash held, last counted |
| Safehouse | who is hiding there (1–3 names), food and doctor |
| Infirmary | who is under care, or "empty this week"; supplies |
| Garage | cars on the floor, plate status |
| Card room | bank, tables running of four, last night's take |
| Brothel | girls working, take last night, the house cut |

In the prototype this panel is a hash of the door id, for looks. **Here it reads the real
simulation** — the guns actually racked, the money actually held, the men actually hiding
(FLAT-003). Nothing display-only.

**The footer bar.** Up to three bill facts (`DEED` paid or owed, `FIT-OUT DUE NOW`, `HEAT WHILE
OPEN`), then the primary button, then the narrow ones:

* `CANCEL` — edit mode only, and only when a role existed before the edit; drops the draft.
* `PULL HIM OUT` — red, whenever a keeper is set; frees him that moment and the flat goes dark.

Under the buttons, one contextual line, colour-coded. The prototype's exact copy, in its order of
precedence — take these as the wording:

1. *"The entrance is common ground. Nothing is bought here and nobody is kept here."*
2. *"The lease stands until the deed changes hands. Buy the flat, name a role and a keeper, then
   save it all in one line."*
3. *"Set a role down before naming a keeper — an empty flat has nothing to keep."* (amber)
4. *"Sealed by the precinct until day 214. A keeper may stand in it, but the door stays shut."*
   (red)
5. *"The card room has no bank. It stays closed until money is put behind the table."* (amber)
6. *"No keeper named. The flat reads dark and takes nothing in."* (red)
7. otherwise: *"CARD ROOM · $15,000 fit-out · 3 heat a day while the door is open."*

## 4. What clicking does

* An unowned flat opens the form in **Buy** mode — deed and the earnings preview only, no role or
  keeper controls.
* `BUY` does not close the form. The flat becomes ours and, having no role, the same form lands
  straight in **Edit** mode. One form, no second trip.
* An owned flat that has a role opens in **Detail**, never in the raw pickers. This was a
  deliberate correction inside the prototype's own history; keep it.
* `EDIT ROLE & KEEPER` seeds the pickers with what the flat currently has. `SAVE` commits and
  returns to Detail without closing. `CANCEL` drops the draft and returns to Detail.
* A keeper cannot be picked before a role is set (reason line 3). A man keeping another flat is
  blocked with `keeps 3C` until he is pulled out of it.
* A card room without a bank reads `CLOSED — BANK EMPTY`. A raided flat reads
  `RAIDED — CLOSED UNTIL DAY n` and stays shut whoever stands in it.
* Backdrop or ✕ closes the **topmost** popup only; a click inside a sheet never reaches the
  backdrop.
* Every committed action — buy, save, pull him out — files its plain-English line in the orders
  ledger with what it cost and what heat it bought. That is the EPIC 13 law, not a UI nicety.

## 5. Type, colour and measure

The prototype is written in oklch and IBM Plex Mono. It maps onto the ledger skin we already
have; **use the skin, not the prototype's literals**:

| Prototype | Ours |
|---|---|
| ink `oklch(0.2 0.01 55)` | `LedgerV2.Ink` |
| muted `oklch(0.48 0.02 55)` | `LedgerV2.Muted` / `LedgerV2.Label` |
| dim `oklch(0.72 0.02 60)` | `LedgerV2.Faint` |
| red `oklch(0.5 0.16 25)` | `LedgerV2.Red` |
| amber `oklch(0.58 0.13 75)` | `LedgerV2.Amber` |
| green `oklch(0.45 0.13 145)` | `LedgerV2.Green` |
| paper blue `oklch(0.38 0.03 250)` | `LedgerV2.PaperBlue` |
| dark chrome `oklch(0.19–0.24 0.012 55)` | `LedgerV2.Head` / `LedgerV2.DarkPlate` |
| sheet `oklch(0.965–0.97 0.008–0.01 72–80)` | `LedgerV2.Panel` |
| selected row `oklch(0.93 0.03 84)` | `LedgerV2.Picked` |
| hairlines | `LedgerV2.Hair` / `LedgerV2.Rule` |
| IBM Plex Mono, 9–15px | `LedgerStyle.Mono`, sized by the ledger's own optical ratio |
| Georgia italic footnotes | `LedgerStyle.MonoItalic` where the ledger already uses it |

Shape rules that DO carry over verbatim: no rounded corners anywhere; no animation on open or
close; stars are the ledger's existing half-star sprites; the hatch on not-ours and common cells
is a drawn diagonal, not an image; every number is right-aligned in its column.

Nothing here is measured in pixels against a 1920×1080 canvas without being re-measured against
the book. The prototype's own sizes (sheet ≤1400px wide, flat form ≤1010px, door cell ~92×64px,
keeper table ≤322px tall) are the PROPORTIONS to hit, not the numbers to paste.

## 6. State the screen needs

Per building: its flats, each with door, floor, slot letter, ours or not, role, the name the
player typed, keeper, asking price, the sitting tenant's name, the card-room bank, the absolute
day a raid sealed it until. Plus: which flat's form is open, the draft role/keeper/name held
apart from the committed row until `SAVE`, the mode, the sort key and direction of OUR FLATS, and
the roster with each man's present post and condition so the keeper table can grey out who is
not available. All of it hangs off the `Apartments` book (FLAT-001); the sheet owns nothing but
the sort and the draft.

## 7. Where the prototype was overruled

| Prototype says | We do | Why |
|---|---|---|
| 6 floors × 10 doors = 60 flats + entrance, a fixed A–J grid | floors and doors per landing come from the real building; the grid draws N × M — see §7.1, because neither number exists in the repo yet | the city's residential fabric is the 4- and 5-storey terrace kit. The sheet must fit a building, not the other way round |
| garage earns $220/day | garage earns nothing | epic rule: no flat takes money in except the card room and the brothel, and theirs is illegal income that needs washing |
| ground-floor shops are bought from the blueprint at $62,000 | the ground floor is drawn, never sold, from this sheet | shops are `BusinessDeeds` and are bought through `DoorMenu`; two ways to buy one thing is two sources of truth |
| asking price varies 36k–62k per door by a hash | the price comes off `EconomyPrices.Apartment` (55,000), varied per door by the unit's own deterministic stream | `Docs/economy-prices.md` is the price authority |
| WHAT'S IN THE ROOM is a hash of the door id | it reads the real simulation | EPIC 13: nothing on a sheet may be a number the simulation does not hold |
| fit is a hash stub | fit is the designed per-role attribute mix from the mechanics brief §3 | same |
| a `BLUEPRINT` tab in the tab bar | no tab; the building in the block film is the way in | the sheet is about ONE building, so it opens from that building |
| tab bar is THE PAPER · ORGANIZATION · BLOCKS · BLUEPRINT · FINANCES · ARMORY · FAMILIES | the ledger's real tabs (`PersonnelAlmanac.cs:201`) are unchanged | the prototype only mocked enough of the book to sit in |
| the roles' costs and heat | unchanged — they already match the mechanics brief §3 | the prototype was built from it |

### 7.1 The grid's two numbers do not exist yet (FLAT-001 blocks FLAT-006)

The contrarian pass measured this. The harvest table records a **height**, not a storey count:
`ResidentialUnit.MaxH` (`Assets/RoadDemo/ResidentialUnits.cs:62`), measured 15.2–18.6 m across
the 14 apartment units. Divide by `RoadSpace.Storey = 3.2f` (`RoadSpace.cs:47`) and you get
4.75–5.8 storeys; divide by the pitch the authored POLYGON modules actually use (base y=0, upper
stack y=3, roof y=12 — `ResidentialBlocks.cs:1026-1028`) and you get 5.07–6.2. **The storey count
changes with the guess.** And doors per landing is worse: `ResidentialUnit.Doors`
(`ResidentialUnits.cs:46`) reads `[0, 0, 0, 0]` on 8 of the 14 units — most of the fabric has no
residential entrance harvested at all, the ground floor being all shop bay.

A deed book keyed by (building, unit) cannot rest on a division. If someone later corrects the
pitch, every building in every save deals a different number of flats at different door letters
for different money.

* **Ruling:** `Storeys` becomes a **measured field in the harvest**, counted off the authored
  window rows once. `Assets/Scripts/Editor/ResidentialHarvest.cs:1182-1200` already generates the
  whole struct, so this is one more field per unit and then a table lookup, never arithmetic.
* **Ruling (user, 2026-09-03): flats per landing = the number of ground-floor SHOP BAYS.** The
  building already tells you how wide it is at the street, and that count is measured, not
  guessed: `ResidentialUnit.ShopBays` (`Assets/RoadDemo/ResidentialUnits.cs:55`). Total flats =
  `ShopBays.Length × (storeys − 1)`, the ground floor being the shops and the entrance. A big
  corner building is then worth many times what a two-cell infill is, which is the point.

  Counted off the table, with `MaxH` for scale:

  | unit | bays | MaxH | footprint | flats at 5 storeys |
  |---|---|---|---|---|
  | residential-06 | 22 | 18.2 | 10×9 | 88 |
  | residential-03 | 20 | 18.6 | 9×5 | 80 |
  | residential-02 | 18 | 18.6 | 8×5 | 72 |
  | residential-01 | 12 | 15.6 | 5×4 | 48 |
  | residential-07 / -16 | 9 | 15.6 / 15.2 | 2×5 | 36 |
  | residential-08 / -10 / -15 | 7 | — | — | 28 |
  | residential-13 | 6 | 17.1 | 4×2 | 24 |
  | residential-11 / -12 | 5 | 15.6 / 15.2 | 3×2 | 20 |
  | residential-04 | 1 | 15.2 | 5×5 | 4 |
  | residential-05 | 0 | 16.7 | 4×9 | see below |

  **The one building with no shops:** `residential-05` has `ShopBays = 0` and is the only unit
  that does — and it is also the one with real harvested residential doors, `Doors = [1, 6, 1, 6]`
  (`ResidentialUnits.cs:46`). So the rule has an exact fallback and no hole: **bays if there are
  any, otherwise the harvested residential door count**. No unit in the table has both at zero.

  This also lands the sheet where the prototype drew it: a median building deals ~28–48 flats
  against the prototype's 60 cells, so the plan grid is a real grid. The width is not fixed at ten
  though — a landing runs from 1 to 22 doors — so the plan panel keeps the prototype's horizontal
  scroll and its door letters run past J.

## 8. The handoff itself

`blueprint.zip` (2026-09-03), kept in the repo under `Docs/design-briefs/blueprint-prototype/`:
`outfit-ledger-v2.html` — a working prototype of the whole screen, open it in a browser — and
`handoff-README.md`, the designer's own notes. The Blueprint lives in the `isBlueprint` section of the single
`Component` class; `BP_ROLES`, `BP_FLATS`, `BP_PLAN`, `bpPick`, `bpBuy`, `bpAssign`, `bpContents`
and `bpOwnedList` are the pieces worth reading. The `<x-dc>` wrapper and `data-dc-script`
attributes are the prototyping tool's scaffolding and mean nothing here.
