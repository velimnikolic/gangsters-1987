# The premises popup

The card that opens on a shop: what the place is, who holds it, what it is worth, and
every move that can be made against it. Built from the `popupstore.zip` handoff
("Premises Popup v2", Gangsters 1987 Ledger design system) on 2026-09-06.

It replaces a flat wall of ten same-weight keys, where TORCH IT stood beside GO TO THE
DOOR and nothing on the panel said which one burned a building down.

## Where it lives

One implementation, three surfaces:

| file | what it owns |
|---|---|
| `Assets/Scripts/UI/DoorMenu.cs` | the reading (`Door`, `TryRead`), the state, the filing |
| `Assets/Scripts/UI/DoorMenu.Card.cs` | **the card** - every measure and every row of paint |
| `Assets/Scripts/UI/CrewMissionPicker.cs` | who may be sent (the roster rule only) |

`DoorMenu.Open` is called by the ledger's block drawer
(`PersonnelAlmanac.BlockSheets.BuildDoorSheet`) and by `DoorMenu.Host`, which the turf
plate (`TurfMapPanel`) and the street (`CrewOverlay`) float over a shop. A row added to
the card appears on all three; a row added anywhere else is the bug the shared table
exists to prevent.

## The measures

The handoff draws the card 420 px wide and reads it at `zoom: 1.12`, and its own note
says to build at the read size rather than copy the zoom. So:

- **lengths**: `Px(designPx) = designPx * 1.12`. Card width 420 -> **470**.
- **type**: `LedgerStyle.FromPx(designPx * 1.12, optical)` - mono 0.831, condensed 0.864.
  A 9.8 px label is TMP **13.21**; a 21 px premises name is TMP **27.22**.
- **coordinate space**: the 1920x1080 ladder. That is the canvas `DoorMenu.Host` builds
  and the canvas the ledger book runs on - the two surfaces the card is painted on.

`Wide()` in the card measures a mono word HONESTLY: `len * print * 0.6 * optical` plus
tracking. IBM Plex Mono advances 0.6 em (verified against the TTF `hmtx`), and the face
is drawn at its optical, which `LedgerV2.MonoWidth` leaves out. The card lays out by
measurement - key, leader, note held to the right margin - so a width guessed 20% wide
drops notes onto a second line that had room on the first.

## What is on it

1. **File band** (chrome, 46): `PREMISES · FILE nnn-X` · `DAY n` · the ghost X.
   The file number is an FNV hash of the business id, so a shop keeps its number.
2. **Name row**: the premises in Oswald 21, `TRADE · PAYS WHOM` under it, and the
   tenure as a filled chip.
3. **Owner strip** (rail, 66): the portrait flush in the left edge, his name, what the
   deed makes him, and `NERVE` as six pips with the line that says what that nerve means
   at his door. A house's own front takes no nerve - nobody leans on a family's premises.
4. **Readings**: three leader rows - the week, `HEAT ON THE BLOCK` as six pips, and the
   asking price. Money prints exact and comma-grouped.
5. **The door's sentence**, wrapped.
6. **The dropdowns**, one open at a time, ordered by consequence.
7. **The confirm strip**, when a move that cannot be taken back is armed.
8. **The foot**: the money moves, any standing refusal, and the office's last word.

## How the game's rows are grouped

Every row still comes from `TerritoryRacketOrders.For`. The card only says which
dropdown it falls in (`BucketOf`):

| section | rows | summary |
|---|---|---|
| WHO GOES | crews, then men who can go alone | the crew that would go |
| AT THE DOOR | approach, demand, collect | `n MOVES · NO HEAT` |
| LEAN ON IT | threaten, beat, smash up, rob | `n MOVES · HEAT` |
| NO WAY BACK | torch, kill the owner | `n MOVES · FINAL` |
| OUR OWN DOOR | repairs, quarters, the hideout | `n MOVES · OURS` |
| the foot | buy it outright, sit on it | - |

Irreversible - and therefore two-step - are **beat, smash up, rob, torch, kill**.
`DoorMenu.TwoStepConfirm` is the handoff's own switch; off, they fire on one press.

## Where this deliberately departs from the handoff

Each of these is argued in a code comment. A divergence that is not one of these is a
bug.

1. **A fifth section, OUR OWN DOOR.** The handoff's florist is a stranger's shop. This
   game also offers repairs, quarters and the hideout on premises we hold, and a row
   that vanishes teaches the player nothing.
2. **SIT ON IT is a real order**, not the handoff's do-nothing exit: our men stand on
   his door (`OrderType.Guard`). It keeps the foot's second place.
3. **The cost notes are the shared table's own sentences**, not the handoff's invented
   `HEAT +2 · TAKE FALLS TO $210`. They say why a row is refused, which is the one thing
   a faded key cannot say for itself. They stay in sentence case; only LABELS are caps.
   A note too long to stand beside its key drops under it and wraps, up to two lines.
4. **The door's own sentence stays.** The handoff has no slot for it. It carries what we
   are worth to this man against what he wants (ECON-002/007), and a card that hides
   that is a card whose keys are guesses.
5. **The weekly figure's LABEL moves**: `TAKES, A WEEK` on a door that pays us,
   `WOULD PAY, A WEEK` on one that does not. Same table, same figure, two meanings.
6. **The tenure chip keeps the map's four words and inks** (OURS / PAYS US / THEIRS /
   OPEN) rather than the handoff's `Unclaimed`, because those are the city's own
   ownership vocabulary. An open door takes the design's amber, since the map's open
   grey cannot carry cream type.
7. **The carets are drawn, not typed.** U+25B6 / U+25BC are Geometric Shapes and NOT ONE
   face in `Assets/Fonts/Ledger1987` cuts them (verified by parsing every cmap), so a
   typed caret prints tofu. `LedgerKit.Caret` draws the triangle, the same ruling
   `LedgerKit.StepBar` was built under.
8. **A section head lifts to rail-trough on hover**, not to rail: the handoff's rule
   lightens a chrome row to rail, which leaves a head that is already rail with no
   feedback at all.
9. **The office's last word is set brighter than the handoff's log line**, so it is not
   read as one more standing refusal in the line above it.
10. **WHO GOES lists as many crews as the roster has**, and the men who can be sent
    alone under them - a demand or a threat can be put by one man in this game.

## Three things an adversarial pass caught, and what was done

1. **An armed move must be bound to the premises.** The state is static and three
   surfaces share it, so a row name alone would let TORCH IT armed on one shop come up
   already armed on the next shop opened - and the second premises would burn on one
   press. `armedDoor` and `armedDispatch` travel with `Armed`, and all three must match
   before a strip is drawn or a commit wired.
2. **Every paragraph height is `LineBox` off the size that PRINTS.** TMP drops a line
   WHOLE when its rect cannot hold it, and inside the ledger the printed size is 15%
   over the point size, so a box cut to the point size loses the line THERE and nowhere
   else. `Box(size, lines)` in the card is the one way heights are struck.
3. **WHO GOES is paged, five rows at a time.** The roster is not bounded; a house with
   thirty spare hoods would push the sections, the confirm strip and the foot off the
   bottom of the window, and a card whose commit key cannot be reached is worse than a
   short list. The `↑` / `↓` keys and an `n-m OF N` count sit at the foot of the body.
   (Paged rather than scrolled on purpose: the book already drives its drawer's scroll
   by hand off the raw wheel, and a `ScrollRect` nested inside it would move twice.)

## The bits that are still open

- The card itself does not scroll. Measured worst case - LEAN ON IT open with four rows,
  two of them wrapping, a confirm strip up and a refusal at the foot - is about 1030
  units against a 1080 canvas. It fits, with little to spare. Inside the ledger the
  drawer's own scroll carries it; on the map it does not, so a section grown past four
  rows wants checking against the window before it ships.
- The portrait is the real `PortraitStudio` bust, not the handoff's halftone plate.
