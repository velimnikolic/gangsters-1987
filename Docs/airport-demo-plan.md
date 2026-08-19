# Plan: AirportDemo — pravi regionalni aerodrom iz Synty paketa (1987)

> Stanje na dan 2026-08-19. Cilj: posebna scena `Assets/Scenes/AirportDemo.unity` — potpun,
> realističan aerodrom (airside + landside) složen **isključivo** iz Synty paketa koje već imamo,
> po istom obrascu kao `HarborDemo`/`SuburbDemo` (samostalan bootstrap, kit-ormar, versioned
> bake u `Assets/CityKit`, RoadDemo mašinerija za ljude/vozila, perf pass). Period: SAD 1987
> (`Docs/1987-period-reference.md`) — bez mlaznih mostova, sa telefonskim govornicama, ručnim
> ukrcavanjem preko asfalta, kokainskom erom u pozadini.

---

## 0. Gde smo sada — šta od aerodroma uopšte imamo u paketima

Nijedan od 13 paketa nije avio-paket. Inventar (proveren po imenima fajlova na disku):

| Kategorija | Imamo | Nemamo (mora kitbash ili odustati) |
|---|---|---|
| Avioni | **jedan** jednomotorac `PalmCity/SM_Veh_Plane_01` (hidroavion sa plovcima `_Floats_01`, attachment `SM_Veh_Plane_01_Wheels_01` = točkovi; propeler + blur, 4 vrata, flapovi, sedišta — sve odvojeni mesh-evi). Izmereno: **13.6 m raspon × 10.0 m dužina × 5.4 m visina** (klasa Cessna Caravan). | bilo koji mlaznjak, dvomotorac, komjuter turboprop, teretni avion, biznis-džet; liveriji |
| Helikopteri | `PalmCity/SM_Veh_Helicopter_01` (12.8 m rotor, 14.8 m, 4.3 m), `PoliceStation/SM_Veh_Helicopter_01` (isti trup + reflektor + kamera) | — |
| Helidromi | `PalmCity/SM_Prop_Helipad_01` (8×8 m oznaka), `PoliceStation/SM_Bld_HeliPad_01` (9×9 m dignut) + `_Stairs_01` | — |
| Hangari | GangWarfare industrijski kit (metalni zidovi, `SM_Bld_Wall_Metal_Door_Slide_01/02`, `Roof_Truss_01`, `Roof_Corrugated_*` iz Prison), Military `SM_Bld_Wall_Garage_Large_01` (7.5 × 4.5 m) + `SM_Bld_Door_Roller_Large_01` (6.8 × 4.2 m) | **vrata avionske razmere**: avion je 5.4 m visok, najveća gotova vrata 4.5 m → hangar = kitbash |
| Toranj | Prison `SM_Bld_Prison_Tower_Top_01` (kabina 6.1 × 4.5 m, zastakljena), `_Bottom/_Middle_01` (šaht 2.3 × 3 m), PalmCity `SM_Bld_Wall_Tower_*`/`Floor_Tower_01`/`Roof_Tower_01` (5 × 4.3 m sprat), Lighthouse | pravi ATC toranj (nagnuto staklo) — kitbash: PalmCity stablo + Prison kabina |
| Terminal | Generic Base kit (2.5 m zid, 3 m sprat), **Plaza staklene pune ploče** `SM_Bld_Base_Wall_Glass_01/_Half/_45/_Door_Glass_01`, PoliceStation `Wall_Window_Large_*`, `Wall_Reception_01..03`, PalmCity `Awning_*`, Plaza `Awning_Cover/Pole`, City `SM_Bld_Station_01..03` (železnička stanica = gotov „mali terminal“) | tabla polazaka, traka za prtljag, rendgen, turniket, jet-bridge |
| Pod | GangWarfare `SM_Bld_Floor_Asphalt_01..03`, PalmCity `Sidewalk_01`/`Road_Grey_01` mat (već `HarborKit.ConcreteMat/AsphaltMat`), Generic parking `SM_Gen_Env_Road_Parking_01..06`, City `Road_Lines/Arrow/ParkingLines`, GangWarfare `Floor_Lines_01..04` | **sve piste oznake** (prag, brojevi, osa, aiming point, hold-short, taksi osa), svetla piste/taksi, PAPI, far, vetrokaz |
| Ograda | PoliceStation `SM_Bld_Fence_01/Pillar/Gate_01/02/Runner`, GangWarfare `Fence_Wire_01/Fence_01/02/Gate_01`, Military `SM_Prop_Barbed_Wire_01/02` (jedina bodljikava), Prison `Fence_01..06` (5.7 m — previsoka, samo za zatvor), PalmCity `SM_Prop_Barrier_Gate_01` (**rampa**), bollardi, kupe | portirnica (kitbash iz Base kita) |
| GSE vozila | `GangWarfare/SM_Veh_Forklift_01`, Town/GangWarfare `Truck_01`, PalmCity `Pickup_01` + preseti + `Pickup_Flood_Lights/Canopy/Cones/Lift`, `Golf_Cart_01`, `Trailer_01`, Town `Bus_01`, `Firetruck_01`, City `Car_Ambo_01`, `Limousine_01`, taksiji, policija | cisterna, pushback, belt-loader, avio-stepenice, prtljažne kolice (sve kitbash iz postojećeg) |
| Ljudi | Generic `SM_Gen_Chr_Jumpsuit_Male/Female_01` (ramp crew), PalmCity `SM_Chr_SeaCaptain_*` (= pilot sa kapom), PoliceStation oficiri/detektivi + `Attach_Headset_01/02`, `Attach_Radio_01/02`, `Attach_Hat_01..07`, Nightclubs `Bouncer` (obezbeđenje), GangWarfare `DEA_Agent_*`/`DEA_Plainclothes_Male_01`, biznis/turisti iz City/PalmCity/Town/Generic, PalmCity `Attach_LifeJacket_01` (= hi-vis prsluk) | kofer na točkiće (1987. ni ne treba — `PoliceStation/SM_Prop_Duffle_Bag_01`, `Briefcase_01`, PalmCity `Bag_01..03`) |
| Znakovi | PalmCity Signs (66): `SM_Sign_Plane_01`, `Fuel_01`, `Authorized_Vehicles_01`, `Taxi_01`, `Parking_01`, `Arrow_01..05`, **`Blank_01..05`** (za sopstvene dekale), `Info_01`, `No_Entry_01` … | taksi-znakovi (žuto/crno, crveni hold) → tekstura na `Blank_*` |
| FX | ParticleFX `FX_Dust_Blowing_Soft_01` (rotor-wash), `FX_Trail_Exhaust_01`, `FX_Smoke_*`, `FX_Steam_*`, PalmCity `FX_Birds_01`, PNB `WeatherControl` | toplotni šimer, dim guma pri sletanju (uzeti `FX_Dust_Small_01`) |

Postojeći kod koji ne koristimo: `Assets/Scripts/City/PlaneBehavior.cs` i `HelicopterController.cs`
su LivingCity nasleđe ([[road-demo-self-contained]] — ne recikliramo). `RoadDemoBuilder.cs:481-491`
namerno izbacuje `"plane"`, `"helicopter"` iz saobraćaja — u AirportDemo avioni su svesno
poseban sistem, ne `DemoVehicle`.

**Zaključak dijagnoze:** „pravi aerodrom“ sa ovim paketima je **regionalni / okružni aerodrom**
(FAA „general aviation reliever + commuter service“): jedna pista, paralelna taksi-staza,
GA apron sa vezanim avionima, red hangara, FBO sa pumpom, mali komjuter-terminal sa ukrcavanjem
preko asfalta, kontrolni toranj, vatrogasna stanica, teretna hala, helidrom, ograda, parking i
prilazni put. To je tačno tip aerodroma kroz koji 1987. ide kokain iz Floride/Karibа malim
avionima — tematski pun pogodak, a ne zahteva ni jedan avion kog nemamo. Veliki međunarodni
aerodrom sa mlaznjacima **nije izvodljiv realistično** (v. §7).

---

## 1. Principi

1. **Samo Synty aseti** + kitbash iz njih (kao brodovi u luci). Nikakvi primitivi vidljivi u
   kadru; primitivi smeju samo kao nevidljivi nosači ili ispod Synty materijala (tankove, stub
   vetrokaza, svetla piste — mali mesh-evi sa pack materijalom, to je isto što i `HarborShipKitBash`).
2. **Mere iz FAA AC 150/5300-13 (ADG II / kategorija B–C)** — raspored, širine, razmaci i oznake
   po stvarnim brojevima, pa tek onda sabijeno gde mora (visina školskog kruga, dužina pravca
   prilaza). Aerodrom se prepoznaje po oznakama i razmacima, ne po broju zgrada.
3. **Isti skelet kao Harbor/Suburb:** `AirportDemoBootstrap` → `AirportDemo.AirportDemoBuilder`
   (partial po temama), `AirportKit` ormar, `Editor/AirportKitBash` (versioned, `AirportKitVersion.txt`),
   `Editor/AirportDemoAutoBake` (`[InitializeOnLoad]`), `Editor/AirportKitProbe` →
   `Logs/airport-kit-probe.txt`, `DemoBatchShot` za screenshot bez otvaranja editora.
4. **Pod = `FlatPlane` ravni** (Harbor), ne 5 m pločice: pista, taksi-staze, aproni su velike
   jednobojne površine; oznake su tanki quad-ovi sa pack materijalom (bela/žuta) 5 mm iznad.
   5 m grid važi samo za StreetKit ulice i parking pločice.
5. **Život iz RoadDemo-a** gde god postoji (`PedestrianAgent`/`CivilianAgent`, `DemoVehicle` +
   `RoadNode/RoadEdge`, `CarOccupant`, `WheelSpin`, `WalkObstacles`, `StreetAlarm`/`PoliceDispatch`);
   avioni i GSE su **nove obične klase** koje tikće builder (`HarborShip`/`HarborShipping` obrazac:
   noge putanje + profil brzine + stanja).
6. **Bez kulisa** van ograde ([[no-backdrop-scenery]]): iza ograde je ravnica/trava, prilazni put
   izlazi iz kadra, ništa se ne „crta“ na horizontu.
7. UI tekst na engleskom ([[ui-english-only]]); sve nazive (Municipal Airport, FBO, gate) na engleskom.

---

## 2. Razmere i raspored (brojevi koje builder nosi kao konstante)

Koordinate: pista duž **X**, airside na **+Z** od piste, landside (terminal/parking/put) dalje ka
+Z. Prizemlje `LandY = 0`, beton/asfalt `TileTop = 0.05`, oznake `+0.01`.

```
                         ← 1500 m →
 z=-120  trava / RSA                                                   (ograda, daleka)
 z=   0  ═══════════════════  RWY 09 / 27  (1500 × 30 m)  ══════════════════   PAPI (levo od 09)
 z=  75  ── ── ── hold-short linije na konektorima A1 A2 A3 A4 (75 m od ose piste)
 z=  90  ─────────────────  TWY A  (paralelna, 10.5 m + ramena 3 m)  ───────────
 z= 120  vetrokaz+segmented circle        ARFF        teretna hala        fuel farm
 z= 130 ┌──────── GA APRON ──────────┐ ┌─ COMMUTER APRON ─┐ ┌── CARGO ──┐
 z= 200 │ tie-down redovi (3 × 8)    │ │ 2 stajanke + helipad│ │ hala+dok  │
        └──────────────────────────────┘ └───────────────────┘ └───────────┘
 z= 210  HANGARI (6 box + 1 maint.)  FBO+pumpa   TERMINAL (60×20)  TORANJ (22 m)
 z= 240  ───── servisni put unutar ograde (jednosmeran, 5 m) ───────────────
 z= 250  ═══ OGRADA 2.4 m + bodljikava ═══ kapija GA (rampa+portirnica) ═ kapija cargo ═
 z= 260  parking GA/FBO    RENT-A-CAR    TERMINAL LOOP (drop-off, nadstrešnica)   parking 120 mesta
 z= 300  ────────── prilazna ulica (StreetKit, 2 trake + trotoari) ────────── → van mape
```

### 2.1 Pista i taksi-staze (FAA)
- **Pista 09/27:** 1500 × 30 m (5000 × 100 ft — komjuter + turboprop; `runwayLength` param,
  minimum 1200). Oznake za visual/non-precision pistu, bele:
  brojevi `09`/`27` visine 18 m (šablon cifara iz quad-ova, font-ovi FAA su 8 pravih segmenata),
  prag (piano keys): **8 traka × 45 m × 1.75 m**, razmak 1.75 m, centralni razmak 3.5 m;
  osa: segment 36 m / pauza 24 m, širina 0.9 m; aiming point 2 × (45 × 6 m) na 300 m od praga;
  ivične linije 0.9 m; ramena 3 m asfalt tamniji; RSA trava 45 m svaka strana bez ičega.
- **TWY A** paralelna: osa na z = 90 (ADG II min 73, C 91), širina 10.5 m (35 ft), ramena 3 m,
  žuta osa 15 cm; konektori **A1 A2 A3 A4** (dva kraja + dva srednja, pod 90°, krivine R = 23 m);
  **hold-short** na 75 m od ose piste: 2 pune + 2 isprekidane žute linije, ukupno 0.9 m,
  uz njih crveni hold znak `9-27` i žuti lokacijski `A` (dekal na `SM_Sign_Blank_*`).
- **Svetla:** ivična bela na 60 m, ≤ 3 m od ivice (poslednjih 600 m ka kraju žuta), prag
  zelena/kraj crvena (po 8), taksi plava na 60 m / 15 m u krivinama, PAPI 4 kutije levo od 09
  na 300 m od praga; **far** (rotirajući belo/zeleno) na vrhu tornja; sva svetla su emissive
  quad-ovi + noću 8 pravih point svetala budžet (`DemoStreetLamps` obrazac), ne 200 Light-ova.
- **Vetrokaz** (lit) na sredini, 5 m jarbol + narandžasto-bela čarapa 3.6 m (kitbash iz
  `SM_Prop_Flag_Stand_01` + konus sa tintom), u **segmented circle** 30 m (bele ploče na travi).

### 2.2 Aproni i stajanke
- **GA apron** 180 × 70 m beton: tie-down redovi, korak 18 m (raspon 13.6 + 4.4), redovi na 15 m
  sa taksi-lane 12 m između (ADG II object-free 12 m); „T“ oznake i 3 ankera po avionu; 14–20
  vezanih aviona; 2 stajanke uz pumpu (avgas ostrvo: `Town/SM_Prop_Gaspump_01` + `Gaspump_Cover_01`
  + `SM_Sign_Fuel_01`).
- **Commuter apron** 90 × 60 m: 2 stajanke (osa na 25 m od fasade terminala, aviona nosom ka
  terminalu, žuta lead-in linija + stop bar), kupe, čokovi, prtljažni voz uz stajanku; pešački
  koridor (bela šrafura) od gejta do stajanke — 1987: hoda se po asfaltu.
- **Helidrom** TLOF `PoliceStation/SM_Bld_HeliPad_01` na kraju commuter aprona, 25 m od svega,
  bela „H“ + FATO krug (quad), policijski helikopter kući ovde.
- **Cargo apron** 60 × 50 m uz teretnu halu (magacin iz `SyntyWarehouseKit` — `building-warehouse-small`),
  kamionski dok na +Z strani, forklifti, palete, `Shipping_Container_Small_01`.
- **Servisni put** (vehicle service road): 5 m asfalt, bela isprekidana ivica, uz ogradu celom
  dužinom, sa 2 prelaza preko TWY A konektora (označeni).

### 2.3 Zgrade (sve kitbash → `Assets/CityKit/Airport/*.prefab`, front +Z, y = 0)
| Zgrada | Mere | Recept |
|---|---|---|
| **Box hangar ×6** (red) | 16 × 12 m, vrata 15 × 6.5 m klizna | GangWarfare `Wall_Metal_01` u 2 reda (4.5 + 2.5 m), `Wall_Metal_Exterior_Corner`, krov `Roof_Truss_01` + Prison `Roof_Corrugated_Slope_01`, vrata: 4 klizna panela od `Wall_Metal_Door_Slide_01` skalirana po visini + gornja šina `SM_Gen_Bld_Beam_01`; 1 od 6 otvoren sa avionom unutra |
| **Maintenance hangar** | 36 × 30 m, vrata 30 × 7 m (2 aviona) | isti kit, 3 reda zidova, `Skylight_01` iz Military, unutra `Walkway_*`, alat, `Ladder`, drugi avion bez motora (propeler skinut) |
| **FBO** (fixed-base operator) | 18 × 10 m, 1 sprat + trem | Base kit + Plaza staklo ka apronu, `Awning`, unutra `Counter_01`, `Seating_01`, `Vending_Machine_01`, `Pay_Phone_01`, `SM_Sign_Fuel_01`; natpis „AVGAS 100LL“ na `Billboard_Sign` |
| **Terminal** | 60 × 20 m, 1 sprat (4.5 m) + 1 m parapet, apron fasada staklo, landside nadstrešnica | varijanta A: Generic Base + Plaza `Wall_Glass_01` (24 ploče) + PoliceStation `Wall_Window_Large_*`; varijanta B: City `SM_Bld_Station_01` kao jezgro + krila. Unutra: 2 check-in pulta (`Wall_Reception_01..03`), `Belt_Barrier` red, `Metal_Detector_01`, 3 reda `Seating_01`, tabla (`SM_Gen_Prop_Screen_01` dekal), `Pay_Phone_01` ×3, `ATM_01`, `Newspaper_Stand_01`, `Vending`, rent-a-car pult, izlaz Gate 1/2 ka apronu. Krov: `Roof_Aircon_*`, `Antenna_01`, `SatDish_01` |
| **Kontrolni toranj** | 22 m, kabina 6.1 m | PalmCity `Wall_Tower_01/02/Windows_01` + `Floor_Tower_01` × 5 spratova (4.3 m) na Base kit prizemlju 8 × 8 m (ulaz, klima), na vrhu Prison `Tower_Top_01` kabina (zastakljena, kao ATC kabina), `Lighthouse_01_Insert_01` kao far, `Antenna_02` + `Satelite_Dish_02` na krovu; lako se vidi iz svakog ugla |
| **ARFF** (vatrogasna) | 15 × 12 m, 1 vrata 5 × 4.5 m | Military `Wall_Garage_Large_01` + `Door_Roller_Large_01` (ovde visina 4.5 staje), `Town/SM_Veh_Firetruck_01` u garaži ili na apronu |
| **Teretna hala** | 30 × 20 m | `SyntyWarehouseKit` magacin (postoji), dok ka putu |
| **Fuel farm** | 20 × 15 m ograđeno | 2 horizontalna tanka (kitbash: cilindar sa GangWarfare metal materijalom na `Floor_Riser` postolju), `Pipeline_Pipe_Large_End_Alt_01`, `Lab_Tank_03`, bollardi, `Danger_Sign_01`, ograda + kapija |
| **Portirnica** ×2 | 3 × 3 m | Base kit 1 ćelija sa `Wall_Window_01` ×4, `Barrier_Gate_01` rampa, `Authorized_Vehicles_01` |
| **Rent-a-car / parking kućica** | 4 × 3 | isto |

### 2.4 Landside
- **Prilazna ulica** `StreetKit` (2 trake, trotoari, `Palms = false`) od ivice mape (z=300) do
  T-raskrsnice; **terminal loop** jednosmeran (krug 10 m kolovoz) sa drop-off ivičnjakom 40 m,
  nadstrešnicom (`Plaza/Awning_Cover` na `Awning_Pole`), `Taxi_Stand_01` + 4 taksija u redu,
  `Bus_Stop_01` + `Town/Bus_01`, `Pay_Phone` par, `Sign_Plane_01` na stubu, `Sign_Arrow` „Terminal / Parking / Air Cargo / General Aviation“ (dekali na `Blank_*`).
- **Parking** 120 mesta: logika iz `BlockParkingBay` (asfalt, `Road_ParkingLines`, prilaz sa
  rampom i kućicom, ivičnjaci, lampe, žbun), runtime verzija u builderu; popunjenost 60 %,
  vozni park = 1987 (`VehicleCatalog.Barred` isključuje anahronizme — Supercar/Delivery_Bot/E_Scooter).
- **Rent-a-car** lot 20 mesta (jednoobrazne boje po kući), **GA parking** 20 mesta kod FBO.
- Dve kapije kroz ogradu: **GA gate** (rampa + portirnica, put do hangara/FBO), **cargo gate**
  (kamioni, `Truck_01` ×3 ciklus kao `HarborTruck`).
- Zelenilo: trava `Generic Grass` + retka stabla (`City/Tree_01..03`) samo landside; airside
  bez drveća (obstruction surface).

---

## 3. Arhitektura koda

```
Assets/AirportDemo/
  AirportDemoBuilder.cs          params (+Tooltip), Awake redosled, Update tick, BuildLight/BuildCamera
  AirportDemoBuilder.Ground.cs   FlatPlane trava/beton/asfalt, RSA, segmented circle
  AirportDemoBuilder.Airside.cs  pista, TWY, konektori, oznake (RunwayPaint), svetla (FieldLights), vetrokaz, PAPI
  AirportDemoBuilder.Apron.cs    GA/commuter/cargo apron, tie-down, stajanke, helidrom, servisni put
  AirportDemoBuilder.Buildings.cs hangari, FBO, terminal, toranj, ARFF, cargo, fuel farm (iz CityKit/Airport)
  AirportDemoBuilder.Landside.cs StreetKit ulica, loop, parking, kapije, ograda, portirnice
  AirportDemoBuilder.Detail.cs   znakovi, kupe, čokovi, bollardi, palete, tragovi guma, brojevi
  AirportDemoBuilder.Life.cs     spawn: FlightOps, GroundOps, ljudi, saobraćaj, policija
  AirportDemoBuilder.Perf.cs     cull slojevi 20/21/22, flat-caster, MergeStaticGeometry (120 m čunkovi)
  AirportKit.cs                  ormar: const string putanje, Load/TryLoad/PrefabBounds/Prop/PlaceRun…
  AirportSpec.cs                 sve mere iz §2 kao const (deli ga editor bake i runtime)
  Aircraft.cs                    jedan avion: stanja + noge putanje + propeler/flapovi/točkovi
  FlightOps.cs                   red letenja, RunwayController, taksi-graf, školski krug, slotovi
  Rotorcraft.cs                  helikopter: hover/klimb/tranzit, rotor blur, downwash FX
  GroundOps.cs                   GSE: FuelTruck, BaggageTrain, FollowMe, Marshaller, Forklift, cargo kamioni
  AirportPeople.cs               putnici (curb→check-in→gate→avion i nazad), osoblje, obezbeđenje
  Editor/AirportKitBash*.cs      versioned bake → Assets/CityKit/Airport/*.prefab + AirportKitVersion.txt
  Editor/AirportDemoAutoBake.cs  [InitializeOnLoad] pre Play-a
  Editor/AirportKitProbe.cs      Tools/City/Probe Airport Kit → Logs/airport-kit-probe.txt
Assets/Scenes/AirportDemo.unity  = 170-linijski šablon, GameObject AirportDemoBootstrap + AirportDemoBuilder
```

- Namespace `AirportDemo`, editor `AirportDemo.EditorTools`, `seed = 1987`, `#if UNITY_EDITOR`
  uz standardnu `LogError` poruku u `#else`.
- **Taksi-graf** = `RoadNode/RoadEdge` iz `RoadDemoNet.cs` (isti tip, druga instanca), čvorovi na
  osama TWY/konektora/taksi-lane-ova/stajanki; avion se kreće 1-D `(edge, s)` kao `DemoVehicle`,
  ali sa sopstvenom klasom (širina 13.6 m, nema pretica­nja, `MinGap` 25 m).
- **RunwayController** = jedini arbitar piste: red zahteva {sletanje > poletanje > prelaz},
  jedan korisnik na pisti, „cleared to land“ tek kad je pista prazna i prethodni polazak
  prešao 600 m; hold-short čvorovi su kapije (isti mehanizam kao `TrafficSignal` gate na ivici).
- **Školski krug** (scaled): uzletanje → 150 m visine → crosswind → downwind na 500 m od piste
  → base → final 1200 m; odlasci „van mape“ nestaju na 2500 m i vraćaju se kao dolasci posle
  slota; brzine: taksi 8 m/s, zalet do 30 m/s na 350 m, prilaz 28 m/s, nagib 3°.
- Svetla/far/vetrokaz/propeleri/rotori = `MonoBehaviour`-i bez fizike (kao `HarborBob`).
- Kamera `DemoCamera` + `DemoShadows` (far clip 2500), `CrewDemoPace`, hint string standardni,
  `DemoClock`/`DemoSky` za dan/noć ako se uključi (far i svetla piste noću su poenta).

---

## 4. Život (šta se kreće)

**Airside**
- 6–8 aktivnih aviona (isti mesh, 4 boje: PalmCity alt materijali / tint) + 14 vezanih + 2 u hangaru.
  Ciklus: Parked → Startup (prop blur, `FX_Dust_Blowing_Soft_01` kratko) → Taxi (graf) → Hold
  short → Line up → Roll (flapovi) → Climb → Pattern / Depart → Final → Touchdown (`FX_Dust_Small_01`
  na točkovima) → Rollout → izlaz A3/A4 → Taxi → Stand → Shutdown. Komjuter slot na 40 min,
  GA slučajno; noću 1/4 prometa.
- 2 helikoptera: policijski (helidrom, patrola 1× na sat, reflektor noću), civilni charter
  (dolazi, stoji 10 min, odlazi).
- GSE: **FuelTruck** (`Truck_01` + tank + `HoseReel`) ide do svakog sletelog GA aviona i stoji
  3 min; **BaggageTrain** (`Golf_Cart_01` + 2 × `Trailer_01` sa torbama) terminal ↔ stajanka po
  dolasku/odlasku komjutera; **FollowMe** pickup (`Pickup_Preset_Construction` + `Flood_Lights` +
  tabla) vodi dolazak do stajanke; **Marshaller** (Jumpsuit + LifeJacket + Radio) maše na stajanci;
  **ARFF** kamion izlazi pred hangar na svakih N sletanja (standby), **Forklift** + `Truck_01`
  ciklus na cargo; line crew vezuje avione.
- Ptice (`FX_Birds_01`) daleko od piste, vetrokaz se okreće po vetru (jedan smer po sceni, pista
  u upotrebi = uz vetar: 09 ili 27).

**Landside**
- Saobraćaj `DemoVehicle` na ulici/loop-u: taksiji (red od 4, odlazi po putniku), autobus svakih
  15 min, privatna kola do parkinga (uđu, parkiraju, ljudi izađu i idu u terminal; obrnuto),
  kamioni na cargo kapiju, rent-a-car kola.
- Putnici `CivilianAgent`: curb → terminal → check-in (red) → sedenje → gate → hod do aviona
  → ukrcaj (nestanu u `CarOccupant` sedišta); dolazak obrnuto + dočekivači na ogradi aprona;
  piloti hodaju FBO ↔ avion; osoblje; obezbeđenje na vratima; šerifov auto na ivičnjaku.
- 1987 ukus: **DEA plainclothes** sedan parkiran s pogledom na GA kapiju (`DEA_Plainclothes_Male_01`
  u `Sedan_01`), jedan hangar „privatan“ (vrata zatvorena, `Van_01` ispred noću) — kuka za igru.
- `StreetAlarm`/`PoliceDispatch` uključeni: pucanj na aerodromu → šerif + helikopter.

---

## 5. Šta zadržati, šta praviti novo

**Zadržati (koristi se direktno):** `StreetKit`, `FlatPlane` obrazac iz Harbor-a (premestiti u
deljeni helper ako je privatan), `SyntyWarehouseKit` hala, `BlockParkingBay` logika (port u
runtime), `RoadNode/RoadEdge/DemoVehicle/TrafficSignal`, `PedestrianAgent/CivilianAgent/CityLife`,
`CarOccupant/WheelSpin/CarBody`, `WalkObstacles`, `CrewKit.Clips`, `DemoCamera/DemoShadows/
CrewDemoPace`, `DemoStreetLamps` budžet obrazac, `PoliceDispatch`, perf pass, `DemoBatchShot`.

**Novo:** `Aircraft`, `FlightOps`/`RunwayController`, `Rotorcraft`, `GroundOps`, `AirportPeople`,
`RunwayPaint` (generator oznaka iz brojeva), `FieldLights`, `AirportKitBash` (hangar/toranj/
terminal/FBO/fuel farm/portirnica/vetrokaz/PAPI/svetla/cisterna/prtljažna kolica/avio-stepenice),
`AirportSpec`, probe.

**Ne koristiti:** `PlaneBehavior`/`HelicopterController` (LivingCity), Prison ograda (5.7 m),
Synty plovci na avionu (`_Floats_01` off, `Wheels_01` on), `Supercar`/`E_*`/`Delivery_Bot`
(anahronizmi), jet-bridge bilo kakav.

---

## 6. Faze (zadaci numerisani redom kroz sve faze)

### Faza A — kit, mere, bake (bez scene)
1. `AirportSpec.cs` sa svim merama iz §2 (FAA vrednosti + komentar izvora na istoj liniji).
2. `AirportKitProbe`: izmeri `SM_Veh_Plane_01` (sa `Wheels_01`, bez `_Floats_01`), helikoptere,
   hangar zidove/vrata, tower delove, fence pieces (PoliceStation vs GangWarfare visina/pivot),
   glass ploče, `Awning`, `Seating_01`, `Metal_Detector_01`, `Barrier_Gate_01`, `Gaspump_*`,
   `Trailer_01`/`Golf_Cart_01` hitch → `Logs/airport-kit-probe.txt`. Ovo je ulaz za sve recepte.
3. `AirportKitBash` v1 → `Assets/CityKit/Airport/`: `hangar-box`, `hangar-maint`, `fbo`,
   `terminal`, `tower`, `arff`, `fuel-farm`, `guard-booth`, `windsock`, `papi`, `rwy-light`
   (beli/žuti/zeleni/crveni/plavi), `fuel-truck`, `baggage-cart`, `airstairs` (Base stairs na
   `Trailer_01`), `taxi-sign` (blank + dekal tekstura generisana u editoru). Front +Z, y=0,
   2.5 m modul, jedan mesh po materijalu, `AirportKitVersion.txt`.
4. `AirportDemoAutoBake` + meni `Tools/City/Catalog/Rebuild Airport Kit (Kit-Bash)` (priority 6).
5. Ručni pregled bake-ova u Prefab Mode (hangar vrata 15 × 6.5 m, avion stane sa 0.5 m rezerve,
   toranj 22 m, terminal 60 × 20).
   **Test:** probe log bez `missing`, svi prefabi postoje, `PrefabBounds` u ±5 % od `AirportSpec`,
   avion unutar hangara bez preseka (OBB test u probe-u).

### Faza B — zemlja i airside geometrija
6. Scena + bootstrap + builder skelet (`Awake` redosled, light, kamera, `CrewDemoPace`).
7. `Ground`: trava `FlatPlane` 2000 × 700 m, beton/asfalt površine piste/TWY/aprona sa per-cell UV
   rotacijom; ramena tamnija; RSA prazna.
8. `RunwayPaint`: brojevi, prag, osa, aiming point, ivice, TWY osa/ivice, hold-short, lead-in i
   stop bar na stajankama, tie-down „T“, servisni put, helidrom H/FATO, pešački koridor —
   sve iz `AirportSpec` brojeva, quad-ovi sa pack materijalom (bela = PalmCity line mat, žuta =
   City YellowLines mat), `+0.01`.
9. `FieldLights`: ivična/prag/taksi svetla, PAPI, far; emissive quad + noćni budžet; vetrokaz +
   segmented circle; taksi znakovi na svakom konektoru.
10. Ograda: 2.4 m panel + bodljikava žica po celom obodu (sa probe-om izabran kit), dve kapije sa
    rampom i portirnicom, `Authorized_Vehicles` znak, `Danger_Sign` na fuel farm.
    **Test:** `DemoBatchShot` 3 kadra (ceo aerodrom iz 600 m, prag 09 iz 80 m, apron iz 120 m);
    u kadru piste prebrojati: 8 pragovih traka, osa 36/24, hold-short na 75 m; ograda zatvorena
    (probe: nema rupe > 0.2 m).

### Faza C — zgrade i landside
11. Postavljanje bake-ova po §2.3 (front +Z, `PrefabBounds`), hangar red sa 22 m razmaka od
    taksi-lane ose, 1 otvoren hangar + avion, maint. hangar sa rasklopljenim avionom.
12. Terminal sa enterijerom (check-in, `Belt_Barrier`, `Metal_Detector`, sedišta, tabla,
    govornice, ATM, kiosk), nadstrešnica na curb-u, roof props.
13. Landside: `StreetKit` ulica + T + loop, drop-off, taxi rank, bus stop, parking (runtime port
    `BlockParkingBay`), rent-a-car, GA parking, drveće landside.
14. `Detail`: kupe, čokovi, bollardi, palete/kutije u cargo, burad kod fuel farm, tragovi guma
    na pragu (touchdown), zastave, korpe, klupe; ništa airside što nije na apronu.
15. `WalkObstacles.Block` za sve zgrade/props/parkirana kola; ped graf: curb → terminal → gate →
    stajanke; lane graf: ulica/loop/parking/servisni put/kapije.
    **Test:** screenshot iz 4 ugla bez providnih spojeva, nijedan objekat u RSA/TWY clearance
    (probe: bounds vs. zabranjene zone = 0 preklapanja), `[AirportDemo] missing prefab` = 0.

### Faza D — život airside
16. `Aircraft`: stanja + noge putanje (pravo/kubna/luk), `WheelSpin` za točkove, propeler blur
    swap na > 30 % gasa, flapovi, vrata otvorena na stajanci; tint/livery.
17. `FlightOps`: taksi-graf, `RunwayController`, hold-short kapije, školski krug, slotovi
    (komjuter 40 min, GA nasumično, noću 1/4), dolasci „van mape“.
18. `Rotorcraft`: policijski + charter, hover 10 m → tranzit, rotor blur, downwash FX, reflektor noću.
19. `GroundOps`: FuelTruck, BaggageTrain, FollowMe, Marshaller, ARFF standby, Forklift/Truck cargo
    ciklus (kapija → dok → kapija kao `HarborTruck`), line crew.
    **Test (sim bez editora, kao `simplan.py`):** 2 h sim-vremena → 0 dvostrukog zauzeća piste,
    0 preklapanja OBB aviona na taksi-stazi/apronu, svaki avion završi ciklus ≤ 25 min sim,
    FuelTruck stigne ≤ 3 min po sletanju, nijedno vozilo na pisti bez odobrenja.

### Faza E — život landside + ljudi + zakon
20. `AirportPeople`: putnici ciklus curb↔avion (ukrcaj kroz `CarOccupant`), dočekivači, piloti,
    osoblje, obezbeđenje; `CivilianAgent.PairChats`.
21. Saobraćaj: taksiji red, autobus, parking ulaz/izlaz, cargo kamioni, rent-a-car; `DriverNerve`.
22. Policija/DEA: šerif na curb-u, DEA sedan na GA kapiji, `PoliceDispatch` + helikopter kao
    `IPoliceUnit`; `StreetAlarm` reakcija putnika (panika u terminal).
    **Test:** 30 min sim: nijedan pešak zaglavljen > 20 s, red za check-in ≤ 8, svaki komjuter
    ukrca ≥ 6 putnika; pucanj → helikopter u vazduhu ≤ 90 s.

### Faza F — noć, perf, završna obrada
23. `DemoClock/DemoSky` dan/noć: far, svetla piste/taksi (emissive + budžet), apron jarboli
    (`Street_Lamp` + `Pickup_Flood_Lights` kitbash jarbol 15 m), kabina tornja svetli, helikopter
    reflektor.
24. Zvuk (`DemoAudio` sloj): prop drone po udaljenosti, rotor, kamion, PA zvono u terminalu
    (bez glasa).
25. Perf: cull slojevi, `MergeStaticGeometry` 120 m čunkovi, flat-caster strip, `unreadable-meshes`
    petlja; ograda/svetla/oznake obavezno u merge.
26. `DemoBatchShot` set + memorija (airport-demo.md) + ažurirati ovaj plan sa „urađeno/viđeno u Play-u“.
    **Test:** ≥ 60 fps na 1080p u kadru celog aerodroma (profil kao `perf-probe.txt`), draw calls
    ≤ 1500, svetla noću ≤ 16 realnih Light-ova.

### Faza G (opciono, odluka posle screenshota F) — veći avioni kitbash
27. Probni `bizjet` (Learjet 35 klasa: 14.8 × 10.8 m, T-rep) i `commuter-twin` (Beech 1900: 17.6 ×
    17.7 m) kao kitbash iz cilindričnih/Base mesh-eva sa Synty materijalima, kao brodovi — izolovano
    u Prefab Mode, **pa tek** ako izgleda kao Synty, u scenu (kućni džet Outfita, komjuter linija).
    Ako ne izgleda — ostaje Caravan-flota, što je i dalje realan GA aerodrom.

---

## 7. Šta NE raditi

- Ne praviti „međunarodni“ aerodrom sa mlaznjacima od primitiva u prvom prolazu — nemamo ni jedan
  mlazni avion, a lažni B727 upropasti celu scenu (iskustvo sa prvim brodovima). Velikih aviona
  nema dok Faza G ne prođe vizuelni test.
- Ne stavljati ništa u RSA/uz TWY osim onoga što FAA dozvoljava (svetla, znakovi, vetrokaz).
- Ne koristiti jet-bridge, x-ray traku, carousel, kofere na točkiće — 1987 regionalni aerodrom to
  nema; ukrcavanje je peške po apronu, prtljag na kolicima.
- Ne reciklirati LivingCity (`PlaneBehavior`, `HelicopterController`, `CameraRig` osim
  `BuildingCardPicker`) — [[road-demo-self-contained]].
- Ne dodavati kulise (grad na horizontu, planine iz `Generic Background`) — [[no-backdrop-scenery]].
- Nikakvi proceduralni „ukrasi“ van recepata iz §2.3/§2.4; sve što je novo ide kroz
  `AirportKitBash` i ima probe.
- Ne mešati podne pločice/šahovnicu (jedna površina = jedan materijal, [[block-floor-calm]]).

---

## 8. Otvoreno / pretpostavke

- **Pretpostavka:** tip = regionalni aerodrom sa komjuter linijom (Caravan-klasa), jedna pista
  1500 m. Ako korisnik hoće manju scenu, `runwayLength` 1200 i bez commuter aprona (samo GA) —
  ostalo ne menja.
- **Pretpostavka:** pista duž X, airside ka +Z, vetar iz zapada (pista 27 u upotrebi) — jedan
  smer po sceni, menja se seed-om.
- **Otvoreno:** liveriji aviona — PalmCity ima alt teksture? (probe u Fazi A); ako nema, tint
  kroz kopiju materijala po boji (4 boje).
- **Otvoreno:** da li terminal iz `SM_Bld_Station_01` (gotov, manje posla, ali „stanica“) ili
  Base+Plaza staklo (više posla, više „terminal“) — odluka posle bake-a oba u Prefab Mode.
- **Otvoreno:** veza sa igrom (Outfit): hangar u vlasništvu, naredbe tipa `Charter`/`AirFreight`,
  carina/DEA heat — nije deo ovog demoa; ovde samo vizuelna kuka (privatni hangar, DEA sedan).
- **Otvoreno:** ograda — PoliceStation `SM_Bld_Fence_01` ili GangWarfare `Fence_Wire_01`
  (visina/pivot iz probe-a); Military bodljikava žica na vrhu oba.
- Redosled rada: A → B → C (screenshot i pregled) → D → E → F → (G). Posle C obavezan
  pregled u Play-u pre života — kao u luci, raspored se menja posle prvog pogleda.

---

## 9. Izmereni aseti (Faza A, zadatak 2 — offline mere iz binarnih FBX-ova)

Mereno skriptom koja primenjuje transformacije čvorova (scratchpad `fbx.py`), vrednosti u metrima,
u lokalnom frejmu FBX-a (pivot u 0). Ovo su brojevi na koje se oslanjaju recepti iz §2.3 — probe u
Unity-ju (`AirportKitProbe`) treba samo da ih potvrdi.

**Avion `SM_Veh_Plane_01`** — raspon **13.64** (X ±6.82), dužina **10.39** (Z −6.47 rep … +3.92 propeler),
trup Y 0.89…3.69, ukupna visina sa plovcima 4.15 (Y −0.46…3.69).
- `_Floats_01` (plovci): X ±1.89, Y −0.46…1.14, Z ±3.89 → **gasi se za kopneni aerodrom**
- `_Wheels_01` (attachment, poseban FBX): X ±1.69, Y **0.01…1.20**, Z −0.79…3.23 → točkovi nose trup;
  na točkovima avion stoji sa Y=0 na tlu, ukupna visina **3.69 m** (a ne 5.4 kako je procenjeno u §0)
- propeler: disk Z 3.40…3.92, centar Y ≈ 1.80, `_Blur` poluprečnik **1.31**
- vrata: FL/FR (Z 0.02…1.25), RL/RR (Z −1.04…−0.07), svaka sa svojim `_Glass`
- flapovi: krilo `_Flap_FL/FR` (X 1.07…6.81, Y 2.57…2.88), rep `_Flap_RL/RR`, kormilo `_Flap_Tail_01`

**Posledica za hangare:** avion na točkovima je **3.7 m visok**, ne 5.4 → vrata hangara **12 × 4.5 m**
(raspon 13.64 traži 15 m otvora ako ulazi pravo; sa 12 m otvor prolazi samo bez krila) →
**box hangar: otvor 15 × 4.5 m**, maintenance 30 × 5.5 m. Military `SM_Bld_Wall_Garage_Large_01`
(7.5 × 4.5) i `SM_Bld_Door_Roller_Large_01` (6.84 × 4.19) sada **staju po visini** — hangar se pravi
slaganjem 2 takva otvora jedan do drugog + nadvratna greda, umesto potpunog kitbash-a.

**Helikopteri:** PalmCity 12.77 (rotor) × 14.82 (sa repom) × 4.18; PoliceStation 12.38 × 14.58 × 4.18.
Helidrom: PoliceStation `SM_Bld_HeliPad_01` **9.06 × 9.06 × 1.60** (dignut), PalmCity
`SM_Prop_Helipad_01` **8.0 × 8.0 × 0.20** (ravna oznaka) → rotor 12.8 preko pada 9 m je normalno
(TLOF < D), FATO krug 16 m oko njega.

**Kit moduli:**
| Komad | Mera | Napomena |
|---|---|---|
| GangWarfare `SM_Bld_Wall_Metal_01` | **3.00 × 3.13** (X 0…3, pivot na uglu) | modul hangara je **3 m**, ne 2.5 |
| GangWarfare `SM_Bld_Wall_Metal_Door_Slide_01` | 3.00 × 3.00 | klizna vrata, isti modul |
| GangWarfare `SM_Bld_Roof_Truss_01` | raspon **9.12** (Z −9.06…0.06), visina 2.15 | rešetka za 9 m raspon → hangar 18 m = 2 rešetke na srednjem stubu |
| Plaza `SM_Bld_Base_Wall_Glass_01` | **2.50 × 3.01** | Base modul 2.5 m / sprat 3 m (potvrđeno) |
| PoliceStation `SM_Bld_Fence_01` | **2.51 × 2.93** | ograda 2.9 m — tačno airside visina |
| PalmCity `SM_Prop_Barrier_Gate_01` | **4.50** krilo, 1.25 visina | rampa na kapiji |

**Pravilo koje iz ovoga sledi:** hangari i sve metalno idu na **3 m modul** (GangWarfare), terminal i
sve zidano na **2.5 m modul / 3 m sprat** (Generic Base + Plaza staklo). Dva modula se ne mešaju u
istom zidu.

### 9.1 Provera offline mera protiv Unity probe-a (`Logs/harbor-kit-probe.txt`)

Poređene su iste kockice iz FBX-a i iz Unity-ja:

| Komad | offline (FBX) | Unity probe | zaključak |
|---|---|---|---|
| `SM_Bld_Wall_Metal_01` | X 0…3.00, Y −0.13…3.00, Z −0.04…0.18 | X **−3.00…0**, Y −0.13…3.00, Z −0.04…0.18 | ista veličina, **X ogledalo** |
| `SM_Bld_Base_Wall_01` | X 0…2.50, Y 0…3.01 | X **−2.50…0**, Y 0…3.01 | isto |
| `SM_Bld_Wall_Metal_Door_Slide_01` | X 0…3.00 | X −3.00…0 | isto |

**Pravilo:** offline mere iz `fbx.py` su tačne po veličini (metri = FBX/100), ali Unity pri importu
**okreće X osu** (levoruki koordinatni sistem) — pivot koji je u FBX-u na `x=0` levom kraju u Unity-ju
je na **desnom** kraju, komad se pruža ka **−X**. Zato se svaki zidni komad postavlja preko
`AirportKit.PlaceRun`/`LayRun` (koji čitaju `RunOf` iz stvarnih bounds-a), a nikad po pretpostavljenom
smeru — tako je i u luci. Y i Z se poklapaju 1:1.

---

## 10. Urađeno (2026-08-19, isti dan) — kod napisan, kompajlira se, geometrija proverena

Sve faze A–F su **odrađene u kodu**. Šta stvarno stoji na disku:

```
Assets/Scenes/AirportDemo.unity          bootstrap (GUID acee6182…) → AirportDemoBuilder
Assets/AirportDemo/
  AirportSpec.cs           sve mere (FAA + izmereni aseti), deli ih editor i runtime
  AirportKit.cs            ormar (193 provere na disku, 0 fali) + blok-alfabet za oznake
  AirportDemoBuilder.cs            parametri, Awake redosled, sunce/magla, kamera, cull
    .Ground.cs      FlatPlane podovi (pista, ramena, TWY, konektori + fileti, ramp,
                    servisni put, zadnji yard put) + Painter (batch quad-ova) + PaintLegend
    .Airside.cs     oznake piste (prag, brojevi 09/27 iz blok-fonta, osa, aiming point,
                    ivice, guma), TWY (osa, ivice, hold-short 2 pune + 2 isprekidane),
                    ramp (tie-down T, lead-in, stop bar, helidrom H+krug, servisni put),
                    svetla (ivična bela/žuta 60 m, prag zeleno/crveno, TWY plava, PAPI ×2,
                    jarboli), vetrokaz + segmented circle + taksi/hold table
    .Buildings.cs   postavljanje bake-ova (svi front +Z → yaw 180), ograda po modulu +
                    bodljikava + 2 kapije sa rampom/portirnicom, WalkObstacles blokovi
    .Landside.cs    kerb petlja (jednosmerna), nadstrešnica, parking sa obeleženim
                    mestima, StreetKit ulica u segmentima + 3 raskrsnice + spur i 2
                    kapijska puta, kerb nameštaj (govornice, kiosk, taksi, bus, klupe)
    .Detail.cs      avgas ostrvo, kupe, teretno dvorište, dvorište radionice, zastave,
                    kamere, ptice
    .Life.cs        pravljenje aviona/helikoptera/GSE/saobraćaja/ljudi
    .Perf.cs        cull slojevi 20/21/22 (260/340/620 m), flat-caster, merge 150 m
  Aircraft.cs       jedan avion: noge putanje, propeler+blur, vrata na pravim šarkama
  FlightOps.cs      taksi-graf (BFS), RunwayController (jedan korisnik, sletanje ima
                    prednost), stanja Parked→StartUp→Taxi→Hold→LineUp→Roll→Climb→
                    Cruise/Circuit→Final→Rollout→Shutdown, levi školski krug
  Rotorcraft.cs     helikopter: spool, lift, tranzit, povratak, sletanje, rotor blur
  AirportDriver.cs  vozilo na ruti (gap, skretanje, vučene prikolice po tragu)
  GroundOps.cs      cisterna po pozivu, prtljažni voz, follow-me, teretni kamioni
  AirportPeople.cs  AirportWalker (PedestrianAgent + WalkObstacles.Steer) + spisak
  AirportTraffic.cs kerb petlja, ulica sa wrap-om, taksi na stajalištu
  Editor/AirportKitBash.cs (+.Buildings +.Field)  versioned bake → Assets/CityKit/Airport
  Editor/AirportKitProbe.cs    Tools/City/Probe Airport Kit + provere aviona/hangara
  Editor/AirportDemoAutoBake.cs [InitializeOnLoad] pre Play-a
Docs/airplan.py     240 provera geometrije (bez editora)
```

### Odstupanja od plana i zašto
1. **Hangarska vrata nisu kitbash.** Izmereno je da `SM_Bld_Wall_Metal_Door_Slide_02` ima
   **6 × 6 m** — tri takva krila daju otvor 18 m, a avion je širok 13.64. Hangar je zato
   običan gang-warfare plašt (3 m modul, 2 reda zida = 6 m) sa generisanim dvovodnim
   krovom, a ne poseban model vrata.
2. **`DemoVehicle`/`RoadEdge` se NE koriste.** RoadDemo je u trenutku pisanja u sredini
   refaktora (`LaneNet`/`RoadCar`/`District`, druga sesija) i ne kompajlira se iz zastarelog
   Bee `.rsp`-a. Landside saobraćaj je zato `AirportDriver` na rutama — jednosmerna petlja
   i kapijski put ionako nemaju raskrsnice sa pravom prvenstva. Od RoadDemo-a se koristi
   samo stabilno: `PedestrianAgent`/`PedClips`/`CrewKit`, `WalkObstacles`, `WheelSpin`,
   `CarOccupant`, `StreetKit`, `DemoCamera`/`DemoShadows`, `CrewDemoPace`.
3. **Kontrolni toranj**: Base kit šaht 5 × 5 na kancelariji 12 × 9, sa Prison
   `Tower_Top_01` kabinom (6.09 m) koja prepušta preko galerije, far na krovu; PalmCity
   Tower zidovi su ispali zaobljeni segmenti (5.26 × 4.56) i nisu upotrebljeni.
4. **Cisterna/prtljažna kolica/stepenice se ne bake-uju kao vozila** — bake je samo telo
   (tank, kolica, stepenice), a nosač je pack kamion/golf kart kome `WheelSpin` vrti točkove.
5. **Flapovi se ne animiraju** (Synty delovi pivotiraju u centru modela, pa bi zamah bio
   pogrešan); propeler i vrata jesu — vrata kroz šarke rekonstruisane iz bounds-a.
6. **Faza G (veći avioni) nije rađena** — čeka vizuelnu proveru osnovne scene.

### Ispravke koje je našla provera (`Docs/airplan.py`)
- 3. red tie-down-a je repovima ulazio u servisni put → korak reda 26 → **24 m**, početak 134 → **130**.
- teretna kapija je bila **u sredini teretne hale** (x=208) → pomerena na **x=242**.
- radionica 30 m duboka probijala je ogradu → **26 m**.
- aiming point trake (`AimingBarInner` 10.5) izlazile su van ivice piste → **5.5 m**
  (unutrašnja ivica od ose, po AC 150/5340-1 za pistu od 100 ft).
- centralna crta se crtala dvaput na sredini; kapijski putevi su bili FlatPlane preko trave
  → sada StreetKit sa T-raskrsnicama na ulici; dodat zadnji yard put uz ogradu.

### Provere koje su prošle
- **Kompajl**: `Assembly-CSharp` i `Assembly-CSharp-Editor`, svi fajlovi sa diska, **0 grešaka**
  (Roslyn iz Unity 6000.5.6f1, flagovi iz Bee `.rsp`-a; lista fajlova iz `find`, jer je
  `.rsp` zastareo zbog tuđeg refaktora).
- **Geometrija** (`python Docs/airplan.py`): **240 provera, 0 padova** — nema preklapanja
  zgrada, ništa nije u RSA ni u TWY object-free zoni, nijedan parkiran avion se ne preklapa
  ni sa drugim ni sa zgradom, svi stajanke/hold tačke su povezane u taksi-grafu, prag i
  aiming point staju u širinu piste, hangar prima avion sa rezervom.

### Šta NIJE provereno (sledeći korak)
**Ništa od ovoga još nije viđeno u Play-u.** Prvi korak:
1. Otvoriti `Assets/Scenes/AirportDemo.unity` → auto-bake ispeče kit (ili ručno
   `Tools/City/Catalog/Rebuild Airport Kit (Kit-Bash)`), pa `Tools/City/Probe Airport Kit`
   → `Logs/airport-kit-probe.txt` (traži `MISSING` i `FAIL`).
2. Play: konzola bez `[AirportDemo] missing prefab` i bez `not baked yet`.
3. Pogledati u Prefab Mode: `airport-hangar-box`, `airport-terminal`, `airport-tower`,
   `airport-windsock` — orijentacija zidova (Unity ogleda X, sve ide kroz `PlaceRun`),
   da li krov seda na zidove, da li kabina tornja stoji na galeriji.
4. U Play-u pratiti: uzleće li avion sa pravog kraja (vetar zapadni → pista 27), staje li
   na hold-short liniji, da li dva aviona ikad dele pistu, vraća li se krug na final,
   ide li cisterna do onog ko je ugasio motor.

---

## 11. Simple Airport: prave letelice, i polje naraslo da ih primi (2026-08-19, isti dan)

Korisnik je dodao paket **`Assets/SimpleAirport`**. Odluka i pravilo (u memoriji,
[[aircraft-from-simple-airport]]): **iz tog paketa uzimamo SAMO letelice.** Njegovi ljudi,
kola, GSE, zgrade i props se ne diraju — drugi je likovni stil i mešanje se odmah vidi.
Avion je izuzetak jer ga Synty praktično nema, a uvek je na pisti/apronu, odvojen od gomile.

### Flota (`AirportKit`, sekcija „the fleet")
| Klasa | Prefabi | Raspon | Uloga |
|---|---|---|---|
| Trojka (727) | `Jet01..03` | **33 m** | jutarnji let, stajanka 1 |
| Dvojka | `Jet04..05` | 33 m (rezerva) | zamena ako trojke nema |
| Komjuter turboprop | `Plane_Propellor01`, `Plane01..03` | **27 m** | linija, stajanka 2 |
| Laki jednomotorci | `Small_Plane01..04` | **11 m** | tie-down redovi, školski krug |
| Helikopteri | `Small_Heli01..03` | rotor **10 m** | pad + patrola/čarter |

Modeli su nosom na **+Z**, točkovi `Wheel01/02` (glavni) + `Wheel03` (nosni), propeleri
`propellor01/02` odnosno `Propellor`, rotori `rotor_main`/`rotor_tail`. Import razmera paketa
nije projektova, pa se **svaki model skalira u runtime-u na raspon svoje klase**
(`Aircraft.Bind(tf, span)`, `Rotorcraft.Bind(tf, rotorDiameter)`) — nikad se ne veruje da
prefab dolazi u metrima.

### Polje naraslo na ADG III
Trojka traži drugu razmeru, pa je ceo `AirportSpec` prepravljen sa ADG II na **ADG III /
kategorija C**: pista **1800 × 45 m** (bila 1500 × 30), ramena 7.5, RSA ±76, prag **12 traka**
(bilo 8), aiming point unutrašnja ivica 11.25; TWY A na **z = 122** (400 ft od ose piste),
široka **18 m**, hold-short 76 m, object-free ±40; ramp z **150–265**, tie-down redovi 178/198/218
(korak 15 m za 11 m krilo), **stajanke na x ±45**, nos staje na z 246; servisni put 272, linija
zgrada **280**, terminal **80 m širok**, ograda 315, ivičnjak 326, petlja 332/356, parking 368–412,
ulica **432**; mapa ±1050 × −300…500. Kamera: pivot z 200, distanca 330, far clip 4000.
`AirportKitBash.Version` → **2** (terminal širi, toranj viši) — pri sledećem otvaranju scene
sam se pere i peče iznova.

### Brzine po klasi
Svaki avion nosi svoje: laki 30/40/28 m/s (rotate/climb/approach) i zalet 450 m; komjuter
48/62/45 i 800 m; mlaznjak **72/95/68 i 1250 m**. Izlaz sa piste posle sletanja bira se po klasi
(`ExitConnector(a)`): laki skreće posle 420 m, komjuter 700, mlaznjak 1100 — pa mali avion
skrene na prvu, a trojka produži.

### Greške koje je uhvatila nova provera putanja
U `Docs/airplan.py` je dodata **simulacija putanja leta za oba smera vetra** (306 provera ukupno).
Odmah je našla **četiri obrnuta znaka** u `FlightOps` koje bi inače video tek u Play-u:
- zalet je išao **od praga u pogrešnu stranu** (`ThresholdX - RunSign*TakeoffRun` → `+`),
- penjanje posle poletanja vraćalo se **nazad preko aerodroma** umesto da produži (`DepartureX - RunSign*…` → `+`),
- odlazak van mape isto,
- **downwind je išao sredinom piste** umesto da se vrati iza praga (`- RunSign * -500` → `- RunSign * 500`).

Ispravljeno je i: propeler-matching je hvatao i sam trup (mesh se zove `Plane_Propellor01`, pa se
ceo avion vrteo oko nosa) → sada ime mora da **počinje** sa „propell"; propeler se vrti od svoje
zatečene rotacije, ne resetuje se; avioni su vraćeni na **default sloj** (bili su na Mid sloju sa
kulingom na 620 m, pa bi avion na finalu od 1.7 km iskakao niotkuda); model se meri poravnat na
koordinatnom početku pre skaliranja.

### Stanje
Kompajl `Assembly-CSharp` + `-Editor`: **0 grešaka**. `python Docs/airplan.py`: **306 provera, 0 padova**
(raspored + putanje leta). **I dalje ništa nije viđeno u Play-u** — prvi korak je isti kao u §10,
s tim da se kit sada peče iznova (verzija 2).

### 11.1 Zašto su avioni bili ljubičasti

`SimpleAirport` je paket za **Built-in render pipeline**: svi njegovi materijali sede na
`Standard` shaderu (`m_Shader: {fileID: 46, guid: 0000000000000000f000000000000000}`), a
projekat je URP, koji taj shader ne ume da nacrta → magenta. Svi avioni dele **jedan**
materijal `Assets/SimpleAirport/Materials/SimpleAirport.mat` (atlas `Textures/SimpleAirport.png`).

Rešeno u `Assets/AirportDemo/Editor/SimpleAirportUrp.cs` — meni
**`Tools/City/Catalog/Convert Simple Airport To URP`** (priority 7). Prolazi kroz svih 37
materijala paketa, čita šta su nosili na Standard-u (`_MainTex`, `_Color`, `_BumpMap`,
`_EmissionMap/_EmissionColor`, `_Metallic`, `_Glossiness`, `_Cutoff`, `_Mode`) i prebacuje ih
na `Universal Render Pipeline/Lit` sa `_BaseMap`/`_BaseColor`, normal keyword-om, emisijom i
alpha-clip-om gde je bio cutout (`Fence_Alpha`). Zove se automatski iz `AirportDemoAutoBake`
(pri prvom učitavanju i pre svakog Play-a na ovoj sceni); marker
`Assets/CityKit/Airport/SimpleAirportUrpVersion.txt` pamti da je urađeno, pa se ne ponavlja.

Menjaju se materijali **samog paketa**, ne kopije — magenta paket je pokvaren za sve što ga
dotakne, uključujući njegovu demo scenu, a Synty paketi u projektu su ionako već URP.
**Reimport paketa vraća Standard** → pustiti meni ponovo. Uz to, `MakePlane` u runtime-u
loguje upozorenje ako naiđe na materijal van URP-a, da se sledeći put ne pogađa.

### 11.2 Providni krovovi hangara i zaobljenje aviona

**Krovovi.** `AirportKitBash.Gable` je namotavao trouglove **suprotno** od svega ostalog u
projektu. Konvencija (`Slab`, `FlatPlane`, `Painter`): quad `a,b,c,d` → trouglovi `(a,c,b)` i
`(a,d,c)`, spoljna normala = `cross(c−a, b−a)`; provereno na podu koji se vidi. Gable je radio
`(a,b,c)`/`(a,c,d)`, pa je **svako lice krova gledalo nadole** i backface culling ga je brisao
iz pogleda odozgo — hangari, vatrogasna i teretna hala bili su bez krova. Ispravljeno; pet lica
(dve strane, dva zabata, potkrovna ploča) su provereni računski da gledaju napolje.
`AirportKitBash.Version` → **3**, pa se kit peče iznova.

**Zaobljenje aviona.** Paket importuje modele sa tvrdim normalama iz FBX-a
(`normalImportMode: 0`), pa se svaka faseta vidi kao faseta; Synty modeli u projektu koriste
**izračunate** normale (`normalImportMode: 1`). Dodat pass `SimpleAirportUrp.SmoothAircraft()`:
za 16 letelica postavlja `importNormals = Calculate`, `normalSmoothingAngle = 60`,
`weldVertices` i radi `SaveAndReimport`. Šezdeset stepeni zaobli trup i nos, a ostavlja oštru
napadnu ivicu krila i prelome repa (dve strane tankog krila su skoro leđa uz leđa, daleko van
praga). Menja se **samo osvetljenje, ne geometrija** — silueta ostaje ista. Meni
`Tools/City/Catalog/Smooth Simple Airport Aircraft` za probu drugog ugla; marker verzija → 2,
pa se pusti i sam pre Play-a.

### 11.3 Krov je pokazivao ceo atlas (v4)

Posle popravke namotavanja krov se video — ali je na sebi imao **celu Synty teksturnu stranu**,
swatch po swatch, tiled preko cele površine.

**Uzrok, i pravilo koje iz njega sledi.** Synty pack materijal je **atlas**: svako lice svakog
pack komada ima UV koji pokazuje na jedan mali swatch zajedničke strane. Farbanje *pack komada*
pack materijalom je ispravno — UV već pada gde treba. Ali mreža **generisana ovde** (krov, ploča,
tank) ima UV razvučen po sopstvenoj površini u metrima, pa atlas materijal **tila celu stranu**
preko nje → šarena tabla umesto lima.

Zato: `Metal` (gang atlas) se od sada daje **isključivo** `WallRun`/`Paint`-u za pack komade, a
sve generisano nosi **ravnu boju**. Konkretno:
- `Tinted()` sada čisti `_BaseMap`/`_MainTex`/`_BumpMap` (novi `Flatten`) — dotad je tintovana
  kopija vukla atlas izvora sa sobom, pa su i `Steel`/`Concrete`/`White`… bili u riziku.
- `Concrete`, `Plaster`, `Glass` više nisu sirovi pack materijali nego ravne tintovane boje.
- Novi `RoofMetal` (0.46/0.43/0.40, smoothness 0.22) ide na sva četiri gable krova — boja zidova,
  nijansu sivlja, da se zgrada čita kao jedna stvar iz vazduha.
- Krovna svetla radionice su bila **unutar** krova (y = 10.6 pod slemenom na 12) gde ih ništa ne
  može videti; zamenjena **slemenskom lanternom** (kutija na slemenu + zastakljene strane), što je
  i realan način da se osvetli hala te dubine.

`AirportKitBash.Version` → **4**.
