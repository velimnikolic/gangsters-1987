# Plan: um igre na Infinite Axis Utility System (Dave Mark)

Stanje na dan 2026-09-06, pročitano iz koda na `main` (HEAD a82732275). Ništa u kodu
nije dirano. Građa: `Docs/rival-families.md` (kako um kuće radi danas),
`Docs/rival-ai-plan.md` (šta je izmereno na ulici 2026-09-04), GAN-244 (EPIC 25,
D-tabela), Dave Mark: *Building a Better Centaur* (GDC 2015), Mark & Dill: *Improving AI
Decision Modeling Through Utility Theory* (GDC 2010).

---

## 0. Šta je IAUS, u tri rečenice

Svaka **radnja** (Action) ima spisak **razmatranja** (Consideration). Razmatranje uzima
jedan **ulaz** iz sveta (dolari u sefu, sati od napada, strah na bloku), svede ga na
0..1 i provuče kroz **krivulju odziva** (linearna, kvadratna, logistička, logit; četiri
broja: m, k, b, c). Ocena radnje je **proizvod** svih razmatranja, popravljen za broj
razmatranja da se šest sitnih faktora ne sruši na nulu:

    mod   = 1 - 1/n
    ocena = p + (1 - p) * mod * p          (p = proizvod krivulja)
    konačno = težina_radnje * ocena         (težina = pojas: 5 preživi, 4 plate, ... 1 dokolica)

Um bira **najviše ocenjenu (radnja, meta)**; meta je blok, vrata, kuća, čovek. Sve je
podatak (tabela krivulja), ništa nije `if`. Um ostaje čist i determinističan: isti pogled,
ista ocena, dvaput.

---

## 1. Gde igra danas odlučuje (izmereno)

| mesto | fajl | oblik danas | kandidat |
|---|---|---|---|
| um kuće | `Assets/Scripts/Outfit/HouseMind.cs` (2149 l.) | 9 strogih pojaseva, 13 „šetača" (`Walk`: Home, Merge, Replace, Law, Answer, Defend, Feud, Expand, Grow, Buy + Release, Collect, AnswerTheCard, Diplomacy), prvi pojas s kandidatom pobeđuje; jedna ocena bloka (`Score` = nedeljni uzimak − skokovi·$100 − pažnja·$20); D-tabela `HouseMindConfig` (25 brojeva) | **DA, prvi** |
| sto (odgovor na predlog) | `Assets/Scripts/Outfit/HouseDiplomacy.cs` `Answer` (l. 491) | `switch` po vrsti predloga, tvrda pravila (pobeđeni ne može da odbije), `DeskAnswer` s razlogom u rečima | **DA, drugi** |
| gazda (plaća / koleba / odbija) | `Assets/Scripts/Territory/TerritoryRacket.cs` `TerritoryComplianceEvaluation` (l. 257) | VEĆ korisnost: linearna suma 4 težine + dva praga + pomak ličnosti + pomak tiera | već jeste; samo krivulje umesto prave (nisko) |
| ulični borac (koga gađa, kad beži, kad u zaklon) | `Assets/RoadDemo/DemoCrews.Combat.cs` (`BestMark`, `CloserThreatThan`, nerv l. 1666), `DemoCrews.Cover.cs`, `CrewWalker.Flee/PanicFrom` | najbliži čovek + izuzeci; nerv = `Random.value < BossNerve`; 15 `Random.*` u Combat, 54 u CrewWalker | **DA, treći**, s budžetom |
| policija (ko odgovara na koji poziv) | `Assets/RoadDemo/PoliceDispatch.*.cs` (`Nearest`, `NearestToAnswer`, `NearestAvailable`) | najbliža jedinica; redosled poziva = redosled dolaska | samo „koji poziv prvi" (nisko) |
| pešaci, kola, patrole, pritvor, sud | `HumanBehavior`, `RoadCar`, `PolicePatrolCar`, `PrisonPipeline`, `CourtCase` | rute, rasporedi, stanja bez izbora | **NE** — nema odluke koju bi um donosio |

„Cela igra" u smislu uma = prva četiri reda. Peti je logistika, šesti je kretanje.

Šta se čuva po pravilu korisnika i po `Docs/rival-families.md`:

* um je ČIST: ne čita `TerritoryRuntime`, `Ledger`, `Roll`; test `gangsters_house_tests`
  skenira `HouseMind.cs` za te tokene — skener se proširuje na nove fajlove uma;
* svaki broj u UI se čita rečima (pravilo „mehanika objašnjena u UI"): svako razmatranje
  nosi rečenicu, trag štampa tri najbolje radnje s ocenama i rečenicama;
* svaki broj u jednoj klasi (D-tabela), nikad literal u metodi: krivulje žive u C#
  tabeli pored `HouseMindConfig`, ne u ScriptableObject-u (scene su probni stolovi,
  ništa se ne podešava u editoru);
* determinizam: `double`, zaokruživanje pre poređenja (Mono i CoreCLR se ne slažu oko
  poslednjeg bita — komentar na `HouseDiplomacy.Answer`), izjednačene ocene lomi
  stabilan ključ (`HouseIntent.Key`), nikad redosled liste.

---

## 2. Šta se dobija, i šta se gubi

Dobija: kuća meri UMESTO da broji. Danas jedan incident od pre 11 sati blokira širenje
(pojas 5 pre pojasa 7), sef od $300 kupuje pištolj ako je pojas 8 na redu, a tri
kolebljiva vrata drže celu kuću kod kuće (nalaz Z2 u `rival-ai-plan.md`). S krivuljama
napad star 11 h vredi 0.1, širenje na blok od $900 vredi 0.8, i kuća ide na blok, a gard
stavlja usput. Ista tabela za 21 kuću, ličnost kuće = drugi brojevi u istoj tabeli.

Gubi: čitljivost pojasa („kuća 4 je u pojasu 6") postaje „kuća 4 dala 0.73 odbrani,
0.71 širenju". Zato trag i UI MORAJU da štampaju reči po razmatranju, ne samo broj.
Drugi gubitak: ocene blizu jedna drugoj = kuća menja odluku svakog sata („kaša").
Lek je razmatranje **zalet** (commitment): radnja koja već teče dobija bonus dok ne
završi ili dok je ne pregazi ocena veća za prag.

---

## 3. Arhitektura (novi fajlovi, sve u `Assets/Scripts/Outfit/Mind/`)

| fajl | šta | čist? |
|---|---|---|
| `Curve.cs` | `enum CurveShape { Linear, Quadratic, Logistic, Logit }` + struct `Curve(shape, m, k, b, c)`, `double Y(double x)` s Clamp01 | da, bez alokacija |
| `Axis.cs` | jedna osa: `AxisId`, ime, rečenica, `Read(context) → 0..1` (normalizacija u odnosu na `Max` iz tabele) | da |
| `Consideration.cs` | `(AxisId, Curve)`; nula = veto (kapija) | da |
| `MindAction.cs` | ime, pojas-težina, razmatranja, `Kind` (koji šetač je nekad bio), `Targets(context)` | da |
| `Reasoner.cs` | `Score(action, target)` s Markovom popravkom i ranim izlazom (kad proizvod padne ispod najbolje dosadašnje ocene, prekini), `Best(context, into)` vraća listu `(action, target, score, reči)` sortiranu | da |
| `HouseAxes.cs` | 20-30 osa iz `HouseView` (v. §4) | da |
| `HouseBook.cs` | TABELA: sve radnje kuće s razmatranjima i krivuljama; jedno mesto za veto korisnika, kao `HouseMindConfig` | da |
| `HouseMind.Utility.cs` | novi `Think`: generiši kandidate → `Reasoner.Best` → najviše tri u `HouseIntent` kao danas | da |
| `DeskBook.cs`, `DeskAxes.cs` | isto za sto (§5) | da |
| `FighterBook.cs`, `FighterAxes.cs` | isto za ulicu (§6); živi u `Assets/RoadDemo`, ne u `Outfit` | ne (čita scenu), ali bez `Random` |

Runtime ivica ostaje `TerritoryRuntime.Minds.cs`: isti `Look` → pogled, isti `Carry`
kroz ista vrata (`TerritoryCommandGateway.Submit`, `Underworld.Issue`, `HouseOps`), isti
`HouseBackoffs`, isti `DriveTrace.House`. Prekidač `HouseMindConfig.Brain = Tiers |
Utility | Both` (Both = senka, §7).

Budžet fajlova (`python3 Tools/project.py sizes`): svaki novi fajl ispod 600 linija;
`HouseMind.cs` se posle prebacivanja tanji — šetači postaju generatori kandidata
(`Candidates.Home(view, into)`) bez `if (X) return Tier`.

---

## 4. Ose kuće (ulazi iz `HouseView`, ništa novo u pogledu)

Svaka osa: ime, rečenica, čitanje, `Max` za normalizaciju. Prvih dvadeset:

| osa | čita | Max | rečenica u tragu |
|---|---|---|---|
| SafeDays | `Accounts.Safe / DailyPayroll` | 14 d | „sef pokriva N dana plata" |
| WeeklyTakeShare | `WeeklyTake / (7·DailyPayroll)` | 2 | „vrata nose N% plata" |
| CrewsFree | slobodne ekipe / sve | 1 | „N od M ekipa besposleno" |
| CrewsUnderTarget | `(BlocksLed·CrewsPerBlock + Spare − ekipe) / target` | 1 | „fali N ekipa do cilja" |
| UnarmedShare | `UnarmedMen / WorkingHoods` | 1 | „N ljudi bez oružja" |
| HoodsShort | fale hoods do `MaxHoods` po ekipi | 4 | „ekipa X ima N od 4" |
| IncidentFresh | `1 − sati od poslednjeg incidenta / ThreatMemoryHours` | 1 | „napad pre N h" |
| IncidentUnanswered | ima li neodgovoren u `AnswerWindowHours` | 0/1 | „napad bez odgovora" |
| FrontAlarm | rastojanje nevolje od fronta / `HqAlarmMetres` | 1 | „pucnjava N m od kuće" |
| BlockTake (meta) | uzimak bloka, otvorena vrata | $2000 | „blok vredi $N nedeljno" |
| BlockHops (meta) | skokovi od najbližeg našeg bloka | 4 | „N ulica daleko" |
| BlockHeat (meta) | `PoliceAttention(block)` | 100 | „policija gleda blok N/100" |
| BlockAskable (meta) | vrata koja se još mogu pitati / sva | 1 | „N vrata još nepitana" |
| BlockWalkedAgo (meta) | sati od `LastWalked` / `DemandCooldownHours` | 1 | „obišli pre N h" |
| Grievance (kuća-meta) | `Grievance(them) / AttackBusinessAt` | 2 | „duguju nam N radnji" |
| TheirStrength (kuća-meta) | `TheirEndurance / Endurance` | 3 | „jači su N puta" |
| StanceWar (kuća-meta) | rat / primirje / mir | 0/1 | „u ratu smo" |
| MenInCells | `Cells.Count / WorkingHoods` | 1 | „N naših u ćeliji" |
| BailCoverage | `Safe / Bail` | 3 | „kaucija je N% sefa" |
| Commitment | radnja iste vrste već teče | 0/1 | „već radimo to" |
| Backoff | `Blocked(key)` | 0/1 (veto) | „odbijeno pre N h" |

Sve ove vrednosti pogled VEĆ daje; ni jedna ne prelazi zid (tuđi roster, sef, knjiga,
ličnost gazde, kocka ostaju izvan).

---

## 5. Radnje kuće (današnjih 13 šetača kao tabela)

Prva verzija tabele ponavlja današnji redosled kroz težine pojasa, da prvi prelaz
donosi ISTE odluke na yardstick-u (`gangsters_house_tests`, `gangsters_underworld_sim`,
`TheMvpRunsForEverySeed`). Tek posle flip-a se težine spuštaju i krivulje omekšavaju.

| radnja | pojas (težina) | meta | razmatranja (kapije boldom) |
|---|---|---|---|
| Home (front pod vatrom) | 5 | — | FrontAlarm↑ logistička, **CrewsFree** |
| Merge | 4 | ekipa | SafeDays↓ (ispod 3 d strmo), broj ekipa ≥ 2 |
| Replace / Sign | 3 | ekipa | HoodsShort↑, SafeDays↑ (`ReserveDays`), WeeklyTakeShare↑, **CanSign** |
| Bail / Retain | 3 | čovek | MenInCells↑, BailCoverage↑, rang čoveka↑ |
| Answer (uzvrati) | 3 | kuća | IncidentUnanswered, IncidentFresh↑, CrewsFree, **Backoff** |
| Defend (gard) | 2.5 | blok | IncidentFresh↑, BlockTake↑, CrewsFree, nije već čuvan |
| Feud (lestvica: pretnja/udar/otimanje) | 2.5 | kuća | Grievance↑, TheirStrength↓, SafeDays↑, ReadyToCollide |
| Expand (radi na bloku / pitaj vrata) | 2 | blok | BlockTake↑, BlockHops↓, BlockHeat↓ (veto iznad `WalkAttentionCap`), BlockAskable↑, BlockWalkedAgo↑, CrewsFree, Commitment |
| Grow (potpiši čoveka) | 1.5 | — | CrewsUnderTarget↑, WeeklyTakeShare↑, SafeDays↑, NothingLeftToAsk |
| Buy (pištolj / kola) | 1 | čovek / ekipa | UnarmedShare↑, SafeDays↑ strmo (`ReserveDays` + cena), QuietThinks |
| Release, Collect, AnswerTheCard, Diplomacy | van takmičenja | — | ostaju „uvek pored" kao danas: ne troše ekipu ili su vezani za sat (runda, karta, sto) |

Pojasevi: kad je Home 0.4·5 = 2.0, Expand mora 1.0·2 da izjednači — dakle „kuća pod
vatrom širi se samo ako je uzbuna slaba". To je namerno: pojasevi nisu nestali, samo su
propustljivi. Kad flip prođe, korisnik menja jedan broj u `HouseBook` da bi kuća bila
lakomija ili plašljivija — bez koda, kao i danas s D-tabelom.

Ličnost kuće: `HouseBook` ima `Temper` po kući (tri profila: Lakom, Oprezan, Ratoboran) =
množioci na tri težine. Ne u prvoj verziji.

---

## 6. Sto i ulica

**Sto** (`HouseDiplomacy.Answer`): svaki `ProposalKind` postaje radnja s dve mete (Da,
Ne). Tvrda pravila ostaju kapije s ocenom 0/1 (`MustAccept` → Ne dobija 0). Ostalo
postaje krivulja: „posle novca ispod praga otimanja" = osa AfterMoney / RetakeAt
logistička. `DeskAnswer` zadržava razlog u rečima — razlog je rečenica najjačeg
razmatranja, ne više fiksni string.

**Ulica** (`DemoCrews.Combat`, `Cover`, `CrewWalker.Flee`): jedan mali reasoner PO
JEDINICI (ne po čoveku), otkucaj na 0.25 s, ne svaki frejm. Radnje: Gađaj (meta =
čovek u dometu), Zaklon (meta = `CoverAnchor`), Beži, Stoj. Ose: rastojanje, viđen,
ranjen, oružje, prijatelj pao u `NerveRange`, poručnik ili hood, brojnost, zaklon
postoji. Nerv prestaje da bude `Random.value < BossNerve`: postaje osa PalMrtavBlizu ×
krivulja po rangu, a slučajnost, ako je korisnik hoće, iz sejanog toka (`MixSeed`,
sim-determinism traps), nikad `UnityEngine.Random`. Pravila korisnika ostaju kapije:
čovek koji trči nikad ne puca (BarrelOn 30°), samo pilion puca s motora, `Engagement.May`
(stav) je kapija na Gađaj.

**Gazda**: `TerritoryComplianceEvaluation` ostaje kakav jeste; četiri težine se mogu
izraziti kao četiri linearne ose kroz isti `Reasoner`, ali to ne menja ni jednu presudu.
Radi se samo ako se posle flip-a pokaže da gazdi treba krivulja (npr. strah da deluje
tek iznad praga). Nije u epicu.

**Policija**: samo `PoliceDispatch.Complaint` red poziva — osa starosti poziva, tier
vrata, ima li mrtvih. Nije u epicu; zaseban ticket ako korisnik hoće.

---

## 7. Redosled rada (tickets, svaki sam za sebe isporučiv)

| ticket | šta | dokaz |
|---|---|---|
| UTIL-000 | `Curve`, `Axis`, `Consideration`, `MindAction`, `Reasoner` + testovi oblika krivulja, popravke za n, ranog izlaza, stabilnog izjednačenja | `gangsters_house_tests` nova grupa `UtilityCoreTests`; skener zabranjenih tokena pokriva `Outfit/Mind/` |
| UTIL-001 | `HouseAxes` (§4) s rečenicama; `gangsters_house_axes --house N` štampa sve ose kuće rečima | komanda + test „svaka osa 0..1 na svakom seed-u" |
| UTIL-002 | `HouseBook` (§5) i `HouseMind.Utility.cs`; šetači postaju generatori kandidata; `Brain = Tiers` i dalje podrazumevano | 21 stari test zelen na OBA mozga |
| UTIL-003 | SENKA: `Brain = Both` — tieri odlučuju, utility ocenjuje pored; `gangsters_underworld_sim --brain both --days 30` štampa tabelu neslaganja (sat, kuća, tier je rekao X, utility je rekao Y, zašto rečima) | tabela u `Docs/utility-ai-shadow.md`; svako neslaganje ili namerno ili bag u tabeli |
| UTIL-004 | FLIP: `Brain = Utility` podrazumevano; trag štampa tri najbolje (radnja, meta, ocena, rečenica); `HouseThinkRecord.Lines` nosi ocene; testovi koji su brojali pojaseve prepisani na „prva radnja je X" | 21 + novi testovi: „napad star 11 h ne zaustavlja širenje", „sef od $300 ne kupuje pištolj", „tri kolebljiva vrata ne drže kuću kod kuće" (Z2) |
| UTIL-005 | UI: kartica kuće u ledgeru (INTELLIGENCE / sto) dobija red „ZAŠTO" = najjače razmatranje rečima, po pravilu mehanika-u-rečima | vizuelno, korisnik u editoru |
| UTIL-006 | Sto (`DeskBook`) | `DiplomacyTests` zeleni; senka kao UTIL-003 na stolu |
| UTIL-007 | Ulica (`FighterBook`) + izbacivanje `UnityEngine.Random` iz odluka (ne iz FX) | `gangsters_ambush_probe`, soak `--brawl`, CrewAudit bez novih redova |
| UTIL-008 | `HouseBook.Temper` (tri ćudi kuće) | novine i sto pokazuju razliku; sim 30 d |
| UTIL-009 | Alat: `Tools/mind/curves.py` crta krivulje iz tabele u ASCII i piše `Docs/utility-ai-book.md` iz koda (generisani dokument, sveže se proverava) | dokument + freshness check u `project.py audit` |

Šta se ne radi: pešaci, kola, patrole, pritvor, sud, ledgeri (Presence, Fear, Control),
kontrola bloka, cene. Ništa od toga ne bira.

Redosled je tvrd: 000 → 001 → 002 → 003 → 004, pa 005 i 006 u bilo kom redu, 007 tek
kad je kuća stabilna nedelju dana u editoru, 008/009 kad korisnik zatraži.

---

## 8. Pre-mortem (šta može da pukne)

| rizik | znak | lek |
|---|---|---|
| kaša: kuća menja odluku svakog sata | trag pokazuje A, B, A, B | osa Commitment + prag pregaženja (nova radnja mora za 0.1 da bude bolja od one koja teče) |
| tabela nečitljiva | korisnik ne zna zašto kuća stoji | UTIL-001 komanda i UTIL-005 red ZAŠTO PRE flip-a, ne posle |
| dva mozga se razilaze, nejasno ko je u pravu | senka puna neslaganja | svako neslaganje se presuđuje ručno i upisuje u `utility-ai-shadow.md` pre UTIL-004; nema flip-a s nepresuđenim redom |
| gubitak pravila korisnika koja žive u `if`-ovima (nikad tuđa vrata, nikad dva zahteva za redom, Don bez naredbi, prag prihoda A30) | test padne ili tiho nestane | svako takvo pravilo = kapija (0/1 osa) s imenom pravila; test `TheMindNeverAsksADoorAnotherHouseHolds` i drugi ostaju kakvi jesu |
| `float` neslaganje Mono/CoreCLR | test prolazi ofline, pada u editoru | `double` svuda u reasoneru, poređenje na 1e-6, ključ lomi izjednačenje |
| ulica preskupa | frame time raste na 10k pešaka | reasoner po jedinici na 0.25 s, ose bez alokacija, `BestMark` ostaje kao brzi kandidat-generator |
| tabela u C# = korisnik mora rebuild da bi menjao ćud | sporo podešavanje | prihvaćeno (isto kao D-tabela danas); `unity command recompile` je 20 s |

---

## 9. Presude korisnika (2026-09-06)

1. Obim: SVE — um kuće, sto i ulica su u epicu (007 nije backlog, samo ide posle kuće).
2. Prva verzija tabele donosi iste odluke kao pojasevi, da bi se tjunovala od poznate
   tačke; senka pa flip.
3. Krivulje u C# tabeli kao `HouseMindConfig`, ne u editoru.
4. Jedan epic UTIL-000..009 u Linear; 008/009 backlog.
5. Biblioteka: jezgro se piše samo (v. §10).

## 10. Biblioteka ili svoje jezgro

Pregledano 2026-09-06: `ZorPastaman/UtilityAI` (MIT, čist C# Brain/Action/Consideration,
Unity omotač odvojen, linearna/kvadratna/logaritamska razmatranja, bez Markove popravke,
bez meta po radnji), `Bartvanderkruys/utility-ai` (MIT, Unity 5, MonoBehaviour),
`DreamersIncStudios/ECS-IAUS-sytstem` (MIT, DOTS/ECS, ne vadi se), `NWagter/Utility_AI_Unity`
(MIT, školski projekat, logistička i logit krivulja nedovršene), Apex Utility AI
(komercijalan, ugašen 2019). Ni jedan ne daje: `double` i stabilno izjednačenje, Markovu
popravku za n, meta po radnji, rečenicu po razmatranju, rad bez alokacija, skener
zabranjenih tokena. Jezgro koje treba je oko 300 linija (`Curve`, `Axis`,
`Consideration`, `MindAction`, `Reasoner`); četiri krivulje su javne formule iz Markove
knjige. Piše se svoje; od biblioteka se ne uzima ništa.
