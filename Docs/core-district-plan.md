# Plan: gradsko jezgro v2 po Synty receptu (CoreDistrict)

> Odobreno 2026-08-25. Anatomija demo scene na kojoj plan stoji: `Docs/synty-demo-anatomy.md`.
> Kod: `Assets/RoadDemo/CoreLayout.cs` (raspored + rezovi + uličice), `Assets/RoadDemo/CoreRoads.cs`
> (raster razmaka → pločice), `Assets/Scripts/Editor/CoreCitySketch.cs` (crtež u editoru,
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
- [x] `CoreRoads`: raster, čitanje razmaka (uličica 1 / uska 2 / ulica 3–4 / bulevar 7–8 ćelija), useci kao
      parking osim kad su hodnik, zebre, medijan, strelice; izveštaj o praznina­ma (`Raster.Report`)
- [x] crtež u CoreHarvest sceni: 295 × 355 m, blokovi 33.600 m², put 38.575 m², parkinzi 2.400 m²;
      jedina neuklopljena praznina: zapadni izlaz bulevara (x −120..−90) gde se prstenske ulice oko
      blokova 01 i 02 mimoilaze za 15 m — ivica kvarta, rešava se spojem na grid
- [ ] `CoreDistrict : IDistrict` + `CoreDemo.unity` (lane graf, pešaci, semafori)
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

### 2.2 Putevi iz razmaka: `CoreRoads` (runtime, `Assets/RoadDemo/CoreRoads.cs`)

Prenos onoga što već radi u `Assets/Scripts/Editor/CoreCitySketch.cs` (raster 5 m, čitanje
razmaka, profili, zebre, parkinzi) u runtime klasu sa jednim delegatom za instanciranje
(editor: `PrefabUtility.InstantiatePrefab`, igra: `Object.Instantiate`), proširen za:

- **1 ćelija = uličica**: `Road_Bare_01` + `Road_Arrow_01` na oba ušća, smer iz tabele
  (Synty strelice: pravac zapisan u `CoreLayout` po uličici).
- **3 = ulica 15 m**: postojeći profil (`YellowLines_02` ×2 + parking trake) = isti kao
  `RoadDemoBuilder.FillVerticalSegment`.
- **4 = ulica + traka**: postojeće (`Wide`).
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

- Raster posle rezova: između ivičnjaka samo {5, 15, 20, 35}; nula preklapanja blokova
  (`clashes`), nula `Outside` rupa unutar pravougaonika; parkinzi samo u usecima.
- Snimci `capture_scene_view` (plan + kos) u CoreHarvest sceni, uporediti sa ASCII mapom
  originala (dodatak A).
- CoreDemo Play (harness, ~30× ubrzano): kola prođu svaku ulicu i uličicu (jednosmerno, bez
  stall-ova u `analyze.py`), semafori ciklusi na 4+ raskrsnice, pešaci obilaze prstenove.
- Game.unity: seed roll sa jezgrom, vožnja iz grida u jezgro i nazad, perf proba
  (`DemoPerfProbe`).

