# Plan: generator industrijskih blokova u stilu jezgra (CoreHarvest)

> Napisano 2026-08-25. Ovaj dokument je **kompletno uputstvo za implementaciju** — sve mere, imena
> prefaba, fajlove i redosled koraka. Ne izmišljaj ništa što ovde nije napisano; ako nešto fali,
> izmeri (`unity command gangsters_measure --name ...`) i zapiši u ovaj dokument.
>
> Cilj u jednoj rečenici: editor alat koji u sceni `Assets/Scenes/CoreHarvest.unity` podigne
> **4 kandidata industrijskog bloka odjednom** (svaki drugačiji), korisnik ih pogleda i kaže
> „ispeci 1 i 2, ignoriši 3 i 4“, a alat one izabrane snimi kao prefabe u `Assets/Prefabs/CoreBlocks/`
> **kroz postojeći bake jezgra** (`CoreBlockTray`), tako da izgledaju i ponašaju se kao 16 blokova
> koje već imamo (`block-01..16`).

---

## 0. Šta „u stilu CoreHarvest blokova“ tačno znači (izmereno, ne pretpostavljeno)

Pročitaj `Docs/synty-demo-anatomy.md §2` i `Docs/core-district-plan.md`. Sažetak pravila koja
**moraju** da važe i za industrijski blok:

| pravilo | vrednost | izvor |
|---|---|---|
| Rešetka | **5 m**, svi pivoti pločica na višekratnicima 5, faza 0, y = 0 | `CoreLayout.Cell` (`Assets/RoadDemo/CoreLayout.cs:26`) |
| Blok nosi SVOJ trotoar | ivičnjak = prsten od **jedne pločice 5 m** `SM_Env_Sidewalk_Straight_01` oko cele kutije, `SM_Env_Sidewalk_Corner_01` na 4 ugla | `Docs/synty-demo-anatomy.md:50-54` |
| Put NIJE deo bloka | kolovoz između ivičnjaka polaže `CoreRoads` po razmaku (1 ćelija = uličica, 3 = ulica 15 m, 7 = bulevar) — blok ne sme da sadrži nijednu `SM_Env_Road_*` pločicu **izvan** svog prstena ivičnjaka, osim parkinga u useku | `Assets/RoadDemo/CoreRoads.cs:58-67` |
| Unutrašnjost | pod od `SM_Env_Sidewalk_01` (PolygonCity, 5 m pločica; ista koju bake koristi za `floor patch`), trava `SM_Env_Grass_01` samo ako ima smisla, **parkinzi** = `SM_Env_Road_ParkingLines_01` (10×5 m, tri mesta) u usecima | `CoreBlockTray.cs:1791` (`FillTile`), anatomija §2 |
| Pod miran | **jedna pločica po površini**, bez šahovnice, bez random rotacija, bez mešanja paketa | memorija `block-floor-calm` |
| Nema praznog prostora | svaki usek = parking / dvorište / manji objekat; u industriji: parking kamiona, kontejneri, bačve, ograđeno dvorište | anatomija §3 pravilo 4 |
| Rotacija bloka | uvek **0°** (`CoreLayout.Place` postavlja `Quaternion.identity`) — pročelja gledaju na ulicu zato što su TAKO složena, ne zato što blok nosi podatak o fasadi | `CoreLayout.cs:241-244` |
| Pivot prefaba | sredina **ground kutije** (samo strukture: `SM_Bld_*`, `SM_Env_Sidewalk*`, `SM_Env_Grass*`), snap na 5 m; X/Z centrirani, **Y netaknut** | `CoreBlockTray.Bake` (`:659-683`) |
| Hijerarhija prefaba | **ravna**: svaki komad je DIREKTNO dete korena, ime = ime instance u sceni; `CoreLayout.Measure` čita samo direktnu decu i klasifikuje po prefiksu imena | `CoreLayout.cs:192-238` |
| Jedina komponenta korena | `LivingCity.Generation.BlockLotTag` sa `lotWidth/lotDepth` (bake je sam dodaje) | `CoreBlockTray.cs:690` |
| Bez kolajdera/slojeva/merge-a | bake ne dodaje ništa; kolajderi dolaze iz Synty izvornih prefaba | izveštaj tray-a |
| Vrh pločice trotoara | `PrefabBounds(SM_Env_Sidewalk_Straight_01).max.y` ≈ **0.13 m** (PolygonCity) — zgrade sa ravnim podom stoje na `TileTop + 0.02` (pravilo iz luke, `HarborDistrict.Yard.cs Seat`) | `RoadDemoBuilder.cs:1731-1735` |

**Razlika od grid-grada (RoadDemo/Game.unity):** tamo ulica sama polaže trotoar od 6.5 m
(`StreetKit`, `SidewalkDressing.Width`) i blok = samo unutrašnjost lota. **Ovde ne.** Jezgro (i
industrijski blokovi za jezgro) nose 5 m ivičnjak u sebi. Ne mešati dva pipeline-a:

| pipeline | meni | izlaz | potrošač |
|---|---|---|---|
| **jezgro** (OVAJ) | `Tools/City/Core/*` | `Assets/Prefabs/CoreBlocks/*.prefab` | `CoreLayout` + `CoreRoads` (`CoreCitySketch`, budući `CoreDistrict`) |
| katalog | `Tools/City/Catalog/*` | `Assets/CityKit/Blocks/*.prefab` (`BlockLotTag.lot` = A1..C3) | `RoadDemoBuilder` |

---

## 1. Građa: koji prefabi smeju (sve osim `Assets/SimpleAirport`)

### 1.1 Zgrade — gotove, ispečene, pod na y=0, pročelje na **+Z**, pivot u centru otiska
Folder `Assets/CityKit/Buildings/`. Mere iz `Logs/harbor-kit-probe.txt` (X × Y × Z u metrima).
Za neizmerene: `unity command gangsters_measure --name building-factory`.

| prefab | mere | šta je | uloga u bloku |
|---|---|---|---|
| `building-warehouse-large` | 32.7 × 13.2 × 40.0 | velika hala (MilWarehouse), ima **ukopan nivo na y=−3** → ispod nje NEMA poda (memorija `no-floor-under-sunken-buildings`) | glavna zgrada depoa |
| `building-warehouse-small` | 22.7 × 7.4 × 17.7 | mala hala | red hala, zaleđe |
| `building-depot-garage` | 25.2 × 7.8 × 17.5 | garaža sa roller vratima | servis, red hala |
| `building-yard-shed` | 6.9 × 3.9 × 6.1 | ostava/portirnica — **nikad u redu**, samo uz kapiju | portirnica |
| `building-warehouse` | izmeriti | stovarište iz GangWarfare demoa (sa dvorišnim propsima) | samostalni blok |
| `building-factory` | ~24 × ? × 15 | fabrika od cigle, 2 sprata, vodotoranj na krovu | pročelje ulice |
| `building-factory-old` | izmeriti | cigla + dimnjak ~12.5 m | pročelje ulice / ugao |
| `building-factory-hall` | ~21 × ? × 9 | metalna hala, dvoja klizna vrata 6×6 | zaleđe |
| `building-workshop` | ~15 × ? × 12 | jednospratna metalna radionica | servisna traka |

Ova imena ne počinju sa `SM_Bld_`, pa ih `CoreLayout.Measure` (`CoreLayout.cs:204`) **ne broji kao
zgrade** (MaxH/MeanH). Popravka u koraku 4.6 (jedan red).

### 1.2 Pod i ivičnjak (PolygonCity, `Assets/Synty/PolygonCity/Prefabs/Environments/`)
Sve pločice: 5 × 5 m, **pivot u +X/+Z uglu** (kao `CoreRoads.Kit.Tile`, `CoreRoads.cs:897` i
`StreetKit.PlaceTile`, `StreetKit.cs:379`), materijal jedan zajednički atlas
`Assets/Synty/PolygonCity/Materials/Alts/PolygonCity_01_A.mat`.

| pločica | upotreba |
|---|---|
| `SM_Env_Sidewalk_Straight_01` | ivičnjak — prsten širine 1 ćelije oko bloka; ivičnjak („stepenica“) je na **spoljnoj** ivici; yaw kao u `StreetKit.cs:206-248` (jug 0, sever 180, zapad 90, istok 270) |
| `SM_Env_Sidewalk_Corner_01` | 4 ugla prstena; yaw 0/90/180/270 kao u `StreetKit.LayCrossroads` (`StreetKit.cs:370-373`), ali ugao je OKRENUT KA SPOLJA bloka (ovde blok nosi ugao, ne raskrsnica) — proveri vizuelno na prvom kandidatu i zapiši ispravan yaw ovde |
| `SM_Env_Sidewalk_Dip_01` / `SM_Env_Sidewalk_Dip_Corner_01` | rampa na ivičnjaku tamo gde kolski ulaz/kapija seče ivičnjak (isto kao Synty demo na prelazima) — **obavezno na svakoj kapiji** |
| `SM_Env_Sidewalk_01` | plato/unutrašnji pod bloka (5 m pločica, ista kao `FillTile` bake-a) |
| `SM_Env_Road_Bare_01` | asfalt dvorišta IZA ograde (jedina druga površina) |
| `SM_Env_Road_ParkingLines_01` | 10 × 5 m, tri mesta — parking kamiona/auta u useku uz ulicu |
| `SM_Env_Grass_01` | samo za „pojas trave uz ogradu“ recept (opciono, v1 bez) |

### 1.3 Ograda, kapija, utovar (`Assets/Synty/PolygonGangWarfare/Prefabs/Buildings/`)
| prefab | mere | napomena |
|---|---|---|
| `SM_Bld_Fence_01` | 3.00 × 2.92, panel žičane ograde | ograda dvorišta; polagati preko `HarborKit.LayRun` logike (moduli jednako razvučeni, bez rupe) |
| `SM_Bld_Fence_Wire_01` | 0.77 m **bodljikava KRUNA**, pivot na 3.16 m — NIJE ograda | spušta se za `crownDrop = max(0, PrefabBounds(kruna).min.y − PrefabBounds(panel).max.y)` (`HarborDistrict.Yard.cs:540 FenceRun`) |
| `SM_Bld_Fence_Gate_01` | 6.02 m širine | kapija; dva krila; ulaz uvek ka ulici, preko `Sidewalk_Dip` |
| `SM_Bld_Fence_Pole_01` | stub | na krajevima ograde i uz kapiju |
| `SM_Bld_LoadingDock_02` | 6.00 × 1.06 × 3.24 | uz zadnji zid hale, yaw 180 (kao `HarborDistrict.Yard.cs:237 BackDocks`) |

### 1.4 Props dvorišta (samo ono što je već u luci — dokazano da izgleda dobro)
Konstante i putanje uzmi iz `Assets/HarborDemo/HarborKit.cs:35-148` (ne prepisivati stringove
napamet — referencirati `HarborKit.Pallet`, `HarborKit.BarrelMetal` itd. ako je klasa dostupna
iz editor asemblija; ako nije, kopirati konstante uz komentar odakle su).

| šta | prefab |
|---|---|
| paleta | GangWarfare `SM_Prop_Pallet_01` (1.44 × 0.23 × 1.42) |
| bačve | GangWarfare `SM_Prop_Barrel_Metal_01`, `SM_Prop_Barrel_Plastic_01` |
| kontejner 20' | `Assets/CityKit/Ships/container-20-{red,blue,green,rust,white}.prefab` (3.36 × 3.21 × 6.38) |
| kontejner (dumpster) | GangWarfare `SM_Prop_Dumpster_01` (2.22 × 1.68 × 1.26) |
| kolut žice / cevi | `SM_Prop_Wirespool_01`, `SM_Prop_PipeStack_01` |
| sanduci | PolygonGeneric `SM_Gen_Prop_Crate_01/02` |
| bandera dvorišta | GangWarfare `SM_Prop_Light_Pole_01` (3.91 m) |
| znak | GangWarfare `SM_Prop_Sign_Danger_01`, `SM_Prop_KeepOut_01` |
| betonska barijera | PolygonCity `SM_Prop_Barrier_01` |
| kupa | PalmCity `SM_Prop_Cone_01` |
| vatra u buretu | PalmCity `SM_Prop_Barrel_Burn_01` |
| kamion (parkiran, dekor) | GangWarfare `SM_Veh_Truck_01` (2.75 × 3.76 × 8.28), Town `SM_Veh_Truck_01` (flatbed), Town `SM_Veh_Truck_Delivery_01` |
| viljuškar (dekor) | GangWarfare `SM_Veh_Forklift_01` |

**Zabranjeno:** bilo šta iz `Assets/SimpleAirport`; iz `PolygonMapsMilitaryWarehouse` samo
bodljikava žica `SM_Prop_Barbed_Wire_01/02` (ostalo je sivo — atlas fali, memorija
`military-warehouse-atlas-missing`); `SM_Env_Background_Crane_*` (kulisa — memorija
`no-backdrop-scenery`); uspavani sistem `Assets/Scripts/Generation/Industrial*.cs` +
`PortLayout.cs` (piše za polyperfect katalog koji ne postoji — **ne dirati, ne oživljavati**).

---

## 2. Četiri recepta (kandidati)

Svaki kandidat = jedan recept + seed. Podrazumevano generator pravi **po jedan od svakog recepta**
(4 kandidata); sa `--recipe works` pravi 4 varijante istog recepta sa 4 seed-a.

Zajedničke mere (sve višekratnici 5):
- širina bloka W ∈ {60, 75, 90, 105}, dubina D ∈ {45, 60, 75} (industrija je krupnija od jezgra,
  gde su blokovi 25–90 × 25–105).
- prsten ivičnjaka 5 m ⇒ unutrašnjost (W−10) × (D−10).
- **pročelje = južna ivica (−Z lokalno)**: zgrade uz južnu ivicu gledaju ka −Z (yaw 180, jer
  CityKit zgrade imaju front na +Z). Kad `CoreLayout` bude slagao ove blokove, jug bloka ide na
  ulicu. (Blok je simetričan po potrebi: recept može da ima pročelje i na severu — „dva lica“.)
- ograda dvorišta ide po unutrašnjoj ivici prstena (0.5 m unutar ivičnjaka) tamo gde nema zgrade
  na ivici; **kapija** je tamo gde ograda seče ivičnjak i uvek dobija `Sidewalk_Dip` pločicu.
- pod: plato `SM_Env_Sidewalk_01` ispod svih zgrada uz ulicu i ispred njih; iza ograde asfalt
  `Road_Bare_01`. Ispod `building-warehouse-large` **nema pločica** (ukopan nivo).
- props: nasumično po seed-u, ali po pravilima luke (`HarborDistrict.Yard.cs BuildBackLots /
  DressYard`): palete u gomili od 2–5, bačve u 2–3 reda na 1.1 m, kontejneri u redu 1–2 visine sa
  20 % praznina, jedan kamion na 25 %, viljuškar na 40 %, bandera dvorišta svakih ~30 m uz ogradu.
  **Ništa na prstenu ivičnjaka** osim `Sidewalk_Dip`. (Props su dozvoljeni jer je ovo *authoring*
  alat u editoru → rezultat je predefinisani bake; memorija `blocks-catalog-only`, dopuna 16.08.)

### Recept A — `works` (fabrika uz ulicu)
- Pročelje: `building-factory-old` na zapadnom uglu (dimnjak ka ulici) + `building-factory`
  desno od nje, obe leđima ka dvorištu, razmak 5 m između njih zatvoren ogradom sa kapijom
  (širina 6 m ⇒ kapija stoji u tom procepu, ivičnjak ispred dobija `Dip`).
- Zaleđe: `building-factory-hall` uz severnu ivicu, yaw 0 (vrata ka dvorištu); ako D ≥ 60,
  `building-workshop` u NE uglu.
- Dvorište: asfalt; bačve + palete + `PipeStack` + 1 kamion uz halu.
- Ograda: istok + zapad + sever (gde nema zgrade), bodljikava kruna.

### Recept B — `depot` (distributivni magacin)
- `building-warehouse-large` centrirano (x = 0), pročelje ka jugu ali **uvučeno 15 m** od
  ivičnjaka: ispred njega parking sa `Road_ParkingLines_01` (2–3 komada po 10 m) i kolski
  prilaz; ivičnjak dobija dva `Dip`-a.
- Zadnji zid: 2 × `SM_Bld_LoadingDock_02` na ±6 m (pravilo iz `BackDocks`), iza njih asfalt i
  1–2 `SM_Veh_Truck_01` na 4.6 m koraka (`lorry park` pravilo).
- Portirnica `building-yard-shed` uz kapiju.
- Ograda po celom obodu osim prilaza.
- **Bez poda ispod velike hale.**

### Recept C — `yard` (stovarište / kontejnersko dvorište)
- Zgrade: red od `building-warehouse-small` + `building-depot-garage` uz **severnu** ivicu
  (pravilo reda iz `BuildWarehouses`: blokovi od 2–3, 6–11 m razmaka u bloku, 25 % šanse da
  plitka hala stane bočno).
- Jug: ograda + široka kapija (2 × `Fence_Gate_01` = 12 m), `Dip` na ivičnjaku.
- Dvorište: red kontejnera (`container-20-*`, 35 % šanse „jedan brodar = jedna boja“), 1–2
  visine, forklift, kolut žice, bačve u uglu, burn barrel + 2 sanduka.
- Kad W ≥ 90: drugi red kontejnera sa prolazom `CrossLane` 4 m.

### Recept D — `strip` (servisna traka: radionica + parking)
- Jug: `building-workshop` na zapadu + `building-factory-hall` na istoku, obe pročeljem ka
  ulici (yaw 180), između njih 10 m otvoreni parking (`ParkingLines` × 1) sa `Dip`.
- Sever: pojas asfalta (usek) sa `ParkingLines` × 2–3 = parking zaposlenih (otvoren ka
  severnoj ulici — **to je usek koji `CoreRoads` čita kao `Parking`**, pa ne sme da ima ogradu
  sa te strane); dumpster uz zid hale, `Barrier_01` × 2 na uglu.
- Ograda samo istok/zapad.

Svaki recept mora da **zatvori ceo pravougaonik** (nema gole ćelije): proverava se rasterom
(korak 4.7).

---

## 3. Radni tok korisnika (šta se dešava kad se sve napravi)

1. Otvorena scena `Assets/Scenes/CoreHarvest.unity` u editoru.
2. `unity command gangsters_industrial --seed 7` (ili meni
   `Tools/City/Core/Industrial/Generate 4 Candidates`) → u sceni nastane koren
   `INDUSTRIAL CANDIDATES` sa 4 deteta `candidate-1 … candidate-4`, poređana u red **južno od
   `CORE BLOCKS (review)`** (ako ga nema, južno od svega), razmak `Gap = 30 m`, svaki sa
   `TextMesh` etiketom `"candidate-3  yard  seed 7-3  90 x 60 m"`.
   Komanda vraća JSON: `[{index, recipe, seed, width, depth, pieces, gaps}]`.
3. Korisnik pogleda (Scene view / `unity command screenshot`) i kaže agentu: „ispeci 1 i 2,
   ignoriši 3 i 4“.
4. Agent: `unity command gangsters_industrial --bake 1,2 --names ind-works-01,ind-depot-01`
   → za svaki izabrani indeks: komadi kandidata se prebace pod NOVI tray `<ime>` u `CORE TRAYS`,
   pozove se postojeći `CoreBlockTray.BakeNow()` (force), prefab nastane u
   `Assets/Prefabs/CoreBlocks/<ime>.prefab`, `Clear Tray After Bake` ih izbaci iz scene, ostali
   kandidati (3, 4) se obrišu, koren `INDUSTRIAL CANDIDATES` nestane, review red se osveži
   (`ShowBaked`) pa korisnik vidi ispečene blokove u redu sa ostalih 16.
   Vraća JSON `[{index, name, path, wrote}]`.
5. Ponovo od 2 sa drugim seed-om dok ne bude dosta blokova.

Bez `--names`: podrazumevano ime `ind-<recept>-<NN>` gde NN = prvi slobodan broj u folderu.

---

## 4. Implementacija — korak po korak

Sve u **jednom novom fajlu** `Assets/Scripts/Editor/IndustrialBlockForge.cs`
(`namespace LivingCity.EditorTools`, `public static class IndustrialBlockForge`), plus male
izmene u `CoreBlockTray.cs`, `CoreLayout.cs`, `PipelineCommands.cs`. Nema runtime koda — sve je
editor authoring. Nema novih scena.

### 4.1 Konstante (vrh fajla)
```csharp
internal const string CandidatesRoot = "INDUSTRIAL CANDIDATES";
const string CandidatePrefix = "candidate-";
const float Cell = 5f;
const float Gap = 30f;                 // isti kao CoreBlockTray.Gap
const string CityEnv  = "Assets/Synty/PolygonCity/Prefabs/Environments/";
const string GangBld  = "Assets/Synty/PolygonGangWarfare/Prefabs/Buildings/";
const string GangProps= "Assets/Synty/PolygonGangWarfare/Prefabs/Props/";
const string GangVeh  = "Assets/Synty/PolygonGangWarfare/Prefabs/Vehicles/";
const string KitBld   = "Assets/CityKit/Buildings/";
const string Ships    = "Assets/CityKit/Ships/";
static readonly int[] Widths = { 60, 75, 90, 105 };
static readonly int[] Depths = { 45, 60, 75 };
enum Recipe { Works, Depot, Yard, Strip }
```

### 4.2 Instanciranje — OBAVEZNO `PrefabUtility.InstantiatePrefab`
`CoreBlockTray.Restand` (`CoreBlockTray.cs:719`) zadržava vezu na izvorni prefab samo ako je
komad **prefab instanca**; goli `Object.Instantiate` daje dubok primerak (radi, ali prefab
naraste i gubi vezu). Zato jedini način da se nešto postavi:
```csharp
static GameObject Stand(string path, Vector3 pos, float yaw, Transform parent, Vector3? scale = null)
{
    var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    if (src == null) { Missing.Add(path); return null; }
    var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
    go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
    if (scale.HasValue) go.transform.localScale = scale.Value;
    Undo.RegisterCreatedObjectUndo(go, "Industrial candidate");
    return go;
}
```
- Sve je **direktno dete kandidata** (`parent = candidate.transform`). NIKAKVE među-grupe
  („fence“, „props“) — grupa nije prefab instanca pa bi bake pravio dubok primerak, a
  `CoreLayout.Measure` čita samo direktnu decu.
- Zgrade koje su gotove pečene (`building-*`) su prefabi → OK.
- Ime instance ostaje ime prefaba (Unity to radi sam) — `CoreLayout.Measure` sudi po prefiksu.

### 4.3 Pločice
Pločice pivotiraju u +X/+Z uglu; kopiraj tačno logiku `CoreRoads.Kit.Tile`
(`Assets/RoadDemo/CoreRoads.cs:897`) — po yaw-u pivot ide u odgovarajući ugao ćelije, skala
`size/5`. Za ivičnjak: ne razvlačiti (5 × 5, skala 1). Ceo blok se gradi u **lokalnim**
koordinatama sa jugozapadnim uglom u (0,0) pa se kandidat kao celina pomeri na svoje mesto u
redu (`candidate.transform.position = slot`, deca prate).

Prsten: za x u [0, W) korak 5: jug `(x, 0)` yaw 0, sever `(x, D−5)` yaw 180; za z u [5, D−5):
zapad `(0, z)` yaw 90, istok `(W−5, z)` yaw 270; četiri ugla `Sidewalk_Corner_01`. **Prvo
proveri yaw-ove na kandidatu vizuelno** (ivičnjak-stepenica mora biti ka spolja) i upiši
konačne vrednosti u ovaj dokument.

Rampe: tamo gde recept ima kapiju/kolski prilaz na ivici, zameni `Straight` sa `Sidewalk_Dip_01`
na tim ćelijama (isti yaw).

### 4.4 Zgrade i props
- Zgrada se stavlja preko `Stand(KitBld + "building-factory.prefab", pos, yaw, parent)` gde je
  `pos.y = TileTop + 0.02f`, `TileTop = PrefabBounds(Sidewalk_Straight_01).max.y` (izmeri jednom,
  keširaj; `HarborKit.PrefabBounds` je uzor — meri renderere sa asseta bez instance).
  Izuzetak: `building-warehouse-large` na `y = 0` (ima sopstveni ukopani nivo) i ispod nje se
  **ne polažu** pločice poda (preskoči ćelije koje njen otisak pokriva ≥ 35 %).
- Otisak zgrade = `PrefabBounds(prefab)` rotiran za yaw; zgrada se poravna tako da joj front
  stoji **na unutrašnjoj ivici prstena** (front zida na z = 5 lokalno za južno pročelje), ne
  na sredini pločice.
- Props: `Sit` logika iz luke (`HarborKit.Sit`) — seti na sopstveno dno, jer Synty pivotira
  neke propse u sredini. Sve props pozicije snap na 0.5 m, yaw ∈ {0, 90, 180, 270} + do ±8°
  za palete/bačve (kao luka).
- Ograda: `LayRun(panel, from, to)` — n = round(len/3), skala x = len/(3n); kruna spuštena za
  `crownDrop`; stub `Fence_Pole_01` na oba kraja i uz kapiju. Kapija = 2 krila `Fence_Gate_01`
  otvorena ravno uz ogradu (`HarborDistrict.Yard.cs:550 BuildFence` je uzor).

### 4.5 Slučajnost
`var rng = new System.Random(seed * 31 + index)` po kandidatu. **Nikad `UnityEngine.Random`** —
kandidat sa istim `--seed` mora ponovo da ispadne isti (da bi „vrati mi kandidata 2 od seed-a 7“
imalo smisla). Sve odluke (W, D, izbor kontejnera, broj paleta…) iz `rng`.

### 4.6 Izmene u postojećim fajlovima (minimalne)
1. `CoreBlockTray.cs`: kandidati ne smeju da uđu u `Sweep` ni u merenje ivica. Na **tri** mesta
   (`:252`, `:393`, `:1995`) gde stoji
   `if (root.name == ReviewRoot || root.name == MapRoot) continue;`
   dodaj `|| root.name == IndustrialBlockForge.CandidatesRoot`. Ništa drugo se ne menja — bake
   se koristi kakav jeste.
2. `CoreBlockTray.cs`: `internal static void BakeNow()` je već `public` (meni) — koristi ga.
   `AddTray()` pravi tray na zapadu sa podrazumevanim imenom; za bake kandidata treba tray sa
   **zadatim imenom i merama** → izvuci privatnu logiku iz `AddTray` u
   `internal static Transform MakeTray(Scene scene, string name, float width, float depth, Vector3 at)`
   (pad + label kao i sad), a `AddTray` neka je zove.
3. `CoreLayout.cs:204`: `bool building = Starts(piece, "SM_Bld_") || Starts(piece, "building-");`
   da industrijske zgrade uđu u `MaxH/MeanH/Buildings`.
4. `PipelineCommands.cs`: nova komanda (4.8).

### 4.7 Provera kandidata pre nego što se pokaže (u samom generatoru)
Za svaki kandidat izračunaj raster W/5 × D/5 nad pločicama poda + otiscima zgrada (pravilo
„pokriveno ≥ 35 % ćelije“, isto kao `CoreBlockTray.Covers`) i:
- **gole ćelije = 0** (osim ispod `warehouse-large`) — inače kandidat dobija u etiketu
  `"GAPS: 3"` i u JSON `gaps: 3`; nije greška, ali korisnik vidi.
- nijedan komad ne prelazi kutiju bloka (`PrefabBounds` rotiran u granicama `[0,W]×[0,D]`
  ± 0.3 m) — inače `Debug.LogWarning` sa imenom komada.
- lista `Missing` (prefabi koji nisu nađeni) ide u JSON i u konzolu; kandidat se i dalje pravi.

### 4.8 Pipeline komanda `gangsters_industrial` (`PipelineCommands.cs`, obrazac `gangsters_layout`)
```csharp
[CliCommand("gangsters_industrial",
    "Stand 4 industrial block candidates in the open CoreHarvest scene, or bake chosen ones.",
    MainThreadRequired = true, Tags = new[] { "gangsters" })]
public static object Industrial(
    [CliArg("seed", "Seed for the four candidates.")] int seed = 7,
    [CliArg("recipe", "works|depot|yard|strip|all (all = one of each).")] string recipe = "all",
    [CliArg("bake", "Comma list of candidate indices to bake, e.g. 1,2. Others are discarded.")] string bake = "",
    [CliArg("names", "Comma list of prefab names for --bake, same order.")] string names = "",
    [CliArg("keepOthers", "With --bake: leave unchosen candidates standing.")] bool keepOthers = false)
```
- bez `--bake`: `IndustrialBlockForge.Generate(seed, recipe)` → JSON liste kandidata.
- sa `--bake`: `IndustrialBlockForge.Bake(indices, names, keepOthers)` → JSON rezultata.
- Meni ekvivalenti: `Tools/City/Core/Industrial/Generate 4 Candidates`,
  `.../Bake Candidate 1` … `4` (svaki poziva `Bake(new[]{i}, null, keepOthers:true)`),
  `.../Discard Candidates`.
- Scena mora biti `CoreHarvest.unity`; ako nije, vrati grešku `"open Assets/Scenes/CoreHarvest.unity first"`.
  Posle bake-a scena se snimi (`EditorSceneManager.SaveScene`) — **pažnja**: scena ima 8 MB i
  `SaveScene` kroz pipeline eval ume da pređe 5 s tajmauta (memorija `core-city-sketch`); zato
  bake vraća JSON PRE snimanja i snimanje radi kroz `EditorApplication.delayCall`, kao
  `CoreBlockTray.OnSceneSaved` (`:475-482`).

### 4.9 `Bake(indices, names, keepOthers)` — tačan redosled
1. nađi `INDUSTRIAL CANDIDATES`; za svaki indeks nađi `candidate-<i>`; ako ime nije dato,
   `ind-<recept>-<NN>` (recept iz etikete/komponente kandidata, NN = prvi slobodan u
   `Assets/Prefabs/CoreBlocks`). Odbij ime koje već postoji (kao tray: „refused, taken“).
2. `var tray = CoreBlockTray.MakeTray(scene, name, W, D, candidate.position)`; prebaci svu
   direktnu decu kandidata (osim `label`) pod tray **bez promene svetskih koordinata**
   (`SetParent(tray, worldPositionStays: true)`); obriši prazan kandidat.
3. `CoreBlockTray.BakeNow()` — jednom, za sve izabrane (svaki tray = svoj prefab). Pošto je
   `Clear Tray After Bake` podrazumevano uključen, komadi nestaju; tray-evi ostaju prazni →
   obriši ih (`Undo.DestroyObjectImmediate`) da `CORE TRAYS` ostane kakav je bio (`core-01`).
4. ako `!keepOthers`: obriši ceo `INDUSTRIAL CANDIDATES`.
5. ako postoji `CORE BLOCKS (review)`: `CoreBlockTray.ShowBaked()` da se red osveži.
6. JSON: `[{index, name, path, wrote: "Yes|Unchanged|No"}]` (reč iz `said` linija BakeAll-a).

**Zamka:** `Bake Trays On Save` je podrazumevano uključen — dok kandidati stoje, običan Ctrl+S
ih **ne** peče (nisu u tray-u ni u rect-u), i to je namerno; ali ako neko prevuče kandidata na
`core-01` rect, Ctrl+S ga peče pod imenom `core-01`. Napomenuti u konzoli posle Generate.

---

## 5. Redosled rada i provere (za agenta koji ovo radi)

1. **Pročitaj**: ovaj dokument, `Docs/synty-demo-anatomy.md §2–3`, `CoreBlockTray.cs:1–120,
   650–760, 1975–2030`, `CoreRoads.cs:890–948` (Kit.Tile), `HarborKit.cs` (helperi),
   `HarborDistrict.Yard.cs:94–300, 540–600` (raspored hala, ograda).
2. **Izmeri** neizmerene zgrade: `unity command gangsters_measure --name building-factory` (i
   `-old`, `-hall`, `-workshop`, `building-warehouse`) → upiši mere u tabelu 1.1.
3. Napiši `IndustrialBlockForge.cs` sa receptom **A samo**, plus izmene 4.6 i komandu 4.8.
4. `unity command recompile && unity command recompile_status --json` — mora biti čisto.
5. **Review pre pokretanja**: `code-review-unity` skill nad diff-om, pa
   `.claude/hooks/unity-review-gate.sh record` (hook inače odbija `eval`/`menu`).
6. Otvori `CoreHarvest.unity`, `unity command gangsters_industrial --seed 7 --recipe works`,
   `unity command screenshot` (Scene view iznad kandidata; kamera ~80 m, 35° nagib). Proveri:
   prsten ivičnjaka ka spolja, ugaone pločice ispravno, nema gole ćelije, zgrade ne štrče, sve
   su direktna deca, sve su prefab instance (`PrefabUtility.IsAnyPrefabInstanceRoot` u eval-u
   nad svom decom → 100 %).
7. `--bake 1 --names ind-test-01` → proveri da `Assets/Prefabs/CoreBlocks/ind-test-01.prefab`
   postoji, koren ima samo `BlockLotTag` (W×D tačni), deca su `PrefabInstance` blokovi (otvori
   YAML: broj `PrefabInstance` ≈ broj komada), da je nestao iz scene, da review red stoji.
   **Obriši** `ind-test-01.prefab` posle provere (test, ne građa).
8. Dodaj recepte B, C, D; ponovi 4–7 sa `--recipe all`.
9. Provera sa `CoreCitySketch`: privremeno dodaj jedan `ind-*` blok u `CoreLayout.Blocks` na
   praznom mestu južno od jezgra (npr. `new Stand("ind-works-01", 0f, -220f)`), pokreni
   `Tools/City/Core/Sketch The Core City`, proveri da `CoreRoads` čita prsten (raster ne
   prijavljuje `Outside` rupu unutar bloka) — pa **vrati** `CoreLayout.Blocks` (ugradnja u
   raspored jezgra je posebna odluka korisnika, ne deo ovog zadatka).
10. Konačni review (`code-review-unity`), pa izveštaj korisniku sa snimkom 4 kandidata.

**Ne diraj**: `Assets/HarborDemo/HarborDistrict.Ground.cs` i `HarborKit.cs` imaju nekomitovane
izmene od ranije — ne mešati ih u ovaj posao. Ne menjati `SyntyDemoBlockRip`, `BlockRandomiser`,
`RoadDemoBuilder` (drugi pipeline).

---

## 6. Šta je namerno OSTAVLJENO za kasnije (nije deo zadatka)
- Gde industrijski blokovi stoje u jezgru / gradu (`CoreLayout.Blocks` je fiksna tabela; grid
  grad bira bake po veličini lota, ne po zoni — `RoadDemoBuilder.Zones.cs:6-10`). Za kvart
  „Works“ biće ili nova tabela u `CoreLayout`, ili `IndustrialDistrict : IDistrict` po obrascu
  `PadDistrict`/`HarborDistrict` (`RoadDemoBuilder.Districts.cs:291 MakeDistrict`).
- Život (kamioni koji voze, radnici) — blok je statičan bake, kao i ostalih 16.
- Dimnjak sa dimom (`FX_Smoke_Black_Small_01`) — FX nije za bake.
- Benzinska pumpa, silos, rezervoar — nema gotovih modela (Town ima samo `SM_Prop_Gaspump_*`);
  ako korisnik zatraži, kitbash iz PolygonGeneric Base stubova + krovnih kapa, kao poseban zadatak.
