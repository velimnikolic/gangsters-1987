# Plan: kretanje vozila bez sudara (RoadDemo + CrewDemo)

> Stanje na dan 2026-08-19. Cilj: jedan sistem vožnje za SVA vozila u igri (saobraćaj,
> gangsterska kola, policija, kasnije kamioni/kupljena/ukradena kola), koji radi na
> gridu RoadDemo-a (glavna igra), a CrewDemo ga samo koristi kao test-bed na jednoj ulici.

---

## 0. Gde smo sada (dijagnoza)

Danas postoje **dva nepovezana sistema**:

| | `DemoVehicle` (RoadDemo grid) | `StreetTraffic.Car` + `CrewCar` (CrewDemo ulica) |
|---|---|---|
| Model puta | graf `RoadNode/RoadEdge` (jedna traka = jedna ivica), semafori | `IRoadModel` = prava ulica duž X (`LaneZ`, `XMin/XMax`) |
| Pozicija | 1-D: `(edge, s)`, `Place()` direktno postavlja transform | slobodna 2-D kinematika: look-ahead + yaw cap |
| Držanje razmaka | sortirana lista `edge.Cars` → O(1) vođa, `Allowed(0,gap)` | skeniranje **svih** `StreetTraffic.Users` × 60–140 uzoraka putanje, svaki frejm, za svaki auto |
| Manevri | samo pravo/levo/desno kroz raskrsnicu | obilazak, pretica­nje, U-okret, parkiranje, rikverc |
| Raskrsnice | semafor + `OncomingPriority` za levo | nema raskrsnica |
| Garancija da nema preklapanja | samo 1-D razmak u istoj traci; bočno ništa | samo `ClampZ` (ostani u kolovozu); preklapanje nije sprečeno |

### Konkretni problemi iz CrewDemo-a i njihovi uzroci

1. **Auto se zaglavi iza zaustavljenih kola** — obilazak (`LaySwerve`) se razmatra tek kad je
   blokada u kočionoj daljini; yaw brzina je ∝ brzini (`Speed/TurnMin`), pa auto koji je
   stao ne može ni da izvije nos; rikverc je naknadno zalepljen hack (`BackOff`, jednom po
   blokeru). Kad dva vozila stanu jedno ispred drugog (npr. saobraćaj koji obilazi parkirani
   crew-auto, a crew-auto obilazi saobraćaj) oba vide "stoji ispred mene" → **uzajamni
   zastoj** bez pravila ko popušta.
2. **"Ode u pogrešnu traku"** — posle obilaska kroz suprotnu traku `ResumeAfterPass` ponovo
   planira "odavde", bez obaveze vraćanja; auto nema stanje "moja traka", samo trenutnu
   putanju. Plus `ParkNear` sam bira ivičnjak po strani klika i po potrebi radi U-okret —
   igrač to ne vidi unapred.
3. **Oscilacija / nervozno preplaniranje** — `Rethink` svakih 0.4 s, `LayDetour` čim se
   promeni "parked", putanja se gradi iz snimka trenutka. Nema histereze ni događaja.
4. **Sudari su mogući po konstrukciji** — `ClearAhead` gleda samo: moja putanja vs.
   njihova trenutna kutija (+ mali "smear" brzine). Dva auta koja istovremeno odluče da uđu
   u isti prostor (kruna, suprotna traka, U-okret) ne vide jedno drugo dok već nisu tamo.
   `OncomingConflict` računa samo vreme prvog reda; ništa ne rezerviše prostor.
5. **Ne skalira** — O(N² × uzorci) po frejmu; u gradu sa 150–200 kola neupotrebljivo.
6. **Ne prenosi se na grid** — `IRoadModel`/`PathBuilder` rade samo u X/Z pravoj ulici;
   saobraćaj se teleportuje na kraj ulice umesto da vozi rute.
7. Ulazak/izlazak ljudi i klik na auto — UI/ukrcavanje, rešeno delimično (`PendingDrive`,
   `PickCarAt`), nije pitanje kretanja; ostaje da se proveri u Play-u.

---

## 1. Ciljna arhitektura (slojevi)

Pravilo: **kinematika, bez Unity fizike** (kao i sad). Ne sprečavamo sudar odbijanjem tela,
nego tako što **niko ne ulazi u prostor koji nije slobodan ili rezervisan za njega**.

```
 Sloj 4  Vozač (policy): Traffic / Gangster(Hot) / Police / Lorry  — pragovi i dozvole
 Sloj 3  Naredbe i parking: ruta do slota uz ivičnjak, ukrcavanje, izlazak iz slota
 Sloj 2  Manevri + prihvatanje prostora: praćenje, promena trake, obilazak, U-okret,
         rikverc, raskrsnica — SVE kroz ZAHTEV za traku-interval → dobije/nema
 Sloj 1  Putanja: PathBuilder u (s,d) koordinatama rute + profil brzine (postojeći)
 Sloj 0  Mreža: graf traka (RoadEdge) + geometrija poprečnog preseka + raskrsnice + slotovi
```

### Sloj 0 — mreža puteva (proširiti `RoadEdge`/`RoadNode`)

- Ivica ostaje "jedna traka u jednom smeru", ali dobija **poprečni presek**: referenca na
  *carriageway* (kolovoz) kome pripada: `CentreLine`, `HalfRoad`, lista traka sa bočnim
  pomakom `d` i smerom (`+1` naš smer / `-1` suprotan), `KerbD` (gde se parkira),
  `ParkingStrip` (slotovi uz ivičnjak: `s0..s1`, zauzet/slobodan).
- **Frenet koordinate**: svaki auto ima `(edge, s, d)`: dokle je stigao duž trake i koliko
  je bočno pomeren od njenog centra. Sve što danas `CrewCar` radi u `x`/`z` postaje `s`/`d`
  na ruti → ista logika radi i na pravoj ulici i na skretanju, bez prepisivanja.
- **Raskrsnica** = čvor sa **konektorima** (kvadratne Bezier krive `p0,p1,p2` koje
  `DemoVehicle.EnterNode` već računa) koji postaju mini-ivice u grafu, i sa
  **konfliktnom tabelom**: koji par konektora se seče (pravo NS vs pravo EW, levo vs
  pravo-suprotno...). Semafor ostaje, ali tabela je ono što garantuje da se dva auta ne
  nađu u istoj tački kutije (žuto/prolazak na kraju faze, policija kroz crveno).
- `IRoadModel` se menja u **`IRoute`/`ILaneFrame`**: `Pose(edge,s,d) → (pos, dir)`,
  `Lanes(edge,s)`, `KerbD(edge,side)`, `ClampD`. `StraightStreetModel` postaje graf od 2
  ivice (istok/zapad) + 2 U-okret konektora na krajevima (ili petlja) → CrewDemo vozi
  **isti kod** kao grad. Teleport na kraju ulice nestaje.

### Sloj 1 — putanja (zadržati `PathBuilder`, preseliti u (s,d))

- Primitive ostaju: `LaneRun`, `LaneChange`, `PullIn`, `UTurn`, `Slow`, plus nove:
  `Reverse(len)`, `Connector(node, from, to)`. Grade se u (s,d) duž **rute** (niz ivica +
  konektora) i tek pri uzorkovanju preslikavaju u svet.
- Profil brzine po krivini / kočenju / parking-stopu (postojeći `Build`) ostaje.
- Svaka putanja dobija **listu zahteva za prostor** (vidi sloj 2): koje trake, na kom
  intervalu `s`, u kom vremenskom prozoru.

### Sloj 2 — izbegavanje sudara: prihvatanje prostora + rezervacija

Ovo je srž plana. Tri mehanizma, od jeftinog ka skupom:

**(a) Uzdužno praćenje u traci** — kao `DemoVehicle`: svaka traka drži sortiranu listu
"zauzimača" (`LaneOccupancy`: ko, `s`, dužina, brzina, *da li je samo rezervacija*).
Vođa = sledeći u listi → `target = Allowed(vLead, gap)`. Konstantno vreme po autu.
Kamion/pešak/parkirano vozilo u traci su obični zauzimači (pešaci iz `Bodies/Walkers`
upisuju se u trake po poziciji, jednom po frejmu).

**(b) Bočni manevri = rezervacija intervala u ciljnoj traci.** Promena trake, obilazak,
ulazak u suprotnu traku, U-okret, izlazak sa parkinga, rikverc — svi traže
`Claim(lane, s0, s1, tFrom, tTo)`. Zahtev prolazi samo ako:
  - niko ko je **ispred** u ciljnoj traci nije bliži od razmaka praćenja; i
  - niko ko je **iza** u ciljnoj traci ne mora da koči jače od `BrakeComfort` da bi
    zadržao razmak (time-gap test, kao gap-acceptance u saobraćajnim simulacijama); i
  - za suprotnu traku: nijedan onaj ko dolazi ne stiže do intervala pre `tTo + margina`,
    računato sa NJEGOVOM kočionom daljinom, ne samo brzinom.
  Kad prođe, auto je **odmah upisan** u listu ciljne trake (kao "rezervacija" sa projektovanim
  `s`) → svi iza ga od tog trenutka prate kao da je već tamo, a drugi kandidati za isti prostor
  vide zauzeto. Ovo je tačno ono što `DemoVehicle` već radi kad se upiše u `next.Cars` na
  ulasku u raskrsnicu — generalizujemo na sve manevre. Tick vozila je sekvencijalan, pa
  "prvi koji zatraži dobija" je determinističko i nema simetričnog duplog ulaska.
  Rezervacija se ukida kad auto fizički napusti interval ili odustane.

  **Vožnja krunom (između dve trake)** — u borbi/bekstvu auto sme da vozi sredinom kolovoza,
  po razdelnoj liniji, da bi prošao između kola u obe trake. To nije posebna logika nego
  rezervacija sa bočnim pomakom `d` = kruna (ili bilo koji `d` između traka): auto koji
  zahvata dve trake traži `Claim` u **obe** (interval iste dužine) i upisan je u obe liste,
  pa ga i saobraćaj u svojoj traci i onaj iz suprotne vide kao zauzimača i koče ili se
  stiskaju uz ivičnjak. Uslov prolaska: u obe trake na tom intervalu ima mesta za **bar
  pola auta + margina** (kola uz ivičnjak, parkirana, ili u zastoju — prolaz između njih
  je poenta), i niko ko dolazi suprotnom trakom ne stiže pre `tTo + margina`. Brzina po
  širini prolaza (postojeći `SqueezeCap`: ~1 m vazduha = pun tempo, 0.5 m = 12 m/s,
  tesno = 8 m/s). Kad se rezervacija na kruni ne može produžiti (ispred je stisnuto), auto
  se vraća u traku koja je slobodnija — vraćanje je deo putanje, ne nova odluka.
  Saobraćaj (Traffic profil) krunu sme samo za obilazak parkiranog (kao danas `LayDetour`,
  do `OverCrown`), Hot gangster i policija sa sirenom je drže dokle god traje prolaz.

**(c) Raskrsnice** — konektor traži `Claim` nad konfliktnim konektorima u čvoru za prozor
`[tUlaz, tIzlaz]`; semafor je prvi filter (zeleno/žuto kao sad), tabela konflikata drugi
(levo skretanje čeka pravo iz suprotnog smera — današnji `OncomingPriority` postaje opšti
slučaj; policija sa sirenom dobija prioritet a ostali ne ulaze). Auto koji nije dobio
rezervaciju koči do stop-linije (`StopBeforeBox`) i traži ponovo svakog frejma.

**(d) Poslednja linija odbrane (safety net)** — prostorni heš (ćelije ~10 m) svih vozila; za
parove u istoj/susednoj ćeliji jeftina OBB provera *predviđenog* preklapanja za 0.5 s unapred.
Ako bi se preklopili: onaj kome je drugi "ispred po heading-u" koči na `HardBrake`; nikad
teleport, nikad guranje. Ovo se ne bi smelo okinuti ako (a)–(c) rade, pa je to i **debug
brojač**: svaki okidač = bug u planiranju.

**(e) Anti-zastoj pravilo** — ako auto stoji > 2 s, i njegov bloker takođe stoji, i bloker
takođe čeka na nas (uzajamno), rešava se **determinističkim prioritetom**: vozilo sa nižim
`Id` (ili: saobraćaj pre gangstera, a gangster pre policije koja ionako ide svojim putem)
sme rikverc + obilazak, drugo drži. Rikverc je manevar prve klase (sloj 1 `Reverse` +
rezervacija unazad), ne izuzetak.

### Sloj 3 — naredbe i parking

- **Slotovi uz ivičnjak** kao podaci na ivici (`ParkingStrip`): `ParkNear(point)` = najbliži
  slobodan slot **na strani klika**; ruta do njega Dijkstrom po grafu (postoji
  `PolicePatrolCar.RouteToward`), `PullIn` u slot, slot zauzet dok auto stoji (saobraćaj ga
  onda tretira kao zauzimača u traci sa `d` ka ivičnjaku → obilazak kroz (b), kao danas
  `LayDetour`, ali kroz rezervaciju).
- **Izlazak iz slota** = rezervacija u traci iza/ispred (kao `KerbClear` kod policije),
  ne kretanje "na slepo".
- Ako slot nije dostupan odmah: stani u redu (saobraćaj), ili uzmi sledeći slot (gangster),
  ili kruži oko bloka (ruta) — politika iz sloja 4.
- **Ukrcavanje**: auto ne kreće dok svi nisu seli (`PendingDrive`, već urađeno) i vrata
  zatvorena; pri naredbi prikazati **nameravani slot/stranu** na overlay-u (rešava "pogrešna
  traka" kao očekivanje igrača, ne samo kao bug).
- **U-okret**: samo na planiranim mestima — raskrsnica (konektor "nazad") ili sredina deonice
  gde rezervacija obe trake prođe; nikad "odmah ovde" bez rezervacije.

### Sloj 4 — vozač (policy nad istim primitivama)

Jedan `DriverProfile` po tipu; razlikuju se **samo pragovi i dozvole**, ne kod:

| | Traffic | Gangster (cold) | Gangster (Hot) | Police |
|---|---|---|---|---|
| Cruise | limit ivice | 14 m/s | 18 m/s | limit ×1.3 sirena |
| Strpljenje iza zaustavljenog | 3–5 s, pa obilazak | 0.8 s | 0 (odmah) | 0 |
| Dozvoljene trake za obilazak | svoja + kruna (samo oko parkiranog) | + suprotna kad je čisto | + suprotna sa manjom marginom; **vožnja krunom između dve trake** dokle traje prolaz | sve, kruna sa sirenom |
| Min. margina vremena za suprotni smer | 3 s | 2 s | 1.2 s | — (drugi mu staju) |
| U-okret | ne | na planiranom mestu | gde god rezervacija prođe | da |
| Semafor | poštuje | poštuje (cold) | prolazi crveno ako je kutija rezervisana | prolazi, prioritet |
| Strah (`DriverNerve`) | da | ne | ne | ne |
| Rikverc | u zastoju | u zastoju | u zastoju | u zastoju |

"Hot" i dalje **nikad ne ulazi u nerezervisan prostor** — brzina i agresija dolaze iz manjih
margina i nulte strpljivosti, ne iz ignorisanja drugih. Suprotni saobraćaj koči za auto koji
mu dolazi u traci (to već radi kroz "oncoming down OUR line"), ali kroz rezervaciju to sada
vidi ranije.

---

## 2. Šta zadržati, šta penzionisati

**Zadržati:** `PathBuilder`/`RoadPath` (profil brzine, primitive), `CarBody`, `WheelSpin`,
`DriverNerve`, `StreetAlarm`, `TrafficSignal`, `RouteToward`, `IRoadUser` (kao interfejs
koji čitaju pešaci/`WalkObstacles`), `DemoVehicle`-ov model liste po traci i konektora kroz
raskrsnicu — to postaje osnova, ne `CrewCar`-ova slobodna kinematika.

**Preraditi:** `IRoadModel` → `ILaneFrame` nad rutom; `CrewCar` taktike (`LaySwerve`,
`FindTurnX`, `HoldLine`, `BackOff`, `RelocateTurn`) → manevri sloja 2 sa rezervacijom;
`StreetTraffic.Car` → običan `DriverProfile.Traffic` na istom vozilu.

**Penzionisati:** skeniranje svih `Users` sa uzorcima putanje (`ClearAhead`, `StationaryOn`,
`GatherObstacles` kutije po celoj širini) — zamenjuju ga liste po traci + heš; teleport
saobraćaja na kraju ulice; `_rethink` tajmeri kao jedini mehanizam odluke.

---

## 3. Koraci (faze), svaka sa proverom

**Faza A — mreža i koordinate (bez vidljive promene ponašanja)**
1. `RoadEdge` dobija `Carriageway` (presek, trake, kerb, parking strip); CrewDemo gradi
   graf od 2 ivice + okreti umesto `StraightStreetModel`.
2. `ILaneFrame`: `Pose(edge,s,d)`; `PathBuilder` u (s,d); postojeće `DemoVehicle.Place`
   izraženo kroz isto.
3. Test: headless sim (kao scratchpad harness za pešake) — 100 auta, 10 min, random rute,
   samo pravo + skretanja; **0 preklapanja OBB, 0 zastoja >20 s** (ovo je baseline, mora proći
   već sa današnjim DemoVehicle-om preko novog frame-a).

**Faza B — rezervacija prostora**
4. `LaneOccupancy` po traci (sortirana, sa rezervacijama), `Claim/Release`, gap-acceptance.
5. Raskrsnica: konfliktna tabela konektora + `Claim` prozora; semafor kao filter.
6. Safety net: prostorni heš + OBB predikcija + brojač okidanja u `DemoPerfProbe`.
7. Test: isti sim + 30% auta nasumično menja trake i obilazi parkirane; brojač safety-neta
   mora biti 0; perf budžet: ≤0.3 ms za 200 auta.

**Faza C — manevri jednog vozila**
8. `LaneChange`, `Overtake` (obilazak zaustavljenog kroz krunu/suprotnu), `CrownRun`
   (vožnja između dve trake, rezervacija u obe), `UTurn`, `Reverse`, `PullIn/PullOut` — svi
   kroz `Claim`. Anti-zastoj pravilo.
9. Test-scenariji iz CrewDemo-a (skriptovani, bez Play-a): (i) auto stoji 1 m iza blokera
   bez prostora → rikverc → obilazak; (ii) dva auta licem u lice na istoj krunu → jedan
   popušta po prioritetu; (iii) obilazak kroz suprotnu traku sa nailazećim autom na 40 m →
   ili prođe sa marginom ili sačeka, nikad preklapanje; (iv) U-okret sa autom parkiranim na
   zamahu → premešta mesto; (v) parkiranje na strani klika, oba smera; (vi) Hot auto
   prolazi krunom između kolone u svojoj traci i kola u suprotnoj — prolazi kad ima
   ≥ pola auta + margina u obe, inače čeka/vraća se u traku, nikad preklapanje.

**Faza D — vozači i naredbe**
10. `DriverProfile` ×4; `DriverNerve` ostaje za Traffic; `Hot` kroz profil.
11. Parking slotovi + `ParkNear` po strani klika + overlay koji pokazuje ciljni slot;
    izlazak iz slota sa rezervacijom; ukrcavanje (`PendingDrive`) zadržano.
12. CrewDemo Play test po listi iz 9 + tri slučaja iz prethodnog feedback-a (zaglavljen auto,
    svi ulaze, klik na karoseriju).

**Faza E — grad**
13. `CrewCar` i policija u RoadDemo na istom grafu: rute Dijkstrom, drive-by kao ruta
    "pored mete → okret na sledećoj raskrsnici → nazad"; saobraćaj i gangsteri dele trake.
14. Sim: 200 auta + 4 crew auta + 2 policije, 15 min, **0 safety-net okidanja**, zastoj >20 s
    = 0, prosečna brzina saobraćaja ≥ 0.6 × limit (nema kolapsa od prerezervisanosti).

---

## 4. Otvoreno / pretpostavke

- Pretpostavka: ostajemo kinematički (bez Rigidbody/collider-a za kola); fizika bi donela
  nepredvidivost i skuplji debug, a vizuelno ne treba.
- Pretpostavka: "pogrešna traka" iz feedback-a = parkiranje preko puta klika; ako je misao
  bila vožnja suprotnom trakom posle obilaska, sloj 2 (obavezno vraćanje u svoju traku
  kao deo rezervisane putanje) to rešava.
- Odluka za kasnije: da li "Hot" sme kroz crveno — plan kaže da, ali samo sa rezervacijom
  kutije; lako se isključi u profilu.
- Pešaci: ostaju u `Bodies/Walkers` listama i upisuju se u trake; kad bude više od ~300
  ljudi, prebaciti i njih u isti prostorni heš.

---

## 5. Status implementacije (2026-08-19, kraj dana)

Urađeno u kodu (sve faze A–E), testirano u headless simulaciji (dotnet harness, stub UnityEngine),
**neprovereno u Unity Play-u**:

| Deo | Fajl | Šta |
|---|---|---|
| Mreža | `Assets/RoadDemo/LaneNet.cs`, `RoadDemoNet.cs` | `Carriageway` (kolovoz, (s,d) okvir, trake, ivičnjak, zauzeće `RoadOccupant` sa telom + claim-om), `Connector` (konektori kroz raskrsnicu: prava, kružni luk, U-okret na slepom kraju) + tabela konflikata, `RoadNode.Inside`, `LaneNet` (gradnja, `Adopt` starih ivica, `Prepare/Rebuild`, `Locate`, Dijkstra `RouteToward`, `AddStatic`), `IRoadUser`, `StaticRoadUser`, `LaneNet.Active` |
| Vozač | `DriverProfile.cs` | Traffic / Gangster / Hot / Police / Patrol / Lorry — samo pragovi i dozvole |
| Vozilo | `RoadCar.cs` | parametarsko kretanje (s,d), praćenje (`Follow`), rezervacije (`Claim`/`BandFree`), raskrsnica (`CanEnter`, tačka bez povratka, `BoxFollow` po telima), manevri: `Pass` (obilazak, i kroz suprotnu traku), `Crown` (vožnja krunom između traka — Hot), `UTurn` (luk u kolovozu), `Reverse` (rikverc pa ponovo), `Aside` (ustupanje), `PullIn/PullOut` (parking, izbor slobodnog mesta `ChooseKerbSpot`), standoff po prioritetu, jam/queue razlika (`InQueue`), `Halt`, slobodan teren (`GoFree`), pojas `RoadSpace` + brojač `BeltHits` |
| Ljuske | `DemoVehicle.cs` (saobraćaj grada, isti API), `CrewCar.cs` (isti API za DemoCrews/Overlay/PoliceDispatch: DriveTo/ParkNear/DriveBy/Stop/HardStop/…), `PolicePatrolCar.cs` (profili Patrol/Police), `StreetTraffic.cs` (spawner na mreži + statičke liste) |
| Scene | `CrewDemoBuilder.cs` (4 ulice = LaneNet sa ćoškovima i slepim krajevima, saobraćaj po celoj mreži, `StoodCar` → statički zauzimač), `RoadDemoBuilder.BuildGraph` (+`RoadDemoBuilder.Districts` weld) gradi kolovoze; `DemoCrews.Net` (LaneNet) | |
| Pojas | `RoadSpace.cs` | prostorni heš (12 m ćelije) |

Sim rezultati (harness u scratchpad-u sesije, scenariji: grid 100/120/200 auta sa semaforima i bez, bulevar,
CrewDemo prsten sa parkiranjem, olupina u traci za Traffic/Gangster/Hot, kruna između kolone i parkiranih,
čeoni obilazak uz nailazeće, zaglavljen iza olupine, U-okret sa autom na zamahu, standoff): **0 preklapanja
tela, 0 okidanja pojasa, bez zamrznutih vozila** na više seed-ova; 200 auta ≈ 0.5 ms/frejm (.NET Release).

Penzionisano (ostaje u repou jer ga koriste testovi druge sesije): `RoadPath.cs` (IRoadModel,
StraightStreetModel, PathBuilder) i `RoadNet.cs` — runtime ih više ne koristi.

Sledeće: Play test CrewDemo po listi iz faze C/D (ukrcavanje, klik, parkiranje po strani klika, drive-by
prolazi), podešavanje protoka raskrsnica u gustom saobraćaju (dugi redovi na semaforima), overlay za
`RoadCar.Why`/`BeltHits` u DemoPerfProbe.
