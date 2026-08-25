# Ledger 1987 fonts

Type the outfit ledger is set in. Loaded at runtime from `Resources/Ledger1987`
by `LivingCity.UI.LedgerStyle`, which builds dynamic TMP font assets from them -
no baked SDF assets to keep in sync.

| File | Role in the book | Licence |
|---|---|---|
| Lekton-Bold.ttf | the typewriter - headings, names, typed labels | OFL 1.1 (`OFL-Lekton.txt`) |
| IBMPlexMono-*.ttf | the ledger's figures and body copy | OFL 1.1 (`OFL-IBMPlexMono.txt`) |
| PTSerif-*.ttf | the newspaper's masthead and text | OFL 1.1 (`OFL-PTSerif.txt`) |
| Oswald-Bold.ttf | tabloid headlines, rubber stamps, label-maker tape, every display line the city's own screens print | OFL 1.1 (`OFL-Oswald.txt`) |
| Oswald-Regular.ttf | the running text of those screens - map marks, block tags, popup lines | OFL 1.1 (`OFL-Oswald.txt`) |

These are the whole game's type, not the book's alone: `DemoUi` (the top bar, the map,
the overlays) takes its two faces from `LedgerStyle` as well, so nothing on screen is
still wearing the Synty pack's 2010s screen gothics.

Each is the face the period actually set that job in: Lekton is drawn off Olivetti
office machines, Plex Mono is IBM's own, PT Serif is cut for newsprint, and Oswald
is the Alternate Gothic every headline deck of the era was set in.

## The faces they replaced

Still on disk, still licensed, and a one-line change in `LedgerStyle` away:
`SpecialElite-Regular.ttf` (Apache 2.0), `CourierPrime-*.ttf`, `OldStandard-*.ttf`
and `BarlowCondensed-Bold.ttf` (all OFL 1.1). Anything reinstated needs its
`optical` figure in `LedgerStyle.Font` set back to `1f` - see below.

## Optical size

A point size does not fix how big the letters come out; the drawing does. Special
Elite's caps fill 0.35 of its em, Lekton's 0.66, so the same `fontSize` sets a
heading nearly twice the height. Every size in the book was written against the old
faces, so each new one carries an `optical` figure in `LedgerStyle.Font` that scales
it back to the cap height it replaced. Swap a face and that figure has to be
remeasured, or the page silently resets itself.

## No stars

None of these cut a `★` (U+2605). The ledger's ratings are drawn as `Image`s for
exactly that reason - see `LedgerText`.
