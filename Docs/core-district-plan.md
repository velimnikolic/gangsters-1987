# Plan: gradsko jezgro v2 po Synty receptu (CoreDistrict)

> Odobreno 2026-08-25. Anatomija demo scene na kojoj plan stoji: `Docs/synty-demo-anatomy.md`.
> Kod: `Assets/RoadDemo/CoreLayout.cs` (raspored + rezovi + uličice), `Assets/RoadDemo/CoreRoads.cs`
> (raster + koridori → pločice), `Assets/Scripts/Editor/CoreCitySketch.cs` (crtež u editoru,
> `Tools/City/Core/Sketch The Core City`). Radna scena za crtež: `Assets/Scenes/CoreHarvest.unity`
> (kopija demo scene bez puteva, sa tray-om i review redom); `Assets/Synty/PolygonCity/Scenes/Demo.unity`
> je vraćen na original.

## Kontekst

Blokovi jezgra su požnjeveni iz Synty PolygonCity demo scene (16 prefaba u `Assets/Prefabs/CoreBlocks`;
`city-hall-block` je duplikat `block-03`). Dva pokušaja da se slože algoritmom (prstenovi oko centra,
mreža sa parcelama) dala su praznine ili nečitljive puteve. Odluka: **Synty raspored kakav jeste**
(svaki prefab se 100 % poklapa sa svojim mestom u originalu), a njihove ulice od 10 m se otvore na
**gradske ulice od 15 m**, glavni put na **bulevar 35 m**, uličice ostaju 5 m jednosmerne.

## Stanje (2026-08-25)

- [x] scena vraćena, radna kopija sačuvana (`CoreHarvest.unity`)
- [x] `CoreLayout`: tabela blokova (pivot u demou), rezovi, uličice sa smerom, merenje bloka (kutija, maska)
- [x] `CoreRoads`: raster + **koridori** (v. 2.2), zebre, medijan, strelice, parkinzi u usecima;
      izveštaj o preostalom tlu (`Raster.Report`)
- [x] crtež u CoreHarvest sceni (2026-08-25, gotovo): blokovi 33.600 m², **put 38.350 m²**,
      parkinzi 2.725 m², **prazno tlo 0 m²**; 33 puta (1 bulevar, 24 ulice, 1 od 10 m — 6 ćelija —
      i 7 uličica). Provereno na živom editoru (`Lay` u preview scenu, pa merenje po ćeliji):
      **1948 pločica, 0 popločanih ćelija bez pločice, 0 ćelija puta zalepljenih uz drugi put,
      0 blokova bez puta uz neku stranu, 0 batrljaka puta između raskrsnica**
- [x] **svaki blok je okružen putem sa sve četiri strane** — proverava `Unserved()` i viče u
      izveštaju (`WARNING: block-15 has no road along its south side`). Strana se broji kao
      opslužena ako se, gledajući pravo iz lica bloka, na najviše 4 ćelije naiđe na put; kroz
      sopstveni parking bloka se gleda, kroz prazno tlo NE — prazno tlo je baš ta greška
- [x] `CoreDistrict : IDistrict` + `CoreDemoBuilder` + `Assets/Scenes/CoreDemo.unity`: lane graf iz
      rastera (`Raster.Junctions` → `LaneNet.AddNode`, `Raster.Stretches` → `AddRoad`/`AddOneWay`,
      pa `Finish()`) i saobraćaj od običnih `DemoVehicle`. Nijedna deljena klasa vožnje nije dirana.
- [x] **provereno harnessom** (`unity command gangsters_play --scene Assets/Scenes/CoreDemo.unity
      --seconds 180`, pa `analyze.py --verdict`): **24 kola → 5/5 runova PASSED**, mediana brzine
      5–6 m/s; 40 kola je pretovar za kvart ove veličine (4/6). Tri kvara koja je harness našao i
      koja su popravljena:
      1. put koji se prosto ZAVRŠI (ivica kvarta, lice bloka) nije imao čvor — traka se završavala
         u vazduhu, kola bi stala zauvek i sve iza njih s njima (kutijica 1 m na kraju, kao u
         drugim demoima);
      2. **S3 se lomi za 5 m** kod bloka-12 i tu su se dva puta iste ose sretala **čelo u čelo bez
         raskrsnice** — svaki sa svojim slepim krajem, pa su se kola okretala jedna drugima u lice
         i zaključavala (najgori run: dvoja kola stajala 132 s). Pravilo „put koji se završava na
         drugi put pravi raskrsnicu" prošireno sa POPREČNOG na **bilo koji drugi put**;
      3. deonica kraća od kola sa slobodnim krajem (bulevar od 5 m na ivici) — ne dobija trake.
- [ ] pešaci, semafori i **portali** (spoj na gradske ulice) — jezgro zasad stoji samo za sebe
- [ ] ugradnja u `Game.unity`

### 2.1 Jedan izvor istine: `CoreLayout` (runtime, `Assets/RoadDemo/CoreLayout.cs`)

Statička tabela iz 1.3 (prefab, pivot, yaw 0) + tabela **razmicanja** („cuts“) koja Synty
mere prevodi u gradske:

| rez (u koordinatama demoa) | koga pomera | Δ |
|---|---|---|
| glavni put → bulevar: z = 0, cela širina | sve sa z ≥ 0 | **+25** (10 → 35 m) |
| S1: x = −65, z ∈ [−110, −10] | 06, 07, 08, 03, 09, 13, 14 | +5 |
| S2: x = 5, cela visina | sve istočno od 5 (severni deo S2 dobija traku: 15 → 20) | +5 |
| S3 jug: x = 100, z ≤ −10; S3 sredina: x = 100, z ∈ [0, 105]; S3 sever: x = 105, z ≥ 110 | 14; 16; 15 | +5 |
| ulica z −80..−70: z = −80, x ∈ [−65, 90], otvara se ka jugu | 03, 08, 04 | −5 |
| ulica z −120..−110 (kroz usek bloka-04): z = −105, x ∈ [−95, −5] | 04 | −5 |
| ulica z 95..105 + uličica z 100..105: z = 105, cela širina | 11, 15 | **+10** |
| uličice | — | 0 (ostaju 5 m jednosmerne) |

Rezovi su u `CoreLayout.Cuts` i sude se po kutijama iz demoa, pa redosled ne igra ulogu.
Pravilo: blok se pomera ako mu je **cela** kutija sa dalje strane reza i seče pojas reza.
Sve mere su višekratnici 5, pa posle rezova svaki razmak između ivičnjaka pada u {5, 15, 20,
35} m. `CoreLayout.Spread()` vraća nove pivote; `CoreLayout.Boxes()` kutije. Rezovi su podaci,
ne kod — lako se menjaju kad se vidi crtež.

Bulevar: severni ivičnjak ide na z = 25; centralna linija bulevara z = 7.5 — to je linija na
koju se kvart kasnije pinuje u gradu (2.4).

### 2.2 Putevi kao koridori: `CoreRoads` (runtime, `Assets/RoadDemo/CoreRoads.cs`)

> **Ispravka 2026-08-25.** Prva verzija je čitala širinu puta ćeliju po ćeliju iz razmaka
> između ivičnjaka. Synty blokovi nisu pravougaonici — ivica bloka-16 je uvučena za jednu
> ćeliju na svojih severnih 40 m, blok-12 se na z 95 izbočio za 5 m — pa je isti put na
> jednom mestu čitan kao 15 m, na drugom kao 10, a ćelija između kao raskrsnica; `Lay` onda
> polaže profil iz zapadne ćelije i ostatak dužine ostane **bez ijedne pločice** (rupa u
> asfaltu između blokova 12/16). Zato:
>
> **Put je KORIDOR**: prava traka JEDNE širine, izvučena dokle god blokovi daju, i to jedne
> od gradskih širina (uličica 5 m, uska 10 m, ulica 15 m, bulevar 35 m). Redom:
> 1. **glavni put** prvi — njega layout deklariše (`MainRoad`) i ide celom širinom jezgra;
> 2. **nađeni** koridori: svaka prava traka standardne širine čiji je ceo hod slobodan od
>    blokova. Redosled je **prvo ŠIRINA pa dužina** — grad hoće ulice od 15 m, a put od 10 m
>    koji slučajno ide preko celog jezgra bi inače pokupio tlo ispod svake ulice koja mu
>    preseca pojas i blokovima ostavio usku traku za pročelje; bulevar se ne traži — jezgro
>    ima jedan i on je deklarisan;
>    Kandidat kome je tlo u međuvremenu zauzeto **polaže se u KOMADIMA oko zauzetog**, nikad
>    se ne odbacuje ceo: komad iza prepreke je baš onaj koji opslužuje blokove tamo (zbog toga
>    je block-15 ostajao bez puta — block-16 ide 5 m severnije od block-12 pa preseca ulicu);
>    **Uličice se isto nalaze, ne polažu iz `CoreLayout.Lanes`** — ta tabela kaže KOJIM
>    SMEROM uličica ide, ne da je ima. Uličica koju su rezovi otvorili na ulicu više nije
>    uličica;
> 3. odbija se traka koja se **na oba kraja** zabija u blok (nema izlaz), traka kojoj je uz
>    bok **drugi put iste ose** (dva puta bez ivičnjaka između čitaju se kao jedan širok put
>    sa strelicama po pola njega — zabrana br. 1 od korisnika), traka **bez ivičnjaka** ni sa
>    jedne strane (sredina parkinga), traka koja je ≥ ¾ **usek jednog bloka** (to je parking
>    tog bloka; uličica je izuzeta — uzidana s obe strane je definicija uličice), i uličica
>    koja nije uzidana blokom s obe strane celom dužinom;
> 4. gde se dva koridora ukrste — **raskrsnica**: goli asfalt + zebre. Raskrsnica uzima
>    **celu širinu oba** puta, pa je svaka dužina puta ili sva raskrsnica ili sav put.
>    Pravila da raskrsnica ima smisla (2026-08-25, korisnikova primedba):
>    - put koji se **završava** na drugi put pravi raskrsnicu i tamo, i kad tlo nije mogao da
>      uzme; inače mu usta gledaju u bok ulice čije linije prolaze pravo pored;
>    - **uličica nije put za ovo** — njena usta su prekid u ivičnjaku i give-way znak, ne
>      raskrsnica; zebra preko bulevara zbog uličice od 5 m je besmislica;
>    - **dužina puta od 5 m stisnuta između dve raskrsnice** (ili raskrsnice i bloka na koji
>      put staje) **pripada raskrsnici** — to je sredina jednog širokog ukrštanja, gde dve
>      ulice izlaze na treću pet metara jedna od druge jer blokovi nisu u liniji. Ostavljena
>      kao put, crta batrljak žute linije između dve zebre. Dužina na **ivici crteža** se ne
>      guta — tu put ide dalje u grid;
>    - `Stubs()` u izveštaju viče ako batrljak ipak ostane.
> 5. sve što nijedan koridor nije uzeo je **preostalo tlo**: usek bloka → parking, a ostalo
>    (`Kind.Spare`) **ostaje PRAZNO** — ništa se ne polaže — i ide u izveštaj sa merom i
>    mestom. Pogađanje (traka asfalta) samo izgleda kao još jedna traka puta pored; prazno
>    se vidi u sceni i korisnik kaže šta tu ide.
>
> `Raster.Across` kaže koliko je ćelija od ivice svog puta — `Lay` polaže profil iz ćelije 0,
> nikad iz susedstva, i ako dužina ipak nije sva iste vrste, polaže go asfalt preko **cele**
> širine. Rupa više nije moguća.

Prenos onoga što je radilo u `Assets/Scripts/Editor/CoreCitySketch.cs` (raster 5 m, profili,
zebre, parkinzi) u runtime klasu sa jednim delegatom za instanciranje
(editor: `PrefabUtility.InstantiatePrefab`, igra: `Object.Instantiate`), proširen za:

- **1 ćelija = uličica**: `Road_Bare_01` + `Road_Arrow_01` na oba ušća; uličica se NALAZI
  (uzidana blokom s obe strane), a smer joj daje `CoreLayout.Lanes` po mestu.
- **3 = ulica 15 m**: postojeći profil (`YellowLines_02` ×2 + parking trake) = isti kao
  `RoadDemoBuilder.FillVerticalSegment`.
- **preostalo tlo**: usek bloka → parking, ostalo `Kind.Spare` — **prazno**, u izveštaju sa
  merom i mestom (ranije `Wide`, „traka uz put“).
- **7 = bulevar 35 m**: vratiti profil iz ranije verzije skice (`Road_02 | Lines_01 | Median ×2 |
  Lines_01 | Road_02` + `Street_Divider_01` po 5 m, prekid uz raskrsnice) = `FillVerticalSegment`
  za `blvd`.
- **raskrsnice**: gola kutija + zebre na ušćima (postojeće); na bulevaru zebre u dva dela oko
  medijana (`Band(-BlvdHalf,-MedianHalf)`/`(MedianHalf,BlvdHalf)` kao u builderu).
- **parkinzi** u usecima: postojeće (`Parking`, `Road_ParkingLines_01`).
- **semafori**: `TrafficSignal` na raskrsnicama gde ih Synty ima ((0,−5), (95,−5), (0,−75),
  (97,107) → posle rezova pomereni) + na svakom ukrštanju bulevara i ulice; give-way ostaje
  kao statični prop u blokovima (već je u prefabima: stubovi/znaci na trotoaru su požnjeveni).
  Synty semafor-stubovi u prefabima ostaju dekor; logika je gradska (`TrafficSignal`,
  `host.RegisterSignal`).
- **Lane graf**: iz istog rastera segmenti ulica (centar, širina, jednosmernost) →
  `LaneNet.AddTwoWay`/`AddOneWay(a, b, halfRoad, laneOffsets, …)` sa čvorovima na
  raskrsnicama; trake ±2.5 (ulica), bulevar kao u `RoadDemoBuilder.LaneOffsets(true)`, uličica
  jedna traka offset 0. Obrazac registracije: `SuburbDistrict.cs:236` (`host.RegisterRoads`).
- **Pešaci**: `PedNode`/`PedLink` po ivičnjaku svakog bloka (prsten `Sidewalk_Straight` iz maske
  bloka) + prelazi na zebrama; obrazac `SuburbDistrict` (PedNode/PedLink + `RegisterPavement`).
- **Tlo**: ravan pod −0.06 m ispod celog pravougaonika kvarta (gradsko pravilo za tlo pod
  putem), nema poda ispod zgrada koje idu ispod nule.

### 2.3 Dva domaćina, jedan kod

- **Crtež u editoru** (prvi vidljivi rezultat, brzo): `Tools/City/Core/Sketch The Core City`
  prepraviti da umesto pakera zove `CoreLayout` + `CoreRoads` — blokovi na Synty mestima
  posle rezova, putevi iz razmaka. Cilj: korisnik u CoreHarvest sceni vidi Synty grad sa
  gradskim ulicama i bulevarom. Ovde se doteruju rezovi.
- **`CoreDistrict : IDistrict`** (`Assets/RoadDemo/CoreDistrict.cs`) + tanki domaćin
  `Assets/Scenes/CoreDemo.unity` (kao HarborDemo/SuburbDemo): `Plan(links, seed)` (nema
  slučajnosti u v1), `LocalBounds` = pravougaonik posle rezova (~275 × 340 m), `Reserve` →
  `Paved` ceo pravougaonik + `Flat` na 0, `Build(host)`: blokovi iz `CoreBlocks` pod
  `host.StaticRoot`, `CoreRoads` pod isti koren, lane graf + pešaci + semafori registrovani na
  domaćina; `Tick`/`Dispose` prazni (život je gradski: `StreetTraffic`, `CityLife`).
- `DistrictFrame` (origin + yaw) — sve lokalno, kroz frame samo na dodiru sveta, po ugovoru iz
  `Docs/city-districts-plan.md §1.1`.

### 2.4 Ugradnja u Game.unity (posle odobrenja crteža)

- `CityLayout.Roll`: rezervacija za jezgro u sredini grida (Downtown kvart), pin: centralna
  linija bulevara jezgra = jedna horizontalna gradska bulevar-linija (`horizontalIsBoulevard`),
  S2 na vertikalnu liniju ako se poklopi u 5 m, inače kosi spoj po `Districts.cs:LayConnector`.
- Gridski lotovi pod pravougaonikom jezgra se ne grade (kao šavovi: `SegmentOpen`/`InSeam`).
- Krajevi bulevara istok/zapad + S1/S2/S3 sever/jug = konektori na gradske ulice.

### 2.5 Redosled rada

1. Vraćanje scene (2.0) i `Docs/synty-demo-anatomy.md` (deo 1 ovog plana + ASCII mapa).
2. `CoreLayout` + `CoreRoads` (iz `CoreCitySketch`) → crtež u CoreHarvest; iteracija rezova sa
   korisnikom (raster `LastMap` + snimci).
3. `CoreDistrict` + `CoreDemo.unity`; Play kroz `gangsters_play` harness.
4. Ugradnja u grad (2.4).
5. Posle: obrisati `city-hall-block.prefab` (duplikat), `CoreCitySketch` paker ostaje samo kao
   istorija ili se briše.

### 2.6 Provera

- Raster posle rezova: svaki put jedne od širina {5, 10, 15, 35} celom dužinom; nula
  preklapanja blokova (`clashes`); nula popločanih ćelija bez pločice; parkinzi samo u
  usecima; preostalo tlo iz izveštaja se gura naniže rezovima.
- Snimci `capture_scene_view` (plan + kos) u CoreHarvest sceni, uporediti sa ASCII mapom
  originala (dodatak A).
- CoreDemo Play (harness, ~30× ubrzano): kola prođu svaku ulicu i uličicu (jednosmerno, bez
  stall-ova u `analyze.py`), semafori ciklusi na 4+ raskrsnice, pešaci obilaze prstenove.
- Game.unity: seed roll sa jezgrom, vožnja iz grida u jezgro i nazad, perf proba
  (`DemoPerfProbe`).

