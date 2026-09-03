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

## Where the houses stand with one another

`HouseRelations` (`Assets/Scripts/Outfit/HouseRelations.cs`, pure, one per city on
`Underworld`) holds two different things and never confuses them:

* a **stance** belongs to the PAIR and is symmetric — two families are at war with each
  other or they are not, and the street reads one answer for both. It is set as PENDING
  and lands at midnight, for everybody at once.
* a **grievance** is DIRECTED, 0–100, and fades by `GrievanceDecayPerDay`. The first
  argument is always the house holding the grudge. One house may be owed a great deal by
  another that is owed nothing back.

What puts a grudge on the books (D14):

| what happened | worth | filed from |
|---|---|---|
| a door we protect was attacked | 15 | `TerritoryRuntime.ResolveEscalation` |
| a door switched from us to them | 10 | `SweepProtectionSwitches` |
| a round of ours was lost on their street | 20 | the round ledger's `Ended` |
| a man of ours killed by theirs | 35 | `OnStreetDeath`, and a paper `Kill` |
| a warning of ours ignored for 48 h | 10 | `SweepWarnings` |
| tribute unpaid | 25 | `CampaignRunner.CollectTribute` |

The ladder (design §26) is derived from the grievance by the D13 thresholds — Ignore 0,
DiplomaticWarning 10, Threat 20, DemandCompensation 30, RetakeBusiness 40, BeatCollector
50, AttackBusiness 60, KidnapCrewMember 70, KillCrewMember 80 — and the mind walks it one
step at a time. It warns, threatens, sends a bill, takes a door back, goes at their men,
and only at the top and only at war goes at their shops. **A house never skips a step.**

War is declared only by a house that can pay its men through one (`MinWarDays`, D15) and
that believes it can outlast the other. What it believes is `HouseRelations.Estimate`:
the truth through a deterministic haze between 0.7 and 1.3 — nobody reads another
family's books. A house at war it cannot pay for, or that has lost `LossesToSueForPeace`
men, offers a truce whatever it is owed; a truce whose two sides both stay under
`PeaceGrievance` for `PeaceAfterDays` becomes peace again on its own.

## Who fights whom

`Engagement.May(stance, oursIsTheGround, provoked)` (`Assets/Scripts/Outfit/Engagement.cs`)
is the street's ONE rule, for every pair including the player's, and it is exactly the
three sentences the FAMILIES card prints:

* **Peace** — nobody starts anything, claimed ground or not. A man being shot at still
  turns and fires back; that is not a stance question.
* **Truce** — territorial: the engaging house's own ground only. Neutral ground and the
  road between two blocks stay quiet.
* **War** — on sight, anywhere.

`DemoCrews.Combat` asks it before it takes a target. Until RIVAL-007 the rule was "a
rival watches for the outfit only", which made the player the one family anybody could
fall out with.

## Kill by name

A `Job` may name a man (`Job.TargetCharacterId`). On the street, `CrewJobs` sics the crew
on whichever unit he is standing in rather than on whoever is nearest; on paper he is
struck off HIS OWN family's roster, his block hears the killing in the ordering family's
name, and the family that lost him holds it against them. A failed killing puts one of the
attackers in a bed for `MisfireDays`, the same as a botched charge.

The player may only name a man his own men have stood near: `TurfKnowledge.LearnMan`
records a face at the same reach a door is learnt at.

## The houses the city never stood up

Every family is simulated always. How many have BODIES is a performance decision, and
`TerritoryRuntime.Stands(house)` is the whole of the test: a family with nobody on the
street runs on paper, and nothing else about it changes.

* its orders go to `PaperOrder` instead of the command gateway, and call the SAME
  resolutions — `ResolveDemand`, `ResolveThreat`, the racket, the round ledger;
* `OperateInBlock` becomes a POSTING, and `SamplePaperHouses` contributes one observation
  per man of that crew into the same presence sample, at the same weights
  (`gangsters_presence_tests` asserts a paper crew is worth exactly what the same crew on
  the street is worth, and is forgotten by the same clock);
* its rounds walk on `TerritoryPaperClock` and bank through the same `OnRoundEnded`;
* its jobs resolve by the paper roll, which is what a street-absent job has always done.

Every think is timed and the milliseconds go on the `"house"` trace row, and
`Underworld.Think` executes at most ONE house's turn of mind per call on a round-robin
rota — a single frame never carries twenty of them.

**The physical count is the user's to write** (D18). Until he has played a measurement
build and put a number in the table, `CoreDemoBuilder.rivalCrews` stays at 6 and
everything above that runs on paper by the rule above.

## Every order, and what it actually does

A mind may only file an order that is BUILT (`Assets/Scripts/Outfit/OrderEffects.cs`).
A person who files a stub can see nothing happened and stop; twenty families would file
it every week for ever and the tally would read as a working economy.
`gangsters_house_tests` asserts no mind ever files an unbuilt one.

| order | built | what it does |
|---|---|---|
| Assault, SmashUp, Raid, Torch | ✔ | the street resolves it; the racket, the fear ledger and the shop all feel it |
| Bomb | ✔ | shuts the premises for seven days (`BusinessShutdownCause.Bomb`, D12), otherwise as Torch |
| Kill | ✔ | names a man; struck off HIS family's roster, his block hears it, they are owed 35 |
| Kidnap | ✔ | names a man; off their books three days, a ransom printed in both books, they are owed 25. **Whether anybody pays is EPIC 10's** |
| Guard | ✔ | men on the door (D10) |
| Recruit | ✔ | a signing never fails; the man lands in the recruiting crew |
| BuyPremises | ✔ | the deed moves to the house that ordered it |
| Explore | ✔ | that block's doors and fronts become known **to that house** (`TurfKnowledge` is per house now) |
| Audit | ✔ | every man of the crew who was short in the count stops, one printed line each |
| RunBusiness | ✔ | a quarter of what the shop takes, for minding it — **not** a second `NetPerDay` |
| SetUpBusiness | ✘ | refused. **The city has no vacant premises**: `BusinessPopulation` fills every eligible site and invents no vacancies, so there is nothing to open a business in. RIVAL-009 step 4 closed |
| Bribe, EmployPolice, Donate | ✘ | any house may file one and the money moves; what they BUY is the police plan's (Phase 4) |
| Extort, Intimidate, CollectProtection, AdjustProtection | ✘ | superseded by the racket's own doorstep chain |
| Patrol, Ambush | ✘ | Patrol is `OperateInBlock` (D11); Ambush has no street behaviour |

**RunBusiness used to pay twice.** A premises on the family's paper already settles its
`NetPerDay` into the safe every midnight; a standing watch over the same door paid its
own book figure on top. It is a bonus now (`OrderResolution.RunBusinessBonus`, D22).

## Save and load

`Assets/Scripts/Save/` holds the campaign file. `CampaignSave.Compose()` gathers the whole
campaign out of the live ledgers and `Apply()` puts it back; `Write`/`Read` are those two
plus a file, and the pair is kept apart so `gangsters_save_tests` can round trip a campaign
with **no scene at all** — the only way to know a save is complete rather than merely
written.

What is in the file: the city seed, the day and hour, every house's roster (every man's
attributes, practice and potential, his crew, his gear, his condition and the day he is
back), every safe and day sheet, every order in flight, who stands where with whom and
what each is owed, every door's standing and dues, the rounds that were out, the deeds,
the shut shops, and what each family has learnt of the city.

The **cells** are in it too: everybody the city is holding, where in the pipe each man
stands, and everybody who has ever come out of the back of a car. That is not optional -
a held man is booked with NO release date (the day tick refuses to discharge a man
without one), so the pipe is the only thing that will ever let him out. A file without it
jailed him for the rest of the campaign.

What is **not**, on purpose (D19):

* **positions** — where a man was standing is a frame, not a campaign. Bodies are re-stood
  from the rosters;
* **fear, presence and derived control** — all three are decaying readings of bodies that
  no longer exist. The streets re-learn what they feel within a day, which is what they
  are for.

`unity command gangsters_save [--path]` writes; `gangsters_load [--path]` reads and applies
over the running city. An **autosave** goes to
`Application.persistentDataPath/gangsters/autosave.json` at every campaign midnight.
The ledger's front page has **SAVE** and **LOAD** rows for that same file, so the player
uses the identical write and scene-reload path without the terminal.

Loading in Play reloads the scene: the city is generated from a seed, so the honest way to
put a saved one back is to build it again from that seed and restore the books over it.
The file waits in `CampaignSave.Pending`; `UnderworldHost.SeedForScene` reads the seed off
it, and the territory runtime applies the rest on its first business tick, by which time
the businesses are populated and the racket is up.

A file from a **newer** version is refused with a printed reason, never half-read.

## The tally

One seed proves nothing; thirty is the verdict.

```
unity command gangsters_underworld_sim --seed 1987 --days 90 --houses 21 --json
unity command gangsters_underworld_sim --sweep 30 --days 90 --houses 21 --json > sweep.json
python Tools/sim/underworld_tally.py sweep.json
```

The sim runs every house on the paper clock over a bench city - two blocks a family, four
doors each - and prints one line per house per week: safe, payroll, doors paying, blocks
led, men (active/hurt/jailed/dead), wars, what it has banked, and the day each of the
eight MVP steps first happened (`lost/signed/deployed/street/asked/banked/paid/answered`,
`-1` for never).

**These are notes, not a verdict.** Only three things make the sim fail: an exception, an
order refused for ownership (which would mean a mind is reaching past its view), and it
counts negative safes rather than failing on them - EPIC 24 owns the short envelope.

Two things the yardstick cannot see, by construction: nobody attacks anybody on the paper
city (no incidents are filed there), so the ANSWER step and the war ladder never fire in
it. Those are `gangsters_house_tests`' and `gangsters_relations_tests`' business.

## See also

* `Docs/racket-collections.md` — the round ledger and the two clocks
* `Docs/territory-geography.md` — blocks, neighbours, the block graph
* `Docs/economy-prices.md` — what everything costs
