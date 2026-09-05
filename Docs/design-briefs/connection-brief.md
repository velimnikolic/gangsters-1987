# The Connection — street events, and the man who knows the Colombian (EPIC 40, draft 4)

Design brief, written 2026-09-05 and **reworked the same day** after the contrarian pass and the
user's rulings. Linear: EPIC 40, GAN-395, label **Trade**; tickets `PRE-001..002` (preconditions
the epic cannot start without) = GAN-396..397, `STREET-000..003` (the street event system) =
GAN-398..401, `CONN-001..006` (the connection) = GAN-402..407.

Whoever implements a ticket of this epic moves that ticket, and when the last one lands the
epic itself, to **Done** in Linear.

The user's words: "kako da napravimo da kao nađeš konekciju za uvoz droge iz Kolumbije";
"možda da imamo neki event sistem kao popups, ali kako da se odredi da smo spremni za drug
import (kako igra da to zna)"; "s bosom ne priča niko direktno no neki lieutenant nam javi šta
je čuo"; and the ruling that turned the first draft round: the connection to the port is not a
block the house holds, it is a **man** the house finds and signs.

## 0. What the first draft got wrong, in one paragraph

Draft 1 made the port a block ("GROUND": hold a block that touches the harbour), rolled a daily
die above a threshold, dealt the card in `CampaignRunner.DayTick`, and let rivals reach the
Colombian through a flat they cannot buy. The contrarian showed that no `HouseView` is ever built
for the player (`RoadDemo/TerritoryRuntime.Minds.cs:475`, three callers, all skip `IsPlayer`),
that `Apartments.Buy` has one caller and it is the player's blueprint form
(`UI/PersonnelAlmanac.Blueprint.Form.cs:788`), that the rig the user tests in has no harbour
(`RoadDemo/CoreDemoBuilder.cs`, no `DistrictKind`), that the day tick is midnight
(`Outfit/Underworld.cs:335`) and the paper is at six (`News/Edition.cs:9`), and that a die
between "ready" and "fired" is where legibility dies. Draft 2 keeps the event system's shape and
rebuilds the connection around a person.

Draft 3 applies the user's correction (2026-09-05): "ali ako nas je on samo povezao sto bi
stala isporuka kad ga izgubim?" The man opens the relationship. Accepting Supplier terms makes
that relationship the house's; losing the introducer afterward does not interrupt deliveries
or undo the relationship.

Draft 4 (2026-09-05, the user's review of the filed plan, each point verified in code): the
headless view has no attention (`Tests/PaperCity.cs:355`, `AttentionLook = blockId => 0f`), so
QUIET became a deal gate instead of a weight the yardstick could never tune; the die that
ruling 4 removed from the trigger had returned at the outcome, so the sting now reads the
watch on the broker's door; `Apartments` had no vacant-unit enumeration for a mind
(`Property/Apartments.cs:134-175`), so PRE-001 adds one and the rule for the unit; STREET-003
said both "a mind never holds a card past the next think" and "the card holds", so the hold is
now three explicit rules; with `DaysToCourt = 1` THE CELL fires on release; the day pass is one
shared `DayPass` with two callers; Pablo's man is a hidden turn with a late-bound id; the
yardstick prints money per house per week. And ruling 16: **everything the epic computes is
explained in the UI.** The two open questions of §10 were ruled the same day: the Trafficking
band is "as in real life" (15–30, §5.5) and the port and the county field get **two cards**,
written out in full (§5.2a).

## 1. Rulings (2026-09-05)

Marked **[user]** where the user ruled in words, **[user, via the contrarian chat]** where the
ruling came through the user's exchange with the contrarian agent and should be confirmed with a
glance, and **[user, "predlozi"]** where the user took this draft's proposal as written (2026-09-05).

1. **The connection is a man.** [user, via the contrarian chat] A Cuban, a docker, a fisherman,
   a man off the county field — a `Character` the house must find along a path and sign. No block
   is required. The port as geography belongs to EPIC 41, when loads physically land.
2. **He need not be a lieutenant; he must stand in a lieutenant's crew.** [user] "ne mora samo
   da je konektovan s poručnikom." The lieutenant whose crew he stands in is the voice of every
   card about him and the default man for the meeting.
3. **Nobody talks to the Don.** [user] Every card is spoken by one of our men: the lieutenant
   who heard it, or the desk. The broker never rings the Don; Sal rings and says what Costa said.
   A house with no lieutenant has nobody to bring the word, and gets no card.
4. **Readiness is a score, not a switch, and the trigger is accumulation, not a die.**
   [user] Signals off the live books fill a pot day by day above
   a threshold; the event fires when the pot is full. Deterministic, monotone, derived from live
   state, never a timer. The weights and thresholds are first guesses; **rule on them after the
   probe** [user].
5. **A card is never dealt to a house that cannot answer it.** [user, draft 1 unopposed] Per
   card: the money for its cheapest choice, a free crew if it needs one, a room if it needs one,
   the Boss not in a cell, no war. Held cards expire after three days. Refined in draft 4
   (§4 STREET-001): money, the Boss, war, the speaker and the watch **gate the deal**; a room
   and a free crew **hold a dealt card**, so the player reads what to fix and a mind can fix it.
6. **Esc holds, it does not decide.** [user, "predlozi"] The card stays PENDING on the ledger's front
   page for its three days.
7. **Every house gets the same events, through the same gateways.** [user, via the contrarian
   chat: the AI may buy flats] Which is why PRE-001 exists.
8. **Two grades of connection: Broker and Direct.** [user, via the contrarian chat] Every house
   can reach a Broker through its paths. Exactly one man in the city carries the Direct line
   (Pablo's man): drawn hidden at `Underworld.Deal`, kept on `UnderworldDto`, learnt only by the
   house that signs him. Direct = lower price per kilo, credit, a bigger load.
9. **Every city has a harbour; the county field is a second, thinner line.** [user] A man's
   background says which: port men (Docker, Sailor, Fisherman) carry the port terms; field men
   (Baggage, Mechanic-at-the-field, Pilot) carry the airport terms with a smaller minimum load.
   Ruled 2026-09-05 ("treba dva, što detaljnije"): **two cards**, `PortMan` and `FieldMan`,
   one pot, the line drawn Port 3 : Field 1 at the firing and saved on the house
   (`ConnectionDto.line`); the line sets `MinLoad`, the broker's door and every wire's words
   (§5.2a). EPIC 41 makes it a ship and a plane.
10. **A stash raid seizes and seals, no docket.** [user, via the contrarian chat] Only a sting on
    the street opens a Trafficking case. The flat raid line stays what it is today
    (`Gameplay/OutfitDirector.cs:390-421`, `RosterOps.Jail`, no `CourtCase`). The band is
    "as in real life" [user, 2026-09-05]: 15–30, a mandatory minimum that binds hoods and
    lawyers alike (§5.5).
11. **Trafficking may be the Don's life sentence** [user: "može"], **but not in this epic**
    [user, "predlozi"]. Nothing today puts the Boss on a docket; the mechanism is a
    conspiracy case (kilos seized on a deed in the house's name AND a witness who talks, through
    EPIC 26's witness pressure). Named as a seam in §9, ticketed in EPIC 41 or on its own.
12. **The epic ends at Supplier with a repeatable paper load.** [user, "predlozi"] A load of `MinLoad`
    kilos lands in the Stash on `NextLoadDay` at the terms price and sells flat, so trust, heat
    and the raid are live and the Supplier row is not dead until EPIC 41.
13. **The man introduces; the established supplier relationship belongs to the house.**
    [user, corrected 2026-09-05] Before Supplier terms are accepted, losing him (killed, jailed,
    defected or walked out) holds introduction-dependent progression; the existing 14-day
    one-stage cooling and replacement rule applies only to this unfinished introduction.
    Accepting Supplier terms establishes the relationship. After that, his absence never
    holds deliveries, sales or supplier cards, reduces trust, drops a stage, or removes the
    agreed Broker / Direct terms. The house needs no replacement introducer. Money, stash,
    trust, raids and Burned still follow their own rules. No ongoing transport, credit-guarantor
    or secret-channel role is assigned to the introducer in this epic.
14. **Pablo's man unsigned moves on.** [user, "predlozi"] If his card expires or the house cannot pay,
    he is re-dealt to the next house whose path fires, once per 30 days.
15. **EPIC 40** [user]; the label **Trade**.
16. **Everything this epic computes is explained in the UI.** [user, 2026-09-05: "dodaj pravilo
    da sve to mora da se objasni u ui"] Every signal and its state, the pot in words, every gate
    and hold reason and what clears it, the watch on a door and the sting risk it makes, the
    terms and why, trust and every change to it with its reason, the next load day, why a stage
    dropped, and whose the line is (the introducer's until Supplier, the house's after) are
    shown in words on the ledger — STREET TALK, the card, the Stash card, the man's row. A
    number the player cannot read on screen is a defect. Every ticket names what it shows and
    where; the probe prints the same words, so a probe row and the screen never disagree.

## 2. What exists and is reused — every row with a line

| Thing | Where | Used for |
|---|---|---|
| The wall a house looks through | `Outfit/HouseView.cs:225-325` (`Roster`, `Accounts`, `Cells`, `AttentionLook`, `StanceLook`, `CrewBlockLook`, `RoundLook`) | every readiness signal is a look already on it |
| Where a view is built | `RoadDemo/TerritoryRuntime.Minds.cs:475` `Look(house, gameHour)`, callers `:122` (think), `:447` (borders), `:462` (probe) — none for the player | PRE-002 adds the fourth caller |
| The think that skips the player | `Outfit/Underworld.cs:307` `if (house.IsPlayer …) continue` | stays; the roll is not a think |
| Midnight | `Outfit/Underworld.cs:335` `DayTick`, driven from `Gameplay/OutfitDirector.cs:351-356` (then `ApplyFlatNight`) | the roll runs beside `ApplyFlatNight`, in the scene edge |
| Six o'clock | `News/Edition.cs:9` `PressHour = 6f`; `UI/NewspaperHud.cs:90-97` `ObserveCut` | the card shows at the cut, after the paper |
| The mind | `Outfit/HouseMind.cs:185` (`Collect` before `Walk`, "TIER 4 NEVER WAITS"), `:196` `Walk`, `:819` `Grow`, `:890` `Buy` | the card is answered before `Walk`, like a collection |
| One door out | `HouseIntent` (`Outfit/HouseIntent.cs`), carried at `RoadDemo/TerritoryRuntime.Minds.cs:806` `Carry`, `:864` `Retain` | every choice is an intent; a signed man goes through `Retain`'s twin |
| Signing a man off an ad | `Gameplay/PersonnelDirector.cs:209` `HireFromAd(ad, out newId)`; `Outfit/HouseOps.cs:66` names the house | the four paths all end in a `HireAd` with a real man |
| An ad outside the column | `Outfit/HireMarket.cs:192` `CounselFor(roster, seed, day)` — per house and day, not in the player's four | the connection man is dealt the same way |
| A man's own stream | `Personnel/Sentencing.cs:213` `StreamFor(rosterSeed, characterId, day)` | `Background.Of(rosterSeed, characterId)` derived at read, never stored |
| Notability | `Personnel/Notability.cs:44` `NewsBand`, `:150` `Of`, `:222` `Top` | the NAME signal |
| Money | `Outfit/Accounts.cs:150` `Receive`, `:162` `Pay(out dirtyPart)`, `:191` `Seize` | the test buy, the sale, the sting |
| Flats | `Property/Apartments.cs:193` `Buy(unit, gangId, day)` takes a gang already; `Property/UnitRole.cs:45-48` `Heat` is a constant | the Stash role; PRE-002 gives the mind the call |
| The flat night | `Gameplay/OutfitDirector.cs:390` `ApplyFlatNight` reads the player's `Runner.Flats` only | PRE-002 makes it a sweep over every house |
| The docket | `Police/CourtCase.cs:148`, `Personnel/Sentencing.cs:7` `Deed` (appended values keep meaning) | `Deed.Trafficking` appended; opened only by the sting |
| Arrest on the street | EPIC 34: officers are Units, custody booked at the station threshold (`RoadDemo/PoliceDispatch.Arrest.cs`, `PoliceDispatch.Custody.cs`) | the sting is a collar at the broker's door |
| Modal checks today | `RoadDemo/StreetHud.cs:270`, `RoadDemo/PoliceDispatch.Arrest.cs:102`, `RoadDemo/DemoCamera.cs:159`, `RoadDemo/CrewOverlay.cs:102, 2570`, `RoadDemo/TurfMapHud.cs:472, 491`, `UI/StrategicMapHud.cs:273, 288`, `UI/NewspaperHud.cs:91` | STREET-000 replaces eight name checks with one gate |
| The wire | `News/PressRecord.cs:9` `PressKind`, `:42` `PressPolicy`, `:160` `PressBook` | a seizure or a charge is public; a rumour is not |
| Learned doors | `RoadDemo/TurfKnowledge.cs:25-123` `LearnDoor`, saved per gang | the broker's door is a learned door, not a DTO field |
| Save | `Outfit/OutfitSnapshot.cs:104` `HouseDto`, `:134` `UnderworldDto`; `Save/CampaignFile.cs:191-207` (bump only when a field is removed or changes meaning; `SaveTests.cs:777-792` pins it) | nullable DTOs appended, **no version bump** |
| The yardstick | `Tests/PaperCity.cs`, `Tests/UnderworldSim.cs`, `gangsters_underworld_sim --days` | the probe and the new rows |
| Prices | `Docs/economy-prices.md` §6 (kilo $14,000, "no system exists yet") | made real |

## 3. Preconditions — two tickets the epic cannot start without

**PRE-001 The mind rents a room.** `HouseIntentKind.Lease` (+ `FitOut`, `SetKeeper`) and
`HouseOps.BuyFlat` through `Apartments.Buy(unit, gangId, day)` — the same call the blueprint form
makes; cases in `Carry` and in `PaperCity`. A mind rents a room **only when a card asks for one**
(a pending card holding `NoRoom`, STREET-003), never from the Buy tier, or twenty houses buy
flats in week one. **Which room:** `Apartments` has `CountIn` and `OwnedBy` and nothing that
lists what is free (`Property/Apartments.cs:134-175`) — the blueprint form picks from the
building the player clicked. `Apartments.VacantIn(building, into)` is added, and the mind takes
the first vacant unit in a building on a block it holds, nearest its front; none → `NoRoom`
stays and STREET TALK says so.
`ApplyFlatNight` becomes a sweep over every house, so a rival's raided flat jails its keeper,
heats its block and reaches the paper like ours. The keeper is named by the mind (every role
needs a keeper, EPIC 27). Yardstick row: a rival flat raided → keeper in a cell, heat on the
block, a line in the paper.

**PRE-002 A view for the player at the day boundary.** One pure sweep,
`StreetEvents.DayPass(houses, look, day)`, calls the `look` it is handed for **every** house,
the player included, and gives each view to `StreetEvents.Roll(view, book, seed)`. The scene
calls it once after `Underworld.DayTick` (beside `ApplyFlatNight`, `OutfitDirector.cs:356`) with
`TerritoryRuntime.Look`; `PaperCity` / `UnderworldSim` call the same method with their own
`Look` (`Tests/PaperCity.cs:301`). Never two sweeps, so the yardstick and the editor deal the
same card on the same day. Not in `CampaignRunner.DayTick`: the pure runner has no geography
and no attention. Known: the paper city's view carries `AttentionLook = 0f`, so anything
attention-driven is exercised only on the live rig. Contract: `gangsters_house_probe` on gang
0 prints the same signals it prints for a rival.

(Draft 1's PRE "harbour in the core rig + a waterside query + the works belt bound to the harbour
edge" is EPIC 41's: with the connection a man, no scene needs a harbour to test it.)

## 4. The street event system (STREET-000..003)

### STREET-000 One modal gate

`UI/ModalGate` with `Any`, `ClaimsEsc`, `Blocked`. The almanac, the paper, the map and the card
register; the eight sites in §2 read the gate instead of a name. Without it Esc "holds" the card
and, the same frame, closes the crew overlay; the arrest clock runs behind the card. The
shared-class fix at the choke point.

### STREET-001 The book, pure — `Outfit/StreetEvents.cs`

```
EventId     TheMan, BrokerRumour, TestBuy, SupplierTerms, LoadLanded
            (later: Funeral, Petition, RatSuspected, JudgeOnTheTake …)
CardId      PortMan, FieldMan, BrokerRumour, TestBuy, SupplierTerms, LoadLanded
            (TheMan deals PortMan or FieldMan by the line drawn, §5.2a)
EventDef    Id, Threshold, CooldownDays, Once,
            Score(HouseView, EventContext) → 0..1,
            CanAnswer(HouseView, EventContext) → the hold reason or null   (per card)
            Explain(HouseView, EventContext) → the weakest signal, one line,
            Deal(HouseView, EventContext, seed) → EventCard
EventCard   Id (CardId), Def (EventId), Speaker (characterId), DealtDay, ExpiresDay,
            Title, Lines[], Choices[]
Choice      Label, Intent (HouseIntent), Cost, NeedsCrew, Appeal(HouseView) → 0..1
EventBook   per house: Pending (one card at most), Pots (id → 0..1), Fired (id → day),
            Cooling (id → until day), Wire (List<WireLine>)
```

* **The pot.** Each day over its threshold a def adds `(s − t) / (1 − t)` to its pot; under the
  threshold it adds nothing and keeps what it has; at 1.0 it fires and the pot empties. No die.
  STREET TALK says how full it is in words ("the docks are starting to talk").
* **The hold rule** is per card, and a reason is a value: `HoldReason { None, NoMoney, NoCrew,
  NoRoom, BossInCell, AtWar, NoSpeaker, Watched }`, each with a line in words and a line saying
  what clears it (`Line`, `Clears`). Two classes. **Deal gates** — not dealt at all:
  `BossInCell`, `AtWar`, `NoSpeaker`, `NoMoney` for the cheapest choice, `Watched` (the QUIET
  gate, §5.3); STREET TALK still shows the gate while the pot is over 0.2. **Hold reasons** —
  dealt, waiting PENDING: `NoRoom`, `NoCrew`, and `NoMoney` when the safe drops under the
  cheapest choice after the deal. A held card waits with `DealtDay` set, `ExpiresDay = DealtDay
  + 3`, then goes to `Fired` marked `Unanswered` and the def cools.
* **Every signal is readable.** `Signals(HouseView, EventContext) → (name, value, line)[]`
  beside `Explain`, so the page prints each signal with its state; `Explain` stays the weakest.
* **Two kinds of firing.** No choices = a **wire**: one `WireLine` on the book (the feed shows it)
  or, through a `PressPolicy` gate, a `PressRecord`. Choices = a **card**.
* **Answering.** `Answer(card, choiceIndex)` returns the intent; the runtime carries it through
  `Carry` for a rival and the same call for the player.
* **The speaker** (ruling 3): `Deal` names the lieutenant whose crew holds the man, else the
  desk manager, else the card is not dealt (`CanAnswer`: "nobody to bring the word"). After
  Supplier is established, supplier cards may use an available house lieutenant or the desk;
  the original introducer is not required. A missing speaker affects card presentation, never
  the scheduled load itself; the house-with-no-lieutenant rule still applies to cards.

### STREET-002 The card and STREET TALK — `UI/EventCardHud.cs`

* Dealt at midnight, **shown at the six o'clock cut after the paper closes**, on the
  `ObserveCut` pattern. The paper at sorting 130 wins; the card opens when `NewspaperHud.IsOpen`
  clears. Latches on `CampaignSave.Pending` like the paper.
* THE PHONE: it rings (a `SoundDatabase` cue), the clock stops, the speaker's name and his
  words in the ledger's type, the choices as numbered rows, keys 1–3, Esc holds. Self-installs at
  Play with the ternary, never `??`.
* **Every choice row explains itself**: cost in dollars, who goes (the crew by name), and its
  risk in words where the def has one (the sting line off the door's watch, §5.5).
* **STREET TALK** on the ledger's front page: the last three wire lines; one PENDING row that
  reopens a held card with its hold reason **and what clears it** ("There is nowhere to keep it
  — rent a room and he will call back"); for the def nearest its threshold every signal with its
  state (`Signals`) and the gate when shut ("QUIET — the docks are being watched"), over the
  threshold how full the pot is in words; while Rumour or Contact is open the broker's door and
  its watch ("The Anchor is quiet" / "The Anchor is being watched"); once a man is signed the
  connection's row: the stage, whose the line is ("Tony's introduction" until Supplier, "our
  line" after), Burned with the days left.

### STREET-003 The mind answers

* `Answer` runs **before `Walk`**, beside `Collect` ("tier 4 never waits"): a pending card whose
  `CanAnswer` is clear is answered with the highest-`Appeal` choice the safe covers. WALK AWAY is
  an explicit choice with its own appeal, never the absence of one. Only a choice with
  `NeedsCrew` competes for `CrewOn`.
* **A held card is resolved by its reason, never left to expire when the mind can act.**
  `NoRoom` → a `Lease` intent (PRE-001) in the same think, the card answered the think after
  the room stands. `NoMoney` → waits its three days, answered the first think the safe covers
  a choice. `NoCrew` → waits until a crew is free, inside its three days. Deal gates never reach
  a mind. The probe prints the reason and what the mind did about it.
* Trace line `{"k":"house","intent":"Card:TestBuy/PAY"}`; `gangsters_house_probe` prints the
  pending card and the choice taken.

## 5. The connection (CONN-001..006)

### 5.1 The stages, per house — `Outfit/Connection.cs`

```
None → PortMan signed → Rumour (a broker's door named) → Contact → Tested → Supplier
Burned (30 days) from a sting or trust under 0
```

Before the first Supplier acceptance only, a stage drops one step after 14 days without the
introducer and a replacement resumes at the stage held. `supplierGrade` is None until terms
are accepted, then Broker or Direct: the persisted house relationship, independent of the
man's current roster membership. His later loss causes no stage or trust change. Burned is
still enforced independently; an established grade does not bypass it.

### 5.2 The man (CONN-001)

* `Background.Of(rosterSeed, characterId, directManId)` — derived at read from
  `Sentencing.StreamFor`'s pattern, never stored, so loaded men and dealt men alike have one and
  the pedestrian census gate is untouched. Values: None (most), Docker, Sailor, Fisherman (port),
  Baggage, FieldMechanic, Pilot (the county field), Direct — answered by the third argument,
  `characterId == directManId`, since a seed alone cannot single out one man in a city.
* **Pablo's man is a hidden turn.** Ids are allocated when a man is made (`HireFromAd(ad, out
  newId)`), so his id cannot exist at Deal. `Underworld.Deal` draws `directTurn` from
  `MixSeed(citySeed, "pablo")` — which `PortMan` firing city-wide carries him; when that
  firing's path materialises its man, `directManId` is bound to him. Unsigned, `directTurn`
  moves to the next firing after 30 days and the id clears. Both on `UnderworldDto`.
* The `TheMan` def fires when the pot is full **and one open path can produce him**. The four
  paths, chosen two-open per house at `Deal` from `MixSeed(citySeed, gangId, "paths")` and
  validated against what the city can satisfy (a seed with no cells yet still has THE COLUMN):

| path | what makes him appear | the speaker's line |
|---|---|---|
| OUR MAN | he is already on the roster (`Background` ≠ None, Loyalty ≥ 60) — no signing, the card just names him | "Tony worked the docks. He knows a man." |
| THE COLUMN | a `HireAd` dealt to this house the way `CounselFor` deals counsel, with a real `Man`, a wage and a signing fee | "There's a man in the paper asking for serious people." |
| THE CELL | a man of ours **released after ≥ 2 nights inside** (`HouseCell.HeldSinceDay` on the day he comes out) brings a cellmate's name; the cellmate is the ad. With `DaysToCourt = 1` two nights means a convicted man, so the path fires on release, never while he is inside | "Vinnie shared a cell with a Cuban. He came out with a name." |
| THE BAR | a Pub, Nightclub or Cafe door of ours (`HouseDoor` gains `Trade`), Standing paying, `FearLook` high; the barman knows a man | "Costa at the Anchor knows a man who works the boats." |

  A closed path is told cold, in words ("Tony asked around. Nobody's buying."), so a visible lead
  is never silently ignored. THE RIVAL (a rival at Supplier; kidnap his man for the name) is
  EPIC 41's, when rivals demonstrably reach Supplier.
* Signing goes through `HireFromAd` for the player (a CLASSIFIED-style row on the card) and a
  `HouseIntentKind.Sign` twin of `Retain` for a mind. He lands in a lieutenant's crew (ruling 2).

### 5.2a The two cards (CONN-001) — ruled 2026-09-05, "treba dva, što detaljnije"

One def and one pot (`EventId.TheMan`); two cards, `PortMan` and `FieldMan`. When the pot
fires, the path stream draws the **line** — Port 3 : Field 1, the field the thinner line — and
the card for that line is dealt. The line is saved as `ConnectionDto.line` (None / Port /
Field); every later card, the broker's door and the terms read it, never the man's background
at that moment. Both lines are abstract in this epic, as the harbour is; EPIC 41 binds the
field to its district. Pablo's man arrives on either card like any other man.

Placeholders: `{Lt}` the speaker, `{Man}` the man, `{Bar}` / `{Barman}` the bar and its keeper,
`{Cellmate}` our man who did the nights, `{Box}` the ad's box number.

**PortMan — "A MAN OFF THE BOATS".** The opening, by path:

| path | {Lt} says |
|---|---|
| OUR MAN | "{Man}'s been with us a while. Before us he worked the water. He says he knows a man who knows a Colombian, and he says it like he's said it before." |
| THE COLUMN | "There's a man in the paper asking for serious people. I made the call. He worked the river twelve years and he talks like a man who's carried more than fruit." |
| THE CELL | "{Cellmate} did his nights. Shared a cell with a Cuban named {Man} who was in for a manifest that didn't add up. {Cellmate} came out with a name and a number." |
| THE BAR | "{Barman} at {Bar} pulled me aside. Says a man off the boats drinks there Thursdays, pays cash, and asks about people like us." |

The man's own words, by trade:

| trade | {Man} says |
|---|---|
| Docker | "I load what the manifest says. I also know which boxes the manifest lies about. There's a container every third week that nobody signs for, and I know who doesn't sign for it." |
| Sailor | "Nine runs to Barranquilla on a banana boat. The chief mate has a cousin. The cousin has a cousin. That's the whole trade — cousins and a quiet mate." |
| Fisherman | "Forty miles out there's no Coast Guard, only me and what comes off a freighter in the dark. I've brought it in before. I know the man who sends the freighter." |

The ad (THE COLUMN only; printed in the classifieds the day before the card, so the paper
carries the lead):

| trade | the ad |
|---|---|
| Docker | "LONGSHOREMAN, 12 yrs Port of Miami. Knows the yard, knows the night gate. Serious people only. Box {Box}." |
| Sailor | "ABLE SEAMAN, South American runs, papers in order. Discreet, will travel. Box {Box}." |
| Fisherman | "CAPTAIN w/ own boat, 34 ft, twin diesels. Charters, deliveries, no questions asked or answered. Box {Box}." |

The line, in {Lt}'s words: "The Colombian doesn't talk to strangers. There's a broker — a
Cuban with a table at a bar on the water — who talks for him. {Man} can get us the table. After
that it's our money and our nerve. A boat brings five kilos at a time and never less, and the
boat wants paying at the rail."

The choices, each row with its cost and where he lands: **SIGN HIM** — "${SigningFee} now,
${Wage} a day. He stands in {Lt}'s crew." (OUR MAN: **PUT HIM ON IT** — no fee, he moves to
{Lt}'s crew if he is not in it.) **WALK AWAY** — "We're not in that business." The def cools 30
days; if he was Pablo's man, `directTurn` moves on. Esc holds three days; the PENDING row:
"{Man} is waiting on an answer — {HoldReason.Clears}".

Told cold: "{Lt} asked around the docks. Nobody's buying." / THE BAR closed: "{Barman} says
nobody off the boats drinks at {Bar}." / THE CELL closed: "{Cellmate} kept his head down
inside. He came out with nothing."

**FieldMan — "A MAN OFF THE COUNTY FIELD".** The opening, by path:

| path | {Lt} says |
|---|---|
| OUR MAN | "{Man} used to work the county field. He says planes come in after dark that nobody logs, and he says he knows who meets them." |
| THE COLUMN | "There's an ad in the paper from a man at the county field. I drove out. He knows the difference between a flight plan and a flight." |
| THE CELL | "{Cellmate} came out with a name. A man from the county field doing eighteen months over a logbook he didn't keep. Gets out Tuesday and wants work that doesn't ask about the logbook." |
| THE BAR | "{Barman} at {Bar} says a man from the field drinks there after the last flight. Cash, no friends, and he asked {Barman} who runs this street." |

The man's own words, by trade:

| trade | {Man} says |
|---|---|
| Baggage | "Everything that comes off a plane goes through my hands before it goes through customs. Some of it doesn't go through customs. I decide which." |
| FieldMechanic | "I sign the airworthiness. I know which Cessna goes to Bimini with the seats out and the tanks full and comes back with the tanks empty and the seats still out." |
| Pilot | "I've flown the Bahamas run. Two hours, under the radar, no flight plan. Two kilos ride in the wheel wells and nobody at that field has ever looked in a wheel well." |

The ad (THE COLUMN only):

| trade | the ad |
|---|---|
| Baggage | "RAMP AGENT, county field, nights. Fast hands, short memory. Box {Box}." |
| FieldMechanic | "A&P MECHANIC, light twins and singles, no paperwork either way. Box {Box}." |
| Pilot | "PILOT, twin rated, island time, cash only, leaves at dusk. Box {Box}." |

The line, in {Lt}'s words: "The Colombian has a man who sits in a diner on the field road.
{Man} can put us at his table. It's a smaller line — two kilos a flight, a plane a week — but
the Coast Guard doesn't fly over a cow pasture, and the man at the diner is hungrier than the
man on the water."

The choices: SIGN HIM / PUT HIM ON IT and WALK AWAY, the same costs and the same cooling. Told
cold: "{Lt} drove out to the field. The men there don't know him and don't want to." / THE BAR
closed: "{Barman} says nobody from the field drinks at {Bar}." / THE CELL closed, as the port's.

What the line changes in this epic:

| | Port | Field |
|---|---|---|
| `MinLoad` on the terms card | 5 | 2 |
| the broker's door (§5.4) | the nearest Pub or Nightclub in sight — "a bar on the water" | the nearest Cafe — "the diner on the field road" |
| STREET TALK's pot line | "the docks are starting to talk" | "the field is starting to talk" |
| the QUIET gate line | "the docks are being watched" | "the field road is being watched" |
| the `BrokerRumour` wire | "{Man} says the Cuban sits at {Door}. Thursdays." | "{Man} says the man from the diner is at {Door} after the last flight." |
| the `LoadLanded` wire (§5.6) | "A boat came in. {n} kilos in the room." | "A plane came in. {n} kilos in the room." |
| EPIC 41 | a ship at the harbour | a plane at the county field |

The test buy is two kilos on either line; the sting, the raid, trust and Burned do not care
which line it is.

### 5.3 Readiness — `ConnectionScore.Of(view)` (CONN-001)

| signal | weight | 1.0 when | 0 when |
|---|---|---|---|
| MONEY | 0.5 | `Safe ≥ 2 × TestBuyPrice` | `Safe < BrokerFee` |
| NAME | 0.5 | the roster's top notability ≥ `NewsBand`, or Named in the paper inside 14 days | never in the paper |

**QUIET is a deal gate, not a weight** (`HoldReason.Watched`): `PortMan` and `BrokerRumour` are
not dealt while max `AttentionLook` over our blocks is over the flat-raid threshold; the pot
keeps filling on MONEY and NAME and STREET TALK says "the docks are being watched". The Boss in
a cell and War are the gates they already are. Why: `Tests/PaperCity.cs:355` builds every
headless view with `AttentionLook = blockId => 0f`, so a QUIET weight would be a constant in
the yardstick and the probe could never rule it; a gate is honest in both rigs and is tuned on
the live one.

Thresholds: `PortMan` 0.4, `BrokerRumour` 0.6. Hold reasons, not weights: a free crew (Meet,
TestBuy), a Stash room (TestBuy), the man (introduction-dependent steps after `PortMan` until
Supplier terms are accepted). Established supplier operations never require the introducer.
First guesses; the probe rules.

**Explained in the UI (CONN-001):** the man's row carries his background in words and, once
signed, his part ("our line to the boats — Tony's introduction"); before Supplier, what losing
him costs; the stage-drop wire names the reason ("Fourteen days without Tony. The docks went
quiet."); a closed path is told cold, an open one that cannot fire yet says why; Pablo's man is
announced by his lieutenant only once signed.

### 5.4 The broker (CONN-002)

* The rumour names the broker's door as a **learned door** in `TurfKnowledge`, by the line:
  Port → the nearest Pub or Nightclub the house can see; Field → the nearest Cafe; if the
  line's kind is not in sight, the other kind, and the wire says so. New `OrderType.Meet` (appended): Point target, 3 hours,
  `Roll` on Streetwise, cost `BrokerFee`; `OrderEffects.Built` lists it. Filed from the door's
  card (MEET THE MAN, the speaker's lieutenant pre-picked) or by a mind's Answer.
* Outcomes: **Contact**, **Robbed** (fee lost, cools 5 days), **Cold** (retry). The roll stream
  is keyed `(day, attempt)` so a retry is a new roll. Robbed and Cold are the street, not the
  police, so Streetwise stays the die; the police risk lives in §5.5 and reads attention.
* The pull: `Explore` on the broker's block adds +0.15 to the day's score for three days.
* **Explained in the UI:** the MEET THE MAN card shows the fee, the lieutenant, the door by name
  and its watch in words off `AttentionLook` on its block; Robbed and Cold each leave a wire
  line saying what happened and what to do; Contact says the test buy is next and its cost.

### 5.5 The test buy and the room (CONN-003)

* `UnitRole.Stash` appended: FitOut $3,000, Earn 0, Wants Discipline. Its heat is **read off the
  kilos** (empty = CashStash's heat), which means `FlatDay.Raid` reads the connection, not
  `spec.Heat` alone. A raid seizes the kilos and seals the room; no case (ruling 10).
* `NoRoom` is a **hold reason, not a deal gate**: the TestBuy card is dealt without a room,
  waits PENDING ("There is nowhere to keep it — rent a room and he will call back"), and a mind
  leases through PRE-001 in the same think.
* The `TestBuy` card: PAY, SEND TWO MEN, WALK AWAY. PAY and SEND TWO MEN file a **job** like
  `Meet` (Point target = the broker's door, the crew walks, the money leaves on arrival,
  dirty-first). The street decides: **Good** (2 kilos in the Stash, Trust 40, Tested), **Short**
  (1 kilo, Trust 25), **Sting** — a `PoliceDispatch` collar at that door through EPIC 34's arrest
  code, `Deed.Trafficking`, the payment seized, Burned 30 days. `PaperCity` resolves it like any
  job.
* **The sting is the police, so its odds read the police, not a die.** `StingChance =
  clamp(AttentionLook(brokerBlock) − raidThreshold, 0, 1) × 0.5 − Trust / 200`, zero under the
  flat-raid threshold: a quiet door cannot be a sting, a watched one may be, and the lever is
  QUIET, shown on the card before the money leaves. Good against Short stays the Streetwise
  roll off the trust stream keyed `(day, attempt)`; the sting draw shares the stream so a
  replay stings on the same day.
* **Explained in the UI:** each choice row carries its cost, who walks, and the risk in words
  off the door's watch ("The Anchor is being watched — the seller could be a cop"); WALK AWAY
  says what it costs; the Stash card shows the kilos, their heat in words, and the one-line
  rule that a raid takes the kilos and seals the room; after a sting the wire says what was
  seized, that a case is open, and Burned with the days left.
* `Deed.Trafficking` appended to `Sentencing` for the crew that was caught. **The band is "as
  in real life"** [user, 2026-09-05]. The bands are campaign days read as the years of the real
  sentence (`Sentencing.cs:54`, "Days everywhere"; Murder 15–25, Extortion 8–14). Real life for
  a two-kilo buy in Florida, 1987, is Fla. Stat. 893.135(1)(b): 400 grams or more is
  trafficking, first-degree felony, **mandatory minimum fifteen, maximum thirty**; §893.135(5)
  gives an attempt or a conspiracy the same penalty, which is why a sting on a buy that never
  happened is charged as the buy. So `BandLow = 15`, `BandHigh = 30` — above Murder's 25 on
  purpose, that was the drug war. The mandatory minimum binds everyone: the lawyer's cut floors
  at `BandLow` already, and for Trafficking the `HoodPercent` scale may not take a man under 15
  either. The rap sheet's words: "TRAFFICKING IN COCAINE, 400 GRAMS OR MORE". The statute's
  $250,000 fine is not modelled — no fine mechanic exists.

### 5.6 The supplier and the paper load (CONN-004)

* `SupplierTerms` after Tested, spoken by the lieutenant: price per kilo `KiloPrice − Trust/10 %`,
  `MinLoad` 5 (Port) or 2 (Field) off `Connection.Line`, credit for half at Trust ≥ 60; Direct terms if the signed man
  is Pablo's (price −20 %, `MinLoad` 10, credit at 40). Accepting sets Supplier, `NextLoadDay`
  and the house's `supplierGrade` (Broker / Direct). Agreed terms and credit eligibility read
  that relationship and its trust, never a live lookup of the introducer's background.
* **The paper load** (ruling 12): on `NextLoadDay` `MinLoad` kilos land in the Stash at the terms
  price (paid on landing, or half on credit and half on the next day), `NextLoadDay += 7`. No
  ship yet; EPIC 41 replaces the line with a landing.
* **SELL TO HIS BUYER** on the Stash card: every kilo at `BuyerPrice` flat, dirty, one record
  line. Trust: sold on time +5, an unanswered terms card −10, a raid −20, a sting → Burned.
* **Losing the introducer after Supplier acceptance** leaves the relationship, trust, agreed
  terms (including Direct), `NextLoadDay`, deliveries and access to the buyer intact. No
  absence timer or replacement requirement applies. Loads still check payment, stash and
  Burned as usual. Supplier cards use a current house lieutenant or the desk under §4.
* The wire: `PressKind.Seizure` (the police made a record) and `Verdict`. A rival's success is
  never printed.
* **Explained in the UI:** the terms card shows price, `MinLoad`, the credit line and why (the
  man's trade sets the load, the grade the discount, trust the rest: "Trust 40 — he takes ten
  per cent off; at sixty he gives credit"); the Stash card shows grade, trust, the next load
  day and its size, and "our line" after acceptance with a line saying the introducer is no
  longer needed; every trust change is a wire line with its reason; `LoadLanded` says kilos,
  price, paid or on credit; a load held for want of a room says so.

**Amendment (2026-09-05, from EPIC 42 The Sit-Down, `Docs/design-briefs/diplomacy-brief.md` §9, written into GAN-405):** the terms carry a **`BuyerCapacity`** — the buyer takes at most N kilos a week (first guess `MinLoad`); kilos beyond it have no outlet that week. And **the desk rule**: a house never quotes a kilo to another house under what its own outlet would pay for it plus a margin. Both exist so a kilo can be sold house to house without the seller being a mug; this epic only stores the capacity and exposes "outlet for the next kilo" as a pure read.

### 5.7 Save, probe, yardstick (CONN-005)

* `ConnectionDto { stage, line, paths, manId, supplierGrade, trust, kilos, pricePerKilo, minLoad,
  nextLoadDay, burnedUntilDay, withoutManSinceDay }` nullable on `HouseDto` (`line`:
  `ConnectionLine.None = 0`, Port, Field); `EventBookDto { pending, pots,
  fired, cooling, wire }` nullable on `HouseDto`; `directTurn` and `directManId` (−1 until bound)
  on `UnderworldDto`. **No version bump**; `ConnectionStage.None = 0` explicit; contract "no
  block reads None".
* `SupplierGrade.None = 0`, followed by Broker and Direct. Persist the accepted grade and
  terms independently of `manId`; restore them even when he is dead or no longer in the house.
  `withoutManSinceDay` applies only before the first Supplier acceptance and is cleared then.
  Save/load must not restart an absence timer for an established relationship.
* `gangsters_connection_probe --seed N --days D`: one row per house per day — MONEY and NAME,
  the QUIET gate (open / shut, and why), the pot, the stage, the man, the card, its hold reason
  if held, and the answer. It prints the same words the UI prints (`Signals`, `HoldReason.Line`),
  so a probe row and STREET TALK never disagree.
* Yardstick rows: houses at Supplier by day 30 and what each earned from the buyer; **money per
  house per week, racket beside buyer**, so the cliff between a house at Supplier and the rest
  is a number on the table before EPIC 41 widens it; a rival flat raided reaches the cell, the
  block and the paper.

### 5.8 Docs and close (CONN-006)

`Docs/street-events.md`, `Docs/connection.md`, `Docs/economy-prices.md` §6 rewritten as real,
the memory file, tickets and epic to Done. Document the boundary between an unfinished
introduction and the house's established supplier relationship, including retained Direct terms.

## 6. The money (1987 dollars)

| line | $ | source |
|---|---|---|
| kilo, wholesale from the Colombian (Broker) | 14,000 | §6 |
| kilo, Direct | 11,200 | −20 % |
| the broker's fee, per meeting | 2,000 | new |
| the test buy (2 kilos) | 28,000 | 2 × kilo |
| the buyer's price, per kilo, flat | 20,000 | new — a wholesale flip, not §6's retail 5–7× |
| Stash fit-out | 3,000 | new, CardRoom's band |
| the man's wage and signing fee | `Wages.WageFor`, `Wages.SigningFee` | the column's own rule |

A good test buy nets $10,000 after the fee; a weekly paper load of 5 kilos nets $30,000 before
wages and heat. That is real money next to a racket that collects roughly $100 a door a week
(`Docs/rival-ai-plan.md` §1.1), which makes access to an introduction valuable and the stash worth
raiding. Killing the introducer after Supplier acceptance does not disable a rival's trade.

## 7. Measure before the numbers

1. `gangsters_underworld_sim --days 30` with the events on, over the paper city: how many houses
   sign a man, reach Tested, reach Supplier; where the pots stall. Then the MONEY / NAME weights.
   The paper city carries no attention (`Tests/PaperCity.cs:355`), so the QUIET gate and the
   sting odds are ruled on the live rig — MiniCoreDemo through `gangsters_play`, the watch read
   off STREET TALK — never off the paper city.
2. `gangsters_connection_probe` on seed 1987 for the player's house: the day the phone first
   rings, and what STREET TALK said the day before.

## 8. Contracts (headless)

* `StreetEventTests.Run()` → `gangsters_event_tests`: the pot is monotone and deterministic per
  seed; nothing is dealt against a deal gate, a card is dealt against a hold reason and waits; a
  held card expires on day +3 and cools; every `HoldReason` has a `Line` and a `Clears`; one card
  a day; the mind answers the highest-appeal affordable choice and WALK AWAY is a real answer;
  with `NoRoom` pending the mind proposes `Lease` before any Walk tier; a card with no speaker is
  not dealt; Esc leaves Pending unchanged; `DayPass` is the only caller of `Roll`.
* `ConnectionTests.Run()` → `gangsters_connection_tests`: each signal at 0 and 1; two paths open
  per seed, validated, the closed ones told cold; `Background.Of` is stable across save and load;
  exactly one Direct man per city; a sting seizes the payment only and opens Trafficking with body
  evidence; attention under the raid threshold never stings and over it the sting draw is
  deterministic per (day, attempt); the `Watched` gate refuses the deal while the pot still
  fills; THE CELL fires on release and never while the man is inside; a raid seizes and seals
  without a case; sold kilos are dirty; before the first Supplier acceptance only, 14 days
  without the man drops a stage and a replacement resumes it. For both
  player and rival, after Supplier acceptance, kill, jail, defection and departure leave trust,
  stage, Broker / Direct terms and the load schedule intact beyond 14 days; deliveries and sales
  continue when their normal conditions hold, and another eligible speaker carries supplier cards.
  A save/load after each such loss preserves the relationship and next load without requiring a
  replacement; raids and Burned still apply. A file with no `connection` block reads None and
  `SaveTests` stays green.
* `LedgerTests.AScriptedMonthIsRepeatable` unchanged with the events off; a second scripted month
  with the events on reaches Tested on a fixed day and matches its replay.

## 9. Out of scope, and the seams left

EPIC 41 The Trade: loads landing (the watched box, the hole in the wire and the shed for rent in
`HarborDemo/HarborDistrict.Contraband.cs:38-220`; the county field; the shore), the harbour in the
core rig and a `Waterside(blockId)` query with the works belt bound to the harbour edge
(`RoadDemo/RoadDemoBuilder.Zones.cs:140-170` vs `Districts.cs:116-136` roll independently today),
THE RIVAL path and a `Kidnap` that yields a name, trucks to the doors, retail, customs as its own
attention pool, the conspiracy case that puts the Boss on the docket (ruling 11). The seams this
epic leaves: `Connection.NextLoadDay` and `MinLoad`, `Stash` as the room a load goes into,
`Background` as the man's trade, `Deed.Trafficking`.

## 10. Still open for the user

Nothing. Both questions of draft 3 were ruled 2026-09-05: the `Trafficking` band is "as in
real life" (§5.5, 15–30, mandatory minimum) and the field gets its own card, written out in
full (§5.2a).

## 11. How it was done (2026-09-05)

Built in one pass on the day the epic was filed, every ticket in the epic's order, on the open
editor (`Tools/play/recompile.sh` after every batch), beside EPIC 42's session in the same
checkout. The docs are `Docs/street-events.md` and `Docs/connection.md`; the contracts are
`gangsters_event_tests` (12) and `gangsters_connection_tests` (20), and `gangsters_flat_tests`,
`gangsters_house_tests`, `gangsters_save_tests`, `gangsters_ledger_tests`, `gangsters_police_tests`
stay green; `gangsters_connection_probe` prints the rows.

What differs from the brief, and why:

* **`HouseOps.Sign`, not `HireFromAd`, for the player too.** `HireFromAd` takes the man off the
  classified column, and the connection's man is not on it; one door with the house named is
  what ruling 5 asks for anyway.
* **The man's trade is kept on the connection** (`ManTrade`), not derived from his id: a man
  dealt off a card has no id until he signs, and the card's words and the almanac's row must
  agree. `Background.Of` stays derived for everybody else and never touches the census.
* **Pablo's turn counts signings, not firings.** A firing cannot be counted once across a
  re-deal after a load; a signing can.
* **The broker's card is a card, not a wire**: MEET THE MAN / NOT YET, so a mind answers it
  through the same Answer as every other card. The stage moves to Rumour when it is dealt.
* **The cold wire cools a week, WALK AWAY cools thirty days** — one cooldown could not be both.
* **The reserve rule binds a card row** (D9): the row's cost and the signed man's wage
  (`EventChoice.Upkeep`) against a week's payroll — the yardstick's own "safe under a week's
  payroll" measure asked for it.
* **The sting in the paper city** jails the men for the statute's minimum: there is no station
  and no court on paper. The live city keeps them at the table with a standing job on the door
  and rings the precinct with a trafficking complaint (EPIC 34's collar).
* **The bottleneck is the flat.** On seed 1987 four of six paper houses reach Contact by day 30
  and none reaches Tested: the Stash's $55,000 flat is what a rival's safe never covers.
  Reported in `Docs/connection.md` for the user to rule on with the thresholds (CONN-005).
