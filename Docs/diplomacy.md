# The table — every word between houses

EPIC 42 (GAN-408, `DIPL-001..010`). Design brief with the rulings and the two contrarian
passes: `Docs/design-briefs/diplomacy-brief.md`. This page is the map of what was built:
one mechanism for everything two houses can say to each other, and where each piece lives.

## The one mechanism

A **proposal** (`Outfit/HouseDiplomacy.cs`, `Proposal`) is one thing one house asks another:
a kind, terms (money, streets, a man, a third house, days), the day it was filed and the
day it lapses. The book of them is one per city, `Underworld.Diplomacy`, beside the book of
standings, for the same reason: what two houses said belongs to the pair.

Every verb - the ledger's button and a rival mind's intent alike - goes through
`HouseOps.Propose`. It refuses what should not be filed, in the ledger's words (nobody to
say it to, the same thing already open, money the safe does not hold, no street named);
files; prints the ask in both books as an `AWordBetweenHouses` incident; and, when the
receiver is a mind, **answers at the desk**: `HouseDiplomacy.Answer` reads the receiver's
own `HouseView` and nothing else, deterministically, with no roll, and `Settle` carries the
effect. A proposal to the player waits in his inbox for `HouseOps.Reply` or its expiry.
The two edges carry a mind's `HouseIntent.Propose` / `Reply` through the same calls:
`RoadDemo/TerritoryRuntime.Minds.cs` `Carry` on the street, `Tests/PaperCity.cs` on paper.

The mind proposes from `HouseMind.Diplomacy`, which runs beside `Collect` and before the
tiers - `Walk` returns at the first tier that proposes, and a word to another family must not
queue behind a shakedown. A mind asks the view `HasOpenProposal` before it files, so a
broke house offers a truce once per expiry, not once per think; the same look answers true
for a proposal accepted today and waiting on midnight.

Money between houses crosses through one door, `Underworld.Transfer`: paid out of the
payer's safe first, dirty-first, then received dirty by the payee, on both sheets as
`FromHouses` / `ToHouses` (Finances prints them as BETWEEN THE HOUSES).

## The kinds

| kind | terms | the receiver's answer | the effect of a yes |
|---|---|---|---|
| `OfferTruce` | money | at war: **cannot refuse** when under `MinWarDays` of wages or past `LossesToSueForPeace` (ruling 1); else money clearing the grudge under `RetakeBusinessAt`, or the sender reading stronger with the grudge under `AttackBusinessAt`. At peace: only while no crew of the receiver works the sender's streets | the stance **agreed** for midnight; the money into escrow |
| `OfferPeace` | money | out of a truce only, grudge under `PeaceGrievance` after the money | as above |
| `Warn` / `Threaten` | one street | complied with from a house that reads stronger while owed less than a threat's worth | the receiver keeps off the street for `ComplyDays` |
| `Bill` | money (priced from the grudge: `(grievance − ThreatAt) × CompensationPerPoint`) | the same test, and the safe covering it over the reserve | the money across, the grudge cleared within the cap |
| `TributeTerms` | money a cycle | the levying desk: broke, or at least half the street's figure; the levied desk: from a stronger house it can pay | the levy pinned for `TermsCycles` |
| `Ransom` | the man, `KidnapCut` | a lieutenant whenever the safe covers it; a hood over the reserve | the money across, the man let go in the morning (to a bed) |
| `Line` | streets | both houses unable to pay for a war | both keep off the streets for `LineDays`; a door taken or hit across it is owed for on top (`LineCrossed`) |
| `Pact` | the third house | at peace with the asker, able to pay, the third reading weaker | sworn for `PactDays`; honoured by the book at midnight |
| `JoinWar` | the third house, money | the pact's test, plus the money clearing the grudge | the receiver's war on the third from midnight, flagged the pact's own |

A word refused or left two days is owed for at once (`WarningIgnored`); the 48-hour sweep is
gone. A lapsed proposal of any other kind is a refusal without a note.

## The guarded write

The pending stance is one slot and the last write wins (`HouseRelations.SetPending`). A
truce agreed at noon would have been overwritten by a defection's `Sour` at eight. So an
acceptance writes `HouseRelations.Agree`, and `ApplyPending` lands the agreed stance over
any harder pending on the pair - unless a grievance of `AgreementBreaksAt` (a killing) was
noted after the agreement: then the pending stands, the agreement is struck, and the money
held in escrow goes back the way it left. `Underworld.DayTick` runs the table's midnight
once every book has turned: escrows released, pacts honoured, tribute settled, proposals
lapsed.

## What money can clear

Every dollar that clears a grudge goes through `HouseDiplomacy.Compensate` →
`HouseRelations.Clear`: `CompensationPerPoint` (200) a point, at most `CompensationCapPerDay`
(20) off one pair in one day, and for `KillingFloorDays` (30) after a `ManKilled` never under
`ThreatAt`. Time still clears the rest. GIFT is not built (ruling 3).

The desk reads the same arithmetic before it says yes: `HouseView.Clearable` (the
`ClearableLook`) is what the money could still clear today - the cap already spent, the floor
after a killing - so an offer priced by the rate alone is refused when the book would not
honour it. A bill's ceiling (`HouseDiplomacy.BillCeiling`) is that clearable figure, never
past `ThreatAt`, at the rate; after the day's cap a second bill asks for nothing, and the
mind prices its own bill by the same figure. The ceiling is read again when a bill is PAID: a
bill lying in an inbox while other money clears the same grudge lapses (`Expired`, "they owe
us no such thing") whatever the answer - accepted, refused or left past its day - nothing
moves and no `WarningIgnored` is taken for it (`HouseDiplomacy.BillLapsed`, read by `Settle`
and `Expire`), and `HouseOps.Reply` answers with the record's words rather than a success.
Only MONEY lapses a bill: the grudge's own daily decay lowers the ceiling too, so the book
compares what money cleared off the pair since the filing (`HouseRelations.ClearedOn` against
`Proposal.ClearedAtFiling`) - a bill that merely aged unanswered is still a word ignored. A
bill that aged is repriced to today's ceiling (`HouseDiplomacy.Reprice`) before any desk reads
it - at midnight for every open bill, so the inbox shows today's figure; at an envoy's
delivery; at the player's reply; and again in `Apply` - so the desk's reserve test and the
payment price the same figure, and no payment clears the grudge under the threat rung; aged
to nothing owed, it lapses. Tribute
terms are priced against the street's figure (`CampaignRunner.DerivedTribute`), never against
a discount already pinned, so half of half is refused. The record keeps a proposal holding
escrow through pruning, and the levies with their pinned terms and the sheets' `FromHouses` /
`ToHouses` survive the file.

## Tribute, for every house

`Underworld.SettleTribute` assesses every house against this morning's holdings in gang-id
order and settles each envelope through `Transfer` into the levying house's safe - it used
to be the player's alone and reach nobody. `CampaignRunner.DayTick` no longer collects.

## The pact's midnight

`HouseRelations.ApplyPending(outcomes, landed)` reports every stance that landed with who
wrote it; `HouseDiplomacy.HonourPacts` then writes each partner's war on the declarer
**pending for the next midnight**, flagged `ByPact` - and a war a pact declared is nobody's
declaration for the next pact, so nothing cascades. A partner that cannot pay breaks the pact
(`PactBroken`, printed in every book). The player always honours; the sheet says so when he
signs.

## Losses

`CampaignRunner.NoteLoss` counts a man killed by another house on the same tally a defection
uses (`MenLostTo`); `LossesThisWar(g)` is that tally over the mark the war opened at, wired
into the view (`LossesLook`, `LossesThisWar`). It was 0 for ever.

## Keep-off

`HouseDiplomacy.KeepOff` is the one memory for a complied warning and a line. The racket's
choke points refuse every order of a kept-off house on the block, in the gateway's words -
`TerritoryRuntime.Execute` for `OperateInBlock`, `ApproachBusiness`, the walks and the
collection; `TerritoryRuntime.Paper.cs` `PaperOrder`; `PaperCity.Order`. The mind's `Expand`,
`Defend` and `BestNeighbour` skip the block through `view.KeptOff`; a posted crew is sent
home by `TerritoryRuntime.SweepKeepOffs` (the paper city's `SweepKeepOffs`).

## The sit-down

`HouseOps.SendToSitDown` files the proposal in transit with the envoy and his Streetwise,
and puts an `OrderType.SitDown` job on his crew's book to the other house's front. On
arrival, whatever the roll, `CampaignRunner.Finish` → `Underworld.SitDown` →
`HouseOps.Deliver` answers at the desk with `HouseDiplomacy.MarginOf`: every dollar test moved
by `EnvoyMarginPerHalfStep` per half-star, capped, and the sender read stronger by the same.
The host may `HouseOps.Ambush` a delivered proposal: the envoy dies on paper where he stands,
`ManKilled` and `SitDownBetrayed`, printed in every book. A mind never ambushes. The Don
never goes. The street-side engagement of an ambush is not wired: the fight happens in the
books, not on the pavement.

## The ledger

THE TABLE (`UI/PersonnelAlmanac.Table.cs`) unfolds from the one key on a FAMILIES card as a
strip under the row the card stands in - the drawer stays, the rows below slide down, FOLD
puts them back (`PersonnelAlmanac.Diplomacy.cs`; the card's STANDING row reads THEY ASK when
their proposal waits): what they ask with ACCEPT / REFUSE / AMBUSH, the ten words as keys, the
terms of the pressed one under it, by telephone or in person with the envoy, and THE RECORD of
the last words between the two houses. The buttons call `OutfitDirector.Propose / Reply / Ambush /
SendToSitDown / SetStance`; WAR stays a declaration. The telephone card (EPIC 40's
STREET-002) is not wired to proposals yet; the inbox on the sheet is the surface.

The keys are the questions and the panel under the pressed one holds the terms: OFFER TRUCE
opens money and the carrier; SEND A BILL opens money already set to the most it may ask
today; WARNING, THREAT and DRAW A LINE open the street; OFFER A PACT and JOIN MY WAR open
the third house (a war joined lists only the houses we are at war with); DECLARE WAR opens
its own word and a DECLARE key. A key that can only ever be refused is greyed, with the reason
printed under it in the sheet's short words - `HouseDiplomacy.WhyNot` and `WhyNotWar`, pure
over the figures the sheet shows (stance, a word already asked, a street of ours, a third, a
pact standing, the bill ceiling, the levy either way, our days of wages). The desk still
answers the word itself.

## Reading it

* `gangsters_diplomacy_tests` - the contracts, every ticket's.
* `gangsters_diplomacy_probe --house N --record K` - the table as it stands, per house.
* `gangsters_underworld_sim --days 60` - the `table` row: proposals made / accepted /
  refused, money crossed, lines and pacts standing, kidnaps and ransoms.

## Not built, and the seams

DIPL-009 the product (blocked by EPIC 41 and the GAN-405 amendments); SELL THE STREET; HAND
HIM OVER; GIFT; the telephone card for proposals; the ambush on the pavement. Every one lands
on `ProposalKind`.
