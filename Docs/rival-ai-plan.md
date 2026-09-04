# Plan: um rivalskih kuća — zašto stoje, i kako da zauzimaju grad

Stanje na dan 2026-09-04, pročitano iz ŽIVE sesije u editoru (seed 1987, CoreDemo, 6
rivalskih kuća + igrač, 9.–10. dan kampanje, misao na 2 sata igre, 1 sat igre = 60 s).
Ništa u kodu nije dirano. Građa: `Docs/rival-families.md` (kako um radi danas),
GAN-244 (EPIC 25, D-tabela), `Docs/racket-collections.md`.

Izvori merenja: `gangsters_save` (rosteri, ćelije, vrata, runde, novine),
`gangsters_control_audit`, `gangsters_fear_audit`, `gangsters_rack_audit`,
`gangsters_underworld_sim --days 30` (papirni sat), Editor.log.

---

## 0. Ugovor (šta je primećeno i traženo)

Korisnikova zapažanja, 2026-09-04:

1. "pohapse im bossove"
2. "presporo reketiraju — u dan mogu da obiđu ceo blok"
3. "bag man ne izlazi"
4. "treba da osvoje što više blokova pre nego što se sudare; tu da regrutuju, spreme se
   za rat i krenu"
5. pitanje: da li misao na 2 sata ima smisla, ili treba češće
6. "ne kontrolišu ni ceo blok"

Traženo: plan kako da se um unapredi. Faza je ČITANJE — kod se ne dira.

---

## 1. Šta je izmereno (dan 9, 17:38, seed 1987)

### 1.1 Kuće

| kuća | ljudi | ekipe | vrata: plaćaju / kolebaju / odbila | blok koji vodi | knjiga naredbi (od dana) | stanje |
|---|---|---|---|---|---|---|
| 1 Falcone | 2 aktivna, 2 mrtva, 1 dezertirao | 1 — vodi je **Don** | 0 / 1 / 0 | nijedan | Guard ×2 (d1), Recruit čeka (d1) | **stoji od dana 1** |
| 2 Santoro | 10 aktivnih, 2 u ćeliji | 3 | 26 / 13 / 2 | 1 (Controlled, 64 % plaća) | Raid ×3 (d9) | runda LEAN **visi od dana 3** (5/17 vrata); nikad nije naplatila |
| 3 Lucchese | 11 aktivnih | 3 | 11 / 3 / 0 | 1 (Dominated) | prazna | 149 zahteva na 14 vrata; **tišina od dana 4** |
| 4 DeMarco | 10 aktivnih, 3 u ćeliji (kapo + 2) | 3 (jedna bez kapa) | 9 / 6 / 0 | 1 | prazna | runda SHAKEDOWN **visi od dana 2** (11/13); nikad nije naplatila |
| 5 Corvetti | 4 aktivna, 3 mrtva | 1 — vodi je **Don** | 16 / 6 / 2 | 1 (Controlled) | Guard (d2), Recruit čeka (d3) | **stoji od dana 2**; nikad nije naplatila |
| 6 Barzini | 5 aktivnih, 1 mrtav, 2 dezertirala | 2 (kapo + Donova) | 0 / 1 / 0 | nijedan | Guard ×2 (d1), Recruit čeka (d1) | **stoji od dana 1** |

Kontrola grada (50 blokova pročitano): rivali zajedno vode **5 blokova**. Odnosi između
kuća: **prazni** (ni jedan stav, ni jedna zamerka) posle 10 dana. Pažnja policije po
blokovima: 0 (poluživot 8 h). Nedeljna cena zaštite na ovim vratima: $100 (jedna $500).

Svi Donovi su živi i unutra. **Nijedan Don nije uhapšen.** Ono što korisnik vidi kao
"hapse im bossove" su KAPOI — i to zato što odlazi CELA EKIPA. Ogrlicu nosi poručnik
(`PoliceDispatch.Arrest.cs`), ali `DemoCrews.Arrest.TakeIn` upisuje SVAKOG čoveka
jedinice; kuća 4 je tako prvog dana izgubila kapoa i dva vojnika u jednom hapšenju.
(Prva verzija ovog plana je pisala "policija uvek prvo uzima vođu"; tačno je da je vođa
onaj kome se prilazi, a onda ide cela ekipa.)

### 1.2 Istorija koraka (novine, dosijei, istorija vrata)

| dan · sat | šta se desilo | posledica |
|---|---|---|
| 1 · 09:00 | ekipa kuće 6 puca na policiju kod kladionice (Salvatore's Turf Accountants); **4 policajca mrtva**, 50 hitaca | otvoren slučaj CopKilling; ekipa ubijena do 09:08 |
| 1 · 09:30 | ekipa kuće 1 puca na policiju (Nowak's Turf Accountants) | slučaj CopKilling |
| 1 · 09–24 | 40 poziva policiji (PoliceBlotter) — vlasnici prijavljuju zahteve kuća 2, 3, 5 | policija ceo dan na Heights-u |
| 1 · 14:12 | policija ubija Walta Browna + 1 (kuća 1) | kuća 1 ostaje na Donu + 1 vojniku |
| 1 · 20:42 | kapo kuće 4 Carmine Hayes + 2 vojnika uhapšeni za iznudu (The Form Book II) | jedina radna ekipa kuće 4 bez kapa |
| 2 · 04:00 | kuća 4: ekipa 4001 kreće SHAKEDOWN bloka (13 vrata) | stane na 11. vratima u 13:38 i **više se ne pomera** |
| 2 · 22:00 | kuća 2: ekipa 2000 kreće LEAN (17 vrata) | stane na 5. vratima u dan 3 06:00 i **više se ne pomera** |
| 2 · 22:48 | poručnik kuće 5 Gus Reed ubijen (3 mrtva) | kuća 5 ostaje na Donu + 1 vojniku + torbaru |
| 2–4 | kuća 3 pita ISTA 3 kolebljiva vrata svaka 2 sata: Nowak Bakery 37 zahteva, jedan tačno na svaku misao (58.7 h, 60.7, 62.7 … 89 h) | vlasnik zove policiju 4 puta; kuća 3 se nikad ne širi |
| 3 | dva vojnika kuće 2 uhapšena za iznudu | |
| 6 → 9 | zatvorenici sa dana 1 i 3 još u ćeliji: dan suđenja se svaki dan pomera za +1 (dan 6, 7, 8, 9, 10) | kapo kuće 4 u ćeliji 9 dana bez presude |
| 4 → 10 | kuća 3: nijedan kontakt sa vratima; knjiga prazna | uzrok nije u knjigama (treba trag misli) |

Papirni sat (`gangsters_underworld_sim`, 30 dana, 21 kuća) ne pokazuje ovo: tamo sve
kuće dođu do 7 vrata / 2 bloka do 14. dana i tu stanu jer je `PaperCity` mala. Papirni
merač ne meri ni jedan od uzroka ispod — svi su na ulici.

---

## 2. Uzroci (svaki sa dokazom i mestom u kodu)

### U1 — GUARD nikad ne prestaje, a Recruit čeka iza njega

`Orders.cs`: Guard je `JobResolution.Standing` — straža se stoji, nikad ne završi.
`CampaignRunner.WorkCrew` radi SAMO prvi živi posao u knjizi ekipe. Um (`HouseMind.Home`,
tier 1, i `Answer`, tier 5) upisuje Guard kad je neko pucao kod ulaza; um nema nameru
"skini stražu" (`HouseIntentKind` nema Cancel). Rezultat: ekipa je zauvek zauzeta
(`Free` = false), Recruit koji je um upisao posle Guard-a nikad ne krene, ekspanzija
(`CrewOn` traži slobodnu ekipu) je nemoguća. Kuće 1, 5, 6 su u ovome od dana 1–2.

**Dopuna (contrarian, 2026-09-04): skidanje straže po 24 h meri pogrešan sat.** `Look`
upisuje incident kao `new HouseIncident(blockId, unanswered, gameHour)` — vreme je SADA,
na svaku misao. Zato provera prozora u `Answer` (`GameHour − Since > AnswerWindowHours`)
nikad nije tačna, a `Home` uopšte nema prozor. Incident se puni protiv SVAKE kuće na
bloku gde je bilo ko pucao, policiju uključujući, postaje "neodgovoren" posle 12 h i živi
72 h (`PowerAnswerWindowHours`, `PowerMemoryHours`); straža koja samo stoji ga ne
odgovara — odgovara ga samo nasilje te kuće ili njeni čuvari koji krenu na napadača.
Zato: Guard se ne piše jednom nego se **prepisuje svake 2 sata**, i to po jedan na svaku
slobodnu ekipu (otud `Guard ×2`). Skidanje straže po isteku 24 h ne pomaže dok je izvor
72 h, a "Recruit pre Guard-a" ne pomaže dok se Guard prepisuje. Zato S1 mora da nosi
pravo vreme incidenta u pogled i da broji stražu po VRATIMA, ne po ekipi; broj se meri
prema 72/12, ne prema 24. Vidi A22.

### U2 — runda stane na putu i niko je ne pokreće ponovo (ISPRAVLJENO 2026-09-04)

**Prva verzija ovog uzroka bila je pogrešna.** Pisalo je da posao iz knjige odvede liniju
sa runde. Merenje (contrarian test): `gangsters_rack_audit` nad vratima na kojima obe
runde stoje — kuća 2 `cafe:0:W:4`, kuća 4 `spot:1:residential-07:shop:2:750:2500` —
vraća **nijedan odnos i nijednu istoriju**. Ljudi nikad nisu stigli do tih vrata;
`inTheDoor` je false u snimku. Runda nije umrla u radnji, umrla je na putu.

Mehanizam: `NextStop` (`TerritoryRuntime.Collection.cs`) na svaku stanicu pozove
`crews.MarchTo` **jednom**. Runda se pomera samo kad `NoteRoundArrival` vidi da je TAČNO
ODREĐEN čovek (`EnsureCollector`, kod rivala poručnik jer markirani torbar nema telo —
U9) došao na 14 m od vrata. Ako taj hod propadne — pathing, tuča, policija, retask — niko
ga ne ponavlja, niko ga ne meri i ništa ga ne prekida. `WatchRounds` proverava samo da
jedinica stoji; `LastMoveAt` ne čita niko osim `NotePoliceAttention`.

Posledica je ista i teža: dok runda "traje", `TerritoryRoundScheduler` ne šalje naplatu
(`roundRunning`), pa kuće 2 i 4 **nikad nisu naplatile** (`lastCollectedDay = -1` na svih
35 vrata), i `WalkTheDoors` svaku sledeću misao prekida i kreće ispočetka.

Stara hipoteza (posao iz knjige) nije isključena, ali **nije objašnjenje**: kuća 4 je za 9
dana izdala ukupno 4 posla (`nextJobId = 5`) i knjiga joj je prazna. Ostaje kao sporedan
put, ne kao uzrok.

**Popravka se menja:** primarno je čuvar + ponovni hod (A3), ne kuka u `CrewJobs.March`.

### U3 — DEFEND petlja: dva okidača, i obilazak koji svejedno pita ista vrata

`HouseMind.Defend` (tier 6) računa vrata kao "labava" ako su `Hesitant` **ili `Late`**, i
upisuje `ShakeDownBlock`. Oba okidača se sama hrane:

* **Hesitant** — `TerritoryShakedown.WorthAsking` uključuje kolebljive, pa ih obilazak
  pita svaku misao. Vlasnik na oceni 35 nikad ne pređe u plaćanje (`hesitantShare 0.35`),
  a svaki zahtev je novi poziv policiji ako je "Connected". Kolebljiva vrata nikad ne
  upišu `RefusedAt`, pa ne ulaze u `Defiances` i ostaju "pitljiva" zauvek.
* **`Late`** — vrata koja plaćaju a nisu naplaćena nedelju dana su `Late`
  (`TerritoryCollectionSchedule.IsLate`). Kod kuća 2, 4 i 5 `lastCollectedDay = -1` na
  SVIM vratima (U2), pa posle sedmog dana **svaka vrata koja plaćaju** postaju labava i
  Defend gori zauvek. A `ShakeDownBlock` to ne može da popravi: kašnjenje leči samo
  naplata, a um **nikad ne upisuje `HouseOrder.CollectDues`** (postoji u `HouseIntent`,
  nekorišćen u `HouseMind`).

Kako Defend vraća `true` pre Expand-a (tier 7), kuća sa jednim kolebljivim ili jednim
zakasnelim vratima se **nikad ne širi**. Kuća 3: 149 zahteva na 14 vrata za 4 dana.

Gde stoji hlađenje od 24 h je odluka, ne detalj: u zajedničkom `WorthAsking` menja i
igračev SHAKE DOWN THE BLOCK (preskakao bi vrata pitana pre manje od 24 h); samo u
`HouseMind.Ask` ostavlja obilazak nepromenjen. Vidi A21.

### U4 — širenje je jedna vrata po misli, a novi blok tek kad su SVA stara "sređena"

`HouseMind.Expand` → `Ask` upisuje JEDAN `ApproachBusiness` po misli (vrata sa najvećom
cenom). Na 2 sata misli to je najviše 12 vrata dnevno, i to samo kad tierovi 1–6 ćute i
ekipa je slobodna. Prelazak na susedni blok traži `Settled`: svaki blok koji kuća vodi
mora biti Controlled+ i ≥ 50 % vrata da plaća (D17) — kolebljiva i zatvorena vrata to
drže zauvek ispod praga. Um ima `HouseOrder.ShakeDownBlock` (ceo blok u jednom prolazu)
ali ga koristi samo u Defend-u, nikad za širenje.

### U5 — dan 1, 09:00: rivalske ekipe pucaju na policiju, i gube kapoe

Prva misao svake kuće šalje ekipu na vrata; kladioničari i "Connected" vlasnici zovu
policiju; par dođe i postavi pitanje; odgovor ekipe je `SurrenderRoll.FightChance`
(hrabrost kapoa 0.5 + temper 0.3 − lojalnost 0.2, pod 0.05–0.95). Tri naoružana čoveka
(mašinka/sačmara iz `Underworld.ArmTheFamily`) protiv para → kuća 6 ubija 4 policajca,
policija onda ubija ekipe kuća 6 i 1 istog prepodneva, i poručnika kuće 5 sledeće noći.
Um posle toga vidi pretnju kod ulaza → Guard → U1. Um ne zna da "sakrije" tražene ljude
(`hidingSince` postoji za igrača), ne plaća kauciju, nema advokata.

### U6 — Don vodi ekipu i dobija naredbe

Donov odred (ekipa čiji je `LieutenantId == BossId`) je u `Roster.Crews` kao svaka druga;
`HouseMind.CrewOn` ga bira kad je slobodan i ima ≥ 2 aktivna. Kad kapoi izginu, kuća
ostaje SAMO na Donovom odredu (kuće 1 i 5) i um mu upisuje Guard/Recruit — a odred je po
D4 unutra (`DemoCrews.TakeTheDonInside`). Nema pravila "Don ne izlazi na posao" niti
"kad nema kapa, napravi ga" (um nikad ne upisuje `Promote`).

### U7 — sud ne zaseda: dan suđenja se pomera za +1 svaki dan

Zatvorenici uzeti dana 1 (kapo kuće 4) i dana 3 još su `Held` na dan 9 sa
`CourtDay = 10`. Pravilo je `DaysToCourt = 5` (dan 6); jedini kod koji piše
`CourtDay = today + 1` je `PrisonPipeline.BackToTheCells` — transfer nije krenuo, "čovek
čeka sutra". Log: `[Core] no parcel downtown will hold a courthouse; transfers drive out
of town on both legs`. Verovatan uzrok: nema slobodnih kola za konvoj (kuća 6 je dana 1
ubila 4 policajca). Ovo pogađa i igračeve ljude; rival bez kaucije i advokata ga ne
može zaobići. Uzrok na strani zakona — treba potvrda u `PoliceDispatch` transfer logici.

### U8 — nema rasta, nema faze, nema razloga za rat

`Replace` (tier 3) regrutuje samo do `MinHoods = 2` po ekipi. `Buy` (tier 8) tek posle 3
tihe misli. `Promote` (nova ekipa) se nikad ne upisuje. Nema pojma "faza" — zemlja /
ljudi / rat. Zamerke (D14) nastaju samo iz napada na vrata, preuzetih vrata, izgubljenih
rundi, ubijenih ljudi i neplaćene tribute; dve kuće koje se sretnu na granici i ne pipnu
jedna drugu ostaju u miru zauvek — posle 10 dana knjiga odnosa je prazna.

### U11 — tišina kuće 3 je TREĆI mehanizam, i još nije objašnjen

Kuća 3 nema nijednu rundu u knjizi rundi, izdala je **jedan jedini posao za 9 dana**
(`nextJobId = 2`), knjiga joj je prazna, sve tri ekipe su joj slobodne i 11 ljudi je
aktivno — a ipak nije dodirnula nijedna vrata od sata 89 (dan 4). Misli i dalje (rota je
uredna). Dakle nije ni U1 (nema Standing posla), ni U2 (nema runde), ni hapšenje.

Kandidati koje snimak ne razrešava: `WalkTheDoors` odbija obilazak ("every door here has
answered us" / "The physical crew refused the walk"), ekipa je fizički zaglavljena, ili
`Defend`/`Expand` ne nalaze kandidata iz razloga koji se vidi samo u pogledu. **Ovo je
razlog zašto AI-000 ide prvi** — bez traga misli i odbijanja se ne razlikuju.

### U9 — torbar rivala nema telo

`RosterOps.SetDuty(Collector)` izvlači vojnika iz `HoodIds` u `Crew.BagId`; ulica deli
liniju iz `HoodIds`, a odred torbe samo za igrača. Um (tier 4 `Collect`) rivalima
markira torbara → ta kuća ima jednog vojnika MANJE na ulici, i nema torbara koji se vidi.
Rundu hoda linija (kad je slobodna i kad ništa ne visi — U2). Naplata je nedeljna po
bloku (`TerritoryCollectionSchedule.DayOf`), pa se i kad radi vidi jednom nedeljno.

### U10 — misao na 2 sata (odgovor na pitanje 5)

Kadenca nije uzrok ničega gore. Ona određuje samo tempo "jedna vrata po misli" (U4). Kad
širenje pređe na naredbe za ceo blok, 2 h i 4 h daju isto — razlika je koliko brzo kuća
REAGUJE (napad na vrata je ionako `WakeNow`). Predlog: ostaviti 2 h dok se ne izmeri
posle popravki; vratiti na 4 h (D7) ako merenje pokaže da je svejedno.

---

## 3. Predlog — šta menjamo, po fazama kako ih je korisnik opisao

### 3.1 Sanacija (bez ovoga ništa drugo ne radi)

| # | popravka | gde | uzrok |
|---|---|---|---|
| S1 | Guard ima kraj I ne prepisuje se: incident nosi PRAVO vreme u pogled (`power` ledger ima `At`), `Home` dobija prozor kao `Answer`, straža se broji po VRATIMA a ne po ekipi (`Filed` po `TargetBusinessId`), i um je skida (`Cancel`) kad prozor istekne. Broj se meri prema 72 h pamćenja / 12 h odgovora, ne prema 24 (A22) | `TerritoryRuntime.Minds.Look`, `HouseMind.Home/Answer`, nova namera `Cancel` | U1 |
| S2 | **Runda ima čuvara i ponovni hod**: noga koja ne stigne se ponovo maršira, pa posle `RoundStallHours` (A3 = 2 h bez pomaka) prekida. Kuka u `CrewJobs.March` (posao prekida rundu) ostaje kao sporedna, ne kao glavna popravka — merenje je pokazalo da runde umiru NA PUTU, ne od posla | `TerritoryRuntime.Collection.NextStop/WatchRounds`, `CrewJobs` | U2 |
| S3 | Donov odred nije kandidat za naredbe; sme samo Guard na svom ulazu | `HouseMind.CrewOn` (preskoči ekipu čiji je poručnik Boss) | U6 |
| S3b | S3 mora da pokrije i `CrewFor` (tier 4), ne samo `CrewOn`: `CrewFor` pada na `crews[0]` i tako i dalje markira torbara u Donovom odredu i daje Donu blok na papir | `HouseMind.CrewFor` | U6 |
| S4 | Kad kuća ostane bez kapa a ima ≥ 2 vojnika: um unapređuje najboljeg (`Promote`) i pravi ekipu; ako nema vojnika: Recruit sa Donovog odreda (jedini izuzetak od S3). `Promote` ume da bude odbijen (`LieutenantCap`) — bez P4 se predlaže zauvek | `HouseMind.Replace` | U6, U8 |
| S5 | Defend gubi OBA okidača: ne broji `Hesitant` kao labavo, a na `Late` upisuje **naplatu** (`HouseOrder.CollectDues`) umesto obilaska — jer kašnjenje leči samo naplata. Hlađenje od 24 h po vratima traži novo polje na `HouseDoor` (`LastInteraction`/`Demands` iz reda odnosa, već se čuvaju); gde hlađenje stoji je odluka A21 | `HouseMind.Defend/Ask`, `HouseView.HouseDoor`, `TerritoryRuntime.Minds.Doors` | U3 |
| S6 | Sud: transfer koji nije krenuo N dana zaredom ide bez konvoja (papirno) ili se sudi u stanici — ODLUKA korisnika; tiket zakona, ne uma | `PrisonPipeline`, `PoliceDispatch` transfer | U7 |
| S7 | Ekipa koja hoda rundu (shakedown, lean, naplata) NIJE slobodna za um: `Free` čita i knjigu rundi, ne samo knjigu poslova; `WalkTheDoors` odbija drugi obilazak dok runda hoda (kao što `CollectDues` već odbija "a round is already out") umesto da je prekine i krene ispočetka. Ovo je mehanizam iza "ista vrata na svaku misao" (U3) i uslov za bržu misao (A17) | `HouseMind.Free`, `HouseView` (RoundRunning), `TerritoryRuntime.Shakedown.WalkTheDoors` | U3, U10 |

### 3.2 Faza ZEMLJA — "u dan obiđu ceo blok"

| # | pravilo | gde |
|---|---|---|
| Z1 | Širenje po BLOKU: na bloku gde kuća stoji (`OurPresence ≥ DemandPresence`) um upisuje `ShakeDownBlock` — ceo blok u jednom prolazu, sva vrata koja niko ne drži; ne `ApproachBusiness` jedna po jedna | `HouseMind.Expand` |
| Z2 | Novi blok kad na svojim blokovima NEMA vrata koja se još mogu pitati (ni Unaffiliated ni Approached u roku) — ne kad su "sređeni" na 50 %. `Contested` blok i dalje drži kuću kod kuće | `HouseMind.Settled` → `NothingLeftToAsk` |
| Z3 | Prag `Score > 0` ostaje, ali se vrata na susednom bloku broje po ceni × broju (danas $100 po vratima, hop $100) — proveriti posle Z1 da li ijedan sused prolazi; ako ne, D8 brojevi u tabelu | `HouseMind.Score` |
| Z4 | Kolebljiva vrata: posle 24 h jedna pretnja (Ladder), pa se ostave dok ne dođe rat; ne pitaju se ponovo | `HouseMind.Ladder`, `CollectDefiances` |
| Z5 | Torbar rivala: ili rival dobija odred torbe kao igrač (isti `BagUnitOf`, jedan skup pravila — vidi CLAUDE.md "central systems"), ili um rivalima NE markira torbara i rundu hoda linija — ODLUKA | `DemoCrews.BagUnitOf`, `HouseMind.Collect` |

### 3.3 Faza LJUDI — regrutacija i naoružanje pre sudara

| # | pravilo | gde |
|---|---|---|
| L1 | Cilj veličine: kuća drži `1 ekipa po bloku koji vodi + 1`; ekipa je puna na `HoodsPerCrew` (predlog 4). Regrutuje dok je ispod cilja i dok rezerva (D9, 7 dana plata) dozvoljava | `HouseMind.Replace` → `Grow` |
| L2 | Nova ekipa: kad ima ≥ 2 bloka i ≥ `HoodsPerCrew + 2` vojnika, unapredi najboljeg (Leadership) u poručnika; nova ekipa dobija sledeći blok | `HouseMind`, `HouseOps.Promote` |
| L3 | Naoružanje ide pre nego što se čeka "tiho" — kad je faza LJUDI, `QuietThinks` se ne traži; prvo pištolj svakom, pa auto svakoj ekipi | `HouseMind.Buy` |
| L4 | Rezerva za rat: kuća ne ulazi u sudar dok `Endurance < MinWarDays` (14) — već postoji (D15), samo faza treba da ga čita | `HouseRelations.Endurance` |

### 3.4 Faza SUDAR — kad nema više slobodnih blokova

| # | pravilo | gde |
|---|---|---|
| R1 | Kuća zna da je "na granici": nijedan sused nije `Open` a susedni blok vodi druga kuća. Tada prelazi u LJUDI (L1–L3) dok nije puna i naoružana | `HouseMind` faza |
| R2 | Kad je puna: ide na TUĐA vrata na graničnom bloku (`Retake`-ov `Demand` na vrata koja štiti slabija kuća — `TheirEndurance` manji od našeg). To je danas dozvoljeno tek na lestvici `RetakeBusiness` (zamerka 40) — ODLUKA: da li granica sama po sebi daje zamerku, ili sudar počinje samo preko postojeće lestvice | `HouseMind.Feud/Retake`, `HouseRelations` D14 |
| R3 | Rat se objavljuje po D15 kako je (zamerka ≥ 60 + Endurance ≥ 14 dana + jači od njih); slabiji nudi primirje | postoji |
| R4 | Policija u sudaru: kuća koja ima ljude u ćeliji/traženih ne ulazi u sudar dok ih ne izvuče (kaucija) — vidi P1 | |

### 3.5 Zakon — da hapšenje ne bude smrt kuće

| # | pravilo | gde |
|---|---|---|
| P1 | Um uzima ADVOKATA pa plaća kauciju — tim redom, jer bez advokata kaucija nije ni moguća (`PrisonPipeline.BailRefusal` traži `Lawyer.BailSkill`, a `RosterSeeder` rivalima ne deli advokata). Za `CopKilling` kaucije nema uopšte (`Sentencing.Bail ≤ 0`) — kuće 1 i 6 se izvlače samo kroz S6 | `HouseMind` nove namere `Retain` + `Bail`, `HouseOps` |
| P4 | **Odbijena namera se ne predlaže ponovo N misli.** `HouseView.LastRefusals` postoji i puni se, ali ga `HouseMind` NIKAD ne čita — svaka nemoguća namera (kaucija bez advokata, `Promote` preko raspona) se predlaže svake 2 sata zauvek | `HouseMind`, `HouseView.LastRefusals` |
| P2 | ~~Odgovor policiji po kontekstu~~ — ODBIJENO (A15): odgovor ostaje na mentalitetu poručnika, `SurrenderRoll` kako je | — |
| P3 | Traženi rival ide unutra i sedi (`hidingSince`) kao igračev; um ne šalje traženog na vrata | `HouseMind`, `PoliceDispatch.Wanted` |

---

## 4. D-tabela odluka (odlučeno 2026-09-04)

| # | pitanje | predlog | odluka korisnika | šta to znači za kod |
|---|---|---|---|---|
| A1 | Guard traje dok pretnja pamti (24 h) pa se skida sam? | da | **da** | S1 kako je |
| A2 | Runda linije: prekida je posao iz knjige, ili posao čeka rundu? | prekida | **prekida** | S2 kako je |
| A3 | Čuvar rundi bez pomaka: posle koliko sati se prekida? | 6 h | **2 h** | `RoundStallHours = 2` u `HouseMindConfig`; runda u kojoj se ništa nije pomerilo 2 sata igre se prekida |
| A4 | Donov odred ne dobija naredbe osim Guard na svom ulazu? | da | **"Don nek sedi u kuću uglavnom"** | Donov odred ne dobija NIJEDNU naredbu uma; izlazi samo kad ulica sama brani ulaz (postojeće pravilo odbrane ulaza). Straža na ulazu nije Donov posao nego kapoov |
| A5 | Kuća bez kapa: unapredi vojnika, ili regrutuje sa Donovog odreda? | unapredi ako ≥ 2 | **unapredi kapa** | uvek `Promote` najboljeg vojnika po Leadership-u; regrutacija tek kad nema NIJEDNOG vojnika (nema koga da unapredi) — pretpostavka, ne odluka |
| A6 | Širenje po bloku: ShakeDownBlock umesto jednih vrata po misli? | da | **da** | Z1 |
| A7 | Novi blok kad "nema šta da se pita" umesto 50 % plaćanja? | da | **da** | Z2 |
| A8 | Kolebljiva vrata: jedna pretnja posle 24 h pa mir do rata? | da | **da** | Z4, S5 |
| A9 | Torbar rivala: odred kao igraču, ili linija bez markiranog torbara? | odred | **odred** | `BagUnitOf` za svaku kuću; rivalski torbar dobija telo i billet u svom ulazu; meriti perf sa 6 kuća |
| A10 | Veličina ekipe do koje se regrutuje? | 4 | **za sad do 4** | `HoodsPerCrew = 4`, broj u tabeli |
| A11 | Cilj: 1 ekipa po bloku + 1? | da | **da** | L1 |
| A12 | Naoružanje bez čekanja tihih misli u fazi LJUDI? | da | **da** | L3 |
| A13 | Granica daje zamerku sama po sebi? | ne | **i sama granica** | NOVI izvor zamerke D14 `BorderPressure`: kuća na granici (R1) svakog dana beleži zamerku prema susedu koji vodi susedni blok; broj u A18 |
| A14 | Rivali plaćaju kauciju? advokata? | kaucija da, advokat ne | **i jedno i drugo** | P1: um plaća kauciju (`PostBail`) i uzima advokata (isti `Counsel` cenovnik) kad rezerva dozvoli |
| A15 | Ekipa na ZAHTEVU ide policiji mirno? | da | **zavisi od mentaliteta poručnika** | P2 se NE radi: `SurrenderRoll` ostaje kako je (hrabrost, temper, lojalnost poručnika odlučuju). Posledica: gubici kapoa prvog dana ostaju deo igre; ublažavaju ih P1, P3, S4 |
| A16 | Sud koji ne zaseda: papirni transfer posle N dana, ili suđenje u stanici? | papirni posle 2 dana | **papirni posle 2 dana** | S6: `TransferFailsBeforePaper = 2` |
| A17 | Misao: 2 h ili 4 h? | 2 h | **"što ne i brže?"** | može: um se poziva svaki frejm (rota), trošak po misli se meri (`ThinkMilliseconds`). USLOV je S7 — inače brža misao samo češće restartuje obilazak. Broj u A19 |

### 4.1 Nova pitanja iz odluka

| # | pitanje | predlog | odluka |
|---|---|---|---|
| A18 | Koliko zamerke daje granica po danu (`BorderPressurePerDay`)? Lestvica: upozorenje 10, pretnja 20, račun 30, uzimanje vrata 40, napad na kolektora 50, napad na radnju 60 | 5 na dan → upozorenje 2. dana granice, uzimanje vrata 8. dana, rat moguć od 12. dana (uz D15: 14 dana plata i jači) | |
| A19 | Koliko brzo misli (`ThinkEveryHours`)? | 1 h; pola sata tek ako merenje AI-008 pokaže da 1 h negde kasni | |
| A20 | Kad Donov odred ipak izlazi: samo kad je rival fizički na bloku ulaza (postojeće), ili nikad? | samo odbrana ulaza, kako ulica već radi | |

### 4.2 Pitanja iz contrarian pregleda (2026-09-04)

| # | pitanje | predlog | odluka |
|---|---|---|---|
| A21 | Hlađenje "ista vrata ne pitaj 24 h": u zajedničkom `WorthAsking` (menja i tvoj SHAKE DOWN THE BLOCK — preskakao bi vrata pitana skoro) ili samo u umu? | samo u umu; tvoj obilazak ostaje kakav je | |
| A22 | Straža: koliko posle poslednjeg incidenta na bloku se skida? Izvor pamti 72 h, "neodgovoren" postaje posle 12 h | 24 h posle POSLEDNJEG incidenta (ne posle prvog), i jedna straža po vratima | |
| A23 | Kad su vrata `Late` a nema runde: um šalje naplatu sam, ili čeka nedeljni raspored? | šalje sam (inače Defend gori do sledeće nedelje) | |
| A24 | Odbijena namera se ne ponavlja koliko misli? | 6 misli (12 h na kadenci od 2 h) | |

---

## 5. Tiketi (redom; svaki se meri pre sledećeg)

| tiket | šta | meri se |
|---|---|---|
| AI-000 | **Trag misli u običnom Play-u**: `gangsters_house_probe` — po kući: tier, namera, **odbijanje**, poslovi (i oni bez ekipe), runde (stanica, `LastMoveAt`, `InTheDoor`, gde je odred fizički), i **po jedinici** `Surrendered / InCustody / Billeted / Dispatched`; plus istorija poslednjih 50 misli po kući u memoriji (ne samo harness `DriveTrace`) | bez ovoga se U11 (tišina kuće 3) ne može objasniti, ni razlikovati zaglavljen hod od zarobljene ekipe |
| AI-001 | S1 + S3 + S4 (Guard ima kraj; Don ne izlazi; kuća bez kapa pravi kapa) | nijedna kuća sa Standing poslom starijim od 24 h; nijedna ekipa koju vodi Don sa poslom |
| AI-002 | S2 + S7 (posao prekida rundu; čuvar rundi na 2 h; runda koja hoda je zauzeta ekipa) | nijedna runda starija od 2 h bez pomaka; nijedan obilazak restartovan pre kraja; svaka kuća sa vratima koja plaćaju naplati bar jednom nedeljno |
| AI-003 | S5 + Z1 + Z2 + Z4 (širenje po bloku, Defend bez petlje) | do dana 7 svaka kuća vodi ≥ 1 blok sa svim vratima pitanim; do dana 14 ≥ 2 bloka; zahteva po vratima ≤ 3 |
| AI-004 | L1–L3 (rast i naoružanje) | do dana 14 svaka kuća ima ≥ 2 ekipe pune po A10 i sve sa pištoljem |
| AI-005 | P1 + P3 (kaucija i advokat, skrivanje) | kapo u ćeliji ≤ 3 dana kad kuća ima novac |
| AI-006 | S6 (sud koji ne zaseda; papirni transfer posle 2 propala dana) — tiket ZAKONA, isto pogađa igrača | nijedan zatvorenik `Held` posle `CourtDay + 2` |
| AI-007 | R1–R3 + granica kao zamerka (A13, broj iz A18) | prvi rat u gradu posle dana 14, ne pre; nijedan pre nego što su obe kuće pune |
| AI-008 | Merač: `gangsters_underworld_sim` sa većim `PaperCity` (≥ 4 bloka po kući) + živ harness od 14 dana koji pišе tabelu iz §1.1 po danu; tally od 30 seedova; tu se meri i brža misao (A19) | tabela §1.1 na dan 14 bez ijednog "stoji" |

Ne radi se: P2 (odgovor policiji) — korisnik je odlučio (A15) da odgovor ostaje na
mentalitetu poručnika, kako `SurrenderRoll` već radi.

---

## 6. Šta NE menjamo

- Isti kod za igrača i rivale (D1): svaka popravka ide kroz `HouseMind` + gateway; ništa
  "samo za rivale" u `RoadDemo` osim projekcije torbe (A9).
- D13–D15 (stav, zamerke, izdržljivost) ostaju; sudar ih koristi, ne zamenjuje.
- Nedeljna naplata po bloku ostaje — problem je bio što se NIKAD nije desila, ne što je
  retka.
- Brojevi (`HouseMindConfig`) ostaju na jednom mestu; novi (`HoodsPerCrew`,
  `RoundStallHours`, `CrewsPerBlock`) idu tamo i u D-tabelu GAN-244.

---

## 7. Contrarian pregled (2026-09-04) — presuda i ostalo

Presuda: **§3.1 traži prepravku**, ostalo stoji. Dijagnoza i arhitektura su dobre, ali je
sanacija — sloj od koga sve zavisi — imala dve otvorene petlje (S1 meri pogrešan sat, S5
zatvara samo jedan od dva okidača), jednu popravku uperenu u pogrešan šav (S2), i jedan
odgovor iz D-tabele koji kod ne može da ispuni (A14 bez advokata). Sve četiri su gore
prepravljene. Test koji je to rešio: `gangsters_rack_audit` nad vratima na kojima runde
stoje — prazno, dakle ljudi nikad nisu stigli.

Ostali nalazi, po težini:

| # | nalaz | šta radimo |
|---|---|---|
| C1 | **Z1 gura sve pozive policiji na jedan blok.** Obilazak celog bloka znači da svaka vrata mogu da zovu policiju u istom satu; ni `Defend` ni `Expand` ne čitaju `PoliceAttention` za blok na kome stoje (samo `Score` za suseda). Danas je pažnja 0, pa je ovo posledica Z1, ne postojeći problem | obilazak se propušta kroz `PoliceAttention` bloka, broj u tabelu; "hapšenja po kući po danu" postaje kolona merenja AI-003 |
| C2 | **A9 opcija 1 nije besplatna.** Rivalski odred torbe ide kroz putanju sa rokovima u STVARNOM vremenu (`Time.time`), koja se pod harness satom ponaša drugačije nego u Play-u; usamljen torbar je i najlakši `RoundLost` u gradu (zamerka 20, dve izgubljene runde = korak `RetakeBusiness`) | rokovi se prevode u sate igre pre AI-008; "izgubljene runde po danu" ulazi u merenje |
| C3 | **Redosled tiketa:** L3 čeka fazu koju definiše tek AI-007; kuće 1 i 6 ne mogu da dostignu meru AI-003 pre zakona (nemaju nijedna vrata koja plaćaju i imaju `CopKilling` bez kaucije) | faza se čita kao izvedeno stanje već u AI-004; mera AI-003 se deli na "kuće koje su imale ekipu 1. dana" i "kuće koje se oporavljaju"; AI-005/006 mogu pre AI-003 |
| C4 | **A10 = 4 nije slobodan broj** — `Crew.MaxTacticalHoods = 4` je taman toliko tela koliko ulica deli; svaki peti čovek je čovek bez tela | broj se vezuje za tu konstantu, ne prepisuje |
| C5 | **Faza se ne sme čuvati.** `QuietThinks` se već ne snima; "faza" ili "straža od kada" bi posle učitavanja bili prazni | faza se izvodi iz pogleda na svaku misao |
| C6 | **Poslovi bez ekipe.** `Guard ×2` kod kuće sa jednom ekipom znači posao čiji `CrewId` više nema ekipu; `CurrentFor` ga nikad ne radi | AI-000 ih ispisuje, inače mera AI-001 pada na knjigovodstvu |

Gde se ne slažem sa pregledom: predlog da se `Late` izbaci iz `Defend` je pola rešenja —
zakašnjela vrata JESU problem kuće, samo je odgovor naplata a ne obilazak (A23). I nalaz
da je A17 (brža misao) suvišan stoji tek posle S7: dok obilazak može da se restartuje,
brža misao aktivno šteti.
