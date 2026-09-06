# Plan: um igre na Infinite Axis Utility System (Dave Mark) — EPIC 48

Stanje na dan 2026-09-06, pročitano iz koda na `main` (HEAD a82732275). Ništa u kodu
nije dirano. Građa: `Docs/rival-families.md` (kako um kuće radi danas),
`Docs/rival-ai-plan.md` (šta je izmereno na ulici 2026-09-04), GAN-244 (EPIC 25,
D-tabela), Dave Mark: *Building a Better Centaur* (GDC 2015), Mark & Dill: *Improving AI
Decision Modeling Through Utility Theory* (GDC 2010), Dill: *Dual-Utility Reasoning*
(Game AI Pro 1, 2013).

Ovo je druga verzija: prva je prošla kontraški prolaz (§0b) i korisnikove presude (§0a).
Obe su ugrađene; tamo gde se razlikuju od prve verzije, piše zašto.

---

## 0a. Presude korisnika (2026-09-06)

1. **Obim: sve što bira.** Um kuće, sto, policijski desk i odgovor na vratima. Ulična
   borba (ko koga gađa, zaklon, beg) NIJE u epicu: „borba je za sad ok"; dobija svoj
   epic tek kad soak pokaže izmeren bag u odluci.
2. **Otvori odmah.** Nema faze „iste odluke kao pojasevi": „ni ovaj današnji nismo
   nešto specijalno istestirali da je prestabilan pa da ga čuvamo". Današnji um ostaje
   na prekidaču samo kao merilo za sweep.
3. **Krivulje u C# tabeli** kao `HouseMindConfig`, ne u editoru.
4. **Jedan epic** UTIL-000..009 u Linear; 008/009 backlog.
5. **Biblioteka: ne** — jezgro se piše samo (§10).
6. **Karta i sto ne troše potez**: obilazak (pravi potez na terenu) uvek dobija svoj
   potez od tri na sat.
7. **Lanac hapšenja se ne menja kao lanac**; menjaju se samo dve tačke gde neko bira:
   ko ide na koji poziv (policijski desk) i šta ekipa odgovori na vratima.
8. Pretpostavka bez izričite presude (predloženo, nije osporeno): spratovi 1–3 ostaju
   strogi jer su redosledi, sve ostalo je jedna arena od prvog dana.
9. **Stari um nije tjunovan, pa nije merilo.** „Novi tuče stari" ne znači ništa. Ciljevi
   u brojkama se ne znaju unapred („pojma nemam ni ja, igraćemo pa ćemo da vidimo"):
   presuda je IGRANJE u editoru; tabela ciljeva u `Docs/utility-ai-baseline.md` počinje
   kao privremene pretpostavke (§11a) i prepisuje se posle svake sesije igranja. Stari
   um ostaje samo kao kolona pored, radi orijentacije.

## 0b. Šta je kontraški prolaz promenio (usvojeno u celini)

| nalaz | promena u planu |
|---|---|
| množeći pojasevi ne mogu da ponove `Walk`; senka pa flip = pozorište | **Dual-Utility**: radnja ima RANG i TEŽINU; najviši rang s kandidatom iznad nule pobeđuje, težina×ocena bira unutar ranga. Uz presudu 2 (otvori odmah): rang 1–3 strogi, rang 4 arena |
| senka poredi jedan think, a pitanje je putanja od 30 dana | dokaz = **sweep** 30 seed-ova × 30 dana na oba mozga, iste kolone kao danas; klasifikator neslaganja u tri korpe, prikazuje se samo treća |
| `NoteThink` (D22) i `PaperCity.Answered` čitaju vraćeni tier | svaka radnja u knjizi nosi tier 1..9, `Think` ga i dalje vraća; test |
| cap 3 + top-3 truje `HouseBackoffs` | jedan takmičarski intent po thinku; `CrewsSpokenFor` na pogledu kao blizanac `Committed`; presuda 6 |
| `Max` nagađani, `BlockTake $2000` ravna grad | svaki `Max` izmeren pre ijedne krivulje (percentil raspodele iz sweep-a); komanda štampa stopu zasićenja po osi |
| Commitment kao bonus ponavlja izmerenu lekciju | `WorkedOut` ostaje tvrda kapija; meka verzija tek posle, sa kapijom kao kontrolom |
| pola „radnji" su sekvenceri | reasoner samo gde JESTE trade-off; sekvenceri = generatori kandidata s fiksnim rangom |
| razlog stola je enum (58 poređenja u testovima) | reasoner BIRA koji enum, ne piše rečenicu; isto za `HouseIntent.Reason` |
| ZAŠTO = najjače razmatranje odgovara na pogrešno pitanje | ZAŠTO = najslabije razmatranje pobednika + ono što je ugušilo drugoplasiranog |
| ulica ne pripada epicu | van (presuda 1) |
| redosled prema EPIC 43–47 | UTIL-000..004 pre EPIC 43; svaki kasniji epic dodaje RED u knjigu, nikad šetača |
| `HouseMind.Utility.cs` kao partial pada `sizes --check` | nova klasa `HouseReasoner`, tanjenje `HouseMind.cs` u istom ticketu |
| `Math.Exp` = Mono/CoreCLR razlika | bez transcendentnih u `Mind/`: racionalna sigmoida `x/(1+|x|)` / smoothstep; skener tokena |
| testova je 19, ne 21 | ispravljeno |
| ZAŠTO u ledgeru curi tuđe misli koje EPIC 43 gejtuje | u tragu i probi uvek; u ledgeru tek kad EPIC 43 da nivo |

---

## 1. Šta je IAUS, u tri rečenice

Svaka **radnja** ima spisak **razmatranja**. Razmatranje uzima jedan **ulaz** iz sveta
(dolari u sefu, sati od napada, strah na bloku), svede ga na 0..1 i provuče kroz
**krivulju odziva** (linearna, kvadratna, sigmoida, logit; četiri broja m, k, b, c).
Ocena radnje je **proizvod** razmatranja, popravljen za broj razmatranja da se šest
sitnih faktora ne sruši na nulu:

    mod    = 1 - 1/n
    ocena  = p + (1 - p) * mod * p        (p = proizvod krivulja)
    (rang, težina × ocena)                 (Dual-Utility: rang prvo, pa težina×ocena)

Um bira najviši **rang** koji ima kandidata iznad nule, pa unutar njega najvišu
**težina × ocena** po (radnja, meta); meta je blok, vrata, kuća, čovek, poziv, jedinica.
Sve je podatak (tabela), pravila korisnika su kapije (0/1 osa s imenom pravila). Um
ostaje čist i determinističan: isti pogled, ista ocena, dvaput.

---

## 2. Gde igra danas odlučuje (izmereno)

| mesto | fajl | oblik danas | u epicu |
|---|---|---|---|
| um kuće | `Assets/Scripts/Outfit/HouseMind.cs` (2149 l.) | 9 strogih pojaseva, 13 šetača (`Walk` l. 449), prvi pojas s kandidatom pobeđuje; jedna ocena bloka (`Score` l. 1620 = uzimak − skokovi·$100 − pažnja·$20); D-tabela `HouseMindConfig` (25 brojeva) | **DA** (000–004) |
| sto | `Assets/Scripts/Outfit/HouseDiplomacy.cs` `Answer` (l. 491) | `switch` po vrsti, tvrda pravila, `DeskAnswer` s enum-razlogom | **DA** (005) |
| policijski desk | `Assets/RoadDemo/PoliceDispatch.Complaint.cs` (`_calls`, `TickCalls` l. 269, `NearestToAnswer` l. 428), `PoliceDispatch.cs` `Nearest` l. 734 | redom kako stižu + strpljenje; najbliža jedinica; nikad se ne skida s poziva; kola po vrelini (lestvica, već tabela) | **DA** (006) |
| odgovor na vratima | `Assets/Scripts/Police/SurrenderRoll.cs` | VEĆ korisnost: hrabrost·0.5 + temper·0.3 − lojalnost·0.2, pod 5–95 %, sejani tok | **DA** (007), više osa |
| gazda | `Assets/Scripts/Territory/TerritoryRacket.cs` `TerritoryComplianceEvaluation` (l. 257) | VEĆ korisnost: 4 težine + 2 praga + pomak ličnosti + tiera | NE (ne menja ni jednu presudu) |
| ulična borba | `DemoCrews.Combat.cs` (`BestMark` l. 249 po hicu, nerv l. 1666 na smrt druga), `DemoCrews.Cover.cs` (prostorni upit), `CrewWalker.Flee` | tri razna takta, nedeterministična (fizika, dt), 10.811 linija partiala | NE (presuda 1) |
| lanac hapšenja posle pitanja (lisice, cela jedinica, sprovod, prag stanice, ćelija, sud) | `PoliceDispatch.Arrest/Custody`, `PrisonPipeline`, `CourtCase` | redosled, ne izbor | NE (presuda 7) |
| pešaci, kola, patrole | `HumanBehavior`, `RoadCar`, `PolicePatrolCar` | rute i stanja | NE |

---

## 3. Šta se dobija, i šta se gubi

Dobija: kuća meri UMESTO da broji. Danas jedan incident od pre 11 sati blokira širenje
(pojas 5 pre 7), sef od $300 kupuje pištolj ako je pojas 8 na redu, tri kolebljiva
vrata drže kuću kod kuće (Z2 u `rival-ai-plan.md`). U areni napad star 11 h vredi 0.1,
blok od $9.000 vredi 0.9, kuća ide na blok i stavlja gard usput. Policija: pucnjava s
mrtvima dobija kola PRE stare iznude, jedinica se skida s iznude kad pozornik padne dva
bloka dalje. Vrata: ekipa s poternicom i pištoljima puca, goloruki novajlije idu mirno.

Gubi: čitljivost pojasa („kuća 4 je u pojasu 6") postaje „kuća 4: Defend 0.73, Expand
0.71". Zato trag i UI štampaju ZAŠTO kao najslabije razmatranje pobednika i ono što je
ugušilo drugoplasiranog — jer korisnikovo pitanje je „zašto kuća STOJI", ne „zašto je
gard jak". Drugi gubitak: bliske ocene = kuća menja odluku svakog sata. `WorkedOut`
(izmereno dvaput: bez njega trećina manje zemlje, s jačim pola vrata) ostaje tvrda
kapija; meki zalet tek posle flipa, sa kapijom kao kontrolom u sweep-u.

---

## 4. Arhitektura

Novi fajlovi u `Assets/Scripts/Outfit/Mind/` (čisti, bez UnityEngine, bez
`Math.Exp/Pow/Log`, bez `Random`; skener zabranjenih tokena pokriva direktorijum):

| fajl | šta |
|---|---|
| `Curve.cs` | `enum CurveShape { Linear, Quadratic, Sigmoid, Logit }` + struct `Curve(shape, m, k, b, c)`, `double Y(double x)` Clamp01; sigmoida racionalna, bez transcendentnih |
| `Axis.cs` | `AxisId`, ime, `Line` (rečenica s brojem), `Read(context)` → 0..1 uz `Max` iz tabele |
| `Consideration.cs` | `(AxisId, Curve)`; nula = veto (kapija) |
| `MindAction.cs` | ime, **rang**, **težina**, razmatranja, `Tier` (1..9, obavezno), `Targets(context)` |
| `Reasoner.cs` | `Score(action, target)` s Markovom popravkom i ranim izlazom; `Best(context, into)` → `(action, target, rank, score, weakest, suppressor)`; `double`, poređenje 1e-6, izjednačenje lomi `string.CompareOrdinal(Key)` |
| `HouseAxes.cs` | ose kuće (§5) |
| `HouseBook.cs` | TABELA radnji kuće (§6); jedno mesto za veto korisnika |
| `HouseReasoner.cs` | NOVA statička klasa (ne partial): generatori kandidata → `Reasoner.Best` → jedan takmičarski `HouseIntent` + „uvek pored" |
| `DeskBook.cs`, `DeskAxes.cs` | sto (§7) |

Policija u `Assets/Scripts/Police/` (čisto kao `WantedLevels`, `PoliceShifts`):
`PoliceDesk.cs`, `PoliceDeskAxes.cs`, `PoliceDeskBook.cs` (§8); `SurrenderRoll` dobija
`DoorBook` (§9). `PoliceDispatch` ostaje ivica i samo pita desk.

Runtime ivica kuće ostaje `TerritoryRuntime.Minds.cs`: isti `Look`, isti `Carry`, isti
`HouseBackoffs`, isti `DriveTrace.House`; `Think` i dalje vraća tier (D22 `NoteThink`,
`PaperCity.Answered`). Prekidač `HouseMindConfig.Brain = Tiers | Utility`; `Tiers`
ostaje samo kao merilo za sweep.

Budžet (`python3 Tools/project.py sizes --check`): svaki novi fajl ispod 600 linija;
`HouseMind.cs` se tanji u ISTOM ticketu u kom `HouseReasoner` raste (šetači postaju
`Candidates.*` bez `if (X) return Tier`).

---

## 5. Ose kuće (ulazi iz `HouseView`, ništa novo u pogledu)

`Max` u tabeli je PLACEHOLDER dok UTIL-001 ne izmeri; komanda `gangsters_house_axes`
štampa raspodelu i stopu zasićenja po osi na 30 seed-ova, pa se `Max` upisuje kao
percentil (p90) izmerene raspodele.

| osa | čita | Max (izmeriti) | `Line` |
|---|---|---|---|
| SafeDays | `Safe / DailyPayroll` | ~14 d | „sef pokriva N dana plata" |
| WeeklyTakeShare | `WeeklyTake / (7·DailyPayroll)` | ~2 | „vrata nose N% plata" |
| CrewsFree | slobodne ekipe / sve | 1 | „N od M ekipa besposleno" |
| CrewsUnderTarget | fali do `BlocksLed·CrewsPerBlock + Spare` | 1 | „fali N ekipa do cilja" |
| UnarmedShare | `UnarmedMen / WorkingHoods` | 1 | „N ljudi bez oružja" |
| HoodsShort | fale hoods do `MaxHoods` | 4 | „ekipa X ima N od 4" |
| IncidentFresh | `1 − h od poslednjeg / ThreatMemoryHours` | 1 | „napad pre N h" |
| IncidentUnanswered | neodgovoren u `AnswerWindowHours` | 0/1 | „napad bez odgovora" |
| FrontAlarm | nevolja od fronta / `HqAlarmMetres` | 1 | „pucnjava N m od kuće" |
| BlockTake (meta) | uzimak bloka | **p90 iz sweep-a** (srednji blok $1.500–7.500, bogat $20k) | „blok vredi $N nedeljno" |
| BlockHops (meta) | skokovi od našeg bloka | 4 | „N ulica daleko" |
| BlockHeat (meta) | `PoliceAttention` | 100 | „policija gleda blok N/100" |
| BlockAskable (meta) | vrata koja se mogu pitati / sva | 1 | „N vrata još nepitana" |
| BlockWalkedAgo (meta) | h od `LastWalked` / `DemandCooldownHours` | 1 | „obišli pre N h" |
| Grievance (kuća) | `Grievance / AttackBusinessAt` | 2 | „duguju nam N radnji" |
| TheirStrength (kuća) | `TheirEndurance / Endurance` | 3 | „jači su N puta" |
| StanceWar (kuća) | rat / primirje / mir | 0/1 | „u ratu smo" |
| MenInCells | `Cells / WorkingHoods` | 1 | „N naših u ćeliji" |
| BailCoverage | `Safe / Bail` | 3 | „kaucija je N% sefa" |
| Backoff | `Blocked(key)` | veto | „odbijeno pre N h" |
| WorkedOut | Z2 kapija | veto | „na ovom bloku ima još da se pita" |
| CrewSpokenFor | ekipa već dobila intent OVOG thinka | veto | „ekipa X je već poslata" |

---

## 6. Radnje kuće (Dual-Utility tabela)

Rang 1–3 su redosledi (nema trade-off-a); rang 4 je arena. Svaka radnja nosi `Tier`
današnjeg pojasa da bi D22 i MVP test čitali isto što i danas.

| radnja | rang | težina | Tier | meta | razmatranja (kapije boldom) |
|---|---|---|---|---|---|
| Home (front pod vatrom) | 1 | 1 | 1 | — | FrontAlarm↑ sigmoida, **CrewsFree** |
| Merge | 2 | 1 | 2 | ekipa | SafeDays↓ strmo ispod 3 d, **≥ 2 ekipe** |
| Replace / Sign | 3 | 1 | 3 | ekipa | HoodsShort↑, **CanSign** (A30, D9) |
| Law (savet pa kaucija) | 3 | 1 | 3 | čovek | MenInCells↑, BailCoverage↑, rang čoveka↑, **savet pre kaucije** |
| Answer (uzvrati) | 4 | 1.0 | 5 | kuća | **IncidentUnanswered**, IncidentFresh↑, CrewsFree, **Backoff** |
| Defend (gard) | 4 | 0.9 | 6 | blok | IncidentFresh↑, BlockTake↑, CrewsFree, **nije već čuvan** |
| Feud (lestvica) | 4 | 0.9 | 6 | kuća | Grievance↑, TheirStrength↓, SafeDays↑, **ReadyToCollide**, **sledeći stepenik lestvice** |
| Expand (radi na bloku) | 4 | 0.8 | 7 | blok | BlockTake↑, BlockHops↓, BlockHeat↓ (**veto iznad `WalkAttentionCap`**), BlockAskable↑, BlockWalkedAgo↑, CrewsFree, **WorkedOut**, **nikad tuđa vrata** |
| Grow (potpiši) | 4 | 0.6 | 8 | — | CrewsUnderTarget↑, WeeklyTakeShare↑, SafeDays↑, **NothingLeftToAsk** |
| Buy (pištolj / kola) | 4 | 0.5 | 8 | čovek / ekipa | UnarmedShare↑, SafeDays↑ strmo (`ReserveDays` + cena), **QuietThinks** (D22) |
| Release, Collect, AnswerTheCard, Diplomacy | van takmičenja | | | | kao danas; **ne troše potez** (presuda 6) |

Jedan takmičarski intent po thinku (kao danas `Walk`). Ako korisnik posle hoće dva,
`CrewSpokenFor` je već tu.

Ličnost kuće (`HouseBook.Temper`: Lakom, Oprezan, Ratoboran = množioci na tri težine):
UTIL-008, backlog.

---

## 7. Sto

Svaki `ProposalKind` = radnja s dve mete (Da, Ne). Tvrda pravila ostaju kapije
(`MustAccept` → Ne dobija 0). Ostalo krivulja: AfterMoney / RetakeAt sigmoida,
TheirStrength, Grievance, CrewOnTheirGround. `DeskAnswer.Reason` OSTAJE enum
(`Reason*` konstante, 58 poređenja u `DiplomacyTests`): reasoner bira koji, po
razmatranju koje je presudilo. Dokaz: `DiplomacyTests` zeleni + sweep sa stolom.

## 8. Policijski desk

Čist model `PoliceDesk.Rank(calls, units, into)` ocenjuje parove (poziv, jedinica):

| osa | `Line` |
|---|---|
| CallAge | „poziv star N s" |
| Severity (mrtvi > pozornik ranjen > hici > iznuda) | „N mrtvih na mestu" |
| DoorTier | „vrata tiera N" |
| Distance (jedinica → poziv) | „N m daleko" |
| UnitsFree | „N jedinica slobodno" |
| PullOffCost (jedinica već na manjem pozivu) | „skida se s poziva X" |

`PoliceDispatch.Complaint.TickCalls` i `Wanted.NearestAvailable` pitaju desk umesto
`Nearest`. Lestvica kola po vrelini ostaje tabela. Sve posle „jedinica stigla" je lanac
(presuda 7) i ne menja se. Dokaz: `PoliceTests` + scenario „pucnjava s mrtvima ide pre
stare iznude" + „jedinica se skida s iznude kad pozornik padne".

## 9. Odgovor na vratima

`SurrenderRoll` postaje tri radnje (Quiet, Run, Fight) nad osama: hrabrost poručnika,
temper, lojalnost (današnje tri) + naoružan, brojnost (pozornici : ljudi), poternica
(nivo), kaucija u sefu (kuća može da izvuče), kola blizu. Sejani tok ostaje kao JEDNA
osa (pravilo iz koda: „0 ili 1 bi napravilo tablicu"), isti `StreamFor(citySeed,
crewKey, incident)`. Igračeva naredba „napadni" i dalje pregazi. Sve posle odgovora
(lisice, cela jedinica, sprovod, prag) ostaje. Dokaz: `PoliceTests` + „ekipa s poternicom
i pištoljima puca; goloruki novajlije idu mirno".

---

## 10. Biblioteka ili svoje jezgro

Pregledano 2026-09-06: `ZorPastaman/UtilityAI` (MIT, čist C# Brain/Action/Consideration,
Unity omotač odvojen; bez Markove popravke, bez meta po radnji, `float`),
`Bartvanderkruys/utility-ai` (MIT, Unity 5, MonoBehaviour), `DreamersIncStudios/ECS-IAUS-sytstem`
(MIT, jedini pravi IAUS, DOTS/ECS, ne vadi se), `NWagter/Utility_AI_Unity` (MIT, školski),
Apex Utility AI (komercijalan, ugašen 2019). Ni jedan ne daje `double` + stabilno
izjednačenje, rang+težinu, Markovu popravku, metu po radnji, rečenicu po razmatranju,
rad bez alokacija ni skener tokena. Jezgro je ~300 linija; krivulje su javne formule.
Piše se svoje.

---

## 11. Redosled rada (tickets)

| ticket | šta | dokaz |
|---|---|---|
| UTIL-000 | jezgro: `Curve`, `Axis`, `Consideration`, `MindAction`, `Reasoner` (rang + težina, Markova popravka, rani izlaz, `double`, ordinal izjednačenje, racionalna sigmoida); `UtilityCoreTests`; skener zabranjenih tokena (`TerritoryRuntime`, `Ledger`, `Roll`, `Random`, `Math.Exp/Pow/Log`) na `Outfit/Mind/` | `gangsters_house_tests` nova grupa |
| UTIL-001 | mera pre krivulje: `HouseAxes` s `Line`; `gangsters_house_axes --sweep 30` štampa raspodelu i stopu zasićenja po osi; `Max` = p90; baseline sweep današnjeg uma (blokovi/vrata po kući dan 14, dani u minusu, zamrznute kuće) u `Docs/utility-ai-baseline.md` — korisnik pokreće editor | dokument s brojevima |
| UTIL-002 | `HouseBook` (§6) i `HouseReasoner`; šetači = `Candidates.*`; svaka radnja nosi Tier; jedan takmičarski intent; `CrewSpokenFor`; karta i sto ne troše potez; `WorkedOut` kapija; `Brain` prekidač (`Tiers` podrazumevano do 003); `HouseMind.cs` tanji u istom ticketu | 19 starih testova zeleni na `Tiers`; `sizes --check` |
| UTIL-003 | PRESUDA: `gangsters_underworld_sim --brain tiers|utility --sweep 30 --days 30`, iste kolone, stari um kao kolona pored; klasifikator neslaganja prikazuje samo treću korpu; tri Z-tvrdnje; `Brain = Utility` podrazumevano kad su Z-tvrdnje zelene, nema izuzetaka, i korisnik posle IGRANJA kaže da ima smisla (presuda 9); privremeni ciljevi §11a se prepisuju posle igranja; testovi koji su brojali pojaseve prepisani na „prva radnja je X" | sweep tabela + korisnikov zapis posle igranja u `Docs/utility-ai-baseline.md` |
| UTIL-004 | reči: trag štampa tri najbolje + ZAŠTO (najslabije razmatranje pobednika, gušitelj drugoplasiranog); `HouseThinkRecord.Lines` nosi ocene; `HouseIntent.Reason` fiksna linija po (radnja, razmatranje); u ledgeru tek kad EPIC 43 da nivo | probni ispis = ono što ekran pokazuje |
| UTIL-005 | sto (`DeskBook`, `DeskAxes`); reasoner bira enum-razlog | `DiplomacyTests` zeleni; sweep |
| UTIL-006 | policijski desk (§8): `PoliceDesk` čist; `PoliceDispatch` pita | `PoliceTests` + dva scenarija |
| UTIL-007 | odgovor na vratima (§9): `DoorBook` u `SurrenderRoll`, pet novih osa, sejani tok kao osa | `PoliceTests` + scenario |
| UTIL-008 | `HouseBook.Temper` (tri ćudi) — backlog | sweep pokazuje razliku |
| UTIL-009 | `Tools/mind/curves.py` crta krivulje u ASCII i generiše `Docs/utility-ai-book.md` iz koda (freshness u `project.py audit`); `Docs/utility-ai.md` sloj; memorija; sve na Done — backlog | dokument + audit |

### 11a. Privremeni ciljevi (pretpostavke, prepisuju se posle igranja)

Nisu presuda. Prve brojke da sweep ima šta da štampa u crvenom ili zelenom; korisnik ih
menja posle svake sesije u editoru.

| cilj (14. dan, bez sudara) | privremeno |
|---|---|
| blokova koje kuća vodi | 3 |
| dana zamrznuta (bez poteza na terenu) | 0 |
| dana zaredom u minusu | ≤ 2 |
| sati da se ceo blok obiđe od vrata do vrata | ≤ 24 |
| prvi sudar dve kuće | dan 10–20 |
| vrata koja plaćaju na vođenom bloku | ≥ 60 % |

Redosled tvrd: 000 → 001 → 002 → 003 → 004; pa 005, 006, 007 u bilo kom redu;
008/009 kad korisnik zatraži. **UTIL-000..004 pre EPIC 43**; svaki kasniji epic (43,
44, 45, 46, 47) dodaje RED u `HouseBook`, nikad šetača.

---

## 12. Pre-mortem

| rizik | znak | lek |
|---|---|---|
| kaša u areni | trag A, B, A, B | `WorkedOut` kapija; sweep s kapijom kao kontrola pre mekog zaleta |
| tabela nečitljiva | korisnik ne zna zašto kuća stoji | ZAŠTO = najslabije + gušitelj; UTIL-001 komanda PRE flipa |
| novi um gori od starog | sweep ispod baseline-a | `Brain = Tiers` ostaje; flip tek kad tuče brojke |
| pravila u `if`-ovima tiho nestanu | test padne ili nestane | svako pravilo = kapija s imenom; stari testovi ostaju |
| Mono/CoreCLR poslednji bit | test prolazi ofline, pada u editoru | `double`, bez transcendentnih, 1e-6, ordinal ključ |
| `Max` pogrešan | jedna osa 1.0 svuda | stopa zasićenja po osi u komandi |
| desk skida jedinicu bez kraja | ping-pong između dva poziva | PullOffCost raste sa svakim skidanjem; kapija „ne skidaj dvaput" |
| vrata: previše pucaju | soak `--law` broji mrtve pozornike | težine kao danas (srednja ekipa 0.30), nove ose pomeraju, ne zamenjuju |
| tabela u C# = rebuild da bi se menjala | sporo | prihvaćeno (presuda 3) |
