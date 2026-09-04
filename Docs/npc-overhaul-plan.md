# Plan: grad koji živi — NPC overhaul (ljudi, rutine, parkirana kola)

**EPIC 39 — GAN-354** (tiketi NPC-001…020, GAN-356…380).
Stanje na dan 2026-09-04, posle odluka korisnika (§11), samopregleda (§13) i **nezavisnog contrarian pregleda (§14), koji je oborio tri nosece pretpostavke.** Pokriva zašto grad deluje prazno,
šta od mašinerije već postoji, model po kom ljudi i kola dobijaju razlog da budu tamo gde
jesu, katalog ideja, faze, presudu i pitanja. Građa perioda: `Docs/1987-period-reference.md`.

Sve brojke u §0 i §1 su **izmerene** u kodu 2026-09-04 (fajl:linija ili komanda u
zagradi). Sve brojke u §4, §5 i §7 su **moje polazne vrednosti** — nijedna nije pravilo dok
je korisnik ne potvrdi u §11.

---

## 0. NPC-001 popis — kapija (izmereno 2026-09-04)

`gangsters_people_census [--seed 1987] [--rows]` je read-only editor komanda koja radi na
glavnoj niti sa zaustavljenim editorom. Pravi plan grada i privremenu preview scenu samo za
merenje, ne postavlja objekte u otvorenu scenu i ništa ne peče. Kanonski poziv je
`powershell -ExecutionPolicy Bypass -File Tools/play/census.ps1 -Rows`: skripta odmah predaje
zadržani detached job i atomski upisuje poseban receipt
`Temp/play/people-census/<job-id>.job`, bez brisanja ranijih ID-eva. ID i `-Resume` komandu
odmah piše na stderr, pa i greška diska ostavlja recovery handle; stdout je samo jedan JSON
dokument, odnosno samo goli ID uz `-DetachOnly`. `-Resume` pre upisa proverava da ID pripada
baš `gangsters_people_census`, a završen rezultat i da je za seed 1987. Pipeline čuva poslednjih
100 završenih poslova jedan sat; kompilacija/domain reload prekida red i tada stari ID namerno
prijavi neuspeh.
Poslednji puni prolaz je vratio JSON `passed = true`, bez gate grešaka, za **1,65 s** rada
(zahtev: manje od 30 s); `-Rows` vraća i svaki pojedinačni izvor/projekciju.

Adversarial proba je stavila `-Rows` iza 35 main-thread census poslova. Predaja je vratila i
zapisala ID za 0,24 s; čekanje od dve sekunde završilo je kodom 6 dok je posao bio `queued` i
dalo tačnu `-Resume` komandu. Ponovno spajanje na isti ID je posle ukupno 63,01 s vratilo
`completed`, `passed = true`, svih nula gate grešaka i pune retke. Dakle red duži od uobičajenog
30-sekundnog transportnog roka više ne može pretvoriti zdrav census u izgubljen rezultat.

Ovo je release kapija, ne seed sweep: svaki seed osim 1987 odbija se **pre planiranja** (proba
`--seed 4`: 0,55 s, editor je odmah zatim vratio `ready`). Unutrašnji radni rok je 25 s od
početka izvršavanja, sa proverama između dugih grupa skenova. Red čekanja i prenos namerno su
odvojeni od tog roka: detached job preživi oba i ostaje dostupan preko svog ID-a. Pored zbirnih
brojki, `passed` vezuje FNV-1a manifeste za identitet i ishod svakog poslovnog,
downtown i stambenog ulaza, kao i svaki legalni interval svake strane puta. Zato zamena jednog
uspešnog i jednog neuspešnog ulaza više ne može sačuvati isti zbir i lažno proći.

### 0.1 Vrata na seedu 1987

| izvor | kandidati | fizička vrata | na `PedLink` ≤ 18 m | neuspeh |
|---|---:|---:|---:|---:|
| upotrebljivi poslovni sajtovi (`Approach`) | 3.564 | 3.564 | 3.209 | 355 |
| direktni moduli `block-01…16` | 141 | 135 | 121 | 20 |
| ulazi `ApartmentBuildings` | 425 | 425 | 425 | 0 |

Svih 355 poslovnih neuspeha nema nezebrin `PedLink` u 18 m. U centru 14 fizičkih vrata
nema takvu vezu, a šest `SM_Bld_Shop_05` modula su samo prozor (`Leaves = 0`) i nemaju
fizička vrata.

**Odgovor na K1/C4:** centar nije nula i faza 2 se ne prekraja u „bez centra", ali se vrata
ne smeju izvoditi kao zamišljeni lobi po zgradi. Autoritativni direktni prefab izvori daju
**92 `SM_Bld_Shop*` modula + 49 `SM_Bld_Apartment_Door*` modula**; nema nijednog
`SM_Bld_Wall_Window*`. Jedanaest `SM_Bld_Shop_Cover*` dekoracija je namerno isključeno.
Preliminarni pregled 84/46 bio je pogrešan. Tačno prazni blokovi su **02, 03, 08, 14, 15,
16**; blok 07 ima jedan shop i devet stambenih ulaza, svih deset uspešno naleže na graf.

### 0.2 Legalni ivičnjak

| mera | rezultat |
|---|---:|
| strane sa parking pojasom | 978 |
| sirova dužina | 60.626 m |
| 7 m od krajeva/raskrsnica | −13.692 m |
| dodatno za zebre + 3 m | 0 m (već unutar prethodnog isključenja) |
| 17 hidranata, poluprečnik 4,6 m | −86,305 m dodatno |
| autobuska stajališta u autorstvu | 0 |
| 3 prilaza javnim parkinzima, otvor + 2 m | −33 m dodatno |
| prilazi pumpama | 0 (seed 1987 nije smestio pogodnu pumpu) |
| 162 ulaza stambenih dvorišta | −641 m dodatno |
| legalna dužina | **46.173,695 m** |
| slotovi na koraku 6 m | **7.168** |
| cilj na 60 % | **4.301** |

Četiri prepoznata hidranta nisu mogla da se upare sa parking stranom u 10 m i ostaju izričito
prijavljena; nema tihog footprint fallbacka. Sva tri parking prilaza i sva 162 dvorišna ulaza
upareni su sa parking stranom. `fuelStations = 5` je maksimum, ne obećan broj: puni plan na
seedu 1987 nema parcelu koja prima ceo `FuelStationBlock`, pa je izmereno nula prilaza pumpi.
Generisani residential dressing je isključen; komanda bi pala umesto da prećuti njegova
proceduralna autobuska stajališta ako se taj prekidač uključi.

### 0.3 Današnji trošak gomile

Medijana pet uzastopnih prolaza; svaki ima 8 zagrevnih + 64 merena frejma, Unity 6000.5.6f1,
AMD Ryzen 9 9900X3D. Brojke čitaju iste `TickTimer` sekcije 3 (`civilians`) i 4 (`crowd`) kao
živi update.

| tela | civilians ms/frejm | crowd ms/frejm | ukupno ms/frejm |
|---:|---:|---:|---:|
| 100 | 0,1374 | 0,0045 | 0,1420 |
| 240 | 0,3448 | 0,0132 | 0,3581 |
| 480 | 0,7126 | 0,0312 | 0,7451 |

Današnja brzina pešaka (1,5 m/s) na satu gde sat igre traje 60 realnih sekundi znači
**90 m po satu igre**. To je činjenica koju D20 i faza odlazaka moraju rešiti, ne nova
pretpostavka o dometu.

### 0.4 Registar, proba gustoće i stanje kapije

`gangsters_business_audit --seed 1987 --json` razrešava protivrečnost 631 / 3.263:
autoritativno je **3.581 sajt, 3.564 upotrebljiva i popunjena**, uz 17 namerno nepodržanih
(16 pečenih downtown blokova + policijska stanica). Upotrebljivi izvori su residential 3.531,
venue 32 i compound 1; audit nema grešaka.

`MiniCoreDemo` je za najjeftiniji vizuelni test podešen na **400 pešaka / 120 kola**.
`Tools/play/night.sh` za nenavedene vrednosti izričito vraća istorijski regresijski baseline
na 100/40, pa vizuelna proba ne menja postojeće smoke/night scenarije; eksplicitni `--sets`
i dalje ima prednost. Automatski harness se nad korisnikovom otvorenom scenom ne pokreće.
**Kapija još nije otvorena:** čeka se korisnikova presuda da li ova gustina već deluje živo, a zatim trenutni
30-run soak baseline pre prve promene ponašanja. Nijedan kasniji NPC tiket ne počinje pre oba
ishoda.

### Ugovor (šta je traženo)

1. **Grad deluje prazno.** Ljudi i kola treba da ga napune tako da oko poveruje.
2. **NPC kola parkirana po ivičnjacima**, po celom gradu, ne samo na parkinzima.
3. **NPC u rutinama koje imaju smisla** — ne nasumično lutanje po trotoaru „ko sad".
4. Korisnikova presuda nad postojećim slojem: *„taj ambijentalni ne izgleda realno
   uopšte"* — posađene žive slike (par koji priča u dvorištu, čovek na klupi zauvek) su
   lutke, ne stanovnici.
5. „Go wild na ideje" — katalog je u §5; šta od njega ulazi u posao odlučuje korisnik (§11).
6. **Svedoci** (korisnik, 2026-09-04): *„treba takođe da budu svedoci ako se zadese. Ako
   postanu svedoci moramo da znamo gde žive i treba tuda da se spawnuju."* Ko se zatekne
   kraj pucnjave je svedok; od tog časa je **osoba sa adresom**: zna se gde stanuje, i
   sledeći put izlazi **na svoja kućna vrata**, ne iz nasumičnih.

---

## 1. Šta već postoji (izmereno 2026-09-04)

### 1.1 Gomila u CoreDemo / MiniCoreDemo

- **100 pešaka** ostaje polazni `CoreDemo` broj (`CoreDemoBuilder.cs:32`); `MiniCoreDemo` je
  za NPC-001 vizuelnu probu privremeno podignut na **400** (`MiniCoreDemo.unity`). Nema
  skaliranja na veličinu grada (`scaleLifeToCity = false`, `CoreDemoBuilder.cs:252`). Deljeni su
  **jednoliko po deonici trotoara** (`RoadDemoBuilder.cs:3396-3418`): ćošak od 5 m i
  bulevar od 90 m imaju istu šansu da dobiju čoveka. Nema mere „ljudi po metru", nema
  razlike centar/rub/park.
- **Ne idu nikud.** `PedestrianAgent.ChooseLink` (`:2241`) bira sledeću deonicu nasumično
  (zebra ×0.35, nazad ×0.15). Nema cilja, nema putanje. A* koji imaju krewovi i policija
  (`WalkRoute.cs`) gomila ne koristi — po sopstvenom komentaru: *„The crowd walks the
  sidewalk graph… The outfit does not."*
- **Sat im ne postoji.** `CivilianAgent`, `CityLife`, `SpawnPedestrians`: nula referenci na
  `DayClock`. Ista gomila u 03:00 i u 13:00.
- **U Core scenama vrata i klupe ne postoje za gomilu.** `_life.Doors.Count == 0`; Core
  gradnja preskače `BuildBlocks/DressStreets` koji ih pune; `insideAtStart = 0` uz komentar
  *„Core does not publish building doors yet"* (`CoreDemoBuilder.cs:243`). Od 15 stanja
  `CivilianAgent.Mode` (`:37-42`) sedam je mrtav kod u sceni koju gledamo: `ToBench, SitDown,
  Sitting, StandUp, FromBench, ToDoor, Inside/WalkOut`. Ostaje: lutanje + ćaskanje u
  prolazu + strah (Startle/Flee/Cower/Gawk) + smrt.
- **Ulazak u zgradu je `SetActive(false)`**, a izlazak iz **nasumičnih slobodnih vrata bilo
  gde u gradu** (`CityLife.PickFreeDoor`). Pravi, animirani ulazak sa otvaranjem krila i
  pragom (`DoorBeat`, 3.5 s unutra, faze Approaching…Closing) koriste samo krew i policija.
- **Nema odjave ni dopune.** Živ civil se nikad ne gasi, ubijeni se ne zamenjuje; populacija
  je fiksna od gradnje do gašenja scene.
- **Nema budžeta.** Svih N agenata svaki frejm (`RoadDemoBuilder.cs:786`); jedina brana je
  `PedDetail.Radius = 60 m` (fine animacije) i cull renderera na 330 m (`ScenePerf.cs:29`).
- **Deca hodaju u gomili sama** — `CrowdLooks.Children` se čita samo za dosijee, ne za filter.
- **Svedoci:** `SnapshotWitnesses` (25 m, najviše 3) radi nad ovom gomilom; svedok ima ime
  izvedeno iz broja (`PersonName`), ne adresu ni posao.

### 1.2 Ambijentalni sloj u stambenim blokovima (ono što „ne izgleda realno")

`ResidentialAmbientPeople.cs` (1.106 linija) + `ResidentialBlockLife.cs` (1.565): po seedu
bloka posadi 5–10 tela (`TargetPeople`, `:261`) u dvorišta, kafiće i terene — rutine
`Pose, Shuttle, Door, Trash, Activity, Conversation, Seated, SeatedConversation`.
**Nisu agenti**: ne stoje na grafu trotoara, ne dolaze ni odlaze, ne čuju pucanj kao gomila.
Ono što u tom sloju VREDI i što se zadržava kao podatak: merena sedišta (`SeatAnchor`,
restoran ≤ 6, klupa ≤ 2, kafe ≤ 2), tačke kanti (`BinAnchors`), strana vrata lokala
(`VenueDoorSide`), i klip-set (sedanje 0.428 m / 1.2 s / 1.1 s, deljen i sa dokerima).

### 1.3 Saobraćaj i parkiranje

- **40 kola** ostaje polazni `CoreDemo` broj; `MiniCoreDemo` je za NPC-001 probu podignut na
  **120**. Jedan spawner (`RoadDemoBuilder.SpawnCars`, `:3821-3884`) daje po jedno kolo na 18 m trake,
  round-robin preko izmešanih traka dok se ne dosegne broj. Bez dopune: zaglavljeno kolo
  posle 60 s nestane (`RoadCar.Vanish`) i flota se samo smanjuje.
- **Civilna kola nemaju cilj.** `DriverProfile.Traffic` (`DriverProfile.cs:112`): `Wanders`,
  `Cruise 10 m/s`, `PassesAtKerb`, preko krune do 1 m, nikad u suprotnu traku. `GoTo` nikad
  ne zovu. **Nikad ne parkiraju.**
- **Mehanika parkiranja uz ivičnjak je KOMPLETNA i radi** — samo je civili ne koriste:
  * `CrewCars.KerbSlotNear` (`CrewCars.cs:140-240`): traži 60 m u oba smera, korak 3 m,
    **7 m od kraja deonice**, `ParkingA/B` (false samo na jednosmernim), `PullOutRoom 4 m`,
    zauzeće iz `LaneNet`. **Ne gleda ništa drugo**: ni hidrant, ni prilaz, ni stanicu, ni zebru.
  * `RoadCar.PullIn/PullOut` (`RoadCar.Parking.cs`): pravi ulazak/izlazak sa rupom
    `2·HalfLen + 1.6 m`, ivice 4 m, odbija cilj dalji od 45 m; strpljenje 6 s, odustajanje
    20 s; parkiranje odustaje posle 25 s.
  * `StoodCar.Park` + `LaneNet.AddStatic`: parkirano kolo je statični zauzimač koji saobraćaj
    obilazi; ako preklapa traku, računa se kao olupina (`Parked = false`).
  * `KerbCars.Park(spans)` (`KerbCars.cs`): red parkiranih kola sa razmakom 1.4 m —
    **samo u lab scenama** (CoverDemo 12, CrewDemo 6), spanove „legalnog ivičnjaka" piše
    graditelj rukom. **U gradu nema pozivaoca.**
  * **`ParkLane = 2.5 m`** pojas uz ivičnjak postoji na SVAKOJ ulici (`RoadDemoBuilder.cs:313`,
    `StreetKit.cs:26`): parkirano kolo je van vozne trake.
  * `StaticRoadUser` (`LaneNet.cs:1251`) — napisan, nula pozivalaca.
- **Ko već parkira:** policija (od 2026-09-04 živi na ivičnjaku, farthest-point po
  blokovima, `policeRestAtKerbs`), krew kola, 3 parkinga × 5 „putnika" (`ParkingLot`: boks →
  vožnja 16–30 s → nazad u boks), do 5 pumpi × 3 mušterije (`FuelCustomer`: jedini pravi
  „odvezi se negde, parkiraj, izađi, plati, vrati se"; seed 1987 trenutno smesti nula
  pogodnih pumpi), motori (4 + 2 na stalku).
- **Zabeležena opasnost:** *„a parked patrol is a registered obstacle and the spread of them
  gridlocked the ambient traffic"* (`RoadDemoBuilder.cs:4492`) — to je bio stari raspored NA
  traci; današnji ide u pojas od 2.5 m. Svejedno se meri (§8).
- **Semafori:** svaki čvor, ciklus 26 s; kola i pešaci ga poštuju.
- **Bazen vozila:** 17 civilnih tela (11 PalmCity + 6 City), tri retka. **Neiskorišćena, a u
  projektu:** PolygonTown `Bus_01`, `SchoolBus_01`, `Firetruck_01`, `Truck_01`,
  `Truck_Delivery_01`, `Truck_Garbage_01`, `Convertable_01`; GangWarfare `Truck/Van/LowCar`;
  `Car_Ambo_01` (služba). Taksi tela (`Sedan_01_Preset_Taxi`, `Car_Taxi_01`) voze u
  saobraćaju bez ikakvog taksi ponašanja. Limuzina, kolica i „e-" vozila ostaju zabranjeni.
- **Jedina fiksna ruta u repou** je lučki kamion (`HarborTruck.Route`, polilinija). Autobuske
  stanice, poštanski sandučići, kiosci, govornice — sve **dekor bez runtime tipa**.

### 1.4 Sat i ko ga čita

- `CityClock` (`Assets/Scripts/Ambient/CityClock.cs`): u Core sceni **1 sat igre = 60 s**
  (`CoreDemoBuilder.cs:56-58`, dan = 24 min), start 06:00, lestvica brzine ×0.5…×16, pauza.
  Nema događaja „promenio se sat" — svi anketiraju.
- Već čitaju: nebo (izlazak 6, zalazak 20), lampe, farovi, prozori, **policijske smene
  07/19** (danju 50 % kola / 100 % pešaka, noću obrnuto), **novine 06:00**, **naplata
  reketa od 09:00 na dan bloka u nedelji** (FNV po id-u bloka), teritorijalni raspored
  (Business kanal na 4 h), zatvaranja radnji (Shut 3 dana / Ruined 7).
- **Ne čita niko od civila i kola.** `Campaign.DayOfWeek` postoji (naplata ga koristi) —
  nedelja kao dan još ništa ne znači na ulici.

### 1.5 Svet: radnje, kuće, mesta

- **Radnje:** 37 arhetipova (`BusinessArchetypes.cs:19-59`): 18 običnih prizemnih (grocer,
  butcher, baker, barber, tailor, laundry, pharmacy, hardware, bookshop, record shop,
  florist, newsstand, cobbler, locksmith, pawn, electrical, travel, betting), 5 imenovanih
  (pub, pizzeria, cafe, diner, restaurant), 5 zabave (nightclub, casino, hotel, gym,
  fairground), 2 forecourt (car yard, fuel), 5 industrije/luke. **Seed 1987: 3.581 sajt,
  3.564 upotrebljiva** (`gangsters_business_audit`, 2026-09-04). Svaki ima **`Approach`** (tačka
  gde čovek prilazi) i `ApproachOutward`; svaki ima **gazdu na papiru** (`BusinessOwner`: ime,
  starost Young/Middle/Old, seed portreta) — **bez tela u svetu**. Nema radnog vremena, nema
  osoblja, nema mušterija kao pojma. Stanje `Trading/Shut/Ruined`; izlog
  `Intact/Open/Smashed/Burning/Boarded`.
- **16 požnjevenih blokova centra (`block-01…16`) nemaju nijedan sajt** — „najveća rupa"
  inventara. Vrata za njih moraju geometrijski (`ShopDoors`/`FacadeFinder`/`StorefrontDoorCatalog`
  postoje), inače je centar bez radnji.
- **Kuće:** seed 1987 = **146 stambenih blokova, 3.913 izloga**; `ApartmentBuilding` ima
  adresu („318 CANAL HEIGHTS"), spratove, stanove — **bez tačke ulaza i bez stanara**.
- **Vrata na grafu:** `DemoDoor` (`CityLife.cs:8-46`) već nosi `SiteId`, `BlockId`, `Address`,
  `Building`, vezu na deonicu (`LinkFwd/Back`, `EntryT`) i `Busy`. `CoreOutfitDoors`
  (`RoadDemoBuilder.CoreFronts.cs:21`) već projektuje sajtove iz kataloga na graf — ali samo
  za izbor sedišta porodice, ne u `CityLife`.
- **Mesta koja postoje:** klupe u parku (`DemoBench`, `TryClaim/Release`), sobe keja
  (`Lawn, Plaza, Fountain, Terrace, Grove, Landing, Fair, Diner`), ulazi u podzemnu
  (`Plan.Subway`, civic, dekor), autobuske stanice/sandučići/kiosk/bilbord (`Stood`, dekor),
  pumpe sa mušterijama, sud, stanica, noćni klub (`nightclub-block`), gradska kuća.
- **U CityKit-u, a nigde u Core gradu:** `building-hospital`, `building-hotel-small`,
  `building-firestation`, `building-school`, `building-post`, `building-bank`,
  `building-casino`. Crkva (`SM_Bld_Church_01`) samo u SuburbDemo. Bioskop, motel,
  biblioteka — nema prefaba.

### 1.6 Uspavani sloj koji se preuzima (LivingCity, nije u Core sceni)

`PedestrianSchedule` (`Assets/Scripts/Entities/PedestrianSchedule.cs`, čist C#, 79 linija):
`DayPhase { AtHomeNight, MorningCommute, WorkDay, EveningCommute, EveningOut }`, prozor
putovanja 1.5 h, **lični pomak po hashu** („plima u minutima, nikad stampedo u jednom
frejmu"), `PhaseFor(hour, offset, worksDays, 8.5, 17.5, 22.5)`. `PedestrianInteractionDirector`:
kućna/radna vrata po hashu, vrata sortirana po položaju (isti seed = isti čovek na istim
vratima). Sve to **ne radi u planiranom gradu**: `GameplayBootstrap` za `RoadDemoBuilder`
instalira samo personnel/outfit/almanah i vraća se (`:44-54`).

### 1.7 Tela i animacije na raspolaganju

- **Gomila danas:** 30 tela posle filtera (bez gangsterskih, bez policije, bez kostima).
  **Neiskorišćene uloge:** PolygonTown `Father_01/02, Mother_01/02, Son, Daughter,
  SchoolBoy, SchoolGirl, ShopKeeper_01`; PolygonNightclubs `Bartender M/F, Bouncer,
  Party ×5`; GangWarfare `Cook M/F` (kuvar), `Hazmat`; Generic `Jumpsuit M/F` (radnik),
  `Street ×8`, `Business ×2`.
- **Animacije:** Mixamo (`Assets/Animations/People`): disanje, razgovor, vika, sedenje na
  klupi, ustajanje, hod, smrt. **Synty AnimationIdles** (~130 ponašanja × M/Ž): naslanjanje
  na zid (leđa/levo/desno, prekrštene ruke, noga uz zid), prekrštene ruke, ruke na kukovima,
  **gledanje na sat**, dosada (tapkanje, šutiranje zemlje), **piće** (drži/pijucka/gutljaj/
  ekser), **jelo** (drži/mali/veliki zalogaj), klimanje/odmahivanje, mahanje, pogled
  levo/desno/uplašen, **molitva** (stojeći/klečeći), **pijano njihanje**, istezanje, zevanje,
  zamišljen, preklinjanje, pokazivanje, držanje (agresivno/stidljivo/pogureno), telefon
  (drži **pametni telefon** — anahron, v. D10). Uz njih props: šolja, krofna, češalj.
  Synty BaseLocomotion: hod/trk/čučanj/strafe/prelazi M/Ž (CrewKit ih već koristi).
- **Arhitektura:** goli `PlayableGraph` sa 33 slota poze i dva zamenljiva porta
  (`PoseJoin/PoseAct`) — nova poza je klip u port, ne novi kontroler.

---

## 2. Dijagnoza (zašto grad deluje prazno)

1. **Nema uzroka ni posledice.** Čovek se pojavi ni od kud, luta, i nikad ne stigne. Kolo
   vozi u krug. Oko traži *odakle* i *kuda*; kad ih nema, čita statiste.
2. **Broj je pogrešna poluga.** 100 ljudi na 146 stambenih blokova + 3.564 radnje je pusto
   ma kako se delilo; 1.000 ljudi bez razloga bi bio *pun* grad koji i dalje deluje lažno
   (i skupo: svih N svaki frejm). Prazno nije mali broj — prazno je **isti broj svuda i
   uvek**.
3. **Ulica ne zna za sat.** Grad ima smene policije, novine u 06:00, naplatu od 09:00 —
   a pešaci i kola žive van vremena. Noć izgleda kao podne s ugašenim lampama.
4. **Ništa ne stoji uz ivičnjak.** Pojas od 2.5 m je na svakoj ulici prazan; jedina parkirana
   kola su rekviziti na parkinzima. Ulica bez parkiranih kola je ulica bez stanovnika.
5. **Ambijentalni ljudi su nameštaj.** Posađeni po seedu bez dolaska i odlaska; ne pripadaju
   ni gomili ni mestu.
6. **Radnja nema čoveka.** 3.564 gazde na papiru, nula na vratima. Reketaš ulazi u praznu
   radnju; policija dolazi jer je „a shopkeeper" telefonirao — a njega niko nije video.

---

## 3. Principi

1. **Svako je odnekud i ide nekud.** Ni jedno telo na ulici bez porekla (vrata, kolo,
   stepenice podzemne, autobus, rub vidljivog) i cilja (isto to). Idle poza je **stanica**
   u tom putu (čekanje autobusa = gleda na sat), nikad ukras.
2. **Stanje grada je funkcija sata, seeda i mesta** — ne simulacija svega. Ko je gde u 14:00
   na bloku X se *izračuna* kad kamera dođe; van prozora kamere se ne troši ni frejm. Isti
   seed = isti gazda na istim vratima svaki dan.
3. **Telo je budžet, čovek je podatak.** Grad ima hiljade ljudi na papiru (gazde, osoblje,
   stanari), a tela onoliko koliko frejm nosi — u prozoru kamere, kao što se već strimuju
   blokovi (`Docs/core-streaming.md`).
4. **Gustina po mestu i satu, ne po deonici.** Centar u 12:00, stambeni pojas u 19:00,
   klub u 01:00, park nedeljom. Krive su podatak koji korisnik menja, ne konstante.
5. **Jedna populacija, tri stanja.** Ista osoba je u zgradi, na nogama ili u kolima.
   Parkirano kolo je ulaz i izlaz; kolo u saobraćaju ima odredište; pešak koji stigne do kola
   nestaje s ulice tako što se odveze.
6. **Ulica reaguje na igru.** Pucnjava prazni blok satima; ugašena radnja nema gazdu na
   vratima; strah teritorije se vidi kao manje ljudi. Rutine su i deo reketa: gazda na vratima
   je onaj s kim reketaš priča, i svedok sa imenom i adresom.
7. **Centralni sistem, sve scene.** Novi sloj živi u `Assets/RoadDemo` + čist C# u
   `Assets/Scripts` (kao novine i poslovi); CoreDemo, MiniCoreDemo i demo scene prolaze kroz
   isti model. Tableau sloj se ne širi (korisnikova presuda).
8. **Presuda meri ono što broji** (pouka parka): nema „izgleda dobro" — ima brojač duhova,
   zaglavljenih, promašenih vrata i stopa nad 30 seedova/runova.

---

## 4. Model

### 4.1 Papirni ljudi (`LivingCity.People`, čist C#, deterministički po seedu)

Grad dobija **registar ljudi** izveden iz onoga što već postoji, bez novih podataka:

| ko | odakle | koliko (seed 1987, procena) |
|---|---|---|
| **gazda** | `BusinessOwner` svake radnje (ime, starost, portret) → telo iz `CrowdLooks` po seedu; radnje pod firmom (`CompanyOwned`) dobiju poslovođu | ~3.200 |
| **osoblje** | po veličini sajta: Small 0–1, Medium 1–2, Large 2–4, Compound 4–8 (polazno) | ~4.000 |
| **stanari** | po `ApartmentBuilding.Flats` × zauzetost; **ne nabrajaju se unapred** — domaćinstvo je brojka, čovek se izvuče po (zgrada, indeks, dan) kad zatreba telo | brojka |
| **prolaznici** | nemaju adresu u gradu; ulaze i izlaze na rubu vidljivog, autobusom, podzemnom | po krivama |

Čovek na papiru ima: ime (iz postojećih tabela), telo (deterministički), **kućna vrata**,
**radna vrata** (ako radi), **kolo ili ne** (deo stanara ima kolo → ono stoji na ivičnjaku
uz kuću noću, uz posao danju), i ličnu **plimu** (pomak u minutima, iz uspavanog
`PedestrianSchedule`). Nema simulacije van prozora — samo se izračuna gde bi bio.

**Prikivanje (svedok).** Telo koje se zatekne kraj pucnjave (današnji `SnapshotWitnesses`,
25 m) prestaje da bude „stanar broj k iz zgrade X koga niko ne pamti": zapis osobe se
**prikuje** — ime, telo, **kućna vrata** (i radna, ako ih ima) se upišu u `Witness` koji
već nadživljava telo (`WitnessWatch`). Od tada:
- **spawnuje se samo na svoja kućna vrata** (ili na radna u radno vreme, ili iz svog kola
  uz kuću), nikad iz nasumičnih vrata ni sa ruba;
- ista je osoba svaki dan (isto lice, ista adresa) dok se slučaj ne zatvori;
- porodica ga može **naći kod kuće** — postojeći `WitnessKilled`/`WitnessWithdrawn` u
  `LawWire` dobijaju mesto na mapi gde se to dešava, umesto apstraktne naredbe;
- prolaznik bez adrese (ušao autobusom/podzemnom/sa ruba) **ne može biti svedok** — ili
  dobija adresu u trenutku snimka (stan izvučen po hashu u najbližem stambenom bloku), ili
  se ne snima; polazno: dobija adresu (D19).

### 4.2 Vrata i portali

- **Core objavljuje vrata u `CityLife`** — upotrebljivi poslovni sajtovi sa `Approach`
  (3.564 kandidata; §0 meri koje projekcije stvarno naležu), plus **vrata direktnih izvornih
  modula u 16 blokova centra** (`StorefrontDoorCatalog` + `SM_Bld_Apartment_Door`), plus
  **ulazi stambenih zgrada** (jedna vrata po
  `ApartmentBuilding`, na strani ulice, izmereno iz jedinice — nedostajuća tačka koju
  `residential-building-generator-plan.md` §1.1 ionako uvodi kao „ulaz").
- **Portal** = mesto gde telo sme da nastane ili nestane u prozoru kamere: vrata, kolo
  (ulazak/izlazak), stepenice podzemne, vrata autobusa/taksija, i **rub iza cull daljine
  (330 m)**. Sve drugo je *duh* (§8, `Ghost`).
- Vrata čuvaju `Busy` (jedan na pragu) i dobijaju **stanje po satu**: otvoreno/zatvoreno
  iz radnog vremena arhetipa (§4.4), zatvoreno kad je radnja `Shut/Ruined`.

### 4.3 Rutina = niz odlazaka (errand)

Jedan odlazak: **poreklo (portal) → putanja po grafu trotoara → cilj (portal ili stanica)
→ boravak → sledeći**. Putanja je Dijkstra po grafu ćoškova (`PedNode/PedLink`, jeftino —
graf je po ćoškovima, ne po metru), poslednjih par metara do praga radi današnji `ToDoor`.
Stanice usput: klupa (`DemoBench`), stajalište (gleda na sat), kiosk (30 s), govornica,
terasa (sedište iz `SeatAnchor`), zid za naslanjanje.

Vrste odlazaka (polazni katalog, korisnik seče):

| vrsta | poreklo → cilj | kad | boravak |
|---|---|---|---|
| **na posao / s posla** | kuća ↔ radna vrata (peške, kolom, autobusom, podzemnom) | 07–09, 17–19 ± lična plima | ceo radni dan unutra |
| **otvaranje/zatvaranje radnje** | gazda: vrata → prag (2 min: gleda na sat, naslanja se) → unutra; u 18:00 obrnuto | radno vreme arhetipa | — |
| **kupovina** | kuća/kolo/rub → radnja po arhetipu → nazad | kriva po arhetipu (§4.5) | 2–20 min |
| **ručak** | radna vrata → diner/cafe/klupa u parku (jede, `Eat_*` + krofna/šolja) → nazad | 12–14 | 20 min |
| **veče u kafani** | kuća → pub/restaurant → kuća (pijano njihanje posle 23) | 19–01 | 45–90 min |
| **noćni klub** | taksi/kolo/peške → **red ispred kluba sa izbacivačem** → unutra → izlazak u talasima | 22–03 | 1–2 h |
| **park** | trkači (06–08, istezanje), klupe (popodne), porodice vikendom | po satu | 10–40 min |
| **kraj kola** | stanar → svoje kolo na ivičnjaku → odvezao se (kolo postaje saobraćaj sa ciljem) | plima | — |
| **hangout** | tinejdžeri na ćošku (naslanjanje), stariji ispred zgrade | 15:30–mrak | 20–60 min |
| **čekanje** | stajalište → autobus; taksi štand; govornica | linija | do vozila |

### 4.4 Radno vreme arhetipa (polazno, nedelja posebno — D9)

| grupa | otvara | zatvara | mušterije najviše |
|---|---|---|---|
| grocer, baker, butcher, pharmacy, newsstand | 07:00 | 19:00 | 08–10, 17–19 |
| barber, tailor, laundry, hardware, bookshop, record, florist, cobbler, locksmith, electrical, travel, pawn | 09:00 | 18:00 | 10–13, 15–17 |
| betting shop | 10:00 | 20:00 | 14–18 |
| cafe, diner | 06:00 | 22:00 (diner 24 h — D) | 07–09, 12–14 |
| pizzeria, restaurant, pub | 11:00 | 01:00 | 19–23 |
| nightclub, casino | 22:00 | 04:00 | 00–02 |
| gym | 06:00 | 22:00 | 07, 18 |
| hotel | 24 h | — | 15–17, 10–11 |
| fuel, car yard | 06:00 / 09:00 | 22:00 / 18:00 | ravno |

Naplata reketa (od 09:00) ostaje kakva je; s ovim radnim vremenom kolektor **zatiče gazdu**.

### 4.5 Plima dana (gustina po mestu × satu, polazne vrednosti = udeo od maksimuma)

| mesto | 00–05 | 05–08 | 08–12 | 12–17 | 17–22 | 22–24 |
|---|---|---|---|---|---|---|
| centar (radnje, kancelarije) | 0.05 | 0.3 | 1.0 | 0.9 | 0.5 | 0.15 |
| stambeni pojas | 0.05 | 0.6 | 0.4 | 0.5 | 1.0 | 0.3 |
| noćni pojas (klub, pub, kazino) | 0.9 | 0.1 | 0.1 | 0.3 | 0.7 | 1.0 |
| park, kej | 0.0 | 0.4 | 0.5 | 1.0 | 0.8 | 0.1 |
| industrija, luka | 0.2 | 0.8 | 1.0 | 1.0 | 0.4 | 0.2 |

Maksimum je **budžet tela** u prozoru (D1). Kriva se množi sa **strahom bloka**
(`TerritoryFear`) i sa **tišinom posle pucnjave** (§4.8). Nedelja: svoja kolona (D9).

### 4.6 Parkirana kola: legalni ivičnjak i plima parkiranja

- **Legalni ivičnjak kao podatak** (ono što `vehicle-movement-plan.md` zove `ParkingStrip` i
  nikad nije napravljeno): za svaku deonicu sa `ParkingA/B`, spanovi `s0..s1` posle
  isključenja: **7 m od raskrsnice** (današnji broj), zebra + 3 m, **hidrant ± 4.6 m** (15 ft,
  pravilo koje je 1987 stvarno važilo), **autobusko stajalište 12 m**, prilaz parkinga/pumpe
  + 2 m, ulaz stambenog dvorišta. Hidranti i stajališta već stoje na trotoaru — samo se
  pročitaju iz `SidewalkPlan`.
- **Slot** = mesto za kolo (dužina tela + 1.4 m, `KerbCars.Gap`) unutar spana; skup slotova
  po gradu je deterministički po seedu.
- **Plima parkiranja:** svaki slot ima **raspored po hashu** (od kad do kad je pun), izveden
  iz klase ulice i sata — polazno: stambena ulica 80 % noću / 45 % danju, centar 25 % noću /
  70 % danju, noćni pojas obrnuto od centra. Van prozora kamere je to samo podatak. **U
  prozoru** se prelaz odigra kolima: u času kad slot „postaje pun", kolo iz saobraćaja (ili
  novo iza cull ruba) dobija `GoTo(slot, park:true)` i **stvarno se parkira**; u času kad se
  prazni, kolo **izlazi** (`PullOut`) i vozi do cilja ili iza ruba. Vozač izlazi/ulazi kao
  telo (portal). Time saobraćaj dobija odredišta bez posebnog sistema odredišta.
- **Registracija:** parkirano kolo = `StoodCar` (statični zauzimač u `LaneNet`, prepreka za
  pešake van grafa), telo se **strimuje sa blokom** (kao geometrija), zapis o zauzeću ostaje.
  Kad slot ne može (pun, zauzet olupinom): pravilo iz plana vozila — saobraćaj traži sledeći
  slot u istoj ulici, pa obilazi blok (jednom), pa odustaje iza ruba. **Bez gridloka** (§8).
- **Bazen:** 17 civilnih tela + `Convertable_01` (kabriolet — 1987 Majami) + Town `Pickup_01`;
  uz kuće više pikapa/kombija, u centru više sedana; `VehiclePaint` kao i danas.

### 4.7 Službena vozila sa rutama (katalog, korisnik bira — D4)

Zajednička mašina: **ruta** = lanac čvorova (kao `HarborTruck.Route`, ali po `LaneNet`
Dijkstrom kao policija), sa **stajanjima** (dwell) i **portalima** na stajanjima.

| vozilo | telo (postoji) | ruta / okidač | šta se vidi |
|---|---|---|---|
| **gradski autobus** | Town `Bus_01` | 1–2 linije bulevarima, na 12 min igre, stoji 20 s | ljudi čekaju (gledaju na sat), ulaze, izlaze; stajališta **na liniji** (D5) |
| **kamion za smeće** | Town `Truck_Garbage_01` | 04:30–06:30 svaka ulica; staje uz kante | dva radnika (Jumpsuit) skoče, kanta, nazad; jedini život u 05:00 |
| **dostava** | Town `Truck_Delivery_01`, Van | 06–10 do grocer/baker/pub; **dupli parking** 40 s | vozač ulazi u radnju; saobraćaj obilazi |
| **taksi** | dva postojeća taksi tela | krstari; **štandovi** kod kluba/hotela/keja; poziv noću | mušterija ulazi/izlazi (portal); posle 01:00 pola saobraćaja su taksiji |
| **hitna / mrtvačka** | `Car_Ambo_01` | `StreetAlarm.OnDeath` posle policije | odvozi telo (danas se telo posle 5 s „kredom" briše — sad ga neko odnese) |
| **vatrogasci** | Town `Firetruck_01` | izlog `Burning` (paljenje je već mehanika reketa) | dolaze sirenom, stoje uz radnju, gase → `Boarded`; treba stanica ili rub (D7) |
| **novinski kombi** | Van | 05:30, obilazi kioske i sandučiće | veza sa City Wire u 06:00, čist ukus perioda |
| **školski autobus** | Town `SchoolBus_01` | 07:30 / 15:00 | traži školu u Core (D7) i decu (D6) |
| **radovi** | `Pickup_01_Preset_Construction` | jedan blok dnevno, kupa | ne — bez čunjeva/rupa u paketu; ostaje ideja |

Nema u paketu: šlep (kad bi bio — odvozio bi olupine), sladoled. **Limuzina ostaje zabranjena.**

### 4.8 Ulica reaguje na igru

- **Pucnjava:** blok i susedi imaju **tišinu** — sat igre bez novih odlazaka (polazno 2 h),
  radnje u vidokrugu zatvaraju ranije; postojeći `Gawk` ostaje. Posle: ambulanta, policija,
  novine — sve već postoji, dobija telo koje odnosi mrtvog.
- **Strah teritorije** (`TerritoryFear`) množi krivu bloka: pojas koji porodica cedi vidno
  se prazni; oporavak s njim.
- **Radnja `Shut/Ruined`:** nema gazde, nema mušterija, `Boarded` izlog (već postoji);
  dostava ne dolazi.
- **Reket na vratima:** kolektor (`DoorBeat.VisitBusiness`) zatiče gazdu na pragu ili
  unutra — u fazi `Talking` gazda je telo koje stoji preko puta njega, ne vazduh. Policajac
  koji dolazi na prijavu ulazi kroz ista vrata (§9.2 policijskog plana) — gazda je tu.
- **Svedoci sa adresom (ugovor, §0.6):** ko se zatekne kraj pucnjave je svedok —
  `SnapshotWitnesses` ostaje kapija, ali snimak sad **prikuje osobu** (§4.1): ime, lice,
  kućna i radna vrata idu u `Witness`. Svedok se od tada spawnuje samo od kuće ili s posla,
  policija zna gde da ga nađe (docket), porodica takođe. Novine mogu (D14) da štampaju
  „the grocer at 318 Canal Heights". Gazda na vratima je prirodno najčešći svedok — i
  najlakši za naći.
- **Policija danju peške** već računa da je „ulica puna ljudi" — sad i jeste.

---

## 5. Ideje (go wild) — šta oko vidi, šta treba, cena

Cena: S = dan, M = 2–4 dana, L = nedelja+. Sve ovde je predlog; u faze (§7) ulazi samo
što korisnik potvrdi.

| # | šta se vidi | šta treba | cena | kuka na igru |
|---|---|---|---|---|
| I1 | **Gazda otključava radnju u 08:45**, stoji na pragu s prekrštenim rukama, gleda na sat, ulazi; u 18 spušta roletnu i ide kući | vrata u CityLife, radno vreme, kast po `BusinessOwner` | M | reket, prijava, svedok |
| I2 | **Kola uz svaki ivičnjak**, ujutru se stambena ulica prazni, centar puni; uveče obrnuto — i to se *vidi* dok se dešava | legalni ivičnjak, slotovi, plima, `GoTo(park)` za civile | L | gridlok test, olupine na slotu |
| I3 | **Red ispred noćnog kluba** sa izbacivačem od 23:00, taksiji dolaze i odlaze, u 02:00 pijani se njišu kući | Nightclubs tela, taksi štand, `Sway_Drunk` | M | klub je već blok; casino isto |
| I4 | **Ručak u parku**: ljudi iz kancelarija u 12:15 sednu na klupu s krofnom i šoljom, u 12:50 odu | klupe već rade; `Eat_*` + props iz Idles paketa | S | — |
| I5 | **Trkači u 06:30** duž keja i parka, istežu se kod česme | trk gait + `Stretch_*` | S | — |
| I6 | **Autobus** koji stvarno staje; ljudi na stajalištu gledaju na sat i ulaze | ruta, stajališta na liniji, portal na vratima | M | policija/krew mogu autobusom? ne |
| I7 | **Kamion za smeće u 05:00** kao jedini zvuk ulice; dva radnika trče uz kante | ruta po svim ulicama, Jumpsuit tela, kante iz `SidewalkPlan` | M | audio bed već ima ulicu |
| I8 | **Dostava ujutru**: kombi na duplom parkingu ispred pekare, saobraćaj obilazi | ruta do arhetipa, dupli parking = privremeni zauzimač u traci | M | `PassesAtKerb` već obilazi |
| I9 | **Mrtvi ne nestaju sami**: hitna dolazi za policijom i odnosi telo; kreda ostaje | `Car_Ambo_01`, nosila (prop?), portal | M | novine, `BodyStays` |
| I10 | **Vatrogasci na zapaljenu radnju** — sirena, crevo, radnja posle daskama | Firetruck, stanica ili rub, veza na `Storefront.Burning` | M | paljenje = reket |
| I11 | **Deca**: samo uz roditelje (Town porodice) vikendom u parku i školski autobus; **nikad sama noću** | filter gomile + porodični odlazak | S | D6 |
| I12 | **Kiosk s prodavcem** od 06:00 (novine izašle), ljudi zastanu 30 s | `Newsstand` arhetip već postoji; telo + stanica | S | City Wire |
| I13 | **Govornica**: čovek na telefonu 1–2 min | `UsePhone_Call` bez pametnog telefona (D10) | S | prijava policiji je „telefon" |
| I14 | **Ćošak**: tinejdžeri naslonjeni na zid 16–20 h, odu kad padne mrak; stariji ispred ulaza | Lean klipovi (CrewKit ih ima), hangout odlazak | S | rivali već drže ćoškove — razlika mora da se vidi |
| I15 | **Terasa kafića sa mušterijama koje dolaze i odlaze** (ne posađene) | `SeatAnchor` iz ambijentalnog sloja kao stanice | S | tableau sloj se povlači |
| I16 | **Nedelja**: radnje zatvorene, park pun, crkva (ako uđe u Core), nema smeća ni dostave | `DayOfWeek` već postoji | S | naplata već zna dan |
| I17 | **Tišina posle pucnjave**: blok se isprazni na 2 h, radnje spuste roletne | §4.8 | S | teritorija, strah |
| I18 | **Hotel**: taksiji, kofer, portir | `building-hotel-small` u Core (D7) | M | rivali/sastanci kasnije |
| I19 | **Parking-garaža i lot** kao portali: kola ulaze/izlaze po plimi umesto po tajmeru | `ParkingLot` već vozi | S | — |
| I20 | **Podzemna kao portal**: ljudi izlaze na stepenice u talasima 08:00/17:30 | `Plan.Subway` već stoji; talas po satu | S | jeftin „grad je veći od mape" |
| I21 | **Radnik koji ide kući s posla u luci/industriji** kroz grad | luka/industrija već imaju smene | S | — |
| I22 | **Doušnik/DEA u civilu** koji sedi u parkiranom kolu preko puta radnje porodice | GangWarfare `DEA_Plainclothes` | M | policijski plan Faza 3 — samo kad korisnik hoće |
| I23 | **Prosjak/skitnica** uz podzemnu i kod kanti (ideja iz expressway plana) | telo + sedeći/ležeći klip | S | period (1987 = beskućništvo) |
| I24 | **Šetač pasa** | nema psa u paketima → ne | — | — |

---

## 6. Arhitektura (kratko — ŠTA, gde)

| deo | gde | uloga |
|---|---|---|
| `People` registar + `Errand` + `DayTide` (krive) + `OpeningHours` | `Assets/Scripts/People` (`LivingCity.People`, čist C#, testovi) | papirni ljudi, radno vreme, plima; deterministički po seedu; bez UnityEngine |
| `KerbStrip` (legalni spanovi + slotovi + raspored) | `Assets/Scripts/People` (čist) + `Assets/RoadDemo/KerbParking.cs` (polaganje) | §4.6; pojedinačni pozivalac `KerbSlotNear` ostaje za policiju/krew, ali čita iste spanove |
| `CityLife` dobija vrata iz Core + ulaze zgrada | `RoadDemoBuilder.CoreFronts.cs`, `CityLife.cs` | §4.2; `CoreOutfitDoors` postaje opšta objava |
| `CivilianAgent` dobija **cilj** (Dijkstra po `PedLink`), stanice, portale; `DoorBeat` za ulazak | `Assets/RoadDemo/CivilianAgent.cs`, `PedestrianAgent.cs` | lutanje ostaje kao poslednje pribežište kad nema cilja |
| `Population` (budžet tela u prozoru, spawn na portalima, LOD tick) | `Assets/RoadDemo/CityPopulation.cs` | zamenjuje `SpawnPedestrians` fiksni broj; vezuje se na strimovanje |
| `ServiceRoute` + vozači (bus, smeće, dostava, taksi, hitna, vatrogasci) | `Assets/RoadDemo/ServiceFleet.cs` + po jedan vozač | §4.7; `DriverProfile` po vozilu |
| `DemoVehicle` dobija `GoTo(slot, park)` + „sledeći slot / obiđi blok" | `RoadDemo/DemoVehicle.cs`, `RoadCar.Parking.cs` | civilno kolo sa odredištem |
| Tableau sloj | `ResidentialAmbientPeople.cs` | povlači se (D11); `SeatAnchor`, `BinAnchors`, `VenueDoorSide` ostaju kao podaci |

Sat: ostaje `CityClock` bez događaja; `Population` ga anketira jednom u frejmu za ceo grad
(kao uspavani director). Testovi: čisti (`gangsters_people_tests`), audit
(`gangsters_people_audit --seed N`: vrata bez sajta, sajt bez vrata, slot na zebri…).

---

## 7. Faze (prekrojeno posle pregleda §13)

**Šta je pregled promenio:** faza 0 više nije samo merenje nego **kapija** — dok popis vrata
ne stoji, sledeća faza se ne otvara. Dodela adrese je izvučena ispred odlazaka (nalaz K3), a
nadmetanje za slot, sat na ×16, migracija save formata i LOD tick dobili su svoje tikete umesto
da budu pretpostavka (K2, O4, O5, O7).

| faza | šta | izlaz koji se može pogledati |
|---|---|---|
| **0. Popis (kapija)** | popis vrata na seedu 1987 **razdvojen po izvoru** (poslovni sajtovi / blokovi centra / ulazi stambenih zgrada), metri legalnog ivičnjaka i broj slotova pri 60 %, ms po frejmu gomile danas, i **proba gustine** (400 pešaka / 120 kola, samo pogled) | tabela u §0; komanda ništa ne postavlja niti peče, a dve vrednosti `MiniCoreDemo` služe samo vizuelnoj probi. Centar daje 121 projekciju, pa faza 2 ne pada na K1. |
| **1. Adrese i vrata** | Core objavljuje vrata u `CityLife`; **`People.HomeDoorFor(id)` / `WorkDoorFor(id)` dobijaju stabilan interfejs** koji u ovoj fazi vraća vrata izvučena hešom iz objavljenog spiska — faza 4 menja SAMO podlogu, ne interfejs; deca van gomile | svaki čovek na ulici ima adresu na koju može da se vrati |
| **2. Odlasci** | civil dobija odlazak vrata→vrata Dijkstrom; ulazak kroz `DoorBeat`; izlazak iz *istih* vrata; sat gasi i pali odlaske po plimi (kriva × budžet); **svako trajanje u satima igre, ne u sekundama** | ljudi izlaze iz radnji i ulaze u njih; grad je u 03:00 prazan a u 12:00 pun |
| **3. Ivičnjak** | legalni spanovi + slotovi + polaganje parkiranih kola po plimi; civilno kolo sa `GoTo(park)`; **nadmetanje za slot u `RoadCar` (sledeći slot → obilazak bloka → odustajanje iza ruba) je poseban tiket, ne pretpostavka** | ulice pune kola, saobraćaj ih obilazi, 0 gridloka na 30 runova |
| **4. Ljudi na papiru** | registar gazdi, osoblja i stanara pod interfejs iz faze 1; gazda otvara i zatvara; mušterije po arhetipu; kolo uz kuću; portali; **budžet tela + LOD tick sa merenim pragom** | isti gazda svaki dan na istim vratima; reketaš zatiče čoveka |
| **5. Službena vozila** | ruta + stajanja: autobus, smeće, dostava, taksi. **Dupli parking dostave samo na bulevarima dok mera ne kaže drugačije** (nalaz O6) | autobus staje, smeće u 05:00 |
| **6. Grad reaguje** | tišina posle pucnjave, strah × kriva, hitna i vatrogasci, **prikivanje svedoka + migracija save formata** | posle pucnjave blok utihne, hitna odnese telo, svedok se nađe kod kuće |
| **7. Mesta** | klub red, park ručak i trkači, terasa kao stanice, hangout ćošak, kiosk, govornica, nedelja | po D4, D7, D12 |

Zavisnosti: 0 je kapija za sve. 2 traži 1. 3 je nezavisna od 1 i 2 (može paralelno). 4 traži 1
i 2. 6 traži 4. 7 traži 2.

---

## 8. Presuda (meri ono što broji)

Nalazi koje kod broji sam nad sobom (u `DriveTrace` kao `fault`, i u auditu po seedu):

| nalaz | šta meri |
|---|---|
| `NoDoor` | upotrebljiv sajt (`Eligible`) bez vrata na grafu; stambena zgrada bez ulaza |
| `DoorOff` | vrata čiji prag nije na trotoaru ispred svoje fasade (preko ulice, u dvorištu) |
| `Ghost` | telo nastalo ili nestalo u prozoru kamere van portala |
| `Stampede` | više od N odlazaka iz vrata u istom frejmu (plima, ne talas) |
| `Lost` | odlazak koji nije stigao za 3× procenu (dužina/brzina) |
| `DoorJam` | `DemoDoor.Busy` držan duže od 20 s |
| `Orphan` | gazda/mušterija na vratima radnje koja je `Shut/Ruined` ili zatvorena po satu |
| `KerbFoul` | slot na zebri, u 7 m od raskrsnice, na hidrantu, stajalištu, prilazu; ili kolo koje `AddStatic` proglasi olupinom (`Parked=false`) |
| `Gridlock` | kolo stoji ≥ 90 s (postojeći nalaz harnessa) — parkirana kola ne smeju da ga proizvedu |
| `NoPark` | civilno kolo sa ciljem parkiranja koje je odustalo (25 s) i nije našlo sledeći slot |
| `NightCrowd` | zauzeće trotoara u 03:00 iznad krive (kriva se poštuje) |
| `HomelessWitness` | svedok u docketu bez kućnih vrata na grafu |
| `WitnessGhost` | svedok čije se telo pojavilo negde drugde osim na svojim kućnim/radnim vratima ili iz svog kola |
| `Overbudget` | tela u prozoru iznad budžeta, ili tick gomile iznad N ms (mera iz `TickTimer`) |

**Sweep:** `gangsters_people_audit` nad seedovima 1–30: `NoDoor = 0` za upotrebljive sajtove
(block-01…16 posebno izveštavaju koliko je vrata geometrija našla), `KerbFoul = 0`.
**Runovi:** 30 runova harnessa (`--seconds 300`, fiksni korak) po fazi: `Ghost = Gridlock =
DoorJam = 0`; stope kao brojka: *udeo odlazaka koji su stigli*, *zauzeće ivičnjaka po klasi
ulice naspram cilja*, *ms po frejmu gomile pri budžetu*. Slika (korisnik u Play) je sud o
tome da li deluje živo — brojke su sud o tome da li je ispravno.

---

## 9. Šta se u postojećim sistemima menja (van NPC sloja)

- `RoadDemoBuilder.SpawnPedestrians` / `SpawnCars`: fiksni brojevi postaju budžeti koje
  `Population` puni po plimi (stari put ostaje iza zastavice za demo scene bez sata).
- `CoreOutfitDoors` → opšta objava vrata u `CityLife` (sedište porodice čita isto).
- `ResidentialBlocks` stajališta: ako uđe autobus (D5), stajališta se polažu **na liniji**,
  ne kao nasumičan dekor.
- `CrewCars.KerbSlotNear` čita legalne spanove (policija i krew prestaju da parkiraju na
  hidrantu i stajalištu) — ponašanje inače isto.
- `CivilianAgent.SnapshotWitnesses` vraća i adresu/posao kad ih svedok ima (novine biraju
  hoće li — D14).
- `CrowdLooks`: deca izlaze iz filtera gomile (D6).
- `Docs/business-inventory.md`: protivrečne brojke 631 / 3.263 razrešene su ponovljenim
  auditom na 3.581 / 3.564 (§0); dokument je usklađen.

---

## 10. Nije deo ovog epica

Unutrašnjosti zgrada; dijalog/interakcija igrača sa civilima; vreme (kiša); LivingCity
`CityBuilder` grad (uspavani sloj se *preuzima kao model*, ne oživljava); `ResidentialForge`
(EPIC 36 — ulaz zgrade je zajednička tačka, dogovoriti se ko ga uvodi); plan performansi
L1–L8 (`city-performance-plan.md`) — ovde samo budžet tela i tick; rivali i njihovi ćoškovi
(EPIC 25) — samo se razlikuju od hangouta; novi tipovi blokova osim onih iz D7.

---

## 11. Odluke (korisnik, 2026-09-04)

**Korisnik 2026-09-04: „sve se slažem" — svih 19 odluka po predlogu.** Predlozi u desnoj
koloni su od tog časa pravila; brojke u njima ostaju polazne vrednosti koje se menjaju po
slici, ali kroz podatak, ne kroz kod. Redosled koji je tražio: pregled, pa epik u Linear-u, pa faze.

**D20–D22 su NOVE i čekaju odluku** — otvorio ih je nezavisni pregled (§14). Uz svaku stoji
moja polazna pretpostavka da posao ne stane; nijedna nije pravilo dok je ne potvrdiš.

| # | pitanje | odluka (= predlog) |
|---|---|---|
| **D1** | **Budžet tela u prozoru kamere**: pešaka, kola u vožnji, parkiranih, službenih? | 240 / 48 / bez gornje granice za statiku u prozoru (meri se u fazi 0) / 6 |
| **D2** | **Plima dana** — prihvatiti tabelu §4.5 kao polaznu i menjati po slici? | da, polazna; menja se iz podatka, ne iz koda |
| **D3** | **Legalni ivičnjak**: 7 m od raskrsnice (današnje), hidrant ±4.6 m, stajalište 12 m, zebra +3 m; **zauzeće** stambeno 80/45 %, centar 25/70 % (noć/dan)? | da, sve polazno |
| **D4** | **Koja službena vozila** ulaze (§4.7): autobus, smeće, dostava, taksi, hitna, vatrogasci, novinski kombi, školski? | prvo autobus, smeće, dostava, taksi; hitna i vatrogasci u fazi 5 |
| **D5** | Autobuska **stajališta se sele na liniju** (danas nasumičan dekor u stambenim blokovima)? | da |
| **D6** | **Deca**: nikad sama; samo uz roditelje (Town porodice) danju/vikendom i školski autobus — ili uopšte ne? | uz roditelje, bez škole dok D7 ne kaže |
| **D7** | **Nove zgrade u Core gradu** (prefabi postoje, nikad položeni): vatrogasna stanica, bolnica, škola, hotel, crkva (samo Town paket), pošta, banka? Svaka je novi tip bloka u `CoreLayout`. | vatrogasna + hotel; ostalo kasnije |
| **D8** | **Gazda kao telo** iz `BusinessOwner` (ime, starost → telo), na vratima po radnom vremenu; kolektor i policajac ga zatiču? | da — ovo je srce reketa na ulici |
| **D9** | **Nedelja**: radnje zatvorene (osim diner/pub/klub/pumpa), park pun, bez smeća/dostave, crkva ako uđe? | da |
| **D10** | **Govornica** sa `UsePhone_Call` klipom (drži nevidljivi pametni telefon, ruka na uhu)? | da, bez propa — ako na slici izgleda kao mobilni, izbaciti |
| **D11** | **Tableau sloj** (`ResidentialAmbientPeople`): potpuno ugasiti, a sedišta/kante/vrata lokala zadržati kao stanice? | da — posađeni ljudi odlaze, mesta ostaju |
| **D12** | **Stanice na trotoaru** koje su dozvoljene: naslanjanje na zid, gledanje na sat, jelo/piće na klupi i terasi, govornica, kiosk, pijano njihanje, istezanje, molitva (samo crkva)? | sve osim molitve dok nema crkve |
| **D13** | **Tišina posle pucnjave** 2 h igre na bloku i susedima; strah teritorije množi krivu? | da |
| **D14** | **Novine štampaju svedoka s adresom i poslom** („the grocer at 318 Canal Heights")? | da, kroz postojeće javne kapije |
| **D15** | **Radno vreme** §4.4 kao polazno? Diner 24 h? | da; diner 24 h (period) |
| **D16** | **Taksi**: dva postojeća taksi tela dobijaju ponašanje (štand, poziv, noću više)? | da |
| **D17** | Kabriolet (`Convertable_01`) i Town pikap ulaze u civilni bazen? | da |
| **D18** | **Linear**: otvoriti EPIC sa tiketima NPC-000… po fazama posle ovih odluka? | da |
| **D19** | **Prolaznik bez adrese** (došao autobusom, podzemnom, sa ruba) koji se zatekne kraj pucnjave: dobija adresu u trenutku snimka (stan po hashu u najbližem stambenom bloku), ili ne može biti svedok? | dobija adresu — svaki svedok ima dom (§0.6) |
| **D20** | **Dužina šetnje (nalaz C1).** Čovek prelazi **90 m po satu igre**, pa se odlazak od 500 m ne može prepešačiti. Tri izlaza: (a) **portal je prečica** — pešači se samo poslednja deonica do ~90 m, ostatak se **razreši** kroz vrata, kola, autobus ili podzemnu; (b) odlasci **samo unutar bloka**; (c) menja se dužina sata igre (danas 60 s). | **moja pretpostavka dok ne kažeš: (a), uz gornju granicu pešačke deonice od 90 m.** Čuva katalog odlazaka i model čini jeftinijim, ne skupljim |
| **D21** | **Nedelja i naplata (nalaz M8).** Dan naplate se hešuje na **sedam** dana uključujući nedelju, a nedeljom radnje zatvaram — jedan blok od sedam se više nikad ne bi naplatio. | **moja pretpostavka: heš ide na šest radnih dana**, niko ne gubi prihod, radno vreme se ne dira. Alternativa je da blok radi svoju nedelju |
| **D22** | **Šta novine štampaju o svedoku (nalaz m14).** Puna adresa je igraču mapa do čoveka koga treba ućutkati. | **moja pretpostavka: ulica bez kućnog broja** za očevica; prijavilac je ionako javno na svojoj radnji. Ako hoćeš da likvidacija bude laka, kaži i štampamo broj |

---

## 12. Rizici koje plan vidi

- **Performanse.** 100 → 240 tela i stotine parkiranih kola nad gradom koji već crta 33 M
  temena po frejmu. Zato faza 0 meri, budžet je tvrd, tick se seče po daljini, a statika ide
  kroz strimovanje. Ako mera kaže „ne staje", budžet pada — ne plan.
- **Gridlok od parkiranih kola.** Zabeležen sa starim rasporedom policije NA traci. Pojas
  od 2.5 m to rešava po geometriji, ali dupli parking dostave i pull-out u gustom saobraćaju
  moraju kroz 30 runova sa `Gridlock = 0` pre nego što uđu.
- **Centar bez sajtova.** 16 blokova centra nema poslovne sajtove; ako geometrija ne nađe
  vrata, najgušći deo grada ostaje bez gazdi — a to je deo koji kamera najviše gleda.
  Faza 0 to broji pre nego što se išta obeća.
- **Duhovi na rubu.** Strimovanje blokova i cull na 330 m moraju da se poklope s portalom
  „rub" — inače telo nestaje na oku. `Ghost` je zato prvi nalaz koji se meri.
- **Dva sloja vrata** (ambijentalni `VenueDoorSide` i `DemoDoor`) — moraju u jedan pre faze 3.
- **Ulaz stambene zgrade** deli sudbinu sa EPIC 36 (ResidentialForge §1.1) — jedan od dva
  epica ga uvodi, ne oba.

---

## 13. Pregled pre epika (2026-09-04)

Contrarian subagent je pao na kvoti; pregled je urađen u glavnoj niti na Opusu, uz proveru
tvrdnji u kodu. To je slabije od nezavisnog pregleda i sme da se ponovi.

**Šta plan drži:** dijagnoza je merena, ne nagađana; presuda u §8 meri ono što broji; odluka
da je telo budžet a čovek podatak je jedina koja preživljava 33 M temena po frejmu.

### Kritični nalazi

**K1 — vrata za centar ne mogu da se izvedu kako §4.2 tvrdi.** `ShopDoors.Of` prima
`BusinessMarker` (`ShopDoors.cs:35`) — dakle biznis koji već postoji kao sajt. Blokovi
`block-01…16` ne objavljuju nijedan sajt, pa nema markera da se pita; `FacadeFinder` nalazi
koja je strana pročelje **date zgrade**, ne otkriva lokale u pečenoj mreži. Segmentacija
pečenog bloka na lokale je posao inventara biznisa, ne prolaza za vrata.
**Popravka (izmerena u §0):** faza 0 broji direktne prefab instance **po izvoru**, ne
zamišljene zgrade. `StorefrontDoorCatalog` daje prag shop modula, a
`SM_Bld_Apartment_Door` sopstveni pivot. Dobijeno je 121 projekcija na graf, pa centar nije
izbačen iz faze 2; 20 neuspeha ostaje eksplicitno, umesto da ih `FacadeFinder` sakrije.

**K2 — „sledeci slot pa obiđi blok“ ne postoji.** `RoadCar` ima samo odustajanje posle 25 s
(`RoadCar.cs:989`) sa rezervom od 22 m (`:999`), a izbor slota odbija cilj dalji od 45 m
(`RoadCar.Parking.cs:91`). Politika nadmetanja je nasleđena specifikacija iz
`vehicle-movement-plan.md` koja nikad nije napravljena.
**Popravka:** svoj tiket u fazi 3, u najosetljivijoj klasi u repou; ne računa se kao gotovo.

**K3 — redosled faza nije držao.** Stara faza 1 tražila je odlazak vrata→vrata i izlazak na
*ista* vrata, a kućna i radna vrata dodeljuje registar iz stare faze 3.
**Popravka:** adrese su sada faza 1 sa **stabilnim interfejsom** (`People.HomeDoorFor`), koji
u fazi 1 vraća vrata po hešu, a u fazi 4 istu funkciju puni registar. Menja se podloga, ne
interfejs — pa faza 4 ne baca ništa što je faza 2 napravila.

### Ozbiljni nalazi

**O4 — prikovani svedok nema gde da živi u sistemu.** `CityBlockRecycler` ne zna ni za jedno
telo (nema nijedne reference na civile). Ništa se danas „ne strimuje sa blokom“ osim
geometrije. Ako svedok mora da bude nađen kod kuće, on je ili izuzet od budžeta tela (curenje
koje raste sa brojem otvorenih slučajeva) ili obećanje pada. Uz to se svedoci **snimaju u
save** (`CampaignFile.cs:95 WitnessDto`, `PrisonSnapshot.cs:67-71`), pa dodavanje kućnih
vrata menja format i traži migraciju.
**Popravka:** prikivanje je faza 6 sa sopstvenim tiketom za migraciju; pravilo budžeta —
prikovan svedok ima **rezervisano mesto u budžetu dok mu je slučaj otvoren**, i broj otvorenih
slučajeva je time gornja granica curenja.

**O5 — lestvica brzine ×16 nije nigde pomenuta.** `CityClock` vuče `Time.timeScale` do 16
(`CityClock.cs:30, :94`). Na ×16 sat igre traje 3,75 s, ceo dnevni ciklus 90 s; plima, radno
vreme, dvočasovna tišina, 40 s duplog parkinga i razmak autobusa postaju treptaj, a vožnja
je podešena na fiksni korak.
**Popravka:** svako trajanje u novom sloju se meri u **satima igre**, ne u sekundama (faza 2);
manevri vozila ostaju u sekundama i dobijaju gornju granicu koraka.

**O6 — geometrija gridloka je tesnija nego što plan pretpostavlja.** Obična ulica ima jednu
traku po smeru (`StreetKit.cs:27, :31` — `RoadHalf = 5`), a `DriverProfile.Traffic` ne koristi
suprotnu traku i sme 1 m preko krune (`DriverProfile.cs:74-77`). Dupli parking od 40 s uz
ciklus semafora od 26 s je ozbiljan kandidat za `Gridlock`.
**Popravka:** dupli parking dostave samo na bulevarima dok 30 runova ne kaže drugačije.

**O7 — sečenje po daljini je imenovano, nije opisano.** Sve se i danas kuca svaki frejm bez
deljenja (`RoadDemoBuilder.cs:786`), a plan odozgo dodaje Dijkstru po odlasku.
**Popravka:** LOD tick je svoj tiket u fazi 4, sa pragom iz mere faze 0.

**O8 — dijagnoza možda štedi na pogrešnom mestu.** Plan tvrdi da prazno nije mali broj.
Moguće je da je delom ipak broj.
**Popravka:** proba gustine (400 pešaka / 120 kola) je stavka faze 0 — dve vrednosti u
`MiniCoreDemo` i pet minuta pogleda u editoru. Ako to već izgleda živo, prioriteti epika se
menjaju.

### Presuda

Model drži; tri kritična nalaza pogađala su prvu fazu. Prekrojen je **redosled**, ne model.
Najjeftiniji test koji obara najrizičniju pretpostavku je popis vrata po izvoru — prva
stavka faze 0, bez izmene koda.


---

## 14. Nezavisni contrarian pregled (2026-09-04, posle otvaranja epika)

Prvi pokušaj nezavisnog pregleda pao je na kvoti, pa je §13 bio **samopregled**. Kad se kvota
vratila, pregled je ponovljen nezavisno, sa dva zadatka: napasti plan, i **napasti §13**.
Presuda nad §13: *pravi pregled sa karakterističnom pristrasnošću — nalazi koji se reše
prekrajanjem redosleda i dodavanjem tiketa, dok je svaka noseća pretpostavka ostala netaknuta.
Tri najopasnije stvari nisu bile na spisku.*

**Presuda nad planom: treba prerada.** Ne modela — „telo je budžet, čovek je podatak, portal je
jedini šav" stoji — nego tri mehanizma pisana protiv činjenica koje kod demantuje.

### Kritični

**C1 — šetnja je aritmetički nemoguća na ovom satu.** `PedestrianAgent.Speed = 1.5f` (`:356`) i
`realSecondsPerGameHour = 60f` (`CoreDemoBuilder.cs:58`) daju **90 m po satu igre** (2.160 m za
ceo dan). Tabela odlazaka u §4.3 je time nemoguća: ručak na 150 m je **1.7 sati igre**, odlazak
na posao od 500 m je 5.5 sati, a telo sa ruba iza 330 m stiže posle **3.7 sati** i mora se celo
to vreme simulirati — što princip 2 (§3) izričito zabranjuje. §13 je uhvatio lestvicu ×16 i
**promašio da je sat već 60× na ×1**. Ovo nije štelovanje nego noseća jedinica modela.
Odluka: **D20**. Faza 0 mora objaviti „metara po satu igre" uz popis vrata, a §4.3 se prepisuje
pre nego što tiket odlazaka počne.

**C2 — rezervacija svedoka nema granicu.** §13 (O4) je tvrdio da je broj otvorenih slučajeva
plafon. `PrisonPipeline.OpenCases` (`:272-279`) **nema nikakav vremenski filter**, a
`LapseAbandonedCases` (`:515`) izričito **preskače slučaj bez optuženog**. Svaka prijava nosi
prijavioca plus do tri očevica. Na oko 60 neodgovorenih prijava rezervacija pojede ceo budžet od
240 tela i ulica se isprazni — **simptom zbog kog epik postoji, proizveden epikom**, i nevidljiv
u runu od 300 s. Popravka: pinovati **samo** očevica, samo dok svedoči, i samo unutar prozora
pamćenja prijave; prijavilac ne traži pinovanje jer već ima adresu (svoju radnju). To je tvrd
plafon `3 × (slučajevi u prozoru)` i **tvrdi se u kodu kao imenovani nalaz**, ne u dokumentu.

**C3 — imenik je premali, a novine spajaju svedoka po IMENU.** `PedestrianIdentity` nosi 40
muških imena, 40 ženskih i 48 prezimena, dakle **1.920 kombinacija po polu** za oko 7.000 ljudi
koje registar traži. `PressDesk` (`:448-449`) spaja ubijenog svedoka sa slučajem preko
`Witnesses[w].Name == witness.Name`, a `CourtCase.Witness` nosi ime i seed, **bez ikakvog id-a
osobe**. Duplikati su matematički sigurni, pa novine upisuju smrt u tuđ slučaj. Popravka:
migracija save formata koju O4 ionako otvara dodaje **stabilan id osobe**, a `PressDesk` spaja
po njemu.

**C4 — popravka iz K1 je bila prazna, a pravi odgovor stoji u repou nepročitan.** „Jedna vrata
po zgradi" pretpostavlja zgrade u pečenom bloku; `block-01.prefab` je **171 modul** (krovovi,
ploče, spratovi), pa bi vrata završila na krovu. `ResidentialHarvest` upućuje na pravi šav:
ime izvornog prefaba direktne instance. Preliminarni ručni broj 84 shop / 46
`SM_Bld_Wall_Window` i spisak sa blokom 07 bili su pogrešni; izvršni popis iz §0 nalazi
**92 shop + 49 `SM_Bld_Apartment_Door`**, nula `SM_Bld_Wall_Window`, i šest stvarno praznih
blokova (02, 03, 08, 14, 15, 16). Uz to §4.2 **precenjuje** šta nasleđuje:
`CoreOutfitDoors` (`RoadDemoBuilder.CoreFronts.cs:57-77`) uzima samo sajtove sa
`Role == FrontageRole`, ne svih 3.564.

### Ozbiljni

| # | nalaz | popravka |
|---|---|---|
| **M5** | **`Ghost = 0` nije dostižan na 330 m.** `CrowdCullDistance` je cull **renderera**; telo iza njega i dalje postoji i kuca. Repo već ima radnu konvenciju: `StreetTraffic.Thin` teleportuje kola po **frustumu i pojasu**, ne po poluprečniku. | `Ghost` broji **vidljivo** nastajanje i nestajanje: van frustuma **i** preko pojasa. Tek tada je kriterijum i smislen i dostižan |
| **M6** | **Faza 3 nije nezavisna.** Vozač koji izlazi iz parkiranog kola je portal (faza 4), a D5 seli stajališta u fazi 5 — dok spanovi iz faze 3 isključuju **12 m po stajalištu**. | Faza se deli: **3a** spanovi, slotovi i položena kola (bez vozača, stvarno nezavisna, i to JESTE korisnikov zahtev broj 2), **3b** vozači posle faze 4. Selidba stajališta ide **pre** 3a |
| **M7** | **Druga sesija je pretekla tiket K2.** Commit `8f6655da2` (posle `aa31246f3`) uveo je `RoadCar.ParkingSpotAvailable/Selected` kao šavove, traženje slobodne rupe 15×3 m u oba smera, i knjigu rezervacija u `PolicePatrolCar`. | Tiket se prepisuje: civili **implementiraju te šavove** uz **zajedničku** knjigu rezervacija; samo „obiđi blok jednom" je zaista novo. `NearestLegalKerbSlot` (neograničen skan cele mreže) dobija granicu pre nego što mu se doda 48 civila |
| **M8** | **Plima razbija reket.** `DayOf(blockId)` hešuje na **sedam** dana uključujući nedelju (D9 zatvara radnje), pa se **jedan blok od sedam nikad više ne naplaćuje**. `ShouldSend` nema **gornju** granicu sata, pa šalje kolektora u 21:00 pred spuštene roletne. | D21, uz `LatestSendHour` izveden iz tabele zatvaranja |
| **M9** | **Zvuk zasićuje i plima se ne čuje.** `DemoAudio` meri gomilu do **16** pešaka i saobraćaj do **10** kola, a **parkirano kolo broji kao saobraćaj**. Sa 240 tela i punim ivičnjakom oba merača stoje na jedinici stalno. | Broje se samo kola **u pokretu**, imenioci se skaliraju na novi budžet, a žamor ulice se vodi iz **plime** koju novi sloj ionako računa |
| **M10** | **Novi sloj pomera deljeni tok slučajnosti.** Gomila vuče iz `UnityEngine.Random`, a harness po sopstvenom pravilu meri **stope nad runovima** baš zato. Prva faza pomera svaki nizvodni izvučeni broj. | `LivingCity.People` dobija **svoj** tok; `UnityEngine.Random` je u njemu zabranjen. **Postojeće stope se ponovo snimaju u fazi 0**, da epik ima „pre" |
| **M11** | **Harness ne može da testira ×16.** `PlayHarness` pinuje `Time.timeScale = 1f` i fiksni korak, a plima se gleda ubrzano. Uz to `RoadDemoBuilder.cs:767` prosleđuje **jedan neograničen `dt`** svemu. | Ograničenje `dt` je izmena **postojećeg** koda i nosi svoj baseline od 30 runova; harness dobija režim sa velikim korakom pre nego što faza 3 potpiše |

### Sitno, ali važno

* **m12 — protivrečnost 631 / 3.263 je noseća, ne fusnota.** Razrešena je u §0 kroz
  `gangsters_business_audit --seed 1987 --json`: 3.581 sajt / 3.564 upotrebljiva pre nego
  što se registar dimenzioniše.
* **m13 — `HomeDoorFor` treba da vrati ADRESU, ne vrata.** `DemoDoor.Building` je živ objekat na
  bloku koji se strimuje, a u fazi 1 jedini id koji telo ima je runtime broj koji se menja pri
  recikliranju. Interfejs je `Address HomeOf(PersonId)` (id sajta ili zgrade plus strana), a
  `DemoDoor` se razrešava pri upotrebi — inače potpis preživi a **pozivi ne**.
* **m14 — puna adresa u novinama je mapa do svedoka.** Odluka D22.
* **m15 — svedok se danas ne može ponovo naći.** `WitnessWatch` (`:106`) izbacuje par čim telo
  nestane, pa „porodica ga nađe kod kuće" traži **ponovno vezivanje** zapisa za novo telo, što
  dira `WitnessWatch`, `LawWire` i doseg pritiska. Plan mu je dao jedan red.
* **Predlog reza (obim je korisnikov):** zadržati fazu 0, **3a**, plimu kao čistu gustinu bez
  adresa po čoveku, i gašenje tableau sloja — to je većina onoga na šta se korisnik žalio.
  Odložiti u drugi epik registar, gazdu kao telo i prikovanog svedoka.

### Najjeftiniji test

Dva grepa, bez editora: `PedestrianAgent.Speed` i `realSecondsPerGameHour`. **Devedeset metara
po satu igre** drži se uz §4.3, i odluka D20 se donosi pre nego što NPC-001 počne. Drugi po ceni
je proba gustine iz O8, koja je već u fazi 0.
