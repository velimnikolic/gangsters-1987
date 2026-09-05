# Recruitment — the corner, the panel, the specialists

*Design brief, 2026-09-05. Authoritative for EPIC 44 (`REC-001..009`). Read in full before
touching code, then `Outfit/HireMarket.cs`, `Outfit/HouseOps.cs`, `Gameplay/PersonnelDirector.cs`,
`Personnel/RosterSeeder.cs`, `Outfit/Wages.cs`, `Personnel/Lawyer.cs`, `Property/UnitRole.cs`,
`Property/FlatDay.cs`, `Outfit/HouseView.cs`, `Outfit/HouseMind.cs`, `RoadDemo/DemoCrews.Bomb.cs`,
`Docs/economy-prices.md`, `Docs/business-inventory.md`.*

## 0. Why

The user (2026-09-05): "ne sviđa mi se trenutno gde idem hire i dobijam random likove … hire
dugme može da ostane ali uzima prvog iz panela ako smo lenji … u recruitment panelu treba da
imamo specialists osim vojnika." Then: "treba od bloka kog kontrolišem da mi zavisi to — što
više blokova to veći pool", and "bordel koji vodim u stanu ne može da proizvodi makroa, to je
cyclic dep, treba mi makro da bi otvorio bordel", and "granata može da se kupi, a bomba kao ona
što se podmeće mora bombaš."

Verified in code: HIRE A MAN (`UI/PersonnelAlmanac.Organization.cs` `SignAndPlace`,
`Command.cs` every branch head and the reserve) pays `EconomyPrices.RecruitSigning` $500 and
`RosterSeeder.Recruit` deals one unseen hood. The only seen men are the classified column's
four lieutenants and the weekly lawyer (`Outfit/HireMarket.cs`). `Specialty` holds
`Accountant` and `Lawyer`; only the lawyer has a door (`Personnel/Lawyer.cs`).

## 0a. The contrarian pass (2026-09-05) and what it changed

The plan's second draft went to the repo's contrarian agent, which dealt seed 1987 with the
editor (`gangsters_business_audit --seed 1987 --rows --json`: 3,544 businesses, 170 blocks)
and read the seams. Findings adopted:

| finding | adopted as |
| -- | -- |
| The haunt table was written against the archetype enum, not the city: Pub, Restaurant, Hotel, Casino, Factory, Works, Refinery stand NOWHERE in the core city; one Nightclub, one Warehouse, one Fairground, six Diners, 187 Cafes; Pharmacy/Laundry are generic retail on ~44 % of blocks; the pimp unlocked on 1/170 blocks | the table in §3 is built from what stands, dormant rows kept and marked; the pimp gains Diner/Cafe/Fairground; Cafe counts at half weight |
| `HouseView.Blocks` is every block the house can SEE, not the controlled list; a crew's afternoon walk would grow the corner | ONE predicate `ControlState ∈ {Controlled, Dominated} && Leader == house`, snapshotted at the day tick |
| "A signing never fails" is a standing assumption of the minds and the order table; an empty corner breaks it with the money already taken | the door refuses AT ISSUE, before money moves; `HouseMind.CanSign` checks the corner first (user's ruling 3) |
| The column deals slots off one stream and names men against the roster; sign, save, load re-deals everybody after the signed man | per-slot streams; taken slots persisted per house per day; `HireMarket` gets the same fix |
| "Keeper of every brothel" collides with `RosterOps.CanKeep` (refuses specialists), `Apartments.SetKeeper` (one man, one flat), `StateOf`; `FlatDay.Skim` is CashStash/CardRoom only | the pimp is a house-level CONDITION and MULTIPLIER; the room keeps its hood keeper; he does not skim |
| Per-charge misfire makes the bomber's charges worse than bought ones (a bought charge has no misfire on the street) | charges stay fungible; his skill reads on the paper order's existing under-floor misfire; grenades bought, bombs made (user's refinement) |
| "Hands the wanted trace a plate": the wanted mark is per MAN, no plate exists | a stolen car is HOT = a heat band on the job through `OrderResolution.HeatFor` until it cools |
| The pad reaches into `PoliceProcedure` (scene) and `TerritoryFear`; neither may read the Outfit; no coverage map exists | per-station coverage = nearest station by block-graph hops; a `PadLook` handed in like `AttentionLook`; `Deed.Bribery` through every `Sentencing` switch |
| Yield-only chemist changes zero dollars (`Connection.Sell` sells `min(Kilos, BuyerCapacity − SoldThisWeek)`) and adds heat | REC-008 is BLOCKED on EPIC 41, not built yield-only |
| Corner price range is $560–$1,232 (ceiling 6 half-steps), not $1,904; derived pricing re-prices `HouseMind.CanSign` and `CostFor` | its own ticket, REC-001b, after the corner lands on the flat price |

Not adopted: the contrarian's preference for a permanent flat $500. The user ruled for a
derived price per man, as a separate ticket.

## 1. Rulings (the user's, 2026-09-05)

| # | question | ruling |
| -- | -- | -- |
| 1 | soldier's price | every man his own derived price (14 days of his rate) — REC-001b, after the corner |
| 2 | where the panel lives | a RECRUITMENT tab in the P-key book |
| 3 | empty corner | the door REFUSES: "the corner is empty today — come back tomorrow" |
| 4 | which blocks give soldiers | every controlled block, one man each |
| 5 | pimp's haunts | Nightclub + Diner + Cafe + Fairground; never the brothel flat |
| 6 | pimp and the keeper | condition and multiplier; the room keeps its hood keeper |
| 7 | archetypes not in the city | stay in the table, marked dormant |
| 8 | bomber | GRENADES (thrown) stay bought; BOMBS (planted) exist only if a bomber made them |
| 9 | stolen cars | beside the car shop; hot = heat on the job |
| 10 | the fixer's pad | PER STATION — covers the blocks that station polices |
| 11 | chemist | after EPIC 41; the ticket is blocked |
| 12 | specialist wage | rises with his stars |

## 2. THE CORNER — a dealt pool of soldiers (pure, Outfit layer)

`Outfit/RecruitPool` (sibling of `HireMarket`): per HOUSE, per campaign day, men dealt off
the books via `RosterSeeder.Deal(roster, rng, RecruitCeilingHalfSteps, stream)` — the corner
boy's ceiling (6 half-steps, three stars).

**Streams.** Each SLOT has its own: `Mix(Mix(seed + SeedOffsets.Personnel, day),
gangId * 31 + 11 + slot * 97)`. Slot N is the same man whatever happened to slots 0..N-1.
Taken slots are PERSISTED per house per day (the `EventBook.Pending` shape) and the corner is
restored, not re-dealt, on load. `HireMarket` receives the same per-slot fix.

**Size = the territory.** `CornerSize(house) = DoorMen (2) + ControlledBlocks(house)`, cap
`CornerCap` 12. "Controlled" is ONE predicate, a helper on `HouseView`:
`ControlState(block) ∈ {Controlled, Dominated} && Leader(block) == house` — NOT
`HouseView.Blocks`. Snapshotted at the day tick. Every soldier is dealt FROM one of those
blocks and his row says so (`From` = the block's neighbourhood name). The page explains the
count in words: "7 men: 2 from your own door, 5 from the 5 blocks you hold."

**Empty corner.** When every slot is taken, HIRE A MAN and the Recruit order are refused AT
ISSUE, before any money moves: "the corner is empty today — come back tomorrow."
`HouseMind.CanSign` checks `CornerLeft(house) > 0` before filing; the gateway refuses a filed
Recruit on an empty corner with that reason and a one-day backoff key; the house tests that
count signings per night are retargeted; `Docs/economy-prices.md` §8's "a signing never
fails" is rewritten to "a signing never fails once filed".

**One door for every house.** `HouseOps.Recruit(house, …)` takes a man OFF THE CORNER:
- HIRE A MAN and a mind's Recruit with no recruiter named → `corner[0]`;
- the Recruit ORDER with a recruiter → the corner's BEST man in the recruiter's eye
  (top-three trade sum), one extra look per half-step of Awareness over the order floor (7).
`RosterSeeder.Recruit`'s on-the-books half (`Members.Add`, `BossHoodIds.Add`,
`GangLooks.LookFor`, `Career.Joined`) is FACTORED into `RosterSeeder.Enlist(roster, man,
broughtBy)` that the corner and the demo outfits (`MonkeyOutfit`, `BlockDemoOutfit`) call.
The director's door goes through `PersonnelDirector.Commit` so `Version` moves and
`DemoCrews.Sync` stands the man in the street.

## 2b. THE PRICE — GAN-272 closes here (REC-001b)

Every candidate prints his own signing money the way an ad does:
`Wages.SigningFee(Wages.WageFor(man))` = 14 days of his house rate — **$560–$1,232** over the
corner's ceiling (three best trades ≤ 18 half-steps → ≤ $88 a day). HIRE A MAN pays the first
man's figure and the filed order says the sum. `EconomyPrices.RecruitSigning` stops being the
price of a man. The Influence discount on the Recruit order goes. With it, in the same ticket:
`OrderResolution.CostFor` takes the man (the chosen slot); `HouseMind.CanSign` budgets the
slot's price; `WageTests.OneBlockCarriesOneCrew` and the founding payroll are re-measured;
`Docs/economy-prices.md` §1/§8 rewritten. Ships AFTER REC-001 so the minds' growth is
re-priced in one measured step.

## 3. THE PANEL — a RECRUITMENT tab in the book

The tenth `LedgerPage`, reached from its tab and from a SEE THE CORNER key beside every HIRE
A MAN on the Organization page. HIRE A MAN is untouched in place and behaviour (first man).

In the book's own furniture (LedgerV2, LedgerStyle, measured widths):
- LEFT, three sections of rows: **SOLDIERS** (the corner, 2–12), **SPECIALISTS** (whoever
  advertises today), **LIEUTENANTS** (the classified column's men — the panel READS
  `HireMarket`, the paper keeps its page; `Take` keeps the two doors honest). A row: press
  photo thumb, name, age, where from, three best trades in stars, ask a day, signing money,
  HIRE.
- Under SOLDIERS the count in words; under SPECIALISTS the reason for every kind NOT offered
  ("No pimp advertises: you hold no nightclub, diner, cafe or fairground").
- RIGHT: the selected man's card — the personnel page's dossier (photo, eleven stars,
  personality where the file shows it, the rap sheet's line) with his pitch. A specialist's
  card shows his ONE skill in stars and, in words, what he does and what changes when he is
  on the books.
- HIRE on a soldier opens the posting picker the Organization page already uses (reserve /
  branch / THE DETAIL / a bag) — the same `SignAndPlace` halves with the chosen slot.
- Refusals that leave the outfit poorer are taken before the money moves.
- Repaint key: corner revision × column revision × safe, plus the note as a string.

## 4. SPECIALISTS — the foundation

`Specialty` appended (serialized values keep meaning): `Bomber`, `Fixer`, `Pimp`, `Locksmith`,
`Chemist`. Each follows the lawyer's pattern:
- one `Skill(man)` 1–5 from TWO stats, in `Personnel/<Trade>.cs`, read by the ad, the file
  and the door alike;
- wage in `Wages.HouseRateAs` (specialty before rank), rising with his stars:
  `Base + PerStar × (Skill − 1)`; ask ×125 %, 14 days down; entered in
  `Docs/economy-prices.md` before the code carries it;
- heading + pitch in `HireMarket`, label in `LedgerText.SpecialtyLabel`, a look in
  `GangLooks`/`MemberModel` (suits, like the lawyer);
- no rank, no crew, no place in the chain, no gun (`RosterOps` already refuses every posting
  for `Specialty != None`); works OFF THE STREET, on the day tick;
- the AI door is `HouseOps.Retain(house, ad)`, its one-per-house check made per-kind. Minds
  retaining the new kinds is a later mind ticket; the door is theirs from day one.

**Haunts — measured against the city.** A pure table `SpecialistHaunts` maps each kind to
business archetypes (`BusinessArchetypeId`) and flat roles (`UnitRole`), each with a weight.
A haunt COUNTS when the business stands on a block the house CONTROLS (§2 predicate →
`TerritoryGeography.BusinessesOf(block)` → archetype).

| kind | living haunts (seed 1987 counts in brackets) | dormant — alive when the industrial quarter / EPIC 41 lands |
| -- | -- | -- |
| Bomber | Warehouse (1), Hardware, ElectricalShop | Works, Factory, Refinery, FuelStation |
| Fixer | Diner (6), Cafe (187, half weight), BettingShop, Pizzeria; +1 if the block neighbours a police station block | Pub, Restaurant, Hotel, Casino |
| Pimp | Nightclub (1), Diner (6), Cafe (half weight), Fairground (1) — never the brothel flat | Hotel, Pub, Casino |
| Locksmith | Locksmith, PawnShop, ElectricalShop; a Garage flat the house keeps | CarYard, FuelStation |
| Chemist | Pharmacy, Laundry | Refinery, Factory |

**Haunts buy quality.** Weighted haunt count 1: he advertises rarely and rolls to a 3-star
ceiling; 3 or more: often, to 5 stars.

**The day's offer.** On the day tick, per house: the unlocked kinds minus the kinds already on
the books (the lawyer's `OnBooks` shape, per kind); one drawn from `Mix(seed, day, gangId)`
weighted by haunt count; NEVER a kind the house already keeps. The lawyer keeps his weekly
slot in the paper, no haunt needed. The offer is persisted with the corner.

Wage bands (1987 dollars; the price doc is the authority once entered):

| specialist | skill from | wage/day 1★ → 5★ | ask/day 1★ → 5★ | down 1★ → 5★ |
| -- | -- | -- | -- | -- |
| Bomber | Awareness + Stealth | 160 → 280 | 200 → 350 | 2,800 → 4,900 |
| Fixer | Persuasion + Connections | 250 → 450 | 312 → 562 | 4,368 → 7,868 |
| Pimp | Streetwise + Intimidation | 140 → 260 | 175 → 325 | 2,450 → 4,550 |
| Locksmith | Stealth + Driving | 130 → 230 | 162 → 287 | 2,268 → 4,018 |
| Chemist | Awareness + Organization | 220 → 380 | 275 → 475 | 3,850 → 6,650 |

## 5. One door per specialist (no silent modifiers — a man nothing reads does not ship)

**Bomber — grenades bought, bombs made.** Today ONE stock, `EquipmentKind.Grenade`, serves
the throw and the plant alike (`DemoCrews.Bomb.cs` `Thrower`/`Planter` through
`HasGrenade`/`SpendGrenade`). It splits:
- **Grenade** — thrown at a rival or a shopfront. Unchanged: bought in `ArmoryCatalog`,
  signed out or loose on the lieutenant's deed, spent by `Thrower`.
- **Bomb** (new `EquipmentKind`, appended) — a planted device: under a car
  (`Planter`/`CanBombPlant`) and the paper `OrderType.Bomb`. NOT sold anywhere. The bomber
  MAKES them: one a day into the armory at material cost (a purchase line), two a day when the
  house leases a GARAGE. Same deed/pin rules as a grenade (`IsGrenade` → `IsCharge` over both
  kinds; `SpendGrenade` takes the kind), so `NormalizeArms` and the drawer need only a second
  row.
Both fungible — no per-item quality. His skill reads on the paper Bomb order's under-floor
misfire (`OrderResolution` ~517, 25 %) in place of the crew's worst man. Without a bomber the
house throws grenades and plants nothing: the car bomb and the Bomb order are refused with
"no bomb — no bomber on the books". `HouseMind` files a Bomb only while `Bombs(house) > 0`.

**Fixer.** Two doors on the LAW sheet, both in words.
- THE PAD, PER STATION: each police station has a coverage — `Precincts.CoverageOf(block)` =
  nearest station by block-graph hops (one station today → whole city; written for several).
  The LAW sheet lists stations with the blocks they cover and a PUT THEM ON THE PAD key: a
  standing weekly payment (`PoliceOnThePad` 800, skill-scaled) out of the safe with the wages,
  a row on Finances. Effects on covered blocks: the shopkeeper's telephone waits
  `PadDelayDays(skill)` before dispatch (a `PadLook` handed to `PoliceProcedure`, the way
  `TerritoryFear` takes `AttentionLook` — Police and Territory never read the Outfit) and
  attention decays a notch faster. Stops the day a payment is missed.
- MAKE IT GO AWAY: on a docket row before trial, a price by deed (Bribe 500 × the deed's
  sentence band) and odds by skill; success drops the case; failure charges the FIXER with
  `Deed.Bribery` (new — rows in EVERY exhaustive `Sentencing` switch: BandLow/BandHigh/
  ChargeFor/Bail) and the case stands. Cop-killing stays unbuyable (`WantedLevels`). The
  dead `OrderType.Bribe`/`EmployPolice` are retired from the order table.

**Pimp — condition and multiplier, not keeper.** Fitting a flat out as a brothel is REFUSED
while no pimp is on the books; `UnitState` gains a `NoPimp` reason printed on the blueprint
sheet ("no pimp, no girls"). The room keeps its hood keeper like every other role —
`RosterOps.CanKeep`, `Apartments.SetKeeper`, `StateOf` untouched; EPIC 27 and the FLAT tests
stand. With a pimp: girls fill by one a day up to the room's cap while his Streetwise holds,
and the house's cut rides his skill (`BrothelTakePerGirl × (1 + skill/10)`). If he leaves,
dies or sits in a cell, every brothel goes DARK (take zero, girls leave one a day) until
another is retained; the rooms keep their fit-out. He does not skim — `FlatDay.Skim` is the
keeper's.

**Locksmith.** Two night jobs, filed from his card, resolved on the day tick, printed on the
incidents book.
- STEAL A CAR: a car of a chosen body lands in the outfit's vehicle deck by morning
  (`RosterEquipment` Vehicle, `Value` 0, `HotUntilDay` on the item — one new field), HOT: a
  job that uses it before it cools adds a heat band through `OrderResolution.HeatFor`; it
  cools `CoolDays` in a GARAGE. Caught (odds by skill) = `Deed.CarTheft` (new, same switch
  discipline). The car shop stays.
- BREAK IN: the till of a named business at night, no shots, no fear, no heat when the roll
  passes; caught = `Deed.Burglary` (new).

**Chemist.** BLOCKED on EPIC 41's product table. He is in the enum, the haunt table and the
wage doc so the panel can say "no chemist advertises" truthfully; he does not advertise until
his door exists.

## 6. Tests, docs, close-out

`gangsters_recruitment_tests`: per-slot determinism (same seed+day+house+slot = same man;
sign slot 0, save, load — slots 1..N unchanged, slot 0 still taken); two houses differ; size =
2 + controlled blocks by the ONE predicate (a neighbouring block with presence does NOT
count), capped, grows at the day tick after a block is won; empty corner refuses at issue
with no money moved and the mind does not file; HIRE A MAN = `corner[0]`; order with a
recruiter = eye-best; the wage table reads specialty first and rises with stars; no haunt =
no specialist, weighted 1 = 3-star ceiling, 3 = 5-star, dormant archetypes contribute
nothing; one-per-kind; each door's contract (bomber: a grenade still buys and throws without
him, a bomb cannot be planted without him, one a day, two with a garage, misfire reads his
skill; fixer: pad covers the station's blocks only, delay + faster decay, missed payment ends
it, failed bribe puts `Deed.Bribery` on HIS docket and every Sentencing switch answers for
it; pimp: brothel refused without him with the `NoPimp` reason, fill, cut, dark when he goes,
hood keeper still required; locksmith: car in the deck, hot heat on a job, cools in a garage,
caught = docket). REC-001b: one derived price at every door, `CanSign` budgets the slot,
`OneBlockCarriesOneCrew` re-measured.

`Docs/recruitment.md` (the corner, the price, the panel, the specialists, the measured haunt
table, the doors) + a row in CLAUDE.md's table; `Docs/economy-prices.md` gains the specialist
wage bands and, with REC-001b, loses `RecruitSigning` as the price of a man; a memory note.

## 7. Tickets and order

Epic: GAN-430 (EPIC 44), label Roster.

| # | Linear | ticket | depends on |
| -- | -- | -- | -- |
| REC-001 | GAN-431 | The corner | — |
| REC-001b | GAN-432 | The price (closes GAN-272) | 001 |
| REC-002 | GAN-433 | The panel | 001 |
| REC-003 | GAN-434 | Specialist foundation | 002 |
| REC-004 | GAN-435 | Bomber's door | 003 |
| REC-005 | GAN-436 | Fixer's doors | 003 |
| REC-006 | GAN-437 | Pimp's door | 003 |
| REC-007 | GAN-438 | Locksmith's doors | 003 |
| REC-008 | GAN-439 | Chemist's door — BLOCKED on EPIC 41 | 003, EPIC 41 |
| REC-009 | GAN-440 | Docs, memory, close-out | all |

Order: 001 → 001b + 002 → 003 → 004–007 in parallel → 009; 008 waits for EPIC 41.

## 8. Cheapest check before building

With the editor open, one minute:

    unity command gangsters_business_audit --seed 1987 --rows --json

and count `archetype` per `block` for the five haunt sets — the living/dormant split in §4
must match the seed the Night Watch rig stands, and the rig must stand `nightclub-block` or
the pimp cannot be exercised live.
