# The connection — the man who knows the Colombian

*EPIC 40 (GAN-395), CONN-001..006. Written 2026-09-05. Code: `Assets/Scripts/Outfit/Connection.cs`
(the paper, the score, the texts, the defs), `CampaignRunner.TickConnection` /
`ResolveConnection`, `HouseOps.Sign` / `AcceptTerms` / `Sell`, `UnitRole.Stash`. Contracts:
`gangsters_connection_tests`; the probe `gangsters_connection_probe`. Design brief:
`Docs/design-briefs/connection-brief.md` (draft 4). What the port is made of — the ships, the
county field, trucks, retail, customs, the harbour in the core rig — is EPIC 41's.*

## The man, not a block

The connection is a **man** the house finds along a path and signs: a Cuban, a docker, a
fisherman, a man off the county field. He need not be a lieutenant; he stands in a
lieutenant's crew, and that lieutenant is the voice of every card about him. His **trade**
(`Background`) is derived at read — `Backgrounds.Of(rosterSeed, id, directManId, connection)`
— one man in eight has one, three in four of those are port men; the connection's own man
reads the trade his card gave him (`Connection.ManTrade`); Pablo's man reads Direct by his id.
Nothing touches the pedestrian census.

## Readiness — `ConnectionScore`

| signal | weight | 1.0 when | 0 when |
|---|---|---|---|
| MONEY | 0.5 | safe ≥ 2 × the test buy ($56,000) | safe < the broker's fee |
| NAME | 0.5 | the roster's top notability ≥ `NewsBand`, or one of ours Named in the paper inside 14 days | nobody knows the name |

**QUIET is a deal gate, not a weight**: `PortMan`/`FieldMan` and the broker's card are not
dealt while attention on any block we stand on is over `HouseMindConfig.WalkAttentionCap` (the
mind's own "law watching" line); the pot keeps filling meanwhile. Why: the paper city carries
no attention at all (`Tests/PaperCity.cs`, `AttentionLook = 0f`), so a weight the yardstick
could never tune is a number nobody can rule on. An Explore inside three days adds 0.15.
Thresholds: the man 0.4, the broker 0.6 — first guesses, ruled off the probe.

## The four paths, two open

Drawn once per house from `MixSeed(citySeed, gangId, "paths")`; at a firing the first open
path that can produce him today is the one used, and if none can, the house is **told cold**
and the def cools a week.

| path | what makes him appear |
|---|---|
| OUR MAN | a man already on the roster with a trade and Loyalty ≥ 60 — no fee, PUT HIM ON IT |
| THE COLUMN | a real man dealt off the same seeder as the classified column, with a wage and a signing fee; his ad is printed on the card |
| THE CELL | a man of ours **released today after two nights inside** (`Character.ReleasedOnDay`, `NightsInside`) brings a cellmate's name; with `DaysToCourt = 1` two nights means a convicted man, so it fires on release and never while he is inside |
| THE BAR | a Pub, Nightclub or Cafe paying us on a block the family is feared on; the barman knows a man |

## Two cards: the port and the county field

One def and one pot (`EventId.TheMan`); two cards, `PortMan` ("A MAN OFF THE BOATS") and
`FieldMan` ("A MAN OFF THE COUNTY FIELD"). The line is drawn Port 3 : Field 1 at the firing and
saved as `Connection.Line`; every later card, the broker's door and the terms read it. Every
line of both cards — the opening by path, the man's own words by trade, the ad by trade, the
lieutenant's summary of the line, the cold lines, the wires — is in `ConnectionText`. What the
line changes in this epic: `MinLoad` 5 / 2; the broker's door (a Pub or Nightclub on the water /
a Cafe on the field road); the words on every wire ("A boat came in." / "A plane came in.").

## Pablo's man — the Direct line

Exactly one man in the city carries it. `Underworld.Deal` draws his **turn** hidden
(`DirectTurn`, which signing of a connection man city-wide is him); `DirectManId` is bound at
that signing (`Underworld.ConnectionManSigned`). Unsigned — his card expired or the house walked
away — he moves on to the next signing, and not for thirty days (`DirectNotBeforeDay`).
Nothing tells him apart before the terms; the terms card says "He's not a broker. He's Pablo's."

## The stages

```
None → PortMan signed → Rumour (the broker's door named) → Contact → Tested → Supplier
Burned (30 days) from a sting or trust under 0
```

- **The broker** (`BrokerRumour`, CONN-002): the card names his door — derived at read, never
  stored: the nearest Pub or Nightclub the house can see on the port line, the nearest Cafe on
  the field line — and MEET THE MAN files `OrderType.Meet` (three hours, $2,000, Streetwise).
  Contact / Robbed (five days cold) / Cold (try again), off the trust stream keyed (day,
  attempt). The door is learnt for that house (`TurfKnowledge`).
- **The test buy** (`TestBuy`, CONN-003): PAY ($28,000) / SEND TWO MEN ($14,000, a harder
  bargain) / WALK AWAY (back to Rumour, ten days). `NoRoom` is a hold, not a gate: the card is
  dealt without a room and waits; a mind leases one. The job walks to the door and the money
  leaves on arrival, dirty-first. **The sting is the police, so its odds read the police**:
  `over = (watch − WalkAttentionCap) / (100 − cap)`, chance `over × 0.5 − trust/200`, and
  **zero under the threshold whatever the trust**. Good (2 kilos, Trust 40) / Short (1 kilo,
  Trust 25) / Sting (the payment gone, `Deed.Trafficking`, Burned 30 days). In the live city
  the men are kept at the table by a standing job on that door and the precinct is rung with a
  trafficking complaint — EPIC 34's own walk-up, surrender roll and booking; the paper city
  has no station, so the book jails the men for the statute's minimum.
- **The terms** (`SupplierTerms`, CONN-004): price `KiloPrice − Trust/10 %` (a fifth off
  Direct), `MinLoad` 5 / 2 / 10, credit for half at Trust 60 / 40. ACCEPT makes the line the
  house's: `supplierGrade` and the terms persist on the house, independent of the introducer.
- **The paper load**: on `NextLoadDay` `MinLoad` kilos land in the Stash at the terms price
  (or half on credit, the rest the next morning), `NextLoadDay += 7`. No room → held, trust −5
  once, retried daily. The safe cannot pay → trust −10.
- **SELL TO HIS BUYER**: every kilo the buyer will take this week (`BuyerCapacity` = the load)
  at $20,000 flat, dirty; sold inside a week of landing, trust +5. `OutletForNextKilo` is what
  EPIC 42 reads.
- **Trust**: sold on time +5; the terms unanswered −10; a raid −20; a sting → Burned; under 0 →
  Burned.

## The introducer opens the line; the line is the house's

Before the first Supplier acceptance, losing the man (dead, jailed, deserted, away) holds the
introduction: after fourteen days the stage drops one step (`WithoutManSinceDay`), a
replacement resumes at the stage held, and the wire says why ("Fourteen days without Tony. The
docks went quiet."). After acceptance his loss changes nothing: not the stage, the trust, the
terms or the next load; supplier cards use any lieutenant or the desk. `Connection.WhoseLine`
prints it: "Tony's introduction" until Supplier, "our line" after.

## The room — `UnitRole.Stash`

Fit-out $3,000, no take; its heat is **read off the kilos** (empty = the cash stash's 1; every
kilo a point), so `FlatDay.Raid` reads the connection. A raid seizes the kilos and seals the
room for a fortnight; the keeper goes to a cell; **no case**. Only a sting on the street opens
`Deed.Trafficking`. The blueprint's Stash card shows the kilos, their heat, the line, and the
SELL key.

## The mind and the rivals

Every house gets the same events through the same gateways. The mind answers a card before it
walks (`HouseMind.AnswerTheCard`), leases a Stash when a card is held for `NoRoom`, sells kilos
the think after they land, and spends only with a week's wages left in the safe — the signed
man's wage included. `ApplyFlatNight` sweeps every house, so a rival's raided room jails its
keeper and reaches the paper like ours; `PaperCity` carries a `Lease` as a paper flat.

## Save

`ConnectionDto` and `EventBookDto` nullable on `HouseDto`; `directTurn`, `directManId`,
`directNotBeforeDay`, `theManSigned` on `UnderworldDto`; `jailedOnDay` / `releasedOnDay` /
`nightsInside` on the character. No version bump. A file with no connection block reads
`None` with an empty book; an established relationship never restarts an absence timer.

## Trafficking — "as in real life"

Fla. Stat. 893.135(1)(b), 400 grams or more: **15–30 campaign days** (the bands are days read as
the years of the real sentence), a mandatory minimum that binds hoods (`HoodPercent` may not
take it under 15) and lawyers alike; §893.135(5) gives an attempt the same penalty, which is why
a sting on a buy that never happened is charged as the buy. Bail $50,000; the $250,000 fine is
not modelled. The rap sheet: "Trafficking in cocaine, 400 grams or more".

## Measured (2026-09-05, seed 1987, six houses, thirty days, the paper city)

`gangsters_underworld_sim --seed 1987 --days 30 --houses 6`: cards 25 dealt / 10 answered / 13
expired; four houses signed a man and reached **Contact** by day 30, none reached Tested: the
test buy is dealt and held for `NoRoom` day after day because a rival's safe ($10–23k) never
covers the $55,000 flat the Stash stands in plus a week's wages. Nobody at Supplier by day 30,
no buyer money. The flat price is the trade's bottleneck for rivals and player alike (about
$86,000 to open the whole path); the weights and the thresholds are the user's to rule
(CONN-005). The two "safe under a week's payroll" failures on that seed (houses 2 and 3) are
not the connection's: house 3 touched no card, and house 2's dip is the payroll the signed man
adds — now weighed by the reserve rule (`EventChoice.Upkeep`).

## What EPIC 41 picks up

Loads that physically land (the watched box, the hole in the wire, the shed), the harbour in the
core rig and `Waterside(blockId)`, THE RIVAL path and a `Kidnap` that yields a name, trucks to
the doors, retail, customs, the conspiracy case that puts the Boss on the docket. The seams
this epic leaves: `Connection.NextLoadDay` / `MinLoad` / `BuyerCapacity`, `UnitRole.Stash`,
`Background`, `Deed.Trafficking`, `PressKind.Seizure`.
