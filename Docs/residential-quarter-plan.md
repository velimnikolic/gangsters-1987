# Plan: rezidencijalni kvartovi oko jezgra (grad v3, korak 3)

> Predlog 2026-08-27; **odluke korisnika uzete istog dana** (§8). Nadovezuje se na
> `Docs/core-district-plan.md` (jezgro), `Docs/park-plan.md` §4 (pojas), `Docs/river-plan.md`
> (reka) i `Docs/residential-blocks-plan.md` §3 (gde blok stoji u gradu). Kod na koji se
> oslanja: `CoreLayout.Roll/Arrange` (deoba + presuda), `CoreRoads` (raster), `ResidentialLot`
> + `ResidentialBlocks` (blok), `ParkWalk` + `ParkBlocks` (park), `CoreDistrict` (kvart u sceni).
>
> U jednoj rečenici: **CoreDemo dobija treći sloj** — jezgro → pojas parka **na tri strane** →
> **rezidencijalna mreža** — i sve to ostaje **jedan deal, jedan raster, jedan lane graf**:
> rezidencijalni blok je još jedan elastičan blok (`res-`, kao `park-` i `quay-`), pa ulice,
> raskrsnice, saobraćaj i presudu dobija besplatno od mašinerije koja već stoji jezgro. Mreža je
> **pravilna**, a **park se nikad ne ponavlja**: najviše jedna linija parka zaredom.

---

## 0. Šta je slika pokazala (seed 1987, crtež odozgo, 2026-08-27)

Jezgro je imalo **7 parkova**, od kojih su samo 3 dealt (`park-01` u redu, `park-02` pojas jug
110 m, `park-03` pojas sever 40 m). Ostala **4 su „dopuna" kratkih redova** na zapadu
(`CoreLayout.Roll`: `shortfall > LotMax → Park`): jedan red overshoot-uje na 99 ćelija = 495 m,
pa je svaki drugi red kraći za 40–60 ćelija i taj manjak je bio park. Na zapadu su i sever i jug
završavali sa **tri parka jedan iznad drugog** (park reda + park reda + pojas).

**Pravilo korisnika, 2026-08-27:** *„ne smeju tri linije parka uzastopno, max 1"*.

Uzroci: (1) dopuna kratkih redova parkom; (2) nema pravila „park preko puta parka" *između*
redova — postoji samo *u* redu; (3) pojas je i sam linija parka i niko ga nije brojao.

---

## 1. Pravila

### 1.1 Jedan deal, jedan raster

Rezidencijalni kvart **ne dobija sopstveni `IDistrict`**. Ulazi u `CoreLayout.Plan` kao lista
blokova sa prefiksom `res-` (pun pravougaonik W×D ćelija; trotoar-prsten od 1 ćelije je unutar
bloka, kao kod parka), pa ih `WithGround` daje čitaču puteva. Time:

- **ulice i raskrsnice** čita `CoreRoads.Build` iz geometrije (nema drugog čitača),
- **lane graf** pravi `RasterGraph.Build` iz istog rastera (jedan graf, jedan saobraćaj),
- **presuda** je ista: `Faults == 0` ili sledeći deal (`Deals = 80`),
- **CoreSim** meri 30 seedova bez editora, kao i do sada.

`CoreDistrict` ostaje kvart koji `CoreDemo.unity` stoji; dodaje mu se `StandResidential()` uz
`StandParks()` i `StandQuays()`. Scena i komanda ostaju (`gangsters_core`).

Opcija (b) iz `residential-blocks-plan.md` §3 — sopstvena deoba `ResidentialLayout.Roll` kao
`IndustrialLayout` — se **ne uzima**: mreža mora da se šije na ulice jezgra i pojasa, a to je
tačno ono što jedan raster daje besplatno, a dva rastera moraju da pogađaju.

### 1.2 Zapadni krajevi redova: rezidencijalno i parking, nikad park

**Odluka 1 (korisnik: „ne")**: redovi jezgra se **ne** izjednačavaju best-fit deobom. Ostaje
mehanizam kakav jeste — svaki red se dopuni do istog raspona, pa je zapadna ivica jezgra i
dalje prava linija — menja se samo **čime** se dopunjuje:

| manjak reda | pre | sada |
|---|---|---|
| ≤ `LotMax` (12 ćelija = 60 m) | parking | **parking** (bez promene) |
| > `LotMax` | **park** | **rezidencijalni blokovi** (`res-`), razdvojeni ulicama od 3 ćelije; ostatak ≤ `LotMax` = parking |
| dubina reda ne nosi nijednu klasu (`Classify` = null, red od 5 ćelija = 25 m) | park | **park** (jedino što stane u tako plitku traku) — ali sada ga `ParkRuns` sudi |

Širine komada se biraju tako da `ResidentialLot.Classify(W−2, D−2)` vrati klasu — dubina reda
je data, pa dubina odlučuje šta može da stane (red od 13 ćelija nosi `block` 12–17 širine, od
6 nosi `row` položen po dužini). Podela je **tačna** (dinamičko programiranje preko dozvoljenih
širina), a rezovi se **poravnavaju sa ulicama reda preko puta**: ulica ili gleda ulicu u oči ili
je ceo blok od nje, jer raster spaja dve raskrsnice na 1–2 ćelije u jednu i to je greška. Ono
što podela ne popuni tačno (najviše 4 ćelije — koliko čitač puteva gleda kroz sopstveni parking)
je **parking na spoljnom kraju**.

Time zapadni rub jezgra postaje prelaz: parking i mali rezidencijalni blokovi uz downtown, pa
pojas, pa mreža. To je i slika 1987 („parking krater" na ivici centra, `park-plan` §4).

### 1.3 Park: najviše jedna linija zaredom

Pravilo u deal-u, ne u kompozeru: **park nikad ne stoji preko ulice od drugog parka** — ni u
redu (već postoji), ni **između redova** (novo), ni u mreži (novo). **Pojas se broji kao park.**
Posledice, izričito:

1. **Poslednji red jezgra na strani pojasa ne nosi park** (gledao bi pojas preko ulice iza reda).
2. **Prvi prsten mreže uz park drive ne nosi park** (isti razlog).
3. U mreži: park u ćeliji nema park ni preko jedne od 4 ulice.
4. Zapadne dopune: nikad park (§1.2).

Presuda meri **`ParkRuns`** — najduži niz parkova preko ulica — i traži `1`. Meri se ono što se
broji, ne izvodi iz nečeg drugog (pouka iz `park-generator-lessons`).

### 1.4 Pojas na tri strane, i kapije kroz kopneni pojas — **URAĐENO 2026-08-27**

**Odluka 2 (korisnik: „pojas na 3 strane")**: pojas ide **sever, jug i zapad**; istok je reka.
Jedna od tri strane je **veliki gradski park** (15–22 ćelije = 75–110 m), druge dve su obična
zelena traka (6–8 ćelija = 30–40 m). Sever i jug su i dalje redovi kao svaki drugi; **zapad je
traka pune visine jezgra**, iza zapadne ivične ulice, i sa **park drive-om** (ulica 15 m) iza
sebe. Na uglovima se trake spajaju u jedan **U** (bez ulice između: `ParkWalk.Rim.Party`).

**Kapije kroz zapadni pojas (izvedena odluka, traži potvrdu ili ćutanje).** Sever i jug ne
prekidaju nijednu ulicu — kroz jezgro ne prolazi nijedna N–S ulica preko cele visine. Zapadni
pojas prekida **sve** E–W ulice redova; ako se ne otvori, u jezgro se ulazi samo bulevarom sa
zapada i quay street-om sa istoka, i saobraćaj se davi na jednom ulazu. Zato:

- **bulevar prolazi kroz zapadni pojas** (kao što prolazi preko reke) — on je glavna ulica i
  arterija W kvarta;
- **jedna do dve ulice reda** takođe prolaze, biraju se kao mostovi na reci (isti mehanizam:
  najmanje 12 ćelija razmaka, ne uz sam kraj trake).

Zapadni pojas time nije jedan komad nego 3–4 duga komada (po 100 m+), što je i dalje daleko od
„3-4 mala parka" koje je korisnik odbio 2026-08-26 — ali jeste odstupanje od „jedan neprekidan
park" i zato stoji ovde crno na belo.

### 1.5 Grad je 3×3 ćelije veličine jezgra — **URAĐENO 2026-08-27**

**Odluka korisnika 2026-08-27**: *„treba 5 residental kvartova velicine core koji okruzuju ovo.
oni treba da idu posle parka."* Grad je zato **tri sa tri ćelije, svaka veličine jezgra sa
pojasom**: jezgro je srednja, reka uzima celu kolonu sa svoje strane, i ostaje **tačno pet**
ćelija za kvartove — tri niz kopnenu kolonu i po jedna iznad i ispod jezgra:

```
NW |  N  | reka
---+-----+------
 W | CORE| reka
---+-----+------
SW |  S  | reka
```

Kvart je **rešetka rezidencijalnih blokova** (ne poseban `IDistrict`): blokovi ulaze u isti
`Plan`, pa ulice, raskrsnice, lane graf i presudu daje ista mašinerija. Podela je ista za
kvartove koji dele ivicu, pa se ulice nastavljaju preko granice: to je kičma grida.

**Ali kvart nije pravilan grid** (korisnik 2026-08-27: *„ne treba ceo kvart da su jedan te isti
blokovi. treba randomnes u ulicama nesto izmedju kako core layoutuje ulice i blokove i ovog grid
sistema"*):

- **Širine kolona su nepravilne** (12–17 ćelija = 60–85 m), izdeljene pa **prodrmane** — ne sve
  iste, ali sve u meri koju recept prima.
- **Dubina se deli na fine trake** (6–8 ćelija = 30–40 m), i **svaka kolona uzima svoje**: jednu
  traku (terasni red, plitak blok) ili dve spojene (običan blok 75–95 m). Zato poprečna ulica
  negde prolazi kroz ceo kvart, a negde staje — linije su sve tu, ali nije svaka ulica cela.
- Gde ništa ne deli tačno, ivica kvarta dobija **traku parkinga** (20–75 m), ne rupu.
- **Bulevar jezgra ide do ivice grada**: prolazi kroz pojas kao kapija, kroz srednji kvart
  kopnene kolone (koji se zato deli na deo ispod i deo iznad bulevara) i staje na graničnoj
  ulici; na drugu stranu ide preko mosta.

### 1.5b Mreža: podela osa (originalni plan, sada opisuje izvedeno)

Rezidencijalno stoji spolja, iza park drive-a, sa tri strane:

| kvart | uz šta stoji | arterija (radnje gledaju u nju) |
|---|---|---|
| **N** | park drive sever | park drive |
| **S** | park drive jug | park drive |
| **W** | park drive zapad | **bulevar**, produžen kroz W kvart do granične ulice |

- **Podela u x (N–S ulice)** je **jedna za ceo grad**: korak `Wp` ćelija po seedu iz klase
  `block` (spoljno 12–17 ćelija = 60–85 m). Kolone kreću od zapadne granične ulice. To je ono
  što odozgo čita kao **grid**: N–S ulice N, S i W kvarta na istim linijama.
- **Podela u z (E–W ulice)**: N i S kvart broje redove **od park drive-a napolje**, dubina `Dp`
  iz klase `block` (spoljno 13–21 = 65–105 m), `Rings` redova, pa granična ulica. Uz park drive
  se ništa ne seče — prvi prsten je uvek pun blok.
- **W kvart u z**: između dva park drive-a, oko produženog bulevara (7 ćelija). Dubine u koloni
  se rešavaju da stanu tačno (`block` 13–21, `corner` 7–10, `row` ≥ 12 × 6–8, ulica 3).
- **Ostaci** (raspon nije umnožak koraka): **parking ili park** — odluka 5 — park samo ako ne
  gleda drugi park (§1.3) i ako uđe u kvotu (§1.7); ispod 6 ćelija uvek parking.
- Ulice **15 m** svuda (3 ćelije); uličice 5 m ostaju *unutar* bloka (`ResidentialLot.Alley`),
  nikad kao ulica mreže.
- **Uglovi** (NW, SW) pripadaju N i S kvartu: njihovi redovi idu preko cele širine grada.
- **Reka**: N i S kvart na istočnom kraju izlaze na quay street; voda, apron i far road se
  produžavaju na celu visinu grada, promenada ostaje samo uz jezgro, a uz reku u N/S kvartu
  stoji **`row`** klasa bloka. Mostovi ostaju samo jezgrovi; druga obala ostaje prazna.

### 1.6 Rings — koliko je mreža duboka

`Rings` je **koliko blokova duboko** rezidencijalno ide od park drive-a napolje. To je jedini
broj koji menja veličinu grada:

| Rings | dubina kvarta | ceo grad (sa pojasom na 3 strane) | blokova u mreži |
|---|---|---|---|
| 1 | jedan red blokova, ~65–105 m | ≈ 750 × 850 m | ~20 |
| 2 | dva reda, ~145–215 m | ≈ 900 × 1050 m | ~40 |
| 3 | tri reda, ~230–320 m | ≈ 1050 × 1250 m | ~60 |

Radi se kao **polje u inspektoru** (`CoreDistrict.rings`), pa se isti seed nacrta sa 1 i sa 2 i
odluka pada **po slici i po fps-u** (§3, mera 5), a ne unapred. Podrazumevano 2.

### 1.7 Park u mreži — 10 %

**Odluka 6 (korisnik: „10%")**: park je **1 na 10 ćelija mreže** (`ParkOdds = 0.10`), uz tvrde
granice: **nijedan u prvom prstenu** (§1.3), **nikad preko ulice od drugog parka**, najviše 2 po
kvartu. Ćelija mreže (12–17 × 13–21) daje `ParkWalk` klasu `Square`/`Park`, sa prelazima na sve
četiri ulice. Brojano i prijavljeno po seedu.

Parkovi *unutar* bloka (`park-01`, `park-02` iz žetve, koje `ResidentialLot.Parks` već deli) su
dvorište bloka, ne linija parka, i ne ulaze u pravilo §1.3.

### 1.8 Policija u mreži

**Odluka 4 (korisnik: „za sad")**: `police-station-block` (80 × 65 m = 16 × 13 ćelija, trotoar u
prefabu) zauzima ćeliju mreže kad korak stane (16 × 13 ili okrenuto). Ostatak ćelije = njegov
parking (isti `Lot` mehanizam kao plići blok jezgra). **Najviše jedna po kvartu, oko 1 na 3
kvarta** → uz stanicu u jezgru, grad ima 1–2. Kad korak ne stane, kvart ostaje bez nje i to se
prijavi — ne bira se manja zgrada, nema je.

### 1.9 Kompozicija bloka

`StandResidential()` kao `StandParks()`: `ResidentialLot.Roll(W, D, dice, artery: strana ka park
drive-u / bulevaru, streets: 4 × true)` → `ResidentialBlocks.Compose(plan, root, rng,
Instantiate)` **u koordinatnom početku**, pa `root.position = box.min`. Presude bloka (`Faults`,
`Refused`, `Stood.Missing`) idu u log kao kod parka. Blokovi `row`/`corner` na ostacima biraju
yaw tako da im front gleda ulicu.

### 1.10 Saobraćaj, performanse, mapa

- **Kola**: `carCount = 24` je bilo za jezgro; lane graf raste ~3×. Broj kola se računa iz
  dužine traka (**kola na 180 m trake**, sadašnja gustina); harness presuđuje (5 runova).
- **Performanse**: zgrada 150–300 delova, blok ~1.500; `Rings = 2` ≈ 40 blokova ≈ 60.000
  delova. Merge pass (`ScenePerf.Merge`) postoji; fps za `Rings = 1` i `2` odlučuje default.
- **Mapa / turf**: `BlockTheBuildings` traži decu `SM_Bld_*`, a rezidencijalni prefabi su jedan
  spojen mesh — kutije se uzimaju iz `ResidentialLot.Plan.Ground == Building` ćelija po
  jedinici, ne iz imena.

---

## 2. Šta se menja gde

| šta | gde |
|---|---|
| `res-` blok u deal-u; zapadne dopune = rezidencijalno + parking (nikad park); pravilo „park preko puta parka" i `ParkRuns` u presudi | `Assets/RoadDemo/CoreLayout.cs` (`Roll`, `Belt`, verdict) |
| pojas na 3 strane: zapadna traka pune visine, park drive iza nje, kapije (bulevar + 1–2 ulice reda), `Rim.Party` na uglovima | `CoreLayout.cs` (`Belt`), `CoreDistrict.StandParks` |
| mreža: `Residential(plan, dice)` — okvir, jedna x-podela, prstenovi N/S, kolone W oko bulevara, ostaci, faza, park 10 % / policija, granična ulica, kraćenje slepih krajeva i bulevara | `CoreLayout.cs` (novi deo / `CoreLayout.Residential.cs`) |
| reka na celu visinu grada, promenada samo uz jezgro | `CoreLayout.River` |
| glif `r` za rezidencijalni blok u `Raster.Map` | `Assets/RoadDemo/CoreRoads.cs` |
| kapije pojasa na ulicama mreže: `Edge.Crossing` → lista `Crossings` | `ParkWalk.cs` (`Mouths`) |
| `StandResidential`, policija u mreži, kola po dužini trake, `rings` polje, kutije za mapu iz ćelija | `CoreDistrict.cs`, `CoreDemoBuilder.cs` |
| crtež u editoru crta i mrežu; **top-down PNG** (`--shot`) za sud po slici bez Play-a | `CoreCitySketch.cs`, `PipelineCommands.cs` |
| tally `--city` (ParkRuns, parkova u mreži, policija, klase, `ResidentialLot` faults, ostaci) | `Tools/CoreSim/Program.cs` |

Ne dira se: `ResidentialLot`/`ResidentialBlocks` (blok je gotov i ocenjen), `ParkBlocks`,
`QuayBlocks`, `LaneNet`/`RasterGraph`, `Game.unity` grid.

---

## 3. Mere i presuda (redom, svaka pre sledeće)

1. **CoreSim, 30 seedova, jezgro + zapadne dopune** (§1.2–1.3): **IZMERENO 2026-08-27** —
   `Faults = 0` na **30/30**, `ParkRuns = 1` na svakom, parkova 3–5 po seedu (2 pojas + 1–2
   dealt + poneka plitka dopuna), rezidencijalnih blokova 1–12 po seedu, prosek 26 deal-ova
   (max 81, ~2.6 ms po deal-u → ~70 ms po seedu). Cena: dopuna nosi 2–4 ulice više po redu, pa
   je udeo čistih deal-ova pao sa 6 % na 5 % i budžet deal-ova dignut sa 80 na **240**.
   Synty referenca netaknuta (0 grešaka, 24 puta).
2. **Crtež u editoru** (`gangsters_core --draw --shot`): slika odozgo — jedan pojas po strani,
   park nikad dva zaredom, zapadni rub čita kao prelaz.
3. **CoreSim `--city`, 30 seedova** (mreža): `Faults = 0` na 30/30; parkova u mreži ≈ 10 %,
   prvi prsten 0, max 2 po kvartu; policija 0–1 po kvartu; ostaci legalni.
4. **Harness** (`gangsters_play --scene CoreDemo --seconds 180`): 5/5 `PASSED` sa kolima po
   dužini trake; nijedan boks zaključan na šavovima i na kapijama pojasa.
5. **Perf**: fps sa `Rings = 1` i `2` posle merge-a → default i slika za odluku o dubini.

---

## 4. Redosled rada

1. **URAĐENO 2026-08-27** — zapadne dopune + pravilo parka + `ParkRuns` (§1.2, §1.3) → mera 1.
   *Tri parka zaredom su nestala.*
2. Pojas na 3 strane sa kapijama (§1.4) → mera 1 ponovo, pa mera 2 (slika korisniku).
3. Mreža u deal-u (§1.5–1.8) + CoreSim `--city` → mera 3.
4. `StandResidential`, kapije pojasa na ulicama mreže, policija → slika.
5. Kola po dužini trake, harness → mera 4; perf i `Rings` → mera 5.
6. Mapa/turf kutije (§1.10).

---

## 5. Rizici

- **Čisti deal-ovi**: dopune sa klasama + zapadni pojas + šavovi mogu da spuste procenat čistih
  deal-ova; `Deals = 80` daje prostora, a CoreSim meri pre nego što se išta crta.
- **Zapadni pojas guši saobraćaj** ako kapije ne prođu — harness (mera 4) je sudija.
- **Delovi**: `Rings = 2` je ~60.000 delova, više nego ijedna demo scena do sada; merge postoji,
  `Rings` je prekidač.
- **Klase na dopunama i ostacima**: `corner`/`row` uz reku i uz park drive su ocenjeni samo u
  laboratoriji; prvi crtež će reći da li čitaju uz okvir.

---

## 6. Šta se koristi od postojećeg

`CoreLayout.Roll/Arrange/WithGround/Park/Reshape/Describe`, `CoreRoads.Build/Lay`,
`RasterGraph.Build`, `ResidentialLot.Roll/Classify/Sized`, `ResidentialBlocks.Compose`,
`ParkWalk.Lay/Edge`, `ParkBlocks.Compose/Pave`, `QuayWalk/QuayBlocks`, `RiverBridge`,
`CoreBuildingBlocks` (police-station-block), `CoreCitySketch.Draw`, `StandaloneDistrictHost`,
`Tools/CoreSim`.

---

## 7. Šta NIJE u ovom planu

- Pešaci u kvartu (jezgro ih još nema).
- Druga obala reke, industrija i luka u istoj sceni — sledeći plan grada v3.
- Ugrađivanje u `Game.unity` grid (`DistrictKind` još nema `Core`).
- Novi tipovi zgrada; radi se sa šest požnjevenih + `park-01/02` + `pizzapub` + policija.

---

### 1.9b Blokovi se ne ponavljaju (2026-08-27)

Korisnik: *„vrtis i dalje iste blokove... ne treba da se ponavlja jedan te isti blok u
residental kvartu"*. Uzrok nije bio raspored nego **recept**: ćošak je uzimao **najveću** kuću
koja stane, red **najdužu**, svaki blok kvarta imao je **istu arteriju** (jug), a kafić je išao
u svaki blok koji ima procep. Ista mera → isti blok.

- `ResidentialLot.Fit` (ćoškovi) i `Longest` (redovi): **žreb među svim jedinicama koje stanu**,
  težina po površini odn. po kvadratu dužine — velike/duge kuće i dalje obično dobiju velike
  ćoškove i duge redove, ali ne uvek.
- Arterija se **deli po bloku** (`CoreLayout.Ring`), ne fiksno.
- Kafić: `CafeOdds = 0.34` umesto uvek.
- Probano i **vraćeno**: ostaviti poneki ćošak prazan — prazan ćošak je greška po odobrenom
  planu (`BlankCorner`), palo je 15 od 30 blokova.

Mera: sweep bloka **120/120 čisto**, 0 praznih ćelija; grad **30/30 čisto**; seed 1987 daje
**184 različita bloka**. Budžet deal-ova 400 → **600** (prosek 101, max 426).

## 8. Stanje

**Urađeno 2026-08-27 (korak 2): pojas na tri strane i pet kvartova.**
`CoreLayout.Ring.cs`: kopneni pojas (traka 30–40 m iza ivične ulice) sa **park drive**-om iza
njega i **kapijama** (bulevar + 1–2 ulice reda prolaze kroz pojas, ostale staju na ivičnoj
ulici); ulice iza pojasa sever/jug spajaju se sa park drive-om u **prsten oko pojasa**; pet
kvartova u rešetci. Presuda: **30/30 seedova čisto**, `ParkRuns = 1`, 75–136 rezidencijalnih
blokova po seedu, 13–20 parkova (pojas se broji kao jedan), grad ~900–1265 × 1350–1830 m.
Cena: prosek 50 deal-ova po seedu (max 231), pa je budžet dignut na **400**; deal ~6 ms.
Crtež seed 1987 u editoru: **160.000 objekata**, 137 blokova, nacrtan za ~40 s.

**Urađeno 2026-08-27 (korak 1)**: `res-` blokovi u deal-u; zapadne dopune se dele na
rezidencijalne blokove (`CoreLayout.Fill/Split`, tačna podela DP-om, ulice poravnate sa redom
preko puta); parking ≤ 4 ćelije na spoljnom kraju; park u dopuni samo kad nijedna klasa ne
stane u dubinu reda; `ParkRuns` mera + odbijanje deal-a; park se ne deli u poslednja dva reda
(uz pojas); `StandHomes` u `CoreDistrict` i `Homes` u `CoreCitySketch` (blokovi se stvarno
postave). Nije još: pojas na 3 strane, mreža, policija/park u mreži, harness i perf.

---

## 9. Odluke (korisnik, 2026-08-27)

1. **Redovi jezgra se ne izjednačavaju** („ne") → zapadne dopune nose rezidencijalne blokove i
   parking (§1.2).
2. **Pojas na tri strane** (sever, jug, zapad) (§1.4). Izvedeno iz toga: kapije kroz zapadni
   pojas, jer bi jezgro inače imalo jedan ulaz.
3. **Rings**: pitanje nije bilo jasno — sada je objašnjeno u §1.6 i **odlučuje se po slici**
   (isti seed nacrtan sa 1 i sa 2), ne unapred.
4. **Policija u mreži: da, za sad** (§1.8).
5. **Ostaci = parking i parkovi** (§1.5), park uz pravilo §1.3 i kvotu §1.7.
6. **Park u mreži: 10 %** (§1.7).

---

## 10. Raznolikost kvarta: mera, uzrok i plafon (2026-08-27)

**Mera (sweep 480 blokova, `--residential`), pre bilo kakve izmene:**

| jedinica | udeo | ćoškovi | redovi |
|---|---:|---:|---:|
| `residential-04` | **63,4 %** | većina | većina |
| `residential-01` | 15,8 % | | |
| ostale četiri | 3–8 % svaka | | |

**Uzrok — nije bio raspored nego pravilo + katalog.** Katalog ima **šest kuća**, od kojih
**četiri nose radnje u prizemlju** (`residential-01/02/03/06`). Pravilo iz plana bloka bilo je
*jedan izlog po bloku*, pa je čim ćošak uzme radnju, **sve četiri bile zabranjene u celom bloku** —
ostajale su dve (`-04` i `-05`), a `-05` gleda na dve **suprotne** strane pa nikad ne može biti
ćošak. Dakle: svaki ne-arterijski ćošak je mogla da uzme **tačno jedna zgrada u igri**.

**Izmene:**
1. **Ograničenje na radnje je UKINUTO** — `ResidentialLot.ShopsPerStreet = 0` (0 = bez
   ograničenja), `TwoShops` presuda se ne primenjuje. Korisnik ga je povukao 2026-08-27
   („ma bez ograničenja") pošto je čuo da pravilo nije bilo njegovo nego moja pretpostavka
   upisana u plan bloka. Mera: na 1 po bloku najčešća zgrada je 63 %, na 3 po ulici 39 %,
   bez ograničenja **isto 39 %** — ograničenje na 3 već nije bilo obavezujuće.
2. **Ćošak se deli iz celog kataloga**, a pravilo o izlogu se proverava u `Fit` po strani;
   arterijski ćošak i dalje prvo traži radnju.
3. **Kazna za ponavljanje u bloku** — težina u žrebu se deli sa `1 + 4×(koliko ih već stoji)`.
4. **Boja po zgradi** — Synty isporučuje atlas u tri varijante (`PolygonCity_0N_A/B/C`), sve
   jedinice su požnjevene u `_A`; sada svaka zgrada dobija svoju. *Napomena: `_B`/`_C` nemaju
   emisionu mapu (osvetljeni prozori noću).*

**Posle izmena:** `residential-01` 39 %, `-04` 22 %, `-02` 11 %, `-03` 10 %, `-06` 8 %, `-05` 3,5 %.
Prve dve **88 % → 61 %**. Sweep **480/480 čisto**, grad seed 1987 i dalje 0 grešaka (93 deal-a).

**Plafon je katalog.** Pokušana je automatska žetva još kuća iz `CORE BLOCKS (bare)` i `Demo`:
ne ide. Izmereno: Synty blok je **jedan povezan prsten** (flood fill daje ceo blok, ne zgradu), a
**radnje su izmešane skoro u svakoj ćeliji** — traženje „redova bez radnji dužine ≥3" našlo je
**jedan** kandidat u celoj sceni. Šest jedinica je požnjeveno **ručno, okom** (ime je ugovor:
`residentialN` na svakom komadu u `Assets/Scenes/CoreHarvest.unity`).

**Otvoreno za korisnika:**
1. **Još kuća** — imenovati 4–6 zgrada kao `residential7..12` u `CoreHarvest.unity`, najbolje bez
   izloga; jedna komanda ih peče i kvartovi ih odmah koriste.
2. **Krovovi** — odozgo se vidi samo krov, a svi su isti sivi. Boja fasade (tačka 4) se odozgo
   **ne vidi**. Oprema na krovu (rezervoari za vodu, klime, bilbordi, krovne bašte) bi za pogled
   odozgo uradila više nego bilo šta drugo — ali je to odluka o izgledu.
3. **Prazan beton u sredini bloka** — polovina svakog bloka je gola ploča.
