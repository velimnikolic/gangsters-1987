# Who are they — the book on the other houses (EPIC 43, draft 2)

Design brief, written 2026-09-05 and **reworked the same day** after the contrarian pass and the
user's rulings. Linear: EPIC 43 = **GAN-419**, tickets `INTEL-000..009` = **GAN-420..429** (INTEL-000 = GAN-420, INTEL-009 = GAN-429), label Rivals, all in Backlog.

The user's words: "malo mi je glupo da odma znaš ko su neprijatelji, treba mi plan kako da otkriješ
ko je enemy. istraži kako je Gangsters: Organized Crime to radio."

So: the Outfit arrives in the city knowing nobody. Every fact about another house — that it exists,
what it is called, which doors are its, who runs it, what it is worth — has to be **found out**,
and the ledger says *how* it was found out. The rival minds and the truth ledgers do not change;
what changes is what the player can read and which verbs he may file.

Sits beside **EPIC 25** the houses (`Docs/rival-families.md`), **EPIC 42** the table
(`Docs/diplomacy.md`), **EPIC 40** the street event book (`Docs/street-events.md`) and **EPIC 35**
the paper (`Docs/newspaper.md`). It fulfils a promise the FAMILIES page already prints and does
not keep: `LedgerText.StrengthUnknown` — "Unknown — no eyes inside … reconnaissance is work, not
a birthright" (`UI/PersonnelAlmanac.Diplomacy.cs:462-466`).

Whoever implements a ticket of this epic moves that ticket, and when the last one lands the epic
itself, to **Done** in Linear.

## 0. Rulings (2026-09-05)

1. **Day one, the Outfit has heard of nobody.** [user: "nikoga", and again "nikoga" after the
   measured silent week in §0b] Not even the neighbour whose ground touches ours. The way in is
   ACTIVE: the player sends men to a door, walks past a front, asks around. A passive Don hears
   nothing, and that is correct.
2. **A door held by a house we have not placed paints like any other shop.** [user: "isto ko
   bilo koja radnja"] No *somebody's* hatch. On the map a rival's door is indistinguishable from
   an unprotected one until it is known; the block's fear reading is the only hint.
3. **A stranger's surname is learnt on sight.** [user: "hmm prezime"] A name is said on the
   street; the HOUSE behind it is not. `STRANGER` → `ROSSI` → `FALCONE · ROSSI`. The one exception
   is the contrarian's: a rival Don's surname IS his family's name (`Personnel/RosterSeeder.cs:367-377`),
   so a `Rank.Boss` prints `STRANGER · THE BOSS` until his house is Heard.
4. **The minds keep the city they know.** [user: "da"] The newcomer fiction stands; symmetric
   knowledge is §9's first seam, a later epic. `HouseView.HeardLook` is laid now so it is a
   one-line change then.
5. **The contrarian's findings are adopted** [user: "usvoji njegove nalaze"] — every one in §0b,
   with ruling 1 overriding its day-one-neighbour recommendation.

## 0a. How Gangsters: Organized Crime (Hothouse, 1998) did it

What the sources say, and only that:

| in Gangsters | source |
|---|---|
| Rival hoods are **unidentified** until you pay for the name: "pay snitches to identify the newest members of their gangs (unidentified rival hoods are bad news)" | Hardcore Gaming 101 |
| The map shows **only what you have found out**: "filters and icons for highlighting your territory, **any info you have on rival gangs**, which neighborhoods hate you the most, and recent events such as shootings, robberies, and bombings" | HG101 |
| During the working week you can "**tail**, assault, or kill any rival gang members you can find" | HG101 |
| At the start a player has **nothing** on the smaller gangs: "We have no information about their HQs or territories location" | CivFanatics AAR |
| Scouting is done by a hood picked for **Stealth** and Intelligence, and a man the other side has already seen is useless for it: "I know a lot of his hoods, and he probably knows a lot of mine too, so I can't use some I already have" | AAR |
| A rival's HQ is found **through conflict** as much as through scouting: "If that tenement is his HQ then trouble lies ahead" | AAR |
| **Kidnap → interrogate at your HQ** to learn about the rival gang; assault him if he will not talk | GameFAQs strategy guide (via search summary; the guide itself is behind Cloudflare) |
| The **paper** tells you "if your rivals are getting too big for their zoot suits"; charts rank the gangs by influence, suspicion, money, loyalty | HG101 |
| Diplomacy settings run "anywhere from alliance to all-out war"; unknown hoods on your turf are the thing the snitch fee buys you out of | HG101 |

From memory, **not verified** by a source and not relied on below: the Gang Info screen listed each
rival's hoods as blank silhouettes until identified, and a `Watch` order on a building reported who
came and went.

The shape to take from it: **identity is bought, tailed or beaten out of somebody**; the map paints
only what you know; the paper and the street volunteer the rest slowly; and knowing a man cuts
both ways.

Sources: [HG101](https://www.hardcoregaming101.net/gangsters-organized-crime/),
[CivFanatics AAR](https://forums.civfanatics.com/threads/gangsters-organized-crime-aar.193941/),
[Wikipedia](https://en.wikipedia.org/wiki/Gangsters:_Organized_Crime),
[GameFAQs guide](https://gamefaqs.gamespot.com/pc/75245-gangsters/faqs/11170).

## 0b. What the contrarian pass changed (2026-09-05)

The verdict was "ship with the listed changes" — the design stands, the text was wrong in the
places below. Every one is folded into the sections that follow.

* **A mind's war never passes `HouseOps.Propose`.** It is `HouseIntent.Stand(them, War)`
  (`Outfit/HouseMind.cs:881-885`, "WAR IS DECLARED, NEVER PROPOSED") carried as
  `Relations.SetPending` (`RoadDemo/TerritoryRuntime.Minds.cs:1234-1239`); a pact writes the same
  way (`Outfit/HouseDiplomacy.cs:1263`). Draft 1's `TheyToldUs` hook would have missed it and the
  player could be at war — on sight — with a house he had never Heard. Fixed: §3 hooks the
  `SetStance` intent against the player and every `StanceLanded` against the player after
  `Underworld.cs:505` (sorted by gang id; `landed` is filled from a dictionary enumeration,
  `HouseRelations.cs:318`).
* **The silent week is measured.** `gangsters_underworld_sim --seed 1987 --days 7 --houses 3`
  with the player passive: 0 proposals, grudge 0, wars 0, cards 0/0/0 for the player. A mind never
  asks a door another house protects (`HouseMind.cs:1594-1597`); the ladder's first word needs
  grievance 10, which early on only `BorderPressure` supplies and only for a squeezed house
  (`Minds.cs:561-566`); the paper needs a death or three witnesses (`News/PressRecord.cs:48-50`).
  The user kept ruling 1, so the entry is ACTIVE (§3a): our own men at a protected door, a front
  walked past, ASK AROUND on any street with a door that pays somebody we have not Heard, and
  THE STRANGERS spoken by a shopkeeper who pays us, not only by a capo.
* **A dozen leaks draft 1 did not list** — the M map's tiles and tooltip, the minimap band and
  dots, the notice HUD, the order sheet's refusal, the door status line, the door menu's
  "BOSS · Falcone", the building popup, the front overlay, `CrewOverlay.RivalInk`,
  `PrintEverywhere`, the defector's door (§1). Fixed by construction: **one choke point**,
  `HouseKnowledge.NameFor / InkFor`, every caller routed through it, and a test that no printed
  line carries an Unheard name (§6, INTEL-005).
* **Three free names undercut the paid verbs**: a rival Don's surname is the family name; the
  pavement mark at a front prints `front.GangName` in the family tint (`RoadDemo/TurfMarks.cs:136-151,
  274-299`); the paper's filler draws `{GANG}` from all 21 catalog names daily
  (`News/HeadlineGenerator.cs:239-249`). Fixed: ruling 3's Boss exception; a front walked past
  DOES hear (the sign, the cars, the men — the AAR's scouting, said so in §3); the filler draws
  from Heard houses only and skips gang templates while that set is empty.
* **Two books, one truth.** `TurfKnowledge` is static, in `RoadDemo`, Unity-bound
  (`TurfKnowledge.cs:2, 25, 173-179`) with a top-level DTO; per-house books live on `House` and save
  on `HouseDto`. Fixed: ONE owner — `House.Knowledge`, pure, saved as a nullable `knowledge` row on
  `HouseDto`; `TurfKnowledge` becomes the scene sweep that calls into it and keeps nothing (§7).
* **`ResolveEscalation` names a house for a SmashUp too** (`TerritoryRuntime.cs:1708-1740` handles
  `Assault` and `PropertyDamage`); and the door that leaves us (`racket.Switch`,
  `TerritoryRuntime.cs:1951-1960`) — the one man who certainly knows the name — told us nothing.
  Fixed: `ADoorTold` on `Assault` and on `Switch`; `PropertyDamage` files unattributed.
* **THE STRANGERS' score could not be computed from the player's view** (`PresenceLook` is our
  presence only, `Minds.cs:945-946`; `HouseIncident` carries no actor). Fixed: `HouseView.HeardLook`
  and the score off `HouseDoor.Protector` on our blocks and their neighbours plus the unattributed
  count.
* **The gate shipped before the verbs.** Fixed order: `LearnMan` first and tiny, the book, the
  sources, the two ways in, THEN the choke point and the surfaces, then the two paid verbs, then
  the yardstick (§8).
* **An old file loaded as "heard nobody" on a campaign at war.** Fixed: Heard is derived on load
  when the row is missing — stance not Peace or pending, an open proposal either way, a known
  `front:N`, `MenLostTo(N) > 0` — source "the old book". No version bump.
* Minor, all taken: ASK AROUND keys on saved facts (sealed actors, then protectors), presence
  last, ties to the lowest gang id; Inside never erases, the figure ages ("as of day 12"); TAIL
  gets a payoff nothing else has (the man's rank and his capo); the toasts and the incident slips
  are frozen text and §4 says so; `PrintEverywhere` filters per book; the defector's door
  (`OpenDoors.cs:207`) is Heard by `TheyToldUs` — a man who walked out told us where.

## 1. What the player knows today on day one, and should not

Verified in code 2026-09-05 (the second half by the contrarian):

| surface | what it hands over on day 1 | where |
|---|---|---|
| FAMILIES card index | **every** dealt house: name, colour, the capo's full name ("RUN BY …"), stance, CAPOS count off the real roster, POWER off the real ledger, "X is the door" off the real front | `UI/PersonnelAlmanac.Diplomacy.cs:103-115` (all of `GangRegistry.Gangs`), `:280-297`, `:342-354` (`PowerFigure` → `TerritoryRuntime.PowerOf`), `:388-402` (`GangRegistry.FrontBusinessOf`) |
| turf map wash, districts, key | every building in its owner's colour; district majority off truth; the key lists every house | `RoadDemo/TurfMapSurvey.cs:391-394` `ReadOwners`, `:402-410`, `:2165-2195` |
| street overlay | every rival man tagged `FALCONE · ROSSI`, every rival marker in the palette colour | `RoadDemo/CrewOverlay.cs:2747`, `:89-95` `RivalInk` |
| THE WIRE toasts | "FALCONE HAVE TAKEN THE …", "FALCONE TOOK THE BAG OFF …" | `RoadDemo/DemoCrews.Boarding.cs:877`, `RoadDemo/BagCarry.cs:308` |
| strategic map (M) | every business tile in its owner's colour; leader name and colour in the block tooltip; dominant holder | `UI/StrategicMapHud.cs:1429, 1561-1563, 1574-1576`; `MapAffiliation` colours people dots only, provider set at `Gameplay/GangDirector.cs:101-104` |
| minimap | district band "FALCONE"; live dots in `TurfHouses.For(unit.Faction).Ink` | `RoadDemo/TurfMinimap.cs:423, 802` |
| notice HUD | "Riverside — Falcone is working it harder" | `RoadDemo/TerritoryNoticeHud.cs:205, 290` |
| order sheet | EXTORT refused as "held by Falcone" | `UI/PersonnelAlmanac.Orders.cs:555-561, 588` |
| door status, block file | the protector's name | `RoadDemo/TerritoryRuntime.cs:2257-2260`, `TerritoryRuntime.Seam.cs:344-349` (`RivalName`), `UI/PersonnelAlmanac.BlockFile.cs:1575` |
| door menu | a rival front's OwnerName is its lieutenant's full name; "BOSS · Falcone" | `UI/DoorMenu.cs:284-296, 766` |
| building popup, front overlay | `GangRegistry.NameOf(GangId)`; "FALCONE FAMILY" with a colour stripe | `Entities/BusinessMarker.cs:239-240`, `RoadDemo/FrontOverlay.cs:205-209` |
| map context menu | enemy crew menu title `target.GangName` | `RoadDemo/TurfMapPanel.cs:634-635` |
| every book | `PrintEverywhere`: pact stands / pact broken / envoy shot, three names into every book | `Outfit/HouseDiplomacy.cs:1264-1290`, `Outfit/HouseOps.cs:439-441` |
| the paper's filler | `{GANG}` from all 21 catalog names, dealt or not | `News/HeadlineGenerator.cs:239-249`, `News/Edition.cs:33` |
| the older gang-agent overlay | colour and `NameOf` | `Entities/GangMemberAgent.cs:153, 162` — verify whether a live scene still stands it |

What already gates, and stays: a rival's **door** is a rumour until one of our crews has been within
`TurfKnowledge.LearnRange` (28 m) of it or an `Explore` brought it back (`TurfMarks.Discover`,
`OutfitDirector.Learned :860-886`); the door menu and the card's TURF pips read only known doors
(`UI/DoorMenu.cs:311-323`, `OutfitDirector.CollectKnownHoldings`). `TurfKnowledge.LearnMan` has no
caller anywhere in `Assets/` or `Tools/`: the faces book has been empty since it was written. The
paper's `Named / Seen / Unknown` attribution (`News/PressRecord.cs:57-64`; `PressText.cs:28, 59`
prints the family only for Seen/Named) is the one place the project already says "we could not
tell whose men they were".

## 2. The rule

A house is one of four things to the Outfit, and every surface reads the level and nothing past it:

| level | what it means | what the ledger may print |
|---|---|---|
| **Unheard** | nobody has said the name to us | nothing. No card. Their doors paint like any other shop (ruling 2). Their men are *strangers* (a Don: `STRANGER · THE BOSS`). An act of theirs is by *men nobody could name* |
| **Heard** | we have the name and how we got it | the card, the colour on the map key, the STANDING row, THE TABLE (a word needs a name) |
| **Placed** | at least one door of theirs is known AND they are Heard | that door in their colour on the map, "X is the door" once the FRONT is known, TURF pips |
| **Inside** | we have had a man of theirs talking, or one of ours in their house | CAPOS, POWER, endurance, printed "as of day N" and never erased — the figure ages, it is not forgotten |

A door learnt before its house is Heard (walked past, Explore) is a door we know pays *somebody*:
it is on the book, it paints like any shop (ruling 2), the door menu says "pays somebody — he
would not say who", and it turns their colour the day the house is Heard.

The truth ledgers — `HouseRelations`, the control ledger, the minds' `HouseView` — do not change.
A mind keeps playing the city it has lived in for years. The Outfit is the newcomer
(Gangsters' "virgin territory"; `GangCatalog.BossName` arrives in 1987), and that is the fiction
that pays for the asymmetry (ruling 4).

**Every fact carries its source and its day**, in words (the UI rule, `[[mechanics-explained-in-ui]]`):
"heard from the barber on Riverside, day 3", "the paper, day 5", "Rossi gave them up, day 9".

## 3. What names a house — the free sources

| source | when | where it hooks | `KnowledgeSource` |
|---|---|---|---|
| **They told us** | a proposal filed TO the player (a word, a bill, a truce, terms, a ransom note); **a war or a pact's war against the player** — at the `SetStance` intent (`Minds.cs:1237`) AND at every `StanceLanded` against the player after `Underworld.cs:505`; a levy assessed on us (`SettleTribute`); a lieutenant of ours who walked out through their door (`OpenDoors.cs:207`) | `HouseOps.Propose` when `To == player`; `Carry`'s `SetStance` case; `Underworld.DayTick` after `ApplyPending`; `Tribute`; `CampaignRunner` defection | `TheyToldUs` |
| **A door told us** | their men at a door that pays us with a SPOKEN act — approach, intimidation, collection, a beating (`TerritoryEscalationKind.Assault`), never `PropertyDamage`; **a door of ours that switches to them** (`racket.Switch`, `TerritoryRuntime.cs:1951-1960`: "the tailor pays somebody else now, and says who"); **OUR men at a door that pays them** — the shopkeeper answers "I already pay the Falcones" (the man-at-the-door precondition: a crew standing at the door through `ApproachBusiness`; the ORDER SHEET's refusal at `PersonnelAlmanac.Orders.cs:555-561` says only "held by another house") | `ResolveEscalation` on `Assault` with a protector that is us; `Switch`; the approach chain's "already protected" branch when the actor is the player | `ADoorTold` |
| **The paper** | a `Named` or `Seen` press record naming a house | `PressBook` at the 06:00 cut | `ThePaper` |
| **Their front** | a crew of ours within `LearnRange` of a house's FRONT — the sign, the cars, the men outside. A plain door walked past is learnt as a door, not as a name | `TurfMarks.Discover` (fronts only) | `Seen` |
| **Seen at their door** | a known face within `LearnRange` of a door already known to be theirs (the house Heard) | the same sweep, faces added — `LearnMan` finally called | `Seen` |
| **The envoy** | our lieutenant delivered a sit-down at their front | `Underworld.SitDown` → `HouseOps.Deliver` | `TheEnvoy` |

**What does NOT name a house:** a bomb, a torch, a killing, a drive-by, a smash-up
(`PropertyDamage`), a car taken, a bag taken — unless the paper names them (three witnesses) or a
face on the job is already attached. Those are filed against **men nobody could name** (§4). A house
that hits you quietly is a house you have to go and find.

### 3a. The way in, on day one

Ruling 1 plus the measured silent week (§0b) mean a passive Don hears nothing for a week, and that
is the design. What the player DOES on day one that hears somebody:

1. **Send a crew to a door on the next street** (`ApproachBusiness`). If it pays somebody, the
   owner says who — `ADoorTold`, free.
2. **Explore or walk a block.** Doors are learnt; a FRONT on it is Heard and Placed (`Seen`).
3. **ASK AROUND** on any street where a door pays somebody we have not Heard, or where men
   nobody could name have been (§5) — priced.
4. **THE STRANGERS** on THE PHONE (§5), spoken by a shopkeeper who pays us when a door on our
   ground or beside it pays a house we have not Heard — a house with no lieutenant still gets it.

INTEL-009's yardstick measures the ACTIVE case: with F3's bench state and one `Explore` of a
neighbouring block on day 1, a house is Heard by day 3 on every seed; the passive Don's zero is
printed and is not a failure.

## 4. Men nobody could name — the unattributed log

`HouseKnowledge.Unattributed`: an append-only list of (day, hour, block, kind, actor gang id kept
**sealed**). The wire and the incident line print `MEN NOBODY COULD NAME HIT THE BARBER ON
RIVERSIDE`; the block file counts them: "three hits on this street by men nobody could name ·
ASK AROUND". When the actor becomes Heard by a source that can tie it — a snitch on that block,
a tail, the question — the rows are re-read as theirs and the card's RECORD lists them.

**Frozen text stays frozen.** The incident book carries rendered sentences and no actor
(`Personnel/Incident.cs:339-357`); the street's "HAVE TAKEN THE …" lines are `CrewOverlay.Announce`
toasts, not incidents (`DemoCrews.Boarding.cs:875-878`). Neither is rewritten. Only
`Unattributed` is re-read — the paper's rule: no old story is improved with later truth.

The truth grievance (`HouseRelations.Note`) is filed as today, so their ladder toward us and ours
toward them are unchanged; the grudge is ours, the name is not. What the player cannot do is
*answer* — no word, no war, no bill — until he has the name. `HouseDiplomacy.WhyNot` gets one more
reason: `NoName` — "you cannot warn off men you cannot name".

## 5. The paid sources — the verbs

### ASK AROUND — the snitch (`OrderType.AskAround`)

`Explore`'s brother: `TargetMode.Area`, a crew standing on the block, `JobResolution.Roll`
(seeded `OrderResolution.Mix(seed, day, jobId)` like every job, `OrderResolution.cs:599-611`),
8 hours. Price by the block's tier — `EconomyPrices.SnitchFee` $200 / $500 / $1,000 (1987,
`Docs/economy-prices.md` gets the row). The roll: our best `Streetwise` on the block against the
block's fear of the house being asked about (a street that is terrified of them does not talk).

**Which house the snitch names** is keyed on SAVED facts first, so the paper city and the editor
agree on one seed: the sealed actor of the newest unattributed row on the block, else the protector
of a door on the block that is not Heard, else (presence is unsaved, D19) the house with the most
presence — ties to the lowest gang id — and the wire prints which it was.

* success: that house is **Heard** (`ASnitch`), one of its doors on the block is learnt, and every
  unattributed row on that block whose sealed actor is that house is tied. Wire: "A man on
  Riverside says the ones leaning on the barber are Falcone's."
* fail: "Nobody on Riverside is talking." The money is gone; the block cools for 3 days
  (`SnitchCooldownDays`), printed in the block file.
* nothing to learn (no door paying an Unheard house, nothing unattributed): refused at the gateway
  in words — "nobody has been on this street but us" — before the money moves.

### THE STRANGERS — the card (`EventId.Strangers`)

The entry point for a house that has not gone looking. An `EventDef` whose `Score` reads the
player's own view and nothing else: `view.Blocks` and `Neighbours(block)` → `Businesses(block)` →
`HouseDoor.Protector` not Heard (`view.Heard(house)`, the new `HeardLook`), plus the unattributed
count inside 3 days. Dealt on THE PHONE; the speaker is a lieutenant when there is one and
otherwise **a shopkeeper who pays us** ("Mr Ricci, it's Tony from the tailor's — men have been
at the barber's across the street all week; nobody knows them"), so a lone Don is told.
Rows: **ASK AROUND** (the fee, that block) · **SEND MEN OVER** (an `ApproachBusiness` at that door
— the owner will say who; needs a crew) · **WALK AWAY** (cooling). Gates and holds are the book's
own (`NoMoney`, `NoCrew`).

### TAIL HIM (`OrderType.Tail`)

Target: a **seen face** (`TargetMode.Character`, the face in the book and seen inside
`TailWithinDays` = 1). The crew walks to where he was last seen (the chase layer's remembered
place, `[[chase-a-remembered-place]]`), 12 hours, `JobResolution.Roll`: our best `Stealth` against
his `Awareness` (the attribute's own text: "scouting another outfit's turf").

The payoff nothing else gives (the contrarian's condition for keeping the verb): **the man** — his
rank, the capo he answers to (named on the card: RUN BY), and the door he went home to — their
FRONT if he is a lieutenant, a door of theirs otherwise. His house is Heard (`Seen`), his face
attached. Wire: "Rossi went home to the Blue Note on Fifth. He answers to Marco Falcone."

* blown: he saw the tail. `GrievanceKind.Tailed` (10, appended and named in `AmountOf`) to his
  house; at war, the street engages where the two crews stand. Nothing learnt. The face is marked
  `Burned` for `BurnedDays`: a man the other side has looked at is no good for the next tail — the
  AAR's rule.

v1 resolves on paper after the walk, like `Explore`. A tail played out on the pavement is a seam.

### PUT THE QUESTION — the kidnap's third key

A ransom we hold (`ProposalKind.Ransom` filed by us, `Underworld.TakeHim :418-443`) gets a third
key on THE TABLE beside the money: **PUT THE QUESTION**. Roll: our best `Intimidation` in the
holding crew against his `Loyalty`.

* he talks: his house is Heard, its front and `QuestionDoors` (3) of its doors are learnt, the capo
  he answers to is named on the card, and if he is a lieutenant the book is **Inside** as of today.
  Wire: "Rossi gave them up."
* he does not: nothing, and the key is spent for this man. The ransom stands either way; he goes
  home on his day beaten (the kidnap's `ManTaken` already prices the taking; the question adds
  `Questioned` (10) so a house whose man was worked over has a reason on the ladder).

### Eyes inside

`Inside` is a day stamp. CAPOS and POWER print "9 · as of day 12" for ever after; the figure is
what we had, and it ages in the reader's eye — nothing on the book is ever erased
(`TurfKnowledge`'s own rule, "the player does not unlearn the address"). A man of theirs who comes
over to us stamps Inside on the day he walks in — once a mind's lieutenant can defect to the
player (EPIC 17 records only the other direction). Seam, §9.

## 6. The surfaces — one choke point

Draft 1 listed six surfaces; the contrarian found twelve more. The fix is not a longer list: it is
**one seam every printed name and every painted ink goes through**, and a test that greps the
output.

`HouseKnowledge.NameFor(gangId)` → the house's name once Heard, else "another house" (the words the
block file already uses); `InkFor(gangId)` → `GangPalette.Of` once Heard, else the map's neutral ink.
Every caller of `GangCatalog.Names[…]`, `GangRegistry.NameOf`, `GangPalette.Of`, `Unit.GangName`,
`GangFront.GangName` and `TurfHouses.For(...).Ink` that prints to the PLAYER goes through it — the
local wrappers that exist are the choke points: `DoorMenu.GangName` (`DoorMenu.cs:327-334`),
`TerritoryRuntime.GangName` (`TerritoryRuntime.cs:2312-2319`), `PressText.Family`
(`PressText.cs:305-312`). Truth callers (the audit, the sim's table, the tests, the minds) keep the
catalogue. `gangsters_intel_tests` asserts that no line the ledger, the paper, the wire, the notice
HUD or the map would print carries an Unheard name.

Then the surfaces are what the seam makes them:

| surface | reads |
|---|---|
| **FAMILIES** | cards for Heard houses only, in the order heard. Head: "TWO HOUSES HEARD OF · THE STREET SAYS THERE ARE MORE" until every dealt house is Heard, then "EVERY HOUSE IN THE CITY". RUN BY: the capo once named (a tail, the question, a snitch on his block), else PERSONS UNKNOWN. CAPOS / POWER: Inside only, "as of day N". The door: the FRONT once in the book. A margin line per row: the source and the day. The rail's `N AT WAR` (`PersonnelAlmanac.cs:2165-2169`) counts Heard wars — with §3's hook a war is always Heard |
| **turf map** | the wash paints a door in a house's colour only when Placed (Heard + known); every other door paints as it paints today for an unprotected one (ruling 2). Block readings and district majority are taken over Placed doors. The key lists Heard houses. Live marks: Heard houses in ink, everybody else the neutral ink |
| **minimap, M map, notice HUD, front overlay, building popup, door menu, order sheet, block file, map context menu** | through `NameFor` / `InkFor`; the order sheet's refusal reads "held by another house — send men to the door and he will say who" |
| **street overlay** | a face not in the book: `STRANGER`; seen (28 m): his surname, a Boss excepted (`STRANGER · THE BOSS`); attached: `FALCONE · ROSSI`. `RivalInk` through `InkFor` |
| **THE WIRE / incidents** | `MEN NOBODY COULD NAME …` for an Unheard actor on a quiet act; frozen. `PrintEverywhere` writes each book's line through THAT book's `NameFor` |
| **the paper** | public record stays public (`Named`/`Seen` name the house and Hear it); the filler's `{GANG}` draws from Heard houses only and skips gang templates while none is Heard |
| **THE TABLE** | opens only from a Heard card; `WhyNot` prints `NoName` where a verb needs a house we cannot name |
| **probe** | `gangsters_intel_probe --house N`: the book in the card's words; the unattributed log |

## 7. What exists and is reused

| thing | where | used for |
|---|---|---|
| per-house door book, faces set, the scene sweep | `RoadDemo/TurfKnowledge.cs`, `TurfMarks.Discover :128-158`, `OutfitDirector.Learned :860-886` | the SWEEP stays in the scene and calls `House.Knowledge`; the static keeps nothing (one owner). `LearnMan` wired in the same sweep |
| per-house books and their save rows | `Outfit/House.cs`, `HouseDto` (`Outfit/OutfitSnapshot.cs:143-153`, restore `:603`), the nullable precedent `connection`/`events` | `House.Knowledge` saved as `knowledge` on `HouseDto`; `KnowledgeDto` (`Save/CampaignFile.cs:166-173`) read once for migration and then retired |
| the view | `Outfit/HouseView.cs` (`HouseDoor.Protector :44-46`, `HouseThreat.By :135-136`) | `HeardLook` (default true — a mind hears everything, ruling 4); the `Protector` comment updated so nobody "fixes" the hatch later |
| the order book: area/character target, roll, hours, XP rows | `Outfit/Orders.cs` (`Explore` spec `:200`), `OrderResolution.Mix :599-611`, `ActivityXp.Scouting` | `AskAround`, `Tail` appended to `OrderType`, listed in `OrderEffects.Built`; both on the `Scouting` XP row |
| the proposal book, the ransom, WhyNot | `Outfit/HouseDiplomacy.cs`, `Underworld.TakeHim :418-443`, `HouseDiplomacy.WhyNot` | the third key; `NoName` |
| the street event book | `Outfit/StreetEvents.cs` (`EventDef`, pots, `HoldReason`, `EventContext :115-137`) | THE STRANGERS card |
| the paper's attribution | `News/PressRecord.cs :57-64`, `PressText.cs :28, 59, 305-312` | `ThePaper` source; the words for *nobody could name*; the filler's draw |
| the grievance ladder | `Outfit/HouseRelations.cs` `GrievanceKind`, `AmountOf` | `Tailed`, `Questioned` appended and named |
| prices | `Outfit/EconomyPrices.cs`, `Docs/economy-prices.md` | `SnitchFee` by tier |
| the chase layer's remembered place | `[[chase-a-remembered-place]]` | where a tail starts |

## 8. Tickets (INTEL-000..009)

| # | ticket | contents |
|---|---|---|
| INTEL-000 | **`LearnMan` wired** | ten lines in `TurfMarks.Discover`: a rival man within `LearnRange` of one of our crews is a known face. Its own commit. Unblocks the street tags and TAIL |
| INTEL-001 | **The book on them** | `Outfit/HouseKnowledge.cs`, pure, one instance on `House`: levels, `KnowledgeSource` with `Line`, capos named, `Inside` stamp, the unattributed log with the sealed actor, `NameFor`/`InkFor`; `HouseView.HeardLook`; `knowledge` on `HouseDto`, migration from `KnowledgeDto`, Heard derived on load when the row is missing; `gangsters_intel_tests` on `PaperCity`; `gangsters_intel_probe` |
| INTEL-002 | **What names a house** | §3's sources at their hooks — the war at BOTH writes, the door that switches, our own men at a protected door, the front walked past, the paper, the envoy, the defector's door; the quiet acts filed unattributed; the wire line; `WhyNot.NoName`; the Boss exception; the filler from Heard |
| INTEL-003 | **ASK AROUND** | the order, the fee by tier, the roll against the block's fear, the saved-facts key, the cooldown, the gateway refusal in words, the block file key |
| INTEL-004 | **THE STRANGERS** | the card: def, score off `Protector` + `HeardLook` + the unattributed count, the shopkeeper speaker, the three rows, gates; F3's bench state extended so the card can be watched in the mini core |
| INTEL-005 | **The choke point** | `NameFor`/`InkFor` routed through every player-facing caller in §1; the no-Unheard-name test; the `Protector` comment |
| INTEL-006 | **The surfaces** | FAMILIES (Heard only; RUN BY, CAPOS, POWER, the door; the source margin; the rail's count), THE TABLE gate, the turf wash/key/live marks over Placed doors, the minimap, the M map, the notice HUD, the order sheet's new refusal words, street tags with the Boss exception |
| INTEL-007 | **TAIL HIM** | the order on a seen face, Stealth vs Awareness, rank + capo + the door he went home to, `Tailed`, `Burned` faces, the engagement when blown at war |
| INTEL-008 | **PUT THE QUESTION** | the third key on a held ransom, Intimidation vs Loyalty, front + doors + capo + Inside for a lieutenant, `Questioned` |
| INTEL-009 | **The yardstick** | `gangsters_underworld_sim` prints a `book` row for the player: houses Heard by day and by which source; the sweep runs the passive Don (zero, printed, not failed) and the active one (F3 + one `Explore` of a neighbouring block on day 1) and fails a seed where the active Don has Heard nobody by day 3 |

Order: 000 → 001 → 002 → 003 + 004 (the two ways in) → 005 → 006 (the gate, last of the
reading half) → 007 / 008 in either order → 009. The gate never ships before the ways in.

## 9. Not built, and the seams

* **The houses' own books.** A mind reads every rival by id (`Minds.cs:490-503`). `HeardLook`
  (default true) is on the view now; filtering `Rivals` by a mind's own Heard, filled by the same
  sources, is a second epic — it changes what the 30-seed sweep measures (houses that never hear
  each other never war).
* **A defector to us** carries their book (Inside). EPIC 17 records only our men leaving.
* **A tail on the pavement** — a crew that actually follows at thirty metres. v1 is a walk and a roll.
* **A paid rat inside a house** — a standing Inside for a weekly fee; a house that finds him has a
  `SitDownBetrayed`-sized grievance. Gangsters' snitch is a one-off; this would be the retainer.
* **Their knowledge of us** — the AAR's "he probably knows a lot of mine": a `Burned` face is the
  first half; a mind that will not send a burned man is the second.
* **Placed and Seen are street-timed by design** — a door is learnt the frame a crew passes it;
  the paper city cannot reproduce that day. Heard from a job, a proposal or the paper is on the
  campaign clock and is reproducible.

## 10. Questions for the user

Answered 2026-09-05 — see §0 Rulings and §0b.
