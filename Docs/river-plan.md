# Plan: reka, kej i šetalište (RiverQuay)

> Odobreno 2026-08-26 („udri s implementacijom"); odluke iz §7 uzete kako su predložene, uz dve
> izmene po korisnikovoj poruci istog dana: **strana reke je po seedu** (istok ili zapad) i
> šetalište je gušće (v. §5.6). **STANJE: URAĐENO** faze 0–4; ono što nije, u §5.7.
> Stranica: https://claude.ai/code/artifact/41b28c83-c3d9-44b2-972b-907df1099418
>
> U jednoj rečenici: **grad se završava na pravoj liniji** — redovi jezgra se poravnaju na obalu,
> njihove ivične ulice se stope u jednu **obalsku ulicu**, iza nje je **šetalište** stalne dubine
> (pločnik, ograda, lampe, kafei, klupe, palme, ringišpil jednom), pa **kej**, **voda** i **druga
> obala** na koju vode **2–3 mosta** — **svi pokretni** (PalmCity bascule), dižu se pred jedrenjakom koji
> plovi celom rekom (korisnik 2026-08-26: „svi mostovi treba da se dižu"). Isti raster i ista presuda kao za blokove i park (`CoreRoads`), ista podela
> plan/kompozer kao park (`QuayWalk`/`QuayBlocks`), isti mostovski komplet kao reka u
> `Game.unity` (`DressBridge`).

| šta | gde (novo) |
|---|---|
| linija reke u jezgru: poravnanje redova, obalska ulica, kej kao jedinica, mostovi kao trake | `Assets/RoadDemo/CoreLayout.cs` (`Align`, `Quay`, `plan.Water`, `WithGround`) |
| raster zna vodu; most = put preko vode; presuda | `Assets/RoadDemo/CoreRoads.cs` (`Kind.Water`, `Oddities`) |
| plan šetališta (sekcije, sobe, programi, presuda) | `Assets/RoadDemo/QuayWalk.cs` (čist C#, `Tools/CoreSim` ga kompajlira) |
| kompozer šetališta (zid, ograda, lampe, kafei, pristan, ringišpil, čamci) | `Assets/RoadDemo/QuayBlocks.cs` (delegat za instanciranje kao `ParkBlocks`) |
| mostovi: svi pokretni (PolygonCity prilazi + PalmCity bascule) | `Assets/RoadDemo/RiverBridge.cs` + `Bascule` (MonoBehaviour: dizanje, rampe) + `RiverBoat` (čamac koji ih zove) |
| voda i druga obala u jezgru | `CoreDistrict.StandQuay()` / `CoreCitySketch.Quay()` |
| laboratorija | `Tools/City/River/Sketch The Quay`, `unity command gangsters_quay --seed N --depth D --length L [--draw]` |
| presuda bez editora | `Tools/CoreSim --quay [--sweep]` |

---

## 0. Šta postoji (analiza, 2026-08-26)

### 0.1 Reka u grid gradu (`Game.unity`) — `RoadDemoBuilder.Seams.cs`

Reka tamo nije kvart nego **šav** (`SeamKind.River`): jedan procep mreže ulica koji nije blok,
širine 100–150 m (`RollSeams`), uvek sa ulicom blokova spolja („grad uvek ima ulicu blokova van
svoje vode"). Šta `BuildRiver` gradi:

- **kej**: `SM_Env_WaterEdge_Straight_03` (PolygonCity) po jedan na 5 m duž obe obale, 18 %
  pohabani `_02`, 6 % ispust `_Pipe_01`, `_Corner_01` na krajevima; ispod mosta `SM_Env_Bridge_Wall_01`
  („Abutment");
- **most**: NEMA prefaba mosta i NE koristi `DeckMesh` — most je **običan kolovoz** (iste pločice,
  isti lane graf, nikakav novi čvor) plus dressing `DressBridge`: hodnik `SM_Env_Bridge_Edge_01`
  (5 m) sa obe strane, podgled `_Underside_01` pod svakom ćelijom, nosač `_Support_01` (23,4 m) na
  **svakih 15 m**, stubići `_Pillar_01`, `SM_Prop_Pier_Lamp_01` na 16 m;
- **jedino šetalište koje postoji**: lampa/klupa/kanta na 9 m duž obale (`:546–567`), i pravilo
  da ulica uz reku **ne dobija** obični trotoarski dressing sa strane vode — „ta strana je
  šetalište, oprema ga reka" (`RoadDemoBuilder.cs:2604`);
- **voda**: jedan skalirani `SM_Env_Water_Plane_01` (PNB_Core) — jedan renderer ma koliko velik,
  na **`WaterY = −2,65`**; pena uz kej ide iz depth buffera sama; podešavanje pene postoji samo u
  luci (`TuneSurf`, poznat bug sa `_Shore_Foam_Noise_Scale`);
- **čamci**: PalmCity Power/RIB/Party/Sailboat na 34–62 m uz obalu.

Ostrvo useca kanal reke u heightfield (`RiverBank = 14 m`, `RiverClear = 26 m`), kvartovi se drže
dalje od kanala (`RiverGap`). **Nigde nema pokretnog mosta, klase šetališta ni vode u rasteru jezgra.**

### 0.2 Linija vode u luci — `HarborDistrict.Waterline.cs`

Ritam koji već stoji i koji šetalište nasleđuje: bokobrani na 9 m, merdevine na 60 m, kolut za
spasavanje na 27 m (uz lampe), konopac na 18 m, bitve na 9 m, lampe na 27 m; `HarborKit.Prop/Sit/
Hang/Span` je API „stvari duž linije na izmerenom odstojanju".

### 0.3 Jezgro — `CoreLayout.Roll`

Redovi idu **istok–zapad** (jedinice uz X), bulevar duž X na z −10..25, redovi se slažu severno i
južno, pojas parkova su **poslednja dva reda**. Ulice između redova i iza poslednjeg su **deklarisane
preko cele širine** i **produžene 30 m** iza ivičnih ulica pa slepo završene (T umesto L-ugla u kom se
saobraćaj zaključavao). **Na istoku i zapadu nema puta** — redovi su nazubljeni (dužina 40–60 ćelija
+ pomak ±20 m), raster je svaka kutija bloka + 15 m. Presuda (`Raster.Faults`): golo tlo, strana
bloka bez puta, batrljak, sudar dve kutije. CoreDemo **nema tlo** ispod jezgra (ostrvo ga daje u gradu).

### 0.4 Kako Synty pravi obalu — PalmCity `Demo.unity`, izmereno

Skeniran offline (grupe `City`/`Estate`/`Environment`, sve pozicije svedene na svet):

| deo | mera |
|---|---|
| **Wharf** (mol) | 14 m širok × 80 m; `Street_Lamp_08` (3,1 m) u **parovima na obe ograde na 10–15 m**; klupe `Bench_Seat_01` u parovima licem u lice kod korena; radnje `Beach_Shop_01/02` (4,5×3,3 m) kod korena; kafe na vrhu: `Table_02` (1,2 m) + 4 `Chair_01` + `Umbrella_02` (3,25 m), **4 stola na 10 m**; `Binoculars_01` i pecaroški štapovi na ogradi; stepenice do vode |
| **Fairground** (vašar) | **30 × 40 m ograđeno dvorište** (`Barrier_Fence` + `Bld_Fence`) na `Gen_Env_Road_Small` asfaltu; `SM_Prop_Ferris_Wheel_01` **30,7 × 31,5 m visok × 13 m dubok** (u demou skaliran 0,93); `Juice_Cart`, 7 `Table_Outdoor_01`, suncobrani, kante |
| **ivica grada ka vodi** | `SM_Bld_Wall_Retaining_01` (2,5 × 1,5 m) u nizu, `SM_Bld_Wall_Railing_01` (2,5 × 1,0 m) **3 m iza zida**, ivica trotoara **16–20 m iza zida** → traka grad–voda je 16–20 m |
| **Marina** | plutajući dokovi `Dock_Platform_01/02` (2,5 m pločice) na **y = −2,2** (voda −2,65 → 0,45 m nadvođa) i na +0,8; `Dock_Pole`, `Dock_Railing_01` (2,5 × 1,14 m); `Dock_Stairs_01` diže **1,56 m** |
| **Drawbridge** | **dva krila 40 m razmaknuta, licem u lice** (baze na z 18 i 58, yaw 0/180) = dvokrilni bascule; krilo `SM_Env_Drawbridge_01` **10,63 m široko × 20,0 m od pivota** (pivot na kopnenom kraju, 1,7 m unutra); baza `_Base_01` **17,4 × 5,35 × 10,2 m visoka, 5,64 m ispod pivota**. **Statični meševi, bez animacije**; krilo je zaseban prefab pa se diže **rotacijom oko sopstvenog pivota**. Nijedan C# ga ne koristi |
| **čamci** | RIB 4,7 m, Power 10,2, Party 11,7, Sailboat 11,2 (**jarbol 13,7 m** — razlog što se most diže) |

**Ringišpil**: ni u jednom paketu nema vrteške (carousel/merry-go-round) — „ringišpil" = **Ferris
wheel**. Već ima rotator (`DemoFerrisWheel`, 1,3 o/min) i već je landmark jednom po gradu
(`PalmBlock_07` = vašarski blok, v. [[landmark-once-per-city]]).

### 0.5 Izmereni prefabi (`gangsters_measure`, 2026-08-26)

| prefab | paket | mera (x × y × z, m) | napomena |
|---|---|---|---|
| `SM_Env_WaterEdge_Straight_03` | City | 5,0 × 5,43 × 1,5 | pivot na vrhu, visi 5 m; kej cele reke u Game |
| `SM_Env_WaterEdge_Corner_01` | City | 5,25 × 5,67 × 5,15 | ugao keja |
| `SM_Env_Bridge_Edge_01` / `_Underside_01` | City | 5,2 × 3,5 × 5 / 5 × 0,65 × 5 | hodnik / podgled, po ćeliji |
| `SM_Env_Bridge_Support_01` | City | 23,4 × 3,6 × 3,35 | nosač na 15 m |
| `SM_Env_Drawbridge_01` | PalmCity | 10,63 × 3,81 × 21,69 | krilo, 20 m od pivota |
| `SM_Env_Drawbridge_Base_01` | PalmCity | 17,38 × 10,21 × 5,35 | baza/kula, 5,64 m pod pivotom |
| `SM_Prop_Ferris_Wheel_01` | PalmCity | 30,73 × 31,52 × 12,98 | 74 renderera, `_Rotate_01` dete |
| `SM_Bld_Wall_Railing_01` (+`_Pillar_02`) | PalmCity | 2,5 × 1,0 × 0,23 | ograda šetališta |
| `SM_Bld_Dock_Platform_01/02` | PalmCity | 2,5 × 0,84 × 2,5 | plutajući dok |
| `SM_Bld_Dock_Stairs_01` / `Dock_Railing_Stairs_01` | PalmCity | 2,77 × 1,56 × 2,43 / 2,5 × 2,64 | stepenice, ograda stepenica |
| `SM_Prop_Pier_Lamp_01` | PalmCity | 0,4 × 4,24 × 0,7 | lampa keja, `DemoStreetLamps` je pali |
| `SM_Prop_Pier_Bench_01/02`, `Bench_Seat_01` | PalmCity | 1,48 / 2,16 / 2,21 m | klupe |
| `SM_Bld_Beach_Shop_01/02/03` | PalmCity | 4,5 × 3,3 × 3,3–3,8 | kiosk |
| `SM_Prop_Umbrella_02`, `Table_02`, `Chair_01` | PalmCity/City/CoffeeShop | 3,25 / 1,2 / 0,56 m | terasa |
| `SM_Veh_Juice_Cart_01` | PalmCity | 4,2 × 6,4 × 4,0 | kolica na vašaru |
| `SM_Prop_Bollard_02`, `Bollard_Chain_01` | PalmCity | 0,19 × 1,26 / 2,07 m | bitva + lanac (pristan) |
| `SM_Prop_Binoculars_01` | PalmCity | 0,56 × 1,56 | dvogled na ogradi |
| `SM_Env_Water_Plane_01` | PNB_Core | 50 × 50 | jedan renderer, skalira se |

Cela tabela mostovskog i dok kompleta već stoji u `Logs/seam-kit-probe.txt` (`SeamKitProbe`).

---

## 1. Šta je rečna obala u američkom gradu 1987 (pravila koja recept sprovodi)

1. **Downtown se završava na reci; glavna ulica ide do nje i prelazi je.** Jacksonville (Main St →
   most), Detroit (Woodward → Hart Plaza), Portland (Burnside). → bulevar je **upravan** na reku,
   njegov most je najveći.
2. **Između zgrada i šetališta je obalska ulica** — Wacker Drive, Biscayne Blvd, Coastline Drive.
   Šetalište je iza ulice, ne zalepljeno za fasade (evropska/San Antonio slika je izuzetak).
3. **1987 je vrhunac „festival marketplace" obale**: Harborplace (Baltimore 1980), South Street
   Seaport (1983), **Bayside Miami (april 1987)**, **Jacksonville Landing (jun 1987)**; Portland je
   1978 srušio autoput za park uz reku; Cleveland Flats su 80-ih bar-četvrt na radnoj reci ispod
   mostova koji se dižu. → šetalište = kiosci i kafei, klupe okrenute vodi, livene lampe, bitve sa
   lancem, turistički brodić; deo šetališta ispred parka pojasa je park.
4. **Pokretni mostovi**: bascule (Chicago, Miami, Fort Lauderdale) se dižu za jedrilice i teretnjake,
   saobraćaj čeka na rampama (Blues Brothers 1980 skaču preko 95th Street mosta). → ~~bulevar fiksni, jedna
   ulica pokretni~~ → **svi mostovi pokretni** (korisnik 2026-08-26); bulevar = krilo po kolovozu,
   kao Michigan Avenue (1920): dva uporedna bascule mosta pod jednim bulevarom.
5. **Ferris wheel na downtown obali 1987 ne postoji** (Navy Pier 1995) — točak pripada **vašaru /
   zabavnom molu na kraju šetališta** (Coney Island, Santa Cruz Boardwalk). → ringišpil **jednom**,
   u **krajnjoj sobi** šetališta, u ograđenom dvorištu sa kolicima i stolovima; nikad usred esplanade.
6. **Radna reka**: industrija preko vode je period (Cleveland, Pittsburgh, čikaški South Branch). →
   druga obala je mesto za budući industrijski/rezidencijalni kvart; dok ga nema — **prazna**
   ([[leave-it-empty-dont-guess]]).
7. **Palme su trotoarsko drveće** ovog paketa; šetalište JESTE trotoar → palme i žardinjere na
   šetalištu, gradsko drveće (`SM_Env_Tree_01..03`) u travnatim sobama, palme **nikad** u travi
   (lekcija parka).

---

## 2. Kako radi

### 2.1 Linija (jedna prava ivica)

```
 zapad                                                                        istok
 ┌──────── red ─────────┐15│ obalska │ šetalište (D=5–8 ćelija) │kej│   voda (14)   │far│k│
 │ blok │ blok │ blok   │  │ ulica   │ ivičnjak · sobe · ŠETNJA │zid│ ~~~~ bascule ~~│put│ │
 └──────────────────────┘  │ 3 ćel.  │        + ograda          │   │  40 m kanal    │ 3 │1│
     ragged (pomak ide      ↑ sve ivične ulice redova se POKLOPE     ↑ most = traka preko vode
     na zapad)              u jednu ulicu preko cele visine          ↑ druga obala: put + ivičnjak
```

- Reka je na **istoku** jezgra (`RiverSide`; okvir kvarta u gradu je okreće gde treba), **upravna na
  bulevar**. Bulevar, ulice između redova i „park drive" iza pojasa svi stižu na obalu.
- Redovi se **poravnaju na istok** (`Align`): svaki red završava na istom `EastEdge`; pomak ±20 m
  koji je birao poklapanje poprečnih ulica sada radi samo na zapadnom kraju (poklapanje ostaje
  napomena, ne greška — kao i danas). Time se sve **istočne ivične ulice redova stope u jednu
  obalsku ulicu** od 15 m preko cele visine jezgra + pojasa — deklarisana kao prva **vertikalna**
  traka (`Declare(vertical:true)` već postoji).
- Zašto poravnanje, a ne šetalište promenljive dubine: ista lekcija kao kod pojasa („3–4 mala
  parka se čitaju kao preostalo tlo") — šetalište koje je 20 m kod jednog reda a 60 m kod drugog
  čita se kao ostatak. **Jedna prava ivica se čita kao mesto gde grad prestaje**; plaza se širi
  samo tamo gde plan to hoće (ušće bulevara).
- Sve istok–zapad trake se **završavaju u obalskoj ulici kao T** (bez L-ugla, bez batrljka od 30 m
  na ovoj strani); most je traka koja **nastavlja preko šetališta i vode** do puta na drugoj obali.
- Ćelije, sa zapada: obalska ulica 3 · šetalište D (5–8, izbor po seedu) · kej 0 (zid je na liniji)
  · voda **14 (70 m)** · put druge obale 3 · ivičnjak 1. Na severu i jugu obalska ulica i put druge
  obale idu **30 m dalje od poslednje trake** (poznati slepi kraj), kej se završava ugaonim komadom,
  voda ide 200 m dalje (jedan renderer).

### 2.2 Raster i presuda (`CoreRoads`)

- Novi `Kind.Water` iz `plan.Water`: gola voda **nije** greška, ništa se na nju ne polaže (ravan
  vode je jedan renderer ispod svega). Traka deklarisana preko vode postaje put — **paluba**;
  pločice puta ostaju iste kao u `Game.unity`, most je dressing (2.5).
- Šetalište je **jedinica kao park** (`quay-N`: tlo, maska, bez zgrada) — u listu za raster ulazi
  kroz `WithGround` (danas `WithParks`); trake mostova mu iseku masku, pa je šetalište niz segmenata
  između prilaza mostova.
- Strana bloka koja gleda u vodu **je opslužena vodom** (ne broji se kao „strana bez puta").
- Nove provere (presuda mora da meri ono što broji, [[park-generator-lessons]]): nijedna gola
  ćelija između obalske ulice i zida; svaka istok–zapad traka se sastaje sa obalskom ulicom u T;
  svaki most je put od ivice do ivice **i sleće u T na putu druge obale**; nijedan segment
  šetališta kraći od 4 ćelije (inače se spoji sa susedom preko prilaza).

### 2.3 Šetalište — `QuayWalk` (plan, 1-D)

Za razliku od parka (2-D graf staza), šetalište je **niz uz liniju**: sekcije između prilaza
mostova, svaka isečena na sobe od 6–16 ćelija duž vode. Dve trake su **fiksne**, ostatak su sobe:

- **ŠETNJA uz ogradu** — 2 ćelije (10 m) uz zid: 1 m ograde sa lampama/klupama/dvogledom, 5 m
  čistog prolaza, 3–4 m klupa i žardinjera. Rezerviše se pre svega (`BookTheWay`), **neprekidna od
  kraja do kraja** — to je mera koju presuda proverava po onom što je stvarno stalo.
- **IVIČNJAK uz obalsku ulicu** — 1 ćelija: `CorePavement.Around/Lay` kao za svaki blok (ivične
  pločice, slivnici na 12, lampe na 20 m, kante, **palme na 10 ivičnjaka**), sa **zebrom naspram
  svake ulice koja stiže** (`Edge.Crossing` — konačno se prosleđuje; ušća su tu, ne na sredini).
- **Sobe** između (D − 3 ćelije duboke), podrazumevano **popločane** (`SM_Env_Sidewalk_01`, isti
  plato kao u parku), programi se biraju kao u parku — **najmanja soba u koju program stane**,
  travnjak je ostatak:

| program | min. soba (ćelije) | od čega | pravilo |
|---|---|---|---|
| **Plaza s fontanom** | 4 × 4 | fontana `ParkBlocks` (4,77 m) + PalmCity `Fountain_01`, klupe u prstenu | **obavezna na ušću bulevara** (glavni trg obale) |
| **Terasa (kafe)** | 4 × 3 | kiosk `Beach_Shop_01/02/03` ili `Shop_Stand_0x` uz ivičnjak, stolovi `Table_02` + 4 `Chair_01` pod `Umbrella_02` (3,25 m), **4 stola na 10 m** (mera Wharf-a), `Menu_Stand`, neon | 1–3 po šetalištu; nikad dve u susednim sobama |
| **Gaj** | 3 × 3 | travnato ostrvo, 3–5 `SM_Env_Tree_01..03`, klupe okrenute vodi | |
| **Pristan** | 4 × 2 uz zid | stepenice `Dock_Stairs` (1,56 m po kraku → **dva kraka** do −2,2), plutajući dok `Dock_Platform` + `Dock_Pole` + `Dock_Railing`, bitve s lancem gore, 1–2 čamca uz dok (`Party_Boat` = turistički) | 1 obavezan; ispod mosta nikad |
| **Vašar (ringišpil)** | **8 × 6** | ograđeno dvorište kao u demou: `Barrier_Fence` + `Bld_Fence`, `Ferris_Wheel_01` u **1×** (nikad ispod 1), `Juice_Cart`, 6 `Table_Outdoor_01`, suncobrani, kante | **jednom po gradu**, u **krajnjoj sobi** (uz park pojasa); ako soba nije dovoljno duboka — **nema ga i to se prijavi** (dubina D=8 se dela tako da bar jedan kraj stane) |
| **Pecaroši** | 0 (traka ograde) | `Binoculars_01`, `Fishing_Rod_01`, kofa | na 40–60 m, ne kod terase |
| **Travnjak** | ostatak | trava, po koja klupa | |

Program koji ne stane se ne skalira i ne stiska — soba ostaje popločana i izveštaj kaže šta je
falilo (mera, mesto). Mape: `~` voda, `=` most, `W` šetnja, `f` fontana, `c` terasa, `g` gaj, `d`
pristan, `F` vašar, `.` trava, `#` plato.

### 2.4 Linija vode — `QuayBlocks`

- **Zid**: `SM_Env_WaterEdge_Straight_03` na 5 m (18 % `_02`, 6 % ispust) na istoj visini kao reka
  u `Game.unity` (vrh u ravni šetališta na y = 0, voda na −2,65 → 2,65 m lica zida); ugao na
  krajevima; pod mostom `Bridge_Wall_01`. Reka u celom gradu v3 izgleda isto.
- **Ograda** na kruni zida: PalmCity `SM_Bld_Wall_Railing_01` (2,5 m) + `_Pillar_02` — ono što
  demo stavlja na ivicu svog grada; prekid samo na pristanu (tu bitve + lanac) i na prilazu mosta.
- **Lampe** `SM_Prop_Pier_Lamp_01` na **16 m** (ritam grid reke), pod sopstvenim imenom da ih
  `DemoStreetLamps` pali; **kolut za spasavanje uz svaku treću lampu, merdevine na 60 m** (ritam
  luke, `HarborDistrict.Waterline`).
- **Klupe** `Pier_Bench_02`/`Bench_Seat_01` okrenute vodi na ~9 m gde nema programa; kante uz lampe.
- **Voda**: jedan `SM_Env_Water_Plane_01` skaliran na ceo pravougaonik + 200 m; pena kao u luci
  (`TuneSurf`, kx/kz po dužini), bug sa `_Shore_Foam_Noise_Scale` se popravlja usput.
- **Čamci**: vezani uz pristane; u pokretu tek u §6.

### 2.5 Mostovi — `RiverBridge`

- **Koliko**: bulevar **uvek**, plus **1–2 ulice** među istok–zapad trakama na ≥ 60 m
  razmaka; **svi su bascule** (izmenjeno 2026-08-26, ranije samo jedan ulični). Sva tri slaze na **put druge obale** (sever–jug, 15 m,
  preko cele visine) pa saobraćaj kruži: most A → put → most B → nazad. Bez toga bi most vodio u
  slepi kraj na drugoj obali i kola bi se tamo okretala.
- **Prilazi** (fiksni deo svakog mosta) = `DressBridge` recept iz `Game.unity`, doslovno: kolovoz od istih pločica (traka
  preko vode), hodnik `Bridge_Edge` po ćeliji sa obe strane, podgled, nosač na 15 m (bulevar dva
  uporedo), stubići, lampe na 16 m. Lane graf nema pojma o mostu — kao ni danas.
- **Bascule** = kanal od **40 m** na sredini reke: dve `Drawbridge_Base_01` na ivicama kanala
  (17,4 m široke → kule sa obe strane kolovoza, 5,64 m pod pivotom = u vodi), dva krila
  `Drawbridge_01` po 20 m koja se sastaju na sredini; od keja do baze **fiksni prilaz** (15 m sa
  svake strane pri 70 m reke, iz istog kompleta). Krilo je **10,63 m široko = kolovoz od 10 m**
  (2 trake); hodnik `Bridge_Edge` uz krilo nastavlja trotoare. Pivot krila je na kopnenom kraju →
  **dizanje = rotacija krila oko sopstvene X ose** (prefab je zaseban, ništa se ne seče).
  **Bulevar: krilo po kolovozu** — baza i krilo na svakoj ivici kanala za svaki od dva kolovoza,
  centrirani na ±10 m od krune (trake su na 7,5 i 12,5 m), medijan između baza (2,6 m).
- **`Bascule` (MonoBehaviour)**: nema sat — zove ga **`RiverBoat`**: jedrenjak (jarbol 13,7 m,
  ne staje ni pod jedan spušten most) plovi celom rekom od jednog kraja vode do drugog i nazad
  (6 m/s, odmor 45 s na kraju). 90 m pred kanalom zove **najbliži most ispred sebe**: kapije
  padaju (nepokretni `RoadOccupant` preko cele širine 9 m ispred kanala — vozači ga prate kao
  parkiran kamion), most čeka da se kanal isprazni (≤ 20 s), krila gore (15 s); čamac čeka 15 m
  pred kanalom dok krila nisu gore, prođe, i 25 m iza kanala pušta most (krila dole 15 s, kapije
  gore). Zove se samo najbliži most, pa drugi ne stoji zatvoren dok čamac čeka na prvom.
- ~~Bulevar bascule ne dobija~~ → dobija, krilo po kolovozu (v. gore; korisnik 2026-08-26).

### 2.6 Druga obala

**Plato** od 3 ćelije (15 m, popločan, ivičnjak uz put; `plan.Aprons`, deonica između svaka dva
mosta) + put druge obale + 1 ćelija ivičnjaka i **ništa više**. Plato je dodat 2026-08-26 kad su
svi mostovi postali pokretni: prilaz mosta je 15 m, pa je red na istočnoj kapiji stajao u
raskrsnici puta druge obale (v. §5.6, pouka 4). Mostovi slaze u T; put ide
30 m dalje od krajnjih mostova i završava se slepo. U gradu v3 tu se pinuje sledeći kvart
(industrija preko reke je period, rezidencijalno takođe); dok ga nema, tlo iza ivičnjaka je ono što
je iza jezgra i danas — ništa. Ne izmišljati kulise ([[no-backdrop-scenery]]).

---

## 3. U jezgru (`CoreLayout.Roll` + `CoreDistrict` + `CoreCitySketch`)

- `Roll`: posle redova, `Belt` i `Reshape` — **`Align(rows, east)`**, pa **`Quay(plan, rows, dice)`**:
  `plan.Quays` (segmenti šetališta kao `Block` sa maskom), `plan.Water` (pravougaonik), trake:
  obalska ulica (vertikalna), put druge obale (vertikalna), mostovi (horizontalne, od istočnog
  ivičnjaka obalske ulice do zapadnog ivičnjaka puta druge obale). Izbor mostova: bulevar + 1–2
  trake ≥ 60 m razmaknute; svi su bascule.
- `WithParks` → `WithGround` (parkovi + šetalište). `Arrange` ne menja ništa: isto deljenje, ista
  „prvo čisto".
- `CoreRoads.Build`: `Kind.Water`, deklarisano preko vode = put, presuda iz 2.2.
- `CoreDistrict.Plan`: `StandQuay()` posle `StandParks()` — `QuayWalk.Lay` + `QuayBlocks.Compose`
  **u koordinatnom početku pa pomeraj korena** (isto kao park, jer svaki komad stoji po izmerenim
  world bounds), `RiverBridge.Dress` po traci preko vode, ravan vode; `Reserve`: `into.Sea(water)` u
  gradu (ostrvo tada usecka svoj kanal po liniji jezgra, v. §4). `Bascule` se instalira sam u `Build`
  (bez menija).
- `CoreCitySketch.Quay()` isto preko `PrefabUtility`; `CoreDemo.unity` u Play crta jezgro + pojas +
  reku; `gangsters_core --draw` isto.

---

## 4. Grad v3: reka je gradska linija

Reka je **linija grada**, ne jezgra: `CityLayout` (v3) dobija `River` — pravu sever–jug liniju kroz
ostrvo do mora na oba kraja (kanal već zna da se useče: `RiverBank`/`RiverClear`), jezgro joj pinuje
istočnu ivicu. Svaki kvart koji dodirne liniju polaže **svoj deo keja istim `QuayWalk`-om**, samo sa
drugim programima:

| kvart | šta stoji na liniji |
|---|---|
| jezgro | šetalište iz §2 (plaza, terase, pristan, vašar) |
| pojas parkova | isto šetalište, tanje (šetnja + trava + gaj) — **park koji izlazi na reku** je najbolji trenutak pojasa; ivica parka ka obalskoj ulici je ivičnjak kao i danas |
| rezidencijalno (buduće) | obalski put + šetnja, bez terasa |
| industrija (buduće, druga obala) | kej luke (`HarborDistrict.BuildQuay`), bitve, bokobrani |

Redosled ostaje: jezgro → pojas (≥ 2 strane, sever i jug) → reka na istoku → rezidencijalno na
zapadu i preko reke.

---

## 5. Alati i redosled rada (svaka faza sa proverom)

| faza | šta | provera |
|---|---|---|
| **0 — papir** | `Align` + `Quay` u `CoreLayout`, `Kind.Water` u `CoreRoads`, `Tools/CoreSim --quay` | 30 seedova **30/30 čisto**, prosek deljenja ne gori od današnjih 3,57; Synty referenca (seed −1) netaknuta (0 grešaka, 21 ulica) |
| **1 — plan šetališta** | `QuayWalk.Lay` + `--sweep` preko D = 4..8 × L = 40..160 ćelija × 2–3 mosta | 0 grešaka na svim merama × 3 seeda; svaki izveštaj puni neko stvarno polje (šetnja neprekidna, svako ušće ima zebru, nijedna gola ćelija, vašar ≤ 1) |
| **2 — kompozer** | `QuayBlocks` + `Tools/City/River/Sketch The Quay` + `gangsters_quay --draw` | u editoru: 0 propsa na šetnji, 0 m ograde koja fali, lampe na 16 m ± 0,5, nijedan prefab ispod 1×, vašar jednom; **screenshot korisniku pre ugradnje** |
| **3 — jezgro s rekom** | `CoreCitySketch.Quay`, `CoreDistrict.StandQuay`, `RiverBridge.Dress`, voda | `gangsters_core --seed 1 --draw` (0 grešaka), harness `gangsters_play --scene CoreDemo --seconds 180` ×5 sa 24 kola → PASSED (mostovi + petlja druge obale ne zaključavaju) |
| **4 — bascule i čamci** | `Bascule`, blok trake, jedrilica kroz kanal | harness sa mostom koji se diže na 90 s (ubrzano) ×5 → bez zaključavanja, kola čekaju i kreću; čamac prođe svaki ciklus |
| **5 — grad v3** | `CityLayout.River`, kanal ostrva po liniji, pojas na obalskoj ulici, pin druge obale | `gangsters_layout` 30 seedova: reka uvek do mora oba kraja, nijedan kvart preko kanala bez mosta |

Sve u čistom C# što može (`QuayWalk`, `CoreLayout`, `CoreRoads`) da CoreSim sudi bez editora; C#
prvo review (`code-review-unity`), pa test — hook to i traži.

### 5.6 Šta je urađeno i šta je merenje promenilo (2026-08-26)

| faza | mera |
|---|---|
| 0 | `CoreLayout.River` + `Kind.Water`/`Outside` u rasteru: 30/30 seedova čisto; Synty referenca 0 grešaka. Prvi prolaz: prosek 3,83 deljenja (bez pomaka redova) |
| 1 | `QuayWalk`: sweep 1280 traka (D 5–8 × L 1–160 × 4 vrste krajeva) **1280/1280 čisto** strukturno; 30 jezgara 30/30. **Pregled pri commit-u (2026-08-26)**: presuda plana je merila samo ono što `Lay` sam garantuje (nije mogla da padne) → sada broji i **tražnju bez sobe** (vašar/pristan/dajner traženi, a ušća nisu ostavila sobu): 30 jezgara **28/30** (dva dajnera), sweep sa tražnjom vezanom za dužinu **499/1280 (39 %)** — sintetička ušća na 3–11 ćelija od kraja retko ostave krajnju sobu od 8 |
| 2 | `QuayBlocks` u laboratoriji: 0 ćelija bez poda, 0 m ograde koja fali; prvi crtež — 0 lampi (noga lampe 25 cm u stazi → lampe uz ogradu), 86 stolova (terasa = 2 reda uz kiosk), štap za pecanje 1,8 m u prolazu (leži uz ogradu po izmerenoj širini) |
| 3 | jezgro + reka u `CoreCitySketch`/`CoreDistrict`, `RiverBridge` (voda, zidovi, mostovi, bascule krila), harness `CoreDemo` ×5: v. dole |
| 4 | `Bascule` MonoBehaviour: kapije = nepokretni `RoadOccupant` preko cele širine 9 m ispred kanala, čekanje da se kanal isprazni (najviše 20 s), krila rotacija oko sopstvene X (15 s gore, 18 s drži, 15 s dole), jedrilica prolazi kanalom; prvo otvaranje na 50 s, pa na 240 s. **Kasnije istog dana: svi mostovi se dižu** — sat ukinut, `RiverBoat` plovi celom rekom i zove mostove redom; bulevar 4 krila (po kolovozu) |

**Šta je harness naučio** (isti seed, runovi nisu ponovljivi — tally je sud):
1. **Poravnanje redova je ukinulo pomak (jitter)** kojim su se izbegavale „spojene raskrsnice" (dve poprečne ulice 5–25 m razmaknute na istoj traci → jedna široka kutija) — i harness je u takvoj kutiji zaključavao tri auta u 4 od 5 runova (seed 1987, x −145..−120 z −55..−35; na bulevaru x −40..−10). Popravke redom: (a) **preuređivanje jedinica u redu** (`CoreLayout.Order`, 120 redosleda, isti test sudara) — pomaže, ne rešava; (b) **svi redovi iste dužine**: kraći red se na kopnenoj strani dopuni **parkingom** (≤ 12 ćelija) ili parkom — pa se i kopnene ivične ulice poklope u jednu pravu ulicu (69 % deljenja čisto, prosek 1,2); (c) spojena raskrsnica je sada **greška** u deljenom gradu (u referenci ostaje napomena — Synty ih ima tri): 11 % deljenja čisto, `Deals` 40 → 80, 30/30 seedova, prosek ~8–10 deljenja. Sama blokada je stvar vozača u kutiji (auto 25 čeka auto 27 u daljoj traci, auto 40 „izlazi unazad" zauvek) — nije dirano.
2. **Uličica koja izlazi u parking je zamka**: jednosmerna traka bez izlaza, auto 5 stajao 152 s na kraju uličice ugnežđenog para (blok-12/10, ista i u referenci). Sada je takva uličica **prilaz** (goli asfalt, bez trake) — `CoreRoads.Build`.
3. **Kapije mosta ne smeju da čekaju red koji su same napravile**: prva verzija je proveravala kanal ±2 m; auti u redu ispred kapije zauzimaju do u kanal → most se nikad ne diže → kapije zauvek. Sada se proverava unutrašnjost kanala, kapije su 9 m ispred njega, strpljenje 20 s.
4. **Red na kapiji mora imati gde da stane**: kad su svi mostovi postali pokretni (2026-08-26), harness je u 1 od 5 runova našao okrz tri auta u raskrsnici puta druge obale — prilaz mosta na toj obali je 15 m, prvi auto u redu iza kapije stajao je 30 s na samoj ivici kutije, drugi u kutiji, treći se okrznuo ulazeći. Popravka: **plato druge obale** od 15 m (`FarApron`) između zida i puta → red ima 30 m pre kutije.

**Tally posle svega** (24 kola, 180 s, seed 1987 — reka na istoku u tom deljenju): prvi krug 4/5 PASSED + jedan run sa 80 odbijanja pojasa u 2 s (dva auta se očešala); završni krug **5/5 PASSED**, najgori auto stoji 42 s (red na kapiji mosta), 0 odbijanja. (Jezgro bez reke: 5/5 sa 24 kola.)
**Svi mostovi pokretni** (kasnije istog dana): prvi krug 4/5 (okrz tri auta u kutiji puta druge obale, pouka 4 → plato), sa platoom **5/5 PASSED**, najgori auto 56 s (red na kapiji), 0 odbijanja; u Play-u proba na 58/109/150 s: oba mosta seeda 1987 dignuta pred čamcem i spuštena za njim.

**Gušće šetalište** (korisnik: „uz reku izgleda praznjikavo, dodaj drveća, putića, kafića, dinera, propsa"): sobe terasa/dajner/pristan/fontana su **trava sa popločanim platoom** (±12 m oko kioska, ±10 m oko stepenica, ±15 m oko fontane), **drvored** uz ivicu staze u svakoj travi (6–8 m), **palme sa rešetkama** između klupa na unutrašnjoj traci staze, žardinjere po ivici platoa, kolica s viršlama na svakom trećem ušću, kiosk za novine + stalak za bicikle uz kafić, terasa na svakih 40 m (`length/8`), **dajner iz kataloga** (`building-diner`, pročelje IZMERENO `FacadeFinder`-om, ne pretpostavljeno) na najdužoj deonici uz pristan; kafić je u četvrtini slučajeva `building-coffeeshop`. Ringišpil se okreće u Play-u (`DemoFerrisWheel` na `_Rotate_01`), u skici editora ne.

**Strana reke po seedu**: `RiverLine.East`; linije su imenovane po tome između čega su (`QuayLand/QuayWater/Wall/FarWater/FarLand/BankEnd`), ne po kompasu. Šetalište se komponuje uvek s ivičnjakom na x=0 i zidom na x=D, pa se za zapadnu reku koren **okrene za 180°** (`CoreLayout.PlaceQuay`), a `ForQuay` obrne ušća i krajeve. Zidovi/abutmenti/ivičnjak druge obale biraju yaw po strani (`RiverBridge.WallPiece`).

### 5.7 Nije urađeno

- **Grad v3** (§4): `CityLayout.River`, kanal ostrva po liniji jezgra, pojas na obalskoj ulici, pin druge obale — jezgro u `Game.unity` još nema reku.
- Pešaci na šetalištu i preko mosta (nema ped-grafa u jezgru), `PedLink` kapija mosta.
- Turistički brodić po redu vožnje (jedrenjak sada plovi stalno i zove mostove, `RiverBoat`).
- **Red na kapiji je zastoj, ne red**: kapija je zauzimač bez auta, pa je vozači (`RoadCar`) gaze kao mrtvu stvar u traci — stanu, a posle vremena njihova pravila dopuštaju obilazak/okret (harness: stoje 20–60 s). Pravi red koji čeka kapiju (i `PedLink` za pešake) je posao vozačkog sloja. Nađeno u pregledu pri commit-u 2026-08-26.
- Blokada vozača u širokoj kutiji (v. 5.6/1) — sada se izbegava rasporedom, ne rešava vožnjom.
- Podešavanje pene (`TuneSurf`) — reka koristi ravan vode kao Game, bez podešavanja.

---

## 6. Život na obali (posle faze 3)

- klupe i ograda: ljudi sede i naslanjaju se (isti `PedLink` sedenje kao park); pecaroši stoje na
  svom mestu; terase imaju goste (sedeća poza iz `SeatPose`);
- turistički brodić po redu vožnje pristan → kanal → nazad; jedrilica koja traži dizanje mosta;
- ringišpil se okreće (`DemoFerrisWheel` već postoji); noću lampe keja + neon na kioscima;
- **most koji se diže kao mehanika**: kola se redaju na rampama; crew auto ne može preko → beg
  preko drugog mosta ili zaseda na rampi; policija isto čeka. To je razlog da bascule postoji u igri
  a ne samo kao dekor;
- galebovi iz luke (`SetTheGulls`), pena uz kej, konopci na bitvama.

---

## 7. Odluke (uzete 2026-08-26 „udri s implementacijom"; 2 izmenjene istog dana)

1. **Strana reke**: ~~istok, fiksno~~ → **istok ili zapad po seedu** (korisnik: „sve treba da se generiše random gde će bude reka"); sever/jug ne — tamo je pojas parkova (≥ 2 strane).
2. **Širina vode**: **70 m** (14 ćelija) = 40 m bascule kanal + 2 × 15 m prilaza; druga obala se
   čita kao drugo mesto. (alt: 50 m — samo bascule bez prilaza; 100–150 m kao u gridu — most bez
   pokretnog dela postaje dugačak, a bascule kanal ostaje 40 m)
3. **Dubina šetališta**: 5–8 ćelija po seedu (25–40 m; Synty-jeva traka grad–voda je 16–20 m, naša
   nosi i sobe); vašar traži 8 → dela se tako da stane bar na jednom kraju, inače ga nema.
4. **Mostovi**: **2–3** = bulevar + 1–2 ulice; ~~jedan bascule~~ → **svi se dižu** (korisnik:
   „svi mostovi treba da se dižu"), bulevar sa krilom po kolovozu.
5. **Ringišpil**: jednom, krajnja soba uz park pojasa, ograđen vašar — ne usred šetališta.
6. **Kafei**: PalmCity kiosci + terase **na šetalištu** (tražio si „na pavementu"); kataloške zgrade
   kafea/dinera ostaju blokovi u redovima. (alt v2: poslednja jedinica reda se okreće licem ka
   obalskoj ulici)
7. **Druga obala**: prazna (put + ivičnjak) dok se ne podeli kvart.
8. **Redovi poravnati na reku**, nazubljenost ide na zapad. Da.

Vezano: `Docs/park-plan.md` (isti obrazac plan/kompozer/presuda), `Docs/island-and-shore.md` §3
(kanal reke), `Docs/harbor-detail.md` (linija vode), `Docs/core-district-plan.md` §2.7 (redovi).
