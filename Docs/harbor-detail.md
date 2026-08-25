# Luka: slojevi detalja

> Stanje: 2026-08-25. Šta luka ima *osim* keja, magacina i kontejnerskih blokova, i gde
> svaki sloj živi. Sve gradi isti `HarborDistrict` — i u `Assets/Scenes/HarborDemo.unity`
> (host `StandaloneDistrictHost`) i na obali grada u `Game.unity`, pa je ono što se ovde
> promeni ono što grad dobije.

## Fajlovi

| sloj | fajl | šta pravi |
|---|---|---|
| tipovi vezova | `HarborDistrict.Berths.cs` | koji vez šta radi + sav dekor koji iz toga sledi |
| linija vode | `HarborDistrict.Waterline.cs` | fenderi, merdevine, koluti, konopi, ulje, galebovi |
| pogon luke | `HarborDistrict.Works.cs` | kapetanija, kapija (rampa, vaga, carina, lay-by), rezervoari |
| krijumčarenje | `HarborDistrict.Contraband.cs` | čuvani kontejner, rupa u žici, magacin u zakup |
| rutina | `HarborDistrict.Routine.cs` | pauze, rampe, prašina, varilac, noćna svetla |
| konopi za privez | `HarborMooring.cs` | 4 užeta po brodu, vezuju se/otpuštaju sa brodom |
| rampa na kapiji | `HarborBoom.cs` | stub + trakasta grana koja se diže |
| lanac istovara | `HarborDevanning.cs` | palete pored kutije koju je kran upravo spustio |

## Tipovi vezova

`PlanBerthKinds()` kotrlja iz seed-a. Uvek bar jedan **Container** vez (kranovi, live red i
stojeći blokovi su kičma luke); ostali mogu biti **Bulk**, **RoRo** ili **Fishing**, svaki
najviše jednom po luci i uvek na *krajevima* keja. Isključivo preko `mixedBerths`.

Tip vezа menja tačno četiri stvari:

1. iza vezа koji ne radi kutije **ne polažu se stojeći blokovi** (`YardClosed`);
2. samo Container vez dobija **portalni kran, live red i `HarborCargo`**;
3. vez koji ne radi kutije dobija **kaster** umesto teretnjaka (`AddBerth(..., small)`);
4. `BuildBerthWorks()` oblači apron iza njega:
   - **Bulk** — tri gomile (proceduralna mreža, pesak/ugalj), traka na nogama od keja
     preko gomila, burad, cevi, konusi; prašina dok brod radi;
   - **RoRo** — redovi uvoznih kola iz `HarborKit.ScanCars()` (bez policijskih i bez
     `VehicleCatalog.Barred`), pregrade i konusi; budžet 54 karoserije;
   - **Fishing** — čamci uz zid, gajbe/burad/konopi/pribor na kejskoj ivici, ledara sa
     tablom, i najviše galebova u luci.

## Linija vode

Fenderi (lopta-boja na lancu) po celoj dužini, gušće uz vezove; merdevine niz zid na
svakom vezu i na svakih 60 m; kolut za spasavanje uz svaku lampu; kotur konopa uz bitve;
ulje i lokve tamo gde ih luka i pravi (šine kranova, ušća prolaza, uz vrata magacina);
galebovi nad vodom. **Habanje zida** (`WearAt`) je gusto uz vez, retko između vezova.

Konopi za privez idu preko `HarborShipping.MakeFast` → `HarborMooring.Make` (deca broda,
pa jašu njegov `HarborBob`) i brišu se u `CastOff` zajedno sa mostićem.

## Pogon

- **Kapetanija** — u najširem procepu keja koji nijedan vez ne traži (`FindOfficeSpan`
  / `BerthReach`): pored krajeva keja, to je najčešće razmak između dva vezа. Meri se
  *noga* portalnog krana na kraju hoda, ne šina — šina je traka od 12 cm. Jarbol,
  antena i tanjir; ima ime, pa mapa može da joj stavi karticu. Ako je kej pun, luka
  ostaje bez kancelarije i log to kaže.
  Kapijski koridor se ovde **ne** računa: on čuva liniju magacina i kapijski put, a
  oboje počinju tek od dvorišnog puta (z 54) — na apronu uz kej to je običan beton.
  Isto važi i za dekor vezova.
- **Kapija** — po jedna rampa nad svakom trakom oba kapijska puta (`HarborBoom`, gradi se
  od rastegnutih `SM_Bld_Base_Pillar_01`, jer nijedan paket nema rampu); kamera na stubu.
  Na zapadnoj kapiji još i **vaga** u *ulaznoj* traci (traci kojom kamioni stvarno voze),
  **carinarnica** preko puta i **lay-by** sa dva kamiona u redu.
- **Rezervoari** — u najširem back-lotu, jednom po luci: dva tanka (uvećano bure), bund
  zid, cevovod na stočićima i dizel pumpa; para nad ventilom.

## Rutina

- **Pauza** — sanduci uz burad sa vatrom su prijavljeni kao *sedišta* (`OfferSeat`);
  `TickBreaks` svakih 20–45 s pošalje najbližeg dokera koji nije zauzet da sedne 25–65 s.
- **Lanac** — kran spusti kutiju → `HarborCargo.Landed` → `HarborDevanning` premesti
  palete *pored te kutije* i pomeri viljuškaru čvor 0 → viljuškar odnese paletu do vrata
  magacina → gomila kod vrata raste → kamion je odveze. Viljuškar stoji dok kutije nema.
- **Noć** — pier lampa je dodata u `DemoStreetLamps.LampKinds` (do sada je ceo kej stajao
  mračan dok je ulica gorela); reflektori pod granom svakog krana + po jedan nad kapijom,
  gase se/pale po `DemoSky.Nightness`. Varilac na platou pred zadnjim magacinom blješti
  po ciklusu.

## Šta luka nema (i zašto)

- **Železnički krak** — izbačen iz plana svesno.
- **Pas na lancu** — nijedan Synty paket u projektu nema model psa.
- **Ro-ro rampa (linkspan)** — brodovi su kitbash bez krmenih vrata, pa bi rampa vodila
  u zid; vez je zato *parking uvoza*, ne pretakalište.

## Zatečeno, nepopravljeno

- `TuneSurf` (`HarborDistrict.Ground.cs`) zove `GetVector("_Shore_Foam_Noise_Scale")` na
  svojstvu koje u `Synty/Water` **nije** vektor, pa svaka izgradnja luke ispiše dve
  crvene greške u konzoli. Bezopasno (podešavanje pene za taj parametar tiho otpada),
  postoji od ranije, nije dirano.
- `[Harbor] the sheds want the street at z 135, further out than the 120 m…` — dubina
  linije magacina zavisi od toga šta kit izvuče; upozorenje je iz ranijeg koda.

## Provere

Kompajl: `unity command recompile && unity command recompile_status --json`.
Mere prefaba: `Tools/City/Probe Harbor Kit` → `Logs/harbor-kit-probe.txt` (lista je
dopunjena svim novim komadima). Play: `unity command gangsters_play --scene
Assets/Scenes/HarborDemo.unity`.
