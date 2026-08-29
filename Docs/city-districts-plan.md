# Plan: luka i predgrađe kao kvartovi RoadDemo grada

> Stanje na dan 2026-08-19. Cilj: HarborDemo (luka) i SuburbDemo (predgrađe) postaju
> **kvartovi u RoadDemo sceni**, povezani putevima sa gridom kao jedan grad na ostrvu —
> a HarborDemo.unity / SuburbDemo.unity ostaju kao radne scene u kojima se ti kvartovi
> i dalje razvijaju. Pravilo broj jedan: **jedan kod, dva domaćina** — kvart se gradi
> istom funkcijom i u svojoj sceni i u gradu, pa svaka izmena u demou automatski važi
> i u RoadDemo-u. RoadDemo je prava igrica.
>
> Korisnik (2026-08-19): **predgrađa ima više (2–3), luka može na bilo koju obalu — raspored
> kvartova se generiše proceduralno po seed-u, grad ne sme uvek da bude isti.** Zato je
> rotacija okvira kvarta (yaw) u v1, spojevi sa gridom su fleksibilni (ne samo „tačno na
> liniji"), a mesto/broj/veličina kvartova roluje `CityLayout` (sekcija 1.4).

---

## 0. Gde smo sada (dijagnoza)

Tri buildera, tri scene, **nijedna scena ne serijalizuje ništa** (svaka `.unity` je jedan
prazan GameObject + builder komponenta sa default vrednostima iz koda). Dakle sve što
vidiš u bilo kom demou je **čist kod u `Awake`** — to je dobra vest: nema „bake-ovanih"
artefakata koje treba sinhronizovati, treba samo da isti kod bude pozvan iz RoadDemo-a.

| | RoadDemo (`Assets/RoadDemo/RoadDemoBuilder*.cs`) | HarborDemo (`Assets/HarborDemo`) | SuburbDemo (`Assets/SuburbDemo`) |
|---|---|---|---|
| Koordinate | pun grid, ishodište = JZ raskrsnica (`verticalRoadX[0]=0`, `horizontalRoadZ[0]=0`), +X istok, +Z sever; ose **nisu** višekratnici 5 (trotoar 6.5) | kej duž X na **z = 0**, more ka **−Z**, kopno +Z; sve apsolutno (svet = luka) | rešetka 5 m, mapa `[0,MapWidth]×[0,MapHeight]` od (0,0); `CellOf(w)=floor(w/5)` bez pomaka |
| Tlo / voda | ostrvo: heightfield oko grida + jedna ravan mora na **−2.65** (`RoadDemoBuilder.Island.cs`) | sopstveni heightfield 900×630 m + sopstvena ravan vode na **−2.65** (`HarborDemoBuilder.Ground.cs`) | zeleni Quad `MapWidth+600 × MapHeight+600` na −0.27 + travnati „skirt" |
| Scena (sunce, kamera, sky, pace, perf, refleksije) | `BuildEnvironment/BuildDayNight/BuildAudio/BuildMap`, DemoSky upravlja `RenderSettings` | svoje `Sun`, `Demo Camera` (MainCamera + AudioListener), `CrewDemoPace`, svoj perf pass | svoje `Sun` + skybox + ambient, `Demo Camera` (MainCamera + AudioListener), `CrewDemoPace`, ReflectionProbe, svoj perf/merge („Merged") |
| Putevi | `RoadNode[,] _nodes` / `List<RoadEdge> _edges` — **privatno**, `AddEdge` privatan; otvorenost segmenta kroz `SegmentOpen` / `InSeam` / `SeamSpan` (`Seams.cs:59-126`) | `StreetKit` (zadnja ulica + petlja u dvorištu), kamioni na **world rutama** (`HarborTruck`), bez grafa | **isti** `RoadNode/RoadEdge/TrafficSignal/DemoVehicle` kao RoadDemo, lokalna instanca (`Life.cs:15`); `StreetHalf=5`, trake ±2.5 — poklapa se sa gradskom ulicom |
| Pešaci | `PedNode/PedLink`, `CityLife _life`, `WalkObstacles`, `BuildWalkClearance` | `HarborWorker : PedestrianAgent` na svojim tačkama; ulazi samo u `PedestrianAgent.Walking` | `PedNode/PedLink` + svoja `CityLife` (bez sedenja), bez `SampleClearance`, bez `WalkObstacles` |
| Život | `_vehicles/_pedestrians/_policeCars/_crews`, `PoliceDispatch`, `StreetTraffic.Users` | brodovi, kranovi, viljuškari, dokeri, kamioni — `Update` u builderu | `SuburbVehicle`, stanovnici, besposličari — `Update` u builderu; kola **nisu** u `StreetTraffic.Users` |
| Kako ide van | ostrvo + more na sve 4 strane; margine W/E/N/S = 280/150/130/170 | zadnja ulica 540 m (±270), kamioni se rađaju/nestaju na krajevima van kadra; brodovi na ±380 | prsten ulica po obodu, drvored na −2.5 / +2.5 van mape, nema slepih izlaza |

### Šta bi se sudarilo ako se dva buildera samo stave u istu scenu
1. **Dve ravni vode na istoj visini** (luka/ostrvo) → z-fight; dva heightfield-a jedan kroz drugi; suburb Quad od 1000×900 m preseca ostrvo i more.
2. **Dva/tri `Sun`-a**, suburb i luka prepisuju `RenderSettings.ambient/skybox` koje `DemoSky` vozi svaki frejm.
3. **Dve/tri MainCamera + AudioListener-i**, tri `DemoCamera` koje sve slušaju WASD/točkić.
4. **`CrewDemoPace` vs `DemoClock`/`DemoTopBar`** — isti tasteri, oba pišu `Time.timeScale`.
5. `CivilianAgent.TickCrowd` bi se zvao dvaput po frejmu (suburb + grad), dva `Merged` root-a, dva upisa u `Logs/unreadable-meshes.txt`.
6. Grafovi puteva i pešaka su odvojene instance → ništa ne vozi/hoda iz grada u kvart.
7. Ni luka ni suburb ne mogu da se **pomere** pomeranjem root-a: statika bi otišla, ali graf, rute brodova/kamiona/viljuškara, vrata, kranovi (`_gantry.position` world) i agenti ostaju na starim koordinatama.

Zaključak: „zalepiti scene" (additive load) **nije opcija** — ne daje jedan grad, a pravi 1–5.
Potreban je **ugovor kvarta** i da oba demoa pređu na njega.

---

## 1. Ciljna arhitektura

```
 CoreDemo.unity                      HarborDemo.unity              SuburbDemo.unity
 ┌──────────────────────┐            ┌───────────────────┐         ┌───────────────────┐
 │ RoadDemoBuilder      │            │ HarborDemoBuilder │         │ SuburbDemoBuilder │
 │  = grid + šavovi +   │            │  = tanki domaćin  │         │  = tanki domaćin  │
 │    ostrvo + IDistrict│            │  (StandaloneHost) │         │  (StandaloneHost) │
 │    Host za N kvartova│            └────────┬──────────┘         └────────┬──────────┘
 └───────┬──────┬───────┘                     │                             │
         │      │                             │                             │
   ┌─────▼──┐ ┌─▼────────┐         ┌──────────▼──────┐            ┌─────────▼───────┐
   │Harbor  │ │Suburb    │   ==    │ HarborDistrict  │            │ SuburbDistrict  │
   │District│ │District  │  isti   │ (isti kod)      │            │ (isti kod)      │
   └────────┘ └──────────┘  kod    └─────────────────┘            └─────────────────┘
```

### 1.1 Ugovor kvarta (`Assets/RoadDemo/District.cs`, namespace `RoadDemo`)

- **`DistrictFrame`** — gde kvart stoji u svetu: `Vector3 origin` (X,Z pomak; Y = 0) i
  `yaw ∈ {0,90,180,270}`; `ToWorld(p)`, `ToWorldDir(d)`, `ToWorldRot(q)`, `ToLocal(p)`.
  Kvart računa **sve u lokalnim koordinatama** i kroz frame prolazi samo tamo gde dodirne
  svet (Instantiate, `RoadEdge.Start/End`, `PedNode.Pos`, rute, `Tf.position`,
  `_gantry.position`). **Yaw je u v1** jer luka može na bilo koju obalu: luka je autorisana
  „more ka −Z", pa jug = 0°, zapad = 90°, sever = 180°, istok = 270°. Kad se kvart već
  prevodi na „lokalno + frame", rotacija košta isto koliko i translacija — uslov je da se
  **nigde** ne piše world vrednost direktno (spisak mesta u tabeli 2). Suburb-u rotacija ne
  treba (grid je simetričan; pinuje se na bilo kojoj ivici bez okretanja).
- **`IDistrictHost`** — šta domaćin daje kvartu (i u demou i u gradu):
  - `Transform StaticRoot(string)` / `LiveRoot(string)` — koreni pod koje kvart parentira
    (domaćin ih posle pušta kroz *svoj* perf pass: merge, cull lejeri, senke);
  - `PedClips Clips`, `CityLife Life` (jedna instanca za ceo grad — vrata kvarta ulaze u nju),
    `RegisterVehicle(DemoVehicle)`, `RegisterCivilian(CivilianAgent)`, `RegisterWalker(PedestrianAgent)`
    (domaćin tiče i drži liste; kvart ne tiče ono što je predao);
  - `RoadNode NodeAt(DistrictPortal)` + `AddEdge/AddPedLink` — šivenje grafova (vidi 1.3);
  - `ReportMissing(prefabPath)` (umesto `Debug.LogError("[HarborDemo] missing prefab")`).
- **`IDistrict`** — šta kvart daje domaćinu:
  - `Rect Footprint` (lokalno) — pravougaonik koji kvart popločava sam (domaćin tu ne gradi tlo);
  - `DistrictReservations` — `FlatLand` (tlo ravno na zadatoj visini), `Water`
    (pravougaonik koji **mora** biti more — obala se tu ne sme probiti ka jugu),
    `NoFlora` (bez divljine, +9 m margina kao za grid);
  - `DistrictPortal[]` — spojevi: lokalna tačka na ivici footprinta, smer, profil
    (`Street` = 2 trake, trotoar) + koje pešačke čvorove nosi;
  - `Build(IDistrictHost, DistrictFrame, settings)`, `Tick(dt)`, `Dispose()`;
  - `MapStrokes` za taktičku mapu (ulice, voda, placevi/hale) — kao što `SeamInfo` danas hrani mapu.
- **Settings** = ono što su danas javna polja buildera, kao `[Serializable] class HarborSettings` /
  `SuburbSettings` sa **istim default-ima u kodu** → demo scena i grad dobijaju isti kvart.
  Seed kvarta je njegov (`HarborSettings.seed = 1987`), ne gradski → luka sa seed-om 1987 je
  **identična** u HarborDemo.unity i u CoreDemo.unity (to je i test: screenshot oba).
  (Kasnije, ako zatreba tuning po sceni: `ScriptableObject` preset koji obe scene referenciraju.)

### 1.2 Dva domaćina, isti ugovor
- `RoadDemoBuilder : IDistrictHost` — novi partial `RoadDemoBuilder.Districts.cs`
  (tu smeju da se zovu privatni `AddEdge`/`AddPedLink` jer je partial). Inspector:
  ```
  [Serializable] class DistrictSlot { DistrictKind kind; Edge edge; int[] pinLines; float strip; int seed; HarborSettings/SuburbSettings }
  public int cityLayoutSeed = 7;            // raspored kvartova (isti seed = isti grad)
  public bool rollDistricts = true;         // true: CityLayout.Roll(seed) puni districts[]; false: ručni districts[]
  public DistrictSlot[] districts;          // rezultat rola (vidljiv/izmenljiv) ili ručni plan
  ```
  Generisani raspored se **upisuje u `districts[]`** pre gradnje, pa je svaki grad čitljiv u
  inspectoru, može da se „zamrzne" (`rollDistricts=false`) i ručno dotera — isti obrazac kao
  `Respace()` koji prepisuje `verticalRoadX` iz palete.
- `StandaloneDistrictHost : MonoBehaviour, IDistrictHost` (u `Assets/RoadDemo`) — ono što
  `HarborDemoBuilder` i `SuburbDemoBuilder` danas rade pored samog kvarta: sunce, `DemoCamera`
  (pivot/dist iz footprinta), pace, jednostavno tlo/more, perf pass, `Update` koji tiče kvart.
  `HarborDemoBuilder`/`SuburbDemoBuilder` ostaju imena komponenti u scenama, ali postaju
  **tanki**: `settings` + `new HarborDistrict().Build(this, frame0, settings)`.
  Kvart **nikad** ne pravi sunce/kameru/sky/pace/probe/perf/`Merged` — to je domaćin. Tako se
  tačke 2–5 iz dijagnoze ne mogu ponoviti ni slučajno.
- Perf: tri kopije istog pass-a (`RoadDemoBuilder.Perf.cs`, `HarborDemoBuilder.Perf.cs`,
  `SuburbDemoBuilder.Perf.cs`) → jedan `RoadDemo/ScenePerf.cs` (`Optimise(roots)`,
  `AssignCullLayers(roots)`, `Merge(roots)`), pozivaju ga oba domaćina nad listom korena
  (grad: 5 svojih + statički koreni kvartova; standalone: koreni kvarta). Cull lejeri/daljine su
  već isti brojevi (20/21/22, 230/330/480).
- Tlo/more: ostrvo (`Island.cs`) dobija **rezervacije** (1.1) i gradi se oko *unije* grida i
  footprinta kvartova (danas samo oko grida, `BuildIsland(gx0..gz1)`). Standalone domaćin
  koristi **isti** `IslandTerrain` kod sa malim marginama → obala luke u demou i u gradu je
  ista (lukin `Ground.cs` heightfield/voda se gase; njegov profil obale `BeachRun/ShelfRun`
  i `shoreFoam/shallowSand` prelaze u zajednički teren kao parametri uz `Water` rezervaciju).
  Do tada (prelazno) standalone može da zadrži svoje tlo — ali to je tačno ono što ne želimo
  dugoročno, pa je u planu kao korak 3c.

### 1.3 Spajanje puteva: „pin na gradsku liniju", bez novih vrsta čvorova

Najjeftinije i najsigurnije je da se kvart kači **samo na krajeve postojećih gradskih linija**
(čvorovi `(i, 0)` na jugu, `(i, nh−1)` na severu, …), jer RoadDemo pravi čvor za svaku
kombinaciju linija, a „zatvorena" strana čvora je već samo *odsustvo* segmenta
(`SegmentOpen=false` → kapa trotoara umesto zebre). Potreban je jedan opšti pojam:
**spoljni krak** — `ArmOpen(i, j, dir)` koji za ivične čvorove kaže „ovde nastavlja spoj ka
kvartu". Prolazi koji danas gledaju `SegmentOpen` (geometrija čvora, `FillVertical/HorizontalSegment`,
`BuildGraph`, `BuildSignals`, `BuildPedGraph`, `DressStreets`) dobijaju i spoljne krakove;
segment spoja (od ivice grida do portala kvarta, preko „pojasa" divljine) se polaže
StreetKit-om (`LayAlongX/Z` sa trotoarom, dužina proizvoljna — kit polaže od `from` u
5 m koracima i seče poslednju ćeliju) i dobija 2×1 traku u grafu + 2 pešačke veze.

- **Profil spoja = obična ulica (2 trake, ±2.5 m)** — isti kao suburb ulica i kao lukina
  kapijska ulica. Zato pinovi idu na **obične** gradske linije (ne bulevare; bulevar od
  4 trake ne može da se suzi u 2 bez novog kita).
- **Tačan pin — pravilo petice.** Gradske ose nisu na rešetki 5 m (svaki razmak = 5k + 3,
  jer trotoar 6.5 dodaje 2×1.5; šav isto 5k+13+2·half), ali zato su linije `i` i `i+5`
  uvek na istoj rešetki (5·3 = 15). Suburb ima rešetku 5 m → ako mu ishodište stavimo na
  `x_i − 10` (osa njegove linije 0 = `x_i`), onda i `x_{i+5}` pada tačno na neku njegovu
  liniju. Važi na svakoj ivici (sever/jug po vertikalnim, istok/zapad po horizontalnim linijama).
- **Kosi spoj (≤ 2.5 m).** Da generator ne bi bio vezan samo za parove linija razmaka 5:
  segment spoja sme da bude **blago zakošen** — od gradskog čvora do portala kvarta koji je
  bočno pomeren do 2.5 m (preko pojasa od ≥ 40 m to je ≤ 3.6°). Graf to ne primećuje (ivica
  je prava od lica do lica čvora, `DemoVehicle` vozi pravu), StreetKit pločice se polažu pod
  tim uglom od `from`, a kvadrati raskrsnica na oba kraja (goli asfalt 10×10) pokrivaju
  sitne klinove na ivičnjaku. Pošto je suburb na rešetki 5 m, **uvek** postoji rešetkasta
  linija na ≤ 2.5 m od bilo koje gradske ose — plan suburba samo treba da na tu rešetkastu
  liniju stavi svoju ulicu. Prvi pin je uvek tačan (njime se bira ishodište), ostali su
  tačni ako važi pravilo petice, inače kosi.
- **Suburb se pinuje na 2–3 obične gradske linije** (po jedna ulica spoja na svaki pin) i
  njegov `PlanLines(pins)` deli širinu na kolone tako da na svaki pin padne linija: dužine
  blokova više nisu iz palete nego bilo koji višekratnici 5 između 70 i 100 (placevi se ionako
  seku iz fronta po 15/20/25 m, broj kuća po bloku ostaje 3–5); razmaci pinova koji se ne mogu
  podeliti na korake 90–120 m (n·pitch) generator odbacuje. Linije grida između pinova
  završavaju na ivici kao danas (kapa), pojas između je divljina/livada.
- **Luka**: kapije (`_gateWestX/_gateEastX`) postaju **ulazi = dve obične gradske linije**
  (razmak 180–260 m, tj. 2 bloka) koje se produžuju preko pojasa do lukine zadnje ulice
  (krst), pa kroz kapiju (T) — i dalje njen `StreetKit`. Luka ne treba rešetku za x kapija
  (kit polaže od `from`); vezovi ostaju simetrični oko centra luke,
  `QuayHalf = max(berths·pitch/2+5, spanKapija/2+20)`. Luka na istoku/zapadu: pinovi su
  horizontalne linije, frame yaw 270/90.
- Kasnije (ako ikad zatreba spoj na sred segmenta) → `SpliceNode` koji seče par ivica i
  pravi čvor van rešetke; **nije** u v1 (kosi spoj ga čini nepotrebnim za ova dva kvarta).

### 1.4 Raspored kvartova: `CityLayout.Roll(seed)` (proceduralno, ne fiksno)

Jedan primer rola (seed A) — sledeći seed daje luku na zapadu i predgrađa na jugu/severu:
```
                      S E V E R
            ┌──────────────┐        ┌──────────────┐
            │  SUBURB 1    │        │  SUBURB 2    │      ← pojas 40–60 m, 2–3 spoja svaki
   ┌────────┴──────────────┴────────┴──────────────┴────┐
   │  GRID 16×10 linija, šavovi: reka / park / wild     │  ← reka izlazi na I/Z: ti pojasevi
   │                                                    │     su zabranjeni za kvartove
   └─────┬──────────────────────┬───────────────────────┘
         │  pojas ~40 m          │                        ┌──────────┐
       ┌─┴──────────────────────┴─┐                      │ SUBURB 3 │ (istok, pin po horiz. linijama)
       │  LUKA  (yaw 0)           │                      └──────────┘
       └──────────────────────────┘
                 M O R E
```
**Ulaz generatora:** `cityLayoutSeed`, grid posle `Respace()` (ose, bulevari, šavovi i gde
izlaze iz grida), palete veličina kvartova. **Izlaz:** `districts[]` (slotovi sa ivicom,
pin-linijama, pojasom, seed-om kvarta, settings).

Koraci rola (deterministično iz `System.Random(cityLayoutSeed)`):
1. **Kandidat-ivice.** Svaka od 4 ivice grida se deli na *slobodne dužine*: od ćoška do
   ćoška, minus izlazi reke (± 60 m), minus rezervisani ćoškovi ostrva (ostrvo ne dobija
   kvart u uglu — da obala ostane obala). Na svakoj slobodnoj dužini kandidati za pin su
   **obične** linije (ne bulevari; ne linija odmah uz izlaz reke).
2. **Luka — jedna po gradu.** Bira se ivica (sve 4 ravnopravne; težina može da favorizuje
   dužu obalu), pa par običnih linija sa 2 bloka razmaka (180–260 m) → kapije; frame yaw
   iz ivice (J 0 / Z 90 / S 180 / I 270), `origin` tako da zadnja ulica luke legne
   `strip` (40 m) iza ivične ulice grida; `berths` 2–4 i `berthPitch` iz dozvoljenog
   raspona širine. Rezervacije: `FlatLand` od ivice grida do keja, `Water` = koridor
   brodova (lokalno `x ∈ ±(QuayHalf+240+60)`, `z ∈ [−110, 0]`) → `CoastDistance` u tom
   opsegu se **steže** na liniju keja (obala ne sme ispred lane-ova `z = −24 −14·i`), van
   koridora obala luta kao danas → bazen luke usečen u obalu; `NoFlora` = footprint + 9 m.
3. **Predgrađa — `suburbs = 2..3`** (inspector raspon, rolovano). Za svako: ivica (ne ista
   dužina koju je uzela luka; ista ivica sme ako stane sa razmakom ≥ 80 m), broj kolona
   3–5 i redova 2–4 (→ širina 300–600 m, dubina 150–300 m), 2–3 pina po pravilima iz 1.3
   (prvi tačan, ostali tačni ili kosi), `strip` 40–60 m, seed kvarta =
   `hash(cityLayoutSeed, index)` → svako predgrađe ima **svoje** kuće, palete, ćorsokake,
   dužine blokova. Ako se pinovi ne mogu zadovoljiti, rola se ponavlja na drugoj dužini
   (max N pokušaja, pa manji suburb).
4. **Provera preklapanja** footprinta + pojaseva u svetu (AABB, sa 80 m razmaka); ostrvo
   računa margine po ivici: `margin(edge) = max(base, strip + dubina kvarta + baseWild)`,
   a unutar footprinta + skirt heightfield preskače ćelije (kao za grid), bez suburb Quad-a.
5. Upis u `districts[]` + `Debug.Log` tabele (ivica, linije, yaw, origin, seed) i crtanje u
   `OnDrawGizmos`/taktičkoj mapi — da se svaki rol vidi i na mapi.

Šta je **fiksno** bez obzira na rol: profil spoja (obična ulica), pravila pinova, pojas ≥ 40 m,
najviše jedna luka, ostrvo oko svega, demo scene = isti kvart sa `frame0` i istim settings-om
(seed kvarta se može upisati u demo scenu da se reprodukuje baš taj primerak iz grada).

### 1.5 Život u kvartovima (šta postaje zajedničko)
- **Jedan `CityLife`** (domaćinov): suburb vrata (`AddStop`) i klupe ulaze u njega → gradski
  civili mogu da odšetaju u predgrađe i uđu u kuću; suburb stanovnici (`pedestrianCount`)
  idu u domaćinovu listu (`CivilianAgent.TickCrowd` jednom po frejmu, `PairChats` jednom).
- Suburb kola → `RegisterVehicle` → u `_vehicles` + `StreetTraffic.Users` (crew/policija ih
  vide); `SuburbVehicle` (duga vozila izbegavaju stub-ove) ostaje kao podklasa.
- `WalkObstacles.Block` za kuće/placeve suburba i hale/blokove kontejnera/ogradu luke
  (CrewWalker ne sme kroz kuću); `PedLink.SampleClearance` i za suburb trotoare.
- Policija: `PolicePatrolCar.RouteToward` je BFS nad ivicama → čim su grafovi zašiveni,
  patrole same stižu u predgrađe; `PoliceDispatch` jedan (domaćinov). Crews: `DemoCrews.Init`
  dobija spojene `_pedLinks`.
- Luka: `HarborWorker`, kranovi, brodovi, viljuškari — tiče ih kvart (`Tick`), domaćin zove.
  **Kamioni v1**: ostaju `HarborTruck` na world rutama po lukinoj zadnjoj ulici + petlji, ali
  se rađaju/nestaju **na spoju sa gradom** (kod krsta sa linijom i_w / i_e) umesto na krajevima
  van kadra; v2 (po `Docs/vehicle-movement-plan.md`): kamion = vozilo na grafu traka koje
  dolazi iz grada, zadnja ulica luke se stapa u red 0 grida.
- Noćni prozori / lampe / farovi / audio / mapa: `DemoNightWindows.facadeRoot` i
  `DemoStreetLamps` dobijaju i korene kvartova; `DemoAudio`/`DemoHeadlights` rade nad
  domaćinovim listama (suburb kola su već tamo); taktička mapa crta `MapStrokes` kvartova.
- Klik-kartice: `BuildingCardPicker.pickRoot` je jedan Transform → drugi picker za kvartove ili
  zajednički roditelj `Districts` pod koji idu i blokovi (odluka u Fazi 4).

---

## 2. Šta zadržati, šta menjati

| Zadržati kakvo jeste | Menjati | Ukinuti |
|---|---|---|
| sav sadržaj kvartova (kej, hale, brodovi, kuće, dvorišta, klasteri, plan suburba) | koordinate: lokalno + `frame` (luka: `_origin` na mestima gde nastaju tačke — `BerthX`, z-konstante, legovi `HarborShipping`, `HarborCrane.Apply`, rute kamiona/viljuškara/radnika; suburb: `originX/Z` kroz `PlanLines`, `CellOf`, `AW/AC`, `TownKit.Tile/Prop` pozivi, `MapWidth/Height`) | lukino `BuildWater/BuildGround` (→ ostrvo + rezervacije) |
| `RoadNode/RoadEdge/PedNode/PedLink/CityLife/DemoVehicle/CivilianAgent` kao deljene klase | `RoadDemoBuilder`: `ArmOpen` pored `SegmentOpen`; `BuildIsland` nad unijom + rezervacije; `Update` tiče kvartove; `BuildMap`/`DayNight`/`Audio` dobijaju kvartove | suburb Quad 600 m + skirt van footprinta, `BuildLight`, `BuildCamera`, `BuildReflections` u modulu (→ domaćin) |
| `StreetKit` za spojeve i lukine puteve | suburb `PlanLines` prima pinove; luka prima x kapija od domaćina | `CrewDemoPace` u kvartovima (grad: `DemoClock`/`TopBar`; standalone: domaćin ga doda) |
| seedovi kvartova | `HarborDemoAutoBake` okida i kad je u sceni `RoadDemoBuilder` sa lukom (danas traži samo `HarborDemoBuilder`) | tri kopije perf pass-a → jedan `ScenePerf` |

---

## 3. Faze (svaka sa proverom)

### Faza 0 — `CityLayout.Roll` na papiru i u gizmu (pola dana)
- Odluke korisnika već date: više predgrađa (2–3), luka na bilo kojoj obali, sve po seed-u.
  Ostaju rasponi u inspectoru: `suburbs 2..3`, kolone/redovi suburba, `strip 40..60`,
  `berths 2..4`, razmak kapija.
- Napisati `CityLayout` (čista klasa, bez Unity scene) + `OnDrawGizmos` u RoadDemoBuilder-u
  koji crta rolovane footprinte/pinove/rezervacije preko grida **pre** ikakve gradnje;
  `Tools/City/Dump City Layout` ispisuje tabelu za seed; proći 10 seed-ova.
- Izmeriti `_streetZ` luke (≈112.5), `QuayHalf` po broju vezova, dubinu suburba po redovima.
- **Provera:** za 10 seed-ova nijedno preklapanje, pinovi obične linije, kosi spojevi ≤ 2.5 m,
  luka nikad na izlazu reke, ostrvo stane oko svega (kamera max boom ≤ 900).

### Faza 1 — ugovor i domaćin u RoadDemo (sa lažnim kvartom)
1. `Assets/RoadDemo/District.cs`: `DistrictFrame`, `IDistrictHost`, `IDistrict`,
   `DistrictReservations`, `DistrictPortal`, `DistrictSlot`.
2. `RoadDemoBuilder.Districts.cs`: `districts[]`, `BuildDistricts()` posle `BuildSeams`
   a pre `DressStreets`/`BuildGraph` (da krakovi uđu u graf i geometriju); `ArmOpen`;
   polaganje segmenta spoja StreetKit-om; `AddEdge`/`AddPedLink` za spoj; `Tick` kvartova u
   `Update`; statički koreni kvartova u perf pass; `MapStrokes` na taktičkoj mapi.
3. `Island.cs`: `BuildIsland(unionRect, reservations)`; `IslandHeight` = 0 u `FlatLand`,
   preskok ćelija u footprintima, `CoastDistance` stegnut u `Water`, `DressWilderness`
   preskače `NoFlora`.
4. `ScenePerf.cs` izvučen iz `RoadDemoBuilder.Perf.cs` (grad ga zove isto kao do sada).
5. Lažni kvart `PadDistrict` (ravna ploča 200×120 + jedan portal) za test.
- **Provera (Play, RoadDemo):** ploča stoji na severu/jugu, ostrvo ravno ispod nje, bez
  drveća na njoj, spoj je vozna ulica (policijski auto BFS-om stiže do portala), pešak hoda
  do nje, mapa je crta, frame-time kao pre.

### Faza 2 — Suburb kao kvart
1. `SuburbDemoBuilder*` → `SuburbDistrict` (partial klasa, isti fajlovi; `Awake/Update/
   OnDestroy/BuildLight/BuildCamera/BuildReflections/CrewDemoPace/perf` sele se u tanki
   `SuburbDemoBuilder : MonoBehaviour` + `StandaloneDistrictHost`).
2. `originX/Z` kroz plan i graf; `PlanLines(pins)`; spoljni krakovi na pin-čvorovima
   (`n.S/N` pre `MarkStreets` → zebra umesto trotoara, `Segment`+`RoadEdge` ka portalu);
   bez drvoreda/skirta preko trake spoja; bez Quad-a u hosted modu (standalone: mali teren).
3. Vrata → domaćinov `CityLife`; kola/ljudi → domaćin; `WalkObstacles.Block` po placu;
   `SampleClearance`.
- **Provera:** SuburbDemo.unity izgleda isto kao pre (screenshot pre/posle, `DemoBatchShot`);
  u RoadDemo: 2 spoja, kola iz grada ulaze u predgrađe i vraćaju se, stop-znaci rade,
  stanovnici ulaze u kuće, patrola prolazi, seed 1987 → iste kuće u obe scene.

### Faza 3 — Luka kao kvart
1. `HarborDemoBuilder*` → `HarborDistrict` (isti rez kao suburb); `_origin` na mestima gde
   nastaju tačke (spisak u tabeli 2); `gateWestX/gateEastX` iz portala.
2. Rezervacije: `FlatLand` (ulica→kej), `Water` (koridor brodova), `NoFlora`; lukin
   `BuildWater/BuildGround` samo u prelaznom standalone modu.
3. `IslandTerrain` zajednički: profil obale iz `HarborDemoBuilder.Ground.cs` (`BeachRun/ShelfRun`,
   podvodni prag uz kej, pena/pesak knobovi) ulazi kao parametri `Water` rezervacije → luka u
   demou i u gradu na **istom** terenu; lukin Ground.cs se briše.
4. Kamioni v1: spawn/despawn kod spoja sa gradom; `HarborDemoAutoBake` okida i za RoadDemo.
5. Hale/kontejneri/ograda → `WalkObstacles.Block`; `DemoNightWindows` koren luke.
- **Provera:** HarborDemo.unity isto kao pre; u RoadDemo: brodovi ulaze sa horizonta bez
  probijanja obale (koridor), kranovi rade, kamioni ulaze kroz zapadnu i izlaze kroz istočnu
  kapiju, gradski auto može da dođe do kapije, voda jedna (bez z-fight-a), perf (merge,
  cull lejeri) obuhvata luku.

### Faza 4 — zajednički život i scena
- jedan `CityLife`, jedan `TickCrowd`, `StreetTraffic.Users` kompletan, `PoliceDispatch`
  pokriva kvartove, `DemoCrews` nad spojenim graf-om, picker za kuće/hale, mapa/moveri,
  `DemoAudio` (brodske sirene kasnije), kamera max boom po uniji.
- **Provera:** pucnjava u predgrađu → policija iz grada dolazi; crew prošeta do luke.

### Faza 5 — zaštita „reflektovanja" i čišćenje
- `Tools/City/Check Districts`: za svaki kvart ispiše footprint/portale/rezervacije, proveri
  BFS povezanost grafa od gradskog čvora do svake ivice kvarta, da nema drveta/heightfield
  ćelije u `FlatLand`, da nema druge MainCamera/AudioListener/Sun u sceni, da su seedovi isti
  kao u demo scenama.
- `DemoBatchShot` u obe demo scene + RoadDemo, isti seed → vizuelna regresija.
- Dalje (van ovog plana): kamioni na grafu (vehicle-movement-plan), yaw okviri, karakter
  kvartova (industrija uz luku), još kvartova po istom ugovoru.

---

## 4. Pravila za buduće izmene kvartova (da „reflektovanje" ostane tačno)

1. Sve što kvart gradi ide **pod njegove korene iz domaćina** i u **lokalnim koordinatama** +
   `frame`. Nijedna world konstanta (z=0 kej, x=±380 spawn) bez `frame.ToWorld`.
2. Kvart **ne pravi** sunce, kameru, sky, pace, reflection probe, `Merged`, `RenderSettings`,
   ravan vode, heightfield. To daje domaćin. Kvart *traži* (rezervacije), ne *gradi* teren.
3. Životi se **predaju** domaćinu (`Register*`), kvart tiče samo svoje specifične stvari
   (brodovi, kranovi, viljuškari, besposličari).
4. Novi spoj sa gradom = novi `DistrictPortal`, nikad ručno pozvan `AddEdge` iz kvarta.
5. Svaka nova podešavanja idu u `*Settings` sa default-om u kodu (ne u scenu).
6. Posle izmene kvarta: Play u demo sceni **i** `Tools/City/Check Districts` u RoadDemo.
7. Ne vraćati scene-lokalne trikove u `HarborDemoBuilder`/`SuburbDemoBuilder` MonoBehaviour —
   oni su samo domaćini; sadržaj ide u `*District`.

---

## 4b. Šta je URAĐENO (2026-08-19, kod napisan, Play još neproveren)

Sve faze 0–5 su implementirane. Kompajl proveren Roslyn-om (oba asemblija, 0 grešaka;
paralelna sesija u istom repou piše `Assets/AirportDemo` i `LaneNet/RoadDemoNet` — te greške
nisu naše). **Ništa još nije viđeno u Play-u.**

**Novi fajlovi**
- `Assets/RoadDemo/District.cs` — `DistrictFrame` (origin + yaw 0/90/180/270, `ToWorld/ToLocal/ToWorldRect`),
  `IDistrict` (Plan → Reserve → Build → Portals → Tick), `IDistrictHost`, `DistrictReservations`
  (`Pave/Level/Sea/NoFlora`), `DistrictPortal`, `DistrictKind`, `CityEdge`.
- `Assets/RoadDemo/ScenePerf.cs` — jedan perf pass za sve domaćine (senke, cull lejeri, merge);
  tri stare kopije obrisane/izvedene na njega.
- `Assets/RoadDemo/CityLayout.cs` — `DistrictSlot` + `Roll(grid, seed, suburbsMin/Max, harbor)`.
- `Assets/RoadDemo/StandaloneDistrictHost.cs` — domaćin za demo scene (sunce, kamera, sky, pace,
  refleksije, perf, tick).
- `Assets/RoadDemo/RoadDemoBuilder.Districts.cs` — grad kao domaćin: `PlanDistricts` (rol, frame,
  rezervacije, `OpenArm`), `BuildDistricts` (gradnja + spojne ulice + varenje grafova), `IDistrictHost`.
- `Assets/RoadDemo/PadDistrict.cs` — prazan kvart za test ugovora.
- `Assets/SuburbDemo/SuburbDistrict*.cs` (preimenovani partial-i) + `SuburbDistrict.Portals.cs`.
- `Assets/HarborDemo/HarborDistrict*.cs` (preimenovani partial-i) + `HarborDistrict.Portals.cs`.
- `Assets/Scripts/Editor/CityDistrictCheck.cs` — `Tools/City/Dump City Layout` (edit mode) i
  `Tools/City/Check Districts (in Play)`.

**Kako je rešeno pomeranje kvarta** (bilo je otvoreno pitanje u planu):
- *Statika*: kvart se crta u SVOJIM koordinatama, a na kraju se koreni pomere/okrenu
  (`MoveIntoPlace`) — nijedan poziv `Instantiate` nije menjan.
- *Suburb*: posle gradnje se konvertuju i podaci (`RoadNode`, `RoadEdge`, `PedNode`, `DemoDoor`),
  pa se tek onda spawnuje život → agenti odmah rade u svetu.
- *Luka*: brodovi i kranovi pišu `localPosition` pod pomerenim korenom; kamioni/viljuškari/dokeri
  dobijaju rute već prevedene u svet (`WorldPoints`); `HarborCargo`/`HarborCrane` dobili `Frame`
  (kutije lete u svetu, kran ih vraća u lokalne).

**Odstupanja od plana**
- „Kosi spoj ≤ 2.5 m" NIJE potreban i nije implementiran: pravilo petice važi univerzalno —
  svaki razmak grida je ≡ 3 (mod 5) (provereno simulacijom nad stvarnim konstantama:
  ulica/bulevar/šav daju 28/38/48 + interior), pa su linije i, i+5 uvek na istoj 5 m rešetki.
  Predgrađe deli razmak između pinova na blokove koji staju tačno (`SplitRun`, provereno za sve
  raspone 490–740 m).
- Standalone demo scene i dalje grade SVOJE tlo (`IDistrictHost.ProvidesGround == false`) — luka
  zadržava svoj heightfield/vodu i plažu, suburb svoj travnati kvad. U gradu ih gasi ostrvo.
  (Zajednički `IslandTerrain` iz plana nije rađen — nepotrebno rizično za izgled demoa.)
- Luka u gradu dobija i mali graf traka (zadnja ulica sa raskrsnicama na kapijama) da policija
  i saobraćaj mogu da uđu; kamioni i dalje voze svoje rute.
- Predgrađa mogu dobiti i JEDAN pin (kad obala nema par linija razmaka 5) — tada imaju jednu
  ulicu ka gradu; rol prvo pokušava dva/tri pina.

**Ostalo nedovršeno (svesno)**
- `DemoNightWindows.facadeRoot` je jedan Transform → prozori u kvartovima ne svetle noću.
- ~~mapa ne crta kvartove~~ — rešeno: turf mapa ih crta (`TurfMapSurvey.DrawQuarters`, `TurfDistrict`).
- `ScaleLifeToCity` broji samo gradske placeve (kvartovi donose svoj život, pa je to OK).
- Trotoar spoja je gradskih 6.5 m, a suburb nastavlja svojim 5 m; visina 0.13 vs 0.10.

## 4a. Kako se proverava kompajl bez editora

Roslyn iz Unity instalacije nad Bee `.rsp` fajlovima (editor može biti zauzet/zaključan):
- runtime asembli: `Library/Bee/artifacts/1900b0aE.dag/Assembly-CSharp.rsp` (896 linija: reference + spisak fajlova)
- editor asembli: `.../Assembly-CSharp-Editor.rsp`
- Unity 6000.5.7f1 (`C:/Program Files/Unity/Hub/Editor/6000.5.7f1/Editor/Data/...` → `Roslyn/csc.exe`)
Novi fajlovi se **moraju dopisati** u kopiju `.rsp`-a pre provere (Bee ih generiše tek kad
editor osveži projekat). Prvi Play u editoru je i dalje jedina prava provera.

## 5. Otvoreno / pretpostavke
- Raspored je rolovan (1.4); izlazi reke i uglovi ostrva su zabranjeni za kvartove —
  pretpostavka (luka na ušću reke bi bila lepa, ali kej vs. kanal je posao za kasnije).
- Luka na S/I/Z znači yaw u v1: sva mesta iz tabele 2 moraju kroz `frame` (najrizičnije
  `HarborShipping` legovi, `HarborCrane.Apply`, `HarborCargo` letovi kutija, rute kamiona);
  prvi test luke u gradu raditi sa yaw 180 (sever) da se rotacija odmah vidi.
- Širina pojasa 40–60 m: dovoljno za ogradu+živicu luke i „kraj grada"; prevelik pojas =
  dva grada.
- Više predgrađa → `ScaleLifeToCity` mora da računa i placeve kvartova (inače gomila/auti
  ostaju skalirani samo po gridu), a perf budžet se proverava sa 3 suburba + luka.
- Razlika trotoara (grad 6.5 / suburb 5 / kit Town pločnik y 0.13 vs 0.10) se rešava na
  segmentu spoja (StreetKit trotoar 6.5 do portala, suburb nastavlja svojim 5 m; prelaz visine
  0.03 je ispod praga vidljivosti).
- Lukina zadnja ulica u v1 je njena (ne red 0 grida) zbog `HarborTruck` world ruta; stapanje u
  grid ide sa prelaskom kamiona na graf traka.
- `PedestrianAgent.Walking` je već globalan — dokeri i gradska gomila se bucket-uju zajedno
  (ništa ne treba menjati, samo znati).
- Rotacija kvartova (yaw ≠ 0) nije u v1.

---

## 6. Scene (updated 2026-08-28)

| scena | šta je | ko gradi |
|---|---|---|
| `Assets/Scenes/CoreDemo.unity` | glavni integracioni harness: core grad, shared gameplay i kamera/mapa | `CoreDemoBuilder` → isti `RoadDemoBuilder` |
| `Assets/Scenes/BlockDemo.unity` | **jedan kvart**: mreža blokova sa ulicama, avenijama i raskrsnicama između njih — sto za fine-tune | `BlockDemoBuilder` → isti `RoadDemoBuilder`, mali grid |
| `Assets/Scenes/HarborDemo.unity` | luka sama | `HarborDemoBuilder` → `HarborDistrict` |
| `Assets/Scenes/SuburbDemo.unity` | predgrađe samo | `SuburbDemoBuilder` → `SuburbDistrict` |

`Game.unity` je penzionisana 2026-08-28 nakon što su layout default-i i mapa prebačeni na
shared sisteme; scena i njen GUID su uklonjeni iz projekta. Build Settings je namerno prazan dok
se ne odabere novi product bootstrap, a `CoreDemo` ostaje test harness realnog ponašanja.

**BlockDemo** je ista pogodba kao luka/predgrađe: ne crta ništa sam, nego pušta gradski
builder na mali grid (podrazumevano 3×3 bloka = 4×4 linije), pa su placevi, bake, pod,
ivičnjak, vrata i gomila isti kod koji ih postavlja u gradu. Podešavanja: `columns`/`rows`
(koliko blokova), `randomiseSizes` + `spacingSeed` (mere se dele iz istih paleta i mešaju
kao u gradu), `avenuesNorthSouth`/`avenuesEastWest` (koje linije su bulevari),
`blockCycle` (od kog bakea po pad šifri kreće deljenje), život, `greenBelt`. Bez šavova
(kvart je ono što je IZMEĐU šavova), bez luke i predgrađa, `scaleLifeToCity` isključen —
brojevi sa inspektora važe kakvi jesu. Konzola ispiše spisak svih placeva sa pad šifrom
i imenom bakea koji je stao.

---

## 7. Autoput-prsten (belt) i aerodrom — mreža autoputeva na ostrvu (2026-08-19)

Korisnik: *„mreža autoputeva treba da postoji na ostrvu u Game modu; mreža spaja velike
rezidencijalne blokove, luku, a sad i aerodrom."*

**Šta je bilo:** jedan nadvožnjak (seam `Highway`) preko grida, koji se spušta na ostrvo i završava
T-om u kratku „link" ulicu duž te dve obale (`BuildHighwayLinks`) — kvartovi na druge dve obale
nisu bili na autoputu, a link ulice nisu bile ni u grafu traka.

**Šta je sada (`Assets/RoadDemo/RoadDemoBuilder.Belt.cs`):**
- **Belt** = dvosmerni autoput u nivou tla (dve Highway deck pločice jedna uz drugu, 2×2 trake na
  ±3.1/±8.3 m od ose, `BeltHalf` 11.4) koji **obilazi ceo grad** na `BeltOut = 166 m` od spoljnog
  trotoara grida — tačno tamo gde se nadvožnjak spušta (20+44+40+40+22), pa se njegov T sada
  završava NA prstenu (`BuildHighway`: `linkS/linkN = ext ± BeltOut`; deck pločice prstena se
  prekidaju oko T-pada). Uglovi su asfaltni padovi 30×30 (`BuildBlockFloor`).
- **Svaka ulica ka kvartu seče prsten** na raskrsnici u nivou: pad 25 m (duž prstena) × 30 m
  (preko), `LayConnector` seče ulicu na `beltU ± BeltPadHalf(15)`; `WeldRoads` polaže DVA puta
  (lice grada → belt čvor, belt čvor → portal). Kutija čvora: 7.5 duž prstena × 15 preko,
  `StopSetback` 2.5, bez semafora (RoadCar daje prednost po konfliktima kao u predgrađu).
- **Prsten JE u LaneNet-u** (`Net.AddRoad` sa `SurfaceY = GradeY` — nova `Carriageway.SurfaceY`,
  `RoadCar.SurfaceLift()` diže auto na pločicu u nivou 0.12 i lerpuje kroz čvor): saobraćaj se
  sam rasipa po njemu (`SpawnCars` po `_edges`), patrola BFS-om stiže iz predgrađa u luku/aerodrom
  bez prolaska kroz centar, crew auto isto. Nadvožnjak ostaje `HighwayTraffic` (okretanje na repu).
- Tlo: `Level(…,0)` + `NoFlora` duž koridora, upis u `_highwayRuns` (`HighwayFade` brda stoje
  dalje, bez flore ±12 m). Mapa ga (kao ni kvartove) ne crta.
- `beltFreeway` prekidač (off → stari `BuildHighwayLinks`), `beltSpeed` 17 m/s.
- Redosled u `Awake`: `Respace → PlanBelt → PlanDistricts → … → BuildGraph → … → BuildBelt →
  BuildDistricts → BuildHighwayLinks(samo bez prstena)`.

**Aerodrom kao kvart:** `DistrictKind.Airport`, `AirportDemo.AirportDistrict` (v. `Docs/airport-demo-plan.md`
§12), `CityLayout.Roll(..., wantAirport)` — aerodrom prvi, cela obala, `WorldRect` provera ugla.

**Neprovereno u Play-u.** Kompajl Roslyn-om čist (4a). Provera posle Play-a: patrola iz predgrađa do
aerodroma ide prstenom; auti na prstenu ne tonu u pločicu (SurfaceY); padovi u nivou sa deck-om
(0.1 vs 0.12); obala svuda ≥ 50 m od prstena (min koast W/E/N/S 272/252 vs prsten 181).

## 8. Prstenovi kvartova, imena, kraj ostrva (2026-08-19, posle Play feedback-a)

Korisnik posle prvog Play-a: „luka je isečena umesto samo da bude na kraju ostrva… napravio si
samo jedan suburb, hoću ih na ovu veličinu mape 10… svaki kvart/suburb mora da ima svoje ime…
aerodrom isto makni na kraj ostrva… brodovi za luku treba da se spawn na kraju mape."

**Autoputevi su izbačeni za sad**: `beltFreeway = false` i `Highway` seam izvađen iz
`RoadDemoBuilder.seams` (gap 12 je sad običan blok). Belt/Interchange kod ostaje netaknut —
vrati seam ili upali prekidač i sve se opet gradi.

**Raspored (`CityLayout`):**
- Suburbi u **3 PRSTENA** (`NearStrip 110`, `RingStep 330`), mali (3–4 × 3 blokova),
  `suburbsMin/Max = 10/12`; realno ispadne 8–11 po seed-u.
- Prstenovi se popunjavaju **spolja ka gradu** (dalji prvi): daljem kvartu pristupna ulica mora da
  prođe između bližih, pa dalji prvi biraju svoje trake. Na kraju `Tighten()` svakog vuče na
  najbliži prsten u koji još staje, da ne visi 800 m napolju bez razloga.
- Zauzeće je **2D**: telo kvarta + koridor pristupne ulice (`Held.Road`). Telo od tela drži 45 m,
  telo od tuđeg puta 12 m. Zato luka ide u **ćošak** obale (njen koridor ide kroz celu dubinu
  ostrva pa bi po sredini presekao tu obalu na dva dela).
- Pin linije: luka/aerodrom traže običnu ulicu (bez bulevara i seam-ova); **suburb sme na bilo
  koju osim ivične** — obala ovog grida ima svega 2–3 „obične" linije, pa je grad inače dobijao
  tri sela. Reka je i dalje zabrana (provera raspona sa `RiverGap`).
- Imena: `DistrictSlot.name` iz pula (`CityLayout.Names`); mapa štampa ime kvarta (tip se skraćuje
  na širinu kvarta), a `RoadDemoBuilder` grupiše korene kvarta pod objekat s tim imenom.

**Kraj ostrva:**
- `DistrictSlot.toCoast` + `RoadDemoBuilder.PushToCoast`: posle `Plan()` (kad se zna stvarna dubina
  kvarta) strip se preračuna tako da lukin **kej padne na obalu**, a aerodromu daleki prag; okvir
  se samo klizne napolje (raspored kvarta ne zavisi od okvira).
- **Obala se meri od GRIDA, ne od kvarta** (`RoadDemoBuilder.Island.OutsideGrid` → `toSea`). Ranije
  se merila od najbližeg tla, pa je svaki kvart oko sebe „izvlačio" još kilometar ostrva — zbog toga
  je luka stajala u sred kopna i njen bazen je morao da se probija kanalom do mora (rupa na mapi).
  Sad kvart TROŠI obalu; unutar `Beach = 70 m` od svog tla nikad ne potone.
- Ako je obala uža nego što polje traži, ostrvo se na toj obali proširi
  (`islandNorth/South ≥ AirportDepth + SuburbRing + CoastMargin`), da ispred aerodroma ostane red sela.

**Brodovi:** `HarborDistrict.seaRun` (domaćin ga postavi na razdaljinu do kraja mape) — brod se
pojavi na jednom kraju mape i nestane na drugom; trasa je rezervisana kao voda (`Sea(lane,
mayOpen: false)` — taj pravougaonik se NE gura ka moru kao bazen, inače bi odneo celu obalu),
`Stay()` i razmak prolaznika se skaliraju sa dužinom trase. Život suburba se skalira na veličinu
kvarta (`ForCity`) — 10 sela po starim brojkama je bilo 1500 agenata.

**Provereno:** Roslyn kompajl (4a) + konzolna simulacija `CityLayout.Roll` po seed-ovima (bez
preklapanja, 8–11 suburba). **Play neproveren.**

## 9. Autoputevi isključeni master-prekidačem, minimum sela, imenovani kvartovi grada (2026-08-19)

Korisnik: „autoputevi su tu i dalje i idu preko suburbana, dodaj minimum 6 suburbana da ima na
mapi, takođe kvartovi u gradu isto moraju da imaju imena, sam odredi koliko blokova je jedan kvart."

**`freeways` (master prekidač, default OFF)** — `RoadDemoBuilder.NoFreeways()` se zove PRVI, pre
`Respace()`: izbaci svaki `SeamKind.Highway` iz `seams` i obori `beltFreeway`. Znači ni seam
ostavljen u inspektoru ni serijalizovan u staroj sceni ne može da vrati kolovoz kroz predgrađa;
u konzoli piše `[RoadDemo] freeways off: N Highway seam(s) dropped`. Upali `freeways` i sve se
vrati (Belt/Interchange kod je netaknut).

**Minimum sela** — `CityLayout.Squeezes`: tri nivoa pakovanja. Nivo 0 je **širokogrud** (razmak
60 m, tuđi put 28 m od placa, sela 4×3 i 3×3 bloka) — to je i odgovor na „putevi idu preko
suburbana", jer je pristupna ulica ranije prolazila 12 m od ograde. Ako grad time ne stigne do
`suburbsMin`, idu nivoi 1 i 2 (uži razmaci, manja sela, više koraka po prstenu) i popunjavaju
rupe koje je prvi prolaz ostavio. `CityLayout.MinSuburbs = 6` je pod: builder loguje
`[RoadDemo] quarters standing: N (M suburbs…)` i upozorava ako ih je manje.
Sim: normalno ostrvo 9–11 sela, ostrvo od 900 m obale na sve četiri strane i dalje 6–7.

**Kvartovi grada (`RoadDemoBuilder.Quarters.cs`)** — unutar grida se ništa ne gradi, samo se
imenuje: kvart = **3 bloka široko × 2 duboko** (`quarterBlocksAcross/Deep`, ~300 × 170 m), nikad
preko šava (reka/park/wild prekidaju niz — komšiluk se završava na vodi), ostatak od jednog bloka
se pripaja prethodnoj grupi. Na ovom gridu ispadne **12 kvartova** (4 kolone × 3 reda). Imena iz
`StreetNames.Quarter(i)` (Riverside, The Flats, Bricktown, Cannery Row…, bez ponavljanja, po
seed-u grada), a onaj nad sredinom grida se uvek zove **Downtown**. Mapa ih štampa manjim slovima
od kvartova van grada i gasi ih kad se dovoljno zumira (`QuarterType`, `QuarterMaxPx`).
`CityQuarters` / `QuarterAt(x,z)` su javni — ledger/teritorije će ih koristiti.
