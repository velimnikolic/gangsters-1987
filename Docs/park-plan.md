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
- **Plato**: ćelije oko fontane/spomenika su `SM_Env_Path_*` (popločano), 3×3 ili 5×5.
- **Invarijante** (`ParkWalk.Check`): graf povezan (svako ušće stiže do svakog); nijedan
  kraj koji nije ušće osim jedne dozvoljene slepe grane u `pocket`; staza ne dodiruje
  ogradu osim na ušću; staza ne seče sobu-program.

### 2.3 Sobe (kako veliki park ne ostane pust)

Unutrašnjost se **deli kičmom i sekundarnim stazama na sobe** ≤ 10×10 ćelija. Svaka soba
dobija tačno jedan program iz tabele, po pravilu a ne kocki:

| program | mera sobe | šta | koliko po parku |
|---|---|---|---|
| `lawn` | bilo koja | prazna trava, po ivici 2–4 stabla | ≥ 1 obavezno |
| `grove` | ≥ 4×4 | 6–10 stabala na 4–6 m, klupa ispod | ≤ 2 |
| `fountain` | ≥ 5×5 | plato 3×3 `Path`, fontana + FX, 4 klupe ka fontani, lampe na ćoškovima platoa | ≤ 1 |
| `playground` | ≥ 5×5 | pesak `Sandpit`, fort/tobogan/ljuljaška/vrteška, drvena ograda sa kapijom ka stazi, klupe spolja | ≤ 1 |
| `courts` | ≥ 5×9 | tenis ×2 na 10 m ili košarka ×1, ograda oko | ≤ 1 |
| `pavilion` | ≥ 4×4 | `Pavilion_01` na travi, klupe u krug | ≤ 1 (`park` klasa) |
| `statue` | 3×3 | plato + `Statue_0x`, cveće u prstenu | ≤ 1 |
| `skatepark` | ≥ 6×6 | `building-skatepark` | ≤ 1 po gradu |
| `toilet` | 2×2 uz ogradu | `building-park-toilet` sa leđima na ogradu | ≤ 1 (`park` klasa) |

`square` = jedna soba = jedan program (nikad `lawn` sam). Redosled dodele: prvo `lawn`
(najveća soba), pa jedan „glasni" program (`fountain`/`playground`/`courts`) u sobu uz
najprometnije ušće, pa ostatak. Program koji **ne staje se izostavlja i prijavljuje** —
soba ostaje `lawn` (ne pogađa se manja verzija).

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

| # | korak | dokaz |
|---|---|---|
| 1 | `ParkWalk` (čist C#) + `CoreSim --park --size WxD --count 30`: ušća, kičma, sobe, invarijante | 30/30 bez `Unreached`, sve klase, i „ružne" mere (5×5, 5×30, 13×28) |
| 2 | `ParkBlocks.Compose` + `ParkLab.unity` + `Tools/City/Park/Sketch A Park` (jedan park zadate mere na nuli, natpis: klasa, mera, programi, gustina) + `gangsters_park --draw` | crtež `pocket` 6×6 **pored** `block-08` pločicu po pločicu: isti tipovi pločica, ograda, ritam nameštaja u ±1 komad |
| 3 | `square` i `park` crteži, sobe i programi; korisnik gleda i doteruje (kao industrija „preveliku su") | snimci plan + kos |
| 4 | jedinica `Park` u `CoreLayout.Roll`, stajanje u `CoreDistrict`, `--synty` netaknut | `CoreSim --seed 1 --count 30` 30/30; `gangsters_core --draw` sa parkom |
| 5 | harness `CoreDemo` 40 kola 180 s ×5 | PASSED kao pre (park ne menja puteve, ali menja raspored — nov raspored, nova mera) |
| 6 | `ParkBelt` (§4) tek posle odluke o v3 | `CoreSim --belt`, crtež jezgro + okvir |

Regresija: `block-08` ostaje u `blocks.txt` i u referenci; nijedna promena u `CoreRoads`
(park ne treba ništa novo od rastera — to je uslov dizajna, kao za industriju).

Pregled koda pre svakog testa (`code-review-unity`, hook), eval/menu tek posle.

---

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
