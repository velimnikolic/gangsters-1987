# The rival families

Twenty families run on the player's own systems. There is one `Roster`, one
`CampaignRunner`, one racket, one round ledger and one command gateway, and every house
uses them — the only difference between house 0 and house 7 is that a person files house
0's orders and a `HouseMind` files house 7's.

EPIC 25 (GAN-244) builds it. This page is the map: what a mind can see, how it decides,
and how to read what it did.

## The three pieces

| piece | where | what it is |
|---|---|---|
| `HouseView` | `Assets/Scripts/Outfit/HouseView.cs` | the wall a mind looks through |
| `HouseIntent` | `Assets/Scripts/Outfit/HouseIntent.cs` | one thing it wants done |
| `HouseMind` | `Assets/Scripts/Outfit/HouseMind.cs` | the decision, pure |

The runtime edge is `Assets/RoadDemo/TerritoryRuntime.Minds.cs`. It reads the real ledgers
into a view, hands the view to the mind, and puts each intent through the SAME door the
player's own button uses — `TerritoryCommandGateway.Submit`, `Underworld.Issue`,
`HouseOps`. It decides nothing.

## What a mind can see

Its own books: `Roster`, `Accounts`, `Book`, `Front`, `FrontBlock`.

The street, as anybody standing on it could work it out:

* `Blocks` — the ground it stands on, holds doors on, or has its front on
* `Neighbours(block)`, `Businesses(block)` — each door's tier, weekly rate, protector,
  where it stands with US, what it owes US, whether it is shut, whether it trades, whose
  paper the premises is on
* `OurPresence(block)`, `OurFear(block)`, `ControlState(block)`, `Leader(block)`,
  `PoliceAttention(block)`
* `Incidents` — trouble on our ground nobody has answered for
* `Defiances` — doors that have said no to us and do not pay us, with the hour and how
  many times we have leant on them since
* `StanceToward(house)`, `LastRefusals`, `GameHour`, `Day`

**Not in the view, and never to be added:** another house's roster, safe or order book; a
shopkeeper's personality; any roll. A mind that could read those would be playing a
different game from the player. `gangsters_house_tests` scans `HouseMind.cs` for the
tokens `TerritoryRuntime`, `Ledger` and `Roll` and fails if one appears.

## The tiers

The mind walks strict priority tiers and emits for the FIRST tier with a feasible
candidate — plus any due collection, because tier 4 never waits behind a war.

| tier | what | built |
|---|---|---|
| 1 | the front is threatened | ✔ |
| 2 | money for wages | ✔ |
| 3 | replace the fallen | ✔ |
| 4 | collect | ✔ |
| 5 | answer an attack or a refusal | ✔ |
| 6 | consolidate a loose street | ✔ |
| 7 | expand | ✔ |
| 8 | buy | ✔ |
| 9 | idle | — |

**Tier 1.** Men who shot at ours were seen within `HqAlarmMetres` of the front inside
`ThreatMemoryHours`, or the front's own block carries an incident: a crew sits on the
front. Nothing else happens that think.

**Tier 2.** Under three days of payroll, a family with two crews puts them together: the
smaller lieutenant's hoods move across, then he is broken back to hood. One intent per
think, because a lieutenant has to be a hood before anybody can put him in a crew.

**Tier 3.** A crew under `MinHoods` active hoods files a `Recruit` order — the same order
the player files, resolved the same way, with the man landing in the recruiting crew. It
files only if the safe still holds a week's wages once the new man is on the payroll too.

**Tier 4.** The scheduler (`TerritoryRoundScheduler`) sends the rounds. The mind only
makes sure the paper is there for it to read: a man on the bag in every crew that protects
doors, and its lieutenant answering for the blocks those doors are on.

**Tier 5.** An attack on a door the family is paid for has a window
(`AnswerWindowHours`), and a house that lets the window close is worth less on that
street for as long as the street remembers. Their men still about and one of ours near
enough: `Assault`. Nobody to chase: `Guard` at the door — sitting on it is an answer too.

The same tier works the ladder at a door that said no: one `Threaten`, then one
`LeanOnHoldouts`, and then the crew's own policy decides — Strict or Brutal puts the
shutters in (`SmashUp`), Normal takes the till (`Raid`), Lenient files the refusal and
walks away. Never at our own doors and never at a door that pays us.

**Tier 6.** A street we lead that is `Contested`, or whose doors have gone wavering or
late, is walked before anybody looks at a new one: `OperateInBlock` while it is contested,
`ShakeDownBlock` when the doors are loose.

**Tier 8.** Only with a week's wages still in the safe after the price, and only after
`QuietThinks` thinks running with nothing louder to do: a car for a crew on foot, then a
gun for a hood with empty hands — through the same `HouseOps.Purchase` the ledger's shop
uses.

**Tier 7.** First it asks on ground it already stands on: the best unprotected door that
`TerritoryRacketOrders.For` would offer DEMAND against — the same rule that lights the
player's own key. A door that has refused is never asked again; it gets one THREAT, then
one LEAN, and is then left alone (RIVAL-006 escalates). Then, and only when every block
the family leads is `Controlled` or better with at least half its doors paying, it walks
onto the best neighbouring block: `score = expected take − hops × HopCostDollars −
attention × HeatCostPerPoint`.

A mind never asks at a door another house protects. Taking one off a family is a decision
about that family, and RIVAL-007 makes it.

## The numbers (the epic's D-table)

They live in `HouseMindConfig` and nowhere else — never a literal in a method.

| what | value | row |
|---|---|---|
| think every | 4 game hours | D7 |
| intents executed per think | 3 | D7 / D22 |
| a hop of travel | $100 | D8 |
| a point of police attention | $20 | D8 |
| reserve before spending | 7 days of payroll | D9 |
| merge crews below | 3 days of payroll | D9 |
| a crew is short below | 2 hoods | D9 |
| presence before a demand | 25 | D17 |
| between a refusal and the threat | 24 hours | D17 |
| doors paying before expanding | half | D17 |
| a door on the paper clock | 2 minutes | D17 |
| an attack stays worth answering | 12 hours | D10 |
| how long the street remembers who shot | 24 hours | D22 |
| close enough to alarm the front | 60 m | D22 |
| quiet thinks before buying | 3 | D22 |

## Men on the door

`Guard` does something now (D10). A crew working a Guard order is registered against that
address in `CrewJobs`, and:

* **on the street** — a wrecking, torching or raiding beat by another house at that door
  sics the guards on the attackers first, and the beat runs only once they are gone;
* **on paper** — `CampaignRunner.GuardOnTheDoor` asks the street who is sitting there, and
  the guard lieutenant's Combat half-steps come straight off the attacker's hand. A
  failure at a guarded door puts one of the attackers in a bed for `MisfireDays`;
* **either way** — the door's open incident counts `Answered` and the block gets a
  `SuccessfulRetaliation`: the house came when it was called.

The player's own SIT ON IT row gets all of it for free — it is the same code.

## Wake-ups

Four hours is a cadence for deciding what to do next, not a delay a family will sit
through while its shops are wrecked. `House.WakeNow(hour)` brings the next think forward,
and the runtime calls it when the power ledger files an incident against a house and when
a defiance opens against one.

The cadence is staggered: a house's first think is at `gangId × 4h / 21`, so twenty-one
minds never land on one frame. `House.WakeNow(hour)` brings one forward (RIVAL-006 hooks
it). **The player's house has no mind.** He is the mind.

## Reading what a house did

Every executed intent writes one `DriveTrace` row of kind `"house"`:

```json
{"t":12.500,"k":"house","gang":7,"tier":7,"intent":"ApproachBusiness",
 "why":"a door on our street that pays nobody","safe":24500,"payroll":540}
```

A think that found nothing writes `"intent":"-","why":"no candidate"`. A refused intent
writes the gateway's own refusal in `why` — and the refusal comes back to the mind in
`LastRefusals` on its next think, so a mind that keeps proposing a refused thing is a mind
with a bug you can see.

## The proof

`unity command gangsters_house_tests` runs the MVP on a paper city — three blocks in a
row, four doors each, twenty metres between doorsteps, the family's front on the first
block, the rounds walked by `TerritoryPaperClock`. For every seed from 1 to 30 the family
must, inside fourteen game days:

1. lose a hood
2. sign a replacement
3. deploy him into the crew
4. walk onto the next street
5. ask a door there
6. collect what it is owed and carry the bag home
7. pay its men out of that money
8. answer for a door it protects when somebody wrecks it

The command answers `mvp`: days-to-complete and dollars banked, per seed.

## See also

* `Docs/racket-collections.md` — the round ledger and the two clocks
* `Docs/territory-geography.md` — blocks, neighbours, the block graph
* `Docs/economy-prices.md` — what everything costs
