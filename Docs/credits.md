# Credits: what the game owes, and to whom

The one place that records every third-party asset in the project whose licence asks
for a credit. Audio has its own provenance file (`Tools/audio/sources/SOURCES.md`,
and the summary in `Docs/audio-map.md`); this file is about the obligation rather
than the provenance, and it covers art as well as sound.

The Synty packs (`Assets/Synty/**`) are licensed, not credited - the Synty EULA asks
for no attribution, so nothing from them appears below. Everything else that is not
ours has to earn its row here before it is allowed into `Assets`.

## There is no credits screen yet

**This is the open item.** Nothing in the running game prints a credit anywhere. The
ledger book (`Assets/Scripts/UI/PersonnelAlmanac.cs`) has six tabs and none of them
is a colophon; the HUD, the map and the almanac carry no imprint.

Two placements were sketched and neither has been chosen:

- **An imprint block at the foot of THE PAPER.** A 1987 newspaper prints its own
  imprint, so the credit stays inside the fiction and costs one text block on a page
  that already exists. `PersonnelAlmanac.Newspaper.cs`.
- **A seventh tab, CREDITS.** More visible and it scales as the list grows, but it
  reads as a modern options screen inside a mob ledger, and it touches
  `LedgerPage`, `TabNames` and the four arrays of length 6 in `PersonnelAlmanac.cs`.

When that screen is built, every row in the table below has to appear in it. Until
then no asset that requires attribution should ship.

## What is owed

| Asset | In the game as | Author | Licence | Source |
| --- | --- | --- | --- | --- |
| DIRT BIKE OFF ROAD BIKE LOW POLY | the outfit's dirt bike, sold by the armoury at $900 (`ArmoryCatalog.Motorcycles`) | nabeelashrafphotography | **CC Attribution** | https://sketchfab.com/3d-models/dirt-bike-off-road-bike-low-poly-dbcca404b21348c69938207d265b6035 |

CC Attribution wants the title, the author, the source and the licence named
wherever the work is used, so a credits line for the bike has to carry all four -
the row above is written to be copied into it as it stands.

## What is not owed, and why it is worth writing down

The audio is attribution-free on purpose. The Sonniss bundle is royalty-free and the
Free Firearm Sound Library is CC0. The one file that ever needed crediting -
KuraiWolf's CC-BY light machine gun from OpenGameArt - was deleted the day the CC0
library replaced it, which is why the game currently owes nothing for sound.

The comment at the head of `Assets/RoadDemo/DemoSounds.cs` still says the gunshot
carries an attribution obligation. It is stale; `SOURCES.md` records the drop.

The UI chrome is CC0 (the Waste No Space set, `Assets/Scripts/UI/UiSkin.cs`) and owes
nothing either.

The type is all OFL 1.1 (Lekton, IBM Plex Mono, PT Serif, Oswald, and Silkscreen -
the tactical map's terminal face), which asks for the licence text to travel with
the fonts and for the reserved names not to be reused. Both hold: every licence
sits beside the files in `Assets/Fonts/Ledger1987/`, and nothing here is a
modified cut. So no credits row is owed for a face.
