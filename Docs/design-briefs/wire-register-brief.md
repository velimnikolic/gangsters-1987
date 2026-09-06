# THE WIRE — the register

The ledger's second tab. Everything that has come in since the first morning, kept the
way an accounts book is kept: one ruled line per slip, newest day at the top, a band
over each day that adds it up, and the whole dispatch printed on a telex slip at the
foot of the page when a line is drawn.

Built 2026-09-06 from the design handoff `design_handoff_wire_register`
(`The Wire - Register.dc.html` + README). It replaced the card list that paged 24 rows
at a time and scrolled inside its own page at the same time.

## The one rule

**Nothing on this surface composes a sentence.** `IncidentText` wrote a man's line the
day it happened and `TerritoryStandingVocabulary` wrote the door's; the paper, the strip
over the street and this tab all print the same words. A register line is one line, so a
long body is CLIPPED with an ellipsis — typesetting — and the slip at the foot prints it
whole. The only words this page writes are counts of what is in front of the reader, and
`WireRegister` writes them once.

## What is on the sheet

The sheet is 1590 × 980 of the book's 1920 × 1080 canvas; the content column is 1542 at
x 24. Top to bottom: page head 58, filter strip 32, the register block (column head 28 +
viewport), the slip strip 178, the footer 38. The register is 1494 wide with the day rail
34 beside it.

| Column | x | Width | What |
| --- | --- | --- | --- |
| DAY | 0 | 100 | `45`, or `45 · 16:57` — the clock only ever comes off a door slip |
| PEN | 100 | 26 | severe 13 filled, notable 10 filled, routine 7 hollow |
| TAG | 126 | 146 | the tag the book wrote, in the pen's colour |
| DISPATCH — AS FILED | 272 | 826 | the frozen body, one line, clipped |
| HEAT | 1098 | 76 | `+3 HEAT`, in the pen's colour |
| MONEY | 1174 | 92 | `$1,200`, exact, in ink — or in the pen when that pen is red or amber |
| SOURCE | 1266 | 118 | the quarter, or THE RACKET — printed only when it CHANGES |
| FILE | 1384 | 110 | what the line opens; `› ` under the pointer |

Hairlines stand at every column boundary and run the full height of the viewport, and the
ruling continues under the last entry to the foot of the page: the page is pre-ruled.

**A day band** (34) carries `DAY 31` in the condensed gothic and the day's own arithmetic
— entries, the split between the two books, heat drawn, and money TAKEN, which is summed
only off the two answers that are money coming in (`He pays`, `Banked`). A day holding
both books is split by a **run divider** (22) naming each: they are counted on different
clocks, so nothing pretends to interleave them.

**The slip strip** prints the drawn line on the book's telex slip — the whole body at
14px, the leader rows (FILED, BOOK, TAG, POLICE ATTENTION, AT THE DOOR / ON THE BOOKS),
and the destination key. With nothing drawn it prints the scope tally instead; that is a
state of its own, not a leftover.

## Narrowing

BOOK (both / our men / our doors), PEN (five swatches, multi-select, ALL clears), SOURCE,
FIND (substring over tag, body, origin and stamp), and one isolated day. The day is
isolated by DOUBLE-CLICKING its tick on the rail — there is no separate day control. The
rules live on `WireNarrow` in `WireRegister.cs`, not on the strip that draws them.

## One navigation model

A single continuous scroll. The wheel scrolls it, `↑`/`↓` walk the selection line by line
and draw each slip (scrolling only far enough to keep the line under the reader's eye),
PageUp/PageDown/Space scroll by a viewport, Home/End go to the ends, and the day rail and
the two day keys are JUMPS inside that one scroll. There are no pages.

A slip landing while the reader is at day one does not throw the page back to the top:
the entries that came in over the head are counted and offered in the blue notice band,
and the reader's own line is put back under his eye (`Collect` takes an anchor, and
`RestoreAnchor` puts it back).

## Source map

- `Assets/Scripts/UI/WireBook.cs` — the two books, dressed as slips. Now also carries the
  line's WEIGHT (severe / notable / routine, derived from the kind — never from the pen),
  which book filed it, heat and money as SEPARATE figures, whether the money came in, and
  the origin without the wire's own prefix.
- `Assets/Scripts/UI/WireRegister.cs` — the whole arithmetic, engine-free: narrowing, the
  flat item list with a y on every band, divider and line, per-day counts, the scope
  tally, and where a day stands in the scroll.
- `Assets/Scripts/UI/WireSheet*.cs` — the page. `WireSheet.cs` is state and input,
  `.Chrome.cs` the head, strip and footer, `.Register.cs` the ruled register and the rail.
- `Assets/Scripts/UI/WireSlip.cs` — the foot of the page, its own object.
- `Assets/Scripts/UI/WireHit.cs` — the pointer's half of a pooled view.
- `Assets/Scripts/Tests/WireRegisterTests.cs` — the bench (`gangsters_wire_tests`).

## Traps this build closed, and would cost again

- **The page is built once and only BOUND.** Two thousand slips are laid out as y offsets
  and only the window around the scroll (± 120) is given views out of a pool. The old tab
  destroyed and rebuilt every child on each repaint, which is why it could only afford 24
  rows and a second navigation model to reach the rest.
- **A caret owns the alphabet.** FIND is the book's second typed field, and `P` closes the
  book: `PersonnelAlmanac.Update` hands the keys to the field while `wireSheet.Typing`,
  and Esc gives them back before it closes anything.
- **A dead file is read when the slip is DRAWN, not when the key is pressed.**
  `WireTargetTrouble` answers in the words the slip prints, the destination key greys, and
  the reason stands beside it under a NO FILE stamp.
- **A design handoff's px is a size in the design system's faces.** Every point size here
  is `LedgerStyle.FromPx(px, optical)`; `LedgerKit.BookSize`'s lift for print under 15 then
  applies on top, as on every other sheet.
- **`AddComponent<Image>` on a rect that already has one returns null.** Every click
  surface goes through `LedgerKit.ClickSurface`.

## What the adversarial review caught (2026-09-06)

Four findings, three fixed:

- **Escape went past the typing guard.** The event system runs before the book, and TMP
  deactivates FIND on Escape itself - so a guard asking only `isFocused` saw a field that
  had just let go and passed the same Escape to the key that closes the ledger.
  `WireSheet.Typing` now stays true for the frame the field let go in, and
  `restoreOriginalTextOnEscape` is off so Escape releases the caret without taking the
  reader's word back.
- **The day keys stalled short of the oldest day.** The last bands can never stand at the
  top of the viewport, so a reading day taken off the scroll stuck a few days short while
  OLDER DAY stayed lit. A jump is now REMEMBERED (`reading`), the wheel clears it, and the
  footer, the rail's current tick and both keys all read `ReadingDay`.
- **The held notice counted arrivals the scope does not print.** It counted the reader's
  place in the WHOLE archive, so a door slip landing under OUR MEN offered him an entry
  his own register would not show. `HoldArrivals` now counts his place in the narrowed
  run, and a scope change retires the notice.

Not changed, and why: the review also read the scope tally's pen rows (BLOOD AND LOSS,
HANDS LAID ON, ...) as claims about event categories, since the ballpoint also carries a
man gone over and the amber a complaint rung. Those five phrases are the handoff's own
words for the five PENS, taken from `WireBook.InkOf`'s own description of what each pen
is for, and each row prints its swatch beside it. The rail's red share is red AND blue by
the design's own rule. Renaming them is a wording decision for the design, not a defect to
fix in the page.

## Two things the first Play capture measured (2026-09-06)

- **The page paints its own paper.** The book's sheet is `LedgerStyle.Ground` `#ebe3da`;
  the design gives this tab `#f4efe9`, and every fill on the register is read against it -
  banded line a shade darker, severe darker again, picked warmer. Left on the book's own
  ground the ladder INVERTS (the banded line comes out lighter than the sheet under it and
  the severe line vanishes into it). Measured off the capture: the register's ground came
  back `(235,227,218)` where the design says `(244,239,233)`.
- **The book now fits the canvas it is given.** The capture's canvas rect was 1484.8 x
  927.9 units against the book's 1920 x 1080 frame, so the whole book was cropped: the
  FILE column off the right, the footer off the foot, the chrome off the top.
  `PersonnelAlmanac.FitPage` scales the page about the canvas centre to fit (never up),
  read at build and every turn. Nothing is re-laid; the sheet prints smaller.

## Deliberate divergences from the handoff

1. **The scope readout and CLEAR SCOPE stand in the head's right margin**, not at the end
   of the filter strip. The strip's controls are set in the BOOK's faces, which print
   larger than the design's screen faces; the readout squeezed in beside them would be an
   ellipsis by the second facet, and the head's right half is empty.
2. **A rail tick may print thinner than the design's 9-unit floor.** The floor is a
   ceiling here as well: a campaign of four hundred days must still stand on one rail, so
   a long book prints thinner ticks rather than running off the foot of the page.
3. **The design's `DRAW STATE` review buttons are not implemented** — they are a design
   tool's affordance, not game furniture.

## Proof

```text
python3 Tools/project.py compile
unity command gangsters_wire_tests --json
```

The bench proves the order (newest day first, both runs under one day), the day's own
arithmetic including money that must NOT be counted (a short envelope is not takings),
the register practice of printing an origin only when it changes, every narrowing, that a
quiet day keeps its tick on the rail, and that the day keys stop at the ends.

Play checks a bench cannot make: the wheel and the arrow keys over a long archive, the
held-entries notice while reading day one as a slip lands, a dead target's NO FILE, and
the two empty states (NIL RETURN with an empty wire, NO MATCH under a scope).
