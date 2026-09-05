# The street event book — how the house learns a thing it did not know

*EPIC 40 (GAN-395), STREET-000..003. Written 2026-09-05. Code: `Assets/Scripts/Outfit/StreetEvents.cs`
(pure), `UI/EventCardHud.cs` (THE PHONE), `UI/ModalGate.cs`, the STREET TALK column on the
ledger's paper page, `HouseMind.AnswerTheCard`. Contracts: `gangsters_event_tests`.*

## What it is

A house cannot ask the street a question; the street tells it things. Every midnight each
house's **pots** are fed off its own view, and a full pot **deals a card**: one of our men rings
the house and says what he heard, with two or three numbered rows under it. The player sees it
at the six o'clock cut once the paper has closed; a rival's mind answers it before it walks its
tiers. Nobody ever talks to the Don — the card is spoken by a lieutenant or the desk, and a
house with nobody to bring the word gets no card.

## The pot, not a die

A def has a `Score(view, ctx)` in 0..1 and a `Threshold`. Each day the score is over the
threshold the pot gains `(s − t) / (1 − t)`; under it the pot keeps what it has; at 1.0
(`StreetEvents.Full`, 0.9995 against rounding) it fires and empties. Deterministic, monotone,
derived from the live books, never a timer: the same campaign fires the same card on the same
day headless and in the editor. STREET TALK says how full it is in words.

## Deal gates and hold reasons — `HoldReason`

A reason is a value, never a string, and every value has a `Line` (what is wrong) and a
`Clears` (what fixes it). Two classes:

| class | reasons | what happens |
|---|---|---|
| **deal gate** | `BossInCell`, `AtWar`, `NoSpeaker`, `NoMoney` (for the cheapest priced row), `Watched` (the QUIET gate) | the card is not dealt; the pot keeps filling; STREET TALK shows the gate, and every midnight a full pot stays shut the wire says `<CARD> waits. <gate> - <what clears it>.` |
| **hold reason** | `NoRoom`, `NoCrew`, and `NoMoney` when the safe drops after the deal | the card is dealt and waits PENDING three days, then expires Unanswered and the def cools |

A house can do something about a hold — rent a room, bring a crew home — which is why it is
dealt and shown; nothing a house does today opens a gate, which is why it is not.

## One book per house — `EventBook`

`Pending` (the light half a save keeps: card id, def, dealt day, expires day, speaker, hold),
`Spoken` (the card re-dealt from its day; memory only), `Pots`, `Fired`, `Cooling`, the last
twenty `Wire` lines, and the counts the yardstick prints (dealt / answered / expired). Saved
as `EventBookDto`, nullable on `HouseDto` — a file with none reads as an empty book, no version
bump.

A pending card is **re-dealt from its day** after a load: `Deal(view, ctx, seed)` is pure on
`(citySeed, gangId, def, dealtDay)`, so the same card comes back with the same words and the
same man.

## The one day pass — `StreetEvents.DayPass`

`DayPass(world, look, context, defs)` looks at every house — the player included — through the
`look` it is handed and rolls its book. Two callers, one sweep:

- the scene: `TerritoryRuntime.RunStreetEvents()`, called by `OutfitDirector` after the day
  tick and the flats, with the runtime's own `Look`;
- the paper city: `PaperCity.RollTheStreet(...)`, called by `UnderworldSim` at midnight.

There is no second sweep anywhere, so the yardstick and the editor deal the same card on the
same day. `StreetEvents.ContextFor(world, house)` builds the context both callers use.

## Answering

`Answer(book, card, choice, ctx)` runs the row's `OnChosen` (state on the house's own paper —
WALK AWAY cooling the broker), records the choice, clears the table, and hands back the row's
`HouseIntent`. The runtime carries that intent through the same `Carry` a mind's intents go
through (`HouseIntentKind.Card` → `Answered` → the inner intent), so **a card can never do what
a button cannot**. The player's HUD calls `TerritoryRuntime.CarryForPlayer`.

The mind (`HouseMind.AnswerTheCard`, before `Walk`, beside `Collect`): a pending card whose hold
is clear is answered with the highest-`Appeal` row the safe covers **with a week's wages left
after it** (D9, the row's `Upkeep` included); WALK AWAY is a row with an appeal of its own.
A held card is resolved by its reason: `NoRoom` → a `Lease` intent for a Stash room this
think; `NoMoney` / `NoCrew` → it waits inside its three days. Kilos in the room are sold the
think after they land.

## The modal gate — `ModalGate`

THE PHONE is a fourth modal. The eight name checks that used to list the others by hand read
one gate now: `PaperUp` (ledger, edition, phone — the screens that stop the clock),
`ScreenTaken` (plus the strategic map), `Blocked` (the arrest clock), `ClaimsEsc` (Esc belongs
to a paper screen this frame, its closing frame included), `OtherPaperUp(which)` (what a modal
checks before opening over another).

## THE PHONE and STREET TALK

The card is dealt at midnight and **shown at the six o'clock cut after the paper closes**
(`EventCardHud`, on `NewspaperHud`'s pattern: it owns the pause, sorting order 129). Keys 1–3
choose; **Esc holds** — nothing is decided and the card stays PENDING. Every row prints its cost,
who goes and its risk in words. STREET TALK is the last column of the paper's foot on today's
edition: the PENDING card with its hold and what clears it (and THE PHONE key that reopens it),
every signal of the def nearest its threshold with its state, the gate when shut, the pot in
words, the broker's door and its watch, whose the line is, and the last three wire lines. The
probe (`gangsters_connection_probe`) prints the same words. The def STREET TALK reads is the
last one that applies today - the test buy and the terms included, so their gates print too.

**The same lines run on THE WIRE strip of the street HUD.** `OutfitDirector.SweepStreetWire`
files every new line of the player's event book as an `IncidentKind.StreetTalk` slip (tag
"Street"), keyed by day and text, so the meeting that went well and the phone that does not
ring are read where the player actually looks, not only on the paper's foot.

## The bench lever - F3

The mini core's player is a lone Don, and a house with no lieutenant is told nothing; the
whole path costs about $86,000 and the street wants a name. **F3 in the ledger** puts the
house in the state the street wants - a lieutenant (it seeds two crews, twenty men, if there is none: sixty on one block bring the precinct and the WATCHED gate shuts the test buy),
$150,000 in the safe, our name in this morning's paper - and nothing else: the pot fills at
the next midnight and the man's card comes at the six o'clock cut, so what is watched is the
ordinary path. F2 alone seeds the men without the money or the name.

## Adding a def

A funeral, a petition, a rat, a judge — an `EventDef` with `Applies`, `Score`, `Signals`,
`Gate`, `Hold`, `Deal` (and `Fired` for a wire, `Expired` for what going unanswered costs),
appended to `EventId` and `CardId` so saved pots keep their numbers, registered in the defs
list the callers hand `DayPass`. Every row on its card needs a cost, a note and — where the def
has one — a risk in words: a number the player cannot read on screen is a defect.
