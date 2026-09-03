# Handoff: Blueprint — Apartment Building Screen (Outfit Ledger)

## Overview
"Blueprint" is a screen inside a 1920×1080 gangster-outfit management app ("Outfit Ledger"). It shows a 60-unit + entrance apartment building the player's outfit owns/is buying into, floor by floor, and lets the player buy flats, assign them a criminal "role" (armory, cash stash, safehouse, infirmary, garage, card room, brothel), assign one of their own men as "keeper", and review what's going on inside each flat. It now opens as a **popup/modal over whatever tab is currently showing**, rather than being its own tab page.

## About the Design Files
The bundled file (`Outfit Ledger v2.dc.html`) is a **design reference built in HTML** (a "Design Component" from the prototyping tool used to make it — ignore the `<x-dc>` / `data-dc-script` wrapper markup, that's tooling scaffolding, not app architecture). It is a high-fidelity, fully-interactive prototype: real click behavior, state transitions, sorting, etc., all in inline styles and vanilla-ish JS inside one file. **Do not ship this file or its markup as-is.** The task is to recreate this screen's look and interactions in the target codebase's real stack (React/Vue/native/whatever the project already uses), using that codebase's existing component library, state management, and styling conventions. If no codebase exists yet, React is a reasonable default given the component-driven structure of this prototype.

## Fidelity
**High-fidelity.** Colors, spacing, typography, copy, and interaction states are final/intentional, not placeholders. Recreate pixel-close using the values below.

## Where it lives in the app
- The app has a top tab bar: THE PAPER · ORGANIZATION · BLOCKS · BLUEPRINT · FINANCES · ARMORY · FAMILIES.
- Clicking **BLUEPRINT** does NOT switch the underlying page — it opens the whole Blueprint screen as a full-screen popup/modal on top of whichever tab is currently showing. Clicking the dark backdrop or the ✕ in the top-right closes it and reveals the tab underneath again. The BLUEPRINT tab itself stays highlighted (active-red) while the popup is open, matching the highlight style used for a truly-selected tab.
- On first load, the underlying tab is "THE PAPER" and the Blueprint popup is open by default.

## Screens / Views

### 1. Blueprint main screen (popup)
**Purpose:** browse the whole building, see which flats are ours, open a flat's detail/edit popup, review a plain list of everything we own.

**Layout:**
- Full-viewport dark backdrop (`oklch(0.16 0.01 55 / 0.66)`), centers a light sheet (`oklch(0.97 0.008 80)` background, max-width 1400px, drop shadow `0 10px 44px oklch(0.14 0.02 55 / 0.55)`), padding `14px 24px 30px`, scrolls vertically if content overflows the viewport.
- Header row: "BLUEPRINT" title (27px/700) + address subline "318 FOURTH ST. · SIX FLOORS · TEN DOORS TO A LANDING" (11px mono, muted) on the left; a row of 5 quick-fact chips on the right (ON OUR DEED, OPEN, DARK, SHUT, HEAT), each a label/value pair. Border-bottom 3px solid dark ink under this header.
- ✕ close button: 30×30px, dark square (`oklch(0.24 0.012 55)`), absolute top-right of the sheet; hover turns red (`oklch(0.42 0.15 25)`).
- **The Building plan**: a horizontally-scrollable panel (min-width ~1080px content) with:
  - Sub-header: "THE BUILDING" title (19px/700) + helper text "click a door · hover reads the name off it · the form on the right is the clerk's paper", border-bottom.
  - Column headers A–J (one per unit slot on a floor), 10.5px mono bold.
  - Six floor rows, top to bottom: 5, 4, 3, 2, 1, GROUND (labelled "GRD"/"SHOPS" on ground floor). Each row = a big floor-number cell (52–56px wide) + 10 unit tiles + a floor-summary column (OURS n/10, OPEN/DARK/SHUT counts, HEAT n/DAY).
  - Each unit tile (~92×64px, padded, min-height ~86px): door-number badge (dark chip, red if selected), a state-color square, a role/tenant/hall line, and either half-star fit rating, a stamp (DARK / RAID ### / NO BANK), or a plain tag (OPEN/EMPTY/TENANT/COMMON). Hall/entrance cells render with a diagonal hatch pattern and are not clickable. Tiles are clickable (opens the flat's own popup) except hall cells.
  - Caption bar under the plan: shows the hovered/selected door's name, one-line summary, and state, with a colored state dot.
  - Legend row: 5 color-coded chips (OPEN·EARNING, DARK·NO KEEPER, CLOSED·NO BANK, RAIDED·SEALED, HATCHED·NOT OURS).
- **"OUR FLATS" list** below the plan, full width, not horizontally scrolled:
  - Header row background `oklch(0.19 0.012 55)`, 7 columns: door (56px) · ROLE (150px, sortable) · NAME (flexible, sortable) · KEEPER (150px) · EARN (90px, right) · HEAT (90px, right) · STATUS (130px, right).
  - Clicking the **ROLE** or **NAME** header cell sorts the list alphabetically by that field; clicking again reverses the sort. The active sort column shows a ▲ (ascending) or ▼ (descending) arrow appended to its label. Header cells are pointer-cursor and lighten on hover.
  - Rows are one per flat the player owns, clickable (opens that flat's popup), light hover highlight. A flat with no role assigned shows "NO ROLE" in red in the ROLE cell so it's easy to spot.

### 2. Flat popup (opens on top of the Blueprint popup, same treatment: dark backdrop, centered sheet, ✕ close, click-outside closes)
Sheet max-width 1010px, dark header bar (`oklch(0.19 0.012 55)`) with:
- Small mono label "PREMISES FORM · 318 FOURTH ST."
- Big title: "FLAT {door}" / "SHOP {door}" / "ENTRANCE" (27px/700, light text)
- Subline: floor + door wording, e.g. "FIFTH FLOOR · DOOR A"
- A state dot + label (e.g. "OPEN", "DARK", "RAIDED — CLOSED UNTIL DAY 214")
- ✕ close (same style as the main popup's)

Two-column body (50/50 split, divider between):

**Left column, always:**
1. "1 · THE DEED" — big price (or "ON OUR DEED" in green once bought) + a note line (e.g. "cash, at the table, the day it is signed").
2. "2 · THE NAME ON THE DOOR" — editable text input once owned (underline style, 16px/600 mono); locked/greyed display of the existing tenant/occupant name before purchase.

**Left column, mode-dependent (see Interactions below for the three modes):**
3a. **Buy mode** ("3 · WHAT RUNS OUT OF IT" placeholder): italic note explaining nothing is set until bought, followed by a one-line preview of what every role type earns per day (so the buyer can see potential income before committing).
3b. **Detail mode**: big role name (24px/700) + two stat blocks side by side — "FIT-OUT PAID" and "HEAT WHILE OPEN" (color-coded: red ≥3, amber >0, green 0).
3c. **Edit mode**: a 5-column role picker table — mark / ROLE / EARN / FIT-OUT / HEAT — one row per role type (armory, cash stash, safehouse, infirmary, garage, card room, brothel), each row clickable to pick, showing cost, daily heat, and daily earn potential for that role.

**Right column, mode-dependent:**
- **"WHAT'S IN THE ROOM"** panel (shown whenever a role is actually assigned, in both detail and edit mode) — light tan background block listing role-specific facts, e.g.:
  - Armory: guns racked, cash on hand, last counted (days ago)
  - Cash stash: cash held, last counted
  - Safehouse: who's hiding there (1–3 names), food & doctor status
  - Infirmary: who's under care (or "empty this week"), supplies status
  - Garage: cars on the floor, plate status
  - Card room: bank amount, tables running (of 4), last night's take
  - Brothel: girls working, take last night, house cut split
  - Plus a top-right earn indicator, e.g. "$950/DAY WHILE OPEN" in green, or "NO DIRECT TAKE — JUST HOLDS IT" in muted grey for storage-only roles.
- **Detail mode, "4 · WHO KEEPS IT"**: big keeper name (18px/700) + rank/duty subline, half-star fit rating on the right; if no keeper, a red "NO KEEPER — THE FLAT READS DARK" line. Always followed by the italic footnote: *"A keeper is off the street. Pull him into a crew and the flat goes dark that moment."*
- **Edit mode, "4 · WHO KEEPS IT"**: a scrollable (max-height 322px) 4-column table — mark / OUR MEN / FIT / STANDING — one row per available man, sorted with the currently-assigned keeper first, each showing name, rank + current post, half-star fit-for-this-role rating, and a status note ("KEEPER", "keeps {other door}", "jailed", "hurt", blank). Rows for men who are elsewhere/jailed are greyed and unclickable. Same footnote below.
- **Buy mode**: a short italic note: "Buy the deed and this same paper reopens straight to the role and keeper — one form, no second trip."

**Footer bar** (light tan, border-top):
- A row of up to 3 bill facts: DEED (paid/owed), FIT-OUT DUE NOW, HEAT WHILE OPEN.
- Primary action button (flex-grow, dark, 13px/600 mono uppercase):
  - Buy mode: "BUY $X" → buys the flat.
  - Detail mode: "EDIT ROLE & KEEPER" → switches into edit mode.
  - Edit mode: "SAVE" (enabled only once something changed) → commits role/keeper/name changes and returns to detail mode.
- Secondary buttons (narrower, dark unless red):
  - "CANCEL" (edit mode only, appears only if a role already existed before editing) → discards the draft and returns to detail mode without saving.
  - "PULL HIM OUT" (red when enabled) → removes the current keeper immediately (flat goes dark), available whenever a keeper is assigned.
- A one-line contextual reason/status message under the buttons (color-coded — amber for warnings, red for blockers, muted/plain for informational), e.g. "No keeper named. The flat reads dark and takes nothing in."

## Interactions & Behavior
- **Opening flow**: clicking an unowned flat opens the flat popup in **Buy mode** (deed info + preview of role earnings only — no role/keeper controls yet). Buying (clicking "BUY $X") marks the flat owned and, without closing the popup, the same popup immediately reflects the new owned state. Because the flat has no role yet, it lands directly in **Edit mode** (role + keeper pickers), skipping Detail mode until a role is actually saved.
- Clicking an **already-owned flat with a role set** opens straight into **Detail mode** (read-only rich info: role, keeper, contents) rather than the raw pickers — this was a deliberate change from an earlier iteration where opening any owned flat showed editable pickers immediately.
- **Detail → Edit**: the "EDIT ROLE & KEEPER" button switches to Edit mode, seeding the pickers with the flat's current role/keeper as already-selected.
- **Edit → Detail**: "SAVE" commits and returns to Detail mode (does not close the popup). "CANCEL" discards drafts and returns to Detail mode.
- **Keeper constraint**: a man already keeping another flat cannot be picked for this one (shown as blocked/greyed with a "keeps {door}" note) unless he's freed first via "PULL HIM OUT" on his current flat.
- **Role constraint**: a keeper cannot be picked before a role is chosen; the reason line explains this.
- **Card room** roles additionally require money "in the bank" (a separate resource) before they're considered open/earning — shown as "CLOSED — BANK EMPTY" otherwise.
- **Raided** flats show "RAIDED — CLOSED UNTIL DAY {n}" and stay shut regardless of keeper.
- Every committed action (buy, save role/keeper, pull keeper) files a plain-English order/log entry elsewhere in the app (an "Orders" ledger) describing what happened and the financial/heat consequence — this logging pattern should be preserved architecturally (a single action-log/audit-trail service that every mutating action reports to).
- **Sorting**: clicking the ROLE or NAME column header in the "OUR FLATS" list sorts ascending; a second click on the same header reverses to descending; sorting by one column clears any indicator on the other. This is a simple client-side alphabetical sort — no persistence needed beyond the current session.
- **Popup nesting/dismissal**: clicking the dark backdrop OR the ✕ closes the topmost popup only (flat popup closes without closing the Blueprint popup underneath it); clicking inside the sheet itself must not propagate to the backdrop's close handler.
- No animations/transitions are used for popup open/close in this prototype — instant show/hide. Adding a simple fade/scale-in is reasonable but not specified.

## State Management
Minimum state needed to reproduce this screen:
- Which underlying app tab is active (independent from whether the Blueprint popup is open).
- Whether the Blueprint popup is open (`bpScreenOpen` boolean).
- The full list of flats (60 units + entrance), each with: door id, floor, slot letter, `own` (bool), `role` (enum or null), `name` (free text, player-typed), `keeper` (man id or null), `ask` (purchase price, for unowned flats), `occupant`/tenant name (for unowned flats), optional `bank` (number, card-room only), optional `raidUntil` (day number).
- Which flat is currently selected/open in its own popup, plus **draft** values for role/keeper/name being edited (kept separate from the committed flat data until "SAVE").
- Whether the currently-open flat's popup is in edit vs. detail mode (derived: edit mode if the flat has no role yet, or the player explicitly clicked "Edit").
- Current sort key/direction for the "OUR FLATS" list (role | name | none, asc/desc).
- The roster of "our men" (with id, name, rank, current duty/post, condition — fit/hurt/jailed — used to compute who's blocked from being picked as keeper elsewhere).
- A per-man "fit" rating function: how good a given man is at a given role (drives the half-star display) — in the prototype this is a deterministic hash-based stub; a real implementation should probably be a designed stat.
- A deterministic-per-flat "contents" generator for the "WHAT'S IN THE ROOM" panel (guns/cash/hidden people/etc.) — in the prototype this is derived from a hash of the door id purely for visual variety; in production this should likely be real simulated/tracked data (actual cash counters, actual NPCs hiding, etc.) rather than a display-only stub.

## Design Tokens

**Colors:**
- Ink (primary text): `oklch(0.2 0.01 55)`
- Muted text: `oklch(0.48 0.02 55)`
- Dim/disabled: `oklch(0.72 0.02 60)`
- Red (danger/selected/raided): `oklch(0.5 0.16 25)`
- Amber (warning/heat): `oklch(0.58 0.13 75)`
- Green (good/open/earning): `oklch(0.45 0.13 145)`
- Paper blue (dark/no-keeper state): `oklch(0.38 0.03 250)`
- Dark chrome (headers, badges, tab bar): `oklch(0.19 0.012 55)` / `oklch(0.24 0.012 55)`
- Sheet/paper background: `oklch(0.965 0.01 72)` (flat popup), `oklch(0.97 0.008 80)` (main Blueprint sheet)
- Selected-row highlight: `oklch(0.93 0.03 84)`
- Hairline borders: `oklch(0.86–0.88 0.015 62)`
- Backdrop: `oklch(0.16 0.01 55 / 0.66)`
- Standard shadow ("SH_DARK"): `0 1px 3px oklch(0.15 0.02 55 / 0.4), 0 3px 9px oklch(0.15 0.02 55 / 0.22)`
- Popup shadow: `0 10px 44px oklch(0.14 0.02 55 / 0.55)`

**Typography:**
- Mono (labels, numbers, badges, buttons): `'IBM Plex Mono', monospace` — sizes range 9px (micro labels) to 15px (keeper name in detail view), weight 600 for emphasis.
- Serif display (titles, big numbers): system default sans at 24–29px/700 for role/price headlines; the app's title uses plain sans 27px/700.
- Italic serif (Georgia) for footnotes/explanatory copy, 13–14px, line-height 1.5–1.65.
- Letter-spacing: 0.04–0.18em on mono labels depending on size (tighter labels get more spacing).

**Spacing/sizing:**
- Popup sheet padding: 14px 20–24px 16–30px.
- Grid gaps in tables: 10–12px.
- Standard row padding: 7–9px vertical, 10–12px horizontal.
- Unit tile: ~92×64px min, ~86px min-height with 8px padding.
- Close button: 28–30px square.
- Border radius: **none used anywhere** — the whole design is hard-edged/paper-like, no rounded corners.

## Assets
No image assets — everything is CSS-drawn (colored squares for state dots, a repeating-linear-gradient hatch pattern for hall/not-ours cells, Unicode ★ characters for star ratings).

## Files
- `Outfit Ledger v2.dc.html` — the full app prototype (all tabs). The Blueprint feature described above is the `isBlueprint` section plus its `blueprintVals()` logic and related state (`bp*` prefixed state keys and methods) inside the single `Component` class. Search for `BP_ROLES`, `BP_FLATS`, `bpPick`, `bpBuy`, `bpAssign`, `bpContents`, `bpOwnedList` to find the relevant logic quickly.
