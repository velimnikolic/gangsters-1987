# The 1987 price of everything

*Pre-EPIC-9 economy baseline, written 2026-08-31. All figures are nominal 1987 US dollars,
anchored to documented Miami / US prices of 1985–1988 (sources at the bottom). $1 of 1987 ≈
$2.80 today. This document is the single authority on what anything costs; code constants
follow it, not the other way round.*

The point: when EPIC 9 (GAN-167) lets money flow, the numbers must already agree with each
other. A hood costs $420/week, so a shop paying $100/week protection funds a quarter of a
hood — the whole balance falls out of real prices instead of invented ones.

---

## 1. Wages (per day — the game pays on the day tick)

| Role | $/day | ≈ real anchor | Code today |
|---|---|---|---|
| Hood / soldier | 60 (+5 per attribute half-step) | gang enforcer ~$1,000/mo, but a full-time made man above that | `Wages.cs` 60+5 — **keep** |
| Lieutenant | 200 | documented local gang leader $4,000–11,000/mo | `Wages.cs` 200 — **keep** |
| Accountant | 250 | crooked professional premium | `Wages.cs` 250 — **keep** |
| Lawyer | 400 | drug-bar retainers ran to six figures a case | `Wages.cs` 400 — **keep** |
| Recruit signing fee | 500 | — | `Orders.cs` — **keep** |
| Hired lieutenant signing | 28 days of wage | — | `Wages.cs DaysDown` — **keep** |

Civilian yardsticks (for flavour and NPC dialogue): minimum wage $134/wk, median household
$500/wk, cop $23,000/yr entry, taxi driver $320/wk take-home.

**The wage layer already matches 1987 reality. Nothing rescales here.**

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
| Lawyer, per serious case | **10,000+** | — |
| Funeral (war cost, §23) | **2,700** | NFDA interpolated |
| Offload crew, per night | **7,500** | FL smuggling cases |
| Bribe (generic order) | **500** | — |
| Police employ (order) | **800/mo** | replaces flat 600 |
| Donation (church/politics) | **1,000+** | — |

## 8. Order economy (current `Orders.cs` → target)

| Order | Today | Target | Why |
|---|---|---|---|
| Extort (one-off shakedown) | 120 | **200** | a register + drawer |
| CollectProtection | 60 flat | **read the business tier** (§2) | flat constant dies |
| Raid | 300 | **500** | register + stock |
| Kidnap | 800 | **5,000** | ransom cut (§7) |
| RunBusiness | 90/day flat | **per-type net** (§3) | flat constant dies |
| BuyPremises | 2,500 | **per-type price** (§3) | — |
| SetUpBusiness | 1,200 | **20,000** | fit-out, licences |
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

## 10. Sanity check of the loop

A lieutenant (200/day) with four hoods (4×60/day) costs **$3,080/week**. His route of ten
tier-1/2 businesses brings **$2,000–4,000/week** — a crew roughly pays for itself and profit
comes from scale, tiers 3–4, owned fronts and (later) the drug flip. That reproduces the
design plan's §23 figure ("a man collects $8,000/week from his businesses") only for a good
earner on a fat route, which is exactly how it should read.

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
