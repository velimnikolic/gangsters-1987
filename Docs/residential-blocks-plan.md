# Plan: generator rezidencijalnih blokova (ResidentialBlocks)

> Predlog 2026-08-26, **odobren istog dana** (sve odluke §7 uzete). Građa: šest zgrada koje je korisnik
> imenovao u `Assets/Scenes/CoreHarvest.unity` (`residential1` … `residential6`), Synty gramatika
> bloka iz `Docs/synty-demo-anatomy.md`, i tri generatora koji već rade po istom obrascu
> (`IndustrialBlocks`, `ParkBlocks`, `QuayBlocks`).
>
> U jednoj rečenici: **rezidencijalni blok je elastičan blok kao park** — kvart mu da
> pravougaonik u 5 m ćelijama, recept ga **komponuje na licu mesta iz šest požnjevenih
> zgrada** po Synty pravilima (ivičnjak-prsten, otisak jedinice na unutrašnjoj ivici trotoara, ugao
> nosi ugaonu zgradu, unutrašnjost je dvorište / uličica / parking, nikad prazno tlo) — i to
> **retko**: redovi sa prekidima, bašte, drveće, jedan ćošak sa radnjom. Ne ulazi u jezgro;
> stoji spolja, iza pojasa parka (grad v3: jezgro → park → rezidencijalno).

| šta | gde (novo) |
|---|---|
| žetva šest jedinica iz harvest scene → prefabi + izmerena tabela | `Assets/Scripts/Editor/ResidentialHarvest.cs`, meni `Tools/City/Residential/Bake Named Buildings` → `Assets/Prefabs/Residential/residential-01..06.prefab` + `Assets/RoadDemo/ResidentialUnits.cs` (tabela, prepisuje je žetva) |
| deoba bloka (recept po meri, čist C#) + presuda | `Assets/RoadDemo/ResidentialLot.cs` (kao `ParkWalk`: bez Unity tipova, `Tools/CoreSim` ga kompajlira) |
| komponovanje (instanciranje po planu) | `Assets/RoadDemo/ResidentialBlocks.cs` na zajedničkom `Composer`-u (`Assets/RoadDemo/Composer.cs`: `Begin(raise)`, `Tile/Sit/Stand/Prop`, `Room` odbijanje, `Missing`) — isti oblik kao `ParkBlocks.Compose` / `IndustrialBlocks` |
| laboratorija | `Assets/Scenes/ResidentialLab.unity` **učitana aditivno** (kao `IndustrialBlockForge.Lab()`) + `Assets/Scripts/Editor/ResidentialSketch.cs`, meni `Tools/City/Residential/Sketch A Block` |
| komanda | `unity command gangsters_residential --seed N --size corner\|row\|block\|court\|WxD [--count M] [--draw]` u `PipelineCommands.cs` |
| presuda bez editora | `Tools/CoreSim --residential` (30 seedova × 4 klase = sud; jedan seed ne dokazuje ništa) |
| kvart (redovi blokova na park drive-u, lane graf, harness ≥ 5 runova) | **nije u ovom planu** — posebna faza, v. §3 i §5 |

## Stanje

- [x] odluke §7 uzete 2026-08-26 (kit kafić: **da**; trotoar **1 pločica**)
- [x] **žetva (2026-08-26)**: `ResidentialHarvest` napisan i pušten — 6 prefaba u
      `Assets/Prefabs/Residential/`, tabela `Assets/RoadDemo/ResidentialUnits.cs` generisana.
      Izmereno, ne procenjeno: 5 ugaonih + 1 kroz blok, mere i lica u §0.1; tri kvara same mere
      nađena i popravljena pre nego što je išta stajalo (§0.4). Prefabi stajali u sceni i
      pogledani: brownstone nosi svoje stepenište, rov i drveće, L-ovi su celi
- [x] **deoba i presuda (2026-08-26)**: `Assets/RoadDemo/ResidentialLot.cs` (čist C#) +
      `Tools/CoreSim --residential`. **120/120 čisto na oba opsega seedova** (1–30 i 500–530),
      **0 praznih ćelija** na svih 240 blokova. Pet kvarova mere nađeno i popravljeno u sweep-u
      (§5.1)
- [x] **kompozicija i demo scena (2026-08-26)**: `Assets/RoadDemo/ResidentialBlocks.cs` na
      `Composer`-u + `Assets/Scripts/Editor/ResidentialSketch.cs`;
      **`Assets/Scenes/ResidentialDemo.unity` = 5 blokova, 0 kvarova**, meniji
      `Tools/City/Residential/Sketch A Block` i `Demo Scene (five blocks)`
- [x] pogledano i ocenjeno (2026-08-27): **šest primedbi** na prvi crtež, sve ugrađene — §5.3;
      CoreSim 120/120 čisto, kafić na 76/120 blokova, najveća jedinica ≤ 24 % (`block`) / 35 % (`court`), uličica odbijena na 4/120
- [ ] kvart (sledeći plan)

---

## 0. Šta postoji (analiza, 2026-08-26)

### 0.1 Šest jedinica u harvest sceni (izmereno u živom editoru)

Svaki komad zgrade (zid, ugao, krov, požarne stepenice, prop) nosi ime `residentialN`; grupa =
jedna zgrada. **Ime je ugovor** — sedma zgrada nazvana `residential7` sutra samo uđe u tabelu.

**Tabela je izmerena žetvom** (`ResidentialHarvest`, 2026-08-26), ne procenjena. „Otisak" je
raster koji jedinica zauzima (zid + sopstveno dvorište), „lica" su strane sa ulazima, „prepust"
je ono što visi preko otiska (požarne stepenice, tende).

| jedinica | odakle | otisak | m | vrsta | lica | vrata | radnje | stepeništa | stabla | h | prepust J/I/S/Z |
|---|---|---|---|---|---|---|---|---|---|---|---|
| residential-01 | block-04 | 5 × 4 | 25 × 20 | Corner | S, Z | 0 | 8 | 0 | 0 | 15.6 | 0.7 / 0.5 / 2.0 / 2.0 |
| residential-02 | block-04 | 8 × 5 | 40 × 25 | Corner | J, Z | 0 | 11 | 0 | 0 | 18.6 | 1.8 / 1.8 / 0.5 / 1.7 |
| residential-03 | block-10 | 9 × 5 | 45 × 25 | Corner | J, Z | 0 | 12 | 0 | 0 | 18.6 | 3.3 / 1.8 / 0.7 / 1.8 |
| residential-04 | block-07 | 5 × 5 | 25 × 25 | Corner | I, S | 7 | 0 | 8 | 6 | 15.2 | 1.1 / 0.1 / 0.1 / 1.8 |
| residential-05 | block-12 | 4 × 9 | 20 × 45 | **Through** | I, Z | 14 | 0 | 16 | 4 | 16.7 | 0.0 / 0.1 / 0.0 / 0.1 |
| residential-06 | block-12 | 10 × 9 | 50 × 45 | Corner | I, S | 6 | 12 | 0 | 0 | 18.2 | 1.8 / 1.8 / 1.8 / 0.3 |

Otisci (sever gore; `#` zgrada, `:` sopstveno dvorište ukopano ispod −0.6 m, `.` tlo bloka):

```
residential-01   residential-02   residential-03   residential-04  residential-05  residential-06
   #####           ##......         ##.......          :::::            ::::         ##########
   #####           ##......         ##.......          ####:            :##:         ##########
   ##...           ##......         ##.......          ####:            :##:         ........##
   ##...           ####.###         #########          ..##:            :##:         ........##
                   ########         #########          ..##:            :##:         ........##
                                                                        :##:         .........#
                                                                        :##:         ........##
                                                                        :##:         ........##
                                                                        ::::         ........##
   ugao, radnje    L, radnje        L, radnje        ugao, stepeništa  red kroz blok  veliko L
```

Šta se iz toga vidi:

* **residential-01/02/03/06** su stambene zgrade **nad radnjama** — nula stambenih vrata na
  ulici, 8–12 radnji u prizemlju: pročelje je izlog. residential-02/03 su **L** sa dugačkim
  južnim krilom i kratkim zapadnim, residential-06 je veliko L 50 × 45.
* **residential-04/05** su **brownstone**: 7 i 14 vrata, 8 i 16 stepeništa, drveće u bašti,
  nijedne radnje. residential-05 je jedina **kroz blok** — lica na istoku i zapadu, zid je samo
  2 ćelije (10 m) širok, a **sve oko njega je njegovo dvorište** (`:`), ukopano na −1.5 m: to je
  predbašta sa ogradom, i ona ide i oko krajeva reda.
* residential-01 je jedina zatečena **van rastera** (1.45 m po x, 0.54 po z); žetva je to
  izmerila, izvadila pri pečenju i prijavila.

Ono što se iz mera i slika vidi i što recept sprovodi:

- **Modul je 5 m ćelija, sprat 3 m, krov 0.5–0.74 m.** Zidovi (`Apartment_01/02/03`, `Corner_01–03`,
  `Door_01/02`, `Stack_01–03` = tri sprata u komadu, `Shop_01–06`, `Shop_Corner_01/02`) stoje na
  višekratnicima 5 m, rotacije 0/90/180/270. Zgrada je **ljuska od fasadnih panela**, iznutra
  prazna — zato se ne seče između modula bez zatvaranja reza (v. §7.1).
- **Dva tipa**: *stambena nad radnjama* (1, 2, 3, 6: radnje = prizemlje, tende, natpisi, ATM) i
  *brownstone* (4, 5: podignuto prizemlje, stepenište do vrata na +3 m, vrata podruma na 0,
  **rov predbašte −1.5 m sa ogradom na liniji trotoara**, drveće u bašti). Brownstone nosi svoju
  baštu: ograda stoji tačno na unutrašnjoj ivici ivičnjaka, kao u Synty demou.
- **Prepusti**: izmereni po strani (tabela gore) — do **3.3 m** (požarne stepenice i tende
  residential-03). Vise nad trotoarom od y ≥ 3 m. Dozvoljeno (Synty to radi), ali uz trotoar od
  5 m (§7.7) prepust od 3.3 m ostavlja 1.7 m prolaza: **strana sa najvećim prepustom ne ide na
  najužu ulicu** — to ulazi u presudu kao mera, v. §2.4.
- **Konvencija modula, izmerena preko `FacadeFinder`** (svaki `SM_Bld_*` prefab stajao u
  koordinatnom početku): pivot je **SI ugao ćelije** (na yaw 0 modul puni x −5..0, z −5..0),
  **lice je na +Z**, sprat je 3 m, `Stack_*` je tri sprata u jednom komadu, `Shop_03` je
  **dupli** modul (10 m). Izuzeci koji nisu pročelja: `Corner_02` (unutrašnji ugao) i tende
  čitaju +X.
- **Stepenište `Stairs_01/02` seže celu ćeliju (5 m) preko zida i pada na −1.5 m.** Zato
  brownstone **ne stoji pročeljem na ivičnjaku**: na ivičnjaku mu je **ograda**, a zid je ćeliju
  unutra. Zbog toga je izmenjeno pravilo pina u §2.5.
- Svaka jedinica ima **lice** (strana sa vratima/radnjama/stepeništem) i **leđa** (goli zid ili
  krilo). Leđa nikad na ulicu.

### 0.2 Šta je već probano

- **`Assets/Prefabs/CoreBlocks/noncoreresidential.prefab`** — korisnikov probni bake tray-a sa
  residential1 + residential2 (block-04, `BlockLotTag` 55 × 60) i **generisanim trotoarom**
  (`CorePavement`: 81 ploča, 33 ivičnjaka, 4 rampe, 8 lampi, 3 slivnika). Dokaz da
  `CorePavement` već ume da opasa ove zgrade. Nije blok — nema unutrašnjost — i ne koristi se
  dalje; ostaje kao proba.
- **`building-res-01..06`, `building-apartment-01..03`** u `Assets/CityKit/Buildings` su stari
  kitbash iz PalmCity (`SyntyKitBash`, `SyntyKitExtractor`) — **drugi paket, ne koriste se ovde.**
  Nova imena ne smeju da se sudare sa njima: jedinice se zovu `residential-0N`, ne `building-res-*`.
- **`SuburbDistrict`** (PolygonTown kuće, organske ulice) je predgrađe — niža gustina, druga
  gramatika, drugi paket. Ovaj generator je **između jezgra i predgrađa**: američki „streetcar
  suburb" / spoljni kvart 1987 — brownstone redovi, trospratne kuće nad radnjom na ćošku.
- **Synty demo** ima tačno tu građu na dva mesta: **block-12** (90 × 100: stambeni obod +
  dvorište-park + red sa stepeništima) i **block-06 / block-07** (60 × 60 L + 35 × 35 u useku,
  između njih **uličica od 5 m**, unutra parking). To su dve unutrašnjosti koje recept zna:
  *dvorište* i *uličica + parking*.

### 0.3 Pravila iz memorije koja ovde važe

- **Blokovi od klastera, nikad isečena zgrada** — jedinica se uzima cela; nema pojedinačnog
  panela iz zgrade. Jedini „singl" su kit storefront-ovi (`building-cafe`, `building-coffeeshop`…)
  pročeljem uz ivicu.
- **Fasade nisu uvek +Z** — lice se meri (`FacadeFinder`), ne pretpostavlja.
- **Ako ne znaš — ostavi prazno**: ćelija kojoj recept nema šta da da ostaje prazna **i prijavi
  se** (mera, mesto). Ne izmišlja se sadržaj.
- **Ne diži zgrade, ne crtaj pod ispod njih**: jedinice 4 i 5 idu do −1.5 m — pod se ne polaže
  ispod njihovog otiska ni ispod rova predbašte.
- **U blokove samo katalog** — dvorište, uličica i parking se popunjavaju *kataloškim* prefabima
  po receptu (kao park), ne proceduralnim mešom; jedina generisana stvar je trotoar
  (`CorePavement`, već postoji).
- **Pod bloka miran** — jedna pločica po površini (trava = `Grass_01`, dvorište/parking = jedna
  vrsta).

---

### 0.4 Šta je žetva pokazala (2026-08-26)

Mera je tri puta bila pogrešna i tri puta popravljena **pre nego što je išta stajalo** — po
pouci iz parka (presuda mora da meri ono što broji):

1. **Otisak po pivotu je pogađanje.** Prvo čitanje je ćeliju uzimalo iz pivota modula
   (pivot + 2.5 m). `Shop_03` je dupli modul, stepenište je duplo duboko, žardinjera nema pivot
   u uglu — residential-01 je zbog toga izmeren za ćeliju uži, sa „prepustom" od 2 m koji je u
   stvari bio zid. Sada se ćelija čita **iz kutije komada** (ćelija je puna kad joj je centar u
   kutiji), isto kao `CoreLayout.Shape`; prepust od 0.8 m ne može da uzme susednu ćeliju.
2. **Rov ispod cele zgrade.** Kutija stepeništa ulazi i pod kuću, pa je pravilo „pada ispod
   −0.6 m" označilo svaku ćeliju zida: **ceo residential-05 je izašao kao rov**. Ćelija na kojoj
   zgrada stoji **nikad nije rov** — rov je predbašta u koju se stepenište spušta.
3. **Svaka zgrada je gledala na sve četiri strane.** Lice se prvo brojalo po tome kuda modul
   gleda; unutrašnji ugao L-a je pun izloga koji gledaju **u usek**, pa je residential-03
   prijavio 6 radnji na severu — a severna linija ima **2 ćelije**. Sada ulaz važi za stranu
   samo ako mu je put do ivice **preko sopstvenog tla** (dvorište sme, tuđe tlo ne), a strana je
   lice kad nosi **bar dva ulaza i bar trećinu najbolje strane**. Na tim brojevima svih šest
   pada tačno: 3 i 4 protiv 1 (res-01), 7 i 3 protiv 1 i 1 (res-03), 6 i 6 protiv 1 i 1 (res-05).

## 1. Šta je „low density residential" u američkom gradu 1987 (pravila koja recept sprovodi)

Ovaj kit **nema** porodične kuće — to je PolygonTown i `SuburbDistrict`. Sa šest walk-up zgrada
„retko" znači **red sa prekidima, ne ulični zid**:

1. **Ćošak nosi zgradu.** Svaki ugao bloka koji gleda na dve ulice dobija ugaonu jedinicu
   (1, 4) ili L (2, 3, 6). Prazan ćošak je greška.
2. **Ivica između ćoškova je red sa rupama.** Red brownstone-a (5) uz dugu ivicu; ostatak ivice
   koji ne prima celu jedinicu **nije problem koji se rešava sečenjem — to je program**:
   - 1 ćelija (5 m): **prilaz** — asfalt, i to samo ako **iza ima šta da opslužuje** (uličicu
     ili parking) u najviše 6 ćelija; ako nema, rupa je popločana, ne prilaz,
   - 2–4 ćelije: **popločana rupa** (`Sidewalk_01`), **nikad trava** (odluka 2026-08-26),
   - ≥ 5 ćelija: **„izvađen zub"** — mali parking **kakav parking jeste** (2026-08-27): ulaz kroz
     prsten trotoara, **prolaz** duž reda iza trotoara, mesta s obe strane prolaza nosom ka njemu
     (`Road_ParkingLines_01`, 10 × 5, tri mesta, položena DUŽ reda — slagana u dubinu pravi pruge
     preko celog placa); neparna ćelija na kraju reda = go asfalt, ne pola mesta.
3. ~~**Radnja samo na ćošku, i to na jednom.**~~ **POVUČENO 2026-08-27 na korisnikovu reč
   („ma bez ograničenja") — i nikad nije ni bilo njegovo pravilo, nego moja pretpostavka
   upisana u ovaj plan.** Jedinice 1/2/3/6 imaju radnje u prizemlju; ograničenje „jedna radnja
   po bloku" ih je isključivalo iz celog bloka čim ćošak uzme radnju, pa je od šest kuća
   ostajalo dve — izmereno: **jedna zgrada = 63 % svih jedinica u gradu, dve = 88 %**. Sada
   **nema ograničenja** (`ResidentialLot.ShopsPerStreet = 0`); arterijski ćošak i dalje prvo
   traži radnju, jer je tu saobraćaj.
4. **Kafić.** Jedan po bloku najviše: ili je to radnja u prizemlju ugaone jedinice (natpisi
   `Sign_Cafe`, `LargeSign_Coffee` već su tu) ili kit storefront (`building-cafe` /
   `building-coffeeshop`) u rupi na arteriji, pročeljem uz ivicu (pravilo BlockFabric-a: 1–3 po
   bloku — ovde 0–1). Kit kafić je PalmCity stil; v. §7.2.
5. **Unutrašnjost po dubini** (ono što ostane iza pročelja):
   - 0–1 ćelija: ništa (jedinica 5 je „kroz blok" — dva lica, nema leđa);
   - 2–3 ćelije: **zadnja dvorišta** (**beton**, ne trava — 2026-08-27; ograde `SM_Env_Fence_01` između, veš-konopci, kante,
     kartonske kutije) i **uličica 5 m** kroz sredinu (`Road_Bare_01` + strelica, jednosmerna, kao
     u jezgru) sa **kontejnerima** (`SM_Prop_Skip_01/02`, `Dumpster_01`) uz leđa zgrada;
   - ≥ 6 ćelija: **dvorište-park** kao u block-12 — `ParkBlocks` klasa `pocket`/`square` u sredini
     (trava + staza + stabla + klupe), ulazi kroz rupe u redu. Uličica i dalje ide do parkinga iza
     radnje.
6. **Parking**: uz ivičnjak u traci (ulica od 15 m ima parking-traku, `RoadDemoBuilder.ParkLane`) i
   3–6 mesta iza ćoška sa radnjom. Nema velikih placeva — to je jezgro („plići blok = parking iza").
6a. **NIJEDAN put unutar bloka ne dodiruje meko tlo, i nijedna kola ne prelaze trotoar**
   (odluka korisnika 2026-08-26, gledajući prvi crtež):
   - svaki potez kojim se vozi (uličica, prilaz, parking) **oivičen je trotoarom i ivičnjakom**
     na svakoj strani gde se dodiruje sa dvorištem, poploćanom rupom ili parkom. Jedina strana
     koja to ne dobija je ona gde je sused **zid** — ćelija se ne može podvući pod kuću;
   - **ušće seče prsten trotoara**: ćelija prstena ispred prilaza prestaje da bude trotoar i
     postaje asfalt, pa se sa glavne ulice ulazi **neprekidno**. Isto pravilo koje
     `CorePavement` već sprovodi za jezgro („a drive is road, right up to the carriageway");
   - uličica se zato deklariše kao **koridor od 3 ćelije** (trotoar / kolovoz 5 m / trotoar),
     a ne kao gola linija asfalta. Blok kome je kraća strana uža od **13 ćelija (65 m)** ne
     može da primi i takvu uličicu i red kuća sa obe strane — takav blok je **bez uličice**, i
     to se prijavljuje (18 od 30 u sweep-u; v. pitanje §7.8);
   - presuda to meri: **`NoWayIn`** — svaki povezan potez asfalta mora da stigne do ivice bloka
     **po asfaltu**; ako ne može, to je kvar, ne stvar ukusa.

7. **Drveće u baštama, ne na ivičnjaku.** Synty ga tako slaže (jedinice 4 i 5 nose svoje);
   trotoar ostaje `CorePavement` — lampa na 20 m, bolardi ćeliju od ćoška, kanta, hidrant.
8. **Nema praznog tla** (Synty pravilo 4) — ali kad recept nema šta da stavi, ćelija ostaje
   **prazna i prijavljena** (`Empty` u presudi), ne izmišljena.

---

## 2. Kako radi

### 2.1 Žetva: `ResidentialHarvest` (jednom, pa kad se doda jedinica)

`Tools/City/Residential/Bake Named Buildings` u `CoreHarvest.unity`:

- skupi sve objekte sa imenom `residential\d+` (**ime je ugovor**: sedma grupa nazvana
  `residential7` sutra samo uđe u tabelu), grupa = jedinica;
- **pivot** = donji-levi ugao otiska zidova, zaokružen na 5 m raster; hijerarhija **ravna** (svaki
  komad direktno pod korenom, koren nosi `Transform + BlockLotTag`), komadi ostaju prefab instance
  kroz `CoreBlockTray.Restand` (merge ide kroz postojeći `MergedChunk` put kao za blokove jezgra);
  y se ne centrira (rov ide ispod nule);
- **meri** dok jedinica stoji u sceni (prefab se ne meri kao asset) — `CoreLayout.Measure(name, go)`
  daje `Block` sa `Mask0/CW0/CD0/Ground0/MaxH` i `Turn(yaw)`/`Footprint(yaw)`; žetva tome dodaje
  lica i upisuje u `ResidentialUnits.cs` (tabela u kodu kao `CoreLayout.Blocks`; v. §7.3):
  - `w × d` u ćelijama i **maska otiska** (koje ćelije zid zauzima — L-ovi nisu pravougaonici),
  - **lice po strani**: strana sa vratima (`Door_*`), radnjama (`Shop_*`) ili stepeništem
    (`Stairs_*`) = ulična; ostalo = leđa; iz toga `Corner` / `Row` / `Through` (dva suprotna lica),
  - **ukopane ćelije** (`min.y < 0` → rov predbašte; `CorePavement.Underground = −0.6` već ne
    polaže ploču pod ćeliju koja pada ispod toga, pa rov od −1.5 m prolazi bez novog pravila —
    proveriti na jedinicama 4 i 5, ne pretpostaviti),
  - prepusti preko otiska po strani (požarne stepenice, tende) — samo da presuda zna da ne broji
    kao preklapanje,
  - broj vrata (gustina), radnji (ćošak sa radnjom), stabala;
- upiše prefab u `Assets/Prefabs/Residential/residential-0N.prefab` i **izveštaj** u konzolu
  (tabela iznad, ali izmerena). Komanda zove tihu varijantu (kao `BakeQuietly`), nikad meni sa
  dijalogom (modalni dijalog iz komande zamrzne editor).

### 2.2 Mera i klase bloka

Ulaz: pravougaonik `w × d` ćelija (5 m), maska ulica (koje strane su ulica; obično sve četiri),
**arterija** (strana ka glavnoj ulici — nosi radnju/kafić), seed. Trotoar-prsten od jedne ćelije
uzima `CorePavement`; recept radi na **unutrašnjem** pravougaoniku `(w−2) × (d−2)`.

| klasa | unutrašnja mera | sastav |
|---|---|---|
| `corner` | 6–8 × 5–7 (30–40 × 25–35 m) | jedna ugaona jedinica (1 ili 4) ili L (2), ostatak = bočna bašta / prilaz + zadnje dvorište |
| `row` | 4–5 × ≥ 10 (20–25 × 50+ m) | jedinica 5 „kroz blok" (dva lica) sa brownstone ćoškovima (4) na krajevima, rupe = bašte |
| `block` | 10–14 × 12–18 (50–70 × 60–90 m) | dva reda leđima jedan drugom + **uličica 5 m**, ćoškovi 4/4/4/1 (jedan sa radnjom), zadnja dvorišta, kontejneri, parking iza radnje |
| `court` | ≥ 16 × 16 (80 × 80 m+) | obod (L-ovi 3/6 na ćoškovima, red 5 između) + **dvorište-park** (`ParkBlocks`), ulazi kroz rupe |

Mera koja ne upada ni u jednu klasu **ne dobija recept**: vraća se presuda `NoRecipe` sa
merom (kvart bira drugu deobu). Ne pogađa se.

**Udeo (2026-08-27):** na `block` i `court` nijedna jedinica ne sme kutijom da pokrije više od
**50 %** unutrašnje površine (`ResidentialLot.ShareMost`; presuda `Monolith`, mera `Share`). L od
50 × 45 m na bloku 50 × 60 m bio je ceo blok sa redom zalepljenim sa strane („ovaj drugi je
preogroman"). `corner` je jedna kuća i bašta, `row` je jedinica kroz blok — njih pravilo ne pita.

### 2.3 Recept (redosled deljenja — sva bacanja unapred, determinizam u `(seed, blockId)`)

1. **Ćoškovi.** Za svaki ugao koji gleda na dve ulice: ako je ugao na arteriji i blok još nema
   radnju → jedinica 1 (ili L 2/3/6 ako mera prima krilo), inače 4. Rotacija u koracima od 90°
   tako da izmerena lica gledaju na obe ulice (**bez ogledanja** — Synty mirroruje, mi ne).
2. **Ivice.** Između dva ćoška ostaje dužina `L`. Red 5 (10 ćelija) stane ako `L ≥ 10`; ostatak
   `r = L − 10k` je program po §1.2 (prilaz / bašta / zub). Za `L < 10`: cela ivica je bašta sa
   prilazom — i to se **prijavi** kao `Gap`. Rupa je uvek **uz ćošak sa radnjom** (prilaz do parkinga
   iza) ili u sredini (bašta) — seed bira, ali prilaz postoji ako iza ima uličica/parking.
3. **Unutrašnjost.** Po dubini iza pročelja (§1.5): uličica se deklariše **preko cele dužine**
   (pouka jezgra: L-ugao zaključava saobraćaj), izlazi na ulicu kroz prilaz, jednosmerna sa
   strelicom na ušću; dvorišta = ćelije između leđa i uličice; dvorište-park kad dubina ≥ 6.
4. **Kafić** (0–1, 2026-08-27): kit storefront u **popločanoj rupi od ≥ 2 ćelije** — na arteriji ako
   je ima, inače na bilo kojoj ulici (rupa na arteriji je sreća; pola blokova je nije imalo, pa kafića
   nije bilo nigde). `building-coffeeshop` (5,8 × 7,2 m) u rupi od 2–3 ćelije, `building-diner` /
   `building-burger-joint` (16,3 × 8,8 m) u rupi od 4; lice **izmereno** (`Composer.FrontYaw`),
   pročelje 0,3 m od linije trotoara, stoji na y = 0 (`Composer.Building`); ispod dajnera (pod na
   −1,56 m) nema pločice, oko njega beton.
5. **Props** — kataloški, po receptu, na y = 0: kontejneri **na trotoaru uz uličicu (verge), uz leđa
   zgrada — nikad na kolovozu** (2026-08-27: „ako si stavio put ne mozes na put da stavis
   kontenjere"), kese/kutije uz njih, veš-konopci između dvorišta (jedinice 1–3 ih već nose na
   krovu), power box na leđima, ograde između dvorišta i na liniji trotoara uz bašte. Na
   trotoaru-prstenu **palme** u ritmu jezgra (`CorePavement.Plant`, jedna na 10 ivičnjaka, ista
   traka) i lampe — jedino drveće bloka (2026-08-27: „palme po trotoaru").
6. **Pod**: **beton** (`Sidewalk_01`) pod dvorištima, baštama, rupama i oko kafića — **trave nema**
   (2026-08-27); `Road_Bare_01` pod uličicom, prilazom, ulazom i prolazom parkinga; **jedna pločica
   po ćeliji, uvek** — strelica na ušću uličice je cela pločica puta i ne leži preko gole (treperile
   su jedna kroz drugu); **ništa** pod otiskom jedinica, njihovim rovom i praznim ćelijama unutar
   kutije jedinice koja ide u minus (bašta u L-u broja 4: ceo otisak je na −1,5 m).

Izlaz: `ResidentialLot.Plan` = lista postavki (jedinica, ćelija, rotacija), program po ćeliji
(`Yard/Alley/Parking/Drive/Court/Empty`) + `Measures` (§2.4). Crta ga
`ResidentialBlocks.Compose(plan, root, rng, raise)` — **u koordinatnom početku, pa se pomeri**
(kao `IndustrialQuarter.Stand`: svaki komad se postavlja merenjem gde je sleteo, ne kroz
roditelja); `raise` delegat služi i editor (`PrefabUtility.InstantiatePrefab`) i igru
(`Object.Instantiate`); `CardFacts.On` daje klik-karticu. Kocka: samo `System.Random`, seed po
`(seed, blockId)` kao industrija (`seed*7919 + I0*104729 + J0*1299709`).

### 2.4 Presuda (meri ono što broji — pouka parka: nikad sakriti meru)

**Kvarovi** (`Faults`, mora 0 na svih 30 seedova svake klase):

| kvar | kako se meri |
|---|---|
| `OffRaster` | pivot jedinice nije na višekratniku 5 m |
| `Overlap` | dve maske otiska dele ćeliju (prepusti se ne broje) |
| `BackToStreet` | strana jedinice bez lica leži uz ulicu |
| `BlankCorner` | ugao bloka na dve ulice bez jedinice (osim klase `row` gde je to dozvoljeno i prijavljeno) |
| `FloorUnderPit` | pod položen ispod ukopane ćelije |
| `DeadAlley` | uličica bez izlaza na ulicu |
| `TwoShops` | više od jednog ćoška sa radnjom na stambenom bloku |

**Mere** (prijave, ne kvarovi) u `Stood` kao kod industrije/parka: `Units`, `Doors` (gustina:
vrata/ha), `Shops`, `Cafes`, `Gaps` (broj i dužina rupa), `Trees`, `Parking` (mesta), `Alley`
(da/ne, dužina), `Court` (da/ne), **`Empty`** (ćelije bez programa — sa mestom, po
`leave-it-empty`), **`Refused`** (šta je recept hteo a mera nije primila — string, nije kvar),
`Missing` (prefab koga nema — od `Composer`-a), `Pavement` (izveštaj `CorePavement`-a).
`Report(stood)` = jedna linija sažetka + `WARNING:` linija po lošem bloku.

```
ResidentialLot.Roll(w, d, streets, artery, seed) → Plan → Judge(plan) → Faults == 0 ?
      → ResidentialBlocks.Compose(plan, root, rng, raise) → Stood → CorePavement.Around → Report
```

Sud = `Tools/CoreSim --residential`: 30 seedova × 4 klase, `Faults == 0` svuda i `Empty` u
granicama koje klasa obeća (`block`: 0; `corner`: ≤ 2). Program koji recept unosi (dvorište,
uličica, parking) je **tlo koje deoba sama izmišlja** — ako blok ikad uđe u deobu jezgra, ide u
raster kroz `WithGround` (pouka §5.4 parka: bez toga čiste deobe padaju sa 72 % na 24 %).

### 2.5 Orijentacija i pin

- Jedinica se okreće tako da joj lica gledaju na ulice; L-ovi obavijaju ugao sami. Rotacija je
  jedina sloboda (bez ogledanja, bez skaliranja).
- **Pin ide po tlu jedinice, ne po zidu** (izmereno, §0.1): jedinica se stavlja tako da joj
  **otisak** dodirne unutrašnju ivicu ivičnjaka. Kod zgrade nad radnjama otisak je zid, pa izlog
  stoji na toj liniji; kod brownstone-a otisak uključuje predbaštu, pa na liniji stoji **ograda**
  a zid je ćeliju (5 m) unutra — tako je i u Synty demou.
- Trotoar: `CorePavement` oko zgrada **na tray-u**, ne preko celog bloka (pravilo tray-a v2) — u
  bloku se generiše po obodu i po ivicama rupa (prilaz dobija `Sidewalk_Dip`). Pojas: Synty meri
  **1 pločicu (5 m)**, projekat podrazumeva 2 (`CoreTray.pavementTiles`); brownstone nosi svoju
  baštu iza ograde, pa je ovde 1 pločica prirodna — v. §7.7. Oblik bloka koji `CorePavement`
  prima: pravougaonik sa najviše 2 useka (`MostNotches = 2`), pa rupe u redu ne smeju da se
  računaju kao usek — rupa je tlo bloka (bašta), obod ostaje pravougaonik.

---

## 3. Gde stoji u gradu

Grad v3 (`Docs/park-plan.md` §4): jezgro → pojas parka na ≥ 2 strane → **rezidencijalni kvartovi**.
Rezidencijalni kvart = redovi blokova pinovani pročeljem na **park drive** (ulica 15 m), ulice
15 m sa parking-trakom, uličice 5 m kroz blokove, tipičan blok klase `block` (60 × 90 m); klasa
`court` jednom-dvaput po kvartu, `row` uz obalu/reku, `corner` gde deoba ostavi mali komad.
Arterija = strana ka park drive-u ili bulevaru (ćošak sa radnjom gleda na nju).

Ovaj plan gradi **blok + laboratoriju + komandu + sud**. Kvart (`ResidentialQuarter : IDistrict`,
lane graf iz rastera, svoja `CoreDemo`-scena, soak ≥ 5 runova — tri kvara čitača puteva koje je
jezgro našlo može svaki novi kvart da vrati) je sledeći plan — isti redosled kao industrija
(`IndustrialBlocks` → `IndustrialQuarter`). Grid `Game.unity` se ne dira. `DistrictKind` još nema
ni `Core`; v3 nema koda — ovaj plan ga ne obavezuje.

Dve mogućnosti za kvart, odluka tada, ne sad: (a) kvart je **red blokova kao u jezgru** —
`ResidentialLot` ulazi u `CoreLayout.Roll` kao elastična jedinica bez prefaba (prefiks `res-`
kao `park-`, `Reshape` na dubinu reda, tlo kroz `WithGround`); (b) kvart ima **svoju deobu**
(`ResidentialLayout.Roll` kao `IndustrialLayout`: redovi uz park drive, parcele po klasi). (a)
dobija ulice, uličice i bulevarsku logiku besplatno; (b) dobija slobodu mera. Sweep §5.1 kaže
koje klase stvarno upadaju u dubine 40–60 ćelija reda jezgra.

---

## 4. Šta se koristi od postojećeg

- **`Composer`** (`Assets/RoadDemo/Composer.cs`) — zajednička osnova `ParkBlocks`/`IndustrialBlocks`:
  keširane kutije prefaba, `Tile/Sit/Stand/Prop`, `Room` odbijanje, `Missing`.
- **`CoreLayout.Measure` → `Block`** — mera jedinice na rasteru (maska, `Turn`, `Footprint`).
- **`CorePavement`** — trotoar oko zgrada (već dokazano na `noncoreresidential.prefab`);
  konstante koje blok nasleđuje: `Covers = 0.35` (koliko ćelije zgrada mora da pokrije da je
  uzme), `Underground = −0.6` (bez ploče pod ukopanim), `MostNotches = 2`, lampa na 20 m
  (`LampEvery = 4`), trake `LampLane 1.0 / KerbLane 1.5 / WallLane 4.0` m, `Deck = 0.054`.
- **`ParkBlocks`** — dvorište-park (`court`) i džepni park u „zubu"; isti raster, ista presuda.
- **`CoreRoads`** raster/pločice za uličicu i parking (`Road_Bare_01`, `Road_Arrow`,
  `Road_ParkingLines_01` 10 × 5, tri mesta) — kao usek u jezgru: preostalo tlo u useku je
  `Kind.Parking`, sve ostalo preostalo je `Kind.Spare` = prazno i prijavljeno.
- **`CoreBlockTray` / `MergedChunk`** put za bake i spajanje mesh-eva.
- **`FacadeFinder`** za kit kafić (lice nije uvek +Z).
- Kataloški propovi: PolygonCity (`Skip_01/02`, `TrashBag`, `CardboardBox`, `Washingline_01–03`,
  `PowerBox`, `Fence_01/End_01`, `Grass_01`, `Tree_01–03`, `Planter`), GangWarfare
  (`Dumpster_01`, `Container_01–03` — v. §7.4).

---

## 5. Alati i redosled rada

1. **Žetva** (§2.1): meni + izveštaj; šest prefaba; tabela izmerena, ne procenjena. *Provera*:
   svaka jedinica stoji na rasteru u `ResidentialLab` sa `CorePavement` okolo i bez poda pod rovom.
2. **`ResidentialBlocks.Compose`** čist C# + presuda; `Tools/CoreSim --residential` sweep 30 × 4.
   Sud pre ijednog crteža (pouka §5.1 parka).
3. **`ResidentialLab` + Sketch + `gangsters_residential --draw`**: jedan blok svake klase iz
   seeda, pogled, popravke **po meri** (nova mera u presudu, ne ugađanje na oko).
4. **Bake u stock** kroz tray (`res-*` blokovi u `Assets/Prefabs/CoreBlocks` ili svoj folder — §7.3)
   ako kvart hoće gotove blokove; inače kvart komponuje na licu mesta kao park.
5. **Kvart** — sledeći plan (§3). Život (pešaci na stepeništu, auta u uličici, kontejneri kao
   zaklon u pucnjavi) posle kvarta.

---

### 5.1 Šta je sweep pokazao (2026-08-26)

Prvi prolaz: **34 % čisto**. Pet kvarova, svaki popravljen u DEOBI, ne u presudi:

1. **Mali blok je tražen na sva četiri ćoška.** Klasa `corner` je po §2.2 *jedna* zgrada i
   njena bašta, a presuda je tražila zgradu na svakom ćošku koji gleda na dve ulice — 182
   odbijanja i 85 kvarova za zgrade koje nikad nisu ni mogle da stanu. Sada svaka klasa kaže
   na koliko ćoškova se gradi: `corner` 1, `row` 2, `block`/`court` 4.
2. **L-zgrada leđima na drugu ulicu.** Biralo se po strani za koju je jedinica *uzeta*; L
   postavljen uz južnu ivicu dohvati istočni trotoar kratkim krilom. Sada svako postavljanje
   proverava **sve četiri strane** (`Fronts`), pa presuda nema šta da uhvati.
3. **Zabat nije leđa.** Prva verzija pravila „leđa nikad na ulicu" odbijala je i **kraj reda**
   — a zabat na poprečnoj ulici je normalan gradski potez i stoji svuda po Synty demou. Devet
   `row` blokova je zbog toga ostalo bez ijedne zgrade. Sada je *leđa* strana **naspram lica**,
   a strana pod pravim uglom na lica je *kraj* i sme na ulicu.
4. **Ugaona radnja pojede susedni ćošak.** Ćoškovi se dele redom i uzima se najveća jedinica
   koja stane, pa je L od 50 × 45 m uzeo ceo blok. Sada postavljanje mora da **ostavi mesta**
   sledećim ćoškovima — i to mesta za pravu **ugaonu** jedinicu (5 ćelija), ne za najužu
   jedinicu u katalogu (4), što je bila druga, tiša verzija iste greške.
5. **Kratka strana pojede dugu.** Jedan prolaz po ivici je odlučivao odmah: južna strana od 4
   ćelije ne prima nijednu jedinicu → bašta duboka 3 ćelije → zapadnoj strani, jedinoj koja
   prima red kroz blok, ostane 7 od potrebnih 9. Sada su **dva prolaza**: prvo zgrade po svim
   ivicama (duža ivica prva), pa tek onda program u ono što je ostalo. Rupa je ostatak, pa se
   ne sme deliti pre onoga od čega ostaje.

Uz to je **uličica prebačena na deklaraciju pre svega ostalog** (kako §2.3 i traži): dodeljena
posle zgrada, morala je da se odustane u 18 od 30 blokova; deklarisana pre njih, zgrade stanu
sa obe strane a oba ušća su na trotoaru po konstrukciji. Linija joj se bira tako da sa obe
strane ostane red kuća — po sredini bez pitanja, blok plići za jednu ćeliju gubi ćošak.

**Sud: 120/120 čisto** (30 seedova × 4 klase), i isto na drugom opsegu seedova (500–530).
Mere po klasi: `corner` 2.8 vrata, `row` 7.7, `block` 30.0, `court` 50.8; **0 praznih ćelija**
u sve 240 deobe.

### 5.2 Šta je prvi crtež pokazao

Scena `Assets/Scenes/ResidentialDemo.unity` (5 blokova, 0 kvarova) stoji i slikana je
2026-08-26. Kvar koji je crtež odmah pokazao: **`Composer` postavlja u svetskim koordinatama**,
pa je root pomeren PRE komponovanja root čija deca ne mare za njega — svih pet blokova je
stajalo jedan na drugom. Kao industrijski kvart: komponuj u koordinatnom početku, pa pomeri.

Ocena izgleda čeka korisnika (v. §7 — estetika je njegova odluka). Šta ja vidim kao slabo:

* **parking programme je krupan** — „izvađen zub" od 5+ ćelija ide 3 ćelije duboko, pa na
  `corner` bloku parking ume da bude veći od kuće;
* **dvorišta su i dalje mirna** — trava, ograda uz uličicu, po koja kanta i veš; nema šupa,
  kola u dvorištu ni ograde IZMEĐU dva dvorišta (nema podatka čije je koje);
* **kafića još nema** (§7.2 odobreno, ali deoba još ne postavlja kit storefront u rupu);
* **jedinice se ponavljaju** — 5 ugaonih zgrada u katalogu, od kojih 4 nose radnje, pa na
  bloku sa 4 ćoška stoje dva-tri ista brownstone-a (`Repeats` je u meri).

### 5.3 Šta je korisnik rekao na prvi crtež (2026-08-27) i šta je urađeno

| primedba | uzrok | urađeno |
|---|---|---|
| „ne vidim da stavljas kafice i to igde" | `Use.Cafe` postojao, niko ga nije dodeljivao; §7.2 vezao kafić za arteriju | `ResidentialLot.Cafe`: rupa ≥ 2 ćelije, arterija pa bilo koja ulica; `ResidentialBlocks.CafeOf/CafeStand` bira coffeeshop ili diner po dužini rupe, lice mereno |
| „zajebi travu izmedju, ocu samo beton i palme po trotoaru" | dvorišta/dvorište-park = `Grass_01` + Synty drveće | sve `Sidewalk_01`; drveća u dvorištima nema; palme na prstenu kroz `CorePavement.Plant` |
| „sta znace ove linije? zna se kako parking izgleda" | `ParkingLines_01` slagane po parovima u dubinu preko celog zuba → linije se nadovezuju u pruge | zub = ulaz + prolaz (`Drive`) + redovi mesta nosom ka prolazu; pločica se polaže DUŽ reda, neparna ćelija = go asfalt |
| „preplice se put s necime… izbegavaj dupli layering" | (a) strelica ušća preko gole pločice u istoj ravni; (b) tepih asfalta demo klupe polagan po pivotu, a pivot pločice je u max-uglu → tepih ispod istočnog i severnog trotoara svakog bloka | (a) ušće = samo strelica (`MouthArrow`), `Mouths()` ukinut; (b) `ResidentialSketch.Asphalt` polaže merenjem gde pločica sleti |
| „ako si stavio put ne mozes na put da stavis kontenjere" | skip/smeće na `Alley` ćelijama | `Bins()`: na verge-u uz uličicu, uz suprotnu ivicu (leđa kuća), dužom stranom duž uličice |
| „izbegavaj velike residential blokove, ovaj drugi je preogroman" | ćošak uzima NAJVEĆU jedinicu koja stane → L 10 × 9 (50 × 45 m) na bloku 12 × 14 = 75 % unutrašnjosti | `ShareMost` 50 % na `block`/`court` u `Fit`/`Longest`, presuda `Monolith`; demo klupa na malom kraju klasa (13×15, 8×7, 6×13, 16×16, 12×14) |
| „ispod residential4 ne smes da crtas pod jer ide u minus" | izmereno: ceo otisak broja 4 je na −1,5 m, a 2 × 2 „prazne" ćelije u njegovom L-u (bašta) dobijale su travu na +0,05 | `Place`: prazne ćelije u kutiji jedinice koja ide u minus = `Forecourt` = ništa se ne polaže; `Forecourt` više nikad ne dobija pločicu |

Mere posle: `CoreSim --residential --count 30` **120/120 čisto**, kafić na **76/120** blokova
(corner 20, row 9, block 23, court 24), najveća jedinica **24 %** (`block`) / **35 %** (`court`),
0 praznih ćelija. **Kola na parkingu (2026-08-27)**: jedno mesto od dva dobija auto iz istog bazena iz
kog jezgro puni svoje parkinge (`CoreRoads.PickCar`: kataloška kola bez pogrešne decenije i bez
livreja), nosom od prolaza (`CoreRoads.InBay`); klupa: 6 kola u 12 mesta, nijedno na trotoaru.
Palme su iste koje tray polaže na `CoreHarvest` (`SM_Env_Tree_Palm_01–06` + rešetka, ista
`CorePavement.Sapling`). Usput: uličica je probala samo dužu osu bloka (18/120 blokova bez uličice, među
njima klupa 13 × 15) — sada uzme kraću kad samo ona prima red kuća sa obe strane (4/120). **Drugi krug (2026-08-27, iste večeri):** „zidovi koji nisu završeni" i „veš koji visi u vazduhu" → ograde,
veš-konopci, kante i kutije po dvorištima **izbačeni skroz** (dvorište je go beton; ono za šta recept nema
ništa ostaje prazno). „Patio pored kafića" → `ResidentialBlocks.Patio`: kafić stoji uz **jedan kraj** rupe
(centriran je ostavljao 2 m sa svake strane), a pojas pored njega dobija stolove sa četiri stolice i
suncobranom (kejski `Table_02`/`Chair_01`/`Umbrella_02`, soba 2,4 m — rupa od 10 m minus 7,2 m pročelja
coffeeshop-a je tačno 2,5) i klupu uz zadnju liniju licem ka ulici. „Ne možeš na sred ulaznog puta da
staviš lampu" → lampa se ne postavlja na ćeliju prstena koja nije trotoar (ušće). „Kante okrenute ka
uličici" → poklopac skipa pada ka +z, to mu je lice; okreće se ka uličici, leđima uz zid. Klupa: 10
stolova, 5 klupa, 0 lampi na kolovozu, 0 ograda.

Nije urađeno: `Dumpster_01` iz GangWarfare (samo skip),
istočna strana broja 4 seže 4,6 m preko prstena (stepeništa; `Over` kaže 0,13 — žetva to ne meri).

## 6. Nije deo ovog zadatka

- Sečenje jedinica između modula (zatvaranje reza ugaonim panelima + krovnim uglom) — tek ako
  30 seedova pokaže da rupe ostavljaju previše `Empty`.
- Nove jedinice od modula (sopstveni brownstone od `Stairs + Door + Stack + Roof`) — kit to
  dozvoljava, ali prvo šest korisnikovih.
- Garaže, prilazni putevi u dvorište za auta, dvorišni parking (nema asseta — ostaje prazno i
  prijavljeno).
- Ogledanje (scale −1), skaliranje, boje.

---

## 7. Odluke (uzete 2026-08-26)

1. **Rupe ili sečenje?** **UZETO: samo rupe** (v1) — jedinice cele, ostatak ivice je bašta /
   prilaz / zub. Sečenje reda 5 na 10 m kuće ostaje za v2 ako mere traže.
2. **Kafić iz kita (PalmCity `building-cafe`/`coffeeshop`) u rupi, ili samo Synty radnje u
   prizemlju jedinica 1/2/3/6?** **UZETO: kit kafić DA** — oba dozvoljena, **najviše jedan kafić
   po bloku**, kit samo na arteriji.
3. **Gde žive prefabi i tabela?** **UZETO:** `Assets/Prefabs/Residential/` + tabela u kodu
   (`ResidentialUnits.cs`, kao `CoreLayout` tabela blokova) koju žetva prepiše.
4. **Kontejneri** = `SM_Prop_Skip_01/02` (PolygonCity) i `Dumpster_01` (GangWarfare) u uličici —
   ili i brodski `container-20-*` iz luke? **UZETO: skip + dumpster**; brodski kontejner nije
   stambeni kvart.
5. **Uličica jednosmerna sa strelicom kao u jezgru** (vozi se, ulazi u lane graf u kvartu) —
   ili samo pešačka? **UZETO: jednosmerna**, 5 m, kao u jezgru.
6. **Gustina**: `block` 60 × 90 m sa dva reda ≈ 16–20 vrata (≈ 30–40 vrata/ha) — dovoljno „retko"?
   **UZETO: da**, prvi prolaz ovako. Ako treba ređe: veće bašte (rupe 3–4 ćelije obavezne),
   jedan red umesto dva.
8. **Uličica traži 65 m** (13 ćelija: 4+ kuća / 5 trotoar / 5 kolovoz / 5 trotoar / 4+ kuća) —
   pa je 18 od 30 `block` blokova u sweep-u ostalo bez nje. Da li da uži blokovi (a) ostanu bez
   uličice, (b) dobiju uličicu **bez trotoara** kao Synty (5 m goli asfalt), ili (c) kvart da ih
   deli krupnije da uličica uvek stane? Trenutno je **(a)**, i to je prijavljeno u meri.

7. **Trotoar 1 pločica (5 m, Synty) ili 2 (10 m, podrazumevano u projektu)?** — **UZETO: 1 (5 m).**
   Šta pitanje znači: trotoar oko bloka se generiše kao pojas pločica od 5 m između ivičnjaka i
   pročelja; Synty u demou ima **jednu** pločicu (fasada 5 m od kolovoza), a naš tray za jezgro
   podrazumeva **dve** (`CoreTray.pavementTiles = 2`, 10 m). Predlog: **1** — brownstone već nosi
   baštu iza ograde, 10 m trotoara uz nju je bulevar, ne stambena ulica.

---

## Provere

| provera | rezultat |
|---|---|
| šest jedinica stoji na rasteru u lab-u, lica ka ulici, bez poda pod rovom | — |
| `CoreSim --residential`: 30 seedova × `corner/row/block/court`, `Faults == 0` | — |
| `Empty` ≤ obećanje klase, svaka prazna ćelija sa mestom u izveštaju | — |
| `CorePavement` oko bloka: 0 pločica sa pogrešnim uglom, prilaz ima rampu | — |
| nijedan blok sa dve radnje; kafić ≤ 1; uličica uvek izlazi na ulicu | — |
| pogled u lab-u: jedan blok svake klase, slika u §5.2 | — |
