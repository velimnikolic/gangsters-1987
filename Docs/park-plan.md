# Plan: generator parka (ParkBlocks)

> Odobreno 2026-08-26. Odluke iz §7 su uzete i upisane; stanje po koracima je u §5.
>
> U jednoj rečenici: **park je blok elastične mere** — kvart mu da pravougaonik (5 m ćelije,
> od 5×5 do 30×30 ćelija i duže trake), a recept ga **komponuje na licu mesta** po Synty
> gramatici izmerenoj iz njihove demo scene — isti obrazac kao industrijska parcela
> (`IndustrialBlocks.Compose`), isti raster i ista presuda (`CoreRoads`). Jedan kod služi
> tri mesta: park **u jezgru** (jedinica u deljenju `CoreLayout.Roll`), **pojas parkova oko
> jezgra** (red park-blokova koje portali seku), i kasnije džepni park u rezidencijalnim
> kvartovima.

| šta | gde (novo) |
|---|---|
| komponovanje parka (recept po meri) | `Assets/RoadDemo/ParkBlocks.cs` (runtime, delegat za instanciranje kao `IndustrialBlocks`) |
| staza kao graf pločica | `Assets/RoadDemo/ParkWalk.cs` (čist C#, `Tools/CoreSim` ga kompajlira) |
| park kao jedinica u jezgru | `CoreLayout.cs` (jedinica `Park` elastične dubine), `CoreDistrict` stoji park posle čistog deljenja |
| pojas oko jezgra | `Assets/RoadDemo/ParkBelt.cs` (faza 3, v. §4) |
| laboratorija | `Assets/Scenes/ParkLab.unity` + `Assets/Scripts/Editor/ParkSketch.cs`, meni `Tools/City/Park/Sketch A Park` |
| komanda | `unity command gangsters_park --seed N --size pocket|square|park|WxD [--count M] [--draw]` |
| presuda bez editora | `Tools/CoreSim --park` |

---

## 0. Šta postoji (analiza, 2026-08-26)

**Tri parka u repou, nijedan dinamičan:**

1. **`block-08.prefab`** (`Assets/Prefabs/CoreBlocks`) — Syntyjev park, požnjeven kakav jeste:
   6×6 ćelija (30×30 m), maska sva `#`, `MaxH = 0`, nijedne zgrade. Ivičnjak-prsten od jedne
   pločice, unutra **4×4 = 16 pločica: 10 `SM_Env_Grass_01` + 6 `SM_Env_GrassPath_*`**
   (4 pravo + 1 ugao + 1 T), ograda `SM_Env_Fence_01`/`Fence_End_01` na svakoj ivičnoj
   pločici, prekinuta samo gde staza izlazi na trotoar. 6 stabala `Tree_01..03` **van rastera**
   (pomak 0.5–1 m, u labavim parovima uz stazu, nikad na liniji ograde), 2 `ParkBench_01` na
   ≤ 1.5 m od ivice staze, 2 `PicnicTable_01` na travi, 2 lampe unutra, 2 kante, **38
   `SM_Env_Flower_01`**; 2 palme u korpama na trotoaru. U deljenju jezgra on je običan blok:
   isti park svaki put, iste mere. To je **referenca** za recept (kao Synty raspored za jezgro).
2. **`RoadDemoBuilder.Seams.BuildPark` / `Zones.BuildPocketPark`** (stari grid-grad): Unity
   `Plane` sa travnatim materijalom, betonske ploče kao staza (krst od ivičnjaka do ivičnjaka),
   fontana / teren iz PalmCity ako stane, klupa/lampa na 11–12 m, drveće rasuto `w·d/110`.
   Radi, ali je **crtež, ne kit**: nema pločica, nema ograde, nema Synty ritma; ne poštuje
   „jedna pločica po površini". Ostaje u gridu dok grid postoji; ovaj plan ga ne dira.
3. **Stari LivingCity `ParkLayout`/`ParkDresser`/`ParkNavPlan`** (`Assets/Scripts/Generation`,
   1842 + 516 + … linija, 3 test-fajla): najozbiljniji od tri — arhetipovi (formal/informal/
   civic), ulazi bodovani po strani, kičme (ose / bezijer / prsten), zone (pojas, travnjak,
   gaj, parter), stanice, pet sanity prolaza, nav graf sa Dijkstrom. **Ne prenosi se**
   (`road-demo-self-contained`), ali se **uzimaju pouke**:
   - determinizam u `(seed, blockId)` i „potroši sve bacanja unapred" — geometrija ne sme da
     premeša tok;
   - invarijante iz testova: svaki kraj staze je ušće, spoj na drugu stazu ili zatvoren prsten;
     klupa na ≤ 9 m od staze i **okrenuta ka njoj**; travnjak postoji i ostaje prazan; gustina u
     opsegu ili upozorenje; nema drveta na stazi;
   - **slabost koju ne ponavljamo**: mali park rešava odbijanjem („nema mesta za jezero,
     preskočeno"), a veliki **ne skalira** — jedna fontana i jedan travnjak na 120 m ostave
     pusto polje. Zato §2.3 „sobe";
   - `PrefabDatabase` mu je prazan za sve park-kofe — nikad nije ni bio povezan sa građom.

**Synty gramatika** (`Docs/synty-demo-anatomy.md` + merenje demo scene, 2026-08-26): park je
**ograđena trava sa stazom**, ne plac sa dekorom. Dva primerka u demou (block-08 i dvorište
bloka-12: 9 trave + 9 staze, 11 stabala, 3 klupe, 2 stola, 3 kante, 4 lampe, isti recept,
gušće). Iz njih: **1.5–2.5 stabla / 100 m²** trave, klupa uz stazu, sto na travi, kanta uz
klupu, lampe unutra 2–4, cveće u grozdovima uz ogradu i stazu. Town demo (predgrađe) park
nema; PalmCity demo daje terene i fontanu kao pojedinačne komade.

**Građa** (ceo inventar u izveštaju, ovde samo šta recept koristi):

| uloga | komad | paket |
|---|---|---|
| trava (jedina pločica poda) | `SM_Env_Grass_01` 5 m | City |
| staza kroz travu | `SM_Env_GrassPath_Straight/Corner/T/Junction_01` 5 m | City |
| popločano (plato oko fontane) | `SM_Env_Path_Straight/Corner/T/Junction_01` 5 m | City |
| ograda parka | `SM_Env_Fence_01` + `Fence_End_01` | City |
| klupa, sto, kanta, lampa, cveće | `ParkBench_01`, `PicnicTable_01`, `Trashbin_01/02`, `LightPole_Base_01`, `Flower_01` | City |
| drveće | `SM_Env_Tree_01..03` (City) + `Tree_Palm_*` (PalmCity, već u jezgru) | City / PalmCity |
| fontana | `SM_Prop_Fountain_01` + `FX_Fountain_Water/Spray_01` | PalmCity (Town ima i svoju) |
| igralište | `Playground_Fort/Roundabout/Slide/Swing/Table_01`, `Sandpit_01`, ograda `Fence_Wood_*` | Town |
| tereni | `SM_Env_Court_Tennis_01`, `Court_BasketBall_01` (+ mreža, koš) | PalmCity |
| paviljon (bandstand) | `SM_Prop_Pavilion_01` | PalmCity |
| spomenik | `SM_Gen_Prop_Statue_01..05`, `SM_Bld_Statue_Corner_01` | Generic / PalmCity |
| kiosk, hot-dog | `SM_Prop_Shop_Stand_01..05`, `HotdogStand_01`, `Newspaper_Stand_01` | PalmCity / City |
| živa ograda, žbun | `SM_Env_Hedge_01..05`, `Hedge_Topiary_*`, `Bush_*` | PalmCity / Town |
| toalet, skejt-park | `building-park-toilet`, `building-skatepark` | CityKit (već kopirano) |

**Nema**: jezera sa obalom (samo `PNB_Core/SM_Env_Water_Plane_01`, bez ivice), estrade,
spomen-ploče, kioska za sladoled. Ako nema — ne pogađa se (`leave-it-empty-dont-guess`).

---

## 1. Šta je park u američkom gradu 1987 (pravila koja recept sprovodi)

1. **Ograđen kvadrat sa stazom**, ne travnjak. Staza ulazi sa trotoara na 2–3 strane
   (nikad 4 pošto Synty ne radi krst), lomi se, i kod većih parkova skupi u **plato** oko
   fontane/spomenika. Ušće staze pada **preko puta zebre** gde blok dodiruje raskrsnicu.
2. **Ivičnjak-prsten** je blok kao svaki drugi (lampa na 20 m, kanta, hidrant — `CorePavement`
   pravila) i park ga **nosi sam** (`Kerbed`), kao industrijski blok.
3. **Drveće van rastera**, u labavim parovima uz stazu i u pojasu uz ogradu; **travnjak u
   sredini ostaje prazan** (to je ono što se vidi odozgo). Gustina 1.5–2.5 / 100 m².
4. **Jedan program po sobi** (§2.3): fontana, igralište, tereni, paviljon, gaj, travnjak.
   Nikad dva ista programa u parku ispod 100 m; tereni su uvek **par** (tenis ×2 na 10 m
   kao u PalmCity demou) ili košarka sama.
5. **Igralište je ograđeno** (Town drvena ograda, kapija ka stazi) — 1987, pesak i gvožđe.
6. **Ćošak parka ka raskrsnici je civilan**: hot-dog / novine na trotoaru u ćošku (ne unutra).
7. **Park nije prazno tlo**: svaka ćelija je trava ili staza; ostatak rastera (`Spare`) i
   dalje ostaje prazan i ide u izveštaj — park se ne koristi kao popuna.
8. **Ništa se ne izmišlja**: bez jezera, bez estrade, bez kulisa. Skejt-park (postoji u
   CityKit-u) je program samo za `park` klasu i samo jednom po gradu (kao svaki landmark).

---

## 2. Kako radi

```
mera (w×d ćelija) + strane (put/uličica/portal po strani, gde su zebre)
        │
        ▼
ParkWalk.Plan(w, d, mouths, rng)   ── staza kao skup ćelija na 5 m rasteru (čist C#)
        │                              ušća → kičma → sobe → plato
        ▼
ParkBlocks.Compose(plan, rng, stand) ── pločice (trava/staza/plato), ograda, program po sobi,
        │                              drveće, nameštaj, ivičnjak-prsten sa opremom
        ▼
Stood { Gaps, FenceGap, TreesOnWalk, Unreached, Density }  ── presuda kompozera, sve 0
```

### 2.1 Mera i klase

Recept prima **bilo koji** pravougaonik `w × d` ćelija, `w, d ≥ 5` (ivičnjak 1 + trava ≥ 3).
Klasa se **izvodi iz mere**, ne bira:

| klasa | unutrašnjost (ćelija) | blok (m) | program | referenca |
|---|---|---|---|---|
| `pocket` | 3×3 … 5×5 | 25–35 | staza L/T, 2 ušća, klupe, sto, cveće; **bez** programa | block-08 (4×4) |
| `square` | 6×6 … 10×10 | 40–60 | **jedan** program u sredini (fontana na platou / igralište / par terena), staza oko njega | dvorište bloka-12 + PalmCity |
| `park` | 11×11 … 28×28 | 65–150 | sobe (§2.3): 2–4 programa, gaj, prazan travnjak, paviljon/spomenik | — (nema Synty reference; sud je crtež) |
| `strip` | odnos strana > 2.5 | 30 × 80–300 | kičma celom dužinom, sobe nanizane, poprečna staza na svako ušće | pojas (§4) |

Iznad 28×28 recept se **ne rasteže** — kvart takav park deli ulicom (park od 150 m je već
Central Park za grad ove veličine; `landmark-once-per-city`).

### 2.2 Staza: `ParkWalk` (graf pločica)

Staza je **skup ćelija** na 5 m rasteru; komad se bira po susedima (1 sused = kraj → pravo
do ušća; 2 u liniji = `Straight`; 2 pod uglom = `Corner`; 3 = `T`; 4 = `Junction`). Time
je svaka 4-povezana staza polagana bez izuzetaka.

- **Ušća**: po jedno na najviše po strani, 2–3 (`pocket`/`square`), 2–4 (`park`), na
  svakom ušću puta u `strip`. Strana bez puta (deljena sa drugim parkom, portal) nema ušće.
  Ušće leži na ćeliji ivičnjaka **preko puta zebre** ako strana dodiruje raskrsnicu, inače na
  sredini strane ± 1 ćelija. Ograda ima procep tačno na ušću.
- **Kičma**: ušća se spajaju **Manhattan** putanjom kroz unutrašnjost, sa jednim lomom
  (L) po paru — Synty ne vuče pravu liniju od ivice do ivice. `pocket`: dva ušća, jedan
  lom, jedna kratka slepa grana (block-08 ima jednu). `square`: kičma obilazi centralnu
  sobu (prsten od `Path` platoa oko fontane, ili obilazak igrališta). `park`: kičma +
  sekundarne staze između soba.
- **Obodna petlja** (dodato 2026-08-26 posle sweep-a, v. §5.1): `park` i `strip` klasa dobijaju
  stazu koja obilazi park **1–2 ćelije od ograde** (inset po strani iz seeda). Bez nje je staza
  DRVO, pa soba koja leđima naleže na ogradu ne može da se preseče — svaka linija kroz nju
  udara u ogradu, a staza koja staje na ogradi je slepi kraj. Petlja to zatvara: svaka soba je
  omeđena stazom i spolja, a pojas između petlje i ograde je baš onaj u kom stoji drveće.
- **Plato**: ćelije oko fontane/spomenika su popločane (`SM_Env_Sidewalk_01` — Syntyjeve
  `Path_*` pločice su **2.5 m široke**, dakle baštenska staza, ne plato).
- **Invarijante** (`ParkWalk.Check`): graf povezan (svako ušće stiže do svakog); nijedan
  kraj koji nije ušće osim jedne dozvoljene slepe grane u `pocket`; staza ne dodiruje
  ogradu osim na ušću; staza ne seče sobu-program.

### 2.3 Sobe (kako veliki park ne ostane pust)

Unutrašnjost se **deli kičmom i sekundarnim stazama na sobe** ≤ 10×10 ćelija. Svaka soba
dobija tačno jedan program iz tabele, po pravilu a ne kocki:

Mere su **izmerene u živom editoru** (`gangsters_measure`, 2026-08-26), ne pogođene:

| program | soba (ćelija) | mera komada | šta |
|---|---|---|---|
| `lawn` | bilo koja | — | prazna trava, po ivici 2–4 stabla; **≥ 1 obavezno** |
| `grove` | 4×4 | stablo 1.9–2.2 m | 6–10 stabala na 4–6 m |
| `fountain` | 4×4 | fontana **4.77 m** | plato 3×3, fontana + `FX_Fountain_Water`, 4 klupe okrenute ka vodi |
| `playground` | 4×4 | pesak 2.7, ljuljaška 3.1, fort 2×3 | 15 m dvorište, 5 komada oko peska, **drvena ograda sa kapijom** |
| `courts` | 5×4 | tenis **7.5 × 12.5** | tenis ×2 na 10 m razmaka (kao PalmCity demo) ili košarka **7.5 × 15** sa dva koša |
| `pavilion` | 3×3 | **9.49 × 8.39** | paviljon + 4 klupe u krug |
| `statue` | 3×3 | statua 1.09 × 0.63 | plato + spomenik + cveće u prstenu |
| `skatepark` | **9×7** | **40.5 × 31.5** | `building-skatepark`; prvi plan je rekao 6×6 — to bi ga postavilo na ogradu |
| `toilet` | **3×2** | **10.57 × 5.49** | `building-park-toilet`, leđima na ogradu |

`square` = jedna soba = jedan program (nikad `lawn` sam). Redosled dodele: prvo `lawn` uzme
**najveću** sobu, pa svaki program bira **NAJMANJU sobu u koju staje** — ne sledeću po redu.
(Prva verzija je delila redom po veličini pa je toalet od 10 × 5 m dobio sobu od 10 × 75 m,
a terenima nije ostalo ništa.) Program koji ne staje se **izostavlja i prijavljuje**; park
kome su sve sobe već podeljene je **pun**, ne odbijen, i o tome ne javlja ništa.

**Soba je „prevelika" po DOMETU, ne po pravougaoniku** (§5.1): prevelika je ako je neka njena
tačka dalje od **25 m od staze**, ili ako je **dvaput duža nego šira** (a nije pojas od ≤ 2
ćelije). Prvi kriterijum je bio veličina pravougaonika i bio je pogrešan — park sa petljom ima
jednu prstenastu sobu trave čiji je pravougaonik ceo park, a nigde u njoj nije više od 10 m
do staze.

### 2.4 Drveće, nameštaj, cveće (izmereno, ne ugođeno)

- **Pojas uz ogradu**: stablo na svakih 2–3 ćelije, pomereno 0.5–1.5 m unutra i po dužini
  (van rastera — Synty ih nema ni na jednom centru pločice), **nikad na liniji ograde**.
- **Uz stazu**: labavi parovi na 1–2 m od ivice staze, naspramno ili smaknuto.
- **Klupa**: na 0.6–1.5 m od ivice staze, **okrenuta ka stazi**, 1 na ~8 m staze u `pocket`,
  1 na ~12 m u većim; **kanta na 1–2 m od svake druge klupe**. Sto za piknik na travi u
  četvrtini bez programa (1 na 200 m² trave, max 3).
- **Lampa**: unutra na ćoškovima staze/platoa, 2 (`pocket`) do 1 na 20 m staze; ivičnjak
  dobija svoje po `CorePavement`.
- **Cveće**: grozd od 3–8 `Flower_01` uz ogradu iznutra i uz plato; block-08 ima 38 na
  400 m² trave — gusto je ispravno.
- **Gustina** se meri i prijavljuje po parku: stabla / 100 m² u [1.5, 2.5], inače upozorenje.
- **Sve seedovano**: `System.Random(seed·7919 + I0·104729 + J0·1299709)` kao industrijska
  parcela; sva bacanja potrošena pre geometrije.

### 2.5 Ivičnjak i ulična oprema

Park **nosi svoj prsten**: `Sidewalk_Straight` po obodu, `Dip_Corner` na ćoškovima, slivnik
na 12, pa `CorePavement` trake (lampa 1.0 m na 20 m, kanta/hidrant/sanduče 1.5 m, klupa uz
zid 4–4.5 m — ovde „zid" je ograda parka). Na ćošku ka najprometnijoj raskrsnici hot-dog
ili novine (1 po parku, samo `square`/`park`). Palme u korpama kao svuda u jezgru.

---

## 3. Park u jezgru (`CoreLayout.Roll` + `CoreDistrict`)

Jezgro deli **gotove** blokove; park je jedini **elastičan**. Najmanja izmena koja to nosi:

- **Jedinica `Park`** u `Roll`: 1–2 po jezgru (seed), bez `Nests`, maska sva `#`. Širina iz
  klase (`pocket` 6–7, `square` 8–12 ćelija), **dubina = dubina reda u koji je upala** —
  park nikad ne dobija `Lot` (parking iza parka je besmislica), nego raste do zadnje ulice.
  Time red sa parkom nikad nema plićeg bloka. Odnos: 70 % `pocket`, 30 % `square`; `park`
  klasa **nije za jezgro** (jezgro je 200–300 m dugo — park od 100 m bi bio trećina).
- **Mesto u redu**: nikad dva parka susedna, nikad uz uličicu (park hoće ulicu s obe strane
  — ušća na uličicu su prekid ivičnjaka bez zebre); prednost ćošku reda (park na ćošku
  bulevara je Synty raspored: block-08 je uz ivicu).
- **Raster ne zna da je park**: `CoreRoads.Build` sudi kao i svaki blok (put sa sve četiri
  strane, bez batrljaka). `Arrange` dela do 40 puta i uzima prvo čisto — **nepromenjeno**.
- **Stajanje**: `CoreDistrict.Plan` posle čistog deljenja komponuje park **na koordinatnom
  početku, pa ga pomeri** (pouka industrije: `IndustrialQuarter.Stand`), sa `Locals()`
  stranama iz rastera (koja strana je ulica/bulevar, gde su raskrsnice → ušća preko puta
  zebri). `block-08` ostaje u stocku kao gotov blok (referenca), ali ga `Roll` ne deli kad je
  `Park` jedinica uključena — inače dva parka istog kroja.
- **Presuda kompozera** posle stajanja, sve nula: `Gaps` (ćelija bez pločice), `FenceGap`
  (procep ograde van ušća), `TreesOnWalk`, `Unreached` (ušće bez veze), `Refused` (program
  koji nije stao — dozvoljen, ali u izveštaju).
- **Mera pre crteža**: `Tools/CoreSim --seed 1 --count 30` → 30/30 čisto **i dalje**
  (park je samo još jedan blok za raster); `--park` tally: po seedu klasa, mera, ušća,
  programi, gustina, odbijanja. Synty referenca (`--synty`) netaknuta: 0 grešaka, 21 ulica.

---

## 4. Pojas oko jezgra (korisnikova zamisao: jezgro → park → rezidencijalno)

**ODLUKA 2026-08-26**: **grad v3** (komponovan iz generisanih kvartova), i pojas hvata
**najmanje DVE strane** jezgra — korisnik: *„msm da treba da okruzimo centar s makar dve strane
da bi bio laksi prelaz na residential"*. Park je time šav između jezgra i rezidencijalnog, ne
ukras: gde god rezidencijalni kvart dodiruje jezgro, dodiruje ga preko parka. Ostale strane
smeju ostati dubine 0 (parking / koridor vijadukta), ali nikad više od dve.

**Šta zamisao traži**: grad koji se **komponuje iz generisanih kvartova** — jezgro u sredini,
pojas parka oko njega, rezidencijalni kvartovi (budući generator) spolja. To nije grid
`Game.unity` (16×10 linija sa šavovima), gde jezgro još nije ni ugrađeno
(`DistrictKind` nema `Core`, `CityLayout.Roll` ga ne zna). **Pretpostavka ovog plana**: to je
grad v3 — komponovan, a grid ostaje dok v3 ne stane; ovaj deo plana se **ne gradi pre §3** i
ne obavezuje na v3 — recept parka je isti u oba.

**Kako pojas izgleda u SAD 1987, iskreno**: pun prsten parka oko centra nije američki obrazac
(to su Beč, Adelaida, Savannah). Američki centar 1987 ima **jedan veliki park na jednoj
strani** (Central Park, Forest Park, Grant Park na obali, Balboa), **parkway/bulevar** duž
ivice, a ostale ivice centra su **parking placevi, rov autoputa i pruga** — „parking krater".
I `Docs/expressway-plan.md` već hoće **deblo na vijaduktu u pojasu D = 120 m oko grida** —
tlo pod vijaduktom je tačno park + parking, najprepoznatljiviji 1987 kadar.

**Predlog: okvir, ne prsten** — `ParkBelt` je red park-blokova oko pravougaonika jezgra sa
**dubinom po strani** iz seeda:

```
                 ┌──── park drive (ulica 15 m) ────┐
                 │  strip │ strip │  strip │ strip │   ← sever: dubina 6–8 ćelija (30–40 m)
   ┌───┐         ├────────┴───┬───┴────────┴───────┤
   │ P │         │        JEZGRO (redovi oko bulevara)        │ P │  ← istok/zapad: 0 (parking/
   └───┘         ├────────────┴───────────────────┤            vijadukt) ili 6–8
                 │     V E L I K I   P A R K  (20–30 ćelija)  │   ← jedna strana „velika" = gradski park
                 └────────────────────────────────┘
```

- **Portali jezgra seku pojas** (krajevi bulevara I/Z, S1/S2/S3 S/J) — svaki komad između
  dva portala je **jedan park-blok** `w × dubina`, klasa iz mere (`strip` za dugačke, `park`
  za veliku stranu). Ćoškovi pripadaju S/J trakama (pune širine).
- **Spolja ide „park drive"** — ulica 15 m preko cele dužine, deklarisana kao `Plan.Bands`
  (pouka jezgra: L-ugao zaključava saobraćaj) — i to je linija na koju **rezidencijalni
  redovi** kasnije pinuju pročelje. Rizik: `Plan.Bands` su horizontalne; vertikalna park
  drive na I/Z se ili **nađe** kao koridor (park-blokovi ostave procep od 3 ćelije) ili
  `Bands` dobija vertikalu — `CoreSim` odlučuje.
- **Najmanje dve strane nose park** (odluka gore); od njih je **tačno jedna velika** (20–30 ćelija = 100–150 m): gradski park, `park` klasa sa
  svim sobama, jednom po gradu. Strana se bira gde jezgro nema bulevar-portal (da park ne
  seče glavni ulaz).
- **Strane dubine 0** (v1: istok/zapad po seedu) su za kasnije: parking placevi jezgra ili
  koridor vijadukta. Ne pogađa se — ostaju prazne u izveštaju dok se ne odluči.
- Kad autoput dođe: deblo na vijaduktu **iznad** pojasa (D = 120 iz plana autoputa pokriva
  park drive + park + prvu ulicu jezgra) — stubovi u parku su `strip` program `viaduct`
  (ne u v1).

**Presuda pojasa** je ista: jezgro + pojas = jedan crtež za `CoreRoads.Build`, `Faults == 0`
pre stajanja; svaki park-blok svoju presudu kompozera.

---

## 5. Alati i redosled rada

| # | korak | dokaz | stanje |
|---|---|---|---|
| 1 | `ParkWalk` (čist C#) + `CoreSim --park --sweep` | **8 seedova × 676 mera (25–150 m) = 5408, sve čisto** | **urađeno** |
| 2 | `ParkBlocks.Compose` + `ParkSketch` meni + `gangsters_park` | kompajlira (runtime + editor zajedno); crtež čeka editor | **napisano, neviđeno** |
| 3 | `square` i `park` crteži, sobe i programi doterani sa korisnikom | snimci plan + kos | čeka |
| 4 | jedinica `Park` u `CoreLayout.Roll`, stajanje u `CoreDistrict` i crtežu | **60 seedova 60/60 čisto, prosek 1.33–1.73 deljenja** (bez parka je bio 1.60), `--synty` netaknut 0 grešaka / 21 ulica; crtež jezgra sa 2 parka, 0 grešaka iz prvog deljenja | **urađeno** |
| 5 | harness `CoreDemo` 40 kola 180 s ×5 | PASSED | čeka |
| — | ušća parka **preko puta zebre** u jezgru (`Edge.Crossing` se u jezgru još ne prosleđuje) | ušće na raskrsnici | čeka |
| 6 | pojas parkova oko jezgra | **30 seedova 30/30 čisto**, prosek 4.43 deljenja; `--synty` netaknut | **urađeno** |

### 5.1 Šta je sweep pokazao (i šta je zbog toga promenjeno)

Sweep je **cela poenta** ovog koraka: generator koji radi na merama za koje je pisan, a pada na
25 × 150 m, pao bi prvi put kad mu kvart doda nezgodan pravougaonik — a pojas oko jezgra
(§4) se **od takvih i sastoji**. Prvi prolaz: **330 od 676 čisto (49 %)**. Redom kako se lomilo:

1. **Plato fontane se spajao sam sa sobom.** Popločana ćelija se broji kao „prohodna", pa je
   fontana tražeći najbližu stazu našla pločnik pod sopstvenim nogama, spojila se sa sobom, i
   plato je ostao ostrvo do kog staza nikad ne stigne. **346 od 676 mera.** Sada se veza traži
   **pre** nego što se plato položi, i traži se ćelija STAZE, ne bilo šta prohodno.
2. **Presuda je lagala.** Soba koja se nije mogla preseći „rešavana" je tako što joj se W/D
   smanji na prag — pa je izveštaj javljao 0 grešaka nad parkom koji ima 55 × 80 m praznog
   polja. Zamenjeno zastavicom `Uncut`: mera ostaje prava, greška se vidi.
3. **Staza je bila drvo** → soba uz ogradu se ne može preseći (v. petlja, §2.2).
4. **Pravougaonik je bio pogrešna mera** za „prevelika soba" → domet do staze (§2.3).
5. **Rez nije umeo da otvori kapiju**, jer je gledao ćeliju IZA ivice — a soba koja već dodiruje
   ogradu nema kuda da zakorači. Sada rez sme da završi na ogradi **novom kapijom** (≥ 20 m od
   postojeće), što je i ono što pravi park radi sa travnjakom uz rešetke.
6. **Kapija se otvarala i kad rez padne.** `Gate` je menjao plan čim izračuna jedan kraj; ako bi
   drugi kraj pao, ostajala je kapija sa jednom ćelijom staze koju ništa ne dodiruje. Kapije se
   sada otvaraju tek kad se ceo rez polaže.
7. **Rez je smeo da otvori kapije na OBA kraja** — savršeno ispravna staza od ulice do ulice
   koja ne dodiruje ostatak parka. Sada najviše jedna po rezu: drugi kraj mora stići do
   postojeće staze, i time je staza uvek jedan komad.
8. **Strip se ulazio sa dugih strana** pa je 200 m parka dobijalo stazu preko sredine i praznu
   polovinu. Sada prve dve kapije stripa idu na **krajeve**.

Posle svih osam: **676/676 čisto na svakom probanom seedu.**

### 5.2 Šta je prvi CRTEŽ pokazao (2026-08-26)

Crtež je stao tek pošto je sweep bio čist — i pokazao tri stvari koje sweep po definiciji ne
može da vidi, jer sweep sudi planu, a ovo su greške u **komadima**:

1. **Palme su bile u parku.** Palma je 8–10 m široka prema gradskom stablu od 2 m; tri u parku
   od 30 m su pojele pola placa (klupe i stolovi su odbijani jer nije bilo mesta) i dve su
   visile preko ograde. **Provera na Syntyju**: block-08 ima dve palme i **obe su na
   trotoaru**, izvan ograde, gde ih `CorePavement` ionako sadi. Unutar ograde je City
   `Tree_01/02/03` i ništa drugo. Palme izbačene iz `ParkBlocks`.
2. **Staza nije bila zauzeto tlo**, pa je jedno stablo stalo na nju. Sada se rezerviše — ali
   **STAZA, NE ĆELIJA**: pločica je 5 m, a napravljen put kroz nju je ~2.6 m, i baš na uglove
   te pločice Synty stavlja lampu i cveće. Rezerviše se kvadrat u sredini + krak ka svakoj
   strani na koju se staza nastavlja; ostatak ćelije je trava. (Prvo je rezervisana cela
   ćelija — i park je ostao bez ijedne lampe, sve odbijene.)
3. **Soba nije svoj pravougaonik.** Park sa obodnom petljom ima jednu **prstenastu** sobu
   trave: njen bounding box je ceo park, a široka je dve ćelije svuda. Program se dodeljivao
   po bounding boxu, pa su fontana, igralište i tereni redom dobijali prsten i redom bili
   **tiho odbijeni** kad dođu da stanu — park od 80 × 70 m je izašao sa sedam imenovanih soba
   i **nijednim programom u sebi**. Sada se meri **najveći upisani pravougaonik** (histogram
   sweep), i to isto meri i kompozer kad postavlja.
4. **Travnjak je uzimao najveću sobu** pa glasnim programima nije ostajalo mesta (na 80 × 70 m
   nije bilo fontane). Obrnuto: **programi biraju, travnjak je ostatak** — a ostatak je uvek
   pojas i ćoškovi, koje ionako nijedan program ne traži. Jedna soba se uvek zadrži.

5. **Plato je bio rezervisan pre nego što fontana i spomenik stignu da stanu na njega.** Staza
   se rezerviše da niko ne stane na nju — ali plato je tlo koje se pravi ZA fontanu, pa je
   fontana bila odbijena na sopstvenom pločniku, bez reči. Park od 80 × 70 m je izašao sa
   golim kvadratom kamena od 15 m u sredini. Plato se sada rezerviše **posle** programa.
6. **Plato se brojao kao slepi kraj staze.** Nije — plato je **odredište**: staza koja vodi do
   spomenika i tu se završava je stigla. Kao slepi kraj obarao je 128 od 676 mera radeći baš
   ono zbog čega postoji.
7. **Spomenik od 1 m je stajao nasred kamenog kvadrata od 15 m.** Plato se sada meri prema
   onome što na njemu stoji: 3×3 ćelije za fontanu (4.77 m + klupe okolo), 1 za spomenik.

Ostalo doterano po meri sa Syntyja: gustina drveća (bila 3.9/100 m², cilj 2.2), klupe (17 na
80 m parka → jedna na 5 ćelija staze), cveće (387 komada → kapirano), stolovi (1 na 125 m²).

### 5.3 Šta je review našao u kompozeru (pre nego što je išta stajalo)

- **`OnWalk` se nikad nije postavljalo** — presuda je uvek javljala „0 stabala na stazi" jer to
  polje niko nije punio. Isti greh kao (2) gore. Sada se broji **po onome što je stvarno stalo**:
  prođe se kroz posađena stabla i meri se da li im stablo (ne krošnja — pod krošnjom se prolazi)
  pada na ćeliju staze.
- **Cveće je niklo u pesku igrališta i u fontani**: cveće ne zauzima tlo (namerno — klupa pored
  cveća je i dalje klupa), ali mora da **poštuje** tlo koje neko drugi drži.
- **Ograda je stajala 22 cm unutar linije** — panel je polagan od linije, a Syntyjev je
  **centriran na njoj**.

### 5.4 Park u jezgru: greška koja je zamalo naučila pogrešnu lekciju

Kad su parkovi ušli u deljenje jezgra, čistih deljenja je palo sa **72 % na 24 %**. Očigledno
čitanje — „park je prosto teško smestiti" — bilo je **pogrešno**, i pre nego što je uzrok nađen
izmerene su tri uverljive popravke, svaka gora od prethodne:

| pokušaj | rezultat |
|---|---|
| park staje u bilo koji red bez obzira na dubinu | 14 % |
| dubina parka iz opsega redova umesto iz klase | 21 % |
| park ne sme biti jedinica koja određuje dubinu reda | prosek 5.5 deljenja |

Pravi uzrok: **parkovi su ulazili u raspored ali ne i u listu koju raster crta.** Deljenje bi
napravilo mesta za park u redu, ništa ne bi popunilo to tlo, i presuda bi ga s pravom prijavila
kao prazno. Kad parkovi odu i rasteru (`CoreLayout.WithParks`): **60/60 seedova čisto, prosek
1.33 deljenja** — bolje nego jezgro bez parkova (1.60).

Pouka je zapisana u kodu: *doterivanje protiv pogrešne dijagnoze pomera brojeve i ne popravlja
ništa.*

### 5.5 Pojas nije zaseban mehanizam — pojas je RED PARKOVA

Korisnik 2026-08-26: *„hocu da kad otvorim coredemo da mi se iscrta kore i park oko njega"*.

Prvi nacrt (§4) je pojas zamišljao kao svoju mašineriju: `ParkBelt` koji čita raster jezgra,
nalazi gde ulice izlaze, seče pojas na komade i deklariše park drive. Sve to je **već
napisano** — u `CoreLayout.Roll`, za redove blokova. Pojas je zato **red kao svaki drugi**,
samo što su mu sve jedinice parkovi:

- ulica između svaka dva parka — mašinerija reda je stavlja sama;
- ulica **iza** reda deklarisana preko cele širine (`Plan.Bands`) — to JE „park drive", i to je
  linija na koju će rezidencijalni redovi pinovati pročelje;
- raster ga sudi kao i sve ostalo, bez ijedne izmene u `CoreRoads`.

Dva reda se dodaju **poslednja**, pa ih naizmenično slaganje sever/jug postavi na **suprotne
strane i najdalje od bulevara** — što su tačno dve strane koje je korisnik tražio. Jedan je
**veliki** (15–22 ćelije = 75–110 m: gradski park sa svim programima), drugi obična traka
(6–8 ćelija = 30–40 m).

**JEDAN NEPREKIDAN PARK PO STRANI** (ispravka 2026-08-26). Prvo je pojas deljen na 3–4 parka
od 50–80 m sa ulicama između — po rezonu da je ploča trave od 200 m nešto što se ne može
preći. Korisnik je pogledao crtež i tražio suprotno (*„moze li ovo da je jedan neprekidan park
sa strane umesto 3 4 mala"*), i po slici je u pravu: niz malih parkova razdvojenih ulicama
čita se kao **preostalo tlo između blokova**, dok se jedna neprekidna zelena ivica čita kao
**ono na čemu grad prestaje** — a to pojas i jeste. Prelazi se **stazom**, ne ulicom, a park te
veličine `ParkWalk.Cut` ionako sam iseče na sobe.

Mera: **30/30 seedova čisto**, prosek 3.57 deljenja, max 15 od 40. Synty referenca netaknuta.
Sweep proširen na **300 × 300 m** (1296 mera po seedu) jer je pojas veći od svega što je prva
verzija probala: **3888/3888 čisto** — uz jednu popravku koju je to otkrilo, `Cut` je stajao
posle 40 rezova, a park od 270 m ih traži četrdesetak samo za sobe.

## 6. Život u parku (faza 2, posle pešaka jezgra)

Jezgro još nema pešake (`core-district-plan §2.2`, nije otkačeno). Kad dođu: **staza parka
je `PedLink` graf odmah** (ćelije su čvorovi, ušća su spoj na ivičnjak i zebru), klupe su
sedišta (obrazac `BenchSeats`/`SitRoutine` iz starog koda kao referenca, ne prenos), deca
kod igrališta, tenis na terenima ako Synty ima animaciju (nema — onda niko ne igra, tereni
stoje prazni; ne izmišlja se). Hot-dog ima red od 1–2 (`FuelCustomer` obrazac).
Gangsterski kuk: park je mesto gde se **dila i gde se čeka** — `CrewWalker` cilj „klupa u
parku" — pravila ostaju prazni hook-ovi (`tactical-map-1987`).

---

## 7. Odluke (uzete 2026-08-26)

1. **Okvir, i to na najmanje dve strane** — ne pun prsten. Razlog je korisnikov i jači od
   estetskog: pojas je **prelaz ka rezidencijalnom**, pa mora da stoji tamo gde rezidencijalno
   naleže. Dubina po strani ostaje parametar; jedna strana je velika.
2. **Grad v3**: jezgro + pojas + rezidencijalni kvartovi, komponovano iz generisanih kvartova.
   Grid `Game.unity` ostaje dok v3 ne stane.
3. **Mešanje paketa je dozvoljeno**: igralište (Town) i tereni (PalmCity) ulaze u `park` klasu,
   kao što jezgro već nosi palme iz PalmCity i kamion iz Towna.
4. **Skejt-park i toalet** iz CityKit-a su programi. Skejt-park je **9×7 ćelija** (izmereno
   40.5 × 31.5 m, ne 6×6 kako je prvo napisano) i ide jednom po gradu; toalet **3×2**
   (10.6 × 5.5 m) leđima na ogradu.
5. **Parkova u jezgru: 1–2**, `pocket` 70 % / `square` 30 % (Synty ima jedan na 16 blokova).
