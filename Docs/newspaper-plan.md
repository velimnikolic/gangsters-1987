# Plan: novine — javna hronika grada (THE CITY WIRE, jutarnje izdanje u 06:00)

Stanje na dan 2026-09-04, posle odluka korisnika (§9) i contrarian pregleda (§10).
Pokriva šta novine smeju da znaju, odakle to čitaju, kako se slaže izdanje, kako
iskače u 06:00 van ledgera. Građa perioda: `Docs/1987-period-reference.md`.

---

## 0. Ugovor (šta je traženo)

1. **Novine prenose ono što se u igri desilo i što se JAVNO saznalo.** Ne spisak
   igračevih naredbi. Dva vrata u novine:
   * **završilo je u policiji** — hapšenje do stanice, sud, potera, prijava sa izjavom;
   * **videlo je dosta ljudi** — pucnjava na ulici, telo, požar, bomba, razbijen izlog.
2. **Iskaču svaki dan u 06:00, van ledgera, sa X dugmetom na vrhu.**

---

## 1. Šta već postoji (temelj, ne dirati bez razloga)

**Jezgro novina — `Assets/Scripts/News` (`LivingCity.News`), čisto, bez UnityEngine:**
- `Headline` / `HeadlineDesk` (Crime, DrugWar, Nation, World, Business, Culture),
  `NewsDate` (kampanja kreće 5. januara 1987; `Masthead()`), `NewsCalendar` (16
  pravih datuma 1987 koji vode stranu), `HeadlineGenerator.FrontPage(seed, date)`
  (6 naslova, deterministično po `(seed, dan)`, `TextBudget = 56`), `PictureDesk`
  (foto po desku, model iz kataloga), `Newsprint` (halftone).
- `PortraitStudio.Treatment.Newsprint` — svaka slika u novinama je pravi model iz grada.

**Strana u ledgeru — `Assets/Scripts/UI/PersonnelAlmanac.Newspaper.cs`:**
masthead THE CITY WIRE, dateline, lead u dve kolone sa fotografijom, red briefova, kolona
**ON OUR STREETS** (čita `outfit.LastNight` — rečenice žice u PRVOM LICU), oglas,
vreme, tasteri SAVE / LOAD i ADS (poleđina: `PersonnelAlmanac.Classified.cs`). Otvara se
samo ručno (P), i to je današnji jedini prozor u novine.

**Knjige koje već pišu šta se desilo:**
- `CampaignRunner.Incidents` → `LastNight` → `IncidentBook` (`Incident` +
  `IncidentText.Line`, prvo lice: *"our men"*, *"our names are on it"*). Pune ih
  `OrderResolution`, `Bodyguards`, `PersonnelDirector`, `LawWire`.
- `RoadDemo.LawWire` — linije zakona. Devet verba prijave/hapšenja filtrira
  `Faction != Ours`; `Verdict`, `BailPosted`, `BailForfeit`, `CutLoose`, `WitnessKilled`,
  `WitnessWithdrawn` NEMAJU filter — ali sud iznad njih radi samo nad igračevim rosterom
  (v. §10, nalaz M1).
- `TerritoryRacket.Dispatches` (`TerritoryDoorDispatch`: Approached, Agreed, Refused,
  Threatened, Wrecked, Beaten, Missed, RoundBanked…) — vrata, privatna stvar reketa.
- `WireBook.Collect` — obrazac čistog kolektora koji oblači tri knjige u jedan niz.

**Kanali događaja na ulici (`Assets/RoadDemo`, `Assets/Scripts/Gameplay`):**
- `StreetAlarm` — jedini kanal za hitac i smrt: `OnShot(Shot {Pos, Time, Faction,
  Shooter})`, `OnDeath(pos, DeathOf {Gangster, Civilian, Officer}, victimFaction)`,
  `IncidentNumber`, `IncidentShots`, toll, `IncidentGap = 45 s`, `PoliceFaction = -1`.
- `TerritoryRuntime.OnStreetDeath` — piše `TerritoryFearEvent` sa
  **`TerritoryFearVisibility { Hidden, Seen, Public }`**; telo na ulici je `Public` po
  definiciji („a body in the street is a public fact whoever saw the shot").
  `AttributeRecentViolence` je PRIVATAN i pamti hice **6 s** (`AttributionSeconds`) —
  neupotrebljiv posle 45 s tišine (§10, C2).
- **Svedoci na živoj ulici**: `CivilianAgent.SnapshotWitnesses(at, SightRadius = 25 m,
  MaxEyewitnesses = 3, into)` — već ga zove `PoliceDispatch.SnapshotTheScene` jednom po
  incidentu za docket. `WitnessSystem` / `CrimeFeed` / `CorpseEvidence` su **parkirani
  sloj** koji u `Assets/RoadDemo` ne vidi nijednog pešaka (nula `InteractableNpc`; §10, C1).
- `PoliceDispatch` — `OnDeath` → heat, `OfficerDown` swarm („EVERY CAR IN THE CITY");
  `PoliceDispatch.Custody` → `LawWire.TakenIn / Sprung`; `PoliceForce` →
  `LawWire.Verdict`, `BailForfeit`. `PoliceForce.Roster()` = `PersonnelDirector.Instance
  .Roster` — **samo igračev**; rival uhapšen jeste (multi-house put u `Arrest`/`Custody`),
  ali njegova presuda se razreši u `null` i nikad ne izađe.
- `BusinessShutdowns.Changed` — `BusinessShutdownChangeKind { Started, Extended, Repaired,
  Expired, Restored }`; `SmashUp` 3 dana, `Arson` / `Bomb` 7 dana. `Restored` puca i pri
  load-u — filtrirati (§10, M6). `ShopDamage`: 22 s vatre pa daske, staklo po trotoaru.
- `OutfitDirector.OnJobResolved` — SmashUp / Torch / Bomb / Raid / Kill za **svaku**
  kuću; `Kill` po papiru → `StrikeHimOff` → `TerritoryRuntime.RecordKilling` (Public);
  `Kidnap` → `TakeHim` (samo reč između kuća).
- `CampaignRunner.BossFell` / `OutfitEnd` — kraj kampanje, već crta crnu stranu; sat
  posle toga i dalje ide.

**Sat:** `CityClock` (`Hour`, `Day`, `Paused`, `SpeedIndex`; `DayClock.Current`),
`OutfitDirector.Update` — **ponoć** = `Underworld.DayTick` + `LastNight` + autosave.
`startHour = 6`; 600 realnih sekundi po satu igre (na 4× dan traje oko sat).

**Load:** `CampaignSave.LoadFromFile` ponovo učitava scenu, fajl čeka u
`CampaignSave.Pending` i primenjuje se na prvom Business tick-u (kadenca 4 sata igre);
do tada je `Campaign.Day = 1`, sat na 06:00. `UnderworldHost.SeedForScene` već čuva
taj isti `Pending` (§10, C3).

**Modalni obrasci:** `PersonnelAlmanac` (`IsOpen`, `ClaimsEsc`, `AcquireLedgerPause`,
`SuspendOtherCanvases` — snimak pri otvaranju; iznutra se sat sme odpauzirati kroz
`PickTimeControl`; sortingOrder 110), `OutfitEnd` (400), `StreetHud` (115),
`DemoClockHud` (**120**, tasteri brzine i pauze), `DoorMenu` (100), turf mapa (60).
Osamnaest fajlova / 31 mesto čita `PersonnelAlmanac.IsOpen` / `ClaimsEsc` /
`StrategicMapHud.IsOpen` / `OutfitEnd.IsUp` — i ne znače isto (§10, M4).

**Snimanje:** `CampaignFile` v2 nosi docket (`PrisonSnapshot`, migracija
`MigrateFromBeforeTheDocket`, literalni v1 JSON fixture u `SaveTests`), ali **nijednu
knjigu incidenata** — posle load-a žica je prazna, i jučerašnje novine ne postoje.

**Scene:** `CoreDemo.unity`, `MiniCoreDemo.unity`. `OutfitEnd` i `StreetHud` instalira
`DemoCrews`. `LedgerMenuScene` je komponenta bez scene (nema `.unity` koji je referiše).

---

## 2. Dijagnoza (šta nedostaje)

1. **Nema javne knjige.** Sve što postoji je ili knjiga NAŠIH ljudi u prvom licu (žica),
   ili knjiga vrata (reket), ili strah po bloku (brojka, ne priča). Nema zapisa „ovo se
   desilo, ovoliko ljudi je videlo, policija je zapisala ova imena".
2. **ON OUR STREETS curi.** Kolona štampa `LastNight` doslovno: „X wants his envelope
   brought up to the rate", „Y bears watching" — privatne stvari iz kancelarije, u
   novinama, u prvom licu. Upravo ono što ugovor ne dozvoljava.
3. **Generator ne zna za grad.** Puni stranu izmišljenim kvartovima (RIVERSIDE, HARBOR
   ROW) iako grad ima `StreetNames.Quarter` i imena blokova.
4. **Rivali su nevidljivi novinama.** Žica odbacuje tuđa hapšenja, a sud tuđe ljude ni
   ne nađe.
5. **Nema 06:00 događaja** — samo ponoć. Novine se ne otvaraju same.
6. **Knjige ne preživljavaju snimanje** — a novine koje „pamte juče" moraju.

---

## 3. Principi

1. **Novine znaju samo ono što bi novinar 1987. znao:** policijski blotter, sudski
   spisi, i ono što su prolaznici videli. Nikad ne čitaju naredbe, knjigu reketa, safe
   ni roster. Isti prozor kroz koji gleda `HouseMind` (`Docs/rival-families.md`).
2. **Jedan zapis, više čitalaca.** `PressRecord` je čista struktura podataka; popup,
   ledger arhiva, bench i testovi čitaju ISTU knjigu. Ništa ne parsira rečenicu nazad u
   činjenice (pouka `IncidentBook`-a).
3. **Ne izmišljati** ([[no-invented-map-data]], [[leave-it-empty-dont-guess]]): broj
   mrtvih je `StreetAlarm`-ov toll; broj svedoka je izmeren; ime porodice se štampa samo
   kad ga je policija zapisala, sud izrekao, ili ga je dovoljno ljudi videlo. Polje kog
   nema → rečenice nema.
4. **Presuda mora da MERI ono što broji** ([[park-generator-lessons]]): svaki merač
   (svedoci, atribucija, okidač) se dokazuje u Play-u linijom `[Press] FILED …` pre nego
   što headless test dobije pravo da kaže „zeleno".
5. **Treće lice, tabloid 1987, verzal u naslovu, `TextBudget = 56`.** Rečenice žice
   (prvo lice) ostaju žica — novine dobijaju svoj `PressText`.
6. **Deterministično po (seed, dan, knjiga)** — reload čita isto izdanje; popup i arhiva
   čitaju isti prozor.
7. **Centralni sistem, ne demo** (CLAUDE.md): knjiga i slagač u `LivingCity`, jedan
   pisac na ivici scene, popup instaliran u svakoj sceni sa kampanjom.

---

## 4. Model: šta je javno

Dva vrata iz ugovora, pretočena u `Attribution` (koga novine smeju da imenuju):

| `Attribution` | kad | kako se štampa |
|---|---|---|
| **Named** | policija uknjižila ljude (TakenIn), sud presudio, slučaj sa imenima na docketu, telo identifikovano iz rostera (§9.6) | ime čoveka i porodice |
| **Seen** | tačno JEDNA kuća pucala u incidentu (`Shot.Faction`, policija isključena) I izmeren broj svedoka ≥ prag (§9.5: 3) | „MEN BELIEVED TO BE {FAMILY}'S" |
| **Unknown** | sve ostalo — dve kuće, nula svedoka, niko uknjižen | „GUNMEN", „A CREW", „UNKNOWN MEN" |

**„Dosta ljudi" se MERI**, ne pogađa — istim meračem kojim policija puni docket:
`CivilianAgent.SnapshotWitnesses(at, SightRadius, max, into)`. `PressDesk` ga zove sa
SVOJIM `max` (npr. 12): dispečerov `MaxEyewitnesses = 3` je jednak pragu, pa bi „≥ 3"
značilo „== kap" i „seen by 5" ne bi mogao da se odštampa. Broji samo civile koji su
već u Gawk / Startle / Flee / Cower, pa se meri na PRVI hitac tek pošto je
`CivilianAgent.HearAll` prošao (redosled pretplate), i na svaku smrt; zapis nosi maksimum.
Osa `TerritoryFearVisibility` ostaje drugi ulaz: `Public` smrt je javna i sa nula
prebrojanih (leš ostaje na trotoaru).

**Ko je pucao** se čita iz toka koji `PressDesk` ionako prima: `Shot.Faction` po
`IncidentNumber`, bez `PoliceFaction`. Jedna frakcija → kandidat za Seen; dve → Unknown
(isto pravilo kao u `TerritoryRuntime`: „a guess would be worse than a blank").

Tabela događaja — izvor u kodu, javnost, rubrika:

| događaj | izvor | javno? | ko se imenuje | rubrika |
|---|---|---|---|---|
| pucnjava na ulici (jedan `IncidentNumber`, zatvoren posle 45 s tišine) | `StreetAlarm.OnShot` + `IncidentShots` | DA ako je bilo smrti ILI svedoci ≥ prag; inače NE | porodica po Seen/Named | CRIME |
| smrt gangstera / civila / policajca | `StreetAlarm.OnDeath` (uvek `Public`) | **DA, uvek** | civil → „A PASSER-BY"; gangster → ime + porodica (§9.6); počinilac po Seen/Named | CRIME; policajac = **lead** |
| policajac poginuo → potera | `PoliceDispatch.OfficerDown` swarm | DA | — | lead |
| hapšenje stiglo do stanice | `LawWire.TakenIn` (kroz `PoliceDispatch.Custody`) | **DA** | imena uhapšenih + porodica + optužba (`Deed`) | CRIME / blotter |
| zatvorenici oslobođeni na putu | `LawWire.Sprung` | DA (pucano na kola zakona) | porodica Named (bili su u kolima) | CRIME |
| pucano na policajca / bekstvo od policajca | `FiredOnTheOfficer` / `RanFromTheOfficer` | DA / blotter | Named ako ih je policajac prepoznao | CRIME |
| slučaj otvoren | `LawWire.CaseOpened` (`CourtCase.Deed`) — puca i iz `Arrest` i iz `Complaint` puta | DA za Murder / CopKilling / AssaultOnOfficer; blotter za Extortion / WitnessTampering; **dedup po `CaseId` sa hapšenjem** — jedan kolar, jedna priča | imena sa docketa | COURTS |
| presuda: osuđen / oslobođen / odbačen | `LawWire.Verdict` (`CaseVerdict.Days`) | **DA** (sud je javan) — za rivale kroz PRESS-006 (§9.12) | ime, porodica, kazna iz `Sentencing` | COURTS |
| kaucija plaćena / preskočena | `BailPosted` / `BailForfeit` | NE / DA (poternica) | ime | COURTS |
| svedok ubijen | `WitnessKilled` (to je već smrt) | DA — „WITNESS IN {WHERE} CASE FOUND DEAD" | žrtva po imenu | CRIME |
| svedok povukao izjavu | `WitnessWithdrawn` | NE (samo kroz kasniji „CASE COLLAPSES") | — | — |
| pritužba (telefon) | `ComplaintRung` | **NE** — poziv nije javan | — | — |
| izjava uzeta na vratima | `StatementTaken` | blotter, jedan red (§9.9) | radnja po imenu | blotter |
| požar / bomba na radnji | `BusinessShutdowns.Changed` **samo `Started` / `Extended`** (Arson / Bomb) | **DA, uvek** (vatrogasci, daske nedelju dana) | radnja po imenu (`CityBusinesses.Name`); porodica samo Named | CRIME / CITY |
| razbijen izlog | `BusinessShutdowns.Changed` `Started` (SmashUp) | DA, blotter | radnja po imenu | blotter |
| prebijen vlasnik (Raid / Assault) | `ResolveEscalation(Assault)` | DA ako svedoci ≥ prag; NE ako Hidden | radnja | blotter |
| ubistvo po papiru (`Kill` nalog) | `StrikeHimOff` → `RecordKilling` (Public) | DA — „FOUND SHOT" | žrtva Named; naručilac NE | CRIME |
| racija stana | `FlatRaided` | DA, blotter | ime uhapšenog (policija ga je zapisala) | blotter |
| Don pao | `CampaignRunner.BossFell` | DA, lead (i tako je `OutfitEnd`) | Named | lead |
| telo nađeno kasnije | `CorpseEvidence` | **IZBAČENO** — na RoadDemo telima ga nema (§10, m3); `Kill` po papiru je već Public | — | — |
| pretnja / reket / dogovor / odbijanje | `Dispatches` Threatened, Agreed, Refused | **NE** | — | — |
| kolekcija, runde, novac, plate | `Dispatches` Round*, `PayrollShort` | **NE** | — | — |
| otmica | `TakeHim` | NE (§9.9) | — | — |
| kupovina radnje | `BusinessDeeds.SetGang` | BUSINESS brief „premises change hands" (§9.9) | radnja | BUSINESS |
| povišica, skimming, unapređenje, prelazak, „bears watching", predaja torbe | `Incident` privatni kindovi | **NIKAD** | — | — |
| rivalske kuće | sve iznad važi ISTO | DA (§9.7) | isto | isto |

Novine ne znaju čije su naredbe: ista pucnjava rivala i naša štampa se istim pravilima.
Požar ulazi kroz `BusinessShutdowns.Changed`, koji ne nosi kuću — novine po konstrukciji
ne mogu da vide ko je naručio.

---

## 5. Arhitektura

### 5.1 `PressRecord` — javna knjiga (`LivingCity.News`, čist C#)

Jedan zapis po događaju, polja iz kojih se sve slaže (nikad obrnuto):

- `Day` (kampanjski, 1-based), `Hour` — **vreme OTVARANJA** incidenta (prvi hitac), ne
  zatvaranja: priča koja se zatvori u 06:03 pripada noći koja je počela, i popup i
  arhiva je vide u istom prozoru.
- `Kind` — `PressKind { Shootout, Killing, OfficerKilled, Arson, Bombing, SmashUp,
  Assault, Arrest, CustodyBroken, FiredOnPolice, ChargesFiled, Verdict, BailJumped,
  WitnessDead, FlatRaid, BossKilled }` (dodaje se na kraj, kao svaki enum u projektu)
- `Where` — ime kvarta (`StreetNames.Quarter`, sa članom: „The Flats"); `Business` —
  ime radnje; razrešeni pri pisanju, kao `WireBook.Of`. **U naslovu je uvek kvart**,
  radnja ide u pasus (budžet, §10 m2).
- `Factions` — frakcije koje su pucale (bez policije); `Family` je izvedeno: jedna → ta,
  inače −1. `Attribution { Named, Seen, Unknown }`, `Witnesses` (izmereno, max)
- `Toll` — mrtvih gangstera / civila / policajaca; `Shots`
- `Names` — ljudi koje je policija ili sud imenovao; `Deed`; `SentenceDays`; `CaseId`
  (za dedup)
- `Weight` — težina vesti (policajac > ubistvo > požar/bomba > hapšenje > pucnjava bez
  žrtava > presuda > izlog > blotter); određuje lead i redosled

**Knjiga je GRADSKA, ne kućna**: `Underworld.Press` (`PressBook` + `PressKept` +
`LastEditionDay`), kao `Underworld.Current.Relations` — ne na `CampaignRunner`, koji je
jedan po kući (§10, M2). **Snima se** — `CampaignFile` v3, top-level `press: PressDto[]`
+ `lastEditionDay`; migracija `version <= 2` → prazna knjiga, `lastEditionDay = file.day`
(pravilo docketa: migrirati, ne izmišljati; literalni v2 JSON fixture kao `SAVE-007`).

### 5.2 `PressDesk` — JEDINI pisac (`Assets/RoadDemo`, ivica scene)

- **Scenski objekat**, ne statika: komponenta na dispečerovom hostu, pretplata u
  `OnEnable`, odjava u `OnDestroy` (kao `TerritoryRuntime`), `ResetForPlay` uz to.
  `LoadFromFile` je reload scene — statička pretplata bi nosila polupriču iz starog
  sveta (§10, M3).
- `StreetAlarm.OnShot` / `OnDeath` → agregat po `IncidentNumber`: prvi hitac otvara
  zapis (Day/Hour), svaki hitac dodaje `Shots` i `Factions`, svaka smrt `Toll` i novo
  merenje svedoka. Fajluje se kad se incident zatvori (`QuietFor > IncidentGap`).
- **Flush otvorenog agregata**: na granici izdanja (06:00) i u `CampaignSave.Compose` —
  priča „dosad" je istinita priča; incident koji preživi granicu nastavlja kao NOVI zapis
  za sledeće izdanje.
- `LawWire` verbi zovu i `PressDesk` — bez filtera kuće (žica zadržava svoj). Rivalske
  presude stižu kad PRESS-006 popravi sud (§9.12).
- `BusinessShutdowns.Changed` filtrirano na `Started` / `Extended` (nikad `Restored`,
  `Expired`, `Repaired`), `ResolveEscalation(Assault)` → zapis sa imenom radnje.
- `StrikeHimOff` / `RecordKilling`, `BossFell`.
- Scena bez kampanje (crew demo, bench) ne fajluje ništa i ćuti — isto kao `LawWire`.
- Svaki zapis loguje `[Press] FILED day N 14:32 Shootout at The Flats, 2 dead, seen by 5,
  factions 1` pa se u Play meri kroz `unity command console --json`.
- Prag svedoka i `max` merenja su polja komponente (konfigurabilni), ne konstante.

`HouseMind` **ne čita** `Underworld.Press` (ista zabrana kao za tuđ roster) — rival koji
„čita novine" je posebna odluka (§9.8).

### 5.3 `Edition` — slagač (`LivingCity.News`, čist)

`Edition.Compose(seed, NewsDate, day, IReadOnlyList<PressRecord> book, window)` →
`Headline[]`:

1. **Prozor**: zapisi čije je vreme otvaranja od prethodnog izdanja do ovog 06:00.
2. **Lead**: zapis najveće težine iz prozora ako prelazi `LeadWeight`; inače današnji
   `NewsCalendar` datum, inače generator. Pravi 1987 datum i pravi lead istog jutra:
   istorijski ide u prvi brief („FROM THE WIRE"), grad vodi stranu.
3. **City desk**: preostali javni zapisi, po težini, dok ima kolona; blotter zapisi u
   JEDNU kolonu **POLICE BLOTTER** (red po zapisu, kao stari lokalni listovi).
4. **Filler**: generator puni ostale deskove postojećom logikom (`FrontPage` se deli na
   `Lead` + `Fill(page, rng, count)`); `{DISTRICT}` dobija kvartove grada **kroz
   parametar** (`StreetNames` zavisi od UnityEngine) i šabloni se ponovo prooftuju sa
   članom („IN THE FLATS" da, „THE HEIGHTS WAREHOUSE" se prepisuje ako ne čita).
5. **Foto desk**: Named čovek → njegov portret (`LedgerModelSet` / `GangLooks` model po
   `Character`), policijska priča → `PictureDesk.PatrolCarModel`, radnja → bez slike
   (nema `Storefront` framinga; ne izmišljati ga u ovom epiku).

`Headline` dobija `Story` (referenca na `PressRecord`) i `Blurb`; `HeadlineDesk` dobija
`City` i `Courts`.

### 5.4 `PressText` — rečenice (čist, jedan fajl kao `IncidentText`)

Naslov ≤ 56, verzal; pasus 1–2 rečenice, treće lice. Slotovi samo iz polja zapisa.
**Budžet se dokazuje protiv pravih tabela imena** (najduže prezime × najduži šablon
radnje iz `BusinessArchetypes`, najduže ime porodice, najduži kvart), ne statičkim
skenom šablona: „FIRE GUTS {BUSINESS}; ARSON SUSPECTED" sa 29-znakovnim imenom radnje
je tačno 56, a `{WHERE}` = radnja u naslovu hapšenja je 62 — zato radnja ide u pasus.

| Kind / Attribution | naslov | pasus |
|---|---|---|
| Shootout, Unknown, 0 mrtvih | `GUNFIRE IN {WHERE}; {SHOTS} SHOTS, NOBODY HIT` | „Residents counted N shots shortly after H. Police found casings and no one to charge." |
| Killing, Seen | `{FAMILY} MEN SEEN FLEEING {WHERE} SHOOTING` | „Witnesses described men known on the street as X's. One man was dead on the pavement; police have made no arrest." |
| Killing, Unknown, civil | `PASSER-BY KILLED IN {WHERE} CROSSFIRE` | — |
| OfficerKilled | `OFFICER SLAIN IN {WHERE}; MANHUNT ON` | „Every car in the city was called…" |
| Arrest, Named | `{N} {FAMILY} MEN HELD AFTER {WHERE} ARREST` | imena, radnja, optužba iz `Deed` |
| CustodyBroken | `PRISONERS FREED IN ATTACK ON POLICE CAR` | — |
| ChargesFiled | `{FAMILY} MEN CHARGED IN {WHERE} {DEED}` | imena sa docketa |
| Verdict Convicted | `{NAME} GETS {SENTENCE}` | godine/meseci iz `Sentencing`, ne izmišljene |
| Verdict Acquitted / Dismissed | `{NAME} ACQUITTED` / `CASE AGAINST {NAME} COLLAPSES` | „…after the state's witness failed to appear." |
| BailJumped | `{NAME} SKIPS BAIL; WARRANT ISSUED` | — |
| Arson / Bombing | `FIRE GUTS {WHERE} SHOP; ARSON SUSPECTED` / `BLAST WRECKS {WHERE} STOREFRONT` | „{BUSINESS} will be shut a week…" (iz `BusinessShutdownConfig`) |
| SmashUp (blotter) | — | „Windows at {BUSINESS} were smashed overnight." |
| WitnessDead | `WITNESS IN {WHERE} CASE FOUND DEAD` | — |
| BossKilled | `{FAMILY} BOSS SHOT DEAD` | — |

Pravilo: ni jedan šablon ne sme da pomene naredbu, novac, reket, ni „our".
Test skenira `PressText.cs` za tokene `our `, `ours`, `we ` (kao što
`gangsters_house_tests` skenira `HouseMind.cs`).

### 5.5 `NewspaperHud` — popup u 06:00 (`LivingCity.UI`, runtime self-install)

- Instalira se tamo gde `DemoCrews` instalira `OutfitEnd` / `StreetHud` — CoreDemo i
  MiniCoreDemo. `LedgerMenuScene` nema scenu; kad je bude, dobija filler-only list.
- **Okidač je LATCH, ne trenutak**: kad sat pređe 06:00 (prethodni < 6 ≤ sadašnji, ili
  `Day` skočio) i `LastEditionDay < Campaign.Day` → `due = true`. Otvara se tek kad
  su SVI uslovi tačni: `CampaignSave.Pending == null` (fajl je primenjen — inače list
  od 5. januara iskoči preko učitavanja dana 30 i pauza mu blokira Business tick),
  `!PersonnelAlmanac.IsOpen`, `!OutfitEnd.IsUp`, `!Runner.Fallen`. Ledger otvoren u
  06:00 (sat odpauziran iznutra) → list iskače kad se ledger zatvori.
- Dan 1 (§9.2) = „`LastEditionDay < Campaign.Day` pošto je eventualni `Pending` primenjen",
  ne „prvi frejm". Load u 09:00 ne iskače (dan je izdat u fajlu); load u 05:50 iskače u
  06:00. **Dokazuje se u Play pravim load-om**, ne headless testom.
- **Canvas** ScreenSpaceOverlay, sortingOrder **130** — iznad `DemoClockHud` (120,
  inače se grad odpauzira kroz „pauzirani" list), `StreetHud` (115), ledgera (110),
  `DoorMenu` (100), turf mape (60); ispod kraja (400). Tamni zastor preko celog ekrana je
  raycast target (modalni štit). List centriran, referenca 1920×1080, isti `LedgerKit` /
  `LedgerStyle` / `Newsprint` inkovi kao strana u ledgeru.
- **X na vrhu**: taster na desnom uhu masthead-a („✕" u `LedgerV2.Key.Outline`),
  **Esc** zatvara isto. `NewspaperHud.IsOpen` / `ClaimsEsc` statika kao u
  `PersonnelAlmanac`.
- **Čitaoci modala — ciljano, bez refaktora**: `IsOpen`/`ClaimsEsc` se dodaje samo tamo
  gde ulaz sveta mora da ustukne — `DemoCamera`, `StreetHud.ModalUp`, `TurfMapHud`,
  `CrewOverlay`, `PoliceDispatch.Arrest.Blocked`, `StrategicMapHud`. `UiModal.AnyOpen`
  NE ide u ovaj epik: 31 čitalac ne znači isto (`Arrest.Blocked` namerno isključuje
  turf mapu; `ClaimsEsc` nosi pravilo jednog frejma; niko ne uključuje `OutfitEnd`) —
  ujedinjenje je poseban commit bez promene ponašanja, dokazan harness-om (§10, M4).
- **Pauza**: dok su novine gore, sat stoji (`AcquireLedgerPause` obrazac, vraća
  prethodno stanje na X) — §9.3.
- **P dok su novine gore**: zatvara novine pa otvara ledger.
- **Crtanje**: slagač lista se **izvlači** iz `PersonnelAlmanac.Newspaper.cs` u
  `NewspaperSheet.Paint(RectTransform, Edition, …)` i zove iz oba mesta — ledger tab i
  popup crtaju identičan list (pravilo centralnih sistema; nikakav fork).
- **Zvuk** (§9.10): jedan udarac novina o sto pri otvaranju, Sonniss kroz
  `Tools/audio/import_sounds.py`.

### 5.6 THE PAPER tab u ledgeru

- Prikazuje **isto današnje izdanje** kroz `NewspaperSheet`, plus **BACK ISSUES** —
  prethodni dani iz `Underworld.Press` po danu (list po list, ← →), koliko knjiga pamti.
- **ON OUR STREETS se briše** iz novina: to je žica, i već stoji na `StreetHud`-u i u
  rail-u ledgera. Zamenjuje je **POLICE BLOTTER** (§5.3).
- SAVE / LOAD / ADS ostaju gde jesu (§9.4).

### 5.7 Šta se NE radi

- Nema drugog sata: 06:00 se čita sa `DayClock`, ponoć ostaje `OutfitDirector`-u.
- Nema parsiranja rečenica; nema „heat" ili reputacije iz novina u ovom epiku (§9.8).
- Nema novog framinga fotografija; nema `BodyFound`; nema `UiModal` refaktora.
- `LawWire` i `IncidentText` se ne prepravljaju — žica ostaje žica.
- `AttributeRecentViolence` se ne otvara — `PressDesk` čita `Shot.Faction` sam.

---

## 6. Faze (svaka ostavlja igru ispravnu)

Redosled je promenjen posle contrariana (§10, M7): **prvo se meri, pa se zamrzava šema**.

| faza | šta | dokaz |
|---|---|---|
| **F0 — pisac, u memoriji** | `PressRecord` / `PressKind` / `Attribution` kao obična klasa BEZ save-a; `PressDesk` na ulici i zakonu (sve kuće), agregat po incidentu, svedoci kroz `SnapshotWitnesses`, frakcije iz `Shot.Faction`, filter `Started/Extended`, dedup `CaseId`, flush na 06:00 | **jedna Play sesija korisnika** na MiniCoreDemo: pucnjava / hapšenje / paljenje → `[Press] FILED … seen by N, factions K` u konzoli; N > 0 na prometnoj ulici, K = 1 kad puca jedna kuća; nijedan zapis iz privatnog kinda |
| **F1 — ram i snimanje** | polja zamrznuta prema onome što je F0 izmerio; `Underworld.Press`, `PressKept`, `LastEditionDay`; `CampaignFile` v3 + migracija + literalni v2 fixture; flush u `CampaignSave.Compose`; `gangsters_news_tests` | suite zelen; `gangsters_save_tests` round-trip knjige; v2 fajl migrira na praznu knjigu |
| **F2 — slagač i tekst** | `Edition.Compose`, `PressText`, `HeadlineDesk.City/Courts`, kvartovi kroz parametar, foto Named čoveka; ledger tab čita izdanje, blotter kolona umesto ON OUR STREETS, back issues | `gangsters_press --seed N --stage shootout|arrest|arson|quiet` štampa izdanje i sudi: budžeti protiv pravih imena, treće lice, determinizam, „nema zapisa → nema priče", porodica samo Named/Seen |
| **F3 — popup** | `NewspaperHud` sa latch okidačem iza `Pending`, X + Esc, pauza, sorting 130, ciljani čitaoci modala, self-install kroz `DemoCrews`, zvuk | korisnik u Play: dan prelazi 06:00 → list iskače, X zatvara, sat nastavlja; **load dana 30 u 09:00 ne iskače i ne blokira učitavanje**; ledger otvoren u 06:00 → list posle zatvaranja |
| **F4 — kasnije, posebna odluka** | posledice novina (heat, reputacija, notability, rival čita novine), `Storefront` foto, `UiModal.AnyOpen` | — |

F1–F2 su čist C# i rade kroz csproj + goli .NET host kad je editor zauzet
([[offline-roster-suite-check]]). F0 i F3 traže oči u Play-u — testira korisnik
([[user-tests-in-editor]]).

---

## 7. Provera

    unity command console --json --since <cursor>        # F0: [Press] FILED … u Play
    unity command gangsters_news_tests --json            # F1/F2 ugovori
    unity command gangsters_press --json --seed 7 --stage arrest
    unity command gangsters_save_tests --json            # knjiga preživljava fajl

Ugovori suite-a (imena u stilu `PoliceTests`):
`APrivateIncidentNeverReachesThePress`, `AComplaintIsNotNews`, `ABodyIsAlwaysNews`,
`AFamilyIsNamedOnlyByThePoliceTheCourtOrEnoughEyes`, `TwoHousesShootingIsNobody`,
`SeenNeedsWitnessesOverTheThreshold`, `OneShootoutIsOneStory`, `OneCollarIsOneStory`,
`AStoryBelongsToTheNightItOpened`, `AShopRestoredFromAFileIsNotNews`,
`TheLeadIsTheHeaviestStory`, `NoRecordNoStory`, `EveryTemplateFitsTheRealNames`,
`ThePressSpeaksInTheThirdPerson`, `TheSameSeedPrintsTheSamePaper`,
`AHistoricalDateStillPrintsOnARealLeadMorning`, `PublicEdgeCasesUseTheirDesks`,
`ThePaperSurvivesASave`, `AVersionTwoFileMigrates`, `TheEditionWindowIsSixToSix`.

Ono što headless NE može da dokaže i zato ide korisniku u Play: da svedoci nisu nula,
da je frakcija jedna, i da load u 09:00 ne iskače i ne blokira.

---

## 8. Šta se u postojećim sistemima menja (van novina)

- `LawWire` — svaki verb dodatno zove `PressDesk` (bez filtera kuće). Rečenice žice
  ostaju.
- `PersonnelAlmanac.Newspaper.cs` — crtanje lista izlazi u `NewspaperSheet`; kolona
  ON OUR STREETS → POLICE BLOTTER; tab dobija back issues.
- `HeadlineGenerator` — kvartovi grada kroz parametar (ostaje čist); `FrontPage` se
  deli na lead + fill; šabloni prooftovani sa članom.
- `Underworld` — `Press`; `CampaignFile` v3 (top-level `press`, `lastEditionDay`);
  `CampaignSave.Compose` flush-uje otvoreni agregat.
- Šest čitalaca ulaza dobija `NewspaperHud.IsOpen` / `ClaimsEsc` (§5.5).
- `PoliceTests` / `SaveTests` — po jedan novi ugovor (knjiga u fajlu, migracija).
- `PoliceForce.Roster()` → roster kuće po `gangId` (obrazac iz
  `PoliceDispatch.Arrest.cs`), da rival dobije presudu (§9.12, PRESS-006).

---

## 9. Odluke (korisnik, 2026-09-04)

Svih dvanaest pitanja je odgovoreno istog dana; ovo je ugovor, ne predlog.

| # | pitanje | odluka |
|---|---|---|
| 9.1 | prozor izdanja | **06:00 → 06:00** — noć ulazi u jutarnje novine |
| 9.2 | dan 1 | **iskače odmah na startu** kampanje (filler + kalendar, ništa naše) — posle contrariana: „odmah" = čim je eventualni `Pending` fajl primenjen |
| 9.3 | pauza | **grad stoji** dok su novine gore, kao ledger |
| 9.4 | THE PAPER tab | **ostaje** u ledgeru kao arhiva, sa SAVE / LOAD / ADS |
| 9.5 | prag svedoka / imenovanje po viđenju | moja odluka: **3 pešaka** u `SightRadius` (25 m), mereno sa većim `max`; porodica se imenuje i po viđenju („MEN BELIEVED TO BE X'S") kad je frakcija jedna |
| 9.6 | mrtav gangster | **po imenu** — „identified as X, of the Y family" |
| 9.7 | rivali | **da** — novine su gradske |
| 9.8 | posledice novina | **ne u ovom epiku** (heat, reputacija, notability, rival čita novine — posebno) |
| 9.9 | granični slučajevi | **kao u tabeli §4**: izjava na vratima = blotter red; otmica NE; kupovina radnje = BUSINESS brief; prebijanje vlasnika = blotter ako svedoci ≥ prag |
| 9.10 | zvuk | **da** — jedan udarac novina o sto pri otvaranju, Sonniss; ide u F3 |
| 9.11 | Linear epik | **da** — GAN-324 (EPIC 35), tiketi PRESS-001..007 po fazama |
| 9.12 | rivalske presude (novo, iz contrariana M1) | **popravlja se i sud** — sud danas radi samo nad igračevim rosterom, pa rival bude uhapšen (priča izađe) a presuda nikad; PRESS-006 (GAN-330): `PoliceForce` razrešava čoveka kroz roster njegove kuće, pa novine štampaju i rivalske presude |

---

## 10. Contrarian pregled (2026-09-04) — šta je nađeno, šta je promenjeno

Presuda: **needs rework, ciljano** — arhitektura (knjiga, jedan pisac, čist slagač,
zajednički crtač) stoji; pogrešan je bio KOD na koji plan pokazuje. Najjeftiniji test
koji je oborio prvu verziju: `rg "InteractableNpc|PedestrianSpawner.PedestrianLayer"
Assets/RoadDemo` → nula pogodaka.

| # | nalaz | šta je promenjeno |
|---|---|---|
| C1 | `WitnessSystem`-ov `OverlapSphere` broji samo `InteractableNpc`; RoadDemo civili su `CivilianAgent` bez njega — svedoci bi uvek bili 0, „Seen" nikad, drugo vrata ugovora zatvoreno, a headless test zelen | merač je `CivilianAgent.SnapshotWitnesses` (već ga zove `PoliceDispatch.SnapshotTheScene`), sa sopstvenim `max` jer je `MaxEyewitnesses = 3 = prag`; princip 3.4 |
| C2 | `AttributeRecentViolence` je privatan i pamti 6 s; posle 45 s tišine uvek prazno → svaka priča Unknown | frakcije iz `Shot.Faction` po `IncidentNumber`, bez policije; jedna → Seen kandidat, dve → Unknown |
| C3 | load dana 30 u 09:00: fajl čeka u `Pending` do prvog Business tick-a, `Campaign.Day = 1`, sat 06:00 → list od 5. januara iskoči i pauza blokira učitavanje | okidač je latch iza `CampaignSave.Pending == null`; dan 1 definisan posle primene fajla; dokaz u Play pravim load-om |
| M1 | `Verdict`/`Bail*` nemaju `Ours` filter, ali `PoliceForce.Roster()` je igračev; rival uhapšen jeste, presuda se razreši u `null` | §9.12: korisnik rekao „popravi i sud" → PRESS-006 |
| M2 | `CampaignRunner` je jedan po kući; gradska knjiga na njemu traži specijalne slučajeve u snapshot-u i kasniju migraciju | `Underworld.Press`, top-level u `CampaignFile` |
| M3 | zapis sa satom zatvaranja ispada iz već odštampanog prozora (popup ≠ arhiva); save usred pucnjave gubi agregat; statička pretplata preživi reload scene | vreme otvaranja; flush na 06:00 i u `Compose`; `PressDesk` scenski objekat sa `OnDestroy` |
| M4 | 31 čitalac modala ne znači isto; `AnyOpen` menja ponašanje u tuđem epiku | šest ciljanih čitalaca; ujedinjenje kasnije, poseban commit |
| M5 | ledger dozvoljava odpauziranje iznutra → list iskače preko knjige; `DemoClockHud` je na 120 iznad 118 → grad se odpauzira kroz list; posle `BossFell` sat ide dalje | latch + uslovi `!IsOpen && !IsUp && !Fallen`; sorting 130 |
| M6 | `BusinessShutdowns.Changed` puca i na `Restored`/`Expired`/`Repaired` → posle load-a svaka zatvorena radnja „gori" | filter `Started`/`Extended` |
| M7 | F0 zamrzava šemu pre prvog izmerenog zapisa | F0 = pisac u memoriji + Play sesija; F1 = šema + save |
| m1 | kvartovi nose član („The Flats"), generatorovi šabloni su pisani bez njega; `StreetNames` zavisi od UnityEngine | parametar + ponovni proof šablona sa članom |
| m2 | 29-znakovno ime radnje probija 56 u naslovu | u naslovu kvart, radnja u pasusu; test protiv pravih tabela imena |
| m3 | `CorpseEvidence` ga nema na RoadDemo telima → `BodyFound` mrtav red | izbačen |
| m4 | `LedgerMenuScene` nema scenu; `OutfitEnd`/`StreetHud` instalira `DemoCrews` | scene: CoreDemo, MiniCoreDemo; instalacija kroz `DemoCrews` |
| m5 | `CaseOpened` puca i iz hapšenja | dedup po `CaseId` |

Pogledano i nije problem: `IncidentGap` 45 s kao granica priče; cena merenja (jednom po
incidentu); imena kao stringovi posle load-a (grad se uvek gradi iz `file.citySeed`);
jedan assembly (nema `.asmdef`); determinizam generatora uz prave zapise (knjiga se
snima).

---

## 11. Kako je izvedeno (2026-09-04)

F0-F3 su implementirani. Gradska knjiga je `Underworld.Press`; `PressDesk` je jedini
scenski pisac i zatvara agregat na tišinu, 06:00 i save; `Edition` i `PressText` su
čisti slagač i copy desk; `NewspaperSheet` crta isti list u popupu i ledger arhivi.
Save format je v3, sa praznom bezbednom migracijom v2 knjige i migracijom kuće starog
zatvorenika preko docket-a. `PoliceForce` presudu razrešava kroz roster zatvorenikove
kuće. Zvuk lista je zaseban Sonniss rez.

Automatizovani ugovori i četiri proof izdanja su deo `PipelineCommands`; završni run
je prošao svih 20 ugovora, save/police/ledger/underworld regresije i četiri izdanja.
Trajna mapa implementacije i ručni checklist su u `Docs/newspaper.md`. Vizuelni i world-state Play
dokazi (broj stvarnih pešaka, jedna/dve frakcije, modalni redosled, load u 09:00 i
rivalska presuda kroz punu scenu) ostaju korisnikov završni test i ne beleže se kao
prošli dok ih korisnik ne potvrdi.

Iz stvarne Play sesije su zasad izmerene samo dve stvari: konzola je zapisala
`[Press] MORNING EDITION day 1 at 06:00`, a pravi obilazak policajca zapisao je
`PoliceBlotter at The Heights, 0 dead, seen by 0, factions 1`. To dokazuje okidač i
javni put uzete izjave, ne dokazuje izgled lista ni preostale tačke iz prethodnog
pasusa. Namerno nisu dodate posledice novina (heat, reputacija, notability ili rivalsko
čitanje): odluka §9.8 ih ostavlja za poseban epik.
