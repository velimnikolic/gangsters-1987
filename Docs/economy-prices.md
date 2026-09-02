# The 1987 price of everything

*Pre-EPIC-9 economy baseline, written 2026-08-31. All figures are nominal 1987 US dollars,
anchored to documented Miami / US prices of 1985–1988 (sources at the bottom). $1 of 1987 ≈
$2.80 today. This document is the single authority on what anything costs; code constants
follow it, not the other way round.*

The point: the numbers have to agree with each other. A hood costs about $525/week, so ten
shopfronts paying $100/week fund roughly one of him once the street is frightened enough to
pay — the whole balance falls out of real prices instead of invented ones. §10 is where that
is asserted rather than asserted-in-prose.

---

## 1. Wages (per day — the game pays on the day tick)

*Rewritten 2026-09-02 for EPIC 24 (Crew Economy). Every figure here is what `Wages.cs`
actually produces; the measured columns come from `unity command eval` over
`RosterSeeder.GenerateStaffed` seeds 1–20 and 200 `RosterSeeder.Recruit` draws.*

**ONE TABLE.** What the house pays a man it raised is what that man is *worth*
(`Wages.HouseRate` is the only formula, and both `WageFor` and `WorthOf` read it). Nobody on
the house scale is ever underpaid. A pay gap can only open under a **bargain** — a man hired
out of the classified column whose stars have outgrown the price he printed.

| Role | Formula ($/day) | Range | Measured in play | Real anchor |
|---|---|---|---|---|
| Hood / soldier | `40 + 4 ×` (his **three best** trades in half-steps − 6) | 40–136 | recruit off the corner avg **75**; founding hoods **68–104** | gang enforcer ~$1,000/mo; a full-time made man above it |
| Lieutenant | `150 + 6 ×` (Leadership + Awareness + Organization + StreetAuthority in half-steps − 8) | 150–342 | founding lieutenants **192–246** | documented local gang leader $4,000–11,000/mo = $133–367/day |
| Boss | 0 | — | — | he owns the payroll; he does not draw from it |
| Accountant | 250 flat | — | — | crooked professional premium. **No code path assigns this specialty yet** |
| Lawyer | 400 flat | — | — | drug-bar retainers ran to six figures a case. **Hired out of the classified column** (GAN-245) — one lawyer, advertised every 7 days while the outfit has none |
| Service premium | `+2` per 30 days on the books, capped at **+20** | 0–20 | ~10 months to the cap | a man who has stood on the corner a year is not a corner boy |

The lieutenant **floor (150) is above the hood ceiling (136)**, so a lieutenant out-earns every
one of his men *by construction* and a promotion is always a rise — no per-crew rule, nothing to
remember. A hood is paid for his three best trades and nothing else: getting better at a fourth
thing nobody asks him for is not a raise.

Service pay is read off the day his file says he joined (`Career.JoinedDay`) — nothing new is
stored on a Character, and the joining line is exempt from the career cull for exactly this
reason. A man on a bargain draws no service premium: his ask was struck for the man he was.

| Hiring | Price | Code |
|---|---|---|
| Recruit signing fee | **500** | `EconomyPrices.RecruitSigning`. ONE PRICE THROUGH EVERY DOOR: the ledger's HIRE A MAN and the Recruit order both charge it, for every house |
| Classified ask | house rate for the lieutenant he advertises as **× 125%** | `Wages.AskFor` / `AskPremiumPercent`. Measured column: **270–307/day** |
| Classified signing | **14 days** of that ask | `Wages.DaysDown`. Measured: **$3,780–4,298** down, against a $25,000 opening safe |

The ask premium is the whole of the difference between a man the outfit built and a man who
walks in ready-made. **A rank change re-prices him**: `RosterOps.Promote` and `Demote` clear
`WageAsked`, and `GrantRaise` clears it too — he asked for the rate, so he is put *on* the rate
and his envelope follows his stars from then on. The old code wrote the demanded figure onto his
bargain, which froze it while his stars kept rising, so a greedy man came back with a new demand
every 35 days forever.

**Founding payroll:** seeds 1–20 open on **$570–682/day** (average $633), lieutenant plus five
hoods plus the Boss. The old table opened on $1,040.

Civilian yardsticks (for flavour and NPC dialogue): minimum wage $134/wk, median household
$500/wk, cop $23,000/yr entry, taxi driver $320/wk take-home.

### The short envelope

The safe **never goes negative from wages** (`CampaignRunner.TurnTheBooks`). What cannot be paid
is not paid, and every unpaid man is a printed event, never a silent modifier:

* the order is lieutenants first, then hoods by service (longest-standing first), then retained
  professionals — every envelope all or nothing;
* `DaySheet.WagesPaid` and `DaySheet.WagesShort` are both frozen at midnight, and the Finances
  page prints the short line in red;
* an unpaid man loses **3** loyalty that night, is read by the greed ladder as underpaid by his
  **whole** wage, and carries `UNPAID SINCE DAY d` on his file;
* a hood unpaid **3** nights running deserts; a lieutenant unpaid **5** goes over the next
  midnight, with his crew, through the defection pass that already exists.

## 2. Protection racket (weekly, per business)

Rule: **the take is ~5–10% of the business's weekly turnover**, floored and capped by tier.
Documented real rates: NYC mob ~$100/wk small shop, Chinatown $200/mo, gambling den $500/wk,
Philly street tax $500–1,000 per payment. The design plan's §12 example ($400/wk) sits in
tier 2.

| Tier | Game businesses | Weekly turnover | Protection $/wk | Precondition |
|---|---|---|---|---|
| 1 street | radnja/shopfront, cafe, terrace | 800–3,000 | **100** | none |
| 2 solid | pizzapub, diner, gym, auto/servis, bar | 3,000–8,000 | **300–500** | presence |
| 3 heavy | restaurant, fuel station, caryard, burger joint, gun shop, warehouse | 8,000–20,000 | **1,000–2,000** | Fear/Power threshold |
| 4 endgame | nightclub, hotel, harbor compound, industry, casino | 30,000+ | **5,000–10,000** | high standing + retaliation + heat (GAN-167 balance guard) |

Turnover anchors: barber-class $800/wk, bar $3,300/wk, diner $5,000/wk, corner store $8,000/wk
in-store, gas station $17,000/wk with fuel, hot Miami nightclub $30,000–60,000/wk.

## 3. Buying the world (premises and going concerns)

| Thing | Buy $ | Real anchor |
|---|---|---|
| Apartment (stan / safehouse) | **55,000** (rent $400/mo) | Miami condo est., census rents |
| House | **85,000** | Miami median SFH $82,400 ('85) |
| Empty storefront | **90,000** (lease $1,000/mo) | period retail leases ~$8–12/sqft/yr |
| Shopfront business (radnja) | **80,000** | c-store per-unit sales × broker multiple |
| Cafe / terrace | **60,000** | below diner class |
| Bar / pizzapub | **75,000** | bars grossed ~$180k/yr |
| Diner | **110,000** | 40-seat opening cost $162k (1980) |
| Gym | **100,000** | equipment-driven, laundromat class |
| Restaurant | **250,000** | full-service, 30–40% of gross |
| Auto repair / caryard | **150,000** | 80s franchise buy-ins $60–150k + stock |
| Burger joint (franchise) | **300,000** | McDonald's fee $45k + buildout |
| Gun shop | **120,000** | small retail + stock |
| Fuel station (with parcel) | **400,000** | dealer $150k, $400k with real estate |
| Warehouse (10,000 sqft) | **250,000** | $20–30/sqft |
| Fairground | **500,000** | — |
| Nightclub (diskoteka) | **750,000** | Club Nu buildout ~$2M ('86), Mutiny $17M ('84) — 750k is a mid club |
| Hotel / motel | **1,000,000** | $35,750/room × ~30 rooms |
| Harbor / industrial compound | **3,000,000+** | not buyable early; endgame asset |
| Casino | **5,000,000+** | no legal Miami casino in '87 — landmark, endgame only |

`RunBusiness` income for an owned business = its net, roughly **10–15% of turnover** (a
tier-1 radnja nets ~$100/day, a nightclub ~$700/day).

Temporary property-damage repairs (GAN-223): **$1,000** after a Smash Up and **$5,000**
after arson. These are owner-only early-reopening costs; waiting for the 3-day / 7-day
shutdown costs no cash, but the skipped protection take is never recovered.

## 4. Vehicles (street prices, no paperwork)

| Armory item | $ | Real anchor | Code today (`ArmoryCatalog.cs`) |
|---|---|---|---|
| Jalopy | **800** | used beater $300–2,000 | 800 — **keep** |
| Sedan | **4,000** | new Taurus/Caprice $11.5k, this is a clean used one | 1,500 → up |
| Panel Van | **5,000** | — | 2,400 → up |
| Armoured Wagon | **30,000** | luxury base $22k + armour | 6,000 → up |
| Moped | **500** | — | 500 — **keep** |
| Enduro | **1,500** | small bike $1,300–2,500 | 900 → up |
| Motorbike | **2,500** | — | 1,200 → up |
| Tourer | **8,500** | Harley FLHS MSRP $8,545 ('87) | 1,400 → up |

Flavour ceiling: Testarossa ~$125,000 — the boss's car, if it ever exists.

## 5. Weapons (street, no questions asked)

| Item | $ | Real anchor | Code today |
|---|---|---|---|
| Cheap pistol | **150** | street handguns mostly under $100; Saturday night special $50 retail | Twin Pistols 250 → 150 |
| Magnum | **450** | quality revolver $300–400 retail + street markup | 450 — **keep** |
| Shotgun | **300** | legal retail ~$250 | 750 → down |
| Machine pistol (MAC/TEC class) | **600** | MAC-10 ~$600 right after the '86 ban | 1,250 → down |
| Rifle | **800** | — | 1,750 → down |
| Tommy gun | **2,000** | transferable full-autos $2,000–4,000 late 80s — a period-true collector piece | 2,000 — **keep** |
| Grenade | **175** | undocumented; plausible | 175 — **keep** |

## 6. Drugs (reference for the future trade system — no system exists yet)

| Item | $ |
|---|---|
| Cocaine, kilo wholesale (1987 Miami) | **14,000** (was $55,000 in 1981 — the crash is period colour) |
| Cocaine, gram street | **100** |
| Crack vial | **10** |
| Marijuana, lb commercial / sinsemilla | **500** / **2,000** |
| Heroin, gram | **2,200** |

Margin note: one kilo cut and retailed is ~$70,000–100,000 street — a 5–7× flip, which is
why it must carry proportional heat/risk when the system lands.

## 7. Crime services, corruption, war costs

| Item | $ | Real anchor |
|---|---|---|
| Contract killing | **15,000** | documented $2k down on $20k; $5k on a $40k promise |
| Kidnap ransom (order payout) | **5,000–25,000** | — |
| Cop on the pad | **800/mo** per officer | Knapp pad $400–1,500/mo |
| Judge / case fix | **2,000–10,000** | Greylord: $100 traffic to thousands for felonies |
| Bail, felony | **10,000** (drug felony to $37,500) | BJS |
| Bail — extortion / intimidating a witness | **2,000** | the low end of the BJS felony series; a shakedown charge with one complainant behind it |
| Bail — affray | **5,000** | firearms in the street, still short of a body |
| Bail — murder | **25,000** | BJS murder bail runs to the high tens of thousands |
| Bail — killing a policeman | **none, at any price** | no judge in 1987 Miami bails one |
| Lawyer, per serious case | **10,000+** | — |
| Funeral (war cost, §23) | **2,700** | NFDA interpolated |
| Offload crew, per night | **7,500** | FL smuggling cases |
| Bribe (generic order) | **500** | — |
| Police employ (order) | **800/mo** | replaces flat 600 |
| Donation (church/politics) | **1,000+** | — |

### Signing a man — the one rule

There is one price for putting a name on the books, `EconomyPrices.RecruitSigning`
(**$500**), and it is charged through every door there is:

* the ledger's **HIRE A MAN** on the ORGANIZATION page — `HouseOps.Recruit`, paid out of
  that house's own safe, and the signing never fails once it is paid for;
* the **Recruit ORDER** — the same money as the order's cost, paid at issue; it differs
  only by taking twelve hours and letting the recruiter's Awareness find a better man
  (`RosterSeeder.Recruit`'s second looks). A failed roll still brings a man back - a
  walk-in, with no bonus.

The counter used to charge $50 while the corner charged $500, which made the counter the
only sane way to grow for no reason anybody had decided. `PersonnelDirector.DefaultHoodRecruitmentCost` and its serialized override are deleted.

The classified column is a separate thing and keeps its own price: a man who advertises
is a lieutenant, asks the house rate for the rank plus a 25% market premium
(`Wages.AskFor`), and wants **14 days of that** down (`Wages.DaysDown`). See §1.

**The lawyer advertises there too** (GAN-245). He is priced off the same table — the wage
table reads a SPECIALTY before it reads a rank, so his ask is `LawyerWage × 125%` = **$500
a day** with **$7,000** down — and he signs as a specialist: no rank, no crew, no place in
the chain of command. One ad every `HireMarket.LawyerAdEveryDays` (7) days, and none at all
while the outfit already has counsel on the books.

### Bail — the money that gets a man out

`EconomyPrices.Bail(deed)` is the door and `Sentencing.Bail` is the table it reads, so the
sentence bands and the price of avoiding them live on one row each. Bail leaves the safe
through the one purchase gate (`OutfitDirector.Purchase` → `BalanceMath.TryPurchase`) and
lands on the day sheet like any other cost. **A forfeited bail is not refunded**: a man who
does not appear costs the outfit the money AND leaves the case open against him.

It also needs a lawyer: below `Lawyer.BailSkill` (2 of 5) nobody gets a remand hearing
listed, which is the first thing the retainer actually buys.

## 8. Order economy (current `Orders.cs` → target)

| Order | Today | Target | Why |
|---|---|---|---|
| Extort (one-off shakedown) | 120 | **200** | a register + drawer |
| CollectProtection | 60 flat | **read the business tier** (§2) | flat constant dies |
| Raid | 300 | **500** | register + stock |
| Kidnap | 800 | **5,000** | ransom cut (§7) |
| RunBusiness | 90/day flat | **per-type net** (§3) | flat constant dies |
| BuyPremises | 2,500 | **per-type price** (§3) | — |
| SetUpBusiness | 1,200 | **20,000** | fit-out, licences - **refused until RIVAL-009**: the order charges the fit-out and opens nothing, so the counter turns it away and the price waits |
| Bribe | 400 | **500** | — |
| EmployPolice | 600 | **800/mo** | Knapp pad |
| Donate | 500 | **1,000** | — |

## 9. The treasury

| Constant | Today | Target | Why |
|---|---|---|---|
| `Accounts.StartingSafe` | 1,000,000 | **25,000** | a million buys the whole cenovnik on day one and voids the GAN-167 casino guard. $25k = a few weeks of payroll + a cheap front + guns: racket first, buy later. $3M (design §22) is an endgame treasury, not an opening one. |
| `Tribute.PerBlockAhead` | 90 | **500** | scale with the new order economy |
| `Tribute.Floor` | 150 | **1,000** | — |
| Tax 30% profit, risk ceilings 5k/20k | — | **revisit ceilings ×5** at the new scale | `Accounts.cs` |
| `PlayerArsenal.TryPay` | stub, always yes | wire to the safe when EPIC 9 lands | the only money seam with no balance behind it |

## 10. Sanity check of the loop — one block, one crew

*The yardstick the whole territory economy is balanced against, and it is a TEST:
`WageTests.OneBlockCarriesOneCrew` asserts every line of this section and reads `EconomyPrices`
and `Wages` only, so retuning either side re-runs it.*

**The city, measured (seed 1987):** 3,246 businesses on 110 blocks. The median block is 40-odd
doors — 41 tier-1 and 3 tier-2 — owing about **$5,600/week** at full compliance. Quartiles:
bottom **$1,800**, median **$5,600**, top **$7,000**, richest **$11,600**. Tier 1 is 54% of all
racket money in the city; tier 3–4 is three businesses in the whole map.

**What is actually collected**, over `TerritoryPaymentRoll` with the dealt owners: fear 30 pays
**48%** of what is owed, fear 50 **68%**, fear 70 and over **91%**. A fortnight of daily rounds
on the median block banks **$5,837 / $8,132 / $10,409** at those three readings.

| Crew | $/day | a fortnight | median block @ fear 50 | @ fear 70 |
|---|---|---|---|---|
| lieutenant + 2 hoods | 378 | 5,292 | **+2,840** | +5,117 |
| lieutenant + 4 hoods | 542 | 7,588 | +544 | **+2,821** |
| lieutenant + 4 hoods @ fear 30 | 542 | 7,588 | — | **−1,751** |

**The rule of thumb: one hood ≈ ten tier-1 doors under fear; a full crew ≈ a whole median block
under fear.** A small crew holds a block with margin. A full crew needs the block properly
frightened, or a second block. The bottom quartile never feeds a crew and is not meant to.

The old table failed this: a full crew cost $870/day — **$12,180 a fortnight** — which the
*best* block in four could not carry. That comparison is the negative control inside the test,
so the yardstick cannot quietly stop discriminating.

**Measurement note, not a change (EPIC 10, not this one):** `Tribute.PerBlockAhead` is 500 with
a floor of 1,000, levied every 5 days by every house holding more blocks than the outfit — among
21 families that can exceed the whole payroll for a one-block outfit. Measure `Tribute.Levies`
on day 2 in Play before blaming wages for a broke opening, and file it against EPIC 10 if it
reads high. This epic does not touch tribute.

---

### Sources (representative)

Cocaine/DEA price history 1980–1990 (dea.gov histories; ONDCP/IDA price & purity series) ·
gang wages: Levitt & Venkatesh, QJE 2000 (Chicago 1987–91 crack gang books) · hit fees: UPI
1986/1987 murder-for-hire cases · pads: Knapp Commission; fixes: Operation Greylord · guns:
Sheley & Wright NIJ survey; Rock Island Auction MAC-10 history · protection rates: Ghost
Shadows RICO (1985), US v. Scarfo · Miami home price: US League of Savings 1985 metro survey
(UPI) · clubs: Miami New Times (Club Nu), Vice (Mutiny) · business per-unit revenue: Gale
industry encyclopedia (SIC 5812/5813/7241), CSNews/NACS · vehicles: 1987 MSRPs (JD Power,
CarGurus, Retrowow ads) · wages/CPI: DOL, Census P-60, BJS 1987 law-enforcement profile.
