# Plan: industrijski kvart (IndustrialQuarter)

> Odobreno i **napisano 2026-08-26** ("udri, hocu demo scenu"). Stanje po koracima je u §5.
> Odluke koje su bile otvorene (§6) uzete su podrazumevano: **bez šine**, **arterija 35 m**,
> **leđa uz leđa DA**, veličina kako ispadne (ispalo 385–605 × 420–760 m, 10–38 parcela).
>
> U jednoj rečenici: **generator koji iz seeda deli ceo industrijski kvart** — parcele u
> redovima oko kamionske ulice, puteve između njih čita i sudi isti `CoreRoads` raster koji sudi
> jezgru, a svaku parcelu **komponuje na licu mesta** iz recepata `IndustrialBlockForge`-a
> (proširenih) — i **scena u kojoj se to gleda**: crtež u editoru na jedan meni/komandu, pa
> `IndustrialDemo.unity` u kojoj kamioni voze.
>
> Odnos prema onome što postoji: `Sketch The Core City` je raspored **gotovih** blokova;
> `IndustrialBlockForge` pravi **jedan** blok po receptu. Ovo je treće: raspored koji
> **prvo deli parcele, pa ih recepti popune tačno na meru** — zato u kvartu nema plićeg bloka
> sa parkingom iza, nema praznog tla i nema dva ista bloka.

| šta | gde (novo) |
|---|---|
| raspored parcela + presuda | `Assets/RoadDemo/IndustrialLayout.cs` (runtime, kompajlira ga i `Tools/CoreSim`) |
| komponovanje parcele (recepti) | `Assets/RoadDemo/IndustrialBlocks.cs` (runtime; **preneto** iz forge-a, sa delegatom za instanciranje) |
| kvart kao `IDistrict` | `Assets/RoadDemo/IndustrialDistrict.cs` + `IndustrialDemoBuilder.cs` |
| scena za gledanje | `Assets/Scenes/IndustrialDemo.unity` (tanki domaćin kao `CoreDemo`) |
| crtež u editoru | `Assets/Scripts/Editor/IndustrialSketch.cs`, meni `Tools/City/Industrial/Sketch The Industrial Quarter` |
| komanda | `unity command gangsters_industry --seed N [--count M] [--draw]` |
| forge posle | `IndustrialBlockForge.cs` ostaje lab + bake **ljuska** nad `IndustrialBlocks` (4 kandidata, `ind-*` za jezgro) |

---

## 1. Šta je „super realističan" industrijski kvart u SAD 1987 (pravila koja raspored sprovodi)

Ovo je ono po čemu se kvart poznaje odozgo, i svako od ovih pravila je jedna odluka u kodu:

1. **Jedna kamionska ulica kroz sredinu** (arterija). Nema parkinga uz nju — dvostruka žuta
   linija, gole trake, rasveta na visokim stubovima, **bandere sa kablovima** duž jedne strane.
   To je jedini put sa saobraćajem koji prolazi; sve ostalo su ulice koje opslužuju kapije.
2. **Parcele su velike i različite** (60–105 × 45–75 m, po 5 m rešetki), a ulica ne ide između
   svake dve: **susedi dele ogradu** (~45 % spojeva) pa poprečna ulica pada na 100–200 m, ne na 90.
3. **Redovi leđa uz leđa**: dva reda dele zadnju liniju bez ulice između (zid uz zid); zadnja
   ulica ide tek iza drugog reda. To je osnovna razlika od jezgra, gde svaki red ima ulicu iza.
4. **Pročelje ka arteriji je ZIDANO** (works/plant: cigla, zgrade popune ulicu, jedna kapija),
   **ka zadnjim ulicama je ŽICA** (yard/depot: bodljikava kruna, kapija sa rampom, portirnica).
   Kvart odozgo čita: čvrsto uz glavnu, prozirno pozadi.
5. **Ćošak na ulazu u kvart je civilan**: pumpa (`FuelStation` forecourt koji već postoji) +
   diner (`building-diner` iz štofa) + kamionski parking. Jedan po kvartu, na arteriji.
6. **Jedan tank-farm** (naftni depo) — jedina niska, otvorena parcela sa cevima i parom.
7. **Jedan prazan plac**: žica sa procepom, šljunak, korov kroz asfalt, mrtvo drvo, bure-vatra,
   bilbord okrenut arteriji, tabla *For Sale*. Prazan plac je **recept**, ne popuna preostalog
   tla — preostalo tlo rastera i dalje ostaje prazno i ide u izveštaj (pravilo iz jezgra).
8. **Kapija = prekid trotoara + prilaz do dvorišta** (forge to već radi), a ispred kapije
   **lokve, tragovi guma i konus** — mesto gde kamioni skreću izgleda tako.
9. **Dimnjaci puše, ventili pare**: `FX_Smoke_Black_Small_01` na dimnjacima (postoji),
   `FX_Steam_01` na rezervoarima (luka ga koristi).
10. **Ivica kvarta ne glumi**: ulice se na ivici crteža završavaju slepim krajem koji vozači
    znaju (u gradu se tu šiju na grid). Nikakve kulise (pravilo `no-backdrop-scenery`).

**Šta se NE pravi, i zašto** (inventar 2026-08-26, ceo projekat bez `SimpleAirport` i
`MilitaryWarehouse`): **šina nema** (nijedna pločica, vagon ni lokomotiva; luka je krak
izbacila svesno — `Docs/harbor-detail.md`), **rezervoara/silosa nema** (luka ih pravi od
`SM_Prop_Barrel_Metal_01` skaliranog na 4–8.5 m: `HarborKit.TankBody`), **šlepera/cisterne
nema** (tri kamionska tela: `SM_Veh_Truck_01` box, `SM_Veh_Truck_Delivery_01`, Town flatbed),
**olupina, gomila guma i vreća nema**, **kaciga nema**. Ako nema modela — ne pogađa se
(`leave-it-empty-dont-guess`). Šina je jedina koja bi se dala kitbash-ovati (šina = razvučen
`SM_Bld_Base_Pillar_01` kao kod rampe u luci, pragovi `SM_Gen_Prop_Plank_01`, zastor
`SM_Gen_Env_Road_Gravel_*`) — **odluka korisnika, van v1**.

---

## 2. Kako radi (tri koraka, isti kod u editoru i u igri)

```
seed ──► IndustrialLayout.Roll ──► parcele (x,z,w,d, recept, strane koje deli)
              │                        │
              │                        ▼
              │              CoreLayout.Describe (maska po parceli / uniji parcela)
              │                        │
              ▼                        ▼
        Plan.Bands (arterija,   CoreRoads.Build ──► Raster ──► Faults == 0 ?  ne ──► sledeći deal (do 40)
        zadnje ulice)                                   │ da
                                                        ▼
                         IndustrialBlocks.Compose(recept, w, d, rng, stand)  po parceli, na licu mesta
                                                        │
                                                        ▼
                                  CoreRoads.Lay(raster, style: Industrial) + IndustrialStreetside
```

### 2.1 `IndustrialLayout` (runtime, čist C# kao `CoreLayout` — `Tools/CoreSim` ga kompajlira)

- **Parcela** = pravougaonik ćelija (w × d, w ∈ {12..21}, d ∈ {9..15} ćelija), recept, pročelje
  (jug/sever = ka kojoj ulici), i koje strane deli sa susedom (`SharedEast`, `SharedBack`).
- **Deal**: arterija je `MainRoad` (deklarisana preko cele širine, kao bulevar u jezgru, ali
  **širina 3 = ulica 15 m** sa industrijskim profilom, v. 2.4). Redovi: 1–2 severno i 1–2 južno,
  po seedu; red = 3–5 parcela **iste dubine** (dubina reda se bira prva, parcele se seku na nju —
  zato nema `Lot` parkinga iza plićeg bloka); između parcela **ulica 15 m ili deljena ograda**
  (odds 0.45); dva reda na istoj strani mogu biti **leđa uz leđa** (odds 0.5) — tada je raster-blok
  unija obe parcele, a zadnja ulica ide tek iza drugog reda; inače ulica između (kao jezgro).
  Zadnje ulice se **deklarišu preko cele širine** (pouka iz jezgra: L-ugao zaključava saobraćaj).
  Pomak reda po istoj logici kao `CoreLayout.Roll` (poprečne ulice suseda ili se poklope ili su
  ≥ 30 m razmaknute).
- **Dodela recepata je pravilo, ne kocka**: parcele **uz arteriju** → `works` / `plant` / `strip`
  (zidano pročelje), jedan ćošak uz arteriju → `stop`; parcele **uz zadnje ulice** → `yard` /
  `depot` / `fuel` / `waste` (žica); tačno **jedan** `fuel`, **jedan** `waste`, **jedan** `stop` po
  kvartu; jedan `works` ili `plant` obavezno (dimnjak je siluета kvarta); ništa dvaput zaredom u
  istom redu.
- **Presuda**: `CoreRoads.Build` nad maskama (`CoreLayout.Describe`, već postoji za CoreSim) →
  `Faults` (prazne mrlje, strana bez puta, batrljci, sudari). `Arrange(seed)` dela do 40 puta i
  uzima prvo čisto — isto kao jezgro; isti seed = isti kvart. Mera pre nego što se išta stane:
  `Tools/CoreSim --industrial --seed 1 --count 30` → **30/30 čisto** je uslov da se ide dalje.
- Veličina kvarta: ~**400–500 × 300–350 m**, 10–16 parcela.

### 2.2 `IndustrialBlocks` — komponovanje preneto iz forge-a (runtime, delegat za `stand`)

Forge-ova klasa `Block` (maska, ring ivičnjaka, `Way` + prilaz, `Surfaces`, zid/žica sa
`Corner`/`WallGap`/`WallInBuilding`, `Put/Prop/Ranks/Scatter/Bay/Chimney`, `Kerbs/Floor`) i pet
recepata (`Works/Plant/Depot/Stockyard/Strip`) sele se u `Assets/RoadDemo/IndustrialBlocks.cs`,
kao što su `CoreRoads` preseljeni iz `CoreCitySketch` — jedan delegat
`Func<GameObject, Transform, GameObject> stand` (editor: `PrefabUtility.InstantiatePrefab`, igra:
`Object.Instantiate`) i `DemoAssetLoad` umesto `AssetDatabase`. Merenje (`Box`) ide kroz isti
delegat (instanca, izmeri, uništi). Forge zadržava lab, četiri kandidata, bake i menije, i
zove `IndustrialBlocks.Compose`. **Regresija**: seed 7 mora dati iste brojeve (komada, rupa,
`wallGap`, `wallInBuilding`) pre i posle seobe — snima se pre prvog reda koda.

Izmene u receptima:
- **mera dolazi spolja**: `Compose(recipe, w, d, …)` umesto `Pick(rng, 75, 90)`; recepti već
  računaju iz `block.W/D`, pa je ovo mala izmena, ali svaki recept mora da **stane u ceo
  opseg** (12–21 × 9–15 ćelija) — proba: svaki recept × svaka mera → 0 rupa, 0 zida u zgradi.
- **deljene strane**: strana označena kao deljena **nema ivičnjak** (nije rub bloka), zid ide
  po njoj sa `Setback` 0.5 umesto 1.6, nikad kapija. Za par leđa uz leđa, drugi blok se
  komponuje okrenut za 180° (pročelje na svoju ulicu).
- **novi recepti** (v. 3).

### 2.3 Scena: dva domaćina, jedan kod

- **Crtež u editoru** (prvi vidljivi rezultat, bez Play-a): `Tools/City/Industrial/Sketch The
  Industrial Quarter` crta pod korenom `INDUSTRIAL QUARTER (sketch)` u otvorenoj sceni (radna:
  `IndustrialLab.unity`), sa natpisima po parceli (recept, mera, seed) i izveštajem rastera u
  konzoli — po uzoru na `CoreCitySketch.Draw`. Komanda `gangsters_industry --seed N --draw`
  isto, `--count M` bez crtanja = tally (deals, faults, rupe, zid u zgradi po parceli).
- **`IndustrialDistrict : IDistrict`** + `IndustrialDemo.unity` (tanki domaćin kao
  `CoreDemoBuilder`: `StandaloneDistrictHost`, kamera 320 m): `Plan` = deal + kompozicija u
  „unplaced" koren, `Build` = `CoreRoads.Lay` + lane graf iz rastera (isti kod kao
  `CoreDistrict.BuildLaneGraph` — izvlači se u deljeni `RasterGraph` da se ne kopira) +
  saobraćaj. Kad prođe harness, ugradnja u `Game.unity` ide istim šavom kao jezgro
  (`CityLayout.Roll` rezervacija, portali = krajevi arterije i zadnjih ulica).

### 2.4 Putevi i ulična oprema industrije

- `CoreRoads.Lay` dobija `Style { City, Industrial }`: industrijska **ulica 15 m = dve gole trake
  + `Road_YellowLines`**, bez `ParkingLines` traka; raskrsnice bez zebri osim na arteriji uz
  `stop`. Sve ostalo (koridori, presuda, raster) netaknuto — **Synty referenca jezgra mora
  ostati 0 grešaka** posle izmene (`CoreSim --synty`).
- `IndustrialStreetside` (dresing ivičnjaka, seedovan, po uzoru na `CorePavement` props ali
  svoja tabela): stub rasvete na 30 m (`SM_Prop_LightPole_Base/Arm/Lights`, City), **bandere
  `SM_Prop_Powerpole_01..03` + `SM_Prop_Powerline_*` (PalmCity) duž jedne strane arterije** (dužina
  spana se meri pri implementaciji; ako nema kabla te dužine — samo stubovi), hidrant 1/45,
  korov `SM_Prop_Grass_Cracks_01..05` uz zidove, lokve `SM_Prop_Puddle_01..05` i tragovi guma
  `SM_Gen_Env_Tyremarks_*` ispred kapija, bilbord `SM_Prop_Billboard_01` (City) na ćošku `waste`/
  `yard` parcele ka arteriji, znak `SM_Sign_Speed_35` / `SM_Sign_Authorized_Vehicles_01` na ulazu.
  **Bez** klupa, novina, palmi, korpi — to je centar.

---

## 3. Recepti (postojećih 5 + 3 nova u v1)

| recept | gde u kvartu | zid | šta je | nova građa |
|---|---|---|---|---|
| `works` | arterija | cigla | dve ciglane + radionica popune ulicu, kapija u procepu, hala, dimnjak, vodotoranj | — (postoji) |
| `plant` | arterija | cigla | dve procesne hale, dimnjak između, rampe, riseri | + `SM_Gen_Bld_Pipe_Valve_01`, `SM_Prop_Warehouse_Venting_*` na halama |
| `strip` | arterija (ćošak) | cigla | radionica + hala, usek = parking ulice | + `SM_Prop_CompanySign_01` |
| `yard` | zadnja ulica | žica | hale + redovi kontejnera, viljuškar | — |
| `depot` | zadnja ulica | žica | velika hala, 2 rampe, portirnica, parking trake | + rampa-boom `HarborBoom` na kapiji |
| **`stop`** (novo) | ćošak arterije | bez zida | `FuelStation.Stand` forecourt (pumpa, nadstrešnica, shop) + `building-diner` + kamionski parking (`Road_ParkingLines` 10×5, 4–6 mesta) + `SM_Sign_Fuel_01` na stubu | postoji sve; crossovers u trotoaru = faza 2 |
| **`fuel`** (novo) | zadnja ulica | žica | 2–3 rezervoara (`HarborKit.TankBody`, 6–8.5 m), bund od `Base_Pillar`, manifold cevi do rampe, `SM_Prop_Gaspump_01` dizel-rampa, `FX_Steam_01` na ventilu, `Sign_Danger` *no naked lights*, kamion na rampi | preuzeto iz `HarborDistrict.BuildTankFarm` — izvući u deljeni `IndustrialKit` |
| **`waste`** (novo) | zadnja ulica | žica sa procepom | pod `SM_Gen_Env_Ground_Dirt_*` (jedna pločica, mirno), `Grass_Cracks`, `SM_Gen_Env_Tree_Dead_01`, bure-vatra, napušten `SM_Prop_Dumpster_01`, `SM_Prop_Skip_01`, bilbord ka arteriji, `SM_Prop_ForSaleSign_01` | — |

v2 (posle prvog tally-ja): `salvage` (auto-otpad: kola u gustim redovima, neka bez točkova —
Synty točkovi su zasebna deca; skipovi, gomile paleta `Atop`), `pair` recept sa dve kapije za
parcelu koju dele dve firme, šina (ako korisnik hoće kitbash).

---

## 4. Život u kvartu (faza 2, `IndustrialDemo.unity`)

- **Saobraćaj**: `DemoVehicle` po lane grafu iz rastera, kao `CoreDistrict.SpawnCars`, ali
  sastav je industrijski: `HarborKit.Lorries` (3 tela) 50 %, pickup/van 30 %, kola 20 %
  (`IndustrialTraffic.Pick`; policija i gangsteri nikad — `VehicleCatalog.IsMarkedService`).
  ~18 vozila za kvart ove veličine (jezgro nosi 24 na više puteva; harness kaže tačno).
- **Dvorišta**: viljuškar koji vozi između rampe i stoka (obrazac `HarborCargo`), radnici na
  kapiji i uz bure-vatru (`HarborKit.Workers` — kombinezon, hazmat, City hoodie; nikad
  gangstersko telo `IsGangBody`), dim na dimnjacima i para na rezervoarima svira sama.
- **Kapije kao prilazi** (faza 3): koridor kapije = dvosmerni batrljak puta u lane grafu,
  kamion uđe, stoji na rampi 60–120 s, izađe. Da li graf nosi slepi batrljak sa okretom —
  odlučuje harness, ne plan.
- Harness: `gangsters_play --scene Assets/Scenes/IndustrialDemo.unity --seconds 180` ×5 runova
  → `analyze.py --verdict` PASSED, kao za `CoreDemo`.

---

## 5. Stanje (2026-08-26)

| # | korak | stanje |
|---|---|---|
| 1 | seoba `Block` + recepata u `IndustrialBlocks` (runtime, delegat), forge = ljuska | **urađeno**, kompajl čist |
| 2 | `IndustrialLayout.Roll/Arrange` + `CoreSim --industrial` | **urađeno**: `Tools/CoreSim --industrial --seed 1 --count 30` → **30/30 čisto, sve iz prvog deljenja**; `--synty` jezgra i dalje 0 grešaka |
| 3 | recepti na meru + deljene ograde (`Rim.Kerb` / `Rim.Party` + ko je polaže) | **urađeno** |
| 4 | `stop`, `fuel`, `waste` recepti | **urađeno** (`stop` koristi postojeći `FuelStation.Stand`) |
| 5 | ulična oprema (`Block.Streetside`: lampe 25 m, bandere sa kablom uz pročelje) | **urađeno** |
| 6 | `IndustrialSketch` meni + `gangsters_industry` komanda | **urađeno**: `--seed 1987 --count 12` → 12/12 čisto |
| 7 | `RasterGraph` izvučen iz `CoreDistrict`, `IndustrialDistrict` + `IndustrialDemo.unity` | **urađeno** |
| 8 | crtež u editoru pogledan i doteran | **urađeno**, v. „Šta je crtež pokazao" ispod |
| 9 | harness na `IndustrialDemo.unity` | **5/5 PASSED** (180 s, 18 vozila): najduže stajanje 15–22 s, 0 odbijanja pojasa, 0 izuzetaka, 38–42× realno vreme |

### Šta je crtež pokazao (i šta je zbog toga promenjeno)

Prvi crtež je bio **prazan grad**: putevi tačni, parcele nigde. Uzrok je pravilo koje forge
poštuje a ja nisam: **svaki komad se postavlja MERENJEM u world koordinatama**, pa se parcela
mora komponovati na koordinatnom početku i tek onda pomeriti. Postavljena pre komponovanja,
svaka parcela se sagradila oko nule i ceo kvart je ispao kao 19 dvorišta na jednom mestu.
(`IndustrialQuarter.Stand`, sada sa komentarom zbog čega.)

Dalje, redom kako se videlo:

- **Prazan plac je bio tepih od tufni.** `SM_Gen_Env_Ground_Dirt_01` je *okrugla* mrlja, ne
  pločica; po ćeliji je dala polje preklopljenih smeđih krugova — najglasniji pod u kvartu na
  jedinoj parceli koja treba da izgleda neiskorišćeno. `Surface.Dirt` je izbačen: plac je isti
  asfalt kao svako dvorište, a govore **korov kroz pukotine, lokve i krpljenje**.
- **Dvorišta su bila prazna.** Recepti su pisani za blok koji sam bira meru, pa im je štof bio
  šaka komada; na parceli 105 × 75 to je radionica sa parkingom umesto dvorišta. Sada postoji
  `FillYard` koji namešta **po površini**: vozni park, redovi robe uz ogradu, naslagane palete
  i kontejneri u sredini.
- **Tank-farma je pala na najširu parcelu** (280 m) sa tri rezervoara na sto metara razmaka.
  Dva popravka: kompaund se sada meri **prema rezervoarima, ne prema parceli**, a jednokratni
  recepti (`fuel`, `waste`) biraju **srednju** parcelu, ne najveću — inače najveće površine u
  kvartu nose dva najmanje izgrađena recepta.
- **Crtež je prolazio kroz drugu otvorenu scenu**, jer je `Extent` merio samo aktivnu. Sada meri
  sve učitane scene i preskače particle sisteme (dimnjak koji ne svira prijavljuje prazne granice
  u koordinatnom početku i odvukao bi meru preko cele mape).

**Odstupanja od plana, sa razlogom:**

- **arterija je 35 m, ne 15 m.** Sa svim putevima od 15 m kvart je odozgo rešetka jednakih ulica
  sa halama na njoj — a industrijska zona je *jedan* put koji nosi kamione i servisne ulice iza.
  35 m je jedina druga širina koju raster čita ({5, 10, 15, 35}) i daje kamionu dve trake u
  svakom smeru i mesto da čeka skretanje.
- **parcela je 75–105 × 55–75 m**, ne 60–105 × 45–75. Prvi tally je dao **116 `strip`-ova na
  1 `plant`**: delilac je pravio većinu parcela tačno na minimumu od 60 m, a jedini recept koji
  stane u 60 m je servisni niz. Pod je sada `IndustrialLayout.Smallest` — najmanja parcela koju
  recept ume da popuni.
- **`strip` više ne grize svoj SI ćošak.** Zagriz je tlo koje *ulica* uzima kao parking; u kvartu
  tim ćoškom često prolazi susedova ograda. Ostaje samo ako blok stoji sam (lab).
- **kvart je veći nego što je plan rekao**: 385–605 × 420–760 m, 10–38 parcela (plan je rekao
  ~450 × 330 i 10–16). Nije ograničavano jer se svaki deal sudi i prolazi.

## 6. Odluke koje su korisnikove (podrazumevano ako se ne kaže drugačije)

1. **Šina**: nema je u paketima. Podrazumevano **bez šine** (kao luka). Alternativa: kitbash
   krak iza zadnjeg reda (razvučen `Base_Pillar` + daske + šljunak) — može kasnije, ne menja plan.
2. **Arterija 15 m** sa industrijskim profilom (podrazumevano) ili **bulevar 35 m** kao u jezgru.
   Profil od 20 m (4 ćelije) bi bio nova širina u rasteru — ne u v1.
3. **Redovi leđa uz leđa** u v1 (podrazumevano DA, odds 0.5; prvi tally ide i sa 0 i sa 0.5).
4. **Veličina**: 10–16 parcela, ~450 × 330 m (podrazumevano) — može i manje za brži pregled.
